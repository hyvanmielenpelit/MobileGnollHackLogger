import { BenchmarkInformationCondition, BenchmarkInstructionVariantDto } from '../../../services/admin-benchmark.service';

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

/** The lines of the *What <name> changes* box, in order: tools, switches, instructions, rewordings, parent. */
export function variantChangeLines(variant: BenchmarkInstructionVariantDto): VariantSummarySegment[][] {
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

/** The line for a Tool Access choice that voids part of the variant; null when nothing is voided. */
export function variantConditionWarning(
  variant: BenchmarkInstructionVariantDto, condition: BenchmarkInformationCondition): string | null {
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
