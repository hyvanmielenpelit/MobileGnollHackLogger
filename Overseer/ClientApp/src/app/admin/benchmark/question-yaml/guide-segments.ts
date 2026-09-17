/**
 * Splits a help guide's Markdown into prose and fenced code, so each code sample can be rendered
 * by `app-code-block` rather than inside the sanitized HTML of the Markdown pipe.
 *
 * Fences are recognised only at column 0. The guide texts are static and nest no fence inside a
 * list, so the split is exact; `guide-segments.spec.ts` round-trips every guide tab.
 */

export type GuideSegment =
  | { kind: 'prose'; markdown: string }
  | { kind: 'code'; language: string; code: string };

const FENCE = /^```([\w-]*)\n([\s\S]*?)\n```[ \t]*$/gm;

export function splitGuideMarkdown(markdown: string): GuideSegment[] {
  const text = (markdown ?? '').replace(/\r\n?/g, '\n');
  const segments: GuideSegment[] = [];
  let last = 0;

  const pushProse = (chunk: string) => {
    if (chunk.trim() !== '') {
      segments.push({ kind: 'prose', markdown: chunk });
    }
  };

  for (const match of text.matchAll(FENCE)) {
    pushProse(text.slice(last, match.index));
    segments.push({ kind: 'code', language: match[1], code: match[2] });
    last = match.index! + match[0].length;
  }
  pushProse(text.slice(last));
  return segments;
}
