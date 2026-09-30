import { ChangeDetectionStrategy, Component, ViewChild } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, TestRequest, provideHttpClientTesting } from '@angular/common/http/testing';

import { BenchmarkReportAudience, BenchmarkReportDocumentListItemDto } from '../../../services/admin-benchmark.service';
import { REPORT_LIBRARY_ALL_TAKE } from './report-document-format';
import { ReportDocumentsLauncherComponent } from './report-documents-launcher.component';

const DOCUMENTS_URL = '/api/admin/benchmark/report-documents';

function doc(id: number, overrides: Partial<BenchmarkReportDocumentListItemDto> = {}): BenchmarkReportDocumentListItemDto {
  return {
    id,
    packId: 'pack-1',
    audience: BenchmarkReportAudience.ExecutiveSummary,
    title: `Executive Summary ${id}`,
    subjectKey: 'run:1',
    subjectLabel: 'Gemini Flash',
    subjectRunIds: [1],
    suiteId: 5,
    suiteName: 'Board Suite',
    writerDisplayName: 'Claude Opus writer',
    writerProvider: 'Anthropic',
    writerModelId: 'claude-opus',
    writerThinkingLevel: null,
    sameProviderAcknowledged: false,
    status: 'Completed',
    reportFormatVersion: 1,
    createdAtUtc: `2026-09-2${id % 10}T16:00:00Z`,
    inputTokens: 0,
    outputTokens: 0,
    durationMs: 0,
    costUsd: 0.05,
    runChangedSinceGeneration: false,
    missingRunIds: [],
    allowedDisclosures: [1, 3],
    comparisonKey: 'cmp-1',
    peerCount: 2,
    peersChangedSinceGeneration: false,
    ...overrides
  };
}

@Component({
  standalone: true,
  imports: [ReportDocumentsLauncherComponent],
  changeDetection: ChangeDetectionStrategy.Eager,
  template: `<app-report-documents-launcher [reloadToken]="reloadToken"></app-report-documents-launcher>`
})
class LauncherHostComponent {
  @ViewChild(ReportDocumentsLauncherComponent, { static: true }) launcher!: ReportDocumentsLauncherComponent;
  reloadToken = 0;
}

describe('ReportDocumentsLauncherComponent', () => {
  let fixture: ComponentFixture<LauncherHostComponent>;
  let http: HttpTestingController;
  let el: HTMLElement;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [LauncherHostComponent],
      providers: [provideHttpClient(), provideHttpClientTesting()]
    }).compileComponents();
    fixture = TestBed.createComponent(LauncherHostComponent);
    http = TestBed.inject(HttpTestingController);
    el = fixture.nativeElement as HTMLElement;
  });

  afterEach(() => {
    el.querySelectorAll('dialog').forEach(dialog => {
      if (dialog.open) {
        dialog.close();
      }
    });
    fixture.destroy();
  });

  const launcher = (): ReportDocumentsLauncherComponent => fixture.componentInstance.launcher;
  const text = (selector: string): string => (el.querySelector(selector)?.textContent ?? '').replace(/\s+/g, ' ').trim();
  const openButton = (): HTMLButtonElement => el.querySelector<HTMLButtonElement>('#mcl-open')!;

  function expectList(): TestRequest {
    const request = http.expectOne(r => r.method === 'GET' && r.url === DOCUMENTS_URL);
    expect(request.request.params.get('origin')).toBe('reportPack');
    expect(request.request.params.get('take')).toBe(String(REPORT_LIBRARY_ALL_TAKE));
    return request;
  }

  function render(documents: BenchmarkReportDocumentListItemDto[]): void {
    fixture.detectChanges();
    expectList().flush(documents);
    fixture.detectChanges();
  }

  it('summarizes every comparison document and tags those changed since written', () => {
    render([
      doc(11),
      doc(12, { runChangedSinceGeneration: true }),
      doc(13, { comparisonKey: 'cmp-2', peersChangedSinceGeneration: true })
    ]);

    expect(text('.rdl-launcher-heading')).toBe('Comparison reports');
    expect(text('.rdl-launcher-summary')).toBe('3 report documents from 2 comparisons · the latest written 2026-09-23 16:00 UTC');
    expect(el.querySelector('.rdl-launcher-summary')!.getAttribute('role')).toBe('status');
    const changed = el.querySelector('.rdl-launcher-changed')!;
    expect(changed.classList).toContain('gh-tag-changed');
    expect(changed.textContent).toContain('2 changed since written');
  });

  it('puts the explanation behind a click-mode info tip', () => {
    render([doc(11)]);

    const tip = el.querySelector('app-info-tip')!;
    expect(tip.querySelector('button.gh-info-btn')!.getAttribute('aria-label')).toBe('About Comparison reports');
    expect(tip.querySelector('button.gh-info-btn')!.hasAttribute('popovertarget')).toBeTrue();
    expect(el.querySelector('#mcl-tip')!.textContent).toContain('A run\'s own reports are in its run report.');
  });

  it('says No reports yet and keeps Open Download Center aria-disabled with that reason', () => {
    render([]);
    const open = spyOn(launcher().downloadCenter!, 'open');

    expect(text('.rdl-launcher-summary')).toContain('No reports yet.');
    expect(el.querySelector('.rdl-launcher-changed')).toBeNull();
    const button = openButton();
    expect(button.classList).toContain('btn-ghost');
    expect(button.getAttribute('aria-disabled')).toBe('true');
    expect(button.getAttribute('aria-describedby')!.split(' ')).toContain('mcl-summary');
    button.click();
    expect(open).not.toHaveBeenCalled();
  });

  it('opens the Download Center on every comparison document with nothing preselected', () => {
    render([doc(11), doc(12)]);
    const open = spyOn(launcher().downloadCenter!, 'open');

    const button = openButton();
    expect(button.hasAttribute('aria-disabled')).toBeFalse();
    expect(button.textContent!.trim()).toBe('Open Download Center');
    expect(button.querySelector('svg.btn-icon')!.getAttribute('aria-hidden')).toBe('true');
    button.click();

    expect(open).toHaveBeenCalledOnceWith(jasmine.objectContaining({
      kind: 'library',
      scope: { kind: 'all' },
      preselect: 'none',
      title: 'Comparison reports'
    }));
  });

  it('counts again on the reload token, when the Download Center closes and when a document changes', () => {
    render([doc(11)]);
    const button = openButton();

    fixture.componentInstance.reloadToken++;
    fixture.detectChanges();
    expectList().flush([doc(11), doc(12)]);
    fixture.detectChanges();
    expect(text('.rdl-launcher-summary')).toContain('2 report documents');

    launcher().downloadCenter!.documentsChanged.emit();
    expectList().flush([doc(11)]);

    launcher().downloadCenter!.closed.emit();
    expectList().flush([doc(11)]);
    fixture.detectChanges();
    expect(document.activeElement).toBe(button);
    expect(text('.rdl-launcher-summary')).toContain('1 report document from 1 comparison');
  });
});
