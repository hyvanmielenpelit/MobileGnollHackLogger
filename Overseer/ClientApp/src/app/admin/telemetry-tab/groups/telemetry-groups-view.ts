import type { AiTelemetryGroupKey, AiTelemetryGroupRowDto } from '../../../services/admin.service';
import { searchTerms } from '../../../shared/model-picker/model-picker-view';
import { parseServerUtcDate } from '../../../utils/date.util';
import {
  TelemetrySort,
  TelemetrySortDirection,
  TelemetrySortFieldInfo,
  telemetrySortLabel
} from '../sort-popover/telemetry-sort-popover.component';

// --- Identity --------------------------------------------------------------------------------

/** The title of the card for the users in no group. */
export const NO_GROUP_TEXT = 'No group';

/** The group's id, or `'none'` for the users in no group, as the server's queries take it. */
export function groupKeyOf(row: AiTelemetryGroupRowDto): AiTelemetryGroupKey {
  return row.groupId ?? 'none';
}

/** The card's title: the group's name, or *No group*. */
export function groupDisplayName(row: AiTelemetryGroupRowDto): string {
  return row.groupId == null ? NO_GROUP_TEXT : (row.groupName?.trim() || `Group ${row.groupId}`);
}

// --- Search ----------------------------------------------------------------------------------

/** The rows whose name holds every search term, in their order; all of them for an empty search. */
export function searchGroups(rows: readonly AiTelemetryGroupRowDto[], search: string): AiTelemetryGroupRowDto[] {
  const terms = searchTerms(search);
  if (terms.length === 0) {
    return [...rows];
  }
  return rows.filter(row => {
    const name = groupDisplayName(row).toLowerCase();
    return terms.every(term => name.includes(term));
  });
}

// --- Sort ------------------------------------------------------------------------------------

export type TelemetryGroupsSortField =
  'cost' | 'requests' | 'tokens' | 'peakHour' | 'refusals' | 'members' | 'lastActive' | 'name';

export type TelemetryGroupsSort = TelemetrySort<TelemetryGroupsSortField>;

export const DEFAULT_TELEMETRY_GROUPS_SORT: TelemetryGroupsSort =
  Object.freeze({ field: 'cost', direction: 'desc' }) as TelemetryGroupsSort;

/** The sort fields in the Sort popover's order, labeled as the cards label the metrics. */
export const TELEMETRY_GROUPS_SORT_FIELDS: readonly TelemetrySortFieldInfo<TelemetryGroupsSortField>[] = [
  { field: 'cost', label: 'Est. cost', kind: 'number' },
  { field: 'requests', label: 'Requests', kind: 'number' },
  { field: 'tokens', label: 'Tokens', kind: 'number' },
  { field: 'peakHour', label: 'Busiest hour', kind: 'number' },
  { field: 'refusals', label: 'Refusals', kind: 'number' },
  { field: 'members', label: 'Members', kind: 'number' },
  { field: 'lastActive', label: 'Last active', kind: 'date' },
  { field: 'name', label: 'Group name', kind: 'text' }
];

const SORT_DIRECTIONS: readonly TelemetrySortDirection[] = ['desc', 'asc'];

/** `Est. cost, highest first`; `Group name, A–Z`. */
export function groupsSortLabel(sort: TelemetryGroupsSort): string {
  return telemetrySortLabel(TELEMETRY_GROUPS_SORT_FIELDS, sort);
}

export function isDefaultGroupsSort(sort: TelemetryGroupsSort): boolean {
  return sort.field === DEFAULT_TELEMETRY_GROUPS_SORT.field && sort.direction === DEFAULT_TELEMETRY_GROUPS_SORT.direction;
}

const collator = new Intl.Collator('en', { numeric: true, sensitivity: 'base' });

/** Name A–Z, then id; *No group* last. */
function compareTies(a: AiTelemetryGroupRowDto, b: AiTelemetryGroupRowDto, byName = true): number {
  if (byName) {
    const byText = collator.compare(groupDisplayName(a), groupDisplayName(b));
    if (byText !== 0) {
      return byText;
    }
  }
  if (a.groupId == null || b.groupId == null) {
    return (a.groupId == null ? 1 : 0) - (b.groupId == null ? 1 : 0);
  }
  return a.groupId - b.groupId;
}

/** A row's value for a numeric or date field; null where the card shows no figure for it. */
function sortValue(row: AiTelemetryGroupRowDto, field: TelemetryGroupsSortField): number | null {
  switch (field) {
    case 'cost': return row.requests > 0 && row.pricedRequests === 0 ? null : row.estimatedCost;
    case 'requests': return row.requests;
    case 'tokens': return row.promptTokens + row.outputTokens;
    case 'peakHour': return row.peakHourRequests;
    case 'refusals': return row.limitRefusals + row.providerRefusals;
    case 'members': return row.memberCount;
    case 'lastActive': {
      const time = row.lastRequestUtc ? parseServerUtcDate(row.lastRequestUtc).getTime() : NaN;
      return Number.isNaN(time) ? null : time;
    }
    default: return null;
  }
}

/**
 * The rows in a new array, sorted by the field in the direction. A row without a figure for the
 * field (an unpriced cost, no last request) sorts last in both directions. Ties go by name A–Z,
 * then id, *No group* last.
 */
export function sortGroups(rows: readonly AiTelemetryGroupRowDto[], sort: TelemetryGroupsSort): AiTelemetryGroupRowDto[] {
  const sign = sort.direction === 'asc' ? 1 : -1;
  let compare: (a: AiTelemetryGroupRowDto, b: AiTelemetryGroupRowDto) => number;
  if (sort.field === 'name') {
    compare = (a, b) => sign * collator.compare(groupDisplayName(a), groupDisplayName(b)) || compareTies(a, b, false);
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
  return [...rows].sort(compare);
}

// --- View settings ---------------------------------------------------------------------------

/** Where the Groups view's sort is kept, per browser. */
export const TELEMETRY_GROUPS_VIEW_STORAGE_KEY = 'overseer.admin.telemetry.groups.view';

const VIEW_VERSION = 1;

/** The search is not part of the view: an old search never hides groups on a later visit. */
export interface TelemetryGroupsView {
  sort: TelemetryGroupsSort;
}

/** Highest cost first. */
export function defaultGroupsView(): TelemetryGroupsView {
  return { sort: DEFAULT_TELEMETRY_GROUPS_SORT };
}

function readSort(value: unknown): TelemetryGroupsSort | null {
  if (!value || typeof value !== 'object') {
    return null;
  }
  const { field, direction } = value as Record<string, unknown>;
  const info = TELEMETRY_GROUPS_SORT_FIELDS.find(f => f.field === field);
  const dir = SORT_DIRECTIONS.find(d => d === direction);
  return info && dir ? { field: info.field, direction: dir } : null;
}

/**
 * The stored view; the default when nothing is stored, the record is unreadable, of another
 * version or names an unknown sort, or storage is unavailable.
 */
export function readGroupsView(storage: Storage | null = safeLocalStorage()): TelemetryGroupsView {
  const fallback = defaultGroupsView();
  try {
    const raw = storage?.getItem(TELEMETRY_GROUPS_VIEW_STORAGE_KEY);
    if (!raw) {
      return fallback;
    }
    const parsed = JSON.parse(raw) as { version?: unknown; sort?: unknown } | null;
    if (!parsed || typeof parsed !== 'object' || parsed.version !== VIEW_VERSION) {
      return fallback;
    }
    return { sort: readSort(parsed.sort) ?? fallback.sort };
  } catch {
    return fallback;
  }
}

/** Stores the view as `{ version: 1, sort: { field, direction } }`; a failure is ignored. */
export function writeGroupsView(view: TelemetryGroupsView, storage: Storage | null = safeLocalStorage()): void {
  try {
    storage?.setItem(TELEMETRY_GROUPS_VIEW_STORAGE_KEY, JSON.stringify({
      version: VIEW_VERSION,
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
