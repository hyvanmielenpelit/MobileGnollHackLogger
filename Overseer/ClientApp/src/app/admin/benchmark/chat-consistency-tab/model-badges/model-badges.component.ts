import { ChangeDetectionStrategy, Component, Input } from '@angular/core';

import { ProviderBadgeComponent } from '../../../../shared/provider-badge/provider-badge.component';
import { formatServiceTier, formatThinkingLevel } from '../../../../utils/model-badge-format.util';

/** What the badges read of a model axis or an analysis subject. */
export interface CcBadgedModel {
  provider: string;
  thinkingLevel: string | null;
  serviceTier: string | null;
}

/**
 * A Chat Consistency model's badges: its thinking level, its provider and, when set, its service tier,
 * each with a visually hidden name. The host takes no box, so the badges are items of the parent's
 * flex row.
 */
@Component({
  selector: 'app-cc-model-badges',
  standalone: true,
  imports: [ProviderBadgeComponent],
  templateUrl: './model-badges.component.html',
  styles: [':host { display: contents; }'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class CcModelBadgesComponent {
  @Input({ required: true }) model!: CcBadgedModel;

  get thinkingText(): string {
    return formatThinkingLevel(this.model.thinkingLevel);
  }

  get tierText(): string {
    return formatServiceTier(this.model.serviceTier);
  }
}
