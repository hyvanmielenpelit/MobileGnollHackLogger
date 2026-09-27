import { ChangeDetectionStrategy, Component, ElementRef, Input, ViewChild } from '@angular/core';
import { BenchmarkScoringProfileDto } from '../../../services/admin-benchmark.service';

/** The guide's sections that `open()` can scroll to; each heading carries the id `graderGuide-<section>`. */
export type GraderGuideSection =
  | 'roles'
  | 'second-reader'
  | 'coverage'
  | 'reference-reader'
  | 'claim-verifier'
  | 'models'
  | 'recommended';

/**
 * The scoring-profile settings the guide prints. `secondOpinionMinimumSample` is optional because
 * the client DTO does not declare it, although the server sends it; a missing value prints the
 * Standard profile default.
 */
export type GraderGuideProfile =
  Pick<BenchmarkScoringProfileDto, 'name' | 'secondOpinionQualityThreshold' | 'secondOpinionOutlierDeltaPoints' | 'secondOpinionBlind'>
  & { secondOpinionMinimumSample?: number };

/**
 * The *How the graders work* guide: what each grading role does, how coverage and the other
 * second-reader settings affect grading, and which models to use. A modal dialog the host opens,
 * optionally at one section.
 */
@Component({
  selector: 'app-benchmark-grader-guide',
  standalone: true,
  templateUrl: './benchmark-grader-guide.component.html',
  styleUrls: ['./benchmark-grader-guide.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class BenchmarkGraderGuideComponent {
  /** The Standard profile's seeded values (BenchmarkScoringProfileService). */
  static readonly DEFAULT_THRESHOLD = 50;
  static readonly DEFAULT_DELTA = 25;
  static readonly DEFAULT_SAMPLE = 4;
  static readonly DEFAULT_BLIND = true;
  static readonly DEFAULT_MARKER = ' (Standard profile default)';

  @ViewChild('graderGuideDialog') dialog!: ElementRef<HTMLDialogElement>;
  @ViewChild('graderGuideBody') body?: ElementRef<HTMLElement>;

  /** The profile whose settings fill the guide; null prints the Standard profile defaults. */
  @Input() profile: GraderGuideProfile | null = null;

  /** Where the printed settings come from. */
  get profileNote(): string {
    return this.profile
      ? `Settings shown are those of the scoring profile ${this.profile.name}.`
      : 'Settings shown are the Standard profile defaults.';
  }

  get threshold(): string {
    const value = this.profile?.secondOpinionQualityThreshold;
    if (value == null) {
      return BenchmarkGraderGuideComponent.DEFAULT_THRESHOLD + BenchmarkGraderGuideComponent.DEFAULT_MARKER;
    }
    return value === 0 ? 'off (critical errors only)' : `${value}`;
  }

  get delta(): string {
    return this.numberSetting(this.profile?.secondOpinionOutlierDeltaPoints, BenchmarkGraderGuideComponent.DEFAULT_DELTA);
  }

  get sample(): string {
    return this.numberSetting(this.profile?.secondOpinionMinimumSample, BenchmarkGraderGuideComponent.DEFAULT_SAMPLE);
  }

  get blind(): string {
    const value = this.profile?.secondOpinionBlind;
    if (value == null) {
      return this.onOff(BenchmarkGraderGuideComponent.DEFAULT_BLIND) + BenchmarkGraderGuideComponent.DEFAULT_MARKER;
    }
    return this.onOff(value);
  }

  /** Shows the guide modally and brings the named section's heading into view, or the top without one. */
  open(section?: GraderGuideSection): void {
    const dialog = this.dialog?.nativeElement;
    if (!dialog) {
      return;
    }
    if (!dialog.open) {
      dialog.showModal();
    }
    const heading = section ? dialog.querySelector<HTMLElement>(`#graderGuide-${section}`) : null;
    if (heading) {
      heading.scrollIntoView({ block: 'start' });
    } else {
      this.body?.nativeElement.scrollTo({ top: 0 });
    }
  }

  close(): void {
    this.dialog?.nativeElement?.close();
  }

  /**
   * Light dismiss where `closedby` is unsupported: a backdrop click reports the dialog itself as
   * the target, so a hit outside its border box closes it. A no-op where `closedby` exists.
   */
  onDialogClick(event: MouseEvent): void {
    if ('closedBy' in HTMLDialogElement.prototype) {
      return;
    }
    const dialog = this.dialog?.nativeElement;
    if (!dialog || event.target !== dialog) {
      return;
    }
    const rect = dialog.getBoundingClientRect();
    const inside = rect.top <= event.clientY && event.clientY <= rect.top + rect.height
      && rect.left <= event.clientX && event.clientX <= rect.left + rect.width;
    if (!inside) {
      dialog.close();
    }
  }

  private numberSetting(value: number | null | undefined, fallback: number): string {
    return value == null ? fallback + BenchmarkGraderGuideComponent.DEFAULT_MARKER : `${value}`;
  }

  private onOff(value: boolean): string {
    return value ? 'on' : 'off';
  }
}
