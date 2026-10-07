import { ChangeDetectionStrategy, ChangeDetectorRef, Component, inject } from '@angular/core';
import { ComponentFixture, TestBed, fakeAsync, tick } from '@angular/core/testing';

import {
  ModelPickerComponent,
  ModelPickerKey,
  ModelPickerModel,
  ModelPickerOption,
  ModelPickerSelection,
  toModelPickerOptions
} from './model-picker.component';

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

/** Feeds every committed choice back into `selectedKey`, the way a real host does. */
@Component({
  standalone: true,
  imports: [ModelPickerComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="outer" (keydown)="outerKeys.push($any($event).key)">
      <label id="pickerALabel">Model A</label>
      <app-model-picker class="picker-a marker" labelledBy="pickerALabel" describedBy="pickerAHint"
                        [noneLabel]="noneLabel" [options]="optionsA" [selectedKey]="selectedA"
                        [showPrice]="true" [showParallel]="true" [disabled]="disabledA"
                        (selectionChange)="onSelect($event)"></app-model-picker>
      <span id="pickerAHint">Hint A</span>
      <app-model-picker class="picker-b" label="Model B" emptyHint="Nothing to choose from."
                        [options]="optionsB" [selectedKey]="null"></app-model-picker>
      <app-model-picker class="picker-c" variant="compact" label="Model C" [options]="optionsA"
                        [selectedKey]="1"></app-model-picker>
      <button type="button" class="outside">Outside</button>
    </div>
  `
})
class HostComponent {
  private cdr = inject(ChangeDetectorRef);
  noneLabel: string | null = null;
  optionsA: ModelPickerOption<TestModel>[] = toModelPickerOptions(MODELS);
  optionsB: ModelPickerOption<TestModel>[] = [];
  selectedA: ModelPickerKey | null = null;
  disabledA = false;
  selections: ModelPickerSelection<TestModel>[] = [];
  outerKeys: string[] = [];

  onSelect(selection: ModelPickerSelection<TestModel>): void {
    this.selections.push(selection);
    this.selectedA = selection.key;
    this.cdr.markForCheck();
  }

  set(changes: Partial<Pick<HostComponent, 'noneLabel' | 'optionsA' | 'selectedA' | 'disabledA'>>): void {
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

  function update(changes: Partial<Pick<HostComponent, 'noneLabel' | 'optionsA' | 'selectedA' | 'disabledA'>>): void {
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
      const groups = Array.from(pickerA().querySelectorAll<HTMLElement>('[role="group"]'));
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
      update({ optionsA: TAGGED });
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
