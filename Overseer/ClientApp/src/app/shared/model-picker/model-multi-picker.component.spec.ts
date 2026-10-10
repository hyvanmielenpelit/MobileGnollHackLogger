import { ChangeDetectionStrategy, ChangeDetectorRef, Component, inject } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';

import { ModelMultiPickerComponent, ModelMultiPickerSelection } from './model-multi-picker.component';
import { ModelPickerComponent, ModelPickerKey, ModelPickerModel, ModelPickerOption } from './model-picker.component';

interface TestModel extends ModelPickerModel {
  id: number;
}

const ALPHA: TestModel = {
  id: 1, displayName: 'Alpha', provider: 'OpenAI', thinkingLevel: 'high', reasoningMode: 'pro', parallelExecutionMode: 0,
  effectiveInputPricePerMillion: 5, effectiveOutputPricePerMillion: 25
};
const BETA: TestModel = { id: 2, displayName: 'Beta', provider: 'Anthropic', parallelExecutionMode: 1 };

const OPTIONS: ModelPickerOption<TestModel>[] = [
  { key: 'run:1', model: ALPHA, detail: 'Run #1' },
  { key: 'battery:10', model: ALPHA, detail: 'Battery run #10' },
  { key: 'run:2', model: BETA }
];

/** Both pickers over the same models, each feeding its selection back. */
@Component({
  standalone: true,
  imports: [ModelPickerComponent, ModelMultiPickerComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="outer" (keydown)="outerKeys.push($any($event).key)">
      <app-model-picker class="single" label="Single" [options]="singleOptions" [selectedKey]="null"></app-model-picker>
      <app-model-multi-picker class="multi" label="Models" [options]="options" [selectedKeys]="selected"
                              (selectionChange)="onChange($event)"></app-model-multi-picker>
      <app-model-multi-picker class="multi-priced" label="Priced models" [options]="options" [selectedKeys]="pricedSelected"
                              [showPrice]="true" [showParallel]="true"></app-model-multi-picker>
      <app-model-multi-picker class="multi-warning" label="Warned models" [options]="options" [selectedKeys]="selected"
                              detailTone="warning"></app-model-multi-picker>
    </div>
  `
})
class HostComponent {
  private cdr = inject(ChangeDetectorRef);
  singleOptions: ModelPickerOption<TestModel>[] = [{ key: 1, model: ALPHA }, { key: 2, model: BETA }];
  options: ModelPickerOption<TestModel>[] = OPTIONS;
  selected: ModelPickerKey[] = [];
  pricedSelected: ModelPickerKey[] = [];
  changes: ModelMultiPickerSelection<TestModel>[] = [];
  outerKeys: string[] = [];

  onChange(selection: ModelMultiPickerSelection<TestModel>): void {
    this.changes.push(selection);
    this.selected = selection.keys;
    this.cdr.markForCheck();
  }

  select(keys: ModelPickerKey[]): void {
    this.selected = keys;
    this.cdr.markForCheck();
  }

  selectPriced(keys: ModelPickerKey[]): void {
    this.pricedSelected = keys;
    this.cdr.markForCheck();
  }
}

describe('ModelMultiPickerComponent', () => {
  let fixture: ComponentFixture<HostComponent>;
  let host: HostComponent;
  let el: HTMLElement;

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [HostComponent] }).compileComponents();
    fixture = TestBed.createComponent(HostComponent);
    host = fixture.componentInstance;
    fixture.detectChanges();
    el = fixture.nativeElement as HTMLElement;
  });

  const part = (selector: string) => el.querySelector<HTMLElement>(selector)!;
  const trigger = (p: HTMLElement) => p.querySelector<HTMLButtonElement>('.selector-trigger')!;
  const listbox = (p: HTMLElement) => p.querySelector<HTMLElement>('[role="listbox"]');
  const options = (p: HTMLElement) => Array.from(p.querySelectorAll<HTMLElement>('[role="option"]'));

  function open(p: HTMLElement): void {
    trigger(p).click();
    fixture.detectChanges();
  }

  function key(target: HTMLElement, keyName: string): KeyboardEvent {
    const event = new KeyboardEvent('keydown', { key: keyName, bubbles: true, cancelable: true });
    target.dispatchEvent(event);
    fixture.detectChanges();
    return event;
  }

  it('draws the same badges as the single picker for the same model', () => {
    const single = part('.single');
    const multi = part('.multi');
    open(single);
    const singleBadges = options(single)[0].querySelector('app-model-option-badges')!.innerHTML;
    key(listbox(single)!, 'Escape');
    open(multi);
    const multiBadges = options(multi)[0].querySelector('app-model-option-badges')!;
    expect(singleBadges).toContain('thinking-badge');
    expect(multiBadges.innerHTML).toBe(singleBadges);
    expect(multiBadges.textContent!.replace(/\s+/g, ' ')).toContain('thinking level High');
    expect(multiBadges.querySelector('.provider-badge')!.textContent!.trim()).toBe('OpenAI');
  });

  it('shows no price or parallel badge by default, and both when enabled', () => {
    const multi = part('.multi');
    open(multi);
    expect(multi.querySelector('.price-badge')).toBeNull();
    expect(multi.querySelector('.parallel-badge')).toBeNull();

    const priced = part('.multi-priced');
    open(priced);
    expect(options(priced)[0].querySelector('.price-badge')).not.toBeNull();
    expect(options(priced)[0].querySelector('.parallel-badge.badge-sequential')).not.toBeNull();
    expect(options(priced)[2].querySelector('.parallel-badge.badge-on-request')).not.toBeNull();
  });

  it('draws the same badges on a chip as on its option', () => {
    const multi = part('.multi');
    host.select(['run:1']);
    fixture.detectChanges();
    open(multi);
    const optionBadges = options(multi)[0].querySelector('app-model-option-badges')!.innerHTML;
    key(listbox(multi)!, 'Escape');
    const chip = multi.querySelector<HTMLElement>('.gh-multi-picker-chip')!;
    const chipBadges = chip.querySelector('.gh-multi-picker-chip-badges app-model-option-badges')!;
    expect(chipBadges.innerHTML).toBe(optionBadges);
    expect(chipBadges.querySelector('.thinking-badge')).not.toBeNull();
    expect(chipBadges.querySelector('.provider-badge')).not.toBeNull();
    expect(chipBadges.querySelector('.price-badge')).toBeNull();
    expect(chip.querySelector('.gh-multi-picker-chip-label')!.textContent!.trim()).toBe('Alpha');
  });

  it('honors showPrice and showParallel on the chips', () => {
    host.selectPriced(['run:1']);
    fixture.detectChanges();
    const chip = part('.multi-priced').querySelector<HTMLElement>('.gh-multi-picker-chip')!;
    expect(chip.querySelector('.price-badge')).not.toBeNull();
    expect(chip.querySelector('.parallel-badge.badge-sequential')).not.toBeNull();
  });

  it('draws a chip detail as a muted note, or as a warning with a hidden prefix', () => {
    host.select(['run:1']);
    fixture.detectChanges();
    const muted = part('.multi').querySelector<HTMLElement>('.gh-multi-picker-chip-note')!;
    expect(muted.classList).not.toContain('is-warning');
    expect(muted.textContent!.replace(/\s+/g, ' ').trim()).toBe('Run #1');

    const warning = part('.multi-warning').querySelector<HTMLElement>('.gh-multi-picker-chip-note')!;
    expect(warning.classList).toContain('is-warning');
    expect(warning.querySelector('svg')!.getAttribute('aria-hidden')).toBe('true');
    expect(warning.textContent!.replace(/\s+/g, ' ').trim()).toMatch(/^Warning: Run #1/);
  });

  it('offers two options of the same model as two options, each with its detail', () => {
    const multi = part('.multi');
    open(multi);
    const all = options(multi);
    expect(all.length).toBe(3);
    expect(all.map(o => o.querySelector('.model-name')!.textContent!.trim())).toEqual(['Alpha', 'Alpha', 'Beta']);
    expect(all.map(o => o.querySelector('.gh-multi-picker-detail')?.textContent!.trim() ?? null))
      .toEqual(['Run #1', 'Battery run #10', null]);
  });

  it('summarizes in models, with the model name when one is selected', () => {
    const multi = part('.multi');
    const summary = () => trigger(multi).querySelector('.gh-multi-picker-summary')!.textContent!.trim();
    expect(summary()).toBe('Select models');
    host.select(['run:2']);
    fixture.detectChanges();
    expect(summary()).toBe('Beta');
    host.select(['run:1', 'run:2']);
    fixture.detectChanges();
    expect(summary()).toBe('2 of 3 models');
  });

  it('a keyboard toggle emits the keys and their models', () => {
    const multi = part('.multi');
    open(multi);
    key(listbox(multi)!, 'ArrowDown');
    key(listbox(multi)!, ' ');
    expect(host.changes.length).toBe(1);
    expect(host.changes[0].keys).toEqual(['battery:10']);
    expect(host.changes[0].models).toEqual([ALPHA]);
    expect(options(multi)[1].getAttribute('aria-selected')).toBe('true');
  });

  it('leads the badges of a model that needs attention with its chip, part of the option text', () => {
    const flagged: TestModel = {
      id: 3, displayName: 'Old Flash', provider: 'Google',
      modelAvailability: { status: 'retired', needsAttention: true, retiredOn: '2026-09-30' }
    };
    host.options = [...OPTIONS, { key: 'run:3', model: flagged }];
    host.select([]);
    fixture.detectChanges();
    const multi = part('.multi');
    open(multi);

    const option = options(multi)[3];
    const chip = option.querySelector<HTMLElement>('.model-option-notice')!;
    expect(chip.getAttribute('data-tone')).toBe('warning');
    expect(chip.textContent!.replace(/\s+/g, ' ').trim()).toBe('status: Removed');
    expect(chip.compareDocumentPosition(option.querySelector('.provider-badge')!) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    expect(option.hasAttribute('aria-label')).toBe(false);
    const text = option.textContent!.replace(/\s+/g, ' ');
    expect(text.indexOf('status: Removed')).toBeGreaterThan(text.indexOf('Old Flash'));
    expect(options(multi)[0].querySelector('.model-option-notice')).toBeNull();
  });

  it('Escape closes the list and keeps the key from the parent', () => {
    const multi = part('.multi');
    open(multi);
    const event = key(listbox(multi)!, 'Escape');
    expect(event.defaultPrevented).toBe(true);
    expect(listbox(multi)).toBeNull();
    expect(host.outerKeys).not.toContain('Escape');
    expect(document.activeElement).toBe(trigger(multi));
  });
});
