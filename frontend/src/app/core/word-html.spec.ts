import { escapeHtml, meaningsToHtml } from './word-html';

describe('escapeHtml', () => {
  it('escapes the characters that would turn text into markup', () => {
    expect(escapeHtml('<b>rock</b> & roll')).toBe('&lt;b&gt;rock&lt;/b&gt; &amp; roll');
  });

  it('escapes & first, so an existing entity is shown literally', () => {
    expect(escapeHtml('&lt;')).toBe('&amp;lt;');
  });

  it('leaves ordinary text alone', () => {
    expect(escapeHtml('en hund — собака')).toBe('en hund — собака');
  });
});

describe('meaningsToHtml', () => {
  it('returns an empty string for no meanings', () => {
    expect(meaningsToHtml([])).toBe('');
  });

  it('returns a single meaning as escaped text, without a list', () => {
    expect(meaningsToHtml(['less <than>'])).toBe('less &lt;than&gt;');
  });

  it('turns several meanings into an escaped numbered list', () => {
    expect(meaningsToHtml(['dog', 'a <b>hound</b>'])).toBe(
      '<ol><li>dog</li><li>a &lt;b&gt;hound&lt;/b&gt;</li></ol>',
    );
  });

  it('skips blank meanings', () => {
    expect(meaningsToHtml(['  ', 'dog', ''])).toBe('dog');
    expect(meaningsToHtml([' ', ''])).toBe('');
  });
});
