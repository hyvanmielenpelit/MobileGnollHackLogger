import {
  ChangeDetectionStrategy,
  ChangeDetectorRef,
  Component,
  HostListener,
  OnDestroy,
  OnInit,
  inject
} from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { EMPTY, Observable, Subject, Subscription, catchError, map, of, switchMap, timer } from 'rxjs';

import {
  AdminDevtoolsService,
  MakedefsOutcomeKind,
  SourceDashboard,
  SourceIndexJob,
  SourceIndexPhase,
  SourceIndexStatus,
  SourceIndexTrigger,
  SourceJobKind,
  SourceJobOutcome,
  SourceJobStarted
} from '../../../services/admin-devtools.service';
import { InfoTipComponent } from '../../../shared/info-tip/info-tip.component';
import { copyToClipboard } from '../../../utils/clipboard.util';
import { parseServerUtcDate } from '../../../utils/date.util';
import { ensureOverlayPolyfills } from '../../../utils/polyfills.util';

/** The status words of the shared `.gh-health-status` chip. */
export type HealthStatus = 'ok' | 'warning' | 'error' | 'info' | 'running';

export type StageState = 'pending' | 'current' | 'done' | 'skipped' | 'ended';

export interface SourceStageItem {
  key: SourceIndexPhase;
  name: string;
  state: StageState;
  note: string | null;
}

export interface SourceBanner {
  status: HealthStatus;
  word: string;
  sentence: string;
}

/** The pipeline's stages after `Idle`, in order. */
export const SOURCE_STAGES: ReadonlyArray<{ key: SourceIndexPhase; name: string }> = [
  { key: 'WaitingForRepository', name: 'Waiting for repository' },
  { key: 'CheckingMakedefs', name: 'Checking makedefs inputs' },
  { key: 'BuildingMakedefs', name: 'Building makedefs' },
  { key: 'GeneratingHeaders', name: 'Generating headers' },
  { key: 'ReadingFiles', name: 'Reading files' },
  { key: 'ParsingGameData', name: 'Parsing game data' },
  { key: 'Publishing', name: 'Publishing index' }
];

type PollResult = { ok: true; dashboard: SourceDashboard } | { ok: false; error: unknown };

/**
 * The GnollHack Source sub-tab of Developer Tools: the state of the source index, makedefs and
 * the running source job, polled from the server, with Re-index, Build makedefs and Cancel.
 */
@Component({
  selector: 'app-source-dashboard',
  standalone: true,
  imports: [InfoTipComponent, NgTemplateOutlet],
  templateUrl: './source-dashboard.component.html',
  styleUrl: './source-dashboard.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class SourceDashboardComponent implements OnInit, OnDestroy {
  /** Poll cadence while a source job runs. */
  static readonly ACTIVE_POLL_MS = 1000;
  /** Poll cadence while nothing runs. */
  static readonly IDLE_POLL_MS = 15000;
  /** Delays after the first, second and every later consecutive failed poll. */
  static readonly ERROR_BACKOFF_MS: readonly number[] = [5000, 10000, 30000];
  /** How long a finished job's progress card stays, with its outcome. */
  static readonly ENDED_JOB_LINGER_MS = 10000;

  private readonly devtools = inject(AdminDevtoolsService);
  private readonly cdr = inject(ChangeDetectorRef);

  /** Each value replaces the pending poll: a delay in ms, or null to stop until rescheduled. */
  private readonly schedule$ = new Subject<number | null>();
  private pollSub: Subscription | null = null;
  private actionSub: Subscription | null = null;
  private cancelSub: Subscription | null = null;
  private lingerTimer: ReturnType<typeof setTimeout> | null = null;
  private trackedJobId: string | null = null;
  private failures = 0;
  private pendingDelayMs = 0;
  private paused = false;
  private destroyed = false;

  private readonly numberFormat = new Intl.NumberFormat();
  private readonly dateTimeFormat = new Intl.DateTimeFormat(undefined, { dateStyle: 'medium', timeStyle: 'medium' });
  private readonly timeFormat = new Intl.DateTimeFormat(undefined, { timeStyle: 'medium' });
  private readonly relativeFormat = new Intl.RelativeTimeFormat(undefined, { numeric: 'auto' });

  dashboard: SourceDashboard | null = null;
  /** The last poll failed; the dashboard keeps showing the last good answer. */
  loadError: string | null = null;
  /** A finished job whose card stays for `ENDED_JOB_LINGER_MS`. */
  endedJob: SourceIndexJob | null = null;

  /** A start request is in flight. */
  starting = false;
  actionMessage: string | null = null;
  /** `warning` for a 409 (another job holds the source), `error` for any other refusal. */
  actionMessageKind: 'warning' | 'error' = 'error';

  canceling = false;
  cancelMessage: string | null = null;

  /** The result of the last Copy build output, for the polite status line. */
  copyStatus = '';

  ngOnInit(): void {
    ensureOverlayPolyfills();
    this.pollSub = this.schedule$.pipe(
      switchMap(delay => delay === null
        ? EMPTY
        : timer(delay).pipe(
          switchMap(() => this.devtools.getSourceStatus().pipe(
            map((dashboard): PollResult => ({ ok: true, dashboard })),
            catchError((error: unknown) => of<PollResult>({ ok: false, error }))
          ))
        ))
    ).subscribe(result => this.onPollResult(result));
    this.scheduleNext(0);
  }

  ngOnDestroy(): void {
    this.destroyed = true;
    this.pollSub?.unsubscribe();
    this.actionSub?.unsubscribe();
    this.cancelSub?.unsubscribe();
    this.schedule$.complete();
    this.clearLinger();
  }

  @HostListener('document:visibilitychange')
  onVisibilityChange(): void {
    if (this.destroyed) {
      return;
    }
    if (document.hidden) {
      this.paused = true;
      this.schedule$.next(null);
    } else if (this.paused) {
      this.paused = false;
      // Back from a hidden page: fresh data at once, unless the server was failing.
      this.schedule$.next(this.failures > 0 ? this.pendingDelayMs : 0);
    }
  }

  // ---------------------------------------------------------------- polling

  /** Polls now, cancelling a pending poll and any request in flight. */
  retry(): void {
    this.scheduleNext(0);
  }

  private scheduleNext(delayMs: number): void {
    if (this.destroyed) {
      return;
    }
    this.pendingDelayMs = delayMs;
    if (document.hidden) {
      this.paused = true;
      this.schedule$.next(null);
      return;
    }
    this.schedule$.next(delayMs);
  }

  private onPollResult(result: PollResult): void {
    if (result.ok) {
      this.failures = 0;
      this.loadError = null;
      this.applyDashboard(result.dashboard);
      this.scheduleNext(result.dashboard.gnollHack.currentJob
        ? SourceDashboardComponent.ACTIVE_POLL_MS
        : SourceDashboardComponent.IDLE_POLL_MS);
    } else {
      this.failures++;
      const backoff = SourceDashboardComponent.ERROR_BACKOFF_MS;
      const delay = backoff[Math.min(this.failures, backoff.length) - 1];
      this.loadError = `${this.describeError(result.error, 'The source status could not be loaded')} `
        + `Trying again in ${delay / 1000} s.`;
      this.scheduleNext(delay);
    }
    this.cdr.markForCheck();
  }

  private applyDashboard(dashboard: SourceDashboard): void {
    this.dashboard = dashboard;
    const job = dashboard.gnollHack.currentJob;
    if (job) {
      this.trackedJobId = job.id;
      this.endedJob = null;
      this.clearLinger();
      return;
    }
    if (this.trackedJobId !== null) {
      const finished = dashboard.gnollHack.recentJobs.find(j => j.id === this.trackedJobId) ?? null;
      this.trackedJobId = null;
      if (finished) {
        this.endedJob = finished;
        this.clearLinger();
        this.lingerTimer = setTimeout(() => {
          this.lingerTimer = null;
          this.endedJob = null;
          this.cdr.markForCheck();
        }, SourceDashboardComponent.ENDED_JOB_LINGER_MS);
      }
    }
  }

  private clearLinger(): void {
    if (this.lingerTimer !== null) {
      clearTimeout(this.lingerTimer);
      this.lingerTimer = null;
    }
  }

  // ---------------------------------------------------------------- actions

  get gnollHack(): SourceIndexStatus | null {
    return this.dashboard?.gnollHack ?? null;
  }

  get netHack(): SourceIndexStatus | null {
    return this.dashboard?.netHack ?? null;
  }

  /** The job the progress card shows: the running one, else a just-finished one. */
  get progressJob(): SourceIndexJob | null {
    return this.gnollHack?.currentJob ?? this.endedJob;
  }

  /** Why Re-index and Build makedefs are unavailable, or null when they are available. */
  get actionsBlockedReason(): string | null {
    const status = this.gnollHack;
    if (!status) {
      return 'The source status has not loaded yet.';
    }
    if (!status.configured) {
      return 'SourceCodePath is not configured.';
    }
    if (!status.pathExists) {
      return 'The SourceCodePath folder does not exist.';
    }
    if (status.currentJob || this.starting) {
      return 'A source job is running.';
    }
    return null;
  }

  /** The `aria-describedby` of an action button: the reason it is unavailable and the last refusal. */
  actionDescribedBy(extraId: string | null = null): string | null {
    const ids: string[] = [];
    if (this.actionsBlockedReason) {
      ids.push('sd-actions-reason');
    }
    if (this.actionMessage) {
      ids.push('sd-action-message');
    }
    if (extraId) {
      ids.push(extraId);
    }
    return ids.length > 0 ? ids.join(' ') : null;
  }

  reindex(): void {
    this.startJob(() => this.devtools.startReindex());
  }

  buildMakedefs(): void {
    this.startJob(() => this.devtools.startMakedefsBuild());
  }

  private startJob(call: () => Observable<SourceJobStarted>): void {
    if (this.actionsBlockedReason) {
      return;
    }
    this.starting = true;
    this.actionMessage = null;
    this.actionSub?.unsubscribe();
    this.actionSub = call().subscribe({
      next: () => {
        this.starting = false;
        this.cdr.markForCheck();
        this.scheduleNext(0);
      },
      error: (error: unknown) => {
        this.starting = false;
        const conflict = error instanceof HttpErrorResponse && error.status === 409;
        this.actionMessageKind = conflict ? 'warning' : 'error';
        this.actionMessage = this.describeError(error, 'The source job could not be started');
        this.cdr.markForCheck();
        if (conflict) {
          // Another job holds the source: show it.
          this.scheduleNext(0);
        }
      }
    });
  }

  cancel(): void {
    if (this.canceling) {
      return;
    }
    this.canceling = true;
    this.cancelMessage = null;
    this.cancelSub?.unsubscribe();
    this.cancelSub = this.devtools.cancelSourceJob().subscribe({
      next: result => {
        this.canceling = false;
        if (!result.canceled) {
          this.cancelMessage = 'Nothing to cancel: the job had already finished.';
        }
        this.cdr.markForCheck();
        this.scheduleNext(0);
      },
      error: (error: unknown) => {
        this.canceling = false;
        this.cancelMessage = this.describeError(error, 'The job could not be canceled');
        this.cdr.markForCheck();
      }
    });
  }

  /** Output lines as one plain-text block. */
  outputText(lines: string[] | null): string {
    return (lines ?? []).join('\n');
  }

  async copyOutput(lines: string[] | null): Promise<void> {
    const copied = await copyToClipboard(this.outputText(lines));
    this.copyStatus = copied ? 'Build output copied.' : 'Copy failed. Select the text and copy it instead.';
    this.cdr.markForCheck();
  }

  private describeError(error: unknown, fallback: string): string {
    if (error instanceof HttpErrorResponse) {
      const body = error.error as { message?: unknown } | null;
      if (body && typeof body.message === 'string' && body.message.trim()) {
        return body.message;
      }
      if (error.status === 400) {
        return 'SourceCodePath is not configured.';
      }
      if (error.status === 409) {
        return 'A source job is running.';
      }
      if (error.status === 0) {
        return `${fallback}: the server could not be reached.`;
      }
      return `${fallback} (HTTP ${error.status}).`;
    }
    return `${fallback}.`;
  }

  // ---------------------------------------------------------------- banner

  get banner(): SourceBanner | null {
    const s = this.gnollHack;
    if (!s) {
      return null;
    }
    if (!s.configured) {
      return {
        status: 'warning', word: 'Not configured',
        sentence: 'SourceCodePath is not set, so the source tools have nothing to index.'
      };
    }
    if (!s.pathExists) {
      return {
        status: 'error', word: 'Not configured',
        sentence: 'The SourceCodePath folder does not exist on the server.'
      };
    }
    const job = s.currentJob;
    if (job) {
      const stage = this.stageName(job.phase);
      if (job.phase === 'WaitingForRepository') {
        return {
          status: 'running', word: 'Waiting for repository',
          sentence: 'A Git operation is changing the source folder; the pass starts once it settles.'
        };
      }
      if (job.kind === 'BuildMakedefs' || job.phase === 'BuildingMakedefs' || job.phase === 'GeneratingHeaders') {
        return {
          status: 'running', word: 'Building makedefs',
          sentence: stage ? `Stage: ${stage}.` : 'The build is starting.'
        };
      }
      return {
        status: 'running', word: 'Indexing',
        sentence: stage ? `Stage: ${stage}.` : 'The pass is starting.'
      };
    }
    const last = this.recentJobs(s)[0] ?? null;
    if (last && last.outcome === 'Failed') {
      return {
        status: 'error', word: 'Last pass failed',
        sentence: last.message || 'See Recent activity for the job.'
      };
    }
    if (!s.index) {
      return {
        status: 'warning', word: 'Not indexed',
        sentence: 'No index has been published yet; the source tools answer that indexing is in progress.'
      };
    }
    if (!s.upToDate) {
      const next = this.formatTime(s.nextCheckAtUtc);
      return {
        status: 'warning', word: 'Index out of date',
        sentence: next
          ? `HEAD has moved since the index was built; the next automatic check is at ${next}.`
          : 'HEAD has moved since the index was built.'
      };
    }
    return {
      status: 'ok', word: 'Ready',
      sentence: 'The index matches the current HEAD of the source folder.'
    };
  }

  // ---------------------------------------------------------------- progress

  stageName(phase: SourceIndexPhase): string | null {
    return SOURCE_STAGES.find(s => s.key === phase)?.name ?? null;
  }

  stageItems(job: SourceIndexJob): SourceStageItem[] {
    const index = SOURCE_STAGES.findIndex(s => s.key === job.phase);
    const running = job.outcome === 'Running';
    const completed = job.outcome === 'Succeeded' || job.outcome === 'SkippedNoChange';
    const makedefs = job.makedefsOutcome;

    return SOURCE_STAGES.map((stage, i): SourceStageItem => {
      let state: StageState;
      if (index < 0) {
        state = !running && completed ? 'done' : 'pending';
      } else if (i < index) {
        state = 'done';
      } else if (i === index) {
        state = running ? 'current' : completed ? 'done' : 'ended';
      } else {
        state = !running && completed ? 'skipped' : 'pending';
      }

      let note: string | null = null;
      const passed = state === 'done' || state === 'skipped';
      if (stage.key === 'BuildingMakedefs' || stage.key === 'GeneratingHeaders') {
        if ((makedefs === 'UpToDate' || makedefs === 'SkippedBranch') && passed) {
          state = 'skipped';
          note = makedefs === 'UpToDate' ? 'Inputs unchanged' : 'Not built on this branch';
        } else if ((makedefs === 'BuildFailed' || makedefs === 'ExecutableMissing') && passed) {
          if (stage.key === 'BuildingMakedefs') {
            state = 'ended';
            note = makedefs === 'BuildFailed' ? 'Build failed' : 'Executable missing';
          } else {
            state = 'skipped';
            note = 'Headers on disk kept';
          }
        } else if (makedefs === 'RunFailed' && stage.key === 'GeneratingHeaders' && passed) {
          state = 'ended';
          note = 'makedefs run failed';
        }
      }
      if (state === 'ended' && note === null && i === index) {
        note = job.outcome === 'Canceled' ? 'Canceled here' : 'Failed here';
      }
      return { key: stage.key, name: stage.name, state, note };
    });
  }

  stateWord(state: StageState): string {
    switch (state) {
      case 'current': return '(current)';
      case 'done': return '(done)';
      case 'skipped': return '(skipped)';
      case 'ended': return '(ended)';
      default: return '';
    }
  }

  /** True while the job reads files and knows how many: the progress bar is determinate. */
  isDeterminate(job: SourceIndexJob): boolean {
    return job.outcome === 'Running' && job.phase === 'ReadingFiles' && (job.filesTotal ?? 0) > 0;
  }

  fileProgressText(job: SourceIndexJob): string {
    return `${this.formatNumber(job.filesRead)} of ${this.formatNumber(job.filesTotal ?? 0)} files`;
  }

  jobTitle(job: SourceIndexJob): string {
    const kind = job.kind === 'BuildMakedefs' ? 'makedefs build' : 'Source index pass';
    return job.outcome === 'Running' ? `${kind} in progress` : `${kind} finished`;
  }

  isBuildJob(job: SourceIndexJob): boolean {
    return job.kind === 'BuildMakedefs' || (job.outputTail?.length ?? 0) > 0;
  }

  triggerLabel(trigger: SourceIndexTrigger): string {
    switch (trigger) {
      case 'Manual': return 'Started manually';
      case 'RepositoryChange': return 'Repository change';
      case 'Startup': return 'Server start';
      case 'FollowUp': return 'Follow-up after a repository change';
      default: return trigger;
    }
  }

  kindLabel(kind: SourceJobKind): string {
    return kind === 'BuildMakedefs' ? 'makedefs build' : 'Index';
  }

  outcomeStatus(outcome: SourceJobOutcome): HealthStatus {
    switch (outcome) {
      case 'Succeeded': return 'ok';
      case 'Failed': return 'error';
      case 'Canceled': return 'warning';
      case 'SkippedNoChange': return 'info';
      default: return 'running';
    }
  }

  outcomeWord(outcome: SourceJobOutcome): string {
    switch (outcome) {
      case 'Succeeded': return 'Succeeded';
      case 'Failed': return 'Failed';
      case 'Canceled': return 'Canceled';
      case 'SkippedNoChange': return 'No change';
      default: return 'Running';
    }
  }

  makedefsOutcomeStatus(outcome: MakedefsOutcomeKind | null): HealthStatus {
    switch (outcome) {
      case 'Generated':
      case 'UpToDate': return 'ok';
      case 'SkippedBranch': return 'info';
      case 'BuildFailed':
      case 'ExecutableMissing':
      case 'RunFailed': return 'error';
      default: return 'info';
    }
  }

  makedefsOutcomeWord(outcome: MakedefsOutcomeKind | null): string {
    switch (outcome) {
      case 'Generated': return 'Headers generated';
      case 'UpToDate': return 'Up to date, not rebuilt';
      case 'SkippedBranch': return 'Skipped on this branch';
      case 'BuildFailed': return 'Build failed';
      case 'ExecutableMissing': return 'Executable missing';
      case 'RunFailed': return 'makedefs run failed';
      default: return 'Not built yet';
    }
  }

  inputsStatus(state: 'upToDate' | 'stale' | 'unknown'): HealthStatus {
    return state === 'upToDate' ? 'ok' : state === 'stale' ? 'warning' : 'info';
  }

  inputsWord(state: 'upToDate' | 'stale' | 'unknown'): string {
    return state === 'upToDate' ? 'Up to date' : state === 'stale' ? 'Changed since last build' : 'Unknown';
  }

  /** The finished jobs, newest first. */
  recentJobs(status: SourceIndexStatus): SourceIndexJob[] {
    return [...status.recentJobs].sort((a, b) => this.jobTime(b) - this.jobTime(a));
  }

  private jobTime(job: SourceIndexJob): number {
    const value = job.finishedAtUtc ?? job.startedAtUtc;
    const ms = value ? parseServerUtcDate(value).getTime() : NaN;
    return isNaN(ms) ? 0 : ms;
  }

  elapsedMs(job: SourceIndexJob): number {
    const start = parseServerUtcDate(job.startedAtUtc).getTime();
    const end = job.finishedAtUtc ? parseServerUtcDate(job.finishedAtUtc).getTime() : Date.now();
    return isNaN(start) || isNaN(end) || end < start ? 0 : end - start;
  }

  /** An ISO 8601 duration for a `<time datetime>`. */
  isoDuration(ms: number): string {
    return `PT${(Math.max(0, ms) / 1000).toFixed(1)}S`;
  }

  // ---------------------------------------------------------------- formatting

  formatNumber(value: number | null | undefined): string {
    return value === null || value === undefined ? '—' : this.numberFormat.format(value);
  }

  formatDuration(ms: number | null | undefined): string {
    if (ms === null || ms === undefined || ms < 0) {
      return '—';
    }
    if (ms < 1000) {
      return `${this.numberFormat.format(Math.round(ms))} ms`;
    }
    const totalSeconds = ms / 1000;
    if (totalSeconds < 60) {
      return `${totalSeconds.toFixed(1)} s`;
    }
    const whole = Math.round(totalSeconds);
    const hours = Math.floor(whole / 3600);
    const minutes = Math.floor((whole % 3600) / 60);
    const seconds = whole % 60;
    const pad = (n: number) => String(n).padStart(2, '0');
    return hours > 0 ? `${hours} h ${pad(minutes)} min` : `${minutes} min ${pad(seconds)} s`;
  }

  formatBytes(bytes: number): string {
    return `${this.numberFormat.format(Math.max(1, Math.round(bytes / 1024)))} KB`;
  }

  /** The local date and time of a server timestamp, or '' when there is none. */
  formatDateTime(value: string | null | undefined): string {
    const date = this.parse(value);
    return date ? this.dateTimeFormat.format(date) : '';
  }

  /** The local time of day of a server timestamp, or '' when there is none. */
  formatTime(value: string | null | undefined): string {
    const date = this.parse(value);
    return date ? this.timeFormat.format(date) : '';
  }

  /** "3 minutes ago", "in 5 minutes", or '' when there is no timestamp. */
  formatRelative(value: string | null | undefined): string {
    const date = this.parse(value);
    if (!date) {
      return '';
    }
    const seconds = Math.round((date.getTime() - Date.now()) / 1000);
    const abs = Math.abs(seconds);
    if (abs < 45) {
      return this.relativeFormat.format(0, 'second');
    }
    if (abs < 3600) {
      return this.relativeFormat.format(Math.round(seconds / 60), 'minute');
    }
    if (abs < 86400) {
      return this.relativeFormat.format(Math.round(seconds / 3600), 'hour');
    }
    return this.relativeFormat.format(Math.round(seconds / 86400), 'day');
  }

  /** The `datetime` attribute value: the instant in ISO form. */
  isoDateTime(value: string | null | undefined): string | null {
    const date = this.parse(value);
    return date ? date.toISOString() : null;
  }

  shortSha(sha: string | null | undefined): string {
    return sha ? sha.slice(0, 10) : '—';
  }

  private parse(value: string | null | undefined): Date | null {
    if (!value) {
      return null;
    }
    const date = parseServerUtcDate(value);
    return isNaN(date.getTime()) ? null : date;
  }
}
