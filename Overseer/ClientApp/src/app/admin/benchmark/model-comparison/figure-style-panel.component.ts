import { ChangeDetectionStrategy, Component, EventEmitter, Input, OnInit, Output } from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';

import type { FigureBadgeKind } from './figure-chrome';
import {
  BadgeControl,
  BarFigureStyle,
  DEFAULT_APPEARANCE_STYLE,
  DEFAULT_FIGURE_STYLE,
  FIGURE_BACKGROUND_MODES,
  FIGURE_FONT_WEIGHTS,
  FIGURE_PREVIEW_BACKDROPS,
  FIGURE_THEME_NAMES,
  FigureAppearanceStyle,
  FigureBackgroundMode,
  FigureFontId,
  FigureFontWeight,
  FigurePreviewBackdrop,
  FigureStyle,
  FigureThemeName,
  HIDDEN_INTERVALS_NOTE,
  NumericAppearanceStyleKey,
  NumericBarStyleKey,
  ProfileFigureStyle,
  RangeControl,
  ScatterFigureStyle,
  appearanceRangeControl,
  badgeControlsFor,
  barRangeControl,
  chromeRangeControl,
  clampToControl,
  normalizeHexColor,
  scatterRangeControl
} from './figure-style';
import { FIGURE_FONTS, figureFont } from './figure-fonts';
import { appearanceWarnings } from './figure-theme';
import {
  DEFAULT_MEASURE_DECIMALS,
  MAX_MEASURE_DECIMALS,
  MEASURE_NAMES,
  MEASURE_SHORT_NAMES,
  NumberMeasure,
  NumberSamples,
  costNumberMeasure,
  formatMeasureSample,
  normalizeMeasureDecimals,
  speedNumberMeasure
} from './measure-format';
import { FRONTIER_UNCERTAINTY_NOTE, MEAN_TIME_NO_INTERVAL_NOTE } from './model-comparison-charts';
import type { CostMeasure, SpeedMeasure } from './model-comparison-charts';
import { InfoTipComponent } from '../../../shared/info-tip/info-tip.component';
import { ensureOverlayPolyfills } from '../../../utils/polyfills.util';

/** Which control set the panel shows: the bar panels', the trade-off scatters', the profile's, or the theme tab's. */
export type FigureStylePanelKind = 'bar' | 'scatter' | 'profile' | 'appearance';

type StyleFamily = 'bar' | 'scatter' | 'profile';

/** Every kind the generic section, range and reset methods operate over. */
type PanelFamily = StyleFamily | 'appearance';

/**
 * One collapsible section: its key within the family, its summary title and the style fields it
 * edits. A shared section edits the number formats every family shares, not family fields.
 */
export interface FigureStyleSection {
  readonly name: string;
  readonly title: string;
  readonly keys: readonly string[];
  readonly shared?: boolean;
}

const NUMBERS_SECTION: FigureStyleSection = { name: 'numbers', title: 'Number format', keys: [], shared: true };

const HEADING_KEYS = ['titleSizePx', 'badgeTextSizePx', 'hiddenBadges'] as const;
const FOOTER_KEYS = ['footer', 'footerTextSizePx'] as const;

/**
 * Each family's sections, in the order the panel stacks them. Together a family's sections claim
 * every one of its style fields once; the shared Number format section edits `numbers`.
 */
export const FIGURE_STYLE_SECTIONS: Readonly<Record<PanelFamily, readonly FigureStyleSection[]>> = {
  bar: [
    { name: 'heading', title: 'Heading and badges', keys: HEADING_KEYS },
    { name: 'bars', title: 'Bars', keys: ['gapPercent', 'maxBarWidthPx', 'cornerRadiusPx', 'outlineWidthPx', 'filledBars'] },
    {
      name: 'values',
      title: 'Values and axes',
      keys: [
        'valueLabels', 'valueLabelSizePx', 'singleRunMarker', 'thinkingLevelBreak', 'axisTextSizePx', 'axisTitleSizePx',
        'axisTitleBreak', 'axisTitleWeight'
      ]
    },
    NUMBERS_SECTION,
    { name: 'uncertainty', title: 'Uncertainty', keys: ['intervals', 'hiddenIntervalsNote', 'meanTimeNoIntervalNote'] },
    { name: 'footer', title: 'Footer', keys: FOOTER_KEYS },
    { name: 'layout', title: 'Layout', keys: ['orientation', 'gridlines', 'plotFrame'] }
  ],
  scatter: [
    { name: 'heading', title: 'Heading and badges', keys: HEADING_KEYS },
    { name: 'marks', title: 'Marks and frontier', keys: ['markRadiusPx', 'frontierWidthPx', 'dominatedShading'] },
    { name: 'labels', title: 'Labels and legend', keys: ['labelTextSizePx', 'legendPosition', 'thinkingLevelBreak'] },
    NUMBERS_SECTION,
    { name: 'axes', title: 'Axes', keys: ['axisTextSizePx', 'axisTitleSizePx', 'gridlines', 'axisTitleWeight', 'plotFrame'] },
    { name: 'uncertainty', title: 'Uncertainty', keys: ['intervals', 'hiddenIntervalsNote', 'frontierIntervalsNote'] },
    { name: 'footer', title: 'Footer', keys: FOOTER_KEYS }
  ],
  profile: [
    { name: 'heading', title: 'Heading and badges', keys: HEADING_KEYS },
    NUMBERS_SECTION,
    { name: 'footer', title: 'Footer', keys: FOOTER_KEYS }
  ],
  appearance: [
    { name: 'theme', title: 'Theme and background', keys: ['theme', 'background', 'backgroundColor', 'previewBackdrop', 'previewBackdropColor'] },
    { name: 'font', title: 'Font', keys: ['fontFamily', 'headingWeight', 'labelWeight'] },
    { name: 'colors', title: 'Text colour', keys: ['headingColor', 'textColor'] },
    { name: 'border', title: 'Borders', keys: ['border', 'borderWidthPx', 'borderRadiusPx', 'borderColor'] }
  ]
};

/** One decimal-count choice: the digit alone. The family's sample is shown beside the select. */
export interface NumberOption {
  readonly value: number;
  readonly label: string;
}

/**
 * Where the open sections are kept, per browser, as `{ bar: [...], scatter: [...], profile: [...],
 * appearance: [...] }`. A family missing from the stored value opens its first section.
 */
export const FIGURE_STYLE_PANEL_OPEN_KEY = 'overseer.figureStylePanel.open';

const FAMILIES: readonly PanelFamily[] = ['bar', 'scatter', 'profile', 'appearance'];

/** The badge names a Heading and badges read-out lists. */
const BADGE_READOUT_NAMES: Record<FigureBadgeKind, string> = {
  direction: 'Better',
  models: 'models',
  runs: 'runs',
  questions: 'questions',
  pricing: 'pricing'
};

const THEME_LABELS: Record<FigureThemeName, string> = { dark: 'Dark', light: 'Light' };
const BACKGROUND_LABELS: Record<FigureBackgroundMode, string> = {
  theme: 'Theme',
  transparent: 'Transparent',
  custom: 'Custom colour'
};
const PREVIEW_BACKDROP_LABELS: Record<FigurePreviewBackdrop, string> = { checkerboard: 'Checkerboard', color: 'Colour' };
const FONT_WEIGHT_LABELS: Record<FigureFontWeight, string> = {
  400: 'Regular (400)',
  500: 'Medium (500)',
  600: 'Semibold (600)',
  700: 'Bold (700)'
};

/** A colour that can follow the theme instead of holding a fixed hex value. */
type NullableAppearanceColorKey = 'headingColor' | 'textColor' | 'borderColor';
type AppearanceColorKey = 'backgroundColor' | 'previewBackdropColor' | NullableAppearanceColorKey;
const NULLABLE_APPEARANCE_COLOR_KEYS: readonly NullableAppearanceColorKey[] = ['headingColor', 'textColor', 'borderColor'];

/** Equal to the page's initial `scatterDirectLabels` and `scatterInlineValues`. */
const DEFAULT_DIRECT_LABELS = false;
const DEFAULT_INLINE_VALUES = true;

/** Equal field values; a badge list compares element by element, as the wizard keeps it in order. */
function sameStyleValue(a: unknown, b: unknown): boolean {
  if (Array.isArray(a) && Array.isArray(b)) {
    return a.length === b.length && a.every((item, index) => item === b[index]);
  }
  return a === b;
}

/**
 * The sidebar's Theme tab and Style sections: one control set for the three bar panels, another
 * for the three trade-off charts, a caption set for the profile, and the theme, font and border
 * controls shared by every chart and the table image — each as a stack of collapsible sections.
 *
 * Every style change is emitted as a whole new style, and the wizard owns the style, its persistence
 * and the rebuild. The panel keeps only the last finite bar width, the last chosen colour of a
 * field that can follow the theme, which sections are open, hex-field errors and the status line of
 * the last reset.
 */
@Component({
  selector: 'app-figure-style-panel',
  standalone: true,
  imports: [NgTemplateOutlet, InfoTipComponent],
  templateUrl: './figure-style-panel.component.html',
  styleUrls: ['./figure-style-panel.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class FigureStylePanelComponent implements OnInit {
  @Input() kind: FigureStylePanelKind = 'bar';

  @Input() figureStyle: FigureStyle = DEFAULT_FIGURE_STYLE;

  /** The wizard's *Label models inside the chart* toggle. */
  @Input() directLabels = false;

  /** The wizard's *Show values in the chart* toggle. */
  @Input() inlineValues = false;

  /** The wizard's speed measure, whose decimals the Number format section shows. */
  @Input() speedMeasure: SpeedMeasure = 'meanModelTime';

  /** The wizard's cost measure, whose decimals the bar and profile Number format sections show. */
  @Input() costMeasure: CostMeasure = 'candidateSuite';

  /** The shown family's plotted values the decimal options preview on. */
  @Input() numberSamples: NumberSamples = {};

  /** The bundled font's load state, shown only by the appearance kind. */
  @Input() fontLoadStatus = '';

  @Output() figureStyleChange = new EventEmitter<FigureStyle>();
  @Output() directLabelsChange = new EventEmitter<boolean>();
  @Output() inlineValuesChange = new EventEmitter<boolean>();

  readonly sections = FIGURE_STYLE_SECTIONS;

  readonly headingControls = [chromeRangeControl('titleSizePx'), chromeRangeControl('badgeTextSizePx')];
  readonly footerControl = chromeRangeControl('footerTextSizePx');

  readonly barBarsControls: readonly RangeControl<NumericBarStyleKey>[] =
    (['gapPercent', 'maxBarWidthPx', 'cornerRadiusPx', 'outlineWidthPx'] as const).map(barRangeControl);
  readonly barValueControl = barRangeControl('valueLabelSizePx');
  readonly barAxisControls = [barRangeControl('axisTextSizePx'), barRangeControl('axisTitleSizePx')];

  readonly scatterMarkControl = scatterRangeControl('markRadiusPx');
  readonly scatterFrontierControl = scatterRangeControl('frontierWidthPx');
  readonly scatterLabelControl = scatterRangeControl('labelTextSizePx');
  readonly scatterAxisControls = [scatterRangeControl('axisTextSizePx'), scatterRangeControl('axisTitleSizePx')];

  readonly appearanceBorderControls: readonly RangeControl<NumericAppearanceStyleKey>[] =
    (['borderWidthPx', 'borderRadiusPx'] as const).map(appearanceRangeControl);

  readonly badgeControls: Readonly<Record<StyleFamily, readonly BadgeControl[]>> = {
    bar: badgeControlsFor('bar'),
    scatter: badgeControlsFor('scatter'),
    profile: badgeControlsFor('profile')
  };

  readonly orientationOptions = [
    { value: 'auto', label: 'Automatic' },
    { value: 'vertical', label: 'Vertical' },
    { value: 'horizontal', label: 'Horizontal' }
  ] as const;

  readonly legendOptions = [
    { value: 'bottom', label: 'Bottom' },
    { value: 'right', label: 'Right' }
  ] as const;

  readonly axisTitleBreakOptions = [
    { value: 'auto', label: 'Automatic' },
    { value: 'always', label: 'Always' },
    { value: 'never', label: 'Never' }
  ] as const;

  /** Regular / Medium / Semibold / Bold: shared by the appearance weights and each family's axis title weight. */
  readonly fontWeightOptions = FIGURE_FONT_WEIGHTS.map((value) => ({ value, label: FONT_WEIGHT_LABELS[value] }));

  readonly themeOptions = FIGURE_THEME_NAMES.map((value) => ({ value, label: THEME_LABELS[value] }));
  readonly backgroundOptions = FIGURE_BACKGROUND_MODES.map((value) => ({ value, label: BACKGROUND_LABELS[value] }));
  readonly previewBackdropOptions = FIGURE_PREVIEW_BACKDROPS.map((value) => ({ value, label: PREVIEW_BACKDROP_LABELS[value] }));
  readonly fontOptions = FIGURE_FONTS;

  readonly measureNames = MEASURE_NAMES;

  /** 0 to 6 decimals, labelled with the digit alone; each row's sample is shown beside the select. */
  readonly decimalOptions: readonly NumberOption[] = Array.from(
    { length: MAX_MEASURE_DECIMALS + 1 },
    (_, decimals) => ({ value: decimals, label: `${decimals}` })
  );

  /** The caption text each note checkbox adds, quoted verbatim in its tip. */
  readonly meanTimeHint = `Adds, on mean time per question: ${MEAN_TIME_NO_INTERVAL_NOTE}`;
  readonly frontierHint = `Adds, when it applies: ${FRONTIER_UNCERTAINTY_NOTE}`;

  /** Open section keys, `bar.heading` and the like. */
  openSections: Set<string> = this.readOpenSections();

  /** What the status region announces after a reset; cleared by the next control change. */
  resetStatus = '';

  /** The hex field's own inline error, per colour field; cleared as soon as its text is edited. */
  hexErrors: Partial<Record<AppearanceColorKey, string>> = {};

  /** Where *No limit* returns to when it is unticked. */
  private lastBarWidthPx = DEFAULT_FIGURE_STYLE.bar.maxBarWidthPx ?? 24;

  /** Where a colour that can follow the theme returns to when it is switched back to a fixed value. */
  private lastNullableColor: Partial<Record<NullableAppearanceColorKey, string>> = {};

  ngOnInit(): void {
    ensureOverlayPolyfills();
  }

  get bar(): BarFigureStyle {
    return this.figureStyle.bar;
  }

  get scatter(): ScatterFigureStyle {
    return this.figureStyle.scatter;
  }

  get profile(): ProfileFigureStyle {
    return this.figureStyle.profile;
  }

  get appearance(): FigureAppearanceStyle {
    return this.figureStyle.appearance;
  }

  /** The *Filled bars* tip, which mentions the `n = 1` marker only while it is off. */
  get filledBarsHint(): string {
    const base = 'Single-run bars are outlined unless this is on; multi-run bars are always filled.';
    return this.bar.singleRunMarker
      ? base
      : `${base} With the n = 1 marker off, only the runs badge shows how many runs each bar has.`;
  }

  /** The label size matters only while one of the two plate toggles is on. */
  get labelsShown(): boolean {
    return this.directLabels || this.inlineValues;
  }

  hiddenIntervalsHint(family: 'bar' | 'scatter'): string {
    const base = `Adds: ${HIDDEN_INTERVALS_NOTE}`;
    return this.figureStyle[family].intervals ? `${base} Available once uncertainty bars are hidden.` : base;
  }

  /** A range's tip, with the reason it is disabled where that is not shown beside it. */
  rangeHint(family: PanelFamily, control: RangeControl<string>): string {
    const hint = control.hint ?? '';
    if (control.key === 'footerTextSizePx' && family !== 'appearance' && !this.figureStyle[family].footer) {
      return hint ? `${hint} Available while the footer is shown.` : 'Available while the footer is shown.';
    }
    return hint;
  }

  controlId(family: PanelFamily, key: string): string {
    return `mc-style-${family}-${key}`;
  }

  tipId(family: PanelFamily, key: string): string {
    return `${this.controlId(family, key)}-tip`;
  }

  rangeValue(family: PanelFamily, key: string): number {
    const value = (this.figureStyle[family] as unknown as Record<string, number | null>)[key];
    return value ?? this.lastBarWidthPx;
  }

  rangeDisabled(family: PanelFamily, key: string): boolean {
    if (family === 'appearance') {
      if (key === 'borderWidthPx') {
        return !this.appearance.border;
      }
      if (key === 'borderRadiusPx') {
        return !this.appearance.border && this.appearance.background === 'transparent';
      }
      return false;
    }
    if (key === 'footerTextSizePx') {
      return !this.figureStyle[family].footer;
    }
    if (family === 'bar') {
      return (key === 'maxBarWidthPx' && this.bar.maxBarWidthPx === null)
        || (key === 'valueLabelSizePx' && !this.bar.valueLabels);
    }
    return family === 'scatter' && key === 'labelTextSizePx' && !this.labelsShown;
  }

  /** What the range announces: `24 pixels`, `28 percent`, or `No limit`. */
  valueText(family: PanelFamily, control: RangeControl<string>): string {
    if (family === 'bar' && control.key === 'maxBarWidthPx' && this.bar.maxBarWidthPx === null) {
      return 'No limit';
    }
    const value = this.rangeValue(family, control.key);
    return `${value} ${control.unit === '%' ? 'percent' : value === 1 ? 'pixel' : 'pixels'}`;
  }

  /** What the `<output>` beside the range shows: `24 px`, `28 %`. */
  valueLabel(family: PanelFamily, control: RangeControl<string>): string {
    if (family === 'bar' && control.key === 'maxBarWidthPx' && this.bar.maxBarWidthPx === null) {
      return 'No limit';
    }
    return `${this.rangeValue(family, control.key)} ${control.unit}`;
  }

  onRange(family: PanelFamily, control: RangeControl<string>, event: Event): void {
    const raw = Number((event.target as HTMLInputElement).value);
    const value = clampToControl(raw, control, this.rangeValue(family, control.key));
    if (family === 'bar' && control.key === 'maxBarWidthPx') {
      this.lastBarWidthPx = value;
    }
    this.setFamily(family, control.key, value);
  }

  onNoBarWidthLimit(checked: boolean): void {
    if (!checked) {
      this.setBar('maxBarWidthPx', this.lastBarWidthPx);
      return;
    }
    if (this.bar.maxBarWidthPx !== null) {
      this.lastBarWidthPx = this.bar.maxBarWidthPx;
    }
    this.setBar('maxBarWidthPx', null);
  }

  /** Emits the style with one field of one family replaced. */
  setFamily(family: PanelFamily, key: string, value: unknown): void {
    this.resetStatus = '';
    this.figureStyleChange.emit({ ...this.figureStyle, [family]: { ...this.figureStyle[family], [key]: value } });
  }

  setBar<K extends keyof BarFigureStyle>(key: K, value: BarFigureStyle[K]): void {
    this.setFamily('bar', key, value);
  }

  setScatter<K extends keyof ScatterFigureStyle>(key: K, value: ScatterFigureStyle[K]): void {
    this.setFamily('scatter', key, value);
  }

  setProfile<K extends keyof ProfileFigureStyle>(key: K, value: ProfileFigureStyle[K]): void {
    this.setFamily('profile', key, value);
  }

  setAppearance<K extends keyof FigureAppearanceStyle>(key: K, value: FigureAppearanceStyle[K]): void {
    this.setFamily('appearance', key, value);
  }

  badgeControlId(family: StyleFamily, kind: FigureBadgeKind): string {
    return `mc-style-${family}-badge-${kind}`;
  }

  badgeShown(family: StyleFamily, kind: FigureBadgeKind): boolean {
    return !this.figureStyle[family].hiddenBadges.includes(kind);
  }

  /** Emits the family's new hidden list; `normalizeFigureStyle` in the wizard puts it in order. */
  setBadgeShown(family: StyleFamily, kind: FigureBadgeKind, shown: boolean): void {
    const current = this.figureStyle[family].hiddenBadges;
    const hidden = shown
      ? current.filter((k) => k !== kind)
      : current.includes(kind) ? [...current] : [...current, kind];
    this.setFamily(family, 'hiddenBadges', hidden);
  }

  checkedOf(event: Event): boolean {
    return (event.target as HTMLInputElement).checked;
  }

  // --- Theme, font, colour and border (the appearance kind) ---------------------------------

  onFontFamily(event: Event): void {
    this.setAppearance('fontFamily', (event.target as HTMLSelectElement).value as FigureFontId);
  }

  followsTheme(key: NullableAppearanceColorKey): boolean {
    return this.appearance[key] === null;
  }

  /** Ticking *Follow theme* remembers the fixed colour it replaces; unticking restores it. */
  onFollowTheme(key: NullableAppearanceColorKey, follow: boolean): void {
    if (follow) {
      const current = this.appearance[key];
      if (current !== null) {
        this.lastNullableColor[key] = current;
      }
      this.setAppearance(key, null);
      return;
    }
    this.setAppearance(key, this.lastNullableColor[key] ?? '#ffffff');
  }

  /** The swatch and hex field's shown value: the field's own colour, or its last one while it follows the theme. */
  appearanceColorValue(key: AppearanceColorKey): string {
    const value = (this.appearance as unknown as Record<AppearanceColorKey, string | null>)[key];
    if (value !== null) {
      return value;
    }
    return this.lastNullableColor[key as NullableAppearanceColorKey] ?? '#ffffff';
  }

  /** The native colour input always carries a valid `#rrggbb`, so it commits at once. */
  onColorPick(key: AppearanceColorKey, event: Event): void {
    this.commitAppearanceColor(key, (event.target as HTMLInputElement).value);
  }

  /** Clears the hex field's own error as soon as its text changes; nothing commits until it validates. */
  onHexInput(key: AppearanceColorKey): void {
    if (this.hexErrors[key] === undefined) {
      return;
    }
    const next = { ...this.hexErrors };
    delete next[key];
    this.hexErrors = next;
  }

  onHexCommit(key: AppearanceColorKey, event: Event): void {
    const raw = (event.target as HTMLInputElement).value.trim();
    if (!/^#[0-9a-fA-F]{6}$/.test(raw)) {
      this.hexErrors = { ...this.hexErrors, [key]: 'Enter a 6-digit hex colour, like #1a2b3c.' };
      return;
    }
    this.commitAppearanceColor(key, raw);
  }

  hexErrorId(key: AppearanceColorKey): string {
    return `${this.controlId('appearance', key)}-error`;
  }

  /** The hex field's pending error, or empty. */
  hexError(key: AppearanceColorKey): string {
    return this.hexErrors[key] ?? '';
  }

  colorWarnings(): string[] {
    return appearanceWarnings(this.appearance);
  }

  resetAppearance(): void {
    this.figureStyleChange.emit({ ...this.figureStyle, appearance: DEFAULT_APPEARANCE_STYLE });
    this.hexErrors = {};
    this.resetStatus = 'Theme and fonts reset to defaults.';
  }

  private commitAppearanceColor(key: AppearanceColorKey, raw: string): void {
    const value = normalizeHexColor(raw, this.appearanceColorValue(key));
    if (this.hexErrors[key] !== undefined) {
      const next = { ...this.hexErrors };
      delete next[key];
      this.hexErrors = next;
    }
    if (NULLABLE_APPEARANCE_COLOR_KEYS.includes(key as NullableAppearanceColorKey)) {
      this.lastNullableColor[key as NullableAppearanceColorKey] = value;
    }
    this.setAppearance(key, value);
  }

  private appearanceReadout(name: string): string {
    const a = this.appearance;
    switch (name) {
      case 'theme': {
        const background = a.background === 'theme' ? 'theme background'
          : a.background === 'transparent' ? 'transparent' : 'custom background';
        return [a.theme, background].join(' · ');
      }
      case 'font':
        return [figureFont(a.fontFamily).label, `headings ${a.headingWeight}`, `labels ${a.labelWeight}`].join(' · ');
      case 'colors':
        return [
          a.headingColor ? `heading ${a.headingColor}` : 'heading follows theme',
          a.textColor ? `text ${a.textColor}` : 'text follows theme'
        ].join(' · ');
      case 'border':
        return a.border ? `${a.borderWidthPx} px · radius ${a.borderRadiusPx}` : 'none';
      default:
        return '';
    }
  }

  // --- Number format -------------------------------------------------------------------------

  /** The three measures a family shows: Intelligence, the selected speed, and its cost. */
  numberRows(family: StyleFamily): readonly NumberMeasure[] {
    return [
      'intelligenceIndex',
      speedNumberMeasure(this.speedMeasure),
      family === 'scatter' ? 'costPerQuestion' : costNumberMeasure(this.costMeasure)
    ];
  }

  numberValue(measure: NumberMeasure): number {
    return this.figureStyle.numbers[measure];
  }

  numberControlId(family: StyleFamily, measure: NumberMeasure): string {
    return `mc-style-${family}-number-${measure}`;
  }

  /** What the row's `<output>` shows: the family's sample written at the measure's current decimal count. */
  numberSample(measure: NumberMeasure): string {
    return formatMeasureSample(measure, this.numberValue(measure), this.numberSamples[measure]);
  }

  numberSampleId(family: StyleFamily, measure: NumberMeasure): string {
    return `mc-style-${family}-number-${measure}-sample`;
  }

  onNumber(measure: NumberMeasure, event: Event): void {
    this.setNumber(measure, Number((event.target as HTMLSelectElement).value));
  }

  /** Emits the style with one measure's decimals replaced, in every family. */
  setNumber(measure: NumberMeasure, value: number): void {
    const numbers = this.figureStyle.numbers;
    this.resetStatus = '';
    this.figureStyleChange.emit({
      ...this.figureStyle,
      numbers: { ...numbers, [measure]: normalizeMeasureDecimals(value, numbers[measure]) }
    });
  }

  onDirectLabels(on: boolean): void {
    this.resetStatus = '';
    this.directLabelsChange.emit(on);
  }

  onInlineValues(on: boolean): void {
    this.resetStatus = '';
    this.inlineValuesChange.emit(on);
  }

  resetBar(): void {
    this.lastBarWidthPx = DEFAULT_FIGURE_STYLE.bar.maxBarWidthPx ?? 24;
    this.figureStyleChange.emit({ ...this.figureStyle, bar: DEFAULT_FIGURE_STYLE.bar });
    this.resetStatus = 'Bar style reset to defaults.';
  }

  resetScatter(): void {
    this.figureStyleChange.emit({ ...this.figureStyle, scatter: DEFAULT_FIGURE_STYLE.scatter });
    this.resetPageToggles();
    this.resetStatus = 'Trade-off style reset to defaults.';
  }

  resetProfile(): void {
    this.figureStyleChange.emit({ ...this.figureStyle, profile: DEFAULT_FIGURE_STYLE.profile });
    this.resetStatus = 'Profile style reset to defaults.';
  }

  /** Whether every field the section edits, and for Labels and legend the two page toggles, is at its default. */
  sectionIsDefault(family: PanelFamily, name: string): boolean {
    const fieldsDefault = this.sectionFieldsDefault(family, name);
    if (family === 'scatter' && name === 'labels') {
      return fieldsDefault && this.directLabels === DEFAULT_DIRECT_LABELS && this.inlineValues === DEFAULT_INLINE_VALUES;
    }
    return fieldsDefault;
  }

  /** Returns one section to its defaults, leaving every other field as it is. */
  resetSection(family: PanelFamily, name: string): void {
    if (this.sectionIsDefault(family, name)) {
      return;
    }
    const section = this.section(family, name);
    if (section.shared) {
      // Only the measures this family shows; a hidden measure keeps its setting.
      const numbers: Record<NumberMeasure, number> = { ...this.figureStyle.numbers };
      for (const measure of this.numberRows(family as StyleFamily)) {
        numbers[measure] = DEFAULT_MEASURE_DECIMALS[measure];
      }
      this.figureStyleChange.emit({ ...this.figureStyle, numbers });
      this.resetStatus = 'Visible number formats reset to defaults.';
      return;
    }
    if (!this.sectionFieldsDefault(family, name)) {
      const defaults = DEFAULT_FIGURE_STYLE[family] as unknown as Record<string, unknown>;
      const reset = Object.fromEntries(section.keys.map((key) => [key, defaults[key]]));
      if (family === 'bar' && name === 'bars') {
        this.lastBarWidthPx = DEFAULT_FIGURE_STYLE.bar.maxBarWidthPx ?? 24;
      }
      this.figureStyleChange.emit({ ...this.figureStyle, [family]: { ...this.figureStyle[family], ...reset } });
    }
    if (family === 'scatter' && name === 'labels') {
      this.resetPageToggles();
    }
    this.resetStatus = `${section.title} reset to defaults.`;
  }

  /** The reset button's accessible name. */
  resetLabel(family: PanelFamily, name: string): string {
    const section = this.section(family, name);
    return section.shared ? 'Reset visible number formats to defaults' : `Reset ${section.title} to defaults`;
  }

  /** The reset button's tooltip. */
  resetTip(family: PanelFamily, name: string): string {
    return this.sectionIsDefault(family, name) ? 'Already at defaults' : 'Reset to defaults';
  }

  private sectionFieldsDefault(family: PanelFamily, name: string): boolean {
    if (this.section(family, name).shared) {
      return this.numberRows(family as StyleFamily).every((measure) => this.figureStyle.numbers[measure] === DEFAULT_MEASURE_DECIMALS[measure]);
    }
    const current = this.figureStyle[family] as unknown as Record<string, unknown>;
    const defaults = DEFAULT_FIGURE_STYLE[family] as unknown as Record<string, unknown>;
    return this.section(family, name).keys.every((key) => sameStyleValue(current[key], defaults[key]));
  }

  /** The two trade-off toggles the page owns, emitted only where they differ from their defaults. */
  private resetPageToggles(): void {
    if (this.directLabels !== DEFAULT_DIRECT_LABELS) {
      this.directLabelsChange.emit(DEFAULT_DIRECT_LABELS);
    }
    if (this.inlineValues !== DEFAULT_INLINE_VALUES) {
      this.inlineValuesChange.emit(DEFAULT_INLINE_VALUES);
    }
  }

  // --- Sections -----------------------------------------------------------------------------

  /** The family's section by name, for a template whose `family` is untyped. */
  section(family: PanelFamily, name: string): FigureStyleSection {
    return this.sections[family].find((section) => section.name === name)!;
  }

  badgeControlsOf(family: StyleFamily): readonly BadgeControl[] {
    return this.badgeControls[family];
  }

  footerShown(family: StyleFamily): boolean {
    return this.figureStyle[family].footer;
  }

  sectionId(family: PanelFamily, name: string): string {
    return `mc-style-${family}-section-${name}`;
  }

  isOpen(family: PanelFamily, name: string): boolean {
    return this.openSections.has(`${family}.${name}`);
  }

  /** Follows the native `toggle`, which fires for a click, a key and a bound `open` alike. */
  onSectionToggle(family: PanelFamily, name: string, event: Event): void {
    const open = (event.target as HTMLDetailsElement).open;
    const key = `${family}.${name}`;
    if (open === this.openSections.has(key)) {
      return;
    }
    const next = new Set(this.openSections);
    if (open) {
      next.add(key);
    } else {
      next.delete(key);
    }
    this.setOpenSections(next);
  }

  expandAll(family: PanelFamily): void {
    const next = new Set(this.openSections);
    for (const section of this.sections[family]) {
      next.add(`${family}.${section.name}`);
    }
    this.setOpenSections(next);
  }

  collapseAll(family: PanelFamily): void {
    this.setOpenSections(new Set([...this.openSections].filter((key) => !key.startsWith(`${family}.`))));
  }

  /** The one-line summary a closed section shows of its current values. */
  readout(family: PanelFamily, name: string): string {
    if (this.section(family, name).shared) {
      return this.numberRows(family as StyleFamily)
        .map((measure) => `${MEASURE_SHORT_NAMES[measure]} ${this.figureStyle.numbers[measure]}`)
        .join(' · ');
    }
    if (family === 'appearance') {
      return this.appearanceReadout(name);
    }
    const style = this.figureStyle[family];
    switch (name) {
      case 'heading': {
        const shown = this.badgeControls[family]
          .filter((control) => !style.hiddenBadges.includes(control.kind))
          .map((control) => BADGE_READOUT_NAMES[control.kind]);
        return [`${style.titleSizePx} px`, `badges ${style.badgeTextSizePx} px`, shown.length > 0 ? shown.join(', ') : 'none shown']
          .join(' · ');
      }
      case 'footer':
        return style.footer ? `shown · ${style.footerTextSizePx} px` : 'hidden';
      case 'uncertainty': {
        const own = family === 'bar' ? this.bar : this.scatter;
        const note = family === 'bar'
          ? (this.bar.meanTimeNoIntervalNote ? 'Speed note' : '')
          : (this.scatter.frontierIntervalsNote ? 'frontier note' : '');
        const state = own.intervals ? 'shown' : own.hiddenIntervalsNote ? 'hidden, noted' : 'hidden';
        return [state, note].filter((part) => part !== '').join(' · ');
      }
      default:
        return family === 'bar' ? this.barReadout(name) : this.scatterReadout(name);
    }
  }

  private barReadout(name: string): string {
    const bar = this.bar;
    switch (name) {
      case 'bars':
        return [
          `${bar.gapPercent} % space`,
          bar.maxBarWidthPx === null ? 'no width limit' : `max ${bar.maxBarWidthPx} px`,
          bar.filledBars ? 'filled' : 'outlined'
        ].join(' · ');
      case 'values':
        return [
          bar.valueLabels ? `values ${bar.valueLabelSizePx} px` : 'no values',
          `axis ${bar.axisTextSizePx}/${bar.axisTitleSizePx} px`,
          ...(bar.axisTitleBreak === 'auto' ? [] : [bar.axisTitleBreak === 'always' ? 'title always broken' : 'title never broken']),
          ...(bar.singleRunMarker ? ['n = 1'] : []),
          ...(bar.thinkingLevelBreak ? ['level on own line'] : [])
        ].join(' · ');
      case 'layout': {
        const orientation = this.orientationOptions.find((option) => option.value === bar.orientation)?.label ?? '';
        return [orientation.toLowerCase(), bar.gridlines ? 'gridlines' : 'no gridlines'].join(' · ');
      }
      default:
        return '';
    }
  }

  private scatterReadout(name: string): string {
    const scatter = this.scatter;
    switch (name) {
      case 'marks':
        return [
          `marks ${scatter.markRadiusPx} px`,
          `frontier ${scatter.frontierWidthPx} px`,
          scatter.dominatedShading ? 'shaded' : 'unshaded'
        ].join(' · ');
      case 'labels': {
        const plates = [...(this.directLabels ? ['names'] : []), ...(this.inlineValues ? ['values'] : [])];
        return [
          plates.length > 0 ? `${plates.join(' and ')} ${scatter.labelTextSizePx} px` : 'no labels',
          this.directLabels ? 'no legend' : `legend ${scatter.legendPosition}`,
          ...(scatter.thinkingLevelBreak ? ['level on own line'] : [])
        ].join(' · ');
      }
      case 'axes':
        return [`axis ${scatter.axisTextSizePx}/${scatter.axisTitleSizePx} px`, scatter.gridlines ? 'gridlines' : 'no gridlines']
          .join(' · ');
      default:
        return '';
    }
  }

  private setOpenSections(next: Set<string>): void {
    this.openSections = next;
    this.writeOpenSections(next);
  }

  /** Each family's first section open, the rest closed. */
  private defaultOpen(family: PanelFamily): string[] {
    return [`${family}.${this.sections[family][0].name}`];
  }

  /** The stored open sections, family by family; the default wherever storage is absent or unreadable. */
  private readOpenSections(): Set<string> {
    let stored: unknown = null;
    try {
      const raw = localStorage.getItem(FIGURE_STYLE_PANEL_OPEN_KEY);
      stored = raw === null ? null : JSON.parse(raw);
    } catch {
      stored = null;
    }
    const record = typeof stored === 'object' && stored !== null && !Array.isArray(stored)
      ? stored as Record<string, unknown>
      : {};
    const open = new Set<string>();
    for (const family of FAMILIES) {
      const names = record[family];
      const known = this.sections[family].map((section) => section.name);
      const keys = Array.isArray(names)
        ? names.filter((name): name is string => typeof name === 'string' && known.includes(name))
          .map((name) => `${family}.${name}`)
        : this.defaultOpen(family);
      keys.forEach((key) => open.add(key));
    }
    return open;
  }

  private writeOpenSections(open: Set<string>): void {
    const record: Record<string, string[]> = {};
    for (const family of FAMILIES) {
      record[family] = this.sections[family]
        .map((section) => section.name)
        .filter((name) => open.has(`${family}.${name}`));
    }
    try {
      localStorage.setItem(FIGURE_STYLE_PANEL_OPEN_KEY, JSON.stringify(record));
    } catch {
      // Private mode or blocked storage: the sections still open and close for this session.
    }
  }
}
