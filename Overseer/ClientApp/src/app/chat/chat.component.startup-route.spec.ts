import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { HttpResponse, provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { of, Subject } from 'rxjs';

import { ChatComponent } from './chat.component';
import { ChatService, ChatSessionStateResponse } from '../services/chat.service';
import { SettingsService, UserAiSettings } from '../services/settings.service';

/* The chat page subscribes to its route only once `/api/settings` has answered. These specs hold
   that answer back, so a first message can be sent before the first route emission arrives. */
describe('ChatComponent start-up route race', () => {
  let component: ChatComponent;
  let fixture: ComponentFixture<ChatComponent>;
  let chatService: ChatService;
  let settingsService: SettingsService;
  let router: Router;
  let settingsResponse: Subject<HttpResponse<UserAiSettings>>;
  let sendResponse: Subject<ChatSessionStateResponse>;

  /* One macrotask: an awaited call inside `sendMessage` resolves, and so does a router navigation.
     `fixture.whenStable()` would also wait out ngOnInit's 2.5 s avatar preload. */
  const nextTask = () => new Promise(resolve => setTimeout(resolve, 0));

  /** Creates the component on `url` and runs its first change detection; the settings stay unanswered. */
  async function create(url = '/'): Promise<void> {
    await router.navigateByUrl(url);
    fixture = TestBed.createComponent(ChatComponent);
    component = fixture.componentInstance;
    (component as any).hubConnection = null;
    (component as any).hubStartPromise = null;
    vi.spyOn(component, 'loadSessions').mockReturnValue(undefined);
    fixture.detectChanges();
  }

  /** `/api/settings` answers, which starts the route subscription. */
  function emitSettings(): void {
    settingsResponse.next(new HttpResponse({ body: { hasApiKey: true, hasModel: true, spoilerFreeMode: false } as UserAiSettings }));
  }

  /** Starts a send and returns its promise, wrapped so that awaiting the helper does not await the send. */
  async function startSend(text = 'Hello'): Promise<{ sending: Promise<void> }> {
    component.currentInput = text;
    const sending = component.sendMessage();
    await nextTask();
    return { sending };
  }

  async function finishSend(sending: Promise<void>, res: ChatSessionStateResponse): Promise<void> {
    sendResponse.next(res);
    sendResponse.complete();
    await sending;
    await nextTask();
  }

  const closeDialog = () => component.ephemeralCloseDialog?.nativeElement;

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
    chatService = TestBed.inject(ChatService);
    settingsService = TestBed.inject(SettingsService);
    router = TestBed.inject(Router);

    settingsResponse = new Subject<HttpResponse<UserAiSettings>>();
    sendResponse = new Subject<ChatSessionStateResponse>();
    vi.spyOn(settingsService, 'getSettingsResponse').mockReturnValue(settingsResponse.asObservable());
    vi.spyOn(settingsService, 'getSettings').mockReturnValue(of({} as UserAiSettings));
    vi.spyOn(settingsService, 'getUserModels').mockReturnValue(of([]));
    vi.spyOn(chatService, 'sendMessage').mockReturnValue(sendResponse.asObservable());
  });

  afterEach(() => {
    if (closeDialog()?.open) {
      closeDialog()!.close();
    }
  });

  it('keeps the first message when the settings answer while the first send is in flight', async () => {
    await create();
    const { sending } = await startSend('Hello');

    emitSettings();

    expect(component.messages.length).toBe(1);
    expect(component.messages[0].role).toBe('user');
    expect(component.messages[0].content).toBe('Hello');
    expect(component.isStreaming).toBe(true);
    expect(component.currentSessionId).toBeNull();

    await finishSend(sending, { sessionId: '42', isConfidential: false });

    expect(component.currentSessionId).toBe('42');
    expect(router.url).toBe('/?sessionId=42');
    expect(component.messages.length).toBe(1);
    expect(component.messages[0].content).toBe('Hello');
  });

  it('keeps an incognito chat created before the settings answer, without asking to close it', async () => {
    await create();
    component.onNewChatEphemeralChange(true);
    const { sending } = await startSend('Off the record');
    await finishSend(sending, { sessionId: 'eph_0123', isConfidential: true });
    const requestClose = vi.spyOn(component, 'requestCloseEphemeralSession');

    emitSettings();

    expect(component.currentSessionId).toBe('eph_0123');
    expect(component.messages.length).toBe(1);
    expect(requestClose).not.toHaveBeenCalled();
    expect(closeDialog()!.open).toBe(false);
  });

  it('applies the first emission as before when nothing was sent: a new chat', async () => {
    await create();

    emitSettings();

    expect(component.currentSessionId).toBeNull();
    expect(component.messages).toEqual([]);
  });

  it('applies the first emission as before when nothing was sent: the chat the URL names', async () => {
    await create('/?sessionId=5');
    vi.spyOn(chatService, 'getSession').mockReturnValue(of(new HttpResponse({
      body: { id: 5, title: 'Chat 5', messages: [] }
    })));
    const load = vi.spyOn(component, 'loadSession');

    emitSettings();

    expect(load).toHaveBeenCalledWith(5);
  });

  it('opens a chat clicked in the sidebar before the settings answer, over the chat started on the page', async () => {
    await create();
    const load = vi.spyOn(component, 'loadSession').mockResolvedValue(undefined);
    await startSend('Hello');

    component.navigateToSession(7);
    await nextTask();
    expect(router.url).toBe('/?sessionId=7');

    emitSettings();

    expect(load).toHaveBeenCalledWith(7);
  });

  it('keeps a chat started before the chat named in the opening URL loaded', async () => {
    await create('/?sessionId=5');
    const load = vi.spyOn(component, 'loadSession').mockResolvedValue(undefined);
    await startSend('Hello');

    emitSettings();

    expect(load).not.toHaveBeenCalled();
    expect(component.messages.length).toBe(1);
    expect(component.messages[0].content).toBe('Hello');
  });

  it('applies later emissions exactly as before', async () => {
    await create();
    emitSettings();
    const load = vi.spyOn(component, 'loadSession').mockResolvedValue(undefined);
    const fresh = vi.spyOn(component, 'newSession');
    component.messages = [{ role: 'user', content: 'Hello', timestampUtc: '2026-10-26T09:00:00Z' }];
    component.currentSessionId = null;

    await router.navigateByUrl('/?sessionId=9');
    expect(load).toHaveBeenCalledWith(9);

    await router.navigateByUrl('/');
    expect(fresh).toHaveBeenCalled();
  });

  it('loads the chat the URL already names when it is clicked in the sidebar', async () => {
    await create('/?sessionId=5');
    const load = vi.spyOn(component, 'loadSession').mockResolvedValue(undefined);
    await startSend('Hello');
    emitSettings();
    expect(load).not.toHaveBeenCalled();

    /* The state a failed first send leaves: the message on screen, no chat, the URL unchanged. */
    component.isStreaming = false;
    component.showSpinner = false;
    component.currentStatusText = '';
    expect(component.currentSessionId).toBeNull();
    expect(router.url).toBe('/?sessionId=5');

    component.navigateToSession(5);
    await nextTask();

    expect(load).toHaveBeenCalledWith(5);
  });
});
