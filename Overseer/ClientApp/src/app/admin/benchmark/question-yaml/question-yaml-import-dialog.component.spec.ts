import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import {
  AdminBenchmarkService,
  BenchmarkQuestionDto,
  BenchmarkSuiteDto
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

  beforeEach(async () => {
    service = jasmine.createSpyObj('AdminBenchmarkService', ['importQuestions', 'importSuite']);

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
    button('Create suite').click();
    fixture.detectChanges();

    expect(service.importSuite).toHaveBeenCalledWith({
      name: 'Core Suite',
      description: null,
      questions: [{ questionId: null, questionText: 'Q', difficulty: null, expectedPoints: 'R', replaceExpectedPoints: true }]
    });
    expect(emitted).toHaveBeenCalledWith(created);
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
