import { diffLines, MAX_DIFF_LINES } from './text-diff.util';

describe('text-diff.util', () => {
  it('marks identical text as equal', () => {
    const result = diffLines('a\nb', 'a\nb');
    expect(result.truncated).toBeFalse();
    expect(result.lines).toEqual([{ kind: 'equal', text: 'a' }, { kind: 'equal', text: 'b' }]);
  });

  it('finds an insertion', () => {
    expect(diffLines('a\nc', 'a\nb\nc').lines).toEqual([
      { kind: 'equal', text: 'a' },
      { kind: 'added', text: 'b' },
      { kind: 'equal', text: 'c' }
    ]);
  });

  it('finds a deletion', () => {
    expect(diffLines('a\nb\nc', 'a\nc').lines).toEqual([
      { kind: 'equal', text: 'a' },
      { kind: 'removed', text: 'b' },
      { kind: 'equal', text: 'c' }
    ]);
  });

  it('shows a replaced line as one removed and one added line', () => {
    expect(diffLines('a\nold\nc', 'a\nnew\nc').lines).toEqual([
      { kind: 'equal', text: 'a' },
      { kind: 'removed', text: 'old' },
      { kind: 'added', text: 'new' },
      { kind: 'equal', text: 'c' }
    ]);
  });

  it('ignores the difference between CRLF and LF', () => {
    expect(diffLines('a\r\nb', 'a\nb').lines.every(l => l.kind === 'equal')).toBeTrue();
  });

  it('treats an empty side as all added or all removed', () => {
    expect(diffLines('', 'x\ny').lines.map(l => l.kind)).toEqual(['added', 'added']);
    expect(diffLines('x', '').lines.map(l => l.kind)).toEqual(['removed']);
  });

  it('does not align inputs above the cap', () => {
    const big = Array.from({ length: MAX_DIFF_LINES + 1 }, (_, i) => `line ${i}`).join('\n');
    const result = diffLines(big, 'line 0');
    expect(result.truncated).toBeTrue();
    expect(result.lines.filter(l => l.kind === 'removed').length).toBe(MAX_DIFF_LINES + 1);
    expect(result.lines.filter(l => l.kind === 'added').length).toBe(1);
  });
});
