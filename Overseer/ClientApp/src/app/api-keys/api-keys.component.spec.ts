import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { ApiKeysComponent } from './api-keys.component';
import { SettingsService } from '../services/settings.service';
import { ApiKeyRefusal } from '../shared/key-verification/key-verification';
import { of, throwError } from 'rxjs';

describe('ApiKeysComponent', () => {
  let component: ApiKeysComponent;
  let fixture: ComponentFixture<ApiKeysComponent>;
  let settingsService: SettingsService;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [ApiKeysComponent],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting()
      ]
    })
    .compileComponents();

    fixture = TestBed.createComponent(ApiKeysComponent);
    component = fixture.componentInstance;
    settingsService = TestBed.inject(SettingsService);
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('should open delete confirm dialog when requestDeleteKey is called', () => {
    const dialogEl = document.createElement('dialog');
    vi.spyOn(dialogEl, 'showModal').mockReturnValue(undefined);
    component.deleteConfirmDialog = { nativeElement: dialogEl };

    component.requestDeleteKey('OpenAI');

    expect(component.deletingProvider).toBe('OpenAI');
    expect(dialogEl.showModal).toHaveBeenCalled();
  });

  it('should close delete confirm dialog and reset deletingProvider when closeDeleteConfirmDialog is called', () => {
    const dialogEl = document.createElement('dialog');
    vi.spyOn(dialogEl, 'close').mockReturnValue(undefined);
    component.deleteConfirmDialog = { nativeElement: dialogEl };
    component.deletingProvider = 'OpenAI';

    component.closeDeleteConfirmDialog();

    expect(component.deletingProvider).toBeNull();
    expect(dialogEl.close).toHaveBeenCalled();
  });

  it('should call deleteApiKeyForProvider and update key status when confirmDeleteKey is called', () => {
    const dialogEl = document.createElement('dialog');
    vi.spyOn(dialogEl, 'close').mockReturnValue(undefined);
    component.deleteConfirmDialog = { nativeElement: dialogEl };
    component.deletingProvider = 'Anthropic';
    component.keyStatuses['Anthropic'] = true;

    vi.spyOn(settingsService, 'deleteApiKeyForProvider').mockReturnValue(of({}));

    component.confirmDeleteKey();

    expect(dialogEl.close).toHaveBeenCalled();
    expect(settingsService.deleteApiKeyForProvider).toHaveBeenCalledWith('Anthropic');
    expect(component.keyStatuses['Anthropic']).toBe(false);
    expect(component.savingProvider).toBe('');
  });

  it('should load parallel execution mode and save on change', () => {
    vi.spyOn(settingsService, 'getApiKeys').mockReturnValue(of([
      { provider: 'OpenAI', hasKey: true, parallelExecutionMode: 0 },
      { provider: 'Anthropic', hasKey: false, parallelExecutionMode: 1 }
    ]));
    const saveModeSpy = vi.spyOn(settingsService, 'saveApiKeyParallelMode').mockReturnValue(of({}));

    component.loadStatuses();

    expect(component.keyParallelModes['OpenAI']).toBe(0);
    expect(component.keyParallelModes['Anthropic']).toBe(1);

    component.onParallelModeChange('OpenAI', 2);
    expect(saveModeSpy).toHaveBeenCalledWith('OpenAI', 2);
    expect(component.keyParallelModes['OpenAI']).toBe(2);
    expect(component.savingParallelProvider).toBe('');
  });

  it('should report no advanced summary when every setting is at its default', () => {
    vi.spyOn(settingsService, 'getApiKeys').mockReturnValue(of([
      {
        provider: 'OpenAI', hasKey: true, parallelExecutionMode: 2,
        confidentialityPosture: 'Unknown', userTrustsForConfidential: null
      }
    ]));

    component.loadStatuses();

    expect(component.advancedSummary('OpenAI')).toBe('');
  });

  it('should list the non-default advanced settings in the summary', () => {
    vi.spyOn(settingsService, 'getApiKeys').mockReturnValue(of([
      {
        provider: 'OpenAI', hasKey: true, parallelExecutionMode: 0,
        confidentialityPosture: 'Unknown', userTrustsForConfidential: false
      }
    ]));

    component.loadStatuses();

    expect(component.advancedSummary('OpenAI')).toBe('Sequential only \u00b7 Not for confidential chats');
  });

  it('should offer a personal key only the postures it can describe', () => {
    vi.spyOn(settingsService, 'getApiKeys').mockReturnValue(of([
      { provider: 'OpenAI', hasKey: true, confidentialityPosture: 'Unknown' },
      { provider: 'Google', hasKey: true, confidentialityPosture: 'SelfHosted' }
    ]));

    component.loadStatuses();

    const offered = component.postureOptions('OpenAI').map(p => p.value);
    expect(offered).toEqual(['Unknown', 'Standard', 'NoTraining', 'ZeroRetention']);

    // A legacy row keeps showing what it holds, so the user can change away from it.
    expect(component.postureOptions('Google').map(p => p.value)).toContain('SelfHosted');
  });

  it('should name the saved posture in the badge, and an undeclared one as an absence', () => {
    vi.spyOn(settingsService, 'getApiKeys').mockReturnValue(of([
      { provider: 'OpenAI', hasKey: true, confidentialityPosture: 'Unknown' },
      { provider: 'Google', hasKey: true, confidentialityPosture: 'NoTraining' }
    ]));

    component.loadStatuses();

    expect(component.savedPostureBadge('OpenAI')).toBe('Not recorded');
    expect(component.savedPostureBadge('Google')).toBe('Self-declared: No training on content');
  });

  it('should open the About dialog from the summary without toggling the disclosure', () => {
    vi.spyOn(settingsService, 'getApiKeys').mockReturnValue(of([
      { provider: 'OpenAI', hasKey: true }
    ]));

    component.loadStatuses();
    fixture.detectChanges();

    const openSpy = vi.spyOn(component, 'openAdvancedInfo').mockReturnValue(undefined);
    const disclosure: HTMLDetailsElement = fixture.nativeElement.querySelector('details.advanced-settings');
    const infoButton = disclosure.querySelector('summary .btn-info') as HTMLButtonElement;

    infoButton.click();
    fixture.detectChanges();

    expect(openSpy).toHaveBeenCalled();
    expect(disclosure.open).toBe(false);
  });

  it('should render the advanced settings disclosure closed', () => {
    vi.spyOn(settingsService, 'getApiKeys').mockReturnValue(of([
      { provider: 'OpenAI', hasKey: true }
    ]));

    component.loadStatuses();
    fixture.detectChanges();

    const disclosures: HTMLDetailsElement[] =
      Array.from(fixture.nativeElement.querySelectorAll('details.advanced-settings'));
    expect(disclosures.length).toBeGreaterThan(0);
    for (const disclosure of disclosures) {
      expect(disclosure.open).toBe(false);
    }
  });

  describe('key verification', () => {
    /** Obviously fake; never a real key's shape. */
    const FAKE_KEY = 'test-key-not-real-0001';

    const unverifiable: ApiKeyRefusal = {
      verdict: 'unverifiable',
      message: 'OpenAI did not answer the key check.',
      detail: {
        request: 'GET https://api.openai.com/v1/models',
        httpStatus: 502,
        httpReason: 'Bad Gateway',
        providerError: 'upstream connect error',
        exception: null,
        elapsedMs: 900,
        text: 'GET https://api.openai.com/v1/models\nHTTP 502 Bad Gateway\nupstream connect error'
      }
    };

    const card = (provider: string) =>
      (Array.from(fixture.nativeElement.querySelectorAll('.provider-card')) as HTMLElement[])
        .find(c => c.querySelector('h3')!.textContent!.trim() === provider)!;
    const verificationDialog = () => fixture.nativeElement.querySelector('dialog.kv-dialog') as HTMLDialogElement;

    function nextClose(dialog: HTMLDialogElement): Promise<void> {
      return new Promise<void>(resolve => dialog.addEventListener('close', () => resolve(), { once: true }));
    }

    function showNoKeys() {
      vi.spyOn(settingsService, 'getApiKeys').mockReturnValue(of([
        { provider: 'Anthropic', hasKey: false }, { provider: 'Google', hasKey: false }, { provider: 'OpenAI', hasKey: false }
      ]));
      component.loadStatuses();
      fixture.detectChanges();
    }

    function clickSave(provider: string) {
      component.newKeys[provider] = FAKE_KEY;
      fixture.detectChanges();
      (card(provider).querySelector('.save-key-btn') as HTMLButtonElement).click();
      fixture.detectChanges();
    }

    afterEach(() => {
      if (verificationDialog()?.open) {
        verificationDialog().close();
      }
    });

    it('says in the intro that each key is checked before it is saved', () => {
      expect(fixture.nativeElement.querySelector('.settings-description').textContent)
        .toContain('Each key is checked with its provider before it is saved.');
    });

    it('shows an invalid refusal under the input with its detail, and keeps the key', async () => {
      showNoKeys();
      const save = vi.spyOn(settingsService, 'saveApiKey').mockReturnValue(throwError(() => new HttpErrorResponse({
        status: 400,
        error: {
          verdict: 'invalid',
          message: 'Anthropic rejected the key: invalid x-api-key.',
          detail: { ...unverifiable.detail!, httpStatus: 401, httpReason: 'Unauthorized', text: 'GET x\nHTTP 401 Unauthorized' }
        }
      })));

      clickSave('Anthropic');
      await fixture.whenStable();
      fixture.detectChanges();

      expect(save).toHaveBeenCalledWith('Anthropic', FAKE_KEY, false);
      const error = card('Anthropic').querySelector('.key-error') as HTMLElement;
      expect(error.querySelector('.key-error-message')!.textContent).toContain('invalid x-api-key');
      expect(error.querySelector('.key-error-detail')!.textContent).toContain('HTTP 401 Unauthorized');
      const input = card('Anthropic').querySelector('#key-Anthropic') as HTMLInputElement;
      expect(input.getAttribute('aria-describedby')).toBe(error.id);
      expect(input.value).toBe(FAKE_KEY);
      expect(component.keyStatuses['Anthropic']).toBe(false);

      // Typing clears the error.
      input.value = FAKE_KEY + 'x';
      input.dispatchEvent(new Event('input'));
      fixture.detectChanges();
      expect(card('Anthropic').querySelector('.key-error')).toBeNull();
    });

    it('shows a plain 400 message the same way', () => {
      showNoKeys();
      vi.spyOn(settingsService, 'saveApiKey').mockReturnValue(throwError(() => new HttpErrorResponse({
        status: 400, error: { message: 'Unknown provider.' }
      })));

      clickSave('Google');

      expect(card('Google').querySelector('.key-error-message')!.textContent).toBe('Unknown provider.');
      expect(card('Google').querySelector('.key-error-detail')).toBeNull();
    });

    it('opens the verification dialog on a 409; Save Anyway resends and the key shows Not verified', async () => {
      showNoKeys();
      const save = vi.spyOn(settingsService, 'saveApiKey').mockReturnValueOnce(throwError(() => new HttpErrorResponse({ status: 409, error: unverifiable }))).mockReturnValueOnce(of({ verification: { status: 'NotVerified', checkedAtUtc: '2026-09-29T10:00:00Z', message: unverifiable.detail!.text }, warning: null }));

      clickSave('OpenAI');

      expect(verificationDialog().open).toBe(true);
      const terms = Array.from(verificationDialog().querySelectorAll('.kv-detail dt')).map(dt => dt.textContent!.trim());
      expect(terms).toEqual(['Check', 'Response', "Provider's message", 'Time']);
      expect(verificationDialog().textContent).toContain('HTTP 502 Bad Gateway');

      const closing = nextClose(verificationDialog());
      (verificationDialog().querySelector('.kv-save-anyway') as HTMLButtonElement).click();
      fixture.detectChanges();
      await closing;
      fixture.detectChanges();

      expect(vi.mocked(save).mock.lastCall).toEqual(['OpenAI', FAKE_KEY, true]);
      expect(component.keyStatuses['OpenAI']).toBe(true);
      const label = card('OpenAI').querySelector('.key-verification') as HTMLElement;
      expect(label.textContent!.trim()).toBe('Not verified');
      const tip = fixture.nativeElement.querySelector('#' + label.getAttribute('interestfor')) as HTMLElement;
      expect(tip.textContent).toContain('HTTP 502 Bad Gateway');
      expect(card('OpenAI').querySelector('.key-verify-again')).not.toBeNull();
      expect(card('OpenAI').querySelector('.key-status-line')!.textContent).toBe('The OpenAI key was saved as Not verified.');
    });

    it('shows a returned warning as an amber alert and a Verified label', () => {
      showNoKeys();
      vi.spyOn(settingsService, 'saveApiKey').mockReturnValue(of({
        verification: { status: 'Verified', checkedAtUtc: '2026-09-29T10:00:00Z', message: 'Rate limited' },
        warning: 'Google answered 429: the key works but is rate-limited.'
      }));

      clickSave('Google');

      expect(card('Google').querySelector('.alert-warning')!.textContent).toContain('rate-limited');
      expect(card('Google').querySelector('.key-verification')!.textContent!.trim()).toBe('Verified');
      expect(card('Google').querySelector('.key-verify-again')).toBeNull();
    });

    it('Verify Again updates the label in place and announces the outcome', () => {
      vi.spyOn(settingsService, 'getApiKeys').mockReturnValue(of([
        { provider: 'Anthropic', hasKey: true, verification: { status: 'NotVerified', checkedAtUtc: '2026-09-29T09:00:00Z', message: 'No response' } }
      ]));
      const verify = vi.spyOn(settingsService, 'verifyApiKey').mockReturnValue(of({
        verification: { status: 'Verified', checkedAtUtc: '2026-09-29T10:00:00Z', message: null }
      }));
      component.loadStatuses();
      fixture.detectChanges();

      expect(card('Anthropic').querySelector('.key-verification')!.textContent!.trim()).toBe('Not verified');
      (card('Anthropic').querySelector('.key-verify-again') as HTMLButtonElement).click();
      fixture.detectChanges();

      expect(verify).toHaveBeenCalledWith('Anthropic');
      expect(card('Anthropic').querySelector('.key-verification')!.textContent!.trim()).toBe('Verified');
      expect(card('Anthropic').querySelector('.key-verify-again')).toBeNull();
      expect(card('Anthropic').querySelector('.key-status-line')!.textContent).toBe('The Anthropic key is verified.');
    });

    it('shows no label on a key saved before keys were checked', () => {
      vi.spyOn(settingsService, 'getApiKeys').mockReturnValue(of([
        { provider: 'Anthropic', hasKey: true, verification: { status: null, checkedAtUtc: null, message: null } }
      ]));
      component.loadStatuses();
      fixture.detectChanges();

      expect(card('Anthropic').querySelector('.status-badge')!.textContent!.trim()).toBe('Key Saved');
      expect(card('Anthropic').querySelector('.key-verification')).toBeNull();
      expect(card('Anthropic').querySelector('.key-verify-again')).toBeNull();
    });
  });
});
