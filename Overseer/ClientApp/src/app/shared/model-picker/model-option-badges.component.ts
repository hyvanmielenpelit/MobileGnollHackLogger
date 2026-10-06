import { ChangeDetectionStrategy, Component, Input } from '@angular/core';

import { ProviderBadgeComponent } from '../provider-badge/provider-badge.component';
import { formatPickerPrice, formatThinkingLevel, showReasoningBadge } from '../../utils/model-badge-format.util';
import type { ModelPickerModel } from './model-picker.component';

/**
 * The badge row after a model's name in `app-model-picker` and `app-model-multi-picker`: thinking
 * level, reasoning mode and provider always, price and parallel execution on request. The host is
 * `display: contents`, so the badges are flex items of the option or trigger around it.
 */
@Component({
  selector: 'app-model-option-badges',
  standalone: true,
  imports: [ProviderBadgeComponent],
  templateUrl: './model-option-badges.component.html',
  styleUrl: './model-option-badges.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {
    '[class.narrow-hides-badges]': 'narrowHidesBadges'
  }
})
export class ModelOptionBadgesComponent {
  @Input({ required: true }) model!: ModelPickerModel;
  @Input() showPrice = false;
  @Input() showParallel = false;
  /** Hides the provider and parallel badges below 992 px. */
  @Input() narrowHidesBadges = false;

  formatThinkingLevel(level: string | null | undefined): string { return formatThinkingLevel(level); }

  showReasoningBadge(mode: string | null | undefined): boolean { return showReasoningBadge(mode); }

  formatPrice(model: ModelPickerModel): string { return formatPickerPrice(model); }
}
