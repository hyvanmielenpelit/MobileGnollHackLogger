import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
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
}
