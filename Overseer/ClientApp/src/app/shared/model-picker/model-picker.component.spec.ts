import { ChangeDetectionStrategy, ChangeDetectorRef, Component, inject } from '@angular/core';
import { ComponentFixture, TestBed, fakeAsync, tick } from '@angular/core/testing';
import { userEvent } from 'vitest/browser';

import {
  ModelPickerComponent,
  ModelPickerKey,
  ModelPickerModel,
  ModelPickerOption,
  ModelPickerSelection,
  toModelPickerOptions
} from './model-picker.component';
import { ModelPickerViewTools } from './model-picker-view';

interface TestModel extends ModelPickerModel {
  id: number;
}

const MODELS: TestModel[] = [
  { id: 1, displayName: 'Alpha', provider: 'OpenAI', thinkingLevel: 'high', reasoningMode: 'pro', parallelExecutionMode: 0,
    effectiveInputPricePerMillion: 5, effectiveOutputPricePerMillion: 25 },
  { id: 2, displayName: 'Beta', provider: 'Anthropic', parallelExecutionMode: 1 },
  { id: 3, displayName: 'Bravo', provider: 'Google', parallelExecutionMode: 2 },
  { id: 4, modelId: 'charlie-model', provider: 'Google' }
];

const FLAGGED: TestModel[] = [
  { id: 11, displayName: 'Old Flash', provider: 'Google', thinkingLevel: 'high',
    modelAvailability: { status: 'retired', needsAttention: true, retiredOn: '2026-09-30' } },
  { id: 12, displayName: 'Mystery', provider: 'OpenAI', modelAvailability: { status: 'notInCatalog', needsAttention: true } },
  { id: 13, displayName: 'Fine', provider: 'Anthropic', modelAvailability: { status: 'available', needsAttention: false } }
];

/** Feeds every committed choice back into `selectedKey`, the way a real host does. */
@Component({
  standalone: true,
  imports: [ModelPickerComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="outer" (keydown)="outerKeys.push($any($event).key)">
      <label id="pickerALabel">Model A</label>
      <app-model-picker class="picker-a marker" labelledBy="pickerALabel" describedBy="pickerAHint"
                        [noneLabel]="noneLabel" [noneMuted]="noneMutedA" [options]="optionsA" [selectedKey]="selectedA"
                        [showPrice]="true" [showParallel]="true" [disabled]="disabledA" [viewTools]="viewToolsA"
                        (selectionChange)="onSelect($event)"></app-model-picker>
      <span id="pickerAHint">Hint A</span>
      <app-model-picker class="picker-b" label="Model B" emptyHint="Nothing to choose from."
                        [options]="optionsB" [selectedKey]="null"></app-model-picker>
      <app-model-picker class="picker-c" variant="compact" label="Model C" [options]="optionsA"
                        [selectedKey]="1"></app-model-picker>
      <app-model-picker class="picker-d" variant="compact" label="Model D" [narrowHidesBadges]="true"
                        [options]="optionsD" [selectedKey]="11"></app-model-picker>
      <button type="button" class="outside">Outside</button>
    </div>
  `
})
class HostComponent {
  private cdr = inject(ChangeDetectorRef);
  noneLabel: string | null = null;
  noneMutedA = true;
  optionsA: ModelPickerOption<TestModel>[] = toModelPickerOptions(MODELS);
  optionsB: ModelPickerOption<TestModel>[] = [];
  optionsD: ModelPickerOption<TestModel>[] = toModelPickerOptions(FLAGGED);
  selectedA: ModelPickerKey | null = null;
  disabledA = false;
  viewToolsA: ModelPickerViewTools = 'auto';
  selections: ModelPickerSelection<TestModel>[] = [];
  outerKeys: string[] = [];

  onSelect(selection: ModelPickerSelection<TestModel>): void {
    this.selections.push(selection);
    this.selectedA = selection.key;
    this.cdr.markForCheck();
  }

  set(changes: Partial<Pick<HostComponent, 'noneLabel' | 'noneMutedA' | 'optionsA' | 'selectedA' | 'disabledA' | 'viewToolsA'>>): void {
    Object.assign(this, changes);
    this.cdr.markForCheck();
  }
}

describe('ModelPickerComponent', () => {
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

  const pickerA = () => el.querySelector<HTMLElement>('.picker-a')!;
  const pickerB = () => el.querySelector<HTMLElement>('.picker-b')!;
  const trigger = (picker = pickerA()) => picker.querySelector<HTMLButtonElement>('.selector-trigger')!;
  const listbox = (picker = pickerA()) => picker.querySelector<HTMLElement>('[role="listbox"]');
  const options = (picker = pickerA()) => Array.from(picker.querySelectorAll<HTMLElement>('[role="option"]'));
  const activeOption = (picker = pickerA()) => {
    const id = listbox(picker)?.getAttribute('aria-activedescendant');
    return id ? picker.querySelector<HTMLElement>(`#${id}`) : null;
  };

  function update(changes: Partial<Pick<HostComponent, 'noneLabel' | 'noneMutedA' | 'optionsA' | 'selectedA' | 'disabledA' | 'viewToolsA'>>): void {
    host.set(changes);
    fixture.detectChanges();
  }

  function openByClick(picker = pickerA()): void {
    trigger(picker).click();
    fixture.detectChanges();
  }

  function key(target: HTMLElement, keyName: string, init: KeyboardEventInit = {}): KeyboardEvent {
    const event = new KeyboardEvent('keydown', { key: keyName, bubbles: true, cancelable: true, ...init });
    target.dispatchEvent(event);
    fixture.detectChanges();
    return event;
  }

  describe('trigger', () => {
    it('is a listbox button whose aria-controls exists only while open', () => {
      const button = trigger();
      expect(button.getAttribute('aria-haspopup')).toBe('listbox');
      expect(button.getAttribute('aria-expanded')).toBe('false');
      expect(button.hasAttribute('aria-controls')).toBe(false);

      openByClick();
      expect(button.getAttribute('aria-expanded')).toBe('true');
      expect(button.getAttribute('aria-controls')).toBe(listbox()!.id);
    });

    it('is named by the visible label and its own content, and described by the hint', () => {
      const button = trigger();
      expect(button.getAttribute('aria-labelledby')).toBe(`pickerALabel ${button.id}`);
      expect(button.getAttribute('aria-describedby')).toBe('pickerAHint');
    });

    it('falls back to a visually hidden label when no labelledBy is given', () => {
      const button = trigger(pickerB());
      const labelId = button.getAttribute('aria-labelledby')!.split(' ')[0];
      const label = pickerB().querySelector<HTMLElement>(`#${labelId}`)!;
      expect(label.textContent!.trim()).toBe('Model B');
      expect(label.classList).toContain('visually-hidden');
    });

    it('shows a muted placeholder, then the muted none label, while nothing is selected', () => {
      let name = trigger().querySelector('.model-name')!;
      expect(name.textContent!.trim()).toBe('Select Model');
      expect(name.classList).toContain('text-muted');

      update({ noneLabel: 'None — nobody' });
      name = trigger().querySelector('.model-name')!;
      expect(name.textContent!.trim()).toBe('None — nobody');
      expect(name.classList).toContain('text-muted');
    });

    it('shows the selected model with visually hidden badge prefixes', () => {
      update({ selectedA: 1 });
      const text = trigger().textContent!.replace(/\s+/g, ' ');
      expect(trigger().querySelector('.model-name')!.classList).not.toContain('text-muted');
      expect(text).toContain('Alpha');
      expect(text).toContain('thinking level High');
      expect(text).toContain('reasoning mode pro');
      expect(text).toContain('price $5.00/$25.00 per 1M');
      expect(text).toContain('parallel execution Sequential, disabled for this key');
      expect(trigger().querySelector('.parallel-badge')!.hasAttribute('title')).toBe(false);
      expect(trigger().querySelector('.visually-hidden')).not.toBeNull();
    });

    it('merges the host marker classes with custom-model-selector', () => {
      expect(pickerA().classList).toContain('custom-model-selector');
      expect(pickerA().classList).toContain('marker');
    });
  });

  describe('none option strength', () => {
    it('draws the none option and a trigger showing it muted by default', () => {
      update({ noneLabel: 'None — nobody' });
      expect(trigger().querySelector('.model-name')!.classList).toContain('text-muted');

      openByClick();
      const none = options()[0];
      expect(none.textContent!.trim()).toBe('None — nobody');
      expect(none.querySelector('.model-name')!.classList).toContain('text-muted');
    });

    it('draws them at full strength with noneMuted false, keeping a placeholder muted', () => {
      update({ noneMutedA: false });
      let name = trigger().querySelector('.model-name')!;
      expect(name.textContent!.trim()).toBe('Select Model');
      expect(name.classList).toContain('text-muted');

      update({ noneLabel: 'Highest score' });
      name = trigger().querySelector('.model-name')!;
      expect(name.textContent!.trim()).toBe('Highest score');
      expect(name.classList).not.toContain('text-muted');

      openByClick();
      const none = options()[0];
      expect(none.textContent!.trim()).toBe('Highest score');
      expect(none.querySelector('.model-name')!.classList).not.toContain('text-muted');
    });
  });

  describe('trigger layout', () => {
    const LONG_NAME = 'Claude Opus Extended Thinking Preview 2026';
    const BADGES = '.thinking-badge, .reasoning-badge, .provider-badge, .price-badge, .parallel-badge';

    it('shows a long name whole in a narrow host, its badges wrapping under it flush with the name', () => {
      update({
        optionsA: toModelPickerOptions([{ id: 7, displayName: LONG_NAME, provider: 'Anthropic', thinkingLevel: 'high',
          effectiveInputPricePerMillion: 5, effectiveOutputPricePerMillion: 25 }]),
        selectedA: 7
      });
      pickerA().style.inlineSize = '220px';

      const button = trigger();
      const content = button.querySelector<HTMLElement>('.selector-trigger-content')!;
      const name = content.querySelector<HTMLElement>('.model-name')!;
      expect(name.textContent!.trim()).toBe(LONG_NAME);
      expect(button.scrollWidth).toBeLessThanOrEqual(button.clientWidth + 1);
      expect(name.scrollWidth).toBeLessThanOrEqual(name.clientWidth + 1);
      const box = button.getBoundingClientRect();
      const nameBox = name.getBoundingClientRect();
      expect(nameBox.left).toBeGreaterThanOrEqual(box.left);
      expect(nameBox.right).toBeLessThanOrEqual(box.right);
      expect(nameBox.top).toBeGreaterThanOrEqual(box.top);
      expect(nameBox.bottom).toBeLessThanOrEqual(box.bottom);

      const chevron = button.querySelector('.chevron')!;
      expect(content.contains(chevron)).toBe(false);
      expect(chevron.parentElement).toBe(button);

      const badges = Array.from(content.querySelectorAll<HTMLElement>(BADGES));
      expect(badges.length).toBeGreaterThanOrEqual(3);
      const wrapped = badges.find(badge => badge.getBoundingClientRect().top > nameBox.top + 1);
      expect(wrapped).toBeDefined();
      expect(Math.abs(wrapped!.getBoundingClientRect().left - nameBox.left)).toBeLessThanOrEqual(1);
    });

    it('keeps the compact variant on one line', () => {
      const picker = el.querySelector<HTMLElement>('.picker-c')!;
      expect(picker.classList).toContain('compact');
      const button = trigger(picker);
      const content = button.querySelector<HTMLElement>('.selector-trigger-content')!;
      expect(getComputedStyle(content).flexWrap).toBe('nowrap');
      expect(content.querySelectorAll(BADGES).length).toBeGreaterThan(0);

      const style = getComputedStyle(button);
      const fontSize = parseFloat(style.fontSize);
      const lineHeight = parseFloat(style.lineHeight) || fontSize * 1.2;
      const chrome = parseFloat(style.paddingTop) + parseFloat(style.paddingBottom)
        + parseFloat(style.borderTopWidth) + parseFloat(style.borderBottomWidth);
      expect(button.getBoundingClientRect().height).toBeLessThan(chrome + 2 * lineHeight);
    });
  });

  describe('options', () => {
    it('are options with unique ids and aria-selected on the selected one', () => {
      update({ selectedA: 2 });
      openByClick();
      const all = options();
      expect(all.length).toBe(4);
      expect(new Set(all.map(o => o.id)).size).toBe(4);
      expect(all.map(o => o.getAttribute('aria-selected'))).toEqual(['false', 'true', 'false', 'false']);
      expect(all.every(o => !o.hasAttribute('tabindex'))).toBe(true);
      expect(all.some(o => o.classList.contains('selected'))).toBe(false);
    });

    it('render price and parallel badges only when asked', () => {
      openByClick();
      expect(options()[0].querySelector('.price-badge')).not.toBeNull();
      expect(options()[1].querySelector('.parallel-badge.badge-on-request')).not.toBeNull();
      expect(options()[2].querySelector('.parallel-badge')).toBeNull();

      openByClick(pickerB());
      expect(pickerB().querySelector('.price-badge')).toBeNull();
    });

    it('group consecutive options under a named role="group"', () => {
      update({
        optionsA: [
          ...toModelPickerOptions(MODELS.slice(0, 2), 'Your Models', 'u_'),
          ...toModelPickerOptions(MODELS.slice(2), 'System Models', 's_')
        ]
      });
      openByClick();
      const groups = Array.from(listbox()!.querySelectorAll<HTMLElement>('[role="group"]'));
      expect(groups.length).toBe(2);
      const names = groups.map(g => pickerA().querySelector(`#${g.getAttribute('aria-labelledby')}`)!.textContent!.trim());
      expect(names).toEqual(['Your Models', 'System Models']);
      expect(groups[0].querySelectorAll('[role="option"]').length).toBe(2);
      expect(options()[0].id).toBeTruthy();
    });

    it('keys options by prefix + id, or by the numeric id', () => {
      expect(toModelPickerOptions(MODELS.slice(0, 1), 'G', 'u_')[0]).toEqual(expect.objectContaining({ key: 'u_1', group: 'G' }));
      expect(toModelPickerOptions(MODELS.slice(0, 1))[0].key).toBe(1);
    });

    it('describe an empty list with the empty hint', () => {
      openByClick(pickerB());
      const box = listbox(pickerB())!;
      const hint = pickerB().querySelector<HTMLElement>(`#${box.getAttribute('aria-describedby')}`)!;
      expect(hint.textContent!.trim()).toBe('Nothing to choose from.');
      expect(options(pickerB()).length).toBe(0);
    });

    it('get distinct ids in two pickers of one host', () => {
      openByClick();
      openByClick(pickerB());
      expect(trigger().id).not.toBe(trigger(pickerB()).id);
    });
  });

  describe('keyboard', () => {
    it('opens on click with focus on the listbox and the selected option active', () => {
      update({ selectedA: 3 });
      openByClick();
      expect(document.activeElement).toBe(listbox());
      expect(activeOption()!.textContent).toContain('Bravo');
    });

    it('opens on ArrowDown at the first option and on ArrowUp at the last', () => {
      key(trigger(), 'ArrowDown');
      expect(listbox()).not.toBeNull();
      expect(activeOption()!.textContent).toContain('Alpha');

      key(listbox()!, 'Escape');
      key(trigger(), 'ArrowUp');
      expect(activeOption()!.textContent).toContain('charlie-model');
    });

    it('moves without wrapping, and by Home, End, PageDown and PageUp', () => {
      openByClick();
      const box = listbox()!;
      key(box, 'ArrowUp');
      expect(activeOption()!.textContent).toContain('Alpha');
      key(box, 'End');
      expect(activeOption()!.textContent).toContain('charlie-model');
      key(box, 'ArrowDown');
      expect(activeOption()!.textContent).toContain('charlie-model');
      key(box, 'Home');
      expect(activeOption()!.textContent).toContain('Alpha');
      key(box, 'PageDown');
      expect(activeOption()!.textContent).toContain('charlie-model');
      key(box, 'PageUp');
      expect(activeOption()!.textContent).toContain('Alpha');
      expect(activeOption()!.classList).toContain('is-active');
    });

    for (const commitKey of ['Enter', ' ']) {
      it(`commits with ${commitKey === ' ' ? 'Space' : commitKey}: emits once, closes and refocuses the trigger`, () => {
        openByClick();
        key(listbox()!, 'ArrowDown');
        const event = key(listbox()!, commitKey);
        expect(event.defaultPrevented).toBe(true);
        expect(host.selections.length).toBe(1);
        expect(host.selections[0].key).toBe(2);
        expect(host.selections[0].model!.displayName).toBe('Beta');
        expect(listbox()).toBeNull();
        expect(document.activeElement).toBe(trigger());
        expect(trigger().querySelector('.model-name')!.textContent!.trim()).toBe('Beta');
      });
    }

    it('closes on Escape without emitting, and keeps the key from the parent', () => {
      openByClick();
      key(listbox()!, 'ArrowDown');
      const event = key(listbox()!, 'Escape');
      expect(event.defaultPrevented).toBe(true);
      expect(host.outerKeys).not.toContain('Escape');
      expect(host.selections.length).toBe(0);
      expect(listbox()).toBeNull();
      expect(document.activeElement).toBe(trigger());
    });

    it('closes on Tab without emitting or preventing the default', () => {
      openByClick();
      const event = key(listbox()!, 'Tab');
      expect(event.defaultPrevented).toBe(false);
      expect(host.selections.length).toBe(0);
      expect(listbox()).toBeNull();
    });

    it('type-ahead matches the start of a name and a repeated letter cycles', fakeAsync(() => {
      update({ viewToolsA: 'never' });
      openByClick();
      const box = listbox()!;
      key(box, 'b');
      expect(activeOption()!.textContent).toContain('Beta');
      key(box, 'b');
      expect(activeOption()!.textContent).toContain('Bravo');
      tick(600);
      key(box, 'c');
      expect(activeOption()!.textContent).toContain('charlie-model');
      tick(600);
      key(box, 'b');
      key(box, 'r');
      expect(activeOption()!.textContent).toContain('Bravo');
      tick(600);
    }));
  });

  describe('option tags', () => {
    const TAGGED: ModelPickerOption<TestModel>[] = [
      { key: 'Assessor', tag: 'Assessor A', model: MODELS[0] },
      { key: 'CoAssessor', tag: 'Co-assessor B', model: MODELS[1] },
      { key: 'Panel', tag: 'Panel', model: { id: 9, displayName: 'Mean of Alpha and Beta' } },
      { key: 'Plain', model: MODELS[2] }
    ];

    it('render the tag before the name, as the start of the option text', () => {
      update({ optionsA: TAGGED });
      openByClick();
      const first = options()[0];
      const tag = first.querySelector('.model-option-tag')!;
      expect(tag.textContent!.trim()).toBe('Assessor A');
      expect(tag.nextElementSibling!.classList).toContain('model-name');
      expect(first.textContent!.trim().startsWith('Assessor A')).toBe(true);
      expect(options()[3].querySelector('.model-option-tag')).toBeNull();
    });

    it('show the selected tag on the trigger, and none for an untagged or empty selection', () => {
      update({ optionsA: TAGGED, selectedA: 'CoAssessor' });
      const tag = trigger().querySelector('.model-option-tag')!;
      expect(tag.textContent!.trim()).toBe('Co-assessor B');
      expect(tag.nextElementSibling!.textContent!.trim()).toBe('Beta');

      update({ selectedA: 'Plain' });
      expect(trigger().querySelector('.model-option-tag')).toBeNull();

      update({ selectedA: null });
      expect(trigger().querySelector('.model-option-tag')).toBeNull();
    });

    it('take part in type-ahead', fakeAsync(() => {
      update({ optionsA: TAGGED, viewToolsA: 'never' });
      openByClick();
      key(listbox()!, 'p');
      expect(activeOption()!.textContent).toContain('Panel');
      tick(600);
    }));

    it('are absent from an untagged picker', () => {
      update({ selectedA: 1 });
      openByClick();
      expect(pickerA().querySelector('.model-option-tag')).toBeNull();
    });
  });

  describe('availability chip', () => {
    const chip = (scope: Element) => scope.querySelector<HTMLElement>('.model-option-notice');
    const name = (el: Element) => el.textContent!.replace(/\s+/g, ' ').trim();

    it('leads the badges of an option that needs attention, with its tone, glyph and hidden prefix', () => {
      update({ optionsA: toModelPickerOptions(FLAGGED) });
      openByClick();
      const [old, mystery, fine] = options();

      const removed = chip(old)!;
      expect(removed.getAttribute('data-tone')).toBe('warning');
      expect(removed.querySelector('svg')!.getAttribute('aria-hidden')).toBe('true');
      expect(removed.querySelector('.visually-hidden')!.textContent).toBe('status: ');
      expect(name(removed)).toBe('status: Removed');
      const thinking = old.querySelector('.thinking-badge')!;
      expect(removed.compareDocumentPosition(thinking) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
      expect(old.querySelector('.model-name')!.compareDocumentPosition(removed) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();

      expect(chip(mystery)!.getAttribute('data-tone')).toBe('info');
      expect(name(chip(mystery)!)).toBe('status: Not in catalog');
      expect(chip(fine)).toBeNull();
    });

    it("is part of the option's accessible name", () => {
      update({ optionsA: toModelPickerOptions(FLAGGED) });
      openByClick();
      const option = options()[0];
      expect(option.hasAttribute('aria-label')).toBe(false);
      expect(option.hasAttribute('aria-labelledby')).toBe(false);
      const text = name(option);
      expect(text.indexOf('Old Flash')).toBe(0);
      expect(text.indexOf('status: Removed')).toBeGreaterThan(0);
      expect(text.indexOf('thinking level High')).toBeGreaterThan(text.indexOf('status: Removed'));
    });

    it("shows on the trigger of a selected model, as part of the trigger's accessible name", () => {
      update({ optionsA: toModelPickerOptions(FLAGGED), selectedA: 12 });
      const button = trigger();
      expect(chip(button)!.getAttribute('data-tone')).toBe('info');
      expect(button.getAttribute('aria-labelledby')).toBe(`pickerALabel ${button.id}`);
      expect(button.hasAttribute('aria-label')).toBe(false);
      const text = name(button);
      expect(text).toContain('Mystery');
      expect(text.indexOf('status: Not in catalog')).toBeGreaterThan(text.indexOf('Mystery'));

      update({ selectedA: 13 });
      expect(chip(trigger())).toBeNull();
    });

    it('stays visible where narrowHidesBadges hides the provider badge', () => {
      const picker = el.querySelector<HTMLElement>('.picker-d')!;
      const button = trigger(picker);
      expect(window.innerWidth).toBeLessThanOrEqual(992);
      expect(getComputedStyle(button.querySelector('.provider-badge')!).display).toBe('none');
      const notice = chip(button)!;
      expect(getComputedStyle(notice).display).not.toBe('none');
      expect(notice.getBoundingClientRect().width).toBeGreaterThan(0);
    });
  });

  describe('pointer and focus', () => {
    it('commits a clicked option', () => {
      openByClick();
      options()[2].click();
      fixture.detectChanges();
      expect(host.selections.map(s => s.key)).toEqual([3]);
      expect(listbox()).toBeNull();
    });

    it('emits key null for the none option', () => {
      update({ noneLabel: 'None — nobody', selectedA: 1 });
      openByClick();
      expect(options().length).toBe(5);
      options()[0].click();
      fixture.detectChanges();
      expect(host.selections[0]).toEqual({ key: null, model: null });
      expect(trigger().querySelector('.model-name')!.textContent!.trim()).toBe('None — nobody');
    });

    it('closes on a pointerdown outside without moving focus', () => {
      openByClick();
      el.querySelector<HTMLElement>('.outside')!.dispatchEvent(new PointerEvent('pointerdown', { bubbles: true }));
      fixture.detectChanges();
      expect(listbox()).toBeNull();
      expect(host.selections.length).toBe(0);
    });

    it('closes the first picker when a second one is opened', () => {
      openByClick();
      trigger(pickerB()).dispatchEvent(new PointerEvent('pointerdown', { bubbles: true }));
      openByClick(pickerB());
      expect(listbox()).toBeNull();
      expect(listbox(pickerB())).not.toBeNull();
    });

    it('closes when focus leaves for an element outside', () => {
      openByClick();
      el.querySelector<HTMLElement>('.outside')!.focus();
      fixture.detectChanges();
      expect(listbox()).toBeNull();
    });
  });

  describe('disabled', () => {
    it('marks the trigger aria-disabled, keeps it focusable, and opens neither on click nor on ArrowDown', () => {
      expect(trigger().hasAttribute('aria-disabled')).toBe(false);
      update({ disabledA: true });
      const button = trigger();
      expect(button.getAttribute('aria-disabled')).toBe('true');
      expect(button.disabled).toBe(false);
      button.focus();
      expect(document.activeElement).toBe(button);

      openByClick();
      expect(listbox()).toBeNull();
      const event = key(button, 'ArrowDown');
      expect(listbox()).toBeNull();
      expect(event.defaultPrevented).toBe(false);
      expect(button.getAttribute('aria-expanded')).toBe('false');
    });

    it('closes an open list when it becomes disabled', () => {
      openByClick();
      expect(listbox()).not.toBeNull();
      update({ disabledA: true });
      expect(listbox()).toBeNull();
      expect(host.selections.length).toBe(0);

      update({ disabledA: false });
      expect(trigger().hasAttribute('aria-disabled')).toBe(false);
      openByClick();
      expect(listbox()).not.toBeNull();
    });
  });
});

const MANY: TestModel[] = [
  { id: 1, displayName: 'Gemini Luna 2', modelId: 'gemini-luna-2', provider: 'Google', releaseDate: '2026-03-01' },
  { id: 2, displayName: 'Claude Opus 5.5', modelId: 'claude-opus-5-5', provider: 'Anthropic', releaseDate: '2026-08-01' },
  { id: 3, displayName: 'GPT-6', modelId: 'gpt-6', provider: 'OpenAI', releaseDate: '2026-01-15' },
  { id: 4, displayName: 'Gemini Flash 4', modelId: 'gemini-flash-4', provider: 'Google' },
  { id: 5, displayName: 'Claude Haiku 5', modelId: 'claude-haiku-5', provider: 'Anthropic', releaseDate: '2025-11-01' },
  { id: 6, displayName: 'Alpha Custom', modelId: 'alpha', provider: 'OpenAI' }
];

const VIEW_KEYS = ['spec.one', 'spec.two', 'spec.never'];

@Component({
  standalone: true,
  imports: [ModelPickerComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <dialog open class="dlg" (keydown)="dialogKeys.push($any($event).key)">
      <app-model-picker class="pk-one" label="One" viewStateKey="spec.one" noneLabel="None"
                        [options]="many" [selectedKey]="selected"
                        (selectionChange)="onSelect($event)"></app-model-picker>
    </dialog>
    <app-model-picker class="pk-two" label="Two" viewStateKey="spec.two" [options]="grouped"></app-model-picker>
    <app-model-picker class="pk-single" label="Single" [options]="single"></app-model-picker>
    <app-model-picker class="pk-pair" label="Pair" [options]="pair"></app-model-picker>
    <app-model-picker class="pk-never" label="Never" viewTools="never" viewStateKey="spec.never"
                      [options]="many"></app-model-picker>
    <app-model-picker class="pk-up" label="Up" variant="compact" [dropsUp]="true" [options]="many"></app-model-picker>
    <div class="scroller" style="block-size: 150px; overflow-y: auto;">
      <div style="block-size: 600px;">
        <app-model-picker class="pk-long" label="Long" [options]="long"></app-model-picker>
      </div>
    </div>
    <div class="edge-scroller" style="block-size: 200px; overflow-y: auto;">
      <div style="block-size: 220px;"></div>
      <app-model-picker class="pk-edge" label="Edge" [options]="long"></app-model-picker>
    </div>
  `
})
class ToolsHostComponent {
  private cdr = inject(ChangeDetectorRef);
  many = toModelPickerOptions(MANY);
  single = toModelPickerOptions(MANY.slice(0, 1));
  pair = toModelPickerOptions(MANY.slice(0, 2));
  long = toModelPickerOptions(Array.from({ length: 24 }, (_, i) => ({ id: 100 + i, displayName: `Model ${i + 1}`, provider: 'OpenAI' })));
  grouped = [
    ...toModelPickerOptions(MANY.slice(0, 3), 'Your Models', 'u_'),
    ...toModelPickerOptions(MANY.slice(3), 'System Models', 's_')
  ];
  selected: ModelPickerKey | null = null;
  selections: ModelPickerSelection<TestModel>[] = [];
  dialogKeys: string[] = [];

  onSelect(selection: ModelPickerSelection<TestModel>): void {
    this.selections.push(selection);
    this.selected = selection.key;
    this.cdr.markForCheck();
  }

  select(key: ModelPickerKey | null): void {
    this.selected = key;
    this.cdr.markForCheck();
  }
}

describe('ModelPickerComponent view toolbar', () => {
  let fixture: ComponentFixture<ToolsHostComponent>;
  let host: ToolsHostComponent;
  let el: HTMLElement;
  let finePointer = true;

  function clearViews(): void {
    for (const key of VIEW_KEYS) localStorage.removeItem(`overseer.modelPicker.view.${key}`);
  }

  function stored(key: string): Record<string, unknown> | null {
    const raw = localStorage.getItem(`overseer.modelPicker.view.${key}`);
    return raw ? JSON.parse(raw) : null;
  }

  function store(key: string, state: Record<string, unknown>): void {
    localStorage.setItem(`overseer.modelPicker.view.${key}`, JSON.stringify({ version: 1, sort: 'default', providers: [], search: '', ...state }));
  }

  async function create(): Promise<void> {
    await TestBed.configureTestingModule({ imports: [ToolsHostComponent] }).compileComponents();
    fixture = TestBed.createComponent(ToolsHostComponent);
    host = fixture.componentInstance;
    fixture.detectChanges();
    el = fixture.nativeElement as HTMLElement;
  }

  beforeEach(() => {
    clearViews();
    finePointer = true;
    const real = window.matchMedia.bind(window);
    vi.spyOn(window, 'matchMedia').mockImplementation((query: string) =>
      query === '(pointer: fine)' ? ({ matches: finePointer, media: query } as MediaQueryList) : real(query));
  });

  afterEach(() => {
    clearViews();
    vi.restoreAllMocks();
  });

  const picker = (name: string) => el.querySelector<HTMLElement>(`.pk-${name}`)!;
  const trigger = (p: HTMLElement) => p.querySelector<HTMLButtonElement>('.selector-trigger')!;
  const listbox = (p: HTMLElement) => p.querySelector<HTMLElement>('[role="listbox"]');
  const tools = (p: HTMLElement) => p.querySelector<HTMLElement>('.model-picker-tools');
  const search = (p: HTMLElement) => p.querySelector<HTMLInputElement>('.model-picker-search')!;
  const searchToggle = (p: HTMLElement) => p.querySelector<HTMLButtonElement>('.model-picker-search-toggle')!;
  const status = (p: HTMLElement) => p.querySelector<HTMLElement>('.model-picker-status');
  const optionNames = (p: HTMLElement) =>
    Array.from(p.querySelectorAll<HTMLElement>('[role="option"] .model-name')).map(n => n.textContent!.trim());
  const headings = (p: HTMLElement) =>
    Array.from(p.querySelectorAll<HTMLElement>('.model-group-title')).map(h => h.textContent!.trim());

  function open(p: HTMLElement): void {
    trigger(p).click();
    fixture.detectChanges();
  }

  function type(p: HTMLElement, text: string): void {
    if (!search(p)) {
      searchToggle(p).click();
      fixture.detectChanges();
    }
    const input = search(p);
    input.value = text;
    input.dispatchEvent(new Event('input', { bubbles: true }));
    fixture.detectChanges();
  }

  function key(target: HTMLElement, keyName: string, init: KeyboardEventInit = {}): KeyboardEvent {
    const event = new KeyboardEvent('keydown', { key: keyName, bubbles: true, cancelable: true, ...init });
    target.dispatchEvent(event);
    fixture.detectChanges();
    return event;
  }

  it('has no toolbar at one option or with viewTools="never", and one at two', async () => {
    await create();
    open(picker('single'));
    expect(tools(picker('single'))).toBeNull();
    expect(document.activeElement).toBe(listbox(picker('single')));

    open(picker('never'));
    expect(tools(picker('never'))).toBeNull();

    open(picker('pair'));
    expect(tools(picker('pair'))).not.toBeNull();
    expect(listbox(picker('pair'))!.closest('.selector-popup')).not.toBeNull();
    expect(tools(picker('pair'))!.closest('[role="listbox"]')).toBeNull();
    expect(searchToggle(picker('pair'))).not.toBeNull();
    expect(search(picker('pair'))).toBeNull();
    expect(status(picker('pair'))).toBeNull();
  });

  it('focuses the listbox on open while the search field is closed, even on a fine pointer', async () => {
    await create();
    const p = picker('one');
    open(p);
    expect(search(p)).toBeNull();
    expect(document.activeElement).toBe(listbox(p));
  });

  it('opens the field with a remembered search, focused on a fine pointer only', async () => {
    store('spec.one', { search: 'luna' });
    await create();
    const p = picker('one');
    open(p);
    expect(search(p).value).toBe('luna');
    expect(document.activeElement).toBe(search(p));
    expect(optionNames(p)).toEqual(['None', 'Gemini Luna 2']);
    key(search(p), 'Escape');

    finePointer = false;
    open(p);
    expect(search(p).value).toBe('luna');
    expect(document.activeElement).toBe(listbox(p));
  });

  it('opens the field with a printable key typed in the listbox', async () => {
    await create();
    const p = picker('one');
    open(p);
    key(listbox(p)!, 'l');
    expect(document.activeElement).toBe(search(p));
    expect(search(p).value).toBe('l');
    type(p, 'lu');
    expect(optionNames(p)).toEqual(['None', 'Gemini Luna 2']);
  });

  it('fits the list to the models a filter leaves', async () => {
    await create();
    const p = picker('one');
    open(p);
    const before = listbox(p)!.getBoundingClientRect().height;
    p.querySelector<HTMLButtonElement>('.model-picker-provider[data-provider="openai"]')!.click();
    fixture.detectChanges();
    expect(optionNames(p)).toEqual(['None', 'GPT-6', 'Alpha Custom']);
    const box = listbox(p)!;
    expect(box.getBoundingClientRect().height).toBeLessThan(before - 1);
    expect(box.scrollHeight).toBeLessThanOrEqual(box.clientHeight + 1);
  });

  it('keeps an upward list\'s controls in place while the list fits a filter', async () => {
    await create();
    const p = picker('up');
    open(p);
    const before = listbox(p)!.getBoundingClientRect().height;
    const controlsTop = p.querySelector<HTMLElement>('.model-picker-controls')!.getBoundingClientRect().top;
    type(p, 'claude');
    expect(listbox(p)!.getBoundingClientRect().height).toBeLessThan(before - 1);
    expect(Math.abs(p.querySelector<HTMLElement>('.model-picker-controls')!.getBoundingClientRect().top - controlsTop))
      .toBeLessThanOrEqual(0.5);
  });

  it('never scrolls the container back when a filter narrows a popup that reaches past it', async () => {
    await create();
    const scroller = el.querySelector<HTMLElement>('.edge-scroller')!;
    const p = picker('edge');
    open(p);
    // Opened first: focusing the field scrolls it into view.
    type(p, '');
    scroller.scrollTop = scroller.scrollHeight - scroller.clientHeight;
    const scrolled = scroller.scrollTop;
    expect(scrolled).toBeGreaterThan(0);
    const visibleEnd = () => scroller.getBoundingClientRect().top + scroller.clientTop + scroller.clientHeight;
    const popup = () => p.querySelector<HTMLElement>('.selector-popup')!;

    type(p, 'model 2');
    expect(optionNames(p).length).toBe(7);
    expect(Math.abs(scroller.scrollTop - scrolled)).toBeLessThanOrEqual(1);
    expect(popup().getBoundingClientRect().bottom).toBeGreaterThanOrEqual(visibleEnd() - 1);

    type(p, 'model 24');
    expect(optionNames(p)).toEqual(['Model 24']);
    expect(Math.abs(scroller.scrollTop - scrolled)).toBeLessThanOrEqual(1);
  });

  it('keeps the picker open when a sort option\'s text is clicked', async () => {
    await create();
    const p = picker('one');
    open(p);
    await userEvent.click(p.querySelector<HTMLButtonElement>('.model-picker-sort')!);
    fixture.detectChanges();
    const panel = p.querySelector<HTMLElement>('.model-picker-sort-panel')!;
    await vi.waitFor(() => expect(panel.matches(':popover-open')).toBe(true));
    const label = Array.from(panel.querySelectorAll<HTMLElement>('.gh-facet-option-label'))
      .find(l => l.textContent!.trim() === 'Name (A–Z)')!;

    await userEvent.click(label);
    fixture.detectChanges();
    expect(listbox(p)).not.toBeNull();
    expect(trigger(p).getAttribute('aria-expanded')).toBe('true');
    expect(stored('spec.one')!['sort']).toBe('name-asc');
    expect(panel.matches(':popover-open')).toBe(true);
    expect(document.activeElement).toBe(label.closest('label')!.querySelector('input'));

    await userEvent.click(panel.querySelector<HTMLElement>('.gh-facet-legend')!);
    fixture.detectChanges();
    expect(listbox(p)).not.toBeNull();
    expect(trigger(p).getAttribute('aria-expanded')).toBe('true');
    expect(stored('spec.one')!['sort']).toBe('name-asc');
  });

  it('stays open through a press that leaves focus on the body, and gives focus back to the listbox', async () => {
    await create();
    const p = picker('one');
    open(p);
    const toggle = p.querySelector<HTMLButtonElement>('.model-picker-provider[data-provider="openai"]')!;
    toggle.dispatchEvent(new PointerEvent('pointerdown', { bubbles: true }));
    (document.activeElement as HTMLElement | null)?.blur();
    p.querySelector<HTMLElement>('.selector-popup')!
      .dispatchEvent(new FocusEvent('focusout', { bubbles: true, relatedTarget: null }));
    fixture.detectChanges();
    expect(listbox(p)).not.toBeNull();

    document.dispatchEvent(new PointerEvent('pointerup', { bubbles: true }));
    await new Promise(r => setTimeout(r));
    fixture.detectChanges();
    expect(listbox(p)).not.toBeNull();
    expect(document.activeElement).toBe(listbox(p));
  });

  it('still closes on a press outside', async () => {
    await create();
    const p = picker('one');
    open(p);
    picker('two').dispatchEvent(new PointerEvent('pointerdown', { bubbles: true }));
    fixture.detectChanges();
    expect(listbox(p)).toBeNull();
  });

  it('puts the toolbar after the listbox in an upward picker, the controls row last', async () => {
    await create();
    const p = picker('up');
    open(p);
    const popup = p.querySelector<HTMLElement>('.selector-popup')!;
    expect(Array.from(popup.children).map(c => c.getAttribute('role') ?? c.tagName.toLowerCase()))
      .toEqual(['listbox', 'app-model-picker-tools']);
    type(p, 'g');
    expect(Array.from(tools(p)!.children).map(c => c.classList[0]))
      .toEqual(['model-picker-status', 'model-picker-search-row', 'model-picker-controls', 'visually-hidden']);
    expect(listbox(p)!.getBoundingClientRect().bottom).toBeLessThanOrEqual(tools(p)!.getBoundingClientRect().top + 1);
  });

  it('scrolls only the listbox on arrow keys, never the container around the picker', async () => {
    await create();
    const scroller = el.querySelector<HTMLElement>('.scroller')!;
    const p = picker('long');
    open(p);
    scroller.scrollTop = 0;
    const box = listbox(p)!;
    key(box, 'End');
    expect(box.scrollTop).toBeGreaterThan(0);
    expect(scroller.scrollTop).toBe(0);
    for (let i = 0; i < 5; i++) key(box, 'ArrowUp');
    key(box, 'Home');
    const first = box.querySelector<HTMLElement>('[role="option"]')!;
    expect(first.getBoundingClientRect().top).toBeGreaterThanOrEqual(box.getBoundingClientRect().top + box.clientTop - 0.5);
    expect(box.scrollTop).toBeLessThan(first.getBoundingClientRect().height);
    expect(scroller.scrollTop).toBe(0);
  });

  it('says in the listbox that no model matches, and describes the listbox with it', async () => {
    await create();
    const p = picker('two');
    open(p);
    type(p, 'zzz');
    const box = listbox(p)!;
    const hint = p.querySelector<HTMLElement>(`#${box.getAttribute('aria-describedby')}`)!;
    expect(box.contains(hint)).toBe(true);
    expect(hint.textContent!.trim()).toBe('No models match "zzz".');
    type(p, '');
    expect(box.hasAttribute('aria-describedby')).toBe(false);
  });

  it('does not apply a stored view where the toolbar is not shown', async () => {
    store('spec.never', { search: 'luna', sort: 'name-asc' });
    await create();
    open(picker('never'));
    expect(optionNames(picker('never'))).toEqual(MANY.map(m => m.displayName));
  });

  it('filters as the user types, and Enter commits the first match', async () => {
    await create();
    const p = picker('one');
    open(p);
    type(p, 'luna');
    expect(optionNames(p)).toEqual(['None', 'Gemini Luna 2']);
    expect(p.querySelector('mark.model-option-match')!.textContent).toBe('Luna');
    expect(p.querySelector('.model-picker-status')!.textContent).toContain('1 of 6 models');
    key(search(p), 'Enter');
    expect(host.selections.at(-1)!.key).toBe(1);
    expect(listbox(p)).toBeNull();
  });

  it('keeps the none option first, however the view sorts', async () => {
    store('spec.one', { sort: 'name-desc' });
    await create();
    open(picker('one'));
    expect(optionNames(picker('one'))[0]).toBe('None');
    expect(optionNames(picker('one'))[1]).toBe('GPT-6');
  });

  it('sorts Your Models and System Models separately and keeps their headings', async () => {
    store('spec.two', { sort: 'name-asc' });
    await create();
    const p = picker('two');
    open(p);
    expect(headings(p)).toEqual(['Your Models', 'System Models']);
    expect(optionNames(p)).toEqual(['Claude Opus 5.5', 'Gemini Luna 2', 'GPT-6', 'Alpha Custom', 'Claude Haiku 5', 'Gemini Flash 4']);
  });

  it('shows the release month while a release-date sort applies', async () => {
    store('spec.two', { sort: 'release-desc' });
    await create();
    const p = picker('two');
    open(p);
    expect(optionNames(p)).toEqual(['Claude Opus 5.5', 'Gemini Luna 2', 'GPT-6', 'Claude Haiku 5', 'Alpha Custom', 'Gemini Flash 4']);
    const notes = Array.from(p.querySelectorAll<HTMLElement>('.model-option-release')).map(n => n.textContent!.trim());
    expect(notes).toEqual(['released Aug 2026', 'released Mar 2026', 'released Jan 2026', 'released Nov 2025', 'released date unknown', 'released date unknown']);
    expect(trigger(p).querySelector('.model-option-release')).toBeNull();
  });

  it('renders no heading for a group the view leaves empty', async () => {
    await create();
    const p = picker('two');
    open(p);
    type(p, 'haiku');
    expect(headings(p)).toEqual(['System Models']);
    expect(optionNames(p)).toEqual(['Claude Haiku 5']);
  });

  it('remembers each picker on its own key', async () => {
    await create();
    open(picker('two'));
    const toggle = picker('two').querySelector<HTMLButtonElement>('.model-picker-provider[data-provider="openai"]')!;
    toggle.click();
    fixture.detectChanges();
    expect(stored('spec.two')).toEqual({ version: 1, sort: 'default', providers: ['openai'], search: '' });
    expect(stored('spec.one')).toBeNull();
    expect(optionNames(picker('two'))).toEqual(['GPT-6', 'Alpha Custom']);
  });

  it('writes the search after 300 ms of quiet, and at once on close', async () => {
    await create();
    vi.useFakeTimers();
    try {
      const p = picker('one');
      open(p);
      type(p, 'lu');
      expect(stored('spec.one')).toBeNull();
      vi.advanceTimersByTime(300);
      expect(stored('spec.one')!['search']).toBe('lu');

      type(p, 'lun');
      key(search(p), 'Escape');
      expect(stored('spec.one')!['search']).toBe('lun');
    } finally {
      vi.useRealTimers();
    }
  });

  it('focuses the listbox instead of the search field on a coarse pointer, and typing moves to the field', async () => {
    finePointer = false;
    await create();
    const p = picker('one');
    open(p);
    expect(document.activeElement).toBe(listbox(p));
    key(listbox(p)!, 'g');
    expect(document.activeElement).toBe(search(p));
    expect(search(p).value).toBe('g');
    expect(optionNames(p)).toContain('GPT-6');
  });

  it('keeps focus in the search field when an option is pressed', async () => {
    await create();
    const p = picker('one');
    open(p);
    type(p, 'a');
    const option = p.querySelectorAll<HTMLElement>('[role="option"]')[2];
    const down = new MouseEvent('mousedown', { bubbles: true, cancelable: true });
    option.dispatchEvent(down);
    expect(down.defaultPrevented).toBe(true);
    const inField = new MouseEvent('mousedown', { bubbles: true, cancelable: true });
    search(p).dispatchEvent(inField);
    expect(inField.defaultPrevented).toBe(false);
    option.click();
    fixture.detectChanges();
    expect(host.selections.at(-1)!.key).toBe(2);
  });

  it('closes on Escape in the search field without closing the enclosing dialog', async () => {
    await create();
    const p = picker('one');
    open(p);
    type(p, 'g');
    const event = key(search(p), 'Escape');
    expect(event.defaultPrevented).toBe(true);
    expect(host.dialogKeys).toEqual([]);
    expect(listbox(p)).toBeNull();
    expect(el.querySelector<HTMLDialogElement>('.dlg')!.open).toBe(true);
    expect(document.activeElement).toBe(trigger(p));
  });

  it('says so when the view hides the selected model', async () => {
    await create();
    host.select(2);
    fixture.detectChanges();
    const p = picker('one');
    open(p);
    type(p, 'luna');
    expect(status(p)!.textContent).toContain('selected model hidden');
    type(p, '');
    expect(status(p)).toBeNull();
  });
});
