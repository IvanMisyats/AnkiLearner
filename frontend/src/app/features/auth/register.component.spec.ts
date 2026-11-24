import { Component } from '@angular/core';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideLocationMocks } from '@angular/common/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { RegisterComponent } from './register.component';

/** Stands in for the pages the register page can send the user to. */
@Component({ template: '' })
class OtherPage {}

/** Lets pending promises settle. */
const settle = () => new Promise((resolve) => setTimeout(resolve, 0));

const user = { id: 'u1', email: 'a@example.test' };
const settings = {
  learningLanguage: 'da', knownLanguages: ['en'], dailyNewLimit: 20, maxIntervalDays: 36500, desiredRetention: 0.9, fsrsWeights: null,
};

describe('RegisterComponent — returnUrl', () => {
  let http: HttpTestingController;
  let harness: RouterTestingHarness;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideLocationMocks(),
        provideRouter([
          { path: 'register', component: RegisterComponent },
          { path: 'login', component: OtherPage },
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

  /** Opens `registerUrl`, registers successfully and returns the URL the user ends up on. */
  async function register(registerUrl: string): Promise<string> {
    const component = await harness.navigateByUrl(registerUrl, RegisterComponent);
    component.form.setValue({ email: 'a@example.test', password: 'password123' });

    const done = component.submit();
    http.expectOne('/api/auth/register').flush({ accessToken: 'token-1', user });
    await settle(); // the register call continues with /me
    http.expectOne('/api/auth/me').flush({ user, settings });
    await done;
    await settle();
    await harness.fixture.whenStable();
    return TestBed.inject(Router).url;
  }

  it('returns to a safe returnUrl after registering', async () => {
    expect(await register('/register?returnUrl=%2Ftranslate%3Fq%3Dhund')).toBe('/translate?q=hund');
  });

  it('ignores an unsafe returnUrl', async () => {
    expect(await register('/register?returnUrl=%2F%2Fevil.com')).toBe('/');
    expect(await register('/register?returnUrl=javascript%3Aalert(1)')).toBe('/');
  });

  it('passes the returnUrl on to the sign-in link', async () => {
    await harness.navigateByUrl('/register?returnUrl=%2Ftranslate%3Fq%3Dhund', RegisterComponent);
    harness.detectChanges();
    const link = harness.routeNativeElement!.querySelector('a[href^="/login"]');

    expect(link?.getAttribute('href')).toBe('/login?returnUrl=%2Ftranslate%3Fq%3Dhund');
  });

  it('drops an unsafe returnUrl from the sign-in link', async () => {
    await harness.navigateByUrl('/register?returnUrl=https%3A%2F%2Fevil.com', RegisterComponent);
    harness.detectChanges();
    const link = harness.routeNativeElement!.querySelector('a[href^="/login"]');

    expect(link?.getAttribute('href')).toBe('/login');
  });

  it('replaces the register page in the history after success', async () => {
    // vi.spyOn records the calls but still runs the real method.
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigateByUrl');

    await register('/register');

    expect(navigate).toHaveBeenLastCalledWith('/', { replaceUrl: true });
  });
});
