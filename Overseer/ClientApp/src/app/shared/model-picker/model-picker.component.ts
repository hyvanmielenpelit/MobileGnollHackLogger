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
  Renderer2,
  SimpleChanges,
  ViewChild,
  inject,
  isDevMode
} from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';
import { ModelOptionBadgesComponent } from './model-option-badges.component';
import { ModelPickerToolsComponent } from './model-picker-tools.component';
import {
  DEFAULT_MODEL_PICKER_VIEW,
  ModelPickerMatchPart,
  ModelPickerNavigateIntent,
  ModelPickerProviderFacet,
  ModelPickerViewState,
  ModelPickerViewTools,
  ModelPickerViewWriter,
  applyModelPickerView,
  effectiveView,
  hasReleaseDates,
  isReleaseSort,
  matchParts,
  providerFacets,
  readModelPickerView,
  releaseMonthLabel,
  sameModelPickerView,
  searchTerms,
  showsViewTools
} from './model-picker-view';
import type { ModelAvailability } from '../model-availability/model-availability';
import { scrollOptionIntoListbox } from '../../utils/listbox-scroll.util';

/** The fields a picker renders. Structural, so SystemAiConfigDto and UserAiModel both fit. */
export interface ModelPickerModel {
  displayName?: string | null;
  modelId?: string | null;
  provider?: string | null;
  thinkingLevel?: string | null;
  reasoningMode?: string | null;
  parallelExecutionMode?: number | null;
  effectiveInputPricePerMillion?: number | null;
  effectiveOutputPricePerMillion?: number | null;
  /** A model that needs attention gets a *Removed* or *Not in catalog* chip before its badges. */
  modelAvailability?: ModelAvailability | null;
  /** The catalog release date, yyyy-MM-dd; the toolbar offers the release-date sorts when an option has one. */
  releaseDate?: string | null;
}

export type ModelPickerKey = string | number;

export interface ModelPickerOption<M extends ModelPickerModel = ModelPickerModel> {
  /** Unique within one picker. */
  key: ModelPickerKey;
  model: M;
  /** Consecutive options with the same group share one heading. */
  group?: string;
  /** A short role shown before the model name, e.g. the panel member a calibration compares against. */
  tag?: string;
  /** A muted note after the badges; shown by `app-model-multi-picker` only. */
  detail?: string;
  /** Makes the option unavailable, with this reason under it; honored by `app-model-multi-picker` only. */
  disabledReason?: string;
  /** A chip's notes, one line each, in this order; shown by `app-model-multi-picker` only. */
  notes?: readonly ModelPickerNote[];
}

/** One line of a chip's notes: a blocker in the error tone, or a warning. */
export interface ModelPickerNote {
  text: string;
  tone: 'warning' | 'error';
}

export interface ModelPickerSelection<M extends ModelPickerModel = ModelPickerModel> {
  /** `null` for the none option. */
  key: ModelPickerKey | null;
  model: M | null;
}

/**
 * Options keyed `keyPrefix + id` when a prefix is given (the `u_` / `s_` keys the chat and Models
 * pages store), and by the plain numeric `id` otherwise.
 */
export function toModelPickerOptions<M extends ModelPickerModel & { id?: number | null }>(
  models: readonly M[], group?: string, keyPrefix = ''): ModelPickerOption<M>[] {
  return models.map((model, i) => ({
    key: keyPrefix ? `${keyPrefix}${model.id}` : (model.id ?? `#${i}`),
    model,
    ...(group ? { group } : {})
  }));
}

/** One row of the listbox: the none option (`model` null) or a model. */
interface FlatEntry<M> {
  key: ModelPickerKey | null;
  model: M | null;
  /** Lower-cased visible name, for type-ahead. */
  searchName: string;
}

interface SectionItem<M extends ModelPickerModel> {
  option: ModelPickerOption<M>;
  index: number;
  /** The name in runs, the ones matching the search marked. */
  nameParts: ModelPickerMatchPart[];
  /** The release month while a release-date sort applies, else null. */
  release: string | null;
}

/** A run of options sharing one `group` (or none). */
interface Section<M extends ModelPickerModel> {
  group: string | null;
  headingId: string | null;
  items: SectionItem<M>[];
}

const TYPEAHEAD_RESET_MS = 500;
const PAGE_STEP = 10;

let nextUid = 0;

/**
 * A model picker implementing the WAI-ARIA *collapsible listbox* pattern: a button that opens a
 * popup `role="listbox"`, which takes focus and tracks the active option with
 * `aria-activedescendant`. Presentational: the host owns the selection and feeds `selectedKey`
 * back in. The `.custom-model-selector` look is global (`styles.scss`); the selected option is
 * styled from `aria-selected`, the open trigger from `aria-expanded`.
 */
@Component({
  selector: 'app-model-picker',
  standalone: true,
  imports: [ModelOptionBadgesComponent, ModelPickerToolsComponent, NgTemplateOutlet],
  templateUrl: './model-picker.component.html',
  styleUrl: './model-picker.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {
    'class': 'custom-model-selector',
    '[class.compact]': "variant === 'compact'",
    '[class.drops-up]': 'dropsUp',
    '[class.narrow-hides-badges]': 'narrowHidesBadges'
  }
})
export class ModelPickerComponent<M extends ModelPickerModel = ModelPickerModel> implements OnInit, OnChanges, OnDestroy {
  private host = inject<ElementRef<HTMLElement>>(ElementRef);
  private renderer = inject(Renderer2);
  private cdr = inject(ChangeDetectorRef);

  @Input() options: readonly ModelPickerOption<M>[] = [];
  /** Compared with `===`. */
  @Input() selectedKey: ModelPickerKey | null = null;
  /** When set, a first option with key `null`, shown muted on the trigger while nothing is selected. */
  @Input() noneLabel: string | null = null;
  /** False draws the none option at full strength: a none choice that is a setting of its own, not an absence. */
  @Input() noneMuted = true;
  @Input() placeholder = 'Select Model';
  /** Shown in the popup when `options` is empty. */
  @Input() emptyHint: string | null = null;
  /** Id of the host's visible label. */
  @Input() labelledBy: string | null = null;
  /** Only for a picker with no visible label: rendered as a visually hidden label. */
  @Input() label: string | null = null;
  /** Id of the host's hint, carried on the trigger's `aria-describedby`. */
  @Input() describedBy: string | null = null;
  @Input() triggerId: string | null = null;
  @Input() showPrice = false;
  @Input() showParallel = false;
  @Input() variant: 'form' | 'compact' = 'form';
  @Input() dropsUp = false;
  /** Hides the provider and parallel badges below 992 px. */
  @Input() narrowHidesBadges = false;
  /** The trigger is aria-disabled and the list does not open; the host names the reason through describedBy. */
  @Input() disabled = false;
  /** The search, sort and provider toolbar: `auto` shows it from 2 options up. */
  @Input() viewTools: ModelPickerViewTools = 'auto';
  /** Names the picker's role; its view is stored as `overseer.modelPicker.view.<key>`. Null keeps it in memory only. */
  @Input() viewStateKey: string | null = null;

  /** Every committed choice, including re-choosing the selected option. */
  @Output() selectionChange = new EventEmitter<ModelPickerSelection<M>>();

  @ViewChild('trigger', { static: true }) private triggerRef!: ElementRef<HTMLButtonElement>;
  @ViewChild('listbox') private listboxRef?: ElementRef<HTMLElement>;
  @ViewChild(ModelPickerToolsComponent) private toolsRef?: ModelPickerToolsComponent;

  readonly uid = `mp-${++nextUid}`;

  open = false;
  activeIndex = 0;
  flat: FlatEntry<M>[] = [];
  sections: Section<M>[] = [];

  /** Whether the popup has the view toolbar; without it the stored view is not applied. */
  toolsShown = false;
  /** The stored view, shown in the toolbar as is. */
  viewState: ModelPickerViewState = DEFAULT_MODEL_PICKER_VIEW;
  facets: ModelPickerProviderFacet[] = [];
  dateSortAvailable = false;
  /** The models the view shows, the none option excluded. */
  shownCount = 0;
  /**
   * The listbox's least height, in px, while open with the toolbar: it only rises, so filtering
   * never shrinks the popup or moves the controls. Null when closed.
   */
  listHold: number | null = null;

  private visibleKeys = new Set<ModelPickerKey>();
  private viewWriter = new ModelPickerViewWriter(() => this.viewStateKey);
  private removeDocumentListener: (() => void) | null = null;
  private typeahead = '';
  private typeaheadTimer: ReturnType<typeof setTimeout> | null = null;

  get triggerIdValue(): string { return this.triggerId || `${this.uid}-trigger`; }

  get labelIdValue(): string | null {
    if (this.labelledBy) return this.labelledBy;
    return this.label ? `${this.uid}-label` : null;
  }

  get triggerLabelledBy(): string | null {
    return this.labelIdValue ? `${this.labelIdValue} ${this.triggerIdValue}` : null;
  }

  get listboxId(): string { return `${this.uid}-listbox`; }

  get emptyHintId(): string { return `${this.uid}-empty`; }

  get noMatchId(): string { return `${this.uid}-no-match`; }

  /** The view hides every model. */
  get noMatchShown(): boolean { return this.toolsShown && this.shownCount === 0 && this.options.length > 0; }

  /** The same words as the toolbar's live line. */
  get noMatchText(): string {
    const search = this.viewState.search.trim();
    return search ? `No models match "${search}".` : 'No models match the filter.';
  }

  get listboxDescribedBy(): string | null {
    if (this.options.length === 0 && !this.hasNoneOption && this.emptyHint) return this.emptyHintId;
    return this.noMatchShown ? this.noMatchId : null;
  }

  get activeOptionId(): string | null {
    return this.open && this.flat.length > 0 ? this.optionId(this.activeIndex) : null;
  }

  get hasNoneOption(): boolean { return this.noneLabel !== null; }

  /** The selected model, or null when the selection is the none option or not among the options. */
  get selectedModel(): M | null {
    if (this.selectedKey === null) return null;
    return this.options.find(o => o.key === this.selectedKey)?.model ?? null;
  }

  /** The selected option's tag, or null when it has none or nothing is selected. */
  get selectedTag(): string | null {
    if (this.selectedKey === null) return null;
    return this.options.find(o => o.key === this.selectedKey)?.tag ?? null;
  }

  /** `selected model hidden` while the view hides the selected model. */
  get hiddenSelectionText(): string | null {
    if (!this.toolsShown || this.selectedKey === null || this.visibleKeys.has(this.selectedKey)) return null;
    return this.options.some(o => o.key === this.selectedKey) ? 'selected model hidden' : null;
  }

  ngOnInit(): void {
    if (isDevMode() && !this.labelledBy && !this.label) {
      console.warn('app-model-picker: set labelledBy or label so the picker has an accessible name.');
    }
    this.viewState = readModelPickerView(this.viewStateKey);
    this.rebuild();
  }

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['options'] || changes['noneLabel'] || changes['viewTools']) {
      this.rebuild();
    }
    if (changes['disabled'] && this.disabled) {
      this.close(false);
    }
  }

  ngOnDestroy(): void {
    this.detachDocumentListener();
    this.clearTypeahead();
    this.viewWriter.dispose();
  }

  optionId(index: number): string { return `${this.uid}-opt-${index}`; }

  isSelected(key: ModelPickerKey | null): boolean { return key === this.selectedKey; }

  modelName(model: ModelPickerModel): string { return model.displayName || model.modelId || ''; }

  toggle(): void {
    if (this.disabled) return;
    if (this.open) {
      this.close(true);
    } else {
      this.openList(this.selectedIndex() ?? 0);
    }
  }

  onTriggerKeydown(event: KeyboardEvent): void {
    if (this.disabled) return;
    switch (event.key) {
      case 'ArrowDown':
        event.preventDefault();
        if (!this.open) this.openList(this.selectedIndex() ?? 0);
        break;
      case 'ArrowUp':
        event.preventDefault();
        if (!this.open) this.openList(this.selectedIndex() ?? this.flat.length - 1);
        break;
    }
  }

  onListboxKeydown(event: KeyboardEvent): void {
    const last = this.flat.length - 1;
    switch (event.key) {
      case 'ArrowDown':
        this.moveActive(this.activeIndex + 1);
        break;
      case 'ArrowUp':
        this.moveActive(this.activeIndex - 1);
        break;
      case 'Home':
        this.moveActive(0);
        break;
      case 'End':
        this.moveActive(last);
        break;
      case 'PageDown':
        this.moveActive(this.activeIndex + PAGE_STEP);
        break;
      case 'PageUp':
        this.moveActive(this.activeIndex - PAGE_STEP);
        break;
      case 'Enter':
        this.commit(this.activeIndex);
        break;
      case ' ':
        if (this.typeahead) {
          this.typeAhead(' ');
        } else {
          this.commit(this.activeIndex);
        }
        break;
      case 'Escape':
        // Stops an enclosing <dialog> from treating the same Escape as its own close request.
        event.stopPropagation();
        this.close(true);
        break;
      case 'Tab':
        // Focus goes back to the trigger first, so the browser's own Tab moves on from there.
        this.triggerRef.nativeElement.focus();
        this.close(false);
        return;
      default:
        if (event.key.length === 1 && !event.ctrlKey && !event.metaKey && !event.altKey) {
          // With the toolbar, typing opens its search field instead of type-ahead.
          if (this.toolsShown && this.toolsRef) {
            this.toolsRef.typeToSearch(event.key);
          } else {
            this.typeAhead(event.key);
          }
          break;
        }
        return;
    }
    event.preventDefault();
  }

  onFocusOut(event: FocusEvent): void {
    const next = event.relatedTarget as Node | null;
    if (!next || !this.host.nativeElement.contains(next)) {
      this.close(false);
    }
  }

  /** Keeps focus where it is (the search field) when the popup's non-interactive parts are pressed; a click still commits. */
  onPopupMousedown(event: MouseEvent): void {
    const target = event.target as Element | null;
    if (target && !target.closest('input, button, label, select, textarea, [popover]')) {
      event.preventDefault();
    }
  }

  onNavigate(intent: ModelPickerNavigateIntent): void {
    switch (intent) {
      case 'next':
      case 'extendNext':
        this.moveActive(this.activeIndex + 1);
        break;
      case 'prev':
      case 'extendPrev':
        this.moveActive(this.activeIndex - 1);
        break;
      case 'pageNext':
        this.moveActive(this.activeIndex + PAGE_STEP);
        break;
      case 'pagePrev':
        this.moveActive(this.activeIndex - PAGE_STEP);
        break;
      case 'first':
        this.moveActive(0);
        break;
      case 'last':
        this.moveActive(this.flat.length - 1);
        break;
      case 'commit':
        this.commit(this.activeIndex);
        break;
      case 'close':
        this.close(true);
        break;
    }
  }

  onViewStateChange(state: ModelPickerViewState): void {
    const previous = this.viewState;
    if (sameModelPickerView(previous, state)) return;
    this.viewState = state;
    const searchOnly = previous.sort === state.sort
      && previous.providers.length === state.providers.length
      && previous.providers.every((p, i) => p === state.providers[i]);
    this.viewWriter.write(state, searchOnly);
    // Measured before the rows change.
    this.holdListHeight();
    this.rebuild(true);
    this.cdr.markForCheck();
    this.cdr.detectChanges();
    this.scrollActiveIntoView();
  }

  commit(index: number): void {
    const entry = this.flat[index];
    if (!entry) return;
    this.selectionChange.emit({ key: entry.key, model: entry.model });
    this.close(true);
  }

  private openList(activeIndex: number): void {
    if (this.open) return;
    this.open = true;
    this.activeIndex = this.clampIndex(this.toolsShown && this.hasSearch() ? this.firstModelIndex() : activeIndex);
    this.clearTypeahead();
    this.cdr.markForCheck();
    this.cdr.detectChanges();
    // The field takes focus only when it opens with a remembered search, and on a touch screen not
    // even then, so the on-screen keyboard waits for a tap on the field.
    if (this.toolsShown && this.toolsRef?.searchShown && finePointer()) {
      this.toolsRef.focusSearch();
    } else {
      this.listboxRef?.nativeElement.focus();
    }
    this.holdListHeight();
    this.scrollActiveIntoView();
    this.removeDocumentListener = this.renderer.listen('document', 'pointerdown', (event: PointerEvent) => {
      if (!this.host.nativeElement.contains(event.target as Node)) {
        this.close(false);
      }
    });
  }

  private close(focusTrigger: boolean): void {
    if (!this.open) return;
    this.open = false;
    this.listHold = null;
    this.detachDocumentListener();
    this.clearTypeahead();
    this.viewWriter.flush();
    this.cdr.markForCheck();
    if (focusTrigger) {
      this.triggerRef.nativeElement.focus();
    }
  }

  private moveActive(index: number): void {
    if (this.flat.length === 0) return;
    this.activeIndex = this.clampIndex(index);
    this.cdr.markForCheck();
    this.cdr.detectChanges();
    this.scrollActiveIntoView();
  }

  /**
   * Printable characters accumulate for 500 ms and match the start of a visible name. The search
   * starts after the active option, except that a longer buffer may keep the active one; a
   * repeated single character cycles through the names that start with it.
   */
  private typeAhead(char: string): void {
    if (this.flat.length === 0) return;
    this.typeahead += char.toLowerCase();
    if (this.typeaheadTimer !== null) clearTimeout(this.typeaheadTimer);
    this.typeaheadTimer = setTimeout(() => this.clearTypeahead(), TYPEAHEAD_RESET_MS);

    const buffer = this.typeahead;
    const repeated = [...buffer].every(c => c === buffer[0]);
    const needle = repeated ? buffer[0] : buffer;
    const start = repeated ? this.activeIndex + 1 : this.activeIndex;
    const count = this.flat.length;
    for (let offset = 0; offset < count; offset++) {
      const index = (start + offset) % count;
      if (this.flat[index].searchName.startsWith(needle)) {
        this.moveActive(index);
        return;
      }
    }
  }

  private clearTypeahead(): void {
    this.typeahead = '';
    if (this.typeaheadTimer !== null) {
      clearTimeout(this.typeaheadTimer);
      this.typeaheadTimer = null;
    }
  }

  private selectedIndex(): number | null {
    const index = this.flat.findIndex(e => e.key === this.selectedKey);
    return index >= 0 ? index : null;
  }

  private clampIndex(index: number): number {
    return Math.max(0, Math.min(this.flat.length - 1, index));
  }

  private scrollActiveIntoView(): void {
    const id = this.activeOptionId;
    const listbox = this.listboxRef?.nativeElement;
    if (!id || !listbox) return;
    const option = this.host.nativeElement.querySelector<HTMLElement>(`#${id}`);
    if (option) scrollOptionIntoListbox(listbox, option);
  }

  /** Raises `listHold` to the listbox's current height while the toolbar is shown. */
  private holdListHeight(): void {
    const listbox = this.listboxRef?.nativeElement;
    if (!this.open || !this.toolsShown || !listbox) return;
    this.listHold = Math.max(this.listHold ?? 0, listbox.getBoundingClientRect().height);
    this.cdr.markForCheck();
  }

  private detachDocumentListener(): void {
    this.removeDocumentListener?.();
    this.removeDocumentListener = null;
  }

  private hasSearch(): boolean {
    return this.viewState.search.trim() !== '';
  }

  /** The first model option, after the none option; the none option when no model is shown. */
  private firstModelIndex(): number {
    return this.hasNoneOption && this.flat.length > 1 ? 1 : 0;
  }

  /**
   * `viewChanged` moves the active option: to the first shown model while there is a search, else
   * to the selected option if it is shown, else to the first.
   */
  private rebuild(viewChanged = false): void {
    this.toolsShown = showsViewTools(this.viewTools, this.options.length);
    const view = this.toolsShown ? effectiveView(this.viewState, this.options) : DEFAULT_MODEL_PICKER_VIEW;
    const order = this.toolsShown ? applyModelPickerView(this.options, view) : this.options.map((_, i) => i);
    const terms = searchTerms(view.search);
    const releaseShown = isReleaseSort(view.sort);
    this.facets = this.toolsShown ? providerFacets(this.options) : [];
    this.dateSortAvailable = this.toolsShown && hasReleaseDates(this.options);

    const flat: FlatEntry<M>[] = [];
    if (this.noneLabel !== null) {
      flat.push({ key: null, model: null, searchName: this.noneLabel.toLowerCase() });
    }
    const sections: Section<M>[] = [];
    const visibleKeys = new Set<ModelPickerKey>();
    let current: Section<M> | null = null;
    let groupCount = 0;
    for (const i of order) {
      const option = this.options[i];
      const name = this.modelName(option.model);
      visibleKeys.add(option.key);
      flat.push({
        key: option.key,
        model: option.model,
        searchName: [option.tag, name].filter(Boolean).join(' ').toLowerCase()
      });
      const group = option.group ?? null;
      if (!current || current.group !== group) {
        current = { group, headingId: group ? `${this.uid}-grp-${++groupCount}` : null, items: [] };
        sections.push(current);
      }
      current.items.push({
        option,
        index: flat.length - 1,
        nameParts: matchParts(name, terms),
        release: releaseShown ? (releaseMonthLabel(option.model.releaseDate) || 'date unknown') : null
      });
    }
    this.flat = flat;
    this.sections = sections;
    this.visibleKeys = visibleKeys;
    this.shownCount = order.length;

    if (viewChanged) {
      const selected = this.selectedIndex();
      this.activeIndex = this.hasSearch() ? this.firstModelIndex() : (selected ?? 0);
    }
    this.activeIndex = flat.length > 0 ? this.clampIndex(this.activeIndex) : 0;
  }
}

function finePointer(): boolean {
  return typeof matchMedia === 'function' && matchMedia('(pointer: fine)').matches;
}
