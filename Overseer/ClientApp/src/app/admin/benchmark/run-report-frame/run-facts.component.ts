import { ChangeDetectionStrategy, Component, Input } from '@angular/core';

import { ProviderBadgeComponent } from '../../../shared/provider-badge/provider-badge.component';
import { RunFactBadge, RunFactModel, RunFactRow, runFactBadges } from './run-facts';

/**
 * The run's settings as a definition list: Model, Assessor(s), Prompt, Scoring profile, Started
 * and Board, each model with the badges the model pickers show. Two pairs per line on a wide
 * header, one below 64 rem, labels stacked above values below 36 rem.
 */
@Component({
  selector: 'app-run-facts',
  standalone: true,
  imports: [ProviderBadgeComponent],
  templateUrl: './run-facts.component.html',
  styleUrls: ['./run-facts.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class RunFactsComponent {
  @Input({ required: true }) rows: readonly RunFactRow[] = [];

  badgesOf(model: RunFactModel): RunFactBadge[] {
    return runFactBadges(model);
  }
}
