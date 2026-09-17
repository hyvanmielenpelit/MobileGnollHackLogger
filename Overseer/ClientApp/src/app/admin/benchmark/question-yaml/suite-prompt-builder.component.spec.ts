import { ComponentFixture, TestBed } from '@angular/core/testing';
import { SUITE_ADD_QUESTIONS_PROMPT_FILE_NAME, SuitePromptBuilderComponent } from './suite-prompt-builder.component';
import { SuiteAgentPromptOptions, buildSuiteAgentPrompt } from './suite-agent-prompt';
import { SUITE_AI_PROMPT_FILE_NAME } from './suite-yaml-guide';

describe('SuitePromptBuilderComponent', () => {
  let fixture: ComponentFixture<SuitePromptBuilderComponent>;
  let component: SuitePromptBuilderComponent;
  let host: HTMLElement;

  const PATH = 'C:\\temp\\gnollhack.valkyrie.ai.html';
  const SUITE_PATH = 'C:\\temp\\benchmark-suite-core.yaml';

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [SuitePromptBuilderComponent] }).compileComponents();
    fixture = TestBed.createComponent(SuitePromptBuilderComponent);
    component = fixture.componentInstance;
    fixture.componentRef.setInput('idPrefix', 'suite-prompt');
    fixture.componentRef.setInput('source', 'snapshot-file');
    fixture.componentRef.setInput('sourcePath', PATH);
    fixture.detectChanges();
    host = fixture.nativeElement as HTMLElement;
  });

  const input = (suffix: string): HTMLInputElement => host.querySelector<HTMLInputElement>(`#suite-prompt-${suffix}`)!;
  const prompt = (): HTMLElement | null => host.querySelector('app-code-block pre code');
  const status = (): string => host.querySelector('.builder-status')!.textContent!.trim();

  const type = (suffix: string, value: string) => {
    const el = input(suffix);
    el.value = value;
    el.dispatchEvent(new Event('input'));
    fixture.detectChanges();
  };

  const submit = () => {
    host.querySelector<HTMLButtonElement>('button[type="submit"]')!.click();
    fixture.detectChanges();
  };

  const snapshotOptions = (overrides: Partial<SuiteAgentPromptOptions> = {}): SuiteAgentPromptOptions => ({
    source: 'snapshot-file', sourcePath: PATH, suiteName: '', counts: null, waitForGoAhead: true, ...overrides
  });

  it('labels every control and prefixes every element id', () => {
    component.setCountsMode('manual');
    fixture.detectChanges();

    const controls = Array.from(host.querySelectorAll<HTMLInputElement>('input'));
    expect(controls.length).toBeGreaterThan(4);
    for (const control of controls) {
      expect(control.labels!.length).withContext(control.id || control.type).toBeGreaterThan(0);
    }
    for (const el of Array.from(host.querySelectorAll('[id]'))) {
      expect(el.id).withContext(el.id).toMatch(/^(tip-)?suite-prompt-/);
    }
  });

  it('opens with no prompt, no path field and the go-ahead box ticked', () => {
    expect(prompt()).toBeNull();
    expect(input('path')).toBeNull();
    expect(input('wait').checked).toBeTrue();
    expect(input('counts-propose').checked).toBeTrue();
  });

  it('generates the prompt the builder module would, announces it and emits the options', () => {
    const emitted = jasmine.createSpy('generated');
    component.generated.subscribe(emitted);
    submit();

    expect(prompt()!.textContent).toBe(buildSuiteAgentPrompt(snapshotOptions()));
    expect(status()).toBe('Prompt generated.');
    expect(emitted).toHaveBeenCalledWith(snapshotOptions());
  });

  it('renders the prompt in a code block with the file name, Download and Copy', () => {
    submit();
    const block = host.querySelector('app-code-block')!;
    expect(block.querySelector('.code-block-caption')!.textContent).toBe(SUITE_AI_PROMPT_FILE_NAME);
    expect(Array.from(block.querySelectorAll('button')).map(b => b.getAttribute('aria-label')))
      .toEqual(['Download the prompt', 'Copy the prompt to the clipboard']);
  });

  it('shows the output file name once a suite name is typed', () => {
    type('name', 'Valkyrie at Dlvl 11');
    expect(host.querySelector('#suite-prompt-name-file')!.textContent)
      .toContain('benchmark-suite-valkyrie-at-dlvl-11.yaml');
  });

  it('hides the suite name for a suite YAML and names the questions file from the known suite', () => {
    fixture.componentRef.setInput('source', 'suite-yaml');
    fixture.componentRef.setInput('sourcePath', SUITE_PATH);
    fixture.componentRef.setInput('knownSuiteName', 'Core');
    fixture.detectChanges();

    expect(input('name')).toBeNull();
    expect(host.querySelector('#suite-prompt-name-file')!.textContent).toContain('benchmark-questions-core.yaml');

    submit();
    expect(prompt()!.textContent).toBe(buildSuiteAgentPrompt({
      source: 'suite-yaml', sourcePath: SUITE_PATH, suiteName: 'Core', counts: null, waitForGoAhead: true
    }));
    expect(host.querySelector('.code-block-caption')!.textContent).toBe(SUITE_ADD_QUESTIONS_PROMPT_FILE_NAME);
  });

  it('discards the prompt when the route or path input changes', () => {
    submit();
    expect(prompt()).not.toBeNull();

    fixture.componentRef.setInput('sourcePath', 'C:\\temp\\other.ai.html');
    fixture.detectChanges();
    expect(prompt()).toBeNull();
    expect(status()).toBe('Inputs changed — generate the prompt again.');
  });

  it('shows a missing path as an error rather than generating', () => {
    fixture.componentRef.setInput('sourcePath', '');
    fixture.detectChanges();
    submit();
    expect(prompt()).toBeNull();
    expect(host.querySelector('#suite-prompt-source-error')!.textContent).toContain('Enter the path to the snapshot file.');
  });

  it('reveals three counts and their total on "Set them myself"', () => {
    expect(host.querySelector('#suite-prompt-simple')).toBeNull();

    input('counts-manual').click();
    fixture.detectChanges();

    expect(host.querySelectorAll('input[type="number"]').length).toBe(3);
    expect(host.querySelector('#suite-prompt-counts-total')!.textContent).toContain('18 questions in total.');

    submit();
    expect(prompt()!.textContent).toContain('6 Simple / 6 Intermediate / 6 Advanced (18 in total)');
  });

  it('refuses counts that add up to more than the cap', () => {
    input('counts-manual').click();
    fixture.detectChanges();
    type('simple', '20');
    type('intermediate', '20');
    type('advanced', '11');
    submit();

    expect(prompt()).toBeNull();
    expect(host.querySelector('#suite-prompt-counts-error')!.textContent)
      .toContain('A suite holds at most 50 questions; these add up to 51.');
    expect(document.activeElement).toBe(input('simple'));
  });

  it('writes the continue-without-waiting line when the box is unticked', () => {
    input('wait').click();
    fixture.detectChanges();
    submit();

    expect(prompt()!.textContent).toContain('Count table: show it for information, then continue without waiting for me');
  });

  it('discards the prompt as soon as any field changes', () => {
    submit();
    expect(prompt()).not.toBeNull();

    type('name', 'Valkyrie');
    expect(prompt()).toBeNull();
    expect(status()).toBe('Inputs changed — generate the prompt again.');
  });
});
