import {
  ChangeDetectionStrategy,
  ChangeDetectorRef,
  Component,
  ElementRef,
  EventEmitter,
  Input,
  Output,
  ViewChild,
  inject
} from '@angular/core';
import { CommonModule } from '@angular/common';
import {
  BenchmarkQuestionDto,
  BenchmarkSuiteDto,
  ImportBenchmarkQuestionsResultDto
} from '../../../services/admin-benchmark.service';
import { ensureOverlayPolyfills, refreshAnchorPositioning } from '../../../utils/polyfills.util';
import { ImportMode } from './question-yaml-format';
import { ImportStep, QuestionYamlImportPanelComponent } from './question-yaml-import-panel.component';

export { errorText, MAX_IMPORT_FILE_BYTES } from './question-yaml-import-panel.component';
export type { ImportSource, ImportStep, ReviewCard, ReviewView } from './question-yaml-import-panel.component';

/**
 * The standalone YAML import: a dialog shell with the stepper and the footer around
 * `app-question-yaml-import-panel`, which holds the import itself.
 */
@Component({
  selector: 'app-question-yaml-import-dialog',
  standalone: true,
  imports: [CommonModule, QuestionYamlImportPanelComponent],
  templateUrl: './question-yaml-import-dialog.component.html',
  styleUrls: ['./question-yaml-import-dialog.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class QuestionYamlImportDialogComponent {
  private cdr = inject(ChangeDetectorRef);

  @ViewChild('dialog') dialog!: ElementRef<HTMLDialogElement>;
  @ViewChild('panel') panel!: QuestionYamlImportPanelComponent;

  /** The suite whose questions are replaced or extended; unused in suite mode. */
  @Input() suite: BenchmarkSuiteDto | null = null;
  @Input() existing: BenchmarkQuestionDto[] = [];
  /** Every suite, for the name-collision note in suite mode. */
  @Input() suites: BenchmarkSuiteDto[] = [];

  @Output() imported = new EventEmitter<ImportBenchmarkQuestionsResultDto>();
  @Output() suiteImported = new EventEmitter<BenchmarkSuiteDto>();
  @Output() helpRequested = new EventEmitter<void>();

  readonly steps: ReadonlyArray<{ id: ImportStep; label: string }> = [
    { id: 1, label: 'Provide YAML' },
    { id: 2, label: 'Review' },
    { id: 3, label: 'Done' }
  ];

  mode: ImportMode = 'questions';
  target: BenchmarkQuestionDto | null = null;

  get step(): ImportStep {
    return this.panel?.step ?? 1;
  }

  get applying(): boolean {
    return this.panel?.applying ?? false;
  }

  open(mode: ImportMode, target?: BenchmarkQuestionDto): void {
    this.mode = mode;
    this.target = target ?? null;
    // Pushes mode and target into the panel before it resets against them.
    this.cdr.detectChanges();
    this.panel.reset();
    ensureOverlayPolyfills();
    const el = this.dialog?.nativeElement;
    if (el && !el.open) {
      el.showModal();
    }
    this.cdr.detectChanges();
    setTimeout(() => refreshAnchorPositioning(), 0);
  }

  close(): void {
    if (this.applying) return;
    this.panel?.cancel();
    this.dialog?.nativeElement?.close();
  }

  /* Escape on the dialog; a request in flight is not abandoned half way. */
  onCancel(event: Event): void {
    if (this.applying) {
      event.preventDefault();
    }
  }

  get title(): string {
    switch (this.mode) {
      case 'single':
        return this.target ? `Import YAML into question #${this.target.orderIndex}` : 'Import YAML into a question';
      case 'questions':
        return `Import questions into ${this.suite?.name ?? 'this suite'}`;
      default:
        return 'Import a suite from YAML';
    }
  }

  onPanelState(): void {
    this.cdr.detectChanges();
  }
}
