import { BenchmarkRunAnswerDto, BenchmarkRunDetailDto } from '../../services/admin-benchmark.service';
import {
  REPAIR_REASON_ABORTED,
  REPAIR_REASON_BUSY,
  REPAIR_REASON_EMPTY_QUESTION,
  REPAIR_REASON_NO_LEVELS,
  REPAIR_REASON_SCORING_METHOD,
  RepairActionGate,
  isRerunInProgress,
  reassess,
  rerunFailedQuestions,
  rerunQuestion,
  rerunSynthesis,
  rescore,
  retryAssessments,
  retryClaimVerification,
  trialReassess
} from './run-repair-actions';

describe('run repair actions', () => {
  function answer(orderIndex: number, overrides: Partial<BenchmarkRunAnswerDto> = {}): BenchmarkRunAnswerDto {
    return {
      id: 100 + orderIndex, benchmarkRunId: 7, orderIndex, questionText: `Question ${orderIndex}`, difficulty: 2,
      answerText: 'An answer', status: 'Ok', assessmentStatus: 'Scored',
      accuracyLevel: 5, completenessLevel: 5, concisenessLevel: 5, readabilityLevel: 5,
      ...overrides
    } as BenchmarkRunAnswerDto;
  }

  /**
   * A run with something for every repair: a provider failure, a failed assessment and a failed
   * claim verification, finished CompletedWithErrors under the current scoring method.
   */
  function run(overrides: Partial<BenchmarkRunDetailDto> = {}): BenchmarkRunDetailDto {
    return {
      id: 7, suiteName: 'Suite', status: 'CompletedWithErrors', isAborted: false, isCurrentScoringMethod: true,
      scoringMethodVersion: 14, totalQuestionCount: 3,
      answers: [
        answer(1),
        answer(2, { status: 'ProviderError', assessmentStatus: 'Failed', accuracyLevel: null }),
        answer(3, { claimVerificationError: 'Verifier timed out' })
      ],
      ...overrides
    } as BenchmarkRunDetailDto;
  }

  const runLevel: Record<string, (r: BenchmarkRunDetailDto, busy: boolean) => RepairActionGate> = {
    rerunFailedQuestions, retryAssessments, retryClaimVerification, rerunSynthesis, rescore
  };

  const perQuestion: Record<string, (r: BenchmarkRunDetailDto, a: BenchmarkRunAnswerDto, busy: boolean) => RepairActionGate> = {
    rerunQuestion, reassess, trialReassess
  };

  function everyGate(r: BenchmarkRunDetailDto, busy: boolean): Record<string, RepairActionGate> {
    const gates: Record<string, RepairActionGate> = {};
    for (const [name, fn] of Object.entries(runLevel)) gates[name] = fn(r, busy);
    for (const [name, fn] of Object.entries(perQuestion)) gates[name] = fn(r, r.answers[0], busy);
    return gates;
  }

  describe('the normal case', () => {
    it('should list and allow every action on a run with something to repair', () => {
      for (const [name, gate] of Object.entries(everyGate(run(), false))) {
        expect(gate, name).toEqual({ visible: true, disabledReason: null });
      }
    });

    it('should list only what a clean run can repair', () => {
      const clean = run({ status: 'Completed', answers: [answer(1), answer(2)] });
      expect(rerunFailedQuestions(clean, false).visible).toBe(false);
      expect(retryAssessments(clean, false).visible).toBe(false);
      expect(retryClaimVerification(clean, false).visible).toBe(false);
      expect(rerunSynthesis(clean, false)).toEqual({ visible: true, disabledReason: null });
      expect(rescore(clean, false)).toEqual({ visible: true, disabledReason: null });
    });

    it('should carry no reason on an action that is not listed', () => {
      const clean = run({ status: 'Completed', isAborted: true, answers: [answer(1)] });
      expect(retryAssessments(clean, true)).toEqual({ visible: false, disabledReason: null });
    });

    it('should not offer Re-run failed questions while the run has not ended with failures', () => {
      expect(rerunFailedQuestions(run({ status: 'Completed' }), false).visible).toBe(false);
      expect(rerunFailedQuestions(run({ status: 'Running' }), true).visible).toBe(false);
      for (const status of ['Failed', 'Canceled', 'CompletedWithErrors', 3, 4, 5]) {
        expect(rerunFailedQuestions(run({ status }), false).visible, String(status)).toBe(true);
      }
    });

    it('should count member B\'s missing verdict as an assessment to retry on a panel run only', () => {
      const answers = [answer(1, { coAssessmentStatus: 'Failed' })];
      expect(retryAssessments(run({ answers }), false).visible).toBe(false);
      expect(retryAssessments(run({ answers, isPanelRun: true }), false).visible).toBe(true);
    });

    it('should not offer Re-run final synthesis on a run with no answers', () => {
      expect(rerunSynthesis(run({ answers: [] }), false).visible).toBe(false);
    });

    it('should refuse Re-score on a run with no dimensional level ratings', () => {
      const legacy = run({ answers: [answer(1, { accuracyLevel: null }), answer(2, { readabilityLevel: undefined })] });
      expect(rescore(legacy, false)).toEqual({ visible: true, disabledReason: REPAIR_REASON_NO_LEVELS });
    });

    it('should refuse Re-run question on an answer whose question text is empty', () => {
      const r = run();
      expect(rerunQuestion(r, answer(1, { questionText: '  ' }), false).disabledReason).toBe(REPAIR_REASON_EMPTY_QUESTION);
      expect(reassess(r, answer(1, { questionText: '' }), false).disabledReason).toBeNull();
    });

    it('should offer the trial on a panel run only where the reference reader has not graded', () => {
      const panel = run({ isPanelRun: true });
      expect(trialReassess(panel, answer(1, { secondOpinionQualityScore: 70 }), false).visible).toBe(false);
      expect(trialReassess(panel, answer(1, { secondOpinionQualityScore: null }), false).visible).toBe(true);
      expect(trialReassess(run(), answer(1, { secondOpinionQualityScore: 70 }), false).visible).toBe(true);
    });
  });

  describe('an aborted run', () => {
    const aborted = () => run({ status: 'Failed', isAborted: true });

    it('should give every listed action the aborted reason', () => {
      for (const [name, gate] of Object.entries(everyGate(aborted(), false))) {
        expect(gate, name).toEqual({ visible: true, disabledReason: REPAIR_REASON_ABORTED });
      }
    });

    it('should read a missing flag from the status, as an older server sends it', () => {
      const older = run({ status: 'Canceled', isAborted: undefined });
      expect(rescore(older, false).disabledReason).toBe(REPAIR_REASON_ABORTED);
      expect(rerunFailedQuestions(older, false).disabledReason).toBe(REPAIR_REASON_ABORTED);
    });

    it('should allow a Canceled run whose answers cover the suite', () => {
      const covered = run({ status: 'Canceled', isAborted: false });
      expect(rerunFailedQuestions(covered, false)).toEqual({ visible: true, disabledReason: null });
    });
  });

  describe('a busy run', () => {
    it('should give every listed action the busy reason', () => {
      for (const [name, gate] of Object.entries(everyGate(run(), true))) {
        expect(gate, name).toEqual({ visible: true, disabledReason: REPAIR_REASON_BUSY });
      }
    });

    it('should put busy before the scoring method', () => {
      const older = run({ isCurrentScoringMethod: false });
      expect(rerunSynthesis(older, true).disabledReason).toBe(REPAIR_REASON_BUSY);
      expect(rescore(older, true).disabledReason).toBe(REPAIR_REASON_BUSY);
    });
  });

  describe('a run scored under an older scoring method', () => {
    const older = () => run({ isCurrentScoringMethod: false, scoringMethodVersion: 9 });

    it('should refuse every action but Re-score', () => {
      for (const [name, gate] of Object.entries(everyGate(older(), false))) {
        const expected = name === 'rescore' ? null : REPAIR_REASON_SCORING_METHOD;
        expect(gate, name).toEqual({ visible: true, disabledReason: expected });
      }
    });

    it('should take a run whose server omits the flag as current', () => {
      const unknown = run({ isCurrentScoringMethod: undefined });
      expect(retryAssessments(unknown, false).disabledReason).toBeNull();
      expect(reassess(unknown, unknown.answers[0], false).disabledReason).toBeNull();
    });
  });

  describe('the retry in progress', () => {
    it('should be a re-run only while a running run carries a re-run start', () => {
      expect(isRerunInProgress(run({ status: 'Running', rerunStartedAtUtc: '2026-10-05T10:00:00Z' }))).toBe(true);
      expect(isRerunInProgress(run({ status: 'Running', rerunStartedAtUtc: null }))).toBe(false);
      expect(isRerunInProgress(run({ status: 'Completed', rerunStartedAtUtc: '2026-10-05T10:00:00Z' }))).toBe(false);
    });
  });
});
