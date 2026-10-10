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
  normalizeDocumentChartLayout,
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
  readonly key: string;
  readonly title: string;
  readonly unavailableReason: string | null;
}

/** A figure the picker lists, in placement order. */
export interface ReportChartPickerFigure<K extends string = string> {
  readonly key: K;
  readonly title: string;
  /**
   * The section the figure lands in: one name for every document type, or one per type. Absent
   * takes the Report Pack's placement for the picker's scope.
   */
  readonly placement?: string | Readonly<Partial<Record<BenchmarkReportAudience, string>>>;
}

/** A document type the picker can show: its full name and the segment's short one. */
export interface ReportChartPickerAudienceOption {
  readonly audience: BenchmarkReportAudience;
  readonly label: string;
  readonly shortLabel: string;
}

/** The fields of a document type's *Layout* disclosure. */
export type ReportChartLayoutField = 'orientation' | 'sideBySide' | 'labelPt' | 'maxHeightPercent' | 'heading' | 'logo' | 'theme';

export const REPORT_CHART_LAYOUT_FIELDS: readonly ReportChartLayoutField[] =
  ['orientation', 'sideBySide', 'labelPt', 'maxHeightPercent', 'heading', 'logo', 'theme'];

/** The Report Pack's figures, placed by `reportChartPlacementLabel`: the picker's figures by default. */
export const REPORT_PACK_PICKER_FIGURES: readonly ReportChartPickerFigure<ReportChartFigureKey>[] =
  REPORT_CHART_FIGURES.map(figure => ({ key: figure.key, title: figure.title }));

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
 * A selection over the figures `keys` and the document types `audiences`: deduplicated, in the
 * order of `keys`, unknown keys and document types dropped. A type present with no figures stays,
 * as none.
 */
export function normalizePickerSelection<K extends string>(
  selection: unknown,
  keys: readonly K[],
  audiences: readonly BenchmarkReportAudience[]
): ReportChartSelection<K> {
  const normalized: Partial<Record<BenchmarkReportAudience, readonly K[]>> = {};
  if (!selection || typeof selection !== 'object') {
    return normalized;
  }
  const source = selection as Record<string | number, unknown>;
  for (const audience of audiences) {
    const stored = source[audience];
    if (!Array.isArray(stored)) {
      continue;
    }
    const known = new Set(stored.filter((key): key is string => typeof key === 'string'));
    normalized[audience] = keys.filter(key => known.has(key));
  }
  return normalized;
}

/**
 * One document type's layout over the figures `keys`: every field checked as the Report Pack's are,
 * and the widths of `keys` kept where they are not full column and fit the label size.
 */
export function normalizePickerDocumentLayout<K extends string>(value: unknown, keys: readonly K[]): ReportChartDocumentLayout<K> {
  const base = normalizeDocumentChartLayout(value);
  const source = (value && typeof value === 'object' ? value : {}) as Record<string, unknown>;
  const stored = (source['widths'] && typeof source['widths'] === 'object' ? source['widths'] : {}) as Record<string, unknown>;
  const widths: Partial<Record<K, ReportChartWidth>> = {};
  for (const key of keys) {
    const width = stored[key];
    if ((width === 'twoThirds' || width === 'half') && documentChartRefusal(width, base.labelPt) === null) {
      widths[key] = width;
    }
  }
  return { ...base, widths };
}

/** The layout of each of `audiences` the source holds, normalized over `keys`; a missing type stays missing. */
export function normalizePickerLayoutSettings<K extends string>(
  layout: unknown,
  keys: readonly K[],
  audiences: readonly BenchmarkReportAudience[]
): ReportChartLayoutSettings<K> {
  const source = (layout && typeof layout === 'object' ? layout : {}) as Record<string | number, unknown>;
  const normalized: Partial<Record<BenchmarkReportAudience, ReportChartDocumentLayout<K>>> = {};
  for (const audience of audiences) {
    if (source[audience] !== undefined) {
      normalized[audience] = normalizePickerDocumentLayout(source[audience], keys);
    }
  }
  return normalized;
}

/** `Not chosen in New reports.` → `not chosen in New reports`, for the segment's status. */
function statusPhrase(reason: string): string {
  const text = reason.trim().replace(/\.$/, '');
  return text.charAt(0).toLowerCase() + text.slice(1);
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
 * charts, or the subset `layoutFields` names. A combination the composer would refuse is offered
 * `aria-disabled`, its reason listed in the disclosure; choosing it keeps the current value and says
 * why. The host owns the selection, the layout and their storage.
 *
 * The figures, the document types and their labels default to the Report Pack's. A host with figures
 * of its own passes them (`figures`) with its document types (`audienceOptions`); the picker then
 * keeps only those keys and types. Inside the picker a figure key is any string; the outputs keep the
 * Report Pack's types, so its hosts bind unchanged, and a host with its own figures reads them as
 * `ReportChartSelection<string>` and `ReportChartLayoutSettings<string>`.
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
  set selection(value: ReportChartSelection<string>) {
    this.hostSelection = value ?? {};
    this.current = this.normalizeSelection(this.hostSelection);
  }
  get selection(): ReportChartSelection<string> {
    return this.current;
  }

  /** The figures listed, in placement order; the Report Pack's by default. */
  @Input()
  set figures(value: readonly ReportChartPickerFigure[]) {
    this.figureList = value ?? REPORT_PACK_PICKER_FIGURES;
    this.renormalize();
  }
  get figures(): readonly ReportChartPickerFigure[] {
    return this.figureList;
  }

  /** The document types the picker knows, with their labels; the Report Pack's by default. */
  @Input()
  set audienceOptions(value: readonly ReportChartPickerAudienceOption[]) {
    this.audienceOptionList = value ?? REPORT_PACK_AUDIENCES;
    this.renormalize();
  }
  get audienceOptions(): readonly ReportChartPickerAudienceOption[] {
    return this.audienceOptionList;
  }

  /** The document types shown, in this order; every one of `audienceOptions` by default. */
  @Input()
  set audiences(value: readonly BenchmarkReportAudience[]) {
    this.audienceList = value ?? null;
  }
  get audiences(): readonly BenchmarkReportAudience[] {
    return this.audienceList ?? this.audienceOptionList.map(option => option.audience);
  }

  /** The document types enabled: those checked; every one of `audienceOptions` by default. */
  @Input()
  set enabledAudiences(value: readonly BenchmarkReportAudience[]) {
    this.enabledList = value ?? null;
  }
  get enabledAudiences(): readonly BenchmarkReportAudience[] {
    return this.enabledList ?? this.audienceOptionList.map(option => option.audience);
  }

  /** The figure keys the comparison can draw; every figure by default. */
  @Input()
  set available(value: readonly string[]) {
    this.availableKeys = value ?? null;
  }
  get available(): readonly string[] {
    return this.availableKeys ?? this.figureList.map(figure => figure.key);
  }

  /** A reason per figure key the comparison cannot draw, in place of the default one. */
  @Input() unavailableReasons: Readonly<Record<string, string>> = {};

  /** A note per figure key, shown under its section and read with its checkbox. */
  @Input() notes: Readonly<Record<string, string>> = {};

  /** Why a document type that is not enabled cannot be changed. */
  @Input() columnDisabledReason = REPORT_CHART_COLUMN_DISABLED_REASON;

  /** The fields the *Layout* disclosure offers; every one by default. */
  @Input() layoutFields: readonly ReportChartLayoutField[] = REPORT_CHART_LAYOUT_FIELDS;

  @Input() idPrefix = 'rcp';

  @Input() caption = 'Charts in PDF and Word';

  /** False keeps the caption for assistive technology only, where a legend around the picker already shows it. */
  @Input() captionVisible = true;

  /**
   * The layout shown, per document type; null shows no width and no *Layout* disclosure. The picker
   * keeps its own copy until the host sets a new one.
   */
  @Input()
  set layout(value: ReportChartLayoutSettings<string> | null) {
    this.hostLayout = value ?? null;
    this.currentLayout = this.normalizeLayout(this.hostLayout);
  }
  get layout(): ReportChartLayoutSettings<string> | null {
    return this.currentLayout;
  }

  /** Whose placements the figures show: a per-model document's, or a comparison-scope one's. */
  @Input() scope: ReportChartScope = 'model';

  /** Every change, normalized; keyed by `figures`' keys, the Report Pack's by default. */
  @Output() readonly selectionChange = new EventEmitter<ReportChartSelection>();

  /** Every layout change, normalized; keyed by `figures`' keys, the Report Pack's by default. */
  @Output() readonly layoutChange = new EventEmitter<ReportChartLayoutSettings>();

  /** The document type shown, whenever the admin chooses another. */
  @Output() readonly activeAudienceChange = new EventEmitter<BenchmarkReportAudience>();

  readonly widthOptions = REPORT_CHART_WIDTHS;
  readonly orientationOptions = REPORT_CHART_ORIENTATIONS;
  readonly labelSizes = REPORT_CHART_LABEL_SIZES_PT;
  readonly maxHeightPercents = REPORT_CHART_MAX_HEIGHT_PERCENTS;
  readonly headingOptions = REPORT_CHART_HEADINGS;
  readonly themeOptions = REPORT_CHART_THEMES;

  private figureList: readonly ReportChartPickerFigure<string>[] = REPORT_PACK_PICKER_FIGURES;

  private audienceOptionList: readonly ReportChartPickerAudienceOption[] = REPORT_PACK_AUDIENCES;

  private audienceList: readonly BenchmarkReportAudience[] | null = null;

  private enabledList: readonly BenchmarkReportAudience[] | null = null;

  private availableKeys: readonly string[] | null = null;

  /** The host's selection and layout as given, normalized again when the figures or document types change. */
  private hostSelection: ReportChartSelection<string> = {};

  private hostLayout: ReportChartLayoutSettings<string> | null = null;

  private current: ReportChartSelection<string> = {};

  private currentLayout: ReportChartLayoutSettings<string> | null = null;

  /** The segment chosen; component state only, never stored. */
  private activeAudience: BenchmarkReportAudience | null = null;

  /** Why the last attempt at a refused choice was kept back, by field id; cleared by the next change. */
  private refusedChoice: { readonly fieldId: string; readonly text: string } | null = null;

  get columns(): ReportChartPickerColumn[] {
    return this.audiences.map(audience => {
      const option = this.audienceOptionList.find(entry => entry.audience === audience);
      return {
        audience,
        label: option?.label ?? audienceLabel(audience),
        shortLabel: option?.shortLabel ?? audienceShortLabel(audience),
        enabled: this.enabledAudiences.includes(audience)
      };
    });
  }

  get rows(): ReportChartPickerRow[] {
    const available = this.available;
    return this.figureList.map(figure => ({
      key: figure.key,
      title: figure.title,
      unavailableReason: available.includes(figure.key)
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

  /** The segment's status for assistive technology: `, 2 of 7 charts`, or why it cannot be changed. */
  tabStatus(column: ReportChartPickerColumn): string {
    return column.enabled
      ? `, ${this.selectedCount(column)} of ${this.drawableCount()} charts`
      : `, ${statusPhrase(this.columnDisabledReason)}`;
  }

  placement(audience: BenchmarkReportAudience, key: string): string {
    const placement = this.figureList.find(figure => figure.key === key)?.placement;
    if (placement === undefined) {
      return reportChartPlacementLabel(audience, key as ReportChartFigureKey, this.scope);
    }
    return typeof placement === 'string' ? placement : (placement[audience] ?? '');
  }

  /** The host's note on a figure, or ''. */
  note(row: ReportChartPickerRow): string {
    return this.notes[row.key] ?? '';
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

  /** The cell's placement, its note, then whichever reasons hold it back. */
  cellDescribedBy(column: ReportChartPickerColumn, row: ReportChartPickerRow): string {
    const ids = [`${this.cellId(column, row)}-placement`];
    if (this.note(row)) {
      ids.push(`${this.cellId(column, row)}-note`);
    }
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

  private commit(audience: BenchmarkReportAudience, keys: string[]): void {
    this.current = this.normalizeSelection({ ...this.current, [audience]: keys });
    this.hostSelection = this.current;
    this.selectionChange.emit(this.current as ReportChartSelection);
  }

  // --- Figures and document types ---

  /** The host passed figures of its own, which the Report Pack's normalizers would drop. */
  private get ownFigures(): boolean {
    return this.figureList !== REPORT_PACK_PICKER_FIGURES;
  }

  private normalizeSelection(value: ReportChartSelection<string>): ReportChartSelection<string> {
    return this.ownFigures
      ? normalizePickerSelection(value, this.figureList.map(figure => figure.key), this.audienceOptionList.map(option => option.audience))
      : normalizeChartSelection(value as ReportChartSelection);
  }

  private normalizeLayout(value: ReportChartLayoutSettings<string> | null): ReportChartLayoutSettings<string> | null {
    if (!value) {
      return null;
    }
    return this.ownFigures
      ? normalizePickerLayoutSettings(value, this.figureList.map(figure => figure.key), this.audienceOptionList.map(option => option.audience))
      : normalizeChartLayoutSettings(value as ReportChartLayoutSettings) as ReportChartLayoutSettings<string>;
  }

  /** The host's selection and layout, normalized for the figures and document types now given. */
  private renormalize(): void {
    this.current = this.normalizeSelection(this.hostSelection);
    this.currentLayout = this.normalizeLayout(this.hostLayout);
  }

  // --- Layout ---

  /** Whether the *Layout* disclosure offers this field. */
  showsField(field: ReportChartLayoutField): boolean {
    return this.layoutFields.includes(field);
  }

  layoutOf(column: ReportChartPickerColumn): ReportChartDocumentLayout<string> {
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
    const refusedSizes = this.showsField('labelPt')
      ? this.labelSizes.filter(size => this.labelSizeRefusal(column, size) !== null)
      : [];
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
    this.commitLayout(column, { [field]: field === 'maxHeightPercent' ? Number(value) : value } as Partial<ReportChartDocumentLayout<string>>);
  }

  onLayoutToggle(event: Event, column: ReportChartPickerColumn, field: 'sideBySide' | 'logo'): void {
    this.commitLayout(column, { [field]: (event.target as HTMLInputElement).checked } as Partial<ReportChartDocumentLayout<string>>);
  }

  private commitLayout(column: ReportChartPickerColumn, patch: Partial<ReportChartDocumentLayout<string>>): void {
    this.refusedChoice = null;
    this.currentLayout = this.normalizeLayout({
      ...(this.currentLayout ?? {}),
      [column.audience]: { ...this.layoutOf(column), ...patch }
    });
    this.hostLayout = this.currentLayout;
    this.layoutChange.emit(this.currentLayout as ReportChartLayoutSettings);
  }
}
