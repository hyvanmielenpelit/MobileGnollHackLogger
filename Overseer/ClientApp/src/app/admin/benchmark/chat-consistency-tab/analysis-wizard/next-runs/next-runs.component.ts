import {
  ChangeDetectionStrategy,
  ChangeDetectorRef,
  Component,
  EventEmitter,
  Input,
  OnChanges,
  Output,
  SimpleChanges,
  inject
} from '@angular/core';

import { BenchmarkShellBridge } from '../../../state/benchmark-shell-bridge.service';
import { NO_VALUE, plural } from '../../chat-consistency-format';
import {
  CcModelBatchSetup,
  CcNextRunGroup,
  ccModelBaseName,
  ccNextRunActionCount,
  ccNextRunGroups
} from '../../chat-consistency-results';
import { CcAnalysisResult, CcRunRow } from '../../chat-consistency.models';

/** One *Set up from run #N* button of a card. */
export interface CcNextRunTargetView {
  runId: number;
  /** `Set up from run #96`. */
  text: string;
  /** The button's accessible name: the visible text, the suite, and that nothing starts. */
  label: string;
}

/** One card: the next runs of one kind and period. */
export interface CcNextRunCardView {
  key: string;
  kind: string;
  period: string;
  /** `Baseline`, `Comparison`, `Both periods`. */
  periodText: string;
  titleId: string;
  /** The action phrase: *Run the model again on another day*. */
  title: string;
  endpointIds: string[];
  reasons: string[];
  suggestions: string[];
  targets: CcNextRunTargetView[];
  /** The control card's *Suite* and *Same build as* facts; empty on the other kinds. */
  suitesText: string;
  buildText: string;
  /** A new baseline's *Set up as model batch*: the batch it fills Run Benchmark with; null without one. */
  modelBatch: CcModelBatchSetup | null;
}

/** The cards of one kind under its heading. */
export interface CcNextRunSection {
  kind: string;
  headingId: string;
  heading: string;
  /** A one-line lead under the heading; empty for none. */
  lead: string;
  cards: CcNextRunCardView[];
}

/** Each card's action phrase by kind. */
const CARD_TITLES: Readonly<Record<string, string>> = {
  checkpoint: 'Run the model again on another day',
  stratum: 'Run at another time of day',
  control: 'Run another provider\'s model',
  regrade: 'Re-grade with one common grader'
};

const PERIOD_TEXT: Readonly<Record<string, string>> = {
  baseline: 'Baseline',
  comparison: 'Comparison',
  both: 'Both periods'
};

const CONTROL_LEAD = 'Another provider\'s model under the same Overseer build tells our changes from the provider\'s.';

function periodText(period: string): string {
  return PERIOD_TEXT[period] ?? (period ? period.charAt(0).toUpperCase() + period.slice(1) : NO_VALUE);
}

/**
 * The Results step's *Next runs*: the server's next runs grouped by kind, one section per kind present
 * (*More runs of {model}*, *Runs at another time of day*, *Control runs*, *Re-grades*), each a grid of
 * cards, one per period, with a *Set up from run #N* button per run whose setup the card repeats. A
 * re-grade has no button; it is started from step 3.
 */
@Component({
  selector: 'app-cc-next-runs',
  standalone: true,
  templateUrl: './next-runs.component.html',
  styleUrls: ['./next-runs.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class CcNextRunsComponent implements OnChanges {
  @Input({ required: true }) result!: CcAnalysisResult;
  /** The runs that name each target's suite. */
  @Input() rows: readonly CcRunRow[] = [];

  /** A run id whose setup fills Run Benchmark; nothing starts. */
  @Output() readonly repeatSetup = new EventEmitter<number>();

  /** Fills Run Benchmark's model batch launcher; absent outside the GnollBench page. */
  private readonly bridge = inject(BenchmarkShellBridge, { optional: true });
  private readonly cdr = inject(ChangeDetectorRef);

  /** Why *Set up as model batch* could not leave this tab now, by card key; shown on that card. */
  modelBatchRefusals = new Map<string, string>();

  sections: CcNextRunSection[] = [];
  /** How many runs and re-grades the cards ask for. */
  actionCount = 0;

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['result'] || changes['rows']) {
      const groups = ccNextRunGroups(this.result, this.rows);
      this.actionCount = ccNextRunActionCount(groups);
      this.sections = nextRunSections(this.result, groups);
    }
  }

  /** `6 runs would resolve the open questions.` */
  get leadText(): string {
    return ccNextRunsLeadText(this.actionCount);
  }

  get provider(): string {
    return this.result.subject.provider;
  }

  /**
   * *Set up as model batch*: Run Benchmark opens as a model batch on the card's target with the
   * subject's model chosen, and asks for a control model of another provider; nothing starts. Leaving
   * this tab closes the wizard. Refused, with the reason on the card, while the tab cannot be left.
   */
  setUpModelBatch(card: CcNextRunCardView): void {
    const setup = card.modelBatch;
    if (!setup || !this.bridge) return;
    const refusal = this.bridge.leaveRefusal();
    if (refusal) {
      this.modelBatchRefusals = new Map(this.modelBatchRefusals).set(card.key, refusal);
      this.cdr.markForCheck();
      return;
    }
    this.modelBatchRefusals = new Map();
    this.bridge.prefillModelBatch({
      targetKind: setup.targetKind,
      suiteId: setup.suiteId,
      batteryId: setup.batteryId,
      modelConfigurationId: setup.modelConfigurationId,
      controlSuggested: true
    });
  }
}

/** `6 runs would resolve the open questions.` */
export function ccNextRunsLeadText(actionCount: number): string {
  return `${plural(actionCount, 'run')} would resolve the open questions.`;
}

/**
 * The server's next runs as the *Next runs* tab shows them: one section per kind present, each holding
 * a card per period. The tab and its image read the same sections.
 */
export function ccNextRunSections(result: CcAnalysisResult, rows: readonly CcRunRow[]): CcNextRunSection[] {
  return nextRunSections(result, ccNextRunGroups(result, rows));
}

function nextRunSections(result: CcAnalysisResult, groups: readonly CcNextRunGroup[]): CcNextRunSection[] {
  // Ids by position: the kind and period are server strings.
  const sections: CcNextRunSection[] = [];
  for (const group of groups) {
    let index = sections.findIndex(entry => entry.kind === group.kind);
    if (index < 0) {
      index = sections.length;
      sections.push({
        kind: group.kind,
        headingId: `cc-nr-${index}-title`,
        heading: sectionHeading(result, group),
        lead: group.kind === 'control' ? CONTROL_LEAD : '',
        cards: []
      });
    }
    const section = sections[index];
    section.cards.push(cardView(group, `cc-nr-${index}-${section.cards.length}-title`));
  }
  return sections;
}

function sectionHeading(result: CcAnalysisResult, group: CcNextRunGroup): string {
  switch (group.kind) {
    case 'checkpoint':
      return `More runs of ${ccModelBaseName(result.subject.displayName, result.subject.thinkingLevel)}`;
    case 'stratum':
      return 'Runs at another time of day';
    case 'control':
      return 'Control runs';
    case 'regrade':
      return 'Re-grades';
    default:
      return group.title;
  }
}

function cardView(group: CcNextRunGroup, titleId: string): CcNextRunCardView {
  const control = group.kind === 'control';
  const suites = group.targets.map(target => target.suiteName).filter((name): name is string => !!name);
  return {
    key: group.key,
    kind: group.kind,
    period: group.period,
    periodText: periodText(group.period),
    titleId,
    title: CARD_TITLES[group.kind] ?? group.title,
    endpointIds: group.endpointIds,
    reasons: group.reasons,
    suggestions: group.suggestions,
    targets: group.kind === 'regrade' ? [] : group.targets.map(target => ({
      runId: target.runId,
      text: `Set up from run #${target.runId}`,
      label: `Set up from run #${target.runId}'s setup${target.suiteName ? ` (${target.suiteName})` : ''}`
        + ' — fills Run Benchmark, starts nothing'
    })),
    suitesText: control ? (suites.length > 0 ? [...new Set(suites)].join(', ') : NO_VALUE) : '',
    buildText: control
      ? (group.targets.length > 0 ? group.targets.map(target => `run #${target.runId}`).join(', ') : NO_VALUE)
      : '',
    modelBatch: group.modelBatch ?? null
  };
}
