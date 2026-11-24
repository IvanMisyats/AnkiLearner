import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { StudyCountsDto } from '../../core/api.types';
import { AuthService } from '../../core/auth.service';
import { DashboardComponent } from './dashboard.component';

describe('DashboardComponent', () => {
  let http: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [DashboardComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    }).compileComponents();
    http = TestBed.inject(HttpTestingController);
    TestBed.inject(AuthService).settings.set({
      learningLanguage: 'da',
      knownLanguages: ['en'],
      dailyNewLimit: 20,
      maxIntervalDays: 36500, desiredRetention: 0.9, fsrsWeights: null,
    });
  });

  afterEach(() => http.verify());

  async function render(counts: StudyCountsDto[]) {
    const fixture = TestBed.createComponent(DashboardComponent);
    fixture.detectChanges(); // ngOnInit issues counts + tags + languages
    http.expectOne((r) => r.url === '/api/study/counts').flush(counts);
    http.expectOne('/api/tags').flush([]);
    http.expectOne('/api/languages').flush([{ code: 'da', name: 'Danish' }]);
    await fixture.whenStable();
    fixture.detectChanges();
    return fixture;
  }

  it('shows a reviewed-today counter per direction', async () => {
    const fixture = await render([
      { exercise: 'TargetToKnown', due: 3, new: 5, reviewedToday: 12 },
      { exercise: 'KnownToTarget', due: 0, new: 0, reviewedToday: 7 },
    ]);
    const text = fixture.nativeElement.textContent as string;

    expect(text).toContain('reviewed today');
    expect(text).toContain('12');
    expect(text).toContain('7');
  });

  it('leaves Start disabled when only reviewed-today is non-zero', async () => {
    // Reviews already done are history — they must not make the direction look startable.
    const fixture = await render([
      { exercise: 'TargetToKnown', due: 0, new: 0, reviewedToday: 12 },
      { exercise: 'KnownToTarget', due: 1, new: 0, reviewedToday: 0 },
    ]);

    expect(fixture.componentInstance.totalFor('TargetToKnown')).toBe(0);
    expect(fixture.componentInstance.totalFor('KnownToTarget')).toBe(1);
  });
});
