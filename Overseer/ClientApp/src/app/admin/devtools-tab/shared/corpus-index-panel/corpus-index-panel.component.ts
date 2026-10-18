import {
  ChangeDetectionStrategy,
  ChangeDetectorRef,
  Component,
  EventEmitter,
  Input,
  OnInit,
  Output,
  inject
} from '@angular/core';

import type {
  AnyCorpusStatus,
  CorpusIndexJob,
  CorpusIndexOutcome,
  CorpusIndexStatus,
  CorpusIndexTrigger,
  CorpusKind
} from '../../../../services/admin-devtools.service';
import { InfoTipComponent } from '../../../../shared/info-tip/info-tip.component';
import { copyToClipboard } from '../../../../utils/clipboard.util';
import { ensureOverlayPolyfills } from '../../../../utils/polyfills.util';
import { CORPUS_STAGES, CorpusBanner, corpusBanner, sortCorpusJobs } from '../corpus-banner';
import * as fmt from '../devtools-format';
import { HealthStatus, HealthStatusComponent } from '../health-status/health-status.component';

export type CorpusStageState = 'pending' | 'current' | 'done' | 'ended';

export interface CorpusStageItem {
  key: string;
  name: string;
  state: CorpusStageState;
  note: string | null;
}

/** The configuration key of each corpus's per-file size limit; the knowledge base has none. */
const SIZE_LIMIT_KEYS: Readonly<Record<CorpusKind, string | null>> = {
  GnollHackWiki: 'MaxWikiFileSizeKB',
  NetHackWiki: 'MaxNetHackWikiFileSizeKB',
  KnowledgeBase: null
};

/**
 * The common part of a corpus dashboard (GnollHack wiki, NetHack wiki, knowledge base), in order:
 * the failed-poll line with Retry, the status banner, the Re-index action with its reason and
 * feedback, the progress card of a running (or just-ended) pass, the key figures, the excluded and
 * unreadable files, and the recent passes. It only renders and emits; the host dashboard polls,
 * starts the pass and keeps an ended job for its linger.
 *
 * ```html
 * <app-corpus-index-panel [status]="status" corpusLabel="GnollHack wiki" idPrefix="gw"
 *     reindexLabel="Re-index wiki" [reindexTip]="tip" [starting]="starting"
 *     [actionMessage]="actionMessage" [actionMessageKind]="actionMessageKind"
 *     [endedJob]="endedJob" [loadError]="loadError" (reindex)="reindex()" (retry)="retry()">
 *   <div figures class="gh-health-tile"><dt>Categories</dt><dd>12</dd></div>
 * </app-corpus-index-panel>
 * ```
 *
 * Elements with the `figures` attribute are projected at the end of the key-figures `dl`, so they
 * should be `div.gh-health-tile` groups of one `dt` and one `dd`. Every id starts with `idPrefix`:
 * `{p}-reindex-btn`, `{p}-actions-reason`, `{p}-action-message`, `{p}-reindex-tip`,
 * `{p}-load-error`, `{p}-progress-title`, `{p}-figures-title`, `{p}-excluded-title`,
 * `{p}-recent-title`, `{p}-copy-fingerprint-btn`.
 */
@Component({
  selector: 'app-corpus-index-panel',
  standalone: true,
  imports: [HealthStatusComponent, InfoTipComponent],
  templateUrl: './corpus-index-panel.component.html',
  styleUrl: './corpus-index-panel.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class CorpusIndexPanelComponent implements OnInit {
  private readonly cdr = inject(ChangeDetectorRef);

  /** The last good status; null until the first answer, when a loading line shows instead. */
  @Input() status: AnyCorpusStatus | CorpusIndexStatus | null = null;
  /** The corpus in running text: "GnollHack wiki", "NetHack wiki", "knowledge base". */
  @Input({ required: true }) corpusLabel = '';
  /** `gw`, `nw` or `kb`: the prefix of every id, so they stay unique in the document. */
  @Input({ required: true }) idPrefix = '';
  /** The action button's label, such as *Re-index wiki* or *Reload articles*. */
  @Input({ required: true }) reindexLabel = '';
  /** The text of the action's click-mode info tip. */
  @Input() reindexTip = '';
  /** A start request is in flight. */
  @Input() starting = false;
  /** The last refusal of a start request; null when there is none. */
  @Input() actionMessage: string | null = null;
  /** `warning` for a 409, `error` for any other refusal. */
  @Input() actionMessageKind: 'warning' | 'error' = 'error';
  /** A just-finished pass the host keeps for a while, shown in the progress card when none runs. */
  @Input() endedJob: CorpusIndexJob | null = null;
  /** The last poll failed: shown with a Retry, above the last good status. */
  @Input() loadError: string | null = null;

  /** The action button was clicked while the action is available. */
  @Output() reindex = new EventEmitter<void>();
  /** Retry was clicked after a failed poll. */
  @Output() retry = new EventEmitter<void>();

  /** The result of the last Copy content fingerprint, for the polite status line. */
  copyStatus = '';

  readonly fmt = fmt;

  ngOnInit(): void {
    ensureOverlayPolyfills();
  }

  get banner(): CorpusBanner | null {
    return this.status ? corpusBanner(this.status, this.corpusLabel) : null;
  }

  /** The corpus label with a capital first letter, for headings. */
  get corpusTitle(): string {
    return this.corpusLabel ? this.corpusLabel.charAt(0).toUpperCase() + this.corpusLabel.slice(1) : '';
  }

  get gitPoll(): boolean {
    return this.status?.refreshMode === 'GitPoll';
  }

  /** Why the action is unavailable, or null when it is available. */
  get blockedReason(): string | null {
    const s = this.status;
    if (!s) {
      return 'The status has not loaded yet.';
    }
    if (!s.configured && !s.path) {
      return `${s.configKey} is not configured.`;
    }
    if (!s.pathExists) {
      return `The ${s.configKey} folder does not exist.`;
    }
    if (s.currentJob || this.starting) {
      return 'A pass is running.';
    }
    return null;
  }

  /** The action button's `aria-describedby`: its reason, the last refusal and the tip. */
  get actionDescribedBy(): string {
    const ids: string[] = [];
    if (this.blockedReason) {
      ids.push(`${this.idPrefix}-actions-reason`);
    }
    if (this.actionMessage) {
      ids.push(`${this.idPrefix}-action-message`);
    }
    if (this.reindexTip) {
      ids.push(`${this.idPrefix}-reindex-tip`);
    }
    return ids.join(' ');
  }

  onReindex(): void {
    if (this.blockedReason) {
      return;
    }
    this.reindex.emit();
  }

  // ---------------------------------------------------------------- progress

  /** The pass the progress card shows: the running one, else the host's just-ended one. */
  get progressJob(): CorpusIndexJob | null {
    return this.status?.currentJob ?? this.endedJob;
  }

  stageItems(job: CorpusIndexJob): CorpusStageItem[] {
    const index = CORPUS_STAGES.findIndex(s => s.key === job.phase);
    const running = job.outcome === 'Running';
    const succeeded = job.outcome === 'Succeeded';
    return CORPUS_STAGES.map((stage, i): CorpusStageItem => {
      let state: CorpusStageState;
      if (succeeded) {
        state = 'done';
      } else if (index < 0) {
        state = running ? 'pending' : i === 0 ? 'ended' : 'pending';
      } else if (i < index) {
        state = 'done';
      } else if (i === index) {
        state = running ? 'current' : 'ended';
      } else {
        state = 'pending';
      }
      const note = state === 'ended' ? (job.outcome === 'Canceled' ? 'Canceled here' : 'Failed here') : null;
      return { key: stage.key, name: stage.name, state, note };
    });
  }

  stateWord(state: CorpusStageState): string {
    switch (state) {
      case 'current': return '(current)';
      case 'done': return '(done)';
      case 'ended': return '(ended)';
      default: return '';
    }
  }

  isDeterminate(job: CorpusIndexJob): boolean {
    return job.outcome === 'Running' && job.phase === 'ReadingFiles' && (job.filesTotal ?? 0) > 0;
  }

  fileProgressText(job: CorpusIndexJob): string {
    return `${fmt.formatNumber(job.filesRead)} of ${fmt.formatNumber(job.filesTotal ?? 0)} files`;
  }

  jobTitle(job: CorpusIndexJob): string {
    return job.outcome === 'Running' ? `${this.corpusTitle} pass in progress` : `${this.corpusTitle} pass finished`;
  }

  triggerLabel(trigger: CorpusIndexTrigger): string {
    switch (trigger) {
      case 'Manual': return 'Started manually';
      case 'RepositoryChange': return 'Repository change';
      case 'Startup': return 'Server start';
      case 'FollowUp': return 'Follow-up after a repository change';
      default: return trigger;
    }
  }

  outcomeStatus(outcome: CorpusIndexOutcome): HealthStatus {
    switch (outcome) {
      case 'Succeeded': return 'ok';
      case 'Failed': return 'error';
      case 'Canceled': return 'warning';
      default: return 'running';
    }
  }

  outcomeWord(outcome: CorpusIndexOutcome): string {
    return outcome;
  }

  elapsedMs(job: CorpusIndexJob): number {
    return fmt.elapsedMs(job.startedAtUtc, job.finishedAtUtc);
  }

  /** The finished passes, newest first. */
  get recentJobs(): CorpusIndexJob[] {
    return this.status ? sortCorpusJobs(this.status.recentJobs) : [];
  }

  // ---------------------------------------------------------------- excluded files

  /** The configuration key of the size limit; null for a corpus without one. */
  get sizeLimitKey(): string | null {
    return this.status ? SIZE_LIMIT_KEYS[this.status.kind] ?? null : null;
  }

  /** "and N more" for a list the server capped, or '' when it is complete. */
  moreText(listed: number, total: number): string {
    return total > listed ? `and ${fmt.formatNumber(total - listed)} more` : '';
  }

  async copyFingerprint(fingerprint: string): Promise<void> {
    const copied = await copyToClipboard(fingerprint);
    this.copyStatus = copied ? 'Content fingerprint copied.' : 'Copy failed. Select the text and copy it instead.';
    this.cdr.markForCheck();
  }
}
