import {
  ChangeDetectionStrategy,
  ChangeDetectorRef,
  Component,
  Input,
  OnDestroy,
  OnInit,
  inject
} from '@angular/core';
import { Observable, Subject, Subscription, catchError, map, of, switchMap } from 'rxjs';

import {
  AdminDevtoolsService,
  CorpusKindSlug,
  CorpusProbeResult,
  readCorpusError
} from '../../../../services/admin-devtools.service';
import { copyToClipboard } from '../../../../utils/clipboard.util';
import { ensureOverlayPolyfills } from '../../../../utils/polyfills.util';
import { formatDuration, formatNumber } from '../devtools-format';
import { HealthStatusComponent } from '../health-status/health-status.component';

/** The longest query the server accepts. */
export const MAX_PROBE_QUERY_LENGTH = 500;

/** A result longer than this many lines, or characters, gets a Show all toggle. */
const COLLAPSE_LINES = 20;
const COLLAPSE_CHARS = 2400;

type ProbeOutcome = { ok: true; result: CorpusProbeResult } | { ok: false; error: string };

/**
 * A corpus dashboard's search probe card: a form that runs the corpus's real chat tool through
 * `AdminDevtoolsService.probeCorpus(kind, query, filter)` and shows what the tool returned, with
 * its length against the chat's cap, its time, and whether it answered (*Found*) or refused
 * (*Tool refused*). Nothing is stored. A new submit cancels the request in flight, and so does
 * destroying the component.
 *
 * ```html
 * <app-corpus-search-probe class="gh-dash-wide" kind="gnollhack-wiki" toolName="wiki_search"
 *     idPrefix="gw" filterLabel="Category" [filterOptions]="categoryNames" />
 * ```
 *
 * The filter select is rendered only when `filterLabel` and `filterOptions` are both given; its
 * first option, *Any*, sends no filter. `topicSuggestions` adds a `<datalist>` to the query field.
 * The result is plain text in a collapsed `pre.gh-dash-pre` with Show all, and its copy button
 * copies the full text. Ids start with `idPrefix`: `{p}-probe-title`, `{p}-probe-query`,
 * `{p}-probe-filter`, `{p}-probe-run`, `{p}-probe-output`, `{p}-probe-copy-btn`.
 */
@Component({
  selector: 'app-corpus-search-probe',
  standalone: true,
  imports: [HealthStatusComponent],
  templateUrl: './corpus-search-probe.component.html',
  styleUrl: './corpus-search-probe.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class CorpusSearchProbeComponent implements OnInit, OnDestroy {
  private readonly devtools = inject(AdminDevtoolsService);
  private readonly cdr = inject(ChangeDetectorRef);

  /** The corpus whose probe endpoint runs. */
  @Input({ required: true }) kind!: CorpusKindSlug;
  /** The chat tool the endpoint runs, for the result line: `wiki_search`, `nethack_wiki_search`, `get_knowledge_article`. */
  @Input({ required: true }) toolName = '';
  /** The prefix of every id. */
  @Input({ required: true }) idPrefix = '';
  @Input() heading = 'Search probe';
  /** The query field's label: *Search terms*, or *Topic* for the knowledge base. */
  @Input() queryLabel = 'Search terms';
  /** The filter select's label, such as *Category*; no select without it. */
  @Input() filterLabel: string | null = null;
  /** The filter values; no select without them. */
  @Input() filterOptions: readonly string[] | null = null;
  /** The label of the first filter option, which sends no filter. */
  @Input() filterAnyLabel = 'Any';
  /** Suggestions for the query field, as a `<datalist>`. */
  @Input() topicSuggestions: readonly string[] | null = null;

  readonly maxQueryLength = MAX_PROBE_QUERY_LENGTH;

  query = '';
  filter = '';
  running = false;
  result: CorpusProbeResult | null = null;
  requestError: string | null = null;
  validationError: string | null = null;
  expanded = false;
  copyStatus = '';

  private readonly requests = new Subject<{ query: string; filter: string } | null>();
  private readonly subscription: Subscription;

  constructor() {
    this.subscription = this.requests.pipe(
      switchMap(request => request ? this.run(request.query, request.filter) : of(null))
    ).subscribe(outcome => {
      if (!outcome) {
        return;
      }
      this.running = false;
      if (outcome.ok) {
        this.result = outcome.result;
        this.requestError = null;
      } else {
        this.result = null;
        this.requestError = outcome.error;
      }
      this.cdr.markForCheck();
    });
  }

  ngOnInit(): void {
    ensureOverlayPolyfills();
  }

  ngOnDestroy(): void {
    this.subscription.unsubscribe();
  }

  get hasFilter(): boolean {
    return !!this.filterLabel && !!this.filterOptions;
  }

  get hasSuggestions(): boolean {
    return (this.topicSuggestions?.length ?? 0) > 0;
  }

  onQueryInput(event: Event): void {
    this.query = (event.target as HTMLInputElement).value;
    if (this.validationError && this.query.trim()) {
      this.validationError = null;
    }
  }

  onFilterChange(event: Event): void {
    this.filter = (event.target as HTMLSelectElement).value;
  }

  /** Runs the probe; an empty query is refused here, as the server would. */
  submit(event?: Event): void {
    event?.preventDefault();
    const query = this.query.trim();
    if (!query) {
      this.validationError = 'Enter something to search for.';
      this.cdr.markForCheck();
      return;
    }
    this.validationError = null;
    this.running = true;
    this.requestError = null;
    this.expanded = false;
    this.copyStatus = '';
    this.cdr.markForCheck();
    this.requests.next({ query, filter: this.hasFilter ? this.filter : '' });
  }

  /** What the result shows and copies: the tool's content, else its error. */
  get resultText(): string {
    return this.result ? (this.result.content || this.result.error || '') : '';
  }

  get resultStatusWord(): string {
    return this.result?.success ? 'Found' : 'Tool refused';
  }

  /** "wiki_search · 4,812 of 10,000 characters · 38 ms". */
  get summaryText(): string {
    const r = this.result;
    if (!r) {
      return '';
    }
    return `${r.toolName || this.toolName} · ${formatNumber(r.lengthChars)} of ${formatNumber(r.maxResultLength)} characters`
      + ` · ${formatDuration(r.elapsedMs)}`;
  }

  /** The result is longer than the chat's cap, so the chat would see it cut. */
  get overCap(): boolean {
    return !!this.result && this.result.lengthChars > this.result.maxResultLength;
  }

  get capText(): string {
    return this.result ? formatNumber(this.result.maxResultLength) : '';
  }

  /** The result is long enough to be collapsed behind Show all. */
  get collapsible(): boolean {
    const t = this.resultText;
    return t.length > COLLAPSE_CHARS || t.split('\n').length > COLLAPSE_LINES;
  }

  toggleExpanded(): void {
    this.expanded = !this.expanded;
    this.cdr.markForCheck();
  }

  async copyResult(): Promise<void> {
    const copied = await copyToClipboard(this.resultText);
    this.copyStatus = copied ? 'Probe result copied.' : 'Copy failed. Select the text and copy it instead.';
    this.cdr.markForCheck();
  }

  private run(query: string, filter: string): Observable<ProbeOutcome> {
    return this.devtools.probeCorpus(this.kind, query, filter || null).pipe(
      map((result): ProbeOutcome => ({ ok: true, result })),
      catchError((error: unknown) => of<ProbeOutcome>({ ok: false, error: this.describeError(error) }))
    );
  }

  private describeError(error: unknown): string {
    const refusal = readCorpusError(error);
    if (refusal.message) {
      return refusal.message;
    }
    if (refusal.status === 0) {
      return 'The probe could not be run: the server could not be reached.';
    }
    return refusal.status ? `The probe could not be run (HTTP ${refusal.status}).` : 'The probe could not be run.';
  }
}
