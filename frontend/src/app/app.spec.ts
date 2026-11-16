import { Component } from '@angular/core';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideLocationMocks } from '@angular/common/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { App } from './app';
import { AuthService } from './core/auth.service';

/** Stands in for every page: these tests only look at the toolbar. */
@Component({ template: '' })
class BlankPage {}

/** Lets the router finish navigating. */
const settle = () => new Promise((resolve) => setTimeout(resolve, 0));

describe('App', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideLocationMocks(),
        provideRouter([
          { path: '', pathMatch: 'full', component: BlankPage },
          { path: 'study', component: BlankPage },
          { path: 'study/:exercise', component: BlankPage },
          { path: 'translate', component: BlankPage },
          { path: 'words', component: BlankPage },
          { path: 'settings', component: BlankPage },
        ]),
      ],
    }).compileComponents();
  });

  it('should create the app', () => {
    const fixture = TestBed.createComponent(App);
    const app = fixture.componentInstance;
    expect(app).toBeTruthy();
  });

  describe('toolbar', () => {
    /** Signs a user in, opens the app at `url` and returns the rendered fixture. */
    async function openAt(url: string): Promise<ComponentFixture<App>> {
      TestBed.inject(AuthService).user.set({ id: 'u1', email: 'a@example.test' });
      const fixture = TestBed.createComponent(App);
      await TestBed.inject(Router).navigateByUrl(url);
      await settle();
      await fixture.whenStable();
      fixture.detectChanges();
      return fixture;
    }

    function navLinks(fixture: ComponentFixture<App>): HTMLAnchorElement[] {
      return Array.from(fixture.nativeElement.querySelectorAll('nav a'));
    }

    function activeLabels(fixture: ComponentFixture<App>): (string | null)[] {
      return navLinks(fixture)
        .filter((a) => a.classList.contains('active'))
        .map((a) => a.getAttribute('aria-label'));
    }

    it('shows Translate, Learn, Dictionary and Settings — Import moved to the Dictionary page', async () => {
      const fixture = await openAt('/');

      expect(navLinks(fixture).map((a) => a.getAttribute('href'))).toEqual([
        '/translate', '/study', '/words', '/settings',
      ]);
      expect(fixture.nativeElement.querySelector('button[aria-label="Sign out"]')).not.toBeNull();
    });

    it('highlights nothing on the home page', async () => {
      const fixture = await openAt('/');

      expect(activeLabels(fixture)).toEqual([]);
    });

    it('highlights Learn on the dashboard and during a review', async () => {
      expect(activeLabels(await openAt('/study'))).toEqual(['Learn']);
      expect(activeLabels(await openAt('/study/TargetToKnown'))).toEqual(['Learn']);
    });

    it('marks the active link for screen readers', async () => {
      const fixture = await openAt('/translate');
      const translate = navLinks(fixture).find((a) => a.getAttribute('href') === '/translate');

      expect(activeLabels(fixture)).toEqual(['Translate']);
      expect(translate?.getAttribute('aria-current')).toBe('page');
    });
  });
});
