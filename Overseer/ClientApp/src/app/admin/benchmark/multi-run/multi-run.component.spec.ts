import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { of } from 'rxjs';

import { MultiRunComponent } from './multi-run.component';
import {
  AdminBenchmarkService,
  BenchmarkComparabilityResultDto,
  BenchmarkGroupAnalysisDto,
  BenchmarkRunGroupDto,
  BenchmarkRunGroupTierPreviewDto,
  BenchmarkRunSummaryDto
} from '../../../services/admin-benchmark.service';

/**
 * The URL the group report download targets.
 *
 * Tested against the real service rather than the component's spy, because the point of the test
 * is the route itself: the component only ever hands this string to window.open, so a wrong path
 * here would fail silently as a 404 in a new browser tab that nothing in the app ever sees.
 */
describe('AdminBenchmarkService group report URL', () => {
  it('should target /api/admin/benchmark/runs/groups/{id}/report', () => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()]
    });

    const service = TestBed.inject(AdminBenchmarkService);

    expect(service.getGroupReportUrl(42)).toBe('/api/admin/benchmark/runs/groups/42/report');
  });
});

describe('MultiRunComponent', () => {
  let component: MultiRunComponent;
  let fixture: ComponentFixture<MultiRunComponent>;
  let serviceMock: jasmine.SpyObj<AdminBenchmarkService>;

  function buildGroup(overrides: Partial<BenchmarkRunGroupDto> = {}): BenchmarkRunGroupDto {
    return {
      id: 7,
      name: 'Suite 5 · GPT-5.6 · baseline · R=3',
      benchmarkSuiteId: 5,
      suiteName: 'GnollHack Player Assistance Benchmark Suite',
      tier: 'Replicate',
      tierLabel: 'Tier A — Replicate',
      comparabilityKeyHash: 'a1b2c3d4e5f6',
      crossCondition: false,
      notes: null,
      createdFromSeriesId: 3,
      createdAtUtc: '2026-09-06T10:00:00Z',
      modifiedAtUtc: '2026-09-06T10:00:00Z',
      runCount: 3,
      members: [
        { runId: 41, startedAtUtc: '2026-09-06T08:00:00Z', status: 'Completed', qualityIndex: 71, speedIndex: 55, testedModelDisplayName: 'GPT-5.6 Luna', shortFingerprint: 'e9b3e9a7', addedAtUtc: '2026-09-06T10:00:00Z' },
        { runId: 42, startedAtUtc: '2026-09-06T09:00:00Z', status: 'Completed', qualityIndex: 73, speedIndex: 54, testedModelDisplayName: 'GPT-5.6 Luna', shortFingerprint: 'e9b3e9a7', addedAtUtc: '2026-09-06T10:00:00Z' },
        { runId: 43, startedAtUtc: '2026-09-06T09:40:00Z', status: 'Completed', qualityIndex: 70, speedIndex: 56, testedModelDisplayName: 'GPT-5.6 Luna', shortFingerprint: 'e9b3e9a7', addedAtUtc: '2026-09-06T10:00:00Z' }
      ],
      latestAnalysisId: null,
      latestAnalysisAtUtc: null,
      analysisStale: false,
      ...overrides
    };
  }

  function buildRun(overrides: Partial<BenchmarkRunSummaryDto> = {}): BenchmarkRunSummaryDto {
    return {
      id: 41,
      benchmarkSuiteId: 5,
      suiteName: 'GnollHack Player Assistance Benchmark Suite',
      testedModelConfigurationId: 1,
      testedModelDisplayNameUsed: 'GPT-5.6 Luna',
      testedModelProviderUsed: 'OpenAI',
      testedModelIdUsed: 'gpt-5.6-luna',
      assessorModelConfigurationId: 2,
      assessorModelDisplayNameUsed: 'Claude Opus 5',
      status: 'Completed',
      startedAtUtc: '2026-09-06T08:00:00Z',
      qualityIndex: 71,
      speedIndex: 55,
      totalAnswerDurationMs: 1000,
      speedMeasurementDegraded: false,
      answeredQuestionCount: 18,
      totalQuestionCount: 18,
      transportDefectAnswerCount: 0,
      totalDurationMs: 1000,
      toolGuidesSha256: 'f59d8b30aaaa',
      ...overrides
    } as BenchmarkRunSummaryDto;
  }

  /** A refusal: the suite differs, so the set is below Tier B and cannot be persisted at all. */
  function buildRefusedPreview(): BenchmarkRunGroupTierPreviewDto {
    const comparability: BenchmarkComparabilityResultDto = {
      tier: 'NotComparable',
      tierLabel: 'Not comparable',
      poolingPermitted: false,
      speedAggregatesDegraded: true,
      costAggregatesDegraded: true,
      explanation: 'These runs do not form a comparable set.',
      comparabilityKeyHash: '',
      matchedKeys: ['HarnessVersion', 'ScoringMethodVersion'],
      differences: [
        {
          name: 'ToolGuidesSha256',
          kind: 'Instrument',
          description: 'The tool guides differ between the runs.',
          variants: [
            { value: 'f59d8b30', runIds: [41, 42] },
            { value: '9c1de220', runIds: [43] }
          ]
        }
      ],
      runIds: [41, 42, 43]
    };
    return { accepted: false, error: 'Refused: the set is below Tier B.', comparability, group: null };
  }

  function buildCrossConditionPreview(): BenchmarkRunGroupTierPreviewDto {
    const refused = buildRefusedPreview();
    return {
      accepted: true,
      error: null,
      comparability: {
        ...refused.comparability!,
        tier: 'CrossCondition',
        tierLabel: 'Tier C — Cross-condition',
        poolingPermitted: false
      },
      group: null
    };
  }

  function buildAnalysis(overrides: Partial<BenchmarkGroupAnalysisDto> = {}): BenchmarkGroupAnalysisDto {
    return {
      id: 11,
      groupId: 7,
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
        items: [
          {
            questionId: 101,
            orderIndex: 11,
            questionText: 'Which intrinsics do gnolls gain?',
            runCount: 3,
            mean: 62.5,
            median: 63,
            min: 55,
            max: 70,
            standardDeviation: 17.4,
            interquartileRange: 12,
            coefficientOfVariation: 0.278,
            meanConfidenceHalfWidth: 9.1,
            criticalErrorCount: 1,
            criticalErrorRate: 0.3333,
            medianModelTimeMs: 41000,
            unstable: true,
            insufficientRuns: false
          }
        ],
        index: {
          runCount: 3,
          pointEstimate: 71.3,
          weightedMeanOfItemMeans: 71.3,
          identityHolds: true,
          perRunIndices: [71, 73, 70],
          meanStoredQualityIndex: 71.3,
          reproducibilityStandardDeviation: 1.53,
          reproducibilityStandardError: 0.88,
          reproducibilityCriticalValue: 4.303,
          reproducibilityHalfWidth: 3.8,
          itemSamplingStandardError: 4.2,
          itemSamplingCriticalValue: 1.96,
          itemSamplingHalfWidth: 8.23,
          combinedHalfWidth: 9.07,
          combinedLower: 62.2,
          combinedUpper: 80.4,
          reproducibilityAvailable: true
        },
        speed: {
          runCount: 3,
          meanSpeedIndex: 55,
          speedIndexStandardDeviation: 1.0,
          perRunSpeedIndices: [55, 54, 56],
          pooledAnswerCount: 54,
          modelTimeP50Ms: 41000,
          modelTimeP90Ms: 96000,
          modelTimeMaxMs: 141000,
          degraded: false,
          degradedReason: null,
          caveat: 'Comparable only within one thinking level and timing mode.'
        },
        cost: {
          runCount: 3,
          totalCost: 7.59,
          meanCostPerRun: 2.53,
          costStandardDeviation: 0.11,
          totalCostByRole: { candidate: 1.8, assessor: 0.69, claimVerifier: 5.1 },
          meanCostByRole: { candidate: 0.6, assessor: 0.23, claimVerifier: 1.7 },
          costPerQuestion: 0.14,
          costPerIndexPoint: 0.035,
          degraded: false,
          degradedReason: null
        },
        unstableQuestionIds: [101],
        pooledIndexReportable: true,
        varianceDecompositionCaveat:
          'Run-to-run variance mixes candidate stochasticity with grader stochasticity.'
      },
      comparison: null,
      ...overrides
    };
  }

  beforeEach(async () => {
    serviceMock = jasmine.createSpyObj('AdminBenchmarkService', [
      'getRunGroups', 'getRunGroup', 'getRuns', 'previewRunGroupTier', 'createRunGroup',
      'updateRunGroup', 'deleteRunGroup', 'analyseRunGroup', 'getRunGroupAnalysis',
      'getGroupReportUrl'
    ]);

    serviceMock.getRunGroups.and.returnValue(of([buildGroup()]));
    serviceMock.getRunGroup.and.returnValue(of(buildGroup()));
    serviceMock.getRuns.and.returnValue(of([buildRun(), buildRun({ id: 42 }), buildRun({ id: 43 })]));
    serviceMock.previewRunGroupTier.and.returnValue(of({ accepted: true, comparability: null, group: null }));
    serviceMock.getRunGroupAnalysis.and.returnValue(of(null));
    serviceMock.analyseRunGroup.and.returnValue(of(buildAnalysis()));
    serviceMock.deleteRunGroup.and.returnValue(of(void 0));
    serviceMock.getGroupReportUrl.and.callFake((id: number) => `/api/admin/benchmark/runs/groups/${id}/report`);

    await TestBed.configureTestingModule({
      imports: [MultiRunComponent],
      providers: [{ provide: AdminBenchmarkService, useValue: serviceMock }]
    }).compileComponents();

    fixture = TestBed.createComponent(MultiRunComponent);
    component = fixture.componentInstance;
  });

  function open(): void {
    component.suiteId = 5;
    fixture.detectChanges();
  }

  function text(selector: string): string {
    const element = fixture.nativeElement.querySelector(selector) as HTMLElement | null;
    return (element?.textContent || '').replace(/\s+/g, ' ').trim();
  }

  function allText(selector: string): string {
    const elements: HTMLElement[] = Array.from(fixture.nativeElement.querySelectorAll(selector));
    return elements.map(e => (e.textContent || '')).join(' ').replace(/\s+/g, ' ').trim();
  }

  it('should load the groups and the suite runs when a suite is selected', () => {
    open();

    expect(serviceMock.getRunGroups).toHaveBeenCalled();
    expect(serviceMock.getRuns).toHaveBeenCalledWith(5, 50);
    expect(component.groups.length).toBe(1);
  });

  it('should show a group row with its tier badge, run count and stale badge', () => {
    serviceMock.getRunGroups.and.returnValue(of([buildGroup({
      latestAnalysisId: 11,
      latestAnalysisAtUtc: '2026-09-06T11:00:00Z',
      analysisStale: true
    })]));
    open();

    expect(text('.mr-group-table .mr-tier')).toContain('Tier A');
    expect(allText('.mr-group-table tbody td')).toContain('3');
    expect(allText('.mr-badge-stale')).toContain('Stale analysis');
  });

  // --- Report download ---

  it('should disable the row download control while the group has no analysis', () => {
    open();

    const button = fixture.nativeElement.querySelector('.mr-download') as HTMLButtonElement;
    expect(button.getAttribute('aria-disabled')).toBe('true');
    expect(component.canDownloadReport(component.groups[0])).toBeFalse();
    expect(component.downloadReportTooltip(component.groups[0]))
      .toContain('No analysis yet');

    const openSpy = spyOn(window, 'open');
    button.click();
    expect(openSpy).not.toHaveBeenCalled();
  });

  it('should download the group report through window.open once an analysis exists', () => {
    const analysed = buildGroup({ latestAnalysisId: 11, latestAnalysisAtUtc: '2026-09-06T11:00:00Z' });
    serviceMock.getRunGroups.and.returnValue(of([analysed]));
    open();

    const openSpy = spyOn(window, 'open');
    const button = fixture.nativeElement.querySelector('.mr-download') as HTMLButtonElement;
    expect(button.getAttribute('aria-disabled')).toBe('false');
    button.click();

    expect(serviceMock.getGroupReportUrl).toHaveBeenCalledWith(7);
    expect(openSpy).toHaveBeenCalledWith('/api/admin/benchmark/runs/groups/7/report', '_blank');
  });

  it('should disable the detail download control until the group has been analysed', () => {
    open();
    component.selectGroup(buildGroup());
    fixture.detectChanges();

    const button = fixture.nativeElement.querySelector('.mr-download-detail') as HTMLButtonElement;
    expect(button.getAttribute('aria-disabled')).toBe('true');
    expect(text('#mr-tip-download-detail')).toContain('No analysis yet');
  });

  // --- Tier feedback ---

  it('should render which keys differ and on which runs when a tier is refused', () => {
    serviceMock.previewRunGroupTier.and.returnValue(of(buildRefusedPreview()));
    open();

    component.toggleRun(41);
    component.toggleRun(43);
    fixture.detectChanges();

    expect(text('.mr-preview .mr-tier')).toContain('Not comparable');
    const differences = allText('.mr-differences');
    expect(differences).toContain('ToolGuidesSha256');
    expect(differences).toContain('The tool guides differ between the runs.');
    expect(differences).toContain('#41, #42');
    expect(differences).toContain('#43');
  });

  it('should offer the cross-condition checkbox only at Tier C', () => {
    serviceMock.previewRunGroupTier.and.returnValue(of(buildRefusedPreview()));
    open();
    component.toggleRun(41);
    component.toggleRun(43);
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('#mr-cross-condition')).toBeNull();

    serviceMock.previewRunGroupTier.and.returnValue(of(buildCrossConditionPreview()));
    component.previewTier();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('#mr-cross-condition')).not.toBeNull();
  });

  it('should not preview a tier below two selected runs', () => {
    open();
    serviceMock.previewRunGroupTier.calls.reset();

    component.toggleRun(41);

    expect(serviceMock.previewRunGroupTier).not.toHaveBeenCalled();
    expect(component.preview).toBeNull();
  });

  // --- Analysis rendering ---

  function openAnalysed(analysis: BenchmarkGroupAnalysisDto = buildAnalysis()): void {
    const analysed = buildGroup({ latestAnalysisId: 11, latestAnalysisAtUtc: '2026-09-06T11:00:00Z' });
    serviceMock.getRunGroups.and.returnValue(of([analysed]));
    serviceMock.getRunGroup.and.returnValue(of(analysed));
    serviceMock.getRunGroupAnalysis.and.returnValue(of(analysis));
    open();
    component.selectGroup(analysed);
    fixture.detectChanges();
  }

  it('should render both interval components with distinct labels, and the item-sampling one with its does-not-shrink note', () => {
    openAnalysed();

    const repro = text('.mr-interval-repro');
    const item = text('.mr-interval-item');

    expect(repro).toContain('Reproducibility standard error');
    expect(repro).toContain('Would a re-run move this?');
    expect(repro).toContain('Shrinks with R');

    expect(item).toContain('Item-sampling standard error');
    expect(item).toContain('Would a different set of questions move this?');
    expect(item).toContain('Does not shrink with R');
    // The reason, not only the claim: without it a reader takes the label for a defect note.
    expect(item).toContain('Every run answers the same items');

    // Two distinct components, not one figure rendered twice.
    expect(repro).not.toBe(item);
    expect(fixture.nativeElement.querySelectorAll('.mr-interval').length).toBe(2);
  });

  it('should label the combined interval as covering both sources', () => {
    openAnalysed();

    const combined = text('.mr-combined');
    expect(combined).toContain('Combined 95 % interval');
    expect(combined).toContain('both');
  });

  it('should say the interval covers one source only below three runs', () => {
    const analysis = buildAnalysis();
    analysis.result.index.reproducibilityAvailable = false;
    analysis.result.index.reproducibilityStandardError = null;
    analysis.result.index.runCount = 2;
    openAnalysed(analysis);

    expect(text('.mr-interval-repro')).toContain('Not reported below three runs');
    expect(text('.mr-combined')).toContain('one');
  });

  it('should always render the variance decomposition caveat', () => {
    openAnalysed();

    const caveat = text('.mr-caveat');
    expect(caveat).toContain('What this cannot decompose');
    expect(caveat).toContain('grader stochasticity');
  });

  it('should fall back to its own caveat text when the stored result carries none', () => {
    const analysis = buildAnalysis();
    analysis.result.varianceDecompositionCaveat = null;
    openAnalysed(analysis);

    expect(text('.mr-caveat')).toContain('re-grading identical answers');
  });

  it('should render the per-item table with mean, median, SD, CV, critical-error rate and the stability flag', () => {
    openAnalysed();

    const row = text('.mr-item-table tbody tr');
    expect(row).toContain('Q11');
    expect(row).toContain('62.5');   // mean
    expect(row).toContain('63.0');   // median
    expect(row).toContain('17.40');  // sample SD
    expect(row).toContain('27.8 %'); // coefficient of variation
    expect(row).toContain('33 %');   // critical-error rate
    expect(row).toContain('Unstable');
  });

  it('should render the speed and cost sections and their degraded flags', () => {
    const analysis = buildAnalysis();
    analysis.result.speed.degraded = true;
    analysis.result.speed.degradedReason = 'Question parallelism differs across members.';
    analysis.result.cost.degraded = true;
    analysis.result.cost.degradedReason = 'The pricing snapshot differs across members.';
    openAnalysed(analysis);

    const panel = allText('.mr-subsection');
    expect(panel).toContain('Speed aggregates degraded');
    expect(panel).toContain('Question parallelism differs across members.');
    expect(panel).toContain('Cost aggregates degraded');
    expect(panel).toContain('The pricing snapshot differs across members.');
    expect(panel).toContain('$7.59');
  });

  it('should flag speed and cost as degraded when only the comparability tier says so', () => {
    // The tier reaches the detail view through the read-only preview endpoint, so the test drives
    // it the same way the component does. Assigning the field from outside would leave the view
    // unmarked and never re-render, which measures the test harness rather than the component.
    serviceMock.previewRunGroupTier.and.returnValue(of({
      accepted: true,
      group: null,
      comparability: {
        tier: 'QualityComparable',
        tierLabel: 'Tier B — Quality-comparable',
        poolingPermitted: true,
        speedAggregatesDegraded: true,
        costAggregatesDegraded: true,
        explanation: 'Question parallelism differs.',
        comparabilityKeyHash: 'abc',
        matchedKeys: [],
        differences: [],
        runIds: [41, 42, 43]
      }
    }));

    // The statistics themselves say nothing is degraded: only the tier does, which is the point.
    const analysis = buildAnalysis();
    expect(analysis.result.speed.degraded).toBeFalse();
    expect(analysis.result.cost.degraded).toBeFalse();
    openAnalysed(analysis);

    expect(component.speedDegraded).toBeTrue();
    expect(component.costDegraded).toBeTrue();

    const panel = allText('.mr-subsection');
    expect(panel).toContain('Speed aggregates degraded');
    expect(panel).toContain('Cost aggregates degraded');
  });
  // --- Comparison ---

  it('should analyse against the selected baseline group', () => {
    openAnalysed();
    component.compareWithGroupId = 9;

    component.analyse();

    expect(serviceMock.analyseRunGroup).toHaveBeenCalledWith(7, { compareWithGroupId: 9 });
  });

  it('should label per-item differences as exploratory under Benjamini-Hochberg control', () => {
    const analysis = buildAnalysis({
      comparedWithGroupId: 9,
      comparedWithGroupName: 'Suite 5 · GPT-5.6 · treatment · R=3'
    });
    analysis.comparison = {
      baselineRunIds: [41, 42, 43],
      treatmentRunIds: [51, 52, 53],
      pairedItemCount: 18,
      unpairedItemCount: 0,
      meanDifference: 2.4,
      differenceStandardDeviation: 6.1,
      differenceConfidenceHalfWidth: 3.0,
      differenceConfidenceLower: -0.6,
      differenceConfidenceUpper: 5.4,
      wilcoxon: { sampleSize: 17, zeroDifferenceCount: 1, statistic: 44, pValue: 0.08, method: 'exact', tiesPresent: false },
      pairedT: { sampleSize: 18, meanDifference: 2.4, tStatistic: 1.67, pValue: 0.11 },
      cohensDz: 0.39,
      itemComparisons: [
        {
          questionId: 101, orderIndex: 14, questionText: "Master Kaen's exact stats?",
          baselineMean: 55, treatmentMean: 78, difference: 23,
          pValue: 0.004, adjustedPValue: 0.041, rejectedAtFdr: true, exploratory: true
        }
      ],
      falseDiscoveryRate: 0.05,
      exploratoryNote: null
    };
    openAnalysed(analysis);

    const comparison = text('.mr-comparison');
    expect(comparison).toContain('Per-item differences — exploratory');
    expect(comparison).toContain('Benjamini');
    expect(comparison).toContain('q-value, not a p-value');
    expect(comparison).toContain('Wilcoxon');
    // The primary/secondary ordering is a claim about the method, not a layout choice.
    expect(comparison.indexOf('Wilcoxon p (primary)')).toBeLessThan(comparison.indexOf('Paired t p (secondary)'));
    expect(text('.mr-comparison-table tbody tr')).toContain('exploratory');
  });

  // --- Formatting ---

  it('should format cost at two decimals from a dollar and four below it', () => {
    expect(component.formatCost(2.5311)).toBe('$2.53');
    expect(component.formatCost(0.0042)).toBe('$0.0042');
    expect(component.formatCost(null)).toBe('—');
  });

  it('should render a p-value below the column resolution as a bound rather than as zero', () => {
    expect(component.formatPValue(0.0000004)).toBe('< 0.001');
    expect(component.formatPValue(0.041)).toBe('0.041');
  });

  it('should show nothing but a prompt when no suite is selected', () => {
    fixture.detectChanges();

    expect(serviceMock.getRunGroups).not.toHaveBeenCalled();
    expect(text('.multi-run')).toContain('Select a benchmark suite');
  });

  // --- The analysis modal ---

  it('should open the analysis in the modal from the row eye control, not from the group name', () => {
    open();

    // The name is text now. A report is opened by the same eye control the run history uses.
    expect(fixture.nativeElement.querySelector('.mr-group-name')?.tagName).toBe('SPAN');

    const view = fixture.nativeElement.querySelector(
      '.mr-group-table .col-actions .action-btn') as HTMLButtonElement;
    expect(view.getAttribute('aria-label')).toContain('View analysis for group');

    spyOn(component, 'openGroup').and.callThrough();
    view.click();
    fixture.detectChanges();

    expect(component.openGroup).toHaveBeenCalled();
    expect(component.selectedGroup?.id).toBe(7);
    expect(serviceMock.getRunGroupAnalysis).toHaveBeenCalledWith(7);
  });

  it('should render the detail inside the dialog and clear it on close', () => {
    serviceMock.getRunGroupAnalysis.and.returnValue(of(buildAnalysis()));
    open();

    component.openGroup(component.groups[0]);
    fixture.detectChanges();

    const dialog = fixture.nativeElement.querySelector('dialog.mr-analysis-dialog') as HTMLElement;
    expect(dialog).not.toBeNull();
    expect(dialog.textContent).toContain('Multi-run Intelligence Index');

    component.closeGroup();
    fixture.detectChanges();

    expect(component.selectedGroup).toBeNull();
    expect(dialog.textContent).not.toContain('Multi-run Intelligence Index');
  });

  it('should emit the originating series from the group row badge', () => {
    serviceMock.getRunGroups.and.returnValue(of([buildGroup({ createdFromSeriesId: 2 })]));
    open();

    const emitted: number[] = [];
    component.openSeries.subscribe((id: number) => emitted.push(id));

    const badge = fixture.nativeElement.querySelector('.mr-badge-series') as HTMLButtonElement;
    expect(badge.tagName).toBe('BUTTON');
    badge.click();

    expect(emitted).toEqual([2]);
  });

  it('should not emit a series for a group that was not created from one', () => {
    open();

    const emitted: number[] = [];
    component.openSeries.subscribe((id: number) => emitted.push(id));
    component.viewSeries(buildGroup({ createdFromSeriesId: null }));

    expect(emitted).toEqual([]);
  });

  // --- The blocks the panel was missing ---

  it('should render the prompt under test, including the divergence from live chat', () => {
    serviceMock.getRunGroupAnalysis.and.returnValue(of(buildAnalysis({
      result: {
        ...(buildAnalysis().result as object),
        promptUnderTest: {
          recorded: true,
          divergent: false,
          overseerMode: 0,
          verboseMode: false,
          spoilerFreeMode: false,
          enableToolUse: true,
          enableWebSearch: false,
          enableSubAgents: false,
          allowSourceCodeReferences: true,
          isGameOn: false,
          developerMode: false,
          hasMessageHistory: false,
          hasWikiContext: false,
          hasGameSnapshot: false,
          parallelMode: 2
        }
      }
    })));
    open();
    component.openGroup(component.groups[0]);
    fixture.detectChanges();

    const body = text('.mr-dialog-body');
    expect(body).toContain('Prompt under test');
    expect(body).toContain('Concise');
    expect(body).toContain('Gameplay Help');
    expect(body).toContain('Live chat pre-injects wiki articles');
  });

  it('should say so when the prompt configuration was not recorded', () => {
    serviceMock.getRunGroupAnalysis.and.returnValue(of(buildAnalysis({
      result: {
        ...(buildAnalysis().result as object),
        promptUnderTest: { recorded: false, divergent: false } as unknown
      }
    })));
    open();
    component.openGroup(component.groups[0]);
    fixture.detectChanges();

    expect(text('.mr-dialog-body')).toContain('Not recorded for these runs');
  });

  it('should render the dimension table and name the lowest dimension', () => {
    serviceMock.getRunGroupAnalysis.and.returnValue(of(buildAnalysis({
      result: {
        ...(buildAnalysis().result as object),
        dimensions: [
          { dimension: 'Accuracy', perRunMeans: [96, 95, 97], mean: 96, standardDeviation: 1, confidenceHalfWidth: 2.5, min: 95, max: 97, itemMeans: { '101': 94 } },
          { dimension: 'Completeness', perRunMeans: [84, 85, 86], mean: 85, standardDeviation: 1, confidenceHalfWidth: 2.5, min: 84, max: 86, itemMeans: { '101': 80 } },
          { dimension: 'Conciseness', perRunMeans: [], mean: null },
          { dimension: 'Readability', perRunMeans: [], mean: null }
        ]
      }
    })));
    open();
    component.openGroup(component.groups[0]);
    fixture.detectChanges();

    const body = text('.mr-dialog-body');
    expect(body).toContain('Quality dimensions');
    expect(body).toContain('Completeness');
    expect(body).toContain('Lowest dimension:');
    // Q11 is the only item in the fixture, and it is the weakest on both scored dimensions.
    expect(body).toContain('Q11');

    // An unscored dimension is dropped from the table rather than rendered as zeroes.
    expect(component.dimensionStats.length).toBe(2);
  });

  it('should render pooled tool families and what the claim verifier bought', () => {
    serviceMock.getRunGroupAnalysis.and.returnValue(of(buildAnalysis({
      result: {
        ...(buildAnalysis().result as object),
        usage: {
          runCount: 3,
          totalInputTokens: 4_210_000,
          totalOutputTokens: 117_600,
          totalCacheReadTokens: 3_806_000,
          cacheReadSharePercentage: 90.4,
          inputOutputRatio: 35.8,
          perRunInputTokens: [1_400_000, 1_400_000, 1_410_000],
          inputTokenStandardDeviation: 5773,
          totalAssessmentInputTokens: 500_000,
          totalAssessmentOutputTokens: 20_000,
          totalClaimVerificationInputTokens: 900_000,
          totalClaimVerificationOutputTokens: 30_000,
          totalToolCalls: 281,
          meanToolCallsPerRun: 93.7,
          toolCallStandardDeviation: 4.2,
          toolCallsByFamily: { SourceCode: 158, Wiki: 115, StructuredLookup: 7, KnowledgeBase: 1 },
          toolFamilyShares: { SourceCode: 56.2, Wiki: 40.9, StructuredLookup: 2.5, KnowledgeBase: 0.4 },
          claimsSupported: 10,
          claimsRefuted: 0,
          claimsIndeterminate: 0,
          claimsChecked: 10,
          answersWithVerification: 3
        }
      }
    })));
    open();
    component.openGroup(component.groups[0]);
    fixture.detectChanges();

    const body = text('.mr-dialog-body');
    expect(body).toContain('Tokens and tool usage');
    expect(body).toContain('4.21 M');
    expect(body).toContain('Grader tokens, kept separate');
    expect(body).toContain('SourceCode');
    expect(body).toContain('10 claims checked across 3 answers');

    // Families are ordered by call count, so the heaviest is first.
    expect(component.toolFamilyRows[0].family).toBe('SourceCode');
  });

  it('should name the role carrying the cost spread', () => {
    serviceMock.getRunGroupAnalysis.and.returnValue(of(buildAnalysis({
      result: {
        ...(buildAnalysis().result as object),
        cost: {
          runCount: 3,
          totalCost: 12.37,
          meanCostPerRun: 4.12,
          costStandardDeviation: 1.57,
          totalCostByRole: { claimVerifier: 9.83, assessor: 1.63, candidate: 0.9073 },
          meanCostByRole: { claimVerifier: 3.28, assessor: 0.5444, candidate: 0.3024 },
          perRunTotals: [3.4582, 2.9969, 5.9154],
          costStandardDeviationByRole: { claimVerifier: 1.55, assessor: 0.01, candidate: 0.002 },
          minCostByRole: { claimVerifier: 2.4, assessor: 0.53, candidate: 0.3 },
          maxCostByRole: { claimVerifier: 5.4, assessor: 0.55, candidate: 0.31 },
          costPerQuestion: 0.2291,
          costPerIndexPoint: 0.0437
        }
      }
    })));
    open();
    component.openGroup(component.groups[0]);
    fixture.detectChanges();

    const body = text('.mr-dialog-body');
    expect(body).toContain('Per-run totals:');
    expect(body).toContain('Cost dispersion sits mostly in claimVerifier');
    expect(component.widestCostRole?.role).toBe('claimVerifier');
  });

  it('should mark a combined interval that was truncated at the score bound', () => {
    const base = buildAnalysis().result as { index: Record<string, unknown> };
    serviceMock.getRunGroupAnalysis.and.returnValue(of(buildAnalysis({
      result: {
        ...(buildAnalysis().result as object),
        index: { ...base.index, combinedUpper: 100, combinedIntervalTruncated: true }
      }
    })));
    open();
    component.openGroup(component.groups[0]);
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('.mr-truncated-marker')).not.toBeNull();
    expect(text('.mr-dialog-body')).toContain('pinned there');
  });
});
