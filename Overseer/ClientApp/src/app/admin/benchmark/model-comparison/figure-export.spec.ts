import {
  FIGURE_EXPORT_LAYOUT_WIDTH,
  FIGURE_EXPORT_MIN_DIMENSION,
  FIGURE_EXPORT_PRESETS,
  FIGURE_EXPORT_SCALE,
  FigureExportResolution,
  composeFigureImage,
  copyImageToClipboard,
  encodeFigureImage,
  figureExportFilename,
  resolveFigureLayout
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

  /** Explicit sizes only: `onscreen` follows the live canvas and has no dimensions of its own. */
  const explicitPresets = FIGURE_EXPORT_PRESETS.filter(
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
        { id: 'custom', label: 'Custom', widthPx: 1280, heightPx: minimumHeight },
        onScreen
      );
      expect(retry.refusal).toBeNull();
      expect(retry.layout!.pixelHeight).toBe(minimumHeight);
    });

    it('rejects a custom size below the minimum dimension', () => {
      const custom: FigureExportResolution = {
        id: 'custom',
        label: 'Custom',
        widthPx: FIGURE_EXPORT_MIN_DIMENSION - 1,
        heightPx: 720
      };

      const { layout, refusal } = resolveFigureLayout(chromeOf(), custom, onScreen);

      expect(layout).toBeNull();
      expect(refusal).toContain(String(FIGURE_EXPORT_MIN_DIMENSION));
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
