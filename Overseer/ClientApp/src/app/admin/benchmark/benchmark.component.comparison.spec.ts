import type { MockedObject } from "vitest";
import { ComponentFixture } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { of, throwError, Subject } from 'rxjs';
import { AdminBenchmarkComponent } from './benchmark.component';
import { AdminBenchmarkService } from '../../services/admin-benchmark.service';
import { serializeQuestionsYaml } from './question-yaml/question-yaml-format';
import { COMPARISON_WIZARD_STEPS } from './model-comparison/model-comparison.component';
import { ReportDocumentsLauncherComponent } from './report-pack/report-documents-launcher.component';
import { MAX_COMPARABILITY_INDEX_GROUPS, MAX_COMPARABILITY_INDEX_RUNS } from './state/benchmark-comparison.state';
import {
  AdminBenchmarkSpecContext, BENCHMARK_SPEC_COMPARISON, clearStoredState, createAdminBenchmarkFixture,
  COMPARISON_SELECTION_KEY, COMPARISON_LAUNCHER_KEY
} from './benchmark.component.testing';
import {
  LAST_COMPARISON_STORAGE_KEY, LAST_COMPARISON_STORAGE_VERSION, LastComparisonRecord
} from './comparison-tab/last-comparison';

describe('AdminBenchmarkComponent', () => {
  let ctx: AdminBenchmarkSpecContext;
  let component: AdminBenchmarkComponent;
  let fixture: ComponentFixture<AdminBenchmarkComponent>;
  let benchmarkServiceMock: MockedObject<AdminBenchmarkService>;

  /** The Last comparison card's record is real browser state too, so no spec may leak one. */
  function clearLastComparison(): void {
    try {
      localStorage.removeItem(LAST_COMPARISON_STORAGE_KEY);
    } catch { /* private-browsing modes throw */ }
  }

  beforeEach(clearStoredState);
  beforeEach(clearLastComparison);

  afterEach(clearStoredState);
  afterEach(clearLastComparison);

  beforeEach(async () => {
    ctx = await createAdminBenchmarkFixture();
    ({ component, fixture, benchmarkServiceMock } = ctx);
  });

  // ---------------------------------------------------------------------------
  // Model Comparison
  //
  // The host owns the selection, the request and the two lists the picker offers, so every
  // guard that keeps a comparison honest — placement, request shape, ordering, scope and
  // persistence — is asserted here rather than in either presentational component.
  // ---------------------------------------------------------------------------
  describe('model comparison', () => {
    function buildRun(id: number, suiteId: number, overrides: any = {}): any {
      return {
        id,
        benchmarkSuiteId: suiteId,
        suiteName: `Suite ${suiteId}`,
        testedModelDisplayNameUsed: `Model ${id}`,
        testedModelProviderUsed: 'Google',
        testedModelIdUsed: 'gemini-2.5-flash',
        assessorModelDisplayNameUsed: 'Claude Opus',
        status: 'Completed',
        startedAtUtc: '2026-09-01T10:00:00Z',
        qualityIndex: 60 + id,
        speedIndex: 80,
        totalAnswerDurationMs: 1000,
        speedMeasurementDegraded: false,
        answeredQuestionCount: 18,
        totalQuestionCount: 18,
        totalDurationMs: 1200,
        ...overrides
      };
    }

    function buildGroup(id: number, suiteId: number): any {
      return {
        id,
        name: `Group ${id}`,
        benchmarkSuiteId: suiteId,
        suiteName: `Suite ${suiteId}`,
        tier: 'Replicate',
        tierLabel: 'Tier A — Replicate',
        crossCondition: false,
        createdAtUtc: '2026-09-05T10:00:00Z',
        modifiedAtUtc: '2026-09-05T10:00:00Z',
        runCount: 3,
        members: [],
        analysisStale: false
      };
    }

    beforeEach(() => {
      ctx.workspace.historyRuns = [buildRun(1, 5), buildRun(2, 5), buildRun(3, 6)];
      ctx.workspace.runGroups = [buildGroup(11, 5), buildGroup(12, 6)];
      // The Last comparison card counts its documents from the comparison list.
      Object.assign(benchmarkServiceMock, {
        listComparisons: vi.fn().mockName('AdminBenchmarkService.listComparisons').mockReturnValue(of([]))
      });
      ctx.refresh();
    });

    /** A remembered comparison as the card stores it: comparison #12, two charted runs and one excluded. */
    function storedRecord(overrides: Partial<LastComparisonRecord> = {}): LastComparisonRecord {
      const entry = {
        label: '', modelDisplayName: '', provider: 'Google', thinkingLevel: null, excluded: false,
        explanation: 'Comparable with the baseline.', qualityPoint: 61, qualityLower: 55, qualityUpper: 67,
        modelTimeP50Ms: 4200, ttftP50Ms: 850, candidateCostPerQuestionUsd: 0.0042
      };
      return {
        version: LAST_COMPARISON_STORAGE_VERSION,
        savedAtUtc: '2026-10-08T12:00:00Z',
        id: 12,
        name: 'Model 1 vs Model 2',
        subjectKind: 'Runs',
        entryKeys: ['group:11', 'run:1', 'run:2'],
        computedAtUtc: '2026-10-08T11:59:00Z',
        pricingBasis: 'AsRun',
        pricingBasisLabel: 'As run, each run at its own price card',
        scopeName: 'Suite 5',
        comparableCount: 2,
        excludedCount: 1,
        entries: [
          { ...entry, key: 'run:1', label: 'Model 1', modelDisplayName: 'Model 1' },
          { ...entry, key: 'run:2', label: 'Model 2', modelDisplayName: 'Model 2', qualityPoint: 58 },
          {
            ...entry, key: 'group:11', label: 'Group 11', modelDisplayName: 'Group 11', excluded: true,
            explanation: 'Graded under another scoring method.', qualityPoint: null, qualityLower: null, qualityUpper: null
          }
        ],
        ...overrides
      };
    }

    function storeRecord(record: LastComparisonRecord): void {
      localStorage.setItem(LAST_COMPARISON_STORAGE_KEY, JSON.stringify(record));
    }

    function showComparisonTab(): void {
      fixture.nativeElement.querySelector('#bm-tab-modelcomparison').click();
      fixture.detectChanges();
    }

    function summaryCard(): HTMLElement | null {
      return fixture.nativeElement.querySelector('#bm-panel-modelcomparison app-comparison-summary-card');
    }

    /** The card's facts as term → value, whitespace collapsed. */
    function cardFacts(): Record<string, string> {
      const pairs: Record<string, string> = {};
      for (const group of Array.from(summaryCard()!.querySelectorAll('dl.bm-summary-facts > div'))) {
        const textOf = (el: Element | null) => (el?.textContent ?? '').replace(/\s+/g, ' ').trim();
        pairs[textOf(group.querySelector('dt'))] = textOf(group.querySelector('dd'));
      }
      return pairs;
    }

    // --- The sticky-container regression guard ---

    it('renders the comparison panel inside .benchmark-container, so the sub-tab row stays pinned', () => {
      fixture.nativeElement.querySelector('#bm-tab-modelcomparison').click();
      fixture.detectChanges();

      const container = fixture.nativeElement.querySelector('.benchmark-container');
      const panel = fixture.nativeElement.querySelector('#bm-panel-modelcomparison');
      expect(panel).toBeTruthy();
      // Structural, not a computed style: a sticky element only sticks while its parent's box is
      // on screen, and the parent is what this asserts.
      expect(container.contains(panel)).toBe(true);
    });

    it('gives the comparison panel the same panel class as the other five sub-tabs', () => {
      fixture.nativeElement.querySelector('#bm-tab-modelcomparison').click();
      fixture.detectChanges();

      const panel = fixture.nativeElement.querySelector('#bm-panel-modelcomparison');
      expect(panel.classList.contains('benchmark-tab-content')).toBe(true);
      // gh-tab-panel is defined in no stylesheet in the repository.
      expect(panel.classList.contains('gh-tab-panel')).toBe(false);
    });

    it('shows a launcher, and mounts nothing of the wizard until it is opened', () => {
      fixture.nativeElement.querySelector('#bm-tab-modelcomparison').click();
      fixture.detectChanges();

      const panel = fixture.nativeElement.querySelector('#bm-panel-modelcomparison');
      expect(panel.querySelector('.mc-launcher .mc-launcher-hero')).toBeTruthy();
      // The task itself is a dialog, so neither of the two components is in the panel.
      expect(panel.querySelector('app-comparison-source-picker')).toBeNull();
      expect(panel.querySelector('app-benchmark-model-comparison')).toBeNull();

      // Deferred: nothing of the wizard is constructed for an operator who never opens it, and a
      // modal that appeared without a gesture would leave them pressing Escape onto an empty tab.
      expect(component.comparisonWizardMounted).toBe(false);
      const dialog = fixture.nativeElement.querySelector('.benchmark-model-comparison-dialog');
      expect(dialog).toBeTruthy();
      expect(dialog.querySelector('app-benchmark-model-comparison')).toBeNull();
      expect(dialog.open).toBe(false);
    });

    it('reports no pending selection in the launcher, and offers no control that could change one', () => {
      ctx.comparison.comparisonRunIds = [1, 2];
      ctx.comparison.comparisonGroupIds = [11];
      fixture.nativeElement.querySelector('#bm-tab-modelcomparison').click();
      fixture.detectChanges();

      const launcher = fixture.nativeElement.querySelector('.mc-launcher-hero');
      // The picker lives in the wizard, so a "Selected" read-out here would label a control that
      // is not on this panel, and Clear would clear something it never showed.
      const terms = Array.from(launcher.querySelectorAll('dt'))
        .map((dt: any) => dt.textContent.trim());
      expect(terms).not.toContain('Selected');
      expect(launcher.textContent).not.toContain('analysis groups');
      expect(launcher.textContent).not.toContain('Clear selection');
      // No comparison remembered yet: the Last comparison card is absent rather than empty, and
      // nothing is fetched for it.
      expect(summaryCard()).toBeNull();
      expect(benchmarkServiceMock.listComparisons).not.toHaveBeenCalled();
      // One action, and it is the one that opens the surface that owns the selection.
      const actions = launcher.querySelectorAll('.mc-launcher-actions button');
      expect(actions.length).toBe(1);
      expect(actions[0].textContent.trim()).toBe('Open Comparison Wizard');
      // The page's primary task, and its only image button outside a dialog footer.
      expect(actions[0].classList.contains('btn-gh')).toBe(true);
      expect(actions[0].classList.contains('btn-gh-small')).toBe(false);
      const imageButtons = (Array.from(fixture.nativeElement.querySelectorAll('#bm-panel-modelcomparison .btn-gh')) as HTMLElement[])
        .filter(button => !button.closest('dialog'));
      expect(imageButtons).toEqual([actions[0]]);
    });

    it('puts Open Comparison Wizard directly under the lead, before the steps', () => {
      fixture.nativeElement.querySelector('#bm-tab-modelcomparison').click();
      fixture.detectChanges();

      const hero = fixture.nativeElement.querySelector('.mc-launcher-hero') as HTMLElement;
      const lead = hero.querySelector('.mc-launcher-lead') as HTMLElement;
      const actions = hero.querySelector('.mc-launcher-actions') as HTMLElement;
      expect(lead.nextElementSibling).toBe(actions);

      const steps = hero.querySelector('ol.mc-launcher-steps') as HTMLElement;
      expect(actions.compareDocumentPosition(steps) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    });

    it('keeps the steps and the like-for-like note in a non-exclusive disclosure after the action', () => {
      fixture.nativeElement.querySelector('#bm-tab-modelcomparison').click();
      fixture.detectChanges();

      const hero = fixture.nativeElement.querySelector('.mc-launcher-hero') as HTMLElement;
      const details = hero.querySelector('details.gh-disclosure.mc-launcher-howto') as HTMLDetailsElement;
      expect(details).toBeTruthy();
      expect(details.hasAttribute('name')).toBe(false);
      expect(details.querySelector('summary')?.textContent?.trim()).toBe('How the comparison works');
      expect(details.querySelector('ol.mc-launcher-steps')).toBeTruthy();
      expect(details.querySelector('.alert.alert-info[role="note"]')?.textContent)
        .toContain('Only like-for-like runs are charted together');
    });

    it('opens the disclosure on the first visit only, and remembers how the operator left it', () => {
      fixture.nativeElement.querySelector('#bm-tab-modelcomparison').click();
      fixture.detectChanges();

      let details = fixture.nativeElement.querySelector('.mc-launcher-howto') as HTMLDetailsElement;
      expect(details.open).toBe(true);
      // Recorded closed at once, so the next visit starts closed unless the operator keeps it open.
      expect(JSON.parse(localStorage.getItem(COMPARISON_LAUNCHER_KEY)!)).toEqual({ howItWorksOpen: false });

      // As a page reload does: the state is read from storage again on the next showing.
      ctx.comparison.comparisonHowItWorksOpen = null;
      fixture.nativeElement.querySelector('#bm-tab-run').click();
      fixture.detectChanges();
      fixture.nativeElement.querySelector('#bm-tab-modelcomparison').click();
      fixture.detectChanges();

      details = fixture.nativeElement.querySelector('.mc-launcher-howto') as HTMLDetailsElement;
      expect(details.open).toBe(false);

      // The native toggle, dispatched synchronously rather than awaited.
      details.open = true;
      details.dispatchEvent(new Event('toggle'));
      expect(JSON.parse(localStorage.getItem(COMPARISON_LAUNCHER_KEY)!)).toEqual({ howItWorksOpen: true });

      ctx.comparison.comparisonHowItWorksOpen = null;
      fixture.nativeElement.querySelector('#bm-tab-run').click();
      fixture.detectChanges();
      fixture.nativeElement.querySelector('#bm-tab-modelcomparison').click();
      fixture.detectChanges();

      details = fixture.nativeElement.querySelector('.mc-launcher-howto') as HTMLDetailsElement;
      expect(details.open).toBe(true);
    });

    it('opens the disclosure when storage throws, and does not throw itself', () => {
      vi.spyOn(localStorage, 'getItem').mockImplementation(() => {
        throw new Error('private browsing');
      });
      vi.spyOn(localStorage, 'setItem').mockImplementation(() => {
        throw new Error('private browsing');
      });

      fixture.nativeElement.querySelector('#bm-tab-modelcomparison').click();
      fixture.detectChanges();

      const details = fixture.nativeElement.querySelector('.mc-launcher-howto') as HTMLDetailsElement;
      expect(details.open).toBe(true);
      details.open = false;
      expect(() => details.dispatchEvent(new Event('toggle'))).not.toThrow();
      expect(ctx.comparison.comparisonHowItWorksOpen).toBe(false);
    });

    // --- The Comparison reports card ---

    /** The card's list requests: the report-pack documents, apart from any run's own list. */
    function comparisonReportLoads(): number {
      return vi.mocked(benchmarkServiceMock.listReportDocuments).mock.calls.filter(call => (call[0] as {
        origin?: string;
      } | undefined)?.origin === 'reportPack')
        .length;
    }

    it('renders the Comparison reports card below the hero card, over every comparison document', () => {
      fixture.nativeElement.querySelector('#bm-tab-modelcomparison').click();
      fixture.detectChanges();

      const launcherDebug = fixture.debugElement.query(By.directive(ReportDocumentsLauncherComponent));
      expect(launcherDebug).toBeTruthy();
      const launcher = launcherDebug.componentInstance as ReportDocumentsLauncherComponent;
      expect(launcher.idPrefix).toBe('mcl');

      const host = launcherDebug.nativeElement as HTMLElement;
      const hero = fixture.nativeElement.querySelector('.mc-launcher-hero') as HTMLElement;
      expect(hero.contains(host)).toBe(false);
      expect(hero.compareDocumentPosition(host) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
      // The lead paragraph is the card's info tip now.
      expect(fixture.nativeElement.querySelector('.mc-launcher-library-lead')).toBeNull();
      expect(host.querySelector('#mcl-tip')?.textContent).toContain('Every report document written from a model comparison, newest first');
      expect(host.querySelector('#mcl-open')?.textContent?.trim()).toBe('Open Download Center');
      // No document yet: the button is aria-disabled, and the summary says why.
      expect(host.querySelector('#mcl-open')?.getAttribute('aria-disabled')).toBe('true');
      expect(host.querySelector('.rdl-launcher-summary')?.textContent).toContain('No reports yet.');

      expect(benchmarkServiceMock.listReportDocuments).toHaveBeenCalledWith(expect.objectContaining({ origin: 'reportPack', take: 500 }));
    });

    it('loads the Comparison reports only once the tab is shown, then on every showing and wizard close', () => {
      // Page load, on another tab: nothing of the card exists and nothing is fetched for it.
      expect(fixture.nativeElement.querySelector('app-report-documents-launcher')).toBeNull();
      expect(comparisonReportLoads()).toBe(0);

      fixture.nativeElement.querySelector('#bm-tab-modelcomparison').click();
      fixture.detectChanges();
      // One load on the first showing, not one on init and a second for the reload token.
      expect(comparisonReportLoads()).toBe(1);

      // A stable scope: another change-detection pass does not read it as a new one.
      fixture.detectChanges();
      expect(comparisonReportLoads()).toBe(1);

      fixture.nativeElement.querySelector('#bm-tab-run').click();
      fixture.detectChanges();
      fixture.nativeElement.querySelector('#bm-tab-modelcomparison').click();
      fixture.detectChanges();
      expect(comparisonReportLoads()).toBe(2);

      // The wizard's Report Pack may have written documents.
      component.onComparisonWizardClose();
      ctx.refresh();
      expect(comparisonReportLoads()).toBe(3);
    });

    // --- The Last comparison card ---

    it('renders the remembered comparison as its own card between the hero and the library', () => {
      storeRecord(storedRecord());
      benchmarkServiceMock.listComparisons.mockReturnValue(of([{
        id: 12, name: 'Model 1 vs Model 2', customName: null, defaultName: 'Model 1 vs Model 2', entryCount: 3,
        subjectKind: 'Runs', documentCount: 4, lastDocumentAtUtc: '2026-10-08T09:00:00Z', createdAtUtc: '2026-10-06T10:00:00Z'
      }]));
      showComparisonTab();

      const card = summaryCard()!;
      expect(card).toBeTruthy();
      expect(card.querySelector('#mc-last-title')!.textContent!.trim()).toBe('Comparison #12 · Model 1 vs Model 2');
      // A grid child of its own: not inside the hero, and before the Comparison reports library.
      const launcher = fixture.nativeElement.querySelector('.mc-launcher.bm-launcher') as HTMLElement;
      const hero = launcher.querySelector('.mc-launcher-hero') as HTMLElement;
      const library = launcher.querySelector('.mc-launcher-library') as HTMLElement;
      expect(card.parentElement).toBe(launcher);
      expect(hero.contains(card)).toBe(false);
      expect(hero.compareDocumentPosition(card) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
      expect(card.compareDocumentPosition(library) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
      // The old read-out in the hero is gone.
      expect(hero.querySelector('dl')).toBeNull();

      expect(benchmarkServiceMock.listComparisons).toHaveBeenCalledTimes(1);
      expect(cardFacts()).toEqual({
        'Charted': '2 of 3 entries · 1 excluded',
        'Pricing': 'As run As run, each run at its own price card',
        'Report documents': '4 · latest 8 Oct 2026'
      });
      expect(card.querySelectorAll('table.bm-summary-table tbody tr').length).toBe(3);
    });

    it('omits the documents fact when the comparison list fails or does not carry the comparison', () => {
      storeRecord(storedRecord());
      benchmarkServiceMock.listComparisons.mockReturnValue(throwError(() => ({ error: 'No.' })));
      showComparisonTab();

      expect(summaryCard()).toBeTruthy();
      expect(Object.keys(cardFacts())).toEqual(['Charted', 'Pricing']);

      benchmarkServiceMock.listComparisons.mockReturnValue(of([]));
      ctx.comparison.refreshLastComparisonDocuments();
      fixture.detectChanges();
      expect(Object.keys(cardFacts())).toEqual(['Charted', 'Pricing']);
    });

    it('ignores a stored record of another version', () => {
      storeRecord({ ...storedRecord(), version: 2 } as unknown as LastComparisonRecord);
      showComparisonTab();

      expect(summaryCard()).toBeNull();
      expect(ctx.comparison.lastComparison).toBeNull();
    });

    it('records the comparison the wizard numbers, and the card follows it', () => {
      showComparisonTab();
      component.openComparisonWizard();
      fixture.detectChanges();
      expect(summaryCard()).toBeNull();

      const comparison = {
        pricingBasis: 'Current',
        pricingBasisLabel: 'Current catalog, as of 2026-10-08',
        computedAtUtc: '2026-10-08T11:00:00Z',
        subjectKind: 'Runs',
        baselineSuiteName: 'Suite 5',
        baselineBatteryName: null,
        comparableCount: 2,
        excludedCount: 0,
        entries: [
          { key: 'run:1', label: 'Model 1', modelDisplayName: 'Model 1', provider: 'Google', excluded: false, explanation: '', quality: { pointEstimate: 61 } },
          { key: 'run:2', label: 'Model 2', modelDisplayName: 'Model 2', provider: 'Google', excluded: false, explanation: '', quality: { pointEstimate: 66 } }
        ]
      } as any;
      component.comparisonWizard!.comparisonIdentified.emit({ comparison, identity: BENCHMARK_SPEC_COMPARISON });
      fixture.detectChanges();

      const stored = JSON.parse(localStorage.getItem(LAST_COMPARISON_STORAGE_KEY)!) as LastComparisonRecord;
      expect(stored.id).toBe(12);
      expect(stored.entryKeys).toEqual(['run:1', 'run:2']);
      expect(stored.entries.map(entry => entry.key)).toEqual(['run:2', 'run:1']);
      expect(ctx.comparison.lastComparison).toEqual(stored);
      expect(summaryCard()!.querySelector('#mc-last-title')!.textContent!.trim()).toBe('Comparison #12 · Model 1 vs Model 2');

      // The wizard's Write step may have written documents, so closing it counts them again.
      benchmarkServiceMock.listComparisons.mockClear();
      benchmarkServiceMock.listComparisons.mockReturnValue(of([{
        id: 12, name: 'Model 1 vs Model 2', customName: null, defaultName: 'Model 1 vs Model 2', entryCount: 2,
        subjectKind: 'Runs', documentCount: 0, lastDocumentAtUtc: null, createdAtUtc: '2026-10-06T10:00:00Z'
      }]));
      // As the dialog's close event does.
      component.onComparisonWizardClose();
      ctx.refresh();
      expect(benchmarkServiceMock.listComparisons).toHaveBeenCalledTimes(1);
      expect(cardFacts()['Report documents']).toBe('None yet');
      component.closeComparisonWizard();
    });

    it('opens the wizard on the remembered selection and basis from Open in wizard', () => {
      storeRecord(storedRecord());
      showComparisonTab();
      const dialog = fixture.nativeElement
        .querySelector('.benchmark-model-comparison-dialog') as HTMLDialogElement;
      const showModal = vi.spyOn(dialog, 'showModal');

      const open = Array.from(summaryCard()!.querySelectorAll<HTMLButtonElement>('.bm-summary-card-actions button'))
        .find(button => button.textContent!.trim() === 'Open in wizard')!;
      expect(open.classList.contains('btn-ghost')).toBe(true);
      open.click();
      fixture.detectChanges();

      expect(ctx.comparison.comparisonRunIds).toEqual([1, 2]);
      expect(ctx.comparison.comparisonGroupIds).toEqual([11]);
      expect(ctx.comparison.comparisonBatteryRunIds).toEqual([]);
      expect(ctx.comparison.comparisonPricingBasis).toBe('AsRun');
      // No payload is what puts the wizard on step 1, where the selection is.
      expect(ctx.comparison.comparison).toBeNull();
      const selection = JSON.parse(localStorage.getItem(COMPARISON_SELECTION_KEY)!);
      expect(selection).toEqual(expect.objectContaining({ runIds: [1, 2], groupIds: [11], batteryRunIds: [], pricingBasis: 'AsRun' }));
      expect(showModal).toHaveBeenCalledTimes(1);
      expect(component.comparisonWizardMounted).toBe(true);
      component.closeComparisonWizard();
    });

    it('clears a suite scope that would hide the remembered sources', () => {
      ctx.comparison.onComparisonSuiteChange(6);
      expect(ctx.comparison.comparisonSuiteId).toBe(6);

      ctx.comparison.applyComparisonEntries(['run:1', 'group:11', 'nonsense', 'run:1'], 'Current');

      expect(ctx.comparison.comparisonSuiteId).toBeNull();
      expect(ctx.comparison.comparisonRunIds).toEqual([1]);
      expect(ctx.comparison.comparisonGroupIds).toEqual([11]);
    });

    it('keeps runs and groups when remembered keys mix them with battery results', () => {
      ctx.comparison.applyComparisonEntries(['run:1', 'battery:4'], 'Current');

      expect(ctx.comparison.comparisonRunIds).toEqual([1]);
      expect(ctx.comparison.comparisonBatteryRunIds).toEqual([]);
    });

    it('lists the four wizard steps under the titles the wizard itself uses', () => {
      fixture.nativeElement.querySelector('#bm-tab-modelcomparison').click();
      fixture.detectChanges();

      const items = Array.from(
        fixture.nativeElement.querySelectorAll('.mc-launcher-hero .mc-launcher-howto ol.mc-launcher-steps > li')) as HTMLElement[];
      expect(items.length).toBe(4);
      expect(COMPARISON_WIZARD_STEPS.map(step => step.title)).toEqual(['Sources', 'Charts & table', 'Write', 'Documents']);
      expect(items.map(item => item.querySelector('strong')?.textContent?.trim()))
        .toEqual(COMPARISON_WIZARD_STEPS.map(step => step.title));
    });

    it('carries one plain-language callout, and none of the internal key vocabulary', () => {
      fixture.nativeElement.querySelector('#bm-tab-modelcomparison').click();
      fixture.detectChanges();

      const launcher = fixture.nativeElement.querySelector('.mc-launcher-hero') as HTMLElement;
      expect(launcher.querySelectorAll('.alert.alert-info[role="note"]').length).toBe(1);
      expect(launcher.textContent).not.toContain('must-match');
    });

    it('opens the wizard modally from the launcher, and keeps it mounted after a close', () => {
      fixture.nativeElement.querySelector('#bm-tab-modelcomparison').click();
      fixture.detectChanges();

      const dialog = fixture.nativeElement
        .querySelector('.benchmark-model-comparison-dialog') as HTMLDialogElement;
      const showModal = vi.spyOn(dialog, 'showModal');

      component.openComparisonWizard();
      fixture.detectChanges();

      expect(showModal).toHaveBeenCalledTimes(1);
      expect(component.comparisonWizardMounted).toBe(true);
      expect(dialog.querySelector('app-benchmark-model-comparison')).toBeTruthy();
      expect(dialog.querySelector('app-comparison-source-picker')).toBeTruthy();

      component.closeComparisonWizard();
      fixture.detectChanges();

      // Mount-once, destroy-never: reopening has to preserve the picker's table state, the step,
      // the filters, the entry selection and the rendered charts.
      expect(component.comparisonWizardMounted).toBe(true);
      expect(dialog.querySelector('app-benchmark-model-comparison')).toBeTruthy();
    });

    it('refuses Escape while an export is running, and allows it otherwise', () => {
      component.openComparisonWizard();
      fixture.detectChanges();

      const cancel = new Event('cancel', { cancelable: true });
      component.onComparisonWizardCancel(cancel);
      expect(cancel.defaultPrevented).toBe(false);

      // An export re-renders charts and writes files in sequence; tearing the DOM out from under
      // it would leave a detached chart and a half-written batch.
      component.comparisonWizard!.exporting = true;
      const blocked = new Event('cancel', { cancelable: true });
      component.onComparisonWizardCancel(blocked);
      expect(blocked.defaultPrevented).toBe(true);
    });

    it('refuses Escape while the wizard draws and uploads document charts', () => {
      component.openComparisonWizard();
      fixture.detectChanges();
      const wizard = component.comparisonWizard!;
      expect(wizard.chartsPublishing).toBe(false);

      (wizard as unknown as {
        pendingPublishes: number;
      }).pendingPublishes = 1;
      expect(wizard.chartsPublishing).toBe(true);
      const blocked = new Event('cancel', { cancelable: true });
      component.onComparisonWizardCancel(blocked);
      expect(blocked.defaultPrevented).toBe(true);

      (wizard as unknown as { pendingPublishes: number }).pendingPublishes = 0;
      const allowed = new Event('cancel', { cancelable: true });
      component.onComparisonWizardCancel(allowed);
      expect(allowed.defaultPrevented).toBe(false);
    });

    it('takes closedby="none" only while an export or a chart upload runs', () => {
      component.openComparisonWizard();
      fixture.detectChanges();
      const dialog: HTMLDialogElement = component.comparisonWizardDialog!.nativeElement;
      expect(dialog.hasAttribute('closedby')).toBe(false);

      component.comparisonWizard!.exporting = true;
      ctx.refresh();
      expect(dialog.getAttribute('closedby')).toBe('none');

      component.comparisonWizard!.exporting = false;
      ctx.refresh();
      expect(dialog.hasAttribute('closedby')).toBe(false);
      component.closeComparisonWizard();
    });

    it('reopens a close that gets through during an export, restoring focus, and closes once it is over', async () => {
      component.openComparisonWizard();
      fixture.detectChanges();
      const dialog: HTMLDialogElement = component.comparisonWizardDialog!.nativeElement;
      const closed = (): Promise<void> =>
        new Promise<void>(resolve => dialog.addEventListener('close', () => resolve(), { once: true }));
      const token = ctx.comparison.comparisonReportsReloadToken;

      component.comparisonWizard!.exporting = true;
      ctx.refresh();
      // A step tab, not the heading, which the reopen focuses when nothing was recorded.
      const focused = dialog.querySelector<HTMLElement>('#mc-step-tab-1')!;
      focused.focus();
      expect(document.activeElement).toBe(focused);
      const refused = new Event('cancel', { cancelable: true });
      dialog.dispatchEvent(refused);
      expect(refused.defaultPrevented).toBe(true);

      // As a second Escape, whose cancel the browser no longer lets the page refuse.
      const bounced = closed();
      dialog.close();
      await bounced;
      expect(dialog.open).toBe(true);
      expect(document.activeElement).toBe(focused);
      expect(ctx.comparison.comparisonReportsReloadToken).toBe(token);

      component.comparisonWizard!.exporting = false;
      ctx.refresh();
      const done = closed();
      dialog.close();
      await done;
      expect(dialog.open).toBe(false);
      expect(ctx.comparison.comparisonReportsReloadToken).toBe(token + 1);
    });

    it('loads the comparability index for the sources on offer, and survives it failing', () => {
      benchmarkServiceMock.getComparabilityIndex.mockClear();

      // A new suite scope is a new set of offered sources, so it re-indexes them.
      ctx.comparison.onComparisonSuiteChange(5);

      expect(benchmarkServiceMock.getComparabilityIndex).toHaveBeenCalledWith({
        runIds: [1, 2],
        groupIds: [11]
      });
      expect(ctx.comparison.comparabilityIndex).toBeTruthy();

      benchmarkServiceMock.getComparabilityIndex.mockReturnValue(throwError(() => ({ error: 'The index could not be built.' })));
      ctx.comparison.onComparisonSuiteChange(6);

      // Non-fatal: the Condition column falls back to a dash and Compare still works.
      expect(ctx.comparison.comparabilityIndex).toBeNull();
      expect(ctx.comparison.comparabilityIndexError).toContain('could not be built');
    });

    it('derives the wizard band notices from the index, the selection and the pricing basis', () => {
      // One owner: the picker's checkboxes and the wizard's band both read this list, so neither
      // can hold its own account of what the selection costs.
      ctx.comparison.comparabilityIndexError = null;
      ctx.comparison.comparabilityIndexLoading = false;
      ctx.comparison.comparabilityIndex = {
        computedAtUtc: '2026-09-07T12:00:00Z',
        entries: [
          {
            key: 'run:1', sourceKind: 'Run', sourceId: 1, conditionOrdinal: 1,
            conditionLabel: 'Condition A', signature: 'sig-a', selfInconsistent: false,
            selfInconsistentKeys: [], differencesFromLargest: [],
            questionParallelism: '1', speedCalibration: 'speed-a', pricingSnapshot: '2026-09-01'
          },
          {
            key: 'run:2', sourceKind: 'Run', sourceId: 2, conditionOrdinal: 1,
            conditionLabel: 'Condition A', signature: 'sig-a', selfInconsistent: false,
            selfInconsistentKeys: [], differencesFromLargest: [],
            questionParallelism: '1', speedCalibration: 'speed-a', pricingSnapshot: '2026-09-01'
          },
          {
            key: 'run:3', sourceKind: 'Run', sourceId: 3, conditionOrdinal: 2,
            conditionLabel: 'Condition B', signature: 'sig-b', selfInconsistent: false,
            selfInconsistentKeys: [], differencesFromLargest: [],
            questionParallelism: '1', speedCalibration: 'speed-a', pricingSnapshot: '2026-09-01'
          }
        ],
        conditions: [
          {
            ordinal: 1, label: 'Condition A', sourceCount: 2, runCount: 2,
            signature: 'sig-a', newestRunStartedAtUtc: '2026-09-05T10:00:00Z'
          },
          {
            ordinal: 2, label: 'Condition B', sourceCount: 1, runCount: 1,
            signature: 'sig-b', newestRunStartedAtUtc: '2026-09-04T10:00:00Z'
          }
        ],
        largestConditionKeys: [],
        referenceSelectionRule: 'The reference condition is the one with the most sources.',
        mustMatchKeyNames: ['BenchmarkSuiteId'],
        modelAxisKeyNames: ['ModelId'],
        degradingKeyNames: ['PricingSnapshot']
      } as any;

      ctx.comparison.comparisonRunIds = [1, 2];
      ctx.comparison.comparisonGroupIds = [];
      expect(ctx.comparison.comparisonSelectionNotices).toEqual([]);

      ctx.comparison.comparisonRunIds = [1, 2, 3];
      expect(ctx.comparison.comparisonSelectionNotices.map(notice => notice.id))
        .toEqual(['cross-condition']);

      ctx.comparison.comparabilityIndex = null;
      ctx.comparison.comparabilityIndexError = 'The index could not be built.';
      const failed = ctx.comparison.comparisonSelectionNotices;
      expect(failed.map(notice => notice.id)).toEqual(['index-error']);
      expect(failed[0].severity).toBe('error');
      expect(failed[0].body).toContain('The index could not be built.');
    });

    it('drops the payload when the selection changes, so Compare is asked for again', () => {
      ctx.comparison.comparison = { entries: [] } as any;
      ctx.comparison.onComparisonSelectionChange({ runIds: [1], groupIds: [] });

      // The figures on hand describe the previous set of sources. It is also what the wizard reads
      // to know Compare has not run for this selection yet.
      expect(ctx.comparison.comparison).toBeNull();
    });

    // --- The selection band's chips ---

    it('names every selected source for the band, runs then groups, skipping one outside suite scope', () => {
      ctx.comparison.comparisonSuiteId = 5;
      ctx.comparison.comparisonRunIds = [1, 3, 2];
      ctx.comparison.comparisonGroupIds = [11, 12];

      // Run 3 and group 12 belong to suite 6, which the current scope no longer offers: skipped
      // rather than rendered as a placeholder, same as the picker's own checkboxes.
      expect(ctx.comparison.comparisonSelectedSources).toEqual([
        { kind: 'run', id: 1, label: 'Model 1', provider: 'Google', detail: '#1' },
        { kind: 'run', id: 2, label: 'Model 2', provider: 'Google', detail: '#2' },
        { kind: 'group', id: 11, label: 'Group 11', provider: null, detail: '3 runs' }
      ]);
    });

    it('names a group of one run in the singular', () => {
      ctx.workspace.runGroups = [...ctx.workspace.runGroups, { ...buildGroup(13, 5), runCount: 1 }];
      ctx.comparison.comparisonGroupIds = [13];

      expect(ctx.comparison.comparisonSelectedSources).toEqual([
        { kind: 'group', id: 13, label: 'Group 13', provider: null, detail: '1 run' }
      ]);
    });

    it('removes one source through the same path every other selection change takes', () => {
      ctx.comparison.comparisonRunIds = [1, 2];
      ctx.comparison.comparisonGroupIds = [11];
      ctx.comparison.comparison = { entries: [] } as any;

      ctx.comparison.onComparisonRemoveSource(
        { kind: 'run', id: 1, label: 'Model 1', provider: 'Google', detail: '#1' });

      expect(ctx.comparison.comparisonRunIds).toEqual([2]);
      expect(ctx.comparison.comparisonGroupIds).toEqual([11]);
      // Persistence, the dropped payload and the Compare reset are onComparisonSelectionChange's
      // job, so routing through it is what keeps them all in force after a chip is removed.
      expect(ctx.comparison.comparison).toBeNull();
    });

    // --- The .gh-dialog-fullscreen lift ---

    it('leaves the suite health dialog opening and closing after the full-screen lift', () => {
      // Its viewport sizing, transition and backdrop now come from styles.scss, and it is the only
      // other consumer of that block, so this is the regression guard for the move.
      component.selectSubTab('suites');
      fixture.detectChanges();
      const dialog = fixture.nativeElement
        .querySelector('.benchmark-suite-health-dialog') as HTMLDialogElement;
      expect(dialog.classList.contains('gh-dialog-fullscreen')).toBe(true);

      ctx.suitesTab().suiteHealthSuiteId = null;
      ctx.suitesTab().openSuiteHealth({ id: 5, name: 'Suite 5', questionCount: 4 } as any);
      fixture.detectChanges();
      expect(dialog.open).toBe(true);

      ctx.suitesTab().closeSuiteHealth();
      fixture.detectChanges();
      expect(dialog.open).toBe(false);
    });

    // --- Loading and the request ---

    it('loads the three lists the picker needs on tab entry, and fetches no comparison', () => {
      benchmarkServiceMock.getRuns.mockClear();
      benchmarkServiceMock.getRunGroups.mockClear();
      benchmarkServiceMock.getSuites.mockClear();
      benchmarkServiceMock.compareModels.mockClear();

      component.selectSubTab('modelcomparison');
      fixture.detectChanges();

      expect(benchmarkServiceMock.getRuns).toHaveBeenCalled();
      expect(benchmarkServiceMock.getRunGroups).toHaveBeenCalled();
      expect(benchmarkServiceMock.getSuites).toHaveBeenCalled();
      // An unattended request on tab entry would re-price for a selection nobody confirmed.
      expect(benchmarkServiceMock.compareModels).not.toHaveBeenCalled();
    });

    it('issues one request carrying the selected ids and the basis name', () => {
      ctx.comparison.onComparisonSelectionChange({ runIds: [1, 2], groupIds: [11] });
      benchmarkServiceMock.compareModels.mockClear();

      ctx.comparison.runComparison();

      expect(benchmarkServiceMock.compareModels).toHaveBeenCalledTimes(1);
      expect(benchmarkServiceMock.compareModels).toHaveBeenCalledWith({
        runIds: [1, 2],
        groupIds: [11],
        pricingBasis: 'Current'
      });
    });

    it('refuses an empty selection rather than sending a request the server will reject', () => {
      ctx.comparison.clearComparisonSelection();
      benchmarkServiceMock.compareModels.mockClear();

      ctx.comparison.runComparison();

      expect(benchmarkServiceMock.compareModels).not.toHaveBeenCalled();
      expect(ctx.comparison.comparisonError).toBe('Select at least one run, analysis group or battery result.');
    });

    it('reports the server error text rather than a generic failure', () => {
      benchmarkServiceMock.compareModels.mockReturnValue(throwError(() => ({ error: 'Run(s) not found: 4' })));
      ctx.comparison.onComparisonSelectionChange({ runIds: [4], groupIds: [] });

      ctx.comparison.runComparison();

      expect(ctx.comparison.comparisonError).toBe('Run(s) not found: 4');
      expect(ctx.comparison.comparison).toBeNull();
      expect(ctx.comparison.comparisonLoading).toBe(false);
    });

    it('discards an out-of-order response so the older payload never overwrites the newer', () => {
      const first = new Subject<any>();
      const second = new Subject<any>();
      benchmarkServiceMock.compareModels.mockReturnValueOnce(first as any).mockReturnValueOnce(second as any);
      ctx.comparison.onComparisonSelectionChange({ runIds: [1], groupIds: [] });

      ctx.comparison.runComparison();
      ctx.comparison.runComparison();

      second.next({ entries: [], explanation: 'newer' });
      first.next({ entries: [], explanation: 'older' });

      expect((ctx.comparison.comparison as any).explanation).toBe('newer');
    });

    // --- Cancelling, and never trapping the operator in the wizard while loading ---

    it('cancels the comparison in flight, releasing the request and ignoring its late result', () => {
      const request = new Subject<any>();
      benchmarkServiceMock.compareModels.mockReturnValue(request as any);
      ctx.comparison.onComparisonSelectionChange({ runIds: [1, 2], groupIds: [] });

      ctx.comparison.runComparison();
      expect(ctx.comparison.comparisonLoading).toBe(true);
      expect(request.observed).toBe(true);

      ctx.comparison.cancelComparison();

      expect(ctx.comparison.comparisonLoading).toBe(false);
      // Unsubscribed, so the HTTP request is aborted and the server stops pricing.
      expect(request.observed).toBe(false);
      request.next({ entries: [], explanation: 'late' });
      expect(ctx.comparison.comparison).toBeNull();
    });

    it('releases a superseded request when Compare runs again', () => {
      const first = new Subject<any>();
      const second = new Subject<any>();
      benchmarkServiceMock.compareModels.mockReturnValueOnce(first as any).mockReturnValueOnce(second as any);
      ctx.comparison.onComparisonSelectionChange({ runIds: [1], groupIds: [] });

      ctx.comparison.runComparison();
      ctx.comparison.runComparison();

      expect(first.observed).toBe(false);
      expect(second.observed).toBe(true);
    });

    it('drops the request in flight when the selection changes under it', () => {
      const request = new Subject<any>();
      benchmarkServiceMock.compareModels.mockReturnValue(request as any);
      ctx.comparison.onComparisonSelectionChange({ runIds: [1], groupIds: [] });
      ctx.comparison.runComparison();

      ctx.comparison.onComparisonSelectionChange({ runIds: [1, 2], groupIds: [] });
      request.next({ entries: [], explanation: 'for the previous selection' });

      // The older response would otherwise chart the previous selection and advance the wizard.
      expect(ctx.comparison.comparison).toBeNull();
      expect(ctx.comparison.comparisonLoading).toBe(false);
      expect(request.observed).toBe(false);
    });

    it('treats a cancel with nothing in flight as a no-op', () => {
      const notify = vi.spyOn(ctx.viewSync, 'notify');

      ctx.comparison.cancelComparison();

      expect(ctx.comparison.comparisonLoading).toBe(false);
      expect(notify).not.toHaveBeenCalled();
    });

    it('never refuses Escape because a comparison is loading', () => {
      ctx.comparison.comparisonLoading = true;
      component.comparisonWizard = { exporting: false } as any;
      const event = { preventDefault: vi.fn().mockName('preventDefault') } as unknown as Event;

      component.onComparisonWizardCancel(event);

      expect(event.preventDefault).not.toHaveBeenCalled();
    });

    it('never switches close requests off on the wizard dialog', () => {
      component.openComparisonWizard();
      fixture.detectChanges();

      const dialog = fixture.nativeElement
        .querySelector('dialog.benchmark-model-comparison-dialog') as HTMLDialogElement;
      expect(dialog).toBeTruthy();
      expect(dialog.getAttribute('closedby')).not.toBe('none');
    });

    it('keeps the projected picker live and the wizard uncovered while a comparison loads', () => {
      const request = new Subject<any>();
      benchmarkServiceMock.compareModels.mockReturnValue(request as any);
      ctx.comparison.onComparisonSelectionChange({ runIds: [1, 2], groupIds: [] });
      component.openComparisonWizard();
      fixture.detectChanges();

      ctx.comparison.runComparison();
      fixture.detectChanges();
      expect(ctx.comparison.comparisonLoading).toBe(true);

      const dialog = fixture.nativeElement
        .querySelector('dialog.benchmark-model-comparison-dialog') as HTMLDialogElement;
      const checkboxes = Array.from(
        dialog.querySelectorAll('#mc-step-panel-1 input[type="checkbox"]')) as HTMLInputElement[];
      expect(checkboxes.length).toBeGreaterThan(0);
      expect(checkboxes.some(checkbox => !checkbox.disabled)).toBe(true);
      expect(dialog.querySelectorAll('[inert]').length).toBe(0);

      const close = dialog.querySelector('[aria-label="Close cross-model comparison"]') as HTMLButtonElement;
      expect(close.disabled).toBe(false);

      component.closeComparisonWizard();
    });

    // --- Suite scope ---

    it('scopes both offered lists to the suite scope', () => {
      ctx.comparison.onComparisonSuiteChange(5);

      expect(ctx.comparison.comparisonRunOptions.map(r => r.id)).toEqual([1, 2]);
      expect(ctx.comparison.comparisonGroupOptions.map(g => g.id)).toEqual([11]);

      ctx.comparison.onComparisonSuiteChange(null);
      expect(ctx.comparison.comparisonRunOptions.length).toBe(3);
      expect(ctx.comparison.comparisonGroupOptions.length).toBe(2);
    });

    it('drops out-of-scope ids when the suite scope changes', () => {
      ctx.comparison.onComparisonSelectionChange({ runIds: [1, 3], groupIds: [11, 12] });

      ctx.comparison.onComparisonSuiteChange(5);

      // Run 3 and group 12 belong to suite 6: leaving them selected is how a figure ends up with
      // a model the picker does not show.
      expect(ctx.comparison.comparisonRunIds).toEqual([1]);
      expect(ctx.comparison.comparisonGroupIds).toEqual([11]);
    });

    it('clears the figures when nothing survives a suite scope change', () => {
      ctx.comparison.onComparisonSelectionChange({ runIds: [3], groupIds: [] });
      ctx.comparison.comparison = { entries: [] } as any;

      ctx.comparison.onComparisonSuiteChange(5);

      expect(ctx.comparison.comparisonRunIds).toEqual([]);
      expect(ctx.comparison.comparison).toBeNull();
    });

    // --- Pricing basis ---

    it('refetches at once on a pricing basis change, because it re-prices an unchanged set', () => {
      ctx.comparison.onComparisonSelectionChange({ runIds: [1], groupIds: [] });
      benchmarkServiceMock.compareModels.mockClear();

      ctx.comparison.onComparisonPricingBasisChange('AsRun');

      expect(ctx.comparison.comparisonPricingBasis).toBe('AsRun');
      expect(benchmarkServiceMock.compareModels).toHaveBeenCalledWith(expect.objectContaining({ pricingBasis: 'AsRun' }));
    });

    // --- Persistence ---

    it('remembers the selection, the scope and the basis across a reload', () => {
      ctx.comparison.onComparisonSuiteChange(5);
      ctx.comparison.onComparisonSelectionChange({ runIds: [1, 2], groupIds: [11] });

      const stored = JSON.parse(localStorage.getItem(COMPARISON_SELECTION_KEY)!);
      expect(stored).toEqual({
        runIds: [1, 2], groupIds: [11], batteryRunIds: [], suiteId: 5, pricingBasis: 'Current'
      });
    });

    it('drops a persisted id that no longer exists rather than sending it', () => {
      localStorage.setItem(COMPARISON_SELECTION_KEY, JSON.stringify({
        runIds: [1, 999], groupIds: [11, 888], suiteId: null, pricingBasis: 'AsRun'
      }));

      component.selectSubTab('modelcomparison');
      fixture.detectChanges();

      // getRuns and getRunGroups both resolve to [] under the default mocks, so the lists that
      // validate the restore are re-seeded here to what the tab actually offers.
      ctx.workspace.historyRuns = [buildRun(1, 5)];
      ctx.workspace.runGroups = [buildGroup(11, 5)];
      ctx.comparison.pruneComparisonSelection();

      expect(ctx.comparison.comparisonRunIds).toEqual([1]);
      expect(ctx.comparison.comparisonGroupIds).toEqual([11]);
      expect(ctx.comparison.comparisonPricingBasis).toBe('AsRun');
    });

    it('survives a localStorage read that throws, leaving every default standing', () => {
      vi.spyOn(localStorage, 'getItem').mockImplementation(() => {
        throw new Error('private browsing');
      });

      expect(() => {
        component.selectSubTab('modelcomparison');
        fixture.detectChanges();
      }).not.toThrow();
      expect(ctx.comparison.comparisonRunIds).toEqual([]);
      expect(ctx.comparison.comparisonPricingBasis).toBe('Current');
    });

    // --- The comparability index caps ---

    it('asks the index about at most the server\'s caps of runs and groups', () => {
      ctx.workspace.historyRuns = Array.from({ length: MAX_COMPARABILITY_INDEX_RUNS + 5 }, (_, i) => buildRun(i + 1, 5));
      ctx.workspace.runGroups = Array.from({ length: MAX_COMPARABILITY_INDEX_GROUPS + 5 }, (_, i) => buildGroup(i + 1, 5));
      benchmarkServiceMock.getComparabilityIndex.mockClear();

      ctx.comparison.loadComparabilityIndex();

      const query = benchmarkServiceMock.getComparabilityIndex.mock.calls[0][0];
      expect(MAX_COMPARABILITY_INDEX_RUNS).toBe(1000);
      expect(MAX_COMPARABILITY_INDEX_GROUPS).toBe(500);
      expect(query.runIds.length).toBe(MAX_COMPARABILITY_INDEX_RUNS);
      expect(query.groupIds.length).toBe(MAX_COMPARABILITY_INDEX_GROUPS);
    });

    // --- Battery results ---

    function buildBatteryRun(id: number, overrides: any = {}): any {
      return {
        id,
        batteryId: 2,
        batteryName: 'Core Battery',
        definitionRevision: 1,
        definitionSha256: 'd'.repeat(64),
        status: 'Completed',
        suiteCount: 3,
        runsPerSuite: 2,
        startedAtUtc: '2026-10-01T10:00:00Z',
        testedModelLabel: `Battery model ${id}`,
        testedProvider: 'Anthropic',
        latestAnalysisId: 100 + id,
        latestAnalysisAtUtc: '2026-10-01T15:00:00Z',
        latestAnalysisComplete: true,
        comparabilityClassSha256: 'c'.repeat(64),
        overallIndex: 70,
        analysisStale: false,
        slots: [],
        members: [],
        suites: [],
        ...overrides
      };
    }

    it('selects battery results through the picker and sends them alone, with no index notices', () => {
      ctx.workspace.batteryRuns = [buildBatteryRun(4), buildBatteryRun(9)];
      ctx.comparison.onComparisonSelectionChange({ runIds: [], groupIds: [], batteryRunIds: [4, 9] });
      benchmarkServiceMock.compareModels.mockClear();

      ctx.comparison.runComparison();

      expect(benchmarkServiceMock.compareModels).toHaveBeenCalledWith({
        runIds: [],
        groupIds: [],
        batteryRunIds: [4, 9],
        pricingBasis: 'Current'
      });
      expect(ctx.comparison.comparisonSelectedSources).toEqual([
        { kind: 'battery', id: 4, label: 'Battery model 4', provider: 'Anthropic', detail: 'Battery run 4' },
        { kind: 'battery', id: 9, label: 'Battery model 9', provider: 'Anthropic', detail: 'Battery run 9' }
      ]);
      // The comparability index does not cover battery results, so it has nothing to say about them.
      ctx.comparison.comparabilityIndexError = 'The index could not be built.';
      expect(ctx.comparison.comparisonSelectionNotices).toEqual([]);

      const stored = JSON.parse(localStorage.getItem(COMPARISON_SELECTION_KEY)!);
      expect(stored.batteryRunIds).toEqual([4, 9]);
      expect(stored.runIds).toEqual([]);
    });

    it('offers the loaded battery runs whatever the suite scope, and keeps their selection through a scope change', () => {
      ctx.workspace.batteryRuns = [buildBatteryRun(4)];
      ctx.comparison.onComparisonSelectionChange({ runIds: [], groupIds: [], batteryRunIds: [4] });

      ctx.comparison.onComparisonSuiteChange(6);

      expect(ctx.comparison.comparisonBatteryRunOptions.map(battery => battery.id)).toEqual([4]);
      expect(ctx.comparison.comparisonBatteryRunIds).toEqual([4]);
    });

    it('removes one battery result through the chip, and clears battery results with the rest', () => {
      ctx.workspace.batteryRuns = [buildBatteryRun(4), buildBatteryRun(9)];
      ctx.comparison.onComparisonSelectionChange({ runIds: [], groupIds: [], batteryRunIds: [4, 9] });

      ctx.comparison.onComparisonRemoveSource(
        { kind: 'battery', id: 4, label: 'Battery model 4', provider: 'Anthropic', detail: 'Battery run 4' });
      expect(ctx.comparison.comparisonBatteryRunIds).toEqual([9]);

      ctx.comparison.clearComparisonSelection();
      expect(ctx.comparison.comparisonBatteryRunIds).toEqual([]);
    });

    it('applies a preset: its battery results selected, runs and groups cleared, back on step 1', () => {
      ctx.workspace.batteryRuns = [buildBatteryRun(4), buildBatteryRun(9)];
      ctx.comparison.onComparisonSelectionChange({ runIds: [1], groupIds: [11] });
      ctx.comparison.comparison = { entries: [] } as any;
      benchmarkServiceMock.getBatteryRuns.mockClear();

      ctx.comparison.applyComparisonPreset({ batteryRunIds: [9, 4, 9] });

      expect(ctx.comparison.comparisonRunIds).toEqual([]);
      expect(ctx.comparison.comparisonGroupIds).toEqual([]);
      expect(ctx.comparison.comparisonBatteryRunIds).toEqual([9, 4]);
      // No payload is what puts the wizard on step 1, where the selection is.
      expect(ctx.comparison.comparison).toBeNull();
      // Both battery runs are already loaded, so Run History is not fetched again.
      expect(benchmarkServiceMock.getBatteryRuns).not.toHaveBeenCalled();
    });

    it('loads Run History when a preset names a battery run that is not loaded yet', () => {
      ctx.workspace.batteryRuns = [];
      benchmarkServiceMock.getBatteryRuns.mockClear();

      ctx.comparison.applyComparisonPreset({ batteryRunIds: [4] });

      expect(benchmarkServiceMock.getBatteryRuns).toHaveBeenCalled();
    });

    it('restores battery results, an older record restoring none', () => {
      localStorage.setItem(COMPARISON_SELECTION_KEY, JSON.stringify({
        runIds: [], groupIds: [], batteryRunIds: [4], suiteId: null, pricingBasis: 'Current'
      }));
      ctx.comparison.restoreComparisonSelection();
      expect(ctx.comparison.comparisonBatteryRunIds).toEqual([4]);

      localStorage.setItem(COMPARISON_SELECTION_KEY, JSON.stringify({
        runIds: [1], groupIds: [], suiteId: null, pricingBasis: 'Current'
      }));
      ctx.comparison.restoreComparisonSelection();
      expect(ctx.comparison.comparisonBatteryRunIds).toEqual([]);
      expect(ctx.comparison.comparisonRunIds).toEqual([1]);
    });

    it('names the battery in Last comparison when the comparison holds battery results', () => {
      storeRecord(storedRecord({
        subjectKind: 'Batteries',
        entryKeys: ['battery:4', 'battery:9'],
        scopeName: 'Core Battery',
        pricingBasis: 'Current',
        pricingBasisLabel: 'Current prices',
        comparableCount: 2,
        excludedCount: 0,
        entries: []
      }));
      showComparisonTab();

      const meta = summaryCard()!.querySelector('.bm-summary-card-meta')!.textContent!.replace(/\s+/g, ' ').trim();
      expect(meta.startsWith('Battery results · Core Battery · computed ')).toBe(true);
      expect(cardFacts()['Pricing']).toBe('Catalog prices Current prices');

      ctx.workspace.batteryRuns = [buildBatteryRun(4), buildBatteryRun(9)];
      ctx.comparison.onComparisonSelectionChange({ runIds: [1], groupIds: [] });
      ctx.comparison.applyComparisonEntries(ctx.comparison.lastComparison!.entryKeys, 'Current');
      expect(ctx.comparison.comparisonRunIds).toEqual([]);
      expect(ctx.comparison.comparisonBatteryRunIds).toEqual([4, 9]);
    });

    it('names battery results as the third source kind in the steps', () => {
      fixture.nativeElement.querySelector('#bm-tab-modelcomparison').click();
      fixture.detectChanges();

      const first = fixture.nativeElement
        .querySelector('.mc-launcher-hero .mc-launcher-howto ol.mc-launcher-steps > li') as HTMLElement;
      expect(first.textContent).toContain('battery results');
    });
  });

  describe('YAML import and export, snapshot upload and delete', () => {
    const suite = {
      id: 1, name: 'Default Suite', description: 'Test', createdAtUtc: '2026-09-01T00:00:00Z', modifiedAtUtc: null,
      questionCount: 2, assessedQuestionCount: 2, difficultyFullyAssessed: true,
      gameSnapshotId: 7, gameSnapshotName: 'Low HP', gameSnapshotCharCount: 12000
    } as any;
    const questions = [
      { id: 11, benchmarkSuiteId: 1, orderIndex: 1, questionText: 'First?', difficulty: 1, expectedPoints: '- a', createdAtUtc: '' },
      { id: 12, benchmarkSuiteId: 1, orderIndex: 2, questionText: 'Second?', difficulty: 3, expectedPoints: null, createdAtUtc: '' }
    ] as any[];

    function host(): HTMLElement {
      return fixture.nativeElement as HTMLElement;
    }

    function tooltipTexts(container: Element): string[] {
      return Array.from(container.querySelectorAll('.gh-tooltip')).map(t => (t.textContent ?? '').trim());
    }

    function showQuestions(): void {
      component.activeSubTab = 'suites';
      // Renders the Suites tab, whose ngOnInit loads the suite list; the state below replaces it.
      ctx.refresh();
      ctx.workspace.suites = [{ ...suite }];
      ctx.workspace.currentSuiteForQuestions = ctx.workspace.suites[0];
      ctx.workspace.questions = questions.map(q => ({ ...q }));
      ctx.workspace.loadingQuestions = false;
      ctx.refresh();
    }

    it('renders the four toolbar icon buttons with their tooltips', () => {
      showQuestions();
      const icons = host().querySelector('.questions-toolbar-icons')!;
      expect(tooltipTexts(icons)).toEqual(['Download All as YAML', 'Copy All to Clipboard', 'Import Questions from YAML', 'Import/Export Help']);
      expect(Array.from(icons.querySelectorAll('button')).every(b => b.getAttribute('aria-label'))).toBe(true);
    });

    it('gives every per-question YAML button a distinct accessible name', () => {
      showQuestions();
      const labels = Array.from(host().querySelectorAll('.card-actions-group button'))
        .map(b => b.getAttribute('aria-label') ?? '')
        .filter(l => l.includes('YAML'));
      expect(labels).toContain('Download question 1 as YAML');
      expect(labels).toContain('Copy question 2 as YAML to the clipboard');
      expect(labels).toContain('Replace question 2 from YAML');
      expect(new Set(labels).size).toBe(labels.length);
    });

    it('renders the suite card export buttons, the toolbar import and Upload Snapshot', () => {
      showQuestions();
      const card = host().querySelector('.suite-card')!;
      expect(tooltipTexts(card.querySelector('.suite-card-export')!)).toEqual(['Download Suite as YAML', 'Copy Suite as YAML to Clipboard']);
      const toolbarLabels = Array.from(host().querySelectorAll('.suites-toolbar button')).map(b => (b.textContent ?? '').trim());
      expect(toolbarLabels).toContain('Import Suite from YAML');
      expect(card.querySelector('.upload-snapshot-card-btn')!.getAttribute('aria-disabled')).toBeNull();
    });

    it('offers suite YAML help on the Manage Suites toolbar', () => {
      showQuestions();
      const toolbar = host().querySelector('.suites-toolbar-grouped')!;
      const help = toolbar.querySelector('.action-btn') as HTMLButtonElement;
      expect(help.getAttribute('aria-label')).toBe('Open suite YAML import and export help');
      expect(tooltipTexts(toolbar)).toEqual(['Suite Import/Export Help']);

      const openSuite = vi.spyOn(ctx.suitesTab().suiteYamlHelpDialog!, 'open').mockReturnValue(undefined);
      const openQuestions = vi.spyOn(ctx.suitesTab().questionYamlHelpDialog!, 'open').mockReturnValue(undefined);
      help.click();
      expect(openSuite).toHaveBeenCalled();
      expect(openQuestions).not.toHaveBeenCalled();
    });

    it('places the Snapshot Suite Wizard after Import Suite from YAML and before the help icon', () => {
      showQuestions();
      const toolbar = host().querySelector('.suites-toolbar-grouped')!;
      const buttons = Array.from(toolbar.querySelectorAll('button'));
      const labels = buttons.map(b => (b.textContent ?? '').trim());
      const wizardIndex = labels.indexOf('Snapshot Suite Wizard');
      expect(wizardIndex).toBe(labels.indexOf('Import Suite from YAML') + 1);
      expect(buttons[wizardIndex + 1].classList).toContain('action-btn');
      expect(buttons[wizardIndex].classList).toContain('btn-ghost');

      const open = vi.spyOn(ctx.suitesTab().snapshotSuiteWizard!, 'open').mockReturnValue(undefined);
      buttons[wizardIndex].click();
      expect(open).toHaveBeenCalled();
    });

    it('closes the suite help before opening the wizard it asks for', () => {
      showQuestions();
      const order: string[] = [];
      vi.spyOn(ctx.suitesTab().suiteYamlHelpDialog!, 'close').mockImplementation(() => { order.push('close help'); });
      vi.spyOn(ctx.suitesTab().snapshotSuiteWizard!, 'open').mockImplementation(() => { order.push('open wizard'); });
      ctx.suitesTab().onSuiteWizardRequestedFromHelp();
      expect(order).toEqual(['close help', 'open wizard']);
    });

    it('opens the assessor for the current copy of the suite the wizard names', () => {
      showQuestions();
      const assess = vi.spyOn(ctx.bridge, 'openDifficultyAssessorDialog').mockReturnValue(undefined);
      const stale = { ...ctx.workspace.suites[0], questionCount: 0 };
      ctx.suitesTab().onWizardAssessRequested(stale);
      expect(assess).toHaveBeenCalledWith(ctx.workspace.suites[0]);
    });

    it('reloads the suites when the wizard applies a description', () => {
      showQuestions();
      const load = vi.spyOn(ctx.workspace, 'loadSuites').mockReturnValue(undefined);
      ctx.suitesTab().onWizardSuiteUpdated();
      expect(load).toHaveBeenCalled();
    });

    it('exports an empty snapshot suite without asking for its questions, and keeps a bare suite inert', async () => {
      showQuestions();
      benchmarkServiceMock.getQuestions.mockClear();
      benchmarkServiceMock.getSnapshot.mockReturnValue(of({
        id: 7, name: 'Low HP', sanitizedText: 'GnollHack 4.2.0 Build 47', charCount: 24,
        sha256: 'a'.repeat(64), captureMethod: 'TextUpload', createdAtUtc: ''
      } as any));
      const emptySnapshotSuite = { ...ctx.workspace.suites[0], questionCount: 0, gameSnapshotId: 7 };
      const bare = { ...ctx.workspace.suites[0], questionCount: 0, gameSnapshotId: null };
      expect(ctx.suitesTab().canExportSuite(emptySnapshotSuite)).toBe(true);
      expect(ctx.suitesTab().canExportSuite(bare)).toBe(false);

      const writeText = vi.fn().mockName('writeText').mockResolvedValue(undefined);
      const original = Object.getOwnPropertyDescriptor(navigator, 'clipboard');
      Object.defineProperty(navigator, 'clipboard', { value: { writeText }, configurable: true, writable: true });
      try {
        await ctx.suitesTab().copySuiteYaml(emptySnapshotSuite);
        expect(benchmarkServiceMock.getQuestions).not.toHaveBeenCalled();
        const yaml = vi.mocked(writeText).mock.lastCall![0] as string;
        expect(yaml).toContain('\nquestions: []\n');
        expect(yaml).toContain('  snapshot:\n');

        writeText.mockClear();
        await ctx.suitesTab().copySuiteYaml(bare);
        expect(writeText).not.toHaveBeenCalled();
      } finally {
        delete (navigator as { clipboard?: unknown }).clipboard;
        if (original) Object.defineProperty(navigator, 'clipboard', original);
      }
    });

    it('routes the import dialog help request by the mode the import was opened for', () => {
      showQuestions();
      const openSuite = vi.spyOn(ctx.suitesTab().suiteYamlHelpDialog!, 'open').mockReturnValue(undefined);
      const openQuestions = vi.spyOn(ctx.suitesTab().questionYamlHelpDialog!, 'open').mockReturnValue(undefined);

      ctx.suitesTab().questionYamlImportDialog!.mode = 'suite';
      ctx.suitesTab().onYamlHelpRequested();
      expect(openSuite).toHaveBeenCalled();
      expect(openQuestions).not.toHaveBeenCalled();

      ctx.suitesTab().questionYamlImportDialog!.mode = 'questions';
      ctx.suitesTab().onYamlHelpRequested();
      expect(openQuestions).toHaveBeenCalled();
    });

    it('exports a suite with its whole snapshot: text, hash and metadata', async () => {
      showQuestions();
      benchmarkServiceMock.getQuestions.mockReturnValue(of(questions.map(q => ({ ...q }))));
      benchmarkServiceMock.getSnapshot.mockReturnValue(of({
        id: 7, name: 'Low HP', sanitizedText: 'GnollHack 4.2.0 Build 47\nDlvl:11 HP:14(58)', charCount: 44,
        sha256: 'a'.repeat(64), captureMethod: 'TextUpload', sourceGnollHackVersion: '4.2.0 Build 47',
        notes: 'From the viewer.', capturedAtUtc: '2026-09-16T18:04:11Z', createdAtUtc: ''
      } as any));

      const writeText = vi.fn().mockName('writeText').mockResolvedValue(undefined);
      const original = Object.getOwnPropertyDescriptor(navigator, 'clipboard');
      Object.defineProperty(navigator, 'clipboard', { value: { writeText }, configurable: true, writable: true });
      try {
        await ctx.suitesTab().copySuiteYaml(ctx.workspace.suites[0]);
        expect(benchmarkServiceMock.getSnapshot).toHaveBeenCalledWith(7, true);
        const yaml = vi.mocked(writeText).mock.lastCall![0] as string;
        expect(yaml).toContain('  snapshot:\n');
        expect(yaml).toContain('    sha256: "' + 'a'.repeat(64) + '"');
        expect(yaml).toContain('    text: |\n');
        expect(yaml).toContain('      GnollHack 4.2.0 Build 47');
        expect(ctx.suitesTab().suitesCopyStatus).toContain('Copied suite Default Suite as YAML');
      } finally {
        delete (navigator as { clipboard?: unknown }).clipboard;
        if (original) Object.defineProperty(navigator, 'clipboard', original);
      }
    });

    it('exports without the board, and says so, when the snapshot fetch fails', async () => {
      showQuestions();
      benchmarkServiceMock.getQuestions.mockReturnValue(of(questions.map(q => ({ ...q }))));
      benchmarkServiceMock.getSnapshot.mockReturnValue(throwError(() => new Error('gone')));

      const writeText = vi.fn().mockName('writeText').mockResolvedValue(undefined);
      const original = Object.getOwnPropertyDescriptor(navigator, 'clipboard');
      Object.defineProperty(navigator, 'clipboard', { value: { writeText }, configurable: true, writable: true });
      try {
        await ctx.suitesTab().copySuiteYaml(ctx.workspace.suites[0]);
        expect(vi.mocked(writeText).mock.lastCall![0] as string).not.toContain('snapshot');
        expect(ctx.suitesTab().suitesCopyStatus).toContain('Exported without the snapshot text');
      } finally {
        delete (navigator as { clipboard?: unknown }).clipboard;
        if (original) Object.defineProperty(navigator, 'clipboard', original);
      }
    });

    it('disables Upload Snapshot while a generation job runs on that suite', () => {
      showQuestions();
      // Set once the Suites tab is shown: its suite load reads the running job from the server.
      ctx.workspace.runningGenerationSuiteId = 1;
      ctx.refresh();
      const button = host().querySelector('.suite-card .upload-snapshot-card-btn') as HTMLButtonElement;
      expect(button.getAttribute('aria-disabled')).toBe('true');

      const open = vi.spyOn(ctx.suitesTab().snapshotUploadDialog!, 'open').mockReturnValue(undefined);
      button.click();
      expect(open).not.toHaveBeenCalled();
      expect(component.snapshotDeleteBlockedReason).toBeNull();
    });

    it('copies one question as YAML', async () => {
      showQuestions();
      const writeText = vi.fn().mockName('writeText').mockResolvedValue(undefined);
      const original = Object.getOwnPropertyDescriptor(navigator, 'clipboard');
      Object.defineProperty(navigator, 'clipboard', { value: { writeText }, configurable: true, writable: true });
      try {
        await ctx.suitesTab().questionsDialogCmp!.copyQuestionYaml(ctx.workspace.questions[0]);
        expect(writeText).toHaveBeenCalledWith(serializeQuestionsYaml([ctx.workspace.questions[0]], ctx.workspace.currentSuiteForQuestions));
        expect(ctx.suitesTab().questionsDialogCmp!.questionsCopyStatus).toBe('Copied question 1 as YAML.');
      } finally {
        delete (navigator as { clipboard?: unknown }).clipboard;
        if (original) Object.defineProperty(navigator, 'clipboard', original);
      }
    });

    it('reloads questions and suites after an import', () => {
      showQuestions();
      benchmarkServiceMock.getQuestions.mockClear();
      benchmarkServiceMock.getSuites.mockClear();

      ctx.suitesTab().onQuestionsImported({ createdCount: 1, replacedCount: 0, unchangedCount: 0, questions: [] });
      expect(benchmarkServiceMock.getQuestions).toHaveBeenCalledWith(1);
      expect(benchmarkServiceMock.getSuites).toHaveBeenCalled();

      benchmarkServiceMock.getSuites.mockClear();
      ctx.suitesTab().onSuiteImported({ ...suite, id: 5, name: 'Imported' });
      expect(benchmarkServiceMock.getSuites).toHaveBeenCalled();
    });

    it('patches the suite card after an upload', () => {
      showQuestions();
      const card = ctx.workspace.suites[0];
      Object.assign(card, { gameSnapshotId: null });
      ctx.suitesTab().onSnapshotUploaded({
        board: { id: 40, name: 'New board', charCount: 321 } as any,
        suite: { ...suite, gameSnapshotId: 40, gameSnapshotName: 'New board', gameSnapshotCharCount: 321 }
      });
      // loadSuites then replaces the list from the mock, so the patched card object is checked.
      expect(card.gameSnapshotId).toBe(40);
      expect(card.gameSnapshotName).toBe('New board');
    });

    it('clears the snapshot fields after a delete and reloads', () => {
      showQuestions();
      benchmarkServiceMock.getSuites.mockClear();
      const card = ctx.workspace.suites[0];

      component.onSnapshotDeleted(7);

      expect(card.gameSnapshotId).toBeNull();
      expect(card.gameSnapshotName).toBeNull();
      expect(ctx.workspace.currentSuiteForQuestions!.gameSnapshotId).toBeNull();
      expect(benchmarkServiceMock.getSuites).toHaveBeenCalled();
    });
  });
});
