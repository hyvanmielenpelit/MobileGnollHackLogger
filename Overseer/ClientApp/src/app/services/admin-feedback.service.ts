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

/** The orders the issue list offers; `lastSeen` is the server's default. All are newest or largest first. */
export type AdminFeedbackIssueSort = 'lastSeen' | 'firstSeen' | 'count';

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
  appVersion: string | null;
  /** Null once the account is erased. */
  userName: string | null;
}

export interface AdminFeedbackIssuePage {
  totalCount: number;
  rows: AdminFeedbackIssue[];
}

export interface AdminFeedbackOccurrencePage {
  totalCount: number;
  rows: AdminFeedbackOccurrence[];
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

/** One page of issues. Every field is optional; a missing one means any, or the server's default. */
export interface AdminFeedbackIssuesQuery {
  /** `YYYY-MM-DD`; an issue is in range when its first-to-last-seen span overlaps it. */
  startDate?: string;
  endDate?: string;
  /** Any of these; sent comma-joined. */
  statuses?: readonly string[];
  category?: string;
  /** Issues with at least one occurrence by this model. */
  modelId?: string;
  /** Subject or summary, contains, case-insensitive. */
  search?: string;
  sort?: AdminFeedbackIssueSort;
  skip?: number;
  /** Clamped to 1–100 by the server. */
  take?: number;
}

/**
 * The typed client of `AdminFeedbackController` (`api/admin/feedback`): the feedback switches, the issue
 * list with its Model facet values and the counts per status, one issue's occurrences, and an issue's
 * triage and deletion.
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

  /** One page of issues; missing and blank fields are left out of the query. 400 on an unknown status, category or sort. */
  getIssues(query: AdminFeedbackIssuesQuery = {}): Observable<AdminFeedbackIssuePage> {
    let params = new HttpParams();
    if (query.startDate) params = params.set('startDate', query.startDate);
    if (query.endDate) params = params.set('endDate', query.endDate);
    const statuses = (query.statuses ?? []).map(s => s.trim()).filter(s => s !== '');
    if (statuses.length > 0) params = params.set('status', statuses.join(','));
    if (query.category?.trim()) params = params.set('category', query.category.trim());
    if (query.modelId?.trim()) params = params.set('modelId', query.modelId.trim());
    if (query.search?.trim()) params = params.set('search', query.search.trim());
    if (query.sort) params = params.set('sort', query.sort);
    if (query.skip && query.skip > 0) params = params.set('skip', String(query.skip));
    if (query.take != null) params = params.set('take', String(query.take));
    return this.http.get<AdminFeedbackIssuePage>(`${ADMIN_FEEDBACK_ENDPOINT}/issues`, { params });
  }

  /** The distinct model ids over all occurrences, for the Model facet. */
  getModels(): Observable<string[]> {
    return this.http.get<string[]>(`${ADMIN_FEEDBACK_ENDPOINT}/models`);
  }

  /** Issue counts per status over all dates, for the sub-tab label. */
  getCounts(): Observable<AdminFeedbackCounts> {
    return this.http.get<AdminFeedbackCounts>(`${ADMIN_FEEDBACK_ENDPOINT}/counts`);
  }

  /** One page of an issue's occurrences, newest first. */
  getOccurrences(issueId: number, skip: number, take: number): Observable<AdminFeedbackOccurrencePage> {
    let params = new HttpParams().set('take', String(take));
    if (skip > 0) params = params.set('skip', String(skip));
    return this.http.get<AdminFeedbackOccurrencePage>(this.issueUrl(issueId, '/occurrences'), { params });
  }

  /** Sets an issue's status and triage note; returns the updated issue. 404 for an unknown issue; 400 for an unknown status. */
  updateStatus(issueId: number, status: AdminFeedbackTriageStatus, note: string | null): Observable<AdminFeedbackIssue> {
    const trimmed = note?.trim() ?? '';
    return this.http.post<AdminFeedbackIssue>(this.issueUrl(issueId, '/status'), { status, note: trimmed === '' ? null : trimmed });
  }

  /** Deletes an issue and, with it, its occurrences; 204 with no body. */
  deleteIssue(issueId: number): Observable<void> {
    return this.http.delete<void>(this.issueUrl(issueId));
  }
}
