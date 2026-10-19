import { ChangeDetectionStrategy, ChangeDetectorRef, Component, inject } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';

import { ModelMultiPickerComponent, ModelMultiPickerSelection } from './model-multi-picker.component';
import { ModelPickerComponent, ModelPickerKey, ModelPickerModel, ModelPickerOption } from './model-picker.component';
import { MultiPickerStatus } from '../multi-picker/multi-picker.models';

interface TestModel extends ModelPickerModel {
  id: number;
}

const ALPHA: TestModel = {
  id: 1, displayName: 'Alpha', provider: 'OpenAI', thinkingLevel: 'high', reasoningMode: 'pro', parallelExecutionMode: 0,
  effectiveInputPricePerMillion: 5, effectiveOutputPricePerMillion: 25
};
const BETA: TestModel = { id: 2, displayName: 'Beta', provider: 'Anthropic', parallelExecutionMode: 1 };

const OPTIONS: ModelPickerOption<TestModel>[] = [
  { key: 'run:1', model: ALPHA, detail: 'Run #1' },
  { key: 'battery:10', model: ALPHA, detail: 'Battery run #10' },
  { key: 'run:2', model: BETA }
];

const CARD_OPTIONS: ModelPickerOption<TestModel>[] = [
  { key: 'card:short', model: ALPHA, detail: 'Run #1' },
  {
    key: 'card:long', model: ALPHA,
    detail: 'Same provider as the report writer, and its settings differ from the others in parallel mode and service tier'
  },
  { key: 'card:beta', model: BETA },
  {
    key: 'card:noted', model: ALPHA, notes: [
      { text: 'Is the report writer: cannot report on itself', tone: 'error' },
      { text: 'Max output tokens 8192; the others use default', tone: 'warning' }
    ]
  }
];

const NOTED_OPTIONS: ModelPickerOption<TestModel>[] = [
  {
    key: 'noted:1', model: ALPHA, detail: 'Run #1', notes: [
      { text: 'Is the assessor: cannot grade itself', tone: 'error' },
      { text: 'Same provider as the report writer', tone: 'warning' }
    ]
  },
  { key: 'noted:2', model: BETA }
];

/** Both pickers over the same models, each feeding its selection back. */
@Component({
  standalone: true,
  imports: [ModelPickerComponent, ModelMultiPickerComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="outer" (keydown)="outerKeys.push($any($event).key)">
      <app-model-picker class="single" label="Single" [options]="singleOptions" [selectedKey]="null"></app-model-picker>
      <app-model-multi-picker class="multi" label="Models" [options]="options" [selectedKeys]="selected"
                              (selectionChange)="onChange($event)"></app-model-multi-picker>
      <app-model-multi-picker class="multi-priced" label="Priced models" [options]="options" [selectedKeys]="pricedSelected"
                              [showPrice]="true" [showParallel]="true"></app-model-multi-picker>
      <app-model-multi-picker class="multi-noted" label="Noted models" [options]="notedOptions"
                              [selectedKeys]="notedSelected"></app-model-multi-picker>
      <app-model-multi-picker class="multi-cards" label="Card models" [options]="cardOptions" [selectedKeys]="cardSelected"
                              chipStyle="card" [showPrice]="true" [showParallel]="true"
                              [status]="status"></app-model-multi-picker>
    </div>
  `
})
class HostComponent {
  private cdr = inject(ChangeDetectorRef);
  singleOptions: ModelPickerOption<TestModel>[] = [{ key: 1, model: ALPHA }, { key: 2, model: BETA }];
  options: ModelPickerOption<TestModel>[] = OPTIONS;
  selected: ModelPickerKey[] = [];
  pricedSelected: ModelPickerKey[] = [];
  cardOptions: ModelPickerOption<TestModel>[] = CARD_OPTIONS;
  cardSelected: ModelPickerKey[] = [];
  notedOptions: ModelPickerOption<TestModel>[] = NOTED_OPTIONS;
  notedSelected: ModelPickerKey[] = ['noted:1', 'noted:2'];
  status: MultiPickerStatus | null = null;
  changes: ModelMultiPickerSelection<TestModel>[] = [];
  outerKeys: string[] = [];

  onChange(selection: ModelMultiPickerSelection<TestModel>): void {
    this.changes.push(selection);
    this.selected = selection.keys;
    this.cdr.markForCheck();
  }

  select(keys: ModelPickerKey[]): void {
    this.selected = keys;
    this.cdr.markForCheck();
  }

  selectPriced(keys: ModelPickerKey[]): void {
    this.pricedSelected = keys;
    this.cdr.markForCheck();
  }

  selectCards(keys: ModelPickerKey[]): void {
    this.cardSelected = keys;
    this.cdr.markForCheck();
  }

  setStatus(status: MultiPickerStatus | null): void {
    this.status = status;
    this.cdr.markForCheck();
  }
}

describe('ModelMultiPickerComponent', () => {
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

  const part = (selector: string) => el.querySelector<HTMLElement>(selector)!;
  const trigger = (p: HTMLElement) => p.querySelector<HTMLButtonElement>('.selector-trigger')!;
  const listbox = (p: HTMLElement) => p.querySelector<HTMLElement>('[role="listbox"]');
  const options = (p: HTMLElement) => Array.from(p.querySelectorAll<HTMLElement>('[role="option"]'));

  function open(p: HTMLElement): void {
    trigger(p).click();
    fixture.detectChanges();
  }

  function key(target: HTMLElement, keyName: string): KeyboardEvent {
    const event = new KeyboardEvent('keydown', { key: keyName, bubbles: true, cancelable: true });
    target.dispatchEvent(event);
    fixture.detectChanges();
    return event;
  }

  it('adds the filter button and the view toolbar behind it from two options, in both pickers', () => {
    for (const name of ['.single', '.multi-noted']) {
      const p = part(name);
      const button = p.querySelector<HTMLButtonElement>('.picker-tools-toggle-btn')!;
      expect(button.getAttribute('aria-label')).toBe('Filter and sort models');
      open(p);
      expect(p.querySelector('.model-picker-tools')).toBeNull();
      button.click();
      fixture.detectChanges();
      const tools = p.querySelector<HTMLElement>('.model-picker-tools')!;
      expect(tools).not.toBeNull();
      expect(button.getAttribute('aria-controls')).toBe(tools.id);
      expect(p.querySelector('.model-picker-search-toggle')).not.toBeNull();
      key(listbox(p)!, 'Escape');
    }
  });

  it('draws the same badges as the single picker for the same model', () => {
    const single = part('.single');
    const multi = part('.multi');
    open(single);
    const singleBadges = options(single)[0].querySelector('app-model-option-badges')!.innerHTML;
    key(listbox(single)!, 'Escape');
    open(multi);
    const multiBadges = options(multi)[0].querySelector('app-model-option-badges')!;
    expect(singleBadges).toContain('thinking-badge');
    expect(multiBadges.innerHTML).toBe(singleBadges);
    expect(multiBadges.textContent!.replace(/\s+/g, ' ')).toContain('thinking level High');
    expect(multiBadges.querySelector('.provider-badge')!.textContent!.trim()).toBe('OpenAI');
  });

  it('shows no price or parallel badge by default, and both when enabled', () => {
    const multi = part('.multi');
    open(multi);
    expect(multi.querySelector('.price-badge')).toBeNull();
    expect(multi.querySelector('.parallel-badge')).toBeNull();

    const priced = part('.multi-priced');
    open(priced);
    expect(options(priced)[0].querySelector('.price-badge')).not.toBeNull();
    expect(options(priced)[0].querySelector('.parallel-badge.badge-sequential')).not.toBeNull();
    expect(options(priced)[2].querySelector('.parallel-badge.badge-on-request')).not.toBeNull();
  });

  it('draws the same badges on a chip as on its option', () => {
    const multi = part('.multi');
    host.select(['run:1']);
    fixture.detectChanges();
    open(multi);
    const optionBadges = options(multi)[0].querySelector('app-model-option-badges')!.innerHTML;
    key(listbox(multi)!, 'Escape');
    const chip = multi.querySelector<HTMLElement>('.gh-multi-picker-chip')!;
    const chipBadges = chip.querySelector('.gh-multi-picker-chip-badges app-model-option-badges')!;
    expect(chipBadges.innerHTML).toBe(optionBadges);
    expect(chipBadges.querySelector('.thinking-badge')).not.toBeNull();
    expect(chipBadges.querySelector('.provider-badge')).not.toBeNull();
    expect(chipBadges.querySelector('.price-badge')).toBeNull();
    expect(chip.querySelector('.gh-multi-picker-chip-label')!.textContent!.trim()).toBe('Alpha');
  });

  it('honors showPrice and showParallel on the chips', () => {
    host.selectPriced(['run:1']);
    fixture.detectChanges();
    const chip = part('.multi-priced').querySelector<HTMLElement>('.gh-multi-picker-chip')!;
    expect(chip.querySelector('.price-badge')).not.toBeNull();
    expect(chip.querySelector('.parallel-badge.badge-sequential')).not.toBeNull();
  });

  it('draws a chip detail as a muted note the remove button is described by', () => {
    host.select(['run:1']);
    fixture.detectChanges();
    const chip = part('.multi').querySelector<HTMLElement>('.gh-multi-picker-chip')!;
    const muted = chip.querySelector<HTMLElement>('.gh-multi-picker-chip-note')!;
    expect(muted.tagName).toBe('SPAN');
    expect(muted.classList).not.toContain('is-warning');
    expect(muted.textContent!.replace(/\s+/g, ' ').trim()).toBe('Run #1');
    const remove = chip.querySelector<HTMLElement>('.action-btn')!;
    expect(remove.getAttribute('aria-label')).toBe('Remove Alpha');
    expect(remove.getAttribute('aria-describedby')).toBe(muted.id);
  });

  it('draws a chip\'s notes as a list, one line each, with the tone\'s glyph and hidden prefix', () => {
    const [noted, plain] = Array.from(part('.multi-noted').querySelectorAll<HTMLElement>('.gh-multi-picker-chip'));
    const list = noted.querySelector<HTMLElement>('ul.gh-multi-picker-chip-notes')!;
    const items = Array.from(list.querySelectorAll<HTMLElement>(':scope > li.gh-multi-picker-chip-note'));
    expect(items.length).toBe(2);
    expect(items[0].classList).toContain('is-error');
    expect(items[0].querySelector('svg circle')).not.toBeNull();
    expect(items[1].classList).toContain('is-warning');
    expect(items[1].querySelector('svg path')).not.toBeNull();
    for (const item of items) {
      expect(item.querySelector('svg')!.getAttribute('aria-hidden')).toBe('true');
    }
    expect(items.map(i => i.textContent!.replace(/\s+/g, ' ').trim())).toEqual([
      'Blocking: Is the assessor: cannot grade itself',
      'Warning: Same provider as the report writer'
    ]);
    // The notes replace the detail on the chip.
    expect(noted.textContent).not.toContain('Run #1');

    const remove = noted.querySelector<HTMLElement>('.action-btn')!;
    expect(remove.getAttribute('aria-label')).toBe('Remove Alpha');
    expect(remove.getAttribute('aria-describedby')).toBe(list.id);
    expect(plain.querySelector('.gh-multi-picker-chip-note, .gh-multi-picker-chip-notes')).toBeNull();
    expect(plain.querySelector('.action-btn')!.hasAttribute('aria-describedby')).toBe(false);
  });

  it('shows a chosen model\'s notes, joined, as the muted detail of its option', () => {
    const noted = part('.multi-noted');
    open(noted);
    expect(options(noted)[0].querySelector('.gh-multi-picker-detail')!.textContent!.trim())
      .toBe('Is the assessor: cannot grade itself · Same provider as the report writer');
  });

  it('offers two options of the same model as two options, each with its detail', () => {
    const multi = part('.multi');
    open(multi);
    const all = options(multi);
    expect(all.length).toBe(3);
    expect(all.map(o => o.querySelector('.model-name')!.textContent!.trim())).toEqual(['Alpha', 'Alpha', 'Beta']);
    expect(all.map(o => o.querySelector('.gh-multi-picker-detail')?.textContent!.trim() ?? null))
      .toEqual(['Run #1', 'Battery run #10', null]);
  });

  it('summarizes in models, with the model name when one is selected', () => {
    const multi = part('.multi');
    const summary = () => trigger(multi).querySelector('.gh-multi-picker-summary')!.textContent!.trim();
    expect(summary()).toBe('Select models');
    host.select(['run:2']);
    fixture.detectChanges();
    expect(summary()).toBe('Beta');
    host.select(['run:1', 'run:2']);
    fixture.detectChanges();
    expect(summary()).toBe('2 of 3 models');
  });

  it('a keyboard toggle emits the keys and their models', () => {
    const multi = part('.multi');
    open(multi);
    key(listbox(multi)!, 'ArrowDown');
    key(listbox(multi)!, ' ');
    expect(host.changes.length).toBe(1);
    expect(host.changes[0].keys).toEqual(['battery:10']);
    expect(host.changes[0].models).toEqual([ALPHA]);
    expect(options(multi)[1].getAttribute('aria-selected')).toBe('true');
  });

  it('leads the badges of a model that needs attention with its chip, part of the option text', () => {
    const flagged: TestModel = {
      id: 3, displayName: 'Old Flash', provider: 'Google',
      modelAvailability: { status: 'retired', needsAttention: true, retiredOn: '2026-09-30' }
    };
    host.options = [...OPTIONS, { key: 'run:3', model: flagged }];
    host.select([]);
    fixture.detectChanges();
    const multi = part('.multi');
    open(multi);

    const option = options(multi)[3];
    const chip = option.querySelector<HTMLElement>('.model-option-notice')!;
    expect(chip.getAttribute('data-tone')).toBe('warning');
    expect(chip.textContent!.replace(/\s+/g, ' ').trim()).toBe('status: Removed');
    expect(chip.compareDocumentPosition(option.querySelector('.provider-badge')!) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    expect(option.hasAttribute('aria-label')).toBe(false);
    const text = option.textContent!.replace(/\s+/g, ' ');
    expect(text.indexOf('status: Removed')).toBeGreaterThan(text.indexOf('Old Flash'));
    expect(options(multi)[0].querySelector('.model-option-notice')).toBeNull();
  });

  it('Escape closes the list and keeps the key from the parent', () => {
    const multi = part('.multi');
    open(multi);
    const event = key(listbox(multi)!, 'Escape');
    expect(event.defaultPrevented).toBe(true);
    expect(listbox(multi)).toBeNull();
    expect(host.outerKeys).not.toContain('Escape');
    expect(document.activeElement).toBe(trigger(multi));
  });

  it('passes its status through to the picker', () => {
    host.selectCards(['card:short', 'card:beta']);
    host.setStatus({ tone: 'warning', text: 'Warnings on 1 model' });
    fixture.detectChanges();
    const status = part('.multi-cards').querySelector<HTMLElement>('.gh-multi-picker-status')!;
    expect(status.textContent!.trim()).toBe('Warnings on 1 model');
    expect(status.getAttribute('data-tone')).toBe('warning');
  });

  describe('card mode layout', () => {
    const REM = 16;
    let picker: HTMLElement;

    beforeEach(() => {
      host.selectCards(['card:short', 'card:long', 'card:beta', 'card:noted']);
      fixture.detectChanges();
      picker = part('.multi-cards');
    });

    const cards = () => Array.from(picker.querySelectorAll<HTMLElement>('.gh-multi-picker-chip'));
    const card = (index: number) => cards()[index];
    const box = (e: Element) => e.getBoundingClientRect();
    const sized = (rem: number) => { picker.style.inlineSize = `${rem}rem`; };

    it('keeps each card at its width when the picker widens', () => {
      sized(40);
      const narrow = cards().map(c => box(c).width);
      sized(46);
      const wide = cards().map(c => box(c).width);
      expect(narrow.length).toBe(4);
      wide.forEach((width, i) => expect(Math.abs(width - narrow[i])).toBeLessThanOrEqual(0.5));
    });

    it('starts the badges right under the name', () => {
      sized(46);
      for (const c of cards()) {
        const name = box(c.querySelector('.gh-multi-picker-chip-main')!);
        const badges = box(c.querySelector('.gh-multi-picker-chip-badges')!);
        expect(badges.top - name.bottom).toBeLessThanOrEqual(0.375 * REM + 1);
      }
    });

    it('keeps the badges on one line when there is room', () => {
      sized(60);
      for (const c of cards()) {
        const middles = Array.from(c.querySelectorAll('.thinking-badge, .provider-badge, .price-badge, .parallel-badge'))
          .map(b => (box(b).top + box(b).bottom) / 2);
        expect(middles.length).toBeGreaterThan(1);
        middles.forEach(middle => expect(Math.abs(middle - middles[0])).toBeLessThanOrEqual(1));
      }
    });

    it('puts the remove button beside the badges, in the card\'s top end corner', () => {
      sized(46);
      for (const c of cards()) {
        const cardBox = box(c);
        const badges = box(c.querySelector('.gh-multi-picker-chip-badges')!);
        const remove = box(c.querySelector(':scope > .action-btn')!);
        const paddingTop = parseFloat(getComputedStyle(c).paddingBlockStart) + parseFloat(getComputedStyle(c).borderBlockStartWidth);
        expect(remove.left).toBeGreaterThanOrEqual(badges.right);
        expect(remove.right).toBeLessThanOrEqual(cardBox.right);
        expect(remove.top - cardBox.top).toBeLessThanOrEqual(paddingTop + 0.5);
      }
    });

    it('wraps a long note without widening its card', () => {
      sized(60);
      const short = card(0);
      const long = card(1);
      expect(Math.abs(box(long).width - box(short).width)).toBeLessThanOrEqual(1);
      const shortNote = box(short.querySelector('.gh-multi-picker-chip-note')!);
      const longNote = box(long.querySelector('.gh-multi-picker-chip-note')!);
      expect(longNote.height).toBeGreaterThan(shortNote.height * 1.5);
    });

    it('puts a note list in the note row without widening its card', () => {
      sized(60);
      const short = card(0);
      const noted = card(3);
      expect(Math.abs(box(noted).width - box(short).width)).toBeLessThanOrEqual(1);
      const list = box(noted.querySelector('.gh-multi-picker-chip-notes')!);
      expect(list.top).toBeGreaterThanOrEqual(box(noted.querySelector('.gh-multi-picker-chip-badges')!).bottom);
      expect(list.right).toBeLessThanOrEqual(box(noted).right);
    });

    it('fits a card into a narrow picker and wraps its badges', () => {
      sized(14);
      const hostRight = box(picker).right;
      for (const c of cards()) {
        expect(box(c).right).toBeLessThanOrEqual(hostRight + 0.5);
        expect(c.scrollWidth).toBeLessThanOrEqual(c.clientWidth + 1);
      }
      const badges = card(0).querySelector('.gh-multi-picker-chip-badges')!;
      expect(box(badges).height).toBeGreaterThan(box(badges.querySelector('.provider-badge')!).height * 1.5);
    });
  });
});

const SIX: ModelPickerOption<TestModel>[] = [
  { id: 11, displayName: 'Claude Opus 5.5', provider: 'Anthropic', releaseDate: '2026-08-01' },
  { id: 12, displayName: 'Claude Haiku 5', provider: 'Anthropic' },
  { id: 13, displayName: 'GPT-6', provider: 'OpenAI' },
  { id: 14, displayName: 'GPT-6 Mini', provider: 'OpenAI' },
  { id: 15, displayName: 'Gemini Luna 2', provider: 'Google' },
  { id: 16, displayName: 'Gemini Flash 4', provider: 'Google' }
].map(model => ({ key: model.id, model, ...(model.id === 12 ? { disabledReason: 'Grades this batch' } : {}) }));

@Component({
  standalone: true,
  imports: [ModelMultiPickerComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <app-model-multi-picker class="six-picker" label="Six" viewStateKey="spec.multi"
                            [options]="options" [selectedKeys]="selected" [max]="max"
                            (selectionChange)="onChange($event)"></app-model-multi-picker>
    <app-model-multi-picker class="six-up" label="Six upward" [dropsUp]="true"
                            [options]="options" [selectedKeys]="[]"></app-model-multi-picker>
  `
})
class SixHostComponent {
  private cdr = inject(ChangeDetectorRef);
  options = SIX;
  selected: ModelPickerKey[] = [];
  max: number | null = null;
  changes: ModelMultiPickerSelection<TestModel>[] = [];

  onChange(selection: ModelMultiPickerSelection<TestModel>): void {
    this.changes.push(selection);
    this.selected = selection.keys;
    this.cdr.markForCheck();
  }

  set(changes: Partial<Pick<SixHostComponent, 'selected' | 'max'>>): void {
    Object.assign(this, changes);
    this.cdr.markForCheck();
  }
}

describe('ModelMultiPickerComponent view toolbar', () => {
  let fixture: ComponentFixture<SixHostComponent>;
  let host: SixHostComponent;
  let el: HTMLElement;

  beforeEach(async () => {
    localStorage.removeItem('overseer.modelPicker.view.spec.multi');
    await TestBed.configureTestingModule({ imports: [SixHostComponent] }).compileComponents();
    fixture = TestBed.createComponent(SixHostComponent);
    host = fixture.componentInstance;
    fixture.detectChanges();
    el = fixture.nativeElement as HTMLElement;
  });

  afterEach(() => localStorage.removeItem('overseer.modelPicker.view.spec.multi'));

  const picker = () => el.querySelector<HTMLElement>('.six-picker')!;
  const labels = () => Array.from(picker().querySelectorAll<HTMLElement>('[role="option"] .model-name')).map(n => n.textContent!.trim());
  const selectShown = () => picker().querySelector<HTMLButtonElement>('.model-picker-select-shown');
  const tools = (p = picker()) => p.querySelector<HTMLElement>('.model-picker-tools');
  const filterButton = (p = picker()) => p.querySelector<HTMLButtonElement>('.picker-tools-toggle-btn')!;
  const listbox = () => picker().querySelector<HTMLElement>('[role="listbox"]');

  function open(): void {
    picker().querySelector<HTMLButtonElement>('.selector-trigger')!.click();
    fixture.detectChanges();
  }

  /** Shows the toolbar of an open picker with the filter button, unless it is already shown. */
  function expand(p = picker()): void {
    if (tools(p)) return;
    filterButton(p).click();
    fixture.detectChanges();
  }

  function pressProvider(key: string): void {
    expand();
    picker().querySelector<HTMLButtonElement>(`.model-picker-provider[data-provider="${key}"]`)!.click();
    fixture.detectChanges();
  }

  function typeSearch(text: string): void {
    expand();
    if (!picker().querySelector('.model-picker-search')) {
      picker().querySelector<HTMLButtonElement>('.model-picker-search-toggle')!.click();
      fixture.detectChanges();
    }
    const search = picker().querySelector<HTMLInputElement>('.model-picker-search')!;
    search.value = text;
    search.dispatchEvent(new Event('input', { bubbles: true }));
    fixture.detectChanges();
  }

  it('opens from the trigger with the toolbar collapsed', () => {
    open();
    expect(listbox()).not.toBeNull();
    expect(tools()).toBeNull();
    expect(filterButton().getAttribute('aria-expanded')).toBe('false');
  });

  it('clears the search and the provider filter on collapse, and keeps and stores the sort', () => {
    localStorage.setItem('overseer.modelPicker.view.spec.multi',
      JSON.stringify({ version: 1, sort: 'name-asc', providers: [], search: '' }));
    fixture.destroy();
    fixture = TestBed.createComponent(SixHostComponent);
    fixture.detectChanges();
    el = fixture.nativeElement as HTMLElement;

    open();
    expect(tools()).toBeNull();
    expect(filterButton().getAttribute('aria-label')).toBe('Filter and sort models (sorted)');
    pressProvider('google');
    typeSearch('luna');
    expect(labels()).toEqual(['Gemini Luna 2']);
    expect(filterButton().getAttribute('aria-label')).toBe('Filter and sort models (filtered and sorted)');

    filterButton().click();
    fixture.detectChanges();
    expect(tools()).toBeNull();
    expect(listbox()).not.toBeNull();
    expect(document.activeElement).toBe(filterButton());
    expect(labels()).toEqual(['Claude Haiku 5', 'Claude Opus 5.5', 'Gemini Flash 4', 'Gemini Luna 2', 'GPT-6', 'GPT-6 Mini']);
    expect(JSON.parse(localStorage.getItem('overseer.modelPicker.view.spec.multi')!))
      .toEqual({ version: 1, sort: 'name-asc', thinking: 'lowest-first', providers: [], search: '' });
    expect(filterButton().getAttribute('aria-label')).toBe('Filter and sort models (sorted)');
    expect(picker().querySelector('.picker-tools-toggle-dot')).not.toBeNull();
  });

  it('orders same-name models by thinking level under a remembered name sort, lowest first unless stored otherwise', () => {
    const levels: ModelPickerOption<TestModel>[] = ['high', 'low', 'max'].map((thinkingLevel, i) => ({
      key: 31 + i,
      model: { id: 31 + i, displayName: 'Claude Opus 5.5', modelId: 'claude-opus-5-5', provider: 'Anthropic', thinkingLevel }
    }));
    const shownLevels = () => Array.from(picker().querySelectorAll<HTMLElement>('[role="option"] .thinking-badge'))
      .map(b => b.textContent!.replace('thinking level', '').trim());
    const reopen = (stored: Record<string, unknown>): void => {
      localStorage.setItem('overseer.modelPicker.view.spec.multi', JSON.stringify({ version: 1, providers: [], search: '', ...stored }));
      fixture.destroy();
      fixture = TestBed.createComponent(SixHostComponent);
      fixture.componentInstance.options = levels;
      fixture.detectChanges();
      el = fixture.nativeElement as HTMLElement;
      open();
    };

    reopen({ sort: 'name-asc' });
    expect(shownLevels()).toEqual(['Low', 'High', 'Max']);
    reopen({ sort: 'name-asc', thinking: 'highest-first' });
    expect(shownLevels()).toEqual(['Max', 'High', 'Low']);
    reopen({ sort: 'default', thinking: 'highest-first' });
    expect(shownLevels()).toEqual(['High', 'Low', 'Max']);
  });

  it('opens expanded for a remembered provider filter', () => {
    localStorage.setItem('overseer.modelPicker.view.spec.multi',
      JSON.stringify({ version: 1, sort: 'default', providers: ['openai'], search: '' }));
    fixture.destroy();
    fixture = TestBed.createComponent(SixHostComponent);
    fixture.detectChanges();
    el = fixture.nativeElement as HTMLElement;

    open();
    expect(tools()).not.toBeNull();
    expect(labels()).toEqual(['GPT-6', 'GPT-6 Mini']);
    expect(picker().querySelector<HTMLButtonElement>('.model-picker-provider[data-provider="openai"]')!
      .getAttribute('aria-pressed')).toBe('true');
  });

  it('narrows the list by provider, and Select all shown adds only the shown enabled models', () => {
    open();
    expect(labels().length).toBe(6);
    expect(selectShown()).toBeNull();

    pressProvider('anthropic');
    expect(labels()).toEqual(['Claude Opus 5.5', 'Claude Haiku 5']);
    expect(selectShown()!.textContent).toContain('Select all shown (1)');
    selectShown()!.click();
    fixture.detectChanges();
    expect(host.changes.length).toBe(1);
    expect(host.selected).toEqual([11]);

    expect(selectShown()!.getAttribute('aria-disabled')).toBe('true');
    const tip = picker().querySelector<HTMLElement>(`#${selectShown()!.getAttribute('interestfor')}`)!;
    expect(tip.textContent).toContain('All shown models are already selected.');
  });

  it('keeps a selected model hidden by the filter, and says so', () => {
    host.set({ selected: [15] });
    fixture.detectChanges();
    open();
    pressProvider('openai');
    expect(labels()).toEqual(['GPT-6', 'GPT-6 Mini']);
    expect(picker().querySelector('.gh-multi-picker-chip-label')!.textContent!.trim()).toBe('Gemini Luna 2');
    expect(picker().querySelector('.model-picker-status')!.textContent).toContain('1 selected model hidden');
  });

  it('expands the toolbar and opens its search with a character typed in the collapsed listbox', () => {
    open();
    const box = listbox()!;
    expect(tools()).toBeNull();
    expect(document.activeElement).toBe(box);
    box.dispatchEvent(new KeyboardEvent('keydown', { key: 'g', bubbles: true, cancelable: true }));
    fixture.detectChanges();
    expect(tools()).not.toBeNull();
    expect(filterButton().getAttribute('aria-expanded')).toBe('true');
    const search = picker().querySelector<HTMLInputElement>('.model-picker-search')!;
    expect(search.value).toBe('g');
    expect(document.activeElement).toBe(search);
    expect(search.selectionStart).toBe(1);
    expect(labels()).toEqual(['GPT-6', 'GPT-6 Mini', 'Gemini Luna 2', 'Gemini Flash 4']);
  });

  it('opens the toolbar search with a character typed in the listbox while the toolbar is expanded', () => {
    open();
    expand();
    const box = listbox()!;
    box.focus();
    box.dispatchEvent(new KeyboardEvent('keydown', { key: 'c', bubbles: true, cancelable: true }));
    fixture.detectChanges();
    const search = picker().querySelector<HTMLInputElement>('.model-picker-search')!;
    expect(search.value).toBe('c');
    expect(document.activeElement).toBe(search);
    expect(labels()).toEqual(['Claude Opus 5.5', 'Claude Haiku 5']);
  });

  it('says in the listbox that no model matches', () => {
    open();
    typeSearch('zzz');
    const box = picker().querySelector<HTMLElement>('[role="listbox"]')!;
    const hint = picker().querySelector<HTMLElement>(`#${box.getAttribute('aria-describedby')}`)!;
    expect(box.contains(hint)).toBe(true);
    expect(hint.textContent!.trim()).toBe('No models match "zzz".');
  });

  it('puts the toolbar after the listbox when the list opens upward, the controls row last', () => {
    const up = el.querySelector<HTMLElement>('.six-up')!;
    up.querySelector<HTMLButtonElement>('.selector-trigger')!.click();
    fixture.detectChanges();
    expand(up);
    const popup = up.querySelector<HTMLElement>('.selector-popup')!;
    expect(Array.from(popup.children).map(c => c.getAttribute('role') ?? c.tagName.toLowerCase()))
      .toEqual(['listbox', 'app-model-picker-tools']);
    const rows = Array.from(up.querySelector('.model-picker-tools')!.children).map(c => c.classList[0]);
    expect(rows).toEqual(['model-picker-controls', 'visually-hidden']);
    up.querySelector<HTMLButtonElement>('.model-picker-provider[data-provider="google"]')!.click();
    fixture.detectChanges();
    expect(Array.from(up.querySelector('.model-picker-tools')!.children).map(c => c.classList[0]))
      .toEqual(['model-picker-status', 'model-picker-controls', 'visually-hidden']);
  });

  it('gives the max as the reason Select all shown cannot add', () => {
    host.set({ selected: [11], max: 1 });
    fixture.detectChanges();
    open();
    pressProvider('openai');
    expect(selectShown()!.getAttribute('aria-disabled')).toBe('true');
    const tip = picker().querySelector<HTMLElement>(`#${selectShown()!.getAttribute('interestfor')}`)!;
    expect(tip.textContent).toContain('At most 1 model can be selected.');
  });
});
