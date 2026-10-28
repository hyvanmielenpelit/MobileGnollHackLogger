import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';

/** The route prefix of `AdminFeedbackController`. */
export const ADMIN_FEEDBACK_ENDPOINT = '/api/admin/feedback';

/** An issue's status, as the server writes it. `regressed` is set by the system only. */
export type AdminFeedbackStatus = 'new' | 'acknowledged' | 'resolved' | 'regressed' | 'dismissed';

/** Every status, in the order the Status facet lists them: the open work first. */
export const ADMIN_FEEDBACK_STATUSES: readonly AdminFeedbackStatus[] = ['new', 'regressed', 'acknowledged', 'resolved', 'dismissed'];

/** The statuses an administrator may set; never `regressed`, which is a fact about recurrence. */
export type AdminFeedbackTriageStatus = Exclude<AdminFeedbackStatus, 'regressed'>;

/** The triage choices, in the order the Status select offers them. */
export const ADMIN_FEEDBACK_TRIAGE_STATUSES: readonly AdminFeedbackTriageStatus[] = ['new', 'acknowledged', 'resolved', 'dismissed'];

/** A report's category, as the `report_feedback` tool takes it. */
export type AdminFeedbackCategory = 'missing_information' | 'tool_problem' | 'own_mistake';

export const ADMIN_FEEDBACK_CATEGORIES: readonly AdminFeedbackCategory[] = ['missing_information', 'tool_problem', 'own_mistake'];

/** Where an occurrence's model came from: a system AI configuration, the user's own model, or not recorded. */
export type AdminFeedbackModelSource = 'system' | 'user' | 'unknown';

/** The Model source facet's values, in order. */
export const ADMIN_FEEDBACK_MODEL_SOURCES: readonly AdminFeedbackModelSource[] = ['system', 'user', 'unknown'];

/** Whether an issue has a written resolution. */
export type AdminFeedbackResolutionFilter = 'with' | 'without';

export const ADMIN_FEEDBACK_RESOLUTION_FILTERS: readonly AdminFeedbackResolutionFilter[] = ['with', 'without'];

/** The `group` filter value for users in no group. */
export const ADMIN_FEEDBACK_NO_GROUP = 'none';

/**
 * The orders the issue list offers; `lastSeen` is the server's default. All are newest or largest first,
 * except `subject` (A–Z) and `triaged`, which puts untriaged issues last.
 */
export type AdminFeedbackIssueSort = 'lastSeen' | 'firstSeen' | 'count' | 'users' | 'regressions' | 'triaged' | 'subject';

/** The longest triage note, resolution and fixed-in version the server keeps. */
export const ADMIN_FEEDBACK_NOTE_MAX = 1000;
export const ADMIN_FEEDBACK_RESOLUTION_MAX = 4000;
export const ADMIN_FEEDBACK_FIXED_IN_VERSION_MAX = 64;

/** What a fixed-in version may contain, as the server checks it. */
export const ADMIN_FEEDBACK_FIXED_IN_VERSION_PATTERN = /^[0-9A-Za-z.+\-]{1,64}$/;

/** The most issue ids one `ids` filter may carry. */
export const ADMIN_FEEDBACK_IDS_MAX = 500;

/** Occurrence counts per model source, over an issue's retained occurrences. */
export interface AdminFeedbackModelSourceCounts {
  system: number;
  user: number;
  unknown: number;
}

/**
 * One model as it ran when an issue was reported (`FeedbackModelIdentityDto`): its id, catalog label, provider and
 * settings. The GnollBench sub-tab's issues carry the same shape.
 */
export interface FeedbackModelIdentity {
  modelId: string;
  /** The catalog display name, else the id. */
  modelLabel: string;
  provider: string | null;
  /** Null while not recorded. */
  thinkingLevel: string | null;
  reasoningMode: string | null;
}

/** The GnollBench issue with the same fingerprint, when an accepted self-report has it. */
export interface AdminFeedbackGnollBenchLink {
  selfReportCount: number;
  status: AdminFeedbackStatus;
}

/** One issue: a group of reports with the same fingerprint (`AdminFeedbackIssueDto`). Dates are UTC ISO strings. */
export interface AdminFeedbackIssue {
  id: number;
  category: string;
  subject: string;
  summary: string;
  firstSeenUtc: string;
  lastSeenUtc: string;
  /** Every occurrence ever recorded; it survives the deletion of occurrences. */
  occurrenceCount: number;
  /** The occurrences still stored. */
  retainedCount: number;
  distinctUserCount: number;
  /** At most five model ids, then `+N`. */
  modelIds: string[];
  /**
   * The models' catalog display names, index for index with `modelIds` (the `+N` entry as is; the id when the catalog
   * has no name). The server always sends it; optional so a fixture without it still types.
   */
  modelLabels?: string[];
  /**
   * The distinct models with their provider, thinking level and reasoning mode, at most five, by label. The server
   * always sends it; optional so a fixture without it still types.
   */
  modelIdentities?: FeedbackModelIdentity[];
  /** The identities beyond the five in `modelIdentities`. Optional like it. */
  moreModelIdentities?: number;
  /** The providers of the retained occurrences. */
  providers: string[];
  modelSources: AdminFeedbackModelSourceCounts;
  regressionCount: number;
  lastRegressionUtc: string | null;
  latestExpectedLocation: string | null;
  latestRelatedTools: string[];
  latestSuspectedCause: string | null;
  latestAppVersion: string | null;
  status: AdminFeedbackStatus;
  triageNote: string | null;
  /** Null when the triager's account is gone, or nobody has triaged the issue. */
  triagedByUserName: string | null;
  triagedUtc: string | null;
  /** What was done about the issue; null while none is written. */
  resolution: string | null;
  fixedInVersion: string | null;
  /**
   * Where the fix went: *Resolved via* ids in the canonical order (`FEEDBACK_RESOLVED_VIA_IDS`), empty when none. The
   * server always sends it; optional so a fixture without it still types.
   */
  resolvedVia?: string[];
  alsoInGnollBench: AdminFeedbackGnollBenchLink | null;
}

/** One stored report of an issue (`AdminFeedbackOccurrenceDto`). */
export interface AdminFeedbackOccurrence {
  id: number;
  createdUtc: string;
  subject: string;
  summary: string;
  details: string | null;
  searchedFor: string | null;
  expectedLocation: string | null;
  relatedTools: string[];
  noticedVia: string | null;
  suspectedCause: string | null;
  suspectedCauseNote: string | null;
  truncated: boolean;
  redacted: boolean;
  chatSessionId: number;
  turnUserMessageId: number | null;
  provider: string | null;
  modelId: string | null;
  /** The model's catalog display name, else the id; null when `modelId` is. Optional like `modelLabels`. */
  modelLabel?: string | null;
  /** The settings the reply ran with; null while not recorded. Optional like `modelLabel`. */
  thinkingLevel?: string | null;
  reasoningMode?: string | null;
  appVersion: string | null;
  /** Null once the account is erased. */
  userName: string | null;
  /** Null while it is not recorded. */
  modelSource: 'system' | 'user' | null;
  systemConfigurationId: number | null;
  /** Null for a configuration since deleted, or a turn on the user's own model. */
  systemConfigurationName: string | null;
  /** The reporting user's groups now; empty for a user in no group. */
  groupNames: string[];
}

export interface AdminFeedbackIssuePage {
  totalCount: number;
  rows: AdminFeedbackIssue[];
}

/** Every issue id matching a filter, at most 1,000; `truncated` when more match. */
export interface AdminFeedbackIssueIds {
  ids: number[];
  totalCount: number;
  truncated: boolean;
}

export interface AdminFeedbackOccurrencePage {
  totalCount: number;
  rows: AdminFeedbackOccurrence[];
}

/** One facet value and the number of issues it has in the range. */
export interface AdminFeedbackFacetValue {
  value: string;
  count: number;
  /**
   * The value's display label: the catalog name for the Models facet (the id when it has none), empty for every other
   * facet. The server always sends it; optional so a fixture without it still types.
   */
  label?: string;
}

/** One facet entity (a user, a group, a configuration) with its display name and issue count. */
export interface AdminFeedbackFacetEntity {
  id: string;
  name: string;
  count: number;
}

/** The facet options present in a date range (`GET facets`). The group id `none` is No group. */
export interface AdminFeedbackFacets {
  categories: AdminFeedbackFacetValue[];
  models: AdminFeedbackFacetValue[];
  providers: AdminFeedbackFacetValue[];
  sources: AdminFeedbackFacetValue[];
  versions: AdminFeedbackFacetValue[];
  tools: AdminFeedbackFacetValue[];
  users: AdminFeedbackFacetEntity[];
  groups: AdminFeedbackFacetEntity[];
  configs: AdminFeedbackFacetEntity[];
  /**
   * The *Resolved via* ids present, in the canonical order, then `none` for issues with none recorded; the label is
   * empty. The server always sends it; optional so a fixture without it still types.
   */
  resolvedVia?: AdminFeedbackFacetValue[];
}

/** Issue counts per status over all dates. */
export interface AdminFeedbackCounts {
  new: number;
  acknowledged: number;
  resolved: number;
  regressed: number;
  dismissed: number;
}

/** One feedback switch; `seeded` while no administrator has set it and the configuration default applies. */
export interface AdminFeedbackSwitch {
  enabled: boolean;
  setAtUtc: string | null;
  setByUserName: string | null;
  seeded: boolean;
}

/** Which feedback switch; the route segment of `POST switch/{target}`. */
export type AdminFeedbackSwitchTarget = 'chat';

/** The feedback switch. */
export interface AdminFeedbackSwitches {
  chat: AdminFeedbackSwitch;
}

/**
 * The issue filters every list request shares. Every field is optional and every list is sent
 * comma-joined; a missing or empty one means any. The occurrence-level filters (model, provider, user,
 * group, source, configuration, version, tool) match an issue when one retained occurrence matches them all.
 */
export interface AdminFeedbackIssueFilters {
  /** `YYYY-MM-DD`; an issue is in range when its first-to-last-seen span overlaps it. */
  startDate?: string;
  endDate?: string;
  statuses?: readonly string[];
  /** *Resolved via* ids, and `none` for issues with none recorded; an issue-level filter. */
  resolvedVia?: readonly string[];
  categories?: readonly string[];
  /** Sent as `modelId`. */
  modelIds?: readonly string[];
  providers?: readonly string[];
  /** Identity user ids. */
  users?: readonly string[];
  /** Group ids, and `none` for users in no group. */
  groups?: readonly string[];
  sources?: readonly string[];
  /** System AI configuration ids. */
  configs?: readonly string[];
  versions?: readonly string[];
  tools?: readonly string[];
  resolution?: AdminFeedbackResolutionFilter;
  /** At most 500 issue ids, for Show selected only. */
  ids?: readonly number[];
  /** Subject, summary and the occurrences' details, searched-for and expected location; contains, case-insensitive. */
  search?: string;
}

/** One page of issues. */
export interface AdminFeedbackIssuesQuery extends AdminFeedbackIssueFilters {
  sort?: AdminFeedbackIssueSort;
  skip?: number;
  /** Clamped to 1–100 by the server. */
  take?: number;
}

/**
 * A triage change. A field left out, or null, keeps the stored value; an empty string clears it.
 * Text is trimmed before it is sent.
 */
export interface AdminFeedbackTriageChanges {
  note?: string | null;
  resolution?: string | null;
  fixedInVersion?: string | null;
  /** *Resolved via* ids; null or left out keeps the stored list, `[]` clears it. */
  resolvedVia?: readonly string[] | null;
}

/** The list filters as query parameters; blank values and empty lists are left out. */
function filterParams(filters: AdminFeedbackIssueFilters, params = new HttpParams()): HttpParams {
  const list = (name: string, values: readonly (string | number)[] | undefined): void => {
    const parts = (values ?? []).map(v => String(v).trim()).filter(v => v !== '');
    if (parts.length > 0) params = params.set(name, parts.join(','));
  };
  if (filters.startDate) params = params.set('startDate', filters.startDate);
  if (filters.endDate) params = params.set('endDate', filters.endDate);
  list('status', filters.statuses);
  list('resolvedVia', filters.resolvedVia);
  list('category', filters.categories);
  list('modelId', filters.modelIds);
  list('provider', filters.providers);
  list('user', filters.users);
  list('group', filters.groups);
  list('source', filters.sources);
  list('config', filters.configs);
  list('version', filters.versions);
  list('tool', filters.tools);
  if (filters.resolution) params = params.set('resolution', filters.resolution);
  list('ids', filters.ids);
  if (filters.search?.trim()) params = params.set('search', filters.search.trim());
  return params;
}

/**
 * The typed client of `AdminFeedbackController` (`api/admin/feedback`): the feedback switches, the issue
 * list, its matching ids and facet options, the counts per status, one issue's occurrences, and an
 * issue's triage and deletion.
 */
@Injectable({
  providedIn: 'root'
})
export class AdminFeedbackService {
  private http = inject(HttpClient);

  private issueUrl(id: number, suffix = ''): string {
    return `${ADMIN_FEEDBACK_ENDPOINT}/issues/${encodeURIComponent(String(id))}${suffix}`;
  }

  // --- Switch ---

  getSwitches(): Observable<AdminFeedbackSwitches> {
    return this.http.get<AdminFeedbackSwitches>(`${ADMIN_FEEDBACK_ENDPOINT}/switch`);
  }

  /** Appends a setting row to the switch; the same value as now still records the click. Returns the switch. */
  setSwitch(target: AdminFeedbackSwitchTarget, enabled: boolean): Observable<AdminFeedbackSwitches> {
    return this.http.post<AdminFeedbackSwitches>(`${ADMIN_FEEDBACK_ENDPOINT}/switch/${target}`, { enabled });
  }

  // --- Issues ---

  /** One page of issues; missing and blank fields are left out of the query. 400 on an unknown filter value or sort. */
  getIssues(query: AdminFeedbackIssuesQuery = {}): Observable<AdminFeedbackIssuePage> {
    let params = filterParams(query);
    if (query.sort) params = params.set('sort', query.sort);
    if (query.skip && query.skip > 0) params = params.set('skip', String(query.skip));
    if (query.take != null) params = params.set('take', String(query.take));
    return this.http.get<AdminFeedbackIssuePage>(`${ADMIN_FEEDBACK_ENDPOINT}/issues`, { params });
  }

  /** Every issue id matching the filters, in the sort's order, at most 1,000; for Select all matching and Show selected only. */
  getIssueIds(filters: AdminFeedbackIssueFilters = {}, sort?: AdminFeedbackIssueSort): Observable<AdminFeedbackIssueIds> {
    let params = filterParams(filters);
    if (sort) params = params.set('sort', sort);
    return this.http.get<AdminFeedbackIssueIds>(`${ADMIN_FEEDBACK_ENDPOINT}/issues/ids`, { params });
  }

  /** The facet options present in the date range, each with its issue count. */
  getFacets(startDate?: string, endDate?: string): Observable<AdminFeedbackFacets> {
    let params = new HttpParams();
    if (startDate) params = params.set('startDate', startDate);
    if (endDate) params = params.set('endDate', endDate);
    return this.http.get<AdminFeedbackFacets>(`${ADMIN_FEEDBACK_ENDPOINT}/facets`, { params });
  }

  /** Issue counts per status over all dates, for the tab's pill. */
  getCounts(): Observable<AdminFeedbackCounts> {
    return this.http.get<AdminFeedbackCounts>(`${ADMIN_FEEDBACK_ENDPOINT}/counts`);
  }

  /**
   * One page of an issue's occurrences, newest first, narrowed by the filters' occurrence-level filters
   * (the server ignores the issue-level ones); `all` ignores them all.
   */
  getOccurrences(issueId: number, skip: number, take: number, filters: AdminFeedbackIssueFilters = {}, all = false):
    Observable<AdminFeedbackOccurrencePage> {
    let params = filterParams(filters).set('take', String(take));
    if (skip > 0) params = params.set('skip', String(skip));
    if (all) params = params.set('all', 'true');
    return this.http.get<AdminFeedbackOccurrencePage>(this.issueUrl(issueId, '/occurrences'), { params });
  }

  /**
   * Sets an issue's status and, for each field given, its triage note, resolution, fixed-in version and resolved-via
   * list; returns the updated issue. 404 for an unknown issue; 400 for an unknown status or resolved-via id, or a
   * field too long.
   */
  updateStatus(issueId: number, status: AdminFeedbackTriageStatus, changes: AdminFeedbackTriageChanges = {}): Observable<AdminFeedbackIssue> {
    const body: {
      status: AdminFeedbackTriageStatus; note?: string; resolution?: string; fixedInVersion?: string; resolvedVia?: string[];
    } = { status };
    if (changes.note != null) body.note = changes.note.trim();
    if (changes.resolution != null) body.resolution = changes.resolution.trim();
    if (changes.fixedInVersion != null) body.fixedInVersion = changes.fixedInVersion.trim();
    if (changes.resolvedVia != null) body.resolvedVia = [...changes.resolvedVia];
    return this.http.post<AdminFeedbackIssue>(this.issueUrl(issueId, '/status'), body);
  }

  /** Deletes an issue and, with it, its occurrences; 204 with no body. */
  deleteIssue(issueId: number): Observable<void> {
    return this.http.delete<void>(this.issueUrl(issueId));
  }
}
