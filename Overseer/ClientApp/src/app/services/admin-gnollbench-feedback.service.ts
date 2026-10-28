import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import type { FeedbackModelIdentity } from './admin-feedback.service';

/** The route prefix of `AdminGnollBenchFeedbackController`. */
export const ADMIN_GNOLLBENCH_FEEDBACK_ENDPOINT = '/api/admin/feedback/gnollbench';

/** An issue's derived status. `regressed` is a resolved issue reported again, and is never set by hand. */
export type GnollBenchFeedbackStatus = 'new' | 'acknowledged' | 'resolved' | 'regressed' | 'dismissed';

/** Every status, in the order the Status facet lists them: the open work first. */
export const GNOLLBENCH_FEEDBACK_STATUSES: readonly GnollBenchFeedbackStatus[] = ['new', 'regressed', 'acknowledged', 'resolved', 'dismissed'];

/** The statuses an administrator may set. */
export type GnollBenchFeedbackTriageStatus = Exclude<GnollBenchFeedbackStatus, 'regressed'>;

/** The triage choices, in the order the Status select offers them. */
export const GNOLLBENCH_FEEDBACK_TRIAGE_STATUSES: readonly GnollBenchFeedbackTriageStatus[] = ['new', 'acknowledged', 'resolved', 'dismissed'];

/** A self-report's category, as the `report_feedback` tool takes it. */
export const GNOLLBENCH_FEEDBACK_CATEGORIES: readonly string[] = ['missing_information', 'tool_problem', 'own_mistake'];

/** Which self-reports form the base set: the accepted calls (the server's default), every call, or the refused ones. */
export type GnollBenchAcceptedScope = 'accepted' | 'all' | 'refused';

export const GNOLLBENCH_ACCEPTED_SCOPES: readonly GnollBenchAcceptedScope[] = ['accepted', 'all', 'refused'];

/** A run's information condition, as the server writes it. */
export type GnollBenchInformationCondition = 'production' | 'sourceOnly' | 'closedBook';

export const GNOLLBENCH_INFORMATION_CONDITIONS: readonly GnollBenchInformationCondition[] = ['production', 'sourceOnly', 'closedBook'];

/** The orders the issue list offers; `lastSeen` is the server's default. */
export type GnollBenchFeedbackSort = 'lastSeen' | 'firstSeen' | 'count' | 'runs' | 'models' | 'subject';

/** The Overseer chat issue with the same fingerprint. */
export interface GnollBenchFeedbackOverseerLink {
  issueId: number;
  status: string;
}

/** One GnollBench issue: the self-reports with one fingerprint (`GnollBenchFeedbackIssueDto`). Dates are UTC ISO strings. */
export interface GnollBenchFeedbackIssue {
  fingerprint: string;
  category: string;
  /** From the newest self-report. */
  subject: string;
  summary: string;
  firstSeenUtc: string;
  lastSeenUtc: string;
  selfReportCount: number;
  runCount: number;
  modelCount: number;
  /** Model labels, at most five, then `+N`. */
  models: string[];
  /**
   * The tested models as their runs ran them, with provider, thinking level and reasoning mode, at most five, by
   * label. The server always sends it; optional so a fixture without it still types.
   */
  modelIdentities?: FeedbackModelIdentity[];
  /** The identities beyond the five in `modelIdentities`. Optional like it. */
  moreModelIdentities?: number;
  providers: string[];
  /** Suite names, at most five, then `+N`. */
  suites: string[];
  status: GnollBenchFeedbackStatus;
  triageNote: string | null;
  resolution: string | null;
  fixedInVersion: string | null;
  /**
   * Where the fix went: *Resolved via* ids in the canonical order (`FEEDBACK_RESOLVED_VIA_IDS`), empty when none. The
   * server always sends it; optional so a fixture without it still types.
   */
  resolvedVia?: string[];
  /** Null when the triager's account is gone, or nobody has triaged the issue. */
  triagedByUserName: string | null;
  triagedUtc: string | null;
  resolvedUtc: string | null;
  alsoInOverseer: GnollBenchFeedbackOverseerLink | null;
}

/** One self-report of an issue, with its run's facts (`GnollBenchFeedbackOccurrenceDto`). */
export interface GnollBenchFeedbackOccurrence {
  id: number;
  occurredUtc: string;
  runId: number;
  seriesId: number | null;
  suiteId: number | null;
  suiteName: string;
  questionId: number | null;
  questionText: string;
  modelId: string;
  modelLabel: string;
  provider: string;
  /** The run's thinking level and reasoning mode; null while not recorded. Optional so a fixture without them still types. */
  thinkingLevel?: string | null;
  reasoningMode?: string | null;
  condition: string;
  /** The stored stamp, `spec:hash12`. */
  variant: string | null;
  /** The stamp's spec, without the hash. */
  variantName: string | null;
  corpus: string;
  harness: string | null;
  accepted: boolean;
  error: string | null;
  category: string | null;
  subject: string | null;
  summary: string | null;
  details: string | null;
  searchedFor: string | null;
  expectedLocation: string | null;
  relatedTools: string[];
  noticedVia: string | null;
  suspectedCause: string | null;
  suspectedCauseNote: string | null;
  truncated: boolean;
}

export interface GnollBenchFeedbackIssuePage {
  totalCount: number;
  rows: GnollBenchFeedbackIssue[];
}

export interface GnollBenchFeedbackOccurrencePage {
  totalCount: number;
  rows: GnollBenchFeedbackOccurrence[];
}

/** The matching fingerprints in sort order, at most 1,000, and whether the cap cut them. */
export interface GnollBenchFeedbackIds {
  fingerprints: string[];
  totalCount: number;
  truncated: boolean;
}

/** Issue counts per derived status over accepted self-reports and all dates. */
export interface GnollBenchFeedbackCounts {
  new: number;
  acknowledged: number;
  resolved: number;
  regressed: number;
  dismissed: number;
}

/** A facet option named by a string value. */
export interface GnollBenchFeedbackFacetValue {
  value: string;
  label: string;
  count: number;
}

/** A facet option named by an id. */
export interface GnollBenchFeedbackFacetEntity {
  id: number;
  name: string;
  count: number;
}

/** The filter options present among the self-reports in a date range, each with its number of issues. */
export interface GnollBenchFeedbackFacets {
  categories: GnollBenchFeedbackFacetValue[];
  /** The value is the model id, the label its display label. */
  models: GnollBenchFeedbackFacetValue[];
  providers: GnollBenchFeedbackFacetValue[];
  conditions: GnollBenchFeedbackFacetValue[];
  /** `live` first. */
  corpora: GnollBenchFeedbackFacetValue[];
  /** Variant specs without the hash, `none` first. */
  variants: GnollBenchFeedbackFacetValue[];
  /** In numeric order. */
  harnesses: GnollBenchFeedbackFacetValue[];
  runs: GnollBenchFeedbackFacetEntity[];
  series: GnollBenchFeedbackFacetEntity[];
  /** Multi-Run Analysis groups. */
  groups: GnollBenchFeedbackFacetEntity[];
  /** Battery runs. */
  batteries: GnollBenchFeedbackFacetEntity[];
  /** Model batches. */
  batches: GnollBenchFeedbackFacetEntity[];
  suites: GnollBenchFeedbackFacetEntity[];
  /** Named by the question text, cut to 200 characters. */
  questions: GnollBenchFeedbackFacetEntity[];
  /**
   * The *Resolved via* ids present, in the canonical order, then `none` for issues with none recorded; the label is
   * the value. The server always sends it; optional so a fixture without it still types.
   */
  resolvedVia?: GnollBenchFeedbackFacetValue[];
}

/**
 * The filters every list endpoint takes. Each list is sent comma-joined; a missing or empty field means
 * any, and a missing `accepted` the accepted calls.
 */
export interface GnollBenchFeedbackFilters {
  /** `YYYY-MM-DD`; an issue is in range when its active span overlaps it. */
  startDate?: string;
  endDate?: string;
  statuses?: readonly string[];
  /** *Resolved via* ids, and `none` for issues with none recorded; an issue-level filter. */
  resolvedVia?: readonly string[];
  accepted?: GnollBenchAcceptedScope;
  categories?: readonly string[];
  /** Tested model ids. */
  models?: readonly string[];
  providers?: readonly string[];
  runs?: readonly string[];
  series?: readonly string[];
  /** Multi-Run Analysis group ids. */
  groups?: readonly string[];
  /** Battery run ids. */
  batteries?: readonly string[];
  /** Model batch ids. */
  batches?: readonly string[];
  suites?: readonly string[];
  questions?: readonly string[];
  conditions?: readonly string[];
  /** `live` for none. */
  corpora?: readonly string[];
  /** Variant specs without the hash; `none` for production instructions. */
  variants?: readonly string[];
  harnesses?: readonly string[];
  /** At most 500. */
  fingerprints?: readonly string[];
  /** Subject, summary, details and question text; at most 200 characters. */
  search?: string;
  sort?: GnollBenchFeedbackSort;
}

/** One page of issues. */
export interface GnollBenchFeedbackIssuesQuery extends GnollBenchFeedbackFilters {
  skip?: number;
  /** Clamped by the server. */
  take?: number;
}

/** One page of an issue's self-reports; `all` ignores the occurrence-level filters. */
export interface GnollBenchFeedbackOccurrencesQuery {
  filters?: GnollBenchFeedbackFilters;
  all?: boolean;
  skip?: number;
  take?: number;
}

/**
 * A triage change. An absent or null note, resolution or fixed-in version keeps the stored value, and an
 * empty string clears it; an absent or null resolved-via list keeps the stored one, and `[]` clears it.
 */
export interface GnollBenchFeedbackStatusUpdate {
  status: GnollBenchFeedbackTriageStatus;
  note?: string | null;
  resolution?: string | null;
  fixedInVersion?: string | null;
  resolvedVia?: readonly string[] | null;
}

/** The list fields of `GnollBenchFeedbackFilters` and the query parameter each one is sent as. */
const LIST_PARAMS: readonly (readonly [keyof GnollBenchFeedbackFilters, string])[] = [
  ['statuses', 'status'],
  ['resolvedVia', 'resolvedVia'],
  ['categories', 'category'],
  ['models', 'model'],
  ['providers', 'provider'],
  ['runs', 'run'],
  ['series', 'series'],
  ['groups', 'group'],
  ['batteries', 'battery'],
  ['batches', 'batch'],
  ['suites', 'suite'],
  ['questions', 'question'],
  ['conditions', 'condition'],
  ['corpora', 'corpus'],
  ['variants', 'variant'],
  ['harnesses', 'harness'],
  ['fingerprints', 'fingerprints']
];

/** The filters as query parameters; blanks and the defaults are left out. */
export function gnollBenchFilterParams(filters: GnollBenchFeedbackFilters = {}, params = new HttpParams()): HttpParams {
  if (filters.startDate) params = params.set('startDate', filters.startDate);
  if (filters.endDate) params = params.set('endDate', filters.endDate);
  if (filters.accepted && filters.accepted !== 'accepted') params = params.set('accepted', filters.accepted);
  for (const [field, name] of LIST_PARAMS) {
    const values = ((filters[field] as readonly string[] | undefined) ?? []).map(v => v.trim()).filter(v => v !== '');
    if (values.length > 0) params = params.set(name, values.join(','));
  }
  const search = filters.search?.trim();
  if (search) params = params.set('search', search);
  if (filters.sort) params = params.set('sort', filters.sort);
  return params;
}

/**
 * The typed client of `AdminGnollBenchFeedbackController` (`api/admin/feedback/gnollbench`): the issues the
 * GnollBench candidates reported about themselves, grouped by fingerprint, with their fingerprints for a
 * selection, their facets and counts per status, one issue's self-reports, and an issue's triage. Errors
 * are `{ message }`. There is no delete: self-reports are run records and leave with their run.
 */
@Injectable({
  providedIn: 'root'
})
export class AdminGnollBenchFeedbackService {
  private http = inject(HttpClient);

  private issueUrl(fingerprint: string, suffix = ''): string {
    return `${ADMIN_GNOLLBENCH_FEEDBACK_ENDPOINT}/issues/${encodeURIComponent(fingerprint)}${suffix}`;
  }

  /** One page of issues. 400 on an unknown status, category, condition, accepted value or sort, or a non-numeric id. */
  getIssues(query: GnollBenchFeedbackIssuesQuery = {}): Observable<GnollBenchFeedbackIssuePage> {
    let params = gnollBenchFilterParams(query);
    if (query.skip && query.skip > 0) params = params.set('skip', String(query.skip));
    if (query.take != null) params = params.set('take', String(query.take));
    return this.http.get<GnollBenchFeedbackIssuePage>(`${ADMIN_GNOLLBENCH_FEEDBACK_ENDPOINT}/issues`, { params });
  }

  /** Every matching fingerprint in sort order, at most 1,000. */
  getIssueIds(filters: GnollBenchFeedbackFilters = {}): Observable<GnollBenchFeedbackIds> {
    return this.http.get<GnollBenchFeedbackIds>(`${ADMIN_GNOLLBENCH_FEEDBACK_ENDPOINT}/issues/ids`, { params: gnollBenchFilterParams(filters) });
  }

  /** One page of an issue's self-reports, newest first. 404 for an unknown fingerprint. */
  getOccurrences(fingerprint: string, query: GnollBenchFeedbackOccurrencesQuery = {}): Observable<GnollBenchFeedbackOccurrencePage> {
    // The date range, the status and Resolved via never narrow the self-reports, and the sort is the server's own.
    const occurrenceFilters: GnollBenchFeedbackFilters = { ...query.filters };
    delete occurrenceFilters.startDate;
    delete occurrenceFilters.endDate;
    delete occurrenceFilters.statuses;
    delete occurrenceFilters.resolvedVia;
    delete occurrenceFilters.sort;
    let params = gnollBenchFilterParams(occurrenceFilters);
    if (query.all) params = params.set('all', 'true');
    if (query.skip && query.skip > 0) params = params.set('skip', String(query.skip));
    if (query.take != null) params = params.set('take', String(query.take));
    return this.http.get<GnollBenchFeedbackOccurrencePage>(this.issueUrl(fingerprint, '/occurrences'), { params });
  }

  /** The filter options present in the range under the base set `accepted` names. */
  getFacets(range: { startDate?: string; endDate?: string; accepted?: GnollBenchAcceptedScope } = {}): Observable<GnollBenchFeedbackFacets> {
    const params = gnollBenchFilterParams({ startDate: range.startDate, endDate: range.endDate, accepted: range.accepted });
    return this.http.get<GnollBenchFeedbackFacets>(`${ADMIN_GNOLLBENCH_FEEDBACK_ENDPOINT}/facets`, { params });
  }

  /** Issue counts per derived status over accepted self-reports and all dates. */
  getCounts(): Observable<GnollBenchFeedbackCounts> {
    return this.http.get<GnollBenchFeedbackCounts>(`${ADMIN_GNOLLBENCH_FEEDBACK_ENDPOINT}/counts`);
  }

  /**
   * Triages an issue and returns it as the list shows it. Only the fields given are sent: an absent field
   * keeps the stored value, and an empty string or list clears it. 400 for `regressed`, an unknown status, a
   * value over its length, a malformed fixed-in version or an unknown resolved-via id; 404 for an unknown fingerprint.
   */
  updateStatus(fingerprint: string, update: GnollBenchFeedbackStatusUpdate): Observable<GnollBenchFeedbackIssue> {
    const body: Record<string, string | string[]> = { status: update.status };
    if (update.note != null) body['note'] = update.note;
    if (update.resolution != null) body['resolution'] = update.resolution;
    if (update.fixedInVersion != null) body['fixedInVersion'] = update.fixedInVersion;
    if (update.resolvedVia != null) body['resolvedVia'] = [...update.resolvedVia];
    return this.http.post<GnollBenchFeedbackIssue>(this.issueUrl(fingerprint, '/status'), body);
  }
}
