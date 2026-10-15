import { ComponentFixture, TestBed } from '@angular/core/testing';

import type {
  BenchmarkBatteryMemberAvailabilityDto,
  ProviderAvailabilityDto,
  ProviderAvailabilityOutcomeDto
} from '../../../../services/admin-benchmark.service';
import {
  PROVIDER_AVAILABILITY_VIEW_STORAGE_KEY,
  ProviderAvailabilityComponent,
  compactReruns,
  formatUtcMinute,
  providerErrorsFigure,
  readStoredProviderAvailabilityView,
  replacementCauseText,
  stripShadeOf
} from './provider-availability.component';

function outcome(name: string, code: number, side: ProviderAvailabilityOutcomeDto['side'], count: number, httpStatuses: number[] = []): ProviderAvailabilityOutcomeDto {
  return { outcome: name, code, side, count, httpStatuses };
}

/** 1,480 valid attempts: 12 provider errors (9 overloaded, 3 server errors), 3 unattributed, 4 ours. */
function availability(overrides: Partial<ProviderAvailabilityDto> = {}): ProviderAvailabilityDto {
  return {
    recorded: true,
    recordedRuns: 1,
    totalRuns: 1,
    replacedRunsIncluded: 0,
    validAttempts: 1480,
    providerErrors: 12,
    unattributedErrors: 3,
    ourSideFailures: 4,
    providerErrorRate: 12 / 1480,
    providerErrorRateLow: 0.0044,
    providerErrorRateHigh: 0.015,
    designEffect: 1.2,
    inclusiveRate: 15 / 1480,
    inclusiveRateLow: 0.006,
    inclusiveRateHigh: 0.017,
    fewAttempts: false,
    overloaded: 9,
    serverErrors: 3,
    timeouts: 0,
    streamFailures: 0,
    rateLimitedOurs: 0,
    rateLimitedUnattributed: 3,
    transportUnattributed: 0,
    byOutcome: [
      outcome('Ok', 0, 'ok', 1465, [200]),
      outcome('ProviderOverloaded', 10, 'provider', 9, [529, 503]),
      outcome('ProviderServerError', 11, 'provider', 3, [500]),
      outcome('ProviderTimeout', 12, 'provider', 0),
      outcome('ProviderStreamFailure', 13, 'provider', 0),
      outcome('RateLimitUnattributed', 20, 'unattributed', 3, [429]),
      outcome('TransportUnattributed', 21, 'unattributed', 0),
      outcome('OurRateLimit', 30, 'ours', 0),
      outcome('OurBilling', 31, 'ours', 0),
      outcome('OurKey', 32, 'ours', 0),
      outcome('OurRequest', 33, 'ours', 0),
      outcome('OurThrottle', 34, 'ours', 1),
      outcome('OurCanceled', 35, 'ours', 3),
      outcome('OurNetwork', 36, 'ours', 0)
    ],
    questionsLost: 1,
    calls: 1480,
    firstAttemptSuccessCalls: 1450,
    repairPasses: 2,
    providerRepairPasses: 1,
    replacementRuns: 1,
    providerReplacementRuns: 1,
    rerunsNeeded: 3,
    providerRerunsNeeded: 2,
    graderValidAttempts: 240,
    graderProviderErrors: 3,
    bins: [
      { startUtc: '2026-10-12T13:00:00Z', valid: 400, provider: 0, unattributed: 0, ours: 0 },
      { startUtc: '2026-10-12T13:05:00Z', valid: 0, provider: 0, unattributed: 0, ours: 0 },
      { startUtc: '2026-10-12T13:10:00Z', valid: 0, provider: 0, unattributed: 0, ours: 4 },
      { startUtc: '2026-10-12T13:15:00Z', valid: 1080, provider: 12, unattributed: 3, ours: 0 }
    ],
    replaces: [{ runId: 41, replacedByRunId: 55, cause: 'ProviderFailure' }],
    ...overrides
  };
}

function member(overrides: Partial<BenchmarkBatteryMemberAvailabilityDto> = {}): BenchmarkBatteryMemberAvailabilityDto {
  return {
    memberId: 1, runId: 101, suiteIndex: 0, round: 1, superseded: false, recorded: true,
    validAttempts: 700, providerErrors: 7, unattributedErrors: 1, providerErrorRate: 0.01,
    rerunsNeeded: 1, providerRerunsNeeded: 1,
    ...overrides
  };
}

describe('provider availability helpers', () => {
  it('builds the key-figure tile from a recorded run', () => {
    expect(providerErrorsFigure(availability())).toEqual({
      recorded: true,
      value: '0.81%',
      muted: false,
      count: '12 of 1,480 attempts',
      note: '95% CI 0.44–1.5% · 3 unattributed · 3 re-runs (2 provider)',
      fewAttempts: false
    });
  });

  it('reads Not recorded on a legacy run, and a dash with few attempts without a valid attempt', () => {
    for (const legacy of [null, undefined, availability({ recorded: false })]) {
      const figure = providerErrorsFigure(legacy);
      expect(figure.recorded).toBe(false);
      expect(figure.value).toBe('Not recorded');
      expect(figure.muted).toBe(true);
      expect(figure.count).toBeNull();
    }

    const empty = providerErrorsFigure(availability({
      validAttempts: 0, providerErrors: 0, unattributedErrors: 0, providerErrorRate: null,
      providerErrorRateLow: null, providerErrorRateHigh: null, rerunsNeeded: 0, providerRerunsNeeded: 0
    }));
    expect(empty.value).toBe('—');
    expect(empty.muted).toBe(true);
    expect(empty.count).toBe('0 of 0 attempts');
    expect(empty.note).toBe('0 re-runs');
    expect(empty.fewAttempts).toBe(true);
  });

  it('tags a run with fewer than 100 valid attempts', () => {
    const figure = providerErrorsFigure(availability({
      validAttempts: 99, providerErrors: 3, providerErrorRate: 3 / 99, unattributedErrors: 0, fewAttempts: true
    }));
    expect(figure.value).toBe('3.0%');
    expect(figure.count).toBe('3 of 99 attempts');
    expect(figure.fewAttempts).toBe(true);
  });

  it('writes the compact re-runs', () => {
    expect(compactReruns(0, 0)).toBe('0 re-runs');
    expect(compactReruns(1, 0)).toBe('1 re-run');
    expect(compactReruns(2, 1)).toBe('2 re-runs (1 provider)');
  });

  it('shades a bin by its provider-error share, gray without attempts and hatched for our side only', () => {
    const bin = (valid: number, provider: number, ours = 0) => ({ startUtc: '2026-10-12T13:00:00Z', valid, provider, unattributed: 0, ours });
    expect(stripShadeOf(bin(0, 0))).toBe('empty');
    expect(stripShadeOf(bin(0, 0, 2))).toBe('ours');
    expect(stripShadeOf(bin(100, 0, 2))).toBe('s0');
    expect(stripShadeOf(bin(100, 1))).toBe('s1');
    expect(stripShadeOf(bin(100, 5))).toBe('s2');
    expect(stripShadeOf(bin(100, 20))).toBe('s3');
    expect(stripShadeOf(bin(100, 21))).toBe('s4');
  });

  it('writes UTC minutes and the replacement causes', () => {
    expect(formatUtcMinute('2026-10-12T13:05:00')).toBe('2026-10-12 13:05 UTC');
    expect(replacementCauseText('ProviderFailure')).toBe('which failed at the provider');
    expect(replacementCauseText('OurFailure')).toBe('which failed on our side');
    expect(replacementCauseText(null)).toBe('cause not recorded');
  });
});

describe('ProviderAvailabilityComponent', () => {
  let fixture: ComponentFixture<ProviderAvailabilityComponent>;

  function clearView(): void {
    try {
      localStorage.removeItem(PROVIDER_AVAILABILITY_VIEW_STORAGE_KEY);
    } catch {
      // Storage unavailable.
    }
  }

  beforeEach(async () => {
    clearView();
    await TestBed.configureTestingModule({ imports: [ProviderAvailabilityComponent] }).compileComponents();
  });

  afterEach(clearView);

  function create(inputs: Record<string, unknown>): HTMLElement {
    fixture = TestBed.createComponent(ProviderAvailabilityComponent);
    for (const [name, value] of Object.entries(inputs)) {
      fixture.componentRef.setInput(name, value);
    }
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  function el(): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  function text(selector: string): string {
    return (el().querySelector(selector)?.textContent ?? '').replace(/\s+/g, ' ').trim();
  }

  function rowTexts(selector: string): string[][] {
    return Array.from(el().querySelectorAll<HTMLTableRowElement>(selector)).map(row =>
      Array.from(row.cells).map(cell => (cell.textContent ?? '').replace(/\s+/g, ' ').trim()));
  }

  it('reads Not recorded on a legacy run and shows no figures', () => {
    create({ availability: availability({ recorded: false }) });
    expect(text('.pa-title')).toBe('Provider availability');
    expect(text('.pa-not-recorded')).toBe('Not recorded: runs started before harness 56 record no provider attempts.');
    expect(el().querySelector('.pa-breakdown')).toBeNull();
    expect(el().querySelector('[role="tablist"]')).toBeNull();

    create({ availability: null });
    expect(el().querySelector('.pa-not-recorded')).not.toBeNull();
  });

  it('opens with the observed headline, its natural frequency and the unattributed upper bound', () => {
    create({ availability: availability() });
    expect(text('.pa-summary')).toBe(
      'Provider errors observed: 0.81% (12 of 1,480 attempts; 95% CI 0.44–1.5%), about 1 in 123; ≤ 1.0% incl. 3 unattributed.');
    expect(el().querySelector('.pa-few')).toBeNull();
    expect(el().querySelector('.pa-scope')).toBeNull();
  });

  it('tags few attempts for the eye and says which runs a pooled figure covers', () => {
    create({
      availability: availability({
        validAttempts: 99, providerErrors: 3, unattributedErrors: 0, providerErrorRate: 3 / 99,
        providerErrorRateLow: 0.01, providerErrorRateHigh: 0.085, fewAttempts: true,
        recordedRuns: 2, totalRuns: 3, replacedRunsIncluded: 1
      })
    });
    expect(text('.pa-summary')).toContain('3.0% (3 of 99 attempts; 95% CI 1.0–8.5%; few attempts)');
    const tag = el().querySelector('.pa-few') as HTMLElement;
    expect(tag.textContent?.trim()).toBe('few attempts');
    expect(tag.getAttribute('aria-hidden')).toBe('true');
    expect(text('.pa-scope')).toBe('Recorded for 2 of 3 runs; includes 1 replaced run.');
  });

  it('breaks the valid attempts down by class, with our side excluded under its own heading', () => {
    create({ availability: availability() });
    const groups = Array.from(el().querySelectorAll('.pa-breakdown tbody')).map(body => body.getAttribute('data-side'));
    expect(groups).toEqual(['ok', 'provider', 'unattributed']);
    expect(rowTexts('.pa-breakdown tbody tr[data-outcome]')).toEqual([
      ['Succeeded', '1,465', '99%', 'HTTP 200'],
      ['Overloaded', '9', '0.61%', 'HTTP 529, HTTP 503'],
      ['Server error', '3', '0.20%', 'HTTP 500'],
      ['Timeout', '0', '0%', '—'],
      ['Stream failure', '0', '0%', '—'],
      ['Rate limited, unattributed', '3', '0.20%', 'HTTP 429'],
      ['Connection failure, unattributed', '0', '0%', '—']
    ]);
    expect(rowTexts('.pa-breakdown .pa-group-row').map(r => r[0])).toEqual(['Provider', 'Unattributed']);
    expect(rowTexts('.pa-breakdown tfoot tr')).toEqual([['Valid attempts', '1,480', '100%', '']]);

    const heading = el().querySelector('h5.pa-subtitle') as HTMLElement;
    expect(heading.textContent?.trim()).toBe('Excluded: our side');
    expect(el().querySelector('.pa-ours')?.getAttribute('aria-labelledby')).toBe(heading.id);
    const ours = rowTexts('.pa-ours tbody tr');
    expect(ours.length).toBe(7);
    expect(ours).toContainEqual(['Overseer request governor', '1', '—']);
    expect(ours).toContainEqual(['Canceled or question time budget', '3', '—']);
    expect(rowTexts('.pa-ours tfoot tr')).toEqual([['Excluded attempts', '4', '']]);
  });

  it('shows the strip by default: shaded cells, gray and hatched bins, and a hidden table of the same data', () => {
    create({ availability: availability() });
    const tabs = Array.from(el().querySelectorAll<HTMLButtonElement>('[role="tablist"] [role="tab"]'));
    expect(el().querySelector('[role="tablist"]')?.getAttribute('aria-label')).toBe('Outcome view');
    expect(tabs.map(t => t.textContent?.trim())).toEqual(['Time strip', 'Stacked bar']);
    expect(tabs.map(t => t.getAttribute('aria-selected'))).toEqual(['true', 'false']);
    expect(tabs.map(t => t.getAttribute('tabindex'))).toEqual(['0', '-1']);
    const panel = el().querySelector('[role="tabpanel"]') as HTMLElement;
    expect(panel.getAttribute('aria-labelledby')).toBe(tabs[0].id);
    expect(tabs[0].getAttribute('aria-controls')).toBe(panel.id);

    const strip = el().querySelector('.pa-strip') as HTMLElement;
    expect(strip.getAttribute('aria-hidden')).toBe('true');
    expect(Array.from(strip.querySelectorAll('.pa-cell')).map(c => c.getAttribute('data-shade')))
      .toEqual(['s0', 'empty', 'ours', 's2']);
    expect(text('.pa-strip-axis')).toBe('2026-10-12 13:00 UTC 2026-10-12 13:20 UTC');

    const table = el().querySelector('table.pa-strip-table') as HTMLTableElement;
    expect(table.classList).toContain('visually-hidden');
    expect(rowTexts('.pa-strip-table tbody tr')).toEqual([
      ['2026-10-12 13:00 UTC', '400', '0', '0', '0', '0%'],
      ['2026-10-12 13:05 UTC', '0', '0', '0', '0', '—'],
      ['2026-10-12 13:10 UTC', '0', '0', '0', '4', '—'],
      ['2026-10-12 13:15 UTC', '1,080', '12', '3', '0', '1.1%']
    ]);
  });

  it('switches to the stacked bar from its tab, remembers it, and labels every class with its count', () => {
    create({ availability: availability() });
    (el().querySelector('[role="tab"][data-view="bar"]') as HTMLButtonElement).click();
    fixture.detectChanges();

    expect(JSON.parse(localStorage.getItem(PROVIDER_AVAILABILITY_VIEW_STORAGE_KEY)!)).toEqual({ version: 1, view: 'bar' });
    expect(el().querySelector('.pa-strip')).toBeNull();
    expect(el().querySelector('[role="tab"][data-view="bar"]')?.getAttribute('aria-selected')).toBe('true');
    const segments = Array.from(el().querySelectorAll<HTMLElement>('.pa-bar .pa-seg'));
    expect(segments.map(s => s.getAttribute('data-outcome'))).toEqual([
      'Ok', 'ProviderOverloaded', 'ProviderServerError', 'RateLimitUnattributed', 'OurThrottle', 'OurCanceled'
    ]);
    expect(segments[0].textContent?.trim()).toBe('1,465');
    expect(segments[1].textContent?.trim()).toBe('');
    expect(Array.from(el().querySelectorAll('.pa-bar-legend li')).map(li => (li.textContent ?? '').replace(/\s+/g, ' ').trim()))
      .toEqual([
        'Succeeded: 1,465 (99%)',
        'Overloaded: 9 (0.61%)',
        'Server error: 3 (0.20%)',
        'Rate limited, unattributed: 3 (0.20%)',
        'Overseer request governor: 1 (0.067%), excluded',
        'Canceled or question time budget: 3 (0.20%), excluded'
      ]);

    create({ availability: availability() });
    expect(el().querySelector('.pa-bar')).not.toBeNull();
  });

  it('moves between the views with the arrow keys, focus following', () => {
    create({ availability: availability() });
    const strip = el().querySelector('[role="tab"][data-view="strip"]') as HTMLButtonElement;
    strip.focus();
    strip.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowRight', bubbles: true }));
    fixture.detectChanges();

    const bar = el().querySelector('[role="tab"][data-view="bar"]') as HTMLButtonElement;
    expect(bar.getAttribute('aria-selected')).toBe('true');
    expect(document.activeElement).toBe(bar);

    bar.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowRight', bubbles: true }));
    fixture.detectChanges();
    expect(strip.getAttribute('aria-selected')).toBe('true');
  });

  it('reads the strip when the stored view is unknown or storage throws', () => {
    localStorage.setItem(PROVIDER_AVAILABILITY_VIEW_STORAGE_KEY, JSON.stringify({ version: 1, view: 'pie' }));
    expect(readStoredProviderAvailabilityView()).toBe('strip');
    localStorage.setItem(PROVIDER_AVAILABILITY_VIEW_STORAGE_KEY, JSON.stringify({ version: 2, view: 'bar' }));
    expect(readStoredProviderAvailabilityView()).toBe('strip');
    const spy = vi.spyOn(localStorage, 'getItem').mockImplementation(() => {
      throw new Error('denied');
    });
    expect(readStoredProviderAvailabilityView()).toBe('strip');
    spy.mockRestore();
  });

  it('gives the re-runs, the replaced runs as links, first-attempt success and the graders', () => {
    create({ availability: availability() });
    expect(text('.pa-reruns dd')).toBe(
      '3 re-runs (2 for provider errors): 2 repair passes (1 for provider errors), 1 replaced run (1 for provider errors)');
    expect(text('.pa-replaces-list li')).toBe('Run #55 replaces run #41, which failed at the provider');
    expect(text('.pa-first-attempt dd')).toBe('98% (1,450 of 1,480 calls)');
    expect(text('.pa-lost dd')).toBe('1');
    expect(text('.pa-graders dd')).toBe('3 provider errors in 240 grader attempts; for information, not counted above');

    const opened: number[] = [];
    fixture.componentInstance.openRun.subscribe(id => opened.push(id));
    const link = el().querySelector('.pa-replaces-list button.pa-run-link') as HTMLButtonElement;
    expect(link.getAttribute('type')).toBe('button');
    expect(link.getAttribute('aria-label')).toBe('Open run #41');
    link.click();
    expect(opened).toEqual([41]);
  });

  it('lists a battery run\'s members, superseded ones marked and legacy ones not recorded', () => {
    create({
      availability: availability(),
      suites: [{ index: 0, suiteName: 'Gameplay Help' }, { index: 1, suiteName: 'Board Reading' }],
      members: [
        member({ memberId: 3, runId: 103, suiteIndex: 1, round: 1, recorded: false, validAttempts: null, providerErrors: null, unattributedErrors: null, providerErrorRate: null, rerunsNeeded: 0, providerRerunsNeeded: 0 }),
        member(),
        member({ memberId: 2, runId: 99, superseded: true, validAttempts: 80, providerErrors: 30, providerErrorRate: 0.375, rerunsNeeded: 0, providerRerunsNeeded: 0 })
      ]
    });

    expect(text('#' + fixture.componentInstance.uid + '-members')).toBe('Members');
    expect(rowTexts('.pa-members tbody tr')).toEqual([
      ['Gameplay Help, round 1, run #101', '700', '7', '1.0%', '1 re-run (1 provider)'],
      ['Gameplay Help, round 1, run #99 superseded', '80', '30', '38%', '0 re-runs'],
      ['Board Reading, round 1, run #103', 'Not recorded', '—', '—', '0 re-runs']
    ]);
    const superseded = el().querySelector('.pa-members tr[data-member-id="2"]') as HTMLElement;
    expect(superseded.classList).toContain('pa-superseded');

    const opened: number[] = [];
    fixture.componentInstance.openRun.subscribe(id => opened.push(id));
    (el().querySelector('.pa-members button[data-run-id="103"]') as HTMLButtonElement).click();
    expect(opened).toEqual([103]);
  });

  it('shows no member table outside a battery run', () => {
    create({ availability: availability() });
    expect(el().querySelector('.pa-members')).toBeNull();
  });
});
