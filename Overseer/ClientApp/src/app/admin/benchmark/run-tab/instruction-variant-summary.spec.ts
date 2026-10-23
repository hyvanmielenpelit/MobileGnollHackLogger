import type {
  BenchmarkInstructionVariantDto,
  BenchmarkInstructionVariantFeatureDto
} from '../../../services/admin-benchmark.service';
import {
  VariantSummarySegment,
  ablationLabel,
  danglingMentionLine,
  danglingMentions,
  featureNotApplyingReason,
  keptFeatures,
  parseVariantSpec,
  resolveVariantSpec,
  toolDisplayName,
  variantChangeLines,
  variantConditionWarning,
  variantOptionSuffix,
  variantReferenceLine,
  variantSpec
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

function feature(
  id: string, kind: BenchmarkInstructionVariantFeatureDto['kind'], overrides: Partial<BenchmarkInstructionVariantFeatureDto> = {}
): BenchmarkInstructionVariantFeatureDto {
  return {
    id, kind, target: id.slice(id.indexOf('.') + 1), fromParent: false,
    appliesUnder: { production: true, sourceOnly: true, closedBook: kind === 'appendix' }, mentions: [], ...overrides
  };
}

/** The features of the shipped asker-profile-v1, as the catalog lists them. */
const ASKER_FEATURES: BenchmarkInstructionVariantFeatureDto[] = [
  feature('tool.get_character_info', 'experimentalTool', { fromParent: true }),
  feature('experiment.stats_referenced_by', 'experiment', { fromParent: true }),
  feature('guide.get_artifact_stats', 'rewordedGuide', { fromParent: true, mentions: ['experiment.stats_referenced_by'] }),
  feature('guide.get_item_stats', 'rewordedGuide', { fromParent: true, mentions: ['experiment.stats_referenced_by'] }),
  feature('guide.get_monster_stats', 'rewordedGuide', { fromParent: true, mentions: ['experiment.stats_referenced_by'] }),
  feature('guide.wiki_search', 'rewordedGuide', { appliesUnder: { production: true, sourceOnly: false, closedBook: false } }),
  feature('appendix.asker-profile-v1', 'appendix', { mentions: ['tool.get_character_info'] })
];

const ASKER_WITH_FEATURES = { ...ASKER_PROFILE, features: ASKER_FEATURES };
const ONE_FEATURE = variant({ name: 'one-v1', rewordedTools: ['wiki_search'], features: [feature('guide.wiki_search', 'rewordedGuide')] });
const CATALOG = [ASKER_WITH_FEATURES, ONE_FEATURE, variant({ name: 'broken-v1', isValid: false })];

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

  describe('ablation specs', () => {
    it('should parse a whole variant and both ablation modes, and nothing else', () => {
      expect(parseVariantSpec('asker-profile-v1')).toEqual({ base: 'asker-profile-v1', mode: null, feature: null });
      expect(parseVariantSpec('asker-profile-v1/minus/guide.wiki_search'))
        .toEqual({ base: 'asker-profile-v1', mode: 'minus', feature: 'guide.wiki_search' });
      expect(parseVariantSpec('asker-profile-v1/only/tool.get_character_info'))
        .toEqual({ base: 'asker-profile-v1', mode: 'only', feature: 'tool.get_character_info' });
      for (const bad of [null, '', 'a/plus/b', 'a/minus', 'a/minus/', 'a/minus/b/c', 'a:0123456789ab']) {
        expect(parseVariantSpec(bad)).toBeNull();
      }
    });

    it('should build the spec it parses', () => {
      expect(variantSpec('asker-profile-v1', null, null)).toBe('asker-profile-v1');
      expect(variantSpec('asker-profile-v1', 'minus', 'guide.wiki_search')).toBe('asker-profile-v1/minus/guide.wiki_search');
    });

    it('should resolve a valid base and a feature of it, and refuse the rest', () => {
      expect(resolveVariantSpec('asker-profile-v1', CATALOG)?.featureDto).toBeNull();
      expect(resolveVariantSpec('asker-profile-v1/only/guide.wiki_search', CATALOG)?.featureDto?.kind).toBe('rewordedGuide');
      expect(resolveVariantSpec('asker-profile-v1/minus/guide.unknown', CATALOG)).toBeNull();
      expect(resolveVariantSpec('broken-v1', CATALOG)).toBeNull();
      expect(resolveVariantSpec('gone-v1/minus/guide.wiki_search', CATALOG)).toBeNull();
      // A one-feature variant has no ablation: leaving it out leaves nothing, and keeping it is the whole variant.
      expect(resolveVariantSpec('one-v1/minus/guide.wiki_search', CATALOG)).toBeNull();
      // A catalog entry from a server that lists no features cannot back an ablation.
      expect(resolveVariantSpec('asker-profile-v1/minus/guide.wiki_search', [ASKER_PROFILE])).toBeNull();
    });

    it('should label an ablation in plain words', () => {
      expect(ablationLabel('asker-profile-v1', CATALOG)).toBe('asker-profile-v1');
      expect(ablationLabel('asker-profile-v1/minus/guide.wiki_search', CATALOG))
        .toBe('asker-profile-v1 without the reworded wiki_search description');
      expect(ablationLabel('asker-profile-v1/only/tool.get_character_info', CATALOG))
        .toBe('asker-profile-v1, only the get_character_info tool');
      expect(ablationLabel('asker-profile-v1/minus/experiment.stats_referenced_by', []))
        .toBe('asker-profile-v1 without the stats_referenced_by output switch');
      expect(ablationLabel('asker-profile-v1/only/appendix.asker-profile-v1', CATALOG))
        .toBe('asker-profile-v1, only the extra instructions of asker-profile-v1');
      expect(ablationLabel('not/a/spec/at/all', CATALOG)).toBe('not/a/spec/at/all');
    });

    it('should name the run to compare with', () => {
      expect(variantReferenceLine('asker-profile-v1/minus/guide.wiki_search'))
        .toBe('Compare with a run of asker-profile-v1 that is identical otherwise.');
      expect(variantReferenceLine('asker-profile-v1/only/guide.wiki_search'))
        .toBe('Compare with a run without Experimental Changes that is identical otherwise.');
      expect(variantReferenceLine('asker-profile-v1'))
        .toBe('Compare with a run without Experimental Changes that is identical otherwise.');
    });

    it('should keep every feature but one, or only one', () => {
      expect(keptFeatures(ASKER_FEATURES, 'minus', 'guide.wiki_search').map(f => f.id)).not.toContain('guide.wiki_search');
      expect(keptFeatures(ASKER_FEATURES, 'minus', 'guide.wiki_search').length).toBe(6);
      expect(keptFeatures(ASKER_FEATURES, 'only', 'guide.wiki_search').map(f => f.id)).toEqual(['guide.wiki_search']);
      expect(keptFeatures(ASKER_FEATURES, null, null).length).toBe(7);
    });
  });

  describe('ablation-aware summary', () => {
    it('should describe a leave-one-out by what it keeps and what it leaves out', () => {
      expect(variantChangeLines(ASKER_WITH_FEATURES, 'asker-profile-v1/minus/guide.wiki_search').map(plain)).toEqual([
        'Adds a tool the live chat does not have: race and role lookup.',
        'Item, monster and artifact stats also list the places in the game\'s code that use them.',
        'Adds extra instructions to the end of the AI\'s instructions.',
        'Rewords the descriptions of: artifact stats, item stats, monster stats.',
        'Builds on source-tools-v1.',
        'Leaves out the reworded wiki_search description.'
      ]);
    });

    it('should describe a single feature by that feature alone', () => {
      expect(variantChangeLines(ASKER_WITH_FEATURES, 'asker-profile-v1/only/tool.get_character_info').map(plain)).toEqual([
        'Adds a tool the live chat does not have: race and role lookup.',
        'Builds on source-tools-v1.'
      ]);
      expect(variantChangeLines(ASKER_WITH_FEATURES, 'asker-profile-v1/only/guide.wiki_search').map(plain)).toEqual([
        'Rewords the descriptions of: wiki search.'
      ]);
    });

    it('should describe the whole variant for its own name, another variant\'s spec, or an unknown feature', () => {
      const whole = variantChangeLines(ASKER_WITH_FEATURES).map(plain);
      expect(variantChangeLines(ASKER_WITH_FEATURES, 'asker-profile-v1').map(plain)).toEqual(whole);
      expect(variantChangeLines(ASKER_WITH_FEATURES, 'other-v1/minus/guide.wiki_search').map(plain)).toEqual(whole);
      expect(variantChangeLines(ASKER_WITH_FEATURES, 'asker-profile-v1/minus/guide.unknown').map(plain)).toEqual(whole);
    });

    it('should say an ablated feature that does not apply makes the run equal its reference', () => {
      expect(variantConditionWarning(ASKER_WITH_FEATURES, 'sourceOnly', 'asker-profile-v1/minus/guide.wiki_search'))
        .toBe('With Source code only, the reworded wiki_search description does not apply, so this run would be the same as '
          + 'a run of asker-profile-v1, and it cannot start. Choose another feature or another Tool Access.');
      expect(variantConditionWarning(ASKER_WITH_FEATURES, 'closedBook', 'asker-profile-v1/only/tool.get_character_info'))
        .toBe('With Closed book, the get_character_info tool does not apply, so this run would be the same as '
          + 'a run without Experimental Changes, and it cannot start. Choose another feature or another Tool Access.');
    });

    it('should judge the kept features of an ablation whose feature applies', () => {
      expect(variantConditionWarning(ASKER_WITH_FEATURES, 'closedBook', 'asker-profile-v1/minus/appendix.asker-profile-v1'))
        .toBe('With Closed book the AI gets no tools, so this change has no effect. Choose None, or another Tool Access.');
      expect(variantConditionWarning(ASKER_WITH_FEATURES, 'sourceOnly', 'asker-profile-v1/minus/appendix.asker-profile-v1'))
        .toBe('With Source code only, its rewording of wiki search does not apply: that tool is switched off.');
      expect(variantConditionWarning(ASKER_WITH_FEATURES, 'sourceOnly', 'asker-profile-v1/only/tool.get_character_info')).toBeNull();
      expect(variantConditionWarning(ASKER_WITH_FEATURES, 'closedBook', 'asker-profile-v1/only/appendix.asker-profile-v1')).toBeNull();
      expect(variantConditionWarning(ASKER_WITH_FEATURES, 'production', 'asker-profile-v1/minus/guide.wiki_search')).toBeNull();
    });

    it('should give the whole variant\'s warning without a spec', () => {
      expect(variantConditionWarning(ASKER_WITH_FEATURES, 'sourceOnly'))
        .toBe('With Source code only, its rewording of wiki search does not apply: that tool is switched off.');
    });

    it('should say why a feature does not apply', () => {
      const wiki = ASKER_FEATURES.find(f => f.id === 'guide.wiki_search')!;
      expect(featureNotApplyingReason(wiki, 'sourceOnly')).toBe('Does not apply under Source code only: wiki_search is switched off there.');
      expect(featureNotApplyingReason(wiki, 'closedBook')).toBe('Does not apply under Closed book: the AI gets no tools.');
      expect(featureNotApplyingReason(wiki, 'production')).toBeNull();
    });
  });

  describe('dangling mentions', () => {
    it('should find a kept text that names a left-out feature', () => {
      const mentions = danglingMentions(ASKER_FEATURES, 'minus', 'tool.get_character_info');
      expect(mentions.map(m => [m.keeper.id, m.missing])).toEqual([['appendix.asker-profile-v1', 'tool.get_character_info']]);
      expect(danglingMentionLine(mentions[0], ASKER_FEATURES))
        .toBe('The get_character_info tool is named in the extra instructions of asker-profile-v1, but this run leaves it out: '
          + 'the model will be told about a tool it does not have.');
    });

    it('should find a single kept guide that names the switch it runs without', () => {
      const mentions = danglingMentions(ASKER_FEATURES, 'only', 'guide.get_item_stats');
      expect(mentions.map(m => m.missing)).toEqual(['experiment.stats_referenced_by']);
      expect(danglingMentionLine(mentions[0], ASKER_FEATURES)).toContain('told about output it does not get');
    });

    it('should find nothing for a whole variant, or when the mentioned feature is kept', () => {
      expect(danglingMentions(ASKER_FEATURES, null, null)).toEqual([]);
      expect(danglingMentions(ASKER_FEATURES, 'minus', 'guide.wiki_search')).toEqual([]);
      expect(danglingMentions(ASKER_FEATURES, 'only', 'tool.get_character_info')).toEqual([]);
    });
  });
});
