import { ChangeDetectionStrategy, Component, Input } from '@angular/core';

import { formatServiceTier, formatThinkingLevel, showReasoningBadge } from '../../utils/model-badge-format.util';
import { ModelCardModel, formatReasoningSummary, hasSettingsBadges } from './model-card-format';

/**
 * A model card's *Model settings* badge row: thinking level and reasoning mode as the pickers draw
 * them, then the reasoning summary and service tier where set. The host is `display: contents`, so
 * the list is the card's grid item, and a model with nothing to show renders nothing.
 */
@Component({
  selector: 'app-model-card-settings',
  standalone: true,
  templateUrl: './model-card-settings.component.html',
  styleUrl: './model-card-settings.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class ModelCardSettingsComponent {
  @Input({ required: true }) model!: ModelCardModel;

  readonly hasSettingsBadges = hasSettingsBadges;
  readonly formatThinkingLevel = formatThinkingLevel;
  readonly showReasoningBadge = showReasoningBadge;
  readonly formatReasoningSummary = formatReasoningSummary;
  readonly formatServiceTier = formatServiceTier;
}
