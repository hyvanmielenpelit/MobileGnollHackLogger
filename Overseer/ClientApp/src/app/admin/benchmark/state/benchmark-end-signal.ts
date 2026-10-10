import type {
  BenchmarkBatteryRunDto,
  BenchmarkModelBatchRunDto,
  BenchmarkRunDetailDto,
  BenchmarkRunSeriesDto
} from '../../../services/admin-benchmark.service';
import { formatStatus } from '../benchmark-run-format';
import { modelBatchCurrentMember, modelBatchMemberName, modelBatchStopReasonLabel } from '../model-batch/model-batch.models';

/** The kinds of benchmark work whose end the page signals. */
export type BenchmarkEndKind = 'run' | 'series' | 'battery' | 'modelBatch';

/** The completion chime, the failure sound, or nothing. */
export type BenchmarkEndSignal = 'complete' | 'failed' | 'none';

/** Statuses that are no end at all: the work is still going. */
const LIVE_STATUSES: readonly string[] = ['Pending', 'Running', 'WaitingForCap'];

/**
 * The end rule. The failure sound marks an end that needs the operator's attention: a run that
 * failed or completed with errors; a series, battery run or model batch that stopped, failed or
 * completed with errors. A cancel signals nothing. A run's status is read through `formatStatus`.
 * A live status is no end; any other status not named here signals completion, so an unforeseen
 * status never silences an end.
 */
export function benchmarkEndSignal(kind: BenchmarkEndKind, status: string | number | null | undefined): BenchmarkEndSignal {
  const name = status == null ? '' : (kind === 'run' ? formatStatus(status) : String(status));
  if (LIVE_STATUSES.includes(name)) return 'none';
  if (kind === 'run') {
    switch (name) {
      case 'Failed':
      case 'CompletedWithErrors':
        return 'failed';
      case 'Canceled':
      case 'Cancelled':
        return 'none';
      default:
        return 'complete';
    }
  }
  switch (name) {
    case 'Stopped':
    case 'Failed':
    case 'CompletedWithErrors':
      return 'failed';
    case 'Cancelled':
    case 'Canceled':
      return 'none';
    default:
      return 'complete';
  }
}

/** Answer statuses that mean the question failed: ProviderError, Failed, Skipped, EmptyAnswer, Canceled. */
const FAILED_ANSWER_STATUSES: readonly (string | number)[] = [
  2, 'ProviderError', 3, 'Failed', 4, 'Skipped', 5, 'EmptyAnswer', 6, 'Canceled'
];

/** The run's failed questions: from its answers, else its terminal failure count. */
export function runFailedQuestionCount(run: BenchmarkRunDetailDto): number {
  const answers = run.answers ?? [];
  if (answers.length > 0) {
    return answers.filter(a => FAILED_ANSWER_STATUSES.includes(a.status as string | number)).length;
  }
  return run.terminalFailureAnswerCount ?? 0;
}

function plural(count: number, one: string, many: string): string {
  return `${count} ${count === 1 ? one : many}`;
}

/**
 * `Run #54 — <suite> — <status>`; a run completed with errors adds its failed questions:
 * `Run #54 — <suite> — Completed with errors: 3 failed questions`.
 */
export function runEndBody(run: BenchmarkRunDetailDto): string {
  const status = formatStatus(run.status);
  if (status === 'CompletedWithErrors') {
    return `Run #${run.id} — ${run.suiteName} — Completed with errors: `
      + plural(runFailedQuestionCount(run), 'failed question', 'failed questions');
  }
  return `Run #${run.id} — ${run.suiteName} — ${status}`;
}

/** The reason a series or battery run gives for a failure end, if it carries one. */
function failureReason(dto: { stopReasonText?: string | null; stopReason?: string | null; errorMessage?: string | null }): string | null {
  return dto.stopReasonText?.trim() || dto.stopReason || dto.errorMessage?.trim() || null;
}

/** `Series #N — k of n runs — <status>`, with `: <reason>` after a failure end that names one. */
export function seriesEndBody(series: BenchmarkRunSeriesDto): string {
  const base = `Series #${series.id} — ${series.completedRunCount} of ${series.requestedRunCount} runs — ${series.status}`;
  const reason = benchmarkEndSignal('series', series.status) === 'failed' ? failureReason(series) : null;
  return reason ? `${base}: ${reason}` : base;
}

/** `Battery #N — <battery name> — k of K suites — <status>`, with `: <reason>` after a failure end that names one. */
export function batteryEndBody(battery: BenchmarkBatteryRunDto): string {
  const base = `Battery #${battery.id} — ${battery.batteryName} — `
    + `${battery.completedSuiteCount} of ${battery.suiteCount} suites — ${battery.status}`;
  const reason = benchmarkEndSignal('battery', battery.status) === 'failed' ? failureReason(battery) : null;
  return reason ? `${base}: ${reason}` : base;
}

/**
 * `Model batch #N — finished: k of M models`, `Model batch #N — stopped: <reason> at <model> (k of M)`,
 * `Model batch #N — failed: <error> at <model> (k of M)` or `Model batch #N — completed with errors:
 * k of M models`.
 */
export function modelBatchEndBody(batch: BenchmarkModelBatchRunDto): string {
  const total = batch.requestedMemberCount || batch.members.length;
  const member = modelBatchCurrentMember(batch);
  const at = member ? ` at ${modelBatchMemberName(member)} (${member.orderIndex + 1} of ${total})` : '';
  switch (batch.status) {
    case 'Stopped':
      return `Model batch #${batch.id} — stopped: ${modelBatchStopReasonLabel(batch)}${at}`;
    case 'Failed': {
      const error = batch.members.find(m => m.status === 'Failed')?.errorMessage?.trim();
      return `Model batch #${batch.id} — failed${error ? `: ${error}` : ''}${at}`;
    }
    case 'CompletedWithErrors':
      return `Model batch #${batch.id} — completed with errors: ${batch.completedMemberCount} of ${total} models`;
    default:
      return `Model batch #${batch.id} — finished: ${batch.completedMemberCount} of ${total} models`;
  }
}

/**
 * `modelbatch:<id>:<status>:<stop reason>:<last progress>`: a batch that stops, is continued and
 * stops again signals each stop, while repeated polls of one end do not.
 */
export function modelBatchSignalKey(batch: BenchmarkModelBatchRunDto): string {
  return `modelbatch:${batch.id}:${batch.status}:${batch.stopReason ?? '-'}:${batch.lastProgressAtUtc ?? '-'}`;
}
