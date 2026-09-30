import {
  ChangeDetectionStrategy,
  ChangeDetectorRef,
  Component,
  ElementRef,
  EventEmitter,
  Input,
  OnInit,
  Output,
  inject
} from '@angular/core';

import { ensureOverlayPolyfills, refreshAnchorPositioning } from '../../utils/polyfills.util';

/** One choice of a facet: its value, what it reads, and how many rows it would leave. */
export interface FilterFacetOption {
  value: string;
  label: string;
  count: number;
}

/** Above this many options the panel adds a field that narrows the options shown. */
export const FILTER_FACET_SEARCH_THRESHOLD = 10;

/**
 * A faceted filter: a pill trigger that opens a popover group of checkboxes (`multiple`) or radios
 * (`single`, with `anyLabel` first), each option with the number of rows it would leave.
 *
 * It emits values and never owns the selection: the host sets the filter and feeds `selected`
 * back. Every element id derives from `facetId`, which must be unique in the document. Escape
 * closes the popover only, so a dialog around the facet stays open.
 */
@Component({
  selector: 'app-filter-facet',
  standalone: true,
  templateUrl: './filter-facet.component.html',
  styleUrls: ['./filter-facet.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class FilterFacetComponent implements OnInit {
  private readonly cdr = inject(ChangeDetectorRef);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);

  /** Unique in the document; the trigger, the popover, the options and the anchor name derive from it. */
  @Input({ required: true }) facetId!: string;
  /** The facet's name on the trigger: `Document`. */
  @Input({ required: true }) label!: string;
  @Input() options: readonly FilterFacetOption[] = [];
  @Input() selected: readonly string[] = [];
  @Input() mode: 'multiple' | 'single' = 'multiple';
  /** The single mode's first option, which clears the filter: `Any time`. */
  @Input() anyLabel = 'Any';
  /** What the counts count, in the plural; the hidden text after each count. */
  @Input() noun = 'documents';

  @Output() readonly selectedChange = new EventEmitter<string[]>();

  /** Whether the popover is open; the popover polyfill does not set `aria-expanded`. */
  open = false;
  /** The option search's text. */
  optionQuery = '';

  ngOnInit(): void {
    ensureOverlayPolyfills();
  }

  get popoverId(): string {
    return `${this.facetId}-popover`;
  }

  get hasOptionSearch(): boolean {
    return this.options.length > FILTER_FACET_SEARCH_THRESHOLD;
  }

  /** The options the option search lets through, each with its index in `options` for its id. */
  get shownOptions(): { option: FilterFacetOption; index: number }[] {
    const query = this.hasOptionSearch ? this.optionQuery.trim().toLowerCase() : '';
    return this.options
      .map((option, index) => ({ option, index }))
      .filter(entry => query === '' || entry.option.label.toLowerCase().includes(query));
  }

  isSelected(value: string): boolean {
    return this.selected.includes(value);
  }

  /** `1 document`, `4 documents`. */
  countNoun(count: number): string {
    return count === 1 ? this.noun.replace(/s$/, '') : this.noun;
  }

  toggleValue(value: string, event: Event): void {
    const checked = (event.target as HTMLInputElement).checked;
    const next = this.selected.filter(v => v !== value);
    if (checked) {
      next.push(value);
    }
    this.selectedChange.emit(next);
  }

  choose(value: string | null): void {
    this.selectedChange.emit(value === null ? [] : [value]);
  }

  clear(): void {
    this.selectedChange.emit([]);
    this.focusFirst();
  }

  onQuery(event: Event): void {
    this.optionQuery = (event.target as HTMLInputElement).value;
    this.cdr.markForCheck();
  }

  onToggle(event: Event): void {
    this.open = (event as ToggleEvent).newState === 'open';
    this.cdr.markForCheck();
    if (this.open) {
      refreshAnchorPositioning();
      this.cdr.detectChanges();
      this.focusFirst();
      return;
    }
    this.optionQuery = '';
    const active = document.activeElement;
    if (!active || active === document.body || !!this.popover()?.contains(active)) {
      this.trigger()?.focus();
    }
  }

  /** Escape closes the popover only; a dialog around the facet stays open. */
  onKeydown(event: KeyboardEvent): void {
    if (event.key !== 'Escape') {
      return;
    }
    event.preventDefault();
    event.stopPropagation();
    try {
      this.popover()?.hidePopover();
    } catch {
      // Already hidden.
    }
    this.trigger()?.focus();
  }

  /** The option search where there is one, else the first option. */
  private focusFirst(): void {
    const popover = this.popover();
    const target = popover?.querySelector<HTMLElement>('.gh-facet-search')
      ?? popover?.querySelector<HTMLElement>('.gh-facet-option input');
    target?.focus();
  }

  private popover(): HTMLElement | null {
    return this.host.nativeElement.querySelector<HTMLElement>(`[id="${this.popoverId}"]`);
  }

  private trigger(): HTMLElement | null {
    return this.host.nativeElement.querySelector<HTMLElement>(`[id="${this.facetId}-trigger"]`);
  }
}
