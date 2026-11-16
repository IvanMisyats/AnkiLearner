// TypeScript mirrors of the backend API contracts (backend/AnkiLearner.Api/Contracts).
// Property names are camelCase because ASP.NET Core serializes JSON that way.

export interface UserDto {
  id: string;
  email: string;
}

export interface SettingsDto {
  learningLanguage: string;
  knownLanguages: string[];
  dailyNewLimit: number;
  /** Ceiling on the SRS interval, in days. */
  maxIntervalDays: number;
}

export interface AuthResponse {
  accessToken: string;
  user: UserDto;
}

export interface MeResponse {
  user: UserDto;
  settings: SettingsDto;
}

export interface Language {
  code: string;
  name: string;
}

export interface TranslationDto {
  languageCode: string;
  text: string;
  exampleTranslation: string | null;
}

export interface WordDto {
  id: string;
  languageCode: string;
  term: string;
  transcription: string | null;
  partOfSpeech: string | null;
  gender: string | null;
  example: string | null;
  notes: string | null;
  createdAt: string;
  updatedAt: string;
  translations: TranslationDto[];
  tags: string[];
}

export interface SaveWordRequest {
  term: string;
  transcription?: string | null;
  partOfSpeech?: string | null;
  gender?: string | null;
  example?: string | null;
  notes?: string | null;
  translations: TranslationDto[];
  tags: string[];
}

export interface PagedResponse<T> {
  items: T[];
  total: number;
  page: number;
  pageSize: number;
}

export interface DuplicateCheckResponse {
  exists: boolean;
  wordId: string | null;
}

export interface TagDto {
  id: string;
  name: string;
  count: number;
}

export interface LookupStatusResponse {
  available: boolean;
  provider: string;
}

export interface WordLookupResult {
  term: string;
  transcription: string;
  partOfSpeech: string;
  gender: string;
  meanings: Record<string, string[]>;
  example: string;
  exampleTranslations: Record<string, string>;
}

/**
 * How the AI classified the translate input. The backend sends the C# enum's name
 * as a string; in TypeScript a union of string literals plays the role of that enum.
 */
export type InputKind = 'Word' | 'Phrase' | 'Sentence';

/**
 * One suggested learning-language entry from POST /api/translate. All text is plain
 * text from the AI — render it with {{ }} only, and escape it before saving.
 */
export interface TranslateCardDto {
  /** Citation form, e.g. "en hund", "at spise". */
  term: string;
  transcription: string;
  partOfSpeech: string;
  gender: string;
  /** Meanings per known-language code, e.g. { en: ['dog'], uk: ['собака'] }. */
  meanings: Record<string, string[]>;
  example: string;
  /** The example translated into each known language, keyed by language code. */
  exampleTranslations: Record<string, string>;
  /** One sentence in the first known language, e.g. how it differs from a synonym. */
  note: string;
  /** Set when the user's dictionary already holds this entry (leading article ignored). */
  existingWordId: string | null;
}

export interface TranslateResponse {
  /** The detected language code, or the override when one was sent. */
  sourceLanguage: string;
  inputKind: InputKind;
  /** The whole input in every language, keyed by code; the source slot holds the original. */
  inputTranslations: Record<string, string>;
  cards: TranslateCardDto[];
  /** The whole multi-word input as one entry, when no card already is. */
  wholeInput: TranslateCardDto | null;
}

export interface ImportPreviewResponse {
  importId: string;
  total: number;
  new: number;
  duplicates: number;
  withProgress: number;
  skipped: string[];
}

export interface ImportCommitResponse {
  imported: number;
  statesImported: number;
}

export type ExerciseType = 'TargetToKnown' | 'KnownToTarget';
export type ReviewGrade = 'Again' | 'Hard' | 'Good' | 'Easy';

export interface StudyIntervalsDto {
  again: string;
  hard: string;
  good: string;
  easy: string;
}

export interface StudyCardDto {
  word: WordDto;
  prompt: string;
  answer: string;
  isNew: boolean;
  intervals: StudyIntervalsDto;
}

export interface StudyNextResponse {
  card: StudyCardDto | null;
  remaining: number;
}

export interface StudyCountsDto {
  exercise: ExerciseType;
  due: number;
  new: number;
  /** Distinct words reviewed since 00:00 UTC in this direction. */
  reviewedToday: number;
}
