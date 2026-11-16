import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { MatRippleModule } from '@angular/material/core';
import { MatIconModule } from '@angular/material/icon';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { LookupApi, SettingsApi, StudyApi } from '../../core/api.services';
import { Language, StudyCountsDto } from '../../core/api.types';
import { AuthService } from '../../core/auth.service';

/**
 * Home (`/`, spec §7): pick a mode — Translate or Learn. Deliberately neutral: two equal cards,
 * neither one the default. Each card is a single big link, so the whole card is the tap target.
 */
@Component({
  selector: 'app-home',
  imports: [RouterLink, MatIconModule, MatRippleModule],
  template: `
    <div class="page">
      <!-- A heading for screen readers only: the cards say it all visually. -->
      <h1 class="visually-hidden">Choose a mode</h1>

      <!-- matRipple adds Material's tap ripple to a plain element (tap feedback on phones). -->
      <a class="mode" routerLink="/translate" matRipple>
        <mat-icon class="mode-icon">translate</mat-icon>
        <span class="mode-text">
          <span class="title">Translate</span>
          <span class="description">{{ translateDescription() }}</span>
          @if (aiAvailable() === false) {
            <span class="status muted">AI is not configured on this server</span>
          }
        </span>
      </a>

      <a class="mode" routerLink="/study" matRipple>
        <mat-icon class="mode-icon">school</mat-icon>
        <span class="mode-text">
          <span class="title">Learn</span>
          <span class="description">Study the words in your dictionary</span>
          <!-- "; as cards" names the checked value for use inside the block. -->
          @if (backlog(); as cards) {
            @if (cards.due + cards.new === 0) {
              <!-- Neutral on purpose: also shown for an empty dictionary, and while "Again"
                   cards are a few minutes away. -->
              <span class="status muted">Nothing due right now</span>
            } @else {
              <span class="status">{{ cards.due }} due &middot; {{ cards.new }} new</span>
            }
          }
        </span>
      </a>
    </div>
  `,
  styles: `
    /* One column on phones; two cards side by side from 600px. */
    .page {
      display: grid; gap: 12px;
      padding: 16px; max-width: 800px; margin: 0 auto;
    }
    @media (min-width: 600px) {
      .page { grid-template-columns: 1fr 1fr; }
    }
    /* A link styled as an outlined card. position + overflow keep the ripple inside it. */
    .mode {
      position: relative; overflow: hidden;
      display: flex; align-items: center; gap: 16px;
      min-height: 120px; padding: 20px; box-sizing: border-box;
      border: 1px solid var(--mat-sys-outline-variant);
      border-radius: var(--mat-sys-corner-large);
      background: var(--mat-sys-surface-container-low);
      color: inherit; text-decoration: none;
    }
    .mode:hover { background: var(--mat-sys-surface-container); }
    .mode:focus-visible { outline: 2px solid var(--mat-sys-primary); outline-offset: 2px; }
    .mode-icon {
      flex: none; width: 40px; height: 40px; font-size: 40px;
      color: var(--mat-sys-primary);
    }
    .mode-text { display: flex; flex-direction: column; gap: 4px; min-width: 0; }
    .title { font-size: 22px; font-weight: 500; }
    .description, .muted { color: var(--mat-sys-on-surface-variant); }
    .status { margin-top: 4px; font-weight: 500; }
    /* Hidden on screen but still read by screen readers (unlike display: none). Absolute
       positioning also keeps it out of the grid's cells. */
    .visually-hidden {
      position: absolute; width: 1px; height: 1px; margin: -1px; padding: 0; border: 0;
      overflow: hidden; clip: rect(0 0 0 0); white-space: nowrap;
    }
  `,
})
export class HomeComponent implements OnInit {
  private readonly studyApi = inject(StudyApi);
  private readonly lookupApi = inject(LookupApi);
  private readonly settingsApi = inject(SettingsApi);
  private readonly auth = inject(AuthService);

  /** null until /api/study/counts answers (and if it fails): the card then shows no numbers. */
  private readonly counts = signal<StudyCountsDto[] | null>(null);
  /** null until /api/lookup/status answers: the hint shows only on a definite "no". */
  readonly aiAvailable = signal<boolean | null>(null);
  private readonly languages = signal<Language[]>([]);

  // computed() is a read-only signal derived from others; it recalculates when they change.

  /** Cards waiting to be studied, summed over both directions (no tag filter). */
  readonly backlog = computed(() => {
    const counts = this.counts();
    if (counts === null) return null;
    let due = 0;
    let newCards = 0;
    for (const direction of counts) {
      due += direction.due;
      newCards += direction.new;
    }
    return { due, new: newCards };
  });

  /** "Look up a word, phrase or sentence in Danish, English or Ukrainian" — the user's languages. */
  readonly translateDescription = computed(() => {
    const settings = this.auth.settings();
    const base = 'Look up a word, phrase or sentence';
    // Until the language names arrive, leave them out rather than flash bare codes.
    if (!settings || this.languages().length === 0) return base;
    const names = [settings.learningLanguage, ...settings.knownLanguages].map((code) =>
      this.languageName(code),
    );
    const last = names.pop();
    const list = names.length > 0 ? `${names.join(', ')} or ${last}` : last;
    return `${base} in ${list}`;
  });

  ngOnInit(): void {
    // Each request fails quietly: the cards still work as links, and the pages they open
    // report problems themselves. .then/.catch is the Promise form of await/try-catch.
    firstValueFrom(this.studyApi.counts())
      .then((counts) => this.counts.set(counts))
      .catch(() => {});
    firstValueFrom(this.lookupApi.status())
      .then((status) => this.aiAvailable.set(status.available))
      .catch(() => {});
    firstValueFrom(this.settingsApi.languages())
      .then((languages) => this.languages.set(languages))
      .catch(() => {});
  }

  private languageName(code: string): string {
    return this.languages().find((l) => l.code === code)?.name ?? code.toUpperCase();
  }
}
