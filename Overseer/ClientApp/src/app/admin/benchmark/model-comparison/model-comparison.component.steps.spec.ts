import { ComponentFixture } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { ModelComparisonComponent } from './model-comparison.component';
import { BenchmarkModelComparisonEntryDto } from './model-comparison.models';
import { HttpTestingController } from '@angular/common/http/testing';
import { BehaviorSubject } from 'rxjs';
import { SystemAlert } from '../../../services/admin-alert.service';
import { BenchmarkReportAudience, BenchmarkReportDocumentListItemDto } from '../../../services/admin-benchmark.service';
import {
  DEFAULT_CHART_SELECTION,
  REPORT_CHART_STORAGE_KEY,
  ReportChartPublishResult,
  ReportChartPublisher
} from '../report-pack/report-charts';
import { DownloadCenterPanelComponent } from '../download-center/download-center-panel.component';
import {
  ReportPackPanelStubComponent, buildEntry, buildExcludedEntry, buildDto, render, comparableSet, textOf,
  setUpModelComparisonSpec
} from './model-comparison.component.testing';

describe('ModelComparisonComponent', () => {
  let component: ModelComparisonComponent;
  let fixture: ComponentFixture<ModelComparisonComponent>;
  let http: HttpTestingController;
  /** The system alerts the wizard reads chart storage from. */
  let alerts: BehaviorSubject<SystemAlert[]>;

  setUpModelComparisonSpec({
    get component() { return component; },
    set component(value) { component = value; },
    get fixture() { return fixture; },
    set fixture(value) { fixture = value; },
    get http() { return http; },
    set http(value) { http = value; },
    get alerts() { return alerts; },
    set alerts(value) { alerts = value; }
  }, { stubReportPackPanel: true });

  describe('Steps 3 and 4', () => {
    /** Step 3's panel: the stub the TestBed puts in place of the real one. */
    function reportPanel(): ReportPackPanelStubComponent | null {
      return fixture.debugElement.query(By.directive(ReportPackPanelStubComponent))?.componentInstance ?? null;
    }

    function documentsPanel(): DownloadCenterPanelComponent | null {
      return fixture.debugElement.query(By.directive(DownloadCenterPanelComponent))?.componentInstance ?? null;
    }

    /** Answers step 4's one list request. */
    function flushDocuments(documents: BenchmarkReportDocumentListItemDto[]): void {
      const request = http.expectOne(r => r.method === 'GET' && r.url === '/api/admin/benchmark/report-documents');
      expect(request.request.params.get('comparison')).toBe(component.comparison!.entries.map(e => e.key).join(','));
      expect(request.request.params.get('origin')).toBe('reportPack');
      request.flush(documents);
      fixture.detectChanges();
    }

    function reportDocument(id: number, overrides: Partial<BenchmarkReportDocumentListItemDto> = {}): BenchmarkReportDocumentListItemDto {
      return {
        id,
        packId: 'pack-1',
        audience: BenchmarkReportAudience.ExecutiveSummary,
        title: `Executive Summary ${id}`,
        subjectKey: 'run:1',
        subjectLabel: 'Model 1',
        subjectRunIds: [1],
        suiteId: 5,
        suiteName: 'GnollHack Player Assistance Benchmark Suite',
        writerDisplayName: 'Claude writer',
        writerProvider: 'Anthropic',
        writerModelId: 'claude',
        writerThinkingLevel: null,
        sameProviderAcknowledged: false,
        status: 'Completed',
        reportFormatVersion: 1,
        createdAtUtc: `2026-09-${10 + (id % 20)}T12:00:00Z`,
        inputTokens: 0,
        outputTokens: 0,
        durationMs: 0,
        costUsd: 0.01,
        runChangedSinceGeneration: false,
        missingRunIds: [],
        allowedDisclosures: [1, 3],
        origin: 1,
        comparisonKey: 'cmp',
        comparisonEntryCount: 3,
        peerCount: 2,
        pricingBasis: 'Current',
        peerLetters: { 'run:2': 'A', 'run:3': 'B' },
        ...overrides
      };
    }

    function publishResult(overrides: Partial<ReportChartPublishResult> = {}): ReportChartPublishResult {
      return { published: [], failed: [], skipped: [], canceled: false, storageNotConfigured: null, ...overrides };
    }

    async function until(condition: () => boolean): Promise<void> {
      for (let i = 0; i < 200 && !condition(); i++) {
        await new Promise(resolve => setTimeout(resolve, 0));
      }
    }

    afterEach(() => {
      (fixture.nativeElement as HTMLElement).querySelectorAll('dialog').forEach(dialog => {
        if (dialog.open) {
          dialog.close();
        }
      });
    });

    it('has four steps: Sources, Charts & table, Reports, Documents', () => {
      render(buildDto(comparableSet(3)));

      expect(component.steps).toEqual([1, 2, 3, 4]);
      const tabs = fixture.debugElement.queryAll(By.css('.mc-wizard-steps .gh-tab'))
        .map(tab => (tab.nativeElement as HTMLElement).textContent?.trim());
      expect(tabs).toEqual(['1. Sources', '2. Charts & table', '3. Reports', '4. Documents']);
      expect(textOf('.mc-wizard-position')).toContain('Step 2 of 4 — Charts & table');
    });

    it('reaches steps 3 and 4 once a comparison exists, and step 3 only with a measured model, saying why', () => {
      render(null, 1);
      expect(component.isStepReachable(3)).toBe(false);
      expect(component.isStepReachable(4)).toBe(false);
      expect(component.stepBlockedReason(4)).toBe('Compare the selected sources first.');

      render(buildDto([
        buildExcludedEntry('run:8', ['ScoringMethodVersion']),
        buildExcludedEntry('run:9', ['CandidatePromptOptions'])
      ]), 2);
      expect(component.isStepReachable(3)).toBe(false);
      expect(component.isStepReachable(4)).toBe(true);
      const tab3 = fixture.debugElement.query(By.css('#mc-step-tab-3')).nativeElement as HTMLElement;
      expect(tab3.getAttribute('aria-disabled')).toBe('true');
      expect(textOf(`#${tab3.getAttribute('aria-describedby')}`)).toContain('measured differently');
      component.goToStep(3);
      expect(component.step).toBe(2);
    });

    it('keeps step 3 closed with one comparable model, because a comparison report needs a peer', () => {
      render(buildDto([comparableSet(1)[0], buildExcludedEntry('run:9', ['ScoringMethodVersion'])]), 2);

      expect(component.hasComparisonPeers).toBe(false);
      expect(component.isStepReachable(3)).toBe(false);
      expect(component.isStepReachable(4)).toBe(true);
      expect(component.stepBlockedReason(3)).toBe(
        'A comparison report compares one model with at least one other. Add another model on step 1, '
        + 'or write a run\'s or battery run\'s own reports in the AI Reports tab of its report.');
      const tab3 = fixture.debugElement.query(By.css('#mc-step-tab-3')).nativeElement as HTMLElement;
      expect(tab3.getAttribute('aria-disabled')).toBe('true');
      expect(textOf(`#${tab3.getAttribute('aria-describedby')}`)).toContain('AI Reports tab of its report');
      component.goToStep(3);
      expect(component.step).toBe(2);

      render(buildDto(comparableSet(2)), 2);
      expect(component.hasComparisonPeers).toBe(true);
      expect(component.isStepReachable(3)).toBe(true);
      expect(component.stepBlockedReason(3)).toBe('');
    });

    it('runs Next from 2 to 3 to 4, where it is Close, and past step 3 when step 3 cannot open', () => {
      render(buildDto(comparableSet(3)), 2);
      expect(component.nextLabel).toBe('Next');

      component.nextStep();
      fixture.detectChanges();
      expect(component.step).toBe(3);
      expect(component.nextLabel).toBe('Next');
      component.nextStep();
      fixture.detectChanges();
      expect(component.step).toBe(4);
      flushDocuments([]);
      expect(component.nextLabel).toBe('Close');
      expect(textOf('.mc-wizard-position')).toContain('Step 4 of 4 — Documents');
      const closed: number[] = [];
      component.closeRequested.subscribe(() => closed.push(1));
      component.nextStep();
      expect(closed.length).toBe(1);
      component.previousStep();
      expect(component.step).toBe(3);

      render(buildDto([buildExcludedEntry('run:8', ['ScoringMethodVersion'])]), 2);
      component.nextStep();
      expect(component.step).toBe(4);
      component.previousStep();
      expect(component.step).toBe(2);
    });

    it('has no Reports button, and keeps About and Recompute on step 2 only', () => {
      render(buildDto(comparableSet(3)), 2);
      expect(fixture.debugElement.query(By.css('#mc-reports-trigger'))).toBeNull();
      expect(fixture.debugElement.query(By.css('#mc-about-trigger'))).not.toBeNull();

      component.goToStep(3);
      fixture.detectChanges();
      expect(fixture.debugElement.query(By.css('#mc-about-trigger'))).toBeNull();
      expect(fixture.debugElement.query(By.css('.mc-recompute'))).toBeNull();
    });

    it('creates the step 3 and 4 panels on their first visit and keeps them, with their state, across steps', () => {
      render(buildDto(comparableSet(3)), 2);
      expect(reportPanel()).toBeNull();
      expect(documentsPanel()).toBeNull();

      component.goToStep(3);
      fixture.detectChanges();
      const panel = reportPanel()!;
      expect(panel).not.toBeNull();
      const field = (fixture.nativeElement as HTMLElement).querySelector<HTMLInputElement>('.rp-stub-field')!;
      field.value = 'a form value';

      component.goToStep(4);
      fixture.detectChanges();
      const documents = documentsPanel()!;
      flushDocuments(Array.from({ length: 12 }, (_, i) => reportDocument(20 + i)));
      documents.table.setPage(2, documents.rows);
      documents.clearSelection();
      const kept = documents.rows[0];
      documents.stateOf(kept).selected = true;
      fixture.detectChanges();

      component.goToStep(2);
      fixture.detectChanges();
      const step3 = fixture.debugElement.query(By.css('#mc-step-panel-3')).nativeElement as HTMLElement;
      const step4 = fixture.debugElement.query(By.css('#mc-step-panel-4')).nativeElement as HTMLElement;
      expect(step3.hidden).toBe(true);
      expect(step4.hidden).toBe(true);
      expect(getComputedStyle(step3).display).toBe('none');

      component.goToStep(3);
      fixture.detectChanges();
      expect(reportPanel()).toBe(panel);
      expect((fixture.nativeElement as HTMLElement).querySelector<HTMLInputElement>('.rp-stub-field')).toBe(field);
      expect(field.value).toBe('a form value');

      component.goToStep(4);
      fixture.detectChanges();
      http.expectNone(r => r.url === '/api/admin/benchmark/report-documents');
      expect(documentsPanel()).toBe(documents);
      expect(documents.table.page).toBe(2);
      expect(documents.selectedCount).toBe(1);
      expect(documents.isIncluded(kept)).toBe(true);
      expect(step4.hidden).toBe(false);
    });

    it('hands step 3 the comparison, the chart selection, the figures it can draw and the advisory', () => {
      const group = buildEntry({ key: 'group:4', sourceKind: 'Group', sourceId: 4, runIds: [7, 8], label: 'Group 4' });
      const excluded = buildExcludedEntry('run:9', ['ScoringMethodVersion']);
      const entries = [...comparableSet(2), group, { ...excluded, sourceId: 9, runIds: [9] }];
      render(buildDto(entries, { pricingBasis: 'AsRun' }), 3);

      const panel = reportPanel()!;
      expect(panel.context).toEqual({
        runIds: [1, 2, 9],
        groupIds: [4],
        pricingBasis: 'AsRun',
        entries,
        // Every entry, the Excluded one included: the set runIds and groupIds carry.
        entryKeys: ['run:1', 'run:2', 'group:4', 'run:9'],
        suiteId: 5,
        suiteName: 'GnollHack Player Assistance Benchmark Suite'
      });
      // One object while the comparison stays: the panel sees no new context on every check.
      const context = panel.context;
      fixture.detectChanges();
      expect(panel.context).toBe(context);
      expect(panel.chartSelection).toEqual(DEFAULT_CHART_SELECTION);
      expect(panel.chartsAvailable).toEqual(['p1a-quality', 'p1b-speed', 'p1c-cost', 'p2-profile', 's1-quality-speed', 's2-quality-cost', 's3-speed-cost']);
      // The default theme is dark, which prints badly: the advisory says so.
      expect(panel.chartAdvisory).toContain('dark theme');
      expect(panel.chartStorageMissing).toBe(false);

      render(buildDto(comparableSet(2)), 3);
      expect(reportPanel()!.chartsAvailable).not.toContain('p2-profile');
    });

    it('says the chart advisory only for a dark theme, or light text on a transparent background', () => {
      render(buildDto(comparableSet(3)), 2);
      const appearance = component.figureStyle.appearance;

      component.onFigureStyleChange({ ...component.figureStyle, appearance: { ...appearance, theme: 'light' } });
      expect(component.chartAdvisory).toBeNull();

      component.onFigureStyleChange({
        ...component.figureStyle,
        appearance: { ...appearance, theme: 'light', background: 'transparent', textColor: '#f0f0f0' }
      });
      expect(component.chartAdvisory).toContain('transparent background with light text');

      component.onFigureStyleChange({
        ...component.figureStyle,
        appearance: { ...appearance, theme: 'light', background: 'transparent', textColor: '#1a1a1a' }
      });
      expect(component.chartAdvisory).toBeNull();
    });

    it('marks chart storage missing from the system alert', () => {
      render(buildDto(comparableSet(3)), 3);
      expect(reportPanel()!.chartStorageMissing).toBe(false);

      alerts.next([{ id: 'report-charts-location-missing', type: 'warning', message: 'Chart storage is not configured.' }]);
      fixture.detectChanges();

      expect(component.chartStorageMissing).toBe(true);
      expect(reportPanel()!.chartStorageMissing).toBe(true);
    });

    it('charts a written document with the selection for its type, and reports the result to step 3', async () => {
      const publish = vi.spyOn(ReportChartPublisher.prototype, 'publish').mockResolvedValue(publishResult({ published: [{ documentId: 41, chartCount: 4 }] }));
      render(buildDto(comparableSet(3)), 3);
      const token = component.documentsReloadToken;

      reportPanel()!.documentWritten.emit({
        audience: BenchmarkReportAudience.ExecutiveSummary, status: 'Completed', documentId: 41, errorMessage: null, modelCalls: 1
      });
      expect(component.chartStatus[41]).toEqual({ state: 'attaching' });
      http.expectOne('/api/admin/benchmark/report-documents/41').flush(reportDocument(41));
      await until(() => component.chartStatus[41]?.state === 'done');

      expect(publish).toHaveBeenCalledTimes(1);
      const [targets, selection, composer, hash] = vi.mocked(publish).mock.lastCall!;
      expect(targets).toEqual([{
        documentId: 41,
        audience: BenchmarkReportAudience.ExecutiveSummary,
        subjectKey: 'run:1',
        peerLetters: { 'run:2': 'A', 'run:3': 'B' },
        label: 'Executive Summary 41'
      }]);
      expect(selection).toEqual(DEFAULT_CHART_SELECTION);
      expect(typeof composer).toBe('function');
      expect(hash).toMatch(/^[0-9a-f]{64}$/);
      expect(component.chartStatus[41]).toEqual({ state: 'done', count: 4 });
      fixture.detectChanges();
      expect(reportPanel()!.chartStatus[41]).toEqual({ state: 'done', count: 4 });
      expect(component.documentsReloadToken).toBe(token + 1);
    });

    it('draws nothing for a written document whose type has no chart chosen', () => {
      const publish = vi.spyOn(ReportChartPublisher.prototype, 'publish').mockResolvedValue(publishResult());
      render(buildDto(comparableSet(3)), 3);
      reportPanel()!.chartSelectionChange.emit({ ...DEFAULT_CHART_SELECTION, [BenchmarkReportAudience.ExecutiveSummary]: [] });
      expect(JSON.parse(localStorage.getItem(REPORT_CHART_STORAGE_KEY)!)).toBeTruthy();

      reportPanel()!.documentWritten.emit({
        audience: BenchmarkReportAudience.ExecutiveSummary, status: 'Completed', documentId: 41, errorMessage: null, modelCalls: 1
      });

      http.expectNone('/api/admin/benchmark/report-documents/41');
      expect(publish).not.toHaveBeenCalled();
      expect(component.chartStatus[41]).toBeUndefined();
    });

    it('stops at chart storage that is not configured, and says so', async () => {
      vi.spyOn(ReportChartPublisher.prototype, 'publish').mockResolvedValue(publishResult({ storageNotConfigured: 'Chart storage is not configured.' }));
      render(buildDto(comparableSet(3)), 3);

      reportPanel()!.chartRetryRequested.emit({
        audience: BenchmarkReportAudience.ExecutiveSummary, status: 'Completed', documentId: 41, errorMessage: null, modelCalls: 1
      });
      http.expectOne('/api/admin/benchmark/report-documents/41').flush(reportDocument(41));
      await until(() => component.chartStatus[41]?.state === 'failed');

      expect(component.chartStatus[41]).toEqual({ state: 'failed', message: 'Chart storage is not configured.' });
      expect(component.chartStorageMissing).toBe(true);
      expect(component.documentChartActions!.storageMissing).toBe(true);
    });

    it('opens step 4 and lists its documents again when step 3 asks for them or its job finishes', () => {
      render(buildDto(comparableSet(3)), 3);
      const token = component.documentsReloadToken;

      reportPanel()!.jobFinished.emit({});
      expect(component.documentsReloadToken).toBe(token + 1);
      reportPanel()!.documentsRequested.emit();
      fixture.detectChanges();

      expect(component.step).toBe(4);
      expect(component.documentsReloadToken).toBe(token + 2);
      flushDocuments([reportDocument(21)]);
      const panel = documentsPanel()!;
      expect(panel.context).toEqual(expect.objectContaining({
        kind: 'library',
        preselect: 'all',
        subtitle: 'Run reports, and each run\'s or battery run\'s own AI reports, are in that report\'s Downloads.'
      }));
      expect(panel.chartActions).toBe(component.documentChartActions);
      expect(panel.rows.map(row => row.key)).toContain('doc:21');
      const note = fixture.nativeElement.querySelector('#mc-step-panel-4 .mc-documents-note') as HTMLElement | null;
      expect(note?.textContent?.trim()).toBe(
        'Run reports, and each run\'s or battery run\'s own AI reports, are in that report\'s Downloads.');
    });

    it('chooses on step 4 only the documents the last finished job wrote', () => {
      render(buildDto(comparableSet(3)), 3);
      const before = component.documentsContext;

      reportPanel()!.jobFinished.emit({
        documents: [
          { audience: BenchmarkReportAudience.ExecutiveSummary, status: 'Completed', documentId: 22, errorMessage: null, modelCalls: 1 },
          { audience: BenchmarkReportAudience.TechnicalReport, status: 'Failed', documentId: null, errorMessage: 'x', modelCalls: 1 }
        ]
      });
      const context = component.documentsContext;
      expect(context).not.toBe(before);
      expect(context).toEqual(expect.objectContaining({ kind: 'library', preselect: { ids: [22] } }));
      expect(component.documentsContext, 'one object per entry set and ids').toBe(context);

      reportPanel()!.documentsRequested.emit();
      fixture.detectChanges();
      flushDocuments([reportDocument(21), reportDocument(22)]);
      const panel = documentsPanel()!;
      const chosen = panel.rows.filter(row => panel.isIncluded(row)).map(row => row.key);
      expect(chosen).toEqual(['doc:22']);
    });

    it('keeps every row chosen when the finished job wrote nothing', () => {
      render(buildDto(comparableSet(3)), 3);

      reportPanel()!.jobFinished.emit({ documents: [] });

      expect(component.documentsContext).toEqual(expect.objectContaining({ preselect: 'all' }));
    });

    it('lends step 4 chart actions that match only this comparison\'s documents on its prices', () => {
      render(buildDto(comparableSet(3)), 2);
      const actions = component.documentChartActions!;

      expect(actions.pricingBasis).toBe('Current');
      expect(actions.comparisonKeyMatches(reportDocument(1))).toBe(true);
      expect(actions.comparisonKeyMatches(reportDocument(1, { comparisonKey: null }))).toBe(false);
      expect(actions.comparisonKeyMatches(reportDocument(1, { subjectKey: 'run:7' }))).toBe(false);
      expect(actions.comparisonKeyMatches(reportDocument(1, { comparisonEntryCount: 4 }))).toBe(false);
      expect(actions.comparisonKeyMatches(reportDocument(1, { peerLetters: { 'run:5': 'A' } }))).toBe(false);
    });

    describe('document charts', () => {
      /** A subject and two peers with names, providers and model ids of their own. */
      function namedSet(): BenchmarkModelComparisonEntryDto[] {
        const base = comparableSet(3);
        const names: [string, string, string][] = [
          ['Gemini Orchard', 'Google', 'gemini-orchard'],
          ['Claude Harbor', 'Anthropic', 'claude-harbor'],
          ['GPT Lantern', 'OpenAI', 'gpt-lantern']
        ];
        return base.map((entry, index) => ({
          ...entry, label: names[index][0], modelDisplayName: names[index][0], provider: names[index][1], modelId: names[index][2]
        }));
      }

      it('resolves the bar orientation from the document layout, not from the page', () => {
        render(buildDto(namedSet()), 2);
        component.applyContainerWidth(2000);
        expect(component.orientation).toBe('vertical');

        // 1800 px at 175 % text composes about 549 layout px wide: below the 720 px breakpoint.
        expect(component.documentChartOrientation('p1a-quality')).toBe('horizontal');

        component.onFigureStyleChange({ ...component.figureStyle, bar: { ...component.figureStyle.bar, orientation: 'vertical' } });
        expect(component.documentChartOrientation('p1a-quality')).toBe('vertical');
        expect(component.orientation).toBe('vertical');
      });

      it('composes a named chart at the document size with its caption and alt text', async () => {
        render(buildDto(namedSet()), 2);

        const chart = await component.composeReportChart('p1a-quality', { kind: 'named' });

        expect(chart.widthPx).toBe(1800);
        expect(chart.heightPx).toBe(1125);
        expect(chart.png.type).toBe('image/png');
        expect(chart.png.size).toBeGreaterThan(0);
        expect(chart.caption).toContain('Drawn from the comparison computed 2026-09-07 12:00 UTC.');
        expect(chart.altText.startsWith(`${chart.title}. `)).toBe(true);
        expect(chart.altText).toContain('Claude Harbor (medium): Intelligence Index 53.0 ± 6.4');
        expect(chart.altText).toContain('Gemini Orchard (medium)');
      });

      it('composes an anonymized chart that names the subject and letters every peer', async () => {
        render(buildDto(namedSet()), 2);

        const chart = await component.composeReportChart('p1a-quality', {
          kind: 'anonymized', subjectKey: 'run:1', letters: { 'run:2': 'A', 'run:3': 'B' }
        });

        expect(chart.widthPx).toBe(1800);
        expect(chart.altText).toContain('Gemini Orchard (medium)');
        expect(chart.altText).toContain('Model A (medium)');
        expect(chart.altText).toContain('Model B (medium)');
        for (const peer of ['Claude Harbor', 'GPT Lantern', 'claude-harbor', 'gpt-lantern']) {
          expect(chart.altText, peer).not.toContain(peer);
          expect(chart.caption, peer).not.toContain(peer);
        }
      });

      it('refuses a figure the comparison plots too few models for', async () => {
        render(buildDto(namedSet().slice(0, 2)), 2);

        await expect(component.composeReportChart('p2-profile', { kind: 'named' })).rejects.toThrowError(/too few models/);
      });
    });
  });

  it('drives the step tablist with a roving tabindex and the arrow keys', () => {
    render(buildDto(comparableSet(3)));

    const tabs = fixture.debugElement.queryAll(By.css('.mc-wizard-steps .gh-tab'))
      .map(tab => tab.nativeElement as HTMLElement);
    expect(tabs.length).toBe(4);
    expect(tabs[1].getAttribute('aria-selected')).toBe('true');
    expect(tabs[1].getAttribute('tabindex')).toBe('0');
    expect(tabs[0].getAttribute('tabindex')).toBe('-1');

    component.onStepKeydown(new KeyboardEvent('keydown', { key: 'ArrowLeft' }), 1);
    fixture.detectChanges();
    expect(component.step).toBe(1);

    component.onStepKeydown(new KeyboardEvent('keydown', { key: 'End' }), 0);
    fixture.detectChanges();
    expect(component.step).toBe(4);

    component.onStepKeydown(new KeyboardEvent('keydown', { key: 'ArrowLeft' }), 3);
    fixture.detectChanges();
    expect(component.step).toBe(3);
  });
});
