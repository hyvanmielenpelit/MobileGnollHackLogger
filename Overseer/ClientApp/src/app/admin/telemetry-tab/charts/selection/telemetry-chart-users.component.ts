import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { LoadingIndicatorComponent } from '../../../../shared/loading-indicator/loading-indicator.component';
import { TelemetrySortPopoverComponent } from '../../sort-popover/telemetry-sort-popover.component';
import {
  CHART_USER_NOUN,
  CHART_USER_SORT_FIELDS,
  ChartUserRow,
  ChartUserSortField,
  DEFAULT_CHART_USER_SORT,
  absentUserRow,
  readChartUsersView,
  usageParts,
  writeChartUsersView
} from './telemetry-chart-filters';
import { ChartSelectionConfig, TelemetryChartSelectionBase } from './telemetry-chart-selection.base';

/**
 * The charts dialog's Users chooser: the users of the scope with their usage (the 1,000 with the most
 * requests at most), a search by user name, the sort, and the choice of the users to chart. The sort
 * is remembered per browser.
 */
@Component({
  selector: 'app-telemetry-chart-users',
  imports: [TelemetrySortPopoverComponent, LoadingIndicatorComponent],
  templateUrl: './telemetry-chart-users.component.html',
  styleUrl: './telemetry-chart-users.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class TelemetryChartUsersComponent extends TelemetryChartSelectionBase<ChartUserRow, ChartUserSortField> {
  /** Whether the rows are only the users with the most requests. */
  readonly truncated = input(false);
  /** The users with usage in the scope, before the cap. */
  readonly total = input(0);

  protected override readonly config: ChartSelectionConfig<ChartUserRow, ChartUserSortField> = {
    idPrefix: 'tl-sel-users',
    noun: CHART_USER_NOUN,
    sortFields: CHART_USER_SORT_FIELDS,
    defaultSort: DEFAULT_CHART_USER_SORT,
    readSort: () => readChartUsersView().sort,
    writeSort: sort => writeChartUsersView({ sort }),
    absentRow: absentUserRow
  };

  protected override truncatedList(): boolean {
    return this.truncated();
  }

  protected override totalCount(): number {
    return Math.max(this.total(), this.rows().length);
  }

  /** *12 requests · est. $0.25 · last active 2026-10-13*. */
  metaParts(row: ChartUserRow): string[] {
    return usageParts(row, 'last active');
  }
}
