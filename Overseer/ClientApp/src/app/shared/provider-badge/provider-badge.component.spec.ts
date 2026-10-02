import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ProviderBadgeComponent } from './provider-badge.component';

describe('ProviderBadgeComponent', () => {
  let component: ProviderBadgeComponent;
  let fixture: ComponentFixture<ProviderBadgeComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [ProviderBadgeComponent]
    }).compileComponents();

    fixture = TestBed.createComponent(ProviderBadgeComponent);
    component = fixture.componentInstance;
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('applies the openai modifier class for provider "openai"', () => {
    component.provider = 'openai';
    fixture.detectChanges();

    const host = fixture.nativeElement;
    expect(host.classList.contains('provider-badge')).toBe(true);
    expect(host.classList.contains('provider-badge--openai')).toBe(true);
    expect(host.classList.contains('provider-badge--anthropic')).toBe(false);
    expect(host.classList.contains('provider-badge--google')).toBe(false);
  });

  it('colours "OpenAI" the same as "openai" (case-insensitive)', () => {
    component.provider = 'OpenAI';
    fixture.detectChanges();

    expect(fixture.nativeElement.classList.contains('provider-badge--openai')).toBe(true);
  });

  it('applies the anthropic modifier class for provider "Anthropic"', () => {
    component.provider = 'Anthropic';
    fixture.detectChanges();

    const host = fixture.nativeElement;
    expect(host.classList.contains('provider-badge--anthropic')).toBe(true);
    expect(host.classList.contains('provider-badge--openai')).toBe(false);
    expect(host.classList.contains('provider-badge--google')).toBe(false);
  });

  it('applies the google modifier class for provider "google"', () => {
    component.provider = 'google';
    fixture.detectChanges();

    const host = fixture.nativeElement;
    expect(host.classList.contains('provider-badge--google')).toBe(true);
    expect(host.classList.contains('provider-badge--openai')).toBe(false);
    expect(host.classList.contains('provider-badge--anthropic')).toBe(false);
  });

  it('applies no modifier class for an unknown provider', () => {
    component.provider = 'Mistral';
    fixture.detectChanges();

    const host = fixture.nativeElement;
    expect(host.classList.contains('provider-badge--openai')).toBe(false);
    expect(host.classList.contains('provider-badge--anthropic')).toBe(false);
    expect(host.classList.contains('provider-badge--google')).toBe(false);
  });

  it('applies no modifier class for a null/empty provider', () => {
    component.provider = null;
    fixture.detectChanges();

    const host = fixture.nativeElement;
    expect(host.classList.contains('provider-badge--openai')).toBe(false);
    expect(host.classList.contains('provider-badge--anthropic')).toBe(false);
    expect(host.classList.contains('provider-badge--google')).toBe(false);
  });

  it('renders the provider label verbatim', () => {
    component.provider = 'Anthropic';
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent.trim()).toBe('Anthropic');
  });
});
