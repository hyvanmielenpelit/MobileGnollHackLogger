import type { AiTelemetryUsersSort } from '../../../services/admin.service';
import { modelUserDirectionLabel } from '../model-users/telemetry-model-users';

// --- Sort ------------------------------------------------------------------------------------

/** The server sorts the users; these are the fields it accepts. */
export type TelemetryUsersSortField = AiTelemetryUsersSort;

export type TelemetryUsersSortDirection = 'desc' | 'asc';

export interface TelemetryUsersSort {
  field: TelemetryUsersSortField;
  direction: TelemetryUsersSortDirection;
}

export type TelemetryUsersSortKind = 'number' | 'text' | 'date';

export interface TelemetryUsersSortFieldInfo {
  field: TelemetryUsersSortField;
  label: string;
  kind: TelemetryUsersSortKind;
}

export const DEFAULT_TELEMETRY_USERS_SORT: TelemetryUsersSort =
  Object.freeze({ field: 'cost', direction: 'desc' }) as TelemetryUsersSort;

/** The sort fields in the Sort popover's order, labeled as the cards label the metrics. */
export const TELEMETRY_USERS_SORT_FIELDS: readonly TelemetryUsersSortFieldInfo[] = [
  { field: 'cost', label: 'Est. cost', kind: 'number' },
  { field: 'requests', label: 'Requests', kind: 'number' },
  { field: 'tokens', label: 'Tokens', kind: 'number' },
  { field: 'peakHour', label: 'Busiest hour', kind: 'number' },
  { field: 'refusals', label: 'Refusals', kind: 'number' },
  { field: 'lastActive', label: 'Last active', kind: 'date' },
  { field: 'name', label: 'User name', kind: 'text' }
];

const SORT_DIRECTIONS: readonly TelemetryUsersSortDirection[] = ['desc', 'asc'];

export function usersSortField(field: TelemetryUsersSortField): TelemetryUsersSortFieldInfo {
  return TELEMETRY_USERS_SORT_FIELDS.find(f => f.field === field) ?? TELEMETRY_USERS_SORT_FIELDS[0];
}

/** `Highest first` / `Lowest first` for a figure, `Newest first` / `Oldest first` for a date, `A–Z` / `Z–A` for a name. */
export function usersDirectionLabel(kind: TelemetryUsersSortKind, direction: TelemetryUsersSortDirection): string {
  return modelUserDirectionLabel(kind, direction);
}

/** `Est. cost, highest first`; `User name, A–Z`. */
export function usersSortLabel(sort: TelemetryUsersSort): string {
  const info = usersSortField(sort.field);
  const direction = usersDirectionLabel(info.kind, sort.direction);
  return `${info.label}, ${info.kind === 'text' ? direction : direction.toLowerCase()}`;
}

export function isDefaultUsersSort(sort: TelemetryUsersSort): boolean {
  return sort.field === DEFAULT_TELEMETRY_USERS_SORT.field && sort.direction === DEFAULT_TELEMETRY_USERS_SORT.direction;
}

// --- View settings ---------------------------------------------------------------------------

/** Where the Users view's sort is kept, per browser. */
export const TELEMETRY_USERS_VIEW_STORAGE_KEY = 'overseer.admin.telemetry.users.view';

const VIEW_VERSION = 2;

/** The record `CardListState` wrote while the list had a Sort by select: `{ version: 1, sort: '<id>' }`, every order descending. */
const CARD_LIST_VERSION = 1;

/** The search is not part of the view: an old search never hides users on a later visit. */
export interface TelemetryUsersView {
  sort: TelemetryUsersSort;
}

/** Highest cost first. */
export function defaultUsersView(): TelemetryUsersView {
  return { sort: DEFAULT_TELEMETRY_USERS_SORT };
}

function readSort(value: unknown): TelemetryUsersSort | null {
  if (!value || typeof value !== 'object') {
    return null;
  }
  const { field, direction } = value as Record<string, unknown>;
  const info = TELEMETRY_USERS_SORT_FIELDS.find(f => f.field === field);
  const dir = SORT_DIRECTIONS.find(d => d === direction);
  return info && dir ? { field: info.field, direction: dir } : null;
}

/**
 * The stored view; the default when nothing is stored, the record is unreadable, of an unknown
 * version or names an unknown order, or storage is unavailable. A version 1 record's order id is
 * read as that field, descending.
 */
export function readUsersView(storage: Storage | null = safeLocalStorage()): TelemetryUsersView {
  const fallback = defaultUsersView();
  try {
    const raw = storage?.getItem(TELEMETRY_USERS_VIEW_STORAGE_KEY);
    if (!raw) {
      return fallback;
    }
    const parsed = JSON.parse(raw) as { version?: unknown; sort?: unknown } | null;
    if (!parsed || typeof parsed !== 'object') {
      return fallback;
    }
    if (parsed.version === VIEW_VERSION) {
      return { sort: readSort(parsed.sort) ?? fallback.sort };
    }
    if (parsed.version === CARD_LIST_VERSION && typeof parsed.sort === 'string') {
      return { sort: readSort({ field: parsed.sort, direction: 'desc' }) ?? fallback.sort };
    }
    return fallback;
  } catch {
    return fallback;
  }
}

/** Stores the view as `{ version: 2, sort: { field, direction } }`; a failure is ignored. */
export function writeUsersView(view: TelemetryUsersView, storage: Storage | null = safeLocalStorage()): void {
  try {
    storage?.setItem(TELEMETRY_USERS_VIEW_STORAGE_KEY, JSON.stringify({
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
