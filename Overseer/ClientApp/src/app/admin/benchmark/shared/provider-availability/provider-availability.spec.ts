import type { ProviderAvailabilityCardFields } from '../../../../services/admin-benchmark.service';
import {
  answerFailureText,
  cardHasProviderTrouble,
  cardRateText,
  cardSentence,
  formatCount,
  formatInteger,
  formatInterval,
  formatOneIn,
  formatPercent,
  formatRate,
  formatReruns,
  outcomeLabel,
  wilson95
} from './provider-availability';

/**
 * The formatting parity fixture, copied verbatim from `BenchmarkProviderAvailabilityTests.ParityTable`
 * on the server. The interval inputs are the integer Wilson interval of (k, n). Change both together.
 */
const PARITY_TABLE: readonly [number, number, string, string, string, string | null, string][] = [
  [0, 1480, '0%', '0 of 1,480 attempts', '95% CI 0–0.26%', null, '0% (0 of 1,480 attempts; 95% CI 0–0.26%)'],
  [12, 1480, '0.81%', '12 of 1,480 attempts', '95% CI 0.46–1.4%', 'about 1 in 123', '0.81% (12 of 1,480 attempts; 95% CI 0.46–1.4%)'],
  [1, 37, '2.7%', '1 of 37 attempts', '95% CI 0.48–14%', null, '2.7% (1 of 37 attempts; 95% CI 0.48–14%; few attempts)'],
  [150, 1200, '13%', '150 of 1,200 attempts', '95% CI 11–14%', null, '13% (150 of 1,200 attempts; 95% CI 11–14%)'],
  [3, 99, '3.0%', '3 of 99 attempts', '95% CI 1.0–8.5%', null, '3.0% (3 of 99 attempts; 95% CI 1.0–8.5%; few attempts)'],
  [1, 1480, '0.068%', '1 of 1,480 attempts', '95% CI 0.012–0.38%', 'about 1 in 1,480', '0.068% (1 of 1,480 attempts; 95% CI 0.012–0.38%)'],
  [1, 1, '100%', '1 of 1 attempt', '95% CI 21–100%', null, '100% (1 of 1 attempt; 95% CI 21–100%; few attempts)']
];

describe('provider availability formatting', () => {
  it.each(PARITY_TABLE)('formats %i of %i exactly as the server does', (k, n, rate, count, interval, oneIn, headline) => {
    const ci = wilson95(k, n)!;
    expect(formatPercent(k / n)).toBe(rate);
    expect(formatCount(k, n)).toBe(count);
    expect(formatInterval(ci.low, ci.high)).toBe(interval);
    expect(formatOneIn(k, n)).toBe(oneIn);
    expect(formatRate(k, n, ci.low, ci.high)).toBe(headline);
  });

  it('keeps two significant digits and rounds half away from zero', () => {
    expect(formatPercent(0)).toBe('0%');
    expect(formatPercent(0.125)).toBe('13%');
    expect(formatPercent(0.0999)).toBe('10%');
    expect(formatPercent(0.00996)).toBe('1.0%');
    expect(formatPercent(0.000042)).toBe('0.0042%');
    expect(formatPercent(2)).toBe('100%');
  });

  it('groups thousands independently of the locale', () => {
    expect(formatInteger(0)).toBe('0');
    expect(formatInteger(999)).toBe('999');
    expect(formatInteger(1480)).toBe('1,480');
    expect(formatInteger(1234567)).toBe('1,234,567');
  });

  it('has no interval without both bounds and no rate without attempts', () => {
    expect(formatInterval(null, 0.1)).toBe('');
    expect(formatRate(0, 0, null, null)).toBe('no valid attempts');
    expect(formatRate(1, 200, null, null)).toBe('0.50% (1 of 200 attempts)');
  });

  it('words re-runs with their provider part', () => {
    expect(formatReruns(0, 0)).toBe('0 re-runs');
    expect(formatReruns(1, 0)).toBe('1 re-run');
    expect(formatReruns(3, 2)).toBe('3 re-runs (2 for provider errors)');
  });
});

describe('answer failure text', () => {
  it('names the side, the class and the status', () => {
    expect(answerFailureText('Provider', 'ProviderOverloaded', 529)).toBe('Provider error: overloaded (HTTP 529)');
    expect(answerFailureText('Unattributed', 'RateLimitUnattributed', 429)).toBe('Unattributed error: rate limited (HTTP 429)');
    expect(answerFailureText('Ours', 'question_budget', null)).toBe('Failed: our side (question time budget)');
    expect(answerFailureText('Ours', 'OurKey', 401)).toBe('Failed: our side (API key rejected)');
    expect(answerFailureText('Ours', null, null)).toBe('Failed: our side');
  });

  it('is null for an answer that did not fail or was not recorded', () => {
    expect(answerFailureText('None', null, null)).toBeNull();
    expect(answerFailureText(null, null, 503)).toBeNull();
  });
});

describe('outcome labels', () => {
  it('labels every known class', () => {
    expect(outcomeLabel('ProviderOverloaded')).toBe('Overloaded');
    expect(outcomeLabel('OurCanceled')).toBe('Canceled or question time budget');
  });

  it('reads an unknown class in words', () => {
    expect(outcomeLabel('ProviderQuotaExceeded')).toBe('Provider quota exceeded');
  });

  it('never reads an inherited property as a label', () => {
    expect(outcomeLabel('constructor')).toBe('Constructor');
    expect(outcomeLabel('__proto__')).toBe('Proto');
    expect(outcomeLabel('toString')).toBe('To string');
  });
});

describe('card figures', () => {
  const card = (overrides: Partial<ProviderAvailabilityCardFields>): ProviderAvailabilityCardFields => ({
    providerAvailabilityRecorded: true,
    providerValidAttempts: 1480,
    providerErrorCount: 12,
    providerUnattributedErrorCount: 3,
    providerErrorRate: 12 / 1480,
    rerunsNeeded: 2,
    providerRerunsNeeded: 1,
    ...overrides
  });

  it('shows trouble only when there are provider errors or re-runs', () => {
    expect(cardHasProviderTrouble(card({}))).toBe(true);
    expect(cardHasProviderTrouble(card({ providerErrorCount: 0, rerunsNeeded: 0 }))).toBe(false);
    expect(cardHasProviderTrouble(card({ providerErrorCount: 0, rerunsNeeded: 1 }))).toBe(true);
    expect(cardHasProviderTrouble(card({ providerAvailabilityRecorded: false }))).toBe(false);
  });

  it('writes the rate with its count and the full sentence', () => {
    expect(cardRateText(card({}))).toBe('0.81% (12/1,480)');
    expect(cardSentence(card({}))).toBe('12 of 1,480 attempts; 3 unattributed; 2 re-runs, 1 for provider errors');
    expect(cardSentence(card({ providerAvailabilityRecorded: false }))).toBe('Not recorded');
    expect(cardRateText(card({ providerAvailabilityRecorded: false }))).toBeNull();
  });
});
