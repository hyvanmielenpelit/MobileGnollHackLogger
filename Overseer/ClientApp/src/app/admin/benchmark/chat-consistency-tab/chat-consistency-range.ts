/**
 * The date range of the Chat Consistency wizard's step 1: a preset (every date, a rolling window
 * back from an anchor instant, or custom UTC calendar days) and the UTC bounds it sends to the
 * timeline and run queries. Pure; every date is UTC.
 */

import { endOfUtcDay, formatUtcDate, isUtcDateInput, startOfUtcDay, utcMillis } from './chat-consistency-format';

export type CcRangePreset = 'all' | '1d' | '3d' | '7d' | '14d' | '28d' | '30d' | '90d' | '180d' | '1y' | 'custom';

/** The presets in the order the *Dates* select lists them; `days` is the length of a rolling day window. */
export const CC_RANGE_PRESETS: readonly { id: CcRangePreset; label: string; days?: number }[] = [
  { id: 'all', label: 'All dates' },
  { id: '1d', label: 'Last 1 day', days: 1 },
  { id: '3d', label: 'Last 3 days', days: 3 },
  { id: '7d', label: 'Last 7 days', days: 7 },
  { id: '14d', label: 'Last 14 days', days: 14 },
  { id: '28d', label: 'Last 28 days', days: 28 },
  { id: '30d', label: 'Last 30 days', days: 30 },
  { id: '90d', label: 'Last 90 days', days: 90 },
  { id: '180d', label: 'Last 180 days', days: 180 },
  { id: '1y', label: 'Last year' },
  { id: 'custom', label: 'Custom' }
];

export interface CcDateRange {
  preset: CcRangePreset;
  /** `YYYY-MM-DD` or `''`; used only by `custom`. */
  fromDay: string;
  /** `YYYY-MM-DD` or `''`; used only by `custom`. */
  toDay: string;
  /** The instant a rolling preset counts back from, as an ISO string; null for `all` and `custom`. */
  anchorUtc: string | null;
}

export const CC_ALL_DATES: CcDateRange = Object.freeze<CcDateRange>({ preset: 'all', fromDay: '', toDay: '', anchorUtc: null });

const MS_PER_DAY = 86_400_000;

function isRolling(preset: CcRangePreset): boolean {
  return preset !== 'all' && preset !== 'custom';
}

function presetLabel(preset: CcRangePreset): string {
  return CC_RANGE_PRESETS.find(entry => entry.id === preset)?.label ?? preset;
}

/**
 * The UTC bounds of a range as ISO strings; null is an open bound. A rolling preset runs from its
 * anchor minus N × 24 hours (`1y`: the anchor with `setUTCFullYear(year - 1)`) with an open end; one
 * without a parsable anchor is unbounded. `custom` runs from the first instant of `fromDay` to the
 * last millisecond of `toDay`, each open when empty or invalid.
 */
export function ccRangeBounds(range: CcDateRange): { fromUtc: string | null; toUtc: string | null } {
  if (range.preset === 'all') return { fromUtc: null, toUtc: null };
  if (range.preset === 'custom') {
    return { fromUtc: startOfUtcDay(range.fromDay), toUtc: endOfUtcDay(range.toDay) };
  }
  const anchor = utcMillis(range.anchorUtc);
  if (!Number.isFinite(anchor)) return { fromUtc: null, toUtc: null };
  if (range.preset === '1y') {
    const from = new Date(anchor);
    from.setUTCFullYear(from.getUTCFullYear() - 1);
    return { fromUtc: from.toISOString(), toUtc: null };
  }
  const days = CC_RANGE_PRESETS.find(entry => entry.id === range.preset)?.days ?? 0;
  return { fromUtc: new Date(anchor - days * MS_PER_DAY).toISOString(), toUtc: null };
}

/** A rolling preset anchored at `now`; `all` and `custom` are returned unchanged. */
export function ccAnchorRange(range: CcDateRange, now: Date): CcDateRange {
  return isRolling(range.preset) ? { ...range, anchorUtc: now.toISOString() } : range;
}

/** `All dates`, `Last 7 days`, `2026-09-01 to 2026-10-05`, `From 2026-09-01`, `Until 2026-10-05`. */
export function ccDateRangeText(range: CcDateRange): string {
  if (range.preset !== 'custom') return presetLabel(range.preset);
  const fromDay = isUtcDateInput(range.fromDay) ? range.fromDay : '';
  const toDay = isUtcDateInput(range.toDay) ? range.toDay : '';
  if (fromDay && toDay) return `${fromDay} to ${toDay}`;
  if (fromDay) return `From ${fromDay}`;
  if (toDay) return `Until ${toDay}`;
  return presetLabel('all');
}

/**
 * The range as custom days: a rolling preset becomes the UTC day of its window's start to the UTC
 * day of its anchor; `all` (or a rolling preset without an anchor) an empty custom range; `custom`
 * is returned unchanged.
 */
export function ccPresetToCustom(range: CcDateRange): CcDateRange {
  if (range.preset === 'custom') return range;
  const empty: CcDateRange = { preset: 'custom', fromDay: '', toDay: '', anchorUtc: null };
  if (range.preset === 'all') return empty;
  const { fromUtc } = ccRangeBounds(range);
  if (fromUtc === null || range.anchorUtc === null) return empty;
  return { preset: 'custom', fromDay: formatUtcDate(fromUtc), toDay: formatUtcDate(range.anchorUtc), anchorUtc: null };
}
