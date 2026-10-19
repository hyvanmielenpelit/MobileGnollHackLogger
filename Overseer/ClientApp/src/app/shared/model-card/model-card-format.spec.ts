import {
  formatCardPrice, formatLongContextPrice, formatPricingSchedule, formatReasoningSummary, hasCardFacts, hasSettingsBadges,
  pricingBadge
} from './model-card-format';

describe('model card formatters', () => {
  it('formats the effective price with and without a cached price', () => {
    expect(formatCardPrice({
      effectiveInputPricePerMillion: 4, effectiveOutputPricePerMillion: 20, effectiveCachedInputPricePerMillion: 0.2
    })).toBe('$4.00 in · $20.00 out · $0.20 cached');
    expect(formatCardPrice({ effectiveInputPricePerMillion: 0.075, effectiveOutputPricePerMillion: 0.3 }))
      .toBe('$0.075 in · $0.30 out');
  });

  it('falls back to the custom price only under the custom pricing mode', () => {
    const custom = { inputPricePerMillion: 1, outputPricePerMillion: 2, cachedInputPricePerMillion: 0.5 };
    expect(formatCardPrice({ ...custom, pricingMode: 'custom' })).toBe('$1.00 in · $2.00 out · $0.50 cached');
    expect(formatCardPrice({ ...custom, pricingMode: 'default' })).toBe('');
  });

  it('is empty when the output price is unknown', () => {
    expect(formatCardPrice({ effectiveInputPricePerMillion: 4, effectiveOutputPricePerMillion: null })).toBe('');
  });

  it('title-cases a reasoning summary and leaves an unset one empty', () => {
    expect(formatReasoningSummary(null)).toBe('');
    expect(formatReasoningSummary('auto')).toBe('Auto');
  });

  it('shows the settings row for a service tier alone, but not for a default reasoning mode', () => {
    expect(hasSettingsBadges({ reasoningMode: 'default' })).toBe(false);
    expect(hasSettingsBadges({ reasoningMode: 'standard' })).toBe(false);
    expect(hasSettingsBadges({ serviceTier: 'priority' })).toBe(true);
    expect(hasSettingsBadges({ reasoningMode: 'extended' })).toBe(true);
  });

  it('names a custom price Custom and any other Catalog', () => {
    expect(pricingBadge({ pricingSource: 'custom' })).toBe('Custom');
    expect(pricingBadge({ pricingMode: 'custom' })).toBe('Custom');
    expect(pricingBadge({ pricingSource: 'catalog', pricingMode: 'default' })).toBe('Catalog');
    expect(pricingBadge({})).toBe('Catalog');
  });

  it('keeps the long-context and schedule notes', () => {
    expect(formatLongContextPrice({
      effectiveLongContextThresholdTokens: 272000,
      effectiveLongContextInputPricePerMillion: 8,
      effectiveLongContextOutputPricePerMillion: 30
    })).toBe('Prompts over 272,000 tokens: $8.00 in / $30.00 out per 1M');
    expect(formatPricingSchedule({
      pricingScheduledChangeFrom: '2026-11-01',
      pricingScheduledChangeInputPricePerMillion: 3,
      pricingScheduledChangeOutputPricePerMillion: 15
    })).toBe('Price changes to $3.00 in / $15.00 out per 1M on 2026-11-01.');
  });

  it('has no facts for an empty model, and has them for a max input alone', () => {
    expect(hasCardFacts({})).toBe(false);
    expect(hasCardFacts({ maxInputTokens: 1000 })).toBe(true);
  });
});
