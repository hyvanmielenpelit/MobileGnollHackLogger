import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { MultiRunProgressDialogComponent } from './multi-run-progress-dialog.component';
import { AdminBenchmarkService } from '../../../services/admin-benchmark.service';
import { SystemService } from '../../../services/system.service';
import {
  BenchmarkRunSeriesDto,
  BenchmarkRunSeriesMemberDto,
  BenchmarkRunSeriesStatus,
  BenchmarkGroupAnalysisDto,
  BenchmarkComparabilityResultDto
} from '../../../services/admin-benchmark.service';

/**
 * The multi-run progress dialog.
 *
 * Two properties carry most of the weight here. The first is that a series between runs is shown
 * as a **named state** rather than a spinner: `WaitingForCap` and `Stopped` are the two places a
 * multi-run operation parks for a long time, and a spinner there is indistinguishable from a hang.
 * The second is that both diagnostics captures are self-explaining — a refused resume has to be
 * readable from the pasted text alone, and a tier verdict has to carry its reasons, or the capture
 * is not worth taking.
 */
describe('MultiRunProgressDialogComponent', () => {
  let component: MultiRunProgressDialogComponent;
  let fixture: ComponentFixture<MultiRunProgressDialogComponent>;
  let serviceMock: jasmine.SpyObj<AdminBenchmarkService>;
  let systemServiceMock: jasmine.SpyObj<SystemService>;

  function buildMember(overrides: Partial<BenchmarkRunSeriesMemberDto> = {}): BenchmarkRunSeriesMemberDto {
    return {
      index: 1,
      runId: 41,
      status: 'Completed',
      startedAtUtc: '2026-09-06T08:00:00Z',
      completedAtUtc: '2026-09-06T08:30:00Z',
      qualityIndex: 71,
      speedIndex: 55,
      estimatedCost: 2.53,
      durationMs: 1_800_000,
      answeredQuestionCount: 18,
      totalQuestionCount: 18,
      shortFingerprint: 'e9b3e9a7',
      ...overrides
    };
  }

  function buildSeries(overrides: Partial<BenchmarkRunSeriesDto> = {}): BenchmarkRunSeriesDto {
    return {
      id: 3,
      benchmarkSuiteId: 5,
      suiteName: 'GnollHack Player Assistance Benchmark Suite',
      requestedRunCount: 3,
      completedRunCount: 1,
      failedRunCount: 0,
      status: 'Running',
      stopReason: null,
      stopReasonText: null,
      allowCapWait: true,
      resumable: false,
      startedAtUtc: '2026-09-06T08:00:00Z',
      completedAtUtc: null,
      errorMessage: null,
      firstMemberCandidateSystemPromptSha256: 'aaaa1111bbbb2222cccc3333dddd4444',
      firstMemberToolGuidesSha256: 'eeee5555ffff6666aaaa7777bbbb8888',
      firstMemberKnowledgeBaseHeadSha: 'cccc9999dddd0000eeee1111ffff2222',
      currentCandidateSystemPromptSha256: 'aaaa1111bbbb2222cccc3333dddd4444',
      currentToolGuidesSha256: 'eeee5555ffff6666aaaa7777bbbb8888',
      currentKnowledgeBaseHeadSha: 'cccc9999dddd0000eeee1111ffff2222',
      changedInstrumentHashes: [],
      instrumentChangeAcknowledged: false,
      autoCreatedGroupId: null,
      autoCreatedGroupTier: null,
      members: [buildMember(), buildMember({ index: 2, runId: 42, status: 'Running', completedAtUtc: null, answeredQuestionCount: 7 })],
      ...overrides
    };
  }

  function buildComparability(): BenchmarkComparabilityResultDto {
    return {
      tier: 'Replicate',
      tierLabel: 'Tier A — Replicate',
      poolingPermitted: true,
      speedAggregatesDegraded: false,
      costAggregatesDegraded: false,
      explanation: 'Every comparability key matches across the three members.',
      comparabilityKeyHash: 'a1b2c3d4e5f6',
      matchedKeys: ['BenchmarkSuiteId', 'CandidateSystemPromptSha256', 'ScoringMethodVersion'],
      differences: [],
      runIds: [41, 42, 43]
    } as BenchmarkComparabilityResultDto;
  }

  /** Only the fields the group capture reads; the analysis DTO itself is exercised elsewhere. */
  function buildAnalysis(): BenchmarkGroupAnalysisDto {
    return {
      id: 11,
      groupId: 9,
      groupName: 'Suite 5 · GPT-5.6 · baseline · R=3',
      computedAtUtc: '2026-09-06T11:00:00Z',
      runCount: 3,
      memberRunIds: [41, 42, 43],
      tier: 'Replicate',
      tierLabel: 'Tier A — Replicate',
      harnessVersion: '8',
      scoringMethodVersion: 8,
      stale: false,
      comparedWithGroupId: null,
      comparedWithGroupName: null,
      result: {
        suiteId: 5,
        suiteName: 'GnollHack Player Assistance Benchmark Suite',
        runIds: [41, 42, 43],
        runCount: 3,
        itemCount: 18,
        unansweredItemCount: 0,
        items: [],
        index: {
          pointEstimate: 71.3,
          weightedMeanOfItemMeans: 71.3,
          identityHolds: true,
          perRunIndices: [71, 73, 70],
          reproducibilityStandardDeviation: 1.53,
          reproducibilityStandardError: 0.88,
          reproducibilityCriticalValue: 4.303,
          reproducibilityHalfWidth: 3.79,
          reproducibilityAvailable: true,
          itemSamplingStandardError: 4.2,
          itemSamplingCriticalValue: 1.96,
          itemSamplingHalfWidth: 8.23,
          combinedHalfWidth: 9.06,
          combinedLower: 62.24,
          combinedUpper: 80.36
        },
        speed: { degraded: false } as any,
        cost: { degraded: false } as any,
        comparison: null,
        varianceDecompositionCaveat: 'Run-to-run variance mixes candidate and grader stochasticity.'
      } as any,
      ...{}
    } as BenchmarkGroupAnalysisDto;
  }

  /** Opens the dialog on a series, which is what starts polling and focuses the heading. */
  function open(series: BenchmarkRunSeriesDto): void {
    serviceMock.getRunSeries.and.returnValue(of(series));
    fixture.componentRef.setInput('seriesId', series.id);
    fixture.componentRef.setInput('visible', true);
    fixture.detectChanges();
  }

  function text(selector: string): string {
    const element = fixture.nativeElement.querySelector(selector) as HTMLElement | null;
    return (element?.textContent || '').replace(/\s+/g, ' ').trim();
  }

  function footerText(): string {
    return text('.dialog-footer');
  }

  beforeEach(async () => {
    serviceMock = jasmine.createSpyObj('AdminBenchmarkService', [
      'getRunSeries', 'getRunLimits', 'getRun', 'getRunGroupAnalysis', 'analyseRunGroup',
      'previewRunGroupTier', 'cancelRunSeries', 'resumeRunSeries', 'getGroupReportUrl'
    ]);
    systemServiceMock = jasmine.createSpyObj('SystemService', ['getVersion']);

    systemServiceMock.getVersion.and.returnValue(of('1.4.2'));
    serviceMock.getRunSeries.and.returnValue(of(buildSeries()));
    serviceMock.getRunLimits.and.returnValue(of({
      maxRunsPerHour: 4,
      maxRunsPerDay: 20,
      runsInLastHour: 1,
      runsInLast24Hours: 6,
      remainingDailyHeadroom: 14,
      maxRunCountPerSeries: 20
    }));
    serviceMock.getRun.and.returnValue(of({ id: 41, answers: [] } as any));
    serviceMock.getRunGroupAnalysis.and.returnValue(of(null));
    serviceMock.analyseRunGroup.and.returnValue(of(buildAnalysis()));
    serviceMock.previewRunGroupTier.and.returnValue(of({
      accepted: true, group: null, comparability: buildComparability()
    } as any));
    serviceMock.cancelRunSeries.and.returnValue(of(void 0 as any));
    serviceMock.resumeRunSeries.and.returnValue(of(void 0 as any));
    serviceMock.getGroupReportUrl.and.callFake((id: number) => `/api/admin/benchmark/runs/groups/${id}/report`);

    await TestBed.configureTestingModule({
      imports: [MultiRunProgressDialogComponent],
      providers: [
        { provide: AdminBenchmarkService, useValue: serviceMock },
        { provide: SystemService, useValue: systemServiceMock }
      ]
    }).compileComponents();

    fixture = TestBed.createComponent(MultiRunProgressDialogComponent);
    component = fixture.componentInstance;
  });

  afterEach(() => {
    // The dialog polls on an interval and ticks the elapsed clock; without this the timers outlive
    // the spec and the next one starts against a running poll.
    fixture.destroy();
  });

  // --- Stages -------------------------------------------------------------------------------

  it('should report the launching stage before any member exists', () => {
    open(buildSeries({ status: 'Pending', members: [], completedRunCount: 0 }));

    expect(component.stage).toBe('launching');
    expect(component.stageIndex).toBe(0);
    expect(text('.progress-status')).toContain('Launching');
  });

  it('should name the running member and its position in the series', () => {
    open(buildSeries());

    expect(component.stage).toBe('running');
    expect(component.stageIndex).toBe(1);
    expect(component.currentMemberIndex).toBe(2);
    expect(component.stageLabel).toBe('Running run 2 of 3');
    expect(text('.progress-status')).toContain('Running run 2 of 3');
  });

  it('should advance to analysing while a group exists with no analysis yet', () => {
    open(buildSeries({
      status: 'Completed', completedRunCount: 3, autoCreatedGroupId: 9,
      completedAtUtc: '2026-09-06T10:30:00Z',
      members: [buildMember(), buildMember({ index: 2, runId: 42 }), buildMember({ index: 3, runId: 43 })]
    }));

    // getRunGroupAnalysis returns null, so the dialog asks for one; analyseRunGroup answers
    // synchronously here, which lands the stage on complete.
    expect(serviceMock.analyseRunGroup).toHaveBeenCalledWith(9);
    expect(component.groupAnalysis).toBeTruthy();
    expect(component.stage).toBe('complete');
  });

  it('should stay on analysing when the analysis has been requested but not returned', () => {
    serviceMock.getRunGroupAnalysis.and.returnValue(of(null));
    serviceMock.analyseRunGroup.and.returnValue(of(null as any));
    open(buildSeries({
      status: 'Completed', completedRunCount: 3, autoCreatedGroupId: 9,
      members: [buildMember(), buildMember({ index: 2, runId: 42 })]
    }));

    expect(component.analysisPending).toBeTrue();
    expect(component.stage).toBe('analysing');
    expect(component.stageIndex).toBe(2);
  });

  // --- Named pauses, not spinners ------------------------------------------------------------

  it('should render WaitingForCap as a named state with an explanation', () => {
    open(buildSeries({ status: 'WaitingForCap' }));

    expect(component.stage).toBe('waitingForCap');
    expect(component.isNamedPause).toBeTrue();
    // Still on step 2: the series is between runs, not past them.
    expect(component.stageIndex).toBe(1);
    expect(text('.series-state-name')).toContain('Waiting for run cap');
    expect(component.stageLabel).toContain('Waiting for the run cap');
    expect(text('.series-state-detail')).toContain('rolling 24-hour window');
  });

  it('should render Stopped as a named state carrying the stop reason in words', () => {
    open(buildSeries({
      status: 'Stopped', stopReason: 'MemberFailed',
      stopReasonText: 'Run 42 failed during assessment.', resumable: true
    }));

    expect(component.stage).toBe('stopped');
    expect(component.isNamedPause).toBeTrue();
    expect(text('.series-state-name')).toContain('Stopped');
    expect(text('.series-state-detail')).toContain('Run 42 failed during assessment.');
    expect(text('.series-state-detail')).toContain('Completed runs are kept');
  });

  // --- Footer -------------------------------------------------------------------------------

  it('should offer Run in Background and Cancel Series while the series is live', () => {
    open(buildSeries());

    expect(component.seriesIsLive).toBeTrue();
    expect(footerText()).toContain('Run in Background');
    expect(footerText()).toContain('Cancel Series');
    expect(footerText()).not.toContain('Continue');
  });

  it('should offer Continue, labelled with the stop reason, only for a resumable series', () => {
    open(buildSeries({
      status: 'Stopped', stopReason: 'RunCapReached',
      stopReasonText: 'The daily run cap was reached.', resumable: true
    }));

    expect(component.canContinue).toBeTrue();
    expect(component.continueLabel).toContain('The daily run cap was reached.');
    expect(footerText()).toContain('The daily run cap was reached.');
    // A halted series is not live, so cancelling it is not offered.
    expect(footerText()).not.toContain('Cancel Series');
    expect(footerText()).toContain('Close');
  });

  it('should not offer Continue for a cancelled series', () => {
    open(buildSeries({ status: 'Cancelled', resumable: false }));

    expect(component.canContinue).toBeFalse();
    expect(footerText()).not.toContain('Continue');
  });

  it('should keep Download report unavailable, with a reason, until an analysis exists', () => {
    open(buildSeries());

    expect(component.canDownloadReport).toBeFalse();
    expect(component.downloadReportTooltip).toContain('No analysis group exists');
    const button = fixture.nativeElement.querySelector('.download-report-btn') as HTMLElement;
    expect(button.getAttribute('aria-disabled')).toBe('true');
  });

  it('should enable Download report once the group analysis is terminal', () => {
    open(buildSeries({
      status: 'Completed', completedRunCount: 3, autoCreatedGroupId: 9,
      members: [buildMember(), buildMember({ index: 2, runId: 42 })]
    }));

    expect(component.canDownloadReport).toBeTrue();
    const button = fixture.nativeElement.querySelector('.download-report-btn') as HTMLElement;
    expect(button.getAttribute('aria-disabled')).toBeNull();
  });

  it('should download the group report from the groups report endpoint', () => {
    const openSpy = spyOn(window, 'open');
    open(buildSeries({
      status: 'Completed', completedRunCount: 3, autoCreatedGroupId: 9,
      members: [buildMember(), buildMember({ index: 2, runId: 42 })]
    }));

    component.downloadGroupReport();

    expect(openSpy).toHaveBeenCalledWith('/api/admin/benchmark/runs/groups/9/report', '_blank');
  });

  // --- Dialog lifecycle -----------------------------------------------------------------------

  it('should open modally and move focus to the progress heading', () => {
    open(buildSeries());

    const dialog = fixture.nativeElement.querySelector('dialog') as HTMLDialogElement;
    expect(dialog.open).toBeTrue();
    expect(document.activeElement).toBe(fixture.nativeElement.querySelector('.progress-heading'));
  });

  it('should ask the host to close when the dialog is cancelled with Escape', () => {
    open(buildSeries());
    const closed = spyOn(component.closed, 'emit');

    const dialog = fixture.nativeElement.querySelector('dialog') as HTMLDialogElement;
    dialog.dispatchEvent(new Event('cancel'));

    expect(closed).toHaveBeenCalled();
    expect(dialog.open).toBeFalse();
  });

  it('should hand off to the single-run dialog rather than stacking two modals', () => {
    open(buildSeries());
    const closed = spyOn(component.closed, 'emit');
    const handoff = spyOn(component.openRunProgress, 'emit');

    component.openMemberRunProgress(buildMember({ index: 2, runId: 42, status: 'Running' }));

    const dialog = fixture.nativeElement.querySelector('dialog') as HTMLDialogElement;
    expect(dialog.open).toBeFalse();
    expect(closed).toHaveBeenCalled();
    expect(handoff).toHaveBeenCalledWith(42);
  });

  // --- Series diagnostics ---------------------------------------------------------------------

  it('should capture both instrument fingerprints, the rolling-window counts and one line per member', () => {
    open(buildSeries({
      status: 'Stopped', stopReason: 'MemberFailed',
      stopReasonText: 'Run 42 failed during assessment.', resumable: true,
      currentToolGuidesSha256: 'ffff0000111122223333444455556666',
      changedInstrumentHashes: ['ToolGuidesSha256']
    }));

    const capture = component.seriesDiagnosticsText;

    expect(capture).toContain('BENCHMARK SERIES DIAGNOSTICS');
    expect(capture).toContain('Series ID: 3');
    expect(capture).toContain('MemberFailed');

    // Member 1's instrument beside the current one: this is what makes a refused resume readable
    // from the capture alone, without anyone re-deriving which hash moved.
    expect(capture).toContain('eeee5555ffff6666aaaa7777bbbb8888');
    expect(capture).toContain('ffff0000111122223333444455556666');
    expect(capture).toContain('ToolGuidesSha256');

    // The rolling-window counts, not a calendar day.
    expect(capture).toContain('6');
    expect(capture).toContain('14');

    // One line per member, each carrying its run id.
    expect(capture).toContain('41');
    expect(capture).toContain('42');
  });

  it('should say so plainly when no series detail has arrived yet', () => {
    serviceMock.getRunSeries.and.returnValue(throwError(() => ({ status: 500 })));
    fixture.componentRef.setInput('seriesId', 3);
    fixture.componentRef.setInput('visible', true);
    fixture.detectChanges();

    expect(component.seriesDiagnosticsText).toContain('No series detail received yet');
  });

  // --- Group analysis diagnostics --------------------------------------------------------------

  it('should name the compared keys and the tier verdict reasons in the group capture', () => {
    open(buildSeries({
      status: 'Completed', completedRunCount: 3, autoCreatedGroupId: 9,
      members: [buildMember(), buildMember({ index: 2, runId: 42 }), buildMember({ index: 3, runId: 43 })]
    }));

    const capture = component.groupAnalysisDiagnosticsText;

    expect(capture).toContain('Tier A');
    // A tier verdict with no reasons is unusable in a bug report, which is the point of the text.
    expect(capture).toContain('BenchmarkSuiteId');
    expect(capture).toContain('CandidateSystemPromptSha256');
    expect(capture).toContain('Every comparability key matches');

    // Both interval components reported separately, then combined, and each labelled with whether
    // it shrinks with R. A reader who expects the whole interval to fall as sqrt(R) concludes the
    // code is broken, so the labels are the mitigation rather than decoration.
    expect(capture).toContain('Reproducibility: SD 1.53, SE 0.88');
    expect(capture).toContain('shrinks with R');
    expect(capture).toContain('Item sampling: SE 4.20');
    expect(capture).toContain('does NOT shrink with R');
    expect(capture).toContain('Combined 95% interval: half-width 9.06, [62.24, 80.36]');
  });

  // --- Clipboard ------------------------------------------------------------------------------

  it('should surface a clipboard rejection rather than throwing it away', async () => {
    open(buildSeries());
    spyOn(navigator.clipboard, 'writeText').and.returnValue(Promise.reject(new Error('denied')));

    await component.copySeriesDiagnostics();

    expect(component.seriesDiagnosticsCopyFailed).toBeTrue();
    expect(component.copiedSeriesDiagnostics).toBeFalse();
    expect(component.seriesDiagnosticsCopyStatus).toContain('Could not copy');
  });

  it('should report a successful copy in the status line', async () => {
    open(buildSeries());
    spyOn(navigator.clipboard, 'writeText').and.returnValue(Promise.resolve());

    await component.copySeriesDiagnostics();

    expect(component.copiedSeriesDiagnostics).toBeTrue();
    expect(component.seriesDiagnosticsCopyFailed).toBeFalse();
    expect(component.seriesDiagnosticsCopyStatus).toContain('copied');
  });
});
