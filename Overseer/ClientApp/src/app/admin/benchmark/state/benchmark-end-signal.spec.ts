import type {
  BenchmarkBatteryRunDto,
  BenchmarkModelBatchMemberDto,
  BenchmarkModelBatchRunDto,
  BenchmarkRunDetailDto,
  BenchmarkRunSeriesDto
} from '../../../services/admin-benchmark.service';
import {
  BenchmarkEndKind,
  BenchmarkEndSignal,
  batteryEndBody,
  benchmarkEndSignal,
  modelBatchEndBody,
  modelBatchSignalKey,
  runEndBody,
  seriesEndBody
} from './benchmark-end-signal';

function batch(overrides: Partial<BenchmarkModelBatchRunDto> = {}): BenchmarkModelBatchRunDto {
  const member = (orderIndex: number, displayName: string, status: BenchmarkModelBatchMemberDto['status']): BenchmarkModelBatchMemberDto => ({
    id: orderIndex + 1, orderIndex, status, runIds: [], stepCount: 1, answeredQuestionCount: 0, totalQuestionCount: 10,
    instrumentDriftKeys: [],
    model: { configurationId: orderIndex + 10, displayName, provider: 'OpenAI', modelId: displayName.toLowerCase(), endpoint: 'official' }
  });
  return {
    id: 12, status: 'Running', targetKind: 'Suite', suiteNames: ['Core'], runsPerModel: 1, order: 'Randomized',
    allowCapWait: false, createdAtUtc: '2026-10-10T08:00:00Z',
    members: [member(0, 'Alpha', 'Completed'), member(1, 'Beta', 'Failed'), member(2, 'Gamma', 'Pending')],
    currentMemberIndex: 1, requestedMemberCount: 3, completedMemberCount: 1, failedMemberCount: 1, skippedMemberCount: 0,
    acknowledgedFindings: [], adviceAtStart: [], instrumentChangeAcknowledged: false, isDriving: false,
    resumable: false, resumeOptions: [], stalled: false, stallMinutes: 15,
    ...overrides
  };
}

describe('benchmarkEndSignal', () => {
  const table: [BenchmarkEndKind, string | number, BenchmarkEndSignal][] = [
    // Single run
    ['run', 'Completed', 'complete'],
    ['run', 'CompletedWithLimits', 'complete'],
    ['run', 'Failed', 'failed'],
    ['run', 'CompletedWithErrors', 'failed'],
    ['run', 'Canceled', 'none'],
    ['run', 2, 'complete'],
    ['run', 6, 'complete'],
    ['run', 4, 'failed'],
    ['run', 3, 'failed'],
    ['run', 5, 'none'],
    // Series
    ['series', 'Completed', 'complete'],
    ['series', 'Stopped', 'failed'],
    ['series', 'Failed', 'failed'],
    ['series', 'CompletedWithErrors', 'failed'],
    ['series', 'Cancelled', 'none'],
    // Battery run
    ['battery', 'Completed', 'complete'],
    ['battery', 'Stopped', 'failed'],
    ['battery', 'Failed', 'failed'],
    ['battery', 'CompletedWithErrors', 'failed'],
    ['battery', 'Cancelled', 'none'],
    // Model batch
    ['modelBatch', 'Completed', 'complete'],
    ['modelBatch', 'Stopped', 'failed'],
    ['modelBatch', 'Failed', 'failed'],
    ['modelBatch', 'CompletedWithErrors', 'failed'],
    ['modelBatch', 'Cancelled', 'none']
  ];

  for (const [kind, status, expected] of table) {
    it(`${kind} ${status} → ${expected}`, () => {
      expect(benchmarkEndSignal(kind, status)).toBe(expected);
    });
  }

  it('signals completion for an unknown terminal status, so an unforeseen end is never silent', () => {
    for (const kind of ['run', 'series', 'battery', 'modelBatch'] as BenchmarkEndKind[]) {
      expect(benchmarkEndSignal(kind, 'SomethingNew')).toBe('complete');
    }
  });

  it('is no end while the work is live', () => {
    for (const kind of ['series', 'battery', 'modelBatch'] as BenchmarkEndKind[]) {
      for (const status of ['Pending', 'Running', 'WaitingForCap']) {
        expect(benchmarkEndSignal(kind, status)).toBe('none');
      }
    }
    expect(benchmarkEndSignal('run', 1)).toBe('none');
  });
});

describe('end notification bodies', () => {
  it('keeps a run\'s body, and counts the failed questions of one completed with errors', () => {
    const run = { id: 54, suiteName: 'Core', status: 'Failed', answers: [] } as unknown as BenchmarkRunDetailDto;
    expect(runEndBody(run)).toBe('Run #54 — Core — Failed');

    const partial = {
      id: 54, suiteName: 'Core', status: 3,
      answers: [{ status: 'Ok' }, { status: 'ProviderError' }, { status: 4 }, { status: 1 }]
    } as unknown as BenchmarkRunDetailDto;
    expect(runEndBody(partial)).toBe('Run #54 — Core — Completed with errors: 2 failed questions');
  });

  it('names a stopped series\' and battery run\'s reason, and leaves a completed one as it was', () => {
    const series = { id: 8, completedRunCount: 2, requestedRunCount: 4, status: 'Stopped', stopReasonText: 'A member run failed' } as BenchmarkRunSeriesDto;
    expect(seriesEndBody(series)).toBe('Series #8 — 2 of 4 runs — Stopped: A member run failed');
    expect(seriesEndBody({ ...series, status: 'Completed', completedRunCount: 4 })).toBe('Series #8 — 4 of 4 runs — Completed');

    const battery = {
      id: 9, batteryName: 'Core Battery', completedSuiteCount: 1, suiteCount: 2, status: 'Stopped', stopReasonText: 'A member run failed'
    } as BenchmarkBatteryRunDto;
    expect(batteryEndBody(battery)).toBe('Battery #9 — Core Battery — 1 of 2 suites — Stopped: A member run failed');
    expect(batteryEndBody({ ...battery, status: 'Completed', completedSuiteCount: 2 }))
      .toBe('Battery #9 — Core Battery — 2 of 2 suites — Completed');
  });

  it('words a model batch\'s stop with its reason, model and position, and its completion with the count', () => {
    expect(modelBatchEndBody(batch({ status: 'Stopped', stopReason: 'MemberStopped' })))
      .toBe('Model batch #12 — stopped: a model\'s run stopped at Beta (2 of 3)');
    expect(modelBatchEndBody(batch({ status: 'Stopped', stopReason: 'RestartReconciled' })))
      .toBe('Model batch #12 — stopped: the server restarted at Beta (2 of 3)');
    expect(modelBatchEndBody(batch({ status: 'Completed', completedMemberCount: 3, currentMemberIndex: null })))
      .toBe('Model batch #12 — finished: 3 of 3 models');
    expect(modelBatchEndBody(batch({ status: 'CompletedWithErrors', completedMemberCount: 2, currentMemberIndex: null })))
      .toBe('Model batch #12 — completed with errors: 2 of 3 models');
  });

  it('keys a model batch by its status, stop reason and last progress', () => {
    expect(modelBatchSignalKey(batch({ status: 'Stopped', stopReason: 'InstrumentChanged', lastProgressAtUtc: '2026-10-10T09:00:00Z' })))
      .toBe('modelbatch:12:Stopped:InstrumentChanged:2026-10-10T09:00:00Z');
    expect(modelBatchSignalKey(batch({ status: 'Completed' }))).toBe('modelbatch:12:Completed:-:-');
  });
});
