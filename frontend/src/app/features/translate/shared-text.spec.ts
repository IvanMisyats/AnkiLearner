import { MAX_TRANSLATE_LENGTH, extractSharedText } from './shared-text';

describe('extractSharedText', () => {
  it('returns plain text unchanged', () => {
    expect(extractSharedText('hund')).toBe('hund');
  });

  it('removes URLs', () => {
    expect(extractSharedText('Jeg har ondt i maven https://example.com/a?b=c')).toBe('Jeg har ondt i maven');
    expect(extractSharedText('see www.dr.dk/nyheder now')).toBe('see now');
  });

  it('collapses whitespace and trims', () => {
    expect(extractSharedText('  Jeg  har\n ondt\ti maven  ')).toBe('Jeg har ondt i maven');
  });

  it('strips wrapping straight, curly and angle quotes', () => {
    expect(extractSharedText('"hund"')).toBe('hund');
    expect(extractSharedText("'hund'")).toBe('hund');
    expect(extractSharedText('\u201Chund\u201D')).toBe('hund'); // English curly quotes
    expect(extractSharedText('\u2018hund\u2019')).toBe('hund'); // English single curly quotes
    expect(extractSharedText('\u201Ehund\u201C')).toBe('hund'); // German/Danish low-high quotes
    expect(extractSharedText('\u201Ahund\u2018')).toBe('hund'); // ... and their single form
    expect(extractSharedText('\u00BBhund\u00AB')).toBe('hund'); // Danish guillemets
    expect(extractSharedText('\u00ABhund\u00BB')).toBe('hund'); // French guillemets
    expect(extractSharedText('" hund "')).toBe('hund');
  });

  it('strips only one layer of quotes', () => {
    // Android wraps a shared selection in quotes once; quotes of the selection's own stay.
    expect(extractSharedText('""Hej" og "farvel""')).toBe('"Hej" og "farvel"');
    expect(extractSharedText('\u00AB "hund" \u00BB')).toBe('"hund"');
  });

  it('strips only a matching pair of quotes', () => {
    expect(extractSharedText('"hund\u201D')).toBe('"hund\u201D'); // straight, then curly
    expect(extractSharedText('\u201Dhund\u201C')).toBe('\u201Dhund\u201C'); // curly, reversed
    expect(extractSharedText('\u00BBhund\u00BB')).toBe('\u00BBhund\u00BB'); // same guillemet twice
    expect(extractSharedText('"hund\'')).toBe('"hund\''); // double, then single
  });

  it('keeps quotes that do not wrap the whole text', () => {
    expect(extractSharedText('"Hej," sagde han.')).toBe('"Hej," sagde han.');
    expect(extractSharedText("it's")).toBe("it's");
  });

  it('caps the text at the translate limit', () => {
    const long = 'a'.repeat(MAX_TRANSLATE_LENGTH + 50);
    expect(extractSharedText(long).length).toBe(MAX_TRANSLATE_LENGTH);
  });

  it('takes the first part that is non-empty after cleaning', () => {
    expect(extractSharedText(null, '  ', 'https://example.com', 'hund', 'kat')).toBe('hund');
    expect(extractSharedText(undefined, null)).toBe('');
  });
});
