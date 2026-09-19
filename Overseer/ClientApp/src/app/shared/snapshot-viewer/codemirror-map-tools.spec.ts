import type { EditorView } from '@codemirror/view';
import type { MapReadout } from './codemirror-setup';
import { buildBoard } from './snapshot-viewer.spec-fixtures';

describe('codemirror-map-tools', () => {
  let setup: typeof import('./codemirror-setup');
  let parent: HTMLElement;
  let view: EditorView;
  let readouts: (MapReadout | null)[];

  /* Line numbers of the fixture: prose on 1-4, rulers on 5-6, map rows y = 0..20 on 7-27. */
  const TENS_RULER = 5;
  const UNITS_RULER = 6;
  const FIRST_ROW = 7;
  const HERO_ROW = FIRST_ROW + 13;

  beforeAll(async () => {
    setup = await import('./codemirror-setup');
  });

  /* Fixed at the window's top left, so neither window scroll nor the Jasmine HTML reporter above it
     in the body can push the editor out of the window; CodeMirror skips measuring an editor it
     cannot see, and its hit tests then run against estimated line heights. */
  beforeEach(() => {
    parent = document.createElement('div');
    parent.style.cssText = 'position: fixed; top: 0; left: 0; display: flex; flex-direction: column; width: 900px; height: 600px;';
    document.body.appendChild(parent);
    readouts = [];
    view = setup.createSnapshotEditor(parent, buildBoard(), () => {}, {
      mapTools: { onReadout: readout => readouts.push(readout) }
    });
  });

  function last(): MapReadout | null | undefined {
    return readouts[readouts.length - 1];
  }

  afterEach(() => {
    view.destroy();
    parent.remove();
  });

  function lineElement(lineNumber: number): HTMLElement {
    const line = view.state.doc.line(lineNumber);
    const { node } = view.domAtPos(line.from);
    const element = node.nodeType === Node.TEXT_NODE ? node.parentElement! : node as HTMLElement;
    return element.closest('.cm-line') as HTMLElement;
  }

  it('marks the hero cell, the ruler lines and every map row', () => {
    const hero = parent.querySelector('.cm-hero-cell');
    expect(hero).toBeTruthy();
    expect(hero!.textContent).toBe('@');
    expect(hero!.closest('.cm-line')).toBe(lineElement(HERO_ROW));

    expect(lineElement(TENS_RULER).classList).toContain('cm-map-ruler');
    expect(lineElement(UNITS_RULER).classList).toContain('cm-map-ruler');
    for (let n = FIRST_ROW; n <= FIRST_ROW + 20; n++) {
      expect(lineElement(n).classList).withContext(`line ${n}`).toContain('cm-map-row');
    }
    expect(parent.querySelectorAll('.cm-map-row').length).toBe(21);
    expect(lineElement(3).classList).not.toContain('cm-map-row');
  });

  it('keeps map rows unwrapped when wrapping is on', () => {
    setup.setLineWrapping(view, true);
    expect(view.contentDOM.classList).toContain('cm-lineWrapping');
    expect(getComputedStyle(lineElement(HERO_ROW)).whiteSpace).toBe('pre');
    expect(getComputedStyle(lineElement(3)).whiteSpace).not.toBe('pre');

    setup.setLineWrapping(view, false);
    expect(view.contentDOM.classList).not.toContain('cm-lineWrapping');
  });

  it('toggles the line-number gutter', () => {
    expect(parent.querySelector('.cm-lineNumbers')).toBeTruthy();
    setup.setLineNumbers(view, false);
    expect(parent.querySelector('.cm-lineNumbers')).toBeNull();
    setup.setLineNumbers(view, true);
    expect(parent.querySelector('.cm-lineNumbers')).toBeTruthy();
  });

  it('reads the map cell under the pointer', () => {
    const heroPos = view.state.doc.line(HERO_ROW).from + 13;
    const coords = view.coordsAtPos(heroPos, 1)!;
    const x = coords.left + view.defaultCharacterWidth / 2;
    const y = (coords.top + coords.bottom) / 2;
    expect(coords.bottom).withContext('hero cell inside the window').toBeLessThanOrEqual(window.innerHeight);

    view.contentDOM.dispatchEvent(new PointerEvent('pointermove', { clientX: x, clientY: y, bubbles: true }));
    expect(last()?.text).toBe('<10,13>  \'@\'');

    view.contentDOM.dispatchEvent(new PointerEvent('pointerleave', { clientX: x, clientY: y }));
    expect(last()).toBeNull();
  });

  it('reads the map cell at the caret, and nothing on prose', () => {
    view.dispatch({ selection: { anchor: view.state.doc.line(HERO_ROW).from + 13 } });
    expect(last()?.text).toBe('<10,13>  \'@\'');

    view.dispatch({ selection: { anchor: view.state.doc.line(FIRST_ROW).from + 4 } });
    expect(last()?.text).toBe('<1,0>  \' \' (blank: unseen or rock)');

    view.dispatch({ selection: { anchor: view.state.doc.line(2).from + 3 } });
    expect(last()).toBeNull();
  });

  it('follows the map when lines are added above it', () => {
    view.dispatch({ changes: { from: 0, insert: 'Extra line\n' } });
    expect(lineElement(HERO_ROW + 1).querySelector('.cm-hero-cell')).toBeTruthy();
    expect(lineElement(FIRST_ROW + 1).classList).toContain('cm-map-row');
  });

  it('reports the selected line range, excluding a line the selection only touches at its start', () => {
    expect(setup.selectedLineRange(view)).toBeNull();
    const doc = view.state.doc;
    view.dispatch({ selection: { anchor: doc.line(12).from + 2, head: doc.line(40).from } });
    expect(setup.selectedLineRange(view)).toEqual({ from: 12, to: 39 });
  });
});
