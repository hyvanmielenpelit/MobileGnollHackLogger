import {
  DEFAULT_FIGURE_RESOLUTION_ID,
  FIGURE_SIZE_STORAGE_KEY,
  FigureSizeSettings,
  defaultFigureSize,
  readStoredFigureSize,
  sameFigureSize,
  writeStoredFigureSize
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
});
