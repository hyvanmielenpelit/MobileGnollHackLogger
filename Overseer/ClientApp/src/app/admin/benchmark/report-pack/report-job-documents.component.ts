import { ChangeDetectionStrategy, Component, EventEmitter, Input, Output } from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';

import { BenchmarkReportAudience } from '../../../services/admin-benchmark.service';
import { InfoTipComponent } from '../../../shared/info-tip/info-tip.component';
import { formatCostUsd } from './report-document-format';
import { ReportValidationDetailsComponent } from './report-validation-details.component';
import { DocumentStatusInput } from './report-validation-explanation';

/** A document card's charts line: its words, which are the retry button's after a failure. */
export interface ReportJobDocumentCharts {
  readonly text: string;
  readonly retry: boolean;
}

/** The explanation beside a card's chip: the info button's subject and the dialog's title. */
export interface ReportJobDocumentWhy {
  readonly subject: string;
  readonly title: string;
}

/** One document of a report-writing job, as its progress card shows it. */
export interface ReportJobDocumentCard {
  /** Unique in the list. */
  readonly key: string;
  readonly audience: BenchmarkReportAudience;
  /** The subject key, for `data-subject`; null when the list does not tell subjects apart. */
  readonly subject: string | null;
  /** The document type: *Root Cause Analysis*. */
  readonly title: string;
  /** The meta line's parts (the model of a per-model document); empty for no meta line. */
  readonly meta: readonly string[];
  /** The document inside a sentence, for accessible names: *the Root Cause Analysis of GPT Sol*. */
  readonly phrase: string;
  readonly statusWord: string;
  readonly chipClass: string;
  /** `warning` or `failed` adds a colored edge beside the chip's words; null for none. */
  readonly tone: 'warning' | 'failed' | null;
  /** The step the document is at, or how its draft was checked; null when there is nothing to say. */
  readonly phaseLine: string | null;
  readonly errorMessage: string | null;
  readonly duration: string;
  readonly modelCalls: string;
  /** `390,985 in · 11,875 out`. */
  readonly tokens: string;
  readonly cost: string;
  /** Null hides the charts line: the document has no charts. */
  readonly charts: ReportJobDocumentCharts | null;
  /** The document is stored and View is offered. */
  readonly viewable: boolean;
  readonly documentId: number | null;
  /** What the info tips explain; null offers neither. */
  readonly doc: DocumentStatusInput | null;
  /** The stem of the card's info tip ids, unique in the document. */
  readonly tipStem: string;
  /** The status explanation, for a document completed with warnings or failed; null otherwise. */
  readonly why: ReportJobDocumentWhy | null;
  /** The first draft's issue count; 0 offers no first-draft info button. */
  readonly draftIssueCount: number;
  /** The first-draft button sits beside the chip while the document is written, beside the phase line once finished. */
  readonly draftTipBesideChip: boolean;
  /** The server's sentence on the current phase, leading the first-draft dialog. */
  readonly phaseDetail: string | null;
}

/** Defaults for a card's optional parts: no meta line, no tips, no charts, no View. */
export const REPORT_JOB_DOCUMENT_CARD_DEFAULTS = {
  subject: null,
  meta: [] as readonly string[],
  tone: null,
  phaseLine: null,
  errorMessage: null,
  charts: null,
  viewable: false,
  documentId: null,
  doc: null,
  why: null,
  draftIssueCount: 0,
  draftTipBesideChip: false,
  phaseDetail: null
} satisfies Partial<ReportJobDocumentCard>;

const numberFormat = new Intl.NumberFormat('en-US');

/** `390,985 in · 11,875 out`; `—` when neither count is known. */
export function reportJobDocumentTokens(inputTokens: number | null | undefined, outputTokens: number | null | undefined): string {
  if ((inputTokens === null || inputTokens === undefined) && (outputTokens === null || outputTokens === undefined)) {
    return '—';
  }
  return `${numberFormat.format(inputTokens ?? 0)} in · ${numberFormat.format(outputTokens ?? 0)} out`;
}

/** The document's cost; `—` from a server that sends none, `Unknown` when the writer had no price. */
export function reportJobDocumentCost(costUsd: number | null | undefined): string {
  return costUsd === undefined ? '—' : formatCostUsd(costUsd);
}

function sameCard(a: ReportJobDocumentCard, b: ReportJobDocumentCard): boolean {
  for (const key of Object.keys(a) as (keyof ReportJobDocumentCard)[]) {
    const x = a[key];
    const y = b[key];
    if (x === y) {
      continue;
    }
    if (key === 'meta') {
      const p = x as readonly string[];
      const q = y as readonly string[];
      if (p.length !== q.length || p.some((part, i) => part !== q[i])) {
        return false;
      }
    } else if (key === 'charts' || key === 'why') {
      if (!x || !y || JSON.stringify(x) !== JSON.stringify(y)) {
        return false;
      }
    } else {
      return false;
    }
  }
  return Object.keys(a).length === Object.keys(b).length;
}

/**
 * `previous` when `next` holds the same cards, else `next`: a host builds its cards in a getter on every
 * change detection pass, and the list's input must not change while nothing it shows has.
 */
export function reuseReportJobDocumentCards<T extends ReportJobDocumentCard>(previous: readonly T[], next: T[]): readonly T[] {
  return previous.length === next.length && next.every((card, i) => sameCard(previous[i], card)) ? previous : next;
}

/** The edge a card's status adds beside the chip's words. */
export function reportJobDocumentTone(status: string): 'warning' | 'failed' | null {
  switch (status) {
    case 'CompletedWithWarnings': return 'warning';
    case 'Failed': return 'failed';
    default: return null;
  }
}

/**
 * The progress of each document of a report-writing job, as a read-only card list: the title and its
 * status chip (with the explanation of a warning or a failure, and the first draft's issues while it is
 * revised), an optional meta line, the phase line in full, the error, the duration, model calls, tokens
 * and cost, and a footer with the charts (a retry after a failure) and View. Laid out by the list's own
 * width. The Model Comparison wizard's Write step, the Chat Consistency reports step and the run report
 * writing dialog all show their documents with it.
 */
@Component({
  selector: 'app-report-job-documents',
  standalone: true,
  imports: [InfoTipComponent, ReportValidationDetailsComponent, NgTemplateOutlet],
  templateUrl: './report-job-documents.component.html',
  styleUrls: ['./report-job-documents.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class ReportJobDocumentsComponent {
  /** One card per document, in order. */
  @Input() rows: readonly ReportJobDocumentCard[] = [];

  /** The prefix of every id in the list, unique in the document. */
  @Input() idPrefix = 'rjd';

  /** The list's accessible name. */
  @Input() label = 'Progress of each document';

  /** *Charts failed — retry* on a card. */
  @Output() readonly retryCharts = new EventEmitter<ReportJobDocumentCard>();

  /** View on a card of a stored document. */
  @Output() readonly view = new EventEmitter<ReportJobDocumentCard>();

  /** The card's id: the prefix and its key, with any character an id cannot hold replaced. */
  cardId(row: ReportJobDocumentCard): string {
    return `${this.idPrefix}-${row.key.replace(/[^A-Za-z0-9_-]/g, '-')}`;
  }
}
