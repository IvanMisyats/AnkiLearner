import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatListModule } from '@angular/material/list';
import { MatSelectModule } from '@angular/material/select';
import { firstValueFrom } from 'rxjs';
import { SettingsApi } from '../../core/api.services';
import { Language, UpdateSettingsRequest } from '../../core/api.types';
import { AuthService } from '../../core/auth.service';
import { NotifyService } from '../../core/notify.service';

@Component({
  selector: 'app-settings',
  imports: [
    FormsModule, MatCardModule, MatFormFieldModule, MatSelectModule, MatInputModule,
    MatButtonModule, MatIconModule, MatListModule,
  ],
  template: `
    <div class="page">
      <h1>Settings</h1>
      <mat-card appearance="outlined">
        <mat-card-content>
          <!-- subscriptSizing="dynamic": the hint area grows for a two-line hint instead of
               overlapping the next element on a phone. -->
          <mat-form-field appearance="outline" class="full" subscriptSizing="dynamic">
            <mat-label>Language I'm learning</mat-label>
            <mat-select [(ngModel)]="learningLanguage">
              @for (language of languages(); track language.code) {
                <mat-option [value]="language.code">{{ language.name }}</mat-option>
              }
            </mat-select>
            <mat-hint>Switching hides words of other languages — nothing is deleted.</mat-hint>
          </mat-form-field>

          <h2>Languages I know</h2>
          <p class="hint">The first one is primary — it is used as the default translation language.</p>
          <mat-list>
            @for (code of knownLanguages(); track code; let i = $index, first = $first) {
              <mat-list-item>
                <span matListItemTitle>{{ languageName(code) }} @if (first) { <em>(primary)</em> }</span>
                <span matListItemMeta>
                  @if (!first) {
                    <button mat-icon-button (click)="moveUp(i)" aria-label="Move up">
                      <mat-icon>arrow_upward</mat-icon>
                    </button>
                  }
                  @if (knownLanguages().length > 1) {
                    <button mat-icon-button (click)="removeKnown(code)" aria-label="Remove">
                      <mat-icon>close</mat-icon>
                    </button>
                  }
                </span>
              </mat-list-item>
            }
          </mat-list>
          <mat-form-field appearance="outline" class="full">
            <mat-label>Add a known language</mat-label>
            <mat-select #addSelect (selectionChange)="addKnown($event.value); addSelect.value = null">
              @for (language of addableLanguages(); track language.code) {
                <mat-option [value]="language.code">{{ language.name }}</mat-option>
              }
            </mat-select>
          </mat-form-field>

          <mat-form-field appearance="outline" class="full">
            <mat-label>New words per day (0 = unlimited)</mat-label>
            <input matInput type="number" min="0" max="1000" [(ngModel)]="dailyNewLimit" />
          </mat-form-field>

          <mat-form-field appearance="outline" class="full" subscriptSizing="dynamic">
            <mat-label>Maximum interval (days)</mat-label>
            <input matInput type="number" min="1" max="36500" [(ngModel)]="maxIntervalDays" />
            <mat-hint>A card is never scheduled further out than this. Anki's default is 36500.</mat-hint>
          </mat-form-field>

          <h2>Scheduling (FSRS)</h2>
          <mat-form-field appearance="outline" class="full" subscriptSizing="dynamic">
            <mat-label>Desired retention (%)</mat-label>
            <input matInput type="number" min="70" max="97" step="1" [(ngModel)]="retentionPercent" />
            <mat-hint>How likely you should be to still remember a card when it comes due.
              Lower = fewer reviews, more forgetting. 85–90 suits most learners.</mat-hint>
          </mat-form-field>

          <mat-form-field appearance="outline" class="full weights" subscriptSizing="dynamic">
            <mat-label>FSRS weights (optional)</mat-label>
            <textarea matInput rows="3" [(ngModel)]="weightsText"
                      placeholder="Leave empty to use the defaults"></textarea>
            <mat-hint>21 numbers fitted to your own reviews. In Anki: Deck options → FSRS →
              Optimize, then copy them here.</mat-hint>
          </mat-form-field>

          <button mat-flat-button (click)="save()" [disabled]="saving()">Save settings</button>
        </mat-card-content>
      </mat-card>
    </div>
  `,
  styles: `
    .page { padding: 16px; max-width: 600px; margin: 0 auto; }
    h1 { font-size: 22px; font-weight: 500; }
    h2 { font-size: 16px; font-weight: 500; margin: 8px 0 0; }
    .hint { color: var(--mat-sys-on-surface-variant); font-size: 13px; margin: 4px 0 0; }
    .full { width: 100%; margin-top: 12px; }
    /* Two-line hints need room below the field. */
    .weights { margin-bottom: 16px; }
    button[mat-flat-button] { margin-top: 8px; }
  `,
})
export class SettingsComponent implements OnInit {
  private readonly settingsApi = inject(SettingsApi);
  private readonly auth = inject(AuthService);
  private readonly notify = inject(NotifyService);

  readonly languages = signal<Language[]>([]);
  readonly knownLanguages = signal<string[]>([]);
  readonly saving = signal(false);
  learningLanguage = '';
  dailyNewLimit = 20;
  maxIntervalDays = 36500;
  retentionPercent = 90;
  weightsText = '';
  // What the page loaded: a field left as it was is not sent, so saving the page for another
  // reason never changes (and re-schedules) the FSRS settings by rounding or a blank box.
  private loadedRetentionPercent = 90;
  private loadedWeightsText = '';

  readonly addableLanguages = computed(() =>
    this.languages().filter(
      (l) => l.code !== this.learningLanguage && !this.knownLanguages().includes(l.code),
    ),
  );

  ngOnInit(): void {
    const settings = this.auth.settings();
    if (settings) {
      this.learningLanguage = settings.learningLanguage;
      this.knownLanguages.set([...settings.knownLanguages]);
      this.dailyNewLimit = settings.dailyNewLimit;
      this.maxIntervalDays = settings.maxIntervalDays;
      this.retentionPercent = this.loadedRetentionPercent = Math.round(settings.desiredRetention * 100);
      // `?.` and `??`: null weights (= defaults) show as an empty box.
      this.weightsText = this.loadedWeightsText = settings.fsrsWeights?.join(', ') ?? '';
    }
    firstValueFrom(this.settingsApi.languages())
      .then((languages) => this.languages.set(languages))
      .catch((error) => this.notify.httpError(error));
  }

  languageName(code: string): string {
    return this.languages().find((l) => l.code === code)?.name ?? code;
  }

  addKnown(code: string | null): void {
    if (code && !this.knownLanguages().includes(code)) {
      this.knownLanguages.set([...this.knownLanguages(), code]);
    }
  }

  removeKnown(code: string): void {
    this.knownLanguages.set(this.knownLanguages().filter((c) => c !== code));
  }

  moveUp(index: number): void {
    const list = [...this.knownLanguages()];
    [list[index - 1], list[index]] = [list[index], list[index - 1]];
    this.knownLanguages.set(list);
  }

  async save(): Promise<void> {
    const retention = Number(this.retentionPercent);
    if (!(retention >= 70 && retention <= 97)) {
      this.notify.error('Desired retention must be between 70 and 97 %.');
      return;
    }
    const weights = parseWeights(this.weightsText);
    if (weights === null) {
      const found = countWeights(this.weightsText);
      this.notify.error(`FSRS weights must be 21 numbers (found ${found}). Anki 25 or later shows 21.`);
      return;
    }
    const request: UpdateSettingsRequest = {
      learningLanguage: this.learningLanguage,
      knownLanguages: this.knownLanguages(),
      dailyNewLimit: Number(this.dailyNewLimit) || 0,
      maxIntervalDays: Number(this.maxIntervalDays) || 36500,
    };
    if (retention !== this.loadedRetentionPercent) request.desiredRetention = retention / 100;
    if (this.weightsText.trim() !== this.loadedWeightsText) request.fsrsWeights = weights; // [] = defaults

    this.saving.set(true);
    try {
      const updated = await firstValueFrom(this.settingsApi.update(request));
      this.retentionPercent = this.loadedRetentionPercent = Math.round(updated.desiredRetention * 100);
      this.weightsText = this.loadedWeightsText = updated.fsrsWeights?.join(', ') ?? '';
      this.auth.refreshSettings(updated);
      this.notify.success('Settings saved.');
    } catch (error) {
      this.notify.httpError(error, 'Could not save settings.');
    } finally {
      this.saving.set(false);
    }
  }
}

/**
 * Parses pasted FSRS weights: numbers separated by commas and/or whitespace, as Anki shows them.
 * Returns [] for an empty box (= use the defaults), the 21 numbers, or null if the text is not
 * exactly 21 numbers. The server checks each value's allowed range.
 */
export function parseWeights(text: string): number[] | null {
  const parts = splitWeights(text);
  if (parts.length === 0) return [];
  const numbers = parts.map(Number);
  return numbers.length === 21 && numbers.every((n) => Number.isFinite(n)) ? numbers : null;
}

/** How many values were pasted — for the error message. */
export function countWeights(text: string): number {
  return splitWeights(text).length;
}

function splitWeights(text: string): string[] {
  return text.split(/[\s,]+/).filter((part) => part.length > 0);
}
