import type { BenchmarkModelBatchMemberDto, BenchmarkModelBatchMemberResultDto } from '../../../services/admin-benchmark.service';
import {
  MODEL_BATCH_REPAIR_LABEL,
  modelBatchIndexWithheldText,
  modelBatchMemberFigures,
  modelBatchMemberRepairLabel,
  modelBatchMemberRepairText,
  modelBatchOwnedReasonText,
  modelBatchWithheldRunsNote
} from './model-batch.models';

function result(overrides: Partial<BenchmarkModelBatchMemberResultDto> = {}): BenchmarkModelBatchMemberResultDto {
  return {
    refutedClaims: 0, confirmedCriticalErrors: 0, failedAnswers: 0, providerErrors: 0, retries: 0,
    ...overrides
  };
}

function member(overrides: Partial<BenchmarkModelBatchMemberDto> = {}): BenchmarkModelBatchMemberDto {
  return {
    id: 100, orderIndex: 0, status: 'Completed',
    model: { configurationId: 10, displayName: 'Model 1', provider: 'OpenAI', modelId: 'model-0', thinkingLevel: 'medium', endpoint: 'official' },
    runIds: [61], runId: 61, stepCount: 1, answeredQuestionCount: 0, totalQuestionCount: 15, instrumentDriftKeys: [],
    ...overrides
  };
}

describe('model batch models', () => {
  describe('modelBatchMemberFigures', () => {
    it('lists every figure in card order', () => {
      const figures = modelBatchMemberFigures(member({
        startedAtUtc: '2026-10-10T08:00:00Z', completedAtUtc: '2026-10-10T08:13:40Z',
        result: result({
          speedIndex: 71.6, medianModelTimeMs: 17200, ttftP50Ms: 1800, candidateCostPerQuestionUsd: 0.0042,
          totalCostUsd: 1.2, refutedClaims: 2, flaggedAnswers: 3, failedAnswers: 1, providerErrors: 1204, rerunsNeeded: 2
        })
      }));
      expect(figures).toEqual([
        { key: 'speed-index', label: 'Speed Index', value: '72' },
        { key: 'duration', label: 'Duration', value: '13m 40s' },
        { key: 'median-model-time', label: 'Median model time', value: '17.2 s' },
        { key: 'ttft-p50', label: 'TTFT P50', value: '1.8 s' },
        { key: 'cost-per-question', label: 'Cost / question', value: '$0.0042' },
        { key: 'total-cost', label: 'Total cost', value: '$1.20' },
        { key: 'refuted-claims', label: 'Refuted claims', value: '2' },
        { key: 'flagged-answers', label: 'Flagged answers', value: '3' },
        { key: 'failed-answers', label: 'Failed answers', value: '1', tone: 'warning' },
        { key: 'provider-errors', label: 'Provider errors', value: '1,204' },
        { key: 'reruns', label: 'Re-runs', value: '2' }
      ]);
    });

    it('omits missing figures, keeps refuted claims and provider errors at zero, and drops flagged and failed answers at zero', () => {
      const figures = modelBatchMemberFigures(member({ result: result({ totalCostUsd: 0.42, flaggedAnswers: 0 }) }));
      expect(figures.map(f => f.label)).toEqual(['Total cost', 'Refuted claims', 'Provider errors']);
      expect(figures).toContainEqual({ key: 'refuted-claims', label: 'Refuted claims', value: '0' });
      expect(figures).toContainEqual({ key: 'provider-errors', label: 'Provider errors', value: '0' });
    });

    it('omits the duration without both timestamps', () => {
      const figures = modelBatchMemberFigures(member({ startedAtUtc: '2026-10-10T08:00:00Z', result: result() }));
      expect(figures.map(f => f.key)).toEqual(['refuted-claims', 'provider-errors']);
    });

    it('says Not recorded for provider errors, and leaves out re-runs, for a result that predates the ledger', () => {
      const figures = modelBatchMemberFigures(member({ result: result({ providerErrors: null, rerunsNeeded: null }) }));
      expect(figures).toEqual([
        { key: 'refuted-claims', label: 'Refuted claims', value: '0' },
        { key: 'provider-errors', label: 'Provider errors', value: 'Not recorded' }
      ]);
    });

    it('shows recorded re-runs at zero', () => {
      const figures = modelBatchMemberFigures(member({ result: result({ providerErrors: 0, rerunsNeeded: 0 }) }));
      expect(figures.map(f => [f.key, f.value])).toEqual([['refuted-claims', '0'], ['provider-errors', '0'], ['reruns', '0']]);
    });

    it('has at most the duration without a result', () => {
      expect(modelBatchMemberFigures(member({ result: null }))).toEqual([]);
      expect(modelBatchMemberFigures(member({
        result: null, startedAtUtc: '2026-10-10T08:00:00Z', completedAtUtc: '2026-10-10T08:00:42Z'
      }))).toEqual([{ key: 'duration', label: 'Duration', value: '42s' }]);
    });
  });

  describe('modelBatchIndexWithheldText', () => {
    it('names the failed answers and the progress to open, by target', () => {
      expect(modelBatchIndexWithheldText(member({ result: result({ failedAnswers: 2 }) }), 'Battery'))
        .toBe('No index — 2 answers failed. Open the battery progress to re-run failed questions.');
      expect(modelBatchIndexWithheldText(member({ result: result({ failedAnswers: 2 }) }), 'Suite'))
        .toBe('No index — 2 answers failed. Open the run progress to re-run failed questions.');
    });

    it('is singular for one failed answer', () => {
      expect(modelBatchIndexWithheldText(member({ result: result({ failedAnswers: 1 }) }), 'Suite'))
        .toBe('No index — 1 answer failed. Open the run progress to re-run failed questions.');
    });

    it('is a plain sentence when no answer failed, or there is no result', () => {
      expect(modelBatchIndexWithheldText(member({ result: result() }), 'Battery')).toBe('No index for this result.');
      expect(modelBatchIndexWithheldText(member({ result: null }), 'Suite')).toBe('No index for this result.');
    });
  });

  describe('modelBatchWithheldRunsNote', () => {
    it('says a group analysis index is over the withholding runs\' scored answers, in singular and plural', () => {
      expect(modelBatchWithheldRunsNote(member({
        result: result({ intelligenceIndex: 72, indexSource: 'groupAnalysis', indexWithheldRunCount: 1 })
      }))).toBe('1 run withholds its own index (a question failed at the provider); this index is over its scored answers.');
      expect(modelBatchWithheldRunsNote(member({
        result: result({ intelligenceIndex: 72, indexSource: 'groupAnalysis', indexWithheldRunCount: 2 })
      }))).toBe('2 runs withhold their own index (a question failed at the provider); this index is over their scored answers.');
    });

    it('says a run mean index is the mean of the other runs, in singular and plural', () => {
      expect(modelBatchWithheldRunsNote(member({
        result: result({ intelligenceIndex: 72, indexSource: 'runMean', indexWithheldRunCount: 1 })
      }))).toBe('1 run withholds its own index (a question failed at the provider); this index is the mean of the other runs.');
      expect(modelBatchWithheldRunsNote(member({
        result: result({ intelligenceIndex: 72, indexSource: 'runMean', indexWithheldRunCount: 2 })
      }))).toBe('2 runs withhold their own index (a question failed at the provider); this index is the mean of the other runs.');
    });

    it('is null at zero or without a count, without an index, and for any other source', () => {
      expect(modelBatchWithheldRunsNote(member({
        result: result({ intelligenceIndex: 72, indexSource: 'groupAnalysis', indexWithheldRunCount: 0 })
      }))).toBeNull();
      expect(modelBatchWithheldRunsNote(member({
        result: result({ intelligenceIndex: 72, indexSource: 'groupAnalysis' })
      }))).toBeNull();
      expect(modelBatchWithheldRunsNote(member({
        result: result({ intelligenceIndex: null, indexSource: 'runMean', indexWithheldRunCount: 2 })
      }))).toBeNull();
      expect(modelBatchWithheldRunsNote(member({ result: null }))).toBeNull();
      expect(modelBatchWithheldRunsNote(member({
        result: result({ intelligenceIndex: 72, indexSource: 'run', indexWithheldRunCount: 1 })
      }))).toBeNull();
    });
  });

  describe('repair', () => {
    it('labels a re-run of failed questions and any other repair', () => {
      expect(modelBatchMemberRepairLabel(member())).toBeNull();
      expect(modelBatchMemberRepairLabel(member({ repairingRunId: 61, rerunStartedAtUtc: '2026-10-10T09:00:00Z' })))
        .toBe('Re-run in progress');
      expect(modelBatchMemberRepairLabel(member({ repairingRunId: 61 }))).toBe(MODEL_BATCH_REPAIR_LABEL);
      expect(MODEL_BATCH_REPAIR_LABEL).toBe('Repair in progress');
    });

    it('counts the questions a re-run covers', () => {
      const rerun = { repairingRunId: 61, rerunStartedAtUtc: '2026-10-10T09:00:00Z' };
      expect(modelBatchMemberRepairText(member({ ...rerun, rerunQuestionCount: 2 }))).toBe('Re-running 2 failed questions');
      expect(modelBatchMemberRepairText(member({ ...rerun, rerunQuestionCount: 1 }))).toBe('Re-running 1 failed question');
      expect(modelBatchMemberRepairText(member({ ...rerun, rerunQuestionCount: null }))).toBe('Re-running failed questions');
      expect(modelBatchMemberRepairText(member({ repairingRunId: 61 }))).toBeNull();
      expect(modelBatchMemberRepairText(member({ rerunStartedAtUtc: '2026-10-10T09:00:00Z', rerunQuestionCount: 2 }))).toBeNull();
    });
  });

  describe('modelBatchOwnedReasonText', () => {
    it('names the batch and the child it controls', () => {
      expect(modelBatchOwnedReasonText(7, 'battery'))
        .toBe('Model batch #7 controls this battery run. Cancel, continue or re-run it from the batch.');
      expect(modelBatchOwnedReasonText(7, 'series'))
        .toBe('Model batch #7 controls this series. Cancel, continue or re-run it from the batch.');
    });
  });
});
