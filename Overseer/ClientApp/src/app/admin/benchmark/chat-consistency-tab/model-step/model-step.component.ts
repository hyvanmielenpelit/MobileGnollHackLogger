import {
  ChangeDetectionStrategy,
  ChangeDetectorRef,
  Component,
  EventEmitter,
  Input,
  OnChanges,
  Output,
  SimpleChanges,
  inject
} from '@angular/core';

import { ModelPickerComponent, ModelPickerOption } from '../../../../shared/model-picker/model-picker.component';
import { SortHeaderComponent } from '../../../../shared/data-table/sort-header.component';
import { TableState } from '../../../../shared/data-table/table-state';
import { TablePagerComponent } from '../../../../shared/data-table/table-pager.component';
import {
  axisText,
  controlRunsText,
  formatUtcDateTime,
  isUtcDateInput,
  regradeCoverageText,
  runStatusText,
  segmentText,
  servedModelsText,
  utcMillis
} from '../chat-consistency-format';
import { CC_AXES, CcAxis, CcAxisEligibility, CcModelAxis, CcRunRow, CcTimeline } from '../chat-consistency.models';

/** A model axis as the picker renders it. */
export interface CcAxisPickerModel {
  displayName: string;
  provider: string;
  modelId: string;
  thinkingLevel: string | null;
}

/** The UTC date range the timeline and the run table are read over; an empty bound is open. */
export interface CcDayRange {
  fromDay: string;
  toDay: string;
}

/** The picker's options: the model axes that have runs, as the server orders them. */
export function axisPickerOptions(axes: readonly CcModelAxis[]): ModelPickerOption<CcAxisPickerModel>[] {
  return axes
    .filter(axis => axis.runCount > 0)
    .map(axis => ({
      key: axis.key,
      model: { displayName: axis.displayName, provider: axis.provider, modelId: axis.modelId, thinkingLevel: axis.thinkingLevel }
    }));
}

/**
 * Step 1 of the Chat Consistency wizard: the subject and the UTC date range in one row, then the run
 * table with each run's eligibility per axis and its row actions. Presentational: the host loads the
 * data and performs the actions this step asks for.
 */
@Component({
  selector: 'app-cc-model-step',
  standalone: true,
  imports: [ModelPickerComponent, SortHeaderComponent, TablePagerComponent],
  templateUrl: './model-step.component.html',
  styleUrls: ['./model-step.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class CcModelStepComponent implements OnChanges {
  private readonly cdr = inject(ChangeDetectorRef);

  @Input() axes: readonly CcModelAxis[] = [];
  @Input() axesLoading = false;
  @Input() axesError: string | null = null;
  @Input() selectedKey: string | null = null;
  @Input() range: CcDayRange = { fromDay: '', toDay: '' };
  @Input() timeline: CcTimeline | null = null;
  @Input() rows: readonly CcRunRow[] = [];
  @Input() loading = false;
  @Input() timelineError: string | null = null;
  @Input() runsError: string | null = null;
  /** Runs whose anchor mark is being saved. */
  @Input() anchorBusy: ReadonlySet<number> = new Set<number>();
  @Input() anchorError: string | null = null;
  /** A one-off confirmation for the table's status line. */
  @Input() announcement = '';
  /** Why the model and the dates cannot change now; empty while they can. */
  @Input() lockedReason = '';

  @Output() readonly modelChange = new EventEmitter<string>();
  @Output() readonly rangeChange = new EventEmitter<CcDayRange>();
  @Output() readonly repeatSetup = new EventEmitter<number>();
  @Output() readonly anchorToggle = new EventEmitter<CcRunRow>();
  @Output() readonly openRunReport = new EventEmitter<number>();

  readonly axisOrder = CC_AXES;

  readonly runTable = new TableState<CcRunRow>('startedAtUtc', 'desc').registerAccessors({
    startedAtUtc: row => utcMillis(row.startedAtUtc),
    runId: row => row.runId,
    suite: row => row.suiteName,
    harness: row => row.harnessVersion ?? '',
    anchor: row => (row.isAnchor ? 1 : 0)
  });

  options: ModelPickerOption<CcAxisPickerModel>[] = [];
  fromDay = '';
  toDay = '';
  rangeError: string | null = null;

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['axes']) {
      this.options = axisPickerOptions(this.axes);
    }
    if (changes['range']) {
      this.fromDay = this.range.fromDay;
      this.toDay = this.range.toDay;
      this.rangeError = null;
    }
  }

  get selectedAxis(): CcModelAxis | null {
    return this.axes.find(axis => axis.key === this.selectedKey) ?? null;
  }

  /** The chosen model's name for the runs heading: the axis list's, else the loaded timeline's. */
  get selectedName(): string {
    return this.selectedAxis?.displayName ?? this.timeline?.subject.displayName ?? 'the model';
  }

  get pickerEmptyHint(): string {
    return this.axesError ?? (this.axesLoading ? 'Loading the models…' : 'No model has benchmark runs yet.');
  }

  /** The picker's description: its hint, and the lock reason while locked. */
  get modelDescribedBy(): string {
    return this.lockedReason ? 'cc-tl-model-hint cc-tl-lock-reason' : 'cc-tl-model-hint';
  }

  /** A date input's description: the range error and the lock reason, whichever are shown. */
  dateDescribedBy(): string | null {
    const ids = [this.rangeError ? 'cc-tl-range-error' : '', this.lockedReason ? 'cc-tl-lock-reason' : ''].filter(Boolean);
    return ids.length > 0 ? ids.join(' ') : null;
  }

  selectModel(key: string | number | null): void {
    if (this.lockedReason) return;
    if (typeof key === 'string' && key !== this.selectedKey) {
      this.modelChange.emit(key);
    }
  }

  /**
   * A date field changed: a valid range is applied at once, an invalid one is explained. While locked
   * the stored date is written back to the field.
   */
  onDayChange(which: 'from' | 'to', event: Event): void {
    const input = event.target as HTMLInputElement;
    if (this.lockedReason) {
      input.value = which === 'from' ? this.fromDay : this.toDay;
      return;
    }
    const value = input.value;
    if (which === 'from') this.fromDay = value; else this.toDay = value;
    if ((this.fromDay && !isUtcDateInput(this.fromDay)) || (this.toDay && !isUtcDateInput(this.toDay))) {
      this.rangeError = 'Enter the dates as complete calendar dates.';
    } else if (this.fromDay && this.toDay && this.fromDay > this.toDay) {
      this.rangeError = 'The start date must not be after the end date.';
    } else {
      this.rangeError = null;
      this.rangeChange.emit({ fromDay: this.fromDay, toDay: this.toDay });
    }
    this.cdr.markForCheck();
  }

  clearRange(): void {
    if (this.lockedReason) return;
    this.fromDay = '';
    this.toDay = '';
    this.rangeError = null;
    this.rangeChange.emit({ fromDay: '', toDay: '' });
  }

  onTableChanged(): void {
    this.cdr.markForCheck();
  }

  // --- Cells ---

  eligibilityOf(row: CcRunRow, axis: CcAxis): CcAxisEligibility | null {
    return row.eligibility.find(entry => entry.axis === axis) ?? null;
  }

  /** The axes a run cannot be used on, with the server's reason. */
  ineligibleReasons(row: CcRunRow): { axis: string; reason: string }[] {
    return row.eligibility
      .filter(entry => !entry.eligible)
      .map(entry => ({ axis: axisText(entry.axis), reason: entry.reason || 'Excluded' }));
  }

  axisLabel(axis: CcAxis): string {
    return axisText(axis);
  }

  started(row: CcRunRow): string {
    return formatUtcDateTime(row.startedAtUtc);
  }

  status(row: CcRunRow): string {
    return runStatusText(row.status);
  }

  segment(row: CcRunRow): string {
    return segmentText(row);
  }

  regrade(row: CcRunRow): string {
    return regradeCoverageText(row);
  }

  controls(row: CcRunRow): string {
    return controlRunsText(row);
  }

  served(row: CcRunRow): string {
    return servedModelsText(row.servedModelIds);
  }
}
