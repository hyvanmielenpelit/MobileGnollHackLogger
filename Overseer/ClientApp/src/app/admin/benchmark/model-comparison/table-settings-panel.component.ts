/**
 * The Table tab of the comparison sidebar: which columns the table shows and in what order, and
 * the table image's row style.
 *
 * Presentational, like `ExportSizeSectionComponent` beside it: the host owns `columns` and
 * `tableStyle`, this component only renders them and the two quick-action rows, and every change
 * goes out as a whole new value. `catalog` and `empty` are what turns a bare key into a labelled,
 * tagged row — `catalog` for the column's header, renderer and parts, `empty` for which keys have
 * nothing to show under the entries currently on screen.
 */

import { ChangeDetectionStrategy, Component, EventEmitter, Input, OnChanges, OnInit, SimpleChanges, Output } from '@angular/core';

import { ReorderableListComponent, ReorderableListItem } from '../../../shared/reorderable-list/reorderable-list.component';
import { DEFAULT_TABLE_IMAGE_STYLE, TableImageStyle } from './figure-style';
import {
  DEFAULT_TABLE_COLUMNS,
  TABLE_DISPLAY_COLUMNS,
  TABLE_MODEL_COLUMN_KEY,
  TableColumnConfig,
  TableDisplayColumn,
  sameTableColumnConfig
} from './table-export';
import { ensureOverlayPolyfills } from '../../../utils/polyfills.util';

/** Where the two sections' open state is kept, per browser. Read and written in `try/catch`. */
export const TABLE_SETTINGS_OPEN_KEY = 'overseer.modelComparison.tableSettingsOpen';

interface StoredTableSettingsOpen {
  readonly columns: boolean;
  readonly imageLayout: boolean;
}

/** Columns open by default: it is the section an admin reaches for first. */
const DEFAULT_OPEN: StoredTableSettingsOpen = { columns: true, imageLayout: false };

function readStoredOpen(): StoredTableSettingsOpen {
  try {
    const raw = localStorage.getItem(TABLE_SETTINGS_OPEN_KEY);
    if (raw === null) {
      return DEFAULT_OPEN;
    }
    const parsed = JSON.parse(raw) as Record<string, unknown>;
    return {
      columns: typeof parsed['columns'] === 'boolean' ? parsed['columns'] : DEFAULT_OPEN.columns,
      imageLayout: typeof parsed['imageLayout'] === 'boolean' ? parsed['imageLayout'] : DEFAULT_OPEN.imageLayout
    };
  } catch {
    return DEFAULT_OPEN;
  }
}

function writeStoredOpen(value: StoredTableSettingsOpen): void {
  try {
    localStorage.setItem(TABLE_SETTINGS_OPEN_KEY, JSON.stringify(value));
  } catch {
    // Private mode or blocked storage: the open state still applies for this session.
  }
}

@Component({
  selector: 'app-table-settings-panel',
  standalone: true,
  imports: [ReorderableListComponent],
  templateUrl: './table-settings-panel.component.html',
  styleUrls: ['./table-settings-panel.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class TableSettingsPanelComponent implements OnChanges, OnInit {
  @Input() columns: TableColumnConfig = DEFAULT_TABLE_COLUMNS;
  @Input() catalog: readonly TableDisplayColumn[] = TABLE_DISPLAY_COLUMNS;
  @Input() empty: ReadonlySet<string> = new Set();
  @Input() tableStyle: TableImageStyle = DEFAULT_TABLE_IMAGE_STYLE;

  @Output() readonly columnsChange = new EventEmitter<TableColumnConfig>();
  @Output() readonly tableStyleChange = new EventEmitter<TableImageStyle>();

  private readonly storedOpen = readStoredOpen();
  columnsOpen = this.storedOpen.columns;
  imageLayoutOpen = this.storedOpen.imageLayout;

  columnsResetStatus = '';
  imageLayoutResetStatus = '';
  private pendingColumnsResetAck = false;
  private pendingImageLayoutResetAck = false;

  ngOnInit(): void {
    ensureOverlayPolyfills();
  }

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['columns'] && !changes['columns'].firstChange) {
      if (this.pendingColumnsResetAck) {
        this.pendingColumnsResetAck = false;
      } else {
        this.columnsResetStatus = '';
      }
    }
    if (changes['tableStyle'] && !changes['tableStyle'].firstChange) {
      if (this.pendingImageLayoutResetAck) {
        this.pendingImageLayoutResetAck = false;
      } else {
        this.imageLayoutResetStatus = '';
      }
    }
  }

  private get catalogByKey(): ReadonlyMap<string, TableDisplayColumn> {
    return new Map(this.catalog.map(column => [column.key, column]));
  }

  /** Every display column in the configuration's order, as the reorderable list renders it. */
  get items(): ReorderableListItem[] {
    const shownSet = new Set(this.columns.shown);
    const byKey = this.catalogByKey;
    return this.columns.order
      .map(key => byKey.get(key))
      .filter((column): column is TableDisplayColumn => column !== undefined)
      .map(column => ({
        key: column.key,
        label: column.header,
        tags: this.tagsFor(column, shownSet),
        checked: shownSet.has(column.key),
        locked: column.key === TABLE_MODEL_COLUMN_KEY,
        lockedReason: column.key === TABLE_MODEL_COLUMN_KEY ? 'Every table needs its model column.' : undefined
      }));
  }

  private tagsFor(column: TableDisplayColumn, shownSet: ReadonlySet<string>): string[] {
    const tags: string[] = [];
    if (this.empty.has(column.key)) {
      tags.push('empty');
    }
    // Once a part is shown as its own column, the no-duplication rule (table-export.ts's
    // `readingCellText`) drops it from the combined cell, so the tag would misdescribe it too.
    if (!shownSet.has(column.key)) {
      const combined = this.combinedColumnContaining(column.key, shownSet);
      if (combined) {
        tags.push(`in ${combined.header}`);
      }
    }
    if (column.key === TABLE_MODEL_COLUMN_KEY) {
      tags.push('always shown');
    }
    return tags;
  }

  /** The shown combined column, if any, whose parts still fold this part into their cell. */
  private combinedColumnContaining(key: string, shownSet: ReadonlySet<string>): TableDisplayColumn | undefined {
    return this.catalog.find(
      column => column.renderer !== 'text' && shownSet.has(column.key) && column.parts.includes(key)
    );
  }

  get shownCount(): number {
    return this.columns.order.filter(key => this.columns.shown.includes(key)).length;
  }

  get totalCount(): number {
    return this.catalog.length;
  }

  get columnsAreDefault(): boolean {
    return sameTableColumnConfig(this.columns, DEFAULT_TABLE_COLUMNS);
  }

  get columnsReadout(): string {
    return `${this.shownCount} of ${this.totalCount} · ${this.columnsAreDefault ? 'default' : 'custom'}`;
  }

  get columnsCountStatus(): string {
    return `${this.shownCount} of ${this.totalCount} columns shown`;
  }

  onOrderChange(order: string[]): void {
    this.columnsResetStatus = '';
    this.emitColumns({ order, shown: this.columns.shown });
  }

  onCheckedChange(event: { key: string; checked: boolean }): void {
    if (event.key === TABLE_MODEL_COLUMN_KEY && !event.checked) {
      return; // Model cannot be hidden; the reorderable list already disables its checkbox.
    }
    const shown = new Set(this.columns.shown);
    if (event.checked) {
      shown.add(event.key);
    } else {
      shown.delete(event.key);
    }
    this.columnsResetStatus = '';
    this.emitColumns({ order: this.columns.order, shown: this.columns.order.filter(key => shown.has(key)) });
  }

  useDefaultColumns(): void {
    this.columnsResetStatus = '';
    this.emitColumns(DEFAULT_TABLE_COLUMNS);
  }

  useColumnsWithValues(): void {
    const shown = this.columns.order.filter(key => key === TABLE_MODEL_COLUMN_KEY || !this.empty.has(key));
    this.columnsResetStatus = '';
    this.emitColumns({ order: this.columns.order, shown });
  }

  useAllColumns(): void {
    this.columnsResetStatus = '';
    this.emitColumns({ order: this.columns.order, shown: [...this.columns.order] });
  }

  /** Same effect as *Default columns*, plus the reset button's own status announcement. */
  resetColumns(): void {
    if (this.columnsAreDefault) {
      return;
    }
    this.columnsResetStatus = 'Columns reset to defaults.';
    this.pendingColumnsResetAck = true;
    this.emitColumns(DEFAULT_TABLE_COLUMNS);
  }

  private emitColumns(config: TableColumnConfig): void {
    this.columnsChange.emit(config);
  }

  get imageLayoutIsDefault(): boolean {
    return this.tableStyle.rowBands === DEFAULT_TABLE_IMAGE_STYLE.rowBands
      && this.tableStyle.rowRules === DEFAULT_TABLE_IMAGE_STYLE.rowRules;
  }

  get imageLayoutReadout(): string {
    return `${this.tableStyle.rowBands ? 'bands' : 'no bands'} · ${this.tableStyle.rowRules ? 'rules' : 'no rules'}`;
  }

  onRowBandsChange(checked: boolean): void {
    this.imageLayoutResetStatus = '';
    this.emitTableStyle({ ...this.tableStyle, rowBands: checked });
  }

  onRowRulesChange(checked: boolean): void {
    this.imageLayoutResetStatus = '';
    this.emitTableStyle({ ...this.tableStyle, rowRules: checked });
  }

  resetImageLayout(): void {
    if (this.imageLayoutIsDefault) {
      return;
    }
    this.imageLayoutResetStatus = 'Image layout reset to defaults.';
    this.pendingImageLayoutResetAck = true;
    this.emitTableStyle(DEFAULT_TABLE_IMAGE_STYLE);
  }

  private emitTableStyle(style: TableImageStyle): void {
    this.tableStyleChange.emit(style);
  }

  onColumnsToggle(event: Event): void {
    const open = (event.target as HTMLDetailsElement).open;
    if (open === this.columnsOpen) {
      return;
    }
    this.columnsOpen = open;
    writeStoredOpen({ columns: this.columnsOpen, imageLayout: this.imageLayoutOpen });
  }

  onImageLayoutToggle(event: Event): void {
    const open = (event.target as HTMLDetailsElement).open;
    if (open === this.imageLayoutOpen) {
      return;
    }
    this.imageLayoutOpen = open;
    writeStoredOpen({ columns: this.columnsOpen, imageLayout: this.imageLayoutOpen });
  }
}
