import { ComponentFixture, TestBed, fakeAsync, tick } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { HttpResponse, provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { of } from 'rxjs';

import { ChatComponent } from './chat.component';
import { SettingsService, UserAiSettings } from '../services/settings.service';

describe('ChatComponent keyboard and controller reachability', () => {
  let component: ChatComponent;
  let fixture: ComponentFixture<ChatComponent>;

  const host = () => fixture.nativeElement as HTMLElement;
  const prompt = () => host().querySelector<HTMLTextAreaElement>('textarea.transparent-textarea')!;

  function create(): void {
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
  }

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [ChatComponent],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()]
    }).compileComponents();
  });

  afterEach(() => {
    delete document.documentElement.dataset['gnhPad'];
    (document.activeElement as HTMLElement | null)?.blur();
  });

  describe('session list', () => {
    beforeEach(() => {
      create();
      component.loadingSessions = false;
      component.loadingMoreSessions = false;
      component.sessions = [
        { id: 7, title: 'First chat', lastMessageUtc: '2026-10-29T10:00:00Z' },
        { id: 8, title: 'Pinned chat', lastMessageUtc: '2026-10-29T09:00:00Z', isPinned: true }
      ];
      component.hasMoreSessions = true;
      fixture.detectChanges();
    });

    it('makes each row a button that opens its chat once, and keeps its actions outside it', () => {
      const navigate = vi.spyOn(component, 'navigateToSession').mockReturnValue(undefined as any);
      const remove = vi.spyOn(component, 'requestDeleteSession').mockReturnValue(undefined as any);
      const rows = Array.from(host().querySelectorAll<HTMLLIElement>('.sidebar li')).filter(li => li.querySelector('.session-info'));
      expect(rows.length).toBe(2);

      const rowButton = rows[0].querySelector<HTMLButtonElement>('button.session-info')!;
      expect(rowButton.type).toBe('button');
      expect(rowButton.querySelector('button')).toBeNull();
      expect(rowButton.contains(rows[0].querySelector('.session-delete-btn'))).toBe(false);

      rowButton.click();
      expect(navigate).toHaveBeenCalledTimes(1);
      expect(navigate).toHaveBeenCalledWith(7);

      rows[0].querySelector<HTMLButtonElement>('.session-delete-btn')!.click();
      expect(remove).toHaveBeenCalledTimes(1);
      expect(navigate).toHaveBeenCalledTimes(1);
    });

    it('marks the open chat\'s row as the current page', () => {
      vi.spyOn(component, 'isCurrentSession').mockImplementation((id: number | string) => Number(id) === 8);
      fixture.detectChanges();
      const buttons = host().querySelectorAll<HTMLButtonElement>('button.session-info');
      expect(buttons[0].getAttribute('aria-current')).toBeNull();
      expect(buttons[1].getAttribute('aria-current')).toBe('page');
    });

    it('makes Load more a button that loads the next page', () => {
      const loadMore = vi.spyOn(component, 'loadMoreSessions').mockReturnValue(undefined as any);
      const button = host().querySelector<HTMLButtonElement>('.load-more-item button.load-more-btn')!;
      expect(button.type).toBe('button');
      button.click();
      expect(loadMore).toHaveBeenCalledTimes(1);
    });

    it('carries the controller hints on the sidebar, the chat area, the messages and the message box', () => {
      expect(host().querySelector('.sidebar')!.getAttribute('data-gnh-pad-area')).toBe('s');
      expect(host().querySelector('.sidebar ul')!.hasAttribute('data-gnh-pad-scroll')).toBe(true);
      expect(host().querySelector('.chat-area')!.getAttribute('data-gnh-pad-area')).toBe('m');
      expect(host().querySelector('.messages')!.hasAttribute('data-gnh-pad-scroll')).toBe(true);
      expect(prompt().getAttribute('data-gnh-pad-key')).toBe('compose');
      expect(prompt().hasAttribute('data-gnh-pad-initial')).toBe(true);
    });
  });

  describe('message box focus', () => {
    it('stops auto-scroll when the controller scrolls the messages', () => {
      create();
      component.autoScrollEnabled = true;
      host().querySelector('.messages')!.dispatchEvent(new CustomEvent('gnhpadscroll', { bubbles: true }));
      expect(component.autoScrollEnabled).toBe(false);
    });

    it('refocuses the message box on window focus only outside controller mode', fakeAsync(() => {
      create();
      tick(1000);
      prompt().blur();

      document.documentElement.dataset['gnhPad'] = 'active';
      window.dispatchEvent(new Event('focus'));
      tick(1000);
      expect(document.activeElement).not.toBe(prompt());

      delete document.documentElement.dataset['gnhPad'];
      window.dispatchEvent(new Event('focus'));
      tick(1000);
      expect(document.activeElement).toBe(prompt());
    }));

    it('skips a pending refocus when the controller becomes active before it fires', fakeAsync(() => {
      create();
      tick(1000);
      prompt().blur();

      window.dispatchEvent(new Event('focus'));
      document.documentElement.dataset['gnhPad'] = 'active';
      tick(1000);
      expect(document.activeElement).not.toBe(prompt());
    }));

    it('releases an empty message box when the controller becomes active, and keeps one being edited', () => {
      create();
      const input = prompt();
      input.focus();
      expect(document.activeElement).toBe(input);

      input.value = 'half a question';
      document.documentElement.dataset['gnhPad'] = 'active';
      document.dispatchEvent(new CustomEvent('gnhpadmode', { detail: { active: true } }));
      expect(document.activeElement).toBe(input);

      input.value = '';
      document.dispatchEvent(new CustomEvent('gnhpadmode', { detail: { active: false } }));
      expect(document.activeElement).toBe(input);

      document.dispatchEvent(new CustomEvent('gnhpadmode', { detail: { active: true } }));
      expect(document.activeElement).not.toBe(input);
    });

    it('releases the message box when created while the controller is already active', async () => {
      document.documentElement.dataset['gnhPad'] = 'active';
      create();
      prompt().focus();
      await new Promise(resolve => requestAnimationFrame(resolve));
      expect(document.activeElement).not.toBe(prompt());
    });

    it('removes the same controller-mode listener it added when destroyed', () => {
      const add = vi.spyOn(document, 'addEventListener');
      const remove = vi.spyOn(document, 'removeEventListener');
      create();
      const added = add.mock.calls.filter(c => c[0] === 'gnhpadmode');
      expect(added.length).toBe(1);

      fixture.destroy();
      expect(remove).toHaveBeenCalledWith('gnhpadmode', added[0][1]);
    });
  });
});
