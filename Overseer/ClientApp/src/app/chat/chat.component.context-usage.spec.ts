import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { ChatComponent } from './chat.component';
import { SettingsService, UserAiModel } from '../services/settings.service';
import { WARN_MODEL_SWITCH_STORAGE_KEY } from './context-usage/context-usage.util';

const claude: UserAiModel = { id: 1, provider: 'Anthropic', modelId: 'claude-y', displayName: 'Claude Y', thinkingLevel: 'High', contextWindowTokens: 1000000, inputLimitTokens: 800000 };
const gemini: UserAiModel = { id: 2, provider: 'Google', modelId: 'gemini-x', displayName: 'Gemini X', contextWindowTokens: 200000, inputLimitTokens: 180000 };

const measurement = (usedTokens: number) => ({
  promptTokens: usedTokens, outputTokens: 0, usedTokens, windowTokens: 1000000, inputLimitTokens: 800000,
  modelDisplayName: 'Claude Y', provider: 'Anthropic', modelId: 'claude-y'
});

describe('ChatComponent: the context read-out and mid-chat model changes', () => {
  let fixture: ComponentFixture<ChatComponent>;
  let component: ChatComponent;
  let settings: SettingsService;

  const nextTask = () => new Promise(resolve => setTimeout(resolve, 0));
  const dialog = () => (fixture.nativeElement as HTMLElement).querySelector('app-model-switch-dialog dialog') as HTMLDialogElement;

  beforeEach(async () => {
    localStorage.removeItem(WARN_MODEL_SWITCH_STORAGE_KEY);
    await TestBed.configureTestingModule({
      imports: [ChatComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])]
    }).compileComponents();
    vi.spyOn(ChatComponent.prototype, 'setupSignalR').mockReturnValue(undefined);
    settings = TestBed.inject(SettingsService);
    fixture = TestBed.createComponent(ChatComponent);
    component = fixture.componentInstance;
    vi.spyOn(component, 'loadSessions').mockReturnValue(undefined);
    component.isLoadingSession = false;
    component.userModels = [claude, gemini];
    component.selectedModelKey = 'u_1';
    component.messages = [{ role: 'assistant', content: 'An answer.', timestampUtc: '2026-11-01T10:00:00Z' }];
    fixture.detectChanges();
    if (dialog()?.open) dialog().close();
  });

  afterEach(() => {
    if (dialog()?.open) dialog().close();
    localStorage.removeItem(WARN_MODEL_SWITCH_STORAGE_KEY);
    fixture.destroy();
    vi.restoreAllMocks();
  });

  describe('requestModelChange', () => {
    it('should ask first in a chat with a reply, and leave the selection alone until answered', async () => {
      component.contextUsage = measurement(29900);
      component.requestModelChange(gemini);
      expect(component.selectedModelKey).toBe('u_1');
      await nextTask();
      expect(dialog().open).toBe(true);
      expect(dialog().textContent).toContain('Gemini X has no cached copy');
      expect(component.selectedModelKey).toBe('u_1');
    });

    it('should open on the next task, so focus returns to the picker trigger the picker refocused', async () => {
      // The chat focuses its prompt shortly after it starts.
      await new Promise(resolve => setTimeout(resolve, 100));
      const trigger = document.createElement('button');
      document.body.appendChild(trigger);
      component.contextUsage = measurement(29900);
      component.requestModelChange(gemini);
      expect(dialog().open).toBe(false);
      trigger.focus();
      await nextTask();
      expect(dialog().open).toBe(true);
      expect(document.activeElement).not.toBe(trigger);
      const closed = new Promise(resolve => dialog().addEventListener('close', resolve, { once: true }));
      dialog().close();
      await closed;
      expect(document.activeElement).toBe(trigger);
      trigger.remove();
    });

    it('should switch only on Switch Model', async () => {
      component.contextUsage = measurement(29900);
      component.requestModelChange(gemini);
      await nextTask();
      component.onModelSwitchDecided({ switchModel: false, dontWarnAgain: false });
      expect(component.selectedModelKey).toBe('u_1');

      component.requestModelChange(gemini);
      await nextTask();
      component.onModelSwitchDecided({ switchModel: true, dontWarnAgain: false });
      expect(component.selectedModelKey).toBe('u_2');
    });

    it('should not ask before the chat has a reply', () => {
      component.messages = [];
      component.requestModelChange(gemini);
      expect(component.selectedModelKey).toBe('u_2');
    });

    it('should not ask in a confidential chat when nothing would be truncated', () => {
      component.isConfidentialSession = true;
      component.contextUsage = measurement(29900);
      component.requestModelChange(gemini);
      expect(component.selectedModelKey).toBe('u_2');
    });

    it('should ask in an old chat with no measurement, with no token figure', async () => {
      component.contextUsage = null;
      component.requestModelChange(gemini);
      await nextTask();
      expect(dialog().open).toBe(true);
      expect(dialog().textContent).toContain('this chat is read at the full input price');
      expect(dialog().textContent).not.toContain('Older messages');
    });

    it('should stop asking about the cache once silenced', async () => {
      component.contextUsage = measurement(29900);
      component.requestModelChange(gemini);
      await nextTask();
      component.onModelSwitchDecided({ switchModel: false, dontWarnAgain: true });
      expect(localStorage.getItem(WARN_MODEL_SWITCH_STORAGE_KEY)).toBe('false');
      dialog().close();

      component.requestModelChange(gemini);
      expect(component.selectedModelKey).toBe('u_2');
    });

    it('should always warn about truncation, even when silenced', async () => {
      localStorage.setItem(WARN_MODEL_SWITCH_STORAGE_KEY, 'false');
      component.contextUsage = measurement(240000);
      component.requestModelChange(gemini);
      expect(component.selectedModelKey).toBe('u_1');
      await nextTask();
      expect(dialog().open).toBe(true);
      expect(dialog().textContent).toContain('Older messages will no longer reach the model');
    });
  });

  describe('labels and measurements from the turn itself', () => {
    const finishTurn = async () => {
      vi.useFakeTimers();
      component.isStreaming = true;
      component.streamingMessage = 'Done.';
      component.processChatEvent({ type: 'done', data: '' } as any);
      vi.advanceTimersByTime(10000);
      vi.useRealTimers();
    };

    beforeEach(() => {
      vi.spyOn(settings, 'getUserModels').mockReturnValue(of([claude, gemini]));
    });

    it('should label the reply from turn_model after the model is changed mid-stream', async () => {
      component.processChatEvent({
        type: 'turn_model',
        data: JSON.stringify({ modelKey: 'u_1', provider: 'Anthropic', modelId: 'claude-y', displayName: 'Claude Y', thinkingLevel: 'low', selectionUnavailable: false })
      } as any);
      component.selectModel(gemini);
      await finishTurn();

      const reply = component.messages[component.messages.length - 1];
      expect(reply.modelDisplayName).toBe('Claude Y');
      expect(reply.thinkingLevel).toBe('low');
      expect(reply.providerUsed).toBe('Anthropic');
      expect(reply.modelUsed).toBe('claude-y');
    });

    it('should fall back to the selected model for a turn that sent no event', async () => {
      await finishTurn();
      expect(component.messages[component.messages.length - 1].modelDisplayName).toBe('Claude Y');
    });

    it('should not let a turn with no usage report inherit the previous measurement', async () => {
      component.contextUsage = measurement(29900);
      await finishTurn();
      expect(component.messages[component.messages.length - 1].contextPromptTokens).toBeUndefined();
    });

    it('should copy this turn\'s measurement and breakdown onto the reply', async () => {
      const breakdown = { version: 1, systemPrompt: 1, toolDefinitions: 1, gameContext: 0, history: 1, documents: 0, images: 0, toolActivity: 0, cacheRead: 0, cacheWrite: 0, reasoning: 0 };
      component.processChatEvent({ type: 'context', data: JSON.stringify({ promptTokens: 3, outputTokens: 1, windowTokens: 1000000, breakdown }) } as any);
      await finishTurn();
      const reply = component.messages[component.messages.length - 1];
      expect(reply.contextPromptTokens).toBe(3);
      expect(reply.contextBreakdown).toEqual(breakdown);
    });

    it('should treat a malformed breakdown as none, from a stored row too', () => {
      component.messages = [{
        role: 'assistant', content: 'a', timestampUtc: '2026-11-01T10:00:00Z',
        contextPromptTokens: 5, contextWindowTokens: 100, contextBreakdown: { version: 9 } as any
      }];
      (component as any).recomputeContextUsage();
      expect(component.contextUsage?.breakdown).toBeUndefined();
      expect(component.contextUsage?.usedTokens).toBe(5);
    });

    it('should read the measured model from a stored row', () => {
      component.messages = [{
        role: 'assistant', content: 'a', timestampUtc: '2026-11-01T10:00:00Z',
        contextPromptTokens: 5, contextWindowTokens: 100, providerUsed: 'Google', modelUsed: 'gemini-x'
      }];
      (component as any).recomputeContextUsage();
      expect(component.contextUsage?.provider).toBe('Google');
      expect(component.contextUsage?.modelId).toBe('gemini-x');
    });

    it('should say which model answered when the selection is gone, and refresh the list', async () => {
      const refreshed = vi.spyOn(settings, 'getUserModels').mockReturnValue(of([gemini]));
      component.selectedModelKey = 'u_1';
      component.processChatEvent({
        type: 'turn_model',
        data: JSON.stringify({ provider: 'Google', modelId: 'gemini-x', displayName: 'Gemini X', selectionUnavailable: true })
      } as any);
      expect(refreshed).toHaveBeenCalled();
      expect(component.restoredModelNote).toBe('The model you selected is no longer available, so this reply used Gemini X.');
      expect(component.selectedModelKey).toBe('u_2');
    });
  });

  describe('refreshing the models', () => {
    it('should refresh in place, without the loader or touching the messages', () => {
      const edited = { ...claude, displayName: 'Claude Renamed' };
      vi.spyOn(settings, 'getUserModels').mockReturnValue(of([edited, gemini]));
      const messages = component.messages;
      component.refreshChatModels();
      expect(component.selectedModel?.displayName).toBe('Claude Renamed');
      expect(component.isLoadingSession).toBe(false);
      expect(component.messages).toBe(messages);
    });

    it('should refresh when the tab becomes visible, and on a model_unavailable refusal', () => {
      const models = vi.spyOn(settings, 'getUserModels').mockReturnValue(of([claude, gemini]));
      document.dispatchEvent(new Event('visibilitychange'));
      const afterVisible = models.mock.calls.length;
      if (document.visibilityState === 'visible') expect(afterVisible).toBe(1);

      component.processChatEvent({ type: 'error', data: 'Refused', errorCode: 'model_unavailable' } as any);
      expect(models.mock.calls.length).toBe(afterVisible + 1);
      expect(component.unansweredModelMessage).toBe('Refused');
    });

    it('should refresh when a turn ends', () => {
      const models = vi.spyOn(settings, 'getUserModels').mockReturnValue(of([claude, gemini]));
      component.processChatEvent({ type: 'done', data: '' } as any);
      expect(models).toHaveBeenCalledTimes(1);
    });

    it('should stop listening once destroyed', () => {
      const models = vi.spyOn(settings, 'getUserModels').mockReturnValue(of([claude, gemini]));
      component.ngOnDestroy();
      document.dispatchEvent(new Event('visibilitychange'));
      expect(models).not.toHaveBeenCalled();
    });
  });

  describe('the edit notice', () => {
    beforeEach(() => {
      vi.spyOn(settings, 'getUserModels').mockReturnValue(of([claude, gemini]));
      component.refreshChatModels();
    });

    it('should say what changed, once per change', () => {
      vi.spyOn(settings, 'getUserModels').mockReturnValue(of([{ ...claude, thinkingLevel: 'Low' }, gemini]));
      component.refreshChatModels();
      expect(component.modelEditNote).toBe('Settings of Claude Y changed: Thinking High → Low. The next reply uses them, and may re-read this chat at full price.');

      component.dismissModelEditNote();
      component.refreshChatModels();
      expect(component.modelEditNote).toBeNull();
    });

    it('should say when the new limit truncates', () => {
      component.contextUsage = measurement(240000);
      vi.spyOn(settings, 'getUserModels').mockReturnValue(of([{ ...claude, inputLimitTokens: 100000 }, gemini]));
      component.refreshChatModels();
      expect(component.modelEditNote).toContain('Older messages will no longer reach the model.');
    });

    it('should stay quiet for another model\'s edit, and in an empty chat', () => {
      vi.spyOn(settings, 'getUserModels').mockReturnValue(of([claude, { ...gemini, thinkingLevel: 'Low' }]));
      component.refreshChatModels();
      expect(component.modelEditNote).toBeNull();

      component.messages = [];
      vi.spyOn(settings, 'getUserModels').mockReturnValue(of([{ ...claude, thinkingLevel: 'Low' }, gemini]));
      component.refreshChatModels();
      expect(component.modelEditNote).toBeNull();
    });

    it('should leave out the price clause in a confidential chat', () => {
      component.isConfidentialSession = true;
      vi.spyOn(settings, 'getUserModels').mockReturnValue(of([{ ...claude, thinkingLevel: 'Low' }, gemini]));
      component.refreshChatModels();
      expect(component.modelEditNote).not.toContain('full price');
    });
  });

  describe('old rows', () => {
    it.each([0, 1, 2, 3])('should render a message DTO without the new fields as before, in mode %i', mode => {
      component.showThoughtsAndTools = mode;
      component.messages = [{
        role: 'assistant',
        content: '<div class="ai-thought">\n\nThinking\n\n</div>\n\nThe answer.',
        timestampUtc: '2026-08-05T10:00:00Z',
        toolCalls: [{ id: 't1', name: 'wiki_search', status: 'completed' }]
      }];
      fixture.detectChanges();
      const host = fixture.nativeElement as HTMLElement;
      const thought = host.querySelector('.ai-thought');
      expect(thought?.textContent).toContain('Thinking');
      expect(host.querySelector('.messages')!.classList.contains('hide-thoughts')).toBe(mode === 0 || mode === 1);
      expect(host.textContent).toContain('The answer.');
      expect(host.querySelector('.context-window-indicator')).toBeFalsy();
    });
  });
});
