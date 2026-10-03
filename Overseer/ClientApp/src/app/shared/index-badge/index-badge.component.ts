import { ChangeDetectionStrategy, Component, Input } from '@angular/core';

import { getScoreBadgeClass } from '../../admin/benchmark/benchmark-run-format';

/**
 * An Intelligence Index as a badge: a decorative ring filled to the index, the whole number and, when
 * known, its half-width as `± 4`. The tier color (`getScoreBadgeClass`: 80 / 50) repeats the number
 * and never replaces it; a visually hidden prefix names the figure for a screen reader. No tooltip and
 * no live region: the host announces changes, if at all. Styles are global (`.index-badge` in
 * `styles.scss`).
 */
@Component({
  selector: 'app-index-badge',
  standalone: true,
  templateUrl: './index-badge.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class IndexBadgeComponent {
  /** The index, 0–100. */
  @Input({ required: true }) value!: number;
  /** The half-width of the index's interval; null or absent hides it. */
  @Input() halfWidth: number | null | undefined = null;
  @Input() size: 'sm' | 'md' = 'sm';
  /** What the visually hidden prefix calls the figure. */
  @Input() label = 'Intelligence Index';

  /** The index as shown, rounded to a whole number; the tier follows this, so color and number agree. */
  get shownValue(): number {
    return Number.isFinite(this.value) ? Math.round(this.value) : 0;
  }

  /** The half-width as shown, rounded to a whole number; null when there is none. */
  get shownHalfWidth(): number | null {
    const halfWidth = this.halfWidth;
    return halfWidth != null && Number.isFinite(halfWidth) ? Math.round(halfWidth) : null;
  }

  get badgeClass(): string {
    return `index-badge index-badge-${this.size} ${getScoreBadgeClass(this.shownValue)}`;
  }

  /** The ring's fill, a percentage clamped to 0–100, for `--index-fill`. */
  get fill(): string {
    return String(Math.min(100, Math.max(0, this.shownValue)));
  }
}
