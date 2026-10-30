import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { HttpResponse, provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { of } from 'rxjs';

import { ChatComponent } from './chat.component';
import { ChatMessage, ChatService, ChatSessionDetailResponse, ChatSessionStateResponse, ChatTurnNotice } from '../services/chat.service';
import { DebugService } from '../services/debug.service';
import { SettingsService, UserAiSettings } from '../services/settings.service';

const WARNING: ChatTurnNotice = {
  code: 'research_rounds',
  severity: 'warning',
  title: 'Research limit reached',
  message: 'The Overseer used all the research steps allowed for one question, so this answer may be incomplete.',
  advice: 'Ask it to continue, or ask a narrower question.',
  action: 'continue'
};

const ERROR: ChatTurnNotice = {
  code: 'time_limit',
  severity: 'error',
  title: 'Time limit reached',
  message: 'The answer took longer than the time allowed and was stopped.',
  advice: 'Try again, or ask a narrower question.',
  action: 'try_again',
  reference: 1234
};

describe('ChatComponent turn notices', () => {
  let component: ChatComponent;
  let fixture: ComponentFixture<ChatComponent>;
  let chatService: ChatService;
  let settingsService: SettingsService;

  const host = () => fixture.nativeElement as HTMLElement;
  const notices = () => Array.from(host().querySelectorAll<HTMLElement>('app-turn-notice'));
  const noticeButton = () => host().querySelector<HTMLButtonElement>('app-turn-notice button');
  const statusRegion = () => host().querySelector<HTMLElement>('.turn-notice-status')!;
  const alertRegion = () => host().querySelector<HTMLElement>('.turn-notice-alert')!;

  function user(content = 'How many pages are on the wiki?', extra: Partial<ChatMessage> = {}): ChatMessage {
    return { role: 'user', content, timestampUtc: '2026-10-30T10:00:00Z', ...extra };
  }

  function assistant(turnNotice: ChatTurnNotice | null, content = 'Part of an answer.'): ChatMessage {
    return { role: 'assistant', content, timestampUtc: '2026-10-30T10:00:01Z', turnNotice };
  }

  function show(messages: ChatMessage[]): void {
    component.currentSessionId = '42';
    component.messages = messages;
    fixture.detectChanges();
  }

  /** A live turn on this page through its deferred push of the message. */
  function runTurn(events: object[]): void {
    component.currentSessionId = '42';
    component.isStreaming = true;
    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] });
    try {
      for (const evt of events) {
        component.processChatEvent(evt);
      }
      vi.advanceTimersByTime(30000);
    } finally {
      vi.useRealTimers();
    }
    fixture.detectChanges();
  }

  function detail(overrides: Partial<ChatSessionDetailResponse> = {}): ChatSessionDetailResponse {
    return { id: 42, sessionRef: '42', title: 'Chat', hasGameSnapshot: false, messages: [], ...overrides } as ChatSessionDetailResponse;
  }

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [ChatComponent],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()]
    }).compileComponents();

    vi.spyOn(ChatComponent.prototype, 'setupSignalR').mockReturnValue(undefined);
    fixture = TestBed.createComponent(ChatComponent);
    component = fixture.componentInstance;
    chatService = TestBed.inject(ChatService);
    settingsService = TestBed.inject(SettingsService);
    vi.spyOn(component, 'loadSessions').mockReturnValue(undefined);
    vi.spyOn(settingsService, 'getSettings').mockReturnValue(of({} as UserAiSettings));
    vi.spyOn(settingsService, 'getSettingsResponse').mockReturnValue(
      of(new HttpResponse({ body: { hasApiKey: true, hasModel: true, spoilerFreeMode: false } as UserAiSettings })));
    vi.spyOn(settingsService, 'getUserModels').mockReturnValue(of([]));
    vi.spyOn(console, 'error').mockReturnValue(undefined);
    fixture.detectChanges();
  });

  describe('a live turn', () => {
    it('puts the notice on the message the done path pushes, and clears it there', () => {
      runTurn([
        { type: 'chunk', data: 'Part of an answer.' },
        { type: 'turn_notice', data: JSON.stringify(WARNING) },
        { type: 'done', data: '' }
      ]);

      const last = component.messages[component.messages.length - 1];
      expect(last.role).toBe('assistant');
      expect(last.turnNotice).toEqual(expect.objectContaining({ code: 'research_rounds', action: 'continue' }));
      expect(component.currentTurnNotice).toBeNull();
      expect(notices().length).toBe(1);
      expect(host().querySelector('.message-box.assistant .markdown-body')!.textContent).toContain('Part of an answer.');
    });

    it('finishes a turn with no text on the done path, as a box holding only the notice', () => {
      runTurn([
        { type: 'turn_notice', data: JSON.stringify(ERROR) },
        { type: 'done', data: '' }
      ]);

      expect(component.isStreaming).toBe(false);
      expect(component.showSpinner).toBe(false);
      expect(component.streamingMessage).toBe('');
      const box = host().querySelector<HTMLElement>('.message-box.assistant')!;
      expect(box.querySelector('app-turn-notice')!.textContent).toContain('Time limit reached');
      expect(box.querySelector('.markdown-body')).toBeNull();
      expect(box.querySelector('.copy-btn')).toBeNull();
      expect(box.textContent).not.toContain('Error:');
    });

    it('still pushes the message when the turn had not started streaming on this page', () => {
      component.currentSessionId = '42';
      component.isStreaming = false;
      vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] });
      try {
        component.processChatEvent({ type: 'turn_notice', data: JSON.stringify(ERROR) });
        component.processChatEvent({ type: 'done', data: '' });
        vi.advanceTimersByTime(30000);
      } finally {
        vi.useRealTimers();
      }
      fixture.detectChanges();

      expect(component.isStreaming).toBe(false);
      expect(component.messages[component.messages.length - 1].turnNotice?.code).toBe('time_limit');
    });

    it('shows the notice on the streaming box before the message is pushed, without an action', () => {
      component.currentSessionId = '42';
      component.isStreaming = true;
      component.processChatEvent({ type: 'turn_notice', data: JSON.stringify(WARNING) });
      fixture.detectChanges();

      expect(notices().length).toBe(1);
      expect(noticeButton()).toBeNull();
    });

    it('ignores a notice of another chat, and one of an unexpected shape', () => {
      const log = vi.spyOn(TestBed.inject(DebugService), 'log');
      component.currentSessionId = '42';
      component.isStreaming = true;

      component.processChatEvent({ type: 'turn_notice', sessionId: '7', data: JSON.stringify(WARNING) });
      expect(component.currentTurnNotice).toBeNull();

      component.processChatEvent({ type: 'turn_notice', sessionId: '42', data: 'not json' });
      component.processChatEvent({ type: 'turn_notice', sessionId: '42', data: JSON.stringify({ code: 'x' }) });
      expect(component.currentTurnNotice).toBeNull();
      expect(log).toHaveBeenCalledWith('[Frontend] Ignored a turn_notice event of an unexpected shape.');
    });

    it('clears the notice on a new chat', () => {
      component.currentSessionId = '42';
      component.isStreaming = true;
      component.processChatEvent({ type: 'turn_notice', data: JSON.stringify(ERROR) });

      component.newSession();
      fixture.detectChanges();

      expect(component.currentTurnNotice).toBeNull();
      expect(alertRegion().textContent!.trim()).toBe('');
    });
  });

  describe('announcements', () => {
    it('uses the status region for a warning and the alert region for an error', () => {
      component.currentSessionId = '42';
      component.isStreaming = true;

      component.processChatEvent({ type: 'turn_notice', data: JSON.stringify(WARNING) });
      fixture.detectChanges();
      expect(statusRegion().textContent).toContain('Research limit reached');
      expect(statusRegion().getAttribute('role')).toBe('status');
      expect(alertRegion().textContent!.trim()).toBe('');

      component.turnNoticeStatusAnnouncement = '';
      component.processChatEvent({ type: 'turn_notice', data: JSON.stringify(ERROR) });
      fixture.detectChanges();
      expect(alertRegion().textContent).toContain('Time limit reached');
      expect(alertRegion().getAttribute('role')).toBe('alert');
      expect(statusRegion().textContent!.trim()).toBe('');
    });

    it('clears both regions when the next turn starts', async () => {
      vi.spyOn(chatService, 'sendMessage').mockReturnValue(of({ sessionId: '42' } as ChatSessionStateResponse));
      runTurn([{ type: 'turn_notice', data: JSON.stringify(ERROR) }, { type: 'done', data: '' }]);
      expect(alertRegion().textContent).toContain('Time limit reached');

      component.currentInput = 'Next question';
      await component.sendMessage();
      fixture.detectChanges();

      expect(alertRegion().textContent!.trim()).toBe('');
    });

    it('announces nothing for a notice loaded with the chat, whose box has no live role', async () => {
      vi.spyOn(chatService, 'getSession').mockReturnValue(of(new HttpResponse({
        body: detail({ messages: [user(), assistant(ERROR, '')] })
      })));

      await component.loadSession(42);
      fixture.detectChanges();

      expect(notices().length).toBe(1);
      expect(notices()[0].textContent).toContain('Reference L-1234');
      expect(notices()[0].querySelector('[role]')).toBeNull();
      expect(statusRegion().textContent!.trim()).toBe('');
      expect(alertRegion().textContent!.trim()).toBe('');
    });

    it('announces nothing for a notice replayed into a reopened chat', async () => {
      vi.spyOn(chatService, 'getSession').mockReturnValue(of(new HttpResponse({
        body: detail({
          messages: [user()],
          ongoingGeneration: { events: [{ type: 'turn_notice', data: JSON.stringify(ERROR), seqNo: 1 }] }
        } as Partial<ChatSessionDetailResponse>)
      })));

      await component.loadSession(42);
      fixture.detectChanges();

      expect(component.currentTurnNotice?.code).toBe('time_limit');
      expect(alertRegion().textContent!.trim()).toBe('');
    });
  });

  describe('rendering', () => {
    it('shows the notice in every thoughts-and-tools mode', () => {
      show([user(), assistant(WARNING)]);

      for (const mode of [0, 1, 2, 3]) {
        component.showThoughtsAndTools = mode;
        fixture.detectChanges();
        expect(host().querySelector('.messages')!.classList.contains('hide-thoughts')).toBe(mode < 2);
        expect(notices().length).toBe(1);
        expect(getComputedStyle(notices()[0]).display).not.toBe('none');
      }
    });

    it('keeps the notice outside the answer body and out of the copied text', () => {
      show([user(), assistant(WARNING)]);

      expect(host().querySelector('.markdown-body app-turn-notice')).toBeNull();
      expect(ChatComponent.stripThoughts(component.messages[1].content)).toBe('Part of an answer.');
    });

    it('keeps the body of a message of thoughts only, and of one without a notice', () => {
      show([user(), assistant(WARNING, '<div class="ai-thought">\n\nThinking\n\n</div>'), assistant(null, '')]);

      expect(host().querySelectorAll('.message-box.assistant .markdown-body').length).toBe(2);
    });
  });

  describe('the action', () => {
    function acceptSends() {
      return vi.spyOn(chatService, 'sendMessage').mockReturnValue(of({ sessionId: '42' } as ChatSessionStateResponse));
    }

    it('Try again sends the turn\'s user message again and keeps the composer', async () => {
      const send = acceptSends();
      show([user('Second question'), assistant(ERROR, '')]);
      component.currentInput = 'A draft';

      noticeButton()!.click();
      await vi.waitFor(() => expect(send).toHaveBeenCalled());

      expect(send.mock.calls[0][1]).toBe('Second question');
      expect(send.mock.calls[0][2]).toEqual([]);
      expect(component.currentInput).toBe('A draft');
      expect(component.messages[component.messages.length - 1]).toEqual(expect.objectContaining({ role: 'user', content: 'Second question' }));
    });

    it('Try again leaves files waiting in the composer out of the resend', async () => {
      const send = acceptSends();
      show([user('Second question'), assistant(ERROR, '')]);
      const waiting = [{ name: 'a.txt', type: 'text/plain', base64: 'YQ==' }] as typeof component.pendingAttachments;
      component.pendingAttachments = waiting;

      noticeButton()!.click();
      await vi.waitFor(() => expect(send).toHaveBeenCalled());

      expect(send.mock.calls[0][2]).toEqual([]);
      expect(component.pendingAttachments).toBe(waiting);
    });

    it('Try again is omitted when the message had attachments, or nothing to resend', () => {
      show([user('With a file', { attachments: [{ fileName: 'a.txt', contentType: 'text/plain' }] }), assistant(ERROR, '')]);
      expect(notices().length).toBe(1);
      expect(noticeButton()).toBeNull();

      show([assistant(ERROR, '')]);
      expect(noticeButton()).toBeNull();
    });

    it('Continue sends the fixed text', async () => {
      const send = acceptSends();
      show([user(), assistant(WARNING)]);

      expect(noticeButton()!.textContent!.trim()).toBe('Continue');
      noticeButton()!.click();
      await vi.waitFor(() => expect(send).toHaveBeenCalled());

      expect(send.mock.calls[0][1]).toBe('Please continue.');
    });

    it('New chat uses the new-chat action', () => {
      const navigate = vi.spyOn(component, 'navigateToNewSession').mockReturnValue(undefined);
      show([user(), assistant({ ...WARNING, action: 'new_chat' })]);

      noticeButton()!.click();

      expect(navigate).toHaveBeenCalledTimes(1);
    });

    it('is absent for action none', () => {
      show([user(), assistant({ ...WARNING, action: 'none' })]);

      expect(notices().length).toBe(1);
      expect(noticeButton()).toBeNull();
    });

    it('is offered only on the last assistant message', () => {
      show([user(), assistant(WARNING), user('Next'), assistant(null, 'Answer')]);

      expect(notices().length).toBe(1);
      expect(noticeButton()).toBeNull();
    });

    it('is withheld while a turn is running, and while the composer refuses to send', () => {
      show([user(), assistant(WARNING)]);
      expect(noticeButton()).toBeTruthy();

      component.isStreaming = true;
      fixture.detectChanges();
      expect(noticeButton()).toBeNull();

      component.isStreaming = false;
      component.overseerBlock = { message: 'Suspended', reason: null };
      fixture.detectChanges();
      expect(noticeButton()).toBeNull();
    });
  });

  describe('tool_error', () => {
    function runningCall() {
      component.currentSessionId = '42';
      component.isStreaming = true;
      component.streamingToolCalls = [{ id: 'call-1', name: 'wiki_search', status: 'running', displayName: 'Searching', argsText: '' }] as typeof component.streamingToolCalls;
    }

    it('marks the call a JSON event names', () => {
      runningCall();

      component.processChatEvent({ type: 'tool_error', data: JSON.stringify({ id: 'call-1', status: 'error', error: 'It failed' }) });

      expect(component.streamingToolCalls[0].status).toBe('error');
      expect(component.streamingToolCalls[0].error).toBe('It failed');
    });

    it('logs plain text instead of dropping it, and leaves the calls alone', () => {
      const log = vi.spyOn(TestBed.inject(DebugService), 'log');
      runningCall();

      component.processChatEvent({ type: 'tool_error', data: 'Maximum tool iterations reached.' });

      expect(log).toHaveBeenCalledWith('[Backend] tool_error: Maximum tool iterations reached.');
      expect(component.streamingToolCalls[0].status).toBe('running');
    });
  });

  describe('readTurnNotice', () => {
    it('needs a code, a known severity, a title and a message', () => {
      expect(ChatComponent.readTurnNotice({ severity: 'warning', title: 't', message: 'm' })).toBeNull();
      expect(ChatComponent.readTurnNotice({ code: 'c', severity: 'note', title: 't', message: 'm' })).toBeNull();
      expect(ChatComponent.readTurnNotice({ code: 'c', severity: 'error', message: 'm' })).toBeNull();
      expect(ChatComponent.readTurnNotice({ code: 'c', severity: 'error', title: 't' })).toBeNull();
      expect(ChatComponent.readTurnNotice(null)).toBeNull();
      expect(ChatComponent.readTurnNotice('text')).toBeNull();
    });

    it('fills what the server omits, and reads an unknown action as none', () => {
      expect(ChatComponent.readTurnNotice({ code: 'c', severity: 'error', title: 't', message: 'm', action: 'other' })).toEqual({
        code: 'c', severity: 'error', title: 't', message: 'm', advice: '', action: 'none', reference: null, adminFacts: null
      });
    });

    it('keeps the reference and the facts', () => {
      const notice = ChatComponent.readTurnNotice({ ...WARNING, reference: 7, adminFacts: { httpStatus: 429 } })!;

      expect(notice.reference).toBe(7);
      expect(notice.adminFacts).toEqual({ httpStatus: 429 });
      expect(notice.action).toBe('continue');
    });
  });
});
