import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, TestRequest, provideHttpClientTesting } from '@angular/common/http/testing';

import { SystemConfigBlockerDto } from '../../services/admin.service';
import { CatalogTarget, ModelAvailability, ModelResolutionResult } from '../model-availability/model-availability';
import { ModelResolutionDialogComponent, ModelResolutionSubject } from './model-resolution-dialog.component';

/** A dialog's close event is queued as a task; wait for the event itself. */
function nextClose(dialog: HTMLDialogElement): Promise<void> {
  return new Promise<void>(resolve => dialog.addEventListener('close', () => resolve(), { once: true }));
}

const TARGETS: CatalogTarget[] = [
  { modelId: 'gemini-3.5-pro', displayName: 'Gemini 3.5 Pro', releaseDate: '2026-03-01', thinkingLevels: [], contextWindowSize: 1000000, maxOutputTokens: 65536 },
  { modelId: 'gemini-3.9-pro', displayName: 'Gemini 3.9 Pro', releaseDate: '2026-10-01', thinkingLevels: [], contextWindowSize: 1000000, maxOutputTokens: 65536 },
  { modelId: 'gemini-3.8-flash', displayName: 'Gemini 3.8 Flash', releaseDate: '2026-08-15', thinkingLevels: [], contextWindowSize: 1000000, maxOutputTokens: 65536 }
];

const RETIRED: ModelAvailability = {
  status: 'retired',
  needsAttention: true,
  retiredOn: '2026-09-30',
  note: 'Google shut it down.',
  replacement: { modelId: 'gemini-3.8-flash', displayName: 'Gemini 3.8 Flash' },
  suggestedCustom: { maxInputTokens: 1000000, maxOutputTokens: 65536, inputPricePerMillion: 0.3, outputPricePerMillion: 2.5, cachedInputPricePerMillion: null }
};

const NOT_IN_CATALOG: ModelAvailability = {
  status: 'notInCatalog',
  needsAttention: true,
  suggestedCustom: { maxInputTokens: 200000, maxOutputTokens: 8192 }
};

const SUBJECT: ModelResolutionSubject = {
  id: 5, provider: 'Google', modelId: 'gemini-3.7-flash', displayName: 'Flash 3.7', availability: RETIRED
};

const BLOCKER: SystemConfigBlockerDto = {
  kind: 'run', id: '12', runId: 12, label: 'Run #12', roles: ['assessor'], startedAtUtc: '2026-10-10T09:40:00Z'
};

const CATALOG_URL = '/api/settings/model-catalog/Google';
const USER_URL = '/api/settings/usermodels/5/model-resolution';
const SYSTEM_URL = '/api/admin/systemconfigs/5/model-resolution';

function preview(changes: { field: string; from?: string; to?: string; note?: string }[], blockers: SystemConfigBlockerDto[] = []): ModelResolutionResult {
  return { changes, blockers, model: {} };
}

describe('ModelResolutionDialogComponent', () => {
  let fixture: ComponentFixture<ModelResolutionDialogComponent>;
  let component: ModelResolutionDialogComponent;
  let http: HttpTestingController;
  let resolved: ModelResolutionResult[];
  let deleteRequests: number[];
  let closedCount: number;

  const dialog = () => fixture.nativeElement.querySelector('dialog.model-resolution-dialog') as HTMLDialogElement;
  const q = <T extends Element = HTMLElement>(selector: string) => dialog().querySelector<T>(selector);
  const text = (el: Element | null) => (el?.textContent ?? '').replace(/\s+/g, ' ').trim();
  const primary = () => q<HTMLButtonElement>('.mrd-primary')!;
  const radio = (value: string) => q<HTMLInputElement>(`input[type="radio"][value="${value}"]`);
  const field = (key: string) => q<HTMLInputElement>(`#${component.uid}-${key}`)!;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [ModelResolutionDialogComponent],
      providers: [provideHttpClient(), provideHttpClientTesting()]
    }).compileComponents();
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(ModelResolutionDialogComponent);
    component = fixture.componentInstance;
    resolved = [];
    deleteRequests = [];
    closedCount = 0;
    component.resolved.subscribe(result => resolved.push(result));
    component.deleteRequested.subscribe(id => deleteRequests.push(id));
    component.closed.subscribe(() => closedCount++);
    fixture.detectChanges();
  });

  afterEach(() => {
    if (dialog()?.open) {
      dialog().close();
    }
    http.verify();
  });

  function setKind(kind: 'user' | 'system', allowDelete = true): void {
    fixture.componentRef.setInput('kind', kind);
    fixture.componentRef.setInput('allowDelete', allowDelete);
    fixture.detectChanges();
  }

  /** Opens the dialog and answers the catalog request. */
  function openWith(subject: ModelResolutionSubject, targets: CatalogTarget[] = TARGETS): void {
    component.open(subject);
    fixture.detectChanges();
    const request = http.expectOne(CATALOG_URL);
    expect(request.request.method).toBe('GET');
    request.flush(targets);
    fixture.detectChanges();
  }

  function expectDryRun(url: string, targetModelId: string): TestRequest {
    const request = http.expectOne(r => r.method === 'POST' && r.url === url && r.body?.dryRun === true && r.body?.targetModelId === targetModelId);
    expect(request.request.body).toEqual({ action: 'switch', targetModelId, dryRun: true });
    return request;
  }

  function chooseTarget(modelId: string): void {
    const select = q<HTMLSelectElement>('select.mrd-target')!;
    select.value = modelId;
    select.dispatchEvent(new Event('change'));
    fixture.detectChanges();
  }

  function type(input: HTMLInputElement, value: string): void {
    input.value = value;
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();
  }

  function blur(input: HTMLInputElement): void {
    input.dispatchEvent(new FocusEvent('blur'));
    fixture.detectChanges();
  }

  function choose(value: string): void {
    radio(value)!.click();
    fixture.detectChanges();
  }

  it('opens modally with focus on its title, named and described by its title and the availability sentence', () => {
    setKind('user');
    openWith(SUBJECT);
    expectDryRun(USER_URL, 'gemini-3.8-flash').flush(preview([]));
    fixture.detectChanges();

    expect(dialog().open).toBe(true);
    expect(dialog().hasAttribute('closedby')).toBe(false);
    expect(dialog().getAttribute('aria-labelledby')).toBe('mrd-title');
    expect(dialog().getAttribute('aria-describedby')).toBe('mrd-summary');
    expect(document.activeElement).toBe(q('#mrd-title'));
    expect(text(q('#mrd-title'))).toBe('Resolve "Flash 3.7"');
    expect(text(q('#mrd-summary'))).toBe('Flash 3.7 was removed from the model catalog on September 30, 2026. Google shut it down.');
    expect(text(q('.mrd-choices legend'))).toBe('What should happen to this model?');
  });

  it('chooses Switch when there is a replacement, listing it first and then the newest first', () => {
    setKind('user');
    openWith(SUBJECT);
    expectDryRun(USER_URL, 'gemini-3.8-flash').flush(preview([]));
    fixture.detectChanges();

    expect(radio('switch')!.checked).toBe(true);
    expect(radio('keepCustom')!.checked).toBe(false);
    const select = q<HTMLSelectElement>('select.mrd-target')!;
    expect(q(`label[for="${select.id}"]`)).not.toBeNull();
    expect(Array.from(select.options).map(o => o.textContent!.trim())).toEqual([
      'Gemini 3.8 Flash — released August 15, 2026',
      'Gemini 3.9 Pro — released October 1, 2026',
      'Gemini 3.5 Pro — released March 1, 2026'
    ]);
    expect(select.value).toBe('gemini-3.8-flash');
    expect(text(primary())).toBe('Switch model');
  });

  it('chooses Keep as custom without a replacement, prefilled from the suggested values, and asks for no preview', () => {
    setKind('user');
    openWith({ ...SUBJECT, availability: NOT_IN_CATALOG });

    expect(radio('keepCustom')!.checked).toBe(true);
    expect(text(primary())).toBe('Keep as custom model');
    expect(text(q(`#${component.uid}-keep-hint`))).toBe(
      'Overseer stops checking it against the catalog and uses the limits and prices below. Replies fail if Google no longer serves it.');
    expect(field('maxInputTokens').value).toBe('200000');
    expect(field('maxOutputTokens').value).toBe('8192');
    expect(field('inputPrice').value).toBe('');
    expect(field('maxInputTokens').getAttribute('inputmode')).toBe('numeric');
    expect(field('inputPrice').getAttribute('inputmode')).toBe('decimal');
    expect(field('maxInputTokens').getAttribute('aria-describedby')).toBe(`${component.uid}-maxInputTokens-hint`);
    http.expectNone(r => r.method === 'POST');
  });

  it('shows the dry-run preview as a list of changes', () => {
    setKind('user');
    openWith(SUBJECT);
    expectDryRun(USER_URL, 'gemini-3.8-flash').flush(preview([
      { field: 'Model ID', from: 'gemini-3.7-flash', to: 'gemini-3.8-flash' },
      { field: 'Thinking level', from: 'max', to: 'high', note: 'Max is not offered by the new model.' }
    ]));
    fixture.detectChanges();

    const rows = Array.from(dialog().querySelectorAll('dl.mrd-changes > .mrd-change'));
    expect(rows.map(row => text(row.querySelector('dt')))).toEqual(['Model ID', 'Thinking level']);
    expect(text(rows[0].querySelector('dd'))).toBe('gemini-3.7-flash → to gemini-3.8-flash');
    expect(text(rows[1].querySelector('.mrd-note'))).toBe('Max is not offered by the new model.');
    expect(primary().hasAttribute('aria-disabled')).toBe(false);
  });

  it('refreshes the preview on each selection and cancels a stale request', () => {
    setKind('user');
    openWith(SUBJECT);
    const first = expectDryRun(USER_URL, 'gemini-3.8-flash');
    expect(text(q('.mrd-preview [role="status"]'))).toBe('Checking what changes…');

    chooseTarget('gemini-3.9-pro');
    expect(first.cancelled).toBe(true);
    expectDryRun(USER_URL, 'gemini-3.9-pro').flush(preview([{ field: 'Model ID', from: 'gemini-3.7-flash', to: 'gemini-3.9-pro' }]));
    fixture.detectChanges();

    expect(text(q('dl.mrd-changes dd'))).toBe('gemini-3.7-flash → to gemini-3.9-pro');
  });

  it('disables the switch with its reason while the dry run reports blockers', () => {
    setKind('system');
    openWith(SUBJECT);
    expectDryRun(SYSTEM_URL, 'gemini-3.8-flash').flush(preview([{ field: 'Model ID', from: 'a', to: 'b' }], [BLOCKER]));
    fixture.detectChanges();

    expect(text(q('.mrd-blockers li'))).toBe('Run #12 — as the assessor, started 2026-10-10 09:40 UTC.');
    const button = primary();
    expect(button.getAttribute('aria-disabled')).toBe('true');
    expect(button.disabled).toBe(false);
    const reason = q(`#${button.getAttribute('aria-describedby')}`)!;
    expect(text(reason)).toBe('This configuration is in use; try again when the work above finishes.');

    button.click();
    fixture.detectChanges();
    http.expectNone(r => r.method === 'POST' && r.body?.dryRun === false);
    expect(dialog().open).toBe(true);
  });

  it('validates a custom field on blur, never while typing', () => {
    setKind('user');
    openWith({ ...SUBJECT, availability: NOT_IN_CATALOG });
    const input = field('maxOutputTokens');

    type(input, '0');
    expect(input.hasAttribute('aria-invalid')).toBe(false);
    expect(q(`#${input.id}-error`)).toBeNull();

    blur(input);
    expect(input.getAttribute('aria-invalid')).toBe('true');
    const error = q(`#${input.id}-error`)!;
    expect(error.classList).toContain('gh-field-error');
    expect(text(error)).toBe('Enter a whole number of 1 or more.');
    expect(input.getAttribute('aria-describedby')).toBe(`${input.id}-hint ${input.id}-error`);

    type(input, '4096');
    expect(input.getAttribute('aria-invalid')).toBe('true');
    blur(input);
    expect(input.hasAttribute('aria-invalid')).toBe(false);
    expect(q(`#${input.id}-error`)).toBeNull();
  });

  it('asks for both prices or neither, and focuses the first invalid field on submit', () => {
    setKind('user');
    openWith({ ...SUBJECT, availability: NOT_IN_CATALOG });
    type(field('inputPrice'), '1.5');
    primary().click();
    fixture.detectChanges();

    expect(text(q(`#${component.uid}-outputPrice-error`))).toBe(
      'Enter the output price too, or leave both prices empty to keep the current pricing.');
    expect(document.activeElement).toBe(field('outputPrice'));
    http.expectNone(r => r.method === 'POST');
  });

  it('switches a user model: Switching… while it saves, then emits resolved and closes', async () => {
    setKind('user');
    openWith(SUBJECT);
    expectDryRun(USER_URL, 'gemini-3.8-flash').flush(preview([]));
    fixture.detectChanges();

    primary().click();
    fixture.detectChanges();
    expect(text(primary())).toBe('Switching…');
    expect(primary().getAttribute('aria-disabled')).toBe('true');
    expect(primary().disabled).toBe(false);
    primary().click();

    const save = http.expectOne(r => r.method === 'POST' && r.url === USER_URL && r.body.dryRun === false);
    expect(save.request.body).toEqual({ action: 'switch', targetModelId: 'gemini-3.8-flash', dryRun: false });
    const closing = nextClose(dialog());
    const result: ModelResolutionResult = { changes: [{ field: 'Model ID', from: 'a', to: 'b' }], blockers: [], model: { id: 5 } };
    save.flush(result);
    await closing;

    expect(resolved).toEqual([result]);
    expect(dialog().open).toBe(false);
    expect(closedCount).toBe(1);
  });

  it('keeps a system configuration as custom, sending only the prices that were given', async () => {
    setKind('system');
    openWith(SUBJECT);
    expectDryRun(SYSTEM_URL, 'gemini-3.8-flash').flush(preview([]));
    fixture.detectChanges();

    choose('keepCustom');
    expect(text(primary())).toBe('Keep as custom model');
    type(field('inputPrice'), '');
    type(field('outputPrice'), '');
    primary().click();
    fixture.detectChanges();
    expect(text(primary())).toBe('Saving…');

    const save = http.expectOne(r => r.method === 'POST' && r.url === SYSTEM_URL);
    expect(save.request.body).toEqual({ action: 'keepCustom', maxInputTokens: 1000000, maxOutputTokens: 65536, dryRun: false });
    const closing = nextClose(dialog());
    save.flush({ changes: [], blockers: [], model: { id: 5 } });
    await closing;
    expect(resolved.length).toBe(1);
  });

  it('hands a system delete to the host with deleteRequested and closes', async () => {
    setKind('system');
    openWith(SUBJECT);
    expectDryRun(SYSTEM_URL, 'gemini-3.8-flash').flush(preview([]));
    fixture.detectChanges();

    choose('delete');
    expect(text(q(`#${component.uid}-delete-hint`))).toBe("Next you'll see what uses this configuration before anything is deleted.");
    expect(primary().classList).toContain('btn-gh-delete');
    expect(text(primary())).toBe('Delete…');

    const closing = nextClose(dialog());
    primary().click();
    await closing;
    expect(deleteRequests).toEqual([5]);
    expect(resolved).toEqual([]);
    http.expectNone(r => r.method === 'DELETE');
  });

  it('deletes a user model itself and reports it as a deletion', async () => {
    setKind('user');
    openWith({ ...SUBJECT, availability: NOT_IN_CATALOG });
    choose('delete');
    expect(text(q(`#${component.uid}-delete-hint`))).toBe('The model is removed from your list. Chats keep their history.');
    expect(text(primary())).toBe('Delete model');

    primary().click();
    fixture.detectChanges();
    const request = http.expectOne('/api/settings/usermodels/5');
    expect(request.request.method).toBe('DELETE');
    const closing = nextClose(dialog());
    request.flush(null);
    await closing;
    expect(resolved).toEqual([{ changes: [], blockers: [], model: null, deleted: true }]);
  });

  it('offers no delete when allowDelete is off', () => {
    setKind('user', false);
    openWith({ ...SUBJECT, availability: NOT_IN_CATALOG });
    expect(radio('delete')).toBeNull();
    expect(dialog().querySelectorAll('input[type="radio"]').length).toBe(2);
  });

  it('shows a refusal in an alert and stays open', () => {
    setKind('user');
    openWith({ ...SUBJECT, availability: NOT_IN_CATALOG });
    primary().click();
    fixture.detectChanges();
    http.expectOne(r => r.method === 'POST' && r.url === USER_URL)
      .flush({ message: 'The limits exceed what the provider allows.' }, { status: 400, statusText: 'Bad Request' });
    fixture.detectChanges();

    const alert = q('.mrd-error')!;
    expect(alert.getAttribute('role')).toBe('alert');
    expect(alert.classList).toContain('alert-danger');
    expect(text(alert)).toBe('The limits exceed what the provider allows.');
    expect(dialog().open).toBe(true);
    expect(text(primary())).toBe('Keep as custom model');
    expect(resolved).toEqual([]);
  });

  it('lists the blockers of a 409 refusal', () => {
    setKind('system');
    openWith(SUBJECT);
    expectDryRun(SYSTEM_URL, 'gemini-3.8-flash').flush(preview([]));
    fixture.detectChanges();

    primary().click();
    fixture.detectChanges();
    http.expectOne(r => r.method === 'POST' && r.url === SYSTEM_URL && r.body.dryRun === false)
      .flush({ message: 'Run #12 uses this configuration.', blockers: [BLOCKER] }, { status: 409, statusText: 'Conflict' });
    fixture.detectChanges();

    const alert = q('.mrd-error')!;
    expect(text(alert.querySelector('.alert-body'))).toBe('Run #12 uses this configuration.');
    expect(Array.from(alert.querySelectorAll('li')).map(li => text(li))).toEqual([
      'Run #12 — as the assessor, started 2026-10-10 09:40 UTC.'
    ]);
    expect(dialog().open).toBe(true);
  });

  it('resets its state on each open', () => {
    setKind('user');
    openWith({ ...SUBJECT, availability: NOT_IN_CATALOG });
    type(field('maxInputTokens'), 'abc');
    blur(field('maxInputTokens'));
    expect(field('maxInputTokens').getAttribute('aria-invalid')).toBe('true');

    openWith({ ...SUBJECT, availability: NOT_IN_CATALOG });
    expect(field('maxInputTokens').value).toBe('200000');
    expect(field('maxInputTokens').hasAttribute('aria-invalid')).toBe(false);
  });
});
