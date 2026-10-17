import { TestBed } from '@angular/core/testing';
import { HttpClient, HttpErrorResponse, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Router, provideRouter } from '@angular/router';

import { authInterceptor } from './auth.interceptor';
import { AuthService } from './services/auth.service';

describe('authInterceptor', () => {
  let http: HttpClient;
  let httpMock: HttpTestingController;
  let auth: AuthService;
  let router: Router;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        provideHttpClient(withInterceptors([authInterceptor])),
        provideHttpClientTesting()
      ]
    });
    http = TestBed.inject(HttpClient);
    httpMock = TestBed.inject(HttpTestingController);
    auth = TestBed.inject(AuthService);
    router = TestBed.inject(Router);
    vi.spyOn(router, 'navigate').mockResolvedValue(true);

    auth.checkAuth().subscribe();
    httpMock.expectOne('/api/auth/me').flush({ userName: 'player', email: 'player@example.com', hasApiKey: true });
  });

  afterEach(() => httpMock.verify());

  function get(status: number, statusText: string): HttpErrorResponse | undefined {
    let error: HttpErrorResponse | undefined;
    http.get('/api/something').subscribe({ error: e => { error = e; } });
    const req = httpMock.expectOne('/api/something');
    expect(req.request.withCredentials).toBe(true);
    req.flush('', { status, statusText });
    return error;
  }

  it('sends a 401 to the login page and clears the cached user', () => {
    const error = get(401, 'Unauthorized');

    expect(error?.status).toBe(401);
    expect(router.navigate).toHaveBeenCalledWith(['/login']);
    expect(auth.currentUser).toBeNull();
  });

  it('rethrows a 503 without navigating or clearing the cached user', () => {
    const error = get(503, 'Service Unavailable');

    expect(error?.status).toBe(503);
    expect(router.navigate).not.toHaveBeenCalled();
    expect(auth.currentUser).not.toBeNull();
  });
});
