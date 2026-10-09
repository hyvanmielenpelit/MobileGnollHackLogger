import { ChangeDetectionStrategy, Component, EventEmitter, Input, OnChanges, Output, SimpleChanges } from '@angular/core';

import { InfoTipComponent } from '../../../../../shared/info-tip/info-tip.component';
import { ccNumber, formatFractionPercent, plural } from '../../chat-consistency-format';
import { CC_PROTOCOL_V1_ENDPOINTS } from '../../chat-consistency-readiness';
import {
  CcEndpointStatus,
  CcOverallOutcome,
  ccEndpointStatus,
  ccEndpointStatusText,
  ccModelBaseName,
  ccOverallOutcome
} from '../../chat-consistency-results';
import { CcAnalysisResult } from '../../chat-consistency.models';
import { CcModelBadgesComponent } from '../../model-badges/model-badges.component';

/** One endpoint chip of the banner. */
export interface CcVerdictChip {
  id: string;
  name: string;
  status: CcEndpointStatus;
  /** `Within margin`, `More work`. */
  text: string;
  /** `P1 Quality: Not computable. Show its verdict.` */
  label: string;
}

/** One term of an info dialog's list. */
export interface CcVerdictTipFact {
  key: string;
  term: string;
  value: string;
  note?: string;
}

/** The verdicts and grades the Protocol dialog explains, in the wording of the protocol. */
export const CC_VERDICT_DEFINITIONS: readonly { readonly term: string; readonly text: string }[] = [
  {
    term: 'Degraded or improved',
    text: 'The change is significant after Holm\'s correction and the whole 95 % interval lies beyond the margin. '
      + 'Work per turn reads more work or less work instead, never better or worse.'
  },
  { term: 'Changed, negligible', text: 'The 95 % interval excludes zero but lies inside the margin: a change too small to matter.' },
  { term: 'Equivalent', text: 'The 90 % interval lies inside the margin.' },
  {
    term: 'Inconclusive',
    text: 'Neither: the data cannot tell. Never read it as no change; the smallest change the sample can detect says how large a change could go unseen.'
  },
  { term: 'Not computable', text: 'The data cannot compute the endpoint; its reason says what is missing.' }
];

export const CC_GRADE_DEFINITIONS: readonly { readonly term: string; readonly text: string }[] = [
  {
    term: 'Established',
    text: 'A decisive verdict with every robustness check passed, the minimum sample, telemetry data and no relaxed pooling.'
  },
  { term: 'Indicated', text: 'A decisive verdict missing any of those.' },
  { term: 'Not established', text: 'An inconclusive or not computable endpoint.' }
];

/**
 * The head of the Results step's Summary tab: the analysis's outcome over the server's verdicts, the model with its
 * badges, one chip per endpoint that asks the host to show its verdict, the scope and the protocol with
 * their explanations in dialogs, and the reliability lines of the headline.
 */
@Component({
  selector: 'app-cc-verdict-banner',
  standalone: true,
  imports: [CcModelBadgesComponent, InfoTipComponent],
  templateUrl: './verdict-banner.component.html',
  styleUrls: ['./verdict-banner.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class CcVerdictBannerComponent implements OnChanges {
  @Input({ required: true }) result!: CcAnalysisResult;

  /** The id of the endpoint whose chip was pressed. */
  @Output() readonly endpointSelected = new EventEmitter<string>();

  readonly verdictDefinitions = CC_VERDICT_DEFINITIONS;
  readonly gradeDefinitions = CC_GRADE_DEFINITIONS;

  outcome: CcOverallOutcome = { kind: 'noneComputed', title: '', detail: '' };
  modelName = '';
  chips: CcVerdictChip[] = [];
  scopeValue = '';
  scopeFacts: CcVerdictTipFact[] = [];
  /** No time block is common to both periods. */
  noCommonBlock = false;
  protocolFacts: CcVerdictTipFact[] = [];

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['result'] && this.result) {
      const result = this.result;
      this.outcome = ccOverallOutcome(result);
      this.modelName = ccModelBaseName(result.subject.displayName, result.subject.thinkingLevel);
      this.chips = result.endpoints.map(endpoint => {
        const text = ccEndpointStatusText(endpoint);
        return {
          id: endpoint.id,
          name: endpoint.name,
          status: ccEndpointStatus(endpoint),
          text,
          label: `${endpoint.id} ${endpoint.name}: ${text}. Show its verdict.`
        };
      });
      this.noCommonBlock = result.scope.strataUsed.length === 0;
      this.scopeValue = this.noCommonBlock
        ? 'No common time stratum'
        : result.scope.text || result.scope.strataUsed.join(', ');
      this.scopeFacts = this.buildScopeFacts(result);
      this.protocolFacts = this.buildProtocolFacts(result);
    }
  }

  get protocolTitle(): string {
    return `About Protocol ${this.result.protocolLabel}`;
  }

  get compareKindText(): string {
    return this.result.comparisonSet?.kind === 'battery' ? 'Battery' : 'Suite';
  }

  select(chip: CcVerdictChip): void {
    this.endpointSelected.emit(chip.id);
  }

  private buildScopeFacts(result: CcAnalysisResult): CcVerdictTipFact[] {
    const scope = result.scope;
    const sampled = (covered: boolean) => (covered ? 'Sampled' : 'Not sampled');
    const facts: CcVerdictTipFact[] = [
      { key: 'blocks', term: 'Blocks both periods sampled', value: scope.strataUsed.length > 0 ? scope.strataUsed.join(', ') : 'None' }
    ];
    if (ccNumber(scope.excludedShare) !== null) {
      facts.push({ key: 'excluded', term: 'Answers outside them', value: formatFractionPercent(scope.excludedShare) });
    }
    const definition = result.protocol.usBusinessHoursDefinition?.trim();
    facts.push({
      key: 'business',
      term: 'US business hours',
      value: sampled(scope.usBusinessHoursCovered),
      ...(definition ? { note: definition } : {})
    });
    facts.push({ key: 'outside', term: 'Outside them', value: sampled(scope.outsideBusinessHoursCovered) });
    return facts;
  }

  private buildProtocolFacts(result: CcAnalysisResult): CcVerdictTipFact[] {
    const protocol = result.protocol;
    const endpoints = result.endpoints;
    const facts: CcVerdictTipFact[] = endpoints.map(endpoint => ({
      key: `endpoint-${endpoint.id}`,
      term: `${endpoint.id} ${endpoint.name}`,
      value: `Margin ${endpoint.marginText}`
    }));

    const alpha = ccNumber(protocol.alpha);
    const span = endpoints.length > 1 ? `${endpoints[0].id}–${endpoints[endpoints.length - 1].id}` : endpoints[0]?.id ?? '';
    facts.push({
      key: 'significance',
      term: 'Significance',
      value: `α ${alpha === null ? '—' : String(alpha)}, with Holm's correction${span ? ` across ${span}` : ''}.`
    });

    const unit = result.unitKind === 'batteryRun' ? 'battery run' : 'run';
    const stratified = new Set(CC_PROTOCOL_V1_ENDPOINTS.filter(entry => entry.stratified).map(entry => entry.id));
    const pooledIds = endpoints.filter(endpoint => !stratified.has(endpoint.id)).map(endpoint => endpoint.id);
    const speedIds = endpoints.filter(endpoint => stratified.has(endpoint.id)).map(endpoint => endpoint.id);
    const lines: string[] = [];
    if (pooledIds.length > 0) {
      lines.push(`${pooledIds.join(', ')}: at least ${plural(protocol.minimumRunsPerPeriod, unit)} per period on at least `
        + `${plural(protocol.minimumDaysPerPeriod, 'UTC day')}, and ${plural(protocol.minimumPairedItems, 'paired item')}.`);
    }
    if (speedIds.length > 0) {
      lines.push(`${speedIds.join(', ')}: at least ${plural(protocol.minimumSpeedRunsPerStratum, unit)} per period in one common time block.`);
    }
    facts.push({ key: 'sample', term: 'Minimum sample', value: lines.join(' '), note: 'With fewer, an endpoint is at most Indicated.' });
    return facts;
  }
}
