import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { AdminBenchmarkService, RubricAuthoringGuidance } from '../../../services/admin-benchmark.service';
import { QuestionYamlHelpDialogComponent } from './question-yaml-help-dialog.component';
import { RUBRIC_GUIDANCE_UNAVAILABLE, YAML_EXAMPLES, buildAiInstructions } from './question-yaml-format';
import { SUITE_YAML_EXAMPLES } from './suite-yaml-guide';

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
    expect(host.querySelector('.help-ai-text')!.textContent).toBe(buildAiInstructions(GUIDANCE));
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
      expect(writeText).toHaveBeenCalledWith(buildAiInstructions(GUIDANCE));
      expect(host.querySelector('.help-copy-status')!.textContent).toBe('Copied');
    });
  });

  it('has distinct accessible names on its icon buttons', () => {
    component.open();
    component.selectTab('ai');
    const labels = Array.from(host.querySelectorAll('.action-btn')).map(b => b.getAttribute('aria-label'));
    expect(labels).toEqual(['Copy AI instructions to the clipboard', 'Download AI instructions as Markdown']);
  });

  it('builds the AI tab from the fetched rubric guidance, fetching it once', () => {
    component.open();
    component.selectTab('ai');
    fixture.detectChanges();
    const text = host.querySelector('.help-ai-text')!.textContent!;
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
      expect(host.querySelector('.help-ai-text')!.textContent).toContain(RUBRIC_GUIDANCE_UNAVAILABLE);

      await component.copyInstructions();
      fixture.detectChanges();
      expect(writeText).toHaveBeenCalledWith(buildAiInstructions(null));
      expect(host.querySelector('.help-copy-status')!.textContent).toBe('Copied');
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

  describe('suite variant', () => {
    beforeEach(() => {
      component.variant = 'suite';
      fixture.detectChanges();
    });

    it('is titled for a suite, opens on Workflow and carries its own five tabs', () => {
      component.open();
      expect(host.querySelector('h3')!.textContent).toBe('Suite YAML Import and Export');
      expect(tabButtons().map(b => b.textContent!.trim()))
        .toEqual(['Workflow', 'Format', 'From a Snapshot', 'Examples', 'For an AI']);
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

    it('holds the prompt builder on For an AI, with no prompt until one is generated', () => {
      component.open();
      component.selectTab('ai');
      fixture.detectChanges();
      expect(host.querySelector('app-suite-prompt-builder')).not.toBeNull();
      expect(host.querySelector('.help-ai-text')).toBeNull();
    });

    it('jumps from From a Snapshot to the builder, moving focus with the selection', () => {
      component.open();
      component.selectTab('snapshot');
      fixture.detectChanges();
      expect(host.querySelector('app-suite-prompt-builder')).toBeNull();

      const jump = host.querySelector<HTMLButtonElement>('.help-jump .btn-ghost')!;
      expect(jump.textContent!.trim()).toBe('Open the prompt builder');
      jump.click();
      fixture.detectChanges();

      expect(component.activeTab).toBe('ai');
      expect(host.querySelector('app-suite-prompt-builder')).not.toBeNull();
      expect(document.activeElement!.id).toBe('suite-yaml-help-tab-ai');
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
