import { NgTemplateOutlet } from '@angular/common';
import {
  ChangeDetectionStrategy,
  ChangeDetectorRef,
  Component,
  ElementRef,
  Input,
  OnInit,
  ViewChild,
  inject
} from '@angular/core';

import { ensureOverlayPolyfills, refreshAnchorPositioning } from '../../utils/polyfills.util';

/**
 * An (i) button that explains a control. The explanation is projected content.
 * `trigger="hover"` shows it in an interest-triggered tooltip; `trigger="click"` in a
 * non-modal, light-dismiss popup the button toggles; `trigger="dialog"` in a modal dialog, for
 * an explanation longer than a popup holds.
 *
 * The element holding the text carries `tipId` in every mode. In the hover and click modes the
 * control the hint describes can point its `aria-describedby` at it: that reads the text even while
 * it is hidden. In dialog mode it should not, since the text is too long to be read out as a
 * description.
 *
 * The dialog stops its own close, cancel and click events, so a dialog it sits in stays open and
 * the control it sits in never sees a click meant for the dialog.
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

  /** `hover`: an interest-triggered tooltip. `click`: a popup the button toggles. `dialog`: a modal dialog. */
  @Input() trigger: 'hover' | 'click' | 'dialog' = 'hover';

  /** The dialog's title in dialog mode; the subject when empty. */
  @Input() dialogTitle = '';

  @ViewChild('infoDialog') infoDialog?: ElementRef<HTMLDialogElement>;
  @ViewChild('infoDialogTitle') infoDialogTitle?: ElementRef<HTMLElement>;

  /** Whether the popup or the dialog is open; the popover polyfill does not set `aria-expanded`. */
  open = false;

  ngOnInit(): void {
    ensureOverlayPolyfills();
  }

  /** The dialog's title. */
  get title(): string {
    return this.dialogTitle || this.subject;
  }

  onToggle(event: Event): void {
    this.open = (event as ToggleEvent).newState === 'open';
    if (this.open) {
      refreshAnchorPositioning();
    }
    this.cdr.markForCheck();
  }

  /** Shows the dialog modally and focuses its title, so the close button's tooltip stays shut. */
  openDialog(): void {
    const dialog = this.infoDialog?.nativeElement;
    if (!dialog) {
      return;
    }
    if (!dialog.open) {
      dialog.showModal();
    }
    this.open = true;
    this.infoDialogTitle?.nativeElement.focus();
    this.cdr.markForCheck();
  }

  closeDialog(): void {
    this.infoDialog?.nativeElement.close();
  }

  /** The dialog's close and cancel events stop here, short of any dialog around it. */
  onDialogEvent(event: Event): void {
    event.stopPropagation();
    if (event.type === 'close') {
      this.open = false;
      this.cdr.markForCheck();
    }
  }

  /**
   * A click in the dialog stops here, short of the handlers of the control it sits in (a sortable
   * table header, a row). Light dismiss where `closedby` is unsupported: a backdrop click reports
   * the dialog itself as the target, so a hit outside its border box closes it.
   */
  onDialogClick(event: MouseEvent): void {
    event.stopPropagation();
    if ('closedBy' in HTMLDialogElement.prototype) {
      return;
    }
    const dialog = this.infoDialog?.nativeElement;
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
}
