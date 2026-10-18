import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TermsAcceptedEvent, TermsDialogComponent } from './terms-dialog.component';
import { OVERSEER_TERMS_KEY, TERMS_ENDPOINT, TermsDocument } from '../../services/terms.service';

const TERMS_URL = `${TERMS_ENDPOINT}/${OVERSEER_TERMS_KEY}`;
const WIKI_URL = 'https://wiki.example/Gnoll%20Overseer%20Terms%20of%20Service.md';
const PRIVACY_URL = 'https://wiki.example/Gnoll%20Overseer%20Privacy%20Policy.md';
const SHA = 'c'.repeat(64);

const terms = (overrides: Partial<TermsDocument> = {}): TermsDocument => ({
  key: OVERSEER_TERMS_KEY,
  title: 'Gnoll Overseer Terms of Service',
  version: '2026-10-18',
  versionLabel: '18 October 2026',
  markdown: '## 1. Plain-Language Summary\n\nPlay GnollHack and be fair.',
  contentSha256: SHA,
  wikiUrl: WIKI_URL,
  privacyPolicyUrl: PRIVACY_URL,
  ...overrides
});

/** A dialog's close event is queued as a task; wait for the event itself, not a fixed number of ticks. */
function nextClose(dialog: HTMLDialogElement): Promise<void> {
  return new Promise<void>(resolve => dialog.addEventListener('close', () => resolve(), { once: true }));
}

describe('TermsDialogComponent', () => {
  let fixture: ComponentFixture<TermsDialogComponent>;
  let component: TermsDialogComponent;
  let http: HttpTestingController;

  const dialog = () => fixture.nativeElement.querySelector('dialog.terms-dialog') as HTMLDialogElement;
  const find = <T extends Element = HTMLElement>(selector: string) => dialog().querySelector<T>(selector);
  const text = () => dialog().textContent!.replace(/\s+/g, ' ');

  /** Opens the dialog and answers its document request. */
  function openWith(mode: 'view' | 'gate', doc: TermsDocument = terms(), sessionId?: number): void {
    component.open(mode, sessionId);
    http.expectOne(TERMS_URL).flush(doc);
    fixture.detectChanges();
  }

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [TermsDialogComponent],
      providers: [provideHttpClient(), provideHttpClientTesting()]
    }).compileComponents();
    fixture = TestBed.createComponent(TermsDialogComponent);
    component = fixture.componentInstance;
    http = TestBed.inject(HttpTestingController);
    fixture.detectChanges();
  });

  afterEach(() => {
    component.close();
    http.verify();
  });

  it('opens modally, focuses its h1 title and renders the document with its date and wiki link', () => {
    openWith('view');

    expect(dialog().open).toBe(true);
    const heading = find<HTMLHeadingElement>('h1#termsDialogTitle')!;
    expect(heading.textContent!.trim()).toBe('Gnoll Overseer Terms of Service');
    expect(heading.getAttribute('tabindex')).toBe('-1');
    expect(document.activeElement).toBe(heading);
    expect(text()).toContain('Last updated 18 October 2026');

    const region = find('.terms-text')!;
    expect(region.getAttribute('role')).toBe('region');
    expect(region.getAttribute('aria-label')).toBe('Terms of Service text');
    expect(region.getAttribute('tabindex')).toBe('0');
    expect(getComputedStyle(region).overscrollBehaviorY).toBe('contain');
    expect(region.querySelector('h2')?.textContent).toContain('Plain-Language Summary');

    const wiki = find<HTMLAnchorElement>('.terms-wiki-link')!;
    expect(wiki.textContent!.trim()).toBe('Open on the wiki');
    expect(wiki.getAttribute('href')).toBe(WIKI_URL);
    expect(wiki.target).toBe('_blank');
    expect(wiki.rel).toContain('noopener');
  });

  it('formats the version when the server gives no label', () => {
    openWith('view', terms({ version: '2026-10-18.2', versionLabel: null }));
    expect(text()).toContain('Last updated 18 October 2026 (revision 2)');
  });

  it('has a named close button and light dismiss in view mode, and no agreement', async () => {
    let closed = 0;
    component.closed.subscribe(() => closed++);
    openWith('view');

    expect(dialog().getAttribute('closedby')).toBe('any');
    expect(find('.terms-agree-input')).toBeNull();
    expect(find('.terms-accept')).toBeNull();

    const close = find<HTMLButtonElement>('button.terms-close')!;
    expect(close.getAttribute('aria-label')).toBe('Close the Terms of Service');
    expect(close.hasAttribute('title')).toBe(false);

    const closing = nextClose(dialog());
    close.click();
    await closing;

    expect(dialog().open).toBe(false);
    expect(closed).toBe(1);
  });

  it('keeps Accept disabled until the agreement is checked, then records the acceptance', async () => {
    const accepted: TermsAcceptedEvent[] = [];
    component.accepted.subscribe(event => accepted.push(event));
    openWith('gate', terms(), 7);

    expect(find('button.terms-close')).toBeNull();
    const box = find<HTMLInputElement>('.terms-agree-input')!;
    const accept = find<HTMLButtonElement>('.terms-accept')!;
    expect(box.checked).toBe(false);
    expect(accept.textContent!.trim()).toBe('Accept and continue');
    expect(accept.disabled).toBe(true);

    box.click();
    fixture.detectChanges();
    expect(accept.disabled).toBe(false);

    accept.click();
    fixture.detectChanges();
    expect(accept.disabled).toBe(true);

    const req = http.expectOne(`${TERMS_URL}/accept`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ version: '2026-10-18', contentSha256: SHA, sessionId: 7 });

    const closing = nextClose(dialog());
    req.flush({ accepted: true, greetingStarted: true });
    await closing;

    expect(dialog().open).toBe(false);
    expect(accepted).toEqual([{ greetingStarted: true }]);
  });

  it('carries the approved agreement statement and its two links', () => {
    openWith('gate');

    const label = find<HTMLLabelElement>('label.terms-agree')!;
    expect(label.textContent!.replace(/\s+/g, ' ').trim()).toBe(
      'I am at least 13 years old, and if I am under 18 my parent or guardian has agreed to these Terms. '
      + 'I agree to the Gnoll Overseer Terms of Service, and I have read the Gnoll Overseer Privacy Policy.');

    const privacy = find<HTMLAnchorElement>('.terms-privacy-link')!;
    expect(privacy.getAttribute('href')).toBe(PRIVACY_URL);
    expect(privacy.target).toBe('_blank');
    expect(privacy.rel).toContain('noopener');
  });

  it('scrolls the text back to the top from the agreement link without checking the box', () => {
    const long = Array.from({ length: 80 }, (_, i) => `Paragraph ${i + 1} of the Terms.`).join('\n\n');
    openWith('gate', terms({ markdown: long }));

    const region = find('.terms-text')!;
    region.scrollTop = 200;
    find<HTMLAnchorElement>('.terms-top-link')!.click();
    fixture.detectChanges();

    expect(region.scrollTop).toBe(0);
    expect(document.activeElement).toBe(region);
    expect(find<HTMLInputElement>('.terms-agree-input')!.checked).toBe(false);
  });

  it('ignores Escape in gate mode', () => {
    openWith('gate');
    expect(dialog().getAttribute('closedby')).toBe('none');

    const cancel = new Event('cancel', { cancelable: true });
    dialog().dispatchEvent(cancel);

    expect(cancel.defaultPrevented).toBe(true);
    expect(dialog().open).toBe(true);
  });

  it('shows the gate again when something else closes it', async () => {
    let closed = 0;
    component.closed.subscribe(() => closed++);
    openWith('gate');

    const closing = nextClose(dialog());
    dialog().close();
    await closing;

    expect(dialog().open).toBe(true);
    expect(closed).toBe(0);
  });

  it('emits declined and closes on Not now', async () => {
    let declined = 0;
    let closed = 0;
    component.declined.subscribe(() => declined++);
    component.closed.subscribe(() => closed++);
    openWith('gate');

    const closing = nextClose(dialog());
    find<HTMLButtonElement>('.terms-decline')!.click();
    await closing;

    expect(dialog().open).toBe(false);
    expect(declined).toBe(1);
    expect(closed).toBe(1);
  });

  it('reloads the Terms, unchecks the agreement and says so when they changed meanwhile', () => {
    openWith('gate');
    const box = find<HTMLInputElement>('.terms-agree-input')!;
    const accept = find<HTMLButtonElement>('.terms-accept')!;
    box.click();
    fixture.detectChanges();
    accept.click();

    http.expectOne(`${TERMS_URL}/accept`).flush(
      { error: 'terms_version_changed', requiredVersion: '2026-10-19' },
      { status: 409, statusText: 'Conflict' });
    http.expectOne(TERMS_URL).flush(terms({
      version: '2026-10-19',
      versionLabel: '19 October 2026',
      markdown: '## 1. Changed\n\nThe new text.'
    }));
    fixture.detectChanges();

    expect(dialog().open).toBe(true);
    expect(box.checked).toBe(false);
    expect(accept.disabled).toBe(true);
    expect(text()).toContain('The Terms of Service have just changed. Please review them again.');
    expect(text()).toContain('Last updated 19 October 2026');
    expect(text()).toContain('The new text.');

    box.click();
    fixture.detectChanges();
    accept.click();
    const retry = http.expectOne(`${TERMS_URL}/accept`);
    expect(retry.request.body).toEqual({ version: '2026-10-19', contentSha256: SHA });
    retry.flush({ accepted: true, greetingStarted: false });
  });

  it('shows a short error and stays open when the acceptance fails otherwise', () => {
    openWith('gate');
    find<HTMLInputElement>('.terms-agree-input')!.click();
    fixture.detectChanges();
    find<HTMLButtonElement>('.terms-accept')!.click();

    http.expectOne(`${TERMS_URL}/accept`).flush('boom', { status: 500, statusText: 'Server Error' });
    fixture.detectChanges();

    expect(dialog().open).toBe(true);
    expect(find('.terms-submit-error')?.textContent).toContain('Your acceptance could not be saved. Please try again.');
    expect(find<HTMLButtonElement>('.terms-accept')!.disabled).toBe(false);
  });

  it('offers the wiki link alone when the server could not read the document', () => {
    openWith('gate', terms({ markdown: null, contentSha256: null }));

    expect(text()).toContain('The Terms could not be loaded here.');
    const link = find<HTMLAnchorElement>('.terms-fallback a')!;
    expect(link.textContent!.trim()).toBe('Read them on the wiki.');
    expect(link.getAttribute('href')).toBe(WIKI_URL);
    expect(link.target).toBe('_blank');
    expect(link.rel).toContain('noopener');
    expect(find('.terms-markdown')).toBeNull();

    // The Terms can still be read on the wiki and accepted by their version.
    find<HTMLInputElement>('.terms-agree-input')!.click();
    fixture.detectChanges();
    expect(find<HTMLButtonElement>('.terms-accept')!.disabled).toBe(false);
  });

  it('cannot be accepted when no version is known', () => {
    openWith('gate', terms({ markdown: null, contentSha256: null, version: null, versionLabel: null }));

    find<HTMLInputElement>('.terms-agree-input')!.click();
    fixture.detectChanges();
    expect(find<HTMLButtonElement>('.terms-accept')!.disabled).toBe(true);
  });

  it('shows a load error with Retry, which fetches the document again', () => {
    component.open('view');
    http.expectOne(TERMS_URL).flush('boom', { status: 500, statusText: 'Server Error' });
    fixture.detectChanges();

    expect(text()).toContain('The Terms of Service could not be loaded.');
    find<HTMLButtonElement>('.terms-retry')!.click();
    http.expectOne(TERMS_URL).flush(terms());
    fixture.detectChanges();

    expect(find('.terms-text h2')?.textContent).toContain('Plain-Language Summary');
  });

  it('stops its own close and cancel events', () => {
    openWith('view');

    const host = fixture.nativeElement as HTMLElement;
    const reached: string[] = [];
    host.addEventListener('cancel', () => reached.push('cancel'));
    host.addEventListener('close', () => reached.push('close'));
    dialog().dispatchEvent(new Event('cancel', { bubbles: true }));
    dialog().dispatchEvent(new Event('close', { bubbles: true }));

    expect(reached).toEqual([]);
  });
});
