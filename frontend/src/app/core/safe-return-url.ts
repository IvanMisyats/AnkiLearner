/**
 * Returns `url` when it is safe to navigate to after signing in, otherwise null.
 *
 * Why: `returnUrl` comes from the query string, so anyone can send a link like
 * `/login?returnUrl=https://evil.example`. Following it blindly after a real sign-in would be an
 * **open redirect** — the user trusts the login page, then lands on a look-alike site. Angular's
 * router only navigates inside the app, but we don't rely on that: only a plain in-app path is
 * accepted, so the value stays harmless even if it ever reaches `window.location`.
 */
export function safeReturnUrl(url: string | null | undefined): string | null {
  if (!url) return null;

  // Exactly one leading slash. No slash means a scheme ("https:", "javascript:") or a relative
  // path; two mean a protocol-relative URL ("//evil.com" = another host).
  if (!url.startsWith('/') || url.startsWith('//')) return null;

  // Browsers treat "\" like "/", so "/\evil.com" is another host too. In-app URLs never
  // contain a raw backslash (the router percent-encodes it).
  if (url.includes('\\')) return null;

  // No whitespace, control or invisible format characters: browsers silently drop tabs and
  // line breaks from URLs ("/<tab>/evil.com" becomes "//evil.com"). The router percent-encodes
  // these in real in-app URLs, so a legitimate value never has them.
  // \s = whitespace, \p{Cc} = control characters, \p{Cf} = invisible format characters;
  // the "u" flag enables the \p{...} Unicode classes.
  if (/[\s\p{Cc}\p{Cf}]/u.test(url)) return null;

  // A broken escape such as "/%" can't be parsed by the router. ("catch" without a variable
  // is like C#'s "catch { }".)
  try {
    decodeURIComponent(url);
  } catch {
    return null;
  }

  // Never back to the sign-in pages themselves: "?returnUrl=/login" would show the form again
  // after signing in, and could nest returnUrls inside each other.
  if (isAuthPage(url)) return null;

  return url;
}

/**
 * True for the login and register pages, with or without a query, fragment, matrix parameters
 * (";x=1") or a trailing path. Route paths are case-sensitive, so "/Login" is not one.
 */
export function isAuthPage(url: string): boolean {
  return /^\/(login|register)([/?#;]|$)/.test(url);
}
