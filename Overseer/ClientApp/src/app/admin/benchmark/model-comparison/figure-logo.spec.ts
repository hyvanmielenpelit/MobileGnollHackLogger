import {
  FIGURE_LOGO_ASSETS,
  FIGURE_LOGO_MAX_WIDTH_SHARE,
  FigureLogo,
  drawFigureLogo,
  ensureFigureLogo,
  figureLogoAspect,
  figureLogoBox,
  figureLogoIo,
  resetFigureLogoCache
} from './figure-logo';

describe('figure-logo', () => {
  beforeEach(() => resetFigureLogoCache());
  afterEach(() => resetFigureLogoCache());

  function stubImage(): HTMLCanvasElement {
    const canvas = document.createElement('canvas');
    canvas.width = 4;
    canvas.height = 1;
    return canvas;
  }

  function logo(heightPx: number, aspectRatio: number): FigureLogo {
    return { image: stubImage(), aspectRatio, heightPx };
  }

  it('loads each variant from its own high-resolution asset', async () => {
    const image = stubImage();
    const load = spyOn(figureLogoIo, 'loadImage').and.resolveTo(image);
    await expectAsync(ensureFigureLogo('wide')).toBeResolvedTo(image);
    await expectAsync(ensureFigureLogo('square')).toBeResolvedTo(image);
    expect(load.calls.allArgs()).toEqual([
      ['/img/gnollbench/gnollbench-wide-v3-h850.webp'],
      ['/img/gnollbench/gnollbench-logo-v3-843.webp']
    ]);
    expect(FIGURE_LOGO_ASSETS.wide.url).toBe('/img/gnollbench/gnollbench-wide-v3-h850.webp');
    expect(FIGURE_LOGO_ASSETS.square.url).toBe('/img/gnollbench/gnollbench-logo-v3-843.webp');
  });

  it('reuses the promise of a variant it has loaded', async () => {
    const load = spyOn(figureLogoIo, 'loadImage').and.resolveTo(stubImage());
    const first = ensureFigureLogo('wide');
    const second = ensureFigureLogo('wide');
    expect(second).toBe(first);
    await first;
    expect(ensureFigureLogo('wide')).toBe(first);
    expect(load).toHaveBeenCalledTimes(1);
  });

  it('does not cache a failed load, so the next call tries again', async () => {
    const image = stubImage();
    const load = spyOn(figureLogoIo, 'loadImage').and.returnValues(Promise.resolve(null), Promise.resolve(image));
    await expectAsync(ensureFigureLogo('square')).toBeResolvedTo(null);
    await expectAsync(ensureFigureLogo('square')).toBeResolvedTo(image);
    expect(load).toHaveBeenCalledTimes(2);
  });

  it('resolves null rather than rejecting when the load throws or rejects', async () => {
    const load = spyOn(figureLogoIo, 'loadImage').and.rejectWith(new Error('404'));
    await expectAsync(ensureFigureLogo('wide')).toBeResolvedTo(null);
    load.and.throwError('no image');
    await expectAsync(ensureFigureLogo('wide')).toBeResolvedTo(null);
    expect(load).toHaveBeenCalledTimes(2);
  });

  it('gives each variant its asset proportions', () => {
    expect(figureLogoAspect('wide')).toBe(3248 / 850);
    expect(figureLogoAspect('square')).toBe(1);
  });

  it('has no box without a logo or for a non-positive height or aspect', () => {
    expect(figureLogoBox(null, 800)).toBeNull();
    expect(figureLogoBox(undefined, 800)).toBeNull();
    expect(figureLogoBox(logo(0, 2), 800)).toBeNull();
    expect(figureLogoBox(logo(-4, 2), 800)).toBeNull();
    expect(figureLogoBox(logo(48, 0), 800)).toBeNull();
    expect(figureLogoBox(logo(48, Number.NaN), 800)).toBeNull();
  });

  it('sizes the box at the logo height and proportions', () => {
    expect(figureLogoBox(logo(48, 1), 800)).toEqual({ width: 48, height: 48 });
    const wide = figureLogoBox(logo(48, 3248 / 850), 800)!;
    expect(wide.height).toBe(48);
    expect(wide.width).toBeCloseTo(48 * 3248 / 850, 6);
  });

  it('scales the box down, height with it, to its share of the content width', () => {
    const box = figureLogoBox(logo(96, 3248 / 850), 600)!;
    expect(box.width).toBeCloseTo(600 * FIGURE_LOGO_MAX_WIDTH_SHARE, 6);
    expect(box.height).toBeCloseTo(600 * FIGURE_LOGO_MAX_WIDTH_SHARE / (3248 / 850), 6);
    expect(box.height).toBeLessThan(96);
  });

  it('draws the image into the box with high-quality smoothing', () => {
    const context = document.createElement('canvas').getContext('2d')!;
    const drawn = logo(48, 2);
    const drawImage = spyOn(context, 'drawImage').and.callThrough();
    const save = spyOn(context, 'save').and.callThrough();
    const restore = spyOn(context, 'restore').and.callThrough();
    context.imageSmoothingQuality = 'low';
    drawFigureLogo(context, drawn, { width: 96, height: 48 }, 10, 20);
    expect(drawImage).toHaveBeenCalledTimes(1);
    expect(drawImage.calls.argsFor(0) as unknown[]).toEqual([drawn.image, 10, 20, 96, 48]);
    expect(save).toHaveBeenCalledTimes(1);
    expect(restore).toHaveBeenCalledTimes(1);
    expect(context.imageSmoothingQuality).toBe('low');
  });
});
