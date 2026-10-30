import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { of, throwError } from 'rxjs';
import { AdminService, ChatTurnLimitEventDetail } from '../../../services/admin.service';
import { configureAdminTestBed } from '../../admin.component.testing';
import { TelemetryLimitDialogComponent } from './telemetry-limit-dialog.component';

const textOf = (el: Element | null | undefined): string => (el?.textContent ?? '').replace(/\s+/g, ' ').trim();

function detail(overrides: Partial<ChatTurnLimitEventDetail> = {}): ChatTurnLimitEventDetail {
  return {
    id: 1234, occurredUtc: '2026-10-30T12:00:00Z', code: 'research_rounds', severity: 'error', userId: 'u1', userName: 'Alice',
    wasConfidential: false, wasEphemeral: false, keySource: 'system', provider: 'Anthropic', requestedModelId: 'claude-x',
    modelDisplayName: 'Claude X', limitValue: 22, observedValue: 22, totalDurationMs: 4200, toolCallCount: 31,
    thinkingLevel: 'high', iterationsUsed: 22, modelCallCount: 23, toolCallsBlocked: 0, toolTimeMs: 850, permitWaitMs: 0,
    backoffWaitMs: 0, retryAttemptCount: 1, totalPromptTokens: 2050, outputTokens: 12, estimatedCost: 0.5,
    providerFinishReason: null, providerErrorCode: null, httpStatus: null, assistantPhase: null, chatSessionId: 20494,
    promptState: 'available',
    ...overrides
  };
}

describe('TelemetryLimitDialogComponent', () => {
  let fixture: ComponentFixture<TelemetryLimitDialogComponent>;
  let component: TelemetryLimitDialogComponent;
  let el: HTMLElement;
  let adminService: AdminService;
  let opener: HTMLButtonElement;

  beforeEach(async () => {
    ({ adminService } = await configureAdminTestBed(TelemetryLimitDialogComponent));
    opener = document.createElement('button');
    document.body.appendChild(opener);
  });

  afterEach(() => {
    vi.restoreAllMocks();
    if (fixture) {
      el.querySelectorAll('dialog').forEach(dialog => {
        if (dialog.open) dialog.close();
      });
      fixture.destroy();
    }
    opener.remove();
  });

  function create(): void {
    fixture = TestBed.createComponent(TelemetryLimitDialogComponent);
    component = fixture.componentInstance;
    el = fixture.nativeElement as HTMLElement;
    fixture.detectChanges();
  }

  function openWith(body: ChatTurnLimitEventDetail = detail()) {
    const spy = vi.spyOn(adminService, 'getChatTurnLimitEvent').mockReturnValue(of(body));
    create();
    component.open(body.id, opener);
    fixture.detectChanges();
    return spy;
  }

  const dialog = (): HTMLDialogElement => el.querySelector<HTMLDialogElement>('dialog.tl-lim-dialog')!;
  const facts = (): Record<string, string> => Object.fromEntries(
    Array.from(el.querySelectorAll('.tl-lim-dialog-facts > div')).map(row => [textOf(row.querySelector('dt')), textOf(row.querySelector('dd'))])
  );
  const showButton = (): HTMLButtonElement | null => el.querySelector<HTMLButtonElement>('.tl-lim-prompt-show');

  /** Resolves on the dialog's native close, which arrives after `dialog.close()` returns. */
  const closed = (): Promise<void> => new Promise<void>(resolve => dialog().addEventListener('close', () => resolve(), { once: true }));

  it('fetches the event, titles the dialog with the reason and the user and focuses the title', () => {
    const spy = openWith();

    expect(spy).toHaveBeenCalledWith(1234);
    expect(dialog().open).toBe(true);
    expect(dialog().classList).toContain('gh-dialog');
    expect(dialog().getAttribute('aria-labelledby')).toBe('tl-lim-dialog-title');
    expect(textOf(el.querySelector('#tl-lim-dialog-title'))).toBe('Research rounds · Alice');
    expect(document.activeElement?.id).toBe('tl-lim-dialog-title');
    expect(el.querySelector('.tl-lim-dialog-subtitle time')?.getAttribute('datetime')).toBe('2026-10-30T12:00:00Z');
    expect(textOf(el.querySelector('.tl-lim-dialog-subtitle'))).toBe('2026-10-30 12:00 UTC');
  });

  it('lists the facts in a definition list', () => {
    openWith();

    expect(facts()['Reference']).toBe('L-1234');
    expect(facts()['Stopped at']).toBe('22 of 22 rounds');
    expect(facts()['Tools, including sub-agents']).toBe('31');
    expect(facts()['Chat']).toBe('20494');
  });

  describe('prompt', () => {
    it('reads the prompt only when asked, says so first, and moves focus to the text', () => {
      const read = vi.spyOn(adminService, 'readChatTurnLimitPrompt').mockReturnValue(of({ text: 'What is a gnoll?', truncated: false }));
      openWith();

      expect(read).not.toHaveBeenCalled();
      expect(textOf(el.querySelector('.tl-lim-prompt-note'))).toBe('Reading the prompt is recorded in the access journal.');
      expect(showButton()!.getAttribute('aria-describedby')).toBe('tl-lim-prompt-journal');

      showButton()!.click();
      fixture.detectChanges();

      expect(read).toHaveBeenCalledTimes(1);
      expect(read).toHaveBeenCalledWith(1234);
      const pre = el.querySelector<HTMLElement>('pre.tl-lim-prompt-text')!;
      expect(pre.textContent).toBe('What is a gnoll?');
      expect(document.activeElement).toBe(pre);
      expect(showButton()).toBeNull();
      expect(textOf(el.querySelector('.tl-lim-prompt-note'))).toBe('Attachments are not included.');
    });

    it('renders markup in a prompt as text, never as HTML', () => {
      const markup = '<img src="x" onerror="alert(1)"> **bold**';
      vi.spyOn(adminService, 'readChatTurnLimitPrompt').mockReturnValue(of({ text: markup, truncated: false }));
      openWith();

      showButton()!.click();
      fixture.detectChanges();

      const pre = el.querySelector<HTMLElement>('pre.tl-lim-prompt-text')!;
      expect(pre.textContent).toBe(markup);
      expect(pre.children.length).toBe(0);
      expect(getComputedStyle(pre).whiteSpace).toBe('pre-wrap');
    });

    it('says when the prompt was cut', () => {
      vi.spyOn(adminService, 'readChatTurnLimitPrompt').mockReturnValue(of({ text: 'long', truncated: true }));
      openWith();

      showButton()!.click();
      fixture.detectChanges();

      expect(textOf(el.querySelector('.tl-lim-prompt-note'))).toBe('Shows the first 4,000 characters. Attachments are not included.');
    });

    for (const state of ['confidential', 'incognito', 'deleted'] as const) {
      it(`offers no button for a ${state} prompt, and says why in one sentence`, () => {
        const read = vi.spyOn(adminService, 'readChatTurnLimitPrompt');
        openWith(detail({ promptState: state }));

        expect(showButton()).toBeNull();
        expect(textOf(el.querySelector('.tl-lim-prompt-unavailable'))).toMatch(/^The prompt .+\.$/);
        expect(read).not.toHaveBeenCalled();
      });
    }

    it('says why when the server refuses the prompt, or it is gone', () => {
      const read = vi.spyOn(adminService, 'readChatTurnLimitPrompt')
        .mockReturnValueOnce(throwError(() => new HttpErrorResponse({ status: 403 })))
        .mockReturnValueOnce(throwError(() => new HttpErrorResponse({ status: 404 })));
      openWith();

      showButton()!.click();
      fixture.detectChanges();
      expect(textOf(el.querySelector('.tl-lim-prompt-error'))).toBe('The prompt is withheld because this chat is confidential.');

      showButton()!.click();
      fixture.detectChanges();
      expect(textOf(el.querySelector('.tl-lim-prompt-error'))).toContain('no longer available');
      expect(read).toHaveBeenCalledTimes(2);
    });

    it('forgets the prompt on close, so reading it again is journaled again', async () => {
      const read = vi.spyOn(adminService, 'readChatTurnLimitPrompt').mockReturnValue(of({ text: 'secret', truncated: false }));
      openWith();
      showButton()!.click();
      fixture.detectChanges();

      const done = closed();
      component.close();
      await done;
      fixture.detectChanges();
      expect(el.querySelector('pre.tl-lim-prompt-text')).toBeNull();

      component.open(1234, opener);
      fixture.detectChanges();
      expect(el.querySelector('pre.tl-lim-prompt-text')).toBeNull();
      showButton()!.click();
      expect(read).toHaveBeenCalledTimes(2);
    });
  });

  it('closes on Escape and returns focus to the opener', async () => {
    openWith();

    const done = closed();
    const cancel = new Event('cancel', { cancelable: true });
    dialog().dispatchEvent(cancel);
    await done;

    expect(cancel.defaultPrevented).toBe(true);
    expect(dialog().open).toBe(false);
    expect(document.activeElement).toBe(opener);
  });

  it('says so when the event no longer exists, and offers a retry', () => {
    const spy = vi.spyOn(adminService, 'getChatTurnLimitEvent').mockReturnValue(throwError(() => new HttpErrorResponse({ status: 404 })));
    create();
    component.open(99, opener);
    fixture.detectChanges();

    expect(textOf(el.querySelector('.tl-lim-dialog-error-text'))).toBe('This event no longer exists. It may have been pruned.');

    spy.mockReturnValue(of(detail()));
    el.querySelector<HTMLButtonElement>('.tl-lim-dialog-retry')!.click();
    fixture.detectChanges();

    expect(el.querySelector('.tl-lim-dialog-error-text')).toBeNull();
    expect(facts()['Reference']).toBe('L-1234');
  });
});
