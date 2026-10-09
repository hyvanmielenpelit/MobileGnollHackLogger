import { ChangeDetectionStrategy, Component, Input } from '@angular/core';

import { CcEventGroup, eventGroupChangesText } from '../../chat-consistency-events';
import { CC_READINESS_STATUS_TEXT, CcEndpointReadiness, CcPreviewNote, CcReadinessStatus } from '../../chat-consistency-readiness';
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

/** What the Compared fact's tag reads: a battery, a suite, or every suite run by run. */
export interface CcPreviewKind {
  kind: 'battery' | 'suite' | 'all';
  text: string;
}

/**
 * The Analyze step's *Preview*: what the analysis will be sent, which primary endpoints can reach a
 * verdict on the chosen periods, and the notes that may change or qualify the result. Read-only; every
 * figure is built by the host from the readiness module, and the server applies the protocol itself.
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
