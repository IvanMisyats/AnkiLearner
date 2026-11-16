import { Injectable, signal } from '@angular/core';
import { TranslateCardDto, TranslateResponse } from '../../core/api.types';

/** Where a card stands on this page: not saved, saving, saved now, or already saved earlier. */
export type CardStatus = 'new' | 'adding' | 'added' | 'existing';

/**
 * A suggested card plus its progress on the page — a small view model. Its signals work like
 * properties raising INotifyPropertyChanged: the template re-renders when they are set.
 */
export class CardEntry {
  readonly status = signal<CardStatus>('new');
  /** The dictionary word behind the card: the existing match, or the word just added. */
  readonly wordId = signal<string | null>(null);
  /** True while a "Study again" request runs, so a double tap can't send two. */
  readonly relearning = signal(false);

  constructor(readonly card: TranslateCardDto) {
    if (card.existingWordId) {
      this.status.set('existing');
      this.wordId.set(card.existingWordId);
    }
  }
}

/**
 * Keeps the Translate page's last result in memory while the user is elsewhere in the app,
 * so Back (or Edit → return) shows it again without a new, paid AI call.
 *
 * `providedIn: 'root'` makes it one shared instance for the whole app (C#: a singleton),
 * whereas the page component is created anew on every visit. This is view state only:
 * nothing is persisted and there is no history — a reload starts empty.
 *
 * The kept state belongs to an **owner** — the user and their languages. Signing out is
 * only a page change inside the app, so without this another user signing in on the same
 * tab, or the same user after a change of languages, would see (and could save) the
 * previous owner's cards. The page calls reset() when the owner differs.
 */
@Injectable({ providedIn: 'root' })
export class TranslateStateService {
  /** Whose state this is, see ownerKey() in the page; '' before the first submission. */
  owner = '';
  /** The text of the last submission. */
  readonly text = signal('');
  /** The language override of the last submission; null = auto-detect. */
  readonly sourceLanguage = signal<string | null>(null);
  /** True while a translation request runs. */
  readonly loading = signal(false);
  /** The last successful response; null before the first one or while a new one loads. */
  readonly result = signal<TranslateResponse | null>(null);
  /** One entry per card of `result`, in the same order. */
  readonly cards = signal<CardEntry[]>([]);
  /** The "whole input" entry of `result`, if it has one. */
  readonly wholeInput = signal<CardEntry | null>(null);

  /** A new submission: remembers who asked what, and drops the previous result. */
  start(owner: string, text: string, sourceLanguage: string | null): void {
    this.owner = owner;
    this.text.set(text);
    this.sourceLanguage.set(sourceLanguage);
    this.loading.set(true);
    this.setResult(null);
  }

  /**
   * The submission succeeded. A response that arrives after a reset for another owner
   * (signed out mid-request) is dropped, so it can't show up on their page.
   */
  finish(owner: string, response: TranslateResponse): void {
    if (owner !== this.owner) return;
    this.loading.set(false);
    this.setResult(response);
  }

  /** The submission failed; the text stays so the user can retry. */
  fail(owner: string): void {
    if (owner !== this.owner) return;
    this.loading.set(false);
  }

  /** Forgets everything: the page starts empty. */
  reset(): void {
    this.owner = '';
    this.text.set('');
    this.sourceLanguage.set(null);
    this.loading.set(false);
    this.setResult(null);
  }

  private setResult(response: TranslateResponse | null): void {
    this.result.set(response);
    this.cards.set(response ? response.cards.map((card) => new CardEntry(card)) : []);
    this.wholeInput.set(response?.wholeInput ? new CardEntry(response.wholeInput) : null);
  }
}
