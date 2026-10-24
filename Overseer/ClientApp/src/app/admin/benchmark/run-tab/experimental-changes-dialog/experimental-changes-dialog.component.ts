import {
  ChangeDetectionStrategy,
  ChangeDetectorRef,
  Component,
  ElementRef,
  EventEmitter,
  Input,
  OnDestroy,
  OnInit,
  Output,
  ViewChild,
  inject
} from '@angular/core';
import { Observable, Subscription } from 'rxjs';
import {
  AdminBenchmarkService,
  BenchmarkExperimentalChangeCatalogDto,
  BenchmarkExperimentalChangeDto,
  BenchmarkExperimentalChangeKind,
  BenchmarkExperimentalChangeRecentSelectionDto,
  BenchmarkExperimentalChangeRunDto,
  BenchmarkExperimentalChangeRunsDto,
  BenchmarkInformationCondition
} from '../../../../services/admin-benchmark.service';
import { TextDiffComponent } from '../../../../shared/text-diff/text-diff.component';
import { DiffResult, diffLines } from '../../../../utils/text-diff.util';
import { downloadBlobFile } from '../../../../utils/download.util';
import { ensureOverlayPolyfills, refreshAnchorPositioning } from '../../../../utils/polyfills.util';
import { formatStatusLabel } from '../../benchmark-run-format';
import {
  CHANGE_KIND_LABELS,
  ExperimentalChangeEditorRequest,
  ExperimentalChangeSaved,
  MissingMention,
  applicabilityLine,
  applicabilityOf,
  changeCountText,
  changeDateText,
  changeErrorText,
  changeKinds,
  changeSpec,
  conditionName,
  conflictBetween,
  conflictReason,
  experimentLine,
  isSelectable,
  mentionLine,
  missingMentions,
  ordinalCompare,
  parseChangeSpec,
  runCountText,
  selectionLabel,
  stampLength
} from '../experimental-changes';

/** The `localStorage` key of the dialog's remembered view. */
export const EXPERIMENTAL_CHANGES_DIALOG_VIEW_KEY = 'overseer.benchmark.experimentalChangesDialog.view';

/** How long the dialog waits after a tick before it asks for the runs of the selection. */
export const EXPERIMENTAL_CHANGES_RUNS_DEBOUNCE_MS = 250;

/** The search field appears above this many selectable changes. */
const FILTER_THRESHOLD = 8;

/** The Used before buttons the dialog shows at most. */
const RECENT_SELECTIONS_MAX = 6;

interface ExperimentalChangesDialogView {
  version: 1;
  tipsOpen: boolean;
}

/** The two presentations of a replaced description's change. */
export type ExperimentalChangeDiffView = 'side' | 'diff';

/** The file saving the dialog does, replaceable in specs. */
export const experimentalChangesIo = {
  saveBlob: (fileName: string, blob: Blob): void => downloadBlobFile(fileName, blob)
};

/** One parameter of an experimental tool, from its JSON schema. */
export interface ExperimentalToolParameter {
  name: string;
  type: string;
  required: boolean;
  description: string;
}

/** An experimental tool's parameters from its JSON schema; null when the schema does not parse. */
export function toolParametersOf(schemaJson: string): ExperimentalToolParameter[] | null {
  let schema: unknown;
  try {
    schema = JSON.parse(schemaJson);
  } catch {
    return null;
  }
  if (!schema || typeof schema !== 'object') return null;
  const record = schema as { properties?: unknown; required?: unknown };
  const properties = record.properties && typeof record.properties === 'object'
    ? record.properties as Record<string, unknown>
    : {};
  const required = Array.isArray(record.required) ? record.required.filter((r): r is string => typeof r === 'string') : [];
  return Object.entries(properties).map(([name, value]) => {
    const property = (value && typeof value === 'object' ? value : {}) as { type?: unknown; enum?: unknown; description?: unknown };
    const types = Array.isArray(property.type) ? property.type.map(String) : property.type != null ? [String(property.type)] : [];
    let type = types.join(' or ') || 'any';
    if (Array.isArray(property.enum) && property.enum.length > 0) {
      type += ` (one of ${property.enum.map(String).join(', ')})`;
    }
    return {
      name,
      type,
      required: required.includes(name),
      description: typeof property.description === 'string' ? property.description : ''
    };
  });
}

/** One effect of a change as Show exactly what the AI gets lists it. */
export interface ChangeEffectView {
  kind: 'newTool' | 'toolOutput' | 'add' | 'replace' | 'instructions';
  /** Unique in the dialog; the block's DOM id prefix. */
  key: string;
  label: string;
  /** The tool offered or edited; null for a switch or instructions. */
  tool: string | null;
  /** A new tool's description, a switch's line, an added or replacing text, or the instructions. */
  text: string | null;
  /** The live description an addition follows. */
  productionText: string | null;
  /** A replacement against the live description. */
  diff: DiffResult | null;
  /** A new tool's parameters; null when it has no schema or the schema does not parse. */
  parameters: ExperimentalToolParameter[] | null;
  parametersUnreadable: boolean;
}

/** One change as its card shows it. */
export interface ChangeCardView {
  change: BenchmarkExperimentalChangeDto;
  /** The id made safe for DOM ids. */
  domId: string;
  kinds: { kind: BenchmarkExperimentalChangeKind; label: string }[];
  /** *Written in Overseer · edited 24 Oct 2026*; null for a built-in change. */
  sourceLine: string | null;
  effects: ChangeEffectView[];
}

function readView(): ExperimentalChangesDialogView | null {
  try {
    const raw = localStorage.getItem(EXPERIMENTAL_CHANGES_DIALOG_VIEW_KEY);
    if (!raw) return null;
    const parsed = JSON.parse(raw) as Partial<ExperimentalChangesDialogView> | null;
    return parsed?.version === 1 && typeof parsed.tipsOpen === 'boolean' ? { version: 1, tipsOpen: parsed.tipsOpen } : null;
  } catch {
    return null;
  }
}

function writeView(view: ExperimentalChangesDialogView): void {
  try {
    localStorage.setItem(EXPERIMENTAL_CHANGES_DIALOG_VIEW_KEY, JSON.stringify(view));
  } catch {
    // Storage may be unavailable or full; the dialog then opens with the tips open next time.
  }
}

function domIdOf(id: string): string {
  return id.replace(/[^A-Za-z0-9_-]/g, '-');
}

function effectsOf(change: BenchmarkExperimentalChangeDto, domId: string): ChangeEffectView[] {
  const blank = { tool: null, text: null, productionText: null, diff: null, parameters: null, parametersUnreadable: false };
  const effects: ChangeEffectView[] = [];
  const tool = change.experimentalTool;
  if (tool) {
    const schema = tool.parameterSchemaJson ?? null;
    const parameters = schema ? toolParametersOf(schema) : null;
    effects.push({
      ...blank, kind: 'newTool', key: `xc-fx-${domId}-tool`, label: `New tool: ${tool.name}`, tool: tool.name,
      text: tool.description ?? null, parameters, parametersUnreadable: schema != null && parameters === null
    });
  }
  if (change.experiment) {
    effects.push({
      ...blank, kind: 'toolOutput', key: `xc-fx-${domId}-output`, label: 'Tool output',
      text: experimentLine(change.experiment) ?? `Turns on the output switch ${change.experiment}.`
    });
  }
  for (const edit of change.descriptionEdits ?? []) {
    const key = `xc-fx-${domId}-${edit.mode}-${domIdOf(edit.tool)}`;
    if (edit.mode === 'replace') {
      effects.push({
        ...blank, kind: 'replace', key, label: `Replaces the ${edit.tool} description`, tool: edit.tool, text: edit.text,
        productionText: edit.productionText ?? null,
        diff: edit.productionText != null ? diffLines(edit.productionText, edit.text) : null
      });
    } else {
      effects.push({
        ...blank, kind: 'add', key, label: `Added to the end of the ${edit.tool} description`, tool: edit.tool, text: edit.text,
        productionText: edit.productionText ?? null
      });
    }
  }
  if (change.instructions?.trim()) {
    effects.push({
      ...blank, kind: 'instructions', key: `xc-fx-${domId}-instructions`, label: 'Added to the end of the AI\'s instructions',
      text: change.instructions
    });
  }
  return effects;
}

function cardViewOf(change: BenchmarkExperimentalChangeDto): ChangeCardView {
  const domId = domIdOf(change.id);
  let sourceLine: string | null = null;
  if (change.source === 'database') {
    const date = changeDateText(change.updatedAtUtc ?? change.createdAtUtc);
    sourceLine = date ? `Written in Overseer · edited ${date}` : 'Written in Overseer';
  }
  return {
    change,
    domId,
    kinds: changeKinds(change).map(kind => ({ kind, label: CHANGE_KIND_LABELS[kind] })),
    sourceLine,
    effects: effectsOf(change, domId)
  };
}

function cardOrder(a: ChangeCardView, b: ChangeCardView): number {
  const source = (a.change.source === 'builtIn' ? 0 : 1) - (b.change.source === 'builtIn' ? 0 : 1);
  return source !== 0 ? source : a.change.title.localeCompare(b.change.title) || ordinalCompare(a.change.id, b.change.id);
}

/**
 * The Experimental Changes dialog: tick the test-only changes a run should try, see what each gives
 * the AI and what the run will be compared with, and manage the changes written in Overseer. The host
 * opens it with the current spec and receives the chosen spec from `chosen`; `closed` fires however it
 * closes, so the host can return focus. The editor is a sibling dialog the host opens on
 * `editRequested`; after a save the host reloads the catalog and calls `onChangeSaved`.
 */
@Component({
  selector: 'app-experimental-changes-dialog',
  standalone: true,
  imports: [TextDiffComponent],
  templateUrl: './experimental-changes-dialog.component.html',
  styleUrls: ['./experimental-changes-dialog.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class ExperimentalChangesDialogComponent implements OnInit, OnDestroy {
  private readonly benchmarkService = inject(AdminBenchmarkService);
  private readonly cdr = inject(ChangeDetectorRef);

  @ViewChild('xcDialog', { static: true }) dialog?: ElementRef<HTMLDialogElement>;

  private catalogValue: BenchmarkExperimentalChangeCatalogDto | null = null;

  /** The catalog; null until it arrives, or when it failed to load. */
  @Input()
  set catalog(value: BenchmarkExperimentalChangeCatalogDto | null) {
    this.catalogValue = value;
    this.cards = (value?.changes ?? []).map(cardViewOf).sort(cardOrder);
    // A change retired or deleted meanwhile cannot stay ticked.
    for (const id of [...this.selected]) {
      const change = this.find(id);
      if (!change || !isSelectable(change)) this.selected.delete(id);
    }
    this.cdr.markForCheck();
  }
  get catalog(): BenchmarkExperimentalChangeCatalogDto | null {
    return this.catalogValue;
  }

  /** Why the catalog could not be loaded; null when it loaded or is loading. */
  @Input() catalogError: string | null = null;

  /** The launcher's Tool Access choice, which decides how much of each change applies. */
  @Input() informationCondition: BenchmarkInformationCondition = 'production';

  /** The chosen spec on Use this; null for no changes. */
  @Output() readonly chosen = new EventEmitter<string | null>();

  /** The dialog closed, by Use this, Cancel, Escape or a run link. */
  @Output() readonly closed = new EventEmitter<void>();

  /** A run whose report should open. */
  @Output() readonly openRunReport = new EventEmitter<number>();

  /** New change, Edit… or Copy as new version…: the host opens the editor. */
  @Output() readonly editRequested = new EventEmitter<ExperimentalChangeEditorRequest>();

  /** A change was retired, restored or deleted: the host reloads the catalog. */
  @Output() readonly catalogChanged = new EventEmitter<void>();

  readonly docPath = 'docs/overseer/ai-benchmark-conditions-and-variants.md § 6';
  readonly separator = '\n\n';

  /** Every change of the catalog as a card, built in first, then by title. */
  cards: ChangeCardView[] = [];
  /** The ticked ids. */
  readonly selected = new Set<string>();
  filterText = '';
  tipsOpen = true;
  /** The change whose inline delete confirmation shows. */
  deleteConfirmId: string | null = null;
  /** The change whose More actions popover is open. */
  moreOpenId: string | null = null;
  /** The change a request is running for. */
  busyId: string | null = null;
  /** The last action's outcome, announced politely. */
  actionStatus = '';
  /** The last action's failure. */
  actionError: string | null = null;

  /** The runs of the current selection as they load. */
  runs: { spec: string; status: 'loading' | 'loaded' | 'error'; data?: BenchmarkExperimentalChangeRunsDto; message?: string } | null = null;

  private readonly diffViews = new Map<string, ExperimentalChangeDiffView>();
  private runsTimer: ReturnType<typeof setTimeout> | null = null;
  private runsSubscription: Subscription | null = null;
  private readonly actionSubscriptions: Subscription[] = [];
  /** The control the editor returns focus to when it closes without a save. */
  private returnFocus: HTMLElement | null = null;

  ngOnInit(): void {
    // The close and More actions buttons carry interest-triggered tooltips; the popovers anchor.
    ensureOverlayPolyfills();
  }

  ngOnDestroy(): void {
    this.cancelRuns();
    for (const subscription of this.actionSubscriptions) subscription.unsubscribe();
    this.actionSubscriptions.length = 0;
  }

  // --- Opening and closing ---

  /** Shows the dialog with the spec's selectable changes ticked and focuses the first ticked one. */
  open(spec: string | null): void {
    const ids = parseChangeSpec(spec) ?? [];
    this.selected.clear();
    for (const id of ids) {
      const change = this.find(id);
      if (change && isSelectable(change)) this.selected.add(id);
    }
    this.filterText = '';
    this.deleteConfirmId = null;
    this.moreOpenId = null;
    this.actionStatus = '';
    this.actionError = null;
    this.diffViews.clear();
    this.tipsOpen = readView()?.tipsOpen ?? true;
    this.runs = null;
    this.scheduleRuns();

    this.cdr.markForCheck();
    this.cdr.detectChanges();
    const dialog = this.dialog?.nativeElement;
    if (dialog && !dialog.open) {
      dialog.showModal();
    }
    const first = dialog?.querySelector<HTMLInputElement>('input.xc-check:checked')
      ?? dialog?.querySelector<HTMLInputElement>('input.xc-check:not(:disabled)');
    first?.focus();
  }

  /** Cancel and the close button: closes without a choice. */
  cancel(): void {
    this.dialog?.nativeElement.close();
  }

  /** The dialog's close event, however it closed. */
  onDialogClosed(): void {
    this.cancelRuns();
    this.closed.emit();
  }

  /** Use this: emits the canonical spec and closes; refused while the server would refuse it. */
  useThis(): void {
    if (this.useRefusal) return;
    this.chosen.emit(this.spec);
    this.dialog?.nativeElement.close();
  }

  /** A run link: closes the dialog and asks the host for that run's report. */
  openRun(run: BenchmarkExperimentalChangeRunDto): void {
    this.dialog?.nativeElement.close();
    this.openRunReport.emit(run.id);
  }

  onTipsToggle(event: Event): void {
    this.tipsOpen = (event.target as HTMLDetailsElement).open;
    writeView({ version: 1, tipsOpen: this.tipsOpen });
  }

  // --- The selection ---

  toggle(change: BenchmarkExperimentalChangeDto, event: Event): void {
    const input = event.target as HTMLInputElement;
    if (input.checked) {
      if (!isSelectable(change) || this.disabledReason(change)) {
        input.checked = false;
        return;
      }
      this.selected.add(change.id);
    } else {
      this.selected.delete(change.id);
    }
    this.selectionChanged();
  }

  /** Clear all: unticks everything and moves focus to the first change. */
  clearAll(): void {
    this.selected.clear();
    this.selectionChanged();
    this.cdr.detectChanges();
    this.dialog?.nativeElement.querySelector<HTMLInputElement>('input.xc-check:not(:disabled)')?.focus();
  }

  /** A Used before button: its selection replaces the current one. */
  applyRecent(selection: BenchmarkExperimentalChangeRecentSelectionDto): void {
    this.selected.clear();
    for (const id of selection.changeIds) {
      const change = this.find(id);
      if (change && isSelectable(change)) this.selected.add(id);
    }
    this.selectionChanged();
  }

  /** The mention warning's Add button: ticks the mentioned change. */
  addMissing(mention: MissingMention): void {
    if (!isSelectable(mention.missing) || this.disabledReason(mention.missing)) return;
    this.selected.add(mention.missing.id);
    this.selectionChanged();
  }

  isChecked(change: BenchmarkExperimentalChangeDto): boolean {
    return this.selected.has(change.id);
  }

  onFilterInput(event: Event): void {
    this.filterText = (event.target as HTMLInputElement).value;
  }

  /** Escape with text clears the filter and keeps the dialog open. */
  onFilterKeydown(event: KeyboardEvent): void {
    if (event.key !== 'Escape' || this.filterText === '') return;
    event.preventDefault();
    event.stopPropagation();
    this.filterText = '';
    (event.target as HTMLInputElement).value = '';
  }

  diffViewOf(key: string): ExperimentalChangeDiffView {
    return this.diffViews.get(key) ?? 'diff';
  }

  setDiffView(key: string, view: ExperimentalChangeDiffView): void {
    this.diffViews.set(key, view);
  }

  // --- More actions ---

  moreId(view: ChangeCardView): string {
    return `xc-more-${view.domId}`;
  }

  onMoreToggle(view: ChangeCardView, event: Event): void {
    const open = (event as ToggleEvent).newState === 'open';
    const popover = this.dialog?.nativeElement.querySelector<HTMLElement>(`[id="${this.moreId(view)}"]`);
    if (open) {
      this.moreOpenId = view.change.id;
      this.cdr.markForCheck();
      refreshAnchorPositioning();
      popover?.querySelector<HTMLElement>('.gh-action-popover-item:not([aria-disabled="true"])')?.focus();
      return;
    }
    if (this.moreOpenId === view.change.id) {
      this.moreOpenId = null;
      this.cdr.markForCheck();
    }
    const active = document.activeElement;
    if (!active || active === document.body || !!popover?.contains(active)) {
      this.moreTrigger(view)?.focus();
    }
  }

  /** Escape closes the popover only; the dialog stays open. */
  onMoreKeydown(view: ChangeCardView, event: KeyboardEvent): void {
    if (event.key !== 'Escape') return;
    event.preventDefault();
    event.stopPropagation();
    this.hideMore(view);
    this.moreTrigger(view)?.focus();
  }

  /** New change: the host opens the editor; focus comes back here if it closes without a save. */
  newChange(): void {
    this.returnFocus = this.dialog?.nativeElement.querySelector<HTMLElement>('#xcNewChange') ?? null;
    this.editRequested.emit({ mode: 'new', change: null });
  }

  editChange(view: ChangeCardView): void {
    this.hideMore(view);
    this.returnFocus = this.moreTrigger(view);
    this.editRequested.emit({ mode: 'edit', change: view.change });
  }

  copyChange(view: ChangeCardView): void {
    this.hideMore(view);
    this.returnFocus = this.moreTrigger(view);
    this.editRequested.emit({ mode: 'copy', change: view.change });
  }

  downloadFolder(view: ChangeCardView): void {
    this.hideMore(view);
    const id = view.change.id;
    this.startAction(id, this.benchmarkService.downloadExperimentalChangeFolder(id), blob => {
      experimentalChangesIo.saveBlob(`${id}.zip`, blob);
      this.actionStatus = `Downloaded ${id}.zip.`;
    }, `${view.change.title} could not be downloaded.`);
    this.moreTrigger(view)?.focus();
  }

  retire(view: ChangeCardView): void {
    this.hideMore(view);
    const change = view.change;
    this.startAction(change.id, this.benchmarkService.setExperimentalChangeRetired(change.id, true), () => {
      this.selected.delete(change.id);
      this.selectionChanged();
      this.actionStatus = `Retired ${change.title}. It is listed under Show retired.`;
      this.catalogChanged.emit();
      this.focusAfterRemoval();
    }, `${change.title} could not be retired.`);
  }

  restore(change: BenchmarkExperimentalChangeDto): void {
    this.startAction(change.id, this.benchmarkService.setExperimentalChangeRetired(change.id, false), () => {
      this.actionStatus = `Restored ${change.title}.`;
      this.catalogChanged.emit();
    }, `${change.title} could not be restored.`);
  }

  /** Delete…: the card shows its inline confirmation, with focus on Keep. */
  askDelete(view: ChangeCardView): void {
    this.hideMore(view);
    this.deleteConfirmId = view.change.id;
    this.cdr.markForCheck();
    this.cdr.detectChanges();
    this.dialog?.nativeElement.querySelector<HTMLElement>(`#xc-keep-${view.domId}`)?.focus();
  }

  keepChange(view: ChangeCardView): void {
    this.deleteConfirmId = null;
    this.cdr.markForCheck();
    this.cdr.detectChanges();
    this.moreTrigger(view)?.focus();
  }

  confirmDelete(view: ChangeCardView): void {
    const change = view.change;
    this.startAction(change.id, this.benchmarkService.deleteExperimentalChange(change.id), () => {
      this.deleteConfirmId = null;
      this.selected.delete(change.id);
      this.selectionChanged();
      this.actionStatus = `Deleted ${change.title}.`;
      this.catalogChanged.emit();
      this.focusAfterRemoval();
    }, `${change.title} could not be deleted.`);
  }

  // --- The editor's outcome, called by the host ---

  /**
   * After a save and the catalog reload: a new change is ticked and its checkbox focused; after an
   * edit, focus returns to the card's More actions.
   */
  onChangeSaved(saved: ExperimentalChangeSaved): void {
    const change = this.find(saved.change.id) ?? saved.change;
    this.actionStatus = `Saved ${change.title}.`;
    this.actionError = null;
    if (saved.created && isSelectable(change) && !this.disabledReason(change)) {
      this.selected.add(change.id);
      this.selectionChanged();
    }
    this.returnFocus = null;
    this.cdr.markForCheck();
    this.cdr.detectChanges();
    const domId = domIdOf(change.id);
    const target = saved.created
      ? this.dialog?.nativeElement.querySelector<HTMLElement>(`#xc-check-${domId}`)
      : this.dialog?.nativeElement.querySelector<HTMLElement>(`[id="xc-more-${domId}-trigger"]`);
    target?.focus();
  }

  /** The editor closed without a save: focus returns to the control that opened it. */
  onEditorClosed(): void {
    const target = this.returnFocus;
    this.returnFocus = null;
    if (target?.isConnected) target.focus();
  }

  // --- What the template reads ---

  get changes(): BenchmarkExperimentalChangeDto[] {
    return this.catalogValue?.changes ?? [];
  }

  /** The cards a new run may tick, then the invalid ones; retired changes are listed apart. */
  get listCards(): ChangeCardView[] {
    const active = this.cards.filter(v => !v.change.retired);
    const valid = active.filter(v => v.change.isValid);
    const invalid = active.filter(v => !v.change.isValid);
    const filter = this.showFilter ? this.filterText.trim().toLowerCase() : '';
    const matching = filter === ''
      ? valid
      : valid.filter(v => `${v.change.title} ${v.change.summary} ${v.change.id}`.toLowerCase().includes(filter)
        || this.selected.has(v.change.id));
    return [...matching, ...invalid];
  }

  get retiredCards(): ChangeCardView[] {
    return this.cards.filter(v => v.change.retired);
  }

  get selectableCount(): number {
    return this.changes.filter(isSelectable).length;
  }

  get showFilter(): boolean {
    return this.selectableCount > FILTER_THRESHOLD;
  }

  get selectedChanges(): BenchmarkExperimentalChangeDto[] {
    return [...this.selected].sort(ordinalCompare)
      .map(id => this.find(id))
      .filter((c): c is BenchmarkExperimentalChangeDto => !!c);
  }

  /** The canonical spec of the ticks; null for none. */
  get spec(): string | null {
    return changeSpec(this.selected);
  }

  get conditionText(): string {
    return conditionName(this.informationCondition);
  }

  /** *2 changes*, for This run. */
  get selectedCountText(): string {
    return changeCountText(this.selected.size);
  }

  /** The polite footer status: *2 changes selected*, or no changes. */
  get liveStatus(): string {
    const count = this.selected.size;
    return count === 0 ? 'No changes — same as the live chat' : `${changeCountText(count)} selected`;
  }

  /** The Used before buttons: distinct recent selections whose changes are all offered. */
  get recentSelections(): { selection: BenchmarkExperimentalChangeRecentSelectionDto; label: string; pressed: boolean }[] {
    const current = this.spec;
    return (this.catalogValue?.recentSelections ?? [])
      .filter(s => s.changeIds.length > 0 && s.changeIds.every(id => {
        const change = this.find(id);
        return !!change && isSelectable(change);
      }))
      .slice(0, RECENT_SELECTIONS_MAX)
      .map(selection => ({
        selection,
        label: `${selectionLabel(selection.changeIds, this.changes)} · ${runCountText(selection.runCount)}`,
        pressed: changeSpec(selection.changeIds) === current
      }));
  }

  /** The mention warnings of the selection. */
  get mentions(): MissingMention[] {
    return missingMentions(this.selected, this.changes);
  }

  mentionText(mention: MissingMention): string {
    return mentionLine(mention);
  }

  /** Whether the mentioned change can be ticked now. */
  canAdd(mention: MissingMention): boolean {
    return isSelectable(mention.missing) && !this.disabledReason(mention.missing);
  }

  /** The ticked changes that do not fully apply under the Tool Access choice, each with its line. */
  get conditionRows(): { title: string; line: string }[] {
    return this.selectedChanges
      .map(change => ({ title: change.title, line: applicabilityLine(change, this.informationCondition) }))
      .filter((row): row is { title: string; line: string } => row.line !== null);
  }

  /** The stamp form of the selection; the hash is computed when the run starts. */
  get stampText(): string | null {
    const spec = this.spec;
    return spec ? `${spec}:<12-character selection hash>` : null;
  }

  /** Why Use this waits: the server would refuse the selection. Null when it would not. */
  get useRefusal(): string | null {
    if (!this.catalogValue) {
      return this.catalogError
        ? `The experimental changes could not be loaded: ${this.catalogError}`
        : 'The experimental changes are still loading.';
    }
    const max = this.catalogValue.maxStampLength;
    if (max > 0 && stampLength(this.spec) > max) {
      return `Too many changes for one run: the run record holds ${max} characters. Choose fewer.`;
    }
    const selected = this.selectedChanges;
    for (const change of selected) {
      if (applicabilityOf(change, this.informationCondition) === 'none') {
        return `${change.title} has no effect under ${this.conditionText}. Untick it, or choose another Tool Access.`;
      }
    }
    for (let i = 0; i < selected.length; i++) {
      for (let j = i + 1; j < selected.length; j++) {
        const reason = conflictBetween(selected[i], selected[j]);
        if (reason) return `${selected[i].title} and ${selected[j].title} cannot run together: ${reason}.`;
      }
    }
    return null;
  }

  /** Why a change that is not ticked cannot be ticked now; null when it can. */
  disabledReason(change: BenchmarkExperimentalChangeDto): string | null {
    if (!change.isValid) return `Not available: ${change.invalidReason || 'the change is invalid.'}`;
    if (this.selected.has(change.id)) return null;
    if (applicabilityOf(change, this.informationCondition) === 'none') {
      return applicabilityLine(change, this.informationCondition);
    }
    return conflictReason(change, this.selected, this.changes);
  }

  /** The line under a card's summary: why it cannot be ticked, else what does not reach the model. */
  cardLine(change: BenchmarkExperimentalChangeDto): string | null {
    return this.disabledReason(change) ?? applicabilityLine(change, this.informationCondition);
  }

  /** *Used in 3 runs*. */
  usedText(change: BenchmarkExperimentalChangeDto): string {
    return change.runCount === 0 ? 'Not used by a run yet' : `Used in ${runCountText(change.runCount)}`;
  }

  /** Each More actions item that applies to the change; a missing action is absent. */
  canEdit(change: BenchmarkExperimentalChangeDto): boolean {
    return change.source === 'database' && change.canEdit;
  }

  canCopy(change: BenchmarkExperimentalChangeDto): boolean {
    return change.canCopy;
  }

  canRetire(change: BenchmarkExperimentalChangeDto): boolean {
    return change.source === 'database' && !change.retired;
  }

  canDelete(change: BenchmarkExperimentalChangeDto): boolean {
    return change.source === 'database' && change.canDelete;
  }

  /** The runs line of the Compare it with callout. */
  get runsSummary(): string | null {
    const data = this.runs?.status === 'loaded' && this.runs.spec === this.spec ? this.runs.data : null;
    if (!data) return null;
    if (data.runCount === 0) return 'No run has used exactly this selection yet.';
    let text = data.runs.length < data.runCount ? `${runCountText(data.runCount)} in all; the newest ${data.runs.length} are listed.` : '';
    if (data.olderHashRunCount > 0) {
      text += `${text ? ' ' : ''}${data.olderHashRunCount === 1 ? '1 of them' : `${data.olderHashRunCount} of them`} used an older version of the text.`;
    }
    return text || null;
  }

  get runsList(): BenchmarkExperimentalChangeRunDto[] {
    return this.runs?.status === 'loaded' && this.runs.spec === this.spec ? this.runs.data?.runs ?? [] : [];
  }

  get runsLoading(): boolean {
    return this.runs?.status === 'loading' && this.runs.spec === this.spec;
  }

  get runsError(): string | null {
    return this.runs?.status === 'error' && this.runs.spec === this.spec ? this.runs.message ?? null : null;
  }

  runLabel(run: BenchmarkExperimentalChangeRunDto): string {
    const parts = [`Open the report of run #${run.id}`, changeDateText(run.startedAtUtc), run.suiteName, run.modelDisplayName, formatStatusLabel(run.status)];
    return parts.filter(p => !!p).join(', ');
  }

  sourceText(change: BenchmarkExperimentalChangeDto): string {
    return change.source === 'builtIn' ? 'Built in' : 'Written in Overseer';
  }

  folderText(change: BenchmarkExperimentalChangeDto): string {
    return change.source === 'builtIn' ? `Overseer/BenchmarkChanges/${change.id}/` : 'database';
  }

  // --- Internals ---

  private find(id: string): BenchmarkExperimentalChangeDto | undefined {
    return this.catalogValue?.changes.find(c => c.id === id);
  }

  private selectionChanged(): void {
    this.scheduleRuns();
    this.cdr.markForCheck();
  }

  private moreTrigger(view: ChangeCardView): HTMLElement | null {
    return this.dialog?.nativeElement.querySelector<HTMLElement>(`[id="${this.moreId(view)}-trigger"]`) ?? null;
  }

  private hideMore(view: ChangeCardView): void {
    const popover = this.dialog?.nativeElement.querySelector<HTMLElement>(`[id="${this.moreId(view)}"]`);
    try {
      popover?.hidePopover();
    } catch {
      // Already hidden.
    }
    if (this.moreOpenId === view.change.id) this.moreOpenId = null;
  }

  /** A retired or deleted card has gone: focus moves to New change. */
  private focusAfterRemoval(): void {
    this.cdr.markForCheck();
    this.cdr.detectChanges();
    this.dialog?.nativeElement.querySelector<HTMLElement>('#xcNewChange')?.focus();
  }

  private startAction<T>(id: string, request: Observable<T>, done: (value: T) => void, failure: string): void {
    if (this.busyId) return;
    this.busyId = id;
    this.actionError = null;
    this.actionStatus = '';
    this.cdr.markForCheck();
    this.actionSubscriptions.push(request.subscribe({
      next: value => {
        this.busyId = null;
        done(value);
        this.cdr.markForCheck();
      },
      error: err => {
        this.busyId = null;
        this.actionError = changeErrorText(err, failure);
        this.cdr.markForCheck();
      }
    }));
  }

  /** Asks for the runs of the current selection once the ticks have settled. */
  private scheduleRuns(): void {
    if (this.runsTimer != null) {
      clearTimeout(this.runsTimer);
      this.runsTimer = null;
    }
    const spec = this.spec;
    if (!spec) {
      this.runsSubscription?.unsubscribe();
      this.runsSubscription = null;
      this.runs = null;
      return;
    }
    if (this.runs?.spec === spec && this.runs.status !== 'error') return;
    this.runsTimer = setTimeout(() => {
      this.runsTimer = null;
      this.runsSubscription?.unsubscribe();
      this.runs = { spec, status: 'loading' };
      this.cdr.markForCheck();
      this.runsSubscription = this.benchmarkService.getExperimentalChangeRuns(spec).subscribe({
        next: data => {
          this.runs = { spec, status: 'loaded', data };
          this.cdr.markForCheck();
        },
        error: err => {
          this.runs = { spec, status: 'error', message: changeErrorText(err, 'The runs of this selection could not be loaded.') };
          this.cdr.markForCheck();
        }
      });
    }, EXPERIMENTAL_CHANGES_RUNS_DEBOUNCE_MS);
  }

  private cancelRuns(): void {
    if (this.runsTimer != null) {
      clearTimeout(this.runsTimer);
      this.runsTimer = null;
    }
    this.runsSubscription?.unsubscribe();
    this.runsSubscription = null;
  }
}
