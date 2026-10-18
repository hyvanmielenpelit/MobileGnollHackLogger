import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';

import { buildCorpusProbeResult } from '../../../admin.component.testing';
import { CorpusSearchProbeComponent } from './corpus-search-probe.component';

const nf = new Intl.NumberFormat();
const GW_PROBE = '/api/admin/devtools/gnollhack-wiki/probe';

describe('CorpusSearchProbeComponent', () => {
  let fixture: ComponentFixture<CorpusSearchProbeComponent>;
  let el: HTMLElement;
  let http: HttpTestingController;

  const q = <T extends Element = HTMLElement>(selector: string) => el.querySelector<T>(selector);
  const text = (node: Element | null) => (node?.textContent ?? '').replace(/\s+/g, ' ').trim();
  const queryInput = () => q<HTMLInputElement>('#gw-probe-query')!;
  const runButton = () => q<HTMLButtonElement>('#gw-probe-run')!;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [CorpusSearchProbeComponent],
      providers: [provideHttpClient(), provideHttpClientTesting()]
    }).compileComponents();
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(CorpusSearchProbeComponent);
    el = fixture.nativeElement as HTMLElement;
    fixture.componentRef.setInput('kind', 'gnollhack-wiki');
    fixture.componentRef.setInput('toolName', 'wiki_search');
    fixture.componentRef.setInput('idPrefix', 'gw');
  });

  afterEach(() => {
    http.verify();
  });

  function render(inputs: Record<string, unknown> = {}): void {
    for (const [name, value] of Object.entries(inputs)) {
      fixture.componentRef.setInput(name, value);
    }
    fixture.detectChanges();
  }

  function type(value: string): void {
    queryInput().value = value;
    queryInput().dispatchEvent(new Event('input'));
  }

  function run(): void {
    runButton().click();
    fixture.detectChanges();
  }

  it('renders a labeled search form with a text-only Run probe submit button and the note', () => {
    render();
    expect(text(q('#gw-probe-title'))).toBe('Search probe');
    expect(q('section.gh-dash-card')!.getAttribute('aria-labelledby')).toBe('gw-probe-title');
    expect(queryInput().type).toBe('search');
    expect(text(q('label[for="gw-probe-query"]'))).toBe('Search terms');
    expect(queryInput().getAttribute('maxlength')).toBe('500');
    expect(runButton().type).toBe('submit');
    expect(runButton().classList).toContain('btn-gh');
    expect(text(runButton())).toBe('Run probe');
    expect(runButton().querySelector('svg')).toBeNull();
    expect(text(q('.csp-note'))).toBe("Runs the chat's own tool against the live index. Nothing is stored.");
  });

  it('has no filter select without both a label and options', () => {
    render({ filterLabel: 'Category' });
    expect(q('select')).toBeNull();
    render({ filterLabel: null, filterOptions: ['Monsters'] });
    expect(q('select')).toBeNull();
  });

  it('refuses an empty query without calling the server', () => {
    render();
    type('   ');
    run();
    const error = q('#gw-probe-query-error')!;
    expect(error.classList).toContain('gh-field-error');
    expect(text(error)).toBe('Enter something to search for.');
    expect(queryInput().getAttribute('aria-invalid')).toBe('true');
    expect(queryInput().getAttribute('aria-describedby')).toBe('gw-probe-query-error');
    http.expectNone(GW_PROBE);
  });

  it('sends the chosen filter, and no filter for Any', () => {
    render({ filterLabel: 'Category', filterOptions: ['Monsters', 'Items'] });
    const select = q<HTMLSelectElement>('#gw-probe-filter')!;
    expect(text(q('label[for="gw-probe-filter"]'))).toBe('Category');
    expect(Array.from(select.options).map(o => o.textContent!.trim())).toEqual(['Any', 'Monsters', 'Items']);

    type('gnoll');
    run();
    let request = http.expectOne(GW_PROBE);
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ query: 'gnoll' });
    request.flush(buildCorpusProbeResult());
    fixture.detectChanges();

    select.value = 'Monsters';
    select.dispatchEvent(new Event('change'));
    run();
    request = http.expectOne(GW_PROBE);
    expect(request.request.body).toEqual({ query: 'gnoll', filter: 'Monsters' });
    request.flush(buildCorpusProbeResult());
  });

  it('shows the running state, then the result line, the status word and the text', () => {
    render();
    type('gnoll lord');
    run();
    expect(text(q('.csp-status'))).toBe('Running wiki_search…');
    expect(runButton().getAttribute('aria-busy')).toBe('true');

    http.expectOne(GW_PROBE).flush(buildCorpusProbeResult({
      content: 'Gnoll lord\nA gnoll that leads.', lengthChars: 4812, maxResultLength: 10000, elapsedMs: 38
    }));
    fixture.detectChanges();

    const status = q('.csp-status')!;
    expect(status.getAttribute('role')).toBe('status');
    expect(status.querySelector('.gh-health-status')!.getAttribute('data-status')).toBe('ok');
    expect(text(status.querySelector('app-health-status'))).toBe('Found');
    expect(text(q('.csp-summary-text'))).toBe(`wiki_search · ${nf.format(4812)} of ${nf.format(10000)} characters · 38 ms`);
    const output = q<HTMLPreElement>('#gw-probe-output')!;
    expect(output.classList).toContain('gh-dash-pre');
    expect(output.textContent).toBe('Gnoll lord\nA gnoll that leads.');
    expect(q('.csp-expand')).toBeNull();
    expect(q('.csp-over-cap')).toBeNull();
    expect(runButton().hasAttribute('aria-busy')).toBe(false);
  });

  it('shows a refusal as Tool refused with the tool\'s error', () => {
    render();
    type('gnoll');
    run();
    http.expectOne(GW_PROBE).flush(buildCorpusProbeResult({
      success: false, content: null, error: 'The wiki is still being indexed.'
    }));
    fixture.detectChanges();
    expect(q('.csp-status .gh-health-status')!.getAttribute('data-status')).toBe('warning');
    expect(text(q('.csp-status app-health-status'))).toBe('Tool refused');
    expect(q('#gw-probe-output')!.textContent).toBe('The wiki is still being indexed.');
  });

  it('renders markup in the result as plain text', () => {
    render();
    type('gnoll');
    run();
    http.expectOne(GW_PROBE).flush(buildCorpusProbeResult({ content: '<b>Gnoll</b>' }));
    fixture.detectChanges();
    const output = q('#gw-probe-output')!;
    expect(output.querySelector('b')).toBeNull();
    expect(output.textContent).toBe('<b>Gnoll</b>');
  });

  it('warns when the result is longer than the chat\'s cap', () => {
    render();
    type('gnoll');
    run();
    http.expectOne(GW_PROBE).flush(buildCorpusProbeResult({ content: 'x', lengthChars: 13000, maxResultLength: 10000 }));
    fixture.detectChanges();
    expect(text(q('.csp-over-cap'))).toBe(`Longer than the chat's limit: the chat sees only the first ${nf.format(10000)} characters.`);
  });

  it('collapses a long result behind Show all and copies the full text', async () => {
    const writeText = vi.fn().mockResolvedValue(undefined);
    const original = Object.getOwnPropertyDescriptor(navigator, 'clipboard');
    Object.defineProperty(navigator, 'clipboard', { value: { writeText }, configurable: true, writable: true });
    try {
      render();
      type('gnoll');
      run();
      const long = Array.from({ length: 60 }, (_, i) => `Line ${i + 1}`).join('\n');
      http.expectOne(GW_PROBE).flush(buildCorpusProbeResult({ content: long, lengthChars: long.length }));
      fixture.detectChanges();

      const toggle = q<HTMLButtonElement>('#gw-probe-expand')!;
      expect(toggle.type).toBe('button');
      expect(toggle.getAttribute('aria-expanded')).toBe('false');
      expect(toggle.getAttribute('aria-controls')).toBe('gw-probe-output');
      expect(text(toggle)).toBe('Show all');
      expect(q('#gw-probe-output')!.classList).not.toContain('is-expanded');

      toggle.click();
      fixture.detectChanges();
      expect(toggle.getAttribute('aria-expanded')).toBe('true');
      expect(text(toggle)).toBe('Show less');
      expect(q('#gw-probe-output')!.classList).toContain('is-expanded');

      toggle.click();
      fixture.detectChanges();
      const copy = q<HTMLButtonElement>('#gw-probe-copy-btn')!;
      expect(copy.classList).toContain('action-btn');
      expect(copy.getAttribute('aria-label')).toBe('Copy probe result');
      expect(copy.getAttribute('interestfor')).toBe('gw-probe-copy-tip');
      copy.click();
      await new Promise(resolve => setTimeout(resolve, 0));
      fixture.detectChanges();
      expect(writeText).toHaveBeenCalledWith(long);
      expect(text(q('.csp-copy-status'))).toBe('Probe result copied.');
    } finally {
      delete (navigator as { clipboard?: unknown }).clipboard;
      if (original) Object.defineProperty(navigator, 'clipboard', original);
    }
  });

  it('shows the server\'s refusal message when the request fails', () => {
    render();
    type('gnoll');
    run();
    http.expectOne(GW_PROBE).flush({ message: 'A probe query is at most 500 characters.' }, { status: 400, statusText: 'Bad Request' });
    fixture.detectChanges();
    expect(text(q('.csp-request-error'))).toBe('A probe query is at most 500 characters.');
    expect(q('#gw-probe-output')).toBeNull();
  });

  it('cancels the request in flight on a new submit and on destroy', () => {
    render();
    type('first');
    run();
    const first = http.expectOne(GW_PROBE);
    type('second');
    run();
    expect(first.cancelled).toBe(true);
    const second = http.expectOne(GW_PROBE);
    expect(second.request.body).toEqual({ query: 'second' });

    fixture.destroy();
    expect(second.cancelled).toBe(true);
  });

  it('offers topic suggestions in a datalist', () => {
    render({ kind: 'knowledge-base', toolName: 'get_knowledge_article', queryLabel: 'Topic', topicSuggestions: ['altars', 'prayer'] });
    expect(text(q('label[for="gw-probe-query"]'))).toBe('Topic');
    expect(queryInput().getAttribute('list')).toBe('gw-probe-topics');
    const options = Array.from(el.querySelectorAll('#gw-probe-topics option')).map(o => (o as HTMLOptionElement).value);
    expect(options).toEqual(['altars', 'prayer']);

    type('altars');
    run();
    const request = http.expectOne('/api/admin/devtools/knowledge-base/probe');
    expect(request.request.body).toEqual({ query: 'altars' });
    request.flush(buildCorpusProbeResult({ toolName: 'get_knowledge_article' }));
  });
});
