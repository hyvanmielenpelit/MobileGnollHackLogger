import { ChangeDetectionStrategy, Component, Input, OnChanges } from '@angular/core';

import {
  DocumentStatusExplanation,
  DocumentStatusInput,
  DocumentStatusSource,
  IssueGroup,
  explainDocumentStatus,
  groupIssuesByRule
} from './report-validation-explanation';

/**
 * Why a Report Pack document has its status, for the body of an info dialog. `outcome` shows a summary
 * sentence, what was removed (each removed text in a disclosure), what was kept with a warning, how the
 * draft was checked and what can be done; `firstDraft` lists the first draft's issues by rule, for a
 * document being revised. Presentational: the text comes from `explainDocumentStatus`, which the
 * diagnostics export shares.
 */
@Component({
  selector: 'app-report-validation-details',
  standalone: true,
  templateUrl: './report-validation-details.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class ReportValidationDetailsComponent implements OnChanges {
  /** A job's progress row as it stands, or a stored document's status and validation issues. */
  @Input({ required: true }) document: DocumentStatusInput | null = null;

  /** `job` adds the diagnostics to what can be done; a stored document has none. */
  @Input() source: DocumentStatusSource = 'job';

  /** `outcome`: why the document has its status. `firstDraft`: the first draft's issues, by rule. */
  @Input() view: 'outcome' | 'firstDraft' = 'outcome';

  /** The server's sentence on the current phase, leading the first-draft view. */
  @Input() phaseDetail: string | null = null;

  explanation: DocumentStatusExplanation | null = null;
  draftGroups: IssueGroup[] = [];

  ngOnChanges(): void {
    const doc = this.document;
    this.explanation = doc ? explainDocumentStatus(doc, this.source) : null;
    this.draftGroups = groupIssuesByRule(doc?.firstTurnIssues ?? []);
  }
}
