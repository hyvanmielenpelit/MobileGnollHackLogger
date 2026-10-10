import type { MockedObject } from "vitest";
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { MultiRunProgressDialogComponent } from './multi-run-progress-dialog.component';
import { AdminBenchmarkService } from '../../../services/admin-benchmark.service';
import { SystemService } from '../../../services/system.service';
import { BenchmarkCompletionSoundService } from '../../../services/benchmark-completion-sound.service';
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
  let serviceMock: MockedObject<AdminBenchmarkService>;
  let systemServiceMock: MockedObject<SystemService>;

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
      firstMemberWikiHeadSha: 'aaaa3333bbbb4444cccc5555dddd6666',
      firstMemberSourceCodeHeadSha: 'eeee7777ffff8888aaaa9999bbbb0000',
      currentCandidateSystemPromptSha256: 'aaaa1111bbbb2222cccc3333dddd4444',
      currentToolGuidesSha256: 'eeee5555ffff6666aaaa7777bbbb8888',
      currentKnowledgeBaseHeadSha: 'cccc9999dddd0000eeee1111ffff2222',
      currentWikiHeadSha: 'aaaa3333bbbb4444cccc5555dddd6666',
      currentSourceCodeHeadSha: 'eeee7777ffff8888aaaa9999bbbb0000',
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
    serviceMock.getRunSeries.mockReturnValue(of(series));
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
    serviceMock = {
      getRunSeries: vi.fn().mockName("AdminBenchmarkService.getRunSeries"),
      getRunLimits: vi.fn().mockName("AdminBenchmarkService.getRunLimits"),
      getRun: vi.fn().mockName("AdminBenchmarkService.getRun"),
      getRunGroupAnalysis: vi.fn().mockName("AdminBenchmarkService.getRunGroupAnalysis"),
      analyseRunGroup: vi.fn().mockName("AdminBenchmarkService.analyseRunGroup"),
      previewRunGroupTier: vi.fn().mockName("AdminBenchmarkService.previewRunGroupTier"),
      cancelRunSeries: vi.fn().mockName("AdminBenchmarkService.cancelRunSeries"),
      resumeRunSeries: vi.fn().mockName("AdminBenchmarkService.resumeRunSeries"),
      getGroupReportUrl: vi.fn().mockName("AdminBenchmarkService.getGroupReportUrl")
    } as unknown as MockedObject<AdminBenchmarkService>;
    systemServiceMock = {
      getVersion: vi.fn().mockName("SystemService.getVersion")
    } as unknown as MockedObject<SystemService>;

    systemServiceMock.getVersion.mockReturnValue(of('1.4.2'));
    serviceMock.getRunSeries.mockReturnValue(of(buildSeries()));
    serviceMock.getRunLimits.mockReturnValue(of({
      maxRunsPerHour: 4,
      maxRunsPerDay: 20,
      runsInLastHour: 1,
      runsInLast24Hours: 6,
      remainingDailyHeadroom: 14,
      maxRunCountPerSeries: 20
    }));
    serviceMock.getRun.mockReturnValue(of({ id: 41, answers: [] } as any));
    serviceMock.getRunGroupAnalysis.mockReturnValue(of(null));
    serviceMock.analyseRunGroup.mockReturnValue(of(buildAnalysis()));
    serviceMock.previewRunGroupTier.mockReturnValue(of({
      accepted: true, group: null, comparability: buildComparability()
    } as any));
    serviceMock.cancelRunSeries.mockReturnValue(of(void 0 as any));
    serviceMock.resumeRunSeries.mockReturnValue(of(void 0 as any));
    serviceMock.getGroupReportUrl.mockImplementation((id: number) => `/api/admin/benchmark/runs/groups/${id}/report`);

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

  it('should read the requested run count in the subtitle', () => {
    open(buildSeries({ requestedRunCount: 3 }));

    expect(text('.dialog-subtitle')).toContain('3 runs');
    expect(text('.dialog-subtitle')).not.toContain('run(s)');
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
    serviceMock.getRunGroupAnalysis.mockReturnValue(of(null));
    serviceMock.analyseRunGroup.mockReturnValue(of(null as any));
    open(buildSeries({
      status: 'Completed', completedRunCount: 3, autoCreatedGroupId: 9,
      members: [buildMember(), buildMember({ index: 2, runId: 42 })]
    }));

    expect(component.analysisPending).toBe(true);
    expect(component.stage).toBe('analysing');
    expect(component.stageIndex).toBe(2);
  });

  // --- Named pauses, not spinners ------------------------------------------------------------

  it('should render WaitingForCap as a named state with an explanation', () => {
    open(buildSeries({ status: 'WaitingForCap' }));

    expect(component.stage).toBe('waitingForCap');
    expect(component.isNamedPause).toBe(true);
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
    expect(component.isNamedPause).toBe(true);
    expect(text('.series-state-name')).toContain('Stopped');
    expect(text('.series-state-detail')).toContain('Run 42 failed during assessment.');
    expect(text('.series-state-detail')).toContain('Completed runs are kept');
  });

  // --- Footer -------------------------------------------------------------------------------

  it('should offer Run in Background and Cancel Series while the series is live', () => {
    open(buildSeries());

    expect(component.seriesIsLive).toBe(true);
    expect(footerText()).toContain('Run in Background');
    expect(footerText()).toContain('Cancel Series');
    expect(footerText()).not.toContain('Continue');
  });

  it('should offer Continue, labelled with the stop reason, only for a resumable series', () => {
    open(buildSeries({
      status: 'Stopped', stopReason: 'RunCapReached',
      stopReasonText: 'The daily run cap was reached.', resumable: true
    }));

    expect(component.canContinue).toBe(true);
    expect(component.continueLabel).toContain('The daily run cap was reached.');
    expect(footerText()).toContain('The daily run cap was reached.');
    // A halted series is not live, so cancelling it is not offered.
    expect(footerText()).not.toContain('Cancel Series');
    expect(footerText()).toContain('Close');
  });

  it('should not offer Continue for a cancelled series', () => {
    open(buildSeries({ status: 'Cancelled', resumable: false }));

    expect(component.canContinue).toBe(false);
    expect(footerText()).not.toContain('Continue');
  });

  // --- A member of a model batch ------------------------------------------------------------

  it('should say a model batch owns the series and keep Continue waiting with the reason', () => {
    const getModelBatch = vi.fn(() => of({ id: 21, status: 'Stopped' }));
    (serviceMock as unknown as { getModelBatch: typeof getModelBatch }).getModelBatch = getModelBatch;
    open(buildSeries({
      status: 'Stopped', stopReason: 'MemberFailed', stopReasonText: 'Run 42 failed.', resumable: true, modelBatchRunId: 21
    }));

    expect(getModelBatch).toHaveBeenCalledWith(21);
    expect(text('.series-batch-notice')).toBe('Part of model batch #21. Open model batch #21');
    expect(text('#seriesBatchOwnedReason')).toBe('Use the model batch\'s progress dialog.');
    const continueButton = fixture.nativeElement.querySelector('.series-continue') as HTMLButtonElement;
    expect(continueButton.getAttribute('aria-disabled')).toBe('true');
    expect(continueButton.getAttribute('aria-describedby')).toBe('seriesBatchOwnedReason');

    continueButton.click();
    expect(serviceMock.resumeRunSeries).not.toHaveBeenCalled();

    const opened = vi.fn();
    component.openModelBatch.subscribe(opened);
    (fixture.nativeElement.querySelector('.series-open-batch') as HTMLButtonElement).click();
    expect(opened).toHaveBeenCalledWith(21);
  });

  it('should keep Cancel Series waiting while a live batch drives the series', () => {
    const getModelBatch = vi.fn(() => of({ id: 21, status: 'Running' }));
    (serviceMock as unknown as { getModelBatch: typeof getModelBatch }).getModelBatch = getModelBatch;
    open(buildSeries({ modelBatchRunId: 21 }));

    const cancel = fixture.nativeElement.querySelector('.series-cancel') as HTMLButtonElement;
    expect(cancel.getAttribute('aria-disabled')).toBe('true');
    cancel.click();
    expect(serviceMock.cancelRunSeries).not.toHaveBeenCalled();
  });

  it('should keep View Report unavailable, with a reason, until an analysis exists', () => {
    open(buildSeries());

    expect(component.canViewReport).toBe(false);
    expect(component.viewReportTooltip).toContain('No analysis group exists');
    const button = fixture.nativeElement.querySelector('.view-report-btn') as HTMLElement;
    expect(button.getAttribute('aria-disabled')).toBe('true');
  });

  it('should enable View Report once the group analysis is terminal', () => {
    open(buildSeries({
      status: 'Completed', completedRunCount: 3, autoCreatedGroupId: 9,
      members: [buildMember(), buildMember({ index: 2, runId: 42 })]
    }));

    expect(component.canViewReport).toBe(true);
    const button = fixture.nativeElement.querySelector('.view-report-btn') as HTMLElement;
    expect(button.getAttribute('aria-disabled')).toBeNull();
  });

  it('should hand the group id to the host and close, rather than downloading a file', () => {
    open(buildSeries({
      status: 'Completed', completedRunCount: 3, autoCreatedGroupId: 9,
      members: [buildMember(), buildMember({ index: 2, runId: 42 })]
    }));
    const handoff = vi.spyOn(component.openGroupAnalysis, 'emit').mockReturnValue(undefined);

    component.viewGroupReport();

    expect(handoff).toHaveBeenCalledWith(9);
    const dialog = fixture.nativeElement.querySelector('dialog') as HTMLDialogElement;
    expect(dialog.open).toBe(false);
  });

  // --- Dialog lifecycle -----------------------------------------------------------------------

  it('should open modally and move focus to the progress heading', () => {
    open(buildSeries());

    const dialog = fixture.nativeElement.querySelector('dialog') as HTMLDialogElement;
    expect(dialog.open).toBe(true);
    expect(document.activeElement).toBe(fixture.nativeElement.querySelector('.progress-heading'));
  });

  it('should ask the host to close when the dialog is cancelled with Escape', () => {
    open(buildSeries());
    const closed = vi.spyOn(component.closed, 'emit').mockReturnValue(undefined);

    const dialog = fixture.nativeElement.querySelector('dialog') as HTMLDialogElement;
    dialog.dispatchEvent(new Event('cancel'));

    expect(closed).toHaveBeenCalled();
    expect(dialog.open).toBe(false);
  });

  it('should hand off to the single-run dialog rather than stacking two modals', () => {
    open(buildSeries());
    const closed = vi.spyOn(component.closed, 'emit').mockReturnValue(undefined);
    const handoff = vi.spyOn(component.openRunProgress, 'emit').mockReturnValue(undefined);

    component.openMemberRunProgress(buildMember({ index: 2, runId: 42, status: 'Running' }));

    const dialog = fixture.nativeElement.querySelector('dialog') as HTMLDialogElement;
    expect(dialog.open).toBe(false);
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
    const consoleError = vi.spyOn(console, 'error').mockReturnValue(undefined);
    serviceMock.getRunSeries.mockReturnValue(throwError(() => ({ status: 500 })));
    fixture.componentRef.setInput('seriesId', 3);
    fixture.componentRef.setInput('visible', true);
    fixture.detectChanges();

    expect(component.seriesDiagnosticsText).toContain('No series detail received yet');
    expect(consoleError).toHaveBeenCalledWith('Failed to poll benchmark run series', expect.any(Object));
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

  it('should name which run produced which score on an item line', () => {
    const analysis = buildAnalysis();
    (analysis.result as any).items = [
      { questionId: 70, orderIndex: 4, runCount: 3, mean: 65, standardDeviation: 12.5,
        criticalErrorRate: 0, unstable: true, insufficientRuns: false,
        scores: [80, 52, 63], runIds: [41, 42, 43] }
    ];
    serviceMock.analyseRunGroup.mockReturnValue(of(analysis));
    open(buildSeries({
      status: 'Completed', completedRunCount: 3, autoCreatedGroupId: 9,
      members: [buildMember(), buildMember({ index: 2, runId: 42 }), buildMember({ index: 3, runId: 43 })]
    }));

    const capture = component.groupAnalysisDiagnosticsText;

    expect(capture).toContain('scores: run #41=80.00, run #42=52.00, run #43=63.00');
  });

  it('should omit the score vector when the scores and run ids do not line up', () => {
    const analysis = buildAnalysis();
    (analysis.result as any).items = [
      { questionId: 70, orderIndex: 4, runCount: 3, mean: 65, standardDeviation: 12.5,
        criticalErrorRate: 0, unstable: true, insufficientRuns: false,
        scores: [80, 52], runIds: [41, 42, 43] }
    ];
    serviceMock.analyseRunGroup.mockReturnValue(of(analysis));
    open(buildSeries({
      status: 'Completed', completedRunCount: 3, autoCreatedGroupId: 9,
      members: [buildMember(), buildMember({ index: 2, runId: 42 }), buildMember({ index: 3, runId: 43 })]
    }));

    const capture = component.groupAnalysisDiagnosticsText;

    expect(capture).not.toContain('scores:');
    expect(capture).toContain('Q4 (id 70)');
  });

  it('should count the rubric revision mismatches beside the unpaired items in the comparison capture', () => {
    const analysis = buildAnalysis();
    (analysis as any).comparedWithGroupId = 8;
    (analysis as any).comparedWithGroupName = 'Baseline';
    (analysis as any).comparison = {
      baselineRunIds: [31], treatmentRunIds: [41],
      pairedItemCount: 15, unpairedItemCount: 1, revisionMismatchedItemCount: 2,
      meanDifference: 1.5
    };
    serviceMock.analyseRunGroup.mockReturnValue(of(analysis));
    open(buildSeries({
      status: 'Completed', completedRunCount: 3, autoCreatedGroupId: 9,
      members: [buildMember(), buildMember({ index: 2, runId: 42 }), buildMember({ index: 3, runId: 43 })]
    }));

    const capture = component.groupAnalysisDiagnosticsText;

    expect(capture).toContain(
      'Paired items: 15, unpaired and excluded: 1, rubric revision mismatched and excluded: 2');
  });

  // --- Model roster ---------------------------------------------------------------------------

  it('should give every roster badge a spoken prefix and no title attribute', () => {
    serviceMock.getRun.mockReturnValue(of({
      id: 41,
      answers: [],
      testedModelDisplayNameUsed: 'Gemini 3.7 Flash',
      testedModelIdUsed: 'gemini-3.7-flash',
      testedModelProviderUsed: 'Google',
      testedModelThinkingLevelUsed: 'high',
      testedModelReasoningModeUsed: 'pro',
      testedModelServiceTierUsed: 'flex',
      testedModelParallelExecutionModeUsed: 1,
      assessorModelDisplayNameUsed: 'GPT-5 Mini',
      assessorModelIdUsed: 'gpt-5-mini',
      assessorModelProviderUsed: 'OpenAI',
      secondOpinionAssessorModelDisplayNameUsed: 'Claude Opus 5',
      secondOpinionAssessorModelIdUsed: 'claude-opus-5',
      secondOpinionAssessorModelProviderUsed: 'Anthropic',
      secondOpinionModeUsed: 1
    } as any));
    open(buildSeries());

    const strip = fixture.nativeElement.querySelector('.run-model-strip') as HTMLElement;
    const badgeText = (el: Element | null | undefined) => el?.textContent?.trim();
    expect(strip).toBeTruthy();
    expect(strip.querySelectorAll('[title]').length).toBe(0);
    expect(badgeText(strip.querySelector('.thinking-badge'))).toBe('thinking level High');
    expect(badgeText(strip.querySelector('.reasoning-badge'))).toBe('reasoning mode pro');
    expect(Array.from(strip.querySelectorAll('.config-badge')).map(badgeText))
      .toEqual(['requested service tier Flex', 'coverage Only flagged answers']);
    expect(badgeText(strip.querySelector('.parallel-badge'))).toBe('parallel execution On request for this key');
    expect(strip.querySelector('app-info-tip .gh-info-btn')?.getAttribute('aria-label')).toBe('About Second reader coverage');
    expect(fixture.nativeElement.querySelector('#multiRunCoverageTip')?.textContent).toContain('raised a flag');
  });

  it("should give a panel series' reference reader its own coverage", () => {
    serviceMock.getRun.mockReturnValue(of({
      id: 41,
      answers: [],
      isPanelRun: true,
      testedModelDisplayNameUsed: 'Gemini 3.7 Flash',
      testedModelIdUsed: 'gemini-3.7-flash',
      assessorModelDisplayNameUsed: 'GPT-5 Mini',
      assessorModelIdUsed: 'gpt-5-mini',
      secondOpinionAssessorModelDisplayNameUsed: 'Gemini 3.7 Pro',
      secondOpinionAssessorModelIdUsed: 'gemini-3.7-pro',
      secondOpinionModeUsed: 3
    } as any));
    open(buildSeries());

    const strip = fixture.nativeElement.querySelector('.run-model-strip') as HTMLElement;
    const rows = Array.from(strip.querySelectorAll('.run-model-row')) as HTMLElement[];
    const readerRow = rows.find(row => row.querySelector('dt')?.textContent?.trim() === 'Reference reader');
    expect(readerRow).toBeTruthy();
    expect(readerRow!.querySelector('.config-badge')?.textContent?.trim())
      .toBe('coverage Every answer, blind (reference reading)');
    expect(readerRow!.querySelector('app-info-tip .gh-info-btn')?.getAttribute('aria-label'))
      .toBe('About Reference reader coverage');
    expect(fixture.nativeElement.querySelector('#multiRunCoverageTip')?.textContent).toContain('never scores');
  });

  // --- Clipboard ------------------------------------------------------------------------------

  it('should surface a clipboard rejection rather than throwing it away', async () => {
    open(buildSeries());
    vi.spyOn(navigator.clipboard, 'writeText').mockRejectedValue(new Error('denied'));

    await component.copySeriesDiagnostics();

    expect(component.seriesDiagnosticsCopyFailed).toBe(true);
    expect(component.copiedSeriesDiagnostics).toBe(false);
    expect(component.seriesDiagnosticsCopyStatus).toContain('Could not copy');
  });

  it('should report a successful copy in the status line', async () => {
    open(buildSeries());
    vi.spyOn(navigator.clipboard, 'writeText').mockResolvedValue(undefined);

    await component.copySeriesDiagnostics();

    expect(component.copiedSeriesDiagnostics).toBe(true);
    expect(component.seriesDiagnosticsCopyFailed).toBe(false);
    expect(component.seriesDiagnosticsCopyStatus).toContain('copied');
  });

  // --- Part C: resuming a stopped series arms the completion sound and tells the host ----------

  describe('continueSeries', () => {
    it('arms the completion sound under this click before the resume request is sent', () => {
      const soundService = TestBed.inject(BenchmarkCompletionSoundService);
      const armSpy = vi.spyOn(soundService, 'arm').mockResolvedValue(undefined);
      open(buildSeries({ status: 'Stopped', stopReason: 'MemberFailed', resumable: true }));

      component.continueSeries();

      expect(armSpy).toHaveBeenCalledTimes(1);
    });

    it('emits seriesResumed with the series id once the resume succeeds', () => {
      open(buildSeries({ id: 7, status: 'Stopped', stopReason: 'MemberFailed', resumable: true }));
      const resumedSpy = vi.fn().mockName('seriesResumed');
      component.seriesResumed.subscribe(resumedSpy);

      component.continueSeries();

      expect(serviceMock.resumeRunSeries).toHaveBeenCalledWith(7, undefined);
      expect(resumedSpy).toHaveBeenCalledWith(7);
    });

    it('does not emit seriesResumed when the resume is refused', () => {
      open(buildSeries({ id: 7, status: 'Stopped', stopReason: 'MemberFailed', resumable: true }));
      serviceMock.resumeRunSeries.mockReturnValue(throwError(() => ({ status: 500, error: 'boom' })));
      const resumedSpy = vi.fn().mockName('seriesResumed');
      component.seriesResumed.subscribe(resumedSpy);

      component.continueSeries();

      expect(resumedSpy).not.toHaveBeenCalled();
      expect(component.errorMessage).toContain('boom');
    });
  });
});
