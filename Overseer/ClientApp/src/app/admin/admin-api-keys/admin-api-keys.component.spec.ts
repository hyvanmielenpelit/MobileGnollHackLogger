import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { of, throwError } from 'rxjs';
import { AdminApiKeysComponent } from './admin-api-keys.component';
import { AdminService, DefaultApiKeyStatus } from '../../services/admin.service';
import { ApiKeyRefusal } from '../../shared/key-verification/key-verification';

/** Obviously fake; never a real key's shape. */
const FAKE_KEY = 'test-key-not-real-0001';

/** A dialog's close event is queued as a task; wait for the event itself. */
function nextClose(dialog: HTMLDialogElement): Promise<void> {
  return new Promise<void>(resolve => dialog.addEventListener('close', () => resolve(), { once: true }));
}

function status(provider: string, overrides: Partial<DefaultApiKeyStatus> = {}): DefaultApiKeyStatus {
  return {
    provider,
    hasKey: false,
    keyHint: null,
    updatedAtUtc: null,
    verification: { status: null, checkedAtUtc: null, message: null },
    usedBy: [],
    ...overrides
  };
}

const unverifiable: ApiKeyRefusal = {
  verdict: 'unverifiable',
  message: 'Anthropic did not answer the key check.',
  detail: {
    request: 'GET https://api.anthropic.com/v1/models',
    httpStatus: null,
    httpReason: null,
    providerError: null,
    exception: 'TaskCanceledException: The request timed out.',
    elapsedMs: 30000,
    text: 'GET https://api.anthropic.com/v1/models\nNo response\nTaskCanceledException: The request timed out.'
  }
};

describe('AdminApiKeysComponent', () => {
  let fixture: ComponentFixture<AdminApiKeysComponent>;
  let component: AdminApiKeysComponent;
  let adminService: AdminService;
  let changed: number;

  const card = (provider: string) =>
    fixture.nativeElement.querySelector(`.aak-card[data-provider="${provider}"]`) as HTMLElement;
  const input = (provider: string) =>
    fixture.nativeElement.querySelector(`#aak-key-${provider}`) as HTMLInputElement;
  const verificationDialog = () =>
    fixture.nativeElement.querySelector('dialog.kv-dialog') as HTMLDialogElement;
  const deleteDialog = () =>
    fixture.nativeElement.querySelector('dialog.aak-delete-dialog') as HTMLDialogElement;

  async function render(statuses: DefaultApiKeyStatus[]): Promise<void> {
    fixture.componentRef.setInput('statuses', statuses);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  function type(provider: string, value: string): void {
    const el = input(provider);
    el.value = value;
    el.dispatchEvent(new Event('input'));
    fixture.detectChanges();
  }

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [AdminApiKeysComponent],
      providers: [provideHttpClient(), provideHttpClientTesting()]
    }).compileComponents();

    adminService = TestBed.inject(AdminService);
    fixture = TestBed.createComponent(AdminApiKeysComponent);
    component = fixture.componentInstance;
    changed = 0;
    component.changed.subscribe(() => changed++);
  });

  afterEach(() => {
    for (const dialog of Array.from(fixture.nativeElement.querySelectorAll('dialog')) as HTMLDialogElement[]) {
      if (dialog.open) {
        dialog.close();
      }
    }
  });

  it('shows one card per provider with its key state, hint, label and users', async () => {
    await render([
      status('Anthropic', {
        hasKey: true, keyHint: 'ab12',
        verification: { status: 'Verified', checkedAtUtc: '2026-09-29T10:00:00Z', message: null },
        usedBy: [{ id: 3, displayName: 'Claude Chat', isEnabled: true }, { id: 4, displayName: 'Claude Title', isEnabled: false }]
      }),
      status('Google'),
      status('OpenAI', { hasKey: true, keyHint: 'zz99' })
    ]);

    const title = fixture.nativeElement.querySelector('.gh-section-title') as HTMLElement;
    expect(title.textContent!.trim()).toBe('Default API Keys');

    const anthropic = card('Anthropic');
    expect(anthropic.querySelector('.aak-key-badge')!.textContent!.trim()).toBe('Default key saved');
    expect(anthropic.querySelector('.aak-hint')!.textContent).toContain('…ab12');
    const label = anthropic.querySelector('.aak-verification') as HTMLElement;
    expect(label.textContent!.trim()).toBe('Verified');
    const tip = fixture.nativeElement.querySelector('#' + label.getAttribute('interestfor')) as HTMLElement;
    expect(tip.textContent).toContain('Checked ');
    expect(anthropic.querySelector('.aak-used-by summary')!.textContent!.trim()).toBe('Used by 2 system configurations');
    expect(anthropic.querySelector('.aak-verify-again')).toBeNull();
    expect(input('Anthropic')).toBeNull();

    const google = card('Google');
    expect(google.querySelector('.aak-key-badge')!.textContent!.trim()).toBe('No default key');
    expect(input('Google').getAttribute('autocomplete')).toBe('off');
    expect(input('Google').type).toBe('password');

    // A key saved before verification existed has no label.
    expect(card('OpenAI').querySelector('.aak-verification')).toBeNull();
  });

  it('shows an invalid refusal under the input with its detail and keeps the key', async () => {
    await render([status('Anthropic'), status('Google'), status('OpenAI')]);
    const refusal: ApiKeyRefusal = {
      verdict: 'invalid',
      message: 'Anthropic rejected the key: invalid x-api-key.',
      detail: { ...unverifiable.detail!, httpStatus: 401, httpReason: 'Unauthorized', exception: null, text: 'GET x\nHTTP 401 Unauthorized' }
    };
    const save = spyOn(adminService, 'saveDefaultApiKey').and.returnValue(
      throwError(() => new HttpErrorResponse({ status: 400, error: refusal })));

    type('Anthropic', FAKE_KEY);
    (card('Anthropic').querySelector('.aak-save') as HTMLButtonElement).click();
    fixture.detectChanges();

    expect(save).toHaveBeenCalledWith('Anthropic', FAKE_KEY, false);
    const error = card('Anthropic').querySelector('.aak-error') as HTMLElement;
    expect(error.querySelector('.aak-error-message')!.textContent).toContain('invalid x-api-key');
    expect(error.querySelector('.aak-error-detail')!.textContent).toContain('HTTP 401 Unauthorized');
    expect(input('Anthropic').getAttribute('aria-describedby')).toBe(error.id);
    expect(input('Anthropic').value).toBe(FAKE_KEY);
    expect(verificationDialog().open).toBeFalse();
    expect(changed).toBe(0);
  });

  it('opens the verification dialog on a 409, and Save Anyway saves the key as Not verified', async () => {
    await render([status('Anthropic'), status('Google'), status('OpenAI')]);
    const save = spyOn(adminService, 'saveDefaultApiKey').and.returnValues(
      throwError(() => new HttpErrorResponse({ status: 409, error: unverifiable })),
      of({
        status: status('Anthropic', {
          hasKey: true, keyHint: '0001',
          verification: { status: 'NotVerified', checkedAtUtc: '2026-09-29T10:00:00Z', message: unverifiable.detail!.text }
        }),
        updatedConfigCount: 2,
        warning: null
      }));

    type('Anthropic', FAKE_KEY);
    (card('Anthropic').querySelector('.aak-save') as HTMLButtonElement).click();
    fixture.detectChanges();

    expect(verificationDialog().open).toBeTrue();
    const terms = Array.from(verificationDialog().querySelectorAll('.kv-detail dt')).map(dt => dt.textContent!.trim());
    expect(terms).toEqual(['Check', 'Response', 'Error', 'Time']);
    expect(verificationDialog().textContent).toContain('No response');

    const closing = nextClose(verificationDialog());
    (verificationDialog().querySelector('.kv-save-anyway') as HTMLButtonElement).click();
    fixture.detectChanges();
    await closing;
    fixture.detectChanges();

    expect(save.calls.mostRecent().args).toEqual(['Anthropic', FAKE_KEY, true]);
    const label = card('Anthropic').querySelector('.aak-verification') as HTMLElement;
    expect(label.textContent!.trim()).toBe('Not verified');
    expect(card('Anthropic').querySelector('.aak-verify-again')).not.toBeNull();
    expect(card('Anthropic').querySelector('.aak-status')!.textContent)
      .toBe('The Anthropic key was saved as Not verified. 2 system configurations received it.');
    expect(changed).toBe(1);
  });

  it('shows a rate-limit warning as an amber alert after a save', async () => {
    await render([status('Anthropic'), status('Google'), status('OpenAI')]);
    spyOn(adminService, 'saveDefaultApiKey').and.returnValue(of({
      status: status('Google', { hasKey: true, keyHint: '0001', verification: { status: 'Verified', checkedAtUtc: '2026-09-29T10:00:00Z', message: 'Rate limited' } }),
      updatedConfigCount: 0,
      warning: 'Google answered 429: the key works but is rate-limited.'
    }));

    type('Google', FAKE_KEY);
    (card('Google').querySelector('.aak-save') as HTMLButtonElement).click();
    fixture.detectChanges();

    expect(card('Google').querySelector('.alert-warning')!.textContent).toContain('rate-limited');
    expect(card('Google').querySelector('.aak-verification')!.textContent!.trim()).toBe('Verified');
    expect(card('Google').querySelector('.aak-status')!.textContent)
      .toBe('The Google key was saved. No system configuration uses it yet.');
  });

  it('Verify Again updates the label in place and announces the outcome', async () => {
    await render([
      status('Anthropic', {
        hasKey: true, keyHint: 'ab12',
        verification: { status: 'NotVerified', checkedAtUtc: '2026-09-29T09:00:00Z', message: 'No response' }
      }),
      status('Google'),
      status('OpenAI')
    ]);
    const verify = spyOn(adminService, 'verifyDefaultApiKey').and.returnValue(of({
      status: status('Anthropic', {
        hasKey: true, keyHint: 'ab12',
        verification: { status: 'Verified', checkedAtUtc: '2026-09-29T10:00:00Z', message: null }
      })
    }));

    expect(card('Anthropic').querySelector('.aak-verification')!.textContent!.trim()).toBe('Not verified');
    (card('Anthropic').querySelector('.aak-verify-again') as HTMLButtonElement).click();
    fixture.detectChanges();

    expect(verify).toHaveBeenCalledWith('Anthropic');
    expect(card('Anthropic').querySelector('.aak-verification')!.textContent!.trim()).toBe('Verified');
    expect(card('Anthropic').querySelector('.aak-verify-again')).toBeNull();
    expect(card('Anthropic').querySelector('.aak-status')!.textContent).toBe('The Anthropic key is verified.');
    expect(changed).toBe(1);
  });

  it('Replace Key reveals the input with Cancel, and Cancel hides it again', async () => {
    await render([status('Anthropic', { hasKey: true, keyHint: 'ab12' }), status('Google'), status('OpenAI')]);

    (card('Anthropic').querySelector('.aak-replace') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(input('Anthropic')).not.toBeNull();
    expect(document.activeElement).toBe(input('Anthropic'));

    (card('Anthropic').querySelector('.aak-cancel-replace') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(input('Anthropic')).toBeNull();
  });

  it('the delete dialog shows the count and names from the deletion check, and deletes', async () => {
    await render([
      status('Anthropic', { hasKey: true, keyHint: 'ab12', usedBy: [{ id: 3, displayName: 'Claude Chat', isEnabled: true }] }),
      status('Google'),
      status('OpenAI')
    ]);
    const check = spyOn(adminService, 'getDefaultApiKeyDeletionCheck').and.returnValue(of({
      count: 2,
      configs: [{ id: 3, displayName: 'Claude Chat', isEnabled: true }, { id: 5, displayName: 'Claude Bench', isEnabled: true }]
    }));
    const del = spyOn(adminService, 'deleteDefaultApiKey').and.returnValue(of({ disabledCount: 2 }));

    (card('Anthropic').querySelector('.aak-delete') as HTMLButtonElement).click();
    fixture.detectChanges();

    expect(check).toHaveBeenCalledWith('Anthropic');
    expect(deleteDialog().open).toBeTrue();
    const text = deleteDialog().textContent!.replace(/\s+/g, ' ');
    expect(text).toContain('Delete the default Anthropic key?');
    expect(text).toContain('2 system AI configurations use this key. They will be disabled');
    expect(Array.from(deleteDialog().querySelectorAll('.aak-delete-configs li')).map(li => li.textContent!.trim()))
      .toEqual(['Claude Chat', 'Claude Bench']);

    const closing = nextClose(deleteDialog());
    (deleteDialog().querySelector('.aak-delete-confirm') as HTMLButtonElement).click();
    await closing;
    fixture.detectChanges();

    expect(del).toHaveBeenCalledWith('Anthropic');
    expect(card('Anthropic').querySelector('.aak-key-badge')!.textContent!.trim()).toBe('No default key');
    expect(card('Anthropic').querySelector('.aak-status')!.textContent)
      .toBe('The Anthropic default key was deleted. 2 system configurations were disabled.');
    expect(changed).toBe(1);
  });

  it('says no configuration uses a key when the deletion check finds none', async () => {
    await render([status('Anthropic'), status('Google'), status('OpenAI', { hasKey: true, keyHint: 'zz99' })]);
    spyOn(adminService, 'getDefaultApiKeyDeletionCheck').and.returnValue(of({ count: 0, configs: [] }));

    (card('OpenAI').querySelector('.aak-delete') as HTMLButtonElement).click();
    fixture.detectChanges();

    expect(deleteDialog().textContent).toContain('No system AI configuration uses it.');
  });

  it('stops the delete dialog close and cancel events', async () => {
    await render([status('Anthropic', { hasKey: true, keyHint: 'ab12' }), status('Google'), status('OpenAI')]);
    spyOn(adminService, 'getDefaultApiKeyDeletionCheck').and.returnValue(of({ count: 0, configs: [] }));
    (card('Anthropic').querySelector('.aak-delete') as HTMLButtonElement).click();
    fixture.detectChanges();

    const host = fixture.nativeElement as HTMLElement;
    const reached: string[] = [];
    host.addEventListener('cancel', () => reached.push('cancel'));
    host.addEventListener('close', () => reached.push('close'));
    deleteDialog().dispatchEvent(new Event('cancel', { bubbles: true }));
    deleteDialog().dispatchEvent(new Event('close', { bubbles: true }));

    expect(reached).toEqual([]);
  });
});
