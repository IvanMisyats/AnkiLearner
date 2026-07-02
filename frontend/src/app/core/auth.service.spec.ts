import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { AuthService } from './auth.service';

describe('AuthService', () => {
  let service: AuthService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });
    service = TestBed.inject(AuthService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('login stores the token and loads the profile', async () => {
    const login = service.login('a@b.c', 'password123');

    http.expectOne('/api/auth/login').flush({
      accessToken: 'token-1',
      user: { id: 'u1', email: 'a@b.c' },
    });
    await Promise.resolve(); // let the login promise continue to the /me call
    http.expectOne('/api/auth/me').flush({
      user: { id: 'u1', email: 'a@b.c' },
      settings: { learningLanguage: 'da', knownLanguages: ['en'], dailyNewLimit: 20 },
    });
    await login;

    expect(service.accessToken).toBe('token-1');
    expect(service.isAuthenticated()).toBe(true);
    expect(service.settings()?.learningLanguage).toBe('da');
  });

  it('failed refresh clears the session and resolves false', async () => {
    const refresh = service.tryRefresh();
    http.expectOne('/api/auth/refresh').flush(null, { status: 401, statusText: 'Unauthorized' });

    expect(await refresh).toBe(false);
    expect(service.accessToken).toBeNull();
    expect(service.isAuthenticated()).toBe(false);
  });

  it('concurrent refresh calls share one HTTP request', async () => {
    const first = service.tryRefresh();
    const second = service.tryRefresh();

    http.expectOne('/api/auth/refresh').flush({
      accessToken: 'token-2',
      user: { id: 'u1', email: 'a@b.c' },
    });

    expect(await first).toBe(true);
    expect(await second).toBe(true);
    expect(service.accessToken).toBe('token-2');
  });
});
