import {
  ChangeDetectionStrategy,
  ChangeDetectorRef,
  Component,
  ElementRef,
  Input,
  OnDestroy,
  OnInit,
  inject
} from '@angular/core';
import { Subject, Subscription, catchError, map, of, switchMap } from 'rxjs';

import {
  AdminDevtoolsService,
  CorpusKind,
  CorpusToolUsage,
  CorpusToolUsageCorpus,
  CorpusToolUsageDays,
  CorpusToolUsageTool,
  readCorpusError
} from '../../../../services/admin-devtools.service';
import { formatDuration, formatNumber, formatTime, isoDateTime, parseTimestamp } from '../devtools-format';

/** The `localStorage` key of the chosen period, shared by every corpus dashboard. */
export const TOOL_USAGE_DAYS_KEY = 'overseer.admin.devtools.toolUsage.days';

/** The periods the server accepts, in the order they are offered. */
export const TOOL_USAGE_PERIODS: readonly CorpusToolUsageDays[] = [7, 30, 90];

const DEFAULT_DAYS: CorpusToolUsageDays = 30;
const DAY_MS = 24 * 60 * 60 * 1000;

/** Sparkline geometry, in viewBox units: a 2-unit bar every 3 units, 24 units high. */
const BAR_WIDTH = 2;
const BAR_STEP = 3;
const SPARK_HEIGHT = 24;
const SPARK_MAX_BAR = 22;

export interface SparkBar {
  x: number;
  y: number;
  height: number;
  /** A day without calls, drawn as a 1-unit baseline tick. */
  zero: boolean;
}

export interface Sparkline {
  viewBox: string;
  bars: SparkBar[];
}

type UsageOutcome = { ok: true; usage: CorpusToolUsage } | { ok: false; error: string };

const percentFormat = new Intl.NumberFormat(undefined, { style: 'percent', minimumFractionDigits: 1, maximumFractionDigits: 1 });
const dayFormat = new Intl.DateTimeFormat(undefined, { month: 'short', day: 'numeric', timeZone: 'UTC' });

/**
 * A corpus dashboard's *Chat tool usage* card: how the chat used the corpus's tools over the last 7,
 * 30 or 90 days, from `AdminDevtoolsService.getCorpusToolUsage(days)`. Tiles for the corpus's tools
 * together (calls, error rate, index-warming refusals, median and 95th percentile time, from the
 * server's per-corpus entry, since percentiles cannot be combined from per-tool values), then one
 * table row per tool with an inline SVG sparkline of the daily calls. Counts and timings only.
 *
 * ```html
 * <app-corpus-tool-usage class="gh-dash-wide" corpus="GnollHackWiki" idPrefix="gw" />
 * ```
 *
 * It loads the first time it scrolls into view (at once where `IntersectionObserver` is missing) and
 * again when the period changes. The period is a `.gh-choice` radio group remembered in
 * `localStorage` under `TOOL_USAGE_DAYS_KEY`. Ids start with `idPrefix`: `{p}-usage-title`,
 * `{p}-usage-days-{7|30|90}`, `{p}-usage-error`.
 */
@Component({
  selector: 'app-corpus-tool-usage',
  standalone: true,
  templateUrl: './corpus-tool-usage.component.html',
  styleUrl: './corpus-tool-usage.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class CorpusToolUsageComponent implements OnInit, OnDestroy {
  private readonly devtools = inject(AdminDevtoolsService);
  private readonly cdr = inject(ChangeDetectorRef);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);

  /** The server's name of the corpus whose tools are shown. */
  @Input({ required: true }) corpus!: CorpusKind;
  /** The prefix of every id. */
  @Input({ required: true }) idPrefix = '';
  @Input() heading = 'Chat tool usage';

  readonly periods = TOOL_USAGE_PERIODS;

  days: CorpusToolUsageDays = readStoredDays();
  usage: CorpusToolUsage | null = null;
  loading = false;
  loadError: string | null = null;
  /** The card has been in view, so the data is wanted. */
  started = false;

  private observer: IntersectionObserver | null = null;
  private readonly requests = new Subject<CorpusToolUsageDays>();
  private readonly subscription: Subscription;

  constructor() {
    this.subscription = this.requests.pipe(
      switchMap(days => this.devtools.getCorpusToolUsage(days).pipe(
        map((usage): UsageOutcome => ({ ok: true, usage })),
        catchError((error: unknown) => of<UsageOutcome>({ ok: false, error: describeError(error) }))
      ))
    ).subscribe(outcome => {
      this.loading = false;
      if (outcome.ok) {
        this.usage = outcome.usage;
        this.loadError = null;
      } else {
        this.loadError = outcome.error;
      }
      this.cdr.markForCheck();
    });
  }

  ngOnInit(): void {
    if (typeof IntersectionObserver === 'undefined') {
      this.start();
      return;
    }
    this.observer = new IntersectionObserver(entries => {
      if (entries.some(entry => entry.isIntersecting)) {
        this.disconnect();
        this.start();
      }
    });
    this.observer.observe(this.host.nativeElement);
  }

  ngOnDestroy(): void {
    this.disconnect();
    this.subscription.unsubscribe();
  }

  /** Chooses a period, remembers it and reloads once the card has been in view. */
  setDays(days: CorpusToolUsageDays): void {
    if (days === this.days) {
      return;
    }
    this.days = days;
    try {
      localStorage.setItem(TOOL_USAGE_DAYS_KEY, String(days));
    } catch {
      // Storage may be unavailable; the choice still applies until the page is left.
    }
    if (this.started) {
      this.load();
    }
    this.cdr.markForCheck();
  }

  /** Try again after a failed load. */
  retry(): void {
    this.load();
  }

  /** The corpus's tools together. */
  get corpusStats(): CorpusToolUsageCorpus | null {
    return this.usage?.corpora.find(c => c.corpus === this.corpus) ?? null;
  }

  /** One row per tool of the corpus, in the server's order. */
  get toolRows(): CorpusToolUsageTool[] {
    return this.usage?.tools.filter(t => t.corpus === this.corpus) ?? [];
  }

  /** Errors as a share of calls, or '—' without calls. */
  errorRate(stats: { calls: number; errors: number }): string {
    return stats.calls > 0 ? percentFormat.format(stats.errors / stats.calls) : '—';
  }

  formatNumber(value: number | null | undefined): string {
    return formatNumber(value);
  }

  formatDuration(ms: number | null | undefined): string {
    return formatDuration(ms);
  }

  formatTime(value: string | null | undefined): string {
    return formatTime(value);
  }

  isoDateTime(value: string | null | undefined): string | null {
    return isoDateTime(value);
  }

  sparkline(daily: readonly number[]): Sparkline {
    const peak = Math.max(0, ...daily);
    const width = Math.max(1, daily.length * BAR_STEP - (BAR_STEP - BAR_WIDTH));
    const bars = daily.map((count, i): SparkBar => {
      const height = count > 0 ? Math.max(1, Math.round((count / peak) * SPARK_MAX_BAR)) : 1;
      return { x: i * BAR_STEP, y: SPARK_HEIGHT - height, height, zero: count <= 0 };
    });
    return { viewBox: `0 0 ${width} ${SPARK_HEIGHT}`, bars };
  }

  /** "wiki_search daily calls, last 30 days: total 1,204, peak 96 on Oct 3". */
  sparklineLabel(row: CorpusToolUsageTool): string {
    const days = this.usage?.days ?? this.days;
    const prefix = `${row.tool} daily calls, last ${days} days`;
    const total = row.daily.reduce((sum, n) => sum + n, 0);
    if (total === 0) {
      return `${prefix}: none`;
    }
    const peak = Math.max(...row.daily);
    const peakIndex = row.daily.indexOf(peak);
    const day = this.dayLabel(peakIndex);
    return `${prefix}: total ${formatNumber(total)}, peak ${formatNumber(peak)}${day ? ` on ${day}` : ''}`;
  }

  /** The UTC date of day `index` of the period, such as "Oct 3"; '' when the start is unknown. */
  private dayLabel(index: number): string {
    const since = parseTimestamp(this.usage?.sinceUtc);
    return since ? dayFormat.format(new Date(since.getTime() + index * DAY_MS)) : '';
  }

  private start(): void {
    if (this.started) {
      return;
    }
    this.started = true;
    this.load();
  }

  private load(): void {
    this.loading = true;
    this.cdr.markForCheck();
    this.requests.next(this.days);
  }

  private disconnect(): void {
    this.observer?.disconnect();
    this.observer = null;
  }
}

/** The stored period when it is one the server accepts; otherwise 30. */
function readStoredDays(): CorpusToolUsageDays {
  try {
    const stored = Number(localStorage.getItem(TOOL_USAGE_DAYS_KEY));
    return (TOOL_USAGE_PERIODS as readonly number[]).includes(stored) ? stored as CorpusToolUsageDays : DEFAULT_DAYS;
  } catch {
    return DEFAULT_DAYS;
  }
}

function describeError(error: unknown): string {
  const refusal = readCorpusError(error);
  if (refusal.message) {
    return refusal.message;
  }
  if (refusal.status === 0) {
    return 'The tool usage could not be loaded: the server could not be reached.';
  }
  return refusal.status ? `The tool usage could not be loaded (HTTP ${refusal.status}).` : 'The tool usage could not be loaded.';
}
