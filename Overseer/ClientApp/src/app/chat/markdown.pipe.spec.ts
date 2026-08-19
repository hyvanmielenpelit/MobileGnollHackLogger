import { MarkdownPipe } from './markdown.pipe';

describe('MarkdownPipe', () => {
  let pipe: MarkdownPipe;

  beforeEach(() => {
    pipe = new MarkdownPipe();
  });

  it('should insert newlines before and after squished code blocks', () => {
    const input = 'overlong lines.```c\nint main() {}\n```Done.';
    const result = pipe.transform(input);
    // Should contain a <pre><code> block, not raw backticks
    expect(result).toContain('<pre>');
    expect(result).toContain('<code');
    expect(result).not.toContain('```');
  });
});
