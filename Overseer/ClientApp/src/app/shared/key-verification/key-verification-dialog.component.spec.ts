import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { KeyVerificationDialogComponent } from './key-verification-dialog.component';
import {
  ApiKeyRefusal,
  describeCheckResponse,
  formatElapsed,
  readApiKeyRefusal,
  verificationLabel,
  verificationTooltip
} from './key-verification';

/** A dialog's close event is queued as a task; wait for the event itself, not a fixed number of ticks. */
function nextClose(dialog: HTMLDialogElement): Promise<void> {
  return new Promise<void>(resolve => dialog.addEventListener('close', () => resolve(), { once: true }));
}

const unverifiable: ApiKeyRefusal = {
  verdict: 'unverifiable',
  message: 'Anthropic did not answer the key check.',
  detail: {
    request: 'GET https://api.anthropic.com/v1/models',
    httpStatus: 503,
    httpReason: 'Service Unavailable',
    providerError: 'overloaded_error: Overloaded',
    exception: 'HttpRequestException: The response ended prematurely.',
    elapsedMs: 12345,
    text: 'GET https://api.anthropic.com/v1/models\nHTTP 503 Service Unavailable'
  }
};

describe('key-verification helpers', () => {
  it('reads an invalid refusal from a 400 and an unverifiable one from a 409', () => {
    const invalid = readApiKeyRefusal(new HttpErrorResponse({
      status: 400,
      error: { verdict: 'invalid', message: 'Anthropic rejected the key.', detail: { request: 'GET x', httpStatus: 401, httpReason: 'Unauthorized', providerError: null, exception: null, elapsedMs: 80, text: 'GET x\nHTTP 401' } }
    }));
    expect(invalid?.verdict).toBe('invalid');
    expect(invalid?.message).toBe('Anthropic rejected the key.');
    expect(invalid?.detail?.httpStatus).toBe(401);

    const conflict = readApiKeyRefusal(new HttpErrorResponse({ status: 409, error: unverifiable }));
    expect(conflict?.verdict).toBe('unverifiable');
    expect(conflict?.detail?.providerError).toBe('overloaded_error: Overloaded');
  });

  it('returns null for a plain message, another status or a body without a verdict', () => {
    expect(readApiKeyRefusal(new HttpErrorResponse({ status: 400, error: { message: 'API key is required.' } }))).toBeNull();
    expect(readApiKeyRefusal(new HttpErrorResponse({ status: 500, error: unverifiable }))).toBeNull();
    expect(readApiKeyRefusal(new HttpErrorResponse({ status: 400, error: 'A plain string' }))).toBeNull();
  });

  it('labels a verification by its status, and gives no label when it was never checked', () => {
    expect(verificationLabel({ status: 'Verified', checkedAtUtc: null, message: null })).toBe('Verified');
    expect(verificationLabel({ status: 'NotVerified', checkedAtUtc: null, message: null })).toBe('Not verified');
    expect(verificationLabel({ status: null, checkedAtUtc: null, message: null })).toBeNull();
    expect(verificationLabel(undefined)).toBeNull();
  });

  it('puts the check time and the stored message in the tooltip', () => {
    const text = verificationTooltip({ status: 'NotVerified', checkedAtUtc: '2026-09-29T10:00:00Z', message: 'No response' });
    expect(text).toMatch(/^Checked .+\nNo response$/);
  });

  it('describes a missing response and formats elapsed times', () => {
    expect(describeCheckResponse({ ...unverifiable.detail!, httpStatus: null, httpReason: null })).toBe('No response');
    expect(describeCheckResponse(unverifiable.detail!)).toBe('HTTP 503 Service Unavailable');
    expect(formatElapsed(850)).toBe('850 ms');
    expect(formatElapsed(12345)).toBe('12.3 s');
  });
});

describe('KeyVerificationDialogComponent', () => {
  let fixture: ComponentFixture<KeyVerificationDialogComponent>;
  let component: KeyVerificationDialogComponent;

  const dialog = () => fixture.nativeElement.querySelector('dialog.kv-dialog') as HTMLDialogElement;
  const text = () => dialog().textContent!.replace(/\s+/g, ' ');

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [KeyVerificationDialogComponent] }).compileComponents();
    fixture = TestBed.createComponent(KeyVerificationDialogComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  afterEach(() => {
    if (dialog()?.open) {
      dialog().close();
    }
  });

  it('opens modally, focuses its title and lists every part of the failure that exists', () => {
    component.open('Anthropic', unverifiable);
    fixture.detectChanges();

    expect(dialog().open).toBeTrue();
    expect(document.activeElement).toBe(fixture.nativeElement.querySelector('#kvTitle'));
    expect(text()).toContain('Anthropic could not confirm this key, so it is not known whether it works.');

    const terms = Array.from(dialog().querySelectorAll('.kv-detail dt')).map(dt => dt.textContent!.trim());
    expect(terms).toEqual(['Check', 'Response', 'Error', "Provider's message", 'Time']);
    expect(text()).toContain('GET https://api.anthropic.com/v1/models');
    expect(text()).toContain('HTTP 503 Service Unavailable');
    expect(text()).toContain('HttpRequestException: The response ended prematurely.');
    expect(text()).toContain('overloaded_error: Overloaded');
    expect(text()).toContain('12.3 s');
    expect(text()).toContain('marked Not verified until a check succeeds');
  });

  it('leaves out the parts a failure does not have and says No response', () => {
    component.open('Google', {
      ...unverifiable,
      detail: { ...unverifiable.detail!, httpStatus: null, httpReason: null, providerError: null }
    });
    fixture.detectChanges();

    const terms = Array.from(dialog().querySelectorAll('.kv-detail dt')).map(dt => dt.textContent!.trim());
    expect(terms).toEqual(['Check', 'Response', 'Error', 'Time']);
    expect(text()).toContain('No response');
  });

  it('emits saveAnyway once and shows Saving… with aria-busy until a new outcome is shown', () => {
    let emitted = 0;
    component.saveAnyway.subscribe(() => emitted++);
    component.open('OpenAI', unverifiable);
    fixture.detectChanges();

    const save = dialog().querySelector('.kv-save-anyway') as HTMLButtonElement;
    save.click();
    save.click();
    fixture.detectChanges();

    expect(emitted).toBe(1);
    expect(save.textContent!.trim()).toBe('Saving…');
    expect(save.getAttribute('aria-busy')).toBe('true');

    component.open('OpenAI', unverifiable);
    fixture.detectChanges();
    expect(save.textContent!.trim()).toBe('Save Anyway');
    expect(save.getAttribute('aria-busy')).toBeNull();
  });

  it('closes on Cancel and reports it', async () => {
    let closed = 0;
    component.closed.subscribe(() => closed++);
    component.open('Anthropic', unverifiable);
    fixture.detectChanges();

    const closing = nextClose(dialog());
    (dialog().querySelector('.kv-cancel') as HTMLButtonElement).click();
    await closing;

    expect(dialog().open).toBeFalse();
    expect(closed).toBe(1);
  });

  it('stops its own close and cancel events', () => {
    component.open('Anthropic', unverifiable);
    fixture.detectChanges();

    const host = fixture.nativeElement as HTMLElement;
    const reached: string[] = [];
    host.addEventListener('cancel', () => reached.push('cancel'));
    host.addEventListener('close', () => reached.push('close'));
    dialog().dispatchEvent(new Event('cancel', { bubbles: true }));
    dialog().dispatchEvent(new Event('close', { bubbles: true }));

    expect(reached).toEqual([]);
  });
});
