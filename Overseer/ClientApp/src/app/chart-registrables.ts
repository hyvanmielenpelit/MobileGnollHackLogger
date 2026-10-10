/**
 * The application-wide chart.js registrable list.
 *
 * chart.js v4 registers nothing by itself: a controller, element, scale or plugin that is not in
 * this list is absent from the registry, and the first chart that asks for it throws at render
 * time rather than failing to compile. `AdminComponent` hands the list to `provideCharts`, which is
 * what `BaseChartDirective` registers from, while code that builds a `Chart` itself calls
 * `registerAppCharts()`, so **every** charted surface in the client draws from this one list and
 * any chart type used anywhere must appear here.
 *
 * It sits apart from any feature so that no feature has cause to keep a list of its own. Only the
 * lazily loaded admin page imports it: importing it from the bootstrap would put chart.js in the
 * initial bundle.
 */

import {
  BarController,
  BarElement,
  CategoryScale,
  Chart,
  Legend,
  LinearScale,
  LineController,
  LineElement,
  LogarithmicScale,
  PointElement,
  ScatterController,
  SubTitle,
  Title,
  Tooltip,
} from 'chart.js';

/** Every chart.js registrable the client's charts need. `AdminComponent` provides this. */
export const APP_CHART_REGISTRABLES = [
  ScatterController,
  LineController,
  BarController,
  PointElement,
  LineElement,
  BarElement,
  CategoryScale,
  LinearScale,
  LogarithmicScale,
  Legend,
  Tooltip,
  Title,
  SubTitle,
];

/**
 * Registers `APP_CHART_REGISTRABLES` with chart.js. For code that constructs a `Chart` itself,
 * where no `BaseChartDirective` has registered them; registering again is a no-op.
 */
export function registerAppCharts(): void {
  Chart.register(...APP_CHART_REGISTRABLES);
}
