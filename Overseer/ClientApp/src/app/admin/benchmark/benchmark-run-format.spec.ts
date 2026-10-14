import { formatCostAmount, formatStatusLabel, getScoreBadgeClass } from './benchmark-run-format';

describe('benchmark run format', () => {
  describe('formatCostAmount', () => {
    it.each([
      { amount: 2.5312, missing: undefined, label: '$2.5312' },
      { amount: 1, missing: undefined, label: '$1.0000' },
      { amount: 0.9912, missing: undefined, label: '$0.9912' },
      { amount: 0.0004, missing: undefined, label: '$0.0004' },
      { amount: 1234.5, missing: undefined, label: '$1,234.5000' },
      // A negative amount that rounds to zero loses its sign.
      { amount: -0.00001, missing: undefined, label: '$0.0000' },
      { amount: null, missing: undefined, label: '-' },
      { amount: undefined, missing: undefined, label: '-' },
      { amount: Number.NaN, missing: undefined, label: '-' },
      { amount: Number.POSITIVE_INFINITY, missing: undefined, label: '-' },
      { amount: Number.NEGATIVE_INFINITY, missing: undefined, label: '-' },
      { amount: null, missing: '—', label: '—' },
      { amount: Number.NaN, missing: '—', label: '—' },
      { amount: Number.POSITIVE_INFINITY, missing: '—', label: '—' },
      { amount: 0.0042, missing: '—', label: '$0.0042' }
    ])('formats $amount as "$label" (missing: $missing)', ({ amount, missing, label }) => {
      expect(formatCostAmount(amount, missing)).toBe(label);
    });
  });

  describe('formatStatusLabel', () => {
    it.each([
      { status: 'CompletedWithLimits', label: 'Completed with limits' },
      { status: 6, label: 'Completed with limits' },
      { status: 'CompletedWithErrors', label: 'Completed with errors' },
      { status: 3, label: 'Completed with errors' },
      { status: 'Running', label: 'Running' },
      { status: 1, label: 'Running' },
      { status: 'Completed', label: 'Completed' },
      { status: 2, label: 'Completed' },
      { status: 'Failed', label: 'Failed' },
      { status: 4, label: 'Failed' },
      { status: 'Canceled', label: 'Canceled' },
      { status: 5, label: 'Canceled' }
    ])('labels status $status as "$label", from the numeric and the string status alike', ({ status, label }) => {
      expect(formatStatusLabel(status)).toBe(label);
    });
  });

  describe('getScoreBadgeClass', () => {
    it.each([
      { score: 90, badgeClass: 'badge-score-high' },
      { score: 65, badgeClass: 'badge-score-mid' },
      { score: 40, badgeClass: 'badge-score-low' },
      { score: null, badgeClass: 'badge-score-na' }
    ])('should compute score badge classes correctly: $score is $badgeClass', ({ score, badgeClass }) => {
      expect(getScoreBadgeClass(score)).toBe(badgeClass);
    });
  });
});
