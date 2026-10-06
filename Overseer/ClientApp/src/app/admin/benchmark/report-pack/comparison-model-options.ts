import type { ModelPickerModel, ModelPickerOption } from '../../../shared/model-picker/model-picker.component';
import type { BenchmarkModelComparisonEntryDto } from '../model-comparison/model-comparison.models';

/*
 * Step 3's *Models* picker over the comparison's entries: one option per entry that is not Excluded,
 * keyed by its entry key, drawn as the model with the single model picker's badges. Two entries of
 * one model stay two options, each then naming its source.
 */

/** The most models one comparison-scope document covers; the charts' `MAX_PLOTTED_ENTRIES`. */
export const MAX_COMPARISON_DOCUMENT_ENTRIES = 12;

/** The fewest models one comparison-scope document covers. */
export const MIN_COMPARISON_DOCUMENT_ENTRIES = 2;

/** A comparison entry as the Models picker offers it. */
export interface ComparisonModelPickerModel extends ModelPickerModel {
  readonly entryKey: string;
  /** The model's name with its thinking level: `GPT-5.6 Luna (max)`. */
  readonly label: string;
  /** Where the entry comes from, in step 1's terms: `Battery run #10`. */
  readonly source: string;
  /** The entry's Intelligence Index; null when it was not measured. */
  readonly intelligenceIndex: number | null;
}

export type ComparisonModelOption = ModelPickerOption<ComparisonModelPickerModel>;

/** Whether an entry can be covered by a document: every entry that is not Excluded. */
export function isOfferedEntry(entry: BenchmarkModelComparisonEntryDto): boolean {
  return !entry.excluded && entry.state !== 'Excluded';
}

/** `Run #31`, `Analysis group #4`, `Battery run #10`: the table step 1 lists the source in, and its id. */
export function comparisonEntrySource(entry: Pick<BenchmarkModelComparisonEntryDto, 'sourceKind' | 'sourceId'>): string {
  switch (entry.sourceKind) {
    case 'Group': return `Analysis group #${entry.sourceId}`;
    case 'Battery': return `Battery run #${entry.sourceId}`;
    default: return `Run #${entry.sourceId}`;
  }
}

/** The model's display name, with its thinking level in parentheses when it has one: `Claude 5 Opus (high)`. */
export function comparisonModelLabel(entry: BenchmarkModelComparisonEntryDto): string {
  const name = entry.modelDisplayName || entry.modelId || entry.label;
  const level = entry.thinkingLevel?.trim();
  return level ? `${name} (${level})` : name;
}

/** What an option shows besides its detail: the name and every badge the picker draws for it. */
function lookOf(entry: BenchmarkModelComparisonEntryDto): string {
  return [
    entry.modelDisplayName || entry.modelId || '',
    entry.provider ?? '',
    entry.thinkingLevel ?? '',
    entry.reasoningMode ?? ''
  ].join('\u0000').toLowerCase();
}

/**
 * The entries that are not Excluded, as Models picker options in the comparison's order. An option
 * that would look exactly like another one (same name, provider, thinking level and reasoning mode)
 * carries its source as its detail; no other option does.
 */
export function comparisonModelOptions(entries: readonly BenchmarkModelComparisonEntryDto[]): ComparisonModelOption[] {
  const offered = entries.filter(isOfferedEntry);
  const looks = new Map<string, number>();
  for (const entry of offered) {
    const look = lookOf(entry);
    looks.set(look, (looks.get(look) ?? 0) + 1);
  }
  return offered.map(entry => {
    const source = comparisonEntrySource(entry);
    const model: ComparisonModelPickerModel = {
      entryKey: entry.key,
      label: comparisonModelLabel(entry),
      source,
      intelligenceIndex: entry.quality?.pointEstimate ?? null,
      displayName: entry.modelDisplayName || entry.label,
      modelId: entry.modelId,
      provider: entry.provider,
      thinkingLevel: entry.thinkingLevel ?? null,
      reasoningMode: entry.reasoningMode ?? null
    };
    return (looks.get(lookOf(entry)) ?? 0) > 1
      ? { key: entry.key, model, detail: source }
      : { key: entry.key, model };
  });
}

/** The options by Intelligence Index, highest first; an unmeasured one last; ties by entry key. */
export function optionsByIntelligence(options: readonly ComparisonModelOption[]): ComparisonModelOption[] {
  return [...options].sort((a, b) => {
    const left = a.model.intelligenceIndex;
    const right = b.model.intelligenceIndex;
    if (left !== right) {
      if (left === null) {
        return 1;
      }
      if (right === null) {
        return -1;
      }
      return right - left;
    }
    const x = String(a.key);
    const y = String(b.key);
    return x < y ? -1 : x > y ? 1 : 0;
  });
}

/**
 * The models a comparison-wide document set starts with: every option, or with more than `max` the
 * `max` of highest Intelligence Index; in the options' order.
 */
export function defaultCoveredKeys(
  options: readonly ComparisonModelOption[],
  max: number = MAX_COMPARISON_DOCUMENT_ENTRIES
): string[] {
  const chosen = new Set(optionsByIntelligence(options).slice(0, max).map(option => String(option.key)));
  return options.map(option => String(option.key)).filter(key => chosen.has(key));
}

/** The model a per-model document set starts with: the highest Intelligence Index, or none. */
export function defaultSubjectKey(options: readonly ComparisonModelOption[]): string | null {
  const best = optionsByIntelligence(options)[0];
  return best ? String(best.key) : null;
}

/**
 * How a sentence names one option: its label, followed by its source where another option has the
 * same label (`GPT-5.6 Luna (max) from Battery run #10`).
 */
export function comparisonModelMention(key: string, options: readonly ComparisonModelOption[]): string {
  const option = options.find(candidate => candidate.key === key);
  if (!option) {
    return key;
  }
  const twice = options.filter(candidate => candidate.model.label === option.model.label).length > 1;
  return twice ? `${option.model.label} from ${option.model.source}` : option.model.label;
}
