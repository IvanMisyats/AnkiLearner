import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import {
  ActivatedRouteSnapshot,
  Router,
  RouterStateSnapshot,
  UrlTree,
  provideRouter,
} from '@angular/router';
import { authGuard } from './auth.guard';
import { AuthService } from './auth.service';

describe('authGuard', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });
  });

  /**
   * Runs the guard as the router would for `url`. A functional guard uses inject(), which
   * only works inside an "injection context" — runInInjectionContext provides one.
   */
  function runGuard(url: string) {
    return TestBed.runInInjectionContext(() =>
      authGuard({} as ActivatedRouteSnapshot, { url } as RouterStateSnapshot),
    );
  }

  /** The guard's redirect as a URL string, e.g. "/login?returnUrl=...". */
  function redirectOf(result: unknown): string {
    expect(result).toBeInstanceOf(UrlTree);
    return TestBed.inject(Router).serializeUrl(result as UrlTree);
  }

  it('lets a signed-in user through', () => {
    TestBed.inject(AuthService).user.set({ id: 'u1', email: 'a@example.test' });

    expect(runGuard('/translate?q=hund')).toBe(true);
  });

  it('sends an anonymous user to login with the requested page as returnUrl', () => {
    expect(redirectOf(runGuard('/translate?q=hund'))).toBe(
      '/login?returnUrl=%2Ftranslate%3Fq%3Dhund',
    );
    expect(redirectOf(runGuard('/study/TargetToKnown'))).toBe(
      '/login?returnUrl=%2Fstudy%2FTargetToKnown',
    );
  });

  it('adds no returnUrl for home, the default after login anyway', () => {
    expect(redirectOf(runGuard('/'))).toBe('/login');
  });
});
