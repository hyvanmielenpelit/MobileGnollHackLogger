import { ComponentFixture, TestBed } from '@angular/core/testing';
import {
  AiGovernorKeyStatusDto,
  AiGovernorStatusDto,
  AiTelemetrySummaryDto,
  AiUsageBreakdownRowDto,
  ChatProviderAvailabilityDto,
  OverseerActivityDto
} from '../../../services/admin.service';
import { TelemetrySummaryComponent } from './telemetry-summary.component';

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

const governor = (keys: AiGovernorKeyStatusDto[] = []): AiGovernorStatusDto => ({
  maxConcurrentCalls: 4, maxRetryAfterSeconds: 90, activeKeys: keys
});

const key = (credentialKey: string, remainingSeconds: number): AiGovernorKeyStatusDto => ({
  credentialKey, isRateLimited: remainingSeconds > 0, remainingCooldownSeconds: remainingSeconds, inFlightCalls: 0
});

/** "YYYY-MM-DD" in local time, as the tab writes its range. */
const localDate = (d: Date): string =>
  `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;

describe('TelemetrySummaryComponent', () => {
  let fixture: ComponentFixture<TelemetrySummaryComponent>;
  let component: TelemetrySummaryComponent;
  let resets: (string | undefined)[];

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [TelemetrySummaryComponent] }).compileComponents();
    fixture = TestBed.createComponent(TelemetrySummaryComponent);
    component = fixture.componentInstance;
    resets = [];
    component.resetCooldown.subscribe(credentialKey => resets.push(credentialKey));
  });

  const host = () => fixture.nativeElement as HTMLElement;

  /** Renders the panel with the tab's inputs: a summary, the governor's state and a range starting 30 days ago. */
  const render = (inputs: { summary?: AiTelemetrySummaryDto | null; governorStatus?: AiGovernorStatusDto | null; governorLoading?: boolean; start?: string } = {}) => {
    const start = new Date();
    start.setDate(start.getDate() - 30);
    fixture.componentRef.setInput('summary', inputs.summary === undefined ? summary() : inputs.summary);
    fixture.componentRef.setInput('governorStatus', inputs.governorStatus === undefined ? governor() : inputs.governorStatus);
    fixture.componentRef.setInput('governorLoading', inputs.governorLoading ?? false);
    fixture.componentRef.setInput('start', inputs.start ?? localDate(start));
    fixture.componentRef.setInput('end', localDate(new Date()));
    fixture.detectChanges();
  };

  const clearAllButton = (): HTMLButtonElement =>
    (Array.from(host().querySelectorAll('button')) as HTMLButtonElement[])
      .find(b => (b.textContent ?? '').includes('Clear All Cooldowns'))!;

  const normalize = (text: string | null | undefined) => (text ?? '').replace(/\s+/g, ' ').trim();

  /** Each `dt`/`dd` pair of a summary card's term lists, whitespace collapsed. */
  const termValues = (cardSelector: string): string[][] =>
    Array.from(host().querySelectorAll(`${cardSelector} dl > div`)).map(pair =>
      ['dt', 'dd'].map(tag => (pair.querySelector(tag)?.textContent ?? '').replace(/\s+/g, ' ').trim()));

  // --- Governor ---

  it('renders Clear All Cooldowns as a small image button, aria-disabled only when nothing is rate-limited', () => {
    render();

    let button = clearAllButton();
    expect(button.matches('button.btn-gh.btn-gh-small[type="button"]')).toBe(true);
    expect(button.hasAttribute('title')).toBe(false);
    expect(button.getAttribute('aria-disabled')).toBe('true');

    fixture.componentRef.setInput('governorStatus', governor([key('openai:user:u1', 30)]));
    fixture.detectChanges();

    button = clearAllButton();
    expect(button.hasAttribute('aria-disabled')).toBe(false);
  });

  it('asks the tab to clear every cooldown from Clear All Cooldowns, and one partition\'s from its row', () => {
    render({ governorStatus: governor([key('openai:user:u1', 30), key('openai:system:7', 0)]) });

    clearAllButton().click();
    expect(resets).toEqual([undefined]);

    const rowButtons = host().querySelectorAll<HTMLButtonElement>('.telemetry-partitions button.action-btn');
    expect(rowButtons.length).toBe(1);
    rowButtons[0].click();
    expect(resets).toEqual([undefined, 'openai:user:u1']);
  });

  it('renders the Other request line only when the named counts fall short of the total', () => {
    render();
    expect(host().querySelector('.telemetry-other-requests')).toBeNull();

    fixture.componentRef.setInput('summary', summary({ totalRequests: 13 }));
    fixture.detectChanges();

    const other: HTMLElement | null = host().querySelector('.telemetry-other-requests');
    expect(other).not.toBeNull();
    expect(other!.textContent).toContain('3');
  });

  it('keeps the partitions section with an empty-state line when no partition exists', () => {
    render();

    const section: HTMLElement | null = host().querySelector('.telemetry-partitions');
    expect(section).not.toBeNull();
    expect(section!.textContent).toContain('No partitions have been used since the last restart.');
    expect(section!.querySelector('table')).toBeNull();
  });

  it('says the governor status is loading, then unavailable, while there is none', () => {
    render({ governorStatus: null, governorLoading: true });
    const empty = () => normalize(host().querySelector('.telemetry-partitions .telemetry-empty')?.textContent);
    expect(empty()).toBe('Loading governor status…');

    fixture.componentRef.setInput('governorLoading', false);
    fixture.detectChanges();
    expect(empty()).toBe('Governor status unavailable.');
    expect(normalize(host().querySelector('.telemetry-governor-card .telemetry-stat-value')?.textContent)).toBe('— Max Concurrency');
  });

  it('names each row reset button after its partition and pairs it with a hint tooltip', () => {
    render({ governorStatus: governor([key('openai:system:7', 30)]) });

    const button: HTMLButtonElement = host().querySelector('.telemetry-partitions button.action-btn')!;
    expect(button.getAttribute('type')).toBe('button');
    expect(button.getAttribute('aria-label')).toBe('Clear cooldown for openai:system:7');
    expect(button.hasAttribute('title')).toBe(false);
    const tip: HTMLElement | null = host().querySelector('#' + button.getAttribute('interestfor'));
    expect(tip?.getAttribute('popover')).toBe('hint');
  });

  it('tags the Request Governor card and the Rate Limit Partitions heading live', () => {
    render();

    const governorTitle = host().querySelector('.telemetry-governor-card .telemetry-card-title') as HTMLElement;
    expect(normalize(governorTitle.querySelector('.telemetry-card-scope')?.textContent)).toBe('live');
    const partitions = host().querySelector('.telemetry-partitions h4') as HTMLElement;
    expect(partitions.classList.contains('tl-title-row')).toBe(true);
    expect(normalize(partitions.querySelector('.telemetry-card-scope')?.textContent)).toBe('live');
  });

  describe('badges that never wrap', () => {
    /** Narrows a summary card, and checks that its scope badge is one box below the title text. */
    const expectBadgeOnNextLine = (cardSelector: string, width: string) => {
      const card = host().querySelector(cardSelector) as HTMLElement;
      card.style.width = width;
      void card.offsetWidth;
      const title = card.querySelector('.telemetry-card-title') as HTMLElement;
      const text = title.querySelector(':scope > span:not(.telemetry-card-scope)') as HTMLElement;
      const badge = title.querySelector('.telemetry-card-scope') as HTMLElement;
      expect(getComputedStyle(badge).whiteSpace).toBe('nowrap');
      expect(badge.getClientRects().length).toBe(1);
      expect(badge.getBoundingClientRect().top).toBeGreaterThan(text.getBoundingClientRect().top);
    };

    it('moves the all keys badge of a narrow Overseer Activity card to the next line whole', () => {
      render();
      expectBadgeOnNextLine('.telemetry-activity-card', '220px');
    });

    it('moves the live badge of a narrow Request Governor card to the next line whole', () => {
      render();
      expectBadgeOnNextLine('.telemetry-governor-card', '150px');
    });
  });

  // --- Summary cards ---

  it('shows Not recorded on the availability card until the ledger has a row, then the rate', () => {
    render();
    const rate = () => host().querySelector('.telemetry-availability-rate')?.textContent ?? '';
    expect(rate()).toContain('Not recorded');
    expect(host().querySelector('.telemetry-error-classes-card')).toBeNull();

    fixture.componentRef.setInput('summary', summary({
      chatAvailability: availability({
        recorded: true, validAttempts: 1480, providerErrors: 12, providerErrorRate: 12 / 1480, low: 0.0046, high: 0.014,
        titleValidAttempts: 140, titleProviderErrors: 2
      })
    }));
    fixture.detectChanges();

    expect(rate()).toContain('0.81%');
    expect(host().querySelector('.telemetry-availability-count')?.textContent).toContain('12 of 1,480 attempts');
    expect(termValues('.telemetry-availability-card')).toContainEqual(['Title generation', '2 of 140 attempts']);
    expect(host().querySelector('.telemetry-error-classes-card')).not.toBeNull();
  });

  it('shows the error classes and the unattributed and our-side counts on their own card', () => {
    render({
      summary: summary({
        chatAvailability: availability({
          recorded: true, validAttempts: 300, providerErrors: 6, providerErrorRate: 6 / 300,
          overloaded: 3, serverErrors: 2, timeouts: 1, streamFailures: 0,
          unattributedErrors: 4, rateLimitedUnattributed: 3, transportUnattributed: 1, inclusiveRate: 0.02, ourSideFailures: 5
        })
      })
    });

    const card = '.telemetry-error-classes-card';
    expect(host().querySelector(`${card} .telemetry-error-total`)?.textContent).toContain('6');
    expect(termValues(card)).toEqual([
      ['Overloaded', '3'],
      ['Server errors', '2'],
      ['Timeouts', '1'],
      ['Stream failures', '0'],
      ['Unattributed', '4 rate limited 3 · transport 1 · 2.0% with them'],
      ['Our side', '5 excluded from the rate']
    ]);
    const notes = Array.from(host().querySelectorAll(`${card} .telemetry-dd-note`)).map(n => n.textContent?.trim());
    expect(notes).toEqual(['rate limited 3 · transport 1 · 2.0% with them', 'excluded from the rate']);
  });

  it('shows the estimated cost with the unpriced requests and when cost recording began', () => {
    const today = localDate(new Date());
    render({
      summary: summary({
        totalRequests: 10, pricedRequests: 7, totalEstimatedCost: 2.5, costRecordedSinceUtc: `${today}T08:00:00Z`,
        rows: [row({ requests: 7, estimatedCost: 2.5, pricedRequests: 7 })]
      })
    });

    const card = host().querySelector('.telemetry-cost-card')?.textContent ?? '';
    expect(card).toContain('$2.50');
    expect(card).toContain('Chat: $2.50');
    expect(card).toContain('3 of 10 requests unpriced');
    expect(card).toContain(`Cost recorded since ${today}`);
  });

  it("shows Overseer activity for all keys with its counts, the anonymous incognito line and the day counting began", () => {
    const today = localDate(new Date());
    render({
      summary: summary({
        activity: activity({
          activeUsers: 1234, newUsers: 5, chatsCreated: 20, confidentialChatsCreated: 2, gnollHackChatsCreated: 3,
          userMessages: 10, assistantMessages: 9, incognitoChatsCreated: 4, incognitoUserMessages: 7, countedSinceUtc: today
        })
      })
    });

    const card = () => (host().querySelector('.telemetry-activity-card')?.textContent ?? '').replace(/\s+/g, ' ');
    expect(card()).toContain('all keys');
    expect(host().querySelector('.telemetry-active-users')?.textContent).toContain('1,234');
    expect(card()).toContain('New users: 5');
    expect(card()).toContain('Chats: 20');
    expect(card()).toContain('confidential 2 · GnollHack 3');
    expect(card()).toContain('Messages: 19');
    expect(card()).toContain('sent 10 · replies 9');
    expect(card()).toContain('Incognito chats 4 · messages 7');
    expect(card()).toContain('not attributed to users');
    expect(card()).toContain(`Counted since ${today}`);

    fixture.componentRef.setInput('summary', summary({ activity: activity({ countedSinceUtc: '2000-01-01' }) }));
    fixture.detectChanges();
    expect(card()).not.toContain('Counted since');
  });
});
