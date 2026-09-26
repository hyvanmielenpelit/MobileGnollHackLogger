import {
  AfterViewChecked,
  ChangeDetectionStrategy,
  ChangeDetectorRef,
  Component,
  ElementRef,
  EventEmitter,
  Input,
  NgZone,
  OnChanges,
  OnDestroy,
  OnInit,
  Output,
  SimpleChanges,
  TemplateRef,
  ViewChild,
  inject
} from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';

import { InfoTipComponent } from '../info-tip/info-tip.component';
import { ensureOverlayPolyfills, refreshAnchorPositioning } from '../../utils/polyfills.util';

/** One row of an `app-reorderable-list`. `label` names the row in accessible names and announcements. */
export interface ReorderableListItem {
  readonly key: string;
  readonly label: string;
  readonly tags?: readonly string[];
  readonly checked?: boolean;
  readonly locked?: boolean;
  readonly lockedReason?: string;
}

/** The context of an `itemTemplate`: the row's item as `$implicit`. */
export interface ReorderableListItemContext {
  $implicit: ReorderableListItem;
}

type MoveDirection = 'up' | 'down';
type MoveEnd = 'top' | 'bottom';

/** How far, in CSS px, a press on the handle must move before it becomes a drag. */
const DRAG_START_PX = 4;
/** Distance from the scroller's top or bottom edge, in CSS px, within which a drag autoscrolls. */
const AUTOSCROLL_EDGE_PX = 32;
/** Autoscroll step at the very edge, in CSS px per animation frame. */
const AUTOSCROLL_MAX_STEP_PX = 14;
/** A little longer than the rows' transform transition; a row put back has settled by then. */
const SETTLE_MS = 200;

/**
 * An id fragment for a key: letters, digits and hyphens pass through, and every other character
 * becomes `_<hex code point>_`, so distinct keys give distinct fragments that are valid in ids and
 * in dashed anchor names.
 */
export function idFragment(key: string): string {
  return Array.from(key, ch => /[A-Za-z0-9-]/.test(ch) ? ch : `_${ch.codePointAt(0)!.toString(16)}_`).join('');
}

/** `entries` with the entry at `from` moved to `to`; a new array. */
export function moveEntry<T>(entries: readonly T[], from: number, to: number): T[] {
  const next = entries.slice();
  const [moved] = next.splice(from, 1);
  next.splice(to, 0, moved);
  return next;
}

/** Drag geometry, captured once at drag start in the list's own layout coordinates. */
interface DragState {
  readonly pointerId: number;
  readonly grip: HTMLElement;
  readonly list: HTMLElement;
  readonly keys: readonly string[];
  readonly from: number;
  readonly rows: readonly HTMLElement[];
  readonly tops: readonly number[];
  readonly heights: readonly number[];
  readonly divider: HTMLElement | null;
  /** The position the divider precedes, or -1 without a divider. */
  readonly dividerAt: number;
  readonly dividerTop: number;
  readonly dividerHeight: number;
  readonly firstTop: number;
  readonly gap: number;
  readonly startY: number;
  readonly scroller: Element;
  readonly startScroll: number;
  pointerY: number;
  to: number;
}

/** A press on a handle, from `pointerdown` until its `pointerup`, whether or not it became a drag. */
interface PressState {
  readonly pointerId: number;
  readonly grip: HTMLElement;
  readonly row: HTMLElement;
  readonly startY: number;
  dragged: boolean;
}

interface PendingFocus {
  readonly key: string;
  /** A Move menu option, or the row's handle. */
  readonly target: MoveDirection | 'handle';
  /** The `items` the move was made against; focus is restored once the host passes new ones. */
  readonly items: readonly ReorderableListItem[];
}

/**
 * A list whose rows are reordered by dragging a row's handle, or with the Move menu that pressing
 * the handle opens, and optionally checked with a checkbox per row.
 *
 * Presentational only. It never reorders or re-checks `items` itself: a move emits
 * `orderChange` with every key in the new order, a checkbox emits `checkedChange`, and the host
 * passes the updated `items` back. A drag moves rows with transforms only and emits once, on drop.
 * One Move popover serves every row; it sits outside the rows, so a move never disconnects it.
 */
@Component({
  selector: 'app-reorderable-list',
  standalone: true,
  imports: [NgTemplateOutlet, InfoTipComponent],
  templateUrl: './reorderable-list.component.html',
  styleUrls: ['./reorderable-list.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class ReorderableListComponent implements OnInit, OnChanges, AfterViewChecked, OnDestroy {
  private cdr = inject(ChangeDetectorRef);
  private zone = inject(NgZone);
  private host = inject<ElementRef<HTMLElement>>(ElementRef);

  /** The rows, in their current order. */
  @Input() items: readonly ReorderableListItem[] = [];
  /** The row body; without it the row shows its label. Tags follow the body either way. */
  @Input() itemTemplate: TemplateRef<ReorderableListItemContext> | null = null;
  /** Each row gets a checkbox named by its label; a locked row's is checked and disabled. */
  @Input() checkable = false;
  /** The list's accessible name. */
  @Input() listLabel = '';
  /** What one row is, for the drag instructions: `model`, `column`. */
  @Input() itemNoun = 'item';
  /** A non-interactive divider is drawn before the row at this index. */
  @Input() dividerIndex: number | null = null;
  @Input() dividerText = '';
  /** Unique in the document; letters, digits and hyphens. Every element id and anchor name starts with it. */
  @Input() idPrefix = 'rl';

  @Output() orderChange = new EventEmitter<string[]>();
  @Output() checkedChange = new EventEmitter<{ key: string; checked: boolean }>();

  @ViewChild('list', { static: true }) list?: ElementRef<HTMLOListElement>;
  @ViewChild('menu', { static: true }) menu?: ElementRef<HTMLElement>;

  /** The polite announcement after a committed move. */
  status = '';
  /** The row whose Move menu is open, or null. */
  menuKey: string | null = null;

  private rowIds = new Map<string, string>();
  private pendingFocus: PendingFocus | null = null;
  private keySignature = '';
  private anchorsStale = true;

  private press: PressState | null = null;
  private drag: DragState | null = null;
  /** The click that ends a press which became a drag must not open the menu. */
  private suppressNextClick = false;
  private suppressClearTimer: ReturnType<typeof setTimeout> | null = null;
  /** The press began on the handle whose menu was open, so its click closes rather than reopens. */
  private pressClosesMenu = false;
  private scrollFrame: number | null = null;
  private settleTimer: ReturnType<typeof setTimeout> | null = null;
  private hostListeners: [string, EventListener][] = [];

  ngOnInit(): void {
    ensureOverlayPolyfills();
    const host = this.host.nativeElement;
    this.hostListeners = [
      ['pointerdown', this.onPointerDown as EventListener],
      ['pointermove', this.onPointerMove as EventListener],
      ['pointerup', this.onPointerUp as EventListener],
      ['pointercancel', this.onPointerCancel as EventListener],
      ['lostpointercapture', this.onPointerCancel as EventListener]
    ];
    this.zone.runOutsideAngular(() => {
      for (const [type, listener] of this.hostListeners) {
        host.addEventListener(type, listener);
      }
    });
  }

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['idPrefix']) {
      this.rowIds.clear();
      this.anchorsStale = true;
    }
    if (changes['items']) {
      const keys = (this.items ?? []).map(item => item.key);
      const signature = keys.slice().sort().join('\u0000');
      if (signature !== this.keySignature) {
        this.keySignature = signature;
        this.anchorsStale = true;
      }
      const drag = this.drag;
      if (drag && keys.join('\u0000') !== drag.keys.join('\u0000')) {
        this.endDrag(false, false);
      }
      if (this.menuKey !== null && !keys.includes(this.menuKey)) {
        this.closeMenu();
      }
    }
  }

  ngAfterViewChecked(): void {
    if (this.anchorsStale) {
      this.anchorsStale = false;
      refreshAnchorPositioning();
    }

    const pending = this.pendingFocus;
    if (!pending || pending.items === this.items) {
      return;
    }
    this.pendingFocus = null;
    const id = pending.target === 'handle' || this.menuKey !== pending.key
      ? `${this.rowId(pending.key)}-handle`
      : `${this.idPrefix}-move-${pending.target}`;
    const target = document.getElementById(id);
    if (target && document.activeElement !== target) {
      target.focus();
    }
  }

  ngOnDestroy(): void {
    if (this.drag) {
      this.endDrag(false, false);
    }
    this.closeMenu();
    const host = this.host.nativeElement;
    for (const [type, listener] of this.hostListeners) {
      host.removeEventListener(type, listener);
    }
    this.hostListeners = [];
    if (this.settleTimer !== null) {
      clearTimeout(this.settleTimer);
      this.settleTimer = null;
    }
    if (this.suppressClearTimer !== null) {
      clearTimeout(this.suppressClearTimer);
      this.suppressClearTimer = null;
    }
  }

  /** The id every element of a row derives from: `<idPrefix>-<key fragment>`. */
  rowId(key: string): string {
    let id = this.rowIds.get(key);
    if (id === undefined) {
      id = `${this.idPrefix}-${idFragment(key)}`;
      this.rowIds.set(key, id);
    }
    return id;
  }

  /** The checkbox's description: the row's tags and, when locked, the reason. */
  checkboxDescribedBy(item: ReorderableListItem, rowId: string): string | null {
    const ids: string[] = [];
    if (item.tags?.length) {
      ids.push(`${rowId}-tags`);
    }
    if (item.locked && item.lockedReason) {
      ids.push(`${rowId}-lock-tip`);
    }
    return ids.length > 0 ? ids.join(' ') : null;
  }

  /** Moves a row by one. At an end the option is aria-disabled and the click does nothing. */
  move(key: string, direction: MoveDirection): void {
    const from = this.items.findIndex(item => item.key === key);
    if (from < 0) {
      return;
    }
    const to = direction === 'up' ? from - 1 : from + 1;
    const last = this.items.length - 1;
    if (to < 0 || to > last) {
      return;
    }
    // At an end the pressed option becomes aria-disabled, so focus goes to the opposite one.
    const target: MoveDirection =
      direction === 'up' && to === 0 ? 'down' : direction === 'down' && to === last ? 'up' : direction;
    this.pendingFocus = { key, target, items: this.items };
    this.commit(from, to);
  }

  /** Moves a row to the top or the bottom and closes the menu; nothing when it is already there. */
  moveTo(key: string, end: MoveEnd): void {
    const from = this.items.findIndex(item => item.key === key);
    const to = end === 'top' ? 0 : this.items.length - 1;
    if (from < 0 || from === to) {
      return;
    }
    this.pendingFocus = { key, target: 'handle', items: this.items };
    this.commit(from, to);
    this.closeMenu();
    this.focusHandleIfLost(key);
  }

  /** The item whose menu is open. */
  get menuItem(): ReorderableListItem | null {
    return this.menuKey === null ? null : this.items.find(item => item.key === this.menuKey) ?? null;
  }

  private get menuIndex(): number {
    return this.menuKey === null ? -1 : this.items.findIndex(item => item.key === this.menuKey);
  }

  get menuAtTop(): boolean {
    return this.menuIndex === 0;
  }

  get menuAtBottom(): boolean {
    return this.menuIndex >= 0 && this.menuIndex === this.items.length - 1;
  }

  onHandleClick(key: string, event: MouseEvent): void {
    if (this.suppressNextClick) {
      this.suppressNextClick = false;
      this.pressClosesMenu = false;
      event.preventDefault();
      return;
    }
    const closes = this.pressClosesMenu;
    this.pressClosesMenu = false;
    if (this.menuKey === key && this.menuIsOpen()) {
      this.closeMenu();
      return;
    }
    if (closes) {
      // The press light-dismissed this handle's own menu.
      return;
    }
    this.openMenu(key);
  }

  onMenuOption(option: MoveDirection | MoveEnd): void {
    const key = this.menuKey;
    if (key === null) {
      return;
    }
    const blocked = option === 'top' || option === 'up' ? this.menuAtTop : this.menuAtBottom;
    if (blocked) {
      return;
    }
    if (option === 'up' || option === 'down') {
      this.move(key, option);
    } else {
      this.moveTo(key, option);
    }
  }

  onMenuKeydown(event: KeyboardEvent): void {
    if (event.key !== 'Escape' || this.menuKey === null) {
      return;
    }
    // Also keeps an enclosing dialog from closing.
    event.preventDefault();
    event.stopPropagation();
    const key = this.menuKey;
    this.closeMenu();
    document.getElementById(`${this.rowId(key)}-handle`)?.focus();
  }

  /** A light dismiss or a native close request; explicit closes have already cleared `menuKey`. */
  onMenuToggle(event: ToggleEvent): void {
    if (event.newState !== 'closed' || this.menuIsOpen() || this.menuKey === null) {
      return;
    }
    const key = this.menuKey;
    this.menuKey = null;
    this.cdr.markForCheck();
    this.focusHandleIfLost(key);
  }

  private openMenu(key: string): void {
    const menu = this.menu?.nativeElement;
    if (!menu) {
      return;
    }
    this.menuKey = key;
    this.cdr.markForCheck();
    this.cdr.detectChanges();
    refreshAnchorPositioning();
    if (!this.menuIsOpen()) {
      const handle = document.getElementById(`${this.rowId(key)}-handle`);
      try {
        (menu.showPopover as (this: HTMLElement, options?: { source?: HTMLElement | null }) => void).call(menu, { source: handle });
      } catch {
        try {
          menu.showPopover();
        } catch {
          // Disconnected or not a popover: the menu stays closed.
        }
      }
    }
    const option = menu.querySelector<HTMLElement>('.rl-move-option:not([aria-disabled="true"])');
    option?.focus();
  }

  private closeMenu(): void {
    if (this.menuKey === null) {
      return;
    }
    this.menuKey = null;
    this.cdr.markForCheck();
    if (this.menuIsOpen()) {
      try {
        this.menu!.nativeElement.hidePopover();
      } catch {
        // Already hidden.
      }
    }
  }

  private menuIsOpen(): boolean {
    const menu = this.menu?.nativeElement;
    if (!menu) {
      return false;
    }
    try {
      return menu.matches(':popover-open');
    } catch {
      return false;
    }
  }

  /** Focus left in the closed menu, or dropped to the body, goes back to the row's handle. */
  private focusHandleIfLost(key: string): void {
    const active = document.activeElement;
    const lost = !active || active === document.body || !!this.menu?.nativeElement.contains(active);
    if (lost) {
      document.getElementById(`${this.rowId(key)}-handle`)?.focus();
    }
  }

  onCheckboxChange(item: ReorderableListItem, event: Event): void {
    const input = event.target as HTMLInputElement;
    const checked = input.checked;
    // The box shows the input's state until the host passes the change back.
    input.checked = !!item.locked || !!item.checked;
    if (item.locked) {
      return;
    }
    this.pendingFocus = null;
    this.checkedChange.emit({ key: item.key, checked });
  }

  private commit(from: number, to: number): void {
    const keys = this.items.map(item => item.key);
    const label = this.items[from].label;
    this.status = `${label} moved to position ${to + 1} of ${keys.length}.`;
    if (this.menuKey !== null) {
      // The open menu follows its handle to the new position.
      this.anchorsStale = true;
    }
    this.cdr.markForCheck();
    this.orderChange.emit(moveEntry(keys, from, to));
  }

  // Pointer drag. The listeners run outside Angular: a move is pure DOM, and only a drop re-enters.

  private onPointerDown = (event: PointerEvent): void => {
    this.suppressNextClick = false;
    this.pressClosesMenu = false;
    if (this.drag || !event.isPrimary || event.button !== 0) {
      return;
    }
    const list = this.list?.nativeElement;
    const grip = (event.target as Element | null)?.closest?.('.rl-grip') as HTMLElement | null;
    const row = grip?.closest('li');
    if (!list || !grip || !row || row.parentElement !== list) {
      return;
    }
    const rows = (Array.from(list.children) as HTMLElement[]).filter(child => child.classList.contains('rl-row'));
    const from = rows.indexOf(row);
    if (from < 0 || rows.length !== this.items.length) {
      return;
    }

    // Keeps the press from selecting text or starting a native drag. A cancelled pointerdown does
    // not focus the button in every engine, so it is focused here.
    event.preventDefault();
    try {
      grip.setPointerCapture(event.pointerId);
    } catch {
      // The pointer is no longer active; the host listeners still see its events while over the list.
    }
    grip.focus({ preventScroll: true });
    this.pressClosesMenu = this.menuKey === this.items[from].key && this.menuIsOpen();
    this.press = { pointerId: event.pointerId, grip, row, startY: event.clientY, dragged: false };
  };

  /** Turns a press that has moved past the threshold into a drag. False when the list cannot be dragged. */
  private beginDrag(press: PressState): boolean {
    const list = this.list?.nativeElement;
    if (!list || press.row.parentElement !== list) {
      return false;
    }
    const children = Array.from(list.children) as HTMLElement[];
    const rows = children.filter(child => child.classList.contains('rl-row'));
    const from = rows.indexOf(press.row);
    if (from < 0 || rows.length < 2 || rows.length !== this.items.length) {
      return false;
    }

    if (this.settleTimer !== null) {
      clearTimeout(this.settleTimer);
      this.settleTimer = null;
    }
    this.clearTransforms(list);

    // offsetTop and offsetHeight ignore transforms, so a row still settling measures at its slot.
    const divider = children.find(child => child.classList.contains('rl-divider')) ?? null;
    const dividerNext = divider?.nextElementSibling ?? null;
    const gap = children.length > 1
      ? Math.max(0, children[1].offsetTop - (children[0].offsetTop + children[0].offsetHeight))
      : 0;
    const scroller = findScroller(list);

    this.pendingFocus = null;
    this.drag = {
      pointerId: press.pointerId,
      grip: press.grip,
      list,
      keys: this.items.map(item => item.key),
      from,
      rows,
      tops: rows.map(r => r.offsetTop),
      heights: rows.map(r => r.offsetHeight),
      divider,
      dividerAt: divider ? rows.indexOf(dividerNext as HTMLElement) : -1,
      dividerTop: divider?.offsetTop ?? 0,
      dividerHeight: divider?.offsetHeight ?? 0,
      firstTop: children[0].offsetTop,
      gap,
      startY: press.startY,
      scroller,
      startScroll: scroller.scrollTop,
      pointerY: press.startY,
      to: from
    };
    list.classList.add('is-sorting');
    press.row.classList.add('is-dragging');
    document.addEventListener('keydown', this.onKeydown, true);
    return true;
  }

  private onPointerMove = (event: PointerEvent): void => {
    const press = this.press;
    if (press && !press.dragged && event.pointerId === press.pointerId) {
      if (Math.abs(event.clientY - press.startY) < DRAG_START_PX) {
        return;
      }
      press.dragged = true;
      this.suppressNextClick = true;
      if (!this.beginDrag(press)) {
        return;
      }
      if (this.menuKey !== null) {
        this.zone.run(() => this.closeMenu());
      }
    }
    const drag = this.drag;
    if (!drag || event.pointerId !== drag.pointerId) {
      return;
    }
    drag.pointerY = event.clientY;
    this.updateDrag(drag);
    this.startAutoscroll();
  };

  private onPointerUp = (event: PointerEvent): void => {
    if (this.drag && event.pointerId === this.drag.pointerId) {
      this.endDrag(true, true);
    }
    this.endPress(event);
  };

  private onPointerCancel = (event: PointerEvent): void => {
    if (this.drag && event.pointerId === this.drag.pointerId) {
      this.endDrag(false, true);
    }
    this.endPress(event);
  };

  /**
   * Forgets a finished press. A press that never moved past the threshold leaves the native click
   * to open the menu. The flags that tell that click what the press did outlive it by one task
   * only, so a later keyboard press on the handle is not misread.
   */
  private endPress(event: PointerEvent): void {
    const press = this.press;
    if (!press || event.pointerId !== press.pointerId) {
      return;
    }
    this.press = null;
    if (this.suppressClearTimer !== null) {
      clearTimeout(this.suppressClearTimer);
    }
    this.suppressClearTimer = setTimeout(() => {
      this.suppressClearTimer = null;
      this.suppressNextClick = false;
      this.pressClosesMenu = false;
    });
  }

  private onKeydown = (event: KeyboardEvent): void => {
    if (event.key !== 'Escape' || !this.drag) {
      return;
    }
    // Also keeps an enclosing dialog from closing.
    event.preventDefault();
    event.stopPropagation();
    this.endDrag(false, true);
  };

  /** Follows the pointer with the dragged row, and opens the gap where it would land. */
  private updateDrag(drag: DragState): void {
    const dy = drag.pointerY - drag.startY + (drag.scroller.scrollTop - drag.startScroll);
    drag.rows[drag.from].style.transform = `translateY(${dy}px)`;

    const center = drag.tops[drag.from] + drag.heights[drag.from] / 2 + dy;
    let to = 0;
    for (let i = 0; i < drag.rows.length; i++) {
      if (i !== drag.from && drag.tops[i] + drag.heights[i] / 2 < center) {
        to++;
      }
    }
    if (to !== drag.to) {
      drag.to = to;
      this.layoutOthers(drag);
    }
  }

  /** Stacks the rows and the divider in the order a drop would give, from the rects taken at drag start. */
  private layoutOthers(drag: DragState): void {
    const order = moveEntry(drag.rows.map((_, i) => i), drag.from, drag.to);
    let cursor = drag.firstTop;
    for (let position = 0; position < order.length; position++) {
      if (drag.divider && position === drag.dividerAt) {
        setOffset(drag.divider, cursor - drag.dividerTop);
        cursor += drag.dividerHeight + drag.gap;
      }
      const index = order[position];
      if (index !== drag.from) {
        setOffset(drag.rows[index], cursor - drag.tops[index]);
      }
      cursor += drag.heights[index] + drag.gap;
    }
  }

  /**
   * Ends a drag. A committed drop that changed the order snaps the rows into place without a
   * transition and emits once; anything else puts the rows back, animated where motion is allowed.
   */
  private endDrag(commit: boolean, animate: boolean): void {
    const drag = this.drag;
    if (!drag) {
      return;
    }
    this.drag = null;
    document.removeEventListener('keydown', this.onKeydown, true);
    if (this.scrollFrame !== null) {
      cancelAnimationFrame(this.scrollFrame);
      this.scrollFrame = null;
    }
    try {
      if (drag.grip.hasPointerCapture?.(drag.pointerId)) {
        drag.grip.releasePointerCapture(drag.pointerId);
      }
    } catch {
      // Already released.
    }

    const { list } = drag;
    drag.rows[drag.from].classList.remove('is-dragging');
    if (commit && drag.to !== drag.from) {
      // Without .is-sorting the rows have no transition, so they land before the host re-renders.
      list.classList.remove('is-sorting');
      this.clearTransforms(list);
      this.zone.run(() => this.commit(drag.from, drag.to));
      return;
    }

    this.clearTransforms(list);
    if (!animate) {
      list.classList.remove('is-sorting');
      return;
    }
    this.settleTimer = setTimeout(() => {
      this.settleTimer = null;
      list.classList.remove('is-sorting');
    }, SETTLE_MS);
  }

  private clearTransforms(list: HTMLElement): void {
    for (const child of Array.from(list.children) as HTMLElement[]) {
      child.style.transform = '';
    }
  }

  private startAutoscroll(): void {
    if (this.scrollFrame !== null || !this.drag || autoscrollStep(this.drag) === 0) {
      return;
    }
    this.scrollFrame = requestAnimationFrame(this.onScrollFrame);
  }

  private onScrollFrame = (): void => {
    this.scrollFrame = null;
    const drag = this.drag;
    if (!drag) {
      return;
    }
    const step = autoscrollStep(drag);
    if (step === 0) {
      return;
    }
    const before = drag.scroller.scrollTop;
    drag.scroller.scrollTop = before + step;
    if (drag.scroller.scrollTop === before) {
      // At the end of the scroll range; the next pointer move tries again.
      return;
    }
    this.updateDrag(drag);
    this.scrollFrame = requestAnimationFrame(this.onScrollFrame);
  };
}

function setOffset(element: HTMLElement, offset: number): void {
  element.style.transform = offset === 0 ? '' : `translateY(${offset}px)`;
}

/** The nearest ancestor that scrolls vertically, or the document's scroller. */
function findScroller(element: HTMLElement): Element {
  for (let node = element.parentElement; node; node = node.parentElement) {
    const { overflowY } = getComputedStyle(node);
    if ((overflowY === 'auto' || overflowY === 'scroll' || overflowY === 'overlay') &&
        node.scrollHeight > node.clientHeight) {
      return node;
    }
  }
  return document.scrollingElement ?? document.documentElement;
}

/** The scroller's visible top and bottom in viewport coordinates, clipped to the viewport. */
function visibleEdges(scroller: Element): { top: number; bottom: number } {
  const viewportBottom = window.innerHeight;
  if (scroller === document.scrollingElement || scroller === document.documentElement || scroller === document.body) {
    return { top: 0, bottom: viewportBottom };
  }
  const rect = scroller.getBoundingClientRect();
  const top = rect.top + scroller.clientTop;
  return { top: Math.max(0, top), bottom: Math.min(viewportBottom, top + scroller.clientHeight) };
}

/** Pixels to scroll this frame: negative near the top edge, positive near the bottom, faster nearer the edge. */
function autoscrollStep(drag: DragState): number {
  const { top, bottom } = visibleEdges(drag.scroller);
  const y = drag.pointerY;
  if (y < top + AUTOSCROLL_EDGE_PX) {
    return -Math.ceil(AUTOSCROLL_MAX_STEP_PX * Math.min(1, (top + AUTOSCROLL_EDGE_PX - y) / AUTOSCROLL_EDGE_PX));
  }
  if (y > bottom - AUTOSCROLL_EDGE_PX) {
    return Math.ceil(AUTOSCROLL_MAX_STEP_PX * Math.min(1, (y - (bottom - AUTOSCROLL_EDGE_PX)) / AUTOSCROLL_EDGE_PX));
  }
  return 0;
}
