import { ChangeDetectionStrategy, ChangeDetectorRef, Component, ElementRef, OnInit, inject, input, output } from '@angular/core';
import { ensureOverlayPolyfills, refreshAnchorPositioning } from '../../../utils/polyfills.util';
import { modelUserDirectionLabel } from '../model-users/telemetry-model-users';

export type TelemetrySortKind = 'number' | 'text' | 'date';

export type TelemetrySortDirection = 'desc' | 'asc';

/** One field of the Sort by group: its value, its label and how its order words read. */
export interface TelemetrySortFieldInfo<F extends string = string> {
  field: F;
  label: string;
  kind: TelemetrySortKind;
}

export interface TelemetrySort<F extends string = string> {
  field: F;
  direction: TelemetrySortDirection;
}

/** The field's info; the first field when the list does not hold it. */
function sortFieldInfo(fields: readonly TelemetrySortFieldInfo[], field: string): TelemetrySortFieldInfo | undefined {
  return fields.find(f => f.field === field) ?? fields[0];
}

/** `Est. cost, highest first`; `User name, A–Z`; `Last active, newest first`. */
export function telemetrySortLabel(fields: readonly TelemetrySortFieldInfo[], sort: TelemetrySort): string {
  const info = sortFieldInfo(fields, sort.field);
  if (!info) {
    return '';
  }
  const direction = modelUserDirectionLabel(info.kind, sort.direction);
  return `${info.label}, ${info.kind === 'text' ? direction : direction.toLowerCase()}`;
}

/**
 * The AI Telemetry Sort popover: a `.btn-ghost` *Sort: {field}, {order}* trigger opening a
 * `popover="auto"` panel with a *Sort by* group of radios, the *Order* segmented radiogroup and
 * **Reset sorting**. Every change applies at once through `sortChange` and the panel stays open; the
 * host owns the sort and feeds it back.
 */
@Component({
  selector: 'app-telemetry-sort-popover',
  templateUrl: './telemetry-sort-popover.component.html',
  styleUrl: './telemetry-sort-popover.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class TelemetrySortPopoverComponent implements OnInit {
  private readonly host: ElementRef<HTMLElement> = inject(ElementRef);
  private readonly cdr = inject(ChangeDetectorRef);

  /** Unique in the document; every id and the anchor name derive from it. */
  readonly idPrefix = input.required<string>();
  /** The Sort by fields, in the panel's order. */
  readonly fields = input.required<readonly TelemetrySortFieldInfo[]>();
  readonly sort = input.required<TelemetrySort>();
  /** What Reset sorting returns to; the button is aria-disabled while the sort is this one. */
  readonly defaultSort = input.required<TelemetrySort>();

  /** A field, an order or Reset sorting was chosen. */
  readonly sortChange = output<TelemetrySort>();

  readonly sortDirections: readonly TelemetrySortDirection[] = ['desc', 'asc'];

  /** Whether the panel is open; the popover polyfill does not set `aria-expanded`. */
  sortOpen = false;

  ngOnInit(): void {
    ensureOverlayPolyfills();
  }

  /** `Est. cost, highest first`. */
  get sortLabel(): string {
    return telemetrySortLabel(this.fields(), this.sort());
  }

  get sortIsDefault(): boolean {
    const sort = this.sort();
    const fallback = this.defaultSort();
    return sort.field === fallback.field && sort.direction === fallback.direction;
  }

  /** `Highest first`, `Newest first` or `A–Z`, by the kind of the chosen field. */
  directionLabel(direction: TelemetrySortDirection): string {
    const info = sortFieldInfo(this.fields(), this.sort().field);
    return modelUserDirectionLabel(info?.kind ?? 'number', direction);
  }

  /** Applies at once and keeps the direction; the popover stays open. */
  chooseSortField(field: string): void {
    const sort = this.sort();
    if (field !== sort.field) {
      this.sortChange.emit({ field, direction: sort.direction });
    }
  }

  chooseSortDirection(direction: TelemetrySortDirection): void {
    const sort = this.sort();
    if (direction !== sort.direction) {
      this.sortChange.emit({ field: sort.field, direction });
    }
  }

  /** Back to the default; refused at the default (the button is aria-disabled), and the button keeps focus. */
  resetSort(): void {
    if (!this.sortIsDefault) {
      const fallback = this.defaultSort();
      this.sortChange.emit({ field: fallback.field, direction: fallback.direction });
    }
  }

  onSortToggle(event: Event): void {
    this.sortOpen = (event as ToggleEvent).newState === 'open';
    this.cdr.markForCheck();
    const panel = this.sortPanel();
    if (this.sortOpen) {
      refreshAnchorPositioning();
      (panel?.querySelector<HTMLInputElement>('input:checked') ?? panel?.querySelector<HTMLInputElement>('input'))?.focus();
      return;
    }
    const active = document.activeElement;
    if (!active || active === document.body || !!panel?.contains(active)) {
      this.sortTrigger()?.focus();
    }
  }

  /** Escape closes the Sort popover only. */
  onSortPanelKeydown(event: KeyboardEvent): void {
    if (event.key !== 'Escape') return;
    event.preventDefault();
    event.stopPropagation();
    try {
      this.sortPanel()?.hidePopover();
    } catch {
      // Already hidden.
    }
    this.sortTrigger()?.focus();
  }

  private sortPanel(): HTMLElement | null {
    return this.host.nativeElement.querySelector<HTMLElement>('.tl-sort-panel');
  }

  private sortTrigger(): HTMLElement | null {
    return this.host.nativeElement.querySelector<HTMLElement>('.tl-sort-trigger');
  }
}
