import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { AdminBenchmarkService, RubricAuthoringGuidance } from '../../../services/admin-benchmark.service';
import { QuestionYamlHelpDialogComponent } from './question-yaml-help-dialog.component';
import { RUBRIC_GUIDANCE_UNAVAILABLE, YAML_EXAMPLES, buildAiInstructions } from './question-yaml-format';
import { SUITE_GUIDE_TABS, SUITE_YAML_EXAMPLES } from './suite-yaml-guide';
import { INSTRUCTIONS_FILE_NAME, buildSuiteWorkflowInstructions } from './suite-workflow-instructions';

describe('QuestionYamlHelpDialogComponent', () => {
  let fixture: ComponentFixture<QuestionYamlHelpDialogComponent>;
  let component: QuestionYamlHelpDialogComponent;
  let host: HTMLElement;
  let service: jasmine.SpyObj<AdminBenchmarkService>;

  const GUIDANCE: RubricAuthoringGuidance = {
    sectionRules: '1. **BOARD FACTS**: fixture rule.\r\n2. **REQUIRED**: fixture rule.',
    gradingSemantics: 'Only REQUIRED and CRITICAL ERROR points are ever charged.',
    workedExample: '**REQUIRED**\r\n- A fixture point.',
    formLabel: '**FORM** (fixture label)',
    bands: [
      { name: 'Simple', range: '1–35', description: 'Fixture simple.' },
      { name: 'Intermediate', range: '36–70', description: 'Fixture intermediate.' },
      { name: 'Advanced', range: '71–100', description: 'Fixture advanced.' }
    ]
  };

  beforeEach(async () => {
    service = jasmine.createSpyObj('AdminBenchmarkService', ['getRubricAuthoringGuidance']);
    service.getRubricAuthoringGuidance.and.returnValue(of(GUIDANCE));
    await TestBed.configureTestingModule({
      imports: [QuestionYamlHelpDialogComponent],
      providers: [{ provide: AdminBenchmarkService, useValue: service }]
    }).compileComponents();
    fixture = TestBed.createComponent(QuestionYamlHelpDialogComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
    host = fixture.nativeElement as HTMLElement;
  });

  afterEach(() => component.dialog?.nativeElement?.close());

  const tabButtons = (): HTMLButtonElement[] => Array.from(host.querySelectorAll<HTMLButtonElement>('.gh-tab'));
  const selectedTab = (): HTMLButtonElement | undefined => tabButtons().find(b => b.getAttribute('aria-selected') === 'true');
  const aiText = (): HTMLElement | null => host.querySelector('.help-ai app-code-block pre code');

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
    expect(host.querySelector('.help-ai')).toBeNull();
  });

  it('renders five tabs with one selected and one in the tab order', () => {
    component.open();
    expect(tabButtons().map(b => b.textContent!.trim())).toEqual(['Workflow', 'Replace or Create', 'Format', 'Examples', 'AI Prompt']);
    expect(tabButtons().filter(b => b.getAttribute('aria-selected') === 'true').length).toBe(1);
    expect(tabButtons().filter(b => b.getAttribute('tabindex') === '0').length).toBe(1);
    expect(host.querySelector('[role="tabpanel"]')!.getAttribute('aria-labelledby')).toBe('yaml-help-tab-workflow');
  });

  it('opens every tab of both variants with an ingress', () => {
    for (const variant of ['questions', 'suite'] as const) {
      component.variant = variant;
      component.open();
      for (const tab of component.tabs) {
        component.selectTab(tab.id);
        fixture.detectChanges();
        expect(host.querySelector('.help-ingress')!.textContent!.trim())
          .withContext(`${variant} / ${tab.id}`).not.toBe('');
      }
      component.close();
    }
  });

  it('shows the AI instructions on the AI tab', () => {
    component.open();
    component.selectTab('ai');
    fixture.detectChanges();
    expect(aiText()!.textContent).toBe(buildAiInstructions(GUIDANCE));
    expect(host.querySelector('.help-ai .code-block-caption')!.textContent).toBe('overseer-benchmark-yaml-instructions.md');
    expect(selectedTab()!.textContent!.trim()).toBe('AI Prompt');
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

  it('copies the instructions from the code block and announces the result', async () => {
    await withClipboard(async writeText => {
      component.open();
      component.selectTab('ai');
      fixture.detectChanges();
      host.querySelector<HTMLButtonElement>('.help-ai .code-block-copy')!.click();
      await fixture.whenStable();
      fixture.detectChanges();
      expect(writeText).toHaveBeenCalledWith(buildAiInstructions(GUIDANCE));
      expect(host.querySelector('.help-ai [role="status"]')!.textContent).toBe('Copied');
    });
  });

  it('has distinct accessible names on its icon buttons', () => {
    component.open();
    component.selectTab('ai');
    fixture.detectChanges();
    const labels = Array.from(host.querySelectorAll('.action-btn')).map(b => b.getAttribute('aria-label'));
    expect(labels).toEqual(['Download the AI instructions', 'Copy the AI instructions to the clipboard']);
  });

  it('builds the AI tab from the fetched rubric guidance, fetching it once', () => {
    component.open();
    component.selectTab('ai');
    fixture.detectChanges();
    const text = aiText()!.textContent!;
    expect(text).toContain('**FORM** (fixture label)');
    for (const band of GUIDANCE.bands) {
      expect(text).toContain(band.name);
    }
    expect(text).not.toContain(RUBRIC_GUIDANCE_UNAVAILABLE);
    expect(host.querySelector('.help-ai-guidance-state')).toBeNull();

    component.close();
    component.open();
    expect(service.getRubricAuthoringGuidance).toHaveBeenCalledTimes(1);
  });

  it('falls back when the guidance cannot be loaded, and Copy still works', async () => {
    service.getRubricAuthoringGuidance.and.returnValue(throwError(() => new Error('offline')));
    await withClipboard(async writeText => {
      component.open();
      component.selectTab('ai');
      fixture.detectChanges();
      expect(component.guidanceState).toBe('failed');
      expect(host.querySelector('.help-ai-guidance-state')).not.toBeNull();
      expect(aiText()!.textContent).toContain(RUBRIC_GUIDANCE_UNAVAILABLE);

      host.querySelector<HTMLButtonElement>('.help-ai .code-block-copy')!.click();
      await fixture.whenStable();
      fixture.detectChanges();
      expect(writeText).toHaveBeenCalledWith(buildAiInstructions(null));
    });
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
      expect(details[0].querySelector('app-code-block pre code')!.textContent).toBe(YAML_EXAMPLES[0].yaml);
    });

    it('orders each example as title, description, then a code card with its file name', () => {
      const body = exampleDetails()[0].querySelector('.gh-disclosure-body')!;
      expect(Array.from(body.children).map(c => c.tagName.toLowerCase())).toEqual(['div', 'app-code-block']);
      expect(body.querySelector('.code-block-caption')!.textContent).toBe(`benchmark-example-${YAML_EXAMPLES[0].id}.yaml`);
    });

    it('copies one example and announces it beside that example only', async () => {
      await withClipboard(async writeText => {
        exampleDetails()[2].querySelector<HTMLButtonElement>('.code-block-copy')!.click();
        await fixture.whenStable();
        fixture.detectChanges();
        expect(writeText).toHaveBeenCalledWith(YAML_EXAMPLES[2].yaml);
        const statuses = exampleDetails().map(d => d.querySelector('app-code-block [role="status"]')!.textContent);
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

  describe('suite variant', () => {
    beforeEach(() => {
      component.variant = 'suite';
      fixture.detectChanges();
    });

    it('is titled for a suite, opens on Workflow and carries its own five tabs', () => {
      component.open();
      expect(host.querySelector('h3')!.textContent).toBe('Suite YAML Import and Export');
      expect(tabButtons().map(b => b.textContent!.trim()))
        .toEqual(['Workflow', 'Format', 'From a Snapshot', 'Examples', 'AI Prompt']);
      expect(selectedTab()!.textContent!.trim()).toBe('Workflow');
      expect(host.querySelector('.help-guide')!.textContent).toContain('Download, edit, import');
    });

    it('prefixes every element id, so the two instances never collide', () => {
      component.open();
      const ids = Array.from(host.querySelectorAll('[id]')).map(e => e.id);
      expect(ids).toContain('suite-yaml-help-title');
      expect(ids).toContain('suite-yaml-help-panel');
      expect(tabButtons().every(b => b.id.startsWith('suite-yaml-help-tab-'))).toBeTrue();
      expect(ids.some(id => id === 'yaml-help-title' || id === 'yaml-help-panel')).toBeFalse();
      expect(host.querySelector('button.btn-icon-action')!.getAttribute('aria-label'))
        .toBe('Close suite YAML import and export help');
    });

    it('never asks the server for the rubric guidance', () => {
      component.open();
      component.selectTab('ai');
      fixture.detectChanges();
      expect(service.getRubricAuthoringGuidance).not.toHaveBeenCalled();
      expect(host.querySelector('.help-ai-guidance-state')).toBeNull();
    });

    it('explains the wizard on AI Prompt, with the step-by-step instructions and no builder', () => {
      component.open();
      component.selectTab('ai');
      fixture.detectChanges();
      expect(host.querySelector('app-suite-prompt-builder')).toBeNull();
      expect(host.querySelector('.help-jump .btn-ghost')!.textContent!.trim()).toBe('Open the Snapshot Suite Wizard');
      expect(host.querySelector('app-code-block pre code')!.textContent).toBe(buildSuiteWorkflowInstructions('both'));
      expect(host.querySelector('.code-block-caption')!.textContent).toBe(INSTRUCTIONS_FILE_NAME);
    });

    it('asks for the wizard from From a Snapshot and from AI Prompt', () => {
      const requested = jasmine.createSpy('wizardRequested');
      component.wizardRequested.subscribe(requested);
      component.open();
      for (const tab of ['snapshot', 'ai'] as const) {
        component.selectTab(tab);
        fixture.detectChanges();
        host.querySelector<HTMLButtonElement>('.help-jump .btn-ghost')!.click();
      }
      expect(requested).toHaveBeenCalledTimes(2);
    });

    it('renders every code sample of the Format guide in a code block with a corner copy button, byte for byte', () => {
      component.open();
      component.selectTab('format');
      fixture.detectChanges();
      const blocks = Array.from(host.querySelectorAll('app-code-block'));
      const fences = (SUITE_GUIDE_TABS.find(t => t.id === 'format')!.markdown.match(/^```/gm) ?? []).length / 2;
      expect(blocks.length).toBe(fences);
      expect(blocks.every(b => b.querySelector('.code-block-corner .code-block-copy'))).toBeTrue();
      expect(host.querySelector('.help-guide pre')).toBeNull();
      const right = blocks.map(b => b.querySelector('pre code')!.textContent!).find(t => t.includes('0123456789012345'))!;
      expect(right).toContain('\n           0123456789012345\n');
      const labels = blocks.map(b => b.querySelector('button')!.getAttribute('aria-label'));
      expect(labels[0]).toBe('Copy code sample 1 of the Format guide to the clipboard');
      expect(new Set(labels).size).toBe(labels.length);
    });

    it('has no duplicate element ids on any tab', () => {
      component.open();
      for (const tab of component.tabs) {
        component.selectTab(tab.id);
        fixture.detectChanges();
        const ids = Array.from(host.querySelectorAll('[id]')).map(e => e.id);
        expect(new Set(ids).size).withContext(tab.id).toBe(ids.length);
      }
    });

    it('names its accordion apart from the question help and opens the first example', () => {
      component.open();
      component.selectTab('examples');
      fixture.detectChanges();
      const details = Array.from(host.querySelectorAll<HTMLDetailsElement>('details.help-example'));
      expect(details.length).toBe(SUITE_YAML_EXAMPLES.length);
      expect(details.every(d => d.getAttribute('name') === 'suite-yaml-help-example')).toBeTrue();
      expect(details.map(d => d.open)).toEqual(SUITE_YAML_EXAMPLES.map((_, i) => i === 0));
    });

    it('wraps the arrow keys round to the last tab', () => {
      component.open();
      const first = tabButtons()[0];
      first.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowLeft', bubbles: true }));
      fixture.detectChanges();
      expect(component.activeTab).toBe('ai');
      expect(document.activeElement!.id).toBe('suite-yaml-help-tab-ai');
    });
  });
});
