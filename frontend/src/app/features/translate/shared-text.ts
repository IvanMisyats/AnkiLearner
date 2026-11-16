/** The server translates at most this many characters (one sentence, spec FR-T1). */
export const MAX_TRANSLATE_LENGTH = 300;

/**
 * Quote pairs (opening + closing character) that may wrap shared text. Written as \u escapes
 * so look-alike characters stay distinguishable in the source.
 */
const QUOTE_PAIRS = [
  '""', // straight double
  "''", // straight single
  '\u201C\u201D', // curly double (English)
  '\u2018\u2019', // curly single (English)
  '\u201E\u201C', // low-high double (German, Danish)
  '\u201A\u2018', // low-high single (German, Danish)
  '\u00AB\u00BB', // guillemets pointing out (French)
  '\u00BB\u00AB', // guillemets pointing in (Danish)
];

/** http(s) links and bare www. links, up to the next whitespace. */
const URL_PATTERN = /\b(?:https?:\/\/|www\.)\S+/gi;

/**
 * Turns text handed to the page from outside — a `?q=` link, or Android's share sheet
 * (text as `q`, `title`, `url`) — into translate input. Takes the first part that
 * is still non-empty after cleaning: URLs removed, whitespace collapsed, wrapping quotes
 * removed, at most {@link MAX_TRANSLATE_LENGTH} characters. Returns '' when nothing is left.
 *
 * `...parts` is a rest parameter (C#: `params string?[] parts`).
 */
export function extractSharedText(...parts: (string | null | undefined)[]): string {
  for (const part of parts) {
    const cleaned = clean(part ?? '');
    if (cleaned !== '') return cleaned;
  }
  return '';
}

function clean(text: string): string {
  let result = text.replace(URL_PATTERN, ' ').replace(/\s+/g, ' ').trim();

  // Strip ONE layer of wrapping quotes, and only a matching pair: "hund" → hund. Android wraps
  // a shared selection once, so a selection that itself starts and ends with quotes
  // ("Hej" og "farvel") keeps them; mismatched quotes are left alone.
  if (result.length >= 2 && QUOTE_PAIRS.includes(result[0] + result[result.length - 1])) {
    result = result.slice(1, -1).trim();
  }

  return result.slice(0, MAX_TRANSLATE_LENGTH).trim();
}
