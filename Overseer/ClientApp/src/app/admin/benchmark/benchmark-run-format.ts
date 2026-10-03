import { DecimalPipe } from '@angular/common';
import {
  BenchmarkRunSummaryDto,
  BenchmarkRunDetailDto,
  BenchmarkRunReportDocumentsStatus
} from '../../services/admin-benchmark.service';

// Pure functions over run summaries and details, shared by Run History, the run report and the state services.

/**
 * U3. A dollar amount at four decimals fixed, so a sub-cent cost like $0.0007 renders
 * as $0.0007, not $0.00, and zero renders as $0.0000.
 */
export function formatCostAmount(amount: number | null | undefined): string {
  if (amount == null || !Number.isFinite(amount)) return '-';
  const numPipe = new DecimalPipe('en-US');
  const digits = '1.4-4';
  return `$${numPipe.transform(amount, digits)}`;
}

export function formatRunEstimatedCost(run: BenchmarkRunSummaryDto | BenchmarkRunDetailDto): string {
  return formatCostAmount(run.estimatedCost);
}

/**
 * The prompt-option keys whose values differ between two runs, or null when the two records cannot
 * be compared at all — either run missing its options, or either one unparseable. An empty array
 * means the comparison was made and the options match.
 */
export function changedPromptOptionKeys(run: BenchmarkRunSummaryDto, previous: BenchmarkRunSummaryDto): string[] | null {
  const current = parsePromptOptions(run.candidatePromptOptionsJson);
  const older = parsePromptOptions(previous.candidatePromptOptionsJson);
  if (!current || !older) return null;

  const keys = Array.from(new Set([...Object.keys(current), ...Object.keys(older)])).sort();
  return keys.filter(key => JSON.stringify(current[key] ?? null) !== JSON.stringify(older[key] ?? null));
}

export function parsePromptOptions(json: string | null | undefined): Record<string, unknown> | null {
  if (!json) return null;
  try {
    const parsed = JSON.parse(json);
    if (!parsed || typeof parsed !== 'object' || Array.isArray(parsed)) return null;
    return parsed as Record<string, unknown>;
  } catch {
    return null;
  }
}

/** A refusal's text: a plain-string body, a body's `message`, else the fallback. */
export function refusalText(err: any, fallback: string): string {
  if (typeof err?.error === 'string' && err.error) return err.error;
  return err?.error?.message || fallback;
}

/** The run's report status as its enum value, whether the server sent the number or the name. */
export function reportDocumentsStatusOf(run: BenchmarkRunDetailDto | null | undefined): BenchmarkRunReportDocumentsStatus {
  const status: unknown = run?.reportDocumentsStatus;
  if (typeof status === 'number') {
    return status;
  }
  if (typeof status === 'string') {
    const value = (BenchmarkRunReportDocumentsStatus as unknown as Record<string, unknown>)[status];
    if (typeof value === 'number') {
      return value;
    }
  }
  return BenchmarkRunReportDocumentsStatus.NotRequested;
}

// --- Formatting Helpers ---
export function formatStatus(status: string | number): string {
  if (status === 1 || status === 'Running') return 'Running';
  if (status === 2 || status === 'Completed') return 'Completed';
  if (status === 3 || status === 'CompletedWithErrors') return 'CompletedWithErrors';
  if (status === 4 || status === 'Failed') return 'Failed';
  if (status === 5 || status === 'Canceled') return 'Canceled';
  if (status === 6 || status === 'CompletedWithLimits') return 'CompletedWithLimits';
  return String(status);
}

export function formatStatusLabel(status: string | number): string {
  const s = formatStatus(status);
  if (s === 'CompletedWithLimits') return 'Completed with limits';
  if (s === 'CompletedWithErrors') return 'Completed with errors';
  return s;
}

/** `formatStatus` never emits spaces, but the strip is kept in case that changes. */
export function statusBadgeClass(status: string | number): string {
  return 'badge-status-' + formatStatus(status).toLowerCase().replace(/\s+/g, '');
}

export function getScoreBadgeClass(score: number | null | undefined): string {
  if (score == null) return 'badge-score-na';
  if (score >= 80) return 'badge-score-high';
  if (score >= 50) return 'badge-score-mid';
  return 'badge-score-low';
}

/**
 * A run that stopped before finishing its suite. The server decides it
 * (BenchmarkRunFinalizer.IsAbortedRun, which tests answer-row coverage as well as the status);
 * the status fallback covers rows from a server that predates the flag.
 */
export function isAbortedRun(run: BenchmarkRunSummaryDto | BenchmarkRunDetailDto): boolean {
  if (run.isAborted != null) return run.isAborted;
  const status = formatStatus(run.status);
  return status === 'Canceled' || status === 'Failed';
}

/**
 * What the Duration column shows. A run that reached the end is measured by the time its answers
 * took; one that stopped early by the wall clock up to the stop, because the questions that never
 * ran are part of what was cancelled. Runs stopped before either figure was recorded fall back to
 * the two timestamps, which are always present on a terminal run.
 */
export function runDurationMs(run: BenchmarkRunSummaryDto): number {
  if (isAbortedRun(run)) {
    return run.totalDurationMs || elapsedBetweenTimestamps(run);
  }
  return run.totalAnswerDurationMs || run.totalDurationMs || elapsedBetweenTimestamps(run);
}

export function elapsedBetweenTimestamps(run: BenchmarkRunSummaryDto | BenchmarkRunDetailDto): number {
  if (!run.completedAtUtc) return 0;
  return Math.max(0, new Date(run.completedAtUtc).getTime() - new Date(run.startedAtUtc).getTime());
}

/**
 * How many questions a terminal run actually answered, when that is fewer than the suite holds.
 * Null while a run is still going, and null for a run that answered everything.
 *
 * `answeredQuestionCount` counts answers whose status is Ok, matching the report's "Answered
 * Questions" line: an answer that came back empty is not an answered question, even though scoring
 * method 10 scores it 0. The status already says a run had errors; this says how many, which is
 * what separates an index of 74 over 16 of 18 questions from 74 over 18.
 */
export function answerShortfallOf(run: BenchmarkRunSummaryDto): { answered: number; total: number } | null {
  if (formatStatus(run.status) === 'Running') return null;
  const total = run.totalQuestionCount ?? 0;
  const answered = run.answeredQuestionCount ?? 0;
  if (total <= 0 || answered >= total) return null;
  return { answered, total };
}

export function formatDuration(ms: number): string {
  if (!ms) return '0s';
  const totalSecs = Math.floor(ms / 1000);
  const mins = Math.floor(totalSecs / 60);
  const secs = totalSecs % 60;
  if (mins > 0) {
    return `${mins}m ${secs}s`;
  }
  return `${(ms / 1000).toFixed(1)}s`;
}

export function formatElapsed(ms: number): string {
  if (!ms || ms < 0) return '0s';
  const totalSecs = Math.floor(ms / 1000);
  const hours = Math.floor(totalSecs / 3600);
  const mins = Math.floor((totalSecs % 3600) / 60);
  const secs = totalSecs % 60;
  const pad = (n: number) => (n < 10 ? '0' + n : '' + n);
  if (hours > 0) {
    return `${hours}h ${pad(mins)}m ${pad(secs)}s`;
  }
  if (mins > 0) {
    return `${mins}m ${pad(secs)}s`;
  }
  return `${secs}s`;
}

// rather than a block — but it is shown before the run, not explained after it.
export const DELIBERATING_THINKING_LEVELS: readonly string[] = ['high', 'max'];

export const INTERACTIVE_SPEED_TARGET_MAX_MS = 30000;

export const MISSING_BOARD_QUOTE_LIST_CAP = 20;

/**
 * H2. Whether this run's instrument differs from the next older completed run of the same suite, and
 * which of the five hashes moved. historyRuns is the loaded history, newest first, in the server's order.
 *
 * All five are compared and every one that moved is named: the candidate prompt, the tool guides, the
 * knowledge base, the GnollHack wiki and the GnollHack source. The first three are the comparability
 * keys a reproduction turns on; the two corpus HEADs are provenance, and a badge that names one says
 * the answers were drawn from a different corpus rather than that the instrument itself moved. The
 * report has stated that rule for some time, but the run list could not support it, so the check was
 * done by hand — and run 13's T8 verification is exactly the case where getting it wrong misattributes
 * a change. Compared client-side over the already-loaded history; no new endpoint.
 *
 * Returns null when there is no older run of the same suite, or when either run is missing a hash: "not
 * recorded" is not "unchanged", and badging it as a change would be a claim the data cannot support.
 *
 * The candidate hash covers the prompt as built, so a run option that changes the prompt text —
 * verboseMode is the usual one — moves it without anything in the instrument having moved. Two runs
 * with different prompt options are not a candidate reproduction on any axis, so the option
 * difference is reported as itself rather than as instrument drift; only runs whose options match
 * can say anything about whether the instrument held still. Where the options cannot be compared —
 * either run missing them, or either one unparseable — the hashes are the only claim available.
 */
export function instrumentChangeOf(run: BenchmarkRunSummaryDto, historyRuns: readonly BenchmarkRunSummaryDto[]):
  { kind: 'instrument' | 'options'; description: string; comparedToRunId: number } | null {
  const index = historyRuns.indexOf(run);
  if (index < 0) return null;

  const previous = historyRuns
    .slice(index + 1)
    .find(r => r.benchmarkSuiteId === run.benchmarkSuiteId && formatStatus(r.status) !== 'Running');
  if (!previous) return null;

  const changedOptions = changedPromptOptionKeys(run, previous);
  if (changedOptions && changedOptions.length > 0) {
    return {
      kind: 'options',
      comparedToRunId: previous.id,
      description: `Run options differ from run #${previous.id}: ${changedOptions.join(', ')}. ` +
        'The prompt is built from these, so the candidate hash moves with them. The two runs are not a reproduction.'
    };
  }

  if (!run.candidateSystemPromptSha256 && !run.toolGuidesSha256 && !run.knowledgeBaseHeadSha &&
      !run.wikiHeadSha && !run.sourceCodeHeadSha) {
    return null;
  }

  const moved: string[] = [];
  if (run.candidateSystemPromptSha256 && previous.candidateSystemPromptSha256 &&
      run.candidateSystemPromptSha256 !== previous.candidateSystemPromptSha256) {
    moved.push('candidate system prompt');
  }
  if (run.toolGuidesSha256 && previous.toolGuidesSha256 &&
      run.toolGuidesSha256 !== previous.toolGuidesSha256) {
    moved.push('tool guides');
  }
  if (run.knowledgeBaseHeadSha && previous.knowledgeBaseHeadSha &&
      run.knowledgeBaseHeadSha !== previous.knowledgeBaseHeadSha) {
    moved.push('knowledge base');
  }
  if (run.wikiHeadSha && previous.wikiHeadSha &&
      run.wikiHeadSha !== previous.wikiHeadSha) {
    moved.push('GnollHack wiki');
  }
  if (run.sourceCodeHeadSha && previous.sourceCodeHeadSha &&
      run.sourceCodeHeadSha !== previous.sourceCodeHeadSha) {
    moved.push('GnollHack source');
  }

  if (moved.length === 0) return null;

  return {
    kind: 'instrument',
    comparedToRunId: previous.id,
    description: `Changed since run #${previous.id}: ${moved.join(', ')}. The two runs are a controlled pair, not a reproduction.`
  };
}
