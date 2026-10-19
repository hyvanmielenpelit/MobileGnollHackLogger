import { ChangeDetectionStrategy, ChangeDetectorRef, Component, inject } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';

import { ModelPickerSelectShown, ModelPickerToolsComponent } from './model-picker-tools.component';
import { ModelPickerNavigateIntent, ModelPickerProviderFacet, ModelPickerViewState } from './model-picker-view';

const TWO_PROVIDERS: ModelPickerProviderFacet[] = [
  { provider: 'Anthropic', key: 'anthropic', count: 3 },
  { provider: 'OpenAI', key: 'openai', count: 1 }
];

/** Feeds every state change back in, the way a picker does. */
@Component({
  standalone: true,
  imports: [ModelPickerToolsComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="outer" (keydown)="outerKeys.push($any($event).key)">
      <app-model-picker-tools uid="tt" listboxId="tt-listbox" [activeOptionId]="activeOptionId"
                              [state]="state" [providers]="providers" [dateSortAvailable]="dates"
                              [thinkingSortAvailable]="thinking"
                              [shownCount]="shown" [totalCount]="10" [selectShown]="selectShown"
                              [hiddenSelectionText]="hiddenSelectionText" [dropsUp]="dropsUp"
                              (stateChange)="onState($event)" (navigate)="intents.push($event)"
                              (selectShownClick)="selectShownClicks = selectShownClicks + 1"></app-model-picker-tools>
    </div>
  `
})
class HostComponent {
  private cdr = inject(ChangeDetectorRef);
  state: ModelPickerViewState = { sort: 'default', thinking: null, providers: [], search: '' };
  providers: ModelPickerProviderFacet[] = TWO_PROVIDERS;
  dates = true;
  thinking = false;
  shown = 10;
  activeOptionId: string | null = 'tt-opt-1';
  selectShown: ModelPickerSelectShown | null = null;
  hiddenSelectionText: string | null = null;
  dropsUp = false;
  states: ModelPickerViewState[] = [];
  intents: ModelPickerNavigateIntent[] = [];
  outerKeys: string[] = [];
  selectShownClicks = 0;

  onState(state: ModelPickerViewState): void {
    this.states.push(state);
    this.state = state;
    this.cdr.markForCheck();
  }

  set(changes: Partial<Pick<HostComponent,
    'providers' | 'dates' | 'thinking' | 'shown' | 'selectShown' | 'state' | 'hiddenSelectionText' | 'dropsUp'>>): void {
    Object.assign(this, changes);
    this.cdr.markForCheck();
  }
}

describe('ModelPickerToolsComponent', () => {
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

  const search = () => el.querySelector<HTMLInputElement>('.model-picker-search')!;
  const searchToggle = () => el.querySelector<HTMLButtonElement>('.model-picker-search-toggle')!;
  const sortButton = () => el.querySelector<HTMLButtonElement>('.model-picker-sort')!;
  const status = () => el.querySelector<HTMLElement>('.model-picker-status');
  const rows = () => Array.from(el.querySelector('search.model-picker-tools')!.children)
    .map(row => row.classList[0]);
  const sortPanel = () => el.querySelector<HTMLElement>('.model-picker-sort-panel')!;
  const radios = () => Array.from(sortPanel().querySelectorAll<HTMLInputElement>('input[type="radio"]'));
  const thinkingGroup = () => sortPanel().querySelector<HTMLFieldSetElement>('.model-picker-thinking');
  const thinkingRadios = () => Array.from(thinkingGroup()!.querySelectorAll<HTMLInputElement>('input[type="radio"]'));
  const clearSorting = () => el.querySelector<HTMLButtonElement>('#tt-sort-clear')!;
  const toggles = () => Array.from(el.querySelectorAll<HTMLButtonElement>('.model-picker-provider'));
  const liveRegion = () => el.querySelector<HTMLElement>('[role="status"]')!;

  function choose(radio: HTMLInputElement): void {
    radio.checked = true;
    radio.dispatchEvent(new Event('change', { bubbles: true }));
    fixture.detectChanges();
  }

  function update(changes: Parameters<HostComponent['set']>[0]): void {
    host.set(changes);
    fixture.detectChanges();
  }

  function key(target: HTMLElement, keyName: string, init: KeyboardEventInit = {}): KeyboardEvent {
    const event = new KeyboardEvent('keydown', { key: keyName, bubbles: true, cancelable: true, ...init });
    target.dispatchEvent(event);
    fixture.detectChanges();
    return event;
  }

  function openField(): void {
    if (search()) return;
    searchToggle().click();
    fixture.detectChanges();
  }

  function type(text: string): void {
    openField();
    search().value = text;
    search().dispatchEvent(new Event('input', { bubbles: true }));
    fixture.detectChanges();
  }

  function tools(): ModelPickerToolsComponent {
    return fixture.debugElement.query(By.directive(ModelPickerToolsComponent)).componentInstance as ModelPickerToolsComponent;
  }

  it('gives the toolbar the id the picker\'s filter button controls', () => {
    const toolbar = el.querySelector<HTMLElement>('search.model-picker-tools')!;
    expect(toolbar.id).toBe('tt-tools');
    expect(tools().toolsId).toBe('tt-tools');
  });

  it('hides the search field behind a Search toggle, which shows and focuses it', () => {
    expect(search()).toBeNull();
    const toggle = searchToggle();
    expect(toggle.closest('search')).not.toBeNull();
    expect(toggle.getAttribute('aria-label')).toBe('Search models');
    expect(toggle.getAttribute('aria-expanded')).toBe('false');
    expect(toggle.hasAttribute('aria-controls')).toBe(false);
    expect(el.querySelector(`#${toggle.getAttribute('interestfor')}`)!.textContent!.trim()).toBe('Search');
    expect(toggle.querySelector('svg circle')).not.toBeNull();

    toggle.click();
    fixture.detectChanges();
    expect(toggle.getAttribute('aria-expanded')).toBe('true');
    expect(toggle.getAttribute('aria-controls')).toBe(search().id);
    expect(document.activeElement).toBe(search());
  });

  it('opens the field at once when the state carries a remembered search', () => {
    const remembered = TestBed.createComponent(HostComponent);
    remembered.componentInstance.state = { sort: 'default', thinking: null, providers: [], search: 'gpt' };
    remembered.detectChanges();
    const root = remembered.nativeElement as HTMLElement;
    expect(root.querySelector<HTMLInputElement>('.model-picker-search')!.value).toBe('gpt');
    expect(root.querySelector('.model-picker-search-toggle')!.getAttribute('aria-expanded')).toBe('true');
  });

  it('clears the search when the open field is closed, keeping focus on the toggle', () => {
    type('luna');
    searchToggle().click();
    fixture.detectChanges();
    expect(search()).toBeNull();
    expect(host.states.at(-1)!.search).toBe('');
    expect(searchToggle().getAttribute('aria-expanded')).toBe('false');
    expect(document.activeElement).toBe(searchToggle());

    const emitted = host.states.length;
    openField();
    searchToggle().click();
    fixture.detectChanges();
    expect(host.states.length).toBe(emitted);
  });

  it('opens the field with a typed character and focuses it with the caret at the end', () => {
    tools().typeToSearch('g');
    fixture.detectChanges();
    tools().typeToSearch('p');
    fixture.detectChanges();
    expect(search().value).toBe('gp');
    expect(host.states.map(s => s.search)).toEqual(['g', 'gp']);
    expect(document.activeElement).toBe(search());
    expect(search().selectionStart).toBe(2);
  });

  it('puts the controls row nearest the trigger: first downward, last upward', () => {
    type('a');
    expect(rows()).toEqual(['model-picker-controls', 'model-picker-search-row', 'model-picker-status', 'visually-hidden']);
    update({ dropsUp: true });
    expect(rows()).toEqual(['model-picker-status', 'model-picker-search-row', 'model-picker-controls', 'visually-hidden']);
    const actions = el.querySelector('.model-picker-actions')!;
    expect(Array.from(actions.querySelectorAll(':scope > button')).map(b => b.classList[1]))
      .toEqual(['model-picker-search-toggle', 'model-picker-sort']);
  });

  it('shows the status row only while the view narrows, sorts or hides the selection', () => {
    expect(status()).toBeNull();
    expect(liveRegion()).not.toBeNull();
    toggles()[0].click();
    fixture.detectChanges();
    expect(status()!.textContent).toContain('10 models');
    toggles()[0].click();
    fixture.detectChanges();
    expect(status()).toBeNull();

    update({ state: { sort: 'name-asc', thinking: null, providers: [], search: '' } });
    expect(status()).not.toBeNull();
    update({ state: { sort: 'default', thinking: null, providers: [], search: '' }, hiddenSelectionText: 'selected model hidden' });
    expect(status()!.textContent).toContain('selected model hidden');
    expect(el.querySelector('.model-picker-reset')).toBeNull();
  });

  it('makes the search field the combobox of the listbox, inside a search landmark', () => {
    openField();
    const input = search();
    expect(input.closest('search')).not.toBeNull();
    expect(input.type).toBe('search');
    expect(input.getAttribute('role')).toBe('combobox');
    expect(input.getAttribute('aria-expanded')).toBe('true');
    expect(input.getAttribute('aria-controls')).toBe('tt-listbox');
    expect(input.getAttribute('aria-autocomplete')).toBe('list');
    expect(input.getAttribute('aria-activedescendant')).toBe('tt-opt-1');
    expect(input.getAttribute('aria-label')).toBe('Search models');
    expect(input.getAttribute('autocomplete')).toBe('off');
    expect(input.getAttribute('spellcheck')).toBe('false');
    expect(el.querySelector('[title]')).toBeNull();
  });

  it('shows provider toggles only from two providers, pressed by the filter', () => {
    expect(toggles().length).toBe(2);
    expect(el.querySelector('[role="group"]')!.getAttribute('aria-label')).toBe('Filter by provider');
    expect(toggles().map(t => t.getAttribute('aria-pressed'))).toEqual(['false', 'false']);

    toggles()[1].click();
    fixture.detectChanges();
    expect(host.states.at(-1)!.providers).toEqual(['openai']);
    expect(toggles()[1].getAttribute('aria-pressed')).toBe('true');
    expect(toggles()[1].querySelector('svg')).not.toBeNull();
    expect(toggles()[0].querySelector('svg')).toBeNull();

    update({ providers: TWO_PROVIDERS.slice(0, 1) });
    expect(toggles().length).toBe(0);
  });

  it('offers the release-date sorts only where the options have dates', () => {
    expect(radios().map(r => r.value)).toEqual(['default', 'name-asc', 'name-desc', 'release-desc', 'release-asc']);
    update({ dates: false });
    expect(radios().map(r => r.value)).toEqual(['default', 'name-asc', 'name-desc']);
  });

  it('emits a sort as soon as a radio changes and keeps the panel open', () => {
    sortPanel().showPopover();
    fixture.detectChanges();
    const nameDesc = radios().find(r => r.value === 'name-desc')!;
    nameDesc.checked = true;
    nameDesc.dispatchEvent(new Event('change', { bubbles: true }));
    fixture.detectChanges();
    expect(host.states.at(-1)!.sort).toBe('name-desc');
    expect(sortPanel().matches(':popover-open')).toBe(true);
    expect(sortButton().getAttribute('aria-label')).toBe('Sort models: Name (Z–A)');
    expect(el.querySelector('.model-picker-sort-dot')).not.toBeNull();
    sortPanel().hidePopover();
  });

  describe('thinking level', () => {
    const labels = () => thinkingRadios().map(r => r.closest('label')!.textContent!.trim());

    it('is not offered where no options differ in thinking level alone, while Clear sorting is', () => {
      expect(thinkingGroup()).toBeNull();
      expect(radios().length).toBe(5);
      expect(clearSorting().textContent!.trim()).toBe('Clear sorting');
    });

    it('is shown disabled under the default order, Not sorted checked, with its hint', () => {
      update({ thinking: true });
      const group = thinkingGroup()!;
      expect(group.querySelector('legend')!.textContent!.trim()).toBe('Thinking level');
      expect(labels()).toEqual(['Not sorted', 'Lowest first', 'Highest first']);
      expect(group.disabled).toBe(true);
      expect(thinkingRadios().map(r => r.checked)).toEqual([true, false, false]);
      const hint = el.querySelector<HTMLElement>(`#${group.getAttribute('aria-describedby')}`)!;
      expect(hint.textContent!.trim()).toBe('Applies with a name or release-date sort.');
      expect(sortButton().getAttribute('aria-label')).toBe('Sort models: Default order');
    });

    it('starts at Lowest first when a sort is chosen from the default order, and clears with the default order', () => {
      update({ thinking: true });
      sortPanel().showPopover();
      fixture.detectChanges();
      choose(radios().find(r => r.value === 'name-asc')!);
      expect(host.states.at(-1)).toEqual({ sort: 'name-asc', thinking: 'lowest-first', providers: [], search: '' });
      expect(thinkingGroup()!.disabled).toBe(false);
      expect(thinkingGroup()!.hasAttribute('aria-describedby')).toBe(false);
      expect(thinkingRadios().map(r => r.checked)).toEqual([false, true, false]);
      expect(sortButton().getAttribute('aria-label')).toBe('Sort models: Name (A–Z), thinking level lowest first');

      choose(radios().find(r => r.value === 'default')!);
      expect(host.states.at(-1)).toEqual({ sort: 'default', thinking: null, providers: [], search: '' });
      sortPanel().hidePopover();
    });

    it('emits a thinking-level choice at once and keeps the panel open', () => {
      update({ thinking: true, state: { sort: 'name-asc', thinking: 'lowest-first', providers: [], search: '' } });
      sortPanel().showPopover();
      fixture.detectChanges();
      choose(thinkingRadios()[2]);
      expect(host.states.at(-1)!.thinking).toBe('highest-first');
      expect(sortPanel().matches(':popover-open')).toBe(true);
      expect(sortButton().getAttribute('aria-label')).toBe('Sort models: Name (A–Z), thinking level highest first');

      choose(radios().find(r => r.value === 'release-desc')!);
      expect(host.states.at(-1)).toEqual({ sort: 'release-desc', thinking: 'highest-first', providers: [], search: '' });

      choose(thinkingRadios()[0]);
      expect(host.states.at(-1)!.thinking).toBeNull();
      expect(sortButton().getAttribute('aria-label')).toBe('Sort models: Release date (newest first)');
      sortPanel().hidePopover();
    });

    it('leaves the label without the suffix where the order is not offered', () => {
      update({ state: { sort: 'name-asc', thinking: 'lowest-first', providers: [], search: '' } });
      expect(sortButton().getAttribute('aria-label')).toBe('Sort models: Name (A–Z)');
    });
  });

  it('makes Clear sorting inert under the default order, and otherwise clears the sort and keeps the rest', () => {
    sortPanel().showPopover();
    fixture.detectChanges();
    expect(clearSorting().getAttribute('aria-disabled')).toBe('true');
    clearSorting().click();
    fixture.detectChanges();
    expect(host.states).toEqual([]);

    update({ thinking: true, state: { sort: 'name-desc', thinking: 'highest-first', providers: ['openai'], search: 'gp' } });
    expect(clearSorting().hasAttribute('aria-disabled')).toBe(false);
    clearSorting().focus();
    clearSorting().click();
    fixture.detectChanges();
    expect(host.states.at(-1)).toEqual({ sort: 'default', thinking: null, providers: ['openai'], search: 'gp' });
    expect(clearSorting().getAttribute('aria-disabled')).toBe('true');
    expect(document.activeElement).toBe(clearSorting());
    expect(sortPanel().matches(':popover-open')).toBe(true);
    sortPanel().hidePopover();
  });

  it('closes only the Sort panel on Escape there', () => {
    sortPanel().showPopover();
    fixture.detectChanges();
    const event = key(radios()[0], 'Escape');
    expect(event.defaultPrevented).toBe(true);
    expect(host.outerKeys).toEqual([]);
    expect(sortPanel().matches(':popover-open')).toBe(false);
  });

  it('turns Escape in the search field into a close intent that stops there', () => {
    type('luna');
    const event = key(search(), 'Escape');
    expect(event.defaultPrevented).toBe(true);
    expect(host.outerKeys).toEqual([]);
    expect(host.intents).toEqual(['close']);
    expect(search().value).toBe('luna');
  });

  it('turns arrow, page and Ctrl+Home / Ctrl+End keys into intents, and leaves plain Home to the caret', () => {
    openField();
    key(search(), 'ArrowDown');
    key(search(), 'ArrowUp');
    key(search(), 'PageDown');
    key(search(), 'PageUp');
    key(search(), 'Home', { ctrlKey: true });
    key(search(), 'End', { ctrlKey: true });
    key(search(), 'Enter');
    const home = key(search(), 'Home');
    expect(home.defaultPrevented).toBe(false);
    expect(host.intents).toEqual(['next', 'prev', 'pageNext', 'pagePrev', 'first', 'last', 'commit']);
  });

  it('extends with Shift+Arrow only in a multi-select picker', () => {
    openField();
    key(search(), 'ArrowDown', { shiftKey: true });
    update({ selectShown: { count: 2, reason: null } });
    key(search(), 'ArrowDown', { shiftKey: true });
    key(search(), 'ArrowUp', { shiftKey: true });
    expect(host.intents).toEqual(['next', 'extendNext', 'extendPrev']);
  });

  it('clears the search with its button and returns focus to the field', () => {
    type('luna');
    const clear = el.querySelector<HTMLButtonElement>('.model-picker-search-clear')!;
    expect(clear.getAttribute('aria-label')).toBe('Clear search');
    clear.click();
    fixture.detectChanges();
    expect(host.state.search).toBe('');
    expect(document.activeElement).toBe(search());
    expect(el.querySelector('.model-picker-search-clear')).toBeNull();
  });

  it('shows Reset view only while the view differs from the default, and resets everything', () => {
    expect(el.querySelector('.model-picker-reset')).toBeNull();
    type('gp');
    update({ state: { sort: 'name-asc', thinking: 'highest-first', providers: ['openai'], search: 'gp' }, shown: 1 });
    expect(status()!.textContent).toContain('1 of 10 models');
    el.querySelector<HTMLButtonElement>('.model-picker-reset')!.click();
    fixture.detectChanges();
    expect(host.states.at(-1)).toEqual({ sort: 'default', thinking: null, providers: [], search: '' });
    expect(search()).toBeNull();
    expect(status()).toBeNull();
    expect(document.activeElement).toBe(searchToggle());
  });

  it('shows Select all shown while the list is narrowed, aria-disabled with a reason', () => {
    update({ selectShown: { count: 3, reason: null } });
    expect(el.querySelector('.model-picker-select-shown')).toBeNull();
    type('a');
    const button = el.querySelector<HTMLButtonElement>('.model-picker-select-shown')!;
    expect(button.textContent).toContain('Select all shown (3)');
    button.click();
    expect(host.selectShownClicks).toBe(1);

    update({ selectShown: { count: 0, reason: 'All shown models are already selected.' } });
    const blocked = el.querySelector<HTMLButtonElement>('.model-picker-select-shown')!;
    expect(blocked.getAttribute('aria-disabled')).toBe('true');
    const tip = el.querySelector<HTMLElement>(`#${blocked.getAttribute('interestfor')}`)!;
    expect(tip.textContent).toContain('All shown models are already selected.');
  });

  it('announces the count 400 ms after the last change, once', () => {
    vi.useFakeTimers();
    try {
      type('lu');
      update({ shown: 4 });
      type('lun');
      update({ shown: 2 });
      vi.advanceTimersByTime(399);
      fixture.detectChanges();
      expect(liveRegion().textContent).toBe('');
      vi.advanceTimersByTime(1);
      fixture.detectChanges();
      expect(liveRegion().textContent).toBe('2 of 10 models shown.');

      type('luna');
      update({ shown: 0 });
      vi.advanceTimersByTime(400);
      fixture.detectChanges();
      expect(liveRegion().textContent).toBe('No models match "luna".');
    } finally {
      vi.useRealTimers();
    }
  });
});
