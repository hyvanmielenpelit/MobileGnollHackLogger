import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { HttpResponse, provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { of } from 'rxjs';

import { ChatComponent } from './chat.component';
import { ChatService, ChatSessionDetailResponse } from '../services/chat.service';
import { SettingsService, UserAiSettings } from '../services/settings.service';

const OWN_TEXT = 'The game snapshot was not loaded. It contains text written as instructions to an AI assistant, in a part of the board you write yourself (a name, a label or a note). Rename or remove it and reconnect.';

describe('ChatComponent snapshot guard banner', () => {
  let component: ChatComponent;
  let fixture: ComponentFixture<ChatComponent>;
  let chatService: ChatService;
  let settingsService: SettingsService;

  const host = () => fixture.nativeElement as HTMLElement;
  const banner = () => host().querySelector<HTMLElement>('.snapshot-refused-banner');

  function detail(overrides: Partial<ChatSessionDetailResponse> = {}): ChatSessionDetailResponse {
    return { id: 42, sessionRef: '42', title: 'GnollHack Session', isGnollHackSession: true, hasGameSnapshot: false, messages: [], ...overrides } as ChatSessionDetailResponse;
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

  it('shows the server\'s text from the session, as text, and no banner without a refusal', async () => {
    vi.spyOn(chatService, 'getSession').mockReturnValue(of(new HttpResponse({
      body: detail({ snapshotRefusal: { code: 'injection-own', message: OWN_TEXT + ' <b>bold</b>', occurredUtc: '2026-10-24T15:00:00Z' } })
    })));

    await component.loadSession(42);
    fixture.detectChanges();

    const alert = banner()!;
    expect(alert).toBeTruthy();
    expect(alert.getAttribute('role')).toBe('status');
    expect(alert.classList.contains('alert-warning')).toBe(true);
    expect(alert.querySelector('.snapshot-refused-message')!.textContent!.trim()).toBe(OWN_TEXT + ' <b>bold</b>');
    expect(alert.querySelector('b')).toBeNull();

    vi.spyOn(chatService, 'getSession').mockReturnValue(of(new HttpResponse({ body: detail({ snapshotRefusal: null }) })));
    await component.loadSession(43);
    fixture.detectChanges();
    expect(banner()).toBeNull();
  });

  it('shows the banner from a snapshot_refused event of the open chat', () => {
    component.currentSessionId = '42';
    component.processChatEvent({
      type: 'snapshot_refused',
      sessionId: '42',
      data: JSON.stringify({ code: 'injection-foreign:history', message: 'The message history was not loaded.' })
    });
    fixture.detectChanges();

    expect(banner()!.textContent!.trim()).toBe('The message history was not loaded.');
  });

  it('ignores a snapshot_refused event of another chat, and one it cannot read', () => {
    component.currentSessionId = '42';
    component.processChatEvent({ type: 'snapshot_refused', sessionId: '7', data: JSON.stringify({ code: 'injection-own', message: 'x' }) });
    component.processChatEvent({ type: 'snapshot_refused', sessionId: '42', data: 'not json' });
    fixture.detectChanges();

    expect(banner()).toBeNull();
  });

  it('clears the banner when a snapshot is attached, and on a new chat', () => {
    component.snapshotRefusal = { code: 'injection-own', message: OWN_TEXT };
    fixture.detectChanges();
    expect(banner()).toBeTruthy();

    component.newSession();
    fixture.detectChanges();
    expect(banner()).toBeNull();
  });

  it('reads a refusal only with a code and a message', () => {
    expect(ChatComponent.readSnapshotRefusal({ code: 'injection-own', message: 'm' })).toEqual({ code: 'injection-own', message: 'm', occurredUtc: undefined });
    expect(ChatComponent.readSnapshotRefusal({ code: 'injection-own' })).toBeNull();
    expect(ChatComponent.readSnapshotRefusal(null)).toBeNull();
    expect(ChatComponent.readSnapshotRefusal('text')).toBeNull();
  });
});
