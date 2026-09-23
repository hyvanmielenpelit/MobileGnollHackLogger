import { ChangeDetectionStrategy, Component, EventEmitter, Input, Output } from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';

import {
  BarFigureStyle,
  DEFAULT_FIGURE_STYLE,
  FigureStyle,
  HIDDEN_INTERVALS_NOTE,
  NumericBarStyleKey,
  NumericScatterStyleKey,
  RangeControl,
  ScatterFigureStyle,
  barRangeControl,
  clampToControl,
  scatterRangeControl
} from './figure-style';
import { FRONTIER_UNCERTAINTY_NOTE, MEAN_TIME_NO_INTERVAL_NOTE } from './model-comparison-charts';

/** Which control set the panel shows: the bar panels', the trade-off scatters', or the profile's note. */
export type FigureStylePanelKind = 'bar' | 'scatter' | 'profile';

type StyleFamily = 'bar' | 'scatter';

/**
 * The preview dialog's Style tab: one control set for the three bar panels, another for the three
 * trade-off charts, and a note for the profile, which has none.
 *
 * Stateless apart from the last finite bar width: every change is emitted as a whole new style, and
 * the wizard owns the state, its persistence and the rebuild.
 */
@Component({
  selector: 'app-figure-style-panel',
  standalone: true,
  imports: [NgTemplateOutlet],
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

  readonly barBarsControls: readonly RangeControl<NumericBarStyleKey>[] =
    (['gapPercent', 'maxBarWidthPx', 'cornerRadiusPx', 'outlineWidthPx'] as const).map(barRangeControl);
  readonly barAxisControl = barRangeControl('axisTextSizePx');
  readonly barValueControl = barRangeControl('valueLabelSizePx');

  readonly scatterMarkControl = scatterRangeControl('markRadiusPx');
  readonly scatterFrontierControl = scatterRangeControl('frontierWidthPx');
  readonly scatterLabelControl = scatterRangeControl('labelTextSizePx');
  readonly scatterAxisControl = scatterRangeControl('axisTextSizePx');

  readonly orientationOptions = [
    { value: 'auto', label: 'Automatic' },
    { value: 'vertical', label: 'Vertical' },
    { value: 'horizontal', label: 'Horizontal' }
  ] as const;

  readonly legendOptions = [
    { value: 'bottom', label: 'Bottom' },
    { value: 'right', label: 'Right' }
  ] as const;

  /** The caption text each note checkbox adds or removes, quoted verbatim in its hint. */
  readonly hiddenIntervalsHint = `Adds: ${HIDDEN_INTERVALS_NOTE}`;
  readonly meanTimeHint = `Adds, when Speed shows mean time per question: ${MEAN_TIME_NO_INTERVAL_NOTE}`;
  readonly frontierHint = `Adds, when it applies: ${FRONTIER_UNCERTAINTY_NOTE}`;

  /** Where *No limit* returns to when it is unticked. */
  private lastBarWidthPx = DEFAULT_FIGURE_STYLE.bar.maxBarWidthPx ?? 24;

  get bar(): BarFigureStyle {
    return this.figureStyle.bar;
  }

  get scatter(): ScatterFigureStyle {
    return this.figureStyle.scatter;
  }

  /** The label size matters only while one of the two plate toggles is on. */
  get labelsShown(): boolean {
    return this.directLabels || this.inlineValues;
  }

  controlId(family: StyleFamily, key: string): string {
    return `mc-style-${family}-${key}`;
  }

  rangeValue(family: StyleFamily, key: string): number {
    if (family === 'bar') {
      const value = this.bar[key as NumericBarStyleKey];
      return value ?? this.lastBarWidthPx;
    }
    return this.scatter[key as NumericScatterStyleKey];
  }

  rangeDisabled(family: StyleFamily, key: string): boolean {
    if (family === 'bar') {
      return (key === 'maxBarWidthPx' && this.bar.maxBarWidthPx === null)
        || (key === 'valueLabelSizePx' && !this.bar.valueLabels);
    }
    return key === 'labelTextSizePx' && !this.labelsShown;
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
    if (family === 'bar') {
      if (control.key === 'maxBarWidthPx') {
        this.lastBarWidthPx = value;
      }
      this.setBar(control.key as NumericBarStyleKey, value);
    } else {
      this.setScatter(control.key as NumericScatterStyleKey, value);
    }
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

  setBar<K extends keyof BarFigureStyle>(key: K, value: BarFigureStyle[K]): void {
    this.figureStyleChange.emit({ ...this.figureStyle, bar: { ...this.bar, [key]: value } });
  }

  setScatter<K extends keyof ScatterFigureStyle>(key: K, value: ScatterFigureStyle[K]): void {
    this.figureStyleChange.emit({ ...this.figureStyle, scatter: { ...this.scatter, [key]: value } });
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
}
