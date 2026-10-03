import { AfterViewInit, Component, ChangeDetectorRef, ElementRef, EventEmitter, Output, ViewChild, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import {
  AdminBenchmarkService,
  DefaultSuiteCatalogEntryDto,
  ImportDefaultSuitesResultDto
} from '../../../services/admin-benchmark.service';
import { CollapsibleMarkdownComponent } from '../../../shared/collapsible-markdown/collapsible-markdown.component';
import { BenchmarkWorkspaceStore } from '../state/benchmark-workspace.store';
import { BenchmarkViewSync } from '../state/benchmark-view-sync.service';
import { BenchmarkShellBridge } from '../state/benchmark-shell-bridge.service';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';

/** The Import Default Suites dialog. */
@Component({
  selector: 'app-import-default-suites-dialog',
  standalone: true,
  imports: [
    CommonModule, CollapsibleMarkdownComponent
  ],
  templateUrl: './import-default-suites-dialog.component.html',
  styleUrls: ['./import-default-suites-dialog.component.scss']
})
export class ImportDefaultSuitesDialogComponent implements AfterViewInit {
  private readonly viewSync = inject(BenchmarkViewSync);
  readonly bridge = inject(BenchmarkShellBridge);
  readonly workspace = inject(BenchmarkWorkspaceStore);
  private benchmarkService = inject(AdminBenchmarkService);
  private cdr = inject(ChangeDetectorRef);

  constructor() {
    // Service state changes outside this component's own events; OnPush needs telling.
    this.viewSync.changed$.pipe(takeUntilDestroyed()).subscribe(() => {
      this.cdr.markForCheck();
      this.cdr.detectChanges();
    });
  }

  @ViewChild('importDefaultSuitesDialog') importDefaultSuitesDialog!: ElementRef<HTMLDialogElement>;

  /** The polite announcement of a finished import; the host reloads the suites and shows it. */
  @Output() imported = new EventEmitter<string>();

  /**
   * Light-dismiss for the import-default-suites dialog where `closedby` is unsupported (Safari,
   * at the time of writing). A backdrop click reports the dialog itself as the target, so a hit
   * outside the dialog's own border box closes it. A no-op in every browser that has `closedby`.
   */
  ngAfterViewInit(): void {
    if ('closedBy' in HTMLDialogElement.prototype) {
      return;
    }
    const dialog = this.importDefaultSuitesDialog?.nativeElement;
    dialog?.addEventListener('click', (event: MouseEvent) => {
      if (event.target !== dialog) {
        return;
      }
      const rect = dialog.getBoundingClientRect();
      const inside = rect.top <= event.clientY && event.clientY <= rect.top + rect.height
        && rect.left <= event.clientX && event.clientX <= rect.left + rect.width;
      if (!inside) {
        dialog.close();
      }
    });
  }

  open(): void {
    this.selectedDefaultSuiteKeys.clear();
    this.defaultSuiteDialogError = null;
    this.importingDefaultSuites = false;
    this.loadDefaultSuiteCatalog();
    this.importDefaultSuitesDialog?.nativeElement.showModal();
  }

  loadDefaultSuiteCatalog(): void {
    this.loadingDefaultSuiteCatalog = true;
    this.benchmarkService.getDefaultSuiteCatalog().subscribe({
      next: (catalog) => {
        this.defaultSuiteCatalog = catalog;
        this.loadingDefaultSuiteCatalog = false;
        this.cdr.detectChanges();
      },
      error: (err) => {
        this.loadingDefaultSuiteCatalog = false;
        this.defaultSuiteDialogError = err?.error || 'Failed to load the default suite catalog.';
        this.cdr.detectChanges();
      }
    });
  }

  // Import Default Suites dialog
  defaultSuiteCatalog: DefaultSuiteCatalogEntryDto[] = [];

  loadingDefaultSuiteCatalog = false;

  selectedDefaultSuiteKeys = new Set<string>();

  importingDefaultSuites = false;

  defaultSuiteDialogError: string | null = null;

  /** In band order, since `difficultyCounts` is a plain object with no guaranteed key order. */
  private static readonly DIFFICULTY_BAND_ORDER: readonly string[] = ['Simple', 'Intermediate', 'Advanced'];

  toggleDefaultSuite(key: string): void {
    if (this.selectedDefaultSuiteKeys.has(key)) {
      this.selectedDefaultSuiteKeys.delete(key);
    } else {
      this.selectedDefaultSuiteKeys.add(key);
    }
  }

  isDefaultSuiteSelected(key: string): boolean {
    return this.selectedDefaultSuiteKeys.has(key);
  }

  get canImportDefaultSuites(): boolean {
    return this.selectedDefaultSuiteKeys.size > 0 && !this.importingDefaultSuites;
  }

  importSelectedDefaultSuites(): void {
    if (!this.canImportDefaultSuites) return;

    this.importingDefaultSuites = true;
    this.defaultSuiteDialogError = null;
    const keys = Array.from(this.selectedDefaultSuiteKeys);

    this.benchmarkService.importDefaultSuites(keys).subscribe({
      next: (result: ImportDefaultSuitesResultDto) => {
        this.importingDefaultSuites = false;
        this.importDefaultSuitesDialog?.nativeElement.close();
        this.imported.emit(this.formatImportAnnouncement(result));
        this.cdr.detectChanges();
      },
      error: (err) => {
        this.importingDefaultSuites = false;
        this.defaultSuiteDialogError = err?.error || 'Failed to import the selected suites.';
        this.cdr.detectChanges();
      }
    });
  }

  /** What the polite live region announces once an import request completes. */
  private formatImportAnnouncement(result: ImportDefaultSuitesResultDto): string {
    const importedNames = result.imported.map(s => s.name).join(', ');
    let text = result.imported.length > 0
      ? `Imported ${result.imported.length} suite${result.imported.length === 1 ? '' : 's'}: ${importedNames}`
      : 'No suites were imported.';
    if (result.skipped.length > 0) {
      text += ' Skipped: ' + result.skipped.map(s => `${s.key} — ${s.reason}`).join('; ');
    }
    return text;
  }

  /** The catalog entry's per-band question counts, in band order, one item per band present. */
  difficultyBands(entry: DefaultSuiteCatalogEntryDto): { band: string; count: number }[] {
    return ImportDefaultSuitesDialogComponent.DIFFICULTY_BAND_ORDER
      .filter(band => entry.difficultyCounts && entry.difficultyCounts[band] != null)
      .map(band => ({ band, count: entry.difficultyCounts[band] }));
  }

  /** Catalog entries that can actually be imported (invalid files are listed but not selectable). */
  get selectableDefaultSuiteCount(): number {
    return this.defaultSuiteCatalog.filter(e => !e.error).length;
  }
}
