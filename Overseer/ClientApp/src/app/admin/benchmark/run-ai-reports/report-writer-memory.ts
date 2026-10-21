/*
 * The report writer this browser used last, shared by every place that writes AI reports: the run
 * report's and the battery run report's AI Reports tabs, and the Model Comparison wizard's Write step.
 * A per-viewer convenience, so it lives in localStorage behind try/catch; where storage is unavailable
 * nothing is remembered.
 */

/** The shared memory of the last report writer used. */
export const REPORT_WRITER_MEMORY_KEY = 'overseer.benchmark.reportWriter';

/** The Model Comparison wizard's own record: its document scope, its sidebar width and an older writer choice. */
export const REPORT_PACK_STORAGE_KEY = 'overseer.benchmark.reportPack';

/** The record under `REPORT_WRITER_MEMORY_KEY`. */
export interface StoredReportWriter {
  readonly version: 1;
  readonly writerConfigId: number;
}

function isConfigId(value: unknown): value is number {
  return typeof value === 'number' && Number.isSafeInteger(value) && value > 0;
}

/**
 * The configuration id of the report writer used last, or null when none is remembered, the record
 * is unreadable or storage is unavailable. While the shared record is absent, the wizard's own
 * `writerConfigId` stands in for it.
 */
export function rememberedReportWriterId(): number | null {
  try {
    const raw = localStorage.getItem(REPORT_WRITER_MEMORY_KEY);
    if (raw !== null) {
      const stored = JSON.parse(raw) as { version?: unknown; writerConfigId?: unknown } | null;
      const storedId = stored?.writerConfigId;
      return stored?.version === 1 && isConfigId(storedId) ? storedId : null;
    }
    const wizardRaw = localStorage.getItem(REPORT_PACK_STORAGE_KEY);
    if (!wizardRaw) {
      return null;
    }
    const wizard = JSON.parse(wizardRaw) as { writerConfigId?: unknown } | null;
    const wizardId = wizard?.writerConfigId;
    return isConfigId(wizardId) ? wizardId : null;
  } catch {
    return null;
  }
}

/** Remembers the report writer just chosen or used. */
export function rememberReportWriterId(writerConfigId: number): void {
  if (!isConfigId(writerConfigId)) {
    return;
  }
  try {
    const record: StoredReportWriter = { version: 1, writerConfigId };
    localStorage.setItem(REPORT_WRITER_MEMORY_KEY, JSON.stringify(record));
  } catch {
    // Storage unavailable: the choice is simply not remembered.
  }
}
