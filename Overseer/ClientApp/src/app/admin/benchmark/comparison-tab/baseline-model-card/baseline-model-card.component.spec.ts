import type { MockedObject } from 'vitest';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { of, throwError } from 'rxjs';

import { AdminBenchmarkService, BenchmarkBaselineModelDto } from '../../../../services/admin-benchmark.service';
import type { SystemAiConfigDto } from '../../../../services/admin.service';
import { ModelPickerComponent, toModelPickerOptions } from '../../../../shared/model-picker/model-picker.component';
import { BaselineModelCardComponent } from './baseline-model-card.component';

// The Model Comparison launcher's Baseline model card, against a stubbed service.

function baselineModel(overrides: Partial<BenchmarkBaselineModelDto> = {}): BenchmarkBaselineModelDto {
  return {
    snapshotId: 31,
    displayName: 'Claude Opus',
    provider: 'Anthropic',
    modelId: 'claude-opus',
    thinkingLevel: 'high',
    reasoningMode: 'adaptive',
    serviceTier: null,
    maxOutputTokens: 32000,
    sourceConfigurationId: 7,
    setAtUtc: '2026-10-09T12:00:00Z',
    ...overrides
  };
}

const CONFIGS = [
  { id: 7, displayName: 'Claude Opus', provider: 'Anthropic', modelId: 'claude-opus', thinkingLevel: 'high', reasoningMode: 'adaptive' },
  { id: 8, displayName: 'GPT Sol', provider: 'OpenAI', modelId: 'gpt-sol', thinkingLevel: 'medium', reasoningMode: null }
] as SystemAiConfigDto[];

describe('BaselineModelCardComponent', () => {
  let fixture: ComponentFixture<BaselineModelCardComponent>;
  let service: MockedObject<AdminBenchmarkService>;

  beforeEach(async () => {
    service = {
      getBaselineModel: vi.fn().mockName('AdminBenchmarkService.getBaselineModel'),
      setBaselineModel: vi.fn().mockName('AdminBenchmarkService.setBaselineModel')
    } as unknown as MockedObject<AdminBenchmarkService>;
    await TestBed.configureTestingModule({
      imports: [BaselineModelCardComponent],
      providers: [{ provide: AdminBenchmarkService, useValue: service }]
    }).compileComponents();
    fixture = TestBed.createComponent(BaselineModelCardComponent);
    fixture.componentRef.setInput('pickerOptions', toModelPickerOptions(CONFIGS));
  });

  function el(): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  function text(selector: string): string {
    return (el().querySelector(selector)?.textContent ?? '').replace(/\s+/g, ' ').trim();
  }

  function click(selector: string): void {
    const target = el().querySelector(selector) as HTMLElement | null;
    expect(target, selector).not.toBeNull();
    target!.click();
    fixture.detectChanges();
  }

  /** The facts as term → value. */
  function facts(selector = 'dl.mc-baseline-facts'): Record<string, string> {
    const pairs: Record<string, string> = {};
    for (const group of Array.from(el().querySelectorAll(`${selector} > div`))) {
      const textOf = (node: Element | null): string => (node?.textContent ?? '').replace(/\s+/g, ' ').trim();
      pairs[textOf(group.querySelector('dt'))] = textOf(group.querySelector('dd'));
    }
    return pairs;
  }

  function picker(): ModelPickerComponent {
    return fixture.debugElement.query(By.directive(ModelPickerComponent)).componentInstance as ModelPickerComponent;
  }

  function show(baseline: BenchmarkBaselineModelDto | null): void {
    service.getBaselineModel.mockReturnValue(of(baseline));
    fixture.detectChanges();
  }

  it('shows the baseline model: its name, its settings and when it was set', () => {
    show(baselineModel());

    expect(service.getBaselineModel).toHaveBeenCalledTimes(1);
    expect(text('.bm-summary-card-eyebrow')).toBe('Baseline model');
    expect(text('#mc-baseline-title')).toBe('Claude Opus');
    expect(text('.bm-summary-card-meta')).toContain('Set on 9 Oct 2026');
    expect(facts()).toEqual({ 'Provider': 'Anthropic', 'Model id': 'claude-opus', 'Thinking level': 'high' });
    // The other settings, those that are set, in a disclosure.
    expect(text('.mc-baseline-more summary')).toBe('Other settings');
    expect(facts('.mc-baseline-more dl.bm-summary-facts')).toEqual({ 'Reasoning mode': 'adaptive', 'Max output tokens': '32,000' });
    expect(el().querySelector('.mc-baseline-clear-btn')).not.toBeNull();
    // The picker is offered only on Change.
    expect(el().querySelector('app-model-picker')).toBeNull();
  });

  it('says when no baseline model is set, and offers no Clear', () => {
    show(null);

    expect(text('#mc-baseline-title')).toBe('No baseline model');
    expect(el().querySelector('dl.mc-baseline-facts')).toBeNull();
    expect(el().querySelector('.mc-baseline-clear-btn')).toBeNull();
    expect(text('.mc-baseline-change-btn')).toBe('Change');
  });

  it('sets the configuration picked under Change as the baseline model', () => {
    show(null);
    const change = el().querySelector('.mc-baseline-change-btn') as HTMLButtonElement;
    expect(change.getAttribute('aria-expanded')).toBe('false');
    expect(change.getAttribute('aria-controls')).toBe('mc-baseline-change');

    click('.mc-baseline-change-btn');
    expect(change.getAttribute('aria-expanded')).toBe('true');
    const trigger = el().querySelector('.baseline-model-selector .selector-trigger') as HTMLButtonElement;
    expect(trigger.getAttribute('aria-labelledby')!.startsWith('mc-baseline-picker-label')).toBe(true);
    expect(trigger.getAttribute('aria-describedby')).toBe('mc-baseline-picker-hint');

    service.setBaselineModel.mockReturnValue(of(baselineModel({ displayName: 'GPT Sol', provider: 'OpenAI', modelId: 'gpt-sol', sourceConfigurationId: 8 })));
    picker().selectionChange.emit({ key: 8, model: CONFIGS[1] });
    fixture.detectChanges();

    expect(service.setBaselineModel).toHaveBeenCalledWith(8);
    expect(text('#mc-baseline-title')).toBe('GPT Sol');
    expect(el().querySelector('app-model-picker')).toBeNull();
    expect(text('.mc-baseline-status')).toBe('The baseline model is now GPT Sol.');
  });

  it('clears the baseline model', () => {
    show(baselineModel());
    service.setBaselineModel.mockReturnValue(of(null));

    click('.mc-baseline-clear-btn');

    expect(service.setBaselineModel).toHaveBeenCalledWith(null);
    expect(text('#mc-baseline-title')).toBe('No baseline model');
    expect(text('.mc-baseline-status')).toBe('The baseline model is cleared.');
  });

  it('shows the server\'s refusal and keeps the picker open', () => {
    show(baselineModel());
    click('.mc-baseline-change-btn');
    service.setBaselineModel.mockReturnValue(throwError(() => ({
      status: 400, error: { error: 'The configuration does not have the Benchmark role.' }
    })));

    picker().selectionChange.emit({ key: 8, model: CONFIGS[1] });
    fixture.detectChanges();

    expect(text('.mc-baseline-error')).toBe('The configuration does not have the Benchmark role.');
    expect(el().querySelector('.mc-baseline-error')!.getAttribute('role')).toBe('alert');
    expect(text('#mc-baseline-title')).toBe('Claude Opus');
    expect(el().querySelector('app-model-picker')).not.toBeNull();
  });

  it('says when the baseline model could not be loaded', () => {
    service.getBaselineModel.mockReturnValue(throwError(() => ({ status: 500 })));
    fixture.detectChanges();

    expect(text('.mc-baseline-error')).toBe('The baseline model could not be loaded.');
    expect(text('.mc-baseline-change-btn')).toBe('Change');
  });
});
