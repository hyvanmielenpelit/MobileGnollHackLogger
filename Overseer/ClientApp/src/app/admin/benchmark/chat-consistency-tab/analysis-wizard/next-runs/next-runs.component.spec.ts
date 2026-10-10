import { ComponentFixture, TestBed } from '@angular/core/testing';

import { CcAnalysisResult, CcNextRun, CcRunRow } from '../../chat-consistency.models';
import { ccAnalysisResult, ccRunRow, ccSubjectWithLevel, textOf } from '../../chat-consistency-tab.testing';
import { CcNextRunsComponent, ccNextRunSections, ccNextRunsLeadText } from './next-runs.component';

/** Runs 205 (Board Suite) and 206 (Wiki Suite), the targets of the control fixtures. */
function targetRows(): CcRunRow[] {
  return [
    ccRunRow(206, '2026-10-01T08:00:00Z', { suiteName: 'Wiki Suite', suiteId: 6, suiteKey: 'id:6' }),
    ccRunRow(205, '2026-09-26T08:00:00Z')
  ];
}

function nextRun(overrides: Partial<CcNextRun>): CcNextRun {
  return { kind: 'checkpoint', period: 'comparison', endpointId: 'P1', reason: '', suggestion: '', repeatRunId: null, ...overrides };
}

/** A battery analysis's advice: one control per member suite in the comparison, a checkpoint, a stratum and a re-grade. */
function batteryNextRuns(): CcNextRun[] {
  return [
    nextRun({
      kind: 'control', period: 'comparison', endpointId: null,
      reason: 'No control run of another provider under the same Overseer build in the comparison period.',
      suggestion: 'No control run for period comparison: make a run of a model from a provider other than OpenAI on suite Board Suite '
        + 'under the same Overseer build as run #205 (instrument 0123456789ab).',
      repeatRunId: 205
    }),
    nextRun({ kind: 'regrade', period: 'both', endpointId: 'P1', reason: 'Quality rests on native grades with no measured grader stability.',
      suggestion: 'Re-grade every compared run with one assessor (a common grader).' }),
    nextRun({
      kind: 'control', period: 'comparison', endpointId: null,
      reason: 'No control run of another provider under the same Overseer build in the comparison period.',
      suggestion: 'No control run for period comparison: make a run of a model from a provider other than OpenAI on suite Wiki Suite '
        + 'under the same Overseer build as run #206 (instrument ba9876543210).',
      repeatRunId: 206
    }),
    nextRun({ kind: 'stratum', period: 'comparison', endpointId: 'P2', reason: 'P2 is below the minimum speed sample.',
      suggestion: '1 more run of GPT-5 starting in weekday 14–18 UTC in the comparison period.', repeatRunId: 206 }),
    nextRun({ kind: 'checkpoint', period: 'baseline', endpointId: 'P4', reason: 'The baseline has 1 run(s) on 1 day(s).',
      suggestion: 'Widen the baseline period to include runs of GPT-5 on another day.', repeatRunId: 205 })
  ];
}

describe('CcNextRunsComponent', () => {
  let fixture: ComponentFixture<CcNextRunsComponent>;
  let component: CcNextRunsComponent;
  let el: HTMLElement;

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [CcNextRunsComponent] }).compileComponents();
    fixture = TestBed.createComponent(CcNextRunsComponent);
    component = fixture.componentInstance;
    el = fixture.nativeElement as HTMLElement;
  });

  function render(result: CcAnalysisResult, rows: readonly CcRunRow[] = targetRows()): void {
    fixture.componentRef.setInput('result', result);
    fixture.componentRef.setInput('rows', rows);
    fixture.detectChanges();
  }

  const renderBattery = () => render(ccAnalysisResult({ subject: ccSubjectWithLevel(), nextRuns: batteryNextRuns() }));
  const section = (kind: string): HTMLElement => el.querySelector<HTMLElement>(`.cc-nr-group[data-kind="${kind}"]`)!;
  const cards = (kind: string): HTMLElement[] => Array.from(section(kind).querySelectorAll<HTMLElement>('.cc-nr-card'));
  const buttons = (card: HTMLElement): HTMLButtonElement[] => Array.from(card.querySelectorAll<HTMLButtonElement>('.cc-nr-repeat'));
  const fact = (card: HTMLElement, key: string): string => textOf(card.querySelector(`.cc-nr-facts [data-fact="${key}"] dd`));

  describe('head', () => {
    it('says how many runs would resolve the open questions: the repeat targets plus the re-grades', () => {
      renderBattery();
      const title = el.querySelector('#cc-nr-title')!;
      expect(title.tagName).toBe('H5');
      expect(textOf(title)).toBe('Next runs');
      expect(title.classList.contains('visually-hidden')).toBe(true);
      expect(title.classList.contains('gh-section-title')).toBe(false);
      // Two control targets, one stratum, one checkpoint, one re-grade.
      expect(textOf(el.querySelector('.cc-nr-lead'))).toBe('5 runs would resolve the open questions.');
      expect(el.querySelector('.cc-nr-empty')).toBeNull();
    });

    it('says 1 run in the singular', () => {
      render(ccAnalysisResult({ nextRuns: [nextRun({ kind: 'regrade', period: 'both', suggestion: 'Re-grade.' })] }));
      expect(textOf(el.querySelector('.cc-nr-lead'))).toBe('1 run would resolve the open questions.');
    });

    it('shows the empty state and no section when no run is needed', () => {
      render(ccAnalysisResult({ nextRuns: [] }));
      expect(textOf(el.querySelector('.cc-nr-empty'))).toBe('No run is needed: no verdict is waiting on more data.');
      expect(el.querySelector('.cc-nr-empty svg')!.getAttribute('aria-hidden')).toBe('true');
      expect(el.querySelector('.cc-nr-lead')).toBeNull();
      expect(el.querySelectorAll('.cc-nr-group').length).toBe(0);
    });
  });

  describe('sections', () => {
    it('are the pure sections the Results image reads too', () => {
      renderBattery();
      const result = ccAnalysisResult({ subject: ccSubjectWithLevel(), nextRuns: batteryNextRuns() });
      expect(ccNextRunSections(result, targetRows())).toEqual(component.sections);
      expect(ccNextRunsLeadText(component.actionCount)).toBe(textOf(el.querySelector('.cc-nr-lead')));
    });

    it('heads one section per kind present, in card order, the model by its base name', () => {
      renderBattery();
      const groups = Array.from(el.querySelectorAll<HTMLElement>('.cc-nr-group'));
      expect(groups.map(group => group.getAttribute('data-kind'))).toEqual(['checkpoint', 'stratum', 'control', 'regrade']);
      expect(groups.map(group => textOf(group.querySelector('.cc-nr-group-title'))))
        .toEqual(['More runs of GPT-5', 'Runs at another time of day', 'Control runs', 'Re-grades']);
      for (const group of groups) {
        const title = group.querySelector('h6.cc-nr-group-title')!;
        expect(group.getAttribute('aria-labelledby')).toBe(title.id);
        const list = group.querySelector('ul.cc-nr-grid')!;
        expect(list.getAttribute('role')).toBe('list');
        expect(list.getAttribute('aria-labelledby')).toBe(title.id);
      }
    });

    it('leads the control section with what a control run tells apart', () => {
      renderBattery();
      expect(textOf(section('control').querySelector('.cc-nr-group-lead')))
        .toBe('Another provider\'s model under the same Overseer build tells our changes from the provider\'s.');
      expect(section('checkpoint').querySelector('.cc-nr-group-lead')).toBeNull();
    });

    it('titles each card by its action phrase and tags its period and endpoints', () => {
      renderBattery();
      const checkpoint = cards('checkpoint')[0];
      expect(textOf(checkpoint.querySelector('h6.cc-nr-title'))).toBe('Run the model again on another day');
      expect(checkpoint.getAttribute('aria-labelledby')).toBe(checkpoint.querySelector('.cc-nr-title')!.id);
      expect(checkpoint.getAttribute('data-period')).toBe('baseline');
      expect(textOf(checkpoint.querySelector('.cc-nr-period'))).toBe('Baseline');
      expect(Array.from(checkpoint.querySelectorAll('.cc-nr-endpoint')).map(tag => textOf(tag))).toEqual(['P4']);
      expect(textOf(checkpoint.querySelector('.cc-nr-suggestion'))).toBe('Widen the baseline period to include runs of GPT-5 on another day.');
      expect(textOf(checkpoint.querySelector('.cc-nr-reason'))).toBe('The baseline has 1 run(s) on 1 day(s).');

      expect(textOf(cards('stratum')[0].querySelector('.cc-nr-title'))).toBe('Run at another time of day');
      expect(textOf(cards('control')[0].querySelector('.cc-nr-title'))).toBe('Run another provider\'s model');
      expect(textOf(cards('regrade')[0].querySelector('.cc-nr-title'))).toBe('Re-grade with one common grader');
    });
  });

  describe('control card', () => {
    it('groups a battery\'s per-suite controls into one card with a Set up button per run', () => {
      renderBattery();
      expect(cards('control').length).toBe(1);
      const card = cards('control')[0];
      expect(card.getAttribute('data-period')).toBe('comparison');
      expect(textOf(card.querySelector('.cc-nr-period'))).toBe('Comparison');
      const repeat = buttons(card);
      expect(repeat.map(button => textOf(button))).toEqual(['Set up from run #205', 'Set up from run #206']);
      expect(repeat.map(button => button.getAttribute('aria-label'))).toEqual([
        'Set up from run #205\'s setup (Board Suite) — fills Run Benchmark, starts nothing',
        'Set up from run #206\'s setup (Wiki Suite) — fills Run Benchmark, starts nothing'
      ]);
      for (const button of repeat) {
        expect(button.type).toBe('button');
        expect(button.classList.contains('btn-ghost')).toBe(true);
        // The accessible name holds the visible text.
        expect(button.getAttribute('aria-label')!.startsWith(textOf(button))).toBe(true);
      }
      expect(card.querySelector('.cc-nr-buttons')!.getAttribute('role')).toBe('group');
    });

    it('emits repeatSetup with the run id of the button pressed', () => {
      renderBattery();
      const emitted: number[] = [];
      component.repeatSetup.subscribe(runId => emitted.push(runId));
      buttons(cards('control')[0])[1].click();
      buttons(cards('checkpoint')[0])[0].click();
      expect(emitted).toEqual([206, 205]);
    });

    it('states the suites, the build and the provider in a dl, with the suggestion behind a closed Details', () => {
      renderBattery();
      const card = cards('control')[0];
      expect(card.querySelector('dl.cc-nr-facts')).not.toBeNull();
      expect(Array.from(card.querySelectorAll('.cc-nr-facts dt')).map(term => textOf(term))).toEqual(['Suite', 'Same build as', 'Provider']);
      expect(fact(card, 'suite')).toBe('Board Suite, Wiki Suite');
      expect(fact(card, 'build')).toBe('run #205, run #206');
      expect(fact(card, 'provider')).toBe('not OpenAI');

      const details = card.querySelector<HTMLDetailsElement>('details.gh-disclosure.cc-nr-details')!;
      expect(details.open).toBe(false);
      expect(textOf(details.querySelector('summary')).startsWith('Details')).toBe(true);
      const suggestions = Array.from(details.querySelectorAll('.cc-nr-suggestion')).map(p => textOf(p));
      expect(suggestions.length).toBe(2);
      expect(suggestions[0]).toContain('instrument 0123456789ab');
      // The suggestion is only inside Details.
      expect(card.querySelectorAll('.cc-nr-suggestion').length).toBe(2);

      expect(textOf(card.querySelector('.cc-nr-reason')))
        .toBe('No control run of another provider under the same Overseer build in the comparison period.');
      expect(textOf(card.querySelector('.cc-nr-actions .cc-nr-hint'))).toBe('Then choose a model from a provider other than OpenAI.');
    });

    it('leaves the suite out of the name of a run not among the rows', () => {
      render(ccAnalysisResult(), []);
      const card = cards('control')[0];
      expect(buttons(card)[0].getAttribute('aria-label')).toBe('Set up from run #205\'s setup — fills Run Benchmark, starts nothing');
      expect(fact(card, 'suite')).toBe('—');
    });
  });

  describe('re-grade card', () => {
    it('offers no button and says where a re-grade is started', () => {
      renderBattery();
      const card = cards('regrade')[0];
      expect(buttons(card).length).toBe(0);
      expect(card.querySelector('button')).toBeNull();
      expect(textOf(card.querySelector('.cc-nr-hint'))).toBe('Re-grade from step 3: Controls → Re-grade.');
      expect(card.getAttribute('data-period')).toBe('both');
      expect(textOf(card.querySelector('.cc-nr-period'))).toBe('Both periods');
      expect(textOf(card.querySelector('.cc-nr-suggestion'))).toBe('Re-grade every compared run with one assessor (a common grader).');
    });
  });
});
