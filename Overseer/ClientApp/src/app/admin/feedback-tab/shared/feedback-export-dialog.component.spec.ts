import type { Mock } from 'vitest';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { of, throwError } from 'rxjs';
import { FeedbackExportResult, FeedbackTransferService } from '../../../services/feedback-transfer.service';
import { FeedbackExportDialogComponent, FeedbackExportDone } from './feedback-export-dialog.component';

const FP = 'a'.repeat(64);

describe('FeedbackExportDialogComponent', () => {
  let fixture: ComponentFixture<FeedbackExportDialogComponent>;
  let component: FeedbackExportDialogComponent;
  let host: HTMLElement;
  let exportIssues: Mock;
  let done: FeedbackExportDone[];

  const q = <T extends Element = HTMLElement>(selector: string): T | null => host.querySelector<T>(selector);
  const text = (el: Element | null): string => (el?.textContent ?? '').replace(/\s+/g, ' ').trim();

  /** The save path of `downloadTextFile`: the blob through its object URL, the name through the anchor. */
  function captureSaves(): { blobs: Blob[]; names: string[] } {
    const saved: { blobs: Blob[]; names: string[] } = { blobs: [], names: [] };
    vi.spyOn(URL, 'createObjectURL').mockImplementation((source: Blob | MediaSource) => {
      saved.blobs.push(source as Blob);
      return 'blob:feedback-export-test';
    });
    vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => undefined);
    vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(function (this: HTMLAnchorElement) {
      saved.names.push(this.download);
    });
    return saved;
  }

  function create(source: 'overseer' | 'gnollbench', ids: readonly (number | string)[]): void {
    fixture = TestBed.createComponent(FeedbackExportDialogComponent);
    component = fixture.componentInstance;
    host = fixture.nativeElement as HTMLElement;
    fixture.componentRef.setInput('source', source);
    fixture.componentRef.setInput('ids', ids);
    done = [];
    component.exported.subscribe(event => done.push(event));
    fixture.detectChanges();
  }

  function result(overrides: Partial<FeedbackExportResult> = {}): FeedbackExportResult {
    return {
      fileName: 'overseer-feedback-overseer-20261023-1234.yaml',
      content: 'format: overseer-feedback-issues\n',
      mediaType: 'application/yaml;charset=utf-8',
      issueCount: 2,
      missingIds: [],
      missingFingerprints: [],
      ...overrides
    };
  }

  beforeEach(async () => {
    exportIssues = vi.fn().mockName('FeedbackTransferService.exportIssues');
    await TestBed.configureTestingModule({
      imports: [FeedbackExportDialogComponent],
      providers: [{ provide: FeedbackTransferService, useValue: { exportIssues } }]
    }).compileComponents();
  });

  afterEach(() => {
    const dialog = q<HTMLDialogElement>('dialog');
    if (dialog?.open) dialog.close();
  });

  it('opens with the default options, YAML first, and no question-text choice for Overseer', () => {
    create('overseer', [42, 7]);
    component.open();

    expect(q<HTMLDialogElement>('dialog')!.open).toBe(true);
    expect(text(q('h3'))).toBe('Export 2 issues');
    expect(q<HTMLInputElement>('input[type="radio"][value="10"]')!.checked).toBe(true);
    expect(q('.fx-question-text')).toBeNull();
    expect(q('.fx-export-btn')!.getAttribute('aria-disabled')).toBeNull();

    const format = q<HTMLFieldSetElement>('fieldset.fx-format')!;
    expect(text(format.querySelector('legend'))).toBe('File format');
    const radios = Array.from(format.querySelectorAll<HTMLInputElement>('input[type="radio"]'));
    expect(radios.map(r => r.value)).toEqual(['yaml', 'markdown']);
    expect(radios.map(r => r.checked)).toEqual([true, false]);
    expect(radios.every(r => r.name === 'overseer-feedback-export-format')).toBe(true);
    expect(Array.from(format.querySelectorAll('label')).map(l => text(l))).toEqual([
      'YAML for an AI coding assistant',
      'Markdown to read'
    ]);
  });

  it('names one issue in the singular', () => {
    create('overseer', [42]);
    component.open();
    expect(text(q('h3'))).toBe('Export 1 issue');
  });

  it('starts from the remembered options', () => {
    create('gnollbench', [FP]);
    fixture.componentRef.setInput('exportOptions', { occurrencesPerIssue: 25, includeQuestionText: false, format: 'markdown' });
    component.open();

    expect(q<HTMLInputElement>('input[type="radio"][value="25"]')!.checked).toBe(true);
    expect(q<HTMLInputElement>('.fx-question-text input')!.checked).toBe(false);
    expect(q<HTMLInputElement>('input[type="radio"][value="markdown"]')!.checked).toBe(true);
    expect(q<HTMLInputElement>('input[type="radio"][value="yaml"]')!.checked).toBe(false);
  });

  it('starts on YAML when the remembered options name no format', () => {
    create('overseer', [42]);
    fixture.componentRef.setInput('exportOptions', { occurrencesPerIssue: 5, includeQuestionText: true });
    component.open();

    expect(q<HTMLInputElement>('input[type="radio"][value="yaml"]')!.checked).toBe(true);
    expect(q<HTMLInputElement>('input[type="radio"][value="5"]')!.checked).toBe(true);
  });

  it('exports Overseer issues by id as YAML, saves the file with its media type and reports the missing ones', () => {
    const saved = captureSaves();
    exportIssues.mockReturnValue(of(result({ missingIds: [7], issueCount: 1 })));
    create('overseer', [42, 7]);
    component.open();

    q<HTMLInputElement>('input[type="radio"][value="5"]')!.click();
    fixture.detectChanges();
    q<HTMLButtonElement>('.fx-export-btn')!.click();
    fixture.detectChanges();

    expect(exportIssues).toHaveBeenCalledWith({ source: 'overseer', format: 'yaml', issueIds: [42, 7], occurrencesPerIssue: 5 });
    expect(saved.names).toEqual(['overseer-feedback-overseer-20261023-1234.yaml']);
    expect(saved.blobs[0].type).toBe('application/yaml;charset=utf-8');
    expect(done).toEqual([{
      missing: [7],
      exportOptions: { occurrencesPerIssue: 5, includeQuestionText: true, format: 'yaml' },
      fileName: 'overseer-feedback-overseer-20261023-1234.yaml',
      issueCount: 1
    }]);
    expect(q<HTMLDialogElement>('dialog')!.open).toBe(false);
  });

  it('saves the content field, not a markdown field', async () => {
    const saved = captureSaves();
    exportIssues.mockReturnValue(of(result({ content: 'format: overseer-feedback-issues\nversion: 1\n' })));
    create('overseer', [42]);
    component.open();

    q<HTMLButtonElement>('.fx-export-btn')!.click();

    expect(saved.blobs.length).toBe(1);
    expect(await saved.blobs[0].text()).toBe('format: overseer-feedback-issues\nversion: 1\n');
  });

  it('exports as Markdown when chosen, remembers the choice and saves with the Markdown media type', () => {
    const saved = captureSaves();
    exportIssues.mockReturnValue(of(result({
      fileName: 'overseer-feedback-overseer-20261023-1234.md', content: '# Overseer Feedback Export\n', mediaType: 'text/markdown;charset=utf-8'
    })));
    create('overseer', [42]);
    component.open();

    q<HTMLInputElement>('input[type="radio"][value="markdown"]')!.click();
    fixture.detectChanges();
    q<HTMLButtonElement>('.fx-export-btn')!.click();

    expect(exportIssues).toHaveBeenCalledWith({ source: 'overseer', format: 'markdown', issueIds: [42], occurrencesPerIssue: 10 });
    expect(saved.names).toEqual(['overseer-feedback-overseer-20261023-1234.md']);
    expect(saved.blobs[0].type).toBe('text/markdown;charset=utf-8');
    expect(done[0].exportOptions).toEqual({ occurrencesPerIssue: 10, includeQuestionText: true, format: 'markdown' });
  });

  it('exports GnollBench issues by fingerprint with the question-text choice', () => {
    captureSaves();
    exportIssues.mockReturnValue(of(result({ fileName: 'overseer-feedback-gnollbench-20261023-1234.yaml', missingFingerprints: [FP] })));
    create('gnollbench', [FP]);
    component.open();

    q<HTMLInputElement>('.fx-question-text input')!.click();
    fixture.detectChanges();
    q<HTMLButtonElement>('.fx-export-btn')!.click();

    expect(exportIssues).toHaveBeenCalledWith({
      source: 'gnollbench', format: 'yaml', fingerprints: [FP], occurrencesPerIssue: 10, includeQuestionText: false
    });
    expect(done[0].missing).toEqual([FP]);
    expect(done[0].exportOptions).toEqual({ occurrencesPerIssue: 10, includeQuestionText: false, format: 'yaml' });
  });

  it('keeps the dialog open and drops the selection when none of the issues exist', () => {
    const saved = captureSaves();
    exportIssues.mockReturnValue(throwError(() => new HttpErrorResponse({
      status: 404, error: { message: 'None of the issues exist.', missingIds: [42], missingFingerprints: [] }
    })));
    create('overseer', [42]);
    component.open();

    q<HTMLButtonElement>('.fx-export-btn')!.click();
    fixture.detectChanges();

    expect(saved.names).toEqual([]);
    expect(q<HTMLDialogElement>('dialog')!.open).toBe(true);
    expect(text(q('.fx-error'))).toContain('None of the selected issues exist any more');
    expect(done).toEqual([{ missing: [42], exportOptions: { occurrencesPerIssue: 10, includeQuestionText: true, format: 'yaml' }, fileName: null, issueCount: 0 }]);
  });

  it('shows the server\'s message when the export fails', () => {
    exportIssues.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 400, error: { message: 'At most 200 issues.' } })));
    create('overseer', [42]);
    component.open();

    q<HTMLButtonElement>('.fx-export-btn')!.click();
    fixture.detectChanges();

    expect(text(q('.fx-error'))).toBe('The export failed: At most 200 issues.');
    expect(done).toEqual([]);
  });

  it('refuses more than 200 issues and says why', () => {
    create('overseer', Array.from({ length: 201 }, (_, i) => i + 1));
    component.open();

    const button = q<HTMLButtonElement>('.fx-export-btn')!;
    expect(button.getAttribute('aria-disabled')).toBe('true');
    const reason = q(`#${button.getAttribute('aria-describedby')}`);
    expect(text(reason)).toBe('An export holds at most 200 issues; 201 are selected.');

    button.click();
    expect(exportIssues).not.toHaveBeenCalled();
  });

  it('closes on Cancel', () => {
    create('overseer', [1]);
    component.open();
    q<HTMLButtonElement>('.btn-gh-cancel')!.click();
    expect(q<HTMLDialogElement>('dialog')!.open).toBe(false);
  });
});
