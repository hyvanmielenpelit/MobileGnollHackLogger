import {
  conditionDetailFor,
  parseConfigurationValue
} from './model-comparison.models';
import type {
  BenchmarkComparabilityIndexDto,
  BenchmarkComparabilityIndexEntryDto,
  BenchmarkComparabilityKeyValueDto
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
