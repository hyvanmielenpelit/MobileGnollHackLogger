import { BenchmarkRunStage } from './benchmark.models';

/**
 * The run progress rail's stage names, by rail index. Items 1 to 3 are the run's scoring stages;
 * item 4 exists only when the run names a report writer.
 */
export const RUN_STAGE_NAMES: Readonly<Record<1 | 2 | 3 | 4, string>> = {
  1: 'Answering and grading',
  2: 'Follow-up grading passes',
  3: 'Synthesis and scoring',
  4: 'Writing reports'
};

/** A run's own stage count: three scoring stages, four when a report writer follows them. */
export const RUN_STAGE_COUNT_WITHOUT_REPORTS = 3;

/**
 * The client stage for the server's `BenchmarkRunStage` name, or null for a missing or unknown one,
 * where the caller falls back to deriving the stage from the run's answers.
 */
export function runStageFromServer(stage: string | null | undefined): BenchmarkRunStage | null {
  switch (stage) {
    case 'Answering': return 'answering';
    case 'Verifying': return 'verifying';
    case 'SecondOpinion': return 'secondopinion';
    case 'Synthesizing': return 'finalizing';
    case 'Terminal': return 'terminal';
    default: return null;
  }
}

/**
 * The rail item a stage sits on: 0 for a terminal run, which highlights nothing. Both follow-up
 * passes map to item 2 so the rail cannot move backwards when the server revisits `Verifying`.
 */
export function runRailIndexOf(stage: BenchmarkRunStage): 0 | 1 | 2 | 3 {
  switch (stage) {
    case 'answering': return 1;
    case 'verifying':
    case 'secondopinion': return 2;
    case 'finalizing': return 3;
    default: return 0;
  }
}

/**
 * A running run's stage as *Stage 1 of 3 — Answering and grading*, from the server's stage name.
 * Without one the run reads *Starting* until its first answer, then *Answering and grading*.
 * Null for a terminal stage.
 */
export function runStageCaption(
  serverStage: string | null | undefined,
  answeredCount: number,
  stageCount: number = RUN_STAGE_COUNT_WITHOUT_REPORTS
): string | null {
  const stage = runStageFromServer(serverStage);
  if (stage == null && answeredCount <= 0) return 'Starting';
  const index = runRailIndexOf(stage ?? 'answering');
  if (index === 0) return null;
  return `Stage ${index} of ${stageCount} — ${RUN_STAGE_NAMES[index]}`;
}
