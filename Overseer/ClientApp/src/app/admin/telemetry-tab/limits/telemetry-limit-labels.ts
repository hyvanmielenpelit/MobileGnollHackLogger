import {
  ChatTurnLimitEventDetail,
  ChatTurnLimitEventSummary,
  ChatTurnLimitPromptState
} from '../../../services/admin.service';
import { formatCost } from '../../benchmark/batteries/battery.models';
import { formatInteger } from '../../benchmark/shared/provider-availability/provider-availability';
import { formatDuration } from '../../devtools-tab/shared/devtools-format';
import { providerLabel, wordsLabel } from '../../../utils/display-labels.util';

// Readable labels for the coded values of the AI Telemetry tab's Limits view. The stored values stay in the
// database and the API. Pure: no Angular, no HTTP.

const NO_VALUE = '—';

/** Every `TurnLimitCodes` value (`Overseer/Services/Agents/TurnLimitHit.cs`), in the order the Reason facet lists them. */
export const TURN_LIMIT_CODES = [
  'no_final_answer', 'research_rounds', 'model_calls', 'tool_budget', 'tool_output', 'answer_length', 'time_limit',
  'provider_busy', 'provider_overloaded', 'provider_timeout', 'provider_error', 'connection_lost',
  'api_key_rejected', 'api_key_balance'
] as const;

export type TurnLimitCode = typeof TURN_LIMIT_CODES[number];

const CODE_LABELS: Readonly<Record<TurnLimitCode, string>> = {
  no_final_answer: 'No final answer',
  research_rounds: 'Research rounds',
  model_calls: 'AI calls',
  tool_budget: 'Chat research budget',
  tool_output: 'Tool output cut',
  answer_length: 'Answer length',
  time_limit: 'Time limit',
  provider_busy: 'Provider busy',
  provider_overloaded: 'Provider overloaded',
  provider_timeout: 'Provider timeout',
  provider_error: 'Provider error',
  connection_lost: 'Connection lost',
  api_key_rejected: 'API key rejected',
  api_key_balance: 'API key without credit'
};

/** A limit code's label; an unknown code reads in words. */
export function limitCodeLabel(code: string | null | undefined): string {
  const text = (code ?? '').trim();
  return Object.hasOwn(CODE_LABELS, text) ? CODE_LABELS[text as TurnLimitCode] : wordsLabel(text);
}

export const TURN_LIMIT_SEVERITIES: readonly string[] = ['error', 'warning'];

/** A severity's label; an unknown one reads in words. */
export function limitSeverityLabel(severity: string | null | undefined): string {
  if (severity === 'warning') return 'Warning';
  if (severity === 'error') return 'Error';
  return wordsLabel((severity ?? '').trim());
}

/** The noun a limit's numbers count, for the codes that record numbers as counts. */
const COUNT_UNITS: Readonly<Partial<Record<TurnLimitCode, string>>> = {
  research_rounds: 'round',
  model_calls: 'AI call',
  tool_budget: 'research call',
  tool_output: 'character'
};

/**
 * Where the turn stopped: `22 of 22 rounds`, `After 300 seconds`, `Maximum length`. Null for a code that records no
 * numbers, such as a provider failure.
 */
export function limitStoppedAtText(event: Pick<ChatTurnLimitEventSummary, 'code' | 'limitValue' | 'observedValue'>): string | null {
  const { code, limitValue, observedValue } = event;
  if (code === 'answer_length') return 'Maximum length';
  if (code === 'time_limit') {
    const seconds = limitValue ?? observedValue;
    return seconds === null ? null : `After ${formatInteger(seconds)} ${seconds === 1 ? 'second' : 'seconds'}`;
  }
  const unit = Object.hasOwn(COUNT_UNITS, code) ? COUNT_UNITS[code as TurnLimitCode] : undefined;
  const counted = (value: number) => unit ? ` ${unit}${value === 1 ? '' : 's'}` : '';
  if (limitValue !== null && observedValue !== null) {
    return `${formatInteger(observedValue)} of ${formatInteger(limitValue)}${counted(limitValue)}`;
  }
  const value = observedValue ?? limitValue;
  return value === null ? null : `${formatInteger(value)}${counted(value)}`;
}

/** The reference the user sees under the notice: `L-1234`. */
export function limitReferenceText(id: number): string {
  return `L-${id}`;
}

export function limitUserLabel(event: Pick<ChatTurnLimitEventSummary, 'userName'>): string {
  return event.userName?.trim() || 'Unknown user';
}

/** The provider's name and the model's display name, else its id; a dash with neither. */
export function limitModelText(event: Pick<ChatTurnLimitEventSummary, 'provider' | 'modelDisplayName' | 'requestedModelId'>): string {
  const parts = [providerLabel(event.provider), event.modelDisplayName?.trim() || event.requestedModelId].filter(part => !!part);
  return parts.length > 0 ? parts.join(' · ') : NO_VALUE;
}

/** Why a prompt cannot be read, in one sentence; empty when it can. */
export function limitPromptUnavailableText(state: ChatTurnLimitPromptState): string {
  switch (state) {
    case 'confidential': return 'The prompt is withheld because this chat is confidential.';
    case 'incognito': return 'The prompt is not available because an incognito chat is never recorded.';
    case 'deleted': return 'The prompt is no longer available because the chat or the message has been deleted.';
    default: return '';
  }
}

/** One row of the details dialog's facts. */
export interface TelemetryLimitFact {
  label: string;
  value: string;
  mono?: boolean;
}

/** Every fact of an event, as the details dialog lists them. */
export function limitDetailFacts(detail: ChatTurnLimitEventDetail): TelemetryLimitFact[] {
  const text = (value: string | null | undefined) => value?.trim() || NO_VALUE;
  const whole = (value: number | null | undefined) => value === null || value === undefined ? NO_VALUE : formatInteger(value);
  const yesNo = (value: boolean) => value ? 'Yes' : 'No';
  return [
    { label: 'Reference', value: limitReferenceText(detail.id) },
    { label: 'Reason', value: limitCodeLabel(detail.code) },
    { label: 'Severity', value: detail.severity === 'error' ? 'Error: the turn has no answer' : 'Warning: the turn has an answer' },
    { label: 'Stopped at', value: limitStoppedAtText(detail) ?? NO_VALUE },
    { label: 'User', value: limitUserLabel(detail) },
    { label: 'Key', value: detail.keySource === 'user' ? 'User\'s own key' : 'System AI configuration' },
    { label: 'Confidential', value: yesNo(detail.wasConfidential) },
    { label: 'Incognito', value: yesNo(detail.wasEphemeral) },
    { label: 'Chat', value: detail.chatSessionId === null ? NO_VALUE : String(detail.chatSessionId) },
    { label: 'Model', value: limitModelText(detail) },
    { label: 'Model id', value: text(detail.requestedModelId), mono: true },
    { label: 'Thinking level', value: text(detail.thinkingLevel) },
    { label: 'Duration', value: formatDuration(detail.totalDurationMs) },
    { label: 'Research rounds used', value: whole(detail.iterationsUsed) },
    { label: 'AI calls, including sub-agents', value: whole(detail.modelCallCount) },
    { label: 'Tools, including sub-agents', value: whole(detail.toolCallCount) },
    { label: 'Tool calls blocked', value: whole(detail.toolCallsBlocked) },
    { label: 'Tool time', value: formatDuration(detail.toolTimeMs) },
    { label: 'Permit wait', value: formatDuration(detail.permitWaitMs) },
    { label: 'Backoff wait', value: formatDuration(detail.backoffWaitMs) },
    { label: 'Retries', value: whole(detail.retryAttemptCount) },
    { label: 'Prompt tokens', value: whole(detail.totalPromptTokens) },
    { label: 'Output tokens', value: whole(detail.outputTokens) },
    { label: 'Estimated cost', value: formatCost(detail.estimatedCost) },
    { label: 'Finish reason', value: text(detail.providerFinishReason), mono: true },
    { label: 'Provider error code', value: text(detail.providerErrorCode), mono: true },
    { label: 'HTTP status', value: detail.httpStatus === null ? NO_VALUE : String(detail.httpStatus) },
    { label: 'Assistant phase', value: text(detail.assistantPhase) }
  ];
}
