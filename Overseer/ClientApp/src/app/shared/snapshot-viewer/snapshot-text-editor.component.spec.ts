import { ComponentFixture, TestBed } from '@angular/core/testing';
import {
  PREF_LINE_NUMBERS,
  PREF_WRAP,
  SNAPSHOT_MAX_CHARS,
  SnapshotTextEditorComponent
} from './snapshot-text-editor.component';
import { buildBoard } from './snapshot-viewer.spec-fixtures';

describe('SnapshotTextEditorComponent', () => {
  let fixture: ComponentFixture<SnapshotTextEditorComponent>;
  let component: SnapshotTextEditorComponent;
  let host: HTMLElement;

  beforeEach(async () => {
    spyOn(Storage.prototype, 'getItem').and.returnValue(null);
    await TestBed.configureTestingModule({
      imports: [SnapshotTextEditorComponent]
    }).compileComponents();
  });

  /* The editor is loaded with import(), so every test waits for ready before touching it. */
  async function mount(text: string) {
    fixture = TestBed.createComponent(SnapshotTextEditorComponent);
    component = fixture.componentInstance;
    fixture.componentRef.setInput('text', text);
    fixture.componentRef.setInput('sha256', 'abc1234567890');
    fixture.componentRef.setInput('snapshotName', 'Emergency Low HP');
    fixture.detectChanges();
    await component.ready;
    fixture.detectChanges();
    host = fixture.nativeElement as HTMLElement;
  }

  /* Promise callbacks settle after a macrotask. */
  function settle(): Promise<void> {
    return new Promise(resolve => setTimeout(resolve));
  }

  function counterText(): string {
    return (host.querySelector('.editor-counter')?.textContent ?? '').trim();
  }

  function statusText(): string {
    return (host.querySelector('.editor-status-text')?.textContent ?? '').trim();
  }

  it('shows the document in the editor', async () => {
    const board = buildBoard();
    await mount(board);

    expect(component.loadError).toBeNull();
    expect(component.view).toBeTruthy();
    expect(host.querySelector('.cm-editor')).toBeTruthy();
    expect(component.view!.state.doc.toString()).toBe(board);
    expect(component.view!.state.doc.lines).toBe(250);
    expect(host.querySelector('.cm-content')!.textContent).toContain('Map grid:');
    expect((host.querySelector('.editor-lines')?.textContent ?? '').trim()).toBe('250 lines');
    expect(component.currentText()).toBe(board);
  });

  it('marks the document dirty on typing, and clean again when the edit is undone', async () => {
    await mount(buildBoard());
    const emitted: boolean[] = [];
    component.dirtyChange.subscribe(value => emitted.push(value));

    component.view!.dispatch({ changes: { from: 0, insert: 'X' } });
    fixture.detectChanges();
    expect(component.dirty).toBeTrue();
    expect(component.canSave).toBeTrue();
    expect(component.canRevert).toBeTrue();
    expect(component.currentText()!.startsWith('XMap:')).toBeTrue();

    component.view!.dispatch({ changes: { from: 0, to: 1 } });
    fixture.detectChanges();
    expect(component.dirty).toBeFalse();
    expect(component.canSave).toBeFalse();
    expect(component.canRevert).toBeFalse();

    expect(emitted).toEqual([true, false]);
  });

  it('emits the current document from a save request only when there are changes', async () => {
    await mount(buildBoard());
    const saved: string[] = [];
    component.save.subscribe(value => saved.push(value));

    component.requestSave();
    expect(saved).toEqual([]);

    component.view!.dispatch({ changes: { from: 0, insert: 'Edited ' } });
    fixture.detectChanges();
    component.requestSave();

    expect(saved.length).toBe(1);
    expect(saved[0].startsWith('Edited Map:\n')).toBeTrue();
  });

  it('does not emit a save while one is in progress', async () => {
    await mount(buildBoard());
    const saved: string[] = [];
    component.save.subscribe(value => saved.push(value));
    component.view!.dispatch({ changes: { from: 0, insert: 'X' } });
    fixture.componentRef.setInput('saving', true);
    fixture.detectChanges();

    component.requestSave();
    expect(saved).toEqual([]);
  });

  it('has no save or revert buttons of its own', async () => {
    await mount(buildBoard());
    const names = Array.from(host.querySelectorAll('button')).map(b => (b.textContent ?? '').trim());
    expect(names).not.toContain('Save Text');
    expect(names).not.toContain('Revert');
  });

  it('restores the loaded text on revert and clears dirty', async () => {
    const board = buildBoard();
    await mount(board);
    component.view!.dispatch({ changes: { from: 0, insert: 'Edited ' } });
    fixture.detectChanges();

    component.revert();
    fixture.detectChanges();

    expect(component.currentText()).toBe(board);
    expect(component.dirty).toBeFalse();
    expect(component.canRevert).toBeFalse();
  });

  it('takes the saved text as the new clean state', async () => {
    await mount(buildBoard());
    component.view!.dispatch({ changes: { from: 0, insert: 'Edited ' } });
    const submitted = component.currentText()!;

    component.markSaved('Normalized by the server', submitted);
    fixture.detectChanges();
    expect(component.currentText()).toBe('Normalized by the server');
    expect(component.dirty).toBeFalse();

    component.view!.dispatch({ changes: { from: 0, insert: 'More ' } });
    component.revert();
    expect(component.currentText()).toBe('Normalized by the server');
  });

  it('reports the length and warns past the server cap', async () => {
    const board = buildBoard();
    await mount(board);
    expect(counterText()).toBe(`${board.length.toLocaleString('en-US')} / 60,000 chars`);
    expect(host.querySelector('.editor-cap-warning')).toBeNull();

    component.view!.dispatch({ changes: { from: 0, insert: 'x'.repeat(SNAPSHOT_MAX_CHARS) } });
    fixture.detectChanges();

    expect(host.querySelector('.editor-counter.over-cap')).toBeTruthy();
    expect(host.querySelector('.editor-cap-warning')!.textContent).toContain('the server will truncate at 60,000 characters');
  });

  it('lists the map grid among the sections', async () => {
    await mount(buildBoard());
    const options = Array.from(host.querySelectorAll<HTMLOptionElement>('.section-select option')).map(o => o.textContent ?? '');
    expect(options.some(o => o.startsWith('Map grid:'))).toBeTrue();
  });

  it('moves the cursor to the chosen section', async () => {
    await mount(buildBoard());
    const select = host.querySelector<HTMLSelectElement>('.section-select')!;
    select.value = '4';
    select.dispatchEvent(new Event('change'));

    const view = component.view!;
    expect(view.state.doc.lineAt(view.state.selection.main.head).number).toBe(4);
    expect(select.value).toBe('');
  });

  it('opens the find panel from the Find button', async () => {
    await mount(buildBoard());
    host.querySelector<HTMLButtonElement>('.find-btn')!.click();
    expect(host.querySelector('.cm-search')).toBeTruthy();
  });

  it('copies the buffer, unsaved edits included, and announces it', async () => {
    await mount(buildBoard());
    const writeText = spyOn(navigator.clipboard, 'writeText').and.returnValue(Promise.resolve());
    component.view!.dispatch({ changes: { from: 0, insert: 'Edited ' } });

    host.querySelector<HTMLButtonElement>('.copy-text-btn')!.click();
    await settle();
    fixture.detectChanges();

    expect(writeText).toHaveBeenCalledWith(component.currentText()!);
    expect((writeText.calls.mostRecent().args[0] as string).startsWith('Edited Map:')).toBeTrue();
    expect(host.querySelector('.copy-text-btn')!.textContent!.trim()).toBe('Copied');
    expect(statusText()).toBe('Copied');
  });

  it('copies all lines with line numbers when nothing is selected', async () => {
    await mount(buildBoard());
    const writeText = spyOn(navigator.clipboard, 'writeText').and.returnValue(Promise.resolve());

    host.querySelector<HTMLButtonElement>('.copy-lines-btn')!.click();
    await settle();
    fixture.detectChanges();

    const copied = writeText.calls.mostRecent().args[0] as string;
    expect(copied.startsWith('L  1: Map:\n')).toBeTrue();
    expect(copied.split('\n').length).toBe(250);
    expect(statusText()).toBe('Copied all 250 lines');
  });

  it('copies only the selected lines with line numbers', async () => {
    await mount(buildBoard());
    const writeText = spyOn(navigator.clipboard, 'writeText').and.returnValue(Promise.resolve());
    const doc = component.view!.state.doc;
    component.view!.dispatch({ selection: { anchor: doc.line(2).from + 1, head: doc.line(4).to } });

    host.querySelector<HTMLButtonElement>('.copy-lines-btn')!.click();
    await settle();
    fixture.detectChanges();

    expect(writeText.calls.mostRecent().args[0]).toBe(
      'L2: The hero is at <10,13>, shown as \'@\'.\nL3: A food ration lies here.\nL4: Map grid:');
    expect(statusText()).toBe('Copied lines 2–4');
  });

  it('emits download with the dirty flag', async () => {
    await mount(buildBoard());
    const emitted: { dirty: boolean }[] = [];
    component.download.subscribe(value => emitted.push(value));
    const button = host.querySelector<HTMLButtonElement>('.download-btn')!;
    expect(button.textContent!.replace(/\s+/g, ' ').trim()).toBe('Download .snapshot.txt of Emergency Low HP');

    button.click();
    component.view!.dispatch({ changes: { from: 0, insert: 'X' } });
    button.click();

    expect(emitted).toEqual([{ dirty: false }, { dirty: true }]);
  });

  it('toggles line numbers and wrapping, and remembers both', async () => {
    await mount(buildBoard());
    const setItem = spyOn(Storage.prototype, 'setItem');
    const view = component.view!;
    expect(host.querySelector<HTMLInputElement>('.line-numbers-toggle')!.checked).toBeTrue();
    expect(host.querySelector<HTMLInputElement>('.wrap-toggle')!.checked).toBeFalse();
    expect(host.querySelector('.cm-lineNumbers')).toBeTruthy();

    host.querySelector<HTMLInputElement>('.line-numbers-toggle')!.click();
    host.querySelector<HTMLInputElement>('.wrap-toggle')!.click();
    fixture.detectChanges();

    expect(host.querySelector('.cm-lineNumbers')).toBeNull();
    expect(view.contentDOM.classList).toContain('cm-lineWrapping');
    expect(setItem).toHaveBeenCalledWith(PREF_LINE_NUMBERS, '0');
    expect(setItem).toHaveBeenCalledWith(PREF_WRAP, '1');
  });

  it('falls back to the defaults when storage throws', async () => {
    (Storage.prototype.getItem as jasmine.Spy).and.throwError(new Error('storage denied'));
    await mount(buildBoard());
    expect(component.showLineNumbers).toBeTrue();
    expect(component.wrapLines).toBeFalse();
  });

  it('shows the map readout and copies its coordinate', async () => {
    await mount(buildBoard());
    const writeText = spyOn(navigator.clipboard, 'writeText').and.returnValue(Promise.resolve());
    expect(host.querySelector('.copy-coordinate-btn')).toBeNull();

    const view = component.view!;
    view.dispatch({ selection: { anchor: view.state.doc.line(20).from + 13 } });
    fixture.detectChanges();
    expect(statusText()).toBe('<10,13>  \'@\'');

    const button = host.querySelector<HTMLButtonElement>('.copy-coordinate-btn')!;
    expect(button.getAttribute('aria-label')).toBe('Copy coordinate <10,13>');
    button.click();
    await settle();
    fixture.detectChanges();

    expect(writeText).toHaveBeenCalledWith('<10,13>');
    expect(statusText()).toBe('Copied <10,13>');
    expect(host.querySelector('.copy-coordinate-btn')).toBeTruthy();
  });
});
