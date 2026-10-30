import type { Mock } from 'vitest';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import {
  AdminService,
  AiTelemetryKeySource,
  ChatTurnLimitEventDetail,
  ChatTurnLimitEventSummary,
  ChatTurnLimitEventsPage,
  ChatTurnLimitEventsQuery,
  ChatTurnLimitSummary
} from '../../../services/admin.service';
import { AdminPageStore } from '../../admin-page.store';
import { configureAdminTestBed } from '../../admin.component.testing';
import { TELEMETRY_LIMITS_BATCH, TELEMETRY_LIMITS_VIEW_STORAGE_KEY, TelemetryLimitsComponent } from './telemetry-limits.component';

const settle = (ms = 0): Promise<void> => new Promise<void>(resolve => setTimeout(resolve, ms));

const textOf = (el: Element | null | undefined): string => (el?.textContent ?? '').replace(/\s+/g, ' ').trim();

function clearStorage(): void {
  try {
    localStorage.removeItem(TELEMETRY_LIMITS_VIEW_STORAGE_KEY);
  } catch {
    // Nothing stored.
  }
}

function limitEvent(id: number, overrides: Partial<ChatTurnLimitEventSummary> = {}): ChatTurnLimitEventSummary {
  return {
    id, occurredUtc: '2026-10-30T12:00:00Z', code: 'research_rounds', severity: 'warning', userId: 'u1', userName: 'Alice',
    wasConfidential: false, wasEphemeral: false, keySource: 'system', provider: 'Anthropic', requestedModelId: 'claude-x',
    modelDisplayName: 'Claude X', limitValue: 22, observedValue: 22, totalDurationMs: 4200, toolCallCount: 31,
    ...overrides
  };
}

function eventDetail(event: ChatTurnLimitEventSummary): ChatTurnLimitEventDetail {
  return {
    ...event,
    thinkingLevel: null, iterationsUsed: 22, modelCallCount: 23, toolCallsBlocked: 0, toolTimeMs: 0, permitWaitMs: 0,
    backoffWaitMs: 0, retryAttemptCount: 0, totalPromptTokens: 0, outputTokens: 0, estimatedCost: null,
    providerFinishReason: null, providerErrorCode: null, httpStatus: null, assistantPhase: null, chatSessionId: 5,
    promptState: 'available'
  };
}

function fullSummary(): ChatTurnLimitSummary {
  return {
    totalHits: 1234, turnsWithoutAnswer: 9, usersAffected: 5,
    byCode: [
      { code: 'research_rounds', hits: 1000, users: 4, lastSeenUtc: '2026-10-30T12:00:00Z' },
      { code: 'tool_output', hits: 234, users: 2, lastSeenUtc: '2026-10-29T08:30:00Z' }
    ],
    bySeverity: [{ severity: 'warning', hits: 1225 }, { severity: 'error', hits: 9 }]
  };
}

function page(items: ChatTurnLimitEventSummary[], totalCount = items.length): ChatTurnLimitEventsPage {
  return { items, totalCount };
}

describe('TelemetryLimitsComponent', () => {
  let fixture: ComponentFixture<TelemetryLimitsComponent>;
  let component: TelemetryLimitsComponent;
  let el: HTMLElement;
  let adminService: AdminService;
  let store: AdminPageStore;
  let summarySpy: Mock;
  let eventsSpy: Mock;

  beforeEach(async () => {
    clearStorage();
    ({ adminService, store } = await configureAdminTestBed(TelemetryLimitsComponent));
    summarySpy = vi.spyOn(adminService, 'getChatTurnLimitSummary').mockReturnValue(of(fullSummary()));
    eventsSpy = vi.spyOn(adminService, 'getChatTurnLimitEvents').mockReturnValue(of(page([limitEvent(1)])));
  });

  afterEach(() => {
    if (fixture) {
      el.querySelectorAll('dialog').forEach(dialog => {
        if (dialog.open) dialog.close();
      });
      el.querySelectorAll<HTMLElement>('[popover]').forEach(popover => {
        try { popover.hidePopover(); } catch { /* already closed */ }
      });
      fixture.destroy();
    }
    clearStorage();
  });

  function create(keys?: AiTelemetryKeySource): void {
    fixture = TestBed.createComponent(TelemetryLimitsComponent);
    component = fixture.componentInstance;
    el = fixture.nativeElement as HTMLElement;
    fixture.componentRef.setInput('startDate', '2026-09-30');
    fixture.componentRef.setInput('endDate', '2026-10-30');
    if (keys !== undefined) fixture.componentRef.setInput('keys', keys);
    fixture.detectChanges();
  }

  const lastQuery = (): ChatTurnLimitEventsQuery => eventsSpy.mock.lastCall![0] as ChatTurnLimitEventsQuery;
  const card = (id: number): HTMLElement => el.querySelector<HTMLElement>(`article.tl-lim-card[data-event-id="${id}"]`)!;
  const cards = (): HTMLElement[] => Array.from(el.querySelectorAll<HTMLElement>('article.tl-lim-card'));
  const factsOf = (cardEl: HTMLElement): Record<string, string> => Object.fromEntries(
    Array.from(cardEl.querySelectorAll('.tl-lim-facts > div')).map(row => [textOf(row.querySelector('dt')), textOf(row.querySelector('dd'))])
  );
  const chips = (): string[] => Array.from(el.querySelectorAll('.tl-lim-chips .gh-filter-chip')).map(chip => chip.getAttribute('aria-label') ?? '');
  const status = (): string => textOf(el.querySelector('#tl-lim-status'));
  const search = (): HTMLInputElement => el.querySelector<HTMLInputElement>('#tl-lim-search')!;

  /** Selects a facet's option by its label, with the real checkbox. */
  function chooseOption(facetId: string, label: string): void {
    const option = Array.from(el.querySelectorAll<HTMLElement>(`#${facetId}-popover .gh-facet-option`))
      .find(o => textOf(o.querySelector('.gh-facet-option-label')) === label)!;
    option.querySelector<HTMLInputElement>('input')!.click();
    fixture.detectChanges();
  }

  describe('summary', () => {
    it('loads for the tab\'s range and keys, and shows the three totals', () => {
      create('user');

      expect(summarySpy).toHaveBeenCalledWith('2026-09-30', '2026-10-30', 'user');
      const stats = Array.from(el.querySelectorAll('ul.tl-lim-summary > li.tl-lim-stat'));
      expect(stats.map(stat => textOf(stat.querySelector('h4')))).toEqual(['Limit hits', 'Turns without an answer', 'Users affected']);
      expect(stats.map(stat => textOf(stat.querySelector('.tl-lim-stat-value')))).toEqual(['1,234', '9', '5']);
    });

    it('has a real By limit table, with the limit name a button that sets the Reason filter', () => {
      create();

      const table = el.querySelector('.tl-lim-by-code table')!;
      expect(textOf(table.querySelector('caption'))).toBe('By limit');
      expect(Array.from(table.querySelectorAll('thead th')).map(th => textOf(th))).toEqual(['Limit', 'Hits', 'Users', 'Last seen']);
      expect(Array.from(table.querySelectorAll('th')).every(th => th.hasAttribute('scope'))).toBe(true);
      const first = table.querySelector('tbody tr')!;
      expect(Array.from(first.children).map(cell => textOf(cell))).toEqual(['Research rounds', '1,000', '4', '2026-10-30 12:00 UTC']);

      const button = el.querySelector<HTMLButtonElement>('.tl-lim-code-select[data-code="tool_output"]')!;
      expect(textOf(button)).toBe('Tool output cut');
      button.click();
      fixture.detectChanges();

      expect(lastQuery().codes).toEqual(['tool_output']);
      expect(chips()).toEqual(['Remove filter Reason: Tool output cut']);
    });

    it('shows an alert when the summary fails, and still shows the list', () => {
      summarySpy.mockReturnValue(throwError(() => ({ message: 'boom' })));
      create();

      expect(textOf(el.querySelector('.tl-lim-summary-error'))).toBe('The limit summary could not be loaded: boom');
      expect(el.querySelector('.tl-lim-summary-error')!.getAttribute('role')).toBe('alert');
      expect(cards().length).toBe(1);
    });
  });

  describe('cards', () => {
    it('asks for the first page, and renders a labeled article with kicker, title and facts', () => {
      create();

      expect(lastQuery()).toEqual({
        startDate: '2026-09-30', endDate: '2026-10-30', keys: 'system', codes: undefined, severities: undefined,
        search: undefined, skip: 0, take: TELEMETRY_LIMITS_BATCH
      });
      expect(el.querySelector('ul.tl-lim-cards')!.getAttribute('role')).toBe('list');
      const article = card(1);
      expect(article.getAttribute('aria-labelledby')).toBe('tl-lim-1-title');
      expect(textOf(article.querySelector('.tl-lim-reason'))).toBe('Research rounds');
      expect(article.querySelector('.tl-lim-kicker time')!.getAttribute('datetime')).toBe('2026-10-30T12:00:00Z');
      expect(article.querySelector('.gh-tag')).toBeNull();
      expect(textOf(article.querySelector('h5'))).toBe('Alice');
      expect(factsOf(article)).toEqual({
        'Model': 'Anthropic · Claude X',
        'Stopped at': '22 of 22 rounds',
        'Duration': '4.2 s',
        'Tools': '31 including sub-agents',
        'Reference': 'L-1'
      });
      expect(status()).toBe('One hit');
    });

    it('tags a confidential and an incognito turn in words', () => {
      eventsSpy.mockReturnValue(of(page([limitEvent(1, { wasConfidential: true }), limitEvent(2, { wasEphemeral: true })])));
      create();

      expect(textOf(card(1).querySelector('.gh-tag'))).toBe('Confidential');
      expect(textOf(card(2).querySelector('.gh-tag'))).toBe('Incognito');
    });

    it('shows a dash for a limit that records no numbers', () => {
      eventsSpy.mockReturnValue(of(page([limitEvent(1, { code: 'provider_error', limitValue: null, observedValue: null })])));
      create();

      expect(factsOf(card(1))['Stopped at']).toBe('—');
    });

    it('opens the details, with the button as the opener, and asks for the event', () => {
      const detailSpy = vi.spyOn(adminService, 'getChatTurnLimitEvent').mockReturnValue(of(eventDetail(limitEvent(1))));
      create();

      const button = card(1).querySelector<HTMLButtonElement>('button.tl-lim-details')!;
      expect(button.getAttribute('aria-label')).toBe('Details of Research rounds for Alice, L-1');
      expect(button.getAttribute('aria-haspopup')).toBe('dialog');
      const tip = el.querySelector('#' + button.getAttribute('interestfor'));
      expect(tip?.getAttribute('popover')).toBe('hint');
      expect(textOf(tip)).toBe('Details');

      button.click();
      fixture.detectChanges();

      expect(detailSpy).toHaveBeenCalledWith(1);
      expect(el.querySelector<HTMLDialogElement>('dialog.tl-lim-dialog')!.open).toBe(true);
    });

    it('opens the user\'s AI usage, and offers it only for an event that names a user', () => {
      eventsSpy.mockReturnValue(of(page([limitEvent(1), limitEvent(2, { userId: null, userName: null })])));
      const request = vi.spyOn(store, 'requestTelemetryUser').mockReturnValue(undefined);
      create();

      const usage = card(1).querySelector<HTMLButtonElement>('button.tl-lim-usage')!;
      expect(usage.getAttribute('aria-label')).toBe('Show the AI usage of Alice');
      usage.click();
      expect(request).toHaveBeenCalledWith('u1', 'Alice');

      expect(card(2).querySelector('button.tl-lim-usage')).toBeNull();
      expect(textOf(card(2).querySelector('h5'))).toBe('Unknown user');
    });

    it('fetches the next page on Show more, appends it and focuses the first new card', () => {
      const firstPage = Array.from({ length: TELEMETRY_LIMITS_BATCH }, (_, i) => limitEvent(100 - i));
      eventsSpy.mockReturnValue(of(page(firstPage, TELEMETRY_LIMITS_BATCH + 3)));
      create();
      expect(status()).toBe(`Showing ${TELEMETRY_LIMITS_BATCH} of ${TELEMETRY_LIMITS_BATCH + 3} hits`);

      const more = el.querySelector<HTMLButtonElement>('.gh-load-more .tl-lim-show-more')!;
      expect(textOf(more)).toBe('Show 3 more');
      eventsSpy.mockReturnValue(of(page([limitEvent(50), limitEvent(49), limitEvent(48)], TELEMETRY_LIMITS_BATCH + 3)));
      more.click();
      fixture.detectChanges();

      expect(lastQuery().skip).toBe(TELEMETRY_LIMITS_BATCH);
      expect(cards().length).toBe(TELEMETRY_LIMITS_BATCH + 3);
      expect(document.activeElement).toBe(el.querySelector('#tl-lim-50-title'));
      expect(el.querySelector('.tl-lim-show-more')).toBeNull();
    });
  });

  describe('filters', () => {
    it('has one search bar with a labeled field, and Reason and Severity facets with the summary\'s counts', () => {
      create();

      expect(el.querySelectorAll('search.gh-filter-bar').length).toBe(1);
      expect(textOf(el.querySelector('label[for="tl-lim-search"]'))).toBe('Search limit hits by user name');
      const reasons = Array.from(el.querySelectorAll('#tl-lim-facet-code-popover .gh-facet-option')).map(o => textOf(o));
      expect(reasons[0]).toContain('Research rounds');
      expect(reasons[0]).toContain('1000');
      expect(reasons[1]).toContain('Tool output cut');
      const severities = Array.from(el.querySelectorAll('#tl-lim-facet-severity-popover .gh-facet-option .gh-facet-option-label')).map(o => textOf(o));
      expect(severities).toEqual(['Error', 'Warning']);
    });

    it('lists no facet while the range holds one value of it', () => {
      summarySpy.mockReturnValue(of({ ...fullSummary(), byCode: [fullSummary().byCode[0]], bySeverity: [{ severity: 'warning', hits: 1 }] }));
      create();

      expect(el.querySelector('#tl-lim-facet-code-trigger')).toBeNull();
      expect(el.querySelector('#tl-lim-facet-severity-trigger')).toBeNull();
    });

    it('refetches page 1 with a facet\'s selection, comma-joined by the service, and shows a chip', () => {
      create();

      chooseOption('tl-lim-facet-severity', 'Error');
      expect(lastQuery().severities).toEqual(['error']);
      expect(lastQuery().skip).toBe(0);
      expect(chips()).toEqual(['Remove filter Severity: Error']);

      chooseOption('tl-lim-facet-code', 'Tool output cut');
      expect(lastQuery().codes).toEqual(['tool_output']);
      expect(chips()).toEqual(['Remove filter Reason: Tool output cut', 'Remove filter Severity: Error']);
    });

    it('removes a chip, refetches and focuses the chip now in its place, else the search', () => {
      create();
      chooseOption('tl-lim-facet-severity', 'Error');
      chooseOption('tl-lim-facet-code', 'Tool output cut');

      el.querySelector<HTMLButtonElement>('.tl-lim-chips .gh-filter-chip')!.click();
      fixture.detectChanges();
      expect(lastQuery().codes).toBeUndefined();
      expect(chips()).toEqual(['Remove filter Severity: Error']);
      expect(document.activeElement).toBe(el.querySelector('.tl-lim-chips .gh-filter-chip'));

      el.querySelector<HTMLButtonElement>('.tl-lim-chips .gh-filter-chip')!.click();
      fixture.detectChanges();
      expect(lastQuery().severities).toBeUndefined();
      expect(document.activeElement).toBe(search());
    });

    it('applies the search after a pause, and clears everything with Clear all', async () => {
      create();

      search().value = 'alice';
      search().dispatchEvent(new Event('input', { bubbles: true }));
      await settle(250);
      fixture.detectChanges();
      expect(lastQuery().search).toBe('alice');
      expect(chips()).toEqual(['Remove filter Search: “alice”']);

      el.querySelector<HTMLButtonElement>('.tl-lim-clear-filters')!.click();
      fixture.detectChanges();
      expect(lastQuery().search).toBeUndefined();
      expect(chips()).toEqual([]);
      expect(search().value).toBe('');
      expect(document.activeElement).toBe(search());
    });

    it('clears the search at once on Escape, and leaves Escape alone with no text', () => {
      create();
      search().value = 'alice';
      search().dispatchEvent(new Event('input', { bubbles: true }));

      const first = new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true });
      search().dispatchEvent(first);
      expect(first.defaultPrevented).toBe(true);
      expect(search().value).toBe('');

      const second = new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true });
      search().dispatchEvent(second);
      expect(second.defaultPrevented).toBe(false);
    });
  });

  describe('states', () => {
    it('says so when the range has no hits', () => {
      eventsSpy.mockReturnValue(of(page([])));
      create();

      expect(textOf(el.querySelector('.tl-lim-empty'))).toBe('No limit hits in this range.');
      expect(status()).toBe('');
    });

    it('offers to clear the filters when they leave no hits', () => {
      create();
      eventsSpy.mockReturnValue(of(page([])));
      chooseOption('tl-lim-facet-severity', 'Error');

      expect(textOf(el.querySelector('.tl-lim-empty'))).toContain('No limit hits match these filters.');
      expect(el.querySelector('.tl-lim-empty .gh-filter-clear')).not.toBeNull();
    });

    it('shows an alert when the list fails', () => {
      eventsSpy.mockReturnValue(throwError(() => ({ error: { message: 'nope' } })));
      create();

      expect(textOf(el.querySelector('.tl-lim-events-error'))).toBe('The limit hits could not be loaded: nope');
      expect(cards().length).toBe(0);
    });
  });

  it('refetches when the range or the keys change, keeping the filters', () => {
    create();
    chooseOption('tl-lim-facet-severity', 'Error');
    summarySpy.mockClear();
    eventsSpy.mockClear();

    fixture.componentRef.setInput('keys', 'both');
    fixture.detectChanges();

    expect(summarySpy).toHaveBeenCalledWith('2026-09-30', '2026-10-30', 'both');
    expect(lastQuery().keys).toBe('both');
    expect(lastQuery().severities).toEqual(['error']);
  });

  it('reloads the summary and the list from reload()', () => {
    create();
    summarySpy.mockClear();
    eventsSpy.mockClear();

    component.reload();

    expect(summarySpy).toHaveBeenCalledTimes(1);
    expect(eventsSpy).toHaveBeenCalledTimes(1);
  });
});
