import {
  ChangeDetectionStrategy,
  ChangeDetectorRef,
  Component,
  ElementRef,
  EventEmitter,
  Input,
  OnInit,
  Output,
  inject
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import type { BenchmarkEvidenceKind, BenchmarkEvidenceUnit } from '../../../services/admin-benchmark.service';
import { ensureOverlayPolyfills, refreshAnchorPositioning } from '../../../utils/polyfills.util';
import { EVIDENCE_KINDS, EVIDENCE_KIND_LABELS, validateEvidenceUnits } from './question-evidence';

const PATH_PLACEHOLDERS: Readonly<Record<BenchmarkEvidenceKind, string>> = {
  wiki: 'Races/Human.md',
  source: 'src/bones.c',
  kb: 'Knowledge base topic'
};

/**
 * Edits a question's gold evidence as rows. Add and Remove emit a new array; typing edits the
 * row objects in place, which the host sees because it holds the same objects. Fields a kind does
 * not use stay on the row while hidden and are dropped when the host normalizes on save.
 */
@Component({
  selector: 'app-question-evidence-editor',
  standalone: true,
  imports: [FormsModule],
  templateUrl: './question-evidence-editor.component.html',
  styleUrls: ['./question-evidence-editor.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class QuestionEvidenceEditorComponent implements OnInit {
  private readonly cdr = inject(ChangeDetectorRef);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);

  @Input() units: BenchmarkEvidenceUnit[] = [];
  @Output() unitsChange = new EventEmitter<BenchmarkEvidenceUnit[]>();

  /** Prefix of every id in the editor, unique in the document. */
  @Input() idPrefix = 'qEvidence';

  readonly kinds = EVIDENCE_KINDS;
  readonly kindLabels = EVIDENCE_KIND_LABELS;

  ngOnInit(): void {
    ensureOverlayPolyfills();
  }

  get error(): string | null {
    return validateEvidenceUnits(this.units);
  }

  pathPlaceholder(kind: BenchmarkEvidenceKind): string {
    return PATH_PLACEHOLDERS[kind] ?? '';
  }

  addUnit(): void {
    this.units = [...this.units, { kind: 'wiki', path: '' }];
    this.unitsChange.emit(this.units);
    this.cdr.detectChanges();
    refreshAnchorPositioning();
    this.focus(`${this.idPrefix}-path-${this.units.length - 1}`);
  }

  removeUnit(index: number): void {
    this.units = this.units.filter((_, i) => i !== index);
    this.unitsChange.emit(this.units);
    this.cdr.detectChanges();
    refreshAnchorPositioning();
    this.focus(`${this.idPrefix}-add`);
  }

  private focus(id: string): void {
    this.host.nativeElement.querySelector<HTMLElement>(`#${CSS.escape(id)}`)?.focus();
  }
}
