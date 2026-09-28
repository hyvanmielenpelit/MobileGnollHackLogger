import { ChangeDetectorRef } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
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
  DownloadCenterContext,
  DownloadRow,
  INTERNAL_REASONS,
  downloadCenterIo
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
  /^\/api\/admin\/benchmark\/runs\/\d+\/report$/,
  /^\/api\/admin\/benchmark\/runs\/\d+\/tool-call-log$/
];

const REPORT_SERVER_NAME = 'Board_Suite_GPT_Model_X_20260921_160000.md';
const LOG_SERVER_NAME = 'Board_Suite_GPT_Model_X_run42_tool_calls.md';

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

describe('BenchmarkDownloadCenterComponent', () => {
  let fixture: ComponentFixture<BenchmarkDownloadCenterComponent>;
  let component: BenchmarkDownloadCenterComponent;
  let httpMock: HttpTestingController;
  let saveText: jasmine.Spy;
  let saveBlob: jasmine.Spy;
  let requested: string[];
  let unexpected: string[];

  /** Packaging time; whole even seconds, as a zip stores time in two-second steps. */
  const NOW = new Date(2026, 8, 28, 10, 15, 2);

  const runContext: DownloadCenterContext = {
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
    saveBlob = spyOn(downloadCenterIo, 'saveBlob');
    spyOn(downloadCenterIo, 'now').and.returnValue(NOW);
    requested = [];
    unexpected = [];
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

  function openRun(documents: BenchmarkReportDocumentListItemDto[] = [doc(1, ExecutiveSummary), doc(2, TechnicalReport), doc(3, InternalBrief)]): void {
    component.open(runContext);
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

  /** Chooses exactly the given rows, each with the given options. */
  function choose(pkg: 'internal' | 'provider' | 'custom', picks: Record<string, Partial<{ disclosure: BenchmarkReportDisclosure; naming: BenchmarkReportPeerNaming; formats: ('md' | 'html' | 'txt')[] }>>): void {
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
  }

  function respond(request: TestRequest, options: ServerOptions): void {
    const url = request.request.url;
    requested.push(url);
    if (!ALLOWED_URLS.some(pattern => pattern.test(url)) || request.request.method !== 'GET') {
      unexpected.push(`${request.request.method} ${url}`);
      request.flush(null, { status: 500, statusText: 'Unexpected' });
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
      unexpected.push(`${request.request.method} ${url}`);
      request.flush(null, { status: 500, statusText: 'Unexpected' });
    }
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
      expect(component.summaryLine).toBe('10 files · 1 ZIP · Internal package');
    });

    it('lists internal-only rows in the Provider package but makes them unselectable, each with its reason', () => {
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
        const check = element.querySelector<HTMLInputElement>('input[type="checkbox"]')!;
        expect(check.disabled).withContext(key).toBeTrue();
        expect(check.checked).withContext(key).toBeFalse();
        const reasonElement = element.querySelector('.dc-reason')!;
        expect(reasonElement.textContent!.trim()).toBe(reason);
        expect(check.getAttribute('aria-describedby')).toBe(reasonElement.id);
        expect(element.querySelector('.gh-tag-internal')).not.toBeNull();
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
      expect(component.summaryLine).toBe('4 files · 1 ZIP · Provider package');
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
      const [esDisclosure] = Array.from(rowElement('doc:1').querySelectorAll<HTMLSelectElement>('select'));
      expect(Array.from(esDisclosure.options).map(o => o.textContent!.trim())).toEqual(['Summary', 'Detailed', 'Full']);
      const [ibDisclosure] = Array.from(rowElement('doc:3').querySelectorAll<HTMLSelectElement>('select'));
      expect(Array.from(ibDisclosure.options).map(o => o.textContent!.trim())).toEqual(['Full']);
      expect(rowElement('report:42').querySelector<HTMLInputElement>('input[type="checkbox"]')!.disabled).toBeFalse();
    });
  });

  describe('rows', () => {
    it('offers formats by kind', () => {
      openRun();

      const formatNames = (key: string): string[] =>
        Array.from(rowElement(key).querySelectorAll<HTMLInputElement>('.dc-format input')).map(input => input.getAttribute('aria-label')!);
      expect(formatNames('doc:1')).toEqual(['Markdown copy of Executive Summary: GPT Model X', 'HTML copy of Executive Summary: GPT Model X']);
      expect(formatNames('report:42')).toEqual(['Markdown copy of Run report, run #42', 'HTML copy of Run report, run #42']);
      expect(formatNames('log:42')).toEqual([]);
      expect(rowElement('log:42').querySelector('.dc-format-fixed')!.textContent!.trim()).toBe('Markdown only');
      expect(rowElement('diag:42').querySelector('.dc-format-fixed')!.textContent!.trim()).toBe('Text only');
      expect(rowElement('diag:42').textContent).toContain('Captured now');
      expect(rowElement('report:42').querySelectorAll('select').length).toBe(0);
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
  });

  describe('remembered settings', () => {
    it('restores the last package and its choices', () => {
      localStorage.setItem(DOWNLOAD_CENTER_STORAGE_KEY, JSON.stringify({
        version: 1,
        package: 'provider',
        packages: { provider: { executiveSummary: { selected: true, disclosure: Detailed, naming: Named, formats: ['html'] } } }
      }));

      openRun();

      expect(component.packageId).toBe('provider');
      expect(component.stateOf(row('doc:1'))).toEqual({ selected: true, disclosure: Detailed, naming: Named, formats: ['html'] });
      expect(component.stateOf(row('doc:2')).disclosure).toBe(Summary);
    });

    it('never restores a remembered choice the package does not allow', () => {
      localStorage.setItem(DOWNLOAD_CENTER_STORAGE_KEY, JSON.stringify({
        version: 1,
        package: 'provider',
        packages: { provider: { executiveSummary: { disclosure: Full, formats: ['txt'] }, runReport: { selected: true } } }
      }));

      openRun();

      expect(component.stateOf(row('doc:1')).disclosure).toBe(Summary);
      expect(component.stateOf(row('doc:1')).formats).toEqual(['md', 'html']);
      expect(component.isIncluded(row('report:42'))).toBeFalse();
    });

    it('tolerates corrupt, foreign-version and unreadable storage', () => {
      localStorage.setItem(DOWNLOAD_CENTER_STORAGE_KEY, '{not json');
      openRun();
      expect(component.packageId).toBe('internal');
      component.close();

      localStorage.setItem(DOWNLOAD_CENTER_STORAGE_KEY, JSON.stringify({ version: 2, package: 'provider' }));
      openRun();
      expect(component.packageId).toBe('internal');
      component.close();

      spyOn(Storage.prototype, 'getItem').and.throwError('denied');
      openRun();
      expect(component.packageId).toBe('internal');
    });

    it('remembers the package and choices of a download', async () => {
      openRun();
      choose('provider', { 'doc:1': { disclosure: Detailed, naming: Named, formats: ['md'] } });

      await runDownload();

      const stored = JSON.parse(localStorage.getItem(DOWNLOAD_CENTER_STORAGE_KEY)!);
      expect(stored.version).toBe(1);
      expect(stored.package).toBe('provider');
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
      expect(saveText).toHaveBeenCalledOnceWith(
        'executive-summary-gpt-model-x_summary_anonymized.md',
        '# Document 1 (summary, anonymized)\n\nCost $4 per question.\n',
        'text/markdown;charset=utf-8');
      expect(requested.filter(url => url.endsWith('/render')).length).toBe(1);
      expect(host().querySelector('[role="status"]')!.textContent!.trim()).toBe('Downloaded 1 file.');
    });

    it('downloads several files as one ZIP with a manifest, each entry carrying its stated time', async () => {
      openRun();

      await runDownload();

      expect(saveText).not.toHaveBeenCalled();
      const zip = await savedZip();
      expect(zip.name).toBe('gpt-model-x_internal-package_20260928_101502.zip');

      const documentTime = new Date('2026-09-20T09:30:12Z').getTime();
      const runTime = new Date('2026-09-21T17:05:44Z').getTime();
      const expected: Record<string, number> = {
        'Board_Suite_GPT_Model_X_20260921_160000_INTERNAL.md': runTime,
        'Board_Suite_GPT_Model_X_20260921_160000_INTERNAL.html': runTime,
        'Board_Suite_GPT_Model_X_run42_tool_calls_INTERNAL.md': runTime,
        'board-suite_gpt-model-x_run42_diagnostics_INTERNAL.txt': NOW.getTime(),
        'executive-summary-gpt-model-x_full_named_INTERNAL.md': documentTime,
        'executive-summary-gpt-model-x_full_named_INTERNAL.html': documentTime,
        'technical-report-gpt-model-x_full_named_INTERNAL.md': documentTime,
        'technical-report-gpt-model-x_full_named_INTERNAL.html': documentTime,
        'internal-improvement-brief-gpt-model-x_full_named_INTERNAL.md': documentTime,
        'internal-improvement-brief-gpt-model-x_full_named_INTERNAL.html': documentTime,
        'MANIFEST.md': NOW.getTime()
      };
      expect(Object.keys(zip.files).sort()).toEqual(Object.keys(expected).sort());
      for (const [name, time] of Object.entries(expected)) {
        expect(zip.times.get(name)?.getTime()).withContext(name).toBe(time);
      }

      // One fetch per source, however many formats use it.
      expect(requested.filter(url => url.endsWith('/runs/42/report')).length).toBe(1);
      expect(requested.filter(url => url.endsWith('/render')).length).toBe(3);

      const manifest = zip.files['MANIFEST.md'];
      expect(manifest).toContain('- **Package:** Internal package');
      expect(manifest).toContain('- **Files:** 10');
      for (const name of Object.keys(expected).filter(n => n !== 'MANIFEST.md')) {
        expect(manifest).toContain(`\`${name}\``);
      }
      expect(manifest).toContain('- **Document id:** 1');
      expect(manifest).toContain('- **Renderer version:** 3');
      expect(manifest).toContain('- **Writer:** Claude Opus');
      expect(manifest).toContain('- **Created:** 2026-09-20T09:30:12Z');
      expect(manifest).toContain('Run diagnostics (captured now)');
      const hash = await sha256Hex(zip.files['executive-summary-gpt-model-x_full_named_INTERNAL.md']);
      expect(manifest).toContain(`\`${hash}\``);
      expect(zip.files['board-suite_gpt-model-x_run42_diagnostics_INTERNAL.txt']).toBe('=== BENCHMARK RUN DIAGNOSTICS ===\n');
      expect(host().querySelector('[role="status"]')!.textContent!.trim()).toBe('Downloaded 10 files as one ZIP.');
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

      choose('internal', { 'diag:42': {} });
      await runDownload();
      expect(saveText.calls.mostRecent().args[0]).toBe('board-suite_gpt-model-x_run42_diagnostics_INTERNAL.txt');
      expect(saveText.calls.mostRecent().args[2]).toBe('text/plain;charset=utf-8');
    });

    it('keeps the server file name of the run report and tool-call log, with _INTERNAL', async () => {
      openRun();
      choose('custom', { 'report:42': { formats: ['html'] } });
      await runDownload();
      expect(saveText.calls.mostRecent().args[0]).toBe('Board_Suite_GPT_Model_X_20260921_160000_INTERNAL.html');
      expect(saveText.calls.mostRecent().args[1]).toContain('<!DOCTYPE html>');

      choose('custom', { 'log:42': {} });
      await runDownload();
      expect(saveText.calls.mostRecent().args[0]).toBe('Board_Suite_GPT_Model_X_run42_tool_calls_INTERNAL.md');
      expect(saveText.calls.mostRecent().args[1]).toBe('# Tool calls\n');
    });

    it('lists a file that fails and still downloads the rest', async () => {
      openRun();

      await runDownload({ reportStatus: 404 });

      const zip = await savedZip();
      expect(Object.keys(zip.files).length).toBe(9);
      expect(Object.keys(zip.files).some(name => name.startsWith('Board_Suite_GPT_Model_X_2026'))).toBeFalse();
      expect(zip.files['MANIFEST.md']).toContain('## Not included');
      expect(zip.files['MANIFEST.md']).toContain('Run report, run #42 (Markdown): the run no longer exists');
      expect(component.failures.map(f => `${f.label}: ${f.reason}`)).toEqual([
        'Run report, run #42 (Markdown): the run no longer exists',
        'Run report, run #42 (HTML): the run no longer exists'
      ]);
      expect(host().querySelector('.dc-failures')!.textContent).toContain('the run no longer exists');
      expect(host().querySelector('[role="status"]')!.textContent!.trim()).toBe('Downloaded 8 of 10 files as one ZIP; 2 failed.');
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
      expect(zip.times.get('Board_Suite_GPT_Model_X_20260921_160000_INTERNAL.md')?.getTime())
        .toBe(new Date('2026-09-21T16:00:00Z').getTime());
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
