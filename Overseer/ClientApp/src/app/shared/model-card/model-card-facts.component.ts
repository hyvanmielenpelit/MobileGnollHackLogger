import { ChangeDetectionStrategy, Component, Input } from '@angular/core';
import { DecimalPipe } from '@angular/common';

import {
  ModelCardModel, formatCardPrice, formatLongContextPrice, formatPricingSchedule, hasCardFacts, pricingBadge
} from './model-card-format';

/**
 * A model card's facts strip under its hairline: max input, max output and the price per 1M tokens
 * with its Catalog or Custom badge, then the long-prompt and scheduled-price notes. The host is
 * `display: contents`, so the area is the card's grid item, and a model with no facts renders nothing.
 */
@Component({
  selector: 'app-model-card-facts',
  standalone: true,
  imports: [DecimalPipe],
  templateUrl: './model-card-facts.component.html',
  styleUrl: './model-card-facts.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class ModelCardFactsComponent {
  @Input({ required: true }) model!: ModelCardModel;

  readonly hasCardFacts = hasCardFacts;
  readonly formatCardPrice = formatCardPrice;
  readonly formatLongContextPrice = formatLongContextPrice;
  readonly formatPricingSchedule = formatPricingSchedule;
  readonly pricingBadge = pricingBadge;
}
