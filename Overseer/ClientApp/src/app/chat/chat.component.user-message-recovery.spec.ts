import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { HttpResponse, provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { of, Subject } from 'rxjs';

import { ChatComponent } from './chat.component';
import { ChatMessage, ChatService, ChatSessionDetailResponse } from '../services/chat.service';
import { SettingsService, UserAiSettings } from '../services/settings.service';

describe('ChatComponent user_message_created', () => {
  let component: ChatComponent;
  let fixture: ComponentFixture<ChatComponent>;
  let chatService: ChatService;
  let settingsService: SettingsService;

  /** Serves the settings, then runs the first change detection (ngOnInit and ngAfterViewInit). */
  function load(): void {
    vi.spyOn(settingsService, 'getSettings').mockReturnValue(of({} as UserAiSettings));
    vi.spyOn(settingsService, 'getSettingsResponse').mockReturnValue(
      of(new HttpResponse({ body: { hasApiKey: true, hasModel: true, spoilerFreeMode: false } as UserAiSettings })));
    vi.spyOn(settingsService, 'getUserModels').mockReturnValue(of([]));
    fixture.detectChanges();
    component.currentSessionId = '42';
    component.isLoadingSession = false;
  }

  const user = (id: number | undefined, content = `Question ${id}`): ChatMessage =>
    ({ id, role: 'user', content, timestampUtc: '2026-10-26T09:00:00Z' });
  const assistant = (id: number | undefined, content = `Answer ${id}`): ChatMessage =>
    ({ id, role: 'assistant', content, timestampUtc: '2026-10-26T09:00:01Z' });

  /** `GET /api/chat/sessions/42` while the turn runs, after the server saved its user message. */
  function detail(messages: ChatMessage[]): ChatSessionDetailResponse {
    return {
      id: 42,
      title: 'Chat',
      isGnollHackSession: false,
      messages,
      hasOngoingGeneration: true,
      ongoingGeneration: { events: [] }
    };
  }

  /** Delivered without a `seqNo`, so the duplicate check in `processChatEvent` stays out of the way. */
  function created(messageId: number, extra: object = {}): void {
    component.processChatEvent({
      type: 'user_message_created',
      sessionId: '42',
      data: JSON.stringify({ messageId, attachments: [], ...extra })
    });
  }

  function serve(messages: ChatMessage[]) {
    return vi.spyOn(chatService, 'getSession').mockReturnValue(of(new HttpResponse({ body: detail(messages) })));
  }

  function serveLater(): Subject<HttpResponse<ChatSessionDetailResponse>> {
    const response = new Subject<HttpResponse<ChatSessionDetailResponse>>();
    vi.spyOn(chatService, 'getSession').mockReturnValue(response.asObservable());
    return response;
  }

  function respond(response: Subject<HttpResponse<ChatSessionDetailResponse>>, messages: ChatMessage[]): void {
    response.next(new HttpResponse({ body: detail(messages) }));
    response.complete();
  }

  const shown = () => component.messages.map(m => `${m.role} ${m.id}`);

  beforeEach(async () => {
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
    (component as any).hubConnection = null;
    (component as any).hubStartPromise = null;
    vi.spyOn(component, 'loadSessions').mockReturnValue(undefined);
  });

  it('gives the page its own message the server id and attachment ids, without a fetch', () => {
    load();
    const sent: ChatMessage = {
      ...user(undefined, 'Look at this'),
      attachments: [{ fileName: 'map.png', contentType: 'image/png' }]
    };
    component.messages = [sent];
    const getSession = serve([]);

    created(101, { attachments: [{ id: 501, fileName: 'map.png' }] });

    expect(sent.id).toBe(101);
    expect(sent.attachments![0].id).toBe(501);
    expect(getSession).not.toHaveBeenCalled();
  });

  it('fetches the first question of a chat loaded before the server saved it', () => {
    load();
    component.messages = [];
    const getSession = serve([user(101)]);

    created(101);

    expect(getSession).toHaveBeenCalledWith('42');
    expect(shown()).toEqual(['user 101']);
  });

  it('appends a later question and leaves the earlier one its own id', () => {
    load();
    component.messages = [user(100), assistant(200)];
    serve([user(100), assistant(200), user(102)]);

    created(102);

    expect(shown()).toEqual(['user 100', 'assistant 200', 'user 102']);
  });

  it('puts the question before a reply that finished before the fetch returned', () => {
    load();
    component.messages = [user(100), assistant(200)];
    const response = serveLater();

    created(102);
    component.messages.push(assistant(undefined, 'The streamed reply'));
    respond(response, [user(100), assistant(200), user(102), assistant(203)]);

    expect(shown()).toEqual(['user 100', 'assistant 200', 'user 102', 'assistant undefined']);
  });

  it('shows nothing for a hidden turn and fetches nothing', () => {
    load();
    const earlier = user(100);
    component.messages = [earlier];
    const getSession = serve([earlier]);

    created(101, { isHidden: true });

    expect(getSession).not.toHaveBeenCalled();
    expect(earlier.id).toBe(100);
    expect(shown()).toEqual(['user 100']);
  });

  it('does nothing when the chat was loaded after the save', () => {
    load();
    component.messages = [user(101)];
    const getSession = serve([user(101)]);

    created(101);

    expect(getSession).not.toHaveBeenCalled();
    expect(shown()).toEqual(['user 101']);
  });

  it('inserts nothing once the user has left the chat', () => {
    load();
    component.messages = [user(100), assistant(200)];
    const response = serveLater();

    created(102);
    component.newSession();
    respond(response, [user(100), assistant(200), user(102)]);

    expect(component.messages.some(m => m.id === 102)).toBe(false);
  });

  it('shows a question once when its event arrives twice', () => {
    load();
    component.messages = [];
    const first = new Subject<HttpResponse<ChatSessionDetailResponse>>();
    const second = new Subject<HttpResponse<ChatSessionDetailResponse>>();
    vi.spyOn(chatService, 'getSession')
      .mockReturnValueOnce(first.asObservable())
      .mockReturnValueOnce(second.asObservable());

    created(101);
    created(101);
    respond(first, [user(101)]);
    respond(second, [user(101)]);

    expect(shown()).toEqual(['user 101']);
  });
});
