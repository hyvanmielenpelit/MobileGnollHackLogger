import type { MockedObject } from "vitest";
import { ChangeDetectorRef } from '@angular/core';
import { ComponentFixture } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { of, throwError, Subject } from 'rxjs';
import { AdminBenchmarkComponent } from './benchmark.component';
import { AdminBenchmarkService } from '../../services/admin-benchmark.service';
import { serializeQuestionsYaml } from './question-yaml/question-yaml-format';
import { COMPARISON_WIZARD_STEPS } from './model-comparison/model-comparison.component';
import { ReportDocumentsLauncherComponent } from './report-pack/report-documents-launcher.component';
import {
  clearStoredState, createAdminBenchmarkFixture, COMPARISON_SELECTION_KEY, COMPARISON_LAUNCHER_KEY
} from './benchmark.component.testing';

describe('AdminBenchmarkComponent', () => {
  let component: AdminBenchmarkComponent;
  let fixture: ComponentFixture<AdminBenchmarkComponent>;
  let benchmarkServiceMock: MockedObject<AdminBenchmarkService>;

  beforeEach(clearStoredState);

  afterEach(clearStoredState);

  beforeEach(async () => {
    ({ component, fixture, benchmarkServiceMock } = await createAdminBenchmarkFixture());
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
      component.historyRuns = [buildRun(1, 5), buildRun(2, 5), buildRun(3, 6)];
      component.runGroups = [buildGroup(11, 5), buildGroup(12, 6)];
      fixture.detectChanges();
    });

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
      component.comparisonRunIds = [1, 2];
      component.comparisonGroupIds = [11];
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
      // No comparison yet: the state list is absent rather than empty.
      expect(launcher.querySelector('.mc-launcher-state')).toBeNull();
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
      component.comparisonHowItWorksOpen = null;
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

      component.comparisonHowItWorksOpen = null;
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
      expect(component.comparisonHowItWorksOpen).toBe(false);
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
      fixture.detectChanges();
      expect(comparisonReportLoads()).toBe(3);
    });

    it('states what the last comparison produced once one exists', () => {
      fixture.nativeElement.querySelector('#bm-tab-modelcomparison').click();
      component.comparison = {
        baselineSuiteName: 'Suite 5',
        pricingBasis: 'Current',
        pricingBasisLabel: 'Current prices',
        comparableCount: 2,
        entries: [{}, {}, {}],
        computedAtUtc: '2026-09-08T10:00:00Z'
      } as any;
      // As runComparison does on its response.
      (component as unknown as { cdr: ChangeDetectorRef }).cdr.detectChanges();

      const state = fixture.nativeElement.querySelector('.mc-launcher .mc-launcher-state');
      expect(state).toBeTruthy();
      const terms = Array.from(state.querySelectorAll('dt')).map((dt: any) => dt.textContent.trim());
      expect(terms).toEqual(['Suite', 'Pricing basis', 'Charted', 'Computed']);
      expect(state.textContent).toContain('2 of 3 entries');
    });

    it('lists the four wizard steps under the titles the wizard itself uses', () => {
      fixture.nativeElement.querySelector('#bm-tab-modelcomparison').click();
      fixture.detectChanges();

      const items = Array.from(
        fixture.nativeElement.querySelectorAll('.mc-launcher-hero .mc-launcher-howto ol.mc-launcher-steps > li')) as HTMLElement[];
      expect(items.length).toBe(4);
      expect(COMPARISON_WIZARD_STEPS.map(step => step.title)).toEqual(['Sources', 'Charts & table', 'Reports', 'Documents']);
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

    it('loads the comparability index for the sources on offer, and survives it failing', () => {
      benchmarkServiceMock.getComparabilityIndex.mockClear();

      // A new suite scope is a new set of offered sources, so it re-indexes them.
      component.onComparisonSuiteChange(5);

      expect(benchmarkServiceMock.getComparabilityIndex).toHaveBeenCalledWith({
        runIds: [1, 2],
        groupIds: [11]
      });
      expect(component.comparabilityIndex).toBeTruthy();

      benchmarkServiceMock.getComparabilityIndex.mockReturnValue(throwError(() => ({ error: 'The index could not be built.' })));
      component.onComparisonSuiteChange(6);

      // Non-fatal: the Condition column falls back to a dash and Compare still works.
      expect(component.comparabilityIndex).toBeNull();
      expect(component.comparabilityIndexError).toContain('could not be built');
    });

    it('derives the wizard band notices from the index, the selection and the pricing basis', () => {
      // One owner: the picker's checkboxes and the wizard's band both read this list, so neither
      // can hold its own account of what the selection costs.
      component.comparabilityIndexError = null;
      component.comparabilityIndexLoading = false;
      component.comparabilityIndex = {
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

      component.comparisonRunIds = [1, 2];
      component.comparisonGroupIds = [];
      expect(component.comparisonSelectionNotices).toEqual([]);

      component.comparisonRunIds = [1, 2, 3];
      expect(component.comparisonSelectionNotices.map(notice => notice.id))
        .toEqual(['cross-condition']);

      component.comparabilityIndex = null;
      component.comparabilityIndexError = 'The index could not be built.';
      const failed = component.comparisonSelectionNotices;
      expect(failed.map(notice => notice.id)).toEqual(['index-error']);
      expect(failed[0].severity).toBe('error');
      expect(failed[0].body).toContain('The index could not be built.');
    });

    it('drops the payload when the selection changes, so Compare is asked for again', () => {
      component.comparison = { entries: [] } as any;
      component.onComparisonSelectionChange({ runIds: [1], groupIds: [] });

      // The figures on hand describe the previous set of sources. It is also what the wizard reads
      // to know Compare has not run for this selection yet.
      expect(component.comparison).toBeNull();
    });

    // --- The selection band's chips ---

    it('names every selected source for the band, runs then groups, skipping one outside suite scope', () => {
      component.comparisonSuiteId = 5;
      component.comparisonRunIds = [1, 3, 2];
      component.comparisonGroupIds = [11, 12];

      // Run 3 and group 12 belong to suite 6, which the current scope no longer offers: skipped
      // rather than rendered as a placeholder, same as the picker's own checkboxes.
      expect(component.comparisonSelectedSources).toEqual([
        { kind: 'run', id: 1, label: 'Model 1', provider: 'Google', detail: '#1' },
        { kind: 'run', id: 2, label: 'Model 2', provider: 'Google', detail: '#2' },
        { kind: 'group', id: 11, label: 'Group 11', provider: null, detail: '3 runs' }
      ]);
    });

    it('names a group of one run in the singular', () => {
      component.runGroups = [...component.runGroups, { ...buildGroup(13, 5), runCount: 1 }];
      component.comparisonGroupIds = [13];

      expect(component.comparisonSelectedSources).toEqual([
        { kind: 'group', id: 13, label: 'Group 13', provider: null, detail: '1 run' }
      ]);
    });

    it('removes one source through the same path every other selection change takes', () => {
      component.comparisonRunIds = [1, 2];
      component.comparisonGroupIds = [11];
      component.comparison = { entries: [] } as any;

      component.onComparisonRemoveSource(
        { kind: 'run', id: 1, label: 'Model 1', provider: 'Google', detail: '#1' });

      expect(component.comparisonRunIds).toEqual([2]);
      expect(component.comparisonGroupIds).toEqual([11]);
      // Persistence, the dropped payload and the Compare reset are onComparisonSelectionChange's
      // job, so routing through it is what keeps them all in force after a chip is removed.
      expect(component.comparison).toBeNull();
    });

    // --- The .gh-dialog-fullscreen lift ---

    it('leaves the suite health dialog opening and closing after the full-screen lift', () => {
      // Its viewport sizing, transition and backdrop now come from styles.scss, and it is the only
      // other consumer of that block, so this is the regression guard for the move.
      const dialog = fixture.nativeElement
        .querySelector('.benchmark-suite-health-dialog') as HTMLDialogElement;
      expect(dialog.classList.contains('gh-dialog-fullscreen')).toBe(true);

      component.suiteHealthSuiteId = null;
      component.openSuiteHealth({ id: 5, name: 'Suite 5', questionCount: 4 } as any);
      fixture.detectChanges();
      expect(dialog.open).toBe(true);

      component.closeSuiteHealth();
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

      expect(benchmarkServiceMock.getRuns).toHaveBeenCalled();
      expect(benchmarkServiceMock.getRunGroups).toHaveBeenCalled();
      expect(benchmarkServiceMock.getSuites).toHaveBeenCalled();
      // An unattended request on tab entry would re-price for a selection nobody confirmed.
      expect(benchmarkServiceMock.compareModels).not.toHaveBeenCalled();
    });

    it('issues one request carrying the selected ids and the basis name', () => {
      component.onComparisonSelectionChange({ runIds: [1, 2], groupIds: [11] });
      benchmarkServiceMock.compareModels.mockClear();

      component.runComparison();

      expect(benchmarkServiceMock.compareModels).toHaveBeenCalledTimes(1);
      expect(benchmarkServiceMock.compareModels).toHaveBeenCalledWith({
        runIds: [1, 2],
        groupIds: [11],
        pricingBasis: 'Current'
      });
    });

    it('refuses an empty selection rather than sending a request the server will reject', () => {
      component.clearComparisonSelection();
      benchmarkServiceMock.compareModels.mockClear();

      component.runComparison();

      expect(benchmarkServiceMock.compareModels).not.toHaveBeenCalled();
      expect(component.comparisonError).toContain('at least one run');
    });

    it('reports the server error text rather than a generic failure', () => {
      benchmarkServiceMock.compareModels.mockReturnValue(throwError(() => ({ error: 'Run(s) not found: 4' })));
      component.onComparisonSelectionChange({ runIds: [4], groupIds: [] });

      component.runComparison();

      expect(component.comparisonError).toBe('Run(s) not found: 4');
      expect(component.comparison).toBeNull();
      expect(component.comparisonLoading).toBe(false);
    });

    it('discards an out-of-order response so the older payload never overwrites the newer', () => {
      const first = new Subject<any>();
      const second = new Subject<any>();
      benchmarkServiceMock.compareModels.mockReturnValueOnce(first as any).mockReturnValueOnce(second as any);
      component.onComparisonSelectionChange({ runIds: [1], groupIds: [] });

      component.runComparison();
      component.runComparison();

      second.next({ entries: [], explanation: 'newer' });
      first.next({ entries: [], explanation: 'older' });

      expect((component.comparison as any).explanation).toBe('newer');
    });

    // --- Cancelling, and never trapping the operator in the wizard while loading ---

    it('cancels the comparison in flight, releasing the request and ignoring its late result', () => {
      const request = new Subject<any>();
      benchmarkServiceMock.compareModels.mockReturnValue(request as any);
      component.onComparisonSelectionChange({ runIds: [1, 2], groupIds: [] });

      component.runComparison();
      expect(component.comparisonLoading).toBe(true);
      expect(request.observed).toBe(true);

      component.cancelComparison();

      expect(component.comparisonLoading).toBe(false);
      // Unsubscribed, so the HTTP request is aborted and the server stops pricing.
      expect(request.observed).toBe(false);
      request.next({ entries: [], explanation: 'late' });
      expect(component.comparison).toBeNull();
    });

    it('releases a superseded request when Compare runs again', () => {
      const first = new Subject<any>();
      const second = new Subject<any>();
      benchmarkServiceMock.compareModels.mockReturnValueOnce(first as any).mockReturnValueOnce(second as any);
      component.onComparisonSelectionChange({ runIds: [1], groupIds: [] });

      component.runComparison();
      component.runComparison();

      expect(first.observed).toBe(false);
      expect(second.observed).toBe(true);
    });

    it('drops the request in flight when the selection changes under it', () => {
      const request = new Subject<any>();
      benchmarkServiceMock.compareModels.mockReturnValue(request as any);
      component.onComparisonSelectionChange({ runIds: [1], groupIds: [] });
      component.runComparison();

      component.onComparisonSelectionChange({ runIds: [1, 2], groupIds: [] });
      request.next({ entries: [], explanation: 'for the previous selection' });

      // The older response would otherwise chart the previous selection and advance the wizard.
      expect(component.comparison).toBeNull();
      expect(component.comparisonLoading).toBe(false);
      expect(request.observed).toBe(false);
    });

    it('treats a cancel with nothing in flight as a no-op', () => {
      const detectChanges = vi.spyOn((component as any).cdr, 'detectChanges');

      component.cancelComparison();

      expect(component.comparisonLoading).toBe(false);
      expect(detectChanges).not.toHaveBeenCalled();
    });

    it('never refuses Escape because a comparison is loading', () => {
      component.comparisonLoading = true;
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
      component.onComparisonSelectionChange({ runIds: [1, 2], groupIds: [] });
      component.openComparisonWizard();
      fixture.detectChanges();

      component.runComparison();
      fixture.detectChanges();
      expect(component.comparisonLoading).toBe(true);

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
      component.onComparisonSuiteChange(5);

      expect(component.comparisonRunOptions.map(r => r.id)).toEqual([1, 2]);
      expect(component.comparisonGroupOptions.map(g => g.id)).toEqual([11]);

      component.onComparisonSuiteChange(null);
      expect(component.comparisonRunOptions.length).toBe(3);
      expect(component.comparisonGroupOptions.length).toBe(2);
    });

    it('drops out-of-scope ids when the suite scope changes', () => {
      component.onComparisonSelectionChange({ runIds: [1, 3], groupIds: [11, 12] });

      component.onComparisonSuiteChange(5);

      // Run 3 and group 12 belong to suite 6: leaving them selected is how a figure ends up with
      // a model the picker does not show.
      expect(component.comparisonRunIds).toEqual([1]);
      expect(component.comparisonGroupIds).toEqual([11]);
    });

    it('clears the figures when nothing survives a suite scope change', () => {
      component.onComparisonSelectionChange({ runIds: [3], groupIds: [] });
      component.comparison = { entries: [] } as any;

      component.onComparisonSuiteChange(5);

      expect(component.comparisonRunIds).toEqual([]);
      expect(component.comparison).toBeNull();
    });

    // --- Pricing basis ---

    it('refetches at once on a pricing basis change, because it re-prices an unchanged set', () => {
      component.onComparisonSelectionChange({ runIds: [1], groupIds: [] });
      benchmarkServiceMock.compareModels.mockClear();

      component.onComparisonPricingBasisChange('AsRun');

      expect(component.comparisonPricingBasis).toBe('AsRun');
      expect(benchmarkServiceMock.compareModels).toHaveBeenCalledWith(expect.objectContaining({ pricingBasis: 'AsRun' }));
    });

    // --- Persistence ---

    it('remembers the selection, the scope and the basis across a reload', () => {
      component.onComparisonSuiteChange(5);
      component.onComparisonSelectionChange({ runIds: [1, 2], groupIds: [11] });

      const stored = JSON.parse(localStorage.getItem(COMPARISON_SELECTION_KEY)!);
      expect(stored).toEqual({
        runIds: [1, 2], groupIds: [11], suiteId: 5, pricingBasis: 'Current'
      });
    });

    it('drops a persisted id that no longer exists rather than sending it', () => {
      localStorage.setItem(COMPARISON_SELECTION_KEY, JSON.stringify({
        runIds: [1, 999], groupIds: [11, 888], suiteId: null, pricingBasis: 'AsRun'
      }));

      component.selectSubTab('modelcomparison');

      // getRuns and getRunGroups both resolve to [] under the default mocks, so the lists that
      // validate the restore are re-seeded here to what the tab actually offers.
      component.historyRuns = [buildRun(1, 5)];
      component.runGroups = [buildGroup(11, 5)];
      (component as any).pruneComparisonSelection();

      expect(component.comparisonRunIds).toEqual([1]);
      expect(component.comparisonGroupIds).toEqual([11]);
      expect(component.comparisonPricingBasis).toBe('AsRun');
    });

    it('survives a localStorage read that throws, leaving every default standing', () => {
      vi.spyOn(localStorage, 'getItem').mockImplementation(() => {
        throw new Error('private browsing');
      });

      expect(() => component.selectSubTab('modelcomparison')).not.toThrow();
      expect(component.comparisonRunIds).toEqual([]);
      expect(component.comparisonPricingBasis).toBe('Current');
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
      component.suites = [{ ...suite }];
      component.currentSuiteForQuestions = component.suites[0];
      component.questions = questions.map(q => ({ ...q }));
      component.loadingQuestions = false;
      fixture.detectChanges();
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

      const openSuite = vi.spyOn(component.suiteYamlHelpDialog!, 'open').mockReturnValue(undefined);
      const openQuestions = vi.spyOn(component.questionYamlHelpDialog!, 'open').mockReturnValue(undefined);
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

      const open = vi.spyOn(component.snapshotSuiteWizard!, 'open').mockReturnValue(undefined);
      buttons[wizardIndex].click();
      expect(open).toHaveBeenCalled();
    });

    it('closes the suite help before opening the wizard it asks for', () => {
      showQuestions();
      const order: string[] = [];
      vi.spyOn(component.suiteYamlHelpDialog!, 'close').mockImplementation(() => { order.push('close help'); });
      vi.spyOn(component.snapshotSuiteWizard!, 'open').mockImplementation(() => { order.push('open wizard'); });
      component.onSuiteWizardRequestedFromHelp();
      expect(order).toEqual(['close help', 'open wizard']);
    });

    it('opens the assessor for the current copy of the suite the wizard names', () => {
      showQuestions();
      const assess = vi.spyOn(component, 'openDifficultyAssessorDialog').mockReturnValue(undefined);
      const stale = { ...component.suites[0], questionCount: 0 };
      component.onWizardAssessRequested(stale);
      expect(assess).toHaveBeenCalledWith(component.suites[0]);
    });

    it('reloads the suites when the wizard applies a description', () => {
      showQuestions();
      const load = vi.spyOn(component, 'loadSuites').mockReturnValue(undefined);
      component.onWizardSuiteUpdated();
      expect(load).toHaveBeenCalled();
    });

    it('exports an empty snapshot suite without asking for its questions, and keeps a bare suite inert', async () => {
      showQuestions();
      benchmarkServiceMock.getQuestions.mockClear();
      benchmarkServiceMock.getSnapshot.mockReturnValue(of({
        id: 7, name: 'Low HP', sanitizedText: 'GnollHack 4.2.0 Build 47', charCount: 24,
        sha256: 'a'.repeat(64), captureMethod: 'TextUpload', createdAtUtc: ''
      } as any));
      const emptySnapshotSuite = { ...component.suites[0], questionCount: 0, gameSnapshotId: 7 };
      const bare = { ...component.suites[0], questionCount: 0, gameSnapshotId: null };
      expect(component.canExportSuite(emptySnapshotSuite)).toBe(true);
      expect(component.canExportSuite(bare)).toBe(false);

      const writeText = vi.fn().mockName('writeText').mockResolvedValue(undefined);
      const original = Object.getOwnPropertyDescriptor(navigator, 'clipboard');
      Object.defineProperty(navigator, 'clipboard', { value: { writeText }, configurable: true, writable: true });
      try {
        await component.copySuiteYaml(emptySnapshotSuite);
        expect(benchmarkServiceMock.getQuestions).not.toHaveBeenCalled();
        const yaml = vi.mocked(writeText).mock.lastCall![0] as string;
        expect(yaml).toContain('\nquestions: []\n');
        expect(yaml).toContain('  snapshot:\n');

        writeText.mockClear();
        await component.copySuiteYaml(bare);
        expect(writeText).not.toHaveBeenCalled();
      } finally {
        delete (navigator as { clipboard?: unknown }).clipboard;
        if (original) Object.defineProperty(navigator, 'clipboard', original);
      }
    });

    it('routes the import dialog help request by the mode the import was opened for', () => {
      showQuestions();
      const openSuite = vi.spyOn(component.suiteYamlHelpDialog!, 'open').mockReturnValue(undefined);
      const openQuestions = vi.spyOn(component.questionYamlHelpDialog!, 'open').mockReturnValue(undefined);

      component.questionYamlImportDialog!.mode = 'suite';
      component.onYamlHelpRequested();
      expect(openSuite).toHaveBeenCalled();
      expect(openQuestions).not.toHaveBeenCalled();

      component.questionYamlImportDialog!.mode = 'questions';
      component.onYamlHelpRequested();
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
        await component.copySuiteYaml(component.suites[0]);
        expect(benchmarkServiceMock.getSnapshot).toHaveBeenCalledWith(7, true);
        const yaml = vi.mocked(writeText).mock.lastCall![0] as string;
        expect(yaml).toContain('  snapshot:\n');
        expect(yaml).toContain('    sha256: "' + 'a'.repeat(64) + '"');
        expect(yaml).toContain('    text: |\n');
        expect(yaml).toContain('      GnollHack 4.2.0 Build 47');
        expect(component.suitesCopyStatus).toContain('Copied suite Default Suite as YAML');
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
        await component.copySuiteYaml(component.suites[0]);
        expect(vi.mocked(writeText).mock.lastCall![0] as string).not.toContain('snapshot');
        expect(component.suitesCopyStatus).toContain('Exported without the snapshot text');
      } finally {
        delete (navigator as { clipboard?: unknown }).clipboard;
        if (original) Object.defineProperty(navigator, 'clipboard', original);
      }
    });

    it('disables Upload Snapshot while a generation job runs on that suite', () => {
      component.runningGenerationSuiteId = 1;
      showQuestions();
      const button = host().querySelector('.suite-card .upload-snapshot-card-btn') as HTMLButtonElement;
      expect(button.getAttribute('aria-disabled')).toBe('true');

      const open = vi.spyOn(component.snapshotUploadDialog!, 'open').mockReturnValue(undefined);
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
        await component.copyQuestionYaml(component.questions[0]);
        expect(writeText).toHaveBeenCalledWith(serializeQuestionsYaml([component.questions[0]], component.currentSuiteForQuestions));
        expect(component.questionsCopyStatus).toBe('Copied question 1 as YAML.');
      } finally {
        delete (navigator as { clipboard?: unknown }).clipboard;
        if (original) Object.defineProperty(navigator, 'clipboard', original);
      }
    });

    it('reloads questions and suites after an import', () => {
      showQuestions();
      benchmarkServiceMock.getQuestions.mockClear();
      benchmarkServiceMock.getSuites.mockClear();

      component.onQuestionsImported({ createdCount: 1, replacedCount: 0, unchangedCount: 0, questions: [] });
      expect(benchmarkServiceMock.getQuestions).toHaveBeenCalledWith(1);
      expect(benchmarkServiceMock.getSuites).toHaveBeenCalled();

      benchmarkServiceMock.getSuites.mockClear();
      component.onSuiteImported({ ...suite, id: 5, name: 'Imported' });
      expect(benchmarkServiceMock.getSuites).toHaveBeenCalled();
    });

    it('patches the suite card after an upload', () => {
      showQuestions();
      const card = component.suites[0];
      Object.assign(card, { gameSnapshotId: null });
      component.onSnapshotUploaded({
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
      const card = component.suites[0];

      component.onSnapshotDeleted(7);

      expect(card.gameSnapshotId).toBeNull();
      expect(card.gameSnapshotName).toBeNull();
      expect(component.currentSuiteForQuestions!.gameSnapshotId).toBeNull();
      expect(benchmarkServiceMock.getSuites).toHaveBeenCalled();
    });
  });
});
