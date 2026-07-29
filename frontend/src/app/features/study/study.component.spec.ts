import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { AuthService } from '../../core/auth.service';
import { StudyComponent } from './study.component';

describe('StudyComponent', () => {
  let http: HttpTestingController;

  const card = {
    word: {
      id: 'w1',
      languageCode: 'da',
      term: 'eg',
      transcription: '[ˈeˀj]',
      partOfSpeech: 'noun',
      gender: 'en',
      example: 'Der står en gammel eg midt på marken.',
      notes: null,
      createdAt: '2026-07-29T00:00:00Z',
      updatedAt: '2026-07-29T00:00:00Z',
      translations: [
        {
          languageCode: 'en',
          text: 'oak (tree)',
          exampleTranslation: 'An old oak stands in the middle of the field.',
        },
        {
          languageCode: 'uk',
          text: 'дуб',
          exampleTranslation: 'Посеред поля стоїть старий дуб.',
        },
      ],
      tags: [],
    },
    prompt: 'eg',
    answer: 'oak (tree)',
    isNew: true,
    intervals: { again: '10 min', hard: '1 d', good: '1 d', easy: '4 d' },
  };

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [StudyComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    }).compileComponents();
    http = TestBed.inject(HttpTestingController);
    TestBed.inject(AuthService).settings.set({
      learningLanguage: 'da',
      knownLanguages: ['en', 'uk'],
      dailyNewLimit: 20,
    });
  });

  afterEach(() => http.verify());

  async function renderCard(payload = card) {
    const fixture = TestBed.createComponent(StudyComponent);
    fixture.detectChanges(); // ngOnInit issues the /api/study/next request
    http.expectOne((r) => r.url === '/api/study/next').flush({ card: payload, remaining: 1 });
    await fixture.whenStable();
    fixture.detectChanges();
    return fixture;
  }

  it('shows only the word before the answer is revealed', async () => {
    const fixture = await renderCard();
    const text = fixture.nativeElement.textContent as string;

    expect(text).toContain('eg');
    expect(text).not.toContain('oak (tree)');
    expect(text).not.toContain('Der står en gammel eg');
    expect(text).not.toContain('An old oak stands');
  });

  it('reveals the example with a translation per known language', async () => {
    const fixture = await renderCard();
    fixture.componentInstance.reveal();
    fixture.detectChanges();
    const text = fixture.nativeElement.textContent as string;

    expect(text).toContain('oak (tree)');
    expect(text).toContain('Der står en gammel eg midt på marken.');
    expect(text).toContain('An old oak stands in the middle of the field.');
    expect(text).toContain('Посеред поля стоїть старий дуб.');
  });

  it('orders example translations by the user\'s known languages', async () => {
    TestBed.inject(AuthService).settings.set({
      learningLanguage: 'da',
      knownLanguages: ['uk', 'en'],
      dailyNewLimit: 20,
    });
    const fixture = await renderCard();

    expect(fixture.componentInstance.exampleTranslations().map((t) => t.languageCode))
      .toEqual(['uk', 'en']);
  });

  it('skips a known language that has no example translation', async () => {
    const partial = structuredClone(card);
    partial.word.translations[0].exampleTranslation = ''; // 'en' has none
    const fixture = await renderCard(partial);

    expect(fixture.componentInstance.exampleTranslations().map((t) => t.languageCode))
      .toEqual(['uk']);
  });
});
