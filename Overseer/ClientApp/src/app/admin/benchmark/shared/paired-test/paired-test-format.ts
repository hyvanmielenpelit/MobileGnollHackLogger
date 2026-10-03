import {
  BenchmarkPairKind,
  BenchmarkPairedMeasureDto,
  BenchmarkPairedTestDto
} from '../../../../services/admin-benchmark.service';

// Formatting shared by every paired-test view: the Model Comparison wizard's Paired tests view, the
// run report's Paired Test tab and the battery report's. Pure functions, no Angular.

/** The minus sign the paired views print: U+2212, not a hyphen. */
export const MINUS = '−';

/** Shown where a figure is absent. */
export const ABSENT = '—';

/** A number with an explicit sign: "+3.2", "−0.8", "0.0". */
export function formatSigned(value: number | null | undefined, decimals: number): string {
  if (value == null || !Number.isFinite(value)) {
    return ABSENT;
  }
  const text = Math.abs(value).toFixed(decimals);
  if (Number(text) === 0) {
    return text;
  }
  return value < 0 ? `${MINUS}${text}` : `+${text}`;
}

/** A number without a forced plus sign, with the paired views' minus: "7.1", "−0.8". */
export function formatPlain(value: number | null | undefined, decimals: number): string {
  if (value == null || !Number.isFinite(value)) {
    return ABSENT;
  }
  const text = Math.abs(value).toFixed(decimals);
  return value < 0 && Number(text) !== 0 ? `${MINUS}${text}` : text;
}

/** One decimal for differences of a point or more, two below that, so a small dimension difference is not "+0.0". */
export function differenceDecimals(...values: (number | null | undefined)[]): number {
  const finite = values.filter((value): value is number => value != null && Number.isFinite(value));
  const largest = finite.length === 0 ? 0 : Math.max(...finite.map(Math.abs));
  return largest >= 1 ? 1 : 2;
}

/** A ratio with its multiplication sign: "0.82×". */
export function formatRatio(value: number | null | undefined): string {
  if (value == null || !Number.isFinite(value)) {
    return ABSENT;
  }
  return `${value.toFixed(2)}×`;
}

/** The pair's effect alone: "+3.2" for a difference, "0.82×" for a ratio. */
export function formatEffectValue(pair: BenchmarkPairedTestDto): string {
  if (pair.effectKind === 'Ratio') {
    return formatRatio(pair.effect);
  }
  return formatSigned(pair.effect, differenceDecimals(pair.effect, pair.effectLower, pair.effectUpper));
}

/** The pair's 95 % interval alone: "−0.8 to 7.1", "0.71–0.95", or empty when the interval is withheld. */
export function formatEffectInterval(pair: BenchmarkPairedTestDto): string {
  const { effectLower: lower, effectUpper: upper } = pair;
  if (lower == null || upper == null || !Number.isFinite(lower) || !Number.isFinite(upper)) {
    return '';
  }
  if (pair.effectKind === 'Ratio') {
    return `${lower.toFixed(2)}–${upper.toFixed(2)}`;
  }
  const decimals = differenceDecimals(pair.effect, lower, upper);
  return `${formatPlain(lower, decimals)} to ${formatPlain(upper, decimals)}`;
}

/** The effect with its interval: "+3.2 (−0.8 to 7.1)", "0.82× (0.71–0.95)"; a dash when nothing was measured. */
export function formatEffect(pair: BenchmarkPairedTestDto): string {
  const value = formatEffectValue(pair);
  if (value === ABSENT) {
    return ABSENT;
  }
  const interval = formatEffectInterval(pair);
  return interval === '' ? value : `${value} (${interval})`;
}

/** A p-value: "< 0.001", three decimals below 0.01, two above; a dash when there is none. */
export function formatP(value: number | null | undefined): string {
  if (value == null || !Number.isFinite(value)) {
    return ABSENT;
  }
  if (value < 0.001) {
    return '< 0.001';
  }
  return value < 0.01 ? value.toFixed(3) : value.toFixed(2);
}

/** Cohen's dz with its sign, two decimals. */
export function formatDz(value: number | null | undefined): string {
  return formatSigned(value, 2);
}

/** What the effect column is called for a measure: a difference, or a ratio for speed and cost. */
export function effectHeader(measure: BenchmarkPairedMeasureDto): string {
  return measureEffectKind(measure) === 'Ratio' ? 'Ratio with 95 % interval' : 'Difference with 95 % interval';
}

/** The measure's effect kind: its pairs', else ratio for speed and cost. */
export function measureEffectKind(measure: BenchmarkPairedMeasureDto): 'Difference' | 'Ratio' {
  const kind = measure.pairs[0]?.effectKind;
  if (kind) {
    return kind;
  }
  return measure.category === 'Speed' || measure.category === 'Cost' ? 'Ratio' : 'Difference';
}

/** The measure's name as a heading: the primary one says so. */
export function measureTitle(measure: BenchmarkPairedMeasureDto): string {
  return measure.primary ? `${measure.label} (primary test)` : measure.label;
}

/** Why a pair was not tested: its own reason, else its measure's; null when it was tested. */
export function notTestedReasonOf(measure: BenchmarkPairedMeasureDto, pair: BenchmarkPairedTestDto | null): string | null {
  return pair?.notTestedReason || measure.notTestedReason || null;
}

/** The verdict's shape: a filled dot for an established difference, a ring otherwise, a dash when not tested. */
export type VerdictShape = 'established' | 'ring' | 'untested';

export function verdictShape(pair: BenchmarkPairedTestDto | null, measure?: BenchmarkPairedMeasureDto): VerdictShape {
  if (!pair || (measure ? notTestedReasonOf(measure, pair) : pair.notTestedReason)) {
    return 'untested';
  }
  return pair.established ? 'established' : 'ring';
}

/** The verdict word: the server's, else "Not tested". Never "equal". */
export function verdictText(pair: BenchmarkPairedTestDto | null): string {
  return pair?.verdict || 'Not tested';
}

// -------------------------------------------------------------------------------------------------
// The forest strip: every interval of one measure on one shared scale
// -------------------------------------------------------------------------------------------------

/** Positions, in percent of the strip's width, of one pair's interval and point estimate. */
export interface ForestMarks {
  readonly lower: number | null;
  readonly upper: number | null;
  readonly point: number | null;
}

/**
 * A shared scale for one measure's strips: linear and centered on 0 for a difference, logarithmic
 * and centered on 1 for a ratio, wide enough for every pair's interval with a small margin.
 */
export interface ForestScale {
  readonly kind: 'Difference' | 'Ratio';
  /** The no-difference line, in percent: always the middle. */
  readonly zero: number;
  position(value: number | null | undefined): number | null;
  marks(pair: BenchmarkPairedTestDto): ForestMarks;
}

export function forestScale(pairs: readonly BenchmarkPairedTestDto[], kind: 'Difference' | 'Ratio'): ForestScale {
  const transform = (value: number): number | null => {
    if (!Number.isFinite(value)) {
      return null;
    }
    if (kind === 'Ratio') {
      return value > 0 ? Math.log(value) : null;
    }
    return value;
  };
  let extent = 0;
  for (const pair of pairs) {
    for (const value of [pair.effect, pair.effectLower, pair.effectUpper]) {
      if (value == null) {
        continue;
      }
      const t = transform(value);
      if (t !== null) {
        extent = Math.max(extent, Math.abs(t));
      }
    }
  }
  const half = extent > 0 ? extent * 1.1 : 1;
  const position = (value: number | null | undefined): number | null => {
    if (value == null) {
      return null;
    }
    const t = transform(value);
    if (t === null) {
      return null;
    }
    const percent = ((t + half) / (2 * half)) * 100;
    return Math.min(100, Math.max(0, percent));
  };
  return {
    kind,
    zero: 50,
    position,
    marks: pair => ({
      lower: position(pair.effectLower),
      upper: position(pair.effectUpper),
      point: position(pair.effect)
    })
  };
}

// -------------------------------------------------------------------------------------------------
// The kind of a two-run or two-result comparison
// -------------------------------------------------------------------------------------------------

/** The sentence the Paired Test tabs show for a kind, when the server sent no explanation. */
export function kindSentence(kind: BenchmarkPairKind | 'NotComparable' | null | undefined): string {
  switch (kind) {
    case 'ModelComparison':
      return 'Model comparison: the model differs and every setting that must match agrees.';
    case 'Verification':
      return 'Verification of a change: the same model, with an instrument setting changed.';
    case 'Replicate':
      return 'Replicate: the same model under the same conditions; the difference measures run-to-run variation.';
    case 'NotComparable':
      return 'Not comparable.';
    default:
      return '';
  }
}

/** The kind's label with the server's explanation, or the fallback sentence. */
export function kindDescription(
  kind: BenchmarkPairKind | 'NotComparable' | null | undefined,
  kindLabel: string | null | undefined,
  explanation: string | null | undefined
): string {
  const text = (explanation ?? '').trim();
  if (text === '') {
    return kindSentence(kind);
  }
  const label = (kindLabel ?? '').trim();
  return label === '' || text.toLowerCase().startsWith(label.toLowerCase()) ? text : `${label}: ${text}`;
}

// -------------------------------------------------------------------------------------------------
// Errors
// -------------------------------------------------------------------------------------------------

/** A refusal's text: a plain-string body, the body's `error` or `message`, else the fallback. */
export function pairedErrorText(err: unknown, fallback: string): string {
  const body = (err as { error?: unknown } | null | undefined)?.error;
  if (typeof body === 'string' && body.trim() !== '') {
    return body;
  }
  if (body && typeof body === 'object') {
    const { error, message, title } = body as { error?: unknown; message?: unknown; title?: unknown };
    for (const candidate of [error, message, title]) {
      if (typeof candidate === 'string' && candidate.trim() !== '') {
        return candidate;
      }
    }
  }
  return fallback;
}

/** The pair's lookup key in a family: baseline, then treatment. */
export function pairKey(baselineKey: string, treatmentKey: string): string {
  return `${baselineKey}\u0000${treatmentKey}`;
}

/** The pair of a measure between two entries, in the server's orientation. */
export function findPair(
  measure: BenchmarkPairedMeasureDto,
  baselineKey: string | null,
  treatmentKey: string | null
): BenchmarkPairedTestDto | null {
  if (baselineKey === null && treatmentKey === null) {
    return measure.pairs[0] ?? null;
  }
  return measure.pairs.find(pair =>
    (baselineKey === null || pair.baselineKey === baselineKey)
    && (treatmentKey === null || pair.treatmentKey === treatmentKey)) ?? null;
}
