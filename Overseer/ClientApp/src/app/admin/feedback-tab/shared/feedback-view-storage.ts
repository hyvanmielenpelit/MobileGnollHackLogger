/**
 * What the Feedback tab remembers per browser, per sub-tab: the view (Sort by, the facets, the search,
 * the date range and the export options) and the selection. Every access is behind `try/catch`, and
 * an unreadable or wrong-version record reads as none.
 */

import type { FeedbackTimeSpan } from '../../admin-page.store';
import {
  FEEDBACK_EXPORT_MAX_OCCURRENCES_PER_ISSUE,
  type FeedbackExportFormat
} from '../../../services/feedback-transfer.service';

export type FeedbackSource = 'overseer' | 'gnollbench';

/** Empty dates mean no bound. */
export interface FeedbackRange { preset: FeedbackTimeSpan; start: string; end: string; }

/**
 * The remembered export options. `occurrencesPerIssue` is a whole number from 1 to 1,000, or `all`. `format` absent
 * reads `yaml`, as does any value but `markdown` read from storage.
 */
export interface FeedbackExportOptions {
  occurrencesPerIssue: number | 'all';
  includeQuestionText: boolean;
  format?: FeedbackExportFormat;
}

/** A count of occurrences per issue an export may ask for: a whole number from 1 to 1,000. */
export function isFeedbackOccurrenceCount(value: unknown): value is number {
  return typeof value === 'number' && Number.isInteger(value) && value >= 1 && value <= FEEDBACK_EXPORT_MAX_OCCURRENCES_PER_ISSUE;
}

/** The export format the options choose: `markdown` only when they say so, `yaml` otherwise. */
export function feedbackExportFormat(options: Pick<FeedbackExportOptions, 'format'> | null | undefined): FeedbackExportFormat {
  return options?.format === 'markdown' ? 'markdown' : 'yaml';
}

export interface FeedbackViewRecord {
  version: 1;
  sort: string;
  /** Facet key → selected values. */
  filters: Record<string, string[]>;
  search: string;
  range: FeedbackRange;
  exportOptions?: FeedbackExportOptions;
}

/** The most issues a selection holds. */
export const FEEDBACK_SELECTION_CAP = 500;

const VIEW_KEYS: Readonly<Record<FeedbackSource, string>> = {
  overseer: 'overseer.admin.feedback.overseer.view',
  gnollbench: 'overseer.admin.feedback.gnollbench.view'
};

const OVERSEER_SELECTION_KEY = 'overseer.admin.feedback.overseer.selection';
const GNOLLBENCH_SELECTION_KEY = 'overseer.admin.feedback.gnollbench.selection';

/** Where the Overseer sub-tab's Sort by was kept before the sub-tabs; read once, never written or removed. */
const LEGACY_OVERSEER_SORT_KEY = 'overseer.admin.telemetry.feedback.view';

const TIME_SPANS: readonly FeedbackTimeSpan[] = ['all', '7d', '30d', '90d', '1y', 'custom'];
const DAY = /^\d{4}-\d{2}-\d{2}$/;

/** All time: no bound. */
export function defaultFeedbackRange(): FeedbackRange {
  return { preset: 'all', start: '', end: '' };
}

function readJson(key: string): unknown {
  try {
    const raw = localStorage.getItem(key);
    return raw ? JSON.parse(raw) as unknown : null;
  } catch {
    return null;
  }
}

function writeJson(key: string, value: unknown): void {
  try {
    localStorage.setItem(key, JSON.stringify(value));
  } catch {
    // Storage full or unavailable: the setting is simply not remembered.
  }
}

function isObject(value: unknown): value is Record<string, unknown> {
  return value !== null && typeof value === 'object' && !Array.isArray(value);
}

function readRange(value: unknown): FeedbackRange {
  if (!isObject(value) || !TIME_SPANS.includes(value['preset'] as FeedbackTimeSpan)) return defaultFeedbackRange();
  const day = (raw: unknown): string => typeof raw === 'string' && DAY.test(raw) ? raw : '';
  return { preset: value['preset'] as FeedbackTimeSpan, start: day(value['start']), end: day(value['end']) };
}

function readFilters(value: unknown): Record<string, string[]> {
  const filters: Record<string, string[]> = {};
  if (!isObject(value)) return filters;
  for (const [key, values] of Object.entries(value)) {
    if (Array.isArray(values)) filters[key] = values.filter((v): v is string => typeof v === 'string');
  }
  return filters;
}

function readExportOptions(value: unknown): FeedbackExportOptions | undefined {
  if (!isObject(value)) return undefined;
  const stored = value['occurrencesPerIssue'];
  const occurrences = stored === 'all' ? 'all' : isFeedbackOccurrenceCount(stored) ? stored : null;
  if (occurrences === null) return undefined;
  const options: FeedbackExportOptions = {
    occurrencesPerIssue: occurrences,
    includeQuestionText: value['includeQuestionText'] !== false
  };
  const format = value['format'];
  if (format === 'yaml' || format === 'markdown') options.format = format;
  return options;
}

/** The sort the Overseer sub-tab kept before the sub-tabs, or null. */
function readLegacyOverseerSort(): string | null {
  const parsed = readJson(LEGACY_OVERSEER_SORT_KEY);
  return isObject(parsed) && parsed['version'] === 1 && typeof parsed['sort'] === 'string' ? parsed['sort'] : null;
}

/**
 * A sub-tab's remembered view, or null. For the Overseer sub-tab with nothing stored yet, the sort it
 * kept before the sub-tabs comes back as a view with that sort alone.
 */
export function loadFeedbackView(source: FeedbackSource): FeedbackViewRecord | null {
  const parsed = readJson(VIEW_KEYS[source]);
  if (parsed === null && source === 'overseer') {
    const sort = readLegacyOverseerSort();
    return sort === null ? null : { version: 1, sort, filters: {}, search: '', range: defaultFeedbackRange() };
  }
  if (!isObject(parsed) || parsed['version'] !== 1) return null;
  const record: FeedbackViewRecord = {
    version: 1,
    sort: typeof parsed['sort'] === 'string' ? parsed['sort'] : '',
    filters: readFilters(parsed['filters']),
    search: typeof parsed['search'] === 'string' ? parsed['search'] : '',
    range: readRange(parsed['range'])
  };
  const exportOptions = readExportOptions(parsed['exportOptions']);
  if (exportOptions) record.exportOptions = exportOptions;
  return record;
}

export function saveFeedbackView(source: FeedbackSource, record: FeedbackViewRecord): void {
  writeJson(VIEW_KEYS[source], record);
}

/** The remembered Overseer selection: issue ids, at most 500. */
export function loadOverseerSelection(): number[] {
  const parsed = readJson(OVERSEER_SELECTION_KEY);
  if (!isObject(parsed) || parsed['version'] !== 1 || !Array.isArray(parsed['ids'])) return [];
  const ids = parsed['ids'].filter((id): id is number => typeof id === 'number' && Number.isInteger(id) && id > 0);
  return [...new Set(ids)].slice(0, FEEDBACK_SELECTION_CAP);
}

/** Keeps at most 500. */
export function saveOverseerSelection(ids: readonly number[]): void {
  writeJson(OVERSEER_SELECTION_KEY, { version: 1, ids: [...new Set(ids)].slice(0, FEEDBACK_SELECTION_CAP) });
}

/** The remembered GnollBench selection: fingerprints, at most 500. */
export function loadGnollBenchSelection(): string[] {
  const parsed = readJson(GNOLLBENCH_SELECTION_KEY);
  if (!isObject(parsed) || parsed['version'] !== 1 || !Array.isArray(parsed['fingerprints'])) return [];
  const fingerprints = parsed['fingerprints'].filter((f): f is string => typeof f === 'string' && f !== '');
  return [...new Set(fingerprints)].slice(0, FEEDBACK_SELECTION_CAP);
}

/** Keeps at most 500. */
export function saveGnollBenchSelection(fingerprints: readonly string[]): void {
  writeJson(GNOLLBENCH_SELECTION_KEY, { version: 1, fingerprints: [...new Set(fingerprints)].slice(0, FEEDBACK_SELECTION_CAP) });
}
