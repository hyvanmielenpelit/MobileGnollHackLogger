import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { of, Subject } from 'rxjs';

import { LoginComponent } from './login.component';
import { AuthService } from '../services/auth.service';
import { SystemService } from '../services/system.service';

describe('LoginComponent', () => {
  let fixture: ComponentFixture<LoginComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [LoginComponent],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting()
      ]
    }).compileComponents();

    vi.spyOn(TestBed.inject(SystemService), 'getVersion').mockReturnValue(of('1.2.3'));
    vi.spyOn(TestBed.inject(AuthService), 'checkAuth').mockReturnValue(of({ kind: 'signedOut' as const }));
    fixture = TestBed.createComponent(LoginComponent);
    fixture.detectChanges();
  });

  it('links the Terms of Service and the privacy notice under the form', () => {
    const legal = (fixture.nativeElement as HTMLElement).querySelector<HTMLElement>('.login-legal')!;
    expect(legal.textContent?.replace(/\s+/g, ' ').trim()).toBe('Gnoll Overseer Terms of Service · Privacy');

    const links = Array.from(legal.querySelectorAll<HTMLAnchorElement>('a'));
    expect(links.map(a => a.textContent?.trim())).toEqual(['Terms of Service', 'Privacy']);
    expect(links.map(a => a.getAttribute('href'))).toEqual(['/terms', '/privacy']);
  });

  it('centers a card that fits', () => {
    const host = fixture.nativeElement as HTMLElement;
    host.style.minHeight = '2000px';

    const hostRect = host.getBoundingClientRect();
    const cardRect = host.querySelector('.login-container')!.getBoundingClientRect();

    expect(Math.abs((cardRect.top - hostRect.top) - (hostRect.bottom - cardRect.bottom))).toBeLessThanOrEqual(1);
  });

  it('does not clip a card that overflows', () => {
    const host = fixture.nativeElement as HTMLElement;
    host.style.minHeight = '100px';

    const hostTop = host.getBoundingClientRect().top;
    const cardTop = host.querySelector('.login-container')!.getBoundingClientRect().top;

    expect(cardTop - hostTop).toBe(16);
  });

  it('keeps the title on one line', async () => {
    await document.fonts.ready;

    const range = document.createRange();
    range.selectNodeContents((fixture.nativeElement as HTMLElement).querySelector('.logo-title')!);
    const tops = new Set(Array.from(range.getClientRects()).map(rect => rect.top));

    expect(tops.size).toBe(1);
  });

  it('does not move the card when the version arrives', () => {
    const version = new Subject<string>();
    vi.mocked(TestBed.inject(SystemService).getVersion).mockReturnValue(version);
    const pending = TestBed.createComponent(LoginComponent);
    pending.detectChanges();

    const host = pending.nativeElement as HTMLElement;
    host.style.minHeight = '2000px';
    const card = host.querySelector('.login-container')!;
    const topBefore = card.getBoundingClientRect().top;
    const heightBefore = card.getBoundingClientRect().height;
    expect(host.querySelector('.app-version-label')!.textContent).not.toContain('Overseer');

    version.next('1.2.3');
    pending.detectChanges();

    expect(host.querySelector('.app-version-label')!.textContent).toContain('Overseer 1.2.3');
    expect(card.getBoundingClientRect().height).toBe(heightBefore);
    expect(card.getBoundingClientRect().top).toBe(topBefore);
  });
});
