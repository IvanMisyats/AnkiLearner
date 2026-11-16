import { Component, computed, input, output } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { RouterLink } from '@angular/router';
import { TranslateCardDto } from '../../core/api.types';
import { CardStatus } from './translate-state.service';

/** One labelled line, e.g. { code: 'en', text: 'dog; hound' }. */
interface LanguageLine {
  code: string;
  text: string;
}

/**
 * Shows one suggested word from a translation and the buttons for its current status.
 * Display only: it makes no API calls — it raises `add` / `studyAgain` and the page acts.
 *
 * All card text comes from the AI, so it is rendered with {{ }} interpolation only (which
 * always shows text literally), never with [innerHTML].
 */
@Component({
  selector: 'app-translate-card',
  imports: [RouterLink, MatCardModule, MatButtonModule, MatIconModule, MatProgressSpinnerModule],
  template: `
    <mat-card appearance="outlined">
      <mat-card-content>
        <div class="term">
          {{ card().term }}
          @if (showGender()) {
            <span class="gender">({{ card().gender }})</span>
          }
        </div>
        @if (details()) {
          <div class="details">{{ details() }}</div>
        }

        @for (line of meaningLines(); track line.code) {
          <div class="line">
            <span class="lang">{{ line.code.toUpperCase() }}</span>
            <span class="text">{{ line.text }}</span>
          </div>
        }

        @if (card().example) {
          <div class="example">{{ card().example }}</div>
          @for (line of exampleLines(); track line.code) {
            <div class="line example-translation">
              <span class="lang">{{ line.code.toUpperCase() }}</span>
              <span class="text">{{ line.text }}</span>
            </div>
          }
        }

        @if (card().note) {
          <div class="note">{{ card().note }}</div>
        }

        <!-- @switch works like a C# switch statement over the status value. -->
        <div class="actions">
          @switch (status()) {
            @case ('new') {
              <button mat-flat-button [disabled]="!canAdd()" (click)="add.emit()">
                <mat-icon>add</mat-icon>
                Add
              </button>
            }
            @case ('adding') {
              <button mat-flat-button disabled>
                <mat-spinner diameter="18" />
                Adding…
              </button>
            }
            @case ('added') {
              <span class="added"><mat-icon>check_circle</mat-icon> Added</span>
              <a mat-button [routerLink]="['/words', wordId(), 'edit']">
                <mat-icon>edit</mat-icon>
                Edit
              </a>
            }
            @case ('existing') {
              <a class="in-dictionary" [routerLink]="['/words', wordId(), 'edit']">
                <mat-icon>bookmark</mat-icon> In your dictionary
              </a>
              <div class="buttons">
                <button mat-flat-button [disabled]="relearning()" (click)="studyAgain.emit()">
                  <mat-icon>replay</mat-icon>
                  Study again
                </button>
                <button mat-button [disabled]="!canAdd()" (click)="add.emit()">Add anyway</button>
              </div>
            }
          }
        </div>
      </mat-card-content>
    </mat-card>
  `,
  styles: `
    .term { font-size: 22px; font-weight: 500; line-height: 1.3; overflow-wrap: anywhere; }
    .gender, .details, .note { color: var(--mat-sys-on-surface-variant); }
    .gender { font-size: 16px; font-weight: 400; }
    .details { margin: 2px 0 8px; }
    .line { display: flex; gap: 8px; margin-top: 4px; line-height: 1.4; }
    /* Lets a long unbroken word wrap instead of widening the card. */
    .text, .example, .note { min-width: 0; overflow-wrap: anywhere; }
    .lang {
      flex: none; min-width: 24px; padding-top: 2px;
      font-size: 11px; color: var(--mat-sys-on-surface-variant);
    }
    .example { font-style: italic; margin-top: 12px; }
    .example-translation { font-style: italic; opacity: 0.75; }
    .note { margin-top: 12px; font-size: 13px; }
    .actions {
      display: flex; flex-wrap: wrap; align-items: center; gap: 8px; margin-top: 16px;
    }
    .added, .in-dictionary { display: inline-flex; align-items: center; gap: 4px; font-weight: 500; }
    .added { color: var(--mat-sys-primary); }
    .in-dictionary { width: 100%; min-height: 32px; color: var(--mat-sys-tertiary); }
    .buttons { display: flex; flex-wrap: wrap; gap: 8px; }
  `,
})
export class TranslateCardComponent {
  // input() declares a property the parent sets in its template — [card]="..." — and
  // output() an event the parent handles — (add)="...". Think of them as a C# property
  // and a C# event. Inputs are signals, so they are read with () like card().
  readonly card = input.required<TranslateCardDto>();
  readonly status = input<CardStatus>('new');
  /** The dictionary word to link to: the existing match, or the word just added. */
  readonly wordId = input<string | null>(null);
  readonly relearning = input(false);
  /** The user's known languages, in their configured order. */
  readonly knownLanguages = input<string[]>([]);
  /** False disables Add / Add anyway (the page shows why). */
  readonly canAdd = input(true);

  readonly add = output<void>();
  readonly studyAgain = output<void>();

  /** The gender is shown unless the term already starts with it ("en hund" + "en"). */
  readonly showGender = computed(() => {
    const gender = (this.card().gender ?? '').trim().toLowerCase();
    return gender !== '' && !this.card().term.toLowerCase().startsWith(gender + ' ');
  });

  /** "[ˈhunˀ] · noun" */
  readonly details = computed(() =>
    [this.card().transcription, this.card().partOfSpeech]
      .map((part) => (part ?? '').trim())
      .filter((part) => part !== '')
      .join(' · '),
  );

  readonly meaningLines = computed(() =>
    this.lines((code) =>
      (this.card().meanings[code] ?? [])
        .map((meaning) => meaning.trim())
        .filter((meaning) => meaning !== '')
        .join('; '),
    ),
  );

  readonly exampleLines = computed(() =>
    this.lines((code) => (this.card().exampleTranslations[code] ?? '').trim()),
  );

  /**
   * One line per known language that has text, in the user's order.
   * `textFor` is a function parameter (C#: Func<string, string>).
   */
  private lines(textFor: (code: string) => string): LanguageLine[] {
    return this.knownLanguages()
      .map((code) => ({ code, text: textFor(code) }))
      .filter((line) => line.text !== '');
  }
}
