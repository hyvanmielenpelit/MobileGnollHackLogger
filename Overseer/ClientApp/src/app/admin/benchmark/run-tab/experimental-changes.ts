import {
  BenchmarkChangeApplicability,
  BenchmarkChangeOverlapDto,
  BenchmarkExperimentalChangeDto,
  BenchmarkExperimentalChangeErrorDto,
  BenchmarkExperimentalChangeKind,
  BenchmarkInformationCondition
} from '../../../services/admin-benchmark.service';
import { parseServerUtcDate } from '../../../utils/date.util';

/** The separator of a spec's change ids. */
export const CHANGE_SPEC_SEPARATOR = '+';

/** The length of the selection hash a stamp appends after its `:`. */
export const STAMP_HASH_LENGTH = 12;

/** The read-out of a selection with no changes. */
export const NO_CHANGES_READOUT = 'None — same as the live chat';

/**
 * Plain names of the tools a change can add or edit: every tool in `Benchmark:AllowedTools`
 * (`Overseer/appsettings.json`) and the experimental ones; the names are the handlers' `ToolName`s
 * in `Overseer/Services/Tools/`.
 */
const TOOL_DISPLAY_NAMES: Readonly<Record<string, string>> = {
  wiki_search: 'wiki search',
  wiki_view: 'wiki page view',
  get_knowledge_article: 'knowledge base articles',
  nethack_wiki_search: 'NetHack wiki search',
  nethack_wiki_view: 'NetHack wiki page view',
  monster_lookup: 'monster lookup',
  item_lookup: 'item lookup',
  get_monster_stats: 'monster stats',
  get_item_stats: 'item stats',
  get_artifact_stats: 'artifact stats',
  get_constants: 'game constants',
  get_function_definition: 'source function view',
  search_definitions: 'source definition search',
  source_code_search: 'source code search',
  source_code_view: 'source code view',
  list_indexed_files: 'source file list',
  get_character_info: 'race and role lookup'
};

/** What each tool-output switch does, in plain words. */
const EXPERIMENT_LINES: Readonly<Record<string, string>> = {
  stats_referenced_by: 'Item, monster and artifact stats also list the places in the game\'s code that use them.'
};

/** The word a change's text names for each tool-output switch. */
const EXPERIMENT_MENTION_WORDS: Readonly<Record<string, string>> = {
  stats_referenced_by: 'referenced_by'
};

const CONDITION_NAMES: Readonly<Record<BenchmarkInformationCondition, string>> = {
  production: 'Production',
  sourceOnly: 'Source code only',
  closedBook: 'Closed book'
};

/** The kind tags' words, in the order the picker shows them. */
export const CHANGE_KIND_LABELS: Readonly<Record<BenchmarkExperimentalChangeKind, string>> = {
  newTool: 'New tool',
  toolOutput: 'Tool output',
  toolDescription: 'Tool description',
  instructions: 'Instructions'
};

const KIND_ORDER: readonly BenchmarkExperimentalChangeKind[] = ['newTool', 'toolOutput', 'toolDescription', 'instructions'];

const COUNT_WORDS: readonly string[] =
  ['zero', 'one', 'two', 'three', 'four', 'five', 'six', 'seven', 'eight', 'nine', 'ten', 'eleven', 'twelve'];

const MONTHS: readonly string[] = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];

/** What a tool-output switch does, in plain words; null for a switch without a description. */
export function experimentLine(experiment: string): string | null {
  return EXPERIMENT_LINES[experiment] ?? null;
}

/** A tool's plain name, or null when it has none and is shown by its identifier. */
export function toolDisplayName(tool: string): string | null {
  return TOOL_DISPLAY_NAMES[tool] ?? null;
}

/** A Tool Access choice's name. */
export function conditionName(condition: BenchmarkInformationCondition): string {
  return CONDITION_NAMES[condition] ?? String(condition);
}

/** Ordinal string order, the order the server sorts a spec's ids in. */
export function ordinalCompare(a: string, b: string): number {
  return a < b ? -1 : a > b ? 1 : 0;
}

/** *a*, *a and b*, *a, b and c*. */
export function joinNames(names: readonly string[]): string {
  return names.length <= 1
    ? names.join('')
    : `${names.slice(0, -1).join(', ')} and ${names[names.length - 1]}`;
}

/** *1 run*, *3 runs*. */
export function runCountText(count: number): string {
  return count === 1 ? '1 run' : `${count} runs`;
}

/** *1 change*, *3 changes*. */
export function changeCountText(count: number): string {
  return count === 1 ? '1 change' : `${count} changes`;
}

/** `24 Oct 2026`, local time; the raw value when it does not parse. */
export function changeDateText(utc: string | null | undefined): string {
  if (!utc) return '';
  const date = parseServerUtcDate(utc);
  if (isNaN(date.getTime())) return utc;
  return `${date.getDate()} ${MONTHS[date.getMonth()]} ${date.getFullYear()}`;
}

/** A change text as the server hashes it: CRLF and CR become LF, and the text is trimmed. */
export function normalizeChangeText(text: string | null | undefined): string {
  return (text ?? '').replace(/\r\n?/g, '\n').trim();
}

// --- Specs: change ids sorted ordinally and joined by `+` ---

/**
 * A spec's ids in their written order: [] for none (null, empty or whitespace), null for a malformed
 * spec (an empty id, a duplicate, or a stamp's `:`). A space stands for `+`, as a decoded query string
 * has it.
 */
export function parseChangeSpec(spec: string | null | undefined): string[] | null {
  const text = (spec ?? '').trim();
  if (text === '') return [];
  if (text.includes(':')) return null;
  const ids = text.replace(/\s+/g, CHANGE_SPEC_SEPARATOR).split(CHANGE_SPEC_SEPARATOR);
  if (ids.some(id => id === '')) return null;
  if (new Set(ids).size !== ids.length) return null;
  return ids;
}

/** The canonical spec of a set of ids: unique, sorted ordinally, joined by `+`; null for none. */
export function changeSpec(ids: Iterable<string>): string | null {
  const unique = [...new Set(ids)].filter(id => id !== '').sort(ordinalCompare);
  return unique.length > 0 ? unique.join(CHANGE_SPEC_SEPARATOR) : null;
}

/** The length of the stamp `<spec>:<hash12>` a run of the spec gets; 0 for none. */
export function stampLength(spec: string | null | undefined): number {
  return spec ? spec.length + 1 + STAMP_HASH_LENGTH : 0;
}

/** Whether a new run may tick the change: it is valid and not retired. */
export function isSelectable(change: BenchmarkExperimentalChangeDto): boolean {
  return change.isValid && !change.retired;
}

/** The ids of a spec the catalog does not offer to a new run, in spec order; the whole spec when it is malformed. */
export function unofferedIds(spec: string | null | undefined, changes: readonly BenchmarkExperimentalChangeDto[]): string[] {
  const ids = parseChangeSpec(spec);
  if (ids === null) return [(spec ?? '').trim()];
  return ids.filter(id => {
    const change = changes.find(c => c.id === id);
    return !change || !isSelectable(change);
  });
}

/** The spec's changes in canonical order, or null when the spec is malformed or names a change not offered; [] for none. */
export function resolveChangeSpec(
  spec: string | null | undefined, changes: readonly BenchmarkExperimentalChangeDto[]): BenchmarkExperimentalChangeDto[] | null {
  const ids = parseChangeSpec(spec);
  if (ids === null) return null;
  const resolved: BenchmarkExperimentalChangeDto[] = [];
  for (const id of [...ids].sort(ordinalCompare)) {
    const change = changes.find(c => c.id === id);
    if (!change || !isSelectable(change)) return null;
    resolved.push(change);
  }
  return resolved;
}

/** The note a remembered, URL or repeated spec leaves when it resets to None. */
export function changesResetNote(ids: readonly string[]): string {
  return ids.length === 1
    ? `Experimental Changes reset to None: ${ids[0]} is no longer offered.`
    : `Experimental Changes reset to None: ${joinNames(ids)} are no longer offered.`;
}

// --- Words ---

/** A change's title from the catalog, else its id. */
export function changeTitle(id: string, changes: readonly BenchmarkExperimentalChangeDto[]): string {
  return changes.find(c => c.id === id)?.title || id;
}

/** What a change contains, in tag order: from its `kinds`, and from its contents where the list misses one. */
export function changeKinds(change: BenchmarkExperimentalChangeDto): BenchmarkExperimentalChangeKind[] {
  const kinds = new Set<BenchmarkExperimentalChangeKind>(change.kinds ?? []);
  if (change.experimentalTool) kinds.add('newTool');
  if (change.experiment) kinds.add('toolOutput');
  if ((change.descriptionEdits?.length ?? 0) > 0) kinds.add('toolDescription');
  if (change.instructions?.trim()) kinds.add('instructions');
  return KIND_ORDER.filter(kind => kinds.has(kind));
}

/**
 * The launcher's read-out of a spec: *None — same as the live chat*, the one change's title, or
 * *2 changes: <title>, <title>*. A malformed spec is shown as it is.
 */
export function changeReadout(spec: string | null | undefined, changes: readonly BenchmarkExperimentalChangeDto[]): string {
  const ids = parseChangeSpec(spec);
  if (ids === null) return (spec ?? '').trim();
  if (ids.length === 0) return NO_CHANGES_READOUT;
  const titles = [...ids].sort(ordinalCompare).map(id => changeTitle(id, changes));
  return titles.length === 1 ? titles[0] : `${changeCountText(titles.length)}: ${titles.join(', ')}`;
}

/** One ticked change as the launcher's summary box lists it. */
export interface ChangeSummaryLine {
  id: string;
  title: string;
  summary: string;
}

/** The summary box's lines: each change of the spec, title and summary, in spec order. */
export function changeSummaryLines(
  spec: string | null | undefined, changes: readonly BenchmarkExperimentalChangeDto[]): ChangeSummaryLine[] {
  const ids = parseChangeSpec(spec) ?? [];
  return [...ids].sort(ordinalCompare).map(id => {
    const change = changes.find(c => c.id === id);
    return { id, title: change?.title || id, summary: change?.summary ?? '' };
  });
}

/** How much of a change reaches the model under a Tool Access choice; `full` when the catalog does not say. */
export function applicabilityOf(
  change: BenchmarkExperimentalChangeDto, condition: BenchmarkInformationCondition): BenchmarkChangeApplicability {
  return change.appliesUnder?.[condition] ?? 'full';
}

/**
 * What of a change does not reach the model under a Tool Access choice, as one line; null when all of
 * it does. Under Closed book only instructions reach the model; under Source code only an edit of a
 * tool that condition does not offer does not.
 */
export function applicabilityLine(change: BenchmarkExperimentalChangeDto, condition: BenchmarkInformationCondition): string | null {
  const applies = applicabilityOf(change, condition);
  if (applies === 'full') return null;
  if (condition === 'closedBook') {
    return applies === 'none'
      ? 'Has no effect under Closed book: the AI gets no tools there.'
      : 'Under Closed book only its instructions reach the AI: the AI gets no tools there.';
  }
  if (condition === 'sourceOnly') {
    const tools = change.notApplyingUnderSourceOnly ?? [];
    if (tools.length === 0) {
      return applies === 'none' ? 'Has no effect under Source code only.' : 'Under Source code only part of it does not reach the AI.';
    }
    const names = joinNames(tools);
    if (applies === 'none') {
      return `Has no effect under Source code only: ${names} ${tools.length === 1 ? 'is' : 'are'} not offered there.`;
    }
    return tools.length === 1
      ? `Under Source code only its edit of ${names} does not reach the AI: that tool is not offered there.`
      : `Under Source code only its edits of ${names} do not reach the AI: those tools are not offered there.`;
  }
  const name = conditionName(condition);
  return applies === 'none' ? `Has no effect under ${name}.` : `Under ${name} part of it does not reach the AI.`;
}

/** Why two changes cannot run together, as a clause; null when they can. */
export function conflictBetween(a: BenchmarkExperimentalChangeDto, b: BenchmarkExperimentalChangeDto): string | null {
  if (a.id === b.id) return null;
  if (!(a.conflictsWith ?? []).includes(b.id) && !(b.conflictsWith ?? []).includes(a.id)) return null;
  const toolA = a.experimentalTool?.name;
  if (toolA && toolA === b.experimentalTool?.name) return `both offer ${toolA}`;
  if (a.experiment && a.experiment === b.experiment) return `both turn on the ${a.experiment} output switch`;
  const editsB = b.descriptionEdits ?? [];
  for (const edit of a.descriptionEdits ?? []) {
    for (const other of editsB) {
      if (edit.tool !== other.tool) continue;
      if (edit.mode === 'replace' && other.mode === 'replace') return `both replace the ${edit.tool} description`;
      if (edit.mode !== other.mode) return `one replaces the ${edit.tool} description the other adds to`;
    }
  }
  return 'they cannot run together';
}

/** Why a change cannot be ticked beside the ticked ones: *Cannot run with <title>: both replace …*; null when it can. */
export function conflictReason(
  change: BenchmarkExperimentalChangeDto, selectedIds: Iterable<string>, changes: readonly BenchmarkExperimentalChangeDto[]): string | null {
  for (const id of [...selectedIds].sort(ordinalCompare)) {
    if (id === change.id) continue;
    const other = changes.find(c => c.id === id);
    if (!other) continue;
    const reason = conflictBetween(change, other);
    if (reason) return `Cannot run with ${other.title}: ${reason}.`;
  }
  return null;
}

/** The word a change's text names for another change: its experimental tool, its switch's word, else its title. */
export function mentionSubject(change: BenchmarkExperimentalChangeDto): string {
  if (change.experimentalTool?.name) return change.experimentalTool.name;
  if (change.experiment) return EXPERIMENT_MENTION_WORDS[change.experiment] ?? change.experiment;
  return change.title;
}

/** A ticked change whose texts name a change that is not ticked. */
export interface MissingMention {
  change: BenchmarkExperimentalChangeDto;
  missing: BenchmarkExperimentalChangeDto;
}

/** Each ticked change's mention of a selectable change that is not ticked, in spec order. */
export function missingMentions(
  selectedIds: Iterable<string>, changes: readonly BenchmarkExperimentalChangeDto[]): MissingMention[] {
  const selected = new Set(selectedIds);
  const result: MissingMention[] = [];
  for (const id of [...selected].sort(ordinalCompare)) {
    const change = changes.find(c => c.id === id);
    if (!change) continue;
    for (const mentioned of change.mentions ?? []) {
      if (selected.has(mentioned) || result.some(m => m.change.id === change.id && m.missing.id === mentioned)) continue;
      const missing = changes.find(c => c.id === mentioned);
      if (missing && isSelectable(missing)) result.push({ change, missing });
    }
  }
  return result;
}

/** *Answer from the player's own character mentions get_character_info, which this run does not offer.* */
export function mentionLine(mention: MissingMention): string {
  return `${mention.change.title} mentions ${mentionSubject(mention.missing)}, which this run does not offer.`;
}

/**
 * A Used before button's label: *All four* when the ids are every selectable built-in change (two or
 * more), else the titles joined by ` + `.
 */
export function selectionLabel(ids: readonly string[], changes: readonly BenchmarkExperimentalChangeDto[]): string {
  const builtIns = changes.filter(c => c.source === 'builtIn' && isSelectable(c)).map(c => c.id);
  const set = new Set(ids);
  if (builtIns.length >= 2 && set.size === builtIns.length && builtIns.every(id => set.has(id))) {
    return `All ${COUNT_WORDS[builtIns.length] ?? String(builtIns.length)}`;
  }
  return [...set].sort(ordinalCompare).map(id => changeTitle(id, changes)).join(' + ');
}

/** *Shares "…" with the rubric of question 3 of Gnoll Basics.* */
export function overlapLine(overlap: BenchmarkChangeOverlapDto): string {
  const where = overlap.field === 'rubric'
    ? `the rubric of question ${overlap.questionNumber}`
    : `question ${overlap.questionNumber}`;
  return `Shares "${overlap.text}" with ${where} of ${overlap.suiteName}.`;
}

// --- The editor's entry modes ---

/** How the editor opens: a new change, an edit of one, or a new version of one. */
export type ExperimentalChangeEditorMode = 'new' | 'edit' | 'copy';

/** What the picker asks its host to open the editor with. */
export interface ExperimentalChangeEditorRequest {
  mode: ExperimentalChangeEditorMode;
  /** The change edited or copied; null for a new one. */
  change: BenchmarkExperimentalChangeDto | null;
}

/** A save the editor made. */
export interface ExperimentalChangeSaved {
  change: BenchmarkExperimentalChangeDto;
  /** True for a new change or a new version, false for an edit. */
  created: boolean;
}

// --- Errors ---

/** The body of a refused experimental-changes request, when it has the `{ error }` shape. */
export function changeErrorBody(err: unknown): BenchmarkExperimentalChangeErrorDto | null {
  const body = (err as { error?: unknown } | null)?.error;
  if (body && typeof body === 'object' && typeof (body as { error?: unknown }).error === 'string') {
    return body as BenchmarkExperimentalChangeErrorDto;
  }
  return null;
}

/** A refused request's message: the `{ error }` body, a string body, a `message`, else the fallback. */
export function changeErrorText(err: unknown, fallback: string): string {
  const body = (err as { error?: unknown } | null)?.error;
  if (typeof body === 'string' && body.trim() !== '') return body;
  const parsed = changeErrorBody(err);
  if (parsed?.error) return parsed.error;
  const message = (body as { message?: unknown } | null | undefined)?.message;
  return typeof message === 'string' && message !== '' ? message : fallback;
}
