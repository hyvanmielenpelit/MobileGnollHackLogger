import { ChangeDetectionStrategy, Component, Input } from '@angular/core';

/**
 * A wait, shown as the shared ring spinner (`.dc-ring` in `styles.scss`) and a line of text.
 *
 * `inline`: a 14 px ring beside the text, in the surrounding font size. `block`: a 40 px ring above
 * the text, centered in a column at least 8rem tall, in place of the content being loaded. With
 * `revealDelay` (the default) both fade in after 0.3 s, so a quick answer shows nothing.
 *
 * Accessibility: the indicator carries no live region and no `role`. The host owns the announcement,
 * with its own `role="status"` line or `aria-busy`; the text here is ordinary text, read in place. The
 * ring is `aria-hidden`.
 */
@Component({
  selector: 'app-loading-indicator',
  standalone: true,
  templateUrl: './loading-indicator.component.html',
  styleUrl: './loading-indicator.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {
    '[class.li-inline]': "size !== 'block'",
    '[class.li-block]': "size === 'block'",
    '[class.li-reveal]': 'revealDelay'
  }
})
export class LoadingIndicatorComponent {
  /** What is being waited for: *Estimating…*, *Loading report documents…*. */
  @Input({ required: true }) text = '';

  /** A small ring beside the text, or a large ring above it. */
  @Input() size: 'inline' | 'block' = 'inline';

  /** Fades in after 0.3 s; off where the host already shows the state and must not look empty. */
  @Input() revealDelay = true;

  /** The ring's width and height, in CSS px. */
  get ringSize(): number {
    return this.size === 'block' ? 40 : 14;
  }
}
