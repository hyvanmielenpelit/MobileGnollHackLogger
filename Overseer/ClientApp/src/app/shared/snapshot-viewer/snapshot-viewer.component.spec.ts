import { ComponentFixture, TestBed } from '@angular/core/testing';
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
  let host: HTMLElement;

  beforeEach(async () => {
    spyOn(Storage.prototype, 'getItem').and.returnValue(null);

    mockBenchmarkService = jasmine.createSpyObj('AdminBenchmarkService', [
      'getSnapshot',
      'getSnapshotTextUrl',
      'updateSnapshot',
      'updateSnapshotText',
      'regenerateSnapshotDigest',
      'deleteSnapshot'
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
    mockBenchmarkService.getSnapshotTextUrl.and.callFake((id: number) => `/api/admin/benchmark/snapshots/${id}/text`);

    await TestBed.configureTestingModule({
      imports: [SnapshotViewerComponent],
      providers: [
        { provide: AdminBenchmarkService, useValue: mockBenchmarkService }
      ]
    }).compileComponents();

    fixture = TestBed.createComponent(SnapshotViewerComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
    host = fixture.nativeElement as HTMLElement;
  });

  afterEach(() => {
    component.deleteConfirmDialog?.nativeElement?.close();
    component.downloadConfirmDialog?.nativeElement?.close();
    component.viewerDialog?.nativeElement?.close();
  });

  function openWith(snapshot: BenchmarkGameSnapshotDto) {
    mockBenchmarkService.getSnapshot.and.returnValue(of(snapshot));
    component.open(1);
    fixture.detectChanges();
  }

  /* The editors load CodeMirror with import(), which never settles inside fakeAsync, so tests
     that need them are async and wait for their ready promises. */
  async function openReady(snapshot: BenchmarkGameSnapshotDto = snapshotWith(buildBoard())) {
    openWith(snapshot);
    await textEditor().ready;
    await digestEditor().ready;
    fixture.detectChanges();
  }

  function textEditor(): SnapshotTextEditorComponent {
    return fixture.debugElement.query(By.directive(SnapshotTextEditorComponent)).componentInstance;
  }

  function digestEditor(): SnapshotDigestEditorComponent {
    return fixture.debugElement.query(By.directive(SnapshotDigestEditorComponent)).componentInstance;
  }

  function buttons(): HTMLButtonElement[] {
    return Array.from(host.querySelectorAll('button'));
  }

  function buttonNamed(name: string): HTMLButtonElement | undefined {
    return buttons().find(b => (b.getAttribute('aria-label') ?? b.textContent ?? '').replace(/\s+/g, ' ').trim() === name);
  }

  function clickButtonNamed(name: string) {
    buttonNamed(name)!.click();
    fixture.detectChanges();
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

  function typeInto(input: HTMLInputElement, value: string) {
    input.value = value;
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();
  }

  function nameInput(): HTMLInputElement {
    return host.querySelector<HTMLInputElement>('#editBoardName')!;
  }

  function editText(insert = 'Edited ') {
    textEditor().view!.dispatch({ changes: { from: 0, insert } });
    fixture.detectChanges();
  }

  function discardLabel(): string {
    return (host.querySelector('.discard-label')?.textContent ?? '').trim();
  }

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

  describe('the tabs', () => {
    it('exposes a tab list of two tabs, each controlling a panel, with one panel shown', () => {
      openWith(snapshotWith(buildBoard()));
      const list = host.querySelector('[role="tablist"]')!;
      expect(list.getAttribute('aria-label')).toBe('Game snapshot sections');
      expect(tabs().map(t => (t.textContent ?? '').trim())).toEqual(['Game Snapshot', 'Metadata']);

      const selected = tabs().filter(t => t.getAttribute('aria-selected') === 'true');
      expect(selected.length).toBe(1);
      expect(selected[0]).toBe(tab('snapshot'));
      expect(selected[0].getAttribute('tabindex')).toBe('0');
      expect(tab('metadata').getAttribute('tabindex')).toBe('-1');

      const panels = Array.from(host.querySelectorAll<HTMLElement>('[role="tabpanel"]'));
      expect(panels.length).toBe(2);
      for (const t of tabs()) {
        const panel = document.getElementById(t.getAttribute('aria-controls')!)!;
        expect(panels).toContain(panel);
        expect(panel.getAttribute('aria-labelledby')).toBe(t.id);
        expect(panel.getAttribute('tabindex')).toBe('0');
      }
      const shown = panels.filter(p => !p.hidden);
      expect(shown.length).toBe(1);
      expect(shown[0].getAttribute('aria-labelledby')).toBe(selected[0].id);
      expect(getComputedStyle(panels.find(p => p.hidden)!).display).toBe('none');
    });

    it('moves selection and focus with the arrow, Home and End keys', () => {
      openWith(snapshotWith(buildBoard()));

      tab('snapshot').focus();
      key(tab('snapshot'), 'ArrowRight');
      expect(component.activeTab).toBe('metadata');
      expect(document.activeElement).toBe(tab('metadata'));

      key(tab('metadata'), 'ArrowRight');
      expect(component.activeTab).toBe('snapshot');
      expect(document.activeElement).toBe(tab('snapshot'));

      key(tab('snapshot'), 'ArrowLeft');
      expect(component.activeTab).toBe('metadata');

      key(tab('metadata'), 'Home');
      expect(component.activeTab).toBe('snapshot');
      expect(document.activeElement).toBe(tab('snapshot'));

      key(tab('snapshot'), 'End');
      expect(component.activeTab).toBe('metadata');
      expect(document.activeElement).toBe(tab('metadata'));
    });

    it('shows the Game Snapshot tab again when reopened', () => {
      openWith(snapshotWith(buildBoard()));
      tab('metadata').click();
      fixture.detectChanges();
      expect(component.activeTab).toBe('metadata');

      component.close();
      openWith(snapshotWith(buildBoard()));
      expect(component.activeTab).toBe('snapshot');
      expect(tab('snapshot').getAttribute('aria-selected')).toBe('true');
    });

    it('keeps unsaved text edits across a tab switch, without asking', async () => {
      await openReady();
      const editor = textEditor();
      editText();

      tab('metadata').click();
      fixture.detectChanges();
      expect(host.querySelector('.discard-strip')).toBeNull();
      expect(component.activeTab).toBe('metadata');

      tab('snapshot').click();
      fixture.detectChanges();
      expect(textEditor()).toBe(editor);
      expect(editor.dirty).toBeTrue();
      expect(editor.currentText()!.startsWith('Edited Map:')).toBeTrue();
    });

    it('names every action in words, with no emoji, on both tabs and in the download confirmation', async () => {
      await openReady();

      function expectNoEmoji() {
        for (const button of buttons()) {
          expect(/\p{Extended_Pictographic}/u.test(button.textContent ?? '')).withContext(button.textContent ?? '').toBeFalse();
        }
      }

      for (const name of ['Close game snapshot', 'Find', 'Copy Text', 'Copy with line numbers',
                          'Download .snapshot.txt of Emergency Low HP', 'Revert', 'Save Text']) {
        expect(buttonNamed(name)).withContext(name).toBeTruthy();
      }
      expectNoEmoji();

      component.selectTab('metadata');
      fixture.detectChanges();
      for (const name of ['Copy SHA-256', 'Regenerate digest from snapshot', 'Save Changes']) {
        expect(buttonNamed(name)).withContext(name).toBeTruthy();
      }
      expect(buttonNamed('Edit Metadata')).toBeUndefined();
      expectNoEmoji();

      for (const name of ['Cancel', 'Download saved text', 'Save and download']) {
        expect(buttonNamed(name)).withContext(name).toBeTruthy();
      }
    });
  });

  describe('deleting the snapshot', () => {
    it('offers Delete Snapshot on the Metadata tab, or the blocked reason instead', async () => {
      await openReady(snapshotWith(buildBoard(), { suiteName: 'Board Suite' }));
      component.selectTab('metadata');
      fixture.detectChanges();

      const panel = host.querySelector('.snapshot-panel-metadata')!;
      expect(panel.querySelector('.delete-snapshot-btn')!.textContent!.trim()).toBe('Delete Snapshot');
      expect(panel.querySelector('.remove-snapshot')!.textContent).toContain('detaches it from suite Board Suite');

      fixture.componentRef.setInput('deleteBlockedReason', 'A question generation job is running on this suite.');
      fixture.detectChanges();
      expect(panel.querySelector('.delete-snapshot-btn')).toBeNull();
      expect(panel.querySelector('.remove-snapshot')!.textContent).toContain('generation job is running');
    });

    it('deletes on confirm, emits the id and closes', async () => {
      mockBenchmarkService.deleteSnapshot.and.returnValue(of(undefined));
      const deleted = jasmine.createSpy('snapshotDeleted');
      component.snapshotDeleted.subscribe(deleted);
      await openReady();
      component.selectTab('metadata');
      fixture.detectChanges();

      host.querySelector<HTMLButtonElement>('.delete-snapshot-btn')!.click();
      fixture.detectChanges();
      expect(component.deleteConfirmDialog.nativeElement.open).toBeTrue();
      expect(mockBenchmarkService.deleteSnapshot).not.toHaveBeenCalled();

      host.querySelector<HTMLButtonElement>('.confirm-delete-snapshot-btn')!.click();
      fixture.detectChanges();

      expect(mockBenchmarkService.deleteSnapshot).toHaveBeenCalledWith(1);
      expect(deleted).toHaveBeenCalledWith(1);
      expect(component.deleteConfirmDialog.nativeElement.open).toBeFalse();
      expect(component.viewerDialog.nativeElement.open).toBeFalse();
    });

    it('keeps the confirmation open with the message when the delete fails', async () => {
      mockBenchmarkService.deleteSnapshot.and.returnValue(throwError(() => ({ error: { error: 'Snapshot is locked.' } })));
      const deleted = jasmine.createSpy('snapshotDeleted');
      component.snapshotDeleted.subscribe(deleted);
      await openReady();

      component.requestDelete();
      fixture.detectChanges();
      component.confirmDelete();
      fixture.detectChanges();

      expect(component.deleteConfirmDialog.nativeElement.open).toBeTrue();
      expect(host.querySelector('.snapshot-delete-confirm [role="alert"]')!.textContent).toContain('Snapshot is locked.');
      expect(deleted).not.toHaveBeenCalled();
    });
  });

  describe('the metadata page', () => {
    it('shows the provenance strip above the fields', async () => {
      await openReady(snapshotWith(buildBoard(), { charCount: 12345, sha256: 'feedface1234' }));
      await fixture.whenStable();
      const strip = host.querySelector<HTMLElement>('.meta-strip')!;
      expect(strip).toBeTruthy();
      const facts = Array.from(strip.querySelectorAll('dt')).map(dt => (dt.textContent ?? '').trim());
      expect(facts).toEqual(['Capture', 'Size', 'Captured', 'Source chat', 'SHA-256']);
      expect(strip.textContent).toContain('client_refresh_snapshot');
      expect(strip.textContent).toContain('12,345 chars');
      expect(strip.querySelector('.sha-box')!.textContent).toBe('feedface1234');
      expect(strip.querySelector('button[aria-label="Copy SHA-256"]')).toBeTruthy();
      expect(nameInput().value).toBe('Emergency Low HP');
    });

    it('announces a SHA-256 copy', async () => {
      const writeText = spyOn(navigator.clipboard, 'writeText').and.returnValue(Promise.resolve());
      await openReady();

      component.copySha();
      await fixture.whenStable();
      fixture.detectChanges();
      expect(writeText).toHaveBeenCalledWith('abc1234567890');
      expect((host.querySelector('.copy-sha-status')?.textContent ?? '').trim()).toBe('Copied');
    });

    it('enables Save Changes and Revert once a field changes, and Revert restores it', async () => {
      await openReady();
      await fixture.whenStable();
      const save = buttonNamed('Save Changes')!;
      const revert = host.querySelector<HTMLButtonElement>('.revert-metadata-btn')!;
      expect(save.getAttribute('aria-disabled')).toBe('true');
      expect(revert.getAttribute('aria-disabled')).toBe('true');

      typeInto(nameInput(), 'Renamed');
      expect(component.metadataDirty).toBeTrue();
      expect(save.getAttribute('aria-disabled')).toBeNull();
      expect(revert.getAttribute('aria-disabled')).toBeNull();

      revert.click();
      fixture.detectChanges();
      await fixture.whenStable();
      fixture.detectChanges();
      expect(component.editName).toBe('Emergency Low HP');
      expect(nameInput().value).toBe('Emergency Low HP');
      expect(save.getAttribute('aria-disabled')).toBe('true');
      expect(revert.getAttribute('aria-disabled')).toBe('true');
    });

    it('saves the form, re-initialises it from the response and shows Saved.', async () => {
      const updatedSpy = jasmine.createSpy('snapshotUpdated');
      component.snapshotUpdated.subscribe(updatedSpy);
      await openReady();
      await fixture.whenStable();
      mockBenchmarkService.updateSnapshot.and.returnValue(of(snapshotWith(buildBoard(), { name: 'Renamed' })));

      typeInto(nameInput(), '  Renamed  ');
      buttonNamed('Save Changes')!.click();
      fixture.detectChanges();

      expect(mockBenchmarkService.updateSnapshot).toHaveBeenCalledWith(1, jasmine.objectContaining({ name: 'Renamed' }));
      expect(component.snapshot!.name).toBe('Renamed');
      expect(component.editName).toBe('Renamed');
      expect(component.metadataDirty).toBeFalse();
      expect(host.querySelector('.form-status .is-ok')!.textContent).toContain('Saved.');
      expect(updatedSpy).toHaveBeenCalledWith(jasmine.objectContaining({ name: 'Renamed' }));
    });

    it('refuses to save an empty name', async () => {
      await openReady();
      await fixture.whenStable();
      typeInto(nameInput(), '   ');
      buttonNamed('Save Changes')!.click();
      fixture.detectChanges();
      expect(mockBenchmarkService.updateSnapshot).not.toHaveBeenCalled();
      expect(host.querySelector('.form-status .is-error')!.textContent).toContain('Snapshot name is required.');
    });
  });

  describe('the digest regenerate action', () => {
    const rebuilt = 'Board digest (extract of the snapshot; the map grid and symbol legend are omitted):\nStatus:\nHP:31(44)';

    function digestText(): string {
      return digestEditor().view!.state.doc.toString();
    }

    function regenerateButton(): HTMLButtonElement {
      return host.querySelector<HTMLButtonElement>('.digest-editor .regenerate-btn')!;
    }

    it('rebuilds the digest, puts it in the editor and keeps the form clean', async () => {
      await openReady(snapshotWith(buildBoard(), { digestText: 'the old prefix digest' }));
      component.selectTab('metadata');
      fixture.detectChanges();
      expect(digestText()).toBe('the old prefix digest');
      mockBenchmarkService.regenerateSnapshotDigest.and.returnValue(
        of(snapshotWith(buildBoard(), { digestText: rebuilt })));

      regenerateButton().click();
      fixture.detectChanges();

      expect(mockBenchmarkService.regenerateSnapshotDigest).toHaveBeenCalledWith(1);
      expect(digestText()).toBe(rebuilt);
      expect(component.snapshot!.digestText).toBe(rebuilt);
      expect(component.metadataDirty).toBeFalse();
    });

    it('marks the button busy and refuses a second click while the request is pending', async () => {
      await openReady();
      component.selectTab('metadata');
      fixture.detectChanges();
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
    });
  });

  describe('editing the text', () => {
    it('saves with the loaded SHA-256 and stays on the tab with the saved text clean', async () => {
      const updatedSpy = jasmine.createSpy('snapshotUpdated');
      component.snapshotUpdated.subscribe(updatedSpy);
      await openReady();
      editText('one\ntwo\n');
      mockBenchmarkService.updateSnapshotText.and.returnValue(
        of(snapshotWith('one\ntwo\nthree', { sha256: 'def0987654321', digestText: 'rebuilt digest' })));

      textEditor().save.emit(textEditor().currentText()!);
      fixture.detectChanges();

      expect(mockBenchmarkService.updateSnapshotText).toHaveBeenCalledWith(1, {
        text: jasmine.stringMatching(/^one\ntwo\nMap:/),
        expectedSha256: 'abc1234567890'
      });
      expect(component.activeTab).toBe('snapshot');
      expect(textEditor().currentText()).toBe('one\ntwo\nthree');
      expect(textEditor().dirty).toBeFalse();
      expect(component.editingTextDirty).toBeFalse();
      expect(host.querySelector('.editor-error .is-ok')!.textContent).toContain('Saved. SHA-256 and digest updated.');
      expect(component.editDigestText).toBe('rebuilt digest');
      expect(host.querySelector('.sha-box')!.textContent).toContain('def0987654321');
      expect(updatedSpy).toHaveBeenCalledWith(jasmine.objectContaining({ charCount: 13, sha256: 'def0987654321' }));
    });

    it('keeps a digest edit in progress when the text is saved', async () => {
      await openReady();
      component.editDigestText = 'my digest edit';
      editText();
      mockBenchmarkService.updateSnapshotText.and.returnValue(
        of(snapshotWith('saved', { digestText: 'rebuilt digest' })));

      textEditor().save.emit('saved');
      fixture.detectChanges();
      expect(component.editDigestText).toBe('my digest edit');
    });

    it('keeps the buffer and shows the server message when the save fails', async () => {
      await openReady();
      editText();
      const message = 'The snapshot text was changed by someone else since it was loaded. Reload the snapshot and reapply your edit.';
      mockBenchmarkService.updateSnapshotText.and.returnValue(throwError(() => ({ status: 409, error: { error: message } })));

      textEditor().save.emit(textEditor().currentText()!);
      fixture.detectChanges();

      expect(host.querySelector('.editor-error')!.textContent).toContain(message);
      expect(textEditor().dirty).toBeTrue();
      expect(component.savingText).toBeFalse();
    });
  });

  describe('closing', () => {
    it('closes at once when nothing is unsaved', async () => {
      await openReady();
      clickButtonNamed('Close game snapshot');
      expect(component.viewerDialog.nativeElement.open).toBeFalse();
      expect(host.querySelector('app-snapshot-text-editor')).toBeNull();
    });

    it('asks before closing with unsaved text; Keep editing stays, Discard closes', async () => {
      await openReady();
      editText();

      clickButtonNamed('Close game snapshot');
      expect(component.viewerDialog.nativeElement.open).toBeTrue();
      expect(discardLabel()).toBe('Discard unsaved changes to the snapshot text?');
      expect((document.activeElement?.textContent ?? '').trim()).toBe('Keep editing');

      clickButtonNamed('Keep editing');
      expect(host.querySelector('.discard-strip')).toBeNull();
      expect(component.viewerDialog.nativeElement.open).toBeTrue();
      expect(textEditor().view!.hasFocus).toBeTrue();

      clickButtonNamed('Close game snapshot');
      clickButtonNamed('Discard');
      expect(component.viewerDialog.nativeElement.open).toBeFalse();
      expect(host.querySelector('app-snapshot-text-editor')).toBeNull();
    });

    it('does not close on Escape while there are unsaved changes', async () => {
      await openReady();
      editText();

      const cancel = new Event('cancel', { cancelable: true });
      component.viewerDialog.nativeElement.dispatchEvent(cancel);
      fixture.detectChanges();

      expect(cancel.defaultPrevented).toBeTrue();
      expect(component.viewerDialog.nativeElement.open).toBeTrue();
      expect(host.querySelector('.discard-strip')).toBeTruthy();
    });

    it('names the metadata, or both, in the strip', async () => {
      await openReady();
      component.selectTab('metadata');
      fixture.detectChanges();
      typeInto(nameInput(), 'Renamed');

      clickButtonNamed('Close game snapshot');
      expect(discardLabel()).toBe('Discard unsaved changes to the metadata?');

      clickButtonNamed('Keep editing');
      editText();
      clickButtonNamed('Close game snapshot');
      expect(discardLabel()).toBe('Discard unsaved changes to the snapshot text and metadata?');
    });
  });

  describe('downloading', () => {
    let clickSpy: jasmine.Spy;
    let clickedLinks: HTMLAnchorElement[];

    beforeEach(() => {
      clickedLinks = [];
      clickSpy = spyOn(HTMLAnchorElement.prototype, 'click').and.callFake(function (this: HTMLAnchorElement) {
        clickedLinks.push(this);
      });
    });

    function confirmOpen(): boolean {
      return component.downloadConfirmDialog.nativeElement.open;
    }

    it('downloads the saved text through a link when the editor is clean', async () => {
      await openReady();
      clickButtonNamed('Download .snapshot.txt of Emergency Low HP');

      expect(confirmOpen()).toBeFalse();
      expect(clickSpy).toHaveBeenCalledTimes(1);
      expect(clickedLinks[0].getAttribute('href')).toBe('/api/admin/benchmark/snapshots/1/text');
      expect(clickedLinks[0].getAttribute('download')).toBe('Emergency_Low_HP.snapshot.txt');
      expect(clickedLinks[0].isConnected).toBeFalse();
    });

    it('sanitises the file name, falling back to snapshot', () => {
      component.snapshot = snapshotWith('x', { name: 'Level 3: "Mines" / ☠' });
      expect(component.downloadFileName).toBe('Level_3_Mines.snapshot.txt');
      component.snapshot = snapshotWith('x', { name: '☠☠' });
      expect(component.downloadFileName).toBe('snapshot.snapshot.txt');
    });

    it('asks first when the editor is dirty; Cancel downloads nothing', async () => {
      await openReady();
      editText();
      clickButtonNamed('Download .snapshot.txt of Emergency Low HP');

      expect(confirmOpen()).toBeTrue();
      expect(clickSpy).not.toHaveBeenCalled();

      clickButtonNamed('Cancel');
      expect(confirmOpen()).toBeFalse();
      expect(clickSpy).not.toHaveBeenCalled();
    });

    it('closes the confirmation on its own Escape', async () => {
      await openReady();
      editText();
      clickButtonNamed('Download .snapshot.txt of Emergency Low HP');

      component.downloadConfirmDialog.nativeElement.dispatchEvent(new Event('cancel', { cancelable: true }));
      fixture.detectChanges();
      expect(confirmOpen()).toBeFalse();
      expect(component.viewerDialog.nativeElement.open).toBeTrue();
      expect(clickSpy).not.toHaveBeenCalled();
    });

    it('downloads the saved text without saving from Download saved text', async () => {
      await openReady();
      editText();
      clickButtonNamed('Download .snapshot.txt of Emergency Low HP');

      clickButtonNamed('Download saved text');
      expect(confirmOpen()).toBeFalse();
      expect(clickSpy).toHaveBeenCalledTimes(1);
      expect(mockBenchmarkService.updateSnapshotText).not.toHaveBeenCalled();
      expect(textEditor().dirty).toBeTrue();
    });

    it('saves the buffer and then downloads from Save and download', async () => {
      await openReady();
      editText();
      const buffer = textEditor().currentText()!;
      mockBenchmarkService.updateSnapshotText.and.returnValue(of(snapshotWith(buffer, { sha256: 'def0987654321' })));
      clickButtonNamed('Download .snapshot.txt of Emergency Low HP');

      clickButtonNamed('Save and download');
      expect(mockBenchmarkService.updateSnapshotText).toHaveBeenCalledWith(1, { text: buffer, expectedSha256: 'abc1234567890' });
      expect(clickSpy).toHaveBeenCalledTimes(1);
      expect(confirmOpen()).toBeFalse();
      expect(textEditor().dirty).toBeFalse();
    });

    it('downloads nothing and shows the error when Save and download fails', async () => {
      await openReady();
      editText();
      mockBenchmarkService.updateSnapshotText.and.returnValue(throwError(() => ({ status: 500, error: { message: 'Server exploded.' } })));
      clickButtonNamed('Download .snapshot.txt of Emergency Low HP');

      clickButtonNamed('Save and download');
      expect(confirmOpen()).toBeFalse();
      expect(clickSpy).not.toHaveBeenCalled();
      expect(host.querySelector('.editor-error')!.textContent).toContain('Server exploded.');
      expect(textEditor().dirty).toBeTrue();
    });
  });
});
