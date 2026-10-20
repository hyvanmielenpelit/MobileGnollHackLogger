import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Subject } from 'rxjs';

import {
  AdminService,
  AiTelemetryModelUserRowDto,
  AiTelemetryModelUsersDto,
  AiTelemetryModelUsersQuery
} from '../../../services/admin.service';
import { TELEMETRY_MODEL_USERS_STORAGE_KEY } from './telemetry-model-users';
import {
  MODEL_USERS_SEARCH_DEBOUNCE_MS,
  TelemetryModelUsersDialogComponent,
  TelemetryModelUsersRequest
} from './telemetry-model-users-dialog.component';

function userRow(overrides: Partial<AiTelemetryModelUserRowDto> = {}): AiTelemetryModelUserRowDto {
  return {
    userId: 'u1', userName: 'alice', roleContext: 1, roleGroup: 'chat', thinkingLevel: 'high',
    requests: 1, promptTokens: 100, uncachedInputTokens: 100, cacheReadTokens: 0, cacheCreationTokens: 0, outputTokens: 10,
    durationSumMs: 100, durationCount: 1, estimatedCost: 0.1, pricedRequests: 1, lastRequestUtc: '2026-10-02T09:30:00Z',
    ...overrides
  };
}

/** alice: high 2 requests $0.40 and low 1 request $0.20; bob: high $0.30; a user without a name: low $0.10. */
const ROWS: AiTelemetryModelUserRowDto[] = [
  userRow({ requests: 2, estimatedCost: 0.4, pricedRequests: 2 }),
  userRow({ thinkingLevel: 'low', estimatedCost: 0.2, lastRequestUtc: '2026-10-03T21:15:00Z' }),
  userRow({ userId: 'u2', userName: 'bob', estimatedCost: 0.3 }),
  userRow({ userId: 'u3', userName: null, thinkingLevel: 'low', estimatedCost: 0.1 })
];

function dto(rows: AiTelemetryModelUserRowDto[] = ROWS, overrides: Partial<AiTelemetryModelUsersDto> = {}): AiTelemetryModelUsersDto {
  return {
    keySource: 'system', provider: 'Anthropic', modelId: 'claude-x', modelName: 'Claude X', roles: ['chat'],
    costRecordedSinceUtc: '2026-09-01T00:00:00Z', rows, ...overrides
  };
}

const REQUEST: TelemetryModelUsersRequest = {
  start: '2026-10-01', end: '2026-10-03', keySource: 'system', provider: 'Anthropic', modelId: 'claude-x',
  modelTitle: 'Claude X', roleGroup: 'chat', thinkingLevel: undefined
};

describe('TelemetryModelUsersDialogComponent', () => {
  let fixture: ComponentFixture<TelemetryModelUsersDialogComponent>;
  let component: TelemetryModelUsersDialogComponent;
  let el: HTMLElement;
  let requests: { query: AiTelemetryModelUsersQuery; subject: Subject<AiTelemetryModelUsersDto> }[];
  let opener: HTMLButtonElement;

  beforeEach(async () => {
    localStorage.removeItem(TELEMETRY_MODEL_USERS_STORAGE_KEY);
    requests = [];
    const admin = {
      getAiTelemetryModelUsers: (query: AiTelemetryModelUsersQuery) => {
        const subject = new Subject<AiTelemetryModelUsersDto>();
        requests.push({ query, subject });
        return subject.asObservable();
      }
    };
    await TestBed.configureTestingModule({
      imports: [TelemetryModelUsersDialogComponent],
      providers: [{ provide: AdminService, useValue: admin }]
    }).compileComponents();
    opener = document.createElement('button');
    opener.textContent = 'Opener';
    document.body.appendChild(opener);
  });

  afterEach(() => {
    vi.useRealTimers();
    fixture?.destroy();
    opener.remove();
    localStorage.removeItem(TELEMETRY_MODEL_USERS_STORAGE_KEY);
  });

  // --- Helpers ---

  function create(): void {
    fixture = TestBed.createComponent(TelemetryModelUsersDialogComponent);
    component = fixture.componentInstance;
    el = fixture.nativeElement as HTMLElement;
    fixture.detectChanges();
  }

  function open(request: TelemetryModelUsersRequest = REQUEST): void {
    component.open(request, opener);
    fixture.detectChanges();
  }

  function respond(index: number, body: AiTelemetryModelUsersDto = dto()): void {
    requests[index].subject.next(body);
    requests[index].subject.complete();
    fixture.detectChanges();
  }

  function q<T extends Element = HTMLElement>(selector: string): T | null {
    return el.querySelector<T>(selector);
  }

  function qa<T extends Element = HTMLElement>(selector: string): T[] {
    return Array.from(el.querySelectorAll<T>(selector));
  }

  function text(element: Element | null): string {
    return (element?.textContent ?? '').replace(/\s+/g, ' ').trim();
  }

  function dialog(): HTMLDialogElement {
    return q<HTMLDialogElement>('dialog.tl-mu-dialog')!;
  }

  /** The user cards, without the total. */
  function cards(): HTMLElement[] {
    return qa<HTMLElement>('.tl-mu-cards > li > article.tl-mu-card:not(.tl-mu-card-total)');
  }

  const titles = () => cards().map(c => text(c.querySelector('h4')));
  const ranks = () => cards().map(c => text(c.querySelector('.tl-mu-rank [aria-hidden="true"]')));
  const shares = () => cards().map(c => text(c.querySelector('.tl-mu-share-value')));
  const status = () => text(q('.tl-mu-status'));

  function setSelect(selector: string, value: string): void {
    const select = q<HTMLSelectElement>(selector)!;
    select.value = value;
    expect(select.value, `${selector} offers ${value}`).toBe(value);
    select.dispatchEvent(new Event('change', { bubbles: true }));
    fixture.detectChanges();
  }

  function typeSearch(value: string): void {
    const input = q<HTMLInputElement>('#tl-mu-search')!;
    input.value = value;
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();
  }

  // --- Opening and fetching ---

  it('opens modal and full-screen, focuses the title and fetches the card\'s scope', () => {
    create();
    expect(dialog().open).toBe(false);
    open({ ...REQUEST, keySource: 'user', roleGroup: 'title' });

    expect(dialog().open).toBe(true);
    expect(dialog().classList).toContain('gh-dialog-fullscreen');
    expect(dialog().getAttribute('aria-labelledby')).toBe('tl-mu-title');
    expect(document.activeElement?.id).toBe('tl-mu-title');
    expect(text(q('#tl-mu-title'))).toBe('Usage by user: Claude X');
    expect(text(q('.tl-mu-subtitle'))).toBe('Title Generation · User keys · 2026-10-01 to 2026-10-03');
    expect(text(q('.tl-mu-identity .gh-model-card-model-id'))).toBe('claude-x');
    expect(q('.tl-mu-identity app-provider-badge')).not.toBeNull();
    expect(q('.tl-mu-loading')).not.toBeNull();
    expect(requests.map(r => r.query)).toEqual([{
      startDate: '2026-10-01', endDate: '2026-10-03', keys: 'user', provider: 'Anthropic', modelId: 'claude-x', roles: ['title']
    }]);
  });

  it('ignores the response for an earlier card once another is opened', () => {
    create();
    open();
    open({ ...REQUEST, modelId: 'gpt-x', modelTitle: 'GPT X' });
    expect(requests.length).toBe(2);

    requests[0].subject.next(dto([userRow({ userName: 'stale' })]));
    fixture.detectChanges();
    expect(cards().length).toBe(0);

    respond(1);
    expect(titles()).toEqual(['alice', 'bob', 'Unknown user']);
  });

  // --- The list ---

  it('lists every user with rank, share and figures, sorted by cost, with the scope totals', () => {
    create();
    open();
    respond(0);

    expect(titles()).toEqual(['alice', 'bob', 'Unknown user']);
    expect(ranks()).toEqual(['#1', '#2', '#3']);
    expect(text(cards()[0].querySelector('.tl-mu-rank .visually-hidden'))).toBe('rank 1 by Est. cost, highest first');
    expect(shares()).toEqual(['60%', '30%', '10%']);
    expect(text(cards()[0].querySelector('.tl-mu-share-label'))).toBe('Share of est. cost');
    expect(cards()[2].querySelector('h4')?.classList).toContain('tl-mu-title-unknown');
    expect(cards()[0].querySelector('h4')?.id).toBe('tl-mu-0-title');
    expect(cards()[0].getAttribute('aria-labelledby')).toBe('tl-mu-0-title');
    expect(text(cards()[0].querySelector('.tl-mu-meta'))).toBe('Last request 2026-10-03 21:15 UTC');
    expect(cards()[0].querySelector('time')?.getAttribute('datetime')).toBe('2026-10-03T21:15:00Z');
    expect(qa('.tl-mu-badges').length).toBe(3);
    expect(Array.from(cards()[0].querySelectorAll('.tl-mu-badges li')).map(text)).toEqual(['thinking level Low', 'thinking level High']);

    const requestsMetric = cards()[0].querySelector('.tl-metric')!;
    expect(text(requestsMetric.querySelector('dd'))).toBe('3');
    expect(text(requestsMetric.querySelector('.tl-metric-note'))).toBe('60% of total');
    expect(cards()[0].querySelectorAll('.tl-metric').length).toBe(9);

    const total = q('.tl-mu-card-total')!;
    expect(text(total.querySelector('h4'))).toBe('All matching');
    expect(text(total.querySelector('.tl-mu-meta'))).toBe('3 users');
    expect(total.querySelectorAll('.tl-metric-note').length).toBe(0);

    const summary = qa('.tl-mu-summary > div').map(item => [text(item.querySelector('dt')), text(item.querySelector('dd'))]);
    expect(summary).toEqual([
      ['Users', '3'], ['Requests', '5'], ['Est. cost', '$1.00'], ['Total tokens', '440'], ['Top user', 'alice · 60% of est. cost']
    ]);
    expect(text(q('.tl-mu-scope-errors'))).toBe('Provider errors are not recorded per user for system AI configurations.');
    expect(status()).toBe('Showing 3 users · 5 requests');
    expect(q('.tl-mu-status')?.getAttribute('role')).toBe('status');
  });

  it('leaves the provider-error note out for user keys', () => {
    create();
    open({ ...REQUEST, keySource: 'user' });
    respond(0, dto(ROWS, { keySource: 'user' }));

    expect(q('.tl-mu-scope-errors')).toBeNull();
  });

  it('sorts by the Sort popover both ways, and labels the trigger', () => {
    create();
    open();
    respond(0);
    expect(text(q('#tl-mu-sort-trigger'))).toBe('Sort: Est. cost, highest first');
    expect(q('#tl-mu-sort-reset')?.getAttribute('aria-disabled')).toBe('true');

    q<HTMLInputElement>('#tl-mu-sort-field-name')!.click();
    fixture.detectChanges();
    expect(text(q('#tl-mu-sort-trigger'))).toBe('Sort: User name, Z–A');
    expect(titles()).toEqual(['bob', 'alice', 'Unknown user']);
    expect(ranks()).toEqual(['#1', '#2', '#3']);
    // The share follows requests when the sort field is not additive.
    expect(text(cards()[0].querySelector('.tl-mu-share-label'))).toBe('Share of requests');

    q<HTMLInputElement>('#tl-mu-sort-direction-asc')!.click();
    fixture.detectChanges();
    expect(text(q('#tl-mu-sort-trigger'))).toBe('Sort: User name, A–Z');
    expect(titles()).toEqual(['alice', 'bob', 'Unknown user']);
    expect(JSON.parse(localStorage.getItem(TELEMETRY_MODEL_USERS_STORAGE_KEY)!)).toEqual({
      version: 1, separateThinkingLevels: false, sort: { field: 'name', direction: 'asc' }
    });

    q<HTMLButtonElement>('#tl-mu-sort-reset')!.click();
    fixture.detectChanges();
    expect(text(q('#tl-mu-sort-trigger'))).toBe('Sort: Est. cost, highest first');
    expect(titles()).toEqual(['alice', 'bob', 'Unknown user']);
  });

  it('splits a user into one card per level with Show separately', () => {
    create();
    open();
    respond(0);

    setSelect('#tl-mu-thinking', 'separate');

    expect(titles()).toEqual(['alice', 'bob', 'alice', 'Unknown user']);
    expect(cards().map(c => text(c.querySelector('.tl-mu-badges')))).toEqual([
      'thinking level High', 'thinking level High', 'thinking level Low', 'thinking level Low'
    ]);
    expect(readStored().separateThinkingLevels).toBe(true);
  });

  it('presets the Thinking level filter to the card\'s level, so the totals are that level\'s', () => {
    create();
    open({ ...REQUEST, thinkingLevel: 'high' });
    respond(0);

    expect(q<HTMLSelectElement>('#tl-mu-level')!.value).toBe('=high');
    expect(titles()).toEqual(['alice', 'bob']);
    expect(text(q('.tl-mu-summary > div:nth-child(2) dd'))).toBe('3');
    expect(q('#tl-mu-clear')).not.toBeNull();

    setSelect('#tl-mu-level', '*');
    expect(titles()).toEqual(['alice', 'bob', 'Unknown user']);
    expect(q('#tl-mu-clear')).toBeNull();
  });

  it('searches after a short quiet, marks the match and keeps the ranks and shares of the scope', () => {
    create();
    open();
    respond(0);
    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] });

    typeSearch('bo');
    expect(titles()).toEqual(['alice', 'bob', 'Unknown user']);
    vi.advanceTimersByTime(MODEL_USERS_SEARCH_DEBOUNCE_MS);
    fixture.detectChanges();

    expect(titles()).toEqual(['bob']);
    expect(text(cards()[0].querySelector('mark.model-option-match'))).toBe('bo');
    expect(ranks()).toEqual(['#2']);
    expect(shares()).toEqual(['30%']);
    expect(status()).toBe('Showing 1 of 3 users');
    expect(text(q('.tl-mu-card-total .tl-mu-meta'))).toBe('1 user');
    expect(text(q('.tl-mu-summary > div:nth-child(1) dd'))).toBe('3');
  });

  it('clears the search on Escape without closing the dialog', () => {
    create();
    open();
    respond(0);
    typeSearch('zzz');

    const input = q<HTMLInputElement>('#tl-mu-search')!;
    const escape = new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true });
    input.dispatchEvent(escape);
    fixture.detectChanges();

    expect(escape.defaultPrevented).toBe(true);
    expect(input.value).toBe('');
    expect(dialog().open).toBe(true);
  });

  it('says when nothing matches and clears the filters from there', () => {
    create();
    open();
    respond(0);
    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] });
    typeSearch('zzz');
    vi.advanceTimersByTime(MODEL_USERS_SEARCH_DEBOUNCE_MS);
    fixture.detectChanges();

    expect(cards().length).toBe(0);
    expect(text(q('.tl-mu-no-match .tl-mu-empty'))).toBe('No users match "zzz".');
    q<HTMLButtonElement>('.tl-mu-no-match-clear')!.click();
    fixture.detectChanges();

    expect(titles()).toEqual(['alice', 'bob', 'Unknown user']);
    expect(document.activeElement?.id).toBe('tl-mu-search');
  });

  it('shows 50 users, then the next batch, focusing its first title', () => {
    const many = Array.from({ length: 120 }, (_, i) =>
      userRow({ userId: `u${i}`, userName: `user ${String(i).padStart(3, '0')}`, estimatedCost: 1000 - i }));
    create();
    open();
    respond(0, dto(many));

    expect(cards().length).toBe(50);
    expect(status()).toBe('Showing 50 of 120 users · 120 requests');
    expect(text(q('.tl-mu-more'))).toBe('Show 50 more');
    expect(text(q('.tl-mu-all'))).toBe('Show all 120');
    expect(text(q('.tl-mu-card-total .tl-mu-meta'))).toBe('120 users');

    q<HTMLButtonElement>('.tl-mu-more')!.click();
    fixture.detectChanges();
    expect(cards().length).toBe(100);
    expect(document.activeElement?.id).toBe('tl-mu-50-title');
    expect(text(q('.tl-mu-more'))).toBe('Show 20 more');
    expect(q('.tl-mu-all')).toBeNull();

    q<HTMLButtonElement>('.tl-mu-more')!.click();
    fixture.detectChanges();
    expect(cards().length).toBe(120);
    expect(q('.tl-mu-load-more')).toBeNull();
  });

  // --- States ---

  it('says so when no user used the model in the range', () => {
    create();
    open();
    respond(0, dto([]));

    expect(text(q('.tl-mu-empty'))).toBe('No user used this model in this range.');
    expect(q('.tl-mu-summary')).toBeNull();
  });

  it('shows a failure with Retry, which fetches again', () => {
    create();
    open();
    requests[0].subject.error({ error: { message: 'Boom.' } });
    fixture.detectChanges();

    expect(text(q('.tl-mu-error-text'))).toBe('Boom.');
    q<HTMLButtonElement>('.tl-mu-retry')!.click();
    fixture.detectChanges();
    expect(requests.length).toBe(2);
    respond(1);
    expect(titles()).toEqual(['alice', 'bob', 'Unknown user']);
  });

  // --- Closing ---

  it('closes on Done and returns focus to the button that opened it', async () => {
    create();
    open();
    respond(0);

    q<HTMLButtonElement>('.tl-mu-done')!.click();
    fixture.detectChanges();

    expect(dialog().open).toBe(false);
    await vi.waitFor(() => expect(document.activeElement).toBe(opener));
  });

  it('closes on Escape and returns focus to the opener', async () => {
    create();
    open();
    respond(0);

    const cancel = new Event('cancel', { cancelable: true });
    dialog().dispatchEvent(cancel);
    fixture.detectChanges();

    expect(cancel.defaultPrevented).toBe(true);
    expect(dialog().open).toBe(false);
    await vi.waitFor(() => expect(document.activeElement).toBe(opener));
  });

  function readStored(): { separateThinkingLevels: boolean } {
    return JSON.parse(localStorage.getItem(TELEMETRY_MODEL_USERS_STORAGE_KEY) ?? '{}');
  }
});
