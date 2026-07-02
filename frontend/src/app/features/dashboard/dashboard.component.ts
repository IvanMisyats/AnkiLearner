import { Component, OnInit, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatChipsModule } from '@angular/material/chips';
import { MatIconModule } from '@angular/material/icon';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { SettingsApi, StudyApi, TagsApi } from '../../core/api.services';
import { ExerciseType, Language, StudyCountsDto, TagDto } from '../../core/api.types';
import { AuthService } from '../../core/auth.service';
import { NotifyService } from '../../core/notify.service';

/** Study dashboard: due/new counters per direction + start buttons (spec FR-R1). */
@Component({
  selector: 'app-dashboard',
  imports: [RouterLink, MatCardModule, MatButtonModule, MatIconModule, MatChipsModule],
  template: `
    <div class="page">
      <h1>Study</h1>

      @if (tags().length > 0) {
        <mat-chip-listbox class="tag-filter" aria-label="Limit study to a tag">
          @for (tag of tags(); track tag.id) {
            <mat-chip-option
              [selected]="selectedTag() === tag.name"
              (selectionChange)="selectTag(tag.name, $event.selected)">
              {{ tag.name }}
            </mat-chip-option>
          }
        </mat-chip-listbox>
      }

      <div class="directions">
        @for (direction of directions; track direction.exercise) {
          <mat-card appearance="outlined">
            <mat-card-header>
              <mat-card-title>{{ directionLabel(direction.exercise) }}</mat-card-title>
            </mat-card-header>
            <mat-card-content>
              <div class="counters">
                <div class="counter">
                  <span class="number due">{{ countFor(direction.exercise)?.due ?? 0 }}</span>
                  <span class="label">due</span>
                </div>
                <div class="counter">
                  <span class="number new">{{ countFor(direction.exercise)?.new ?? 0 }}</span>
                  <span class="label">new</span>
                </div>
              </div>
            </mat-card-content>
            <mat-card-actions>
              <a
                mat-flat-button
                [routerLink]="['/study', direction.exercise]"
                [queryParams]="selectedTag() ? { tag: selectedTag() } : {}"
                [disabled]="totalFor(direction.exercise) === 0">
                <mat-icon>play_arrow</mat-icon>
                Start
              </a>
            </mat-card-actions>
          </mat-card>
        }
      </div>
    </div>
  `,
  styles: `
    .page { padding: 16px; max-width: 800px; margin: 0 auto; }
    h1 { font-size: 22px; font-weight: 500; }
    .tag-filter { display: block; margin-bottom: 12px; }
    .directions { display: grid; grid-template-columns: repeat(auto-fit, minmax(260px, 1fr)); gap: 12px; }
    .counters { display: flex; gap: 32px; padding: 8px 0; }
    .counter { display: flex; flex-direction: column; align-items: center; }
    .number { font-size: 32px; font-weight: 600; }
    .number.due { color: var(--mat-sys-primary); }
    .number.new { color: var(--mat-sys-tertiary); }
    .label { font-size: 13px; color: var(--mat-sys-on-surface-variant); }
    mat-card-actions { padding: 0 16px 16px; }
  `,
})
export class DashboardComponent implements OnInit {
  private readonly studyApi = inject(StudyApi);
  private readonly tagsApi = inject(TagsApi);
  private readonly settingsApi = inject(SettingsApi);
  private readonly auth = inject(AuthService);
  private readonly notify = inject(NotifyService);

  readonly counts = signal<StudyCountsDto[]>([]);
  readonly tags = signal<TagDto[]>([]);
  readonly selectedTag = signal<string | null>(null);
  private readonly languages = signal<Language[]>([]);

  readonly directions: { exercise: ExerciseType }[] = [
    { exercise: 'TargetToKnown' },
    { exercise: 'KnownToTarget' },
  ];

  ngOnInit(): void {
    this.loadCounts();
    firstValueFrom(this.tagsApi.list()).then((t) => this.tags.set(t)).catch(() => {});
    firstValueFrom(this.settingsApi.languages()).then((l) => this.languages.set(l)).catch(() => {});
  }

  countFor(exercise: ExerciseType): StudyCountsDto | undefined {
    return this.counts().find((c) => c.exercise === exercise);
  }

  totalFor(exercise: ExerciseType): number {
    const count = this.countFor(exercise);
    return (count?.due ?? 0) + (count?.new ?? 0);
  }

  /** e.g. "Danish → English, Ukrainian" using the user's actual languages (FR-R10). */
  directionLabel(exercise: ExerciseType): string {
    const settings = this.auth.settings();
    if (!settings) return exercise;
    const target = this.languageName(settings.learningLanguage);
    const known = settings.knownLanguages.map((c) => this.languageName(c)).join(', ');
    return exercise === 'TargetToKnown' ? `${target} → ${known}` : `${known} → ${target}`;
  }

  selectTag(name: string, selected: boolean): void {
    this.selectedTag.set(selected ? name : null);
    this.loadCounts();
  }

  private languageName(code: string): string {
    return this.languages().find((l) => l.code === code)?.name ?? code.toUpperCase();
  }

  private async loadCounts(): Promise<void> {
    try {
      this.counts.set(await firstValueFrom(this.studyApi.counts(this.selectedTag() ?? undefined)));
    } catch (error) {
      this.notify.httpError(error);
    }
  }
}
