import { ChangeDetectionStrategy, Component, Input } from '@angular/core';

/** The status words of the shared `.gh-health-status` chip. */
export type HealthStatus = 'ok' | 'warning' | 'error' | 'info' | 'running';

/**
 * A status chip of the Services tab dashboards: the global `.gh-health-status` with its 14 px glyph
 * (`aria-hidden`: check-circle, alert-triangle, alert-circle, info, or the `.dc-ring` while running)
 * and the projected word.
 *
 * ```html
 * <app-health-status status="warning">Out of date</app-health-status>
 * ```
 *
 * The word is the meaning; the glyph and the tone only repeat it. The chip has no role and no live
 * region: a host that announces a change puts it inside its own `role="status"` element.
 */
@Component({
  selector: 'app-health-status',
  standalone: true,
  templateUrl: './health-status.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class HealthStatusComponent {
  /** The tone and glyph; the word is the projected content. */
  @Input({ required: true }) status: HealthStatus = 'info';
}
