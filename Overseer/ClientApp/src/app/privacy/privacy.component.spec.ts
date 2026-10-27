import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { OperatorStatementKey, OperatorStatementsResponse, PrivacyComponent } from './privacy.component';

describe('PrivacyComponent', () => {
  let component: PrivacyComponent;
  let fixture: ComponentFixture<PrivacyComponent>;
  let host: HTMLElement;
  let httpMock: HttpTestingController;

  const operatorUrl = '/api/legal/operator';

  const textOf = (el: HTMLElement): string => (el.textContent ?? '').replace(/\s+/g, ' ').trim();

  /** Answers the operator statements request and lets the view pick up the result. */
  const respondWith = async (body: OperatorStatementsResponse): Promise<void> => {
    const req = httpMock.expectOne(operatorUrl);
    expect(req.request.method).toBe('GET');
    req.flush(body);
    await fixture.whenStable();
    fixture.detectChanges();
  };

  /** The rendered operator item for a key, found by its position in the fallback list. */
  const operatorItem = (key: OperatorStatementKey): HTMLElement => {
    const index = component.operatorGaps.findIndex(gap => gap.key === key);
    expect(index, `operator key ${key}`).toBeGreaterThanOrEqual(0);
    return host.querySelectorAll<HTMLElement>('#operator-statements .operator-gap')[index];
  };

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [PrivacyComponent],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()]
    }).compileComponents();

    httpMock = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(PrivacyComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
    host = fixture.nativeElement as HTMLElement;
  });

  afterEach(() => {
    // Tests that do not answer the operator request render with the fallbacks; discard it.
    httpMock.match(operatorUrl);
    httpMock.verify();
  });

  it('should render one h1 and an h2 for every section, skipping no heading level', () => {
    const h1s = host.querySelectorAll('h1');
    expect(h1s.length).toBe(1);
    expect(textOf(h1s[0] as HTMLElement)).toBe('Privacy Notice');

    // One h2 per section, plus the table of contents heading.
    const sectionCount = host.querySelectorAll('section[id]').length;
    expect(host.querySelectorAll('h2').length).toBe(sectionCount + 1);
    expect(host.querySelectorAll('h4, h5, h6').length).toBe(0);
  });

  it('should render every declared section id exactly once', () => {
    for (const section of component.sections) {
      const matches = host.querySelectorAll(`section[id="${section.id}"]`);
      expect(matches.length, `section id ${section.id}`).toBe(1);
    }

    expect(host.querySelectorAll('section[id]').length).toBe(component.sections.length);
  });

  it('should link the table of contents to exactly the section ids that exist', () => {
    const links = Array.from(host.querySelectorAll<HTMLAnchorElement>('.privacy-toc a[href]'));
    const linkedIds = links.map(a => (a.getAttribute('href') ?? '').replace(/^.*#/, ''));
    const sectionIds = Array.from(host.querySelectorAll('section[id]')).map(s => s.id);

    expect(linkedIds.length).toBe(sectionIds.length);
    expect(linkedIds).toEqual(sectionIds);
    for (const id of linkedIds) {
      expect(id.length, 'a table of contents link must name a section').toBeGreaterThan(0);
    }
  });

  it('should state the declared ceiling of the data sensitivity ladder', () => {
    const ladder = host.querySelector('#data-sensitivity-ladder') as HTMLElement;
    expect(ladder).toBeTruthy();

    const rungs = ladder.querySelectorAll('.ladder-rung');
    expect(rungs.length).toBe(component.ladder.length);

    const ceilings = ladder.querySelectorAll('.ladder-rung.is-ceiling');
    expect(ceilings.length).toBe(1);
    expect(textOf(ceilings[0] as HTMLElement)).toContain('Yes — this is the declared ceiling.');

    const sectionText = textOf(ladder);
    expect(sectionText).toContain('Not supported.');
    expect(sectionText).toContain('Protected identically in technical terms, but not claimed.');
  });

  it('should render the conversation data map with a row for each kind of content', () => {
    const map = host.querySelector('#conversation-data-map') as HTMLElement;
    expect(map).toBeTruthy();

    const rows = map.querySelectorAll('tbody tr');
    expect(rows.length).toBe(component.dataMap.length);

    const text = textOf(map);
    for (const label of ['The message you type', 'Documents and images you attach',
      'The call to the AI provider', 'Error telemetry', 'Cost and token accounting',
      'Activity counts', 'Security events', 'Provider safety identifier', 'Terms of Service acceptance']) {
      expect(text, label).toContain(label);
    }
  });

  it('should list Terms of Service acceptance as kept until account deletion and shared with nobody', () => {
    const rows = Array.from(host.querySelectorAll<HTMLElement>('#conversation-data-map tbody tr'));
    const terms = rows.find(r => textOf(r.querySelector('th') as HTMLElement) === 'Terms of Service acceptance');
    expect(terms).toBeTruthy();

    const cells = Array.from(terms!.querySelectorAll<HTMLElement>('td')).map(textOf);
    expect(cells.length).toBe(3);
    expect(cells[0]).toContain('Overseer’s database');
    expect(cells[0]).toContain('the version, the time, and how you accepted');
    expect(cells[0]).toContain('No IP address');
    expect(cells[1]).toBe('Until your account is deleted.');
    expect(cells[2]).toBe('Nobody outside Overseer.');
  });

  it('should say security events hold no message content and which providers get the safety identifier', () => {
    const rows = Array.from(host.querySelectorAll<HTMLElement>('#conversation-data-map tbody tr'));
    const row = (title: string) => rows.find(r => textOf(r.querySelector('th') as HTMLElement) === title);

    const security = textOf(row('Security events')!);
    expect(security).toContain('identifiers and codes only, never message content');
    expect(security).toContain('An IP address is recorded only on sign-in and rate-limit events.');
    expect(security).toContain('180 days by default.');
    expect(security).toContain('rejected attachment, snapshot guard refusal, report,');
    expect(security).toContain('never message content, except that a game snapshot, message history, file listing or ' +
      'game-tool result the snapshot guard flagged is kept encrypted so an administrator can review it, in every chat mode.');
    expect(security).toContain('The flagged game text is deleted after 30 days, also while a review keeps its event.');

    const message = textOf(row('The message you type')!);
    expect(message).toContain('Game text the snapshot guard flags is the one exception in every mode: see Security events.');

    const safety = textOf(row('Provider safety identifier')!);
    expect(safety).toContain('OpenAI and Anthropic');
    expect(safety).toContain('not sent on your own API key or to a custom endpoint');
    expect(safety).toContain('Google receives none');
  });

  it('should say who sees the usage records and that activity counts carry no content', () => {
    const rows = Array.from(host.querySelectorAll<HTMLElement>('#conversation-data-map tbody tr'));
    const row = (title: string) => rows.find(r => textOf(r.querySelector('th') as HTMLElement) === title);

    const cost = textOf(row('Cost and token accounting')!);
    expect(cost).toContain('including for calls on your own API key');
    expect(cost).toContain('Administrators can see both with your user name.');
    expect(cost).toContain('The attempt records are kept 400 days.');
    expect(cost).toContain('deleted with your account');

    const activity = textOf(row('Activity counts')!);
    expect(activity).toContain('never their content');
    expect(activity).toContain('a daily total that names nobody');
    expect(activity).toContain('Until your account is deleted.');
  });

  it('should state acceptable use, link each provider policy and mention the deletion review', () => {
    const section = host.querySelector('#acceptable-use') as HTMLElement;
    expect(section).toBeTruthy();

    const text = textOf(section);
    expect(text).toContain('Overseer is for questions about GnollHack and NetHack.');
    expect(text).toContain('Misuse can lead to limits on your AI use or to a block of your access to the AI chat.');

    const hrefs = Array.from(section.querySelectorAll<HTMLAnchorElement>('a[href]')).map(a => a.getAttribute('href'));
    expect(hrefs).toEqual([
      'https://openai.com/policies/usage-policies/',
      'https://www.anthropic.com/legal/aup',
      'https://policies.google.com/terms/generative-ai/use-policy',
      '/terms'
    ]);
    for (const link of Array.from(section.querySelectorAll<HTMLAnchorElement>('a[target="_blank"]'))) {
      expect(link.rel).toContain('noopener');
    }

    const review = section.querySelector('.account-deletion-review') as HTMLElement;
    expect(textOf(review)).toBe('A request to delete your account may be held for review by an administrator when there is ' +
      'evidence of misuse that may be illegal. You are told that it is under review, and you are e-mailed when the ' +
      'deletion is completed or declined.');

    expect(textOf(section.querySelector('.snapshot-guard-use') as HTMLElement)).toBe('Game snapshots that contain text addressed to the AI ' +
      'assistant are not delivered to it and are recorded as security events; the flagged text is kept encrypted for 30 days for an ' +
      'administrator’s review, in incognito and confidential chats too, and repeated cases can suspend AI chat access automatically.');
  });

  it('should link the acceptable use section to the Terms of Service page', () => {
    const section = host.querySelector('#acceptable-use') as HTMLElement;
    const link = Array.from(section.querySelectorAll<HTMLAnchorElement>('a[href]'))
      .find(a => a.getAttribute('href') === '/terms');

    expect(link).toBeTruthy();
    expect(textOf(link!)).toBe('Gnoll Overseer Terms of Service');
    expect(link!.hasAttribute('target')).toBe(false);
    expect(textOf(section.querySelector('.terms-reference') as HTMLElement))
      .toBe('The rules for using Overseer are in the Gnoll Overseer Terms of Service.');
  });

  it('should render every limitation and overstate none of them', () => {
    const limits = host.querySelector('#limitations') as HTMLElement;
    expect(limits).toBeTruthy();
    expect(limits.querySelectorAll('tbody tr').length).toBe(component.limitations.length);

    const text = textOf(limits);

    // The entries a reader could otherwise get wrong. Asserted by their actual wording, because
    // softening any one of them is the failure this section exists to prevent.
    const nonNegotiable = [
      'The provider still sees the conversation.',
      'A provider’s retention posture is a claim about a contract, not a technical control.',
      'A normal chat’s content is stored in plain text.',
      'Crypto-shredding does not reach a backup taken before the shred.',
      'An incognito chat is not saved by Overseer; that does not make it private.',
      'Outbound secret masking is a reduction, not a guarantee.',
      'Prompt injection is bounded, not prevented.',
      'Retrieval sends fewer parts of a document, not none.',
      'Malware scanning is best-effort.',
      'No regulated-data support.'
    ];

    for (const entry of nonNegotiable) {
      expect(text, entry).toContain(entry);
    }
  });

  it('should say that RAM is not a legal or forensic boundary', () => {
    const text = textOf(host.querySelector('#limitations') as HTMLElement);
    expect(text).toContain('RAM is not a legal or forensic boundary');
    expect(text).toContain('The conversation still reaches the AI provider');
  });

  it('should name the snapshot guard exception on the incognito protection', () => {
    const text = textOf(host.querySelector('#limitations') as HTMLElement);
    expect(text).toContain('Incognito, which writes no chat, message, tool-call or attachment row and puts no file on disk; ' +
      'game text the snapshot guard flags is kept as a security event, see above');
  });

  it('should state the plaintext rule and name the most consequential readable category first', () => {
    const readable = host.querySelector('#readable-metadata') as HTMLElement;
    expect(readable).toBeTruthy();

    const text = textOf(readable);
    expect(text).toContain('only a named set of things is encrypted, and everything outside that set stays plain text');
    expect(text).toContain('In a normal chat nothing is encrypted at all, content included.');

    const entries = readable.querySelectorAll('.readable-entry');
    expect(entries.length).toBe(component.readableCategories.length);

    const primary = readable.querySelectorAll('.readable-entry.is-primary');
    expect(primary.length).toBe(1);
    expect(primary[0]).toBe(entries[0]);
    expect(textOf(primary[0] as HTMLElement)).toContain('Which provider saw the content');
  });

  it('should list every subprocessor and deny that Google Fonts is one', () => {
    const subprocessors = host.querySelector('#subprocessors') as HTMLElement;
    expect(subprocessors).toBeTruthy();
    expect(subprocessors.querySelectorAll('tbody tr').length).toBe(component.subprocessors.length);

    const text = textOf(subprocessors);
    for (const name of ['OpenAI', 'Anthropic', 'Google', 'Sentry', 'Azure Communication Services', 'GitHub']) {
      expect(text, name).toContain(name);
    }

    const fontsNote = subprocessors.querySelector('.fonts-note') as HTMLElement;
    expect(fontsNote).toBeTruthy();
    expect(textOf(fontsNote)).toContain('Google Fonts is not a subprocessor.');
  });

  it('should note that a confidential chat blocks the GitHub tools', () => {
    const text = textOf(host.querySelector('#subprocessors') as HTMLElement);
    expect(text).toContain('This is the one category a confidential chat blocks');
  });

  it('should render each unconfigured operator statement as visibly not supplied', async () => {
    await respondWith({ statements: {} });

    const statements = host.querySelector('#operator-statements') as HTMLElement;
    expect(statements).toBeTruthy();
    expect(textOf(statements.querySelector('h2') as HTMLElement)).toBe('7. Operator statements');

    const items = statements.querySelectorAll('.operator-gap');
    expect(items.length).toBe(component.operatorGaps.length);

    items.forEach((item, index) => {
      const flag = item.querySelector('.operator-gap-flag') as HTMLElement;
      expect(flag, `placeholder ${index} flag`).toBeTruthy();
      expect(textOf(flag)).toBe('Not supplied by the operator');
      expect(textOf(item as HTMLElement)).toContain(component.operatorGaps[index].title);
      expect(textOf(item as HTMLElement)).toContain(component.operatorGaps[index].detail);
      expect(item.querySelector('.operator-statement-text'), `placeholder ${index} statement`).toBeNull();
    });

    expect(textOf(statements.querySelector('p') as HTMLElement)).toBe('Statements only the operator of this deployment ' +
      'can make. Each names where it comes from. An item marked Not supplied has not been made yet, and nothing has ' +
      'been invented to fill it.');
  });

  it('should render a configured operator statement with its source, linked when it has a URL', async () => {
    await respondWith({
      statements: {
        legalEntity: {
          text: 'Example Games Oy, a company registered in Finland.',
          source: 'Operator configuration',
          sourceUrl: 'https://example.test/legal'
        },
        contact: {
          text: 'Write to the privacy contact on the account site.',
          source: 'Account site',
          sourceUrl: null
        }
      }
    });

    const legal = operatorItem('legalEntity');
    expect(legal.classList.contains('is-supplied')).toBe(true);
    expect(legal.querySelector('.operator-gap-flag')).toBeNull();
    expect(textOf(legal)).toContain('The legal entity responsible for this deployment');
    expect(textOf(legal.querySelector('.operator-statement-text') as HTMLElement))
      .toBe('Example Games Oy, a company registered in Finland.');
    expect(textOf(legal.querySelector('.operator-statement-source') as HTMLElement))
      .toBe('Source: Operator configuration (opens in a new tab)');

    const link = legal.querySelector<HTMLAnchorElement>('.operator-statement-source a') as HTMLAnchorElement;
    expect(link).toBeTruthy();
    expect(link.getAttribute('href')).toBe('https://example.test/legal');
    expect(link.target).toBe('_blank');
    expect(link.rel).toContain('noopener');

    const contact = operatorItem('contact');
    expect(contact.querySelector('.operator-gap-flag')).toBeNull();
    expect(textOf(contact.querySelector('.operator-statement-source') as HTMLElement)).toBe('Source: Account site');
    expect(contact.querySelector('.operator-statement-source a')).toBeNull();

    // A key the operator did not configure keeps its fallback.
    const agreement = operatorItem('dataProcessingAgreement');
    expect(agreement.classList.contains('is-supplied')).toBe(false);
    expect(textOf(agreement.querySelector('.operator-gap-flag') as HTMLElement)).toBe('Not supplied by the operator');
    expect(textOf(agreement)).toContain('This page does not state that one exists.');
  });

  it('should render the whole notice with the fallbacks when the operator request fails', async () => {
    httpMock.expectOne(operatorUrl).flush('Server error', { status: 500, statusText: 'Internal Server Error' });
    await fixture.whenStable();
    fixture.detectChanges();

    expect(host.querySelectorAll('section[id]').length).toBe(component.sections.length);

    const items = host.querySelectorAll('#operator-statements .operator-gap');
    expect(items.length).toBe(component.operatorGaps.length);
    items.forEach((item, index) => {
      expect(textOf(item.querySelector('.operator-gap-flag') as HTMLElement), `placeholder ${index} flag`)
        .toBe('Not supplied by the operator');
    });
    expect(host.querySelectorAll('#conversation-data-map tbody tr').length).toBe(component.dataMap.length);
  });

  it('should name every statement only the operator can make', () => {
    const text = textOf(host.querySelector('#operator-statements') as HTMLElement);
    for (const expected of [
      'The legal entity responsible for this deployment',
      'A contact address for data-subject requests',
      'Whether a data processing agreement is offered, and on what terms',
      'How a personal-data breach is assessed and notified, and within what time',
      'Where data is stored and processed, and in which regions inference runs',
      'How long error telemetry is kept',
      'The operator’s own acceptable-use terms'
    ]) {
      expect(text, expected).toContain(expected);
    }
  });

  it('should scroll wide tables inside their own container', () => {
    const tables = Array.from(host.querySelectorAll('table'));
    expect(tables.length).toBeGreaterThan(0);

    for (const table of tables) {
      expect(table.parentElement?.classList.contains('privacy-table-scroll'), 'every table sits in a horizontally scrolling wrapper').toBe(true);
      expect(table.querySelector('caption'), 'every table is named').toBeTruthy();
    }
  });

  it('should use no title attributes and no emoji in the rendered markup', () => {
    expect(host.querySelectorAll('[title]').length).toBe(0);

    const emoji = /[\u{1F300}-\u{1FAFF}\u{2600}-\u{27BF}\u{FE0F}]/u;
    expect(emoji.test(host.innerHTML)).toBe(false);
  });
});
