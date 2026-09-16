import { formatThinkingLevel, showReasoningBadge, formatServiceTier, formatDifficulty } from './model-badge-format.util';

describe('model-badge-format.util', () => {
  describe('formatThinkingLevel', () => {
    it('should return Default when the level is null', () => {
      expect(formatThinkingLevel(null)).toBe('Default');
    });

    it('should return Default when the level is undefined', () => {
      expect(formatThinkingLevel(undefined)).toBe('Default');
    });

    it('should return Default when the level is empty', () => {
      expect(formatThinkingLevel('')).toBe('Default');
    });

    it('should title-case a lowercase level', () => {
      expect(formatThinkingLevel('high')).toBe('High');
    });
  });

  describe('showReasoningBadge', () => {
    it('should return false when the mode is null', () => {
      expect(showReasoningBadge(null)).toBeFalse();
    });

    it('should return false when the mode is undefined', () => {
      expect(showReasoningBadge(undefined)).toBeFalse();
    });

    it('should return false for default, case-insensitively', () => {
      expect(showReasoningBadge('Default')).toBeFalse();
    });

    it('should return false for standard, case-insensitively', () => {
      expect(showReasoningBadge('STANDARD')).toBeFalse();
    });

    it('should return true for any other mode', () => {
      expect(showReasoningBadge('extended')).toBeTrue();
    });
  });

  describe('formatServiceTier', () => {
    it('should return None when the tier is null', () => {
      expect(formatServiceTier(null)).toBe('None');
    });

    it('should return None when the tier is undefined', () => {
      expect(formatServiceTier(undefined)).toBe('None');
    });

    it('should return Standard Only for standard_only, case-insensitively', () => {
      expect(formatServiceTier('STANDARD_ONLY')).toBe('Standard Only');
    });

    it('should title-case any other tier', () => {
      expect(formatServiceTier('priority')).toBe('Priority');
    });
  });

  describe('formatDifficulty', () => {
    it('should return Simple for the numeric value', () => {
      expect(formatDifficulty(1)).toBe('Simple');
    });

    it('should return Simple for the name', () => {
      expect(formatDifficulty('Simple')).toBe('Simple');
    });

    it('should return Intermediate for the numeric value', () => {
      expect(formatDifficulty(2)).toBe('Intermediate');
    });

    it('should return Intermediate for the name', () => {
      expect(formatDifficulty('Intermediate')).toBe('Intermediate');
    });

    it('should return Advanced for the numeric value', () => {
      expect(formatDifficulty(3)).toBe('Advanced');
    });

    it('should return Advanced for the name', () => {
      expect(formatDifficulty('Advanced')).toBe('Advanced');
    });

    it('should stringify an unrecognized value', () => {
      expect(formatDifficulty(99)).toBe('99');
    });
  });
});
