import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';

import { SuiteHealthComponent } from './suite-health.component';
import {
  AdminBenchmarkService,
  BenchmarkItemStatisticsDto,
  BenchmarkSuiteItemAnalysisDto
} from '../../../services/admin-benchmark.service';
import { SystemAiConfigDto } from '../../../services/admin.service';

describe('SuiteHealthComponent', () => {
  let component: SuiteHealthComponent;
  let fixture: ComponentFixture<SuiteHealthComponent>;
  let serviceMock: jasmine.SpyObj<AdminBenchmarkService>;

  function buildItem(overrides: Partial<BenchmarkItemStatisticsDto> = {}): BenchmarkItemStatisticsDto {
    return {
      questionId: 1,
      orderIndex: 1,
      questionText: 'Which intrinsics do gnolls gain?',
      authoredDifficulty: 'Simple',
      itemRevision: 1,
      runCount: 6,
      distinctModelCount: 2,
      distinctAssessorCount: 1,
      distinctScoringMethodVersionCount: 1,
      unknownRevisionCount: 0,
      meanQuality: 70,
      minQuality: 60,
      maxQuality: 80,
      stdDev: 8.2,
      empiricalDifficulty: 30,
      assessedDifficulty: 30,
      difficultyDelta: 0,
      discrimination: 12.5,
      meanToolCalls: 4,
      budgetBoundFraction: 0,
      flags: 0,
      flagNames: [],
      confounded: false,
      insufficientData: false,
      ...overrides
    };
  }

  function buildAnalysis(overrides: Partial<BenchmarkSuiteItemAnalysisDto> = {}): BenchmarkSuiteItemAnalysisDto {
    return {
      suiteId: 5,
      suiteName: 'GnollHack Player Assistance Benchmark Suite',
      questionCount: 1,
      runCount: 6,
      distinctModelCount: 2,
      distinctAssessorCount: 1,
      distinctScoringMethodVersionCount: 1,
      linkedAnswerCount: 6,
      unlinkedAnswerCount: 0,
      minRunsForMeasurement: 4,
      minRunsForDiscrimination: 4,
      items: [buildItem()],
      ...overrides
    };
  }

  beforeEach(async () => {
    serviceMock = jasmine.createSpyObj('AdminBenchmarkService', [
      'getItemAnalysis', 'getRubricGaps', 'validateCitations', 'analyzeCoverage'
    ]);
    serviceMock.getItemAnalysis.and.returnValue(of(buildAnalysis()));
    serviceMock.getRubricGaps.and.returnValue(of({ suiteId: 5, runCount: 0, claimCount: 0, clusters: [] }));

    await TestBed.configureTestingModule({
      imports: [SuiteHealthComponent],
      providers: [{ provide: AdminBenchmarkService, useValue: serviceMock }]
    }).compileComponents();

    fixture = TestBed.createComponent(SuiteHealthComponent);
    component = fixture.componentInstance;
  });

  function open(analysis?: BenchmarkSuiteItemAnalysisDto): void {
    if (analysis) serviceMock.getItemAnalysis.and.returnValue(of(analysis));
    component.suiteId = 5;
    component.ngOnChanges({ suiteId: { previousValue: null, currentValue: 5, firstChange: true, isFirstChange: () => true } });
    fixture.detectChanges();
  }

  /**
   * A benchmark-capable configuration for the Coverage tab's model selector. Only the fields the
   * selector reads are meaningful; the rest of SystemAiConfigDto is quota bookkeeping the panel
   * never touches, so the seed is a partial cast rather than forty irrelevant zeroes.
   */
  function buildConfig(overrides: Partial<SystemAiConfigDto> = {}): SystemAiConfigDto {
    return {
      id: 1,
      displayName: 'Claude Opus 5',
      provider: 'Anthropic',
      modelId: 'claude-opus-5',
      thinkingLevel: 'high',
      reasoningMode: null,
      parallelExecutionMode: 2,
      ...overrides
    } as SystemAiConfigDto;
  }

  function bannerText(): string {
    const headings: HTMLElement[] = Array.from(fixture.nativeElement.querySelectorAll('.alert-heading'));
    const heading = headings.find(h => (h.textContent || '').includes('Sample and confounds'));
    return ((heading?.parentElement?.querySelector('.alert-body') as HTMLElement)?.textContent || '')
      .replace(/\s+/g, ' ').trim();
  }

  it('should load the item analysis and the rubric gaps when a suite is selected', () => {
    open();

    expect(serviceMock.getItemAnalysis).toHaveBeenCalledWith(5);
    expect(serviceMock.getRubricGaps).toHaveBeenCalledWith(5);
    expect(component.itemAnalysis?.items.length).toBe(1);
  });

  it('should state the sample size and both mixes in the banner', () => {
    open();

    const text = bannerText();
    expect(text).toContain('6 stored run(s)');
    expect(text).toContain('2 model(s)');
    expect(text).toContain('1 assessor(s)');
    expect(text).toContain('1 scoring method version(s)');
  });

  it('should warn that nothing is a measurement below the run floor', () => {
    open(buildAnalysis({
      runCount: 1,
      items: [buildItem({ runCount: 1, discrimination: null, insufficientData: true })]
    }));

    expect(component.itemsAdvisory).toBeTrue();
    expect(bannerText()).toContain('Below 4 runs, nothing in this table is a measurement.');
  });

  it('should read "insufficient data" for discrimination below four runs', () => {
    open(buildAnalysis({
      runCount: 1,
      items: [buildItem({ runCount: 1, discrimination: null, insufficientData: true })]
    }));

    expect(component.discriminationLabel(component.itemAnalysis!.items[0]))
      .toBe('insufficient data (< 4 runs)');
    expect(fixture.nativeElement.textContent).toContain('insufficient data');
  });

  it('should name the assessor mix and the scoring-method mix when either exceeds one', () => {
    open(buildAnalysis({ distinctAssessorCount: 2, distinctScoringMethodVersionCount: 2 }));

    expect(component.assessorMixed).toBeTrue();
    expect(component.scoringMethodMixed).toBeTrue();

    const text = bannerText();
    expect(text).toContain('Assessor mix:');
    expect(text).toContain('Scoring method mix:');
  });

  it('should say how many answers were excluded for having no question link', () => {
    open(buildAnalysis({ linkedAnswerCount: 10, unlinkedAnswerCount: 8 }));

    expect(bannerText()).toContain('8 of 18 stored answer(s) could not be tied to a question');
  });

  it('should always state that empirical difficulty is reported and never applied', () => {
    open();

    // The non-writeback rule is the load-bearing one in this panel, so it is in the banner
    // rather than only in the docs.
    expect(bannerText()).toContain('Empirical difficulty is reported, never applied');
  });

  it('should render each row with its own sample size and both confound counts', () => {
    open();

    const row = fixture.nativeElement.querySelector('.item-analysis-table tbody tr') as HTMLElement;
    expect(row.textContent).toContain('6 run(s) / 2 model(s) / 1 assessor(s) / 1 scoring method(s)');
  });

  it('should badge a row\'s flags and mark a confounded row', () => {
    open(buildAnalysis({
      items: [buildItem({ flags: 48, flagNames: ['AssessorConfounded', 'ScoringMethodMixed'], confounded: true })]
    }));

    const badges: HTMLElement[] = Array.from(fixture.nativeElement.querySelectorAll('.item-flag'));
    expect(badges.map(b => b.textContent?.trim())).toEqual(['AssessorConfounded', 'ScoringMethodMixed']);
    expect(fixture.nativeElement.querySelector('.item-analysis-table tbody tr.is-confounded')).toBeTruthy();
  });

  it('should summarise the suite as counts only', () => {
    open(buildAnalysis({
      questionCount: 3,
      items: [
        buildItem({ questionId: 1, runCount: 6 }),
        buildItem({ questionId: 2, runCount: 1, insufficientData: true, flags: 16, flagNames: ['AssessorConfounded'], confounded: true }),
        buildItem({ questionId: 3, runCount: 0 })
      ]
    }));

    expect(component.itemsWithRunsCount).toBe(2);
    expect(component.itemsBelowFloorCount).toBe(2);
    expect(component.flaggedItemCount).toBe(1);
    expect(component.confoundedItemCount).toBe(1);

    // Counts only. A suite-level mean rendered this large would be read before the advisory
    // banner that says the figures are not measurements, which is the mistake this panel's
    // design exists to prevent.
    const stats = fixture.nativeElement.querySelector('.sh-stats') as HTMLElement;
    expect(stats).toBeTruthy();
    expect(stats.textContent || '').not.toMatch(/mean|average|index/i);
  });

  it('should reload the stored reports on refresh, and never the paid ones', () => {
    open();
    expect(serviceMock.getItemAnalysis).toHaveBeenCalledTimes(1);

    component.refreshActiveTab();
    expect(serviceMock.getItemAnalysis).toHaveBeenCalledTimes(2);

    component.selectTab('gaps');
    component.refreshActiveTab();
    expect(serviceMock.getRubricGaps).toHaveBeenCalledTimes(2);

    // Citations and Coverage cost an index scan and AI tokens respectively, so a generic
    // Refresh must neither offer itself nor fire them.
    component.selectTab('citations');
    expect(component.canRefreshActiveTab).toBeFalse();
    component.refreshActiveTab();
    expect(serviceMock.validateCitations).not.toHaveBeenCalled();

    component.selectTab('coverage');
    expect(component.canRefreshActiveTab).toBeFalse();
    component.refreshActiveTab();
    expect(serviceMock.analyzeCoverage).not.toHaveBeenCalled();
  });

  it('should let a keyboard user reach the panel from the tab row', () => {
    open();

    const panels: HTMLElement[] = Array.from(fixture.nativeElement.querySelectorAll('[role="tabpanel"]'));
    expect(panels.length).toBe(1);
    expect(panels.every(p => p.getAttribute('tabindex') === '0')).toBeTrue();

    // The active tab's aria-controls has to resolve to the panel that is actually rendered.
    const activeTab = fixture.nativeElement.querySelector('[role="tab"][aria-selected="true"]') as HTMLElement;
    expect(activeTab.getAttribute('aria-controls')).toBe(panels[0].id);
  });

  it('should explain a flag with a hint popover rather than a title attribute', () => {
    open(buildAnalysis({
      items: [buildItem({ flags: 16, flagNames: ['AssessorConfounded'], confounded: true })]
    }));

    const badge = fixture.nativeElement.querySelector('.item-flag') as HTMLElement;
    // title is unstyleable and never appears on keyboard focus, so it is not used here.
    expect(badge.getAttribute('title')).toBeNull();
    expect(badge.getAttribute('interestfor')).toBe('sh-tip-flag-1-AssessorConfounded');

    const tip = fixture.nativeElement.querySelector('#sh-tip-flag-1-AssessorConfounded') as HTMLElement;
    expect(tip.getAttribute('popover')).toBe('hint');
    expect(tip.textContent).toContain('More than one assessor graded these runs');
  });

  it('should surface an item-analysis failure without breaking the panel', () => {
    serviceMock.getItemAnalysis.and.returnValue(throwError(() => ({ error: 'boom' })));
    open();

    expect(component.itemsError).toBe('boom');
    expect(component.loadingItems).toBeFalse();
  });

  it('should expose no write action anywhere in the panel', () => {
    // The panel reports; it never edits. Its only outward action asks the host to open a
    // question, and in particular there is no control that copies empirical difficulty into
    // assessed difficulty ÔÇö that number weights the Intelligence Index.
    open();

    const labels: string[] = Array.from(fixture.nativeElement.querySelectorAll('button'))
      .map(b => ((b as HTMLElement).textContent || '').trim().toLowerCase());

    expect(labels).toContain('edit question');
    expect(labels.some(l => l.includes('apply') || l.includes('save') || l.includes('re-rate') || l.includes('write'))).toBeFalse();
  });

  it('should emit the question id rather than editing anything itself', () => {
    open();
    const emitted: number[] = [];
    component.editQuestionRequested.subscribe((id: number) => emitted.push(id));

    component.editQuestion(42);

    expect(emitted).toEqual([42]);
  });

  it('should validate citations only when asked, and say when the index is not ready', () => {
    open();
    expect(serviceMock.validateCitations).not.toHaveBeenCalled();

    serviceMock.validateCitations.and.returnValue(of({
      suiteId: 5,
      unresolvedCount: 1,
      notValidatedCount: 1,
      sourceIndexReady: false,
      questions: [{
        questionId: 1,
        orderIndex: 1,
        unresolvedCount: 1,
        notValidatedCount: 1,
        hasNoCitations: false,
        citations: [
          { kind: 'SourceFile', value: 'src/gone.c', status: 'Unresolved', lineNumber: 1217 },
          { kind: 'WikiArticle', value: 'How GnollHack differs from NetHack', status: 'NotValidated', lineNumber: null }
        ]
      }]
    }));

    component.selectTab('citations');
    component.validateCitations();
    fixture.detectChanges();

    expect(serviceMock.validateCitations).toHaveBeenCalledWith(5);
    const text = (fixture.nativeElement.textContent || '').replace(/\s+/g, ' ');
    expect(text).toContain('The source index is still building');
    expect(text).toContain('src/gone.c');
    expect(text).toContain('line 1217 (not validated)');
  });

  it('should label a cross-family cluster as a rubric gap and a single-family one as a hallucination', () => {
    expect(component.verdictLabel('LikelyRubricGap')).toBe('Likely rubric gap');
    expect(component.verdictLabel('LikelyHallucination')).toBe('Likely hallucination');
    expect(component.verdictTitle('LikelyRubricGap')).toContain('two or more independent model families');
    expect(component.verdictTitle('LikelyHallucination')).toContain('not a suite issue');
  });

  it('should run the coverage analysis only with a model selected', () => {
    open();
    component.coverageModelConfigId = null;
    component.analyzeCoverage();
    expect(serviceMock.analyzeCoverage).not.toHaveBeenCalled();

    serviceMock.analyzeCoverage.and.returnValue(of({
      suiteId: 5,
      suiteName: 'Suite',
      questionCount: 18,
      analysisModelConfigurationId: 3,
      analysisModelDisplayNameUsed: 'Claude Opus 5',
      analysisModelProviderUsed: 'Anthropic',
      analysisModelIdUsed: 'claude-opus-5',
      analysisModelThinkingLevelUsed: 'high',
      analyzedAtUtc: '2026-09-04T08:00:00Z',
      inputTokens: 100,
      outputTokens: 200,
      durationMs: 4000,
      gaps: [{ subsystem: 'Polymorph control', sourceLocation: 'src/polyself.c', rationale: 'Untested.', suggestedBand: 'Advanced' }],
      comment: 'Coverage is broad but shallow on transformation mechanics.'
    }));

    component.coverageModelConfigId = 3;
    component.selectTab('coverage');
    component.analyzeCoverage();
    fixture.detectChanges();

    expect(serviceMock.analyzeCoverage).toHaveBeenCalledWith(5, 3);
    const text = (fixture.nativeElement.textContent || '').replace(/\s+/g, ' ');
    expect(text).toContain('Polymorph control');
    expect(text).toContain('src/polyself.c');
    // The report discloses the model that produced it, as a difficulty rating does.
    expect(text).toContain('Claude Opus 5');
  });

  it('should pick the coverage analysis model from the badge dropdown', () => {
    open();
    component.benchmarkCapableConfigs = [
      buildConfig(),
      buildConfig({ id: 2, displayName: 'Gemini 3 Pro', provider: 'Google', modelId: 'gemini-3-pro', thinkingLevel: 'low' })
    ];
    component.selectTab('coverage');
    fixture.detectChanges();

    const trigger = fixture.nativeElement.querySelector('.coverage-model-selector .selector-trigger') as HTMLElement;
    expect(trigger).withContext('the Coverage tab renders the shared model selector').toBeTruthy();

    trigger.click();
    fixture.detectChanges();
    expect(component.isCoverageModelDropdownOpen).toBeTrue();

    const options: HTMLElement[] = Array.from(
      fixture.nativeElement.querySelectorAll('.coverage-model-selector .model-option'));
    expect(options.length).toBe(2);

    options[options.length - 1].click();
    fixture.detectChanges();

    expect(component.isCoverageModelDropdownOpen).toBeFalse();
    expect(component.coverageModelConfigId)
      .toBe(component.benchmarkCapableConfigs[component.benchmarkCapableConfigs.length - 1].id);

    // The badges are what distinguishes this selector from the plain <select> it replaced.
    expect(trigger.querySelector('.provider-badge')?.textContent?.trim()).toBe('Google');
    expect(trigger.querySelector('.thinking-badge')?.textContent?.trim()).toBe('Low');
  });

  it('should move between tabs with the arrow keys', () => {
    open();

    component.onTabKeydown(new KeyboardEvent('keydown', { key: 'ArrowRight' }), 0);
    expect(component.activeTab).toBe('gaps');

    component.onTabKeydown(new KeyboardEvent('keydown', { key: 'End' }), 1);
    expect(component.activeTab).toBe('coverage');

    component.onTabKeydown(new KeyboardEvent('keydown', { key: 'ArrowRight' }), 3);
    expect(component.activeTab).toBe('items');
  });

  it('should give every tab the roles and state a tab widget needs', () => {
    open();

    const tabs: HTMLElement[] = Array.from(fixture.nativeElement.querySelectorAll('[role="tab"]'));
    expect(tabs.length).toBe(4);
    expect(tabs[0].getAttribute('aria-selected')).toBe('true');
    expect(tabs[0].getAttribute('tabindex')).toBe('0');
    expect(tabs[1].getAttribute('aria-selected')).toBe('false');
    expect(tabs[1].getAttribute('tabindex')).toBe('-1');
    expect(tabs.every(t => !!t.getAttribute('aria-controls'))).toBeTrue();

    const list = fixture.nativeElement.querySelector('[role="tablist"]') as HTMLElement;
    expect(list.getAttribute('aria-label')).toBe('Suite health sections');
  });
});
