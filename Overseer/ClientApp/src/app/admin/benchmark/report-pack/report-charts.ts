import { HttpErrorResponse } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';

import {
  AdminBenchmarkService,
  BenchmarkReportAudience,
  ReportDocumentChartUpload
} from '../../../services/admin-benchmark.service';

/*
 * The charts a Report Pack document carries into its PDF and Word copies: which figures, where each
 * lands, the size they are drawn at, the hash of the settings that shaped them, and the publisher
 * that composes and uploads them one document at a time. The wizard supplies the composer, so
 * nothing here knows Chart.js.
 */

export type ReportChartFigureKey =
  'p1a-quality' | 'p1b-speed' | 'p1c-cost' | 'p2-profile' | 's1-quality-speed' | 's2-quality-cost' | 's3-speed-cost';

/** In placement order. */
export const REPORT_CHART_FIGURES: readonly { readonly key: ReportChartFigureKey; readonly title: string; readonly minModels: number }[] = [
  { key: 'p1a-quality', title: 'Intelligence', minModels: 2 },
  { key: 'p1b-speed', title: 'Speed', minModels: 2 },
  { key: 'p1c-cost', title: 'Cost', minModels: 2 },
  { key: 'p2-profile', title: 'Model profiles', minModels: 3 },
  { key: 's1-quality-speed', title: 'Intelligence against speed', minModels: 2 },
  { key: 's2-quality-cost', title: 'Intelligence against cost', minModels: 2 },
  { key: 's3-speed-cost', title: 'Speed against cost', minModels: 2 }
];

const FIGURE_KEYS: ReadonlySet<string> = new Set<string>(REPORT_CHART_FIGURES.map(figure => figure.key));

const KNOWN_AUDIENCES: readonly BenchmarkReportAudience[] = [
  BenchmarkReportAudience.ExecutiveSummary,
  BenchmarkReportAudience.TechnicalReport,
  BenchmarkReportAudience.InternalBrief
];

export function isReportChartFigureKey(value: string): value is ReportChartFigureKey {
  return FIGURE_KEYS.has(value);
}

/** The section of the Report for AI Researchers and Developers each figure lands in; mirrors the server's placement table. */
const TECHNICAL_PLACEMENT: Readonly<Record<ReportChartFigureKey, string>> = {
  'p1a-quality': 'Results against peers → Quality',
  'p1b-speed': 'Results against peers → Speed',
  'p1c-cost': 'Results against peers → Cost',
  'p2-profile': 'Results against peers',
  's1-quality-speed': 'Speed and cost',
  's2-quality-cost': 'Speed and cost',
  's3-speed-cost': 'Speed and cost'
};

/** The section a figure lands in for a document type: 'How it compares', 'Results against peers → Quality', … */
export function reportChartPlacementLabel(audience: BenchmarkReportAudience, key: ReportChartFigureKey): string {
  switch (audience) {
    case BenchmarkReportAudience.ExecutiveSummary:
      return 'How it compares';
    case BenchmarkReportAudience.TechnicalReport:
      return TECHNICAL_PLACEMENT[key] ?? 'Results against peers';
    case BenchmarkReportAudience.InternalBrief:
      return '§3 Key figures';
    default:
      return '';
  }
}

/** Per document type, the figures to include, in placement order. A missing audience means none. */
export type ReportChartSelection = Readonly<Partial<Record<BenchmarkReportAudience, readonly ReportChartFigureKey[]>>>;

export const DEFAULT_CHART_SELECTION: ReportChartSelection = Object.freeze({
  [BenchmarkReportAudience.ExecutiveSummary]: Object.freeze(['p1a-quality', 's2-quality-cost'] as ReportChartFigureKey[]),
  [BenchmarkReportAudience.TechnicalReport]: Object.freeze(REPORT_CHART_FIGURES.map(figure => figure.key)),
  [BenchmarkReportAudience.InternalBrief]: Object.freeze(['p1a-quality', 'p1b-speed', 'p1c-cost'] as ReportChartFigureKey[])
});

export const REPORT_CHART_STORAGE_KEY = 'overseer.benchmark.reportCharts';

/** The stored record's version. */
const STORED_SELECTION_VERSION = 1;

/** Dedupes, puts in placement order and drops unknown keys and audiences. An audience present with no figures stays, as none. */
export function normalizeChartSelection(selection: ReportChartSelection): ReportChartSelection {
  const normalized: Partial<Record<BenchmarkReportAudience, readonly ReportChartFigureKey[]>> = {};
  if (!selection || typeof selection !== 'object') {
    return normalized;
  }
  const source = selection as Record<string | number, unknown>;
  for (const audience of KNOWN_AUDIENCES) {
    const keys = source[audience];
    if (!Array.isArray(keys)) {
      continue;
    }
    const known = new Set(keys.filter((key): key is ReportChartFigureKey => typeof key === 'string' && isReportChartFigureKey(key)));
    normalized[audience] = REPORT_CHART_FIGURES.map(figure => figure.key).filter(key => known.has(key));
  }
  return normalized;
}

/** Version-1 record in localStorage, try/catch; DEFAULT_CHART_SELECTION when absent or unreadable. */
export function readStoredChartSelection(): ReportChartSelection {
  try {
    const raw = localStorage.getItem(REPORT_CHART_STORAGE_KEY);
    if (!raw) {
      return DEFAULT_CHART_SELECTION;
    }
    const parsed = JSON.parse(raw) as { version?: unknown; selection?: unknown } | null;
    if (!parsed || parsed.version !== STORED_SELECTION_VERSION || !parsed.selection || typeof parsed.selection !== 'object') {
      return DEFAULT_CHART_SELECTION;
    }
    return normalizeChartSelection(parsed.selection as ReportChartSelection);
  } catch {
    return DEFAULT_CHART_SELECTION;
  }
}

export function storeChartSelection(selection: ReportChartSelection): void {
  try {
    localStorage.setItem(REPORT_CHART_STORAGE_KEY, JSON.stringify({
      version: STORED_SELECTION_VERSION,
      selection: normalizeChartSelection(selection)
    }));
  } catch {
    // Storage unavailable: the selection is simply not remembered.
  }
}

/** 1800 px wide; 16:10 (1800×1125) for bars and scatters, 4:3 (1800×1350) for the profile; text scale 175 %; PNG. */
export const DOCUMENT_CHART_LAYOUT: {
  readonly widthPx: 1800; readonly barHeightPx: 1125; readonly scatterHeightPx: 1125; readonly profileHeightPx: 1350;
  readonly textScalePercent: 175; readonly format: 'png';
} = Object.freeze({
  widthPx: 1800,
  barHeightPx: 1125,
  scatterHeightPx: 1125,
  profileHeightPx: 1350,
  textScalePercent: 175,
  format: 'png'
} as const);

export function documentChartSize(key: ReportChartFigureKey): { widthPx: number; heightPx: number } {
  const heightPx = key === 'p2-profile'
    ? DOCUMENT_CHART_LAYOUT.profileHeightPx
    : key.startsWith('s')
      ? DOCUMENT_CHART_LAYOUT.scatterHeightPx
      : DOCUMENT_CHART_LAYOUT.barHeightPx;
  return { widthPx: DOCUMENT_CHART_LAYOUT.widthPx, heightPx };
}

/** What shapes the images; hashed as canonical JSON (object keys sorted recursively). */
export interface ReportChartSettingsInput {
  readonly figureStyle: unknown;
  readonly layout: typeof DOCUMENT_CHART_LAYOUT;
  readonly show: unknown;
  readonly highlight: unknown;
  readonly order: unknown;
  readonly measures: unknown;
  readonly pricingBasis: string;
  readonly computedAtUtc: string | null;
}

function canonicalPart(value: unknown): string | undefined {
  if (value === null) {
    return 'null';
  }
  switch (typeof value) {
    case 'string':
      return JSON.stringify(value);
    case 'number':
      return Number.isFinite(value) ? JSON.stringify(value) : 'null';
    case 'boolean':
      return value ? 'true' : 'false';
    case 'bigint':
      return JSON.stringify(value.toString());
    case 'object':
      break;
    default:
      // undefined, functions and symbols are left out, as JSON.stringify leaves them out.
      return undefined;
  }
  const toJson = (value as { toJSON?: unknown }).toJSON;
  if (typeof toJson === 'function') {
    return canonicalPart((toJson as () => unknown).call(value));
  }
  if (Array.isArray(value)) {
    return `[${value.map(item => canonicalPart(item) ?? 'null').join(',')}]`;
  }
  const record = value as Record<string, unknown>;
  const parts: string[] = [];
  for (const key of Object.keys(record).sort()) {
    const part = canonicalPart(record[key]);
    if (part !== undefined) {
      parts.push(`${JSON.stringify(key)}:${part}`);
    }
  }
  return `{${parts.join(',')}}`;
}

/** JSON with every object's keys sorted, at every depth, and no whitespace; the same value always gives the same text. */
export function canonicalJson(value: unknown): string {
  return canonicalPart(value) ?? 'null';
}

/** SHA-256 via crypto.subtle over canonicalJson(input); 64 lowercase hex characters. */
export async function chartSettingsHash(input: ReportChartSettingsInput): Promise<string> {
  const subtle = typeof crypto !== 'undefined' ? crypto.subtle : undefined;
  if (!subtle) {
    throw new Error('SHA-256 is not available: crypto.subtle needs a secure context.');
  }
  const digest = await subtle.digest('SHA-256', new TextEncoder().encode(canonicalJson(input)));
  return Array.from(new Uint8Array(digest), byte => byte.toString(16).padStart(2, '0')).join('');
}

export const CHART_STORAGE_NOT_CONFIGURED = 'Chart storage is not configured';

export type ReportChartVariant =
  | { readonly kind: 'named' }
  | { readonly kind: 'anonymized'; readonly subjectKey: string; readonly letters: Readonly<Record<string, string>> };

export interface ComposedReportChart {
  readonly png: Blob; readonly widthPx: number; readonly heightPx: number;
  readonly title: string; readonly caption: string; readonly altText: string;
}

export type ReportChartComposer = (key: ReportChartFigureKey, variant: ReportChartVariant) => Promise<ComposedReportChart>;

/** A document to chart. */
export interface ReportChartTarget {
  readonly documentId: number;
  readonly audience: BenchmarkReportAudience;
  readonly subjectKey: string;
  /** Peer entry key → letter, from the document list (`peerLetters`). Without letters no anonymized variant is drawn. */
  readonly peerLetters: Readonly<Record<string, string>>;
  readonly label: string;
}

export interface ReportChartPublishProgress { readonly done: number; readonly total: number; readonly step: string; readonly documentId: number | null; }

export interface ReportChartPublishResult {
  readonly published: readonly { readonly documentId: number; readonly chartCount: number }[];
  readonly failed: readonly { readonly documentId: number; readonly message: string }[];
  /** Targets whose selection for their audience was empty; nothing was uploaded for them. */
  readonly skipped: readonly number[];
  readonly canceled: boolean;
  /** The server's message, once, when a 400 said chart storage is not configured; the publish stopped there. */
  readonly storageNotConfigured: string | null;
}

/** Per-document chart state shown in the Reports panel's progress list. */
export type ReportChartRowStatus =
  | { readonly state: 'attaching' }
  | { readonly state: 'done'; readonly count: number }
  | { readonly state: 'failed'; readonly message: string }
  | { readonly state: 'skipped'; readonly reason: string };

function errorText(error: unknown): string {
  if (error instanceof HttpErrorResponse) {
    const body = error.error;
    if (typeof body === 'string' && body.trim()) {
      return body.trim();
    }
    if (body && typeof body === 'object') {
      const message = (body as { error?: unknown }).error ?? (body as { message?: unknown }).message;
      if (typeof message === 'string' && message.trim()) {
        return message.trim();
      }
    }
    return error.status > 0 ? `HTTP ${error.status}${error.statusText ? ` ${error.statusText}` : ''}` : 'The server could not be reached.';
  }
  if (error instanceof Error && error.message) {
    return error.message;
  }
  return typeof error === 'string' && error ? error : 'Unknown error';
}

async function blobToBase64(blob: Blob): Promise<string> {
  const bytes = new Uint8Array(await blob.arrayBuffer());
  const chunk = 0x8000;
  let binary = '';
  for (let i = 0; i < bytes.length; i += chunk) {
    binary += String.fromCharCode(...Array.from(bytes.subarray(i, i + chunk)));
  }
  return btoa(binary);
}

function variantsFor(target: ReportChartTarget): ReportChartVariant[] {
  const variants: ReportChartVariant[] = [{ kind: 'named' }];
  if (target.peerLetters && Object.keys(target.peerLetters).length > 0) {
    variants.push({ kind: 'anonymized', subjectKey: target.subjectKey, letters: target.peerLetters });
  }
  return variants;
}

function figureTitle(key: ReportChartFigureKey): string {
  return REPORT_CHART_FIGURES.find(figure => figure.key === key)?.title ?? key;
}

/**
 * Composes and uploads the charts of Report Pack documents, one document at a time. One publish
 * runs at a time per instance; `cancel()` stops it after the document in flight.
 */
export class ReportChartPublisher {
  private runningNow = false;
  private cancelRequested = false;

  constructor(private readonly service: AdminBenchmarkService) {}

  get running(): boolean {
    return this.runningNow;
  }

  /**
   * One document at a time: for each target, composes every selected figure for its audience, named and
   * (when the target has peer letters) anonymized, converts each Blob to base64, and PUTs the whole set.
   * A figure the composer rejects is left out of the set (its error is recorded in the step text);
   * a failed PUT is recorded and the rest continue; a 400 containing CHART_STORAGE_NOT_CONFIGURED stops everything.
   * An empty set for a target (every figure failed to compose) is a failure for that target, not a PUT.
   */
  async publish(
    targets: readonly ReportChartTarget[],
    selection: ReportChartSelection,
    compose: ReportChartComposer,
    settingsHash: string,
    onProgress?: (progress: ReportChartPublishProgress) => void
  ): Promise<ReportChartPublishResult> {
    if (this.runningNow) {
      throw new Error('A chart publish is already running.');
    }
    this.runningNow = true;
    this.cancelRequested = false;

    const normalized = normalizeChartSelection(selection);
    const published: { documentId: number; chartCount: number }[] = [];
    const failed: { documentId: number; message: string }[] = [];
    const skipped: number[] = [];
    let storageNotConfigured: string | null = null;
    let canceled = false;

    // One unit per image to compose and one per upload.
    const total = targets.reduce((sum, target) => {
      const figures = normalized[target.audience]?.length ?? 0;
      return figures === 0 ? sum : sum + figures * variantsFor(target).length + 1;
    }, 0);
    let done = 0;
    const report = (step: string, documentId: number | null): void => {
      onProgress?.({ done, total, step, documentId });
    };

    try {
      for (const target of targets) {
        if (this.cancelRequested) {
          canceled = true;
          break;
        }
        const figures = normalized[target.audience] ?? [];
        if (figures.length === 0) {
          skipped.push(target.documentId);
          continue;
        }

        const uploads: ReportDocumentChartUpload[] = [];
        const composeErrors: string[] = [];
        for (const key of figures) {
          for (const variant of variantsFor(target)) {
            const what = `${figureTitle(key)} (${variant.kind})`;
            report(`Drawing ${what} for ${target.label}`, target.documentId);
            try {
              const chart = await compose(key, variant);
              uploads.push({
                figureKey: key,
                naming: variant.kind,
                title: chart.title,
                caption: chart.caption,
                altText: chart.altText,
                settingsHash,
                pngBase64: await blobToBase64(chart.png)
              });
              done++;
            } catch (error) {
              done++;
              const message = errorText(error);
              composeErrors.push(`${what}: ${message}`);
              report(`Could not draw ${what} for ${target.label}: ${message}`, target.documentId);
            }
          }
        }

        if (uploads.length === 0) {
          done++;
          failed.push({
            documentId: target.documentId,
            message: `No chart could be drawn${composeErrors.length > 0 ? `: ${composeErrors[0]}` : '.'}`
          });
          continue;
        }

        report(`Attaching ${uploads.length} ${uploads.length === 1 ? 'chart' : 'charts'} to ${target.label}`, target.documentId);
        try {
          const summary = await firstValueFrom(this.service.putReportDocumentCharts(target.documentId, uploads));
          done++;
          published.push({ documentId: target.documentId, chartCount: summary?.chartCount ?? uploads.length });
        } catch (error) {
          done++;
          const message = errorText(error);
          if (error instanceof HttpErrorResponse && error.status === 400 && message.includes(CHART_STORAGE_NOT_CONFIGURED)) {
            // Reported once, not as one failure per document; nothing after this target is attempted.
            storageNotConfigured = message;
            break;
          }
          failed.push({ documentId: target.documentId, message });
        }
      }
    } finally {
      this.runningNow = false;
      this.cancelRequested = false;
    }

    const step = storageNotConfigured
      ?? (canceled ? 'Canceled' : `Charts attached to ${published.length} of ${targets.length - skipped.length} ${targets.length - skipped.length === 1 ? 'document' : 'documents'}`);
    report(step, null);
    return { published, failed, skipped, canceled, storageNotConfigured };
  }

  /** Stops after the document in flight; the result reports canceled: true. */
  cancel(): void {
    if (this.runningNow) {
      this.cancelRequested = true;
    }
  }
}
