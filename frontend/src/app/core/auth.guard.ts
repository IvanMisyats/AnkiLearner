import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from './auth.service';

/**
 * Blocks navigation for anonymous users and sends them to the login page, remembering the page
 * they asked for (`state.url`, e.g. a shared `/translate?q=hund` link) as `?returnUrl=`, so the
 * login page can bring them back there (it checks the value with safeReturnUrl first).
 */
export const authGuard: CanActivateFn = (_route, state) => {
  const auth = inject(AuthService);
  const router = inject(Router);
  if (auth.isAuthenticated()) return true;
  // Returning a UrlTree (instead of true/false) tells the router to redirect there.
  // Home needs no returnUrl (it is the default); a null query param is left out of the URL.
  return router.createUrlTree(['/login'], {
    queryParams: { returnUrl: state.url === '/' ? null : state.url },
  });
};
