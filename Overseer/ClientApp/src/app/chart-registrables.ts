/**
 * The single chart.js registrable list for the cross-model comparison figures.
 *
 * It lives in its own module, apart from the chart core, so that `app.config.ts` and the component
 * spec can both register exactly the same set without the bootstrap pulling in the chart core. The
 * scatter, line and bar controllers, their elements, the three scales and the shared plugins are
 * all reachable from at least one of the six figures.
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

/** Every chart.js registrable the six figures need. `app.config.ts` spreads this. */
export const MODEL_COMPARISON_REGISTRABLES = [
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
