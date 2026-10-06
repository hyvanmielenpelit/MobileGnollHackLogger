import type { BenchmarkModelComparisonEntryDto } from '../model-comparison/model-comparison.models';
import {
  comparisonEntrySource,
  comparisonModelMention,
  comparisonModelOptions,
  defaultCoveredKeys,
  defaultSubjectKey,
  optionsByIntelligence
} from './comparison-model-options';

function entry(key: string, name: string, index: number | null, overrides: Partial<BenchmarkModelComparisonEntryDto> = {}): BenchmarkModelComparisonEntryDto {
  const [kind, id] = key.split(':');
  return {
    key,
    sourceKind: kind === 'group' ? 'Group' : kind === 'battery' ? 'Battery' : 'Run',
    sourceId: Number(id),
    label: name,
    provider: 'OpenAI',
    modelId: name.toLowerCase().replace(/\s+/g, '-'),
    modelDisplayName: name,
    thinkingLevel: 'medium',
    reasoningMode: null,
    state: 'Comparable',
    comparable: true,
    excluded: false,
    speedDegraded: false,
    costDegraded: false,
    quality: (index === null ? null : { pointEstimate: index }) as BenchmarkModelComparisonEntryDto['quality'],
    ...overrides
  } as BenchmarkModelComparisonEntryDto;
}

describe('comparison-model-options', () => {
  it('names each source in step 1\'s terms', () => {
    expect(comparisonEntrySource({ sourceKind: 'Run', sourceId: 31 })).toBe('Run #31');
    expect(comparisonEntrySource({ sourceKind: 'Group', sourceId: 4 })).toBe('Analysis group #4');
    expect(comparisonEntrySource({ sourceKind: 'Battery', sourceId: 10 })).toBe('Battery run #10');
  });

  it('offers every entry that is not Excluded, keyed by entry key, with the single picker\'s badge fields and no price', () => {
    const options = comparisonModelOptions([
      entry('run:1', 'GPT-6.1 Sol', 71),
      entry('run:2', 'Old GPT', 40, { excluded: true, state: 'Excluded' }),
      entry('group:3', 'Claude 5 Opus', 68, { provider: 'Anthropic', thinkingLevel: 'high', reasoningMode: 'adaptive' })
    ]);

    expect(options.map(option => option.key)).toEqual(['run:1', 'group:3']);
    const claude = options[1].model;
    expect(claude.displayName).toBe('Claude 5 Opus');
    expect(claude.provider).toBe('Anthropic');
    expect(claude.thinkingLevel).toBe('high');
    expect(claude.reasoningMode).toBe('adaptive');
    expect(claude.label).toBe('Claude 5 Opus (high)');
    expect(claude.source).toBe('Analysis group #3');
    expect(claude.intelligenceIndex).toBe(68);
    expect(claude.effectiveInputPricePerMillion).toBeUndefined();
    expect(options.every(option => option.detail === undefined)).toBe(true);
  });

  it('adds the source as a detail only where two options would look identical', () => {
    const options = comparisonModelOptions([
      entry('battery:9', 'GPT-5.6 Luna', 60, { thinkingLevel: 'max' }),
      entry('battery:10', 'GPT-5.6 Luna', 62, { thinkingLevel: 'max' }),
      entry('battery:11', 'GPT-5.6 Luna', 55, { thinkingLevel: 'low' })
    ]);

    expect(options.map(option => option.detail)).toEqual(['Battery run #9', 'Battery run #10', undefined]);
    expect(comparisonModelMention('battery:10', options)).toBe('GPT-5.6 Luna (max) from Battery run #10');
    expect(comparisonModelMention('battery:11', options)).toBe('GPT-5.6 Luna (low)');
  });

  it('orders by Intelligence Index, an unmeasured entry last and ties by entry key', () => {
    const options = comparisonModelOptions([
      entry('run:3', 'C', 50),
      entry('run:1', 'A', null),
      entry('run:2', 'B', 70),
      entry('run:4', 'D', 50)
    ]);

    expect(optionsByIntelligence(options).map(option => option.key)).toEqual(['run:2', 'run:3', 'run:4', 'run:1']);
    expect(defaultSubjectKey(options)).toBe('run:2');
    expect(defaultSubjectKey([])).toBeNull();
  });

  it('chooses every model for a comparison-wide set, and the 12 highest-Index ones of more, in the options\' order', () => {
    const few = comparisonModelOptions([entry('run:1', 'A', 50), entry('run:2', 'B', 60)]);
    expect(defaultCoveredKeys(few)).toEqual(['run:1', 'run:2']);

    const many = comparisonModelOptions(Array.from({ length: 14 }, (_, i) => entry(`run:${i + 1}`, `Model ${i + 1}`, i + 1)));
    const chosen = defaultCoveredKeys(many);
    expect(chosen.length).toBe(12);
    expect(chosen).not.toContain('run:1');
    expect(chosen).not.toContain('run:2');
    expect(chosen[0]).toBe('run:3');
    expect(chosen[11]).toBe('run:14');
  });
});
