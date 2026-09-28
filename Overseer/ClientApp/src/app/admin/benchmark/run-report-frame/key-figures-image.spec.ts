import {
  CARD_IMAGE_MAX_WIDTH,
  CARD_IMAGE_SIZE,
  CardImageText,
  ImageContext,
  KeyFigureCell,
  StripText,
  TextWrapper,
  chooseCardImageLayout,
  chooseStripLayout,
  composeCardImage,
  composeStripImage,
  exportKeyFiguresImage,
  keyFigureCardFileName,
  keyFigureSlug,
  keyFiguresFileName,
  keyFiguresFooterText,
  keyFiguresImageIo,
  keyFiguresStatusMessage,
  loadKeyFigureLogos,
  readKeyFigureCell,
  readKeyFigureCells
} from './key-figures-image';

/** Every card kind the run report renders, each with a card-actions element that must be ignored. */
const STRIP_FIXTURE = `
  <div class="rrf-figures">
    <div class="score-card main-score">
      <span class="score-label">Intelligence Index</span>
      <span class="score-value badge-score-mid">
        73 / 100
      </span>
      <span class="score-note">± 8 (95%)</span>
      <app-key-figure-card-actions>
        <button type="button" aria-label="Copy Intelligence Index of run 72 as an image"></button>
        <div popover="hint" class="gh-tooltip">Copy as image</div>
      </app-key-figure-card-actions>
    </div>
    <div class="score-card">
      <span class="score-label">Raw Quality Index</span>
      <span class="score-subvalue badge-score-high">85 / 100</span>
      <app-key-figure-card-actions><div popover="hint" class="gh-tooltip">Download as PNG</div></app-key-figure-card-actions>
    </div>
    <div class="score-card">
      <span class="score-label">Unweighted Mean</span>
      <span class="score-subvalue badge-score-na">N/A / 100</span>
      <span class="score-note">weighting +2</span>
    </div>
    <div class="score-card">
      <span class="score-label">Speed Index</span>
      <span class="score-subvalue badge-score-low">
        40 / 100
        <span class="degraded-tag" title="Concurrency enabled; speed advisory">*</span>
        <span class="degraded-tag" title="Profile latency target does not fit">*</span>
      </span>
      <span class="score-note">saturated — 9 of 10 at the ceiling</span>
    </div>
    <div class="score-card">
      <span class="score-label">Median Model Time</span>
      <span class="score-subvalue">24,985 ms</span>
      <span class="score-note">Speed Index 99 / 100 — advisory</span>
    </div>
    <div class="score-card">
      <span class="score-label">Speed Index</span>
      <span class="score-subvalue text-muted">Not computed</span>
      <span class="score-note">2 question(s) failed at the provider</span>
    </div>
    <div class="score-card panel-tile">
      <span class="score-label">Panel</span>
      <span class="score-subvalue">A 70 · B 72</span>
      <span class="score-note">ICC 0.81 · mean B − A +1.2</span>
      <span class="score-note">3 disagreement(s) over 10 answers both scored</span>
    </div>
    <div class="score-card">
      <span class="score-label">Assessor Agreement</span>
      <span class="score-subvalue">0.0 pts <span class="degraded-tag" title="One answer only">*</span></span>
      <span class="score-note">1 of 10 · triggered · blind</span>
    </div>
    <div class="score-card">
      <span class="score-label">Model Under Test</span>
      <span class="score-subvalue">$1.0000 <span class="degraded-tag" title="Some participating models lack pricing">*</span></span>
      <span class="score-note">31 % of catalog total</span>
    </div>
    <div class="score-card">
      <span class="score-label">Estimated Cost</span>
      <span class="score-subvalue">$3.2322 <span class="degraded-tag" title="Some participating models lack pricing">*</span></span>
      <span class="score-note">Anthropic API</span>
    </div>
  </div>
`;

function fixtureRoot(): HTMLElement {
  const root = document.createElement('div');
  root.innerHTML = STRIP_FIXTURE;
  document.body.appendChild(root);
  return root;
}

/** Never wraps: every card has the same height at every width, so the layout search is exact. */
const noWrap: TextWrapper = text => (text.trim() === '' ? [] : [text]);

/** Greedy wrap where every character is half the font size wide. */
const charWrap: TextWrapper = (text, maxWidth, sizePx) => {
  const lines: string[] = [];
  let current = '';
  for (const word of text.trim().split(/\s+/).filter(part => part !== '')) {
    const candidate = current === '' ? word : `${current} ${word}`;
    if (current !== '' && candidate.length * sizePx * 0.5 > maxWidth) {
      lines.push(current);
      current = word;
    } else {
      current = candidate;
    }
  }
  if (current !== '') {
    lines.push(current);
  }
  return lines;
};

function cell(label: string, main = false, notes: string[] = ['note']): KeyFigureCell {
  return { label, value: main ? '73 / 100' : '24 / 100', notes, main, tone: null, footnotes: [] };
}

/** A main card and `count - 1` ordinary cards, each with one note. */
function cellsOf(count: number): KeyFigureCell[] {
  return [cell('Intelligence Index', true), ...Array.from({ length: count - 1 }, (_, i) => cell(`Card ${i + 2}`))];
}

const STRIP_TEXT: StripText = {
  title: 'Run #72 · Default Suite',
  lines: ['Model: M', 'Started'],
  footnotes: [],
  footer: 'GnollBench'
};

const CARD_TEXT: CardImageText = {
  title: 'Run #72 · Default Suite',
  line: 'Model: M',
  footnotes: [],
  footer: 'GnollBench'
};

const CONTEXT: ImageContext = {
  title: 'Run #72 · Snapshot: Tommi2 2026-09-17',
  lines: ['Model: GPT 5.5 (high) · Assessor: Gemini', 'Started 2026-09-17 10:00:00 UTC · Completed'],
  runId: 72,
  overseerVersion: '1.0.29',
  suiteName: 'Snapshot: Tommi2 2026-09-17',
  modelName: 'GPT 5.5 (high)'
};

const NOW = new Date(2026, 8, 28, 12, 34, 56);

function words(count: number): string {
  return Array.from({ length: count }, () => 'abcd').join(' ');
}

describe('key figures image', () => {
  describe('reading the rendered cards', () => {
    let root: HTMLElement;

    beforeEach(() => {
      root = fixtureRoot();
    });

    afterEach(() => {
      root.remove();
    });

    it('reads one cell per score card, in order', () => {
      const cells = readKeyFigureCells(root);
      expect(cells.map(c => c.label)).toEqual([
        'Intelligence Index', 'Raw Quality Index', 'Unweighted Mean', 'Speed Index', 'Median Model Time',
        'Speed Index', 'Panel', 'Assessor Agreement', 'Model Under Test', 'Estimated Cost'
      ]);
    });

    it('reads label, value, notes and the main card, ignoring the card actions', () => {
      const [main, raw] = readKeyFigureCells(root);
      expect(main).toEqual({
        label: 'Intelligence Index',
        value: '73 / 100',
        notes: ['± 8 (95%)'],
        main: true,
        tone: 'mid',
        footnotes: []
      });
      expect(raw.value).toBe('85 / 100');
      expect(raw.notes).toEqual([]);
      expect(raw.main).toBeFalse();

      const text = JSON.stringify(readKeyFigureCells(root));
      expect(text).not.toContain('Copy as image');
      expect(text).not.toContain('Download as PNG');
    });

    it('reads the tone from the badge class, muted text as na, and none otherwise', () => {
      const tones = readKeyFigureCells(root).map(c => c.tone);
      expect(tones).toEqual(['mid', 'high', 'na', 'low', null, 'na', null, null, null, null]);
    });

    it('keeps one star per advisory marker and turns each distinct title into a footnote', () => {
      const cells = readKeyFigureCells(root);
      const speed = cells[3];
      expect(speed.value).toBe('40 / 100**');
      expect(speed.footnotes).toEqual([
        '* Concurrency enabled; speed advisory',
        '* Profile latency target does not fit'
      ]);
      expect(cells[7].value).toBe('0.0 pts*');
      expect(cells[9].value).toBe('$3.2322*');
      expect(cells[9].footnotes).toEqual(['* Some participating models lack pricing']);
    });

    it('reads every note of a card', () => {
      const panel = readKeyFigureCells(root)[6];
      expect(panel.notes).toEqual(['ICC 0.81 · mean B − A +1.2', '3 disagreement(s) over 10 answers both scored']);
    });

    it('reads one card without changing it', () => {
      const card = root.querySelector('.score-card') as HTMLElement;
      const before = card.innerHTML;
      expect(readKeyFigureCell(card).label).toBe('Intelligence Index');
      expect(card.innerHTML).toBe(before);
      expect(card.querySelector('app-key-figure-card-actions')).not.toBeNull();
    });
  });

  describe('strip layout', () => {
    it('pads 7 cards in two columns to an exact square', () => {
      const layout = chooseStripLayout(cellsOf(7), STRIP_TEXT, noWrap);
      expect([layout.columns, layout.cardWidth, layout.mainSpans]).toEqual([2, 280, false]);
      expect(layout.naturalHeight).toBe(617);
      expect([layout.width, layout.height, layout.square]).toEqual([618, 618, true]);
    });

    it('pads 9 cards in two columns to an exact square', () => {
      const layout = chooseStripLayout(cellsOf(9), STRIP_TEXT, noWrap);
      expect([layout.columns, layout.cardWidth, layout.mainSpans]).toEqual([2, 340, false]);
      expect(layout.naturalHeight).toBe(719);
      expect([layout.width, layout.height, layout.square]).toEqual([738, 738, true]);
    });

    it('pads 10 cards in two columns to an exact square', () => {
      const layout = chooseStripLayout(cellsOf(10), STRIP_TEXT, noWrap);
      expect([layout.columns, layout.cardWidth, layout.mainSpans]).toEqual([2, 340, false]);
      expect(layout.naturalHeight).toBe(719);
      expect([layout.width, layout.height, layout.square]).toEqual([738, 738, true]);
    });

    it('keeps 11 cards landscape in three columns, the main card spanning two', () => {
      const layout = chooseStripLayout(cellsOf(11), STRIP_TEXT, noWrap);
      expect([layout.columns, layout.cardWidth, layout.mainSpans]).toEqual([3, 220, true]);
      expect([layout.width, layout.height, layout.square]).toEqual([728, 617, false]);

      const byIndex = (index: number) => layout.placements.find(p => p.index === index)!;
      expect([byIndex(0).x, byIndex(0).y, byIndex(0).width]).toEqual([0, 0, 450]);
      expect([byIndex(1).x, byIndex(1).y, byIndex(1).width]).toEqual([460, 0, 220]);
      expect([byIndex(2).x, byIndex(2).y]).toEqual([0, 111]);
      expect(layout.placements.length).toBe(11);
    });

    it('pads a single card to an exact square', () => {
      const layout = chooseStripLayout(cellsOf(1), STRIP_TEXT, noWrap);
      expect([layout.columns, layout.cardWidth]).toEqual([1, 270]);
      expect(layout.naturalHeight).toBe(311);
      expect([layout.width, layout.height, layout.square]).toEqual([318, 318, true]);
    });

    it('widens a single card with long notes until it is no longer portrait', () => {
      const long = [{ ...cell('Intelligence Index', true, [words(80)]) }];
      const layout = chooseStripLayout(long, STRIP_TEXT, charWrap);
      expect([layout.columns, layout.cardWidth]).toEqual([1, 380]);
      expect(layout.naturalHeight).toBe(413);
      expect([layout.width, layout.height, layout.square]).toEqual([428, 428, true]);
    });

    it('is never portrait, and square whenever it is within 1.1', () => {
      for (const wrap of [noWrap, charWrap]) {
        for (let count = 1; count <= 11; count++) {
          const layout = chooseStripLayout(cellsOf(count), STRIP_TEXT, wrap);
          expect(layout.width).withContext(`${count} cards`).toBeGreaterThanOrEqual(layout.height);
          if (layout.width / layout.naturalHeight <= 1.1) {
            expect(layout.height).withContext(`${count} cards`).toBe(layout.width);
          } else {
            expect(layout.height).withContext(`${count} cards`).toBe(layout.naturalHeight);
          }
        }
      }
    });

    it('spreads the square padding above and below the grid', () => {
      const layout = chooseStripLayout(cellsOf(7), STRIP_TEXT, noWrap);
      const unpaddedGridTop = 24 + 117 + 16;
      expect(layout.gridTop).toBe(unpaddedGridTop);
      expect(layout.afterGridTop).toBe(unpaddedGridTop + layout.gridHeight + 1);
    });

    it('wraps the footnotes into the layout', () => {
      const plain = chooseStripLayout(cellsOf(7), STRIP_TEXT, noWrap);
      const noted = chooseStripLayout(cellsOf(7), { ...STRIP_TEXT, footnotes: ['* One', '* Two'] }, noWrap);
      expect(noted.footnoteLines).toEqual([['* One'], ['* Two']]);
      expect(noted.naturalHeight).toBe(plain.naturalHeight + 12 + 17 + 2 + 17);
    });
  });

  describe('card image size', () => {
    it('is 640 square when the card fits', () => {
      for (const main of [true, false]) {
        const layout = chooseCardImageLayout(cell('Speed Index', main), CARD_TEXT, noWrap);
        expect([layout.width, layout.height, layout.notePx, layout.fits]).toEqual([CARD_IMAGE_SIZE, CARD_IMAGE_SIZE, 18, true]);
      }
    });

    it('widens in 40 px steps only for notes that do not fit', () => {
      const layout = chooseCardImageLayout(cell('Speed Index', false, [words(130)]), CARD_TEXT, charWrap);
      expect([layout.width, layout.height, layout.notePx, layout.fits]).toEqual([680, 640, 18, true]);
    });

    it('stops at 4 : 3 and steps the notes down to 15 px', () => {
      const layout = chooseCardImageLayout(cell('Speed Index', false, [words(400)]), CARD_TEXT, charWrap);
      expect(CARD_IMAGE_MAX_WIDTH).toBe(853);
      expect([layout.width, layout.height, layout.notePx, layout.fits]).toEqual([853, 640, 15, false]);
      expect(layout.width / layout.height).toBeLessThanOrEqual(4 / 3 + 0.001);
    });
  });

  describe('names and messages', () => {
    it('names the strip and a card with sanitized parts', () => {
      expect(keyFiguresFileName(CONTEXT, NOW))
        .toBe('gnollbench_run72_snapshot-tommi2-2026-09-17_gpt-5.5-high_key-figures_20260928_123456.png');
      expect(keyFigureCardFileName(CONTEXT, 'Intelligence Index', NOW))
        .toBe('gnollbench_run72_snapshot-tommi2-2026-09-17_gpt-5.5-high_intelligence-index_20260928_123456.png');
      expect(keyFigureCardFileName({ ...CONTEXT, suiteName: '../Suite/<x>', modelName: '' }, '../Cost/$', NOW))
        .toBe('gnollbench_run72_suitex_export_cost_20260928_123456.png');
    });

    it('slugs a label for ids and anchor names', () => {
      expect(keyFigureSlug('Reference Reader Agreement')).toBe('reference-reader-agreement');
      expect(keyFigureSlug(' Speed Index * ')).toBe('speed-index');
      expect(keyFigureSlug('···')).toBe('figure');
    });

    it('writes the footer in UTC', () => {
      const now = new Date(Date.UTC(2026, 8, 28, 9, 5, 30));
      expect(keyFiguresFooterText('1.0.29', now)).toBe('GnollBench · Overseer 1.0.29 · exported 2026-09-28 09:05 UTC');
      expect(keyFiguresFooterText(null, now)).toBe('GnollBench · Overseer unknown · exported 2026-09-28 09:05 UTC');
    });

    it('words every outcome', () => {
      expect(keyFiguresStatusMessage('copied', 'Key figures')).toBe('Key figures copied as an image.');
      expect(keyFiguresStatusMessage('copied', 'Speed Index')).toBe('Speed Index copied as an image.');
      expect(keyFiguresStatusMessage('unsupported', 'Key figures')).toBe('This browser cannot copy images here; use Download instead.');
      expect(keyFiguresStatusMessage('denied', 'Key figures')).toBe('Could not copy the image.');
      expect(keyFiguresStatusMessage('downloaded', 'Key figures')).toBe('Image downloaded.');
      expect(keyFiguresStatusMessage('failed', 'Key figures')).toBe('Could not create the image.');
    });
  });

  describe('composing and exporting', () => {
    let root: HTMLElement;

    beforeEach(() => {
      root = fixtureRoot();
      spyOn(keyFiguresImageIo, 'loadImage').and.callFake(() => Promise.reject(new Error('404')));
      spyOn(keyFiguresImageIo, 'now').and.returnValue(NOW);
    });

    afterEach(() => {
      root.remove();
    });

    it('leaves out a logo that fails to load', async () => {
      expect(await loadKeyFigureLogos()).toEqual({ wide: null, emblem: null });
    });

    it('composes a strip image that is never portrait, at twice the logical size', async () => {
      const canvas = await composeStripImage(readKeyFigureCells(root), CONTEXT, { wide: null, emblem: null }, NOW);
      expect(canvas.width).toBeGreaterThan(0);
      expect(canvas.width).toBeGreaterThanOrEqual(canvas.height);
      expect(canvas.width % 2).toBe(0);
    });

    it('composes a card image 1280 px tall and at most 4 : 3', async () => {
      const canvas = await composeCardImage(readKeyFigureCells(root)[0], CONTEXT, { wide: null, emblem: null }, NOW);
      expect(canvas.height).toBe(1280);
      expect(canvas.width).toBeGreaterThanOrEqual(1280);
      expect(canvas.width).toBeLessThanOrEqual(1706);
    });

    it('downloads the strip as a PNG even when no logo loads', async () => {
      const save = spyOn(keyFiguresImageIo, 'save');
      const message = await exportKeyFiguresImage('download', root, null, CONTEXT);

      expect(message).toBe('Image downloaded.');
      expect(save).toHaveBeenCalledTimes(1);
      const [blob, fileName] = save.calls.mostRecent().args;
      expect(blob.type).toBe('image/png');
      expect(blob.size).toBeGreaterThan(0);
      expect(fileName).toBe('gnollbench_run72_snapshot-tommi2-2026-09-17_gpt-5.5-high_key-figures_20260928_123456.png');
    });

    it('copies one card and names it in the message', async () => {
      const copy = spyOn(keyFiguresImageIo, 'copy').and.resolveTo('copied');
      const card = root.querySelectorAll<HTMLElement>('.score-card')[3];
      const message = await exportKeyFiguresImage('copy', root, card, CONTEXT);

      expect(message).toBe('Speed Index copied as an image.');
      expect(copy).toHaveBeenCalledTimes(1);
      expect(copy.calls.mostRecent().args[0].type).toBe('image/png');
    });

    it('reports a copy the browser refuses or cannot make', async () => {
      const copy = spyOn(keyFiguresImageIo, 'copy').and.resolveTo('unsupported');
      expect(await exportKeyFiguresImage('copy', root, null, CONTEXT))
        .toBe('This browser cannot copy images here; use Download instead.');
      copy.and.resolveTo('denied');
      expect(await exportKeyFiguresImage('copy', root, null, CONTEXT)).toBe('Could not copy the image.');
    });
  });
});
