import { NgTemplateOutlet } from '@angular/common';
import {
  ChangeDetectionStrategy,
  ChangeDetectorRef,
  Component,
  Input,
  OnChanges,
  OnDestroy,
  OnInit,
  SimpleChanges,
  inject
} from '@angular/core';
import { Observable, Subject, Subscription, catchError, map, of, switchMap } from 'rxjs';

import {
  AdminBenchmarkService,
  BenchmarkPairedComparisonDto,
  BenchmarkPairedComparisonMode,
  BenchmarkPairedComparisonRequest,
  BenchmarkPairedMeasureDto,
  BenchmarkPairedTestDto,
  BenchmarkReportPackPricingBasis
} from '../../../services/admin-benchmark.service';
import { showReasoningBadge } from '../../../utils/model-badge-format.util';
import { ensureOverlayPolyfills } from '../../../utils/polyfills.util';
import {
  ForestMarks,
  VerdictShape,
  effectHeader,
  forestScale,
  formatDz,
  formatEffectInterval,
  formatEffectValue,
  formatP,
  measureEffectKind,
  measureTitle,
  notTestedReasonOf,
  pairKey,
  pairedErrorText,
  verdictShape,
  verdictText
} from '../shared/paired-test/paired-test-format';
import { PairedTestResultComponent } from '../shared/paired-test/paired-test-result.component';
import { saveFigureBlob } from './figure-export';
import { BenchmarkModelComparisonDto, BenchmarkModelComparisonEntryDto } from './model-comparison.models';
import {
  PairedExportFormat,
  pairedExportFormat,
  pairedFamilyLine,
  pairedTestsCsv,
  pairedTestsFilename,
  pairedTestsMarkdown,
  pairedTestsMediaType
} from './paired-tests-export';
import type { TableFileFormat } from './table-export';

/** All pairs is offered up to this many comparable entries, until the server says otherwise. */
export const PAIRED_ALL_PAIRS_LIMIT = 12;

/**
 * Where the open state of the view's measure sections is kept, per browser, as `{ version: 1, open:
 * { intelligence, dimensions, speed, cost } }`. Read and written in `try/catch`; all open by default.
 */
export const PAIRED_VIEW_SECTIONS_STORAGE_KEY = 'overseer.modelComparison.pairedSections';

/** The view's measure sections, primary first. */
export type PairedViewSection = 'intelligence' | 'dimensions' | 'speed' | 'cost';

const VIEW_SECTIONS: readonly { readonly id: PairedViewSection; readonly title: string }[] = [
  { id: 'intelligence', title: 'Intelligence' },
  { id: 'dimensions', title: 'Quality dimensions' },
  { id: 'speed', title: 'Speed' },
  { id: 'cost', title: 'Cost' }
];

/** Why a single comparable entry cannot be compared, and where its result lives. */
export const PAIRED_NEEDS_TWO_TEXT =
  'Comparing needs a second comparable entry. A single model\'s result — its Intelligence Index with '
  + 'the interval, and its stand-alone AI documents — is in its run report, or its battery run report.';

/** One entry as a row or column names it. */
export interface PairedEntryLabel {
  readonly key: string;
  readonly name: string;
  readonly thinkingLevel: string | null;
  readonly reasoningMode: string | null;
}

/** One pair of one measure, ready to render. */
export interface PairedRowView {
  readonly id: string;
  readonly pair: BenchmarkPairedTestDto;
  readonly treatment: PairedEntryLabel;
  readonly baseline: PairedEntryLabel;
  readonly pairedItems: number;
  readonly effect: string;
  readonly interval: string;
  readonly marks: ForestMarks;
  readonly dz: string;
  readonly p: string;
  readonly adjustedP: string;
  readonly verdict: string;
  readonly shape: VerdictShape;
  readonly notTested: string | null;
}

/** The lower triangle of one measure: row model against column model. */
export interface PairedMatrixView {
  readonly rows: readonly PairedEntryLabel[];
  readonly columns: readonly PairedEntryLabel[];
  /** `cells[r][c]`, null above the diagonal. */
  readonly cells: readonly (readonly (PairedRowView | null)[])[];
}

/** One measure's family, ready to render. */
export interface PairedMeasureView {
  readonly measure: BenchmarkPairedMeasureDto;
  readonly id: string;
  readonly title: string;
  readonly effectHeader: string;
  readonly zero: number;
  readonly rows: readonly PairedRowView[];
  readonly matrix: PairedMatrixView | null;
}

/** The pair whose details are open. */
export interface PairedSelection {
  readonly baselineKey: string;
  readonly treatmentKey: string;
}

function readStoredSections(): Record<PairedViewSection, boolean> {
  const fallback: Record<PairedViewSection, boolean> = { intelligence: true, dimensions: true, speed: true, cost: true };
  try {
    const raw = localStorage.getItem(PAIRED_VIEW_SECTIONS_STORAGE_KEY);
    const stored: unknown = raw === null ? null : JSON.parse(raw);
    const open = (stored as { open?: unknown } | null)?.open;
    if (!open || typeof open !== 'object') {
      return fallback;
    }
    const result = { ...fallback };
    for (const section of VIEW_SECTIONS) {
      const value = (open as Record<string, unknown>)[section.id];
      if (typeof value === 'boolean') {
        result[section.id] = value;
      }
    }
    return result;
  } catch {
    return fallback;
  }
}

function sectionOf(measure: BenchmarkPairedMeasureDto): PairedViewSection {
  switch (measure.category) {
    case 'Intelligence': return 'intelligence';
    case 'QualityDimension': return 'dimensions';
    case 'Speed': return 'speed';
    default: return 'cost';
  }
}

/** A DOM-safe fragment of an entry key (`run:12` → `run-12`). */
function domKey(key: string): string {
  return key.replace(/[^A-Za-z0-9_-]/g, '-');
}

/** The outcome of one request, tagged with the request it answers. */
type PairedFetchOutcome =
  | { readonly ok: true; readonly dto: BenchmarkPairedComparisonDto }
  | { readonly ok: false; readonly error: string };

/**
 * The Model Comparison wizard's **Paired tests** view: is one model better than another on the same
 * questions, and on which measures. Each measure is its own family — Intelligence (the primary test),
 * each quality dimension, speed and cost — tested against a reference (k − 1 tests, the default) or
 * across all pairs (up to twelve entries), Holm-adjusted; two entries make one unadjusted test.
 *
 * It fetches when it is first active, again whenever the comparison, the pricing basis, the mode or
 * the reference changes while it is active, and on Recompute; a newer request cancels the one in
 * flight. Excluded entries never take part.
 */
@Component({
  selector: 'app-paired-tests-view',
  standalone: true,
  imports: [NgTemplateOutlet, PairedTestResultComponent],
  templateUrl: './paired-tests-view.component.html',
  styleUrls: ['./paired-tests-view.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class PairedTestsViewComponent implements OnInit, OnChanges, OnDestroy {
  private readonly cdr = inject(ChangeDetectorRef);
  private readonly benchmarkService = inject(AdminBenchmarkService);

  /** The wizard's computed comparison: its entries' sources are the request's. */
  @Input() comparison: BenchmarkModelComparisonDto | null = null;

  /** The comparison's pricing basis. */
  @Input() pricingBasis: string = 'Current';

  /** Step 2's Highlight; the first comparable one is the default reference. */
  @Input() highlightedKeys: readonly string[] = [];

  /** The Download tab's table format, which picks Markdown or CSV for Download. */
  @Input() tableFormat: TableFileFormat = 'xlsx';

  /** The view is on screen; nothing is fetched while it is not. */
  @Input() active = false;

  readonly sections = VIEW_SECTIONS;
  readonly showReasoningBadge = showReasoningBadge;
  readonly needsTwoText = PAIRED_NEEDS_TWO_TEXT;

  /** The admin's mode; with two entries it is not offered and not sent. */
  mode: BenchmarkPairedComparisonMode = 'Reference';

  /** The admin's reference, or null for the default: the first highlighted entry, else the server's choice. */
  chosenReference: string | null = null;

  result: BenchmarkPairedComparisonDto | null = null;
  loading = false;
  error: string | null = null;

  /** The pair whose details panel is open. */
  selected: PairedSelection | null = null;

  /** The outcome of the last copy or download, in the view's one live region. */
  exportStatus = '';

  sectionOpen: Record<PairedViewSection, boolean> = readStoredSections();

  /** Built from the result whenever it changes. */
  measureViews: readonly PairedMeasureView[] = [];
  measureViewsBySection: Readonly<Record<PairedViewSection, readonly PairedMeasureView[]>> =
    { intelligence: [], dimensions: [], speed: [], cost: [] };

  private comparableSource: BenchmarkModelComparisonDto | null = null;
  private comparableList: readonly BenchmarkModelComparisonEntryDto[] = [];
  private lastSignature: string | null = null;
  private readonly requests = new Subject<{ readonly request: BenchmarkPairedComparisonRequest } | null>();
  private requestSub: Subscription | null = null;

  ngOnInit(): void {
    ensureOverlayPolyfills();
    this.requestSub = this.requests.pipe(
      switchMap((item): Observable<PairedFetchOutcome | null> => item === null
        ? of(null)
        : this.benchmarkService.getPairedComparison(item.request).pipe(
          map((dto): PairedFetchOutcome => ({ ok: true, dto })),
          catchError((err: unknown) => of<PairedFetchOutcome>({
            ok: false, error: pairedErrorText(err, 'The paired tests could not be computed.')
          }))
        ))
    ).subscribe(outcome => this.onOutcome(outcome));
    this.ensureFetched(false);
  }

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['comparison'] && this.selected && !this.isComparable(this.selected.baselineKey)) {
      this.selected = null;
    }
    if (this.requestSub) {
      this.ensureFetched(false);
    }
  }

  ngOnDestroy(): void {
    this.requestSub?.unsubscribe();
    this.requestSub = null;
  }

  // --- The entries ---

  /** The comparison's entries that take part: every one that is not excluded. */
  get comparableEntries(): readonly BenchmarkModelComparisonEntryDto[] {
    if (this.comparableSource !== this.comparison) {
      this.comparableSource = this.comparison;
      this.comparableList = (this.comparison?.entries ?? []).filter(entry => !entry.excluded);
    }
    return this.comparableList;
  }

  get comparableCount(): number {
    return this.comparableEntries.length;
  }

  private isComparable(key: string | null | undefined): boolean {
    return key != null && this.comparableEntries.some(entry => entry.key === key);
  }

  /** The most entries All pairs is offered for: the server's figure once known. */
  get allPairsLimit(): number {
    return this.result?.allPairsLimit ?? PAIRED_ALL_PAIRS_LIMIT;
  }

  /** The Mode control: shown with three or more entries. */
  get showMode(): boolean {
    return this.comparableCount > 2;
  }

  get allPairsUnavailable(): boolean {
    return this.comparableCount > this.allPairsLimit;
  }

  get allPairsUnavailableReason(): string {
    return `All pairs is offered for at most ${this.allPairsLimit} comparable models; this comparison has `
      + `${this.comparableCount}. Test against a reference instead.`;
  }

  /** The mode the request carries: Reference with two entries, or while All pairs is over the limit. */
  get effectiveMode(): BenchmarkPairedComparisonMode {
    if (!this.showMode || (this.mode === 'AllPairs' && this.allPairsUnavailable)) {
      return 'Reference';
    }
    return this.mode;
  }

  /** The reference the request names: the admin's, else the first highlighted comparable entry, else none. */
  get requestedReference(): string | null {
    if (this.isComparable(this.chosenReference)) {
      return this.chosenReference;
    }
    return this.highlightedKeys.find(key => this.isComparable(key)) ?? null;
  }

  /** What the Reference select shows: the request's, else the reference the server chose. */
  get referenceValue(): string {
    return this.requestedReference ?? this.result?.referenceKey ?? '';
  }

  labelOf = (key: string): string => {
    const entry = this.comparison?.entries.find(candidate => candidate.key === key);
    return entry ? (entry.label || entry.modelDisplayName) : key;
  };

  entryLabel(key: string): PairedEntryLabel {
    const entry = this.comparison?.entries.find(candidate => candidate.key === key);
    return {
      key,
      name: entry ? (entry.modelDisplayName || entry.label) : key,
      thinkingLevel: entry?.thinkingLevel ?? null,
      reasoningMode: entry?.reasoningMode ?? null
    };
  }

  // --- Fetching ---

  private buildRequest(recompute: boolean): BenchmarkPairedComparisonRequest | null {
    const comparison = this.comparison;
    if (!comparison) {
      return null;
    }
    const idsOf = (kind: string): number[] => {
      const ids: number[] = [];
      for (const entry of comparison.entries) {
        if (entry.sourceKind.toLowerCase() === kind && !ids.includes(entry.sourceId)) {
          ids.push(entry.sourceId);
        }
      }
      return ids;
    };
    return {
      runIds: idsOf('run'),
      groupIds: idsOf('group'),
      batteryRunIds: idsOf('battery'),
      pricingBasis: this.pricingBasis === 'AsRun' ? BenchmarkReportPackPricingBasis.AsRun : BenchmarkReportPackPricingBasis.Current,
      mode: this.effectiveMode,
      referenceKey: this.effectiveMode === 'Reference' ? this.requestedReference : null,
      ...(recompute ? { recompute: true } : {})
    };
  }

  /**
   * Requests the paired tests while the view is active and two entries are comparable: always on
   * Recompute, otherwise only when the request differs from the last one sent.
   */
  private ensureFetched(recompute: boolean): void {
    if (!this.active || this.comparableCount < 2) {
      return;
    }
    const request = this.buildRequest(recompute);
    if (!request) {
      return;
    }
    const signature = JSON.stringify([
      request.runIds, request.groupIds, request.batteryRunIds, request.pricingBasis, request.mode,
      request.referenceKey ?? null, this.comparison?.computedAtUtc ?? ''
    ]);
    if (!recompute && signature === this.lastSignature) {
      return;
    }
    this.lastSignature = signature;
    this.loading = true;
    this.error = null;
    this.exportStatus = '';
    this.cdr.markForCheck();
    this.requests.next({ request });
  }

  private onOutcome(outcome: PairedFetchOutcome | null): void {
    if (outcome === null) {
      return;
    }
    this.loading = false;
    if (outcome.ok) {
      this.result = outcome.dto;
      this.error = null;
      if (this.selected && !this.hasPair(this.selected)) {
        this.selected = null;
      }
      this.buildViews();
    } else {
      this.error = outcome.error;
      // A failed request is retried by the next change or by Recompute, never by repeating this one.
      this.lastSignature = null;
    }
    this.cdr.markForCheck();
  }

  recompute(): void {
    this.ensureFetched(true);
  }

  // --- The toolbar ---

  selectMode(mode: BenchmarkPairedComparisonMode): void {
    if (mode === 'AllPairs' && this.allPairsUnavailable) {
      return;
    }
    if (mode !== this.mode) {
      this.mode = mode;
      this.selected = null;
    }
    this.cdr.markForCheck();
    this.ensureFetched(false);
  }

  /** Left/Right move and wrap, Home/End jump to the ends; focus follows selection, onto a refusing tab too. */
  onModeKeydown(event: KeyboardEvent, index: number): void {
    const modes: BenchmarkPairedComparisonMode[] = ['Reference', 'AllPairs'];
    const targets: Record<string, number> = { ArrowRight: index + 1, ArrowLeft: index - 1, Home: 0, End: modes.length - 1 };
    const requested = targets[event.key];
    if (requested === undefined) {
      return;
    }
    event.preventDefault();
    const next = modes[(requested + modes.length) % modes.length];
    this.selectMode(next);
    this.cdr.detectChanges();
    document.getElementById(`mc-paired-mode-${next}`)?.focus();
  }

  onReferenceChange(event: Event): void {
    const value = (event.target as HTMLSelectElement).value;
    this.chosenReference = value === '' ? null : value;
    this.selected = null;
    this.cdr.markForCheck();
    this.ensureFetched(false);
  }

  // --- The body ---

  /** The line that says how many tests each family holds and how they are adjusted. */
  get familyLine(): string {
    return this.result ? pairedFamilyLine(this.result, this.labelOf) : '';
  }

  /** The status line: the request in flight, else the last export's outcome. */
  get statusText(): string {
    return this.loading ? 'Computing paired tests…' : this.exportStatus;
  }

  private buildViews(): void {
    const dto = this.result;
    const views: PairedMeasureView[] = [];
    const bySection: Record<PairedViewSection, PairedMeasureView[]> = { intelligence: [], dimensions: [], speed: [], cost: [] };
    if (dto) {
      const labels = new Map(dto.entryKeys.map(key => [key, this.entryLabel(key)]));
      const labelFor = (key: string): PairedEntryLabel => labels.get(key) ?? this.entryLabel(key);
      for (const measure of dto.measures) {
        const scale = forestScale(measure.pairs, measureEffectKind(measure));
        const id = `mc-paired-${measure.measure.toLowerCase().replace(/[^a-z0-9]+/g, '-')}`;
        const rows = measure.pairs.map((pair): PairedRowView => ({
          id: `${id}-${domKey(pair.baselineKey)}-${domKey(pair.treatmentKey)}`,
          pair,
          treatment: labelFor(pair.treatmentKey),
          baseline: labelFor(pair.baselineKey),
          pairedItems: pair.pairedItems,
          effect: formatEffectValue(pair),
          interval: formatEffectInterval(pair),
          marks: scale.marks(pair),
          dz: formatDz(pair.dz),
          p: formatP(pair.pValue),
          adjustedP: formatP(pair.adjustedPValue),
          verdict: verdictText(pair),
          shape: verdictShape(pair, measure),
          notTested: notTestedReasonOf(measure, pair)
        }));
        let matrix: PairedMatrixView | null = null;
        if (dto.mode === 'AllPairs' && dto.entryKeys.length > 2) {
          const byKey = new Map(rows.map(row => [pairKey(row.pair.baselineKey, row.pair.treatmentKey), row]));
          const keys = dto.entryKeys;
          matrix = {
            rows: keys.slice(1).map(labelFor),
            columns: keys.slice(0, -1).map(labelFor),
            cells: keys.slice(1).map((treatmentKey, r) => keys.slice(0, -1).map((baselineKey, c) =>
              c <= r ? byKey.get(pairKey(baselineKey, treatmentKey)) ?? null : null))
          };
        }
        const view: PairedMeasureView = {
          measure, id, title: measureTitle(measure), effectHeader: effectHeader(measure), zero: scale.zero, rows, matrix
        };
        views.push(view);
        bySection[sectionOf(measure)].push(view);
      }
    }
    this.measureViews = views;
    this.measureViewsBySection = bySection;
  }

  onSectionToggle(section: PairedViewSection, event: Event): void {
    const open = (event.target as HTMLDetailsElement).open;
    if (open === this.sectionOpen[section]) {
      return;
    }
    this.sectionOpen = { ...this.sectionOpen, [section]: open };
    try {
      localStorage.setItem(PAIRED_VIEW_SECTIONS_STORAGE_KEY, JSON.stringify({ version: 1, open: this.sectionOpen }));
    } catch {
      // Private mode or blocked storage: the sections still open and close for this session.
    }
    this.cdr.markForCheck();
  }

  // --- The details panel ---

  private hasPair(selection: PairedSelection): boolean {
    return (this.result?.measures ?? []).some(measure => measure.pairs.some(pair =>
      pair.baselineKey === selection.baselineKey && pair.treatmentKey === selection.treatmentKey));
  }

  isSelected(row: PairedRowView): boolean {
    return this.selected?.baselineKey === row.pair.baselineKey && this.selected?.treatmentKey === row.pair.treatmentKey;
  }

  /** Opens one pair's full row below the sections and moves focus to its heading; a second press closes it. */
  openDetails(row: PairedRowView): void {
    if (this.isSelected(row)) {
      this.closeDetails();
      return;
    }
    this.selected = { baselineKey: row.pair.baselineKey, treatmentKey: row.pair.treatmentKey };
    this.cdr.detectChanges();
    document.getElementById('mc-paired-detail-title')?.focus();
  }

  closeDetails(): void {
    this.selected = null;
    this.cdr.markForCheck();
  }

  get selectedTreatmentLabel(): string {
    return this.selected ? this.entryLabel(this.selected.treatmentKey).name : '';
  }

  get selectedBaselineLabel(): string {
    return this.selected ? this.entryLabel(this.selected.baselineKey).name : '';
  }

  /** A matrix cell's whole accessible name. */
  cellName(view: PairedMeasureView, row: PairedRowView): string {
    const result = row.notTested
      ? `not tested: ${row.notTested}`
      : `${row.effect}, adjusted p ${row.adjustedP}, ${row.verdict}`;
    return `${row.treatment.name} against ${row.baseline.name}, ${view.measure.label}: ${result}. Show details`;
  }

  // --- Copy and download ---

  get canExport(): boolean {
    return this.result !== null && !this.loading;
  }

  get downloadFormat(): PairedExportFormat {
    return pairedExportFormat(this.tableFormat);
  }

  get downloadName(): string {
    return this.downloadFormat === 'md' ? 'Download the paired tests as Markdown' : 'Download the paired tests as CSV';
  }

  async copyMarkdown(): Promise<void> {
    const dto = this.result;
    if (!dto || !this.canExport) {
      return;
    }
    const text = pairedTestsMarkdown(dto, this.labelOf);
    const clipboard = navigator.clipboard as Clipboard | undefined;
    if (!clipboard || typeof clipboard.writeText !== 'function') {
      this.exportStatus = 'This browser cannot copy text to the clipboard — download the paired tests instead.';
      this.cdr.markForCheck();
      return;
    }
    try {
      await clipboard.writeText(text);
      this.exportStatus = 'Copied the paired tests as Markdown.';
    } catch {
      this.exportStatus = 'The clipboard write was refused.';
    }
    this.cdr.markForCheck();
  }

  download(): void {
    const dto = this.result;
    if (!dto || !this.canExport) {
      return;
    }
    const format = this.downloadFormat;
    const text = format === 'md' ? pairedTestsMarkdown(dto, this.labelOf) : pairedTestsCsv(dto, this.labelOf);
    const filename = pairedTestsFilename(format);
    saveFigureBlob(new Blob([text], { type: pairedTestsMediaType(format) }), filename);
    this.exportStatus = `Paired tests saved as ${filename}.`;
    this.cdr.markForCheck();
  }
}
