import { ChangeDetectionStrategy, Component, EventEmitter, Input, Output } from '@angular/core';
import { DatePipe } from '@angular/common';

import { InfoTipComponent } from '../../../../shared/info-tip/info-tip.component';
import { ProviderBadgeComponent } from '../../../../shared/provider-badge/provider-badge.component';
import { formatIndexText, formatMsText, formatUsdText } from '../../model-comparison/table-export';
import type { LastComparisonEntry, LastComparisonRecord } from '../last-comparison';

/** The report documents written from the remembered comparison. */
export interface LastComparisonDocuments {
  count: number;
  latestAtUtc: string | null;
}

/**
 * The Model Comparison launcher's *Last comparison* card: the comparison last computed in this
 * browser, its figures per entry, and one action that hands its selection back to the wizard. A
 * read-out of a stored record; it fetches nothing itself.
 */
@Component({
  selector: 'app-comparison-summary-card',
  standalone: true,
  imports: [DatePipe, InfoTipComponent, ProviderBadgeComponent],
  templateUrl: './comparison-summary-card.component.html',
  styleUrls: ['./comparison-summary-card.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class ComparisonSummaryCardComponent {
  @Input({ required: true }) record!: LastComparisonRecord;

  /** The documents count, or null while it is unknown or could not be read; the fact is then omitted. */
  @Input() documents: LastComparisonDocuments | null = null;

  /** Open in wizard, with the record whose selection the wizard is to take. */
  @Output() readonly openInWizard = new EventEmitter<LastComparisonRecord>();

  /** The meta line before the date: the subject, then the scope where there is one. */
  get metaLead(): string {
    const subject = this.record.subjectKind === 'Batteries' ? 'Battery results' : 'Runs and groups';
    return this.record.scopeName ? `${subject} · ${this.record.scopeName}` : subject;
  }

  get pricingText(): string {
    return this.record.pricingBasis === 'AsRun' ? 'As run' : 'Catalog prices';
  }

  /** `2 of 3 entries`, and `· 1 excluded` when some are. */
  get chartedText(): string {
    const total = this.record.comparableCount + this.record.excludedCount;
    const charted = `${this.record.comparableCount} of ${total} ${total === 1 ? 'entry' : 'entries'}`;
    return this.record.excludedCount > 0 ? `${charted} · ${this.record.excludedCount} excluded` : charted;
  }

  formatIndex(value: number | null): string {
    return formatIndexText(value);
  }

  formatMs(value: number | null): string {
    return formatMsText(value);
  }

  formatUsd(value: number | null): string {
    return formatUsdText(value);
  }

  /** The 95 % interval under the point, or null when the record has none. */
  intervalText(entry: LastComparisonEntry): string | null {
    return entry.qualityLower === null || entry.qualityUpper === null
      ? null
      : `${formatIndexText(entry.qualityLower)}–${formatIndexText(entry.qualityUpper)}`;
  }

  /** The tip's id: unique in the document, and a valid anchor name. */
  statusTipId(index: number): string {
    return `mc-last-status-tip-${index}`;
  }

  onOpenInWizard(): void {
    this.openInWizard.emit(this.record);
  }
}
