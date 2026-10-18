import {
  elapsedMs,
  formatBytes,
  formatDateTime,
  formatDuration,
  formatNumber,
  formatRelative,
  formatTime,
  isoDateTime,
  isoDuration,
  parseTimestamp,
  shortSha
} from './devtools-format';

const nf = new Intl.NumberFormat();
const one = new Intl.NumberFormat(undefined, { minimumFractionDigits: 1, maximumFractionDigits: 1 });

describe('devtools-format', () => {
  afterEach(() => {
    vi.useRealTimers();
  });

  describe('parseTimestamp', () => {
    it('reads a UTC timestamp with or without the trailing Z', () => {
      expect(parseTimestamp('2026-10-18T12:00:00Z')?.toISOString()).toBe('2026-10-18T12:00:00.000Z');
      expect(parseTimestamp('2026-10-18T12:00:00')?.toISOString()).toBe('2026-10-18T12:00:00.000Z');
    });

    it('returns null for a missing or unparseable value', () => {
      expect(parseTimestamp(null)).toBeNull();
      expect(parseTimestamp(undefined)).toBeNull();
      expect(parseTimestamp('')).toBeNull();
      expect(parseTimestamp('not a date')).toBeNull();
    });
  });

  describe('formatNumber', () => {
    it('groups digits in the reader locale and shows a dash for a missing value', () => {
      expect(formatNumber(2050)).toBe(nf.format(2050));
      expect(formatNumber(0)).toBe(nf.format(0));
      expect(formatNumber(null)).toBe('—');
      expect(formatNumber(undefined)).toBe('—');
    });
  });

  describe('formatDuration', () => {
    it('uses ms below a second, one-decimal seconds below a minute', () => {
      expect(formatDuration(850)).toBe(`${nf.format(850)} ms`);
      expect(formatDuration(4200)).toBe('4.2 s');
    });

    it('uses minutes and padded seconds, then hours and padded minutes', () => {
      expect(formatDuration(187000)).toBe('3 min 07 s');
      expect(formatDuration(3900000)).toBe('1 h 05 min');
    });

    it('shows a dash for a missing or negative value', () => {
      expect(formatDuration(null)).toBe('—');
      expect(formatDuration(undefined)).toBe('—');
      expect(formatDuration(-1)).toBe('—');
    });
  });

  describe('formatBytes', () => {
    it('gives KB with one decimal below 1 MB', () => {
      expect(formatBytes(0)).toBe(`${one.format(0)} KB`);
      expect(formatBytes(1536)).toBe(`${one.format(1.5)} KB`);
      expect(formatBytes(524288)).toBe(`${one.format(512)} KB`);
    });

    it('gives MB with one decimal from 1 MB, and for a size that rounds up to it', () => {
      expect(formatBytes(1024 * 1024)).toBe(`${one.format(1)} MB`);
      expect(formatBytes(1.5 * 1024 * 1024)).toBe(`${one.format(1.5)} MB`);
      expect(formatBytes(1024 * 1024 - 10)).toBe(`${one.format(1)} MB`);
    });

    it('shows a dash for a missing or negative value', () => {
      expect(formatBytes(null)).toBe('—');
      expect(formatBytes(undefined)).toBe('—');
      expect(formatBytes(-1)).toBe('—');
    });
  });

  describe('dates and times', () => {
    const value = '2026-10-18T11:50:00Z';
    const date = new Date(value);

    it('formats the local date and time, and the local time of day', () => {
      expect(formatDateTime(value)).toBe(
        new Intl.DateTimeFormat(undefined, { dateStyle: 'medium', timeStyle: 'medium' }).format(date));
      expect(formatTime(value)).toBe(new Intl.DateTimeFormat(undefined, { timeStyle: 'medium' }).format(date));
    });

    it('returns empty text for a missing timestamp', () => {
      expect(formatDateTime(null)).toBe('');
      expect(formatTime(undefined)).toBe('');
      expect(formatRelative(null)).toBe('');
    });

    it('gives the ISO instant for a datetime attribute, or null', () => {
      expect(isoDateTime('2026-10-18T11:50:00')).toBe('2026-10-18T11:50:00.000Z');
      expect(isoDateTime(null)).toBeNull();
    });

    it('describes a timestamp relative to now in seconds, minutes, hours and days', () => {
      vi.useFakeTimers();
      vi.setSystemTime(new Date('2026-10-18T12:00:00Z'));
      const rtf = new Intl.RelativeTimeFormat(undefined, { numeric: 'auto' });

      expect(formatRelative('2026-10-18T11:59:30Z')).toBe(rtf.format(0, 'second'));
      expect(formatRelative('2026-10-18T11:57:00Z')).toBe(rtf.format(-3, 'minute'));
      expect(formatRelative('2026-10-18T12:05:00Z')).toBe(rtf.format(5, 'minute'));
      expect(formatRelative('2026-10-18T09:00:00Z')).toBe(rtf.format(-3, 'hour'));
      expect(formatRelative('2026-10-16T12:00:00Z')).toBe(rtf.format(-2, 'day'));
    });
  });

  describe('isoDuration', () => {
    it('gives seconds with one decimal and never a negative duration', () => {
      expect(isoDuration(4200)).toBe('PT4.2S');
      expect(isoDuration(-5)).toBe('PT0.0S');
    });
  });

  describe('shortSha', () => {
    it('keeps the first 10 characters, or shows a dash', () => {
      expect(shortSha('0123456789abcdef')).toBe('0123456789');
      expect(shortSha(null)).toBe('—');
      expect(shortSha('')).toBe('—');
    });
  });

  describe('elapsedMs', () => {
    it('measures a finished job from start to finish', () => {
      expect(elapsedMs('2026-10-18T12:00:00Z', '2026-10-18T12:00:05Z')).toBe(5000);
    });

    it('measures a running job to now', () => {
      vi.useFakeTimers();
      vi.setSystemTime(new Date('2026-10-18T12:00:10Z'));
      expect(elapsedMs('2026-10-18T12:00:00Z', null)).toBe(10000);
    });

    it('returns 0 for an unparseable timestamp or a finish before the start', () => {
      expect(elapsedMs('nonsense', '2026-10-18T12:00:05Z')).toBe(0);
      expect(elapsedMs('2026-10-18T12:00:05Z', '2026-10-18T12:00:00Z')).toBe(0);
    });
  });
});
