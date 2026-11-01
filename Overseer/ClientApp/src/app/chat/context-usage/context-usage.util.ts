import { ChatContextBreakdown, ChatContextUsage } from '../../services/chat.service';
import { UserAiModel } from '../../services/settings.service';

export type ContextViewState = 'measured' | 'projected' | 'unknownWindow';
export type ContextSeverity = 'normal' | 'elevated' | 'critical';

export interface ContextView {
  state: ContextViewState;
  /** Null when no window is known for the selected model, so no percentage is claimed. */
  percent: number | null;
  usedTokens: number;
  windowTokens: number | null;
  inputLimitTokens: number | null;
  severity: ContextSeverity;
  willTruncate: boolean;
  /** The selected model's name, or the measured model's when none is selected. */
  modelName: string;
}

export interface ContextBreakdownRow {
  key: string;
  label: string;
  tokens: number;
  percent: number;
  /** A share of the exact total rather than a measured figure. */
  estimated: boolean;
}

export type ModelSwitchCacheEffect = 'none' | 'partial' | 'cold';

export interface ModelSwitchImpact {
  cache: ModelSwitchCacheEffect;
  truncates: boolean;
  /** About how many tokens of the chat the target has room for; null when nothing predicts it. */
  keptTokens: number | null;
}

export const WARN_MODEL_SWITCH_STORAGE_KEY = 'overseer_warn_model_switch';

const SEVERITY_CRITICAL_WINDOW_PERCENT = 90;
const SEVERITY_ELEVATED_WINDOW_PERCENT = 70;
const SEVERITY_ELEVATED_LIMIT_PERCENT = 85;

export function contextUsagePercent(usage: ChatContextUsage | null | undefined): number {
  if (!usage || usage.windowTokens <= 0) return 0;
  return percentOf(usage.usedTokens, usage.windowTokens);
}

/** Structured tooltip content. One string per rendered line. */
export function contextUsageTooltipLines(usage: ChatContextUsage | null | undefined): string[] {
  if (!usage) return [];
  const lines = [
    `${usage.promptTokens.toLocaleString()} prompt + ${usage.outputTokens.toLocaleString()} output tokens`,
    `of a ${usage.windowTokens.toLocaleString()}-token context window`
  ];
  if (usage.inputLimitTokens) {
    lines.push(`History is truncated above ${usage.inputLimitTokens.toLocaleString()} input tokens.`);
  }
  if (usage.modelDisplayName) {
    lines.push(`Measured on ${usage.modelDisplayName} at the last reply.`);
  }
  return lines;
}

/** The numbers and the percentage. The tooltip carries the explanation. */
export function contextUsageValueText(usage: ChatContextUsage | null | undefined): string {
  if (!usage) return '';
  return `${usage.usedTokens.toLocaleString()} of ${usage.windowTokens.toLocaleString()} tokens, ${contextUsagePercent(usage)}%`;
}

/** The breakdown a message or event carries, or undefined when it is missing, malformed or of an unknown version. */
export function readContextBreakdown(raw: unknown): ChatContextBreakdown | undefined {
  if (!raw || typeof raw !== 'object') return undefined;
  const b = raw as Record<string, unknown>;
  if (b['version'] !== 1) return undefined;
  const fields = ['systemPrompt', 'toolDefinitions', 'gameContext', 'history', 'documents', 'images', 'toolActivity', 'cacheRead', 'cacheWrite', 'reasoning'];
  return fields.every(f => typeof b[f] === 'number' && Number.isFinite(b[f]) && (b[f] as number) >= 0)
    ? raw as ChatContextBreakdown
    : undefined;
}

/**
 * The measurement as it applies to the selected model. A measurement stands only while the selected
 * model has the same provider, model id and window, which also catches a configuration edited to another
 * model. A row with no stored provider or model compares by window alone, so it never shows a false projection.
 */
export function contextView(usage: ChatContextUsage, selected: UserAiModel | null | undefined): ContextView {
  const selectedName = selected?.displayName || selected?.modelId;
  const measuredName = usage.modelDisplayName || usage.modelId || '';
  const measuredLimit = usage.inputLimitTokens ?? null;

  if (!selected || isMeasuredOn(usage, selected)) {
    const percent = contextUsagePercent(usage);
    return {
      state: 'measured',
      percent,
      usedTokens: usage.usedTokens,
      windowTokens: usage.windowTokens,
      inputLimitTokens: measuredLimit,
      severity: severityOf(percent, usage.usedTokens, measuredLimit),
      willTruncate: false,
      modelName: selectedName || measuredName
    };
  }

  const windowTokens = selected.contextWindowTokens;
  if (!windowTokens) {
    return {
      state: 'unknownWindow',
      percent: null,
      usedTokens: usage.usedTokens,
      windowTokens: null,
      inputLimitTokens: selected.inputLimitTokens ?? null,
      severity: 'normal',
      willTruncate: false,
      modelName: selectedName || measuredName
    };
  }

  const inputLimit = selected.inputLimitTokens ?? windowTokens;
  const usedTokens = Math.min(usage.usedTokens, inputLimit);
  const percent = percentOf(usedTokens, windowTokens);
  return {
    state: 'projected',
    percent,
    usedTokens,
    windowTokens,
    inputLimitTokens: inputLimit,
    severity: severityOf(percent, usedTokens, inputLimit),
    willTruncate: usage.usedTokens > inputLimit,
    modelName: selectedName || measuredName
  };
}

function isMeasuredOn(usage: ChatContextUsage, selected: UserAiModel): boolean {
  const sameWindow = usage.windowTokens === (selected.contextWindowTokens ?? null);
  if (!usage.provider || !usage.modelId) return sameWindow;
  return sameWindow && sameText(usage.provider, selected.provider) && usage.modelId === selected.modelId;
}

function severityOf(percent: number, usedTokens: number, inputLimit: number | null): ContextSeverity {
  if (percent >= SEVERITY_CRITICAL_WINDOW_PERCENT) return 'critical';
  const nearLimit = !!inputLimit && usedTokens / inputLimit * 100 >= SEVERITY_ELEVATED_LIMIT_PERCENT;
  return percent >= SEVERITY_ELEVATED_WINDOW_PERCENT || nearLimit ? 'elevated' : 'normal';
}

/** What the panel lists, in a fixed order, measured against the measured model's window. Zero rows are left out. */
export function contextBreakdownRows(usage: ChatContextUsage): ContextBreakdownRow[] {
  const b = usage.breakdown;
  const free = Math.max(0, usage.windowTokens - usage.usedTokens);
  const rows: [string, string, number, boolean][] = b
    ? [
        ['systemPrompt', 'System prompt', b.systemPrompt, true],
        ['toolDefinitions', 'Tool definitions', b.toolDefinitions, true],
        ['gameContext', 'Game context', b.gameContext, true],
        ['history', 'Conversation', b.history, true],
        ['documents', 'Documents', b.documents, true],
        ['images', 'Images', b.images, true],
        ['toolActivity', 'Tool activity', b.toolActivity, true],
        ['latestReply', 'Latest reply', usage.outputTokens, false],
        ['free', 'Free space', free, false]
      ]
    : [
        ['prompt', 'Prompt', usage.promptTokens, false],
        ['latestReply', 'Output', usage.outputTokens, false],
        ['free', 'Free space', free, false]
      ];

  return rows
    .filter(([, , tokens]) => tokens > 0)
    .map(([key, label, tokens, estimated]) => ({
      key,
      label,
      tokens,
      estimated,
      percent: usage.windowTokens > 0 ? Math.round(tokens / usage.windowTokens * 1000) / 10 : 0
    }));
}

/**
 * What changing the model costs. Confidential and incognito chats send no cache controls, so they have no
 * cached copy to lose. Two system configurations count as different funding, since the client cannot tell
 * whether they share a key. Truncation is predicted whatever the cache does.
 */
export function modelSwitchImpact(
  usage: ChatContextUsage | null | undefined,
  current: UserAiModel | null | undefined,
  target: UserAiModel,
  flags: { confidential: boolean; ephemeral: boolean }
): ModelSwitchImpact {
  const limit = target.inputLimitTokens ?? target.contextWindowTokens ?? null;
  const truncates = !!usage && !!limit && usage.usedTokens > limit;
  return {
    cache: flags.confidential || flags.ephemeral ? 'none' : cacheEffect(current, target),
    truncates,
    keptTokens: truncates ? limit : null
  };
}

function cacheEffect(current: UserAiModel | null | undefined, target: UserAiModel): ModelSwitchCacheEffect {
  if (!current) return 'cold';

  const sameFunding = !!current.isSystem === !!target.isSystem && (!current.isSystem || current.id === target.id);
  if (!sameFunding || !sameText(current.provider, target.provider) || current.modelId !== target.modelId) {
    return 'cold';
  }

  const sameThinking = (current.thinkingLevel ?? '') === (target.thinkingLevel ?? '')
    && (current.reasoningMode ?? '') === (target.reasoningMode ?? '')
    && (current.reasoningSummary ?? '') === (target.reasoningSummary ?? '')
    && (current.serviceTier ?? '') === (target.serviceTier ?? '');
  return sameThinking ? 'none' : 'partial';
}

const FINGERPRINT_FIELDS: [keyof UserAiModel, string][] = [
  ['provider', 'Provider'],
  ['modelId', 'Model'],
  ['thinkingLevel', 'Thinking'],
  ['reasoningMode', 'Reasoning mode'],
  ['reasoningSummary', 'Reasoning summary'],
  ['serviceTier', 'Service tier'],
  ['maxInputTokens', 'Max input tokens'],
  ['maxOutputTokens', 'Max output tokens'],
  ['inputLimitTokens', 'History limit']
];

/** The settings of a model that change what the next reply does. */
export function modelFingerprint(model: UserAiModel): string {
  return FINGERPRINT_FIELDS.map(([field]) => model[field] ?? '').join('|');
}

/** Each fingerprinted setting that differs, as `Thinking High → Low`. */
export function describeModelChanges(previous: UserAiModel, next: UserAiModel): string[] {
  return FINGERPRINT_FIELDS
    .filter(([field]) => (previous[field] ?? '') !== (next[field] ?? ''))
    .map(([field, label]) => `${label} ${previous[field] ?? 'Default'} → ${next[field] ?? 'Default'}`);
}

export function readWarnModelSwitch(): boolean {
  try {
    return localStorage.getItem(WARN_MODEL_SWITCH_STORAGE_KEY) !== 'false';
  } catch {
    return true;
  }
}

export function writeWarnModelSwitch(warn: boolean): void {
  try {
    localStorage.setItem(WARN_MODEL_SWITCH_STORAGE_KEY, String(warn));
  } catch {
    /* Storage unavailable: the warning keeps its default. */
  }
}

function percentOf(tokens: number, windowTokens: number): number {
  return Math.min(100, Math.max(0, Math.round(tokens / windowTokens * 100)));
}

function sameText(a: string | null | undefined, b: string | null | undefined): boolean {
  return (a ?? '').toLowerCase() === (b ?? '').toLowerCase();
}
