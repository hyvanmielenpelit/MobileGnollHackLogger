import { ComponentFixture, TestBed } from '@angular/core/testing';
import {
  TelemetrySort,
  TelemetrySortFieldInfo,
  TelemetrySortPopoverComponent,
  telemetrySortLabel
} from './telemetry-sort-popover.component';

const settle = (ms = 0): Promise<void> => new Promise<void>(resolve => setTimeout(resolve, ms));

const textOf = (el: Element | null | undefined): string => (el?.textContent ?? '').replace(/\s+/g, ' ').trim();

const FIELDS: readonly TelemetrySortFieldInfo[] = [
  { field: 'cost', label: 'Est. cost', kind: 'number' },
  { field: 'requests', label: 'Requests', kind: 'number' },
  { field: 'lastActive', label: 'Last active', kind: 'date' },
  { field: 'name', label: 'User name', kind: 'text' }
];

const DEFAULT_SORT: TelemetrySort = { field: 'cost', direction: 'desc' };

describe('telemetrySortLabel', () => {
  it('reads the field, then the order in lower case, except for a name', () => {
    expect(telemetrySortLabel(FIELDS, { field: 'cost', direction: 'desc' })).toBe('Est. cost, highest first');
    expect(telemetrySortLabel(FIELDS, { field: 'requests', direction: 'asc' })).toBe('Requests, lowest first');
    expect(telemetrySortLabel(FIELDS, { field: 'lastActive', direction: 'desc' })).toBe('Last active, newest first');
    expect(telemetrySortLabel(FIELDS, { field: 'lastActive', direction: 'asc' })).toBe('Last active, oldest first');
    expect(telemetrySortLabel(FIELDS, { field: 'name', direction: 'asc' })).toBe('User name, A–Z');
    expect(telemetrySortLabel(FIELDS, { field: 'name', direction: 'desc' })).toBe('User name, Z–A');
  });

  it('reads an unknown field as the first field', () => {
    expect(telemetrySortLabel(FIELDS, { field: 'gone', direction: 'desc' })).toBe('Est. cost, highest first');
  });
});

describe('TelemetrySortPopoverComponent', () => {
  let fixture: ComponentFixture<TelemetrySortPopoverComponent>;
  let el: HTMLElement;
  let emitted: TelemetrySort[];

  const q = <T extends HTMLElement = HTMLElement>(selector: string): T | null => el.querySelector<T>(selector);
  const trigger = () => q<HTMLButtonElement>('#tl-test-sort-trigger')!;
  const panel = () => q<HTMLElement>('#tl-test-sort-panel')!;

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [TelemetrySortPopoverComponent] }).compileComponents();
    fixture = TestBed.createComponent(TelemetrySortPopoverComponent);
    el = fixture.nativeElement as HTMLElement;
    emitted = [];
    fixture.componentRef.setInput('idPrefix', 'tl-test');
    fixture.componentRef.setInput('fields', FIELDS);
    fixture.componentRef.setInput('sort', DEFAULT_SORT);
    fixture.componentRef.setInput('defaultSort', DEFAULT_SORT);
    // The host feeds the sort back, as a real host does.
    fixture.componentInstance.sortChange.subscribe(sort => {
      emitted.push(sort);
      fixture.componentRef.setInput('sort', sort);
    });
    fixture.detectChanges();
  });

  afterEach(() => {
    try { panel().hidePopover(); } catch { /* already closed */ }
    fixture.destroy();
  });

  async function openPanel(): Promise<void> {
    trigger().click();
    await settle(20);
    fixture.detectChanges();
  }

  it('labels the trigger Sort: {field}, {order} with the arrow-down-up glyph', () => {
    expect(textOf(trigger())).toBe('Sort: Est. cost, highest first');
    expect(trigger().classList).toContain('btn-ghost');
    expect(trigger().classList).toContain('tl-sort-trigger');
    expect(trigger().querySelector('svg')?.getAttribute('aria-hidden')).toBe('true');
    expect(trigger().querySelectorAll('svg path').length).toBe(4);
  });

  it('derives every id, radio name and anchor name from the prefix', () => {
    expect(trigger().getAttribute('popovertarget')).toBe('tl-test-sort-panel');
    expect(trigger().getAttribute('style')).toContain('anchor-name: --tl-test-sort-trigger');
    expect(panel().getAttribute('style')).toContain('position-anchor: --tl-test-sort-trigger');
    expect(panel().getAttribute('popover')).toBe('auto');
    expect(panel().classList).toContain('gh-facet-popover');
    expect(panel().classList).toContain('model-picker-sort-panel');
    const fields = Array.from(el.querySelectorAll<HTMLInputElement>('input[name="tl-test-sort-field"]'));
    expect(fields.map(f => f.id)).toEqual(['tl-test-sort-field-cost', 'tl-test-sort-field-requests', 'tl-test-sort-field-lastActive', 'tl-test-sort-field-name']);
    const group = q('[role="radiogroup"]')!;
    expect(group.getAttribute('aria-labelledby')).toBe('tl-test-sort-order-label');
    expect(textOf(q('#tl-test-sort-order-label'))).toBe('Order');
    expect(Array.from(group.querySelectorAll<HTMLInputElement>('input[name="tl-test-sort-direction"]')).map(r => r.id))
      .toEqual(['tl-test-sort-direction-desc', 'tl-test-sort-direction-asc']);
    expect(q('#tl-test-sort-reset')).not.toBeNull();
  });

  it('emits a field with the current order, then an order with the current field', () => {
    q<HTMLInputElement>('#tl-test-sort-field-requests')!.click();
    fixture.detectChanges();
    expect(emitted).toEqual([{ field: 'requests', direction: 'desc' }]);
    expect(textOf(trigger())).toBe('Sort: Requests, highest first');

    q<HTMLInputElement>('#tl-test-sort-direction-asc')!.click();
    fixture.detectChanges();
    expect(emitted[1]).toEqual({ field: 'requests', direction: 'asc' });
    expect(textOf(trigger())).toBe('Sort: Requests, lowest first');
  });

  it('reads the order words by the kind of the chosen field', () => {
    const directions = () => Array.from(el.querySelectorAll('.tl-sort-segment')).map(textOf);
    expect(directions()).toEqual(['Highest first', 'Lowest first']);

    q<HTMLInputElement>('#tl-test-sort-field-name')!.click();
    fixture.detectChanges();
    expect(directions()).toEqual(['Z–A', 'A–Z']);

    q<HTMLInputElement>('#tl-test-sort-field-lastActive')!.click();
    fixture.detectChanges();
    expect(directions()).toEqual(['Newest first', 'Oldest first']);
  });

  it('keeps Reset sorting aria-disabled at the default and refuses it there', () => {
    const reset = () => q<HTMLButtonElement>('#tl-test-sort-reset')!;
    expect(reset().getAttribute('aria-disabled')).toBe('true');
    reset().click();
    expect(emitted).toEqual([]);

    q<HTMLInputElement>('#tl-test-sort-direction-asc')!.click();
    fixture.detectChanges();
    expect(reset().hasAttribute('aria-disabled')).toBe(false);

    reset().click();
    fixture.detectChanges();
    expect(emitted[emitted.length - 1]).toEqual({ field: 'cost', direction: 'desc' });
    expect(reset().getAttribute('aria-disabled')).toBe('true');
  });

  it('opens with aria-expanded and focus on the checked radio, and closes back to the trigger', async () => {
    fixture.componentRef.setInput('sort', { field: 'lastActive', direction: 'desc' });
    fixture.detectChanges();
    expect(trigger().getAttribute('aria-expanded')).toBe('false');

    await openPanel();
    expect(panel().matches(':popover-open')).toBe(true);
    expect(trigger().getAttribute('aria-expanded')).toBe('true');
    expect(document.activeElement?.id).toBe('tl-test-sort-field-lastActive');

    panel().hidePopover();
    await settle(20);
    fixture.detectChanges();
    expect(trigger().getAttribute('aria-expanded')).toBe('false');
    expect(document.activeElement).toBe(trigger());
  });

  it('closes only the panel on Escape, and stops the key there', async () => {
    await openPanel();
    const outer = vi.fn();
    document.addEventListener('keydown', outer);
    try {
      const event = new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true });
      (document.activeElement as HTMLElement).dispatchEvent(event);
      expect(event.defaultPrevented).toBe(true);
      expect(outer).not.toHaveBeenCalled();
      expect(panel().matches(':popover-open')).toBe(false);
      expect(document.activeElement).toBe(trigger());
    } finally {
      document.removeEventListener('keydown', outer);
    }
  });
});
