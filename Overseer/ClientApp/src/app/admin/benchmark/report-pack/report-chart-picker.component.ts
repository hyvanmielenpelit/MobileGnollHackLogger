import { ChangeDetectionStrategy, ChangeDetectorRef, Component, EventEmitter, Input, Output, inject } from '@angular/core';

import { BenchmarkReportAudience } from '../../../services/admin-benchmark.service';
import { REPORT_PACK_AUDIENCES, audienceLabel, audienceShortLabel } from './report-document-format';
import {
  REPORT_CHART_FIGURES,
  REPORT_CHART_HEADINGS,
  REPORT_CHART_LABEL_SIZES_PT,
  REPORT_CHART_MAX_HEIGHT_PERCENTS,
  REPORT_CHART_ORIENTATIONS,
  REPORT_CHART_THEMES,
  REPORT_CHART_WIDTHS,
  ReportChartDocumentLayout,
  ReportChartFigureKey,
  ReportChartLayoutSettings,
  ReportChartScope,
  ReportChartSelection,
  ReportChartWidth,
  documentChartLayoutFor,
  documentChartRefusal,
  documentLabelSizeRefusal,
  formatPoints,
  normalizeChartLayoutSettings,
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

/** Why a column is unavailable: its document type is not checked under Documents of this comparison. */
export const REPORT_CHART_COLUMN_DISABLED_REASON = 'Not checked under Documents of this comparison';

/** The reason a figure cannot be drawn, when the host gives none. */
export function defaultChartUnavailableReason(key: string): string {
  return key === 'p2-profile' ? 'needs three or more models' : 'needs two or more models';
}

/** One refused choice of a layout field, and why. */
export interface ReportChartLayoutRefusal {
  readonly id: string;
  readonly text: string;
}

/**
 * Which charts each document type carries into its PDF and Word copies: a segmented tab row of the
 * document types, each with its count, over the selected document's figures, one checkbox each with
 * the section it lands in. One document type shows its figures alone. A document type not being
 * written, and a figure the comparison cannot draw, stay listed with their checkboxes
 * `aria-disabled` and the reason shown.
 *
 * Given a `layout`, each figure also gets its width, and each document type a *Layout* disclosure
 * with the orientation, side by side, label size, maximum height, heading, logo and theme of its
 * charts. A combination the composer would refuse is offered `aria-disabled`, its reason listed in
 * the disclosure; choosing it keeps the current value and says why. The host owns the selection, the layout and
 * their storage.
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

  /**
   * The layout shown, per document type; null shows no width and no *Layout* disclosure. The picker
   * keeps its own copy until the host sets a new one.
   */
  @Input()
  set layout(value: ReportChartLayoutSettings | null) {
    this.currentLayout = value ? normalizeChartLayoutSettings(value) : null;
  }
  get layout(): ReportChartLayoutSettings | null {
    return this.currentLayout;
  }

  /** Whose placements the figures show: a per-model document's, or a comparison-scope one's. */
  @Input() scope: ReportChartScope = 'model';

  /** Every change, normalized. */
  @Output() readonly selectionChange = new EventEmitter<ReportChartSelection>();

  /** Every layout change, normalized. */
  @Output() readonly layoutChange = new EventEmitter<ReportChartLayoutSettings>();

  /** The document type shown, whenever the admin chooses another. */
  @Output() readonly activeAudienceChange = new EventEmitter<BenchmarkReportAudience>();

  readonly columnDisabledReason = REPORT_CHART_COLUMN_DISABLED_REASON;
  readonly widthOptions = REPORT_CHART_WIDTHS;
  readonly orientationOptions = REPORT_CHART_ORIENTATIONS;
  readonly labelSizes = REPORT_CHART_LABEL_SIZES_PT;
  readonly maxHeightPercents = REPORT_CHART_MAX_HEIGHT_PERCENTS;
  readonly headingOptions = REPORT_CHART_HEADINGS;
  readonly themeOptions = REPORT_CHART_THEMES;

  private current: ReportChartSelection = {};

  private currentLayout: ReportChartLayoutSettings | null = null;

  /** The segment chosen; component state only, never stored. */
  private activeAudience: BenchmarkReportAudience | null = null;

  /** Why the last attempt at a refused choice was kept back, by field id; cleared by the next change. */
  private refusedChoice: { readonly fieldId: string; readonly text: string } | null = null;

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
    if (audience === this.activeAudience) {
      return;
    }
    this.refusedChoice = null;
    this.activeAudience = audience;
    this.activeAudienceChange.emit(audience);
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
      : ', not checked under Documents of this comparison';
  }

  placement(audience: BenchmarkReportAudience, key: ReportChartFigureKey): string {
    return reportChartPlacementLabel(audience, key, this.scope);
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

  // --- Layout ---

  layoutOf(column: ReportChartPickerColumn): ReportChartDocumentLayout {
    return documentChartLayoutFor(this.currentLayout, column.audience);
  }

  widthOf(column: ReportChartPickerColumn, row: ReportChartPickerRow): ReportChartWidth {
    return this.layoutOf(column).widths[row.key] ?? 'full';
  }

  /** Why `width` cannot be composed at the document's label size, or null. */
  widthRefusal(column: ReportChartPickerColumn, width: ReportChartWidth): string | null {
    return documentChartRefusal(width, this.layoutOf(column).labelPt);
  }

  /** Why `labelPt` cannot be chosen with the widths of the document's selected figures, or null. */
  labelSizeRefusal(column: ReportChartPickerColumn, labelPt: number): string | null {
    return documentLabelSizeRefusal(this.layoutOf(column), this.current[column.audience] ?? [], labelPt);
  }

  /** The refused choices of the document's layout, each with the id its field is described by. */
  layoutRefusals(column: ReportChartPickerColumn): ReportChartLayoutRefusal[] {
    const refusals: ReportChartLayoutRefusal[] = [];
    for (const option of this.widthOptions) {
      const text = this.widthRefusal(column, option.value);
      if (text) {
        refusals.push({ id: this.widthReasonId(column, option.value), text: `${option.label}: ${text}` });
      }
    }
    const refusedSizes = this.labelSizes.filter(size => this.labelSizeRefusal(column, size) !== null);
    if (refusedSizes.length > 0) {
      const sizes = refusedSizes.map(size => formatPoints(size)).join(', ');
      refusals.push({ id: this.labelReasonId(column), text: `${sizes} pt: ${this.labelSizeRefusal(column, refusedSizes[0])}` });
    }
    return refusals;
  }

  /** The kept-back choice's reason, when it was on this field. */
  refusedChoiceText(fieldId: string): string | null {
    return this.refusedChoice?.fieldId === fieldId ? this.refusedChoice.text : null;
  }

  widthId(column: ReportChartPickerColumn, row: ReportChartPickerRow): string {
    return `${this.cellId(column, row)}-width`;
  }

  widthReasonId(column: ReportChartPickerColumn, width: ReportChartWidth): string {
    return `${this.idPrefix}-${column.audience}-width-${width}-reason`;
  }

  labelReasonId(column: ReportChartPickerColumn): string {
    return `${this.idPrefix}-${column.audience}-label-reason`;
  }

  layoutFieldId(column: ReportChartPickerColumn, field: string): string {
    return `${this.idPrefix}-${column.audience}-${field}`;
  }

  /** `Width of Intelligence in the Executive Summary`. */
  widthName(column: ReportChartPickerColumn, row: ReportChartPickerRow): string {
    return `Width of ${row.title} in the ${column.label}`;
  }

  /** The refused widths' reasons, then a kept-back choice's. */
  widthDescribedBy(column: ReportChartPickerColumn, row: ReportChartPickerRow): string | null {
    const ids = this.widthOptions
      .filter(option => this.widthRefusal(column, option.value) !== null)
      .map(option => this.widthReasonId(column, option.value));
    if (this.refusedChoiceText(this.widthId(column, row))) {
      ids.push(`${this.widthId(column, row)}-refused`);
    }
    return ids.length > 0 ? ids.join(' ') : null;
  }

  labelDescribedBy(column: ReportChartPickerColumn): string | null {
    const ids: string[] = [];
    if (this.labelSizes.some(size => this.labelSizeRefusal(column, size) !== null)) {
      ids.push(this.labelReasonId(column));
    }
    const fieldId = this.layoutFieldId(column, 'label');
    if (this.refusedChoiceText(fieldId)) {
      ids.push(`${fieldId}-refused`);
    }
    return ids.length > 0 ? ids.join(' ') : null;
  }

  formatPoints(points: number): string {
    return formatPoints(points);
  }

  onWidthChange(event: Event, column: ReportChartPickerColumn, row: ReportChartPickerRow): void {
    const select = event.target as HTMLSelectElement;
    const width = select.value as ReportChartWidth;
    const refusal = this.widthRefusal(column, width);
    if (refusal) {
      select.value = this.widthOf(column, row);
      this.refusedChoice = { fieldId: this.widthId(column, row), text: refusal };
      return;
    }
    const widths = { ...this.layoutOf(column).widths, [row.key]: width };
    this.commitLayout(column, { widths });
  }

  onLabelSizeChange(event: Event, column: ReportChartPickerColumn): void {
    const select = event.target as HTMLSelectElement;
    const labelPt = Number(select.value);
    const refusal = this.labelSizeRefusal(column, labelPt);
    if (refusal) {
      select.value = String(this.layoutOf(column).labelPt);
      this.refusedChoice = { fieldId: this.layoutFieldId(column, 'label'), text: refusal };
      return;
    }
    this.commitLayout(column, { labelPt });
  }

  /** A select whose every option can be chosen: orientation, maximum height, heading and theme. */
  onLayoutSelect(event: Event, column: ReportChartPickerColumn, field: 'orientation' | 'maxHeightPercent' | 'heading' | 'theme'): void {
    const value = (event.target as HTMLSelectElement).value;
    this.commitLayout(column, { [field]: field === 'maxHeightPercent' ? Number(value) : value } as Partial<ReportChartDocumentLayout>);
  }

  onLayoutToggle(event: Event, column: ReportChartPickerColumn, field: 'sideBySide' | 'logo'): void {
    this.commitLayout(column, { [field]: (event.target as HTMLInputElement).checked } as Partial<ReportChartDocumentLayout>);
  }

  private commitLayout(column: ReportChartPickerColumn, patch: Partial<ReportChartDocumentLayout>): void {
    this.refusedChoice = null;
    this.currentLayout = normalizeChartLayoutSettings({
      ...(this.currentLayout ?? {}),
      [column.audience]: { ...this.layoutOf(column), ...patch }
    });
    this.layoutChange.emit(this.currentLayout);
  }
}
