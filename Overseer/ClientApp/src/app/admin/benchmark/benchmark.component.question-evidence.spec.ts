import type { MockedObject } from 'vitest';
import { ComponentFixture } from '@angular/core/testing';
import { of } from 'rxjs';
import { AdminBenchmarkComponent } from './benchmark.component';
import { AdminBenchmarkService, BenchmarkQuestionDto, BenchmarkSuiteDto } from '../../services/admin-benchmark.service';
import { AdminBenchmarkSpecContext, clearStoredState, createAdminBenchmarkFixture } from './benchmark.component.testing';

describe('AdminBenchmarkComponent question gold evidence', () => {
  let ctx: AdminBenchmarkSpecContext;
  let component: AdminBenchmarkComponent;
  let fixture: ComponentFixture<AdminBenchmarkComponent>;
  let benchmarkServiceMock: MockedObject<AdminBenchmarkService>;

  const suite: BenchmarkSuiteDto = {
    id: 1,
    name: 'Evidence Suite',
    description: '',
    createdAtUtc: '2026-10-01T00:00:00Z',
    modifiedAtUtc: null,
    questionCount: 2,
    assessedQuestionCount: 0,
    difficultyFullyAssessed: false
  } as BenchmarkSuiteDto;

  const withEvidence = (): BenchmarkQuestionDto => ({
    id: 10,
    benchmarkSuiteId: 1,
    orderIndex: 1,
    questionText: 'What does a human start with?',
    difficulty: 1,
    expectedPoints: '- point',
    evidence: [
      { kind: 'wiki', path: 'Races/Human.md', personal: true },
      { kind: 'source', path: 'src/u_init.c', lines: '140-203' }
    ]
  }) as BenchmarkQuestionDto;

  const withoutEvidence = (): BenchmarkQuestionDto => ({
    id: 11,
    benchmarkSuiteId: 1,
    orderIndex: 2,
    questionText: 'Plain question?',
    difficulty: 1,
    expectedPoints: ''
  }) as BenchmarkQuestionDto;

  const root = (): HTMLElement => fixture.nativeElement as HTMLElement;
  const questionsDialog = () => ctx.suitesTab().questionsDialogCmp!;
  const subtitle = (): string => root().querySelector('.benchmark-questions-dialog .dialog-subtitle')?.textContent?.trim() ?? '';
  const saveButton = (): HTMLButtonElement =>
    Array.from(root().querySelectorAll<HTMLButtonElement>('.benchmark-question-form-dialog .dialog-footer button'))
      .find(b => b.textContent?.trim() === 'Save Question')!;
  const updateMock = () => (benchmarkServiceMock as any).updateQuestion as ReturnType<typeof vi.fn>;

  function showQuestions(questions: BenchmarkQuestionDto[]): void {
    component.selectSubTab('suites');
    ctx.refresh();
    ctx.workspace.currentSuiteForQuestions = suite;
    ctx.workspace.questions = questions;
    ctx.workspace.loadingQuestions = false;
    ctx.refresh();
  }

  /** The question form is OnPush: a state change made by the test is announced, then rendered. */
  async function settle(): Promise<void> {
    ctx.refresh();
    await fixture.whenStable();
    ctx.refresh();
  }

  function typeInto(selector: string, value: string): void {
    const input = root().querySelector(selector) as HTMLInputElement | HTMLTextAreaElement;
    input.value = value;
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();
  }

  beforeEach(clearStoredState);

  afterEach(clearStoredState);

  beforeEach(async () => {
    ctx = await createAdminBenchmarkFixture();
    ({ component, fixture, benchmarkServiceMock } = ctx);
    (benchmarkServiceMock as any).updateQuestion = vi.fn().mockReturnValue(of(withEvidence()));
    (benchmarkServiceMock as any).createQuestion = vi.fn().mockReturnValue(of(withEvidence()));
  });

  it('renders the Gold Evidence box and badge, and the subtitle counts the question', () => {
    showQuestions([withEvidence(), withoutEvidence()]);

    expect(subtitle()).toBe('2 Questions Total · 1 with Gold Evidence');
    const cards = root().querySelectorAll('.question-list-item');
    expect(cards.length).toBe(2);
    const box = cards[0].querySelector('.q-evidence-box');
    expect(box).toBeTruthy();
    expect(box!.querySelector('.criteria-label')?.textContent?.trim()).toBe('Gold Evidence:');
    expect(box!.querySelectorAll('app-question-evidence-list li').length).toBe(2);
    expect(box!.textContent).toContain('Races/Human.md');
    expect(box!.textContent).toContain('L140-203');
    const badge = Array.from(cards[0].querySelectorAll('.meta-badges span')).find(s => s.textContent?.includes('Evidence:'));
    expect(badge?.textContent?.trim()).toBe('Evidence: 2');
    expect(cards[1].querySelector('.q-evidence-box')).toBeNull();
  });

  it('says No Gold Evidence and renders no evidence box for a suite without any', () => {
    showQuestions([withoutEvidence()]);

    expect(subtitle()).toBe('1 Questions Total · No Gold Evidence');
    expect(root().querySelector('.q-evidence-box')).toBeNull();
    expect(root().querySelector('.benchmark-questions-dialog app-question-evidence-list')).toBeNull();
  });

  it('opens the form with both rows prefilled from copies, not the card objects', async () => {
    const question = withEvidence();
    showQuestions([question]);

    questionsDialog().openEditQuestion(question);
    await settle();

    expect(questionsDialog().evidenceUnits).toEqual(question.evidence);
    expect(questionsDialog().evidenceUnits).not.toBe(question.evidence);
    expect(questionsDialog().evidenceUnits[0]).not.toBe(question.evidence![0]);
    expect(root().querySelectorAll('.benchmark-question-form-dialog .evidence-row').length).toBe(2);
    expect((root().querySelector('#qEvidence-path-0') as HTMLInputElement).value).toBe('Races/Human.md');
    expect((root().querySelector('#qEvidence-lines-1') as HTMLInputElement).value).toBe('140-203');
  });

  it('sends no evidence key when only the text changed', async () => {
    const question = withEvidence();
    showQuestions([question]);
    questionsDialog().openEditQuestion(question);
    await settle();

    typeInto('#qTextInput', 'Edited?');
    saveButton().click();

    expect(updateMock()).toHaveBeenCalledTimes(1);
    const payload = updateMock().mock.calls[0][1];
    expect(payload.questionText).toBe('Edited?');
    expect('evidence' in payload).toBe(false);
  });

  it('sends the remaining unit after a row is removed, and the card keeps its units', async () => {
    const question = withEvidence();
    showQuestions([question]);
    questionsDialog().openEditQuestion(question);
    await settle();

    (root().querySelector('button[aria-label="Remove evidence unit 1"]') as HTMLButtonElement).click();
    fixture.detectChanges();
    saveButton().click();

    const payload = updateMock().mock.calls[0][1];
    expect(payload.evidence).toEqual([{ kind: 'source', path: 'src/u_init.c', lines: '140-203' }]);
    expect(question.evidence!.length).toBe(2);
  });

  it('sends an in-place row edit', async () => {
    const question = withEvidence();
    showQuestions([question]);
    questionsDialog().openEditQuestion(question);
    await settle();

    typeInto('#qEvidence-lines-1', '140-210');
    saveButton().click();

    const payload = updateMock().mock.calls[0][1];
    expect(payload.evidence[1]).toEqual({ kind: 'source', path: 'src/u_init.c', lines: '140-210' });
    expect(question.evidence![1].lines).toBe('140-203');
  });

  it('disables Save Question while a row is invalid', async () => {
    const question = withEvidence();
    showQuestions([question]);
    questionsDialog().openEditQuestion(question);
    await settle();
    expect(saveButton().disabled).toBe(false);

    typeInto('#qEvidence-lines-1', '203-140');

    expect(saveButton().disabled).toBe(true);
    expect(root().querySelector('.benchmark-question-form-dialog .evidence-editor-error')?.textContent?.trim())
      .toBe("Evidence unit 2: lines '203-140' must read a-b with 1 <= a <= b.");
    questionsDialog().saveQuestion();
    expect(updateMock()).not.toHaveBeenCalled();
  });

  it('sends a new question\'s evidence on create and omits an empty list', async () => {
    showQuestions([]);
    questionsDialog().openCreateQuestion();
    await settle();
    typeInto('#qTextInput', 'New?');
    saveButton().click();
    const createMock = (benchmarkServiceMock as any).createQuestion as ReturnType<typeof vi.fn>;
    expect('evidence' in createMock.mock.calls[0][1]).toBe(false);

    questionsDialog().openCreateQuestion();
    await settle();
    typeInto('#qTextInput', 'New with evidence?');
    (root().querySelector('#qEvidence-add') as HTMLButtonElement).click();
    fixture.detectChanges();
    typeInto('#qEvidence-path-0', ' Races/Elf.md ');
    saveButton().click();

    expect(createMock.mock.calls[1][1].evidence).toEqual([{ kind: 'wiki', path: 'Races/Elf.md' }]);
  });
});
