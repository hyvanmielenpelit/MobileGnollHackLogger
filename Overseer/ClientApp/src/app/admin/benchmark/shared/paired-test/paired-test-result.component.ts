import { NgTemplateOutlet } from '@angular/common';
import { ChangeDetectionStrategy, ChangeDetectorRef, Component, Input, OnChanges, inject } from '@angular/core';

import {
  BenchmarkPairComparisonDto,
  BenchmarkPairKind,
  BenchmarkPairedMeasureDto,
  BenchmarkPairedSuiteDetailDto,
  BenchmarkPairedTestDto
} from '../../../../services/admin-benchmark.service';
import {
  VerdictShape,
  findPair,
  formatDz,
  formatEffectInterval,
  formatEffectValue,
  formatP,
  formatSigned,
  kindDescription,
  measureTitle,
  notTestedReasonOf,
  verdictShape,
  verdictText
} from './paired-test-format';

/**
 * Where the open state of the result's Quality dimensions, Speed and Cost sections is kept, per
 * browser, as `{ version: 1, open: { dimensions, speed, cost } }`. Read and written in `try/catch`.
 */
export const PAIRED_RESULT_SECTIONS_STORAGE_KEY = 'overseer.benchmark.pairedTest.sections';

/** The result's collapsible sections. */
export type PairedResultSection = 'dimensions' | 'speed' | 'cost';

const SECTIONS: readonly PairedResultSection[] = ['dimensions', 'speed', 'cost'];

/** One measure of the pair, ready to render. */
export interface PairedResultRow {
  readonly measure: BenchmarkPairedMeasureDto;
  readonly pair: BenchmarkPairedTestDto | null;
  readonly title: string;
  readonly label: string;
  readonly effect: string;
  readonly interval: string;
  readonly dz: string;
  readonly p: string;
  readonly adjustedP: string;
  readonly verdict: string;
  readonly shape: VerdictShape;
  readonly notTested: string | null;
  readonly pairedItems: number | null;
  readonly unpairedNote: string | null;
  readonly method: string;
  readonly note: string | null;
  readonly caption: string | null;
  readonly adjustmentNote: string;
  readonly suites: readonly PairedSuiteRow[];
}

/** One suite of a battery Intelligence test. */
export interface PairedSuiteRow {
  readonly suite: BenchmarkPairedSuiteDetailDto;
  readonly difference: string;
  readonly p: string;
  readonly adjustedP: string;
}

/** Everything the template renders, computed once per input change. */
interface PairedResultView {
  readonly treatmentLabel: string;
  readonly baselineLabel: string;
  readonly kind: BenchmarkPairKind | null;
  readonly kindText: string;
  readonly changedKeys: readonly string[];
  readonly caveat: string | null;
  readonly primary: PairedResultRow | null;
  readonly dimensions: readonly PairedResultRow[];
  readonly speed: PairedResultRow | null;
  readonly cost: PairedResultRow | null;
  readonly sectionRows: Readonly<Record<PairedResultSection, readonly PairedResultRow[]>>;
}

function readStoredSections(): Record<PairedResultSection, boolean> {
  const fallback: Record<PairedResultSection, boolean> = { dimensions: true, speed: true, cost: true };
  try {
    const raw = localStorage.getItem(PAIRED_RESULT_SECTIONS_STORAGE_KEY);
    const stored: unknown = raw === null ? null : JSON.parse(raw);
    const open = (stored as { open?: unknown } | null)?.open;
    if (!open || typeof open !== 'object') {
      return fallback;
    }
    const result = { ...fallback };
    for (const section of SECTIONS) {
      const value = (open as Record<string, unknown>)[section];
      if (typeof value === 'boolean') {
        result[section] = value;
      }
    }
    return result;
  } catch {
    return fallback;
  }
}

/** One measure's row for the pair between `baselineKey` and `treatmentKey` (the first pair when both are null). */
export function pairedResultRow(
  measure: BenchmarkPairedMeasureDto,
  baselineKey: string | null,
  treatmentKey: string | null
): PairedResultRow {
  const pair = findPair(measure, baselineKey, treatmentKey);
  const notTested = notTestedReasonOf(measure, pair);
  const unpaired = pair ? [
    pair.unpairedItems > 0 ? `${pair.unpairedItems} unpaired` : '',
    pair.revisionMismatched > 0 ? `${pair.revisionMismatched} with a different question revision` : ''
  ].filter(part => part !== '') : [];
  return {
    measure,
    pair,
    title: measureTitle(measure),
    label: measure.label,
    effect: pair ? formatEffectValue(pair) : '—',
    interval: pair ? formatEffectInterval(pair) : '',
    dz: formatDz(pair?.dz),
    p: formatP(pair?.pValue),
    adjustedP: formatP(pair?.adjustedPValue),
    verdict: verdictText(pair),
    shape: verdictShape(pair, measure),
    notTested,
    pairedItems: pair?.pairedItems ?? null,
    unpairedNote: unpaired.length > 0 ? `Left out: ${unpaired.join(', ')}.` : null,
    method: pair?.method ?? '',
    note: pair?.note ?? null,
    caption: measure.caption ?? null,
    adjustmentNote: measure.adjustmentNote,
    suites: (pair?.suites ?? []).map(suite => ({
      suite,
      difference: formatSigned(suite.weightedDifference, 1),
      p: formatP(suite.wilcoxonPValue),
      adjustedP: formatP(suite.holmAdjustedPValue)
    }))
  };
}

/**
 * One pair's paired test on every measure: the Intelligence Index headline with its interval and p,
 * then the quality dimensions, speed and cost in a non-exclusive stack of disclosures. Each verdict is
 * a word and a shape (a filled dot for an established difference, a ring otherwise, a dash when not
 * tested); color only repeats it.
 *
 * Fed either a whole pair comparison (`comparison`, the run and battery reports) or a family's
 * measures with the pair's keys and labels (the wizard's details panel). Separate inputs override
 * the comparison's fields.
 */
@Component({
  selector: 'app-paired-test-result',
  standalone: true,
  imports: [NgTemplateOutlet],
  templateUrl: './paired-test-result.component.html',
  styleUrls: ['./paired-test-result.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class PairedTestResultComponent implements OnChanges {
  private readonly cdr = inject(ChangeDetectorRef);

  /** A two-run or two-result comparison; supplies every field the inputs below leave unset. */
  @Input() comparison: BenchmarkPairComparisonDto | null = null;

  /** The measures to show; each measure's pair is chosen by the keys below. */
  @Input() measures: readonly BenchmarkPairedMeasureDto[] | null = null;
  @Input() baselineKey: string | null = null;
  @Input() treatmentKey: string | null = null;
  @Input() baselineLabel: string | null = null;
  @Input() treatmentLabel: string | null = null;

  /** Shown whenever a side rests on one run. */
  @Input() singleRunCaveat: string | null = null;

  /** Shows each measure's adjusted p and family note; a single pair is never adjusted. */
  @Input() showAdjustment = false;

  /** Shows the Intelligence headline; a host that shows the primary test itself turns it off. */
  @Input() showPrimary = true;

  /** Shows the kind sentence and, for a verification, the changed keys. */
  @Input() showKind = true;

  /** Prefixes every element id; unique in the document. */
  @Input() idPrefix = 'ptr';

  view: PairedResultView | null = null;

  sectionOpen: Record<PairedResultSection, boolean> = readStoredSections();

  ngOnChanges(): void {
    this.view = this.buildView();
  }

  private buildView(): PairedResultView | null {
    const comparison = this.comparison;
    const measures = this.measures ?? comparison?.measures ?? null;
    if (!measures) {
      return null;
    }
    const baselineKey = this.baselineKey ?? comparison?.baselineKey ?? null;
    const treatmentKey = this.treatmentKey ?? comparison?.treatmentKey ?? null;
    const rows = measures.map(measure => pairedResultRow(measure, baselineKey, treatmentKey));
    const kind = comparison?.kind ?? null;
    const dimensions = rows.filter(row => row.measure.category === 'QualityDimension');
    const speed = rows.find(row => row.measure.category === 'Speed') ?? null;
    const cost = rows.find(row => row.measure.category === 'Cost') ?? null;
    return {
      treatmentLabel: this.treatmentLabel ?? comparison?.treatmentLabel ?? treatmentKey ?? '',
      baselineLabel: this.baselineLabel ?? comparison?.baselineLabel ?? baselineKey ?? '',
      kind,
      kindText: comparison ? kindDescription(kind, comparison.kindLabel, comparison.explanation) : '',
      changedKeys: comparison?.changedKeys ?? [],
      caveat: this.singleRunCaveat ?? comparison?.singleRunCaveat ?? null,
      primary: rows.find(row => row.measure.category === 'Intelligence') ?? null,
      dimensions,
      speed,
      cost,
      sectionRows: { dimensions, speed: speed ? [speed] : [], cost: cost ? [cost] : [] }
    };
  }

  /** Follows the native `toggle`, so a key press and a bound `open` are tracked too. */
  onSectionToggle(section: PairedResultSection, event: Event): void {
    const open = (event.target as HTMLDetailsElement).open;
    if (open === this.sectionOpen[section]) {
      return;
    }
    this.sectionOpen = { ...this.sectionOpen, [section]: open };
    try {
      localStorage.setItem(PAIRED_RESULT_SECTIONS_STORAGE_KEY, JSON.stringify({ version: 1, open: this.sectionOpen }));
    } catch {
      // Private mode or blocked storage: the sections still open and close for this session.
    }
    this.cdr.markForCheck();
  }

  readonly sectionTitles: Readonly<Record<PairedResultSection, string>> = {
    dimensions: 'Quality dimensions',
    speed: 'Speed',
    cost: 'Cost'
  };

  readonly sections = SECTIONS;
}
