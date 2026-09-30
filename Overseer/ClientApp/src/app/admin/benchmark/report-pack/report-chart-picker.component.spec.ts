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
  const tab = (audience: BenchmarkReportAudience): HTMLButtonElement => q<HTMLButtonElement>(`#rcp-tab-${audience}`)!;
  const select = (audience: BenchmarkReportAudience): void => {
    tab(audience).click();
    fixture.detectChanges();
  };
  const cell = (audience: BenchmarkReportAudience, key: string): HTMLInputElement =>
    q<HTMLInputElement>(`#rcp-${audience}-${key}`)!;
  const checkedIn = (audience: BenchmarkReportAudience): string[] => {
    select(audience);
    return ALL_KEYS.filter(key => cell(audience, key).checked);
  };
  const button = (kind: 'all' | 'none', audience: BenchmarkReportAudience): HTMLButtonElement =>
    q<HTMLButtonElement>(`.rcp-${kind}[data-audience="${audience}"]`)!;
  const selectedTab = (): string | null =>
    q('[role="tab"][aria-selected="true"]')?.getAttribute('data-audience') ?? null;
  const press = (target: HTMLElement, key: string): KeyboardEvent => {
    const event = new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true });
    target.dispatchEvent(event);
    fixture.detectChanges();
    return event;
  };
  const text = (element: Element | null): string => element!.textContent!.replace(/\s+/g, ' ').trim();

  it('is a captioned group with one segment per document type', () => {
    const group = q('.rcp')!;
    expect(group.getAttribute('role')).toBe('group');
    expect(group.getAttribute('aria-labelledby')).toBe('rcp-caption');
    expect(text(q('#rcp-caption'))).toBe('Charts in PDF and Word');
    expect(q('table')).toBeNull();

    const tabs = Array.from(host.querySelectorAll<HTMLElement>('[role="tablist"] [role="tab"]'));
    expect(tabs.map(t => text(t.querySelector('.rcp-tab-name')))).toEqual(['Executive', 'Researchers', 'Internal']);
    expect(tabs.map(t => t.getAttribute('aria-selected'))).toEqual(['true', 'false', 'false']);
    expect(tabs.map(t => t.getAttribute('tabindex'))).toEqual(['0', '-1', '-1']);
    expect(tabs[0].getAttribute('aria-controls')).toBe(`rcp-panel-${ExecutiveSummary}`);

    const panel = q('[role="tabpanel"]')!;
    expect(panel.id).toBe(`rcp-panel-${ExecutiveSummary}`);
    expect(panel.getAttribute('aria-labelledby')).toBe(`rcp-tab-${ExecutiveSummary}`);
    expect(panel.getAttribute('tabindex')).toBe('0');
    expect(text(panel.querySelector('.rcp-panel-title'))).toBe('Executive Summary');

    const boxes = Array.from(panel.querySelectorAll<HTMLInputElement>('input[type="checkbox"]'));
    expect(boxes.length).toBe(7);
    expect(boxes.every(box => box.id.startsWith(`rcp-${ExecutiveSummary}-`))).toBeTrue();
    expect(host.querySelectorAll('input[type="checkbox"]').length).toBe(7);
    const titles = Array.from(panel.querySelectorAll('.rcp-figure-title')).map(title => text(title));
    expect(titles).toEqual(REPORT_CHART_FIGURES.map(figure => figure.title));
  });

  it('names every checkbox for its figure and document, with the target section beneath it', () => {
    const exec = cell(ExecutiveSummary, 'p1a-quality');
    expect(exec.getAttribute('aria-label')).toBe('Include Intelligence in the Executive Summary');
    const placement = q(`#${exec.getAttribute('aria-describedby')}`)!;
    expect(text(placement)).toBe('How it compares');

    select(TechnicalReport);
    expect(cell(TechnicalReport, 's1-quality-speed').getAttribute('aria-label'))
      .toBe('Include Intelligence against speed in the Report for AI Researchers and Developers');
    expect(text(q(`#rcp-${TechnicalReport}-p1a-quality-placement`))).toBe('Results against peers → Quality');

    select(InternalBrief);
    expect(cell(InternalBrief, 'p1c-cost').getAttribute('aria-label')).toBe('Include Cost in the Internal Improvement Brief');
    expect(text(q(`#rcp-${InternalBrief}-p2-profile-placement`))).toBe('§3 Key figures');
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

  it('offers All and None per document, each named for its document', () => {
    expect(text(button('all', ExecutiveSummary))).toBe('All charts in the Executive Summary');

    select(TechnicalReport);
    const none = button('none', TechnicalReport);
    expect(text(none)).toBe('None of the charts in the Report for AI Researchers and Developers');
    none.click();
    fixture.detectChanges();
    expect(emitted[0][TechnicalReport]).toEqual([]);
    expect(checkedIn(TechnicalReport)).toEqual([]);

    select(ExecutiveSummary);
    button('all', ExecutiveSummary).click();
    fixture.detectChanges();
    expect(emitted[1][ExecutiveSummary]).toEqual(ALL_KEYS);
    expect(checkedIn(ExecutiveSummary)).toEqual(ALL_KEYS);
  });

  it('switches documents by click and by the keyboard', () => {
    select(TechnicalReport);
    expect(selectedTab()).toBe(String(TechnicalReport));
    expect(q('[role="tabpanel"]')!.id).toBe(`rcp-panel-${TechnicalReport}`);
    expect(text(q('.rcp-panel-title'))).toBe('Report for AI Researchers and Developers');
    expect(tab(TechnicalReport).getAttribute('tabindex')).toBe('0');
    expect(tab(ExecutiveSummary).getAttribute('tabindex')).toBe('-1');

    const steps: [string, BenchmarkReportAudience][] = [
      ['ArrowRight', InternalBrief],
      ['ArrowRight', ExecutiveSummary],
      ['ArrowLeft', InternalBrief],
      ['Home', ExecutiveSummary],
      ['End', InternalBrief],
      ['ArrowLeft', TechnicalReport]
    ];
    tab(TechnicalReport).focus();
    for (const [key, expected] of steps) {
      const event = press(document.activeElement as HTMLElement, key);
      expect(event.defaultPrevented).withContext(key).toBeTrue();
      expect(selectedTab()).withContext(key).toBe(String(expected));
      expect(document.activeElement).withContext(key).toBe(tab(expected));
      expect(q('[role="tabpanel"]')!.id).withContext(key).toBe(`rcp-panel-${expected}`);
    }

    const other = press(tab(TechnicalReport), 'Enter');
    expect(other.defaultPrevented).toBeFalse();
    expect(selectedTab()).toBe(String(TechnicalReport));
    expect(emitted).toEqual([]);
  });

  it('counts the selection on each segment', () => {
    const counts = (): string[] => Array.from(host.querySelectorAll('.rcp-tab-count')).map(count => text(count));
    expect(counts()).toEqual(['2', '7', '3']);
    expect(q('.rcp-tab-count')!.getAttribute('aria-hidden')).toBe('true');
    expect(text(tab(ExecutiveSummary).querySelector('.visually-hidden'))).toBe(', 2 of 7 charts');

    cell(ExecutiveSummary, 'p1c-cost').click();
    fixture.detectChanges();
    expect(counts()).toEqual(['3', '7', '3']);
    expect(text(tab(ExecutiveSummary).querySelector('.visually-hidden'))).toBe(', 3 of 7 charts');
  });

  it('keeps an unchecked document selectable but inert, with the reason', () => {
    fixture.componentRef.setInput('enabledAudiences', [ExecutiveSummary, TechnicalReport]);
    fixture.detectChanges();

    const briefTab = tab(InternalBrief);
    expect(briefTab.classList).toContain('is-unchecked');
    expect(text(briefTab.querySelector('.rcp-tab-count'))).toBe('—');
    expect(text(briefTab.querySelector('.visually-hidden'))).toBe(', not checked under Documents');
    expect(selectedTab()).toBe(String(ExecutiveSummary));
    expect(cell(ExecutiveSummary, 'p1a-quality').hasAttribute('aria-disabled')).toBeFalse();
    expect(button('all', ExecutiveSummary).hasAttribute('aria-disabled')).toBeFalse();
    expect(q(`#rcp-col-${ExecutiveSummary}-reason`)).toBeNull();

    select(InternalBrief);
    const brief = cell(InternalBrief, 'p1a-quality');
    expect(brief.disabled).toBeFalse();
    expect(brief.getAttribute('aria-disabled')).toBe('true');
    const reason = q(`#rcp-col-${InternalBrief}-reason`)!;
    expect(text(reason)).toBe('Not checked under Documents');
    expect(brief.getAttribute('aria-describedby')).toContain(`rcp-col-${InternalBrief}-reason`);
    expect(button('all', InternalBrief).getAttribute('aria-disabled')).toBe('true');
    expect(button('none', InternalBrief).getAttribute('aria-describedby')).toBe(`rcp-col-${InternalBrief}-reason`);

    // Inert: the click changes nothing and emits nothing.
    brief.click();
    button('none', InternalBrief).click();
    button('all', InternalBrief).click();
    fixture.detectChanges();
    expect(brief.checked).toBeTrue();
    expect(emitted).toEqual([]);
  });

  it('defaults to the first document being written', () => {
    fixture.componentRef.setInput('enabledAudiences', [TechnicalReport]);
    fixture.detectChanges();
    expect(selectedTab()).toBe(String(TechnicalReport));
    expect(q('[role="tabpanel"]')!.id).toBe(`rcp-panel-${TechnicalReport}`);
  });

  it('lists a figure the comparison cannot draw, unchecked and aria-disabled, with the reason', () => {
    fixture.componentRef.setInput('available', ALL_KEYS.filter(key => key !== 'p2-profile' && key !== 's3-speed-cost'));
    fixture.componentRef.setInput('unavailableReasons', { 's3-speed-cost': 'no cost measured' });
    fixture.detectChanges();
    select(TechnicalReport);

    const profile = cell(TechnicalReport, 'p2-profile');
    expect(profile.getAttribute('aria-disabled')).toBe('true');
    expect(profile.checked).toBeFalse();
    expect(text(q('#rcp-row-p2-profile-reason'))).toBe('needs three or more models');
    expect(profile.getAttribute('aria-describedby')).toContain('rcp-row-p2-profile-reason');
    expect(text(q('#rcp-row-s3-speed-cost-reason'))).toBe('no cost measured');
    expect(text(tab(TechnicalReport).querySelector('.visually-hidden'))).toBe(', 5 of 5 charts');

    profile.click();
    fixture.detectChanges();
    expect(profile.checked).toBeFalse();
    expect(emitted).toEqual([]);

    // All takes only the figures the comparison can draw.
    button('all', TechnicalReport).click();
    fixture.detectChanges();
    expect(emitted[0][TechnicalReport]).toEqual(['p1a-quality', 'p1b-speed', 'p1c-cost', 's1-quality-speed', 's2-quality-cost']);
  });

  it('shows one document type without a tab row', () => {
    fixture.componentRef.setInput('audiences', [InternalBrief]);
    fixture.detectChanges();

    expect(q('[role="tablist"]')).toBeNull();
    expect(q('[role="tab"]')).toBeNull();
    const panel = q('.rcp-doc-panel')!;
    expect(panel.getAttribute('role')).toBe('group');
    expect(panel.hasAttribute('tabindex')).toBeFalse();
    const title = q(`#${panel.getAttribute('aria-labelledby')}`)!;
    expect(text(title)).toBe('Internal Improvement Brief');
    expect(panel.querySelectorAll('input[type="checkbox"]').length).toBe(7);
    expect(cell(InternalBrief, 'p1a-quality').checked).toBeTrue();
  });

  it('falls back when the active document leaves', () => {
    select(InternalBrief);
    expect(selectedTab()).toBe(String(InternalBrief));

    fixture.componentRef.setInput('audiences', [ExecutiveSummary, TechnicalReport]);
    fixture.detectChanges();
    expect(selectedTab()).toBe(String(ExecutiveSummary));
    expect(q('[role="tabpanel"]')!.id).toBe(`rcp-panel-${ExecutiveSummary}`);
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
    select(InternalBrief);
    button('none', InternalBrief).click();
    fixture.detectChanges();
    expect(setItem).not.toHaveBeenCalled();
    expect(getItem).not.toHaveBeenCalled();
    expect(localStorage.getItem(REPORT_CHART_STORAGE_KEY)).toBeNull();
  });

  it('keeps the caption for assistive technology alone when told to', () => {
    fixture.componentRef.setInput('captionVisible', false);
    fixture.detectChanges();
    expect(q('.rcp-caption')!.classList).toContain('visually-hidden');
  });
});
