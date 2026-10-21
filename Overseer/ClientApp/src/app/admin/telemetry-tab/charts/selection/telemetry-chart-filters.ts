/**
 * The charts dialog's Models, Users and Groups choosers: their rows and keys, the search, the
 * provider and key-source toggles, the sort with nulls last, the batches, the caps, the effective
 * filter the dialog sends, the selection band's and status line's texts, and the views remembered
 * per browser. Pure TypeScript, no Angular.
 */

import type {
  AiTelemetryGroupKey,
  AiTelemetryModelRef,
  AiTelemetrySeriesGroupOptionDto,
  AiTelemetrySeriesModelOptionDto,
  AiTelemetrySeriesOptionsDto,
  AiTelemetrySeriesUserOptionDto
} from '../../../../services/admin.service';
import { ModelPickerProviderFacet, providerKey, searchTerms } from '../../../../shared/model-picker/model-picker-view';
import { formatCost } from '../../../benchmark/batteries/battery.models';
import { formatInteger } from '../../../benchmark/shared/provider-availability/provider-availability';
import { parseServerUtcDate } from '../../../../utils/date.util';
import type { TelemetrySort, TelemetrySortDirection, TelemetrySortFieldInfo } from '../../sort-popover/telemetry-sort-popover.component';

// --- Caps and batches ------------------------------------------------------------------------

/** The most models, users and groups a partial choice may hold; the series endpoint takes at most 50 of each. */
export const TL_CHARTS_MAX_MODELS = 50;
export const TL_CHARTS_MAX_USERS = 50;
export const TL_CHARTS_MAX_GROUPS = 50;

/** The rows a list shows at first, and how many more Show more adds. */
export const TL_CHARTS_SELECTION_BATCH = 50;

/** The applied search follows the field after this much quiet. */
export const TL_CHARTS_SEARCH_DEBOUNCE_MS = 200;

// --- Rows and keys ---------------------------------------------------------------------------

/** A model's key source as the options carry it. */
export type ChartKeySource = 'system' | 'user';

interface ChartRowBase {
  /** The chooser's key: `provider|modelId`, the user id, or a group id or `none`. */
  key: string;
  /** What the row and its chip read. */
  name: string;
  requests: number;
  estimatedCost: number;
  pricedRequests: number;
  lastRequestUtc: string | null;
  /** A chosen key the options no longer hold: listed first, with *No usage in this scope*. */
  absent?: boolean;
}

export interface ChartModelRow extends ChartRowBase {
  provider: string;
  modelId: string;
  modelName: string | null;
  keySources: ChartKeySource[];
  tokens: number;
}

export interface ChartUserRow extends ChartRowBase {
  userId: string;
  /** Null when the account is gone (shown as *Unknown user*). */
  userName: string | null;
  systemRequests: number;
  userKeyRequests: number;
}

export interface ChartGroupRow extends ChartRowBase {
  /** Null is *No group*. */
  groupId: number | null;
  memberCount: number;
}

export type ChartSelectionRow = ChartModelRow | ChartUserRow | ChartGroupRow;

export function isModelRow(row: ChartSelectionRow): row is ChartModelRow {
  return 'modelId' in row;
}

export const UNKNOWN_USER_NAME = 'Unknown user';

export const NO_GROUP_NAME = 'No group';

/** The group key of the users in no group. */
export const NO_GROUP_KEY = 'none';

/** `provider|modelId`. */
export function modelKey(ref: AiTelemetryModelRef): string {
  return `${ref.provider}|${ref.modelId}`;
}

/** The model of a `provider|modelId` key, split at the first `|`; null without one, or with an empty side. */
export function parseModelKey(key: string): AiTelemetryModelRef | null {
  const at = key.indexOf('|');
  if (at <= 0 || at === key.length - 1) {
    return null;
  }
  return { provider: key.slice(0, at), modelId: key.slice(at + 1) };
}

/** A group's id as a key; `none` for the users in no group. */
export function groupKeyString(groupId: number | null | undefined): string {
  return groupId === null || groupId === undefined ? NO_GROUP_KEY : String(groupId);
}

/** A group key as the series filter takes it: the id, or `none`. An unreadable key reads as `none`. */
export function parseGroupKey(key: string): AiTelemetryGroupKey {
  if (key === NO_GROUP_KEY) {
    return 'none';
  }
  const id = Number(key);
  return Number.isInteger(id) ? id : 'none';
}

function modelRow(option: AiTelemetrySeriesModelOptionDto): ChartModelRow {
  const modelName = option.modelName?.trim() || null;
  return {
    key: modelKey(option),
    name: modelName ?? option.modelId,
    provider: option.provider,
    modelId: option.modelId,
    modelName,
    keySources: [...(option.keySources ?? [])],
    requests: option.requests,
    estimatedCost: option.estimatedCost,
    pricedRequests: option.pricedRequests,
    tokens: option.tokens,
    lastRequestUtc: option.lastRequestUtc ?? null
  };
}

function userRow(option: AiTelemetrySeriesUserOptionDto): ChartUserRow {
  const userName = option.userName?.trim() || null;
  return {
    key: option.userId,
    name: userName ?? UNKNOWN_USER_NAME,
    userId: option.userId,
    userName,
    requests: option.requests,
    systemRequests: option.systemRequests,
    userKeyRequests: option.userKeyRequests,
    estimatedCost: option.estimatedCost,
    pricedRequests: option.pricedRequests,
    lastRequestUtc: option.lastRequestUtc ?? null
  };
}

function groupRow(option: AiTelemetrySeriesGroupOptionDto): ChartGroupRow {
  const groupId = option.groupId ?? null;
  return {
    key: groupKeyString(groupId),
    name: option.groupName?.trim() || (groupId === null ? NO_GROUP_NAME : `Group ${groupId}`),
    groupId,
    memberCount: option.memberCount,
    requests: option.requests,
    estimatedCost: option.estimatedCost,
    pricedRequests: option.pricedRequests,
    lastRequestUtc: option.lastRequestUtc ?? null
  };
}

export function modelRows(dto: Pick<AiTelemetrySeriesOptionsDto, 'models'> | null | undefined): ChartModelRow[] {
  return (dto?.models ?? []).map(modelRow);
}

export function userRows(dto: Pick<AiTelemetrySeriesOptionsDto, 'users'> | null | undefined): ChartUserRow[] {
  return (dto?.users ?? []).map(userRow);
}

export function groupRows(dto: Pick<AiTelemetrySeriesOptionsDto, 'groups'> | null | undefined): ChartGroupRow[] {
  return (dto?.groups ?? []).map(groupRow);
}

/** A chosen model the options no longer hold, named by its model id. */
export function absentModelRow(key: string, name?: string): ChartModelRow {
  const ref = parseModelKey(key);
  return {
    key, name: name || ref?.modelId || key, provider: ref?.provider ?? '', modelId: ref?.modelId ?? key, modelName: null,
    keySources: [], requests: 0, estimatedCost: 0, pricedRequests: 0, tokens: 0, lastRequestUtc: null, absent: true
  };
}

/** A chosen user the options no longer hold, named as last seen, else by the user id. */
export function absentUserRow(key: string, name?: string): ChartUserRow {
  return {
    key, name: name || key, userId: key, userName: name || null,
    requests: 0, systemRequests: 0, userKeyRequests: 0, estimatedCost: 0, pricedRequests: 0, lastRequestUtc: null, absent: true
  };
}

/** A chosen group the options no longer hold, named as last seen, else *Group {id}* or *No group*. */
export function absentGroupRow(key: string, name?: string): ChartGroupRow {
  const id = parseGroupKey(key);
  return {
    key, name: name || (id === 'none' ? NO_GROUP_NAME : `Group ${id}`), groupId: id === 'none' ? null : id, memberCount: 0,
    requests: 0, estimatedCost: 0, pricedRequests: 0, lastRequestUtc: null, absent: true
  };
}

/** The chosen keys the rows do not hold, in choice order. */
export function absentKeys(chosen: readonly string[], rows: readonly { key: string }[]): string[] {
  const present = new Set(rows.map(r => r.key));
  return chosen.filter(key => !present.has(key));
}

// --- The effective filter --------------------------------------------------------------------

/**
 * What the dialog sends for a choice: nothing (no filter) when nothing is chosen, or when the choice
 * covers every row of an untruncated list; otherwise the chosen keys. The cap applies to this only.
 */
export function effectiveFilter(chosen: readonly string[], rows: readonly { key: string }[], truncated = false): string[] {
  if (chosen.length === 0) {
    return [];
  }
  if (!truncated && coversEveryRow(chosen, rows)) {
    return [];
  }
  return [...chosen];
}

/** Whether every row is chosen; never for an empty list. */
export function coversEveryRow(chosen: readonly string[], rows: readonly { key: string }[]): boolean {
  if (rows.length === 0) {
    return false;
  }
  const set = new Set(chosen);
  return rows.every(row => set.has(row.key));
}

/** Whether the choice would be a filter longer than `max`, which the series endpoint refuses. */
export function exceedsCap(chosen: readonly string[], rows: readonly { key: string }[], truncated: boolean, max: number): boolean {
  return effectiveFilter(chosen, rows, truncated).length > max;
}

/** Whether `key` can be added to the choice without a filter over the cap. */
export function canChoose(key: string, chosen: readonly string[], rows: readonly { key: string }[], truncated: boolean, max: number): boolean {
  return chosen.includes(key) || !exceedsCap([...chosen, key], rows, truncated, max);
}

/**
 * The choice with the shown rows added, in their order: all of them when that covers every row of an
 * untruncated list, otherwise as many as the cap leaves room for.
 */
export function chooseShown(chosen: readonly string[], shown: readonly { key: string }[], rows: readonly { key: string }[],
                            truncated: boolean, max: number): string[] {
  const set = new Set(chosen);
  const added = shown.map(r => r.key).filter(key => !set.has(key));
  const all = [...chosen, ...added];
  if (!exceedsCap(all, rows, truncated, max)) {
    return all;
  }
  return [...chosen, ...added.slice(0, Math.max(0, max - chosen.length))];
}

// --- Search and toggles ----------------------------------------------------------------------

/** What the search matches, lower-cased: a model's name, model id and provider; a user's or group's name. */
export function chartSearchText(row: ChartSelectionRow): string {
  if (isModelRow(row)) {
    return [row.name, row.modelId, row.provider].join(' ').toLowerCase();
  }
  return row.name.toLowerCase();
}

export interface ChartRowFilter {
  search: string;
  /** Lower-cased provider keys; none means every provider. Models only. */
  providers?: readonly string[];
  /** None means both key sources. Models only. */
  keySources?: readonly ChartKeySource[];
  /** The chosen rows only. */
  selectedOnly?: boolean;
  chosen?: readonly string[];
}

/**
 * The rows, in a new array, that contain every search term, whose provider is one of `providers` and
 * that have one of `keySources`, and with `selectedOnly` that are chosen. An absent row is matched by
 * the search alone.
 */
export function filterChartRows<T extends ChartSelectionRow>(rows: readonly T[], filter: ChartRowFilter): T[] {
  const terms = searchTerms(filter.search);
  const providers = new Set(filter.providers ?? []);
  const keySources = new Set(filter.keySources ?? []);
  const chosen = new Set(filter.chosen ?? []);
  return rows.filter((item: T) => {
    const row: ChartSelectionRow = item;
    if (terms.length > 0) {
      const text = chartSearchText(row);
      if (!terms.every(term => text.includes(term))) {
        return false;
      }
    }
    if (row.absent) {
      return true;
    }
    if (filter.selectedOnly && !chosen.has(row.key)) {
      return false;
    }
    if (isModelRow(row)) {
      if (providers.size > 0 && !providers.has(providerKey(row.provider))) {
        return false;
      }
      if (keySources.size > 0 && !row.keySources.some(source => keySources.has(source))) {
        return false;
      }
    }
    return true;
  });
}

const collator = new Intl.Collator('en', { numeric: true, sensitivity: 'base' });

/** One toggle per provider of the rows, spelled as its first row spells it, counting rows, by name. */
export function chartProviderFacets(rows: readonly ChartModelRow[]): ModelPickerProviderFacet[] {
  const byKey = new Map<string, ModelPickerProviderFacet>();
  for (const row of rows) {
    const key = providerKey(row.provider);
    if (!key || row.absent) {
      continue;
    }
    const facet = byKey.get(key);
    if (facet) {
      facet.count++;
    } else {
      byKey.set(key, { provider: row.provider.trim(), key, count: 1 });
    }
  }
  return [...byKey.values()].sort((a, b) => collator.compare(a.provider, b.provider));
}

/** The pressed providers some row has; a stored provider no row has never hides everything. */
export function effectiveChartProviders(selected: readonly string[], facets: readonly ModelPickerProviderFacet[]): string[] {
  const present = new Set(facets.map(f => f.key));
  return selected.filter(key => present.has(key));
}

export interface ChartKeySourceFacet {
  key: ChartKeySource;
  label: string;
  count: number;
}

export const CHART_KEY_SOURCE_LABELS: Readonly<Record<ChartKeySource, string>> = {
  system: 'System',
  user: 'Own keys'
};

/** *System* and *Own keys* with the models that have each, the ones that occur only. */
export function chartKeySourceFacets(rows: readonly ChartModelRow[]): ChartKeySourceFacet[] {
  return (['system', 'user'] as const)
    .map(key => ({ key, label: CHART_KEY_SOURCE_LABELS[key], count: rows.filter(r => !r.absent && r.keySources.includes(key)).length }))
    .filter(facet => facet.count > 0);
}

// --- Sort ------------------------------------------------------------------------------------

export type ChartModelSortField = 'requests' | 'cost' | 'tokens' | 'lastUsed' | 'name' | 'provider';
export type ChartUserSortField = 'cost' | 'requests' | 'lastActive' | 'name';
export type ChartGroupSortField = 'requests' | 'cost' | 'members' | 'lastActive' | 'name';

export const CHART_MODEL_SORT_FIELDS: readonly TelemetrySortFieldInfo<ChartModelSortField>[] = [
  { field: 'requests', label: 'Requests', kind: 'number' },
  { field: 'cost', label: 'Est. cost', kind: 'number' },
  { field: 'tokens', label: 'Tokens', kind: 'number' },
  { field: 'lastUsed', label: 'Last used', kind: 'date' },
  { field: 'name', label: 'Model name', kind: 'text' },
  { field: 'provider', label: 'Provider', kind: 'text' }
];

export const CHART_USER_SORT_FIELDS: readonly TelemetrySortFieldInfo<ChartUserSortField>[] = [
  { field: 'cost', label: 'Est. cost', kind: 'number' },
  { field: 'requests', label: 'Requests', kind: 'number' },
  { field: 'lastActive', label: 'Last active', kind: 'date' },
  { field: 'name', label: 'User name', kind: 'text' }
];

export const CHART_GROUP_SORT_FIELDS: readonly TelemetrySortFieldInfo<ChartGroupSortField>[] = [
  { field: 'requests', label: 'Requests', kind: 'number' },
  { field: 'cost', label: 'Est. cost', kind: 'number' },
  { field: 'members', label: 'Members', kind: 'number' },
  { field: 'lastActive', label: 'Last active', kind: 'date' },
  { field: 'name', label: 'Group name', kind: 'text' }
];

export const DEFAULT_CHART_MODEL_SORT: TelemetrySort<ChartModelSortField> =
  Object.freeze({ field: 'requests', direction: 'desc' }) as TelemetrySort<ChartModelSortField>;
export const DEFAULT_CHART_USER_SORT: TelemetrySort<ChartUserSortField> =
  Object.freeze({ field: 'cost', direction: 'desc' }) as TelemetrySort<ChartUserSortField>;
export const DEFAULT_CHART_GROUP_SORT: TelemetrySort<ChartGroupSortField> =
  Object.freeze({ field: 'requests', direction: 'desc' }) as TelemetrySort<ChartGroupSortField>;

/** Whether a cost is unknown: requests and none of them priced. */
export function isUnpriced(row: Pick<ChartRowBase, 'requests' | 'pricedRequests'>): boolean {
  return row.requests > 0 && row.pricedRequests === 0;
}

/** A server timestamp in ms; null when missing or unreadable. */
function timeOf(utc: string | null | undefined): number | null {
  if (!utc) {
    return null;
  }
  const time = parseServerUtcDate(utc).getTime();
  return Number.isNaN(time) ? null : time;
}

/** A row's value for a figure or date field; null where it has none (an unpriced cost, an unknown date). */
function sortValue(row: ChartSelectionRow, field: string): number | null {
  switch (field) {
    case 'requests': return row.requests;
    case 'cost': return isUnpriced(row) ? null : row.estimatedCost;
    case 'tokens': return 'tokens' in row ? row.tokens : null;
    case 'members': return 'memberCount' in row ? row.memberCount : null;
    case 'lastUsed':
    case 'lastActive': return timeOf(row.lastRequestUtc);
    default: return null;
  }
}

function providerOf(row: ChartSelectionRow): string {
  return isModelRow(row) ? row.provider : '';
}

/** Name A–Z, then key. */
function compareTies(a: ChartSelectionRow, b: ChartSelectionRow): number {
  return collator.compare(a.name, b.name) || (a.key < b.key ? -1 : a.key > b.key ? 1 : 0);
}

/**
 * The rows in a new array, sorted by the field in the direction; names compare numerically. A row
 * without a figure for the field (an unpriced cost, an unknown date) sorts last in both directions.
 * Ties go by name A–Z, then key.
 */
export function sortChartRows<T extends ChartSelectionRow>(rows: readonly T[], sort: TelemetrySort): T[] {
  const sign = sort.direction === 'asc' ? 1 : -1;
  let compare: (a: T, b: T) => number;
  if (sort.field === 'name') {
    compare = (a, b) => sign * collator.compare(a.name, b.name) || compareTies(a, b);
  } else if (sort.field === 'provider') {
    compare = (a, b) => sign * collator.compare(providerOf(a), providerOf(b)) || compareTies(a, b);
  } else {
    compare = (a, b) => {
      const va = sortValue(a, sort.field);
      const vb = sortValue(b, sort.field);
      if (va === null || vb === null) {
        return (va === null ? 1 : 0) - (vb === null ? 1 : 0) || compareTies(a, b);
      }
      return sign * (va - vb) || compareTies(a, b);
    };
  }
  return [...rows].sort(compare);
}

// --- Texts -----------------------------------------------------------------------------------

/** The noun of each list, singular and plural. */
export interface ChartListNoun {
  one: string;
  many: string;
}

export const CHART_MODEL_NOUN: ChartListNoun = { one: 'model', many: 'models' };
export const CHART_USER_NOUN: ChartListNoun = { one: 'user', many: 'users' };
export const CHART_GROUP_NOUN: ChartListNoun = { one: 'group', many: 'groups' };

function counted(n: number, noun: ChartListNoun): string {
  return `${formatInteger(n)} ${n === 1 ? noun.one : noun.many}`;
}

/**
 * The selection band's label, never empty: *All models — no filter*; *All 14 models chosen — the
 * same as no filter*; *3 of 14 models*. `total` is the list's size before any cap.
 */
export function selectionBandText(noun: ChartListNoun, chosenCount: number, total: number, coversAll: boolean): string {
  if (chosenCount === 0) {
    return `All ${noun.many} — no filter`;
  }
  if (coversAll) {
    return total === 1
      ? `The only ${noun.one} chosen — the same as no filter`
      : `All ${counted(total, noun)} chosen — the same as no filter`;
  }
  return `${formatInteger(chosenCount)} of ${counted(total, noun)}`;
}

/**
 * The status line: *Showing 12 of 27 models · 3 chosen*, and for a capped list *· the 1,000 users
 * with the most requests of 1,284*.
 */
export function selectionStatusText(noun: ChartListNoun, shown: number, listed: number, chosenCount: number,
                                    cap?: { truncated: boolean; total: number }): string {
  const parts = [`Showing ${formatInteger(shown)} of ${counted(listed, noun)}`];
  if (chosenCount > 0) {
    parts.push(`${formatInteger(chosenCount)} chosen`);
  }
  if (cap?.truncated) {
    parts.push(`the ${counted(listed, noun)} with the most requests of ${formatInteger(cap.total)}`);
  }
  return parts.join(' · ');
}

/** The cap's warning: *At most 50 models can be chosen — or choose them all with Select all.* */
export function capWarningText(noun: ChartListNoun, max: number, selectAllAvailable: boolean): string {
  const base = `At most ${counted(max, noun)} can be chosen`;
  return selectAllAvailable ? `${base} — or choose them all with Select all.` : `${base}.`;
}

/** `12 requests`, `1 request`. */
export function requestsText(n: number): string {
  return `${formatInteger(n)} ${n === 1 ? 'request' : 'requests'}`;
}

/** `12 members`, `1 member`. */
export function membersText(n: number): string {
  return `${formatInteger(n)} ${n === 1 ? 'member' : 'members'}`;
}

/** `2026-10-13` (UTC); empty for none, the text itself when unreadable. */
export function chartDateText(utc: string | null | undefined): string {
  if (!utc) {
    return '';
  }
  const date = parseServerUtcDate(utc);
  if (Number.isNaN(date.getTime())) {
    return utc;
  }
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${date.getUTCFullYear()}-${pad(date.getUTCMonth() + 1)}-${pad(date.getUTCDate())}`;
}

export const NO_USAGE_TEXT = 'No usage in this scope';

/**
 * A row's meta line parts: *12 requests*, *est. $0.25* (with *3 unpriced* while part of them has no
 * price, or *cost unknown* while none has), then *{lastWord} 2026-10-13*. An absent row reads *No
 * usage in this scope*.
 */
export function usageParts(row: Pick<ChartRowBase, 'requests' | 'estimatedCost' | 'pricedRequests' | 'lastRequestUtc' | 'absent'>,
                           lastWord: string): string[] {
  if (row.absent) {
    return [NO_USAGE_TEXT];
  }
  const parts = [requestsText(row.requests)];
  if (row.requests > 0) {
    if (isUnpriced(row)) {
      parts.push('cost unknown');
    } else {
      parts.push(`est. ${formatCost(row.estimatedCost)}`);
      const unpriced = Math.max(0, row.requests - row.pricedRequests);
      if (unpriced > 0) {
        parts.push(`${formatInteger(unpriced)} unpriced`);
      }
    }
  }
  const date = chartDateText(row.lastRequestUtc);
  if (date) {
    parts.push(`${lastWord} ${date}`);
  }
  return parts;
}

// --- View storage ----------------------------------------------------------------------------

export const TL_CHARTS_MODELS_VIEW_STORAGE_KEY = 'overseer.admin.telemetry.charts.models.view';
export const TL_CHARTS_USERS_VIEW_STORAGE_KEY = 'overseer.admin.telemetry.charts.users.view';
export const TL_CHARTS_GROUPS_VIEW_STORAGE_KEY = 'overseer.admin.telemetry.charts.groups.view';

const VIEW_VERSION = 1;

const SORT_DIRECTIONS: readonly TelemetrySortDirection[] = ['desc', 'asc'];

/** The search and the other toggles are not part of a view: they never hide rows on a later visit. */
export interface ChartModelsView {
  sort: TelemetrySort<ChartModelSortField>;
  /** Lower-cased provider keys; empty means every provider. */
  providers: string[];
}

export interface ChartListView<F extends string> {
  sort: TelemetrySort<F>;
}

function readSort<F extends string>(value: unknown, fields: readonly TelemetrySortFieldInfo<F>[]): TelemetrySort<F> | null {
  if (!value || typeof value !== 'object') {
    return null;
  }
  const { field, direction } = value as Record<string, unknown>;
  const info = fields.find(f => f.field === field);
  const dir = SORT_DIRECTIONS.find(d => d === direction);
  return info && dir ? { field: info.field, direction: dir } : null;
}

/** The stored record of version 1; null when there is none, it is unreadable or of another version. */
function readRecord(key: string, storage: Storage | null): Record<string, unknown> | null {
  try {
    const raw = storage?.getItem(key);
    if (!raw) {
      return null;
    }
    const parsed = JSON.parse(raw) as unknown;
    if (!parsed || typeof parsed !== 'object' || (parsed as Record<string, unknown>)['version'] !== VIEW_VERSION) {
      return null;
    }
    return parsed as Record<string, unknown>;
  } catch {
    return null;
  }
}

function writeRecord(key: string, record: Record<string, unknown>, storage: Storage | null): void {
  try {
    storage?.setItem(key, JSON.stringify({ version: VIEW_VERSION, ...record }));
  } catch {
    // Storage full or unavailable: the view is simply not remembered.
  }
}

export function defaultChartModelsView(): ChartModelsView {
  return { sort: { ...DEFAULT_CHART_MODEL_SORT }, providers: [] };
}

/** The Models view; the default when nothing is stored, the record is unreadable, or storage is unavailable. */
export function readChartModelsView(storage: Storage | null = safeLocalStorage()): ChartModelsView {
  const fallback = defaultChartModelsView();
  const record = readRecord(TL_CHARTS_MODELS_VIEW_STORAGE_KEY, storage);
  if (!record) {
    return fallback;
  }
  const providers = Array.isArray(record['providers'])
    ? [...new Set((record['providers'] as unknown[])
      .filter((p): p is string => typeof p === 'string')
      .map(p => providerKey(p))
      .filter(p => p !== ''))]
    : [];
  return { sort: readSort(record['sort'], CHART_MODEL_SORT_FIELDS) ?? fallback.sort, providers };
}

/** Stores the Models view as `{ version: 1, sort, providers }`; a failure is ignored. */
export function writeChartModelsView(view: ChartModelsView, storage: Storage | null = safeLocalStorage()): void {
  writeRecord(TL_CHARTS_MODELS_VIEW_STORAGE_KEY, {
    sort: { field: view.sort.field, direction: view.sort.direction },
    providers: [...view.providers]
  }, storage);
}

export function readChartUsersView(storage: Storage | null = safeLocalStorage()): ChartListView<ChartUserSortField> {
  const record = readRecord(TL_CHARTS_USERS_VIEW_STORAGE_KEY, storage);
  return { sort: readSort(record?.['sort'], CHART_USER_SORT_FIELDS) ?? { ...DEFAULT_CHART_USER_SORT } };
}

/** Stores the Users view as `{ version: 1, sort }`; a failure is ignored. */
export function writeChartUsersView(view: ChartListView<ChartUserSortField>, storage: Storage | null = safeLocalStorage()): void {
  writeRecord(TL_CHARTS_USERS_VIEW_STORAGE_KEY, { sort: { field: view.sort.field, direction: view.sort.direction } }, storage);
}

export function readChartGroupsView(storage: Storage | null = safeLocalStorage()): ChartListView<ChartGroupSortField> {
  const record = readRecord(TL_CHARTS_GROUPS_VIEW_STORAGE_KEY, storage);
  return { sort: readSort(record?.['sort'], CHART_GROUP_SORT_FIELDS) ?? { ...DEFAULT_CHART_GROUP_SORT } };
}

/** Stores the Groups view as `{ version: 1, sort }`; a failure is ignored. */
export function writeChartGroupsView(view: ChartListView<ChartGroupSortField>, storage: Storage | null = safeLocalStorage()): void {
  writeRecord(TL_CHARTS_GROUPS_VIEW_STORAGE_KEY, { sort: { field: view.sort.field, direction: view.sort.direction } }, storage);
}

function safeLocalStorage(): Storage | null {
  try {
    return typeof localStorage === 'undefined' ? null : localStorage;
  } catch {
    return null;
  }
}
