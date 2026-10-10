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
import type { ModelAvailability } from '../model-availability/model-availability';

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
  imports: [ModelOptionBadgesComponent, NgTemplateOutlet],
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

  /** Every committed choice, including re-choosing the selected option. */
  @Output() selectionChange = new EventEmitter<ModelPickerSelection<M>>();

  @ViewChild('trigger', { static: true }) private triggerRef!: ElementRef<HTMLButtonElement>;
  @ViewChild('listbox') private listboxRef?: ElementRef<HTMLElement>;

  readonly uid = `mp-${++nextUid}`;

  open = false;
  activeIndex = 0;
  flat: FlatEntry<M>[] = [];
  sections: Section<M>[] = [];

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

  ngOnInit(): void {
    if (isDevMode() && !this.labelledBy && !this.label) {
      console.warn('app-model-picker: set labelledBy or label so the picker has an accessible name.');
    }
  }

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['options'] || changes['noneLabel']) {
      this.rebuild();
    }
    if (changes['disabled'] && this.disabled) {
      this.close(false);
    }
  }

  ngOnDestroy(): void {
    this.detachDocumentListener();
    this.clearTypeahead();
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
          this.typeAhead(event.key);
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

  commit(index: number): void {
    const entry = this.flat[index];
    if (!entry) return;
    this.selectionChange.emit({ key: entry.key, model: entry.model });
    this.close(true);
  }

  private openList(activeIndex: number): void {
    if (this.open) return;
    this.open = true;
    this.activeIndex = this.clampIndex(activeIndex);
    this.clearTypeahead();
    this.cdr.markForCheck();
    this.cdr.detectChanges();
    this.listboxRef?.nativeElement.focus();
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
    this.detachDocumentListener();
    this.clearTypeahead();
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
    if (!id) return;
    const option = this.host.nativeElement.querySelector<HTMLElement>(`#${id}`);
    option?.scrollIntoView?.({ block: 'nearest' });
  }

  private detachDocumentListener(): void {
    this.removeDocumentListener?.();
    this.removeDocumentListener = null;
  }

  private rebuild(): void {
    const flat: FlatEntry<M>[] = [];
    if (this.noneLabel !== null) {
      flat.push({ key: null, model: null, searchName: this.noneLabel.toLowerCase() });
    }
    const offset = flat.length;
    const sections: Section<M>[] = [];
    let current: Section<M> | null = null;
    let groupCount = 0;
    this.options.forEach((option, i) => {
      flat.push({
        key: option.key,
        model: option.model,
        searchName: [option.tag, this.modelName(option.model)].filter(Boolean).join(' ').toLowerCase()
      });
      const group = option.group ?? null;
      if (!current || current.group !== group) {
        current = { group, headingId: group ? `${this.uid}-grp-${++groupCount}` : null, items: [] };
        sections.push(current);
      }
      current.items.push({ option, index: offset + i });
    });
    this.flat = flat;
    this.sections = sections;
    this.activeIndex = flat.length > 0 ? this.clampIndex(this.activeIndex) : 0;
  }
}
