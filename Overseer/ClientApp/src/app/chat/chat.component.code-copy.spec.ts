import { ComponentFixture, TestBed, fakeAsync, tick } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { HttpResponse, provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { of } from 'rxjs';

import { ChatComponent } from './chat.component';
import { ChatMessage } from '../services/chat.service';
import { SettingsService, UserAiSettings } from '../services/settings.service';

const CODE = 'def heal(hp):\n    if hp < 5:\n\n        return "quaff"';
const REPLY = 'Try this:\n\n```python\n' + CODE + '\n```\n\nThat is all.';

describe('ChatComponent code copy', () => {
  let component: ChatComponent;
  let fixture: ComponentFixture<ChatComponent>;

  const host = () => fixture.nativeElement as HTMLElement;
  const codeButtons = () => Array.from(host().querySelectorAll<HTMLButtonElement>('.markdown-body .md-code-copyable .md-code-copy-btn'));
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

  it('shows a copy button on a code block in an assistant reply', () => {
    show([message('user', 'Any code?'), message('assistant', REPLY)]);

    expect(codeButtons().length).toBe(1);
  });

  it('copies the exact code, announces it, and resets after two seconds', fakeAsync(() => {
    const write = vi.spyOn(navigator.clipboard, 'writeText').mockResolvedValue();
    show([message('assistant', REPLY)]);
    const button = codeButtons()[0];

    clickAndSettle(button);

    expect(write).toHaveBeenCalledTimes(1);
    expect(write).toHaveBeenCalledWith(CODE);
    expect(button.hasAttribute('data-copied')).toBe(true);
    expect(statusOf(button).textContent).toBe('Copied');
    expect(button.disabled).toBe(false);

    tick(2000);

    expect(button.hasAttribute('data-copied')).toBe(false);
    expect(statusOf(button).textContent).toBe('');
  }));

  it('shows a failed copy', fakeAsync(() => {
    vi.spyOn(navigator.clipboard, 'writeText').mockRejectedValue(new Error('denied'));
    show([message('assistant', REPLY)]);
    const button = codeButtons()[0];

    clickAndSettle(button);

    expect(button.hasAttribute('data-failed')).toBe(true);
    expect(button.hasAttribute('data-copied')).toBe(false);
    expect(statusOf(button).textContent).toBe('Copy failed');

    tick(2000);
  }));

  it('gives a code block in a user message no copy button', () => {
    show([message('user', '```\nmine\n```'), message('assistant', 'No code here.')]);

    expect(host().querySelector('.md-code-copy-btn')).toBeNull();
  });

  it('shows a copy button on a code block in the streaming message', () => {
    component.currentSessionId = '42';
    component.isStreaming = true;
    component.streamingMessage = '```\nstreamed code';
    fixture.detectChanges();

    const all = codeButtons();
    expect(all.length).toBe(1);
    expect(all[0].getAttribute('data-md-code-text')).toBe('streamed code');
  });

  it('copies each button its own text in a reply with a quote and a code block', fakeAsync(() => {
    const write = vi.spyOn(navigator.clipboard, 'writeText').mockResolvedValue();
    show([message('assistant', '> quoted **advice**\n\nAnd:\n\n```\ncode text\n```')]);

    clickAndSettle(host().querySelector<HTMLButtonElement>('.md-quote-copy-btn')!);
    clickAndSettle(codeButtons()[0]);

    expect(write.mock.calls.map(call => call[0])).toEqual(['quoted **advice**', 'code text']);
    tick(2000);
  }));

  it('leaves the message copy button copying the whole reply', fakeAsync(() => {
    const write = vi.spyOn(navigator.clipboard, 'writeText').mockResolvedValue();
    show([message('assistant', REPLY)]);

    host().querySelector<HTMLButtonElement>('.message-box.assistant .copy-btn')!.click();
    tick();

    expect(write).toHaveBeenCalledWith(REPLY);
    tick(2000);
  }));
});
