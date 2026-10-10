import { keyFiguresFooterText } from '../../run-report-frame/key-figures-image';
import { ccAnalysisResult, ccRunRows } from '../chat-consistency-tab.testing';
import {
  CcResultsImageRequest,
  ccResultsImageFileName,
  ccResultsImageInput,
  ccResultsImageIo,
  ccResultsImageStatusMessage,
  exportResultsImage,
  measureResultsImage,
  renderResultsImage
} from './results-image-export';
import { CcResultsImageSection, CcResultsImageSettings, defaultCcResultsImageSettings, withSectionExclusions } from './results-image-settings';

/** 2026-10-10 09:45:12, local time, as the file names stamp it. */
const NOW = new Date(2026, 9, 10, 9, 45, 12);

function request(section: CcResultsImageSection = 'verdicts', settings: CcResultsImageSettings = defaultCcResultsImageSettings()): CcResultsImageRequest {
  return {
    section,
    result: ccAnalysisResult(),
    context: { rows: ccRunRows(), batteryRows: [], eventDays: [] },
    overseerVersion: '1.0.29',
    settings
  };
}

function withSize(size: Partial<CcResultsImageSettings['size']>, format: CcResultsImageSettings['format'] = 'png'): CcResultsImageSettings {
  const defaults = defaultCcResultsImageSettings();
  return { ...defaults, format, size: { ...defaults.size, ...size } };
}

describe('results image export', () => {
  beforeEach(() => {
    vi.spyOn(ccResultsImageIo, 'loadImage').mockImplementation(() => Promise.reject(new Error('404')));
    vi.spyOn(ccResultsImageIo, 'now').mockReturnValue(NOW);
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('names the file by the analysis, the model and the section', () => {
    const result = ccAnalysisResult();
    expect(ccResultsImageFileName(result, 'nextRuns', NOW)).toBe('chat-consistency-7_gpt-5-high_next-runs_20261010_094512.png');
    expect(ccResultsImageFileName(ccAnalysisResult({ analysisId: null }), 'summary', NOW, 'webp'))
      .toBe('chat-consistency-unsaved_gpt-5-high_summary_20261010_094512.webp');
  });

  it('announces each outcome, naming the section', () => {
    expect(ccResultsImageStatusMessage('copied', 'verdicts')).toBe('Verdicts section copied as an image.');
    expect(ccResultsImageStatusMessage('downloaded', 'verdicts')).toBe('Image downloaded.');
    expect(ccResultsImageStatusMessage('webp-fallback', 'verdicts')).toBe('This browser cannot write WebP; the image was saved as PNG.');
    expect(ccResultsImageStatusMessage('unsupported', 'verdicts')).toBe('This browser cannot copy images here; use Download instead.');
    expect(ccResultsImageStatusMessage('empty', 'nextRuns')).toBe('Nothing in the Next runs section is selected; use Image settings.');
  });

  it('leaves out the image details the settings exclude', () => {
    const settings = { ...defaultCcResultsImageSettings(), detailsExcluded: ['header', 'model', 'analysis', 'footer'] };
    const bare = ccResultsImageInput(request('summary', settings), NOW, true);
    expect(bare.header).toBeNull();
    expect(bare.modelRows).toEqual([]);
    expect(bare.analysisLine).toBe('');
    expect(bare.footer).toBe('');
    const full = ccResultsImageInput(request('summary'), NOW, true);
    expect(full.header).toEqual({ title: 'Chat Consistency · Summary', logo: true });
    expect(full.analysisLine).toBe('Analysis #7 · saved 2026-10-02 09:00 UTC · Protocol V1');
    expect(full.footer).toBe(keyFiguresFooterText('1.0.29', NOW));
    expect(full.footer).toMatch(/^GnollBench · Overseer 1\.0\.29 · exported \d{4}-\d{2}-\d{2} \d{2}:\d{2} UTC$/);
  });

  it('downloads a PNG at fit, 2560 px wide, even when the logo does not load', async () => {
    const save = vi.spyOn(ccResultsImageIo, 'save').mockReturnValue(undefined);
    expect(await exportResultsImage('download', request())).toBe('Image downloaded.');
    expect(save).toHaveBeenCalledTimes(1);
    const [blob, fileName] = vi.mocked(save).mock.lastCall!;
    expect(blob.type).toBe('image/png');
    expect(blob.size).toBeGreaterThan(0);
    expect(fileName).toBe('chat-consistency-7_gpt-5-high_verdicts_20261010_094512.png');
    const image = await renderResultsImage(request());
    const bitmap = await createImageBitmap(image.blob);
    expect(bitmap.width).toBe(2560);
  });

  it('downloads a WebP as image/webp under a .webp name', async () => {
    const save = vi.spyOn(ccResultsImageIo, 'save').mockReturnValue(undefined);
    expect(await exportResultsImage('download', request('periods', withSize({}, 'webp')))).toBe('Image downloaded.');
    const [blob, fileName] = vi.mocked(save).mock.lastCall!;
    expect(blob.type).toBe('image/webp');
    expect(fileName).toBe('chat-consistency-7_gpt-5-high_periods_20261010_094512.webp');
  });

  it('names a WebP the browser wrote as PNG .png, and says so', async () => {
    vi.spyOn(HTMLCanvasElement.prototype, 'toBlob').mockImplementation(function (callback: BlobCallback) {
      callback(new Blob(['png'], { type: 'image/png' }));
    });
    const save = vi.spyOn(ccResultsImageIo, 'save').mockReturnValue(undefined);
    expect(await exportResultsImage('download', request('summary', withSize({}, 'webp'))))
      .toBe('This browser cannot write WebP; the image was saved as PNG.');
    expect(vi.mocked(save).mock.lastCall![1]).toBe('chat-consistency-7_gpt-5-high_summary_20261010_094512.png');
  });

  it('copies a PNG whatever the format', async () => {
    const copy = vi.spyOn(ccResultsImageIo, 'copy').mockResolvedValue('copied');
    expect(await exportResultsImage('copy', request('attribution', withSize({}, 'webp')))).toBe('Attribution section copied as an image.');
    expect(vi.mocked(copy).mock.lastCall![0].type).toBe('image/png');
    copy.mockResolvedValue('denied');
    expect(await exportResultsImage('copy', request('attribution'))).toBe('Could not copy the image.');
  });

  it('writes a box at exactly its size times the density, in either scheme', async () => {
    const dark = await renderResultsImage(request('summary', withSize({ resolutionId: 'fullhd', densitySelection: 1 })));
    const darkBitmap = await createImageBitmap(dark.blob);
    expect([darkBitmap.width, darkBitmap.height]).toEqual([1920, 1080]);
    const light = await renderResultsImage(request('summary', { ...withSize({ resolutionId: 'hd', densitySelection: 2 }), scheme: 'light' }));
    const lightBitmap = await createImageBitmap(light.blob);
    expect([lightBitmap.width, lightBitmap.height]).toEqual([2560, 1440]);
  });

  it('composes nothing for a section with nothing selected', async () => {
    const save = vi.spyOn(ccResultsImageIo, 'save').mockReturnValue(undefined);
    const settings = withSectionExclusions(defaultCcResultsImageSettings(), 'summary',
      ['verdict', 'model', 'chips', 'scope', 'figure-decided', 'figure-baseline', 'figure-comparison', 'figure-pairedItems', 'figure-nextRuns']);
    expect(await exportResultsImage('download', request('summary', settings)))
      .toBe('Nothing in the Summary section is selected; use Image settings.');
    expect(save).not.toHaveBeenCalled();
    expect(measureResultsImage(request('summary', settings))).toEqual({ empty: true });
  });

  it('refuses a size the browser cannot write, and writes nothing', async () => {
    const save = vi.spyOn(ccResultsImageIo, 'save').mockReturnValue(undefined);
    const oversized = withSize({ resolutionId: 'custom', customWidthPx: 8000, customHeightPx: 8000, densitySelection: 4 });
    const message = await exportResultsImage('download', request('verdicts', oversized));
    expect(message).toContain('at most 16384 px');
    expect(save).not.toHaveBeenCalled();
    expect(measureResultsImage(request('verdicts', oversized))).toEqual({ refusal: message });
  });

  it('measures the next image without drawing it', () => {
    expect(measureResultsImage(request('summary', withSize({ resolutionId: 'uhd', densitySelection: 2 }))))
      .toEqual({ widthPx: 7680, heightPx: 4320 });
    const fit = measureResultsImage(request());
    expect('widthPx' in fit && fit.widthPx).toBe(2560);
  });
});
