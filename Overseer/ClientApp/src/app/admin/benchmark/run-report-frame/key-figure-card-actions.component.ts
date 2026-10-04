import { ChangeDetectionStrategy, Component, ElementRef, EventEmitter, Input, OnInit, Output, inject } from '@angular/core';

import { ensureOverlayPolyfills } from '../../../utils/polyfills.util';
import { KeyFiguresAction, keyFigureSlug } from './key-figures-image';

/** One card's export request: what to do, and the `.score-card` to draw. */
export interface KeyFigureCardExportRequest {
  readonly action: KeyFiguresAction;
  readonly card: HTMLElement;
}

/**
 * Copy and Download for one key-figure card, over its top right corner. The last child of a
 * `.score-card`; the host page reveals it while the card is hovered or holds focus, and always where
 * there is no hover. Opacity only, so both buttons stay Tab stops.
 */
@Component({
  selector: 'app-key-figure-card-actions',
  standalone: true,
  templateUrl: './key-figure-card-actions.component.html',
  styleUrls: ['./key-figure-card-actions.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class KeyFigureCardActionsComponent implements OnInit {
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);

  @Input({ required: true }) runId = 0;

  /** The card's visible label, for the accessible names and the anchor names. */
  @Input({ required: true }) cardLabel = '';

  /** While an export runs: both buttons are `aria-disabled` and refuse the click. */
  @Input() busy = false;

  /** What the card belongs to in the accessible names, as in `… of battery run 7`; null reads `run {runId}`. */
  @Input() subjectLabel: string | null = null;

  /** The subject part of the tooltip ids, slugged, as in `battery7`; null reads `run{runId}`. */
  @Input() subjectKey: string | null = null;

  /** The download's format as its name and tooltip print it, `PNG` or `WebP`; Copy always writes PNG. */
  @Input() formatLabel = 'PNG';

  @Output() readonly exportRequested = new EventEmitter<KeyFigureCardExportRequest>();

  ngOnInit(): void {
    ensureOverlayPolyfills();
  }

  /** `run 72`, or the host's {@link subjectLabel}. */
  get subject(): string {
    return this.subjectLabel ?? `run ${this.runId}`;
  }

  /** `kfc-copy-run72-intelligence-index`: the tooltip's id and, prefixed with `--`, the anchor name. */
  tipId(action: KeyFiguresAction): string {
    const subject = this.subjectKey != null ? keyFigureSlug(this.subjectKey) : `run${this.runId}`;
    return `kfc-${action}-${subject}-${keyFigureSlug(this.cardLabel)}`;
  }

  request(action: KeyFiguresAction): void {
    if (this.busy) {
      return;
    }
    const card = this.host.nativeElement.closest<HTMLElement>('.score-card');
    if (card) {
      this.exportRequested.emit({ action, card });
    }
  }
}
