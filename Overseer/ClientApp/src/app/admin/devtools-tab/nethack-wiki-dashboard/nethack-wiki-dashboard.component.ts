import {
  ChangeDetectionStrategy,
  ChangeDetectorRef,
  Component,
  HostListener,
  OnDestroy,
  OnInit,
  ViewChild,
  inject
} from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';
import { Subscription } from 'rxjs';

import {
  AdminDevtoolsService,
  ConversionSkipReason,
  CorpusIndexJob,
  CorpusRequestError,
  NetHackWikiConversionReport,
  NetHackWikiStatus,
  readCorpusError
} from '../../../services/admin-devtools.service';
import { ensureOverlayPolyfills } from '../../../utils/polyfills.util';
import { CorpusIndexPanelComponent } from '../shared/corpus-index-panel/corpus-index-panel.component';
import { CorpusSearchProbeComponent } from '../shared/corpus-search-probe/corpus-search-probe.component';
import { CorpusToolUsageComponent } from '../shared/corpus-tool-usage/corpus-tool-usage.component';
import { DashboardPoller } from '../shared/dashboard-poller';
import type { PollResult } from '../shared/dashboard-poller';
import * as fmt from '../shared/devtools-format';
import { HealthStatusComponent } from '../shared/health-status/health-status.component';
import { ReindexConfirmDialogComponent } from '../shared/reindex-confirm-dialog/reindex-confirm-dialog.component';

/** One row of the Namespaces card. */
export interface NamespaceRow {
  name: string;
  articles: number;
  /** The share of all indexed articles, 0 to 100, with one decimal. */
  percent: number;
  /** The share as the locale writes a percentage, such as "96.0%". */
  shareText: string;
}

/** Readable names of the converter's skip reasons; any other key is shown by `skipReasonLabel`'s general rule. */
const SKIP_REASON_LABELS: Readonly<Record<string, string>> = {
  namespace: 'Namespace',
  formerly_converted_namespaces: 'Formerly converted namespaces',
  non_english: 'Non-English',
  variant_only: 'Variant only',
  variant_only_by_rule: 'Variant only, by rule',
  non_game: 'Non-game',
  title_suffix: 'Title suffix'
};

const percentFormat = new Intl.NumberFormat(undefined, { style: 'percent', minimumFractionDigits: 1, maximumFractionDigits: 1 });

/**
 * A skip reason key of the conversion report in words: a known key by its name, any other with its
 * underscores as spaces and a capital first letter (`user_talk` → "User talk").
 */
export function skipReasonLabel(reason: string): string {
  const known = SKIP_REASON_LABELS[reason];
  if (known) {
    return known;
  }
  const words = reason.replace(/_+/g, ' ').trim();
  return words ? words.charAt(0).toUpperCase() + words.slice(1) : reason;
}

/**
 * The NetHack Wiki sub-tab of the Services tab: the index of the generated NetHack wiki tree, polled
 * from the server, with Re-index, the converter's last report, the namespace counts, how to
 * regenerate the tree, the chat's use of the wiki tools and a search probe. The corpus refreshes at
 * startup and on Re-index only.
 *
 * A Re-index that the server answers with 409 `benchmarkRunActive` opens the confirmation dialog,
 * whose *Re-index anyway* resends it acknowledged; 409 `alreadyRunning` shows a warning and polls at
 * once; 400 shows an error. A pass this dashboard saw running, or started, stays on the progress card
 * for `ENDED_JOB_LINGER_MS` after it ends. Ids start with `nw`.
 */
@Component({
  selector: 'app-nethack-wiki-dashboard',
  standalone: true,
  imports: [
    CorpusIndexPanelComponent,
    CorpusSearchProbeComponent,
    CorpusToolUsageComponent,
    HealthStatusComponent,
    NgTemplateOutlet,
    ReindexConfirmDialogComponent
  ],
  templateUrl: './nethack-wiki-dashboard.component.html',
  styleUrl: './nethack-wiki-dashboard.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class NetHackWikiDashboardComponent implements OnInit, OnDestroy {
  /** How long a finished pass stays on the progress card, with its outcome. */
  static readonly ENDED_JOB_LINGER_MS = 10000;
  /** The text of the Re-index action's info tip. */
  static readonly REINDEX_TIP = 'Indexes the wiki folder once, now. This corpus never refreshes on its own.';

  private readonly devtools = inject(AdminDevtoolsService);
  private readonly cdr = inject(ChangeDetectorRef);

  private readonly poller = new DashboardPoller<NetHackWikiStatus>(
    () => this.devtools.getCorpusStatus('nethack-wiki'),
    status => !!status.currentJob,
    result => this.onPollResult(result)
  );
  private actionSub: Subscription | null = null;
  private lingerTimer: ReturnType<typeof setTimeout> | null = null;
  private trackedJobId: string | null = null;

  @ViewChild('confirmDialog') confirmDialog?: ReindexConfirmDialogComponent;

  readonly fmt = fmt;
  readonly reindexTip = NetHackWikiDashboardComponent.REINDEX_TIP;

  /** The last good status; null until the first answer. */
  status: NetHackWikiStatus | null = null;
  /** The last poll failed; the dashboard keeps showing the last good status. */
  loadError: string | null = null;
  /** A finished pass whose progress card stays for `ENDED_JOB_LINGER_MS`. */
  endedJob: CorpusIndexJob | null = null;

  /** A start request is in flight. */
  starting = false;
  /** The last refusal of a start request. */
  actionMessage: string | null = null;
  /** `warning` for a 409 `alreadyRunning`, `error` for any other refusal. */
  actionMessageKind: 'warning' | 'error' = 'error';

  ngOnInit(): void {
    ensureOverlayPolyfills();
    this.poller.start();
  }

  ngOnDestroy(): void {
    this.poller.stop();
    this.actionSub?.unsubscribe();
    this.actionSub = null;
    this.clearLinger();
    this.confirmDialog?.close();
  }

  @HostListener('document:visibilitychange')
  onVisibilityChange(): void {
    this.poller.onVisibilityChange(document.hidden);
  }

  // ---------------------------------------------------------------- polling

  /** Polls now, cancelling a pending poll and any request in flight. */
  retry(): void {
    this.poller.retry();
  }

  private onPollResult(result: PollResult<NetHackWikiStatus>): void {
    if (result.ok) {
      this.loadError = null;
      this.applyStatus(result.value);
    } else {
      this.loadError = `${this.describeError(readCorpusError(result.error), 'The NetHack wiki status could not be loaded')} `
        + `Trying again in ${result.retryInMs / 1000} s.`;
    }
    this.cdr.markForCheck();
  }

  private applyStatus(status: NetHackWikiStatus): void {
    this.status = status;
    const job = status.currentJob;
    if (job) {
      this.trackedJobId = job.id;
      this.endedJob = null;
      this.clearLinger();
      return;
    }
    if (this.trackedJobId !== null) {
      const finished = status.recentJobs.find(j => j.id === this.trackedJobId) ?? null;
      this.trackedJobId = null;
      if (finished) {
        this.endedJob = finished;
        this.clearLinger();
        this.lingerTimer = setTimeout(() => {
          this.lingerTimer = null;
          this.endedJob = null;
          this.cdr.markForCheck();
        }, NetHackWikiDashboardComponent.ENDED_JOB_LINGER_MS);
      }
    }
  }

  private clearLinger(): void {
    if (this.lingerTimer !== null) {
      clearTimeout(this.lingerTimer);
      this.lingerTimer = null;
    }
  }

  // ---------------------------------------------------------------- re-index

  /** Asks the server for a pass; `acknowledgeActiveBenchmark` after the GnollBench warning was confirmed. */
  reindex(acknowledgeActiveBenchmark = false): void {
    if (this.starting) {
      return;
    }
    this.starting = true;
    this.actionMessage = null;
    this.actionSub?.unsubscribe();
    this.actionSub = this.devtools.startCorpusReindex('nethack-wiki', acknowledgeActiveBenchmark).subscribe({
      next: started => {
        this.starting = false;
        if (started?.jobId) {
          this.trackedJobId = started.jobId;
        }
        this.cdr.markForCheck();
        this.poller.pollNow();
      },
      error: (error: unknown) => {
        this.starting = false;
        const refusal = readCorpusError(error);
        if (refusal.status === 409 && refusal.code === 'benchmarkRunActive') {
          this.cdr.markForCheck();
          this.confirmDialog?.open(refusal.message ?? 'A GnollBench run is executing and reads the NetHack wiki.');
          return;
        }
        const conflict = refusal.status === 409;
        this.actionMessageKind = conflict ? 'warning' : 'error';
        this.actionMessage = conflict
          ? refusal.message ?? 'A pass over the NetHack wiki is already running.'
          : this.describeError(refusal, 'The NetHack wiki could not be re-indexed');
        this.cdr.markForCheck();
        if (conflict) {
          this.poller.pollNow();
        }
      }
    });
  }

  /** *Re-index anyway* in the GnollBench warning. */
  onReindexConfirmed(): void {
    this.reindex(true);
  }

  private describeError(refusal: CorpusRequestError, fallback: string): string {
    if (refusal.message) {
      return refusal.message;
    }
    if (refusal.status === 400) {
      return 'NetHackWikiPath is not configured, or its folder does not exist.';
    }
    if (refusal.status === 0) {
      return `${fallback}: the server could not be reached.`;
    }
    return refusal.status ? `${fallback} (HTTP ${refusal.status}).` : `${fallback}.`;
  }

  // ---------------------------------------------------------------- cards

  get report(): NetHackWikiConversionReport | null {
    return this.status?.conversionReport ?? null;
  }

  /** The Namespaces tile: how many namespaces the published index holds, or '—' without an index. */
  get namespaceCountText(): string {
    return this.status?.index ? fmt.formatNumber(this.status.namespaces.length) : '—';
  }

  /** The namespaces with their share of the indexed articles, most articles first. */
  get namespaceRows(): NamespaceRow[] {
    const namespaces = this.status?.namespaces ?? [];
    const total = namespaces.reduce((sum, ns) => sum + ns.articles, 0);
    return [...namespaces]
      .sort((a, b) => b.articles - a.articles)
      .map(ns => {
        const share = total > 0 ? ns.articles / total : 0;
        return {
          name: ns.namespace || 'article',
          articles: ns.articles,
          percent: Math.round(share * 1000) / 10,
          shareText: percentFormat.format(share)
        };
      });
  }

  /** The `--gh-dash-share` value of a share bar. */
  shareStyle(row: NamespaceRow): string {
    return `${row.percent.toFixed(1)}%`;
  }

  skipReasonLabel(reason: string): string {
    return skipReasonLabel(reason);
  }

  hasBreakdown(reason: ConversionSkipReason): boolean {
    return (reason.breakdown?.length ?? 0) > 0;
  }

  /** The dump's size, or '' when the report lacks it. */
  dumpSizeText(report: NetHackWikiConversionReport): string {
    return report.dumpBytes === null ? '' : fmt.formatBytes(report.dumpBytes);
  }

  /** The converter's run time, or '—' when the report lacks it. */
  durationText(report: NetHackWikiConversionReport): string {
    return report.durationSeconds === null ? '—' : fmt.formatDuration(report.durationSeconds * 1000);
  }
}
