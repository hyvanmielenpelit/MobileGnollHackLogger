import { ComponentFixture, TestBed } from '@angular/core/testing';
import { SNAPSHOT_MAX_CHARS, SnapshotTextEditorComponent } from './snapshot-text-editor.component';
import { buildBoard } from './snapshot-viewer.spec-fixtures';

describe('SnapshotTextEditorComponent', () => {
  let fixture: ComponentFixture<SnapshotTextEditorComponent>;
  let component: SnapshotTextEditorComponent;
  let host: HTMLElement;

  beforeEach(async () => {
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
    fixture.detectChanges();
    await component.ready;
    fixture.detectChanges();
    host = fixture.nativeElement as HTMLElement;
  }

  function saveButton(): HTMLButtonElement {
    return host.querySelector<HTMLButtonElement>('.save-text-btn')!;
  }

  function counterText(): string {
    return (host.querySelector('.editor-counter')?.textContent ?? '').trim();
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
  });

  it('marks the document dirty on typing, and clean again when the edit is undone', async () => {
    await mount(buildBoard());
    const emitted: boolean[] = [];
    component.dirtyChange.subscribe(value => emitted.push(value));

    component.view!.dispatch({ changes: { from: 0, insert: 'X' } });
    fixture.detectChanges();
    expect(component.dirty).toBeTrue();
    expect(saveButton().getAttribute('aria-disabled')).toBeNull();

    component.view!.dispatch({ changes: { from: 0, to: 1 } });
    fixture.detectChanges();
    expect(component.dirty).toBeFalse();
    expect(saveButton().getAttribute('aria-disabled')).toBe('true');

    expect(emitted).toEqual([true, false]);
  });

  it('emits the current document from Save Text only when there are changes', async () => {
    await mount(buildBoard());
    const saved: string[] = [];
    component.save.subscribe(value => saved.push(value));

    expect(saveButton().getAttribute('aria-disabled')).toBe('true');
    saveButton().click();
    expect(saved).toEqual([]);

    component.view!.dispatch({ changes: { from: 0, insert: 'Edited ' } });
    fixture.detectChanges();
    saveButton().click();

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

    expect(saveButton().getAttribute('aria-busy')).toBe('true');
    expect(saveButton().textContent!.trim()).toBe('Saving...');
    saveButton().click();
    expect(saved).toEqual([]);
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

  it('emits cancelled from Cancel', async () => {
    await mount(buildBoard());
    let cancelled = 0;
    component.cancelled.subscribe(() => cancelled++);
    Array.from(host.querySelectorAll('button')).find(b => (b.textContent ?? '').trim() === 'Cancel')!.click();
    expect(cancelled).toBe(1);
  });
});
