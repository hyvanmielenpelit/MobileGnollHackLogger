import { Component, ViewChild } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';

import { KeyFigureChoice, KeyFiguresChooserComponent, KeyFiguresChooserExport } from './key-figures-chooser.component';

@Component({
  standalone: true,
  imports: [KeyFiguresChooserComponent],
  template: `
    <dialog class="outer-dialog" (cancel)="events.push('cancel')" (close)="events.push('close')" (click)="events.push('click')">
      <button type="button" class="opener">Choose figures</button>
      <app-key-figures-chooser (exportRequested)="exports.push($event)"></app-key-figures-chooser>
    </dialog>`
})
class HostComponent {
  readonly events: string[] = [];
  readonly exports: KeyFiguresChooserExport[] = [];
  @ViewChild(KeyFiguresChooserComponent) chooser!: KeyFiguresChooserComponent;
}

const FIGURES: KeyFigureChoice[] = [
  { key: 'intelligence', label: 'Intelligence Index', value: '83 / 100' },
  { key: 'mean-time', label: 'Mean Time per Question', value: '17.2 s' },
  { key: 'estimated-cost', label: 'Estimated Cost', value: '$3.2322*' }
];

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

describe('KeyFiguresChooserComponent', () => {
  let fixture: ComponentFixture<HostComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [HostComponent] }).compileComponents();
    fixture = TestBed.createComponent(HostComponent);
    fixture.detectChanges();
    outer().showModal();
  });

  afterEach(() => {
    for (const dialog of Array.from(document.querySelectorAll('dialog[open]')) as HTMLDialogElement[]) {
      dialog.close();
    }
  });

  function root(): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  function outer(): HTMLDialogElement {
    return root().querySelector('dialog.outer-dialog') as HTMLDialogElement;
  }

  function dialog(): HTMLDialogElement {
    return root().querySelector('app-key-figures-chooser dialog') as HTMLDialogElement;
  }

  function opener(): HTMLButtonElement {
    return root().querySelector('.opener') as HTMLButtonElement;
  }

  function open(excluded: string[] = []): void {
    fixture.componentInstance.chooser.open(FIGURES, excluded, opener());
    fixture.detectChanges();
  }

  function boxes(): HTMLInputElement[] {
    return Array.from(dialog().querySelectorAll<HTMLInputElement>('input[type="checkbox"]'));
  }

  function checkedKeys(): string[] {
    return boxes().filter(box => box.checked).map(box => box.id.replace(/^kfch-/, ''));
  }

  function status(): string {
    return (dialog().querySelector('[role="status"]')?.textContent || '').trim();
  }

  function button(selector: string): HTMLButtonElement {
    return dialog().querySelector(selector) as HTMLButtonElement;
  }

  function click(element: HTMLElement): void {
    element.click();
    fixture.detectChanges();
  }

  it('keeps a closed, light-dismiss dialog labelled by its title', () => {
    expect(dialog().open).toBeFalse();
    expect(dialog().classList).toContain('gh-dialog');
    expect(dialog().getAttribute('closedby')).toBe('any');
    expect(dialog().getAttribute('aria-labelledby')).toBe('kfchTitle');
    const title = dialog().querySelector('#kfchTitle') as HTMLElement;
    expect(title.tagName).toBe('H3');
    expect(title.getAttribute('tabindex')).toBe('-1');
    expect(title.textContent?.trim()).toBe('Key Figures in the Image');
  });

  it('opens modally over the dialog it sits in and focuses its title', () => {
    open();
    expect(dialog().open).toBeTrue();
    expect(dialog().matches(':modal')).toBeTrue();
    expect(document.activeElement).toBe(dialog().querySelector('#kfchTitle'));
  });

  it('lists one row per figure, in order, with its label and muted value, in a captioned group', () => {
    open(['mean-time', 'panel']);

    const group = dialog().querySelector('[role="group"]') as HTMLElement;
    expect(group.getAttribute('aria-labelledby')).toBe('kfchCaption');
    expect(dialog().querySelector('#kfchCaption')?.textContent?.trim()).toBe('Figures');

    const rows = Array.from(group.querySelectorAll<HTMLElement>('li'));
    expect(rows.map(row => row.getAttribute('data-figure'))).toEqual(['intelligence', 'mean-time', 'estimated-cost']);
    expect(rows[0].querySelector('label.checkbox-label')?.textContent?.replace(/\s+/g, ' ').trim())
      .toBe('Intelligence Index — 83 / 100');
    expect(rows[0].querySelector('.kfch-value')?.textContent?.trim()).toBe('— 83 / 100');
    expect(checkedKeys()).toEqual(['intelligence', 'estimated-cost']);
    expect(status()).toBe('2 of 3 selected');
    expect(dialog().querySelector('.kfch-hint')?.textContent?.trim())
      .toBe('Remembered for every run report. A figure a run does not have is left out.');
  });

  it('selects all and none, naming both links fully for assistive technology', () => {
    open(['mean-time']);
    const all = button('.kfch-all');
    const none = button('.kfch-none');
    expect(all.textContent?.trim()).toBe('All key figures');
    expect(none.textContent?.trim()).toBe('None of the key figures');
    expect(all.querySelector('.visually-hidden')).not.toBeNull();

    click(all);
    expect(checkedKeys()).toEqual(['intelligence', 'mean-time', 'estimated-cost']);
    expect(status()).toBe('3 of 3 selected');

    click(none);
    expect(checkedKeys()).toEqual([]);
    expect(status()).toBe('0 of 3 selected');
  });

  it('with nothing selected, marks both exports aria-disabled with a visible reason, and refuses them', () => {
    open();
    expect(dialog().querySelector('#kfchReason')).toBeNull();
    click(button('.kfch-none'));

    const reason = dialog().querySelector('#kfchReason') as HTMLElement;
    expect(reason.classList).toContain('gh-field-error');
    expect(reason.textContent?.trim()).toBe('Select at least one figure.');
    for (const selector of ['.kfch-copy', '.kfch-download']) {
      expect(button(selector).getAttribute('aria-disabled')).withContext(selector).toBe('true');
      expect(button(selector).getAttribute('aria-describedby')).withContext(selector).toBe('kfchReason');
      expect(button(selector).disabled).withContext(selector).toBeFalse();
    }

    click(button('.kfch-copy'));
    click(button('.kfch-download'));
    expect(fixture.componentInstance.exports).toEqual([]);
    expect(dialog().open).toBeTrue();
  });

  it('commits the draft on Copy Image, keeping exclusions of figures this run does not show', async () => {
    open(['panel']);
    click(boxes()[1]);
    expect(status()).toBe('2 of 3 selected');

    const closed = nextEvent(dialog(), 'close');
    click(button('.kfch-copy'));
    expect(fixture.componentInstance.exports).toEqual([{ action: 'copy', excluded: ['panel', 'mean-time'] }]);
    await closed;
    expect(dialog().open).toBeFalse();
    expect(document.activeElement).toBe(opener());
    expect(outer().open).toBeTrue();
    expect(fixture.componentInstance.events).not.toContain('close');
  });

  it('commits the draft on Download PNG', async () => {
    open(['estimated-cost']);
    click(button('.kfch-all'));
    const closed = nextEvent(dialog(), 'close');
    click(button('.kfch-download'));
    expect(fixture.componentInstance.exports).toEqual([{ action: 'download', excluded: [] }]);
    await closed;
    expect(dialog().open).toBeFalse();
  });

  it('discards the draft on Cancel, and starts from the stored choice when opened again', async () => {
    open(['mean-time']);
    click(button('.kfch-none'));
    const closed = nextEvent(dialog(), 'close');
    click(button('.kfch-cancel'));
    await closed;
    expect(dialog().open).toBeFalse();
    expect(fixture.componentInstance.exports).toEqual([]);
    expect(document.activeElement).toBe(opener());

    open(['mean-time']);
    expect(checkedKeys()).toEqual(['intelligence', 'estimated-cost']);
  });

  it('stops its own close, cancel and click events short of the dialog around it', async () => {
    open();
    const eventsBefore = fixture.componentInstance.events.length;
    click(dialog().querySelector('.kfch-hint') as HTMLElement);
    const closed = nextEvent(dialog(), 'close');
    dialog().dispatchEvent(new Event('cancel', { cancelable: true }));
    dialog().close();
    await closed;
    expect(fixture.componentInstance.events.length).toBe(eventsBefore);
    expect(outer().open).toBeTrue();
  });

  it('puts Cancel first in the footer, text only, and the two exports after it with their glyphs', () => {
    open();
    const footer = Array.from(dialog().querySelectorAll<HTMLButtonElement>('.dialog-footer button'));
    expect(footer.map(b => b.textContent?.trim())).toEqual(['Cancel', 'Copy Image', 'Download PNG']);
    expect(footer[0].classList).toContain('btn-gh-cancel');
    expect(footer[0].querySelector('svg')).toBeNull();
    for (const exportButton of footer.slice(1)) {
      expect(exportButton.classList).toContain('btn-gh');
      expect(exportButton.getAttribute('type')).toBe('button');
      expect(exportButton.querySelector('svg.btn-icon')?.getAttribute('aria-hidden')).toBe('true');
    }
  });
});
