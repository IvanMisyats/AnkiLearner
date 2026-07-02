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
import { Language } from '../../core/api.types';
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
          <mat-form-field appearance="outline" class="full">
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
    this.saving.set(true);
    try {
      const updated = await firstValueFrom(this.settingsApi.update({
        learningLanguage: this.learningLanguage,
        knownLanguages: this.knownLanguages(),
        dailyNewLimit: Number(this.dailyNewLimit) || 0,
      }));
      this.auth.refreshSettings(updated);
      this.notify.success('Settings saved.');
    } catch (error) {
      this.notify.httpError(error, 'Could not save settings.');
    } finally {
      this.saving.set(false);
    }
  }
}
