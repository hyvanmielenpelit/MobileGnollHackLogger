import { formatStatusLabel, getScoreBadgeClass } from './benchmark-run-format';

describe('benchmark run format', () => {
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
