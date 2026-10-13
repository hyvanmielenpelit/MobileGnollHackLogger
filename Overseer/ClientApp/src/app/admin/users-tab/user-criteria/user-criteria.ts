/**
 * The user criteria as the filter bar shows them: the Sort by orders, the facet options and their
 * labels, the mapping between a facet choice and `AdminUserCriteria`, and the facet counts. Pure, so
 * the user list, the Bulk Actions wizard and the page store share one definition.
 */

import type { CardListSort } from '../../../shared/data-table/card-list-state';
import type { FilterFacetOption } from '../../../shared/data-table/filter-facet.component';
import {
  ADMIN_USER_STATUSES,
  AdminFacetCount,
  AdminUserCriteria,
  AdminUserFacetCounts,
  AdminUserSort,
  AdminUserStatus
} from '../../../services/admin-users.service';
import { statusLabel } from '../user-status/admin-user-status';

/** Where the user list's Sort by is kept, per browser, as `{ version: 1, sort }`. */
export const USERS_VIEW_STORAGE_KEY = 'overseer.admin.users.view';

export const DEFAULT_USER_SORT: AdminUserSort = 'newest';

/** The Sort by orders. The server sorts; `column` only names the order for `CardListState`. */
export const USER_SORTS: readonly (CardListSort & { id: AdminUserSort })[] = [
  { id: 'newest', label: 'Newest first', column: 'newest', direction: 'desc' },
  { id: 'oldest', label: 'Oldest first', column: 'oldest', direction: 'asc' },
  { id: 'name', label: 'User name (A–Z)', column: 'name', direction: 'asc' },
  { id: 'email', label: 'Email (A–Z)', column: 'email', direction: 'asc' },
  { id: 'signin-recent', label: 'Signed in recently', column: 'signin-recent', direction: 'desc' },
  { id: 'signin-oldest', label: 'Longest without sign-in', column: 'signin-oldest', direction: 'asc' }
];

/** The remembered Sort by order; the default when none is stored, it is unreadable or unknown. */
export function readStoredUserSort(): AdminUserSort {
  try {
    const raw = localStorage.getItem(USERS_VIEW_STORAGE_KEY);
    const parsed = raw ? JSON.parse(raw) as { version?: unknown; sort?: unknown } | null : null;
    const sort = parsed && typeof parsed === 'object' && parsed.version === 1 ? parsed.sort : null;
    return USER_SORTS.find(option => option.id === sort)?.id ?? DEFAULT_USER_SORT;
  } catch {
    return DEFAULT_USER_SORT;
  }
}

/** One choice of a facet: the value the bar keeps and what it reads. */
export interface UserCriteriaOption {
  value: string;
  label: string;
}

/** The Email facet (single). */
export const EMAIL_OPTIONS: readonly UserCriteriaOption[] = [
  { value: 'yes', label: 'Confirmed' },
  { value: 'no', label: 'Not confirmed' }
];

/** The Registered facet (single). `between:7:30` is registered within 30 days and more than 7 days ago. */
export const REGISTERED_OPTIONS: readonly UserCriteriaOption[] = [
  { value: 'withinDays:7', label: 'In the last 7 days' },
  { value: 'between:7:30', label: '7–30 days ago' },
  { value: 'olderThanDays:30', label: 'More than 30 days ago' },
  { value: 'olderThanDays:365', label: 'More than a year ago' }
];

/**
 * The Last sign-in facet (single): the server's own facet keys. A sign-in date is recorded only from
 * the release that added it, so accounts in use may still read *Never recorded*.
 */
export const LAST_SIGN_IN_OPTIONS: readonly UserCriteriaOption[] = [
  { value: 'withinDays:30', label: 'In the last 30 days' },
  { value: 'withinDays:90', label: 'In the last 90 days' },
  { value: 'olderThanDays:90', label: 'More than 90 days ago' },
  { value: 'olderThanDays:365', label: 'More than a year ago' },
  { value: 'never', label: 'Never recorded' }
];

/** The Data facet (multiple). Choosing one of a pair drops the other. */
export const DATA_OPTIONS: readonly UserCriteriaOption[] = [
  { value: 'game:yes', label: 'Has game data' },
  { value: 'game:no', label: 'No game data' },
  { value: 'overseer:yes', label: 'Has Overseer data' },
  { value: 'overseer:no', label: 'No Overseer data' }
];

/** The Status facet (multiple). */
export const STATUS_OPTIONS: readonly UserCriteriaOption[] = ADMIN_USER_STATUSES.map(status => ({ value: status, label: statusLabel(status) }));

export function optionLabel(options: readonly UserCriteriaOption[], value: string): string {
  return options.find(option => option.value === value)?.label ?? value;
}

/** The Registered option the criteria hold, or `''` for none or a combination the facet does not offer. */
export function registeredOptionOf(criteria: AdminUserCriteria): string {
  const older = criteria.registeredOlderThanDays;
  const within = criteria.registeredWithinDays;
  if (older == null && within === 7) return 'withinDays:7';
  if (older === 7 && within === 30) return 'between:7:30';
  if (within == null && older === 30) return 'olderThanDays:30';
  if (within == null && older === 365) return 'olderThanDays:365';
  return '';
}

/** Writes a Registered option into the criteria; `''` clears both registration criteria. */
export function applyRegisteredOption(criteria: AdminUserCriteria, value: string): void {
  delete criteria.registeredOlderThanDays;
  delete criteria.registeredWithinDays;
  if (value === 'withinDays:7') {
    criteria.registeredWithinDays = 7;
  } else if (value === 'between:7:30') {
    criteria.registeredOlderThanDays = 7;
    criteria.registeredWithinDays = 30;
  } else if (value === 'olderThanDays:30') {
    criteria.registeredOlderThanDays = 30;
  } else if (value === 'olderThanDays:365') {
    criteria.registeredOlderThanDays = 365;
  }
}

/** The Last sign-in option the criteria hold, or `''`. */
export function lastSignInOptionOf(criteria: AdminUserCriteria): string {
  const value = criteria.lastSignIn ?? '';
  return LAST_SIGN_IN_OPTIONS.some(option => option.value === value) ? value : '';
}

/** The Data facet's values the criteria hold. */
export function dataValuesOf(criteria: AdminUserCriteria): string[] {
  const values: string[] = [];
  if (criteria.hasGameData) values.push(`game:${criteria.hasGameData}`);
  if (criteria.hasOverseerData) values.push(`overseer:${criteria.hasOverseerData}`);
  return values;
}

/**
 * A new Data selection with at most one value per pair: when both of a pair are chosen, the one not
 * in `previous` (the one just added) is kept.
 */
export function resolveDataSelection(previous: readonly string[], next: readonly string[]): string[] {
  const result: string[] = [];
  for (const kind of ['game', 'overseer']) {
    const chosen = next.filter(value => value.startsWith(`${kind}:`));
    if (chosen.length === 0) continue;
    const added = chosen.find(value => !previous.includes(value));
    result.push(chosen.length > 1 && added ? added : chosen[chosen.length - 1]);
  }
  return result;
}

/** Writes the Data selection into the criteria. */
export function applyDataValues(criteria: AdminUserCriteria, values: readonly string[]): void {
  delete criteria.hasGameData;
  delete criteria.hasOverseerData;
  for (const value of values) {
    if (value === 'game:yes' || value === 'game:no') criteria.hasGameData = value === 'game:yes' ? 'yes' : 'no';
    if (value === 'overseer:yes' || value === 'overseer:no') criteria.hasOverseerData = value === 'overseer:yes' ? 'yes' : 'no';
  }
}

/** A key equal for equal criteria, whatever their field order or empty fields. */
export function criteriaKey(criteria: AdminUserCriteria | null | undefined): string {
  const c = criteria ?? {};
  return JSON.stringify([
    c.search?.trim() || null,
    c.statuses?.length ? [...c.statuses].sort() : null,
    c.emailConfirmed ?? null,
    c.registeredOlderThanDays ?? null,
    c.registeredWithinDays ?? null,
    c.lastSignIn || null,
    c.hasGameData ?? null,
    c.hasOverseerData ?? null
  ]);
}

/** Whether any criterion is set, the search included. */
export function hasAnyCriteria(criteria: AdminUserCriteria | null | undefined): boolean {
  return criteriaKey(criteria) !== criteriaKey({});
}

/** Whether a criterion other than the search is set. */
export function hasFilterCriteria(criteria: AdminUserCriteria | null | undefined): boolean {
  return hasAnyCriteria({ ...(criteria ?? {}), search: undefined });
}

/** A copy that shares no array with the original. */
export function cloneCriteria(criteria: AdminUserCriteria | null | undefined): AdminUserCriteria {
  const copy: AdminUserCriteria = { ...(criteria ?? {}) };
  if (copy.statuses) copy.statuses = [...copy.statuses];
  return copy;
}

/** The count the server gave for a key, 0 when absent. */
export function facetCount(counts: readonly AdminFacetCount[] | null | undefined, key: string): number {
  return counts?.find(entry => entry.key === key)?.count ?? 0;
}

/** Every facet's options with their counts; 0 until counted. */
export interface UserFacetOptions {
  status: FilterFacetOption[];
  email: FilterFacetOption[];
  registered: FilterFacetOption[];
  signin: FilterFacetOption[];
  data: FilterFacetOption[];
}

export function userFacetOptions(counts: AdminUserFacetCounts | null): UserFacetOptions {
  const registeredCount = (value: string): number => {
    if (value === 'between:7:30') {
      return Math.max(0, facetCount(counts?.registered, 'withinDays:30') - facetCount(counts?.registered, 'withinDays:7'));
    }
    return facetCount(counts?.registered, value);
  };
  const dataCount = (value: string): number => {
    const [kind, answer] = value.split(':');
    return facetCount(kind === 'game' ? counts?.hasGameData : counts?.hasOverseerData, answer);
  };
  return {
    status: STATUS_OPTIONS.map(o => ({ ...o, count: facetCount(counts?.statuses, o.value) })),
    email: EMAIL_OPTIONS.map(o => ({ ...o, count: facetCount(counts?.emailConfirmed, o.value) })),
    registered: REGISTERED_OPTIONS.map(o => ({ ...o, count: registeredCount(o.value) })),
    signin: LAST_SIGN_IN_OPTIONS.map(o => ({ ...o, count: facetCount(counts?.lastSignIn, o.value) })),
    data: DATA_OPTIONS.map(o => ({ ...o, count: dataCount(o.value) }))
  };
}

/** The statuses the criteria hold, as the facet takes them. */
export function statusValuesOf(criteria: AdminUserCriteria): AdminUserStatus[] {
  return [...(criteria.statuses ?? [])];
}
