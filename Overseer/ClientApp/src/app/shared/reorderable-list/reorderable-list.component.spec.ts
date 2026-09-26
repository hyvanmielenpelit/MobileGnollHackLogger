import { ChangeDetectionStrategy, ChangeDetectorRef, Component, inject } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import {
  ReorderableListComponent,
  ReorderableListItem,
  idFragment,
  moveEntry
} from './reorderable-list.component';

const ITEMS: readonly ReorderableListItem[] = [
  { key: 'a', label: 'Alpha' },
  { key: 'b', label: 'Beta', tags: ['table only'] },
  { key: 'c|x', label: 'Gamma' },
  { key: 'd', label: 'Delta' }
];

/** Feeds every emitted order and check back into `items`, the way a real host does. */
@Component({
  standalone: true,
  imports: [ReorderableListComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <button type="button" class="outside">Outside</button>
    <app-reorderable-list idPrefix="t" listLabel="Model order" itemNoun="model"
                          [items]="items" [checkable]="checkable"
                          [dividerIndex]="dividerIndex" dividerText="Charts plot the entries above this line"
                          [itemTemplate]="useTemplate ? custom : null"
                          (orderChange)="onOrder($event)" (checkedChange)="onChecked($event)" />
    <ng-template #custom let-item><span class="custom-body">Custom {{ item.label }}</span></ng-template>
  `
})
class TestHostComponent {
  private cdr = inject(ChangeDetectorRef);
  items: readonly ReorderableListItem[] = ITEMS;
  checkable = false;
  dividerIndex: number | null = null;
  useTemplate = false;
  orders: string[][] = [];
  checks: { key: string; checked: boolean }[] = [];

  update(patch: Partial<Pick<TestHostComponent, 'items' | 'checkable' | 'dividerIndex' | 'useTemplate'>>): void {
    Object.assign(this, patch);
    this.cdr.markForCheck();
  }

  onOrder(keys: string[]): void {
    this.orders.push(keys);
    const byKey = new Map(this.items.map(item => [item.key, item] as const));
    this.items = keys.map(key => byKey.get(key)!);
    this.cdr.markForCheck();
  }

  onChecked(change: { key: string; checked: boolean }): void {
    this.checks.push(change);
    this.items = this.items.map(item => item.key === change.key ? { ...item, checked: change.checked } : item);
    this.cdr.markForCheck();
  }
}

describe('ReorderableListComponent', () => {
  let fixture: ComponentFixture<TestHostComponent>;
  let hostComponent: TestHostComponent;
  let el: HTMLElement;

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [TestHostComponent] }).compileComponents();
    fixture = TestBed.createComponent(TestHostComponent);
    hostComponent = fixture.componentInstance;
    fixture.detectChanges();
    el = fixture.nativeElement as HTMLElement;
  });

  const rows = () => Array.from(el.querySelectorAll<HTMLLIElement>('li.rl-row'));
  const rowLabels = () => rows().map(row => row.querySelector('.rl-label')?.textContent?.trim());
  const byId = <T extends HTMLElement = HTMLElement>(id: string) => el.querySelector<T>(`#${id}`)!;
  const status = () => el.querySelector('[role="status"]')!.textContent!.trim();
  const grip = (row: HTMLElement) => row.querySelector<HTMLElement>('.rl-grip')!;
  const center = (row: HTMLElement) => {
    const rect = row.getBoundingClientRect();
    return rect.top + rect.height / 2;
  };

  function update(patch: Parameters<TestHostComponent['update']>[0]): void {
    hostComponent.update(patch);
    fixture.detectChanges();
  }

  function press(id: string): void {
    const button = byId<HTMLButtonElement>(id);
    button.focus();
    button.click();
    fixture.detectChanges();
  }

  const menu = () => byId('t-move-menu');
  const menuOpen = () => menu().matches(':popover-open');
  /** Lets a queued popover `toggle` event fire, then renders. */
  async function nextTask(): Promise<void> {
    await new Promise(resolve => setTimeout(resolve));
    fixture.detectChanges();
  }

  function pointer(type: string, target: HTMLElement, clientY: number): PointerEvent {
    const event = new PointerEvent(type, {
      bubbles: true, cancelable: true, pointerId: 7, isPrimary: true, button: 0, clientY
    });
    target.dispatchEvent(event);
    return event;
  }

  describe('helpers', () => {
    it('idFragment keeps letters, digits and hyphens and encodes everything else distinctly', () => {
      expect(idFragment('gpt-5')).toBe('gpt-5');
      expect(idFragment('c|x')).toBe('c_7c_x');
      expect(idFragment('a_b')).toBe('a_5f_b');
      expect(idFragment('a b')).not.toBe(idFragment('a_b'));
    });

    it('moveEntry returns a new array with one entry moved', () => {
      const source = ['a', 'b', 'c', 'd'];
      expect(moveEntry(source, 0, 2)).toEqual(['b', 'c', 'a', 'd']);
      expect(moveEntry(source, 3, 1)).toEqual(['a', 'd', 'b', 'c']);
      expect(source).toEqual(['a', 'b', 'c', 'd']);
    });
  });

  describe('rendering', () => {
    it('renders the items in order with their labels and tags', () => {
      expect(rowLabels()).toEqual(['Alpha', 'Beta', 'Gamma', 'Delta']);
      const tags = Array.from(rows()[1].querySelectorAll('.rl-tag')).map(t => t.textContent!.trim());
      expect(tags).toEqual(['table only']);
      expect(rows()[0].querySelector('.rl-tag')).toBeNull();
    });

    it('names the list and describes it with the drag instructions', () => {
      const list = el.querySelector('ol')!;
      expect(list.getAttribute('aria-label')).toBe('Model order');
      const instructions = byId(list.getAttribute('aria-describedby')!);
      expect(instructions.textContent!.trim())
        .toBe('Drag a model by its handle, or press the handle for move options.');
    });

    it('renders the handle as a button named "Move <label>", with aria-expanded false and aria-controls the menu', () => {
      const handle = grip(rows()[2]);
      expect(handle.tagName).toBe('BUTTON');
      expect(handle.getAttribute('type')).toBe('button');
      expect(handle.id).toBe('t-c_7c_x-handle');
      expect(handle.getAttribute('aria-label')).toBe('Move Gamma');
      expect(handle.getAttribute('aria-expanded')).toBe('false');
      expect(handle.getAttribute('aria-controls')).toBe('t-move-menu');
      expect(handle.hasAttribute('aria-hidden')).toBeFalse();
      expect(handle.querySelector('svg')!.getAttribute('aria-hidden')).toBe('true');
      expect(handle.querySelectorAll('circle').length).toBe(6);
      expect(handle.getAttribute('interestfor')).toBe('t-c_7c_x-handle-tip');
      expect(handle.hasAttribute('title')).toBeFalse();
      expect(byId('t-c_7c_x-handle-tip').getAttribute('popover')).toBe('hint');
      expect(byId('t-c_7c_x-handle-tip').textContent!.trim()).toBe('Drag, or press for move options');
    });

    it('has no per-row move buttons', () => {
      for (const row of rows()) {
        expect(row.querySelectorAll('button').length).withContext(row.textContent!).toBe(1);
      }
      expect(el.querySelector('.rl-moves, .rl-move')).toBeNull();
      expect(el.querySelector('#t-a-up, #t-a-down')).toBeNull();
    });

    it('renders one closed Move menu for the whole list', () => {
      expect(el.querySelectorAll('.rl-move-menu').length).toBe(1);
      expect(menu().getAttribute('popover')).toBe('auto');
      expect(menu().getAttribute('role')).toBe('group');
      expect(menuOpen()).toBeFalse();
      expect(Array.from(menu().querySelectorAll('button')).map(b => b.textContent!.trim()))
        .toEqual(['Move to top', 'Move up', 'Move down', 'Move to bottom']);
    });

    it('renders a custom body through the item template, with the tags after it', () => {
      update({ useTemplate: true });
      const bodies = Array.from(el.querySelectorAll('.custom-body')).map(b => b.textContent!.trim());
      expect(bodies).toEqual(['Custom Alpha', 'Custom Beta', 'Custom Gamma', 'Custom Delta']);
      expect(el.querySelector('.rl-label')).toBeNull();
      expect(rows()[1].querySelector('.rl-tag')!.textContent!.trim()).toBe('table only');
    });
  });

  describe('move menu', () => {
    afterEach(() => {
      if (menuOpen()) {
        menu().hidePopover();
      }
    });

    it('opens for the pressed row, names the group and focuses the first enabled option', () => {
      press('t-c_7c_x-handle');
      expect(menuOpen()).toBeTrue();
      expect(byId('t-c_7c_x-handle').getAttribute('aria-expanded')).toBe('true');
      expect(byId('t-a-handle').getAttribute('aria-expanded')).toBe('false');
      expect(menu().getAttribute('aria-label')).toBe('Move Gamma');
      expect(document.activeElement?.id).toBe('t-move-top');
    });

    it('skips the aria-disabled options when it focuses the first one', () => {
      press('t-a-handle');
      expect(byId('t-move-top').getAttribute('aria-disabled')).toBe('true');
      expect(byId('t-move-up').getAttribute('aria-disabled')).toBe('true');
      expect(byId('t-move-down').hasAttribute('aria-disabled')).toBeFalse();
      expect(byId<HTMLButtonElement>('t-move-top').disabled).toBeFalse();
      expect(document.activeElement?.id).toBe('t-move-down');
    });

    it('closes when its own handle is pressed again', () => {
      press('t-b-handle');
      press('t-b-handle');
      expect(menuOpen()).toBeFalse();
      expect(byId('t-b-handle').getAttribute('aria-expanded')).toBe('false');
    });

    it('Move down and Move up move the row by one, keep the menu open and keep focus on the option', () => {
      press('t-a-handle');
      press('t-move-down');
      expect(hostComponent.orders).toEqual([['b', 'a', 'c|x', 'd']]);
      expect(rowLabels()).toEqual(['Beta', 'Alpha', 'Gamma', 'Delta']);
      expect(menuOpen()).toBeTrue();
      expect(document.activeElement?.id).toBe('t-move-down');

      press('t-move-up');
      expect(hostComponent.orders[1]).toEqual(['a', 'b', 'c|x', 'd']);
      expect(menuOpen()).toBeTrue();
    });

    it('moves focus to the opposite option when the row reaches an end', () => {
      press('t-b-handle');
      press('t-move-down');
      press('t-move-down');
      expect(rowLabels()).toEqual(['Alpha', 'Gamma', 'Delta', 'Beta']);
      expect(byId('t-move-down').getAttribute('aria-disabled')).toBe('true');
      expect(document.activeElement?.id).toBe('t-move-up');

      press('t-c_7c_x-handle');
      press('t-move-up');
      expect(rowLabels()).toEqual(['Gamma', 'Alpha', 'Delta', 'Beta']);
      expect(byId('t-move-up').getAttribute('aria-disabled')).toBe('true');
      expect(document.activeElement?.id).toBe('t-move-down');
    });

    it('Move to top and Move to bottom move the row to the end, close the menu and focus the handle', () => {
      press('t-c_7c_x-handle');
      press('t-move-top');
      expect(hostComponent.orders).toEqual([['c|x', 'a', 'b', 'd']]);
      expect(menuOpen()).toBeFalse();
      expect(document.activeElement?.id).toBe('t-c_7c_x-handle');

      press('t-a-handle');
      press('t-move-bottom');
      expect(hostComponent.orders[1]).toEqual(['c|x', 'b', 'd', 'a']);
      expect(menuOpen()).toBeFalse();
      expect(document.activeElement?.id).toBe('t-a-handle');
    });

    it('ignores the aria-disabled options at an end', () => {
      press('t-a-handle');
      press('t-move-top');
      press('t-move-up');
      expect(hostComponent.orders).toEqual([]);
      expect(status()).toBe('');
      expect(menuOpen()).toBeTrue();

      press('t-d-handle');
      press('t-move-bottom');
      press('t-move-down');
      expect(hostComponent.orders).toEqual([]);
      expect(status()).toBe('');
    });

    it('closes on Escape and focuses the handle', async () => {
      press('t-b-handle');
      const escape = new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true });
      document.activeElement!.dispatchEvent(escape);
      fixture.detectChanges();
      expect(escape.defaultPrevented).toBeTrue();
      expect(menuOpen()).toBeFalse();
      expect(document.activeElement?.id).toBe('t-b-handle');

      await nextTask();
      expect(byId('t-b-handle').getAttribute('aria-expanded')).toBe('false');
    });

    it('clears its row when it is light-dismissed', async () => {
      press('t-b-handle');
      menu().hidePopover();
      await nextTask();
      expect(byId('t-b-handle').getAttribute('aria-expanded')).toBe('false');
      expect(menu().hasAttribute('aria-label')).toBeFalse();
    });

    it('announces each committed move politely', () => {
      press('t-b-handle');
      press('t-move-down');
      expect(status()).toBe('Beta moved to position 3 of 4.');
      press('t-move-down');
      expect(status()).toBe('Beta moved to position 4 of 4.');
      press('t-move-up');
      press('t-move-top');
      expect(status()).toBe('Beta moved to position 1 of 4.');
    });

    it('skips the divider in the index arithmetic', () => {
      update({ dividerIndex: 2 });
      press('t-c_7c_x-handle');
      press('t-move-up');
      expect(hostComponent.orders).toEqual([['a', 'c|x', 'b', 'd']]);
      expect(status()).toBe('Gamma moved to position 2 of 4.');
    });
  });

  describe('checkboxes', () => {
    it('renders no checkbox unless checkable', () => {
      expect(el.querySelector('input[type="checkbox"]')).toBeNull();
    });

    it('labels each checkbox by the item and emits checkedChange', () => {
      update({ checkable: true });
      const box = byId<HTMLInputElement>('t-b-check');
      expect(box.closest('label')!.classList).toContain('checkbox-label');
      expect(box.getAttribute('aria-label')).toBe('Beta');
      expect(box.getAttribute('aria-describedby')).toBe('t-b-tags');
      expect(box.checked).toBeFalse();

      box.click();
      fixture.detectChanges();
      expect(hostComponent.checks).toEqual([{ key: 'b', checked: true }]);
      expect(byId<HTMLInputElement>('t-b-check').checked).toBeTrue();
      expect(status()).toBe('');
    });

    it('shows the input state until the host passes a change back', () => {
      update({ checkable: true });
      spyOn(hostComponent, 'onChecked').and.callFake(change => hostComponent.checks.push(change));
      const box = byId<HTMLInputElement>('t-a-check');
      box.click();
      fixture.detectChanges();
      expect(hostComponent.checks).toEqual([{ key: 'a', checked: true }]);
      expect(box.checked).toBeFalse();
    });

    it('renders a locked item checked and disabled, with its reason in an info tip', () => {
      update({
        checkable: true,
        items: [{ key: 'a', label: 'Alpha', locked: true, lockedReason: 'The model column is always shown.' }, ...ITEMS.slice(1)]
      });
      const box = byId<HTMLInputElement>('t-a-check');
      expect(box.checked).toBeTrue();
      expect(box.disabled).toBeTrue();
      expect(box.getAttribute('aria-describedby')).toBe('t-a-lock-tip');
      expect(byId('t-a-lock-tip').textContent!.trim()).toBe('The model column is always shown.');
      expect(rows()[0].querySelector('app-info-tip button')!.getAttribute('aria-label')).toBe('About Alpha');

      box.click();
      fixture.detectChanges();
      expect(hostComponent.checks).toEqual([]);
      expect(byId<HTMLButtonElement>('t-a-handle').disabled).toBeFalse();
    });
  });

  describe('divider', () => {
    it('renders a presentational divider before the item at dividerIndex', () => {
      update({ dividerIndex: 2 });
      const divider = el.querySelector<HTMLLIElement>('li.rl-divider')!;
      expect(divider.getAttribute('role')).toBe('presentation');
      expect(divider.textContent!.trim()).toBe('Charts plot the entries above this line');
      expect(divider.nextElementSibling).toBe(rows()[2]);
      expect(divider.querySelector('button, input')).toBeNull();
      expect(rows().length).toBe(4);
    });

    it('renders no divider without dividerIndex', () => {
      expect(el.querySelector('li.rl-divider')).toBeNull();
    });
  });

  describe('pointer drag', () => {
    beforeEach(() => {
      spyOn(Element.prototype, 'setPointerCapture').and.stub();
    });

    it('emits one reordered orderChange on drop and none mid-drag', () => {
      const [alpha, , gamma] = rows();
      const startY = center(alpha);
      const handle = grip(alpha);

      const down = pointer('pointerdown', handle, startY);
      expect(down.defaultPrevented).toBeTrue();
      expect(Element.prototype.setPointerCapture).toHaveBeenCalledWith(7);
      expect(document.activeElement).toBe(handle);
      expect(el.querySelector('ol')!.classList).not.toContain('is-sorting');

      pointer('pointermove', handle, center(gamma) + 1);
      expect(el.querySelector('ol')!.classList).toContain('is-sorting');
      expect(alpha.classList).toContain('is-dragging');
      expect(hostComponent.orders).toEqual([]);
      expect(status()).toBe('');
      expect(alpha.style.transform).toContain('translateY');
      expect(rows()[1].style.transform).toContain('translateY(-');

      pointer('pointerup', handle, center(gamma) + 1);
      fixture.detectChanges();
      expect(hostComponent.orders).toEqual([['b', 'c|x', 'a', 'd']]);
      expect(rowLabels()).toEqual(['Beta', 'Gamma', 'Alpha', 'Delta']);
      expect(status()).toBe('Alpha moved to position 3 of 4.');
      expect(rows().every(row => row.style.transform === '')).toBeTrue();
      expect(el.querySelector('ol')!.classList).not.toContain('is-sorting');
      expect(el.querySelector('.is-dragging')).toBeNull();
    });

    it('moves a row across the divider, which stays at its position', () => {
      update({ dividerIndex: 2 });
      const [, beta, , delta] = rows();
      const handle = grip(delta);
      pointer('pointerdown', handle, center(delta));
      pointer('pointermove', handle, center(beta) - 1);
      expect(hostComponent.orders).toEqual([]);
      pointer('pointerup', handle, center(beta) - 1);
      fixture.detectChanges();
      expect(hostComponent.orders).toEqual([['a', 'd', 'b', 'c|x']]);
      expect(el.querySelector('li.rl-divider')!.nextElementSibling).toBe(rows()[2]);
      expect(rowLabels()[2]).toBe('Beta');
    });

    it('puts the row back and emits nothing when Escape is pressed mid-drag', () => {
      const [alpha, , gamma] = rows();
      const handle = grip(alpha);
      pointer('pointerdown', handle, center(alpha));
      pointer('pointermove', handle, center(gamma) + 1);

      const escape = new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true });
      document.dispatchEvent(escape);
      expect(escape.defaultPrevented).toBeTrue();
      expect(rows().every(row => row.style.transform === '')).toBeTrue();
      expect(alpha.classList).not.toContain('is-dragging');

      pointer('pointerup', handle, center(gamma) + 1);
      fixture.detectChanges();
      expect(hostComponent.orders).toEqual([]);
      expect(rowLabels()).toEqual(['Alpha', 'Beta', 'Gamma', 'Delta']);
      expect(status()).toBe('');
    });

    it('puts the row back and emits nothing on pointercancel', () => {
      const [alpha, , gamma] = rows();
      const handle = grip(alpha);
      pointer('pointerdown', handle, center(alpha));
      pointer('pointermove', handle, center(gamma) + 1);
      pointer('pointercancel', handle, center(gamma) + 1);
      pointer('pointerup', handle, center(gamma) + 1);
      fixture.detectChanges();
      expect(hostComponent.orders).toEqual([]);
      expect(rows().every(row => row.style.transform === '')).toBeTrue();
    });

    it('emits nothing when the row is dropped where it started', () => {
      const [alpha] = rows();
      const handle = grip(alpha);
      pointer('pointerdown', handle, center(alpha));
      pointer('pointermove', handle, center(alpha) + 2);
      pointer('pointerup', handle, center(alpha) + 2);
      fixture.detectChanges();
      expect(hostComponent.orders).toEqual([]);
    });

    it('ignores a press that is not on a grip', () => {
      const [alpha] = rows();
      const down = pointer('pointerdown', alpha.querySelector<HTMLElement>('.rl-label')!, center(alpha));
      expect(down.defaultPrevented).toBeFalse();
      expect(el.querySelector('ol')!.classList).not.toContain('is-sorting');
    });

    describe('and the Move menu', () => {
      afterEach(() => {
        if (menuOpen()) {
          menu().hidePopover();
        }
      });

      it('a press that moves less than the threshold does not drag, and its click opens the menu', () => {
        const [alpha] = rows();
        const handle = grip(alpha);
        pointer('pointerdown', handle, center(alpha));
        pointer('pointermove', handle, center(alpha) + 3);
        expect(el.querySelector('ol')!.classList).not.toContain('is-sorting');
        expect(alpha.style.transform).toBe('');
        pointer('pointerup', handle, center(alpha) + 3);
        handle.click();
        fixture.detectChanges();
        expect(hostComponent.orders).toEqual([]);
        expect(menuOpen()).toBeTrue();
        expect(handle.getAttribute('aria-expanded')).toBe('true');
      });

      it('the click that follows a real drag does not open the menu', () => {
        const [alpha, , gamma] = rows();
        const handle = grip(alpha);
        pointer('pointerdown', handle, center(alpha));
        pointer('pointermove', handle, center(gamma) + 1);
        pointer('pointerup', handle, center(gamma) + 1);
        handle.click();
        fixture.detectChanges();
        expect(hostComponent.orders).toEqual([['b', 'c|x', 'a', 'd']]);
        expect(menuOpen()).toBeFalse();
      });

      it('a drag put back with Escape does not open the menu on release either', () => {
        const [alpha, , gamma] = rows();
        const handle = grip(alpha);
        pointer('pointerdown', handle, center(alpha));
        pointer('pointermove', handle, center(gamma) + 1);
        document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true }));
        pointer('pointerup', handle, center(gamma) + 1);
        handle.click();
        fixture.detectChanges();
        expect(hostComponent.orders).toEqual([]);
        expect(menuOpen()).toBeFalse();
      });

      it('a keyboard press on the handle after a drag still opens the menu', async () => {
        const [alpha, , gamma] = rows();
        pointer('pointerdown', grip(alpha), center(alpha));
        pointer('pointermove', grip(alpha), center(gamma) + 1);
        pointer('pointerup', grip(alpha), center(gamma) + 1);
        fixture.detectChanges();
        // No click followed the drop; the next task forgets the suppression.
        await nextTask();
        press('t-b-handle');
        expect(menuOpen()).toBeTrue();
      });

      it('starting a drag closes an open menu', () => {
        press('t-a-handle');
        expect(menuOpen()).toBeTrue();
        const [alpha, , gamma] = rows();
        const handle = grip(alpha);
        pointer('pointerdown', handle, center(alpha));
        expect(menuOpen()).toBeTrue();
        pointer('pointermove', handle, center(gamma) + 1);
        fixture.detectChanges();
        expect(menuOpen()).toBeFalse();
        expect(handle.getAttribute('aria-expanded')).toBe('false');
        pointer('pointerup', handle, center(gamma) + 1);
        fixture.detectChanges();
        expect(hostComponent.orders).toEqual([['b', 'c|x', 'a', 'd']]);
      });
    });
  });
});
