import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideLocationMocks } from '@angular/common/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { AuthService } from '../../core/auth.service';
import { ConfirmData, ConfirmService } from '../../core/confirm.service';
import { WordFormComponent } from './word-form.component';

/** Stands in for the Material dialog so no real one is opened during tests. */
class ConfirmStub {
  answer = true;
  readonly asked: ConfirmData[] = [];

  async ask(data: ConfirmData): Promise<boolean> {
    this.asked.push(data);
    return this.answer;
  }
}

/** Lets pending promises settle — the confirmation resolves before the HTTP call is made. */
const settle = () => new Promise((resolve) => setTimeout(resolve, 0));

const word = {
  id: 'w1',
  languageCode: 'da',
  term: 'hund',
  transcription: null,
  partOfSpeech: null,
  gender: null,
  example: null,
  notes: null,
  createdAt: '2026-09-06T00:00:00Z',
  updatedAt: '2026-09-06T00:00:00Z',
  translations: [{ languageCode: 'en', text: 'dog', exampleTranslation: null }],
  tags: [],
};

describe('WordFormComponent — reset progress', () => {
  let http: HttpTestingController;
  let confirm: ConfirmStub;

  beforeEach(async () => {
    confirm = new ConfirmStub();
    await TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideLocationMocks(),
        provideRouter([
          { path: 'words/new', component: WordFormComponent },
          { path: 'words/:id/edit', component: WordFormComponent },
        ]),
        { provide: ConfirmService, useValue: confirm },
      ],
    }).compileComponents();
    http = TestBed.inject(HttpTestingController);
    // Read while the form is built, so it must be set before the component is created.
    TestBed.inject(AuthService).settings.set({
      learningLanguage: 'da',
      knownLanguages: ['en'],
      dailyNewLimit: 20,
      maxIntervalDays: 36500,
    });
  });

  afterEach(() => http.verify());

  /**
   * Navigates to a real route so the component picks up :id exactly as it does in the
   * app — RouterTestingHarness creates the routed component and hands it back.
   */
  async function render(url: string, editedId?: string) {
    const harness = await RouterTestingHarness.create();
    const component = await harness.navigateByUrl(url, WordFormComponent);
    http.expectOne('/api/lookup/status').flush({ available: false, provider: 'none' });
    http.expectOne('/api/tags').flush([]);
    if (editedId) http.expectOne(`/api/words/${editedId}`).flush(word);
    await harness.fixture.whenStable();
    harness.detectChanges();
    return { component, text: () => harness.routeNativeElement!.textContent as string };
  }

  it('offers no reset button when adding a word', async () => {
    const { text } = await render('/words/new');
    expect(text()).not.toContain('Reset progress');
  });

  it('deletes the progress once the user confirms', async () => {
    const { component, text } = await render('/words/w1/edit', 'w1');
    expect(text()).toContain('Reset progress');

    const done = component.resetProgress();
    await settle();
    http.expectOne({ url: '/api/words/w1/progress', method: 'DELETE' }).flush(null);
    await done;

    expect(confirm.asked.length).toBe(1);
    expect(confirm.asked[0].confirmText).toBe('Reset');
    expect(confirm.asked[0].destructive).toBe(true);
  });

  it('does nothing when the user cancels', async () => {
    const { component } = await render('/words/w1/edit', 'w1');
    confirm.answer = false;

    await component.resetProgress();

    http.expectNone({ url: '/api/words/w1/progress', method: 'DELETE' });
    expect(confirm.asked.length).toBe(1);
  });
});
