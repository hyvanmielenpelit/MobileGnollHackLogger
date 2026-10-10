import { ChangeDetectionStrategy, Component, Input } from '@angular/core';

import { CcEventGroup, eventGroupChangesText } from '../../chat-consistency-events';
import {
  CC_READINESS_STATUS_TEXT,
  CcEndpointReadiness,
  CcPreviewNote,
  CcReadinessStatus,
  ccNothingEstablishable
} from '../../chat-consistency-readiness';
import { CcComparisonSet, CcModelAxis } from '../../chat-consistency.models';
import { CcModelBadgesComponent } from '../../model-badges/model-badges.component';

/** One fact of the Input list, after Model and Compared. */
export interface CcPreviewFact {
  key: string;
  term: string;
  value: string;
  /** A visible second line. */
  note?: string;
}

/** The evidence warning over the endpoint list when no endpoint can be better than Indicated. */
export const CC_NOTHING_ESTABLISHABLE_TEXT = 'With this selection nothing can be Established.';
export const CC_EVIDENCE_REASON_TEXT = 'Each endpoint below says why. The analysis can still be made.';

/** Where an endpoint that needs a common grader points to. */
export const CC_REGRADE_POINTER_TEXT = 'Re-grade with a common assessor is under Controls in the analysis settings.';

/** What the Compared fact's tag reads: a battery, a suite, or every suite run by run. */
export interface CcPreviewKind {
  kind: 'battery' | 'suite' | 'all';
  text: string;
}

/**
 * The Analyze step's *Preview*: what the analysis will be sent, which primary endpoints can reach a
 * verdict on the chosen periods, and the notes that may change or qualify the result. When no endpoint
 * can be better than Indicated, a warning over the endpoint list says so beforehand; it never blocks.
 * Read-only; every figure is built by the host from the readiness module, and the server applies the
 * protocol itself.
 */
@Component({
  selector: 'app-cc-analysis-preview',
  standalone: true,
  imports: [CcModelBadgesComponent],
  templateUrl: './analysis-preview.component.html',
  styleUrls: ['./analysis-preview.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class CcAnalysisPreviewComponent {
  @Input() axis: CcModelAxis | null = null;
  @Input() compareSet: CcComparisonSet | null = null;
  /** Name, Split rule, Baseline, Comparison, Not used, Left out in step 1, Control runs, Grading, Pooling, Protocol: built by the host. */
  @Input() facts: readonly CcPreviewFact[] = [];
  /** Why the periods cannot be analyzed; '' when they can. */
  @Input() refusal = '';
  @Input() endpoints: readonly CcEndpointReadiness[] = [];
  @Input() notes: readonly CcPreviewNote[] = [];

  readonly nothingEstablishableText = CC_NOTHING_ESTABLISHABLE_TEXT;
  readonly evidenceReasonText = CC_EVIDENCE_REASON_TEXT;
  readonly regradePointer = CC_REGRADE_POINTER_TEXT;

  /** No endpoint can be better than Indicated, or be computed at all, on valid periods. */
  get nothingEstablishable(): boolean {
    return !this.refusal && ccNothingEstablishable(this.endpoints);
  }

  get compareKind(): CcPreviewKind {
    if (!this.compareSet) return { kind: 'all', text: 'All suites' };
    return this.compareSet.kind === 'battery' ? { kind: 'battery', text: 'Battery' } : { kind: 'suite', text: 'Suite' };
  }

  get compareLabel(): string {
    return this.compareSet?.label ?? 'Runs analyzed one by one';
  }

  statusText(status: CcReadinessStatus): string {
    return CC_READINESS_STATUS_TEXT[status];
  }

  /** `System prompt, Tool guides`; empty when the title names the only change. */
  changesText(group: CcEventGroup): string {
    return eventGroupChangesText(group);
  }
}
