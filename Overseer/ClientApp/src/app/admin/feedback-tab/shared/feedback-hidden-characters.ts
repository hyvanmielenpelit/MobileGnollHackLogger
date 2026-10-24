const FORMAT = /^\p{Cf}$/u;
const CONTROL = /^\p{Cc}$/u;

/** Whether one code point is a character a reader cannot see. */
function isHidden(codePoint: number, character: string): boolean {
  if (codePoint === 0x09 || codePoint === 0x0a || codePoint === 0x0d) return false;
  if (codePoint === 0x2028 || codePoint === 0x2029) return true;
  if (codePoint >= 0xd800 && codePoint <= 0xdfff) return true;
  return FORMAT.test(character) || CONTROL.test(character);
}

function codePointLabel(codePoint: number): string {
  return 'U+' + codePoint.toString(16).toUpperCase().padStart(4, '0');
}

/**
 * The invisible characters in a text, as distinct `U+XXXX` labels in order of first appearance: every format (Cf)
 * character, every control (Cc) character except tab, line feed and carriage return, U+2028, U+2029 and a lone
 * surrogate. The server counterpart, `FeedbackMarkdownExport.RevealHidden`, shows the same class as `[U+XXXX]` in
 * the export.
 */
export function findHiddenCharacters(text: string | null | undefined): string[] {
  if (!text) return [];
  const found: string[] = [];
  for (const character of text) {
    const codePoint = character.codePointAt(0)!;
    if (!isHidden(codePoint, character)) continue;
    const label = codePointLabel(codePoint);
    if (!found.includes(label)) found.push(label);
  }
  return found;
}

/** Whether any of the texts holds a character {@link findHiddenCharacters} reports. */
export function hasHiddenCharacters(...texts: (string | null | undefined)[]): boolean {
  return texts.some(text => findHiddenCharacters(text).length > 0);
}

/** The distinct hidden characters of several texts, in order of first appearance. */
export function hiddenCharactersOf(...texts: (string | null | undefined)[]): string[] {
  const found: string[] = [];
  for (const text of texts) {
    for (const label of findHiddenCharacters(text)) {
      if (!found.includes(label)) found.push(label);
    }
  }
  return found;
}
