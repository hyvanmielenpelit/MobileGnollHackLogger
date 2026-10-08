import type { BenchmarkComparisonDto } from '../../../services/admin-benchmark.service';
import type {
  BenchmarkModelComparisonDto,
  BenchmarkModelComparisonEntryDto
} from '../model-comparison/model-comparison.models';
import {
  LAST_COMPARISON_MAX_ENTRIES,
  LAST_COMPARISON_MAX_EXPLANATION,
  LAST_COMPARISON_STORAGE_KEY,
  LAST_COMPARISON_STORAGE_VERSION,
  LastComparisonRecord,
  buildLastComparisonRecord,
  readLastComparison,
  writeLastComparison
} from './last-comparison';

describe('last comparison record', () => {
  const now = new Date('2026-10-08T12:00:00Z');

  const identity = (overrides: Partial<BenchmarkComparisonDto> = {}): BenchmarkComparisonDto => ({
    id: 12,
    name: 'Model A vs Model B',
    customName: null,
    defaultName: 'Model A vs Model B',
    entryCount: 3,
    subjectKind: 'Runs',
    entryKeys: ['run:1', 'run:2', 'group:3'],
    createdAtUtc: '2026-10-06T10:00:00Z',
    renamedAtUtc: null,
    ...overrides
  });

  const entry = (key: string, quality: number | null, overrides: Partial<BenchmarkModelComparisonEntryDto> = {}): BenchmarkModelComparisonEntryDto => ({
    key,
    label: `Label ${key}`,
    modelDisplayName: `Model ${key}`,
    provider: 'Anthropic',
    thinkingLevel: 'high',
    excluded: false,
    explanation: `Explanation of ${key}.`,
    quality: quality === null ? null : {
      pointEstimate: quality, intervalLower: quality - 4, intervalUpper: quality + 4
    },
    speed: { modelTimeP50Ms: 4200, ttftP50Ms: 850 },
    cost: { candidateCostPerQuestionUsd: 0.0042 },
    ...overrides
  } as unknown as BenchmarkModelComparisonEntryDto);

  const comparison = (entries: BenchmarkModelComparisonEntryDto[], overrides: Partial<BenchmarkModelComparisonDto> = {}): BenchmarkModelComparisonDto => ({
    pricingBasis: 'Current',
    pricingBasisLabel: 'Catalog prices as of 8 Oct 2026',
    computedAtUtc: '2026-10-08T11:59:00Z',
    subjectKind: 'Runs',
    baselineSuiteName: 'Default Suite',
    baselineBatteryName: null,
    comparableCount: entries.filter(e => !e.excluded).length,
    excludedCount: entries.filter(e => e.excluded).length,
    entries,
    ...overrides
  } as unknown as BenchmarkModelComparisonDto);

  /** A storage double over a plain map. */
  function memoryStorage(initial: Record<string, string> = {}): Storage {
    const values = new Map(Object.entries(initial));
    return {
      getItem: (key: string) => values.get(key) ?? null,
      setItem: (key: string, value: string) => { values.set(key, value); },
      removeItem: (key: string) => { values.delete(key); },
      clear: () => values.clear(),
      key: (index: number) => Array.from(values.keys())[index] ?? null,
      get length() { return values.size; }
    } as Storage;
  }

  describe('buildLastComparisonRecord', () => {
    it('takes the identity, the basis, the scope and the counts', () => {
      const record = buildLastComparisonRecord(comparison([entry('run:1', 60)]), identity(), now);

      expect(record.version).toBe(LAST_COMPARISON_STORAGE_VERSION);
      expect(record.savedAtUtc).toBe('2026-10-08T12:00:00.000Z');
      expect(record.id).toBe(12);
      expect(record.name).toBe('Model A vs Model B');
      expect(record.subjectKind).toBe('Runs');
      expect(record.entryKeys).toEqual(['run:1', 'run:2', 'group:3']);
      expect(record.computedAtUtc).toBe('2026-10-08T11:59:00Z');
      expect(record.pricingBasis).toBe('Current');
      expect(record.pricingBasisLabel).toBe('Catalog prices as of 8 Oct 2026');
      expect(record.scopeName).toBe('Default Suite');
      expect(record.comparableCount).toBe(1);
      expect(record.excludedCount).toBe(0);
    });

    it('names the battery before the suite, and reads AsRun as AsRun', () => {
      const record = buildLastComparisonRecord(
        comparison([entry('battery:4', 60)], { baselineBatteryName: 'Core Battery', baselineSuiteName: null, pricingBasis: 'AsRun' }),
        identity({ subjectKind: 'Batteries', entryKeys: ['battery:4'] }),
        now);

      expect(record.scopeName).toBe('Core Battery');
      expect(record.subjectKind).toBe('Batteries');
      expect(record.pricingBasis).toBe('AsRun');
    });

    it('orders charted entries by Intelligence Index, then the excluded ones, ties by key', () => {
      const record = buildLastComparisonRecord(comparison([
        entry('run:5', null, { excluded: true }),
        entry('run:2', 55),
        entry('run:4', 70),
        entry('run:1', 55),
        entry('run:3', null),
        entry('run:0', 90, { excluded: true })
      ]), identity(), now);

      expect(record.entries.map(e => e.key)).toEqual(['run:4', 'run:1', 'run:2', 'run:3', 'run:0', 'run:5']);
    });

    it('keeps the figures of an entry, and null for each measure it lacks', () => {
      const record = buildLastComparisonRecord(comparison([
        entry('run:1', 62.5),
        entry('run:2', null, { speed: null, cost: { candidateCostPerQuestionUsd: null } } as unknown as Partial<BenchmarkModelComparisonEntryDto>)
      ]), identity(), now);

      expect(record.entries[0]).toEqual({
        key: 'run:1',
        label: 'Label run:1',
        modelDisplayName: 'Model run:1',
        provider: 'Anthropic',
        thinkingLevel: 'high',
        excluded: false,
        explanation: 'Explanation of run:1.',
        qualityPoint: 62.5,
        qualityLower: 58.5,
        qualityUpper: 66.5,
        modelTimeP50Ms: 4200,
        ttftP50Ms: 850,
        candidateCostPerQuestionUsd: 0.0042
      });
      expect(record.entries[1]).toEqual(expect.objectContaining({
        qualityPoint: null,
        qualityLower: null,
        qualityUpper: null,
        modelTimeP50Ms: null,
        ttftP50Ms: null,
        candidateCostPerQuestionUsd: null
      }));
    });

    it('cuts a long explanation to the limit, ellipsis included', () => {
      const long = 'x'.repeat(500);
      const record = buildLastComparisonRecord(
        comparison([entry('run:1', 60, { excluded: true, explanation: long })]), identity(), now);

      const explanation = record.entries[0].explanation;
      expect(explanation.length).toBe(LAST_COMPARISON_MAX_EXPLANATION);
      expect(explanation.endsWith('…')).toBe(true);
    });

    it('keeps at most the entry cap, the highest first', () => {
      const entries = Array.from({ length: LAST_COMPARISON_MAX_ENTRIES + 6 }, (_, i) => entry(`run:${i}`, i));
      const record = buildLastComparisonRecord(comparison(entries), identity(), now);

      expect(record.entries.length).toBe(LAST_COMPARISON_MAX_ENTRIES);
      expect(record.entries[0].key).toBe(`run:${LAST_COMPARISON_MAX_ENTRIES + 5}`);
    });
  });

  describe('readLastComparison and writeLastComparison', () => {
    function record(): LastComparisonRecord {
      return buildLastComparisonRecord(comparison([entry('run:1', 60), entry('run:2', null, { excluded: true })]), identity(), now);
    }

    it('reads back what it wrote', () => {
      const storage = memoryStorage();
      expect(writeLastComparison(record(), storage)).toBe(true);
      expect(readLastComparison(storage)).toEqual(record());
    });

    it('reads nothing when there is no record', () => {
      expect(readLastComparison(memoryStorage())).toBeNull();
    });

    it('rejects malformed JSON', () => {
      expect(readLastComparison(memoryStorage({ [LAST_COMPARISON_STORAGE_KEY]: '{not json' }))).toBeNull();
    });

    it('rejects a record written under another version', () => {
      const other = { ...record(), version: 2 };
      expect(readLastComparison(memoryStorage({ [LAST_COMPARISON_STORAGE_KEY]: JSON.stringify(other) }))).toBeNull();
    });

    it('rejects a record of the wrong shape', () => {
      const shapes: unknown[] = [
        null,
        [],
        { ...record(), id: '12' },
        { ...record(), comparableCount: null },
        { ...record(), entries: 'none' },
        { ...record(), entryKeys: [1, 2] },
        { ...record(), pricingBasis: 'Tomorrow' },
        { ...record(), entries: [{ ...record().entries[0], qualityPoint: '60' }] },
        { ...record(), entries: [{ ...record().entries[0], excluded: 'no' }] }
      ];
      for (const shape of shapes) {
        expect(readLastComparison(memoryStorage({ [LAST_COMPARISON_STORAGE_KEY]: JSON.stringify(shape) }))).toBeNull();
      }
      // JSON has no NaN or Infinity; a non-finite number is written as null and fails the shape.
      expect(readLastComparison(memoryStorage({
        [LAST_COMPARISON_STORAGE_KEY]: JSON.stringify({ ...record(), excludedCount: Number.NaN })
      }))).toBeNull();
    });

    it('returns null when reading throws', () => {
      const storage = { getItem: () => { throw new Error('private browsing'); } };
      expect(readLastComparison(storage)).toBeNull();
    });

    it('swallows a storage that refuses the write', () => {
      const storage = { setItem: () => { throw new Error('quota exceeded'); } };
      expect(() => writeLastComparison(record(), storage)).not.toThrow();
      expect(writeLastComparison(record(), storage)).toBe(false);
    });

    it('uses localStorage by default', () => {
      try {
        writeLastComparison(record());
        expect(readLastComparison()?.id).toBe(12);
      } finally {
        localStorage.removeItem(LAST_COMPARISON_STORAGE_KEY);
      }
    });
  });
});
