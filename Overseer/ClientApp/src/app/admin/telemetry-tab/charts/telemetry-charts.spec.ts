import { AiTelemetryActivityPointDto, AiTelemetryPointDto, AiTelemetrySeriesDto, AiTelemetrySeriesLineDto } from '../../../services/admin.service';
import { FigureSizeSettings, defaultFigureSize } from '../../benchmark/model-comparison/figure-size';
import { DEFAULT_FIGURE_STYLE, FigureStyle, TimelineFigureStyle } from '../../benchmark/model-comparison/figure-style';
import { contrastRatio, providerHue, resolveFigureTheme } from '../../benchmark/model-comparison/figure-theme';
import {
  ACTIVITY_SCOPE_NOTE,
  BENCHMARK_AVAILABILITY_NOTE,
  INCOGNITO_USER_NOTE,
  TELEMETRY_FIGURE_KEYS,
  TELEMETRY_OTHER_COLOR,
  TELEMETRY_SERIES_COLORS,
  TelemetryComposeContext,
  buildFigureSeries,
  buildTelemetryFigure,
  buildTelemetryFigures,
  composeTelemetryFigure,
  telemetryArchiveFilename,
  telemetryAxisRange,
  telemetryChartFilename,
  telemetryFigureChrome,
  telemetryFigureFooter,
  telemetryFigureLayout,
  telemetryFigureSummary,
  telemetryKeysLabel,
  telemetryRangeLabel,
  telemetryTimeTicks,
  telemetryUsageEmpty
} from './telemetry-charts';

const DAY = 86_400_000;

function point(overrides: Partial<AiTelemetryPointDto> = {}): AiTelemetryPointDto {
  return {
    requests: 0, promptTokens: 0, cacheReadTokens: 0, outputTokens: 0, estimatedCost: 0, pricedRequests: 0,
    durationSumMs: 0, durationCount: 0, validAttempts: 0, providerErrors: 0, ...overrides
  };
}

function line(key: string, label: string, points: AiTelemetryPointDto[], overrides: Partial<AiTelemetrySeriesLineDto> = {}): AiTelemetrySeriesLineDto {
  return { key, label, provider: null, isOther: false, points, ...overrides };
}

function activity(overrides: Partial<AiTelemetryActivityPointDto> = {}): AiTelemetryActivityPointDto {
  return {
    activeUsers: 3, newUsers: 1, chatsCreated: 5, confidentialChatsCreated: 1, gnollHackChatsCreated: 2,
    incognitoChatsCreated: 1, userMessages: 20, assistantMessages: 19, incognitoUserMessages: 4, ...overrides
  };
}

/** Three days split by role: a Chat line and a Benchmark line, activity counted from the second day. */
function seriesDto(overrides: Partial<AiTelemetrySeriesDto> = {}): AiTelemetrySeriesDto {
  return {
    bucket: 'day',
    splitBy: 'role',
    bucketStartsUtc: ['2026-10-01T00:00:00Z', '2026-10-02T00:00:00Z', '2026-10-03T00:00:00Z'],
    series: [
      line('chat', 'Chat', [
        point({ requests: 4, promptTokens: 1000, cacheReadTokens: 250, outputTokens: 200, estimatedCost: 0.5, pricedRequests: 4, durationSumMs: 8000, durationCount: 4, validAttempts: 4, providerErrors: 1 }),
        point(),
        point({ requests: 2, promptTokens: 400, cacheReadTokens: 400, outputTokens: 100, estimatedCost: 0.25, pricedRequests: 2, durationSumMs: 3000, durationCount: 2, validAttempts: 2 })
      ]),
      line('benchmark', 'Benchmark', [
        point({ requests: 1, promptTokens: 100, outputTokens: 10, estimatedCost: 0.1, pricedRequests: 1, validAttempts: 1, providerErrors: 1 }),
        point({ requests: 1, promptTokens: 100, outputTokens: 10, estimatedCost: 0.1, pricedRequests: 1 }),
        point()
      ])
    ],
    costRecordedSinceUtc: '2026-09-01T00:00:00Z',
    attemptsRecordedSinceUtc: '2026-09-01T00:00:00Z',
    userId: null,
    userName: null,
    activity: [activity(), activity({ activeUsers: 4 }), activity({ activeUsers: 5 })],
    activityCountedSinceUtc: '2026-10-02',
    ...overrides
  };
}

function context(overrides: Partial<TelemetryComposeContext> = {}, timeline: Partial<TimelineFigureStyle> = {}): TelemetryComposeContext {
  const style: FigureStyle = { ...DEFAULT_FIGURE_STYLE, appearance: { ...DEFAULT_FIGURE_STYLE.appearance, logo: false } };
  return {
    style: { appearance: style.appearance, timeline: { ...style.timeline, ...timeline } },
    theme: resolveFigureTheme(style.appearance),
    logo: null,
    rangeLabel: '2026-10-01 to 2026-10-03',
    keys: 'system',
    cumulative: false,
    userName: null,
    loadedAt: '2026-10-13T08:00:00Z',
    ...overrides
  };
}

function valuesOf(dto: AiTelemetrySeriesDto, key: Parameters<typeof buildFigureSeries>[1], cumulative = false): Record<string, (number | null)[]> {
  return Object.fromEntries(buildFigureSeries(dto, key, { cumulative }).lines.map(entry => [entry.key, [...entry.values]]));
}

describe('telemetry-charts', () => {
  describe('buildFigureSeries', () => {
    it('reads requests, tokens and the ratios from the points, with a gap where a ratio is over nothing', () => {
      const dto = seriesDto();
      expect(valuesOf(dto, 'requests')).toEqual({ chat: [4, 0, 2], benchmark: [1, 1, 0] });
      expect(valuesOf(dto, 'tokens')).toEqual({ chat: [1200, 0, 500], benchmark: [110, 110, 0] });
      expect(valuesOf(dto, 'cacheHit')).toEqual({ chat: [0.25, null, 1], benchmark: [0, 0, null] });
      expect(valuesOf(dto, 'duration')).toEqual({ chat: [2000, null, 1500], benchmark: [null, null, null] });
      expect(valuesOf(dto, 'providerErrors')).toEqual({ chat: [0.25, null, 0] });
    });

    it('sums the cost from the first bucket only when asked', () => {
      const dto = seriesDto();
      expect(valuesOf(dto, 'cost')).toEqual({ chat: [0.5, 0, 0.25], benchmark: [0.1, 0.1, 0] });
      const cumulative = valuesOf(dto, 'cost', true);
      expect(cumulative['chat']).toEqual([0.5, 0.5, 0.75]);
      expect(cumulative['benchmark'][0]).toBeCloseTo(0.1);
      expect(cumulative['benchmark'][1]).toBeCloseTo(0.2);
      expect(cumulative['benchmark'][2]).toBeCloseTo(0.2);
    });

    it('drops the Benchmark role from the provider error rate and says so; the other charts keep it', () => {
      const errors = buildFigureSeries(seriesDto(), 'providerErrors');
      expect(errors.lines.map(entry => entry.key)).toEqual(['chat']);
      expect(errors.notes.map(note => note.text)).toEqual([BENCHMARK_AVAILABILITY_NOTE]);
      expect(buildFigureSeries(seriesDto(), 'requests').lines.map(entry => entry.key)).toEqual(['chat', 'benchmark']);
      // Not split by role: no Benchmark line to drop, and no note.
      const byProvider = seriesDto({ splitBy: 'provider', series: [line('Anthropic', 'Anthropic', [point(), point(), point()], { provider: 'Anthropic' })] });
      expect(buildFigureSeries(byProvider, 'providerErrors').notes).toEqual([]);
    });

    it('notes the unpriced share and a recording that started inside the range', () => {
      const dto = seriesDto({
        costRecordedSinceUtc: '2026-10-02T06:00:00Z',
        attemptsRecordedSinceUtc: '2026-10-02T00:00:00',
        series: [line('chat', 'Chat', [point({ requests: 4, pricedRequests: 3 }), point(), point()])]
      });
      expect(buildFigureSeries(dto, 'cost').notes).toEqual([
        { text: '25 % of requests have no price and are not in the cost.', tone: 'warning' },
        { text: 'Cost recorded since 2026-10-02.', tone: 'info' }
      ]);
      expect(buildFigureSeries(dto, 'providerErrors').notes).toEqual([{ text: 'Attempts recorded since 2026-10-02.', tone: 'info' }]);
      expect(buildFigureSeries(seriesDto(), 'cost').notes).toEqual([]);
    });

    it('reads the activity apart from Split by, with a gap before the counting started', () => {
      const dto = seriesDto({ splitBy: 'provider', series: [] });
      expect(valuesOf(dto, 'activeUsers')).toEqual({ activeUsers: [null, 4, 5], newUsers: [null, 1, 1] });
      expect(valuesOf(dto, 'chats')).toEqual({
        chats: [null, 5, 5], confidential: [null, 1, 1], gnollhack: [null, 2, 2], incognito: [null, 1, 1]
      });
      expect(valuesOf(dto, 'messages')).toEqual({ sent: [null, 20, 20], replies: [null, 19, 19], incognitoSent: [null, 4, 4] });
      const notes = buildFigureSeries(dto, 'activeUsers').notes.map(note => note.text);
      expect(notes).toEqual([ACTIVITY_SCOPE_NOTE, 'Counted since 2026-10-02.']);
      // Counted from before the range: every bucket has a value and there is no counting note.
      const counted = seriesDto({ activityCountedSinceUtc: '2026-01-01' });
      expect(valuesOf(counted, 'activeUsers')['activeUsers']).toEqual([3, 4, 5]);
      expect(buildFigureSeries(counted, 'activeUsers').notes.map(note => note.text)).toEqual([ACTIVITY_SCOPE_NOTE]);
      // Nothing counted yet: every bucket is a gap.
      const uncounted = seriesDto({ activityCountedSinceUtc: null });
      expect(valuesOf(uncounted, 'messages')['sent']).toEqual([null, null, null]);
    });

    it('leaves the incognito lines out for one user', () => {
      const dto = seriesDto({ userId: 'u1', userName: 'alice', activityCountedSinceUtc: '2026-01-01' });
      expect(Object.keys(valuesOf(dto, 'chats'))).toEqual(['chats', 'confidential', 'gnollhack']);
      expect(Object.keys(valuesOf(dto, 'messages'))).toEqual(['sent', 'replies']);
      expect(Object.keys(valuesOf(dto, 'activeUsers'))).toEqual(['activeUsers', 'newUsers']);
      expect(buildFigureSeries(dto, 'messages').notes.map(note => note.text)).toContain(INCOGNITO_USER_NOTE);
      expect(buildFigureSeries(dto, 'activeUsers').notes.map(note => note.text)).not.toContain(INCOGNITO_USER_NOTE);
    });

    it('tells an empty usage series from one with requests', () => {
      expect(telemetryUsageEmpty(seriesDto())).toBe(false);
      expect(telemetryUsageEmpty(seriesDto({ series: [] }))).toBe(true);
      expect(telemetryUsageEmpty(seriesDto({ series: [line('chat', 'Chat', [point(), point(), point()])] }))).toBe(true);
    });
  });

  describe('colors', () => {
    it('keeps every categorical color and the Other gray at 3 : 1 on both themes\' plot grounds', () => {
      const dark = resolveFigureTheme({ ...DEFAULT_FIGURE_STYLE.appearance, theme: 'dark' });
      const light = resolveFigureTheme({ ...DEFAULT_FIGURE_STYLE.appearance, theme: 'light' });
      const grounds = [dark.background!, dark.chart.surface, light.background!, light.chart.surface];
      expect(TELEMETRY_SERIES_COLORS.length).toBe(8);
      expect(new Set(TELEMETRY_SERIES_COLORS).size).toBe(8);
      for (const color of [...TELEMETRY_SERIES_COLORS, TELEMETRY_OTHER_COLOR]) {
        for (const ground of grounds) {
          expect(contrastRatio(color, ground), `${color} on ${ground}`).toBeGreaterThanOrEqual(3);
        }
      }
    });

    it('colors by provider when split by provider, else categorically, and draws Other gray and dashed', () => {
      const ctx = context();
      const byRole = buildTelemetryFigure(seriesDto(), 'requests', ctx);
      expect(byRole.lines.map(entry => entry.color)).toEqual([TELEMETRY_SERIES_COLORS[0], TELEMETRY_SERIES_COLORS[1]]);
      expect(byRole.lines.map(entry => entry.dashed)).toEqual([false, false]);

      const byProvider = seriesDto({
        splitBy: 'provider',
        series: [
          line('Anthropic', 'Anthropic', [point({ requests: 1 }), point(), point()], { provider: 'Anthropic' }),
          line('OpenAI', 'OpenAI', [point({ requests: 1 }), point(), point()], { provider: 'OpenAI' }),
          line('__other', 'Other', [point({ requests: 1 }), point(), point()], { isOther: true })
        ]
      });
      const figure = buildTelemetryFigure(byProvider, 'requests', ctx);
      expect(figure.lines.map(entry => entry.color)).toEqual([
        providerHue('Anthropic', ctx.theme), providerHue('OpenAI', ctx.theme), TELEMETRY_OTHER_COLOR
      ]);
      expect(figure.lines.map(entry => entry.dashed)).toEqual([false, false, true]);
      const datasets = figure.config!.data.datasets as unknown as { borderDash?: number[] }[];
      expect(datasets[2].borderDash).toEqual([6, 4]);
      expect(datasets[0].borderDash).toEqual([]);
      // The activity charts are never colored by provider.
      expect(buildTelemetryFigure(byProvider, 'activeUsers', context()).lines[0].color).toBe(TELEMETRY_SERIES_COLORS[0]);
    });
  });

  describe('the chart', () => {
    it('builds nine line charts over a linear time axis titled Date (UTC), with nothing to draw as no configuration', () => {
      const figures = buildTelemetryFigures(seriesDto(), context());
      expect(figures.map(figure => figure.key)).toEqual(TELEMETRY_FIGURE_KEYS);
      const requests = figures[0];
      expect(requests.config!.type).toBe('line');
      const scales = requests.config!.options!.scales as unknown as Record<string, { type: string; min: number; max: number; title: { text: string } }>;
      const x = scales['x'];
      expect(x.type).toBe('linear');
      expect(x.title.text).toBe('Date (UTC)');
      expect([x.min, x.max]).toEqual([Date.UTC(2026, 9, 1), Date.UTC(2026, 9, 3)]);
      const points = requests.config!.data.datasets[0].data as unknown as { x: number; y: number | null }[];
      expect(points.map(entry => entry.x)).toEqual([Date.UTC(2026, 9, 1), Date.UTC(2026, 9, 2), Date.UTC(2026, 9, 3)]);
      // Every activity bucket a gap: nothing to draw.
      const empty = buildTelemetryFigure(seriesDto({ activityCountedSinceUtc: null }), 'messages', context());
      expect(empty.config).toBeNull();
    });

    it('spans one bucket by its own length', () => {
      expect(telemetryAxisRange([Date.UTC(2026, 9, 1)], 'week')).toEqual({ min: Date.UTC(2026, 9, 1), max: Date.UTC(2026, 9, 8) });
      expect(telemetryAxisRange([], 'day')).toBeNull();
    });

    it('ticks day, week and month spans in UTC', () => {
      const days = telemetryTimeTicks(Date.UTC(2026, 9, 1), Date.UTC(2026, 9, 14), 800);
      expect(days.values.length).toBeGreaterThan(1);
      expect(days.values.length).toBeLessThanOrEqual(8);
      // On UTC midnights, labeled by month and day.
      expect(days.values.every(value => value % DAY === 0)).toBe(true);
      expect(days.labels.every(label => /^Oct \d{1,2}$/.test(label))).toBe(true);

      const weeks = telemetryTimeTicks(Date.UTC(2026, 3, 6), Date.UTC(2026, 8, 28), 800);
      expect(weeks.values.length).toBeGreaterThan(1);
      expect(weeks.values.length).toBeLessThanOrEqual(8);
      // Under a year: month and day.
      expect(weeks.labels.every(label => /^[A-Z][a-z]{2} \d{1,2}$/.test(label))).toBe(true);

      const months = telemetryTimeTicks(Date.UTC(2024, 0, 1), Date.UTC(2026, 9, 1), 800);
      expect(months.values.every(value => new Date(value).getUTCDate() === 1)).toBe(true);
      expect(months.labels.every(label => /^\d{4}-\d{2}$/.test(label))).toBe(true);
    });
  });

  describe('chrome and footer', () => {
    it('heads a chart with its title and the dates badge, and the cost chart with the pricing badge too', () => {
      const ctx = context();
      const requests = telemetryFigureChrome(buildTelemetryFigure(seriesDto(), 'requests', ctx), ctx);
      expect(requests.title).toBe('Requests');
      expect(requests.badges.map(badge => [badge.kind, badge.text])).toEqual([['dates', '2026-10-01 to 2026-10-03 · by day']]);
      expect(requests.direction).toBeUndefined();
      expect(requests.detail).toBe('');

      const cost = telemetryFigureChrome(buildTelemetryFigure(seriesDto(), 'cost', ctx), ctx);
      expect(cost.badges.map(badge => badge.text)).toEqual(['2026-10-01 to 2026-10-03 · by day', 'Estimated, USD']);

      // The dates badge hides; the pricing badge has no toggle.
      const hidden = context({}, { hiddenBadges: ['dates', 'pricing'] });
      expect(telemetryFigureChrome(buildTelemetryFigure(seriesDto(), 'cost', hidden), hidden).badges.map(badge => badge.text))
        .toEqual(['Estimated, USD']);
    });

    it('names a cumulative cost and the filtered user on the detail line', () => {
      const ctx = context({ cumulative: true, userName: 'alice' });
      expect(telemetryFigureChrome(buildTelemetryFigure(seriesDto(), 'cost', ctx), ctx).detail).toBe('Cumulative · User: alice');
      expect(telemetryFigureChrome(buildTelemetryFigure(seriesDto(), 'requests', ctx), ctx).detail).toBe('User: alice');
    });

    it('writes the source footer, or none while the footer is off', () => {
      const footer = telemetryFigureFooter(context({ keys: 'both' }));
      expect(footer.label).toBe('Source');
      expect(footer.suite).toBe('Overseer AI Telemetry · All keys');
      expect(footer.computedAt).not.toBe('');
      expect(telemetryFigureFooter(context({}, { footer: false }))).toEqual({ suite: '', computedAt: '' });
      expect(telemetryKeysLabel('system')).toBe('System AI configurations');
      expect(telemetryKeysLabel('user')).toBe('User keys');
    });

    it('summarizes the badges, lines and notes for assistive technology', () => {
      const ctx = context();
      const figure = buildTelemetryFigure(seriesDto(), 'providerErrors', ctx);
      const summary = telemetryFigureSummary(figure, telemetryFigureChrome(figure, ctx));
      expect(summary).toBe(`2026-10-01 to 2026-10-03 · by day. Lines: Chat. ${BENCHMARK_AVAILABILITY_NOTE}`);
    });

    it('labels the range', () => {
      expect(telemetryRangeLabel('2026-09-01', '2026-10-13')).toBe('2026-09-01 to 2026-10-13');
      expect(telemetryRangeLabel('2026-09-01', '2026-09-01')).toBe('2026-09-01');
      expect(telemetryRangeLabel('2026-09-01', '')).toBe('from 2026-09-01');
      expect(telemetryRangeLabel('', '2026-10-13')).toBe('until 2026-10-13');
      expect(telemetryRangeLabel('', '')).toBe('All dates');
    });
  });

  describe('layout and composition', () => {
    /** A small custom size at 100 % density, so the composition stays quick. */
    const small: FigureSizeSettings = {
      ...defaultFigureSize(1), resolutionId: 'custom', customWidthPx: 640, customHeightPx: 400, densitySelection: 1
    };

    it('lays out a chart at the chart size, or refuses a size that is out of range', () => {
      const ctx = context();
      const figure = buildTelemetryFigure(seriesDto(), 'requests', ctx);
      const { layout, refusal } = telemetryFigureLayout(figure, ctx, small);
      expect(refusal).toBeNull();
      expect([layout!.pixelWidth, layout!.pixelHeight]).toEqual([640, 400]);
      const refused = telemetryFigureLayout(figure, ctx, { ...small, customWidthPx: 10 });
      expect(refused.layout).toBeNull();
      expect(refused.refusal).not.toBe('');
    });

    it('composes a chart at its layout\'s pixel size, and nothing for a chart with nothing to draw', async () => {
      const ctx = context();
      const figure = buildTelemetryFigure(seriesDto(), 'requests', ctx);
      const { layout } = telemetryFigureLayout(figure, ctx, small);
      const canvas = await composeTelemetryFigure(figure, ctx, layout!);
      expect(canvas).not.toBeNull();
      expect([canvas!.width, canvas!.height]).toEqual([640, 400]);
      const empty = buildTelemetryFigure(seriesDto({ activityCountedSinceUtc: null }), 'chats', ctx);
      expect(await composeTelemetryFigure(empty, ctx, layout!)).toBeNull();
    });
  });

  describe('file names', () => {
    it('names a chart and the archive with the export time stamp', () => {
      const now = new Date(2026, 9, 13, 9, 5, 7);
      expect(telemetryChartFilename('cacheHit', 'png', now)).toBe('ai-telemetry_cacheHit_20261013_090507.png');
      expect(telemetryChartFilename('requests', 'webp', now)).toBe('ai-telemetry_requests_20261013_090507.webp');
      expect(telemetryArchiveFilename(now)).toBe('ai-telemetry_charts_20261013_090507.zip');
    });
  });
});
