import { ChangeDetectionStrategy, Component, Input } from '@angular/core';
import type { BenchmarkEvidenceKind, BenchmarkEvidenceUnit } from '../../../services/admin-benchmark.service';

const KIND_CHIPS: Readonly<Record<BenchmarkEvidenceKind, string>> = {
  wiki: 'Wiki',
  source: 'Source',
  kb: 'KB'
};

/** A question's gold evidence as a read-only list, one unit per line. */
@Component({
  selector: 'app-question-evidence-list',
  standalone: true,
  templateUrl: './question-evidence-list.component.html',
  styleUrls: ['./question-evidence-list.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class QuestionEvidenceListComponent {
  @Input() units: BenchmarkEvidenceUnit[] = [];

  kindChip(kind: BenchmarkEvidenceKind | string): string {
    return KIND_CHIPS[kind as BenchmarkEvidenceKind] ?? kind;
  }
}
