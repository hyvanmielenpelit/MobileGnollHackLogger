import type { Mock } from 'vitest';
import { Component, EventEmitter, Input, Output } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { Router, provideRouter } from '@angular/router';
import { provideLocationMocks } from '@angular/common/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { Subject, of, throwError } from 'rxjs';
import { AdminFeedbackCounts, AdminFeedbackService, AdminFeedbackSwitches } from '../../services/admin-feedback.service';
import { InfoTipComponent } from '../../shared/info-tip/info-tip.component';
import { AdminPageStore } from '../admin-page.store';
import { adminTestRoutes } from '../admin.component.testing';
import { AdminFeedbackTabComponent, feedbackRangeDates } from './admin-feedback-tab.component';
import { feedbackDateTimeText } from './overseer/feedback-overseer-panel.component';
import { FeedbackRange, defaultFeedbackRange, saveFeedbackView } from './shared/feedback-view-storage';

const settle = (ms = 0): Promise<void> => new Promise<void>(resolve => setTimeout(resolve, ms));

const textOf = (el: Element | null | undefined): string => (el?.textContent ?? '').replace(/\s+/g, ' ').trim();

const STORAGE_KEYS = ['overseer.admin.feedback.overseer.view', 'overseer.admin.feedback.gnollbench.view'];

function clearStorage(): void {
  for (const key of STORAGE_KEYS) {
    try { localStorage.removeItem(key); } catch { /* nothing stored */ }
  }
}

function counts(overrides: Partial<AdminFeedbackCounts> = {}): AdminFeedbackCounts {
  return { new: 0, acknowledged: 0, resolved: 0, regressed: 0, dismissed: 0, ...overrides };
}

/** The Overseer panel, as the shell sees it: its inputs and its loading output. */
@Component({ selector: 'app-feedback-overseer-panel', template: '' })
class OverseerPanelStub {
  @Input() range: FeedbackRange = defaultFeedbackRange();
  @Input() refreshToken = 0;
  @Output() readonly loadingChange = new EventEmitter<boolean>();
}

/** The GnollBench panel, as the shell sees it. */
@Component({ selector: 'app-feedback-gnollbench-panel', template: '' })
class GnollBenchPanelStub {
  @Input() range: FeedbackRange = defaultFeedbackRange();
  @Input() refreshToken = 0;
}

describe('AdminFeedbackTabComponent', () => {
  let fixture: ComponentFixture<AdminFeedbackTabComponent>;
  let component: AdminFeedbackTabComponent;
  let el: HTMLElement;
  let service: AdminFeedbackService;
  let store: AdminPageStore;
  let router: Router;
  let switchSpy: Mock;
  let countsSpy: Mock;
  let switchState: AdminFeedbackSwitches;

  beforeEach(async () => {
    clearStorage();
    await TestBed.configureTestingModule({
      imports: [AdminFeedbackTabComponent],
      providers: [provideRouter(adminTestRoutes()), provideLocationMocks(), provideHttpClient(), provideHttpClientTesting(), AdminPageStore]
    }).overrideComponent(AdminFeedbackTabComponent, {
      set: { imports: [InfoTipComponent, OverseerPanelStub, GnollBenchPanelStub] }
    }).compileComponents();

    service = TestBed.inject(AdminFeedbackService);
    store = TestBed.inject(AdminPageStore);
    router = TestBed.inject(Router);
    switchState = {
      chat: { enabled: true, setAtUtc: '2026-10-01T09:00:00Z', setByUserName: 'tommi', seeded: false }
    };
    switchSpy = vi.spyOn(service, 'getSwitches').mockImplementation(() => of(switchState));
    countsSpy = vi.spyOn(service, 'getCounts').mockReturnValue(of(counts()));
  });

  afterEach(() => {
    if (fixture) {
      el.querySelectorAll<HTMLElement>('[popover]').forEach(popover => {
        try { popover.hidePopover(); } catch { /* already closed */ }
      });
      fixture.destroy();
    }
    clearStorage();
  });

  async function create(url = '/admin/feedback'): Promise<void> {
    await router.navigateByUrl(url);
    store.applyLocation('feedback', url.startsWith('/admin/feedback/gnollbench') ? 'gnollbench' : 'overseer');
    fixture = TestBed.createComponent(AdminFeedbackTabComponent);
    component = fixture.componentInstance;
    el = fixture.nativeElement as HTMLElement;
    fixture.detectChanges();
  }

  const q = <T extends HTMLElement = HTMLElement>(selector: string): T | null => el.querySelector<T>(selector);
  const overseerPanel = (): OverseerPanelStub | undefined =>
    fixture.debugElement.query(By.directive(OverseerPanelStub))?.componentInstance as OverseerPanelStub | undefined;
  const gnollBenchPanel = (): GnollBenchPanelStub | undefined =>
    fixture.debugElement.query(By.directive(GnollBenchPanelStub))?.componentInstance as GnollBenchPanelStub | undefined;
  const queryParams = () => router.parseUrl(router.url).queryParams;
  const tab = (id: string) => q<HTMLButtonElement>(`#fb-subtab-${id}`)!;

  function selectValue(select: HTMLSelectElement, value: string): void {
    select.value = value;
    select.dispatchEvent(new Event('change', { bubbles: true }));
    fixture.detectChanges();
  }

  function press(target: HTMLElement, key: string): KeyboardEvent {
    const event = new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true });
    target.dispatchEvent(event);
    fixture.detectChanges();
    return event;
  }

  describe('header', () => {
    const rangeSelect = () => q<HTMLSelectElement>('#fb-range-select')!;

    it('is a sticky header titled Feedback, with the feedback tool switch, the date range, its (i) and a Refresh icon button', async () => {
      await create();

      const head = q('.tab-header-row')!;
      expect(textOf(head.querySelector('h3'))).toBe('Feedback');
      expect(getComputedStyle(head).position).toBe('sticky');
      expect(head.closest('.gh-subtab-head')).not.toBeNull();
      expect(head.querySelector('#fb-tool-toggle')).not.toBeNull();
      expect(textOf(q('label[for="fb-range-select"]'))).toBe('Date range');
      expect(rangeSelect().getAttribute('aria-describedby')).toBe('fb-range-tip');
      const rangeButton = rangeSelect().closest('.fb-range')!.querySelector<HTMLButtonElement>('app-info-tip button')!;
      expect(rangeButton.getAttribute('popovertarget')).toBe('fb-range-tip-popup');
      expect(textOf(q('#fb-range-tip'))).toBe(
        'Shows the issues reported at least once in this range. Each sub-tab keeps its own range. The counts on the tab are over all dates.');
      const refresh = q<HTMLButtonElement>('#fb-refresh-btn')!;
      expect(refresh.getAttribute('aria-label')).toBe('Refresh feedback');
      expect(refresh.getAttribute('interestfor')).toBe('fb-refresh-tip');
      expect(textOf(q('#fb-refresh-tip'))).toBe('Refresh');
    });

    it('offers six ranges with All time selected by default, and hands the panel no dates', async () => {
      await create();

      expect(Array.from(rangeSelect().options).map(o => o.textContent?.trim())).toEqual(
        ['All time', 'Last 7 days', 'Last 30 days', 'Last 90 days', 'Last year', 'Custom date range']);
      expect(rangeSelect().value).toBe('all');
      expect(overseerPanel()!.range).toEqual({ preset: 'all', start: '', end: '' });
      expect(q('#fb-range-start')).toBeNull();
    });

    it('starts each sub-tab on the range its remembered view holds', async () => {
      const view = { version: 1 as const, sort: 'lastSeen', filters: {}, search: '' };
      saveFeedbackView('overseer', { ...view, range: { preset: '7d', start: '2026-10-15', end: '2026-10-22' } });
      saveFeedbackView('gnollbench', { ...view, range: { preset: 'custom', start: '2026-09-01', end: '2026-09-30' } });
      await create();

      expect(rangeSelect().value).toBe('7d');
      expect(overseerPanel()!.range).toEqual({ preset: '7d', start: '2026-10-15', end: '2026-10-22' });
      expect(component.ranges.gnollbench).toEqual({ preset: 'custom', start: '2026-09-01', end: '2026-09-30' });
    });

    it('hands the active panel a new range object with both dates for a preset', async () => {
      await create();
      const before = overseerPanel()!.range;

      selectValue(rangeSelect(), '30d');

      const expected = feedbackRangeDates('30d', new Date());
      expect(overseerPanel()!.range).toEqual({ preset: '30d', ...expected });
      expect(overseerPanel()!.range).not.toBe(before);
      expect(component.ranges.gnollbench).toEqual(defaultFeedbackRange());
    });

    it('shows two labeled date inputs for Custom, and moves an end before the start to the start', async () => {
      await create();

      selectValue(rangeSelect(), 'custom');
      const start = q<HTMLInputElement>('#fb-range-start')!;
      const end = q<HTMLInputElement>('#fb-range-end')!;
      expect(textOf(q('label[for="fb-range-start"]'))).toBe('Start date');
      expect(textOf(q('label[for="fb-range-end"]'))).toBe('End date');
      expect(start.value).toBe(feedbackRangeDates('custom', new Date()).start);

      start.value = '2026-10-01';
      start.dispatchEvent(new Event('change'));
      fixture.detectChanges();
      expect(overseerPanel()!.range.start).toBe('2026-10-01');

      end.value = '2026-09-01';
      end.dispatchEvent(new Event('change'));
      fixture.detectChanges();
      expect(overseerPanel()!.range).toEqual(expect.objectContaining({ preset: 'custom', start: '2026-10-01', end: '2026-10-01' }));
    });

    it('turns each preset into local days ending today, All time into no bound', () => {
      const today = new Date(2026, 9, 22, 15, 0);
      expect(feedbackRangeDates('all', today)).toEqual({ start: '', end: '' });
      expect(feedbackRangeDates('7d', today)).toEqual({ start: '2026-10-15', end: '2026-10-22' });
      expect(feedbackRangeDates('30d', today)).toEqual({ start: '2026-09-22', end: '2026-10-22' });
      expect(feedbackRangeDates('90d', today)).toEqual({ start: '2026-07-24', end: '2026-10-22' });
      expect(feedbackRangeDates('1y', today)).toEqual({ start: '2025-10-22', end: '2026-10-22' });
      expect(feedbackRangeDates('custom', today)).toEqual({ start: '2026-09-22', end: '2026-10-22' });
    });

    it('refreshes the switch and the counts, and asks the active panel to refetch', async () => {
      await create();
      switchSpy.mockClear();
      countsSpy.mockClear();
      const loadCounts = vi.spyOn(store, 'loadFeedbackCounts');
      expect(overseerPanel()!.refreshToken).toBe(0);

      q<HTMLButtonElement>('#fb-refresh-btn')!.click();
      fixture.detectChanges();

      expect(switchSpy).toHaveBeenCalledTimes(1);
      expect(loadCounts).toHaveBeenCalledTimes(1);
      expect(countsSpy).toHaveBeenCalledTimes(1);
      expect(overseerPanel()!.refreshToken).toBe(1);
    });

    it('marks Refresh aria-disabled and shows a spinner while the Overseer panel loads', async () => {
      await create();
      const refresh = q<HTMLButtonElement>('#fb-refresh-btn')!;
      expect(refresh.getAttribute('aria-disabled')).toBeNull();

      overseerPanel()!.loadingChange.emit(true);
      fixture.detectChanges();

      expect(refresh.getAttribute('aria-disabled')).toBe('true');
      expect(refresh.querySelector('.gh-spinner-small')).not.toBeNull();
      switchSpy.mockClear();
      refresh.click();
      fixture.detectChanges();
      expect(switchSpy).not.toHaveBeenCalled();
      expect(overseerPanel()!.refreshToken).toBe(0);

      overseerPanel()!.loadingChange.emit(false);
      fixture.detectChanges();
      expect(refresh.getAttribute('aria-disabled')).toBeNull();
    });

    it('sets the sticky clearance from the head\'s height', async () => {
      const callbacks: ResizeObserverCallback[] = [];
      const original = globalThis.ResizeObserver;
      globalThis.ResizeObserver = class {
        constructor(callback: ResizeObserverCallback) { callbacks.push(callback); }
        observe(): void { /* driven by hand */ }
        unobserve(): void { /* driven by hand */ }
        disconnect(): void { /* driven by hand */ }
      } as unknown as typeof ResizeObserver;
      try {
        await create();
        const head = q('.gh-subtab-head')!;
        vi.spyOn(head, 'getBoundingClientRect').mockReturnValue({ height: 63.2 } as DOMRect);

        callbacks.forEach(callback => callback([], {} as ResizeObserver));

        expect(el.style.getPropertyValue('--tl-sticky-clearance')).toBe('72px');
      } finally {
        globalThis.ResizeObserver = original;
      }
    });
  });

  describe('feedback tool switch', () => {
    const toggle = () => q<HTMLInputElement>('#fb-tool-toggle')!;
    const changedText = () => textOf(q('.fb-tool-changed'));
    const word = () => q('.fb-tool .fb-tool-word');
    const statusText = () => textOf(q('#fb-tool-status'));
    const at = '2026-10-21T12:02:00Z';

    function serve(chat: Partial<AdminFeedbackSwitches['chat']>): void {
      switchState = { chat: { ...switchState.chat, ...chat } };
      component.loadSwitches();
      fixture.detectChanges();
    }

    it('sits in the header as one large switch labeled Feedback tool and named for the surface it governs', async () => {
      await create();

      const head = q('.tab-header-row')!;
      const switches = head.querySelectorAll<HTMLInputElement>('input[role="switch"]');
      expect(switches.length).toBe(1);
      expect(switches[0]).toBe(toggle());
      expect(toggle().type).toBe('checkbox');
      expect(toggle().closest('.gh-switch')?.classList.contains('gh-switch-lg')).toBe(true);
      expect(toggle().getAttribute('aria-labelledby')).toBe('fb-tool-label');
      const label = q('#fb-tool-label')!;
      expect(label.getAttribute('for')).toBe('fb-tool-toggle');
      expect(textOf(label)).toBe('Feedback tool in the Overseer chat');
      const visible = label.cloneNode(true) as HTMLElement;
      visible.querySelectorAll('.visually-hidden').forEach(hidden => hidden.remove());
      expect(textOf(visible)).toBe('Feedback tool');
      expect(toggle().getAttribute('aria-describedby')).toBe('fb-tool-tip');
      expect(toggle().checked).toBe(true);
      expect(toggle().disabled).toBe(false);
    });

    it('says in its (i) popup what the tool does, that it is for the Overseer chat only and where GnollBench sets it', async () => {
      await create();

      const button = q('.fb-tool')!.querySelector<HTMLButtonElement>('app-info-tip button')!;
      expect(button.getAttribute('aria-label')).toBe('About the feedback tool');
      expect(button.getAttribute('popovertarget')).toBe('fb-tool-tip-popup');
      const tip = textOf(q('#fb-tool-tip'));
      expect(tip).toContain('Lets the chat assistant report missing information, tool problems and its own mistakes to administrators.');
      expect(tip).toContain('Overseer chat only');
      expect(tip).toContain('GnollBench → Run Benchmark → Feedback Tool');
    });

    it('keeps the status line announced but visually hidden', async () => {
      await create();

      const line = q('#fb-tool-status')!;
      expect(line.getAttribute('role')).toBe('status');
      expect(line.classList.contains('visually-hidden')).toBe(true);
    });

    it('states how the switch was set in its (i), and its state in a word', async () => {
      await create();
      expect(changedText()).toBe(`Changed ${feedbackDateTimeText('2026-10-01T09:00:00Z')} by tommi.`);
      expect(q('#fb-tool-tip')!.contains(q('.fb-tool-changed'))).toBe(true);
      expect(textOf(word())).toBe('On');
      expect(word()!.getAttribute('data-state')).toBe('on');
      expect(word()!.getAttribute('aria-hidden')).toBe('true');

      serve({ enabled: false, setAtUtc: at, setByUserName: 'tommi', seeded: false });
      expect(changedText()).toBe(`Changed ${feedbackDateTimeText(at)} by tommi.`);
      expect(textOf(word())).toBe('Off');
      expect(word()!.getAttribute('data-state')).toBe('off');
      expect(toggle().checked).toBe(false);

      serve({ enabled: true, setAtUtc: at, setByUserName: null, seeded: false });
      expect(changedText()).toBe(`Changed ${feedbackDateTimeText(at)}.`);

      serve({ enabled: false, setAtUtc: null, setByUserName: null, seeded: true });
      expect(changedText()).toBe('Configuration default.');
      expect(textOf(word())).toBe('Off');

      serve({ enabled: true, setAtUtc: null, setByUserName: null, seeded: true });
      expect(changedText()).toBe('Configuration default.');
      expect(textOf(word())).toBe('On');
    });

    it('shows no state word and no changed line until the state arrives, and keeps the switch disabled', async () => {
      const pending = new Subject<AdminFeedbackSwitches>();
      switchSpy.mockReturnValue(pending.asObservable());
      await create();

      expect(word()).toBeNull();
      expect(q('.fb-tool-changed')).toBeNull();
      expect(toggle().disabled).toBe(true);
      expect(textOf(q('.tab-header-row'))).not.toContain('Loading');
    });

    it('commits an Overseer chat click at once, disables the switch while it runs, announces it and refetches', async () => {
      await create();
      const pending = new Subject<AdminFeedbackSwitches>();
      const setSpy = vi.spyOn(service, 'setSwitch').mockReturnValue(pending.asObservable());
      switchSpy.mockClear();

      toggle().click();
      fixture.detectChanges();

      expect(setSpy).toHaveBeenCalledTimes(1);
      expect(setSpy).toHaveBeenCalledWith('chat', false);
      expect(toggle().disabled).toBe(true);
      expect(toggle().getAttribute('aria-busy')).toBe('true');

      switchState = { chat: { enabled: false, setAtUtc: at, setByUserName: 'tommi', seeded: false } };
      pending.next(switchState);
      pending.complete();
      fixture.detectChanges();

      expect(toggle().disabled).toBe(false);
      expect(toggle().checked).toBe(false);
      expect(toggle().getAttribute('aria-busy')).toBeNull();
      expect(statusText()).toBe('Overseer chat: the feedback tool is off.');
      expect(switchSpy).toHaveBeenCalledTimes(1);
      expect(changedText()).toBe(`Changed ${feedbackDateTimeText(at)} by tommi.`);
      expect(textOf(word())).toBe('Off');
      expect(q('#fb-tool-error')).toBeNull();
    });

    it('reverts the switch and shows the error in the header when the request fails', async () => {
      await create();
      vi.spyOn(service, 'setSwitch').mockReturnValue(throwError(() => ({ status: 500, error: { message: 'Database unavailable' } })));

      toggle().click();
      fixture.detectChanges();

      expect(toggle().checked).toBe(true);
      expect(toggle().disabled).toBe(false);
      const errors = el.querySelectorAll('#fb-tool-error.gh-field-error');
      expect(errors.length).toBe(1);
      expect(q('.tab-header-row')!.contains(errors[0])).toBe(true);
      expect(textOf(errors[0])).toBe('The feedback tool could not be turned off for Overseer chat: Database unavailable');
      expect(toggle().getAttribute('aria-describedby')).toBe('fb-tool-tip fb-tool-error');
      expect(statusText()).toBe('');
    });

    it('shows a failed load once, in the header, and leaves the switch disabled', async () => {
      switchSpy.mockReturnValue(throwError(() => ({ status: 500, error: { message: 'Database unavailable' } })));
      await create();

      const errors = el.querySelectorAll('#fb-tool-load-error');
      expect(errors.length).toBe(1);
      expect(q('.tab-header-row')!.contains(errors[0])).toBe(true);
      expect(textOf(errors[0])).toBe('The state of the feedback tool could not be loaded: Database unavailable');
      expect(q('#fb-tool-error')).toBeNull();
      expect(toggle().disabled).toBe(true);
      expect(q('.fb-tool-changed')).toBeNull();
    });
  });

  describe('sub-tabs', () => {
    it('is a text-only wrapping secondary tab row of Overseer and GnollBench in the sticky head, Overseer selected', async () => {
      await create();

      const row = q('.gh-subtab-head > [role="tablist"]')!;
      expect(row.classList).toContain('gh-tabs');
      expect(row.classList).toContain('gh-tabs-secondary');
      expect(row.classList).toContain('gh-tabs-wrap');
      expect(row.getAttribute('aria-label')).toBe('Feedback sources');
      const tabs = Array.from(row.querySelectorAll<HTMLButtonElement>('[role="tab"]'));
      expect(tabs.map(t => textOf(t))).toEqual(['Overseer', 'GnollBench']);
      expect(tabs.map(t => t.id)).toEqual(['fb-subtab-overseer', 'fb-subtab-gnollbench']);
      expect(tabs.map(t => t.getAttribute('aria-controls'))).toEqual(['fb-subpanel-overseer', 'fb-subpanel-gnollbench']);
      expect(tabs.map(t => t.getAttribute('aria-selected'))).toEqual(['true', 'false']);
      expect(tabs.map(t => t.getAttribute('tabindex'))).toEqual(['0', '-1']);
      expect(tabs.every(t => t.type === 'button' && t.querySelector('svg') === null)).toBe(true);
    });

    it('mounts only the active sub-tab\'s panel, in a tabpanel labeled by its tab', async () => {
      await create();

      const panel = q('#fb-subpanel-overseer')!;
      expect(panel.getAttribute('role')).toBe('tabpanel');
      expect(panel.getAttribute('aria-labelledby')).toBe('fb-subtab-overseer');
      expect(panel.getAttribute('tabindex')).toBe('0');
      expect(overseerPanel()).toBeDefined();
      expect(gnollBenchPanel()).toBeUndefined();
      expect(q('#fb-subpanel-gnollbench')).toBeNull();
    });

    it('opens the GnollBench sub-tab on a click: a new history entry without the Feedback filters, every other parameter kept', async () => {
      await create('/admin/feedback?status=resolved&model=gpt-x&q=crystal&sessionId=s1');
      const navigate = vi.spyOn(router, 'navigate');

      tab('gnollbench').click();
      await settle();
      fixture.detectChanges();

      expect(navigate).toHaveBeenCalledWith(['/admin', 'feedback', 'gnollbench'],
        expect.objectContaining({ queryParamsHandling: 'merge', replaceUrl: false }));
      expect(router.url).toBe('/admin/feedback/gnollbench?sessionId=s1');
      expect(queryParams()).toEqual({ sessionId: 's1' });
      expect(store.feedbackSubTab).toBe('gnollbench');
      expect(tab('gnollbench').getAttribute('aria-selected')).toBe('true');
      expect(tab('overseer').getAttribute('tabindex')).toBe('-1');
      expect(overseerPanel()).toBeUndefined();
      expect(gnollBenchPanel()).toBeDefined();
      expect(q('#fb-subpanel-gnollbench')!.getAttribute('aria-labelledby')).toBe('fb-subtab-gnollbench');
    });

    it('shows the range of the sub-tab it shows, and hands each panel its own', async () => {
      const view = { version: 1 as const, sort: 'lastSeen', filters: {}, search: '' };
      saveFeedbackView('gnollbench', { ...view, range: { preset: '90d', start: '2026-07-24', end: '2026-10-22' } });
      await create();
      expect(q<HTMLSelectElement>('#fb-range-select')!.value).toBe('all');

      tab('gnollbench').click();
      await settle();
      fixture.detectChanges();

      expect(q<HTMLSelectElement>('#fb-range-select')!.value).toBe('90d');
      expect(gnollBenchPanel()!.range).toEqual({ preset: '90d', start: '2026-07-24', end: '2026-10-22' });
    });

    it('moves with the arrow keys, Home and End, replacing the history entry and focusing the tab reached', async () => {
      await create();
      const navigate = vi.spyOn(router, 'navigate');

      const right = press(tab('overseer'), 'ArrowRight');
      expect(right.defaultPrevented).toBe(true);
      expect(document.activeElement).toBe(tab('gnollbench'));
      await settle();
      fixture.detectChanges();
      expect(navigate).toHaveBeenLastCalledWith(['/admin', 'feedback', 'gnollbench'], expect.objectContaining({ replaceUrl: true }));
      expect(store.feedbackSubTab).toBe('gnollbench');

      press(tab('gnollbench'), 'ArrowRight');
      expect(document.activeElement).toBe(tab('overseer'));
      await settle();
      fixture.detectChanges();
      expect(router.url).toBe('/admin/feedback');
      expect(store.feedbackSubTab).toBe('overseer');

      press(tab('overseer'), 'End');
      await settle();
      fixture.detectChanges();
      expect(store.feedbackSubTab).toBe('gnollbench');

      press(tab('gnollbench'), 'Home');
      await settle();
      fixture.detectChanges();
      expect(store.feedbackSubTab).toBe('overseer');
      expect(document.activeElement).toBe(tab('overseer'));

      navigate.mockClear();
      const other = press(tab('overseer'), 'ArrowDown');
      expect(other.defaultPrevented).toBe(false);
      expect(navigate).not.toHaveBeenCalled();
    });

    it('leaves the address alone for a click on the selected tab', async () => {
      await create();
      const navigate = vi.spyOn(router, 'navigate');

      tab('overseer').click();
      await settle();

      expect(navigate).not.toHaveBeenCalled();
    });

    it('follows the page store when Back or Forward changes the sub-tab', async () => {
      await create();

      store.applyLocation('feedback', 'gnollbench');
      fixture.detectChanges();

      expect(tab('gnollbench').getAttribute('aria-selected')).toBe('true');
      expect(gnollBenchPanel()).toBeDefined();
      expect(overseerPanel()).toBeUndefined();
    });
  });
});
