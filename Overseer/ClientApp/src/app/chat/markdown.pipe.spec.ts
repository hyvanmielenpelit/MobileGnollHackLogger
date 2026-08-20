import { MarkdownPipe } from './markdown.pipe';
import { DomSanitizer } from '@angular/platform-browser';

describe('MarkdownPipe', () => {
  let pipe: MarkdownPipe;
  let mockSanitizer: jasmine.SpyObj<DomSanitizer>;

  beforeEach(() => {
    mockSanitizer = jasmine.createSpyObj('DomSanitizer', ['bypassSecurityTrustHtml']);
    mockSanitizer.bypassSecurityTrustHtml.and.callFake((html: string) => html as any);
    pipe = new MarkdownPipe(mockSanitizer);
  });

  it('should insert newlines before and after squished code blocks', () => {
    const input = 'overlong lines.```c\nint main() {}\n```Done.';
    const result = pipe.transform(input);
    // Should contain a <pre><code> block, not raw backticks
    expect(result).toContain('<pre>');
    expect(result).toContain('<code');
    expect(result).not.toContain('```');
  });

  it('should render LaTeX display math \\[ ... \\] with KaTeX', () => {
    const input = `The GnollHack score formula is:

\\[
\\textbf{Score} = \\text{BaseScore} \\times \\text{AscensionMultiplier} \\times \\text{DifficultyMultiplier} \\times \\text{ModernMultiplier}
\\]

Where:`;
    const result = pipe.transform(input);
    expect(result).toContain('katex-display');
    expect(result).toContain('katex-html');
    expect(result).toContain('Score');
    expect(result).not.toContain('\\[');
    expect(result).not.toContain('\\]');
  });

  it('should render inline LaTeX math \\( ... \\) with KaTeX', () => {
    const input = 'Here is inline math \\( E = mc^2 \\) in text.';
    const result = pipe.transform(input);
    expect(result).toContain('katex');
    expect(result).not.toContain('\\( E = mc^2 \\)');
  });

  it('should render double dollar $$ ... $$ display math with KaTeX', () => {
    const input = '$$ a^2 + b^2 = c^2 $$';
    const result = pipe.transform(input);
    expect(result).toContain('katex-display');
    expect(result).not.toContain('$$');
  });

  it('should render single dollar $ ... $ inline math with KaTeX', () => {
    const input = 'The value $x = 42$ is true.';
    const result = pipe.transform(input);
    expect(result).toContain('katex');
    expect(result).not.toContain('$x = 42$');
  });

  it('should not treat currency amounts as math', () => {
    const input = 'The item costs $50 now and $100 later.';
    const result = pipe.transform(input);
    expect(result).toContain('$50');
    expect(result).toContain('$100');
    expect(result).not.toContain('katex');
  });

  it('should protect math expressions containing dots from squished sentence splitting', () => {
    const input = '\\[ \\text{Score.Total} = 100 \\]';
    const result = pipe.transform(input);
    expect(result).toContain('katex-display');
    expect(result).not.toContain('\n\nTotal');
  });

  it('should render complex LaTeX array formula table', () => {
    const input = `Understood.

\\[
C = 3.5\\,\\mathrm{lb}\\,(S+\\mathrm{Con}) + 3\\,\\mathrm{lb}
\\]

where \\(C\\) is carrying capacity, \\(S\\) is Strength, and \\(\\mathrm{Con}\\) is Constitution.

Let \\(E\\) represent current carried weight:

\\[
\\begin{array}{c|c|c}
\\text{Encumbrance level} & \\text{Condition} & \\text{Movement speed} \\\\ \\hline
\\text{Unencumbered} & E \\le C & 1 \\\\[2pt]
\\text{Burdened} & C < E < 1.5C & \\frac{3}{4} \\\\[2pt]
\\text{Stressed} & 1.5C \\le E < 2C & \\frac{1}{2} \\\\[2pt]
\\text{Strained} & 2C \\le E < 2.5C & \\frac{1}{4} \\\\[2pt]
\\text{Overtaxed} & 2.5C \\le E < 3C & \\frac{1}{8} \\\\[2pt]
\\text{Overloaded} & E \\ge 3C & \\text{Cannot move}
\\end{array}
\\]`;
    const result = pipe.transform(input);
    expect(result).toContain('Encumbrance level');
    expect(result).toContain('Unencumbered');
    expect(result).toContain('Cannot move');

    // Mount to DOM to test actual browser rendering
    const container = document.createElement('div');
    container.className = 'markdown-body';
    container.innerHTML = result as string;
    document.body.appendChild(container);

    const katexHtmls = container.querySelectorAll('.katex-html');
    expect(katexHtmls.length).toBe(6); // 2 display formulas + 4 inline math

    const arrayFormula = katexHtmls[5] as HTMLElement;
    const normalizedText = arrayFormula.innerText.replace(/\u00a0/g, ' ');
    expect(normalizedText).toContain('Encumbrance level');
    expect(normalizedText).toContain('Unencumbered');
    expect(normalizedText).toContain('Burdened');
    expect(normalizedText).toContain('Cannot move');

    document.body.removeChild(container);
  });

  it('should render display math with single newline before bracket and \\dfrac / \\leq', () => {
    const input = `Yes—this is a clearer format. A polished version is:
\\[
\\begin{array}{c|c|c}
\\text{State} & \\text{Condition} & \\text{Movement speed} \\\\ \\hline
\\text{Unencumbered}
    & E \\leq C
    & 1 \\\\[2pt]
\\text{Burdened}
    & C < E < 1.5C
    & \\dfrac{3}{4} \\\\[2pt]
\\text{Stressed}
    & 1.5C \\leq E < 2C
    & \\dfrac{1}{2} \\\\[2pt]
\\text{Strained}
    & 2C \\leq E < 2.5C
    & \\dfrac{1}{4} \\\\[2pt]
\\text{Overtaxed}
    & 2.5C \\leq E < 3C
    & \\dfrac{1}{8} \\\\[2pt]
\\text{Overloaded}
    & E \\geq 3C
    & \\text{Cannot move}
\\end{array}
\\]
Here, \\(E\\) denotes carried weight and \\(C\\) denotes carrying capacity.`;
    const result = pipe.transform(input);
    expect(result).not.toContain('<p>Yes—this is a clearer format. A polished version is: <span class="katex-display">');
    expect(result).toContain('State');
    expect(result).toContain('Unencumbered');
    expect(result).toContain('Cannot move');

    const container = document.createElement('div');
    container.className = 'markdown-body';
    container.innerHTML = result as string;
    document.body.appendChild(container);

    const katexHtmls = container.querySelectorAll('.katex-html');
    expect(katexHtmls.length).toBe(3); // 1 display array + 2 inline (E, C)

    const arrayFormula = katexHtmls[0] as HTMLElement;
    const normalizedText = arrayFormula.innerText.replace(/\u00a0/g, ' ');
    expect(normalizedText).toContain('State');
    expect(normalizedText).toContain('Unencumbered');
    expect(normalizedText).toContain('Cannot move');

    document.body.removeChild(container);
  });

  it('should render bare \\begin{array} environments without outer brackets', () => {
    const input = `Here is a matrix:
\\begin{array}{cc}
1 & 2 \\\\
3 & 4
\\end{array}
End of matrix.`;
    const result = pipe.transform(input);
    expect(result).toContain('katex-display');
    expect(result).toContain('katex-html');
  });
});

