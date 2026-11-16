import { safeReturnUrl } from './safe-return-url';

describe('safeReturnUrl', () => {
  it('accepts in-app paths, with or without a query', () => {
    expect(safeReturnUrl('/translate?q=hund')).toBe('/translate?q=hund');
    expect(safeReturnUrl('/study/TargetToKnown')).toBe('/study/TargetToKnown');
    expect(safeReturnUrl('/')).toBe('/');
    expect(safeReturnUrl('/translate?q=jeg%20har%20ondt')).toBe('/translate?q=jeg%20har%20ondt');
  });

  it('rejects other hosts and schemes (open redirect)', () => {
    expect(safeReturnUrl('//evil.com')).toBeNull();
    expect(safeReturnUrl('/\\evil.com')).toBeNull();
    expect(safeReturnUrl('https://evil.com')).toBeNull();
    expect(safeReturnUrl('javascript:alert(1)')).toBeNull();
    expect(safeReturnUrl('evil.com')).toBeNull();
  });

  it('rejects a backslash anywhere', () => {
    expect(safeReturnUrl('/x\\y')).toBeNull();
  });

  it('rejects whitespace and control characters', () => {
    // Built with fromCharCode so the test source holds no invisible characters.
    const tab = String.fromCharCode(9);
    const newline = String.fromCharCode(10);
    const zeroWidthSpace = String.fromCharCode(0x200b);

    expect(safeReturnUrl(' /x')).toBeNull();
    expect(safeReturnUrl('/x ')).toBeNull();
    expect(safeReturnUrl('/' + tab + '/evil.com')).toBeNull();
    expect(safeReturnUrl('/' + newline + '/evil.com')).toBeNull();
    expect(safeReturnUrl('/' + zeroWidthSpace + '/evil.com')).toBeNull();
  });

  it('rejects a broken percent escape', () => {
    expect(safeReturnUrl('/%')).toBeNull();
    expect(safeReturnUrl('/translate?q=%E0%A4%A')).toBeNull();
  });

  it('rejects the sign-in pages themselves, so returning can never loop', () => {
    expect(safeReturnUrl('/login')).toBeNull();
    expect(safeReturnUrl('/login?returnUrl=%2Ftranslate')).toBeNull();
    expect(safeReturnUrl('/login#top')).toBeNull();
    expect(safeReturnUrl('/login;x=1')).toBeNull();
    expect(safeReturnUrl('/login/')).toBeNull();
    expect(safeReturnUrl('/register')).toBeNull();
    expect(safeReturnUrl('/register?returnUrl=%2Fstudy')).toBeNull();
  });

  it('accepts paths that only start like a sign-in page', () => {
    expect(safeReturnUrl('/loginhelp')).toBe('/loginhelp');
    expect(safeReturnUrl('/words?search=login')).toBe('/words?search=login');
  });

  it('rejects empty values', () => {
    expect(safeReturnUrl('')).toBeNull();
    expect(safeReturnUrl(null)).toBeNull();
    expect(safeReturnUrl(undefined)).toBeNull();
  });
});
