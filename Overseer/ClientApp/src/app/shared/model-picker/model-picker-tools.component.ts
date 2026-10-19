import {
  ChangeDetectionStrategy,
  ChangeDetectorRef,
  Component,
  ElementRef,
  EventEmitter,
  Input,
  OnChanges,
  OnDestroy,
  OnInit,
  Output,
  SimpleChanges,
  ViewChild,
  inject
} from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';

import { ensureOverlayPolyfills, refreshAnchorPositioning } from '../../utils/polyfills.util';
import {
  DEFAULT_MODEL_PICKER_VIEW,
  MODEL_PICKER_SORT_LABELS,
  ModelPickerNavigateIntent,
  ModelPickerProviderFacet,
  ModelPickerSort,
  ModelPickerViewState,
  isReleaseSort,
  isViewActive
} from './model-picker-view';

/** The *Select all shown* link of a multi-select picker: how many it would add, and why it cannot. */
export interface ModelPickerSelectShown {
  count: number;
  reason: string | null;
}

const LIVE_DELAY_MS = 400;

/**
 * The view toolbar of a model picker's popup: a controls row of provider toggles, a Search toggle and
 * a Sort popover of five radios; the search field, shown by the toggle, which is the listbox's
 * combobox; and a status line while the view narrows, sorts or hides the selection. The controls row
 * is the edge nearest the trigger (`dropsUp` puts it last), and the DOM order is the visual order.
 * Shown by the picker's filter button, which points `aria-controls` at `toolsId`. Created each time
 * it is shown, so the field starts open only with a remembered search. Presentational:
 * the host picker owns the view and feeds `state` back after each `stateChange`. Every id derives
 * from `uid`. The look is global (`styles.scss`, `.model-picker-tools`).
 */
@Component({
  selector: 'app-model-picker-tools',
  standalone: true,
  imports: [NgTemplateOutlet],
  templateUrl: './model-picker-tools.component.html',
  styleUrl: './model-picker-tools.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class ModelPickerToolsComponent implements OnInit, OnChanges, OnDestroy {
  private host = inject<ElementRef<HTMLElement>>(ElementRef);
  private cdr = inject(ChangeDetectorRef);

  @Input({ required: true }) uid!: string;
  @Input() listboxId: string | null = null;
  @Input() activeOptionId: string | null = null;
  @Input() state: ModelPickerViewState = DEFAULT_MODEL_PICKER_VIEW;
  @Input() providers: readonly ModelPickerProviderFacet[] = [];
  /** Whether the two release-date sorts are offered. */
  @Input() dateSortAvailable = false;
  /** The models shown, the none option excluded. */
  @Input() shownCount = 0;
  @Input() totalCount = 0;
  @Input() hiddenSelectionText: string | null = null;
  /** Multi-select pickers only; its presence also turns Shift+Arrow into extend intents. */
  @Input() selectShown: ModelPickerSelectShown | null = null;
  /** The host's list opens upward, so the controls row comes last, next to the trigger. */
  @Input() dropsUp = false;

  @Output() stateChange = new EventEmitter<ModelPickerViewState>();
  @Output() navigate = new EventEmitter<ModelPickerNavigateIntent>();
  @Output() selectShownClick = new EventEmitter<void>();

  @ViewChild('search') private searchRef?: ElementRef<HTMLInputElement>;

  /** Whether the search field is shown. A closed field never filters: closing it clears the search. */
  searchOpen = false;
  /** Whether the Sort popover is open; the popover polyfill does not set `aria-expanded`. */
  sortOpen = false;
  /** The live region's text. */
  liveText = '';

  private liveTimer: ReturnType<typeof setTimeout> | null = null;

  readonly sortChoices: readonly ModelPickerSort[] = ['default', 'name-asc', 'name-desc', 'release-desc', 'release-asc'];

  get toolsId(): string { return `${this.uid}-tools`; }
  get searchId(): string { return `${this.uid}-search`; }
  get sortId(): string { return `${this.uid}-sort`; }
  get sortPanelId(): string { return `${this.uid}-sort-panel`; }
  get clearId(): string { return `${this.uid}-search-clear`; }
  get searchToggleId(): string { return `${this.uid}-search-toggle`; }

  /** For the host's focus on open. */
  get searchShown(): boolean { return this.searchOpen; }

  /** The status row is rendered only while the view narrows, sorts or hides the selection. */
  get statusShown(): boolean { return this.viewActive || this.hiddenSelectionText !== null; }

  /** The sort as it applies: a release-date sort reads as the default order where no option has a date. */
  get shownSort(): ModelPickerSort {
    return isReleaseSort(this.state.sort) && !this.dateSortAvailable ? 'default' : this.state.sort;
  }

  get sortLabel(): string { return MODEL_PICKER_SORT_LABELS[this.shownSort]; }

  get viewActive(): boolean { return isViewActive(this.state); }

  /** A search or a provider filter narrows the list. */
  get narrowed(): boolean { return this.state.search.trim() !== '' || this.state.providers.length > 0; }

  get countText(): string {
    const noun = this.totalCount === 1 ? 'model' : 'models';
    return this.shownCount === this.totalCount
      ? `${this.totalCount} ${noun}`
      : `${this.shownCount} of ${this.totalCount} ${noun}`;
  }

  get visibleSortChoices(): readonly ModelPickerSort[] {
    return this.dateSortAvailable ? this.sortChoices : this.sortChoices.filter(s => !isReleaseSort(s));
  }

  sortChoiceLabel(sort: ModelPickerSort): string { return MODEL_PICKER_SORT_LABELS[sort]; }

  isProviderPressed(facet: ModelPickerProviderFacet): boolean { return this.state.providers.includes(facet.key); }

  ngOnInit(): void {
    ensureOverlayPolyfills();
    this.searchOpen = this.state.search.trim() !== '';
  }

  ngOnChanges(changes: SimpleChanges): void {
    const counts = changes['shownCount'] ?? changes['totalCount'];
    const state = changes['state'];
    if ((counts && !counts.firstChange) || (state && !state.firstChange)) {
      this.scheduleLive();
    }
  }

  ngOnDestroy(): void {
    if (this.liveTimer !== null) clearTimeout(this.liveTimer);
  }

  /** Focuses the search field with the caret at the end; does nothing while the field is closed. */
  focusSearch(): void {
    const input = this.searchRef?.nativeElement;
    if (!this.searchOpen || !input) return;
    input.focus();
    const end = input.value.length;
    try {
      input.setSelectionRange(end, end);
    } catch {
      // Not every input type supports a selection.
    }
  }

  /** Shows the field and focuses it; on an open field, hides it, clears the search and keeps focus on the toggle. */
  toggleSearch(): void {
    if (!this.searchOpen) {
      this.showSearch();
      this.focusSearch();
      return;
    }
    this.hideSearch();
    if (this.state.search !== '') this.emit({ ...this.state, search: '' });
  }

  /** Opens the field if needed and appends a character typed in the listbox, moving focus there. */
  typeToSearch(char: string): void {
    if (!this.searchOpen) this.showSearch();
    const input = this.searchRef?.nativeElement;
    if (!input) return;
    const search = this.state.search + char;
    // Written ahead of the binding, so the caret lands after the new character.
    input.value = search;
    this.focusSearch();
    this.emit({ ...this.state, search });
  }

  onSearchInput(event: Event): void {
    this.emit({ ...this.state, search: (event.target as HTMLInputElement).value });
  }

  clearSearch(): void {
    this.emit({ ...this.state, search: '' });
    this.focusSearch();
  }

  onSearchKeydown(event: KeyboardEvent): void {
    const multi = this.selectShown !== null;
    let intent: ModelPickerNavigateIntent | null = null;
    switch (event.key) {
      case 'ArrowDown':
        intent = event.shiftKey && multi ? 'extendNext' : 'next';
        break;
      case 'ArrowUp':
        intent = event.shiftKey && multi ? 'extendPrev' : 'prev';
        break;
      case 'PageDown':
        intent = 'pageNext';
        break;
      case 'PageUp':
        intent = 'pagePrev';
        break;
      case 'Home':
        // Plain Home and End move the caret, as in any text field.
        if (event.ctrlKey || event.metaKey) intent = 'first';
        break;
      case 'End':
        if (event.ctrlKey || event.metaKey) intent = 'last';
        break;
      case 'Enter':
        intent = 'commit';
        break;
      case 'Escape':
        // Closes the list and keeps the text: no native clear, and no enclosing <dialog> close.
        event.stopPropagation();
        intent = 'close';
        break;
    }
    if (intent === null) return;
    event.preventDefault();
    this.navigate.emit(intent);
  }

  chooseSort(sort: ModelPickerSort): void {
    this.emit({ ...this.state, sort });
  }

  onSortToggle(event: Event): void {
    this.sortOpen = (event as ToggleEvent).newState === 'open';
    this.cdr.markForCheck();
    if (this.sortOpen) {
      refreshAnchorPositioning();
      this.cdr.detectChanges();
      const panel = this.sortPanel();
      (panel?.querySelector<HTMLInputElement>('input:checked') ?? panel?.querySelector<HTMLInputElement>('input'))?.focus();
      return;
    }
    const active = document.activeElement;
    if (!active || active === document.body || !!this.sortPanel()?.contains(active)) {
      this.sortButton()?.focus();
    }
  }

  /** Escape closes the Sort popover only; the picker and any dialog around it stay open. */
  onSortPanelKeydown(event: KeyboardEvent): void {
    if (event.key !== 'Escape') return;
    event.preventDefault();
    event.stopPropagation();
    try {
      this.sortPanel()?.hidePopover();
    } catch {
      // Already hidden.
    }
    this.sortButton()?.focus();
  }

  toggleProvider(facet: ModelPickerProviderFacet): void {
    const providers = this.isProviderPressed(facet)
      ? this.state.providers.filter(p => p !== facet.key)
      : [...this.state.providers, facet.key];
    this.emit({ ...this.state, providers });
  }

  /** Emits the default view and closes the field; focus goes to the Search toggle, since Reset view itself disappears. */
  resetView(): void {
    this.hideSearch();
    this.emit({ sort: 'default', providers: [], search: '' });
  }

  onSelectShown(): void {
    this.selectShownClick.emit();
  }

  private emit(state: ModelPickerViewState): void {
    this.stateChange.emit(state);
  }

  private showSearch(): void {
    this.searchOpen = true;
    this.cdr.markForCheck();
    this.cdr.detectChanges();
  }

  /** Focus moves to the toggle before the field and its row are removed, so it never falls to the body. */
  private hideSearch(): void {
    this.searchToggle()?.focus();
    this.searchOpen = false;
    this.cdr.markForCheck();
    this.cdr.detectChanges();
  }

  /** The live text settles 400 ms after the last change; a repeated message alternates a trailing space. */
  private scheduleLive(): void {
    if (this.liveTimer !== null) clearTimeout(this.liveTimer);
    this.liveTimer = setTimeout(() => {
      this.liveTimer = null;
      const search = this.state.search.trim();
      const text = this.shownCount === 0
        ? (search ? `No models match "${search}".` : 'No models match the filter.')
        : `${this.shownCount} of ${this.totalCount} models shown.`;
      this.liveText = this.liveText === text ? `${text} ` : text;
      this.cdr.markForCheck();
    }, LIVE_DELAY_MS);
  }

  private sortPanel(): HTMLElement | null {
    return this.host.nativeElement.querySelector<HTMLElement>(`[id="${this.sortPanelId}"]`);
  }

  private sortButton(): HTMLElement | null {
    return this.host.nativeElement.querySelector<HTMLElement>(`[id="${this.sortId}"]`);
  }

  private searchToggle(): HTMLElement | null {
    return this.host.nativeElement.querySelector<HTMLElement>(`[id="${this.searchToggleId}"]`);
  }
}
