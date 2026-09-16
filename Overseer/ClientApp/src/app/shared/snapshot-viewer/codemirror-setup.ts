/* CodeMirror 6 assembly for the snapshot text editor.

   This module and codemirror-map-tools.ts, which only this module imports, are the only ones that
   import @codemirror/*. It is loaded with import() from the editor components, so the editor code
   is a lazy chunk; importing it statically anywhere would fold CodeMirror into the initial bundle.
   Framework-free on purpose. */

import { Compartment, EditorSelection, EditorState, StateEffect, StateField, Text } from '@codemirror/state';
import {
  EditorView,
  drawSelection,
  highlightActiveLine,
  highlightActiveLineGutter,
  highlightSpecialChars,
  keymap,
  lineNumbers
} from '@codemirror/view';
import { defaultKeymap, history, historyKeymap, indentWithTab } from '@codemirror/commands';
import { highlightSelectionMatches, openSearchPanel, search, searchKeymap } from '@codemirror/search';
import { MapToolsOptions, mapTools } from './codemirror-map-tools';

export type { MapReadout, MapToolsOptions } from './codemirror-map-tools';

export interface SnapshotDocInfo {
  length: number;
  lineCount: number;
  /** True when the document differs from the saved one: the one the editor was created with, or the last passed to markSaved. */
  modified: boolean;
}

export interface SnapshotEditorOptions {
  /** Accessible name of the editable content. */
  ariaLabel?: string;
  /** Runs on Ctrl-S / Cmd-S inside the editor; the browser's own save is always suppressed. */
  onSave?: () => void;
  /** Wraps long lines instead of scrolling them horizontally; map rows and rulers never wrap. Off by default. */
  lineWrapping?: boolean;
  /** Shows the line-number gutter. On by default. */
  lineNumbers?: boolean;
  /** Adds the map-row and hero-cell decorations and the cell readout. */
  mapTools?: MapToolsOptions;
}

const lineNumbersCompartment = new Compartment();
const lineWrappingCompartment = new Compartment();

/* Carries the new saved document, or null for the transaction's own resulting document. */
const markSavedEffect = StateEffect.define<Text | null>();

/* The saved document the dirty flag compares against. */
const savedDocField = StateField.define<Text>({
  create: state => state.doc,
  update(saved, tr) {
    for (const effect of tr.effects) {
      if (effect.is(markSavedEffect)) return effect.value ?? tr.newDoc;
    }
    return saved;
  }
});

/* The reader's look: the same monospace stack and background as .reader-scroll, a gutter
   coloured like .reader-line::before, and the gold accent for the cursor and active line. */
const snapshotTheme = EditorView.theme({
  '&': {
    flex: '1 1 auto',
    minHeight: '0',
    backgroundColor: 'rgb(30, 30, 30)',
    color: 'var(--font-color, #fff)',
    fontSize: '0.82rem'
  },
  '&.cm-focused': {
    outline: '2px solid var(--primary-color, #e0ba6d)',
    outlineOffset: '-2px'
  },
  '.cm-scroller': {
    fontFamily: 'ui-monospace, SFMono-Regular, Menlo, Monaco, Consolas, monospace',
    fontVariantLigatures: 'none',
    lineHeight: '1.35',
    overscrollBehavior: 'contain'
  },
  '.cm-content': {
    caretColor: 'var(--primary-color, #e0ba6d)'
  },
  '.cm-cursor, .cm-dropCursor': {
    borderLeftColor: 'var(--primary-color, #e0ba6d)',
    borderLeftWidth: '2px'
  },
  '.cm-gutters': {
    backgroundColor: 'rgb(30, 30, 30)',
    color: '#8c8c8c',
    borderRight: '1px solid var(--border-glass, rgba(212, 160, 23, 0.25))'
  },
  '.cm-lineNumbers .cm-gutterElement': {
    padding: '0 12px 0 4px'
  },
  '.cm-activeLine': {
    backgroundColor: 'rgba(212, 175, 55, 0.08)'
  },
  '.cm-activeLineGutter': {
    backgroundColor: 'rgba(212, 175, 55, 0.16)',
    color: 'var(--primary-color, #e0ba6d)'
  },
  '&.cm-focused > .cm-scroller > .cm-selectionLayer .cm-selectionBackground, .cm-selectionBackground, .cm-content ::selection': {
    backgroundColor: 'rgba(96, 165, 250, 0.35)'
  },
  '.cm-selectionMatch': {
    backgroundColor: 'rgba(96, 165, 250, 0.2)'
  },
  '.cm-searchMatch': {
    backgroundColor: 'rgba(96, 165, 250, 0.35)',
    outline: 'none'
  },
  '.cm-searchMatch.cm-searchMatch-selected': {
    backgroundColor: 'rgba(212, 175, 55, 0.7)',
    color: '#000'
  },
  '.cm-specialChar': {
    color: 'var(--color-warning, #ffc107)'
  },
  /* A wrapped map row misstates the map, so map rows and rulers stay unwrapped when wrapping is on. */
  '.cm-line.cm-map-row, .cm-line.cm-map-ruler': {
    whiteSpace: 'pre',
    overflowWrap: 'normal',
    wordBreak: 'normal'
  },
  '.cm-line.cm-map-ruler': {
    color: 'var(--nav-color, #ccc)'
  },
  '.cm-hero-cell': {
    backgroundColor: 'rgba(212, 175, 55, 0.55)',
    color: '#000',
    textDecoration: 'underline double'
  },
  /* The find / replace and go-to-line panels are created by CodeMirror outside any Angular
     template, so component styles cannot reach them; they are given the .gh-input and
     .btn-ghost looks here. */
  '.cm-panels': {
    backgroundColor: 'rgb(24, 24, 24)',
    color: 'var(--font-color, #fff)'
  },
  '.cm-panels.cm-panels-top': {
    borderBottom: '1px solid var(--border-glass, rgba(212, 160, 23, 0.25))'
  },
  '.cm-panel.cm-search, .cm-panel.cm-gotoLine': {
    padding: '8px 36px 8px 12px',
    fontFamily: 'inherit',
    fontSize: '13px'
  },
  '.cm-panel label': {
    color: 'var(--nav-color, #ccc)',
    fontSize: '13px'
  },
  '.cm-textfield': {
    backgroundColor: 'var(--bg-input, rgba(30, 30, 30, 0.95))',
    border: '1px solid var(--border-glass, rgba(212, 160, 23, 0.25))',
    borderRadius: '4px',
    color: '#fff',
    colorScheme: 'dark',
    padding: '4px 8px',
    fontSize: '13px'
  },
  '.cm-textfield:focus': {
    outline: 'none',
    borderColor: 'var(--primary-color, #e0ba6d)',
    boxShadow: '0 0 5px var(--gold-glow, rgba(224, 186, 109, 0.5))'
  },
  '.cm-button': {
    backgroundImage: 'none',
    backgroundColor: 'transparent',
    border: '1px solid var(--border-glass, rgba(212, 160, 23, 0.25))',
    borderRadius: '6px',
    color: 'var(--font-color, #fff)',
    padding: '4px 10px',
    fontSize: '13px',
    cursor: 'pointer'
  },
  '.cm-button:hover': {
    borderColor: 'var(--primary-color, #e0ba6d)',
    backgroundColor: 'rgba(212, 175, 55, 0.08)',
    color: 'var(--primary-color, #e0ba6d)'
  },
  '.cm-button:active': {
    backgroundImage: 'none'
  },
  '.cm-button:focus-visible, .cm-panel button[name=close]:focus-visible': {
    outline: '2px solid var(--primary-color, #e0ba6d)',
    outlineOffset: '2px'
  },
  '.cm-panel button[name=close]': {
    color: 'var(--nav-color, #ccc)',
    fontSize: '20px',
    right: '8px'
  }
}, { dark: true });

/* Line wrapping is off unless the caller asks for it. It is EditorView.lineWrapping, so the height
   measurement knows lines wrap; the map-row and ruler line classes opt out of it. */
export function createSnapshotEditor(
  parent: HTMLElement,
  doc: string,
  onDocChanged: (info: SnapshotDocInfo) => void,
  options: SnapshotEditorOptions = {}
): EditorView {
  const saveKeymap = options.onSave
    ? [keymap.of([{ key: 'Mod-s', preventDefault: true, run: () => { options.onSave!(); return true; } }])]
    : [];

  const state = EditorState.create({
    doc,
    extensions: [
      savedDocField,
      lineNumbersCompartment.of(options.lineNumbers === false ? [] : lineNumbers()),
      highlightActiveLineGutter(),
      highlightSpecialChars(),
      history(),
      drawSelection(),
      highlightActiveLine(),
      highlightSelectionMatches(),
      search({ top: true }),
      EditorState.tabSize.of(8),
      lineWrappingCompartment.of(options.lineWrapping ? EditorView.lineWrapping : []),
      ...(options.mapTools ? [mapTools(options.mapTools)] : []),
      ...saveKeymap,
      keymap.of([...defaultKeymap, ...historyKeymap, ...searchKeymap, indentWithTab]),
      EditorView.contentAttributes.of({
        'aria-label': options.ariaLabel ?? 'Snapshot text',
        spellcheck: 'false',
        autocorrect: 'off',
        autocapitalize: 'off'
      }),
      EditorView.updateListener.of(update => {
        const markedSaved = update.transactions.some(tr => tr.effects.some(effect => effect.is(markSavedEffect)));
        if (!update.docChanged && !markedSaved) return;
        const current = update.state.doc;
        onDocChanged({
          length: current.length,
          lineCount: current.lines,
          modified: !current.eq(update.state.field(savedDocField))
        });
      }),
      snapshotTheme
    ]
  });

  return new EditorView({ state, parent });
}

export function setLineNumbers(view: EditorView, on: boolean): void {
  view.dispatch({ effects: lineNumbersCompartment.reconfigure(on ? lineNumbers() : []) });
}

export function setLineWrapping(view: EditorView, on: boolean): void {
  view.dispatch({ effects: lineWrappingCompartment.reconfigure(on ? EditorView.lineWrapping : []) });
}

/** 1-based first and last line of the main selection, or null when it is empty. A selection ending
    at the very start of a line does not include that line. */
export function selectedLineRange(view: EditorView): { from: number; to: number } | null {
  const selection = view.state.selection.main;
  if (selection.empty) return null;
  const doc = view.state.doc;
  const from = doc.lineAt(selection.from).number;
  const last = doc.lineAt(selection.to);
  const to = selection.to === last.from && last.number > from ? last.number - 1 : last.number;
  return { from, to };
}

/** Makes text the saved document. With replace, the buffer is replaced by it as well; without,
    the buffer is kept and is dirty wherever it differs. */
export function markSaved(view: EditorView, text: string, replace: boolean): void {
  const current = view.state.doc;
  if (replace) {
    const changes = current.toString() === text ? undefined : { from: 0, to: current.length, insert: text };
    view.dispatch({ changes, effects: markSavedEffect.of(null) });
  } else {
    view.dispatch({ effects: markSavedEffect.of(view.state.toText(text)) });
  }
}

/** Replaces the buffer with the saved document. */
export function revertToSaved(view: EditorView): void {
  const saved = view.state.field(savedDocField);
  if (view.state.doc.eq(saved)) return;
  view.dispatch({ changes: { from: 0, to: view.state.doc.length, insert: saved } });
}

export function getDocText(view: EditorView): string {
  return view.state.doc.toString();
}

export function getDocInfo(view: EditorView): { length: number; lineCount: number } {
  return { length: view.state.doc.length, lineCount: view.state.doc.lines };
}

/** Replaces the whole document, leaving the editor alone when the text already matches. */
export function replaceDocText(view: EditorView, text: string): void {
  const current = view.state.doc;
  if (current.toString() === text) return;
  view.dispatch({ changes: { from: 0, to: current.length, insert: text } });
}

/** Opens CodeMirror's find / replace panel. */
export function openFind(view: EditorView): void {
  openSearchPanel(view);
}

/** Puts the cursor at the start of a 1-based line, centres it and focuses the editor. */
export function scrollToLine(view: EditorView, lineNumber: number): void {
  const doc = view.state.doc;
  const target = Math.min(doc.lines, Math.max(1, Math.round(lineNumber)));
  const line = doc.line(target);
  view.dispatch({
    selection: EditorSelection.cursor(line.from),
    effects: EditorView.scrollIntoView(line.from, { y: 'center' })
  });
  view.focus();
}
