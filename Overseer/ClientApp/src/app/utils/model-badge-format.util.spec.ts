import { formatThinkingLevel, showReasoningBadge, formatServiceTier, formatDifficulty } from './model-badge-format.util';

describe('model-badge-format.util', () => {
  describe('formatThinkingLevel', () => {
    it.each([
      { name: 'return Default when the level is null', input: null, expected: 'Default' },
      { name: 'return Default when the level is undefined', input: undefined, expected: 'Default' },
      { name: 'return Default when the level is empty', input: '', expected: 'Default' },
      { name: 'title-case a lowercase level', input: 'high', expected: 'High' }
    ])('should $name', ({ input, expected }) => {
      expect(formatThinkingLevel(input)).toBe(expected);
    });
  });

  describe('showReasoningBadge', () => {
    it.each([
      { name: 'return false when the mode is null', input: null, expected: false },
      { name: 'return false when the mode is undefined', input: undefined, expected: false },
      { name: 'return false for default, case-insensitively', input: 'Default', expected: false },
      { name: 'return false for standard, case-insensitively', input: 'STANDARD', expected: false },
      { name: 'return true for any other mode', input: 'extended', expected: true }
    ])('should $name', ({ input, expected }) => {
      expect(showReasoningBadge(input)).toBe(expected);
    });
  });

  describe('formatServiceTier', () => {
    it.each([
      { name: 'return None when the tier is null', input: null, expected: 'None' },
      { name: 'return None when the tier is undefined', input: undefined, expected: 'None' },
      { name: 'return Standard Only for standard_only, case-insensitively', input: 'STANDARD_ONLY', expected: 'Standard Only' },
      { name: 'title-case any other tier', input: 'priority', expected: 'Priority' }
    ])('should $name', ({ input, expected }) => {
      expect(formatServiceTier(input)).toBe(expected);
    });
  });

  describe('formatDifficulty', () => {
    it.each([
      { name: 'return Simple for the numeric value', input: 1, expected: 'Simple' },
      { name: 'return Simple for the name', input: 'Simple', expected: 'Simple' },
      { name: 'return Intermediate for the numeric value', input: 2, expected: 'Intermediate' },
      { name: 'return Intermediate for the name', input: 'Intermediate', expected: 'Intermediate' },
      { name: 'return Advanced for the numeric value', input: 3, expected: 'Advanced' },
      { name: 'return Advanced for the name', input: 'Advanced', expected: 'Advanced' },
      { name: 'stringify an unrecognized value', input: 99, expected: '99' }
    ])('should $name', ({ input, expected }) => {
      expect(formatDifficulty(input)).toBe(expected);
    });
  });
});
