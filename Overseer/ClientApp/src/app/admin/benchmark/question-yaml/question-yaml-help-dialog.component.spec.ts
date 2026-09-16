import { ComponentFixture, TestBed } from '@angular/core/testing';
import { QuestionYamlHelpDialogComponent } from './question-yaml-help-dialog.component';
import { AI_INSTRUCTIONS_MARKDOWN, YAML_EXAMPLES } from './question-yaml-format';

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

  const withClipboard = async (run: (writeText: jasmine.Spy) => Promise<void>) => {
    const writeText = jasmine.createSpy('writeText').and.returnValue(Promise.resolve());
    const original = Object.getOwnPropertyDescriptor(navigator, 'clipboard');
    Object.defineProperty(navigator, 'clipboard', { value: { writeText }, configurable: true, writable: true });
    try {
      await run(writeText);
    } finally {
      delete (navigator as { clipboard?: unknown }).clipboard;
      if (original) Object.defineProperty(navigator, 'clipboard', original);
    }
  };

  it('opens on the workflow tab with the guide, and without the AI instructions', () => {
    component.open();
    expect(component.dialog.nativeElement.open).toBeTrue();
    expect(host.querySelector('.help-guide')!.textContent).toContain('Export, edit, import');
    expect(host.querySelector('.help-ai-text')).toBeNull();
  });

  it('renders five tabs with one selected and one in the tab order', () => {
    component.open();
    expect(tabButtons().map(b => b.textContent!.trim())).toEqual(['Workflow', 'Replace or Create', 'Format', 'Examples', 'For an AI']);
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

    keydown('ArrowRight', 4);
    expect(component.activeTab).toBe('workflow');

    keydown('End', 0);
    expect(component.activeTab).toBe('ai');
    keydown('Home', 4);
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
    await withClipboard(async writeText => {
      component.open();
      component.selectTab('ai');
      await component.copyInstructions();
      fixture.detectChanges();
      expect(writeText).toHaveBeenCalledWith(AI_INSTRUCTIONS_MARKDOWN);
      expect(host.querySelector('.help-copy-status')!.textContent).toBe('Copied');
    });
  });

  it('has distinct accessible names on its icon buttons', () => {
    component.open();
    component.selectTab('ai');
    const labels = Array.from(host.querySelectorAll('.action-btn')).map(b => b.getAttribute('aria-label'));
    expect(labels).toEqual(['Copy AI instructions to the clipboard', 'Download AI instructions as Markdown']);
  });

  describe('examples tab', () => {
    beforeEach(() => {
      component.open();
      component.selectTab('examples');
      fixture.detectChanges();
    });

    const exampleDetails = (): HTMLDetailsElement[] => Array.from(host.querySelectorAll<HTMLDetailsElement>('details.help-example'));

    it('renders every example as an exclusive disclosure, with the first one open', () => {
      const details = exampleDetails();
      expect(details.length).toBe(YAML_EXAMPLES.length);
      expect(details.every(d => d.getAttribute('name') === 'yaml-help-example')).toBeTrue();
      expect(details.map(d => d.open)).toEqual(YAML_EXAMPLES.map((_, i) => i === 0));
      expect(details.map(d => d.querySelector('summary')!.textContent!.trim())).toEqual(YAML_EXAMPLES.map(e => e.title));
      expect(details[0].querySelector('.help-ai-text')!.textContent).toBe(YAML_EXAMPLES[0].yaml);
    });

    it('copies one example and announces it beside that example only', async () => {
      await withClipboard(async writeText => {
        await component.copyExample(YAML_EXAMPLES[2]);
        fixture.detectChanges();
        expect(writeText).toHaveBeenCalledWith(YAML_EXAMPLES[2].yaml);
        const statuses = exampleDetails().map(d => d.querySelector('.help-copy-status')!.textContent);
        expect(statuses.filter(s => s === 'Copied').length).toBe(1);
        expect(statuses[2]).toBe('Copied');
      });
    });

    it('gives every example button a distinct accessible name', () => {
      const labels = Array.from(host.querySelectorAll('.action-btn')).map(b => b.getAttribute('aria-label'));
      expect(labels.length).toBe(YAML_EXAMPLES.length * 2);
      expect(new Set(labels).size).toBe(labels.length);
    });
  });
});
