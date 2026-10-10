import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpTestingController } from '@angular/common/http/testing';

import { CC_OUT_OF_DATE_RULE } from '../../chat-consistency-results';
import {
  CC_API,
  ccFreshness,
  ccOutOfDateFreshness,
  chatConsistencyTestProviders,
  textOf
} from '../../chat-consistency-tab.testing';
import { CcAnalysisFreshness } from '../../chat-consistency.models';
import { CcFreshnessNoticeComponent } from './freshness-notice.component';

describe('CcFreshnessNoticeComponent', () => {
  let fixture: ComponentFixture<CcFreshnessNoticeComponent>;
  let http: HttpTestingController;
  let el: HTMLElement;
  let answered: CcAnalysisFreshness[];

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [CcFreshnessNoticeComponent],
      providers: chatConsistencyTestProviders()
    }).compileComponents();
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(CcFreshnessNoticeComponent);
    el = fixture.nativeElement as HTMLElement;
    answered = [];
    fixture.componentInstance.freshnessChange.subscribe(freshness => answered.push(freshness));
  });

  afterEach(() => {
    http.verify();
    fixture.destroy();
    vi.restoreAllMocks();
  });

  /** Shows analysis `id` and answers its freshness request with `freshness`. */
  function show(id: number, freshness: CcAnalysisFreshness): void {
    fixture.componentRef.setInput('idPrefix', 'cc-test-fresh');
    fixture.componentRef.setInput('analysisId', id);
    fixture.detectChanges();
    const request = http.expectOne(`${CC_API}/analyses/${id}/freshness`);
    expect(request.request.method).toBe('GET');
    request.flush(freshness);
    fixture.detectChanges();
  }

  it('asks nothing for an unsaved analysis and shows nothing', () => {
    fixture.componentRef.setInput('analysisId', null);
    fixture.detectChanges();
    http.expectNone(r => r.url.endsWith('/freshness'));
    expect(el.children.length).toBe(0);
  });

  it('shows nothing while the freshness loads, nor for a current analysis', () => {
    fixture.componentRef.setInput('analysisId', 7);
    fixture.detectChanges();
    expect(el.querySelector('.cc-fresh-notice')).toBeNull();
    http.expectOne(`${CC_API}/analyses/7/freshness`).flush(ccFreshness());
    fixture.detectChanges();
    expect(el.querySelector('.cc-fresh-notice')).toBeNull();
    expect(el.querySelector('.cc-fresh-unchecked')).toBeNull();
    expect(answered.map(freshness => freshness.analysisId)).toEqual([7]);
  });

  it('says why an analysis is out of date, with the rule behind an info tip, and Analyze again', () => {
    let again = 0;
    fixture.componentInstance.analyzeAgain.subscribe(() => again++);
    show(7, ccOutOfDateFreshness());

    const notice = el.querySelector<HTMLElement>('.cc-fresh-notice')!;
    expect(notice.classList).toContain('alert-warning');
    expect(notice.getAttribute('role')).toBe('note');
    expect(notice.getAttribute('aria-labelledby')).toBe('cc-test-fresh-title');
    const title = notice.querySelector<HTMLElement>('#cc-test-fresh-title')!;
    expect(textOf(title)).toBe('This analysis is out of date.');
    expect(title.querySelector('strong')).not.toBeNull();
    expect(Array.from(notice.querySelectorAll('.cc-fresh-reason')).map(reason => textOf(reason))).toEqual([
      'Saved under analysis code version 5; Overseer now analyzes under version 6.',
      'Its runs, grades, controls, annotations or prices changed after it was saved.'
    ]);
    expect(textOf(notice.querySelector('.cc-fresh-advice'))).toBe(
      'Saved analyses never change. Analyze again for a current analysis with the same settings; this one stays in Analysis history as a record.');
    expect(textOf(notice.querySelector('#cc-test-fresh-rule-tip'))).toBe(CC_OUT_OF_DATE_RULE);
    expect(CC_OUT_OF_DATE_RULE).toBe(
      'An analysis is out of date when it was saved under an earlier analysis code version, or when its inputs changed after it was saved. '
      + 'Saved analyses never change.');

    const button = notice.querySelector<HTMLButtonElement>('#cc-test-fresh-again')!;
    expect(textOf(button)).toBe('Analyze again');
    expect(button.classList).toContain('btn-ghost');
    button.click();
    expect(again).toBe(1);
    http.expectNone(r => r.method === 'POST');
  });

  it('names only the earlier code when the inputs could not be checked', () => {
    show(7, ccOutOfDateFreshness({ inputsChanged: null, inputsNote: 'Saved before the request was stored.' }));
    expect(Array.from(el.querySelectorAll('.cc-fresh-reason')).map(reason => textOf(reason))).toEqual([
      'Saved under analysis code version 5; Overseer now analyzes under version 6.'
    ]);
    expect(el.querySelector('.cc-fresh-unchecked')).toBeNull();
  });

  it('says quietly why changes could not be checked on a current-code analysis', () => {
    show(7, ccFreshness({ inputsChanged: null, inputsNote: 'A run of the analysis was deleted.' }));
    expect(el.querySelector('.cc-fresh-notice')).toBeNull();
    const line = el.querySelector<HTMLElement>('.cc-fresh-unchecked')!;
    expect(textOf(line)).toBe('Changes since saving could not be checked: A run of the analysis was deleted.');
  });

  it('asks once per analysis id, and again for another analysis', () => {
    show(7, ccFreshness());
    fixture.componentRef.setInput('analysisId', 7);
    fixture.detectChanges();
    fixture.detectChanges();
    http.expectNone(`${CC_API}/analyses/7/freshness`);

    show(8, ccOutOfDateFreshness({ analysisId: 8 }));
    expect(el.querySelector('.cc-fresh-notice')).not.toBeNull();
    expect(answered.map(freshness => freshness.analysisId)).toEqual([7, 8]);
  });

  it('shows nothing and logs when the freshness cannot be read', () => {
    const log = vi.spyOn(console, 'error').mockImplementation(() => undefined);
    fixture.componentRef.setInput('analysisId', 7);
    fixture.detectChanges();
    http.expectOne(`${CC_API}/analyses/7/freshness`).flush({ error: 'Boom' }, { status: 500, statusText: 'Server Error' });
    fixture.detectChanges();
    expect(el.querySelector('.cc-fresh-notice')).toBeNull();
    expect(el.querySelector('.cc-fresh-unchecked')).toBeNull();
    expect(log).toHaveBeenCalledTimes(1);
    expect(answered).toEqual([]);
  });
});
