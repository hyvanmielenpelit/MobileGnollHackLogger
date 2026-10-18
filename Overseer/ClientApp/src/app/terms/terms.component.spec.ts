import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { TermsComponent } from './terms.component';
import { OVERSEER_TERMS_KEY, TERMS_ENDPOINT, TermsDocument } from '../services/terms.service';

const TERMS_URL = `${TERMS_ENDPOINT}/${OVERSEER_TERMS_KEY}`;
const WIKI_URL = 'https://wiki.example/Gnoll%20Overseer%20Terms%20of%20Service.md';

const terms = (overrides: Partial<TermsDocument> = {}): TermsDocument => ({
  key: OVERSEER_TERMS_KEY,
  title: 'Gnoll Overseer Terms of Service',
  version: '2026-10-18',
  versionLabel: '18 October 2026',
  markdown: '## 1. Plain-Language Summary\n\nPlay GnollHack and be fair.',
  contentSha256: 'd'.repeat(64),
  wikiUrl: WIKI_URL,
  privacyPolicyUrl: null,
  ...overrides
});

describe('TermsComponent', () => {
  let fixture: ComponentFixture<TermsComponent>;
  let http: HttpTestingController;
  let host: HTMLElement;

  const text = () => (host.textContent ?? '').replace(/\s+/g, ' ');

  /** Signed out: no AuthService stub and no auth request, only the anonymous document fetch. */
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [TermsComponent],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()]
    }).compileComponents();

    fixture = TestBed.createComponent(TermsComponent);
    http = TestBed.inject(HttpTestingController);
    host = fixture.nativeElement as HTMLElement;
    fixture.detectChanges();
  });

  afterEach(() => http.verify());

  it('renders the document for a signed-out visitor', () => {
    expect(text()).toContain('Loading the Terms of Service…');

    const req = http.expectOne(TERMS_URL);
    expect(req.request.method).toBe('GET');
    req.flush(terms());
    fixture.detectChanges();

    const h1s = host.querySelectorAll('h1');
    expect(h1s.length).toBe(1);
    expect(h1s[0].textContent!.trim()).toBe('Gnoll Overseer Terms of Service');
    expect(text()).toContain('Last updated 18 October 2026');
    expect(host.querySelector('.terms-markdown h2')?.textContent).toContain('Plain-Language Summary');
    expect(text()).toContain('Play GnollHack and be fair.');

    const wiki = host.querySelector<HTMLAnchorElement>('.terms-wiki-link')!;
    expect(wiki.textContent!.trim()).toBe('Open on the wiki');
    expect(wiki.getAttribute('href')).toBe(WIKI_URL);
    expect(wiki.target).toBe('_blank');
    expect(wiki.rel).toContain('noopener');

    expect(host.querySelector('a.nav-back-link')?.getAttribute('href')).toBe('/chat');
  });

  it('offers the wiki link alone when the server could not read the document', () => {
    http.expectOne(TERMS_URL).flush(terms({ markdown: null, contentSha256: null }));
    fixture.detectChanges();

    expect(text()).toContain('The Terms could not be loaded here.');
    const link = host.querySelector<HTMLAnchorElement>('.terms-fallback a')!;
    expect(link.getAttribute('href')).toBe(WIKI_URL);
    expect(link.target).toBe('_blank');
    expect(host.querySelector('.terms-markdown')).toBeNull();
  });

  it('shows a load error with Retry, which fetches the document again', () => {
    http.expectOne(TERMS_URL).flush('boom', { status: 500, statusText: 'Server Error' });
    fixture.detectChanges();

    expect(text()).toContain('The Terms of Service could not be loaded.');
    host.querySelector<HTMLButtonElement>('.terms-retry')!.click();
    http.expectOne(TERMS_URL).flush(terms());
    fixture.detectChanges();

    expect(host.querySelector('.terms-markdown h2')?.textContent).toContain('Plain-Language Summary');
  });
});
