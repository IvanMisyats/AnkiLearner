import { SaveWordRequest, TranslateCardDto, TranslationDto } from '../../core/api.types';
import { escapeHtml, meaningsToHtml } from '../../core/word-html';

/** Every word added from the Translate page gets this tag, so they are easy to find. */
export const TRANSLATED_TAG = 'translated';

/**
 * Builds the POST /api/words body for a translate card (one-tap Add).
 *
 * The card's text is plain AI output: the HTML fields (term, example, notes, translation
 * text and example translation) are escaped, the plain-text fields (transcription, part of
 * speech, gender) are sent as they are. A known language without meanings is left out —
 * the server rejects a translation with empty text.
 */
export function toSaveWordRequest(card: TranslateCardDto, knownLanguages: string[]): SaveWordRequest {
  const translations: TranslationDto[] = [];
  for (const code of knownLanguages) {
    const text = meaningsToHtml(card.meanings[code] ?? []);
    if (text === '') continue;
    translations.push({
      languageCode: code,
      text,
      exampleTranslation: htmlOrNull(card.exampleTranslations[code]),
    });
  }

  return {
    term: escapeHtml(card.term.trim()),
    transcription: textOrNull(card.transcription),
    partOfSpeech: textOrNull(card.partOfSpeech),
    gender: textOrNull(card.gender),
    example: htmlOrNull(card.example),
    notes: htmlOrNull(card.note),
    translations,
    tags: [TRANSLATED_TAG],
  };
}

/** Trimmed plain text, or null when there is nothing. */
function textOrNull(text: string | null | undefined): string | null {
  const trimmed = (text ?? '').trim();
  return trimmed === '' ? null : trimmed;
}

/** Trimmed text escaped for an HTML field, or null when there is nothing. */
function htmlOrNull(text: string | null | undefined): string | null {
  const trimmed = textOrNull(text);
  return trimmed === null ? null : escapeHtml(trimmed);
}
