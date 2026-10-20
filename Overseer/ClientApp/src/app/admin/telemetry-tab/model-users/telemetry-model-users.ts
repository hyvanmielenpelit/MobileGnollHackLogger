/**
 * The AI Telemetry *Usage by user* dialog's logic: one model's usage rows grouped into one card per
 * user (and thinking level when shown separately), the thinking-level filter, the user-name search,
 * the sort, each card's share of the scope total and its rank, the status line, and the view
 * settings remembered per browser. Pure TypeScript, no Angular.
 */

import type { AiTelemetryModelUserRowDto } from '../../../services/admin.service';
import { formatThinkingLevel } from '../../../utils/model-badge-format.util';
import { formatInteger } from '../../benchmark/shared/provider-availability/provider-availability';
import { searchTerms } from '../../../shared/model-picker/model-picker-view';
import { parseServerUtcDate } from '../../../utils/date.util';
import {
  BreakdownMetrics,
  BreakdownSortDirection,
  MetricSums,
  addMetricRow,
  breakdownDirectionLabel,
  compareThinkingLevels,
  deriveMetrics,
  emptyMetricSums
} from '../telemetry-breakdown';

/** One card: a user, and a thinking level when levels are shown separately. */
export interface ModelUserCard extends BreakdownMetrics {
  key: string;
  userId: string;
  /** The user's current name; null when the account is gone (shown as *Unknown user*). */
  userName: string | null;
  /** The level, lower-cased ("" none sent, null not recorded); undefined while levels are grouped. */
  thinkingLevel: string | null | undefined;
  /** The distinct recorded levels of the rows summed, lower-cased, in the catalog's order. */
  thinkingLevels: string[];
  /** The latest request of the rows summed. */
  lastRequestUtc: string | null;
  /** Prompt plus output tokens. */
  totalTokens: number;
}

export const UNKNOWN_USER_TEXT = 'Unknown user';

/** The level as the dialog compares it: trimmed and lower-cased; "" none sent, null not recorded. */
function normalizedLevel(level: string | null | undefined): string | null {
  return level === null || level === undefined ? null : level.trim().toLowerCase();
}

/** A server timestamp in ms; NaN when unreadable. */
export function utcTime(utc: string): number {
  return parseServerUtcDate(utc).getTime();
}

function laterOf(a: string | null, b: string | null | undefined): string | null {
  if (!b) {
    return a;
  }
  if (!a) {
    return b;
  }
  return utcTime(b) > utcTime(a) ? b : a;
}

/** Groups the rows by user, and by thinking level when `separateThinkingLevels`; each card sums its rows. */
export function buildModelUserCards(
  rows: readonly AiTelemetryModelUserRowDto[],
  options: { separateThinkingLevels: boolean }
): ModelUserCard[] {
  const groups = new Map<string, {
    head: Pick<ModelUserCard, 'key' | 'userId' | 'userName' | 'thinkingLevel'>;
    levels: Set<string>;
    last: string | null;
    sums: MetricSums;
  }>();

  for (const row of rows) {
    const level = normalizedLevel(row.thinkingLevel);
    const key = options.separateThinkingLevels
      ? `${row.userId}|${level === null ? 'unrecorded' : '=' + level}`
      : row.userId;
    let group = groups.get(key);
    if (!group) {
      group = {
        head: {
          key,
          userId: row.userId,
          userName: row.userName?.trim() || null,
          thinkingLevel: options.separateThinkingLevels ? level : undefined
        },
        levels: new Set<string>(),
        last: null,
        sums: emptyMetricSums()
      };
      groups.set(key, group);
    } else {
      group.head.userName ??= row.userName?.trim() || null;
    }
    if (level) {
      group.levels.add(level);
    }
    group.last = laterOf(group.last, row.lastRequestUtc);
    addMetricRow(group.sums, row);
  }

  return Array.from(groups.values(), g => {
    const metrics = deriveMetrics(g.sums);
    return {
      ...g.head,
      thinkingLevels: [...g.levels].sort(compareThinkingLevels),
      lastRequestUtc: g.last,
      ...metrics,
      totalTokens: metrics.promptTokens + metrics.outputTokens
    };
  });
}

/** The sums of the cards, and their derived figures. */
export function sumModelUserCards(cards: readonly ModelUserCard[]): BreakdownMetrics {
  const sums = emptyMetricSums();
  for (const card of cards) {
    addMetricRow(sums, card);
  }
  return deriveMetrics(sums);
}

// --- Thinking-level filter -------------------------------------------------------------------

/** `*` all levels, `=level` one recorded level, `none` none sent, `unrecorded` not recorded. */
export type ThinkingLevelOptionValue = string;

export interface ThinkingLevelOption {
  value: ThinkingLevelOptionValue;
  label: string;
}

export const ALL_LEVELS = '*';
const NONE_SENT = 'none';
const NOT_RECORDED = 'unrecorded';

/** The option of a level as recorded: undefined is every level. */
export function thinkingLevelOptionOf(level: string | null | undefined): ThinkingLevelOptionValue {
  if (level === undefined) {
    return ALL_LEVELS;
  }
  const normalized = normalizedLevel(level);
  if (normalized === null) {
    return NOT_RECORDED;
  }
  return normalized === '' ? NONE_SENT : '=' + normalized;
}

/** The level an option filters to: `*` every level, "" none sent, null not recorded, else the level. */
export function thinkingLevelFilterValue(option: ThinkingLevelOptionValue): string | null {
  if (option === NONE_SENT) {
    return '';
  }
  if (option === NOT_RECORDED) {
    return null;
  }
  return option.startsWith('=') ? option.slice(1) : ALL_LEVELS;
}

/** *All levels*, each recorded level in the catalog's order, then *None sent* and *Not recorded* when present. */
export function thinkingLevelOptions(rows: readonly AiTelemetryModelUserRowDto[]): ThinkingLevelOption[] {
  const levels = new Set<string>();
  let noneSent = false;
  let notRecorded = false;
  for (const row of rows) {
    const level = normalizedLevel(row.thinkingLevel);
    if (level === null) {
      notRecorded = true;
    } else if (level === '') {
      noneSent = true;
    } else {
      levels.add(level);
    }
  }
  const options: ThinkingLevelOption[] = [{ value: ALL_LEVELS, label: 'All levels' }];
  for (const level of [...levels].sort(compareThinkingLevels)) {
    options.push({ value: '=' + level, label: formatThinkingLevel(level) });
  }
  if (noneSent) {
    options.push({ value: NONE_SENT, label: 'None sent' });
  }
  if (notRecorded) {
    options.push({ value: NOT_RECORDED, label: 'Not recorded' });
  }
  return options;
}

/** The rows of the option's level, in a new array; applied before grouping. */
export function filterByThinkingLevel(
  rows: readonly AiTelemetryModelUserRowDto[],
  option: ThinkingLevelOptionValue
): AiTelemetryModelUserRowDto[] {
  const value = thinkingLevelFilterValue(option);
  if (value === ALL_LEVELS) {
    return [...rows];
  }
  return rows.filter(row => normalizedLevel(row.thinkingLevel) === value);
}

// --- Search ----------------------------------------------------------------------------------

/** The cards, in a new array and the same order, whose user name contains every search term. */
export function searchModelUsers(cards: readonly ModelUserCard[], search: string): ModelUserCard[] {
  const terms = searchTerms(search);
  if (terms.length === 0) {
    return [...cards];
  }
  return cards.filter(card => {
    const name = (card.userName ?? '').toLowerCase();
    return terms.every(term => name.includes(term));
  });
}

// --- Sort ------------------------------------------------------------------------------------

export type ModelUserSortField =
  'cost' | 'requests' | 'uncached' | 'cacheRead' | 'cacheCreated' | 'output' | 'totalTokens'
  | 'cacheHit' | 'duration' | 'lastRequest' | 'name';

export type ModelUserSortKind = 'number' | 'text' | 'date';

export interface ModelUserSort {
  field: ModelUserSortField;
  direction: BreakdownSortDirection;
}

export interface ModelUserSortFieldInfo {
  field: ModelUserSortField;
  label: string;
  kind: ModelUserSortKind;
}

export const DEFAULT_MODEL_USER_SORT: ModelUserSort = Object.freeze({ field: 'cost', direction: 'desc' }) as ModelUserSort;

/** The sort fields in the Sort popover's order, labeled as the cards label the metrics. */
export const MODEL_USER_SORT_FIELDS: readonly ModelUserSortFieldInfo[] = [
  { field: 'cost', label: 'Est. cost', kind: 'number' },
  { field: 'requests', label: 'Requests', kind: 'number' },
  { field: 'uncached', label: 'Input (uncached)', kind: 'number' },
  { field: 'cacheRead', label: 'Cache read', kind: 'number' },
  { field: 'cacheCreated', label: 'Cache created', kind: 'number' },
  { field: 'output', label: 'Output', kind: 'number' },
  { field: 'totalTokens', label: 'Total tokens', kind: 'number' },
  { field: 'cacheHit', label: 'Cache hit', kind: 'number' },
  { field: 'duration', label: 'Avg duration', kind: 'number' },
  { field: 'lastRequest', label: 'Last request', kind: 'date' },
  { field: 'name', label: 'User name', kind: 'text' }
];

const SORT_DIRECTIONS: readonly BreakdownSortDirection[] = ['desc', 'asc'];

export function modelUserSortField(field: ModelUserSortField): ModelUserSortFieldInfo {
  return MODEL_USER_SORT_FIELDS.find(f => f.field === field) ?? MODEL_USER_SORT_FIELDS[0];
}

/** `Highest first` / `Lowest first`, `A–Z` / `Z–A`, or `Newest first` / `Oldest first`. */
export function modelUserDirectionLabel(kind: ModelUserSortKind, direction: BreakdownSortDirection): string {
  if (kind === 'date') {
    return direction === 'desc' ? 'Newest first' : 'Oldest first';
  }
  return breakdownDirectionLabel(kind, direction);
}

/** `Est. cost, highest first`; `User name, A–Z`. */
export function modelUserSortLabel(sort: ModelUserSort): string {
  const info = modelUserSortField(sort.field);
  const direction = modelUserDirectionLabel(info.kind, sort.direction);
  return `${info.label}, ${info.kind === 'text' ? direction : direction.toLowerCase()}`;
}

export function isDefaultModelUserSort(sort: ModelUserSort): boolean {
  return sort.field === DEFAULT_MODEL_USER_SORT.field && sort.direction === DEFAULT_MODEL_USER_SORT.direction;
}

const collator = new Intl.Collator('en', { numeric: true, sensitivity: 'base' });

/** User name A–Z, a missing name last; then thinking level in the catalog's order; then the key. */
function compareTies(a: ModelUserCard, b: ModelUserCard, byName = true): number {
  return (byName ? compareNames(a, b, 1) : 0)
    || compareThinkingLevels(a.thinkingLevel, b.thinkingLevel)
    || a.key.localeCompare(b.key);
}

/** By name in the direction of `sign`; a missing name last in both directions. */
function compareNames(a: ModelUserCard, b: ModelUserCard, sign: number): number {
  if (a.userName === null || b.userName === null) {
    return (a.userName === null ? 1 : 0) - (b.userName === null ? 1 : 0);
  }
  return sign * collator.compare(a.userName, b.userName);
}

/** A card's value for a numeric or date field; null where the card shows no figure for it. */
function sortValue(card: ModelUserCard, field: ModelUserSortField): number | null {
  switch (field) {
    case 'requests': return card.requests;
    case 'uncached': return card.uncachedInputTokens;
    case 'cacheRead': return card.cacheReadTokens;
    case 'cacheCreated': return card.cacheCreationTokens;
    case 'output': return card.outputTokens;
    case 'totalTokens': return card.totalTokens;
    case 'cost': return card.requests > 0 && card.pricedRequests === 0 ? null : card.estimatedCost;
    case 'cacheHit': return card.cacheHitRatio;
    case 'duration': return card.avgDurationMs;
    case 'lastRequest': {
      const time = card.lastRequestUtc ? utcTime(card.lastRequestUtc) : NaN;
      return Number.isNaN(time) ? null : time;
    }
    default: return null;
  }
}

/**
 * The cards in a new array, sorted by the field in the direction. A card without a figure for the
 * field (an unpriced cost, no cache hit or duration, a missing name under *User name*) sorts last
 * in both directions. Ties go by user name A–Z, thinking level and key.
 */
export function sortModelUserCards(cards: readonly ModelUserCard[], sort: ModelUserSort): ModelUserCard[] {
  const sign = sort.direction === 'asc' ? 1 : -1;
  let compare: (a: ModelUserCard, b: ModelUserCard) => number;
  if (sort.field === 'name') {
    compare = (a, b) => compareNames(a, b, sign) || compareTies(a, b, false);
  } else {
    const field = sort.field;
    compare = (a, b) => {
      const va = sortValue(a, field);
      const vb = sortValue(b, field);
      if (va === null || vb === null) {
        return (va === null ? 1 : 0) - (vb === null ? 1 : 0) || compareTies(a, b);
      }
      return sign * (va - vb) || compareTies(a, b);
    };
  }
  return [...cards].sort(compare);
}

/** Each card's 1-based position in the sorted scope, by key. */
export function modelUserRanks(sortedScope: readonly ModelUserCard[]): Map<string, number> {
  return new Map(sortedScope.map((card, i) => [card.key, i + 1]));
}

// --- Shares ----------------------------------------------------------------------------------

/** The additive figures a share is taken of. */
export type ModelUserShareField = 'cost' | 'requests' | 'uncached' | 'cacheRead' | 'cacheCreated' | 'output' | 'totalTokens';

export type ModelUserShares = Record<ModelUserShareField, number | null>;

export const SHARE_FIELD_LABELS: Readonly<Record<ModelUserShareField, string>> = {
  cost: 'est. cost',
  requests: 'requests',
  uncached: 'input (uncached)',
  cacheRead: 'cache read',
  cacheCreated: 'cache created',
  output: 'output',
  totalTokens: 'total tokens'
};

const SHARE_FIELDS = Object.keys(SHARE_FIELD_LABELS) as ModelUserShareField[];

/** The field the share bar and *Top user* show: the sort field when it is additive, else requests. */
export function shareFieldOf(sort: ModelUserSort): ModelUserShareField {
  return (SHARE_FIELDS as string[]).includes(sort.field) ? sort.field as ModelUserShareField : 'requests';
}

function share(part: number, whole: number): number | null {
  return whole > 0 ? part / whole : null;
}

/**
 * The card's share of the scope total for each additive figure; null where the total is 0, and for
 * the cost also while the card has requests but none of them priced.
 */
export function modelUserShares(card: ModelUserCard, total: BreakdownMetrics): ModelUserShares {
  const unpriced = card.requests > 0 && card.pricedRequests === 0;
  return {
    cost: unpriced ? null : share(card.estimatedCost, total.estimatedCost),
    requests: share(card.requests, total.requests),
    uncached: share(card.uncachedInputTokens, total.uncachedInputTokens),
    cacheRead: share(card.cacheReadTokens, total.cacheReadTokens),
    cacheCreated: share(card.cacheCreationTokens, total.cacheCreationTokens),
    output: share(card.outputTokens, total.outputTokens),
    totalTokens: share(card.promptTokens + card.outputTokens, total.promptTokens + total.outputTokens)
  };
}

// --- Status ----------------------------------------------------------------------------------

export interface ModelUsersStatusInput {
  /** Cards in the scope: the level filter applied, before the search. */
  scope: number;
  /** Cards matching the search. */
  matching: number;
  /** Cards rendered by the batch. */
  shown: number;
  search: string;
  /** Requests of the scope. */
  requests: number;
}

/**
 * `Showing 50 of 312 users · 12,345 requests`; searching, `Showing 3 of 312 users` or `No users
 * match "bob".`; empty with no user in the scope.
 */
export function modelUsersStatus(input: ModelUsersStatusInput): string {
  const { scope, matching, shown, requests } = input;
  if (scope === 0) {
    return '';
  }
  const search = input.search.trim();
  const users = (n: number): string => `${formatInteger(n)} ${n === 1 ? 'user' : 'users'}`;
  if (search) {
    if (matching === 0) {
      return `No users match "${search}".`;
    }
    return shown < matching
      ? `Showing ${formatInteger(shown)} of ${formatInteger(matching)} matching users`
      : `Showing ${formatInteger(matching)} of ${users(scope)}`;
  }
  const requestText = `${formatInteger(requests)} ${requests === 1 ? 'request' : 'requests'}`;
  if (shown < scope) {
    return `Showing ${formatInteger(shown)} of ${users(scope)} · ${requestText}`;
  }
  return scope === 1 ? `One user · ${requestText}` : `Showing ${users(scope)} · ${requestText}`;
}

// --- View settings ---------------------------------------------------------------------------

/** Where the dialog's view settings are kept, per browser; apart from the main list's. */
export const TELEMETRY_MODEL_USERS_STORAGE_KEY = 'overseer.admin.telemetry.modelUsers';

const VIEW_VERSION = 1;

/** The search and the thinking-level filter are not part of the view. */
export interface TelemetryModelUsersView {
  separateThinkingLevels: boolean;
  sort: ModelUserSort;
}

/** Levels grouped, cost highest first. */
export function defaultModelUsersView(): TelemetryModelUsersView {
  return { separateThinkingLevels: false, sort: DEFAULT_MODEL_USER_SORT };
}

function readSort(value: unknown): ModelUserSort {
  if (!value || typeof value !== 'object') {
    return DEFAULT_MODEL_USER_SORT;
  }
  const { field, direction } = value as Record<string, unknown>;
  const info = MODEL_USER_SORT_FIELDS.find(f => f.field === field);
  const dir = SORT_DIRECTIONS.find(d => d === direction);
  return info && dir ? { field: info.field, direction: dir } : DEFAULT_MODEL_USER_SORT;
}

/**
 * The stored view; the default when nothing is stored, the record is unreadable, of another
 * version, or storage is unavailable. An invalid field falls back to its default alone.
 */
export function readModelUsersView(storage: Storage | null = safeLocalStorage()): TelemetryModelUsersView {
  const fallback = defaultModelUsersView();
  try {
    const raw = storage?.getItem(TELEMETRY_MODEL_USERS_STORAGE_KEY);
    if (!raw) {
      return fallback;
    }
    const parsed = JSON.parse(raw) as Partial<Record<keyof TelemetryModelUsersView | 'version', unknown>> | null;
    if (!parsed || typeof parsed !== 'object' || parsed.version !== VIEW_VERSION) {
      return fallback;
    }
    const separateThinkingLevels = typeof parsed.separateThinkingLevels === 'boolean'
      ? parsed.separateThinkingLevels
      : fallback.separateThinkingLevels;
    return { separateThinkingLevels, sort: readSort(parsed.sort) };
  } catch {
    return fallback;
  }
}

/** Stores the view as `{ version: 1, separateThinkingLevels, sort }`; a failure is ignored. */
export function writeModelUsersView(view: TelemetryModelUsersView, storage: Storage | null = safeLocalStorage()): void {
  try {
    storage?.setItem(TELEMETRY_MODEL_USERS_STORAGE_KEY, JSON.stringify({
      version: VIEW_VERSION,
      separateThinkingLevels: view.separateThinkingLevels,
      sort: { field: view.sort.field, direction: view.sort.direction }
    }));
  } catch {
    // Storage full or unavailable: the view is simply not remembered.
  }
}

function safeLocalStorage(): Storage | null {
  try {
    return typeof localStorage === 'undefined' ? null : localStorage;
  } catch {
    return null;
  }
}
