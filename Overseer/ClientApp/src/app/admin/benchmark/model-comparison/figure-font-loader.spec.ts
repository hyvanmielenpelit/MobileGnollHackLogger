import {
  FIGURE_FONT_LOAD_WEIGHTS,
  FigureFontSource,
  ensureFigureFont,
  resetFigureFontCache
} from './figure-font-loader';

describe('figure-font-loader', () => {
  beforeEach(() => resetFigureFontCache());
  afterEach(() => resetFigureFontCache());

  function source(result: (font: string) => Promise<readonly unknown[]>): FigureFontSource & { calls: string[] } {
    const calls: string[] = [];
    return { calls, load: (font: string) => { calls.push(font); return result(font); } };
  }

  it('resolves the Overseer default at once without loading anything', async () => {
    const fonts = source(() => Promise.resolve([{}]));
    await expect(ensureFigureFont('default', { fonts })).resolves.toEqual(true);
    expect(fonts.calls).toEqual([]);
  });

  it('loads every offered weight of a bundled family', async () => {
    const fonts = source(() => Promise.resolve([{}]));
    await expect(ensureFigureFont('inter', { fonts })).resolves.toEqual(true);
    expect(fonts.calls).toEqual(FIGURE_FONT_LOAD_WEIGHTS.map(weight => `${weight} 16px "Inter"`));
  });

  it('caches the result per family', async () => {
    const fonts = source(() => Promise.resolve([{}]));
    const first = ensureFigureFont('roboto', { fonts });
    const second = ensureFigureFont('roboto', { fonts });
    expect(second).toBe(first);
    await first;
    expect(fonts.calls.length).toBe(FIGURE_FONT_LOAD_WEIGHTS.length);
  });

  it('resolves false where a weight has no face or the load fails', async () => {
    await expect(ensureFigureFont('geist', { fonts: source(f => Promise.resolve(f.startsWith('700') ? [] : [{}])) })).resolves.toEqual(false);
    await expect(ensureFigureFont('open-sans', { fonts: source(() => Promise.reject(new Error('network'))) })).resolves.toEqual(false);
  });

  it('resolves false without a font loading API', async () => {
    await expect(ensureFigureFont('ibm-plex-sans', { fonts: null })).resolves.toEqual(false);
  });

  it('resolves false when the load outlasts the timeout', async () => {
    const never = source(() => new Promise<readonly unknown[]>(() => undefined));
    await expect(ensureFigureFont('source-sans-3', { fonts: never, timeoutMs: 10 })).resolves.toEqual(false);
  });
});
