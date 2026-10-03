import { Component, ViewChild } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';

import { KeyFigureChoice, KeyFiguresChooserComponent } from './key-figures-chooser.component';

@Component({
  standalone: true,
  imports: [KeyFiguresChooserComponent],
  template: `
    <dialog class="outer-dialog" (cancel)="events.push('cancel')" (close)="events.push('close')" (click)="events.push('click')">
      <button type="button" class="opener">Choose figures</button>
      <app-key-figures-chooser (selectionChange)="changes.push($event)" (detailSelectionChange)="detailChanges.push($event)"></app-key-figures-chooser>
    </dialog>`
})
class HostComponent {
  readonly events: string[] = [];
  readonly changes: string[][] = [];
  readonly detailChanges: string[][] = [];
  @ViewChild(KeyFiguresChooserComponent) chooser!: KeyFiguresChooserComponent;
}

const FIGURES: KeyFigureChoice[] = [
  { key: 'intelligence', label: 'Intelligence Index', value: '83 / 100' },
  { key: 'mean-time', label: 'Mean Time per Question', value: '17.2 s' },
  { key: 'estimated-cost', label: 'Estimated Cost', value: '$3.2322*' }
];

const DETAILS: KeyFigureChoice[] = [
  { key: 'model', label: 'Model', value: 'GPT-6.1 Sol' },
  { key: 'assessor', label: 'Assessors', value: 'Claude 5 Opus + GPT-5.6 Sol' },
  { key: 'prompt', label: 'Prompt', value: 'Gameplay Help · concise (tools on) · snapshot' },
  { key: 'profile', label: 'Scoring profile', value: 'Default' },
  { key: 'started', label: 'Started', value: '2026-09-30 13:35:24 UTC' },
  { key: 'board', label: 'Board', value: 'Assessor 18/18' }
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
    expect(dialog().open).toBe(false);
    expect(dialog().classList).toContain('gh-dialog');
    expect(dialog().getAttribute('closedby')).toBe('any');
    expect(dialog().getAttribute('aria-labelledby')).toBe('kfchTitle');
    const title = dialog().querySelector('#kfchTitle') as HTMLElement;
    expect(title.tagName).toBe('H3');
    expect(title.getAttribute('tabindex')).toBe('-1');
    expect(title.textContent?.trim()).toBe('Choose key figures');
  });

  it('opens modally over the dialog it sits in and focuses its title', () => {
    open();
    expect(dialog().open).toBe(true);
    expect(dialog().matches(':modal')).toBe(true);
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
      .toBe('Shown in the Summary and in the copied or downloaded image. Remembered for every run report; a figure a run does not have is left out.');
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

  it('emits nothing on open', () => {
    open(['mean-time']);
    expect(fixture.componentInstance.changes).toEqual([]);
  });

  it('emits the exclusions at once on every change, keeping those of figures this run does not show', () => {
    open(['panel']);
    click(boxes()[1]);
    expect(status()).toBe('2 of 3 selected');
    expect(fixture.componentInstance.changes).toEqual([['panel', 'mean-time']]);
    expect(dialog().open).toBe(true);

    click(button('.kfch-all'));
    click(button('.kfch-none'));
    expect(fixture.componentInstance.changes).toEqual([
      ['panel', 'mean-time'],
      ['panel'],
      ['panel', 'intelligence', 'mean-time', 'estimated-cost']
    ]);
  });

  it('closes on its header close button, which has a name and a tooltip, and emits nothing', async () => {
    open();
    const close = dialog().querySelector('.dialog-header button.btn-icon-action.kfch-close') as HTMLButtonElement;
    expect(close.getAttribute('type')).toBe('button');
    expect(close.getAttribute('aria-label')).toBe('Close key figures chooser');
    expect(close.getAttribute('interestfor')).toBe('kfch-close-tip');
    expect(close.getAttribute('style')).toContain('anchor-name: --kfch-close-tip');
    const tip = dialog().querySelector('#kfch-close-tip') as HTMLElement;
    expect(tip.getAttribute('popover')).toBe('hint');
    expect(tip.getAttribute('style')).toContain('position-anchor: --kfch-close-tip');
    expect(tip.textContent?.trim()).toBe('Close');

    const closed = nextEvent(dialog(), 'close');
    click(close);
    await closed;
    expect(dialog().open).toBe(false);
    expect(document.activeElement).toBe(opener());
    expect(outer().open).toBe(true);
    expect(fixture.componentInstance.changes).toEqual([]);
  });

  it('has a single text-only Done in the footer, which closes and returns focus', async () => {
    open();
    const footer = Array.from(dialog().querySelectorAll<HTMLButtonElement>('.dialog-footer button'));
    expect(footer.map(b => b.textContent?.trim())).toEqual(['Done']);
    const done = footer[0];
    expect(done.classList).toContain('btn-gh');
    expect(done.classList).toContain('btn-gh-cancel');
    expect(done.getAttribute('type')).toBe('button');
    expect(done.querySelector('svg')).toBeNull();

    const closed = nextEvent(dialog(), 'close');
    click(done);
    await closed;
    expect(dialog().open).toBe(false);
    expect(document.activeElement).toBe(opener());
    expect(outer().open).toBe(true);
    expect(fixture.componentInstance.events).not.toContain('close');
  });

  it('has no export buttons, Cancel or reason, even with nothing selected', () => {
    open();
    click(button('.kfch-none'));
    for (const selector of ['#kfchReason', '.kfch-copy', '.kfch-download', '.kfch-cancel']) {
      expect(dialog().querySelector(selector), selector).toBeNull();
    }
  });

  it('starts from the given exclusions when opened again', async () => {
    open(['mean-time']);
    click(button('.kfch-none'));
    const closed = nextEvent(dialog(), 'close');
    click(button('.kfch-done'));
    await closed;

    open(['mean-time']);
    expect(checkedKeys()).toEqual(['intelligence', 'estimated-cost']);
  });

  describe('image details', () => {
    function openWithDetails(excluded: string[] = [], detailExcluded: string[] = ['board'], rows = DETAILS): void {
      fixture.componentInstance.chooser.open(FIGURES, excluded, opener(), { rows, excluded: detailExcluded });
      fixture.detectChanges();
    }

    function detailGroup(): HTMLElement | null {
      return dialog().querySelector('.kfch-group-details');
    }

    function detailBoxes(): HTMLInputElement[] {
      return Array.from(dialog().querySelectorAll<HTMLInputElement>('.kfch-group-details input[type="checkbox"]'));
    }

    function checkedDetails(): string[] {
      return detailBoxes().filter(box => box.checked).map(box => box.id.replace(/^kfch-detail-/, ''));
    }

    it('has no second group without details', () => {
      open();
      expect(detailGroup()).toBeNull();
      expect(dialog().querySelectorAll('[role="group"]').length).toBe(1);

      fixture.componentInstance.chooser.open(FIGURES, [], opener(), { rows: [], excluded: [] });
      fixture.detectChanges();
      expect(detailGroup()).toBeNull();
    });

    it('lists one checkbox per row in a captioned group, checked unless excluded', () => {
      openWithDetails();
      const group = detailGroup()!;
      expect(group.getAttribute('role')).toBe('group');
      expect(group.getAttribute('aria-labelledby')).toBe('kfchDetailsCaption');
      expect(dialog().querySelector('#kfchDetailsCaption')?.textContent?.trim()).toBe('Image details');

      const rows = Array.from(group.querySelectorAll<HTMLElement>('li'));
      expect(rows.map(row => row.getAttribute('data-detail'))).toEqual(['model', 'assessor', 'prompt', 'profile', 'started', 'board']);
      expect(rows[1].querySelector('label.checkbox-label')?.textContent?.replace(/\s+/g, ' ').trim())
        .toBe('Assessors — Claude 5 Opus + GPT-5.6 Sol');
      expect(checkedDetails()).toEqual(['model', 'assessor', 'prompt', 'profile', 'started']);
      expect(dialog().querySelector('.kfch-detail-count')?.textContent?.trim()).toBe('5 of 6 selected');
      expect(dialog().querySelector('.kfch-detail-count')?.getAttribute('role')).toBe('status');
      expect(dialog().querySelector('.kfch-detail-hint')?.textContent?.trim())
        .toBe("The run's settings above the figures in the copied or downloaded image. The dialog header always lists them all. Remembered for every run report.");
    });

    it('emits image-detail exclusions on a detail toggle, and figure exclusions on a figure toggle', () => {
      openWithDetails();
      click(detailBoxes()[5]);
      expect(fixture.componentInstance.detailChanges).toEqual([[]]);
      expect(fixture.componentInstance.changes).toEqual([]);

      click(detailBoxes()[2]);
      expect(fixture.componentInstance.detailChanges).toEqual([[], ['prompt']]);

      click(boxes()[0]);
      expect(fixture.componentInstance.changes).toEqual([['intelligence']]);
      expect(fixture.componentInstance.detailChanges.length).toBe(2);
    });

    it('keeps the stored exclusions of rows this run does not have', () => {
      openWithDetails([], ['board', 'prompt'], DETAILS.filter(row => row.key !== 'board'));
      expect(checkedDetails()).toEqual(['model', 'assessor', 'profile', 'started']);
      click(detailBoxes()[0]);
      expect(fixture.componentInstance.detailChanges).toEqual([['board', 'model', 'prompt']]);
    });

    it("acts with each group's All and None on that group only, named apart for assistive technology", () => {
      openWithDetails(['mean-time']);
      const all = button('.kfch-details-all');
      const none = button('.kfch-details-none');
      expect(all.textContent?.trim()).toBe('All image details');
      expect(none.textContent?.trim()).toBe('None of the image details');
      expect(all.textContent?.trim()).not.toBe(button('.kfch-all').textContent?.trim());

      click(none);
      expect(checkedDetails()).toEqual([]);
      expect(checkedKeys().filter(key => !key.startsWith('detail-'))).toEqual(['intelligence', 'estimated-cost']);
      click(all);
      expect(checkedDetails()).toEqual(DETAILS.map(row => row.key));
      expect(dialog().querySelector('.kfch-detail-count')?.textContent?.trim()).toBe('6 of 6 selected');

      click(button('.kfch-none'));
      expect(checkedDetails()).toEqual(DETAILS.map(row => row.key));
      expect(fixture.componentInstance.detailChanges).toEqual([DETAILS.map(row => row.key), []]);
    });
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
    expect(outer().open).toBe(true);
  });
});

@Component({
  standalone: true,
  imports: [KeyFiguresChooserComponent],
  template: `
    <button type="button" class="opener">Choose figures</button>
    <app-key-figures-chooser class="battery-chooser" idPrefix="brr-kfch" figuresHint="Remembered for every battery run report."
                             detailsHint="The battery run's settings in the image."></app-key-figures-chooser>
    <app-key-figures-chooser class="run-chooser"></app-key-figures-chooser>`
})
class TwoChoosersHostComponent {
  @ViewChild(KeyFiguresChooserComponent) chooser!: KeyFiguresChooserComponent;
}

describe('KeyFiguresChooserComponent with a host id prefix and hints', () => {
  let fixture: ComponentFixture<TwoChoosersHostComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [TwoChoosersHostComponent] }).compileComponents();
    fixture = TestBed.createComponent(TwoChoosersHostComponent);
    fixture.detectChanges();
  });

  afterEach(() => {
    for (const dialog of Array.from(document.querySelectorAll('dialog[open]')) as HTMLDialogElement[]) {
      dialog.close();
    }
  });

  function root(): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  function dialog(): HTMLDialogElement {
    return root().querySelector('.battery-chooser dialog') as HTMLDialogElement;
  }

  function open(): void {
    fixture.componentInstance.chooser.open(FIGURES, ['mean-time'], root().querySelector<HTMLElement>('.opener'), { rows: DETAILS, excluded: [] });
    fixture.detectChanges();
  }

  it('prefixes every id with the host prefix and points every reference at it', () => {
    open();
    expect(dialog().getAttribute('aria-labelledby')).toBe('brr-kfchTitle');
    expect(dialog().querySelector('h3')?.id).toBe('brr-kfchTitle');
    expect(document.activeElement).toBe(dialog().querySelector('#brr-kfchTitle'));

    const close = dialog().querySelector('.kfch-close') as HTMLButtonElement;
    expect(close.getAttribute('interestfor')).toBe('brr-kfch-close-tip');
    expect(close.getAttribute('style')).toContain('anchor-name: --brr-kfch-close-tip');
    expect(dialog().querySelector('#brr-kfch-close-tip')?.getAttribute('style')).toContain('position-anchor: --brr-kfch-close-tip');

    const [figures, details] = Array.from(dialog().querySelectorAll<HTMLElement>('[role="group"]'));
    expect(figures.getAttribute('aria-labelledby')).toBe('brr-kfchCaption');
    expect(dialog().querySelector('#brr-kfchCaption')?.textContent?.trim()).toBe('Figures');
    expect(details.getAttribute('aria-labelledby')).toBe('brr-kfchDetailsCaption');
    expect(dialog().querySelector('#brr-kfchDetailsCaption')?.textContent?.trim()).toBe('Image details');

    expect(Array.from(figures.querySelectorAll('input')).map(box => box.id))
      .toEqual(['brr-kfch-intelligence', 'brr-kfch-mean-time', 'brr-kfch-estimated-cost']);
    expect((dialog().querySelector('#brr-kfch-mean-time') as HTMLInputElement).checked).toBe(false);
    expect(Array.from(details.querySelectorAll('input')).map(box => box.id)[0]).toBe('brr-kfch-detail-model');
  });

  it('shares no id with a default chooser on the same page', () => {
    open();
    const ids = (selector: string): string[] =>
      Array.from(root().querySelectorAll<HTMLElement>(`${selector} [id]`)).map(element => element.id);
    const battery = ids('.battery-chooser');
    const run = ids('.run-chooser');
    expect(run).toContain('kfchTitle');
    expect(run).toContain('kfch-close-tip');
    expect(battery.filter(id => run.includes(id))).toEqual([]);
  });

  it('shows the hints the host gives', () => {
    open();
    expect(dialog().querySelector('.kfch-hint')?.textContent?.trim()).toBe('Remembered for every battery run report.');
    expect(dialog().querySelector('.kfch-detail-hint')?.textContent?.trim()).toBe("The battery run's settings in the image.");
  });
});
