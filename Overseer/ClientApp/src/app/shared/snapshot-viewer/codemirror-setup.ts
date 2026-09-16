/* CodeMirror 6 assembly for the snapshot text editor.

   This is the only module that imports @codemirror/*. It is loaded with import() from
   SnapshotTextEditorComponent, so the editor code is a lazy chunk; importing it statically
   anywhere would fold CodeMirror into the initial bundle. Framework-free on purpose. */

import { EditorSelection, EditorState, Text } from '@codemirror/state';
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

export interface SnapshotDocInfo {
  length: number;
  lineCount: number;
  /** True when the document differs from the one the editor was created with. */
  modified: boolean;
}

export interface SnapshotEditorOptions {
  /** Accessible name of the editable content. */
  ariaLabel?: string;
  /** Runs on Ctrl-S / Cmd-S inside the editor; the browser's own save is always suppressed. */
  onSave?: () => void;
  /** Wraps long lines instead of scrolling them horizontally. Off by default. */
  lineWrapping?: boolean;
}

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

/* Line wrapping is off unless the caller asks for it: EditorView.lineWrapping is document-wide,
   and a wrapped map row misstates the map. A caller whose document carries no map grid — the
   snapshot digest, for one — may turn it on. */
export function createSnapshotEditor(
  parent: HTMLElement,
  doc: string,
  onDocChanged: (info: SnapshotDocInfo) => void,
  options: SnapshotEditorOptions = {}
): EditorView {
  let initialDoc: Text | null = null;

  const saveKeymap = options.onSave
    ? [keymap.of([{ key: 'Mod-s', preventDefault: true, run: () => { options.onSave!(); return true; } }])]
    : [];

  const wrapping = options.lineWrapping ? [EditorView.lineWrapping] : [];

  const state = EditorState.create({
    doc,
    extensions: [
      lineNumbers(),
      highlightActiveLineGutter(),
      highlightSpecialChars(),
      history(),
      drawSelection(),
      highlightActiveLine(),
      highlightSelectionMatches(),
      search({ top: true }),
      EditorState.tabSize.of(8),
      ...wrapping,
      ...saveKeymap,
      keymap.of([...defaultKeymap, ...historyKeymap, ...searchKeymap, indentWithTab]),
      EditorView.contentAttributes.of({
        'aria-label': options.ariaLabel ?? 'Snapshot text',
        spellcheck: 'false',
        autocorrect: 'off',
        autocapitalize: 'off'
      }),
      EditorView.updateListener.of(update => {
        if (!update.docChanged) return;
        const current = update.state.doc;
        onDocChanged({
          length: current.length,
          lineCount: current.lines,
          modified: !initialDoc || !current.eq(initialDoc)
        });
      }),
      snapshotTheme
    ]
  });

  initialDoc = state.doc;
  return new EditorView({ state, parent });
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
