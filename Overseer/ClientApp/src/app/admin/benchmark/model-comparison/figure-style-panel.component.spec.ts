import { ComponentFixture, TestBed } from '@angular/core/testing';

import {
  FIGURE_STYLE_PANEL_OPEN_KEY,
  FigureStylePanelComponent,
  FigureStylePanelKind
} from './figure-style-panel.component';
import { DEFAULT_FIGURE_STYLE, FigureStyle, HIDDEN_INTERVALS_NOTE } from './figure-style';
import { FRONTIER_UNCERTAINTY_NOTE, MEAN_TIME_NO_INTERVAL_NOTE } from './model-comparison-charts';

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
    expect(sectionTitles()).toEqual(['Heading and badges', 'Bars', 'Values and axes', 'Uncertainty', 'Footer', 'Layout']);
  });

  it('renders the trade-off set for a scatter and not the bar set', () => {
    render('scatter');
    expect(host().querySelector('#mc-style-scatter-heading')?.textContent).toContain('Trade-off charts — all three');
    expect(host().querySelector('#mc-style-bar-heading')).toBeNull();
    expect(sectionTitles()).toEqual(['Heading and badges', 'Marks and frontier', 'Labels and legend', 'Axes', 'Uncertainty', 'Footer']);
  });

  it('renders the caption sections, the note and a reset button for the profile', () => {
    render('profile');
    expect(host().querySelector('#mc-style-profile-heading')?.textContent).toContain('Profile plot');
    const inputs = Array.from(host().querySelectorAll<HTMLInputElement>('input'));
    expect(sectionTitles()).toEqual(['Heading and badges', 'Footer']);
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
      profile: { ...DEFAULT_FIGURE_STYLE.profile, hiddenBadges: ['questions', 'pricing'] }
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
      profile: DEFAULT_FIGURE_STYLE.profile
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
    expect(JSON.parse(localStorage.getItem(FIGURE_STYLE_PANEL_OPEN_KEY)!).scatter.length).toBe(6);

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
});
