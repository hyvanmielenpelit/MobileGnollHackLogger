import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import {
  AdminBenchmarkService,
  BenchmarkQuestionDto,
  BenchmarkSuiteDto,
  BoardFactsCheckDto,
  MatchSnapshotResult
} from '../../../services/admin-benchmark.service';
import { QuestionYamlImportPanelComponent } from './question-yaml-import-panel.component';
import { ImportMode, serializeQuestionsYaml } from './question-yaml-format';
import { ImportExpectation } from './import-expectation';

describe('QuestionYamlImportPanelComponent', () => {
  let fixture: ComponentFixture<QuestionYamlImportPanelComponent>;
  let component: QuestionYamlImportPanelComponent;
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
    service = jasmine.createSpyObj('AdminBenchmarkService', ['importQuestions', 'importSuite', 'matchSnapshot', 'getBoardFactsCheck']);
    service.matchSnapshot.and.returnValue(of(noMatch()));
    service.getBoardFactsCheck.and.returnValue(of(null));

    await TestBed.configureTestingModule({
      imports: [QuestionYamlImportPanelComponent],
      providers: [{ provide: AdminBenchmarkService, useValue: service }]
    }).compileComponents();

    fixture = TestBed.createComponent(QuestionYamlImportPanelComponent);
    component = fixture.componentInstance;
    fixture.componentRef.setInput('idPrefix', 'panel-test');
    fixture.componentRef.setInput('suite', suite);
    fixture.componentRef.setInput('existing', existing);
    fixture.componentRef.setInput('suites', [suite]);
    fixture.detectChanges();
    host = fixture.nativeElement as HTMLElement;
  });

  /** What the dialog's open() does for the panel: set the mode and target, then reset. */
  function open(mode: ImportMode, target?: BenchmarkQuestionDto): void {
    fixture.componentRef.setInput('mode', mode);
    fixture.componentRef.setInput('target', target ?? null);
    fixture.detectChanges();
    component.reset();
  }

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

  it('switching the source removes the other control', () => {
    open('questions');
    expect(host.querySelector('textarea')).not.toBeNull();
    expect(host.querySelector('input[type="file"]')).toBeNull();

    (host.querySelector('input[type="radio"][value="file"]') as HTMLInputElement).click();
    fixture.detectChanges();

    expect(host.querySelector('textarea')).toBeNull();
    expect(host.querySelector('input[type="file"]')).not.toBeNull();
  });

  it('prefixes its element ids and radio group name', () => {
    open('questions');
    expect(host.querySelector('textarea')!.id).toBe('panel-test-paste');
    expect(host.querySelector('input[type="radio"]')!.getAttribute('name')).toBe('panel-test-source');
  });

  it('shows the parser errors and keeps Review unavailable', async () => {
    open('questions');
    await paste(header + 'questions:\n  - question: a\n    tier: hard\n');

    await component.validate();
    fixture.detectChanges();

    const errors = host.querySelectorAll('.import-errors li');
    expect(errors.length).toBe(1);
    expect(errors[0].textContent).toContain('unknown key `tier`');
    expect(component.canReview).toBeFalse();
  });

  it('enables Review after a valid paste, and editing the text clears it', async () => {
    open('questions');
    await paste(serializeQuestionsYaml([existing[0]], suite));

    await component.validate();
    fixture.detectChanges();
    expect(host.querySelector('.import-valid-summary')!.textContent).toContain('Valid: 1 question to replace.');
    expect(component.canReview).toBeTrue();

    await paste(serializeQuestionsYaml([existing[0]], suite) + '# edited\n');
    expect(component.canReview).toBeFalse();
  });

  it('reviews side by side and as a diff', async () => {
    open('questions');
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
    open('questions');
    await paste(header + 'questions:\n  - id: 17\n    rubric: |\n      **FORM (readability)**\n      - Lead with the answer.\n');
    await component.validate();
    await component.review();
    fixture.detectChanges();

    const notices = Array.from(host.querySelectorAll('.import-rubric-notice')).map(n => n.textContent!.trim());
    expect(notices.length).toBe(2);
    expect(notices.every(n => n.startsWith('Rubric:'))).toBeTrue();
    expect(notices[0]).toContain('**REQUIRED**');
    expect(component.applyLabel).toBe('Apply 1 change');
    expect(component.canApply).toBeTrue();
  });

  it('shows no rubric notice for an unchanged rubric', async () => {
    open('questions');
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

    open('questions');
    await paste(header + 'questions:\n  - id: 18\n    difficulty: Advanced\n  - question: |\n      Brand new\n');
    await component.validate();
    await component.review();
    fixture.detectChanges();

    expect(component.applyLabel).toBe('Apply 2 changes');
    component.apply();
    fixture.detectChanges();

    expect(service.importQuestions).toHaveBeenCalledWith(7, {
      items: [
        { questionId: 18, questionText: null, difficulty: 3, expectedPoints: null, replaceExpectedPoints: false },
        { questionId: null, questionText: 'Brand new', difficulty: null, expectedPoints: null, replaceExpectedPoints: false }
      ]
    });
    expect(emitted).toHaveBeenCalled();
    expect(host.querySelector('.import-done')!.textContent).toContain('Replaced 1, created 1, unchanged 0.');
    expect(host.querySelector('.import-intent')).toBeNull();
  });

  it('lists a missing board-facts literal the import result carries, with the repair sentence', async () => {
    const check: BoardFactsCheckDto = {
      bulletCount: 10, checkedLiteralCount: 9, unquotedBulletCount: 2,
      unquotedBullets: [],
      missingLiterals: [{ questionId: 18, orderIndex: 5, literal: 'the uncursed Holy Grail', lineExcerpt: 'T - the Holy Grail' }]
    };
    service.importQuestions.and.returnValue(of({ createdCount: 0, replacedCount: 1, unchangedCount: 0, questions: [], boardFactsCheck: check }));

    open('questions');
    await paste(header + 'questions:\n  - id: 18\n    difficulty: Advanced\n');
    await component.validate();
    await component.review();
    fixture.detectChanges();
    component.apply();
    fixture.detectChanges();

    const notice = host.querySelector('.board-facts-notice')!;
    expect(notice.textContent).toContain('Q6: "the uncursed Holy Grail"');
    expect(notice.textContent).toContain('server_rubric_handoff');
    expect(host.querySelector('.board-facts-unquoted')!.textContent).toContain('2 BOARD FACTS line');
    expect(service.getBoardFactsCheck).not.toHaveBeenCalled();
  });

  it('creates a suite in suite mode', async () => {
    const created: BenchmarkSuiteDto = { ...suite, id: 9, name: 'Core Suite (Imported)', questionCount: 1 };
    service.importSuite.and.returnValue(of(created));
    const emitted = jasmine.createSpy('suiteImported');
    component.suiteImported.subscribe(emitted);

    open('suite');
    await paste(header + 'suite:\n  name: Core Suite\nquestions:\n  - id: 17\n    question: Q\n    rubric: |\n      R\n');
    await component.validate();
    await component.review();
    fixture.detectChanges();

    expect(host.textContent).toContain('the import will be named Core Suite (Imported)');
    expect(host.textContent).toContain('No game snapshot in this document.');
    expect(component.applyLabel).toBe('Create suite');
    component.apply();
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

  it('fetches the board-facts check on demand after a successful suite import', async () => {
    const created: BenchmarkSuiteDto = { ...suite, id: 9, name: 'Core Suite (Imported)', questionCount: 1 };
    service.importSuite.and.returnValue(of(created));
    const check: BoardFactsCheckDto = {
      bulletCount: 4, checkedLiteralCount: 3, unquotedBulletCount: 0,
      unquotedBullets: [],
      missingLiterals: [{ questionId: 17, orderIndex: 0, literal: 'a level 3 peaceful dwarf', lineExcerpt: 'h - a level 3 peaceful dwarf' }]
    };
    service.getBoardFactsCheck.and.returnValue(of(check));

    open('suite');
    await paste(header + 'suite:\n  name: Core Suite\nquestions:\n  - id: 17\n    question: Q\n    rubric: |\n      R\n');
    await component.validate();
    await component.review();
    fixture.detectChanges();
    component.apply();
    fixture.detectChanges();

    expect(service.getBoardFactsCheck).toHaveBeenCalledWith(9);
    const notice = host.querySelector('.board-facts-notice')!;
    expect(notice.textContent).toContain('Q1: "a level 3 peaceful dwarf"');
  });

  describe('the game snapshot a suite document carries', () => {
    async function reviewSnapshotDoc(): Promise<void> {
      open('suite');
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
      expect(component.canApply).toBeTrue();
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

      component.apply();
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

      await reviewSnapshotDoc();
      const box = host.querySelector('#panel-test-attach-snapshot') as HTMLInputElement;
      box.click();
      fixture.detectChanges();
      expect(component.attachSnapshot).toBeFalse();

      component.apply();
      fixture.detectChanges();
      expect(service.importSuite.calls.mostRecent().args[0].snapshot).toBeNull();
    });

    it('names the attached snapshot as existing when the response reuses the matched board', async () => {
      service.matchSnapshot.and.returnValue(of({ ...noMatch(), match: { id: 12, name: 'Stored board', suiteId: null, suiteName: null } }));
      service.importSuite.and.returnValue(of({ ...suite, id: 9, name: 'Core Suite (Imported)', questionCount: 1, gameSnapshotId: 12, gameSnapshotName: 'Stored board' }));
      await reviewSnapshotDoc();

      component.apply();
      fixture.detectChanges();
      expect(host.querySelector('.import-done')!.textContent).toContain('Attached the existing game snapshot Stored board.');
    });

    it('raises the missing BOARD FACTS notice only while the box is ticked', async () => {
      await reviewSnapshotDoc();
      expect(component.cards[0].rubricNotices.map(n => n.code)).toEqual(['no-board-facts']);

      const box = host.querySelector('#panel-test-attach-snapshot') as HTMLInputElement;
      box.click();
      fixture.detectChanges();
      expect(component.cards[0].rubricNotices).toEqual([]);
    });
  });

  it('shows a server error inline and stays on the review step', async () => {
    service.importQuestions.and.returnValue(throwError(() => ({ error: 'Suite question limit reached (50 questions maximum).' })));

    open('single', existing[0]);
    await paste(header + 'questions:\n  - question: Changed\n');
    await component.validate();
    await component.review();
    fixture.detectChanges();

    expect(component.applyLabel).toBe('Replace question');
    component.apply();
    fixture.detectChanges();

    expect(host.querySelector('.error-message[role="alert"]')!.textContent).toContain('Suite question limit reached');
    expect(component.step).toBe(2);
  });

  it('reads an uploaded file', async () => {
    open('single', existing[0]);
    component.setSource('file');

    await component.loadFile(new File([header + 'questions:\n  - question: From a file\n'], 'q.yaml', { type: 'application/yaml' }));
    fixture.detectChanges();

    const card = host.querySelector('#panel-test-file-card')!;
    expect(card.querySelector('.gh-file-card-name')!.textContent).toBe('q.yaml');
    expect(host.querySelector('input[type="file"]')).toBeNull();
    expect(host.querySelector('.import-file-advice')).toBeNull();
    await expectAsync(component.validate()).toBeResolvedTo(true);
  });

  it('returns to the empty picker when the attached file is removed', async () => {
    open('questions');
    component.setSource('file');
    await component.loadFile(new File(['x'], 'q.yaml'));
    fixture.detectChanges();

    (host.querySelector('#panel-test-file-remove') as HTMLButtonElement).click();
    fixture.detectChanges();

    expect(component.fileText).toBeNull();
    expect(host.querySelector('#panel-test-file-card')).toBeNull();
    expect(host.querySelector('input[type="file"]#panel-test-file')).not.toBeNull();
  });

  describe('file-name advice', () => {
    async function attach(name: string, expected: string | null, downloaded: string | null): Promise<void> {
      fixture.componentRef.setInput('expectedFileName', expected);
      fixture.componentRef.setInput('downloadedFileName', downloaded);
      open('questions');
      component.setSource('file');
      await component.loadFile(new File(['format: x'], name));
      fixture.detectChanges();
    }

    it('advises against the downloaded suite export, by pattern or by exact name', async () => {
      await attach('overseer-suite-export-core (1).yaml', 'agent-new-questions-core.yaml', 'overseer-suite-export-core.yaml');
      expect(host.querySelector('.import-file-advice')!.textContent)
        .toContain('This is the file you downloaded for the agent. Upload agent-new-questions-core.yaml instead.');

      await attach('renamed.yaml', 'agent-new-questions-core.yaml', 'renamed.yaml');
      expect(host.querySelector('.import-file-advice')).not.toBeNull();
    });

    it('says nothing for another name, nor without the wizard\'s file names', async () => {
      await attach('something-else.yaml', 'agent-new-questions-core.yaml', 'overseer-suite-export-core.yaml');
      expect(host.querySelector('.import-file-advice')).toBeNull();

      await attach('overseer-suite-export-core.yaml', null, null);
      expect(host.querySelector('.import-file-advice')).toBeNull();

      await attach('overseer-suite-export-core.yaml', 'agent-new-suite-core.yaml', null);
      expect(host.querySelector('.import-file-advice')).toBeNull();
    });

    it('never blocks validation', async () => {
      await attach('overseer-suite-export-core.yaml', 'agent-new-questions-core.yaml', 'overseer-suite-export-core.yaml');
      expect(host.querySelector('.import-file-advice')).not.toBeNull();
      expect(component.canValidate).toBeTrue();
    });
  });

  describe('with an expectation', () => {
    const snapshotSuite: BenchmarkSuiteDto = { ...suite, questionCount: 0, gameSnapshotId: 3, gameSnapshotName: 'Board' };
    const boardBlock = '  snapshot:\n    name: Board\n    text: |\n      GnollHack 4.2.0 Build 47\n';
    const rubric = '    rubric: |\n      **BOARD FACTS**\n      - "GnollHack 4.2.0 Build 47"\n\n      **REQUIRED**\n      - A point.\n';
    const addDoc = header + 'suite:\n  name: Core Suite\n' + boardBlock
      + 'questions:\n  - difficulty: Simple\n    question: One\n' + rubric + '  - difficulty: Simple\n    question: Two\n' + rubric;

    function expect_(route: 'add-to-suite' | 'create-suite'): ImportExpectation {
      return {
        route, targetSuite: route === 'add-to-suite' ? snapshotSuite : null,
        requestedSuiteName: null, requestedCounts: null, maxQuestionsPerSuite: 50
      };
    }

    async function reviewWith(route: 'add-to-suite' | 'create-suite', text: string): Promise<boolean> {
      fixture.componentRef.setInput('suite', route === 'add-to-suite' ? snapshotSuite : null);
      fixture.componentRef.setInput('existing', []);
      fixture.componentRef.setInput('expectation', expect_(route));
      open(route === 'add-to-suite' ? 'questions' : 'suite');
      await paste(text);
      const ok = await component.validateAndReview();
      fixture.detectChanges();
      return ok;
    }

    it('states the outcome, lists the checks and waits for the confirmation', async () => {
      service.matchSnapshot.and.returnValue(of({ ...noMatch(), match: { id: 3, name: 'Board', suiteId: 7, suiteName: 'Core Suite' } }));
      expect(await reviewWith('add-to-suite', addDoc)).toBeTrue();

      expect(host.querySelector('.import-outcome')!.textContent).toContain('add 2 new questions');
      expect(host.querySelector('[data-code="board-matches"]')!.textContent).toContain('The board in the file is the board stored on this suite.');
      expect(component.applyLabel).toBe('Add 2 Questions to Core Suite');
      expect(component.canApply).toBeFalse();

      (host.querySelector('#panel-test-confirm') as HTMLInputElement).click();
      fixture.detectChanges();
      expect(component.canApply).toBeTrue();
    });

    it('keeps the suggested description of the validated file, and clears it on reset', async () => {
      const withSuggestion = addDoc.replace('  name: Core Suite\n', '  name: Core Suite\n  suggested_description: |\n    The **whole** suite.\n');
      expect(await reviewWith('add-to-suite', withSuggestion)).toBeTrue();
      expect(component.suggestedDescription).toBe('The **whole** suite.');
      expect(host.querySelector('.import-outcome')!.textContent).toContain('The file\'s suggested description is offered in the next steps.');

      component.reset();
      expect(component.suggestedDescription).toBeNull();

      expect(await reviewWith('add-to-suite', addDoc)).toBeTrue();
      expect(component.suggestedDescription).toBeNull();
      expect(host.querySelector('.import-outcome')!.textContent).not.toContain('suggested description');
    });

    it('checks the result against the intent after apply', async () => {
      service.matchSnapshot.and.returnValue(of({ ...noMatch(), match: { id: 3, name: 'Board', suiteId: 7, suiteName: 'Core Suite' } }));
      service.importQuestions.and.returnValue(of({ createdCount: 2, replacedCount: 0, unchangedCount: 0, questions: [] }));
      await reviewWith('add-to-suite', addDoc);
      component.onConfirmedChange(true);
      component.apply();
      fixture.detectChanges();
      expect(host.querySelector('.import-intent')!.textContent).toContain('As intended: created 2, replaced 0.');
    });

    it('blocks a file that replaces a question even when confirmed', async () => {
      fixture.componentRef.setInput('existing', existing);
      const withId = header + 'questions:\n  - id: 17\n    question: Changed\n';
      fixture.componentRef.setInput('suite', snapshotSuite);
      fixture.componentRef.setInput('expectation', expect_('add-to-suite'));
      open('questions');
      await paste(withId);
      await component.validateAndReview();
      component.onConfirmedChange(true);
      fixture.detectChanges();

      expect(component.blockingFindings.map(f => f.code)).toEqual(['replaces-questions']);
      expect(component.canApply).toBeFalse();
      component.apply();
      expect(service.importQuestions).not.toHaveBeenCalled();
    });

    it('hints that an empty downloaded suite file is the wrong file', async () => {
      const downloaded = header + 'suite:\n  name: Core Suite\n' + boardBlock + 'questions: []\n';
      expect(await reviewWith('add-to-suite', downloaded)).toBeFalse();
      expect(host.querySelector('.import-downloaded-hint')!.textContent).toContain('the file you downloaded for the agent');
    });

    it('route B offers no attach checkbox and blocks a file without a board', async () => {
      await reviewWith('create-suite', snapshotDoc);
      expect(host.querySelector('#panel-test-attach-snapshot')).toBeNull();
      expect(component.applyLabel).toBe('Create Suite Core Suite (Imported)');

      await reviewWith('create-suite', header + 'suite:\n  name: New\nquestions:\n  - question: Q\n');
      expect(component.blockingFindings.map(f => f.code)).toEqual(['no-board']);
    });
  });
});
