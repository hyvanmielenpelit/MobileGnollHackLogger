import { ComponentFixture, TestBed, fakeAsync, tick } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { HttpResponse, provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { of } from 'rxjs';

import { ChatComponent } from './chat.component';
import { ChatMessage } from '../services/chat.service';
import { SettingsService, UserAiSettings } from '../services/settings.service';

const REPLY = 'Here is the advice.\n\n> **D-pad for precision.** Moving one step\n> - at a time\n\nThat is all.';
const QUOTE_MARKDOWN = '**D-pad for precision.** Moving one step\n- at a time';

describe('ChatComponent quote copy', () => {
  let component: ChatComponent;
  let fixture: ComponentFixture<ChatComponent>;

  const host = () => fixture.nativeElement as HTMLElement;
  const quoteButtons = () => Array.from(host().querySelectorAll<HTMLButtonElement>('.markdown-body blockquote .md-quote-copy-btn'));
  const statusOf = (button: HTMLButtonElement) => button.nextElementSibling as HTMLElement;

  function message(role: 'user' | 'assistant', content: string): ChatMessage {
    return { role, content, timestampUtc: '2026-11-16T10:00:00Z' };
  }

  function show(messages: ChatMessage[]): void {
    component.currentSessionId = '42';
    component.messages = messages;
    fixture.detectChanges();
  }

  /** Clicks the button, lets the clipboard promise settle, and runs the next animation frame. */
  function clickAndSettle(button: HTMLButtonElement): void {
    button.click();
    tick(16);
  }

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [ChatComponent],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()]
    }).compileComponents();

    vi.spyOn(ChatComponent.prototype, 'setupSignalR').mockReturnValue(undefined);
    fixture = TestBed.createComponent(ChatComponent);
    component = fixture.componentInstance;
    const settingsService = TestBed.inject(SettingsService);
    vi.spyOn(component, 'loadSessions').mockReturnValue(undefined);
    vi.spyOn(settingsService, 'getSettings').mockReturnValue(of({} as UserAiSettings));
    vi.spyOn(settingsService, 'getSettingsResponse').mockReturnValue(
      of(new HttpResponse({ body: { hasApiKey: true, hasModel: true, spoilerFreeMode: false } as UserAiSettings })));
    vi.spyOn(settingsService, 'getUserModels').mockReturnValue(of([]));
    vi.spyOn(console, 'error').mockReturnValue(undefined);
    fixture.detectChanges();
  });

  it('shows a copy button on a quote in an assistant reply', () => {
    show([message('user', 'Any tips?'), message('assistant', REPLY)]);

    expect(quoteButtons().length).toBe(1);
  });

  it('copies the quote as Markdown, announces it, and resets after two seconds', fakeAsync(() => {
    const write = vi.spyOn(navigator.clipboard, 'writeText').mockResolvedValue();
    show([message('assistant', REPLY)]);
    const button = quoteButtons()[0];

    clickAndSettle(button);

    expect(write).toHaveBeenCalledTimes(1);
    expect(write).toHaveBeenCalledWith(QUOTE_MARKDOWN);
    expect(button.hasAttribute('data-copied')).toBe(true);
    expect(statusOf(button).textContent).toBe('Copied');
    expect(button.disabled).toBe(false);

    tick(2000);

    expect(button.hasAttribute('data-copied')).toBe(false);
    expect(statusOf(button).textContent).toBe('');
    expect(button.disabled).toBe(false);
  }));

  it('shows a failed copy', fakeAsync(() => {
    vi.spyOn(navigator.clipboard, 'writeText').mockRejectedValue(new Error('denied'));
    show([message('assistant', REPLY)]);
    const button = quoteButtons()[0];

    clickAndSettle(button);

    expect(button.hasAttribute('data-failed')).toBe(true);
    expect(button.hasAttribute('data-copied')).toBe(false);
    expect(statusOf(button).textContent).toBe('Copy failed');

    tick(2000);
  }));

  it('restarts the feedback on a repeat click and makes the repeated message differ', fakeAsync(() => {
    vi.spyOn(navigator.clipboard, 'writeText').mockResolvedValue();
    show([message('assistant', REPLY)]);
    const button = quoteButtons()[0];

    clickAndSettle(button);
    tick(1000);
    clickAndSettle(button);
    tick(1500);

    expect(button.hasAttribute('data-copied')).toBe(true);
    expect(statusOf(button).textContent).toBe('Copied\u00A0');

    tick(500);

    expect(button.hasAttribute('data-copied')).toBe(false);
    expect(statusOf(button).textContent).toBe('');
  }));

  it('gives a quote in a user message no copy button', () => {
    show([message('user', '> quoted by the user'), message('assistant', 'No quote here.')]);

    expect(host().querySelector('.md-quote-copy-btn')).toBeNull();
  });

  it('shows a copy button on a quote in the streaming message', () => {
    component.currentSessionId = '42';
    component.isStreaming = true;
    component.streamingMessage = '> streamed quote';
    fixture.detectChanges();

    const all = quoteButtons();
    expect(all.length).toBe(1);
    expect(all[0].getAttribute('data-md-quote-text')).toBe('streamed quote');
  });

  it('leaves the message copy button copying the whole reply', fakeAsync(() => {
    const write = vi.spyOn(navigator.clipboard, 'writeText').mockResolvedValue();
    show([message('assistant', REPLY)]);

    host().querySelector<HTMLButtonElement>('.message-box.assistant .copy-btn')!.click();
    tick();

    expect(write).toHaveBeenCalledWith(REPLY);
    tick(2000);
  }));
});
