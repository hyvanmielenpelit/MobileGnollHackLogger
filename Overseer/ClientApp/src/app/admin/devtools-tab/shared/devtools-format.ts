import { parseServerUtcDate } from '../../../utils/date.util';

/*
 * Formatting for the Services tab dashboards. Every function is pure apart from the reader's locale
 * and, for `formatRelative` and a running `elapsedMs`, the clock. Server timestamps are UTC strings,
 * with or without a trailing `Z` (`parseServerUtcDate`). A missing value reads as an em dash where
 * the result is shown as a figure, and as '' or null where the caller decides what to show.
 */

const numberFormat = new Intl.NumberFormat();
const oneDecimalFormat = new Intl.NumberFormat(undefined, { minimumFractionDigits: 1, maximumFractionDigits: 1 });
const dateTimeFormat = new Intl.DateTimeFormat(undefined, { dateStyle: 'medium', timeStyle: 'medium' });
const timeFormat = new Intl.DateTimeFormat(undefined, { timeStyle: 'medium' });
const relativeFormat = new Intl.RelativeTimeFormat(undefined, { numeric: 'auto' });

const KB = 1024;
const MB = 1024 * 1024;

/** A server timestamp as a `Date`, or null when it is missing or unparseable. */
export function parseTimestamp(value: string | null | undefined): Date | null {
  if (!value) {
    return null;
  }
  const date = parseServerUtcDate(value);
  return isNaN(date.getTime()) ? null : date;
}

/** A count with the locale's grouping: "2,050", or '—' when there is none. */
export function formatNumber(value: number | null | undefined): string {
  return value === null || value === undefined ? '—' : numberFormat.format(value);
}

/** "850 ms", "4.2 s", "3 min 07 s", "1 h 05 min", or '—' when there is none or it is negative. */
export function formatDuration(ms: number | null | undefined): string {
  if (ms === null || ms === undefined || ms < 0) {
    return '—';
  }
  if (ms < 1000) {
    return `${numberFormat.format(Math.round(ms))} ms`;
  }
  const totalSeconds = ms / 1000;
  if (totalSeconds < 60) {
    return `${totalSeconds.toFixed(1)} s`;
  }
  const whole = Math.round(totalSeconds);
  const hours = Math.floor(whole / 3600);
  const minutes = Math.floor((whole % 3600) / 60);
  const seconds = whole % 60;
  const pad = (n: number) => String(n).padStart(2, '0');
  return hours > 0 ? `${hours} h ${pad(minutes)} min` : `${minutes} min ${pad(seconds)} s`;
}

/**
 * A size with one decimal: KB below 1 MB ("512.3 KB"), MB from there ("1.4 MB"), in binary units.
 * '—' when there is none or it is negative.
 */
export function formatBytes(bytes: number | null | undefined): string {
  if (bytes === null || bytes === undefined || bytes < 0) {
    return '—';
  }
  const kb = bytes / KB;
  // A size that rounds to 1,024.0 KB reads as MB.
  if (bytes >= MB || Math.round(kb * 10) >= KB * 10) {
    return `${oneDecimalFormat.format(bytes / MB)} MB`;
  }
  return `${oneDecimalFormat.format(kb)} KB`;
}

/** The local date and time of a server timestamp, or '' when there is none. */
export function formatDateTime(value: string | null | undefined): string {
  const date = parseTimestamp(value);
  return date ? dateTimeFormat.format(date) : '';
}

/** The local time of day of a server timestamp, or '' when there is none. */
export function formatTime(value: string | null | undefined): string {
  const date = parseTimestamp(value);
  return date ? timeFormat.format(date) : '';
}

/** "3 minutes ago", "in 5 minutes", or '' when there is no timestamp. */
export function formatRelative(value: string | null | undefined): string {
  const date = parseTimestamp(value);
  if (!date) {
    return '';
  }
  const seconds = Math.round((date.getTime() - Date.now()) / 1000);
  const abs = Math.abs(seconds);
  if (abs < 45) {
    return relativeFormat.format(0, 'second');
  }
  if (abs < 3600) {
    return relativeFormat.format(Math.round(seconds / 60), 'minute');
  }
  if (abs < 86400) {
    return relativeFormat.format(Math.round(seconds / 3600), 'hour');
  }
  return relativeFormat.format(Math.round(seconds / 86400), 'day');
}

/** The `datetime` attribute value of a server timestamp: the instant in ISO form, or null. */
export function isoDateTime(value: string | null | undefined): string | null {
  const date = parseTimestamp(value);
  return date ? date.toISOString() : null;
}

/** An ISO 8601 duration for a `<time datetime>`: "PT4.2S". */
export function isoDuration(ms: number): string {
  return `PT${(Math.max(0, ms) / 1000).toFixed(1)}S`;
}

/** The first 10 characters of a commit SHA, or '—' when there is none. */
export function shortSha(sha: string | null | undefined): string {
  return sha ? sha.slice(0, 10) : '—';
}

/**
 * Milliseconds from a job's start to its finish, or to now while it runs. 0 when a timestamp is
 * unparseable or the finish precedes the start.
 */
export function elapsedMs(startedAtUtc: string, finishedAtUtc: string | null | undefined): number {
  const start = parseServerUtcDate(startedAtUtc).getTime();
  const end = finishedAtUtc ? parseServerUtcDate(finishedAtUtc).getTime() : Date.now();
  return isNaN(start) || isNaN(end) || end < start ? 0 : end - start;
}
