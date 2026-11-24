import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { ImportApi } from '../../core/api.services';
import { ImportCommitResponse, ImportPreviewResponse } from '../../core/api.types';
import { NotifyService } from '../../core/notify.service';

/** Anki .apkg import: upload → preview → confirm (spec §3.5). */
@Component({
  selector: 'app-import',
  imports: [
    FormsModule, RouterLink, MatCardModule, MatButtonModule, MatIconModule,
    MatCheckboxModule, MatProgressSpinnerModule,
  ],
  template: `
    <div class="page">
      <h1>Import from Anki</h1>

      @if (!preview() && !result()) {
        <mat-card appearance="outlined">
          <mat-card-content>
            <p>
              Upload an <code>.apkg</code> export from AnkiDroid or Anki.
              Decks and note tags become tags; study progress can be carried over.
            </p>
            <input
              #fileInput type="file" accept=".apkg" hidden
              (change)="onFileSelected($event)" />
            <button mat-flat-button (click)="fileInput.click()" [disabled]="uploading()">
              @if (uploading()) {
                <mat-spinner diameter="20" />
              } @else {
                <mat-icon>upload_file</mat-icon>
              }
              Choose .apkg file
            </button>
          </mat-card-content>
        </mat-card>
      }

      @if (preview(); as p) {
        <mat-card appearance="outlined">
          <mat-card-header><mat-card-title>Ready to import</mat-card-title></mat-card-header>
          <mat-card-content>
            <ul class="summary">
              <li><strong>{{ p.total }}</strong> notes in the file</li>
              <li><strong>{{ p.new }}</strong> new words</li>
              <li><strong>{{ p.duplicates }}</strong> already in your dictionary</li>
              <li><strong>{{ p.withProgress }}</strong> with study progress to carry over</li>
              @if (p.existingWithHistory > 0) {
                <li><strong>{{ p.existingWithHistory }}</strong> of your existing words have Anki review history</li>
              }
              @if (p.skipped.length > 0) {
                <li><strong>{{ p.skipped.length }}</strong> skipped (malformed)</li>
              }
            </ul>
            @if (p.skipped.length > 0) {
              <details>
                <summary>Skipped notes</summary>
                <ul>
                  @for (reason of p.skipped; track reason) {
                    <li>{{ reason }}</li>
                  }
                </ul>
              </details>
            }
            <mat-checkbox [(ngModel)]="importProgress">
              Carry over study progress (intervals are transferred approximately)
            </mat-checkbox>
            <!-- One-way [ngModel] + (ngModelChange) instead of [(ngModel)]: shown unticked while
                 progress is off, because no history is imported then. -->
            <mat-checkbox [ngModel]="importProgress && importHistory" (ngModelChange)="importHistory = $event"
                          [disabled]="!importProgress">
              Include the review history, so scheduling starts from it
            </mat-checkbox>
            <mat-checkbox [(ngModel)]="importDuplicates">
              Also import the {{ p.duplicates }} duplicates
            </mat-checkbox>
            @if (p.existingWithHistory > 0) {
              <p class="history-only">
                Already imported these words? <strong>History only</strong> adds no words: it gives
                the words in your dictionary their Anki review history and restarts their
                scheduling from it. The options above don't apply to it.
              </p>
            }
          </mat-card-content>
          <mat-card-actions>
            <button mat-flat-button (click)="commit(false)" [disabled]="committing()">
              @if (committing()) {
                <mat-spinner diameter="20" />
              } @else {
                Import
              }
            </button>
            @if (p.existingWithHistory > 0) {
              <button mat-stroked-button (click)="commit(true)" [disabled]="committing()">
                History only
              </button>
            }
            <button mat-button (click)="reset()">Cancel</button>
          </mat-card-actions>
        </mat-card>
      }

      @if (result(); as r) {
        <mat-card appearance="outlined" class="result">
          <mat-card-content>
            <mat-icon class="ok">check_circle</mat-icon>
            <h2>Import finished</h2>
            <p>
              {{ r.imported }} words imported,
              {{ r.statesImported }} study states carried over.
              @if (r.historyCards > 0) {
                <br />{{ r.historyCards }} cards got their review history ({{ r.historyReviews }} reviews).
              }
            </p>
            <div class="result-actions">
              <a mat-flat-button routerLink="/words">Open dictionary</a>
              <a mat-button routerLink="/study">Start studying</a>
              <button mat-button (click)="reset()">Import another file</button>
            </div>
          </mat-card-content>
        </mat-card>
      }
    </div>
  `,
  styles: `
    .page { padding: 16px; max-width: 600px; margin: 0 auto; }
    h1 { font-size: 22px; font-weight: 500; }
    .summary { padding-left: 20px; }
    mat-checkbox { display: block; margin-top: 4px; }
    .history-only { color: var(--mat-sys-on-surface-variant); font-size: 13px; margin: 12px 0 0; }
    mat-card-actions { padding: 0 16px 16px; gap: 8px; }
    details { margin-bottom: 12px; color: var(--mat-sys-on-surface-variant); }
    .result { text-align: center; padding: 16px; }
    .ok { font-size: 48px; width: 48px; height: 48px; color: var(--mat-sys-primary); }
    .result-actions { display: flex; gap: 8px; justify-content: center; flex-wrap: wrap; }
  `,
})
export class ImportComponent {
  private readonly importApi = inject(ImportApi);
  private readonly notify = inject(NotifyService);

  readonly preview = signal<ImportPreviewResponse | null>(null);
  readonly result = signal<ImportCommitResponse | null>(null);
  readonly uploading = signal(false);
  readonly committing = signal(false);

  importProgress = true;
  importHistory = true;
  importDuplicates = false;

  async onFileSelected(event: Event): Promise<void> {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    input.value = ''; // allow re-selecting the same file
    if (!file) return;

    this.uploading.set(true);
    try {
      this.preview.set(await firstValueFrom(this.importApi.upload(file)));
    } catch (error) {
      this.notify.httpError(error, 'Could not read this file.');
    } finally {
      this.uploading.set(false);
    }
  }

  /** historyOnly = the "History only" button: add no words, attach history to existing ones. */
  async commit(historyOnly: boolean): Promise<void> {
    const preview = this.preview();
    if (!preview) return;
    this.committing.set(true);
    try {
      this.result.set(await firstValueFrom(
        this.importApi.commit(preview.importId, {
          importDuplicates: this.importDuplicates,
          importProgress: this.importProgress,
          importHistory: this.importProgress && this.importHistory,
          historyOnly,
        }),
      ));
      this.preview.set(null);
    } catch (error) {
      // The server used up this upload when the commit started, so a retry needs the file again.
      this.notify.httpError(error, 'Import failed — nothing was saved. Upload the file again.');
      this.reset();
    } finally {
      this.committing.set(false);
    }
  }

  reset(): void {
    this.preview.set(null);
    this.result.set(null);
    this.importDuplicates = false;
    this.importProgress = true;
    this.importHistory = true;
  }
}
