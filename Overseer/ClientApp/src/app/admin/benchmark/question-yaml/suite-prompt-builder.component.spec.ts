import { ComponentFixture, TestBed } from '@angular/core/testing';
import { SuitePromptBuilderComponent } from './suite-prompt-builder.component';
import { buildSuiteAgentPrompt } from './suite-agent-prompt';

describe('SuitePromptBuilderComponent', () => {
  let fixture: ComponentFixture<SuitePromptBuilderComponent>;
  let component: SuitePromptBuilderComponent;
  let host: HTMLElement;

  const PATH = 'C:\\temp\\gnollhack.valkyrie.ai.html';

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [SuitePromptBuilderComponent] }).compileComponents();
    fixture = TestBed.createComponent(SuitePromptBuilderComponent);
    component = fixture.componentInstance;
    component.idPrefix = 'suite-prompt';
    fixture.detectChanges();
    host = fixture.nativeElement as HTMLElement;
  });

  const input = (suffix: string): HTMLInputElement => host.querySelector<HTMLInputElement>(`#suite-prompt-${suffix}`)!;
  const prompt = (): HTMLElement | null => host.querySelector('.help-ai-text');
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

  const withClipboard = async (run: (writeText: jasmine.Spy) => Promise<void>, reject = false) => {
    const writeText = jasmine.createSpy('writeText')
      .and.returnValue(reject ? Promise.reject(new Error('denied')) : Promise.resolve());
    const original = Object.getOwnPropertyDescriptor(navigator, 'clipboard');
    Object.defineProperty(navigator, 'clipboard', { value: { writeText }, configurable: true, writable: true });
    try {
      await run(writeText);
    } finally {
      delete (navigator as { clipboard?: unknown }).clipboard;
      if (original) Object.defineProperty(navigator, 'clipboard', original);
    }
  };

  it('labels every control and prefixes every element id', () => {
    component.setCountsMode('manual');
    fixture.detectChanges();

    const controls = Array.from(host.querySelectorAll<HTMLInputElement>('input'));
    expect(controls.length).toBeGreaterThan(5);
    for (const control of controls) {
      expect(control.labels!.length).withContext(control.id || control.type).toBeGreaterThan(0);
    }
    for (const el of Array.from(host.querySelectorAll('[id]'))) {
      expect(el.id).withContext(el.id).toMatch(/^suite-prompt-/);
    }
  });

  it('opens with no prompt and the go-ahead box ticked', () => {
    expect(prompt()).toBeNull();
    expect(input('wait').checked).toBeTrue();
    expect(input('counts-propose').checked).toBeTrue();
  });

  it('refuses a blank path, marks the field and puts focus in it', () => {
    submit();
    expect(prompt()).toBeNull();
    expect(input('path').getAttribute('aria-invalid')).toBe('true');
    expect(host.querySelector('#suite-prompt-path-error')!.textContent)
      .toContain('Enter the path to the snapshot file.');
    expect(document.activeElement).toBe(input('path'));

    type('path', PATH);
    expect(host.querySelector('#suite-prompt-path-error')).toBeNull();
    expect(input('path').getAttribute('aria-invalid')).toBeNull();
  });

  it('generates the prompt the builder module would, and announces it', () => {
    type('path', PATH);
    submit();

    expect(prompt()!.textContent).toBe(buildSuiteAgentPrompt({
      snapshotPath: PATH,
      suiteName: '',
      counts: null,
      waitForGoAhead: true
    }));
    expect(status()).toBe('Prompt generated.');
  });

  it('advises on a path that does not look rooted, without refusing it', () => {
    type('path', 'board.ai.html');
    expect(host.querySelector('#suite-prompt-path-advisory')).not.toBeNull();
    submit();
    expect(prompt()).not.toBeNull();
  });

  it('shows the output file name once a suite name is typed', () => {
    type('name', 'Valkyrie at Dlvl 11');
    expect(host.querySelector('#suite-prompt-name-file')!.textContent)
      .toContain('benchmark-suite-valkyrie-at-dlvl-11.yaml');
  });

  it('reveals three counts and their total on "Set them myself"', () => {
    expect(host.querySelector('#suite-prompt-simple')).toBeNull();

    input('counts-manual').click();
    fixture.detectChanges();

    expect(host.querySelectorAll('input[type="number"]').length).toBe(3);
    expect(host.querySelector('#suite-prompt-counts-total')!.textContent).toContain('18 questions in total.');

    type('path', PATH);
    submit();
    expect(prompt()!.textContent).toContain('6 Simple / 6 Intermediate / 6 Advanced (18 in total)');
  });

  it('refuses counts that add up to more than the cap', () => {
    input('counts-manual').click();
    fixture.detectChanges();
    type('path', PATH);
    type('simple', '20');
    type('intermediate', '20');
    type('advanced', '11');
    submit();

    expect(prompt()).toBeNull();
    expect(host.querySelector('#suite-prompt-counts-error')!.textContent)
      .toContain('A suite holds at most 50 questions; these add up to 51.');
  });

  it('writes the continue-without-waiting line when the box is unticked', () => {
    type('path', PATH);
    input('wait').click();
    fixture.detectChanges();
    submit();

    expect(prompt()!.textContent).toContain('Count table: show it for information, then continue without waiting for me');
  });

  it('discards the prompt as soon as any field changes', () => {
    type('path', PATH);
    submit();
    expect(prompt()).not.toBeNull();

    type('name', 'Valkyrie');
    expect(prompt()).toBeNull();
    expect(status()).toBe('Inputs changed — generate the prompt again.');
  });

  it('offers Copy and Download beside the prompt', () => {
    type('path', PATH);
    submit();
    expect(Array.from(host.querySelectorAll('.builder-result .btn-ghost')).map(b => b.textContent!.trim()))
      .toEqual(['Copy Prompt', 'Download Prompt']);
  });

  it('copies the prompt and announces the result', async () => {
    await withClipboard(async writeText => {
      type('path', PATH);
      submit();
      await component.copyPrompt();
      fixture.detectChanges();

      expect(writeText).toHaveBeenCalledWith(component.prompt);
      expect(host.querySelector('.help-copy-status')!.textContent).toBe('Copied');
    });
  });

  it('falls back to Download when the clipboard refuses', async () => {
    await withClipboard(async () => {
      type('path', PATH);
      submit();
      await component.copyPrompt();
      fixture.detectChanges();

      expect(host.querySelector('.help-copy-status')!.textContent).toBe('Could not copy; use Download instead.');
    }, true);
  });
});
