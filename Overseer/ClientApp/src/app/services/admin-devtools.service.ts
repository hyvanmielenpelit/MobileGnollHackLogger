import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpErrorResponse, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';

/** A stage of a source job, in pipeline order. */
export type SourceIndexPhase =
  'Idle' | 'WaitingForRepository' | 'CheckingMakedefs' | 'BuildingMakedefs' | 'GeneratingHeaders' |
  'ReadingFiles' | 'ParsingGameData' | 'Publishing';

export type SourceIndexTrigger = 'Startup' | 'RepositoryChange' | 'Manual' | 'FollowUp';
export type SourceJobKind = 'Index' | 'BuildMakedefs';
export type SourceJobOutcome = 'Running' | 'Succeeded' | 'Failed' | 'Canceled' | 'SkippedNoChange';
export type MakedefsOutcomeKind =
  'UpToDate' | 'SkippedBranch' | 'BuildFailed' | 'ExecutableMissing' | 'RunFailed' | 'Generated';

export interface SourceIndexJob {
  id: string;
  trigger: SourceIndexTrigger;
  kind: SourceJobKind;
  startedAtUtc: string;
  finishedAtUtc: string | null;
  phase: SourceIndexPhase;
  filesRead: number;
  /** Null until the files have been enumerated. */
  filesTotal: number | null;
  outcome: SourceJobOutcome;
  message: string | null;
  outputTail: string[] | null;
  makedefsOutcome: MakedefsOutcomeKind | null;
}

export interface SourceSkippedFile {
  path: string;
  bytes: number;
}

export interface SourceIndexInfo {
  generation: number;
  headSha: string | null;
  branch: string | null;
  indexedAtUtc: string;
  durationMs: number;
  fileCount: number;
  totalLines: number;
  constantCount: number;
  fingerprintShort: string | null;
  skippedBySize: SourceSkippedFile[];
  skippedExcludedDirectories: number;
  maxFileSizeKB: number;
}

export interface GeneratedHeaderState {
  name: string;
  exists: boolean;
  lastWriteUtc: string | null;
  inIndex: boolean;
}

export interface MakedefsState {
  supported: boolean;
  buildCommand: string | null;
  buildCommandToolExists: boolean;
  executablePath: string | null;
  executableExists: boolean;
  executableLastWriteUtc: string | null;
  branchRestriction: string | null;
  inputsState: 'upToDate' | 'stale' | 'unknown';
  stampGeneratedAtUtc: string | null;
  lastOutcome: MakedefsOutcomeKind | null;
  lastBuildAtUtc: string | null;
  lastBuildDurationMs: number | null;
  lastOutputTail: string[] | null;
}

/** One source service's index, makedefs and job state (GET api/admin/devtools/source/status). */
export interface SourceIndexStatus {
  configured: boolean;
  sourcePath: string | null;
  pathExists: boolean;
  isIndexingComplete: boolean;
  currentHeadSha: string | null;
  currentBranch: string | null;
  index: SourceIndexInfo | null;
  upToDate: boolean;
  nextCheckAtUtc: string | null;
  checkIntervalMinutes: number;
  generatedHeaders: GeneratedHeaderState[];
  makedefs: MakedefsState | null;
  currentJob: SourceIndexJob | null;
  recentJobs: SourceIndexJob[];
}

export interface SourceDashboard {
  gnollHack: SourceIndexStatus;
  netHack: SourceIndexStatus;
  serverTimeUtc: string;
}

export interface SourceJobStarted {
  jobId: string | null;
}

export type ConfigHealthStatus = 'ok' | 'warning' | 'error' | 'info';

export interface ConfigSourceInfo {
  name: string;
  kind: string;
  fileExists: boolean | null;
  keyCount: number;
}

export interface ConfigHealthCheck {
  id: string;
  category: string;
  title: string;
  keys: string[];
  status: ConfigHealthStatus;
  /** Which configuration provider supplied the value, or "Not set". */
  source: string | null;
  /** Null for a sensitive key, whose value never leaves the server. */
  value: string | null;
  summary: string;
  remedy: string | null;
}

/** GET api/admin/devtools/config-health. */
export interface ConfigHealthReport {
  generatedAtUtc: string;
  environmentName: string | null;
  userSecretsExpected: boolean;
  sources: ConfigSourceInfo[];
  summary: { ok: number; warning: number; error: number; info: number };
  checks: ConfigHealthCheck[];
}

// ------------------------------------------------------------------ corpora

/** A corpus as the URL segment of its endpoints: `api/admin/devtools/{slug}/…`. */
export type CorpusKindSlug = 'gnollhack-wiki' | 'nethack-wiki' | 'knowledge-base';

/** A corpus as the server names it (`CorpusKind`). */
export type CorpusKind = 'GnollHackWiki' | 'NetHackWiki' | 'KnowledgeBase';

/** The server's corpus name for each URL slug. */
export const CORPUS_KIND_BY_SLUG: Readonly<Record<CorpusKindSlug, CorpusKind>> = {
  'gnollhack-wiki': 'GnollHackWiki',
  'nethack-wiki': 'NetHackWiki',
  'knowledge-base': 'KnowledgeBase'
};

/** `GitPoll`: at startup, on a new HEAD and on request. `StartupAndManual`: at startup and on request only. */
export type CorpusRefreshMode = 'GitPoll' | 'StartupAndManual';
export type CorpusIndexTrigger = 'Startup' | 'RepositoryChange' | 'Manual' | 'FollowUp';
/** The step a corpus pass is in, in pass order after `Idle`. */
export type CorpusIndexPhase = 'Idle' | 'ReadingFiles' | 'BuildingIndex' | 'Publishing';
export type CorpusIndexOutcome = 'Running' | 'Succeeded' | 'Failed' | 'Canceled';

/** One corpus pass, running or finished. */
export interface CorpusIndexJob {
  id: string;
  trigger: CorpusIndexTrigger;
  startedAtUtc: string;
  finishedAtUtc: string | null;
  phase: CorpusIndexPhase;
  filesRead: number;
  /** Null until the files have been enumerated. */
  filesTotal: number | null;
  outcome: CorpusIndexOutcome;
  message: string | null;
}

/** A file left out because it exceeds the corpus's size limit. */
export interface SkippedCorpusFile {
  path: string;
  bytes: number;
}

/** A file a pass could not read; `error` is the exception type and message. */
export interface FailedCorpusFile {
  path: string;
  error: string;
}

/** One published corpus index. */
export interface CorpusIndexInfo {
  generation: number;
  /** Null outside a Git working tree. */
  headSha: string | null;
  branch: string | null;
  indexedAtUtc: string;
  durationMs: number;
  fileCount: number;
  totalBytes: number;
  /** The first 12 characters of the content fingerprint. */
  fingerprintShort: string | null;
  /** Null for a corpus without a size limit. */
  maxFileSizeKB: number | null;
  /** At most 200 entries; `skippedBySizeCount` is the full count. */
  skippedBySize: SkippedCorpusFile[];
  skippedBySizeCount: number;
  skippedDotDirectories: number;
  /** At most 200 entries; `failedFileCount` is the full count. */
  failedFiles: FailedCorpusFile[];
  failedFileCount: number;
}

/** The part of a corpus status every corpus shares (GET api/admin/devtools/{slug}/status). */
export interface CorpusIndexStatus {
  kind: CorpusKind;
  /** The configuration key of the corpus folder, such as `WikiPath`; never its value. */
  configKey: string;
  /** Whether the key is set. */
  configured: boolean;
  /** The folder the service reads, a built-in default included; null when there is none. */
  path: string | null;
  pathExists: boolean;
  isGitRepository: boolean;
  isIndexingComplete: boolean;
  currentHeadSha: string | null;
  currentBranch: string | null;
  /** Null until a pass has published an index. */
  index: CorpusIndexInfo | null;
  /** The index was read at the current HEAD; always true for `StartupAndManual`. */
  upToDate: boolean;
  refreshMode: CorpusRefreshMode;
  nextCheckAtUtc: string | null;
  checkIntervalMinutes: number | null;
  currentJob: CorpusIndexJob | null;
  /** Finished passes, newest first. */
  recentJobs: CorpusIndexJob[];
  serverTimeUtc: string;
}

/** One top-level wiki directory: the `wiki_search` category, its articles and their size. */
export interface WikiCategory {
  category: string;
  articles: number;
  bytes: number;
}

/** A title several indexed articles share, with each article's path form. */
export interface WikiDuplicateTitle {
  title: string;
  paths: string[];
}

/** GET api/admin/devtools/gnollhack-wiki/status. */
export interface WikiStatus extends CorpusIndexStatus {
  /** Most articles first; `(root)` for files at the root. */
  categories: WikiCategory[];
  /** At most 100 groups; `duplicateTitleCount` is the full count. */
  duplicateTitles: WikiDuplicateTitle[];
  duplicateTitleCount: number;
}

export interface NetHackWikiNamespace {
  namespace: string;
  articles: number;
}

/** One reason the converter skipped pages; `breakdown` lists the parts of the count when the report gives them. */
export interface ConversionSkipReason {
  /** The report's own key, such as `non_english`. */
  reason: string;
  count: number;
  breakdown?: ConversionSkipReason[] | null;
}

/** The NetHack wiki converter's last report; a field the report lacks is null. */
export interface NetHackWikiConversionReport {
  reportLastWriteUtc: string;
  /** The converter's local time, as written. */
  generatedAt: string | null;
  /** The dump file's local modification time, as written. */
  dumpModified: string | null;
  dumpBytes: number | null;
  durationSeconds: number | null;
  pagesWritten: number | null;
  redirects: number | null;
  errors: number | null;
  skipped: ConversionSkipReason[] | null;
  tablesConverted: number | null;
  droppedTemplates: number | null;
  largestFileName: string | null;
  largestFileBytes: number | null;
  filesOver500Kb: number | null;
}

/** GET api/admin/devtools/nethack-wiki/status. */
export interface NetHackWikiStatus extends CorpusIndexStatus {
  /** Most articles first; empty until a pass publishes. */
  namespaces: NetHackWikiNamespace[];
  /** Null when there is no report or it cannot be read. */
  conversionReport: NetHackWikiConversionReport | null;
  /** Why the report could not be read; null when it was read or does not exist. */
  conversionReportError: string | null;
  /** The conversion report was written after the published index was built. */
  treeChangedSinceIndex: boolean;
}

export interface KnowledgeArticleInfo {
  topic: string;
  title: string | null;
  /** The front matter carried a non-blank title. */
  hasTitle: boolean;
  /** The front matter carried a non-blank summary. */
  hasSummary: boolean;
  bytes: number;
  lastWriteUtc: string;
}

/** An article a later file with a case-variant topic replaced; paths relative to `KbPath`. */
export interface KnowledgeShadowedTopic {
  topic: string;
  path: string;
  shadowedByPath: string;
}

/** GET api/admin/devtools/knowledge-base/status. */
export interface KnowledgeBaseStatus extends CorpusIndexStatus {
  /** Every loaded article, sorted by topic. */
  articles: KnowledgeArticleInfo[];
  shadowedTopics: KnowledgeShadowedTopic[];
  /** The length of the topic list the chat system prompt carries. */
  topicListChars: number;
  contentPathExists: boolean;
}

/** The status shape of each corpus. */
export interface CorpusStatusBySlug {
  'gnollhack-wiki': WikiStatus;
  'nethack-wiki': NetHackWikiStatus;
  'knowledge-base': KnowledgeBaseStatus;
}

export type AnyCorpusStatus = CorpusStatusBySlug[CorpusKindSlug];

/** 202 from a re-index request. */
export interface CorpusReindexStarted {
  jobId: string | null;
}

/** What the real chat tool returned to a search probe, uncapped, with the cap the chat applies. */
export interface CorpusProbeResult {
  toolName: string;
  success: boolean;
  content: string | null;
  error: string | null;
  lengthChars: number;
  maxResultLength: number;
  elapsedMs: number;
}

/** GET api/admin/devtools/knowledge-base/article: the raw text as loaded, front matter included. */
export interface KnowledgeArticleSource {
  topic: string;
  title: string | null;
  source: string;
}

/** Call counts and timings of one tool, or of a corpus's tools together. */
export interface CorpusToolUsageStats {
  calls: number;
  completed: number;
  /** Status `error`, guard refusals included. */
  errors: number;
  other: number;
  /** Refused because the corpus index was not ready. */
  guardRefusals: number;
  p50ExecutionMs: number | null;
  p95ExecutionMs: number | null;
  /** Calls per UTC day of the period, oldest first. */
  daily: number[];
}

export interface CorpusToolUsageTool extends CorpusToolUsageStats {
  tool: string;
  corpus: CorpusKind;
}

/** A corpus's tools together; its percentiles are over all their calls. */
export interface CorpusToolUsageCorpus extends CorpusToolUsageStats {
  corpus: CorpusKind;
  tools: string[];
}

/** The periods the usage endpoint accepts. */
export type CorpusToolUsageDays = 7 | 30 | 90;

/** GET api/admin/devtools/corpus-tool-usage?days=. */
export interface CorpusToolUsage {
  days: number;
  /** Midnight UTC of the period's first day. */
  sinceUtc: string;
  generatedAtUtc: string;
  includesGnollBenchCalls: boolean;
  includesEphemeralSessions: boolean;
  includesSubAgentCalls: boolean;
  /** False: in a confidential session a guard refusal counts as an error, since its error text is encrypted. */
  guardRefusalsIncludeEncryptedSessions: boolean;
  /** Every corpus tool, unused ones included. */
  tools: CorpusToolUsageTool[];
  /** One entry per corpus. */
  corpora: CorpusToolUsageCorpus[];
}

/**
 * A refused corpus request, read from its error body (`{ code, message }` or `{ message }`).
 * `code` is `benchmarkRunActive` or `alreadyRunning` on a re-index 409; `status` is null for a
 * non-HTTP error and 0 when the server could not be reached.
 */
export interface CorpusRequestError {
  status: number | null;
  code: string | null;
  message: string | null;
}

export function readCorpusError(error: unknown): CorpusRequestError {
  if (!(error instanceof HttpErrorResponse)) {
    return { status: null, code: null, message: null };
  }
  const body = error.error as { code?: unknown; message?: unknown } | null;
  const field = (value: unknown) => typeof value === 'string' && value.trim() ? value : null;
  return {
    status: error.status,
    code: body && typeof body === 'object' ? field(body.code) : null,
    message: body && typeof body === 'object' ? field(body.message) : null
  };
}

const NO_CACHE_HEADERS = { 'Cache-Control': 'no-cache', Pragma: 'no-cache', Expires: '0' } as const;

@Injectable({ providedIn: 'root' })
export class AdminDevtoolsService {
  private http = inject(HttpClient);
  private readonly base = '/api/admin/devtools';

  getSourceStatus(): Observable<SourceDashboard> {
    return this.http.get<SourceDashboard>(`${this.base}/source/status`);
  }

  /** 202 with the job id; 409 while a source job runs; 400 when the source path is not configured. */
  startReindex(): Observable<SourceJobStarted> {
    return this.http.post<SourceJobStarted>(`${this.base}/source/reindex`, {});
  }

  /** Same responses as `startReindex`. */
  startMakedefsBuild(): Observable<SourceJobStarted> {
    return this.http.post<SourceJobStarted>(`${this.base}/source/makedefs`, {});
  }

  cancelSourceJob(): Observable<{ canceled: boolean }> {
    return this.http.post<{ canceled: boolean }>(`${this.base}/source/cancel`, {});
  }

  getConfigHealth(): Observable<ConfigHealthReport> {
    return this.http.get<ConfigHealthReport>(`${this.base}/config-health`);
  }

  /** The corpus's status, in the shape of its own subclass. */
  getCorpusStatus<K extends CorpusKindSlug>(kind: K): Observable<CorpusStatusBySlug[K]> {
    return this.http.get<CorpusStatusBySlug[K]>(`${this.base}/${kind}/status`, { headers: NO_CACHE_HEADERS });
  }

  /**
   * 202 with the job id. 409 `benchmarkRunActive` while a GnollBench run executes, unless
   * `acknowledgeActiveBenchmark`; 409 `alreadyRunning` while a pass runs; 400 when the folder is
   * not configured or missing. `readCorpusError` reads the refusal.
   */
  startCorpusReindex(kind: CorpusKindSlug, acknowledgeActiveBenchmark = false): Observable<CorpusReindexStarted> {
    const params = acknowledgeActiveBenchmark ? new HttpParams().set('acknowledgeActiveBenchmark', 'true') : undefined;
    return this.http.post<CorpusReindexStarted>(`${this.base}/${kind}/reindex`, {}, { params });
  }

  /**
   * Runs the corpus's real chat tool; nothing is stored. `filter` is the GnollHack wiki's category;
   * the other corpora's tools take none, so the server ignores it there. 400 for an empty query.
   */
  probeCorpus(kind: CorpusKindSlug, query: string, filter?: string | null): Observable<CorpusProbeResult> {
    const body: { query: string; filter?: string } = { query };
    if (filter) {
      body.filter = filter;
    }
    return this.http.post<CorpusProbeResult>(`${this.base}/${kind}/probe`, body);
  }

  /** 404 when no article has the topic. */
  getKnowledgeArticle(topic: string): Observable<KnowledgeArticleSource> {
    return this.http.get<KnowledgeArticleSource>(`${this.base}/knowledge-base/article`, {
      params: new HttpParams().set('topic', topic),
      headers: NO_CACHE_HEADERS
    });
  }

  /** The chat's corpus tool usage over the last 7, 30 or 90 UTC days; the server caches it for 5 minutes. */
  getCorpusToolUsage(days: CorpusToolUsageDays): Observable<CorpusToolUsage> {
    return this.http.get<CorpusToolUsage>(`${this.base}/corpus-tool-usage`, {
      params: new HttpParams().set('days', String(days)),
      headers: NO_CACHE_HEADERS
    });
  }
}
