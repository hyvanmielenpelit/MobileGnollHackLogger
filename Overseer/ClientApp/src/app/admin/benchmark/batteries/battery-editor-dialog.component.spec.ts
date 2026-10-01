import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';

import {
  AdminBenchmarkService,
  BenchmarkBatteryDto,
  BenchmarkQuestionDto,
  BenchmarkSuiteDto
} from '../../../services/admin-benchmark.service';
import { BatteryEditorDialogComponent } from './battery-editor-dialog.component';

function dto<T>(value: object): T {
  return value as T;
}

function suite(id: number, name: string, questionCount: number, assessed: number): BenchmarkSuiteDto {
  return dto<BenchmarkSuiteDto>({
    id,
    name,
    description: null,
    createdAtUtc: '2026-09-01T00:00:00Z',
    modifiedAtUtc: null,
    questionCount,
    assessedQuestionCount: assessed,
    difficultyFullyAssessed: assessed === questionCount
  });
}

function questions(difficulties: (number | null)[]): BenchmarkQuestionDto[] {
  return difficulties.map((d, i) => dto<BenchmarkQuestionDto>({ id: i + 1, assessedDifficulty: d }));
}

const SUITES = [
  suite(11, 'Gameplay Help', 2, 2),
  suite(12, 'Board Reading', 4, 3),
  suite(13, 'Item Lore', 3, 3)
];

/** Gameplay Help: mass 100 over 2 questions. Board Reading: 50 + 50 + 50 + 100 = 250 over 4. */
const QUESTIONS: Record<number, BenchmarkQuestionDto[]> = {
  11: questions([20, 80]),
  12: questions([null, 50, 50, 100]),
  13: questions([10, 10, 10])
};

function battery(overrides: Partial<BenchmarkBatteryDto> = {}): BenchmarkBatteryDto {
  return dto<BenchmarkBatteryDto>({
    id: 3,
    name: 'Core Battery',
    description: 'Two suites',
    weightingScheme: 'DifficultyMass',
    weightingSchemeLabel: 'Questions and difficulty',
    revision: 2,
    definitionSha256: 'abcdef',
    isArchived: false,
    brokenSuiteNames: [],
    validationErrors: [],
    createdByUserName: 'admin',
    createdAtUtc: '2026-09-01T00:00:00Z',
    modifiedAtUtc: '2026-09-02T00:00:00Z',
    batteryRunCount: 1,
    hasActiveBatteryRun: false,
    suites: [
      { index: 0, suiteId: 12, suiteName: 'Board Reading', deleted: false, customWeight: null, questionCount: 4, assessedQuestionCount: 3, difficultyFullyAssessed: false, difficultyMass: 250 },
      { index: 1, suiteId: 11, suiteName: 'Gameplay Help', deleted: false, customWeight: null, questionCount: 2, assessedQuestionCount: 2, difficultyFullyAssessed: true, difficultyMass: 100 }
    ],
    weightPreviews: [],
    ...overrides
  });
}

describe('BatteryEditorDialogComponent', () => {
  let fixture: ComponentFixture<BatteryEditorDialogComponent>;
  let component: BatteryEditorDialogComponent;
  let service: jasmine.SpyObj<AdminBenchmarkService>;

  function el(): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  function checkSuite(name: string): void {
    const box = el().querySelector(`.rl-list input[type="checkbox"][aria-label="${name}"]`) as HTMLInputElement;
    expect(box).withContext(`checkbox for ${name}`).not.toBeNull();
    box.click();
    fixture.detectChanges();
  }

  function chooseScheme(value: string): void {
    const select = el().querySelector('#bbeScheme') as HTMLSelectElement;
    select.value = value;
    select.dispatchEvent(new Event('change'));
    fixture.detectChanges();
  }

  function chosenColumn(): string[] {
    return Array.from(el().querySelectorAll('.bbe-preview tbody td.bbe-chosen'))
      .map(td => (td.textContent ?? '').trim());
  }

  function typeName(value: string): void {
    const input = el().querySelector('#bbeName') as HTMLInputElement;
    input.value = value;
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();
  }

  function clickSave(): void {
    (el().querySelector('.bbe-save') as HTMLButtonElement).click();
    fixture.detectChanges();
  }

  beforeEach(async () => {
    service = jasmine.createSpyObj<AdminBenchmarkService>('AdminBenchmarkService', [
      'getQuestions', 'createBattery', 'updateBattery'
    ]);
    service.getQuestions.and.callFake((suiteId: number) => of(QUESTIONS[suiteId] ?? []));
    service.createBattery.and.callFake((req) => of(battery({ id: 9, name: req.name })));
    service.updateBattery.and.callFake((id: number) => of(battery({ id, revision: 3 })));

    await TestBed.configureTestingModule({
      imports: [BatteryEditorDialogComponent],
      providers: [{ provide: AdminBenchmarkService, useValue: service }]
    }).compileComponents();

    fixture = TestBed.createComponent(BatteryEditorDialogComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  afterEach(() => {
    const dialog = el().querySelector('dialog') as HTMLDialogElement | null;
    if (dialog?.open) dialog.close();
  });

  it('creates', () => {
    expect(component).toBeTruthy();
  });

  it('opens a new battery with every suite unchecked and the default weighting', () => {
    component.open(null, SUITES);
    fixture.detectChanges();

    expect(el().querySelector('#bbeTitle')?.textContent?.trim()).toBe('New Battery');
    expect((el().querySelector('#bbeScheme') as HTMLSelectElement).value).toBe('DifficultyMass');
    expect(el().querySelectorAll('.rl-list input[type="checkbox"]:checked').length).toBe(0);
    expect(el().querySelector('.bbe-preview tbody')?.textContent).toContain('Select suites to see their weights.');
    expect(el().querySelector('#bbeRevisionNote')).toBeNull();
  });

  it('previews weights under Questions and difficulty, then under Equal per suite', () => {
    component.open(null, SUITES);
    fixture.detectChanges();
    checkSuite('Gameplay Help');
    checkSuite('Board Reading');

    expect(service.getQuestions).toHaveBeenCalledWith(11);
    expect(service.getQuestions).toHaveBeenCalledWith(12);
    // List order is by name, so Board Reading (250 / 350) precedes Gameplay Help (100 / 350).
    expect(chosenColumn()).toEqual(['71.4 %', '28.6 %']);
    const muted = Array.from(el().querySelectorAll('.bbe-preview tbody tr:first-child td.bbe-muted'))
      .map(td => (td.textContent ?? '').trim());
    // Questions only (4 / 6), then Equal per suite.
    expect(muted).toEqual(['66.7 %', '50.0 %']);

    chooseScheme('Equal');
    expect(chosenColumn()).toEqual(['50.0 %', '50.0 %']);
    expect(el().querySelector('.bbe-preview thead .bbe-chosen')?.textContent?.trim()).toBe('Equal per suite');
  });

  it('warns about a suite whose difficulties are not assessed', () => {
    component.open(null, SUITES);
    fixture.detectChanges();
    checkSuite('Board Reading');

    const warning = el().querySelector('.bbe-preview .bbe-warning')?.textContent?.replace(/\s+/g, ' ') ?? '';
    expect(warning).toContain('Difficulties not assessed for 1 of 4 questions');
    expect(warning).toContain('the launcher refuses this suite');
  });

  it('shows custom weight inputs only under Custom', () => {
    component.open(null, SUITES);
    fixture.detectChanges();
    checkSuite('Gameplay Help');
    checkSuite('Board Reading');
    expect(el().querySelectorAll('.bbe-custom-input').length).toBe(0);

    chooseScheme('Custom');
    const inputs = el().querySelectorAll('.bbe-custom-input') as NodeListOf<HTMLInputElement>;
    expect(inputs.length).toBe(2);
    expect(inputs[0].value).toBe('1');
    expect(chosenColumn()).toEqual(['50.0 %', '50.0 %']);

    inputs[0].value = '3';
    inputs[0].dispatchEvent(new Event('input'));
    fixture.detectChanges();
    expect(chosenColumn()).toEqual(['75.0 %', '25.0 %']);

    chooseScheme('ItemCount');
    expect(el().querySelectorAll('.bbe-custom-input').length).toBe(0);
  });

  it('refuses to save without a name or with fewer than two suites', () => {
    component.open(null, SUITES);
    fixture.detectChanges();
    checkSuite('Gameplay Help');
    clickSave();

    expect(service.createBattery).not.toHaveBeenCalled();
    expect(el().querySelector('#bbeNameError')?.textContent).toContain('Enter a name');
    expect(el().querySelector('#bbeSuiteCountError')?.textContent).toContain('at least two suites');
  });

  it('creates a battery with the checked suites in list order', () => {
    const saved = jasmine.createSpy('saved');
    component.saved.subscribe(saved);
    component.open(null, SUITES);
    fixture.detectChanges();
    typeName('  New One ');
    checkSuite('Item Lore');
    checkSuite('Gameplay Help');
    clickSave();

    expect(service.createBattery).toHaveBeenCalledTimes(1);
    const request = service.createBattery.calls.mostRecent().args[0];
    expect(request.name).toBe('New One');
    expect(request.weightingScheme).toBe('DifficultyMass');
    // The unchecked rows are sorted by name: Board Reading, Gameplay Help, Item Lore.
    expect(request.suiteIds).toEqual([11, 13]);
    expect('customWeights' in request).toBeFalse();
    expect(saved).toHaveBeenCalled();
  });

  it('edits a battery from its own masses and notes the new revision', () => {
    component.open(battery(), SUITES);
    fixture.detectChanges();

    expect(el().querySelector('#bbeTitle')?.textContent?.trim()).toBe('Edit Battery');
    expect(el().querySelector('#bbeRevisionNote')?.textContent).toContain('creates revision 3');
    expect(service.getQuestions).not.toHaveBeenCalled();
    expect(chosenColumn()).toEqual(['71.4 %', '28.6 %']);

    clickSave();
    expect(service.updateBattery).toHaveBeenCalledTimes(1);
    const [id, request] = service.updateBattery.calls.mostRecent().args;
    expect(id).toBe(3);
    expect(request.suiteIds).toEqual([12, 11]);
    expect(request.name).toBe('Core Battery');
  });

  it('shows the server refusal when saving fails', () => {
    service.updateBattery.and.returnValue(throwError(() => ({ status: 400, error: 'A battery with this name already exists.' })));
    component.open(battery(), SUITES);
    fixture.detectChanges();
    clickSave();

    expect(el().querySelector('.bbe-save-error')?.textContent?.trim()).toBe('A battery with this name already exists.');
  });
});
