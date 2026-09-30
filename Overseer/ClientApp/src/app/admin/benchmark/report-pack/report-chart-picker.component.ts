import { ChangeDetectionStrategy, ChangeDetectorRef, Component, EventEmitter, Input, Output, inject } from '@angular/core';

import { BenchmarkReportAudience } from '../../../services/admin-benchmark.service';
import { REPORT_PACK_AUDIENCES, audienceLabel, audienceShortLabel } from './report-document-format';
import {
  REPORT_CHART_FIGURES,
  ReportChartFigureKey,
  ReportChartSelection,
  normalizeChartSelection,
  reportChartPlacementLabel
} from './report-charts';

/** One segment of the picker: a document type. */
export interface ReportChartPickerColumn {
  readonly audience: BenchmarkReportAudience;
  readonly label: string;
  readonly shortLabel: string;
  readonly enabled: boolean;
}

/** One row of the picker: a figure, and why the comparison cannot draw it when it cannot. */
export interface ReportChartPickerRow {
  readonly key: ReportChartFigureKey;
  readonly title: string;
  readonly unavailableReason: string | null;
}

/** Why a column is unavailable: its document type is not checked under Documents. */
export const REPORT_CHART_COLUMN_DISABLED_REASON = 'Not checked under Documents';

/** The reason a figure cannot be drawn, when the host gives none. */
export function defaultChartUnavailableReason(key: string): string {
  return key === 'p2-profile' ? 'needs three or more models' : 'needs two or more models';
}

/**
 * Which charts each document type carries into its PDF and Word copies: a segmented tab row of the
 * document types, each with its count, over the selected document's figures, one checkbox each with
 * the section it lands in. One document type shows its figures alone. A document type not being
 * written, and a figure the comparison cannot draw, stay listed with their checkboxes
 * `aria-disabled` and the reason shown. The host owns the selection and its storage.
 */
@Component({
  selector: 'app-report-chart-picker',
  standalone: true,
  templateUrl: './report-chart-picker.component.html',
  styleUrls: ['./report-chart-picker.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class ReportChartPickerComponent {
  private readonly cdr = inject(ChangeDetectorRef);

  /** The selection shown; the picker keeps its own copy until the host sets a new one. */
  @Input()
  set selection(value: ReportChartSelection) {
    this.current = normalizeChartSelection(value ?? {});
  }
  get selection(): ReportChartSelection {
    return this.current;
  }

  /** The document types shown, in this order. */
  @Input() audiences: readonly BenchmarkReportAudience[] = REPORT_PACK_AUDIENCES.map(option => option.audience);

  /** The document types enabled: those checked. */
  @Input() enabledAudiences: readonly BenchmarkReportAudience[] = REPORT_PACK_AUDIENCES.map(option => option.audience);

  /** The figure keys the comparison can draw. */
  @Input() available: readonly string[] = REPORT_CHART_FIGURES.map(figure => figure.key);

  /** A reason per figure key the comparison cannot draw, in place of the default one. */
  @Input() unavailableReasons: Readonly<Record<string, string>> = {};

  @Input() idPrefix = 'rcp';

  @Input() caption = 'Charts in PDF and Word';

  /** False keeps the caption for assistive technology only, where a legend around the picker already shows it. */
  @Input() captionVisible = true;

  /** Every change, normalized. */
  @Output() readonly selectionChange = new EventEmitter<ReportChartSelection>();

  readonly columnDisabledReason = REPORT_CHART_COLUMN_DISABLED_REASON;

  private current: ReportChartSelection = {};

  /** The segment chosen; component state only, never stored. */
  private activeAudience: BenchmarkReportAudience | null = null;

  get columns(): ReportChartPickerColumn[] {
    return this.audiences.map(audience => ({
      audience,
      label: audienceLabel(audience),
      shortLabel: audienceShortLabel(audience),
      enabled: this.enabledAudiences.includes(audience)
    }));
  }

  get rows(): ReportChartPickerRow[] {
    return REPORT_CHART_FIGURES.map(figure => ({
      key: figure.key,
      title: figure.title,
      unavailableReason: this.available.includes(figure.key)
        ? null
        : (this.unavailableReasons[figure.key] || defaultChartUnavailableReason(figure.key))
    }));
  }

  /** The chosen document when still shown, else the first one being written, else the first. */
  get activeColumn(): ReportChartPickerColumn | null {
    const columns = this.columns;
    return columns.find(column => column.audience === this.activeAudience)
      ?? columns.find(column => column.enabled)
      ?? columns[0]
      ?? null;
  }

  /** One document type needs no tab row. */
  get tabbed(): boolean {
    return this.audiences.length > 1;
  }

  selectAudience(audience: BenchmarkReportAudience): void {
    this.activeAudience = audience;
  }

  /** Arrow keys wrap, Home and End jump; focus follows the selection. */
  onTabKeydown(event: KeyboardEvent, index: number): void {
    const columns = this.columns;
    const count = columns.length;
    const targets: Record<string, number> = {
      ArrowRight: index + 1,
      ArrowLeft: index - 1,
      Home: 0,
      End: count - 1
    };
    const requested = targets[event.key];
    if (requested === undefined || count === 0) {
      return;
    }
    event.preventDefault();
    const column = columns[(requested + count) % count];
    this.selectAudience(column.audience);
    this.cdr.detectChanges();
    document.getElementById(this.tabId(column))?.focus();
  }

  tabId(column: ReportChartPickerColumn): string {
    return `${this.idPrefix}-tab-${column.audience}`;
  }

  panelId(column: ReportChartPickerColumn): string {
    return `${this.idPrefix}-panel-${column.audience}`;
  }

  panelTitleId(column: ReportChartPickerColumn): string {
    return `${this.idPrefix}-panel-${column.audience}-title`;
  }

  selectedCount(column: ReportChartPickerColumn): number {
    return this.rows.filter(row => this.isChecked(column.audience, row)).length;
  }

  drawableCount(): number {
    return this.rows.filter(row => row.unavailableReason === null).length;
  }

  /** The segment's status for assistive technology: `, 2 of 7 charts`. */
  tabStatus(column: ReportChartPickerColumn): string {
    return column.enabled
      ? `, ${this.selectedCount(column)} of ${this.drawableCount()} charts`
      : ', not checked under Documents';
  }

  placement(audience: BenchmarkReportAudience, key: ReportChartFigureKey): string {
    return reportChartPlacementLabel(audience, key);
  }

  /** Checked when selected and drawable; a figure the comparison cannot draw reads unchecked. */
  isChecked(audience: BenchmarkReportAudience, row: ReportChartPickerRow): boolean {
    return row.unavailableReason === null && (this.current[audience] ?? []).includes(row.key);
  }

  isCellDisabled(column: ReportChartPickerColumn, row: ReportChartPickerRow): boolean {
    return !column.enabled || row.unavailableReason !== null;
  }

  /** `Include Intelligence in the Executive Summary`. */
  checkboxName(column: ReportChartPickerColumn, row: ReportChartPickerRow): string {
    return `Include ${row.title} in the ${column.label}`;
  }

  columnReasonId(column: ReportChartPickerColumn): string {
    return `${this.idPrefix}-col-${column.audience}-reason`;
  }

  rowReasonId(row: ReportChartPickerRow): string {
    return `${this.idPrefix}-row-${row.key}-reason`;
  }

  cellId(column: ReportChartPickerColumn, row: ReportChartPickerRow): string {
    return `${this.idPrefix}-${column.audience}-${row.key}`;
  }

  /** The cell's placement, then whichever reasons hold it back. */
  cellDescribedBy(column: ReportChartPickerColumn, row: ReportChartPickerRow): string {
    const ids = [`${this.cellId(column, row)}-placement`];
    if (row.unavailableReason !== null) {
      ids.push(this.rowReasonId(row));
    }
    if (!column.enabled) {
      ids.push(this.columnReasonId(column));
    }
    return ids.join(' ');
  }

  /** An `aria-disabled` checkbox keeps focus but never changes. */
  onCellClick(event: Event, column: ReportChartPickerColumn, row: ReportChartPickerRow): void {
    if (this.isCellDisabled(column, row)) {
      event.preventDefault();
    }
  }

  onCellChange(event: Event, column: ReportChartPickerColumn, row: ReportChartPickerRow): void {
    if (this.isCellDisabled(column, row)) {
      return;
    }
    const checked = (event.target as HTMLInputElement).checked;
    const keys = new Set(this.current[column.audience] ?? []);
    if (checked) {
      keys.add(row.key);
    } else {
      keys.delete(row.key);
    }
    this.commit(column.audience, [...keys]);
  }

  /** All: every figure the comparison can draw. */
  selectAll(column: ReportChartPickerColumn): void {
    if (!column.enabled) {
      return;
    }
    this.commit(column.audience, this.rows.filter(row => row.unavailableReason === null).map(row => row.key));
  }

  /** None: no figure. */
  selectNone(column: ReportChartPickerColumn): void {
    if (!column.enabled) {
      return;
    }
    this.commit(column.audience, []);
  }

  private commit(audience: BenchmarkReportAudience, keys: ReportChartFigureKey[]): void {
    this.current = normalizeChartSelection({ ...this.current, [audience]: keys });
    this.selectionChange.emit(this.current);
  }
}
