import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import {
  AdminBenchmarkService,
  BenchmarkQuestionDto,
  BenchmarkSuiteDto,
  MatchSnapshotResult
} from '../../../services/admin-benchmark.service';
import { QuestionYamlImportDialogComponent } from './question-yaml-import-dialog.component';
import { serializeQuestionsYaml } from './question-yaml-format';

describe('QuestionYamlImportDialogComponent', () => {
  let fixture: ComponentFixture<QuestionYamlImportDialogComponent>;
  let component: QuestionYamlImportDialogComponent;
  let host: HTMLElement;
  let service: jasmine.SpyObj<AdminBenchmarkService>;

  const suite: BenchmarkSuiteDto = {
    id: 7, name: 'Core Suite', description: null, createdAtUtc: '2026-09-16T00:00:00Z', modifiedAtUtc: null,
    questionCount: 2, assessedQuestionCount: 0, difficultyFullyAssessed: false
  };
  const existing: BenchmarkQuestionDto[] = [
    { id: 17, benchmarkSuiteId: 7, orderIndex: 4, questionText: 'Current question', difficulty: 1, expectedPoints: 'line one\nline two', createdAtUtc: '' },
    { id: 18, benchmarkSuiteId: 7, orderIndex: 5, questionText: 'Other question', difficulty: 2, expectedPoints: null, createdAtUtc: '' }
  ];
  const header = 'format: overseer-benchmark-questions\nversion: 1\n';
  const BOARD = 'GnollHack 4.2.0 Build 47\nDlvl:11 HP:14(58) Hungry';
  /* A suite document that carries a board, with a rubric that has no BOARD FACTS section. */
  const snapshotDoc = header
    + 'suite:\n  name: Core Suite\n  snapshot:\n    name: Valkyrie dlvl 11\n'
    + '    gnollhack_version: "4.2.0 Build 47"\n'
    + '    sha256: "' + 'a'.repeat(64) + '"\n'
    + '    text: |\n      GnollHack 4.2.0 Build 47\n      Dlvl:11 HP:14(58) Hungry\n'
    + 'questions:\n  - question: Q\n    rubric: |\n      **REQUIRED**\n      - A point.\n';

  function noMatch(): MatchSnapshotResult {
    return { sha256: 'a'.repeat(64), charCount: BOARD.length, truncated: false, isHtml: false, match: null };
  }

  beforeEach(async () => {
    service = jasmine.createSpyObj('AdminBenchmarkService', ['importQuestions', 'importSuite', 'matchSnapshot']);
    service.matchSnapshot.and.returnValue(of(noMatch()));

    await TestBed.configureTestingModule({
      imports: [QuestionYamlImportDialogComponent],
      providers: [{ provide: AdminBenchmarkService, useValue: service }]
    }).compileComponents();

    fixture = TestBed.createComponent(QuestionYamlImportDialogComponent);
    component = fixture.componentInstance;
    component.suite = suite;
    component.existing = existing;
    component.suites = [suite];
    fixture.detectChanges();
    host = fixture.nativeElement as HTMLElement;
  });

  afterEach(() => {
    component.dialog?.nativeElement?.close();
  });

  function button(text: string): HTMLButtonElement {
    const found = Array.from(host.querySelectorAll('button'))
      .find(b => (b.textContent ?? '').replace(/\s+/g, ' ').trim() === text);
    if (!found) throw new Error(`No button "${text}"`);
    return found;
  }

  async function paste(text: string): Promise<void> {
    const textarea = host.querySelector('textarea') as HTMLTextAreaElement;
    textarea.value = text;
    textarea.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    await fixture.whenStable();
  }

  it('renders a title for each mode', () => {
    component.open('single', existing[0]);
    expect(host.querySelector('h3')!.textContent).toContain('Import YAML into question #4');
    component.close();

    component.open('questions');
    expect(host.querySelector('h3')!.textContent).toContain('Import questions into Core Suite');
    component.close();

    component.open('suite');
    expect(host.querySelector('h3')!.textContent).toContain('Import a suite from YAML');
  });

  it('switching the source removes the other control', () => {
    component.open('questions');
    expect(host.querySelector('textarea')).not.toBeNull();
    expect(host.querySelector('input[type="file"]')).toBeNull();

    (host.querySelector('input[type="radio"][value="file"]') as HTMLInputElement).click();
    fixture.detectChanges();

    expect(host.querySelector('textarea')).toBeNull();
    expect(host.querySelector('input[type="file"]')).not.toBeNull();
  });

  it('shows the parser errors and keeps Review disabled', async () => {
    component.open('questions');
    await paste(header + 'questions:\n  - question: a\n    tier: hard\n');

    await component.validate();
    fixture.detectChanges();

    const errors = host.querySelectorAll('.import-errors li');
    expect(errors.length).toBe(1);
    expect(errors[0].textContent).toContain('unknown key `tier`');
    expect(button('Review changes').getAttribute('aria-disabled')).toBe('true');
  });

  it('enables Review after a valid paste, and editing the text clears it', async () => {
    component.open('questions');
    await paste(serializeQuestionsYaml([existing[0]], suite));

    await component.validate();
    fixture.detectChanges();
    expect(host.querySelector('.import-valid-summary')!.textContent).toContain('Valid: 1 question to replace.');
    expect(button('Review changes').getAttribute('aria-disabled')).toBeNull();

    await paste(serializeQuestionsYaml([existing[0]], suite) + '# edited\n');
    expect(button('Review changes').getAttribute('aria-disabled')).toBe('true');
  });

  it('reviews side by side and as a diff', async () => {
    component.open('questions');
    const edited = serializeQuestionsYaml([existing[0]], null).replace('line two', 'line 2');
    await paste(edited);
    await component.validate();
    await component.review();
    fixture.detectChanges();

    expect(host.textContent).toContain('Replace #4 (id 17)');
    expect(Array.from(host.querySelectorAll('.import-column h6')).map(h => h.textContent)).toEqual(['Current', 'Imported']);

    button('Diff').click();
    fixture.detectChanges();
    const lines = Array.from(host.querySelectorAll('.diff-line')).map(l => l.textContent);
    expect(lines).toContain('- line two');
    expect(lines).toContain('+ line 2');
  });

  it('shows a rubric notice on the review card and keeps the import enabled', async () => {
    component.open('questions');
    await paste(header + 'questions:\n  - id: 17\n    rubric: |\n      **FORM (readability)**\n      - Lead with the answer.\n');
    await component.validate();
    await component.review();
    fixture.detectChanges();

    const notices = Array.from(host.querySelectorAll('.import-rubric-notice')).map(n => n.textContent!.trim());
    expect(notices.length).toBe(2);
    expect(notices.every(n => n.startsWith('Rubric:'))).toBeTrue();
    expect(notices[0]).toContain('**REQUIRED**');
    expect(button('Apply 1 change').getAttribute('aria-disabled')).toBeNull();
  });

  it('shows no rubric notice for an unchanged rubric', async () => {
    component.open('questions');
    await paste(serializeQuestionsYaml([existing[0]], suite));
    await component.validate();
    await component.review();
    fixture.detectChanges();

    expect(host.querySelectorAll('.import-rubric-notice').length).toBe(0);
  });

  it('applies a questions import without replacing an absent rubric', async () => {
    service.importQuestions.and.returnValue(of({ createdCount: 1, replacedCount: 1, unchangedCount: 0, questions: [] }));
    const emitted = jasmine.createSpy('imported');
    component.imported.subscribe(emitted);

    component.open('questions');
    await paste(header + 'questions:\n  - id: 18\n    difficulty: Advanced\n  - question: |\n      Brand new\n');
    await component.validate();
    await component.review();
    fixture.detectChanges();

    button('Apply 2 changes').click();
    fixture.detectChanges();

    expect(service.importQuestions).toHaveBeenCalledWith(7, {
      items: [
        { questionId: 18, questionText: null, difficulty: 3, expectedPoints: null, replaceExpectedPoints: false },
        { questionId: null, questionText: 'Brand new', difficulty: null, expectedPoints: null, replaceExpectedPoints: false }
      ]
    });
    expect(emitted).toHaveBeenCalled();
    expect(host.querySelector('.import-done')!.textContent).toContain('Replaced 1, created 1, unchanged 0.');
  });

  it('creates a suite in suite mode', async () => {
    const created: BenchmarkSuiteDto = { ...suite, id: 9, name: 'Core Suite (Imported)', questionCount: 1 };
    service.importSuite.and.returnValue(of(created));
    const emitted = jasmine.createSpy('suiteImported');
    component.suiteImported.subscribe(emitted);

    component.open('suite');
    await paste(header + 'suite:\n  name: Core Suite\nquestions:\n  - id: 17\n    question: Q\n    rubric: |\n      R\n');
    await component.validate();
    await component.review();
    fixture.detectChanges();

    expect(host.textContent).toContain('the import will be named Core Suite (Imported)');
    expect(host.textContent).toContain('No game snapshot in this document.');
    button('Create suite').click();
    fixture.detectChanges();

    expect(service.importSuite).toHaveBeenCalledWith({
      name: 'Core Suite',
      description: null,
      questions: [{ questionId: null, questionText: 'Q', difficulty: null, expectedPoints: 'R', replaceExpectedPoints: true }],
      snapshot: null
    });
    expect(emitted).toHaveBeenCalledWith(created);
    expect(service.matchSnapshot).not.toHaveBeenCalled();
  });

  describe('the game snapshot a suite document carries', () => {
    async function reviewSnapshotDoc(): Promise<void> {
      component.open('suite');
      await paste(snapshotDoc);
      await component.validate();
      await component.review();
      fixture.detectChanges();
    }

    it('announces a snapshot that will be created', async () => {
      await reviewSnapshotDoc();

      expect(service.matchSnapshot).toHaveBeenCalledWith('GnollHack 4.2.0 Build 47\nDlvl:11 HP:14(58) Hungry');
      expect(host.textContent).toContain('will be created and attached');
      expect(host.textContent).toContain('GnollHack 4.2.0 Build 47');
    });

    it('announces a reused unattached snapshot, and a copy of one owned by another suite', async () => {
      service.matchSnapshot.and.returnValue(of({ ...noMatch(), match: { id: 5, name: 'Stored board', suiteId: null, suiteName: null } }));
      await reviewSnapshotDoc();
      expect(host.textContent).toContain('An identical snapshot, Stored board, is already stored and belongs to no suite.');

      component.close();
      service.matchSnapshot.and.returnValue(of({ ...noMatch(), match: { id: 5, name: 'Stored board', suiteId: 3, suiteName: 'Other Suite' } }));
      await reviewSnapshotDoc();
      expect(host.textContent).toContain('belongs to suite Other Suite');
      expect(host.textContent).toContain('stores a copy named Stored board (2)');
    });

    it('still imports when the preflight fails', async () => {
      service.matchSnapshot.and.returnValue(throwError(() => new Error('down')));
      await reviewSnapshotDoc();

      expect(component.snapshotCheckState).toBe('failed');
      expect(host.textContent).toContain('Could not check for an identical stored snapshot. The import still attaches one.');
      expect(button('Create suite').disabled).toBeFalse();
    });

    it('warns when the file hash differs from the hash the server computes', async () => {
      service.matchSnapshot.and.returnValue(of({ ...noMatch(), sha256: 'b'.repeat(64) }));
      await reviewSnapshotDoc();

      expect(component.snapshotHashMismatch).toBeTrue();
      expect(host.querySelector('.import-snapshot-warning')!.textContent).toContain('it was edited, or damaged in transit');
    });

    it('sends the snapshot with the box ticked and null with it cleared', async () => {
      const created: BenchmarkSuiteDto = { ...suite, id: 9, name: 'Core Suite (Imported)', questionCount: 1, gameSnapshotId: 12, gameSnapshotName: 'Valkyrie dlvl 11' };
      service.importSuite.and.returnValue(of(created));
      await reviewSnapshotDoc();

      button('Create suite').click();
      fixture.detectChanges();
      expect(service.importSuite.calls.mostRecent().args[0].snapshot).toEqual({
        name: 'Valkyrie dlvl 11',
        text: 'GnollHack 4.2.0 Build 47\nDlvl:11 HP:14(58) Hungry',
        sourceGnollHackVersion: '4.2.0 Build 47',
        capturedAtUtc: null,
        notes: null
      });
      expect(host.querySelector('.import-done')!.textContent).toContain('Created game snapshot Valkyrie dlvl 11.');
      expect(host.querySelector('.import-done')!.textContent).toContain('Run Assess Difficulty before the first benchmark run.');

      component.close();
      await reviewSnapshotDoc();
      const box = host.querySelector('#importAttachSnapshot') as HTMLInputElement;
      box.click();
      fixture.detectChanges();
      expect(component.attachSnapshot).toBeFalse();

      button('Create suite').click();
      fixture.detectChanges();
      expect(service.importSuite.calls.mostRecent().args[0].snapshot).toBeNull();
    });

    it('names the attached snapshot as existing when the response reuses the matched board', async () => {
      service.matchSnapshot.and.returnValue(of({ ...noMatch(), match: { id: 12, name: 'Stored board', suiteId: null, suiteName: null } }));
      service.importSuite.and.returnValue(of({ ...suite, id: 9, name: 'Core Suite (Imported)', questionCount: 1, gameSnapshotId: 12, gameSnapshotName: 'Stored board' }));
      await reviewSnapshotDoc();

      button('Create suite').click();
      fixture.detectChanges();
      expect(host.querySelector('.import-done')!.textContent).toContain('Attached the existing game snapshot Stored board.');
    });

    it('raises the missing BOARD FACTS notice only while the box is ticked', async () => {
      await reviewSnapshotDoc();
      expect(component.cards[0].rubricNotices.map(n => n.code)).toEqual(['no-board-facts']);

      const box = host.querySelector('#importAttachSnapshot') as HTMLInputElement;
      box.click();
      fixture.detectChanges();
      expect(component.cards[0].rubricNotices).toEqual([]);
    });
  });

  it('shows a server error inline and stays on the review step', async () => {
    service.importQuestions.and.returnValue(throwError(() => ({ error: 'Suite question limit reached (50 questions maximum).' })));

    component.open('single', existing[0]);
    await paste(header + 'questions:\n  - question: Changed\n');
    await component.validate();
    await component.review();
    fixture.detectChanges();

    button('Replace question').click();
    fixture.detectChanges();

    expect(host.querySelector('.error-message[role="alert"]')!.textContent).toContain('Suite question limit reached');
    expect(component.step).toBe(2);
  });

  it('reads an uploaded file', async () => {
    component.open('single', existing[0]);
    component.setSource('file');

    await component.loadFile(new File([header + 'questions:\n  - question: From a file\n'], 'q.yaml', { type: 'application/yaml' }));
    fixture.detectChanges();

    expect(host.querySelector('.import-file-info')!.textContent).toContain('q.yaml');
    await expectAsync(component.validate()).toBeResolvedTo(true);
  });
});
