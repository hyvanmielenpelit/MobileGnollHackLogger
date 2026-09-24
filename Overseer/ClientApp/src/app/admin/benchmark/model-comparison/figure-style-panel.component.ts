import { ChangeDetectionStrategy, Component, EventEmitter, Input, Output } from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';

import type { FigureBadgeKind } from './figure-chrome';
import {
  BadgeControl,
  BarFigureStyle,
  DEFAULT_FIGURE_STYLE,
  FigureStyle,
  HIDDEN_INTERVALS_NOTE,
  NumericBarStyleKey,
  ProfileFigureStyle,
  RangeControl,
  ScatterFigureStyle,
  badgeControlsFor,
  barRangeControl,
  chromeRangeControl,
  clampToControl,
  scatterRangeControl
} from './figure-style';
import { FRONTIER_UNCERTAINTY_NOTE, MEAN_TIME_NO_INTERVAL_NOTE } from './model-comparison-charts';
import { InfoTipComponent } from '../../../shared/info-tip/info-tip.component';

/** Which control set the panel shows: the bar panels', the trade-off scatters', or the profile's. */
export type FigureStylePanelKind = 'bar' | 'scatter' | 'profile';

type StyleFamily = 'bar' | 'scatter' | 'profile';

/** One collapsible section: its key within the family and its summary title. */
export interface FigureStyleSection {
  readonly name: string;
  readonly title: string;
}

/** Each family's sections, in the order the panel stacks them. */
export const FIGURE_STYLE_SECTIONS: Readonly<Record<StyleFamily, readonly FigureStyleSection[]>> = {
  bar: [
    { name: 'heading', title: 'Heading and badges' },
    { name: 'bars', title: 'Bars' },
    { name: 'values', title: 'Values and axes' },
    { name: 'uncertainty', title: 'Uncertainty' },
    { name: 'footer', title: 'Footer' },
    { name: 'layout', title: 'Layout' }
  ],
  scatter: [
    { name: 'heading', title: 'Heading and badges' },
    { name: 'marks', title: 'Marks and frontier' },
    { name: 'labels', title: 'Labels and legend' },
    { name: 'axes', title: 'Axes' },
    { name: 'uncertainty', title: 'Uncertainty' },
    { name: 'footer', title: 'Footer' }
  ],
  profile: [
    { name: 'heading', title: 'Heading and badges' },
    { name: 'footer', title: 'Footer' }
  ]
};

/**
 * Where the open sections are kept, per browser, as `{ bar: [...], scatter: [...], profile: [...] }`.
 * A family missing from the stored value opens its first section.
 */
export const FIGURE_STYLE_PANEL_OPEN_KEY = 'overseer.figureStylePanel.open';

const FAMILIES: readonly StyleFamily[] = ['bar', 'scatter', 'profile'];

/** The badge names a Heading and badges read-out lists. */
const BADGE_READOUT_NAMES: Record<FigureBadgeKind, string> = {
  direction: 'Better',
  models: 'models',
  runs: 'runs',
  questions: 'questions',
  pricing: 'pricing'
};

/**
 * The preview dialog's Style tab: one control set for the three bar panels, another for the three
 * trade-off charts, and a caption set for the profile, each as a stack of collapsible sections.
 *
 * Every style change is emitted as a whole new style, and the wizard owns the style, its persistence
 * and the rebuild. The panel keeps only the last finite bar width and which sections are open.
 */
@Component({
  selector: 'app-figure-style-panel',
  standalone: true,
  imports: [NgTemplateOutlet, InfoTipComponent],
  templateUrl: './figure-style-panel.component.html',
  styleUrls: ['./figure-style-panel.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class FigureStylePanelComponent {
  @Input() kind: FigureStylePanelKind = 'bar';

  @Input() figureStyle: FigureStyle = DEFAULT_FIGURE_STYLE;

  /** The wizard's *Label models inside the chart* toggle. */
  @Input() directLabels = false;

  /** The wizard's *Show values in the chart* toggle. */
  @Input() inlineValues = false;

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

  /** The caption text each note checkbox adds, quoted verbatim in its tip. */
  readonly meanTimeHint = `Adds, on mean time per question: ${MEAN_TIME_NO_INTERVAL_NOTE}`;
  readonly frontierHint = `Adds, when it applies: ${FRONTIER_UNCERTAINTY_NOTE}`;

  /** Open section keys, `bar.heading` and the like. */
  openSections: Set<string> = this.readOpenSections();

  /** Where *No limit* returns to when it is unticked. */
  private lastBarWidthPx = DEFAULT_FIGURE_STYLE.bar.maxBarWidthPx ?? 24;

  get bar(): BarFigureStyle {
    return this.figureStyle.bar;
  }

  get scatter(): ScatterFigureStyle {
    return this.figureStyle.scatter;
  }

  get profile(): ProfileFigureStyle {
    return this.figureStyle.profile;
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
  rangeHint(family: StyleFamily, control: RangeControl<string>): string {
    const hint = control.hint ?? '';
    if (control.key === 'footerTextSizePx' && !this.figureStyle[family].footer) {
      return `${hint} Available while the footer is shown.`;
    }
    return hint;
  }

  controlId(family: StyleFamily, key: string): string {
    return `mc-style-${family}-${key}`;
  }

  tipId(family: StyleFamily, key: string): string {
    return `${this.controlId(family, key)}-tip`;
  }

  rangeValue(family: StyleFamily, key: string): number {
    const value = (this.figureStyle[family] as unknown as Record<string, number | null>)[key];
    return value ?? this.lastBarWidthPx;
  }

  rangeDisabled(family: StyleFamily, key: string): boolean {
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
  valueText(family: StyleFamily, control: RangeControl<string>): string {
    if (family === 'bar' && control.key === 'maxBarWidthPx' && this.bar.maxBarWidthPx === null) {
      return 'No limit';
    }
    const value = this.rangeValue(family, control.key);
    return `${value} ${control.unit === '%' ? 'percent' : value === 1 ? 'pixel' : 'pixels'}`;
  }

  /** What the `<output>` beside the range shows: `24 px`, `28 %`. */
  valueLabel(family: StyleFamily, control: RangeControl<string>): string {
    if (family === 'bar' && control.key === 'maxBarWidthPx' && this.bar.maxBarWidthPx === null) {
      return 'No limit';
    }
    return `${this.rangeValue(family, control.key)} ${control.unit}`;
  }

  onRange(family: StyleFamily, control: RangeControl<string>, event: Event): void {
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
  setFamily(family: StyleFamily, key: string, value: unknown): void {
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

  resetBar(): void {
    this.lastBarWidthPx = DEFAULT_FIGURE_STYLE.bar.maxBarWidthPx ?? 24;
    this.figureStyleChange.emit({ ...this.figureStyle, bar: DEFAULT_FIGURE_STYLE.bar });
  }

  resetScatter(): void {
    this.figureStyleChange.emit({ ...this.figureStyle, scatter: DEFAULT_FIGURE_STYLE.scatter });
  }

  resetProfile(): void {
    this.figureStyleChange.emit({ ...this.figureStyle, profile: DEFAULT_FIGURE_STYLE.profile });
  }

  // --- Sections -----------------------------------------------------------------------------

  /** The family's section by name, for a template whose `family` is untyped. */
  section(family: StyleFamily, name: string): FigureStyleSection {
    return this.sections[family].find((section) => section.name === name)!;
  }

  badgeControlsOf(family: StyleFamily): readonly BadgeControl[] {
    return this.badgeControls[family];
  }

  footerShown(family: StyleFamily): boolean {
    return this.figureStyle[family].footer;
  }

  sectionId(family: StyleFamily, name: string): string {
    return `mc-style-${family}-section-${name}`;
  }

  isOpen(family: StyleFamily, name: string): boolean {
    return this.openSections.has(`${family}.${name}`);
  }

  /** Follows the native `toggle`, which fires for a click, a key and a bound `open` alike. */
  onSectionToggle(family: StyleFamily, name: string, event: Event): void {
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

  expandAll(family: StyleFamily): void {
    const next = new Set(this.openSections);
    for (const section of this.sections[family]) {
      next.add(`${family}.${section.name}`);
    }
    this.setOpenSections(next);
  }

  collapseAll(family: StyleFamily): void {
    this.setOpenSections(new Set([...this.openSections].filter((key) => !key.startsWith(`${family}.`))));
  }

  /** The one-line summary a closed section shows of its current values. */
  readout(family: StyleFamily, name: string): string {
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
          ...(bar.singleRunMarker ? ['n = 1'] : [])
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
          this.directLabels ? 'no legend' : `legend ${scatter.legendPosition}`
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
  private defaultOpen(family: StyleFamily): string[] {
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
