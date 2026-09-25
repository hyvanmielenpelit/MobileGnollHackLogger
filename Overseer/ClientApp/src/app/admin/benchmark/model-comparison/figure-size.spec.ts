import {
  DEFAULT_FIGURE_RESOLUTION_ID,
  FIGURE_SIZE_STORAGE_KEY,
  FIT_RESOLUTION_ID,
  FigureSizeSettings,
  TABLE_IMAGE_SIZE_STORAGE_KEY,
  clampDensityPercent,
  clampExportDimension,
  defaultFigureSize,
  defaultTableImageSize,
  readStoredFigureSize,
  readStoredTableImageSize,
  resolveSizeDensity,
  resolveSizeResolution,
  sameFigureSize,
  sizeDimensionsLabel,
  sizeErrors,
  sizeReadout,
  writeStoredFigureSize,
  writeStoredTableImageSize
} from './figure-size';

describe('figure-size', () => {
  beforeEach(() => {
    localStorage.removeItem(FIGURE_SIZE_STORAGE_KEY);
  });

  afterEach(() => {
    localStorage.removeItem(FIGURE_SIZE_STORAGE_KEY);
  });

  function store(value: unknown): void {
    localStorage.setItem(FIGURE_SIZE_STORAGE_KEY, typeof value === 'string' ? value : JSON.stringify(value));
  }

  it('defaults to Full HD at the display’s own density and 100 % text', () => {
    const size = defaultFigureSize(2);
    expect(size.resolutionId).toBe('fullhd');
    expect(DEFAULT_FIGURE_RESOLUTION_ID).toBe('fullhd');
    expect(size.densitySelection).toBe(2);
    expect(size.customDensityPercent).toBe(200);
    expect(size.textScalePercent).toBe(100);
    expect(size.customWidthPx).toBe(1920);
    expect(size.customHeightPx).toBe(1080);
  });

  it('holds a display density no listed step matches in the custom field', () => {
    const size = defaultFigureSize(2.2);
    expect(size.densitySelection).toBe('custom');
    expect(size.customDensityPercent).toBe(220);
  });

  it('reads the default where nothing is stored', () => {
    expect(readStoredFigureSize(1.5)).toEqual(defaultFigureSize(1.5));
  });

  it('round-trips every field', () => {
    const size: FigureSizeSettings = {
      resolutionId: 'custom',
      customWidthPx: 1600,
      customHeightPx: 900,
      densitySelection: 'custom',
      customDensityPercent: 175,
      textScalePercent: 150
    };
    writeStoredFigureSize(size);

    expect(JSON.parse(localStorage.getItem(FIGURE_SIZE_STORAGE_KEY)!).version).toBe(1);
    expect(readStoredFigureSize(1)).toEqual(size);
    expect(sameFigureSize(readStoredFigureSize(1), size)).toBeTrue();

    const preset: FigureSizeSettings = { ...defaultFigureSize(1), resolutionId: 'a4p', densitySelection: 3 };
    writeStoredFigureSize(preset);
    expect(readStoredFigureSize(1)).toEqual(preset);
  });

  it('reads the retired On-screen size, and any unknown id, as Full HD', () => {
    store({ version: 1, ...defaultFigureSize(1), resolutionId: 'onscreen', textScalePercent: 120 });
    const migrated = readStoredFigureSize(1);
    expect(migrated.resolutionId).toBe('fullhd');
    // The other fields are read on their own merits.
    expect(migrated.textScalePercent).toBe(120);

    store({ version: 1, ...defaultFigureSize(1), resolutionId: 'eight-k' });
    expect(readStoredFigureSize(1).resolutionId).toBe('fullhd');

    store({ version: 1, ...defaultFigureSize(1), resolutionId: 42 });
    expect(readStoredFigureSize(1).resolutionId).toBe('fullhd');
  });

  it('falls back field by field on a value out of range or of the wrong kind', () => {
    store({
      version: 1,
      resolutionId: 'uhd',
      customWidthPx: 10,
      customHeightPx: 'tall',
      densitySelection: 1.1,
      customDensityPercent: 9000,
      textScalePercent: 400
    });
    const fallback = defaultFigureSize(1.25);
    const read = readStoredFigureSize(1.25);

    expect(read.resolutionId).toBe('uhd');
    expect(read.customWidthPx).toBe(fallback.customWidthPx);
    expect(read.customHeightPx).toBe(fallback.customHeightPx);
    expect(read.densitySelection).toBe(fallback.densitySelection);
    expect(read.customDensityPercent).toBe(fallback.customDensityPercent);
    expect(read.textScalePercent).toBe(100);
  });

  it('reads the default from unreadable storage', () => {
    store('{not json');
    expect(readStoredFigureSize(1)).toEqual(defaultFigureSize(1));

    store('[1, 2, 3]');
    expect(readStoredFigureSize(1)).toEqual(defaultFigureSize(1));
  });

  it('leaves the defaults when storage throws, on read and on write', () => {
    spyOn(Storage.prototype, 'getItem').and.throwError('blocked');
    spyOn(Storage.prototype, 'setItem').and.throwError('blocked');

    expect(readStoredFigureSize(1)).toEqual(defaultFigureSize(1));
    expect(() => writeStoredFigureSize({ ...defaultFigureSize(1), resolutionId: 'hd' })).not.toThrow();
  });

  it('tells a size apart from the default by any one field', () => {
    const base = defaultFigureSize(1);
    expect(sameFigureSize(base, defaultFigureSize(1))).toBeTrue();
    expect(sameFigureSize(base, { ...base, textScalePercent: 105 })).toBeFalse();
    expect(sameFigureSize(base, { ...base, densitySelection: 'custom' })).toBeFalse();
  });

  it('reads a stored table fit size as Full HD for the charts', () => {
    store({ version: 1, ...defaultFigureSize(1), resolutionId: 'fit' });
    expect(readStoredFigureSize(1).resolutionId).toBe('fullhd');
  });

  describe('table image size', () => {
    beforeEach(() => localStorage.removeItem(TABLE_IMAGE_SIZE_STORAGE_KEY));
    afterEach(() => localStorage.removeItem(TABLE_IMAGE_SIZE_STORAGE_KEY));

    it('defaults to Fit the table at 200 % and 100 % text, whatever the display', () => {
      expect(defaultTableImageSize()).toEqual({
        resolutionId: 'fit',
        customWidthPx: 1920,
        customHeightPx: 1080,
        densitySelection: 2,
        customDensityPercent: 200,
        textScalePercent: 100
      });
      expect(FIT_RESOLUTION_ID).toBe('fit');
      expect(readStoredTableImageSize()).toEqual(defaultTableImageSize());
    });

    it('is stored apart from the charts’ size and keeps fit, presets and custom', () => {
      const custom: FigureSizeSettings = { ...defaultTableImageSize(), resolutionId: 'custom', customWidthPx: 1480, customHeightPx: 620 };
      writeStoredTableImageSize(custom);
      expect(readStoredTableImageSize()).toEqual(custom);
      expect(localStorage.getItem(FIGURE_SIZE_STORAGE_KEY)).toBeNull();

      writeStoredTableImageSize(defaultTableImageSize());
      expect(readStoredTableImageSize().resolutionId).toBe('fit');

      localStorage.setItem(TABLE_IMAGE_SIZE_STORAGE_KEY, JSON.stringify({ ...defaultTableImageSize(), resolutionId: 'eight-k' }));
      expect(readStoredTableImageSize().resolutionId).toBe('fit');
      localStorage.setItem(TABLE_IMAGE_SIZE_STORAGE_KEY, '{not json');
      expect(readStoredTableImageSize()).toEqual(defaultTableImageSize());
    });
  });

  describe('pure size helpers', () => {
    const base = defaultFigureSize(1);

    it('resolves presets, custom pairs and fit', () => {
      expect(resolveSizeResolution({ ...base, resolutionId: 'uhd' })).toEqual(jasmine.objectContaining({ widthPx: 3840, heightPx: 2160 }));
      expect(resolveSizeResolution({ ...base, resolutionId: 'custom', customWidthPx: 100, customHeightPx: 9999.6 }))
        .toEqual(jasmine.objectContaining({ id: 'custom', widthPx: 320, heightPx: 8000 }));
      expect(resolveSizeResolution({ ...base, resolutionId: 'fit' }).id).toBe('fullhd');
    });

    it('resolves a listed density or the clamped custom percentage', () => {
      expect(resolveSizeDensity({ ...base, densitySelection: 1.5 })).toBe(1.5);
      expect(resolveSizeDensity({ ...base, densitySelection: 'custom', customDensityPercent: 175 })).toBe(1.75);
      expect(resolveSizeDensity({ ...base, densitySelection: 'custom', customDensityPercent: 9000 })).toBe(8);
      expect(clampDensityPercent(Number.NaN)).toBe(100);
      expect(clampExportDimension(Number.NaN)).toBe(320);
    });

    it('names out-of-range sides, densities and oversized bitmaps, most specific first', () => {
      const badSide = sizeErrors({ ...base, resolutionId: 'custom', customWidthPx: 10 });
      expect(badSide.customResolution).toBe('The figure width must be between 320 and 8000 px.');
      expect(badSide.bitmap).toBe('');
      expect(badSide.any).toBe(badSide.customResolution);
      expect(sizeErrors({ ...base, resolutionId: 'custom', customWidthPx: 10, customHeightPx: 10 }, 'image').customResolution)
        .toBe('The image width and height must be between 320 and 8000 px.');

      const badDensity = sizeErrors({ ...base, densitySelection: 'custom', customDensityPercent: 10 });
      expect(badDensity.customDensity).toBe('The pixel density must be between 50 and 800 %.');

      const oversized = sizeErrors({ ...base, resolutionId: 'a4p', densitySelection: 'custom', customDensityPercent: 800 });
      expect(oversized.bitmap).toContain('at most 16384 px');
      expect(sizeErrors(base).any).toBe('');
      expect(sizeErrors({ ...defaultTableImageSize(), densitySelection: 4 }).any).toBe('');
    });

    it('labels what a size writes under the chart rule and the plain table rule', () => {
      const fullHd2x = { ...base, densitySelection: 2 as const };
      expect(sizeDimensionsLabel(fullHd2x)).toBe('3840 × 2160 px (1920 × 1080 at 200%) — laid out at 960 × 540, 4× density');
      expect(sizeDimensionsLabel({ ...fullHd2x, textScalePercent: 125 }, 'plain'))
        .toBe('3840 × 2160 px (1920 × 1080 at 200%) — laid out at 1536 × 864, 2.50× density');
      expect(sizeDimensionsLabel(defaultTableImageSize(), 'plain')).toBe('');
    });

    it('reads out a size in one line', () => {
      expect(sizeReadout(base)).toBe('Full HD — 1920 × 1080 · 100% · text 100 %');
      expect(sizeReadout({ ...base, resolutionId: 'custom', customWidthPx: 1480, customHeightPx: 620 }))
        .toBe('Custom 1480 × 620 · 100% · text 100 %');
      expect(sizeReadout(defaultTableImageSize())).toBe('Fit the table · 200%');
    });
  });
});
