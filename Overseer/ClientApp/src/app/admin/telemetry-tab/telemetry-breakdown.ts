/**
 * The AI Telemetry tab's breakdown: the server's usage rows grouped into cards by key source, user,
 * provider, model and role group (and thinking level when shown separately), with their sums, the
 * ratios derived from those sums, and the view settings remembered per browser. Pure TypeScript, no
 * Angular.
 */

import type { AiTelemetryKeySource, AiUsageBreakdownRowDto, AiUsageRoleGroup } from '../../services/admin.service';

/** Every role group, in display order. */
export const ROLE_GROUPS: readonly AiUsageRoleGroup[] = ['chat', 'title', 'benchmark', 'other'];

/** The role groups the breakdown shows by default; Other is listed only while it has rows. */
export const NAMED_ROLE_GROUPS: readonly AiUsageRoleGroup[] = ['chat', 'title', 'benchmark'];

export const ROLE_GROUP_LABELS: Readonly<Record<AiUsageRoleGroup, string>> = {
  chat: 'Chat',
  title: 'Title Generation',
  benchmark: 'Benchmark',
  other: 'Other'
};

export const KEY_SOURCES: readonly AiTelemetryKeySource[] = ['system', 'user', 'both'];

export const KEY_SOURCE_LABELS: Readonly<Record<AiTelemetryKeySource, string>> = {
  system: 'System AI configurations',
  user: 'User keys',
  both: 'Both'
};

export function roleGroupLabel(group: AiUsageRoleGroup): string {
  return ROLE_GROUP_LABELS[group] ?? group;
}

/** `Not recorded` for null, `None sent` for an empty level, else the level itself. */
export function thinkingLevelLabel(level: string | null | undefined): string {
  if (level === null || level === undefined) {
    return 'Not recorded';
  }
  return level === '' ? 'None sent' : level;
}

/** The sums a card or the total carries, and the figures derived from them. */
export interface BreakdownMetrics {
  /** The rows summed. */
  rowCount: number;
  requests: number;
  promptTokens: number;
  uncachedInputTokens: number;
  cacheReadTokens: number;
  cacheCreationTokens: number;
  outputTokens: number;
  durationSumMs: number;
  durationCount: number;
  estimatedCost: number;
  pricedRequests: number;
  validAttempts: number;
  providerErrors: number;
  unattributedErrors: number;
  ourSideFailures: number;
  /** Cache read ÷ prompt; null with no prompt tokens. */
  cacheHitRatio: number | null;
  /** Duration sum ÷ duration count; null at count 0. */
  avgDurationMs: number | null;
  /** Requests without a price. */
  unpricedRequests: number;
  /** Priced ÷ requests; null at no request. */
  pricedShare: number | null;
}

/** One card: a key source, user, provider, model, role group and (when separate) thinking level. */
export interface BreakdownCard extends BreakdownMetrics {
  key: string;
  keySource: 'system' | 'user';
  userId: string | null;
  userName: string | null;
  provider: string;
  modelId: string;
  modelName: string | null;
  roleGroup: AiUsageRoleGroup;
  /** The level as recorded ("" none sent, null not recorded); undefined while levels are grouped. */
  thinkingLevel: string | null | undefined;
}

export interface BreakdownOptions {
  roles: readonly AiUsageRoleGroup[];
  separateThinkingLevels: boolean;
}

export interface Breakdown {
  cards: BreakdownCard[];
  /** The sums over every card shown. */
  total: BreakdownMetrics;
}

type Sums = Omit<BreakdownMetrics, 'cacheHitRatio' | 'avgDurationMs' | 'unpricedRequests' | 'pricedShare'>;

function emptySums(): Sums {
  return {
    rowCount: 0, requests: 0, promptTokens: 0, uncachedInputTokens: 0, cacheReadTokens: 0,
    cacheCreationTokens: 0, outputTokens: 0, durationSumMs: 0, durationCount: 0, estimatedCost: 0,
    pricedRequests: 0, validAttempts: 0, providerErrors: 0, unattributedErrors: 0, ourSideFailures: 0
  };
}

function addRow(sums: Sums, row: AiUsageBreakdownRowDto | Sums): void {
  sums.rowCount += 'rowCount' in row ? row.rowCount : 1;
  sums.requests += row.requests;
  sums.promptTokens += row.promptTokens;
  sums.uncachedInputTokens += row.uncachedInputTokens;
  sums.cacheReadTokens += row.cacheReadTokens;
  sums.cacheCreationTokens += row.cacheCreationTokens;
  sums.outputTokens += row.outputTokens;
  sums.durationSumMs += row.durationSumMs;
  sums.durationCount += row.durationCount;
  sums.estimatedCost += row.estimatedCost;
  sums.pricedRequests += row.pricedRequests;
  sums.validAttempts += row.validAttempts;
  sums.providerErrors += row.providerErrors;
  sums.unattributedErrors += row.unattributedErrors;
  sums.ourSideFailures += row.ourSideFailures;
}

function derive(sums: Sums): BreakdownMetrics {
  return {
    ...sums,
    cacheHitRatio: sums.promptTokens > 0 ? sums.cacheReadTokens / sums.promptTokens : null,
    avgDurationMs: sums.durationCount > 0 ? sums.durationSumMs / sums.durationCount : null,
    unpricedRequests: Math.max(0, sums.requests - sums.pricedRequests),
    pricedShare: sums.requests > 0 ? sums.pricedRequests / sums.requests : null
  };
}

/** The name a card is titled and sorted by: the model's name, else its id. */
export function cardTitle(card: Pick<BreakdownCard, 'modelName' | 'modelId'>): string {
  return card.modelName?.trim() || card.modelId;
}

function groupKey(row: AiUsageBreakdownRowDto, separateThinkingLevels: boolean): string {
  const parts = [row.keySource, row.keySource === 'user' ? row.userId ?? '' : '', row.provider, row.modelId, row.roleGroup];
  if (separateThinkingLevels) {
    const level = row.thinkingLevel;
    parts.push(level === null || level === undefined ? '\u0000' : '=' + level);
  }
  return parts.join('|');
}

/**
 * Groups the rows of the selected role groups into cards and sums each, sorted by requests (most
 * first), then title, then thinking level; a row with no request is kept.
 */
export function buildBreakdown(rows: readonly AiUsageBreakdownRowDto[], options: BreakdownOptions): Breakdown {
  const roles = new Set(options.roles);
  const groups = new Map<string, { head: Omit<BreakdownCard, keyof BreakdownMetrics>; sums: Sums }>();

  for (const row of rows) {
    if (!roles.has(row.roleGroup)) {
      continue;
    }
    const key = groupKey(row, options.separateThinkingLevels);
    let group = groups.get(key);
    if (!group) {
      group = {
        head: {
          key,
          keySource: row.keySource,
          userId: row.keySource === 'user' ? row.userId ?? null : null,
          userName: row.keySource === 'user' ? row.userName?.trim() || null : null,
          provider: row.provider,
          modelId: row.modelId,
          modelName: row.modelName?.trim() || null,
          roleGroup: row.roleGroup,
          thinkingLevel: options.separateThinkingLevels ? row.thinkingLevel ?? null : undefined
        },
        sums: emptySums()
      };
      groups.set(key, group);
    } else {
      group.head.modelName ??= row.modelName?.trim() || null;
      if (row.keySource === 'user') {
        group.head.userName ??= row.userName?.trim() || null;
      }
    }
    addRow(group.sums, row);
  }

  const cards: BreakdownCard[] = Array.from(groups.values(), g => ({ ...g.head, ...derive(g.sums) }));
  cards.sort((a, b) =>
    b.requests - a.requests
    || cardTitle(a).localeCompare(cardTitle(b), undefined, { sensitivity: 'base', numeric: true })
    || thinkingSortKey(a.thinkingLevel).localeCompare(thinkingSortKey(b.thinkingLevel))
    || a.key.localeCompare(b.key));

  const total = emptySums();
  for (const card of cards) {
    addRow(total, card);
  }
  return { cards, total: derive(total) };
}

/** Not recorded first, then none sent, then the levels by name. */
function thinkingSortKey(level: string | null | undefined): string {
  if (level === null || level === undefined) {
    return '0';
  }
  return level === '' ? '1' : '2' + level.toLowerCase();
}

/** Requests per role group over every row, whatever the selection. */
export function roleCounts(rows: readonly AiUsageBreakdownRowDto[]): Record<AiUsageRoleGroup, number> {
  const counts: Record<AiUsageRoleGroup, number> = { chat: 0, title: 0, benchmark: 0, other: 0 };
  for (const row of rows) {
    if (row.roleGroup in counts) {
      counts[row.roleGroup] += row.requests;
    }
  }
  return counts;
}

/** Estimated cost per role group over every row, whatever the selection. */
export function roleCosts(rows: readonly AiUsageBreakdownRowDto[]): Record<AiUsageRoleGroup, number> {
  const costs: Record<AiUsageRoleGroup, number> = { chat: 0, title: 0, benchmark: 0, other: 0 };
  for (const row of rows) {
    if (row.roleGroup in costs) {
      costs[row.roleGroup] += row.estimatedCost;
    }
  }
  return costs;
}

// --- View settings ---------------------------------------------------------------------------

/** Where the breakdown's view settings and the Keys filter are kept, per browser. */
export const TELEMETRY_BREAKDOWN_STORAGE_KEY = 'overseer.admin.telemetry.breakdown';

const VIEW_VERSION = 1;

export interface TelemetryBreakdownView {
  roles: AiUsageRoleGroup[];
  separateThinkingLevels: boolean;
  keys: AiTelemetryKeySource;
}

/** The three named role groups, levels grouped, system configurations. */
export function defaultBreakdownView(): TelemetryBreakdownView {
  return { roles: [...NAMED_ROLE_GROUPS], separateThinkingLevels: false, keys: 'system' };
}

/**
 * The stored view; the default when nothing is stored, the record is unreadable, of another
 * version, or storage is unavailable. Unknown role groups are dropped, and an invalid field falls
 * back to its default alone.
 */
export function readBreakdownView(storage: Storage | null = safeLocalStorage()): TelemetryBreakdownView {
  const fallback = defaultBreakdownView();
  try {
    const raw = storage?.getItem(TELEMETRY_BREAKDOWN_STORAGE_KEY);
    if (!raw) {
      return fallback;
    }
    const parsed = JSON.parse(raw) as Partial<Record<keyof TelemetryBreakdownView | 'version', unknown>> | null;
    if (!parsed || typeof parsed !== 'object' || parsed.version !== VIEW_VERSION) {
      return fallback;
    }
    const roles = Array.isArray(parsed.roles)
      ? ROLE_GROUPS.filter(group => (parsed.roles as unknown[]).includes(group))
      : fallback.roles;
    const separateThinkingLevels = typeof parsed.separateThinkingLevels === 'boolean'
      ? parsed.separateThinkingLevels
      : fallback.separateThinkingLevels;
    const keys = KEY_SOURCES.find(k => k === parsed.keys) ?? fallback.keys;
    return { roles, separateThinkingLevels, keys };
  } catch {
    return fallback;
  }
}

/** Stores the view as `{ version: 1, roles, separateThinkingLevels, keys }`; a failure is ignored. */
export function writeBreakdownView(view: TelemetryBreakdownView, storage: Storage | null = safeLocalStorage()): void {
  try {
    storage?.setItem(TELEMETRY_BREAKDOWN_STORAGE_KEY, JSON.stringify({
      version: VIEW_VERSION,
      roles: view.roles,
      separateThinkingLevels: view.separateThinkingLevels,
      keys: view.keys
    }));
  } catch {
    // Storage full or unavailable: the view is simply not remembered.
  }
}

function safeLocalStorage(): Storage | null {
  try {
    return typeof localStorage === 'undefined' ? null : localStorage;
  } catch {
    return null;
  }
}
