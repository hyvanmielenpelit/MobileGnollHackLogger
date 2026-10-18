import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { of } from 'rxjs';

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
});
