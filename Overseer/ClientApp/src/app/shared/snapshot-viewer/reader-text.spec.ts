import {
  cellAt,
  cellOffset,
  detectMapBlock,
  detectSections,
  formatWithLineNumbers,
  mapRowY,
  parseHeroPosition,
  splitLines
} from './reader-text';

/* A map block built the way dump_map_ai() writes it, trailing whitespace trimmed as the
   sanitizer does. The hero '@' is at <10,13>; row 3 is entirely blank. */
function trimEnd(s: string): string {
  return s.replace(/\s+$/, '');
}

function tensRuler(): string {
  let s = '    ';
  for (let x = 1; x < 80; x++) s += x % 10 === 0 ? String(x / 10) : ' ';
  return trimEnd(s);
}

function unitsRuler(): string {
  let s = '    ';
  for (let x = 1; x < 80; x++) s += String(x % 10);
  return s;
}

function mapRow(y: number, cells: string): string {
  const gutter = (y < 10 ? ' ' : '') + y + ': ';
  return trimEnd(gutter + cells);
}

function buildMapBlock(): string[] {
  const lines = ['Map grid:', tensRuler(), unitsRuler()];
  for (let y = 0; y <= 20; y++) {
    let cells = '';
    if (y === 3) cells = '';
    else if (y === 13) cells = '---------@....%....|';
    else cells = '  |....|' + '.'.repeat(y);
    lines.push(mapRow(y, cells));
  }
  return lines;
}

describe('reader-text', () => {
  describe('splitLines', () => {
    it('strips a trailing carriage return from every line', () => {
      expect(splitLines('a\r\nb\r\nc')).toEqual(['a', 'b', 'c']);
    });

    it('returns no lines for empty or missing text', () => {
      expect(splitLines('')).toEqual([]);
      expect(splitLines(null)).toEqual([]);
    });
  });

  describe('detectMapBlock', () => {
    const prose = ['Map:', 'The hero is at <10,13>, shown as \'@\'.', ''];

    it('finds the heading, both rulers and all 21 rows', () => {
      const lines = [...prose, ...buildMapBlock(), '', 'Inventory:'];
      const block = detectMapBlock(lines)!;
      expect(block).not.toBeNull();
      expect(block.headingLine).toBe(3);
      expect(block.tensRulerLine).toBe(4);
      expect(block.unitsRulerLine).toBe(5);
      expect(block.firstRowLine).toBe(6);
      expect(block.lastRowLine).toBe(26);
      expect(block.rowByY.size).toBe(21);
      expect(block.rowByY.get(13)).toBe(19);
      expect(block.yByLine.get(19)).toBe(13);
    });

    it('keeps rows trimmed to different lengths, including a fully blank one', () => {
      const block = detectMapBlock(buildMapBlock())!;
      expect(block.rowByY.has(3)).toBeTrue();
      expect(block.rowByY.size).toBe(21);
    });

    it('returns null without the heading', () => {
      expect(detectMapBlock(buildMapBlock().slice(1))).toBeNull();
    });

    it('returns null when the units ruler is malformed', () => {
      const lines = buildMapBlock();
      lines[2] = lines[2] + ' x';
      expect(detectMapBlock(lines)).toBeNull();
    });

    it('returns null when no row follows the rulers', () => {
      expect(detectMapBlock(buildMapBlock().slice(0, 3).concat(['Inventory:']))).toBeNull();
    });
  });

  describe('mapRowY', () => {
    it('reads y from the gutter', () => {
      expect(mapRowY(' 0: ...')).toBe(0);
      expect(mapRowY('20: ...')).toBe(20);
      expect(mapRowY(' 3:')).toBe(3);
    });

    it('rejects prose and out-of-range rows', () => {
      expect(mapRowY('Inventory:')).toBeNull();
      expect(mapRowY('21: ...')).toBeNull();
      expect(mapRowY('    123')).toBeNull();
    });
  });

  describe('cellAt', () => {
    const row = mapRow(13, '---------@....%....|');

    it('returns null inside the gutter', () => {
      expect(cellAt(row, 3)).toBeNull();
    });

    it('maps offset 4 to x = 1 and offset 82 to x = 79', () => {
      expect(cellAt(row, 4)).toEqual({ x: 1, symbol: '-' });
      expect(cellAt(row, 82)).toEqual({ x: 79, symbol: ' ' });
    });

    it('returns null past x = 79', () => {
      expect(cellAt(row, 83)).toBeNull();
    });

    it('reads the symbol at a cell', () => {
      expect(cellAt(row, cellOffset(10))).toEqual({ x: 10, symbol: '@' });
    });
  });

  describe('detectSections', () => {
    it('lists headings and skips rulers and map rows', () => {
      const lines = ['Map:', 'Some prose.', ...buildMapBlock(), 'Inventory:', '  Indented:', 'Map:'];
      const sections = detectSections(lines);
      expect(sections.map(s => s.title)).toEqual(['Map:', 'Map grid:', 'Inventory:']);
      expect(sections[1].line).toBe(3);
    });
  });

  describe('formatWithLineNumbers', () => {
    it('prefixes each line and pads the numbers so the colons align', () => {
      const lines = Array.from({ length: 12 }, (_, i) => `t${i + 1}`);
      expect(formatWithLineNumbers(lines, 9, 11)).toBe('L 9: t9\nL10: t10\nL11: t11');
    });

    it('leaves no trailing space on an empty line', () => {
      expect(formatWithLineNumbers(['a', '', 'c'], 1, 3)).toBe('L1: a\nL2:\nL3: c');
    });
  });

  describe('parseHeroPosition', () => {
    it('reads the legend sentence', () => {
      expect(parseHeroPosition(['x', 'The hero is at <10,13>, shown as \'@\'.'])).toEqual({ x: 10, y: 13 });
    });

    it('returns null when the sentence is absent', () => {
      expect(parseHeroPosition(['Map:'])).toBeNull();
    });
  });
});
