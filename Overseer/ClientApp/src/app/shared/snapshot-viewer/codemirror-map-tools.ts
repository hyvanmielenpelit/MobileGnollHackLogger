/* Map tools for the snapshot text editor: map-row and ruler line classes, the hero-cell mark, and a
   readout of the map cell under the pointer or the caret.

   Imported only by codemirror-setup.ts, so it lives in the same lazy chunk. Line indices in the
   map model are 0-based, as in reader-text.ts; CodeMirror line numbers are 1-based. */

import { EditorState, Extension, Range, StateEffect, StateField, Text } from '@codemirror/state';
import { Decoration, DecorationSet, EditorView, ViewPlugin, ViewUpdate } from '@codemirror/view';
import { MapBlock, cellAt, cellOffset, detectMapBlock, parseHeroPosition } from './reader-text';

const RECOMPUTE_DEBOUNCE_MS = 400;

export interface MapReadout {
  x: number;
  y: number;
  symbol: string;
  /** `<x,y>  'sym'`, or `<x,y>  ' ' (blank: unseen or rock)`. */
  text: string;
}

export interface MapToolsOptions {
  /** Receives the readout for the status bar: null when neither the pointer nor the caret is on the map. */
  onReadout: (readout: MapReadout | null) => void;
}

interface MapModel {
  block: MapBlock;
  hero: { x: number; y: number } | null;
}

const setMapModel = StateEffect.define<MapModel | null>();

function docLines(doc: Text): string[] {
  const lines: string[] = [];
  for (const line of doc.iterLines()) lines.push(line);
  return lines;
}

function computeModel(doc: Text): MapModel | null {
  const lines = docLines(doc);
  const block = detectMapBlock(lines);
  return block ? { block, hero: parseHeroPosition(lines) } : null;
}

function shiftModel(model: MapModel, delta: number): MapModel {
  if (delta === 0) return model;
  const b = model.block;
  const rowByY = new Map<number, number>();
  const yByLine = new Map<number, number>();
  b.rowByY.forEach((line, y) => rowByY.set(y, line + delta));
  b.yByLine.forEach((y, line) => yByLine.set(line + delta, y));
  return {
    hero: model.hero,
    block: {
      headingLine: b.headingLine + delta,
      tensRulerLine: b.tensRulerLine + delta,
      unitsRulerLine: b.unitsRulerLine + delta,
      firstRowLine: b.firstRowLine + delta,
      lastRowLine: b.lastRowLine + delta,
      rowByY,
      yByLine
    }
  };
}

function sameModel(a: MapModel | null, b: MapModel | null): boolean {
  if (a === b) return true;
  if (!a || !b) return false;
  if (a.hero?.x !== b.hero?.x || a.hero?.y !== b.hero?.y) return false;
  const x = a.block;
  const y = b.block;
  if (x.headingLine !== y.headingLine || x.lastRowLine !== y.lastRowLine || x.yByLine.size !== y.yByLine.size) return false;
  for (const [line, row] of x.yByLine) {
    if (y.yByLine.get(line) !== row) return false;
  }
  return true;
}

/* An edit inside the map block is re-detected at once; an edit elsewhere only shifts the block by
   the lines it added or removed above it, and the debounced recompute catches anything else, such
   as an edited hero sentence. */
const mapModelField = StateField.define<MapModel | null>({
  create: state => computeModel(state.doc),
  update(model, tr) {
    for (const effect of tr.effects) {
      if (effect.is(setMapModel)) return effect.value;
    }
    if (!tr.docChanged) return model;
    if (!model) return computeModel(tr.newDoc);

    const oldDoc = tr.startState.doc;
    if (model.block.lastRowLine + 1 > oldDoc.lines) return computeModel(tr.newDoc);
    const start = oldDoc.line(model.block.headingLine + 1).from;
    const end = oldDoc.line(model.block.lastRowLine + 1).to;
    let touched = false;
    tr.changes.iterChangedRanges((fromA, toA) => {
      if (fromA <= end && toA >= start) touched = true;
    });
    if (touched) return computeModel(tr.newDoc);

    const newHeading = tr.newDoc.lineAt(tr.changes.mapPos(start, 1)).number - 1;
    return shiftModel(model, newHeading - model.block.headingLine);
  }
});

const mapRowLine = Decoration.line({ class: 'cm-map-row' });
const mapRulerLine = Decoration.line({ class: 'cm-map-ruler' });
const heroMark = Decoration.mark({ class: 'cm-hero-cell' });

function buildDecorations(state: EditorState): DecorationSet {
  const model = state.field(mapModelField);
  if (!model) return Decoration.none;
  const doc = state.doc;
  const b = model.block;
  const ranges: Range<Decoration>[] = [];

  for (const index of [b.tensRulerLine, b.unitsRulerLine]) {
    if (index < doc.lines) ranges.push(mapRulerLine.range(doc.line(index + 1).from));
  }
  for (const index of b.yByLine.keys()) {
    if (index < doc.lines) ranges.push(mapRowLine.range(doc.line(index + 1).from));
  }
  if (model.hero) {
    const index = b.rowByY.get(model.hero.y);
    if (index !== undefined && index < doc.lines) {
      const line = doc.line(index + 1);
      const offset = cellOffset(model.hero.x);
      if (offset >= 0 && offset < line.length) {
        ranges.push(heroMark.range(line.from + offset, line.from + offset + 1));
      }
    }
  }
  return Decoration.set(ranges, true);
}

function describe(x: number, y: number, symbol: string): MapReadout {
  const coordinate = `<${x},${y}>`;
  const text = symbol === ' '
    ? `${coordinate}  ' ' (blank: unseen or rock)`
    : `${coordinate}  '${symbol}'`;
  return { x, y, symbol, text };
}

function readoutAtOffset(state: EditorState, lineNumber: number, offset: number): MapReadout | null {
  const model = state.field(mapModelField);
  if (!model) return null;
  const y = model.block.yByLine.get(lineNumber - 1);
  if (y === undefined) return null;
  const cell = cellAt(state.doc.line(lineNumber).text, offset);
  return cell ? describe(cell.x, y, cell.symbol) : null;
}

function caretReadout(state: EditorState): MapReadout | null {
  const head = state.selection.main.head;
  const line = state.doc.lineAt(head);
  return readoutAtOffset(state, line.number, head - line.from);
}

/* posAtCoords gives the nearest character boundary, so a pointer over the right half of a cell
   lands after it; the cell is the one on the pointer's side of that boundary. Past a row's trimmed
   end the cells are blank, one character width each. */
function pointerReadout(view: EditorView, clientX: number, clientY: number): MapReadout | null {
  if (!view.state.field(mapModelField)) return null;
  const pos = view.posAtCoords({ x: clientX, y: clientY }, false);
  const line = view.state.doc.lineAt(pos);
  let offset = pos - line.from;

  const boundary = view.coordsAtPos(pos, 1);
  if (boundary && clientX < boundary.left && offset > 0) {
    offset -= 1;
  } else if (pos === line.to) {
    const end = view.coordsAtPos(line.to, -1);
    if (end && clientX > end.right) {
      offset = line.length + Math.floor((clientX - end.right) / view.defaultCharacterWidth);
    }
  }
  return readoutAtOffset(view.state, line.number, offset);
}

export function mapTools(options: MapToolsOptions): Extension {
  const plugin = ViewPlugin.fromClass(class {
    /* Set while the pointer is over a map cell; otherwise the caret's cell is reported. */
    pointer: MapReadout | null = null;
    lastText = '';
    timer: ReturnType<typeof setTimeout> | null = null;

    constructor(readonly view: EditorView) {}

    update(update: ViewUpdate): void {
      if (update.docChanged) {
        this.pointer = null;
        this.scheduleRecompute();
      }
      if (update.docChanged || update.selectionSet || update.transactions.some(tr => tr.effects.some(e => e.is(setMapModel)))) {
        this.report();
      }
    }

    destroy(): void {
      if (this.timer) clearTimeout(this.timer);
      this.timer = null;
    }

    onPointerMove(event: PointerEvent): void {
      this.pointer = pointerReadout(this.view, event.clientX, event.clientY);
      this.report();
    }

    onPointerLeave(): void {
      this.pointer = null;
      this.report();
    }

    report(): void {
      const readout = this.pointer ?? caretReadout(this.view.state);
      const text = readout?.text ?? '';
      if (text === this.lastText) return;
      this.lastText = text;
      options.onReadout(readout);
    }

    private scheduleRecompute(): void {
      if (this.timer) clearTimeout(this.timer);
      this.timer = setTimeout(() => {
        this.timer = null;
        const next = computeModel(this.view.state.doc);
        if (!sameModel(next, this.view.state.field(mapModelField))) {
          this.view.dispatch({ effects: setMapModel.of(next) });
        }
      }, RECOMPUTE_DEBOUNCE_MS);
    }
  }, {
    eventHandlers: {
      pointermove(event) {
        this.onPointerMove(event);
      },
      pointerleave() {
        this.onPointerLeave();
      }
    }
  });

  return [
    mapModelField,
    EditorView.decorations.compute([mapModelField], buildDecorations),
    plugin
  ];
}
