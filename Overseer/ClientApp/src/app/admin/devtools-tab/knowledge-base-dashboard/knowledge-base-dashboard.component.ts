import {
  ChangeDetectionStrategy,
  ChangeDetectorRef,
  Component,
  ElementRef,
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
  FailedCorpusFile,
  KnowledgeArticleInfo,
  KnowledgeArticleSource,
  KnowledgeBaseStatus,
  KnowledgeShadowedTopic,
  readCorpusError
} from '../../../services/admin-devtools.service';
import { SortHeaderComponent } from '../../../shared/data-table/sort-header.component';
import { TablePagerComponent } from '../../../shared/data-table/table-pager.component';
import { TableState, exactFilter } from '../../../shared/data-table/table-state';
import { copyToClipboard } from '../../../utils/clipboard.util';
import { ensureOverlayPolyfills, refreshAnchorPositioning } from '../../../utils/polyfills.util';
import { CorpusIndexPanelComponent } from '../shared/corpus-index-panel/corpus-index-panel.component';
import { CorpusSearchProbeComponent } from '../shared/corpus-search-probe/corpus-search-probe.component';
import { CorpusToolUsageComponent } from '../shared/corpus-tool-usage/corpus-tool-usage.component';
import { DashboardPoller } from '../shared/dashboard-poller';
import type { PollResult } from '../shared/dashboard-poller';
import * as fmt from '../shared/devtools-format';
import { HealthStatusComponent } from '../shared/health-status/health-status.component';
import { ReindexConfirmDialogComponent } from '../shared/reindex-confirm-dialog/reindex-confirm-dialog.component';

/** A filter value of the Title and Summary selects: the front-matter field is set or not. */
export type PresenceFilter = '' | 'present' | 'missing';

/** The text columns of the Articles table's filter row. */
export type ArticleTextFilter = 'topic' | 'title';

/** The select columns of the Articles table's filter row. */
export type ArticlePresenceColumn = 'hasTitle' | 'hasSummary';

/** The characters per token the topic-list estimate assumes. */
export const CHARS_PER_TOKEN = 4;

const presence = (value: boolean): Exclude<PresenceFilter, ''> => value ? 'present' : 'missing';

/**
 * The Knowledge Base sub-tab of the Services tab: the knowledge base's status, polled from the
 * server, with Reload articles; the loaded articles as a sortable, filterable, paged table with a
 * read-only preview of each article's source; the health checks; the chat's use of
 * `get_knowledge_article`; and a probe that runs that tool.
 *
 * A Reload refused with 409 `benchmarkRunActive` opens the GnollBench warning, whose *Reload anyway*
 * resends it acknowledged. Ids start with `kb`: the panel's, the probe's, the tool usage card's and
 * the warning's own (see each component), and `kb-articles-title`, `kb-articles-wrap`,
 * `kb-filter-topic`, `kb-filter-title`, `kb-filter-has-title`, `kb-filter-has-summary`,
 * `kb-articles-clear`, `kb-articles-empty`, `kb-articles-no-matches`,
 * `kb-articles-no-matches-clear`, `kb-article-{n}-preview-btn` and `-tip` (n is the row on the page),
 * `kb-health-title`, `kb-health-list`, `kb-preview-dialog`, `kb-preview-title`,
 * `kb-preview-subtitle`, `kb-preview-close`, `kb-preview-close-tip`, `kb-preview-status`,
 * `kb-preview-error`, `kb-preview-copy-btn`, `kb-preview-copy-tip` and `kb-preview-source`.
 */
@Component({
  selector: 'app-knowledge-base-dashboard',
  standalone: true,
  imports: [
    CorpusIndexPanelComponent,
    CorpusSearchProbeComponent,
    CorpusToolUsageComponent,
    HealthStatusComponent,
    ReindexConfirmDialogComponent,
    SortHeaderComponent,
    TablePagerComponent
  ],
  templateUrl: './knowledge-base-dashboard.component.html',
  styleUrl: './knowledge-base-dashboard.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class KnowledgeBaseDashboardComponent implements OnInit, OnDestroy {
  /** How long a finished pass's progress card stays, with its outcome. */
  static readonly ENDED_JOB_LINGER_MS = 10000;

  /** The text of the Reload action's info tip. */
  static readonly RELOAD_TIP = 'Reloads the articles. The topic list is part of the chat system prompt, '
    + 'so the next turn of every chat starts without the prompt cache once.';

  private readonly devtools = inject(AdminDevtoolsService);
  private readonly cdr = inject(ChangeDetectorRef);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);

  private readonly poller = new DashboardPoller<KnowledgeBaseStatus>(
    () => this.devtools.getCorpusStatus('knowledge-base'),
    status => !!status.currentJob,
    result => this.onPollResult(result)
  );
  private actionSub: Subscription | null = null;
  private previewSub: Subscription | null = null;
  private lingerTimer: ReturnType<typeof setTimeout> | null = null;
  private trackedJobId: string | null = null;
  private previewReturnFocus: HTMLElement | null = null;
  private destroyed = false;

  @ViewChild(ReindexConfirmDialogComponent) confirmDialog?: ReindexConfirmDialogComponent;
  @ViewChild('previewDialog') previewDialog?: ElementRef<HTMLDialogElement>;
  @ViewChild('previewTitle') previewTitle?: ElementRef<HTMLHeadingElement>;

  readonly fmt = fmt;
  readonly reloadTip = KnowledgeBaseDashboardComponent.RELOAD_TIP;

  /** The last good status; null until the first answer. */
  status: KnowledgeBaseStatus | null = null;
  /** The last poll failed; the dashboard keeps showing the last good answer. */
  loadError: string | null = null;
  /** A finished pass the progress card keeps for `ENDED_JOB_LINGER_MS`. */
  endedJob: CorpusIndexJob | null = null;

  /** A reload request is in flight. */
  starting = false;
  actionMessage: string | null = null;
  /** `warning` for a 409, `error` for any other refusal. */
  actionMessageKind: 'warning' | 'error' = 'error';

  /** The loaded topics, for the probe's suggestions; replaced only when the topics change. */
  topics: readonly string[] = [];

  readonly articleTable = new TableState<KnowledgeArticleInfo>('topic', 'asc').registerAccessors(
    {
      topic: a => a.topic,
      title: a => a.hasTitle ? a.title : null,
      summary: a => a.hasSummary ? 1 : 0,
      bytes: a => a.bytes,
      modified: a => fmt.parseTimestamp(a.lastWriteUtc)
    },
    {
      topic: a => a.topic,
      title: a => a.hasTitle ? a.title : null,
      hasTitle: exactFilter(a => presence(a.hasTitle)),
      hasSummary: exactFilter(a => presence(a.hasSummary))
    }
  );

  /** The article the preview shows; null while the dialog has never opened. */
  previewTopic: string | null = null;
  previewArticle: KnowledgeArticleSource | null = null;
  previewLoading = false;
  previewError: string | null = null;
  /** The result of the last Copy article source, for the polite status line. */
  previewCopyStatus = '';

  ngOnInit(): void {
    ensureOverlayPolyfills();
    this.poller.start();
  }

  ngOnDestroy(): void {
    this.destroyed = true;
    this.poller.stop();
    this.actionSub?.unsubscribe();
    this.previewSub?.unsubscribe();
    this.clearLinger();
    const dialog = this.previewDialog?.nativeElement;
    if (dialog?.open) {
      dialog.close();
    }
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

  private onPollResult(result: PollResult<KnowledgeBaseStatus>): void {
    if (result.ok) {
      this.loadError = null;
      this.applyStatus(result.value);
    } else {
      this.loadError = `${this.describeError(result.error, 'The knowledge base status could not be loaded')} `
        + `Trying again in ${result.retryInMs / 1000} s.`;
    }
    this.cdr.markForCheck();
  }

  private applyStatus(status: KnowledgeBaseStatus): void {
    this.status = status;
    const topics = (status.articles ?? []).map(a => a.topic);
    if (topics.join('\n') !== this.topics.join('\n')) {
      this.topics = topics;
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
        }, KnowledgeBaseDashboardComponent.ENDED_JOB_LINGER_MS);
      }
    }
  }

  private clearLinger(): void {
    if (this.lingerTimer !== null) {
      clearTimeout(this.lingerTimer);
      this.lingerTimer = null;
    }
  }

  // ---------------------------------------------------------------- reload

  /**
   * Starts a reload. `acknowledgeActiveBenchmark` is true when it is resent from the GnollBench
   * warning's *Reload anyway*.
   */
  reload(acknowledgeActiveBenchmark = false): void {
    if (this.starting) {
      return;
    }
    this.starting = true;
    this.actionMessage = null;
    this.cdr.markForCheck();
    this.actionSub?.unsubscribe();
    this.actionSub = this.devtools.startCorpusReindex('knowledge-base', acknowledgeActiveBenchmark).subscribe({
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
          this.confirmDialog?.open(refusal.message ?? 'A GnollBench run is executing and reads the knowledge base.');
          return;
        }
        if (refusal.status === 409) {
          this.actionMessageKind = 'warning';
          this.actionMessage = refusal.message ?? 'A pass is already running.';
          this.cdr.markForCheck();
          this.poller.pollNow();
          return;
        }
        this.actionMessageKind = 'error';
        this.actionMessage = refusal.status === 400
          ? refusal.message ?? `${this.status?.configKey ?? 'KbPath'} is not configured, or its folder does not exist.`
          : this.describeError(error, 'The reload could not be started');
        this.cdr.markForCheck();
      }
    });
  }

  private describeError(error: unknown, fallback: string): string {
    const refusal = readCorpusError(error);
    if (refusal.message) {
      return refusal.message;
    }
    if (refusal.status === 0) {
      return `${fallback}: the server could not be reached.`;
    }
    return refusal.status !== null ? `${fallback} (HTTP ${refusal.status}).` : `${fallback}.`;
  }

  // ---------------------------------------------------------------- figures and health

  get articles(): readonly KnowledgeArticleInfo[] {
    return this.status?.articles ?? [];
  }

  get topicListChars(): number {
    return this.status?.topicListChars ?? 0;
  }

  /** The topic list's size in tokens, estimated at `CHARS_PER_TOKEN` characters a token. */
  get topicListTokens(): number {
    return Math.round(this.topicListChars / CHARS_PER_TOKEN);
  }

  get missingTitle(): KnowledgeArticleInfo[] {
    return this.articles.filter(a => !a.hasTitle);
  }

  get missingSummary(): KnowledgeArticleInfo[] {
    return this.articles.filter(a => !a.hasSummary);
  }

  get shadowedTopics(): readonly KnowledgeShadowedTopic[] {
    return this.status?.shadowedTopics ?? [];
  }

  get unreadableFiles(): readonly FailedCorpusFile[] {
    return this.status?.index?.failedFiles ?? [];
  }

  get unreadableCount(): number {
    return this.status?.index?.failedFileCount ?? 0;
  }

  get hasProblems(): boolean {
    return this.missingTitle.length > 0 || this.missingSummary.length > 0
      || this.shadowedTopics.length > 0 || this.unreadableCount > 0;
  }

  /** "and N more" for the unreadable files the server left out of its list, or ''. */
  get unreadableMoreText(): string {
    const more = this.unreadableCount - this.unreadableFiles.length;
    return more > 0 ? `and ${fmt.formatNumber(more)} more` : '';
  }

  // ---------------------------------------------------------------- articles table

  onTableChanged(): void {
    this.cdr.markForCheck();
  }

  onTextFilter(column: ArticleTextFilter, event: Event): void {
    this.articleTable.setFilter(column, (event.target as HTMLInputElement).value);
    this.cdr.markForCheck();
  }

  onPresenceFilter(column: ArticlePresenceColumn, event: Event): void {
    this.articleTable.setFilter(column, (event.target as HTMLSelectElement).value);
    this.cdr.markForCheck();
  }

  textFilter(column: ArticleTextFilter): string {
    return this.articleTable.filters[column] ?? '';
  }

  presenceFilter(column: ArticlePresenceColumn): PresenceFilter {
    return (this.articleTable.filters[column] ?? '') as PresenceFilter;
  }

  clearFilters(): void {
    this.articleTable.clearFilters();
    this.cdr.markForCheck();
  }

  // ---------------------------------------------------------------- preview

  /** Opens the preview of `article` and loads its source; `trigger` takes the focus back on close. */
  openPreview(article: KnowledgeArticleInfo, trigger: HTMLElement): void {
    this.previewReturnFocus = trigger;
    this.previewTopic = article.topic;
    this.previewArticle = null;
    this.previewError = null;
    this.previewLoading = true;
    this.previewCopyStatus = '';
    this.previewSub?.unsubscribe();
    this.cdr.markForCheck();
    this.cdr.detectChanges();

    const dialog = this.previewDialog?.nativeElement;
    if (dialog && !dialog.open) {
      dialog.showModal();
    }
    refreshAnchorPositioning();
    this.previewTitle?.nativeElement.focus();

    const topic = article.topic;
    this.previewSub = this.devtools.getKnowledgeArticle(topic).subscribe({
      next: source => {
        this.previewLoading = false;
        this.previewArticle = source;
        this.cdr.markForCheck();
      },
      error: (error: unknown) => {
        this.previewLoading = false;
        this.previewError = this.describePreviewError(error, topic);
        this.cdr.markForCheck();
      }
    });
  }

  closePreview(): void {
    const dialog = this.previewDialog?.nativeElement;
    if (dialog?.open) {
      dialog.close();
    }
  }

  /** The dialog closed, by its button, Escape or a backdrop click: stop loading, focus the row's button. */
  onPreviewClosed(): void {
    if (this.destroyed) {
      return;
    }
    this.previewSub?.unsubscribe();
    this.previewSub = null;
    this.previewLoading = false;
    this.cdr.markForCheck();
    const target = this.previewReturnFocus;
    this.previewReturnFocus = null;
    if (target?.isConnected) {
      target.focus();
    } else {
      this.host.nativeElement.querySelector<HTMLElement>('#kb-articles-wrap')?.focus();
    }
  }

  /**
   * Light dismiss where `closedby` is unsupported: a backdrop click reports the dialog itself as the
   * target, so a hit outside its border box closes it.
   */
  onPreviewDialogClick(event: MouseEvent): void {
    if ('closedBy' in HTMLDialogElement.prototype) {
      return;
    }
    const dialog = this.previewDialog?.nativeElement;
    if (!dialog || event.target !== dialog) {
      return;
    }
    const rect = dialog.getBoundingClientRect();
    const inside = rect.top <= event.clientY && event.clientY <= rect.top + rect.height
      && rect.left <= event.clientX && event.clientX <= rect.left + rect.width;
    if (!inside) {
      dialog.close();
    }
  }

  async copyPreview(): Promise<void> {
    const source = this.previewArticle?.source ?? '';
    const copied = await copyToClipboard(source);
    this.previewCopyStatus = copied ? 'Article source copied.' : 'Copy failed. Select the text and copy it instead.';
    this.cdr.markForCheck();
  }

  private describePreviewError(error: unknown, topic: string): string {
    const refusal = readCorpusError(error);
    if (refusal.status === 404) {
      return `No article has the topic ${topic} now. The articles may have been reloaded since the list was loaded.`;
    }
    return this.describeError(error, 'The article could not be loaded');
  }
}
