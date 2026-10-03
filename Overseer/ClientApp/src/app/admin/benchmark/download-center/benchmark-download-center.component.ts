import {
  ChangeDetectionStrategy,
  ChangeDetectorRef,
  Component,
  ElementRef,
  EventEmitter,
  OnInit,
  Output,
  ViewChild,
  inject
} from '@angular/core';

import { ensureOverlayPolyfills } from '../../../utils/polyfills.util';
import { DownloadCenterContext, DownloadCenterPanelComponent } from './download-center-panel.component';

export {
  CHART_SKIP_REASONS,
  DOWNLOAD_CENTER_REPORT_JOB_POLL_MS,
  DOWNLOAD_CENTER_STORAGE_KEY,
  DOWNLOAD_PACKAGES,
  INTERNAL_REASONS,
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
  DownloadCenterContext,
  DownloadCenterDocumentsContext,
  DownloadCenterLibraryContext,
  DownloadCenterRunContext,
  DownloadCenterRunInfo,
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
 * launcher's **Open Download Center**; none lends chart actions.
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
export class BenchmarkDownloadCenterComponent implements OnInit {
  private readonly cdr = inject(ChangeDetectorRef);

  @ViewChild('downloadCenterDialog') dialog?: ElementRef<HTMLDialogElement>;
  @ViewChild('downloadCenterHeading') heading?: ElementRef<HTMLElement>;
  @ViewChild(DownloadCenterPanelComponent, { static: true }) panel!: DownloadCenterPanelComponent;

  /** Emitted when the dialog closes, however it was closed. */
  @Output() readonly closed = new EventEmitter<void>();

  /** A document was deleted, or its charts changed, while the dialog was open. */
  @Output() readonly documentsChanged = new EventEmitter<void>();

  readonly idPrefix = `dc${++nextInstanceId}`;

  context: DownloadCenterContext | null = null;

  ngOnInit(): void {
    ensureOverlayPolyfills();
  }

  /** Shows the dialog for a run, a battery run, chosen documents or a library, at the last package and paper used. */
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

  /** The native close event: Escape, Cancel, the close button, or close() from the host. */
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
      return context.title || 'Report documents';
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
    if (context.subtitle) {
      return context.subtitle;
    }
    if (context.kind === 'library') {
      return context.scope.kind === 'comparison'
        ? 'The report documents of this comparison and the reports of their runs'
        : 'Every report document written from a model comparison, and the reports of their runs';
    }
    const count = context.documentIds.length;
    return `${count} report document${count === 1 ? '' : 's'} and the reports of their runs`;
  }
}
