/**
 * The comparison last computed in this browser, as the Model Comparison launcher's *Last comparison*
 * card reads it back: the numbered comparison, its figures per entry and the basis they were costed
 * on. Kept in `localStorage` only, so it is a per-browser convenience and never shared state.
 *
 * Pure TypeScript with no Angular dependency. Every storage access is inside try/catch: storage
 * throws in private-browsing modes and on a full quota, and forgetting the card is not worth
 * surfacing to the operator.
 */

import type { BenchmarkComparisonDto } from '../../../services/admin-benchmark.service';
import type {
  BenchmarkModelComparisonDto,
  BenchmarkModelComparisonEntryDto,
  BenchmarkModelComparisonPricingBasis
} from '../model-comparison/model-comparison.models';

export const LAST_COMPARISON_STORAGE_KEY = 'overseer.benchmark.modelComparison.last';

export const LAST_COMPARISON_STORAGE_VERSION = 1;

/** The most entries a record keeps; the rest of a larger comparison is in the wizard. */
export const LAST_COMPARISON_MAX_ENTRIES = 64;

/** The longest explanation a record keeps per entry, ellipsis included. */
export const LAST_COMPARISON_MAX_EXPLANATION = 240;

/** One entry of the remembered comparison. A measure the comparison did not produce is null. */
export interface LastComparisonEntry {
  /** `run:12`, `group:3` or `battery:4`. */
  key: string;
  label: string;
  modelDisplayName: string;
  provider: string;
  thinkingLevel: string | null;
  excluded: boolean;
  /** At most {@link LAST_COMPARISON_MAX_EXPLANATION} characters. */
  explanation: string;
  qualityPoint: number | null;
  qualityLower: number | null;
  qualityUpper: number | null;
  modelTimeP50Ms: number | null;
  ttftP50Ms: number | null;
  candidateCostPerQuestionUsd: number | null;
}

/** The stored record. */
export interface LastComparisonRecord {
  version: typeof LAST_COMPARISON_STORAGE_VERSION;
  savedAtUtc: string;
  /** The numbered comparison: its id, display name, subject and entry keys. */
  id: number;
  name: string;
  subjectKind: 'Runs' | 'Batteries';
  entryKeys: string[];
  computedAtUtc: string;
  pricingBasis: BenchmarkModelComparisonPricingBasis;
  pricingBasisLabel: string;
  /** The baseline battery's name, else the baseline suite's, else null. */
  scopeName: string | null;
  comparableCount: number;
  excludedCount: number;
  /** Charted entries by Intelligence Index, highest first, then excluded ones; ties by key. */
  entries: LastComparisonEntry[];
}

/** The part of `Storage` the record is read through. */
export type LastComparisonStorageReader = Pick<Storage, 'getItem'>;

/** The part of `Storage` the record is written through. */
export type LastComparisonStorageWriter = Pick<Storage, 'setItem'>;

function finiteOrNull(value: number | null | undefined): number | null {
  return typeof value === 'number' && Number.isFinite(value) ? value : null;
}

function truncate(text: string, max: number): string {
  return text.length <= max ? text : `${text.slice(0, max - 1)}…`;
}

function entryOf(entry: BenchmarkModelComparisonEntryDto): LastComparisonEntry {
  return {
    key: entry.key,
    label: entry.label ?? '',
    modelDisplayName: entry.modelDisplayName ?? '',
    provider: entry.provider ?? '',
    thinkingLevel: entry.thinkingLevel ?? null,
    excluded: entry.excluded === true,
    explanation: truncate(entry.explanation ?? '', LAST_COMPARISON_MAX_EXPLANATION),
    qualityPoint: finiteOrNull(entry.quality?.pointEstimate),
    qualityLower: finiteOrNull(entry.quality?.intervalLower),
    qualityUpper: finiteOrNull(entry.quality?.intervalUpper),
    modelTimeP50Ms: finiteOrNull(entry.speed?.modelTimeP50Ms),
    ttftP50Ms: finiteOrNull(entry.speed?.ttftP50Ms),
    candidateCostPerQuestionUsd: finiteOrNull(entry.cost?.candidateCostPerQuestionUsd)
  };
}

/** Charted before excluded; then the higher Intelligence Index, an unmeasured one last; then the key. */
function compareEntries(a: LastComparisonEntry, b: LastComparisonEntry): number {
  if (a.excluded !== b.excluded) {
    return a.excluded ? 1 : -1;
  }
  if (a.qualityPoint !== b.qualityPoint) {
    if (a.qualityPoint === null) { return 1; }
    if (b.qualityPoint === null) { return -1; }
    return b.qualityPoint - a.qualityPoint;
  }
  return a.key < b.key ? -1 : a.key > b.key ? 1 : 0;
}

/** The record for a computed comparison and the numbered comparison it is. */
export function buildLastComparisonRecord(
  dto: BenchmarkModelComparisonDto,
  identity: BenchmarkComparisonDto,
  now: Date
): LastComparisonRecord {
  const entries = (dto.entries ?? [])
    .map(entryOf)
    .sort(compareEntries)
    .slice(0, LAST_COMPARISON_MAX_ENTRIES);
  return {
    version: LAST_COMPARISON_STORAGE_VERSION,
    savedAtUtc: now.toISOString(),
    id: identity.id,
    name: identity.name,
    subjectKind: identity.subjectKind === 'Batteries' ? 'Batteries' : 'Runs',
    entryKeys: [...identity.entryKeys],
    computedAtUtc: dto.computedAtUtc,
    pricingBasis: dto.pricingBasis === 'AsRun' ? 'AsRun' : 'Current',
    pricingBasisLabel: dto.pricingBasisLabel ?? '',
    scopeName: dto.baselineBatteryName ?? dto.baselineSuiteName ?? null,
    comparableCount: dto.comparableCount,
    excludedCount: dto.excludedCount,
    entries
  };
}

function isFiniteNumber(value: unknown): value is number {
  return typeof value === 'number' && Number.isFinite(value);
}

function isNullableFiniteNumber(value: unknown): boolean {
  return value === null || isFiniteNumber(value);
}

function isString(value: unknown): value is string {
  return typeof value === 'string';
}

function isEntry(value: unknown): value is LastComparisonEntry {
  if (!value || typeof value !== 'object') {
    return false;
  }
  const entry = value as Record<string, unknown>;
  return isString(entry['key'])
    && isString(entry['label'])
    && isString(entry['modelDisplayName'])
    && isString(entry['provider'])
    && (entry['thinkingLevel'] === null || isString(entry['thinkingLevel']))
    && typeof entry['excluded'] === 'boolean'
    && isString(entry['explanation'])
    && isNullableFiniteNumber(entry['qualityPoint'])
    && isNullableFiniteNumber(entry['qualityLower'])
    && isNullableFiniteNumber(entry['qualityUpper'])
    && isNullableFiniteNumber(entry['modelTimeP50Ms'])
    && isNullableFiniteNumber(entry['ttftP50Ms'])
    && isNullableFiniteNumber(entry['candidateCostPerQuestionUsd']);
}

function isRecord(value: unknown): value is LastComparisonRecord {
  if (!value || typeof value !== 'object') {
    return false;
  }
  const record = value as Record<string, unknown>;
  return record['version'] === LAST_COMPARISON_STORAGE_VERSION
    && isString(record['savedAtUtc'])
    && isFiniteNumber(record['id'])
    && isString(record['name'])
    && (record['subjectKind'] === 'Runs' || record['subjectKind'] === 'Batteries')
    && Array.isArray(record['entryKeys'])
    && (record['entryKeys'] as unknown[]).every(isString)
    && isString(record['computedAtUtc'])
    && (record['pricingBasis'] === 'Current' || record['pricingBasis'] === 'AsRun')
    && isString(record['pricingBasisLabel'])
    && (record['scopeName'] === null || isString(record['scopeName']))
    && isFiniteNumber(record['comparableCount'])
    && isFiniteNumber(record['excludedCount'])
    && Array.isArray(record['entries'])
    && (record['entries'] as unknown[]).every(isEntry);
}

/**
 * The stored record, or null when there is none, it cannot be read or parsed, it was written under
 * another version, or its shape is not the one this version writes.
 */
export function readLastComparison(storage?: LastComparisonStorageReader): LastComparisonRecord | null {
  let parsed: unknown;
  try {
    const stored = (storage ?? localStorage).getItem(LAST_COMPARISON_STORAGE_KEY);
    if (!stored) {
      return null;
    }
    parsed = JSON.parse(stored);
  } catch {
    return null;
  }
  if (!isRecord(parsed)) {
    return null;
  }
  return { ...parsed, entries: parsed.entries.slice(0, LAST_COMPARISON_MAX_ENTRIES) };
}

/** Stores the record; true when it was written. A storage failure is swallowed. */
export function writeLastComparison(record: LastComparisonRecord, storage?: LastComparisonStorageWriter): boolean {
  try {
    (storage ?? localStorage).setItem(LAST_COMPARISON_STORAGE_KEY, JSON.stringify(record));
    return true;
  } catch {
    return false;
  }
}
