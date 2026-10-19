import type { MultiPickerNavigateIntent } from '../multi-picker/multi-picker.models';
import thinkingLevelOrder from '../../../../../Services/ModelCatalogs/ThinkingLevelOrder.json';

/**
 * The view a model picker's toolbar sets over its options: search, sort (with the thinking-level
 * order) and provider filter. Pure functions, shared by `app-model-picker` and
 * `app-model-multi-picker`; the only side effect is `localStorage`, behind try/catch.
 */

export type ModelPickerSort = 'default' | 'name-asc' | 'name-desc' | 'release-desc' | 'release-asc';

/** How options that differ only in thinking level are ordered among themselves under a sort. */
export type ModelPickerThinkingOrder = 'lowest-first' | 'highest-first';

export interface ModelPickerViewState {
  sort: ModelPickerSort;
  /** Null is not set; always null under the default order. */
  thinking: ModelPickerThinkingOrder | null;
  /** Lower-cased provider keys; empty means every provider. */
  providers: string[];
  search: string;
}

export const DEFAULT_MODEL_PICKER_VIEW: ModelPickerViewState = Object.freeze({
  sort: 'default',
  thinking: null,
  providers: Object.freeze([]) as readonly string[] as string[],
  search: ''
}) as ModelPickerViewState;

/**
 * The fewest options (the none option excluded) at which `viewTools="auto"` shows the toolbar. With
 * one option there is nothing to sort or narrow.
 */
export const MODEL_PICKER_TOOLS_MIN_OPTIONS = 2;

export type ModelPickerViewTools = 'auto' | 'always' | 'never';

export type ModelPickerNavigateIntent = MultiPickerNavigateIntent;

export const MODEL_PICKER_VIEW_STORAGE_PREFIX = 'overseer.modelPicker.view.';

const STORAGE_VERSION = 1;

export const MODEL_PICKER_SORT_LABELS: Readonly<Record<ModelPickerSort, string>> = {
  'default': 'Default order',
  'name-asc': 'Name (A–Z)',
  'name-desc': 'Name (Z–A)',
  'release-desc': 'Release date (newest first)',
  'release-asc': 'Release date (oldest first)'
};

const SORTS = Object.keys(MODEL_PICKER_SORT_LABELS) as ModelPickerSort[];

export const MODEL_PICKER_THINKING_LABELS: Readonly<Record<ModelPickerThinkingOrder, string>> = {
  'lowest-first': 'Lowest first',
  'highest-first': 'Highest first'
};

const THINKING_ORDERS = Object.keys(MODEL_PICKER_THINKING_LABELS) as ModelPickerThinkingOrder[];

/** The fields the view reads. Structural, so `ModelPickerOption` fits. */
export interface ModelPickerViewModel {
  displayName?: string | null;
  modelId?: string | null;
  provider?: string | null;
  releaseDate?: string | null;
  thinkingLevel?: string | null;
}

export interface ModelPickerViewOption {
  model: ModelPickerViewModel;
  tag?: string;
  group?: string;
}

export interface ModelPickerProviderFacet {
  /** The provider as the first option spells it. */
  provider: string;
  /** Lower-cased and trimmed: what the filter stores and compares. */
  key: string;
  count: number;
}

export interface ModelPickerMatchPart {
  text: string;
  match: boolean;
}

const collator = new Intl.Collator('en', { numeric: true, sensitivity: 'base' });

const RELEASE_DATE = /^\d{4}-\d{2}-\d{2}$/;

export function showsViewTools(mode: ModelPickerViewTools, optionCount: number): boolean {
  if (mode === 'always') return true;
  if (mode === 'never') return false;
  return optionCount >= MODEL_PICKER_TOOLS_MIN_OPTIONS;
}

export function providerKey(provider: string | null | undefined): string {
  return (provider ?? '').trim().toLowerCase();
}

function defaultView(): ModelPickerViewState {
  return { sort: 'default', thinking: null, providers: [], search: '' };
}

/**
 * The view with a new sort. The default order is no sorting at all, so it clears the thinking-level
 * order; a sort chosen from the default order starts at lowest first; a move between two other
 * sorts keeps the thinking-level order as it is.
 */
export function withSort(state: ModelPickerViewState, sort: ModelPickerSort): ModelPickerViewState {
  let thinking = state.thinking;
  if (sort === 'default') thinking = null;
  else if (state.sort === 'default' && thinking === null) thinking = 'lowest-first';
  return { ...state, sort, thinking };
}

/** The view with its sorting cleared and its search and provider filter kept. */
export function withoutSorting(state: ModelPickerViewState): ModelPickerViewState {
  return { ...state, sort: 'default', thinking: null };
}

/** The stored thinking-level order; a missing or unknown one reads as `withSort` would have set it. */
function storedThinking(parsed: Record<string, unknown>, sort: ModelPickerSort): ModelPickerThinkingOrder | null {
  if (sort === 'default') return null;
  const stored = parsed['thinking'];
  if (stored === null) return null;
  return THINKING_ORDERS.includes(stored as ModelPickerThinkingOrder) ? stored as ModelPickerThinkingOrder : 'lowest-first';
}

/** The stored view for `key`; the default when there is none, it is unreadable, or the key is null. */
export function readModelPickerView(key: string | null): ModelPickerViewState {
  if (!key) return defaultView();
  try {
    const raw = localStorage.getItem(MODEL_PICKER_VIEW_STORAGE_PREFIX + key);
    if (!raw) return defaultView();
    const parsed = JSON.parse(raw) as Record<string, unknown> | null;
    if (!parsed || typeof parsed !== 'object' || parsed['version'] !== STORAGE_VERSION) return defaultView();
    const sort = SORTS.includes(parsed['sort'] as ModelPickerSort) ? parsed['sort'] as ModelPickerSort : 'default';
    const providers = Array.isArray(parsed['providers'])
      ? [...new Set((parsed['providers'] as unknown[])
        .filter((p): p is string => typeof p === 'string')
        .map(p => providerKey(p))
        .filter(p => p !== ''))]
      : [];
    const search = typeof parsed['search'] === 'string' ? parsed['search'] : '';
    return { sort, thinking: storedThinking(parsed, sort), providers, search };
  } catch {
    return defaultView();
  }
}

/** Stores the view under `key`; the default view removes the entry, and a null key stores nothing. */
export function writeModelPickerView(key: string | null, state: ModelPickerViewState): void {
  if (!key) return;
  try {
    const storageKey = MODEL_PICKER_VIEW_STORAGE_PREFIX + key;
    if (!isViewActive(state)) {
      localStorage.removeItem(storageKey);
      return;
    }
    localStorage.setItem(storageKey, JSON.stringify({
      version: STORAGE_VERSION,
      sort: state.sort,
      thinking: state.thinking,
      providers: state.providers,
      search: state.search
    }));
  } catch {
    // Storage unavailable: the view is simply not remembered.
  }
}

/** Whether the view differs from the default: a sort, a provider filter or search text. */
export function isViewActive(state: ModelPickerViewState): boolean {
  return state.sort !== 'default' || state.providers.length > 0 || state.search.trim() !== '';
}

/** Whether the view hides models: a search or a provider filter. Pass the effective view. */
export function viewNarrows(state: ModelPickerViewState): boolean {
  return state.search.trim() !== '' || state.providers.length > 0;
}

/** The view with its search and provider filter cleared and its sort kept. */
export function withoutNarrowing(state: ModelPickerViewState): ModelPickerViewState {
  return { sort: state.sort, thinking: state.thinking, providers: [], search: '' };
}

/** The filter button's accessible name for the effective view: whether it is filtered, sorted, or both. */
export function toolsToggleLabel(state: ModelPickerViewState): string {
  const filtered = viewNarrows(state);
  const sorted = state.sort !== 'default';
  const base = 'Filter and sort models';
  if (filtered && sorted) return `${base} (filtered and sorted)`;
  if (filtered) return `${base} (filtered)`;
  if (sorted) return `${base} (sorted)`;
  return base;
}

export function sameModelPickerView(a: ModelPickerViewState, b: ModelPickerViewState): boolean {
  return a.search === b.search && onlySearchChanged(a, b);
}

/** Whether the two views differ, if at all, only in their search text. */
export function onlySearchChanged(previous: ModelPickerViewState, next: ModelPickerViewState): boolean {
  return previous.sort === next.sort && previous.thinking === next.thinking
    && previous.providers.length === next.providers.length
    && previous.providers.every((p, i) => p === next.providers[i]);
}

/** Trimmed, lower-cased, split on whitespace. */
export function searchTerms(search: string): string[] {
  return search.trim().toLowerCase().split(/\s+/).filter(term => term !== '');
}

export function modelPickerName(model: ModelPickerViewModel): string {
  return model.displayName || model.modelId || '';
}

function searchableText(option: ModelPickerViewOption): string {
  return [option.model.displayName, option.model.modelId, option.tag]
    .filter(Boolean)
    .join(' ')
    .toLowerCase();
}

/** One facet per provider present, by name. */
export function providerFacets(options: readonly ModelPickerViewOption[]): ModelPickerProviderFacet[] {
  const byKey = new Map<string, ModelPickerProviderFacet>();
  for (const option of options) {
    const key = providerKey(option.model.provider);
    if (!key) continue;
    const facet = byKey.get(key);
    if (facet) {
      facet.count++;
    } else {
      byKey.set(key, { provider: (option.model.provider ?? '').trim(), key, count: 1 });
    }
  }
  return [...byKey.values()].sort((a, b) => collator.compare(a.provider, b.provider));
}

/** The standardized thinking levels, lowest first. */
export const THINKING_LEVEL_ORDER: readonly string[] = Object.freeze([...thinkingLevelOrder.levels]);

const UNRECOGNIZED_THINKING_RANK = THINKING_LEVEL_ORDER.length;
const UNSET_THINKING_RANK = THINKING_LEVEL_ORDER.length + 1;

/** The level's index in `THINKING_LEVEL_ORDER`; an unrecognized level ranks after all of them, an unset one after that. */
export function thinkingLevelRank(level: string | null | undefined): number {
  const key = (level ?? '').trim().toLowerCase();
  if (!key) return UNSET_THINKING_RANK;
  const rank = THINKING_LEVEL_ORDER.indexOf(key);
  return rank >= 0 ? rank : UNRECOGNIZED_THINKING_RANK;
}

/**
 * Recognized levels in the order's direction; unrecognized and then unset levels after them in
 * both directions, two unrecognized levels by name.
 */
function compareThinking(a: string | null | undefined, b: string | null | undefined, order: ModelPickerThinkingOrder): number {
  const rankA = thinkingLevelRank(a);
  const rankB = thinkingLevelRank(b);
  if (rankA !== rankB) {
    if (rankA >= UNRECOGNIZED_THINKING_RANK || rankB >= UNRECOGNIZED_THINKING_RANK) return rankA - rankB;
    return order === 'lowest-first' ? rankA - rankB : rankB - rankA;
  }
  return rankA === UNRECOGNIZED_THINKING_RANK ? collator.compare(a!.trim(), b!.trim()) : 0;
}

/**
 * Whether two options of one group have the same name and model id and differ in thinking level
 * alone, so the thinking-level order has something to order.
 */
export function hasThinkingLevelChoice(options: readonly ModelPickerViewOption[]): boolean {
  const levelOf = (option: ModelPickerViewOption): string => (option.model.thinkingLevel ?? '').trim().toLowerCase();
  for (let i = 0; i < options.length; i++) {
    const a = options[i];
    for (let j = i + 1; j < options.length; j++) {
      const b = options[j];
      if (a.group === b.group
        && levelOf(a) !== levelOf(b)
        && collator.compare(modelPickerName(a.model), modelPickerName(b.model)) === 0
        && collator.compare(a.model.modelId ?? '', b.model.modelId ?? '') === 0) {
        return true;
      }
    }
  }
  return false;
}

function releaseDateOf(model: ModelPickerViewModel): string | null {
  const date = model.releaseDate?.trim();
  return date && RELEASE_DATE.test(date) ? date : null;
}

export function hasReleaseDates(options: readonly ModelPickerViewOption[]): boolean {
  return options.some(option => releaseDateOf(option.model) !== null);
}

export function isReleaseSort(sort: ModelPickerSort): boolean {
  return sort === 'release-desc' || sort === 'release-asc';
}

/**
 * The view as it applies to these options: providers no option has are dropped, a release-date
 * sort falls back to the default order when no option has a date, and the thinking-level order is
 * unset under the default order or where no options differ in thinking level alone. The stored view
 * is left as it is.
 */
export function effectiveView(state: ModelPickerViewState, options: readonly ModelPickerViewOption[]): ModelPickerViewState {
  const present = new Set(options.map(option => providerKey(option.model.provider)));
  const providers = state.providers.filter(p => present.has(p));
  const sort = isReleaseSort(state.sort) && !hasReleaseDates(options) ? 'default' : state.sort;
  const thinking = sort === 'default' || state.thinking === null || !hasThinkingLevelChoice(options) ? null : state.thinking;
  return { sort, thinking, providers, search: state.search };
}

/**
 * The indices of the options the view shows, in display order. Each run of consecutive options
 * sharing a `group` is sorted on its own, and the runs keep their order.
 */
export function applyModelPickerView(options: readonly ModelPickerViewOption[], state: ModelPickerViewState): number[] {
  const terms = searchTerms(state.search);
  const providers = new Set(state.providers);
  const visible = (option: ModelPickerViewOption): boolean => {
    if (providers.size > 0 && !providers.has(providerKey(option.model.provider))) return false;
    if (terms.length === 0) return true;
    const text = searchableText(option);
    return terms.every(term => text.includes(term));
  };

  const compare = comparator(options, state.sort, state.thinking);
  const result: number[] = [];
  let run: number[] = [];
  let runGroup: string | undefined;
  const flush = (): void => {
    if (compare) run.sort(compare);
    result.push(...run);
    run = [];
  };
  options.forEach((option, index) => {
    if (index > 0 && option.group !== runGroup) flush();
    runGroup = option.group;
    if (visible(option)) run.push(index);
  });
  flush();
  return result;
}

/**
 * The default order is the host's, unsorted. Under any other sort, ties after name and model id
 * break on thinking level in its own direction, then on the original index.
 */
function comparator(options: readonly ModelPickerViewOption[], sort: ModelPickerSort,
                    thinking: ModelPickerThinkingOrder | null): ((a: number, b: number) => number) | null {
  if (sort === 'default') return null;
  const nameOf = (i: number): string => modelPickerName(options[i].model);
  const idOf = (i: number): string => options[i].model.modelId ?? '';
  const byName = (a: number, b: number): number =>
    collator.compare(nameOf(a), nameOf(b)) || collator.compare(idOf(a), idOf(b));
  const byThinking = (a: number, b: number): number =>
    thinking === null ? 0 : compareThinking(options[a].model.thinkingLevel, options[b].model.thinkingLevel, thinking);
  if (sort === 'name-asc') return (a, b) => byName(a, b) || byThinking(a, b) || a - b;
  if (sort === 'name-desc') return (a, b) => byName(b, a) || byThinking(a, b) || a - b;
  const newestFirst = sort === 'release-desc';
  return (a, b) => {
    const dateA = releaseDateOf(options[a].model);
    const dateB = releaseDateOf(options[b].model);
    if (dateA !== dateB) {
      // An unknown date sorts last in both directions.
      if (dateA === null) return 1;
      if (dateB === null) return -1;
      return newestFirst ? (dateA < dateB ? 1 : -1) : (dateA < dateB ? -1 : 1);
    }
    return byName(a, b) || byThinking(a, b) || a - b;
  };
}

/** The text split into runs that match a search term and runs that do not, case-insensitively. */
export function matchParts(text: string, terms: readonly string[]): ModelPickerMatchPart[] {
  if (!text) return [];
  const lower = text.toLowerCase();
  // A case mapping that changes the length would misalign the runs.
  if (terms.length === 0 || lower.length !== text.length) return [{ text, match: false }];
  const marked = new Array<boolean>(text.length).fill(false);
  for (const term of terms) {
    if (!term) continue;
    for (let at = lower.indexOf(term); at >= 0; at = lower.indexOf(term, at + 1)) {
      marked.fill(true, at, at + term.length);
    }
  }
  const parts: ModelPickerMatchPart[] = [];
  let start = 0;
  for (let i = 1; i <= text.length; i++) {
    if (i === text.length || marked[i] !== marked[start]) {
      parts.push({ text: text.slice(start, i), match: marked[start] });
      start = i;
    }
  }
  return parts;
}

/** `Mar 2026` for `2026-03-14`, read in UTC; an empty string for a missing or malformed date. */
export function releaseMonthLabel(date: string | null | undefined): string {
  const value = date?.trim();
  if (!value || !RELEASE_DATE.test(value)) return '';
  const [year, month, day] = value.split('-').map(Number);
  const parsed = new Date(Date.UTC(year, month - 1, day));
  if (Number.isNaN(parsed.getTime()) || parsed.getUTCMonth() !== month - 1) return '';
  return parsed.toLocaleDateString('en-US', { month: 'short', year: 'numeric', timeZone: 'UTC' });
}

const SEARCH_WRITE_DELAY_MS = 300;

/**
 * Writes a picker's view to storage: search keystrokes after 300 ms of quiet, every other change at
 * once. `flush()` writes a pending search; `dispose()` flushes and stops.
 */
export class ModelPickerViewWriter {
  private pending: ModelPickerViewState | null = null;
  private timer: ReturnType<typeof setTimeout> | null = null;

  constructor(private readonly key: () => string | null) {}

  /** `searchOnly` marks a change of the search text alone, which waits for the quiet period. */
  write(state: ModelPickerViewState, searchOnly: boolean): void {
    if (!searchOnly) {
      this.cancel();
      writeModelPickerView(this.key(), state);
      return;
    }
    this.pending = state;
    if (this.timer !== null) clearTimeout(this.timer);
    this.timer = setTimeout(() => this.flush(), SEARCH_WRITE_DELAY_MS);
  }

  flush(): void {
    const pending = this.pending;
    this.cancel();
    if (pending) writeModelPickerView(this.key(), pending);
  }

  dispose(): void {
    this.flush();
  }

  private cancel(): void {
    this.pending = null;
    if (this.timer !== null) {
      clearTimeout(this.timer);
      this.timer = null;
    }
  }
}
