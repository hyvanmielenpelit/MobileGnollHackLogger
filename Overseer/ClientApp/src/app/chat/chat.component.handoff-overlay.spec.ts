import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { HttpResponse, provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { of } from 'rxjs';

import { ChatComponent } from './chat.component';
import { ChatMessage, ChatService, ChatSessionDetailResponse } from '../services/chat.service';
import { SettingsService, UserAiSettings } from '../services/settings.service';

describe('ChatComponent hand-off waiting overlay', () => {
  let component: ChatComponent;
  let fixture: ComponentFixture<ChatComponent>;
  let chatService: ChatService;
  let settingsService: SettingsService;

  const host = () => fixture.nativeElement as HTMLElement;
  const overlayTitle = () => host().querySelector<HTMLElement>('.handoff-loading-title');

  /** Serves the settings, then runs the first change detection (ngOnInit and ngAfterViewInit). */
  function load(): void {
    vi.spyOn(settingsService, 'getSettings').mockReturnValue(of({} as UserAiSettings));
    vi.spyOn(settingsService, 'getSettingsResponse').mockReturnValue(
      of(new HttpResponse({ body: { hasApiKey: true, hasModel: true, spoilerFreeMode: false } as UserAiSettings })));
    vi.spyOn(settingsService, 'getUserModels').mockReturnValue(of([]));
    fixture.detectChanges();
    // The overlay needs a usable model, which the harness's empty model list does not provide.
    component.hasApiKey = true;
    component.hasModel = true;
  }

  /** A chat whose turn is running, as `GET /api/chat/sessions/42` reports it. */
  function detail(isGnollHackSession: boolean, messages: ChatMessage[] = []): ChatSessionDetailResponse {
    return {
      id: 42,
      title: 'Chat',
      isGnollHackSession,
      messages,
      hasOngoingGeneration: true,
      ongoingGeneration: { events: [] }
    };
  }

  async function open(body: ChatSessionDetailResponse): Promise<void> {
    vi.spyOn(chatService, 'getSession').mockReturnValue(of(new HttpResponse({ body })));
    await component.loadSession(42);
  }

  /** The component is OnPush: a field set outside its own handlers needs a mark before the view shows it. */
  function render(): void {
    component.cdr.markForCheck();
    fixture.detectChanges();
  }

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

  it('shows the overlay when a hand-off chat opens while its hidden greeting runs', async () => {
    load();
    await open(detail(true));
    render();

    expect(component.isHandoffWaiting).toBe(true);
    expect(overlayTitle()?.textContent).toContain('Consulting the Overseer');
  });

  it('does not show it for a web chat with a turn running and no visible messages', async () => {
    load();
    await open(detail(false));
    render();

    expect(component.isHandoffWaiting).toBe(false);
    expect(overlayTitle()).toBeNull();
  });

  it('does not show it when a status event reaches an empty web chat', () => {
    load();
    component.currentSessionId = '42';
    component.messages = [];
    component.isLoadingSession = false;

    component.processChatEvent({ type: 'status', sessionId: '42', data: 'Thinking...' });
    render();

    expect(component.isHandoffWaiting).toBe(false);
    expect(overlayTitle()).toBeNull();
  });

  it('does not show it for a hand-off chat that already has visible messages', async () => {
    load();
    await open(detail(true, [{ role: 'assistant', content: 'Greetings, adventurer.', timestampUtc: '2026-10-26T09:00:00Z' }]));
    render();

    expect(component.isHandoffWaiting).toBe(false);
    expect(overlayTitle()).toBeNull();
  });

  it('clears it when the user leaves the hand-off chat', async () => {
    load();
    await open(detail(true));
    expect(component.isHandoffWaiting).toBe(true);

    component.newSession();
    expect(component.isHandoffWaiting).toBe(false);

    component.processChatEvent({ type: 'status', data: 'Thinking...' });
    expect(component.isHandoffWaiting).toBe(false);
  });

  it('clears it for good once the 60-second timeout dismisses it', async () => {
    load();
    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] });
    try {
      await open(detail(true));
      expect(component.isHandoffWaiting).toBe(true);

      vi.advanceTimersByTime(60000);
      expect(component.isHandoffWaiting).toBe(false);

      component.processChatEvent({ type: 'status', sessionId: '42', data: 'Thinking...' });
      expect(component.isHandoffWaiting).toBe(false);
    } finally {
      vi.useRealTimers();
    }
  });
});
