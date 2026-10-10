import { FigureSizeSettings } from '../../model-comparison/figure-size';
import { ccAnalysisResult, ccRunRows } from '../chat-consistency-tab.testing';
import { CcAnalysisResult } from '../chat-consistency.models';
import { CcResultsImageBlock, ccImageModelRows, ccResultsImageBlocks } from './results-image-blocks';
import {
  CC_RESULTS_IMAGE_FIT_WIDTH,
  CC_RESULTS_IMAGE_MIN_SCALE,
  CC_RESULTS_IMAGE_PAD,
  CC_RESULTS_IMAGE_TWO_COLUMN_WIDTH,
  CcImageOp,
  CcResultsImageLayoutInput,
  ccResultsImageFitRefusal,
  composeResultsImageLayout,
  layoutResultsImage
} from './results-image-layout';
import { defaultCcResultsImageSize } from './results-image-settings';

function input(blocks: readonly CcResultsImageBlock[], result: CcAnalysisResult = ccAnalysisResult()): CcResultsImageLayoutInput {
  return {
    blocks,
    header: { title: 'Chat Consistency · Verdicts', logo: true },
    modelRows: ccImageModelRows(result, false),
    analysisLine: 'Analysis #7 · saved 2026-10-02 09:00 UTC · Protocol V1',
    footer: 'GnollBench · Overseer 1.0.0 · exported 2026-10-10 09:45 UTC'
  };
}

function verdictBlocks(result: CcAnalysisResult = ccAnalysisResult()): CcResultsImageBlock[] {
  return ccResultsImageBlocks('verdicts', result, { rows: ccRunRows(), batteryRows: [], eventDays: [] });
}

/** `count` limitations, each a sentence, in the Details layout. */
function tallBlocks(count: number): CcResultsImageBlock[] {
  return [{ kind: 'bullets', title: 'Limitations', items: Array.from({ length: count }, (_, i) => `Limitation number ${i + 1} of the analysis.`) }];
}

function box(resolutionId: string, density = 1, textScalePercent = 100): FigureSizeSettings {
  return { ...defaultCcResultsImageSize(), resolutionId, densitySelection: density, textScalePercent };
}

const ofKind = <K extends CcImageOp['kind']>(ops: readonly CcImageOp[], kind: K): Extract<CcImageOp, { kind: K }>[] =>
  ops.filter((op): op is Extract<CcImageOp, { kind: K }> => op.kind === kind);

describe('results image layout', () => {
  it('lays fit mode out 1280 px wide, as tall as the content', () => {
    const short = layoutResultsImage(input(tallBlocks(2)), CC_RESULTS_IMAGE_FIT_WIDTH);
    const tall = layoutResultsImage(input(tallBlocks(40)), CC_RESULTS_IMAGE_FIT_WIDTH);
    expect(short.width).toBe(1280);
    expect(tall.height).toBeGreaterThan(short.height);
    expect(short.height).toBe(short.naturalHeight);

    const composition = composeResultsImageLayout(input(tallBlocks(2)), defaultCcResultsImageSize(), 'Details');
    expect('layout' in composition).toBe(true);
    if ('layout' in composition) {
      expect(composition.layout.width).toBe(1280);
      expect(composition.frame).toEqual({ pixelWidth: 2560, pixelHeight: Math.round(composition.layout.height * 2), scale: 2 });
      expect(composition.contentScale).toBe(1);
    }
  });

  it('draws the header and the footer inside the padding, the footer last', () => {
    const layout = layoutResultsImage(input(tallBlocks(2)), CC_RESULTS_IMAGE_FIT_WIDTH);
    const logo = ofKind(layout.ops, 'logo');
    expect(logo).toEqual([{ kind: 'logo', x: CC_RESULTS_IMAGE_PAD, y: CC_RESULTS_IMAGE_PAD, height: 36 }]);
    const texts = ofKind(layout.ops, 'text');
    expect(texts[0].lines).toEqual(['Chat Consistency · Verdicts']);
    expect(texts[0].ink).toBe('title');
    expect(ofKind(layout.ops, 'factRows').length).toBe(1);
    const footer = texts[texts.length - 1];
    expect(footer.lines.join(' ')).toContain('GnollBench · Overseer 1.0.0');
    expect(footer.y + footer.lines.length * Math.round(footer.size * 1.4)).toBeLessThanOrEqual(layout.height - CC_RESULTS_IMAGE_PAD);
  });

  it('leaves out the header, the model line, the analysis line and the footer when they are empty', () => {
    const bare = layoutResultsImage({ blocks: tallBlocks(2), header: null, modelRows: [], analysisLine: '', footer: '' }, 1280);
    expect(ofKind(bare.ops, 'logo')).toEqual([]);
    expect(ofKind(bare.ops, 'factRows')).toEqual([]);
    expect(ofKind(bare.ops, 'rule')).toEqual([]);
    const full = layoutResultsImage(input(tallBlocks(2)), 1280);
    expect(bare.height).toBeLessThan(full.height);
  });

  it('puts the cards two to a row from 1100 layout px, one below', () => {
    const blocks = verdictBlocks();
    const wide = layoutResultsImage(input(blocks), CC_RESULTS_IMAGE_TWO_COLUMN_WIDTH);
    const narrow = layoutResultsImage(input(blocks), CC_RESULTS_IMAGE_TWO_COLUMN_WIDTH - 1);
    expect(wide.columns).toBe(2);
    expect(narrow.columns).toBe(1);
    const cardBoxes = (ops: readonly CcImageOp[]) => ofKind(ops, 'box').filter(op => op.radius === 10 && op.surface === 'card');
    const wideBoxes = cardBoxes(wide.ops);
    expect(wideBoxes.length).toBe(5);
    expect(wideBoxes[0].y).toBe(wideBoxes[1].y);
    expect(wideBoxes[1].x).toBeGreaterThan(wideBoxes[0].x);
    // A row is as tall as its tallest card.
    expect(wideBoxes[0].height).toBe(wideBoxes[1].height);
    const narrowBoxes = cardBoxes(narrow.ops);
    expect(new Set(narrowBoxes.map(op => op.x)).size).toBe(1);
    expect(narrow.height).toBeGreaterThan(wide.height);
  });

  it('draws an interval bar for each computed endpoint with a geometry', () => {
    const layout = layoutResultsImage(input(verdictBlocks()), 1280);
    expect(ofKind(layout.ops, 'interval').length).toBe(5);
    const pills = ofKind(layout.ops, 'pill').map(op => op.text);
    expect(pills).toContain('Changed');
    expect(pills).toContain('P2');
  });

  it('writes a box at exactly its size times the density, the footer pinned to its bottom', () => {
    const composition = composeResultsImageLayout(input(tallBlocks(2)), box('fullhd', 2), 'Details');
    expect('layout' in composition).toBe(true);
    if (!('layout' in composition)) return;
    expect([composition.frame.pixelWidth, composition.frame.pixelHeight]).toEqual([3840, 2160]);
    expect(composition.layout.width).toBe(1920);
    expect(composition.layout.height).toBe(1080);
    expect(composition.frame.scale).toBe(2);
    const footer = ofKind(composition.layout.ops, 'text').at(-1)!;
    expect(footer.y).toBeGreaterThan(900);
  });

  it('lays a box out at its text size', () => {
    const composition = composeResultsImageLayout(input(tallBlocks(2)), box('fullhd', 1, 200), 'Details');
    expect('layout' in composition).toBe(true);
    if (!('layout' in composition)) return;
    expect(composition.layout.width).toBe(960);
    expect(composition.frame).toEqual({ pixelWidth: 1920, pixelHeight: 1080, scale: 2 });
  });

  it('scales content taller than the box down, to no less than 70 %', () => {
    const fits = composeResultsImageLayout(input(tallBlocks(2)), box('hd'), 'Details');
    const taller = composeResultsImageLayout(input(tallBlocks(22)), box('hd'), 'Details');
    expect('layout' in fits && fits.contentScale).toBe(1);
    expect('layout' in taller).toBe(true);
    if (!('layout' in taller)) return;
    expect(taller.contentScale).toBeLessThan(1);
    expect(taller.contentScale).toBeGreaterThanOrEqual(CC_RESULTS_IMAGE_MIN_SCALE);
    expect(taller.layout.width).toBeCloseTo(1280 / taller.contentScale, 6);
    expect(taller.layout.naturalHeight).toBeLessThanOrEqual(taller.layout.height + 0.5);
    expect([taller.frame.pixelWidth, taller.frame.pixelHeight]).toEqual([1280, 720]);
  });

  it('refuses a box the content does not fit at 70 %', () => {
    const composition = composeResultsImageLayout(input(tallBlocks(120)), box('hd'), 'Details');
    expect(composition).toEqual({ refusal: ccResultsImageFitRefusal('Details', 1280, 720) });
    expect(ccResultsImageFitRefusal('Details', 1280, 720))
      .toBe('The Details section does not fit 1280 × 720 px. Choose Fit the content, a taller size, or include less.');
  });

  it('refuses a bitmap over 16384 px a side, in fit mode and for a box', () => {
    const fit = composeResultsImageLayout(input(tallBlocks(1500)), { ...defaultCcResultsImageSize(), densitySelection: 4 }, 'Details');
    expect('refusal' in fit && fit.refusal).toContain('at most 16384 px');
    const oversized = composeResultsImageLayout(input(tallBlocks(2)),
      { ...defaultCcResultsImageSize(), resolutionId: 'custom', customWidthPx: 8000, customHeightPx: 8000, densitySelection: 4 }, 'Details');
    expect('refusal' in oversized && oversized.refusal).toContain('at most 16384 px');
  });
});
