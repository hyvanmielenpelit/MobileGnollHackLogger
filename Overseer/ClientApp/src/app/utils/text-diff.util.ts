/** One line of a line-level diff. */
export interface DiffLine {
  kind: 'equal' | 'added' | 'removed';
  text: string;
}

export interface DiffResult {
  lines: DiffLine[];
  /** True when an input exceeded {@link MAX_DIFF_LINES} and the diff is one removed and one added block. */
  truncated: boolean;
}

/** Per side. The LCS table is O(n·m) in time and memory, so larger inputs are not aligned. */
export const MAX_DIFF_LINES = 4000;

/** A line-level diff by longest common subsequence. Line endings are normalized to LF first. */
export function diffLines(before: string, after: string): DiffResult {
  const a = splitLines(before);
  const b = splitLines(after);

  if (a.length > MAX_DIFF_LINES || b.length > MAX_DIFF_LINES) {
    return {
      lines: [
        ...a.map(text => ({ kind: 'removed' as const, text })),
        ...b.map(text => ({ kind: 'added' as const, text }))
      ],
      truncated: true
    };
  }

  const n = a.length;
  const m = b.length;
  // lcs[i][j] is the LCS length of a[i..] and b[j..], stored row-major in one typed array.
  const width = m + 1;
  const lcs = new Uint32Array((n + 1) * width);
  for (let i = n - 1; i >= 0; i--) {
    for (let j = m - 1; j >= 0; j--) {
      lcs[i * width + j] = a[i] === b[j]
        ? lcs[(i + 1) * width + j + 1] + 1
        : Math.max(lcs[(i + 1) * width + j], lcs[i * width + j + 1]);
    }
  }

  const lines: DiffLine[] = [];
  let i = 0;
  let j = 0;
  while (i < n && j < m) {
    if (a[i] === b[j]) {
      lines.push({ kind: 'equal', text: a[i] });
      i++;
      j++;
    } else if (lcs[(i + 1) * width + j] >= lcs[i * width + j + 1]) {
      lines.push({ kind: 'removed', text: a[i] });
      i++;
    } else {
      lines.push({ kind: 'added', text: b[j] });
      j++;
    }
  }
  while (i < n) {
    lines.push({ kind: 'removed', text: a[i++] });
  }
  while (j < m) {
    lines.push({ kind: 'added', text: b[j++] });
  }

  return { lines, truncated: false };
}

function splitLines(text: string): string[] {
  if (!text) {
    return [];
  }
  return text.replace(/\r\n?/g, '\n').split('\n');
}
