import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MAX_PAGE_SLOTS, TableState } from './table-state';
import { TablePagerComponent } from './table-pager.component';

/** `n` rows, enough to produce `n / pageSize` pages at the default page size of 10. */
function rowsOf(count: number): number[] {
  return Array.from({ length: count }, (_, i) => i);
}

describe('TablePagerComponent', () => {
  let fixture: ComponentFixture<TablePagerComponent>;
  let component: TablePagerComponent;
  let host: HTMLElement;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [TablePagerComponent]
    }).compileComponents();

    fixture = TestBed.createComponent(TablePagerComponent);
    component = fixture.componentInstance;
    host = fixture.nativeElement;
  });

  function render(rows: number[], noun = 'runs'): void {
    fixture.componentRef.setInput('state', new TableState<number>('value'));
    fixture.componentRef.setInput('rows', rows);
    fixture.componentRef.setInput('noun', noun);
    fixture.detectChanges();
  }

  function jumpInput(): HTMLInputElement | null {
    return host.querySelector<HTMLInputElement>('.gh-pager-jump-input');
  }

  function pressEnter(input: HTMLInputElement, value: string): void {
    input.value = value;
    input.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter' }));
  }

  function fireChange(input: HTMLInputElement, value: string): void {
    input.value = value;
    input.dispatchEvent(new Event('change'));
  }

  describe('the "Go to page" field', () => {
    it('is absent when the page count fits within MAX_PAGE_SLOTS', () => {
      render(rowsOf(7 * 10));

      expect(component.totalPages).toBe(7);
      expect(MAX_PAGE_SLOTS).toBe(7);
      expect(host.querySelector('.gh-pager-jump')).toBeNull();
    });

    it('appears once the page count exceeds MAX_PAGE_SLOTS', () => {
      render(rowsOf(8 * 10));

      expect(component.totalPages).toBe(8);
      expect(jumpInput()).not.toBeNull();
    });

    it('moves to the typed page on Enter and emits changed exactly once', () => {
      const changed = jasmine.createSpy('changed');
      component.changed.subscribe(changed);
      render(rowsOf(20 * 10));

      const input = jumpInput()!;
      pressEnter(input, '5');

      expect(component.state.page).toBe(5);
      expect(changed).toHaveBeenCalledTimes(1);
      expect(input.value).toBe('');

      // A native change event that follows Enter on the same field must not re-fire the
      // jump: the field was already cleared, so it is read as empty and ignored.
      fireChange(input, '');
      expect(changed).toHaveBeenCalledTimes(1);
    });

    it('clamps an out-of-range page to the last page', () => {
      const changed = jasmine.createSpy('changed');
      component.changed.subscribe(changed);
      render(rowsOf(20 * 10));

      pressEnter(jumpInput()!, '999');

      expect(component.state.page).toBe(20);
      expect(changed).toHaveBeenCalledTimes(1);
    });

    it('leaves the page unchanged and emits nothing for non-numeric or empty input', () => {
      const changed = jasmine.createSpy('changed');
      component.changed.subscribe(changed);
      render(rowsOf(20 * 10));

      const input = jumpInput()!;
      pressEnter(input, 'abc');
      expect(component.state.page).toBe(1);
      expect(changed).not.toHaveBeenCalled();
      expect(input.value).toBe('');

      pressEnter(input, '');
      expect(component.state.page).toBe(1);
      expect(changed).not.toHaveBeenCalled();
    });
  });

  describe('the first/previous step buttons on page 1', () => {
    it('carry aria-disabled="true" and emit nothing when clicked', () => {
      const changed = jasmine.createSpy('changed');
      component.changed.subscribe(changed);
      render(rowsOf(50));

      const first = host.querySelector<HTMLButtonElement>('[aria-label="First page of runs"]')!;
      const previous = host.querySelector<HTMLButtonElement>('[aria-label="Previous page of runs"]')!;

      expect(first.getAttribute('aria-disabled')).toBe('true');
      expect(previous.getAttribute('aria-disabled')).toBe('true');

      first.click();
      previous.click();

      expect(component.state.page).toBe(1);
      expect(changed).not.toHaveBeenCalled();
    });
  });

  describe('.gh-page-compact', () => {
    it('reads "Page 3 of 12" on page 3', () => {
      render(rowsOf(12 * 10));
      host.querySelector<HTMLButtonElement>('[aria-label="Page 3"]')!.click();
      fixture.detectChanges();

      const compact = host.querySelector('.gh-page-compact');
      expect(compact?.textContent?.replace(/\s+/g, ' ').trim()).toBe('Page 3 of 12');
    });
  });

  describe('.gh-pager-buttons', () => {
    it('carries role="group" and an aria-label naming the noun', () => {
      render(rowsOf(50), 'runs');

      const group = host.querySelector('.gh-pager-buttons')!;
      expect(group.getAttribute('role')).toBe('group');
      expect(group.getAttribute('aria-label')).toBe('Pages of runs');
    });
  });
});
