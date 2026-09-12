/**
 * The application-wide chart.js registrable list.
 *
 * chart.js v4 registers nothing by itself: a controller, element, scale or plugin that is not in
 * this list is absent from the registry, and the first chart that asks for it throws at render
 * time rather than failing to compile. `app.config.ts` hands the list to `provideCharts`, which is
 * what `BaseChartDirective` registers from, so **every** charted surface in the client draws from
 * this one list and any chart type used anywhere must appear here.
 *
 * It sits at the application level, apart from any feature and apart from the chart core, so that
 * the bootstrap does not pull a feature's chart module in and so that no feature has cause to keep
 * a list of its own.
 */

import {
  BarController,
  BarElement,
  CategoryScale,
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

/** Every chart.js registrable the client's charts need. `app.config.ts` spreads this. */
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
