import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { PrivacyComponent } from './privacy.component';

describe('PrivacyComponent', () => {
  let component: PrivacyComponent;
  let fixture: ComponentFixture<PrivacyComponent>;
  let host: HTMLElement;

  const textOf = (el: HTMLElement): string => (el.textContent ?? '').replace(/\s+/g, ' ').trim();

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [PrivacyComponent],
      providers: [provideRouter([])]
    }).compileComponents();

    fixture = TestBed.createComponent(PrivacyComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
    host = fixture.nativeElement as HTMLElement;
  });

  it('should create', () => {
    expect(component).toBeTruthy();
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
      expect(matches.length)
        .withContext(`section id ${section.id}`)
        .toBe(1);
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
      expect(id.length).withContext('a table of contents link must name a section').toBeGreaterThan(0);
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
      'The call to the AI provider', 'Error telemetry', 'Cost and token accounting']) {
      expect(text).withContext(label).toContain(label);
    }
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
      expect(text).withContext(entry).toContain(entry);
    }
  });

  it('should say that RAM is not a legal or forensic boundary', () => {
    const text = textOf(host.querySelector('#limitations') as HTMLElement);
    expect(text).toContain('RAM is not a legal or forensic boundary');
    expect(text).toContain('The conversation still reaches the AI provider');
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
      expect(text).withContext(name).toContain(name);
    }

    const fontsNote = subprocessors.querySelector('.fonts-note') as HTMLElement;
    expect(fontsNote).toBeTruthy();
    expect(textOf(fontsNote)).toContain('Google Fonts is not a subprocessor.');
  });

  it('should note that a confidential chat blocks the GitHub tools', () => {
    const text = textOf(host.querySelector('#subprocessors') as HTMLElement);
    expect(text).toContain('This is the one category a confidential chat blocks');
  });

  it('should render each operator placeholder as visibly unfilled', () => {
    const gaps = host.querySelector('#operator-gaps') as HTMLElement;
    expect(gaps).toBeTruthy();

    const items = gaps.querySelectorAll('.operator-gap');
    expect(items.length).toBe(component.operatorGaps.length);

    items.forEach((item, index) => {
      const flag = item.querySelector('.operator-gap-flag') as HTMLElement;
      expect(flag).withContext(`placeholder ${index} flag`).toBeTruthy();
      expect(textOf(flag)).toBe('Not supplied by the operator');
      expect(textOf(item as HTMLElement)).toContain(component.operatorGaps[index].title);
    });

    expect(textOf(gaps)).toContain('this notice is incomplete rather than');
  });

  it('should name every statement only the operator can make', () => {
    const text = textOf(host.querySelector('#operator-gaps') as HTMLElement);
    for (const expected of [
      'The legal entity responsible for this deployment',
      'A contact address for data-subject requests',
      'Whether a data processing agreement is offered, and on what terms',
      'How a personal-data breach is assessed and notified, and within what time',
      'Where data is stored and processed, and in which regions inference runs',
      'How long error telemetry is kept'
    ]) {
      expect(text).withContext(expected).toContain(expected);
    }
  });

  it('should scroll wide tables inside their own container', () => {
    const tables = Array.from(host.querySelectorAll('table'));
    expect(tables.length).toBeGreaterThan(0);

    for (const table of tables) {
      expect(table.parentElement?.classList.contains('privacy-table-scroll'))
        .withContext('every table sits in a horizontally scrolling wrapper')
        .toBeTrue();
      expect(table.querySelector('caption')).withContext('every table is named').toBeTruthy();
    }
  });

  it('should use no title attributes and no emoji in the rendered markup', () => {
    expect(host.querySelectorAll('[title]').length).toBe(0);

    const emoji = /[\u{1F300}-\u{1FAFF}\u{2600}-\u{27BF}\u{FE0F}]/u;
    expect(emoji.test(host.innerHTML)).toBeFalse();
  });
});
