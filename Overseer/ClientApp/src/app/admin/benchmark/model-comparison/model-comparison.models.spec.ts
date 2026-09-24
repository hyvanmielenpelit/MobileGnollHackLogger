import {
  buildConditionLegend,
  conditionDetailFor,
  normalizeThinkingLevel,
  parseConfigurationValue,
  questionCoverageNotes,
  summarizeConditionDifference,
  toChartContext,
  toChartEntries
} from './model-comparison.models';
import type {
  BenchmarkComparabilityConditionDto,
  BenchmarkComparabilityIndexDto,
  BenchmarkComparabilityIndexEntryDto,
  BenchmarkComparabilityKeyValueDto,
  BenchmarkModelComparisonCostDto,
  BenchmarkModelComparisonDto,
  BenchmarkModelComparisonEntryDto,
  BenchmarkModelComparisonQualityDto,
  ConditionDetailRow
} from './model-comparison.models';

/** The reference condition's assessor configuration, in the `key=value;…` shape the wire uses. */
const ASSESSOR_REFERENCE =
  'provider=OpenAI;model=gpt-5.6-sol;thinking=medium;secondOpinion=(none)';

/** The same configuration one field apart: the thinking level, and nothing else. */
const ASSESSOR_VARIANT =
  'provider=OpenAI;model=gpt-5.6-sol;thinking=high;secondOpinion=(none)';

/** A full-length digest, so a test can tell a value with no fields from one that has them. */
const REFERENCE_DIGEST = 'bb19dc24e287'.repeat(5) + 'abcd';

const OTHER_DIGEST = '77ae01f3c904'.repeat(5) + 'dcba';

function buildEntry(
  overrides: Partial<BenchmarkComparabilityIndexEntryDto> = {}
): BenchmarkComparabilityIndexEntryDto {
  return {
    key: 'run:1',
    sourceKind: 'Run',
    sourceId: 1,
    conditionOrdinal: 1,
    conditionLabel: 'Condition A',
    signature: 'sig-a',
    selfInconsistent: false,
    selfInconsistentKeys: [],
    differencesFromLargest: [],
    questionParallelism: '1',
    speedCalibration: 'speed-a',
    pricingSnapshot: '2026-09-01',
    ...overrides
  };
}

function buildKey(
  overrides: Partial<BenchmarkComparabilityKeyValueDto> = {}
): BenchmarkComparabilityKeyValueDto {
  return {
    name: 'AssessorConfiguration',
    label: 'Assessor configuration',
    description: 'A difference here means the answers were graded by a different assessor.',
    kind: 'Instrument',
    valueKind: 'Text',
    value: ASSESSOR_REFERENCE,
    displayValue: null,
    ...overrides
  };
}

/** The two differences every source outside the reference condition carries in these fixtures. */
function differences(runIds: number[]) {
  return [
    {
      name: 'AssessorConfiguration',
      kind: 'MustMatch',
      description: 'Assessor configuration differs from the largest condition',
      variants: [
        { value: ASSESSOR_VARIANT, runIds },
        { value: ASSESSOR_REFERENCE, runIds: [1, 2] }
      ]
    },
    {
      name: 'CandidateSystemPromptSha256',
      kind: 'MustMatch',
      description: 'Candidate system prompt differs from the largest condition',
      variants: [
        { value: OTHER_DIGEST, runIds },
        { value: REFERENCE_DIGEST, runIds: [1, 2] }
      ]
    }
  ];
}

/**
 * Run 1 in the reference condition; run 3 and group 4 outside it on the same two keys; group 9
 * self-inconsistent. Group 4's runs are 11 and 12, which no other source shares.
 */
function buildIndex(
  overrides: Partial<BenchmarkComparabilityIndexDto> = {}
): BenchmarkComparabilityIndexDto {
  return {
    computedAtUtc: '2026-09-11T12:00:00Z',
    entries: [
      buildEntry({ key: 'run:1', sourceId: 1 }),
      buildEntry({
        key: 'run:3',
        sourceId: 3,
        conditionOrdinal: 2,
        conditionLabel: 'Condition B',
        signature: 'sig-b',
        differencesFromLargest: differences([3])
      }),
      buildEntry({
        key: 'group:4',
        sourceKind: 'Group',
        sourceId: 4,
        conditionOrdinal: 2,
        conditionLabel: 'Condition B',
        signature: 'sig-b',
        differencesFromLargest: differences([11, 12])
      }),
      buildEntry({
        key: 'group:9',
        sourceKind: 'Group',
        sourceId: 9,
        conditionOrdinal: 0,
        conditionLabel: 'Self-inconsistent',
        signature: '',
        selfInconsistent: true,
        selfInconsistentKeys: ['BenchmarkSuiteId', 'CandidateModelId']
      })
    ],
    conditions: [
      {
        ordinal: 1, label: 'Condition A', sourceCount: 2, runCount: 2,
        signature: 'sig-a', newestRunStartedAtUtc: '2026-09-10T08:00:00Z'
      },
      {
        ordinal: 2, label: 'Condition B', sourceCount: 2, runCount: 3,
        signature: 'sig-b', newestRunStartedAtUtc: '2026-09-09T08:00:00Z'
      }
    ],
    largestConditionKeys: [
      buildKey(),
      buildKey({
        name: 'CandidateSystemPromptSha256',
        label: 'Candidate system prompt',
        description: 'A difference here means the candidate was given different instructions.',
        valueKind: 'Hash',
        value: REFERENCE_DIGEST
      })
    ],
    referenceSelectionRule: 'The reference condition is the one with the most sources.',
    mustMatchKeyNames: ['AssessorConfiguration', 'CandidateSystemPromptSha256'],
    modelAxisKeyNames: ['CandidateModelId'],
    degradingKeyNames: ['QuestionParallelism'],
    ...overrides
  };
}

describe('parseConfigurationValue', () => {
  it('returns the fields of a key=value configuration in the order it carries them', () => {
    const fields = parseConfigurationValue(ASSESSOR_REFERENCE)!;

    expect(fields.map(field => field.name))
      .toEqual(['provider', 'model', 'thinking', 'secondOpinion']);
    expect(fields[1].value).toBe('gpt-5.6-sol');
  });

  it('keeps an unconfigured field as the literal the wire writes for it', () => {
    const fields = parseConfigurationValue(ASSESSOR_REFERENCE)!;

    // The field being set on one side and absent on the other is the difference, so "(none)" is a
    // value to show, not an absence to hide.
    expect(fields[3]).toEqual({ name: 'secondOpinion', value: '(none)' });
  });

  it('tolerates surrounding space and a trailing separator', () => {
    expect(parseConfigurationValue(' provider = OpenAI ; model=gpt-5.6-sol; ')).toEqual([
      { name: 'provider', value: 'OpenAI' },
      { name: 'model', value: 'gpt-5.6-sol' }
    ]);
  });

  it('returns null for every value that has no fields to split', () => {
    expect(parseConfigurationValue(REFERENCE_DIGEST)).toBeNull();
    expect(parseConfigurationValue('{"systemPrompt":"x","temperature":0.2}')).toBeNull();
    expect(parseConfigurationValue('70:1,71:1')).toBeNull();
    expect(parseConfigurationValue('(none)')).toBeNull();
    expect(parseConfigurationValue('   ')).toBeNull();
  });

  it('refuses a value only half of which is fields', () => {
    // A half-parsed configuration would show real fields beside a remainder that silently vanished.
    expect(parseConfigurationValue('provider=OpenAI;just-a-word')).toBeNull();
  });
});

describe('conditionDetailFor', () => {
  it('returns null when the index has not loaded or does not carry the key', () => {
    expect(conditionDetailFor(null, 'run:3', [3])).toBeNull();
    expect(conditionDetailFor(buildIndex(), 'run:77', [77])).toBeNull();
  });

  it('names the source, its condition and the reference condition', () => {
    const detail = conditionDetailFor(buildIndex(), 'run:3', [3])!;

    expect(detail.sourceLabel).toBe('Run 3');
    expect(detail.conditionLabel).toBe('Condition B');
    expect(detail.conditionOrdinal).toBe(2);
    expect(detail.referenceConditionLabel).toBe('Condition A');
    expect(detail.rows.length).toBe(2);
  });

  it('attributes a run source its own variant and the reference condition the other', () => {
    const [assessor] = conditionDetailFor(buildIndex(), 'run:3', [3])!.rows;

    expect(assessor.thisValue).toBe(ASSESSOR_VARIANT);
    expect(assessor.referenceValue).toBe(ASSESSOR_REFERENCE);
    // Both sides attributed, so the raw variant list is not needed.
    expect(assessor.variants).toEqual([]);
  });

  it('describes a key by the label, kind and value kind the reference condition holds for it', () => {
    const [assessor, prompt] = conditionDetailFor(buildIndex(), 'run:3', [3])!.rows;

    expect(assessor.label).toBe('Assessor configuration');
    // The taxonomy kind, not the difference's own `MustMatch`.
    expect(assessor.kind).toBe('Instrument');
    expect(assessor.description).toContain('graded by a different assessor');
    expect(prompt.valueKind).toBe('Hash');
  });

  it('names only the fields that differ between two parsed configurations', () => {
    const [assessor] = conditionDetailFor(buildIndex(), 'run:3', [3])!.rows;

    expect(assessor.thisFields?.length).toBe(4);
    expect(assessor.changedFields).toEqual(['thinking']);
  });

  it('leaves a value with no fields unparsed rather than inventing them', () => {
    const [, prompt] = conditionDetailFor(buildIndex(), 'run:3', [3])!.rows;

    expect(prompt.thisFields).toBeNull();
    expect(prompt.referenceFields).toBeNull();
    expect(prompt.changedFields).toEqual([]);
    expect(prompt.thisValue).toBe(OTHER_DIGEST);
  });

  it("attributes a group's variant from its own member run ids", () => {
    const detail = conditionDetailFor(buildIndex(), 'group:4', [11, 12])!;

    expect(detail.sourceLabel).toBe('Analysis group 4');
    expect(detail.rows[0].thisValue).toBe(ASSESSOR_VARIANT);
    expect(detail.rows[0].referenceValue).toBe(ASSESSOR_REFERENCE);
  });

  it("falls back to the reference condition's own value when the membership is unknown", () => {
    // The picker's group list carries no members, so run ids are routinely unavailable; the value
    // the reference condition holds for the key still settles a two-variant difference.
    const [assessor] = conditionDetailFor(buildIndex(), 'group:4', [])!.rows;

    expect(assessor.referenceValue).toBe(ASSESSOR_REFERENCE);
    expect(assessor.thisValue).toBe(ASSESSOR_VARIANT);
  });

  it('lists every variant with its runs when neither side can be told apart', () => {
    const index = buildIndex({
      largestConditionKeys: [],
      entries: buildIndex().entries.map(entry => entry.key === 'run:3'
        ? {
          ...entry,
          differencesFromLargest: [{
            name: 'AssessorConfiguration',
            kind: 'MustMatch',
            description: 'Assessor configuration differs from the largest condition',
            variants: [
              { value: 'provider=OpenAI', runIds: [3] },
              { value: 'provider=Google', runIds: [1] },
              { value: 'provider=Anthropic', runIds: [2] }
            ]
          }]
        }
        : entry)
    });

    const [assessor] = conditionDetailFor(index, 'run:3', [])!.rows;

    expect(assessor.thisValue).toBeNull();
    expect(assessor.referenceValue).toBeNull();
    expect(assessor.variants.length).toBe(3);
    expect(assessor.variants[0].runIds).toEqual([3]);
    // With no described key the difference's own name has to carry the row.
    expect(assessor.label).toBe('AssessorConfiguration');
  });

  it('reports a self-inconsistent source with the keys its own members disagree on', () => {
    const detail = conditionDetailFor(buildIndex(), 'group:9', [])!;

    expect(detail.selfInconsistent).toBeTrue();
    expect(detail.selfInconsistentKeys).toEqual(['BenchmarkSuiteId', 'CandidateModelId']);
    // Such a source is not in any condition, so it differs from the reference on nothing named.
    expect(detail.rows).toEqual([]);
    expect(detail.conditionLabel).toBe('Self-inconsistent');
  });
});

describe('buildConditionLegend', () => {
  function condition(
    ordinal: number,
    newestRunStartedAtUtc: string | null
  ): BenchmarkComparabilityConditionDto {
    return {
      ordinal,
      label: `Condition ${String.fromCharCode(64 + ordinal)}`,
      sourceCount: 1,
      runCount: 1,
      signature: `sig-${ordinal}`,
      newestRunStartedAtUtc
    };
  }

  it('returns no reference, no others and an empty map for no index', () => {
    const legend = buildConditionLegend(null);

    expect(legend.reference).toBeNull();
    expect(legend.others).toEqual([]);
    expect(legend.entryByKey.size).toBe(0);
  });

  it('maps every entry by key, the unassigned ones included', () => {
    const legend = buildConditionLegend(buildIndex());

    expect(legend.entryByKey.size).toBe(4);
    expect(legend.entryByKey.get('group:9')?.conditionLabel).toBe('Self-inconsistent');
  });

  it('pins ordinal 1 as the reference even when another condition is newer', () => {
    const index = buildIndex({
      entries: [],
      conditions: [
        condition(1, '2026-09-01T08:00:00Z'),
        condition(2, '2026-09-05T08:00:00Z'),
        condition(3, null),
        condition(4, '2026-09-09T08:00:00Z'),
        condition(5, '2026-09-05T08:00:00Z')
      ]
    });

    const legend = buildConditionLegend(index);

    expect(legend.reference?.condition.ordinal).toBe(1);
    expect(legend.reference?.isReference).toBeTrue();
    // Newest first; the undated condition last; the two equal dates in ordinal order.
    expect(legend.others.map(item => item.condition.ordinal)).toEqual([4, 2, 5, 3]);
    expect(legend.others.every(item => !item.isReference)).toBeTrue();
  });

  it('falls back to the first condition when none has ordinal 1', () => {
    const legend = buildConditionLegend(buildIndex({
      entries: [],
      conditions: [condition(3, null), condition(2, null)]
    }));

    expect(legend.reference?.condition.ordinal).toBe(3);
    expect(legend.others.map(item => item.condition.ordinal)).toEqual([2]);
  });

  it('groups members by condition, runs first, and reads the cohort through a run', () => {
    const index = buildIndex({
      entries: [
        ...buildIndex().entries,
        // A group listed before a run of the same condition, with a lower id.
        buildEntry({
          key: 'group:2', sourceKind: 'Group', sourceId: 2, conditionOrdinal: 3,
          differencesFromLargest: []
        }),
        buildEntry({
          key: 'run:7', sourceId: 7, conditionOrdinal: 3,
          differencesFromLargest: differences([7]).slice(0, 1)
        })
      ],
      conditions: [...buildIndex().conditions, condition(3, '2026-09-08T08:00:00Z')]
    });

    const legend = buildConditionLegend(index);

    expect(legend.reference?.memberKeys).toEqual(['run:1']);
    expect(legend.reference?.differingKeyLabels).toEqual([]);

    // Condition B's newest run (9 September) is newer than the third condition's (8 September).
    const [second, third] = legend.others;
    expect(third.memberKeys).toEqual(['run:7', 'group:2']);
    expect(third.representativeKey).toBe('run:7');
    expect(third.differingKeyLabels).toEqual(['Assessor configuration']);

    expect(second.memberKeys).toEqual(['run:3', 'group:4']);
    expect(second.differingKeyLabels).toEqual(['Assessor configuration', 'Candidate system prompt']);
  });

  it('takes the first group as the representative of a cohort with no run', () => {
    const index = buildIndex({
      entries: [
        buildEntry({ key: 'run:1', sourceId: 1 }),
        buildEntry({ key: 'group:8', sourceKind: 'Group', sourceId: 8, conditionOrdinal: 2 }),
        buildEntry({ key: 'group:5', sourceKind: 'Group', sourceId: 5, conditionOrdinal: 2 })
      ]
    });

    const [other] = buildConditionLegend(index).others;

    expect(other.memberKeys).toEqual(['group:5', 'group:8']);
    expect(other.representativeKey).toBe('group:5');
  });
});

describe('summarizeConditionDifference', () => {
  function row(overrides: Partial<ConditionDetailRow> = {}): ConditionDetailRow {
    return {
      name: 'serviceTier',
      label: 'Candidate service tier',
      kind: 'Instrument',
      valueKind: 'Text',
      description: 'A difference here means the runs were served at different priorities.',
      thisValue: 'priority',
      referenceValue: 'standard',
      thisFields: null,
      referenceFields: null,
      changedFields: [],
      variants: [],
      ...overrides
    };
  }

  it('reads a configuration field by field, charted value first', () => {
    const [assessor] = conditionDetailFor(buildIndex(), 'run:3', [3])!.rows;

    expect(summarizeConditionDifference(assessor)).toEqual({
      kind: 'fields',
      changes: [{ name: 'thinking', from: 'medium', to: 'high' }]
    });
  });

  it('writes (none) for a field present on one side only', () => {
    const summary = summarizeConditionDifference(row({
      referenceValue: 'provider=OpenAI',
      thisValue: 'provider=OpenAI;secondOpinion=Google',
      referenceFields: [{ name: 'provider', value: 'OpenAI' }],
      thisFields: [
        { name: 'provider', value: 'OpenAI' },
        { name: 'secondOpinion', value: 'Google' }
      ],
      changedFields: ['secondOpinion']
    }));

    expect(summary).toEqual({
      kind: 'fields',
      changes: [{ name: 'secondOpinion', from: '(none)', to: 'Google' }]
    });
  });

  it('shortens a digest on both sides', () => {
    const [, prompt] = conditionDetailFor(buildIndex(), 'run:3', [3])!.rows;

    expect(summarizeConditionDifference(prompt)).toEqual({
      kind: 'hash',
      from: REFERENCE_DIGEST.slice(0, 12),
      to: OTHER_DIGEST.slice(0, 12)
    });
    // Digest-shaped values are shortened whatever kind the key declares.
    expect(summarizeConditionDifference(row({
      referenceValue: REFERENCE_DIGEST, thisValue: OTHER_DIGEST
    })).kind).toBe('hash');
  });

  it('shows a short value inline, charted value first', () => {
    expect(summarizeConditionDifference(row()))
      .toEqual({ kind: 'text', from: 'standard', to: 'priority' });
  });

  it('refers a long or multi-line value to the full detail', () => {
    expect(summarizeConditionDifference(row({ thisValue: 'x'.repeat(81) })).kind).toBe('long');
    expect(summarizeConditionDifference(row({ referenceValue: 'one\ntwo' })).kind).toBe('long');
  });

  it('says so when either side could not be attributed', () => {
    expect(summarizeConditionDifference(row({ thisValue: null })).kind).toBe('unattributed');
    expect(summarizeConditionDifference(row({ referenceValue: null })).kind).toBe('unattributed');
  });
});

/**
 * A comparison entry carrying only what the question-coverage and cost adapters read. The rest of
 * the wire shape is irrelevant to them, so the cast keeps the fixture to the fields under test.
 */
function buildComparisonEntry(
  key: string,
  quality: Partial<BenchmarkModelComparisonQualityDto> | null,
  overrides: Partial<BenchmarkModelComparisonEntryDto> = {}
): BenchmarkModelComparisonEntryDto {
  return {
    key,
    label: key,
    modelDisplayName: key,
    modelId: key,
    runCount: 1,
    excluded: false,
    excludingKeys: [],
    speedDegraded: false,
    costDegraded: false,
    quality: quality === null
      ? null
      : { pointEstimate: 80, itemCount: 18, examItemCount: 18, unscoredItemCount: 0, ...quality },
    cost: null,
    ...overrides
  } as unknown as BenchmarkModelComparisonEntryDto;
}

function buildComparison(entries: BenchmarkModelComparisonEntryDto[]): BenchmarkModelComparisonDto {
  return { entries, pricingBasis: 'Current', pricingBasisLabel: 'Current catalog' } as unknown as BenchmarkModelComparisonDto;
}

describe('toChartContext', () => {
  it('spans the charted entries\' scored counts against the exam', () => {
    const same = toChartContext(buildComparison([
      buildComparisonEntry('a', { itemCount: 16, unscoredItemCount: 2 }),
      buildComparisonEntry('b', { itemCount: 16, unscoredItemCount: 2 })
    ]));
    expect([same.scoredItemsMin, same.scoredItemsMax, same.examItemCount]).toEqual([16, 16, 18]);

    const differing = toChartContext(buildComparison([
      buildComparisonEntry('a', { itemCount: 16, unscoredItemCount: 2 }),
      buildComparisonEntry('b', { itemCount: 15, unscoredItemCount: 3 })
    ]));
    expect([differing.scoredItemsMin, differing.scoredItemsMax, differing.examItemCount]).toEqual([15, 16, 18]);
  });

  it('reads the exam size from examItemCount, whatever the suite holds now', () => {
    const context = toChartContext(buildComparison([
      buildComparisonEntry('a', { itemCount: 18, examItemCount: 18 }),
      buildComparisonEntry('b', { itemCount: 18, examItemCount: 18 })
    ]));
    expect([context.scoredItemsMin, context.scoredItemsMax, context.examItemCount]).toEqual([18, 18, 18]);
  });

  it('is all zeros with no charted entry, and ignores excluded ones', () => {
    const none = toChartContext(buildComparison([
      buildComparisonEntry('x', null, { excluded: true })
    ]));
    expect([none.scoredItemsMin, none.scoredItemsMax, none.examItemCount]).toEqual([0, 0, 0]);
  });
});

describe('questionCoverageNotes', () => {
  it('is empty when every charted entry scored every asked question', () => {
    expect(questionCoverageNotes([buildComparisonEntry('a', {}), buildComparisonEntry('b', {})])).toEqual([]);
  });

  it('adds no note about rubric revisions', () => {
    const notes = questionCoverageNotes([
      buildComparisonEntry('a', { itemCount: 18, examItemCount: 18 }),
      buildComparisonEntry('b', { itemCount: 18, examItemCount: 18 })
    ]);
    expect(notes).toEqual([]);
    expect(notes.some(note => note.tone === 'info')).toBeFalse();
  });

  it('warns once per entry with unscored questions, naming the entry', () => {
    const notes = questionCoverageNotes([
      buildComparisonEntry('GPT-5.6 Luna (max)', { itemCount: 17, unscoredItemCount: 1 }),
      buildComparisonEntry('Gemini 3.7 Flash (medium)', {})
    ]);
    expect(notes).toEqual([{
      text: 'GPT-5.6 Luna (max): 1 question has no scored answer (failed, skipped or ungraded) and is left out of its index.',
      tone: 'warning'
    }]);
  });

  it('names the entry with its thinking level, as the figures do', () => {
    const notes = questionCoverageNotes([
      buildComparisonEntry('run:1', { itemCount: 17, unscoredItemCount: 1 }, {
        label: 'GPT-5.6 Luna', modelDisplayName: 'GPT-5.6 Luna', thinkingLevel: 'max'
      })
    ]);
    expect(notes[0].text).toBe(
      'GPT-5.6 Luna (max): 1 question has no scored answer (failed, skipped or ungraded) and is left out of its index.'
    );
  });

  it('warns for each entry with unscored questions, in entry order', () => {
    const notes = questionCoverageNotes([
      buildComparisonEntry('a', { itemCount: 15, unscoredItemCount: 3 }),
      buildComparisonEntry('b', { itemCount: 16, unscoredItemCount: 2 })
    ]);
    expect(notes.map(note => note.tone)).toEqual(['warning', 'warning']);
    expect(notes[0].text).toBe(
      'a: 3 questions have no scored answer (failed, skipped or ungraded) and are left out of its index.'
    );
    expect(notes[1].text).toBe(
      'b: 2 questions have no scored answer (failed, skipped or ungraded) and are left out of its index.'
    );
  });
});

describe('normalizeThinkingLevel', () => {
  it('trims, lower-cases the first letter only, and is null without a level', () => {
    expect(normalizeThinkingLevel('Max')).toBe('max');
    expect(normalizeThinkingLevel(' xhigh ')).toBe('xhigh');
    expect(normalizeThinkingLevel('XHigh')).toBe('xHigh');
    for (const value of [null, undefined, '', '   ']) {
      expect(normalizeThinkingLevel(value)).withContext(String(value)).toBeNull();
    }
  });
});

describe('toChartEntries labels', () => {
  function chartEntry(overrides: Partial<BenchmarkModelComparisonEntryDto>) {
    return toChartEntries(buildComparison([buildComparisonEntry('run:1', {}, overrides)]))[0];
  }

  it('always follows the display name with the thinking level in parentheses', () => {
    const entry = chartEntry({ label: 'GPT-5.6 Luna', modelDisplayName: 'GPT-5.6 Luna', thinkingLevel: 'max' });
    expect(entry.label).toBe('GPT-5.6 Luna (max)');
    expect(entry.name).toBe('GPT-5.6 Luna');
    expect(entry.thinkingLevel).toBe('max');
  });

  it('starts the level with a lower-case letter', () => {
    expect(chartEntry({ modelDisplayName: 'GPT-5.6 Luna', thinkingLevel: 'Max' }).label).toBe('GPT-5.6 Luna (max)');
  });

  it('adds no parentheses without a thinking level', () => {
    for (const thinkingLevel of [null, '  ']) {
      const entry = chartEntry({ modelDisplayName: 'GPT-5.6 Luna', thinkingLevel });
      expect(entry.label).withContext(String(thinkingLevel)).toBe('GPT-5.6 Luna');
      expect(entry.thinkingLevel).withContext(String(thinkingLevel)).toBeNull();
    }
  });

  it('does not repeat a level the server label already carries', () => {
    expect(chartEntry({ label: 'X (low)', modelDisplayName: 'X', thinkingLevel: 'low' }).label).toBe('X (low)');
  });
});

describe('toChartEntries candidate run cost', () => {
  it('reads the run cost off the cost object, and is UNMEASURED without one', () => {
    const [priced, unpriced] = toChartEntries(buildComparison([
      buildComparisonEntry('priced', {}, {
        cost: { candidateCostPerQuestionUsd: 0.01, candidateCostPerRunUsd: 0.18 } as BenchmarkModelComparisonCostDto
      }),
      buildComparisonEntry('unpriced', {})
    ]));
    expect(priced.candidateCostPerRunUsd).toBe(0.18);
    expect(Number.isNaN(unpriced.candidateCostPerRunUsd)).toBeTrue();
  });
});
