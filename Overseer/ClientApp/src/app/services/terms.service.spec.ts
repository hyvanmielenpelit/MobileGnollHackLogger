import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';

import {
  OVERSEER_TERMS_KEY,
  TERMS_ENDPOINT,
  TermsDocument,
  TermsService,
  formatTermsVersion,
  isTermsVersionChanged,
  withoutLastUpdatedLine
} from './terms.service';

const TERMS_URL = `${TERMS_ENDPOINT}/${OVERSEER_TERMS_KEY}`;

const terms = (overrides: Partial<TermsDocument> = {}): TermsDocument => ({
  key: OVERSEER_TERMS_KEY,
  title: 'Gnoll Overseer Terms of Service',
  version: '2026-10-18',
  versionLabel: '18 October 2026',
  markdown: '## 1. Summary\n\nPlay nice.',
  contentSha256: 'a'.repeat(64),
  wikiUrl: 'https://wiki.example/Gnoll%20Overseer%20Terms%20of%20Service.md',
  privacyPolicyUrl: 'https://wiki.example/Gnoll%20Overseer%20Privacy%20Policy.md',
  ...overrides
});

describe('formatTermsVersion', () => {
  it('formats an ISO date with an English month name', () => {
    expect(formatTermsVersion('2026-10-18')).toBe('18 October 2026');
    expect(formatTermsVersion('2026-01-05')).toBe('5 January 2026');
  });

  it('adds the revision, reading revision 1 as none', () => {
    expect(formatTermsVersion('2026-10-18.2')).toBe('18 October 2026 (revision 2)');
    expect(formatTermsVersion('2026-10-18.10')).toBe('18 October 2026 (revision 10)');
    expect(formatTermsVersion('2026-10-18.1')).toBe('18 October 2026');
  });

  it('returns unparsable input unchanged and null as null', () => {
    expect(formatTermsVersion('next week')).toBe('next week');
    expect(formatTermsVersion('2026-13-01')).toBe('2026-13-01');
    expect(formatTermsVersion('2026-02-30')).toBe('2026-02-30');
    expect(formatTermsVersion(null)).toBeNull();
    expect(formatTermsVersion(undefined)).toBeNull();
  });
});

describe('isTermsVersionChanged', () => {
  it('recognizes only the terms_version_changed 409', () => {
    expect(isTermsVersionChanged(new HttpErrorResponse({ status: 409, error: { error: 'terms_version_changed', requiredVersion: '2026-10-19' } }))).toBe(true);
    expect(isTermsVersionChanged(new HttpErrorResponse({ status: 409, error: { error: 'something_else' } }))).toBe(false);
    expect(isTermsVersionChanged(new HttpErrorResponse({ status: 400, error: { error: 'terms_version_changed' } }))).toBe(false);
    expect(isTermsVersionChanged(new Error('terms_version_changed'))).toBe(false);
  });
});

describe('TermsService', () => {
  let service: TermsService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()]
    });
    service = TestBed.inject(TermsService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('uses the controller route prefix', () => {
    expect(TERMS_ENDPOINT).toBe('/api/legal/terms');
  });

  it('fetches a document once and shares it between callers', () => {
    const seen: TermsDocument[] = [];
    service.getTerms().subscribe(doc => seen.push(doc));
    service.getTerms(OVERSEER_TERMS_KEY).subscribe(doc => seen.push(doc));

    const req = http.expectOne(TERMS_URL);
    expect(req.request.method).toBe('GET');
    req.flush(terms());

    service.getTerms().subscribe(doc => seen.push(doc));
    http.expectNone(TERMS_URL);
    expect(seen.length).toBe(3);
    expect(seen.every(doc => doc.version === '2026-10-18')).toBe(true);
  });

  it('fetches again on reload and keeps the new document for later callers', () => {
    service.getTerms().subscribe();
    http.expectOne(TERMS_URL).flush(terms());

    let reloaded: TermsDocument | undefined;
    service.getTerms(OVERSEER_TERMS_KEY, true).subscribe(doc => reloaded = doc);
    http.expectOne(TERMS_URL).flush(terms({ version: '2026-10-19' }));
    expect(reloaded?.version).toBe('2026-10-19');

    let later: TermsDocument | undefined;
    service.getTerms().subscribe(doc => later = doc);
    http.expectNone(TERMS_URL);
    expect(later?.version).toBe('2026-10-19');
  });

  it('does not keep a failed fetch', () => {
    let failed = false;
    service.getTerms().subscribe({ error: () => failed = true });
    http.expectOne(TERMS_URL).flush('boom', { status: 500, statusText: 'Server Error' });
    expect(failed).toBe(true);

    let doc: TermsDocument | undefined;
    service.getTerms().subscribe(d => doc = d);
    http.expectOne(TERMS_URL).flush(terms());
    expect(doc?.title).toBe('Gnoll Overseer Terms of Service');
  });

  it('posts the acceptance with the version, hash and session', () => {
    let result: { accepted: boolean; greetingStarted: boolean } | undefined;
    service.accept(OVERSEER_TERMS_KEY, { version: '2026-10-18', contentSha256: 'b'.repeat(64), sessionId: 42 })
      .subscribe(r => result = r);

    const req = http.expectOne(`${TERMS_URL}/accept`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ version: '2026-10-18', contentSha256: 'b'.repeat(64), sessionId: 42 });
    req.flush({ accepted: true, greetingStarted: true });

    expect(result).toEqual({ accepted: true, greetingStarted: true });
  });
});

describe('withoutLastUpdatedLine', () => {
  it('drops the Last updated line of the document and the blank line after it', () => {
    expect(withoutLastUpdatedLine('**Last updated:** 18 October 2026\r\n\r\nThese Terms apply.\r\n')).toBe('These Terms apply.\n');
  });

  it('leaves a document without the line unchanged', () => {
    expect(withoutLastUpdatedLine('These Terms apply.')).toBe('These Terms apply.');
  });

  it('gives an empty string for no text', () => {
    expect(withoutLastUpdatedLine(null)).toBe('');
  });
});