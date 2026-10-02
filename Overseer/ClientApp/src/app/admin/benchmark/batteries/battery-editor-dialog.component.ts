import {
  ChangeDetectionStrategy,
  ChangeDetectorRef,
  Component,
  ElementRef,
  EventEmitter,
  OnDestroy,
  OnInit,
  Output,
  ViewChild,
  inject
} from '@angular/core';
import { Subscription } from 'rxjs';

import {
  AdminBenchmarkService,
  BenchmarkBatteryDto,
  BenchmarkSuiteDto,
  CreateBenchmarkBatteryRequest
} from '../../../services/admin-benchmark.service';
import { ensureOverlayPolyfills } from '../../../utils/polyfills.util';
import { InfoTipComponent } from '../../../shared/info-tip/info-tip.component';
import {
  ReorderableListComponent,
  ReorderableListItem
} from '../../../shared/reorderable-list/reorderable-list.component';
import {
  BATTERY_SCHEME_OPTIONS,
  BatterySchemeOption,
  BatteryWeightingSchemeKey,
  BenchmarkBatterySuiteMass,
  DEFAULT_BATTERY_SCHEME,
  formatNumber,
  formatPercent,
  httpErrorText,
  isValidCustomWeight,
  previewBatteryWeights,
  questionWeight
} from './battery.models';

/** One suite in the editor, checked or not; the list order is the run order of the checked ones. */
interface EditorRow {
  readonly suiteId: number;
  readonly name: string;
  checked: boolean;
  customWeight: number | null;
  readonly questionCount: number;
  readonly assessedQuestionCount: number;
  readonly fullyAssessed: boolean;
}

/** One row of the live weight preview: a checked suite. */
export interface BatteryPreviewRow {
  readonly suiteId: number;
  readonly name: string;
  readonly questionCount: number;
  readonly assessedQuestionCount: number;
  readonly fullyAssessed: boolean;
  /** Null while the suite's questions are loading or could not be loaded. */
  readonly difficultyMass: number | null;
  readonly massError: string | null;
  readonly customWeight: number | null;
  /** The normalized weight under each scheme; null where that scheme's weights are undefined. */
  readonly weights: Readonly<Record<BatteryWeightingSchemeKey, number | null>>;
}

const KEY_PREFIX = 'suite-';

/**
 * Creates or edits a battery: its name, description, suites in run order, weighting scheme and,
 * under Custom, the declared weights. The weight preview is computed here from each suite's
 * question count and difficulty mass as the selection changes, under every scheme at once.
 *
 * A suite's difficulty mass comes from the battery being edited when it already holds the suite,
 * else from that suite's questions, loaded once per opening when it is first checked.
 */
@Component({
  selector: 'app-battery-editor-dialog',
  standalone: true,
  imports: [InfoTipComponent, ReorderableListComponent],
  templateUrl: './battery-editor-dialog.component.html',
  styleUrls: ['./battery-editor-dialog.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class BatteryEditorDialogComponent implements OnInit, OnDestroy {
  private benchmarkService = inject(AdminBenchmarkService);
  private cdr = inject(ChangeDetectorRef);

  /** The created or updated battery, as the server returned it. */
  @Output() saved = new EventEmitter<BenchmarkBatteryDto>();
  /** Every close, saved or not. */
  @Output() closed = new EventEmitter<void>();

  @ViewChild('editorDialog') dialog?: ElementRef<HTMLDialogElement>;
  @ViewChild('editorHeading') heading?: ElementRef<HTMLElement>;

  readonly schemeOptions: readonly BatterySchemeOption[] = BATTERY_SCHEME_OPTIONS;
  readonly formatPercent = formatPercent;
  readonly formatNumber = formatNumber;

  editing: BenchmarkBatteryDto | null = null;
  name = '';
  description = '';
  scheme: BatteryWeightingSchemeKey = DEFAULT_BATTERY_SCHEME;
  rows: EditorRow[] = [];
  items: ReorderableListItem[] = [];
  previewRows: BatteryPreviewRow[] = [];
  /** Suites of the edited battery that have been deleted; saving drops them. */
  deletedSuiteNames: string[] = [];

  saving = false;
  saveError: string | null = null;
  attemptedSave = false;
  isOpen = false;

  private masses = new Map<number, number>();
  private massErrors = new Map<number, string>();
  private massRequests = new Map<number, Subscription>();

  ngOnInit(): void {
    ensureOverlayPolyfills();
  }

  ngOnDestroy(): void {
    this.cancelMassRequests();
  }

  /** Opens the editor on `battery`, or on a new battery when it is null, over every suite in `suites`. */
  open(battery: BenchmarkBatteryDto | null, suites: readonly BenchmarkSuiteDto[]): void {
    this.cancelMassRequests();
    this.masses.clear();
    this.massErrors.clear();
    this.editing = battery;
    this.name = battery?.name ?? '';
    this.description = battery?.description ?? '';
    this.scheme = (battery?.weightingScheme as BatteryWeightingSchemeKey | undefined) ?? DEFAULT_BATTERY_SCHEME;
    this.saveError = null;
    this.attemptedSave = false;
    this.saving = false;

    const byId = new Map(suites.map(s => [s.id, s]));
    const rows: EditorRow[] = [];
    const taken = new Set<number>();
    this.deletedSuiteNames = [];

    for (const suite of [...(battery?.suites ?? [])].sort((a, b) => a.index - b.index)) {
      if (suite.suiteId == null || suite.deleted) {
        this.deletedSuiteNames.push(suite.suiteName);
        continue;
      }
      const current = byId.get(suite.suiteId);
      taken.add(suite.suiteId);
      this.masses.set(suite.suiteId, suite.difficultyMass);
      rows.push({
        suiteId: suite.suiteId,
        name: current?.name ?? suite.suiteName,
        checked: true,
        customWeight: suite.customWeight ?? null,
        questionCount: suite.questionCount,
        assessedQuestionCount: suite.assessedQuestionCount,
        fullyAssessed: suite.difficultyFullyAssessed
      });
    }

    const others = suites
      .filter(s => !taken.has(s.id))
      .sort((a, b) => a.name.localeCompare(b.name, undefined, { numeric: true, sensitivity: 'base' }));
    for (const suite of others) {
      rows.push({
        suiteId: suite.id,
        name: suite.name,
        checked: false,
        customWeight: null,
        questionCount: suite.questionCount,
        assessedQuestionCount: suite.assessedQuestionCount,
        fullyAssessed: suite.difficultyFullyAssessed
      });
    }

    this.rows = rows;
    this.refresh();

    this.isOpen = true;
    const dialog = this.dialog?.nativeElement;
    if (dialog && !dialog.open) {
      dialog.showModal();
    }
    this.cdr.detectChanges();
    this.heading?.nativeElement.focus();
  }

  close(): void {
    this.cancelMassRequests();
    this.isOpen = false;
    const dialog = this.dialog?.nativeElement;
    if (dialog?.open) {
      dialog.close();
    }
    this.closed.emit();
    this.cdr.markForCheck();
  }

  onCancel(event: Event): void {
    event.preventDefault();
    if (!this.saving) {
      this.close();
    }
  }

  // --- Form events -----------------------------------------------------------------------------

  onNameInput(event: Event): void {
    this.name = (event.target as HTMLInputElement).value;
  }

  onDescriptionInput(event: Event): void {
    this.description = (event.target as HTMLTextAreaElement).value;
  }

  onSchemeChange(event: Event): void {
    this.scheme = (event.target as HTMLInputElement | HTMLSelectElement).value as BatteryWeightingSchemeKey;
    if (this.scheme === 'Custom') {
      for (const row of this.rows) {
        if (row.checked && row.customWeight == null) {
          row.customWeight = 1;
        }
      }
    }
    this.refresh();
  }

  onOrderChange(keys: string[]): void {
    const byKey = new Map(this.rows.map(r => [KEY_PREFIX + r.suiteId, r]));
    this.rows = keys.map(k => byKey.get(k)).filter((r): r is EditorRow => !!r);
    this.refresh();
  }

  onCheckedChange(change: { key: string; checked: boolean }): void {
    const row = this.rows.find(r => KEY_PREFIX + r.suiteId === change.key);
    if (!row) {
      return;
    }
    row.checked = change.checked;
    if (row.checked && this.scheme === 'Custom' && row.customWeight == null) {
      row.customWeight = 1;
    }
    this.refresh();
  }

  onCustomWeightInput(suiteId: number, event: Event): void {
    const row = this.rows.find(r => r.suiteId === suiteId);
    if (!row) {
      return;
    }
    const text = (event.target as HTMLInputElement).value.trim();
    const value = text === '' ? null : Number(text);
    row.customWeight = value === null || Number.isNaN(value) ? null : value;
    this.refresh();
  }

  // --- Derived state ---------------------------------------------------------------------------

  get checkedRows(): EditorRow[] {
    return this.rows.filter(r => r.checked);
  }

  get nameError(): string | null {
    return this.name.trim() === '' ? 'Enter a name for the battery.' : null;
  }

  get suiteCountError(): string | null {
    return this.checkedRows.length < 2 ? 'Select at least two suites.' : null;
  }

  get customWeightError(): string | null {
    if (this.scheme !== 'Custom') {
      return null;
    }
    return this.checkedRows.every(r => isValidCustomWeight(r.customWeight))
      ? null
      : 'Every selected suite needs a custom weight above zero.';
  }

  get canSave(): boolean {
    return !this.saving && !this.nameError && !this.suiteCountError && !this.customWeightError;
  }

  get schemeLabel(): string {
    return this.schemeOptions.find(o => o.value === this.scheme)?.label ?? this.scheme;
  }

  /** The schemes the preview shows besides the chosen one, muted. */
  get otherSchemes(): BatterySchemeOption[] {
    return this.schemeOptions.filter(o => o.value !== this.scheme && o.value !== 'Custom');
  }

  isUnassessed(row: { fullyAssessed: boolean; questionCount: number }): boolean {
    return !row.fullyAssessed && row.questionCount > 0;
  }

  /** The radio input id of a weighting option. */
  schemeOptionId(value: BatteryWeightingSchemeKey): string {
    return 'bbeScheme-' + value;
  }

  /** The chosen scheme's weight of a preview row as a bar width in percent, 0 while undefined. */
  weightPercent(row: BatteryPreviewRow): number {
    const weight = row.weights[this.scheme];
    return weight != null && Number.isFinite(weight) ? Math.min(100, Math.max(0, weight * 100)) : 0;
  }

  /** Rebuilds the list items and the preview, and loads the masses the preview still lacks. */
  private refresh(): void {
    this.items = this.rows.map(row => ({
      key: KEY_PREFIX + row.suiteId,
      label: row.name,
      checked: row.checked,
      tags: [
        `${row.questionCount} ${row.questionCount === 1 ? 'question' : 'questions'}`,
        ...(this.isUnassessed(row) ? ['Difficulties not assessed'] : [])
      ]
    }));

    for (const row of this.checkedRows) {
      this.ensureMass(row.suiteId);
    }

    const checked = this.checkedRows;
    const masses: BenchmarkBatterySuiteMass[] = checked.map(row => ({
      itemCount: row.questionCount,
      difficultyMass: this.masses.get(row.suiteId) ?? Number.NaN
    }));
    const customWeights = checked.map(row => row.customWeight);
    const bySchema = {} as Record<BatteryWeightingSchemeKey, number[] | null>;
    for (const option of this.schemeOptions) {
      bySchema[option.value] = previewBatteryWeights(option.value, masses, customWeights);
    }

    this.previewRows = checked.map((row, i) => ({
      suiteId: row.suiteId,
      name: row.name,
      questionCount: row.questionCount,
      assessedQuestionCount: row.assessedQuestionCount,
      fullyAssessed: row.fullyAssessed,
      difficultyMass: this.masses.get(row.suiteId) ?? null,
      massError: this.massErrors.get(row.suiteId) ?? null,
      customWeight: row.customWeight,
      weights: {
        DifficultyMass: bySchema.DifficultyMass?.[i] ?? null,
        ItemCount: bySchema.ItemCount?.[i] ?? null,
        Equal: bySchema.Equal?.[i] ?? null,
        Custom: bySchema.Custom?.[i] ?? null
      }
    }));
    this.cdr.markForCheck();
  }

  private ensureMass(suiteId: number): void {
    if (this.masses.has(suiteId) || this.massRequests.has(suiteId) || this.massErrors.has(suiteId)) {
      return;
    }
    const request = this.benchmarkService.getQuestions(suiteId).subscribe({
      next: (questions) => {
        this.massRequests.delete(suiteId);
        this.masses.set(suiteId, questions.reduce((sum, q) => sum + questionWeight(q.assessedDifficulty), 0));
        this.refresh();
      },
      error: (err) => {
        this.massRequests.delete(suiteId);
        this.massErrors.set(suiteId, httpErrorText(err, 'Could not load the questions.'));
        this.refresh();
      }
    });
    if (!request.closed) {
      this.massRequests.set(suiteId, request);
    }
  }

  private cancelMassRequests(): void {
    for (const request of this.massRequests.values()) {
      request.unsubscribe();
    }
    this.massRequests.clear();
  }

  // --- Save ------------------------------------------------------------------------------------

  save(): void {
    this.attemptedSave = true;
    if (!this.canSave) {
      this.cdr.markForCheck();
      return;
    }
    const checked = this.checkedRows;
    const request: CreateBenchmarkBatteryRequest = {
      name: this.name.trim(),
      description: this.description.trim() === '' ? null : this.description.trim(),
      weightingScheme: this.scheme,
      suiteIds: checked.map(r => r.suiteId),
      ...(this.scheme === 'Custom' ? { customWeights: checked.map(r => r.customWeight) } : {})
    };

    this.saving = true;
    this.saveError = null;
    const call = this.editing
      ? this.benchmarkService.updateBattery(this.editing.id, request)
      : this.benchmarkService.createBattery(request);
    call.subscribe({
      next: (battery) => {
        this.saving = false;
        this.saved.emit(battery);
        this.close();
      },
      error: (err) => {
        this.saving = false;
        this.saveError = httpErrorText(err, 'The battery could not be saved.');
        this.cdr.markForCheck();
      }
    });
  }
}
