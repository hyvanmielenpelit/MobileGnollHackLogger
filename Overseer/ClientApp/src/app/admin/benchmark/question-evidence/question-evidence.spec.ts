import type { BenchmarkEvidenceUnit } from '../../../services/admin-benchmark.service';
import {
  EVIDENCE_KINDS,
  evidenceEquals,
  evidenceUnitText,
  normalizeEvidenceUnit,
  validateEvidenceUnits
} from './question-evidence';

describe('question evidence helpers', () => {
  const wiki = (path: string, extra: Partial<BenchmarkEvidenceUnit> = {}): BenchmarkEvidenceUnit => ({ kind: 'wiki', path, ...extra });
  const source = (path: string, extra: Partial<BenchmarkEvidenceUnit> = {}): BenchmarkEvidenceUnit => ({ kind: 'source', path, ...extra });

  it('lists the kinds in the server order', () => {
    expect(EVIDENCE_KINDS).toEqual(['wiki', 'source', 'kb']);
  });

  describe('validateEvidenceUnits', () => {
    it('accepts null, an empty list and valid units', () => {
      expect(validateEvidenceUnits(null)).toBeNull();
      expect(validateEvidenceUnits([])).toBeNull();
      expect(validateEvidenceUnits([
        wiki('Races/Human.md', { personal: true }),
        wiki('Artifacts/The Orb of Fate.md', { section: 'Invoking' }),
        source('src/artifact.c', { lines: '4194-4231' }),
        source('src/artifact.c', { function: 'arti_invoke' }),
        { kind: 'kb', path: 'Altars: identify B/U/C' }
      ])).toBeNull();
    });

    it('rejects an unknown kind with the server message', () => {
      expect(validateEvidenceUnits([wiki('Races/Human.md'), { kind: 'web' as any, path: 'https://example.com' }]))
        .toBe("Evidence unit 2: unknown kind 'web' (expected wiki, source, kb).");
    });

    it('rejects an empty path', () => {
      expect(validateEvidenceUnits([wiki('  ')])).toBe('Evidence unit 1: path is empty.');
    });

    it.each(['Races\\Human.md', '/Races/Human.md', 'C:/wiki/Races/Human.md', 'Races/../Human.md'])(
      'rejects the path %s that is not repository-relative', path => {
        expect(validateEvidenceUnits([wiki(path)]))
          .toBe(`Evidence unit 1: path '${path}' must be repository-relative with forward slashes.`);
      });

    it.each(['10-5', '0-3', 'abc', '12', 'L10-L20', '9-3'])('rejects the lines %s', lines => {
      expect(validateEvidenceUnits([source('src/artifact.c', { lines })]))
        .toBe(`Evidence unit 1: lines '${lines}' must read a-b with 1 <= a <= b.`);
    });

    it('accepts a single-line range and blank lines on a source unit', () => {
      expect(validateEvidenceUnits([source('src/bones.c', { lines: '7-7' })])).toBeNull();
      expect(validateEvidenceUnits([source('src/bones.c', { lines: '   ' })])).toBeNull();
    });

    it('ignores fields the kind does not use, since normalization drops them', () => {
      expect(validateEvidenceUnits([wiki('Races/Human.md', { lines: '9-3', function: 'u_init' })])).toBeNull();
      expect(validateEvidenceUnits([source('src/u_init.c', { section: 'Starting inventory' })])).toBeNull();
    });
  });

  describe('normalizeEvidenceUnit', () => {
    it('trims, drops blank and inapplicable fields and keeps personal only when true', () => {
      expect(normalizeEvidenceUnit({ kind: 'wiki', path: ' Races/Human.md ', section: '  ', lines: '1-2', function: 'f', personal: false }))
        .toEqual({ kind: 'wiki', path: 'Races/Human.md' });
      expect(normalizeEvidenceUnit({ kind: 'source', path: 'src/bones.c', section: 'S', lines: ' 140-203 ', function: ' savebones ', personal: true }))
        .toEqual({ kind: 'source', path: 'src/bones.c', lines: '140-203', function: 'savebones', personal: true });
      expect(normalizeEvidenceUnit({ kind: 'kb', path: 'Praying', section: 'x', lines: '1-2', function: 'f' }))
        .toEqual({ kind: 'kb', path: 'Praying' });
    });

    it('writes keys in the server canonical order', () => {
      const unit = normalizeEvidenceUnit({ personal: true, function: 'f', lines: '1-2', path: 'src/a.c', kind: 'source' } as BenchmarkEvidenceUnit);
      expect(Object.keys(unit)).toEqual(['kind', 'path', 'lines', 'function', 'personal']);
    });
  });

  describe('evidenceEquals', () => {
    const a = wiki('Races/Human.md', { personal: true });
    const b = source('src/bones.c', { lines: '140-203' });

    it('is true for the same list', () => {
      expect(evidenceEquals([a, b], [{ ...a }, { ...b }])).toBe(true);
    });

    it('is false for the same units in another order', () => {
      expect(evidenceEquals([a, b], [b, a])).toBe(false);
    });

    it('treats a blank field as absent', () => {
      expect(evidenceEquals([{ ...b, function: '  ', section: '' }], [b])).toBe(true);
    });

    it('is false when a field changed', () => {
      expect(evidenceEquals([b], [{ ...b, lines: '140-204' }])).toBe(false);
    });
  });

  it('evidenceUnitText lists every present field', () => {
    expect(evidenceUnitText(wiki('Races/Human.md', { personal: true }))).toBe('wiki · Races/Human.md · personal');
    expect(evidenceUnitText(source('src/bones.c', { lines: '140-203', function: 'savebones' })))
      .toBe('source · src/bones.c · L140-203 · savebones()');
    expect(evidenceUnitText(wiki('Roles/Healer.md', { section: 'Starting inventory' }))).toBe('wiki · Roles/Healer.md · § Starting inventory');
  });
});
