/**
 * The AI Telemetry tab's breakdown: the server's usage rows grouped into cards by key source, user,
 * provider, model and role group (and thinking level when shown separately), with their sums, the
 * ratios derived from those sums, the search, provider filter and sort over the cards, and the view
 * settings remembered per browser. Pure TypeScript, no Angular.
 */

import type { AiTelemetryKeySource, AiUsageBreakdownRowDto, AiUsageRoleGroup } from '../../services/admin.service';
import {
  ModelPickerProviderFacet,
  providerKey,
  searchTerms,
  thinkingLevelRank
} from '../../shared/model-picker/model-picker-view';
import { formatThinkingLevel } from '../../utils/model-badge-format.util';
import { formatCost } from '../benchmark/batteries/battery.models';
import { formatInteger } from '../benchmark/shared/provider-availability/provider-availability';

/** Every role group, in display order. */
export const ROLE_GROUPS: readonly AiUsageRoleGroup[] = ['chat', 'title', 'benchmark', 'other'];

/** The role groups the breakdown shows by default; Other is listed only while it has rows. */
export const NAMED_ROLE_GROUPS: readonly AiUsageRoleGroup[] = ['chat', 'title', 'benchmark'];

export const ROLE_GROUP_LABELS: Readonly<Record<AiUsageRoleGroup, string>> = {
  chat: 'Chat',
  title: 'Title Generation',
  benchmark: 'Benchmark',
  other: 'Other'
};

export const KEY_SOURCES: readonly AiTelemetryKeySource[] = ['system', 'user', 'both'];

export const KEY_SOURCE_LABELS: Readonly<Record<AiTelemetryKeySource, string>> = {
  system: 'System AI configurations',
  user: 'User keys',
  both: 'Both'
};

export function roleGroupLabel(group: AiUsageRoleGroup): string {
  return ROLE_GROUP_LABELS[group] ?? group;
}

const ROLE_BADGE_CLASSES: Readonly<Record<AiUsageRoleGroup, string>> = {
  chat: 'badge-role-chat',
  title: 'badge-role-title',
  benchmark: 'badge-role-benchmark',
  other: 'badge-role-other'
};

/** The `status-badge` variant of a role group's badge. */
export function roleBadgeClass(group: AiUsageRoleGroup): string {
  return ROLE_BADGE_CLASSES[group] ?? ROLE_BADGE_CLASSES.other;
}

/** `Not recorded` for null, `None sent` for an empty level, else the level itself. */
export function thinkingLevelLabel(level: string | null | undefined): string {
  if (level === null || level === undefined) {
    return 'Not recorded';
  }
  return level === '' ? 'None sent' : level;
}

/** The sums a card or the total carries, and the figures derived from them. */
export interface BreakdownMetrics {
  /** The rows summed. */
  rowCount: number;
  requests: number;
  promptTokens: number;
  uncachedInputTokens: number;
  cacheReadTokens: number;
  cacheCreationTokens: number;
  outputTokens: number;
  durationSumMs: number;
  durationCount: number;
  estimatedCost: number;
  pricedRequests: number;
  validAttempts: number;
  providerErrors: number;
  unattributedErrors: number;
  ourSideFailures: number;
  /** Cache read ÷ prompt; null with no prompt tokens. */
  cacheHitRatio: number | null;
  /** Duration sum ÷ duration count; null at count 0. */
  avgDurationMs: number | null;
  /** Requests without a price. */
  unpricedRequests: number;
  /** Priced ÷ requests; null at no request. */
  pricedShare: number | null;
}

/** One card: a key source, user, provider, model, role group and (when separate) thinking level. */
export interface BreakdownCard extends BreakdownMetrics {
  key: string;
  keySource: 'system' | 'user';
  userId: string | null;
  userName: string | null;
  provider: string;
  modelId: string;
  modelName: string | null;
  roleGroup: AiUsageRoleGroup;
  /** The level as recorded ("" none sent, null not recorded); undefined while levels are grouped. */
  thinkingLevel: string | null | undefined;
  /** The distinct recorded levels of the rows summed, lower-cased, in the catalog's order; none sent and not recorded left out. */
  thinkingLevels: string[];
}

export interface BreakdownOptions {
  roles: readonly AiUsageRoleGroup[];
  separateThinkingLevels: boolean;
  /** Whitespace-separated terms that must all match; default none. */
  search?: string;
  /** Lower-cased provider keys, any of which matches; default none, which is every provider. */
  providers?: readonly string[];
  /** Default `DEFAULT_BREAKDOWN_SORT`. */
  sort?: BreakdownSort;
}

export interface Breakdown {
  /** Every card of the selected roles, before the search and the provider filter; default order. */
  all: BreakdownCard[];
  /** The cards shown: filtered, then sorted. */
  cards: BreakdownCard[];
  /** The sums over every card shown. */
  total: BreakdownMetrics;
}

// --- Sort ------------------------------------------------------------------------------------

export type BreakdownSortField =
  'requests' | 'cost' | 'uncached' | 'cacheRead' | 'cacheCreated' | 'output'
  | 'cacheHit' | 'duration' | 'providerErrors' | 'name' | 'provider';

export type BreakdownSortDirection = 'desc' | 'asc';

export interface BreakdownSort {
  field: BreakdownSortField;
  direction: BreakdownSortDirection;
}

export const DEFAULT_BREAKDOWN_SORT: BreakdownSort = Object.freeze({ field: 'requests', direction: 'desc' }) as BreakdownSort;

export type BreakdownSortKind = 'number' | 'text';

export interface BreakdownSortFieldInfo {
  field: BreakdownSortField;
  label: string;
  kind: BreakdownSortKind;
}

/** The sort fields in the Sort popover's order, labeled as the cards label the metrics. */
export const BREAKDOWN_SORT_FIELDS: readonly BreakdownSortFieldInfo[] = [
  { field: 'requests', label: 'Requests', kind: 'number' },
  { field: 'cost', label: 'Est. cost', kind: 'number' },
  { field: 'uncached', label: 'Input (uncached)', kind: 'number' },
  { field: 'cacheRead', label: 'Cache read', kind: 'number' },
  { field: 'cacheCreated', label: 'Cache created', kind: 'number' },
  { field: 'output', label: 'Output', kind: 'number' },
  { field: 'cacheHit', label: 'Cache hit', kind: 'number' },
  { field: 'duration', label: 'Avg duration', kind: 'number' },
  { field: 'providerErrors', label: 'Provider errors', kind: 'number' },
  { field: 'name', label: 'Model name', kind: 'text' },
  { field: 'provider', label: 'Provider', kind: 'text' }
];

const SORT_DIRECTIONS: readonly BreakdownSortDirection[] = ['desc', 'asc'];

export function breakdownSortField(field: BreakdownSortField): BreakdownSortFieldInfo {
  return BREAKDOWN_SORT_FIELDS.find(f => f.field === field) ?? BREAKDOWN_SORT_FIELDS[0];
}

/** `Highest first` / `Lowest first` for a figure, `A–Z` / `Z–A` for a name. */
export function breakdownDirectionLabel(kind: BreakdownSortKind, direction: BreakdownSortDirection): string {
  if (kind === 'text') {
    return direction === 'asc' ? 'A–Z' : 'Z–A';
  }
  return direction === 'desc' ? 'Highest first' : 'Lowest first';
}

/** `Requests, highest first`. */
export function breakdownSortLabel(sort: BreakdownSort): string {
  const info = breakdownSortField(sort.field);
  const direction = breakdownDirectionLabel(info.kind, sort.direction);
  return `${info.label}, ${info.kind === 'text' ? direction : direction.toLowerCase()}`;
}

export function isDefaultBreakdownSort(sort: BreakdownSort): boolean {
  return sort.field === DEFAULT_BREAKDOWN_SORT.field && sort.direction === DEFAULT_BREAKDOWN_SORT.direction;
}

/** The additive part of `BreakdownMetrics`. */
export type MetricSums = Omit<BreakdownMetrics, 'cacheHitRatio' | 'avgDurationMs' | 'unpricedRequests' | 'pricedShare'>;

/** What `addMetricRow` adds: a usage row or a sum of rows; a row without attempt figures adds none. */
export interface MetricRow {
  /** Present on a sum: the rows it holds; a row counts as one. */
  rowCount?: number;
  requests: number;
  promptTokens: number;
  uncachedInputTokens: number;
  cacheReadTokens: number;
  cacheCreationTokens: number;
  outputTokens: number;
  durationSumMs: number;
  durationCount: number;
  estimatedCost: number;
  pricedRequests: number;
  validAttempts?: number;
  providerErrors?: number;
  unattributedErrors?: number;
  ourSideFailures?: number;
}

export function emptyMetricSums(): MetricSums {
  return {
    rowCount: 0, requests: 0, promptTokens: 0, uncachedInputTokens: 0, cacheReadTokens: 0,
    cacheCreationTokens: 0, outputTokens: 0, durationSumMs: 0, durationCount: 0, estimatedCost: 0,
    pricedRequests: 0, validAttempts: 0, providerErrors: 0, unattributedErrors: 0, ourSideFailures: 0
  };
}

export function addMetricRow(sums: MetricSums, row: MetricRow): void {
  sums.rowCount += row.rowCount ?? 1;
  sums.requests += row.requests;
  sums.promptTokens += row.promptTokens;
  sums.uncachedInputTokens += row.uncachedInputTokens;
  sums.cacheReadTokens += row.cacheReadTokens;
  sums.cacheCreationTokens += row.cacheCreationTokens;
  sums.outputTokens += row.outputTokens;
  sums.durationSumMs += row.durationSumMs;
  sums.durationCount += row.durationCount;
  sums.estimatedCost += row.estimatedCost;
  sums.pricedRequests += row.pricedRequests;
  sums.validAttempts += row.validAttempts ?? 0;
  sums.providerErrors += row.providerErrors ?? 0;
  sums.unattributedErrors += row.unattributedErrors ?? 0;
  sums.ourSideFailures += row.ourSideFailures ?? 0;
}

export function deriveMetrics(sums: MetricSums): BreakdownMetrics {
  return {
    ...sums,
    cacheHitRatio: sums.promptTokens > 0 ? sums.cacheReadTokens / sums.promptTokens : null,
    avgDurationMs: sums.durationCount > 0 ? sums.durationSumMs / sums.durationCount : null,
    unpricedRequests: Math.max(0, sums.requests - sums.pricedRequests),
    pricedShare: sums.requests > 0 ? sums.pricedRequests / sums.requests : null
  };
}

/** The name a card is titled and sorted by: the model's name, else its id. */
export function cardTitle(card: Pick<BreakdownCard, 'modelName' | 'modelId'>): string {
  return card.modelName?.trim() || card.modelId;
}

function groupKey(row: AiUsageBreakdownRowDto, separateThinkingLevels: boolean): string {
  const parts = [row.keySource, row.keySource === 'user' ? row.userId ?? '' : '', row.provider, row.modelId, row.roleGroup];
  if (separateThinkingLevels) {
    const level = row.thinkingLevel;
    parts.push(level === null || level === undefined ? '\u0000' : '=' + level);
  }
  return parts.join('|');
}

/**
 * Groups the rows of the selected role groups into cards and sums each; a row with no request is
 * kept. `all` is in the default order: requests (most first), then title, then thinking level in
 * the catalog's order (unset last, not recorded before none sent), then the card key. `cards` is
 * `all` narrowed by the search and the provider filter, then sorted; `total` sums `cards`.
 */
export function buildBreakdown(rows: readonly AiUsageBreakdownRowDto[], options: BreakdownOptions): Breakdown {
  const roles = new Set(options.roles);
  const groups = new Map<string, { head: Omit<BreakdownCard, keyof BreakdownMetrics | 'thinkingLevels'>; levels: Set<string>; sums: MetricSums }>();

  for (const row of rows) {
    if (!roles.has(row.roleGroup)) {
      continue;
    }
    const key = groupKey(row, options.separateThinkingLevels);
    let group = groups.get(key);
    if (!group) {
      group = {
        head: {
          key,
          keySource: row.keySource,
          userId: row.keySource === 'user' ? row.userId ?? null : null,
          userName: row.keySource === 'user' ? row.userName?.trim() || null : null,
          provider: row.provider,
          modelId: row.modelId,
          modelName: row.modelName?.trim() || null,
          roleGroup: row.roleGroup,
          thinkingLevel: options.separateThinkingLevels ? row.thinkingLevel ?? null : undefined
        },
        levels: new Set<string>(),
        sums: emptyMetricSums()
      };
      groups.set(key, group);
    } else {
      group.head.modelName ??= row.modelName?.trim() || null;
      if (row.keySource === 'user') {
        group.head.userName ??= row.userName?.trim() || null;
      }
    }
    const level = (row.thinkingLevel ?? '').trim().toLowerCase();
    if (level) {
      group.levels.add(level);
    }
    addMetricRow(group.sums, row);
  }

  const all: BreakdownCard[] = Array.from(groups.values(), g => ({
    ...g.head,
    thinkingLevels: [...g.levels].sort((a, b) => thinkingLevelRank(a) - thinkingLevelRank(b) || collator.compare(a, b)),
    ...deriveMetrics(g.sums)
  }));
  all.sort(compareByDefault);

  const providers = effectiveBreakdownProviders(options.providers ?? [], breakdownProviderFacets(all));
  const cards = sortBreakdownCards(
    filterBreakdownCards(all, { search: options.search ?? '', providers }),
    options.sort ?? DEFAULT_BREAKDOWN_SORT);

  const total = emptyMetricSums();
  for (const card of cards) {
    addMetricRow(total, card);
  }
  return { all, cards, total: deriveMetrics(total) };
}

const collator = new Intl.Collator('en', { numeric: true, sensitivity: 'base' });

/** The catalog's order; an unset level last, and of the two unset ones not recorded before none sent; then by name. */
export function compareThinkingLevels(a: string | null | undefined, b: string | null | undefined): number {
  const recorded = (level: string | null | undefined): number => level === null || level === undefined ? 0 : 1;
  return thinkingLevelRank(a) - thinkingLevelRank(b)
    || recorded(a) - recorded(b)
    || collator.compare(a ?? '', b ?? '');
}

/** Title A–Z (unless `byTitle` is false), then thinking level, then the card key. */
function compareTies(a: BreakdownCard, b: BreakdownCard, byTitle = true): number {
  return (byTitle ? collator.compare(cardTitle(a), cardTitle(b)) : 0)
    || compareThinkingLevels(a.thinkingLevel, b.thinkingLevel)
    || a.key.localeCompare(b.key);
}

function compareByDefault(a: BreakdownCard, b: BreakdownCard): number {
  return b.requests - a.requests || compareTies(a, b);
}

/** A card's value for a numeric sort field; null where the card shows no figure for it. */
function numericSortValue(card: BreakdownCard, field: BreakdownSortField): number | null {
  switch (field) {
    case 'requests': return card.requests;
    case 'uncached': return card.uncachedInputTokens;
    case 'cacheRead': return card.cacheReadTokens;
    case 'cacheCreated': return card.cacheCreationTokens;
    case 'output': return card.outputTokens;
    case 'cost': return card.requests > 0 && card.pricedRequests === 0 ? null : card.estimatedCost;
    case 'cacheHit': return card.cacheHitRatio;
    case 'duration': return card.avgDurationMs;
    case 'providerErrors':
      return card.roleGroup === 'benchmark' || card.validAttempts <= 0 ? null : card.providerErrors / card.validAttempts;
    default: return null;
  }
}

/**
 * The cards in a new array, sorted by the field in the direction. A card without a figure for the
 * field (an unpriced cost, no cache hit or duration, provider errors measured in GnollBench or
 * without attempts) sorts last in both directions. Ties go by title A–Z, thinking level and key.
 */
export function sortBreakdownCards(cards: readonly BreakdownCard[], sort: BreakdownSort): BreakdownCard[] {
  const sign = sort.direction === 'asc' ? 1 : -1;
  let compare: (a: BreakdownCard, b: BreakdownCard) => number;
  if (sort.field === 'name') {
    compare = (a, b) => sign * collator.compare(cardTitle(a), cardTitle(b)) || compareTies(a, b, false);
  } else if (sort.field === 'provider') {
    compare = (a, b) => sign * collator.compare(a.provider, b.provider) || compareTies(a, b);
  } else {
    const field = sort.field;
    compare = (a, b) => {
      const va = numericSortValue(a, field);
      const vb = numericSortValue(b, field);
      if (va === null || vb === null) {
        return (va === null ? 1 : 0) - (vb === null ? 1 : 0) || compareTies(a, b);
      }
      return sign * (va - vb) || compareTies(a, b);
    };
  }
  return [...cards].sort(compare);
}

// --- Search and provider filter --------------------------------------------------------------

/** What the search matches: the title, the model id, the provider and a user key's user name, lower-cased. */
export function breakdownSearchText(card: BreakdownCard): string {
  const parts = [cardTitle(card), card.modelId, card.provider];
  if (card.keySource === 'user' && card.userName) {
    parts.push(card.userName);
  }
  return parts.join(' ').toLowerCase();
}

/**
 * The cards, in a new array, whose search text contains every search term and whose provider is
 * one of `providers`; no provider means every provider.
 */
export function filterBreakdownCards(
  cards: readonly BreakdownCard[],
  filter: { search: string; providers: readonly string[] }
): BreakdownCard[] {
  const terms = searchTerms(filter.search);
  const providers = new Set(filter.providers);
  return cards.filter(card => {
    if (providers.size > 0 && !providers.has(providerKey(card.provider))) {
      return false;
    }
    if (terms.length === 0) {
      return true;
    }
    const text = breakdownSearchText(card);
    return terms.every(term => text.includes(term));
  });
}

/** One facet per provider of the cards, spelled as its first card spells it, counting cards, by name. */
export function breakdownProviderFacets(cards: readonly BreakdownCard[]): ModelPickerProviderFacet[] {
  const byKey = new Map<string, ModelPickerProviderFacet>();
  for (const card of cards) {
    const key = providerKey(card.provider);
    if (!key) {
      continue;
    }
    const facet = byKey.get(key);
    if (facet) {
      facet.count++;
    } else {
      byKey.set(key, { provider: card.provider.trim(), key, count: 1 });
    }
  }
  return [...byKey.values()].sort((a, b) => collator.compare(a.provider, b.provider));
}

/** The selected providers that some card has; a stored provider no card has never hides everything. */
export function effectiveBreakdownProviders(selected: readonly string[], facets: readonly ModelPickerProviderFacet[]): string[] {
  const present = new Set(facets.map(f => f.key));
  return selected.filter(key => present.has(key));
}

/** Requests per role group over every row, whatever the selection. */
export function roleCounts(rows: readonly AiUsageBreakdownRowDto[]): Record<AiUsageRoleGroup, number> {
  const counts: Record<AiUsageRoleGroup, number> = { chat: 0, title: 0, benchmark: 0, other: 0 };
  for (const row of rows) {
    if (row.roleGroup in counts) {
      counts[row.roleGroup] += row.requests;
    }
  }
  return counts;
}

/** Estimated cost per role group over every row, whatever the selection. */
export function roleCosts(rows: readonly AiUsageBreakdownRowDto[]): Record<AiUsageRoleGroup, number> {
  const costs: Record<AiUsageRoleGroup, number> = { chat: 0, title: 0, benchmark: 0, other: 0 };
  for (const row of rows) {
    if (row.roleGroup in costs) {
      costs[row.roleGroup] += row.estimatedCost;
    }
  }
  return costs;
}

// --- Card text -------------------------------------------------------------------------------

/** A badge of a breakdown card's badge row; `prefix` is read before the text by assistive technology only. */
export interface TelemetryCardBadge {
  cssClass: string;
  prefix: string;
  text: string;
}

/** A metric's value, and the qualifier line under it when there is one. */
export interface TelemetryMetricText {
  value: string;
  note: string | null;
  /** The value is a placeholder rather than a figure. */
  muted: boolean;
}

/**
 * Shown separately, the card's one level, or None sent or Not recorded in a neutral badge, since
 * those are not levels; grouped, one badge per recorded level, none for the others.
 */
export function thinkingBadgesOf(card: Pick<BreakdownCard, 'thinkingLevel' | 'thinkingLevels'>): TelemetryCardBadge[] {
  const prefix = 'thinking level';
  if (card.thinkingLevel === undefined) {
    return card.thinkingLevels.map(level => ({ cssClass: 'thinking-badge', prefix, text: formatThinkingLevel(level) }));
  }
  if (card.thinkingLevel === null || card.thinkingLevel === '') {
    return [{ cssClass: 'config-badge', prefix, text: thinkingLevelLabel(card.thinkingLevel) }];
  }
  return [{ cssClass: 'thinking-badge', prefix, text: formatThinkingLevel(card.thinkingLevel.trim().toLowerCase()) }];
}

/** The estimated cost, with `3 unpriced` while part of the requests has no price. */
export function costMetricText(m: BreakdownMetrics): TelemetryMetricText {
  if (m.requests > 0 && m.pricedRequests === 0) {
    return { value: '—', note: `${formatInteger(m.unpricedRequests)} unpriced`, muted: true };
  }
  return {
    value: formatCost(m.estimatedCost),
    note: m.unpricedRequests > 0 ? `${formatInteger(m.unpricedRequests)} unpriced` : null,
    muted: false
  };
}

// --- View settings ---------------------------------------------------------------------------

/** Where the breakdown's view settings and the Keys filter are kept, per browser. */
export const TELEMETRY_BREAKDOWN_STORAGE_KEY = 'overseer.admin.telemetry.breakdown';

const VIEW_VERSION = 1;

/** The search text is not part of the view: an old search never hides cards on a later visit. */
export interface TelemetryBreakdownView {
  roles: AiUsageRoleGroup[];
  separateThinkingLevels: boolean;
  keys: AiTelemetryKeySource;
  sort: BreakdownSort;
  /** Lower-cased provider keys; empty means every provider. */
  providers: string[];
  /** Whether the Date Range and Keys bar is collapsed to its summary line. */
  toolbarCollapsed: boolean;
}

/** The three named role groups, levels grouped, system configurations, most requests first, every provider, the bar expanded. */
export function defaultBreakdownView(): TelemetryBreakdownView {
  return {
    roles: [...NAMED_ROLE_GROUPS], separateThinkingLevels: false, keys: 'system',
    sort: DEFAULT_BREAKDOWN_SORT, providers: [], toolbarCollapsed: false
  };
}

function readSort(value: unknown): BreakdownSort {
  if (!value || typeof value !== 'object') {
    return DEFAULT_BREAKDOWN_SORT;
  }
  const { field, direction } = value as Record<string, unknown>;
  const info = BREAKDOWN_SORT_FIELDS.find(f => f.field === field);
  const dir = SORT_DIRECTIONS.find(d => d === direction);
  return info && dir ? { field: info.field, direction: dir } : DEFAULT_BREAKDOWN_SORT;
}

function readProviders(value: unknown): string[] {
  if (!Array.isArray(value)) {
    return [];
  }
  return [...new Set(value
    .filter((p): p is string => typeof p === 'string')
    .map(p => providerKey(p))
    .filter(p => p !== ''))];
}

/**
 * The stored view; the default when nothing is stored, the record is unreadable, of another
 * version, or storage is unavailable. Unknown role groups are dropped, and an invalid field falls
 * back to its default alone; a record written before a field existed reads it as its default.
 */
export function readBreakdownView(storage: Storage | null = safeLocalStorage()): TelemetryBreakdownView {
  const fallback = defaultBreakdownView();
  try {
    const raw = storage?.getItem(TELEMETRY_BREAKDOWN_STORAGE_KEY);
    if (!raw) {
      return fallback;
    }
    const parsed = JSON.parse(raw) as Partial<Record<keyof TelemetryBreakdownView | 'version', unknown>> | null;
    if (!parsed || typeof parsed !== 'object' || parsed.version !== VIEW_VERSION) {
      return fallback;
    }
    const roles = Array.isArray(parsed.roles)
      ? ROLE_GROUPS.filter(group => (parsed.roles as unknown[]).includes(group))
      : fallback.roles;
    const separateThinkingLevels = typeof parsed.separateThinkingLevels === 'boolean'
      ? parsed.separateThinkingLevels
      : fallback.separateThinkingLevels;
    const keys = KEY_SOURCES.find(k => k === parsed.keys) ?? fallback.keys;
    const sort = readSort(parsed.sort);
    const providers = readProviders(parsed.providers);
    const toolbarCollapsed = typeof parsed.toolbarCollapsed === 'boolean' ? parsed.toolbarCollapsed : fallback.toolbarCollapsed;
    return { roles, separateThinkingLevels, keys, sort, providers, toolbarCollapsed };
  } catch {
    return fallback;
  }
}

/**
 * Stores the view as `{ version: 1, roles, separateThinkingLevels, keys, sort, providers,
 * toolbarCollapsed }`; a failure is ignored.
 */
export function writeBreakdownView(view: TelemetryBreakdownView, storage: Storage | null = safeLocalStorage()): void {
  try {
    storage?.setItem(TELEMETRY_BREAKDOWN_STORAGE_KEY, JSON.stringify({
      version: VIEW_VERSION,
      roles: view.roles,
      separateThinkingLevels: view.separateThinkingLevels,
      keys: view.keys,
      sort: { field: view.sort.field, direction: view.sort.direction },
      providers: view.providers,
      toolbarCollapsed: view.toolbarCollapsed
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
