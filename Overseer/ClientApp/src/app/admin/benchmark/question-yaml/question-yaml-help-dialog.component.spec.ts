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

  const tabButtons = (): HTMLButtonElement[] => Array.from(host.querySelectorAll<HTMLButtonElement>('.gh-tab'));
  const selectedTab = (): HTMLButtonElement | undefined => tabButtons().find(b => b.getAttribute('aria-selected') === 'true');

  it('opens on the workflow tab with the guide, and without the AI instructions', () => {
    component.open();
    expect(component.dialog.nativeElement.open).toBeTrue();
    expect(host.querySelector('.help-guide')!.textContent).toContain('Export, edit, import');
    expect(host.querySelector('.help-ai-text')).toBeNull();
  });

  it('renders four tabs with one selected and one in the tab order', () => {
    component.open();
    expect(tabButtons().map(b => b.textContent!.trim())).toEqual(['Workflow', 'Replace or Create', 'Format', 'For an AI']);
    expect(tabButtons().filter(b => b.getAttribute('aria-selected') === 'true').length).toBe(1);
    expect(tabButtons().filter(b => b.getAttribute('tabindex') === '0').length).toBe(1);
    expect(host.querySelector('[role="tabpanel"]')!.getAttribute('aria-labelledby')).toBe('yaml-help-tab-workflow');
  });

  it('shows the AI instructions on the AI tab', () => {
    component.open();
    component.selectTab('ai');
    fixture.detectChanges();
    expect(host.querySelector('.help-ai-text')!.textContent).toBe(AI_INSTRUCTIONS_MARKDOWN);
    expect(selectedTab()!.textContent!.trim()).toBe('For an AI');
    expect(host.querySelector('.help-guide')).toBeNull();
  });

  it('moves and wraps with the arrow keys, and focus follows the selection', () => {
    component.open();
    const keydown = (key: string, index: number) => {
      const event = new KeyboardEvent('keydown', { key, cancelable: true });
      component.onTabKeydown(event, index);
      fixture.detectChanges();
      return event;
    };

    expect(keydown('ArrowLeft', 0).defaultPrevented).toBeTrue();
    expect(component.activeTab).toBe('ai');
    expect(document.activeElement).toBe(host.querySelector('#yaml-help-tab-ai'));

    keydown('ArrowRight', 3);
    expect(component.activeTab).toBe('workflow');

    keydown('End', 0);
    expect(component.activeTab).toBe('ai');
    keydown('Home', 3);
    expect(component.activeTab).toBe('workflow');

    expect(keydown('a', 0).defaultPrevented).toBeFalse();
    expect(component.activeTab).toBe('workflow');
  });

  it('reopens on the workflow tab', () => {
    component.open();
    component.selectTab('format');
    component.close();
    component.open();
    expect(component.activeTab).toBe('workflow');
    expect(selectedTab()!.textContent!.trim()).toBe('Workflow');
  });

  it('copies the instructions and announces the result', async () => {
    const writeText = jasmine.createSpy('writeText').and.returnValue(Promise.resolve());
    const original = Object.getOwnPropertyDescriptor(navigator, 'clipboard');
    Object.defineProperty(navigator, 'clipboard', { value: { writeText }, configurable: true, writable: true });
    try {
      component.open();
      component.selectTab('ai');
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
    component.open();
    component.selectTab('ai');
    const labels = Array.from(host.querySelectorAll('.action-btn')).map(b => b.getAttribute('aria-label'));
    expect(labels).toEqual(['Copy AI instructions to the clipboard', 'Download AI instructions as Markdown']);
  });
});
