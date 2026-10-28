import {
  ChangeDetectorRef,
  Component,
  ElementRef,
  Input,
  OnChanges,
  OnDestroy,
  OnInit,
  SimpleChanges,
  ViewChild,
  computed,
  inject,
  signal
} from '@angular/core';
import { ActivatedRoute, ParamMap, Router, RouterLink } from '@angular/router';
import { Observable, Subscription, catchError, defer, finalize, map, tap, throwError } from 'rxjs';
import { AdminBenchmarkService } from '../../../services/admin-benchmark.service';
import {
  AdminGnollBenchFeedbackService,
  GNOLLBENCH_ACCEPTED_SCOPES,
  GNOLLBENCH_FEEDBACK_CATEGORIES,
  GNOLLBENCH_FEEDBACK_STATUSES,
  GNOLLBENCH_FEEDBACK_TRIAGE_STATUSES,
  GNOLLBENCH_INFORMATION_CONDITIONS,
  GnollBenchAcceptedScope,
  GnollBenchFeedbackCounts,
  GnollBenchFeedbackFacetEntity,
  GnollBenchFeedbackFacetValue,
  GnollBenchFeedbackFacets,
  GnollBenchFeedbackFilters,
  GnollBenchFeedbackIssue,
  GnollBenchFeedbackOccurrence,
  GnollBenchFeedbackSort,
  GnollBenchFeedbackStatus,
  GnollBenchFeedbackStatusUpdate,
  GnollBenchFeedbackTriageStatus
} from '../../../services/admin-gnollbench-feedback.service';
import { adminApiError } from '../../../services/admin-users.service';
import { FeedbackTransferService, feedbackTransferStatus } from '../../../services/feedback-transfer.service';
import { CardListSort, CardListState } from '../../../shared/data-table/card-list-state';
import { FilterFacetComponent, FilterFacetOption } from '../../../shared/data-table/filter-facet.component';
import { TableState, customFilter } from '../../../shared/data-table/table-state';
import { InfoTipComponent } from '../../../shared/info-tip/info-tip.component';
import { parseServerUtcDate } from '../../../utils/date.util';
import { providerLabel, toolLabel } from '../../../utils/display-labels.util';
import { downloadTextFile } from '../../../utils/download.util';
import { ensureOverlayPolyfills, refreshAnchorPositioning } from '../../../utils/polyfills.util';
import { adminQueryList, readAdminQueryList } from '../../admin-url';
import { formatInteger } from '../../benchmark/shared/provider-availability/provider-availability';
import { DEFAULT_FEEDBACK_EXPORT_OPTIONS } from '../shared/feedback-export-dialog.component';
import {
  feedbackCategoryLabel,
  feedbackCauseLabel,
  feedbackNoticedViaLabel,
  feedbackStatusLabel,
  RESOLVED_VIA_FILTER_VALUES,
  formatResolvedVia,
  labelFrom,
  resolvedViaFilterLabel
} from '../shared/feedback-labels';
import { FeedbackSelectionBarComponent } from '../shared/feedback-selection-bar.component';
import { hiddenCharactersOf } from '../shared/feedback-hidden-characters';
import { FeedbackTriageDialogComponent, FeedbackTriageSubmission, FeedbackTriageValues } from '../shared/feedback-triage-dialog.component';
import {
  FEEDBACK_SELECTION_CAP,
  FeedbackExportOptions,
  FeedbackRange,
  FeedbackViewRecord,
  defaultFeedbackRange,
  loadFeedbackView,
  loadGnollBenchSelection,
  saveFeedbackView,
  saveGnollBenchSelection
} from '../shared/feedback-view-storage';

/** Where the view is remembered; Sort by reads its order from the same record. */
export const GNOLLBENCH_FEEDBACK_VIEW_STORAGE_KEY = 'overseer.admin.feedback.gnollbench.view';

/** The issues the list fetches at first, and how many more Show more fetches. */
export const GNOLLBENCH_FEEDBACK_BATCH = 25;

/** The self-reports an issue's disclosure fetches at first, and how many more Show more fetches. */
export const GNOLLBENCH_OCCURRENCE_BATCH = 10;

/** The server's limits on a triage change. */
export const GNOLLBENCH_NOTE_MAX = 1000;
export const GNOLLBENCH_RESOLUTION_MAX = 4000;
export const GNOLLBENCH_FIXED_IN_VERSION_MAX = 64;
export const GNOLLBENCH_FIXED_IN_VERSION_PATTERN = /^[0-9A-Za-z.+\-]{1,64}$/;

/** The longest search the server takes. */
export const GNOLLBENCH_SEARCH_MAX = 200;

/** The Status facet's selection while nothing else names one: the open work. */
export const DEFAULT_GNOLLBENCH_FEEDBACK_STATUSES: readonly GnollBenchFeedbackStatus[] = ['new', 'regressed'];

/** The `status` query value for no status filter; an absent parameter means the default. */
export const GNOLLBENCH_STATUS_QUERY_ALL = 'all';

/** The query parameter of the search; the facets use their keys, which are the server's names. */
export const GNOLLBENCH_SEARCH_QUERY = 'q';

/** The query parameter of the base set; absent means the accepted calls. */
export const GNOLLBENCH_ACCEPTED_QUERY = 'accepted';

/** The facets of the filter bar, in order. Each key is also its query parameter and its record key. */
export type GnollBenchFacetKey =
  'status' | 'resolvedVia' | 'category' | 'model' | 'provider' | 'run' | 'series' | 'group' | 'battery' | 'batch' |
  'suite' | 'question' | 'condition' | 'corpus' | 'variant' | 'harness';

interface GnollBenchFacetSpec {
  key: GnollBenchFacetKey;
  label: string;
  /** The filter field the selection is sent as. */
  field: keyof GnollBenchFeedbackFilters;
  /** Only these values are taken from an address or a record. */
  known?: readonly string[];
  /** Ids: only whole numbers are taken from an address or a record. */
  numeric?: boolean;
  /** Matched against the issue rather than its self-reports, so it never narrows the self-reports shown. */
  issueLevel?: boolean;
}

export const GNOLLBENCH_FACETS: readonly GnollBenchFacetSpec[] = [
  { key: 'status', label: 'Status', field: 'statuses', known: GNOLLBENCH_FEEDBACK_STATUSES, issueLevel: true },
  { key: 'resolvedVia', label: 'Resolved via', field: 'resolvedVia', known: RESOLVED_VIA_FILTER_VALUES, issueLevel: true },
  { key: 'category', label: 'Category', field: 'categories', known: GNOLLBENCH_FEEDBACK_CATEGORIES },
  { key: 'model', label: 'Model', field: 'models' },
  { key: 'provider', label: 'Provider', field: 'providers' },
  { key: 'run', label: 'Run', field: 'runs', numeric: true },
  { key: 'series', label: 'Series', field: 'series', numeric: true },
  { key: 'group', label: 'Analysis group', field: 'groups', numeric: true },
  { key: 'battery', label: 'Battery run', field: 'batteries', numeric: true },
  { key: 'batch', label: 'Model batch', field: 'batches', numeric: true },
  { key: 'suite', label: 'Suite', field: 'suites', numeric: true },
  { key: 'question', label: 'Question', field: 'questions', numeric: true },
  { key: 'condition', label: 'Information condition', field: 'conditions', known: GNOLLBENCH_INFORMATION_CONDITIONS },
  { key: 'corpus', label: 'Corpus', field: 'corpora' },
  { key: 'variant', label: 'Experimental Changes', field: 'variants' },
  { key: 'harness', label: 'Harness', field: 'harnesses' }
];

/** Every query parameter the panel reads; an address carrying any of them is applied instead of the remembered view. */
export const GNOLLBENCH_QUERY_PARAMS: readonly string[] = [
  ...GNOLLBENCH_FACETS.map(f => f.key), GNOLLBENCH_SEARCH_QUERY, GNOLLBENCH_ACCEPTED_QUERY
];

const GNOLLBENCH_SORTS: readonly CardListSort[] = [
  { id: 'lastSeen', label: 'Last seen', column: 'lastSeen', direction: 'desc' },
  { id: 'firstSeen', label: 'First seen', column: 'firstSeen', direction: 'desc' },
  { id: 'count', label: 'Most self-reports', column: 'count', direction: 'desc' },
  { id: 'runs', label: 'Most runs', column: 'runs', direction: 'desc' },
  { id: 'models', label: 'Most models', column: 'models', direction: 'desc' },
  { id: 'subject', label: 'Subject (A–Z)', column: 'subject', direction: 'asc' }
];

const CONDITION_LABELS: Readonly<Record<string, string>> = {
  production: 'Production',
  sourceOnly: 'Source code only',
  closedBook: 'Closed book'
};

const ACCEPTED_LABELS: Readonly<Record<GnollBenchAcceptedScope, string>> = {
  accepted: 'Accepted only',
  all: 'Accepted and refused',
  refused: 'Refused only'
};

/** What a stored id facet value reads as while the facets do not name it. */
const ID_FALLBACK_LABELS: Readonly<Partial<Record<GnollBenchFacetKey, string>>> = {
  run: 'Run',
  series: 'Series',
  group: 'Analysis group',
  battery: 'Battery run',
  batch: 'Model batch',
  suite: 'Suite',
  question: 'Question'
};

const MONTHS = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];

/** Details or a question longer than this many lines, or characters, start collapsed. */
const COLLAPSED_LINES = 6;
const COLLAPSED_CHARS = 480;

const NO_VALUES: readonly string[] = [];

export function gnollBenchConditionLabel(condition: string | null | undefined): string {
  return labelFrom(CONDITION_LABELS, condition);
}

/** `Live` for the live corpora; a scarce branch's own name otherwise. */
export function gnollBenchCorpusLabel(corpus: string | null | undefined): string {
  if (!corpus || corpus === 'live') return 'Live';
  return corpus;
}

/**
 * An Experimental Changes spec in words: `None`, or its changes joined by `, ` — each by its title when
 * `titles` (the catalog's, by id) has it, else by its id. A stored stamp's hash, after the first `:`, is
 * dropped, and a space stands for the `+` between ids.
 */
export function gnollBenchVariantLabel(value: string | null | undefined, titles?: ReadonlyMap<string, string> | null): string {
  if (!value || value === 'none') return 'None';
  const colon = value.indexOf(':');
  const spec = (colon >= 0 ? value.slice(0, colon) : value).trim();
  const ids = spec.split(/[+\s]+/).filter(id => id !== '');
  if (ids.length === 0) return 'None';
  return ids.map(id => titles?.get(id) || id).join(', ');
}

/** `21 Oct 2026`, local time; the raw value when it does not parse. */
export function gnollBenchDateText(utc: string | null | undefined): string {
  if (!utc) return '';
  const date = parseServerUtcDate(utc);
  if (isNaN(date.getTime())) return utc;
  return `${date.getDate()} ${MONTHS[date.getMonth()]} ${date.getFullYear()}`;
}

/** `14:02`, local time; empty when the value does not parse. */
export function gnollBenchTimeText(utc: string | null | undefined): string {
  if (!utc) return '';
  const date = parseServerUtcDate(utc);
  if (isNaN(date.getTime())) return '';
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${pad(date.getHours())}:${pad(date.getMinutes())}`;
}

/** `21 Oct 2026 14:02`, local time; the raw value when it does not parse. */
export function gnollBenchDateTimeText(utc: string | null | undefined): string {
  if (!utc) return '';
  const date = parseServerUtcDate(utc);
  if (isNaN(date.getTime())) return utc;
  return `${gnollBenchDateText(utc)} ${gnollBenchTimeText(utc)}`;
}

/** The part of a fingerprint safe in an element id. */
function idPart(fingerprint: string): string {
  return fingerprint.replace(/[^A-Za-z0-9]/g, '');
}

/** The filters the panel holds; the address and the remembered record carry the same. */
export interface GnollBenchFilterState {
  facets: Readonly<Record<GnollBenchFacetKey, readonly string[]>>;
  accepted: GnollBenchAcceptedScope;
  search: string;
}

/** Keeps the values a facet accepts: a known value, a whole-number id, or any non-blank text. */
function cleanValues(spec: GnollBenchFacetSpec, raw: readonly string[]): string[] {
  const values = raw.map(v => v.trim()).filter(v => v !== '');
  const kept = spec.known
    ? values.filter(v => spec.known!.includes(v))
    : spec.numeric ? values.filter(v => /^\d+$/.test(v)) : values;
  return [...new Set(kept)];
}

function emptyFacets(): Record<GnollBenchFacetKey, readonly string[]> {
  const facets = {} as Record<GnollBenchFacetKey, readonly string[]>;
  for (const spec of GNOLLBENCH_FACETS) facets[spec.key] = NO_VALUES;
  return facets;
}

export function defaultGnollBenchFilterState(): GnollBenchFilterState {
  return { facets: { ...emptyFacets(), status: [...DEFAULT_GNOLLBENCH_FEEDBACK_STATUSES] }, accepted: 'accepted', search: '' };
}

/** Whether an address names any of the panel's filters. */
export function hasGnollBenchQuery(query: ParamMap): boolean {
  return GNOLLBENCH_QUERY_PARAMS.some(name => query.has(name));
}

/** The filters an address names: an absent `status` is the default, `status=all` none; unknown values are dropped. */
export function gnollBenchStateFromQuery(query: ParamMap): GnollBenchFilterState {
  const facets = emptyFacets();
  for (const spec of GNOLLBENCH_FACETS) {
    const listed = readAdminQueryList(query, spec.key);
    if (spec.key === 'status') {
      facets.status = listed === null
        ? [...DEFAULT_GNOLLBENCH_FEEDBACK_STATUSES]
        : listed.includes(GNOLLBENCH_STATUS_QUERY_ALL) ? NO_VALUES : cleanValues(spec, listed);
    } else if (listed !== null) {
      facets[spec.key] = cleanValues(spec, listed);
    }
  }
  const accepted = query.get(GNOLLBENCH_ACCEPTED_QUERY) as GnollBenchAcceptedScope | null;
  return {
    facets,
    accepted: accepted && GNOLLBENCH_ACCEPTED_SCOPES.includes(accepted) ? accepted : 'accepted',
    search: (query.get(GNOLLBENCH_SEARCH_QUERY) ?? '').trim().slice(0, GNOLLBENCH_SEARCH_MAX)
  };
}

/** The filters a remembered record holds; a facet it does not name has no selection, Status its default. */
export function gnollBenchStateFromRecord(record: FeedbackViewRecord): GnollBenchFilterState {
  const facets = emptyFacets();
  for (const spec of GNOLLBENCH_FACETS) {
    const stored = record.filters[spec.key];
    if (spec.key === 'status' && stored === undefined) {
      facets.status = [...DEFAULT_GNOLLBENCH_FEEDBACK_STATUSES];
    } else if (stored !== undefined) {
      facets[spec.key] = cleanValues(spec, stored);
    }
  }
  const accepted = record.filters[GNOLLBENCH_ACCEPTED_QUERY]?.[0] as GnollBenchAcceptedScope | undefined;
  return {
    facets,
    accepted: accepted && GNOLLBENCH_ACCEPTED_SCOPES.includes(accepted) ? accepted : 'accepted',
    search: record.search.trim().slice(0, GNOLLBENCH_SEARCH_MAX)
  };
}

export function sameGnollBenchState(a: GnollBenchFilterState, b: GnollBenchFilterState): boolean {
  const set = (values: readonly string[]) => [...values].sort().join(',');
  return a.accepted === b.accepted && a.search === b.search &&
    GNOLLBENCH_FACETS.every(spec => set(a.facets[spec.key]) === set(b.facets[spec.key]));
}

/** The address's parameters for a state; null removes one. Defaults are left out. */
export function gnollBenchQueryParams(state: GnollBenchFilterState): Record<string, string | null> {
  const params: Record<string, string | null> = {};
  for (const spec of GNOLLBENCH_FACETS) {
    const values = state.facets[spec.key];
    if (spec.key === 'status') {
      const isDefault = values.length === DEFAULT_GNOLLBENCH_FEEDBACK_STATUSES.length &&
        DEFAULT_GNOLLBENCH_FEEDBACK_STATUSES.every(status => values.includes(status));
      const ordered = GNOLLBENCH_FEEDBACK_STATUSES.filter(status => values.includes(status));
      params[spec.key] = isDefault ? null : ordered.length === 0 ? GNOLLBENCH_STATUS_QUERY_ALL : adminQueryList(ordered);
    } else {
      params[spec.key] = adminQueryList(values);
    }
  }
  params[GNOLLBENCH_ACCEPTED_QUERY] = state.accepted === 'accepted' ? null : state.accepted;
  params[GNOLLBENCH_SEARCH_QUERY] = state.search || null;
  return params;
}

/** One facet as the filter bar renders it. */
interface FacetView {
  key: GnollBenchFacetKey;
  facetId: string;
  label: string;
  options: FilterFacetOption[];
  selected: readonly string[];
}

/** One removable chip: a facet value, the base set or the search. */
interface FilterChip {
  key: string;
  column: GnollBenchFacetKey | 'accepted' | 'search';
  value: string;
  facetLabel: string;
  valueLabel: string;
}

/** One issue's self-reports fetched so far; `all` while they are fetched without the filters. */
interface OccurrenceState {
  rows: GnollBenchFeedbackOccurrence[];
  total: number;
  loading: boolean;
  loaded: boolean;
  error: string | null;
  all: boolean;
}

/** The last Save of one issue. */
interface TriageResult {
  message: string;
  error: string | null;
}

/**
 * The Feedback tab's GnollBench sub-tab: the issues GnollBench candidates reported about themselves with
 * `report_feedback`, grouped by fingerprint, as a server-paged card list. Its filter bar has a search, Sort by,
 * the base set (accepted, all or refused calls), a facet for every run fact (status, resolved via, category, model,
 * provider, run, series, analysis group, battery run, model batch, suite, question, information condition,
 * corpus, Experimental Changes and harness) and chips; its selection bar holds a selection by fingerprint for
 * the export and the resolution import. Each card has the issue's figures, its self-reports on demand, a
 * one-issue YAML download and Edit triage, which opens the triage dialog. The shell supplies the date range and a Refresh token. The view
 * is remembered per browser and the filters are kept in the address. Report text is model output and is
 * rendered as plain text only.
 */
@Component({
  selector: 'app-feedback-gnollbench-panel',
  imports: [FilterFacetComponent, InfoTipComponent, RouterLink, FeedbackSelectionBarComponent, FeedbackTriageDialogComponent],
  templateUrl: './feedback-gnollbench-panel.component.html',
  styleUrl: './feedback-gnollbench-panel.component.scss'
})
export class FeedbackGnollBenchPanelComponent implements OnInit, OnChanges, OnDestroy {
  private readonly feedback = inject(AdminGnollBenchFeedbackService);
  private readonly transfer = inject(FeedbackTransferService);
  private readonly benchmark = inject(AdminBenchmarkService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly host: ElementRef<HTMLElement> = inject(ElementRef);
  private readonly cdr = inject(ChangeDetectorRef);

  /** The shell's date range for this sub-tab; a new object on every change. */
  @Input({ required: true }) range!: FeedbackRange;

  /** Incremented by the shell's Refresh; a change after the first value refetches everything. */
  @Input() refreshToken = 0;

  @ViewChild(FeedbackTriageDialogComponent, { static: true }) triageDialog!: FeedbackTriageDialogComponent;

  readonly sorts = GNOLLBENCH_SORTS;
  readonly acceptedScopes = GNOLLBENCH_ACCEPTED_SCOPES;
  readonly acceptedLabels = ACCEPTED_LABELS;
  readonly searchMax = GNOLLBENCH_SEARCH_MAX;
  readonly selectionCap = FEEDBACK_SELECTION_CAP;
  readonly statusLabel = feedbackStatusLabel;
  readonly categoryLabel = feedbackCategoryLabel;
  readonly conditionLabel = gnollBenchConditionLabel;
  readonly corpusLabel = gnollBenchCorpusLabel;
  /** An Experimental Changes spec in words, with the catalog's titles once it has arrived. */
  readonly variantLabel = (value: string | null | undefined): string => gnollBenchVariantLabel(value, this.changeTitles());
  readonly dateText = gnollBenchDateText;
  readonly timeText = gnollBenchTimeText;
  readonly dateTimeText = gnollBenchDateTimeText;
  readonly formatInteger = formatInteger;
  readonly idOf = idPart;

  // --- Filters ---

  /** Each facet's selection. */
  readonly filters = signal<Readonly<Record<GnollBenchFacetKey, readonly string[]>>>(defaultGnollBenchFilterState().facets);
  /** The base set of self-reports. */
  readonly accepted = signal<GnollBenchAcceptedScope>('accepted');
  /** The search as applied, once typing paused. */
  readonly searchTerm = signal('');
  readonly sortId = signal<string>('lastSeen');

  /** The facet options in the range, from the server; null until they arrive or when they fail. */
  readonly facetData = signal<GnollBenchFeedbackFacets | null>(null);
  /** Issue counts per status over all dates, for the Status facet. */
  readonly counts = signal<GnollBenchFeedbackCounts | null>(null);
  /** The experimental changes' titles by id; null until the catalog arrives, or when it fails. */
  readonly changeTitles = signal<ReadonlyMap<string, string> | null>(null);

  // --- Issues ---

  /** The issues fetched so far, in the server's order. */
  readonly rows = signal<GnollBenchFeedbackIssue[]>([]);
  readonly totalCount = signal(0);
  readonly loading = signal(false);
  /** Whether the first page of the current query has arrived. */
  readonly loaded = signal(false);
  readonly error = signal<string | null>(null);
  /** The issues in the date range under no filter, fetched only when the filters leave none. */
  readonly rangeTotal = signal<number | null>(null);
  /** Announced in the status line until the next fetch. */
  readonly notice = signal('');
  readonly occurrences = signal<Readonly<Record<string, OccurrenceState>>>({});
  /** The long texts shown in full, as `details-{id}` or `question-{id}`. */
  readonly expandedTexts = signal<ReadonlySet<string>>(new Set<string>());
  readonly savingFingerprint = signal<string | null>(null);
  /** The issue whose YAML download is running; another download waits until it ends. */
  readonly downloadingId = signal<string | null>(null);
  readonly triageResults = signal<Readonly<Record<string, TriageResult>>>({});

  // --- Selection ---

  /** The selected fingerprints, at most 500; remembered per browser. */
  readonly selection = signal<ReadonlySet<string>>(new Set<string>());
  /** Selected fingerprints known to match the current filters. */
  readonly matchedSelection = signal<ReadonlySet<string>>(new Set<string>());
  readonly showSelectedOnly = signal(false);
  /** The issues the filters match, whatever Show selected only does. */
  readonly matchingCount = signal(0);
  readonly exportOptions = signal<FeedbackExportOptions | undefined>(undefined);

  readonly selectedIds = computed<readonly string[]>(() => [...this.selection()]);

  /** Selected issues the current filters do not list. */
  readonly notShownCount = computed(() => {
    const matched = this.matchedSelection();
    let count = 0;
    for (const fingerprint of this.selection()) if (!matched.has(fingerprint)) count++;
    return count;
  });

  /** The facets the filter bar lists: Status always, any other with two options or more or a selection. */
  readonly facetViews = computed<FacetView[]>(() => {
    const data = this.facetData();
    const counts = this.counts();
    const filters = this.filters();
    const views: FacetView[] = [];
    for (const spec of GNOLLBENCH_FACETS) {
      const selected = filters[spec.key];
      const options = this.facetOptions(spec.key, data, counts);
      for (const value of selected) {
        if (!options.some(o => o.value === value)) options.push({ value, label: this.fallbackLabel(spec.key, value), count: 0 });
      }
      if (spec.key !== 'status' && options.length < 2 && selected.length === 0) continue;
      views.push({ key: spec.key, facetId: `tl-gbfb-facet-${spec.key}`, label: spec.label, options, selected });
    }
    return views;
  });

  /** One chip per selected facet value, one for a widened base set, then one for the search. */
  readonly chips = computed<FilterChip[]>(() => {
    const chips: FilterChip[] = [];
    for (const view of this.facetViews()) {
      for (const value of view.selected) {
        chips.push({
          key: `${view.key}:${value}`, column: view.key, value, facetLabel: view.label,
          valueLabel: view.options.find(o => o.value === value)?.label ?? value
        });
      }
    }
    const accepted = this.accepted();
    if (accepted !== 'accepted') {
      chips.push({ key: 'accepted', column: 'accepted', value: accepted, facetLabel: 'Self-reports', valueLabel: ACCEPTED_LABELS[accepted] });
    }
    const search = this.searchTerm();
    if (search) chips.push({ key: 'search', column: 'search', value: search, facetLabel: 'Search', valueLabel: `“${search}”` });
    return chips;
  });

  /** Whether a filter other than the status, Resolved via or the base set narrows each issue's self-reports. */
  readonly hasOccurrenceFilters = computed(() => {
    const filters = this.filters();
    return this.searchTerm() !== '' || GNOLLBENCH_FACETS.some(spec => !spec.issueLevel && filters[spec.key].length > 0);
  });

  /** The server filters, sorts and pages; the table carries the search and the sort. */
  readonly table = new TableState<GnollBenchFeedbackIssue>('lastSeen', 'desc').registerAccessors(
    {
      lastSeen: i => parseServerUtcDate(i.lastSeenUtc).getTime(),
      firstSeen: i => parseServerUtcDate(i.firstSeenUtc).getTime(),
      count: i => i.selfReportCount,
      runs: i => i.runCount,
      models: i => i.modelCount,
      subject: i => i.subject
    },
    { search: customFilter<GnollBenchFeedbackIssue>(() => true) }
  );

  /** The debounced search and the remembered Sort by; every change refetches the first page. */
  readonly list = new CardListState<GnollBenchFeedbackIssue>(this.table, {
    idPrefix: 'tl-gbfb',
    batch: GNOLLBENCH_FEEDBACK_BATCH,
    sorts: GNOLLBENCH_SORTS,
    defaultSort: 'lastSeen',
    storageKey: GNOLLBENCH_FEEDBACK_VIEW_STORAGE_KEY,
    facets: [],
    onChange: () => this.onSearchApplied()
  });

  private listSub: Subscription | null = null;
  private probeSub: Subscription | null = null;
  private facetsSub: Subscription | null = null;
  private countsSub: Subscription | null = null;
  private querySub: Subscription | null = null;
  private selectionSub: Subscription | null = null;
  private matchingSub: Subscription | null = null;
  private pruneSub: Subscription | null = null;
  private selectAllSub: Subscription | null = null;
  private readonly occurrenceSubs = new Map<string, Subscription>();
  private changesSub: Subscription | null = null;
  private downloadSub: Subscription | null = null;
  private initialized = false;

  ngOnInit(): void {
    ensureOverlayPolyfills();
    // The Experimental Changes labels use the catalog's titles once it arrives, and the ids until then.
    this.changesSub = this.benchmark.getExperimentalChanges().subscribe({
      next: catalog => this.changeTitles.set(new Map((catalog?.changes ?? []).map(c => [c.id, c.title] as const))),
      error: () => this.changeTitles.set(null)
    });
    this.sortId.set(this.list.sortId);
    this.exportOptions.set(loadFeedbackView('gnollbench')?.exportOptions);
    this.selection.set(new Set(loadGnollBenchSelection()));
    this.table.setRemoteTotal(0);
    // Emits the current address at once, so the first fetch already carries its filters.
    this.querySub = this.route.queryParamMap.subscribe(query => this.applyAddress(query));
    this.initialized = true;
    this.loadFacets();
    this.loadCounts();
    this.reloadIssues();
  }

  ngOnChanges(changes: SimpleChanges): void {
    if (!this.initialized) return;
    const range = changes['range'];
    const refresh = changes['refreshToken'];
    if (refresh && !refresh.firstChange) {
      if (range && !range.firstChange) this.persist();
      this.reload();
      return;
    }
    if (range && !range.firstChange) {
      this.persist();
      this.loadFacets();
      this.reloadIssues();
    }
  }

  ngOnDestroy(): void {
    this.list.dispose();
    for (const sub of [this.listSub, this.probeSub, this.facetsSub, this.countsSub, this.querySub, this.selectionSub,
      this.matchingSub, this.pruneSub, this.selectAllSub, this.changesSub, this.downloadSub]) {
      sub?.unsubscribe();
    }
    this.occurrenceSubs.forEach(sub => sub.unsubscribe());
  }

  /** Refetches the facets, the counts and the first page; open self-report lists start again and the selection is checked. */
  reload(): void {
    this.triageResults.set({});
    this.resetOccurrences();
    this.loadFacets();
    this.loadCounts();
    this.reloadIssues();
    if (this.showSelectedOnly()) this.pruneSelection();
  }

  // --- Filters ---

  get hasFilters(): boolean {
    return this.chips().length > 0 || this.showSelectedOnly();
  }

  onSearchInput(event: Event): void {
    this.list.setSearchInput((event.target as HTMLInputElement).value.slice(0, GNOLLBENCH_SEARCH_MAX));
  }

  /** Escape with text clears the search at once and refetches; with none, Escape is left alone. */
  onSearchKeydown(event: KeyboardEvent): void {
    if (this.list.clearSearchOnEscape(event)) this.onSearchApplied();
  }

  onSortChange(event: Event): void {
    if (!this.list.setSort((event.target as HTMLSelectElement).value)) return;
    this.sortId.set(this.list.sortId);
    this.persist();
    this.reloadIssues();
  }

  onAcceptedChange(event: Event): void {
    const value = (event.target as HTMLSelectElement).value as GnollBenchAcceptedScope;
    if (!GNOLLBENCH_ACCEPTED_SCOPES.includes(value) || value === this.accepted()) return;
    this.accepted.set(value);
    this.list.resetBatch();
    this.loadFacets();
    this.onFiltersChanged();
  }

  onFacetChange(key: GnollBenchFacetKey, values: string[]): void {
    this.filters.set({ ...this.filters(), [key]: values });
    this.list.resetBatch();
    this.onFiltersChanged();
  }

  /** Removes a chip's filter and refetches, then focuses the chip now in its place, else the previous one, else the search. */
  removeChip(chip: FilterChip): void {
    const index = this.chips().findIndex(c => c.key === chip.key);
    if (chip.column === 'search') {
      this.applySearch('');
    } else if (chip.column === 'accepted') {
      this.accepted.set('accepted');
      this.loadFacets();
    } else {
      this.filters.set({ ...this.filters(), [chip.column]: this.filters()[chip.column].filter(v => v !== chip.value) });
    }
    this.list.resetBatch();
    this.onFiltersChanged();
    this.cdr.detectChanges();
    const chips = Array.from(this.host.nativeElement.querySelectorAll<HTMLButtonElement>('.tl-gbfb-chips .gh-filter-chip'));
    const target = index >= 0 ? chips[index] ?? chips[index - 1] : undefined;
    (target ?? this.element('#tl-gbfb-search'))?.focus();
  }

  /** Clears the search, the base set and every facet, the Status default included, refetches, then focuses the search. */
  clearFilters(alsoSelectedOnly = false): void {
    const acceptedChanged = this.accepted() !== 'accepted';
    this.filters.set(emptyFacets());
    this.accepted.set('accepted');
    this.applySearch('');
    if (alsoSelectedOnly) this.showSelectedOnly.set(false);
    if (acceptedChanged) this.loadFacets();
    this.list.resetBatch();
    this.onFiltersChanged();
    this.cdr.detectChanges();
    this.element('#tl-gbfb-search')?.focus();
  }

  /** The debounced search applied, or Escape cleared it. */
  private onSearchApplied(): void {
    this.searchTerm.set((this.table.filters['search'] ?? '').trim());
    this.onFiltersChanged();
  }

  /** A filter changed here: remembered, written to the address and refetched. */
  private onFiltersChanged(): void {
    this.persist();
    this.writeQuery();
    this.reloadIssues();
    this.cdr.markForCheck();
  }

  private applySearch(text: string): void {
    this.list.searchText = text;
    this.table.setFilter('search', text);
    this.searchTerm.set(text);
    const field = this.element('#tl-gbfb-search') as HTMLInputElement | null;
    if (field) field.value = text;
  }

  private facetOptions(key: GnollBenchFacetKey, data: GnollBenchFeedbackFacets | null, counts: GnollBenchFeedbackCounts | null): FilterFacetOption[] {
    const values = (list: GnollBenchFeedbackFacetValue[] | undefined, labelOf: (v: GnollBenchFeedbackFacetValue) => string) =>
      (list ?? []).map(v => ({ value: v.value, label: labelOf(v), count: v.count }));
    const entities = (list: GnollBenchFeedbackFacetEntity[] | undefined) =>
      (list ?? []).map(e => ({ value: String(e.id), label: e.name, count: e.count }));
    switch (key) {
      case 'status':
        return GNOLLBENCH_FEEDBACK_STATUSES.map(status => ({ value: status, label: feedbackStatusLabel(status), count: counts?.[status] ?? 0 }));
      case 'resolvedVia': return values(data?.resolvedVia, v => resolvedViaFilterLabel(v.value));
      case 'category': return values(data?.categories, v => feedbackCategoryLabel(v.value));
      case 'model': return values(data?.models, v => v.label || v.value);
      case 'provider': return values(data?.providers, v => providerLabel(v.value) || v.label || v.value);
      case 'run': return entities(data?.runs);
      case 'series': return entities(data?.series);
      case 'group': return entities(data?.groups);
      case 'battery': return entities(data?.batteries);
      case 'batch': return entities(data?.batches);
      case 'suite': return entities(data?.suites);
      case 'question': return entities(data?.questions);
      case 'condition': return values(data?.conditions, v => gnollBenchConditionLabel(v.value) || v.label);
      case 'corpus': return values(data?.corpora, v => gnollBenchCorpusLabel(v.value));
      case 'variant': return values(data?.variants, v => gnollBenchVariantLabel(v.value, this.changeTitles()));
      case 'harness': return values(data?.harnesses, v => `Harness ${v.value}`);
    }
  }

  /** What a selected value reads as while the facets do not list it. */
  private fallbackLabel(key: GnollBenchFacetKey, value: string): string {
    const idLabel = ID_FALLBACK_LABELS[key];
    if (idLabel) return `${idLabel} #${value}`;
    switch (key) {
      case 'status': return feedbackStatusLabel(value);
      case 'resolvedVia': return resolvedViaFilterLabel(value);
      case 'category': return feedbackCategoryLabel(value);
      case 'condition': return gnollBenchConditionLabel(value);
      case 'provider': return providerLabel(value) || value;
      case 'corpus': return gnollBenchCorpusLabel(value);
      case 'variant': return gnollBenchVariantLabel(value, this.changeTitles());
      case 'harness': return `Harness ${value}`;
      default: return value;
    }
  }

  // --- The address and the remembered view ---

  private filterState(): GnollBenchFilterState {
    return { facets: this.filters(), accepted: this.accepted(), search: this.searchTerm() };
  }

  /**
   * An address naming any of the panel's filters is applied and remembered; one naming none takes the
   * remembered view, else the defaults, and writes it into the address. After the first load, a change refetches.
   */
  private applyAddress(query: ParamMap): void {
    if (!hasGnollBenchQuery(query)) {
      if (!this.initialized) {
        const record = loadFeedbackView('gnollbench');
        this.applyState(record ? gnollBenchStateFromRecord(record) : defaultGnollBenchFilterState());
      }
      if (Object.values(gnollBenchQueryParams(this.filterState())).some(v => v !== null)) this.writeQuery();
      return;
    }
    const next = gnollBenchStateFromQuery(query);
    if (this.initialized && sameGnollBenchState(next, this.filterState())) return;
    const acceptedChanged = next.accepted !== this.accepted();
    this.applyState(next);
    this.persist();
    if (this.initialized) {
      if (acceptedChanged) this.loadFacets();
      this.reloadIssues();
      this.cdr.markForCheck();
    }
  }

  private applyState(state: GnollBenchFilterState): void {
    this.filters.set(state.facets);
    this.accepted.set(state.accepted);
    this.applySearch(state.search);
    this.list.resetBatch();
    this.list.invalidate();
  }

  /** Writes the filters into the address, replacing the history entry and keeping every other parameter. */
  private writeQuery(): void {
    // A navigation the router fails is reported by its own NavigationError event.
    this.router.navigate([], {
      queryParams: gnollBenchQueryParams(this.filterState()),
      queryParamsHandling: 'merge',
      replaceUrl: true
    }).catch(() => false);
  }

  /** Remembers the whole view: Sort by, the facets, the base set, the search, the range and the export options. */
  private persist(): void {
    const filters: Record<string, string[]> = {};
    const current = this.filters();
    for (const spec of GNOLLBENCH_FACETS) filters[spec.key] = [...current[spec.key]];
    filters[GNOLLBENCH_ACCEPTED_QUERY] = this.accepted() === 'accepted' ? [] : [this.accepted()];
    const record: FeedbackViewRecord = {
      version: 1,
      sort: this.list.sortId,
      filters,
      search: this.searchTerm(),
      range: { ...(this.range ?? defaultFeedbackRange()) }
    };
    const exportOptions = this.exportOptions();
    if (exportOptions) record.exportOptions = exportOptions;
    saveFeedbackView('gnollbench', record);
  }

  // --- Fetching ---

  get remainingCount(): number {
    return Math.max(0, this.totalCount() - this.rows().length);
  }

  get nextBatchCount(): number {
    return Math.min(this.list.batch, this.remainingCount);
  }

  /** `Showing 25 of 40 issues`, `One issue`, or empty with none; a notice is announced before it. */
  get listStatus(): string {
    if (this.loading() && !this.loaded()) return 'Loading GnollBench feedback issues…';
    const notice = this.notice();
    if (this.error()) return notice;
    const total = this.totalCount();
    const base = total === 0 ? '' : total === 1 && this.rows().length === 1
      ? 'One issue'
      : `Showing ${formatInteger(this.rows().length)} of ${formatInteger(total)} issues`;
    return notice ? `${notice} ${base}`.trim() : base;
  }

  /** Which empty state applies once the first page is empty; null while the range total is still being counted. */
  get emptyKind(): 'range' | 'filters' | null {
    if (!this.hasFilters) return 'range';
    const total = this.rangeTotal();
    if (total === null) return null;
    return total === 0 ? 'range' : 'filters';
  }

  /** The filters as the server takes them; with `withSelection`, Show selected only's fingerprints too. */
  private currentFilters(withSelection = true): GnollBenchFeedbackFilters {
    const filters: GnollBenchFeedbackFilters = {
      startDate: this.range?.start || undefined,
      endDate: this.range?.end || undefined,
      accepted: this.accepted(),
      search: this.searchTerm() || undefined,
      sort: this.list.sortId as GnollBenchFeedbackSort
    };
    const current = this.filters();
    for (const spec of GNOLLBENCH_FACETS) {
      const values = current[spec.key];
      if (values.length > 0) (filters as Record<string, unknown>)[spec.field] = [...values];
    }
    if (withSelection && this.showSelectedOnly()) filters.fingerprints = [...this.selection()];
    return filters;
  }

  /** Fetches the first page of the current query, replacing the list. */
  reloadIssues(): void {
    this.fetch(false);
  }

  /** Appends the next page and focuses the title of the first new card. */
  showMore(): void {
    if (this.loading() || this.remainingCount === 0) return;
    this.fetch(true);
  }

  private fetch(append: boolean): void {
    if (!append) {
      this.loaded.set(false);
      this.rangeTotal.set(null);
      this.probeSub?.unsubscribe();
    }
    this.loading.set(true);
    this.error.set(null);
    this.notice.set('');
    this.listSub?.unsubscribe();
    this.listSub = this.feedback.getIssues({
      ...this.currentFilters(),
      skip: append ? this.rows().length : 0,
      take: GNOLLBENCH_FEEDBACK_BATCH
    }).subscribe({
      next: page => {
        const firstNew = append ? this.rows().length : -1;
        if (append) {
          const known = new Set(this.rows().map(i => i.fingerprint));
          this.rows.set([...this.rows(), ...page.rows.filter(i => !known.has(i.fingerprint))]);
        } else {
          this.rows.set(page.rows);
        }
        this.totalCount.set(page.totalCount);
        this.table.setRemoteTotal(page.totalCount);
        this.loading.set(false);
        this.loaded.set(true);
        this.list.invalidate();
        if (!append) {
          if (page.rows.length === 0 && this.hasFilters) this.countRange();
          this.refreshSelectionFacts();
        } else {
          this.markShownAsMatched(page.rows);
        }
        const focusId = firstNew >= 0 ? this.rows()[firstNew]?.fingerprint : undefined;
        if (focusId !== undefined) {
          this.focusAfterRender(`#tl-gbfb-${idPart(focusId)}-title`);
        } else {
          this.cdr.markForCheck();
        }
      },
      error: err => {
        this.loading.set(false);
        this.loaded.set(true);
        this.error.set('The GnollBench feedback issues could not be loaded: ' + adminApiError(err).message);
      }
    });
  }

  /** Counts the issues in the range under no filter, to tell the two empty states apart. */
  private countRange(): void {
    this.probeSub?.unsubscribe();
    this.probeSub = this.feedback.getIssues({
      startDate: this.range?.start || undefined,
      endDate: this.range?.end || undefined,
      take: 1
    }).subscribe({
      next: page => this.rangeTotal.set(page.totalCount),
      // Unknown: the filters are the likelier cause, and Clear filters is the useful offer.
      error: () => this.rangeTotal.set(1)
    });
  }

  private loadFacets(): void {
    this.facetsSub?.unsubscribe();
    this.facetsSub = this.feedback.getFacets({
      startDate: this.range?.start || undefined,
      endDate: this.range?.end || undefined,
      accepted: this.accepted()
    }).subscribe({
      next: facets => this.facetData.set(facets),
      // The facets other than Status are left out; the list itself still works.
      error: () => this.facetData.set(null)
    });
  }

  private loadCounts(): void {
    this.countsSub?.unsubscribe();
    this.countsSub = this.feedback.getCounts().subscribe({
      next: counts => this.counts.set(counts),
      error: () => this.counts.set(null)
    });
  }

  // --- Selection ---

  isSelected(fingerprint: string): boolean {
    return this.selection().has(fingerprint);
  }

  /** Selects or unselects a card's issue; a 501st is refused and announced. */
  onSelectChange(issue: GnollBenchFeedbackIssue, event: Event): void {
    const box = event.target as HTMLInputElement;
    const next = new Set(this.selection());
    if (box.checked) {
      if (next.size >= FEEDBACK_SELECTION_CAP) {
        box.checked = false;
        this.notice.set(`The selection holds at most ${formatInteger(FEEDBACK_SELECTION_CAP)} issues.`);
        return;
      }
      next.add(issue.fingerprint);
      this.matchedSelection.set(new Set([...this.matchedSelection(), issue.fingerprint]));
    } else {
      next.delete(issue.fingerprint);
    }
    this.notice.set('');
    this.setSelection(next);
  }

  onShowSelectedOnlyChange(value: boolean): void {
    if (value === this.showSelectedOnly() || (value && this.selection().size === 0)) return;
    this.showSelectedOnly.set(value);
    this.list.resetBatch();
    this.reloadIssues();
    if (value) this.pruneSelection();
  }

  onClearSelection(): void {
    this.setSelection(new Set<string>());
    this.notice.set('Cleared the selection.');
  }

  /** Adds every issue the filters match, the selection first, up to 500, and says when the cap cut the list. */
  onSelectAllMatching(): void {
    this.selectAllSub?.unsubscribe();
    this.selectAllSub = this.feedback.getIssueIds(this.currentFilters(false)).subscribe({
      next: ids => {
        const next = new Set(this.selection());
        for (const fingerprint of ids.fingerprints) {
          if (next.size >= FEEDBACK_SELECTION_CAP) break;
          next.add(fingerprint);
        }
        const every = !ids.truncated && ids.fingerprints.every(f => next.has(f));
        this.matchedSelection.set(new Set([...this.matchedSelection(), ...ids.fingerprints.filter(f => next.has(f))]));
        this.setSelection(next);
        this.notice.set(every
          ? `Selected all ${formatInteger(ids.totalCount)} matching issues.`
          : `Selected the first ${formatInteger(FEEDBACK_SELECTION_CAP)} of ${formatInteger(ids.totalCount)}.`);
      },
      error: err => this.notice.set('The matching issues could not be selected: ' + adminApiError(err).message)
    });
  }

  /** An export finished: issues that no longer exist leave the selection, and the options are remembered. */
  onExported(event: { missing: readonly (string | number)[]; exportOptions: FeedbackExportOptions }): void {
    this.exportOptions.set(event.exportOptions);
    const missing = new Set(event.missing.map(String));
    if (missing.size > 0) {
      this.setSelection(new Set([...this.selection()].filter(f => !missing.has(f))));
    }
    this.persist();
  }

  /** Resolutions were imported: the list, the facets and the counts are fetched again. */
  onImported(): void {
    this.reload();
  }

  /** Holds and remembers a selection; an empty one ends Show selected only and refetches the list. */
  private setSelection(next: ReadonlySet<string>): void {
    this.selection.set(next);
    saveGnollBenchSelection([...next]);
    if (next.size === 0 && this.showSelectedOnly()) {
      this.showSelectedOnly.set(false);
      this.list.resetBatch();
      this.reloadIssues();
    }
    this.cdr.markForCheck();
  }

  private markShownAsMatched(rows: readonly GnollBenchFeedbackIssue[]): void {
    const selection = this.selection();
    const shown = rows.map(r => r.fingerprint).filter(f => selection.has(f));
    if (shown.length > 0) this.matchedSelection.set(new Set([...this.matchedSelection(), ...shown]));
  }

  /**
   * After a first page: which selected issues the filters match (shown issues match; the server says for the
   * rest), and, under Show selected only, how many issues the filters match without it.
   */
  private refreshSelectionFacts(): void {
    this.selectionSub?.unsubscribe();
    this.matchingSub?.unsubscribe();
    const selection = this.selection();
    const shown = this.rows().map(r => r.fingerprint).filter(f => selection.has(f));
    this.matchedSelection.set(new Set(shown));
    if (!this.showSelectedOnly()) this.matchingCount.set(this.totalCount());
    if (selection.size === 0) return;
    this.selectionSub = this.feedback.getIssueIds({ ...this.currentFilters(false), fingerprints: [...selection] }).subscribe({
      next: ids => this.matchedSelection.set(new Set([...shown, ...ids.fingerprints])),
      // Unknown: the shown issues are the ones known to match.
      error: () => undefined
    });
    if (this.showSelectedOnly()) {
      this.matchingSub = this.feedback.getIssueIds(this.currentFilters(false)).subscribe({
        next: ids => this.matchingCount.set(ids.totalCount),
        error: () => undefined
      });
    }
  }

  /** Drops selected fingerprints the server no longer has under any filter, and says so. */
  private pruneSelection(): void {
    const selected = [...this.selection()];
    if (selected.length === 0) return;
    this.pruneSub?.unsubscribe();
    this.pruneSub = this.feedback.getIssueIds({ accepted: 'all', fingerprints: selected }).subscribe({
      next: ids => {
        const existing = new Set(ids.fingerprints);
        const kept = selected.filter(f => existing.has(f));
        const dropped = selected.length - kept.length;
        if (dropped === 0) return;
        this.setSelection(new Set(kept));
        this.notice.set(dropped === 1
          ? 'One selected issue no longer exists and left the selection.'
          : `${formatInteger(dropped)} selected issues no longer exist and left the selection.`);
      },
      // The selection is kept as it is.
      error: () => undefined
    });
  }

  // --- A card ---

  statusBadgeClass(status: GnollBenchFeedbackStatus): string {
    switch (status) {
      case 'regressed': return 'status-badge badge-warning';
      case 'resolved': return 'status-badge badge-success';
      default: return `status-badge tl-gbfb-status-${status}`;
    }
  }

  /** `in 3 runs`; `in 1 run`. */
  runsText(issue: GnollBenchFeedbackIssue): string {
    return issue.runCount === 1 ? 'in 1 run' : `in ${formatInteger(issue.runCount)} runs`;
  }

  /** The invisible characters in an issue's subject and summary, as `U+XXXX`. */
  issueHiddenCharacters(issue: GnollBenchFeedbackIssue): string[] {
    return hiddenCharactersOf(issue.subject, issue.summary);
  }

  /** The invisible characters in the texts a self-report card shows, as `U+XXXX`. */
  occurrenceHiddenCharacters(occurrence: GnollBenchFeedbackOccurrence): string[] {
    return hiddenCharactersOf(occurrence.subject, occurrence.summary, occurrence.details, occurrence.searchedFor,
      occurrence.expectedLocation, occurrence.suspectedCauseNote, occurrence.questionText, occurrence.error, ...occurrence.relatedTools);
  }

  /** `Triaged 21 Oct 2026 14:02 by tommi`; empty for an issue nobody has triaged. */
  triagedText(issue: GnollBenchFeedbackIssue): string {
    if (!issue.triagedUtc) return '';
    const by = issue.triagedByUserName ? ` by ${issue.triagedByUserName}` : '';
    const resolved = issue.status === 'resolved' || issue.status === 'regressed'
      ? issue.resolvedUtc ? ` · resolved ${gnollBenchDateTimeText(issue.resolvedUtc)}` : ''
      : '';
    return `Triaged ${gnollBenchDateTimeText(issue.triagedUtc)}${by}${resolved}`;
  }

  /** The Overseer sub-tab's address for the chat issue with the same fingerprint, under every status. */
  overseerQuery(issue: GnollBenchFeedbackIssue): Record<string, string> {
    return { ids: String(issue.alsoInOverseer?.issueId ?? ''), status: GNOLLBENCH_STATUS_QUERY_ALL };
  }

  // --- Self-reports ---

  occurrenceState(fingerprint: string): OccurrenceState | undefined {
    return this.occurrences()[fingerprint];
  }

  /** The first open fetches the first page; later opens show what was fetched. */
  onOccurrencesToggle(issue: GnollBenchFeedbackIssue, event: Event): void {
    if (!(event.target as HTMLDetailsElement).open) return;
    const state = this.occurrenceState(issue.fingerprint);
    if (state && (state.loaded || state.loading)) return;
    this.fetchOccurrences(issue.fingerprint, false, false);
  }

  showMoreOccurrences(fingerprint: string): void {
    const state = this.occurrenceState(fingerprint);
    if (!state || state.loading || state.rows.length >= state.total) return;
    this.fetchOccurrences(fingerprint, true, state.all);
  }

  /** Shows the self-reports outside the filters too, or only those inside them again. */
  toggleAllOccurrences(fingerprint: string): void {
    const state = this.occurrenceState(fingerprint);
    if (state?.loading) return;
    this.fetchOccurrences(fingerprint, false, !(state?.all ?? false));
  }

  remainingOccurrences(fingerprint: string): number {
    const state = this.occurrenceState(fingerprint);
    return state ? Math.max(0, state.total - state.rows.length) : 0;
  }

  nextOccurrenceBatch(fingerprint: string): number {
    return Math.min(GNOLLBENCH_OCCURRENCE_BATCH, this.remainingOccurrences(fingerprint));
  }

  private fetchOccurrences(fingerprint: string, append: boolean, all: boolean): void {
    const previous = this.occurrenceState(fingerprint);
    const rows = append && previous ? previous.rows : [];
    this.setOccurrenceState(fingerprint, {
      rows, total: previous?.total ?? 0, loading: true, loaded: previous?.loaded ?? false, error: null, all
    });
    this.occurrenceSubs.get(fingerprint)?.unsubscribe();
    const filters = this.currentFilters(false);
    const sub = this.feedback.getOccurrences(fingerprint, {
      filters, all, skip: rows.length, take: GNOLLBENCH_OCCURRENCE_BATCH
    }).subscribe({
      next: page => {
        const known = new Set(rows.map(o => o.id));
        const merged = [...rows, ...page.rows.filter(o => !known.has(o.id))];
        this.setOccurrenceState(fingerprint, { rows: merged, total: page.totalCount, loading: false, loaded: true, error: null, all });
        const firstNew = append ? merged[rows.length] : undefined;
        if (firstNew) {
          this.focusAfterRender(`#tl-gbfb-occ-${firstNew.id}-time`);
        } else {
          this.cdr.markForCheck();
        }
      },
      error: err => {
        this.setOccurrenceState(fingerprint, {
          rows, total: previous?.total ?? 0, loading: false, loaded: rows.length > 0, all,
          error: 'The self-reports could not be loaded: ' + adminApiError(err).message
        });
      }
    });
    this.occurrenceSubs.set(fingerprint, sub);
  }

  private setOccurrenceState(fingerprint: string, state: OccurrenceState): void {
    this.occurrences.set({ ...this.occurrences(), [fingerprint]: state });
  }

  /** Forgets every fetched self-report list, then fetches the first page again for each disclosure still open. */
  private resetOccurrences(): void {
    this.occurrenceSubs.forEach(sub => sub.unsubscribe());
    this.occurrenceSubs.clear();
    this.occurrences.set({});
    this.expandedTexts.set(new Set<string>());
    const open = Array.from(this.host.nativeElement.querySelectorAll<HTMLDetailsElement>('details.tl-gbfb-occurrences[open]'));
    for (const details of open) {
      const fingerprint = details.dataset['fingerprint'];
      if (fingerprint) this.fetchOccurrences(fingerprint, false, false);
    }
  }

  /** `Claude Opus · Anthropic`; whichever is known. */
  modelText(occurrence: GnollBenchFeedbackOccurrence): string {
    return [occurrence.modelLabel || occurrence.modelId, providerLabel(occurrence.provider)].filter(part => !!part).join(' · ');
  }

  /** The issue's providers by name (`OpenAI`); empty for none. */
  providersText(issue: GnollBenchFeedbackIssue): string {
    return issue.providers.map(provider => providerLabel(provider) || provider).join(', ');
  }

  /** `GnollHack wiki search, Item stats`; empty for none. */
  toolsText(tools: readonly string[]): string {
    return tools.map(tool => toolLabel(tool) || tool).join(', ');
  }

  /** `GnollHack wiki, Overseer release`; empty while the issue says nothing about where the fix went. */
  resolvedViaText(issue: GnollBenchFeedbackIssue): string {
    return issue.resolvedVia?.length ? formatResolvedVia(issue.resolvedVia) : '';
  }

  causeLabel(cause: string | null | undefined): string {
    return feedbackCauseLabel(cause);
  }

  noticedViaText(value: string | null): string {
    return feedbackNoticedViaLabel(value);
  }

  /** The run's report, which the server serves as a download. */
  runReportUrl(runId: number): string {
    return this.benchmark.getRunReportUrl(runId);
  }

  /** Whether a text is long enough to start collapsed. */
  isLongText(text: string | null | undefined): boolean {
    if (!text) return false;
    return text.split('\n').length > COLLAPSED_LINES || text.length > COLLAPSED_CHARS;
  }

  isExpanded(key: string): boolean {
    return this.expandedTexts().has(key);
  }

  toggleText(key: string): void {
    const next = new Set(this.expandedTexts());
    if (next.has(key)) next.delete(key);
    else next.add(key);
    this.expandedTexts.set(next);
  }

  // --- Download ---

  /**
   * Downloads one issue as a YAML issues file with the remembered export options, and says so on the card's
   * result line. An issue the server no longer has is said so, and the list is fetched again. Ignored while
   * a download runs.
   */
  downloadIssue(issue: GnollBenchFeedbackIssue): void {
    if (this.downloadingId() !== null) return;
    const fingerprint = issue.fingerprint;
    const options = this.exportOptions() ?? DEFAULT_FEEDBACK_EXPORT_OPTIONS;
    this.downloadingId.set(fingerprint);
    this.setTriageResult(fingerprint, { message: '', error: null });
    this.downloadSub?.unsubscribe();
    this.downloadSub = this.transfer.exportIssues({
      source: 'gnollbench', format: 'yaml', fingerprints: [fingerprint],
      occurrencesPerIssue: options.occurrencesPerIssue, includeQuestionText: options.includeQuestionText !== false
    }).subscribe({
      next: result => {
        this.downloadingId.set(null);
        downloadTextFile(result.fileName, result.content, result.mediaType);
        this.setTriageResult(fingerprint, { message: `Downloaded ${result.fileName}.`, error: null });
        this.cdr.markForCheck();
      },
      error: err => {
        this.downloadingId.set(null);
        if (feedbackTransferStatus(err) === 404) {
          this.setTriageResult(fingerprint, { message: 'This issue no longer exists.', error: null });
          this.reloadIssues();
          this.notice.set(`The issue “${issue.subject}” no longer exists.`);
        } else {
          this.setTriageResult(fingerprint, { message: 'The issue could not be downloaded: ' + adminApiError(err).message, error: null });
        }
        this.cdr.markForCheck();
      }
    });
  }

  // --- Triage ---

  triageResult(fingerprint: string): TriageResult | undefined {
    return this.triageResults()[fingerprint];
  }

  /** Opens the triage dialog on the issue's stored triage; it saves through `saveTriage`. */
  editTriage(issue: GnollBenchFeedbackIssue): void {
    if (this.savingFingerprint() !== null) return;
    this.triageDialog.open({
      idPrefix: `tl-gbfb-${idPart(issue.fingerprint)}-triage`,
      subject: issue.subject,
      details: [
        feedbackCategoryLabel(issue.category),
        `Status: ${feedbackStatusLabel(issue.status)}`,
        issue.selfReportCount === 1 ? '1 self-report' : `${formatInteger(issue.selfReportCount)} self-reports`
      ],
      values: FeedbackGnollBenchPanelComponent.triageValuesOf(issue),
      statuses: GNOLLBENCH_FEEDBACK_TRIAGE_STATUSES.map(status => ({ value: status, label: feedbackStatusLabel(status) })),
      statusHelp: 'A resolved issue that a run reports again becomes Regressed; a dismissed one stays dismissed.',
      limits: { note: GNOLLBENCH_NOTE_MAX, resolution: GNOLLBENCH_RESOLUTION_MAX, fixedInVersion: GNOLLBENCH_FIXED_IN_VERSION_MAX },
      fixedInVersionPattern: GNOLLBENCH_FIXED_IN_VERSION_PATTERN,
      fixedInVersionError: 'Fixed in version takes letters, digits, dots, plus and minus signs only, up to 64 characters.',
      submit: values => this.saveTriage(issue, values)
    });
  }

  /**
   * Saves the status, each text that differs from the stored one, and Resolved via when the dialog changed it:
   * an unchanged text is left out, which keeps it, and an emptied one is sent empty, which clears it. The card
   * takes the server's copy, the result is announced and the Status counts are fetched again. A failure reaches
   * the dialog as an error with the message to show.
   */
  saveTriage(issue: GnollBenchFeedbackIssue, values: FeedbackTriageSubmission): Observable<GnollBenchFeedbackIssue> {
    return defer(() => {
      const fingerprint = issue.fingerprint;
      const update: GnollBenchFeedbackStatusUpdate = { status: values.status as GnollBenchFeedbackTriageStatus };
      if (values.note !== (issue.triageNote ?? '')) update.note = values.note;
      if (values.resolution !== (issue.resolution ?? '')) update.resolution = values.resolution;
      if (values.fixedInVersion !== (issue.fixedInVersion ?? '')) update.fixedInVersion = values.fixedInVersion;
      if (values.resolvedVia !== undefined) update.resolvedVia = [...values.resolvedVia];

      this.savingFingerprint.set(fingerprint);
      this.setTriageResult(fingerprint, { message: '', error: null });
      return this.feedback.updateStatus(fingerprint, update).pipe(
        map((updated): GnollBenchFeedbackIssue => updated && updated.fingerprint === fingerprint
          ? updated
          : {
            ...issue, status: update.status,
            triageNote: update.note !== undefined ? update.note || null : issue.triageNote,
            resolution: update.resolution !== undefined ? update.resolution || null : issue.resolution,
            fixedInVersion: update.fixedInVersion !== undefined ? update.fixedInVersion || null : issue.fixedInVersion,
            resolvedVia: update.resolvedVia ? [...update.resolvedVia] : issue.resolvedVia,
            triagedUtc: new Date().toISOString()
          }),
        tap(saved => {
          this.rows.set(this.rows().map(i => i.fingerprint === fingerprint ? saved : i));
          this.list.invalidate();
          this.setTriageResult(fingerprint, { message: `Saved. The issue is ${feedbackStatusLabel(saved.status)}.`, error: null });
          this.loadCounts();
        }),
        catchError(err => throwError(() => new Error('The issue could not be saved: ' + adminApiError(err).message))),
        finalize(() => this.savingFingerprint.set(null))
      );
    });
  }

  private setTriageResult(fingerprint: string, result: TriageResult): void {
    this.triageResults.set({ ...this.triageResults(), [fingerprint]: result });
  }

  /** The stored triage as the dialog takes it; a regressed issue has no status until one is chosen. */
  private static triageValuesOf(issue: GnollBenchFeedbackIssue): FeedbackTriageValues {
    return {
      status: issue.status === 'regressed' ? '' : issue.status,
      note: issue.triageNote ?? '',
      resolution: issue.resolution ?? '',
      fixedInVersion: issue.fixedInVersion ?? '',
      resolvedVia: issue.resolvedVia ?? []
    };
  }

  // --- Helpers ---

  /** Renders the view now, then focuses an element in it. */
  private focusAfterRender(selector: string): void {
    this.cdr.detectChanges();
    refreshAnchorPositioning();
    this.element(selector)?.focus();
  }

  private element(selector: string): HTMLElement | null {
    return this.host.nativeElement.querySelector<HTMLElement>(selector);
  }
}
