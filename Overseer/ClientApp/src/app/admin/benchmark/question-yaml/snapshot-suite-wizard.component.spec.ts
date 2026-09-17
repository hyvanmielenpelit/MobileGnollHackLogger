import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { of, throwError } from 'rxjs';
import { AdminBenchmarkService, BenchmarkSuiteDto, MatchSnapshotResult } from '../../../services/admin-benchmark.service';
import { MarkdownEditorComponent } from '../../../shared/markdown-editor/markdown-editor.component';
import { SnapshotSuiteWizardComponent, WIZARD_STORAGE_KEY, SnapshotSuiteWizardState } from './snapshot-suite-wizard.component';

describe('SnapshotSuiteWizardComponent', () => {
  let fixture: ComponentFixture<SnapshotSuiteWizardComponent>;
  let component: SnapshotSuiteWizardComponent;
  let host: HTMLElement;
  let service: jasmine.SpyObj<AdminBenchmarkService>;

  const base: BenchmarkSuiteDto = {
    id: 0, name: '', description: null, createdAtUtc: '', modifiedAtUtc: null,
    questionCount: 0, assessedQuestionCount: 0, difficultyFullyAssessed: false
  };
  const full: BenchmarkSuiteDto = { ...base, id: 1, name: 'Busy', questionCount: 3, gameSnapshotId: 11, gameSnapshotName: 'B1', gameSnapshotCharCount: 1200 };
  const empty: BenchmarkSuiteDto = { ...base, id: 2, name: 'Zed Empty', questionCount: 0, gameSnapshotId: 12, gameSnapshotName: 'B2', gameSnapshotCharCount: 900 };
  const plain: BenchmarkSuiteDto = { ...base, id: 3, name: 'No Board', questionCount: 4 };
  const header = 'format: overseer-benchmark-questions\nversion: 1\n';

  beforeEach(async () => {
    localStorage.removeItem(WIZARD_STORAGE_KEY);
    service = jasmine.createSpyObj('AdminBenchmarkService', ['getQuestions', 'importQuestions', 'importSuite', 'matchSnapshot', 'updateSuite']);
    service.getQuestions.and.returnValue(of([]));
    service.matchSnapshot.and.returnValue(of<MatchSnapshotResult>({
      sha256: 'a'.repeat(64), charCount: 10, truncated: false, isHtml: false,
      match: { id: 12, name: 'B2', suiteId: 2, suiteName: 'Zed Empty' }
    }));

    await TestBed.configureTestingModule({
      imports: [SnapshotSuiteWizardComponent],
      providers: [{ provide: AdminBenchmarkService, useValue: service }]
    }).compileComponents();

    fixture = TestBed.createComponent(SnapshotSuiteWizardComponent);
    component = fixture.componentInstance;
    fixture.componentRef.setInput('suites', [full, empty, plain]);
    fixture.detectChanges();
    host = fixture.nativeElement as HTMLElement;
  });

  afterEach(() => {
    component.applying = false;
    component.dialog?.nativeElement?.close();
    localStorage.removeItem(WIZARD_STORAGE_KEY);
  });

  const forward = (): HTMLButtonElement => host.querySelector<HTMLButtonElement>('.wizard-forward')!;
  const heading = (): string => host.querySelector('.wizard-step-heading')!.textContent!.trim();
  const saved = (): SnapshotSuiteWizardState => JSON.parse(localStorage.getItem(WIZARD_STORAGE_KEY)!);

  function click(el: HTMLElement): void {
    el.click();
    fixture.detectChanges();
  }

  function typePath(value: string): void {
    const input = host.querySelector<HTMLInputElement>('#snapshot-wizard-path')!;
    input.value = value;
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();
  }

  async function toPromptStep(): Promise<void> {
    component.open();
    click(host.querySelector<HTMLInputElement>('#snapshot-wizard-route-suite')!);
    click(host.querySelectorAll<HTMLInputElement>('input[name="snapshot-wizard-suite"]')[0]);
    click(forward());
    typePath('C:\\temp\\overseer-suite-export-zed-empty.yaml');
    click(forward());
  }

  it('opens on the source step with no route chosen, and refuses to go on', () => {
    component.open();
    expect(component.dialog.nativeElement.open).toBeTrue();
    expect(host.querySelector('.gh-steps li[aria-current="step"]')!.textContent).toContain('Source');
    expect(host.querySelectorAll<HTMLInputElement>('input[name="snapshot-wizard-route"]:checked').length).toBe(0);
    expect(forward().getAttribute('aria-disabled')).toBe('true');

    click(forward());
    expect(component.step).toBe(1);
    expect(host.querySelector('.wizard-step-error')!.textContent).toContain('Choose where the game snapshot is.');
    expect(document.activeElement!.id).toBe('snapshot-wizard-route-suite');
  });

  it('lists only the suites with a snapshot, the empty ones first', () => {
    component.open();
    click(host.querySelector<HTMLInputElement>('#snapshot-wizard-route-suite')!);
    const rows = Array.from(host.querySelectorAll('.wizard-suite-row')).map(r => r.textContent!.replace(/\s+/g, ' ').trim());
    expect(rows.length).toBe(2);
    expect(rows[0]).toContain('Zed Empty · 0 questions · B2 (900 chars)');
    expect(rows[1]).toContain('Busy · 3 questions');
    expect(rows[1]).toContain('The agent adds new questions and leaves these alone.');
  });

  it('shows the empty state when no suite has a snapshot', () => {
    fixture.componentRef.setInput('suites', [plain]);
    fixture.detectChanges();
    component.open();
    click(host.querySelector<HTMLInputElement>('#snapshot-wizard-route-suite')!);
    expect(host.querySelector('.wizard-empty')!.textContent).toContain('No suite has a game snapshot yet.');
  });

  it('walks route A to the prompt, downloading the suite and remembering each step', async () => {
    const download = jasmine.createSpy('downloadSuite');
    component.downloadSuite.subscribe(download);
    component.open();
    click(host.querySelector<HTMLInputElement>('#snapshot-wizard-route-suite')!);
    click(host.querySelectorAll<HTMLInputElement>('input[name="snapshot-wizard-suite"]')[0]);
    click(forward());

    expect(component.step).toBe(2);
    expect(document.activeElement).toBe(host.querySelector('.wizard-step-heading'));
    const downloadButton = Array.from(host.querySelectorAll<HTMLButtonElement>('.wizard-field .btn-ghost')).find(b => b.textContent!.includes('Download Suite YAML'))!;
    click(downloadButton);
    expect(download).toHaveBeenCalledWith(empty);
    const pair = Array.from(host.querySelectorAll('.wizard-file-pair .wizard-file-row'));
    expect(pair.map(r => r.querySelector('dt')!.textContent!.trim())).toEqual(['You download', 'The agent writes']);
    expect(pair.map(r => r.querySelector('code')!.textContent)).toEqual(['overseer-suite-export-zed-empty.yaml', 'agent-new-questions-zed-empty.yaml']);
    expect(pair[1].classList).toContain('is-upload');

    click(forward());
    expect(component.step).toBe(2);
    expect(document.activeElement!.id).toBe('snapshot-wizard-path');

    typePath('board.ai.html');
    expect(host.textContent).toContain('This does not look like a full path');
    expect(host.textContent).toContain('This does not look like a suite YAML file.');
    typePath('"C:\\temp\\overseer-suite-export-zed-empty.yaml"');
    click(forward());

    expect(component.step).toBe(3);
    expect(saved().step).toBe(3);
    expect(saved().sourcePath).toBe('"C:\\temp\\overseer-suite-export-zed-empty.yaml"');
    expect(forward().getAttribute('aria-disabled')).toBe('true');

    click(host.querySelector<HTMLButtonElement>('#snapshot-wizard-builder-generate')!);
    expect(component.promptOptions!.source).toBe('suite-yaml');
    expect(host.querySelector('app-suite-prompt-builder pre code')!.textContent).toContain('Mode: add questions to an existing suite');
    expect(forward().getAttribute('aria-disabled')).toBeNull();
    expect(host.textContent).toContain('agent-new-questions-zed-empty.yaml');
    expect(host.querySelector('.wizard-after-prompt')!.textContent).toContain('beside the file you downloaded');
  });

  it('imports route A in the wizard and hands the suite on for assessment', async () => {
    const imported = jasmine.createSpy('imported');
    const assess = jasmine.createSpy('assess');
    component.imported.subscribe(imported);
    component.assessRequested.subscribe(assess);
    service.importQuestions.and.returnValue(of({ createdCount: 1, replacedCount: 0, unchangedCount: 0, questions: [] }));

    await toPromptStep();
    click(host.querySelector<HTMLButtonElement>('#snapshot-wizard-builder-generate')!);
    click(forward());

    expect(component.step).toBe(4);
    expect(service.getQuestions).toHaveBeenCalledWith(2);
    expect(component.panel!.source).toBe('file');
    const rows = Array.from(host.querySelectorAll('.wizard-upload-pair .wizard-file-row'));
    expect(rows.length).toBe(2);
    expect(rows[0].classList).toContain('is-upload');
    expect(rows[0].querySelector('dt')!.textContent).toContain('Upload this');
    expect(rows[0].querySelector('code')!.textContent).toBe('agent-new-questions-zed-empty.yaml');
    expect(rows[1].querySelector('dt')!.textContent).toContain('Not this');
    expect(rows[1].querySelector('code')!.textContent).toBe('overseer-suite-export-zed-empty.yaml');
    expect(component.panel!.expectedFileName).toBe('agent-new-questions-zed-empty.yaml');
    expect(component.panel!.downloadedFileName).toBe('overseer-suite-export-zed-empty.yaml');

    component.panel!.setSource('paste');
    const textarea = host.querySelector<HTMLTextAreaElement>('#snapshot-wizard-import-paste')!;
    textarea.value = header + 'suite:\n  name: Zed Empty\n  snapshot:\n    text: |\n      GnollHack 4.2.0\n'
      + 'questions:\n  - difficulty: Simple\n    question: Q\n    rubric: |\n      **BOARD FACTS**\n      - "GnollHack 4.2.0"\n\n      **REQUIRED**\n      - A.\n';
    textarea.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    await component.forward();
    fixture.detectChanges();
    expect(component.step).toBe(5);
    expect(forward().textContent!.trim()).toBe('Add 1 Question to Zed Empty');
    expect(forward().getAttribute('aria-disabled')).toBe('true');

    click(forward());
    expect(service.importQuestions).not.toHaveBeenCalled();
    expect(document.activeElement!.id).toBe('snapshot-wizard-import-confirm');

    click(host.querySelector<HTMLInputElement>('#snapshot-wizard-import-confirm')!);
    expect(forward().getAttribute('aria-disabled')).toBeNull();
    await component.forward();
    fixture.detectChanges();

    expect(service.importQuestions).toHaveBeenCalled();
    expect(imported).toHaveBeenCalled();
    expect(component.step).toBe(6);
    expect(host.querySelector('.wizard-back')!.textContent!.trim()).toBe('Close');
    expect(saved().importedSuiteId).toBe(2);

    const assessButton = Array.from(host.querySelectorAll<HTMLButtonElement>('.wizard-assess button')).find(b => b.textContent!.includes('Assess Difficulty'))!;
    click(assessButton);
    expect(assess).toHaveBeenCalledWith(empty);

    expect(forward().textContent!.trim()).toBe('Next');
    click(forward());
    expect(component.step).toBe(7);
    expect(heading()).toBe('Apply the suggested description');

    click(forward());
    expect(localStorage.getItem(WIZARD_STORAGE_KEY)).toBeNull();
    expect(component.dialog.nativeElement.open).toBeFalse();
  });

  it('offers to resume, and lands a resume inside the import on the upload step', async () => {
    const state: SnapshotSuiteWizardState = {
      v: 1, route: 'add-to-suite', suiteId: 2, sourcePath: 'C:\\t\\s.yaml', suiteName: '',
      counts: { simple: 2, intermediate: 1, advanced: 0 }, waitForGoAhead: true, step: 5, importedSuiteId: null
    };
    localStorage.setItem(WIZARD_STORAGE_KEY, JSON.stringify(state));
    component.open();

    expect(host.querySelector('.wizard-resume')!.textContent).toContain('Continue with Zed Empty, step 5 of 7?');
    const resume = Array.from(host.querySelectorAll<HTMLButtonElement>('.wizard-resume button')).find(b => b.textContent!.trim() === 'Resume')!;
    click(resume);

    expect(component.step).toBe(4);
    expect(component.sourcePath).toBe('C:\\t\\s.yaml');
    expect(component.expectation!.requestedCounts).toEqual({ simple: 2, intermediate: 1, advanced: 0 });
  });

  it('restores the prompt fields on a resume at the prompt step', () => {
    localStorage.setItem(WIZARD_STORAGE_KEY, JSON.stringify({
      v: 1, route: 'create-suite', suiteId: null, sourcePath: 'C:\\t\\b.ai.html', suiteName: 'Valk',
      counts: { simple: 3, intermediate: 3, advanced: 3 }, waitForGoAhead: false, step: 3, importedSuiteId: null
    }));
    component.open();
    expect(host.querySelector('.wizard-resume')!.textContent).toContain('Continue with b.ai.html, step 3 of 6?');
    component.resume();
    fixture.detectChanges();

    expect(component.step).toBe(3);
    expect(host.querySelector('.wizard-after-prompt')!.textContent).toContain('agent-new-suite-valk.yaml beside the snapshot');
    expect(component.builder!.suiteName).toBe('Valk');
    expect(component.promptOptions!.counts).toEqual({ simple: 3, intermediate: 3, advanced: 3 });
    expect(host.querySelector('app-suite-prompt-builder pre code')!.textContent).toContain('3 Simple / 3 Intermediate / 3 Advanced');
  });

  it('starts over, clearing the saved state', () => {
    localStorage.setItem(WIZARD_STORAGE_KEY, JSON.stringify({
      v: 1, route: 'create-suite', suiteId: null, sourcePath: 'x', suiteName: '', counts: null, waitForGoAhead: true, step: 2, importedSuiteId: null
    }));
    component.open();
    component.startOver();
    fixture.detectChanges();
    expect(component.step).toBe(1);
    expect(component.route).toBeNull();
    expect(saved().step).toBe(1);
  });

  it('discards a saved state whose suite has gone, and says so', () => {
    localStorage.setItem(WIZARD_STORAGE_KEY, JSON.stringify({
      v: 1, route: 'add-to-suite', suiteId: 99, sourcePath: 'x', suiteName: '', counts: null, waitForGoAhead: true, step: 3, importedSuiteId: null
    }));
    component.open();
    expect(host.querySelector('.wizard-resume')).toBeNull();
    expect(host.querySelector('.wizard-notice')!.textContent).toContain('discarded');
    expect(localStorage.getItem(WIZARD_STORAGE_KEY)).toBeNull();
  });

  it('ignores a saved state of another version', () => {
    localStorage.setItem(WIZARD_STORAGE_KEY, JSON.stringify({ v: 99, step: 3 }));
    component.open();
    expect(host.querySelector('.wizard-resume')).toBeNull();
    expect(component.step).toBe(1);
  });

  it('checks a snapshot file locally for route B and advises without blocking', async () => {
    component.open();
    click(host.querySelector<HTMLInputElement>('#snapshot-wizard-route-file')!);
    click(forward());
    expect(component.step).toBe(2);
    expect(host.querySelector('.wizard-file-pair')).toBeNull();
    expect(host.querySelector('#snapshot-wizard-check-file-label')!.textContent).toContain('(optional)');
    expect(host.querySelector('#snapshot-wizard-check-file')!.getAttribute('aria-describedby')).toContain('snapshot-wizard-check-file-hint');

    await component.checkFile(new File(['<html><body><pre>GnollHack 4.2.0\nDlvl:1</pre></body></html>'], 'valk.ai.html'));
    fixture.detectChanges();
    expect(host.querySelector('.wizard-status')!.textContent).toContain('valk.ai.html looks like an exported .ai.html snapshot');
    expect(host.querySelector('#snapshot-wizard-check-file-card .gh-file-card-name')!.textContent).toBe('valk.ai.html');
    expect(host.querySelector<HTMLInputElement>('#snapshot-wizard-path')!.placeholder).toContain('valk.ai.html');

    click(host.querySelector<HTMLButtonElement>('#snapshot-wizard-check-file-remove')!);
    expect(component.checkedFileName).toBeNull();
    expect(host.querySelector('.wizard-status')).toBeNull();
    expect(host.querySelector('input[type="file"]#snapshot-wizard-check-file')).not.toBeNull();

    typePath('C:\\t\\suite.yaml');
    expect(host.textContent).toContain('This looks like a suite YAML; that is the other route.');
    click(forward());
    expect(component.step).toBe(3);
  });

  it('refuses Escape and Close while an import is applying', () => {
    component.open();
    component.applying = true;
    const event = new Event('cancel', { cancelable: true });
    component.onCancel(event);
    expect(event.defaultPrevented).toBeTrue();
    component.close();
    expect(component.dialog.nativeElement.open).toBeTrue();
  });

  it('has no duplicate element ids on any early step', async () => {
    await toPromptStep();
    click(host.querySelector<HTMLButtonElement>('#snapshot-wizard-builder-generate')!);
    host.querySelector<HTMLDetailsElement>('.wizard-checklist')!.open = true;
    fixture.detectChanges();
    const ids = Array.from(host.querySelectorAll('[id]')).map(e => e.id);
    expect(new Set(ids).size).toBe(ids.length);
  });

  describe('after the import', () => {
    /** Suite 2 after questions were added to it: one of three assessed. */
    const added: BenchmarkSuiteDto = { ...empty, description: 'Old text.', questionCount: 3, assessedQuestionCount: 1 };
    const assessedSuite: BenchmarkSuiteDto = { ...added, assessedQuestionCount: 3, difficultyFullyAssessed: true };

    function stateAt(step: 6 | 7, overrides: Partial<SnapshotSuiteWizardState> = {}): SnapshotSuiteWizardState {
      return {
        v: 1, route: 'add-to-suite', suiteId: 2, sourcePath: 'C:\\t\\s.yaml', suiteName: '',
        counts: null, waitForGoAhead: true, step, importedSuiteId: 2,
        suggestedDescription: 'The **whole** suite.', agentFileName: 'agent-new-questions-zed-empty.yaml',
        ...overrides
      };
    }

    function resumeAt(state: SnapshotSuiteWizardState, suites: BenchmarkSuiteDto[] = [full, added, plain]): void {
      fixture.componentRef.setInput('suites', suites);
      fixture.detectChanges();
      localStorage.setItem(WIZARD_STORAGE_KEY, JSON.stringify(state));
      component.open();
      component.resume();
      fixture.detectChanges();
    }

    const stepItems = () => Array.from(host.querySelectorAll('.gh-steps li'));
    const assessButton = () => host.querySelector<HTMLButtonElement>('.wizard-assess-button')!;
    const applyButton = () => host.querySelector<HTMLButtonElement>('.wizard-apply-description')!;
    const textarea = () => host.querySelector<HTMLTextAreaElement>('#snapshot-wizard-description')!;
    const editor = () => fixture.debugElement.query(By.directive(MarkdownEditorComponent)).componentInstance as MarkdownEditorComponent;

    /** ngModel writes the textarea's value after the change detection that set it. */
    async function settle(): Promise<void> {
      await fixture.whenStable();
      fixture.detectChanges();
    }

    function typeDescription(value: string): void {
      textarea().value = value;
      textarea().dispatchEvent(new Event('input'));
      fixture.detectChanges();
    }

    it('route B has six steps and ends with Done on step 6', () => {
      resumeAt(stateAt(6, { route: 'create-suite', suiteId: null, suggestedDescription: null, agentFileName: null }));
      expect(stepItems().length).toBe(6);
      expect(component.step).toBe(6);
      expect(forward().textContent!.trim()).toBe('Done');
      expect(host.querySelector('.wizard-assess')!.textContent).toContain('Optional next checks');
    });

    it('route A has seven steps, Next on step 6, and marks step 6 done only once the suite is assessed', () => {
      resumeAt(stateAt(6));
      expect(stepItems().map(li => li.textContent!.replace(/\s+/g, ' ').trim()).pop()).toBe('7 Describe');
      expect(forward().textContent!.trim()).toBe('Next');
      expect(host.querySelector('.wizard-assess-status')!.textContent)
        .toContain('2 of 3 questions still need an AI-assessed difficulty. The suite cannot run until they have one.');
      expect(stepItems()[5].classList).not.toContain('is-done');
      expect(host.querySelector('.wizard-done-row')).toBeNull();

      const region = host.querySelector('.wizard-assess-announcement')!;
      expect(region.getAttribute('role')).toBe('status');
      expect(region.textContent!.trim()).toBe('');
      const button = assessButton();
      expect(button.classList).toContain('btn-gh');

      fixture.componentRef.setInput('suites', [full, assessedSuite, plain]);
      fixture.detectChanges();

      expect(host.querySelector('.wizard-done-row')!.textContent!.replace(/\s+/g, ' ').trim())
        .toBe('Done. All 3 questions have an AI-assessed difficulty.');
      expect(stepItems()[5].classList).toContain('is-done');
      expect(stepItems()[5].querySelector('.visually-hidden')!.textContent).toContain('Completed:');
      expect(stepItems()[5].querySelector('.gh-step-number svg')).not.toBeNull();
      expect(host.querySelector('.wizard-assess-announcement')).toBe(region);
      expect(region.textContent!.trim())
        .toBe('Difficulty assessment complete: all 3 questions are assessed. Next: the suggested description.');
      expect(assessButton()).toBe(button);
      expect(button.classList).toContain('btn-ghost');
      expect(button.classList).not.toContain('btn-gh');

      fixture.componentRef.setInput('suites', [full, { ...assessedSuite }, plain]);
      fixture.detectChanges();
      expect(region.textContent!.trim()).toBe('Difficulty assessment complete: all 3 questions are assessed. Next: the suggested description.');
    });

    it('keeps step 6 unfinished on step 7 when it was skipped', () => {
      resumeAt(stateAt(6));
      click(forward());
      expect(component.step).toBe(7);
      expect(stepItems()[5].classList).not.toContain('is-done');
      expect(stepItems()[6].getAttribute('aria-current')).toBe('step');
    });

    it('seeds step 7 from the file, in split, and applies the description with the fresh name', async () => {
      const updated = jasmine.createSpy('suiteUpdated');
      component.suiteUpdated.subscribe(updated);
      service.updateSuite.and.returnValue(of(undefined));
      resumeAt(stateAt(7));
      await settle();

      expect(component.step).toBe(7);
      expect(textarea().value).toBe('The **whole** suite.');
      expect(editor().initialMode).toBe('split');
      expect(host.querySelector('.wizard-describe')!.textContent).toContain('From suite.suggested_description in agent-new-questions-zed-empty.yaml.');
      expect(host.querySelector('.wizard-no-suggestion')).toBeNull();
      expect(host.querySelector('.wizard-panel')!.hasAttribute('hidden')).toBeTrue();
      expect(host.querySelector('.wizard-current-description')!.textContent).toContain('Old text.');
      expect(applyButton().textContent!.trim()).toBe('Apply Suggested Description');
      expect(applyButton().getAttribute('aria-disabled')).toBeNull();

      typeDescription('   ');
      expect(applyButton().getAttribute('aria-disabled')).toBe('true');
      click(applyButton());
      expect(service.updateSuite).not.toHaveBeenCalled();
      expect(host.querySelector('.wizard-step-error')!.textContent).toContain('Write or paste a description first.');

      typeDescription('Old text.');
      expect(applyButton().getAttribute('aria-disabled')).toBe('true');

      const renamed = { ...added, name: 'Zed Renamed' };
      fixture.componentRef.setInput('suites', [full, renamed, plain]);
      typeDescription('A new description.');
      expect(applyButton().getAttribute('aria-disabled')).toBeNull();
      click(applyButton());

      expect(service.updateSuite).toHaveBeenCalledWith(2, { name: 'Zed Renamed', description: 'A new description.' });
      expect(host.querySelector('.wizard-apply-status')!.textContent).toBe('Description applied to Zed Renamed.');
      expect(updated).toHaveBeenCalledWith(renamed);
      expect(stepItems()[6].classList).toContain('is-done');
      expect(applyButton().getAttribute('aria-disabled')).toBe('true');
    });

    it('shows a failed save inline and keeps the draft', () => {
      service.updateSuite.and.returnValue(throwError(() => ({ error: 'A suite with this name already exists.' })));
      resumeAt(stateAt(7));
      typeDescription('Draft kept.');
      click(applyButton());
      expect(host.querySelector('.wizard-describe .error-message[role="alert"]')!.textContent).toContain('A suite with this name already exists.');
      expect(textarea().value).toBe('Draft kept.');
      expect(component.descriptionApplied).toBeFalse();
    });

    it('says the file included no suggested description, and describes the empty editor with it', () => {
      resumeAt(stateAt(7, { suggestedDescription: null }));
      const notice = host.querySelector('.wizard-no-suggestion')!;
      expect(notice.textContent).toContain('No suggested description was included in the YAML file (agent-new-questions-zed-empty.yaml).');
      expect(textarea().value).toBe('');
      expect(textarea().placeholder).toBe('Paste the suggested description here');
      expect(editor().initialMode).toBe('write');
      expect(editor().mode).toBe('write');
      expect(textarea().getAttribute('aria-describedby')).toContain(notice.id);
      expect(host.querySelector('label[for="snapshot-wizard-description"]')!.textContent).toContain('Description to apply');
      expect(applyButton().getAttribute('aria-disabled')).toBe('true');

      typeDescription('Pasted from the handoff.');
      expect(applyButton().getAttribute('aria-disabled')).toBeNull();
    });

    it('names no file for a pasted document', () => {
      resumeAt(stateAt(7, { suggestedDescription: null, agentFileName: null }));
      expect(host.querySelector('.wizard-no-suggestion')!.textContent)
        .toContain('No suggested description was included in the YAML file. Paste the description');
    });

    it('goes back from 7 to 6 but not from 6 to 5', () => {
      resumeAt(stateAt(7));
      expect(host.querySelector('.wizard-back')!.textContent!.trim()).toBe('Back');
      click(host.querySelector<HTMLButtonElement>('.wizard-back')!);
      expect(component.step).toBe(6);
      expect(host.querySelector('.wizard-back')!.textContent!.trim()).toBe('Close');
      component.back();
      expect(component.step).toBe(6);
    });

    it('remembers step 7, the suggestion and the file name, and resumes a state saved without them', async () => {
      resumeAt(stateAt(6));
      click(forward());
      expect(saved().step).toBe(7);
      expect(saved().suggestedDescription).toBe('The **whole** suite.');
      expect(saved().agentFileName).toBe('agent-new-questions-zed-empty.yaml');

      component.dialog.nativeElement.close();
      component.open();
      fixture.detectChanges();
      expect(host.querySelector('.wizard-resume')!.textContent).toContain('step 7 of 7?');
      component.resume();
      fixture.detectChanges();
      await settle();
      expect(component.step).toBe(7);
      expect(textarea().value).toBe('The **whole** suite.');

      const older = stateAt(6);
      delete older.suggestedDescription;
      delete older.agentFileName;
      component.dialog.nativeElement.close();
      resumeAt(older);
      expect(component.step).toBe(6);
      click(forward());
      expect(host.querySelector('.wizard-no-suggestion')!.textContent).toContain('No suggested description was included in the YAML file.');
    });

    it('carries the suggestion and the uploaded file name from the import panel', () => {
      service.importQuestions.and.returnValue(of({ createdCount: 1, replacedCount: 0, unchangedCount: 0, questions: [] }));
      resumeAt(stateAt(6));
      const panel = component.panel!;
      panel.suggestedDescription = 'From the panel.';
      panel.source = 'file';
      panel.fileName = 'agent-new-questions-x.yaml';
      component.onQuestionsImported({ createdCount: 1, replacedCount: 0, unchangedCount: 0, questions: [] });
      expect(component.suggestedDescription).toBe('From the panel.');
      expect(component.agentFileName).toBe('agent-new-questions-x.yaml');

      panel.source = 'paste';
      component.onQuestionsImported({ createdCount: 1, replacedCount: 0, unchangedCount: 0, questions: [] });
      expect(component.agentFileName).toBeNull();
    });

    it('no longer offers Edit suite', () => {
      resumeAt(stateAt(6));
      expect(host.querySelector('.wizard-edit-suite')).toBeNull();
      expect((component as unknown as Record<string, unknown>)['editSuiteRequested']).toBeUndefined();
    });
  });

  it('shows the generic checklist before a route and the route checklist after', () => {
    component.open();
    expect(component.instructions).toContain('## B — A snapshot file from GnollHack');
    click(host.querySelector<HTMLInputElement>('#snapshot-wizard-route-file')!);
    expect(component.instructions).not.toContain('## A —');
  });
});
