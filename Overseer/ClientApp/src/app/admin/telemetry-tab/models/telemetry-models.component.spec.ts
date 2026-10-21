import { ComponentFixture, TestBed } from '@angular/core/testing';
import {
  AiTelemetryKeySource,
  AiTelemetrySummaryDto,
  AiUsageBreakdownRowDto,
  ChatProviderAvailabilityDto,
  OverseerActivityDto
} from '../../../services/admin.service';
import { TELEMETRY_BREAKDOWN_STORAGE_KEY, TelemetryBreakdownView, defaultBreakdownView } from '../telemetry-breakdown';
import { TelemetryModelUsersRef, TelemetryModelsComponent } from './telemetry-models.component';

const availability = (overrides: Partial<ChatProviderAvailabilityDto> = {}): ChatProviderAvailabilityDto => ({
  recorded: false, recordedSinceUtc: null, validAttempts: 0, providerErrors: 0, unattributedErrors: 0, ourSideFailures: 0,
  providerErrorRate: null, low: null, high: null, inclusiveRate: null,
  overloaded: 0, serverErrors: 0, timeouts: 0, streamFailures: 0, rateLimitedUnattributed: 0, transportUnattributed: 0,
  calls: 0, firstAttemptSuccessCalls: 0, failedCalls: 0, chatTurns: 0, chatTurnsEndedByProviderError: 0,
  titleValidAttempts: 0, titleProviderErrors: 0, titleRequests: 0, titleRequestsFailed: 0,
  ...overrides
});

const activity = (overrides: Partial<OverseerActivityDto> = {}): OverseerActivityDto => ({
  activeUsers: 0, newUsers: 0, chatsCreated: 0, confidentialChatsCreated: 0, gnollHackChatsCreated: 0,
  userMessages: 0, assistantMessages: 0, incognitoChatsCreated: 0, incognitoUserMessages: 0, countedSinceUtc: null,
  ...overrides
});

const summary = (overrides: Partial<AiTelemetrySummaryDto> = {}): AiTelemetrySummaryDto => ({
  keySource: 'system',
  totalRequests: 10, totalChatRequests: 6, totalTitleRequests: 2, totalBenchmarkRequests: 2,
  totalPromptTokens: 0, totalUncachedInputTokens: 0, totalOutputTokens: 0, totalCacheReadTokens: 0, totalCacheCreationTokens: 0,
  cacheHitRatio: 0, avgDurationMs: 0, totalEstimatedCost: 0, pricedRequests: 0, costRecordedSinceUtc: null,
  rows: [], chatAvailability: availability(), activity: activity(),
  ...overrides
});

/** A system-key Chat row of one request with every figure at zero. */
const row = (overrides: Partial<AiUsageBreakdownRowDto> = {}): AiUsageBreakdownRowDto => ({
  keySource: 'system', userId: null, userName: null,
  provider: 'Anthropic', modelId: 'claude-x', modelName: 'Claude X',
  roleContext: 1, roleGroup: 'chat', thinkingLevel: 'high',
  requests: 1, promptTokens: 0, uncachedInputTokens: 0, cacheReadTokens: 0, cacheCreationTokens: 0, outputTokens: 0,
  durationSumMs: 0, durationCount: 0, estimatedCost: 0, pricedRequests: 0,
  validAttempts: 0, providerErrors: 0, unattributedErrors: 0, ourSideFailures: 0,
  ...overrides
});

const START = '2026-09-01';
const END = '2026-09-30';

describe('TelemetryModelsComponent', () => {
  let fixture: ComponentFixture<TelemetryModelsComponent>;
  let component: TelemetryModelsComponent;
  /** Every view the panel emitted, in order. */
  let views: TelemetryBreakdownView[];
  /** Every Usage by user request the panel emitted, in order. */
  let usageRequests: TelemetryModelUsersRef[];

  beforeEach(async () => {
    localStorage.removeItem(TELEMETRY_BREAKDOWN_STORAGE_KEY);
    await TestBed.configureTestingModule({ imports: [TelemetryModelsComponent] }).compileComponents();
    fixture = TestBed.createComponent(TelemetryModelsComponent);
    component = fixture.componentInstance;
    views = [];
    usageRequests = [];
    // Stands in for the tab, which stores an emitted view and passes it back.
    component.viewChange.subscribe(view => {
      views.push(view);
      fixture.componentRef.setInput('view', view);
    });
    component.usageByUser.subscribe(ref => usageRequests.push(ref));
  });

  afterEach(() => {
    localStorage.removeItem(TELEMETRY_BREAKDOWN_STORAGE_KEY);
  });

  const host = () => fixture.nativeElement as HTMLElement;

  /** Renders the panel with the tab's inputs: a summary, the Keys filter, the range and the default view. */
  const render = (data: AiTelemetrySummaryDto = summary(), keys: AiTelemetryKeySource = 'system') => {
    fixture.componentRef.setInput('summary', data);
    fixture.componentRef.setInput('keys', keys);
    fixture.componentRef.setInput('start', START);
    fixture.componentRef.setInput('end', END);
    fixture.componentRef.setInput('view', { ...defaultBreakdownView(), keys });
    fixture.detectChanges();
  };

  /** What the tab does when the Keys select changes: a new summary, the Keys filter and the view with it. */
  const changeKeys = (data: AiTelemetrySummaryDto, keys: AiTelemetryKeySource) => {
    fixture.componentRef.setInput('summary', data);
    fixture.componentRef.setInput('keys', keys);
    fixture.componentRef.setInput('view', { ...component.view(), keys });
    fixture.detectChanges();
  };

  const lastView = (): TelemetryBreakdownView => views[views.length - 1];

  /** The breakdown's model cards, without the total. */
  const cards = (): HTMLElement[] =>
    Array.from(host().querySelectorAll<HTMLElement>('.tl-cards > li > article.tl-card:not(.tl-card-total)'));

  const status = () => host().querySelector('.tl-breakdown-status')?.textContent?.trim() ?? '';

  const normalize = (text: string | null | undefined) => (text ?? '').replace(/\s+/g, ' ').trim();

  /** The texts of a card's badges matching the selector, visually hidden prefix included. */
  const badgeTexts = (card: HTMLElement, selector: string): string[] =>
    Array.from(card.querySelectorAll(`.tl-card-badges ${selector}`)).map(b => normalize(b.textContent));

  it('hides the cards of an unchecked role and updates the status line', () => {
    render(summary({
      rows: [
        row({ requests: 5 }),
        row({ provider: 'OpenAI', modelId: 'gpt-x', modelName: 'GPT X', roleGroup: 'title', roleContext: 2, requests: 3 })
      ]
    }));

    expect(cards().length).toBe(2);
    expect(status()).toBe('Showing 2 entries · 8 requests');

    const title = host().querySelector('#tl-role-title') as HTMLInputElement;
    expect(title.closest('label')?.textContent).toContain('3');
    title.click();
    fixture.detectChanges();

    expect(cards().length).toBe(1);
    expect(cards()[0].querySelector('h5')?.textContent?.trim()).toBe('Claude X');
    expect(status()).toBe('One entry · 5 of 8 requests');
    expect(lastView().roles).toEqual(['chat', 'benchmark']);
    expect(localStorage.getItem(TELEMETRY_BREAKDOWN_STORAGE_KEY)).toBeNull();
  });

  it('lists Other only while it has rows, and says when no role is selected', () => {
    render();
    expect(host().querySelector('#tl-role-other')).toBeNull();

    fixture.componentRef.setInput('summary', summary({ rows: [row({ roleGroup: 'other', roleContext: 9 })] }));
    fixture.detectChanges();
    expect(host().querySelector('#tl-role-other')).not.toBeNull();
    expect(host().querySelector('.tl-empty')?.textContent).toContain('No usage for the selected roles in this range.');

    for (const group of ['chat', 'title', 'benchmark']) {
      (host().querySelector(`#tl-role-${group}`) as HTMLInputElement).click();
      fixture.detectChanges();
    }
    expect(host().querySelector('.tl-empty')?.textContent).toContain('No role is selected.');
  });

  it('says so when the range has no usage at all', () => {
    render();
    expect(host().querySelector('.tl-empty')?.textContent).toContain('No AI usage logged for the selected date range.');
    expect(host().querySelector('#tl-thinking-select')).toBeNull();
  });

  it('splits a model by thinking level with Show separately', () => {
    render(summary({
      rows: [row({ thinkingLevel: 'high', requests: 4 }), row({ thinkingLevel: '', requests: 2 })]
    }));
    expect(cards().length).toBe(1);
    expect(cards()[0].textContent).not.toContain('Thinking:');

    const select = host().querySelector('#tl-thinking-select') as HTMLSelectElement;
    expect(host().querySelector('label[for="tl-thinking-select"]')).not.toBeNull();
    select.value = 'separate';
    select.dispatchEvent(new Event('change'));
    fixture.detectChanges();

    const [high, none] = cards();
    expect(badgeTexts(high, '.thinking-badge')).toEqual(['thinking level High']);
    expect(badgeTexts(none, '.thinking-badge')).toEqual([]);
    expect(badgeTexts(none, '.config-badge')).toEqual(['thinking level None sent']);
  });

  it('shows one thinking badge per recorded level of a grouped card, in the catalog order', () => {
    render(summary({
      rows: [row({ thinkingLevel: 'high' }), row({ thinkingLevel: 'medium' }), row({ thinkingLevel: '' }), row({ thinkingLevel: null })]
    }));

    expect(cards().length).toBe(1);
    expect(badgeTexts(cards()[0], '.thinking-badge')).toEqual(['thinking level Medium', 'thinking level High']);
    expect(badgeTexts(cards()[0], '.config-badge')).toEqual([]);
  });

  it('gives each card a role badge in its role color', () => {
    render(summary({
      rows: [
        row({ requests: 4 }),
        row({ modelId: 'b', modelName: 'B', roleGroup: 'title', roleContext: 2, requests: 3 }),
        row({ modelId: 'c', modelName: 'C', roleGroup: 'benchmark', roleContext: 4, requests: 2 }),
        row({ modelId: 'd', modelName: 'D', roleGroup: 'other', roleContext: 9, requests: 1 })
      ]
    }));
    (host().querySelector('#tl-role-other') as HTMLInputElement).click();
    fixture.detectChanges();

    const roleBadges = cards().map(c => c.querySelector<HTMLElement>('.tl-card-badges .status-badge')!);
    expect(roleBadges.map(b => normalize(b.textContent))).toEqual(['role Chat', 'role Title Generation', 'role Benchmark', 'role Other']);
    expect(roleBadges.map(b => Array.from(b.classList).find(c => c.startsWith('badge-role-'))))
      .toEqual(['badge-role-chat', 'badge-role-title', 'badge-role-benchmark', 'badge-role-other']);
    expect(cards()[0].querySelector('.tl-card-badges')?.getAttribute('role')).toBe('list');
  });

  it('titles a card with the model name and shows the id and the provider under it, else titles it with the id', () => {
    render(summary({
      rows: [row({ requests: 2 }), row({ modelId: 'mystery-1', modelName: null })]
    }));

    const [named, unnamed] = cards();
    expect(named.querySelector('h5')?.textContent?.trim()).toBe('Claude X');
    expect(named.querySelector('.tl-card-identity .gh-model-card-model-id')?.textContent?.trim()).toBe('claude-x');
    expect(named.querySelector('.tl-card-identity app-provider-badge')).not.toBeNull();
    expect(unnamed.querySelector('h5')?.textContent?.trim()).toBe('mystery-1');
    expect(unnamed.querySelector('.tl-card-identity app-provider-badge')).not.toBeNull();
    expect(unnamed.querySelector('.tl-card-identity .gh-model-card-model-id')).toBeNull();
  });

  it('names the user of a user-key card and marks system cards under All keys', () => {
    const data = summary({
      keySource: 'both',
      rows: [row({ requests: 3 }), row({ keySource: 'user', userId: 'u-1', userName: 'alice', requests: 2 })]
    });
    render(data);

    changeKeys(data, 'both');

    expect(cards().map(c => normalize(c.querySelector('.tl-key-badge')?.textContent))).toEqual(['key System key', 'key User key: alice']);
  });

  // --- Breakdown search, provider toggles and sort ---

  const threeProviders = () => summary({
    rows: [
      row({ provider: 'Google', modelId: 'gemini-flash', modelName: 'Gemini Flash', requests: 5, estimatedCost: 3, pricedRequests: 5 }),
      row({ provider: 'Anthropic', modelId: 'claude-opus', modelName: 'Claude Opus', requests: 3, estimatedCost: 1, pricedRequests: 3 }),
      row({ provider: 'OpenAI', modelId: 'gpt-x', modelName: 'GPT X', requests: 2, estimatedCost: 2, pricedRequests: 2 }),
      row({ provider: 'OpenAI', modelId: 'gpt-y', modelName: 'GPT Y', requests: 1, pricedRequests: 0 })
    ]
  });

  const searchField = () => host().querySelector('#tl-breakdown-search') as HTMLInputElement;
  const titlesShown = () => cards().map(c => c.querySelector('h5')?.textContent?.trim());

  const typeSearch = (text: string) => {
    searchField().value = text;
    searchField().dispatchEvent(new Event('input'));
    fixture.detectChanges();
  };

  it('filters the cards by the search after a short quiet, marks the match and updates the status line', () => {
    vi.useFakeTimers();
    try {
      render(threeProviders());
      expect(host().querySelector('label[for="tl-breakdown-search"]')?.textContent).toContain('Search by model name');

      typeSearch('flash');
      expect(cards().length).toBe(4);

      vi.advanceTimersByTime(200);
      fixture.detectChanges();

      expect(titlesShown()).toEqual(['Gemini Flash']);
      expect(cards()[0].querySelector('h5 mark.model-option-match')?.textContent).toBe('Flash');
      expect(cards()[0].querySelector('.gh-model-card-model-id mark.model-option-match')?.textContent).toBe('flash');
      expect(status()).toBe('Showing 1 of 4 entries · 5 of 11 requests');
      expect(host().querySelector('.tl-card-total .tl-metrics')?.textContent).toContain('5');
      // The search is never part of the view, so it is never handed to the tab to store.
      expect(views).toEqual([]);

      typeSearch('zzz');
      vi.advanceTimersByTime(200);
      fixture.detectChanges();
      expect(cards().length).toBe(0);
      expect(status()).toBe('No entries match "zzz".');
      expect(host().querySelector('.tl-no-match .tl-empty')?.textContent?.trim()).toBe('No entries match "zzz".');
    }
    finally {
      vi.useRealTimers();
    }
  });

  it('clears the search on Escape with text, and leaves Escape alone without it', () => {
    vi.useFakeTimers();
    try {
      render(threeProviders());
      typeSearch('gpt');
      vi.advanceTimersByTime(200);
      fixture.detectChanges();
      expect(cards().length).toBe(2);

      const escape = new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true });
      const stop = vi.spyOn(escape, 'stopPropagation');
      searchField().dispatchEvent(escape);
      fixture.detectChanges();

      expect(stop).toHaveBeenCalled();
      expect(escape.defaultPrevented).toBe(true);
      expect(searchField().value).toBe('');
      expect(cards().length).toBe(4);

      const second = new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true });
      const stopSecond = vi.spyOn(second, 'stopPropagation');
      searchField().dispatchEvent(second);
      expect(stopSecond).not.toHaveBeenCalled();
      expect(second.defaultPrevented).toBe(false);
    }
    finally {
      vi.useRealTimers();
    }
  });

  it('shows provider toggles from two providers, ORs them, stores them, and Clear filters clears them and the search', () => {
    vi.useFakeTimers();
    try {
      render(threeProviders());

      const group = host().querySelector('.tl-providers') as HTMLElement;
      expect(group.getAttribute('role')).toBe('group');
      expect(group.getAttribute('aria-label')).toBe('Filter by provider');
      const toggle = (key: string) => host().querySelector(`#tl-provider-${key}`) as HTMLButtonElement;
      expect(Array.from(group.querySelectorAll('button')).map(b =>
        [b.querySelector('span')?.textContent, normalize(b.querySelector('.model-picker-provider-count')?.textContent)]))
        .toEqual([['Anthropic', '1 entry'], ['Google', '1 entry'], ['OpenAI', '2 entries']]);
      expect(toggle('google').getAttribute('aria-pressed')).toBe('false');
      expect(host().querySelector('#tl-breakdown-clear')).toBeNull();

      toggle('google').click();
      fixture.detectChanges();
      toggle('openai').click();
      fixture.detectChanges();

      expect(toggle('google').getAttribute('aria-pressed')).toBe('true');
      expect(titlesShown()).toEqual(['Gemini Flash', 'GPT X', 'GPT Y']);
      expect(status()).toBe('Showing 3 of 4 entries · 8 of 11 requests');
      expect(lastView().providers).toEqual(['google', 'openai']);

      typeSearch('gpt');
      vi.advanceTimersByTime(200);
      fixture.detectChanges();
      expect(titlesShown()).toEqual(['GPT X', 'GPT Y']);

      (host().querySelector('#tl-breakdown-clear') as HTMLButtonElement).click();
      fixture.detectChanges();

      expect(cards().length).toBe(4);
      expect(searchField().value).toBe('');
      expect(toggle('google').getAttribute('aria-pressed')).toBe('false');
      expect(document.activeElement).toBe(searchField());
      expect(host().querySelector('#tl-breakdown-clear')).toBeNull();
      expect(lastView().providers).toEqual([]);
    }
    finally {
      vi.useRealTimers();
    }
  });

  it('hides the provider toggles with one provider', () => {
    render(summary({ rows: [row(), row({ modelId: 'b', modelName: 'B' })] }));
    expect(host().querySelector('.tl-providers')).toBeNull();
  });

  it('sorts the cards from the Sort popover, keeps All shown last, and stores the sort', () => {
    render(threeProviders());

    const trigger = host().querySelector('#tl-sort-trigger') as HTMLButtonElement;
    expect(trigger.matches('button.btn-ghost[type="button"]')).toBe(true);
    expect(normalize(trigger.textContent)).toBe('Sort: Requests, highest first');
    expect(trigger.getAttribute('popovertarget')).toBe('tl-sort-panel');
    expect(trigger.getAttribute('aria-expanded')).toBe('false');
    const panel = host().querySelector('#tl-sort-panel') as HTMLElement;
    expect(panel.getAttribute('popover')).toBe('auto');
    expect(Array.from(panel.querySelectorAll('fieldset legend')).map(l => l.textContent?.trim())).toEqual(['Sort by']);
    expect(panel.querySelector('#tl-sort-order-label')?.textContent?.trim()).toBe('Order');
    const reset = host().querySelector('#tl-sort-reset') as HTMLButtonElement;
    expect(reset.getAttribute('aria-disabled')).toBe('true');

    (host().querySelector('#tl-sort-field-cost') as HTMLInputElement).click();
    fixture.detectChanges();
    expect(titlesShown()).toEqual(['Gemini Flash', 'GPT X', 'Claude Opus', 'GPT Y']);

    (host().querySelector('#tl-sort-direction-asc') as HTMLInputElement).click();
    fixture.detectChanges();

    expect(titlesShown()).toEqual(['Claude Opus', 'GPT X', 'Gemini Flash', 'GPT Y']);
    expect(host().querySelector('.tl-cards > li:last-child article')?.classList.contains('tl-card-total')).toBe(true);
    expect(normalize(trigger.textContent)).toBe('Sort: Est. cost, lowest first');
    expect(reset.hasAttribute('aria-disabled')).toBe(false);
    expect(lastView().sort).toEqual({ field: 'cost', direction: 'asc' });

    (host().querySelector('#tl-sort-field-name') as HTMLInputElement).click();
    fixture.detectChanges();
    expect(normalize(host().querySelector('label:has(#tl-sort-direction-asc)')?.textContent)).toBe('A–Z');
    expect(titlesShown()).toEqual(['Claude Opus', 'Gemini Flash', 'GPT X', 'GPT Y']);

    reset.click();
    fixture.detectChanges();
    expect(normalize(trigger.textContent)).toBe('Sort: Requests, highest first');
    expect(reset.getAttribute('aria-disabled')).toBe('true');
    expect(host().querySelector('#tl-sort-reset')).toBe(reset);
  });

  describe('Sort popover layout', () => {
    /** Opens the Sort popover from its trigger, runs the check, and closes it again. */
    const withSortPanel = (check: (panel: HTMLElement) => void) => {
      render(threeProviders());
      (host().querySelector('#tl-sort-trigger') as HTMLButtonElement).click();
      fixture.detectChanges();
      const panel = host().querySelector('#tl-sort-panel') as HTMLElement;
      try {
        expect(panel.matches(':popover-open')).toBe(true);
        check(panel);
      } finally {
        try { panel.hidePopover(); } catch { /* already closed */ }
      }
    };

    const options = (panel: HTMLElement) => Array.from(panel.querySelectorAll<HTMLElement>('.tl-sort-fields .gh-facet-option'));
    const lefts = (panel: HTMLElement) => options(panel).map(o => Math.round(o.getBoundingClientRect().left));

    it('lays Sort by out in two column-major columns of at most six rows', () => {
      withSortPanel(panel => {
        const all = lefts(panel);
        const columns = [...new Set(all)].sort((a, b) => a - b);
        expect(columns.length).toBe(2);
        for (const left of columns) {
          expect(all.filter(l => l === left).length).toBeLessThanOrEqual(6);
        }
        const firstColumn = options(panel).filter((_, i) => all[i] === columns[0]).map(o => normalize(o.textContent));
        expect(firstColumn[0]).toBe('Requests');
      });
    });

    it('fits its content without scrolling', () => {
      withSortPanel(panel => {
        expect(panel.scrollHeight).toBeLessThanOrEqual(360);
        expect(panel.scrollHeight - panel.clientHeight).toBeLessThanOrEqual(1);
      });
    });

    it('puts Order and Reset sorting on one row', () => {
      withSortPanel(panel => {
        const segments = panel.querySelector('.tl-sort-segments')!.getBoundingClientRect();
        const reset = panel.querySelector('#tl-sort-reset')!.getBoundingClientRect();
        expect(reset.top).toBeLessThan(segments.bottom);
        expect(reset.bottom).toBeGreaterThan(segments.top);
      });
    });

    it('falls back to one column when the panel is narrow', () => {
      withSortPanel(panel => {
        panel.style.inlineSize = '18rem';
        void panel.offsetWidth;
        expect(new Set(lefts(panel)).size).toBe(1);
      });
    });

    it('groups Order as a labeled radio group of two focusable radios', () => {
      withSortPanel(panel => {
        const group = panel.querySelector('[role="radiogroup"]') as HTMLElement;
        expect(group).not.toBeNull();
        expect(panel.querySelector('#' + group.getAttribute('aria-labelledby'))?.textContent?.trim()).toBe('Order');
        const radios = Array.from(group.querySelectorAll<HTMLInputElement>('input[type="radio"]'));
        expect(radios.length).toBe(2);
        for (const radio of radios) {
          expect(radio.name).toBe('tl-sort-direction');
          radio.focus();
          expect(document.activeElement).toBe(radio);
        }
      });
    });
  });

  it('keeps a long user-key badge on one line, cut with an ellipsis inside its card', () => {
    render(summary({
      keySource: 'user',
      rows: [row({ keySource: 'user', userId: 'u-1', userName: 'a'.repeat(60) })]
    }));
    host().style.width = '420px';
    fixture.detectChanges();
    void host().offsetWidth;

    const card = cards()[0];
    const badge = card.querySelector('.tl-key-badge') as HTMLElement;
    expect(badge.getClientRects().length).toBe(1);
    expect(getComputedStyle(badge).whiteSpace).toBe('nowrap');
    expect(badge.scrollWidth).toBeGreaterThan(badge.clientWidth);
    expect(badge.getBoundingClientRect().right).toBeLessThanOrEqual(card.getBoundingClientRect().right);
  });

  it('is a labeled list of articles, each labeled by its title, ending in the All shown total', () => {
    render(summary({ rows: [row({ requests: 2, outputTokens: 10 }), row({ modelId: 'b', modelName: 'B', outputTokens: 5 })] }));

    const list = host().querySelector('ul.tl-cards') as HTMLElement;
    expect(list.getAttribute('role')).toBe('list');
    expect(list.getAttribute('aria-label')).toBe('Usage by model and role');
    expect(host().querySelector('h4')).toBeNull();

    for (const article of Array.from(list.querySelectorAll<HTMLElement>(':scope > li > article'))) {
      const title = host().querySelector('#' + article.getAttribute('aria-labelledby'));
      expect(title?.tagName).toBe('H5');
    }
    const total = list.querySelector(':scope > li:last-child > article.tl-card-total') as HTMLElement;
    expect(total.querySelector('h5')?.textContent?.trim()).toBe('All shown');
    expect(total.querySelector('.tl-metrics')?.textContent).toContain('15');
  });

  it('shows provider errors for Chat with the count and one-in, No attempts at none, and In GnollBench for Benchmark', () => {
    render(summary({
      rows: [
        row({ requests: 9, validAttempts: 200, providerErrors: 1, pricedRequests: 9, estimatedCost: 2 }),
        row({ modelId: 'b', modelName: 'B', roleGroup: 'title', roleContext: 2, requests: 5, pricedRequests: 0 }),
        row({ modelId: 'c', modelName: 'C', roleGroup: 'benchmark', roleContext: 4, requests: 3 })
      ]
    }));

    const errors = (card: HTMLElement) => card.querySelector('.tl-metric-errors')?.textContent?.replace(/\s+/g, ' ').trim();
    const [chat, title, benchmark] = cards();
    expect(errors(chat)).toContain('0.50%');
    expect(errors(chat)).toContain('1 of 200 attempts');
    expect(errors(chat)).toContain('about 1 in 200');
    expect(errors(title)).toContain('No attempts');
    expect(errors(benchmark)).toContain('In GnollBench');

    // A placeholder value, In GnollBench, No attempts and an unpriced cost are grayed; a rate and a priced cost are not.
    const value = (card: HTMLElement, metric: string) =>
      card.querySelector<HTMLElement>(`.tl-metric-${metric} dd:not(.tl-metric-note)`)!;
    expect(value(chat, 'errors').classList.contains('tl-metric-muted')).toBe(false);
    expect(value(chat, 'cost').classList.contains('tl-metric-muted')).toBe(false);
    expect(value(title, 'errors').textContent?.trim()).toBe('No attempts');
    expect(value(title, 'errors').classList.contains('tl-metric-muted')).toBe(true);
    expect(value(title, 'cost').textContent?.trim()).toBe('—');
    expect(value(title, 'cost').classList.contains('tl-metric-muted')).toBe(true);
    expect(value(benchmark, 'errors').textContent?.trim()).toBe('In GnollBench');
    expect(value(benchmark, 'errors').classList.contains('tl-metric-muted')).toBe(true);
  });

  it('shows a partial cost with its unpriced qualifier', () => {
    render(summary({ rows: [row({ requests: 4, pricedRequests: 3, estimatedCost: 1.5 })] }));

    const cost = cards()[0].querySelector('.tl-metric-cost')?.textContent ?? '';
    expect(cost).toContain('$1.50');
    expect(cost).toContain('1 unpriced');
  });

  it('lays the nine metrics out in 9, 5, 5 and 2 columns as the list narrows, lined up with the total', () => {
    render(summary({ rows: [row({ requests: 2 }), row({ modelId: 'b', modelName: 'A much longer model name' })] }));

    const metrics = () => cards()[0].querySelector<HTMLElement>('.tl-metrics')!;
    const totalMetrics = () => host().querySelector<HTMLElement>('.tl-card-total .tl-metrics')!;
    const columns = (width: string): number => {
      host().style.width = width;
      fixture.detectChanges();
      void host().offsetWidth;
      return getComputedStyle(metrics()).gridTemplateColumns.split(' ').length;
    };

    expect(columns('1700px')).toBe(9);
    expect(totalMetrics().getBoundingClientRect().left).toBe(metrics().getBoundingClientRect().left);
    expect(['1300px', '900px', '380px'].map(columns)).toEqual([5, 5, 2]);
  });

  // --- Usage by user ---

  const usersButtons = (): HTMLButtonElement[] =>
    Array.from(host().querySelectorAll<HTMLButtonElement>('.tl-cards button.tl-card-users'));

  it('gives each model card a Usage by user button, and the total card none', () => {
    render(summary({
      rows: [row({ requests: 3 }), row({ modelId: 'b', modelName: 'B', roleGroup: 'title', roleContext: 2 })]
    }));

    expect(cards().map(c => c.querySelectorAll('button.tl-card-users').length)).toEqual([1, 1]);
    expect(host().querySelector('.tl-card-total button')).toBeNull();
    const button = usersButtons()[0];
    expect(button.matches('.action-btn[type="button"][aria-haspopup="dialog"]')).toBe(true);
    expect(button.getAttribute('aria-label')).toBe('Usage by user: Claude X · Chat');
    expect(button.querySelector('svg')?.getAttribute('aria-hidden')).toBe('true');
    expect(button.closest('.tl-card-actions')?.getAttribute('role')).toBe('group');
    expect(button.closest('.tl-card-actions')?.getAttribute('aria-label')).toBe('Actions for Claude X · Chat');
    const tip = host().querySelector('#' + button.getAttribute('interestfor'));
    expect(tip?.textContent?.trim()).toBe('Usage by user');
    expect(tip?.getAttribute('popover')).toBe('hint');
  });

  it('names each Usage by user button apart: by level under Show separately, by key under All keys', () => {
    render(summary({
      rows: [row({ thinkingLevel: 'high', requests: 4 }), row({ thinkingLevel: 'low', requests: 2 })]
    }));
    const thinking = host().querySelector('#tl-thinking-select') as HTMLSelectElement;
    thinking.value = 'separate';
    thinking.dispatchEvent(new Event('change'));
    fixture.detectChanges();

    expect(usersButtons().map(b => b.getAttribute('aria-label')))
      .toEqual(['Usage by user: Claude X · Chat · high', 'Usage by user: Claude X · Chat · low']);

    thinking.value = 'grouped';
    thinking.dispatchEvent(new Event('change'));
    changeKeys(summary({
      keySource: 'both',
      rows: [row({ requests: 3 }), row({ keySource: 'user', userId: 'u-1', userName: 'alice', requests: 2 })]
    }), 'both');

    expect(usersButtons().map(b => b.getAttribute('aria-label')))
      .toEqual(['Usage by user: Claude X · Chat · System key', 'Usage by user: Claude X · Chat · User key: alice']);
  });

  it('asks for the Usage by user dialog with the card\'s scope and the tab\'s dates, the button as the opener', () => {
    render(summary({
      rows: [row({ provider: 'OpenAI', modelId: 'gpt-x', modelName: 'GPT X', roleGroup: 'title', roleContext: 2, thinkingLevel: 'low' })]
    }));

    const button = usersButtons()[0];
    button.click();

    expect(usageRequests).toEqual([{
      request: {
        start: START,
        end: END,
        keySource: 'system',
        provider: 'OpenAI',
        modelId: 'gpt-x',
        modelTitle: 'GPT X',
        roleGroup: 'title',
        thinkingLevel: undefined
      },
      opener: button
    }]);

    const thinking = host().querySelector('#tl-thinking-select') as HTMLSelectElement;
    thinking.value = 'separate';
    thinking.dispatchEvent(new Event('change'));
    fixture.detectChanges();
    usersButtons()[0].click();

    expect(usageRequests[1].request.thinkingLevel).toBe('low');
    expect(usageRequests[1].opener).toBe(usersButtons()[0]);
  });

  it('asks for a user-key card\'s dialog on user keys', () => {
    render(summary({
      keySource: 'user',
      rows: [row({ keySource: 'user', userId: 'u-1', userName: 'alice' })]
    }));

    usersButtons()[0].click();

    expect(usageRequests[0].request).toEqual(expect.objectContaining({ keySource: 'user', modelId: 'claude-x' }));
    expect(usageRequests[0].opener).toBe(usersButtons()[0]);
  });

  it('removes its search timer when destroyed', () => {
    vi.useFakeTimers();
    try {
      render(threeProviders());
      typeSearch('gpt');

      fixture.destroy();
      vi.advanceTimersByTime(200);

      expect(component.search).toBe('');
    }
    finally {
      vi.useRealTimers();
    }
  });
});
