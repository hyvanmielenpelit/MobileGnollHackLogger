import { ComponentFixture, TestBed } from '@angular/core/testing';

import { ExportSizeSectionComponent } from './export-size-section.component';
import { FIT_RESOLUTION_ID, FigureSizeSettings, defaultFigureSize, defaultTableImageSize } from './figure-size';

describe('ExportSizeSectionComponent', () => {
  let fixture: ComponentFixture<ExportSizeSectionComponent>;
  let emittedSettings: FigureSizeSettings[];
  let emittedOpen: boolean[];
  let resetCount: number;

  function host(): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  function control<T extends HTMLElement>(id: string): T {
    const element = host().querySelector<T>(`#${id}`);
    expect(element).withContext(id).not.toBeNull();
    return element!;
  }

  function maybeControl<T extends HTMLElement>(id: string): T | null {
    return host().querySelector<T>(`#${id}`);
  }

  function render(input: {
    settings: FigureSizeSettings;
    defaults: FigureSizeSettings;
    allowFit?: boolean;
    title?: string;
    idPrefix?: string;
    fitInfo?: string;
    textSizeHint?: string;
    refusal?: string;
    errorNoun?: string;
  }): void {
    fixture.componentRef.setInput('title', input.title ?? 'Figure size');
    fixture.componentRef.setInput('idPrefix', input.idPrefix ?? 'mc-export');
    fixture.componentRef.setInput('settings', input.settings);
    fixture.componentRef.setInput('defaults', input.defaults);
    fixture.componentRef.setInput('allowFit', input.allowFit ?? false);
    fixture.componentRef.setInput('displayDensity', 1);
    fixture.componentRef.setInput('fitInfo', input.fitInfo ?? '');
    fixture.componentRef.setInput('textSizeHint', input.textSizeHint ?? '');
    fixture.componentRef.setInput('refusal', input.refusal ?? '');
    fixture.componentRef.setInput('errorNoun', input.errorNoun ?? 'figure');
    fixture.detectChanges();
  }

  /** Feeds the last emitted settings back in, the way a host applies the change. */
  function acceptLast(): void {
    fixture.componentRef.setInput('settings', emittedSettings[emittedSettings.length - 1]);
    fixture.detectChanges();
  }

  function setSelect(select: HTMLSelectElement, value: string): void {
    select.value = value;
    select.dispatchEvent(new Event('change'));
    fixture.detectChanges();
  }

  function setNumber(input: HTMLInputElement, value: number): void {
    input.value = String(value);
    input.dispatchEvent(new Event('input'));
    input.dispatchEvent(new Event('change'));
    fixture.detectChanges();
  }

  function setChecked(input: HTMLInputElement, on: boolean): void {
    input.checked = on;
    input.dispatchEvent(new Event('change'));
    fixture.detectChanges();
  }

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [ExportSizeSectionComponent] }).compileComponents();
    fixture = TestBed.createComponent(ExportSizeSectionComponent);
    emittedSettings = [];
    emittedOpen = [];
    resetCount = 0;
    fixture.componentInstance.settingsChange.subscribe(settings => emittedSettings.push(settings));
    fixture.componentInstance.openChange.subscribe(open => emittedOpen.push(open));
    fixture.componentInstance.reset.subscribe(() => resetCount++);
  });

  it('reproduces the wizard\'s ids under the mc-export prefix', () => {
    render({ settings: defaultFigureSize(1), defaults: defaultFigureSize(1) });
    expect(control('mc-export-resolution')).not.toBeNull();
    expect(control('mc-export-density')).not.toBeNull();
    expect(control('mc-export-text-scale')).not.toBeNull();
    // Custom fields only render in custom mode.
    expect(maybeControl('mc-export-width')).toBeNull();
    expect(maybeControl('mc-export-height')).toBeNull();
  });

  it('has no Fit the table option when allowFit is off', () => {
    render({ settings: defaultFigureSize(1), defaults: defaultFigureSize(1), allowFit: false });
    const options = Array.from(control<HTMLSelectElement>('mc-export-resolution').options).map(o => o.value);
    expect(options).not.toContain(FIT_RESOLUTION_ID);
  });

  it('offers Fit the table first when allowFit is on', () => {
    render({
      settings: defaultTableImageSize(),
      defaults: defaultTableImageSize(),
      allowFit: true,
      idPrefix: 'mc-table-image'
    });
    const select = control<HTMLSelectElement>('mc-table-image-resolution');
    expect(select.options[0].value).toBe(FIT_RESOLUTION_ID);
    expect(select.value).toBe(FIT_RESOLUTION_ID);
  });

  it('hides Text size in fit mode and shows it once a size is chosen', () => {
    render({
      settings: defaultTableImageSize(),
      defaults: defaultTableImageSize(),
      allowFit: true,
      idPrefix: 'mc-table-image'
    });
    expect(maybeControl('mc-table-image-text-scale')).toBeNull();
    expect(host().textContent).toContain('The image is as large as the table');

    setSelect(control<HTMLSelectElement>('mc-table-image-resolution'), 'fullhd');
    expect(emittedSettings[0].resolutionId).toBe('fullhd');
    acceptLast();
    expect(control('mc-table-image-text-scale')).not.toBeNull();
  });

  it('shows fitInfo only in custom mode, and references it from width and height', () => {
    render({
      settings: { ...defaultTableImageSize(), resolutionId: 'custom' },
      defaults: defaultTableImageSize(),
      allowFit: true,
      idPrefix: 'mc-table-image',
      fitInfo: 'Fit the table: 1480 × 620 px at this text size — the whole table with its 8 shown columns and 15 rows.'
    });
    const info = control('mc-table-image-fit-info');
    expect(info.getAttribute('role')).toBe('status');
    const width = control<HTMLInputElement>('mc-table-image-width');
    const height = control<HTMLInputElement>('mc-table-image-height');
    expect(width.getAttribute('aria-describedby')).toContain('mc-table-image-fit-info');
    expect(height.getAttribute('aria-describedby')).toContain('mc-table-image-fit-info');
  });

  it('does not show fitInfo when the resolution is not custom, even if allowFit and fitInfo are set', () => {
    render({
      settings: defaultTableImageSize(),
      defaults: defaultTableImageSize(),
      allowFit: true,
      idPrefix: 'mc-table-image',
      fitInfo: 'Fit the table: 1480 × 620 px.'
    });
    expect(maybeControl('mc-table-image-fit-info')).toBeNull();
  });

  it('emits a whole new settings object on a resolution change', () => {
    render({ settings: defaultFigureSize(1), defaults: defaultFigureSize(1) });
    setSelect(control<HTMLSelectElement>('mc-export-resolution'), 'custom');
    expect(emittedSettings.length).toBe(1);
    expect(emittedSettings[0].resolutionId).toBe('custom');
    // Every other field survives untouched, proving the emission is the whole settings object.
    expect(emittedSettings[0].densitySelection).toBe(defaultFigureSize(1).densitySelection);
  });

  it('keeps the aspect ratio locked while typing a custom width or height', () => {
    render({ settings: { ...defaultFigureSize(1), resolutionId: 'custom' }, defaults: defaultFigureSize(1) });
    // The lock checkbox has no id of its own; select it structurally instead.
    const lockCheckbox = host().querySelector<HTMLInputElement>('.checkbox-label input[type="checkbox"]')!;
    setChecked(lockCheckbox, true);

    const width = control<HTMLInputElement>('mc-export-width');
    setNumber(width, 3840);
    expect(emittedSettings[emittedSettings.length - 1].customWidthPx).toBe(3840);
    expect(emittedSettings[emittedSettings.length - 1].customHeightPx).toBe(2160);
  });

  it('reports the custom width and height error via errorNoun', () => {
    render({
      settings: { ...defaultFigureSize(1), resolutionId: 'custom', customWidthPx: 100 },
      defaults: defaultFigureSize(1),
      idPrefix: 'mc-export'
    });
    const error = control('mc-export-resolution-error');
    expect(error.getAttribute('role')).toBe('alert');
    expect(error.textContent).toContain('figure width');
  });

  it('uses errorNoun for the table instance', () => {
    render({
      settings: { ...defaultTableImageSize(), resolutionId: 'custom', customWidthPx: 100 },
      defaults: defaultTableImageSize(),
      allowFit: true,
      idPrefix: 'mc-table-image',
      errorNoun: 'image'
    });
    const error = control('mc-table-image-resolution-error');
    expect(error.textContent).toContain('image width');
  });

  it('shows the refusal as an alert when given', () => {
    render({
      settings: defaultFigureSize(1),
      defaults: defaultFigureSize(1),
      refusal: 'The table needs more room than this box allows.'
    });
    const alerts = Array.from(host().querySelectorAll('[role="alert"]'));
    const refusalAlert = alerts.find(el => el.textContent === 'The table needs more room than this box allows.');
    expect(refusalAlert).toBeTruthy();
  });

  it('disables the reset button at defaults and enables it once changed', () => {
    render({ settings: defaultFigureSize(1), defaults: defaultFigureSize(1) });
    const reset = control<HTMLButtonElement>('mc-export-reset');
    expect(reset.getAttribute('aria-disabled')).toBe('true');

    reset.click();
    fixture.detectChanges();
    expect(resetCount).toBe(0);

    setSelect(control<HTMLSelectElement>('mc-export-resolution'), 'hd');
    acceptLast();
    const resetAfter = control<HTMLButtonElement>('mc-export-reset');
    expect(resetAfter.getAttribute('aria-disabled')).toBeNull();

    resetAfter.click();
    fixture.detectChanges();
    expect(resetCount).toBe(1);
  });

  it('announces the reset and clears the announcement on the next distinct settings change', () => {
    render({ settings: { ...defaultFigureSize(1), resolutionId: 'hd' }, defaults: defaultFigureSize(1) });
    const reset = control<HTMLButtonElement>('mc-export-reset');
    reset.click();
    fixture.detectChanges();
    expect(fixture.componentInstance.resetStatus).toBe('Figure size reset to defaults.');

    // The host applies the reset by pushing the defaults back in — this must not clear the status.
    fixture.componentRef.setInput('settings', defaultFigureSize(1));
    fixture.detectChanges();
    expect(fixture.componentInstance.resetStatus).toBe('Figure size reset to defaults.');

    // A later, distinct settings change clears it.
    fixture.componentRef.setInput('settings', { ...defaultFigureSize(1), resolutionId: 'hd' });
    fixture.detectChanges();
    expect(fixture.componentInstance.resetStatus).toBe('');
  });

  it('mirrors the disclosure open state through openChange', () => {
    render({ settings: defaultFigureSize(1), defaults: defaultFigureSize(1) });
    const details = control<HTMLDetailsElement>('mc-export-section');
    details.open = true;
    details.dispatchEvent(new Event('toggle'));
    fixture.detectChanges();
    expect(emittedOpen).toEqual([true]);
  });
});
