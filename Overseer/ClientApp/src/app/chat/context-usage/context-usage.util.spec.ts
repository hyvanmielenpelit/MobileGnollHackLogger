import { afterEach, describe, expect, it } from 'vitest';

import { ChatContextUsage } from '../../services/chat.service';
import { UserAiModel } from '../../services/settings.service';
import {
  WARN_MODEL_SWITCH_STORAGE_KEY, contextBreakdownRows, contextUsagePercent,
  contextView, describeModelChanges, modelFingerprint, modelSwitchImpact, readContextBreakdown, readWarnModelSwitch, writeWarnModelSwitch
} from './context-usage.util';

const usage = (over: Partial<ChatContextUsage> = {}): ChatContextUsage => ({
  promptTokens: 5000, outputTokens: 250, usedTokens: 5250, windowTokens: 200000,
  inputLimitTokens: 190000, modelDisplayName: 'Claude Y', provider: 'Anthropic', modelId: 'claude-y',
  ...over
});

const model = (over: Partial<UserAiModel> = {}): UserAiModel => ({
  id: 1, provider: 'Anthropic', modelId: 'claude-y', displayName: 'Claude Y',
  contextWindowTokens: 200000, inputLimitTokens: 190000, ...over
});

const breakdown = {
  version: 1, systemPrompt: 2000, toolDefinitions: 1000, gameContext: 0, history: 1500, documents: 300, images: 100,
  toolActivity: 100, cacheRead: 4000, cacheWrite: 100, reasoning: 10
};

const flags = { confidential: false, ephemeral: false };

describe('contextUsagePercent', () => {
  it('should be zero when there is no usage', () => {
    expect(contextUsagePercent(null)).toBe(0);
  });

  it('should round the ratio to a whole percentage', () => {
    expect(contextUsagePercent({ promptTokens: 200000, outputTokens: 4400, usedTokens: 204400, windowTokens: 1000000 })).toBe(20);
  });

  it('should clamp to 100 when the used count exceeds the window', () => {
    // Possible after a mid-chat switch to a model with a smaller window.
    expect(contextUsagePercent({ promptTokens: 300000, outputTokens: 0, usedTokens: 300000, windowTokens: 200000 })).toBe(100);
  });

  it('should be zero rather than infinite when the window is unknown', () => {
    expect(contextUsagePercent({ promptTokens: 100, outputTokens: 0, usedTokens: 100, windowTokens: 0 })).toBe(0);
  });
});

describe('readContextBreakdown', () => {
  it('should accept a current breakdown', () => {
    expect(readContextBreakdown(breakdown)).toEqual(breakdown);
  });

  it.each([
    [undefined], [null], ['text'], [{}], [{ ...breakdown, version: 2 }], [{ ...breakdown, history: 'many' }], [{ ...breakdown, images: -1 }]
  ])('should treat %j as no breakdown', raw => {
    expect(readContextBreakdown(raw)).toBeUndefined();
  });
});

describe('contextView', () => {
  it('should be measured when no model is selected', () => {
    expect(contextView(usage(), undefined).state).toBe('measured');
  });

  it('should be measured on the same provider, model and window', () => {
    const view = contextView(usage(), model());
    expect(view.state).toBe('measured');
    expect(view.usedTokens).toBe(5250);
    expect(view.percent).toBe(3);
    expect(view.willTruncate).toBe(false);
  });

  it('should compare an old row by window alone, so it never shows a false projection', () => {
    expect(contextView(usage({ provider: undefined, modelId: undefined }), model({ modelId: 'other' })).state).toBe('measured');
  });

  it('should project an old row when the window differs', () => {
    expect(contextView(usage({ provider: undefined, modelId: undefined }), model({ contextWindowTokens: 100000 })).state).toBe('projected');
  });

  it('should project onto another model, and predict truncation', () => {
    const view = contextView(usage({ usedTokens: 240000, windowTokens: 1000000 }), model({ modelId: 'small', displayName: 'Small', contextWindowTokens: 200000, inputLimitTokens: 180000 }));
    expect(view.state).toBe('projected');
    expect(view.usedTokens).toBe(180000);
    expect(view.percent).toBe(90);
    expect(view.willTruncate).toBe(true);
    expect(view.modelName).toBe('Small');
  });

  it('should project a configuration edited to another model under one key', () => {
    expect(contextView(usage(), model({ id: 1, modelId: 'edited' })).state).toBe('projected');
  });

  it('should claim no percentage for a model with no catalog window', () => {
    const view = contextView(usage(), model({ modelId: 'custom', contextWindowTokens: null, inputLimitTokens: 100000 }));
    expect(view.state).toBe('unknownWindow');
    expect(view.percent).toBeNull();
    expect(view.severity).toBe('normal');
  });

  it.each([
    [138000, 'normal'],
    [140000, 'elevated'],
    [180000, 'critical']
  ])('should grade %i of 200,000 as %s', (usedTokens, severity) => {
    expect(contextView(usage({ usedTokens, inputLimitTokens: 1000000 }), undefined).severity).toBe(severity);
  });

  it('should raise the severity from the input limit, before the window fills', () => {
    const view = contextView(usage({ usedTokens: 170000, windowTokens: 1000000, inputLimitTokens: 200000 }), undefined);
    expect(view.percent).toBe(17);
    expect(view.severity).toBe('elevated');
  });
});

describe('contextBreakdownRows', () => {
  it('should list the available figures when there is no breakdown', () => {
    expect(contextBreakdownRows(usage()).map(r => [r.label, r.tokens, r.estimated])).toEqual([
      ['Prompt', 5000, false], ['Output', 250, false], ['Free space', 194750, false]
    ]);
  });

  it('should mark the categories estimated, hide zero rows and sum to the window', () => {
    const rows = contextBreakdownRows(usage({ breakdown }));
    expect(rows.find(r => r.key === 'gameContext')).toBeUndefined();
    expect(rows.filter(r => r.estimated).map(r => r.key)).toEqual(['systemPrompt', 'toolDefinitions', 'history', 'documents', 'images', 'toolActivity']);
    expect(rows.find(r => r.key === 'latestReply')?.estimated).toBe(false);
    expect(rows.reduce((sum, r) => sum + r.tokens, 0)).toBe(200000);
  });
});

describe('modelSwitchImpact', () => {
  it('should cost nothing in a confidential or incognito chat', () => {
    const target = model({ modelId: 'other' });
    expect(modelSwitchImpact(usage(), model(), target, { confidential: true, ephemeral: false }).cache).toBe('none');
    expect(modelSwitchImpact(usage(), model(), target, { confidential: false, ephemeral: true }).cache).toBe('none');
  });

  it('should still predict truncation in a confidential chat', () => {
    const impact = modelSwitchImpact(usage({ usedTokens: 240000 }), model(), model({ inputLimitTokens: 180000 }), { confidential: true, ephemeral: false });
    expect(impact.truncates).toBe(true);
    expect(impact.keptTokens).toBe(180000);
  });

  it('should cost nothing when only an unrelated setting differs', () => {
    expect(modelSwitchImpact(usage(), model(), model({ id: 2, maxOutputTokens: 100 }), flags).cache).toBe('none');
  });

  it.each([
    [{ thinkingLevel: 'high' }], [{ reasoningMode: 'auto' }], [{ reasoningSummary: 'detailed' }], [{ serviceTier: 'fast' }]
  ])('should be partial when %j differs', over => {
    expect(modelSwitchImpact(usage(), model(), model({ id: 2, ...over }), flags).cache).toBe('partial');
  });

  it.each([
    [{ modelId: 'other' }], [{ provider: 'Google' }], [{ isSystem: true }]
  ])('should be cold when %j differs', over => {
    expect(modelSwitchImpact(usage(), model(), model({ id: 2, ...over }), flags).cache).toBe('cold');
  });

  it('should treat two system configurations as cold, since they may hold different keys', () => {
    expect(modelSwitchImpact(usage(), model({ id: 1, isSystem: true }), model({ id: 2, isSystem: true }), flags).cache).toBe('cold');
  });

  it('should be cold with no current model', () => {
    expect(modelSwitchImpact(usage(), undefined, model(), flags).cache).toBe('cold');
  });

  it('should predict nothing without a measurement', () => {
    const impact = modelSwitchImpact(null, model(), model({ modelId: 'other', inputLimitTokens: 10 }), flags);
    expect(impact.cache).toBe('cold');
    expect(impact.truncates).toBe(false);
    expect(impact.keptTokens).toBeNull();
  });
});

describe('the model fingerprint', () => {
  it('should change with a setting that matters, and not with the display name', () => {
    expect(modelFingerprint(model({ thinkingLevel: 'low' }))).not.toBe(modelFingerprint(model({ thinkingLevel: 'high' })));
    expect(modelFingerprint(model({ displayName: 'A' }))).toBe(modelFingerprint(model({ displayName: 'B' })));
  });

  it('should describe what changed', () => {
    expect(describeModelChanges(model({ thinkingLevel: 'High' }), model({ thinkingLevel: 'Low', maxOutputTokens: 100 })))
      .toEqual(['Thinking High → Low', 'Max output tokens Default → 100']);
  });
});

describe('the warning preference', () => {
  afterEach(() => localStorage.removeItem(WARN_MODEL_SWITCH_STORAGE_KEY));

  it('should warn by default, and remember being silenced', () => {
    expect(readWarnModelSwitch()).toBe(true);
    writeWarnModelSwitch(false);
    expect(readWarnModelSwitch()).toBe(false);
    writeWarnModelSwitch(true);
    expect(readWarnModelSwitch()).toBe(true);
  });
});
