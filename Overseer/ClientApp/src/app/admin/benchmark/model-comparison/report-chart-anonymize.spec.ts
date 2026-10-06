import type { BenchmarkModelComparisonDto, BenchmarkModelComparisonEntryDto } from './model-comparison.models';
import { toChartEntries } from './model-comparison.models';
import {
  anonymizeComparisonForAll,
  anonymizeComparisonForSubject,
  anonymizedPeerName,
  restrictComparisonToEntries
} from './report-chart-anonymize';

function entry(key: string, provider: string, modelId: string, name: string, overrides: Partial<BenchmarkModelComparisonEntryDto> = {}): BenchmarkModelComparisonEntryDto {
  return {
    key,
    sourceKind: 'Run',
    sourceId: Number(key.split(':')[1]),
    sourceName: `${name} nightly`,
    runIds: [Number(key.split(':')[1])],
    runCount: 1,
    suiteId: 5,
    suiteName: 'Board Suite',
    provider,
    modelId,
    modelDisplayName: name,
    thinkingLevel: 'medium',
    reasoningMode: null,
    reasoningSummary: null,
    serviceTier: null,
    maxOutputTokens: 8192,
    parallelExecutionMode: 'Enabled',
    label: name,
    firstRunStartedAtUtc: '2026-09-01T10:00:00Z',
    lastRunStartedAtUtc: '2026-09-01T10:00:00Z',
    state: 'Comparable',
    comparable: true,
    excluded: false,
    speedDegraded: false,
    costDegraded: false,
    excludingKeys: [],
    speedDegradingKeys: [],
    costDegradingKeys: [],
    differences: [{ name: 'ModelId', kind: 'Candidate', description: 'The model.', variants: [{ value: modelId, runIds: [1] }] }],
    explanation: `${name} (${modelId}) is comparable with the baseline.`,
    quality: {
      pointEstimate: 70,
      itemCount: 18,
      examItemCount: 18,
      unscoredItemCount: 0,
      intervalHalfWidth: 5,
      intervalLower: 65,
      intervalUpper: 75,
      intervalTruncated: false,
      itemSamplingHalfWidth: 5,
      reproducibilityHalfWidth: null,
      reproducibilityStandardDeviation: null,
      reproducibilityAvailable: false,
      intervalBasis: 'item sampling'
    },
    speed: null,
    cost: null,
    table: null,
    ...overrides
  } as BenchmarkModelComparisonEntryDto;
}

function comparison(entries: BenchmarkModelComparisonEntryDto[]): BenchmarkModelComparisonDto {
  return {
    pricingBasis: 'Current',
    pricingBasisLabel: 'Current catalog, as of 2026-09-07',
    computedAtUtc: '2026-09-07T12:00:00Z',
    baselineSuiteId: 5,
    baselineSuiteName: 'Board Suite',
    baselineEntryKeys: entries.map(e => e.key),
    baselineKeyValues: { BenchmarkSuiteId: '5' },
    baselineSignature: 'abc',
    modelAxisKeys: ['Provider', 'ModelId'],
    entries,
    comparableCount: entries.length,
    excludedCount: 0,
    thinkingLevelsDiffer: false,
    speedAxisCaveat: 'Claude Harbor answered with parallel questions.',
    explanation: 'Claude Harbor and GPT Lantern and Mistral Quill were compared with Gemini Orchard.',
    excludedMeasures: [],
    panelDiagnostics: {
      applicable: true,
      memberALabel: 'Claude Harbor',
      memberAProvider: 'Anthropic',
      memberBLabel: 'GPT Lantern',
      memberBProvider: 'OpenAI',
      entries: [],
      judgeDependentPairs: [],
      referenceDependentPairs: [],
      familyGaps: [],
      accusationAudit: [],
      auditSummaries: [],
      caveats: ['Anthropic and OpenAI graded each other.']
    }
  };
}

describe('anonymizeComparisonForSubject', () => {
  const subject = entry('run:1', 'Google', 'gemini-orchard-2', 'Gemini Orchard');
  const peerA = entry('run:2', 'Anthropic', 'claude-harbor-5', 'Claude Harbor');
  const peerB = entry('run:3', 'OpenAI', 'gpt-lantern-4', 'GPT Lantern');
  const unlettered = entry('run:4', 'Mistral', 'mistral-quill-1', 'Mistral Quill');
  const dto = comparison([subject, peerA, peerB, unlettered]);
  const letters = { 'run:2': 'A', 'run:3': 'B' };

  it('names each lettered peer Model X, with no provider and no model id, and removes the others', () => {
    const copy = anonymizeComparisonForSubject(dto, 'run:1', letters);

    expect(copy.entries.map(e => e.key)).toEqual(['run:1', 'run:2', 'run:3']);
    const [, a, b] = copy.entries;
    expect(a.label).toBe('Model A');
    expect(a.modelDisplayName).toBe('Model A');
    expect(a.provider).toBe('');
    expect(a.modelId).toBe('');
    expect(b.label).toBe(anonymizedPeerName('B'));
    expect(copy.baselineEntryKeys).toEqual(['run:1', 'run:2', 'run:3']);
    expect(copy.comparableCount).toBe(3);
    expect(copy.excludedCount).toBe(0);
    // An empty provider is what draws the neutral gray.
    expect(toChartEntries(copy).map(e => e.provider)).toEqual(['google', '', '']);
  });

  it('leaves no peer name, provider or model id anywhere in the copy', () => {
    const json = JSON.stringify(anonymizeComparisonForSubject(dto, 'run:1', letters));

    for (const peer of [peerA, peerB, unlettered]) {
      for (const text of [peer.label, peer.modelDisplayName, peer.modelId, peer.provider, peer.sourceName!]) {
        expect(json.toLowerCase(), text).not.toContain(text.toLowerCase());
      }
    }
  });

  it('keeps the subject exactly as it is', () => {
    const copy = anonymizeComparisonForSubject(dto, 'run:1', letters);

    expect(copy.entries[0]).toEqual(subject);
    const json = JSON.stringify(copy);
    expect(json).toContain('Gemini Orchard');
    expect(json).toContain('gemini-orchard-2');
    expect(json).toContain('Google');
  });

  it('never changes its input', () => {
    const before = JSON.stringify(dto);

    anonymizeComparisonForSubject(dto, 'run:1', letters);

    expect(JSON.stringify(dto)).toBe(before);
  });

  it('keeps only the subject when no peer is lettered', () => {
    const copy = anonymizeComparisonForSubject(dto, 'run:1', {});

    expect(copy.entries.map(e => e.key)).toEqual(['run:1']);
    expect(JSON.stringify(copy)).not.toContain('Claude Harbor');
  });
});

describe('anonymizeComparisonForAll', () => {
  const first = entry('run:1', 'Google', 'gemini-orchard-2', 'Gemini Orchard');
  const second = entry('run:2', 'Anthropic', 'claude-harbor-5', 'Claude Harbor');
  const third = entry('run:3', 'OpenAI', 'gpt-lantern-4', 'GPT Lantern');
  const uncovered = entry('run:4', 'Mistral', 'mistral-quill-1', 'Mistral Quill');
  const dto = comparison([first, second, third, uncovered]);
  const letters = { 'run:1': 'B', 'run:2': 'A', 'run:3': 'C' };

  it('letters every covered entry by the covered set\'s letters, with no provider or model id, and drops the rest', () => {
    const copy = anonymizeComparisonForAll(dto, letters);

    expect(copy.entries.map(e => [e.key, e.label, e.modelDisplayName, e.provider, e.modelId])).toEqual([
      ['run:1', 'Model B', 'Model B', '', ''],
      ['run:2', 'Model A', 'Model A', '', ''],
      ['run:3', 'Model C', 'Model C', '', '']
    ]);
    expect(copy.baselineEntryKeys).toEqual(['run:1', 'run:2', 'run:3']);
    expect(copy.comparableCount).toBe(3);
    expect(copy.excludedCount).toBe(0);
    expect(copy.panelDiagnostics).toBeNull();
    // Every entry in the neutral gray.
    expect(toChartEntries(copy).map(e => e.provider)).toEqual(['', '', '']);
  });

  it('leaves no entry\'s name, provider, model id or source name anywhere in the copy', () => {
    const json = JSON.stringify(anonymizeComparisonForAll(dto, letters)).toLowerCase();

    for (const model of [first, second, third, uncovered]) {
      for (const text of [model.label, model.modelDisplayName, model.modelId, model.provider, model.sourceName!]) {
        expect(json, text).not.toContain(text.toLowerCase());
      }
    }
  });

  it('never changes its input', () => {
    const before = JSON.stringify(dto);

    anonymizeComparisonForAll(dto, letters);

    expect(JSON.stringify(dto)).toBe(before);
  });
});

describe('restrictComparisonToEntries', () => {
  const first = entry('run:1', 'Google', 'gemini-orchard-2', 'Gemini Orchard');
  const second = entry('run:2', 'Anthropic', 'claude-harbor-5', 'Claude Harbor');
  const excluded = entry('run:3', 'OpenAI', 'gpt-lantern-4', 'GPT Lantern', { excluded: true, comparable: false, state: 'Excluded' });
  const dto = comparison([first, second, excluded]);

  it('keeps the named entries as they are, recounts, and drops the judge-family diagnostics', () => {
    const copy = restrictComparisonToEntries(dto, ['run:3', 'run:1']);

    expect(copy.entries).toEqual([first, excluded]);
    expect(copy.baselineEntryKeys).toEqual(['run:1', 'run:3']);
    expect(copy.excludedCount).toBe(1);
    expect(copy.comparableCount).toBe(1);
    expect(copy.panelDiagnostics).toBeNull();
    expect(dto.entries.length).toBe(3);
  });
});
