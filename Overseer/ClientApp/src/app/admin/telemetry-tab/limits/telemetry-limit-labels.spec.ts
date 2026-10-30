import { ChatTurnLimitEventDetail } from '../../../services/admin.service';
import {
  TURN_LIMIT_CODES,
  limitCodeLabel,
  limitDetailFacts,
  limitModelText,
  limitPromptUnavailableText,
  limitReferenceText,
  limitSeverityLabel,
  limitStoppedAtText,
  limitUserLabel
} from './telemetry-limit-labels';

/** Every value of `TurnLimitCodes` in `Overseer/Services/Agents/TurnLimitHit.cs`, with its label. */
const EXPECTED_LABELS: Readonly<Record<string, string>> = {
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

const stopped = (code: string, limitValue: number | null, observedValue: number | null) =>
  limitStoppedAtText({ code, limitValue, observedValue });

function detail(overrides: Partial<ChatTurnLimitEventDetail> = {}): ChatTurnLimitEventDetail {
  return {
    id: 1234, occurredUtc: '2026-10-30T12:00:00Z', code: 'research_rounds', severity: 'error', userId: 'u1', userName: 'Alice',
    wasConfidential: false, wasEphemeral: false, keySource: 'system', provider: 'Anthropic', requestedModelId: 'claude-x',
    modelDisplayName: 'Claude X', limitValue: 22, observedValue: 22, totalDurationMs: 4200, toolCallCount: 31,
    thinkingLevel: 'high', iterationsUsed: 22, modelCallCount: 23, toolCallsBlocked: 0, toolTimeMs: 850, permitWaitMs: 0,
    backoffWaitMs: 0, retryAttemptCount: 1, totalPromptTokens: 2050, outputTokens: 12, estimatedCost: 0.5,
    providerFinishReason: null, providerErrorCode: null, httpStatus: null, assistantPhase: null, chatSessionId: 20494,
    promptState: 'available',
    ...overrides
  };
}

describe('telemetry-limit-labels', () => {
  describe('limitCodeLabel', () => {
    it('labels every code the server writes, tool_output included, and the list holds no other', () => {
      expect([...TURN_LIMIT_CODES].sort()).toEqual(Object.keys(EXPECTED_LABELS).sort());
      for (const [code, label] of Object.entries(EXPECTED_LABELS)) {
        expect(limitCodeLabel(code), code).toBe(label);
      }
    });

    it('reads an unknown code in words, never an inherited property', () => {
      expect(limitCodeLabel('new_limit')).toBe('New limit');
      expect(limitCodeLabel('constructor')).toBe('Constructor');
      expect(limitCodeLabel(null)).toBe('');
    });
  });

  it('labels the severities', () => {
    expect(limitSeverityLabel('warning')).toBe('Warning');
    expect(limitSeverityLabel('error')).toBe('Error');
  });

  describe('limitStoppedAtText', () => {
    it('reads the counted limits with their nouns', () => {
      expect(stopped('research_rounds', 22, 22)).toBe('22 of 22 rounds');
      expect(stopped('research_rounds', 1, 1)).toBe('1 of 1 round');
      expect(stopped('model_calls', 40, 40)).toBe('40 of 40 AI calls');
      expect(stopped('tool_budget', 200, 200)).toBe('200 of 200 research calls');
      expect(stopped('tool_output', 30000, 30000)).toBe('30,000 of 30,000 characters');
    });

    it('reads the time limit and the answer length as words', () => {
      expect(stopped('time_limit', 300, 300)).toBe('After 300 seconds');
      expect(stopped('time_limit', null, null)).toBeNull();
      expect(stopped('answer_length', null, null)).toBe('Maximum length');
    });

    it('has nothing to say for a code that records no numbers', () => {
      expect(stopped('provider_error', null, null)).toBeNull();
      expect(stopped('no_final_answer', null, null)).toBeNull();
    });

    it('reads a lone number, and an unknown code without a noun', () => {
      expect(stopped('research_rounds', null, 5)).toBe('5 rounds');
      expect(stopped('new_limit', 4, 3)).toBe('3 of 4');
    });
  });

  it('reads the reference, the user and the model', () => {
    expect(limitReferenceText(1234)).toBe('L-1234');
    expect(limitUserLabel({ userName: ' Alice ' })).toBe('Alice');
    expect(limitUserLabel({ userName: null })).toBe('Unknown user');
    expect(limitModelText({ provider: 'Anthropic', modelDisplayName: 'Claude X', requestedModelId: 'claude-x' })).toBe('Anthropic · Claude X');
    expect(limitModelText({ provider: null, modelDisplayName: null, requestedModelId: 'claude-x' })).toBe('claude-x');
    expect(limitModelText({ provider: null, modelDisplayName: null, requestedModelId: null })).toBe('—');
  });

  it('says why a prompt is not available, in one sentence per state', () => {
    expect(limitPromptUnavailableText('available')).toBe('');
    for (const state of ['confidential', 'incognito', 'deleted'] as const) {
      expect(limitPromptUnavailableText(state)).toMatch(/^The prompt .+\.$/);
    }
  });

  it('lists every fact of an event, a dash for what it lacks', () => {
    const facts = Object.fromEntries(limitDetailFacts(detail()).map(fact => [fact.label, fact.value]));

    expect(facts['Reference']).toBe('L-1234');
    expect(facts['Reason']).toBe('Research rounds');
    expect(facts['Severity']).toBe('Error: the turn has no answer');
    expect(facts['Stopped at']).toBe('22 of 22 rounds');
    expect(facts['Key']).toBe('System AI configuration');
    expect(facts['Chat']).toBe('20494');
    expect(facts['Duration']).toBe('4.2 s');
    expect(facts['Tools, including sub-agents']).toBe('31');
    expect(facts['Prompt tokens']).toBe('2,050');
    expect(facts['Estimated cost']).toBe('$0.5000');
    expect(facts['HTTP status']).toBe('—');
    expect(facts['Finish reason']).toBe('—');
    expect(Object.keys(facts).length).toBe(limitDetailFacts(detail()).length);
  });
});
