import { HttpErrorResponse, HttpInterceptorFn, HttpRequest } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, from, switchMap, throwError } from 'rxjs';
import { AuthService } from './auth.service';

/** Endpoints that must never trigger a token refresh (they ARE the auth flow). */
const AUTH_URLS = ['/api/auth/login', '/api/auth/register', '/api/auth/refresh', '/api/auth/logout'];

/**
 * Attaches the bearer token to every API request. When a request comes back 401
 * (expired access token), it silently refreshes once and retries the request;
 * if the refresh fails too, the user is sent back to the login page.
 */
export const authInterceptor: HttpInterceptorFn = (request, next) => {
  const auth = inject(AuthService);

  const authorized = withToken(request, auth.accessToken);
  if (AUTH_URLS.some((url) => request.url.startsWith(url))) {
    return next(authorized);
  }

  return next(authorized).pipe(
    catchError((error: unknown) => {
      if (!(error instanceof HttpErrorResponse) || error.status !== 401) {
        return throwError(() => error);
      }
      // from(promise) bridges the async refresh into the rxjs pipeline.
      return from(auth.tryRefresh()).pipe(
        switchMap((refreshed) => {
          if (!refreshed) {
            auth.sessionExpired();
            return throwError(() => error);
          }
          return next(withToken(request, auth.accessToken));
        }),
      );
    }),
  );
};

function withToken(request: HttpRequest<unknown>, token: string | null): HttpRequest<unknown> {
  return token
    ? request.clone({ setHeaders: { Authorization: `Bearer ${token}` } })
    : request;
}
