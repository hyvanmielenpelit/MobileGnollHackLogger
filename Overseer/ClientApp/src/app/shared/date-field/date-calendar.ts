/**
 * UTC calendar-day helpers for `app-date-field`. A day is an ISO `YYYY-MM-DD` string, months are
 * 1–12, and every computation reads and writes UTC fields only, so a day never shifts with the
 * reader's time zone.
 */

/** Sunday-first weekday names, with the two-letter column headers of the calendar grid. */
export const CALENDAR_WEEKDAYS: readonly { name: string; short: string }[] = [
  { name: 'Sunday', short: 'Su' },
  { name: 'Monday', short: 'Mo' },
  { name: 'Tuesday', short: 'Tu' },
  { name: 'Wednesday', short: 'We' },
  { name: 'Thursday', short: 'Th' },
  { name: 'Friday', short: 'Fr' },
  { name: 'Saturday', short: 'Sa' }
];

export const CALENDAR_MONTHS: readonly string[] = [
  'January', 'February', 'March', 'April', 'May', 'June',
  'July', 'August', 'September', 'October', 'November', 'December'
];

/** A day's parts: `month` 1–12, `date` 1–31. */
export interface DayParts {
  year: number;
  month: number;
  date: number;
}

/** One cell of a month grid. */
export interface CalendarCell {
  day: string;
  /** The day of the month, as the cell shows it. */
  date: number;
  /** False for the leading and trailing days of the neighboring months. */
  inMonth: boolean;
}

/** The keys `moveDay` handles on the calendar grid. */
export type CalendarKey = 'ArrowLeft' | 'ArrowRight' | 'ArrowUp' | 'ArrowDown' | 'Home' | 'End' | 'PageUp' | 'PageDown';

const DAY_PATTERN = /^(\d{4})-(\d{2})-(\d{2})$/;
/** `2026-9-5`, `2026/9/5`, `2026.9.5`: one separator, used twice. */
const LOOSE_DAY_PATTERN = /^(\d{4})([-/.])(\d{1,2})\2(\d{1,2})$/;

/** Midnight UTC of a day; out-of-range months and dates roll over as `Date.UTC` does. */
function utcDate(year: number, month: number, date: number): Date {
  const result = new Date(0);
  // setUTCFullYear, unlike Date.UTC, does not map years 0–99 to 1900–1999.
  result.setUTCFullYear(year, month - 1, date);
  return result;
}

function pad(value: number, width: number): string {
  return String(value).padStart(width, '0');
}

/** `2026-10-07` from its parts, without validating them. */
export function partsToDay(year: number, month: number, date: number): string {
  return `${pad(year, 4)}-${pad(month, 2)}-${pad(date, 2)}`;
}

/** The UTC calendar day of an instant. */
export function dateToDay(value: Date): string {
  return partsToDay(value.getUTCFullYear(), value.getUTCMonth() + 1, value.getUTCDate());
}

/** Today, as a UTC calendar day. */
export function todayUtc(now: Date = new Date()): string {
  return dateToDay(now);
}

/** The parts of a real calendar day in `YYYY-MM-DD` form; null for anything else. */
export function parseDay(text: string | null | undefined): DayParts | null {
  const match = DAY_PATTERN.exec(text ?? '');
  if (!match) {
    return null;
  }
  const year = Number(match[1]);
  const month = Number(match[2]);
  const date = Number(match[3]);
  if (year < 1 || month < 1 || month > 12 || date < 1 || date > daysInMonth(year, month)) {
    return null;
  }
  return { year, month, date };
}

export function isDay(text: string | null | undefined): boolean {
  return parseDay(text) !== null;
}

export function daysInMonth(year: number, month: number): number {
  return utcDate(year, month + 1, 0).getUTCDate();
}

/** The day `date` of the month, or the month's last day when the month is shorter. */
export function clampDay(year: number, month: number, date: number): string {
  // Normalizes a month outside 1–12 into the right year first.
  const first = utcDate(year, month, 1);
  const y = first.getUTCFullYear();
  const m = first.getUTCMonth() + 1;
  return partsToDay(y, m, Math.min(Math.max(date, 1), daysInMonth(y, m)));
}

function requireParts(day: string): DayParts {
  const parts = parseDay(day);
  if (!parts) {
    throw new RangeError(`Not a calendar day: ${day}`);
  }
  return parts;
}

export function addDays(day: string, days: number): string {
  const { year, month, date } = requireParts(day);
  return dateToDay(utcDate(year, month, date + days));
}

/** The same day `months` months on, clamped to the end of a shorter month. */
export function addMonths(day: string, months: number): string {
  const { year, month, date } = requireParts(day);
  return clampDay(year, month + months, date);
}

/** The same day `years` years on; February 29 becomes February 28 outside a leap year. */
export function addYears(day: string, years: number): string {
  const { year, month, date } = requireParts(day);
  return clampDay(year + years, month, date);
}

/** 0 for Sunday through 6 for Saturday. */
export function weekdayOf(day: string): number {
  const { year, month, date } = requireParts(day);
  return utcDate(year, month, date).getUTCDay();
}

/** Six Sunday-first weeks of seven days that hold the month, padded with its neighbors' days. */
export function monthGrid(year: number, month: number): CalendarCell[][] {
  const first = partsToDay(year, month, 1);
  let cursor = addDays(first, -weekdayOf(first));
  const weeks: CalendarCell[][] = [];
  for (let w = 0; w < 6; w++) {
    const week: CalendarCell[] = [];
    for (let d = 0; d < 7; d++) {
      const parts = requireParts(cursor);
      week.push({ day: cursor, date: parts.date, inMonth: parts.year === year && parts.month === month });
      cursor = addDays(cursor, 1);
    }
    weeks.push(week);
  }
  return weeks;
}

/**
 * The day a grid key moves to: Left / Right ±1 day, Up / Down ±7, Home / End the week's Sunday /
 * Saturday, PageUp / PageDown ±1 month, with `shift` ±1 year (both clamped to the month's end).
 * Null for any other key.
 */
export function moveDay(day: string, key: string, shift = false): string | null {
  switch (key as CalendarKey) {
    case 'ArrowLeft': return addDays(day, -1);
    case 'ArrowRight': return addDays(day, 1);
    case 'ArrowUp': return addDays(day, -7);
    case 'ArrowDown': return addDays(day, 7);
    case 'Home': return addDays(day, -weekdayOf(day));
    case 'End': return addDays(day, 6 - weekdayOf(day));
    case 'PageUp': return shift ? addYears(day, -1) : addMonths(day, -1);
    case 'PageDown': return shift ? addYears(day, 1) : addMonths(day, 1);
    default: return null;
  }
}

/** `Wednesday, October 7, 2026`. */
export function dayLabel(day: string): string {
  const { year, month, date } = requireParts(day);
  return `${CALENDAR_WEEKDAYS[weekdayOf(day)].name}, ${CALENDAR_MONTHS[month - 1]} ${date}, ${year}`;
}

/** `October 2026`. */
export function monthTitle(year: number, month: number): string {
  return `${CALENDAR_MONTHS[month - 1]} ${year}`;
}

/** True when the day is within `min` and `max`, inclusive; a bound that is not a day is open. */
export function dayInRange(day: string, min?: string | null, max?: string | null): boolean {
  if (isDay(min) && day < min!) {
    return false;
  }
  if (isDay(max) && day > max!) {
    return false;
  }
  return true;
}

/**
 * Typed text as the field emits it: trimmed, and `2026/9/5`, `2026.9.5` or `2026-9-5` written
 * `2026-09-05` when that is a real day. Anything else is returned as typed, trimmed.
 */
export function normalizeDayInput(text: string): string {
  const trimmed = (text ?? '').trim();
  const match = LOOSE_DAY_PATTERN.exec(trimmed);
  if (!match) {
    return trimmed;
  }
  const day = partsToDay(Number(match[1]), Number(match[3]), Number(match[4]));
  return isDay(day) ? day : trimmed;
}
