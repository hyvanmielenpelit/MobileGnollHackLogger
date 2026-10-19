import { ChangeDetectionStrategy, ChangeDetectorRef, Component, inject } from '@angular/core';
import { ComponentFixture, TestBed, fakeAsync, tick } from '@angular/core/testing';
import { By } from '@angular/platform-browser';

import { MultiPickerComponent } from './multi-picker.component';
import { MultiPickerKey, MultiPickerOption, MultiPickerSelection, MultiPickerStatus } from './multi-picker.models';

const OPTIONS: MultiPickerOption[] = [
  { key: 'a', label: 'Alpha', tag: 'Run', detail: 'Run #1' },
  { key: 'b', label: 'Beta' },
  { key: 'c', label: 'Bravo', disabledReason: 'No answers recorded.' },
  { key: 'd', label: 'Charlie' },
  { key: 'e', label: 'Delta' }
];

type HostState = Pick<HostComponent,
  'options' | 'selected' | 'min' | 'max' | 'chips' | 'maxChips' | 'useTemplate' | 'chipStyle' | 'status' | 'showAllNone'>;

/** Feeds every emitted selection back into `selectedKeys`, the way a real host does. */
@Component({
  standalone: true,
  imports: [MultiPickerComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="outer" (keydown)="outerKeys.push($any($event).key)">
      <label id="pickLabel">Sources</label>
      <app-multi-picker class="picker" labelledBy="pickLabel" describedBy="pickHint" summaryNoun="sources"
                        [options]="options" [selectedKeys]="selected" [min]="min" [max]="max"
                        [chips]="chips" [maxChips]="maxChips" [chipStyle]="chipStyle"
                        [status]="status" [showAllNone]="showAllNone"
                        [optionTemplate]="useTemplate ? custom : null"
                        [chipTemplate]="useTemplate ? customChip : null"
                        (selectionChange)="onChange($event)"></app-multi-picker>
      <span id="pickHint">Hint</span>
      <button type="button" class="outside">Outside</button>
    </div>
    <ng-template #custom let-option let-selected="selected">
      <span class="custom-row">{{ option.label }} is {{ selected ? 'on' : 'off' }}</span>
    </ng-template>
    <ng-template #customChip let-option let-noteId="noteId">
      <span class="custom-chip">{{ option.label }} chip</span>
      @if (option.detail) {
        <span class="custom-note" [id]="noteId">{{ option.detail }}</span>
      }
    </ng-template>
    <dialog class="dlg" (keydown)="dialogKeys.push($any($event).key)">
      <app-multi-picker class="dialog-picker" label="In dialog" [options]="options" [selectedKeys]="selected"
                        (selectionChange)="onChange($event)"></app-multi-picker>
    </dialog>
  `
})
class HostComponent {
  private cdr = inject(ChangeDetectorRef);
  options: MultiPickerOption[] = OPTIONS;
  selected: MultiPickerKey[] = [];
  min = 0;
  max: number | null = null;
  chips: 'none' | 'selected' = 'selected';
  maxChips = 6;
  useTemplate = false;
  chipStyle: 'pill' | 'card' = 'pill';
  status: MultiPickerStatus | null = null;
  showAllNone = true;
  changes: MultiPickerSelection[] = [];
  outerKeys: string[] = [];
  dialogKeys: string[] = [];

  onChange(selection: MultiPickerSelection): void {
    this.changes.push(selection);
    this.selected = selection.keys;
    this.cdr.markForCheck();
  }

  set(changes: Partial<HostState>): void {
    Object.assign(this, changes);
    this.cdr.markForCheck();
  }
}

describe('MultiPickerComponent', () => {
  let fixture: ComponentFixture<HostComponent>;
  let host: HostComponent;
  let el: HTMLElement;

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [HostComponent] }).compileComponents();
    fixture = TestBed.createComponent(HostComponent);
    host = fixture.componentInstance;
    fixture.detectChanges();
    el = fixture.nativeElement as HTMLElement;
  });

  const picker = () => el.querySelector<HTMLElement>('.picker')!;
  const trigger = (p = picker()) => p.querySelector<HTMLButtonElement>('.selector-trigger')!;
  const listbox = (p = picker()) => p.querySelector<HTMLElement>('[role="listbox"]');
  const options = (p = picker()) => Array.from(p.querySelectorAll<HTMLElement>('[role="option"]'));
  const activeOption = (p = picker()) => {
    const id = listbox(p)?.getAttribute('aria-activedescendant');
    return id ? p.querySelector<HTMLElement>(`#${id}`) : null;
  };
  const chipButtons = () => Array.from(picker().querySelectorAll<HTMLButtonElement>('.gh-multi-picker-chip .action-btn'));
  const linkButton = (name: string) => Array.from(picker().querySelectorAll<HTMLButtonElement>('.gh-multi-picker-actions .btn-link'))
    .find(b => b.textContent!.trim().startsWith(name))!;
  const liveLine = () => picker().querySelector<HTMLElement>('[role="status"]')!.textContent!.trim();
  const summary = () => trigger().querySelector<HTMLElement>('.gh-multi-picker-summary')!;

  function update(changes: Partial<HostState>): void {
    host.set(changes);
    fixture.detectChanges();
  }

  function openByClick(p = picker()): void {
    trigger(p).click();
    fixture.detectChanges();
  }

  function key(target: HTMLElement, keyName: string, init: KeyboardEventInit = {}): KeyboardEvent {
    const event = new KeyboardEvent('keydown', { key: keyName, bubbles: true, cancelable: true, ...init });
    target.dispatchEvent(event);
    fixture.detectChanges();
    return event;
  }

  function click(target: HTMLElement): void {
    target.click();
    fixture.detectChanges();
  }

  describe('roles and states', () => {
    it('is a listbox button named by the label and its summary, with aria-controls only while open', () => {
      const button = trigger();
      expect(button.getAttribute('aria-haspopup')).toBe('listbox');
      expect(button.getAttribute('aria-expanded')).toBe('false');
      expect(button.hasAttribute('aria-controls')).toBe(false);
      expect(button.getAttribute('aria-labelledby')).toBe(`pickLabel ${button.id}`);
      expect(button.getAttribute('aria-describedby')).toBe('pickHint');

      openByClick();
      expect(button.getAttribute('aria-expanded')).toBe('true');
      expect(button.getAttribute('aria-controls')).toBe(listbox()!.id);
    });

    it('opens a multiselectable listbox that takes focus and tracks the active option', () => {
      update({ selected: ['b', 'd'] });
      openByClick();
      const box = listbox()!;
      expect(box.getAttribute('aria-multiselectable')).toBe('true');
      expect(box.getAttribute('aria-labelledby')).toBe('pickLabel');
      expect(document.activeElement).toBe(box);
      expect(activeOption()!.textContent).toContain('Beta');
      expect(options().map(o => o.getAttribute('aria-selected'))).toEqual(['false', 'true', 'false', 'true', 'false']);
      expect(new Set(options().map(o => o.id)).size).toBe(5);
    });

    it('wraps a long single-selection summary in a narrow host instead of clipping it', () => {
      const label = 'Claude Opus Extended Thinking Preview 2026';
      update({ options: [{ key: 'long', label }, ...OPTIONS], selected: ['long'] });
      picker().style.inlineSize = '200px';

      const line = summary();
      expect(line.textContent!.trim()).toBe(label);
      expect(line.scrollWidth).toBeLessThanOrEqual(line.clientWidth + 1);
      const style = getComputedStyle(line);
      const lineHeight = parseFloat(style.lineHeight) || parseFloat(style.fontSize) * 1.2;
      expect(line.getBoundingClientRect().height).toBeGreaterThan(lineHeight);
      const chevron = trigger().querySelector('.chevron')!.getBoundingClientRect();
      const box = trigger().getBoundingClientRect();
      expect(chevron.right).toBeLessThanOrEqual(box.right);
      expect(chevron.left).toBeGreaterThanOrEqual(box.left);
    });

    it('shows selection with an aria-hidden check glyph', () => {
      update({ selected: ['b'] });
      openByClick();
      const checks = options().map(o => o.querySelector<HTMLElement>('.gh-multi-picker-check')!);
      expect(checks.every(c => c.getAttribute('aria-hidden') === 'true')).toBe(true);
      expect(checks[1].querySelector('svg')).not.toBeNull();
      expect(checks[0].querySelector('svg')).toBeNull();
    });

    it('summarizes the selection on the trigger', () => {
      expect(summary().textContent!.trim()).toBe('Select sources');
      expect(summary().classList).toContain('text-muted');

      update({ selected: ['a'] });
      expect(summary().textContent!.trim()).toBe('Alpha');
      expect(trigger().querySelector('.model-option-tag')!.textContent!.trim()).toBe('Run');

      update({ selected: ['a', 'b', 'd'] });
      expect(summary().textContent!.trim()).toBe('3 of 5 sources');

      update({ selected: ['a', 'b', 'c', 'd', 'e'] });
      expect(summary().textContent!.trim()).toBe('All 5 sources');
    });

    it('renders tag, label and detail by default', () => {
      openByClick();
      const first = options()[0];
      expect(first.querySelector('.model-option-tag')!.textContent!.trim()).toBe('Run');
      expect(first.querySelector('.model-name')!.textContent!.trim()).toBe('Alpha');
      expect(first.querySelector('.gh-multi-picker-detail')!.textContent!.trim()).toBe('Run #1');
    });

    it('groups consecutive options under a named role="group"', () => {
      update({
        options: [
          { key: 'x', label: 'Xi', group: 'Runs' },
          { key: 'y', label: 'Ypsilon', group: 'Runs' },
          { key: 'z', label: 'Zeta', group: 'Batteries' }
        ]
      });
      openByClick();
      const groups = Array.from(picker().querySelectorAll<HTMLElement>('[role="group"]'));
      const names = groups.map(g => picker().querySelector(`#${g.getAttribute('aria-labelledby')}`)!.textContent!.trim());
      expect(names).toEqual(['Runs', 'Batteries']);
      expect(groups[0].querySelectorAll('[role="option"]').length).toBe(2);
    });

    it('has no title attribute anywhere', () => {
      update({ selected: ['a', 'b'], min: 2 });
      openByClick();
      expect(picker().querySelectorAll('[title]').length).toBe(0);
    });
  });

  describe('keyboard', () => {
    it('Space toggles the active option and keeps the list open', () => {
      openByClick();
      const box = listbox()!;
      key(box, 'ArrowDown');
      const event = key(box, ' ');
      expect(event.defaultPrevented).toBe(true);
      expect(host.changes).toEqual([{ keys: ['b'] }]);
      expect(listbox()).not.toBeNull();
      expect(document.activeElement).toBe(listbox());
      expect(options()[1].getAttribute('aria-selected')).toBe('true');

      key(listbox()!, ' ');
      expect(host.changes[1]).toEqual({ keys: [] });
    });

    it('Enter toggles, closes and refocuses the trigger', () => {
      update({ selected: ['a'] });
      openByClick();
      key(listbox()!, 'ArrowDown');
      key(listbox()!, 'Enter');
      expect(host.changes).toEqual([{ keys: ['a', 'b'] }]);
      expect(listbox()).toBeNull();
      expect(document.activeElement).toBe(trigger());
    });

    it('Ctrl+A selects every enabled option, and clears all when all are selected', () => {
      openByClick();
      key(listbox()!, 'a', { ctrlKey: true });
      expect(host.changes[0]).toEqual({ keys: ['a', 'b', 'd', 'e'] });
      key(listbox()!, 'a', { ctrlKey: true });
      expect(host.changes[1]).toEqual({ keys: [] });
      expect(host.changes.length).toBe(2);
    });

    it('Shift+ArrowDown and Shift+ArrowUp extend the selection, skipping a disabled option', () => {
      update({ selected: ['a'] });
      openByClick();
      key(listbox()!, 'ArrowDown', { shiftKey: true });
      expect(host.changes).toEqual([{ keys: ['a', 'b'] }]);
      key(listbox()!, 'ArrowDown', { shiftKey: true });
      expect(activeOption()!.textContent).toContain('Bravo');
      expect(host.changes.length).toBe(1);
      key(listbox()!, 'ArrowDown', { shiftKey: true });
      expect(host.changes[1]).toEqual({ keys: ['a', 'b', 'd'] });
      key(listbox()!, 'ArrowUp', { shiftKey: true });
      expect(host.changes.length).toBe(2);
    });

    it('moves without wrapping, and by Home, End, PageDown and PageUp', () => {
      openByClick();
      const box = listbox()!;
      key(box, 'ArrowUp');
      expect(activeOption()!.textContent).toContain('Alpha');
      key(box, 'End');
      expect(activeOption()!.textContent).toContain('Delta');
      key(box, 'ArrowDown');
      expect(activeOption()!.textContent).toContain('Delta');
      key(box, 'Home');
      expect(activeOption()!.textContent).toContain('Alpha');
      key(box, 'PageDown');
      expect(activeOption()!.textContent).toContain('Delta');
      key(box, 'PageUp');
      expect(activeOption()!.textContent).toContain('Alpha');
      expect(activeOption()!.classList).toContain('is-active');
    });

    it('opens on ArrowDown at the first selected option, else the first, and on ArrowUp at the last', () => {
      key(trigger(), 'ArrowDown');
      expect(activeOption()!.textContent).toContain('Alpha');
      key(listbox()!, 'Escape');
      key(trigger(), 'ArrowUp');
      expect(activeOption()!.textContent).toContain('Delta');
      key(listbox()!, 'Escape');
      update({ selected: ['d'] });
      key(trigger(), 'ArrowDown');
      expect(activeOption()!.textContent).toContain('Charlie');
    });

    it('Escape closes without undoing a toggle, and keeps the key from the parent', () => {
      openByClick();
      key(listbox()!, ' ');
      const event = key(listbox()!, 'Escape');
      expect(event.defaultPrevented).toBe(true);
      expect(host.outerKeys).not.toContain('Escape');
      expect(host.changes).toEqual([{ keys: ['a'] }]);
      expect(listbox()).toBeNull();
      expect(document.activeElement).toBe(trigger());
      expect(summary().textContent!.trim()).toBe('Alpha');
    });

    it('Tab closes without emitting or preventing the default', () => {
      openByClick();
      const event = key(listbox()!, 'Tab');
      expect(event.defaultPrevented).toBe(false);
      expect(host.changes.length).toBe(0);
      expect(listbox()).toBeNull();
    });

    it('type-ahead matches the start of a label and a repeated letter cycles', fakeAsync(() => {
      openByClick();
      const box = listbox()!;
      key(box, 'b');
      expect(activeOption()!.textContent).toContain('Beta');
      key(box, 'b');
      expect(activeOption()!.textContent).toContain('Bravo');
      tick(600);
      key(box, 'c');
      expect(activeOption()!.textContent).toContain('Charlie');
      tick(600);
      key(box, 'r');
      expect(activeOption()!.textContent).toContain('Charlie');
      tick(600);
      key(box, 'd');
      key(box, 'e');
      expect(activeOption()!.textContent).toContain('Delta');
      tick(600);
      expect(host.changes.length).toBe(0);
    }));
  });

  describe('limits and disabled options', () => {
    it('keeps min selected: refuses the toggle and disables None with its reason', () => {
      update({ selected: ['a', 'b'], min: 2 });
      openByClick();
      key(listbox()!, ' ');
      expect(host.changes.length).toBe(0);
      expect(liveLine()).toBe('At least 2 sources must stay selected.');

      const none = linkButton('None');
      expect(none.getAttribute('aria-disabled')).toBe('true');
      const tip = picker().querySelector<HTMLElement>(`#${none.getAttribute('interestfor')}`)!;
      expect(tip.textContent!.trim()).toBe('At least 2 sources must stay selected.');
      click(none);
      expect(host.changes.length).toBe(0);
    });

    it('keeps max: refuses a further toggle and disables All with its reason', () => {
      update({ selected: ['a', 'b'], max: 2 });
      openByClick();
      key(listbox()!, 'End');
      key(listbox()!, ' ');
      expect(host.changes.length).toBe(0);
      expect(liveLine()).toBe('At most 2 sources can be selected.');

      const all = linkButton('All');
      expect(all.getAttribute('aria-disabled')).toBe('true');
      expect(picker().querySelector(`#${all.getAttribute('interestfor')}`)!.textContent!.trim())
        .toBe('At most 2 sources can be selected.');
    });

    it('All and None emit once each when they can act', () => {
      update({ selected: ['b'] });
      const all = linkButton('All');
      expect(all.hasAttribute('aria-disabled')).toBe(false);
      expect(all.textContent!.replace(/\s+/g, ' ').trim()).toBe('All sources');
      click(all);
      expect(host.changes).toEqual([{ keys: ['a', 'b', 'd', 'e'] }]);
      expect(linkButton('All').getAttribute('aria-disabled')).toBe('true');

      click(linkButton('None'));
      expect(host.changes[1]).toEqual({ keys: [] });
      expect(linkButton('None').getAttribute('aria-disabled')).toBe('true');
    });

    it('marks a disabled option aria-disabled with its reason, and refuses to toggle it', () => {
      openByClick();
      const bravo = options()[2];
      expect(bravo.getAttribute('aria-disabled')).toBe('true');
      expect(bravo.querySelector('.gh-multi-picker-reason')!.textContent).toContain('No answers recorded.');
      expect(options()[1].hasAttribute('aria-disabled')).toBe(false);

      click(bravo);
      expect(host.changes.length).toBe(0);
      expect(liveLine()).toBe('Bravo is unavailable. No answers recorded.');
      expect(listbox()).not.toBeNull();
    });
  });

  describe('pointer and live line', () => {
    it('a click toggles the option, keeps the list open and announces the result', () => {
      openByClick();
      click(options()[3]);
      expect(host.changes).toEqual([{ keys: ['d'] }]);
      expect(listbox()).not.toBeNull();
      expect(activeOption()!.textContent).toContain('Charlie');
      expect(liveLine()).toBe('Charlie selected. 1 of 5 selected.');

      click(options()[3]);
      expect(liveLine()).toBe('Charlie deselected. 0 of 5 selected.');
    });

    it('closes on a pointerdown outside', () => {
      openByClick();
      el.querySelector<HTMLElement>('.outside')!.dispatchEvent(new PointerEvent('pointerdown', { bubbles: true }));
      fixture.detectChanges();
      expect(listbox()).toBeNull();
      expect(host.changes.length).toBe(0);
    });

    it('has exactly one polite status line', () => {
      const lines = picker().querySelectorAll('[role="status"]');
      expect(lines.length).toBe(1);
    });
  });

  describe('option template', () => {
    it('replaces the default rendering and receives the selected state', () => {
      update({ useTemplate: true, selected: ['b'] });
      openByClick();
      const rows = options().map(o => o.querySelector('.custom-row')!.textContent!.trim());
      expect(rows[0]).toBe('Alpha is off');
      expect(rows[1]).toBe('Beta is on');
      expect(options()[0].querySelector('.model-option-tag')).toBeNull();
      expect(options()[2].querySelector('.gh-multi-picker-reason')).not.toBeNull();
    });
  });

  describe('chips', () => {
    it('show one chip per selected option with a named remove button and a tooltip', () => {
      update({ selected: ['a', 'b', 'd'] });
      const list = picker().querySelector<HTMLElement>('ul.gh-multi-picker-chips')!;
      expect(list.getAttribute('aria-label')).toBe('Selected sources');
      expect(trigger().querySelector('.gh-multi-picker-chip')).toBeNull();
      const buttons = chipButtons();
      expect(buttons.map(b => b.getAttribute('aria-label'))).toEqual(['Remove Alpha', 'Remove Beta', 'Remove Charlie']);
      // Only an option with a detail is described, by the chip's detail span.
      const detail = picker().querySelector<HTMLElement>(`#${buttons[0].getAttribute('aria-describedby')}`)!;
      expect(detail.classList).toContain('gh-multi-picker-detail');
      expect(detail.textContent!.trim()).toBe('Run #1');
      expect(detail.id).not.toBe(buttons[0].id);
      expect(detail.id).not.toBe(buttons[0].getAttribute('interestfor'));
      expect(buttons[1].hasAttribute('aria-describedby')).toBe(false);
      const tip =picker().querySelector<HTMLElement>(`#${buttons[1].getAttribute('interestfor')}`)!;
      expect(tip.getAttribute('popover')).toBe('hint');
      expect(tip.textContent!.trim()).toBe('Remove from the selection');
    });

    it('are absent with chips="none"', () => {
      update({ selected: ['a', 'b'], chips: 'none' });
      expect(picker().querySelector('.gh-multi-picker-chips')).toBeNull();
    });

    it('end in "+N more" above maxChips, which opens the list', () => {
      update({ selected: ['a', 'b', 'd', 'e'], maxChips: 2 });
      expect(chipButtons().length).toBe(2);
      const more = picker().querySelector<HTMLButtonElement>('.gh-multi-picker-chip-more button')!;
      expect(more.textContent!.replace(/\s+/g, ' ').trim()).toBe('+2 more selected sources');
      click(more);
      expect(listbox()).not.toBeNull();
      expect(document.activeElement).toBe(listbox());
    });

    it('removal emits once and moves focus to the next chip, else the trigger', () => {
      update({ selected: ['a', 'b'] });
      click(chipButtons()[0]);
      expect(host.changes).toEqual([{ keys: ['b'] }]);
      expect(liveLine()).toBe('Alpha removed. 1 of 5 selected.');
      expect(document.activeElement).toBe(chipButtons()[0]);
      expect(chipButtons()[0].getAttribute('aria-label')).toBe('Remove Beta');

      click(chipButtons()[0]);
      expect(host.changes[1]).toEqual({ keys: [] });
      expect(chipButtons().length).toBe(0);
      expect(document.activeElement).toBe(trigger());
    });

    it('refuse removal below min, aria-disabled with the reason', () => {
      update({ selected: ['a', 'b'], min: 2 });
      const button = chipButtons()[0];
      expect(button.getAttribute('aria-disabled')).toBe('true');
      expect(picker().querySelector(`#${button.getAttribute('interestfor')}`)!.textContent!.trim())
        .toBe('At least 2 sources must stay selected.');
      click(button);
      expect(host.changes.length).toBe(0);
    });

    it('render the chip template in place of the default content, keeping the remove button', () => {
      update({ useTemplate: true, selected: ['a'] });
      const chip = picker().querySelector<HTMLElement>('.gh-multi-picker-chip')!;
      expect(chip.querySelector('.custom-chip')!.textContent!.trim()).toBe('Alpha chip');
      expect(chip.querySelector('.gh-multi-picker-chip-label')).toBeNull();
      expect(chipButtons().map(b => b.getAttribute('aria-label'))).toEqual(['Remove Alpha']);
      // The template's context carries the id its detail element takes.
      const note = chip.querySelector<HTMLElement>('.custom-note')!;
      expect(note.id).toBeTruthy();
      expect(chipButtons()[0].getAttribute('aria-describedby')).toBe(note.id);
    });

    it('in card mode mark the host and show the selection count beside All and None', () => {
      const count = () => picker().querySelector<HTMLElement>('.gh-multi-picker-actions .gh-multi-picker-count');
      update({ selected: ['a', 'b'], max: 5 });
      expect(picker().classList).not.toContain('gh-multi-picker--cards');
      expect(count()).toBeNull();

      update({ chipStyle: 'card' });
      expect(picker().classList).toContain('gh-multi-picker--cards');
      expect(count()!.textContent!.trim()).toBe('2 selected · max 5');

      update({ max: null });
      expect(count()!.textContent!.trim()).toBe('2 selected');
    });
  });

  describe('status', () => {
    const status = () => picker().querySelector<HTMLElement>('.gh-multi-picker-status');
    const chipList = () => picker().querySelector<HTMLElement>('ul.gh-multi-picker-chips')!;

    it('is absent by default, leaving the trigger described by the host hint alone', () => {
      update({ selected: ['a', 'b'] });
      expect(status()).toBeNull();
      expect(trigger().getAttribute('aria-describedby')).toBe('pickHint');
      expect(chipList().hasAttribute('data-checking')).toBe(false);
    });

    it('while busy shows a ring and the text, describes the trigger and marks the chips as checking, unannounced', () => {
      update({ selected: ['a', 'b'], status: { tone: 'busy', text: 'Checking the sources' } });
      const s = status()!;
      expect(s.getAttribute('data-tone')).toBe('busy');
      expect(s.querySelector('.dc-ring')).not.toBeNull();
      expect(s.textContent!.trim()).toBe('Checking the sources');
      expect(trigger().getAttribute('aria-describedby')).toBe(`pickHint ${s.id}`);
      expect(chipList().hasAttribute('data-checking')).toBe(true);
      expect(s.hasAttribute('role')).toBe(false);
      expect(s.hasAttribute('aria-live')).toBe(false);
    });

    it('with a warning shows the triangle glyph and no ring, and the chips are not checking', () => {
      update({ selected: ['a', 'b'], status: { tone: 'warning', text: 'Warnings on 1 source' } });
      const s = status()!;
      expect(s.querySelector('.dc-ring')).toBeNull();
      expect(s.querySelector('svg path')).not.toBeNull();
      expect(s.querySelector('svg circle')).toBeNull();
      expect(chipList().hasAttribute('data-checking')).toBe(false);
    });

    it('renders without chips or All and None', () => {
      update({ selected: ['a'], chips: 'none', showAllNone: false, status: { tone: 'ok', text: 'All clear' } });
      expect(picker().querySelector('.gh-multi-picker-actions')).toBeNull();
      expect(picker().querySelector('.gh-multi-picker-chips')).toBeNull();
      expect(status()!.textContent!.trim()).toBe('All clear');
    });
  });

  describe('inside a dialog', () => {
    it('Escape closes the list only, and the dialog stays open', () => {
      const dialog = el.querySelector<HTMLDialogElement>('dialog.dlg')!;
      dialog.showModal();
      try {
        const inner = el.querySelector<HTMLElement>('.dialog-picker')!;
        openByClick(inner);
        expect(listbox(inner)).not.toBeNull();
        const event = key(listbox(inner)!, 'Escape');
        expect(event.defaultPrevented).toBe(true);
        expect(listbox(inner)).toBeNull();
        expect(dialog.open).toBe(true);
        expect(host.dialogKeys).not.toContain('Escape');
        expect(document.activeElement).toBe(trigger(inner));
      } finally {
        dialog.close();
      }
    });
  });
});

/** A picker with a plain search field as its tools, and a host-chosen list of visible keys. */
@Component({
  standalone: true,
  imports: [MultiPickerComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="vk-scroller" style="block-size: 150px; overflow-y: auto;">
      <div style="block-size: 600px;">
        <app-multi-picker class="vk-picker" label="Visible" summaryNoun="sources"
                          [options]="options" [selectedKeys]="selected" [max]="max"
                          [visibleKeys]="visible" [toolsTemplate]="tools"
                          [typeToTools]="typeToTools" [noMatchHint]="noMatchHint"
                          (selectionChange)="onChange($event)"></app-multi-picker>
      </div>
    </div>
    <ng-template #tools let-listboxId="listboxId" let-navigate="navigate">
      @if (fieldShown) {
        <label class="vk-label">Scope</label>
        <input type="search" class="vk-search" data-picker-search [attr.aria-controls]="listboxId"
               (keydown.enter)="$any($event).preventDefault(); navigate('commit')">
      }
    </ng-template>
  `
})
class VisibleKeysHostComponent {
  private cdr = inject(ChangeDetectorRef);
  options: MultiPickerOption[] = OPTIONS;
  selected: MultiPickerKey[] = [];
  max: number | null = null;
  visible: MultiPickerKey[] | null = ['d', 'b'];
  fieldShown = true;
  typeToTools: ((char: string) => boolean) | null = null;
  noMatchHint: string | null = null;
  changes: MultiPickerSelection[] = [];

  onChange(selection: MultiPickerSelection): void {
    this.changes.push(selection);
    this.selected = selection.keys;
    this.cdr.markForCheck();
  }

  set(changes: Partial<Pick<VisibleKeysHostComponent,
    'options' | 'selected' | 'max' | 'visible' | 'fieldShown' | 'typeToTools' | 'noMatchHint'>>): void {
    Object.assign(this, changes);
    this.cdr.markForCheck();
  }
}

describe('MultiPickerComponent visible keys and tools', () => {
  let fixture: ComponentFixture<VisibleKeysHostComponent>;
  let host: VisibleKeysHostComponent;
  let el: HTMLElement;

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [VisibleKeysHostComponent] }).compileComponents();
    fixture = TestBed.createComponent(VisibleKeysHostComponent);
    host = fixture.componentInstance;
    fixture.detectChanges();
    el = fixture.nativeElement as HTMLElement;
  });

  const picker = () => el.querySelector<HTMLElement>('.vk-picker')!;
  const component = () => fixture.debugElement.query(By.directive(MultiPickerComponent)).componentInstance as MultiPickerComponent;
  const labels = () => Array.from(picker().querySelectorAll<HTMLElement>('[role="option"] .model-name')).map(n => n.textContent!.trim());
  const chips = () => Array.from(picker().querySelectorAll<HTMLElement>('.gh-multi-picker-chip-label')).map(c => c.textContent!.trim());
  const liveLine = () => picker().querySelector<HTMLElement>(':scope > [role="status"]')!.textContent!.trim();
  const listbox = () => picker().querySelector<HTMLElement>('[role="listbox"]');
  const activeLabel = () => {
    const id = listbox()!.getAttribute('aria-activedescendant');
    return picker().querySelector<HTMLElement>(`#${id} .model-name`)!.textContent!.trim();
  };

  function update(changes: Parameters<VisibleKeysHostComponent['set']>[0]): void {
    host.set(changes);
    fixture.detectChanges();
  }

  function key(target: HTMLElement, keyName: string): void {
    target.dispatchEvent(new KeyboardEvent('keydown', { key: keyName, bubbles: true, cancelable: true }));
    fixture.detectChanges();
  }

  function open(): void {
    picker().querySelector<HTMLButtonElement>('.selector-trigger')!.click();
    fixture.detectChanges();
  }

  it('lists only the visible keys, in their order, while a hidden selected option keeps its chip', () => {
    update({ selected: ['a'] });
    open();
    expect(labels()).toEqual(['Charlie', 'Beta']);
    expect(chips()).toEqual(['Alpha']);
    expect(picker().querySelector('.gh-multi-picker-summary')!.textContent!.trim()).toBe('Alpha');
  });

  it('renders the tools template inside the popup and outside the listbox, and focuses its field', () => {
    open();
    const search = picker().querySelector<HTMLInputElement>('.vk-search')!;
    expect(search.closest('.selector-popup')).not.toBeNull();
    expect(search.closest('[role="listbox"]')).toBeNull();
    expect(search.getAttribute('aria-controls')).toBe(picker().querySelector('[role="listbox"]')!.id);
    expect(document.activeElement).toBe(search);
  });

  it('toggles the active option on Enter in the search field and stays open', () => {
    open();
    const search = picker().querySelector<HTMLInputElement>('.vk-search')!;
    search.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true, cancelable: true }));
    fixture.detectChanges();
    expect(host.selected).toEqual(['d']);
    expect(picker().querySelector('[role="listbox"]')).not.toBeNull();
  });

  it('offers a typed character to typeToTools while no search field is rendered, and type-ahead when it declines', () => {
    const typed: string[] = [];
    let accept = true;
    update({ fieldShown: false, typeToTools: char => { typed.push(char); return accept; } });
    open();
    const box = listbox()!;
    expect(document.activeElement).toBe(box);
    expect(activeLabel()).toBe('Charlie');

    key(box, 'b');
    expect(typed).toEqual(['b']);
    expect(activeLabel()).toBe('Charlie');

    accept = false;
    key(box, 'b');
    expect(typed).toEqual(['b', 'b']);
    expect(activeLabel()).toBe('Beta');

    update({ fieldShown: true });
    key(box, 'x');
    expect(typed).toEqual(['b', 'b']);
    expect(picker().querySelector<HTMLInputElement>('.vk-search')!.value).toBe('x');
  });

  it('fits the listbox to the visible options as they narrow, and says when none is left', () => {
    update({ noMatchHint: 'No sources match.' });
    open();
    const before = listbox()!.getBoundingClientRect().height;
    update({ visible: ['d'] });
    expect(labels()).toEqual(['Charlie']);
    expect(listbox()!.getBoundingClientRect().height).toBeLessThan(before - 1);
    expect(listbox()!.hasAttribute('aria-describedby')).toBe(false);

    update({ visible: [] });
    const box = listbox()!;
    const hint = picker().querySelector<HTMLElement>(`#${box.getAttribute('aria-describedby')}`)!;
    expect(box.contains(hint)).toBe(true);
    expect(hint.textContent!.trim()).toBe('No sources match.');
  });

  it('keeps focus where it is when a label in the tools is pressed', () => {
    open();
    const onLabel = new MouseEvent('mousedown', { bubbles: true, cancelable: true });
    picker().querySelector<HTMLElement>('.vk-label')!.dispatchEvent(onLabel);
    expect(onLabel.defaultPrevented).toBe(true);
    const inField = new MouseEvent('mousedown', { bubbles: true, cancelable: true });
    picker().querySelector<HTMLElement>('.vk-search')!.dispatchEvent(inField);
    expect(inField.defaultPrevented).toBe(false);
  });

  it('stays open through a press that leaves focus on the body, and gives focus back to the listbox', async () => {
    open();
    picker().querySelector<HTMLElement>('.vk-search')!.dispatchEvent(new PointerEvent('pointerdown', { bubbles: true }));
    (document.activeElement as HTMLElement | null)?.blur();
    picker().querySelector<HTMLElement>('.selector-popup')!
      .dispatchEvent(new FocusEvent('focusout', { bubbles: true, relatedTarget: null }));
    fixture.detectChanges();
    expect(listbox()).not.toBeNull();

    document.dispatchEvent(new PointerEvent('pointerup', { bubbles: true }));
    await new Promise(r => setTimeout(r));
    fixture.detectChanges();
    expect(listbox()).not.toBeNull();
    expect(document.activeElement).toBe(listbox());
  });

  it('still closes on a press outside', () => {
    open();
    document.body.dispatchEvent(new PointerEvent('pointerdown', { bubbles: true }));
    fixture.detectChanges();
    expect(listbox()).toBeNull();
  });

  it('scrolls only the listbox on arrow keys, never the container around the picker', () => {
    const many = Array.from({ length: 30 }, (_, i) => ({ key: `k${i}`, label: `Item ${i}` }));
    update({ options: many, visible: null });
    open();
    const scroller = el.querySelector<HTMLElement>('.vk-scroller')!;
    scroller.scrollTop = 0;
    const box = listbox()!;
    key(box, 'End');
    expect(box.scrollTop).toBeGreaterThan(0);
    expect(scroller.scrollTop).toBe(0);
    key(box, 'Home');
    const first = box.querySelector<HTMLElement>('[role="option"]')!;
    expect(first.getBoundingClientRect().top).toBeGreaterThanOrEqual(box.getBoundingClientRect().top + box.clientTop - 0.5);
    expect(box.scrollTop).toBeLessThan(first.getBoundingClientRect().height);
    expect(scroller.scrollTop).toBe(0);
  });

  it('adds the enabled unselected keys up to max with one change and one announcement', () => {
    update({ selected: ['a'], max: 3, visible: ['b', 'c', 'd', 'e'] });
    component().addToSelection(['e', 'c', 'b', 'd']);
    fixture.detectChanges();
    expect(host.changes.length).toBe(1);
    expect(host.selected).toEqual(['a', 'b', 'd']);
    expect(liveLine()).toBe('2 added. 3 of 5 selected.');

    component().addToSelection(['e']);
    fixture.detectChanges();
    expect(host.changes.length).toBe(1);
    expect(liveLine()).toBe('At most 3 sources can be selected.');

    component().addToSelection(['a', 'c']);
    fixture.detectChanges();
    expect(liveLine()).toBe('All shown sources are already selected.');
  });

  it('shows the tools on every opening without a toggle label, and no filter button', () => {
    open();
    expect(picker().querySelector('.vk-search')).not.toBeNull();
    expect(picker().querySelector('.picker-tools-toggle-btn')).toBeNull();
  });
});

/** A picker whose tools sit behind its filter button, inside a dialog. */
@Component({
  standalone: true,
  imports: [MultiPickerComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <dialog open class="tg-dlg" (keydown)="dialogKeys.push($any($event).key)">
      <app-multi-picker class="tg-picker" label="Toggled" summaryNoun="sources"
                        [options]="options" [selectedKeys]="selected" [toolsTemplate]="tools"
                        [toolsToggleLabel]="toggleLabel" [toolsToggleActive]="active"
                        [toolsStartExpanded]="startExpanded" toolsId="tg-tools"
                        (toolsExpandedChange)="expandedChanges.push($event)"></app-multi-picker>
    </dialog>
    <ng-template #tools>
      <div id="tg-tools" class="tg-tools">
        <input type="search" class="tg-search" data-picker-search value="be">
      </div>
    </ng-template>
  `
})
class ToggleHostComponent {
  private cdr = inject(ChangeDetectorRef);
  options: MultiPickerOption[] = OPTIONS;
  selected: MultiPickerKey[] = [];
  toggleLabel: string | null = 'Filter and sort models';
  active = false;
  startExpanded = false;
  expandedChanges: boolean[] = [];
  dialogKeys: string[] = [];

  set(changes: Partial<Pick<ToggleHostComponent, 'toggleLabel' | 'active' | 'startExpanded'>>): void {
    Object.assign(this, changes);
    this.cdr.markForCheck();
  }
}

describe('MultiPickerComponent tools behind a filter button', () => {
  let fixture: ComponentFixture<ToggleHostComponent>;
  let host: ToggleHostComponent;
  let el: HTMLElement;

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [ToggleHostComponent] }).compileComponents();
    fixture = TestBed.createComponent(ToggleHostComponent);
    host = fixture.componentInstance;
    fixture.detectChanges();
    el = fixture.nativeElement as HTMLElement;
  });

  const picker = () => el.querySelector<HTMLElement>('.tg-picker')!;
  const component = () => fixture.debugElement.query(By.directive(MultiPickerComponent)).componentInstance as MultiPickerComponent;
  const trigger = () => picker().querySelector<HTMLButtonElement>('.selector-trigger')!;
  const button = () => picker().querySelector<HTMLButtonElement>('.picker-tools-toggle-btn')!;
  const listbox = () => picker().querySelector<HTMLElement>('[role="listbox"]');
  const tools = () => picker().querySelector<HTMLElement>('.tg-tools');

  function update(changes: Parameters<ToggleHostComponent['set']>[0]): void {
    host.set(changes);
    fixture.detectChanges();
  }

  function open(): void {
    trigger().click();
    fixture.detectChanges();
  }

  function press(): void {
    button().click();
    fixture.detectChanges();
  }

  it('renders the filter button after the trigger in the field, and no tools on a trigger opening', () => {
    expect(button().getAttribute('aria-label')).toBe('Filter and sort models');
    expect(button().closest('.gh-multi-picker-field')).not.toBeNull();
    expect(trigger().contains(button())).toBe(false);
    expect(trigger().compareDocumentPosition(button()) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    expect(picker().querySelector(`#${button().getAttribute('interestfor')}`)!.textContent!.trim()).toBe('Filter and sort');
    open();
    expect(listbox()).not.toBeNull();
    expect(listbox()!.closest('.selector-popup')).not.toBeNull();
    expect(tools()).toBeNull();
    expect(document.activeElement).toBe(listbox());
  });

  it('expands and collapses the tools of an open picker, emitting each change and keeping focus on the button', () => {
    open();
    button().focus();
    press();
    expect(tools()).not.toBeNull();
    expect(button().getAttribute('aria-expanded')).toBe('true');
    expect(button().getAttribute('aria-controls')).toBe('tg-tools');
    expect(document.activeElement).toBe(button());

    press();
    expect(tools()).toBeNull();
    expect(button().getAttribute('aria-expanded')).toBe('false');
    expect(button().hasAttribute('aria-controls')).toBe(false);
    expect(listbox()).not.toBeNull();
    expect(document.activeElement).toBe(button());
    expect(host.expandedChanges).toEqual([true, false]);
  });

  it('opens a closed picker with the tools expanded from the filter button', () => {
    press();
    expect(listbox()).not.toBeNull();
    expect(tools()).not.toBeNull();
    expect(host.expandedChanges).toEqual([true]);
  });

  it('collapses with focus in the tools by moving it to the button first', () => {
    update({ startExpanded: true });
    open();
    expect(document.activeElement).toBe(picker().querySelector('.tg-search'));
    press();
    expect(tools()).toBeNull();
    expect(listbox()).not.toBeNull();
    expect(document.activeElement).toBe(button());
  });

  it('opens expanded from the trigger with toolsStartExpanded, and is collapsed again after closing', () => {
    update({ startExpanded: true });
    open();
    expect(tools()).not.toBeNull();
    expect(host.expandedChanges).toEqual([]);
    open();
    expect(listbox()).toBeNull();
    update({ startExpanded: false });
    open();
    expect(tools()).toBeNull();
  });

  it('closes on Escape on the filter button without closing the enclosing dialog', () => {
    open();
    button().focus();
    const event = new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true });
    button().dispatchEvent(event);
    fixture.detectChanges();
    expect(event.defaultPrevented).toBe(true);
    expect(listbox()).toBeNull();
    expect(host.dialogKeys).toEqual([]);
    expect(el.querySelector<HTMLDialogElement>('.tg-dlg')!.open).toBe(true);
    expect(document.activeElement).toBe(trigger());
  });

  it('expands the tools on showTools() and focuses the field with the caret at the end', () => {
    open();
    component().showTools();
    fixture.detectChanges();
    const field = picker().querySelector<HTMLInputElement>('.tg-search')!;
    expect(document.activeElement).toBe(field);
    expect(field.selectionStart).toBe(2);
    expect(host.expandedChanges).toEqual([true]);
  });

  it('shows the dot while active', () => {
    expect(picker().querySelector('.picker-tools-toggle-dot')).toBeNull();
    update({ active: true });
    expect(picker().querySelector('.picker-tools-toggle-dot')).not.toBeNull();
  });

  it('has no filter button and shows the tools on every opening when the label is null', () => {
    update({ toggleLabel: null });
    expect(picker().querySelector('.picker-tools-toggle-btn')).toBeNull();
    open();
    expect(tools()).not.toBeNull();
  });
});
