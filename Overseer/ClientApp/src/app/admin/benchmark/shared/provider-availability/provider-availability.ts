/**
 * GnollBench provider availability: the formatting every surface shows, and the class and origin
 * labels. Pure TypeScript, no Angular.
 *
 * The formatting mirrors `BenchmarkProviderAvailability` on the server byte for byte (invariant
 * culture, "," thousands separator, no space before "%", an en dash in intervals), so a
 * document and the dialog beside it never disagree. `provider-availability.spec.ts` holds the parity
 * table copied verbatim from `BenchmarkProviderAvailabilityTests`. The method is in
 * docs/overseer/ai-benchmark-provider-availability.md.
 */

import type {
  ProviderAttemptSide,
  ProviderAvailabilityCardFields,
  ProviderAvailabilityDto
} from '../../../../services/admin-benchmark.service';

/** Below this many valid attempts a figure carries the tag "few attempts". */
export const FEW_ATTEMPTS_THRESHOLD = 100;

/** The two-sided 95% normal quantile, as the server's `BenchmarkProportionInterval.Z95`. */
export const Z95 = 1.959964;

/** Shown wherever a run does not record the ledger (before harness 56). */
export const NOT_RECORDED_TEXT = 'Not recorded';

/**
 * The number part of a percentage with 2 significant digits, without the "%": the proportion is
 * clamped to [0, 1] and v = proportion × 100; 0 gives "0"; the decimals d are 0 when v ≥ 10, else the
 * smallest d ≥ 1 with v × 10^d ≥ 10 (10^d by repeated multiplication; beyond 15 decimals "0");
 * r = floor(v × 10^d + 0.5); when d > 0 and r reaches 100, d drops by one and r is rounded again;
 * r is written with the point d places from the right, zero-padded, trailing zeros kept.
 */
export function formatPercentNumber(proportion: number): string {
  if (Number.isNaN(proportion)) {
    throw new RangeError('The proportion must be a number.');
  }

  const v = Math.min(1, Math.max(0, proportion)) * 100;
  if (v === 0) {
    return '0';
  }

  const maxDecimals = 15;
  let d = 0;
  let scale = 1;
  if (v < 10) {
    do {
      d++;
      scale *= 10;
    } while (v * scale < 10 && d < maxDecimals);

    if (v * scale < 10) {
      return '0';
    }
  }

  let r = Math.floor(v * scale + 0.5);
  if (d > 0 && r >= 100) {
    d--;
    scale /= 10;
    r = Math.floor(v * scale + 0.5);
  }

  let digits = String(r);
  if (d === 0) {
    return digits;
  }

  digits = digits.padStart(d + 1, '0');
  return `${digits.slice(0, digits.length - d)}.${digits.slice(digits.length - d)}`;
}

/** A proportion as a percentage: `0.81%`, `3.0%`, `12%`, `0%`. */
export function formatPercent(proportion: number): string {
  return `${formatPercentNumber(proportion)}%`;
}

/** An integer with the "," thousands separator, independent of the browser's locale. */
export function formatInteger(value: number): string {
  const sign = value < 0 ? '-' : '';
  const digits = String(Math.trunc(Math.abs(value)));
  return sign + digits.replace(/\B(?=(\d{3})+(?!\d))/g, ',');
}

/** The count with its denominator: `12 of 1,480 attempts`; `1 of 1 attempt`. */
export function formatCount(k: number, n: number): string {
  return `${formatInteger(k)} of ${formatInteger(n)} ${n === 1 ? 'attempt' : 'attempts'}`;
}

/** The interval `95% CI 0.46–1.4%`; empty when either bound is missing. */
export function formatInterval(low: number | null | undefined, high: number | null | undefined): string {
  if (low == null || high == null) {
    return '';
  }
  return `95% CI ${formatPercentNumber(low)}–${formatPercentNumber(high)}%`;
}

/** `about 1 in 123` when 0 < k/n < 1% (k × 100 < n), N = floor(n / k + 0.5); null otherwise. */
export function formatOneIn(k: number, n: number): string | null {
  if (k <= 0 || n <= 0 || k * 100 >= n) {
    return null;
  }
  return `about 1 in ${formatInteger(Math.floor(n / k + 0.5))}`;
}

/**
 * The headline: `0.81% (12 of 1,480 attempts; 95% CI 0.46–1.4%)`, with `; few attempts` inside
 * the parentheses below 100 valid attempts; `no valid attempts` when n is 0.
 */
export function formatRate(k: number, n: number, low: number | null | undefined, high: number | null | undefined): string {
  if (n <= 0) {
    return 'no valid attempts';
  }
  const parts = [formatCount(k, n)];
  const interval = formatInterval(low, high);
  if (interval) {
    parts.push(interval);
  }
  if (n < FEW_ATTEMPTS_THRESHOLD) {
    parts.push('few attempts');
  }
  return `${formatPercent(k / n)} (${parts.join('; ')})`;
}

/**
 * The 95% Wilson score interval for k of n, clamped to [0, 1], as the server's integer
 * `BenchmarkProportionInterval.Wilson95`; null when n is 0.
 */
export function wilson95(k: number, n: number): { low: number; high: number } | null {
  if (n <= 0) {
    return null;
  }
  const p = k / n;
  const z2 = Z95 * Z95;
  const denominator = 1 + z2 / n;
  const center = (p + z2 / (2 * n)) / denominator;
  const halfWidth = (Z95 * Math.sqrt((p * (1 - p)) / n + z2 / (4 * n * n))) / denominator;
  return {
    low: k === 0 ? 0 : Math.max(0, center - halfWidth),
    high: k === n ? 1 : Math.min(1, center + halfWidth)
  };
}

/** `2 re-runs (1 for provider errors)`, `1 re-run`, or `0 re-runs`. */
export function formatReruns(reruns: number, providerReruns: number): string {
  const head = `${formatInteger(reruns)} ${reruns === 1 ? 're-run' : 're-runs'}`;
  return providerReruns > 0 ? `${head} (${formatInteger(providerReruns)} for provider errors)` : head;
}

/** Human labels for the outcome classes, by class name. */
export const OUTCOME_LABELS: Readonly<Record<string, string>> = {
  Ok: 'Succeeded',
  ProviderOverloaded: 'Overloaded',
  ProviderServerError: 'Server error',
  ProviderTimeout: 'Timeout',
  ProviderStreamFailure: 'Stream failure',
  RateLimitUnattributed: 'Rate limited, unattributed',
  TransportUnattributed: 'Connection failure, unattributed',
  OurRateLimit: 'Rate limited by our limit',
  OurBilling: 'Billing or credits',
  OurKey: 'API key rejected',
  OurRequest: 'Request rejected',
  OurThrottle: 'Overseer request governor',
  OurCanceled: 'Canceled or question time budget',
  OurNetwork: 'Our network'
};

/** The label of an outcome class name; the name itself when unknown. */
export function outcomeLabel(outcome: string): string {
  return OUTCOME_LABELS[outcome] ?? outcome;
}

/** Headings of the breakdown's sides. */
export const SIDE_LABELS: Readonly<Record<ProviderAttemptSide, string>> = {
  ok: 'Succeeded',
  provider: 'Provider',
  unattributed: 'Unattributed',
  ours: 'Excluded: our side'
};

/**
 * The kind of an answer's failure inside its line, where the origin already names the side: by class
 * name, plus `question_budget`.
 */
const FAILURE_KIND_TEXT: Readonly<Record<string, string>> = {
  ProviderOverloaded: 'overloaded',
  ProviderServerError: 'server error',
  ProviderTimeout: 'timeout',
  ProviderStreamFailure: 'stream failure',
  RateLimitUnattributed: 'rate limited',
  TransportUnattributed: 'connection failure',
  OurRateLimit: 'rate limited by our limit',
  OurBilling: 'billing or credits',
  OurKey: 'API key rejected',
  OurRequest: 'request rejected',
  OurThrottle: 'Overseer request governor',
  OurCanceled: 'canceled',
  OurNetwork: 'our network',
  question_budget: 'question time budget'
};

/**
 * The failure line of an answer from its origin and kind:
 * `Provider error: overloaded (HTTP 529)`, `Unattributed error: rate limited (HTTP 429)`,
 * `Failed: our side (question time budget)`. Null when the answer carries no recorded failure.
 */
export function answerFailureText(
  origin: string | null | undefined,
  kind: string | null | undefined,
  httpStatus: number | null | undefined
): string | null {
  if (!origin || origin === 'None') {
    return null;
  }
  const status = httpStatus ? ` (HTTP ${httpStatus})` : '';
  const what = kind ? FAILURE_KIND_TEXT[kind] ?? kind : '';
  switch (origin) {
    case 'Provider':
      return `Provider error${what ? `: ${what}` : ''}${status}`;
    case 'Unattributed':
      return `Unattributed error${what ? `: ${what}` : ''}${status}`;
    case 'Ours':
      return `Failed: our side${what ? ` (${what})` : ''}`;
    default:
      return null;
  }
}

/** The headline of a full availability record, or `Not recorded`. */
export function availabilityHeadline(a: ProviderAvailabilityDto | null | undefined): string {
  if (!a || !a.recorded) {
    return NOT_RECORDED_TEXT;
  }
  return formatRate(a.providerErrors, a.validAttempts, a.providerErrorRateLow, a.providerErrorRateHigh);
}

/** Whether a card's compact figures show anything worth a badge: provider errors or re-runs. */
export function cardHasProviderTrouble(card: ProviderAvailabilityCardFields | null | undefined): boolean {
  if (!card?.providerAvailabilityRecorded) {
    return false;
  }
  return (card.providerErrorCount ?? 0) > 0 || (card.rerunsNeeded ?? 0) > 0;
}

/** A card's one-line figure: `0.81% (12/1,480)`, or null when not recorded or without attempts. */
export function cardRateText(card: ProviderAvailabilityCardFields | null | undefined): string | null {
  if (!card?.providerAvailabilityRecorded || !card.providerValidAttempts) {
    return null;
  }
  const k = card.providerErrorCount ?? 0;
  return `${formatPercent(k / card.providerValidAttempts)} (${formatInteger(k)}/${formatInteger(card.providerValidAttempts)})`;
}

/**
 * A card's full sentence for a tooltip: `12 of 1,480 attempts; 3 unattributed; 2 re-runs, 1 for
 * provider errors`, or `Not recorded`.
 */
export function cardSentence(card: ProviderAvailabilityCardFields | null | undefined): string {
  if (!card?.providerAvailabilityRecorded) {
    return NOT_RECORDED_TEXT;
  }
  const parts = [formatCount(card.providerErrorCount ?? 0, card.providerValidAttempts ?? 0)];
  if ((card.providerUnattributedErrorCount ?? 0) > 0) {
    parts.push(`${formatInteger(card.providerUnattributedErrorCount ?? 0)} unattributed`);
  }
  const reruns = card.rerunsNeeded ?? 0;
  if (reruns > 0) {
    const providerReruns = card.providerRerunsNeeded ?? 0;
    parts.push(
      `${formatInteger(reruns)} ${reruns === 1 ? 're-run' : 're-runs'}` +
        (providerReruns > 0 ? `, ${formatInteger(providerReruns)} for provider errors` : '')
    );
  }
  return parts.join('; ');
}
