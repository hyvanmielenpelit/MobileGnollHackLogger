import type { Mock } from 'vitest';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { of, throwError } from 'rxjs';
import { FeedbackExportResult, FeedbackTransferService } from '../../../services/feedback-transfer.service';
import { FeedbackExportDialogComponent, FeedbackExportDone, OCCURRENCE_PRESETS } from './feedback-export-dialog.component';
import { FeedbackExportOptions } from './feedback-view-storage';

const FP = 'a'.repeat(64);

describe('FeedbackExportDialogComponent', () => {
  let fixture: ComponentFixture<FeedbackExportDialogComponent>;
  let component: FeedbackExportDialogComponent;
  let host: HTMLElement;
  let exportIssues: Mock;
  let done: FeedbackExportDone[];
  const fixtures: ComponentFixture<FeedbackExportDialogComponent>[] = [];

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

  function create(
    source: 'overseer' | 'gnollbench',
    ids: readonly (number | string)[],
    inputs: Partial<{ idPrefix: string; issueSubject: string | null; occurrenceTotal: number | null; exportOptions: FeedbackExportOptions }> = {}
  ): void {
    fixture = TestBed.createComponent(FeedbackExportDialogComponent);
    fixtures.push(fixture);
    component = fixture.componentInstance;
    host = fixture.nativeElement as HTMLElement;
    fixture.componentRef.setInput('source', source);
    fixture.componentRef.setInput('ids', ids);
    for (const [key, value] of Object.entries(inputs)) {
      fixture.componentRef.setInput(key, value);
    }
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

  const select = (): HTMLSelectElement => q<HTMLSelectElement>('select.fx-occurrences-select')!;
  const customInput = (): HTMLInputElement | null => q<HTMLInputElement>('input.fx-custom-input');
  const exportButton = (): HTMLButtonElement => q<HTMLButtonElement>('.fx-export-btn')!;

  /** Chooses an occurrences option the way a user does: the select's value, then its change event. */
  function chooseOccurrences(value: string): void {
    const el = select();
    el.value = value;
    el.dispatchEvent(new Event('change'));
    fixture.detectChanges();
  }

  /** Types into the custom number field. */
  function typeCustom(value: string): void {
    const input = customInput()!;
    input.value = value;
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();
  }

  function clickExport(): void {
    exportButton().click();
    fixture.detectChanges();
  }

  beforeEach(async () => {
    exportIssues = vi.fn().mockName('FeedbackTransferService.exportIssues');
    await TestBed.configureTestingModule({
      imports: [FeedbackExportDialogComponent],
      providers: [{ provide: FeedbackTransferService, useValue: { exportIssues } }]
    }).compileComponents();
  });

  afterEach(() => {
    for (const f of fixtures.splice(0)) {
      const dialog = (f.nativeElement as HTMLElement).querySelector<HTMLDialogElement>('dialog');
      if (dialog?.open) dialog.close();
    }
  });

  it('opens with the default options, YAML first and focused, and no question-text choice for Overseer', () => {
    create('overseer', [42, 7]);
    component.open();

    expect(q<HTMLDialogElement>('dialog')!.open).toBe(true);
    expect(text(q('h3'))).toBe('Export 2 issues');
    expect(q('.fx-issue')).toBeNull();
    expect(q<HTMLDialogElement>('dialog')!.getAttribute('aria-describedby')).toBeNull();
    expect(text(q('.fx-privacy'))).toBe('Anonymized: no user names and no links to chats.');
    expect(q('.fx-privacy svg')!.getAttribute('aria-hidden')).toBe('true');
    expect(q('.fx-question-text')).toBeNull();
    expect(exportButton().getAttribute('aria-disabled')).toBeNull();
    expect(text(exportButton())).toBe('Download YAML');
    expect(exportButton().querySelector('svg.btn-icon')!.getAttribute('aria-hidden')).toBe('true');

    const format = q<HTMLFieldSetElement>('fieldset.fx-format')!;
    expect(text(format.querySelector('legend'))).toBe('File format');
    const cards = Array.from(format.querySelectorAll<HTMLLabelElement>('label.fx-format-card'));
    const radios = cards.map(card => card.querySelector<HTMLInputElement>(':scope > input[type="radio"]')!);
    expect(radios.map(r => r.value)).toEqual(['yaml', 'markdown']);
    expect(radios.map(r => r.checked)).toEqual([true, false]);
    expect(radios.every(r => r.name === 'overseer-feedback-export-format')).toBe(true);
    expect(cards.map(card => Array.from(card.querySelectorAll('.fx-format-name, .fx-format-tag, .fx-format-hint')).map(text).join(' '))).toEqual([
      'YAML Recommended For an AI coding assistant. Ends in a resolution template.',
      'Markdown For reading.'
    ]);
    expect(document.activeElement).toBe(radios[0]);

    expect(select().value).toBe('10');
    expect(Array.from(select().options).map(o => o.value)).toEqual(['5', '10', '25', '50', '100', 'all', 'custom']);
    expect(Array.from(select().options).map(o => text(o))).toEqual(
      ['5 newest', '10 newest', '25 newest', '50 newest', '100 newest', 'All occurrences', 'Custom…']);
    expect(OCCURRENCE_PRESETS).toEqual([5, 10, 25, 50, 100]);
    expect(text(q(`label[for="${select().id}"]`))).toBe('Occurrences per issue');
    const hint = q(`#${select().getAttribute('aria-describedby')}`);
    expect(text(hint)).toBe('Newest first.');
    expect(customInput()).toBeNull();

    expect(text(q('.fx-next .gh-section-title'))).toBe('Next steps');
    expect(q('.fx-next')!.getAttribute('aria-labelledby')).toBe(q('.fx-next h4')!.id);
    const steps = Array.from(host.querySelectorAll('ol.fx-steps > li')).map(li => text(li));
    expect(steps.length).toBe(4);
    expect(steps[0]).toBe('Download the file.');
    expect(steps[1]).toContain('server-feedback-resolution');
    expect(steps[3]).toContain('Import resolutions…');
    expect(steps[3]).toContain('Edit triage');
    expect(q('.fx-markdown-note')).toBeNull();
  });

  it('names one selected issue in the singular', () => {
    create('overseer', [42]);
    component.open();
    expect(text(q('h3'))).toBe('Export 1 issue');
  });

  it('shows one issue in the gold-ruled panel, with its stored occurrences in the panel and the hint', () => {
    create('overseer', [42], { issueSubject: 'The wiki search misses altars', occurrenceTotal: 7 });
    component.open();

    expect(text(q('h3'))).toBe('Export issue');
    const panel = q('.fx-issue')!;
    expect(text(panel.querySelector('.fx-issue-label'))).toBe('Issue');
    expect(text(panel.querySelector('.fx-issue-subject'))).toBe('The wiki search misses altars');
    expect(text(panel.querySelector('.fx-issue-details'))).toBe('7 stored occurrences');
    expect(q<HTMLDialogElement>('dialog')!.getAttribute('aria-describedby')).toBe(panel.id);
    expect(text(q('.fx-occurrences-hint'))).toBe('Newest first. This issue has 7.');

    fixture.componentRef.setInput('occurrenceTotal', 1);
    fixture.detectChanges();
    expect(text(q('.fx-issue-details'))).toBe('1 stored occurrence');
    expect(text(q('.fx-occurrences-hint'))).toBe('Newest first. This issue has 1.');
  });

  it('leaves the count out of one issue\'s panel and hint when it is not known', () => {
    create('gnollbench', [FP], { issueSubject: 'Rubric gap', occurrenceTotal: null });
    component.open();

    expect(text(q('h3'))).toBe('Export issue');
    expect(q('.fx-issue-details')).toBeNull();
    expect(text(q('.fx-occurrences-hint'))).toBe('Newest first.');
  });

  it('starts from the remembered options', () => {
    create('gnollbench', [FP], { exportOptions: { occurrencesPerIssue: 25, includeQuestionText: false, format: 'markdown' } });
    component.open();

    expect(select().value).toBe('25');
    expect(q<HTMLInputElement>('.fx-question-text input')!.checked).toBe(false);
    expect(q<HTMLInputElement>('input[type="radio"][value="markdown"]')!.checked).toBe(true);
    expect(q<HTMLInputElement>('input[type="radio"][value="yaml"]')!.checked).toBe(false);
    expect(document.activeElement).toBe(q('input[type="radio"][value="markdown"]'));
    expect(text(exportButton())).toBe('Download Markdown');
  });

  it('starts on All occurrences, or on Custom… with the number filled in, when that was remembered', () => {
    create('overseer', [42], { exportOptions: { occurrencesPerIssue: 'all', includeQuestionText: true, format: 'yaml' } });
    component.open();
    expect(select().value).toBe('all');
    expect(customInput()).toBeNull();

    create('overseer', [42], { exportOptions: { occurrencesPerIssue: 137, includeQuestionText: true, format: 'yaml' } });
    component.open();
    expect(select().value).toBe('custom');
    expect(customInput()!.value).toBe('137');
  });

  it('starts on YAML when the remembered options name no format', () => {
    create('overseer', [42], { exportOptions: { occurrencesPerIssue: 5, includeQuestionText: true } });
    component.open();

    expect(q<HTMLInputElement>('input[type="radio"][value="yaml"]')!.checked).toBe(true);
    expect(select().value).toBe('5');
  });

  it('exports Overseer issues by id as YAML, saves the file with its media type and reports the missing ones', () => {
    const saved = captureSaves();
    exportIssues.mockReturnValue(of(result({ missingIds: [7], issueCount: 1 })));
    create('overseer', [42, 7]);
    component.open();

    chooseOccurrences('50');
    clickExport();

    expect(exportIssues).toHaveBeenCalledWith({ source: 'overseer', format: 'yaml', issueIds: [42, 7], occurrencesPerIssue: 50 });
    expect(saved.names).toEqual(['overseer-feedback-overseer-20261023-1234.yaml']);
    expect(saved.blobs[0].type).toBe('application/yaml;charset=utf-8');
    expect(done).toEqual([{
      missing: [7],
      exportOptions: { occurrencesPerIssue: 50, includeQuestionText: true, format: 'yaml' },
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

    exportButton().click();

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

    q<HTMLLabelElement>('label.fx-format-card:has(input[value="markdown"])')!.click();
    fixture.detectChanges();
    expect(text(exportButton())).toBe('Download Markdown');
    exportButton().click();

    expect(exportIssues).toHaveBeenCalledWith({ source: 'overseer', format: 'markdown', issueIds: [42], occurrencesPerIssue: 10 });
    expect(saved.names).toEqual(['overseer-feedback-overseer-20261023-1234.md']);
    expect(saved.blobs[0].type).toBe('text/markdown;charset=utf-8');
    expect(done[0].exportOptions).toEqual({ occurrencesPerIssue: 10, includeQuestionText: true, format: 'markdown' });
  });

  it('shows the next steps while YAML is chosen and one line instead while Markdown is', () => {
    create('overseer', [42]);
    component.open();
    expect(host.querySelectorAll('ol.fx-steps > li').length).toBe(4);

    q<HTMLInputElement>('input[type="radio"][value="markdown"]')!.click();
    fixture.detectChanges();
    expect(q('.fx-steps')).toBeNull();
    expect(q('.fx-next')).toBeNull();
    expect(text(q('.fx-markdown-note'))).toBe('Markdown is for reading. Choose YAML to hand the issues to an assistant.');

    q<HTMLInputElement>('input[type="radio"][value="yaml"]')!.click();
    fixture.detectChanges();
    expect(host.querySelectorAll('ol.fx-steps > li').length).toBe(4);
    expect(q('.fx-markdown-note')).toBeNull();
  });

  it('exports GnollBench issues by fingerprint with the question-text choice', () => {
    captureSaves();
    exportIssues.mockReturnValue(of(result({ fileName: 'overseer-feedback-gnollbench-20261023-1234.yaml', missingFingerprints: [FP] })));
    create('gnollbench', [FP]);
    component.open();

    q<HTMLInputElement>('.fx-question-text input')!.click();
    fixture.detectChanges();
    exportButton().click();

    expect(exportIssues).toHaveBeenCalledWith({
      source: 'gnollbench', format: 'yaml', fingerprints: [FP], occurrencesPerIssue: 10, includeQuestionText: false
    });
    expect(done[0].missing).toEqual([FP]);
    expect(done[0].exportOptions).toEqual({ occurrencesPerIssue: 10, includeQuestionText: false, format: 'yaml' });
  });

  it('asks for every occurrence with allOccurrences and no count', () => {
    captureSaves();
    exportIssues.mockReturnValue(of(result()));
    create('overseer', [42]);
    component.open();

    chooseOccurrences('all');
    expect(customInput()).toBeNull();
    clickExport();

    expect(exportIssues).toHaveBeenCalledWith({ source: 'overseer', format: 'yaml', issueIds: [42], allOccurrences: true });
    expect('occurrencesPerIssue' in exportIssues.mock.calls[0][0]).toBe(false);
    expect(done[0].exportOptions).toEqual({ occurrencesPerIssue: 'all', includeQuestionText: true, format: 'yaml' });
  });

  it('reveals a labeled number field for Custom…, focused and filled with the preset it replaces, and exports its count', () => {
    captureSaves();
    exportIssues.mockReturnValue(of(result()));
    create('gnollbench', [FP]);
    component.open();

    chooseOccurrences('25');
    chooseOccurrences('custom');
    const input = customInput()!;
    expect(input.type).toBe('number');
    expect(input.min).toBe('1');
    expect(input.max).toBe('1000');
    expect(input.step).toBe('1');
    expect(input.getAttribute('inputmode')).toBe('numeric');
    expect(input.value).toBe('25');
    expect(document.activeElement).toBe(input);
    expect(text(q(`label[for="${input.id}"]`))).toBe('Number of occurrences');
    expect(input.getAttribute('aria-describedby')).toBe(q('.fx-occurrences-hint')!.id);
    expect(input.getAttribute('aria-invalid')).toBeNull();

    typeCustom('250');
    clickExport();

    expect(exportIssues).toHaveBeenCalledWith({
      source: 'gnollbench', format: 'yaml', fingerprints: [FP], occurrencesPerIssue: 250, includeQuestionText: true
    });
    expect(done[0].exportOptions).toEqual({ occurrencesPerIssue: 250, includeQuestionText: true, format: 'yaml' });
  });

  it('refuses a custom count that is not a whole number from 1 to 1,000, at the field, and sends nothing', () => {
    create('overseer', [42]);
    component.open();
    chooseOccurrences('custom');

    for (const value of ['0', '1001', '2.5', '-3', '']) {
      typeCustom(value);
      (document.activeElement as HTMLElement | null)?.blur();
      clickExport();

      const input = customInput()!;
      const error = q('.fx-custom-error')!;
      expect(text(error)).toBe('Enter a whole number from 1 to 1,000.');
      expect(error.classList).toContain('gh-field-error');
      expect(input.getAttribute('aria-invalid')).toBe('true');
      expect(input.getAttribute('aria-describedby')!.split(' ')).toEqual([q('.fx-occurrences-hint')!.id, error.id]);
      expect(document.activeElement).toBe(input);
    }
    expect(exportIssues).not.toHaveBeenCalled();
    expect(q<HTMLDialogElement>('dialog')!.open).toBe(true);

    typeCustom('1000');
    expect(q('.fx-custom-error')).toBeNull();
    expect(customInput()!.getAttribute('aria-invalid')).toBeNull();
  });

  it('accepts the bounds of a custom count', () => {
    captureSaves();
    exportIssues.mockReturnValue(of(result()));
    create('overseer', [42]);
    component.open();
    chooseOccurrences('custom');

    typeCustom('1');
    clickExport();
    expect(exportIssues).toHaveBeenLastCalledWith({ source: 'overseer', format: 'yaml', issueIds: [42], occurrencesPerIssue: 1 });

    component.open();
    chooseOccurrences('custom');
    typeCustom('1000');
    clickExport();
    expect(exportIssues).toHaveBeenLastCalledWith({ source: 'overseer', format: 'yaml', issueIds: [42], occurrencesPerIssue: 1000 });
  });

  it('gives two instances on one page distinct ids and radio names', () => {
    create('overseer', [1, 2]);
    const first = host;
    component.open();
    chooseOccurrences('custom');
    typeCustom('');
    clickExport();
    first.querySelector<HTMLDialogElement>('dialog')!.close();

    create('overseer', [3], { idPrefix: 'overseer-card', issueSubject: 'One issue', occurrenceTotal: 3 });
    const second = host;
    component.open();
    chooseOccurrences('custom');
    typeCustom('');
    clickExport();

    const ids = (root: HTMLElement): string[] => Array.from(root.querySelectorAll('[id]')).map(el => el.id);
    const firstIds = ids(first);
    const secondIds = ids(second);
    expect(firstIds.length).toBeGreaterThan(3);
    expect(secondIds.length).toBeGreaterThan(firstIds.length);
    expect(firstIds.every(id => id.startsWith('overseer-'))).toBe(true);
    expect(secondIds.every(id => id.startsWith('overseer-card-'))).toBe(true);
    expect(secondIds.filter(id => firstIds.includes(id))).toEqual([]);
    expect(new Set([...firstIds, ...secondIds]).size).toBe(firstIds.length + secondIds.length);
    expect(second.querySelector('h3')!.id).toBe('overseer-card-feedback-export-title');

    const name = (root: HTMLElement): string => root.querySelector<HTMLInputElement>('input[type="radio"]')!.name;
    expect(name(first)).toBe('overseer-feedback-export-format');
    expect(name(second)).toBe('overseer-card-feedback-export-format');
  });

  it('keeps the dialog open and drops the selection when none of the issues exist', () => {
    const saved = captureSaves();
    exportIssues.mockReturnValue(throwError(() => new HttpErrorResponse({
      status: 404, error: { message: 'None of the issues exist.', missingIds: [42], missingFingerprints: [] }
    })));
    create('overseer', [42]);
    component.open();

    clickExport();

    expect(saved.names).toEqual([]);
    expect(q<HTMLDialogElement>('dialog')!.open).toBe(true);
    expect(text(q('.fx-error'))).toContain('None of the selected issues exist any more');
    expect(done).toEqual([{ missing: [42], exportOptions: { occurrencesPerIssue: 10, includeQuestionText: true, format: 'yaml' }, fileName: null, issueCount: 0 }]);
  });

  it('says the one issue is gone when it no longer exists', () => {
    exportIssues.mockReturnValue(throwError(() => new HttpErrorResponse({
      status: 404, error: { message: 'None of the issues exist.', missingIds: [], missingFingerprints: [FP] }
    })));
    create('gnollbench', [FP], { issueSubject: 'Gone', occurrenceTotal: 2 });
    component.open();

    clickExport();

    expect(text(q('.fx-error'))).toBe('This issue no longer exists, so nothing was exported.');
    expect(done).toEqual([{ missing: [FP], exportOptions: { occurrencesPerIssue: 10, includeQuestionText: true, format: 'yaml' }, fileName: null, issueCount: 0 }]);
  });

  it('shows the server\'s message when the export fails', () => {
    exportIssues.mockReturnValue(throwError(() => new HttpErrorResponse({
      status: 400, error: { message: 'This export would hold 6000 occurrences, more than 5,000.' }
    })));
    create('overseer', [42]);
    component.open();

    clickExport();

    expect(text(q('.fx-error'))).toBe('The export failed: This export would hold 6000 occurrences, more than 5,000.');
    expect(done).toEqual([]);
  });

  it('refuses more than 200 issues and says why', () => {
    create('overseer', Array.from({ length: 201 }, (_, i) => i + 1));
    component.open();

    const button = exportButton();
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
