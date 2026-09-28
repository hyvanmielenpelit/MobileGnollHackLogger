import { ChangeDetectorRef } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpParams, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, TestRequest, provideHttpClientTesting } from '@angular/common/http/testing';
import { unzipSync } from 'fflate';

import {
  BenchmarkReportAudience,
  BenchmarkReportDisclosure,
  BenchmarkReportDocumentListItemDto,
  BenchmarkReportPeerNaming
} from '../../../services/admin-benchmark.service';
import {
  BenchmarkDownloadCenterComponent,
  DOWNLOAD_CENTER_STORAGE_KEY,
  DOWNLOAD_PACKAGES,
  DownloadCenterContext,
  DownloadCenterRunContext,
  DownloadFormat,
  DownloadRow,
  INTERNAL_REASONS,
  ROW_NOTES,
  downloadCenterIo,
  internalServerName
} from './benchmark-download-center.component';
import { sha256Hex } from './text-archive';
import { zipEntryTimes } from './zip-entry-times.testing';

const { ExecutiveSummary, TechnicalReport, InternalBrief } = BenchmarkReportAudience;
const { Summary, Detailed, Full } = BenchmarkReportDisclosure;
const { Named, Anonymized } = BenchmarkReportPeerNaming;

/** Every request the Download Center may make; anything else fails the spec. */
const ALLOWED_URLS = [
  /^\/api\/admin\/benchmark\/report-documents$/,
  /^\/api\/admin\/benchmark\/report-documents\/\d+$/,
  /^\/api\/admin\/benchmark\/report-documents\/\d+\/render$/,
  /^\/api\/admin\/benchmark\/report-documents\/\d+\/render\/pdf$/,
  /^\/api\/admin\/benchmark\/runs\/\d+\/report$/,
  /^\/api\/admin\/benchmark\/runs\/\d+\/report\/pdf$/,
  /^\/api\/admin\/benchmark\/runs\/\d+\/tool-call-log$/,
  /^\/api\/admin\/benchmark\/runs\/\d+\/tool-call-log\/pdf$/,
  /^\/api\/admin\/benchmark\/runs\/\d+\/diagnostics\/pdf$/
];

/** The one request that is not a GET: the diagnostics PDF, which carries the captured text. */
const POST_URL = /\/runs\/\d+\/diagnostics\/pdf$/;

const REPORT_SERVER_NAME = 'Board_Suite_GPT_Model_X_20260921_160000.md';
const LOG_SERVER_NAME = 'Board_Suite_GPT_Model_X_run42_tool_calls.md';
const REPORT_PDF_NAME = 'Board_Suite_GPT_Model_X_20260921_160000_INTERNAL.pdf';
const LOG_PDF_NAME = 'Board_Suite_GPT_Model_X_run42_tool_calls_INTERNAL.pdf';
const DIAG_PDF_NAME = 'Board_Suite_GPT_Model_X_run42_diagnostics_INTERNAL.pdf';

function doc(id: number, audience: BenchmarkReportAudience, overrides: Partial<BenchmarkReportDocumentListItemDto> = {}): BenchmarkReportDocumentListItemDto {
  const titles: Record<number, string> = {
    [ExecutiveSummary]: 'Executive Summary: GPT Model X',
    [TechnicalReport]: 'Technical Report: GPT Model X',
    [InternalBrief]: 'Internal Improvement Brief: GPT Model X'
  };
  return {
    id,
    packId: 'pack-1',
    audience,
    title: titles[audience],
    subjectKey: 'run:42',
    subjectLabel: 'GPT Model X',
    subjectRunIds: [42],
    suiteId: 1,
    suiteName: 'Board Suite',
    writerDisplayName: 'Claude Opus',
    writerProvider: 'Anthropic',
    writerModelId: 'claude-opus',
    writerThinkingLevel: 'medium',
    sameProviderAcknowledged: false,
    status: 'Completed',
    reportFormatVersion: 3,
    createdAtUtc: '2026-09-20T09:30:12Z',
    inputTokens: 1000,
    outputTokens: 500,
    durationMs: 9000,
    costUsd: 0.12,
    runChangedSinceGeneration: false,
    missingRunIds: [],
    allowedDisclosures: audience === InternalBrief ? [Full] : [Summary, Detailed, Full],
    ...overrides
  };
}

/** The bytes of a fake PDF, as the ArrayBuffer an `arraybuffer` response carries. */
function arrayBufferOf(text: string): ArrayBuffer {
  const bytes = new TextEncoder().encode(text);
  return bytes.buffer.slice(bytes.byteOffset, bytes.byteOffset + bytes.byteLength) as ArrayBuffer;
}

describe('BenchmarkDownloadCenterComponent', () => {
  let fixture: ComponentFixture<BenchmarkDownloadCenterComponent>;
  let component: BenchmarkDownloadCenterComponent;
  let httpMock: HttpTestingController;
  let saveText: jasmine.Spy;
  let saveBytes: jasmine.Spy;
  let saveBlob: jasmine.Spy;
  let requested: string[];
  let unexpected: string[];
  let pdfRequests: { url: string; method: string; params: HttpParams; body: unknown }[];

  /** Packaging time; whole even seconds, as a zip stores time in two-second steps. */
  const NOW = new Date(2026, 8, 28, 10, 15, 2);
  const NOW_ISO = NOW.toISOString().replace(/\.\d{3}Z$/, 'Z');

  const runContext: DownloadCenterRunContext = {
    kind: 'run',
    run: {
      id: 42,
      suiteName: 'Board Suite',
      modelLabel: 'GPT Model X',
      startedAtUtc: '2026-09-21T16:00:00Z',
      completedAtUtc: '2026-09-21T17:05:44Z'
    },
    diagnosticsText: () => '=== BENCHMARK RUN DIAGNOSTICS ===\n'
  };

  beforeEach(async () => {
    localStorage.removeItem(DOWNLOAD_CENTER_STORAGE_KEY);
    await TestBed.configureTestingModule({
      imports: [BenchmarkDownloadCenterComponent],
      providers: [provideHttpClient(), provideHttpClientTesting()]
    }).compileComponents();

    httpMock = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(BenchmarkDownloadCenterComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();

    saveText = spyOn(downloadCenterIo, 'saveText');
    saveBytes = spyOn(downloadCenterIo, 'saveBytes');
    saveBlob = spyOn(downloadCenterIo, 'saveBlob');
    spyOn(downloadCenterIo, 'now').and.returnValue(NOW);
    requested = [];
    unexpected = [];
    pdfRequests = [];
  });

  afterEach(() => {
    component.close();
    httpMock.verify();
    expect(unexpected).toEqual([]);
    localStorage.removeItem(DOWNLOAD_CENTER_STORAGE_KEY);
  });

  const host = (): HTMLElement => fixture.nativeElement as HTMLElement;

  function render(): void {
    (component as unknown as { cdr: ChangeDetectorRef }).cdr.markForCheck();
    fixture.detectChanges();
  }

  function openRun(
    documents: BenchmarkReportDocumentListItemDto[] = [doc(1, ExecutiveSummary), doc(2, TechnicalReport), doc(3, InternalBrief)],
    context: DownloadCenterContext = runContext
  ): void {
    component.open(context);
    const list = httpMock.expectOne(request => request.url === '/api/admin/benchmark/report-documents');
    expect(list.request.method).toBe('GET');
    expect(list.request.params.get('runId')).toBe('42');
    requested.push(list.request.url);
    list.flush(documents);
    render();
  }

  function row(key: string): DownloadRow {
    return component.rows.find(r => r.key === key)!;
  }

  function rowElement(key: string): HTMLTableRowElement {
    return host().querySelector<HTMLTableRowElement>(`tr[data-row-key="${key}"]`)!;
  }

  function byId(id: string): HTMLElement | null {
    return host().querySelector<HTMLElement>(`[id="${id}"]`);
  }

  /** Chooses exactly the given rows, each with the given options. */
  function choose(pkg: 'internal' | 'provider' | 'custom', picks: Record<string, Partial<{ disclosure: BenchmarkReportDisclosure; naming: BenchmarkReportPeerNaming; formats: DownloadFormat[] }>>): void {
    component.selectPackage(pkg);
    for (const r of component.rows) {
      const state = component.stateOf(r);
      const pick = picks[r.key];
      state.selected = !!pick;
      if (pick?.disclosure) state.disclosure = pick.disclosure;
      if (pick?.naming) state.naming = pick.naming;
      if (pick?.formats) state.formats = pick.formats;
    }
    render();
  }

  interface ServerOptions {
    reportStatus?: number;
    renderBody?: (id: number, disclosure: string | null, peers: string | null) => string;
    /** A JSON error answer to every PDF request whose URL matches. */
    pdfError?: { pattern: RegExp; status: number; body: unknown };
  }

  function respond(request: TestRequest, options: ServerOptions): void {
    const url = request.request.url;
    const method = request.request.method;
    requested.push(url);
    if (!ALLOWED_URLS.some(pattern => pattern.test(url)) || method !== (POST_URL.test(url) ? 'POST' : 'GET')) {
      unexpected.push(`${method} ${url}`);
      request.flush(null, { status: 500, statusText: 'Unexpected' });
      return;
    }
    if (url.endsWith('/pdf')) {
      respondPdf(request, options);
      return;
    }
    const renderMatch = /report-documents\/(\d+)\/render$/.exec(url);
    if (renderMatch) {
      expect(request.request.responseType).toBe('text');
      const disclosure = request.request.params.get('disclosure');
      const peers = request.request.params.get('peers');
      const body = options.renderBody
        ? options.renderBody(Number(renderMatch[1]), disclosure, peers)
        : `# Document ${renderMatch[1]} (${disclosure}, ${peers})\n\nCost $4 per question.\n`;
      request.flush(body);
    } else if (/runs\/\d+\/report$/.test(url)) {
      if (options.reportStatus && options.reportStatus !== 200) {
        request.flush('Not found', { status: options.reportStatus, statusText: 'Not Found' });
      } else {
        request.flush('# Run 42 report\n', { headers: { 'Content-Disposition': `attachment; filename=${REPORT_SERVER_NAME}` } });
      }
    } else if (/tool-call-log$/.test(url)) {
      request.flush('# Tool calls\n', { headers: { 'Content-Disposition': `attachment; filename="${LOG_SERVER_NAME}"` } });
    } else {
      unexpected.push(`${method} ${url}`);
      request.flush(null, { status: 500, statusText: 'Unexpected' });
    }
  }

  function respondPdf(request: TestRequest, options: ServerOptions): void {
    const url = request.request.url;
    const params = request.request.params;
    pdfRequests.push({ url, method: request.request.method, params, body: request.request.body });
    expect(request.request.responseType).withContext(url).toBe('arraybuffer');

    if (options.pdfError && options.pdfError.pattern.test(url)) {
      request.flush(arrayBufferOf(JSON.stringify(options.pdfError.body)), { status: options.pdfError.status, statusText: 'Error' });
      return;
    }
    if (/runs\/\d+\/report\/pdf$/.test(url) && options.reportStatus && options.reportStatus !== 200) {
      request.flush(null, { status: options.reportStatus, statusText: 'Not Found' });
      return;
    }
    const names: [RegExp, string][] = [
      [/runs\/\d+\/report\/pdf$/, REPORT_PDF_NAME],
      [/tool-call-log\/pdf$/, LOG_PDF_NAME],
      [/diagnostics\/pdf$/, DIAG_PDF_NAME],
      [/render\/pdf$/, `server-name_${params.get('disclosure')}_${params.get('peers')}.pdf`]
    ];
    const name = names.find(([pattern]) => pattern.test(url))![1];
    request.flush(arrayBufferOf(`%PDF-1.7\n% ${url} ${params.get('paper')}\n`), {
      headers: { 'Content-Disposition': `attachment; filename=${name}` }
    });
  }

  /** Runs a download to its end, answering each request as the one-at-a-time preparation issues it. */
  async function runDownload(options: ServerOptions = {}): Promise<void> {
    const done = component.download();
    let finished = false;
    done.then(() => finished = true, () => finished = true);
    for (let i = 0; i < 1000 && !finished; i++) {
      await new Promise(resolve => setTimeout(resolve, 0));
      for (const request of httpMock.match(() => true)) {
        respond(request, options);
      }
    }
    await done;
    render();
  }

  async function savedZip(): Promise<{ files: Record<string, string>; times: Map<string, Date>; name: string }> {
    expect(saveBlob).toHaveBeenCalledTimes(1);
    const [blob, name] = saveBlob.calls.mostRecent().args as [Blob, string];
    const bytes = new Uint8Array(await blob.arrayBuffer());
    const unzipped = unzipSync(bytes);
    const files: Record<string, string> = {};
    for (const key of Object.keys(unzipped)) {
      files[key] = new TextDecoder().decode(unzipped[key]);
    }
    return { files, times: zipEntryTimes(bytes), name };
  }

  describe('packages', () => {
    it('opens on the Internal package: every row chosen, pack documents at Full with peers named', () => {
      openRun();

      expect(component.packageId).toBe('internal');
      expect(component.rows.map(r => r.key)).toEqual(['report:42', 'log:42', 'diag:42', 'doc:1', 'doc:2', 'doc:3']);
      for (const r of component.rows) {
        const check = rowElement(r.key).querySelector<HTMLInputElement>('input[type="checkbox"]')!;
        expect(check.checked).withContext(r.key).toBeTrue();
        expect(check.disabled).withContext(r.key).toBeFalse();
      }
      for (const key of ['doc:1', 'doc:2', 'doc:3']) {
        const [disclosure, names] = Array.from(rowElement(key).querySelectorAll<HTMLSelectElement>('select'));
        expect(disclosure.value).toBe(String(Full));
        expect(names.value).toBe(String(Named));
        expect(disclosure.disabled).toBeTrue();
        expect(names.disabled).toBeTrue();
      }
      expect(component.summaryLine).toBe('12 files · 1 ZIP · Internal package');
    });

    it('chooses the PDF and its text source in the Internal package, and the PDF alone in the Provider package', () => {
      openRun();

      expect(component.stateOf(row('doc:1')).formats).toEqual(['pdf', 'md']);
      expect(component.stateOf(row('report:42')).formats).toEqual(['pdf', 'md']);
      expect(component.stateOf(row('log:42')).formats).toEqual(['pdf', 'md']);
      expect(component.stateOf(row('diag:42')).formats).toEqual(['pdf', 'txt']);

      component.selectPackage('provider');

      expect(component.stateOf(row('doc:1')).formats).toEqual(['pdf']);
      expect(component.stateOf(row('doc:2')).formats).toEqual(['pdf']);
    });

    it('lists internal-only rows in the Provider package but makes them unselectable, each with its reason behind an info button', () => {
      openRun();
      host().querySelector<HTMLInputElement>(`#${component.idPrefix}-package-provider`)!.click();
      render();

      expect(component.packageId).toBe('provider');
      const expectations: [string, string][] = [
        ['report:42', INTERNAL_REASONS.runReport],
        ['log:42', INTERNAL_REASONS.toolCallLog],
        ['diag:42', INTERNAL_REASONS.diagnostics],
        ['doc:3', INTERNAL_REASONS.internalBrief]
      ];
      for (const [key, reason] of expectations) {
        const element = rowElement(key);
        const rid = component.rowId(row(key));
        const check = element.querySelector<HTMLInputElement>('input[type="checkbox"]')!;
        expect(check.disabled).withContext(key).toBeTrue();
        expect(check.checked).withContext(key).toBeFalse();
        expect(check.getAttribute('aria-describedby')!.split(' ')).withContext(key).toContain(`${rid}-reason`);
        const reasonElement = element.querySelector(`[id="${rid}-reason"]`)!;
        expect(reasonElement.textContent!.trim()).withContext(key).toBe(reason);
        const popup = reasonElement.closest('.gh-info-popup')!;
        expect(popup.querySelector('.gh-info-popup-title')!.textContent!.trim()).toBe(`Internal only: ${row(key).label}`);
        expect(element.querySelector('.gh-tag-internal')).withContext(key).not.toBeNull();
        expect(element.querySelector('.dc-reason')).withContext(key).toBeNull();
      }
      expect(INTERNAL_REASONS.internalBrief).toBe('Internal only: contains rubric text');
      expect(INTERNAL_REASONS.runReport).toBe('Internal only: contains questions, rubrics and answers');

      for (const key of ['doc:1', 'doc:2']) {
        const element = rowElement(key);
        expect(element.querySelector<HTMLInputElement>('input[type="checkbox"]')!.checked).toBeTrue();
        const [disclosure, names] = Array.from(element.querySelectorAll<HTMLSelectElement>('select'));
        expect(Array.from(disclosure.options).map(o => o.textContent!.trim())).toEqual(['Summary', 'Detailed']);
        expect(disclosure.value).toBe(String(Summary));
        expect(names.value).toBe(String(Anonymized));
        expect(disclosure.disabled).toBeFalse();
        expect(element.querySelector('.gh-tag-shareable')).not.toBeNull();
      }
      expect(component.summaryLine).toBe('2 files · 1 ZIP · Provider package');
    });

    it('warns, naming the compliance note, when a provider copy names its peers', () => {
      openRun();
      component.selectPackage('provider');
      render();
      expect(host().querySelector('.dc-named-warning')).toBeNull();

      const names = rowElement('doc:1').querySelectorAll<HTMLSelectElement>('select')[1];
      names.value = String(Named);
      names.dispatchEvent(new Event('change'));
      render();

      const warning = host().querySelector('.dc-named-warning');
      expect(warning).not.toBeNull();
      expect(warning!.textContent).toContain('docs/overseer/ai-benchmark.md');
      expect(warning!.textContent).toContain('Sharing Reports with AI Providers');
    });

    it('offers any allowed level in Custom, keeping the current choices', () => {
      openRun();
      component.selectPackage('provider');
      component.selectPackage('custom');
      render();

      expect(component.stateOf(row('doc:1')).disclosure).toBe(Summary);
      expect(component.stateOf(row('doc:1')).formats).toEqual(['pdf']);
      const [esDisclosure] = Array.from(rowElement('doc:1').querySelectorAll<HTMLSelectElement>('select'));
      expect(Array.from(esDisclosure.options).map(o => o.textContent!.trim())).toEqual(['Summary', 'Detailed', 'Full']);
      const [ibDisclosure] = Array.from(rowElement('doc:3').querySelectorAll<HTMLSelectElement>('select'));
      expect(Array.from(ibDisclosure.options).map(o => o.textContent!.trim())).toEqual(['Full']);
      expect(rowElement('report:42').querySelector<HTMLInputElement>('input[type="checkbox"]')!.disabled).toBeFalse();
    });
  });

  describe('rows', () => {
    it('offers formats by kind, PDF first, one per line', () => {
      openRun();

      expect(row('doc:1').formats).toEqual(['pdf', 'md', 'html']);
      expect(row('report:42').formats).toEqual(['pdf', 'md', 'html']);
      expect(row('log:42').formats).toEqual(['pdf', 'md']);
      expect(row('diag:42').formats).toEqual(['pdf', 'txt']);

      const formatNames = (key: string): string[] =>
        Array.from(rowElement(key).querySelectorAll<HTMLInputElement>('.dc-format input')).map(input => input.getAttribute('aria-label')!);
      expect(formatNames('doc:1')).toEqual([
        'PDF copy of Executive Summary: GPT Model X',
        'Markdown copy of Executive Summary: GPT Model X',
        'HTML copy of Executive Summary: GPT Model X'
      ]);
      expect(formatNames('report:42')).toEqual(['PDF copy of Run report, run #42', 'Markdown copy of Run report, run #42', 'HTML copy of Run report, run #42']);
      expect(formatNames('log:42')).toEqual(['PDF copy of Tool-call log, run #42', 'Markdown copy of Tool-call log, run #42']);
      expect(formatNames('diag:42')).toEqual(['PDF copy of Run diagnostics, run #42', 'Text copy of Run diagnostics, run #42']);
      expect(rowElement('report:42').querySelectorAll('select').length).toBe(0);
    });

    it('keeps a row’s detail visible and puts its note behind an info button the checkbox is described by', () => {
      openRun();

      const cases: [string, string][] = [['log:42', ROW_NOTES.toolCallLog], ['diag:42', ROW_NOTES.diagnostics]];
      for (const [key, note] of cases) {
        const rid = component.rowId(row(key));
        const element = rowElement(key);
        expect(element.querySelector('.dc-doc-detail')!.textContent!.trim()).withContext(key).toBe('Board Suite · GPT Model X');
        const tip = element.querySelector(`[id="${rid}-note"]`)!;
        expect(tip.textContent!.trim()).withContext(key).toBe(note);
        expect(tip.closest('.gh-info-popup')!.querySelector('.gh-info-popup-title')!.textContent!.trim()).toBe(row(key).label);
        const check = element.querySelector<HTMLInputElement>('input[type="checkbox"]')!;
        expect(check.getAttribute('aria-describedby')).withContext(key).toBe(`${rid}-note`);
      }
      expect(ROW_NOTES.toolCallLog).toBe('Can run to several megabytes; its PDF can be hundreds of pages.');
      expect(ROW_NOTES.diagnostics).toBe('Captured when the download is prepared, not stored.');
      expect(rowElement('report:42').querySelector(`[id="${component.rowId(row('report:42'))}-note"]`)).toBeNull();
      expect(rowElement('report:42').querySelector('input[type="checkbox"]')!.hasAttribute('aria-describedby')).toBeFalse();
    });

    it('names each row checkbox after its document', () => {
      openRun();

      expect(rowElement('doc:2').querySelector('input[type="checkbox"]')!.getAttribute('aria-label'))
        .toBe('Include Technical Report: GPT Model X');
    });

    it('tags a document whose run changed since it was written', () => {
      openRun([doc(1, ExecutiveSummary, { runChangedSinceGeneration: true }), doc(2, TechnicalReport)]);

      expect(rowElement('doc:1').querySelector('.gh-tag-changed')!.textContent!.trim())
        .toBe('Run changed since this document was written');
      expect(rowElement('doc:2').querySelector('.gh-tag-changed')).toBeNull();
    });

    it('shows No format chosen while a row has none', () => {
      openRun();
      component.stateOf(row('doc:1')).formats = [];
      render();

      expect(rowElement('doc:1').querySelector('.dc-format-none')!.textContent!.trim()).toBe('No format chosen');
    });
  });

  describe('explanations', () => {
    it('shows the GnollBench emblem, decorative, beside the title', () => {
      openRun();

      const emblem = host().querySelector<HTMLImageElement>('.dc-title-row > img.gnollbench-emblem')!;
      expect(emblem).not.toBeNull();
      expect(emblem.getAttribute('alt')).toBe('');
      expect(emblem.getAttribute('src')).toBe('/img/gnollbench/gnollbench-logo-v3-256.webp');
      expect(emblem.nextElementSibling!.querySelector('h3')!.textContent!.trim()).toBe('Downloads');
    });

    it('puts each package description behind an info button beside, not inside, its label', () => {
      openRun();

      for (const pkg of DOWNLOAD_PACKAGES) {
        const radio = host().querySelector<HTMLInputElement>(`#${component.idPrefix}-package-${pkg.id}`)!;
        const label = radio.closest('label')!;
        expect(label.querySelector('button')).withContext(pkg.id).toBeNull();
        expect(label.textContent!.trim()).withContext(pkg.id).toBe(pkg.name);
        const descriptionId = radio.getAttribute('aria-describedby')!;
        expect(descriptionId).toBe(`${component.idPrefix}-package-${pkg.id}-desc`);
        expect(byId(descriptionId)!.textContent!.trim()).withContext(pkg.id).toBe(pkg.description);
        const card = label.closest('.dc-package-card')!;
        expect(card.querySelector('button.gh-info-btn')!.getAttribute('aria-label')).toBe(`About ${pkg.name}`);
      }
    });

    it('explains the columns with info buttons and definition lists', () => {
      openRun();
      const p = component.idPrefix;

      const header = host().querySelector('thead')!;
      for (const name of ['sharing', 'disclosure', 'names', 'formats']) {
        expect(header.querySelector(`[id="${p}-${name}-tip"]`)).withContext(name).not.toBeNull();
      }
      expect(byId(`${p}-sharing-tip`)!.textContent).toContain('internal-only ones never leave the Overseer team');
      const terms = (name: string): string[] =>
        Array.from(byId(`${p}-${name}-tip`)!.querySelectorAll('dl > div > dt .gh-info-term')).map(term => term.textContent!.trim());
      expect(terms('disclosure')).toEqual(['Summary', 'Detailed', 'Full']);
      expect(terms('names')).toEqual(['Named', 'Anonymized']);
      expect(terms('formats')).toEqual(['PDF', 'Markdown', 'HTML', 'Text']);
      expect(host().querySelector('.dc-reason')).toBeNull();
    });

    it('gives every id once, from the instance prefix, and resolves every description', () => {
      openRun();
      component.selectPackage('provider');
      render();

      const ids = Array.from(host().querySelectorAll('[id]')).map(element => element.id);
      expect(new Set(ids).size).toBe(ids.length);
      for (const id of ids) {
        expect(id.startsWith(`${component.idPrefix}-`)).withContext(id).toBeTrue();
      }
      const described = Array.from(host().querySelectorAll('[aria-describedby]'));
      expect(described.length).toBeGreaterThan(0);
      for (const element of described) {
        for (const id of element.getAttribute('aria-describedby')!.split(' ')) {
          expect(byId(id)).withContext(id).not.toBeNull();
        }
      }
    });
  });

  describe('paper size', () => {
    it('defaults to A4, and describes both radios with its info tip', () => {
      openRun();

      expect(component.paper).toBe('a4');
      const a4 = host().querySelector<HTMLInputElement>(`#${component.idPrefix}-paper-a4`)!;
      const letter = host().querySelector<HTMLInputElement>(`#${component.idPrefix}-paper-letter`)!;
      expect(a4.checked).toBeTrue();
      expect(letter.checked).toBeFalse();
      expect(letter.closest('label')!.textContent!.trim()).toBe('US Letter');
      for (const radio of [a4, letter]) {
        expect(byId(radio.getAttribute('aria-describedby')!)!.textContent!.trim())
          .toBe('Letter suits readers in North America; A4 everywhere else.');
      }
    });

    it('sends the chosen paper to every PDF, names it in the manifest and remembers it', async () => {
      openRun();
      host().querySelector<HTMLInputElement>(`#${component.idPrefix}-paper-letter`)!.click();
      render();
      expect(component.paper).toBe('letter');
      choose('internal', {
        'doc:1': { formats: ['pdf'] },
        'report:42': { formats: ['pdf'] },
        'log:42': { formats: ['pdf'] },
        'diag:42': { formats: ['pdf'] }
      });

      await runDownload();

      expect(pdfRequests.map(r => r.url).sort()).toEqual([
        '/api/admin/benchmark/report-documents/1/render/pdf',
        '/api/admin/benchmark/runs/42/diagnostics/pdf',
        '/api/admin/benchmark/runs/42/report/pdf',
        '/api/admin/benchmark/runs/42/tool-call-log/pdf'
      ]);
      for (const request of pdfRequests) {
        expect(request.params.get('paper')).withContext(request.url).toBe('letter');
      }
      const zip = await savedZip();
      expect(zip.files['MANIFEST.md'].match(/- \*\*PDF:\*\* PDF\/UA-1, PDF\/A-3A, US Letter/g)?.length).toBe(4);
      expect(JSON.parse(localStorage.getItem(DOWNLOAD_CENTER_STORAGE_KEY)!).paper).toBe('letter');

      component.close();
      openRun();
      expect(component.paper).toBe('letter');
      expect(host().querySelector<HTMLInputElement>(`#${component.idPrefix}-paper-letter`)!.checked).toBeTrue();
    });
  });

  describe('remembered settings', () => {
    it('restores the last package, paper and choices', () => {
      localStorage.setItem(DOWNLOAD_CENTER_STORAGE_KEY, JSON.stringify({
        version: 2,
        package: 'provider',
        paper: 'letter',
        packages: { provider: { executiveSummary: { selected: true, disclosure: Detailed, naming: Named, formats: ['html'] } } }
      }));

      openRun();

      expect(component.packageId).toBe('provider');
      expect(component.paper).toBe('letter');
      expect(component.stateOf(row('doc:1'))).toEqual({ selected: true, disclosure: Detailed, naming: Named, formats: ['html'] });
      expect(component.stateOf(row('doc:2')).disclosure).toBe(Summary);
    });

    it('never restores a remembered choice the package does not allow', () => {
      localStorage.setItem(DOWNLOAD_CENTER_STORAGE_KEY, JSON.stringify({
        version: 2,
        package: 'provider',
        paper: 'tabloid',
        packages: { provider: { executiveSummary: { disclosure: Full, formats: ['txt'] }, runReport: { selected: true } } }
      }));

      openRun();

      expect(component.paper).toBe('a4');
      expect(component.stateOf(row('doc:1')).disclosure).toBe(Summary);
      expect(component.stateOf(row('doc:1')).formats).toEqual(['pdf']);
      expect(component.isIncluded(row('report:42'))).toBeFalse();
    });

    it('ignores settings of version 1, so every admin starts from the PDF presets once', () => {
      localStorage.setItem(DOWNLOAD_CENTER_STORAGE_KEY, JSON.stringify({
        version: 1,
        package: 'provider',
        packages: { provider: { executiveSummary: { selected: true, formats: ['md', 'html'] } } }
      }));

      openRun();

      expect(component.packageId).toBe('internal');
      expect(component.paper).toBe('a4');
      expect(component.stateOf(row('doc:1')).formats).toEqual(['pdf', 'md']);
    });

    it('tolerates corrupt, foreign-version and unreadable storage', () => {
      localStorage.setItem(DOWNLOAD_CENTER_STORAGE_KEY, '{not json');
      openRun();
      expect(component.packageId).toBe('internal');
      component.close();

      localStorage.setItem(DOWNLOAD_CENTER_STORAGE_KEY, JSON.stringify({ version: 3, package: 'provider' }));
      openRun();
      expect(component.packageId).toBe('internal');
      component.close();

      spyOn(Storage.prototype, 'getItem').and.throwError('denied');
      openRun();
      expect(component.packageId).toBe('internal');
    });

    it('remembers the package, paper and choices of a download', async () => {
      openRun();
      choose('provider', { 'doc:1': { disclosure: Detailed, naming: Named, formats: ['md'] } });

      await runDownload();

      const stored = JSON.parse(localStorage.getItem(DOWNLOAD_CENTER_STORAGE_KEY)!);
      expect(stored.version).toBe(2);
      expect(stored.package).toBe('provider');
      expect(stored.paper).toBe('a4');
      expect(stored.packages.provider.executiveSummary).toEqual({ selected: true, disclosure: Detailed, naming: Named, formats: ['md'] });
      expect(stored.packages.provider.technicalReport.selected).toBeFalse();
    });
  });

  describe('downloading', () => {
    it('downloads one file as itself', async () => {
      openRun();
      choose('provider', { 'doc:1': { formats: ['md'] } });
      expect(component.summaryLine).toBe('1 file · Provider package');

      await runDownload();

      expect(saveBlob).not.toHaveBeenCalled();
      expect(saveBytes).not.toHaveBeenCalled();
      expect(saveText).toHaveBeenCalledOnceWith(
        'executive-summary-gpt-model-x_summary_anonymized.md',
        '# Document 1 (summary, anonymized)\n\nCost $4 per question.\n',
        'text/markdown;charset=utf-8');
      expect(requested.filter(url => url.endsWith('/render')).length).toBe(1);
      expect(host().querySelector('[role="status"]')!.textContent!.trim()).toBe('Downloaded 1 file.');
    });

    it('downloads one PDF as its bytes, rendered at the chosen options', async () => {
      openRun();
      choose('provider', { 'doc:1': {} });
      expect(component.summaryLine).toBe('1 file · Provider package');

      await runDownload();

      expect(saveText).not.toHaveBeenCalled();
      expect(saveBlob).not.toHaveBeenCalled();
      expect(saveBytes).toHaveBeenCalledTimes(1);
      const [name, bytes, mime] = saveBytes.calls.mostRecent().args as [string, Uint8Array, string];
      expect(name).toBe('executive-summary-gpt-model-x_summary_anonymized.pdf');
      expect(mime).toBe('application/pdf');
      expect(new TextDecoder().decode(bytes)).toBe('%PDF-1.7\n% /api/admin/benchmark/report-documents/1/render/pdf a4\n');
      expect(pdfRequests.length).toBe(1);
      expect(pdfRequests[0].params.get('disclosure')).toBe('summary');
      expect(pdfRequests[0].params.get('peers')).toBe('anonymized');
      expect(pdfRequests[0].params.get('paper')).toBe('a4');
      expect(requested.some(url => url.endsWith('/render'))).toBeFalse();
    });

    it('downloads several files as one ZIP with a manifest, each entry carrying its stated time', async () => {
      openRun();

      await runDownload();

      expect(saveText).not.toHaveBeenCalled();
      expect(saveBytes).not.toHaveBeenCalled();
      const zip = await savedZip();
      expect(zip.name).toBe('gpt-model-x_internal-package_20260928_101502.zip');

      const documentTime = new Date('2026-09-20T09:30:12Z').getTime();
      const runTime = new Date('2026-09-21T17:05:44Z').getTime();
      const expected: Record<string, number> = {
        [REPORT_PDF_NAME]: runTime,
        'Board_Suite_GPT_Model_X_20260921_160000_INTERNAL.md': runTime,
        [LOG_PDF_NAME]: runTime,
        'Board_Suite_GPT_Model_X_run42_tool_calls_INTERNAL.md': runTime,
        [DIAG_PDF_NAME]: NOW.getTime(),
        'board-suite_gpt-model-x_run42_diagnostics_INTERNAL.txt': NOW.getTime(),
        'executive-summary-gpt-model-x_full_named_INTERNAL.pdf': documentTime,
        'executive-summary-gpt-model-x_full_named_INTERNAL.md': documentTime,
        'technical-report-gpt-model-x_full_named_INTERNAL.pdf': documentTime,
        'technical-report-gpt-model-x_full_named_INTERNAL.md': documentTime,
        'internal-improvement-brief-gpt-model-x_full_named_INTERNAL.pdf': documentTime,
        'internal-improvement-brief-gpt-model-x_full_named_INTERNAL.md': documentTime,
        'MANIFEST.md': NOW.getTime()
      };
      expect(Object.keys(zip.files).sort()).toEqual(Object.keys(expected).sort());
      for (const [name, time] of Object.entries(expected)) {
        expect(zip.times.get(name)?.getTime()).withContext(name).toBe(time);
      }

      // One fetch per source, however many formats use it.
      expect(requested.filter(url => url.endsWith('/runs/42/report')).length).toBe(1);
      expect(requested.filter(url => url.endsWith('/render')).length).toBe(3);
      expect(requested.filter(url => url.endsWith('/render/pdf')).length).toBe(3);

      const manifest = zip.files['MANIFEST.md'];
      expect(manifest).toContain('- **Package:** Internal package');
      expect(manifest).toContain('- **Files:** 12');
      for (const name of Object.keys(expected).filter(n => n !== 'MANIFEST.md')) {
        expect(manifest).toContain(`\`${name}\``);
      }
      expect(manifest).toContain('- **Document id:** 1');
      expect(manifest).toContain('- **Renderer version:** 3');
      expect(manifest).toContain('- **Writer:** Claude Opus');
      expect(manifest).toContain('- **Created:** 2026-09-20T09:30:12Z');
      expect(manifest).toContain('Run diagnostics (captured when the download was prepared)');
      expect(manifest.match(/- \*\*Format:\*\* PDF\n- \*\*PDF:\*\* PDF\/UA-1, PDF\/A-3A, A4\n/g)?.length).toBe(6);
      expect(manifest.match(/- \*\*Format:\*\* Markdown\n/g)?.length).toBe(5);
      expect(manifest).toContain('- **Format:** Text\n');
      const hash = await sha256Hex(zip.files['executive-summary-gpt-model-x_full_named_INTERNAL.md']);
      expect(manifest).toContain(`\`${hash}\``);
      const pdfHash = await sha256Hex(zip.files[REPORT_PDF_NAME]);
      expect(manifest).toContain(`\`${pdfHash}\``);
      expect(zip.files[REPORT_PDF_NAME].startsWith('%PDF-1.7\n')).toBeTrue();
      expect(zip.files['board-suite_gpt-model-x_run42_diagnostics_INTERNAL.txt']).toBe('=== BENCHMARK RUN DIAGNOSTICS ===\n');
      expect(host().querySelector('[role="status"]')!.textContent!.trim()).toBe('Downloaded 12 files as one ZIP.');
    });

    it('captures the diagnostics once, feeding the Text and the PDF the same text and time', async () => {
      const diagnosticsText = jasmine.createSpy('diagnosticsText').and.returnValue('=== BENCHMARK RUN DIAGNOSTICS ===\nCaptured once.\n');
      openRun(undefined, { ...runContext, diagnosticsText });
      choose('internal', { 'diag:42': {} });
      expect(component.stateOf(row('diag:42')).formats).toEqual(['pdf', 'txt']);

      await runDownload();

      expect(diagnosticsText).toHaveBeenCalledTimes(1);
      const zip = await savedZip();
      const text = zip.files['board-suite_gpt-model-x_run42_diagnostics_INTERNAL.txt'];
      expect(text).toBe('=== BENCHMARK RUN DIAGNOSTICS ===\nCaptured once.\n');
      expect(pdfRequests.length).toBe(1);
      expect(pdfRequests[0].method).toBe('POST');
      expect(pdfRequests[0].body).toEqual({ text, capturedAtUtc: NOW_ISO });
      expect(zip.times.get(DIAG_PDF_NAME)?.getTime()).toBe(NOW.getTime());
      expect(zip.times.get('board-suite_gpt-model-x_run42_diagnostics_INTERNAL.txt')?.getTime()).toBe(NOW.getTime());
      expect(zip.files['MANIFEST.md'].match(new RegExp(`- \\*\\*Created:\\*\\* ${NOW_ISO}\\n`, 'g'))?.length).toBe(2);
    });

    it('marks _INTERNAL in every package, after safeFileName, so the suffix keeps its case', async () => {
      openRun();
      choose('custom', { 'doc:1': { disclosure: Full, naming: Anonymized, formats: ['md'] } });
      await runDownload();
      expect(saveText.calls.mostRecent().args[0]).toBe('executive-summary-gpt-model-x_full_anonymized_INTERNAL.md');

      choose('custom', { 'doc:2': { disclosure: Detailed, naming: Named, formats: ['html'] } });
      await runDownload();
      expect(saveText.calls.mostRecent().args[0]).toBe('technical-report-gpt-model-x_detailed_named.html');
      expect(saveText.calls.mostRecent().args[2]).toBe('text/html;charset=utf-8');

      choose('provider', { 'doc:2': { formats: ['md'] } });
      await runDownload();
      expect(saveText.calls.mostRecent().args[0]).toBe('technical-report-gpt-model-x_summary_anonymized.md');
      expect(saveText.calls.mostRecent().args[0]).not.toContain('INTERNAL');

      choose('internal', { 'diag:42': { formats: ['txt'] } });
      await runDownload();
      expect(saveText.calls.mostRecent().args[0]).toBe('board-suite_gpt-model-x_run42_diagnostics_INTERNAL.txt');
      expect(saveText.calls.mostRecent().args[2]).toBe('text/plain;charset=utf-8');

      choose('custom', { 'doc:1': { disclosure: Full, naming: Named, formats: ['pdf'] } });
      await runDownload();
      expect(saveBytes.calls.mostRecent().args[0]).toBe('executive-summary-gpt-model-x_full_named_INTERNAL.pdf');
    });

    it('keeps the server file name of the run files, with _INTERNAL once', async () => {
      openRun();
      choose('custom', { 'report:42': { formats: ['html'] } });
      await runDownload();
      expect(saveText.calls.mostRecent().args[0]).toBe('Board_Suite_GPT_Model_X_20260921_160000_INTERNAL.html');
      expect(saveText.calls.mostRecent().args[1]).toContain('<!DOCTYPE html>');

      choose('custom', { 'log:42': { formats: ['md'] } });
      await runDownload();
      expect(saveText.calls.mostRecent().args[0]).toBe('Board_Suite_GPT_Model_X_run42_tool_calls_INTERNAL.md');
      expect(saveText.calls.mostRecent().args[1]).toBe('# Tool calls\n');

      const pdfs: [string, string][] = [['report:42', REPORT_PDF_NAME], ['log:42', LOG_PDF_NAME], ['diag:42', DIAG_PDF_NAME]];
      for (const [key, name] of pdfs) {
        choose('custom', { [key]: { formats: ['pdf'] } });
        await runDownload();
        expect(saveBytes.calls.mostRecent().args[0]).withContext(key).toBe(name);
        expect(saveBytes.calls.mostRecent().args[2]).withContext(key).toBe('application/pdf');
      }
    });

    it('never doubles _INTERNAL on a server name that already carries it', () => {
      expect(internalServerName('Suite_Model_run42_tool_calls_INTERNAL.pdf', 'pdf')).toBe('Suite_Model_run42_tool_calls_INTERNAL.pdf');
      expect(internalServerName('Suite_Model_20260921_160000.md', 'html')).toBe('Suite_Model_20260921_160000_INTERNAL.html');
      expect(internalServerName('Suite_Model_20260921_160000.md', 'pdf')).toBe('Suite_Model_20260921_160000_INTERNAL.pdf');
    });

    it('lists a file that fails and still downloads the rest', async () => {
      openRun();

      await runDownload({ reportStatus: 404 });

      const zip = await savedZip();
      expect(Object.keys(zip.files).length).toBe(11);
      expect(Object.keys(zip.files).some(name => name.startsWith('Board_Suite_GPT_Model_X_2026'))).toBeFalse();
      expect(zip.files['MANIFEST.md']).toContain('## Not included');
      expect(zip.files['MANIFEST.md']).toContain('Run report, run #42 (Markdown): the run no longer exists');
      expect(component.failures.map(f => `${f.label}: ${f.reason}`)).toEqual([
        'Run report, run #42 (PDF): the run no longer exists',
        'Run report, run #42 (Markdown): the run no longer exists'
      ]);
      expect(host().querySelector('.dc-failures')!.textContent).toContain('the run no longer exists');
      expect(host().querySelector('[role="status"]')!.textContent!.trim()).toBe('Downloaded 10 of 12 files as one ZIP; 2 failed.');
    });

    it('shows the server’s message when a PDF is refused as too large', async () => {
      const message = 'This document is too large for a PDF (4000001 characters); download the Markdown instead.';
      openRun();
      choose('custom', { 'log:42': { formats: ['pdf', 'md'] } });

      await runDownload({ pdfError: { pattern: /tool-call-log\/pdf$/, status: 413, body: { error: message } } });

      expect(component.failures).toEqual([{ label: 'Tool-call log, run #42 (PDF)', reason: message }]);
      expect(host().querySelector('.dc-failures')!.textContent).toContain(message);
      const zip = await savedZip();
      expect(Object.keys(zip.files).sort()).toEqual(['Board_Suite_GPT_Model_X_run42_tool_calls_INTERNAL.md', 'MANIFEST.md']);
      expect(zip.files['MANIFEST.md']).toContain(`- Tool-call log, run #42 (PDF): ${message}`);
    });

    it('sanitizes HTML through the private converter', async () => {
      openRun();
      choose('custom', { 'doc:1': { disclosure: Summary, naming: Anonymized, formats: ['html'] } });

      await runDownload({
        renderBody: () => '# Title\n\n<script>alert(1)</script>\n\n<img src="https://example.invalid/x.png" onerror="alert(2)">\n\n<p style="color: red" onclick="x()">Styled</p>\n\nCost $4 and $20 in total.\n'
      });

      const [name, html, mime] = saveText.calls.mostRecent().args as [string, string, string];
      expect(name).toBe('executive-summary-gpt-model-x_summary_anonymized.html');
      expect(mime).toBe('text/html;charset=utf-8');
      expect(html.startsWith('<!DOCTYPE html>')).toBeTrue();
      expect(html).toContain('<title>Executive Summary: GPT Model X</title>');
      expect(html).not.toContain('<script>alert');
      expect(html).not.toContain('onerror');
      expect(html).not.toContain('onclick');
      expect(html).not.toContain('<img');
      expect(html).not.toContain('color: red');
      expect(html).not.toContain('katex');
      expect(html).toContain('Cost $4 and $20 in total.');
    });

    it('refuses to download while nothing is chosen', async () => {
      openRun();
      choose('custom', {});

      expect(component.summaryLine).toBe('No files chosen · Custom');
      expect(host().querySelector('.dc-download')!.getAttribute('aria-disabled')).toBe('true');
      await component.download();

      expect(saveText).not.toHaveBeenCalled();
      expect(saveBytes).not.toHaveBeenCalled();
      expect(saveBlob).not.toHaveBeenCalled();
    });
  });

  describe('document context', () => {
    function openDocuments(details: Record<number, BenchmarkReportDocumentListItemDto | null>): void {
      component.open({ kind: 'documents', documentIds: Object.keys(details).map(Number) });
      for (const [id, detail] of Object.entries(details)) {
        const request = httpMock.expectOne(`/api/admin/benchmark/report-documents/${id}`);
        requested.push(request.request.url);
        if (detail) {
          request.flush(detail);
        } else {
          request.flush(null, { status: 404, statusText: 'Not Found' });
        }
      }
      render();
    }

    it('lists the chosen documents and their runs’ reports, omitting a deleted run with a notice', () => {
      openDocuments({
        1: doc(1, ExecutiveSummary, { subjectRunIds: [42, 43], missingRunIds: [43], runChangedSinceGeneration: true }),
        2: doc(2, TechnicalReport, { subjectRunIds: [42, 43], missingRunIds: [43] }),
        9: null
      });

      expect(component.rows.map(r => r.key)).toEqual(['doc:1', 'doc:2', 'report:42']);
      expect(component.rows.some(r => r.kind === 'diagnostics' || r.kind === 'toolCallLog')).toBeFalse();
      expect(row('report:42').formats).toEqual(['pdf', 'md', 'html']);
      const notices = Array.from(host().querySelectorAll('.dc-notice')).map(n => n.textContent!.trim());
      expect(notices).toContain('Run #43 no longer exists, so its run report is not listed.');
      expect(notices).toContain('Report document #9 is no longer available.');
      expect(rowElement('doc:1').querySelector('.gh-tag-changed')).not.toBeNull();
    });

    it('makes no request but the allowed endpoints, and dates a run report from its server name', async () => {
      openDocuments({ 1: doc(1, ExecutiveSummary), 2: doc(2, TechnicalReport) });

      await runDownload();

      expect(requested.every(url => ALLOWED_URLS.some(pattern => pattern.test(url)))).toBeTrue();
      const zip = await savedZip();
      expect(zip.name).toBe('gpt-model-x_internal-package_20260928_101502.zip');
      const started = new Date('2026-09-21T16:00:00Z').getTime();
      expect(zip.times.get('Board_Suite_GPT_Model_X_20260921_160000_INTERNAL.md')?.getTime()).toBe(started);
      expect(zip.times.get(REPORT_PDF_NAME)?.getTime()).toBe(started);
    });

    it('fails a deleted run’s report gracefully', async () => {
      openDocuments({ 1: doc(1, ExecutiveSummary) });
      choose('custom', { 'doc:1': { formats: ['md'] }, 'report:42': { formats: ['md'] } });

      await runDownload({ reportStatus: 404 });

      const zip = await savedZip();
      expect(Object.keys(zip.files).sort()).toEqual(['MANIFEST.md', 'executive-summary-gpt-model-x_full_named_INTERNAL.md']);
      expect(component.failures[0].reason).toBe('the run no longer exists');
    });
  });

  it('emits closed when the dialog closes', () => {
    openRun();
    const closed = jasmine.createSpy('closed');
    component.closed.subscribe(closed);

    component.close();
    fixture.nativeElement.querySelector('dialog')!.dispatchEvent(new Event('close'));

    expect(closed).toHaveBeenCalled();
  });
});
