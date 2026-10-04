import {
  KEY_FIGURES_EXPORT_SECTIONS_STORAGE_KEY,
  KEY_FIGURES_EXPORT_STORAGE_KEY,
  KeyFiguresExportSettings,
  defaultKeyFiguresExportSettings,
  keyFiguresFormatLabel,
  readStoredKeyFiguresExportSections,
  readStoredKeyFiguresExportSettings,
  sameKeyFiguresExportSettings,
  writeStoredKeyFiguresExportSections,
  writeStoredKeyFiguresExportSettings
} from './key-figures-export-settings';

describe('key figures export settings', () => {
  beforeEach(() => {
    localStorage.removeItem(KEY_FIGURES_EXPORT_STORAGE_KEY);
    localStorage.removeItem(KEY_FIGURES_EXPORT_SECTIONS_STORAGE_KEY);
  });

  afterEach(() => {
    localStorage.removeItem(KEY_FIGURES_EXPORT_STORAGE_KEY);
    localStorage.removeItem(KEY_FIGURES_EXPORT_SECTIONS_STORAGE_KEY);
  });

  it('defaults to PNG, quality 85, Fit the figures at 200 %', () => {
    expect(defaultKeyFiguresExportSettings()).toEqual({
      format: 'png',
      webpQuality: 85,
      size: {
        resolutionId: 'fit',
        customWidthPx: 1920,
        customHeightPx: 1080,
        densitySelection: 2,
        customDensityPercent: 200,
        textScalePercent: 100
      }
    });
    expect(readStoredKeyFiguresExportSettings()).toEqual(defaultKeyFiguresExportSettings());
    expect(KEY_FIGURES_EXPORT_STORAGE_KEY).toBe('overseer.benchmark.keyFigures.export');
    expect(KEY_FIGURES_EXPORT_SECTIONS_STORAGE_KEY).toBe('overseer.benchmark.keyFigures.exportSections');
  });

  it('round-trips every field under one versioned record', () => {
    const settings: KeyFiguresExportSettings = {
      format: 'webp',
      webpQuality: 95,
      size: { ...defaultKeyFiguresExportSettings().size, resolutionId: 'custom', customWidthPx: 1200, customHeightPx: 1200, densitySelection: 1 }
    };
    writeStoredKeyFiguresExportSettings(settings);

    const stored = JSON.parse(localStorage.getItem(KEY_FIGURES_EXPORT_STORAGE_KEY)!);
    expect(stored.version).toBe(1);
    expect(stored.format).toBe('webp');
    expect(readStoredKeyFiguresExportSettings()).toEqual(settings);
    expect(sameKeyFiguresExportSettings(readStoredKeyFiguresExportSettings(), settings)).toBe(true);
  });

  it('reads fresh after another writer changes the storage, and the same object while it holds', () => {
    const first = readStoredKeyFiguresExportSettings();
    expect(readStoredKeyFiguresExportSettings()).toBe(first);

    localStorage.setItem(KEY_FIGURES_EXPORT_STORAGE_KEY, JSON.stringify({ version: 1, format: 'webp', webpQuality: 80 }));
    const second = readStoredKeyFiguresExportSettings();
    expect(second.format).toBe('webp');
    expect(second.webpQuality).toBe(80);
    expect(second.size).toEqual(defaultKeyFiguresExportSettings().size);
  });

  it('falls back field by field on values it does not offer', () => {
    localStorage.setItem(KEY_FIGURES_EXPORT_STORAGE_KEY, JSON.stringify({
      version: 1,
      format: 'jpeg',
      webpQuality: 42,
      size: { resolutionId: 'square1080', customWidthPx: 5, densitySelection: 'lots' }
    }));
    const read = readStoredKeyFiguresExportSettings();
    expect(read.format).toBe('png');
    expect(read.webpQuality).toBe(85);
    expect(read.size).toEqual({ ...defaultKeyFiguresExportSettings().size, resolutionId: 'square1080' });

    localStorage.setItem(KEY_FIGURES_EXPORT_STORAGE_KEY, '{not json');
    expect(readStoredKeyFiguresExportSettings()).toEqual(defaultKeyFiguresExportSettings());
    localStorage.setItem(KEY_FIGURES_EXPORT_STORAGE_KEY, '[1]');
    expect(readStoredKeyFiguresExportSettings()).toEqual(defaultKeyFiguresExportSettings());
  });

  it('keeps the defaults when storage throws, on read and on write', () => {
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new Error('blocked');
    });
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new Error('blocked');
    });
    expect(readStoredKeyFiguresExportSettings()).toEqual(defaultKeyFiguresExportSettings());
    expect(() => writeStoredKeyFiguresExportSettings({ ...defaultKeyFiguresExportSettings(), format: 'webp' })).not.toThrow();
    expect(readStoredKeyFiguresExportSections()).toEqual({ format: true, size: true });
    expect(() => writeStoredKeyFiguresExportSections({ format: false, size: false })).not.toThrow();
  });

  it('tells settings apart by any one field', () => {
    const base = defaultKeyFiguresExportSettings();
    expect(sameKeyFiguresExportSettings(base, defaultKeyFiguresExportSettings())).toBe(true);
    expect(sameKeyFiguresExportSettings(base, { ...base, webpQuality: 90 })).toBe(false);
    expect(sameKeyFiguresExportSettings(base, { ...base, size: { ...base.size, densitySelection: 1 } })).toBe(false);
  });

  it('remembers the open state of both sections, open by default', () => {
    expect(readStoredKeyFiguresExportSections()).toEqual({ format: true, size: true });
    writeStoredKeyFiguresExportSections({ format: false, size: true });
    expect(JSON.parse(localStorage.getItem(KEY_FIGURES_EXPORT_SECTIONS_STORAGE_KEY)!)).toEqual({ version: 1, format: false, size: true });
    expect(readStoredKeyFiguresExportSections()).toEqual({ format: false, size: true });

    localStorage.setItem(KEY_FIGURES_EXPORT_SECTIONS_STORAGE_KEY, JSON.stringify({ version: 1, format: 'no' }));
    expect(readStoredKeyFiguresExportSections()).toEqual({ format: true, size: true });
  });

  it('labels the format for the download names', () => {
    expect(keyFiguresFormatLabel('png')).toBe('PNG');
    expect(keyFiguresFormatLabel('webp')).toBe('WebP');
  });
});
