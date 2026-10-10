import { ChangeDetectionStrategy, Component, Input, OnChanges, SimpleChanges } from '@angular/core';

import { ModelAvailability, availabilitySentence, needsAttention } from './model-availability';

/**
 * The notice for a model that needs attention: an amber `alert-warning` for a retired model, a
 * blue `alert-info` for one the catalog does not describe, and nothing otherwise. The host's
 * buttons are projected into `.alert-actions`.
 *
 * The host is a live region only for a notice that appears after the first render: when the
 * availability turns to needing attention after the first change, or when the host creates the
 * notice in response to a user action and sets `announce`. A page that loads with several flagged
 * rows therefore announces none of them.
 */
@Component({
  selector: 'app-model-availability-notice',
  standalone: true,
  templateUrl: './model-availability-notice.component.html',
  styleUrl: './model-availability-notice.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {
    '[attr.role]': 'liveRole',
    '[attr.data-variant]': 'variant'
  }
})
export class ModelAvailabilityNoticeComponent implements OnChanges {
  @Input() availability: ModelAvailability | null | undefined = null;
  @Input() modelName = '';
  /** Named by the not-in-catalog sentence; falls back to `modelName`. */
  @Input() modelId: string | null | undefined = null;
  @Input() variant: 'row' | 'composer' = 'row';
  @Input() live: 'status' | 'alert' = 'status';
  /** Renders a heading of this level over the sentence; none by default. */
  @Input() headingLevel: 2 | 3 | 4 | 5 | 6 | null = null;
  /** A sentence put before the availability sentence. */
  @Input() leadSentence: string | null = null;
  /** Text put after the availability sentence. */
  @Input() extraText: string | null = null;
  /** Makes the host a live region from its first render, for a notice created by a user action. */
  @Input() announce = false;

  private armed = false;

  get shown(): boolean { return needsAttention(this.availability); }

  get tone(): 'warning' | 'info' { return this.availability?.status === 'retired' ? 'warning' : 'info'; }

  get liveRole(): 'status' | 'alert' | null { return this.armed && this.shown ? this.live : null; }

  get heading(): string {
    return this.tone === 'warning' ? 'Model removed from the catalog' : 'Model not in the catalog';
  }

  get text(): string {
    return [this.leadSentence, availabilitySentence(this.availability, this.modelName, this.modelId), this.extraText]
      .map(part => part?.trim())
      .filter(Boolean)
      .join(' ');
  }

  get replacementName(): string | null {
    return this.availability?.replacement?.displayName || this.availability?.replacement?.modelId || null;
  }

  ngOnChanges(changes: SimpleChanges): void {
    if (this.announce) {
      this.armed = true;
    }
    const change = changes['availability'];
    if (change && !change.firstChange && !needsAttention(change.previousValue) && needsAttention(change.currentValue)) {
      this.armed = true;
    }
  }
}
