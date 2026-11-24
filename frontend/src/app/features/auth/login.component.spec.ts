import { Component } from '@angular/core';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideLocationMocks } from '@angular/common/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { LoginComponent } from './login.component';

/** Stands in for the pages the login page can send the user to. */
@Component({ template: '' })
class OtherPage {}

/** Lets pending promises settle. */
const settle = () => new Promise((resolve) => setTimeout(resolve, 0));

const user = { id: 'u1', email: 'a@example.test' };
const settings = {
  learningLanguage: 'da', knownLanguages: ['en'], dailyNewLimit: 20, maxIntervalDays: 36500, desiredRetention: 0.9, fsrsWeights: null,
};

describe('LoginComponent — returnUrl', () => {
  let http: HttpTestingController;
  let harness: RouterTestingHarness;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideLocationMocks(),
        provideRouter([
          { path: 'login', component: LoginComponent },
          { path: 'register', component: OtherPage },
          { path: 'translate', component: OtherPage },
          { path: '', pathMatch: 'full', component: OtherPage },
          { path: '**', component: OtherPage },
        ]),
      ],
    }).compileComponents();
    http = TestBed.inject(HttpTestingController);
    harness = await RouterTestingHarness.create();
  });

  afterEach(() => http.verify());

  /** Opens `loginUrl`, signs in successfully and returns the URL the user ends up on. */
  async function signIn(loginUrl: string): Promise<string> {
    const component = await harness.navigateByUrl(loginUrl, LoginComponent);
    component.form.setValue({ email: 'a@example.test', password: 'password123' });

    const done = component.submit();
    http.expectOne('/api/auth/login').flush({ accessToken: 'token-1', user });
    await settle(); // the login call continues with /me
    http.expectOne('/api/auth/me').flush({ user, settings });
    await done;
    await settle();
    await harness.fixture.whenStable();
    return TestBed.inject(Router).url;
  }

  it('returns to a safe returnUrl after signing in', async () => {
    expect(await signIn('/login?returnUrl=%2Ftranslate%3Fq%3Dhund')).toBe('/translate?q=hund');
  });

  it('replaces the login page in the history, so Back does not return to the form', async () => {
    // vi.spyOn records the calls but still runs the real method.
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigateByUrl');

    await signIn('/login?returnUrl=%2Ftranslate%3Fq%3Dhund');

    expect(navigate).toHaveBeenLastCalledWith('/translate?q=hund', { replaceUrl: true });
  });

  it('goes home without a returnUrl', async () => {
    expect(await signIn('/login')).toBe('/');
  });

  it('ignores a returnUrl pointing to another host', async () => {
    expect(await signIn('/login?returnUrl=%2F%2Fevil.com')).toBe('/');
  });

  it('ignores a returnUrl with a scheme', async () => {
    expect(await signIn('/login?returnUrl=https%3A%2F%2Fevil.com')).toBe('/');
  });

  it('ignores a returnUrl with a backslash', async () => {
    expect(await signIn('/login?returnUrl=%2F%5Cevil.com')).toBe('/');
  });

  it('ignores a malformed returnUrl ("/%") instead of failing', async () => {
    expect(await signIn('/login?returnUrl=%2F%25')).toBe('/');
  });

  it('ignores a returnUrl pointing back to the sign-in pages', async () => {
    expect(await signIn('/login?returnUrl=%2Flogin')).toBe('/');
    expect(await signIn('/login?returnUrl=%2Fregister%3FreturnUrl%3D%252Fx')).toBe('/');
  });

  it('keeps an odd but accepted returnUrl inside the app', async () => {
    // "/%2F%252F%252Fevil.com" is one oddly named path segment: the router treats it as an
    // in-app page (here the ** fallback), never as another host.
    const landed = await signIn('/login?returnUrl=' + encodeURIComponent('/%2F%252F%252Fevil.com'));

    expect(landed).toBe('/%2F%252F%252Fevil.com');
    expect(new URL(landed, 'https://app.example').origin).toBe('https://app.example');
  });

  it('passes a safe returnUrl on to the register link, and drops an unsafe one', async () => {
    await harness.navigateByUrl('/login?returnUrl=%2Ftranslate%3Fq%3Dhund', LoginComponent);
    harness.detectChanges();
    let link = harness.routeNativeElement!.querySelector('a[href^="/register"]');
    expect(link?.getAttribute('href')).toBe('/register?returnUrl=%2Ftranslate%3Fq%3Dhund');

    await harness.navigateByUrl('/login?returnUrl=%2F%2Fevil.com', LoginComponent);
    harness.detectChanges();
    link = harness.routeNativeElement!.querySelector('a[href^="/register"]');
    expect(link?.getAttribute('href')).toBe('/register');
  });
});
