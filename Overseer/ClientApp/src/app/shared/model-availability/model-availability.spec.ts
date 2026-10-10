import {
  ModelAvailability,
  availabilityChip,
  availabilitySentence,
  blockerText,
  formatCatalogDate,
  formatUtcMinute,
  isResolutionDeletion,
  needsAttention
} from './model-availability';

const RETIRED: ModelAvailability = {
  status: 'retired',
  needsAttention: true,
  retiredOn: '2026-09-30',
  note: 'Google shut it down.',
  replacement: { modelId: 'gemini-3.8-flash', displayName: 'Gemini 3.8 Flash' }
};

const NOT_IN_CATALOG: ModelAvailability = { status: 'notInCatalog', needsAttention: true };

describe('model-availability helpers', () => {
  it('needsAttention is null-safe and follows the server flag', () => {
    expect(needsAttention(null)).toBe(false);
    expect(needsAttention(undefined)).toBe(false);
    expect(needsAttention({ status: 'available', needsAttention: false })).toBe(false);
    expect(needsAttention(RETIRED)).toBe(true);
    expect(needsAttention(NOT_IN_CATALOG)).toBe(true);
  });

  it('gives a warning chip for a retired model, an info chip for one not in the catalog, and none otherwise', () => {
    expect(availabilityChip(RETIRED)).toEqual({ text: 'Removed', tone: 'warning' });
    expect(availabilityChip(NOT_IN_CATALOG)).toEqual({ text: 'Not in catalog', tone: 'info' });
    for (const status of ['available', 'custom', 'customEndpoint'] as const) {
      expect(availabilityChip({ status, needsAttention: false })).toBeNull();
    }
    expect(availabilityChip(null)).toBeNull();
  });

  it('formats a catalog date as a fixed en-US long date and leaves anything else alone', () => {
    expect(formatCatalogDate('2026-09-30')).toBe('September 30, 2026');
    expect(formatCatalogDate('2026-01-01')).toBe('January 1, 2026');
    expect(formatCatalogDate('soon')).toBe('soon');
    expect(formatCatalogDate(null)).toBe('');
  });

  it('writes the retired sentence with the date and the note, each only when present', () => {
    expect(availabilitySentence(RETIRED, 'Flash 3.7')).toBe(
      'Flash 3.7 was removed from the model catalog on September 30, 2026. Google shut it down.');
    expect(availabilitySentence({ ...RETIRED, note: null }, 'Flash 3.7')).toBe(
      'Flash 3.7 was removed from the model catalog on September 30, 2026.');
    expect(availabilitySentence({ ...RETIRED, retiredOn: null, note: '  ' }, 'Flash 3.7')).toBe(
      'Flash 3.7 was removed from the model catalog.');
  });

  it('names the model ID in the not-in-catalog sentence, falling back to the name', () => {
    expect(availabilitySentence(NOT_IN_CATALOG, 'My model', 'my-model-x')).toBe(
      "my-model-x isn't in Overseer's model catalog, so its limits and price aren't known.");
    expect(availabilitySentence(NOT_IN_CATALOG, 'My model')).toBe(
      "My model isn't in Overseer's model catalog, so its limits and price aren't known.");
  });

  it('writes no sentence for a model that needs no attention', () => {
    expect(availabilitySentence({ status: 'custom', needsAttention: false }, 'Mine', 'mine')).toBe('');
    expect(availabilitySentence(null, 'Mine')).toBe('');
  });

  it('recognizes the deletion result', () => {
    expect(isResolutionDeletion({ changes: [], blockers: [], model: null, deleted: true })).toBe(true);
    expect(isResolutionDeletion({ changes: [{ field: 'Model ID', from: 'a', to: 'b' }], blockers: [], model: {} })).toBe(false);
    expect(isResolutionDeletion(null)).toBe(false);
  });

  it('formats a blocker with its roles and start time in UTC', () => {
    expect(formatUtcMinute('2026-10-10T09:40:12')).toBe('2026-10-10 09:40 UTC');
    expect(formatUtcMinute('2026-10-10T09:40:12Z')).toBe('2026-10-10 09:40 UTC');
    expect(blockerText({
      kind: 'run', id: '12', runId: 12, label: 'Run #12', roles: ['assessor', 'claim verifier'],
      startedAtUtc: '2026-10-10T09:40:00Z'
    })).toBe('Run #12 — as the assessor and the claim verifier, started 2026-10-10 09:40 UTC.');
    expect(blockerText({ kind: 'difficultyJob', id: 'j1', label: 'Difficulty job', roles: [], startedAtUtc: '' }))
      .toBe('Difficulty job.');
  });
});
