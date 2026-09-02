/** Model role bit values, matching the server-side ModelRole flags enum. */
export const ROLE_CHAT = 1;
export const ROLE_TITLE = 2;
export const ROLE_BENCHMARK = 4;

/** How several ticked boxes inside one facet combine. */
export type MatchMode = 'any' | 'all';   // 'any' = OR, 'all' = AND

export interface ConfigFilter {
  /** Ticked role bits. Empty array = this facet imposes no constraint. */
  roles: number[];
  /** OR ('any') or AND ('all') across the ticked role bits. */
  roleMatchMode: MatchMode;
  /** Ticked provider names. Empty array = no constraint. Always OR — see User Review 1. */
  providers: string[];
}

export interface FilterChip {
  kind: 'role' | 'provider';
  /** The role bit or the provider name this chip removes. */
  value: number | string;
  /** Visible chip text. */
  label: string;
  /** Badge class from styles.scss, so a chip matches the badge on the cards below. */
  cssClass: string;
}

/**
 * The role facet's options, in display order. Exported so the component renders them
 * with @for rather than three hand-copied checkbox blocks, and so a template can reach
 * them — an Angular template cannot see module-level constants, only instance members,
 * which is why the component re-exposes this as a readonly field.
 */
export const ROLE_OPTIONS: ReadonlyArray<{ bit: number; label: string; cssClass: string }> = [
  { bit: ROLE_CHAT,      label: 'Chat',      cssClass: 'badge-role-chat' },
  { bit: ROLE_TITLE,     label: 'Title',     cssClass: 'badge-role-title' },
  { bit: ROLE_BENCHMARK, label: 'Benchmark', cssClass: 'badge-role-benchmark' }
];

/**
 * A FACTORY, not a shared constant. `{ ...EMPTY_FILTER }` is a shallow copy, so every
 * "empty" filter made that way would share one `roles` array and one `providers` array.
 * Nothing in this plan mutates them — but the first person who reaches for `.push()`
 * would corrupt the empty filter for the whole application, silently.
 */
export function createEmptyFilter(): ConfigFilter {
  return { roles: [], roleMatchMode: 'any', providers: [] };
}

export function isFilterActive(f: ConfigFilter): boolean {
  return f.roles.length > 0 || f.providers.length > 0;
}

export function activeFilterChips(filter: ConfigFilter, providers: string[]): FilterChip[] {
  const chips: FilterChip[] = [];
  
  for (const opt of ROLE_OPTIONS) {
    if (filter.roles.includes(opt.bit)) {
      chips.push({ kind: 'role', value: opt.bit, label: opt.label, cssClass: opt.cssClass });
    }
  }

  for (const p of providers) {
    if (filter.providers.includes(p)) {
      chips.push({ kind: 'provider', value: p, label: p, cssClass: '' });
    }
  }

  return chips;
}

export function matchesFilter(config: { modelRole: number; provider: string }, filter: ConfigFilter): boolean {
  // Role facet: empty matches all; non-empty checks any/all
  if (filter.roles.length > 0) {
    if (filter.roleMatchMode === 'all') {
      const matchAll = filter.roles.every(bit => (config.modelRole & bit) !== 0);
      if (!matchAll) {
        return false;
      }
    } else {
      const matchAny = filter.roles.some(bit => (config.modelRole & bit) !== 0);
      if (!matchAny) {
        return false;
      }
    }
  }

  // Provider facet: empty matches all; non-empty checks OR
  if (filter.providers.length > 0) {
    if (!filter.providers.includes(config.provider)) {
      return false;
    }
  }

  return true;
}
