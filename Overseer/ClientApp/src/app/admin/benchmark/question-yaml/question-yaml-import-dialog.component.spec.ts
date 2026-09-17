import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
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
    { id: 17, benchmarkSuiteId: 7, orderIndex: 4, questionText: 'Current question', difficulty: 1, expectedPoints: 'line one\nline two', createdAtUtc: '' }
  ];
  const header = 'format: overseer-benchmark-questions\nversion: 1\n';

  beforeEach(async () => {
    service = jasmine.createSpyObj('AdminBenchmarkService', ['importQuestions', 'importSuite', 'matchSnapshot']);

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
    const found = Array.from(host.querySelectorAll('.dialog-footer button'))
      .find(b => (b.textContent ?? '').replace(/\s+/g, ' ').trim() === text) as HTMLButtonElement | undefined;
    if (!found) throw new Error(`No footer button "${text}"`);
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

  it('opens on the first step with the stepper and no route checks', () => {
    component.open('questions');
    expect(component.dialog.nativeElement.open).toBeTrue();
    const current = host.querySelector('.gh-steps li[aria-current="step"]')!;
    expect(current.textContent).toContain('Provide YAML');
    expect(host.querySelector('textarea')!.id).toBe('questionYamlImport-paste');
    expect(host.querySelector('.import-checks')).toBeNull();
  });

  it('keeps Review inert until the text validates, and walks the footer through the steps', async () => {
    service.importQuestions.and.returnValue(of({ createdCount: 0, replacedCount: 1, unchangedCount: 0, questions: [] }));
    component.open('questions');
    expect(button('Validate').getAttribute('aria-disabled')).toBe('true');

    await paste(serializeQuestionsYaml([existing[0]], suite).replace('line two', 'line 2'));
    expect(button('Validate').getAttribute('aria-disabled')).toBeNull();
    expect(button('Review changes').getAttribute('aria-disabled')).toBe('true');

    await component.panel.validate();
    fixture.detectChanges();
    expect(button('Review changes').getAttribute('aria-disabled')).toBeNull();

    await component.panel.review();
    fixture.detectChanges();
    expect(host.querySelector('.gh-steps li[aria-current="step"]')!.textContent).toContain('Review');
    expect(host.querySelector('.gh-steps li.is-done')!.textContent).toContain('Provide YAML');

    button('Apply 1 change').click();
    fixture.detectChanges();
    expect(component.step).toBe(3);
    expect(button('Close')).toBeTruthy();
  });

  it('re-emits the panel outputs', async () => {
    const imported = jasmine.createSpy('imported');
    const help = jasmine.createSpy('help');
    component.imported.subscribe(imported);
    component.helpRequested.subscribe(help);
    service.importQuestions.and.returnValue(of({ createdCount: 1, replacedCount: 0, unchangedCount: 0, questions: [] }));

    component.open('questions');
    (Array.from(host.querySelectorAll('button')).find(b => b.textContent!.trim() === 'Open the format help') as HTMLButtonElement).click();
    expect(help).toHaveBeenCalled();

    await paste(header + 'questions:\n  - question: New\n');
    await component.panel.validate();
    await component.panel.review();
    component.panel.apply();
    expect(imported).toHaveBeenCalled();
  });

  it('refuses Escape and Close while applying', () => {
    component.open('questions');
    component.panel.applying = true;
    const event = new Event('cancel', { cancelable: true });
    component.onCancel(event);
    expect(event.defaultPrevented).toBeTrue();

    component.close();
    expect(component.dialog.nativeElement.open).toBeTrue();
    component.panel.applying = false;
  });

  it('has no duplicate element ids', () => {
    component.open('suite');
    const ids = Array.from(host.querySelectorAll('[id]')).map(e => e.id);
    expect(new Set(ids).size).toBe(ids.length);
  });
});
