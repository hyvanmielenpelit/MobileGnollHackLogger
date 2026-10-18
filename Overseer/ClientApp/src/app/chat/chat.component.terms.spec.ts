import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { HttpResponse, provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { of, Subject, throwError } from 'rxjs';

import { ChatComponent } from './chat.component';
import { ChatService } from '../services/chat.service';
import { SettingsService, UserAiSettings } from '../services/settings.service';
import { AuthCheckResult, AuthService, AuthUser } from '../services/auth.service';
import { TermsDocument, TermsService } from '../services/terms.service';

const TERMS_TEXT = 'To use Gnoll Overseer, accept the Terms of Service.';

const DOC: TermsDocument = {
  key: 'overseer-terms',
  title: 'Gnoll Overseer Terms of Service',
  version: '2026-10-18',
  versionLabel: null,
  markdown: '# Gnoll Overseer Terms of Service\n\nPlay fair.',
  contentSha256: 'hash-of-the-terms',
  wikiUrl: 'https://wiki.example.test/Terms',
  privacyPolicyUrl: null
};

function user(overrides: Partial<AuthUser> = {}): AuthUser {
  return { userName: 'player', email: 'player@example.com', hasApiKey: true, ...overrides };
}

const NOT_ACCEPTED: Partial<AuthUser> = { termsRequiredVersion: '2026-10-18', termsAccepted: false, termsAcceptedVersion: null, termsAcceptedUtc: null };
const ACCEPTED: Partial<AuthUser> = { termsRequiredVersion: '2026-10-18', termsAccepted: true, termsAcceptedVersion: '2026-10-18', termsAcceptedUtc: '2026-10-18T09:30:00Z' };

describe('ChatComponent Terms gate', () => {
  let component: ChatComponent;
  let fixture: ComponentFixture<ChatComponent>;
  let chatService: ChatService;
  let settingsService: SettingsService;
  let authService: AuthService;
  let termsService: TermsService;

  const host = () => fixture.nativeElement as HTMLElement;
  const termsDialog = () => host().querySelector<HTMLDialogElement>('dialog.terms-dialog')!;
  const banner = () => host().querySelector<HTMLElement>('.terms-required-banner');
  const sendButton = () => host().querySelector<HTMLButtonElement>('.send-icon')!;
  /** One macrotask: the gate opens after the first check, and a native dialog's close event is queued. */
  const nextTask = () => new Promise(resolve => setTimeout(resolve, 0));

  function signIn(overrides: Partial<AuthUser>): void {
    (authService as any).userSubject.next(user(overrides));
  }

  /** Serves the settings, then runs the first change detection (ngOnInit and ngAfterViewInit). */
  function load(): void {
    vi.spyOn(settingsService, 'getSettings').mockReturnValue(of({} as UserAiSettings));
    vi.spyOn(settingsService, 'getSettingsResponse').mockReturnValue(
      of(new HttpResponse({ body: { hasApiKey: true, hasModel: true, spoilerFreeMode: false } as UserAiSettings })));
    vi.spyOn(settingsService, 'getUserModels').mockReturnValue(of([]));
    fixture.detectChanges();
  }

  function expectGateOpen(): void {
    expect(termsDialog().open).toBe(true);
    expect(component.termsDialog!.mode).toBe('gate');
    expect(host().querySelector('.terms-decline')).toBeTruthy();
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
    authService = TestBed.inject(AuthService);
    termsService = TestBed.inject(TermsService);
    (component as any).hubConnection = null;
    (component as any).hubStartPromise = null;
    vi.spyOn(component, 'loadSessions').mockReturnValue(undefined);
    vi.spyOn(termsService, 'getTerms').mockReturnValue(of(DOC));
    vi.spyOn(console, 'error').mockReturnValue(undefined);
  });

  afterEach(() => {
    const dialog = host().querySelector<HTMLDialogElement>('dialog.terms-dialog');
    if (dialog?.open) {
      component.termsDialog?.close();
    }
  });

  it('opens the gate when the signed-in user has not accepted the Terms', async () => {
    signIn(NOT_ACCEPTED);
    const open = vi.spyOn(component.termsDialog!, 'open');
    const check = vi.spyOn(authService, 'checkAuth');
    load();
    await nextTask();
    fixture.detectChanges();

    expectGateOpen();
    expect(open).toHaveBeenCalledWith('gate', null);
    expect(check).not.toHaveBeenCalled();
    expect(sendButton().getAttribute('aria-disabled')).toBe('true');
    expect(host().querySelector('#tip-send-blocked')?.textContent?.trim()).toBe(TERMS_TEXT);
  });

  it('asks the server when the cached user carries no Terms status, and opens the gate from its answer', async () => {
    signIn({});
    const me = new Subject<AuthCheckResult>();
    vi.spyOn(authService, 'checkAuth').mockReturnValue(me.asObservable());
    load();
    await nextTask();
    expect(termsDialog().open).toBe(false);

    me.next({ kind: 'signedIn', user: user(NOT_ACCEPTED) });
    fixture.detectChanges();

    expectGateOpen();
  });

  it('leaves the chat alone when the Terms are accepted', async () => {
    signIn(ACCEPTED);
    load();
    await nextTask();
    fixture.detectChanges();

    expect(termsDialog().open).toBe(false);
    expect(banner()).toBeNull();
    expect(sendButton().hasAttribute('aria-disabled')).toBe(false);
  });

  it('keeps the composer disabled with a banner after Not now, and Review Terms opens the gate again', async () => {
    signIn(NOT_ACCEPTED);
    load();
    await nextTask();
    fixture.detectChanges();

    host().querySelector<HTMLButtonElement>('.terms-decline')!.click();
    await nextTask();
    fixture.detectChanges();

    expect(termsDialog().open).toBe(false);
    const alert = banner();
    expect(alert).toBeTruthy();
    expect(alert!.querySelector('.terms-required-message')?.textContent?.trim()).toBe(TERMS_TEXT);
    expect(sendButton().getAttribute('aria-disabled')).toBe('true');

    const send = vi.spyOn(chatService, 'sendMessage');
    component.currentInput = 'Hello';
    sendButton().click();
    expect(send).not.toHaveBeenCalled();

    const review = alert!.querySelector<HTMLButtonElement>('.terms-review-btn')!;
    expect(review.textContent?.trim()).toBe('Review Terms');
    expect(review.classList).toContain('btn-gh');
    review.click();
    fixture.detectChanges();

    expectGateOpen();
  });

  it('enables the composer once the gate records the acceptance', async () => {
    signIn(NOT_ACCEPTED);
    vi.spyOn(termsService, 'accept').mockReturnValue(of({ accepted: true, greetingStarted: false }));
    load();
    await nextTask();
    fixture.detectChanges();

    vi.spyOn(authService, 'checkAuth').mockReturnValue(of<AuthCheckResult>({ kind: 'signedIn', user: user(ACCEPTED) }));
    host().querySelector<HTMLInputElement>('.terms-agree-input')!.click();
    fixture.detectChanges();
    host().querySelector<HTMLButtonElement>('.terms-accept')!.click();
    await nextTask();
    fixture.detectChanges();

    expect(termsDialog().open).toBe(false);
    expect(component.termsAcceptanceRequired).toBe(false);
    expect(banner()).toBeNull();
    expect(sendButton().hasAttribute('aria-disabled')).toBe(false);
  });

  it('shows the handoff waiting state when the acceptance started the greeting of the open chat', () => {
    signIn(ACCEPTED);
    load();
    (component as any).termsGateSessionId = 7;
    component.currentSessionId = '7';
    component.messages = [];
    component.isLoadingSession = false;
    // The overlay needs a usable model, which the harness's settings response may not provide.
    component.hasApiKey = true;
    component.hasModel = true;
    vi.spyOn(authService, 'checkAuth').mockReturnValue(of<AuthCheckResult>({ kind: 'signedIn', user: user(ACCEPTED) }));

    component.onTermsAccepted({ greetingStarted: true });
    fixture.detectChanges();

    expect(component.isHandoffWaiting).toBe(true);
    expect(host().querySelector('.handoff-loading-title')?.textContent).toContain('Consulting the Overseer');
  });

  it('reopens the gate on a terms_not_accepted error event and reads the status again', async () => {
    signIn(ACCEPTED);
    load();
    await nextTask();
    const check = vi.spyOn(authService, 'checkAuth')
      .mockReturnValue(of<AuthCheckResult>({ kind: 'signedIn', user: user(NOT_ACCEPTED) }));
    const open = vi.spyOn(component.termsDialog!, 'open');

    component.processChatEvent({ type: 'error', sessionId: '7', errorCode: 'terms_not_accepted', data: TERMS_TEXT });
    fixture.detectChanges();

    expectGateOpen();
    expect(open).toHaveBeenCalledWith('gate', 7);
    expect(check).toHaveBeenCalled();
    expect(component.termsAcceptanceRequired).toBe(true);
  });

  it('puts the message back and opens the gate on a 403 terms_not_accepted send', async () => {
    signIn(ACCEPTED);
    load();
    await nextTask();
    vi.spyOn(chatService, 'sendMessage').mockReturnValue(throwError(() => ({
      status: 403,
      error: { error: 'terms_not_accepted', requiredVersion: '2026-10-18' }
    })));
    vi.spyOn(authService, 'checkAuth').mockReturnValue(of<AuthCheckResult>({ kind: 'signedIn', user: user(NOT_ACCEPTED) }));
    component.currentInput = 'Hello';

    await component.sendMessage();
    fixture.detectChanges();

    expectGateOpen();
    expect(component.currentInput).toBe('Hello');
    expect(component.messages.length).toBe(0);
    expect(component.isStreaming).toBe(false);
    expect(host().querySelector('.message-box')).toBeNull();
  });

  it('lets an administrator block win over the Terms banner', async () => {
    signIn(NOT_ACCEPTED);
    load();
    await nextTask();
    component.termsDialog!.close();
    component.processChatEvent({ type: 'error', errorCode: 'overseer_blocked', data: 'Suspended.' });
    fixture.detectChanges();

    expect(host().querySelector('.overseer-blocked-banner')).toBeTruthy();
    expect(banner()).toBeNull();
    expect(host().querySelector('#tip-send-blocked')?.textContent?.trim()).toBe('Suspended.');
  });

  it('does not offer the Terms in the sidebar', async () => {
    signIn(ACCEPTED);
    load();
    await nextTask();

    const links = host().querySelector<HTMLElement>('.bottom-links')!;
    const texts = Array.from(links.querySelectorAll('a, button')).map(e => e.textContent?.trim());
    expect(texts).toContain('Logout');
    expect(texts).not.toContain('Terms');
    expect(host().querySelector('.bottom-links .bottom-link-btn')).toBeNull();
  });
});
