import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';

import { TABLE_SETTINGS_OPEN_KEY, TableSettingsPanelComponent } from './table-settings-panel.component';
import { ReorderableListComponent } from '../../../shared/reorderable-list/reorderable-list.component';
import { DEFAULT_TABLE_IMAGE_STYLE, TableImageStyle } from './figure-style';
import { DEFAULT_TABLE_COLUMNS, TABLE_MODEL_COLUMN_KEY, TableColumnConfig } from './table-export';

describe('TableSettingsPanelComponent', () => {
  let fixture: ComponentFixture<TableSettingsPanelComponent>;
  let emittedColumns: TableColumnConfig[];
  let emittedStyle: TableImageStyle[];

  function host(): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  function control<T extends HTMLElement>(id: string): T {
    const element = host().querySelector<T>(`#${id}`);
    expect(element).withContext(id).not.toBeNull();
    return element!;
  }

  function reorderableList(): ReorderableListComponent {
    return fixture.debugElement.query(By.directive(ReorderableListComponent)).componentInstance as ReorderableListComponent;
  }

  function render(columns: TableColumnConfig = DEFAULT_TABLE_COLUMNS, style: TableImageStyle = DEFAULT_TABLE_IMAGE_STYLE, empty: ReadonlySet<string> = new Set()): void {
    fixture.componentRef.setInput('columns', columns);
    fixture.componentRef.setInput('tableStyle', style);
    fixture.componentRef.setInput('empty', empty);
    fixture.detectChanges();
  }

  /** Feeds the last emitted columns back in, the way the wizard applies a change. */
  function acceptLastColumns(): void {
    fixture.componentRef.setInput('columns', emittedColumns[emittedColumns.length - 1]);
    fixture.detectChanges();
  }

  function acceptLastStyle(): void {
    fixture.componentRef.setInput('tableStyle', emittedStyle[emittedStyle.length - 1]);
    fixture.detectChanges();
  }

  function setChecked(input: HTMLInputElement, on: boolean): void {
    input.checked = on;
    input.dispatchEvent(new Event('change'));
    fixture.detectChanges();
  }

  beforeEach(async () => {
    localStorage.removeItem(TABLE_SETTINGS_OPEN_KEY);
    await TestBed.configureTestingModule({ imports: [TableSettingsPanelComponent] }).compileComponents();
    fixture = TestBed.createComponent(TableSettingsPanelComponent);
    emittedColumns = [];
    emittedStyle = [];
    fixture.componentInstance.columnsChange.subscribe(config => emittedColumns.push(config));
    fixture.componentInstance.tableStyleChange.subscribe(style => emittedStyle.push(style));
  });

  afterEach(() => {
    localStorage.removeItem(TABLE_SETTINGS_OPEN_KEY);
  });

  it('lists every column of the configuration, in order, with the shown ones checked', () => {
    render();
    const items = fixture.componentInstance.items;
    expect(items.map(item => item.key)).toEqual(DEFAULT_TABLE_COLUMNS.order);
    expect(items.filter(item => item.checked).map(item => item.key)).toEqual(DEFAULT_TABLE_COLUMNS.shown);
  });

  it('locks the model column with a reason and tags it always shown', () => {
    render();
    const model = fixture.componentInstance.items.find(item => item.key === TABLE_MODEL_COLUMN_KEY)!;
    expect(model.locked).toBeTrue();
    expect(model.lockedReason).toBe('Every table needs its model column.');
    expect(model.tags).toContain('always shown');
  });

  it('tags a column empty when its key is in the empty set', () => {
    render(DEFAULT_TABLE_COLUMNS, DEFAULT_TABLE_IMAGE_STYLE, new Set(['runs']));
    const runs = fixture.componentInstance.items.find(item => item.key === 'runs')!;
    expect(runs.tags).toContain('empty');
  });

  it('tags a part column with the shown combined column that already includes it', () => {
    // intervalHalfWidth ("±") is a part of the shown "intelligence" combined column by default.
    render();
    const halfWidth = fixture.componentInstance.items.find(item => item.key === 'intervalHalfWidth')!;
    expect(halfWidth.tags).toContain('in Intelligence Index');
  });

  it('drops the "in <column>" tag once the part becomes its own shown column', () => {
    const config: TableColumnConfig = { order: DEFAULT_TABLE_COLUMNS.order, shown: [...DEFAULT_TABLE_COLUMNS.shown, 'intervalHalfWidth'] };
    render(config);
    const halfWidth = fixture.componentInstance.items.find(item => item.key === 'intervalHalfWidth')!;
    expect(halfWidth.tags).not.toContain('in Intelligence Index');
  });

  it('reorders through the reorderable list\'s orderChange', () => {
    render();
    const reordered = [...DEFAULT_TABLE_COLUMNS.order].reverse();
    reorderableList().orderChange.emit(reordered);
    expect(emittedColumns.length).toBe(1);
    expect(emittedColumns[0].order).toEqual(reordered);
    expect(emittedColumns[0].shown).toEqual(DEFAULT_TABLE_COLUMNS.shown);
  });

  it('checks and unchecks a column through checkedChange, keeping order', () => {
    render();
    reorderableList().checkedChange.emit({ key: 'intervalHalfWidth', checked: true });
    expect(emittedColumns[0].shown).toContain('intervalHalfWidth');
    acceptLastColumns();

    reorderableList().checkedChange.emit({ key: 'intervalHalfWidth', checked: false });
    expect(emittedColumns[1].shown).not.toContain('intervalHalfWidth');
  });

  it('never removes the model column even if asked to', () => {
    render();
    reorderableList().checkedChange.emit({ key: TABLE_MODEL_COLUMN_KEY, checked: false });
    expect(emittedColumns.length).toBe(0);
  });

  it('restores the default columns, keeps order for "with values", and shows all with "All columns"', () => {
    const customOrder = [...DEFAULT_TABLE_COLUMNS.order];
    const config: TableColumnConfig = { order: customOrder, shown: ['model'] };
    render(config, DEFAULT_TABLE_IMAGE_STYLE, new Set(['runs', 'stateCol']));

    fixture.componentInstance.useColumnsWithValues();
    expect(emittedColumns[0].order).toEqual(customOrder);
    expect(emittedColumns[0].shown).not.toContain('runs');
    expect(emittedColumns[0].shown).toContain('model');

    fixture.componentInstance.useAllColumns();
    expect(emittedColumns[1].shown).toEqual(customOrder);

    fixture.componentInstance.useDefaultColumns();
    expect(emittedColumns[2]).toEqual(DEFAULT_TABLE_COLUMNS);
  });

  it('reports the shown count, total and default/custom read-out', () => {
    render();
    expect(fixture.componentInstance.columnsCountStatus).toBe(`${DEFAULT_TABLE_COLUMNS.shown.length} of ${DEFAULT_TABLE_COLUMNS.order.length} columns shown`);
    expect(fixture.componentInstance.columnsReadout).toBe(`${DEFAULT_TABLE_COLUMNS.shown.length} of ${DEFAULT_TABLE_COLUMNS.order.length} · default`);

    fixture.componentInstance.useAllColumns();
    acceptLastColumns();
    expect(fixture.componentInstance.columnsReadout).toContain('· custom');
  });

  it('disables the columns reset at defaults, resets and announces it', () => {
    render();
    const reset = control<HTMLButtonElement>('mc-table-columns-reset');
    expect(reset.getAttribute('aria-disabled')).toBe('true');

    fixture.componentInstance.useAllColumns();
    acceptLastColumns();
    fixture.detectChanges();
    expect(control<HTMLButtonElement>('mc-table-columns-reset').getAttribute('aria-disabled')).toBeNull();

    fixture.componentInstance.resetColumns();
    expect(emittedColumns[emittedColumns.length - 1]).toEqual(DEFAULT_TABLE_COLUMNS);
    expect(fixture.componentInstance.columnsResetStatus).toBe('Columns reset to defaults.');

    // The host applying the reset must not clear the just-announced status.
    acceptLastColumns();
    expect(fixture.componentInstance.columnsResetStatus).toBe('Columns reset to defaults.');

    // A later, distinct change does clear it.
    fixture.componentInstance.useAllColumns();
    acceptLastColumns();
    expect(fixture.componentInstance.columnsResetStatus).toBe('');
  });

  it('chooses the shading level and the lines between rows, and reads them out', () => {
    render();
    expect(fixture.componentInstance.imageLayoutReadout).toBe('medium shading');

    setChecked(control<HTMLInputElement>('mc-table-image-layout-rowShading-strong'), true);
    expect(emittedStyle[0]).toEqual({ rowShading: 'strong', rowRules: false });
    acceptLastStyle();
    expect(fixture.componentInstance.imageLayoutReadout).toBe('strong shading');

    setChecked(control<HTMLInputElement>('mc-table-image-layout-rowRules'), true);
    expect(emittedStyle[1]).toEqual({ rowShading: 'strong', rowRules: true });
    acceptLastStyle();
    expect(fixture.componentInstance.imageLayoutReadout).toBe('strong shading + lines');

    setChecked(control<HTMLInputElement>('mc-table-image-layout-rowShading-none'), true);
    expect(emittedStyle[2]).toEqual({ rowShading: 'none', rowRules: true });
    acceptLastStyle();
    expect(fixture.componentInstance.imageLayoutReadout).toBe('lines');

    setChecked(control<HTMLInputElement>('mc-table-image-layout-rowRules'), false);
    acceptLastStyle();
    expect(fixture.componentInstance.imageLayoutReadout).toBe('none');
  });

  it('labels the row style controls and describes each with an info tip', () => {
    render();
    const component = fixture.componentInstance;
    const legend = control<HTMLElement>('mc-table-image-layout-rowShading-label');
    expect(legend.textContent!.trim()).toBe('Shade alternate rows');

    const radios = Array.from(host().querySelectorAll<HTMLInputElement>('input[name="mc-table-image-layout-rowShading"]'));
    expect(radios.map(radio => radio.closest('label')!.textContent!.trim())).toEqual(['None', 'Light', 'Medium', 'Strong']);
    expect(radios.filter(radio => radio.checked).map(radio => radio.value)).toEqual(['medium']);

    const rules = control<HTMLInputElement>('mc-table-image-layout-rowRules');
    expect(rules.closest('label')!.textContent!.trim()).toBe('Lines between rows');

    const fieldset = legend.closest('fieldset')!;
    const described: [HTMLElement, string][] = [[fieldset, component.rowShadingHint], [rules, component.rowRulesHint]];
    for (const [element, hint] of described) {
      const tipId = element.getAttribute('aria-describedby')!;
      expect(control<HTMLElement>(tipId).textContent!.trim()).withContext(tipId).toBe(hint);
    }

    render(DEFAULT_TABLE_COLUMNS, { rowShading: 'light', rowRules: true });
    expect(radios.filter(radio => radio.checked).map(radio => radio.value)).toEqual(['light']);
    expect(rules.checked).toBeTrue();
  });

  it('disables the row style reset at defaults and resets it', () => {
    render();
    const reset = control<HTMLButtonElement>('mc-table-image-layout-reset');
    expect(reset.getAttribute('aria-disabled')).toBe('true');
    expect(reset.getAttribute('aria-label')).toBe('Reset Row style to defaults');

    setChecked(control<HTMLInputElement>('mc-table-image-layout-rowRules'), true);
    acceptLastStyle();
    expect(control<HTMLButtonElement>('mc-table-image-layout-reset').getAttribute('aria-disabled')).toBeNull();

    fixture.componentInstance.resetImageLayout();
    expect(emittedStyle[emittedStyle.length - 1]).toEqual(DEFAULT_TABLE_IMAGE_STYLE);
    expect(fixture.componentInstance.imageLayoutResetStatus).toBe('Row style reset to defaults.');
  });

  it('opens the Columns section by default and persists an explicit toggle', () => {
    render();
    expect(control<HTMLDetailsElement>('mc-table-columns-section').open).toBeTrue();
    expect(control<HTMLDetailsElement>('mc-table-image-layout-section').open).toBeFalse();

    const imageSection = control<HTMLDetailsElement>('mc-table-image-layout-section');
    imageSection.open = true;
    imageSection.dispatchEvent(new Event('toggle'));
    fixture.detectChanges();

    const stored = JSON.parse(localStorage.getItem(TABLE_SETTINGS_OPEN_KEY)!);
    expect(stored).toEqual({ columns: true, imageLayout: true });
  });

  it('restores the stored open state on the next render', () => {
    localStorage.setItem(TABLE_SETTINGS_OPEN_KEY, JSON.stringify({ columns: false, imageLayout: true }));
    // The open state is read when the component is created, so this one is created after the write.
    fixture = TestBed.createComponent(TableSettingsPanelComponent);
    render();
    expect(control<HTMLDetailsElement>('mc-table-columns-section').open).toBeFalse();
    expect(control<HTMLDetailsElement>('mc-table-image-layout-section').open).toBeTrue();
  });
});
