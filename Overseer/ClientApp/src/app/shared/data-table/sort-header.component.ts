import { Component, EventEmitter, Input, Output } from '@angular/core';
import { TableState } from './table-state';

/**
 * A sortable table header cell.
 *
 * The component's host element *is* the `<th>` — hence the attribute selector — so
 * `aria-sort` sits on the cell itself, where assistive technology looks for it, with no
 * wrapper element between the row and the cell. The attribute is the single source of truth
 * for the sorted state: the caret is drawn from `th[aria-sort="…"]` in the global stylesheet,
 * so there is no parallel `.active` class that could disagree with what is announced.
 *
 * The label is a real `<button>`, which makes the header operable by keyboard and Enter/Space
 * come free.
 */
@Component({
  selector: 'th[app-sort-header]',
  standalone: true,
  host: {
    'scope': 'col',
    'class': 'gh-th-sortable',
    '[attr.aria-sort]': 'state.ariaSort(column)'
  },
  template: `
    <button type="button" class="gh-th-sort" (click)="onSort()">
      <span class="gh-th-label">{{ label }}</span>
      <svg class="gh-sort-caret" xmlns="http://www.w3.org/2000/svg" width="12" height="12"
           viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"
           stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">
        <polyline points="6 9 12 15 18 9"></polyline>
      </svg>
    </button>
  `
})
export class SortHeaderComponent {
  /** The table state this header sorts. Shared with the table's pager and filter row. */
  @Input({ required: true }) state!: TableState<any>;

  /** Key of the sort accessor registered for this column. */
  @Input({ required: true }) column!: string;

  /** Visible header text. */
  @Input() label = '';

  /** Fires after the sort has changed, for a host that must re-render or persist the state. */
  @Output() changed = new EventEmitter<void>();

  onSort(): void {
    this.state.toggleSort(this.column);
    this.changed.emit();
  }
}
