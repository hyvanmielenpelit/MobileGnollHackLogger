import {
  ChangeDetectionStrategy,
  ChangeDetectorRef,
  Component,
  ElementRef,
  Input,
  OnChanges,
  OnDestroy,
  OnInit,
  SimpleChanges,
  ViewChild,
  inject
} from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import type { Subscription } from 'rxjs';

import { AdminBenchmarkService, BenchmarkReportDocumentListItemDto } from '../../../services/admin-benchmark.service';
import { InfoTipComponent } from '../../../shared/info-tip/info-tip.component';
import { ensureOverlayPolyfills } from '../../../utils/polyfills.util';
import { BenchmarkDownloadCenterComponent } from '../download-center/benchmark-download-center.component';
import { REPORT_LIBRARY_ALL_TAKE, formatUtc } from './report-document-format';

function plural(count: number, one: string, many: string): string {
  return `${count} ${count === 1 ? one : many}`;
}

/**
 * The Model Comparison launcher's *Comparison reports* card: how many report documents the model
 * comparisons have written, how many of them changed since, and **Open Download Center** on every
 * one of them with nothing preselected. It reloads when the host bumps `reloadToken` (the wizard
 * closed), when the Download Center closes, and when a document is deleted in it.
 */
@Component({
  selector: 'app-report-documents-launcher',
  standalone: true,
  imports: [InfoTipComponent, BenchmarkDownloadCenterComponent],
  templateUrl: './report-documents-launcher.component.html',
  styleUrls: ['./report-documents-launcher.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class ReportDocumentsLauncherComponent implements OnInit, OnChanges, OnDestroy {
  private readonly benchmarkService = inject(AdminBenchmarkService);
  private readonly cdr = inject(ChangeDetectorRef);

  /** Bumped by the host to count the documents again. */
  @Input() reloadToken = 0;
  /** The prefix of every element id. */
  @Input() idPrefix = 'mcl';

  @ViewChild(BenchmarkDownloadCenterComponent) downloadCenter?: BenchmarkDownloadCenterComponent;
  @ViewChild('openButton') openButton?: ElementRef<HTMLButtonElement>;

  documents: BenchmarkReportDocumentListItemDto[] = [];
  /** The list has answered at least once. */
  loaded = false;
  loading = false;
  loadError: string | null = null;

  private generation = 0;
  private listSub: Subscription | null = null;

  ngOnInit(): void {
    ensureOverlayPolyfills();
    this.reload();
  }

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['reloadToken'] && !changes['reloadToken'].firstChange) {
      this.reload();
    }
  }

  ngOnDestroy(): void {
    this.generation++;
    this.listSub?.unsubscribe();
  }

  /** Lists every Report Pack document again; a list in flight is dropped. */
  reload(): void {
    const generation = ++this.generation;
    this.loading = true;
    this.listSub?.unsubscribe();
    this.listSub = this.benchmarkService.listReportDocuments({ origin: 'reportPack', take: REPORT_LIBRARY_ALL_TAKE }).subscribe({
      next: documents => {
        if (generation !== this.generation) {
          return;
        }
        this.documents = documents ?? [];
        this.loaded = true;
        this.loading = false;
        this.loadError = null;
        this.cdr.markForCheck();
      },
      error: (error: HttpErrorResponse) => {
        if (generation !== this.generation) {
          return;
        }
        this.loaded = true;
        this.loading = false;
        this.loadError = typeof error?.error?.error === 'string' ? error.error.error : 'The report documents could not be listed.';
        this.cdr.markForCheck();
      }
    });
    this.cdr.markForCheck();
  }

  /** Documents whose subject run or a peer's run changed since they were written. */
  get changedCount(): number {
    return this.documents.filter(doc => doc.runChangedSinceGeneration || doc.peersChangedSinceGeneration).length;
  }

  /**
   * The comparisons the documents were written for: by comparison number; a document without one
   * (written before comparisons were numbered) by the number another document of its comparison key
   * carries, else by that key, else by its subject.
   */
  get comparisonCount(): number {
    const numberOfKey = new Map<string, number>();
    for (const doc of this.documents) {
      if (doc.comparisonId !== null && doc.comparisonId !== undefined && doc.comparisonKey) {
        numberOfKey.set(doc.comparisonKey, doc.comparisonId);
      }
    }
    return new Set(this.documents.map(doc => {
      const id = doc.comparisonId ?? (doc.comparisonKey ? numberOfKey.get(doc.comparisonKey) : undefined);
      if (id !== null && id !== undefined) {
        return `id:${id}`;
      }
      return doc.comparisonKey ? `key:${doc.comparisonKey}` : `subject:${doc.subjectKey}`;
    })).size;
  }

  /** `5 report documents from 2 comparisons · the latest written 2026-09-21 16:00 UTC`. */
  get summary(): string {
    if (!this.loaded) {
      return 'Counting the report documents…';
    }
    if (this.loadError) {
      return this.loadError;
    }
    if (this.documents.length === 0) {
      return 'No reports yet. Reports written on the comparison wizard’s Reports step appear here.';
    }
    const latest = this.documents.map(doc => doc.createdAtUtc ?? '').sort().pop() ?? '';
    return `${plural(this.documents.length, 'report document', 'report documents')} from `
      + `${plural(this.comparisonCount, 'comparison', 'comparisons')}${latest ? ` · the latest written ${formatUtc(latest)}` : ''}`;
  }

  /** Why Open Download Center is unavailable, or null; the summary line says it. */
  get openBlocked(): boolean {
    return !this.loaded || this.loadError !== null || this.documents.length === 0;
  }

  openDownloadCenter(): void {
    if (this.openBlocked) {
      return;
    }
    this.downloadCenter?.open({
      kind: 'library',
      scope: { kind: 'all' },
      preselect: 'none',
      title: 'Comparison reports',
      subtitle: `${plural(this.documents.length, 'report document', 'report documents')} written from model comparisons`
    });
  }

  /** The Download Center closed: count again, and give focus back to the button that opened it. */
  onClosed(): void {
    this.reload();
    const button = this.openButton?.nativeElement;
    if (button?.isConnected) {
      button.focus();
    }
  }

  onDocumentsChanged(): void {
    this.reload();
  }
}
