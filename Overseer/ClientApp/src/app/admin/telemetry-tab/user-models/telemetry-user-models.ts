/**
 * The AI Telemetry *Usage by model* dialog's logic: one user's usage rows filtered by role group, key
 * source and thinking level, grouped into one card per key source, provider, model and role group
 * (and thinking level when shown separately), the model search, the sort, each card's share of the
 * scope total and its rank, the summary, the status line, and the view settings remembered per
 * browser. Pure TypeScript, no Angular.
 */

import type { AiTelemetryUserModelRowDto, AiUsageRoleGroup } from '../../../services/admin.service';
import { formatThinkingLevel } from '../../../utils/model-badge-format.util';
import { formatInteger, formatPercent } from '../../benchmark/shared/provider-availability/provider-availability';
import { searchTerms } from '../../../shared/model-picker/model-picker-view';
import {
  BreakdownMetrics,
  BreakdownSortDirection,
  KEY_SOURCE_LABELS,
  MetricSums,
  ROLE_GROUPS,
  addMetricRow,
  breakdownDirectionLabel,
  cardTitle,
  compareThinkingLevels,
  deriveMetrics,
  emptyMetricSums,
  roleGroupLabel
} from '../telemetry-breakdown';
import {
  ALL_LEVELS,
  ModelUserShareField,
  SHARE_FIELD_LABELS,
  ThinkingLevelOption,
  ThinkingLevelOptionValue,
  modelUserShares,
  thinkingLevelFilterValue,
  utcTime
} from '../model-users/telemetry-model-users';

/** One card: a key source, provider, model and role group, and a thinking level when levels are shown separately. */
export interface UserModelCard extends BreakdownMetrics {
  key: string;
  keySource: 'system' | 'user';
  provider: string;
  modelId: string;
  /** The catalog's name, else the retired entry's; null when neither is known. */
  modelName: string | null;
  /** The name the card is titled and searched by: the model's name, else its id. */
  title: string;
  roleGroup: AiUsageRoleGroup;
  /** The level, lower-cased ("" none sent, null not recorded); undefined while levels are grouped. */
  thinkingLevel: string | null | undefined;
  /** The distinct recorded levels of the rows summed, lower-cased, in the catalog's order. */
  thinkingLevels: string[];
  /** The latest request of the rows summed. */
  lastRequestUtc: string | null;
  /** Prompt plus output tokens. */
  totalTokens: number;
}

/** The level as the dialog compares it: trimmed and lower-cased; "" none sent, null not recorded or absent. */
function normalizedLevel(level: string | null | undefined): string | null {
  return level === null || level === undefined ? null : level.trim().toLowerCase();
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

/**
 * Groups the rows by key source, provider, model and role group, and by thinking level when
 * `separateThinkingLevels`; each card sums its rows.
 */
export function buildUserModelCards(
  rows: readonly AiTelemetryUserModelRowDto[],
  options: { separateThinkingLevels: boolean }
): UserModelCard[] {
  const groups = new Map<string, {
    head: Pick<UserModelCard, 'key' | 'keySource' | 'provider' | 'modelId' | 'modelName' | 'roleGroup' | 'thinkingLevel'>;
    levels: Set<string>;
    last: string | null;
    sums: MetricSums;
  }>();

  for (const row of rows) {
    const level = normalizedLevel(row.thinkingLevel);
    const base = `${row.keySource}|${row.provider}|${row.modelId}|${row.roleGroup}`;
    const key = options.separateThinkingLevels
      ? `${base}|${level === null ? 'unrecorded' : '=' + level}`
      : base;
    let group = groups.get(key);
    if (!group) {
      group = {
        head: {
          key,
          keySource: row.keySource,
          provider: row.provider,
          modelId: row.modelId,
          modelName: row.modelName?.trim() || null,
          roleGroup: row.roleGroup,
          thinkingLevel: options.separateThinkingLevels ? level : undefined
        },
        levels: new Set<string>(),
        last: null,
        sums: emptyMetricSums()
      };
      groups.set(key, group);
    } else {
      group.head.modelName ??= row.modelName?.trim() || null;
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
      title: cardTitle(g.head),
      thinkingLevels: [...g.levels].sort(compareThinkingLevels),
      lastRequestUtc: g.last,
      ...metrics,
      totalTokens: metrics.promptTokens + metrics.outputTokens
    };
  });
}

/** The sums of the cards, and their derived figures. */
export function sumUserModelCards(cards: readonly UserModelCard[]): BreakdownMetrics {
  const sums = emptyMetricSums();
  for (const card of cards) {
    addMetricRow(sums, card);
  }
  return deriveMetrics(sums);
}

/** The distinct models among the cards: provider and model id, whatever the role group, key or level. */
export function distinctModelCount(cards: readonly UserModelCard[]): number {
  return new Set(cards.map(card => `${card.provider}|${card.modelId}`)).size;
}

// --- Filters ---------------------------------------------------------------------------------

export type UserModelRoleFilter = 'all' | AiUsageRoleGroup;
export type UserModelKeyFilter = 'all' | 'system' | 'user';

/** The role, key and thinking-level filters; applied to the rows before grouping. */
export interface UserModelRowFilter {
  role: UserModelRoleFilter;
  key: UserModelKeyFilter;
  /** `*` all levels, `=level` one recorded level, `none` none sent, `unrecorded` not recorded. */
  level: ThinkingLevelOptionValue;
}

export const ALL_USER_MODEL_ROWS: UserModelRowFilter = Object.freeze({ role: 'all', key: 'all', level: ALL_LEVELS }) as UserModelRowFilter;

export interface UserModelRoleOption {
  value: UserModelRoleFilter;
  label: string;
}

export interface UserModelKeyOption {
  value: UserModelKeyFilter;
  label: string;
}

/** The rows the filters keep, in a new array. */
export function filterUserModelRows(
  rows: readonly AiTelemetryUserModelRowDto[],
  filter: UserModelRowFilter
): AiTelemetryUserModelRowDto[] {
  const level = thinkingLevelFilterValue(filter.level);
  return rows.filter(row =>
    (filter.role === 'all' || row.roleGroup === filter.role)
    && (filter.key === 'all' || row.keySource === filter.key)
    && (level === ALL_LEVELS || normalizedLevel(row.thinkingLevel) === level));
}

/** *All roles*, then each role group the rows hold, in the Chat, Title Generation, Benchmark, Other order. */
export function roleOptions(rows: readonly AiTelemetryUserModelRowDto[]): UserModelRoleOption[] {
  const present = new Set(rows.map(row => row.roleGroup));
  return [
    { value: 'all', label: 'All roles' },
    ...ROLE_GROUPS.filter(group => present.has(group)).map(group => ({ value: group, label: roleGroupLabel(group) }))
  ];
}

/** *All keys*, then *System AI configurations* and *User keys* where the rows hold them. */
export function keyOptions(rows: readonly AiTelemetryUserModelRowDto[]): UserModelKeyOption[] {
  const present = new Set(rows.map(row => row.keySource));
  const options: UserModelKeyOption[] = [{ value: 'all', label: 'All keys' }];
  for (const source of ['system', 'user'] as const) {
    if (present.has(source)) {
      options.push({ value: source, label: KEY_SOURCE_LABELS[source] });
    }
  }
  return options;
}

/** *All levels*, each recorded level in the catalog's order, then *None sent* and *Not recorded* when present. */
export function userModelLevelOptions(rows: readonly AiTelemetryUserModelRowDto[]): ThinkingLevelOption[] {
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
    options.push({ value: 'none', label: 'None sent' });
  }
  if (notRecorded) {
    options.push({ value: 'unrecorded', label: 'Not recorded' });
  }
  return options;
}

// --- Search ----------------------------------------------------------------------------------

/** The cards, in a new array and the same order, whose title, model id or provider together contain every search term. */
export function searchUserModels(cards: readonly UserModelCard[], search: string): UserModelCard[] {
  const terms = searchTerms(search);
  if (terms.length === 0) {
    return [...cards];
  }
  return cards.filter(card => {
    const text = `${card.title} ${card.modelId} ${card.provider}`.toLowerCase();
    return terms.every(term => text.includes(term));
  });
}

// --- Sort ------------------------------------------------------------------------------------

export type UserModelSortField =
  'cost' | 'requests' | 'uncached' | 'cacheRead' | 'cacheCreated' | 'output' | 'totalTokens'
  | 'cacheHit' | 'duration' | 'lastRequest' | 'name' | 'provider';

export type UserModelSortKind = 'number' | 'text' | 'date';

export interface UserModelSort {
  field: UserModelSortField;
  direction: BreakdownSortDirection;
}

export interface UserModelSortFieldInfo {
  field: UserModelSortField;
  label: string;
  kind: UserModelSortKind;
}

export const DEFAULT_USER_MODEL_SORT: UserModelSort = Object.freeze({ field: 'cost', direction: 'desc' }) as UserModelSort;

/** The sort fields in the Sort popover's order, labeled as the cards label the metrics. */
export const USER_MODEL_SORT_FIELDS: readonly UserModelSortFieldInfo[] = [
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
  { field: 'name', label: 'Model name', kind: 'text' },
  { field: 'provider', label: 'Provider', kind: 'text' }
];

const SORT_DIRECTIONS: readonly BreakdownSortDirection[] = ['desc', 'asc'];

export function userModelSortField(field: UserModelSortField): UserModelSortFieldInfo {
  return USER_MODEL_SORT_FIELDS.find(f => f.field === field) ?? USER_MODEL_SORT_FIELDS[0];
}

/** `Highest first` / `Lowest first`, `A–Z` / `Z–A`, or `Newest first` / `Oldest first`. */
export function userModelDirectionLabel(kind: UserModelSortKind, direction: BreakdownSortDirection): string {
  if (kind === 'date') {
    return direction === 'desc' ? 'Newest first' : 'Oldest first';
  }
  return breakdownDirectionLabel(kind, direction);
}

/** `Est. cost, highest first`; `Model name, A–Z`. */
export function userModelSortLabel(sort: UserModelSort): string {
  const info = userModelSortField(sort.field);
  const direction = userModelDirectionLabel(info.kind, sort.direction);
  return `${info.label}, ${info.kind === 'text' ? direction : direction.toLowerCase()}`;
}

export function isDefaultUserModelSort(sort: UserModelSort): boolean {
  return sort.field === DEFAULT_USER_MODEL_SORT.field && sort.direction === DEFAULT_USER_MODEL_SORT.direction;
}

const collator = new Intl.Collator('en', { numeric: true, sensitivity: 'base' });

const KEY_SOURCE_ORDER: Readonly<Record<'system' | 'user', number>> = { system: 0, user: 1 };

function roleRank(group: AiUsageRoleGroup): number {
  const index = ROLE_GROUPS.indexOf(group);
  return index < 0 ? ROLE_GROUPS.length : index;
}

/** Title A–Z (unless `byTitle` is false), then role group order, thinking level, key source (system first) and key. */
function compareTies(a: UserModelCard, b: UserModelCard, byTitle = true): number {
  return (byTitle ? collator.compare(a.title, b.title) : 0)
    || roleRank(a.roleGroup) - roleRank(b.roleGroup)
    || compareThinkingLevels(a.thinkingLevel, b.thinkingLevel)
    || KEY_SOURCE_ORDER[a.keySource] - KEY_SOURCE_ORDER[b.keySource]
    || a.key.localeCompare(b.key);
}

/** A card's value for a numeric or date field; null where the card shows no figure for it. */
function sortValue(card: UserModelCard, field: UserModelSortField): number | null {
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

/** By a text field in the direction of `sign`; an empty value last in both directions. */
function compareText(a: string, b: string, sign: number): number {
  if (!a || !b) {
    return (a ? 0 : 1) - (b ? 0 : 1);
  }
  return sign * collator.compare(a, b);
}

/**
 * The cards in a new array, sorted by the field in the direction. A card without a figure for the
 * field (an unpriced cost, no cache hit, duration or last request) sorts last in both directions.
 * Ties go by title A–Z, role group, thinking level, key source and key.
 */
export function sortUserModelCards(cards: readonly UserModelCard[], sort: UserModelSort): UserModelCard[] {
  const sign = sort.direction === 'asc' ? 1 : -1;
  let compare: (a: UserModelCard, b: UserModelCard) => number;
  if (sort.field === 'name') {
    compare = (a, b) => compareText(a.title, b.title, sign) || compareTies(a, b, false);
  } else if (sort.field === 'provider') {
    compare = (a, b) => compareText(a.provider.trim(), b.provider.trim(), sign) || compareTies(a, b);
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
export function userModelRanks(sortedScope: readonly UserModelCard[]): Map<string, number> {
  return new Map(sortedScope.map((card, i) => [card.key, i + 1]));
}

// --- Shares ----------------------------------------------------------------------------------

const SHARE_FIELDS = Object.keys(SHARE_FIELD_LABELS) as ModelUserShareField[];

/** The field the share bar and *Top model* show: the sort field when it is additive, else requests. */
export function userModelShareFieldOf(sort: UserModelSort): ModelUserShareField {
  return (SHARE_FIELDS as string[]).includes(sort.field) ? sort.field as ModelUserShareField : 'requests';
}

/**
 * `Claude 5 Opus · Benchmark · 41% of est. cost`: rank 1 of the sorted scope with its role and share;
 * without the share where it has none; `—` for an empty scope.
 */
export function topUserModelText(
  sortedScope: readonly UserModelCard[],
  total: BreakdownMetrics,
  shareField: ModelUserShareField
): string {
  const top = sortedScope[0];
  if (!top) {
    return '—';
  }
  const head = `${top.title} · ${roleGroupLabel(top.roleGroup)}`;
  const share = modelUserShares(top, total)[shareField];
  return share === null ? head : `${head} · ${formatPercent(share)} of ${SHARE_FIELD_LABELS[shareField]}`;
}

// --- Status ----------------------------------------------------------------------------------

export interface UserModelsStatusInput {
  /** Cards in the scope: the role, key and level filters applied, before the search. */
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
 * `Showing 12 entries · 1,033 requests`, `Showing 5 of 12 entries · 1,033 requests`; searching,
 * `Showing 3 of 12 entries` or `No entries match "opus".`; empty with no entry in the scope.
 */
export function userModelsStatus(input: UserModelsStatusInput): string {
  const { scope, matching, shown, requests } = input;
  if (scope === 0) {
    return '';
  }
  const search = input.search.trim();
  const entries = (n: number): string => `${formatInteger(n)} ${n === 1 ? 'entry' : 'entries'}`;
  if (search) {
    if (matching === 0) {
      return `No entries match "${search}".`;
    }
    return shown < matching
      ? `Showing ${formatInteger(shown)} of ${formatInteger(matching)} matching entries`
      : `Showing ${formatInteger(matching)} of ${entries(scope)}`;
  }
  const requestText = `${formatInteger(requests)} ${requests === 1 ? 'request' : 'requests'}`;
  if (shown < scope) {
    return `Showing ${formatInteger(shown)} of ${entries(scope)} · ${requestText}`;
  }
  return scope === 1 ? `One entry · ${requestText}` : `Showing ${entries(scope)} · ${requestText}`;
}

// --- View settings ---------------------------------------------------------------------------

/** Where the dialog's view settings are kept, per browser; apart from the main list's and the Usage by user dialog's. */
export const TELEMETRY_USER_MODELS_STORAGE_KEY = 'overseer.admin.telemetry.userModels';

const VIEW_VERSION = 1;

/** The search and the role, key and thinking-level filters are not part of the view. */
export interface TelemetryUserModelsView {
  separateThinkingLevels: boolean;
  sort: UserModelSort;
}

/** Levels grouped, cost highest first. */
export function defaultUserModelsView(): TelemetryUserModelsView {
  return { separateThinkingLevels: false, sort: DEFAULT_USER_MODEL_SORT };
}

function readSort(value: unknown): UserModelSort {
  if (!value || typeof value !== 'object') {
    return DEFAULT_USER_MODEL_SORT;
  }
  const { field, direction } = value as Record<string, unknown>;
  const info = USER_MODEL_SORT_FIELDS.find(f => f.field === field);
  const dir = SORT_DIRECTIONS.find(d => d === direction);
  return info && dir ? { field: info.field, direction: dir } : DEFAULT_USER_MODEL_SORT;
}

/**
 * The stored view; the default when nothing is stored, the record is unreadable, of another
 * version, or storage is unavailable. An invalid field falls back to its default alone.
 */
export function readUserModelsView(storage: Storage | null = safeLocalStorage()): TelemetryUserModelsView {
  const fallback = defaultUserModelsView();
  try {
    const raw = storage?.getItem(TELEMETRY_USER_MODELS_STORAGE_KEY);
    if (!raw) {
      return fallback;
    }
    const parsed = JSON.parse(raw) as Partial<Record<keyof TelemetryUserModelsView | 'version', unknown>> | null;
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
export function writeUserModelsView(view: TelemetryUserModelsView, storage: Storage | null = safeLocalStorage()): void {
  try {
    storage?.setItem(TELEMETRY_USER_MODELS_STORAGE_KEY, JSON.stringify({
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
