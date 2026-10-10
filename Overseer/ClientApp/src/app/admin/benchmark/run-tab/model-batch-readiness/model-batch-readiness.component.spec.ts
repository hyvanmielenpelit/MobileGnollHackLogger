import { ComponentFixture, TestBed } from '@angular/core/testing';

import { BenchmarkModelBatchFindingDto } from '../../../../services/admin-benchmark.service';
import {
  MODEL_BATCH_FINDING_RATIONALE,
  ModelBatchAcknowledgment,
  ModelBatchReadinessComponent,
  modelBatchFindingCode
} from './model-batch-readiness.component';

function finding(overrides: Partial<BenchmarkModelBatchFindingDto>): BenchmarkModelBatchFindingDto {
  return {
    code: 'MB-A01', name: 'SingleRunPerModel', severity: 'Advice', field: null, title: 'One run per model',
    detail: 'Differences under about 2 index points are noise; use 2–3 runs to rank.',
    modelConfigurationIds: [], acknowledgmentKey: null,
    ...overrides
  };
}

const BLOCKER = finding({
  code: 'MB-B03', name: 'GraderIsCandidate', severity: 'Blocker', field: 'assessor', title: 'Model One cannot grade itself',
  detail: 'Choose another assessor: a scoring grader must not be a model under test.', modelConfigurationIds: [1]
});

const WARNING = finding({
  code: 'MB-W01', name: 'MixedFamiliesSingleAssessor', severity: 'Warning', field: 'coAssessor', title: 'Use a two-family panel for this batch',
  detail: 'A single assessor favors its own provider\'s models over the others.',
  modelConfigurationIds: [2, 3], acknowledgmentKey: 'MB-W01:2,3'
});

const ADVICE_A = finding({ code: 'MB-A01', field: 'runsPerModel' });
const ADVICE_B = finding({ code: 'MB-T01', name: 'OrderAsListed', title: 'Models run in a fixed order', detail: 'Randomize so no model always gets the same hours.' });

describe('ModelBatchReadinessComponent', () => {
  let fixture: ComponentFixture<ModelBatchReadinessComponent>;
  let component: ModelBatchReadinessComponent;

  const el = (): HTMLElement => fixture.nativeElement as HTMLElement;
  const query = <T extends Element = HTMLElement>(selector: string): T | null => el().querySelector(selector) as T | null;
  const text = (node: Element | null): string => (node?.textContent ?? '').replace(/\s+/g, ' ').trim();

  function render(findings: BenchmarkModelBatchFindingDto[], acknowledged: string[] = [], busy = false): void {
    fixture.componentRef.setInput('findings', findings);
    fixture.componentRef.setInput('acknowledgedKeys', new Set(acknowledged));
    fixture.componentRef.setInput('busy', busy);
    fixture.detectChanges();
  }

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [ModelBatchReadinessComponent] }).compileComponents();
    fixture = TestBed.createComponent(ModelBatchReadinessComponent);
    component = fixture.componentInstance;
  });

  afterEach(() => fixture.destroy());

  it('reads Ready with no findings, and shows no chips and no disclosure', () => {
    render([]);

    expect(query('.gh-readiness')!.getAttribute('data-state')).toBe('ready');
    expect(text(query('.gh-readiness-status'))).toBe('Ready');
    expect(query('.gh-readiness-chips')).toBeNull();
    expect(query('details')).toBeNull();
    expect(text(query('[role="status"]'))).toBe('Batch ready.');
  });

  it('labels the card by its heading', () => {
    render([]);

    const section = query('section')!;
    expect(text(query(`#${section.getAttribute('aria-labelledby')}`))).toBe('Batch Readiness');
  });

  it('names a blocker with its count, opens the findings and offers no acknowledgment for it', () => {
    render([BLOCKER, WARNING, ADVICE_A]);

    expect(query('.gh-readiness')!.getAttribute('data-state')).toBe('blocked');
    expect(text(query('.gh-readiness-status'))).toBe('1 blocking');
    const chips = Array.from(el().querySelectorAll('.gh-readiness-chip')).map(c => text(c));
    expect(chips).toEqual(['Blocking 1', 'To review 1', 'Tips 1']);
    expect(query<HTMLDetailsElement>('details.gh-readiness-details')!.open).toBe(true);

    const blockerLine = query('.gh-readiness-item[data-severity="blocker"]')!;
    expect(text(blockerLine.querySelector('.gh-readiness-severity'))).toBe('Blocking');
    expect(text(blockerLine.querySelector('.gh-readiness-item-title'))).toBe(BLOCKER.title);
    expect(text(blockerLine.querySelector('.gh-readiness-item-detail'))).toBe(BLOCKER.detail);
    expect(blockerLine.querySelector('input[type="checkbox"]')).toBeNull();
  });

  it('lists blockers before warnings, and the advice in a nested closed tips disclosure', () => {
    render([ADVICE_A, WARNING, BLOCKER, ADVICE_B]);

    const main = query('details.gh-readiness-details > .gh-disclosure-body > .gh-readiness-list')!;
    expect(Array.from(main.children).map(li => li.getAttribute('data-severity'))).toEqual(['blocker', 'warning']);

    const tips = query<HTMLDetailsElement>('details.gh-readiness-tips')!;
    expect(tips.open).toBe(false);
    expect(text(tips.querySelector('summary'))).toBe('2 tips');
    expect(tips.querySelectorAll('.gh-readiness-item[data-severity="advice"]').length).toBe(2);
    expect(Array.from(tips.querySelectorAll('.gh-readiness-severity')).map(s => text(s))).toEqual(['Tip', 'Tip']);
  });

  it('asks for a warning\'s acknowledgment and emits its key when the checkbox is checked', () => {
    const emitted: ModelBatchAcknowledgment[] = [];
    component.acknowledgedChange.subscribe(e => emitted.push(e));
    render([WARNING]);

    expect(text(query('.gh-readiness-status'))).toBe('1 to review');
    const box = query<HTMLInputElement>('.gh-readiness-item[data-severity="warning"] input[type="checkbox"]')!;
    expect(box.checked).toBe(false);
    expect(text(box.closest('label'))).toBe('I understand');
    const describedBy = box.getAttribute('aria-describedby')!.split(' ');
    expect(describedBy.map(id => text(query(`#${id}`)))).toEqual([WARNING.title, WARNING.detail]);

    box.click();

    expect(emitted).toEqual([{ key: 'MB-W01:2,3', acknowledged: true }]);
  });

  it('reads Ready once every warning is acknowledged, and closes the findings', () => {
    render([WARNING, ADVICE_A]);
    expect(query<HTMLDetailsElement>('details.gh-readiness-details')!.open).toBe(true);

    render([WARNING, ADVICE_A], ['MB-W01:2,3']);

    expect(text(query('.gh-readiness-status'))).toBe('Ready');
    expect(query<HTMLDetailsElement>('details.gh-readiness-details')!.open).toBe(false);
    expect(Array.from(el().querySelectorAll('.gh-readiness-chip')).map(c => text(c))).toEqual(['Acknowledged 1', 'Tips 1']);
    expect(query<HTMLInputElement>('.gh-readiness-item[data-severity="warning"] input[type="checkbox"]')!.checked).toBe(true);
    expect(text(query('.gh-readiness-item[data-severity="warning"] .gh-readiness-severity'))).toBe('Acknowledged');
  });

  it('emits the field when Go to field is pressed, and names the line in the button', () => {
    const fields: string[] = [];
    component.focusField.subscribe(f => fields.push(f));
    render([BLOCKER, ADVICE_B]);

    const go = query<HTMLButtonElement>('.gh-readiness-item[data-severity="blocker"] .gh-readiness-go')!;
    expect(text(go)).toBe(`Go to field: ${BLOCKER.title}`);
    go.click();

    expect(fields).toEqual(['assessor']);
    // A finding about no one field has nowhere to go.
    expect(query('.gh-readiness-item[data-severity="advice"] .gh-readiness-go')).toBeNull();
  });

  it('gives each line a click-mode info tip keyed by code and acknowledgment key, with the rationale', () => {
    render([WARNING, ADVICE_B]);

    expect(query('#mbReadiness-tip-MB-W01-MB-W01_2_3')).toBeTruthy();
    expect(text(query('#mbReadiness-tip-MB-W01-MB-W01_2_3'))).toBe(MODEL_BATCH_FINDING_RATIONALE['MB-W01']);
    expect(text(query('#mbReadiness-tip-MB-T01-0'))).toBe(MODEL_BATCH_FINDING_RATIONALE['MB-T01']);
  });

  it('prefers the finding\'s own rationale over the launcher\'s text', () => {
    render([{ ...WARNING, rationale: 'The server says why.' }]);

    expect(text(query('#mbReadiness-tip-MB-W01-MB-W01_2_3'))).toBe('The server says why.');
  });

  it('announces the counts once the check settles, not while it is pending', () => {
    render([BLOCKER, WARNING]);
    expect(text(query('[role="status"]'))).toBe('Batch not ready: 1 blocking, 1 to review.');

    render([WARNING, ADVICE_A, ADVICE_B], [], true);
    expect(query('section')!.getAttribute('aria-busy')).toBe('true');
    expect(text(query('.gh-readiness-busy'))).toBe('Checking…');
    expect(text(query('[role="status"]'))).toBe('Batch not ready: 1 blocking, 1 to review.');

    render([WARNING, ADVICE_A, ADVICE_B], [], false);
    expect(text(query('[role="status"]'))).toBe('Batch not ready: 1 to review, 2 tips.');
  });

  it('carries no title attribute anywhere', () => {
    render([BLOCKER, WARNING, ADVICE_A, ADVICE_B]);

    expect(el().querySelectorAll('[title]').length).toBe(0);
  });

  it('reads a code by its leading token', () => {
    expect(modelBatchFindingCode({ code: ' mb-w01 MixedFamiliesSingleAssessor' })).toBe('MB-W01');
  });
});
