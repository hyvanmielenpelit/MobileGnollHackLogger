import { Component, ViewChild } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';

import { CcResultsImageItem } from './results-image-blocks';
import { CcResultsImageDialogComponent, CcResultsImageItems } from './results-image-dialog.component';
import {
  CC_RESULTS_IMAGE_SECTIONS_STORAGE_KEY,
  CcResultsImageSettings,
  defaultCcResultsImageSettings
} from './results-image-settings';

@Component({
  standalone: true,
  imports: [CcResultsImageDialogComponent],
  template: `
    <dialog class="outer-dialog" (cancel)="events.push('cancel')" (close)="events.push('close')" (click)="events.push('click')">
      <button type="button" class="opener">Image settings</button>
      <app-cc-results-image-dialog (settingsChange)="changes.push($event)"></app-cc-results-image-dialog>
    </dialog>`
})
class HostComponent {
  readonly events: string[] = [];
  readonly changes: CcResultsImageSettings[] = [];
  @ViewChild(CcResultsImageDialogComponent) dialog!: CcResultsImageDialogComponent;
}

const item = (key: string, label: string, value = ''): CcResultsImageItem => ({ key, label, value });

const ITEMS: CcResultsImageItems = {
  summary: [item('verdict', 'Verdict and detail', 'The chat changed'), item('model', 'Model and compared set'), item('chips', 'Endpoint chips')],
  verdicts: [item('endpoint-P1', 'P1 Quality', 'Within margin'), item('endpoint-P2', 'P2 Time to first answer text', 'Changed'), item('more', 'More about details')],
  periods: [item('baseline', 'Baseline card'), item('comparison', 'Comparison card'), item('units', 'Runs in the periods')],
  attribution: [item('changes', 'Decisive changes'), item('groups', 'Attribution by side'), item('unattributed', 'Unattributed note')],
  nextRuns: [item('group-control:comparison', 'Run another provider\'s model', 'Comparison'), item('suggestions', 'Suggestions'), item('reasons', 'Reasons')],
  details: [item('limitations', 'Limitations'), item('identity', 'Analysis identity')]
};

/** Resolves on the next event of a type, or after a second so a missing event fails the expectations. */
function nextEvent(target: EventTarget, type: string): Promise<Event | null> {
  return new Promise(resolve => {
    const timer = setTimeout(() => resolve(null), 1000);
    target.addEventListener(type, event => {
      clearTimeout(timer);
      resolve(event);
    }, { once: true });
  });
}

describe('CcResultsImageDialogComponent', () => {
  let fixture: ComponentFixture<HostComponent>;

  beforeEach(async () => {
    localStorage.removeItem(CC_RESULTS_IMAGE_SECTIONS_STORAGE_KEY);
    await TestBed.configureTestingModule({ imports: [HostComponent] }).compileComponents();
    fixture = TestBed.createComponent(HostComponent);
    fixture.detectChanges();
    outer().showModal();
  });

  afterEach(() => {
    for (const dialog of Array.from(document.querySelectorAll('dialog[open]')) as HTMLDialogElement[]) {
      dialog.close();
    }
    localStorage.removeItem(CC_RESULTS_IMAGE_SECTIONS_STORAGE_KEY);
  });

  const root = (): HTMLElement => fixture.nativeElement as HTMLElement;
  const outer = (): HTMLDialogElement => root().querySelector('dialog.outer-dialog') as HTMLDialogElement;
  const dialog = (): HTMLDialogElement => root().querySelector('app-cc-results-image-dialog dialog') as HTMLDialogElement;
  const opener = (): HTMLButtonElement => root().querySelector('.opener') as HTMLButtonElement;
  const component = (): CcResultsImageDialogComponent => fixture.componentInstance.dialog;
  const panel = (section: string): HTMLElement => dialog().querySelector(`#cc-rim-panel-${section}`) as HTMLElement;
  const textOf = (element: Element | null | undefined): string => (element?.textContent || '').replace(/\s+/g, ' ').trim();
  const checked = (section: string): string[] => Array.from(panel(section).querySelectorAll<HTMLInputElement>('input[type="checkbox"]'))
    .filter(box => box.checked).map(box => box.closest('li')!.getAttribute('data-item')!);

  function open(section: 'summary' | 'verdicts' | 'nextRuns' = 'verdicts', settings: CcResultsImageSettings = defaultCcResultsImageSettings()): void {
    component().open(section, ITEMS, settings, opener());
    fixture.detectChanges();
  }

  function click(element: HTMLElement): void {
    element.click();
    fixture.detectChanges();
  }

  it('keeps a closed, light-dismiss dialog labelled by its title', () => {
    expect(dialog().open).toBe(false);
    expect(dialog().classList).toContain('gh-dialog');
    expect(dialog().getAttribute('closedby')).toBe('any');
    expect(dialog().getAttribute('aria-labelledby')).toBe('cc-rim-title');
    expect(textOf(dialog().querySelector('h3#cc-rim-title'))).toBe('Image settings');
  });

  it('opens modally on the section it was opened from, and focuses its title', () => {
    open('verdicts');
    expect(dialog().open).toBe(true);
    expect(dialog().matches(':modal')).toBe(true);
    expect(document.activeElement).toBe(dialog().querySelector('#cc-rim-title'));
    const tabs = Array.from(dialog().querySelectorAll<HTMLButtonElement>('.cc-rim-tabs [role="tab"]'));
    expect(tabs.map(tab => textOf(tab))).toEqual(['Summary', 'Verdicts', 'Periods', 'Attribution', 'Next runs', 'Details']);
    expect(tabs.map(tab => tab.getAttribute('aria-selected'))).toEqual(['false', 'true', 'false', 'false', 'false', 'false']);
    expect(tabs.map(tab => tab.getAttribute('tabindex'))).toEqual(['-1', '0', '-1', '-1', '-1', '-1']);
    expect(panel('verdicts').hidden).toBe(false);
    expect(panel('summary').hidden).toBe(true);
    expect(panel('verdicts').getAttribute('aria-labelledby')).toBe('cc-rim-tab-verdicts');
  });

  it('shows each section\'s checklist, checked unless excluded, with its count', () => {
    open('verdicts');
    expect(checked('verdicts')).toEqual(['endpoint-P1', 'endpoint-P2']);
    expect(textOf(panel('verdicts').querySelector('[role="status"]'))).toBe('2 of 3 selected');
    expect(textOf(panel('verdicts').querySelector('li[data-item="endpoint-P2"] label'))).toBe('P2 Time to first answer text — Changed');

    click(dialog().querySelector('#cc-rim-tab-nextRuns') as HTMLButtonElement);
    expect(panel('nextRuns').hidden).toBe(false);
    expect(checked('nextRuns')).toEqual(['group-control:comparison', 'suggestions']);
  });

  it('moves between the sections with the arrow keys, Home and End', () => {
    open('summary');
    const tab = (id: string) => dialog().querySelector(`#cc-rim-tab-${id}`) as HTMLButtonElement;
    const key = (target: HTMLElement, name: string) => {
      const event = new KeyboardEvent('keydown', { key: name, bubbles: true, cancelable: true });
      target.dispatchEvent(event);
      fixture.detectChanges();
      return event;
    };
    expect(key(tab('summary'), 'ArrowLeft').defaultPrevented).toBe(true);
    expect(component().section).toBe('details');
    expect(document.activeElement).toBe(tab('details'));
    key(tab('details'), 'Home');
    expect(component().section).toBe('summary');
    expect(key(tab('summary'), 'a').defaultPrevented).toBe(false);
  });

  it('applies every change at once: a toggle, All and None, named apart for assistive technology', () => {
    open('verdicts');
    const changes = fixture.componentInstance.changes;
    click(panel('verdicts').querySelector('li[data-item="endpoint-P1"] input') as HTMLInputElement);
    expect(changes.at(-1)!.excluded.verdicts).toEqual(['more', 'endpoint-P1']);
    expect(checked('verdicts')).toEqual(['endpoint-P2']);

    const all = panel('verdicts').querySelector('.cc-rim-all') as HTMLButtonElement;
    const none = panel('verdicts').querySelector('.cc-rim-none') as HTMLButtonElement;
    expect(textOf(all)).toBe('All of the Verdicts section');
    expect(textOf(none)).toBe('None of the Verdicts section');
    click(all);
    expect(changes.at(-1)!.excluded.verdicts).toEqual([]);
    expect(textOf(panel('verdicts').querySelector('[role="status"]'))).toBe('3 of 3 selected');
    click(none);
    expect(checked('verdicts')).toEqual([]);
    expect(textOf(panel('verdicts').querySelector('[role="status"]'))).toBe('0 of 3 selected');
    // Other sections are untouched.
    expect(changes.at(-1)!.excluded.nextRuns).toEqual(['reasons']);
  });

  it('keeps the exclusions of items this result does not have', () => {
    const settings = { ...defaultCcResultsImageSettings(), excluded: { ...defaultCcResultsImageSettings().excluded, verdicts: ['endpoint-P5', 'more'] } };
    open('verdicts', settings);
    click(panel('verdicts').querySelector('.cc-rim-all') as HTMLButtonElement);
    expect(fixture.componentInstance.changes.at(-1)!.excluded.verdicts).toEqual(['endpoint-P5']);
  });

  it('chooses the image details and the colors', () => {
    open('summary');
    const changes = fixture.componentInstance.changes;
    const details = Array.from(dialog().querySelectorAll<HTMLElement>('.cc-rim-group-details li')).map(row => textOf(row));
    expect(details).toEqual(['Header', 'Model line', 'Analysis line', 'Footer']);
    click(dialog().querySelector('#cc-rim-detail-footer') as HTMLInputElement);
    expect(changes.at(-1)!.detailsExcluded).toEqual(['footer']);
    expect(textOf(dialog().querySelector('.cc-rim-detail-count'))).toBe('3 of 4 selected');
    click(dialog().querySelector('.cc-rim-details-none') as HTMLButtonElement);
    expect(changes.at(-1)!.detailsExcluded).toEqual(['footer', 'header', 'model', 'analysis']);

    const scheme = dialog().querySelector('fieldset.gh-choice.cc-rim-scheme') as HTMLElement;
    expect(textOf(scheme.querySelector('legend'))).toBe('Colors');
    const radios = Array.from(scheme.querySelectorAll<HTMLInputElement>('input[type="radio"]'));
    expect(radios.map(radio => textOf(radio.closest('label')))).toEqual(['Dark, as on screen', 'Light, for print']);
    expect(radios[0].checked).toBe(true);
    click(radios[1]);
    expect(changes.at(-1)!.scheme).toBe('light');
  });

  it('holds the format and the size sections, with Fit the content and a text size', () => {
    open('summary');
    const file = dialog().querySelector('.cc-rim-file') as HTMLElement;
    expect(textOf(file.querySelector('h4'))).toBe('Image file');
    expect(textOf(file.querySelector('#cc-rim-fmt-note'))).toBe('Copy always places a PNG on the clipboard.');
    expect(textOf((file.querySelector('#cc-rim-size-resolution') as HTMLSelectElement).options[0])).toBe('Fit the content');
    expect(file.querySelector('#cc-rim-fmt-format-jpeg')).toBeNull();

    click(file.querySelector('#cc-rim-fmt-format-webp') as HTMLInputElement);
    expect(fixture.componentInstance.changes.at(-1)!.format).toBe('webp');
  });

  it('summarizes the next download from the host measurer, for the section shown', () => {
    const measured: string[] = [];
    component().measureImage = (settings, section) => {
      measured.push(section);
      return settings.size.resolutionId === 'fit' ? { widthPx: 2560, heightPx: 1890 } : { widthPx: 3840, heightPx: 2160 };
    };
    open('verdicts');
    const summary = dialog().querySelector('.dialog-footer p.cc-rim-summary') as HTMLElement;
    expect(textOf(summary)).toBe('Downloads a PNG, about 2560 × 1890 px.');
    expect(measured.at(-1)).toBe('verdicts');

    click(dialog().querySelector('#cc-rim-fmt-format-webp') as HTMLInputElement);
    const size = dialog().querySelector('#cc-rim-size-resolution') as HTMLSelectElement;
    size.value = 'uhd';
    size.dispatchEvent(new Event('change'));
    fixture.detectChanges();
    expect(textOf(summary)).toBe('Downloads a WebP, 3840 × 2160 px.');

    component().measureImage = () => ({ refusal: 'The Verdicts section does not fit 3840 × 2160 px. Choose Fit the content, a taller size, or include less.' });
    click(dialog().querySelector('#cc-rim-tab-periods') as HTMLButtonElement);
    expect(textOf(summary)).toBe('The Verdicts section does not fit 3840 × 2160 px. Choose Fit the content, a taller size, or include less.');
    expect(component().sizeRefusal).toBe(textOf(summary));

    component().measureImage = () => ({ empty: true });
    click(dialog().querySelector('#cc-rim-tab-summary') as HTMLButtonElement);
    expect(textOf(summary)).toBe('Nothing in the Summary section is selected.');
  });

  it('has a single text-only Done, which closes and returns focus', async () => {
    open();
    const footer = Array.from(dialog().querySelectorAll<HTMLButtonElement>('.dialog-footer button'));
    expect(footer.map(button => textOf(button))).toEqual(['Done']);
    expect(footer[0].classList).toContain('btn-gh-cancel');
    const closed = nextEvent(dialog(), 'close');
    click(footer[0]);
    await closed;
    expect(dialog().open).toBe(false);
    expect(document.activeElement).toBe(opener());
    expect(outer().open).toBe(true);
  });

  it('closes on its header close button, which has a name and a tooltip', async () => {
    open();
    const close = dialog().querySelector('.dialog-header button.btn-icon-action') as HTMLButtonElement;
    expect(close.getAttribute('aria-label')).toBe('Close image settings');
    expect(close.getAttribute('interestfor')).toBe('cc-rim-close-tip');
    expect(close.getAttribute('style')).toContain('anchor-name: --cc-rim-close-tip');
    const closed = nextEvent(dialog(), 'close');
    click(close);
    await closed;
    expect(dialog().open).toBe(false);
  });

  it('stops its own close, cancel and click events short of the dialog around it', async () => {
    open();
    const eventsBefore = fixture.componentInstance.events.length;
    click(dialog().querySelector('.cc-rim-hint') as HTMLElement);
    const closed = nextEvent(dialog(), 'close');
    dialog().dispatchEvent(new Event('cancel', { cancelable: true }));
    dialog().close();
    await closed;
    expect(fixture.componentInstance.events.length).toBe(eventsBefore);
    expect(outer().open).toBe(true);
  });

  it('remembers which file sections are open', () => {
    localStorage.setItem(CC_RESULTS_IMAGE_SECTIONS_STORAGE_KEY, JSON.stringify({ version: 1, format: false, size: true }));
    open();
    expect((dialog().querySelector('#cc-rim-fmt-section') as HTMLDetailsElement).open).toBe(false);
    const size = dialog().querySelector('#cc-rim-size-section') as HTMLDetailsElement;
    expect(size.open).toBe(true);
    size.open = false;
    size.dispatchEvent(new Event('toggle'));
    expect(JSON.parse(localStorage.getItem(CC_RESULTS_IMAGE_SECTIONS_STORAGE_KEY)!)).toEqual({ version: 1, format: false, size: false });
  });
});
