import { ComponentFixture, TestBed } from '@angular/core/testing';
import { beforeEach, describe, expect, it } from 'vitest';

import { ChatContextUsage } from '../../services/chat.service';
import { ContextUsageComponent } from './context-usage.component';

const usage: ChatContextUsage = {
  promptTokens: 28400, outputTokens: 1500, usedTokens: 29900, windowTokens: 1050000,
  inputLimitTokens: 200000, modelDisplayName: 'GPT-x', provider: 'OpenAI', modelId: 'gpt-x'
};

const breakdown = {
  version: 1, systemPrompt: 8120, toolDefinitions: 6430, gameContext: 4200, history: 7300, documents: 1100, images: 650,
  toolActivity: 600, cacheRead: 28100, cacheWrite: 1800, reasoning: 400
};

describe('ContextUsageComponent', () => {
  let fixture: ComponentFixture<ContextUsageComponent>;

  const set = (next: ChatContextUsage, selectedModel?: any) => {
    fixture.componentRef.setInput('usage', next);
    fixture.componentRef.setInput('selectedModel', selectedModel);
    fixture.detectChanges();
  };
  const el = () => fixture.nativeElement as HTMLElement;
  const button = () => el().querySelector('button.cw-button') as HTMLButtonElement;

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [ContextUsageComponent] }).compileComponents();
    fixture = TestBed.createComponent(ContextUsageComponent);
  });

  it('should show the percentage in one button that names its value', () => {
    set(usage);
    expect(button().textContent).toContain('Context');
    expect(button().textContent).toContain('3%');
    expect(button().getAttribute('aria-label')).toBe(`Context usage: 3%, ${(29900).toLocaleString()} of ${(1050000).toLocaleString()} tokens. Show breakdown`);
    expect(button().getAttribute('aria-describedby')).toBeNull();
    expect(button().getAttribute('popovertarget')).toBe('cw-breakdown');
    expect(button().getAttribute('interestfor')).toBeNull();
    expect(el().querySelector('[popover="hint"]')).toBeNull();
    expect(button().getAttribute('aria-expanded')).toBe('false');
  });

  it('should keep the target at least 24 px', () => {
    set(usage);
    const rect = button().getBoundingClientRect();
    expect(rect.height).toBeGreaterThanOrEqual(24);
    expect(rect.width).toBeGreaterThanOrEqual(24);
  });

  it('should bind the ring to the percentage and grade it by severity', () => {
    set(usage);
    const ring = () => el().querySelector('.cw-ring') as HTMLElement;
    expect(ring().style.getPropertyValue('--cw-value')).toBe('3');
    expect(ring().getAttribute('aria-hidden')).toBe('true');

    set({ ...usage, usedTokens: 170000 });
    expect(ring().classList.contains('cw-ring-elevated')).toBe(true);

    set({ ...usage, usedTokens: 950000, inputLimitTokens: undefined });
    expect(ring().classList.contains('cw-ring-critical')).toBe(true);
    expect(el().querySelector('.cw-warning')).toBeTruthy();
  });

  it('should mark a projected figure, and say what it is measured on', () => {
    set(usage, { provider: 'Anthropic', modelId: 'claude-y', displayName: 'Claude Y', contextWindowTokens: 50000, inputLimitTokens: 20000 });
    expect(button().textContent).toContain('~40%');
    expect(button().getAttribute('aria-label')).toContain('about 40%');
    const notes = el().querySelector('.cw-panel-notes')!.textContent!;
    expect(notes).toContain('Measured on GPT-x. On Claude Y the chat would use about');
    expect(notes).toContain('Older messages will no longer be sent.');
  });

  it('should claim no percentage when the selected window is unknown', () => {
    set(usage, { provider: 'OpenAI', modelId: 'custom', displayName: 'Custom', contextWindowTokens: null });
    expect(button().textContent).not.toContain('%');
    expect(el().querySelector('.cw-panel-notes')!.textContent).toContain('is not known');
  });

  it('should show what is available with no stored breakdown', () => {
    set(usage);
    const rows = Array.from(el().querySelectorAll('.cw-table tbody th')).map(th => th.textContent!.trim());
    expect(rows).toEqual(['Prompt', 'Output', 'Free space']);
    expect(el().querySelector('.cw-panel-notes')!.textContent).toContain('Category detail appears after the next reply.');
    expect(el().querySelector('.cw-table')!.textContent).not.toContain('≈');
  });

  it('should show the categories with estimated rows marked, and the measured rows without', () => {
    set({ ...usage, breakdown });
    const table = el().querySelector('.cw-table')!.textContent!;
    expect(table).toContain('System prompt');
    expect(table).toContain(`≈ ${(8120).toLocaleString()}`);
    expect(table).toContain('Latest reply');
    expect(table).not.toContain(`≈ ${(1500).toLocaleString()}`);
    expect(table).toContain('Reused from cache');
    expect(table).toContain('Written to cache');
    expect(table).toContain('Reasoning in reply');
    expect(el().querySelector('.cw-panel-notes')!.textContent).toContain('≈ Estimated share of the exact total.');
    expect(el().querySelector('.cw-bar')!.getAttribute('role')).toBe('img');
    expect(el().querySelector('.cw-bar')!.getAttribute('aria-label')).toContain('System prompt');
  });

  it('should open the panel from the button, and report it in aria-expanded', async () => {
    set(usage);
    const panel = el().querySelector('#cw-breakdown') as HTMLElement;
    const toggled = new Promise(resolve => panel.addEventListener('toggle', resolve, { once: true }));
    button().click();
    await toggled;
    fixture.detectChanges();
    expect(panel.matches(':popover-open')).toBe(true);
    expect(button().getAttribute('aria-expanded')).toBe('true');
    panel.hidePopover();
  });
});
