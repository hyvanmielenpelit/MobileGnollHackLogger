/* DOM-free helpers for the snapshot board reader.

   Line indices are 0-based; line numbers, which the reader shows and the admin quotes, are
   1-based. The map layout is the one GnollHack's dump_map_ai() writes (src/detect.c): a
   "Map grid:" heading, a tens and a units column ruler each led by the four-character gutter,
   then rows y = 0..20, each "%2d: " followed by cells x = 1..79. Trailing blank cells may have
   been trimmed in transit; the units ruler ends in a digit and is never shortened. */

export const MAP_GUTTER_WIDTH = 4;
export const MAP_COLUMNS = 79;
export const MAP_MAX_Y = 20;

export interface MapBlock {
  headingLine: number;
  tensRulerLine: number;
  unitsRulerLine: number;
  firstRowLine: number;
  lastRowLine: number;
  /** Line index of each map row, keyed by the y its gutter states. */
  rowByY: Map<number, number>;
  /** The y each map row's gutter states, keyed by line index. */
  yByLine: Map<number, number>;
}

export interface ReaderSection {
  title: string;
  /** 1-based line number. */
  line: number;
}

/* A blank row trimmed of its trailing whitespace is left as " 3:" with no space after the colon. */
const MAP_ROW = /^ ?(\d{1,2}):(?: |$)/;

export function splitLines(text: string | null | undefined): string[] {
  if (!text) return [];
  return text.split('\n').map(line => (line.endsWith('\r') ? line.slice(0, -1) : line));
}

/** The y a map row's gutter states, or null when the line is not a map row. */
export function mapRowY(line: string): number | null {
  const match = MAP_ROW.exec(line);
  if (!match) return null;
  const y = Number(match[1]);
  return y <= MAP_MAX_Y ? y : null;
}

export function detectMapBlock(lines: string[]): MapBlock | null {
  const headingLine = lines.findIndex(line => line.trim() === 'Map grid:');
  if (headingLine < 0 || headingLine + 3 >= lines.length) return null;

  const gutter = ' '.repeat(MAP_GUTTER_WIDTH);
  const tens = lines[headingLine + 1];
  const units = lines[headingLine + 2];
  if (!tens.startsWith(gutter) || !units.startsWith(gutter) || !/\d$/.test(units)) return null;

  const rowByY = new Map<number, number>();
  const yByLine = new Map<number, number>();
  let lastRowLine = -1;
  for (let i = headingLine + 3; i < lines.length; i++) {
    const y = mapRowY(lines[i]);
    if (y === null) break;
    if (!rowByY.has(y)) rowByY.set(y, i);
    yByLine.set(i, y);
    lastRowLine = i;
  }
  if (lastRowLine < 0) return null;

  return {
    headingLine,
    tensRulerLine: headingLine + 1,
    unitsRulerLine: headingLine + 2,
    firstRowLine: headingLine + 3,
    lastRowLine,
    rowByY,
    yByLine
  };
}

/** The map cell at a character offset in a map row, or null inside the gutter or past x = 79. */
export function cellAt(lineText: string, charOffset: number): { x: number; symbol: string } | null {
  if (!Number.isInteger(charOffset)
      || charOffset < MAP_GUTTER_WIDTH
      || charOffset >= MAP_GUTTER_WIDTH + MAP_COLUMNS) {
    return null;
  }
  return { x: charOffset - MAP_GUTTER_WIDTH + 1, symbol: lineText.charAt(charOffset) || ' ' };
}

/** Character offset of column x in a map row. */
export function cellOffset(x: number): number {
  return MAP_GUTTER_WIDTH + x - 1;
}

/** Unindented lines of 3 to 48 characters ending in ':', such as "Inventory:" and "Map grid:". */
export function detectSections(lines: string[]): ReaderSection[] {
  const seen = new Set<string>();
  const sections: ReaderSection[] = [];
  lines.forEach((line, index) => {
    if (line.length < 3 || line.length > 48) return;
    if (/^\s/.test(line) || !line.endsWith(':')) return;
    if (MAP_ROW.test(line)) return;
    if (seen.has(line)) return;
    seen.add(line);
    sections.push({ title: line, line: index + 1 });
  });
  return sections;
}

/** Lines fromLine..toLine (1-based, inclusive) as "L{n}: text", numbers padded so the colons align. */
export function formatWithLineNumbers(lines: string[], fromLine: number, toLine: number): string {
  const from = Math.max(1, Math.min(fromLine, toLine));
  const to = Math.min(lines.length, Math.max(fromLine, toLine));
  const width = String(to).length;
  const out: string[] = [];
  for (let n = from; n <= to; n++) {
    const text = lines[n - 1];
    const label = `L${String(n).padStart(width, ' ')}:`;
    out.push(text ? `${label} ${text}` : label);
  }
  return out.join('\n');
}

/** The hero position from the legend sentence "The hero is at <x,y>", written by pager.c. */
export function parseHeroPosition(lines: string[]): { x: number; y: number } | null {
  for (const line of lines) {
    const match = /hero is at <(\d+),(\d+)>/.exec(line);
    if (match) return { x: Number(match[1]), y: Number(match[2]) };
  }
  return null;
}
