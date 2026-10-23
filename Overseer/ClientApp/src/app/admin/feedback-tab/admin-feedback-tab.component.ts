import {
  AfterViewInit,
  ChangeDetectionStrategy,
  ChangeDetectorRef,
  Component,
  ElementRef,
  OnDestroy,
  OnInit,
  ViewChild,
  inject,
  signal
} from '@angular/core';
import { Router } from '@angular/router';
import { Subscription } from 'rxjs';
import {
  AdminFeedbackService,
  AdminFeedbackSwitch,
  AdminFeedbackSwitchTarget,
  AdminFeedbackSwitches
} from '../../services/admin-feedback.service';
import { adminApiError } from '../../services/admin-users.service';
import { InfoTipComponent } from '../../shared/info-tip/info-tip.component';
import { ensureOverlayPolyfills } from '../../utils/polyfills.util';
import { rovingTabIndex } from '../../utils/roving-tab.util';
import { AdminHistoryMode, AdminPageStore, FeedbackSubTab, FeedbackTimeSpan } from '../admin-page.store';
import { adminCommands, clearedFeedbackQueryParams } from '../admin-url';
import { FeedbackGnollBenchPanelComponent } from './gnollbench/feedback-gnollbench-panel.component';
import { FeedbackOverseerPanelComponent, feedbackDateTimeText } from './overseer/feedback-overseer-panel.component';
import { FeedbackRange, FeedbackSource, defaultFeedbackRange, loadFeedbackView } from './shared/feedback-view-storage';

/** The Date range select's options, in order. */
export const FEEDBACK_TIME_SPAN_LABELS: Readonly<Record<FeedbackTimeSpan, string>> = {
  all: 'All time',
  '7d': 'Last 7 days',
  '30d': 'Last 30 days',
  '90d': 'Last 90 days',
  '1y': 'Last year',
  custom: 'Custom date range'
};

const TIME_SPANS = Object.keys(FEEDBACK_TIME_SPAN_LABELS) as FeedbackTimeSpan[];

/** The sub-tab row's labels. */
export const FEEDBACK_SUBTAB_LABELS: Readonly<Record<FeedbackSubTab, string>> = {
  overseer: 'Overseer',
  gnollbench: 'GnollBench'
};

/** The surface the switch governs, as its status and error messages name it. */
const FEEDBACK_SWITCH_NAME = 'Overseer chat';

/** A local day as "YYYY-MM-DD". */
function localDay(d: Date): string {
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
}

/**
 * A preset's dates, "YYYY-MM-DD", ending `today`: both empty for All time, which bounds nothing;
 * the last 30 days for Custom, as its starting point.
 */
export function feedbackRangeDates(span: FeedbackTimeSpan, today: Date): { start: string; end: string } {
  if (span === 'all') return { start: '', end: '' };
  const start = new Date(today);
  if (span === '7d') start.setDate(today.getDate() - 7);
  else if (span === '90d') start.setDate(today.getDate() - 90);
  else if (span === '1y') start.setFullYear(today.getFullYear() - 1);
  else start.setDate(today.getDate() - 30);
  return { start: localDay(start), end: localDay(today) };
}

/**
 * The Admin page's Feedback tab: a sticky head with the title, the switch that offers the assistant's
 * `report_feedback` tool in the Overseer chat (GnollBench runs choose it per run in the launcher), the
 * active sub-tab's date range and Refresh, and the sub-tab row (*Overseer*, *GnollBench*), whose
 * selection is a place in the address. Only the active sub-tab's panel is mounted; each keeps its own
 * range, which the shell holds and the panel remembers with the rest of its view. The counts per status,
 * which the Admin tab row shows, come from the page store.
 */
@Component({
  selector: 'app-admin-feedback-tab',
  changeDetection: ChangeDetectionStrategy.Eager,   // matches every other component here
  imports: [InfoTipComponent, FeedbackOverseerPanelComponent, FeedbackGnollBenchPanelComponent],
  templateUrl: './admin-feedback-tab.component.html',
  styleUrl: './admin-feedback-tab.component.scss'
})
export class AdminFeedbackTabComponent implements OnInit, AfterViewInit, OnDestroy {
  private readonly feedback = inject(AdminFeedbackService);
  private readonly store = inject(AdminPageStore);
  private readonly router = inject(Router);
  private readonly host: ElementRef<HTMLElement> = inject(ElementRef);
  private readonly cdr = inject(ChangeDetectorRef);

  @ViewChild('fbHead') private fbHead?: ElementRef<HTMLElement>;

  readonly subTabs: readonly FeedbackSubTab[] = ['overseer', 'gnollbench'];
  readonly subTabLabels = FEEDBACK_SUBTAB_LABELS;
  readonly timeSpans = TIME_SPANS;
  readonly timeSpanLabels = FEEDBACK_TIME_SPAN_LABELS;

  /** Today as "YYYY-MM-DD", the custom range's last selectable day. */
  todayDate = '';

  /** Each sub-tab's date range, from its remembered view; the active one is shown and handed to its panel. */
  ranges: Record<FeedbackSource, FeedbackRange> = { overseer: defaultFeedbackRange(), gnollbench: defaultFeedbackRange() };

  /** Bumped by Refresh; the active panel refetches when it changes. */
  refreshToken = 0;

  /** Whether the Overseer panel is loading a page, which Refresh waits for. */
  readonly panelLoading = signal(false);

  // --- Switch ---

  readonly switches = signal<AdminFeedbackSwitches | null>(null);
  /** Per switch: its request is running, its last result, its last error. */
  readonly switchPending = { chat: signal(false) };
  readonly switchMessage = { chat: signal('') };
  readonly switchError = { chat: signal<string | null>(null) };
  readonly switchLoadError = signal<string | null>(null);

  private switchSub: Subscription | null = null;
  private headObserver?: ResizeObserver;

  /** The selected sub-tab lives in the page store, which follows the address. */
  get subTab(): FeedbackSubTab {
    return this.store.feedbackSubTab;
  }

  /** The active sub-tab's date range. */
  get range(): FeedbackRange {
    return this.ranges[this.subTab];
  }

  /** Whether Refresh waits for a load of the active panel. */
  get refreshBusy(): boolean {
    return this.subTab === 'overseer' && this.panelLoading();
  }

  ngOnInit(): void {
    ensureOverlayPolyfills();
    this.todayDate = localDay(new Date());
    this.ranges = {
      overseer: loadFeedbackView('overseer')?.range ?? defaultFeedbackRange(),
      gnollbench: loadFeedbackView('gnollbench')?.range ?? defaultFeedbackRange()
    };
    this.loadSwitches();
  }

  /** Focused or scrolled-to elements stop below the sticky head: its height, plus a gap, as `--tl-sticky-clearance`. */
  ngAfterViewInit(): void {
    const head = this.fbHead?.nativeElement;
    if (!head || typeof ResizeObserver === 'undefined') {
      return;
    }
    this.headObserver = new ResizeObserver(() => {
      const height = head.getBoundingClientRect().height;
      this.host.nativeElement.style.setProperty('--tl-sticky-clearance', `${Math.ceil(height) + 8}px`);
    });
    this.headObserver.observe(head);
  }

  ngOnDestroy(): void {
    this.headObserver?.disconnect();
    this.switchSub?.unsubscribe();
  }

  /** Header Refresh: the switch, the counts, and everything the active panel shows; refused while the panel loads. */
  refresh(): void {
    if (this.refreshBusy) return;
    this.loadSwitches();
    this.store.loadFeedbackCounts();
    this.refreshToken++;
  }

  onPanelLoading(loading: boolean): void {
    this.panelLoading.set(loading);
  }

  // --- Sub-tabs ---

  /**
   * Moves the address to a sub-tab, removing every Feedback filter parameter and keeping the others,
   * so the entering panel applies its own remembered view. The store takes the sub-tab once the address
   * has arrived, so the entering panel never reads the leaving one's filters.
   */
  selectSubTab(tab: FeedbackSubTab, mode: AdminHistoryMode = 'push'): void {
    if (tab === this.subTab) {
      return;
    }
    // A navigation the router fails is reported by its own NavigationError event.
    this.router.navigate(adminCommands('feedback', tab), {
      queryParams: clearedFeedbackQueryParams(),
      queryParamsHandling: 'merge',
      replaceUrl: mode === 'replace'
    }).then(navigated => {
      if (navigated === false) return;
      this.store.applyLocation('feedback', tab);
      this.panelLoading.set(false);
      this.cdr.markForCheck();
    }).catch(() => false);
  }

  /** Arrow keys, Home and End move between the sub-tabs, select the one reached and focus it. */
  onSubTabKeydown(event: KeyboardEvent, index: number): void {
    const next = rovingTabIndex(event, index, this.subTabs.length);
    if (next === null) {
      return;
    }
    const tab = this.subTabs[next];
    this.selectSubTab(tab, 'replace');
    this.host.nativeElement.querySelector<HTMLElement>(`#fb-subtab-${tab}`)?.focus();
  }

  // --- Date range ---

  /** A preset sets both dates; Custom keeps the dates it finds, or starts from the last 30 days. */
  onTimeSpanChange(event: Event): void {
    const value = (event.target as HTMLSelectElement).value as FeedbackTimeSpan;
    if (!TIME_SPANS.includes(value)) return;
    const range = this.range;
    const keep = value === 'custom' && !!range.start && !!range.end;
    const dates = keep ? { start: range.start, end: range.end } : feedbackRangeDates(value, new Date());
    this.setRange({ preset: value, ...dates });
  }

  /** A custom date; an end before the start is moved to the start. */
  onCustomDateChange(which: 'start' | 'end', event: Event): void {
    const next: FeedbackRange = { ...this.range, [which]: (event.target as HTMLInputElement).value };
    if (next.start && next.end && next.end < next.start) {
      next.end = next.start;
    }
    this.setRange(next);
  }

  /** A new object, so the panel sees the change; the panel remembers it with its view. */
  private setRange(range: FeedbackRange): void {
    this.ranges = { ...this.ranges, [this.subTab]: range };
    this.cdr.markForCheck();
  }

  // --- Switch ---

  /** The switch's state as the server last sent it; null until loaded. */
  switchOf(target: AdminFeedbackSwitchTarget): AdminFeedbackSwitch | null {
    return this.switches()?.[target] ?? null;
  }

  /** `Changed 21 Oct 2026 22:54 by tommi.`, `Configuration default.` while seeded; empty while unknown. */
  switchChangedText(target: AdminFeedbackSwitchTarget): string {
    const state = this.switchOf(target);
    if (!state || this.switchLoadError()) return '';
    if (state.seeded) return 'Configuration default.';
    if (!state.setAtUtc) return '';
    const by = state.setByUserName ? ` by ${state.setByUserName}` : '';
    return `Changed ${feedbackDateTimeText(state.setAtUtc)}${by}.`;
  }

  loadSwitches(): void {
    this.switchSub?.unsubscribe();
    this.switchSub = this.feedback.getSwitches().subscribe({
      next: switches => {
        this.switches.set(switches);
        this.switchLoadError.set(null);
      },
      error: err => {
        this.switchLoadError.set('The state of the feedback tool could not be loaded: ' + adminApiError(err).message);
      }
    });
  }

  /**
   * Commits one switch at once; that switch alone is disabled while its request runs and reverts when
   * it fails. A success refetches it.
   */
  onSwitchChange(target: AdminFeedbackSwitchTarget, event: Event): void {
    const checkbox = event.target as HTMLInputElement;
    const enabled = checkbox.checked;
    const pending = this.switchPending[target];
    if (pending() || !this.switchOf(target)) {
      checkbox.checked = !enabled;
      return;
    }
    const name = FEEDBACK_SWITCH_NAME;
    pending.set(true);
    this.switchMessage[target].set('');
    this.switchError[target].set(null);
    this.feedback.setSwitch(target, enabled).subscribe({
      next: () => {
        pending.set(false);
        this.switchMessage[target].set(`${name}: the feedback tool is ${enabled ? 'on' : 'off'}.`);
        this.loadSwitches();
      },
      error: err => {
        pending.set(false);
        checkbox.checked = !enabled;
        this.switchError[target].set(
          `The feedback tool could not be turned ${enabled ? 'on' : 'off'} for ${name}: ${adminApiError(err).message}`);
      }
    });
  }
}
