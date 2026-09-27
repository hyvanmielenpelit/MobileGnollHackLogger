import { NgTemplateOutlet } from '@angular/common';
import { ChangeDetectionStrategy, ChangeDetectorRef, Component, Input, OnInit, inject } from '@angular/core';

import { ensureOverlayPolyfills, refreshAnchorPositioning } from '../../utils/polyfills.util';

/**
 * An (i) button that explains a control. The explanation is projected content.
 * `trigger="hover"` shows it in an interest-triggered tooltip; `trigger="click"` in a
 * non-modal, light-dismiss popup the button toggles.
 *
 * The element holding the text carries `tipId` in both modes, so the control the hint describes
 * can point its `aria-describedby` at it: that reads the text even while it is hidden.
 */
@Component({
  selector: 'app-info-tip',
  standalone: true,
  imports: [NgTemplateOutlet],
  templateUrl: './info-tip.component.html',
  styles: [':host { display: inline-flex; vertical-align: middle; }'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class InfoTipComponent implements OnInit {
  private readonly cdr = inject(ChangeDetectorRef);

  /** The text element's `id`, unique in the document; also the anchor name. */
  @Input({ required: true }) tipId = '';

  /** What the hint is about, for the button's accessible name: `About {subject}`. */
  @Input({ required: true }) subject = '';

  /** `hover`: an interest-triggered tooltip. `click`: a popup the button toggles. */
  @Input() trigger: 'hover' | 'click' = 'hover';

  /** Whether the click-mode popup is open; the popover polyfill does not set `aria-expanded`. */
  open = false;

  ngOnInit(): void {
    ensureOverlayPolyfills();
  }

  onToggle(event: Event): void {
    this.open = (event as ToggleEvent).newState === 'open';
    if (this.open) {
      refreshAnchorPositioning();
    }
    this.cdr.markForCheck();
  }
}
