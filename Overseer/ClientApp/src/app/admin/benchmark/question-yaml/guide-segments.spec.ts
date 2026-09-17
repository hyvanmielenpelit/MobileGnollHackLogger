import { HUMAN_GUIDE_TABS } from './question-yaml-format';
import { SUITE_GUIDE_TABS } from './suite-yaml-guide';
import { GuideSegment, splitGuideMarkdown } from './guide-segments';

/** Reassembles the Markdown a segment list was split from. */
function join(segments: GuideSegment[]): string {
  return segments.map(s => s.kind === 'prose' ? s.markdown : '```' + s.language + '\n' + s.code + '\n```').join('\n\n');
}

/** The Markdown with the whitespace-only runs between segments removed, as the splitter drops them. */
function squeeze(text: string): string {
  return text.replace(/\n{2,}/g, '\n\n').trim();
}

describe('splitGuideMarkdown', () => {
  it('keeps prose and code in order, with the language captured', () => {
    const segments = splitGuideMarkdown('Intro\n\n```yaml\na: 1\n```\n\nMiddle\n\n```\nplain\n```\n\nEnd\n');
    expect(segments.map(s => s.kind)).toEqual(['prose', 'code', 'prose', 'code', 'prose']);
    expect(segments[1]).toEqual({ kind: 'code', language: 'yaml', code: 'a: 1' });
    expect(segments[3]).toEqual({ kind: 'code', language: '', code: 'plain' });
  });

  it('drops empty prose between adjacent blocks and at the ends', () => {
    const segments = splitGuideMarkdown('```text\none\n```\n\n```text\ntwo\n```\n');
    expect(segments.map(s => s.kind)).toEqual(['code', 'code']);
  });

  it('matches fences only at column 0', () => {
    const segments = splitGuideMarkdown('- item\n  ```yaml\n  a: 1\n  ```\n');
    expect(segments.map(s => s.kind)).toEqual(['prose']);
  });

  it('keeps the code byte for byte, leading spaces included', () => {
    const format = SUITE_GUIDE_TABS.find(t => t.id === 'format')!.markdown;
    const codes = splitGuideMarkdown(format).filter(s => s.kind === 'code').map(s => (s as { code: string }).code);
    const right = codes.find(c => c.includes('Map grid:') && c.includes('0123456789012345'))!;
    expect(right).toContain('\n           0123456789012345\n        8  |..........@...|');
    expect(right.startsWith('    text: |')).toBeTrue();
    const wrong = codes.find(c => c.endsWith('\n    Map grid:'))!;
    expect(wrong).toBe('    text: |\n      GnollHack 4.2.0 Build 47\n    Map grid:');
  });

  it('round-trips every guide tab of both variants', () => {
    for (const tab of [...HUMAN_GUIDE_TABS, ...SUITE_GUIDE_TABS]) {
      const segments = splitGuideMarkdown(tab.markdown);
      expect(segments.length).withContext(tab.id).toBeGreaterThan(0);
      expect(squeeze(join(segments))).withContext(tab.id).toBe(squeeze(tab.markdown));
    }
  });
});
