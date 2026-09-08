import {
  FIGURE_EXPORT_SCALE,
  composeFigureImage,
  encodeFigureImage,
  figureExportFilename
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

  function request(overrides: Partial<Parameters<typeof composeFigureImage>[0]> = {}) {
    return {
      canvas: sourceCanvas(),
      title: 'P1 — Quality, speed and cost',
      subtitle: '18 items per run, current catalog as of 2026-09-07',
      caption: 'Read the whiskers before the bar tops.',
      notices: ['Cost bars carry no interval at any R.'],
      footer: 'Suite A — Current catalog — 4 of 5 entries charted — computed 2026-09-07',
      format: 'png' as const,
      ...overrides
    };
  }

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
});
