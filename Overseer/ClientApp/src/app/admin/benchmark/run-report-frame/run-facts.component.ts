import { ChangeDetectionStrategy, Component, Input } from '@angular/core';

import { InfoTipComponent } from '../../../shared/info-tip/info-tip.component';
import { ProviderBadgeComponent } from '../../../shared/provider-badge/provider-badge.component';
import { RunFactBadge, RunFactModel, RunFactRow, runFactBadges } from './run-facts';

/**
 * The run's settings as a definition list: Model, Assessor(s), Prompt, Scoring profile, Started
 * and Board, each model with the badges the model pickers show. The label/value pairs flow and
 * wrap side by side; below 36 rem each takes a line, its label stacked above its value. With
 * `layout="stacked"` each label sits above its value, the facts still flowing side by side. The
 * Board note is a click info tip, `#rr-board-note-tip`.
 */
@Component({
  selector: 'app-run-facts',
  standalone: true,
  imports: [InfoTipComponent, ProviderBadgeComponent],
  templateUrl: './run-facts.component.html',
  styleUrls: ['./run-facts.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {
    '[class.rr-facts-stacked]': "layout === 'stacked'"
  }
})
export class RunFactsComponent {
  @Input({ required: true }) rows: readonly RunFactRow[] = [];
  @Input() layout: 'inline' | 'stacked' = 'inline';

  badgesOf(model: RunFactModel): RunFactBadge[] {
    return runFactBadges(model);
  }
}
