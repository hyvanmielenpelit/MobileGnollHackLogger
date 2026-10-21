import { ChangeDetectionStrategy, Component } from '@angular/core';
import { LoadingIndicatorComponent } from '../../../../shared/loading-indicator/loading-indicator.component';
import { TelemetrySortPopoverComponent } from '../../sort-popover/telemetry-sort-popover.component';
import {
  CHART_GROUP_NOUN,
  CHART_GROUP_SORT_FIELDS,
  ChartGroupRow,
  ChartGroupSortField,
  DEFAULT_CHART_GROUP_SORT,
  NO_USAGE_TEXT,
  absentGroupRow,
  membersText,
  readChartGroupsView,
  usageParts,
  writeChartGroupsView
} from './telemetry-chart-filters';
import { ChartSelectionConfig, TelemetryChartSelectionBase } from './telemetry-chart-selection.base';

/**
 * The charts dialog's Groups chooser: the groups with their members' usage in the scope, *No group*
 * for the users in none, a search by group name, the sort, and the choice of the groups to chart.
 * The sort is remembered per browser.
 */
@Component({
  selector: 'app-telemetry-chart-groups',
  imports: [TelemetrySortPopoverComponent, LoadingIndicatorComponent],
  templateUrl: './telemetry-chart-groups.component.html',
  styleUrl: './telemetry-chart-groups.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class TelemetryChartGroupsComponent extends TelemetryChartSelectionBase<ChartGroupRow, ChartGroupSortField> {
  protected override readonly config: ChartSelectionConfig<ChartGroupRow, ChartGroupSortField> = {
    idPrefix: 'tl-sel-groups',
    noun: CHART_GROUP_NOUN,
    sortFields: CHART_GROUP_SORT_FIELDS,
    defaultSort: DEFAULT_CHART_GROUP_SORT,
    readSort: () => readChartGroupsView().sort,
    writeSort: sort => writeChartGroupsView({ sort }),
    absentRow: absentGroupRow
  };

  /** *12 members · 40 requests · est. $0.25 · last active 2026-10-13*; *No usage in this scope* for an absent group. */
  metaParts(row: ChartGroupRow): string[] {
    return row.absent ? [NO_USAGE_TEXT] : [membersText(row.memberCount), ...usageParts(row, 'last active')];
  }
}
