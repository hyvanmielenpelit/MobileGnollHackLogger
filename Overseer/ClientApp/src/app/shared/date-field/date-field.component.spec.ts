import { ChangeDetectionStrategy, Component } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';

import { DateFieldComponent } from './date-field.component';

@Component({
  standalone: true,
  imports: [DateFieldComponent],
  changeDetection: ChangeDetectionStrategy.Eager,
  template: `
    <dialog class="t-dialog" (keydown)="outerKeys.push($event.key)" (cancel)="onCancel()">
      <label for="t-from">From (UTC)</label>
      <app-date-field inputId="t-from" label="From (UTC)" [value]="value" [min]="min" [max]="max"
                      [invalid]="invalid" [readonly]="readonly" [describedBy]="describedBy"
                      (valueChange)="onChange($event)"></app-date-field>
      <p id="t-from-error">The start must be a date.</p>
    </dialog>
  `
})
class DateFieldHostComponent {
  value = '';
  min: string | null = null;
  max: string | null = null;
  invalid = false;
  readonly = false;
  describedBy: string | null = 't-from-error';
  emitted: string[] = [];
  outerKeys: string[] = [];
  cancels = 0;

  onChange(value: string): void {
    this.emitted.push(value);
    this.value = value;
  }

  onCancel(): void {
    this.cancels++;
  }
}

describe('DateFieldComponent', () => {
  let fixture: ComponentFixture<DateFieldHostComponent>;
  let host: DateFieldHostComponent;
  let el: HTMLElement;

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [DateFieldHostComponent] }).compileComponents();
    fixture = TestBed.createComponent(DateFieldHostComponent);
    host = fixture.componentInstance;
    el = fixture.nativeElement as HTMLElement;
    fixture.detectChanges();
    dialog().showModal();
  });

  afterEach(() => {
    if (calendar()?.matches(':popover-open')) {
      calendar().hidePopover();
    }
    if (dialog()?.open) {
      dialog().close();
    }
    fixture.destroy();
    vi.useRealTimers();
  });

  const dialog = (): HTMLDialogElement => el.querySelector<HTMLDialogElement>('dialog.t-dialog')!;
  const field = (): HTMLElement => el.querySelector<HTMLElement>('app-date-field')!;
  const input = (): HTMLInputElement => el.querySelector<HTMLInputElement>('#t-from')!;
  const button = (): HTMLButtonElement => el.querySelector<HTMLButtonElement>('#t-from-cal-btn')!;
  const calendar = (): HTMLElement => el.querySelector<HTMLElement>('#t-from-cal')!;
  const title = (): HTMLElement => el.querySelector<HTMLElement>('#t-from-cal-title')!;
  const day = (iso: string): HTMLButtonElement => calendar().querySelector<HTMLButtonElement>(`[data-day="${iso}"]`)!;
  const days = (): HTMLButtonElement[] => Array.from(calendar().querySelectorAll<HTMLButtonElement>('.gh-calendar-day'));
  const footButtons = (): HTMLButtonElement[] => Array.from(calendar().querySelectorAll<HTMLButtonElement>('.gh-calendar-foot button'));
  const flat = (node: Element | null): string => (node?.textContent ?? '').replace(/\s+/g, ' ').trim();
  const isOpen = (): boolean => calendar().matches(':popover-open');
  const tabStops = (): (string | undefined)[] => days().filter(button => button.tabIndex === 0).map(button => button.dataset['day']);
  const focusedDay = (): string | undefined => (document.activeElement as HTMLElement | null)?.dataset['day'];

  function setHost(changes: Partial<DateFieldHostComponent>): void {
    Object.assign(host, changes);
    fixture.detectChanges();
  }

  function nextToggle(): Promise<void> {
    return new Promise<void>(resolve => calendar().addEventListener('toggle', () => resolve(), { once: true }));
  }

  /** Focuses the calendar button, opens the calendar with it and waits for the toggle event. */
  async function openCalendar(): Promise<void> {
    const toggled = nextToggle();
    button().focus();
    button().click();
    await toggled;
    fixture.detectChanges();
  }

  async function closeCalendar(): Promise<void> {
    const toggled = nextToggle();
    calendar().hidePopover();
    await toggled;
    fixture.detectChanges();
  }

  /** Dispatches a keydown on the focused element, as the keyboard would. */
  function press(key: string, shiftKey = false): KeyboardEvent {
    const event = new KeyboardEvent('keydown', { key, shiftKey, bubbles: true, cancelable: true });
    (document.activeElement as HTMLElement).dispatchEvent(event);
    fixture.detectChanges();
    return event;
  }

  function typeAndCommit(text: string): void {
    input().value = text;
    input().dispatchEvent(new Event('input', { bubbles: true }));
    input().dispatchEvent(new Event('change', { bubbles: true }));
    fixture.detectChanges();
  }

  /** Today, 2026-10-07, at 23:30 UTC: already 2026-10-08 in any zone east of UTC. */
  function freezeLateOnOctober7(): void {
    vi.useFakeTimers({ toFake: ['Date'] });
    vi.setSystemTime(new Date(Date.UTC(2026, 9, 7, 23, 30)));
  }

  describe('the input', () => {
    it('is a numeric text field described by its format hint and the host ids', () => {
      expect(input().type).toBe('text');
      expect(input().inputMode).toBe('numeric');
      expect(input().getAttribute('autocomplete')).toBe('off');
      expect(input().getAttribute('spellcheck')).toBe('false');
      expect(input().placeholder).toBe('YYYY-MM-DD');
      expect(Array.from(input().classList)).toEqual(expect.arrayContaining(['gh-input', 'gh-date-field-input']));
      expect(flat(input().labels![0])).toBe('From (UTC)');

      expect(input().getAttribute('aria-describedby')).toBe('t-from-format t-from-error');
      const format = el.querySelector('#t-from-format')!;
      expect(format.classList).toContain('visually-hidden');
      expect(flat(format)).toBe('Format: year-month-day, for example 2026-10-07.');

      setHost({ describedBy: null });
      expect(input().getAttribute('aria-describedby')).toBe('t-from-format');
    });

    it('shows the value and sets aria-invalid only while invalid', () => {
      setHost({ value: '2026-10-07' });
      expect(input().value).toBe('2026-10-07');
      expect(input().hasAttribute('aria-invalid')).toBe(false);

      setHost({ invalid: true });
      expect(input().getAttribute('aria-invalid')).toBe('true');
    });

    it('emits nothing while typing and a normalized value on change', () => {
      input().value = '2026/9/';
      input().dispatchEvent(new Event('input', { bubbles: true }));
      input().value = '2026/9/5';
      input().dispatchEvent(new Event('input', { bubbles: true }));
      expect(host.emitted).toEqual([]);

      input().dispatchEvent(new Event('change', { bubbles: true }));
      fixture.detectChanges();
      expect(host.emitted).toEqual(['2026-09-05']);
      expect(input().value).toBe('2026-09-05');

      typeAndCommit('2026.9.5');
      expect(host.emitted[1]).toBe('2026-09-05');
      expect(input().value).toBe('2026-09-05');

      typeAndCommit(' 2026-9-5 ');
      expect(host.emitted[2]).toBe('2026-09-05');

      typeAndCommit('  next tuesday ');
      expect(host.emitted[3]).toBe('next tuesday');
      typeAndCommit('2026-2-30');
      expect(host.emitted[4]).toBe('2026-2-30');
    });
  });

  describe('the calendar button', () => {
    it('is an icon-only button named for the field, with a hint tooltip', () => {
      expect(button().type).toBe('button');
      expect(Array.from(button().classList)).toEqual(expect.arrayContaining(['action-btn', 'gh-date-field-btn']));
      expect(button().getAttribute('aria-label')).toBe('Choose From (UTC) on a calendar');
      expect(button().getAttribute('aria-haspopup')).toBe('dialog');
      expect(button().getAttribute('popovertarget')).toBe('t-from-cal');
      expect(button().hasAttribute('title')).toBe(false);
      expect(button().querySelector('svg')!.getAttribute('aria-hidden')).toBe('true');

      expect(button().getAttribute('interestfor')).toBe('t-from-cal-tip');
      expect(button().getAttribute('style')).toContain('anchor-name: --t-from-cal-btn');
      const tip = el.querySelector<HTMLElement>('#t-from-cal-tip')!;
      expect(tip.getAttribute('popover')).toBe('hint');
      expect(tip.getAttribute('style')).toContain('position-anchor: --t-from-cal-btn');
      expect(flat(tip)).toBe('Open calendar');
    });

    it('anchors the calendar under the field', () => {
      expect(el.querySelector('.gh-date-field')!.getAttribute('style')).toContain('anchor-name: --t-from-field');
      expect(calendar().getAttribute('style')).toContain('position-anchor: --t-from-field');
    });

    it('follows the calendar in aria-expanded and gets focus back when it closes', async () => {
      expect(button().getAttribute('aria-expanded')).toBe('false');

      await openCalendar();
      expect(isOpen()).toBe(true);
      expect(button().getAttribute('aria-expanded')).toBe('true');

      await closeCalendar();
      expect(button().getAttribute('aria-expanded')).toBe('false');
      expect(document.activeElement).toBe(button());
    });

    it('opens nothing on a read-only field, yet stays focusable', async () => {
      setHost({ readonly: true });
      expect(input().readOnly).toBe(true);
      expect(button().getAttribute('aria-disabled')).toBe('true');
      expect(button().disabled).toBe(false);
      expect(button().hasAttribute('popovertarget')).toBe(false);

      button().focus();
      expect(document.activeElement).toBe(button());
      button().click();
      await new Promise(resolve => setTimeout(resolve, 0));
      fixture.detectChanges();
      expect(isOpen()).toBe(false);
      expect(button().getAttribute('aria-expanded')).toBe('false');
      expect(host.emitted).toEqual([]);
    });
  });

  describe('the calendar', () => {
    it('is a non-modal dialog named by the field and the month', async () => {
      expect(calendar().getAttribute('popover')).toBe('auto');
      expect(calendar().getAttribute('role')).toBe('dialog');
      expect(calendar().getAttribute('aria-modal')).toBe('false');
      expect(calendar().getAttribute('aria-labelledby')).toBe('t-from-cal-name t-from-cal-title');
      expect(flat(el.querySelector('#t-from-cal-name'))).toBe('From (UTC)');

      setHost({ value: '2026-10-07' });
      await openCalendar();
      expect(title().tagName).toBe('H2');
      expect(title().getAttribute('aria-live')).toBe('polite');
      expect(flat(title())).toBe('October 2026');

      const grid = calendar().querySelector('table')!;
      expect(grid.getAttribute('role')).toBe('grid');
      expect(grid.getAttribute('aria-labelledby')).toBe('t-from-cal-title');
    });

    it('shows a Sunday-first month grid with named days', async () => {
      setHost({ value: '2026-10-07' });
      await openCalendar();

      const headers = Array.from(calendar().querySelectorAll('thead th'));
      expect(headers.map(th => flat(th))).toEqual(['Su', 'Mo', 'Tu', 'We', 'Th', 'Fr', 'Sa']);
      expect(headers.map(th => th.getAttribute('abbr'))).toEqual(
        ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday']);
      expect(headers.every(th => th.getAttribute('scope') === 'col')).toBe(true);

      expect(days().length).toBe(42);
      expect(days()[0].dataset['day']).toBe('2026-09-27');
      expect(days().every(button => button.type === 'button')).toBe(true);
      expect(day('2026-10-07').getAttribute('aria-label')).toBe('Wednesday, October 7, 2026');
      expect(flat(day('2026-10-07'))).toBe('7');
      expect(day('2026-09-27').classList).toContain('is-outside-month');
      expect(day('2026-10-01').classList).not.toContain('is-outside-month');
    });

    it('opens on the chosen day, selected, with the only tab stop', async () => {
      setHost({ value: '2026-10-07' });
      await openCalendar();

      expect(focusedDay()).toBe('2026-10-07');
      const selected = Array.from(calendar().querySelectorAll('td[aria-selected="true"]'));
      expect(selected.length).toBe(1);
      expect(selected[0].contains(day('2026-10-07'))).toBe(true);
      expect(tabStops()).toEqual(['2026-10-07']);
    });

    it('moves with the arrow, Home, End and page keys and announces the month', async () => {
      setHost({ value: '2026-10-07' });
      await openCalendar();

      expect(press('ArrowRight').defaultPrevented).toBe(true);
      expect(focusedDay()).toBe('2026-10-08');
      press('ArrowDown');
      expect(focusedDay()).toBe('2026-10-15');
      press('ArrowLeft');
      press('ArrowUp');
      expect(focusedDay()).toBe('2026-10-07');
      press('Home');
      expect(focusedDay()).toBe('2026-10-04');
      press('End');
      expect(focusedDay()).toBe('2026-10-10');

      press('PageDown');
      expect(focusedDay()).toBe('2026-11-10');
      expect(flat(title())).toBe('November 2026');
      press('PageUp', true);
      expect(focusedDay()).toBe('2025-11-10');
      expect(flat(title())).toBe('November 2025');
      press('PageDown', true);
      press('PageUp');
      expect(focusedDay()).toBe('2026-10-10');
      expect(flat(title())).toBe('October 2026');

      expect(tabStops()).toEqual(['2026-10-10']);
      expect(host.emitted).toEqual([]);
      expect(press('a').defaultPrevented).toBe(false);
    });

    it('clamps the day when a page key lands in a shorter month', async () => {
      setHost({ value: '2028-01-31' });
      await openCalendar();
      press('PageDown');
      expect(focusedDay()).toBe('2028-02-29');
      press('PageDown', true);
      expect(focusedDay()).toBe('2029-02-28');
    });

    it('picks with Enter, closes and returns focus to the calendar button', async () => {
      setHost({ value: '2026-10-07' });
      await openCalendar();
      press('ArrowRight');

      const toggled = nextToggle();
      expect(press('Enter').defaultPrevented).toBe(true);
      await toggled;
      fixture.detectChanges();

      expect(host.emitted).toEqual(['2026-10-08']);
      expect(isOpen()).toBe(false);
      expect(document.activeElement).toBe(button());
      expect(button().getAttribute('aria-expanded')).toBe('false');
      expect(input().value).toBe('2026-10-08');
    });

    it('picks with Space and with a click, neighboring months included', async () => {
      setHost({ value: '2026-10-07' });
      await openCalendar();
      press('ArrowLeft');
      let toggled = nextToggle();
      press(' ');
      await toggled;
      expect(host.emitted).toEqual(['2026-10-06']);

      fixture.detectChanges();
      await openCalendar();
      toggled = nextToggle();
      day('2026-11-01').click();
      await toggled;
      fixture.detectChanges();
      expect(host.emitted[1]).toBe('2026-11-01');
      expect(input().value).toBe('2026-11-01');
    });

    it('closes only itself on Escape inside a modal dialog', async () => {
      await openCalendar();
      const toggled = nextToggle();
      const escape = press('Escape');
      await toggled;
      fixture.detectChanges();

      expect(escape.defaultPrevented).toBe(true);
      expect(host.outerKeys).toEqual([]);
      expect(isOpen()).toBe(false);
      expect(dialog().open).toBe(true);
      expect(host.cancels).toBe(0);
      expect(document.activeElement).toBe(button());
      expect(host.emitted).toEqual([]);
    });

    it('closes when focus leaves it, without taking focus back', async () => {
      await openCalendar();
      const toggled = nextToggle();
      input().focus();
      await toggled;
      fixture.detectChanges();

      expect(isOpen()).toBe(false);
      expect(document.activeElement).toBe(input());
    });

    it('turns the month with Previous and Next, keeping focus on the button', async () => {
      setHost({ value: '2026-10-07' });
      await openCalendar();
      const prev = el.querySelector<HTMLButtonElement>('#t-from-cal-prev')!;
      const next = el.querySelector<HTMLButtonElement>('#t-from-cal-next')!;
      expect(prev.getAttribute('aria-label')).toBe('Previous month');
      expect(next.getAttribute('aria-label')).toBe('Next month');
      expect(flat(el.querySelector('#t-from-cal-prev-tip'))).toBe('Previous month');
      expect(flat(el.querySelector('#t-from-cal-next-tip'))).toBe('Next month');

      next.focus();
      next.click();
      fixture.detectChanges();
      expect(flat(title())).toBe('November 2026');
      expect(document.activeElement).toBe(next);
      expect(tabStops()).toEqual(['2026-11-07']);

      prev.focus();
      prev.click();
      prev.click();
      fixture.detectChanges();
      expect(flat(title())).toBe('September 2026');
      expect(document.activeElement).toBe(prev);
      expect(isOpen()).toBe(true);
    });

    it('marks days outside min and max aria-disabled and does not pick them', async () => {
      setHost({ value: '2026-10-10', min: '2026-10-05', max: '2026-10-20' });
      await openCalendar();

      expect(day('2026-10-04').getAttribute('aria-disabled')).toBe('true');
      expect(day('2026-10-05').hasAttribute('aria-disabled')).toBe(false);
      expect(day('2026-10-20').hasAttribute('aria-disabled')).toBe(false);
      expect(day('2026-10-21').getAttribute('aria-disabled')).toBe('true');
      expect(day('2026-10-04').disabled).toBe(false);

      day('2026-10-04').click();
      fixture.detectChanges();
      expect(host.emitted).toEqual([]);
      expect(isOpen()).toBe(true);

      press('ArrowDown');
      press('ArrowDown');
      expect(focusedDay()).toBe('2026-10-24');
      press('Enter');
      expect(host.emitted).toEqual([]);
      expect(isOpen()).toBe(true);
      expect(focusedDay()).toBe('2026-10-24');
    });
  });

  describe('today', () => {
    it('marks today in UTC and opens on it without a value', async () => {
      freezeLateOnOctober7();
      await openCalendar();

      expect(focusedDay()).toBe('2026-10-07');
      const current = days().filter(button => button.getAttribute('aria-current') === 'date');
      expect(current.map(button => button.dataset['day'])).toEqual(['2026-10-07']);
      expect(calendar().querySelectorAll('td[aria-selected="true"]').length).toBe(0);
    });

    it('opens on the nearest enabled day when today is outside min and max', async () => {
      freezeLateOnOctober7();
      setHost({ min: '2026-11-03' });
      await openCalendar();
      expect(focusedDay()).toBe('2026-11-03');
      expect(flat(title())).toBe('November 2026');
      await closeCalendar();

      setHost({ min: null, max: '2026-08-15' });
      await openCalendar();
      expect(focusedDay()).toBe('2026-08-15');
    });

    it('offers Today and Clear, each naming the field, which choose and close', async () => {
      freezeLateOnOctober7();
      setHost({ value: '2026-09-01' });
      await openCalendar();

      const [today, clear] = footButtons();
      expect(Array.from(today.classList)).toContain('btn-link');
      expect(flat(today)).toBe('Today for From (UTC)');
      expect(flat(clear)).toBe('Clear From (UTC)');
      expect(today.querySelector('.visually-hidden')).not.toBeNull();
      expect(clear.querySelector('.visually-hidden')).not.toBeNull();

      let toggled = nextToggle();
      today.click();
      await toggled;
      fixture.detectChanges();
      expect(host.emitted).toEqual(['2026-10-07']);
      expect(isOpen()).toBe(false);
      expect(document.activeElement).toBe(button());

      await openCalendar();
      toggled = nextToggle();
      footButtons()[1].click();
      await toggled;
      fixture.detectChanges();
      expect(host.emitted[1]).toBe('');
      expect(input().value).toBe('');
      expect(document.activeElement).toBe(button());
    });

    it('refuses Today when today is outside min and max', async () => {
      freezeLateOnOctober7();
      setHost({ value: '2026-09-01', max: '2026-10-01' });
      await openCalendar();

      const today = footButtons()[0];
      expect(today.getAttribute('aria-disabled')).toBe('true');
      today.click();
      fixture.detectChanges();
      expect(host.emitted).toEqual([]);
      expect(isOpen()).toBe(true);
    });
  });

  it('derives every id from inputId', () => {
    const ids = Array.from(field().querySelectorAll('[id]')).map(node => node.id);
    expect(ids.length).toBeGreaterThan(0);
    for (const id of ids) {
      expect(id.startsWith('t-from'), id).toBe(true);
    }
  });
});
