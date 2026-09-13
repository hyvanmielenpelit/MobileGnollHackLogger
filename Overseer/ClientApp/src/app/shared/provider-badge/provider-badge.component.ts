import { ChangeDetectionStrategy, Component, Input } from '@angular/core';

@Component({
  selector: 'app-provider-badge',
  standalone: true,
  templateUrl: './provider-badge.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {
    'class': 'provider-badge',
    '[class.provider-badge--openai]': "providerKey === 'openai'",
    '[class.provider-badge--anthropic]': "providerKey === 'anthropic'",
    '[class.provider-badge--google]': "providerKey === 'google'"
  }
})
export class ProviderBadgeComponent {
  /** The provider as the API names it: 'OpenAI', 'Anthropic', 'Google', or anything else. */
  @Input() provider: string | null | undefined;

  /** Lower-cased and trimmed, so 'openai' and 'OpenAI' colour alike. */
  get providerKey(): string { return (this.provider ?? '').trim().toLowerCase(); }
}
