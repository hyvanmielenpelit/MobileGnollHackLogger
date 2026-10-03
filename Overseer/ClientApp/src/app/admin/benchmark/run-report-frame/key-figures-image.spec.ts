import {
  BADGE_PALETTE,
  CARD_IMAGE_MAX_WIDTH,
  CARD_IMAGE_SIZE,
  CardImageText,
  FactLayoutSizes,
  IMAGE_DETAILS_STORAGE_KEY,
  ImageContext,
  ImageFactRow,
  KeyFigureCell,
  StripText,
  TextMeasurer,
  TextWrapper,
  chooseCardImageLayout,
  chooseStripLayout,
  composeCardImage,
  composeStripImage,
  factBadgeHeight,
  KEY_FIGURES_STORAGE_KEY,
  exportKeyFiguresImage,
  filterKeyFigureCells,
  keyFigureCardFileName,
  keyFigureSlug,
  keyFiguresFileName,
  keyFiguresFooterText,
  keyFiguresImageIo,
  keyFiguresStatusMessage,
  layoutFactRows,
  loadKeyFigureLogos,
  measureStripCard,
  mixHex,
  readKeyFigureCell,
  readKeyFigureCells,
  readStoredImageDetailExclusions,
  readStoredKeyFigureExclusions,
  statusImageTone,
  storeImageDetailExclusions,
  storeKeyFigureExclusions,
  stripFootnotes,
  toImageFactRows
} from './key-figures-image';
import { RunFactModel, RunFactRow } from './run-facts';

/** Every card kind the run report renders, each with a card-actions element that must be ignored. */
const STRIP_FIXTURE = `
  <div class="rrf-figures">
    <div class="score-card main-score" data-figure="intelligence">
      <span class="score-label">Intelligence Index</span>
      <span class="score-value badge-score-mid">
        73 / 100
      </span>
      <span class="score-note">± 8 (95% CI)</span>
      <app-key-figure-card-actions>
        <button type="button" aria-label="Copy Intelligence Index of run 72 as an image"></button>
        <div popover="hint" class="gh-tooltip">Copy as image</div>
      </app-key-figure-card-actions>
    </div>
    <div class="score-card" data-figure="raw-quality">
      <span class="score-label">Raw Quality Index</span>
      <span class="score-subvalue badge-score-high">85 / 100</span>
      <app-key-figure-card-actions><div popover="hint" class="gh-tooltip">Download as PNG</div></app-key-figure-card-actions>
    </div>
    <div class="score-card" data-figure="unweighted-mean">
      <span class="score-label">Unweighted Mean</span>
      <span class="score-subvalue badge-score-na">N/A / 100</span>
      <span class="score-note">equal weights · difficulty weighting moved the index +2</span>
    </div>
    <div class="score-card" data-figure="critical-errors">
      <span class="score-label">Critical Errors</span>
      <span class="score-subvalue">2</span>
      <span class="score-note">Q3, Q7</span>
      <span class="score-note">1 disputed by the second reader</span>
    </div>
    <div class="score-card" data-figure="answered">
      <span class="score-label">Answered</span>
      <span class="score-subvalue">16 / 18</span>
      <span class="score-note">2 without text</span>
    </div>
    <div class="score-card" data-figure="speed">
      <span class="score-label">Speed Index</span>
      <span class="score-subvalue score-headline badge-score-low">
        40 / 100
        <span class="degraded-tag" title="Concurrency enabled; speed advisory">*</span>
        <span class="degraded-tag" title="Profile latency target does not fit">*</span>
      </span>
      <span class="score-note">saturated — 9 of 10 at the ceiling</span>
    </div>
    <div class="score-card" data-figure="speed">
      <span class="score-label">Median Model Time</span>
      <span class="score-subvalue">25.0 s</span>
      <span class="score-note">Speed Index 99 / 100 — advisory</span>
    </div>
    <div class="score-card" data-figure="speed">
      <span class="score-label">Speed Index</span>
      <span class="score-subvalue score-headline text-muted">Not computed</span>
      <span class="score-note">2 question(s) failed at the provider</span>
    </div>
    <div class="score-card" data-figure="mean-time">
      <span class="score-label">Mean Time per Question</span>
      <span class="score-subvalue">17.2 s</span>
      <span class="score-note">model time, tools excluded · median 14.7 s</span>
    </div>
    <div class="score-card panel-tile" data-figure="panel">
      <span class="score-label">Panel</span>
      <span class="score-subvalue">A 70 · B 72</span>
      <span class="score-note">ICC 0.81 · mean B − A +1.2</span>
      <span class="score-note">3 disagreement(s) over 10 answers both scored</span>
    </div>
    <div class="score-card" data-figure="agreement">
      <span class="score-label">Assessor Agreement</span>
      <span class="score-subvalue">mean |Δ| 0.0 pts <span class="degraded-tag" title="One answer only">*</span></span>
      <span class="score-note">1 of 10 answers graded twice · Flagged only · blind</span>
    </div>
    <div class="score-card" data-figure="model-cost">
      <span class="score-label">Candidate Cost</span>
      <span class="score-subvalue">$1.0000 <span class="degraded-tag" title="Some participating models lack pricing">*</span></span>
      <span class="score-note">31 % of estimated total</span>
      <span class="score-note">$0.0556 per question · 18 asked</span>
    </div>
    <div class="score-card">
      <span class="score-label">Total Cost</span>
      <span class="score-subvalue">$3.2322 <span class="degraded-tag" title="Some participating models lack pricing">*</span></span>
      <span class="score-note">estimated · all roles · catalog prices</span>
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

function cell(label: string, main = false, notes: string[] = ['note'], headline = main): KeyFigureCell {
  return { key: keyFigureSlug(label), label, value: main ? '73 / 100' : '24 / 100', notes, main, headline, tone: null, footnotes: [] };
}

/** A main card and `count - 1` ordinary cards, each with one note. */
function cellsOf(count: number): KeyFigureCell[] {
  return [cell('Intelligence Index', true), ...Array.from({ length: count - 1 }, (_, i) => cell(`Card ${i + 2}`))];
}

/** `count` ordinary cards and no main card, each with one note. */
function plainCellsOf(count: number): KeyFigureCell[] {
  return Array.from({ length: count }, (_, i) => cell(`Card ${i + 1}`));
}

function textRow(label: string, text: string, primary = false): ImageFactRow {
  return { label, runs: [{ kind: 'text', text }], primary };
}

/**
 * Two one-line text rows: 18 px each (the 13 px value line) with 4 px between, under a 4 px gap, so the
 * header is 48 (logo and gap) + 25 (title) + 4 + 18 + 4 + 18 = 117 px at every grid width the search tries.
 */
const STRIP_TEXT: StripText = {
  title: 'Run #72 · Default Suite',
  facts: [textRow('Model', 'M', true), textRow('Started', 'x')],
  footnotes: [],
  footer: 'GnollBench'
};

/**
 * One primary text row, 20 px (the 14 px value line): the header is 25 (title) + 4 + 20 = 49 px, taller
 * than the 40 px emblem. The Started row is not primary, so the card image leaves it out.
 */
const CARD_TEXT: CardImageText = {
  title: 'Run #72 · Default Suite',
  facts: [textRow('Model', 'M', true), textRow('Started', 'x')],
  footnotes: [],
  footer: 'GnollBench'
};

const CONTEXT: ImageContext = {
  title: 'Run #72 · Snapshot: Tommi2 2026-09-17',
  facts: [
    {
      label: 'Model',
      runs: [
        { kind: 'text', text: 'GPT 5.5', strong: true },
        { kind: 'badge', text: 'High', tone: 'thinking' },
        { kind: 'badge', text: 'OpenAI', tone: 'openai' }
      ],
      primary: true
    },
    { label: 'Assessor', runs: [{ kind: 'text', text: 'Gemini', strong: true }], primary: true },
    { label: 'Started', runs: [{ kind: 'text', text: '2026-09-17 10:00:00 UTC' }, { kind: 'badge', text: 'Completed', tone: 'success' }], primary: false }
  ],
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
        'Intelligence Index', 'Raw Quality Index', 'Unweighted Mean', 'Critical Errors', 'Answered', 'Speed Index',
        'Median Model Time', 'Speed Index', 'Mean Time per Question', 'Panel', 'Assessor Agreement', 'Candidate Cost',
        'Total Cost'
      ]);
    });

    it("reads each card's key from data-figure, and from its label where it has none", () => {
      expect(readKeyFigureCells(root).map(c => c.key)).toEqual([
        'intelligence', 'raw-quality', 'unweighted-mean', 'critical-errors', 'answered', 'speed', 'speed', 'speed',
        'mean-time', 'panel', 'agreement', 'model-cost', 'total-cost'
      ]);
    });

    it('reads label, value, notes and the main card, ignoring the card actions', () => {
      const [main, raw] = readKeyFigureCells(root);
      expect(main).toEqual({
        key: 'intelligence',
        label: 'Intelligence Index',
        value: '73 / 100',
        notes: ['± 8 (95% CI)'],
        main: true,
        headline: true,
        tone: 'mid',
        footnotes: []
      });
      expect(raw.value).toBe('85 / 100');
      expect(raw.notes).toEqual([]);
      expect(raw.main).toBe(false);
      expect(raw.headline).toBe(false);

      const text = JSON.stringify(readKeyFigureCells(root));
      expect(text).not.toContain('Copy as image');
      expect(text).not.toContain('Download as PNG');
    });

    it('reads the headline flag from .score-value and .score-headline', () => {
      expect(readKeyFigureCells(root).map(c => c.headline))
        .toEqual([true, false, false, false, false, true, false, true, false, false, false, false, false]);
    });

    it('reads the tone from the badge class, muted text as na, and none otherwise', () => {
      const tones = readKeyFigureCells(root).map(c => c.tone);
      expect(tones).toEqual(['mid', 'high', 'na', null, null, 'low', null, 'na', null, null, null, null, null]);
    });

    it('keeps one star per advisory marker and turns each distinct title into a footnote', () => {
      const cells = readKeyFigureCells(root);
      const speed = cells[5];
      expect(speed.value).toBe('40 / 100**');
      expect(speed.footnotes).toEqual([
        '* Concurrency enabled; speed advisory',
        '* Profile latency target does not fit'
      ]);
      expect(cells[10].value).toBe('mean |Δ| 0.0 pts*');
      expect(cells[12].value).toBe('$3.2322*');
      expect(cells[12].footnotes).toEqual(['* Some participating models lack pricing']);
    });

    it('reads every note of a card', () => {
      const cells = readKeyFigureCells(root);
      expect(cells[9].notes).toEqual(['ICC 0.81 · mean B − A +1.2', '3 disagreement(s) over 10 answers both scored']);
      expect(cells[3].notes).toEqual(['Q3, Q7', '1 disputed by the second reader']);
      expect(cells[11].notes).toEqual(['31 % of estimated total', '$0.0556 per question · 18 asked']);
    });

    it('reads one card without changing it', () => {
      const card = root.querySelector('.score-card') as HTMLElement;
      const before = card.innerHTML;
      expect(readKeyFigureCell(card).label).toBe('Intelligence Index');
      expect(card.innerHTML).toBe(before);
      expect(card.querySelector('app-key-figure-card-actions')).not.toBeNull();
    });
  });

  describe('choosing the figures', () => {
    let root: HTMLElement;

    beforeEach(() => {
      root = fixtureRoot();
      localStorage.removeItem(KEY_FIGURES_STORAGE_KEY);
    });

    afterEach(() => {
      root.remove();
      localStorage.removeItem(KEY_FIGURES_STORAGE_KEY);
    });

    it('keeps the cells the filter accepts, in order, and every cell without one', () => {
      const cells = readKeyFigureCells(root);
      expect(filterKeyFigureCells(cells).length).toBe(13);
      const kept = filterKeyFigureCells(cells, key => key === 'total-cost' || key === 'intelligence');
      expect(kept.map(c => c.label)).toEqual(['Intelligence Index', 'Total Cost']);
    });

    it('takes footnotes from the included cells only', () => {
      const cells = readKeyFigureCells(root);
      expect(stripFootnotes(cells)).toContain('* Concurrency enabled; speed advisory');
      const withoutSpeed = filterKeyFigureCells(cells, key => key !== 'speed');
      expect(stripFootnotes(withoutSpeed)).toEqual([
        '* One answer only',
        '* Some participating models lack pricing'
      ]);
      expect(stripFootnotes(filterKeyFigureCells(cells, key => key === 'mean-time'))).toEqual([]);
    });

    it('remembers the excluded keys as version 1, and reads nothing from an unknown shape', () => {
      expect(readStoredKeyFigureExclusions()).toEqual([]);

      storeKeyFigureExclusions(['raw-quality', ' panel ', 'raw-quality', '']);
      expect(JSON.parse(localStorage.getItem(KEY_FIGURES_STORAGE_KEY)!))
        .toEqual({ version: 1, excluded: ['raw-quality', 'panel'] });
      expect(readStoredKeyFigureExclusions()).toEqual(['raw-quality', 'panel']);

      localStorage.setItem(KEY_FIGURES_STORAGE_KEY, JSON.stringify({ version: 2, excluded: ['panel'] }));
      expect(readStoredKeyFigureExclusions()).toEqual([]);
      localStorage.setItem(KEY_FIGURES_STORAGE_KEY, '{not json');
      expect(readStoredKeyFigureExclusions()).toEqual([]);
      localStorage.setItem(KEY_FIGURES_STORAGE_KEY, JSON.stringify({ version: 1, excluded: ['speed', 3, null] }));
      expect(readStoredKeyFigureExclusions()).toEqual(['speed']);
    });

    it('survives storage that throws', () => {
      vi.spyOn(localStorage, 'getItem').mockImplementation(() => {
        throw new Error('denied');
      });
      vi.spyOn(localStorage, 'setItem').mockImplementation(() => {
        throw new Error('denied');
      });
      expect(readStoredKeyFigureExclusions()).toEqual([]);
      expect(() => storeKeyFigureExclusions(['panel'])).not.toThrow();
    });

    it('keeps a selection under another storage key apart from the run report', () => {
      const otherKey = 'overseer.spec.otherReport.keyFigures';
      try {
        storeKeyFigureExclusions(['speed', ' speed ', 'panel'], otherKey);
        expect(JSON.parse(localStorage.getItem(otherKey)!)).toEqual({ version: 1, excluded: ['speed', 'panel'] });
        expect(localStorage.getItem(KEY_FIGURES_STORAGE_KEY)).toBeNull();
        expect(readStoredKeyFigureExclusions(otherKey)).toEqual(['speed', 'panel']);
        expect(readStoredKeyFigureExclusions()).toEqual([]);

        storeKeyFigureExclusions(['raw-quality']);
        expect(readStoredKeyFigureExclusions(otherKey)).toEqual(['speed', 'panel']);
      } finally {
        localStorage.removeItem(otherKey);
      }
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

    it('pads 12 cards in three columns, the main card spanning two, to an exact square', () => {
      const layout = chooseStripLayout(cellsOf(12), STRIP_TEXT, noWrap);
      expect([layout.columns, layout.cardWidth, layout.mainSpans]).toEqual([3, 220, true]);
      expect(layout.naturalHeight).toBe(719);
      expect([layout.width, layout.height, layout.square]).toEqual([728, 728, true]);

      const byIndex = (index: number) => layout.placements.find(p => p.index === index)!;
      expect([byIndex(0).x, byIndex(0).y, byIndex(0).width]).toEqual([0, 0, 450]);
      expect([byIndex(1).x, byIndex(1).y]).toEqual([460, 0]);
      expect([byIndex(2).x, byIndex(2).y]).toEqual([0, 111]);
      expect([byIndex(11).x, byIndex(11).y]).toEqual([0, 417]);
      expect(layout.placements.length).toBe(12);
      expect(layout.gridHeight).toBe(509);
    });

    it('lays out a selection without the main card, never spanning', () => {
      const five = chooseStripLayout(plainCellsOf(5), STRIP_TEXT, noWrap);
      expect([five.columns, five.cardWidth, five.mainSpans]).toEqual([2, 230, false]);
      expect(five.naturalHeight).toBe(506);
      expect([five.width, five.height, five.square]).toEqual([518, 518, true]);

      const one = chooseStripLayout(plainCellsOf(1), STRIP_TEXT, noWrap);
      expect([one.columns, one.cardWidth, one.mainSpans]).toEqual([1, 260, false]);
      expect(one.naturalHeight).toBe(302);
      expect([one.width, one.height, one.square]).toEqual([308, 308, true]);
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

    it('is never portrait, and square whenever it is within 1.1, for every selection size 1 to 12', () => {
      for (const wrap of [noWrap, charWrap]) {
        for (let count = 1; count <= 12; count++) {
          for (const [kind, cells] of [['with main', cellsOf(count)], ['without main', plainCellsOf(count)]] as const) {
            const context = `${count} cards ${kind}`;
            const layout = chooseStripLayout(cells, STRIP_TEXT, wrap);
            expect(layout.width, context).toBeGreaterThanOrEqual(layout.height);
            expect(layout.placements.length, context).toBe(count);
            if (layout.width / layout.naturalHeight <= 1.1) {
              expect(layout.height, context).toBe(layout.width);
            }
            else {
              expect(layout.height, context).toBe(layout.naturalHeight);
            }
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

    it('lays out the run facts at the grid width under the title', () => {
      const layout = chooseStripLayout(cellsOf(7), STRIP_TEXT, noWrap);
      expect(layout.facts.rows.map(row => row.label)).toEqual(['MODEL', 'STARTED']);
      expect(layout.facts.height).toBe(18 + 4 + 18);
      expect(layout.facts.stacked).toBe(false);
    });

    it('draws a headline value at the main card size without the main card', () => {
      const speed = measureStripCard(cell('Speed Index', false, ['note'], true), 300, noWrap);
      const intelligence = measureStripCard(cell('Intelligence Index', true), 300, noWrap);
      const plain = measureStripCard(cell('Card 2'), 300, noWrap);
      expect([speed.valuePx, speed.valueWeight]).toEqual([intelligence.valuePx, intelligence.valueWeight]);
      expect([speed.valuePx, speed.valueWeight]).not.toEqual([plain.valuePx, plain.valueWeight]);
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

    it('draws a headline value at the main card size', () => {
      const speed = chooseCardImageLayout(cell('Speed Index', false, ['note'], true), CARD_TEXT, noWrap);
      const intelligence = chooseCardImageLayout(cell('Intelligence Index', true), CARD_TEXT, noWrap);
      expect(speed.card.valuePx).toBe(intelligence.card.valuePx);
      expect(speed.card.valueWeight).toBe(intelligence.card.valueWeight);
    });

    it('carries only the primary run facts in its header', () => {
      const layout = chooseCardImageLayout(cell('Speed Index'), CARD_TEXT, noWrap);
      expect(layout.facts.rows.map(row => row.label)).toEqual(['MODEL']);
      expect(layout.headerHeight).toBe(49);

      const none = chooseCardImageLayout(cell('Speed Index'), { ...CARD_TEXT, facts: [textRow('Started', 'x')] }, noWrap);
      expect(none.facts.height).toBe(0);
      expect(none.headerHeight).toBe(40);
    });
  });

  describe('run facts', () => {
    /** Every character half the font size wide, whatever the weight. */
    const halfWidth: TextMeasurer = (text, sizePx) => text.length * sizePx * 0.5;
    const SIZES: FactLayoutSizes = { labelPx: 12, textPx: 13 };

    function badgeRow(label: string, count: number): ImageFactRow {
      return {
        label,
        runs: [
          { kind: 'text', text: 'Name', strong: true },
          ...Array.from({ length: count }, () => ({ kind: 'badge' as const, text: 'High', tone: 'thinking' as const }))
        ],
        primary: true
      };
    }

    it('aligns every value on the widest label, upper-cased', () => {
      const layout = layoutFactRows([textRow('Model', 'M'), textRow('Scoring profile', 'Default')], 300, halfWidth, SIZES);
      expect(layout.labelWidth).toBe('SCORING PROFILE'.length * 12 * 0.5);
      expect(layout.valueX).toBe(90 + 12);
      expect(layout.rows.map(row => row.label)).toEqual(['MODEL', 'SCORING PROFILE']);
      expect(layout.height).toBe(18 + 4 + 18);
      expect(layout.rows[1].y).toBe(22);
    });

    it('never splits a badge across lines, and draws its text upper-case', () => {
      const layout = layoutFactRows([badgeRow('Model', 8)], 300, halfWidth, SIZES);
      const valueWidth = 300 - layout.valueX;
      expect(layout.rows[0].lines.length).toBeGreaterThan(1);
      const items = layout.rows[0].lines.flat();
      expect(items.filter(item => item.run.kind === 'badge').map(item => item.text)).toEqual(Array(8).fill('HIGH'));
      for (const line of layout.rows[0].lines) {
        for (const item of line) {
          expect(item.x + item.width).toBeLessThanOrEqual(valueWidth + 0.001);
        }
      }
    });

    it('wraps a long value at spaces onto more lines, keeping every word', () => {
      const text = words(20);
      const layout = layoutFactRows([textRow('Prompt', text)], 300, halfWidth, SIZES);
      const lines = layout.rows[0].lines;
      expect(lines.length).toBeGreaterThan(1);
      expect(lines.map(line => line.map(item => item.text).join(' ')).join(' ')).toBe(text);
      expect(layout.rows[0].height).toBe(lines.length * 18);
    });

    it('cuts a word wider than the value column with an ellipsis, alone on its line', () => {
      const layout = layoutFactRows([textRow('Model', `${'x'.repeat(80)} tail`)], 300, halfWidth, SIZES);
      const [first, second] = layout.rows[0].lines;
      expect(first.length).toBe(1);
      expect(first[0].text.endsWith('…')).toBe(true);
      expect(first[0].width).toBeLessThanOrEqual(300 - layout.valueX);
      expect(second.map(item => item.text)).toEqual(['tail']);
    });

    it('stacks labels above values when the value column would be under 60 % of the width', () => {
      const wide = layoutFactRows([textRow('Scoring profile', 'Default')], 300, halfWidth, SIZES);
      expect(wide.stacked).toBe(false);

      const narrow = layoutFactRows([textRow('Scoring profile', 'Default')], 200, halfWidth, SIZES);
      expect(narrow.stacked).toBe(true);
      expect(narrow.valueX).toBe(0);
      expect(narrow.rows[0].height).toBe(17 + 18);
    });

    it('makes a line holding a badge as tall as the badge box', () => {
      expect(factBadgeHeight(SIZES)).toBe(19);
      const layout = layoutFactRows([badgeRow('Model', 1), textRow('Started', 'x')], 300, halfWidth, SIZES);
      expect(layout.rows[0].lineHeights).toEqual([19]);
      expect(layout.rows[1].lineHeights).toEqual([18]);
      expect(layout.height).toBe(19 + 4 + 18);
    });

    it('gives no rows a height of 0', () => {
      const layout = layoutFactRows([], 300, halfWidth, SIZES);
      expect([layout.height, layout.rows.length, layout.labelWidth]).toEqual([0, 0, 0]);
    });

    it('starts a warning on its own line', () => {
      const layout = layoutFactRows([{
        label: 'Board',
        runs: [{ kind: 'text', text: 'Assessor 18/18' }, { kind: 'text', text: 'Gap', warning: true, lineBreak: true }],
        primary: false
      }], 600, halfWidth, SIZES);
      expect(layout.rows[0].lines.map(line => line.map(item => item.text))).toEqual([['Assessor 18/18'], ['Gap']]);
    });

    it('starts a badge with a line break on a new line', () => {
      const layout = layoutFactRows([{
        label: 'Model',
        runs: [{ kind: 'text', text: 'Name', strong: true }, { kind: 'badge', text: 'B', tone: 'role', lineBreak: true }],
        primary: true
      }], 600, halfWidth, SIZES);
      expect(layout.rows[0].lines.map(line => line.map(item => item.text))).toEqual([['Name'], ['B']]);
    });

    const ASSESSOR_A: RunFactModel = {
      role: 'A', name: 'Claude 5 Opus', provider: 'Anthropic', thinkingLevel: null, reasoningMode: 'max', serviceTier: null, customEndpoint: false
    };
    const ASSESSOR_B: RunFactModel = {
      role: 'B', name: 'Other', provider: 'Acme', thinkingLevel: null, reasoningMode: null, serviceTier: null, customEndpoint: false
    };

    it('starts each later model on its own line', () => {
      const facts = toImageFactRows(
        [{ key: 'assessor', label: 'Assessors', item: { kind: 'models', models: [ASSESSOR_A, ASSESSOR_B] } }],
        { text: 'Completed', tone: 'success' }
      );
      const layout = layoutFactRows(facts, 1200, halfWidth, SIZES);
      expect(layout.rows[0].lines.map(line => line.map(item => item.text))).toEqual([
        ['A', 'Claude 5 Opus', 'MAX', 'Anthropic'],
        ['B', 'Other', 'Acme']
      ]);
      expect(layout.rows[0].lineHeights).toEqual([factBadgeHeight(SIZES), factBadgeHeight(SIZES)]);
    });

    it('keeps a single model on one line', () => {
      const facts = toImageFactRows(
        [{ key: 'assessor', label: 'Assessor', item: { kind: 'models', models: [ASSESSOR_A] } }],
        { text: 'Completed', tone: 'success' }
      );
      const layout = layoutFactRows(facts, 1200, halfWidth, SIZES);
      expect(layout.rows[0].lines.length).toBe(1);
    });

    it('converts the header rows, with the badges, the status and the primary rows', () => {
      const rows: RunFactRow[] = [
        {
          key: 'model', label: 'Model', item: {
            kind: 'models', models: [{
              name: 'GPT-6.1 Sol', provider: 'OpenAI', thinkingLevel: 'high', reasoningMode: 'standard',
              serviceTier: 'flex', customEndpoint: true
            }]
          }
        },
        {
          key: 'assessor', label: 'Assessors', item: {
            kind: 'models', models: [
              { role: 'A', name: 'Claude 5 Opus', provider: 'Anthropic', thinkingLevel: null, reasoningMode: 'max', serviceTier: null, customEndpoint: false },
              { role: 'B', name: 'Other', provider: 'Acme', thinkingLevel: null, reasoningMode: null, serviceTier: null, customEndpoint: false }
            ]
          }
        },
        { key: 'prompt', label: 'Prompt', item: { kind: 'prompt', name: 'Gameplay Help', tags: ['concise', 'tools on'], summary: 'Gameplay Help · concise (tools on)' } },
        { key: 'started', label: 'Started', item: { kind: 'time', iso: '2026-09-30T13:35:24Z', text: '2026-09-30 13:35:24 UTC' } },
        {
          key: 'board', label: 'Board', item: {
            kind: 'board', figures: [{ role: 'Assessor', delivered: 18, total: 18 }, { role: 'Claim verifier', delivered: 16, total: 16 }],
            note: 'Synthesis: yes', gaps: ['assessor: Q3']
          }
        }
      ];
      const facts = toImageFactRows(rows, { text: 'Completed', tone: 'success' });

      expect(facts.map(row => row.label)).toEqual(['Model', 'Assessors', 'Prompt', 'Started', 'Board']);
      expect(facts.map(row => row.primary)).toEqual([true, true, false, false, false]);
      expect(facts[0].runs).toEqual([
        { kind: 'text', text: 'GPT-6.1 Sol', strong: true, lineBreak: undefined },
        { kind: 'badge', text: 'High', tone: 'thinking' },
        { kind: 'badge', text: 'OpenAI', tone: 'openai' },
        { kind: 'badge', text: 'Flex', tone: 'config' },
        { kind: 'badge', text: 'Custom endpoint', tone: 'config' }
      ]);
      expect(facts[1].runs).toEqual([
        { kind: 'badge', text: 'A', tone: 'role', lineBreak: undefined },
        { kind: 'text', text: 'Claude 5 Opus', strong: true, lineBreak: undefined },
        { kind: 'badge', text: 'max', tone: 'reasoning' },
        { kind: 'badge', text: 'Anthropic', tone: 'anthropic' },
        { kind: 'badge', text: 'B', tone: 'role', lineBreak: true },
        { kind: 'text', text: 'Other', strong: true, lineBreak: undefined },
        { kind: 'badge', text: 'Acme', tone: 'provider' }
      ]);
      expect(facts[2].runs).toEqual([
        { kind: 'text', text: 'Gameplay Help' },
        { kind: 'badge', text: 'concise', tone: 'config' },
        { kind: 'badge', text: 'tools on', tone: 'config' }
      ]);
      expect(facts[3].runs).toEqual([
        { kind: 'text', text: '2026-09-30 13:35:24 UTC' },
        { kind: 'badge', text: 'Completed', tone: 'success' }
      ]);
      expect(facts[4].runs).toEqual([
        { kind: 'text', text: 'Assessor 18/18 · Claim verifier 16/16' },
        { kind: 'text', text: '· Synthesis: yes', muted: true },
        { kind: 'text', text: 'Graded without the board — assessor: Q3', warning: true, strong: true, lineBreak: true }
      ]);
    });

    it('maps each run status class to a badge tone', () => {
      const tones = ['completed', 'scored', 'completedwitherrors', 'failed', 'completedwithlimits', 'running', 'canceled', 'pending']
        .map(status => statusImageTone(`badge-status-${status}`));
      expect(tones).toEqual(['success', 'success', 'warning', 'danger', 'info', 'running', 'config', 'config']);
    });

    it('mixes the provider badge colors as color-mix does', () => {
      expect(mixHex('#ffffff', '#000000', 0.5)).toBe('#808080');
      expect(mixHex('#10a37f', '#1b1b1b', 1)).toBe('#10a37f');
      expect(BADGE_PALETTE.openai.fill).toBe(mixHex('#10a37f', '#1b1b1b', 0.22));
      expect(BADGE_PALETTE.anthropic.border).toBe(mixHex('#d97757', '#383838', 0.6));
    });
  });

  describe('remembering the image details', () => {
    beforeEach(() => localStorage.removeItem(IMAGE_DETAILS_STORAGE_KEY));
    afterEach(() => localStorage.removeItem(IMAGE_DETAILS_STORAGE_KEY));

    it('leaves out the board while nothing is stored', () => {
      expect(readStoredImageDetailExclusions()).toEqual(['board']);
    });

    it('reads a stored empty list as every row', () => {
      storeImageDetailExclusions([]);
      expect(JSON.parse(localStorage.getItem(IMAGE_DETAILS_STORAGE_KEY)!)).toEqual({ version: 1, excluded: [] });
      expect(readStoredImageDetailExclusions()).toEqual([]);
    });

    it('round-trips a selection, normalized', () => {
      storeImageDetailExclusions(['prompt', ' started ', 'prompt', '']);
      expect(readStoredImageDetailExclusions()).toEqual(['prompt', 'started']);
    });

    it('falls back to the default for another version or unreadable JSON', () => {
      localStorage.setItem(IMAGE_DETAILS_STORAGE_KEY, JSON.stringify({ version: 2, excluded: [] }));
      expect(readStoredImageDetailExclusions()).toEqual(['board']);
      localStorage.setItem(IMAGE_DETAILS_STORAGE_KEY, '{not json');
      expect(readStoredImageDetailExclusions()).toEqual(['board']);
    });

    it('survives storage that throws', () => {
      vi.spyOn(localStorage, 'getItem').mockImplementation(() => {
        throw new Error('denied');
      });
      vi.spyOn(localStorage, 'setItem').mockImplementation(() => {
        throw new Error('denied');
      });
      expect(readStoredImageDetailExclusions()).toEqual(['board']);
      expect(() => storeImageDetailExclusions(['prompt'])).not.toThrow();
    });

    it('keeps a selection under another storage key apart, with the fallback the caller passes', () => {
      const otherKey = 'overseer.spec.otherReport.imageDetails';
      try {
        expect(readStoredImageDetailExclusions(otherKey)).toEqual(['board']);
        expect(readStoredImageDetailExclusions(otherKey, [])).toEqual([]);

        storeImageDetailExclusions(['suites', ' suites '], otherKey);
        expect(JSON.parse(localStorage.getItem(otherKey)!)).toEqual({ version: 1, excluded: ['suites'] });
        expect(localStorage.getItem(IMAGE_DETAILS_STORAGE_KEY)).toBeNull();
        expect(readStoredImageDetailExclusions(otherKey, [])).toEqual(['suites']);
        expect(readStoredImageDetailExclusions()).toEqual(['board']);

        localStorage.setItem(otherKey, '{not json');
        expect(readStoredImageDetailExclusions(otherKey, ['battery'])).toEqual(['battery']);
      } finally {
        localStorage.removeItem(otherKey);
      }
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

    it('names a battery run by its file stem instead of the run id', () => {
      const battery = { ...CONTEXT, fileStem: 'battery-run-7', suiteName: 'Core Battery' };
      expect(keyFiguresFileName(battery, NOW))
        .toBe('gnollbench_battery-run-7_core-battery_gpt-5.5-high_key-figures_20260928_123456.png');
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
      expect(keyFiguresStatusMessage('empty', 'Key figures')).toBe("None of this run's key figures is selected; use Choose figures.");
      expect(keyFiguresStatusMessage('failed', 'Key figures')).toBe('Could not create the image.');
    });
  });

  describe('composing and exporting', () => {
    let root: HTMLElement;

    beforeEach(() => {
      root = fixtureRoot();
      vi.spyOn(keyFiguresImageIo, 'loadImage').mockImplementation(() => Promise.reject(new Error('404')));
      vi.spyOn(keyFiguresImageIo, 'now').mockReturnValue(NOW);
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
      const save = vi.spyOn(keyFiguresImageIo, 'save').mockReturnValue(undefined);
      const message = await exportKeyFiguresImage('download', root, null, CONTEXT);

      expect(message).toBe('Image downloaded.');
      expect(save).toHaveBeenCalledTimes(1);
      const [blob, fileName] = vi.mocked(save).mock.lastCall!;
      expect(blob.type).toBe('image/png');
      expect(blob.size).toBeGreaterThan(0);
      expect(fileName).toBe('gnollbench_run72_snapshot-tommi2-2026-09-17_gpt-5.5-high_key-figures_20260928_123456.png');
    });

    it('copies one card and names it in the message', async () => {
      const copy = vi.spyOn(keyFiguresImageIo, 'copy').mockResolvedValue('copied');
      const card = root.querySelectorAll<HTMLElement>('.score-card')[5];
      const message = await exportKeyFiguresImage('copy', root, card, CONTEXT);

      expect(message).toBe('Speed Index copied as an image.');
      expect(copy).toHaveBeenCalledTimes(1);
      expect(vi.mocked(copy).mock.lastCall![0].type).toBe('image/png');
    });

    it('downloads only the cells the filter accepts, and composes nothing when none is left', async () => {
      const save = vi.spyOn(keyFiguresImageIo, 'save').mockReturnValue(undefined);
      expect(await exportKeyFiguresImage('download', root, null, CONTEXT, key => key === 'mean-time'))
        .toBe('Image downloaded.');
      expect(save).toHaveBeenCalledTimes(1);

      save.mockClear();
      expect(await exportKeyFiguresImage('download', root, null, CONTEXT, () => false))
        .toBe("None of this run's key figures is selected; use Choose figures.");
      expect(save).not.toHaveBeenCalled();
    });

    it('exports one card whatever the filter says', async () => {
      const save = vi.spyOn(keyFiguresImageIo, 'save').mockReturnValue(undefined);
      const card = root.querySelectorAll<HTMLElement>('.score-card')[0];
      expect(await exportKeyFiguresImage('download', root, card, CONTEXT, () => false)).toBe('Image downloaded.');
      expect(save).toHaveBeenCalledTimes(1);
    });

    it('reports a copy the browser refuses or cannot make', async () => {
      const copy = vi.spyOn(keyFiguresImageIo, 'copy').mockResolvedValue('unsupported');
      expect(await exportKeyFiguresImage('copy', root, null, CONTEXT))
        .toBe('This browser cannot copy images here; use Download instead.');
      copy.mockResolvedValue('denied');
      expect(await exportKeyFiguresImage('copy', root, null, CONTEXT)).toBe('Could not copy the image.');
    });
  });
});
