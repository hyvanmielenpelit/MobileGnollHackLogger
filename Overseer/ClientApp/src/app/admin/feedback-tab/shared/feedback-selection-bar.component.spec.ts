import type { Mock } from 'vitest';
import { ChangeDetectorRef } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { FeedbackTransferService } from '../../../services/feedback-transfer.service';
import { FeedbackSelectionBarComponent, FeedbackSelectionId } from './feedback-selection-bar.component';
import { FeedbackExportOptions } from './feedback-view-storage';

const FP = 'a'.repeat(64);

describe('FeedbackSelectionBarComponent', () => {
  let fixture: ComponentFixture<FeedbackSelectionBarComponent>;
  let component: FeedbackSelectionBarComponent;
  let host: HTMLElement;
  let exportIssues: Mock;
  let formatExamples: Mock;

  const q = <T extends Element = HTMLElement>(selector: string): T | null => host.querySelector<T>(selector);
  const text = (el: Element | null): string => (el?.textContent ?? '').replace(/\s+/g, ' ').trim();

  function set(inputs: Partial<{
    source: 'overseer' | 'gnollbench';
    selectedIds: readonly FeedbackSelectionId[];
    matchingCount: number;
    notShownCount: number;
    showSelectedOnly: boolean;
    exportOptions: FeedbackExportOptions | undefined;
  }>): void {
    for (const [key, value] of Object.entries(inputs)) {
      fixture.componentRef.setInput(key, value);
    }
    fixture.detectChanges();
  }

  beforeEach(async () => {
    exportIssues = vi.fn().mockName('FeedbackTransferService.exportIssues');
    formatExamples = vi.fn().mockName('FeedbackTransferService.formatExamples');
    await TestBed.configureTestingModule({
      imports: [FeedbackSelectionBarComponent],
      providers: [{
        provide: FeedbackTransferService,
        useValue: { exportIssues, formatExamples, previewResolutions: vi.fn(), applyResolutions: vi.fn() }
      }]
    }).compileComponents();

    fixture = TestBed.createComponent(FeedbackSelectionBarComponent);
    component = fixture.componentInstance;
    host = fixture.nativeElement as HTMLElement;
    set({ source: 'overseer', selectedIds: [], matchingCount: 12 });
  });

  afterEach(() => {
    host.querySelectorAll('dialog').forEach(dialog => {
      if (dialog.open) dialog.close();
    });
  });

  it('says when nothing is selected and offers only what applies', () => {
    expect(text(q('.fsb-count'))).toBe('Nothing selected');
    expect(q('.fsb-show-selected')).toBeNull();
    expect(q('.fsb-clear')).toBeNull();
    expect(text(q('.fsb-select-all'))).toBe('Select all 12 matching');
    expect(q('.fsb-export')!.getAttribute('aria-disabled')).toBe('true');
    expect(text(q(`#${q('.fsb-export')!.getAttribute('aria-describedby')}`))).toBe('Select issues to export.');
    expect(q('.fsb-import')!.getAttribute('aria-disabled')).toBeNull();
  });

  it('counts the selection and the selected issues the filters hide', () => {
    set({ selectedIds: [1, 2, 3], notShownCount: 2 });
    expect(text(q('.fsb-count'))).toBe('3 selected — 2 not shown');
    expect(q('.fsb-show-selected')!.getAttribute('aria-pressed')).toBe('false');
    expect(text(q('.fsb-clear'))).toBe('Clear selection (3)');
    expect(q('.fsb-export')!.getAttribute('aria-disabled')).toBeNull();
    expect(q('.fsb-export-reason')).toBeNull();
  });

  it('puts the export reason on the buttons\' line, just before Export…', () => {
    const reason = q('.fsb-export-reason')!;
    const exportButton = q('.fsb-export')!;
    expect(reason.parentElement).toBe(q('.fsb-actions'));
    expect(reason.nextElementSibling).toBe(exportButton);
    expect(exportButton.getAttribute('aria-describedby')).toBe(reason.id);
    expect(Math.abs(reason.getBoundingClientRect().top + reason.getBoundingClientRect().height / 2
      - (exportButton.getBoundingClientRect().top + exportButton.getBoundingClientRect().height / 2))).toBeLessThan(2);
  });

  it('keeps its height, and the status line\'s, when a selection is made and an export reports', () => {
    host.style.display = 'block';
    host.style.width = '1100px';
    const bar = q('.fsb')!;
    const status = q('.fsb-status')!;
    const emptyBar = bar.offsetHeight;
    const emptyStatus = status.offsetHeight;
    expect(emptyStatus).toBeGreaterThan(0);

    set({ selectedIds: [1] });
    expect(q('.fsb-export-reason')).toBeNull();
    expect(bar.offsetHeight).toBe(emptyBar);

    component.statusMessage = 'Saved overseer-feedback-20261027-1200.md with 1 issue.';
    fixture.componentRef.injector.get(ChangeDetectorRef).markForCheck();
    fixture.detectChanges();
    expect(text(status)).toBe('Saved overseer-feedback-20261027-1200.md with 1 issue.');
    expect(status.offsetHeight).toBe(emptyStatus);
  });

  it('reports the selection requests to the host', () => {
    const toggles: boolean[] = [];
    let cleared = 0;
    let selectAll = 0;
    component.showSelectedOnlyChange.subscribe(v => toggles.push(v));
    component.clear.subscribe(() => cleared++);
    component.selectAllMatching.subscribe(() => selectAll++);

    set({ selectedIds: [1] });
    q<HTMLButtonElement>('.fsb-show-selected')!.click();
    q<HTMLButtonElement>('.fsb-clear')!.click();
    q<HTMLButtonElement>('.fsb-select-all')!.click();
    expect(toggles).toEqual([true]);
    expect(cleared).toBe(1);
    expect(selectAll).toBe(1);

    set({ showSelectedOnly: true });
    expect(q('.fsb-show-selected')!.getAttribute('aria-pressed')).toBe('true');
    expect(q('.fsb-select-all')).toBeNull();
    q<HTMLButtonElement>('.fsb-show-selected')!.click();
    expect(toggles).toEqual([true, false]);
  });

  it('offers no Select all when nothing matches or the selection is full', () => {
    set({ matchingCount: 0 });
    expect(q('.fsb-select-all')).toBeNull();
    set({ matchingCount: 900, selectedIds: Array.from({ length: 500 }, (_, i) => i + 1) });
    expect(q('.fsb-select-all')).toBeNull();
  });

  it('refuses Export… over 200 issues and says why', () => {
    set({ selectedIds: Array.from({ length: 201 }, (_, i) => i + 1) });
    const button = q<HTMLButtonElement>('.fsb-export')!;
    expect(button.getAttribute('aria-disabled')).toBe('true');
    expect(text(q('.fsb-export-reason'))).toBe('An export holds at most 200 issues; 201 are selected.');
    button.click();
    expect(q<HTMLDialogElement>('dialog.feedback-export-dialog')!.open).toBe(false);
  });

  it('opens the export dialog and passes its outcome on', () => {
    vi.spyOn(URL, 'createObjectURL').mockReturnValue('blob:selection-bar-test');
    vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => undefined);
    vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => undefined);
    exportIssues.mockReturnValue(of({
      fileName: 'overseer-feedback-gnollbench-20261023-1234.yaml', content: 'format: x\n', mediaType: 'application/yaml;charset=utf-8',
      issueCount: 1, missingIds: [], missingFingerprints: [FP]
    }));
    const exported: { missing: FeedbackSelectionId[]; exportOptions: FeedbackExportOptions }[] = [];
    component.exported.subscribe(e => exported.push(e));

    set({ source: 'gnollbench', selectedIds: [FP, 'b'.repeat(64)], exportOptions: { occurrencesPerIssue: 25, includeQuestionText: true } });
    q<HTMLButtonElement>('.fsb-export')!.click();
    fixture.detectChanges();

    const dialog = q<HTMLDialogElement>('dialog.feedback-export-dialog')!;
    expect(dialog.open).toBe(true);
    expect(text(dialog.querySelector('h3'))).toBe('Export 2 issues');
    expect(dialog.querySelector('h3')!.id).toBe('gnollbench-selection-feedback-export-title');
    expect(dialog.querySelector('.fx-issue')).toBeNull();
    const select = dialog.querySelector<HTMLSelectElement>('select.fx-occurrences-select')!;
    expect(select.value).toBe('25');

    select.value = 'all';
    select.dispatchEvent(new Event('change'));
    fixture.detectChanges();
    dialog.querySelector<HTMLButtonElement>('.fx-export-btn')!.click();
    fixture.detectChanges();

    expect(exportIssues).toHaveBeenCalledWith({
      source: 'gnollbench', format: 'yaml', fingerprints: [FP, 'b'.repeat(64)], allOccurrences: true, includeQuestionText: true
    });
    expect(exported).toEqual([{ missing: [FP], exportOptions: { occurrencesPerIssue: 'all', includeQuestionText: true, format: 'yaml' } }]);
    expect(text(q('.fsb-status'))).toBe(
      'Saved overseer-feedback-gnollbench-20261023-1234.yaml with 1 issue. 1 selected issue no longer exists and has left the selection.');
  });

  it('ends its actions row with the file formats tip of its sub-tab', () => {
    set({ source: 'gnollbench' });
    const actions = q('.fsb-actions')!;
    const tip = actions.lastElementChild!;
    expect(tip.tagName.toLowerCase()).toBe('app-feedback-formats-tip');
    expect(tip.previousElementSibling).toBe(q('.fsb-import'));
    expect(tip.querySelector('button.gh-info-btn')!.getAttribute('aria-label')).toBe('About Feedback file formats');
    expect(tip.querySelector('#gnollbench-feedback-formats')).not.toBeNull();
    expect(formatExamples).not.toHaveBeenCalled();
  });

  it('opens and closes the formats tip without touching its own dialogs or outputs', () => {
    formatExamples.mockReturnValue(of({
      issues: { fileName: 'a.yaml', content: 'a: 1\n', mediaType: 'application/yaml;charset=utf-8' },
      resolutions: { fileName: 'b.yaml', content: 'b: 1\n', mediaType: 'application/yaml;charset=utf-8' }
    }));
    let outputs = 0;
    component.exported.subscribe(() => outputs++);
    component.imported.subscribe(() => outputs++);
    component.clear.subscribe(() => outputs++);
    set({ source: 'overseer', selectedIds: [1] });

    const tip = q('app-feedback-formats-tip')!;
    tip.querySelector<HTMLButtonElement>('button.gh-info-btn')!.click();
    fixture.detectChanges();
    const tipDialog = tip.querySelector<HTMLDialogElement>('dialog')!;
    expect(tipDialog.open).toBe(true);
    expect(formatExamples).toHaveBeenCalledWith('overseer');

    tipDialog.dispatchEvent(new Event('cancel', { cancelable: true }));
    tip.querySelector<HTMLButtonElement>('dialog .dialog-header .btn-icon-action')!.click();
    fixture.detectChanges();

    expect(tipDialog.open).toBe(false);
    expect(q<HTMLDialogElement>('dialog.feedback-export-dialog')!.open).toBe(false);
    expect(q<HTMLDialogElement>('dialog.feedback-import-dialog')!.open).toBe(false);
    expect(outputs).toBe(0);
    expect(exportIssues).not.toHaveBeenCalled();
  });

  it('opens the import dialog for its sub-tab and passes an import on', () => {
    let imported = 0;
    component.imported.subscribe(() => imported++);
    set({ source: 'gnollbench' });

    q<HTMLButtonElement>('.fsb-import')!.click();
    fixture.detectChanges();
    const dialog = q<HTMLDialogElement>('dialog.feedback-import-dialog')!;
    expect(dialog.open).toBe(true);
    expect(text(dialog.querySelector('h3'))).toBe('Import resolutions into the GnollBench sub-tab');

    component.importDialog!.imported.emit({ changed: 2, unchanged: 0 });
    expect(imported).toBe(1);
  });
});
