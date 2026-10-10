import {
  AfterViewChecked,
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
  TemplateRef,
  ViewChild,
  inject,
  isDevMode
} from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';

import { ensureOverlayPolyfills, refreshAnchorPositioning } from '../../utils/polyfills.util';
import { MultiPickerKey, MultiPickerOption, MultiPickerOptionContext, MultiPickerSelection } from './multi-picker.models';

/** One row of the listbox. */
export interface MultiPickerEntry {
  option: MultiPickerOption;
  index: number;
  /** Lower-cased label, for type-ahead. */
  searchName: string;
}

/** A run of options sharing one `group` (or none). */
interface Section {
  group: string | null;
  headingId: string | null;
  items: MultiPickerEntry[];
}

type PendingFocus = { kind: 'chip'; key: MultiPickerKey } | { kind: 'trigger' };

const TYPEAHEAD_RESET_MS = 500;
const PAGE_STEP = 10;

let nextUid = 0;

/**
 * A generic multi-select picker implementing the WAI-ARIA *collapsible listbox* pattern with
 * `aria-multiselectable`: a button whose text summarizes the selection opens a popup
 * `role="listbox"`, which takes focus and tracks the active option with `aria-activedescendant`.
 * Toggles commit as they happen, so Escape and Tab close without undoing anything. Presentational:
 * the host owns the selection and feeds `selectedKeys` back in. The trigger and dropdown reuse the
 * `.custom-model-selector` look; `.gh-multi-picker` in `styles.scss` adds the check column, the
 * chip row and the forced-colors rules.
 */
@Component({
  selector: 'app-multi-picker',
  standalone: true,
  imports: [NgTemplateOutlet],
  templateUrl: './multi-picker.component.html',
  styleUrl: './multi-picker.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {
    'class': 'gh-multi-picker',
    '[class.drops-up]': 'dropsUp',
    '[class.gh-multi-picker--cards]': "chipStyle === 'card'"
  }
})
export class MultiPickerComponent implements OnInit, OnChanges, AfterViewChecked, OnDestroy {
  private host = inject<ElementRef<HTMLElement>>(ElementRef);
  private renderer = inject(Renderer2);
  private cdr = inject(ChangeDetectorRef);

  @Input() options: readonly MultiPickerOption[] = [];
  /** Compared with `===`; keys not among `options` are ignored. */
  @Input() selectedKeys: readonly MultiPickerKey[] = [];
  /** The fewest options that must stay selected. */
  @Input() min = 0;
  /** The most options that may be selected; `null` for no limit. */
  @Input() max: number | null = null;
  /** Id of the host's visible label. */
  @Input() labelledBy: string | null = null;
  /** Only for a picker with no visible label: rendered as a visually hidden label. */
  @Input() label: string | null = null;
  /** Id of the host's hint, carried on the trigger's `aria-describedby`. */
  @Input() describedBy: string | null = null;
  /** Muted trigger text while nothing is selected; `Select <summaryNoun>` when unset. */
  @Input() placeholder: string | null = null;
  /** Shown in the popup when `options` is empty. */
  @Input() emptyHint: string | null = null;
  /** What the options are, in the plural: `All 5 sources`, `3 of 5 sources`. */
  @Input() summaryNoun = 'items';
  @Input() chips: 'none' | 'selected' = 'selected';
  /** Chips shown before the row ends in `+N more`. */
  @Input() maxChips = 6;
  @Input() showAllNone = true;
  @Input() dropsUp = false;
  /** Replaces the default tag, label and detail rendering of an option; text only. */
  @Input() optionTemplate: TemplateRef<MultiPickerOptionContext> | null = null;
  /**
   * Rendered in place of a chip's tag, label and detail; non-interactive content only, the remove
   * button is the picker's.
   */
  @Input() chipTemplate: TemplateRef<MultiPickerOptionContext> | null = null;
  /** `card` draws each chip as a glass card in a grid that spans the host (`.gh-multi-picker--cards`). */
  @Input() chipStyle: 'pill' | 'card' = 'pill';

  /** Once per toggle, All, None or chip removal. */
  @Output() selectionChange = new EventEmitter<MultiPickerSelection>();

  @ViewChild('field', { static: true }) private fieldRef!: ElementRef<HTMLElement>;
  @ViewChild('trigger', { static: true }) private triggerRef!: ElementRef<HTMLButtonElement>;
  @ViewChild('listbox') private listboxRef?: ElementRef<HTMLElement>;

  readonly uid = `msp-${++nextUid}`;

  open = false;
  activeIndex = 0;
  flat: MultiPickerEntry[] = [];
  sections: Section[] = [];
  /** The selected entries, in option order. */
  selectedEntries: MultiPickerEntry[] = [];
  /** The live line's text. */
  announcement = '';

  private selectedSet = new Set<MultiPickerKey>();
  private indexByKey = new Map<MultiPickerKey, number>();
  private pendingFocus: PendingFocus | null = null;
  private anchorsChanged = false;
  private removeDocumentListener: (() => void) | null = null;
  private typeahead = '';
  private typeaheadTimer: ReturnType<typeof setTimeout> | null = null;

  get triggerId(): string { return `${this.uid}-trigger`; }

  get labelIdValue(): string | null {
    if (this.labelledBy) return this.labelledBy;
    return this.label ? `${this.uid}-label` : null;
  }

  get triggerLabelledBy(): string | null {
    return this.labelIdValue ? `${this.labelIdValue} ${this.triggerId}` : null;
  }

  get listboxId(): string { return `${this.uid}-listbox`; }

  get emptyHintId(): string { return `${this.uid}-empty`; }

  get activeOptionId(): string | null {
    return this.open && this.flat.length > 0 ? this.optionId(this.activeIndex) : null;
  }

  get placeholderText(): string { return this.placeholder ?? `Select ${this.summaryNoun}`; }

  /** The trigger text when more than one option is selected. */
  get summaryText(): string {
    const count = this.selectedEntries.length;
    const total = this.flat.length;
    return count === total ? `All ${total} ${this.summaryNoun}` : `${count} of ${total} ${this.summaryNoun}`;
  }

  /** The card-mode read-out beside All and None: `2 selected · max 12`. */
  get selectionCountText(): string {
    const count = this.selectedEntries.length;
    return this.max === null ? `${count} selected` : `${count} selected · max ${this.max}`;
  }

  get maxValue(): number { return this.max ?? Number.POSITIVE_INFINITY; }

  get shownChips(): MultiPickerEntry[] { return this.selectedEntries.slice(0, Math.max(0, this.maxChips)); }

  get hiddenChipCount(): number { return Math.max(0, this.selectedEntries.length - this.shownChips.length); }

  /** Why All cannot act, or null when it can. */
  get allBlockedReason(): string | null {
    if (this.flat.length === 0) return `There are no ${this.summaryNoun} to select.`;
    const next = this.allKeys();
    if (next.length === this.selectedEntries.length) return `All ${this.summaryNoun} are already selected.`;
    if (next.length > this.maxValue) return this.maxReason();
    return null;
  }

  /** Why None cannot act, or null when it can. */
  get noneBlockedReason(): string | null {
    const next = this.noneKeys();
    if (next.length === this.selectedEntries.length) return `No ${this.summaryNoun} are selected.`;
    if (next.length < this.min) return this.minReason();
    return null;
  }

  ngOnInit(): void {
    ensureOverlayPolyfills();
    if (isDevMode() && !this.labelledBy && !this.label) {
      console.warn('app-multi-picker: set labelledBy or label so the picker has an accessible name.');
    }
  }

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['options']) {
      this.rebuild();
    }
    if (changes['options'] || changes['selectedKeys']) {
      this.rebuildSelection();
    }
  }

  ngAfterViewChecked(): void {
    if (this.pendingFocus) {
      const pending = this.pendingFocus;
      this.pendingFocus = null;
      const index = pending.kind === 'chip' ? this.indexByKey.get(pending.key) : undefined;
      const chip = index === undefined
        ? null
        : this.host.nativeElement.querySelector<HTMLElement>(`#${this.chipRemoveId(index)}`);
      (chip ?? this.triggerRef.nativeElement).focus();
    }
    if (this.anchorsChanged) {
      this.anchorsChanged = false;
      refreshAnchorPositioning();
    }
  }

  ngOnDestroy(): void {
    this.detachDocumentListener();
    this.clearTypeahead();
  }

  optionId(index: number): string { return `${this.uid}-opt-${index}`; }

  chipRemoveId(index: number): string { return `${this.uid}-chip-${index}`; }

  isSelected(key: MultiPickerKey): boolean { return this.selectedSet.has(key); }

  /** Why a chip cannot be removed, or null when it can. */
  chipBlockedReason(entry: MultiPickerEntry): string | null {
    if (entry.option.disabledReason) return entry.option.disabledReason;
    if (this.selectedEntries.length <= this.min) return this.minReason();
    return null;
  }

  chipRemoveLabel(option: MultiPickerOption): string {
    return option.detail ? `Remove ${option.label} (${option.detail})` : `Remove ${option.label}`;
  }

  toggle(): void {
    if (this.open) {
      this.close(true);
    } else {
      this.openList(this.firstSelectedIndex() ?? 0);
    }
  }

  onTriggerKeydown(event: KeyboardEvent): void {
    switch (event.key) {
      case 'ArrowDown':
        event.preventDefault();
        if (!this.open) this.openList(this.firstSelectedIndex() ?? 0);
        break;
      case 'ArrowUp':
        event.preventDefault();
        if (!this.open) this.openList(this.firstSelectedIndex() ?? this.flat.length - 1);
        break;
    }
  }

  onListboxKeydown(event: KeyboardEvent): void {
    const last = this.flat.length - 1;
    switch (event.key) {
      case 'ArrowDown':
        if (event.shiftKey) {
          this.extend(1);
        } else {
          this.moveActive(this.activeIndex + 1);
        }
        break;
      case 'ArrowUp':
        if (event.shiftKey) {
          this.extend(-1);
        } else {
          this.moveActive(this.activeIndex - 1);
        }
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
        this.toggleIndex(this.activeIndex);
        this.close(true);
        break;
      case ' ':
        if (this.typeahead) {
          this.typeAhead(' ');
        } else {
          this.toggleIndex(this.activeIndex);
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
        if ((event.ctrlKey || event.metaKey) && !event.altKey && event.key.toLowerCase() === 'a') {
          this.toggleAll();
          break;
        }
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
    if (!next || !this.fieldRef.nativeElement.contains(next)) {
      this.close(false);
    }
  }

  onOptionClick(entry: MultiPickerEntry): void {
    this.activeIndex = entry.index;
    this.toggleIndex(entry.index);
    this.cdr.markForCheck();
  }

  selectAll(): void {
    const reason = this.allBlockedReason;
    if (reason) {
      this.announce(reason);
      return;
    }
    this.commit(this.allKeys(), `All ${this.summaryNoun} selected.`);
  }

  selectNone(): void {
    const reason = this.noneBlockedReason;
    if (reason) {
      this.announce(reason);
      return;
    }
    this.commit(this.noneKeys(), 'Selection cleared.');
  }

  removeChip(entry: MultiPickerEntry): void {
    const reason = this.chipBlockedReason(entry);
    if (reason) {
      this.announce(reason);
      return;
    }
    const position = this.selectedEntries.indexOf(entry);
    const next = this.selectedEntries[position + 1];
    this.pendingFocus = next ? { kind: 'chip', key: next.option.key } : { kind: 'trigger' };
    this.commit(this.keysWithout(entry.option.key), `${entry.option.label} removed.`);
  }

  /** `+N more` opens the list, where every selected option is shown. */
  showMore(): void {
    this.openList(this.firstSelectedIndex() ?? 0);
  }

  private toggleAll(): void {
    if (this.allKeys().length === this.selectedEntries.length) {
      this.selectNone();
    } else {
      this.selectAll();
    }
  }

  /** Moves to the next option in a direction and adds it to the selection; never removes. */
  private extend(delta: number): void {
    const target = this.clampIndex(this.activeIndex + delta);
    if (target === this.activeIndex) return;
    this.moveActive(target);
    if (!this.isSelected(this.flat[target].option.key)) {
      this.toggleIndex(target);
    }
  }

  private toggleIndex(index: number): void {
    const entry = this.flat[index];
    if (!entry) return;
    const { option } = entry;
    if (option.disabledReason) {
      this.announce(`${option.label} is unavailable. ${option.disabledReason}`);
      return;
    }
    if (this.isSelected(option.key)) {
      if (this.selectedEntries.length <= this.min) {
        this.announce(this.minReason());
        return;
      }
      this.commit(this.keysWithout(option.key), `${option.label} deselected.`);
    } else {
      if (this.selectedEntries.length >= this.maxValue) {
        this.announce(this.maxReason());
        return;
      }
      this.commit(this.keysWith(option.key), `${option.label} selected.`);
    }
  }

  private commit(keys: MultiPickerKey[], what: string): void {
    this.selectionChange.emit({ keys });
    this.announce(`${what} ${keys.length} of ${this.flat.length} selected.`);
  }

  /** A repeated message alternates a trailing no-break space, so the live region reads it again. */
  private announce(text: string): void {
    this.announcement = this.announcement === text ? `${text} ` : text;
    this.cdr.markForCheck();
  }

  private keysWith(key: MultiPickerKey): MultiPickerKey[] {
    return this.flat.filter(e => e.option.key === key || this.isSelected(e.option.key)).map(e => e.option.key);
  }

  private keysWithout(key: MultiPickerKey): MultiPickerKey[] {
    return this.selectedEntries.filter(e => e.option.key !== key).map(e => e.option.key);
  }

  /** Every enabled option, plus the disabled ones already selected. */
  private allKeys(): MultiPickerKey[] {
    return this.flat
      .filter(e => !e.option.disabledReason || this.isSelected(e.option.key))
      .map(e => e.option.key);
  }

  /** Only the disabled options already selected, which None cannot remove. */
  private noneKeys(): MultiPickerKey[] {
    return this.selectedEntries.filter(e => !!e.option.disabledReason).map(e => e.option.key);
  }

  private minReason(): string {
    return `At least ${this.min} ${this.noun(this.min)} must stay selected.`;
  }

  private maxReason(): string {
    return `At most ${this.max} ${this.noun(this.maxValue)} can be selected.`;
  }

  /** `1 source`, `2 sources`. */
  private noun(count: number): string {
    return count === 1 ? this.summaryNoun.replace(/s$/, '') : this.summaryNoun;
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
      if (!this.fieldRef.nativeElement.contains(event.target as Node)) {
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
   * Printable characters accumulate for 500 ms and match the start of a label. The search starts
   * after the active option, except that a longer buffer may keep the active one; a repeated
   * single character cycles through the labels that start with it.
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

  private firstSelectedIndex(): number | null {
    return this.selectedEntries[0]?.index ?? null;
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
    const flat: MultiPickerEntry[] = [];
    const sections: Section[] = [];
    const indexByKey = new Map<MultiPickerKey, number>();
    let current: Section | null = null;
    let groupCount = 0;
    this.options.forEach((option, index) => {
      const entry: MultiPickerEntry = { option, index, searchName: option.label.toLowerCase() };
      flat.push(entry);
      indexByKey.set(option.key, index);
      const group = option.group ?? null;
      if (!current || current.group !== group) {
        current = { group, headingId: group ? `${this.uid}-grp-${++groupCount}` : null, items: [] };
        sections.push(current);
      }
      current.items.push(entry);
    });
    this.flat = flat;
    this.sections = sections;
    this.indexByKey = indexByKey;
    this.activeIndex = flat.length > 0 ? this.clampIndex(this.activeIndex) : 0;
  }

  private rebuildSelection(): void {
    this.selectedSet = new Set(this.selectedKeys);
    this.selectedEntries = this.flat.filter(e => this.selectedSet.has(e.option.key));
    this.anchorsChanged = true;
  }
}
