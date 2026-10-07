import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpTestingController } from '@angular/common/http/testing';

import { CcAnalysisResult } from '../chat-consistency.models';
import {
  CC_API,
  ccAnalysisResult,
  ccAnalysisSummary,
  ccAxis,
  ccRunRows,
  ccTimeline,
  chatConsistencyTestProviders,
  textOf
} from '../chat-consistency-tab.testing';
import { CcAnalysisWizardComponent, launchPreset, periodsRefusal } from './analysis-wizard.component';

describe('CcAnalysisWizardComponent', () => {
  let fixture: ComponentFixture<CcAnalysisWizardComponent>;
  let http: HttpTestingController;
  let el: HTMLElement;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [CcAnalysisWizardComponent],
      providers: chatConsistencyTestProviders()
    }).compileComponents();
    fixture = TestBed.createComponent(CcAnalysisWizardComponent);
    http = TestBed.inject(HttpTestingController);
    el = fixture.nativeElement as HTMLElement;
    document.body.appendChild(el);
    fixture.componentRef.setInput('timeline', ccTimeline());
    fixture.componentRef.setInput('rows', ccRunRows());
    fixture.componentRef.setInput('axis', ccAxis());
    fixture.detectChanges();
  });

  afterEach(() => {
    http.verify();
    fixture.destroy();
    el.remove();
  });

  const heading = (): HTMLElement => el.querySelector<HTMLElement>('#cc-wiz-step-title')!;
  const currentStep = (): string => textOf(el.querySelector('.cc-wiz-steps li[aria-current="step"]'));

  /** Step 2 starts the re-grade panel, which asks whether a re-grade is already running. */
  function goToRuns(): void {
    el.querySelector<HTMLButtonElement>('.cc-wiz-next')!.click();
    fixture.detectChanges();
    http.expectOne(`${CC_API}/regrade/job`).flush(null, { status: 204, statusText: 'No Content' });
    fixture.detectChanges();
  }

  function setDay(id: string, value: string): void {
    const input = el.querySelector<HTMLInputElement>(`#${id}`)!;
    input.value = value;
    input.dispatchEvent(new Event('change'));
    fixture.detectChanges();
  }

  it('shows four steps, the current one marked with aria-current="step"', () => {
    const items = Array.from(el.querySelectorAll('.cc-wiz-steps li'));
    expect(items.map(item => textOf(item))).toEqual(['1 Subject and periods', '2 Runs', '3 Results', '4 Reports']);
    expect(currentStep()).toBe('1 Subject and periods');
    expect(items[2].querySelector('button')!.getAttribute('aria-disabled')).toBe('true');
    expect(textOf(heading())).toBe('Step 1 of 4: Subject and periods');
  });

  it('starts from Launch vs last 14 days and shows Protocol V1 read-only', () => {
    expect(el.querySelector<HTMLInputElement>('input[name="cc-wiz-preset"][value="launch"]')!.checked).toBe(true);
    expect(el.querySelector<HTMLInputElement>('#cc-wiz-bs')!.value).toBe('2026-09-01');
    expect(el.querySelector<HTMLInputElement>('#cc-wiz-be')!.value).toBe('2026-09-14');
    expect(el.querySelector<HTMLInputElement>('#cc-wiz-cs')!.value).toBe('2026-09-18');
    expect(el.querySelector<HTMLInputElement>('#cc-wiz-ce')!.value).toBe('2026-10-01');
    const rows = Array.from(el.querySelectorAll<HTMLTableRowElement>('.cc-protocol-table tbody tr'))
      .map(row => Array.from(row.cells).map(cell => textOf(cell)));
    expect(rows).toEqual([
      ['P1 Quality', '±3 index points'], ['P2 Time to first answer text', '±15 %'], ['P3 Answer streaming rate', '±10 %'],
      ['P4 Work per turn', '±15 %'], ['P5 Cost per question', '±10 %']
    ]);
    expect(textOf(el.querySelector('.cc-wiz-protocol-facts'))).toContain('0.05, Holm-adjusted across P1–P5');
  });

  it('moves focus to the step heading on Next and on Back', () => {
    goToRuns();
    expect(currentStep()).toBe('2 Runs');
    expect(textOf(heading())).toBe('Step 2 of 4: Runs');
    expect(document.activeElement).toBe(heading());

    el.querySelector<HTMLButtonElement>('.cc-wiz-back')!.click();
    fixture.detectChanges();
    expect(currentStep()).toBe('1 Subject and periods');
    expect(document.activeElement).toBe(heading());
  });

  it('keeps Next unavailable while the periods overlap, and says why', () => {
    setDay('cc-wiz-cs', '2026-09-10');
    expect(textOf(el.querySelector('.cc-wiz-periods-error'))).toBe('The comparison must start after the baseline ends.');
    const next = el.querySelector<HTMLButtonElement>('.cc-wiz-next')!;
    expect(next.getAttribute('aria-disabled')).toBe('true');
    next.click();
    fixture.detectChanges();
    expect(currentStep()).toBe('1 Subject and periods');
    expect(el.querySelector<HTMLInputElement>('input[name="cc-wiz-preset"][value="custom"]')!.checked).toBe(true);
  });

  it('lists the detected Overseer changes for Before vs after an Overseer change', () => {
    el.querySelector<HTMLInputElement>('input[name="cc-wiz-preset"][value="event"]')!.click();
    fixture.detectChanges();
    const options = Array.from(el.querySelectorAll<HTMLOptionElement>('#cc-wiz-event option')).map(o => textOf(o));
    expect(options).toEqual(['2026-09-15: tool guides edited on 2026-09-15']);
    expect(el.querySelector<HTMLInputElement>('#cc-wiz-be')!.value).toBe('2026-09-14');
    expect(el.querySelector<HTMLInputElement>('#cc-wiz-cs')!.value).toBe('2026-09-15');
    expect(el.querySelector<HTMLInputElement>('#cc-wiz-ce')!.value).toBe('2026-10-01');
  });

  it('confirms on later data from the day after the last analysis of the subject', () => {
    fixture.componentRef.setInput('axis', ccAxis({ lastRunAtUtc: '2026-10-20T08:00:00Z' }));
    fixture.componentRef.setInput('analyses', [ccAnalysisSummary(7), ccAnalysisSummary(9, { createdAtUtc: '2026-09-01T00:00:00Z' })]);
    fixture.detectChanges();
    el.querySelector<HTMLInputElement>('input[name="cc-wiz-preset"][value="later"]')!.click();
    fixture.detectChanges();
    expect(el.querySelector<HTMLInputElement>('#cc-wiz-bs')!.value).toBe('2026-09-01');
    expect(el.querySelector<HTMLInputElement>('#cc-wiz-be')!.value).toBe('2026-09-14');
    expect(el.querySelector<HTMLInputElement>('#cc-wiz-cs')!.value).toBe('2026-10-03');
    expect(el.querySelector<HTMLInputElement>('#cc-wiz-ce')!.value).toBe('2026-10-20');
    expect(textOf(el.querySelector('.cc-wiz-preset-note'))).toContain('saved 2026-10-02 09:00 UTC');
  });

  it('preselects the eligible runs and the matched controls, posts the analysis and shows the result', () => {
    const saved: CcAnalysisResult[] = [];
    fixture.componentInstance.analysisSaved.subscribe(result => saved.push(result));
    el.querySelector<HTMLDetailsElement>('.cc-wiz-overrides')!.open = true;
    const p2 = el.querySelector<HTMLInputElement>('#cc-wiz-margin-P2')!;
    p2.value = '20';
    p2.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    goToRuns();

    const checked = (period: string) => Array.from(el.querySelectorAll<HTMLInputElement>(`table[data-period="${period}"] .cc-wiz-run-check`))
      .filter(box => box.checked).map(box => box.closest('tr')!.getAttribute('data-run-id'));
    expect(checked('baseline')).toEqual(['101', '102', '103']);
    expect(checked('comparison')).toEqual(['104', '105', '106']);
    const controls = Array.from(el.querySelectorAll<HTMLInputElement>('.cc-wiz-control-check'));
    expect(controls.map(box => textOf(box.closest('label')))).toEqual(['Control run #201', 'Control run #202', 'Control run #205', 'Control run #206']);
    expect(controls.every(box => box.checked)).toBe(true);
    expect(textOf(el.querySelector('.cc-wiz-strata'))).toBe('weekday 08–12 UTC');
    expect(Array.from(el.querySelectorAll('.cc-wiz-missing li')).map(item => textOf(item)))
      .toEqual(['Run #103 (Board Suite) has no matched control run.', 'Run #104 (Board Suite) has no matched control run.']);
    expect(textOf(el.querySelector('.cc-wiz-events'))).toBe('2026-09-15: tool guides edited on 2026-09-15');
    expect(textOf(el.querySelector('.cc-wiz-relaxed'))).toContain('caps grades at Indicated');

    // A run and a control are left out by hand.
    el.querySelector<HTMLInputElement>('table[data-period="baseline"] tr[data-run-id="103"] .cc-wiz-run-check')!.click();
    controls[0].click();
    fixture.detectChanges();

    el.querySelector<HTMLButtonElement>('.cc-wiz-analyze')!.click();
    fixture.detectChanges();
    expect(textOf(el.querySelector('.cc-wiz-analyze-status'))).toBe('Analyzing… this can take a minute.');
    const post = http.expectOne(`${CC_API}/analyses`);
    expect(post.request.method).toBe('POST');
    expect(post.request.body).toEqual({
      subjectModelKey: 'openai/gpt-5|high',
      baselineStartUtc: '2026-09-01T00:00:00.000Z',
      baselineEndUtc: '2026-09-14T23:59:59.999Z',
      comparisonStartUtc: '2026-09-18T00:00:00.000Z',
      comparisonEndUtc: '2026-10-01T23:59:59.999Z',
      baselineRunIds: [101, 102],
      comparisonRunIds: [104, 105, 106],
      relaxedPooling: false,
      controlRunIds: [202, 205, 206],
      protocolOverrides: { margins: { P2: 0.2 } }
    });
    post.flush(ccAnalysisResult());
    fixture.detectChanges();

    expect(currentStep()).toBe('3 Results');
    expect(document.activeElement).toBe(heading());
    expect(textOf(el.querySelector('.cc-headline-text'))).toContain('Overseer chat with GPT-5 high');
    expect(saved.map(result => result.analysisId)).toEqual([7]);
  });

  it('shows the refusal of a request the server turns down', () => {
    goToRuns();
    el.querySelector<HTMLButtonElement>('.cc-wiz-analyze')!.click();
    http.expectOne(`${CC_API}/analyses`).flush({ error: 'The baseline has no usable run.' }, { status: 400, statusText: 'Bad Request' });
    fixture.detectChanges();
    expect(textOf(el.querySelector('.cc-wiz-analyze-error'))).toBe('The baseline has no usable run.');
    expect(currentStep()).toBe('2 Runs');
  });

  it('opens a saved analysis on its results', () => {
    fixture.componentInstance.showResult(ccAnalysisResult());
    fixture.detectChanges();
    expect(currentStep()).toBe('3 Results');
    expect(document.activeElement).toBe(heading());
    expect(el.querySelector('app-cc-results-view')).not.toBeNull();
  });
});

describe('analysis wizard periods', () => {
  it('never lets the launch preset overlap on a short series', () => {
    const days = launchPreset(ccAxis({ firstRunAtUtc: '2026-09-01T00:00:00Z', lastRunAtUtc: '2026-09-10T00:00:00Z' }));
    expect(days).toEqual({ baselineStart: '2026-09-01', baselineEnd: '2026-08-31', comparisonStart: '2026-09-01', comparisonEnd: '2026-09-10' });
    expect(periodsRefusal(days)).toBe('The baseline must not end before it starts.');
  });

  it('accepts adjacent, ordered periods', () => {
    expect(periodsRefusal({ baselineStart: '2026-09-01', baselineEnd: '2026-09-14', comparisonStart: '2026-09-15', comparisonEnd: '2026-09-30' })).toBe('');
    expect(periodsRefusal({ baselineStart: '', baselineEnd: '2026-09-14', comparisonStart: '2026-09-15', comparisonEnd: '2026-09-30' })).toBe('Enter all four dates.');
  });
});
