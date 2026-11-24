import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
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
      settings: {
        learningLanguage: 'da', knownLanguages: ['en'], dailyNewLimit: 20, maxIntervalDays: 36500, desiredRetention: 0.9, fsrsWeights: null,
      },
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

describe('AuthService — where signing out leads', () => {
  let service: AuthService;
  let http: HttpTestingController;
  let router: Router;

  /** Lets the router finish navigating. */
  const settle = () => new Promise((resolve) => setTimeout(resolve, 0));

  beforeEach(async () => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        // Routes without components: enough for the router to navigate between URLs.
        provideRouter([
          { path: 'login', children: [] },
          { path: 'register', children: [] },
          { path: 'words/:id/edit', children: [] },
          { path: '', pathMatch: 'full', children: [] },
        ]),
      ],
    });
    service = TestBed.inject(AuthService);
    http = TestBed.inject(HttpTestingController);
    router = TestBed.inject(Router);
    service.user.set({ id: 'u1', email: 'a@b.c' });
    await router.navigateByUrl('/words/abc/edit');
  });

  afterEach(() => http.verify());

  it('an expired session goes to login with the current page as returnUrl', async () => {
    service.sessionExpired();
    await settle();

    expect(router.url).toBe('/login?returnUrl=%2Fwords%2Fabc%2Fedit');
    expect(service.isAuthenticated()).toBe(false);
  });

  it('a second expiry on the login page keeps the first returnUrl', async () => {
    service.sessionExpired();
    await settle();
    service.sessionExpired();
    await settle();

    expect(router.url).toBe('/login?returnUrl=%2Fwords%2Fabc%2Fedit');
  });

  it('an expiry on the register page stays there, with no returnUrl', async () => {
    await router.navigateByUrl('/register');
    service.sessionExpired();
    await settle();

    expect(router.url).toBe('/register');
  });

  it('an expired session on home adds no returnUrl', async () => {
    await router.navigateByUrl('/');
    service.sessionExpired();
    await settle();

    expect(router.url).toBe('/login');
  });

  it('a plain logout goes to login without a returnUrl', async () => {
    const done = service.logout();
    http.expectOne('/api/auth/logout').flush(null);
    await done;
    await settle();

    expect(router.url).toBe('/login');
    expect(service.isAuthenticated()).toBe(false);
  });
});
