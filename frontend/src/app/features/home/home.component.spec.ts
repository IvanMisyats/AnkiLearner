import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { StudyCountsDto } from '../../core/api.types';
import { AuthService } from '../../core/auth.service';
import { HomeComponent } from './home.component';

describe('HomeComponent', () => {
  let http: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [HomeComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    }).compileComponents();
    http = TestBed.inject(HttpTestingController);
    TestBed.inject(AuthService).settings.set({
      learningLanguage: 'da',
      knownLanguages: ['en', 'uk'],
      dailyNewLimit: 20,
      maxIntervalDays: 36500, desiredRetention: 0.9, fsrsWeights: null,
    });
  });

  afterEach(() => http.verify());

  /** Creates the page and answers the three requests it makes on arrival. */
  async function render(counts: StudyCountsDto[], aiAvailable = true) {
    const fixture = TestBed.createComponent(HomeComponent);
    fixture.detectChanges(); // ngOnInit sends the requests
    http.expectOne((r) => r.url === '/api/study/counts').flush(counts);
    http.expectOne('/api/lookup/status').flush({ available: aiAvailable, provider: 'claude' });
    http.expectOne('/api/languages').flush([
      { code: 'da', name: 'Danish' },
      { code: 'en', name: 'English' },
      { code: 'uk', name: 'Ukrainian' },
    ]);
    await fixture.whenStable();
    fixture.detectChanges();
    return fixture;
  }

  function text(fixture: { nativeElement: HTMLElement }): string {
    return fixture.nativeElement.textContent ?? '';
  }

  it('sums due and new cards over both directions', async () => {
    const fixture = await render([
      { exercise: 'TargetToKnown', due: 7, new: 3, reviewedToday: 4 },
      { exercise: 'KnownToTarget', due: 5, new: 2, reviewedToday: 9 },
    ]);

    expect(fixture.componentInstance.backlog()).toEqual({ due: 12, new: 5 });
    expect(text(fixture)).toContain('12 due');
    expect(text(fixture)).toContain('5 new');
    expect(text(fixture)).not.toContain('Nothing due');
  });

  it('says "Nothing due right now" when nothing is due or new', async () => {
    // Reviews already done today don't count: they are history, not work left.
    const fixture = await render([
      { exercise: 'TargetToKnown', due: 0, new: 0, reviewedToday: 12 },
      { exercise: 'KnownToTarget', due: 0, new: 0, reviewedToday: 3 },
    ]);

    expect(text(fixture)).toContain('Nothing due right now');
    expect(text(fixture)).not.toContain(' new');
  });

  it('names the user\'s languages on the Translate card', async () => {
    const fixture = await render([]);

    expect(text(fixture)).toContain(
      'Look up a word, phrase or sentence in Danish, English or Ukrainian',
    );
    expect(text(fixture)).not.toContain('AI is not configured');
  });

  it('hints when AI is not configured, but keeps Translate a link', async () => {
    const fixture = await render([], false);

    expect(text(fixture)).toContain('AI is not configured on this server');
    const translate = fixture.nativeElement.querySelector('a[href="/translate"]');
    expect(translate).not.toBeNull();
  });

  it('has a page heading for screen readers', async () => {
    const fixture = await render([]);

    expect(fixture.nativeElement.querySelector('h1')?.textContent).toBe('Choose a mode');
  });

  it('links the two cards to /translate and /study', async () => {
    const fixture = await render([]);
    const links = Array.from(fixture.nativeElement.querySelectorAll('a')) as HTMLAnchorElement[];

    expect(links.map((a) => a.getAttribute('href'))).toEqual(['/translate', '/study']);
  });
});
