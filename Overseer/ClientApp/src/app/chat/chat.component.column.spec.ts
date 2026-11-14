import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { HttpResponse, provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { of } from 'rxjs';

import { ChatComponent } from './chat.component';
import { SettingsService, UserAiSettings } from '../services/settings.service';

// The host is sized rather than the viewport, which stays at 800 x 600, above the 768 px phone rule.
describe('ChatComponent message column', () => {
  let fixture: ComponentFixture<ChatComponent>;

  const messages = () => (fixture.nativeElement as HTMLElement).querySelector<HTMLElement>('.messages')!;

  function create(hostWidthPx: number): void {
    vi.spyOn(ChatComponent.prototype, 'setupSignalR').mockReturnValue(undefined);
    fixture = TestBed.createComponent(ChatComponent);
    const component = fixture.componentInstance;
    const settingsService = TestBed.inject(SettingsService);
    vi.spyOn(component, 'loadSessions').mockReturnValue(undefined);
    vi.spyOn(settingsService, 'getSettings').mockReturnValue(of({} as UserAiSettings));
    vi.spyOn(settingsService, 'getSettingsResponse').mockReturnValue(
      of(new HttpResponse({ body: { hasApiKey: true, hasModel: true, spoilerFreeMode: false } as UserAiSettings })));
    vi.spyOn(settingsService, 'getUserModels').mockReturnValue(of([]));
    vi.spyOn(console, 'error').mockReturnValue(undefined);
    const host = fixture.nativeElement as HTMLElement;
    host.style.display = 'block';
    host.style.width = `${hostWidthPx}px`;
    fixture.detectChanges();
  }

  function paddings(): { start: number; end: number; content: number } {
    const element = messages();
    const style = getComputedStyle(element);
    const start = parseFloat(style.paddingInlineStart);
    const end = parseFloat(style.paddingInlineEnd);
    return { start, end, content: element.clientWidth - start - end };
  }

  beforeEach(async () => {
    // The sidebar keeps its default 250 px whatever width an earlier spec saved.
    localStorage.removeItem('overseer_sidebar_width');
    await TestBed.configureTestingModule({
      imports: [ChatComponent],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()]
    }).compileComponents();
  });

  it('centers a 56 rem column in a wide chat area', () => {
    create(1600);
    const { start, end, content } = paddings();

    expect(Math.abs(start - end)).toBeLessThanOrEqual(1);
    expect(start).toBeGreaterThan(20);
    expect(end).toBeGreaterThan(20);
    expect(Math.abs(content - 896)).toBeLessThanOrEqual(1);
  });

  it('keeps the 20 px padding in a narrower chat area', () => {
    create(1000);
    const { start, end } = paddings();

    expect(start).toBe(20);
    expect(end).toBe(20);
  });
});
