import { BenchmarkPairedMeasureDto, BenchmarkPairedTestDto } from '../../../../services/admin-benchmark.service';
import {
  differenceDecimals,
  effectHeader,
  findPair,
  forestScale,
  formatDz,
  formatEffect,
  formatEffectInterval,
  formatEffectValue,
  formatP,
  formatRatio,
  formatSigned,
  kindDescription,
  kindSentence,
  measureTitle,
  pairedErrorText,
  verdictShape,
  verdictText
} from './paired-test-format';

function pair(overrides: Partial<BenchmarkPairedTestDto> = {}): BenchmarkPairedTestDto {
  return {
    baselineKey: 'run:1',
    treatmentKey: 'run:2',
    pairedItems: 18,
    unpairedItems: 0,
    revisionMismatched: 0,
    effect: 3.2,
    effectLower: -0.8,
    effectUpper: 7.1,
    effectKind: 'Difference',
    dz: 0.45,
    pValue: 0.042,
    adjustedPValue: 0.084,
    method: 'Wilcoxon signed-rank',
    direction: 'Higher',
    established: false,
    verdict: 'No difference established',
    notTestedReason: null,
    note: null,
    suites: null,
    ...overrides
  };
}

function measure(overrides: Partial<BenchmarkPairedMeasureDto> = {}): BenchmarkPairedMeasureDto {
  return {
    measure: 'Intelligence',
    label: 'Intelligence Index',
    category: 'Intelligence',
    primary: true,
    familySize: 1,
    adjustment: 'None',
    adjustmentNote: 'Single comparison — no adjustment needed',
    notTestedReason: null,
    caption: null,
    pairs: [pair()],
    ...overrides
  };
}

describe('paired-test-format', () => {
  it('formats a difference with its sign and interval, using the minus sign', () => {
    expect(formatEffect(pair())).toBe('+3.2 (−0.8 to 7.1)');
    expect(formatEffectValue(pair({ effect: -2.04 }))).toBe('−2.0');
    expect(formatEffectInterval(pair({ effectLower: null }))).toBe('');
    expect(formatEffect(pair({ effectLower: null, effectUpper: null }))).toBe('+3.2');
    expect(formatEffect(pair({ effect: null }))).toBe('—');
  });

  it('uses two decimals for a difference under one point, so a small one is not +0.0', () => {
    expect(differenceDecimals(0.04, -0.1, 0.3)).toBe(2);
    expect(differenceDecimals(0.4, -0.2, 1)).toBe(1);
    expect(formatEffect(pair({ effect: 0.04, effectLower: -0.1, effectUpper: 0.3 }))).toBe('+0.04 (−0.10 to 0.30)');
    expect(formatSigned(0, 1)).toBe('0.0');
    expect(formatSigned(-0.001, 1)).toBe('0.0');
  });

  it('formats a ratio with a multiplication sign and an en-dash interval', () => {
    const ratio = pair({ effectKind: 'Ratio', effect: 0.82, effectLower: 0.71, effectUpper: 0.95 });
    expect(formatEffect(ratio)).toBe('0.82× (0.71–0.95)');
    expect(formatRatio(1.234)).toBe('1.23×');
    expect(formatRatio(null)).toBe('—');
  });

  it('formats p-values, dz and the effect header', () => {
    expect(formatP(0.0004)).toBe('< 0.001');
    expect(formatP(0.0042)).toBe('0.004');
    expect(formatP(0.042)).toBe('0.04');
    expect(formatP(0.5)).toBe('0.50');
    expect(formatP(null)).toBe('—');
    expect(formatDz(0.451)).toBe('+0.45');
    expect(formatDz(-0.2)).toBe('−0.20');
    expect(effectHeader(measure())).toBe('Difference with 95 % interval');
    expect(effectHeader(measure({ category: 'Speed', pairs: [] }))).toBe('Ratio with 95 % interval');
    expect(measureTitle(measure())).toBe('Intelligence Index (primary test)');
    expect(measureTitle(measure({ primary: false, label: 'Accuracy' }))).toBe('Accuracy');
  });

  it('gives an established difference a filled dot, any other a ring, and a not-tested one a dash', () => {
    expect(verdictShape(pair({ established: true }))).toBe('established');
    expect(verdictShape(pair())).toBe('ring');
    expect(verdictShape(pair({ notTestedReason: 'Speed is degraded.' }))).toBe('untested');
    expect(verdictShape(pair(), measure({ notTestedReason: 'Too few paired questions.' }))).toBe('untested');
    expect(verdictShape(null)).toBe('untested');
    expect(verdictText(pair())).toBe('No difference established');
    expect(verdictText(pair({ verdict: '' }))).toBe('Not tested');
    expect(verdictText(null)).toBe('Not tested');
  });

  it('puts every interval of a measure on one scale centered on zero', () => {
    const pairs = [pair(), pair({ effect: -5, effectLower: -10, effectUpper: 0 })];
    const scale = forestScale(pairs, 'Difference');
    expect(scale.zero).toBe(50);
    expect(scale.position(0)).toBe(50);
    // The widest bound (10) plus a tenth of margin spans each half.
    expect(scale.position(-10)).toBeCloseTo(50 - 50 / 1.1, 6);
    expect(scale.position(10)).toBeCloseTo(50 + 50 / 1.1, 6);
    const marks = scale.marks(pairs[0]);
    expect(marks.point!).toBeGreaterThan(50);
    expect(marks.lower!).toBeLessThan(50);
    expect(scale.position(null)).toBeNull();
  });

  it('centers a ratio scale on one, on a log scale, so 0.5× and 2× are equally far from it', () => {
    const scale = forestScale([pair({ effectKind: 'Ratio', effect: 2, effectLower: 0.5, effectUpper: 2 })], 'Ratio');
    expect(scale.position(1)).toBeCloseTo(50, 6);
    expect(50 - scale.position(0.5)!).toBeCloseTo(scale.position(2)! - 50, 6);
    expect(scale.position(0)).toBeNull();
  });

  it('keeps a scale usable when every value is zero', () => {
    const scale = forestScale([pair({ effect: 0, effectLower: 0, effectUpper: 0 })], 'Difference');
    expect(scale.position(0)).toBe(50);
  });

  it('describes a kind from the server, or with its own sentence', () => {
    expect(kindDescription('Verification', 'Verification of a change', 'CandidateSystemPromptSha256 differs.'))
      .toBe('Verification of a change: CandidateSystemPromptSha256 differs.');
    expect(kindDescription('Replicate', 'Replicate', 'Replicate: nothing differs.')).toBe('Replicate: nothing differs.');
    expect(kindDescription('ModelComparison', 'Model comparison', '')).toBe(kindSentence('ModelComparison'));
    expect(kindSentence('Replicate')).toContain('run-to-run variation');
  });

  it('reads a refusal from a string body, an error field or a message', () => {
    expect(pairedErrorText({ error: 'Comparing needs two comparable entries.' }, 'x')).toBe('Comparing needs two comparable entries.');
    expect(pairedErrorText({ error: { error: 'Unknown reference.' } }, 'x')).toBe('Unknown reference.');
    expect(pairedErrorText({ error: { message: 'Bad.' } }, 'x')).toBe('Bad.');
    expect(pairedErrorText({ status: 0 }, 'fallback')).toBe('fallback');
    expect(pairedErrorText(null, 'fallback')).toBe('fallback');
  });

  it('finds a pair by its keys, or the first pair without them', () => {
    const second = pair({ baselineKey: 'run:1', treatmentKey: 'run:3' });
    const family = measure({ pairs: [pair(), second] });
    expect(findPair(family, 'run:1', 'run:3')).toBe(second);
    expect(findPair(family, null, null)).toBe(family.pairs[0]);
    expect(findPair(family, 'run:3', 'run:1')).toBeNull();
  });
});
