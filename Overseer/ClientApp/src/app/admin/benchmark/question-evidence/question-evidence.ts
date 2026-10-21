import type { BenchmarkEvidenceKind, BenchmarkEvidenceUnit } from '../../../services/admin-benchmark.service';

/** The evidence kinds in display order, as `BenchmarkEvidenceUnit.Kinds` lists them on the server. */
export const EVIDENCE_KINDS: readonly BenchmarkEvidenceKind[] = ['wiki', 'source', 'kb'];

/** The kind names the question form's Kind select shows. */
export const EVIDENCE_KIND_LABELS: Readonly<Record<BenchmarkEvidenceKind, string>> = {
  wiki: 'Wiki',
  source: 'Source',
  kb: 'Knowledge base'
};

/** One evidence unit as the review card lists it: `wiki · Races/Human.md · personal`. */
export function evidenceUnitText(unit: BenchmarkEvidenceUnit): string {
  return [
    `${unit.kind} · ${unit.path}`,
    unit.section ? `§ ${unit.section}` : null,
    unit.lines ? `L${unit.lines}` : null,
    unit.function ? `${unit.function}()` : null,
    unit.personal ? 'personal' : null
  ].filter(part => part !== null).join(' · ');
}

function trimmedOrAbsent(value: string | null | undefined): string | undefined {
  const trimmed = (value ?? '').trim();
  return trimmed ? trimmed : undefined;
}

/**
 * The unit as it is sent: every string trimmed, blank optional fields and the fields its kind
 * does not use left out, and `personal` present only when true. Keys come in the server's
 * canonical order (kind, path, section, lines, function, personal).
 */
export function normalizeEvidenceUnit(unit: BenchmarkEvidenceUnit): BenchmarkEvidenceUnit {
  const normalized: BenchmarkEvidenceUnit = { kind: unit.kind, path: (unit.path ?? '').trim() };
  const section = unit.kind === 'wiki' ? trimmedOrAbsent(unit.section) : undefined;
  const lines = unit.kind === 'source' ? trimmedOrAbsent(unit.lines) : undefined;
  const fn = unit.kind === 'source' ? trimmedOrAbsent(unit.function) : undefined;
  if (section !== undefined) normalized.section = section;
  if (lines !== undefined) normalized.lines = lines;
  if (fn !== undefined) normalized.function = fn;
  if (unit.personal) normalized.personal = true;
  return normalized;
}

const LINES_PATTERN = /^(\d+)-(\d+)$/;
const INT32_MAX = 2147483647;

/** `a-b` with 1 <= a <= b, as `BenchmarkEvidence.TryParseLines` reads it. */
function linesAreValid(lines: string): boolean {
  const match = LINES_PATTERN.exec(lines.trim());
  if (!match) return false;
  const start = Number(match[1]);
  const end = Number(match[2]);
  if (start > INT32_MAX || end > INT32_MAX) return false;
  return start >= 1 && start <= end;
}

/**
 * The first problem with the units after normalization, worded as `BenchmarkEvidence.Validate`
 * words it; null when every unit is valid.
 */
export function validateEvidenceUnits(units: readonly BenchmarkEvidenceUnit[] | null | undefined): string | null {
  if (!units) return null;
  for (let i = 0; i < units.length; i++) {
    const unit = normalizeEvidenceUnit(units[i]);
    const where = `Evidence unit ${i + 1}`;
    if (!EVIDENCE_KINDS.includes(unit.kind)) {
      return `${where}: unknown kind '${unit.kind}' (expected ${EVIDENCE_KINDS.join(', ')}).`;
    }
    if (!unit.path) {
      return `${where}: path is empty.`;
    }
    if (unit.kind !== 'kb') {
      const path = unit.path;
      if (path.includes('\\') || path.startsWith('/') || path.includes(':') || path.split('/').includes('..')) {
        return `${where}: path '${path}' must be repository-relative with forward slashes.`;
      }
    }
    if (unit.lines != null) {
      if (unit.kind !== 'source') return `${where}: lines apply to source units only.`;
      if (!linesAreValid(unit.lines)) return `${where}: lines '${unit.lines}' must read a-b with 1 <= a <= b.`;
    }
    if (unit.function != null && unit.kind !== 'source') {
      return `${where}: function applies to source units only.`;
    }
    if (unit.section != null && unit.kind !== 'wiki') {
      return `${where}: section applies to wiki units only.`;
    }
  }
  return null;
}

/** Whether two unit lists are the same after normalization, order included. */
export function evidenceEquals(a: readonly BenchmarkEvidenceUnit[], b: readonly BenchmarkEvidenceUnit[]): boolean {
  return JSON.stringify(a.map(normalizeEvidenceUnit)) === JSON.stringify(b.map(normalizeEvidenceUnit));
}
