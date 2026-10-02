import type { BenchmarkRunDetailDto } from '../../../services/admin-benchmark.service';
import {
  RUN_FACT_PRIMARY_KEYS,
  RunFactModel,
  RunFactRow,
  boardDeliveryFigures,
  buildRunFacts,
  candidatePromptParts,
  formatCandidatePrompt,
  runFactBadges,
  runFactPlainText,
  runFactsReadout
} from './run-facts';

function run(overrides: Partial<BenchmarkRunDetailDto> = {}): BenchmarkRunDetailDto {
  return {
    id: 75,
    suiteName: 'Snapshot: Tommi2 2026-09-17',
    testedModelDisplayNameUsed: 'GPT-6.1 Sol',
    testedModelProviderUsed: 'OpenAI',
    testedModelIdUsed: 'gpt-6.1-sol',
    testedModelThinkingLevelUsed: 'high',
    testedModelReasoningModeUsed: null,
    testedModelServiceTierUsed: null,
    testedModelParallelExecutionModeUsed: 2,
    testedModelEndpoint: 'official',
    assessorModelDisplayNameUsed: 'Claude 5 Opus',
    assessorModelProviderUsed: 'Anthropic',
    assessorModelIdUsed: 'claude-5-opus',
    assessorModelThinkingLevelUsed: 'high',
    assessorModelReasoningModeUsed: null,
    status: 'Completed',
    startedAtUtc: '2026-09-30T13:35:24',
    totalAnswerDurationMs: 0,
    ...overrides
  } as BenchmarkRunDetailDto;
}

const PANEL: Partial<BenchmarkRunDetailDto> = {
  isPanelRun: true,
  coAssessorModelDisplayNameUsed: 'GPT-5.6 Sol',
  coAssessorModelProviderUsed: 'OpenAI',
  coAssessorModelIdUsed: 'gpt-5.6-sol',
  coAssessorModelThinkingLevelUsed: 'medium',
  coAssessorModelReasoningModeUsed: 'Standard'
};

const BOARD: Partial<BenchmarkRunDetailDto> = {
  boardDelivery: [
    { role: 'claim verifier', delivered: 16, total: 16, missingQuestions: [] },
    { role: 'assessor', delivered: 18, total: 18, missingQuestions: [] },
    { role: 'co-assessor', delivered: 17, total: 18, missingQuestions: [4] },
    { role: 'reference reader', delivered: 18, total: 18, missingQuestions: [] }
  ]
};

const PROMPT_JSON = JSON.stringify({ verboseMode: false, enableToolUse: true, hasGameSnapshot: true });

function model(overrides: Partial<RunFactModel> = {}): RunFactModel {
  return {
    name: 'M', provider: 'OpenAI', thinkingLevel: null, reasoningMode: null, serviceTier: null, customEndpoint: false,
    ...overrides
  };
}

function row(rows: RunFactRow[], key: string): RunFactRow | undefined {
  return rows.find(r => r.key === key);
}

describe('run facts', () => {
  describe('buildRunFacts', () => {
    it('lists a single-assessor run as Model, Assessor, Scoring profile and Started, in order', () => {
      const rows = buildRunFacts(run(), { gaps: [] });
      expect(rows.map(r => [r.key, r.label])).toEqual([
        ['model', 'Model'], ['assessor', 'Assessor'], ['profile', 'Scoring profile'], ['started', 'Started']
      ]);
      const assessor = row(rows, 'assessor')!.item;
      expect(assessor.kind === 'models' && assessor.models.map(m => [m.role, m.name])).toEqual([[undefined, 'Claude 5 Opus']]);
      expect(row(rows, 'profile')!.item).toEqual({ kind: 'text', text: 'Default' });
    });

    it('lists both panel members as Assessors, tagged A and B', () => {
      const rows = buildRunFacts(run(PANEL), { gaps: [] });
      const assessor = row(rows, 'assessor')!;
      expect(assessor.label).toBe('Assessors');
      expect(assessor.item.kind === 'models' && assessor.item.models.map(m => [m.role, m.name, m.thinkingLevel]))
        .toEqual([['A', 'Claude 5 Opus', 'high'], ['B', 'GPT-5.6 Sol', 'medium']]);
    });

    it('carries the model under test with its service tier and a custom endpoint, the assessors with neither', () => {
      const rows = buildRunFacts(run({ testedModelServiceTierUsed: 'flex', testedModelEndpoint: 'custom (key; fingerprint ab12)' }), { gaps: [] });
      const tested = row(rows, 'model')!.item;
      const assessor = row(rows, 'assessor')!.item;
      expect(tested.kind === 'models' && tested.models[0]).toEqual(expect.objectContaining({ serviceTier: 'flex', customEndpoint: true }));
      expect(assessor.kind === 'models' && assessor.models[0]).toEqual(expect.objectContaining({ serviceTier: null, customEndpoint: false }));

      const official = row(buildRunFacts(run(), { gaps: [] }), 'model')!.item;
      expect(official.kind === 'models' && official.models[0].customEndpoint).toBe(false);
    });

    it('falls back from the display name to the model id, then to not recorded', () => {
      const rows = buildRunFacts(run({ testedModelDisplayNameUsed: '', assessorModelDisplayNameUsed: '', assessorModelIdUsed: '' }), { gaps: [] });
      expect(runFactPlainText(row(rows, 'model')!)).toBe('gpt-6.1-sol');
      expect(runFactPlainText(row(rows, 'assessor')!)).toBe('not recorded');
    });

    it('has a Prompt row only with prompt options, named by the source when they do not parse', () => {
      expect(row(buildRunFacts(run(), { gaps: [] }), 'prompt')).toBeUndefined();

      const parsed = row(buildRunFacts(run({ candidatePromptOptionsJson: PROMPT_JSON }), { gaps: [] }), 'prompt')!;
      expect(parsed.item).toEqual({
        kind: 'prompt', name: 'Gameplay Help', tags: ['concise', 'tools on', 'snapshot'],
        summary: 'Gameplay Help · concise (tools on) · snapshot'
      });

      const invalid = row(buildRunFacts(run({ candidatePromptOptionsJson: '{bad', candidatePromptSourceUsed: 'Custom.Source' }), { gaps: [] }), 'prompt')!;
      expect(invalid.item).toEqual({ kind: 'prompt', name: 'Custom.Source', tags: [], summary: 'Custom.Source' });
    });

    it('names the scoring profile', () => {
      const rows = buildRunFacts(run({ scoringProfileName: 'Strict' }), { gaps: [] });
      expect(runFactPlainText(row(rows, 'profile')!)).toBe('Strict');
    });

    it('gives the start time a zone-qualified datetime, adding Z only when there is none', () => {
      const bare = row(buildRunFacts(run(), { gaps: [] }), 'started')!.item;
      expect(bare).toEqual({ kind: 'time', iso: '2026-09-30T13:35:24Z', text: '2026-09-30 13:35:24 UTC' });

      for (const zoned of ['2026-09-30T13:35:24Z', '2026-09-30T13:35:24.5+00:00']) {
        const item = row(buildRunFacts(run({ startedAtUtc: zoned }), { gaps: [] }), 'started')!.item;
        expect(item.kind === 'time' && item.iso).toBe(zoned);
      }
    });

    it('reads a missing start time as not recorded, and an unparsable one as given', () => {
      const missing = row(buildRunFacts(run({ startedAtUtc: undefined as unknown as string }), { gaps: [] }), 'started')!;
      expect(missing.item).toEqual({ kind: 'text', text: 'not recorded' });
      const garbled = row(buildRunFacts(run({ startedAtUtc: 'sometime' }), { gaps: [] }), 'started')!;
      expect(garbled.item).toEqual({ kind: 'text', text: 'sometime' });
    });

    it('has a Board row only with board figures, the co-assessor only on a panel run, and the gaps it is given', () => {
      expect(row(buildRunFacts(run(), { gaps: [] }), 'board')).toBeUndefined();

      const panel = row(buildRunFacts(run({ ...PANEL, ...BOARD }), { gaps: ['co-assessor: Q4'] }), 'board')!;
      expect(panel.item).toEqual({
        kind: 'board',
        figures: [
          { role: 'Assessor', delivered: 18, total: 18 },
          { role: 'Co-assessor', delivered: 17, total: 18 },
          { role: 'Reference reader', delivered: 18, total: 18 },
          { role: 'Claim verifier', delivered: 16, total: 16 }
        ],
        note: 'Synthesis: yes · Difficulty assessment: digest (no map)',
        gaps: ['co-assessor: Q4']
      });

      const single = boardDeliveryFigures(run({
        boardDelivery: [
          { role: 'assessor', delivered: 18, total: 18, missingQuestions: [] },
          { role: 'co-assessor', delivered: 18, total: 18, missingQuestions: [] },
          { role: 'second reader', delivered: 13, total: 14, missingQuestions: [] }
        ]
      }));
      expect(single).toEqual([
        { role: 'Assessor', delivered: 18, total: 18 },
        { role: 'Second reader', delivered: 13, total: 14 }
      ]);
    });
  });

  describe('runFactBadges', () => {
    it('shows no thinking badge without a configured level', () => {
      expect(runFactBadges(model()).map(b => b.kind)).toEqual(['provider']);
    });

    it('hides the reasoning badge for default and standard, in any case', () => {
      for (const mode of ['default', 'Standard', 'STANDARD']) {
        expect(runFactBadges(model({ reasoningMode: mode })).map(b => b.kind), mode).toEqual(['provider']);
      }
      expect(runFactBadges(model({ reasoningMode: 'max' })).map(b => [b.kind, b.text])).toEqual([['reasoning', 'max'], ['provider', 'OpenAI']]);
    });

    it('orders thinking, reasoning, provider, tier and endpoint, each with its spoken prefix', () => {
      const badges = runFactBadges(model({
        thinkingLevel: 'high', reasoningMode: 'pro', serviceTier: 'standard_only', customEndpoint: true
      }));
      expect(badges.map(b => [b.kind, b.text, b.srPrefix])).toEqual([
        ['thinking', 'High', 'thinking level '],
        ['reasoning', 'pro', 'reasoning mode '],
        ['provider', 'OpenAI', ''],
        ['tier', 'Standard Only', 'service tier '],
        ['endpoint', 'Custom endpoint', '']
      ]);
      expect(badges[2].provider).toBe('OpenAI');
    });
  });

  describe('runFactPlainText', () => {
    it('reads each kind of row as one line', () => {
      const rows = buildRunFacts(run({ ...PANEL, ...BOARD, candidatePromptOptionsJson: PROMPT_JSON }), { gaps: ['x'] });
      expect(rows.map(runFactPlainText)).toEqual([
        'GPT-6.1 Sol',
        'Claude 5 Opus + GPT-5.6 Sol',
        'Gameplay Help · concise (tools on) · snapshot',
        'Default',
        '2026-09-30 13:35:24 UTC',
        'Assessor 18/18 · Co-assessor 17/18 · Reference reader 18/18 · Claim verifier 16/16'
      ]);
    });
  });

  describe('RUN_FACT_PRIMARY_KEYS', () => {
    it('is the model and the assessor', () => {
      expect([...RUN_FACT_PRIMARY_KEYS]).toEqual(['model', 'assessor']);
    });
  });

  describe('runFactsReadout', () => {
    const COMPLETE_BOARD: Partial<BenchmarkRunDetailDto> = {
      boardDelivery: [
        { role: 'assessor', delivered: 18, total: 18, missingQuestions: [] },
        { role: 'claim verifier', delivered: 16, total: 16, missingQuestions: [] }
      ]
    };

    function secondary(rows: RunFactRow[]): RunFactRow[] {
      return rows.filter(r => !(RUN_FACT_PRIMARY_KEYS as readonly string[]).includes(r.key));
    }

    it('reads the prompt name without its tags, the profile, the start time and the board at the question count', () => {
      const rows = buildRunFacts(
        run({ ...COMPLETE_BOARD, candidatePromptOptionsJson: PROMPT_JSON, scoringProfileName: 'Standard Intelligence Index' }),
        { gaps: [] });
      expect(runFactsReadout(secondary(rows)))
        .toBe('Gameplay Help · Standard Intelligence Index · Started 2026-09-30 13:35:24 UTC · Board 18/18');
    });

    it('ignores the model and assessor rows', () => {
      const rows = buildRunFacts(run({ candidatePromptOptionsJson: PROMPT_JSON }), { gaps: [] });
      expect(runFactsReadout(rows)).toBe(runFactsReadout(secondary(rows)));
    });

    it('leaves out a missing prompt row and a missing board row', () => {
      const rows = buildRunFacts(run(), { gaps: [] });
      expect(runFactsReadout(secondary(rows))).toBe('Default · Started 2026-09-30 13:35:24 UTC');
    });

    it('reads a board with gaps as incomplete', () => {
      const rows = buildRunFacts(run({ ...PANEL, ...BOARD }), { gaps: ['co-assessor: Q4'] });
      expect(runFactsReadout(secondary(rows))).toBe('Default · Started 2026-09-30 13:35:24 UTC · Board incomplete');
    });

    it('reads no rows as an empty string', () => {
      expect(runFactsReadout([])).toBe('');
    });
  });

  describe('the candidate prompt summary', () => {
    const cases: { name: string; json: string | null; source?: string; expected: string | null }[] = [
      { name: 'concise, tools on, snapshot', json: '{"verboseMode":false,"enableToolUse":true,"hasGameSnapshot":true}', expected: 'Gameplay Help · concise (tools on) · snapshot' },
      { name: 'detailed, tools off, no snapshot', json: '{"verboseMode":true,"enableToolUse":false}', expected: 'Gameplay Help · detailed (tools off)' },
      { name: 'tools unset counts as on', json: '{}', expected: 'Gameplay Help · concise (tools on)' },
      { name: 'invalid JSON with a source', json: '{bad', source: 'Custom.Source', expected: 'Custom.Source' },
      { name: 'invalid JSON without a source', json: '{bad', expected: 'ChatService.BuildSystemPrompt' },
      { name: 'JSON null', json: 'null', expected: 'ChatService.BuildSystemPrompt' },
      { name: 'no JSON', json: null, expected: null },
      { name: 'empty JSON', json: '', expected: null }
    ];

    for (const c of cases) {
      it(`formats ${c.name} unchanged`, () => {
        const parts = candidatePromptParts({ candidatePromptOptionsJson: c.json, candidatePromptSourceUsed: c.source ?? null });
        expect(formatCandidatePrompt(parts)).toBe(c.expected);
      });
    }

    it('returns null for no run', () => {
      expect(candidatePromptParts(null)).toBeNull();
      expect(formatCandidatePrompt(null)).toBeNull();
    });
  });
});
