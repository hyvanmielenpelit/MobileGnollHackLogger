import { ComponentFixture, TestBed } from '@angular/core/testing';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';

import { ModelSwitchDecision, ModelSwitchDialogComponent, ModelSwitchDialogData } from './model-switch-dialog.component';

const data = (over: Partial<ModelSwitchDialogData> = {}): ModelSwitchDialogData => ({
  currentName: 'Claude Y',
  targetName: 'Gemini X',
  impact: { cache: 'cold', truncates: false, keptTokens: null },
  usedTokens: 29900,
  cacheMayCarryOver: false,
  isStreaming: false,
  ...over
});

describe('ModelSwitchDialogComponent', () => {
  let fixture: ComponentFixture<ModelSwitchDialogComponent>;
  let component: ModelSwitchDialogComponent;
  let decisions: ModelSwitchDecision[];

  const dialog = () => (fixture.nativeElement as HTMLElement).querySelector('dialog') as HTMLDialogElement;
  const text = () => dialog().textContent!;
  const button = (label: string) => Array.from(dialog().querySelectorAll('button')).find(b => b.textContent!.includes(label)) as HTMLButtonElement;
  const closed = () => new Promise(resolve => dialog().addEventListener('close', resolve, { once: true }));

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [ModelSwitchDialogComponent] }).compileComponents();
    fixture = TestBed.createComponent(ModelSwitchDialogComponent);
    component = fixture.componentInstance;
    decisions = [];
    component.decided.subscribe(d => decisions.push(d));
    fixture.detectChanges();
  });

  afterEach(() => {
    if (dialog().open) dialog().close();
  });

  it('should open modally with focus on the safe action', () => {
    component.open(data());
    expect(dialog().open).toBe(true);
    expect(dialog().getAttribute('closedby')).toBe('any');
    expect(document.activeElement).toBe(button('Keep Claude Y'));
    expect(button('Keep Claude Y').classList.contains('btn-gh-cancel')).toBe(true);
  });

  it('should state the cold cache with its token figure', () => {
    component.open(data());
    expect(text()).toContain('The next reply will be slower and cost more');
    expect(text()).toContain('Gemini X has no cached copy of this chat');
    expect(text()).toContain(`about ${(29900).toLocaleString()} tokens`);
    expect(text()).not.toContain('Older messages');
  });

  it('should say "may" where the cache scope is not established', () => {
    component.open(data({ cacheMayCarryOver: true }));
    expect(text()).toContain('The next reply may be slower');
    expect(text()).toContain('may have no cached copy');
  });

  it('should say "may" for a settings change on one model', () => {
    component.open(data({ impact: { cache: 'partial', truncates: false, keptTokens: null } }));
    expect(text()).toContain('The next reply may be slower');
  });

  it('should give no token figure in a chat with no measurement', () => {
    component.open(data({ usedTokens: null }));
    expect(text()).toContain('this chat is read at the full input price');
    expect(text()).not.toContain('Older messages');
  });

  it('should say which model finishes the reply in progress', () => {
    component.open(data({ isStreaming: true }));
    expect(text()).toContain('The reply in progress finishes on Claude Y.');
  });

  it('should offer silencing only when the cache alone is affected', () => {
    component.open(data());
    expect(text()).toContain("Don't warn me again");
    dialog().close();

    component.open(data({ impact: { cache: 'cold', truncates: true, keptTokens: 180000 }, usedTokens: 240000 }));
    expect(text()).not.toContain("Don't warn me again");
    expect(text()).toContain('Older messages will no longer reach the model');
    expect(text()).toContain(`room for about ${(180000).toLocaleString()} of this chat's ${(240000).toLocaleString()} tokens`);
  });

  it('should show only the truncation paragraph when the cache is unaffected', () => {
    component.open(data({ impact: { cache: 'none', truncates: true, keptTokens: 180000 }, usedTokens: 240000 }));
    expect(text()).not.toContain('slower');
    expect(text()).toContain('Older messages will no longer reach the model');
  });

  it('should keep the model on Keep', async () => {
    component.open(data());
    const done = closed();
    button('Keep Claude Y').click();
    await done;
    expect(decisions).toEqual([{ switchModel: false, dontWarnAgain: false }]);
  });

  it('should switch on Switch Model, carrying the silence choice', async () => {
    component.open(data());
    const checkbox = dialog().querySelector('input[type=checkbox]') as HTMLInputElement;
    checkbox.click();
    const done = closed();
    button('Switch Model').click();
    await done;
    expect(decisions).toEqual([{ switchModel: true, dontWarnAgain: true }]);
  });

  it('should keep the model when it closes any other way', async () => {
    component.open(data());
    const done = closed();
    dialog().close();
    await done;
    expect(decisions).toEqual([{ switchModel: false, dontWarnAgain: false }]);
  });

  it('should close on a click on the backdrop', async () => {
    component.open(data());
    const done = closed();
    dialog().dispatchEvent(new MouseEvent('click', { bubbles: true }));
    await done;
    expect(decisions).toEqual([{ switchModel: false, dontWarnAgain: false }]);
  });
});
