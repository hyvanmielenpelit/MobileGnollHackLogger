import { ChangeDetectionStrategy, Component, EventEmitter, Input, OnChanges, Output } from '@angular/core';

import { formatUtcDate, formatUtcDateTime, plural } from '../chat-consistency-format';
import { CcCompareKind, CcOverallOutcome, ccCompareKindOf, ccOverallOutcome } from '../chat-consistency-results';
import { CcAnalysisResult, CcAnalysisSummary, CcModelAxis } from '../chat-consistency.models';
import { CcEndpointChipsComponent } from '../endpoint-chips/endpoint-chips.component';
import { CcBadgedModel, CcModelBadgesComponent } from '../model-badges/model-badges.component';

/** One period's fact on the card. */
export interface CcLatestPeriodFact {
  period: 'baseline' | 'comparison';
  label: string;
  /** `2026-10-08`, or `2026-09-01 – 2026-09-14` over several days. */
  dates: string;
  /** `1 battery run`, `3 runs`; null until the result is loaded. */
  units: string | null;
}

/** The reason Documents cannot open while no document has been written. */
export const CC_LATEST_NO_DOCUMENTS = 'No report document has been written from this analysis.';

/** `2026-10-08`, or `2026-09-01 – 2026-09-14` when the period spans several UTC days. */
function periodDates(startUtc: string, endUtc: string): string {
  const start = formatUtcDate(startUtc);
  const end = formatUtcDate(endUtc);
  return start === end ? start : `${start} – ${end}`;
}

/**
 * The launcher's *Latest analysis* card: the newest saved analysis of any model, with its outcome and
 * endpoint chips from the full result, its periods, when it was saved and its report documents, and
 * the gold **Open Analysis #N** (the wizard on Results) beside **Documents (N)** (the wizard on step 6).
 * Its summary shows at once; the outcome and the chips wait for the result the host fetches.
 */
@Component({
  selector: 'app-cc-latest-analysis-card',
  standalone: true,
  imports: [CcModelBadgesComponent, CcEndpointChipsComponent],
  templateUrl: './latest-analysis-card.component.html',
  styleUrls: ['./latest-analysis-card.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class CcLatestAnalysisCardComponent implements OnChanges {
  /** The newest saved analysis; null when none is saved. */
  @Input() summary: CcAnalysisSummary | null = null;
  /** Its full result, once fetched. */
  @Input() result: CcAnalysisResult | null = null;
  /** The result is being fetched. */
  @Input() loading = false;
  /** Why the result could not be fetched. */
  @Input() error: string | null = null;
  /** This analysis is being opened in the wizard. */
  @Input() opening = false;
  /** Why the last open started from this card failed. */
  @Input() openError: string | null = null;
  /** The saved analyses are being read, so whether one exists is not known yet. */
  @Input() listLoading = false;
  /** Why the saved analyses could not be read. */
  @Input() listError: string | null = null;
  /** The model axes, for the badges of a summary that does not name its model. */
  @Input() axes: readonly CcModelAxis[] = [];

  /** The wizard on this analysis's results, by id. */
  @Output() readonly openAnalysis = new EventEmitter<number>();
  /** The wizard on this analysis's documents (step 6), by id. */
  @Output() readonly openDocuments = new EventEmitter<number>();

  readonly noDocumentsReason = CC_LATEST_NO_DOCUMENTS;

  outcome: CcOverallOutcome | null = null;
  compareKind: CcCompareKind = ccCompareKindOf(null);
  badges: CcBadgedModel | null = null;
  periods: CcLatestPeriodFact[] = [];

  ngOnChanges(): void {
    const summary = this.summary;
    const result = this.shownResult;
    this.outcome = result ? ccOverallOutcome(result) : null;
    this.compareKind = ccCompareKindOf(summary?.comparisonSetKey);
    this.badges = result?.subject ?? summary?.subject ?? this.axes.find(axis => axis.key === summary?.subjectModelKey) ?? null;
    this.periods = summary
      ? [
        { period: 'baseline', label: 'Baseline', dates: periodDates(summary.baselineStartUtc, summary.baselineEndUtc), units: this.unitsOf(result, 'baseline') },
        { period: 'comparison', label: 'Comparison', dates: periodDates(summary.comparisonStartUtc, summary.comparisonEndUtc), units: this.unitsOf(result, 'comparison') }
      ]
      : [];
  }

  /** The result for this summary, or null while another analysis's or none is held. */
  get shownResult(): CcAnalysisResult | null {
    return this.result && this.summary && this.result.analysisId === this.summary.id ? this.result : null;
  }

  get title(): string {
    const summary = this.summary;
    if (!summary) return '';
    return `#${summary.id} · ${summary.name?.trim() || `Analysis #${summary.id}`}`;
  }

  get savedText(): string {
    return this.summary ? `${formatUtcDateTime(this.summary.createdAtUtc)} · Protocol ${this.summary.protocolVersion}` : '';
  }

  get documentCount(): number {
    return this.summary?.reportDocumentCount ?? 0;
  }

  get reportsText(): string {
    return this.documentCount > 0 ? plural(this.documentCount, 'report document') : 'None yet';
  }

  onOpen(): void {
    if (this.summary && !this.opening) this.openAnalysis.emit(this.summary.id);
  }

  onDocuments(): void {
    if (this.summary && !this.opening && this.documentCount > 0) this.openDocuments.emit(this.summary.id);
  }

  /** A period's units: battery runs in a battery analysis, runs otherwise; null without a result. */
  private unitsOf(result: CcAnalysisResult | null, period: 'baseline' | 'comparison'): string | null {
    if (!result) return null;
    const units = result.units ?? [];
    if (result.unitKind === 'batteryRun' && units.length > 0) {
      return plural(units.filter(unit => unit.period === period).length, 'battery run');
    }
    return plural((period === 'baseline' ? result.baseline : result.comparison).runCount, 'run');
  }
}
