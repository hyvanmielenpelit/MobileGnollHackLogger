import {
  CC_ALL_DATES,
  CC_RANGE_PRESETS,
  CcDateRange,
  CcRangePreset,
  ccAnchorRange,
  ccDateRangeText,
  ccPresetToCustom,
  ccRangeBounds
} from './chat-consistency-range';

const ANCHOR = '2026-10-07T12:34:56.000Z';

function rolling(preset: CcRangePreset, anchorUtc: string | null = ANCHOR): CcDateRange {
  return { preset, fromDay: '', toDay: '', anchorUtc };
}

function custom(fromDay: string, toDay: string): CcDateRange {
  return { preset: 'custom', fromDay, toDay, anchorUtc: null };
}

describe('chat-consistency-range', () => {
  it('lists the eleven presets in order, with day counts on the day windows only', () => {
    expect(CC_RANGE_PRESETS.map(entry => entry.label)).toEqual([
      'All dates', 'Last 1 day', 'Last 3 days', 'Last 7 days', 'Last 14 days', 'Last 28 days',
      'Last 30 days', 'Last 90 days', 'Last 180 days', 'Last year', 'Custom'
    ]);
    expect(CC_RANGE_PRESETS.map(entry => entry.id)).toEqual(
      ['all', '1d', '3d', '7d', '14d', '28d', '30d', '90d', '180d', '1y', 'custom']
    );
    expect(CC_RANGE_PRESETS.map(entry => entry.days)).toEqual(
      [undefined, 1, 3, 7, 14, 28, 30, 90, 180, undefined, undefined]
    );
  });

  it('is every date by default', () => {
    expect(CC_ALL_DATES).toEqual({ preset: 'all', fromDay: '', toDay: '', anchorUtc: null });
    expect(ccRangeBounds(CC_ALL_DATES)).toEqual({ fromUtc: null, toUtc: null });
  });

  describe('ccRangeBounds', () => {
    it('counts each rolling preset back from its anchor, with an open end', () => {
      const expected: [CcRangePreset, string][] = [
        ['1d', '2026-10-06T12:34:56.000Z'],
        ['3d', '2026-10-04T12:34:56.000Z'],
        ['7d', '2026-09-30T12:34:56.000Z'],
        ['14d', '2026-09-23T12:34:56.000Z'],
        ['28d', '2026-09-09T12:34:56.000Z'],
        ['30d', '2026-09-07T12:34:56.000Z'],
        ['90d', '2026-07-09T12:34:56.000Z'],
        ['180d', '2026-04-10T12:34:56.000Z'],
        ['1y', '2025-10-07T12:34:56.000Z']
      ];
      for (const [preset, fromUtc] of expected) {
        expect(ccRangeBounds(rolling(preset)), preset).toEqual({ fromUtc, toUtc: null });
      }
    });

    it('counts a day window across a leap day in whole 24-hour days', () => {
      expect(ccRangeBounds(rolling('7d', '2028-03-02T00:00:00.000Z')))
        .toEqual({ fromUtc: '2028-02-24T00:00:00.000Z', toUtc: null });
    });

    it('takes "Last year" from a leap day with setUTCFullYear(year - 1), which rolls February 29 over to March 1', () => {
      expect(ccRangeBounds(rolling('1y', '2028-02-29T08:00:00.000Z')))
        .toEqual({ fromUtc: '2027-03-01T08:00:00.000Z', toUtc: null });
    });

    it('treats a rolling preset without an anchor as unbounded', () => {
      expect(ccRangeBounds(rolling('7d', null))).toEqual({ fromUtc: null, toUtc: null });
      expect(ccRangeBounds(rolling('1y', null))).toEqual({ fromUtc: null, toUtc: null });
    });

    it('runs a custom range over whole UTC days, each bound open when empty or invalid', () => {
      expect(ccRangeBounds(custom('2026-09-01', '2026-10-05')))
        .toEqual({ fromUtc: '2026-09-01T00:00:00.000Z', toUtc: '2026-10-05T23:59:59.999Z' });
      expect(ccRangeBounds(custom('2026-09-01', ''))).toEqual({ fromUtc: '2026-09-01T00:00:00.000Z', toUtc: null });
      expect(ccRangeBounds(custom('', '2026-10-05'))).toEqual({ fromUtc: null, toUtc: '2026-10-05T23:59:59.999Z' });
      expect(ccRangeBounds(custom('', ''))).toEqual({ fromUtc: null, toUtc: null });
      expect(ccRangeBounds(custom('2026-02-30', '2026-13-01'))).toEqual({ fromUtc: null, toUtc: null });
    });

    it('ignores an anchor on a custom range', () => {
      expect(ccRangeBounds({ ...custom('', ''), anchorUtc: ANCHOR })).toEqual({ fromUtc: null, toUtc: null });
    });
  });

  describe('ccAnchorRange', () => {
    it('anchors a rolling preset at now and leaves every date and custom ranges unchanged', () => {
      const now = new Date('2026-10-08T09:15:00Z');
      expect(ccAnchorRange(rolling('7d'), now)).toEqual(rolling('7d', '2026-10-08T09:15:00.000Z'));
      expect(ccAnchorRange(rolling('1y', null), now)).toEqual(rolling('1y', '2026-10-08T09:15:00.000Z'));
      expect(ccAnchorRange(CC_ALL_DATES, now)).toBe(CC_ALL_DATES);
      const range = custom('2026-09-01', '2026-10-05');
      expect(ccAnchorRange(range, now)).toBe(range);
    });

    it('returns a new object for a rolling preset', () => {
      const range = rolling('30d');
      const anchored = ccAnchorRange(range, new Date(ANCHOR));
      expect(anchored).not.toBe(range);
      expect(range.anchorUtc).toBe(ANCHOR);
    });
  });

  describe('ccDateRangeText', () => {
    it('names the preset', () => {
      expect(ccDateRangeText(CC_ALL_DATES)).toBe('All dates');
      expect(ccDateRangeText(rolling('1d'))).toBe('Last 1 day');
      expect(ccDateRangeText(rolling('7d'))).toBe('Last 7 days');
      expect(ccDateRangeText(rolling('1y'))).toBe('Last year');
    });

    it('writes a custom range by its days', () => {
      expect(ccDateRangeText(custom('2026-09-01', '2026-10-05'))).toBe('2026-09-01 to 2026-10-05');
      expect(ccDateRangeText(custom('2026-09-01', ''))).toBe('From 2026-09-01');
      expect(ccDateRangeText(custom('', '2026-10-05'))).toBe('Until 2026-10-05');
    });

    it('reads an empty or invalid custom range as every date', () => {
      expect(ccDateRangeText(custom('', ''))).toBe('All dates');
      expect(ccDateRangeText(custom('2026-02-30', ''))).toBe('All dates');
    });
  });

  describe('ccPresetToCustom', () => {
    it('turns a rolling preset into the UTC days of its start and its anchor', () => {
      expect(ccPresetToCustom(rolling('7d'))).toEqual(custom('2026-09-30', '2026-10-07'));
      expect(ccPresetToCustom(rolling('1y'))).toEqual(custom('2025-10-07', '2026-10-07'));
    });

    it('uses UTC days when the window starts just after midnight UTC', () => {
      expect(ccPresetToCustom(rolling('1d', '2026-10-07T00:30:00.000Z'))).toEqual(custom('2026-10-06', '2026-10-07'));
    });

    it('turns every date, or a rolling preset without an anchor, into an empty custom range', () => {
      expect(ccPresetToCustom(CC_ALL_DATES)).toEqual(custom('', ''));
      expect(ccPresetToCustom(rolling('14d', null))).toEqual(custom('', ''));
    });

    it('leaves a custom range unchanged', () => {
      const range = custom('2026-09-01', '');
      expect(ccPresetToCustom(range)).toBe(range);
    });

    it('covers the rolling window it came from', () => {
      const rollingBounds = ccRangeBounds(rolling('30d'));
      const customBounds = ccRangeBounds(ccPresetToCustom(rolling('30d')));
      expect(customBounds.fromUtc! <= rollingBounds.fromUtc!).toBe(true);
      expect(customBounds.toUtc! >= ANCHOR).toBe(true);
    });
  });
});
