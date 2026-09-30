import { ComponentFixture, TestBed } from '@angular/core/testing';

import { BenchmarkReportAudience } from '../../../services/admin-benchmark.service';
import { ReportChartPickerComponent } from './report-chart-picker.component';
import {
  DEFAULT_CHART_SELECTION,
  REPORT_CHART_FIGURES,
  REPORT_CHART_STORAGE_KEY,
  ReportChartSelection
} from './report-charts';

const { ExecutiveSummary, TechnicalReport, InternalBrief } = BenchmarkReportAudience;
const ALL_KEYS = REPORT_CHART_FIGURES.map(figure => figure.key);

describe('ReportChartPickerComponent', () => {
  let fixture: ComponentFixture<ReportChartPickerComponent>;
  let host: HTMLElement;
  let emitted: ReportChartSelection[];

  beforeEach(async () => {
    localStorage.removeItem(REPORT_CHART_STORAGE_KEY);
    await TestBed.configureTestingModule({ imports: [ReportChartPickerComponent] }).compileComponents();
    fixture = TestBed.createComponent(ReportChartPickerComponent);
    host = fixture.nativeElement as HTMLElement;
    emitted = [];
    fixture.componentInstance.selectionChange.subscribe(selection => emitted.push(selection));
    fixture.componentRef.setInput('selection', DEFAULT_CHART_SELECTION);
    fixture.componentRef.setInput('available', ALL_KEYS);
    fixture.detectChanges();
  });

  afterEach(() => {
    fixture.destroy();
    localStorage.removeItem(REPORT_CHART_STORAGE_KEY);
  });

  const q = <T extends HTMLElement = HTMLElement>(selector: string): T | null => host.querySelector<T>(selector);
  const cell = (audience: BenchmarkReportAudience, key: string): HTMLInputElement =>
    q<HTMLInputElement>(`#rcp-${audience}-${key}`)!;
  const checkedIn = (audience: BenchmarkReportAudience): string[] =>
    ALL_KEYS.filter(key => cell(audience, key).checked);
  const button = (kind: 'all' | 'none', audience: BenchmarkReportAudience): HTMLButtonElement =>
    q<HTMLButtonElement>(`.rcp-${kind}[data-audience="${audience}"]`)!;

  it('is a captioned table of one row per figure and one column per document type', () => {
    const table = q('table.rcp-table')!;
    expect(table.querySelector('caption')!.textContent!.trim()).toBe('Charts in PDF and Word');
    const headers = Array.from(table.querySelectorAll('thead th .rcp-col-name')).map(th => th.textContent!.trim());
    expect(table.querySelector('thead th')!.textContent!.trim()).toBe('Chart');
    expect(headers).toEqual(['Executive Summary', 'Report for AI Researchers and Developers', 'Internal Improvement Brief']);
    const rows = Array.from(table.querySelectorAll('tbody th[scope="row"] .rcp-figure-title')).map(th => th.textContent!.trim());
    expect(rows).toEqual(REPORT_CHART_FIGURES.map(figure => figure.title));
    expect(table.querySelectorAll('tbody input[type="checkbox"]').length).toBe(21);
  });

  it('names every checkbox for its figure and document, with the target section beneath it', () => {
    const exec = cell(ExecutiveSummary, 'p1a-quality');
    expect(exec.getAttribute('aria-label')).toBe('Include Intelligence in the Executive Summary');
    expect(cell(TechnicalReport, 's1-quality-speed').getAttribute('aria-label'))
      .toBe('Include Intelligence against speed in the Report for AI Researchers and Developers');
    expect(cell(InternalBrief, 'p1c-cost').getAttribute('aria-label')).toBe('Include Cost in the Internal Improvement Brief');

    const placement = q(`#${exec.getAttribute('aria-describedby')}`)!;
    expect(placement.textContent!.trim()).toBe('How it compares');
    expect(q(`#rcp-${TechnicalReport}-p1a-quality-placement`)!.textContent!.trim()).toBe('Results against peers → Quality');
    expect(q(`#rcp-${InternalBrief}-p2-profile-placement`)!.textContent!.trim()).toBe('§3 Key figures');
  });

  it('shows the default selection', () => {
    expect(checkedIn(ExecutiveSummary)).toEqual(['p1a-quality', 's2-quality-cost']);
    expect(checkedIn(TechnicalReport)).toEqual(ALL_KEYS);
    expect(checkedIn(InternalBrief)).toEqual(['p1a-quality', 'p1b-speed', 'p1c-cost']);
  });

  it('emits the normalized selection when a checkbox changes', () => {
    cell(ExecutiveSummary, 'p1c-cost').click();
    fixture.detectChanges();

    expect(emitted.length).toBe(1);
    expect(emitted[0][ExecutiveSummary]).toEqual(['p1a-quality', 'p1c-cost', 's2-quality-cost']);
    expect(emitted[0][TechnicalReport]).toEqual(ALL_KEYS);

    cell(ExecutiveSummary, 'p1a-quality').click();
    fixture.detectChanges();
    expect(emitted[1][ExecutiveSummary]).toEqual(['p1c-cost', 's2-quality-cost']);
    expect(checkedIn(ExecutiveSummary)).toEqual(['p1c-cost', 's2-quality-cost']);
  });

  it('offers All and None per column, each named for its column', () => {
    const none = button('none', TechnicalReport);
    expect(none.textContent!.replace(/\s+/g, ' ').trim()).toBe('None of the charts in the Report for AI Researchers and Developers');
    expect(button('all', ExecutiveSummary).textContent!.replace(/\s+/g, ' ').trim()).toBe('All charts in the Executive Summary');

    none.click();
    fixture.detectChanges();
    expect(emitted[0][TechnicalReport]).toEqual([]);
    expect(checkedIn(TechnicalReport)).toEqual([]);

    button('all', ExecutiveSummary).click();
    fixture.detectChanges();
    expect(emitted[1][ExecutiveSummary]).toEqual(ALL_KEYS);
    expect(checkedIn(ExecutiveSummary)).toEqual(ALL_KEYS);
  });

  it('keeps the columns of unchecked documents listed but aria-disabled, with the reason', () => {
    fixture.componentRef.setInput('enabledAudiences', [ExecutiveSummary, TechnicalReport]);
    fixture.detectChanges();

    const brief = cell(InternalBrief, 'p1a-quality');
    expect(brief.disabled).toBeFalse();
    expect(brief.getAttribute('aria-disabled')).toBe('true');
    const reason = q(`#rcp-col-${InternalBrief}-reason`)!;
    expect(reason.textContent!.trim()).toBe('Not checked under Documents');
    expect(brief.getAttribute('aria-describedby')).toContain(`rcp-col-${InternalBrief}-reason`);
    expect(button('all', InternalBrief).getAttribute('aria-disabled')).toBe('true');
    expect(button('none', InternalBrief).getAttribute('aria-describedby')).toBe(`rcp-col-${InternalBrief}-reason`);
    expect(cell(ExecutiveSummary, 'p1a-quality').hasAttribute('aria-disabled')).toBeFalse();
    expect(button('all', ExecutiveSummary).hasAttribute('aria-disabled')).toBeFalse();

    // Inert: the click changes nothing and emits nothing.
    brief.click();
    button('none', InternalBrief).click();
    button('all', InternalBrief).click();
    fixture.detectChanges();
    expect(brief.checked).toBeTrue();
    expect(emitted).toEqual([]);
  });

  it('lists a figure the comparison cannot draw, unchecked and aria-disabled, with the reason', () => {
    fixture.componentRef.setInput('available', ALL_KEYS.filter(key => key !== 'p2-profile' && key !== 's3-speed-cost'));
    fixture.componentRef.setInput('unavailableReasons', { 's3-speed-cost': 'no cost measured' });
    fixture.detectChanges();

    const profile = cell(TechnicalReport, 'p2-profile');
    expect(profile.getAttribute('aria-disabled')).toBe('true');
    expect(profile.checked).toBeFalse();
    expect(q('#rcp-row-p2-profile-reason')!.textContent!.trim()).toBe('needs three or more models');
    expect(profile.getAttribute('aria-describedby')).toContain('rcp-row-p2-profile-reason');
    expect(q('#rcp-row-s3-speed-cost-reason')!.textContent!.trim()).toBe('no cost measured');

    profile.click();
    fixture.detectChanges();
    expect(profile.checked).toBeFalse();
    expect(emitted).toEqual([]);

    // All takes only the figures the comparison can draw.
    button('all', TechnicalReport).click();
    fixture.detectChanges();
    expect(emitted[0][TechnicalReport]).toEqual(['p1a-quality', 'p1b-speed', 'p1c-cost', 's1-quality-speed', 's2-quality-cost']);
  });

  it('takes a new selection from its host', () => {
    fixture.componentRef.setInput('selection', { [ExecutiveSummary]: ['p1b-speed'] });
    fixture.detectChanges();
    expect(checkedIn(ExecutiveSummary)).toEqual(['p1b-speed']);
    expect(checkedIn(TechnicalReport)).toEqual([]);
  });

  it('never touches localStorage; the host stores the selection', () => {
    const setItem = spyOn(localStorage, 'setItem').and.callThrough();
    const getItem = spyOn(localStorage, 'getItem').and.callThrough();
    cell(ExecutiveSummary, 'p1c-cost').click();
    button('none', InternalBrief).click();
    fixture.detectChanges();
    expect(setItem).not.toHaveBeenCalled();
    expect(getItem).not.toHaveBeenCalled();
    expect(localStorage.getItem(REPORT_CHART_STORAGE_KEY)).toBeNull();
  });

  it('keeps the caption for assistive technology alone when told to', () => {
    fixture.componentRef.setInput('captionVisible', false);
    fixture.detectChanges();
    expect(q('caption')!.classList).toContain('visually-hidden');
  });
});
