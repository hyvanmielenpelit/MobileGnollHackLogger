import { ChangeDetectionStrategy, Component, Input, OnInit } from '@angular/core';

import { ensureOverlayPolyfills } from '../../utils/polyfills.util';

/**
 * An (i) button that shows a short hint in an interest-triggered tooltip. The hint is projected
 * content.
 *
 * The tooltip element carries `tipId`, so the control the hint describes can point its
 * `aria-describedby` at it: that reads the text even while the tooltip is hidden.
 */
@Component({
  selector: 'app-info-tip',
  standalone: true,
  templateUrl: './info-tip.component.html',
  styles: [':host { display: inline-flex; vertical-align: middle; }'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class InfoTipComponent implements OnInit {
  /** The tooltip's `id`, unique in the document; also the anchor name. */
  @Input({ required: true }) tipId = '';

  /** What the hint is about, for the button's accessible name: `About {subject}`. */
  @Input({ required: true }) subject = '';

  ngOnInit(): void {
    ensureOverlayPolyfills();
  }
}
