import { ComponentFixture, TestBed } from '@angular/core/testing';

import { BenchmarkReportAudience } from '../../../services/admin-benchmark.service';
import {
  REPORT_CHART_COLUMN_DISABLED_REASON,
  REPORT_CHART_LAYOUT_FIELDS,
  REPORT_CHART_SERVER_DRAWN_NOTE,
  REPORT_PACK_PICKER_FIGURES,
  ReportChartPickerAudienceOption,
  ReportChartPickerComponent,
  ReportChartPickerFigure
} from './report-chart-picker.component';
import { COMPARISON_REPORT_AUDIENCE_OPTIONS, MODEL_REPORT_AUDIENCE_OPTIONS } from './report-document-format';
import {
  DEFAULT_CHART_LAYOUT_SETTINGS,
  DEFAULT_CHART_SELECTION,
  DEFAULT_DOCUMENT_CHART_LAYOUT,
  REPORT_CHART_FIGURES,
  REPORT_CHART_NOT_DRAWN,
  REPORT_CHART_STORAGE_KEY,
  ReportChartLayoutSettings,
  ReportChartSelection,
  documentChartRefusal
} from './report-charts';

const { ExecutiveSummary, RootCauseAnalysis } = BenchmarkReportAudience;
const ALL_KEYS = REPORT_CHART_FIGURES.map(figure => figure.key);
/** The browser-drawn figures the Root Cause Analysis places, in placement order. */
const RCA_KEYS = ['p1a-quality', 'p1b-speed', 'p1c-cost', 's1-quality-speed', 's2-quality-cost'];

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
    expect(tabs.map(t => text(t.querySelector('.rcp-tab-name')))).toEqual(['Executive', 'Root Cause']);
    expect(tabs.map(t => t.getAttribute('aria-selected'))).toEqual(['true', 'false']);
    expect(tabs.map(t => t.getAttribute('tabindex'))).toEqual(['0', '-1']);
    expect(tabs[0].getAttribute('aria-controls')).toBe(`rcp-panel-${ExecutiveSummary}`);

    const panel = q('[role="tabpanel"]')!;
    expect(panel.id).toBe(`rcp-panel-${ExecutiveSummary}`);
    expect(panel.getAttribute('aria-labelledby')).toBe(`rcp-tab-${ExecutiveSummary}`);
    expect(panel.getAttribute('tabindex')).toBe('0');
    expect(text(panel.querySelector('.rcp-panel-title'))).toBe('Executive Summary');

    const boxes = Array.from(panel.querySelectorAll<HTMLInputElement>('input[type="checkbox"]'));
    expect(boxes.length).toBe(8);
    expect(boxes.every(box => box.id.startsWith(`rcp-${ExecutiveSummary}-`))).toBe(true);
    expect(host.querySelectorAll('input[type="checkbox"]').length).toBe(8);
    const titles = Array.from(panel.querySelectorAll('.rcp-figure-title')).map(title => text(title));
    expect(titles).toEqual(REPORT_CHART_FIGURES.map(figure => figure.title));
  });

  it('names every checkbox for its figure and document, with the target section beneath it', () => {
    const exec = cell(ExecutiveSummary, 'p1a-quality');
    expect(exec.getAttribute('aria-label')).toBe('Include Intelligence in the Executive Summary');
    const placement = q(`#${exec.getAttribute('aria-describedby')}`)!;
    expect(text(placement)).toBe('How it compares');

    select(RootCauseAnalysis);
    expect(cell(RootCauseAnalysis, 's1-quality-speed').getAttribute('aria-label'))
      .toBe('Include Intelligence against speed in the Root Cause Analysis');
    expect(text(q(`#rcp-${RootCauseAnalysis}-p1a-quality-placement`))).toBe('Evidence: intelligence');
    expect(cell(RootCauseAnalysis, 'p1c-cost').getAttribute('aria-label')).toBe('Include Cost in the Root Cause Analysis');
    expect(text(q(`#rcp-${RootCauseAnalysis}-p2-profile-placement`))).toBe(REPORT_CHART_NOT_DRAWN);
  });

  it('shows the default selection', () => {
    expect(checkedIn(ExecutiveSummary)).toEqual(['p1a-quality', 's2-quality-cost']);
    expect(checkedIn(RootCauseAnalysis)).toEqual(RCA_KEYS);
  });

  it('notes the Root Cause Analysis\' four server-drawn charts under its figures, and nowhere else', () => {
    expect(q('.rcp-server-note')).toBeNull();

    select(RootCauseAnalysis);
    expect(text(q('.rcp-server-note'))).toBe(REPORT_CHART_SERVER_DRAWN_NOTE);
    expect(REPORT_CHART_SERVER_DRAWN_NOTE).toBe('The Root Cause Analysis also carries four charts drawn by Overseer: where the points '
      + 'were lost, where the time goes, where the cost goes, and how sure each hypothesis is.');
    // The server-drawn charts are never listed as figures to choose.
    expect(host.querySelector('[data-figure^="r1-"], [data-figure^="r4-"]')).toBeNull();
  });

  it('emits the normalized selection when a checkbox changes', () => {
    cell(ExecutiveSummary, 'p1c-cost').click();
    fixture.detectChanges();

    expect(emitted.length).toBe(1);
    expect(emitted[0][ExecutiveSummary]).toEqual(['p1a-quality', 'p1c-cost', 's2-quality-cost']);
    expect(emitted[0][RootCauseAnalysis]).toEqual(RCA_KEYS);

    cell(ExecutiveSummary, 'p1a-quality').click();
    fixture.detectChanges();
    expect(emitted[1][ExecutiveSummary]).toEqual(['p1c-cost', 's2-quality-cost']);
    expect(checkedIn(ExecutiveSummary)).toEqual(['p1c-cost', 's2-quality-cost']);
  });

  it('offers All and None per document, each named for its document', () => {
    expect(text(button('all', ExecutiveSummary))).toBe('All charts in the Executive Summary');

    select(RootCauseAnalysis);
    const none = button('none', RootCauseAnalysis);
    expect(text(none)).toBe('None of the charts in the Root Cause Analysis');
    none.click();
    fixture.detectChanges();
    expect(emitted[0][RootCauseAnalysis]).toEqual([]);
    expect(checkedIn(RootCauseAnalysis)).toEqual([]);

    select(ExecutiveSummary);
    button('all', ExecutiveSummary).click();
    fixture.detectChanges();
    expect(emitted[1][ExecutiveSummary]).toEqual(ALL_KEYS);
    expect(checkedIn(ExecutiveSummary)).toEqual(ALL_KEYS);
  });

  it('switches documents by click and by the keyboard', () => {
    select(RootCauseAnalysis);
    expect(selectedTab()).toBe(String(RootCauseAnalysis));
    expect(q('[role="tabpanel"]')!.id).toBe(`rcp-panel-${RootCauseAnalysis}`);
    expect(text(q('.rcp-panel-title'))).toBe('Root Cause Analysis');
    expect(tab(RootCauseAnalysis).getAttribute('tabindex')).toBe('0');
    expect(tab(ExecutiveSummary).getAttribute('tabindex')).toBe('-1');

    const steps: [string, BenchmarkReportAudience][] = [
      ['ArrowRight', ExecutiveSummary],
      ['ArrowLeft', RootCauseAnalysis],
      ['Home', ExecutiveSummary],
      ['End', RootCauseAnalysis],
      ['ArrowLeft', ExecutiveSummary],
      ['ArrowRight', RootCauseAnalysis]
    ];
    tab(RootCauseAnalysis).focus();
    for (const [key, expected] of steps) {
      const event = press(document.activeElement as HTMLElement, key);
      expect(event.defaultPrevented, key).toBe(true);
      expect(selectedTab(), key).toBe(String(expected));
      expect(document.activeElement, key).toBe(tab(expected));
      expect(q('[role="tabpanel"]')!.id, key).toBe(`rcp-panel-${expected}`);
    }

    const other = press(tab(RootCauseAnalysis), 'Enter');
    expect(other.defaultPrevented).toBe(false);
    expect(selectedTab()).toBe(String(RootCauseAnalysis));
    expect(emitted).toEqual([]);
  });

  it('counts the selection on each segment', () => {
    const counts = (): string[] => Array.from(host.querySelectorAll('.rcp-tab-count')).map(count => text(count));
    expect(counts()).toEqual(['2', '5']);
    expect(q('.rcp-tab-count')!.getAttribute('aria-hidden')).toBe('true');
    expect(text(tab(ExecutiveSummary).querySelector('.visually-hidden'))).toBe(', 2 of 8 charts');
    expect(text(tab(RootCauseAnalysis).querySelector('.visually-hidden'))).toBe(', 5 of 5 charts');

    cell(ExecutiveSummary, 'p1c-cost').click();
    fixture.detectChanges();
    expect(counts()).toEqual(['3', '5']);
    expect(text(tab(ExecutiveSummary).querySelector('.visually-hidden'))).toBe(', 3 of 8 charts');
  });

  it('keeps an unchecked document selectable but inert, with the reason', () => {
    fixture.componentRef.setInput('enabledAudiences', [ExecutiveSummary]);
    fixture.detectChanges();

    const briefTab = tab(RootCauseAnalysis);
    expect(briefTab.classList).toContain('is-unchecked');
    expect(text(briefTab.querySelector('.rcp-tab-count'))).toBe('—');
    expect(text(briefTab.querySelector('.visually-hidden'))).toBe(', not checked under Documents of this comparison');
    expect(selectedTab()).toBe(String(ExecutiveSummary));
    expect(cell(ExecutiveSummary, 'p1a-quality').hasAttribute('aria-disabled')).toBe(false);
    expect(button('all', ExecutiveSummary).hasAttribute('aria-disabled')).toBe(false);
    expect(q(`#rcp-col-${ExecutiveSummary}-reason`)).toBeNull();

    select(RootCauseAnalysis);
    const brief = cell(RootCauseAnalysis, 'p1a-quality');
    expect(brief.disabled).toBe(false);
    expect(brief.getAttribute('aria-disabled')).toBe('true');
    const reason = q(`#rcp-col-${RootCauseAnalysis}-reason`)!;
    expect(text(reason)).toBe('Not checked under Documents of this comparison');
    expect(brief.getAttribute('aria-describedby')).toContain(`rcp-col-${RootCauseAnalysis}-reason`);
    expect(button('all', RootCauseAnalysis).getAttribute('aria-disabled')).toBe('true');
    expect(button('none', RootCauseAnalysis).getAttribute('aria-describedby')).toBe(`rcp-col-${RootCauseAnalysis}-reason`);

    // Inert: the click changes nothing and emits nothing.
    brief.click();
    button('none', RootCauseAnalysis).click();
    button('all', RootCauseAnalysis).click();
    fixture.detectChanges();
    expect(brief.checked).toBe(true);
    expect(emitted).toEqual([]);
  });

  it('defaults to the first document being written', () => {
    fixture.componentRef.setInput('enabledAudiences', [RootCauseAnalysis]);
    fixture.detectChanges();
    expect(selectedTab()).toBe(String(RootCauseAnalysis));
    expect(q('[role="tabpanel"]')!.id).toBe(`rcp-panel-${RootCauseAnalysis}`);
  });

  it('lists a figure the comparison cannot draw, unchecked and aria-disabled, with the reason', () => {
    fixture.componentRef.setInput('available', ALL_KEYS.filter(key => key !== 'p2-profile' && key !== 's3-speed-cost'));
    fixture.componentRef.setInput('unavailableReasons', { 's3-speed-cost': 'no cost measured' });
    fixture.detectChanges();
    select(ExecutiveSummary);

    const profile = cell(ExecutiveSummary, 'p2-profile');
    expect(profile.getAttribute('aria-disabled')).toBe('true');
    expect(profile.checked).toBe(false);
    expect(text(q('#rcp-row-p2-profile-reason'))).toBe('needs three or more models');
    expect(profile.getAttribute('aria-describedby')).toContain('rcp-row-p2-profile-reason');
    expect(text(q('#rcp-row-s3-speed-cost-reason'))).toBe('no cost measured');
    expect(text(tab(ExecutiveSummary).querySelector('.visually-hidden'))).toBe(', 2 of 6 charts');

    profile.click();
    fixture.detectChanges();
    expect(profile.checked).toBe(false);
    expect(emitted).toEqual([]);

    // All takes only the figures the comparison can draw.
    button('all', ExecutiveSummary).click();
    fixture.detectChanges();
    expect(emitted[0][ExecutiveSummary]).toEqual(['p1a-quality', 'p1b-speed', 'p1c-cost', 's1-quality-speed', 's2-quality-cost', 'a1-availability']);
  });

  it('shows one document type without a tab row', () => {
    fixture.componentRef.setInput('audiences', [RootCauseAnalysis]);
    fixture.detectChanges();

    expect(q('[role="tablist"]')).toBeNull();
    expect(q('[role="tab"]')).toBeNull();
    const panel = q('.rcp-doc-panel')!;
    expect(panel.getAttribute('role')).toBe('group');
    expect(panel.hasAttribute('tabindex')).toBe(false);
    const title = q(`#${panel.getAttribute('aria-labelledby')}`)!;
    expect(text(title)).toBe('Root Cause Analysis');
    expect(panel.querySelectorAll('input[type="checkbox"]').length).toBe(8);
    expect(cell(RootCauseAnalysis, 'p1a-quality').checked).toBe(true);
  });

  it('falls back when the active document leaves', () => {
    select(RootCauseAnalysis);
    expect(selectedTab()).toBe(String(RootCauseAnalysis));

    fixture.componentRef.setInput('audiences', [ExecutiveSummary]);
    fixture.detectChanges();
    expect(q('.rcp-doc-panel')!.id).toBe(`rcp-panel-${ExecutiveSummary}`);
  });

  it('takes a new selection from its host', () => {
    fixture.componentRef.setInput('selection', { [ExecutiveSummary]: ['p1b-speed'] });
    fixture.detectChanges();
    expect(checkedIn(ExecutiveSummary)).toEqual(['p1b-speed']);
    expect(checkedIn(RootCauseAnalysis)).toEqual([]);
  });

  it('never touches localStorage; the host stores the selection', () => {
    const setItem = vi.spyOn(localStorage, 'setItem');
    const getItem = vi.spyOn(localStorage, 'getItem');
    cell(ExecutiveSummary, 'p1c-cost').click();
    select(RootCauseAnalysis);
    button('none', RootCauseAnalysis).click();
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

  it('shows no width and no Layout without a layout', () => {
    expect(q('.rcp-width-select')).toBeNull();
    expect(q('details.rcp-layout')).toBeNull();
  });

  it('says which document type is shown when the admin switches', () => {
    const shown: BenchmarkReportAudience[] = [];
    fixture.componentInstance.activeAudienceChange.subscribe(audience => shown.push(audience));
    select(RootCauseAnalysis);
    select(RootCauseAnalysis);
    select(ExecutiveSummary);
    expect(shown).toEqual([RootCauseAnalysis, ExecutiveSummary]);
  });

  it('shows the comparison-scope sections and document types when told the scope', () => {
    fixture.componentRef.setInput('scope', 'comparison');
    fixture.detectChanges();
    expect(fixture.componentInstance.audienceOptions).toBe(COMPARISON_REPORT_AUDIENCE_OPTIONS);
    expect(text(q(`#rcp-${ExecutiveSummary}-p1a-quality-placement`))).toBe('How they compare');
    select(RootCauseAnalysis);
    // The comparison-scope Root Cause Analysis places no Intelligence bars: neither offered nor selected.
    expect(text(q(`#rcp-${RootCauseAnalysis}-p1a-quality-placement`))).toBe(REPORT_CHART_NOT_DRAWN);
    expect(cell(RootCauseAnalysis, 'p1a-quality').checked).toBe(false);
    expect(cell(RootCauseAnalysis, 'p1a-quality').getAttribute('aria-disabled')).toBe('true');
    expect(text(q(`#rcp-${RootCauseAnalysis}-p1b-speed-placement`))).toBe('Evidence: speed');
    expect(text(q(`#rcp-${RootCauseAnalysis}-s2-quality-cost-placement`))).toBe('Evidence: cost');
    expect(text(q(`#rcp-${RootCauseAnalysis}-a1-availability-placement`))).toBe(REPORT_CHART_NOT_DRAWN);
    expect(checkedIn(RootCauseAnalysis)).toEqual(RCA_KEYS.filter(key => key !== 'p1a-quality'));
  });

  it('never offers or selects A1 in a comparison-scope Executive Summary, which does not place it', () => {
    fixture.componentRef.setInput('scope', 'comparison');
    fixture.componentRef.setInput('selection', { ...DEFAULT_CHART_SELECTION, [ExecutiveSummary]: ['p1a-quality', 'a1-availability'] });
    fixture.detectChanges();

    const a1 = cell(ExecutiveSummary, 'a1-availability');
    expect(a1.checked).toBe(false);
    expect(a1.getAttribute('aria-disabled')).toBe('true');
    expect(text(q(`#rcp-${ExecutiveSummary}-a1-availability-placement`))).toBe(REPORT_CHART_NOT_DRAWN);
    expect(text(tab(ExecutiveSummary).querySelector('.visually-hidden'))).toBe(', 1 of 7 charts');

    a1.click();
    fixture.detectChanges();
    expect(emitted).toEqual([]);

    select(RootCauseAnalysis);
    expect(cell(RootCauseAnalysis, 's1-quality-speed').checked).toBe(true);
    expect(cell(RootCauseAnalysis, 's1-quality-speed').getAttribute('aria-disabled')).toBeNull();
    // Neither does the Root Cause Analysis place A1, the profile or speed against cost.
    for (const key of ['a1-availability', 'p2-profile', 's3-speed-cost']) {
      expect(cell(RootCauseAnalysis, key).getAttribute('aria-disabled'), key).toBe('true');
    }
  });

  it('gives A1 its own reason when the comparison cannot draw it', () => {
    fixture.componentRef.setInput('available', ALL_KEYS.filter(key => key !== 'a1-availability'));
    fixture.detectChanges();
    expect(text(q('#rcp-row-a1-availability-reason'))).toBe('needs two or more models, one with provider errors recorded');
  });

  describe('with a layout', () => {
    let layouts: ReportChartLayoutSettings[];

    beforeEach(() => {
      layouts = [];
      fixture.componentInstance.layoutChange.subscribe(layout => layouts.push(layout));
      fixture.componentRef.setInput('layout', DEFAULT_CHART_LAYOUT_SETTINGS);
      fixture.detectChanges();
    });

    const width = (audience: BenchmarkReportAudience, key: string): HTMLSelectElement =>
      q<HTMLSelectElement>(`#rcp-${audience}-${key}-width`)!;
    const field = <T extends HTMLElement>(audience: BenchmarkReportAudience, name: string): T =>
      q<T>(`#rcp-${audience}-${name}`)!;
    const choose = (element: HTMLSelectElement, value: string): void => {
      element.value = value;
      element.dispatchEvent(new Event('change'));
      fixture.detectChanges();
    };
    const option = (element: HTMLSelectElement, value: string): HTMLOptionElement =>
      Array.from(element.options).find(candidate => candidate.value === value)!;

    it('gives every figure a width, named for its figure and document, full column by default', () => {
      const intelligence = width(ExecutiveSummary, 'p1a-quality');
      expect(intelligence.getAttribute('aria-label')).toBe('Width of Intelligence in the Executive Summary');
      expect(intelligence.value).toBe('full');
      expect(Array.from(intelligence.options).map(item => item.value)).toEqual(['full', 'twoThirds', 'half']);
      expect(host.querySelectorAll('.rcp-width-select').length).toBe(8);
    });

    /** The Executive Summary's labels at 9 pt, where half width does not fit. */
    const atNinePoints = (): void => {
      fixture.componentRef.setInput('layout', {
        ...DEFAULT_CHART_LAYOUT_SETTINGS, [ExecutiveSummary]: { ...DEFAULT_DOCUMENT_CHART_LAYOUT, labelPt: 9 }
      });
      fixture.detectChanges();
    };

    it('offers every width at 8 pt', () => {
      const intelligence = width(ExecutiveSummary, 'p1a-quality');
      expect(Array.from(intelligence.options).map(item => item.hasAttribute('aria-disabled'))).toEqual([false, false, false]);
      expect(intelligence.hasAttribute('aria-describedby')).toBe(false);
      expect(q('.rcp-refusals')).toBeNull();

      choose(intelligence, 'half');
      expect(layouts[0][ExecutiveSummary]).toEqual({ ...DEFAULT_DOCUMENT_CHART_LAYOUT, widths: { 'p1a-quality': 'half' } });
    });

    it('offers a refused width aria-disabled, never disabled, with its reason in the Layout disclosure', () => {
      atNinePoints();
      const intelligence = width(ExecutiveSummary, 'p1a-quality');
      const half = option(intelligence, 'half');
      expect(half.disabled).toBe(false);
      expect(half.getAttribute('aria-disabled')).toBe('true');
      expect(text(half)).toBe('Half column (does not fit)');
      expect(option(intelligence, 'twoThirds').hasAttribute('aria-disabled')).toBe(false);

      const reasonId = `rcp-${ExecutiveSummary}-width-half-reason`;
      expect(intelligence.getAttribute('aria-describedby')).toBe(reasonId);
      expect(text(q(`#${reasonId}`))).toBe(`Half column: ${documentChartRefusal('half', 9)}`);
    });

    it('sets each width select beside its figure in a wide picker and under the figure title in a narrow one', () => {
      const rows = Array.from(host.querySelectorAll<HTMLElement>('.rcp-row'));
      expect(rows.length).toBe(8);
      expect(q('.rcp-width-label')).toBeNull();

      host.style.inlineSize = '24rem';
      for (const row of rows) {
        const select = row.querySelector<HTMLSelectElement>('.rcp-width-select')!;
        const title = row.querySelector<HTMLElement>('.rcp-figure-title')!;
        expect(getComputedStyle(select).fontSize, row.dataset['figure']).toBe(getComputedStyle(title).fontSize);
        const box = select.getBoundingClientRect();
        const cellBox = row.querySelector<HTMLElement>('.rcp-cell')!.getBoundingClientRect();
        const middle = (box.top + box.bottom) / 2;
        expect(Math.abs(box.right - row.getBoundingClientRect().right), row.dataset['figure']).toBeLessThanOrEqual(1);
        expect(middle, row.dataset['figure']).toBeGreaterThanOrEqual(cellBox.top);
        expect(middle, row.dataset['figure']).toBeLessThanOrEqual(cellBox.bottom);
      }

      host.style.inlineSize = '17rem';
      for (const row of rows) {
        const box = row.querySelector<HTMLSelectElement>('.rcp-width-select')!.getBoundingClientRect();
        const cellBox = row.querySelector<HTMLElement>('.rcp-cell')!.getBoundingClientRect();
        const title = row.querySelector<HTMLElement>('.rcp-figure-title')!.getBoundingClientRect();
        expect(box.top, row.dataset['figure']).toBeGreaterThanOrEqual(cellBox.bottom);
        expect(Math.abs(box.left - title.left), row.dataset['figure']).toBeLessThanOrEqual(1);
      }
    });

    it('centers a width select on its row\'s checkbox and keeps it within the row\'s top, under a multi-line cell', () => {
      fixture.componentRef.setInput('notes', { 'p1a-quality': 'P1 was not computable in this analysis.' });
      fixture.detectChanges();
      host.style.inlineSize = '24rem';

      const row = q(`.rcp-row[data-figure="p1a-quality"]`)!;
      expect(row.querySelector('.rcp-note')).not.toBeNull();
      const select = row.querySelector<HTMLSelectElement>('.rcp-width-select')!.getBoundingClientRect();
      const checkbox = row.querySelector<HTMLInputElement>('input[type="checkbox"]')!.getBoundingClientRect();
      const cellBox = row.querySelector<HTMLElement>('.rcp-cell')!.getBoundingClientRect();
      expect(cellBox.height).toBeGreaterThan(select.height);
      const selectCenter = (select.top + select.bottom) / 2;
      const checkboxCenter = (checkbox.top + checkbox.bottom) / 2;
      expect(Math.abs(selectCenter - checkboxCenter)).toBeLessThanOrEqual(1);
      expect(select.top).toBeGreaterThanOrEqual(row.getBoundingClientRect().top - 0.5);
    });

    it('keeps the width and says why when a refused width is chosen, and emits a width that fits', () => {
      atNinePoints();
      const intelligence = width(ExecutiveSummary, 'p1a-quality');
      choose(intelligence, 'half');
      expect(intelligence.value).toBe('full');
      expect(layouts).toEqual([]);
      const refused = q(`#rcp-${ExecutiveSummary}-p1a-quality-width-refused`)!;
      expect(refused.getAttribute('role')).toBe('status');
      expect(text(refused)).toBe(documentChartRefusal('half', 9)!);
      expect(intelligence.getAttribute('aria-describedby')).toContain(`rcp-${ExecutiveSummary}-p1a-quality-width-refused`);

      choose(width(ExecutiveSummary, 'p1a-quality'), 'twoThirds');
      expect(layouts.length).toBe(1);
      expect(layouts[0][ExecutiveSummary]).toEqual({ ...DEFAULT_DOCUMENT_CHART_LAYOUT, labelPt: 9, widths: { 'p1a-quality': 'twoThirds' } });
      expect(layouts[0][RootCauseAnalysis]).toEqual(DEFAULT_DOCUMENT_CHART_LAYOUT);
      expect(q(`#rcp-${ExecutiveSummary}-p1a-quality-width-refused`)).toBeNull();
    });

    it('holds every layout field of the document in a closed Layout disclosure, at the defaults', () => {
      const disclosure = q<HTMLDetailsElement>('details.rcp-layout')!;
      expect(disclosure.classList).toContain('gh-disclosure');
      expect(disclosure.open).toBe(false);
      expect(text(disclosure.querySelector('summary'))).toBe('Layout of the charts in the Executive Summary');

      const labelOf = (id: string): string => text(q(`label[for="${id}"]`));
      expect(labelOf(`rcp-${ExecutiveSummary}-orientation`)).toBe('Bar orientation');
      expect(field<HTMLSelectElement>(ExecutiveSummary, 'orientation').value).toBe('asInStep2');
      expect(Array.from(field<HTMLSelectElement>(ExecutiveSummary, 'orientation').options).map(item => text(item)))
        .toEqual(['As in step 2', 'Vertical', 'Horizontal']);
      expect(field<HTMLInputElement>(ExecutiveSummary, 'sideBySide').checked).toBe(true);
      expect(labelOf(`rcp-${ExecutiveSummary}-label`)).toBe('Label size');
      expect(field<HTMLSelectElement>(ExecutiveSummary, 'label').value).toBe('8');
      expect(Array.from(field<HTMLSelectElement>(ExecutiveSummary, 'label').options).map(item => text(item)))
        .toEqual(['7 pt', '7.5 pt', '8 pt', '8.5 pt', '9 pt', '10 pt']);
      expect(field<HTMLSelectElement>(ExecutiveSummary, 'maxHeight').value).toBe('60');
      expect(Array.from(field<HTMLSelectElement>(ExecutiveSummary, 'maxHeight').options).map(item => text(item)))
        .toEqual(['40% of the page', '50% of the page', '60% of the page']);
      expect(labelOf(`rcp-${ExecutiveSummary}-heading`)).toBe('Heading inside the chart');
      expect(field<HTMLSelectElement>(ExecutiveSummary, 'heading').value).toBe('none');
      expect(text(option(field<HTMLSelectElement>(ExecutiveSummary, 'heading'), 'none'))).toBe('None — the caption names it');
      expect(field<HTMLInputElement>(ExecutiveSummary, 'logo').checked).toBe(false);
      expect(field<HTMLSelectElement>(ExecutiveSummary, 'theme').value).toBe('lightPrint');
      expect(text(option(field<HTMLSelectElement>(ExecutiveSummary, 'theme'), 'lightPrint'))).toBe('Light, for print');
    });

    it('emits each field\'s change for the document shown alone', () => {
      select(RootCauseAnalysis);
      choose(field<HTMLSelectElement>(RootCauseAnalysis, 'orientation'), 'vertical');
      choose(field<HTMLSelectElement>(RootCauseAnalysis, 'maxHeight'), '40');
      choose(field<HTMLSelectElement>(RootCauseAnalysis, 'heading'), 'titleAndBadges');
      choose(field<HTMLSelectElement>(RootCauseAnalysis, 'theme'), 'asInStep2');
      choose(field<HTMLSelectElement>(RootCauseAnalysis, 'label'), '10');
      field<HTMLInputElement>(RootCauseAnalysis, 'logo').click();
      field<HTMLInputElement>(RootCauseAnalysis, 'sideBySide').click();
      fixture.detectChanges();

      expect(layouts.length).toBe(7);
      expect(layouts[6][RootCauseAnalysis]).toEqual({
        ...DEFAULT_DOCUMENT_CHART_LAYOUT,
        orientation: 'vertical', maxHeightPercent: 40, heading: 'titleAndBadges', theme: 'asInStep2', labelPt: 10,
        logo: true, sideBySide: false
      });
      expect(layouts[6][ExecutiveSummary]).toEqual(DEFAULT_DOCUMENT_CHART_LAYOUT);
    });

    it('refuses a label size too large for a selected figure\'s width, aria-disabled with its reason', () => {
      choose(width(ExecutiveSummary, 'p1a-quality'), 'half');
      const label = field<HTMLSelectElement>(ExecutiveSummary, 'label');
      expect(['7', '7.5', '8', '8.5'].map(value => option(label, value).hasAttribute('aria-disabled'))).toEqual([false, false, false, false]);
      expect(option(label, '9').getAttribute('aria-disabled')).toBe('true');
      expect(text(option(label, '9'))).toBe('9 pt (does not fit)');
      expect(option(label, '10').disabled).toBe(false);
      const reasonId = `rcp-${ExecutiveSummary}-label-reason`;
      expect(label.getAttribute('aria-describedby')).toBe(reasonId);
      expect(text(q(`#${reasonId}`))).toBe(`9, 10 pt: ${documentChartRefusal('half', 9)}`);

      choose(label, '9');
      expect(label.value).toBe('8');
      expect(layouts.length).toBe(1);
      expect(text(q(`#rcp-${ExecutiveSummary}-label-refused`))).toBe(documentChartRefusal('half', 9)!);

      choose(label, '8.5');
      expect(layouts[1][ExecutiveSummary]!.labelPt).toBe(8.5);
    });

    it('never touches localStorage for the layout either', () => {
      const setItem = vi.spyOn(localStorage, 'setItem');
      choose(width(ExecutiveSummary, 's2-quality-cost'), 'twoThirds');
      expect(setItem).not.toHaveBeenCalled();
    });
  });

  it('keeps 1 rem above the Layout disclosure', () => {
    fixture.componentRef.setInput('layout', DEFAULT_CHART_LAYOUT_SETTINGS);
    fixture.detectChanges();
    const details = q('details.rcp-layout')!;
    expect(getComputedStyle(details).marginBlockStart).toBe('16px');
    expect(details.querySelector('.rcp-layout-actions')).toBeNull();
  });

  it('defaults to the Report Pack\'s figures, document types, reason and every layout field, with no notes', () => {
    const picker = fixture.componentInstance;
    expect(picker.figures).toBe(REPORT_PACK_PICKER_FIGURES);
    expect(picker.figures.map(figure => figure.key)).toEqual(ALL_KEYS);
    expect(picker.audienceOptions).toBe(MODEL_REPORT_AUDIENCE_OPTIONS);
    expect(picker.audiences).toEqual([ExecutiveSummary, RootCauseAnalysis]);
    expect(picker.columnDisabledReason).toBe(REPORT_CHART_COLUMN_DISABLED_REASON);
    expect(picker.layoutFields).toEqual(REPORT_CHART_LAYOUT_FIELDS);
    expect(picker.notes).toEqual({});
    expect(q('.rcp-note')).toBeNull();
  });

  describe('with figures and document types of its own', () => {
    const { ProviderIssueReport } = BenchmarkReportAudience;
    const FIGURES: ReportChartPickerFigure[] = [
      { key: 'x1', title: 'First figure', placement: 'Results' },
      { key: 'x2', title: 'Second figure', placement: { [ExecutiveSummary]: 'Results', [ProviderIssueReport]: 'Events' } }
    ];
    const AUDIENCES: ReportChartPickerAudienceOption[] = [
      { audience: ExecutiveSummary, label: 'Executive Summary', shortLabel: 'Executive' },
      { audience: ProviderIssueReport, label: 'Provider Issue Report', shortLabel: 'Provider' }
    ];
    let own: ComponentFixture<ReportChartPickerComponent>;
    let ownHost: HTMLElement;
    let ownSelections: ReportChartSelection<string>[];
    let ownLayouts: ReportChartLayoutSettings<string>[];

    beforeEach(() => {
      own = TestBed.createComponent(ReportChartPickerComponent);
      ownHost = own.nativeElement as HTMLElement;
      ownSelections = [];
      ownLayouts = [];
      own.componentInstance.selectionChange.subscribe(selection => ownSelections.push(selection));
      own.componentInstance.layoutChange.subscribe(layout => ownLayouts.push(layout));
      own.componentRef.setInput('idPrefix', 'own');
      own.componentRef.setInput('selection', {
        [ExecutiveSummary]: ['x2', 'p1a-quality', 'x1', 'x2'], [ProviderIssueReport]: ['x2'], [RootCauseAnalysis]: ['x1']
      });
      own.componentRef.setInput('figures', FIGURES);
      own.componentRef.setInput('audienceOptions', AUDIENCES);
      own.detectChanges();
    });

    afterEach(() => own.destroy());

    const ownQ = <T extends HTMLElement = HTMLElement>(selector: string): T | null => ownHost.querySelector<T>(selector);
    const ownSelect = (audience: BenchmarkReportAudience): void => {
      ownQ<HTMLButtonElement>(`#own-tab-${audience}`)!.click();
      own.detectChanges();
    };
    const ownChoose = (element: HTMLSelectElement, value: string): void => {
      element.value = value;
      element.dispatchEvent(new Event('change'));
      own.detectChanges();
    };

    it('lists its figures for its document types, with each figure\'s section per document type, every figure drawable', () => {
      const tabs = Array.from(ownHost.querySelectorAll<HTMLElement>('[role="tab"]'));
      expect(tabs.map(t => text(t.querySelector('.rcp-tab-name')))).toEqual(['Executive', 'Provider']);
      expect(tabs.map(t => text(t.querySelector('.rcp-tab-count')))).toEqual(['2', '1']);
      expect(Array.from(ownHost.querySelectorAll('.rcp-figure-title')).map(title => text(title))).toEqual(['First figure', 'Second figure']);
      expect(Array.from(ownHost.querySelectorAll('.rcp-placement')).map(section => text(section))).toEqual(['Results', 'Results']);
      expect(ownQ('.rcp-row .rcp-reason')).toBeNull();
      expect(ownQ<HTMLInputElement>(`#own-${ExecutiveSummary}-x1`)!.getAttribute('aria-label')).toBe('Include First figure in the Executive Summary');

      ownSelect(ProviderIssueReport);
      expect(Array.from(ownHost.querySelectorAll('.rcp-placement')).map(section => text(section))).toEqual(['Results', 'Events']);
      expect(text(ownQ('.rcp-panel-title'))).toBe('Provider Issue Report');
    });

    it('keeps only its own keys and document types, in its figures\' order', () => {
      expect(own.componentInstance.selection).toEqual({ [ExecutiveSummary]: ['x1', 'x2'], [ProviderIssueReport]: ['x2'] });
      ownQ<HTMLInputElement>(`#own-${ExecutiveSummary}-x1`)!.click();
      own.detectChanges();
      expect(ownSelections).toEqual([{ [ExecutiveSummary]: ['x2'], [ProviderIssueReport]: ['x2'] }]);

      ownQ<HTMLButtonElement>(`.rcp-all[data-audience="${ExecutiveSummary}"]`)!.click();
      own.detectChanges();
      expect(ownSelections[1][ExecutiveSummary]).toEqual(['x1', 'x2']);
    });

    it('shows a note under a figure and reads it with its checkbox', () => {
      own.componentRef.setInput('notes', { x2: 'P1 was not computable in this analysis.' });
      own.detectChanges();
      const note = ownQ(`#own-${ExecutiveSummary}-x2-note`)!;
      expect(note.classList).toContain('rcp-note');
      expect(text(note)).toBe('P1 was not computable in this analysis.');
      expect(ownQ<HTMLInputElement>(`#own-${ExecutiveSummary}-x2`)!.getAttribute('aria-describedby'))
        .toBe(`own-${ExecutiveSummary}-x2-placement own-${ExecutiveSummary}-x2-note`);
      expect(ownQ(`#own-${ExecutiveSummary}-x1-note`)).toBeNull();
    });

    it('says its own reason for a document type that is not enabled', () => {
      own.componentRef.setInput('enabledAudiences', [ExecutiveSummary]);
      own.componentRef.setInput('columnDisabledReason', 'Not chosen in New reports.');
      own.detectChanges();
      expect(text(ownQ(`#own-tab-${ProviderIssueReport} .visually-hidden`))).toBe(', not chosen in New reports');
      ownSelect(ProviderIssueReport);
      expect(text(ownQ(`#own-col-${ProviderIssueReport}-reason`))).toBe('Not chosen in New reports.');
      expect(ownQ<HTMLInputElement>(`#own-${ProviderIssueReport}-x2`)!.getAttribute('aria-disabled')).toBe('true');
    });

    it('offers only the layout fields asked for, and keeps its own figures\' widths', () => {
      own.componentRef.setInput('layoutFields', ['labelPt', 'maxHeightPercent']);
      own.componentRef.setInput('layout', {
        [ExecutiveSummary]: { ...DEFAULT_DOCUMENT_CHART_LAYOUT, widths: { x1: 'half', 'p1a-quality': 'half' }, maxHeightPercent: 50 },
        [ProviderIssueReport]: { ...DEFAULT_DOCUMENT_CHART_LAYOUT, widths: { x2: 'twoThirds' } }
      });
      own.detectChanges();

      expect(ownQ<HTMLSelectElement>(`#own-${ExecutiveSummary}-x1-width`)!.value).toBe('half');
      expect(ownQ<HTMLSelectElement>(`#own-${ExecutiveSummary}-x2-width`)!.value).toBe('full');
      expect(ownQ<HTMLSelectElement>(`#own-${ExecutiveSummary}-label`)).not.toBeNull();
      expect(ownQ<HTMLSelectElement>(`#own-${ExecutiveSummary}-maxHeight`)!.value).toBe('50');
      for (const field of ['orientation', 'sideBySide', 'heading', 'logo', 'theme']) {
        expect(ownQ(`#own-${ExecutiveSummary}-${field}`), field).toBeNull();
      }

      ownChoose(ownQ<HTMLSelectElement>(`#own-${ExecutiveSummary}-maxHeight`)!, '40');
      expect(ownLayouts.length).toBe(1);
      expect(ownLayouts[0][ExecutiveSummary]).toEqual({ ...DEFAULT_DOCUMENT_CHART_LAYOUT, widths: { x1: 'half' }, maxHeightPercent: 40 });
      expect(ownLayouts[0][ProviderIssueReport]!.widths).toEqual({ x2: 'twoThirds' });
      expect(ownLayouts[0][RootCauseAnalysis]).toBeUndefined();
    });
  });
});
