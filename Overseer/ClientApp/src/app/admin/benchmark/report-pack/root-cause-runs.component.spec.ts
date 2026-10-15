import type { Mock } from 'vitest';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';

import { AdminBenchmarkService } from '../../../services/admin-benchmark.service';
import { BenchmarkShellBridge, LauncherSetupPrefill } from '../state/benchmark-shell-bridge.service';
import { recommendedRun } from './root-cause.testing';
import {
  RootCauseRunsComponent,
  recommendedRunEstimate,
  recommendedRunKindLabel,
  rootCauseRunSourceLabel
} from './root-cause-runs.component';

describe('RootCauseRunsComponent', () => {
  let fixture: ComponentFixture<RootCauseRunsComponent>;
  let el: HTMLElement;
  let getReportDocument: Mock;
  let bridge: BenchmarkShellBridge;
  let prefills: LauncherSetupPrefill[];

  beforeEach(async () => {
    getReportDocument = vi.fn();
    await TestBed.configureTestingModule({
      imports: [RootCauseRunsComponent],
      providers: [BenchmarkShellBridge, { provide: AdminBenchmarkService, useValue: { getReportDocument } }]
    }).compileComponents();
    bridge = TestBed.inject(BenchmarkShellBridge);
    prefills = [];
    bridge.prefillSetup$.subscribe(prefill => prefills.push(prefill));
    fixture = TestBed.createComponent(RootCauseRunsComponent);
    el = fixture.nativeElement as HTMLElement;
    fixture.componentRef.setInput('documentId', 12);
  });

  const text = (root: Element | null): string => (root?.textContent ?? '').replace(/\s+/g, ' ').trim();
  const cards = (): HTMLElement[] => Array.from(el.querySelectorAll<HTMLElement>('.rcr-card'));

  function openDisclosure(): void {
    const details = el.querySelector<HTMLDetailsElement>('details.rcr')!;
    details.open = true;
    details.dispatchEvent(new Event('toggle'));
    fixture.detectChanges();
  }

  it('words the kinds, the estimate and the note heading', () => {
    expect(recommendedRunKindLabel('thinkingPair')).toBe('Thinking-level pair');
    expect(recommendedRunKindLabel('control')).toBe('Control run');
    expect(recommendedRunEstimate({ estimatedCostUsd: 1.2, estimatedMinutes: 34.6 })).toBe('about $1.20 · about 35 min');
    expect(recommendedRunEstimate({ estimatedCostUsd: null, estimatedMinutes: null })).toBeNull();
    expect(rootCauseRunSourceLabel(12, 2)).toBe('Set up from Root Cause Analysis #12, run 2');
  });

  it('shows one card per given run, with its purpose, mark, decision rule, tier, estimate and settings, then the checks', () => {
    fixture.componentRef.setInput('recommendedRuns', [
      recommendedRun(),
      recommendedRun({
        number: 2, kind: 'replicate', purpose: 'M', mark: 'Required', hypothesisIds: ['WS1', 'WS2'], targetKind: 'Battery',
        batteryId: 5, batteryName: 'Core Battery', suiteId: null, suiteName: null, runCount: 1, runsPerModel: 3,
        estimatedCostUsd: null, estimatedMinutes: null, predictedTier: '', afterSystemHypotheses: true,
        notes: ['Make this run after the system hypotheses are settled.', 'Runs per Suite 3.']
      })
    ]);
    fixture.componentRef.setInput('checks', [
      { number: 1, kind: 'rubric', text: 'Check the rubric of S1-Q12 against the source.', hypothesisIds: ['HQ3'] }
    ]);
    fixture.componentRef.setInput('open', true);
    fixture.detectChanges();

    expect(getReportDocument).not.toHaveBeenCalled();
    expect(text(el.querySelector('summary'))).toContain('Next runs and checks (2 runs, 1 check)');
    const [first, second] = cards();
    expect(text(first.querySelector('.rcr-title'))).toBe('Run 1: Control run');
    expect(text(first.querySelector('.rcr-purpose'))).toBe('For the benchmark');
    expect(text(first.querySelector('.rcr-mark'))).toBe('Recommended');
    expect(text(first.querySelector('.rcr-decides'))).toContain('Confirms HQ1');
    expect(text(first.querySelector('[data-fact="tier"] dd'))).toBe('Comparable');
    expect(text(first.querySelector('[data-fact="estimate"] dd'))).toBe('about $1.20 · about 35 min');
    expect(text(first.querySelector('[data-fact="target"] dt'))).toBe('Suite');
    expect(text(first.querySelector('[data-fact="target"] dd'))).toBe('Second Suite');
    expect(text(first.querySelector('[data-fact="models"] dt'))).toBe('Model batch');
    expect(text(first.querySelector('[data-fact="models"] dd'))).toBe('Model Two, Model Three');
    expect(text(first.querySelector('[data-fact="style"] dd'))).toBe('Concise');
    expect(first.querySelector('.rcr-after-system')).toBeNull();

    expect(text(second.querySelector('.rcr-title'))).toBe('Run 2: Replicate');
    expect(text(second.querySelector('.rcr-mark'))).toBe('Required');
    expect(Array.from(second.querySelectorAll('.rcr-hypothesis')).map(tag => text(tag))).toEqual(['WS1', 'WS2']);
    expect(text(second.querySelector('[data-fact="target"] dt'))).toBe('Battery');
    expect(text(second.querySelector('[data-fact="target"] dd'))).toBe('Core Battery');
    expect(text(second.querySelector('[data-fact="runs"] dt'))).toBe('Runs per Suite');
    expect(text(second.querySelector('[data-fact="runs"] dd'))).toBe('3');
    expect(text(second.querySelector('[data-fact="tier"] dd'))).toBe('Not predicted');
    expect(text(second.querySelector('[data-fact="estimate"] dd'))).toBe('Not estimated');
    expect(text(second.querySelector('.rcr-after-system'))).toBe('Make this run after the system hypotheses are settled.');
    // The server's note saying the same is not repeated; its other notes are shown.
    expect(Array.from(second.querySelectorAll('.rcr-run-note')).map(note => text(note))).toEqual(['Runs per Suite 3.']);

    expect(text(el.querySelector('.rcr-check'))).toContain('Check the rubric of S1-Q12 against the source.');
  });

  it('sets up each run through the bridge with its own note heading, and starts nothing', () => {
    const second = recommendedRun({
      number: 2, kind: 'stylePair', purpose: 'C', runMode: 'Single', testedModelConfigurationIds: [2], modelNames: ['Model Two']
    });
    fixture.componentRef.setInput('recommendedRuns', [recommendedRun(), second]);
    fixture.componentRef.setInput('checks', []);
    fixture.componentRef.setInput('open', true);
    fixture.detectChanges();

    const button = cards()[1].querySelector<HTMLButtonElement>('.rcr-set-up')!;
    expect(button.classList).toContain('btn-ghost');
    expect(text(button)).toContain('Set up this run');
    button.click();

    expect(prefills.length).toBe(1);
    expect(prefills[0].sourceLabel).toBe('Set up from Root Cause Analysis #12, run 2');
    expect(prefills[0].kind).toBe('stylePair');
    expect(prefills[0].testedModelConfigurationIds).toEqual([2]);
    expect(prefills[0].runMode).toBe('Single');
  });

  it('refuses while the active sub-tab cannot be left, and says why on the card', () => {
    fixture.componentRef.setInput('recommendedRuns', [recommendedRun()]);
    fixture.componentRef.setInput('checks', []);
    fixture.componentRef.setInput('open', true);
    fixture.detectChanges();
    const release = bridge.setLeaveGuard(() => 'The comparison wizard is exporting charts.');

    cards()[0].querySelector<HTMLButtonElement>('.rcr-set-up')!.click();
    fixture.detectChanges();

    expect(prefills).toEqual([]);
    expect(text(cards()[0].querySelector('.rcr-refusal'))).toBe('The comparison wizard is exporting charts.');
    release();
  });

  it('reads the runs from the document detail the first time it is opened', () => {
    getReportDocument.mockReturnValue(of({ recommendedRuns: [recommendedRun()], checks: [] }));
    fixture.detectChanges();
    expect(getReportDocument).not.toHaveBeenCalled();
    expect(cards()).toEqual([]);

    openDisclosure();

    expect(getReportDocument).toHaveBeenCalledWith(12);
    expect(cards().length).toBe(1);
    expect(text(el.querySelector('.rcr-checks'))).toContain('No check by hand is needed.');

    openDisclosure();
    expect(getReportDocument).toHaveBeenCalledTimes(1);
  });

  it('says so when the detail cannot be read, and tries again on request', () => {
    getReportDocument.mockReturnValueOnce(throwError(() => ({ status: 500 })));
    fixture.detectChanges();
    openDisclosure();

    expect(text(el.querySelector('.rcr-error'))).toContain('The recommended runs could not be read.');

    getReportDocument.mockReturnValue(of({ recommendedRuns: [], checks: [] }));
    el.querySelector<HTMLButtonElement>('.rcr-retry')!.click();
    fixture.detectChanges();

    expect(text(el.querySelector('.rcr-body'))).toContain('No run is recommended');
  });
});
