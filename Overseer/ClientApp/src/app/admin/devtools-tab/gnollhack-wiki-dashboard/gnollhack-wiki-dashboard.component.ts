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
import { Subscription } from 'rxjs';

import {
  AdminDevtoolsService,
  CorpusIndexJob,
  CorpusKindSlug,
  WikiDuplicateTitle,
  WikiStatus,
  readCorpusError
} from '../../../services/admin-devtools.service';
import { ensureOverlayPolyfills } from '../../../utils/polyfills.util';
import { CorpusIndexPanelComponent } from '../shared/corpus-index-panel/corpus-index-panel.component';
import { CorpusSearchProbeComponent } from '../shared/corpus-search-probe/corpus-search-probe.component';
import { CorpusToolUsageComponent } from '../shared/corpus-tool-usage/corpus-tool-usage.component';
import { DashboardPoller } from '../shared/dashboard-poller';
import type { PollResult } from '../shared/dashboard-poller';
import * as fmt from '../shared/devtools-format';
import { ReindexConfirmDialogComponent } from '../shared/reindex-confirm-dialog/reindex-confirm-dialog.component';

/** One row of the Categories table. */
export interface WikiCategoryRow {
  category: string;
  articles: number;
  bytes: number;
  /** The category's articles as a percentage of all categorized articles, 0 to 100. */
  sharePercent: number;
  shareText: string;
}

/** The category the server reports for files at the root of the wiki folder; not a `wiki_search` category value. */
export const WIKI_ROOT_CATEGORY = '(root)';

const KIND = 'gnollhack-wiki' satisfies CorpusKindSlug;

const shareFormat = new Intl.NumberFormat(undefined, { style: 'percent', maximumFractionDigits: 1 });

/**
 * The GnollHack Wiki sub-tab of the Services tab: the wiki index's status, key figures, excluded files
 * and recent passes (`app-corpus-index-panel`), then the categories, the duplicate titles, the chat's
 * use of the wiki tools and a `wiki_search` probe. Polls `getCorpusStatus('gnollhack-wiki')` and owns
 * the Re-index flow, including the confirmation a running GnollBench run asks for.
 *
 * Ids start with `gw`: the panel's, the probe's, the tool usage card's and the confirm dialog's, plus
 * `gw-categories-title` and `gw-duplicates-title`.
 */
@Component({
  selector: 'app-gnollhack-wiki-dashboard',
  standalone: true,
  imports: [CorpusIndexPanelComponent, CorpusSearchProbeComponent, CorpusToolUsageComponent, ReindexConfirmDialogComponent],
  templateUrl: './gnollhack-wiki-dashboard.component.html',
  styleUrl: './gnollhack-wiki-dashboard.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class GnollHackWikiDashboardComponent implements OnInit, OnDestroy {
  /** How long a finished pass's progress card stays, with its outcome. */
  static readonly ENDED_JOB_LINGER_MS = 10000;

  /** The text of the Re-index action's info tip. */
  readonly reindexTip = 'Reads the wiki folder as it is now, uncommitted edits included, and swaps in the new '
    + 'index when it is complete. The chat keeps answering from the current index meanwhile. Automatic '
    + 'refresh only follows new commits.';

  readonly fmt = fmt;

  private readonly devtools = inject(AdminDevtoolsService);
  private readonly cdr = inject(ChangeDetectorRef);

  private readonly poller = new DashboardPoller<WikiStatus>(
    () => this.devtools.getCorpusStatus(KIND),
    status => !!status.currentJob,
    result => this.onPollResult(result)
  );
  private actionSub: Subscription | null = null;
  private lingerTimer: ReturnType<typeof setTimeout> | null = null;
  private trackedJobId: string | null = null;

  @ViewChild(ReindexConfirmDialogComponent) private confirmDialog?: ReindexConfirmDialogComponent;

  /** The last good status; null until the first answer. */
  status: WikiStatus | null = null;
  /** The last poll failed; the dashboard keeps showing the last good status. */
  loadError: string | null = null;
  /** A finished pass whose progress card stays for `ENDED_JOB_LINGER_MS`. */
  endedJob: CorpusIndexJob | null = null;

  /** A start request is in flight. */
  starting = false;
  /** The last refusal of a start request. */
  actionMessage: string | null = null;
  /** `warning` for a 409 (a pass is already running), `error` for any other refusal. */
  actionMessageKind: 'warning' | 'error' = 'error';

  /** The categories, most articles first. */
  categoryRows: WikiCategoryRow[] = [];
  /** The `wiki_search` category values, alphabetical, for the probe's filter. */
  categoryNames: string[] = [];

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

  private onPollResult(result: PollResult<WikiStatus>): void {
    if (result.ok) {
      this.loadError = null;
      this.applyStatus(result.value);
    } else {
      this.loadError = `${this.describeError(result.error, 'The GnollHack wiki status could not be loaded')} `
        + `Trying again in ${result.retryInMs / 1000} s.`;
    }
    this.cdr.markForCheck();
  }

  private applyStatus(status: WikiStatus): void {
    this.status = status;
    this.categoryRows = buildCategoryRows(status);
    const names = status.categories
      .map(c => c.category)
      .filter(name => name !== WIKI_ROOT_CATEGORY)
      .sort((a, b) => a.localeCompare(b, undefined, { sensitivity: 'base' }));
    if (!sameList(names, this.categoryNames)) {
      this.categoryNames = names;
    }

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
        }, GnollHackWikiDashboardComponent.ENDED_JOB_LINGER_MS);
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

  /**
   * Asks the server for a manual pass. A 409 `benchmarkRunActive` opens the confirmation dialog,
   * whose confirm button calls this again with `acknowledgeActiveBenchmark`; a 409 `alreadyRunning`
   * shows a warning and polls at once; any other refusal shows an error.
   */
  reindex(acknowledgeActiveBenchmark = false): void {
    if (this.starting) {
      return;
    }
    this.starting = true;
    this.actionMessage = null;
    this.actionSub?.unsubscribe();
    this.actionSub = this.devtools.startCorpusReindex(KIND, acknowledgeActiveBenchmark).subscribe({
      next: () => {
        this.starting = false;
        this.cdr.markForCheck();
        this.poller.pollNow();
      },
      error: (error: unknown) => {
        this.starting = false;
        const refusal = readCorpusError(error);
        if (refusal.status === 409 && refusal.code === 'benchmarkRunActive' && !acknowledgeActiveBenchmark) {
          this.cdr.markForCheck();
          this.confirmDialog?.open(refusal.message ?? 'A GnollBench run is executing and reads the GnollHack wiki.');
          return;
        }
        const conflict = refusal.status === 409;
        this.actionMessageKind = conflict ? 'warning' : 'error';
        this.actionMessage = this.describeError(error, 'The re-index could not be started');
        this.cdr.markForCheck();
        if (conflict) {
          this.poller.pollNow();
        }
      }
    });
  }

  private describeError(error: unknown, fallback: string): string {
    const refusal = readCorpusError(error);
    if (refusal.message) {
      return refusal.message;
    }
    if (refusal.status === 409) {
      return 'A pass is already running.';
    }
    if (refusal.status === 400) {
      return `${this.status?.configKey ?? 'WikiPath'} is not configured, or its folder does not exist.`;
    }
    if (refusal.status === 0) {
      return `${fallback}: the server could not be reached.`;
    }
    return refusal.status ? `${fallback} (HTTP ${refusal.status}).` : `${fallback}.`;
  }

  // ---------------------------------------------------------------- figures

  /** The Categories tile's value: '—' until an index is published. */
  get categoryCountText(): string {
    return this.status?.index ? fmt.formatNumber(this.status.categories.length) : '—';
  }

  /** The Duplicate titles tile's value: '—' until an index is published. */
  get duplicateCountText(): string {
    return this.status?.index ? fmt.formatNumber(this.status.duplicateTitleCount) : '—';
  }

  /** "and N more titles" when the server capped the list, or ''. */
  get moreDuplicatesText(): string {
    const s = this.status;
    if (!s) {
      return '';
    }
    const more = s.duplicateTitleCount - s.duplicateTitles.length;
    return more > 0 ? `and ${fmt.formatNumber(more)} more ${more === 1 ? 'title' : 'titles'}` : '';
  }

  /** "3 articles" for a duplicate title's summary. */
  articleCountText(group: WikiDuplicateTitle): string {
    const n = group.paths.length;
    return `${fmt.formatNumber(n)} ${n === 1 ? 'article' : 'articles'}`;
  }
}

/** The categories sorted by articles, most first, then by name, with their share of all categorized articles. */
export function buildCategoryRows(status: WikiStatus): WikiCategoryRow[] {
  const total = status.categories.reduce((sum, c) => sum + c.articles, 0);
  return [...status.categories]
    .sort((a, b) => b.articles - a.articles || a.category.localeCompare(b.category, undefined, { sensitivity: 'base' }))
    .map(c => {
      const ratio = total > 0 ? c.articles / total : 0;
      return {
        category: c.category,
        articles: c.articles,
        bytes: c.bytes,
        sharePercent: Math.round(ratio * 1000) / 10,
        shareText: shareFormat.format(ratio)
      };
    });
}

function sameList(a: readonly string[], b: readonly string[]): boolean {
  return a.length === b.length && a.every((value, i) => value === b[i]);
}
