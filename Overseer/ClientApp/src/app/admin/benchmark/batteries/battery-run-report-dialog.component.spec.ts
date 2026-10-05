import { TestBed, fakeAsync, tick } from '@angular/core/testing';
import { of, throwError } from 'rxjs';

import { BenchmarkRunReportDocumentsStatus } from '../../../services/admin-benchmark.service';
import { keyFiguresImageIo } from '../run-report-frame/key-figures-image';
import {
  defaultKeyFiguresExportSettings,
  readStoredKeyFiguresExportSettings,
  writeStoredKeyFiguresExportSettings
} from '../run-report-frame/key-figures-export-settings';
import {
  BATTERY_RUN_KEY_FIGURES,
  BATTERY_RUN_REPORT_HEADER_STORAGE_KEY,
  BATTERY_RUN_REPORT_KEY_FIGURES_STORAGE_KEY,
  BATTERY_RUN_REPORT_TABS,
  BATTERY_RUN_REPORT_TAB_STORAGE_KEY,
  BatteryRunReportDialogComponent,
  batteryRunDiagnosticsText,
  formatBatteryWallClock
} from './battery-run-report-dialog.component';
import {
  BatteryRunReportHarness,
  analysis,
  batteryRun,
  completeResult,
  configureBatteryRunReport,
  member,
  slot,
  tearDownBatteryRunReport
} from './battery-run-report-dialog.testing';

/** The text assistive technology reads: `aria-hidden` parts left out, whitespace collapsed. */
function accessibleText(element: HTMLElement): string {
  const copy = element.cloneNode(true) as HTMLElement;
  copy.querySelectorAll('[aria-hidden="true"]').forEach(node => node.remove());
  return (copy.textContent ?? '').replace(/\s+/g, ' ').replace(/\s+,/g, ',').trim();
}

describe('BatteryRunReportDialogComponent', () => {
  let h: BatteryRunReportHarness;

  beforeEach(async () => {
    h = await configureBatteryRunReport();
  });

  afterEach(() => {
    tearDownBatteryRunReport(h);
  });

  describe('opening and closing', () => {
    it('loads the battery run and its analysis, and focuses the title', () => {
      h.open();

      expect(h.service.getBatteryRun).toHaveBeenCalledWith(7);
      expect(h.service.getBatteryAnalysis).toHaveBeenCalledWith(7);
      expect(h.dialog().open).toBe(true);
      expect(h.dialog().getAttribute('aria-labelledby')).toBe('brrTitle');
      expect(h.dialog().classList).toContain('gh-dialog-fullscreen');
      const title = h.el().querySelector('h3#brrTitle') as HTMLElement;
      expect(title.textContent?.trim()).toBe('Battery Run #7');
      expect(title.getAttribute('tabindex')).toBe('-1');
      expect(document.activeElement).toBe(title);
      expect(h.el().querySelector('app-run-report-frame')?.getAttribute('layout')).toBe('single');
      expect(h.el().querySelector('.rr-identity > img.gnollbench-emblem')?.getAttribute('alt')).toBe('');
    });

    it('closes from Close, emits closed once and clears the battery run', () => {
      const closed = vi.fn().mockName('closed');
      h.component.closed.subscribe(closed);
      h.open();

      const close = h.el().querySelector('.rr-close') as HTMLButtonElement;
      expect(close.getAttribute('aria-label')).toBe('Close battery run report');
      expect(close.closest('[role="group"]')).toBeNull();
      close.click();
      h.fixture.detectChanges();

      expect(h.dialog().open).toBe(false);
      expect(closed).toHaveBeenCalledTimes(1);
      expect(h.component.batteryRunId).toBeNull();
      expect(h.el().querySelector('#brrTitle')).toBeNull();
    });

    it('closes on Escape', () => {
      const closed = vi.fn().mockName('closed');
      h.component.closed.subscribe(closed);
      h.open();

      h.dialog().dispatchEvent(new Event('cancel', { cancelable: true }));
      h.fixture.detectChanges();

      expect(h.dialog().open).toBe(false);
      expect(closed).toHaveBeenCalledTimes(1);
    });

    it('shows a load failure with Try again, and no tab row', () => {
      h.service.getBatteryRun.mockReturnValue(throwError(() => ({ status: 404 })));
      h.open();

      expect(h.text('.brr-load-error')).toContain('Battery run #7 no longer exists.');
      expect(h.el().querySelector('[role="tablist"]')).toBeNull();
      expect(h.el().querySelector('.rr-close')).not.toBeNull();

      h.service.getBatteryRun.mockReturnValue(of(batteryRun()));
      h.click('#brr-retry-load');
      expect(h.el().querySelector('[role="tablist"]')).not.toBeNull();
    });

    it('polls while the battery run is live and stops when the dialog closes', fakeAsync(() => {
      h.service.getBatteryRun.mockReturnValue(of(batteryRun({ status: 'Running', completedAtUtc: null })));
      h.open();
      expect(h.service.getBatteryRun).toHaveBeenCalledTimes(1);

      tick(BatteryRunReportDialogComponent.POLL_INTERVAL_MS);
      expect(h.service.getBatteryRun).toHaveBeenCalledTimes(2);

      h.component.close();
      tick(BatteryRunReportDialogComponent.POLL_INTERVAL_MS * 3);
      expect(h.service.getBatteryRun).toHaveBeenCalledTimes(2);
    }));

    it('stops polling once the battery run is no longer live, and reloads a new analysis', fakeAsync(() => {
      h.service.getBatteryRun.mockReturnValue(of(batteryRun({ status: 'Running', completedAtUtc: null, latestAnalysisId: null })));
      h.open();
      h.service.getBatteryRun.mockReturnValue(of(batteryRun()));

      tick(BatteryRunReportDialogComponent.POLL_INTERVAL_MS);
      expect(h.service.getBatteryRun).toHaveBeenCalledTimes(2);
      expect(h.service.getBatteryAnalysis).toHaveBeenCalledTimes(2);

      tick(BatteryRunReportDialogComponent.POLL_INTERVAL_MS * 3);
      expect(h.service.getBatteryRun).toHaveBeenCalledTimes(2);
      h.component.close();
    }));

    it('does not poll a finished battery run', fakeAsync(() => {
      h.open();
      tick(BatteryRunReportDialogComponent.POLL_INTERVAL_MS * 2);
      expect(h.service.getBatteryRun).toHaveBeenCalledTimes(1);
      h.component.close();
    }));
  });

  describe('header', () => {
    it('lists Model and Assessor as primary facts and the rest in a closed Run details', () => {
      h.open();

      const primary = h.el().querySelector('.rr-run-facts-primary') as HTMLElement;
      const primaryKeys = Array.from(primary.querySelectorAll('[data-fact]')).map(f => f.getAttribute('data-fact'));
      expect(primaryKeys).toEqual(['model', 'assessor']);
      expect(primary.textContent).toContain('Model X');
      expect(primary.textContent).toContain('Assessor Y');

      const details = h.el().querySelector('details#brr-run-details') as HTMLDetailsElement;
      expect(details.classList).toContain('gh-disclosure');
      expect(details.open).toBe(false);
      const detailKeys = Array.from(details.querySelectorAll('[data-fact]')).map(f => f.getAttribute('data-fact'));
      expect(detailKeys).toEqual(['profile', 'started', 'battery', 'suites']);
      expect(h.text('.rr-run-details-readout')).toContain('Core Battery · Revision 2 · Questions and difficulty');
      expect(h.text('.rr-run-details-readout')).toContain('2 of 2 complete · 1 run per suite');
      expect(h.el().querySelector('.rr-run-details-readout')?.getAttribute('aria-hidden')).toBe('true');
      expect(h.text('.dialog-title-badges .status-badge')).toBe('Completed');
    });

    it('remembers the Run details open state', () => {
      h.open();
      const details = h.el().querySelector('details#brr-run-details') as HTMLDetailsElement;
      details.open = true;
      details.dispatchEvent(new Event('toggle'));

      expect(JSON.parse(localStorage.getItem(BATTERY_RUN_REPORT_HEADER_STORAGE_KEY)!)).toEqual({ version: 1, detailsOpen: true });
      h.component.close();

      const second = TestBed.createComponent(BatteryRunReportDialogComponent);
      second.detectChanges();
      second.componentInstance.open(7);
      second.detectChanges();
      expect((second.nativeElement.querySelector('details#brr-run-details') as HTMLDetailsElement).open).toBe(true);
      second.componentInstance.close();
      second.destroy();
    });

    it('groups Downloads, Actions and Copy diagnostics as the battery run actions', () => {
      h.open();
      const group = h.el().querySelector('.rr-header-controls > [role="group"]') as HTMLElement;
      expect(group.getAttribute('aria-label')).toBe('Battery run actions');
      expect(group.querySelector('#brr-downloads-trigger')?.textContent?.trim()).toBe('Downloads');
      expect(group.querySelector('#brr-actions-trigger')?.getAttribute('popovertarget')).toBe('brr-actions-popover');
      const copy = group.querySelector('#brr-copy-diagnostics-btn') as HTMLButtonElement;
      expect(copy.getAttribute('aria-label')).toBe('Copy diagnostics of battery run 7');
      expect(copy.hasAttribute('title')).toBe(false);
      expect(group.querySelector('#brr-copy-diagnostics-tip')?.getAttribute('popover')).toBe('hint');
      const popover = group.querySelector('#brr-actions-popover') as HTMLElement;
      expect(popover.getAttribute('role')).toBe('group');
      expect(popover.getAttribute('popover')).toBe('auto');
      expect(Array.from(popover.querySelectorAll('[data-action]')).map(b => b.getAttribute('data-action')))
        .toEqual(['recompute', 'progress']);
    });

    it('opens the Download Center on a battery context, labeled without the id its subtitle already gives', () => {
      h.open();
      h.click('#brr-downloads-trigger');
      expect(h.downloadCenter().open).toHaveBeenCalledWith({
        kind: 'battery',
        batteryRunId: 7,
        label: 'Core Battery · Model X'
      });
    });

    it('hands the host\'s member diagnostics callback to the Download Center and to the AI Reports tab', () => {
      const memberDiagnosticsText = (run: { id: number }): string => `diagnostics of run ${run.id}`;
      h.fixture.componentRef.setInput('memberDiagnosticsText', memberDiagnosticsText);
      h.open();
      expect(h.aiReports().memberDiagnosticsText).toBe(memberDiagnosticsText);

      h.click('#brr-downloads-trigger');
      expect(h.downloadCenter().open).toHaveBeenCalledWith({
        kind: 'battery',
        batteryRunId: 7,
        label: 'Core Battery · Model X',
        memberDiagnosticsText
      });
    });
  });

  describe('actions popover', () => {
    function reason(key: string): string {
      return (h.action(key).querySelector('.gh-action-popover-item-reason')?.textContent ?? '').trim();
    }

    it('holds only Recompute and Show progress: Continue and the re-run belong to the progress dialog', () => {
      h.service.getBatteryRun.mockReturnValue(of(batteryRun({ status: 'Stopped', stopReason: 'MemberFailed', resumable: true, completedAtUtc: null })));
      h.open();

      expect(Array.from(h.el().querySelectorAll('#brr-actions-popover [data-action]')).map(b => b.textContent?.trim()))
        .toEqual(['Recompute analysis', 'Show progress']);
      expect(h.el().querySelector('[data-action="continue"]')).toBeNull();
      expect(h.el().querySelector('[data-action="rerun"]')).toBeNull();
      expect(h.el().querySelector('.brr-action-error')).toBeNull();
      h.component.close();
    });

    it('enables Recompute on a finished battery run and disables Show progress with its reason', () => {
      h.open();
      expect(h.action('recompute').getAttribute('aria-disabled')).toBeNull();
      expect(h.action('recompute').textContent).toContain('Recompute analysis');
      expect(h.action('progress').getAttribute('aria-disabled')).toBe('true');
      expect(reason('progress')).toBe('The battery run has finished.');

      h.action('progress').click();
      expect(h.monitor.openBatteryDialog).not.toHaveBeenCalled();

      h.action('recompute').click();
      expect(h.service.analyseBatteryRun).toHaveBeenCalledWith(7);
      expect(h.service.resumeBatteryRun).not.toHaveBeenCalled();
    });

    it('offers Show progress while the battery run is live', () => {
      h.service.getBatteryRun.mockReturnValue(of(batteryRun({ status: 'Running', completedAtUtc: null })));
      h.open();
      expect(h.action('progress').getAttribute('aria-disabled')).toBeNull();
      h.component.close();
    });

    it('offers Show progress for a finished battery run the server still works on', () => {
      h.service.getBatteryRun.mockReturnValue(of(batteryRun({ postRunWork: 'Repairing', repairingRunIds: [101] })));
      h.open();
      expect(h.action('progress').getAttribute('aria-disabled')).toBeNull();
      h.component.close();
    });

    it('Show progress closes the report and opens the battery progress dialog', () => {
      const closed = vi.fn().mockName('closed');
      h.component.closed.subscribe(closed);
      h.service.getBatteryRun.mockReturnValue(of(batteryRun({ status: 'Stopped', stopReason: 'MemberFailed', resumable: true, completedAtUtc: null })));
      h.open();

      h.action('progress').click();
      h.fixture.detectChanges();

      expect(h.dialog().open).toBe(false);
      expect(closed).toHaveBeenCalledTimes(1);
      expect(h.monitor.openBatteryDialog).toHaveBeenCalledWith(7);
    });
  });

  describe('copy diagnostics', () => {
    it('describes the definition, status, members by suite and round, exclusions and caveats', () => {
      const superseded = member({ memberId: 9, suiteIndex: 1, runId: 99, runStatus: 'Failed', superseded: true, usable: false, unusableReason: 'run failed' });
      const run = batteryRun({
        status: 'Stopped', stopReason: 'MemberFailed', stopReasonText: 'A member run failed.',
        members: [...batteryRun().members, superseded]
      });
      const text = batteryRunDiagnosticsText(run, analysis({
        excludedMembers: [{ suiteIndex: 1, round: 1, runId: 102, reason: 'index withheld' }]
      }));

      expect(text).toContain('Battery Run #7 diagnostics');
      expect(text).toContain('Battery: Core Battery · revision 2 · Questions and difficulty');
      expect(text).toContain(`Definition SHA-256: abcdef0123456789abcdef0123456789`);
      expect(text).toContain('Status: Stopped');
      expect(text).toContain('Stop reason: A member run failed. (MemberFailed)');
      expect(text).toContain('  1. Gameplay Help');
      expect(text).toContain('    Round 1: run #101 · Completed · index 70.0 · usable');
      expect(text).toContain('  2. Board Reading');
      expect(text).toContain('      superseded: run #99 · Failed');
      expect(text).toContain('  Board Reading, round 1, run #102: index withheld');
      expect(text).toContain('  - Fewer than three complete battery rounds');
    });

    it('says so when there is no analysis', () => {
      const text = batteryRunDiagnosticsText(batteryRun({ slots: [slot(0, 1, null), slot(1, 1, null)], members: [] }), null);
      expect(text).toContain('No analysis has been computed.');
      expect(text).toContain('    Round 1: empty');
    });

    it('copies the text from the header and announces it', async () => {
      h.open();
      const write = vi.spyOn(navigator.clipboard, 'writeText').mockResolvedValue(undefined);
      await h.component.copyDiagnostics();
      h.fixture.detectChanges();

      expect(write).toHaveBeenCalledWith(batteryRunDiagnosticsText(batteryRun(), analysis()));
      expect(h.text('.brr-status[role="status"]')).toBe('Diagnostics copied to the clipboard.');
    });
  });

  describe('tabs', () => {
    function tabs(): HTMLButtonElement[] {
      return Array.from(h.el().querySelectorAll('[role="tablist"] [role="tab"]')) as HTMLButtonElement[];
    }

    it('renders the eleven tabs with the §5 contract and every panel, the unchosen hidden', () => {
      h.open();
      const list = h.el().querySelector('[role="tablist"]') as HTMLElement;
      expect(list.classList).toContain('gh-tabs-secondary');
      expect(list.getAttribute('aria-label')).toBe('Battery run report sections');
      expect(tabs().map(t => t.textContent?.trim())).toEqual([
        'Summary', 'Integrity', 'Suites', 'Robustness', 'Members', 'Dimensions', 'Speed', 'Cost',
        'Configuration', 'Paired Test', 'AI Reports'
      ]);
      for (const tab of BATTERY_RUN_REPORT_TABS) {
        const button = h.el().querySelector(`#brr-tab-${tab.key}`) as HTMLElement;
        const panel = h.el().querySelector(`#brr-panel-${tab.key}`) as HTMLElement;
        expect(button.getAttribute('aria-controls')).toBe(`brr-panel-${tab.key}`);
        expect(panel.getAttribute('role')).toBe('tabpanel');
        expect(panel.getAttribute('aria-labelledby')).toBe(`brr-tab-${tab.key}`);
        expect(panel.getAttribute('tabindex')).toBe('0');
        expect(panel.hidden).toBe(tab.key !== 'summary');
        expect(button.getAttribute('aria-selected')).toBe(String(tab.key === 'summary'));
        expect(button.getAttribute('tabindex')).toBe(tab.key === 'summary' ? '0' : '-1');
        expect(button.querySelector('svg')).toBeNull();
      }
    });

    it('moves with the arrow keys, Home and End, and remembers the choice', () => {
      h.open();
      const summary = h.el().querySelector('#brr-tab-summary') as HTMLButtonElement;
      summary.focus();
      summary.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowRight', bubbles: true }));
      h.fixture.detectChanges();
      expect(h.component.tab).toBe('integrity');
      expect(document.activeElement?.id).toBe('brr-tab-integrity');
      expect(localStorage.getItem(BATTERY_RUN_REPORT_TAB_STORAGE_KEY)).toBe('integrity');

      (document.activeElement as HTMLElement).dispatchEvent(new KeyboardEvent('keydown', { key: 'End', bubbles: true }));
      h.fixture.detectChanges();
      expect(h.component.tab).toBe('reports');
      (document.activeElement as HTMLElement).dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowRight', bubbles: true }));
      h.fixture.detectChanges();
      expect(h.component.tab).toBe('summary');
      (document.activeElement as HTMLElement).dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowLeft', bubbles: true }));
      h.fixture.detectChanges();
      expect(h.component.tab).toBe('reports');
      expect((h.el().querySelector('#brr-panel-reports') as HTMLElement).hidden).toBe(false);
    });

    it('restores the remembered tab on open, and Summary for an unknown one', () => {
      localStorage.setItem(BATTERY_RUN_REPORT_TAB_STORAGE_KEY, 'cost');
      h.open();
      expect(h.component.tab).toBe('cost');
      expect((h.el().querySelector('#brr-panel-cost') as HTMLElement).hidden).toBe(false);
      h.component.close();

      localStorage.setItem(BATTERY_RUN_REPORT_TAB_STORAGE_KEY, 'questions');
      h.open();
      expect(h.component.tab).toBe('summary');
    });

    it('passes the battery run and its analysis to the AI Reports panel, and opens its Download Center for it', () => {
      h.open();
      const reports = h.aiReports();
      expect(reports.batteryRun).toBe(h.component.detail);
      expect(reports.analysis).toBe(h.component.analysis);
      expect(reports.dialogOpen).toBe(true);

      const opener = document.createElement('button');
      reports.downloadsRequested.emit(opener);
      expect(h.downloadCenter().open).toHaveBeenCalledWith(expect.objectContaining({ kind: 'battery', batteryRunId: 7 }));

      reports.reportStatusChange.emit({ status: BenchmarkRunReportDocumentsStatus.Writing, message: null, writerId: 12, writerName: 'Writer' });
      expect(h.component.detail?.reportDocumentsStatus).toBe(BenchmarkRunReportDocumentsStatus.Writing);
      expect(h.component.detail?.reportWriterModelConfigurationId).toBe(12);
    });
  });

  describe('Summary key figures', () => {
    const NOW = new Date(2026, 8, 28, 12, 34, 56);

    beforeEach(() => {
      vi.spyOn(keyFiguresImageIo, 'loadImage').mockImplementation(() => Promise.reject(new Error('404')));
      vi.spyOn(keyFiguresImageIo, 'now').mockReturnValue(NOW);
    });

    it('renders the eight battery cards, each with its card actions', () => {
      h.open();
      const panel = h.el().querySelector('#brr-panel-summary') as HTMLElement;
      expect(panel.querySelector('.rr-figures-head > h4.gh-section-title#brrFiguresTitle')?.textContent?.trim()).toBe('Key figures');
      const cards = Array.from(panel.querySelectorAll('.rr-figures > .score-card')) as HTMLElement[];
      expect(cards.map(c => c.getAttribute('data-figure'))).toEqual([...BATTERY_RUN_KEY_FIGURES]);
      for (const card of cards) {
        expect(card.lastElementChild?.tagName.toLowerCase()).toBe('app-key-figure-card-actions');
      }
      expect(h.text('[data-figure="intelligence"] .score-value')).toBe('72.4 / 100');
      expect(h.text('[data-figure="intelligence"]')).toContain('[67.3, 77.5]');
      expect(h.text('[data-figure="answered"] .score-subvalue')).toBe('18 / 18');
      expect(h.text('[data-figure="speed"] .score-subvalue')).toBe('61.2 / 100');
      expect(h.text('[data-figure="model-cost"] .score-subvalue')).toBe('$1.00');
      expect(h.text('[data-figure="model-cost"] .score-note')).toBe('29 % of the total cost');
      expect(h.text('[data-figure="estimated-cost"] .score-subvalue')).toBe('$3.50');
      expect(h.text('[data-figure="wall-time"] .score-subvalue')).toBe('1 h 00 min');
      expect(h.text('.bb-caveats')).toContain('Fewer than three complete battery rounds');
    });

    it('names the battery run in every card action and keys its tooltips by it', () => {
      h.open();
      const actions = Array.from(h.el().querySelectorAll('#brr-panel-summary app-key-figure-card-actions')) as HTMLElement[];
      expect(actions.length).toBe(BATTERY_RUN_KEY_FIGURES.length);

      const intelligence = h.el().querySelector('[data-figure="intelligence"] app-key-figure-card-actions') as HTMLElement;
      const copy = intelligence.querySelector('button.kfc-copy') as HTMLButtonElement;
      const download = intelligence.querySelector('button.kfc-download') as HTMLButtonElement;
      expect(copy.getAttribute('aria-label')).toBe('Copy Overall Intelligence Index of battery run 7 as an image');
      expect(download.getAttribute('aria-label')).toBe('Download Overall Intelligence Index of battery run 7 as a PNG image');
      expect(copy.getAttribute('interestfor')).toContain('battery7');
      expect(download.getAttribute('interestfor')).toContain('battery7');

      for (const action of actions) {
        for (const button of Array.from(action.querySelectorAll('button'))) {
          expect(button.getAttribute('aria-label')).toMatch(/ of battery run 7 as /);
        }
        for (const tip of Array.from(action.querySelectorAll('.gh-tooltip'))) {
          expect(tip.id).toContain('-battery7-');
          expect(tip.id).not.toContain('run7');
        }
      }
    });

    it('opens the chooser under its own id prefix and battery hints', () => {
      h.open();
      h.click('#brr-figures-choose-btn');

      const chooser = h.el().querySelector('app-key-figures-chooser') as HTMLElement;
      const dialog = chooser.querySelector('dialog') as HTMLDialogElement;
      expect(dialog.open).toBe(true);
      expect(dialog.getAttribute('aria-labelledby')).toBe('brr-kfchTitle');
      const ids = Array.from(chooser.querySelectorAll('[id]')).map(node => node.id);
      expect(ids.length).toBeGreaterThan(0);
      for (const id of ids) {
        expect(id.startsWith('brr-kfch')).toBe(true);
      }
      expect(chooser.querySelector('#brr-kfch-intelligence')).not.toBeNull();
      expect(h.text('app-key-figures-chooser .kfch-hint')).toBe(
        'Shown in the Summary and in the copied or downloaded image. Remembered for every battery run report.');
      expect(h.text('app-key-figures-chooser .kfch-detail-hint')).toBe(
        'The battery run\'s settings above the figures in the copied or downloaded image. The dialog header always lists them all. Remembered for every battery run report.');
    });

    it('reads Incomplete without an Overall Index, and Not analyzed without an analysis', () => {
      const incomplete = completeResult();
      incomplete.complete = false;
      incomplete.completedSuiteCount = 1;
      incomplete.overallIndex = null;
      h.service.getBatteryAnalysis.mockReturnValue(of(analysis({ complete: false, result: incomplete })));
      h.open();
      expect(h.text('[data-figure="intelligence"] .score-value')).toBe('Incomplete');
      expect(h.text('[data-figure="intelligence"] .score-note')).toBe('1 of 2 suites have a usable result');
      h.component.close();

      h.service.getBatteryAnalysis.mockReturnValue(of(null));
      h.open();
      expect(h.text('[data-figure="intelligence"] .score-value')).toBe('Not analyzed');
    });

    it('downloads the strip under a battery-run file stem', async () => {
      h.open();
      const save = vi.spyOn(keyFiguresImageIo, 'save').mockReturnValue(undefined);
      await h.component.downloadKeyFigures();
      h.fixture.detectChanges();

      expect(save).toHaveBeenCalledTimes(1);
      const [blob, fileName] = vi.mocked(save).mock.lastCall!;
      expect(blob.type).toBe('image/png');
      expect(fileName).toBe('gnollbench_battery-run-7_core-battery_model-x_key-figures_20260928_123456.png');
      expect(h.text('.brr-status[role="status"]')).toBe('Image downloaded.');
      expect(h.component.keyFiguresContext(h.component.detail!).fileStem).toBe('battery-run-7');
    });

    it('copies the strip from the header Copy', async () => {
      h.open();
      const copy = vi.spyOn(keyFiguresImageIo, 'copy').mockResolvedValue('copied');
      const handler = vi.spyOn(h.component, 'copyKeyFigures');
      (h.el().querySelector('#brr-figures-copy-btn') as HTMLButtonElement).click();
      await handler.mock.results.at(-1)!.value;
      h.fixture.detectChanges();

      expect(copy).toHaveBeenCalledTimes(1);
      expect(h.text('.brr-status[role="status"]')).toBe('Key figures copied as an image.');
    });

    it('downloads in the format the run report stored, labels the downloads for it, and still copies a PNG', async () => {
      // Stored as the run report's chooser stores it: one key for both reports.
      writeStoredKeyFiguresExportSettings({ ...defaultKeyFiguresExportSettings(), format: 'webp' });
      h.open();

      expect(h.el().querySelector('#brr-figures-download-btn')?.getAttribute('aria-label'))
        .toBe('Download key figures of battery run 7 as a WebP image');
      expect(h.text('#brr-figures-download-tip')).toBe('Download key figures as WebP');
      expect(h.el().querySelector('[data-figure="intelligence"] button.kfc-download')?.getAttribute('aria-label'))
        .toBe('Download Overall Intelligence Index of battery run 7 as a WebP image');

      const save = vi.spyOn(keyFiguresImageIo, 'save').mockReturnValue(undefined);
      await h.component.downloadKeyFigures();
      h.fixture.detectChanges();
      const [blob, fileName] = vi.mocked(save).mock.lastCall!;
      expect(blob.type).toBe('image/webp');
      expect(fileName).toBe('gnollbench_battery-run-7_core-battery_model-x_key-figures_20260928_123456.webp');

      const copy = vi.spyOn(keyFiguresImageIo, 'copy').mockResolvedValue('copied');
      await h.component.copyKeyFigures();
      expect(vi.mocked(copy).mock.lastCall![0].type).toBe('image/png');
    });

    it('stores a format chosen in its chooser for the run report to read', () => {
      h.open();
      h.click('#brr-figures-choose-btn');
      const chooser = h.el().querySelector('app-key-figures-chooser') as HTMLElement;
      expect(chooser.querySelector('#brr-kfch-fmt-section')).not.toBeNull();
      expect(chooser.querySelector('#brr-kfch-size-section')).not.toBeNull();

      (chooser.querySelector('#brr-kfch-fmt-format-webp') as HTMLInputElement).click();
      h.fixture.detectChanges();
      expect(readStoredKeyFiguresExportSettings().format).toBe('webp');
      expect(h.el().querySelector('#brr-figures-download-btn')?.getAttribute('aria-label'))
        .toBe('Download key figures of battery run 7 as a WebP image');
    });

    it('rounds the battery duration as the battery report\'s wall clock does', () => {
      expect(formatBatteryWallClock(3_599_900)).toBe('59 min 59 s');
      expect(formatBatteryWallClock(3_600_000)).toBe('1 h 00 min');
      expect(formatBatteryWallClock(7_259_999)).toBe('2 h 00 min');
      expect(formatBatteryWallClock(27 * 3_600_000 + 4 * 60_000)).toBe('1 d 3 h 04 min');
      expect(formatBatteryWallClock(5_400)).toBe('0 min 05 s');
      expect(formatBatteryWallClock(null)).toBe('—');

      h.service.getBatteryRun.mockReturnValue(of(batteryRun({ startedAtUtc: '2026-10-01T10:00:00Z', completedAtUtc: '2026-10-01T10:59:59.700Z' })));
      h.open();
      expect(h.text('[data-figure="wall-time"] .score-subvalue')).toBe('59 min 59 s');
    });

    it('hides unselected cards, remembers the selection under the battery key and labels the chooser', () => {
      h.open();
      h.component.onKeyFigureSelectionChange(['speed', 'mean-time']);
      h.fixture.detectChanges();

      expect((h.el().querySelector('[data-figure="speed"]') as HTMLElement).hidden).toBe(true);
      expect((h.el().querySelector('[data-figure="intelligence"]') as HTMLElement).hidden).toBe(false);
      expect(JSON.parse(localStorage.getItem(BATTERY_RUN_REPORT_KEY_FIGURES_STORAGE_KEY)!)).toEqual({ version: 1, excluded: ['speed', 'mean-time'] });
      expect(h.text('#brr-figures-choose-btn')).toBe('Choose figures (6 of 8)');

      h.component.onKeyFigureSelectionChange([...BATTERY_RUN_KEY_FIGURES]);
      h.fixture.detectChanges();
      expect((h.el().querySelector('.rr-figures') as HTMLElement).hidden).toBe(true);
      expect(h.text('.rr-figures-empty')).toContain('No key figures are selected');
    });
  });

  describe('integrity, suites and members', () => {
    it('flags the Integrity tab through one notice while members are excluded', () => {
      h.service.getBatteryAnalysis.mockReturnValue(of(analysis({
        excludedMembers: [{ suiteIndex: 1, round: 1, runId: 102, reason: 'index withheld' }]
      })));
      h.open();

      expect(h.component.hasIntegrityNotice).toBe(true);
      expect(h.text('#brr-tab-integrity .rr-tab-flag')).toBe('Notice');
      expect(h.text('#brr-panel-integrity .brr-integrity-notice')).toContain('1 member run is left out of the analysis.');
      expect(h.text('#brr-panel-integrity .bb-excluded')).toContain('Board Reading, round 1, run #102');
    });

    it('carries no notice on a clean, complete analysis', () => {
      h.open();
      expect(h.component.hasIntegrityNotice).toBe(false);
      expect(h.el().querySelector('#brr-tab-integrity .rr-tab-flag')).toBeNull();
      expect(h.el().querySelector('.brr-integrity-notice')).toBeNull();
      expect(h.text('#brr-panel-integrity')).toContain('Holds');
    });

    it('flags a stale analysis and a degraded speed comparison', () => {
      const result = completeResult();
      result.speed = { ...result.speed!, degraded: true, degradedReason: 'Concurrency differs between suites.' };
      h.service.getBatteryRun.mockReturnValue(of(batteryRun({ analysisStale: true })));
      h.service.getBatteryAnalysis.mockReturnValue(of(analysis({ result })));
      h.open();

      const notice = h.text('.brr-integrity-notice');
      expect(notice).toContain('changed after this analysis was computed');
      expect(notice).toContain('Speed is degraded: Concurrency differs between suites.');
      expect(h.text('.dialog-title-badges')).toContain('Analysis stale');
    });

    it('opens a member run report from the Suites tab', () => {
      const opened = vi.fn().mockName('openRunReport');
      h.component.openRunReport.subscribe(opened);
      h.open();

      const button = h.el().querySelector('#brr-panel-suites .brr-suite-card button.btn-gh[data-run-id="102"]') as HTMLButtonElement;
      expect(button).not.toBeNull();
      expect(button.getAttribute('type')).toBe('button');
      const name = accessibleText(button);
      expect(name).toContain('Run #102');
      expect(name).toContain('Round 1');
      expect(name).toContain('Board Reading');
      expect(button.closest('.brr-suite-card')?.getAttribute('data-suite-index')).toBe('1');
      button.click();
      expect(opened).toHaveBeenCalledWith(102);
      expect(h.dialog().open).toBe(true);
    });

    it('lists the suites as cards with their member runs before an analysis exists', () => {
      h.service.getBatteryAnalysis.mockReturnValue(of(null));
      h.open();

      const cards = h.el().querySelectorAll('#brr-panel-suites ul.brr-suite-cards[role="list"] > li > article.brr-suite-card');
      expect(cards.length).toBe(2);
      expect(cards[0].getAttribute('aria-labelledby')).toBe('brr-suite-title-0');
      expect(cards[0].querySelector('h5#brr-suite-title-0')?.textContent?.trim()).toBe('Gameplay Help');
      expect(accessibleText(cards[0].querySelector('.brr-suite-kicker') as HTMLElement)).toBe('Suite 1 of 2');
      expect(cards[0].querySelector('[data-metric="index"] .brr-suite-metric-note')?.textContent?.trim()).toBe('Not analyzed');
      expect(cards[0].querySelector('button.btn-gh[data-run-id="101"]')).not.toBeNull();
      expect(h.text('#brr-panel-suites .brr-suite-strip')).toBe('The suite profile needs an analysis.');
    });

    it('prints the profile unevenness to two decimals, with the interval method behind an info button', () => {
      h.open();

      const unevenness = accessibleText(h.el().querySelector('#brr-panel-suites .brr-suite-unevenness') as HTMLElement);
      expect(unevenness).toContain('between-suite SD 2.80');
      expect(unevenness).toContain('range 4.00 points');
      const tip = h.el().querySelector('#brr-panel-suites .brr-suite-strip app-info-tip') as HTMLElement;
      expect(tip.querySelector('button.gh-info-btn--click')?.getAttribute('aria-label')).toBe('About Suite intervals');
      expect(tip.querySelector('#brr-suite-interval-tip')?.textContent).toContain('Student\'s t on ν');
    });

    it('shows each suite\'s weight, index, interval and figures on its card', () => {
      h.open();

      const card = h.el().querySelector('#brr-panel-suites .brr-suite-card[data-suite-index="1"]') as HTMLElement;
      expect(accessibleText(card.querySelector('.brr-suite-kicker') as HTMLElement)).toBe('Suite 2 of 2, 60.0 % weight');
      expect(accessibleText(card.querySelector('.brr-suite-meta') as HTMLElement)).toBe('8 of 8 scored items, 1 usable run');
      const index = card.querySelector('[data-metric="index"] .score-badge') as HTMLElement;
      expect(index.textContent?.trim()).toBe('74.0');
      expect(index.classList).toContain('badge-score-mid');
      expect(card.querySelector('[data-metric="index"] .brr-suite-metric-note')?.textContent?.trim()).toBe('95 % [64.1, 75.9]');
      expect(card.querySelector('[data-metric="contribution"] dd')?.textContent?.trim()).toBe('44.4');
      expect(card.querySelector('[data-metric="critical-errors"] dd')?.textContent?.trim()).toBe('10.0 %');
      expect(card.querySelector('.brr-suite-members')?.getAttribute('aria-labelledby')).toBe('brr-suite-members-1');
      expect(h.text('#brr-suite-members-1')).toBe('Member runs');
    });

    it('lays out the Members grid by suite and round and opens a member run report', () => {
      const opened = vi.fn().mockName('openRunReport');
      h.component.openRunReport.subscribe(opened);
      const withheld = member({ memberId: 2, suiteIndex: 1, runId: 102, runStatus: 'CompletedWithErrors', qualityIndex: null, usable: false, unusableReason: 'index withheld' });
      h.service.getBatteryRun.mockReturnValue(of(batteryRun({
        runsPerSuite: 2,
        slots: [slot(0, 1, member()), slot(1, 1, withheld), slot(0, 2, null), slot(1, 2, null)],
        members: [member(), withheld]
      })));
      h.open();

      const rows = h.el().querySelectorAll('.brr-members-table tbody tr');
      expect(rows.length).toBe(2);
      expect(Array.from(h.el().querySelectorAll('.brr-members-table thead th')).map(th => th.textContent?.trim()))
        .toEqual(['Suite', 'Round 1', 'Round 2']);
      const cells = rows[1].querySelectorAll('td.brr-member-cell');
      expect(cells[0].getAttribute('data-state')).toBe('indexWithheld');
      expect(cells[0].textContent).toContain('Index withheld');
      expect(cells[1].getAttribute('data-state')).toBe('pending');
      expect(cells[1].querySelector('.brr-open-run')).toBeNull();

      const open = rows[0].querySelector('.brr-open-run') as HTMLButtonElement;
      expect(open.getAttribute('aria-label')).toBe('Open run report of run 101, Gameplay Help, round 1');
      open.click();
      expect(opened).toHaveBeenCalledWith(101);
    });
  });
});
