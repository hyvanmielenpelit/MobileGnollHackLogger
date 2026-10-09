import { ComponentFixture, TestBed } from '@angular/core/testing';

import { CC_NO_PERIOD_IDS, CcPeriodIds, ccPeriodMembers, ccPeriodUnits, ccPeriodWindows } from '../../chat-consistency-periods';
import { ccSampleNeedText } from '../../chat-consistency-readiness';
import { ccBatteryRunRows, ccRunRows, textOf } from '../../chat-consistency-tab.testing';
import { CcPeriodSummaryComponent } from './period-summary.component';

/** Runs 101–103 against 104–106. */
const RUN_IDS: CcPeriodIds = { baselineFirstId: 101, baselineLastId: 103, comparisonFirstId: 104, comparisonLastId: 106 };

/** Battery run 11 against 12, both on 2026-10-08. */
const BATTERY_IDS: CcPeriodIds = { baselineFirstId: 11, baselineLastId: 11, comparisonFirstId: 12, comparisonLastId: 12 };

describe('CcPeriodSummaryComponent', () => {
  let fixture: ComponentFixture<CcPeriodSummaryComponent>;
  let component: CcPeriodSummaryComponent;
  let el: HTMLElement;

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [CcPeriodSummaryComponent] }).compileComponents();
    fixture = TestBed.createComponent(CcPeriodSummaryComponent);
    component = fixture.componentInstance;
    el = fixture.nativeElement as HTMLElement;
  });

  afterEach(() => fixture.destroy());

  function set(inputs: Partial<Record<keyof CcPeriodSummaryComponent, unknown>>): void {
    for (const [name, value] of Object.entries(inputs)) fixture.componentRef.setInput(name, value);
    fixture.detectChanges();
  }

  /** The runs 101–106 split at 103 / 104, as the host binds them. */
  function renderRuns(extra: Partial<Record<keyof CcPeriodSummaryComponent, unknown>> = {}): void {
    const units = ccPeriodUnits(ccRunRows(), [], false);
    const members = ccPeriodMembers(units, RUN_IDS);
    set({
      batteryMode: false,
      ids: RUN_IDS,
      refusal: '',
      baseline: members.baseline,
      comparison: members.comparison,
      windows: ccPeriodWindows(units, RUN_IDS),
      ...extra
    });
  }

  function renderBattery(): void {
    const units = ccPeriodUnits([], ccBatteryRunRows(), true);
    const members = ccPeriodMembers(units, BATTERY_IDS);
    set({
      batteryMode: true,
      ids: BATTERY_IDS,
      refusal: '',
      baseline: members.baseline,
      comparison: members.comparison,
      windows: ccPeriodWindows(units, BATTERY_IDS)
    });
  }

  function period(name: 'baseline' | 'comparison'): HTMLElement {
    return el.querySelector(`.cc-ps-period[data-period="${name}"]`) as HTMLElement;
  }

  function bound(key: keyof CcPeriodIds): HTMLButtonElement | null {
    return el.querySelector(`button.cc-ps-bound[data-bound="${key}"]`);
  }

  it('names each period by its heading, with a swatch and an arrow between them', () => {
    renderRuns();
    const sections = Array.from(el.querySelectorAll('section.cc-ps-period'));
    expect(sections.map(section => section.getAttribute('data-period'))).toEqual(['baseline', 'comparison']);
    expect(sections.map(section => section.getAttribute('aria-labelledby'))).toEqual(['cc-ps-baseline-title', 'cc-ps-comparison-title']);
    expect(textOf(el.querySelector('#cc-ps-baseline-title'))).toBe('Baseline');
    expect(textOf(el.querySelector('#cc-ps-comparison-title'))).toBe('Comparison');
    expect(el.querySelector('#cc-ps-baseline-title .cc-ps-swatch')?.getAttribute('aria-hidden')).toBe('true');
    const arrows = el.querySelectorAll('.cc-ps-arrow');
    expect(arrows.length).toBe(1);
    expect(arrows[0].getAttribute('aria-hidden')).toBe('true');
    expect(arrows[0].previousElementSibling).toBe(sections[0]);
  });

  it('shows each range as two links named by their bound, and a click goes to the unit', () => {
    renderRuns();
    expect(Array.from(period('baseline').querySelectorAll('button.cc-ps-bound')).map(b => textOf(b))).toEqual(['#101', '#103']);
    expect(Array.from(period('comparison').querySelectorAll('button.cc-ps-bound')).map(b => textOf(b))).toEqual(['#104', '#106']);
    expect(bound('baselineFirstId')?.getAttribute('aria-label')).toBe('Go to run #101, the baseline\'s first run');
    expect(bound('comparisonLastId')?.getAttribute('aria-label')).toBe('Go to run #106, the comparison\'s last run');
    expect(bound('baselineFirstId')?.type).toBe('button');
    expect(period('baseline').querySelector('.cc-ps-range-to')?.getAttribute('aria-hidden')).toBe('true');

    const emitted: number[] = [];
    component.goToUnit.subscribe(id => emitted.push(id));
    bound('comparisonFirstId')?.click();
    bound('baselineLastId')?.click();
    expect(emitted).toEqual([104, 103]);
  });

  it('shows both bounds when they are the same unit, named for a battery run', () => {
    renderBattery();
    expect(Array.from(period('baseline').querySelectorAll('button.cc-ps-bound')).map(b => textOf(b))).toEqual(['#11', '#11']);
    expect(bound('baselineFirstId')?.getAttribute('aria-label')).toBe('Go to battery run #11, the baseline\'s first run');
    expect(bound('comparisonLastId')?.getAttribute('aria-label')).toBe('Go to battery run #12, the comparison\'s last run');
  });

  it('reads Not set for an unset bound', () => {
    set({
      ids: { ...CC_NO_PERIOD_IDS, baselineFirstId: 101 },
      refusal: 'Choose the first and last run of both periods.'
    });
    expect(bound('baselineFirstId')).not.toBeNull();
    expect(bound('baselineLastId')).toBeNull();
    const unset = Array.from(el.querySelectorAll('.cc-ps-unset'));
    expect(unset.map(span => span.getAttribute('data-bound'))).toEqual(['baselineLastId', 'comparisonFirstId', 'comparisonLastId']);
    expect(unset.map(span => textOf(span))).toEqual(['Not set', 'Not set', 'Not set']);
  });

  it('shows each period\'s count, window and sample while the periods can be analyzed', () => {
    renderRuns();
    expect(textOf(period('baseline').querySelector('.cc-ps-count'))).toBe('3 runs on 3 days · 2026-09-01 to 2026-09-12');
    expect(textOf(period('comparison').querySelector('.cc-ps-count'))).toBe('3 runs on 3 days · 2026-09-20 to 2026-10-01');
    expect(textOf(period('baseline').querySelector('.cc-ps-window'))).toBe('Window 2026-09-01 00:00 to 2026-09-12 23:59 UTC');
    expect(textOf(period('comparison').querySelector('.cc-ps-window'))).toBe('Window 2026-09-20 00:00 to 2026-10-01 23:59 UTC');
    for (const name of ['baseline', 'comparison'] as const) {
      const sample = period(name).querySelector('.cc-ps-sample');
      expect(sample?.classList.contains('is-met')).toBe(true);
      expect(textOf(sample)).toBe('Meets the minimum sample');
      expect(sample?.querySelector('svg')?.getAttribute('aria-hidden')).toBe('true');
      expect(sample?.querySelector('.cc-ps-sample-need')).toBeNull();
    }
  });

  it('formats the window from the bound windows', () => {
    renderRuns({
      windows: {
        baselineStartUtc: '2026-09-01T00:00:00.000Z',
        baselineEndUtc: '2026-09-12T23:59:59.999Z',
        comparisonStartUtc: '2026-09-20T00:00:00.000Z',
        comparisonEndUtc: '2026-10-01T23:59:59.999Z'
      }
    });
    expect(textOf(period('baseline').querySelector('.cc-ps-window'))).toBe('Window 2026-09-01 00:00 to 2026-09-12 23:59 UTC');
    expect(textOf(period('comparison').querySelector('.cc-ps-window'))).toBe('Window 2026-09-20 00:00 to 2026-10-01 23:59 UTC');
  });

  it('marks a battery period below the minimum sample with the icon, the words and what it needs', () => {
    renderBattery();
    expect(textOf(period('baseline').querySelector('.cc-ps-count'))).toBe('1 battery run on 1 day · 2026-10-08');
    expect(textOf(period('comparison').querySelector('.cc-ps-count'))).toBe('1 battery run on 1 day · 2026-10-08');
    // The two battery runs share a day, so the windows split at the comparison's start.
    expect(textOf(period('baseline').querySelector('.cc-ps-window'))).toBe('Window 2026-10-08 00:00 to 2026-10-08 09:59 UTC');
    expect(textOf(period('comparison').querySelector('.cc-ps-window'))).toBe('Window 2026-10-08 10:00 to 2026-10-08 23:59 UTC');
    const sample = period('baseline').querySelector('.cc-ps-sample');
    expect(sample?.classList.contains('is-short')).toBe(true);
    expect(sample?.classList.contains('is-met')).toBe(false);
    expect(sample?.querySelector('svg')?.getAttribute('aria-hidden')).toBe('true');
    expect(textOf(sample)).toContain('Below the minimum sample');
    expect(textOf(sample?.querySelector('.cc-ps-sample-need'))).toBe(ccSampleNeedText());
    expect(textOf(sample?.querySelector('.cc-ps-sample-need'))).toBe('P1, P4 and P5 need at least 2 on 2 days to be Established.');
  });

  it('counts the units outside the periods, singular and plural, and omits the count at 0', () => {
    renderRuns({ notUsedCount: 2 });
    expect(textOf(el.querySelector('.cc-ps-foot .cc-ps-not-used'))).toBe('Not used: 2 runs outside the periods');
    set({ notUsedCount: 1 });
    expect(textOf(el.querySelector('.cc-ps-not-used'))).toBe('Not used: 1 run outside the periods');
    set({ batteryMode: true, notUsedCount: 3 });
    expect(textOf(el.querySelector('.cc-ps-not-used'))).toBe('Not used: 3 battery runs outside the periods');
    set({ notUsedCount: 1 });
    expect(textOf(el.querySelector('.cc-ps-not-used'))).toBe('Not used: 1 battery run outside the periods');
    set({ notUsedCount: 0 });
    expect(el.querySelector('.cc-ps-not-used')).toBeNull();
  });

  it('links to the preview with its note count, or ready', () => {
    renderRuns({ noteCount: 3 });
    const link = () => el.querySelector('.cc-ps-foot button.btn-link.cc-ps-preview-link') as HTMLButtonElement;
    expect(link().type).toBe('button');
    expect(textOf(link())).toBe('Preview: 3 notes');
    set({ noteCount: 1 });
    expect(textOf(link())).toBe('Preview: 1 note');
    set({ noteCount: 0 });
    expect(textOf(link())).toBe('Preview: ready');

    let opened = 0;
    component.openPreview.subscribe(() => opened++);
    link().click();
    expect(opened).toBe(1);
  });

  it('shows the refusal under the foot line, and no count, window or sample while refused', () => {
    renderRuns();
    expect(el.querySelector('.cc-wiz-periods-error')).toBeNull();
    set({
      ids: { ...RUN_IDS, comparisonFirstId: 102 },
      refusal: 'The comparison must start after the baseline\'s last run.',
      baseline: [],
      comparison: [],
      windows: null
    });
    const error = el.querySelector('p.gh-field-error.cc-wiz-periods-error');
    expect(error).not.toBeNull();
    expect(textOf(error)).toBe('The comparison must start after the baseline\'s last run.');
    expect(error?.querySelector('svg')?.getAttribute('aria-hidden')).toBe('true');
    expect(error?.previousElementSibling?.classList.contains('cc-ps-foot')).toBe(true);
    expect(el.querySelector('.cc-ps-count')).toBeNull();
    expect(el.querySelector('.cc-ps-window')).toBeNull();
    expect(el.querySelector('.cc-ps-sample')).toBeNull();
    // The bounds still show.
    expect(Array.from(period('comparison').querySelectorAll('button.cc-ps-bound')).map(b => textOf(b))).toEqual(['#102', '#106']);
  });
});
