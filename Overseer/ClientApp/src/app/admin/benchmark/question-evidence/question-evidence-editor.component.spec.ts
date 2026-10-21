import { ComponentFixture, TestBed } from '@angular/core/testing';
import type { BenchmarkEvidenceUnit } from '../../../services/admin-benchmark.service';
import { QuestionEvidenceEditorComponent } from './question-evidence-editor.component';

describe('QuestionEvidenceEditorComponent', () => {
  let fixture: ComponentFixture<QuestionEvidenceEditorComponent>;
  let component: QuestionEvidenceEditorComponent;

  const root = (): HTMLElement => fixture.nativeElement as HTMLElement;
  const rows = (): HTMLElement[] => Array.from(root().querySelectorAll<HTMLElement>('.evidence-row'));
  const byId = <T extends HTMLElement>(id: string): T | null => root().querySelector<T>(`#${id}`);
  const alertText = (): string => root().querySelector('[role="alert"]')?.textContent?.trim() ?? '';

  async function render(units: BenchmarkEvidenceUnit[]): Promise<void> {
    fixture.componentRef.setInput('units', units);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  function type(input: HTMLInputElement, value: string): void {
    input.value = value;
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();
  }

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [QuestionEvidenceEditorComponent] }).compileComponents();
    fixture = TestBed.createComponent(QuestionEvidenceEditorComponent);
    component = fixture.componentInstance;
  });

  it('adds a wiki row when Add Evidence Unit is clicked and emits a new array', async () => {
    await render([]);
    expect(root().textContent).toContain('No gold evidence.');
    const emitted: BenchmarkEvidenceUnit[][] = [];
    component.unitsChange.subscribe(units => emitted.push(units));

    byId<HTMLButtonElement>('qEvidence-add')!.click();
    fixture.detectChanges();

    expect(rows().length).toBe(1);
    expect(emitted.length).toBe(1);
    expect(emitted[0]).toEqual([{ kind: 'wiki', path: '' }]);
    expect(byId('qEvidence-section-0')).toBeTruthy();
    expect(byId('qEvidence-lines-0')).toBeNull();
    expect(document.activeElement).toBe(byId('qEvidence-path-0'));
  });

  it('shows Lines and Function and hides Section when the kind becomes Source', async () => {
    await render([{ kind: 'wiki', path: 'Races/Human.md' }]);
    expect(byId('qEvidence-section-0')).toBeTruthy();

    const select = byId<HTMLSelectElement>('qEvidence-kind-0')!;
    select.selectedIndex = 1;
    select.dispatchEvent(new Event('change'));
    fixture.detectChanges();

    expect(component.units[0].kind).toBe('source');
    expect(byId('qEvidence-section-0')).toBeNull();
    expect(byId('qEvidence-lines-0')).toBeTruthy();
    expect(byId('qEvidence-function-0')).toBeTruthy();
    expect(byId<HTMLInputElement>('qEvidence-path-0')!.placeholder).toBe('src/bones.c');
  });

  it('shows the validation message for invalid lines', async () => {
    await render([{ kind: 'source', path: 'src/bones.c' }]);
    expect(alertText()).toBe('');

    type(byId<HTMLInputElement>('qEvidence-lines-0')!, '9-3');

    expect(alertText()).toBe("Evidence unit 1: lines '9-3' must read a-b with 1 <= a <= b.");
  });

  it('removes the right row and emits a new array', async () => {
    const units: BenchmarkEvidenceUnit[] = [
      { kind: 'wiki', path: 'A.md' },
      { kind: 'wiki', path: 'B.md' },
      { kind: 'kb', path: 'C' }
    ];
    await render(units);
    const emitted: BenchmarkEvidenceUnit[][] = [];
    component.unitsChange.subscribe(next => emitted.push(next));

    root().querySelector<HTMLButtonElement>('button[aria-label="Remove evidence unit 2"]')!.click();
    fixture.detectChanges();

    expect(emitted.length).toBe(1);
    expect(emitted[0]).not.toBe(units);
    expect(emitted[0].map(u => u.path)).toEqual(['A.md', 'C']);
    expect(units.length).toBe(3);
    expect(rows().length).toBe(2);
    expect(byId<HTMLInputElement>('qEvidence-path-1')!.value).toBe('C');
  });

  it('labels every input and names every remove button', async () => {
    await render([{ kind: 'source', path: 'src/bones.c' }, { kind: 'wiki', path: 'Races/Human.md' }]);

    for (const input of Array.from(root().querySelectorAll<HTMLElement>('input, select'))) {
      expect(root().querySelector(`label[for="${input.id}"]`)).toBeTruthy();
    }
    expect(root().querySelector('button[aria-label="Remove evidence unit 1"]')).toBeTruthy();
    expect(root().querySelector('button[aria-label="Remove evidence unit 2"]')).toBeTruthy();
  });
});
