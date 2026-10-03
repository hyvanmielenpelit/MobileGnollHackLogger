import {
  BenchmarkBatteryRunDto,
  BenchmarkRunAnswerDto,
  BenchmarkRunSummaryDto,
  BenchmarkModelComparisonPricingBasis
} from '../../services/admin-benchmark.service';
import { CardListSort } from '../../shared/data-table/card-list-state';

// Types and constants the Benchmark tab's components and state services share.

export const COPY_STATUS_MS = 3000;

/** The run report's tabs, in order. */
export const RUN_REPORT_TABS = [
  { key: 'summary', label: 'Summary' },
  { key: 'integrity', label: 'Integrity' },
  { key: 'synthesis', label: 'Synthesis' },
  { key: 'questions', label: 'Questions' },
  { key: 'difficulty', label: 'Difficulty' },
  { key: 'tools', label: 'Tools' },
  { key: 'cost', label: 'Cost' },
  { key: 'configuration', label: 'Configuration' },
  { key: 'reports', label: 'AI Reports' },
  { key: 'calibration', label: 'Calibration' },
  { key: 'paired', label: 'Paired Test' }
] as const;

export type RunReportTabKey = typeof RUN_REPORT_TABS[number]['key'];

/** Where the run report's chosen tab is remembered, per viewer. */
export const RUN_REPORT_TAB_STORAGE_KEY = 'overseer.benchmark.runReport.tab';

/** Where the run report header's Run details open state is remembered, per viewer, as `{ version: 1, detailsOpen }`. */
export const RUN_REPORT_HEADER_STORAGE_KEY = 'overseer.benchmark.runReport.header';

/** Where Run History's Sort by order is remembered, per viewer, as `{ version: 1, sort }`. */
export const RUN_HISTORY_VIEW_STORAGE_KEY = 'overseer.benchmark.runHistory.view';

/** Where Run History's *Show battery member runs* is remembered, per viewer, as `'1'` or `'0'`. */
export const RUN_HISTORY_MEMBERS_STORAGE_KEY = 'overseer.benchmark.runHistory.members';

/** One card of Run History: a single run, or a battery run standing for its members. */
export type HistoryItem =
  | { readonly kind: 'run'; readonly key: string; readonly run: BenchmarkRunSummaryDto }
  | { readonly kind: 'battery'; readonly key: string; readonly battery: BenchmarkBatteryRunDto };

/** The Kind facet's values, in the order it lists them. */
export const RUN_HISTORY_KINDS = ['Single run', 'Battery run'] as const;

/**
 * The orders Run History's Sort by offers. The `id` column is the card's position in
 * `historyItems`, which is newest first, so it stands in for the date.
 */
export const RUN_HISTORY_SORTS: readonly CardListSort[] = [
  { id: 'newest', label: 'Newest first', column: 'id', direction: 'desc' },
  { id: 'oldest', label: 'Oldest first', column: 'id', direction: 'asc' },
  { id: 'intelligence-desc', label: 'Intelligence Index, highest first', column: 'qualityIndex', direction: 'desc' },
  { id: 'speed-desc', label: 'Speed Index, highest first', column: 'speedIndex', direction: 'desc' },
  { id: 'cost-asc', label: 'Cost, lowest first', column: 'estimatedCost', direction: 'asc' },
  { id: 'cost-desc', label: 'Cost, highest first', column: 'estimatedCost', direction: 'desc' },
  { id: 'duration-asc', label: 'Duration, shortest first', column: 'durationMs', direction: 'asc' },
  { id: 'tested-asc', label: 'Tested model (A–Z)', column: 'testedModelDisplayNameUsed', direction: 'asc' },
  { id: 'suite-asc', label: 'Suite (A–Z)', column: 'suiteName', direction: 'asc' }
];

/** The Flags facet's values, in the order it lists them. */
export const RUN_HISTORY_FLAGS = [
  'Degraded answers', 'Unanswered questions', 'Failed at the provider', 'Advisory timing', 'Pricing incomplete', 'None'
] as const;

/** The Changes facet's values, in the order it lists them. */
export const RUN_HISTORY_CHANGES = ['Instrument changed', 'Options changed', 'No change'] as const;

/** The Started facet's ranges. */
export const RUN_HISTORY_STARTED_RANGES: readonly { value: string; label: string; hours: number }[] = [
  { value: '24h', label: 'Last 24 hours', hours: 24 },
  { value: '7d', label: 'Last 7 days', hours: 24 * 7 },
  { value: '30d', label: 'Last 30 days', hours: 24 * 30 }
];

/** The run count the runs endpoint returns at most; Run History says when it holds that many. */
export const RUN_HISTORY_LIMIT = 1000;

/** The battery run count Run History asks for; it says when it holds that many. */
export const BATTERY_RUN_HISTORY_LIMIT = 500;

/** What each instrument label stands for, read after the short label by assistive technology. */
export const FINGERPRINT_LONG_NAMES: Record<BenchmarkFingerprintEntry['label'], string> = {
  PROMPT: 'candidate system prompt',
  GUIDES: 'tool guides',
  KB: 'knowledge base',
  WIKI: 'wiki',
  SRC: 'source code'
};

/** The remembered Run details open state; closed when none is stored, it is unreadable or storage is unavailable. */
export function readStoredRunHeaderDetailsOpen(): boolean {
  try {
    const raw = localStorage.getItem(RUN_REPORT_HEADER_STORAGE_KEY);
    const parsed = raw ? JSON.parse(raw) as { version?: unknown; detailsOpen?: unknown } | null : null;
    return !!parsed && typeof parsed === 'object' && parsed.version === 1 && parsed.detailsOpen === true;
  } catch {
    return false;
  }
}

export const SNAPSHOT_TEXT_EXPORT_FAILED = 'Exported without the snapshot text: it could not be loaded.';

/**
 * The Model Comparison selection, as it is remembered between visits and between sessions.
 *
 * The selection is persisted rather than the comparison: a stored payload would be re-priced stale
 * the moment the catalog moved, and re-issuing the request is cheap next to showing costs that are
 * no longer true.
 */
export interface BenchmarkComparisonSelection {
  runIds: number[];
  groupIds: number[];
  suiteId: number | null;
  pricingBasis: BenchmarkModelComparisonPricingBasis;
}

/**
 * One row of the run progress list: one of the run's answers, under its stored order index and
 * question text, or, during the run's first pass, a suite question with no answer yet. The
 * executor writes an answer row only after the model replies, so a question with no answer row
 * is either dispatched or not: `BenchmarkRunDetailDto.inFlightOrderIndexes` — server-side state
 * kept by `BenchmarkRunManager` — is what tells the two apart. In flight is 'Answering';
 * everything else with no answer is 'Pending'.
 */
export interface BenchmarkRunProgressRow {
  orderIndex: number;
  questionText: string;
  /** The answer this row shows; null or absent for a question the first pass has not answered yet. */
  answer?: BenchmarkRunAnswerDto | null;
  /**
   * Formatted answer status, or 'Answering' while the provider request is in flight, or
   * 'Pending' when the run has not dispatched this question yet, or 'Verifying' /
   * 'SecondOpinion' while an already-scored answer is being re-read by a grading role.
   */
  status: string;
  /** Formatted assessment status, or '' when there is no answer yet. */
  assessmentStatus: string;
  errorMessage: string | null;
}

/**
 * The functional families a benchmark tool belongs to. Mirrors `BenchmarkToolFamily` in
 * `Overseer/Services/Benchmarking/BenchmarkChatTransfer.cs`, which is what the report builder's
 * Tool Routing table classifies against.
 */
export type BenchmarkToolFamilyName =
  'SourceCode' | 'Wiki' | 'StructuredLookup' | 'KnowledgeBase' | 'Other';

/**
 * U1. The one place tool-name membership is written on the client.
 *
 * This is a deliberate mirror of `BenchmarkChatTransfer.ClassifyTool`, and it exists as a single
 * exported constant rather than as lists spelled out at each call site because the report and this
 * screen must classify the same call the same way. Two inline copies of "which tools are source
 * tools" would agree on the day they were written and disagree the first time a tool is added — and
 * the disagreement would surface as an operator reading two different source shares for one run.
 *
 * When a tool is added on the server, it is added here in the same change.
 */
export const BENCHMARK_TOOL_FAMILY_MEMBERSHIP: ReadonlyArray<readonly [BenchmarkToolFamilyName, readonly string[]]> = [
  ['SourceCode', ['source_code_search', 'source_code_view', 'search_definitions',
    'get_function_definition', 'get_constants', 'list_indexed_files']],
  ['Wiki', ['wiki_search', 'wiki_view', 'nethack_wiki_search', 'nethack_wiki_view']],
  ['StructuredLookup', ['monster_lookup', 'item_lookup', 'get_monster_stats', 'get_item_stats']],
  ['KnowledgeBase', ['get_knowledge_article']]
];

/** Display order and labels, matching the report's Tool Routing table exactly. */
export const BENCHMARK_TOOL_FAMILY_LABELS: ReadonlyArray<readonly [BenchmarkToolFamilyName, string]> = [
  ['SourceCode', 'Source Code'],
  ['Wiki', 'Wiki'],
  ['StructuredLookup', 'Structured Lookup'],
  ['KnowledgeBase', 'Knowledge Base'],
  ['Other', 'Other']
];

/** One tool name to its family. Case- and whitespace-insensitive, as the server's switch is. */
export function classifyBenchmarkTool(toolName: string | null | undefined): BenchmarkToolFamilyName {
  const key = (toolName ?? '').trim().toLowerCase();
  for (const [family, members] of BENCHMARK_TOOL_FAMILY_MEMBERSHIP) {
    if (members.includes(key)) return family;
  }
  return 'Other';
}

/** One row of the Tool Routing block: a family, its call count, and its share of the run. */
export interface BenchmarkToolFamilyRow {
  family: BenchmarkToolFamilyName;
  label: string;
  count: number;
  sharePercentage: number;
}

/**
 * U1. Pearson *r* of per-answer source-family share against model time and against quality, with
 * the sample size that produced them. A correlation without its *n* is not a finding.
 */
export interface BenchmarkSourceShareCorrelations {
  modelTimeR: number | null;
  qualityR: number | null;
  sampleSize: number;
}

/** Which pass of a run is executing, as the progress rail and the diagnostics name it. */
export type BenchmarkRunStage = 'answering' | 'verifying' | 'secondopinion' | 'finalizing' | 'terminal';

/** A question filter of the run report. A card is shown when it matches any pressed filter. */
export type RunReportQuestionFilter = 'critical' | 'disputed' | 'disagree' | 'below70' | 'flagged';

/** One item of the run report's Re-run popover; `reason` says why it is unavailable, or is null. */
export interface RunReportRerunAction {
  key: string;
  label: string;
  reason: string | null;
  run: () => void;
}

/** The progress figures the diagnostics capture prints for one run. */
export interface RunDiagnosticsFacts {
  answered: number;
  total: number;
  scored: number;
  failed: BenchmarkRunAnswerDto[];
  gradeable: number;
  terminal: boolean;
  /** Order indexes of the re-run in effect; empty when there is none. */
  rerunScope: number[];
  /** Answers with a claim verification, and with a second verdict, within the re-run scope. */
  verifiedInScope: number;
  secondOpinionInScope: number;
  rows: BenchmarkRunProgressRow[];
}

/** One row of the run report's Run configuration list. */
export interface RunReportConfigRow {
  term: string;
  value: string;
  /** A hash or identifier, set in a monospace face. */
  code?: boolean;
}

/**
 * One line of a run's instrument fingerprint stack: a short visible label, a colour class, the
 * eight-character hash prefix, and the tooltip carrying the corpus name and the full hash.
 *
 * The label is what makes the stack readable in greyscale and to a colour-blind reader, so the
 * colour class only reinforces it. `PROMPT`, `GUIDES`, `KB`, `WIKI` and `SRC` are the same labels
 * the Markdown group report prints, so all the surfaces read identically.
 */
export interface BenchmarkFingerprintEntry {
  label: 'PROMPT' | 'GUIDES' | 'KB' | 'WIKI' | 'SRC';
  cssClass: 'fp-prompt' | 'fp-guides' | 'fp-kb' | 'fp-wiki' | 'fp-source';
  short: string;
  title: string;
}

/** A one-time request from the host page to show a sub-tab and, on Manage Suites, one suite. */
export interface BenchmarkNavigationRequest {
  subTab: string | null;
  suiteId: number | null;
}

/**
 * The run setup an operator last started, remembered across reloads. Exactly the fields that make up a
 * run: not the same-provider acknowledgement, which is a per-run safety gate, and not the
 * difficulty-assessor, retry-assessor, generation-model or calibration-assessor selections, which belong
 * to other workflows on the same screen.
 *
 * Every field is nullable because a stored blob may predate a field, and because every id is re-validated
 * against the currently available list before it is applied.
 */
export interface BenchmarkRunSettings {
  suiteId: number | null;
  testedConfigId: number | null;
  assessorConfigId: number | null;
  /** Panel member B, or null for a single-assessor run. */
  coAssessorConfigId: number | null;
  secondOpinionConfigId: number | null;
  claimVerifierConfigId: number | null;
  /** The model that writes the run's AI-written reports, or null for none. */
  reportWriterConfigId: number | null;
  /** The operator's explicit override, or null to keep following the scoring profile's own default. */
  secondOpinionMode: number | null;
  scoringProfileId: number | null;
  verboseMode: boolean | null;
  /** Whether the candidate may cite source files and lines. Defaults to false when absent. */
  allowSourceCodeReferences: boolean | null;
  runCount: number | null;
  /** Single suite or a battery of suites. Absent restores Single suite. */
  targetKind: 'suite' | 'battery' | null;
  /** The battery a battery run starts from; restored only while it is listed, unarchived and runnable. */
  batteryId: number | null;
  /** Whether a run or series completion plays the chime. Defaults to true when absent. */
  completionSound: boolean | null;
  /** Whether a run or series completion also raises a desktop notification. Defaults to false when absent. */
  completionNotification: boolean | null;
}
