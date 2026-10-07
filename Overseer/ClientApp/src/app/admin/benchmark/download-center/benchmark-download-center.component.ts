import {
  ChangeDetectionStrategy,
  ChangeDetectorRef,
  Component,
  ElementRef,
  EventEmitter,
  OnDestroy,
  OnInit,
  Output,
  ViewChild,
  inject
} from '@angular/core';
import { Subscription } from 'rxjs';

import { ensureOverlayPolyfills } from '../../../utils/polyfills.util';
import { DownloadCenterContext, DownloadCenterPanelComponent } from './download-center-panel.component';

export {
  CHART_SKIP_REASONS,
  DOWNLOAD_CENTER_REPORT_JOB_POLL_MS,
  DOWNLOAD_CENTER_STORAGE_KEY,
  DOWNLOAD_PACKAGES,
  INCLUDE_MEMBER_RUNS_TIP,
  INTERNAL_REASONS,
  MEMBER_RUNS_FAILED_NOTICE,
  PDF_PAPERS,
  ROW_NOTES,
  STORED_SETTINGS_VERSION,
  downloadCenterIo,
  internalServerName,
  rememberedPdfPaper,
  reportDocumentFileStem,
  reportJobPhaseText
} from './download-center-panel.component';
export type {
  DownloadCenterBatteryContext,
  DownloadCenterChartActions,
  DownloadCenterChatConsistencyContext,
  DownloadCenterContext,
  DownloadCenterDocumentsContext,
  DownloadCenterLibraryContext,
  DownloadCenterLibraryScope,
  DownloadCenterPreselect,
  DownloadCenterRunContext,
  DownloadCenterRunInfo,
  DownloadCenterSubjectScope,
  DownloadFailure,
  DownloadFormat,
  DownloadPackage,
  DownloadPackageId,
  DownloadProgress,
  DownloadRow,
  DownloadRowCategory,
  DownloadRowKind,
  DownloadRowState
} from './download-center-panel.component';

let nextInstanceId = 0;

/**
 * The Download Center dialog: its `<dialog>`, header and close button around
 * `app-download-center-panel`, which holds the packages, the documents table and the download.
 * Opened by the run report's and the battery run report's **Downloads** and by the Model Comparison
 * launcher's **Open Download Center**, and on a saved chat consistency analysis's documents from the
 * Chat Consistency tab; none lends chart actions. The panel's **Open comparison
 * documents** switches this dialog to the comparison documents about the run or battery run.
 *
 * The panel's nested dialogs stop their own close and cancel events, so this dialog's `close` event
 * is always its own.
 */
@Component({
  selector: 'app-benchmark-download-center',
  standalone: true,
  imports: [DownloadCenterPanelComponent],
  templateUrl: './benchmark-download-center.component.html',
  styleUrls: ['./benchmark-download-center.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class BenchmarkDownloadCenterComponent implements OnInit, OnDestroy {
  private readonly cdr = inject(ChangeDetectorRef);

  @ViewChild('downloadCenterDialog') dialog?: ElementRef<HTMLDialogElement>;
  @ViewChild('downloadCenterHeading') heading?: ElementRef<HTMLElement>;
  @ViewChild(DownloadCenterPanelComponent, { static: true }) panel!: DownloadCenterPanelComponent;

  /** Emitted when the dialog closes, however it was closed. */
  @Output() readonly closed = new EventEmitter<void>();

  /** A document was deleted, or its charts changed, while the dialog was open. */
  @Output() readonly documentsChanged = new EventEmitter<void>();

  /** The panel's Open battery run downloads: the id of the battery run the listed run is a member of. */
  @Output() readonly openBatteryDownloads = new EventEmitter<number>();

  readonly idPrefix = `dc${++nextInstanceId}`;

  context: DownloadCenterContext | null = null;

  private batteryDownloadsSub: Subscription | null = null;
  private comparisonDocumentsSub: Subscription | null = null;

  ngOnInit(): void {
    ensureOverlayPolyfills();
    this.batteryDownloadsSub = this.panel.openBatteryDownloads.subscribe(id => this.openBatteryDownloads.emit(id));
    this.comparisonDocumentsSub = this.panel.openComparisonDocuments.subscribe(context => this.open(context));
  }

  ngOnDestroy(): void {
    this.batteryDownloadsSub?.unsubscribe();
    this.comparisonDocumentsSub?.unsubscribe();
  }

  /** Shows the dialog for a run, a battery run, chosen documents, a library or a chat consistency analysis, at the last package and paper used. */
  open(context: DownloadCenterContext): void {
    this.context = context;
    this.panel.load(context);
    const dialog = this.dialog?.nativeElement;
    if (dialog && !dialog.open) {
      dialog.showModal();
    }
    // showModal() focuses the first focusable element, the Close button, whose hint tooltip opens on focus.
    this.heading?.nativeElement.focus();
    this.cdr.markForCheck();
  }

  close(): void {
    this.dialog?.nativeElement?.close();
  }

  /**
   * The native close event: Escape, the close button, or close() from the host. A download in
   * preparation is abandoned with its requests aborted, and nothing is saved.
   */
  onDialogClose(): void {
    // The close event is queued: one that arrives after a reopen belongs to the earlier opening.
    if (this.dialog?.nativeElement?.open) {
      return;
    }
    this.panel.deactivate();
    this.cdr.markForCheck();
    this.closed.emit();
  }

  get title(): string {
    const context = this.context;
    if (context?.kind === 'documents' && context.title) {
      return context.title;
    }
    if (context?.kind === 'library') {
      return context.title || (context.scope.kind === 'subject' ? 'Comparison documents' : 'Report documents');
    }
    if (context?.kind === 'chatConsistency') {
      return 'Chat consistency documents';
    }
    return 'Downloads';
  }

  get subtitle(): string {
    const context = this.context;
    if (!context) {
      return '';
    }
    if (context.kind === 'run') {
      return `Run #${context.run.id} · ${context.run.suiteName} · ${context.run.modelLabel}`;
    }
    if (context.kind === 'battery') {
      return context.label
        ? `Battery run #${context.batteryRunId} · ${context.label}`
        : `Battery run #${context.batteryRunId}`;
    }
    if (context.kind === 'chatConsistency') {
      return `Chat consistency analysis #${context.analysisId}`;
    }
    if (context.subtitle) {
      return context.subtitle;
    }
    if (context.kind === 'library') {
      switch (context.scope.kind) {
        case 'comparison': return 'The report documents of this comparison';
        case 'subject': return `About ${context.scope.label}`;
        default: return 'Every report document written from a model comparison';
      }
    }
    const count = context.documentIds.length;
    return `${count} report document${count === 1 ? '' : 's'}`;
  }
}
