import { ComponentFixture, TestBed } from '@angular/core/testing';
import { QuestionYamlHelpDialogComponent } from './question-yaml-help-dialog.component';
import { AI_INSTRUCTIONS_MARKDOWN } from './question-yaml-format';

describe('QuestionYamlHelpDialogComponent', () => {
  let fixture: ComponentFixture<QuestionYamlHelpDialogComponent>;
  let component: QuestionYamlHelpDialogComponent;
  let host: HTMLElement;

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [QuestionYamlHelpDialogComponent] }).compileComponents();
    fixture = TestBed.createComponent(QuestionYamlHelpDialogComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
    host = fixture.nativeElement as HTMLElement;
  });

  afterEach(() => component.dialog?.nativeElement?.close());

  it('opens with the guide and the AI instructions', () => {
    component.open();
    expect(component.dialog.nativeElement.open).toBeTrue();
    expect(host.querySelector('.help-guide')!.textContent).toContain('What the buttons do');
    expect(host.querySelector('.help-ai-text')!.textContent).toBe(AI_INSTRUCTIONS_MARKDOWN);
  });

  it('copies the instructions and announces the result', async () => {
    const writeText = jasmine.createSpy('writeText').and.returnValue(Promise.resolve());
    const original = Object.getOwnPropertyDescriptor(navigator, 'clipboard');
    Object.defineProperty(navigator, 'clipboard', { value: { writeText }, configurable: true, writable: true });
    try {
      component.open();
      await component.copyInstructions();
      fixture.detectChanges();
      expect(writeText).toHaveBeenCalledWith(AI_INSTRUCTIONS_MARKDOWN);
      expect(host.querySelector('.help-copy-status')!.textContent).toBe('Copied');
    } finally {
      delete (navigator as { clipboard?: unknown }).clipboard;
      if (original) Object.defineProperty(navigator, 'clipboard', original);
    }
  });

  it('has distinct accessible names on its icon buttons', () => {
    const labels = Array.from(host.querySelectorAll('.action-btn')).map(b => b.getAttribute('aria-label'));
    expect(labels).toEqual(['Copy AI instructions to the clipboard', 'Download AI instructions as Markdown']);
  });
});
