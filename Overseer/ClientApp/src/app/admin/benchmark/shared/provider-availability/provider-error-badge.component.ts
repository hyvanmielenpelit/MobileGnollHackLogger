import { ChangeDetectionStrategy, Component, OnInit, computed, input } from '@angular/core';

import { ensureOverlayPolyfills } from '../../../../utils/polyfills.util';
import { cardSentence, formatInteger, formatPercent } from './provider-availability';

let nextBadgeId = 0;

/**
 * A card's outlined provider-error badge: `Provider errors 0.81 %` and/or `2 re-runs`, with the full
 * sentence in a tooltip (`12 of 1,480 attempts; 3 unattributed; 2 re-runs, 1 for provider errors`).
 * It renders nothing unless the figures are recorded and show provider errors or re-runs, so a card
 * says nothing while all is well. Distinct from `app-provider-badge`, the provider's name.
 */
@Component({
  selector: 'app-provider-error-badge',
  standalone: true,
  templateUrl: './provider-error-badge.component.html',
  styleUrls: ['./provider-error-badge.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class ProviderErrorBadgeComponent implements OnInit {
  readonly valid = input<number | null | undefined>(null);
  readonly providerErrors = input<number | null | undefined>(null);
  readonly unattributed = input<number | null | undefined>(null);
  readonly rerunsNeeded = input<number | null | undefined>(null);
  readonly providerReruns = input<number | null | undefined>(null);
  readonly recorded = input<boolean | null | undefined>(null);

  /** The tooltip's id and anchor name, unique per badge: the inputs carry no id. */
  readonly tipId = `peb-tip-${++nextBadgeId}`;

  /** `Provider errors 0.81 %`, or null without provider errors. */
  readonly errorsText = computed(() => {
    const k = this.providerErrors() ?? 0;
    if (!this.recorded() || k <= 0) {
      return null;
    }
    const n = this.valid() ?? 0;
    return n > 0 ? `Provider errors ${formatPercent(k / n)}` : `Provider errors ${formatInteger(k)}`;
  });

  /** `2 re-runs`, or null without re-runs. */
  readonly rerunsText = computed(() => {
    const r = this.rerunsNeeded() ?? 0;
    if (!this.recorded() || r <= 0) {
      return null;
    }
    return `${formatInteger(r)} ${r === 1 ? 're-run' : 're-runs'}`;
  });

  readonly shown = computed(() => this.errorsText() !== null || this.rerunsText() !== null);

  readonly sentence = computed(() => cardSentence({
    providerAvailabilityRecorded: this.recorded(),
    providerValidAttempts: this.valid(),
    providerErrorCount: this.providerErrors(),
    providerUnattributedErrorCount: this.unattributed(),
    rerunsNeeded: this.rerunsNeeded(),
    providerRerunsNeeded: this.providerReruns()
  }));

  ngOnInit(): void {
    ensureOverlayPolyfills();
  }
}
