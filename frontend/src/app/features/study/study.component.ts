import { Component, HostListener, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { StudyApi, WordsApi } from '../../core/api.services';
import { ExerciseType, ReviewGrade, StudyCardDto } from '../../core/api.types';
import { AuthService } from '../../core/auth.service';
import { NotifyService } from '../../core/notify.service';

/**
 * AnkiDroid-style review loop (spec §3.6): prompt → "Show answer" → grade with
 * Again/Hard/Good/Easy. Keyboard: Space reveals, 1–4 grade. The revealed answer
 * offers an inline editor to correct the word without leaving the session (FR-R5).
 */
@Component({
  selector: 'app-study',
  imports: [
    FormsModule, RouterLink, MatCardModule, MatButtonModule, MatIconModule,
    MatFormFieldModule, MatInputModule, MatProgressSpinnerModule,
  ],
  template: `
    <div class="page">
      @if (loading()) {
        <div class="center"><mat-spinner diameter="36" /></div>
      } @else if (!card()) {
        <mat-card appearance="outlined" class="done">
          <mat-card-content>
            <mat-icon class="done-icon">celebration</mat-icon>
            <h2>All done for now!</h2>
            <p>No cards are waiting in this direction.</p>
            <a mat-flat-button routerLink="/">Back to dashboard</a>
          </mat-card-content>
        </mat-card>
      } @else {
        <div class="remaining">{{ remaining() }} left @if (card()!.isNew) { <span class="new-badge">new</span> }</div>

        <mat-card appearance="outlined" class="card">
          <mat-card-content>
            <div class="prompt" [innerHTML]="card()!.prompt"></div>

            @if (revealed()) {
              <hr />
              <div class="answer" [innerHTML]="card()!.answer"></div>
              @if (card()!.word.transcription) {
                <div class="extra transcription">{{ card()!.word.transcription }}</div>
              }
              @if (card()!.word.example) {
                <div class="extra example" [innerHTML]="card()!.word.example"></div>
                @for (translation of exampleTranslations(); track translation.languageCode) {
                  <div class="extra example-translation">
                    @if (exampleTranslations().length > 1) {
                      <span class="lang">{{ translation.languageCode.toUpperCase() }}</span>
                    }
                    <span [innerHTML]="translation.text"></span>
                  </div>
                }
              }

              @if (editing()) {
                <div class="edit-panel">
                  <mat-form-field appearance="outline" class="full">
                    <mat-label>Word (HTML)</mat-label>
                    <textarea matInput [(ngModel)]="editTerm" rows="2"></textarea>
                  </mat-form-field>
                  <mat-form-field appearance="outline" class="full">
                    <mat-label>Transcription</mat-label>
                    <input matInput [(ngModel)]="editTranscription" />
                  </mat-form-field>
                  @for (translation of editTranslations; track translation.languageCode) {
                    <mat-form-field appearance="outline" class="full">
                      <mat-label>Translation — {{ translation.languageCode.toUpperCase() }}</mat-label>
                      <textarea matInput [(ngModel)]="translation.text" rows="2"></textarea>
                    </mat-form-field>
                  }
                  <div class="edit-buttons">
                    <button mat-flat-button (click)="saveCorrection()" [disabled]="savingCorrection()">Save</button>
                    <button mat-button (click)="editing.set(false)">Cancel</button>
                  </div>
                </div>
              } @else {
                <button mat-button class="correct-button" (click)="startEditing()">
                  <mat-icon>edit</mat-icon>
                  Correct this word
                </button>
              }
            }
          </mat-card-content>
        </mat-card>

        @if (!revealed()) {
          <button mat-flat-button class="reveal-button" (click)="reveal()">
            Show answer <span class="key-hint">Space</span>
          </button>
        } @else if (!editing()) {
          <div class="grades">
            <button mat-flat-button class="grade again" (click)="grade('Again')">
              <span class="grade-name">Again <span class="key-hint">1</span></span>
              <span class="interval">{{ card()!.intervals.again }}</span>
            </button>
            <button mat-flat-button class="grade hard" (click)="grade('Hard')">
              <span class="grade-name">Hard <span class="key-hint">2</span></span>
              <span class="interval">{{ card()!.intervals.hard }}</span>
            </button>
            <button mat-flat-button class="grade good" (click)="grade('Good')">
              <span class="grade-name">Good <span class="key-hint">3</span></span>
              <span class="interval">{{ card()!.intervals.good }}</span>
            </button>
            <button mat-flat-button class="grade easy" (click)="grade('Easy')">
              <span class="grade-name">Easy <span class="key-hint">4</span></span>
              <span class="interval">{{ card()!.intervals.easy }}</span>
            </button>
          </div>
        }
      }
    </div>
  `,
  styles: `
    .page {
      padding: 16px; max-width: 640px; margin: 0 auto;
      display: flex; flex-direction: column; min-height: calc(100% - 32px);
    }
    .center { display: flex; justify-content: center; padding: 48px; }
    .done { text-align: center; padding: 24px; }
    .done-icon { font-size: 48px; width: 48px; height: 48px; color: var(--mat-sys-primary); }

    .remaining { color: var(--mat-sys-on-surface-variant); font-size: 14px; margin-bottom: 8px; }
    .new-badge {
      background: var(--mat-sys-tertiary-container); color: var(--mat-sys-on-tertiary-container);
      border-radius: 10px; padding: 1px 8px; font-size: 12px;
    }

    .card { flex: 1; }
    .prompt { font-size: 24px; line-height: 1.4; padding: 8px 0; }
    .answer { font-size: 20px; line-height: 1.4; padding: 8px 0; }
    hr { border: none; border-top: 1px solid var(--mat-sys-outline-variant); margin: 8px 0; }
    .extra { color: var(--mat-sys-on-surface-variant); margin-top: 4px; }
    .example { font-style: italic; }
    /* Subordinate to the target-language sentence it translates. */
    .example-translation { font-style: italic; opacity: 0.75; margin-top: 2px; }
    .example-translation .lang {
      font-size: 11px; margin-right: 6px; vertical-align: middle; font-style: normal;
    }

    .correct-button { margin-top: 12px; }
    .edit-panel { margin-top: 12px; }
    .full { width: 100%; }
    .edit-buttons { display: flex; gap: 8px; }

    .reveal-button { margin-top: 16px; height: 56px; font-size: 16px; }

    /* Four large tap targets in a row — the core mobile interaction (spec §7). */
    .grades { display: grid; grid-template-columns: repeat(4, 1fr); gap: 8px; margin-top: 16px; }
    .grade {
      height: 64px; display: flex; flex-direction: column; gap: 2px;
      --mat-button-filled-label-text-size: 14px;
    }
    .grade .interval { font-size: 11px; opacity: 0.8; }
    .grade.again { background: #b3261e; color: white; }
    .grade.hard { background: #7d5700; color: white; }
    .grade.good { background: #2e6b27; color: white; }
    .grade.easy { background: #00639b; color: white; }

    .key-hint {
      opacity: 0.6; font-size: 11px; border: 1px solid currentColor;
      border-radius: 3px; padding: 0 4px; margin-left: 4px;
    }
    @media (pointer: coarse) {
      .key-hint { display: none; } /* keyboard hints are noise on touch screens */
    }
  `,
})
export class StudyComponent implements OnInit {
  private readonly studyApi = inject(StudyApi);
  private readonly wordsApi = inject(WordsApi);
  private readonly auth = inject(AuthService);
  private readonly notify = inject(NotifyService);
  private readonly route = inject(ActivatedRoute);

  readonly card = signal<StudyCardDto | null>(null);
  readonly remaining = signal(0);
  readonly revealed = signal(false);
  readonly loading = signal(true);
  readonly editing = signal(false);
  readonly savingCorrection = signal(false);

  /**
   * Translations of the example sentence, in the user's known-language order — shown with the
   * answer, under the target-language example. flatMap is used as a filter+map in one pass:
   * returning [] drops a language that has no example translation.
   */
  readonly exampleTranslations = computed(() => {
    const known = this.auth.settings()?.knownLanguages ?? [];
    const translations = this.card()?.word.translations ?? [];
    return known.flatMap((languageCode) => {
      const text = translations.find((t) => t.languageCode === languageCode)?.exampleTranslation?.trim();
      return text ? [{ languageCode, text }] : [];
    });
  });

  private exercise: ExerciseType = 'TargetToKnown';
  private tag: string | undefined;
  private grading = false;

  // Inline-correction model (plain fields — a full reactive form is overkill here).
  editTerm = '';
  editTranscription = '';
  editTranslations: { languageCode: string; text: string }[] = [];

  ngOnInit(): void {
    this.exercise = (this.route.snapshot.paramMap.get('exercise') as ExerciseType) ?? 'TargetToKnown';
    this.tag = this.route.snapshot.queryParamMap.get('tag') ?? undefined;
    this.loadNext();
  }

  @HostListener('window:keydown', ['$event'])
  onKey(event: KeyboardEvent): void {
    // Don't steal keys while the user types in the inline editor.
    const target = event.target as HTMLElement;
    if (this.editing() || target.tagName === 'INPUT' || target.tagName === 'TEXTAREA') return;
    if (!this.card()) return;

    if (event.code === 'Space' && !this.revealed()) {
      event.preventDefault();
      this.reveal();
    } else if (this.revealed()) {
      const grades: Record<string, ReviewGrade> = { '1': 'Again', '2': 'Hard', '3': 'Good', '4': 'Easy' };
      const grade = grades[event.key];
      if (grade) {
        event.preventDefault();
        this.grade(grade);
      }
    }
  }

  reveal(): void {
    this.revealed.set(true);
  }

  async grade(grade: ReviewGrade): Promise<void> {
    const current = this.card();
    if (!current || this.grading) return;
    this.grading = true;
    try {
      const next = await firstValueFrom(
        this.studyApi.grade(current.word.id, this.exercise, grade, this.tag),
      );
      this.applyNext(next.card, next.remaining);
    } catch (error) {
      this.notify.httpError(error, 'Could not save the grade.');
    } finally {
      this.grading = false;
    }
  }

  startEditing(): void {
    const word = this.card()!.word;
    this.editTerm = word.term;
    this.editTranscription = word.transcription ?? '';
    // Only currently-known languages are editable (and sendable — the API rejects
    // other codes; hidden-language translations are preserved server-side, FR-S6).
    const known = this.auth.settings()?.knownLanguages ?? [];
    this.editTranslations = word.translations
      .filter((t) => known.includes(t.languageCode))
      .map((t) => ({ languageCode: t.languageCode, text: t.text }));
    this.editing.set(true);
  }

  /** Saves the correction and re-fetches the (still due) card with fresh content. */
  async saveCorrection(): Promise<void> {
    const word = this.card()!.word;
    this.savingCorrection.set(true);
    try {
      await firstValueFrom(this.wordsApi.update(word.id, {
        term: this.editTerm,
        transcription: this.editTranscription || null,
        partOfSpeech: word.partOfSpeech,
        gender: word.gender,
        example: word.example,
        notes: word.notes,
        translations: this.editTranslations
          .filter((e) => e.text.trim().length > 0)
          .map((e) => ({
            languageCode: e.languageCode,
            text: e.text,
            exampleTranslation: word.translations
              .find((t) => t.languageCode === e.languageCode)?.exampleTranslation ?? null,
          })),
        tags: word.tags,
      }));
      this.notify.success('Word corrected.');
      this.editing.set(false);
      const next = await firstValueFrom(this.studyApi.next(this.exercise, this.tag));
      this.applyNext(next.card, next.remaining, /* keepRevealed */ true);
    } catch (error) {
      this.notify.httpError(error, 'Could not save the correction.');
    } finally {
      this.savingCorrection.set(false);
    }
  }

  private async loadNext(): Promise<void> {
    this.loading.set(true);
    try {
      const next = await firstValueFrom(this.studyApi.next(this.exercise, this.tag));
      this.applyNext(next.card, next.remaining);
    } catch (error) {
      this.notify.httpError(error);
    } finally {
      this.loading.set(false);
    }
  }

  private applyNext(card: StudyCardDto | null, remaining: number, keepRevealed = false): void {
    this.card.set(card);
    this.remaining.set(remaining);
    this.revealed.set(keepRevealed && card !== null);
    this.editing.set(false);
  }
}
