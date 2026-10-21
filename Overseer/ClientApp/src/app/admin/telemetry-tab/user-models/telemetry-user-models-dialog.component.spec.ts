import { ChangeDetectionStrategy, Component, ViewChild } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Subject } from 'rxjs';

import {
  AdminService,
  AiTelemetryModelUsersDto,
  AiTelemetryModelUsersQuery,
  AiTelemetryUserModelRowDto,
  AiTelemetryUserModelsDto,
  AiTelemetryUserModelsQuery
} from '../../../services/admin.service';
import { TelemetryModelUsersDialogComponent } from '../model-users/telemetry-model-users-dialog.component';
import { TELEMETRY_MODEL_USERS_STORAGE_KEY } from '../model-users/telemetry-model-users';
import { TELEMETRY_USER_MODELS_STORAGE_KEY } from './telemetry-user-models';
import {
  TelemetryUserModelsDialogComponent,
  TelemetryUserModelsRequest,
  USER_MODELS_SEARCH_DEBOUNCE_MS
} from './telemetry-user-models-dialog.component';

function modelRow(overrides: Partial<AiTelemetryUserModelRowDto> = {}): AiTelemetryUserModelRowDto {
  return {
    keySource: 'system', provider: 'Anthropic', modelId: 'claude-x', modelName: 'Claude X', roleContext: 1, roleGroup: 'chat',
    thinkingLevel: 'high', requests: 1, promptTokens: 100, uncachedInputTokens: 100, cacheReadTokens: 0, cacheCreationTokens: 0,
    outputTokens: 10, durationSumMs: 100, durationCount: 1, estimatedCost: 0.1, pricedRequests: 1, lastRequestUtc: '2026-10-02T09:30:00Z',
    ...overrides
  };
}

/** Claude X in Chat: high 2 requests $0.40 and low 1 request $0.20; gpt-x (no name) in Benchmark: $0.30; Gemini Y in Chat: low $0.10. */
const ROWS: AiTelemetryUserModelRowDto[] = [
  modelRow({ requests: 2, estimatedCost: 0.4, pricedRequests: 2 }),
  modelRow({ thinkingLevel: 'low', estimatedCost: 0.2, lastRequestUtc: '2026-10-03T21:15:00Z' }),
  modelRow({ provider: 'OpenAI', modelId: 'gpt-x', modelName: null, roleGroup: 'benchmark', estimatedCost: 0.3 }),
  modelRow({ provider: 'Google', modelId: 'gemini-y', modelName: 'Gemini Y', thinkingLevel: 'low', estimatedCost: 0.1 })
];

function dto(rows: AiTelemetryUserModelRowDto[] = ROWS, overrides: Partial<AiTelemetryUserModelsDto> = {}): AiTelemetryUserModelsDto {
  return { keySource: 'system', userId: 'u1', userName: 'alice', costRecordedSinceUtc: '2026-09-01T00:00:00Z', rows, ...overrides };
}

const REQUEST: TelemetryUserModelsRequest = { start: '2026-10-01', end: '2026-10-03', keys: 'system', userId: 'u1', userName: 'alice' };

/** Both usage dialogs side by side, as the AI Telemetry tab renders them. */
@Component({
  standalone: true,
  imports: [TelemetryModelUsersDialogComponent, TelemetryUserModelsDialogComponent],
  changeDetection: ChangeDetectionStrategy.Eager,
  template: `<app-telemetry-model-users-dialog #modelUsers /><app-telemetry-user-models-dialog #userModels />`
})
class BothDialogsHostComponent {
  @ViewChild('modelUsers') modelUsers!: TelemetryModelUsersDialogComponent;
  @ViewChild('userModels') userModels!: TelemetryUserModelsDialogComponent;
}

describe('TelemetryUserModelsDialogComponent', () => {
  let fixture: ComponentFixture<TelemetryUserModelsDialogComponent>;
  let component: TelemetryUserModelsDialogComponent;
  let el: HTMLElement;
  let requests: { query: AiTelemetryUserModelsQuery; subject: Subject<AiTelemetryUserModelsDto> }[];
  let modelUserRequests: { query: AiTelemetryModelUsersQuery; subject: Subject<AiTelemetryModelUsersDto> }[];
  let opener: HTMLButtonElement;

  beforeEach(async () => {
    localStorage.removeItem(TELEMETRY_USER_MODELS_STORAGE_KEY);
    localStorage.removeItem(TELEMETRY_MODEL_USERS_STORAGE_KEY);
    requests = [];
    modelUserRequests = [];
    const admin = {
      getAiTelemetryUserModels: (query: AiTelemetryUserModelsQuery) => {
        const subject = new Subject<AiTelemetryUserModelsDto>();
        requests.push({ query, subject });
        return subject.asObservable();
      },
      getAiTelemetryModelUsers: (query: AiTelemetryModelUsersQuery) => {
        const subject = new Subject<AiTelemetryModelUsersDto>();
        modelUserRequests.push({ query, subject });
        return subject.asObservable();
      }
    };
    await TestBed.configureTestingModule({
      imports: [TelemetryUserModelsDialogComponent, BothDialogsHostComponent],
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
    localStorage.removeItem(TELEMETRY_USER_MODELS_STORAGE_KEY);
    localStorage.removeItem(TELEMETRY_MODEL_USERS_STORAGE_KEY);
  });

  // --- Helpers ---

  function create(): void {
    fixture = TestBed.createComponent(TelemetryUserModelsDialogComponent);
    component = fixture.componentInstance;
    el = fixture.nativeElement as HTMLElement;
    fixture.detectChanges();
  }

  function open(request: TelemetryUserModelsRequest = REQUEST): void {
    component.open(request, opener);
    fixture.detectChanges();
  }

  function respond(index: number, body: AiTelemetryUserModelsDto = dto()): void {
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
    return q<HTMLDialogElement>('dialog.tl-um-dialog')!;
  }

  /** The model cards, without the total. */
  function cards(): HTMLElement[] {
    return qa<HTMLElement>('.tl-mu-cards > li > article.tl-mu-card:not(.tl-mu-card-total)');
  }

  const titles = () => cards().map(c => text(c.querySelector('h4')));
  const ranks = () => cards().map(c => text(c.querySelector('.tl-mu-rank [aria-hidden="true"]')));
  const shares = () => cards().map(c => text(c.querySelector('.tl-mu-share-value')));
  const status = () => text(q('.tl-mu-status'));
  const summary = () => qa('.tl-mu-summary > div').map(item => [text(item.querySelector('dt')), text(item.querySelector('dd'))]);

  function setSelect(selector: string, value: string): void {
    const select = q<HTMLSelectElement>(selector)!;
    select.value = value;
    expect(select.value, `${selector} offers ${value}`).toBe(value);
    select.dispatchEvent(new Event('change', { bubbles: true }));
    fixture.detectChanges();
  }

  function typeSearch(value: string): void {
    const input = q<HTMLInputElement>('#tl-um-search')!;
    input.value = value;
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();
  }

  function readStored(): { separateThinkingLevels: boolean } {
    return JSON.parse(localStorage.getItem(TELEMETRY_USER_MODELS_STORAGE_KEY) ?? '{}');
  }

  // --- Opening and fetching ---

  it('opens modal and full-screen, focuses the title and fetches the user\'s scope', () => {
    create();
    expect(dialog().open).toBe(false);
    open({ ...REQUEST, keys: 'both' });

    expect(dialog().open).toBe(true);
    expect(dialog().classList).toContain('gh-dialog-fullscreen');
    expect(dialog().getAttribute('aria-labelledby')).toBe('tl-um-title');
    expect(document.activeElement?.id).toBe('tl-um-title');
    expect(text(q('#tl-um-title'))).toBe('Usage by model: alice');
    expect(text(q('.tl-mu-subtitle'))).toBe('All keys · 2026-10-01 to 2026-10-03');
    expect(q('.tl-mu-close')?.getAttribute('aria-label')).toBe('Close usage by model');
    expect(text(q('#tl-um-tip-close'))).toBe('Close');
    expect(q('.tl-mu-loading')).not.toBeNull();
    expect(requests.map(r => r.query)).toEqual([{ startDate: '2026-10-01', endDate: '2026-10-03', keys: 'both', userId: 'u1' }]);
  });

  it('titles a user whose account is gone as Unknown user', () => {
    create();
    open({ ...REQUEST, userName: '' });
    respond(0, dto(ROWS, { userName: null }));

    expect(text(q('#tl-um-title'))).toBe('Usage by model: Unknown user');
  });

  it('ignores the response for an earlier user once another is opened', () => {
    create();
    open();
    open({ ...REQUEST, userId: 'u2', userName: 'bob' });
    expect(requests.length).toBe(2);

    requests[0].subject.next(dto([modelRow({ modelName: 'Stale' })]));
    fixture.detectChanges();
    expect(cards().length).toBe(0);

    respond(1);
    expect(titles()).toEqual(['Claude X', 'gpt-x', 'Gemini Y']);
  });

  // --- The list ---

  it('lists every model with rank, identity, badges, share and figures, sorted by cost, with the scope totals', () => {
    create();
    open();
    respond(0);

    expect(titles()).toEqual(['Claude X', 'gpt-x', 'Gemini Y']);
    expect(ranks()).toEqual(['#1', '#2', '#3']);
    expect(text(cards()[0].querySelector('.tl-mu-rank .visually-hidden'))).toBe('rank 1 by Est. cost, highest first');
    expect(shares()).toEqual(['60%', '30%', '10%']);
    expect(text(cards()[0].querySelector('.tl-mu-share-label'))).toBe('Share of est. cost');
    expect(cards()[0].querySelector('h4')?.id).toBe('tl-um-0-title');
    expect(cards()[0].getAttribute('aria-labelledby')).toBe('tl-um-0-title');
    expect(q('.tl-mu-cards')?.getAttribute('role')).toBe('list');
    expect(q('.tl-mu-cards')?.getAttribute('aria-labelledby')).toBe('tl-um-title');

    expect(text(cards()[0].querySelector('.tl-um-identity .gh-model-card-model-id'))).toBe('claude-x');
    expect(cards()[0].querySelector('.tl-um-identity app-provider-badge')).not.toBeNull();
    expect(cards()[1].querySelector('.tl-um-identity .gh-model-card-model-id')).toBeNull();

    const badges = (card: HTMLElement) => Array.from(card.querySelectorAll('.tl-um-badges > li')).map(text);
    expect(badges(cards()[0])).toEqual(['role Chat', 'thinking level Low', 'thinking level High']);
    expect(badges(cards()[1])).toEqual(['role Benchmark', 'thinking level High']);
    const roleBadge = cards()[0].querySelector('.tl-um-badges .status-badge')!;
    expect(roleBadge.classList).toContain('badge-role-chat');
    expect(getComputedStyle(roleBadge).whiteSpace).toBe('nowrap');
    expect(q('.tl-um-key-badge')).toBeNull();

    expect(text(cards()[0].querySelector('.tl-mu-meta'))).toBe('Last request 2026-10-03 21:15 UTC');
    expect(cards()[0].querySelector('time')?.getAttribute('datetime')).toBe('2026-10-03T21:15:00Z');

    const requestsMetric = cards()[0].querySelector('.tl-metric')!;
    expect(text(requestsMetric.querySelector('dd'))).toBe('3');
    expect(text(requestsMetric.querySelector('.tl-metric-note'))).toBe('60% of total');
    expect(cards()[0].querySelectorAll('.tl-metric').length).toBe(9);

    const total = q('.tl-mu-card-total')!;
    expect(text(total.querySelector('h4'))).toBe('All matching');
    expect(text(total.querySelector('.tl-mu-meta'))).toBe('3 entries');
    expect(total.querySelectorAll('.tl-metric-note').length).toBe(0);

    expect(summary()).toEqual([
      ['Models', '3'], ['Requests', '5'], ['Est. cost', '$1.00'], ['Total tokens', '440'], ['Top model', 'Claude X · Chat · 60% of est. cost']
    ]);
    expect(text(q('.tl-mu-scope-errors'))).toBe('Provider errors are not recorded per user for system AI configurations.');
    expect(q('.tl-mu-scope-cost')).toBeNull();
    expect(status()).toBe('Showing 3 entries · 5 requests');
    expect(q('.tl-mu-status')?.getAttribute('role')).toBe('status');
  });

  it('counts a model once in Models however many role groups it has', () => {
    create();
    open();
    respond(0, dto([...ROWS, modelRow({ roleGroup: 'title' })]));

    expect(cards().length).toBe(4);
    expect(summary()[0]).toEqual(['Models', '3']);
  });

  it('leaves the provider-error note out for user keys, and says when cost recording began', () => {
    create();
    open({ ...REQUEST, keys: 'user' });
    respond(0, dto(ROWS.map(r => ({ ...r, keySource: 'user' as const })), {
      keySource: 'user', costRecordedSinceUtc: '2026-10-02T00:00:00Z'
    }));

    expect(q('.tl-mu-scope-errors')).toBeNull();
    expect(text(q('.tl-mu-scope-cost'))).toBe('Cost recorded since 2026-10-02.');
  });

  // --- Filters ---

  it('offers the Key select and the key badges only under Both', () => {
    const both = [...ROWS, modelRow({ keySource: 'user', estimatedCost: 0.5 })];
    create();
    open({ ...REQUEST, keys: 'both' });
    respond(0, dto(both, { keySource: 'both' }));

    const select = q<HTMLSelectElement>('#tl-um-key')!;
    expect(select).not.toBeNull();
    expect(Array.from(select.options).map(o => o.text)).toEqual(['All keys', 'System AI configurations', 'User keys']);
    expect(text(q('label[for="tl-um-key"]'))).toBe('Key');
    expect(cards().map(c => text(c.querySelector('.tl-um-key-badge')))).toEqual(['key System key', 'key Own key', 'key System key', 'key System key']);
    expect(titles()).toEqual(['Claude X', 'Claude X', 'gpt-x', 'Gemini Y']);

    setSelect('#tl-um-key', 'user');
    expect(titles()).toEqual(['Claude X']);
    expect(q('.tl-mu-scope-errors')).toBeNull();
    expect(q('#tl-um-clear')).not.toBeNull();

    open();
    respond(1);
    expect(q('#tl-um-key')).toBeNull();
    expect(q('.tl-um-key-badge')).toBeNull();
  });

  it('offers the Role select from two role groups, and takes shares and ranks of the filtered scope', () => {
    create();
    open();
    respond(0, dto(ROWS.filter(r => r.roleGroup === 'chat')));
    expect(q('#tl-um-role')).toBeNull();

    open();
    respond(1);
    const select = q<HTMLSelectElement>('#tl-um-role')!;
    expect(Array.from(select.options).map(o => o.text)).toEqual(['All roles', 'Chat', 'Benchmark']);

    setSelect('#tl-um-role', 'chat');
    expect(titles()).toEqual(['Claude X', 'Gemini Y']);
    expect(ranks()).toEqual(['#1', '#2']);
    expect(shares()).toEqual(['86%', '14%']);
    expect(summary()[0]).toEqual(['Models', '2']);
    expect(summary()[1]).toEqual(['Requests', '4']);
    expect(status()).toBe('Showing 2 entries · 4 requests');
    expect(q('#tl-um-clear')).not.toBeNull();
  });

  it('offers the Thinking level filter from two levels, and clears every filter but the view', () => {
    create();
    open();
    respond(0, dto(ROWS.filter(r => r.thinkingLevel === 'high')));
    expect(q('#tl-um-level')).toBeNull();

    open();
    respond(1);
    expect(Array.from(q<HTMLSelectElement>('#tl-um-level')!.options).map(o => o.text)).toEqual(['All levels', 'Low', 'High']);
    setSelect('#tl-um-level', '=low');
    expect(titles()).toEqual(['Claude X', 'Gemini Y']);
    setSelect('#tl-um-thinking', 'separate');
    setSelect('#tl-um-role', 'chat');

    q<HTMLButtonElement>('#tl-um-clear')!.click();
    fixture.detectChanges();

    expect(q<HTMLSelectElement>('#tl-um-level')!.value).toBe('*');
    expect(q<HTMLSelectElement>('#tl-um-role')!.value).toBe('all');
    expect(q<HTMLSelectElement>('#tl-um-thinking')!.value).toBe('separate');
    expect(titles()).toEqual(['Claude X', 'gpt-x', 'Claude X', 'Gemini Y']);
    expect(q('#tl-um-clear')).toBeNull();
    expect(document.activeElement?.id).toBe('tl-um-search');
  });

  it('splits a model into one card per level with Show separately', () => {
    create();
    open();
    respond(0);

    setSelect('#tl-um-thinking', 'separate');

    expect(titles()).toEqual(['Claude X', 'gpt-x', 'Claude X', 'Gemini Y']);
    expect(cards().map(c => text(c.querySelector('.tl-um-badges > li:nth-child(2)')))).toEqual([
      'thinking level High', 'thinking level High', 'thinking level Low', 'thinking level Low'
    ]);
    expect(readStored().separateThinkingLevels).toBe(true);
  });

  it('searches after a short quiet, marks the match and keeps the ranks and shares of the scope', () => {
    create();
    open();
    respond(0);
    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] });

    typeSearch('gem');
    expect(titles()).toEqual(['Claude X', 'gpt-x', 'Gemini Y']);
    vi.advanceTimersByTime(USER_MODELS_SEARCH_DEBOUNCE_MS);
    fixture.detectChanges();

    expect(titles()).toEqual(['Gemini Y']);
    expect(text(cards()[0].querySelector('h4 mark.model-option-match'))).toBe('Gem');
    expect(text(cards()[0].querySelector('code mark.model-option-match'))).toBe('gem');
    expect(ranks()).toEqual(['#3']);
    expect(shares()).toEqual(['10%']);
    expect(status()).toBe('Showing 1 of 3 entries');
    expect(text(q('.tl-mu-card-total .tl-mu-meta'))).toBe('1 entry');
    expect(summary()[0]).toEqual(['Models', '3']);

    typeSearch('openai');
    vi.advanceTimersByTime(USER_MODELS_SEARCH_DEBOUNCE_MS);
    fixture.detectChanges();
    expect(titles()).toEqual(['gpt-x']);
  });

  it('clears the search on Escape without closing the dialog', () => {
    create();
    open();
    respond(0);
    typeSearch('zzz');

    const input = q<HTMLInputElement>('#tl-um-search')!;
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
    typeSearch('opus');
    vi.advanceTimersByTime(USER_MODELS_SEARCH_DEBOUNCE_MS);
    fixture.detectChanges();

    expect(cards().length).toBe(0);
    expect(text(q('.tl-mu-no-match .tl-mu-empty'))).toBe('No entries match "opus".');
    q<HTMLButtonElement>('.tl-mu-no-match-clear')!.click();
    fixture.detectChanges();

    expect(titles()).toEqual(['Claude X', 'gpt-x', 'Gemini Y']);
    expect(document.activeElement?.id).toBe('tl-um-search');
  });

  // --- Sort ---

  it('sorts by the Sort popover both ways, and labels the trigger', () => {
    create();
    open();
    respond(0);
    expect(text(q('#tl-um-sort-trigger'))).toBe('Sort: Est. cost, highest first');
    expect(q('#tl-um-sort-reset')?.getAttribute('aria-disabled')).toBe('true');
    expect(qa<HTMLInputElement>('#tl-um-sort-panel input[name="tl-um-sort-field"]').length).toBe(12);

    q<HTMLInputElement>('#tl-um-sort-field-provider')!.click();
    fixture.detectChanges();
    expect(text(q('#tl-um-sort-trigger'))).toBe('Sort: Provider, Z–A');
    expect(titles()).toEqual(['gpt-x', 'Gemini Y', 'Claude X']);
    expect(ranks()).toEqual(['#1', '#2', '#3']);
    // The share follows requests when the sort field is not additive.
    expect(text(cards()[0].querySelector('.tl-mu-share-label'))).toBe('Share of requests');

    q<HTMLInputElement>('#tl-um-sort-direction-asc')!.click();
    fixture.detectChanges();
    expect(text(q('#tl-um-sort-trigger'))).toBe('Sort: Provider, A–Z');
    expect(titles()).toEqual(['Claude X', 'Gemini Y', 'gpt-x']);
    expect(JSON.parse(localStorage.getItem(TELEMETRY_USER_MODELS_STORAGE_KEY)!)).toEqual({
      version: 1, separateThinkingLevels: false, sort: { field: 'provider', direction: 'asc' }
    });

    q<HTMLInputElement>('#tl-um-sort-field-name')!.click();
    fixture.detectChanges();
    expect(text(q('#tl-um-sort-trigger'))).toBe('Sort: Model name, A–Z');
    expect(titles()).toEqual(['Claude X', 'Gemini Y', 'gpt-x']);

    q<HTMLButtonElement>('#tl-um-sort-reset')!.click();
    fixture.detectChanges();
    expect(text(q('#tl-um-sort-trigger'))).toBe('Sort: Est. cost, highest first');
    expect(titles()).toEqual(['Claude X', 'gpt-x', 'Gemini Y']);
  });

  it('opens the Sort popover from its trigger, Order a labeled group of two radios', () => {
    create();
    open();
    respond(0);
    q<HTMLButtonElement>('#tl-um-sort-trigger')!.click();
    fixture.detectChanges();
    const panel = q('#tl-um-sort-panel')!;
    try {
      expect(panel.matches(':popover-open')).toBe(true);
      const group = panel.querySelector<HTMLElement>('[role="radiogroup"]')!;
      expect(text(panel.querySelector('#' + group.getAttribute('aria-labelledby')))).toBe('Order');
      const radios = Array.from(group.querySelectorAll<HTMLInputElement>('input[type="radio"]'));
      expect(radios.map(r => r.name)).toEqual(['tl-um-sort-direction', 'tl-um-sort-direction']);
      expect(panel.scrollHeight - panel.clientHeight).toBeLessThanOrEqual(1);
    } finally {
      try { panel.hidePopover(); } catch { /* already closed */ }
    }
  });

  // --- Batches ---

  it('shows 50 entries, then the next batch, focusing its first title', () => {
    const many = Array.from({ length: 120 }, (_, i) =>
      modelRow({ modelId: `m-${i}`, modelName: `Model ${String(i).padStart(3, '0')}`, estimatedCost: 1000 - i }));
    create();
    open();
    respond(0, dto(many));

    expect(cards().length).toBe(50);
    expect(status()).toBe('Showing 50 of 120 entries · 120 requests');
    expect(text(q('.tl-mu-more'))).toBe('Show 50 more');
    expect(text(q('.tl-mu-all'))).toBe('Show all 120');
    expect(text(q('.tl-mu-card-total .tl-mu-meta'))).toBe('120 entries');

    q<HTMLButtonElement>('.tl-mu-more')!.click();
    fixture.detectChanges();
    expect(cards().length).toBe(100);
    expect(document.activeElement?.id).toBe('tl-um-50-title');
    expect(text(q('.tl-mu-more'))).toBe('Show 20 more');
    expect(q('.tl-mu-all')).toBeNull();

    q<HTMLButtonElement>('.tl-mu-more')!.click();
    fixture.detectChanges();
    expect(cards().length).toBe(120);
    expect(q('.tl-mu-load-more')).toBeNull();
  });

  // --- States ---

  it('says so when the user used no model in the range', () => {
    create();
    open();
    respond(0, dto([]));

    expect(text(q('.tl-mu-empty'))).toBe('This user used no model in this range.');
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
    expect(titles()).toEqual(['Claude X', 'gpt-x', 'Gemini Y']);
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

  // --- Beside the Usage by user dialog ---

  it('shares no element id with the Usage by user dialog when both are rendered', () => {
    const host = TestBed.createComponent(BothDialogsHostComponent);
    host.detectChanges();
    const root = host.nativeElement as HTMLElement;

    host.componentInstance.modelUsers.open({
      start: '2026-10-01', end: '2026-10-03', keySource: 'system', provider: 'Anthropic', modelId: 'claude-x',
      modelTitle: 'Claude X', roleGroup: 'chat', thinkingLevel: 'high'
    });
    modelUserRequests[0].subject.next({
      keySource: 'system', provider: 'Anthropic', modelId: 'claude-x', modelName: 'Claude X', roles: ['chat'],
      costRecordedSinceUtc: '2026-09-01T00:00:00Z',
      rows: [
        { userId: 'u1', userName: 'alice', roleContext: 1, roleGroup: 'chat', thinkingLevel: 'high', requests: 1, promptTokens: 1,
          uncachedInputTokens: 1, cacheReadTokens: 0, cacheCreationTokens: 0, outputTokens: 1, durationSumMs: 1, durationCount: 1,
          estimatedCost: 0.1, pricedRequests: 1, lastRequestUtc: '2026-10-02T09:30:00Z' },
        { userId: 'u2', userName: 'bob', roleContext: 1, roleGroup: 'chat', thinkingLevel: 'low', requests: 1, promptTokens: 1,
          uncachedInputTokens: 1, cacheReadTokens: 0, cacheCreationTokens: 0, outputTokens: 1, durationSumMs: 1, durationCount: 1,
          estimatedCost: 0.1, pricedRequests: 1, lastRequestUtc: '2026-10-02T09:30:00Z' }
      ]
    });
    host.componentInstance.userModels.open({ ...REQUEST, keys: 'both' });
    requests[0].subject.next(dto([...ROWS, modelRow({ keySource: 'user' })], { keySource: 'both' }));
    host.detectChanges();
    const roleSelect = root.querySelector<HTMLSelectElement>('#tl-um-role')!;
    roleSelect.value = 'chat';
    roleSelect.dispatchEvent(new Event('change', { bubbles: true }));
    host.detectChanges();

    const idsOf = (selector: string) =>
      Array.from(root.querySelector(selector)!.querySelectorAll('[id]'), node => node.id);
    const modelUserIds = idsOf('app-telemetry-model-users-dialog');
    const userModelIds = idsOf('app-telemetry-user-models-dialog');
    expect(modelUserIds).toContain('tl-mu-clear');
    expect(userModelIds).toContain('tl-um-clear');
    expect(userModelIds).toContain('tl-um-key');
    expect(userModelIds).toContain('tl-um-level');
    expect(userModelIds).toContain('tl-um-0-title');
    const shared = userModelIds.filter(id => modelUserIds.includes(id));
    expect(shared).toEqual([]);

    host.destroy();
  });
});
