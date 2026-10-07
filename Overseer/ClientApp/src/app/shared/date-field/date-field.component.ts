import {
  ChangeDetectionStrategy,
  ChangeDetectorRef,
  Component,
  ElementRef,
  EventEmitter,
  Input,
  OnChanges,
  OnInit,
  Output,
  SimpleChanges,
  inject
} from '@angular/core';

import { ensureOverlayPolyfills, refreshAnchorPositioning } from '../../utils/polyfills.util';
import {
  CALENDAR_WEEKDAYS,
  CalendarCell,
  addMonths,
  dayInRange,
  dayLabel,
  isDay,
  monthGrid,
  monthTitle,
  moveDay,
  normalizeDayInput,
  parseDay,
  todayUtc
} from './date-calendar';

/** A grid cell with the name its day button reads. */
interface DateFieldCell extends CalendarCell {
  label: string;
}

/**
 * A `YYYY-MM-DD` text field with a calendar button that opens a glass calendar popover: the
 * WAI-ARIA APG Date Picker Dialog, non-modal because it is a light-dismiss popover.
 *
 * The host owns the value and its validation, keeps its own `<label for="{inputId}">`, and feeds
 * the value back. `valueChange` is emitted on the input's `change` (normalized), on a calendar
 * pick, and on Today and Clear — never per keystroke. Every id and anchor name derives from
 * `inputId`. All dates are UTC calendar days. Escape closes the calendar only, so a dialog around
 * the field stays open.
 */
@Component({
  selector: 'app-date-field',
  standalone: true,
  templateUrl: './date-field.component.html',
  styleUrls: ['./date-field.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class DateFieldComponent implements OnInit, OnChanges {
  private readonly cdr = inject(ChangeDetectorRef);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);

  /** The input's id, unique in the document; every other id and both anchor names derive from it. */
  @Input({ required: true }) inputId!: string;
  /** The visible label text, as the host's `<label>` shows it: `From (UTC)`. */
  @Input() label = '';
  /** `YYYY-MM-DD` or `''`. */
  @Input() value = '';
  /** The first day the calendar lets the user choose, `YYYY-MM-DD`; anything else is no bound. */
  @Input() min: string | null = null;
  /** The last day the calendar lets the user choose, `YYYY-MM-DD`; anything else is no bound. */
  @Input() max: string | null = null;
  /** Extra ids for the input's `aria-describedby`, after the field's own format hint. */
  @Input() describedBy: string | null = null;
  @Input() invalid = false;
  @Input() readonly = false;

  @Output() readonly valueChange = new EventEmitter<string>();

  readonly weekdays = CALENDAR_WEEKDAYS;

  /** Whether the calendar is open; the popover polyfill does not set `aria-expanded`. */
  open = false;
  /** Today as a UTC day, refreshed whenever the calendar opens. */
  today = todayUtc();
  /** The month the grid shows. */
  viewYear = 0;
  viewMonth = 0;
  /** The day button that holds the grid's single tab stop. */
  focusedDay = '';

  private weeksKey = '';
  private weeksCache: DateFieldCell[][] = [];

  constructor() {
    this.showMonthOf(this.today);
  }

  ngOnInit(): void {
    ensureOverlayPolyfills();
  }

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['readonly'] && this.readonly && this.open) {
      this.hideCalendar();
    }
  }

  get calendarId(): string {
    return `${this.inputId}-cal`;
  }

  get describedByIds(): string {
    return [`${this.inputId}-format`, this.describedBy].filter((id): id is string => !!id).join(' ');
  }

  /** The chosen day, when the value is one. */
  get selectedDay(): string {
    return isDay(this.value) ? this.value : '';
  }

  get title(): string {
    return monthTitle(this.viewYear, this.viewMonth);
  }

  get weeks(): DateFieldCell[][] {
    const key = `${this.viewYear}-${this.viewMonth}`;
    if (key !== this.weeksKey) {
      this.weeksKey = key;
      this.weeksCache = monthGrid(this.viewYear, this.viewMonth)
        .map(week => week.map(cell => ({ ...cell, label: dayLabel(cell.day) })));
    }
    return this.weeksCache;
  }

  get todayAllowed(): boolean {
    return !this.isDisabled(this.today);
  }

  isDisabled(day: string): boolean {
    return !dayInRange(day, this.min, this.max);
  }

  onInputChange(event: Event): void {
    const input = event.target as HTMLInputElement;
    const text = normalizeDayInput(input.value);
    input.value = text;
    this.valueChange.emit(text);
  }

  /** A read-only field's button has no popover target and opens nothing. */
  onButtonClick(event: MouseEvent): void {
    if (this.readonly) {
      event.preventDefault();
    }
  }

  onToggle(event: Event): void {
    this.open = (event as ToggleEvent).newState === 'open';
    if (this.open) {
      if (this.readonly) {
        this.open = false;
        this.hideCalendar();
        return;
      }
      this.today = todayUtc();
      const start = this.initialDay();
      this.focusedDay = start;
      this.showMonthOf(start);
      this.cdr.detectChanges();
      refreshAnchorPositioning();
      this.dayButton(start)?.focus();
      return;
    }
    this.cdr.markForCheck();
    const active = document.activeElement;
    if (!active || active === document.body || !!this.calendar()?.contains(active)) {
      this.calendarButton()?.focus();
    }
  }

  /** Escape closes the calendar only; a dialog around the field stays open. */
  onCalendarKeydown(event: KeyboardEvent): void {
    if (event.key !== 'Escape') {
      return;
    }
    event.preventDefault();
    event.stopPropagation();
    this.close();
  }

  onGridKeydown(event: KeyboardEvent): void {
    const target = (event.target as HTMLElement).closest<HTMLElement>('[data-day]');
    const day = target?.dataset['day'] ?? this.focusedDay;
    if (event.key === 'Enter' || event.key === ' ') {
      event.preventDefault();
      this.pick(day);
      return;
    }
    if (event.altKey || event.ctrlKey || event.metaKey) {
      return;
    }
    const next = moveDay(day, event.key, event.shiftKey);
    if (next === null) {
      return;
    }
    event.preventDefault();
    this.focusDay(next);
  }

  /** Tabbing out of the calendar closes it; the calendar button toggles it natively. */
  onCalendarFocusOut(event: FocusEvent): void {
    const next = event.relatedTarget as Node | null;
    if (!this.open || !next || this.calendar()?.contains(next) || next === this.calendarButton()) {
      return;
    }
    this.hideCalendar();
  }

  /** Previous or next month; focus stays on the button, and the title announces the month. */
  stepMonth(delta: number): void {
    const next = addMonths(this.focusedDay || this.today, delta);
    this.focusedDay = next;
    this.showMonthOf(next);
    this.cdr.markForCheck();
  }

  pick(day: string): void {
    if (!isDay(day) || this.isDisabled(day)) {
      return;
    }
    this.emit(day);
    this.close();
  }

  pickToday(): void {
    this.today = todayUtc();
    this.pick(this.today);
  }

  clear(): void {
    this.emit('');
    this.close();
  }

  private emit(value: string): void {
    const input = this.input();
    if (input) {
      input.value = value;
    }
    this.valueChange.emit(value);
  }

  /** The chosen day, else today, else the nearest enabled day: `min` or `max`. */
  private initialDay(): string {
    if (this.selectedDay) {
      return this.selectedDay;
    }
    if (!this.isDisabled(this.today)) {
      return this.today;
    }
    if (isDay(this.min) && this.today < this.min!) {
      return this.min!;
    }
    if (isDay(this.max) && this.today > this.max!) {
      return this.max!;
    }
    return this.today;
  }

  private focusDay(day: string): void {
    this.focusedDay = day;
    this.showMonthOf(day);
    this.cdr.detectChanges();
    this.dayButton(day)?.focus();
  }

  private showMonthOf(day: string): void {
    const parts = parseDay(day);
    if (parts) {
      this.viewYear = parts.year;
      this.viewMonth = parts.month;
    }
  }

  private close(): void {
    this.hideCalendar();
    this.calendarButton()?.focus();
  }

  private hideCalendar(): void {
    try {
      this.calendar()?.hidePopover();
    } catch {
      // Already hidden.
    }
  }

  private dayButton(day: string): HTMLElement | null {
    return this.calendar()?.querySelector<HTMLElement>(`[data-day="${day}"]`) ?? null;
  }

  private calendar(): HTMLElement | null {
    return this.host.nativeElement.querySelector<HTMLElement>(`[id="${this.calendarId}"]`);
  }

  private calendarButton(): HTMLElement | null {
    return this.host.nativeElement.querySelector<HTMLElement>(`[id="${this.inputId}-cal-btn"]`);
  }

  private input(): HTMLInputElement | null {
    return this.host.nativeElement.querySelector<HTMLInputElement>(`[id="${this.inputId}"]`);
  }
}
