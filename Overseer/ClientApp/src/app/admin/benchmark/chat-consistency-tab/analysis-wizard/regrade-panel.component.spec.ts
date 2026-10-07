import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpTestingController } from '@angular/common/http/testing';

import { toModelPickerOptions } from '../../../../shared/model-picker/model-picker.component';
import {
  CC_API,
  ccConfig,
  ccRegradeEstimate,
  ccRegradeJob,
  chatConsistencyTestProviders,
  textOf
} from '../chat-consistency-tab.testing';
import { CcRegradePanelComponent } from './regrade-panel.component';

describe('CcRegradePanelComponent', () => {
  let fixture: ComponentFixture<CcRegradePanelComponent>;
  let http: HttpTestingController;
  let el: HTMLElement;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [CcRegradePanelComponent],
      providers: chatConsistencyTestProviders()
    }).compileComponents();
    fixture = TestBed.createComponent(CcRegradePanelComponent);
    http = TestBed.inject(HttpTestingController);
    el = fixture.nativeElement as HTMLElement;
    fixture.componentRef.setInput('runIds', [101, 103]);
    fixture.componentRef.setInput('pickerOptions', toModelPickerOptions([ccConfig(21, { displayName: 'Claude Opus assessor' })]));
    fixture.detectChanges();
    http.expectOne(`${CC_API}/regrade/job`).flush(null, { status: 204, statusText: 'No Content' });
    fixture.detectChanges();
  });

  afterEach(() => {
    el.querySelectorAll('dialog').forEach(dialog => dialog.open && dialog.close());
    http.verify();
    fixture.destroy();
  });

  const estimateButton = () => el.querySelector<HTMLButtonElement>('.cc-regrade-estimate-btn')!;
  const dialog = () => el.querySelector<HTMLDialogElement>('dialog.cc-regrade-dialog')!;

  function chooseAssessor(): void {
    el.querySelector<HTMLButtonElement>('.cc-regrade-assessor-selector .selector-trigger')!.click();
    fixture.detectChanges();
    el.querySelector<HTMLElement>('.cc-regrade-assessor-selector [role="option"]')!.click();
    fixture.detectChanges();
  }

  function estimate(): void {
    estimateButton().click();
    fixture.detectChanges();
    const req = http.expectOne(`${CC_API}/regrade/estimate`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ runIds: [101, 103], assessorConfigId: 21 });
    req.flush(ccRegradeEstimate());
    fixture.detectChanges();
  }

  it('asks for an assessor before estimating', () => {
    expect(estimateButton().getAttribute('aria-disabled')).toBe('true');
    expect(textOf(el.querySelector('#cc-regrade-blocked'))).toBe('Choose the common assessor.');
    estimateButton().click();
    http.expectNone(`${CC_API}/regrade/estimate`);
  });

  it('shows the estimate in a confirmation dialog and posts the re-grade only from it, with confirmed: true', () => {
    chooseAssessor();
    estimate();

    // The estimate alone starts nothing.
    http.expectNone(`${CC_API}/regrade`);
    expect(dialog().open).toBe(true);
    expect(textOf(dialog().querySelector('h3'))).toBe('Re-grade 1 of 2 runs?');
    expect(textOf(dialog().querySelector('.cc-regrade-total'))).toBe('about $0.400');
    expect(textOf(dialog().querySelector('.cc-regrade-skipped'))).toBe('Run #103: The run has no gradable answers.');

    dialog().querySelector<HTMLButtonElement>('.cc-regrade-confirm')!.click();
    fixture.detectChanges();
    const start = http.expectOne(`${CC_API}/regrade`);
    expect(start.request.method).toBe('POST');
    expect(start.request.body).toEqual({ runIds: [101, 103], assessorConfigId: 21, confirmed: true });
    start.flush(ccRegradeJob(), { status: 202, statusText: 'Accepted' });
    fixture.detectChanges();

    expect(dialog().open).toBe(false);
    expect(textOf(el.querySelector('.cc-regrade-progress-text'))).toBe('Re-grading: 0 of 1 run re-graded, now run #101.');
    expect(el.querySelector('.cc-regrade-progress progress')).not.toBeNull();
  });

  it('starts nothing when the confirmation is canceled', () => {
    chooseAssessor();
    estimate();
    dialog().querySelector<HTMLButtonElement>('.cc-regrade-dialog-cancel')!.click();
    fixture.detectChanges();
    expect(dialog().open).toBe(false);
    http.expectNone(`${CC_API}/regrade`);
    expect(document.activeElement).toBe(estimateButton());
  });

  it('keeps Re-grade unavailable when the assessor is refused, and shows why', () => {
    chooseAssessor();
    estimateButton().click();
    http.expectOne(`${CC_API}/regrade/estimate`).flush(ccRegradeEstimate({ assessorRefusal: 'The assessor has no API key.', eligibleRunCount: 0 }));
    fixture.detectChanges();
    expect(textOf(dialog().querySelector('.cc-regrade-refusal'))).toBe('The assessor has no API key.');
    expect(dialog().querySelector<HTMLButtonElement>('.cc-regrade-confirm')!.disabled).toBe(true);
  });

  it('shows a refused start inside the confirmation', () => {
    chooseAssessor();
    estimate();
    dialog().querySelector<HTMLButtonElement>('.cc-regrade-confirm')!.click();
    http.expectOne(`${CC_API}/regrade`).flush({ error: 'A benchmark run is in progress.' }, { status: 400, statusText: 'Bad Request' });
    fixture.detectChanges();
    expect(dialog().open).toBe(true);
    expect(textOf(dialog().querySelector('.cc-regrade-start-error'))).toBe('A benchmark run is in progress.');
  });

  it('cancels a running re-grade', () => {
    chooseAssessor();
    estimate();
    dialog().querySelector<HTMLButtonElement>('.cc-regrade-confirm')!.click();
    http.expectOne(`${CC_API}/regrade`).flush(ccRegradeJob(), { status: 202, statusText: 'Accepted' });
    fixture.detectChanges();

    el.querySelector<HTMLButtonElement>('.cc-regrade-cancel-btn')!.click();
    const cancel = http.expectOne(`${CC_API}/regrade/cancel`);
    expect(cancel.request.method).toBe('POST');
    cancel.flush(ccRegradeJob({ status: 'canceled', currentRunId: null }), { status: 202, statusText: 'Accepted' });
    fixture.detectChanges();
    expect(textOf(el.querySelector('.cc-regrade-progress-text'))).toBe('Canceled: 0 of 1 run re-graded.');
    expect(el.querySelector('.cc-regrade-cancel-btn')).toBeNull();
  });
});
