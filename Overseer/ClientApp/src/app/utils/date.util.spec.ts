import { parseServerUtcDate, elapsedMsBetween } from './date.util';

describe('date.util', () => {
  describe('parseServerUtcDate', () => {
    it('should parse an ISO timestamp without timezone designator as UTC', () => {
      const date = parseServerUtcDate('2026-09-02T17:00:00');
      // Assert via Date.UTC to be timezone-independent (Month 8 = September)
      expect(date.getTime()).toBe(Date.UTC(2026, 8, 2, 17, 0, 0));
    });

    it('should parse a space-separated timestamp without timezone designator as UTC', () => {
      const date = parseServerUtcDate('2026-09-02 17:00:00');
      expect(date.getTime()).toBe(Date.UTC(2026, 8, 2, 17, 0, 0));
    });

    it('should preserve existing Z suffix without modification', () => {
      const date = parseServerUtcDate('2026-09-02T17:00:00Z');
      expect(date.getTime()).toBe(Date.UTC(2026, 8, 2, 17, 0, 0));
    });

    it('should respect positive offset (+03:00) without appending Z', () => {
      const date = parseServerUtcDate('2026-09-02T17:00:00+03:00');
      // 17:00 UTC+3 is 14:00 UTC
      expect(date.getTime()).toBe(Date.UTC(2026, 8, 2, 14, 0, 0));
    });

    it('should respect negative offset (-05:00) without appending Z', () => {
      const date = parseServerUtcDate('2026-09-02T17:00:00-05:00');
      // 17:00 UTC-5 is 22:00 UTC
      expect(date.getTime()).toBe(Date.UTC(2026, 8, 2, 22, 0, 0));
    });

    it('should return a Date instance directly without modification', () => {
      const original = new Date(Date.UTC(2026, 8, 2, 12, 0, 0));
      const result = parseServerUtcDate(original);
      expect(result).toBe(original);
      expect(result.getTime()).toBe(original.getTime());
    });
  });

  describe('elapsedMsBetween', () => {
    it('should calculate elapsed milliseconds between two UTC timestamps', () => {
      const start = '2026-09-02T17:00:00';
      const end = '2026-09-02T17:05:26';
      // 5 minutes 26 seconds = 326 seconds = 326,000 ms
      expect(elapsedMsBetween(start, end)).toBe(326000);
    });

    it('should clamp future start time to 0 when end time is before start time', () => {
      const start = '2026-09-02T18:00:00';
      const end = '2026-09-02T17:00:00';
      expect(elapsedMsBetween(start, end)).toBe(0);
    });

    it('should calculate elapsed from startUtc to now when endUtc is omitted', () => {
      const fiveSecondsAgo = new Date(Date.now() - 5000).toISOString().replace('Z', '');
      const elapsed = elapsedMsBetween(fiveSecondsAgo);
      // Allow slight timing tolerance (4500ms - 6500ms)
      expect(elapsed).toBeGreaterThanOrEqual(4500);
      expect(elapsed).toBeLessThanOrEqual(6500);
    });

    it('should clamp future startUtc to 0 when endUtc is omitted and start is in future', () => {
      const future = new Date(Date.now() + 60000).toISOString().replace('Z', '');
      expect(elapsedMsBetween(future)).toBe(0);
    });

    it('should return 0 when startUtc is null or undefined', () => {
      expect(elapsedMsBetween(null)).toBe(0);
      expect(elapsedMsBetween(undefined)).toBe(0);
    });

    it('should return 0 when timestamps are invalid', () => {
      expect(elapsedMsBetween('invalid-date', '2026-09-02T17:00:00')).toBe(0);
    });
  });
});
