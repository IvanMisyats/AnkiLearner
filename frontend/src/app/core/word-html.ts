// Helpers for putting plain text (e.g. AI output) into the word fields that hold HTML:
// term, example, notes, translation text and example translation. The server sanitizes
// those fields, but sanitizing would still keep a literal "<b>" from the AI as markup —
// escaping makes it show up as the text it is.

/** Escapes text so it displays literally inside an HTML field ("a < b" stays "a < b"). */
export function escapeHtml(text: string): string {
  return text
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;');
}

/**
 * Turns a list of meanings into one translation field: none → '', one → the escaped
 * text, several → a numbered HTML list (spec FR-W3). Blank meanings are skipped.
 */
export function meaningsToHtml(meanings: string[]): string {
  const items = meanings.map((m) => m.trim()).filter((m) => m.length > 0);
  if (items.length === 0) return '';
  if (items.length === 1) return escapeHtml(items[0]);
  return `<ol>${items.map((m) => `<li>${escapeHtml(m)}</li>`).join('')}</ol>`;
}
