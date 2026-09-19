import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import {
  AdminBenchmarkService,
  BenchmarkSuiteDto,
  BoardFactsCheckDto,
  CaptureBenchmarkSnapshotResponse
} from '../../../services/admin-benchmark.service';
import {
  SnapshotUploadDialogComponent,
  looksLikeHtml,
  snapshotNameFromFileName
} from './snapshot-upload-dialog.component';

describe('SnapshotUploadDialogComponent', () => {
  let fixture: ComponentFixture<SnapshotUploadDialogComponent>;
  let component: SnapshotUploadDialogComponent;
  let host: HTMLElement;
  let service: jasmine.SpyObj<AdminBenchmarkService>;

  const bareSuite: BenchmarkSuiteDto = {
    id: 3, name: 'Bare Suite', description: null, createdAtUtc: '', modifiedAtUtc: null,
    questionCount: 0, assessedQuestionCount: 0, difficultyFullyAssessed: false
  };
  const boundSuite: BenchmarkSuiteDto = {
    ...bareSuite, id: 4, name: 'Bound Suite',
    gameSnapshotId: 99, gameSnapshotName: 'Old board', gameSnapshotCharCount: 1234
  };
  const response = { board: { id: 100 }, suite: { id: 4 } } as unknown as CaptureBenchmarkSnapshotResponse;

  beforeEach(async () => {
    service = jasmine.createSpyObj('AdminBenchmarkService', ['uploadSuiteSnapshot', 'getSnapshot']);
    service.getSnapshot.and.returnValue(of({
      id: 99, name: 'Old board', charCount: 1234, sha256: 'x', captureMethod: 'TextUpload',
      createdAtUtc: '2026-09-01T10:00:00Z', capturedAtUtc: '2026-09-01T10:00:00Z'
    }));

    await TestBed.configureTestingModule({
      imports: [SnapshotUploadDialogComponent],
      providers: [{ provide: AdminBenchmarkService, useValue: service }]
    }).compileComponents();

    fixture = TestBed.createComponent(SnapshotUploadDialogComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
    host = fixture.nativeElement as HTMLElement;
  });

  afterEach(() => {
    component.replaceConfirmDialog?.nativeElement?.close();
    component.dialog?.nativeElement?.close();
  });

  function uploadButton(): HTMLButtonElement {
    return host.querySelector('.upload-snapshot-btn') as HTMLButtonElement;
  }

  async function chooseFile(text: string, name = 'Valkyrie dlvl 12.snapshot.txt'): Promise<void> {
    await component.loadFile(new File([text], name, { type: 'text/plain' }));
    fixture.detectChanges();
  }

  it('detects HTML dumps the way the server does', () => {
    expect(looksLikeHtml('  <html><body>x')).toBeTrue();
    expect(looksLikeHtml('<PRE>map</PRE>')).toBeTrue();
    expect(looksLikeHtml('<div>no dump</div>')).toBeFalse();
    expect(looksLikeHtml('Dlvl:1 <html>')).toBeFalse();
  });

  it('derives the name from the file name', () => {
    expect(snapshotNameFromFileName('Valkyrie dlvl 12.snapshot.txt')).toBe('Valkyrie dlvl 12');
    expect(snapshotNameFromFileName('dump.html')).toBe('dump');
  });

  it('reads the chosen file, shows the detected kind and fills the name', async () => {
    component.open(bareSuite);
    fixture.detectChanges();
    expect(uploadButton().getAttribute('aria-disabled')).toBe('true');

    await chooseFile('<html><body><pre>Dlvl:1</pre></body></html>', 'dump.html');

    expect(host.querySelector('#snapshotUploadFile-card .gh-file-card-name')!.textContent).toBe('dump.html');
    expect(host.querySelector('.gh-file-card-detail')!.textContent).toBe('Detected: HTML dump, 43 characters');
    expect(component.name).toBe('dump');
    expect(uploadButton().getAttribute('aria-disabled')).toBeNull();
  });

  it('clears an attached file, dropping a derived name and keeping a typed one', async () => {
    component.open(bareSuite);
    fixture.detectChanges();
    const remove = () => host.querySelector('#snapshotUploadFile-remove') as HTMLButtonElement;

    await chooseFile('Dlvl:1');
    expect(component.name).toBe('Valkyrie dlvl 12');
    remove().click();
    fixture.detectChanges();

    expect(host.querySelector('#snapshotUploadFile-card')).toBeNull();
    expect(host.querySelector('input[type="file"]#snapshotUploadFile')).not.toBeNull();
    expect(component.content).toBeNull();
    expect(component.name).toBe('');
    expect(uploadButton().getAttribute('aria-disabled')).toBe('true');

    const nameInput = host.querySelector('#snapshotUploadName') as HTMLInputElement;
    nameInput.value = 'My board';
    nameInput.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    await chooseFile('Dlvl:2');
    expect(component.name).toBe('My board');
    remove().click();
    fixture.detectChanges();

    expect(component.name).toBe('My board');
    expect(uploadButton().getAttribute('aria-disabled')).toBe('true');
  });

  it('posts directly, with the chosen content kind, for a suite without a snapshot', async () => {
    service.uploadSuiteSnapshot.and.returnValue(of(response));
    const emitted = jasmine.createSpy('uploaded');
    component.uploaded.subscribe(emitted);

    component.open(bareSuite);
    await chooseFile('Dlvl:1 $:0');
    (host.querySelector('input[type="radio"][value="Text"]') as HTMLInputElement).click();
    fixture.detectChanges();

    expect(host.querySelector('.upload-replace-warning')).toBeNull();
    uploadButton().click();
    fixture.detectChanges();

    expect(service.uploadSuiteSnapshot).toHaveBeenCalledWith(3, {
      name: 'Valkyrie dlvl 12',
      content: 'Dlvl:1 $:0',
      contentKind: 'Text',
      notes: null,
      sourceGnollHackVersion: null,
      replaceExisting: false
    });
    expect(emitted).toHaveBeenCalledWith(response);
    expect(component.dialog.nativeElement.open).toBeFalse();
  });

  it('stays open with the missing-literal list and a Done button when the check finds one', async () => {
    const check: BoardFactsCheckDto = {
      bulletCount: 10, checkedLiteralCount: 9, unquotedBulletCount: 1,
      unquotedBullets: [],
      missingLiterals: [{ questionId: 6, orderIndex: 5, literal: 'the uncursed Holy Grail', lineExcerpt: 'T - the Holy Grail' }]
    };
    service.uploadSuiteSnapshot.and.returnValue(of({ ...response, boardFactsCheck: check }));

    component.open(bareSuite);
    await chooseFile('Dlvl:1');
    uploadButton().click();
    fixture.detectChanges();

    expect(component.dialog.nativeElement.open).toBeTrue();
    expect(host.querySelector('.upload-snapshot-btn')).toBeNull();
    const notice = host.querySelector('.board-facts-notice')!;
    // OrderIndex is stored 1-based and printed as it is.
    expect(notice.textContent).toContain('Q5: "the uncursed Holy Grail"');
    expect(notice.textContent).not.toContain('Q6');
    expect(notice.textContent).toContain('server_rubric_handoff');
    expect(host.querySelector('.board-facts-unquoted')!.textContent).toContain('1 BOARD FACTS line');

    (host.querySelector('.done-upload-btn') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(component.dialog.nativeElement.open).toBeFalse();
  });

  it('closes as usual when the check finds nothing missing', async () => {
    const check: BoardFactsCheckDto = {
      bulletCount: 10, checkedLiteralCount: 10, unquotedBulletCount: 0, unquotedBullets: [], missingLiterals: []
    };
    service.uploadSuiteSnapshot.and.returnValue(of({ ...response, boardFactsCheck: check }));

    component.open(bareSuite);
    await chooseFile('Dlvl:1');
    uploadButton().click();
    fixture.detectChanges();

    expect(component.dialog.nativeElement.open).toBeFalse();
  });

  it('asks before replacing, sends nothing on Cancel, and replaces on confirm', async () => {
    service.uploadSuiteSnapshot.and.returnValue(of(response));

    component.open(boundSuite);
    fixture.detectChanges();
    expect(host.querySelector('.upload-replace-warning')!.textContent).toContain("'Old board'");

    await chooseFile('Dlvl:2');
    uploadButton().click();
    fixture.detectChanges();

    expect(component.replaceConfirmDialog.nativeElement.open).toBeTrue();
    expect(service.uploadSuiteSnapshot).not.toHaveBeenCalled();
    expect(host.querySelector('#snapshotReplaceBody')!.textContent).toContain("replaced by 'Valkyrie dlvl 12'");

    (Array.from(host.querySelectorAll('.snapshot-replace-confirm button')) as HTMLButtonElement[])
      .find(b => b.textContent!.trim() === 'Cancel')!.click();
    fixture.detectChanges();
    expect(component.replaceConfirmDialog.nativeElement.open).toBeFalse();
    expect(component.content).toBe('Dlvl:2');
    expect(service.uploadSuiteSnapshot).not.toHaveBeenCalled();

    uploadButton().click();
    fixture.detectChanges();
    (host.querySelector('.replace-snapshot-btn') as HTMLButtonElement).click();
    fixture.detectChanges();

    expect(service.uploadSuiteSnapshot).toHaveBeenCalledTimes(1);
    expect(service.uploadSuiteSnapshot.calls.mostRecent().args[1].replaceExisting).toBeTrue();
  });

  it('shows a 409 body inline and stays open', async () => {
    service.uploadSuiteSnapshot.and.returnValue(throwError(() => ({
      status: 409,
      error: { error: 'This suite already has a snapshot. Confirm the replacement to upload a new one.' }
    })));

    component.open(bareSuite);
    await chooseFile('Dlvl:1');
    uploadButton().click();
    fixture.detectChanges();

    expect(host.querySelector('.error-message[role="alert"]')!.textContent).toContain('Confirm the replacement');
    expect(component.dialog.nativeElement.open).toBeTrue();
  });
});
