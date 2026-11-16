import { TranslateCardDto } from '../../core/api.types';
import { toSaveWordRequest } from './save-request';

function card(overrides: Partial<TranslateCardDto> = {}): TranslateCardDto {
  return {
    term: 'en hund',
    transcription: '[ˈhunˀ]',
    partOfSpeech: 'noun',
    gender: 'en',
    meanings: { en: ['dog', 'hound'], uk: ['собака'] },
    example: 'Hunden gør.',
    exampleTranslations: { en: 'The dog barks.', uk: 'Собака гавкає.' },
    note: 'Common gender.',
    existingWordId: null,
    ...overrides, // "..." copies the properties of `overrides` over the defaults above
  };
}

describe('toSaveWordRequest', () => {
  it('maps every field and tags the word "translated"', () => {
    expect(toSaveWordRequest(card(), ['en', 'uk'])).toEqual({
      term: 'en hund',
      transcription: '[ˈhunˀ]',
      partOfSpeech: 'noun',
      gender: 'en',
      example: 'Hunden gør.',
      notes: 'Common gender.',
      translations: [
        { languageCode: 'en', text: '<ol><li>dog</li><li>hound</li></ol>', exampleTranslation: 'The dog barks.' },
        { languageCode: 'uk', text: 'собака', exampleTranslation: 'Собака гавкає.' },
      ],
      tags: ['translated'],
    });
  });

  it('escapes the HTML fields but not the plain-text ones', () => {
    const request = toSaveWordRequest(
      card({
        term: '<b>en</b> hund',
        transcription: '<ipa>',
        partOfSpeech: 'a & b',
        gender: '<en>',
        meanings: { en: ['less <than>'] },
        example: 'Hund <i>gør</i>.',
        exampleTranslations: { en: 'Dog & cat.' },
        note: 'Not <script>.',
      }),
      ['en'],
    );

    expect(request.term).toBe('&lt;b&gt;en&lt;/b&gt; hund');
    expect(request.example).toBe('Hund &lt;i&gt;gør&lt;/i&gt;.');
    expect(request.notes).toBe('Not &lt;script&gt;.');
    expect(request.translations).toEqual([
      { languageCode: 'en', text: 'less &lt;than&gt;', exampleTranslation: 'Dog &amp; cat.' },
    ]);
    // Plain-text fields: the server stores and the UI renders them as text.
    expect(request.transcription).toBe('<ipa>');
    expect(request.partOfSpeech).toBe('a & b');
    expect(request.gender).toBe('<en>');
  });

  it('leaves out known languages without meanings', () => {
    const request = toSaveWordRequest(
      card({ meanings: { en: ['dog'], uk: [] }, exampleTranslations: { en: '', uk: 'Собака гавкає.' } }),
      ['en', 'uk', 'de'],
    );

    expect(request.translations).toEqual([{ languageCode: 'en', text: 'dog', exampleTranslation: null }]);
  });

  it('only sends the user\'s known languages, in their order', () => {
    const request = toSaveWordRequest(card(), ['uk']);

    expect(request.translations.map((t) => t.languageCode)).toEqual(['uk']);
  });

  it('sends null for empty optional fields', () => {
    const request = toSaveWordRequest(
      card({ transcription: '', partOfSpeech: ' ', gender: '', example: '', note: '  ' }),
      ['en'],
    );

    expect(request.transcription).toBeNull();
    expect(request.partOfSpeech).toBeNull();
    expect(request.gender).toBeNull();
    expect(request.example).toBeNull();
    expect(request.notes).toBeNull();
  });
});
