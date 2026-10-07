import { ChangeDetectorRef } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpTestingController, TestRequest } from '@angular/common/http/testing';

import { toModelPickerOptions } from '../../../../shared/model-picker/model-picker.component';
import { groupOverseerEvents } from '../chat-consistency-events';
import { CcOpenDocumentsRequest } from '../chat-consistency.models';
import {
  CC_API,
  ccAnalysisResult,
  ccConfig,
  ccPoint,
  ccReportEstimate,
  ccTimeline,
  chatConsistencyTestProviders,
  textOf
} from '../chat-consistency-tab.testing';
import { CC_PROVIDER_REPORT_UNKNOWN, CC_REPORT_ESTIMATE_DEBOUNCE_MS, CcReportsStepComponent } from './reports-step.component';

const DOCUMENTS_URL = '/api/admin/benchmark/report-documents';

const settle = () => new Promise<void>(resolve => setTimeout(resolve, CC_REPORT_ESTIMATE_DEBOUNCE_MS + 60));

describe('CcReportsStepComponent', () => {
  let fixture: ComponentFixture<CcReportsStepComponent>;
  let http: HttpTestingController;
  let el: HTMLElement;
  const writer = ccConfig(30, { displayName: 'Claude writer', provider: 'Anthropic' });

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [CcReportsStepComponent],
      providers: chatConsistencyTestProviders()
    }).compileComponents();
    fixture = TestBed.createComponent(CcReportsStepComponent);
    http = TestBed.inject(HttpTestingController);
    el = fixture.nativeElement as HTMLElement;
    fixture.componentRef.setInput('result', ccAnalysisResult());
    fixture.componentRef.setInput('writerConfigs', [writer]);
    fixture.componentRef.setInput('writerOptions', toModelPickerOptions([writer]));
    fixture.componentRef.setInput('points', ccTimeline().points);
    fixture.detectChanges();
    http.expectOne(`${CC_API}/analyses/7/report-documents/job`).flush(null, { status: 204, statusText: 'No Content' });
    const documents = http.expectOne(r => r.url === DOCUMENTS_URL);
    expect(documents.request.params.get('subject')).toBe('chat-consistency:7');
    documents.flush([]);
    fixture.detectChanges();
  });

  afterEach(() => {
    el.querySelectorAll('dialog').forEach(dialog => dialog.open && dialog.close());
    http.verify();
    fixture.destroy();
  });

  const providerBox = () => el.querySelector<HTMLInputElement>('#cc-rep-audience-4')!;

  async function chooseWriter(): Promise<TestRequest> {
    el.querySelector<HTMLButtonElement>('.cc-report-writer-model-selector .selector-trigger')!.click();
    fixture.detectChanges();
    el.querySelector<HTMLElement>('.cc-report-writer-model-selector [role="option"]')!.click();
    fixture.detectChanges();
    await settle();
    const estimate = http.expectOne(`${CC_API}/analyses/7/report-documents/estimate`);
    expect(estimate.request.method).toBe('POST');
    return estimate;
  }

  it('draws the attached charts over the analyzed runs, with every timeline point for the harness and the timeline\'s numbering', () => {
    const points = [...ccTimeline().points, ccPoint(201, '2026-10-05T08:00:00Z')];
    const numbering = groupOverseerEvents(ccTimeline().events, points);
    fixture.componentRef.setInput('points', points);
    fixture.componentRef.setInput('eventNumbering', numbering);
    fixture.detectChanges();

    const input = fixture.componentInstance.chartInput();
    expect(input.points.map(point => point.runId)).toEqual([101, 102, 103, 104, 105, 106]);
    expect(input.harnessPoints).toBe(points);
    expect(input.eventNumbering).toBe(numbering);
    expect(input.events).toEqual(ccAnalysisResult().events);
    expect(input.bands!.map(band => band.name)).toEqual(['Baseline', 'Comparison']);
  });

  it('lists the four documents, the first three checked', () => {
    const labels = Array.from(el.querySelectorAll('.cc-rep-audiences .checkbox-label')).map(label => textOf(label));
    expect(labels).toEqual(['Executive Summary', 'Report for AI Researchers and Developers', 'Internal Brief', 'Provider Issue Report']);
    const boxes = Array.from(el.querySelectorAll<HTMLInputElement>('.cc-rep-audiences input[type="checkbox"]'));
    expect(boxes.map(box => box.checked)).toEqual([true, true, true, false]);
  });

  it('disables the Provider Issue Report with its reason shown as text', async () => {
    expect(providerBox().disabled).toBe(true);
    const reason = el.querySelector('#cc-rep-provider-reason')!;
    expect(textOf(reason)).toBe(CC_PROVIDER_REPORT_UNKNOWN);
    expect(providerBox().getAttribute('aria-describedby')).toBe('cc-rep-provider-reason');

    const estimate = await chooseWriter();
    expect(estimate.request.body).toEqual({ writerModelConfigurationId: 30, audiences: [1, 2, 3] });
    estimate.flush(ccReportEstimate());
    fixture.detectChanges();

    expect(providerBox().disabled).toBe(true);
    expect(textOf(el.querySelector('#cc-rep-provider-reason'))).toBe('No change attributed to the provider is established or indicated.');
    expect(textOf(el.querySelector('#cc-rep-estimate'))).toBe('Estimated cost: about $0.120');
  });

  it('enables the Provider Issue Report when the estimate says it is available', async () => {
    const estimate = await chooseWriter();
    estimate.flush(ccReportEstimate({ providerIssueReportAvailable: true, providerIssueReportReason: null }));
    fixture.detectChanges();
    expect(providerBox().disabled).toBe(false);
    expect(el.querySelector('#cc-rep-provider-reason')).toBeNull();

    providerBox().click();
    fixture.detectChanges();
    await settle();
    const again = http.expectOne(`${CC_API}/analyses/7/report-documents/estimate`);
    expect(again.request.body).toEqual({ writerModelConfigurationId: 30, audiences: [1, 2, 3, 4] });
    again.flush(ccReportEstimate({ providerIssueReportAvailable: true, providerIssueReportReason: null }));
  });

  it('writes the checked documents and follows the job, with Cancel', async () => {
    const estimate = await chooseWriter();
    estimate.flush(ccReportEstimate());
    fixture.detectChanges();

    el.querySelector<HTMLButtonElement>('.cc-rep-write-btn')!.click();
    const write = http.expectOne(`${CC_API}/analyses/7/report-documents`);
    expect(write.request.method).toBe('POST');
    expect(write.request.body).toEqual({ writerModelConfigurationId: 30, audiences: [1, 2, 3] });
    write.flush({ runId: 7, status: 1, audiences: [1, 2, 3] }, { status: 202, statusText: 'Accepted' });
    fixture.detectChanges();

    expect(textOf(el.querySelector('.cc-rep-status'))).toBe('Waiting for the report writer');
    el.querySelector<HTMLButtonElement>('.cc-rep-cancel-btn')!.click();
    const cancel = http.expectOne(`${CC_API}/analyses/7/report-documents/cancel`);
    expect(cancel.request.method).toBe('POST');
    cancel.flush(null, { status: 409, statusText: 'Conflict' });
    fixture.detectChanges();
  });

  it('asks for the same-provider acknowledgment before writing with such a writer', async () => {
    const sameProvider = ccConfig(31, { displayName: 'GPT writer', provider: 'OpenAI', modelId: 'gpt-4.1' });
    fixture.componentRef.setInput('writerConfigs', [sameProvider]);
    fixture.componentRef.setInput('writerOptions', toModelPickerOptions([sameProvider]));
    fixture.detectChanges();
    const estimate = await chooseWriter();
    estimate.flush(ccReportEstimate());
    fixture.detectChanges();
    expect(textOf(el.querySelector('#cc-rep-writer-warning'))).toContain('GPT writer is from OpenAI');

    el.querySelector<HTMLButtonElement>('.cc-rep-write-btn')!.click();
    fixture.detectChanges();
    const dialog = el.querySelector<HTMLDialogElement>('dialog.cc-rep-confirm-dialog')!;
    expect(dialog.open).toBe(true);
    http.expectNone(`${CC_API}/analyses/7/report-documents`);

    dialog.querySelector<HTMLButtonElement>('.cc-rep-same-provider-confirm')!.click();
    const write = http.expectOne(`${CC_API}/analyses/7/report-documents`);
    expect(write.request.body).toEqual({ writerModelConfigurationId: 31, audiences: [1, 2, 3], acknowledgeSameProvider: true });
    write.flush({ error: 'The spending cap is reached.' }, { status: 429, statusText: 'Too Many Requests' });
    fixture.detectChanges();
    expect(textOf(el.querySelector('.cc-rep-write-error'))).toBe('The spending cap is reached.');
  });

  it('keeps Open in Download Center unavailable until a document exists, then emits the analysis id', () => {
    const requests: CcOpenDocumentsRequest[] = [];
    fixture.componentInstance.openDocuments.subscribe(request => requests.push(request));
    const button = el.querySelector<HTMLButtonElement>('.cc-rep-open-btn')!;
    expect(button.getAttribute('aria-disabled')).toBe('true');
    button.click();
    expect(requests).toEqual([]);

    fixture.componentInstance.documentCount = 2;
    fixture.debugElement.injector.get(ChangeDetectorRef).markForCheck();
    fixture.detectChanges();
    expect(button.hasAttribute('aria-disabled')).toBe(false);
    button.click();
    expect(requests).toEqual([{ analysisId: 7 }]);
  });
});
