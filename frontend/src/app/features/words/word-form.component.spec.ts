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

describe('WordFormComponent — progress actions and lookup', () => {
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
    return { component, harness, text: () => harness.routeNativeElement!.textContent as string };
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

  it('Study again relearns the word without asking, disabled while it runs', async () => {
    const { harness } = await render('/words/w1/edit', 'w1');
    const studyAgainButton = () =>
      Array.from(harness.routeNativeElement!.querySelectorAll('button')).find((b) =>
        b.textContent!.includes('Study again'),
      )!;

    studyAgainButton().click();
    harness.detectChanges();
    const request = http.expectOne({ url: '/api/words/w1/relearn', method: 'POST' });
    expect(studyAgainButton().disabled).toBe(true);

    request.flush(null);
    await settle();
    harness.detectChanges();

    expect(studyAgainButton().disabled).toBe(false);
    expect(confirm.asked.length).toBe(0); // not destructive, so no confirmation
  });

  it('offers no Study again button when adding a word', async () => {
    const { text } = await render('/words/new');
    expect(text()).not.toContain('Study again');
  });

  it('escapes the AI lookup result before putting it into the HTML fields', async () => {
    const { component } = await render('/words/new');
    component.form.controls.term.setValue('mindre');

    const done = component.lookup();
    http.expectOne({ url: '/api/lookup', method: 'POST' }).flush({
      term: 'mindre <end>',
      transcription: '[ˈmendʁɐ]',
      partOfSpeech: 'adjective',
      gender: '',
      meanings: { en: ['less <than>'] },
      example: 'a <b> c',
      exampleTranslations: { en: 'x & y' },
    });
    await done;
    // The filled-in term triggers the duplicate check.
    http.expectOne((r) => r.url === '/api/words/duplicate').flush({ exists: false, wordId: null });

    const value = component.form.getRawValue();
    expect(value.term).toBe('mindre &lt;end&gt;');
    expect(value.example).toBe('a &lt;b&gt; c');
    expect(value.translations[0].text).toBe('less &lt;than&gt;'); // a single meaning: no list
    expect(value.translations[0].exampleTranslation).toBe('x &amp; y');
    expect(value.transcription).toBe('[ˈmendʁɐ]'); // plain-text field, as is
  });
});
