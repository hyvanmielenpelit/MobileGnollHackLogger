// The body font as the global stylesheets declare it: fonts.scss and styles.scss reach this page
// through the test target's build.

const BODY_FAMILY = 'Lato';

/** The descriptor weights of each style's faces; 700 is served by Heavy and 800 900 by Black. */
const DESCRIPTOR_WEIGHTS = ['300', '400', '500', '600', '700', '800 900'];

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

/**
 * A span of `text`, measured inside a block host so it does not stretch: 15 px in the body font
 * unless a size, a family or a weight is given.
 */
function spanWidth(text: string, sizePx = 15, family?: string, weight?: string): number {
  const host = document.createElement('div');
  const span = document.createElement('span');
  span.style.fontSize = `${sizePx}px`;
  span.style.whiteSpace = 'pre';
  if (family) {
    span.style.fontFamily = `"${family}"`;
  }
  if (weight) {
    span.style.fontWeight = weight;
  }
  span.textContent = text;
  host.appendChild(span);
  document.body.appendChild(host);
  try {
    return span.getBoundingClientRect().width;
  } finally {
    host.remove();
  }
}

describe('the body font', () => {
  it('is declared as 48 faces, four subsets per weight and style, with no metric overrides', () => {
    const body = faces().filter(face => unquote(face.family) === BODY_FAMILY);

    expect(body.length).toBe(48);
    for (const style of ['normal', 'italic']) {
      const ofStyle = body.filter(face => face.style === style);
      expect(ofStyle.length, style).toBe(24);
      expect([...new Set(ofStyle.map(face => face.weight))].sort(), style).toEqual([...DESCRIPTOR_WEIGHTS].sort());
      for (const weight of DESCRIPTOR_WEIGHTS) {
        const ofWeight = ofStyle.filter(face => face.weight === weight);
        const label = `${style} ${weight}`;
        expect(ofWeight.length, label).toBe(4);
        expect(ofWeight.filter(face => covers(face, 0x41)).length, `${label} U+0041`).toBe(1);
        // Google's math range repeats the Greek letters, so U+03A3 is in the greek and math faces;
        // U+0386 is in the greek face alone and U+2264 in the math face alone.
        expect(ofWeight.filter(face => covers(face, 0x386)).length, `${label} U+0386`).toBe(1);
        expect(ofWeight.some(face => covers(face, 0x3a3)), `${label} U+03A3`).toBe(true);
        expect(ofWeight.filter(face => covers(face, 0x2264)).length, `${label} U+2264`).toBe(1);
      }
    }
    for (const face of body) {
      expect(face.ascentOverride).toBe('normal');
      expect(face.descentOverride).toBe('normal');
      expect(face.lineGapOverride).toBe('normal');
    }
  });

  it('renders 600 as Semibold, 700 and bold as Heavy, and 800 and 900 as Black', async () => {
    const sample = 'Hamburgefonstiv';
    const probes = [
      { family: 'Lato Probe Semibold', file: 'lato2-normal-600-latin.woff2' },
      { family: 'Lato Probe Heavy', file: 'lato2-normal-800-latin.woff2' },
      { family: 'Lato Probe Black', file: 'lato2-normal-900-latin.woff2' }
    ].map(probe => ({ ...probe, face: new FontFace(probe.family, `url("/fonts/${probe.file}")`) }));
    try {
      for (const probe of probes) {
        await probe.face.load();
        document.fonts.add(probe.face);
      }
      for (const weight of ['600', '700', 'bold', '800', '900']) {
        await document.fonts.load(`${weight} 100px "${BODY_FAMILY}"`);
      }
      const [semibold, heavy, black] = probes.map(probe => spanWidth(sample, 100, probe.family));

      expect(Math.abs(heavy - semibold), 'Heavy against Semibold').toBeGreaterThan(4);
      expect(Math.abs(black - heavy), 'Black against Heavy').toBeGreaterThan(4);
      expect(Math.abs(black - semibold), 'Black against Semibold').toBeGreaterThan(4);

      const cases: [string, number][] = [['600', semibold], ['700', heavy], ['bold', heavy], ['800', black], ['900', black]];
      for (const [weight, expected] of cases) {
        expect(Math.abs(spanWidth(sample, 100, BODY_FAMILY, weight) - expected), `authored ${weight}`).toBeLessThan(0.5);
      }
    } finally {
      for (const probe of probes) {
        document.fonts.delete(probe.face);
      }
    }
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

  it('gives a normal line its own metrics: 18 px at 15 px', async () => {
    await document.fonts.load(`15px "${BODY_FAMILY}"`);
    const line = document.createElement('div');
    line.style.fontSize = '15px';
    line.style.lineHeight = 'normal';
    line.textContent = 'Ågy';
    document.body.appendChild(line);
    try {
      expect(line.getBoundingClientRect().height).toBe(18);
    } finally {
      line.remove();
    }
  });

  it('renders -> as typed, not as an arrow', async () => {
    await document.fonts.load(`15px "${BODY_FAMILY}"`);

    expect(Math.abs(spanWidth('->') - (spanWidth('-') + spanWidth('>')))).toBeLessThan(0.5);
  });

  it('sets digits tabular by default', async () => {
    await document.fonts.load(`15px "${BODY_FAMILY}"`);

    expect(Math.abs(spanWidth('1111') - spanWidth('0000'))).toBeLessThan(0.5);
  });
});
