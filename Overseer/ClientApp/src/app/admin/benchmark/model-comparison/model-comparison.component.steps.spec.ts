import { ComponentFixture } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { ModelComparisonComponent } from './model-comparison.component';
import { BenchmarkModelComparisonEntryDto } from './model-comparison.models';
import { HttpTestingController } from '@angular/common/http/testing';
import { BehaviorSubject } from 'rxjs';
import { SystemAlert } from '../../../services/admin-alert.service';
import {
  BenchmarkComparisonDto,
  BenchmarkReportAudience,
  BenchmarkReportDocumentListItemDto,
  BenchmarkReportScope
} from '../../../services/admin-benchmark.service';
import {
  DEFAULT_CHART_LAYOUT_SETTINGS,
  DEFAULT_CHART_SELECTION,
  DEFAULT_DOCUMENT_CHART_LAYOUT,
  REPORT_CHART_STORAGE_KEY,
  ReportChartDocumentLayout,
  ReportChartPublishResult,
  ReportChartPublisher,
  documentTextStyle,
  printFigureAppearance
} from '../report-pack/report-charts';
import { resolveFigureTheme } from './figure-theme';
import type { ResolvedFigureTheme } from './figure-theme';
import type { FigureChrome } from './figure-chrome';
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
      // The documents draw Light, for print by default, so step 2's dark default theme never reaches them.
      expect(panel.chartAdvisory).toBeNull();
      // A document type whose charts take step 2's theme prints the dark one badly: the advisory says so.
      component.onChartLayoutChange({
        ...DEFAULT_CHART_LAYOUT_SETTINGS,
        [BenchmarkReportAudience.InternalBrief]: { ...DEFAULT_DOCUMENT_CHART_LAYOUT, theme: 'asInStep2' }
      });
      fixture.detectChanges();
      expect(panel.chartAdvisory).toContain('dark theme');
      expect(panel.chartStorageMissing).toBe(false);

      render(buildDto(comparableSet(2)), 3);
      expect(reportPanel()!.chartsAvailable).not.toContain('p2-profile');
    });

    it('says the chart advisory only for a dark theme, or light text on a transparent background', () => {
      render(buildDto(comparableSet(3)), 2);
      const appearance = component.figureStyle.appearance;
      expect(component.chartAdvisory, 'Light, for print: step 2\'s theme does not reach the documents').toBeNull();
      component.onChartLayoutChange({
        ...DEFAULT_CHART_LAYOUT_SETTINGS,
        [BenchmarkReportAudience.ExecutiveSummary]: { ...DEFAULT_DOCUMENT_CHART_LAYOUT, theme: 'asInStep2' }
      });
      expect(component.chartAdvisory).toContain('dark theme');

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
      const publish = vi.spyOn(ReportChartPublisher.prototype, 'publish').mockResolvedValue(publishResult({ published: [{ documentId: 41, chartCount: 4, figureCount: 2 }] }));
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
      // The layout the server places the charts by goes with them.
      expect(vi.mocked(publish).mock.lastCall![5]).toEqual(DEFAULT_CHART_LAYOUT_SETTINGS);
      expect(component.chartStatus[41]).toEqual({ state: 'done', count: 2, images: 4 });
      fixture.detectChanges();
      expect(reportPanel()!.chartStatus[41]).toEqual({ state: 'done', count: 2, images: 4 });
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

        // As in step 2, under step 2's Automatic: a full column at 8 pt labels composes about 663 layout
        // px wide, below the 720 px breakpoint.
        expect(component.documentChartOrientation('p1a-quality')).toBe('horizontal');

        component.onFigureStyleChange({ ...component.figureStyle, bar: { ...component.figureStyle.bar, orientation: 'vertical' } });
        expect(component.documentChartOrientation('p1a-quality')).toBe('vertical');
        expect(component.orientation).toBe('vertical');
      });

      it('takes the document type\'s own orientation over step 2\'s, and resolves Automatic at its label size', () => {
        render(buildDto(namedSet()), 2);
        const { ExecutiveSummary, TechnicalReport } = BenchmarkReportAudience;
        component.onChartLayoutChange({
          ...DEFAULT_CHART_LAYOUT_SETTINGS,
          [ExecutiveSummary]: { ...DEFAULT_DOCUMENT_CHART_LAYOUT, orientation: 'vertical' },
          // 481.9 pt × 11 / 7 pt = 757 layout px: at or above the breakpoint.
          [TechnicalReport]: { ...DEFAULT_DOCUMENT_CHART_LAYOUT, labelPt: 7 }
        });

        expect(component.figureStyle.bar.orientation).toBe('auto');
        expect(component.documentChartOrientation('p1a-quality', ExecutiveSummary)).toBe('vertical');
        expect(component.documentChartOrientation('p1a-quality', TechnicalReport)).toBe('vertical');
        expect(component.documentChartOrientation('p1a-quality', BenchmarkReportAudience.InternalBrief)).toBe('horizontal');
      });

      it('composes a named chart at the document size with its caption and alt text', async () => {
        render(buildDto(namedSet()), 2);

        const chart = await component.composeReportChart('p1a-quality', { kind: 'named' });

        // A full A4 column at 300 dpi, 16:10.
        expect(chart.widthPx).toBe(2008);
        expect(chart.heightPx).toBe(1255);
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

        expect(chart.widthPx).toBe(2008);
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

      it('composes a figure at its document type\'s width', async () => {
        render(buildDto(namedSet()), 2);
        const { ExecutiveSummary } = BenchmarkReportAudience;
        component.onChartLayoutChange({
          ...DEFAULT_CHART_LAYOUT_SETTINGS,
          [ExecutiveSummary]: { ...DEFAULT_DOCUMENT_CHART_LAYOUT, widths: { 'p1a-quality': 'twoThirds' } }
        });

        const chart = await component.composeReportChart('p1a-quality', { kind: 'named' }, ExecutiveSummary);

        // Two thirds of the A4 column, 321.3 pt, at 300 dpi; 4:3 at least, taller where the chrome needs it.
        expect(chart.widthPx).toBe(1339);
        expect(chart.heightPx).toBeGreaterThanOrEqual(1004);
      });

      it('composes a half-width figure at 8 pt, never refused for its height', async () => {
        render(buildDto(namedSet()), 2);
        const { InternalBrief } = BenchmarkReportAudience;
        component.onChartLayoutChange({
          ...DEFAULT_CHART_LAYOUT_SETTINGS,
          [InternalBrief]: { ...DEFAULT_DOCUMENT_CHART_LAYOUT, heading: 'titleAndBadges', widths: { 'p1a-quality': 'half', 's1-quality-speed': 'half' } }
        });

        for (const key of ['p1a-quality', 's1-quality-speed'] as const) {
          const chart = await component.composeReportChart(key, { kind: 'named' }, InternalBrief);
          // Half the A4 column, 241.0 pt, at 300 dpi; square at least.
          expect(chart.widthPx, key).toBe(1004);
          expect(chart.heightPx, key).toBeGreaterThanOrEqual(1004);
        }
      });

      it('draws Light, for print in the light theme, and step 2\'s theme As in step 2', () => {
        render(buildDto(namedSet()), 2);
        const internals = component as unknown as {
          documentLook(layout: ReportChartDocumentLayout): { theme: ResolvedFigureTheme; style: { appearance: unknown } };
        };

        const print = internals.documentLook(DEFAULT_DOCUMENT_CHART_LAYOUT);
        expect(print.style.appearance).toEqual(printFigureAppearance(component.figureStyle.appearance));
        expect(print.theme).toEqual(resolveFigureTheme(printFigureAppearance(component.figureStyle.appearance)));

        const step2 = internals.documentLook({ ...DEFAULT_DOCUMENT_CHART_LAYOUT, theme: 'asInStep2' });
        expect(step2.theme).toBe(component.figureTheme);
        expect(step2.style).toEqual(documentTextStyle(component.figureStyle));
      });

      it('leaves the heading to the caption, or keeps the title alone, or all of it, as the layout says', () => {
        render(buildDto(namedSet()), 2);
        const card = component.scatterCards[0];
        const internals = component as unknown as {
          documentChrome(card: unknown, notes: unknown[], layout: ReportChartDocumentLayout, theme: ResolvedFigureTheme, logo: null):
            { chrome: FigureChrome; footer: { suite: string; computedAt: string }; theme: ResolvedFigureTheme; logo: unknown };
        };
        const theme = resolveFigureTheme(printFigureAppearance(component.figureStyle.appearance));
        const chromeFor = (heading: ReportChartDocumentLayout['heading']) =>
          internals.documentChrome(card, [], { ...DEFAULT_DOCUMENT_CHART_LAYOUT, heading }, theme, null);

        const none = chromeFor('none');
        expect(none.chrome.title).toBe('');
        expect(none.chrome.badges).toEqual([]);
        expect(none.chrome.direction).toBeUndefined();
        expect(none.chrome.detail).toBe('');
        expect(none.chrome.key).toEqual(card.chrome.key);
        expect(none.theme).toBe(theme);
        expect(none.logo).toBeNull();
        // The document names the suite and the time; the chart carries no footer of its own.
        expect(none.footer).toEqual({ suite: '', computedAt: '' });

        const title = chromeFor('title');
        expect(title.chrome.title).toBe(card.chrome.title);
        expect(title.chrome.badges).toEqual([]);
        expect(title.footer).toEqual({ suite: '', computedAt: '' });

        const all = chromeFor('titleAndBadges');
        expect(all.chrome.title).toBe(card.chrome.title);
        expect(all.chrome.badges).toEqual(card.chrome.badges);
        expect(all.chrome.direction).toEqual(card.chrome.direction);
        expect(all.footer.suite).not.toBe('');
        expect(all.footer.computedAt).not.toBe('');
      });

      it('notes the measure a speed or cost chart plots where the document\'s tables give another', async () => {
        render(buildDto(namedSet()), 2);
        expect(component.speedMeasure).toBe('meanModelTime');

        const speed = await component.composeReportChart('p1b-speed', { kind: 'named' });
        const intelligence = await component.composeReportChart('p1a-quality', { kind: 'named' });

        expect(speed.caption).toContain('Times are the mean model time per question; the document\'s tables give the median. Drawn from');
        expect(intelligence.caption).not.toContain('Times are');
        expect(intelligence.caption).not.toContain('Costs are');
      });
      it('draws a comparison-scope document\'s named chart over its covered entries only', async () => {
        render(buildDto(namedSet()), 2);

        const chart = await component.composeReportChart('p1a-quality', { kind: 'named', coveredKeys: ['run:1', 'run:2'] });

        expect(chart.altText).toContain('Gemini Orchard (medium)');
        expect(chart.altText).toContain('Claude Harbor (medium)');
        expect(chart.altText).not.toContain('GPT Lantern');
        expect(chart.caption).not.toContain('GPT Lantern');
      });

      it('letters every covered entry of a comparison-scope document\'s anonymized chart, and drops the rest', async () => {
        render(buildDto(namedSet()), 2);

        const chart = await component.composeReportChart('p1a-quality', {
          kind: 'anonymized', subjectKey: 'comparison:12', letters: { 'run:1': 'A', 'run:3': 'B' }, coveredKeys: ['run:1', 'run:3']
        });

        expect(chart.altText).toContain('Model A (medium)');
        expect(chart.altText).toContain('Model B (medium)');
        for (const name of ['Gemini Orchard', 'Claude Harbor', 'GPT Lantern', 'gemini-orchard', 'claude-harbor', 'gpt-lantern']) {
          expect(chart.altText, name).not.toContain(name);
          expect(chart.caption, name).not.toContain(name);
        }
      });
    });

    describe('comparison identity', () => {
      const IDENTIFY_URL = '/api/admin/benchmark/model-comparisons/identify';

      function identity(overrides: Partial<BenchmarkComparisonDto> = {}): BenchmarkComparisonDto {
        return {
          id: 12,
          name: 'Model 1 vs Model 2 vs Model 3',
          customName: null,
          defaultName: 'Model 1 vs Model 2 vs Model 3',
          entryCount: 3,
          subjectKind: 'Runs',
          entryKeys: ['run:1', 'run:2', 'run:3'],
          createdAtUtc: '2026-10-06T10:00:00Z',
          renamedAtUtc: null,
          ...overrides
        };
      }

      function identityLine(): HTMLElement | null {
        return (fixture.nativeElement as HTMLElement).querySelector('.mc-wizard-header .mc-identity');
      }

      function renameButton(): HTMLButtonElement {
        return (fixture.nativeElement as HTMLElement).querySelector<HTMLButtonElement>('#mc-rename-comparison')!;
      }

      it('numbers the comparison after Compare and shows Comparison #12 — name with Rename comparison', () => {
        render(buildDto(comparableSet(3)), 2);

        const request = http.expectOne(IDENTIFY_URL);
        expect(request.request.method).toBe('POST');
        expect(request.request.body).toEqual({ runIds: [1, 2, 3], groupIds: [], batteryRunIds: [] });
        expect(identityLine()).toBeNull();
        request.flush(identity());
        fixture.detectChanges();

        expect(identityLine()!.querySelector('.mc-identity-name')!.textContent!.trim()).toBe('Comparison #12 — Model 1 vs Model 2 vs Model 3');
        const button = renameButton();
        expect(button.classList).toContain('action-btn');
        expect(button.getAttribute('aria-label')).toBe('Rename comparison #12');
        expect(button.getAttribute('title')).toBeNull();
        const tipId = button.getAttribute('interestfor')!;
        const tip = (fixture.nativeElement as HTMLElement).querySelector(`#${tipId}`)!;
        expect(tip.getAttribute('popover')).toBe('hint');
        expect(tip.textContent!.trim()).toBe('Rename comparison');
        expect(button.getAttribute('style')).toContain(`anchor-name: --${tipId}`);
        expect(component.currentComparison).toEqual({ id: 12, name: 'Model 1 vs Model 2 vs Model 3', entryKeys: ['run:1', 'run:2', 'run:3'] });
      });

      it('asks again only for another entry set, and drops an answer for an older one', () => {
        const group = buildEntry({ key: 'group:4', sourceKind: 'Group', sourceId: 4, runIds: [7, 8], label: 'Group 4' });
        render(buildDto(comparableSet(3)), 2);
        const first = http.expectOne(IDENTIFY_URL);
        first.flush(identity());

        // A recompute of the same entries (another pricing basis) keeps the identity.
        render(buildDto(comparableSet(3), { pricingBasis: 'AsRun' }), 2);
        http.expectNone(IDENTIFY_URL);
        expect(component.currentComparison?.id).toBe(12);

        render(buildDto([...comparableSet(2), group]), 2);
        const second = http.expectOne(IDENTIFY_URL);
        expect(second.request.body).toEqual({ runIds: [1, 2], groupIds: [4], batteryRunIds: [] });
        expect(component.currentComparison, 'no number while the new set is being identified').toBeNull();
        expect(identityLine()).toBeNull();

        render(buildDto(comparableSet(3)), 2);
        const third = http.expectOne(IDENTIFY_URL);
        expect(second.cancelled, 'the request for the older set is dropped').toBe(true);
        third.flush(identity());
        expect(component.currentComparison?.id).toBe(12);
      });

      it('never blocks the wizard when identify fails: the header omits the number', () => {
        render(buildDto(comparableSet(3)), 2);
        http.expectOne(IDENTIFY_URL).flush({ error: 'No.' }, { status: 500, statusText: 'Server Error' });
        fixture.detectChanges();

        expect(identityLine()).toBeNull();
        expect(component.comparisonIdentity).toBeNull();
        expect(component.isStepReachable(3)).toBe(true);
        component.goToStep(3);
        expect(component.step).toBe(3);
      });

      it('renames the comparison in the nested dialog, and the header and steps 3 and 4 take the new name', async () => {
        render(buildDto(comparableSet(3)), 2);
        http.expectOne(IDENTIFY_URL).flush(identity());
        fixture.detectChanges();

        expect((fixture.nativeElement as HTMLElement).querySelector('dialog.mc-rename-dialog')).toBeNull();
        renameButton().click();
        fixture.detectChanges();
        const dialog = (fixture.nativeElement as HTMLElement).querySelector<HTMLDialogElement>('dialog.mc-rename-dialog')!;
        expect(dialog.open).toBe(true);
        const input = dialog.querySelector<HTMLInputElement>('#mc-rename-name')!;
        expect(input.value).toBe('Model 1 vs Model 2 vs Model 3');
        input.value = 'Flagships, October';
        input.dispatchEvent(new Event('input'));
        dialog.querySelector<HTMLButtonElement>('.mc-rename-save')!.click();
        fixture.detectChanges();

        const patch = http.expectOne('/api/admin/benchmark/model-comparisons/12');
        expect(patch.request.method).toBe('PATCH');
        expect(patch.request.body).toEqual({ name: 'Flagships, October' });
        patch.flush(identity({ name: 'Flagships, October', customName: 'Flagships, October', renamedAtUtc: '2026-10-06T11:00:00Z' }));
        fixture.detectChanges();

        expect(dialog.open).toBe(false);
        expect(identityLine()!.textContent).toContain('Comparison #12 — Flagships, October');
        expect(component.currentComparison).toEqual({ id: 12, name: 'Flagships, October', entryKeys: ['run:1', 'run:2', 'run:3'] });
        // The close event is queued after close(): focus returns to Rename comparison once it fires.
        await until(() => document.activeElement === renameButton());
        expect(document.activeElement).toBe(renameButton());
        // Rendered only while open: once closed, the wizard holds no rename dialog.
        fixture.detectChanges();
        expect((fixture.nativeElement as HTMLElement).querySelector('dialog.mc-rename-dialog')).toBeNull();
      });

      it('lists step 4 by the comparison\'s number, and by its entries for documents written before, under its number and name', () => {
        render(buildDto(comparableSet(3)), 2);
        http.expectOne(IDENTIFY_URL).flush(identity());
        component.goToStep(4);
        fixture.detectChanges();

        const entryKeys = component.comparison!.entries.map(entry => entry.key);
        const lists = http.match(r => r.method === 'GET' && r.url === '/api/admin/benchmark/report-documents');
        expect(lists.length).toBe(2);
        const byNumber = lists.find(list => list.request.params.get('comparisonId') === '12')!;
        const byEntries = lists.find(list => list.request.params.has('comparison'))!;
        expect(byNumber.request.params.get('origin')).toBe('reportPack');
        expect(byEntries.request.params.get('comparison')).toBe(entryKeys.join(','));
        byNumber.flush([reportDocument(21, { comparisonId: 12, comparisonName: 'Model 1 vs Model 2 vs Model 3' })]);
        byEntries.flush([reportDocument(21, { comparisonId: 12 }), reportDocument(22, { comparisonId: null })]);
        fixture.detectChanges();

        const panel = documentsPanel()!;
        expect(panel.context).toEqual(expect.objectContaining({
          kind: 'library',
          scope: { kind: 'comparison', comparisonId: 12, name: 'Model 1 vs Model 2 vs Model 3', entryKeys }
        }));
        expect(panel.rows.map(row => row.key).sort()).toEqual(['doc:21', 'doc:22']);
        const heading = (): string =>
          ((fixture.nativeElement as HTMLElement).querySelector('#mc-step-panel-4 #mc-dc-documents-title')?.textContent ?? '').trim();
        expect(heading()).toBe('Documents of Comparison #12 — Model 1 vs Model 2 vs Model 3');
        expect(panel.facets.map(facet => facet.column)).not.toContain('comparison');

        // A rename changes the heading; step 4 keeps its rows and lists nothing again.
        renameButton().click();
        fixture.detectChanges();
        const dialog = (fixture.nativeElement as HTMLElement).querySelector<HTMLDialogElement>('dialog.mc-rename-dialog')!;
        const input = dialog.querySelector<HTMLInputElement>('#mc-rename-name')!;
        input.value = 'Flagships, October';
        input.dispatchEvent(new Event('input'));
        dialog.querySelector<HTMLButtonElement>('.mc-rename-save')!.click();
        fixture.detectChanges();
        http.expectOne('/api/admin/benchmark/model-comparisons/12')
          .flush(identity({ name: 'Flagships, October', customName: 'Flagships, October', renamedAtUtc: '2026-10-06T11:00:00Z' }));
        fixture.detectChanges();

        http.expectNone(r => r.url === '/api/admin/benchmark/report-documents');
        expect(heading()).toBe('Documents of Comparison #12 — Flagships, October');
        expect(documentsPanel()).toBe(panel);
        expect(panel.rows.map(row => row.key).sort()).toEqual(['doc:21', 'doc:22']);
      });

      it('feeds step 3 the number, the chart layout and the composer, and follows its layout and deletes', async () => {
        render(buildDto(comparableSet(3)), 3);
        const panel = reportPanel()!;
        expect(panel.comparisonId).toBeNull();
        http.expectOne(IDENTIFY_URL).flush(identity());
        fixture.detectChanges();
        expect(panel.comparisonId).toBe(12);
        expect(panel.chartLayout).toBe(component.chartLayout);

        // The layout step 3 changes is the wizard's, stored with the selection.
        const layout = {
          ...DEFAULT_CHART_LAYOUT_SETTINGS,
          [BenchmarkReportAudience.ExecutiveSummary]: { ...DEFAULT_DOCUMENT_CHART_LAYOUT, labelPt: 9 }
        };
        panel.chartLayoutChange.emit(layout);
        fixture.detectChanges();
        expect(component.chartLayout).toBe(layout);
        expect(panel.chartLayout).toBe(layout);
        expect(JSON.parse(localStorage.getItem(REPORT_CHART_STORAGE_KEY)!).layout[BenchmarkReportAudience.ExecutiveSummary].labelPt).toBe(9);

        // Preview layout composes through the wizard's own composeDocumentCharts.
        const compose = vi.spyOn(component, 'composeDocumentCharts').mockResolvedValue({ charts: [], failed: [], layout: { version: 1, figures: [], maxHeightShare: 0.6 } });
        const composer = panel.documentChartsComposer as (audience: BenchmarkReportAudience, variant: unknown, scope: unknown) => Promise<unknown>;
        await composer(BenchmarkReportAudience.TechnicalReport, { kind: 'named', coveredKeys: ['run:1', 'run:2'] }, 'comparison');
        expect(compose).toHaveBeenCalledWith(BenchmarkReportAudience.TechnicalReport, { kind: 'named', coveredKeys: ['run:1', 'run:2'] }, 'comparison');

        // A delete on step 3 makes step 4 list afresh.
        const token = component.documentsReloadToken;
        panel.documentsChanged.emit();
        expect(component.documentsReloadToken).toBe(token + 1);
      });

      it('matches a document to the comparison by its number, and a legacy one by its entries', () => {
        render(buildDto(comparableSet(3)), 2);
        http.expectOne(IDENTIFY_URL).flush(identity());
        const actions = component.documentChartActions!;

        expect(actions.comparisonKeyMatches(reportDocument(1, { comparisonId: 12 }))).toBe(true);
        // Numbered for another comparison: no match, though its entries would match.
        expect(actions.comparisonKeyMatches(reportDocument(1, { comparisonId: 13 }))).toBe(false);
        // A comparison-scope document of this comparison.
        expect(actions.comparisonKeyMatches(reportDocument(1, {
          comparisonId: 12, scope: BenchmarkReportScope.Comparison, subjectKey: 'comparison:12', peerLetters: { 'run:1': 'A', 'run:2': 'B', 'run:3': 'C' }
        }))).toBe(true);
        // A legacy document without a number: by its entry set, as before.
        expect(actions.comparisonKeyMatches(reportDocument(1, { comparisonId: null }))).toBe(true);
        expect(actions.comparisonKeyMatches(reportDocument(1, { comparisonId: null, comparisonEntryCount: 4 }))).toBe(false);
      });

      it('charts a written comparison-scope document over its covered entries', async () => {
        const publish = vi.spyOn(ReportChartPublisher.prototype, 'publish').mockResolvedValue(publishResult({ published: [{ documentId: 51, chartCount: 2, figureCount: 1 }] }));
        render(buildDto(comparableSet(3)), 3);
        http.expectOne(IDENTIFY_URL).flush(identity());

        reportPanel()!.documentWritten.emit({
          audience: BenchmarkReportAudience.ExecutiveSummary, status: 'Completed', documentId: 51, errorMessage: null, modelCalls: 1
        });
        http.expectOne('/api/admin/benchmark/report-documents/51').flush(reportDocument(51, {
          comparisonId: 12,
          scope: BenchmarkReportScope.Comparison,
          subjectKey: 'comparison:12/0123456789abcdef',
          peerCount: 0,
          peerLetters: { 'run:1': 'A', 'run:3': 'B' },
          coveredModels: [
            { entryKey: 'run:1', label: 'Model 1', provider: 'Google' },
            { entryKey: 'run:3', label: 'Model 3', provider: 'Google' }
          ]
        }));
        await until(() => component.chartStatus[51]?.state === 'done');

        const [targets] = vi.mocked(publish).mock.lastCall!;
        expect(targets).toEqual([{
          documentId: 51,
          audience: BenchmarkReportAudience.ExecutiveSummary,
          subjectKey: 'comparison:12/0123456789abcdef',
          peerLetters: { 'run:1': 'A', 'run:3': 'B' },
          label: 'Executive Summary 51',
          coveredKeys: ['run:1', 'run:3']
        }]);
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
