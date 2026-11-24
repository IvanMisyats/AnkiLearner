import { countWeights, parseWeights } from './settings.component';

// The weights parser is a plain function, so it is tested without Angular's TestBed.
describe('parseWeights', () => {
  const fitted =
    '0.6124, 6.4925, 6.6843, 22.9712, 6.3995, 0.8529, 3.0992, 0.1689, 2.0540, 0.1066, 0.9812, ' +
    '1.4928, 0.0500, 0.2353, 1.6991, 0.3774, 2.3288, 0.6921, 0.1676, 0.0888, 0.1448';

  it('reads 21 comma-separated numbers, as Anki shows them', () => {
    const weights = parseWeights(fitted);
    expect(weights).toHaveLength(21);
    expect(weights![0]).toBe(0.6124);
    expect(weights![20]).toBe(0.1448);
  });

  it('accepts spaces and line breaks instead of commas', () => {
    expect(parseWeights(fitted.replaceAll(',', '\n'))).toHaveLength(21);
  });

  it('treats an empty box as "use the defaults"', () => {
    expect(parseWeights('')).toEqual([]);
    expect(parseWeights('   \n ')).toEqual([]);
  });

  it('rejects the wrong count or non-numbers', () => {
    expect(parseWeights('1, 2, 3')).toBeNull();
    expect(parseWeights(fitted.replace('0.6124', 'abc'))).toBeNull();
  });

  it('counts what was pasted, for the error message', () => {
    expect(countWeights('1, 2,3  4')).toBe(4);
    expect(countWeights('')).toBe(0);
  });
});
