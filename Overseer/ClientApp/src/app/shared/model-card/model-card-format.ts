import { showReasoningBadge } from '../../utils/model-badge-format.util';

/** The fields a model card reads; `SystemAiConfigDto` and `UserAiModel` both satisfy it. */
export interface ModelCardModel {
  thinkingLevel?: string | null;
  reasoningMode?: string | null;
  reasoningSummary?: string | null;
  serviceTier?: string | null;
  maxInputTokens?: number | null;
  maxOutputTokens?: number | null;
  pricingMode?: string | null;
  pricingSource?: string | null;
  inputPricePerMillion?: number | null;
  outputPricePerMillion?: number | null;
  cachedInputPricePerMillion?: number | null;
  effectiveInputPricePerMillion?: number | null;
  effectiveOutputPricePerMillion?: number | null;
  effectiveCachedInputPricePerMillion?: number | null;
  effectiveLongContextThresholdTokens?: number | null;
  effectiveLongContextInputPricePerMillion?: number | null;
  effectiveLongContextOutputPricePerMillion?: number | null;
  pricingScheduledChangeFrom?: string | null;
  pricingScheduledChangeInputPricePerMillion?: number | null;
  pricingScheduledChangeOutputPricePerMillion?: number | null;
  pricingScheduledChangeNote?: string | null;
  pricingScheduleElapsed?: boolean | null;
}

/** A requested reasoning summary, title-cased; empty when none is set. */
export function formatReasoningSummary(level: string | null | undefined): string {
  if (!level) return '';
  return level.charAt(0).toUpperCase() + level.slice(1);
}

/** Whether the card's *Model settings* row has a badge to show. */
export function hasSettingsBadges(model: ModelCardModel): boolean {
  return !!model.thinkingLevel || showReasoningBadge(model.reasoningMode) || !!model.reasoningSummary || !!model.serviceTier;
}

function formatRate(value: number): string {
  const formatted = new Intl.NumberFormat('en-US', {
    minimumFractionDigits: 2,
    maximumFractionDigits: 6
  }).format(value);
  return `$${formatted}`;
}

/**
 * The price per 1M tokens, as `$4.00 in · $20.00 out · $0.20 cached`: the effective price, else the
 * custom price of a custom-priced model. Empty when the input or output price is unknown.
 */
export function formatCardPrice(model: ModelCardModel): string {
  const custom = model.pricingMode === 'custom';
  const input = model.effectiveInputPricePerMillion ?? (custom ? model.inputPricePerMillion : null);
  const output = model.effectiveOutputPricePerMillion ?? (custom ? model.outputPricePerMillion : null);
  const cached = model.effectiveCachedInputPricePerMillion ?? (custom ? model.cachedInputPricePerMillion : null);

  if (input == null || output == null) {
    return '';
  }

  const parts = [`${formatRate(input)} in`, `${formatRate(output)} out`];
  if (cached != null) {
    parts.push(`${formatRate(cached)} cached`);
  }
  return parts.join(' · ');
}

/**
 * The model's long-prompt rate card, shown beside the base price. Empty for a flat-rate model — which
 * is every Anthropic model except Claude Haiku 5.5, every Gemini Flash model, and every custom price
 * override.
 */
export function formatLongContextPrice(model: ModelCardModel): string {
  const threshold = model.effectiveLongContextThresholdTokens;
  const input = model.effectiveLongContextInputPricePerMillion;
  const output = model.effectiveLongContextOutputPricePerMillion;
  if (threshold == null || input == null || output == null) {
    return '';
  }
  const tokens = new Intl.NumberFormat('en-US').format(threshold);
  return `Prompts over ${tokens} tokens: ${formatRate(input)} in / ${formatRate(output)} out per 1M`;
}

/**
 * A quiet note about an announced future price change, or — once its date has passed — the advisory
 * that the base rates already carry it. The cost is correct either way; the advisory exists so the
 * catalog does not silently turn into a changelog of elapsed schedules.
 */
export function formatPricingSchedule(model: ModelCardModel): string {
  if (model.pricingScheduleElapsed) {
    return 'A scheduled price change is in effect — fold it into the base rates and re-verify.';
  }
  if (!model.pricingScheduledChangeFrom) {
    return '';
  }
  const input = model.pricingScheduledChangeInputPricePerMillion;
  const output = model.pricingScheduledChangeOutputPricePerMillion;
  const change = (input != null && output != null)
    ? `Price changes to ${formatRate(input)} in / ${formatRate(output)} out per 1M on ${model.pricingScheduledChangeFrom}.`
    : `Price changes on ${model.pricingScheduledChangeFrom}.`;
  return model.pricingScheduledChangeNote ? `${change} ${model.pricingScheduledChangeNote}` : change;
}

/** Where the price comes from: the model's own custom price, or the model catalog. */
export function pricingBadge(model: ModelCardModel): 'Custom' | 'Catalog' {
  return (model.pricingSource === 'custom' || model.pricingMode === 'custom') ? 'Custom' : 'Catalog';
}

/** Whether the card's facts strip has anything to show. */
export function hasCardFacts(model: ModelCardModel): boolean {
  return !!model.maxInputTokens
    || !!model.maxOutputTokens
    || !!formatCardPrice(model)
    || !!formatLongContextPrice(model)
    || !!formatPricingSchedule(model);
}
