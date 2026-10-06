import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';

import { BenchmarkComparisonDto } from '../../../services/admin-benchmark.service';
import { COMPARISON_NAME_MAX_LENGTH, ComparisonRenameDialogComponent } from './comparison-rename-dialog.component';

describe('ComparisonRenameDialogComponent', () => {
  let fixture: ComponentFixture<ComparisonRenameDialogComponent>;
  let component: ComparisonRenameDialogComponent;
  let http: HttpTestingController;
  let host: HTMLElement;
  let renamed: BenchmarkComparisonDto[];

  const comparison = (overrides: Partial<BenchmarkComparisonDto> = {}): BenchmarkComparisonDto => ({
    id: 12,
    name: 'GPT-5.6 Luna (max) vs GPT-6.1 Sol (medium)',
    customName: null,
    defaultName: 'GPT-5.6 Luna (max) vs GPT-6.1 Sol (medium)',
    entryCount: 2,
    subjectKind: 'Runs',
    entryKeys: ['run:1', 'run:2'],
    createdAtUtc: '2026-10-06T10:00:00Z',
    renamedAtUtc: null,
    ...overrides
  });

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [ComparisonRenameDialogComponent],
      providers: [provideHttpClient(), provideHttpClientTesting()]
    }).compileComponents();
    fixture = TestBed.createComponent(ComparisonRenameDialogComponent);
    component = fixture.componentInstance;
    http = TestBed.inject(HttpTestingController);
    host = fixture.nativeElement as HTMLElement;
    renamed = [];
    component.renamed.subscribe(dto => renamed.push(dto));
    fixture.detectChanges();
  });

  afterEach(() => {
    const dialog = host.querySelector('dialog');
    if (dialog?.open) {
      dialog.close();
    }
    fixture.destroy();
  });

  const dialog = (): HTMLDialogElement => host.querySelector<HTMLDialogElement>('dialog')!;
  const input = (): HTMLInputElement => host.querySelector<HTMLInputElement>('#mc-rename-name')!;
  const save = (): HTMLButtonElement => host.querySelector<HTMLButtonElement>('.mc-rename-save')!;
  const type = (value: string): void => {
    input().value = value;
    input().dispatchEvent(new Event('input'));
    fixture.detectChanges();
  };
  const buttonNamed = (name: string): HTMLButtonElement =>
    Array.from(host.querySelectorAll<HTMLButtonElement>('button')).find(button => button.textContent!.trim() === name)!;

  async function until(condition: () => boolean): Promise<void> {
    for (let i = 0; i < 200 && !condition(); i++) {
      await new Promise(resolve => setTimeout(resolve, 0));
    }
  }

  it('opens on the display name, labelled with the comparison number, with Reset to default, Cancel and Save', () => {
    component.open(comparison({ name: 'Flagships', customName: 'Flagships' }));
    fixture.detectChanges();

    expect(dialog().open).toBe(true);
    expect(dialog().getAttribute('aria-labelledby')).toBe('mc-rename-title');
    expect(host.querySelector('#mc-rename-title')!.textContent!.trim()).toBe('Rename comparison #12');
    expect(input().value).toBe('Flagships');
    expect(input().maxLength).toBe(COMPARISON_NAME_MAX_LENGTH);
    expect(COMPARISON_NAME_MAX_LENGTH).toBe(160);
    expect(host.querySelector('#mc-rename-hint')!.textContent).toContain('GPT-5.6 Luna (max) vs GPT-6.1 Sol (medium)');
    expect(buttonNamed('Reset to default')).toBeTruthy();
    expect(buttonNamed('Cancel')).toBeTruthy();
    expect(save().textContent!.trim()).toBe('Save');
    const close = host.querySelector<HTMLButtonElement>('.btn-icon-action')!;
    expect(close.getAttribute('aria-label')).toBe('Close Rename comparison');
    expect(host.querySelector(`#${close.getAttribute('interestfor')}`)!.getAttribute('popover')).toBe('hint');
  });

  it('saves the trimmed name, emits the server\'s answer and closes', () => {
    component.open(comparison());
    type('  Flagships, October  ');
    save().click();
    fixture.detectChanges();

    const request = http.expectOne('/api/admin/benchmark/model-comparisons/12');
    expect(request.request.method).toBe('PATCH');
    expect(request.request.body).toEqual({ name: 'Flagships, October' });
    expect(save().getAttribute('aria-busy')).toBe('true');
    const answer = comparison({ name: 'Flagships, October', customName: 'Flagships, October' });
    request.flush(answer);
    fixture.detectChanges();

    expect(renamed).toEqual([answer]);
    expect(dialog().open).toBe(false);
  });

  it('resets to the default: Reset to default fills it in and Save sends null, as a blank name does', () => {
    component.open(comparison({ name: 'Flagships', customName: 'Flagships' }));
    buttonNamed('Reset to default').click();
    fixture.detectChanges();
    expect(input().value).toBe('GPT-5.6 Luna (max) vs GPT-6.1 Sol (medium)');
    save().click();
    const reset = http.expectOne('/api/admin/benchmark/model-comparisons/12');
    expect(reset.request.body).toEqual({ name: null });
    reset.flush(comparison());
    expect(renamed.length).toBe(1);

    component.open(comparison({ name: 'Flagships', customName: 'Flagships' }));
    type('   ');
    save().click();
    const blank = http.expectOne('/api/admin/benchmark/model-comparisons/12');
    expect(blank.request.body).toEqual({ name: null });
  });

  it('shows the server\'s refusal under the field and stays open', () => {
    component.open(comparison());
    type('A name');
    save().click();
    http.expectOne('/api/admin/benchmark/model-comparisons/12')
      .flush({ error: 'A comparison name can be at most 160 characters.' }, { status: 400, statusText: 'Bad Request' });
    fixture.detectChanges();

    const error = host.querySelector('#mc-rename-error')!;
    expect(error.getAttribute('role')).toBe('alert');
    expect(error.textContent!.trim()).toBe('A comparison name can be at most 160 characters.');
    expect(input().getAttribute('aria-invalid')).toBe('true');
    expect(input().getAttribute('aria-describedby')).toBe('mc-rename-hint mc-rename-error');
    expect(dialog().open).toBe(true);
    expect(renamed).toEqual([]);

    // Typing clears it.
    type('Another name');
    expect(host.querySelector('#mc-rename-error')).toBeNull();
  });

  it('closes on Cancel with nothing sent, and returns focus to the control that opened it', async () => {
    const trigger = document.createElement('button');
    document.body.appendChild(trigger);
    let closed = 0;
    component.closed.subscribe(() => closed++);
    try {
      component.open(comparison(), trigger);
      buttonNamed('Cancel').click();
      fixture.detectChanges();

      http.expectNone('/api/admin/benchmark/model-comparisons/12');
      expect(dialog().open).toBe(false);
      await until(() => document.activeElement === trigger);
      expect(document.activeElement).toBe(trigger);
      expect(closed).toBe(1);
    } finally {
      trigger.remove();
    }
  });

  it('keeps its close, cancel and click events from reaching the dialog around it', () => {
    const outer: string[] = [];
    host.addEventListener('click', () => outer.push('click'));
    host.addEventListener('cancel', () => outer.push('cancel'));
    component.open(comparison());

    input().click();
    dialog().dispatchEvent(new Event('cancel', { bubbles: true, cancelable: true }));

    expect(outer).toEqual([]);
  });

  it('refuses Escape while a save is in flight', () => {
    component.open(comparison());
    save().click();
    fixture.detectChanges();
    const cancel = new Event('cancel', { cancelable: true });
    dialog().dispatchEvent(cancel);

    expect(cancel.defaultPrevented).toBe(true);
    http.expectOne('/api/admin/benchmark/model-comparisons/12').flush(comparison());
  });
});
