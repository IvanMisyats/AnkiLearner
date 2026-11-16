import { HttpErrorResponse } from '@angular/common/http';
import { AfterViewInit, Component, ElementRef, OnInit, computed, inject, signal, viewChild } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { LookupApi, SettingsApi, TranslateApi, WordsApi } from '../../core/api.services';
import { Language } from '../../core/api.types';
import { AuthService } from '../../core/auth.service';
import { NotifyService } from '../../core/notify.service';
import { toSaveWordRequest } from './save-request';
import { MAX_TRANSLATE_LENGTH, extractSharedText } from './shared-text';
import { TranslateCardComponent } from './translate-card.component';
import { CardEntry, TranslateStateService } from './translate-state.service';

/** The language toggle's "detect it for me" value; any other value is a language code. */
const AUTO = 'auto';

interface TranslateError {
  message: string;
  /** True for 422 (no supported language detected): suggest choosing one by hand. */
  suggestLanguage: boolean;
}

/**
 * Translate mode (spec §3.8, §7): type a word, phrase or sentence, see its translation and the
 * AI-picked learning-language words, and add them to the dictionary with one tap.
 *
 * This page makes all the API calls; the cards only display. The last result lives in
 * TranslateStateService, so coming back to the page shows it again without a new paid call.
 * `/translate?q=text` translates `text` straight away (links, and text shared from Android apps
 * to the installed app, which arrives as ?q= / ?title= / ?url=).
 */
@Component({
  selector: 'app-translate',
  imports: [
    FormsModule, RouterLink, MatButtonModule, MatButtonToggleModule, MatCardModule,
    MatFormFieldModule, MatIconModule, MatInputModule, MatProgressSpinnerModule,
    TranslateCardComponent,
  ],
  template: `
    <div class="page">
      @if (available() === false) {
        <mat-card appearance="outlined" class="unavailable">
          <mat-card-content>
            <mat-icon class="unavailable-icon">cloud_off</mat-icon>
            <p>AI translation is not set up on this server, so Translate is unavailable.</p>
            <a mat-flat-button routerLink="/words/new">
              <mat-icon>add</mat-icon>
              Add a word manually
            </a>
          </mat-card-content>
        </mat-card>
      } @else {
        <mat-form-field appearance="outline" class="full">
          <mat-label>Word, phrase or sentence</mat-label>
          <!-- #input names the element so the class can reach it through viewChild('input').
               The phone-keyboard hints: "Go" on the Enter key, no autocorrect/capitals. -->
          <textarea
            #input matInput rows="3"
            [maxlength]="maxLength"
            [(ngModel)]="draft"
            (keydown)="onKeydown($event)"
            enterkeyhint="go" spellcheck="false"
            autocapitalize="off" autocomplete="off" autocorrect="off"></textarea>
          @if (draft()) {
            <button matIconSuffix mat-icon-button aria-label="Clear" (click)="clear()">
              <mat-icon>close</mat-icon>
            </button>
          }
          <mat-hint align="end">{{ draft().length }} / {{ maxLength }}</mat-hint>
        </mat-form-field>

        <div class="controls">
          <mat-button-toggle-group
            aria-label="Language of the text"
            hideSingleSelectionIndicator
            [value]="language()"
            [disabled]="state.loading()"
            (change)="changeLanguage($event.value)">
            <mat-button-toggle [value]="auto">Auto</mat-button-toggle>
            @for (code of languageCodes(); track code) {
              <mat-button-toggle [value]="code" [title]="languageName(code)">
                {{ code.toUpperCase() }}
              </mat-button-toggle>
            }
          </mat-button-toggle-group>
          <button
            mat-flat-button class="translate-button"
            [disabled]="draft().trim() === '' || state.loading()"
            (click)="translate()">
            Translate
          </button>
        </div>

        <!-- "; as error" names the checked value for use inside its block. -->
        @if (state.loading()) {
          <div class="loading" role="status">
            <mat-spinner diameter="32" />
            <span>Translating…</span>
          </div>
        } @else if (error(); as error) {
          <div class="error" role="alert">
            <mat-icon>error_outline</mat-icon>
            <div>
              <div>{{ error.message }}</div>
              @if (error.suggestLanguage) {
                <div class="hint">
                  A typo can make a word look foreign — pick the language manually above.
                </div>
              }
            </div>
          </div>
        } @else if (state.result(); as result) {
          <div class="detected">
            @if (state.sourceLanguage()) {
              Translated from {{ languageName(result.sourceLanguage) }}
            } @else {
              Detected: {{ languageName(result.sourceLanguage) }}
            }
          </div>

          @for (line of inputTranslations(); track line.code) {
            <div class="input-translation">
              <span class="lang">{{ line.code.toUpperCase() }}</span>
              <span class="text">{{ line.text }}</span>
            </div>
          }

          @if (!canAdd()) {
            <p class="muted">Your settings did not load, so words can't be added. Reload the page.</p>
          }

          <!-- track tells Angular how to recognise an item between renders (like a key). -->
          @for (entry of state.cards(); track entry) {
            <app-translate-card
              [card]="entry.card"
              [status]="entry.status()"
              [wordId]="entry.wordId()"
              [relearning]="entry.relearning()"
              [knownLanguages]="knownLanguages()"
              [canAdd]="canAdd()"
              (add)="add(entry)"
              (studyAgain)="studyAgain(entry)" />
          }

          @if (state.wholeInput(); as whole) {
            <h2>{{ result.inputKind === 'Sentence' ? 'Whole sentence' : 'Whole phrase' }}</h2>
            <app-translate-card
              [card]="whole.card"
              [status]="whole.status()"
              [wordId]="whole.wordId()"
              [relearning]="whole.relearning()"
              [knownLanguages]="knownLanguages()"
              [canAdd]="canAdd()"
              (add)="add(whole)"
              (studyAgain)="studyAgain(whole)" />
          }

          @if (state.cards().length === 0 && !state.wholeInput()) {
            <p class="muted">No words to add from this text.</p>
          }
        }
      }
    </div>
  `,
  styles: `
    .page { padding: 16px; max-width: 640px; margin: 0 auto; }
    .full { width: 100%; }
    .controls { display: flex; flex-wrap: wrap; align-items: center; gap: 8px; margin-bottom: 16px; }
    .translate-button { margin-left: auto; }
    .loading { display: flex; align-items: center; gap: 12px; padding: 24px 0; }
    .error {
      display: flex; gap: 8px; padding: 12px; border-radius: 8px;
      background: var(--mat-sys-error-container); color: var(--mat-sys-on-error-container);
    }
    .hint { margin-top: 4px; }
    .detected, .muted, .lang { color: var(--mat-sys-on-surface-variant); }
    .detected { font-size: 13px; margin-bottom: 4px; }
    .input-translation { display: flex; gap: 8px; font-size: 18px; line-height: 1.4; margin-bottom: 4px; }
    /* Lets a long unbroken word wrap instead of widening the page. */
    .text { min-width: 0; overflow-wrap: anywhere; }
    .lang { flex: none; min-width: 24px; padding-top: 4px; font-size: 11px; }
    app-translate-card { display: block; margin-top: 12px; }
    h2 { font-size: 16px; font-weight: 500; margin: 24px 0 0; }
    .unavailable { text-align: center; padding: 16px; }
    /* Only the big cloud icon — a plain ".unavailable mat-icon" also hit the button's "+" icon. */
    .unavailable-icon { font-size: 40px; width: 40px; height: 40px; color: var(--mat-sys-on-surface-variant); }
  `,
})
export class TranslateComponent implements OnInit, AfterViewInit {
  private readonly translateApi = inject(TranslateApi);
  private readonly wordsApi = inject(WordsApi);
  private readonly lookupApi = inject(LookupApi);
  private readonly settingsApi = inject(SettingsApi);
  private readonly auth = inject(AuthService);
  private readonly notify = inject(NotifyService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  /** Outlives this component: the last text, override and result (see the service). */
  readonly state = inject(TranslateStateService);

  readonly auto = AUTO;
  readonly maxLength = MAX_TRANSLATE_LENGTH;

  /** null until /api/lookup/status answers; the form shows meanwhile. */
  readonly available = signal<boolean | null>(null);
  /** The textarea's text; [(ngModel)] keeps the two in sync. */
  readonly draft = signal('');
  /** The language toggle: AUTO or a language code. */
  readonly language = signal(AUTO);
  readonly error = signal<TranslateError | null>(null);
  private readonly languages = signal<Language[]>([]);

  /** The textarea element, for focus/blur. */
  private readonly input = viewChild<ElementRef<HTMLTextAreaElement>>('input');

  // computed() is a read-only signal derived from others; it recalculates when they change.
  readonly knownLanguages = computed(() => this.auth.settings()?.knownLanguages ?? []);
  /** Without the user's languages (settings failed to load) a word would be saved untranslated. */
  readonly canAdd = computed(() => this.knownLanguages().length > 0);
  /** The toggle's languages after Auto: the learning language, then the known ones. */
  readonly languageCodes = computed(() => {
    const settings = this.auth.settings();
    return settings ? [settings.learningLanguage, ...settings.knownLanguages] : [];
  });

  /** The input in every other language, learning language first; empty ones skipped. */
  readonly inputTranslations = computed(() => {
    const result = this.state.result();
    if (!result) return [];
    return this.languageCodes()
      .filter((code) => code !== result.sourceLanguage)
      .map((code) => ({ code, text: (result.inputTranslations[code] ?? '').trim() }))
      .filter((line) => line.text !== '');
  });

  constructor() {
    // The kept state belongs to whoever translated: another user signed in on this tab, or
    // changed languages, gets a clean page. Otherwise show the kept text and language again.
    if (this.state.owner !== this.ownerKey()) this.state.reset();
    this.draft.set(this.state.text());
    this.language.set(this.state.sourceLanguage() ?? AUTO);
  }

  ngOnInit(): void {
    firstValueFrom(this.lookupApi.status())
      .then((status) => this.available.set(status.available))
      .catch(() => {});
    firstValueFrom(this.settingsApi.languages())
      .then((languages) => this.languages.set(languages))
      .catch(() => {});

    // Subscribe rather than read once: when the page is already open and a new ?q= arrives,
    // Angular reuses this component and only the query parameters change.
    // Android's share sheet (the share_target in manifest.webmanifest) sends the shared text
    // as ?q= and may add ?title= and ?url=; get() returns null for a missing parameter.
    this.route.queryParamMap.subscribe((params) => {
      const q = params.get('q');
      const title = params.get('title');
      const url = params.get('url');
      if (q !== null || title !== null || url !== null) this.translateShared(q, title, url);
    });
  }

  ngAfterViewInit(): void {
    // Ready to type on arrival — unless a result is (about to be) on screen: on a phone the
    // keyboard would cover it.
    if (!this.state.result() && !this.state.loading()) this.input()?.nativeElement.focus();
  }

  async translate(): Promise<void> {
    const text = this.draft().trim();
    if (text === '' || this.state.loading()) return;
    const sourceLanguage = this.language() === AUTO ? null : this.language();

    const owner = this.ownerKey();
    this.error.set(null);
    this.state.start(owner, text, sourceLanguage);
    try {
      const response = await firstValueFrom(this.translateApi.translate(text, sourceLanguage));
      this.state.finish(owner, response);
    } catch (error) {
      this.state.fail(owner);
      this.showError(error);
    }
  }

  /**
   * Enter translates; Shift+Enter inserts a line break as usual. While an input method is
   * composing (e.g. picking a Japanese candidate), Enter belongs to it, not to us.
   */
  onKeydown(event: KeyboardEvent): void {
    if (event.key !== 'Enter' || event.shiftKey || event.isComposing) return;
    event.preventDefault(); // no line break
    if (this.draft().trim() === '') return;
    this.input()?.nativeElement.blur(); // closes the phone keyboard so the result is visible
    this.translate();
  }

  /** Picking a language while a result or an error is shown translates again with it. */
  changeLanguage(code: string): void {
    this.language.set(code);
    if (this.state.result() || this.error()) this.translate();
  }

  clear(): void {
    this.draft.set('');
    this.input()?.nativeElement.focus();
  }

  /** "Add" and "Add anyway": saves the card as a new dictionary word (tag "translated"). */
  async add(entry: CardEntry): Promise<void> {
    const previous = entry.status();
    if (previous === 'adding' || !this.canAdd()) return;
    entry.status.set('adding');
    try {
      const request = toSaveWordRequest(entry.card, this.knownLanguages());
      const word = await firstValueFrom(this.wordsApi.create(request));
      entry.wordId.set(word.id);
      entry.status.set('added');
      this.offerEdit(word.id);
    } catch (error) {
      entry.status.set(previous);
      this.notify.httpError(error, 'Could not add the word.');
    }
  }

  /** "Study again": the existing word becomes due now in both directions (spec FR-D8). */
  async studyAgain(entry: CardEntry): Promise<void> {
    const id = entry.wordId();
    // The disabled button alone is not enough: a fast double tap can arrive before it renders.
    if (!id || entry.relearning()) return;
    entry.relearning.set(true);
    try {
      await firstValueFrom(this.wordsApi.relearn(id));
      this.notify.success('It will come up in your next study session.');
    } catch (error) {
      this.notify.httpError(error);
    } finally {
      entry.relearning.set(false);
    }
  }

  languageName(code: string): string {
    return this.languages().find((l) => l.code === code)?.name ?? code.toUpperCase();
  }

  /**
   * Runs a translation handed over in the URL: a ?q= link, or text shared from another app.
   * The first parameter that still has text after cleaning wins (q, then title, then url).
   * Android usually puts the selected text in ?q=, in quotes and followed by a link to its
   * source page; extractSharedText drops both.
   */
  private translateShared(q: string | null, title: string | null, url: string | null): void {
    // Drop the parameters from the address bar, replacing the history entry, so that Back or a
    // reload doesn't run the paid translation again. null removes a parameter.
    this.router.navigate([], {
      relativeTo: this.route,
      queryParams: { q: null, title: null, url: null },
      queryParamsHandling: 'merge',
      replaceUrl: true,
    });

    const text = extractSharedText(q, title, url);
    if (text === '') return;
    // One translation at a time: while one runs, the new text is dropped, and the box and the
    // toggle keep showing what is being translated.
    if (this.state.loading()) return;
    // The kept result is for this very text (e.g. the same link opened again): show it.
    if (text === this.state.text() && this.state.result()) return;

    this.draft.set(text);
    this.language.set(AUTO);
    this.translate();
  }

  /** Whose translation this is: the user and their languages, e.g. "u1|da|en,uk". */
  private ownerKey(): string {
    const settings = this.auth.settings();
    return [
      this.auth.user()?.id ?? '',
      settings?.learningLanguage ?? '',
      (settings?.knownLanguages ?? []).join(','),
    ].join('|');
  }

  /** The "Added" toast with an Edit button. Not awaited: nothing waits for the toast. */
  private offerEdit(wordId: string): void {
    this.notify.successWithAction('Added', 'Edit').then((editClicked) => {
      if (editClicked) this.router.navigate(['/words', wordId, 'edit']);
    });
  }

  private showError(error: unknown): void {
    const status = error instanceof HttpErrorResponse ? error.status : 0;
    if (status === 503) {
      this.available.set(false); // AI not configured: show the manual-add alternative
      return;
    }
    this.error.set({
      message: NotifyService.messageFor(error, 'Translation failed — please try again.'),
      suggestLanguage: status === 422,
    });
  }
}
