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
}
