/**
 * Parses a timestamp emitted by the Overseer API. Those come from `DateTime` values with
 * `DateTimeKind.Unspecified`, which System.Text.Json writes with no `Z` and no offset —
 * and ECMAScript reads an offset-less date-time as LOCAL time, not UTC. Appending `Z`
 * when nothing else designates a zone restores the intended instant.
 */
export function parseServerUtcDate(value: string | Date): Date {
  if (value instanceof Date) {
    return value;
  }
  let dateStr = value;
  if (typeof dateStr === 'string' && !dateStr.endsWith('Z') && !dateStr.includes('+') && !dateStr.match(/-\d{2}:\d{2}$/)) {
    if (dateStr.length > 10 || dateStr.includes('T') || dateStr.includes(' ')) {
      dateStr += 'Z';
    }
  }
  return new Date(dateStr);
}

/**
 * Local calendar date for a stored UTC timestamp (read with `parseServerUtcDate`), or '' when
 * nothing has been recorded or the value does not parse.
 */
export function formatServerDate(iso: string | null | undefined): string {
  if (!iso) return '';
  const parsed = parseServerUtcDate(iso);
  return isNaN(parsed.getTime()) ? '' : parsed.toLocaleDateString();
}

/**
 * A date entered as a calendar date (stored as its midnight, with no zone), shown as that same
 * date in every time zone, or '' when nothing has been recorded. Read as local time on purpose,
 * unlike `formatServerDate`: appending `Z` would shift the date west of UTC.
 */
export function formatCalendarDate(iso: string | null | undefined): string {
  if (!iso) return '';
  const parsed = new Date(iso);
  return isNaN(parsed.getTime()) ? '' : parsed.toLocaleDateString();
}

/**
 * Milliseconds from a server timestamp to `end` (default: now), clamped at 0 so client
 * clock skew reads as "just started" rather than as a negative duration.
 */
export function elapsedMsBetween(startUtc: string | null | undefined, endUtc?: string | null): number {
  if (!startUtc) {
    return 0;
  }
  const start = parseServerUtcDate(startUtc).getTime();
  const end = endUtc ? parseServerUtcDate(endUtc).getTime() : Date.now();
  if (isNaN(start) || isNaN(end) || end < start) {
    return 0;
  }
  return end - start;
}
