import { ComponentFixture, TestBed, fakeAsync, flushMicrotasks, tick } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { SnapshotViewerComponent } from './snapshot-viewer.component';
import { SnapshotTextEditorComponent } from './snapshot-text-editor.component';
import { SnapshotDigestEditorComponent } from './snapshot-digest-editor.component';
import { AdminBenchmarkService, BenchmarkGameSnapshotDto } from '../../services/admin-benchmark.service';
import { Subject, of, throwError } from 'rxjs';
import { buildBoard, snapshotWith } from './snapshot-viewer.spec-fixtures';

describe('SnapshotViewerComponent', () => {
  let component: SnapshotViewerComponent;
  let fixture: ComponentFixture<SnapshotViewerComponent>;
  let mockBenchmarkService: jasmine.SpyObj<AdminBenchmarkService>;
  let getItemSpy: jasmine.Spy;

  beforeEach(async () => {
    getItemSpy = spyOn(Storage.prototype, 'getItem').and.returnValue(null);

    mockBenchmarkService = jasmine.createSpyObj('AdminBenchmarkService', [
      'getSnapshot',
      'getSnapshotTextUrl',
      'updateSnapshot',
      'updateSnapshotText',
      'regenerateSnapshotDigest'
    ]);

    mockBenchmarkService.getSnapshot.and.returnValue(of({
      id: 1,
      name: 'Emergency Low HP',
      charCount: 15000,
      sha256: 'abc1234567890',
      captureMethod: 'client_refresh_snapshot',
      sanitizedText: 'Line 1\nLine 2',
      createdAtUtc: new Date().toISOString()
    }));

    await TestBed.configureTestingModule({
      imports: [SnapshotViewerComponent],
      providers: [
        { provide: AdminBenchmarkService, useValue: mockBenchmarkService }
      ]
    }).compileComponents();

    fixture = TestBed.createComponent(SnapshotViewerComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('should load snapshot when open is called', () => {
    component.open(1);
    expect(mockBenchmarkService.getSnapshot).toHaveBeenCalledWith(1, true);
    expect(component.snapshot?.name).toBe('Emergency Low HP');
  });

  it('should detect truncation marker', () => {
    component.snapshot = {
      id: 1,
      name: 'Test',
      charCount: 100,
      sha256: '123',
      captureMethod: 'test',
      sanitizedText: 'Some text [SNAPSHOT TRUNCATED at 60000 chars]',
      createdAtUtc: new Date().toISOString()
    };
    expect(component.hasTruncationMarker).toBeTrue();
  });

  describe('the toolbar', () => {
    beforeEach(() => {
      component.open(1);
      fixture.detectChanges();
    });

    afterEach(() => component.viewerDialog?.nativeElement?.close());

    function buttonNamed(name: string): HTMLButtonElement | undefined {
      const buttons = Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('button'));
      return buttons.find(b => (b.getAttribute('aria-label') ?? b.textContent ?? '').trim() === name);
    }

    function liveText(selector: string): string {
      return ((fixture.nativeElement as HTMLElement).querySelector(selector)?.textContent ?? '').trim();
    }

    function expectNoEmoji() {
      const buttons = Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('button'));
      for (const button of buttons) {
        expect(/\p{Extended_Pictographic}/u.test(button.textContent ?? '')).withContext(button.textContent ?? '').toBeFalse();
      }
    }

    it('names every action in words, with no emoji', async () => {
      for (const name of ['Copy Text', 'Copy with line numbers', 'Download .snapshot.txt']) {
        const button = buttonNamed(name);
        expect(button).withContext(name).toBeTruthy();
      }
      for (const name of ['Close game snapshot', 'Go to line', 'Previous match in snapshot', 'Next match in snapshot']) {
        expect(buttonNamed(name)).withContext(name).toBeTruthy();
      }
      expectNoEmoji();

      component.selectTab('metadata');
      fixture.detectChanges();
      for (const name of ['Edit Metadata', 'Copy SHA-256']) {
        expect(buttonNamed(name)).withContext(name).toBeTruthy();
      }
      expectNoEmoji();

      buttonNamed('Edit Metadata')!.click();
      fixture.detectChanges();
      await fixture.debugElement.query(By.directive(SnapshotDigestEditorComponent)).componentInstance.ready;
      fixture.detectChanges();
      expect(buttonNamed('Regenerate digest from snapshot')).toBeTruthy();
      expectNoEmoji();
    });

    it('announces a text copy, then clears the announcement', fakeAsync(() => {
      spyOn(navigator.clipboard, 'writeText').and.returnValue(Promise.resolve());

      component.copyText();
      flushMicrotasks();
      fixture.detectChanges();
      expect(liveText('.copy-text-status')).toBe('Copied');
      expect(buttonNamed('Copied')).toBeTruthy();

      tick(2000);
      fixture.detectChanges();
      expect(liveText('.copy-text-status')).toBe('');
      expect(buttonNamed('Copy Text')).toBeTruthy();
    }));

    it('announces a SHA-256 copy', fakeAsync(() => {
      spyOn(navigator.clipboard, 'writeText').and.returnValue(Promise.resolve());
      component.selectTab('metadata');
      fixture.detectChanges();

      component.copySha();
      flushMicrotasks();
      fixture.detectChanges();
      expect(liveText('.copy-sha-status')).toBe('Copied');

      tick(2000);
      fixture.detectChanges();
      expect(liveText('.copy-sha-status')).toBe('');
    }));
  });

  describe('the digest regenerate action', () => {
    let host: HTMLElement;

    const rebuilt = 'Board digest (extract of the snapshot; the map grid and symbol legend are omitted):\nStatus:\nHP:31(44)';

    /* The digest editor loads CodeMirror with import(), which never settles inside fakeAsync,
       so these tests are async and wait for its ready promise. */
    async function openEditing() {
      mockBenchmarkService.getSnapshot.and.returnValue(
        of(snapshotWith(buildBoard(), { digestText: 'the old prefix digest' })));
      component.open(1);
      fixture.detectChanges();
      host = fixture.nativeElement as HTMLElement;
      component.selectTab('metadata');
      fixture.detectChanges();
      clickButtonNamed('Edit Metadata');
      fixture.detectChanges();
      await digestEditor().ready;
      fixture.detectChanges();
    }

    function digestEditor(): SnapshotDigestEditorComponent {
      return fixture.debugElement.query(By.directive(SnapshotDigestEditorComponent)).componentInstance;
    }

    function digestText(): string {
      return digestEditor().view!.state.doc.toString();
    }

    /* The form is opened and closed by clicking its own buttons: a property set from the test
       leaves the view unchecked, exactly as it would in the running app. */
    function clickButtonNamed(name: string) {
      Array.from(host.querySelectorAll('button'))
        .find(b => (b.textContent ?? '').trim() === name)!
        .click();
      fixture.detectChanges();
    }

    function regenerateButton(): HTMLButtonElement {
      return host.querySelector<HTMLButtonElement>('.digest-editor .regenerate-btn')!;
    }

    afterEach(() => component.viewerDialog?.nativeElement?.close());

    it('rebuilds the digest and shows it in the editor and the disclosure', async () => {
      await openEditing();
      expect(digestText()).toBe('the old prefix digest');
      mockBenchmarkService.regenerateSnapshotDigest.and.returnValue(
        of(snapshotWith(buildBoard(), { digestText: rebuilt })));

      regenerateButton().click();
      fixture.detectChanges();

      expect(mockBenchmarkService.regenerateSnapshotDigest).toHaveBeenCalledWith(1);
      expect(digestText()).toBe(rebuilt);
      expect(component.snapshot!.digestText).toBe(rebuilt);

      clickButtonNamed('Cancel');
      const disclosure = host.querySelector<HTMLDetailsElement>('details.digest-disclosure')!;
      expect(disclosure.textContent).toContain('the map grid and symbol legend are omitted');
    });

    it('marks the button busy and refuses a second click while the request is pending', async () => {
      await openEditing();
      const pending = new Subject<BenchmarkGameSnapshotDto>();
      mockBenchmarkService.regenerateSnapshotDigest.and.returnValue(pending.asObservable());

      regenerateButton().click();
      fixture.detectChanges();
      expect(regenerateButton().getAttribute('aria-disabled')).toBe('true');
      expect(regenerateButton().getAttribute('aria-label')).toBe('Regenerate digest from snapshot');

      regenerateButton().click();
      expect(mockBenchmarkService.regenerateSnapshotDigest).toHaveBeenCalledTimes(1);

      pending.next(snapshotWith(buildBoard(), { digestText: rebuilt }));
      pending.complete();
      fixture.detectChanges();

      expect(regenerateButton().getAttribute('aria-disabled')).toBeNull();
      expect(regenerateButton().getAttribute('aria-label')).toBe('Regenerate digest from snapshot');
    });
  });

  describe('editing the text', () => {
    let host: HTMLElement;

    function openWith(snapshot: BenchmarkGameSnapshotDto) {
      mockBenchmarkService.getSnapshot.and.returnValue(of(snapshot));
      component.open(1);
      fixture.detectChanges();
      host = fixture.nativeElement as HTMLElement;
    }

    function clickButtonNamed(name: string) {
      Array.from(host.querySelectorAll('button'))
        .find(b => (b.textContent ?? '').trim() === name)!
        .click();
      fixture.detectChanges();
    }

    function editor(): SnapshotTextEditorComponent {
      return fixture.debugElement.query(By.directive(SnapshotTextEditorComponent)).componentInstance;
    }

    function tab(id: string): HTMLButtonElement {
      return host.querySelector<HTMLButtonElement>(`[role="tab"][id^="snapshot-tab-${id}-"]`)!;
    }

    async function openEditor() {
      openWith(snapshotWith(buildBoard()));
      tab('editor').click();
      fixture.detectChanges();
      await editor().ready;
      fixture.detectChanges();
    }

    afterEach(() => component.viewerDialog?.nativeElement?.close());

    it('mounts the editor in place of the reader', async () => {
      await openEditor();
      expect(host.querySelector('app-snapshot-text-editor')).toBeTruthy();
      expect(host.querySelector('.reader-scroll')).toBeNull();
      expect(host.querySelector('.snapshot-edit-panel')).toBeNull();
    });

    it('saves with the loaded SHA-256 and re-renders the returned text', async () => {
      const updatedSpy = jasmine.createSpy('snapshotUpdated');
      component.snapshotUpdated.subscribe(updatedSpy);
      await openEditor();
      mockBenchmarkService.updateSnapshotText.and.returnValue(
        of(snapshotWith('one\ntwo\nthree', { sha256: 'def0987654321' })));

      editor().save.emit('one\ntwo\nthree');
      fixture.detectChanges();

      expect(mockBenchmarkService.updateSnapshotText).toHaveBeenCalledWith(1, {
        text: 'one\ntwo\nthree',
        expectedSha256: 'abc1234567890'
      });
      expect(host.querySelector('app-snapshot-text-editor')).toBeNull();
      expect(component.lineCount).toBe(3);
      expect(host.querySelectorAll('.reader-line').length).toBe(3);
      component.selectTab('metadata');
      fixture.detectChanges();
      expect(host.querySelector('.sha-box')!.textContent).toContain('def0987654321');
      expect(updatedSpy).toHaveBeenCalledWith(jasmine.objectContaining({ charCount: 13, sha256: 'def0987654321' }));
    });

    it('keeps the editor open and shows the server message when the save fails', async () => {
      await openEditor();
      const message = 'The snapshot text was changed by someone else since it was loaded. Reload the snapshot and reapply your edit.';
      mockBenchmarkService.updateSnapshotText.and.returnValue(throwError(() => ({ status: 409, error: { error: message } })));

      editor().save.emit('edited');
      fixture.detectChanges();

      expect(host.querySelector('app-snapshot-text-editor')).toBeTruthy();
      expect(host.querySelector('.editor-error')!.textContent).toContain(message);
      expect(component.savingText).toBeFalse();
    });

    it('does not close the dialog on Escape while there are unsaved changes', async () => {
      await openEditor();
      editor().dirtyChange.emit(true);
      fixture.detectChanges();

      const cancel = new Event('cancel', { cancelable: true });
      component.viewerDialog.nativeElement.dispatchEvent(cancel);
      fixture.detectChanges();

      expect(cancel.defaultPrevented).toBeTrue();
      expect(component.viewerDialog.nativeElement.open).toBeTrue();
      expect(host.querySelector('.discard-strip')).toBeTruthy();

      clickButtonNamed('Discard');
      expect(host.querySelector('app-snapshot-text-editor')).toBeNull();
      expect(host.querySelector('.reader-scroll')).toBeTruthy();
    });

    it('closes the editor at once when Cancel is pressed with no changes', async () => {
      await openEditor();
      clickButtonNamed('Cancel');
      expect(host.querySelector('app-snapshot-text-editor')).toBeNull();
      expect(host.querySelector('.discard-strip')).toBeNull();
      expect(component.activeTab).toBe('viewer');
    });
  });

  describe('the tabs', () => {
    let host: HTMLElement;

    function openWith(snapshot: BenchmarkGameSnapshotDto) {
      mockBenchmarkService.getSnapshot.and.returnValue(of(snapshot));
      component.open(1);
      fixture.detectChanges();
      host = fixture.nativeElement as HTMLElement;
    }

    function tabs(): HTMLButtonElement[] {
      return Array.from(host.querySelectorAll<HTMLButtonElement>('[role="tab"]'));
    }

    function tab(id: string): HTMLButtonElement {
      return host.querySelector<HTMLButtonElement>(`[role="tab"][id^="snapshot-tab-${id}-"]`)!;
    }

    function key(target: HTMLElement, name: string) {
      target.dispatchEvent(new KeyboardEvent('keydown', { key: name, bubbles: true, cancelable: true }));
      fixture.detectChanges();
    }

    function editor(): SnapshotTextEditorComponent {
      return fixture.debugElement.query(By.directive(SnapshotTextEditorComponent)).componentInstance;
    }

    function clickButtonNamed(name: string) {
      Array.from(host.querySelectorAll('button'))
        .find(b => (b.textContent ?? '').trim() === name)!
        .click();
      fixture.detectChanges();
    }

    afterEach(() => component.viewerDialog?.nativeElement?.close());

    it('exposes a tab list with one selected tab and its labelled panel', () => {
      openWith(snapshotWith(buildBoard()));
      const list = host.querySelector('[role="tablist"]')!;
      expect(list.getAttribute('aria-label')).toBe('Game snapshot sections');
      expect(tabs().map(t => (t.textContent ?? '').trim())).toEqual(['Viewer', 'Editor', 'Metadata']);

      const selected = tabs().filter(t => t.getAttribute('aria-selected') === 'true');
      expect(selected.length).toBe(1);
      expect(selected[0].getAttribute('tabindex')).toBe('0');
      for (const other of tabs().filter(t => t !== selected[0])) {
        expect(other.getAttribute('tabindex')).toBe('-1');
      }

      const panels = host.querySelectorAll<HTMLElement>('[role="tabpanel"]');
      expect(panels.length).toBe(1);
      expect(panels[0].getAttribute('tabindex')).toBe('0');
      expect(panels[0].getAttribute('aria-labelledby')).toBe(selected[0].id);
      expect(selected[0].getAttribute('aria-controls')).toBe(panels[0].id);
    });

    it('moves selection and focus with the arrow, Home and End keys', () => {
      openWith(snapshotWith(buildBoard()));

      tab('viewer').focus();
      key(tab('viewer'), 'ArrowRight');
      expect(component.activeTab).toBe('editor');
      expect(document.activeElement).toBe(tab('editor'));

      key(tab('editor'), 'Home');
      expect(component.activeTab).toBe('viewer');
      expect(document.activeElement).toBe(tab('viewer'));

      key(tab('viewer'), 'ArrowLeft');
      expect(component.activeTab).toBe('metadata');
      expect(document.activeElement).toBe(tab('metadata'));

      key(tab('metadata'), 'Home');
      key(tab('viewer'), 'End');
      expect(component.activeTab).toBe('metadata');
      expect(document.activeElement).toBe(tab('metadata'));
    });

    it('shows the Viewer tab again when reopened', () => {
      openWith(snapshotWith(buildBoard()));
      tab('metadata').click();
      fixture.detectChanges();
      expect(component.activeTab).toBe('metadata');

      component.viewerDialog.nativeElement.close();
      openWith(snapshotWith(buildBoard()));
      expect(component.activeTab).toBe('viewer');
      expect(tab('viewer').getAttribute('aria-selected')).toBe('true');
      expect(host.querySelector('.reader-scroll')).toBeTruthy();
    });

    it('asks before leaving the editor with unsaved changes', async () => {
      openWith(snapshotWith(buildBoard()));
      tab('editor').click();
      fixture.detectChanges();
      await editor().ready;
      fixture.detectChanges();
      editor().dirtyChange.emit(true);
      fixture.detectChanges();

      tab('metadata').click();
      fixture.detectChanges();
      expect(host.querySelector('.discard-strip')).toBeTruthy();
      expect(component.activeTab).toBe('editor');
      expect((document.activeElement?.textContent ?? '').trim()).toBe('Keep editing');

      clickButtonNamed('Keep editing');
      expect(host.querySelector('.discard-strip')).toBeNull();
      expect(host.querySelector('app-snapshot-text-editor')).toBeTruthy();
      expect(component.activeTab).toBe('editor');

      tab('metadata').click();
      fixture.detectChanges();
      clickButtonNamed('Discard');
      expect(component.activeTab).toBe('metadata');
      expect(host.querySelector('app-snapshot-text-editor')).toBeNull();
      expect(host.querySelector('.sha-fact')).toBeTruthy();
    });

    it('has no footer and no Edit Text button on any tab', () => {
      openWith(snapshotWith(buildBoard()));
      expect(host.querySelector('.benchmark-snapshot-viewer-dialog > .dialog-actions')).toBeNull();
      for (const id of ['viewer', 'editor', 'metadata']) {
        component.selectTab(id as 'viewer' | 'editor' | 'metadata');
        fixture.detectChanges();
        const names = Array.from(host.querySelectorAll('button')).map(b => (b.textContent ?? '').trim());
        expect(names).withContext(id).not.toContain('Edit Text');
      }
    });
  });

  describe('the reader', () => {
    let host: HTMLElement;

    function openWith(snapshot: BenchmarkGameSnapshotDto) {
      mockBenchmarkService.getSnapshot.and.returnValue(of(snapshot));
      component.open(1);
      fixture.detectChanges();
      host = fixture.nativeElement as HTMLElement;
    }

    function line(n: number): HTMLElement {
      return host.querySelector<HTMLElement>(`.reader-line[data-ln="${n}"]`)!;
    }

    function heroRowCenter(): { row: HTMLElement; text: Text; x: number; y: number } {
      const rowIndex = component.mapBlock!.rowByY.get(13)!;
      const row = line(rowIndex + 1);
      const text = row.firstChild as Text;
      const range = document.createRange();
      range.setStart(text, 13);
      range.setEnd(text, 14);
      const box = range.getBoundingClientRect();
      return { row, text, x: box.left + box.width / 2, y: box.top + box.height / 2 };
    }

    afterEach(() => component.viewerDialog?.nativeElement?.close());

    it('renders a 250-line board as three numbered chunks', () => {
      openWith(snapshotWith(Array.from({ length: 250 }, (_, i) => `row ${i + 1}`).join('\n')));
      const chunks = host.querySelectorAll('.reader-chunk');
      expect(chunks.length).toBe(3);
      const lines = host.querySelectorAll<HTMLElement>('.reader-line');
      expect(lines.length).toBe(250);
      expect(lines[0].dataset['ln']).toBe('1');
      expect(lines[249].dataset['ln']).toBe('250');
      expect(lines[0].textContent).toBe('row 1');
    });

    it('is a focusable, named region', () => {
      openWith(snapshotWith(buildBoard()));
      const region = host.querySelector<HTMLElement>('.reader-scroll')!;
      expect(region.getAttribute('role')).toBe('region');
      expect(region.getAttribute('aria-label')).toBe('Snapshot text of Emergency Low HP');
      expect(region.getAttribute('tabindex')).toBe('0');
    });

    it('toggles line numbers and wrapping, and remembers both', fakeAsync(() => {
      const setItem = spyOn(Storage.prototype, 'setItem');
      openWith(snapshotWith(buildBoard()));
      /* ngModel writes the checkboxes' initial checked state in a microtask. */
      flushMicrotasks();
      fixture.detectChanges();
      const region = host.querySelector<HTMLElement>('.reader-scroll')!;
      expect(region.classList.contains('no-ln')).toBeFalse();
      expect(region.classList.contains('wrap')).toBeFalse();

      host.querySelector<HTMLInputElement>('.line-numbers-toggle')!.click();
      host.querySelector<HTMLInputElement>('.wrap-toggle')!.click();
      fixture.detectChanges();

      expect(region.classList.contains('no-ln')).toBeTrue();
      expect(region.classList.contains('wrap')).toBeTrue();
      expect(setItem).toHaveBeenCalledWith('overseer.snapshotReader.lineNumbers', '0');
      expect(setItem).toHaveBeenCalledWith('overseer.snapshotReader.wrap', '1');
    }));

    it('falls back to the defaults when storage throws', () => {
      getItemSpy.and.throwError(new Error('storage denied'));
      const second = TestBed.createComponent(SnapshotViewerComponent);
      second.detectChanges();
      expect(second.componentInstance.showLineNumbers).toBeTrue();
      expect(second.componentInstance.wrapLines).toBeFalse();
      second.destroy();
    });

    it('keeps the digest in a closed disclosure, and omits it when absent', () => {
      openWith(snapshotWith(buildBoard(), { digestText: 'Hero at low HP beside a fountain.' }));
      component.selectTab('metadata');
      fixture.detectChanges();
      const digest = host.querySelector<HTMLDetailsElement>('details.digest-disclosure');
      expect(digest).toBeTruthy();
      expect(digest!.open).toBeFalse();

      component.viewerDialog.nativeElement.close();
      openWith(snapshotWith(buildBoard()));
      component.selectTab('metadata');
      fixture.detectChanges();
      expect(host.querySelector('details.digest-disclosure')).toBeNull();
    });

    it('lists the map grid among the sections', () => {
      openWith(snapshotWith(buildBoard()));
      const options = Array.from(host.querySelectorAll<HTMLOptionElement>('.section-select option')).map(o => o.textContent ?? '');
      expect(options.some(o => o.startsWith('Map grid:'))).toBeTrue();
      expect(options.some(o => o.startsWith('Inventory:'))).toBeTrue();
    });

    it('marks the target line after going to it', () => {
      openWith(snapshotWith(buildBoard()));
      component.goToLine(200);
      expect(line(200).classList.contains('is-target')).toBeTrue();
      expect(host.querySelectorAll('.is-target').length).toBe(1);
    });

    it('finds text, reports the count, and steps between matches', fakeAsync(() => {
      openWith(snapshotWith(buildBoard()));
      const input = host.querySelector<HTMLInputElement>('.find-input')!;
      input.value = 'ration';
      input.dispatchEvent(new Event('input'));
      tick(150);
      fixture.detectChanges();

      expect(host.querySelector('.find-count')!.textContent!.trim()).toBe('1 of 2');
      expect(component.targetLine).toBe(3);
      if ('highlights' in CSS) {
        expect(CSS.highlights.has('reader-find')).toBeTrue();
      }

      component.stepMatch(1);
      fixture.detectChanges();
      expect(host.querySelector('.find-count')!.textContent!.trim()).toBe('2 of 2');
      expect(component.targetLine).toBe(30);

      input.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', cancelable: true }));
      fixture.detectChanges();
      expect(host.querySelector('.find-count')!.textContent!.trim()).toBe('');
      tick(2000);
    }));

    it('reads the map cell under the pointer as <x,y> and its symbol', () => {
      openWith(snapshotWith(buildBoard()));
      const { text, x, y } = heroRowCenter();
      spyOn(document, 'caretPositionFromPoint').and.returnValue({ offsetNode: text, offset: 13 } as unknown as CaretPosition);

      component.updateCellReadout(x, y);
      expect(component.cellReadout).toBe('<10,13>  \'@\'');
    });

    it('copies the coordinate of a clicked map cell', () => {
      openWith(snapshotWith(buildBoard()));
      const writeText = spyOn(navigator.clipboard, 'writeText').and.returnValue(Promise.resolve());
      const { row, text, x, y } = heroRowCenter();
      spyOn(document, 'caretPositionFromPoint').and.returnValue({ offsetNode: text, offset: 13 } as unknown as CaretPosition);
      window.getSelection()?.removeAllRanges();

      row.dispatchEvent(new MouseEvent('click', { bubbles: true, clientX: x, clientY: y }));
      expect(writeText).toHaveBeenCalledWith('<10,13>');
    });

    it('copies the whole board with line numbers when nothing is selected', () => {
      openWith(snapshotWith(buildBoard()));
      const writeText = spyOn(navigator.clipboard, 'writeText').and.returnValue(Promise.resolve());
      window.getSelection()?.removeAllRanges();

      component.copyWithLineNumbers();
      const copied = writeText.calls.mostRecent().args[0] as string;
      expect(copied.startsWith('L  1: Map:\n')).toBeTrue();
      expect(copied.split('\n').length).toBe(250);
    });
  });
});
