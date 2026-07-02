import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { authInterceptor } from './auth.interceptor';
import { AuthService } from './auth.service';

describe('authInterceptor', () => {
  let httpClient: HttpClient;
  let http: HttpTestingController;
  let auth: AuthService;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([authInterceptor])),
        provideHttpClientTesting(),
        // sessionExpired() navigates to /login — the test router needs that route.
        provideRouter([{ path: 'login', children: [] }]),
      ],
    });
    httpClient = TestBed.inject(HttpClient);
    http = TestBed.inject(HttpTestingController);
    auth = TestBed.inject(AuthService);
  });

  afterEach(() => http.verify());

  /** Lets pending promise chains (interceptor → refresh → retry) fully settle. */
  const settle = () => new Promise((resolve) => setTimeout(resolve, 0));

  it('attaches the bearer token to requests', async () => {
    auth.accessToken = 'token-1';
    const request = firstValueFrom(httpClient.get('/api/words'));

    const pending = http.expectOne('/api/words');
    expect(pending.request.headers.get('Authorization')).toBe('Bearer token-1');
    pending.flush({ items: [] });
    await request;
  });

  it('on 401 it refreshes once and retries the request', async () => {
    auth.accessToken = 'stale';
    const request = firstValueFrom(httpClient.get('/api/words'));

    http.expectOne('/api/words').flush(null, { status: 401, statusText: 'Unauthorized' });
    await settle();
    http.expectOne('/api/auth/refresh').flush({
      accessToken: 'fresh',
      user: { id: 'u1', email: 'a@b.c' },
    });
    await settle();

    const retried = http.expectOne('/api/words');
    expect(retried.request.headers.get('Authorization')).toBe('Bearer fresh');
    retried.flush({ items: [], total: 0 });

    await request;
  });

  it('when the refresh also fails the error propagates and the session is cleared', async () => {
    auth.accessToken = 'stale';
    const request = firstValueFrom(httpClient.get('/api/words'));

    http.expectOne('/api/words').flush(null, { status: 401, statusText: 'Unauthorized' });
    await settle();
    http.expectOne('/api/auth/refresh').flush(null, { status: 401, statusText: 'Unauthorized' });

    await expect(request).rejects.toMatchObject({ status: 401 });
    expect(auth.accessToken).toBeNull();
  });
});
