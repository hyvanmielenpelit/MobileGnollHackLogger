import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { AppComponent } from './app.component';
import { ClientBridgeService } from './services/client-bridge.service';
import { AuthService } from './services/auth.service';

describe('AppComponent', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [AppComponent],
      providers: [provideRouter([])]
    }).compileComponents();
  });

  it('should notify ClientBridgeService on NavigationEnd when embedded', async () => {
    const fixture = TestBed.createComponent(AppComponent);
    const bridge = TestBed.inject(ClientBridgeService);
    const router = TestBed.inject(Router);

    vi.spyOn(bridge, 'isEmbedded').mockReturnValue(true);
    const notifySpy = vi.spyOn(bridge, 'notifyUrlChanged').mockReturnValue(undefined);

    fixture.detectChanges();
    await router.navigateByUrl('/');

    expect(notifySpy).toHaveBeenCalled();
  });

  it('shows the connection-problem notice, and Retry clears it and navigates to the URL', () => {
    const fixture = TestBed.createComponent(AppComponent);
    const auth = TestBed.inject(AuthService);
    const navigateSpy = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
    fixture.detectChanges();

    const alert = () => fixture.nativeElement.querySelector('.auth-connection-problem[role="alert"]') as HTMLElement | null;
    expect(alert()).toBeNull();

    auth.connectionProblemUrl.set('/settings/models');
    fixture.detectChanges();
    expect(alert()?.textContent).toContain('You have not been signed out');

    const retry = alert()!.querySelector('button') as HTMLButtonElement;
    expect(retry.textContent?.trim()).toBe('Retry');
    retry.click();
    fixture.detectChanges();

    expect(navigateSpy).toHaveBeenCalledWith('/settings/models');
    expect(auth.connectionProblemUrl()).toBeNull();
    expect(alert()).toBeNull();
  });
});
