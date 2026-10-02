import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { AppComponent } from './app.component';
import { ClientBridgeService } from './services/client-bridge.service';

describe('AppComponent', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [AppComponent],
      providers: [provideRouter([])]
    }).compileComponents();
  });

  it('should create the app', () => {
    const fixture = TestBed.createComponent(AppComponent);
    const app = fixture.componentInstance;
    expect(app).toBeTruthy();
  });

  it(`should have the 'ClientApp' title`, () => {
    const fixture = TestBed.createComponent(AppComponent);
    const app = fixture.componentInstance;
    expect(app.title).toEqual('ClientApp');
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
});
