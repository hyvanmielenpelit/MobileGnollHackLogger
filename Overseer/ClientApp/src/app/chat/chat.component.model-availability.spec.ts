import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { HttpResponse, provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { of } from 'rxjs';

import { ChatComponent } from './chat.component';
import { ChatService } from '../services/chat.service';
import { SettingsService, UserAiModel, UserAiSettings } from '../services/settings.service';
import { AuthService } from '../services/auth.service';
import { ModelAvailability } from '../shared/model-availability/model-availability';

const GLOBAL_KEY = 'overseer_chat_model_global';
const UNAVAILABLE_TEXT = 'Google no longer serves the model gemini-3.7-flash. Choose another model, or update this model in Models.';

function retired(replacement: { modelId: string; displayName: string } | null = null): ModelAvailability {
  return { status: 'retired', needsAttention: true, retiredOn: '2026-09-30', replacement };
}

const AVAILABLE: ModelAvailability = { status: 'available', needsAttention: false };

function ownModel(overrides: Partial<UserAiModel> = {}): UserAiModel {
  return { id: 1, provider: 'Google', modelId: 'gemini-3.7-flash', displayName: 'Flash', isSystem: false, modelAvailability: AVAILABLE, ...overrides };
}

function systemModel(overrides: Partial<UserAiModel> = {}): UserAiModel {
  return { id: 2, provider: 'Google', modelId: 'gemini-3.8-flash', displayName: 'Gemini 3.8 Flash', isSystem: true, modelAvailability: AVAILABLE, ...overrides };
}

function clearChatStorage(): void {
  for (let i = localStorage.length - 1; i >= 0; i--) {
    const key = localStorage.key(i);
    if (key && (key.startsWith('overseer_chat_model') || key.startsWith('chat_draft'))) {
      localStorage.removeItem(key);
    }
  }
}

describe('ChatComponent model availability', () => {
  let component: ChatComponent;
  let fixture: ComponentFixture<ChatComponent>;
  let chatService: ChatService;
  let settingsService: SettingsService;
  let authService: AuthService;

  const host = () => fixture.nativeElement as HTMLElement;
  const composerNotice = () => host().querySelector<HTMLElement>('app-model-availability-notice[data-variant="composer"]');
  const unansweredNotice = () => host().querySelector<HTMLElement>('.composer-unanswered-notice');
  const buttonNamed = (text: string) => Array.from(host().querySelectorAll<HTMLButtonElement>('button'))
    .find(b => b.textContent?.trim() === text) ?? null;

  /** Serves the settings and these models, then runs the first change detection (ngOnInit). */
  function load(models: UserAiModel[], storedKey?: string): void {
    if (storedKey) {
      localStorage.setItem(GLOBAL_KEY, storedKey);
    }
    vi.spyOn(settingsService, 'getSettingsResponse').mockReturnValue(
      of(new HttpResponse({ body: { hasApiKey: true, hasModel: true, spoilerFreeMode: false } as UserAiSettings })));
    vi.spyOn(settingsService, 'getUserModels').mockReturnValue(of(models));
    fixture.detectChanges();
  }

  function signIn(isAdmin: boolean): void {
    (authService as any).userSubject.next({ userName: 'player', email: 'player@example.com', hasApiKey: true, isAdmin });
  }

  /** Runs one turn: the send, then the given events, with the done animation's timers flushed. */
  async function runTurn(text: string, events: Record<string, unknown>[]): Promise<void> {
    component.currentInput = text;
    await component.sendMessage();
    vi.useFakeTimers();
    try {
      for (const evt of events) {
        component.processChatEvent(evt);
      }
      // Past the longest avatar loop, which the done handler waits out before committing the reply.
      vi.advanceTimersByTime(20000);
    } finally {
      vi.useRealTimers();
    }
    fixture.detectChanges();
  }

  beforeEach(async () => {
    clearChatStorage();
    await TestBed.configureTestingModule({
      imports: [ChatComponent],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting()
      ]
    }).compileComponents();

    vi.spyOn(ChatComponent.prototype, 'setupSignalR').mockReturnValue(undefined);
    fixture = TestBed.createComponent(ChatComponent);
    component = fixture.componentInstance;
    chatService = TestBed.inject(ChatService);
    settingsService = TestBed.inject(SettingsService);
    authService = TestBed.inject(AuthService);
    (component as any).hubConnection = null;
    (component as any).hubStartPromise = null;
    vi.spyOn(component, 'loadSessions').mockReturnValue(undefined);
  });

  afterEach(() => {
    clearChatStorage();
  });

  describe('the composer notice', () => {
    it('should show a retired own model with Resolve, quietly on page load', () => {
      load([ownModel({ modelAvailability: retired() }), systemModel()], 'u_1');

      const notice = composerNotice();
      expect(notice).toBeTruthy();
      expect(notice!.textContent).toContain('Flash was removed from the model catalog on September 30, 2026.');
      expect(notice!.getAttribute('role')).toBeNull();

      const resolve = buttonNamed('Resolve…');
      expect(resolve).toBeTruthy();
      expect(resolve!.getAttribute('aria-haspopup')).toBe('dialog');
      expect(resolve!.getAttribute('aria-label')).toBe('Resolve "Flash"');
      expect(buttonNamed('Choose another model')).toBeNull();
    });

    it('should open the resolution dialog for the own model', () => {
      const availability = retired();
      load([ownModel({ modelAvailability: availability }), systemModel()], 'u_1');
      const open = vi.spyOn(component.modelResolutionDialog!, 'open').mockReturnValue(undefined);

      buttonNamed('Resolve…')!.click();

      expect(open).toHaveBeenCalledWith({
        id: 1, provider: 'Google', modelId: 'gemini-3.7-flash', displayName: 'Flash', availability
      });
    });

    it('should offer a retired system model another choice, focusing the picker', () => {
      signIn(false);
      load([ownModel(), systemModel({ modelAvailability: retired() })], 's_2');

      expect(composerNotice()).toBeTruthy();
      expect(buttonNamed('Resolve…')).toBeNull();

      buttonNamed('Choose another model')!.click();

      expect(document.activeElement?.id).toBe(component.chatModelPickerTriggerId);
    });

    it('should link an administrator to the system configuration, and not a regular user', () => {
      signIn(false);
      load([ownModel(), systemModel({ modelAvailability: retired() })], 's_2');
      expect(host().querySelector('.composer-model-config-link')).toBeNull();

      signIn(true);
      fixture.detectChanges();

      const link = host().querySelector<HTMLAnchorElement>('.composer-model-config-link');
      expect(link).toBeTruthy();
      expect(link!.textContent?.trim()).toBe('Open System Configs');
      const href = link!.getAttribute('href') ?? '';
      expect(href.startsWith('/admin?')).toBe(true);
      expect(href).toContain('tab=configs');
      expect(href).toContain('resolveConfig=2');
    });

    it('should select the replacement among the chat models through the picker path', () => {
      load([
        ownModel({ modelAvailability: retired({ modelId: 'gemini-3.8-flash', displayName: 'Gemini 3.8 Flash' }) }),
        systemModel()
      ], 'u_1');

      buttonNamed('Use Gemini 3.8 Flash')!.click();
      fixture.detectChanges();

      expect(component.selectedModelKey).toBe('s_2');
      expect(localStorage.getItem(GLOBAL_KEY)).toBe('s_2');
      expect(composerNotice()).toBeNull();
    });

    it('should not offer a replacement that is itself flagged', () => {
      load([
        ownModel({ modelAvailability: retired({ modelId: 'gemini-3.8-flash', displayName: 'Gemini 3.8 Flash' }) }),
        systemModel({ modelAvailability: retired() })
      ], 'u_1');

      expect(buttonNamed('Use Gemini 3.8 Flash')).toBeNull();
    });

    it('should announce the notice when the user selects a flagged model', () => {
      const flagged = ownModel({ id: 3, displayName: 'Old Flash', modelAvailability: retired() });
      load([ownModel(), flagged], 'u_1');
      expect(composerNotice()).toBeNull();

      component.selectModel(flagged);
      fixture.detectChanges();

      expect(composerNotice()!.getAttribute('role')).toBe('status');
    });

    it('should keep the selection after a resolution switched the model', () => {
      load([ownModel({ modelAvailability: retired() }), systemModel()], 'u_1');
      vi.spyOn(settingsService, 'getUserModels').mockReturnValue(of([
        ownModel({ modelId: 'gemini-3.8-flash' }), systemModel()
      ]));

      component.onModelResolved({ changes: [{ field: 'Model ID', from: 'gemini-3.7-flash', to: 'gemini-3.8-flash' }], blockers: [], model: null });
      fixture.detectChanges();

      expect(component.selectedModelKey).toBe('u_1');
      expect(composerNotice()).toBeNull();
      expect(component.restoredModelNote).toBeNull();
    });

    it('should fall back with the restored-selection note after the model was deleted', () => {
      load([ownModel({ modelAvailability: retired() }), systemModel()], 'u_1');
      vi.spyOn(settingsService, 'getUserModels').mockReturnValue(of([systemModel()]));

      component.onModelResolved({ changes: [], blockers: [], model: null, deleted: true });
      fixture.detectChanges();

      expect(component.selectedModelKey).toBe('s_2');
      expect(localStorage.getItem(GLOBAL_KEY)).toBeNull();
      expect(host().querySelector('.restored-model-note-text')?.textContent)
        .toBe('The model you used last is no longer available, so Gemini 3.8 Flash is selected.');
    });
  });

  describe('a restored selection', () => {
    it('should remove a stale global key, fall back, and say so once', () => {
      load([ownModel(), systemModel()], 'u_99');

      expect(component.selectedModelKey).toBe('u_1');
      expect(localStorage.getItem(GLOBAL_KEY)).toBeNull();
      const region = host().querySelector<HTMLElement>('.restored-model-note-region');
      expect(region!.getAttribute('role')).toBe('status');
      expect(region!.textContent).toContain('The model you used last is no longer available, so Flash is selected.');

      const dismiss = region!.querySelector<HTMLButtonElement>('button[aria-label="Dismiss"]');
      expect(dismiss).toBeTruthy();
      dismiss!.click();
      fixture.detectChanges();

      expect(host().querySelector('.restored-model-note')).toBeNull();

      component.applySavedModelPreference();
      expect(component.restoredModelNote).toBeNull();
    });

    it('should remove a stale session key and fall back to the global one', () => {
      load([ownModel(), systemModel()], 's_2');
      component.currentSessionId = '42';
      localStorage.setItem('overseer_chat_model_session_42', 'u_77');

      component.applySavedModelPreference();

      expect(component.selectedModelKey).toBe('s_2');
      expect(localStorage.getItem('overseer_chat_model_session_42')).toBeNull();
      expect(localStorage.getItem(GLOBAL_KEY)).toBe('s_2');
      expect(component.restoredModelNote).toBe('The model you used last is no longer available, so Gemini 3.8 Flash is selected.');
    });

    it('should say nothing when the stored model still resolves', () => {
      load([ownModel(), systemModel()], 's_2');

      expect(component.selectedModelKey).toBe('s_2');
      expect(component.restoredModelNote).toBeNull();
      expect(host().querySelector('.restored-model-note')).toBeNull();
    });
  });

  describe('a model_unavailable error', () => {
    beforeEach(() => {
      vi.spyOn(settingsService, 'getSettings').mockReturnValue(of({} as UserAiSettings));
      vi.spyOn(chatService, 'sendMessage').mockReturnValue(of({ sessionId: '42' } as any));
      vi.spyOn(component.router, 'navigateByUrl').mockReturnValue(Promise.resolve(true));
    });

    it('should raise an alert for an unflagged model, keep the error in the reply once, and clear after a successful turn', async () => {
      load([ownModel(), systemModel()], 'u_1');

      await runTurn('What is a gnoll?', [
        { type: 'error', errorCode: 'model_unavailable', data: UNAVAILABLE_TEXT },
        { type: 'done', data: '' }
      ]);

      const alert = unansweredNotice();
      expect(alert).toBeTruthy();
      expect(alert!.getAttribute('role')).toBe('alert');
      expect(alert!.textContent).toContain(`Your last message was not answered. ${UNAVAILABLE_TEXT}`);
      expect(buttonNamed('Choose another model')).toBeTruthy();

      const reply = component.messages[component.messages.length - 1];
      expect(reply.role).toBe('assistant');
      expect(reply.content.split(`**Error:** ${UNAVAILABLE_TEXT}`).length - 1).toBe(1);

      component.currentInput = 'Try again';
      await component.sendMessage();
      fixture.detectChanges();
      expect(unansweredNotice()).toBeTruthy();

      vi.useFakeTimers();
      try {
        component.streamingMessage = 'A gnoll is a hyena-headed humanoid.';
        component.processChatEvent({ type: 'done', data: '' });
        vi.advanceTimersByTime(20000);
      } finally {
        vi.useRealTimers();
      }
      fixture.detectChanges();

      expect(unansweredNotice()).toBeNull();
      expect(composerNotice()).toBeNull();
    });

    it('should lead the flagged notice with the unanswered sentence as an alert', async () => {
      load([ownModel({ modelAvailability: retired() }), systemModel()], 'u_1');
      expect(composerNotice()!.getAttribute('role')).toBeNull();

      await runTurn('What is a gnoll?', [
        { type: 'error', errorCode: 'model_unavailable', data: UNAVAILABLE_TEXT },
        { type: 'done', data: '' }
      ]);

      const notice = composerNotice();
      expect(notice!.getAttribute('role')).toBe('alert');
      expect(notice!.querySelector('.man-sentence')!.textContent)
        .toBe('Your last message was not answered. Flash was removed from the model catalog on September 30, 2026.');
      expect(unansweredNotice()).toBeNull();
      expect(buttonNamed('Resolve…')).toBeTruthy();
    });

    it('should clear when the user changes the model', async () => {
      load([ownModel(), systemModel()], 'u_1');
      await runTurn('What is a gnoll?', [
        { type: 'error', errorCode: 'model_unavailable', data: UNAVAILABLE_TEXT },
        { type: 'done', data: '' }
      ]);
      expect(unansweredNotice()).toBeTruthy();

      component.selectModel(component.systemModels[0]);
      fixture.detectChanges();
      expect(unansweredNotice()).toBeNull();

      component.selectModel(component.userModels[0]);
      fixture.detectChanges();
      expect(unansweredNotice()).toBeNull();
    });

    it('should leave an ordinary error without a composer notice', async () => {
      load([ownModel(), systemModel()], 'u_1');

      await runTurn('What is a gnoll?', [
        { type: 'error', data: 'The provider returned an error.' },
        { type: 'done', data: '' }
      ]);

      expect(unansweredNotice()).toBeNull();
      expect(composerNotice()).toBeNull();
    });
  });
});
