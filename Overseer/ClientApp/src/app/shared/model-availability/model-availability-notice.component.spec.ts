import { ComponentFixture, TestBed } from '@angular/core/testing';

import { ModelAvailability } from './model-availability';
import { ModelAvailabilityNoticeComponent } from './model-availability-notice.component';

const RETIRED: ModelAvailability = {
  status: 'retired',
  needsAttention: true,
  retiredOn: '2026-09-30',
  note: 'Google shut it down.',
  replacement: { modelId: 'gemini-3.8-flash', displayName: 'Gemini 3.8 Flash' }
};
const NOT_IN_CATALOG: ModelAvailability = { status: 'notInCatalog', needsAttention: true };
const AVAILABLE: ModelAvailability = { status: 'available', needsAttention: false };

describe('ModelAvailabilityNoticeComponent', () => {
  let fixture: ComponentFixture<ModelAvailabilityNoticeComponent>;
  let host: HTMLElement;

  const notice = () => host.querySelector<HTMLElement>('.model-availability-notice');
  const text = (el: Element | null) => (el?.textContent ?? '').replace(/\s+/g, ' ').trim();

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [ModelAvailabilityNoticeComponent] }).compileComponents();
    fixture = TestBed.createComponent(ModelAvailabilityNoticeComponent);
    host = fixture.nativeElement as HTMLElement;
  });

  function render(inputs: Record<string, unknown>): void {
    for (const [name, value] of Object.entries(inputs)) {
      fixture.componentRef.setInput(name, value);
    }
    fixture.detectChanges();
  }

  it('shows an amber warning with its icon, the sentence and the suggested replacement for a retired model', () => {
    render({ availability: RETIRED, modelName: 'Flash 3.7', modelId: 'gemini-3.7-flash' });
    const box = notice()!;
    expect(box.classList).toContain('alert');
    expect(box.classList).toContain('alert-warning');
    expect(box.classList).toContain('alert-compact');
    expect(box.querySelector('svg.alert-icon path')).not.toBeNull();
    expect(box.querySelector('svg.alert-icon')!.getAttribute('aria-hidden')).toBe('true');
    expect(text(box.querySelector('.man-sentence'))).toBe(
      'Flash 3.7 was removed from the model catalog on September 30, 2026. Google shut it down.');
    expect(text(box.querySelector('.man-replacement'))).toBe('Suggested replacement: Gemini 3.8 Flash');
  });

  it('shows a blue info notice naming the model ID for a model not in the catalog', () => {
    render({ availability: NOT_IN_CATALOG, modelName: 'My model', modelId: 'my-model-x' });
    const box = notice()!;
    expect(box.classList).toContain('alert-info');
    expect(box.classList).not.toContain('alert-warning');
    expect(box.querySelector('svg.alert-icon circle')).not.toBeNull();
    expect(text(box.querySelector('.man-sentence'))).toBe(
      "my-model-x isn't in Overseer's model catalog, so its limits and price aren't known.");
    expect(box.querySelector('.man-replacement')).toBeNull();
  });

  it('shows nothing for a model that needs no attention', () => {
    render({ availability: AVAILABLE, modelName: 'Fine' });
    expect(notice()).toBeNull();
    expect(host.hasAttribute('role')).toBe(false);
  });

  it('puts the lead sentence before and the extra text after the availability sentence', () => {
    render({
      availability: RETIRED, modelName: 'Flash 3.7',
      leadSentence: 'Your last message was not answered.', extraText: 'Choose another model to continue.'
    });
    expect(text(notice()!.querySelector('.man-sentence'))).toBe(
      'Your last message was not answered. Flash 3.7 was removed from the model catalog on September 30, 2026. '
      + 'Google shut it down. Choose another model to continue.');
  });

  it('renders a heading only when a level is given', () => {
    render({ availability: RETIRED, modelName: 'Flash 3.7' });
    expect(notice()!.querySelector('[role="heading"]')).toBeNull();
    render({ headingLevel: 4 });
    const heading = notice()!.querySelector('[role="heading"]')!;
    expect(heading.getAttribute('aria-level')).toBe('4');
    expect(text(heading)).toBe('Model removed from the catalog');
  });

  it('is no live region when it renders flagged from the start', () => {
    render({ availability: RETIRED, modelName: 'Flash 3.7' });
    expect(notice()).not.toBeNull();
    expect(host.hasAttribute('role')).toBe(false);
  });

  it('becomes a status region when the availability turns to needing attention after the first render', () => {
    render({ availability: AVAILABLE, modelName: 'Flash 3.7' });
    expect(host.hasAttribute('role')).toBe(false);
    render({ availability: RETIRED });
    expect(host.getAttribute('role')).toBe('status');
    expect(notice()).not.toBeNull();
  });

  it('is a live region from the start when the host sets announce, with the live role it asks for', () => {
    render({ availability: NOT_IN_CATALOG, modelName: 'Mine', announce: true, live: 'alert' });
    expect(host.getAttribute('role')).toBe('alert');
  });

  it('carries the variant on the host', () => {
    render({ availability: RETIRED, modelName: 'Flash 3.7', variant: 'composer' });
    expect(host.getAttribute('data-variant')).toBe('composer');
  });
});
