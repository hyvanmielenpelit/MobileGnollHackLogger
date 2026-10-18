import type {
  AnyCorpusStatus,
  CorpusIndexJob,
  CorpusIndexPhase,
  CorpusIndexStatus,
  KnowledgeBaseStatus,
  NetHackWikiStatus
} from '../../../services/admin-devtools.service';
import { formatNumber, formatTime, parseTimestamp } from './devtools-format';
import type { HealthStatus } from './health-status/health-status.component';

/** The status banner of a corpus dashboard: the tone, the status word and one sentence. */
export interface CorpusBanner {
  status: HealthStatus;
  word: string;
  sentence: string;
}

/** The phases of a corpus pass after `Idle`, in order, with their names. */
export const CORPUS_STAGES: ReadonlyArray<{ key: CorpusIndexPhase; name: string }> = [
  { key: 'ReadingFiles', name: 'Reading files' },
  { key: 'BuildingIndex', name: 'Building index' },
  { key: 'Publishing', name: 'Publishing index' }
];

/** The name of a pass phase, or null for `Idle`. */
export function corpusStageName(phase: CorpusIndexPhase): string | null {
  return CORPUS_STAGES.find(stage => stage.key === phase)?.name ?? null;
}

/** The finished passes, newest first by finish (else start) time. */
export function sortCorpusJobs(jobs: readonly CorpusIndexJob[]): CorpusIndexJob[] {
  const time = (job: CorpusIndexJob) => parseTimestamp(job.finishedAtUtc ?? job.startedAtUtc)?.getTime() ?? 0;
  return [...jobs].sort((a, b) => time(b) - time(a));
}

/** The name of the manual action in a sentence: *Reload articles* for the knowledge base, else *Re-index*. */
export function corpusActionName(status: CorpusIndexStatus): string {
  return status.kind === 'KnowledgeBase' ? 'Reload articles' : 'Re-index';
}

/**
 * The banner of a corpus dashboard. The first matching condition wins: not configured, the folder
 * missing, a pass running, the last pass failed, no index, not a Git working tree, out of date, the
 * NetHack tree regenerated, unreadable files, then ready. `corpusLabel` names the corpus in running
 * text, such as "GnollHack wiki".
 */
export function corpusBanner(status: AnyCorpusStatus | CorpusIndexStatus, corpusLabel: string): CorpusBanner {
  const key = status.configKey;
  const kb = status.kind === 'KnowledgeBase';
  const tools = kb ? 'tool' : 'tools';

  if (!status.configured && !status.path) {
    return {
      status: 'warning', word: 'Not configured',
      sentence: `${key} is not set, so the ${corpusLabel} ${kb ? 'tool has' : 'tools have'} nothing to search.`
    };
  }
  if (!status.configured) {
    return status.pathExists
      ? {
        status: 'warning', word: 'Using default path',
        sentence: `${key} is not set; the service reads the built-in default ${status.path}.`
      }
      : {
        status: 'error', word: 'Folder missing',
        sentence: `${key} is not set, and the built-in default ${status.path} does not exist.`
      };
  }
  if (!status.pathExists) {
    return {
      status: 'error', word: 'Folder missing',
      sentence: `The ${key} folder does not exist on the server.`
    };
  }

  const job = status.currentJob;
  if (job) {
    return { status: 'running', word: 'Indexing', sentence: runningSentence(job) };
  }

  const last = sortCorpusJobs(status.recentJobs)[0] ?? null;
  if (last && last.outcome === 'Failed') {
    return {
      status: 'error', word: 'Last pass failed',
      sentence: last.message || 'See Recent activity for the pass.'
    };
  }

  const index = status.index;
  if (!index) {
    return {
      status: 'warning', word: 'Not indexed',
      sentence: `No index has been published; the ${tools} answer${kb ? 's' : ''} that indexing is in progress.`
    };
  }

  if (status.refreshMode === 'GitPoll' && !status.isGitRepository) {
    return {
      status: 'warning', word: 'Never refreshes',
      sentence: 'The folder is not a Git working tree, so automatic refresh never fires. '
        + `Use ${corpusActionName(status)} after a change.`
    };
  }
  if (status.refreshMode === 'GitPoll' && !status.upToDate) {
    const next = formatTime(status.nextCheckAtUtc);
    return {
      status: 'warning', word: 'Out of date',
      sentence: next
        ? `HEAD has moved since the index was built; the next automatic check is at ${next}.`
        : 'HEAD has moved since the index was built.'
    };
  }
  if (status.kind === 'NetHackWiki' && (status as NetHackWikiStatus).treeChangedSinceIndex) {
    return {
      status: 'warning', word: 'Tree regenerated',
      sentence: 'The wiki was regenerated after this index was built. Re-index to load it.'
    };
  }
  if (index.failedFileCount > 0) {
    const n = index.failedFileCount;
    return {
      status: 'warning', word: 'Some files unreadable',
      sentence: n === 1
        ? '1 file could not be read and is not searchable.'
        : `${formatNumber(n)} files could not be read and are not searchable.`
    };
  }

  if (kb) {
    return knowledgeBaseReady(status as KnowledgeBaseStatus);
  }
  return {
    status: 'ok', word: 'Ready',
    sentence: status.kind === 'NetHackWiki'
      ? 'The index matches the generated tree.'
      : 'The index matches the current HEAD.'
  };
}

function runningSentence(job: CorpusIndexJob): string {
  const stage = corpusStageName(job.phase);
  if (!stage) {
    return 'The pass is starting.';
  }
  if (job.phase === 'ReadingFiles' && job.filesTotal !== null && job.filesTotal > 0) {
    return `Stage: ${stage} (${formatNumber(job.filesRead)} of ${formatNumber(job.filesTotal)}).`;
  }
  return `Stage: ${stage}.`;
}

function knowledgeBaseReady(status: KnowledgeBaseStatus): CorpusBanner {
  const count = status.articles?.length ?? status.index?.fileCount ?? 0;
  if (count === 0) {
    return {
      status: 'warning', word: 'No articles',
      sentence: status.contentPathExists === false
        ? `The ${status.configKey} folder has no Content folder, so no article is loaded.`
        : 'The Content folder holds no article.'
    };
  }
  return {
    status: 'ok', word: 'Ready',
    sentence: count === 1 ? 'The one article is loaded.' : `All ${formatNumber(count)} articles are loaded.`
  };
}
