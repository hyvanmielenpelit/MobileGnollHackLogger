import { Chart } from 'chart.js';
import { unzipSync } from 'fflate';

import {
  DEFAULT_WEBP_QUALITY,
  FIGURE_EXPORT_DENSITY_PRESETS,
  FIGURE_EXPORT_LAYOUT_HEIGHT,
  FIGURE_EXPORT_LAYOUT_WIDTH,
  FIGURE_EXPORT_MAX_BITMAP_DIMENSION,
  FIGURE_EXPORT_MIN_DIMENSION,
  FIGURE_EXPORT_PRESETS,
  FIGURE_EXPORT_PRESET_GROUPS,
  FigureExportLayout,
  FigureExportResolution,
  WEBP_QUALITY_OPTIONS,
  WebpQuality,
  aspectRatioLabel,
  buildFigureArchive,
  composeFigureImage,
  copyImageToClipboard,
  densityPercentLabel,
  densityPresetFor,
  displayDensity,
  encodeFigureImage,
  figureArchiveFilename,
  figureExportFilename,
  layoutBoxFor,
  measureFigureChrome,
  previewLayoutFor,
  renderPlotOffscreen,
  resolveFigureLayout,
  webpEncoderQuality
} from './figure-export';
import type { FigureBadge, FigureChrome, FigureFooter, FigureNote } from './figure-chrome';
import { APP_CHART_REGISTRABLES } from '../../../chart-registrables';

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

  /** The card chrome a figure carries besides its plot. */
  function figureChrome(overrides: Partial<FigureChrome> = {}): FigureChrome {
    return {
      title: 'P1 — Quality, speed and cost',
      badges: [
        { text: 'Current catalog prices', tone: 'pricing' },
        { text: 'Higher is better', tone: 'direction' }
      ],
      detail: '18 items per run, current catalog as of 2026-09-07',
      key: [
        { glyph: 'solid', text: 'Comparable' },
        { glyph: 'hollow', text: 'Degraded' }
      ],
      highlight: 'Best trade-offs: GPT-5.6 Luna',
      notes: [{ text: 'Cost bars carry no interval at any R.', tone: 'info' }],
      ...overrides
    };
  }

  /** The export's last line. */
  function figureFooter(overrides: Partial<FigureFooter> = {}): FigureFooter {
    return {
      suite: 'Suite A',
      computedAt: '7 Sep 2026, 14:03',
      ...overrides
    };
  }

  /** The chrome half of a request: everything `resolveFigureLayout` and `measureFigureChrome` measure. */
  function sourceOf(
    chromeOverrides: Partial<FigureChrome> = {},
    footerOverrides: Partial<FigureFooter> = {}
  ) {
    return {
      chrome: figureChrome(chromeOverrides),
      footer: figureFooter(footerOverrides)
    };
  }

  function request(overrides: Partial<Parameters<typeof composeFigureImage>[0]> = {}) {
    return {
      canvas: sourceCanvas(),
      chrome: figureChrome(),
      footer: figureFooter(),
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
    const composed = composeFigureImage(request({ density: 2 }));

    // Wider and taller than the plot: padding, the title block and the caveats below it.
    expect(composed.width).toBeGreaterThan(400 * 2);
    expect(composed.height).toBeGreaterThan(240 * 2);

    // Opaque, or a PNG of a transparent Chart.js canvas renders dark-on-dark in a document.
    const pixel = composed.getContext('2d')!.getImageData(0, 0, 1, 1).data;
    expect(pixel[3]).toBe(255);
  });

  it('draws the title, badges, detail, key, highlight, every note and the footer into the image', () => {
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
      chrome: figureChrome({
        notes: [
          { text: 'Speed is degraded for Gemini 2.5 Flash.', tone: 'warning' },
          { text: 'Charts plot at most 8 models.', tone: 'info' }
        ]
      })
    }));

    const composited = drawn.join(' ');
    expect(composited).toContain('Quality, speed and cost');
    expect(composited).toContain('Current catalog prices');
    expect(composited).toContain('18 items per run');
    expect(composited).toContain('Comparable');
    expect(composited).toContain('Best trade-offs');
    expect(composited).toContain('Speed is degraded');
    expect(composited).toContain('Charts plot at most 8 models');
    expect(composited).toContain('Suite A');
    expect(composited).toContain('Computed');
    expect(composited).toContain('7 Sep 2026, 14:03');
    // The footer is composed by the caller now: no pricing basis, count or condition string of its
    // own is assembled here.
    expect(composited).not.toContain('entries charted');
    expect(composited).not.toContain('condition');
  });

  /** A header of title and badges only, with nothing drawn below the plot. */
  function headerOnlyChrome(): Partial<FigureChrome> {
    return { detail: '', key: [], highlight: '', notes: [] };
  }

  const emptyFooter: Partial<FigureFooter> = { suite: '', computedAt: '' };

  /** The `dy` the plot was drawn at, read from the `drawImage` call that paints `plot`. */
  function plotTopOf(plot: HTMLCanvasElement, compose: () => void): number {
    const real = CanvasRenderingContext2D.prototype.drawImage;
    const tops: number[] = [];
    spyOn(CanvasRenderingContext2D.prototype, 'drawImage').and.callFake(function (
      this: CanvasRenderingContext2D,
      ...args: any[]
    ) {
      if (args[0] === plot) {
        tops.push(args[2]);
      }
      return (real as any).apply(this, args);
    } as any);
    compose();
    expect(tops.length).toBe(1);
    return tops[0];
  }

  it('leaves at least 16 px between the badge row and the plot', () => {
    const canvas = sourceCanvas();
    const figure = request({ canvas, chrome: figureChrome(headerOnlyChrome()), footer: figureFooter(emptyFooter) });
    const measured = measureFigureChrome(figure, 400);

    // Padding, the title block, the gap above the badges and the badge rows, as the export lays them out.
    const titleHeight = measured.titleLines.length * Math.round(18 * 1.4);
    const badgeRowsHeight = measured.badgeRows.length * 17 + (measured.badgeRows.length - 1) * 6;
    const badgeBottom = 20 + titleHeight + 6 + badgeRowsHeight;

    const plotTop = plotTopOf(canvas, () => composeFigureImage(figure));

    expect(measured.badgeRows.length).toBeGreaterThan(0);
    expect(plotTop).toBeGreaterThanOrEqual(badgeBottom + 16);
  });

  it('draws each badge in its tone\'s card colors, filling before stroking', () => {
    const scratch = document.createElement('canvas').getContext('2d')!;
    const normalized = (color: string): string => {
      scratch.fillStyle = '#000000';
      scratch.fillStyle = color;
      return String(scratch.fillStyle);
    };

    const events: { op: 'fill' | 'stroke' | 'fillText'; style: string; text?: string }[] = [];
    const realFill = CanvasRenderingContext2D.prototype.fill;
    const realStroke = CanvasRenderingContext2D.prototype.stroke;
    const realFillText = CanvasRenderingContext2D.prototype.fillText;
    spyOn(CanvasRenderingContext2D.prototype, 'fill').and.callFake(function (
      this: CanvasRenderingContext2D,
      ...args: any[]
    ) {
      events.push({ op: 'fill', style: String(this.fillStyle) });
      return (realFill as any).apply(this, args);
    } as any);
    spyOn(CanvasRenderingContext2D.prototype, 'stroke').and.callFake(function (
      this: CanvasRenderingContext2D,
      ...args: any[]
    ) {
      events.push({ op: 'stroke', style: String(this.strokeStyle) });
      return (realStroke as any).apply(this, args);
    } as any);
    spyOn(CanvasRenderingContext2D.prototype, 'fillText').and.callFake(function (
      this: CanvasRenderingContext2D,
      ...args: any[]
    ) {
      events.push({ op: 'fillText', style: String(this.fillStyle), text: args[0] });
      return (realFillText as any).apply(this, args);
    } as any);

    composeFigureImage(request({
      chrome: figureChrome({
        ...headerOnlyChrome(),
        badges: [
          { text: '2 models', tone: 'neutral' },
          { text: 'Current catalog prices', tone: 'pricing' },
          { text: 'Higher is better', tone: 'direction' }
        ]
      }),
      footer: figureFooter(emptyFooter)
    }));

    const expected = [
      { text: '2 models', border: 'rgba(255, 255, 255, 0.25)', fill: 'rgba(255, 255, 255, 0.04)', ink: '#e0ba6d' },
      { text: 'Current catalog prices', border: 'rgba(16, 185, 129, 0.3)', fill: 'rgba(16, 185, 129, 0.1)', ink: '#6ee7b7' },
      { text: 'Higher is better', border: 'rgba(224, 186, 109, 0.45)', fill: 'rgba(224, 186, 109, 0.08)', ink: '#e0ba6d' }
    ];
    for (const badge of expected) {
      const textIndex = events.findIndex(event => event.op === 'fillText' && event.text === badge.text);
      expect(textIndex).withContext(badge.text).toBeGreaterThan(1);
      const [fill, stroke, text] = events.slice(textIndex - 2, textIndex + 1);

      expect(fill.op).withContext(`${badge.text}: filled before stroked`).toBe('fill');
      expect(stroke.op).withContext(`${badge.text}: stroked before its text`).toBe('stroke');
      expect(fill.style).withContext(`${badge.text} fill`).toBe(normalized(badge.fill));
      expect(stroke.style).withContext(`${badge.text} border`).toBe(normalized(badge.border));
      expect(text.style).withContext(`${badge.text} text`).toBe(normalized(badge.ink));
    }
    // The neutral pill reads gold, as on the card, not the body grey.
    expect(normalized(expected[0].ink)).not.toBe(normalized('#d4d4d8'));
  });

  describe('measureFigureChrome', () => {
    it('measures exactly the height the composition draws around the plot', () => {
      const canvas = sourceCanvas();
      const figure = request({ canvas, chrome: figureChrome(headerOnlyChrome()), footer: figureFooter(emptyFooter) });
      const measured = measureFigureChrome(figure, 400);

      let composed!: HTMLCanvasElement;
      const plotTop = plotTopOf(canvas, () => { composed = composeFigureImage({ ...figure, density: 1 }); });

      expect(composed.height).toBe(measured.height + onScreen.height);
      // Nothing is drawn below this plot, so only the bottom padding follows it.
      expect(plotTop + onScreen.height + 20).toBe(composed.height);
    });

    it('wraps five badges at a 360 px content width onto more than one row', () => {
      const badges: FigureBadge[] = [
        { text: 'Current catalog prices', tone: 'pricing' },
        { text: 'Higher is better', tone: 'direction' },
        { text: '4 of 5 entries charted', tone: 'neutral' },
        { text: 'Speed degraded', tone: 'neutral' },
        { text: 'Cost degraded', tone: 'neutral' }
      ];

      const measured = measureFigureChrome(sourceOf({ badges }), 360);

      expect(measured.badgeRows.length).toBeGreaterThan(1);
    });

    it('adds no height for an empty detail, highlight or key', () => {
      const bare = measureFigureChrome(
        sourceOf({ badges: [], detail: '', key: [], highlight: '', notes: [] }, { suite: '', computedAt: '' }),
        400
      );
      expect(bare.detailLines).toEqual([]);
      expect(bare.highlightLines).toEqual([]);
      expect(bare.keyRows).toEqual([]);

      const withDetail = measureFigureChrome(
        sourceOf({ badges: [], detail: 'One short sentence.', key: [], highlight: '', notes: [] },
          { suite: '', computedAt: '' }),
        400
      );
      const withHighlight = measureFigureChrome(
        sourceOf({ badges: [], detail: '', key: [], highlight: 'Best trade-offs: GPT-5.6 Luna', notes: [] },
          { suite: '', computedAt: '' }),
        400
      );
      const withKey = measureFigureChrome(
        sourceOf({ badges: [], detail: '', key: [{ glyph: 'solid', text: 'Comparable' }], highlight: '', notes: [] },
          { suite: '', computedAt: '' }),
        400
      );

      expect(withDetail.height).toBeGreaterThan(bare.height);
      expect(withHighlight.height).toBeGreaterThan(bare.height);
      expect(withKey.height).toBeGreaterThan(bare.height);
    });
  });

  describe('resolveFigureLayout', () => {
    it('returns exactly the requested pixel size for every preset', () => {
      for (const resolution of explicitPresets) {
        const { layout, refusal } = resolveFigureLayout(sourceOf(), resolution, onScreen, 1);

        expect(refusal).withContext(resolution.id).toBeNull();
        expect(layout!.pixelWidth).withContext(resolution.id).toBe(resolution.widthPx!);
        expect(layout!.pixelHeight).withContext(resolution.id).toBe(resolution.heightPx!);
      }
    });

    it('lays every explicit size out at least 960 wide and 540 tall, in the target’s ratio', () => {
      const epsilon = 1e-9;
      for (const resolution of explicitPresets) {
        const { layout } = resolveFigureLayout(sourceOf(), resolution, onScreen, 1);

        expect(layout!.layoutWidth)
          .withContext(resolution.id)
          .toBeGreaterThanOrEqual(FIGURE_EXPORT_LAYOUT_WIDTH - epsilon);
        expect(layout!.layoutHeight)
          .withContext(resolution.id)
          .toBeGreaterThanOrEqual(FIGURE_EXPORT_LAYOUT_HEIGHT - epsilon);

        // Smallest such box: one of the two minimums is met exactly, never both overshot.
        const slack = Math.min(
          layout!.layoutWidth / FIGURE_EXPORT_LAYOUT_WIDTH,
          layout!.layoutHeight / FIGURE_EXPORT_LAYOUT_HEIGHT
        );
        expect(slack).withContext(resolution.id).toBeCloseTo(1, 9);

        // One density on both axes, so the composition carries the target's own shape.
        expect(layout!.layoutWidth / layout!.layoutHeight)
          .withContext(resolution.id)
          .toBeCloseTo(resolution.widthPx! / resolution.heightPx!, 9);
      }
    });

    it('composes an explicit size to exactly the requested bitmap', () => {
      const { layout } = resolveFigureLayout(sourceOf(), preset('fullhd'), onScreen, 1);

      const composed = composeFigureImage(request({ layout }));

      expect(composed.width).toBe(1920);
      expect(composed.height).toBe(1080);
    });

    it('reproduces the on-screen composition exactly', () => {
      const composed = composeFigureImage(request({ density: 2 }));

      const { layout, refusal } = resolveFigureLayout(sourceOf(), preset('onscreen'), onScreen, 2);

      expect(refusal).toBeNull();
      expect(layout!.density).toBe(2);
      // A 400 px plot in a 360 px minimum column, plus 20 px of padding on both sides, at 2x.
      expect(layout!.pixelWidth).toBe(880);
      expect(layout!.pixelWidth).toBe(composed.width);
      expect(layout!.pixelHeight).toBe(composed.height);
    });

    it('refuses a figure whose caveats leave no room for the plot, and names a height that fits', () => {
      const note = (index: number): FigureNote => ({
        text: `Notice ${index}: ` +
          'the speed axis is degraded for this entry, so its bar is drawn from a partial sample. '
            .repeat(6),
        tone: 'info'
      });
      const source = sourceOf({ notes: [1, 2, 3, 4, 5, 6].map(note) });

      const { layout, refusal } = resolveFigureLayout(source, preset('hd'), onScreen, 1);

      expect(layout).toBeNull();
      expect(refusal).toContain('Quality, speed and cost');
      expect(refusal).toContain('header, key and notes');

      // The named minimum is a promise: the same figure must resolve at it.
      const named = /(\d+) px tall or more/.exec(refusal!);
      expect(named).not.toBeNull();
      const minimumHeight = Number(named![1]);
      expect(minimumHeight).toBeGreaterThan(720);

      const retry = resolveFigureLayout(
        source,
        { id: 'custom', label: 'Custom', group: 'Custom', widthPx: 1280, heightPx: minimumHeight },
        onScreen,
        1
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

    it('multiplies the bitmap by the density and leaves the composition alone', () => {
      for (const resolution of explicitPresets) {
        const base = resolveFigureLayout(sourceOf(), resolution, onScreen, 1).layout!;

        for (const density of FIGURE_EXPORT_DENSITY_PRESETS) {
          const context = `${resolution.id} at ${density}`;
          const { layout, refusal } = resolveFigureLayout(sourceOf(), resolution, onScreen, density);

          expect(refusal).withContext(context).toBeNull();
          expect(layout!.pixelWidth).withContext(context).toBe(Math.round(resolution.widthPx! * density));
          expect(layout!.pixelHeight).withContext(context).toBe(Math.round(resolution.heightPx! * density));

          // The composition is what the type size is measured in, so none of it may move.
          expect(layout!.layoutWidth).withContext(context).toBe(base.layoutWidth);
          expect(layout!.layoutHeight).withContext(context).toBe(base.layoutHeight);
          expect(layout!.plotWidth).withContext(context).toBe(base.plotWidth);
          expect(layout!.plotHeight).withContext(context).toBe(base.plotHeight);
          expect(layout!.density).withContext(context).toBeCloseTo(base.density * density, 9);
        }
      }
    });

    it('composes Full HD at 200 % to a 3840 × 2160 bitmap of the same figure', () => {
      const { layout } = resolveFigureLayout(sourceOf(), preset('fullhd'), onScreen, 2);

      const composed = composeFigureImage(request({ layout }));

      expect(composed.width).toBe(3840);
      expect(composed.height).toBe(2160);
      expect(layout!.layoutWidth).toBe(960);
      expect(layout!.density).toBe(4);
    });

    it('writes the on-screen size at the chosen density', () => {
      const { layout, refusal } = resolveFigureLayout(sourceOf(), preset('onscreen'), onScreen, 1.5);

      expect(refusal).toBeNull();
      expect(layout!.density).toBe(1.5);
      // The same 440 layout px the 2x case resolves to, at one and a half device pixels each.
      expect(layout!.pixelWidth).toBe(660);
    });

    it('refuses a bitmap the browser could not allocate, naming both sides and the cap', () => {
      const custom: FigureExportResolution = {
        id: 'custom',
        label: 'Custom',
        group: 'Custom',
        widthPx: 8000,
        heightPx: 8000
      };

      const refused = resolveFigureLayout(sourceOf(), custom, onScreen, 3);
      expect(refused.layout).toBeNull();
      expect(refused.refusal).toContain('24000 × 24000');
      expect(refused.refusal).toContain(String(FIGURE_EXPORT_MAX_BITMAP_DIMENSION));

      // 16 000 px a side is under the cap, so the same size at 200 % is written rather than refused.
      const accepted = resolveFigureLayout(sourceOf(), custom, onScreen, 2);
      expect(accepted.refusal).toBeNull();
      expect(accepted.layout!.pixelWidth).toBe(16000);
    });

    it('rejects a custom size below the minimum dimension', () => {
      const custom: FigureExportResolution = {
        id: 'custom',
        label: 'Custom',
        group: 'Custom',
        widthPx: FIGURE_EXPORT_MIN_DIMENSION - 1,
        heightPx: 720
      };

      const { layout, refusal } = resolveFigureLayout(sourceOf(), custom, onScreen, 1);

      expect(layout).toBeNull();
      expect(refusal).toContain(String(FIGURE_EXPORT_MIN_DIMENSION));
    });
  });

  describe('renderPlotOffscreen', () => {
    // No `provideCharts` here: this spec builds a chart without a TestBed, so the controllers,
    // elements and scales the application registers have to be registered by hand.
    beforeAll(() => {
      Chart.register(...APP_CHART_REGISTRABLES);
    });

    it('renders the plot box at the layout’s own density', async () => {
      const layout: FigureExportLayout = {
        layoutWidth: 960,
        layoutHeight: 540,
        plotWidth: 920,
        plotHeight: 380,
        density: 2,
        pixelWidth: 1920,
        pixelHeight: 1080
      };

      const plot = await renderPlotOffscreen(
        {
          type: 'bar',
          data: { labels: ['A', 'B'], datasets: [{ data: [1, 2] }] }
        },
        layout
      );

      // `responsive: false` stops Chart.js measuring the container, so an unsized canvas would be
      // rasterised from the HTML default and composed into the plot box stretched.
      expect(plot).not.toBeNull();
      expect(plot!.width).toBe(1840);
      expect(plot!.height).toBe(760);
      expect(plot!.style.width).toBe('920px');
      expect(plot!.style.height).toBe('380px');
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

  describe('layoutBoxFor', () => {
    it('anchors a 16:9 size on both minimums at once', () => {
      expect(layoutBoxFor(1920, 1080)).toEqual({ layoutWidth: 960, layoutHeight: 540, density: 2 });
    });

    it('lets a 21:9 size grow wider rather than shrinking its composition', () => {
      // Anchored on the height: 405 layout px of composition would leave the plot shorter than the
      // caption under it.
      expect(layoutBoxFor(2560, 1080)).toEqual({ layoutWidth: 1280, layoutHeight: 540, density: 2 });
    });

    it('lets a portrait size grow taller at the same width', () => {
      const box = layoutBoxFor(2480, 3508);

      expect(box.density).toBe(2480 / 960);
      expect(box.layoutWidth).toBeCloseTo(960, 9);
      expect(box.layoutHeight).toBeCloseTo((3508 * 960) / 2480, 9);
    });
  });

  describe('pixel density', () => {
    it('reads the display’s own density, clamped into the custom bounds', () => {
      expect(displayDensity({ devicePixelRatio: 2 })).toBe(2);
      // A browser zoom lands between the listed steps, which is what Custom exists to hold.
      expect(displayDensity({ devicePixelRatio: 2.2 })).toBe(2.2);
      expect(displayDensity({ devicePixelRatio: 0.1 })).toBe(0.5);
      expect(displayDensity({ devicePixelRatio: 12 })).toBe(8);
    });

    it('falls back to 1 where there is no window to read a ratio from', () => {
      expect(displayDensity(null)).toBe(1);
      expect(displayDensity({})).toBe(1);
      expect(displayDensity({ devicePixelRatio: 0 })).toBe(1);
    });

    it('snaps a density onto a listed preset, or leaves it to Custom', () => {
      expect(densityPresetFor(2)).toBe(2);
      expect(densityPresetFor(2.004)).toBe(2);
      expect(densityPresetFor(2.2)).toBeNull();
      expect(densityPresetFor(Number.NaN)).toBeNull();
    });

    it('names a factor by its percentage', () => {
      expect(densityPercentLabel(1.75)).toBe('175%');
      expect(densityPercentLabel(1)).toBe('100%');
      expect(densityPercentLabel(2.2)).toBe('220%');
    });
  });

  describe('previewLayoutFor', () => {
    /** The export layout a preview is fitted from. */
    function target(id: string): FigureExportLayout {
      return resolveFigureLayout(sourceOf(), preset(id), onScreen, 1).layout!;
    }

    it('keeps the export’s composition and changes only its density', () => {
      const full = target('fullhd');

      const fit = previewLayoutFor(full, { width: 900, height: 700, devicePixelRatio: 2 })!;

      expect(fit.cssWidth).toBe(900);
      expect(fit.cssHeight).toBeCloseTo(506.25, 9);
      expect(fit.layout.pixelWidth).toBe(1800);
      expect(fit.layout.pixelHeight).toBe(1013);
      // The composition itself is the export's, or the preview would be a picture of another figure.
      expect(fit.layout.layoutWidth).toBe(full.layoutWidth);
      expect(fit.layout.layoutHeight).toBe(full.layoutHeight);
      expect(fit.layout.plotWidth).toBe(full.plotWidth);
      expect(fit.layout.plotHeight).toBe(full.plotHeight);
    });

    it('fits a portrait target by the stage’s height', () => {
      const portrait = target('a4p');

      const fit = previewLayoutFor(portrait, { width: 900, height: 700, devicePixelRatio: 2 })!;

      expect(fit.cssHeight).toBeCloseTo(700, 9);
      expect(fit.cssWidth).toBeCloseTo(700 * (2480 / 3508), 9);
    });

    it('does not upscale a target smaller than the stage', () => {
      const hd = target('hd');

      const fit = previewLayoutFor(hd, { width: 3000, height: 2000, devicePixelRatio: 1 })!;

      expect(fit.cssWidth).toBe(1280);
      expect(fit.layout.pixelWidth).toBe(1280);
    });

    it('returns null for a stage with no usable area', () => {
      expect(previewLayoutFor(target('fullhd'), { width: 900, height: 0, devicePixelRatio: 2 }))
        .toBeNull();
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

  it('names the archive after the same sortable timestamp', () => {
    const name = figureArchiveFilename(new Date(2026, 8, 7, 14, 3, 9));

    expect(name).toBe('model-comparison_figures_20260907_140309.zip');
  });

  describe('buildFigureArchive', () => {
    it('packs every entry into one zip under its own name', async () => {
      const entries = [
        { name: 'p1-panels.png', blob: new Blob(['first figure'], { type: 'image/png' }) },
        { name: 'p2-panels.webp', blob: new Blob(['second figure'], { type: 'image/webp' }) }
      ];

      const archive = await buildFigureArchive(entries);

      expect(archive.type).toBe('application/zip');
      const unzipped = unzipSync(new Uint8Array(await archive.arrayBuffer()));
      expect(Object.keys(unzipped).sort()).toEqual(['p1-panels.png', 'p2-panels.webp']);
      expect(new TextDecoder().decode(unzipped['p1-panels.png'])).toBe('first figure');
      expect(new TextDecoder().decode(unzipped['p2-panels.webp'])).toBe('second figure');
    });
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
