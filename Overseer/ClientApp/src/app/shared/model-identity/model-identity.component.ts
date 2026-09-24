import { ChangeDetectionStrategy, Component, Input } from '@angular/core';
import { ProviderBadgeComponent } from '../provider-badge/provider-badge.component';
import { showReasoningBadge } from '../../utils/model-badge-format.util';

/**
 * One model identity on one line: the name, then its provider, thinking level and reasoning mode
 * badges, wrapping as a unit when the column is narrow. The badge classes are global
 * (`styles.scss`); this component owns only the layout.
 */
@Component({
  selector: 'app-model-identity',
  standalone: true,
  imports: [ProviderBadgeComponent],
  templateUrl: './model-identity.component.html',
  styleUrl: './model-identity.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class ModelIdentityComponent {
  @Input() name = '';
  @Input() provider: string | null | undefined;
  /** Unset means no badge, not a `DEFAULT` one. */
  @Input() thinkingLevel: string | null | undefined;
  /** `default` and `standard` get no badge; see `showReasoningBadge`. */
  @Input() reasoningMode: string | null | undefined;

  get hasProvider(): boolean { return !!this.provider?.trim(); }

  get hasThinkingLevel(): boolean { return !!this.thinkingLevel?.trim(); }

  get hasReasoningMode(): boolean { return showReasoningBadge(this.reasoningMode?.trim()); }
}
