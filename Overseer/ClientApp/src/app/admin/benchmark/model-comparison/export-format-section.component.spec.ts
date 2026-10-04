import { ComponentFixture, TestBed } from '@angular/core/testing';

import { ExportFormatSectionComponent, exportFormatReadout } from './export-format-section.component';
import { FigureExportFormat, WebpQuality } from './figure-export';

describe('ExportFormatSectionComponent', () => {
  let fixture: ComponentFixture<ExportFormatSectionComponent>;
  let formats: FigureExportFormat[];
  let qualities: WebpQuality[];
  let opens: boolean[];
  let resets: number;

  function host(): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  function byId<T extends HTMLElement>(id: string): T | null {
    return host().querySelector<T>(`#${id}`);
  }

  function render(input: { format?: FigureExportFormat; webpQuality?: WebpQuality; note?: string; idPrefix?: string } = {}): void {
    fixture.componentRef.setInput('idPrefix', input.idPrefix ?? 'kf-fmt');
    fixture.componentRef.setInput('format', input.format ?? 'png');
    fixture.componentRef.setInput('webpQuality', input.webpQuality ?? 85);
    fixture.componentRef.setInput('note', input.note ?? '');
    fixture.componentRef.setInput('open', true);
    fixture.detectChanges();
  }

  /** Feeds the last emitted values back in, the way a host applies a change. */
  function accept(): void {
    if (formats.length > 0) {
      fixture.componentRef.setInput('format', formats[formats.length - 1]);
    }
    if (qualities.length > 0) {
      fixture.componentRef.setInput('webpQuality', qualities[qualities.length - 1]);
    }
    fixture.detectChanges();
  }

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [ExportFormatSectionComponent] }).compileComponents();
    fixture = TestBed.createComponent(ExportFormatSectionComponent);
    formats = [];
    qualities = [];
    opens = [];
    resets = 0;
    fixture.componentInstance.formatChange.subscribe(value => formats.push(value));
    fixture.componentInstance.qualityChange.subscribe(value => qualities.push(value));
    fixture.componentInstance.openChange.subscribe(value => opens.push(value));
    fixture.componentInstance.reset.subscribe(() => resets++);
  });

  it('reads out PNG, or WebP with its quality', () => {
    expect(exportFormatReadout('png', 90)).toBe('PNG');
    expect(exportFormatReadout('webp', 85)).toBe('WebP · quality 85');
  });

  it('is a settings section whose format is a legended radio group, with every id from the prefix', () => {
    render({ note: 'Copy always places a PNG on the clipboard.' });
    const details = byId<HTMLDetailsElement>('kf-fmt-section')!;
    expect(details.classList).toContain('gh-disclosure--section');
    expect(details.querySelector('.gh-disclosure-summary-title')?.textContent?.trim()).toBe('Image format');
    const readout = details.querySelector('.gh-disclosure-summary-value') as HTMLElement;
    expect(readout.getAttribute('aria-hidden')).toBe('true');
    expect(readout.textContent?.trim()).toBe('PNG');

    const group = host().querySelector('fieldset.gh-choice') as HTMLFieldSetElement;
    expect(group.querySelector('legend')?.textContent?.trim()).toBe('Format');
    const radios = Array.from(group.querySelectorAll<HTMLInputElement>('label.gh-radio input[type="radio"]'));
    expect(radios.map(radio => radio.id)).toEqual(['kf-fmt-format-png', 'kf-fmt-format-webp']);
    expect(radios.map(radio => radio.checked)).toEqual([true, false]);
    expect(new Set(radios.map(radio => radio.name)).size).toBe(1);
    expect(group.getAttribute('aria-describedby')).toBe('kf-fmt-note');
    expect(byId('kf-fmt-note')?.textContent?.trim()).toBe('Copy always places a PNG on the clipboard.');

    for (const element of Array.from(host().querySelectorAll<HTMLElement>('[id]'))) {
      expect(element.id.startsWith('kf-fmt-'), element.id).toBe(true);
    }
  });

  it('shows the WebP quality only while WebP is chosen', () => {
    render();
    expect(byId('kf-fmt-quality')).toBeNull();

    byId<HTMLInputElement>('kf-fmt-format-webp')!.click();
    expect(formats).toEqual(['webp']);
    accept();
    const quality = byId<HTMLSelectElement>('kf-fmt-quality')!;
    expect(host().querySelector('label[for="kf-fmt-quality"]')?.textContent?.trim()).toBe('WebP quality');
    expect(Array.from(quality.options).map(option => option.textContent?.trim())).toEqual(['75', '80', '85', '90', '95', '100']);
    expect(quality.value).toBe('85');
    expect(host().querySelector('.gh-disclosure-summary-value')?.textContent?.trim()).toBe('WebP · quality 85');

    quality.value = '95';
    quality.dispatchEvent(new Event('change'));
    expect(qualities).toEqual([95]);
  });

  it('leaves the note out when none is given', () => {
    render();
    expect(byId('kf-fmt-note')).toBeNull();
    expect(host().querySelector('fieldset.gh-choice')?.hasAttribute('aria-describedby')).toBe(false);
  });

  it('resets from a changed format, announces it, and refuses at the defaults', () => {
    render();
    const reset = byId<HTMLButtonElement>('kf-fmt-reset')!;
    expect(reset.getAttribute('type')).toBe('button');
    expect(reset.getAttribute('aria-label')).toBe('Reset Image format to defaults');
    expect(reset.getAttribute('aria-disabled')).toBe('true');
    expect(reset.getAttribute('interestfor')).toBe('kf-fmt-reset-tip');
    expect(byId('kf-fmt-reset-tip')?.getAttribute('popover')).toBe('hint');
    reset.click();
    expect(resets).toBe(0);

    render({ format: 'webp', webpQuality: 90 });
    expect(reset.getAttribute('aria-disabled')).toBeNull();
    reset.click();
    fixture.detectChanges();
    expect(resets).toBe(1);
    expect(host().querySelector('p.visually-hidden[role="status"]')?.textContent?.trim()).toBe('Image format reset to defaults.');

    // The host applies the reset; the announcement stays until a later change.
    fixture.componentRef.setInput('format', 'png');
    fixture.componentRef.setInput('webpQuality', 85);
    fixture.detectChanges();
    expect(fixture.componentInstance.resetStatus).toBe('Image format reset to defaults.');
    fixture.componentRef.setInput('format', 'webp');
    fixture.detectChanges();
    expect(fixture.componentInstance.resetStatus).toBe('');
  });

  it('mirrors the disclosure open state through openChange', () => {
    render();
    const details = byId<HTMLDetailsElement>('kf-fmt-section')!;
    details.open = false;
    details.dispatchEvent(new Event('toggle'));
    expect(opens).toEqual([false]);
  });
});
