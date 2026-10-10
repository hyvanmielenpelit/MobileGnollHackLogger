import {
  CC_RESULTS_IMAGE_SECTIONS,
  CC_RESULTS_IMAGE_SECTIONS_STORAGE_KEY,
  CC_RESULTS_IMAGE_STORAGE_KEY,
  ccResultsImageFormatLabel,
  defaultCcResultsImageSettings,
  parseCcResultsImageSettings,
  readStoredCcResultsImageFileSections,
  readStoredCcResultsImageSettings,
  withSectionExclusions,
  writeStoredCcResultsImageFileSections,
  writeStoredCcResultsImageSettings
} from './results-image-settings';

describe('results image settings', () => {
  beforeEach(() => {
    localStorage.removeItem(CC_RESULTS_IMAGE_STORAGE_KEY);
    localStorage.removeItem(CC_RESULTS_IMAGE_SECTIONS_STORAGE_KEY);
  });

  afterEach(() => {
    vi.restoreAllMocks();
    localStorage.removeItem(CC_RESULTS_IMAGE_STORAGE_KEY);
    localStorage.removeItem(CC_RESULTS_IMAGE_SECTIONS_STORAGE_KEY);
  });

  it('keeps its own storage keys, apart from the key-figures images', () => {
    expect(CC_RESULTS_IMAGE_STORAGE_KEY).toBe('overseer.benchmark.chatConsistency.resultsImage');
    expect(CC_RESULTS_IMAGE_SECTIONS_STORAGE_KEY).toBe('overseer.benchmark.chatConsistency.resultsImage.sections');
  });

  it('defaults to a PNG at Fit the content and 200 %, dark, with the long parts of three sections off', () => {
    const defaults = defaultCcResultsImageSettings();
    expect(defaults.format).toBe('png');
    expect(defaults.webpQuality).toBe(85);
    expect(defaults.size.resolutionId).toBe('fit');
    expect(defaults.size.densitySelection).toBe(2);
    expect(defaults.size.textScalePercent).toBe(100);
    expect(defaults.scheme).toBe('dark');
    expect(defaults.excluded).toEqual({
      summary: [], verdicts: ['more'], periods: ['units'], attribution: [], nextRuns: ['reasons'], details: []
    });
    expect(defaults.detailsExcluded).toEqual([]);
    expect(Object.keys(defaults.excluded)).toEqual([...CC_RESULTS_IMAGE_SECTIONS]);
  });

  it('reads the defaults while nothing is stored', () => {
    expect(readStoredCcResultsImageSettings()).toEqual(defaultCcResultsImageSettings());
  });

  it('stores exclusions rather than inclusions, and reads them back', () => {
    const stored = withSectionExclusions(
      { ...defaultCcResultsImageSettings(), format: 'webp', webpQuality: 90, scheme: 'light', detailsExcluded: ['footer'] },
      'verdicts',
      ['endpoint-P3', 'mde']
    );
    writeStoredCcResultsImageSettings(stored);

    const raw = JSON.parse(localStorage.getItem(CC_RESULTS_IMAGE_STORAGE_KEY)!) as Record<string, unknown>;
    expect(raw['version']).toBe(1);
    expect(raw['excluded']).toEqual({
      summary: [], verdicts: ['endpoint-P3', 'mde'], periods: ['units'], attribution: [], nextRuns: ['reasons'], details: []
    });
    expect(raw['detailsExcluded']).toEqual(['footer']);

    const read = readStoredCcResultsImageSettings();
    expect(read).toEqual(stored);
    expect(read.excluded.verdicts).toEqual(['endpoint-P3', 'mde']);
  });

  it('falls back field by field on a damaged record', () => {
    localStorage.setItem(CC_RESULTS_IMAGE_STORAGE_KEY, JSON.stringify({
      version: 1,
      format: 'jpeg',
      webpQuality: 42,
      size: { resolutionId: 'fullhd', densitySelection: 1 },
      scheme: 'sepia',
      excluded: { verdicts: 'more', periods: [' units ', 7, '', 'units', 'baseline'] },
      detailsExcluded: [3, 'header']
    }));
    const read = readStoredCcResultsImageSettings();
    const defaults = defaultCcResultsImageSettings();
    expect(read.format).toBe('png');
    expect(read.webpQuality).toBe(85);
    expect(read.size.resolutionId).toBe('fullhd');
    expect(read.size.densitySelection).toBe(1);
    expect(read.scheme).toBe('dark');
    expect(read.excluded.verdicts).toEqual(defaults.excluded.verdicts);
    expect(read.excluded.periods).toEqual(['units', 'baseline']);
    expect(read.excluded.summary).toEqual([]);
    expect(read.detailsExcluded).toEqual(['header']);
  });

  it('reads a corrupt, foreign or other-version record as the defaults', () => {
    const defaults = defaultCcResultsImageSettings();
    expect(parseCcResultsImageSettings('{')).toEqual(defaults);
    expect(parseCcResultsImageSettings('[1, 2]')).toEqual(defaults);
    expect(parseCcResultsImageSettings('null')).toEqual(defaults);
    expect(parseCcResultsImageSettings(JSON.stringify({ version: 2, format: 'webp' }))).toEqual(defaults);
  });

  it('reads the defaults when storage throws, and stores nothing then without throwing', () => {
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new Error('blocked');
    });
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new Error('blocked');
    });
    expect(readStoredCcResultsImageSettings()).toEqual(defaultCcResultsImageSettings());
    expect(() => writeStoredCcResultsImageSettings(defaultCcResultsImageSettings())).not.toThrow();
    expect(readStoredCcResultsImageFileSections()).toEqual({ format: true, size: true });
    expect(() => writeStoredCcResultsImageFileSections({ format: false, size: true })).not.toThrow();
  });

  it('remembers the open state of the two file sections, both open by default', () => {
    expect(readStoredCcResultsImageFileSections()).toEqual({ format: true, size: true });
    writeStoredCcResultsImageFileSections({ format: false, size: true });
    expect(readStoredCcResultsImageFileSections()).toEqual({ format: false, size: true });
    localStorage.setItem(CC_RESULTS_IMAGE_SECTIONS_STORAGE_KEY, JSON.stringify({ format: 'no', size: false }));
    expect(readStoredCcResultsImageFileSections()).toEqual({ format: true, size: false });
  });

  it('replaces one section\'s exclusions and leaves the others', () => {
    const next = withSectionExclusions(defaultCcResultsImageSettings(), 'nextRuns', []);
    expect(next.excluded.nextRuns).toEqual([]);
    expect(next.excluded.verdicts).toEqual(['more']);
  });

  it('names the formats as the download labels print them', () => {
    expect(ccResultsImageFormatLabel('png')).toBe('PNG');
    expect(ccResultsImageFormatLabel('webp')).toBe('WebP');
  });
});
