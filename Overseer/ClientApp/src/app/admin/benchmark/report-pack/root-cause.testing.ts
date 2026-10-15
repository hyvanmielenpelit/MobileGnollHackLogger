import { RecommendedRun, RootCauseControlDto, RootCauseControlsDto } from '../../../services/admin-benchmark.service';

/**
 * Run 1, a control run on suite 2: a batch of models 2 and 3, graded by 1 alone with no second reader or
 * claim verifier, about $1.20 and 35 minutes.
 */
export function recommendedRun(overrides: Partial<RecommendedRun> = {}): RecommendedRun {
  return {
    number: 1,
    kind: 'control',
    purpose: 'B',
    mark: 'Recommended',
    hypothesisIds: ['HQ1'],
    decides: 'Confirms HQ1 if the controls also miss S1-Q12; refutes it if they score above 75.',
    predictedTier: 'Comparable',
    estimatedCostUsd: 1.2,
    estimatedMinutes: 35,
    afterSystemHypotheses: false,
    targetKind: 'Suite',
    suiteId: 2,
    suiteName: 'Second Suite',
    batteryId: null,
    batteryName: null,
    runMode: 'Batch',
    testedModelConfigurationIds: [2, 3],
    modelNames: ['Model Two', 'Model Three'],
    order: 'Randomized',
    runsPerModel: 1,
    runCount: 1,
    scoringProfileId: 1,
    verboseMode: false,
    allowSourceCodeReferences: false,
    assessorModelConfigurationId: 1,
    coAssessorModelConfigurationId: null,
    secondOpinionAssessorModelConfigurationId: null,
    secondOpinionMode: null,
    claimVerifierModelConfigurationId: null,
    reportWriterModelConfigurationId: null,
    notes: [],
    ...overrides
  };
}

/** GPT-6 Luna (high), run #812, finished 2026-10-10, sharing 15 questions. */
export function rootCauseControl(overrides: Partial<RootCauseControlDto> = {}): RootCauseControlDto {
  return {
    modelLabel: 'GPT-6 Luna (high)',
    provider: 'OpenAI',
    runId: 812,
    batteryRunId: null,
    completedAtUtc: '2026-10-10T08:30:00Z',
    sharedQuestions: 15,
    ...overrides
  };
}

/** The controls lookup: none qualify, and the server proposes `recommendedRun()` as the control run. */
export function rootCauseControls(overrides: Partial<RootCauseControlsDto> = {}): RootCauseControlsDto {
  return {
    controls: [],
    controlLimit: 'Low',
    controlRun: recommendedRun(),
    ...overrides
  };
}
