import { Component, OnInit, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatChipsModule } from '@angular/material/chips';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { TagsApi, WordsApi } from '../../core/api.services';
import { TagDto, WordDto } from '../../core/api.types';
import { NotifyService } from '../../core/notify.service';

@Component({
  selector: 'app-words-list',
  imports: [
    RouterLink, MatFormFieldModule, MatInputModule, MatButtonModule, MatIconModule,
    MatCardModule, MatChipsModule, MatPaginatorModule, MatProgressSpinnerModule,
  ],
  template: `
    <div class="page">
      <div class="toolbar-row">
        <mat-form-field appearance="outline" class="search" subscriptSizing="dynamic">
          <mat-label>Search</mat-label>
          <input matInput [value]="search()" (input)="onSearch($event)" placeholder="term or translation" />
          <mat-icon matSuffix>search</mat-icon>
        </mat-form-field>
        <a mat-flat-button routerLink="/words/new">
          <mat-icon>add</mat-icon>
          Add word
        </a>
      </div>

      @if (tags().length > 0) {
        <mat-chip-listbox multiple class="tag-filter" aria-label="Filter by tag">
          @for (tag of tags(); track tag.id) {
            <mat-chip-option
              [selected]="selectedTags().includes(tag.name)"
              (selectionChange)="toggleTag(tag.name, $event.selected)">
              {{ tag.name }} ({{ tag.count }})
            </mat-chip-option>
          }
        </mat-chip-listbox>
      }

      @if (loading()) {
        <div class="center"><mat-spinner diameter="36" /></div>
      } @else if (words().length === 0) {
        <mat-card appearance="outlined" class="empty">
          <mat-card-content>
            @if (search() || selectedTags().length > 0) {
              <p>No words match your filter.</p>
            } @else {
              <p>Your dictionary is empty. Add your first word or import an Anki deck.</p>
            }
          </mat-card-content>
        </mat-card>
      } @else {
        @for (word of words(); track word.id) {
          <mat-card appearance="outlined" class="word-card">
            <div class="word-main">
              <!-- Word content is HTML sanitized on the server; Angular's [innerHTML]
                   binding sanitizes again on render (defense in depth). -->
              <div class="term-row">
                <span class="term" [innerHTML]="word.term"></span>
                @if (word.transcription) {
                  <span class="transcription">{{ word.transcription }}</span>
                }
                @if (word.gender) {
                  <span class="gender">{{ word.gender }}</span>
                }
              </div>
              @for (translation of word.translations; track translation.languageCode) {
                <div class="translation">
                  <span class="lang">{{ translation.languageCode.toUpperCase() }}</span>
                  <span [innerHTML]="translation.text"></span>
                </div>
              }
              @if (word.tags.length > 0) {
                <div class="tags">
                  @for (tag of word.tags; track tag) {
                    <span class="tag">{{ tag }}</span>
                  }
                </div>
              }
            </div>
            <div class="actions">
              <a mat-icon-button [routerLink]="['/words', word.id, 'edit']" aria-label="Edit">
                <mat-icon>edit</mat-icon>
              </a>
              <button mat-icon-button (click)="remove(word)" aria-label="Delete">
                <mat-icon>delete</mat-icon>
              </button>
            </div>
          </mat-card>
        }
        <mat-paginator
          [length]="total()"
          [pageIndex]="page() - 1"
          [pageSize]="pageSize"
          [hidePageSize]="true"
          (page)="onPage($event)" />
      }
    </div>
  `,
  styles: `
    .page { padding: 16px; max-width: 900px; margin: 0 auto; }
    .toolbar-row { display: flex; gap: 12px; align-items: center; margin-bottom: 12px; }
    .search { flex: 1; }
    .tag-filter { margin-bottom: 12px; display: block; }
    .center { display: flex; justify-content: center; padding: 32px; }
    .empty { text-align: center; color: var(--mat-sys-on-surface-variant); }

    .word-card {
      display: flex; flex-direction: row; align-items: flex-start;
      margin-bottom: 8px; padding: 12px 8px 12px 16px;
    }
    .word-main { flex: 1; min-width: 0; }
    .term-row { display: flex; gap: 8px; align-items: baseline; flex-wrap: wrap; }
    .term { font-size: 18px; font-weight: 500; }
    .transcription, .gender { color: var(--mat-sys-on-surface-variant); font-size: 14px; }
    .translation { margin-top: 4px; }
    .translation .lang {
      font-size: 11px; color: var(--mat-sys-on-surface-variant);
      margin-right: 6px; vertical-align: middle;
    }
    .tags { margin-top: 8px; display: flex; gap: 6px; flex-wrap: wrap; }
    .tag {
      font-size: 12px; padding: 2px 8px; border-radius: 12px;
      background: var(--mat-sys-surface-container-high);
    }
    .actions { display: flex; flex-direction: column; }
  `,
})
export class WordsListComponent implements OnInit {
  private readonly wordsApi = inject(WordsApi);
  private readonly tagsApi = inject(TagsApi);
  private readonly notify = inject(NotifyService);

  readonly words = signal<WordDto[]>([]);
  readonly tags = signal<TagDto[]>([]);
  readonly total = signal(0);
  readonly page = signal(1);
  readonly search = signal('');
  readonly selectedTags = signal<string[]>([]);
  readonly loading = signal(true);
  readonly pageSize = 25;

  private searchDebounce?: ReturnType<typeof setTimeout>;

  ngOnInit(): void {
    this.load();
    firstValueFrom(this.tagsApi.list())
      .then((tags) => this.tags.set(tags))
      .catch(() => {});
  }

  onSearch(event: Event): void {
    const value = (event.target as HTMLInputElement).value;
    clearTimeout(this.searchDebounce);
    this.searchDebounce = setTimeout(() => {
      this.search.set(value);
      this.page.set(1);
      this.load();
    }, 300);
  }

  toggleTag(name: string, selected: boolean): void {
    const current = this.selectedTags();
    this.selectedTags.set(selected ? [...current, name] : current.filter((t) => t !== name));
    this.page.set(1);
    this.load();
  }

  onPage(event: PageEvent): void {
    this.page.set(event.pageIndex + 1);
    this.load();
  }

  async remove(word: WordDto): Promise<void> {
    const plain = word.term.replace(/<[^>]*>/g, '');
    if (!confirm(`Delete "${plain}" and its study progress?`)) return;
    try {
      await firstValueFrom(this.wordsApi.delete(word.id));
      this.notify.success('Word deleted.');
      this.load();
    } catch (error) {
      this.notify.httpError(error);
    }
  }

  private async load(): Promise<void> {
    this.loading.set(true);
    try {
      const result = await firstValueFrom(this.wordsApi.list({
        search: this.search() || undefined,
        tag: this.selectedTags().join(',') || undefined,
        page: this.page(),
        pageSize: this.pageSize,
      }));
      this.words.set(result.items);
      this.total.set(result.total);
    } catch (error) {
      this.notify.httpError(error);
    } finally {
      this.loading.set(false);
    }
  }
}
