import {
  ChangeDetectionStrategy,
  ChangeDetectorRef,
  Component,
  EventEmitter,
  Input,
  OnChanges,
  OnDestroy,
  Output,
  SimpleChanges,
  inject
} from '@angular/core';
import { Subscription } from 'rxjs';

import { AdminChatConsistencyService } from '../../../../../services/admin-chat-consistency.service';
import { InfoTipComponent } from '../../../../../shared/info-tip/info-tip.component';
import {
  CC_OUT_OF_DATE_ADVICE,
  CC_OUT_OF_DATE_RULE,
  ccInputsUncheckedText,
  ccOutOfDateReasons
} from '../../chat-consistency-results';
import { CcAnalysisFreshness } from '../../chat-consistency.models';

/**
 * Whether the shown saved analysis is out of date, on the Results and Write steps. It reads the
 * analysis's freshness once per analysis id and shows nothing while it loads, after a failure, or
 * while the analysis is current. Out of date: an amber notice with the reasons and **Analyze again**.
 * Current code whose inputs could not be checked: one quiet line saying why.
 */
@Component({
  selector: 'app-cc-freshness-notice',
  standalone: true,
  imports: [InfoTipComponent],
  templateUrl: './freshness-notice.component.html',
  styleUrls: ['./freshness-notice.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class CcFreshnessNoticeComponent implements OnChanges, OnDestroy {
  private readonly service = inject(AdminChatConsistencyService);
  private readonly cdr = inject(ChangeDetectorRef);

  /** The shown analysis; null for an unsaved one, which has no freshness. */
  @Input() analysisId: number | null = null;
  /** Prefixes the element ids, so the Results and Write steps can share the document. */
  @Input() idPrefix = 'cc-fresh';

  /** *Analyze again*: the host restores the analysis's settings on Analyze. */
  @Output() readonly analyzeAgain = new EventEmitter<void>();
  /** Each freshness the server answers, for the Write step's confirmation. */
  @Output() readonly freshnessChange = new EventEmitter<CcAnalysisFreshness>();

  readonly rule = CC_OUT_OF_DATE_RULE;
  readonly advice = CC_OUT_OF_DATE_ADVICE;

  freshness: CcAnalysisFreshness | null = null;
  /** The reason sentences of an out-of-date analysis; empty otherwise. */
  reasons: string[] = [];
  /** `Changes since saving could not be checked: …`; null when there is nothing to say. */
  uncheckedText: string | null = null;

  /** The analysis id the freshness was last requested for. */
  private requestedId: number | null = null;
  private sub: Subscription | null = null;

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['analysisId'] && this.analysisId !== this.requestedId) this.load();
  }

  ngOnDestroy(): void {
    this.sub?.unsubscribe();
  }

  get outOfDate(): boolean {
    return this.freshness?.outOfDate === true;
  }

  private load(): void {
    this.sub?.unsubscribe();
    this.sub = null;
    const id = this.analysisId;
    this.requestedId = id;
    this.apply(null);
    if (id === null) return;
    this.sub = this.service.getAnalysisFreshness(id).subscribe({
      next: freshness => {
        if (id !== this.analysisId) return;
        this.apply(freshness);
        this.freshnessChange.emit(freshness);
        this.cdr.markForCheck();
      },
      error: err => {
        // Nothing is shown: the notice is advice, and the server refuses an out-of-date write itself.
        console.error(`The freshness of analysis #${id} could not be checked.`, err);
      }
    });
  }

  private apply(freshness: CcAnalysisFreshness | null): void {
    this.freshness = freshness;
    this.reasons = ccOutOfDateReasons(freshness);
    this.uncheckedText = freshness && !freshness.outOfDate ? ccInputsUncheckedText(freshness) : null;
  }
}
