import type { MockedObject } from "vitest";
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';

import { GroupAnalysisDialogComponent } from './group-analysis-dialog.component';
import {
  AdminBenchmarkService,
  BenchmarkGroupAnalysisDto,
  BenchmarkRunGroupDto
} from '../../../../services/admin-benchmark.service';

const SUITE_NAME = 'GnollHack Player Assistance Benchmark Suite';

describe('GroupAnalysisDialogComponent', () => {
  let component: GroupAnalysisDialogComponent;
  let fixture: ComponentFixture<GroupAnalysisDialogComponent>;
  let serviceMock: MockedObject<AdminBenchmarkService>;
  /** Whether this test has created its fixture; `openWith` creates one with the default groups otherwise. */
  let created = false;

  function buildGroup(overrides: Partial<BenchmarkRunGroupDto> = {}): BenchmarkRunGroupDto {
    return {
      id: 7,
      name: `${SUITE_NAME} · GPT-5.6 Luna · 2026-09-06 · R=3`,
      benchmarkSuiteId: 5,
      suiteName: SUITE_NAME,
      testedModelDisplayName: 'GPT-5.6 Luna',
      testedModelProvider: 'OpenAI',
      testedModelId: 'gpt-5.6-luna',
      testedModelThinkingLevel: 'high',
      testedModelReasoningMode: null,
      tier: 'Replicate',
      tierLabel: 'Tier A — Replicate',
      comparabilityKeyHash: 'a1b2c3d4e5f6',
      comparabilityKeyStale: false,
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
      memberRunIds: [41, 42, 43],
      latestAnalysisId: 11,
      latestAnalysisAtUtc: '2026-09-06T11:00:00Z',
      analysisStale: false,
      headlineIndex: 71.3,
      headlineHalfWidth: 9.07,
      headlineSpeedIndex: 55,
      headlineCostPerRun: 2.53,
      nameIsGenerated: true,
      ...overrides
    } as BenchmarkRunGroupDto;
  }

  function buildAnalysis(overrides: Partial<BenchmarkGroupAnalysisDto> = {}): BenchmarkGroupAnalysisDto {
    return {
      id: 11,
      groupId: 7,
      groupName: `${SUITE_NAME} · GPT-5.6 Luna · 2026-09-06 · R=3`,
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
        suiteName: SUITE_NAME,
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

  /** The same analysis under statistics method 2: a crossed bootstrap interval and bootstrap dimension bounds. */
  function buildAnalysisV2(overrides: Partial<BenchmarkGroupAnalysisDto> = {}): BenchmarkGroupAnalysisDto {
    const analysis = buildAnalysis(overrides);
    analysis.result = {
      ...analysis.result,
      statisticsMethodVersion: 2,
      index: {
        ...analysis.result.index,
        combinedHalfWidth: 8.35,
        combinedLower: 63.1,
        combinedUpper: 79.8,
        combinedIntervalTruncated: false,
        combinedIntervalMethod: 'crossedBootstrap',
        bootstrapReplicates: 10000,
        bootstrapSeed: 20261025,
        runsWhereRunComponentMatchesQuestions: 5,
        runSpanHours: 30
      },
      dimensions: [
        {
          dimension: 'Accuracy', perRunMeans: [96, 95, 97], mean: 96, standardDeviation: 1, confidenceHalfWidth: 2.05,
          min: 95, max: 97, itemMeans: { '101': 94 }, bootstrapLower: 93.8, bootstrapUpper: 97.9, intervalMethod: 'crossedBootstrap'
        },
        {
          dimension: 'Completeness', perRunMeans: [84, 85, 86], mean: 85, standardDeviation: 1, confidenceHalfWidth: 4.7,
          min: 84, max: 86, itemMeans: { '101': 80 }, bootstrapLower: 80.2, bootstrapUpper: 89.6, intervalMethod: 'crossedBootstrap'
        },
        { dimension: 'Conciseness', perRunMeans: [], mean: null },
        { dimension: 'Readability', perRunMeans: [], mean: null }
      ]
    };
    return analysis;
  }

  /** A method 1 comparison, as analyses stored before statistics method 2 carry it. */
  function buildComparisonV1(): Record<string, unknown> {
    return {
      baselineRunIds: [31, 32, 33],
      treatmentRunIds: [41, 42, 43],
      pairedItemCount: 18,
      unpairedItemCount: 0,
      revisionMismatchedItemCount: 0,
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
  }

  function buildComparisonV2(overrides: Record<string, unknown> = {}): Record<string, unknown> {
    return {
      ...buildComparisonV1(),
      meanDifference: 2.1,
      differenceConfidenceLower: -0.5,
      differenceConfidenceUpper: 4.7,
      wilcoxon: { sampleSize: 17, zeroDifferenceCount: 1, statistic: 40, pValue: 0.06, method: 'exact', tiesPresent: false },
      pairedT: { sampleSize: 18, meanDifference: 2.1, tStatistic: 1.7, pValue: 0.1 },
      cohensDz: 0.4,
      itemComparisons: [
        {
          questionId: 101, orderIndex: 11, questionText: 'Which intrinsics do gnolls gain?',
          baselineMean: 60, treatmentMean: 62.5, difference: 2.5,
          pValue: null, adjustedPValue: null, rejectedAtFdr: false, exploratory: true, untestable: true
        }
      ],
      statisticsMethodVersion: 2,
      indexDifference: 2.4,
      indexDifferenceLower: 0.9,
      indexDifferenceUpper: 3.9,
      indexDifferenceMethod: 'crossedBootstrap',
      runVarianceIncluded: true,
      indexDifferenceBootstrapP: 0,
      indexDifferenceStandardError: 0.62,
      hodgesLehmannShift: 2,
      standardizedDifferenceDav: 0.31,
      minimumDetectableIndexDifference: 1.74,
      untestableItemCount: 1,
      pairKeyDifferences: [
        {
          key: 'CandidateSystemPromptSha256', kind: 'Instrument', description: 'The candidate system prompt differs.',
          baselineValue: 'bb19dc24', treatmentValue: '9a7c1e02'
        }
      ],
      priorComparisonCount: 0,
      ...overrides
    };
  }

  beforeEach(async () => {
    created = false;
    serviceMock = {
      getRunGroup: vi.fn().mockName("AdminBenchmarkService.getRunGroup"),
      previewRunGroupTier: vi.fn().mockName("AdminBenchmarkService.previewRunGroupTier"),
      analyseRunGroup: vi.fn().mockName("AdminBenchmarkService.analyseRunGroup"),
      getRunGroupAnalysis: vi.fn().mockName("AdminBenchmarkService.getRunGroupAnalysis"),
      getGroupReportUrl: vi.fn().mockName("AdminBenchmarkService.getGroupReportUrl")
    } as unknown as MockedObject<AdminBenchmarkService>;

    serviceMock.getRunGroup.mockReturnValue(of(buildGroup()));
    serviceMock.previewRunGroupTier.mockReturnValue(of({ accepted: true, comparability: null, group: null }));
    serviceMock.getRunGroupAnalysis.mockReturnValue(of(null));
    serviceMock.analyseRunGroup.mockReturnValue(of(buildAnalysis()));
    serviceMock.getGroupReportUrl.mockImplementation((id: number) => `/api/admin/benchmark/runs/groups/${id}/report`);

    await TestBed.configureTestingModule({
      imports: [GroupAnalysisDialogComponent],
      providers: [{ provide: AdminBenchmarkService, useValue: serviceMock }]
    }).compileComponents();
  });

  afterEach(() => {
    if (created) {
      const dialog = fixture.nativeElement.querySelector('dialog') as HTMLDialogElement | null;
      if (dialog?.open) dialog.close();
    }
    vi.restoreAllMocks();
  });

  function create(groups: BenchmarkRunGroupDto[] = [buildGroup()]): void {
    created = true;
    fixture = TestBed.createComponent(GroupAnalysisDialogComponent);
    component = fixture.componentInstance;
    fixture.componentRef.setInput('groups', groups);
    fixture.componentRef.setInput('suiteName', SUITE_NAME);
    fixture.detectChanges();
  }

  /** Opens the dialog on a group whose stored analysis is `analysis`, as the page does. */
  function openWith(
    analysis: BenchmarkGroupAnalysisDto | null = buildAnalysis(),
    groupOverrides: Partial<BenchmarkRunGroupDto> = {},
    groups?: BenchmarkRunGroupDto[]
  ): BenchmarkRunGroupDto {
    const group = buildGroup(groupOverrides);
    serviceMock.getRunGroup.mockReturnValue(of(group));
    serviceMock.getRunGroupAnalysis.mockReturnValue(of(analysis));
    if (!created) {
      create(groups ?? [group]);
    }
    component.open(group);
    fixture.detectChanges();
    return group;
  }

  function el(): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  function q<T extends HTMLElement = HTMLElement>(selector: string): T {
    const found = el().querySelector<T>(selector);
    expect(found, selector).not.toBeNull();
    return found!;
  }

  function text(selector: string): string {
    const element = el().querySelector(selector) as HTMLElement | null;
    return (element?.textContent || '').replace(/\s+/g, ' ').trim();
  }

  function allText(selector: string): string {
    const elements: HTMLElement[] = Array.from(el().querySelectorAll(selector));
    return elements.map(e => (e.textContent || '')).join(' ').replace(/\s+/g, ' ').trim();
  }

  function click(selector: string): void {
    q(selector).click();
    fixture.detectChanges();
  }

  function showTab(section: string): void {
    click(`#mr-an-tab-${section}`);
  }

  function chooseBaseline(id: number): void {
    const select = q<HTMLSelectElement>('#mr-an-compare-group');
    select.value = String(id);
    select.dispatchEvent(new Event('change'));
    fixture.detectChanges();
  }

  function withWithheld(...runIds: number[]): Partial<BenchmarkRunGroupDto> {
    return {
      members: buildGroup().members.map(member =>
        runIds.includes(member.runId) ? { ...member, indexWithheld: true } : member)
    };
  }

  // =============================================================================================
  // The frame
  // =============================================================================================

  describe('the frame', () => {
    it('should title the dialog with the model and R, and name the group, suite and runs below it', () => {
      openWith();

      const dialog = q<HTMLDialogElement>('dialog.mr-analysis-dialog');
      expect(dialog.open).toBe(true);
      expect(dialog.getAttribute('aria-labelledby')).toBe('mr-an-title');
      expect(text('#mr-an-title .model-identity-name')).toBe('GPT-5.6 Luna');
      expect(text('#mr-an-title')).toContain('R = 3');
      expect(text('.dialog-subtitle')).toBe(`Group #7 · ${SUITE_NAME} · runs #41, #42, #43`);
      expect(text('.mr-tier')).toContain('Tier A');

      const close = q('.dialog-header .btn-icon-action');
      expect(close.getAttribute('aria-label')).toBe('Close group analysis');
      expect(close.getAttribute('title')).toBeNull();
      expect(close.getAttribute('interestfor')).toBe('mr-an-close-tip');
    });

    it('should render the detail inside the dialog and clear it on close', async () => {
      serviceMock.getRunGroupAnalysis.mockReturnValue(of(buildAnalysis()));
      openWith();

      const dialog = q<HTMLDialogElement>('dialog.mr-analysis-dialog');
      expect(dialog.textContent).toContain('Multi-run Intelligence Index');

      const closed = new Promise<void>(resolve => dialog.addEventListener('close', () => resolve(), { once: true }));
      component.close();
      fixture.detectChanges();
      await closed;
      fixture.detectChanges();

      expect(dialog.open).toBe(false);
      expect(component.group).toBeNull();
      expect(dialog.textContent).not.toContain('Multi-run Intelligence Index');
    });

    it('should disable the detail download control until the group has been analysed', () => {
      openWith(null, { latestAnalysisId: null, latestAnalysisAtUtc: null });

      const button = q<HTMLButtonElement>('.mr-download-detail');
      expect(button.getAttribute('aria-disabled')).toBe('true');
      expect(text('#mr-an-download-tip')).toContain('No analysis yet');
      expect(text('#mr-an-panel-summary')).toContain('This group has not been analyzed yet.');

      const openSpy = vi.spyOn(window, 'open').mockReturnValue(null);
      button.click();
      expect(openSpy).not.toHaveBeenCalled();
    });

    it('should download the group report through window.open, with the file-with-arrow glyph', () => {
      openWith();

      const openSpy = vi.spyOn(window, 'open').mockReturnValue(null);
      const button = q<HTMLButtonElement>('.mr-download-detail');
      expect(button.getAttribute('aria-disabled')).toBe('false');
      expect(button.querySelector('svg.btn-icon polyline[points="14 2 14 8 20 8"]')).not.toBeNull();
      button.click();

      expect(serviceMock.getGroupReportUrl).toHaveBeenCalledWith(7);
      expect(openSpy).toHaveBeenCalledWith('/api/admin/benchmark/runs/groups/7/report', '_blank');
    });
  });

  // =============================================================================================
  // The section tabs
  // =============================================================================================

  describe('the section tabs', () => {
    function tabs(): HTMLButtonElement[] {
      return Array.from(el().querySelectorAll<HTMLButtonElement>('[role="tablist"] [role="tab"]'));
    }

    function key(tab: HTMLElement, name: string): void {
      tab.dispatchEvent(new KeyboardEvent('keydown', { key: name, bubbles: true, cancelable: true }));
      fixture.detectChanges();
    }

    function selected(): string | undefined {
      return tabs().find(t => t.getAttribute('aria-selected') === 'true')?.id;
    }

    it('should follow the tab contract: roles, aria-selected, aria-controls, roving tabindex and hidden panels', () => {
      openWith();

      const tablist = q('[role="tablist"]');
      expect(tablist.classList.contains('gh-tabs')).toBe(true);
      expect(tablist.classList.contains('gh-tabs-secondary')).toBe(true);
      expect(tablist.getAttribute('aria-label')).toBe('Analysis sections');

      expect(tabs().map(t => t.textContent?.trim()))
        .toEqual(['Summary', 'Dimensions', 'Items', 'Speed & cost', 'Tokens & tools', 'Comparison']);
      for (const tab of tabs()) {
        expect(tab.getAttribute('type')).toBe('button');
        expect(tab.classList.contains('gh-tab')).toBe(true);
        expect(tab.querySelector('svg')).toBeNull();
        const panel = q(`#${tab.getAttribute('aria-controls')}`);
        expect(panel.getAttribute('role')).toBe('tabpanel');
        expect(panel.getAttribute('aria-labelledby')).toBe(tab.id);
        expect(panel.getAttribute('tabindex')).toBe('0');
      }

      expect(selected()).toBe('mr-an-tab-summary');
      expect(tabs().map(t => t.getAttribute('tabindex'))).toEqual(['0', '-1', '-1', '-1', '-1', '-1']);
      expect(q('#mr-an-panel-summary').hidden).toBe(false);
      for (const id of ['dimensions', 'items', 'speed', 'tokens', 'comparison']) {
        expect(q(`#mr-an-panel-${id}`).hidden).toBe(true);
      }
    });

    it('should move with the arrow keys, wrap, jump with Home and End, and move focus with the selection', () => {
      openWith();

      key(q('#mr-an-tab-summary'), 'ArrowRight');
      expect(selected()).toBe('mr-an-tab-dimensions');
      expect(document.activeElement?.id).toBe('mr-an-tab-dimensions');
      expect(q('#mr-an-tab-dimensions').getAttribute('tabindex')).toBe('0');
      expect(q('#mr-an-tab-summary').getAttribute('tabindex')).toBe('-1');
      expect(q('#mr-an-panel-dimensions').hidden).toBe(false);
      expect(q('#mr-an-panel-summary').hidden).toBe(true);

      key(q('#mr-an-tab-dimensions'), 'End');
      expect(selected()).toBe('mr-an-tab-comparison');
      expect(document.activeElement?.id).toBe('mr-an-tab-comparison');

      key(q('#mr-an-tab-comparison'), 'ArrowRight');
      expect(selected()).toBe('mr-an-tab-summary');

      key(q('#mr-an-tab-summary'), 'ArrowLeft');
      expect(selected()).toBe('mr-an-tab-comparison');

      key(q('#mr-an-tab-comparison'), 'Home');
      expect(selected()).toBe('mr-an-tab-summary');
      expect(document.activeElement?.id).toBe('mr-an-tab-summary');

      showTab('items');
      expect(selected()).toBe('mr-an-tab-items');
      expect(q('#mr-an-panel-items').hidden).toBe(false);
    });

    it('should reopen on the Summary tab', () => {
      openWith();
      showTab('items');

      component.close();
      fixture.detectChanges();
      component.open(buildGroup());
      fixture.detectChanges();

      expect(selected()).toBe('mr-an-tab-summary');
    });
  });

  // =============================================================================================
  // Compare with
  // =============================================================================================

  describe('Compare with', () => {
    const candidates = (): BenchmarkRunGroupDto[] => [
      buildGroup(),
      buildGroup({ id: 9, testedModelDisplayName: 'GPT-5.6 Luna', tier: 'Replicate' }),
      buildGroup({ id: 10, testedModelDisplayName: 'Claude Luna', tier: 'QualityComparable', runCount: 4 }),
      buildGroup({ id: 11, tier: 'CrossCondition' }),
      buildGroup({ id: 12, tier: 'NotComparable' }),
      buildGroup({ id: 13, tier: 'Replicate', benchmarkSuiteId: 6 })
    ];

    it('should list only poolable groups of the same suite, never this group, each labeled with its model, R and tier', () => {
      create(candidates());
      openWith();
      showTab('comparison');

      const options = Array.from(q<HTMLSelectElement>('#mr-an-compare-group').options).map(o => o.textContent?.trim());
      expect(options).toEqual([
        'Choose a group…',
        '#9 · GPT-5.6 Luna · R = 3 · Tier A',
        '#10 · Claude Luna · R = 4 · Tier B'
      ]);
    });

    it('should analyse against the selected baseline group', () => {
      create(candidates());
      openWith();
      const emitted: number[] = [];
      component.analysisChanged.subscribe((id: number) => emitted.push(id));
      showTab('comparison');

      // Nothing chosen yet: Compare refuses.
      expect(q('.mr-an-compare-btn').getAttribute('aria-disabled')).toBe('true');
      click('.mr-an-compare-btn');
      expect(serviceMock.analyseRunGroup).not.toHaveBeenCalled();

      chooseBaseline(9);
      click('.mr-an-compare-btn');

      expect(serviceMock.analyseRunGroup).toHaveBeenCalledWith(7, { compareWithGroupId: 9 });
      expect(emitted).toEqual([7]);
    });

    it('should show a server refusal under the select', () => {
      serviceMock.analyseRunGroup.mockReturnValue(throwError(() => ({ error: 'These groups cannot be paired.' })));
      create(candidates());
      openWith();
      const emitted: number[] = [];
      component.analysisChanged.subscribe((id: number) => emitted.push(id));
      showTab('comparison');

      chooseBaseline(10);
      click('.mr-an-compare-btn');

      const error = q('#mr-an-compare-error');
      expect(error.classList.contains('gh-field-error')).toBe(true);
      expect(error.textContent).toContain('These groups cannot be paired.');
      expect(q('#mr-an-compare-group').getAttribute('aria-describedby')).toContain('mr-an-compare-error');
      expect(emitted).toEqual([]);
    });

    it('should recompute against the stored baseline and tell the page', () => {
      create(candidates());
      openWith(buildAnalysis({ comparedWithGroupId: 9, comparedWithGroupName: 'Baseline' }));
      const emitted: number[] = [];
      component.analysisChanged.subscribe((id: number) => emitted.push(id));

      const recompute = q('.mr-an-recompute');
      expect(recompute.textContent?.trim()).toBe('Recompute analysis');
      expect(recompute.querySelector('svg')).toBeNull();
      click('.mr-an-recompute');

      expect(serviceMock.analyseRunGroup).toHaveBeenCalledWith(7, { compareWithGroupId: 9 });
      expect(emitted).toEqual([7]);
    });
  });

  // =============================================================================================
  // Statistics method 1: every figure rendered as before method 2
  // =============================================================================================

  describe('statistics method 1', () => {
    it('should name statistics method 1 in the provenance of an analysis without a method version', () => {
      openWith();

      expect(component.methodVersion).toBe(1);
      expect(text('.mr-provenance')).toContain('statistics method 1');
    });

    it('should show a member withholding its own index as not computed, with a note under the point estimate', () => {
      openWith(buildAnalysis(), withWithheld(42));

      const members = allText('.mr-member-list li');
      expect(members).toContain('#42');
      expect(members).toContain('Index not computed');
      expect(members).toContain('Index 71');
      expect(allText('.mr-aggregates [role="note"]')).toContain(
        'Run #42 withholds its own index (a question failed at the provider); its per-run index here is over its scored answers.');
    });

    it('should name every analyzed member withholding its own index, ids ascending, and leave out an unanalyzed one', () => {
      const group = withWithheld(43, 41);
      group.members = [...group.members!, {
        runId: 44, startedAtUtc: '2026-09-06T10:10:00Z', status: 'Completed', qualityIndex: null, speedIndex: null,
        testedModelDisplayName: 'GPT-5.6 Luna', shortFingerprint: 'e9b3e9a7', addedAtUtc: '2026-09-06T10:20:00Z', indexWithheld: true
      }];
      openWith(buildAnalysis(), group);

      expect(allText('.mr-aggregates [role="note"]')).toContain(
        'Runs #41, #43 withhold their own index (a question failed at the provider); their per-run indices here are over their scored answers.');
    });

    it('should show no withheld-index note and no "not computed" when no member withholds its index', () => {
      openWith();

      expect(allText('.mr-member-list li')).not.toContain('not computed');
      expect(allText('.mr-aggregates [role="note"]')).not.toContain('withhold');
      expect(component.withheldIndexNote).toBeNull();
    });

    it('should render both interval components with distinct labels, and the item-sampling one with its does-not-shrink note', () => {
      openWith();

      const repro = text('.mr-interval-repro');
      const item = text('.mr-interval-item');

      expect(repro).toContain('Reproducibility standard error');
      expect(repro).toContain('Would a re-run move this?');
      expect(repro).toContain('Shrinks with R');
      expect(text('#mr-an-repro-tip')).toContain('This is the component multi-run exists to measure.');

      expect(item).toContain('Item-sampling standard error');
      expect(item).toContain('Would a different set of questions move this?');
      expect(item).toContain('Does not shrink with R');
      // The reason, not only the claim: without it a reader takes the label for a defect note.
      expect(text('#mr-an-item-sampling-tip')).toContain('Every run answers the same items');
      expect(q('.mr-interval-item app-info-tip button').getAttribute('aria-label')).toBe('About Item-sampling standard error');

      // Two distinct components, not one figure rendered twice.
      expect(repro).not.toBe(item);
      expect(el().querySelectorAll('.mr-interval').length).toBe(2);
    });

    it('should label the combined interval as covering both sources', () => {
      openWith();

      const combined = text('.mr-combined');
      expect(combined).toContain('Combined 95% interval');
      expect(combined).toContain('both');
      expect(combined).toContain('71.3 ± 9.07');
      expect(combined).toContain('(62.2 – 80.4)');
    });

    it('should say the interval covers one source only below three runs', () => {
      const analysis = buildAnalysis();
      analysis.result.index.reproducibilityAvailable = false;
      analysis.result.index.reproducibilityStandardError = null;
      analysis.result.index.runCount = 2;
      openWith(analysis);

      expect(text('.mr-interval-repro')).toContain('Not reported below three runs');
      expect(text('.mr-combined')).toContain('one');
    });

    it('should always render the variance decomposition caveat', () => {
      openWith();

      const caveat = text('.mr-caveat');
      expect(caveat).toContain('What this cannot decompose');
      expect(caveat).toContain('grader stochasticity');
    });

    it('should fall back to its own caveat text when the stored result carries none', () => {
      const analysis = buildAnalysis();
      analysis.result.varianceDecompositionCaveat = null;
      openWith(analysis);

      expect(text('.mr-caveat')).toContain('re-grading identical answers');
    });

    it('should keep the cross-check line in an info tip beside the point estimate', () => {
      openWith();

      expect(text('#mr-an-cross-check-tip')).toContain('Cross-check only');
      expect(q('.mr-point app-info-tip button').getAttribute('aria-label')).toBe('About Stored per-run indices');
    });

    it('should render the per-item table with mean, median, SD, CV, critical-error rate and the stability flag', () => {
      openWith();
      showTab('items');

      const row = text('.mr-item-table tbody tr');
      expect(row).toContain('Q11');
      expect(row).toContain('62.5');   // mean
      expect(row).toContain('63.0');   // median
      expect(row).toContain('17.40');  // sample SD
      expect(row).toContain('27.8%'); // coefficient of variation
      expect(row).toContain('33%');   // critical-error rate
      expect(row).toContain('Unstable');
      expect(text('#mr-an-item-sd-tip')).toContain('Sample SD (n−1)');
    });

    it('should render the speed and cost sections and their degraded flags', () => {
      const analysis = buildAnalysis();
      analysis.result.speed.degraded = true;
      analysis.result.speed.degradedReason = 'Question parallelism differs across members.';
      analysis.result.cost.degraded = true;
      analysis.result.cost.degradedReason = 'The pricing snapshot differs across members.';
      openWith(analysis);
      showTab('speed');

      const panel = allText('.mr-subsection');
      expect(panel).toContain('Speed aggregates degraded');
      expect(panel).toContain('Question parallelism differs across members.');
      expect(panel).toContain('Cost aggregates degraded');
      expect(panel).toContain('The pricing snapshot differs across members.');
      expect(panel).toContain('$7.5900');
      expect(text('#mr-an-speed-percentiles-tip')).toContain('pooled over all 54 answers');
    });

    it('should flag speed and cost as degraded when only the comparability tier says so', () => {
      // The tier reaches the dialog through the read-only preview endpoint, so the test drives it
      // the same way the component does.
      serviceMock.previewRunGroupTier.mockReturnValue(of({
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
      expect(analysis.result.speed.degraded).toBe(false);
      expect(analysis.result.cost.degraded).toBe(false);
      openWith(analysis);

      expect(component.speedDegraded).toBe(true);
      expect(component.costDegraded).toBe(true);

      const panel = allText('.mr-subsection');
      expect(panel).toContain('Speed aggregates degraded');
      expect(panel).toContain('Cost aggregates degraded');
    });

    it('should label per-item differences as exploratory under Benjamini-Hochberg control', () => {
      const analysis = buildAnalysis({
        comparedWithGroupId: 9,
        comparedWithGroupName: 'Suite 5 · GPT-5.6 · baseline · R=3'
      });
      analysis.comparison = buildComparisonV1();
      openWith(analysis);
      showTab('comparison');

      const comparison = text('.mr-comparison');
      expect(comparison).toContain('Per-item differences — exploratory');
      expect(comparison).toContain('Benjamini');
      expect(comparison).toContain('q-value, not a p-value');
      expect(comparison).toContain('Wilcoxon');
      // The primary/secondary ordering is a claim about the method, not a layout choice.
      expect(comparison.indexOf('Wilcoxon p (primary)')).toBeLessThan(comparison.indexOf('Paired t p (secondary)'));
      expect(text('.mr-comparison-table tbody tr')).toContain('exploratory');
      expect(comparison).not.toContain('different rubric revisions');
      expect(text('#mr-an-wilcoxon-tip')).toContain('Wilcoxon signed-rank is the primary test');
      // Method 1 keeps d_z and shows none of method 2's blocks.
      expect(el().querySelector('.mr-an-dz')).not.toBeNull();
      expect(el().querySelector('.mr-an-dav')).toBeNull();
      expect(el().querySelector('.mr-an-primary')).toBeNull();
    });

    it('should say how many items were excluded for a rubric revision mismatch, under the unpaired line', () => {
      const analysis = buildAnalysis({ comparedWithGroupId: 9, comparedWithGroupName: 'Baseline' });
      analysis.comparison = {
        baselineRunIds: [41],
        treatmentRunIds: [51],
        pairedItemCount: 15,
        unpairedItemCount: 1,
        revisionMismatchedItemCount: 2,
        meanDifference: 1.0,
        wilcoxon: { sampleSize: 15, zeroDifferenceCount: 0, statistic: 40, pValue: 0.3, method: 'exact', tiesPresent: false },
        pairedT: { sampleSize: 15, meanDifference: 1.0, tStatistic: 0.9, pValue: 0.38 },
        itemComparisons: [],
        falseDiscoveryRate: 0.05,
        exploratoryNote: null
      };
      openWith(analysis);

      const note = text('.mr-comparison .section-note');
      expect(note).toContain('1 item(s) appear on one side only and are excluded.');
      expect(note).toContain('2 item(s) were graded under different rubric revisions on the two sides and are excluded.');
      expect(note.indexOf('appear on one side only')).toBeLessThan(note.indexOf('different rubric revisions'));
    });

    it('should format cost at four decimals at every magnitude', () => {
      create();
      expect(component.formatCost(2.5311)).toBe('$2.5311');
      expect(component.formatCost(0.0042)).toBe('$0.0042');
      expect(component.formatCost(null)).toBe('—');
      expect(component.formatCost(Number.NaN)).toBe('—');
    });

    it('should render a p-value below the column resolution as a bound rather than as zero', () => {
      create();
      expect(component.formatPValue(0.0000004)).toBe('< 0.001');
      expect(component.formatPValue(0)).toBe('< 0.001');
      expect(component.formatPValue(0.041)).toBe('0.041');
    });

    it('should render the prompt under test, including the divergence from live chat', () => {
      openWith(buildAnalysis({
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
      }));

      const summary = text('#mr-an-panel-summary');
      expect(summary).toContain('Prompt under test');
      expect(summary).toContain('Concise');
      expect(summary).toContain('Gameplay Help');
      expect(summary).toContain('Live chat pre-injects wiki articles');
    });

    it('should say so when the prompt configuration was not recorded', () => {
      openWith(buildAnalysis({
        result: {
          ...(buildAnalysis().result as object),
          promptUnderTest: { recorded: false, divergent: false } as unknown
        }
      }));

      expect(text('#mr-an-panel-summary')).toContain('Not recorded for these runs');
    });

    it('should render the dimension table and name the lowest dimension', () => {
      openWith(buildAnalysis({
        result: {
          ...(buildAnalysis().result as object),
          dimensions: [
            { dimension: 'Accuracy', perRunMeans: [96, 95, 97], mean: 96, standardDeviation: 1, confidenceHalfWidth: 2.5, min: 95, max: 97, itemMeans: { '101': 94 } },
            { dimension: 'Completeness', perRunMeans: [84, 85, 86], mean: 85, standardDeviation: 1, confidenceHalfWidth: 2.5, min: 84, max: 86, itemMeans: { '101': 80 } },
            { dimension: 'Conciseness', perRunMeans: [], mean: null },
            { dimension: 'Readability', perRunMeans: [], mean: null }
          ]
        }
      }));
      showTab('dimensions');

      const panel = text('#mr-an-panel-dimensions');
      expect(panel).toContain('Quality dimensions');
      expect(panel).toContain('Completeness');
      expect(panel).toContain('Lowest dimension:');
      // Q11 is the only item in the fixture, and it is the weakest on both scored dimensions.
      expect(panel).toContain('Q11');
      expect(text('.mr-dimension-table thead')).toContain('95% ±');
      expect(text('.mr-an-dim-interval')).toBe('2.5');
      expect(text('#mr-an-dimension-weighting-tip')).toContain('unweighted');

      // An unscored dimension is dropped from the table rather than rendered as zeroes.
      expect(component.dimensionStats.length).toBe(2);
    });

    it('should render pooled tool families and what the claim verifier bought', () => {
      openWith(buildAnalysis({
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
      }));
      showTab('tokens');

      const panel = text('#mr-an-panel-tokens');
      expect(panel).toContain('Tokens and tool usage');
      expect(panel).toContain('4.21 M');
      expect(panel).toContain('Grader tokens, kept separate');
      expect(text('.mr-an-grader-tokens')).toContain('Assessor 500.0 k in');
      expect(text('#mr-an-grader-tokens-tip')).toContain('never folded into the candidate');
      expect(panel).toContain('SourceCode');
      expect(text('#mr-an-tool-policy-tip')).toContain('_policy.md');
      expect(panel).toContain('10 claims checked across 3 answers');
      expect(text('#mr-an-claim-reading-tip')).toContain('it is what that spend bought');

      // Families are ordered by call count, so the heaviest is first.
      expect(component.toolFamilyRows[0].family).toBe('SourceCode');
    });

    it('should name the role carrying the cost spread', () => {
      openWith(buildAnalysis({
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
      }));
      showTab('speed');

      const panel = text('#mr-an-panel-speed');
      expect(panel).toContain('Per-run totals:');
      expect(panel).toContain('Cost dispersion sits mostly in claimVerifier');
      expect(component.widestCostRole?.role).toBe('claimVerifier');
    });

    it('should show a stored analysis\'s legacy Second opinion cost role as Second reader', () => {
      openWith(buildAnalysis({
        result: {
          ...(buildAnalysis().result as object),
          cost: {
            runCount: 2,
            totalCost: 3,
            meanCostPerRun: 1.5,
            costStandardDeviation: 0.1,
            totalCostByRole: { 'Second opinion': 1, 'Reference reader': 0.5, Assessor: 1.5 },
            meanCostByRole: { 'Second opinion': 0.5, 'Reference reader': 0.25, Assessor: 0.75 },
            perRunTotals: [1.4, 1.6]
          }
        }
      }));

      const roleHeaders = Array.from(el().querySelectorAll<HTMLElement>('.mr-cost-role-table th[scope="row"]'))
        .map(th => (th.textContent ?? '').trim());
      expect(roleHeaders).toContain('Second reader');
      expect(roleHeaders).toContain('Reference reader');
      expect(roleHeaders).not.toContain('Second opinion');
      expect(component.roleLabel('Second opinion')).toBe('Second reader');
      expect(component.roleLabel('Reference reader')).toBe('Reference reader');
    });

    it('should name the questions asked beside the cost per question', () => {
      const base = buildAnalysis().result as { cost: Record<string, unknown> };
      openWith(buildAnalysis({
        result: {
          ...(buildAnalysis().result as object),
          cost: { ...base.cost, questionsAskedPerRun: 18 }
        }
      }));

      const panel = text('#mr-an-panel-speed');
      expect(panel).toContain('over 18 questions asked per run');
      expect(panel).not.toContain('over scored items');
    });

    it('should mark a stored analysis whose cost per question predates the asked count', () => {
      openWith();

      const panel = text('#mr-an-panel-speed');
      expect(panel).toContain('over scored items; recompute for per question asked');
      expect(panel).not.toContain('asked per run');
    });

    it('should mark a combined interval that was truncated at the score bound', () => {
      const base = buildAnalysis().result as { index: Record<string, unknown> };
      openWith(buildAnalysis({
        result: {
          ...(buildAnalysis().result as object),
          index: { ...base.index, combinedUpper: 100, combinedIntervalTruncated: true }
        }
      }));

      const marker = q('.mr-truncated-marker');
      expect(marker.getAttribute('title')).toBeNull();
      expect(text('.mr-combined')).toContain('truncated at the score bound');
      expect(text('#mr-an-panel-summary')).toContain('pinned there');
    });
  });

  // =============================================================================================
  // Statistics method 2
  // =============================================================================================

  describe('statistics method 2', () => {
    function v2Index(overrides: Record<string, unknown>): BenchmarkGroupAnalysisDto {
      const analysis = buildAnalysisV2();
      analysis.result.index = { ...analysis.result.index, ...overrides };
      return analysis;
    }

    it('should show the bootstrap interval with its method line and name statistics method 2', () => {
      openWith(buildAnalysisV2());

      expect(component.methodVersion).toBe(2);
      const combined = text('.mr-combined');
      expect(combined).toContain('Combined 95% interval');
      expect(combined).toContain('63.1 – 79.8');
      expect(combined).toContain('Crossed bootstrap over runs and questions');
      expect(combined).not.toContain('quadrature');
      expect(text('.mr-provenance')).toContain('statistics method 2');

      // The components stay, as diagnostics under their own heading.
      expect(text('.mr-an-made-of')).toBe('What the interval is made of');
      expect(el().querySelectorAll('.mr-interval').length).toBe(2);
      expect(text('.mr-interval-repro')).toContain('Shrinks with R');
      expect(text('.mr-interval-item')).toContain('Does not shrink with R');

      const howTip = text('#mr-an-interval-method-tip');
      expect(howTip).toContain('Owen 2007');
      expect(howTip).toContain('10,000');
      expect(q('.mr-aggregates .mr-an-heading-row app-info-tip button').getAttribute('aria-label'))
        .toBe('About How the interval is computed');
    });

    it('should say the question bootstrap leaves out run-to-run variation below three runs', () => {
      openWith(v2Index({
        runCount: 2,
        reproducibilityAvailable: false,
        reproducibilityStandardError: null,
        combinedIntervalMethod: 'questionBootstrap',
        runsWhereRunComponentMatchesQuestions: null,
        runSpanHours: 0.5
      }));

      expect(text('.mr-an-method-line'))
        .toBe('Question bootstrap — fewer than three runs, so run-to-run variation is not included');
      expect(el().querySelector('.mr-an-planning')).toBeNull();
      // The time-span warning is about the run component, which two runs do not report.
      expect(el().querySelector('.mr-an-span-warning')).toBeNull();
    });

    it('should show the planning line when the server computed it', () => {
      openWith(buildAnalysisV2());

      expect(text('.mr-an-planning')).toBe('From about 5 runs, more questions narrow the interval more than more runs.');
    });

    it('should warn when every run was made within a day', () => {
      openWith(v2Index({ runSpanHours: 1.7 }));

      const warning = q('.mr-an-span-warning');
      expect(warning.classList.contains('gh-field-warning')).toBe(true);
      expect(text('.mr-an-span-warning')).toBe(
        'All runs were made within 2 hours; the run component measures re-running now, not on another day.');
    });

    it('should not warn about the time span when the runs span more than a day', () => {
      openWith(buildAnalysisV2());

      expect(el().querySelector('.mr-an-span-warning')).toBeNull();
    });

    it('should show the dimension bootstrap bounds in the 95% column', () => {
      openWith(buildAnalysisV2());
      showTab('dimensions');

      expect(text('.mr-dimension-table thead')).toContain('95%');
      expect(text('.mr-dimension-table thead')).not.toContain('95% ±');
      expect(text('.mr-an-dim-interval')).toBe('93.8 – 97.9');
      expect(el().querySelector('.mr-an-qb-tag')).toBeNull();
      expect(text('#mr-an-dimension-weighting-tip')).toContain('bootstrap interval');
    });

    it('should tag a dimension interval from the question bootstrap', () => {
      const analysis = buildAnalysisV2();
      analysis.result.dimensions[0].intervalMethod = 'questionBootstrap';
      openWith(analysis);
      showTab('dimensions');

      expect(text('.mr-an-qb-tag')).toBe('Question bootstrap');
    });

    it('should show Too few runs rather than Unstable for an item answered by two runs', () => {
      const analysis = buildAnalysisV2();
      analysis.result.items[0] = { ...analysis.result.items[0], runCount: 2, unstable: true, insufficientRuns: false };
      openWith(analysis);
      showTab('items');

      const row = text('.mr-item-table tbody tr');
      expect(row).toContain('Too few runs');
      expect(row).not.toContain('Unstable');
    });

    it('should still show Unstable for an item answered by three runs', () => {
      openWith(buildAnalysisV2());
      showTab('items');

      expect(text('.mr-item-table tbody tr')).toContain('Unstable');
    });

    it('should render the index difference as the primary result, then the detectable difference and the secondary tests', () => {
      const analysis = buildAnalysisV2({ comparedWithGroupId: 9, comparedWithGroupName: 'Baseline' });
      analysis.comparison = buildComparisonV2();
      openWith(analysis);
      showTab('comparison');

      const primary = text('.mr-an-primary');
      expect(primary).toContain('Index difference');
      expect(primary).toContain('+2.4');
      expect(primary).toContain('95%');
      expect(primary).toContain('+0.9 – +3.9');
      expect(primary).toContain('bootstrap p');
      expect(primary).toContain('< 0.001');
      expect(primary).toContain('Run-to-run variation included');
      expect(primary).not.toContain('not included');
      expect(text('#mr-an-index-difference-tip')).toContain('difficulty-weighted');

      expect(text('.mr-an-mde')).toBe('Smallest reliably detectable difference: ±1.7 points');

      const secondary = text('.mr-an-secondary');
      expect(secondary).toContain('Secondary, question-level tests');
      expect(secondary).toContain('Mean difference (unweighted)');
      expect(secondary).toContain('+2.10');
      expect(secondary).toContain('-0.50 – +4.70');
      expect(secondary).toContain('Wilcoxon p');
      expect(secondary).toContain('Hodges–Lehmann shift');
      expect(secondary).toContain('+2.00');
      expect(secondary).toContain('Paired t p');
      expect(secondary).toContain("Cohen's dav");
      expect(secondary).toContain('0.31');
      expect(text('.mr-comparison')).not.toContain("Cohen's dz");
      expect(el().querySelector('.mr-an-dz')).toBeNull();

      const comparison = text('.mr-comparison');
      expect(comparison.indexOf('What differs between the groups')).toBeLessThan(comparison.indexOf('Index difference'));
      expect(comparison.indexOf('Index difference')).toBeLessThan(comparison.indexOf('Smallest reliably detectable'));
      expect(comparison.indexOf('Smallest reliably detectable')).toBeLessThan(comparison.indexOf('Secondary, question-level tests'));
      expect(comparison.indexOf('Secondary, question-level tests')).toBeLessThan(comparison.indexOf('Per-item differences — exploratory'));
    });

    it('should say run-to-run variation is not included when a side has fewer than three runs', () => {
      const analysis = buildAnalysisV2({ comparedWithGroupId: 9, comparedWithGroupName: 'Baseline' });
      analysis.comparison = buildComparisonV2({ runVarianceIncluded: false, indexDifferenceMethod: 'questionBootstrap' });
      openWith(analysis);

      expect(text('.mr-an-primary')).toContain('Run-to-run variation not included (fewer than three runs on a side)');
    });

    it('should mark an untestable item in the q column and count the untestable items in the note', () => {
      const analysis = buildAnalysisV2({ comparedWithGroupId: 9, comparedWithGroupName: 'Baseline' });
      analysis.comparison = buildComparisonV2();
      openWith(analysis);

      expect(text('.mr-comparison-table tbody tr td:last-child')).toBe('Untestable');
      expect(text('.mr-an-fdr-note')).toContain('1 item(s) are untestable');
    });

    it('should list what differs between the groups and warn when more than one thing does', () => {
      const analysis = buildAnalysisV2({ comparedWithGroupId: 9, comparedWithGroupName: 'Baseline' });
      analysis.comparison = buildComparisonV2();
      openWith(analysis);

      expect(text('.mr-an-differences')).toContain('CandidateSystemPromptSha256');
      expect(text('.mr-an-differences')).toContain('bb19dc24');
      expect(text('.mr-an-differences')).toContain('9a7c1e02');
      expect(el().querySelector('.mr-an-multi-difference')).toBeNull();

      const two = buildAnalysisV2({ comparedWithGroupId: 9, comparedWithGroupName: 'Baseline' });
      two.comparison = buildComparisonV2({
        pairKeyDifferences: [
          { key: 'CandidateSystemPromptSha256', kind: 'Instrument', baselineValue: 'bb19dc24', treatmentValue: '9a7c1e02' },
          { key: 'ToolGuidesSha256', kind: 'Instrument', baselineValue: 'f59d8b30', treatmentValue: '9c1de220' }
        ]
      });
      serviceMock.getRunGroupAnalysis.mockReturnValue(of(two));
      component.open(buildGroup());
      fixture.detectChanges();

      const warning = q('.mr-an-multi-difference');
      expect(warning.classList.contains('gh-field-warning')).toBe(true);
      expect(text('.mr-an-multi-difference'))
        .toBe('More than one thing differs between these groups, so the difference cannot be attributed to one change.');
    });

    it('should say the differences were not recorded when the comparison carries none', () => {
      const analysis = buildAnalysisV2({ comparedWithGroupId: 9, comparedWithGroupName: 'Baseline' });
      analysis.comparison = buildComparisonV2({ pairKeyDifferences: null });
      openWith(analysis);

      expect(text('.mr-an-differences')).toContain('Not recorded for this analysis.');
    });

    it('should warn after two or more earlier comparisons of this group', () => {
      const analysis = buildAnalysisV2({ comparedWithGroupId: 9, comparedWithGroupName: 'Baseline' });
      analysis.comparison = buildComparisonV2({ priorComparisonCount: 2 });
      openWith(analysis);

      const warning = q('.mr-an-prior-comparisons');
      expect(warning.classList.contains('gh-field-warning')).toBe(true);
      expect(text('.mr-an-prior-comparisons')).toBe(
        'This group has been compared 2 times before. A difference found after several looks is weaker evidence; decide what difference matters before looking.');
    });

    it('should not warn after a single earlier comparison', () => {
      const analysis = buildAnalysisV2({ comparedWithGroupId: 9, comparedWithGroupName: 'Baseline' });
      analysis.comparison = buildComparisonV2({ priorComparisonCount: 1 });
      openWith(analysis);

      expect(el().querySelector('.mr-an-prior-comparisons')).toBeNull();
    });
  });
});
