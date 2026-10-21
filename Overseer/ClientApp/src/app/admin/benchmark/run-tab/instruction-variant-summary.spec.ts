import type { BenchmarkInstructionVariantDto } from '../../../services/admin-benchmark.service';
import {
  VariantSummarySegment,
  toolDisplayName,
  variantChangeLines,
  variantConditionWarning,
  variantOptionSuffix
} from './instruction-variant-summary';

function variant(overrides: Partial<BenchmarkInstructionVariantDto> = {}): BenchmarkInstructionVariantDto {
  return {
    name: 'probe-v1',
    description: null,
    extends: null,
    hashPrefix: 'ab12cd34ef56',
    isValid: true,
    experimentalTools: [],
    experiments: [],
    rewordedTools: [],
    hasPromptAppendix: false,
    rewordingsWithheldUnderSourceOnly: [],
    ...overrides
  };
}

/** Shaped like the shipped source-tools-v1. */
const SOURCE_TOOLS = variant({
  name: 'source-tools-v1',
  description: 'Source-derived tools: get_character_info, and referenced_by in the stats tools. No new instruction prose.',
  experimentalTools: ['get_character_info'],
  experiments: ['stats_referenced_by'],
  rewordedTools: ['get_artifact_stats', 'get_item_stats', 'get_monster_stats']
});

/** Shaped like the shipped asker-profile-v1. */
const ASKER_PROFILE = variant({
  name: 'asker-profile-v1',
  extends: 'source-tools-v1',
  experimentalTools: ['get_character_info'],
  experiments: ['stats_referenced_by'],
  rewordedTools: ['get_artifact_stats', 'get_item_stats', 'get_monster_stats', 'wiki_search'],
  hasPromptAppendix: true,
  rewordingsWithheldUnderSourceOnly: ['wiki_search']
});

function plain(line: VariantSummarySegment[]): string {
  return line.map(s => s.text).join('');
}

describe('instruction-variant-summary', () => {
  describe('variantOptionSuffix', () => {
    it('should name tools and instructions together', () => {
      expect(variantOptionSuffix(ASKER_PROFILE)).toBe('new tools and extra instructions');
    });

    it('should name new tools, counting an output switch as one', () => {
      expect(variantOptionSuffix(SOURCE_TOOLS)).toBe('new tools');
      expect(variantOptionSuffix(variant({ experiments: ['stats_referenced_by'] }))).toBe('new tools');
    });

    it('should name extra instructions alone', () => {
      expect(variantOptionSuffix(variant({ hasPromptAppendix: true }))).toBe('extra instructions');
    });

    it('should name reworded descriptions when that is all the variant does', () => {
      expect(variantOptionSuffix(variant({ rewordedTools: ['wiki_search'] }))).toBe('reworded tool descriptions');
    });

    it('should give nothing for an empty variant or one from a server that does not describe its contents', () => {
      expect(variantOptionSuffix(variant())).toBeNull();
      expect(variantOptionSuffix({ name: 'old', isValid: true })).toBeNull();
    });
  });

  describe('variantChangeLines', () => {
    it('should describe source-tools-v1 in plain words', () => {
      expect(variantChangeLines(SOURCE_TOOLS).map(plain)).toEqual([
        'Adds a tool the live chat does not have: race and role lookup.',
        'Item, monster and artifact stats also list the places in the game\'s code that use them.',
        'Rewords the descriptions of: artifact stats, item stats, monster stats.'
      ]);
    });

    it('should describe asker-profile-v1 with its instructions and its parent', () => {
      expect(variantChangeLines(ASKER_PROFILE).map(plain)).toEqual([
        'Adds a tool the live chat does not have: race and role lookup.',
        'Item, monster and artifact stats also list the places in the game\'s code that use them.',
        'Adds extra instructions to the end of the AI\'s instructions.',
        'Rewords the descriptions of: artifact stats, item stats, monster stats, wiki search.',
        'Builds on source-tools-v1.'
      ]);
    });

    it('should show an unmapped tool and an unknown switch by their identifiers, as code', () => {
      const lines = variantChangeLines(variant({ experimentalTools: ['new_probe'], experiments: ['probe_switch'] }));

      expect(lines.map(plain)).toEqual([
        'Adds a tool the live chat does not have: new_probe.',
        'Turns on the output switch probe_switch.'
      ]);
      expect(lines[0].filter(s => s.code).map(s => s.text)).toEqual(['new_probe']);
      expect(lines[1].filter(s => s.code).map(s => s.text)).toEqual(['probe_switch']);
    });

    it('should give no lines when the server does not describe the contents', () => {
      expect(variantChangeLines({ name: 'old', isValid: true })).toEqual([]);
    });
  });

  describe('variantConditionWarning', () => {
    it('should say only the extra instructions apply under Closed book', () => {
      expect(variantConditionWarning(ASKER_PROFILE, 'closedBook'))
        .toBe('With Closed book the AI gets no tools, so only this change\'s extra instructions apply.');
    });

    it('should say a variant without instructions has no effect under Closed book', () => {
      expect(variantConditionWarning(SOURCE_TOOLS, 'closedBook'))
        .toBe('With Closed book the AI gets no tools, so this change has no effect. Choose None, or another Tool Access.');
    });

    it('should name a rewording Source code only withholds', () => {
      expect(variantConditionWarning(ASKER_PROFILE, 'sourceOnly'))
        .toBe('With Source code only, its rewording of wiki search does not apply: that tool is switched off.');
    });

    it('should name several withheld rewordings together', () => {
      expect(variantConditionWarning(variant({ rewordingsWithheldUnderSourceOnly: ['wiki_search', 'get_knowledge_article'] }), 'sourceOnly'))
        .toBe('With Source code only, its rewording of wiki search and knowledge base articles does not apply: those tools are switched off.');
    });

    it('should say nothing under Production, or under Source code only when nothing is withheld', () => {
      expect(variantConditionWarning(ASKER_PROFILE, 'production')).toBeNull();
      expect(variantConditionWarning(SOURCE_TOOLS, 'sourceOnly')).toBeNull();
    });
  });

  describe('toolDisplayName', () => {
    it('should name every tool the benchmark allows, and nothing it does not know', () => {
      for (const tool of ['wiki_search', 'wiki_view', 'get_knowledge_article', 'nethack_wiki_search', 'nethack_wiki_view',
        'monster_lookup', 'item_lookup', 'get_monster_stats', 'get_item_stats', 'get_artifact_stats', 'get_constants',
        'get_function_definition', 'search_definitions', 'source_code_search', 'source_code_view', 'list_indexed_files',
        'get_character_info']) {
        expect(toolDisplayName(tool)).toBeTruthy();
      }
      expect(toolDisplayName('new_probe')).toBeNull();
    });
  });
});
