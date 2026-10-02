import { ChangeDetectionStrategy, Component } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';

import { FILTER_FACET_SEARCH_THRESHOLD, FilterFacetComponent, FilterFacetOption } from './filter-facet.component';

@Component({
  standalone: true,
  imports: [FilterFacetComponent],
  changeDetection: ChangeDetectionStrategy.Eager,
  template: `
    <div class="outer" (keydown)="outerKeys.push($event.key)">
      <app-filter-facet facetId="t-facet-document" label="Document" [options]="options" [selected]="selected"
                        [mode]="mode" anyLabel="Any time" (selectedChange)="onChange($event)"></app-filter-facet>
    </div>
  `
})
class FacetHostComponent {
  options: FilterFacetOption[] = [
    { value: 'Executive Summary', label: 'Executive Summary', count: 4 },
    { value: 'Run report', label: 'Run report', count: 1 },
    { value: 'Tool-call log', label: 'Tool-call log', count: 0 }
  ];
  selected: string[] = [];
  mode: 'multiple' | 'single' = 'multiple';
  emitted: string[][] = [];
  outerKeys: string[] = [];

  onChange(values: string[]): void {
    this.emitted.push(values);
    this.selected = values;
  }
}

describe('FilterFacetComponent', () => {
  let fixture: ComponentFixture<FacetHostComponent>;
  let host: FacetHostComponent;
  let el: HTMLElement;

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [FacetHostComponent] }).compileComponents();
    fixture = TestBed.createComponent(FacetHostComponent);
    host = fixture.componentInstance;
    el = fixture.nativeElement as HTMLElement;
    fixture.detectChanges();
  });

  afterEach(() => {
    const popover = el.querySelector<HTMLElement>('.gh-facet-popover');
    if (popover?.matches(':popover-open')) {
      popover.hidePopover();
    }
    fixture.destroy();
  });

  const trigger = (): HTMLButtonElement => el.querySelector<HTMLButtonElement>('#t-facet-document-trigger')!;
  const popover = (): HTMLElement => el.querySelector<HTMLElement>('#t-facet-document-popover')!;
  const inputs = (): HTMLInputElement[] => Array.from(el.querySelectorAll<HTMLInputElement>('.gh-facet-option input'));
  const flat = (node: Element | null): string => (node?.textContent ?? '').replace(/\s+/g, ' ').trim();

  /** Opens the popover by its trigger and waits for the toggle event. */
  async function openPopover(): Promise<void> {
    const toggled = new Promise<void>(resolve => popover().addEventListener('toggle', () => resolve(), { once: true }));
    trigger().click();
    await toggled;
    fixture.detectChanges();
  }

  async function closePopover(): Promise<void> {
    const toggled = new Promise<void>(resolve => popover().addEventListener('toggle', () => resolve(), { once: true }));
    popover().hidePopover();
    await toggled;
    fixture.detectChanges();
  }

  it('names the trigger by its label and the number selected, and anchors the popover to it', () => {
    expect(trigger().type).toBe('button');
    expect(trigger().getAttribute('popovertarget')).toBe('t-facet-document-popover');
    expect(flat(trigger())).toBe('Document');
    expect(trigger().classList).not.toContain('is-active');
    expect(trigger().querySelector('svg')!.getAttribute('aria-hidden')).toBe('true');
    expect(trigger().getAttribute('style')).toContain('anchor-name: --t-facet-document');
    expect(popover().getAttribute('style')).toContain('position-anchor: --t-facet-document');
    expect(popover().getAttribute('popover')).toBe('auto');

    host.selected = ['Run report'];
    fixture.detectChanges();
    expect(flat(trigger())).toBe('Document 1 selected');
    expect(trigger().classList).toContain('is-active');
  });

  it('follows the popover in aria-expanded, focuses the first option on open and the trigger on close', async () => {
    expect(trigger().getAttribute('aria-expanded')).toBe('false');

    await openPopover();
    expect(trigger().getAttribute('aria-expanded')).toBe('true');
    expect(document.activeElement).toBe(inputs()[0]);

    await closePopover();
    expect(trigger().getAttribute('aria-expanded')).toBe('false');
    expect(document.activeElement).toBe(trigger());
  });

  it('closes on Escape without the key reaching a parent', async () => {
    await openPopover();
    const toggled = new Promise<void>(resolve => popover().addEventListener('toggle', () => resolve(), { once: true }));

    const escape = new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true });
    inputs()[0].dispatchEvent(escape);
    await toggled;
    fixture.detectChanges();

    expect(escape.defaultPrevented).toBe(true);
    expect(host.outerKeys).toEqual([]);
    expect(popover().matches(':popover-open')).toBe(false);
    expect(document.activeElement).toBe(trigger());

    trigger().dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
    expect(host.outerKeys).toEqual(['Escape']);
  });

  it('groups checkboxes under a Filter by legend, each with its count and hidden noun', () => {
    const group = el.querySelector('fieldset.gh-facet-group')!;
    expect(flat(group.querySelector('legend'))).toBe('Filter by Document');
    expect(inputs().map(input => input.type)).toEqual(['checkbox', 'checkbox', 'checkbox']);
    expect(inputs().map(input => input.id)).toEqual(['t-facet-document-opt-0', 't-facet-document-opt-1', 't-facet-document-opt-2']);

    const labels = Array.from(el.querySelectorAll('.gh-facet-option')).map(flat);
    expect(labels).toEqual(['Executive Summary, 4 documents', 'Run report, 1 document', 'Tool-call log, 0 documents']);
    const empty = el.querySelectorAll('.gh-facet-option')[2];
    expect(empty.classList).toContain('is-empty');
    expect(empty.querySelector('input')!.disabled).toBe(false);
  });

  it('emits the selection on each change and offers Clear while something is selected', () => {
    expect(el.querySelector('.gh-facet-clear')).toBeNull();

    inputs()[0].click();
    fixture.detectChanges();
    inputs()[1].click();
    fixture.detectChanges();
    expect(host.emitted).toEqual([['Executive Summary'], ['Executive Summary', 'Run report']]);
    expect(inputs().map(input => input.checked)).toEqual([true, true, false]);

    inputs()[0].click();
    fixture.detectChanges();
    expect(host.emitted[2]).toEqual(['Run report']);

    const clear = el.querySelector<HTMLButtonElement>('.gh-facet-clear')!;
    expect(flat(clear)).toBe('Clear Document filter');
    clear.click();
    fixture.detectChanges();
    expect(host.emitted[3]).toEqual([]);
    expect(el.querySelector('.gh-facet-clear')).toBeNull();
  });

  it('renders radios in single mode, the Any option first, which emits an empty selection', () => {
    host.mode = 'single';
    host.selected = ['Run report'];
    fixture.detectChanges();

    const radios = inputs();
    expect(radios.map(radio => radio.type)).toEqual(['radio', 'radio', 'radio', 'radio']);
    expect(radios.every(radio => radio.name === 't-facet-document')).toBe(true);
    expect(flat(radios[0].closest('label'))).toBe('Any time');
    expect(radios.map(radio => radio.checked)).toEqual([false, false, true, false]);

    radios[1].click();
    fixture.detectChanges();
    expect(host.emitted[0]).toEqual(['Executive Summary']);

    inputs()[0].click();
    fixture.detectChanges();
    expect(host.emitted[1]).toEqual([]);
  });

  it('adds an option search only above the threshold, and keeps the selections it hides', async () => {
    expect(el.querySelector('.gh-facet-search')).toBeNull();

    host.options = Array.from({ length: FILTER_FACET_SEARCH_THRESHOLD + 1 }, (_, i) =>
      ({ value: `suite-${i}`, label: i === 3 ? 'Wiki Suite' : `Board Suite ${i}`, count: i }));
    host.selected = ['suite-0'];
    fixture.detectChanges();

    const search = el.querySelector<HTMLInputElement>('#t-facet-document-search')!;
    expect(search.type).toBe('search');
    const label = el.querySelector(`label[for="t-facet-document-search"]`)!;
    expect(label.classList).toContain('visually-hidden');
    expect(flat(label)).toBe('Filter options');

    await openPopover();
    expect(document.activeElement).toBe(search);

    search.value = 'wiki';
    search.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    expect(inputs().map(input => input.id)).toEqual(['t-facet-document-opt-3']);

    inputs()[0].click();
    fixture.detectChanges();
    expect(host.emitted[0]).toEqual(['suite-0', 'suite-3']);
  });

  it('derives every id from facetId', () => {
    host.selected = ['Run report'];
    fixture.detectChanges();

    const ids = Array.from(el.querySelectorAll('[id]')).map(node => node.id);
    expect(ids.length).toBeGreaterThan(0);
    for (const id of ids) {
      expect(id.startsWith('t-facet-document'), id).toBe(true);
    }
  });
});
