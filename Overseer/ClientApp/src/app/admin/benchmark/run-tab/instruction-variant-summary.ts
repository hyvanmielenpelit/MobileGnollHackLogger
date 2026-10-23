import {
  BenchmarkInformationCondition,
  BenchmarkInstructionVariantDto,
  BenchmarkInstructionVariantFeatureDto,
  BenchmarkVariantFeatureKind
} from '../../../services/admin-benchmark.service';

/** A piece of a summary line: plain text, or an identifier shown as code. */
export interface VariantSummarySegment {
  text: string;
  code: boolean;
}

/**
 * Plain names of the tools a variant can add or reword: every tool in `Benchmark:AllowedTools`
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

/** What a tool-output switch does, in plain words; null for a switch without a description. */
export function experimentLine(experiment: string): string | null {
  return EXPERIMENT_LINES[experiment] ?? null;
}

/** A tool's plain name, or null when it has none and is shown by its identifier. */
export function toolDisplayName(tool: string): string | null {
  return TOOL_DISPLAY_NAMES[tool] ?? null;
}

function toolSegment(tool: string): VariantSummarySegment {
  const name = toolDisplayName(tool);
  return name ? { text: name, code: false } : { text: tool, code: true };
}

function text(value: string): VariantSummarySegment {
  return { text: value, code: false };
}

function addsTools(variant: BenchmarkInstructionVariantDto): boolean {
  return (variant.experimentalTools?.length ?? 0) > 0 || (variant.experiments?.length ?? 0) > 0;
}

/** The option text after the variant's name, or null when the catalog says nothing of its contents. */
export function variantOptionSuffix(variant: BenchmarkInstructionVariantDto): string | null {
  const tools = addsTools(variant);
  const instructions = variant.hasPromptAppendix === true;
  if (tools && instructions) return 'new tools and extra instructions';
  if (tools) return 'new tools';
  if (instructions) return 'extra instructions';
  if ((variant.rewordedTools?.length ?? 0) > 0) return 'reworded tool descriptions';
  return null;
}

// --- Ablation specs: `<name>`, `<name>/minus/<feature id>`, `<name>/only/<feature id>` ---

/** A single-feature ablation: leave one feature out, or run only that one. */
export type VariantAblationMode = 'minus' | 'only';

/** A spec split into its parts; `mode` and `feature` are null for a whole variant. */
export interface ParsedVariantSpec {
  base: string;
  mode: VariantAblationMode | null;
  feature: string | null;
}

/** A spec whose base the catalog lists as valid and whose feature, for an ablation, is one of the base's. */
export interface ResolvedVariantSpec extends ParsedVariantSpec {
  spec: string;
  variant: BenchmarkInstructionVariantDto;
  /** The ablated feature; null for a whole variant. */
  featureDto: BenchmarkInstructionVariantFeatureDto | null;
}

/** The parts of a spec, or null when it does not follow the grammar. */
export function parseVariantSpec(spec: string | null | undefined): ParsedVariantSpec | null {
  const text = (spec ?? '').trim();
  if (text === '' || text.includes(':')) return null;
  const parts = text.split('/');
  if (parts.length === 1) return { base: parts[0], mode: null, feature: null };
  if (parts.length !== 3 || parts[0] === '' || parts[2] === '') return null;
  const mode = parts[1];
  if (mode !== 'minus' && mode !== 'only') return null;
  return { base: parts[0], mode, feature: parts[2] };
}

/** The spec of a whole variant (no mode) or of one of its ablations. */
export function variantSpec(base: string, mode: VariantAblationMode | null, feature: string | null): string {
  return mode && feature ? `${base}/${mode}/${feature}` : base;
}

/**
 * The spec resolved against the catalog: its base must be listed and valid, and an ablation's feature
 * must be one of the base's `features`, of which there must be two or more (the server refuses an
 * ablation of a one-feature variant). Null otherwise.
 */
export function resolveVariantSpec(
  spec: string | null | undefined, variants: readonly BenchmarkInstructionVariantDto[]): ResolvedVariantSpec | null {
  const parsed = parseVariantSpec(spec);
  if (!parsed) return null;
  const variant = variants.find(v => v.name === parsed.base && v.isValid);
  if (!variant) return null;
  if (parsed.mode === null) {
    return { ...parsed, spec: parsed.base, variant, featureDto: null };
  }
  const features = variant.features ?? [];
  const featureDto = features.find(f => f.id === parsed.feature) ?? null;
  if (!featureDto || features.length < 2) return null;
  return { ...parsed, spec: variantSpec(parsed.base, parsed.mode, parsed.feature), variant, featureDto };
}

const FEATURE_ID_PREFIXES: ReadonlyArray<readonly [string, BenchmarkVariantFeatureKind]> = [
  ['tool.', 'experimentalTool'],
  ['experiment.', 'experiment'],
  ['guide.', 'rewordedGuide'],
  ['appendix.', 'appendix']
];

/** The kind and target a feature id names, or null for an id of no known kind. */
export function featureOfId(id: string): { kind: BenchmarkVariantFeatureKind; target: string } | null {
  for (const [prefix, kind] of FEATURE_ID_PREFIXES) {
    if (id.startsWith(prefix) && id.length > prefix.length) {
      return { kind, target: id.slice(prefix.length) };
    }
  }
  return null;
}

/** A feature in words, after an article: *the get_character_info tool*, *the reworded wiki_search description*. */
export function featurePhrase(feature: { kind: BenchmarkVariantFeatureKind; target: string }): string {
  switch (feature.kind) {
    case 'experimentalTool': return `the ${feature.target} tool`;
    case 'experiment': return `the ${feature.target} output switch`;
    case 'rewordedGuide': return `the reworded ${feature.target} description`;
    case 'appendix': return `the extra instructions of ${feature.target}`;
  }
}

/** A feature id in words, from the catalog entry when there is one, else from the id itself. */
export function featureIdPhrase(id: string, features?: readonly BenchmarkInstructionVariantFeatureDto[] | null): string {
  const known = features?.find(f => f.id === id) ?? featureOfId(id);
  return known ? featurePhrase(known) : id;
}

/**
 * A spec in words: the name for a whole variant, *asker-profile-v1 without the reworded wiki_search
 * description* for a leave-one-out, and *asker-profile-v1, only the get_character_info tool* for a
 * single feature. A spec that does not parse is returned as it is.
 */
export function ablationLabel(spec: string, variants: readonly BenchmarkInstructionVariantDto[]): string {
  const parsed = parseVariantSpec(spec);
  if (!parsed) return spec;
  if (parsed.mode === null || parsed.feature === null) return parsed.base;
  const features = variants.find(v => v.name === parsed.base)?.features;
  const phrase = featureIdPhrase(parsed.feature, features);
  return parsed.mode === 'minus' ? `${parsed.base} without ${phrase}` : `${parsed.base}, only ${phrase}`;
}

/** The run a spec's run is compared with, as one sentence. */
export function variantReferenceLine(spec: string): string {
  const parsed = parseVariantSpec(spec);
  return parsed?.mode === 'minus'
    ? `Compare with a run of ${parsed.base} that is identical otherwise.`
    : 'Compare with a run without Experimental Changes that is identical otherwise.';
}

/** The features a run of the spec keeps: all for a whole variant, all but one for `minus`, one for `only`. */
export function keptFeatures<T extends BenchmarkInstructionVariantFeatureDto>(
  features: readonly T[], mode: VariantAblationMode | null, feature: string | null): T[] {
  if (mode === 'minus') return features.filter(f => f.id !== feature);
  if (mode === 'only') return features.filter(f => f.id === feature);
  return [...features];
}

/** Whether a feature reaches the candidate under a Tool Access choice. */
export function featureApplies(feature: BenchmarkInstructionVariantFeatureDto, condition: BenchmarkInformationCondition): boolean {
  return feature.appliesUnder?.[condition] !== false;
}

const CONDITION_NAMES: Readonly<Record<BenchmarkInformationCondition, string>> = {
  production: 'Production',
  sourceOnly: 'Source code only',
  closedBook: 'Closed book'
};

/** Why a feature does not reach the candidate under a Tool Access choice; null when it does. */
export function featureNotApplyingReason(
  feature: BenchmarkInstructionVariantFeatureDto, condition: BenchmarkInformationCondition): string | null {
  if (featureApplies(feature, condition)) return null;
  if (condition === 'closedBook') return 'Does not apply under Closed book: the AI gets no tools.';
  if (condition === 'sourceOnly' && feature.kind === 'rewordedGuide') {
    return `Does not apply under Source code only: ${feature.target} is switched off there.`;
  }
  return `Does not apply under ${CONDITION_NAMES[condition]}.`;
}

/** A kept feature whose text names a feature the run leaves out. */
export interface DanglingMention {
  keeper: BenchmarkInstructionVariantFeatureDto;
  missing: string;
}

/** The kept features whose text names a feature the spec's run leaves out; none for a whole variant. */
export function danglingMentions(
  features: readonly BenchmarkInstructionVariantFeatureDto[], mode: VariantAblationMode | null, feature: string | null): DanglingMention[] {
  if (mode === null || feature === null) return [];
  const kept = keptFeatures(features, mode, feature);
  const keptIds = new Set(kept.map(f => f.id));
  const allIds = new Set(features.map(f => f.id));
  const result: DanglingMention[] = [];
  for (const keeper of kept) {
    for (const missing of keeper.mentions ?? []) {
      if (allIds.has(missing) && !keptIds.has(missing)) {
        result.push({ keeper, missing });
      }
    }
  }
  return result;
}

/** One dangling mention as a sentence. */
export function danglingMentionLine(
  mention: DanglingMention, features: readonly BenchmarkInstructionVariantFeatureDto[]): string {
  const keeper = featurePhrase(mention.keeper);
  const missing = featureIdPhrase(mention.missing, features);
  const kind = (features.find(f => f.id === mention.missing) ?? featureOfId(mention.missing))?.kind;
  const consequence = kind === 'experimentalTool'
    ? 'the model will be told about a tool it does not have'
    : kind === 'experiment'
      ? 'the model will be told about output it does not get'
      : 'the model will be told about text it does not get';
  return `${missing.charAt(0).toUpperCase()}${missing.slice(1)} is named in ${keeper}, but this run leaves it out: ${consequence}.`;
}

function linesFromFeatures(features: readonly BenchmarkInstructionVariantFeatureDto[], parent: string | null | undefined): VariantSummarySegment[][] {
  const lines: VariantSummarySegment[][] = [];
  for (const f of features.filter(f => f.kind === 'experimentalTool')) {
    lines.push([text('Adds a tool the live chat does not have: '), toolSegment(f.target), text('.')]);
  }
  for (const f of features.filter(f => f.kind === 'experiment')) {
    const line = EXPERIMENT_LINES[f.target];
    lines.push(line
      ? [text(line)]
      : [text('Turns on the output switch '), { text: f.target, code: true }, text('.')]);
  }
  if (features.some(f => f.kind === 'appendix')) {
    lines.push([text('Adds extra instructions to the end of the AI\'s instructions.')]);
  }
  const reworded = features.filter(f => f.kind === 'rewordedGuide').map(f => f.target).sort();
  if (reworded.length > 0) {
    const line: VariantSummarySegment[] = [text('Rewords the descriptions of: ')];
    reworded.forEach((tool, i) => {
      if (i > 0) line.push(text(', '));
      line.push(toolSegment(tool));
    });
    line.push(text('.'));
    lines.push(line);
  }
  if (parent && features.some(f => f.fromParent)) {
    lines.push([text(`Builds on ${parent}.`)]);
  }
  return lines;
}

/**
 * The lines of the *What <name> changes* box, in order: tools, switches, instructions, rewordings,
 * parent. For an ablation spec of the variant they describe what the run keeps, and a leave-one-out
 * ends with the feature it leaves out; a spec of another variant, or one naming a feature the catalog
 * does not list, gets the whole variant's lines.
 */
export function variantChangeLines(variant: BenchmarkInstructionVariantDto, spec?: string | null): VariantSummarySegment[][] {
  const parsed = parseVariantSpec(spec);
  const features = variant.features ?? [];
  if (parsed && parsed.base === variant.name && parsed.mode && parsed.feature && features.some(f => f.id === parsed.feature)) {
    const lines = linesFromFeatures(keptFeatures(features, parsed.mode, parsed.feature), variant.extends);
    if (parsed.mode === 'minus') {
      lines.push([text(`Leaves out ${featureIdPhrase(parsed.feature, features)}.`)]);
    }
    return lines;
  }
  const lines: VariantSummarySegment[][] = [];
  for (const tool of variant.experimentalTools ?? []) {
    lines.push([text('Adds a tool the live chat does not have: '), toolSegment(tool), text('.')]);
  }
  for (const experiment of variant.experiments ?? []) {
    const line = EXPERIMENT_LINES[experiment];
    lines.push(line
      ? [text(line)]
      : [text('Turns on the output switch '), { text: experiment, code: true }, text('.')]);
  }
  if (variant.hasPromptAppendix) {
    lines.push([text('Adds extra instructions to the end of the AI\'s instructions.')]);
  }
  const reworded = variant.rewordedTools ?? [];
  if (reworded.length > 0) {
    const line: VariantSummarySegment[] = [text('Rewords the descriptions of: ')];
    reworded.forEach((tool, i) => {
      if (i > 0) line.push(text(', '));
      line.push(toolSegment(tool));
    });
    line.push(text('.'));
    lines.push(line);
  }
  if (variant.extends) {
    lines.push([text(`Builds on ${variant.extends}.`)]);
  }
  return lines;
}

function joinNames(names: string[]): string {
  return names.length <= 1
    ? names.join('')
    : `${names.slice(0, -1).join(', ')} and ${names[names.length - 1]}`;
}

/**
 * The line for a Tool Access choice that voids part of the variant; null when nothing is voided. For
 * an ablation spec of the variant it judges the features the run keeps, and says so first when the
 * ablated feature itself does not apply, since the run would then equal its reference run and the
 * server refuses it.
 */
export function variantConditionWarning(
  variant: BenchmarkInstructionVariantDto, condition: BenchmarkInformationCondition, spec?: string | null): string | null {
  const parsed = parseVariantSpec(spec);
  const features = variant.features ?? [];
  const ablated = parsed && parsed.base === variant.name && parsed.mode && parsed.feature
    ? features.find(f => f.id === parsed.feature) ?? null
    : null;
  if (parsed?.mode && ablated) {
    if (!featureApplies(ablated, condition)) {
      const reference = parsed.mode === 'minus' ? `a run of ${variant.name}` : 'a run without Experimental Changes';
      return `With ${CONDITION_NAMES[condition]}, ${featurePhrase(ablated)} does not apply, so this run would be the same as `
        + `${reference}, and it cannot start. Choose another feature or another Tool Access.`;
    }
    const kept = keptFeatures(features, parsed.mode, ablated.id);
    const voided = kept.filter(f => !featureApplies(f, condition));
    if (voided.length === 0) return null;
    if (condition === 'closedBook') {
      return kept.some(f => featureApplies(f, condition))
        ? 'With Closed book the AI gets no tools, so only this change\'s extra instructions apply.'
        : 'With Closed book the AI gets no tools, so this change has no effect. Choose None, or another Tool Access.';
    }
    const names = joinNames(voided.map(f => f.kind === 'rewordedGuide' || f.kind === 'experimentalTool'
      ? toolDisplayName(f.target) ?? f.target
      : f.target));
    const allRewordings = voided.every(f => f.kind === 'rewordedGuide');
    if (condition === 'sourceOnly' && allRewordings) {
      return voided.length === 1
        ? `With Source code only, its rewording of ${names} does not apply: that tool is switched off.`
        : `With Source code only, its rewording of ${names} does not apply: those tools are switched off.`;
    }
    return `With ${CONDITION_NAMES[condition]}, part of this change does not apply: ${names}.`;
  }
  if (condition === 'closedBook') {
    return variant.hasPromptAppendix
      ? 'With Closed book the AI gets no tools, so only this change\'s extra instructions apply.'
      : 'With Closed book the AI gets no tools, so this change has no effect. Choose None, or another Tool Access.';
  }
  if (condition === 'sourceOnly') {
    const withheld = variant.rewordingsWithheldUnderSourceOnly ?? [];
    if (withheld.length === 0) return null;
    const names = joinNames(withheld.map(tool => toolDisplayName(tool) ?? tool));
    return withheld.length === 1
      ? `With Source code only, its rewording of ${names} does not apply: that tool is switched off.`
      : `With Source code only, its rewording of ${names} does not apply: those tools are switched off.`;
  }
  return null;
}
