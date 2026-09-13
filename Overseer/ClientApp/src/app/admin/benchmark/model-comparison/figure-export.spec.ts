import {
  DEFAULT_WEBP_QUALITY,
  FIGURE_EXPORT_LAYOUT_WIDTH,
  FIGURE_EXPORT_MIN_DIMENSION,
  FIGURE_EXPORT_PRESETS,
  FIGURE_EXPORT_PRESET_GROUPS,
  FIGURE_EXPORT_SCALE,
  FIGURE_PREVIEW_MAX_WIDTH,
  FigureExportResolution,
  WEBP_QUALITY_OPTIONS,
  WebpQuality,
  aspectRatioLabel,
  composeFigureImage,
  copyImageToClipboard,
  encodeFigureImage,
  figureExportFilename,
  previewResolution,
  resolveFigureLayout,
  webpEncoderQuality
} from './figure-export';

describe('figure-export', () => {
  /** A stand-in for a rendered Chart.js canvas: painted, and sized through its style box. */
  function sourceCanvas(width = 400, height = 240): HTMLCanvasElement {
    const canvas = document.createElement('canvas');
    canvas.width = width;
    canvas.height = height;
    canvas.style.width = `${width}px`;
    canvas.style.height = `${height}px`;
    const context = canvas.getContext('2d')!;
    context.fillStyle = '#4488cc';
    context.fillRect(0, 0, width, height);
    return canvas;
  }

  /** The chrome half of a request: everything `resolveFigureLayout` measures. */
  function chromeOf(overrides: Partial<Parameters<typeof resolveFigureLayout>[0]> = {}) {
    return {
      title: 'P1 — Quality, speed and cost',
      subtitle: '18 items per run, current catalog as of 2026-09-07',
      caption: 'Read the whiskers before the bar tops.',
      notices: ['Cost bars carry no interval at any R.'],
      footer: 'Suite A — Current catalog — 4 of 5 entries charted — computed 2026-09-07',
      ...overrides
    };
  }

  function request(overrides: Partial<Parameters<typeof composeFigureImage>[0]> = {}) {
    return {
      canvas: sourceCanvas(),
      ...chromeOf(),
      format: 'png' as const,
      ...overrides
    };
  }

  /** The size of the live canvas `sourceCanvas` stands in for. */
  const onScreen = { width: 400, height: 240 };

  function preset(id: string): FigureExportResolution {
    return FIGURE_EXPORT_PRESETS.find(candidate => candidate.id === id)!;
  }

  /** Every preset as the picker offers it: the grouped list, flattened back to one sequence. */
  const groupedPresets = FIGURE_EXPORT_PRESET_GROUPS.flatMap(group => group.presets);

  /** Explicit sizes only: `onscreen` follows the live canvas and has no dimensions of its own. */
  const explicitPresets = groupedPresets.filter(
    candidate => candidate.widthPx !== null && candidate.heightPx !== null
  );

  it('composes at twice the source density over an opaque ground', () => {
    const composed = composeFigureImage(request());

    // Wider and taller than the plot: padding, the title block and the caveats below it.
    expect(composed.width).toBeGreaterThan(400 * FIGURE_EXPORT_SCALE);
    expect(composed.height).toBeGreaterThan(240 * FIGURE_EXPORT_SCALE);

    // Opaque, or a PNG of a transparent Chart.js canvas renders dark-on-dark in a document.
    const pixel = composed.getContext('2d')!.getImageData(0, 0, 1, 1).data;
    expect(pixel[3]).toBe(255);
  });

  it('draws the title, the caption, every notice and the footer into the image', () => {
    const drawn: string[] = [];
    const real = HTMLCanvasElement.prototype.getContext;
    spyOn(HTMLCanvasElement.prototype, 'getContext').and.callFake(function (
      this: HTMLCanvasElement,
      ...args: any[]
    ) {
      const context = (real as any).apply(this, args);
      if (context && args[0] === '2d' && !(context as any).__spied) {
        (context as any).__spied = true;
        const fillText = context.fillText.bind(context);
        context.fillText = (text: string, x: number, y: number) => {
          drawn.push(text);
          fillText(text, x, y);
        };
      }
      return context;
    } as any);

    composeFigureImage(request({
      notices: ['Speed is degraded for Gemini 2.5 Flash.', 'Charts plot at most 8 models.']
    }));

    const composited = drawn.join(' ');
    expect(composited).toContain('Quality, speed and cost');
    expect(composited).toContain('Read the whiskers');
    expect(composited).toContain('Speed is degraded');
    expect(composited).toContain('Charts plot at most 8 models');
    expect(composited).toContain('4 of 5 entries charted');
  });

  describe('resolveFigureLayout', () => {
    it('returns exactly the requested pixel size for every preset', () => {
      for (const resolution of explicitPresets) {
        const { layout, refusal } = resolveFigureLayout(chromeOf(), resolution, onScreen);

        expect(refusal).withContext(resolution.id).toBeNull();
        expect(layout!.pixelWidth).withContext(resolution.id).toBe(resolution.widthPx!);
        expect(layout!.pixelHeight).withContext(resolution.id).toBe(resolution.heightPx!);
      }
    });

    it('composes every explicit size at one layout width, so the typography never changes', () => {
      for (const resolution of explicitPresets) {
        const { layout } = resolveFigureLayout(chromeOf(), resolution, onScreen);

        expect(layout!.layoutWidth).withContext(resolution.id).toBe(FIGURE_EXPORT_LAYOUT_WIDTH);
        expect(layout!.density)
          .withContext(resolution.id)
          .toBeCloseTo(resolution.widthPx! / FIGURE_EXPORT_LAYOUT_WIDTH, 10);
      }
    });

    it('composes an explicit size to exactly the requested bitmap', () => {
      const { layout } = resolveFigureLayout(chromeOf(), preset('fullhd'), onScreen);

      const composed = composeFigureImage(request({ layout }));

      expect(composed.width).toBe(1920);
      expect(composed.height).toBe(1080);
    });

    it('reproduces the on-screen composition exactly', () => {
      const composed = composeFigureImage(request());

      const { layout, refusal } = resolveFigureLayout(chromeOf(), preset('onscreen'), onScreen);

      expect(refusal).toBeNull();
      expect(layout!.density).toBe(FIGURE_EXPORT_SCALE);
      // A 400 px plot in a 360 px minimum column, plus 20 px of padding on both sides, at 2x.
      expect(layout!.pixelWidth).toBe(880);
      expect(layout!.pixelWidth).toBe(composed.width);
      expect(layout!.pixelHeight).toBe(composed.height);
    });

    it('refuses a figure whose caveats leave no room for the plot, and names a height that fits', () => {
      const notice = (index: number): string =>
        `Notice ${index}: ` +
        'the speed axis is degraded for this entry, so its bar is drawn from a partial sample. '
          .repeat(6);
      const chrome = chromeOf({ notices: [1, 2, 3, 4, 5, 6].map(notice) });

      const { layout, refusal } = resolveFigureLayout(chrome, preset('hd'), onScreen);

      expect(layout).toBeNull();
      expect(refusal).toContain('Quality, speed and cost');

      // The named minimum is a promise: the same figure must resolve at it.
      const named = /(\d+) px tall or more/.exec(refusal!);
      expect(named).not.toBeNull();
      const minimumHeight = Number(named![1]);
      expect(minimumHeight).toBeGreaterThan(720);

      const retry = resolveFigureLayout(
        chrome,
        { id: 'custom', label: 'Custom', group: 'Custom', widthPx: 1280, heightPx: minimumHeight },
        onScreen
      );
      expect(retry.refusal).toBeNull();
      expect(retry.layout!.pixelHeight).toBe(minimumHeight);
    });

    it('offers every preset in exactly one group, in the order the list declares them', () => {
      // The grouping is what the picker renders, so a preset missing from it is a size nobody can
      // choose however correctly it resolves.
      expect(groupedPresets.map(preset => preset.id))
        .toEqual(FIGURE_EXPORT_PRESETS.map(preset => preset.id));
      expect(FIGURE_EXPORT_PRESET_GROUPS.map(group => group.label))
        .toEqual(['On-screen', '16:9', '16:10', '4:3', '3:2', '1:1', '21:9', 'Print']);
      for (const group of FIGURE_EXPORT_PRESET_GROUPS) {
        expect(group.presets.every(preset => preset.group === group.label))
          .withContext(group.label)
          .toBeTrue();
      }
    });

    it('rejects a custom size below the minimum dimension', () => {
      const custom: FigureExportResolution = {
        id: 'custom',
        label: 'Custom',
        group: 'Custom',
        widthPx: FIGURE_EXPORT_MIN_DIMENSION - 1,
        heightPx: 720
      };

      const { layout, refusal } = resolveFigureLayout(chromeOf(), custom, onScreen);

      expect(layout).toBeNull();
      expect(refusal).toContain(String(FIGURE_EXPORT_MIN_DIMENSION));
    });
  });

  describe('aspectRatioLabel', () => {
    it('names a size by its reduced ratio', () => {
      expect(aspectRatioLabel(1920, 1080)).toBe('16:9');
      expect(aspectRatioLabel(2048, 1536)).toBe('4:3');
      expect(aspectRatioLabel(1080, 1080)).toBe('1:1');
    });

    it('falls back to a decimal where neither reduced term names anything', () => {
      // A4 reduces to 877:620, which is arithmetic rather than a shape a reader recognises.
      expect(aspectRatioLabel(3508, 2480)).toBe('1.41:1');
    });
  });

  describe('previewResolution', () => {
    it('leaves a size at or under the cap exactly as it is', () => {
      const hd = preset('hd');
      // Exactly at the cap, which is the boundary the comparison has to include.
      const uxga = preset('uxga');

      expect(previewResolution(hd)).toBe(hd);
      expect(uxga.widthPx).toBe(FIGURE_PREVIEW_MAX_WIDTH);
      expect(previewResolution(uxga)).toBe(uxga);
    });

    it('scales a larger size down to the cap, keeping its ratio', () => {
      const capped = previewResolution(preset('uhd'));

      expect(capped.id).toBe('preview');
      expect(capped.widthPx).toBe(FIGURE_PREVIEW_MAX_WIDTH);
      expect(capped.heightPx).toBe(900);
    });

    it('returns the on-screen size untouched, which has no dimensions to cap', () => {
      const onscreen = preset('onscreen');

      expect(previewResolution(onscreen)).toBe(onscreen);
    });
  });

  it('reports a PNG fallback rather than naming a PNG file .webp', async () => {
    const canvas = sourceCanvas(120, 80);
    // A browser with no WebP encoder answers a WebP request with a PNG rather than failing.
    spyOn(canvas, 'toBlob').and.callFake((callback: BlobCallback) => {
      callback(new Blob(['fake'], { type: 'image/png' }));
    });

    const result = await encodeFigureImage(canvas, 'webp');

    expect(result.format).toBe('png');
    expect(result.fellBackToPng).toBeTrue();
    expect(figureExportFilename('p1-panels', result.format)).toMatch(/\.png$/);
  });

  it('reports no fallback when WebP really was encoded', async () => {
    const canvas = sourceCanvas(120, 80);
    spyOn(canvas, 'toBlob').and.callFake((callback: BlobCallback) => {
      callback(new Blob(['fake'], { type: 'image/webp' }));
    });

    const result = await encodeFigureImage(canvas, 'webp');

    expect(result.format).toBe('webp');
    expect(result.fellBackToPng).toBeFalse();
  });

  describe('WebP quality encoding', () => {
    it('passes quality divided by 100 to toBlob for WebP', async () => {
      const canvas = sourceCanvas(120, 80);
      const blobSpy = spyOn(canvas, 'toBlob').and.callFake((callback: BlobCallback) => {
        callback(new Blob(['fake'], { type: 'image/webp' }));
      });

      await encodeFigureImage(canvas, 'webp', 85);

      expect(blobSpy).toHaveBeenCalledWith(
        jasmine.any(Function),
        'image/webp',
        0.85
      );
    });

    it('passes 1.0 to toBlob for WebP quality 100', async () => {
      const canvas = sourceCanvas(120, 80);
      const blobSpy = spyOn(canvas, 'toBlob').and.callFake((callback: BlobCallback) => {
        callback(new Blob(['fake'], { type: 'image/webp' }));
      });

      await encodeFigureImage(canvas, 'webp', 100);

      expect(blobSpy).toHaveBeenCalledWith(
        jasmine.any(Function),
        'image/webp',
        1.0
      );
    });

    it('passes undefined to toBlob for PNG regardless of quality parameter', async () => {
      const canvas = sourceCanvas(120, 80);
      const blobSpy = spyOn(canvas, 'toBlob').and.callFake((callback: BlobCallback) => {
        callback(new Blob(['fake'], { type: 'image/png' }));
      });

      await encodeFigureImage(canvas, 'png', 85);

      expect(blobSpy).toHaveBeenCalledWith(
        jasmine.any(Function),
        'image/png',
        undefined
      );
    });

    // 85 is the project-wide WebP quality (`.agents/AGENTS.md` § Image Conventions).
    it('defaults WebP encoding to quality 85', () => {
      expect(DEFAULT_WEBP_QUALITY).toBe(85);
    });

    it('uses DEFAULT_WEBP_QUALITY when no quality is provided', async () => {
      const canvas = sourceCanvas(120, 80);
      const blobSpy = spyOn(canvas, 'toBlob').and.callFake((callback: BlobCallback) => {
        callback(new Blob(['fake'], { type: 'image/webp' }));
      });

      await encodeFigureImage(canvas, 'webp');

      expect(blobSpy).toHaveBeenCalledWith(
        jasmine.any(Function),
        'image/webp',
        webpEncoderQuality(DEFAULT_WEBP_QUALITY)
      );
    });
  });

  it('names the file after the figure and a sortable timestamp', () => {
    const name = figureExportFilename('s1-quality-speed', 'webp', new Date(2026, 8, 7, 14, 3, 9));

    expect(name).toBe('model-comparison_s1-quality-speed_20260907_140309.webp');
  });

  it('strips characters a filename cannot carry from the figure id', () => {
    const name = figureExportFilename('run:12/panel', 'png', new Date(2026, 0, 2, 3, 4, 5));

    expect(name).toBe('model-comparison_run-12-panel_20260102_030405.png');
  });

  describe('copyImageToClipboard', () => {
    /**
     * `navigator.clipboard` is a getter on the prototype, so it is stood in for with an own
     * property on the instance; deleting that property afterwards restores the real one.
     */
    function withClipboard(value: unknown): void {
      Object.defineProperty(navigator, 'clipboard', { value, configurable: true });
    }

    afterEach(() => {
      delete (navigator as unknown as { clipboard?: unknown }).clipboard;
    });

    it('writes the blob as a clipboard item and reports it copied', async () => {
      const write = jasmine.createSpy('write').and.returnValue(Promise.resolve());
      withClipboard({ write });
      const blob = new Blob(['png'], { type: 'image/png' });

      expect(await copyImageToClipboard(blob)).toBe('copied');
      expect(write).toHaveBeenCalledTimes(1);
    });

    it('reports a refused write rather than throwing', async () => {
      withClipboard({ write: () => Promise.reject(new Error('Document is not focused.')) });

      // Never throws: the caller's only sane response to a refusal is an inline message.
      await expectAsync(copyImageToClipboard(new Blob(['png'], { type: 'image/png' })))
        .toBeResolvedTo('denied');
    });

    it('reports an absent clipboard API as unsupported', async () => {
      withClipboard(undefined);

      expect(await copyImageToClipboard(new Blob(['png'], { type: 'image/png' })))
        .toBe('unsupported');
    });

    it('reports a clipboard with no write method as unsupported', async () => {
      // The read-only half of the API is available in more places than the write half.
      withClipboard({ readText: () => Promise.resolve('') });

      expect(await copyImageToClipboard(new Blob(['png'], { type: 'image/png' })))
        .toBe('unsupported');
    });
  });
});
