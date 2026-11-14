// The body font as the global stylesheets declare it: fonts.scss and styles.scss reach this page
// through the test target's build.

const BODY_FAMILY = 'Inter';

function faces(): FontFace[] {
  const all: FontFace[] = [];
  document.fonts.forEach(face => all.push(face));
  return all;
}

function unquote(family: string): string {
  return family.replace(/^["']|["']$/g, '');
}

/** The first family of a computed `font-family`; Chromium leaves a plain identifier unquoted. */
function firstFamily(fontFamily: string): string {
  return unquote(fontFamily.split(',')[0].trim());
}

/** `unicodeRange` as code-point ranges; Chromium reports it normalized, as in `U+0-FF, U+131`. */
function codePointRanges(unicodeRange: string): [number, number][] {
  return unicodeRange.split(',').map(part => {
    const spec = part.trim().replace(/^U\+/i, '');
    if (spec.includes('?')) {
      return [parseInt(spec.replace(/\?/g, '0'), 16), parseInt(spec.replace(/\?/g, 'F'), 16)];
    }
    const [from, to] = spec.split('-');
    return [parseInt(from, 16), parseInt(to ?? from, 16)];
  });
}

function covers(face: FontFace, codePoint: number): boolean {
  return codePointRanges(face.unicodeRange).some(([from, to]) => codePoint >= from && codePoint <= to);
}

describe('the body font', () => {
  it('is declared as four variable faces, upright and italic, with the line-box overrides', () => {
    const body = faces().filter(face => unquote(face.family) === BODY_FAMILY);

    expect(body.length).toBe(4);
    for (const style of ['normal', 'italic']) {
      const ofStyle = body.filter(face => face.style === style);
      expect(ofStyle.length, style).toBe(2);
      expect(ofStyle.filter(face => covers(face, 0x41)).length, style).toBe(1);
    }
    for (const face of body) {
      expect(face.weight).toBe('100 900');
      expect(face.ascentOverride).toBe('98.7%');
      expect(face.descentOverride).toBe('21.3%');
      expect(face.lineGapOverride).toBe('0%');
    }
  });

  it('has no Lato face', () => {
    expect(faces().filter(face => unquote(face.family) === 'Lato')).toEqual([]);
  });

  it('is the family of the body', () => {
    expect(firstFamily(getComputedStyle(document.body).fontFamily)).toBe(BODY_FAMILY);
  });

  it('sets markdown bold at 700 and emphasis in italic, in the body family', () => {
    const host = document.createElement('div');
    host.className = 'markdown-body';
    host.innerHTML = '<p>a <strong>b</strong> <em>c</em></p>';
    document.body.appendChild(host);
    try {
      const strong = getComputedStyle(host.querySelector('strong')!);
      const em = getComputedStyle(host.querySelector('em')!);

      expect(strong.fontWeight).toBe('700');
      expect(em.fontStyle).toBe('italic');
      expect(firstFamily(strong.fontFamily)).toBe(BODY_FAMILY);
      expect(firstFamily(em.fontFamily)).toBe(BODY_FAMILY);
    } finally {
      host.remove();
    }
  });

  it('loads its bold upright and bold italic faces', async () => {
    for (const font of [`700 15px "${BODY_FAMILY}"`, `italic 700 15px "${BODY_FAMILY}"`]) {
      const loaded = await document.fonts.load(font);
      expect(loaded.some(face => face.status === 'loaded'), font).toBe(true);
    }
  });

  it('keeps the normal line height on the body', () => {
    expect(getComputedStyle(document.body).lineHeight).toBe('normal');
  });

  it('turns contextual alternates off on the body', () => {
    expect(getComputedStyle(document.body).fontVariantLigatures).toBe('no-contextual');
  });

  it('sets table cells in tabular digits', () => {
    const table = document.createElement('table');
    table.innerHTML = '<tbody><tr><td>1</td></tr></tbody>';
    document.body.appendChild(table);
    try {
      expect(getComputedStyle(table.querySelector('td')!).fontVariantNumeric).toBe('tabular-nums');
    } finally {
      table.remove();
    }
  });

  it('renders -> as typed, not as an arrow', async () => {
    await document.fonts.load(`15px "${BODY_FAMILY}"`);
    // The span sits in a block host: as a direct child of the flex body it would stretch.
    const width = (text: string): number => {
      const host = document.createElement('div');
      const span = document.createElement('span');
      span.style.fontSize = '15px';
      span.style.whiteSpace = 'pre';
      span.textContent = text;
      host.appendChild(span);
      document.body.appendChild(host);
      try {
        return span.getBoundingClientRect().width;
      } finally {
        host.remove();
      }
    };

    expect(Math.abs(width('->') - (width('-') + width('>')))).toBeLessThan(0.5);
  });
});
