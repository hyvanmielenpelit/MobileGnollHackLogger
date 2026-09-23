import { ComponentFixture, TestBed } from '@angular/core/testing';

import { FigureStylePanelComponent, FigureStylePanelKind } from './figure-style-panel.component';
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

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [FigureStylePanelComponent] }).compileComponents();
    fixture = TestBed.createComponent(FigureStylePanelComponent);
    emitted = [];
    fixture.componentInstance.figureStyleChange.subscribe(style => emitted.push(style));
  });

  it('renders the bar set for a bar figure and not the trade-off set', () => {
    render('bar');
    expect(host().querySelector('#mc-style-bar-heading')?.textContent).toContain('Bar charts — Intelligence, Speed and Cost');
    expect(host().querySelector('#mc-style-scatter-heading')).toBeNull();
    const legends = Array.from(host().querySelectorAll('fieldset.gh-fieldset > legend')).map(l => l.textContent!.trim());
    expect(legends).toEqual(['Bars', 'Uncertainty', 'Text', 'Layout']);
  });

  it('renders the trade-off set for a scatter and not the bar set', () => {
    render('scatter');
    expect(host().querySelector('#mc-style-scatter-heading')?.textContent).toContain('Trade-off charts — all three');
    expect(host().querySelector('#mc-style-bar-heading')).toBeNull();
    const legends = Array.from(host().querySelectorAll('fieldset.gh-fieldset > legend')).map(l => l.textContent!.trim());
    expect(legends).toEqual(['Marks', 'Uncertainty and frontier', 'Labels', 'Text', 'Layout']);
  });

  it('renders only the note for the profile', () => {
    render('profile');
    expect(host().querySelectorAll('input').length).toBe(0);
    expect(host().textContent).toContain('The profile plot has no style controls of its own.');
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
    expect(hintOf(filled)).toContain('n = 1');

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
      scatter: { ...DEFAULT_FIGURE_STYLE.scatter, markRadiusPx: 12, intervals: false, dominatedShading: false, frontierIntervalsNote: false }
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
    for (const kind of ['bar', 'scatter'] as const) {
      render(kind);
      const inputs = Array.from(host().querySelectorAll('input'));
      expect(inputs.length).withContext(kind).toBeGreaterThan(5);
      for (const input of inputs) {
        expect(input.id).withContext(`${kind} ${input.type}`).toMatch(/^mc-style-/);
        const label = input.closest('label') ?? host().querySelector(`label[for="${input.id}"]`);
        expect(label?.textContent?.trim()).withContext(input.id).toBeTruthy();
      }
      const ids = Array.from(host().querySelectorAll('[id]')).map(element => element.id);
      expect(new Set(ids).size).withContext(kind).toBe(ids.length);
    }
  });
});
