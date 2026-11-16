import { Component, OnInit, inject, signal } from '@angular/core';
import { FormArray, FormBuilder, FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatChipInputEvent, MatChipsModule } from '@angular/material/chips';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { LookupApi, TagsApi, WordsApi } from '../../core/api.services';
import { SaveWordRequest, WordLookupResult } from '../../core/api.types';
import { AuthService } from '../../core/auth.service';
import { ConfirmService } from '../../core/confirm.service';
import { NotifyService } from '../../core/notify.service';
import { escapeHtml, meaningsToHtml } from '../../core/word-html';

/**
 * Add/edit form for a dictionary word. One translation block per known language.
 * "Look up" pre-fills every field via the AI provider; everything stays editable
 * and nothing is saved until the user presses Save (spec FR-W3/FR-W4).
 */
@Component({
  selector: 'app-word-form',
  imports: [
    ReactiveFormsModule, RouterLink, MatCardModule, MatFormFieldModule, MatInputModule,
    MatButtonModule, MatIconModule, MatChipsModule, MatProgressSpinnerModule,
  ],
  template: `
    <div class="page">
      <h1>{{ wordId ? 'Edit word' : 'Add word' }}</h1>

      <form [formGroup]="form" (ngSubmit)="save()">
        <div class="term-row">
          <mat-form-field appearance="outline" class="grow">
            <mat-label>Word or phrase ({{ learningLanguage.toUpperCase() }}, HTML allowed)</mat-label>
            <textarea matInput formControlName="term" rows="2" (blur)="checkDuplicate()"></textarea>
          </mat-form-field>
          @if (lookupAvailable()) {
            <button
              mat-flat-button type="button" class="lookup-button"
              [disabled]="!form.controls.term.value.trim() || lookingUp()"
              (click)="lookup()">
              @if (lookingUp()) {
                <mat-spinner diameter="20" />
              } @else {
                <mat-icon>auto_awesome</mat-icon>
              }
              Look up
            </button>
          }
        </div>

        @if (duplicateId()) {
          <div class="duplicate-warning">
            <mat-icon>warning</mat-icon>
            <span>
              This word already exists.
              <a [routerLink]="['/words', duplicateId(), 'edit']">Open the existing entry</a>
              — or save anyway.
            </span>
          </div>
        }

        <div class="two-columns">
          <mat-form-field appearance="outline">
            <mat-label>Transcription (IPA)</mat-label>
            <input matInput formControlName="transcription" placeholder="[ˈhunˀ]" />
          </mat-form-field>
          <mat-form-field appearance="outline">
            <mat-label>Part of speech</mat-label>
            <input matInput formControlName="partOfSpeech" placeholder="noun" />
          </mat-form-field>
          <mat-form-field appearance="outline">
            <mat-label>Gender / article</mat-label>
            <input matInput formControlName="gender" placeholder="en / et" />
          </mat-form-field>
        </div>

        <div formArrayName="translations">
          @for (group of translations.controls; track group; let i = $index) {
            <mat-card appearance="outlined" class="translation-card" [formGroupName]="i">
              <mat-card-content>
                <div class="translation-lang">{{ languageLabel(group.controls.languageCode.value) }}</div>
                <mat-form-field appearance="outline" class="full">
                  <mat-label>Translation (HTML allowed)</mat-label>
                  <textarea matInput formControlName="text" rows="2"></textarea>
                </mat-form-field>
                <mat-form-field appearance="outline" class="full">
                  <mat-label>Example translation</mat-label>
                  <textarea matInput formControlName="exampleTranslation" rows="1"></textarea>
                </mat-form-field>
              </mat-card-content>
            </mat-card>
          }
        </div>

        <mat-form-field appearance="outline" class="full">
          <mat-label>Example sentence ({{ learningLanguage.toUpperCase() }})</mat-label>
          <textarea matInput formControlName="example" rows="2"></textarea>
        </mat-form-field>

        <mat-form-field appearance="outline" class="full">
          <mat-label>Notes</mat-label>
          <textarea matInput formControlName="notes" rows="2"></textarea>
        </mat-form-field>

        <mat-form-field appearance="outline" class="full">
          <mat-label>Tags</mat-label>
          <mat-chip-grid #chipGrid aria-label="Tags">
            @for (tag of tagNames(); track tag) {
              <mat-chip-row (removed)="removeTag(tag)">
                {{ tag }}
                <button matChipRemove aria-label="Remove tag"><mat-icon>cancel</mat-icon></button>
              </mat-chip-row>
            }
          </mat-chip-grid>
          <input
            [matChipInputFor]="chipGrid"
            (matChipInputTokenEnd)="addTag($event)"
            placeholder="Add tag…"
            [attr.list]="'known-tags'" />
          <datalist id="known-tags">
            @for (tag of allTags(); track tag) {
              <option [value]="tag"></option>
            }
          </datalist>
        </mat-form-field>

        <div class="buttons">
          <button mat-flat-button type="submit" [disabled]="form.invalid || saving()">
            @if (saving()) {
              <mat-spinner diameter="20" />
            } @else {
              Save
            }
          </button>
          <a mat-button routerLink="/words">Cancel</a>
          @if (wordId) {
            <span class="progress-buttons">
              <!-- type="button" matters: inside a <form> a bare <button> submits it. -->
              <button
                mat-button type="button"
                [disabled]="relearning()" (click)="studyAgain()">
                <mat-icon>replay</mat-icon>
                Study again
              </button>
              <button
                mat-button type="button" class="reset-button"
                [disabled]="resetting()" (click)="resetProgress()">
                <mat-icon>restart_alt</mat-icon>
                Reset progress
              </button>
            </span>
          }
        </div>
      </form>
    </div>
  `,
  styles: `
    .page { padding: 16px; max-width: 700px; margin: 0 auto; }
    h1 { font-size: 22px; font-weight: 500; }
    form { display: flex; flex-direction: column; }
    .term-row { display: flex; gap: 12px; align-items: flex-start; }
    .grow { flex: 1; }
    .lookup-button { margin-top: 8px; height: 44px; }
    .duplicate-warning {
      display: flex; gap: 8px; align-items: center;
      padding: 8px 12px; margin-bottom: 12px; border-radius: 8px;
      background: var(--mat-sys-tertiary-container);
      color: var(--mat-sys-on-tertiary-container);
    }
    .two-columns { display: grid; grid-template-columns: repeat(auto-fit, minmax(160px, 1fr)); gap: 0 12px; }
    .translation-card { margin-bottom: 12px; }
    .translation-lang { font-weight: 500; margin-bottom: 8px; }
    .full { width: 100%; }
    .buttons { display: flex; flex-wrap: wrap; gap: 12px; margin-top: 8px; align-items: center; }
    /* Rare progress actions sit apart from Save/Cancel (on a phone they wrap to their own line). */
    .progress-buttons { margin-left: auto; display: flex; flex-wrap: wrap; gap: 4px; }
    .reset-button { color: var(--mat-sys-error); }
  `,
})
export class WordFormComponent implements OnInit {
  private readonly wordsApi = inject(WordsApi);
  private readonly tagsApi = inject(TagsApi);
  private readonly lookupApi = inject(LookupApi);
  private readonly auth = inject(AuthService);
  private readonly notify = inject(NotifyService);
  private readonly confirm = inject(ConfirmService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly formBuilder = inject(FormBuilder);

  readonly lookupAvailable = signal(false);
  readonly lookingUp = signal(false);
  readonly saving = signal(false);
  readonly resetting = signal(false);
  readonly relearning = signal(false);
  readonly duplicateId = signal<string | null>(null);
  readonly tagNames = signal<string[]>([]);
  readonly allTags = signal<string[]>([]);

  wordId: string | null = null;
  readonly learningLanguage = this.auth.settings()?.learningLanguage ?? '';
  private readonly knownLanguages = this.auth.settings()?.knownLanguages ?? [];

  readonly form = this.formBuilder.nonNullable.group({
    term: ['', Validators.required],
    transcription: [''],
    partOfSpeech: [''],
    gender: [''],
    example: [''],
    notes: [''],
    // One group per known language, in the user's configured order.
    translations: this.formBuilder.array(
      this.knownLanguages.map((code) => this.translationGroup(code)),
    ),
  });

  get translations(): FormArray<TranslationGroup> {
    return this.form.controls.translations;
  }

  ngOnInit(): void {
    firstValueFrom(this.lookupApi.status())
      .then((status) => this.lookupAvailable.set(status.available))
      .catch(() => {});
    firstValueFrom(this.tagsApi.list())
      .then((tags) => this.allTags.set(tags.map((t) => t.name)))
      .catch(() => {});

    // Subscribe (not snapshot): Angular reuses this component when only the :id
    // route parameter changes (e.g. following the duplicate-warning link while editing).
    this.route.paramMap.subscribe((params) => {
      this.wordId = params.get('id');
      this.form.reset();
      this.tagNames.set([]);
      this.duplicateId.set(null);
      if (this.wordId) this.loadWord(this.wordId);
    });
  }

  private async loadWord(id: string): Promise<void> {
    try {
      const word = await firstValueFrom(this.wordsApi.get(id));
      this.form.patchValue({
        term: word.term,
        transcription: word.transcription ?? '',
        partOfSpeech: word.partOfSpeech ?? '',
        gender: word.gender ?? '',
        example: word.example ?? '',
        notes: word.notes ?? '',
      });
      for (const group of this.translations.controls) {
        const existing = word.translations.find(
          (t) => t.languageCode === group.controls.languageCode.value,
        );
        if (existing) {
          group.patchValue({
            text: existing.text,
            exampleTranslation: existing.exampleTranslation ?? '',
          });
        }
      }
      this.tagNames.set(word.tags);
    } catch (error) {
      this.notify.httpError(error, 'Could not load the word.');
      this.router.navigate(['/words']);
    }
  }

  async lookup(): Promise<void> {
    this.lookingUp.set(true);
    try {
      const result = await firstValueFrom(this.lookupApi.lookup(this.form.controls.term.value));
      this.applyLookup(result);
    } catch (error) {
      this.notify.httpError(error, 'Lookup failed — you can fill the form manually.');
    } finally {
      this.lookingUp.set(false);
    }
  }

  async checkDuplicate(): Promise<void> {
    const term = this.form.controls.term.value.trim();
    if (!term) {
      this.duplicateId.set(null);
      return;
    }
    try {
      const result = await firstValueFrom(
        this.wordsApi.checkDuplicate(term, this.wordId ?? undefined),
      );
      this.duplicateId.set(result.exists ? result.wordId : null);
    } catch {
      this.duplicateId.set(null);
    }
  }

  addTag(event: MatChipInputEvent): void {
    const value = event.value.trim();
    if (value && !this.tagNames().includes(value)) {
      this.tagNames.set([...this.tagNames(), value]);
    }
    event.chipInput.clear();
  }

  removeTag(tag: string): void {
    this.tagNames.set(this.tagNames().filter((t) => t !== tag));
  }

  languageLabel(code: string): string {
    return `Translation — ${code.toUpperCase()}`;
  }

  /**
   * "Study again" (spec FR-D8): the word becomes due now in both directions. Not destructive,
   * so no confirmation. Preferred over Reset for a forgotten word — new cards are served
   * newest-first, so a reset old word would wait behind the whole new-card backlog.
   */
  async studyAgain(): Promise<void> {
    const id = this.wordId; // captured for the same reason as in resetProgress()
    if (!id || this.relearning()) return;
    this.relearning.set(true);
    try {
      await firstValueFrom(this.wordsApi.relearn(id));
      this.notify.success('It will come up in your next study session.');
    } catch (error) {
      this.notify.httpError(error);
    } finally {
      this.relearning.set(false);
    }
  }

  /** Forgets the word's SRS progress in both directions, after a confirmation (spec FR-D8). */
  async resetProgress(): Promise<void> {
    // Capture the id up front: the route subscription can swap wordId while the
    // dialog is open (following the duplicate-warning link), and the DELETE must
    // still target the word the user was looking at.
    const id = this.wordId;
    if (!id) return;
    const confirmed = await this.confirm.ask({
      title: 'Reset progress?',
      message:
        'This word will be scheduled as new again in both study directions. ' +
        'Its translations, tags and notes are kept.',
      confirmText: 'Reset',
      destructive: true,
    });
    if (!confirmed) return;

    this.resetting.set(true);
    try {
      await firstValueFrom(this.wordsApi.resetProgress(id));
      this.notify.success('Progress reset.');
    } catch (error) {
      this.notify.httpError(error);
    } finally {
      this.resetting.set(false);
    }
  }

  async save(): Promise<void> {
    if (this.form.invalid) return;
    this.saving.set(true);
    try {
      const value = this.form.getRawValue();
      const request: SaveWordRequest = {
        term: value.term,
        transcription: value.transcription || null,
        partOfSpeech: value.partOfSpeech || null,
        gender: value.gender || null,
        example: value.example || null,
        notes: value.notes || null,
        translations: value.translations
          .filter((t) => t.text.trim().length > 0)
          .map((t) => ({
            languageCode: t.languageCode,
            text: t.text,
            exampleTranslation: t.exampleTranslation || null,
          })),
        tags: this.tagNames(),
      };
      await firstValueFrom(
        this.wordId
          ? this.wordsApi.update(this.wordId, request)
          : this.wordsApi.create(request),
      );
      this.notify.success(this.wordId ? 'Word updated.' : 'Word added.');
      this.router.navigate(['/words']);
    } catch (error) {
      this.notify.httpError(error, 'Could not save the word.');
    } finally {
      this.saving.set(false);
    }
  }

  /**
   * Fills the form from the AI result. The AI returns plain text, so everything going into
   * an HTML field is escaped; multiple meanings become a numbered HTML list. Transcription,
   * part of speech and gender are plain-text fields and go in as they are.
   */
  private applyLookup(result: WordLookupResult): void {
    this.form.patchValue({
      term: result.term ? escapeHtml(result.term) : this.form.controls.term.value,
      transcription: result.transcription,
      partOfSpeech: result.partOfSpeech,
      gender: result.gender,
      example: escapeHtml(result.example ?? ''),
    });
    for (const group of this.translations.controls) {
      const code = group.controls.languageCode.value;
      group.patchValue({
        text: meaningsToHtml(result.meanings[code] ?? []),
        exampleTranslation: escapeHtml(result.exampleTranslations[code] ?? ''),
      });
    }
    this.checkDuplicate();
  }

  private translationGroup(languageCode: string): TranslationGroup {
    return this.formBuilder.nonNullable.group({
      languageCode: [languageCode],
      text: [''],
      exampleTranslation: [''],
    });
  }
}

/** Typed form group for one known-language translation block. */
type TranslationGroup = FormGroup<{
  languageCode: FormControl<string>;
  text: FormControl<string>;
  exampleTranslation: FormControl<string>;
}>;
