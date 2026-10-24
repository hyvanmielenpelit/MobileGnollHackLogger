import { findHiddenCharacters, hasHiddenCharacters, hiddenCharactersOf } from './feedback-hidden-characters';

const ch = (codePoint: number): string => String.fromCodePoint(codePoint);

/** The same code points as FeedbackMarkdownExportTests.RevealHidden_ShowsFormatAndControlCharactersAsCodePoints. */
const HIDDEN: readonly [number, string][] = [
  [0xe0041, 'U+E0041'],
  [0xe007f, 'U+E007F'],
  [0x202e, 'U+202E'],
  [0x2066, 'U+2066'],
  [0x200b, 'U+200B'],
  [0x200d, 'U+200D'],
  [0x2060, 'U+2060'],
  [0xfeff, 'U+FEFF'],
  [0x00ad, 'U+00AD'],
  [0x0000, 'U+0000'],
  [0x0007, 'U+0007'],
  [0x001b, 'U+001B'],
  [0x2028, 'U+2028'],
  [0x2029, 'U+2029']
];

describe('feedback-hidden-characters', () => {
  for (const [codePoint, label] of HIDDEN) {
    it(`reports ${label}`, () => {
      expect(findHiddenCharacters('a' + ch(codePoint) + 'b')).toEqual([label]);
      expect(hasHiddenCharacters('a' + ch(codePoint) + 'b')).toBe(true);
    });
  }

  it('reports a lone surrogate by its UTF-16 value', () => {
    expect(findHiddenCharacters('a' + String.fromCharCode(0xd83d) + 'b' + String.fromCharCode(0xdc00))).toEqual(['U+D83D', 'U+DC00']);
  });

  it('keeps line breaks, tabs, emoji, combining marks and every script', () => {
    for (const text of ['line\nbreak', 'tab\there', 'carriage\rreturn', 'emoji ' + ch(0x1f600), 'e' + ch(0x0301), ch(0x00e4), ch(0x6f22)]) {
      expect(findHiddenCharacters(text)).toEqual([]);
    }
    expect(hasHiddenCharacters('plain', null, undefined, '')).toBe(false);
  });

  it('lists distinct characters in order of first appearance across texts', () => {
    const text = ch(0x202e) + 'x' + ch(0x200b) + ch(0x202e);
    expect(findHiddenCharacters(text)).toEqual(['U+202E', 'U+200B']);
    expect(hiddenCharactersOf('plain', text, ch(0x2066) + ch(0x200b))).toEqual(['U+202E', 'U+200B', 'U+2066']);
  });

  it('reports a zero-width joiner inside an emoji sequence', () => {
    expect(findHiddenCharacters(ch(0x1f469) + ch(0x200d) + ch(0x1f4bb))).toEqual(['U+200D']);
  });
});
