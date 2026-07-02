import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import {
  DuplicateCheckResponse,
  ExerciseType,
  ImportCommitResponse,
  ImportPreviewResponse,
  Language,
  LookupStatusResponse,
  PagedResponse,
  ReviewGrade,
  SaveWordRequest,
  SettingsDto,
  StudyCountsDto,
  StudyNextResponse,
  TagDto,
  WordDto,
  WordLookupResult,
} from './api.types';

// Thin, typed wrappers over the REST API (one class per controller).
// They return Observables; components typically convert with firstValueFrom().

@Injectable({ providedIn: 'root' })
export class WordsApi {
  private readonly http = inject(HttpClient);

  list(options: { search?: string; tag?: string; page?: number; pageSize?: number }): Observable<PagedResponse<WordDto>> {
    let params = new HttpParams();
    if (options.search) params = params.set('search', options.search);
    if (options.tag) params = params.set('tag', options.tag);
    if (options.page) params = params.set('page', options.page);
    if (options.pageSize) params = params.set('pageSize', options.pageSize);
    return this.http.get<PagedResponse<WordDto>>('/api/words', { params });
  }

  get(id: string): Observable<WordDto> {
    return this.http.get<WordDto>(`/api/words/${id}`);
  }

  create(request: SaveWordRequest): Observable<WordDto> {
    return this.http.post<WordDto>('/api/words', request);
  }

  update(id: string, request: SaveWordRequest): Observable<WordDto> {
    return this.http.put<WordDto>(`/api/words/${id}`, request);
  }

  delete(id: string): Observable<void> {
    return this.http.delete<void>(`/api/words/${id}`);
  }

  checkDuplicate(term: string, excludeId?: string): Observable<DuplicateCheckResponse> {
    let params = new HttpParams().set('term', term);
    if (excludeId) params = params.set('excludeId', excludeId);
    return this.http.get<DuplicateCheckResponse>('/api/words/duplicate', { params });
  }
}

@Injectable({ providedIn: 'root' })
export class TagsApi {
  private readonly http = inject(HttpClient);

  list(): Observable<TagDto[]> {
    return this.http.get<TagDto[]>('/api/tags');
  }

  create(name: string): Observable<TagDto> {
    return this.http.post<TagDto>('/api/tags', { name });
  }

  rename(id: string, name: string): Observable<TagDto> {
    return this.http.put<TagDto>(`/api/tags/${id}`, { name });
  }

  delete(id: string): Observable<void> {
    return this.http.delete<void>(`/api/tags/${id}`);
  }
}

@Injectable({ providedIn: 'root' })
export class SettingsApi {
  private readonly http = inject(HttpClient);

  get(): Observable<SettingsDto> {
    return this.http.get<SettingsDto>('/api/settings');
  }

  update(settings: SettingsDto): Observable<SettingsDto> {
    return this.http.put<SettingsDto>('/api/settings', settings);
  }

  languages(): Observable<Language[]> {
    return this.http.get<Language[]>('/api/languages');
  }
}

@Injectable({ providedIn: 'root' })
export class LookupApi {
  private readonly http = inject(HttpClient);

  status(): Observable<LookupStatusResponse> {
    return this.http.get<LookupStatusResponse>('/api/lookup/status');
  }

  lookup(term: string): Observable<WordLookupResult> {
    return this.http.post<WordLookupResult>('/api/lookup', { term });
  }
}

@Injectable({ providedIn: 'root' })
export class ImportApi {
  private readonly http = inject(HttpClient);

  upload(file: File): Observable<ImportPreviewResponse> {
    const form = new FormData();
    form.append('file', file, file.name);
    return this.http.post<ImportPreviewResponse>('/api/import/apkg', form);
  }

  commit(importId: string, importDuplicates: boolean, importProgress: boolean): Observable<ImportCommitResponse> {
    return this.http.post<ImportCommitResponse>(
      `/api/import/apkg/${importId}/commit`, { importDuplicates, importProgress });
  }
}

@Injectable({ providedIn: 'root' })
export class StudyApi {
  private readonly http = inject(HttpClient);

  counts(tag?: string): Observable<StudyCountsDto[]> {
    let params = new HttpParams();
    if (tag) params = params.set('tag', tag);
    return this.http.get<StudyCountsDto[]>('/api/study/counts', { params });
  }

  next(exercise: ExerciseType, tag?: string): Observable<StudyNextResponse> {
    let params = new HttpParams().set('exercise', exercise);
    if (tag) params = params.set('tag', tag);
    return this.http.get<StudyNextResponse>('/api/study/next', { params });
  }

  grade(wordId: string, exercise: ExerciseType, grade: ReviewGrade, tag?: string): Observable<StudyNextResponse> {
    let params = new HttpParams();
    if (tag) params = params.set('tag', tag);
    return this.http.post<StudyNextResponse>('/api/study/grade', { wordId, exercise, grade }, { params });
  }
}
