import { Component } from '@angular/core';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideLocationMocks } from '@angular/common/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { TranslateCardDto, TranslateResponse } from '../../core/api.types';
import { AuthService } from '../../core/auth.service';
import { NotifyService } from '../../core/notify.service';
import { TranslateComponent } from './translate.component';

/** Records toasts instead of opening Material snackbars. */
class NotifyStub {
  readonly messages: string[] = [];
  /** What successWithAction reports: was the toast's action button clicked? */
  actionClicked = false;

  success(message: string): void {
    this.messages.push(message);
  }

  error(message: string): void {
    this.messages.push(message);
  }

  httpError(error: unknown, fallback?: string): void {
    this.messages.push(NotifyService.messageFor(error, fallback));
  }

  async successWithAction(message: string): Promise<boolean> {
    this.messages.push(message);
    return this.actionClicked;
  }
}

/** Stands in for the pages the translate page links to. */
@Component({ template: '' })
class OtherPage {}

/** Lets pending promises settle. */
const settle = () => new Promise((resolve) => setTimeout(resolve, 0));

function card(overrides: Partial<TranslateCardDto> = {}): TranslateCardDto {
  return {
    term: 'en hund',
    transcription: '[ˈhunˀ]',
    partOfSpeech: 'noun',
    gender: 'en',
    meanings: { en: ['dog'], uk: ['собака'] },
    example: 'Hunden gør.',
    exampleTranslations: { en: 'The dog barks.', uk: 'Собака гавкає.' },
    note: 'Common gender.',
    existingWordId: null,
    ...overrides,
  };
}

function response(overrides: Partial<TranslateResponse> = {}): TranslateResponse {
  return {
    sourceLanguage: 'da',
    inputKind: 'Word',
    inputTranslations: { da: 'hund', en: 'dog', uk: 'собака' },
    cards: [card()],
    wholeInput: null,
    ...overrides,
  };
}

const savedWord = {
  id: 'w9',
  languageCode: 'da',
  term: 'en hund',
  transcription: null,
  partOfSpeech: null,
  gender: null,
  example: null,
  notes: null,
  createdAt: '2026-11-15T00:00:00Z',
  updatedAt: '2026-11-15T00:00:00Z',
  translations: [],
  tags: ['translated'],
};

describe('TranslateComponent', () => {
  let http: HttpTestingController;
  let notify: NotifyStub;
  let harness: RouterTestingHarness;

  beforeEach(async () => {
    notify = new NotifyStub();
    await TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideLocationMocks(),
        provideRouter([
          { path: 'translate', component: TranslateComponent },
          { path: 'words/new', component: OtherPage },
          { path: 'words/:id/edit', component: OtherPage },
          { path: 'other', component: OtherPage },
        ]),
        { provide: NotifyService, useValue: notify },
      ],
    }).compileComponents();
    http = TestBed.inject(HttpTestingController);
    // The kept result belongs to this user and their languages (see TranslateStateService).
    TestBed.inject(AuthService).user.set({ id: 'u1', email: 'a@example.test' });
    TestBed.inject(AuthService).settings.set({
      learningLanguage: 'da',
      knownLanguages: ['en', 'uk'],
      dailyNewLimit: 20,
      maxIntervalDays: 36500,
    });
    harness = await RouterTestingHarness.create();
  });

  afterEach(() => http.verify());

  /** Opens the page and answers the two requests it makes on arrival. */
  async function render(url = '/translate', available = true) {
    const component = await harness.navigateByUrl(url, TranslateComponent);
    http.expectOne('/api/lookup/status').flush({ available, provider: 'claude' });
    http.expectOne('/api/languages').flush([
      { code: 'da', name: 'Danish' },
      { code: 'en', name: 'English' },
      { code: 'uk', name: 'Ukrainian' },
    ]);
    await refresh();
    return component;
  }

  async function refresh(): Promise<void> {
    await settle();
    await harness.fixture.whenStable();
    harness.detectChanges();
  }

  function page(): HTMLElement {
    return harness.routeNativeElement as HTMLElement;
  }

  function text(): string {
    return page().textContent as string;
  }

  /**
   * The button or link labelled `label`. Material puts a button's text in a label element
   * of its own, apart from the icon (whose ligature name, e.g. "add", is text too).
   */
  function button(label: string): HTMLButtonElement | undefined {
    return Array.from(page().querySelectorAll<HTMLButtonElement>('button, a')).find((element) => {
      const labelElement =
        element.querySelector('.mdc-button__label, .mat-button-toggle-label-content') ?? element;
      return labelElement.textContent!.trim() === label;
    });
  }

  async function click(label: string): Promise<void> {
    const target = button(label);
    expect(target, `button "${label}"`).toBeTruthy();
    target!.click();
    await refresh();
  }

  /** Types into the textarea the way a user does (sets the value, fires "input"). */
  function type(value: string): HTMLTextAreaElement {
    const textarea = page().querySelector('textarea')!;
    textarea.value = value;
    textarea.dispatchEvent(new Event('input'));
    return textarea;
  }

  /** Types `value`, presses Enter and answers the translate request with `body`. */
  async function translate(value: string, body: TranslateResponse = response()) {
    const textarea = type(value);
    textarea.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter' }));
    const request = http.expectOne({ url: '/api/translate', method: 'POST' });
    request.flush(body);
    await refresh();
    return request.request.body;
  }

  it('translates ?q= straight away and removes it from the URL', async () => {
    await render('/translate?q=%22hund%22');

    const request = http.expectOne({ url: '/api/translate', method: 'POST' });
    expect(request.request.body).toEqual({ text: 'hund', sourceLanguage: null });
    request.flush(response());
    await refresh();

    expect(TestBed.inject(Router).url).toBe('/translate');
    expect(text()).toContain('en hund');
  });

  it('translates a share that has only ?title=', async () => {
    await render('/translate?title=hund');

    const request = http.expectOne({ url: '/api/translate', method: 'POST' });
    expect(request.request.body).toEqual({ text: 'hund', sourceLanguage: null });
    request.flush(response());
    await refresh();

    expect(TestBed.inject(Router).url).toBe('/translate');
  });

  it('takes shared text out of its quotes and drops the link Android appends', async () => {
    // Selected text shared from Chrome on Android: form-encoded (+ for a space), in quotes and
    // followed by a link to the selection.
    await render('/translate?q=%22ondt+i+maven%22+https%3A%2F%2Fexample.com%2Fa%23%3A~%3Atext%3Dondt');

    const request = http.expectOne({ url: '/api/translate', method: 'POST' });
    expect(request.request.body).toEqual({ text: 'ondt i maven', sourceLanguage: null });
    request.flush(response());
    await refresh();
  });

  it('prefers ?q= to ?title= and ?url=, and clears all three from the URL', async () => {
    await render('/translate?title=Avisen&q=hund&url=https%3A%2F%2Fexample.com%2F');

    const request = http.expectOne({ url: '/api/translate', method: 'POST' });
    expect(request.request.body).toEqual({ text: 'hund', sourceLanguage: null });
    request.flush(response());
    await refresh();

    expect(TestBed.inject(Router).url).toBe('/translate');
  });

  it('clears a share that holds only a link, without translating', async () => {
    await render('/translate?q=https%3A%2F%2Fexample.com%2F&url=https%3A%2F%2Fexample.com%2F');

    http.expectNone('/api/translate');
    expect(TestBed.inject(Router).url).toBe('/translate');
  });

  it('translates on Enter, but Shift+Enter only adds a line break', async () => {
    await render();

    const textarea = type('hund');
    textarea.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', shiftKey: true }));
    // Enter that confirms an input-method candidate is not ours either.
    textarea.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', isComposing: true }));
    http.expectNone('/api/translate');

    expect(await translate('hund')).toEqual({ text: 'hund', sourceLanguage: null });
  });

  it('sends one request when Enter is pressed again while translating', async () => {
    await render();
    const textarea = type('hund');
    textarea.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter' }));
    textarea.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter' }));

    http.expectOne('/api/translate').flush(response()); // expectOne fails on two requests
    await refresh();
  });

  it('shows the detected language, the translations and the cards', async () => {
    await render();
    await translate('hund');

    expect(text()).toContain('Detected: Danish');
    expect(text()).toContain('dog');
    expect(text()).toContain('собака');
    expect(text()).toContain('[ˈhunˀ] · noun');
    expect(text()).toContain('Hunden gør.');
    expect(text()).toContain('Common gender.');
  });

  it('renders AI text literally, never as HTML', async () => {
    await render();
    await translate('hund', response({ cards: [card({ term: '<img src=x onerror=alert(1)>' })] }));

    expect(page().querySelector('img')).toBeNull();
    expect(text()).toContain('<img src=x onerror=alert(1)>');
  });

  it('adds a card with the expected body and marks it Added', async () => {
    await render();
    await translate('hund');

    await click('Add');
    const request = http.expectOne({ url: '/api/words', method: 'POST' });
    expect(request.request.body).toEqual({
      term: 'en hund',
      transcription: '[ˈhunˀ]',
      partOfSpeech: 'noun',
      gender: 'en',
      example: 'Hunden gør.',
      notes: 'Common gender.',
      translations: [
        { languageCode: 'en', text: 'dog', exampleTranslation: 'The dog barks.' },
        { languageCode: 'uk', text: 'собака', exampleTranslation: 'Собака гавкає.' },
      ],
      tags: ['translated'],
    });
    expect(text()).toContain('Adding…');

    request.flush(savedWord);
    await refresh();

    expect(text()).toContain('Added');
    expect(button('Add')).toBeUndefined();
    expect(button('Edit')!.getAttribute('href')).toBe('/words/w9/edit');
    expect(notify.messages).toContain('Added');
  });

  it('opens the new word when the toast\'s Edit is clicked', async () => {
    notify.actionClicked = true;
    await render();
    await translate('hund');

    await click('Add');
    http.expectOne({ url: '/api/words', method: 'POST' }).flush(savedWord);
    await refresh();

    expect(TestBed.inject(Router).url).toBe('/words/w9/edit');
  });

  it('flags an existing word: no Add, but Study again and Add anyway', async () => {
    await render();
    await translate('hund', response({ cards: [card({ existingWordId: 'w1' })] }));

    expect(text()).toContain('In your dictionary');
    expect(button('Add')).toBeUndefined();
    expect(button('Study again')).toBeTruthy();

    await click('Add anyway');
    http.expectOne({ url: '/api/words', method: 'POST' }).flush(savedWord);
    await refresh();

    expect(text()).toContain('Added');
  });

  it('puts the card back to "In your dictionary" when Add anyway fails', async () => {
    await render();
    await translate('hund', response({ cards: [card({ existingWordId: 'w1' })] }));

    await click('Add anyway');
    http.expectOne({ url: '/api/words', method: 'POST' }).flush(
      { title: 'Could not save.' },
      { status: 500, statusText: 'Server Error' },
    );
    await refresh();

    expect(page().querySelector('a.in-dictionary')!.getAttribute('href')).toBe('/words/w1/edit');
    expect(button('Add anyway')).toBeTruthy();
    expect(notify.messages).toContain('Could not save.');
  });

  it('Study again relearns the word and is disabled while it runs', async () => {
    await render();
    await translate('hund', response({ cards: [card({ existingWordId: 'w1' })] }));

    await click('Study again');
    const request = http.expectOne({ url: '/api/words/w1/relearn', method: 'POST' });
    expect(button('Study again')!.disabled).toBe(true);

    request.flush(null);
    await refresh();

    expect(button('Study again')!.disabled).toBe(false);
    expect(notify.messages).toContain('It will come up in your next study session.');
  });

  it('offers the whole input as its own card', async () => {
    await render();
    await translate(
      'tak for mad',
      response({
        inputKind: 'Phrase',
        inputTranslations: { da: 'tak for mad', en: 'thanks for the meal', uk: '' },
        cards: [card({ term: 'en mad', meanings: { en: ['food'], uk: [] } })],
        wholeInput: card({
          term: 'tak for mad',
          transcription: '',
          partOfSpeech: '',
          gender: '',
          meanings: { en: ['thanks for the meal'], uk: [] },
          example: '',
          exampleTranslations: { en: '', uk: '' },
          note: '',
        }),
      }),
    );

    expect(text()).toContain('Whole phrase');
    const addButtons = Array.from(page().querySelectorAll('app-translate-card'));
    expect(addButtons.length).toBe(2);
    (addButtons[1].querySelector('button') as HTMLButtonElement).click(); // the whole input's Add
    await refresh();

    const request = http.expectOne({ url: '/api/words', method: 'POST' });
    expect(request.request.body).toEqual({
      term: 'tak for mad',
      transcription: null,
      partOfSpeech: null,
      gender: null,
      example: null,
      notes: null,
      translations: [{ languageCode: 'en', text: 'thanks for the meal', exampleTranslation: null }],
      tags: ['translated'],
    });
    request.flush({ ...savedWord, id: 'w10' });
  });

  it('translates again with the chosen language', async () => {
    await render();
    await translate('gift');

    await click('EN');
    const request = http.expectOne({ url: '/api/translate', method: 'POST' });
    expect(request.request.body).toEqual({ text: 'gift', sourceLanguage: 'en' });
    request.flush(response({ sourceLanguage: 'en' }));
    await refresh();

    expect(text()).toContain('Translated from English');
  });

  it('shows the unsupported-language message with hints to add or pick the language', async () => {
    await render();
    type('Hund');
    page().querySelector('textarea')!.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter' }));
    http.expectOne('/api/translate').flush(
      { title: 'Only Danish, English and Ukrainian are supported.' },
      { status: 422, statusText: 'Unprocessable Entity' },
    );
    await refresh();

    expect(text()).toContain('Only Danish, English and Ukrainian are supported.');
    const settingsLink = page().querySelector<HTMLAnchorElement>('.error .hint a')!;
    expect(settingsLink.textContent).toContain('Languages I know');
    expect(settingsLink.getAttribute('href')).toBe('/settings');
    expect(text()).toContain('pick the language above');
    expect(page().querySelector('textarea')!.value).toBe('Hund'); // kept for the retry
  });

  it('shows the rate-limit message on 429', async () => {
    await render();
    type('hund');
    page().querySelector('textarea')!.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter' }));
    http.expectOne('/api/translate').flush(null, { status: 429, statusText: 'Too Many Requests' });
    await refresh();

    expect(text()).toContain('Too many requests — try again in a minute.');
  });

  it('shows the unavailable state when the AI is not configured', async () => {
    await render('/translate', false);

    expect(text()).toContain('Translate is unavailable');
    expect(page().querySelector('textarea')).toBeNull();
    expect(button('Add a word manually')!.getAttribute('href')).toBe('/words/new');
  });

  it('shows the unavailable state on 503', async () => {
    await render();
    type('hund');
    page().querySelector('textarea')!.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter' }));
    http.expectOne('/api/translate').flush(
      { title: 'AI translation is not configured on this server.' },
      { status: 503, statusText: 'Service Unavailable' },
    );
    await refresh();

    expect(text()).toContain('Translate is unavailable');
  });

  it('shows the kept result again on return, without a new request', async () => {
    await render();
    await translate('hund');
    await click('Add');
    http.expectOne({ url: '/api/words', method: 'POST' }).flush(savedWord);
    await refresh();

    await harness.navigateByUrl('/other');
    await render('/translate');

    http.expectNone('/api/translate');
    expect(text()).toContain('en hund');
    expect(text()).toContain('Added'); // the card keeps its status too
    expect(page().querySelector('textarea')!.value).toBe('hund');
  });

  it('does not translate a ?q= again when its result is already kept', async () => {
    await render('/translate?q=hund');
    http.expectOne('/api/translate').flush(response());
    await refresh();

    await harness.navigateByUrl('/other');
    await render('/translate?q=hund');

    http.expectNone('/api/translate');
    expect(text()).toContain('en hund');
  });

  it('ignores a ?q= that arrives while a translation runs', async () => {
    const component = await render();
    await click('EN'); // no result yet, so this only sets the override
    type('hund').dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter' }));

    // Same page, new query parameter: Angular reuses the component.
    await harness.navigateByUrl('/translate?q=kat');
    await refresh();

    const pending = http.match('/api/translate'); // match() also takes them off the open list
    expect(pending.length).toBe(1);
    expect(pending[0].request.body).toEqual({ text: 'hund', sourceLanguage: 'en' });
    expect(component.draft()).toBe('hund');
    expect(component.language()).toBe('en');
    expect(TestBed.inject(Router).url).toBe('/translate'); // ?q= is still removed
    pending[0].flush(response());
  });

  it('starts clean for another user, without a new request', async () => {
    await render();
    await translate('hund');

    await harness.navigateByUrl('/other');
    TestBed.inject(AuthService).user.set({ id: 'u2', email: 'b@example.test' });
    await render('/translate');

    http.expectNone('/api/translate');
    expect(text()).not.toContain('en hund');
    expect(page().querySelector('textarea')!.value).toBe('');
  });

  it('starts clean after the learning language changed', async () => {
    await render();
    await translate('hund');

    await harness.navigateByUrl('/other');
    TestBed.inject(AuthService).settings.set({
      learningLanguage: 'de',
      knownLanguages: ['en', 'uk'],
      dailyNewLimit: 20,
      maxIntervalDays: 36500,
    });
    await render('/translate');

    http.expectNone('/api/translate');
    expect(text()).not.toContain('en hund');
  });

  it('drops a response that arrives after another user signed in', async () => {
    await render();
    type('hund').dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter' }));
    const request = http.expectOne('/api/translate');

    await harness.navigateByUrl('/other');
    TestBed.inject(AuthService).user.set({ id: 'u2', email: 'b@example.test' });
    await render('/translate');
    request.flush(response());
    await refresh();

    expect(text()).not.toContain('en hund');
    expect(text()).not.toContain('Translating…');
  });

  it('does not offer Add when the settings did not load', async () => {
    TestBed.inject(AuthService).settings.set(null);
    await render();
    await translate('hund');

    expect(button('Add')!.disabled).toBe(true);
    expect(text()).toContain('Your settings did not load');
  });
});
