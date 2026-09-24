import { ComponentFixture, TestBed } from '@angular/core/testing';

import {
  FIGURE_STYLE_PANEL_OPEN_KEY,
  FIGURE_STYLE_SECTIONS,
  FigureStylePanelComponent,
  FigureStylePanelKind
} from './figure-style-panel.component';
import { DEFAULT_FIGURE_STYLE, FigureStyle, HIDDEN_INTERVALS_NOTE } from './figure-style';
import { FRONTIER_UNCERTAINTY_NOTE, MEAN_TIME_NO_INTERVAL_NOTE } from './model-comparison-charts';
import type { CostMeasure, SpeedMeasure } from './model-comparison-charts';
import { MEASURE_NAMES, NUMBER_MEASURES, costNumberMeasure, speedNumberMeasure } from './measure-format';
import type { NumberMeasure } from './measure-format';

describe('FigureStylePanelComponent', () => {
  let fixture: ComponentFixture<FigureStylePanelComponent>;
  let emitted: FigureStyle[];

  function render(kind: FigureStylePanelKind, style: FigureStyle = DEFAULT_FIGURE_STYLE): void {
    fixture.componentRef.setInput('kind', kind);
    fixture.componentRef.setInput('figureStyle', style);
    fixture.detectChanges();
  }

  /** Feeds the last emitted style back in, the way the wizard does. */
  function acceptLast(): void {
    fixture.componentRef.setInput('figureStyle', emitted[emitted.length - 1]);
    fixture.detectChanges();
  }

  function host(): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  function control(id: string): HTMLInputElement {
    const element = host().querySelector<HTMLInputElement>(`#${id}`);
    expect(element).withContext(id).not.toBeNull();
    return element!;
  }

  function setChecked(input: HTMLInputElement, on: boolean): void {
    input.checked = on;
    input.dispatchEvent(new Event('change'));
    fixture.detectChanges();
  }

  function setRange(input: HTMLInputElement, value: number): void {
    input.value = String(value);
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();
  }

  function hintOf(input: HTMLInputElement): string {
    const id = input.getAttribute('aria-describedby');
    expect(id).withContext(input.id).not.toBeNull();
    return host().querySelector(`#${id}`)?.textContent ?? '';
  }

  function create(): void {
    fixture = TestBed.createComponent(FigureStylePanelComponent);
    emitted = [];
    fixture.componentInstance.figureStyleChange.subscribe(style => emitted.push(style));
  }

  beforeEach(async () => {
    localStorage.removeItem(FIGURE_STYLE_PANEL_OPEN_KEY);
    await TestBed.configureTestingModule({ imports: [FigureStylePanelComponent] }).compileComponents();
    create();
  });

  afterEach(() => {
    localStorage.removeItem(FIGURE_STYLE_PANEL_OPEN_KEY);
  });

  /** The section titles, in the order the panel stacks them. */
  function sectionTitles(): string[] {
    return Array.from(host().querySelectorAll('details.gh-disclosure--section > summary .gh-disclosure-summary-title'))
      .map(title => title.textContent!.trim());
  }

  function section(id: string): HTMLDetailsElement {
    const element = host().querySelector<HTMLDetailsElement>(`#${id}`);
    expect(element).withContext(id).not.toBeNull();
    return element!;
  }

  /** Opens or closes a section the way a click does: the property flips, then 	oggle fires. */
  function toggleSection(id: string): void {
    const details = section(id);
    details.open = !details.open;
    details.dispatchEvent(new Event('toggle'));
    fixture.detectChanges();
  }

  it('renders the bar set for a bar figure and not the trade-off set', () => {
    render('bar');
    expect(host().querySelector('#mc-style-bar-heading')?.textContent).toContain('Bar charts — Intelligence, Speed and Cost');
    expect(host().querySelector('#mc-style-scatter-heading')).toBeNull();
    expect(sectionTitles()).toEqual(['Heading and badges', 'Bars', 'Values and axes', 'Number format', 'Uncertainty', 'Footer', 'Layout']);
  });

  it('renders the trade-off set for a scatter and not the bar set', () => {
    render('scatter');
    expect(host().querySelector('#mc-style-scatter-heading')?.textContent?.trim()).toBe('Trade-off charts');
    expect(host().querySelector('#mc-style-bar-heading')).toBeNull();
    expect(sectionTitles()).toEqual(['Heading and badges', 'Marks and frontier', 'Labels and legend', 'Number format', 'Axes', 'Uncertainty', 'Footer']);
  });

  it('renders the caption sections, the note and a reset button for the profile', () => {
    render('profile');
    expect(host().querySelector('#mc-style-profile-heading')?.textContent).toContain('Profile plot');
    const inputs = Array.from(host().querySelectorAll<HTMLInputElement>('input'));
    expect(sectionTitles()).toEqual(['Heading and badges', 'Number format', 'Footer']);
    expect(inputs.map(input => input.id)).toEqual([
      'mc-style-profile-titleSizePx',
      'mc-style-profile-badgeTextSizePx',
      'mc-style-profile-badge-models',
      'mc-style-profile-badge-runs',
      'mc-style-profile-badge-questions',
      'mc-style-profile-badge-pricing',
      'mc-style-profile-footer',
      'mc-style-profile-footerTextSizePx'
    ]);
    expect(inputs.filter(input => input.type === 'checkbox').every(input => input.checked)).toBeTrue();
    expect(host().querySelector('.fsp-note')?.textContent?.trim()).toBe('Chart text follows Text size on the Download tab.');
    const reset = host().querySelector('#mc-style-profile-reset') as HTMLButtonElement;
    expect(reset.textContent!.trim()).toBe('Reset profile style');
  });

  it('switches the n = 1 marker, rewords the filled-bars hint and resets it', () => {
    render('bar');
    const marker = control('mc-style-bar-singleRunMarker');
    expect(marker.checked).toBeTrue();

    setChecked(marker, false);
    expect(emitted[0].bar).toEqual({ ...DEFAULT_FIGURE_STYLE.bar, singleRunMarker: false });
    expect(emitted[0].scatter).toBe(DEFAULT_FIGURE_STYLE.scatter);
    expect(emitted[0].profile).toBe(DEFAULT_FIGURE_STYLE.profile);
    acceptLast();
    expect(control('mc-style-bar-singleRunMarker').checked).toBeFalse();
    const hint = hintOf(control('mc-style-bar-filledBars'));
    expect(hint).toBe('Single-run bars are outlined unless this is on; multi-run bars are always filled. '
      + 'With the n = 1 marker off, only the runs badge shows how many runs each bar has.');

    fixture.componentInstance.resetBar();
    expect(emitted[emitted.length - 1].bar.singleRunMarker).toBeTrue();
  });

  it('hides and shows one badge of one family at a time', () => {
    render('bar');
    const questions = control('mc-style-bar-badge-questions');
    expect(questions.checked).toBeTrue();
    setChecked(questions, false);
    expect(emitted[0].bar.hiddenBadges).toEqual(['questions']);
    expect(emitted[0].bar).toEqual({ ...DEFAULT_FIGURE_STYLE.bar, hiddenBadges: ['questions'] });
    expect(emitted[0].scatter).toBe(DEFAULT_FIGURE_STYLE.scatter);
    expect(emitted[0].profile).toBe(DEFAULT_FIGURE_STYLE.profile);
    acceptLast();
    expect(control('mc-style-bar-badge-questions').checked).toBeFalse();
    expect(control('mc-style-bar-badge-models').checked).toBeTrue();
    setChecked(control('mc-style-bar-badge-questions'), true);
    expect(emitted[1].bar.hiddenBadges).toEqual([]);

    render('scatter');
    setChecked(control('mc-style-scatter-badge-runs'), false);
    const scatterLast = emitted[emitted.length - 1];
    expect(scatterLast.scatter.hiddenBadges).toEqual(['runs']);
    expect(scatterLast.bar.hiddenBadges).toEqual([]);
    expect(scatterLast.profile.hiddenBadges).toEqual([]);

    render('profile');
    setChecked(control('mc-style-profile-badge-pricing'), false);
    const profileLast = emitted[emitted.length - 1];
    expect(profileLast.profile.hiddenBadges).toEqual(['pricing']);
    expect(profileLast.bar.hiddenBadges).toEqual([]);
    expect(profileLast.scatter.hiddenBadges).toEqual([]);
  });

  it('ties the pricing badge checkbox to its hint', () => {
    render('bar');
    expect(hintOf(control('mc-style-bar-badge-pricing')).trim()).toBe('Only on figures with a cost axis.');
    expect(control('mc-style-bar-badge-models').getAttribute('aria-describedby')).toBeNull();
  });

  it('resets the profile only', () => {
    const changed: FigureStyle = {
      bar: { ...DEFAULT_FIGURE_STYLE.bar, hiddenBadges: ['runs'] },
      scatter: { ...DEFAULT_FIGURE_STYLE.scatter, hiddenBadges: ['models'] },
      profile: { ...DEFAULT_FIGURE_STYLE.profile, hiddenBadges: ['questions', 'pricing'] },
      numbers: DEFAULT_FIGURE_STYLE.numbers
    };
    render('profile', changed);
    (host().querySelector('#mc-style-profile-reset') as HTMLButtonElement).click();
    expect(emitted[0].profile).toEqual(DEFAULT_FIGURE_STYLE.profile);
    expect(emitted[0].bar).toBe(changed.bar);
    expect(emitted[0].scatter).toBe(changed.scatter);
  });

  it('emits a clamped style as a range moves, announcing it in words', () => {
    render('bar');
    const gap = control('mc-style-bar-gapPercent');
    expect(gap.getAttribute('aria-valuetext')).toBe('28 percent');
    expect(control('mc-style-bar-maxBarWidthPx').getAttribute('aria-valuetext')).toBe('24 pixels');

    setRange(gap, 10);
    expect(emitted.length).toBe(1);
    expect(emitted[0].bar.gapPercent).toBe(10);
    expect(emitted[0].scatter).toBe(DEFAULT_FIGURE_STYLE.scatter);
    acceptLast();
    expect(control('mc-style-bar-gapPercent').getAttribute('aria-valuetext')).toBe('10 percent');
  });

  it('emits null for No limit and disables the width range until it is unticked', () => {
    render('bar');
    setChecked(control('mc-style-bar-noBarWidthLimit'), true);
    expect(emitted[0].bar.maxBarWidthPx).toBeNull();
    acceptLast();
    const range = control('mc-style-bar-maxBarWidthPx');
    expect(range.disabled).toBeTrue();
    expect(range.getAttribute('aria-valuetext')).toBe('No limit');

    setChecked(control('mc-style-bar-noBarWidthLimit'), false);
    expect(emitted[1].bar.maxBarWidthPx).toBe(24);
  });

  it('switches filled bars and resets it', () => {
    render('bar');
    const filled = control('mc-style-bar-filledBars');
    expect(filled.checked).toBeFalse();
    expect(hintOf(filled).trim()).toBe('Single-run bars are outlined unless this is on; multi-run bars are always filled.');

    setChecked(filled, true);
    expect(emitted[0].bar).toEqual({ ...DEFAULT_FIGURE_STYLE.bar, filledBars: true });
    expect(emitted[0].scatter).toBe(DEFAULT_FIGURE_STYLE.scatter);
    acceptLast();
    expect(control('mc-style-bar-filledBars').checked).toBeTrue();

    fixture.componentInstance.resetBar();
    expect(emitted[emitted.length - 1].bar.filledBars).toBeFalse();
  });

  it('switches the uncertainty bars of one family only', () => {
    render('bar');
    setChecked(control('mc-style-bar-intervals'), false);
    expect(emitted[0].bar.intervals).toBeFalse();
    expect(emitted[0].scatter.intervals).toBeTrue();

    render('scatter');
    setChecked(control('mc-style-scatter-intervals'), false);
    expect(emitted[emitted.length - 1].scatter.intervals).toBeFalse();
    expect(emitted[emitted.length - 1].bar.intervals).toBeTrue();
  });

  it('enables the hidden-intervals note only once the bars are hidden, and switches it for one family', () => {
    render('bar');
    expect(control('mc-style-bar-hiddenIntervalsNote').disabled).toBeTrue();
    expect(control('mc-style-bar-hiddenIntervalsNote').checked).toBeTrue();

    setChecked(control('mc-style-bar-intervals'), false);
    acceptLast();
    const note = control('mc-style-bar-hiddenIntervalsNote');
    expect(note.disabled).toBeFalse();
    setChecked(note, false);
    const last = emitted[emitted.length - 1];
    expect(last.bar.hiddenIntervalsNote).toBeFalse();
    expect(last.scatter.hiddenIntervalsNote).toBeTrue();

    render('scatter');
    expect(control('mc-style-scatter-hiddenIntervalsNote').disabled).toBeTrue();
    setChecked(control('mc-style-scatter-intervals'), false);
    acceptLast();
    setChecked(control('mc-style-scatter-hiddenIntervalsNote'), false);
    expect(emitted[emitted.length - 1].scatter.hiddenIntervalsNote).toBeFalse();
    expect(emitted[emitted.length - 1].bar.hiddenIntervalsNote).toBeTrue();
  });

  it('switches the frontier note and the shading', () => {
    render('scatter');
    setChecked(control('mc-style-scatter-frontierIntervalsNote'), false);
    expect(emitted[0].scatter.frontierIntervalsNote).toBeFalse();
    setChecked(control('mc-style-scatter-dominatedShading'), false);
    expect(emitted[1].scatter.dominatedShading).toBeFalse();
    expect(emitted[1].scatter.frontierIntervalsNote).toBeTrue();
  });

  it('keeps the mean-time note enabled whatever the uncertainty bars, and switches it alone', () => {
    render('bar');
    expect(control('mc-style-bar-meanTimeNoIntervalNote').disabled).toBeFalse();
    render('bar', { ...DEFAULT_FIGURE_STYLE, bar: { ...DEFAULT_FIGURE_STYLE.bar, intervals: false } });
    expect(control('mc-style-bar-meanTimeNoIntervalNote').disabled).toBeFalse();

    render('bar');
    setChecked(control('mc-style-bar-meanTimeNoIntervalNote'), false);
    expect(emitted[0].bar).toEqual({ ...DEFAULT_FIGURE_STYLE.bar, meanTimeNoIntervalNote: false });
  });

  it('quotes each note verbatim in its checkbox hint', () => {
    render('bar');
    expect(hintOf(control('mc-style-bar-hiddenIntervalsNote'))).toContain(HIDDEN_INTERVALS_NOTE);
    expect(hintOf(control('mc-style-bar-meanTimeNoIntervalNote'))).toContain(MEAN_TIME_NO_INTERVAL_NOTE);
    render('scatter');
    expect(hintOf(control('mc-style-scatter-hiddenIntervalsNote'))).toContain(HIDDEN_INTERVALS_NOTE);
    expect(hintOf(control('mc-style-scatter-frontierIntervalsNote'))).toContain(FRONTIER_UNCERTAINTY_NOTE);
  });

  it('resets only its own half, intervals, shading and notes included', () => {
    const changed: FigureStyle = {
      bar: { ...DEFAULT_FIGURE_STYLE.bar, gapPercent: 5, intervals: false, hiddenIntervalsNote: false, meanTimeNoIntervalNote: false },
      scatter: { ...DEFAULT_FIGURE_STYLE.scatter, markRadiusPx: 12, intervals: false, dominatedShading: false, frontierIntervalsNote: false },
      profile: DEFAULT_FIGURE_STYLE.profile,
      numbers: DEFAULT_FIGURE_STYLE.numbers
    };
    render('bar', changed);
    (host().querySelector('#mc-style-bar-reset') as HTMLButtonElement).click();
    expect(emitted[0].bar).toEqual(DEFAULT_FIGURE_STYLE.bar);
    expect(emitted[0].scatter).toEqual(changed.scatter);

    render('scatter', changed);
    const reset = host().querySelector('#mc-style-scatter-reset') as HTMLButtonElement;
    expect(reset.textContent!.trim()).toBe('Reset trade-off style');
    reset.click();
    expect(emitted[1].scatter).toEqual(DEFAULT_FIGURE_STYLE.scatter);
    expect(emitted[1].bar).toEqual(changed.bar);
  });

  it('emits the two trade-off toggles through their own outputs and disables the legend under direct labels', () => {
    const named: boolean[] = [];
    const valued: boolean[] = [];
    fixture.componentInstance.directLabelsChange.subscribe(on => named.push(on));
    fixture.componentInstance.inlineValuesChange.subscribe(on => valued.push(on));
    fixture.componentRef.setInput('inlineValues', false);
    render('scatter');
    expect(control('mc-style-scatter-labelTextSizePx').disabled).toBeTrue();

    setChecked(control('mc-style-scatter-directLabels'), true);
    setChecked(control('mc-style-scatter-inlineValues'), true);
    expect(named).toEqual([true]);
    expect(valued).toEqual([true]);
    expect(emitted.length).toBe(0);

    fixture.componentRef.setInput('directLabels', true);
    fixture.detectChanges();
    expect(control('mc-style-scatter-labelTextSizePx').disabled).toBeFalse();
    const legend = host().querySelector('#mc-style-scatter-legend-right')!.closest('fieldset') as HTMLFieldSetElement;
    expect(legend.disabled).toBeTrue();
  });

  it('labels every control, and every id is unique', () => {
    for (const kind of ['bar', 'scatter', 'profile'] as const) {
      render(kind);
      const inputs = Array.from(host().querySelectorAll('input'));
      expect(inputs.length).withContext(kind).toBeGreaterThan(kind === 'profile' ? 3 : 5);
      for (const input of inputs) {
        expect(input.id).withContext(`${kind} ${input.type}`).toMatch(/^mc-style-/);
        const label = input.closest('label') ?? host().querySelector(`label[for="${input.id}"]`);
        expect(label?.textContent?.trim()).withContext(input.id).toBeTruthy();
      }
      const ids = Array.from(host().querySelectorAll('[id]')).map(element => element.id);
      expect(new Set(ids).size).withContext(kind).toBe(ids.length);
    }
  });
  it('opens the first section of each family by default and leaves the rest closed', () => {
    for (const kind of ['bar', 'scatter', 'profile'] as const) {
      render(kind);
      const sections = Array.from(host().querySelectorAll<HTMLDetailsElement>('details.gh-disclosure--section'));
      expect(sections[0].id).withContext(kind).toBe(`mc-style-${kind}-section-heading`);
      expect(sections.map(details => details.open)).withContext(kind)
        .toEqual(sections.map((_details, index) => index === 0));
    }
  });

  it('keeps several sections open at once, and remembers them in local storage', () => {
    render('bar');
    toggleSection('mc-style-bar-section-bars');
    toggleSection('mc-style-bar-section-footer');
    expect(section('mc-style-bar-section-heading').open).toBeTrue();
    expect(section('mc-style-bar-section-bars').open).toBeTrue();
    expect(section('mc-style-bar-section-footer').open).toBeTrue();

    toggleSection('mc-style-bar-section-heading');
    const stored = JSON.parse(localStorage.getItem(FIGURE_STYLE_PANEL_OPEN_KEY)!);
    expect(stored.bar).toEqual(['bars', 'footer']);
    expect(stored.scatter).toEqual(['heading']);

    fixture.destroy();
    create();
    render('bar');
    expect(section('mc-style-bar-section-heading').open).toBeFalse();
    expect(section('mc-style-bar-section-bars').open).toBeTrue();
    expect(section('mc-style-bar-section-footer').open).toBeTrue();
    render('scatter');
    expect(section('mc-style-scatter-section-heading').open).toBeTrue();
  });

  it('falls back to the default sections, silently, when storage throws', () => {
    fixture.destroy();
    spyOn(Storage.prototype, 'getItem').and.throwError('blocked');
    spyOn(Storage.prototype, 'setItem').and.throwError('blocked');
    expect(() => create()).not.toThrow();
    render('bar');
    expect(section('mc-style-bar-section-heading').open).toBeTrue();
    expect(section('mc-style-bar-section-bars').open).toBeFalse();
    expect(() => toggleSection('mc-style-bar-section-bars')).not.toThrow();
    expect(section('mc-style-bar-section-bars').open).toBeTrue();
  });

  it('ignores a malformed stored value', () => {
    fixture.destroy();
    localStorage.setItem(FIGURE_STYLE_PANEL_OPEN_KEY, JSON.stringify({ bar: ['layout', 'nonsense', 3], scatter: 'all' }));
    create();
    render('bar');
    expect(section('mc-style-bar-section-layout').open).toBeTrue();
    expect(section('mc-style-bar-section-heading').open).toBeFalse();
    render('scatter');
    expect(section('mc-style-scatter-section-heading').open).toBeTrue();
  });

  it('expands and collapses every section of the shown family', () => {
    render('scatter');
    (host().querySelector('#mc-style-scatter-expand') as HTMLButtonElement).click();
    fixture.detectChanges();
    const sections = (): HTMLDetailsElement[] =>
      Array.from(host().querySelectorAll<HTMLDetailsElement>('details.gh-disclosure--section'));
    expect(sections().every(details => details.open)).toBeTrue();
    expect(JSON.parse(localStorage.getItem(FIGURE_STYLE_PANEL_OPEN_KEY)!).scatter.length).toBe(7);

    (host().querySelector('#mc-style-scatter-collapse') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(sections().some(details => details.open)).toBeFalse();
    expect(JSON.parse(localStorage.getItem(FIGURE_STYLE_PANEL_OPEN_KEY)!).bar).toEqual(['heading']);
  });

  it('summarizes each section in a read-out hidden from assistive technology', () => {
    render('bar');
    const readout = (name: string): HTMLElement =>
      host().querySelector(`#mc-style-bar-section-${name} > summary .gh-disclosure-summary-value`) as HTMLElement;
    expect(readout('heading').getAttribute('aria-hidden')).toBe('true');
    expect(readout('heading').textContent!.trim()).toBe('18 px · badges 11 px · Better, models, runs, questions, pricing');
    expect(readout('values').textContent!.trim()).toBe('values 11 px · axis 11/12 px · n = 1');
    expect(readout('uncertainty').textContent!.trim()).toBe('shown · Speed note');
    expect(readout('footer').textContent!.trim()).toBe('shown · 12 px');
    expect(readout('layout').textContent!.trim()).toBe('automatic · gridlines');
    for (const summary of Array.from(host().querySelectorAll('details.gh-disclosure--section > summary'))) {
      expect(summary.querySelector('button, input, a')).toBeNull();
    }
  });

  it('puts every hint into an info tip the control is described by, and no hint paragraph remains', () => {
    for (const kind of ['bar', 'scatter', 'profile'] as const) {
      render(kind);
      expect(host().querySelectorAll('.gh-fieldset-hint').length).withContext(kind).toBe(0);
      const described = Array.from(host().querySelectorAll('[aria-describedby]'));
      expect(described.length).withContext(kind).toBeGreaterThan(0);
      for (const element of described) {
        const id = element.getAttribute('aria-describedby')!;
        const tip = host().querySelector(`#${id}`);
        expect(tip?.getAttribute('popover')).withContext(`${kind} ${id}`).toBe('hint');
        expect(tip?.closest('app-info-tip')).withContext(`${kind} ${id}`).not.toBeNull();
        expect(id.endsWith('-tip')).withContext(`${kind} ${id}`).toBeTrue();
      }
      for (const button of Array.from(host().querySelectorAll('app-info-tip button'))) {
        expect(button.getAttribute('aria-label')).withContext(kind).toMatch(/^About /);
      }
    }
  });

  it('offers the Better badge for bars and trade-offs, with its own tip, but not for the profile', () => {
    render('bar');
    const bar = control('mc-style-bar-badge-direction');
    expect(bar.checked).toBeTrue();
    expect(hintOf(bar)).toContain('toward the better end of the value axis');
    setChecked(bar, false);
    expect(emitted[0].bar.hiddenBadges).toEqual(['direction']);
    expect(emitted[0].scatter.hiddenBadges).toEqual([]);

    render('scatter');
    expect(hintOf(control('mc-style-scatter-badge-direction')).trim()).toBe('An arrow toward the better corner of the chart.');

    render('profile');
    expect(host().querySelector('#mc-style-profile-badge-direction')).toBeNull();
  });

  it('sizes the caption text of one family, up to 48 px', () => {
    render('scatter');
    const heading = control('mc-style-scatter-titleSizePx');
    expect(heading.max).toBe('48');
    expect(heading.getAttribute('aria-describedby')).toBeNull();
    expect(host().querySelector('#mc-style-scatter-titleSizePx-tip')).toBeNull();
    setRange(heading, 30);
    expect(emitted[0].scatter.titleSizePx).toBe(30);
    expect(emitted[0].bar).toBe(DEFAULT_FIGURE_STYLE.bar);

    setRange(control('mc-style-scatter-axisTitleSizePx'), 20);
    expect(emitted[1].scatter.axisTitleSizePx).toBe(20);
    expect(control('mc-style-scatter-labelTextSizePx').max).toBe('48');
  });

  it('hides the footer and disables its size, saying why', () => {
    render('bar');
    const size = control('mc-style-bar-footerTextSizePx');
    expect(size.disabled).toBeFalse();
    setChecked(control('mc-style-bar-footer'), false);
    expect(emitted[0].bar.footer).toBeFalse();
    acceptLast();
    expect(control('mc-style-bar-footerTextSizePx').disabled).toBeTrue();
    expect(hintOf(control('mc-style-bar-footerTextSizePx'))).toBe('Available while the footer is shown.');
    expect(host().querySelector('#mc-style-bar-section-footer .gh-disclosure-summary-value')?.textContent?.trim()).toBe('hidden');
  });

  // --- Section resets ---------------------------------------------------------------------------

  function resetButton(id: string): HTMLButtonElement {
    const element = host().querySelector<HTMLButtonElement>(`#${id}`);
    expect(element).withContext(id).not.toBeNull();
    return element!;
  }

  function statusText(): string {
    return host().querySelector('[role="status"]')?.textContent?.trim() ?? '';
  }

  it('claims every style field of each family in exactly one section', () => {
    for (const kind of ['bar', 'scatter', 'profile'] as const) {
      const keys = FIGURE_STYLE_SECTIONS[kind].filter(section => !section.shared).flatMap(section => [...section.keys]);
      expect(new Set(keys).size).withContext(`${kind} duplicates`).toBe(keys.length);
      expect([...keys].sort()).withContext(kind).toEqual(Object.keys(DEFAULT_FIGURE_STYLE[kind]).sort());
    }
  });

  it('gives every section a named reset button with a tooltip, disabled at defaults', () => {
    for (const kind of ['bar', 'scatter', 'profile'] as const) {
      fixture.componentRef.setInput('inlineValues', kind === 'scatter');
      render(kind);
      const sections = Array.from(host().querySelectorAll('.fsp-section'));
      expect(sections.length).withContext(kind).toBe(FIGURE_STYLE_SECTIONS[kind].length);
      sections.forEach((wrapper, index) => {
        const { title, shared } = FIGURE_STYLE_SECTIONS[kind][index];
        const buttons = wrapper.querySelectorAll('.fsp-section-reset');
        expect(buttons.length).withContext(`${kind} ${title}`).toBe(1);
        const button = buttons[0] as HTMLButtonElement;
        expect(button.getAttribute('aria-label'))
          .toBe(shared ? 'Reset visible number formats to defaults' : `Reset ${title} to defaults`);
        expect(button.getAttribute('aria-disabled')).withContext(`${kind} ${title}`).toBe('true');
        expect(button.closest('summary')).toBeNull();
        expect(button.closest('details')).toBeNull();
        const tip = host().querySelector(`#${button.getAttribute('interestfor')}`);
        expect(tip?.getAttribute('popover')).withContext(`${kind} ${title}`).toBe('hint');
        expect(tip?.textContent?.trim()).toBe('Already at defaults');
      });
    }
  });

  // --- Number format and axis title break --------------------------------------------------------

  function numberSelect(family: FigureStylePanelKind, measure: NumberMeasure): HTMLSelectElement {
    const element = host().querySelector<HTMLSelectElement>(`#mc-style-${family}-number-${measure}`);
    expect(element).withContext(`${family} ${measure}`).not.toBeNull();
    return element!;
  }

  function numberRowIds(family: FigureStylePanelKind): string[] {
    return Array.from(host().querySelectorAll<HTMLSelectElement>(`#mc-style-${family}-section-numbers select`)).map(s => s.id);
  }

  function chooseNumber(select: HTMLSelectElement, value: number): void {
    select.value = String(value);
    select.dispatchEvent(new Event('change'));
    fixture.detectChanges();
  }

  function withNumbers(numbers: Partial<Record<NumberMeasure, number>>): FigureStyle {
    return { ...DEFAULT_FIGURE_STYLE, numbers: { ...DEFAULT_FIGURE_STYLE.numbers, ...numbers } };
  }

  it('covers every number measure through the shared record and the row mappings', () => {
    expect(Object.keys(DEFAULT_FIGURE_STYLE.numbers).sort()).toEqual([...NUMBER_MEASURES].sort());
    const reachable = new Set<NumberMeasure>(['intelligenceIndex', 'costPerQuestion']);
    (['meanModelTime', 'totalModelTime', 'ttftP50', 'speedIndex'] as SpeedMeasure[]).forEach(m => reachable.add(speedNumberMeasure(m)));
    (['candidateSuite', 'totalRun'] as CostMeasure[]).forEach(m => reachable.add(costNumberMeasure(m)));
    expect([...reachable].sort()).toEqual([...NUMBER_MEASURES].sort());
    for (const kind of ['bar', 'scatter', 'profile'] as const) {
      expect(FIGURE_STYLE_SECTIONS[kind].filter(section => section.shared).map(section => section.name))
        .withContext(kind).toEqual(['numbers']);
    }
  });

  it('shows three labelled number rows per family, with the family\'s own cost', () => {
    render('bar');
    expect(numberRowIds('bar')).toEqual([
      'mc-style-bar-number-intelligenceIndex', 'mc-style-bar-number-meanModelTime', 'mc-style-bar-number-suiteCost'
    ]);
    render('scatter');
    expect(numberRowIds('scatter')).toEqual([
      'mc-style-scatter-number-intelligenceIndex', 'mc-style-scatter-number-meanModelTime', 'mc-style-scatter-number-costPerQuestion'
    ]);
    render('profile');
    expect(numberRowIds('profile')).toEqual([
      'mc-style-profile-number-intelligenceIndex', 'mc-style-profile-number-meanModelTime', 'mc-style-profile-number-suiteCost'
    ]);
    for (const id of numberRowIds('profile')) {
      const label = host().querySelector(`label[for="${id}"]`);
      const measure = id.replace('mc-style-profile-number-', '') as NumberMeasure;
      expect(label?.textContent?.trim()).withContext(id).toBe(MEASURE_NAMES[measure]);
      expect(control(id).getAttribute('aria-describedby')).toBe('mc-style-profile-numbers-tip');
    }
    expect(hintOf(control('mc-style-profile-number-intelligenceIndex'))).toContain('Resetting a figure style leaves number formats as they are.');
  });

  it('follows the selected speed and cost measures, and shows each measure\'s own stored setting', () => {
    fixture.componentRef.setInput('speedMeasure', 'ttftP50');
    fixture.componentRef.setInput('costMeasure', 'totalRun');
    render('bar', withNumbers({ ttftP50: 3, totalRunCost: 1, meanModelTime: 5 }));
    expect(numberRowIds('bar')).toEqual([
      'mc-style-bar-number-intelligenceIndex', 'mc-style-bar-number-ttftP50', 'mc-style-bar-number-totalRunCost'
    ]);
    expect(numberSelect('bar', 'ttftP50').value).toBe('3');
    expect(numberSelect('bar', 'totalRunCost').value).toBe('1');
    render('scatter', withNumbers({ ttftP50: 3, totalRunCost: 1 }));
    expect(numberSelect('scatter', 'costPerQuestion').value).toBe('4');

    fixture.componentRef.setInput('speedMeasure', 'meanModelTime');
    render('bar', withNumbers({ ttftP50: 3, totalRunCost: 1, meanModelTime: 5 }));
    expect(numberSelect('bar', 'meanModelTime').value).toBe('5');
  });

  it('labels each option with its decimal count and the family sample, or the fixed example', () => {
    render('bar');
    const options = (measure: NumberMeasure): string[] =>
      Array.from(numberSelect('bar', measure).options).map(option => option.textContent!.trim());
    expect(options('meanModelTime')).toEqual([
      '0 (23 s)', '1 (22.5 s)', '2 (22.50 s)', '3 (22.500 s)', '4 (22.5000 s)', '5 (22.50000 s)', '6 (22.500000 s)'
    ]);
    expect(Array.from(numberSelect('bar', 'meanModelTime').options).map(option => option.value)).toEqual(['0', '1', '2', '3', '4', '5', '6']);
    expect(options('suiteCost')[4]).toBe('4 ($0.0761)');
    expect(options('intelligenceIndex')[0]).toBe('0 (71)');

    fixture.componentRef.setInput('numberSamples', {
      intelligenceIndex: { value: 71.44 },
      meanModelTime: { value: 850.3, unit: 'ms' },
      suiteCost: { value: 0.0428 }
    });
    fixture.detectChanges();
    expect(options('intelligenceIndex')[1]).toBe('1 (71.4)');
    expect(options('meanModelTime').slice(0, 5)).toEqual(['0 (850 ms)', '1 (850 ms)', '2 (850 ms)', '3 (850 ms)', '4 (850.3 ms)']);
    expect(options('suiteCost')[2]).toBe('2 ($0.04)');
  });

  it('emits one measure\'s decimals for every family and clears the reset status', () => {
    render('scatter', withNumbers({ suiteCost: 2 }));
    fixture.componentInstance.resetStatus = 'Something reset.';
    chooseNumber(numberSelect('scatter', 'intelligenceIndex'), 2);
    expect(emitted.length).toBe(1);
    expect(emitted[0].numbers).toEqual({ ...DEFAULT_FIGURE_STYLE.numbers, suiteCost: 2, intelligenceIndex: 2 });
    expect(emitted[0].bar).toBe(DEFAULT_FIGURE_STYLE.bar);
    expect(emitted[0].scatter).toBe(DEFAULT_FIGURE_STYLE.scatter);
    expect(fixture.componentInstance.resetStatus).toBe('');
    expect(DEFAULT_FIGURE_STYLE.numbers.intelligenceIndex).toBe(0);

    fixture.componentInstance.setNumber('costPerQuestion', 9.4);
    expect(emitted[1].numbers.costPerQuestion).toBe(6);
  });

  it('summarizes the visible number formats in the read-out', () => {
    render('bar', withNumbers({ meanModelTime: 3 }));
    const readout = host().querySelector('#mc-style-bar-section-numbers > summary .gh-disclosure-summary-value');
    expect(readout?.textContent?.trim()).toBe('Intelligence 0 · Mean time 3 · Cost 4');
    render('scatter');
    expect(host().querySelector('#mc-style-scatter-section-numbers > summary .gh-disclosure-summary-value')?.textContent?.trim())
      .toBe('Intelligence 0 · Mean time 1 · Cost / question 4');
  });

  it('resets only the visible number formats, and keeps a hidden measure\'s setting', () => {
    render('bar', withNumbers({ ttftP50: 5, suiteCost: 1 }));
    const reset = resetButton('mc-style-bar-section-numbers-reset');
    expect(reset.getAttribute('aria-disabled')).toBeNull();
    expect(host().querySelector('#mc-style-bar-section-numbers-reset-tip')?.textContent?.trim()).toBe('Reset to defaults');
    reset.click();
    fixture.detectChanges();
    expect(emitted.length).toBe(1);
    expect(emitted[0].numbers).toEqual({ ...DEFAULT_FIGURE_STYLE.numbers, ttftP50: 5 });
    expect(emitted[0].bar).toBe(DEFAULT_FIGURE_STYLE.bar);
    expect(statusText()).toBe('Visible number formats reset to defaults.');

    // Only a hidden measure differs, so the visible rows are at their defaults.
    render('bar', withNumbers({ ttftP50: 5 }));
    expect(resetButton('mc-style-bar-section-numbers-reset').getAttribute('aria-disabled')).toBe('true');
    resetButton('mc-style-bar-section-numbers-reset').click();
    expect(emitted.length).toBe(1);
  });

  it('leaves every number format alone on each family reset', () => {
    const numbers = { ...DEFAULT_FIGURE_STYLE.numbers, intelligenceIndex: 2, suiteCost: 1, costPerQuestion: 6 };
    const changed: FigureStyle = { ...DEFAULT_FIGURE_STYLE, numbers };
    for (const [kind, id] of [['bar', 'mc-style-bar-reset'], ['scatter', 'mc-style-scatter-reset'], ['profile', 'mc-style-profile-reset']] as const) {
      emitted = [];
      render(kind, changed);
      (host().querySelector(`#${id}`) as HTMLButtonElement).click();
      expect(emitted[0].numbers).withContext(kind).toEqual(numbers);
    }
  });

  it('opens the Number format section closed under an old stored disclosure state', () => {
    fixture.destroy();
    localStorage.setItem(FIGURE_STYLE_PANEL_OPEN_KEY, JSON.stringify({ bar: ['values', 'layout'], scatter: ['labels'] }));
    create();
    render('bar');
    expect(section('mc-style-bar-section-values').open).toBeTrue();
    expect(section('mc-style-bar-section-numbers').open).toBeFalse();
    render('scatter');
    expect(section('mc-style-scatter-section-numbers').open).toBeFalse();
    render('profile');
    expect(section('mc-style-profile-section-heading').open).toBeTrue();
    expect(section('mc-style-profile-section-numbers').open).toBeFalse();
  });

  it('gives every number control and title-break radio a distinct id', () => {
    for (const kind of ['bar', 'scatter', 'profile'] as const) {
      render(kind);
      const ids = Array.from(host().querySelectorAll('[id]')).map(element => element.id);
      expect(new Set(ids).size).withContext(kind).toBe(ids.length);
    }
  });

  it('chooses the axis title line break with a radio group, Automatic by default', () => {
    render('bar');
    const auto = control('mc-style-bar-axisTitleBreak-auto');
    expect(auto.checked).toBeTrue();
    expect(auto.name).toBe('mc-style-bar-axisTitleBreak');
    const fieldset = auto.closest('fieldset')!;
    expect(fieldset.querySelector('legend')?.textContent).toContain('Axis title line break');
    expect(host().querySelector(`#${fieldset.getAttribute('aria-describedby')}`)?.textContent?.trim())
      .toBe('Automatic moves the part in parentheses to a second line when the title is longer than its axis.');

    setChecked(control('mc-style-bar-axisTitleBreak-never'), true);
    expect(emitted[0].bar.axisTitleBreak).toBe('never');
    acceptLast();
    expect(control('mc-style-bar-axisTitleBreak-never').checked).toBeTrue();
    expect(host().querySelector('#mc-style-bar-section-values > summary .gh-disclosure-summary-value')?.textContent?.trim())
      .toBe('values 11 px · axis 11/12 px · title never broken · n = 1');
    expect(resetButton('mc-style-bar-section-values-reset').getAttribute('aria-disabled')).toBeNull();
    resetButton('mc-style-bar-section-values-reset').click();
    expect(emitted[1].bar.axisTitleBreak).toBe('auto');
  });

  it('resets one section only, and lights its button while it differs', () => {
    const changed: FigureStyle = {
      ...DEFAULT_FIGURE_STYLE,
      bar: { ...DEFAULT_FIGURE_STYLE.bar, titleSizePx: 30, gapPercent: 10 }
    };
    render('bar', changed);
    const bars = resetButton('mc-style-bar-section-bars-reset');
    expect(bars.getAttribute('aria-disabled')).toBeNull();
    expect(host().querySelector('#mc-style-bar-section-bars-reset-tip')?.textContent?.trim()).toBe('Reset to defaults');
    expect(resetButton('mc-style-bar-section-values-reset').getAttribute('aria-disabled')).toBe('true');

    bars.click();
    expect(emitted.length).toBe(1);
    expect(emitted[0].bar).toEqual({ ...changed.bar, gapPercent: DEFAULT_FIGURE_STYLE.bar.gapPercent });
    expect(emitted[0].scatter).toBe(changed.scatter);
    expect(emitted[0].profile).toBe(changed.profile);
  });

  it('emits nothing from a reset button already at defaults', () => {
    render('bar');
    resetButton('mc-style-bar-section-bars-reset').click();
    resetButton('mc-style-bar-section-heading-reset').click();
    expect(emitted).toEqual([]);
    expect(statusText()).toBe('');
  });

  it('resets a closed section', () => {
    render('bar', { ...DEFAULT_FIGURE_STYLE, bar: { ...DEFAULT_FIGURE_STYLE.bar, titleSizePx: 30 } });
    toggleSection('mc-style-bar-section-heading');
    expect(section('mc-style-bar-section-heading').open).toBeFalse();
    resetButton('mc-style-bar-section-heading-reset').click();
    expect(emitted[0].bar).toEqual(DEFAULT_FIGURE_STYLE.bar);
  });

  it('resets the two page toggles with the Labels and legend section', () => {
    const named: boolean[] = [];
    const valued: boolean[] = [];
    fixture.componentInstance.directLabelsChange.subscribe(on => named.push(on));
    fixture.componentInstance.inlineValuesChange.subscribe(on => valued.push(on));
    fixture.componentRef.setInput('directLabels', true);
    fixture.componentRef.setInput('inlineValues', false);
    render('scatter');
    const labels = resetButton('mc-style-scatter-section-labels-reset');
    expect(labels.getAttribute('aria-disabled')).toBeNull();

    labels.click();
    expect(named).toEqual([false]);
    expect(valued).toEqual([true]);
    expect(emitted.length).withContext('the style fields were already at defaults').toBe(0);
  });

  it('announces a reset in the status region and clears it on the next change', () => {
    render('bar', { ...DEFAULT_FIGURE_STYLE, bar: { ...DEFAULT_FIGURE_STYLE.bar, gapPercent: 10 } });
    expect(statusText()).toBe('');
    resetButton('mc-style-bar-section-bars-reset').click();
    fixture.detectChanges();
    expect(statusText()).toBe('Bars reset to defaults.');

    acceptLast();
    setRange(control('mc-style-bar-gapPercent'), 12);
    expect(statusText()).toBe('');

    resetButton('mc-style-bar-reset').click();
    fixture.detectChanges();
    expect(statusText()).toBe('Bar style reset to defaults.');
  });

  it('resets the two page toggles with the whole trade-off style', () => {
    const named: boolean[] = [];
    const valued: boolean[] = [];
    fixture.componentInstance.directLabelsChange.subscribe(on => named.push(on));
    fixture.componentInstance.inlineValuesChange.subscribe(on => valued.push(on));
    fixture.componentRef.setInput('directLabels', true);
    fixture.componentRef.setInput('inlineValues', false);
    render('scatter');
    (host().querySelector('#mc-style-scatter-reset') as HTMLButtonElement).click();
    expect(named).toEqual([false]);
    expect(valued).toEqual([true]);
    expect(emitted[0].scatter).toEqual(DEFAULT_FIGURE_STYLE.scatter);

    fixture.componentRef.setInput('directLabels', false);
    fixture.componentRef.setInput('inlineValues', true);
    fixture.detectChanges();
    (host().querySelector('#mc-style-scatter-reset') as HTMLButtonElement).click();
    expect(named.length).withContext('no toggle emission at its default').toBe(1);
    expect(valued.length).toBe(1);
  });
});
