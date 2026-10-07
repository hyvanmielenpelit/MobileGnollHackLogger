import { ChangeDetectionStrategy, Component, Input, OnChanges } from '@angular/core';
import { BaseChartDirective } from 'ng2-charts';
import type { ChartConfiguration, ChartType, Plugin } from 'chart.js';

import { CcFigure } from '../chat-consistency-charts';

/**
 * One Chat Consistency chart as a `<figure>`: the title and the takeaway sentence as its caption,
 * the chart (a canvas named by the takeaway), the list of its markers, and a *Show data* disclosure
 * holding the same numbers as a real table.
 */
@Component({
  selector: 'app-cc-chart-figure',
  standalone: true,
  imports: [BaseChartDirective],
  templateUrl: './cc-chart-figure.component.html',
  styleUrls: ['./cc-chart-figure.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class CcChartFigureComponent implements OnChanges {
  @Input({ required: true }) figure!: CcFigure;
  /** Unique in the document; the caption's and the table's ids derive from it. */
  @Input({ required: true }) figureId = '';

  // The figure's line configuration, erased to the directive's default chart typing.
  chartType: ChartType = 'line';
  chartData: ChartConfiguration['data'] | null = null;
  chartOptions: ChartConfiguration['options'] = {};
  chartPlugins: Plugin[] = [];

  ngOnChanges(): void {
    const config = this.figure?.config ?? null;
    this.chartData = config ? (config.data as unknown as ChartConfiguration['data']) : null;
    this.chartOptions = config ? (config.options as unknown as ChartConfiguration['options']) : {};
    this.chartPlugins = config ? (config.plugins as unknown as Plugin[]) : [];
  }
}
