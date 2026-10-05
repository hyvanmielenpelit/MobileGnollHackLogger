import { ChangeDetectorRef, Component, inject } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { provideCharts } from 'ng2-charts';
import { ModelComparisonComponent } from './model-comparison.component';
import { APP_CHART_REGISTRABLES } from '../../../chart-registrables';
import {
  BenchmarkComparabilityIndexDto,
  BenchmarkComparabilityIndexEntryDto,
  BenchmarkModelComparisonCostDto,
  BenchmarkModelComparisonDto,
  BenchmarkModelComparisonEntryDto,
  ComparisonSelectedSource,
  ComparisonSelectionNotice,
  ComparisonSelectionState,
  selectionNotices,
  toChartContext,
  toChartEntries
} from './model-comparison.models';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { BehaviorSubject } from 'rxjs';
import { SystemAlert } from '../../../services/admin-alert.service';
import {
  buildExcludedEntry, buildDto, render, refresh, comparableSet, textOf, nextButton, cancelCompareButton,
  setUpModelComparisonSpec
} from './model-comparison.component.testing';

describe('ModelComparisonComponent', () => {
  let component: ModelComparisonComponent;
  let fixture: ComponentFixture<ModelComparisonComponent>;
  /** The system alerts the wizard reads chart storage from. */
  let alerts: BehaviorSubject<SystemAlert[]>;

  setUpModelComparisonSpec({
    get component() { return component; },
    set component(value) { component = value; },
    get fixture() { return fixture; },
    set fixture(value) { fixture = value; },
    get alerts() { return alerts; },
    set alerts(value) { alerts = value; }
  }, { stubReportPackPanel: false });

  // -------------------------------------------------------------------------------------------
  // The step-1 notice band
  // -------------------------------------------------------------------------------------------

  const crossCondition: ComparisonSelectionNotice = {
    id: 'cross-condition',
    severity: 'warning',
    heading: 'Part of this selection will be excluded',
    body: '2 of 3 selected sources fall outside Condition A and will be excluded.'
  };

  const indexFailure: ComparisonSelectionNotice = {
    id: 'index-error',
    severity: 'error',
    heading: 'Conditions could not be computed',
    body: 'The index could not be built.'
  };

  const stillComputing: ComparisonSelectionNotice = {
    id: 'index-loading',
    severity: 'info',
    heading: 'Conditions are still being computed',
    body: 'The Condition column stays muted until the index lands.'
  };

  /** `n` run sources, for tests that only care about the chip count and the band label. */
  function runSources(count: number): ComparisonSelectedSource[] {
    return Array.from({ length: count }, (_unused, index) => ({
      kind: 'run',
      id: index + 1,
      label: `Model ${index + 1}`,
      provider: 'Google',
      detail: `#${index + 1}`
    }));
  }

  /** `n` group sources, for the same reason. */
  function groupSources(count: number): ComparisonSelectedSource[] {
    return Array.from({ length: count }, (_unused, index) => ({
      kind: 'group',
      id: index + 1,
      label: `Group ${index + 1}`,
      provider: null,
      detail: '3 runs'
    }));
  }

  /** Step 1, with counts, an optional chip list and a notice set in force. */
  function band(counts: {
    runs?: number;
    groups?: number;
    sources?: readonly ComparisonSelectedSource[];
    notices?: readonly ComparisonSelectionNotice[];
  } = {}): void {
    render(null, 1);
    fixture.componentRef.setInput('selectedRunCount', counts.runs ?? 0);
    fixture.componentRef.setInput('selectedGroupCount', counts.groups ?? 0);
    if (counts.sources) {
      fixture.componentRef.setInput('selectedSources', counts.sources);
    }
    fixture.componentRef.setInput('selectionNotices', counts.notices ?? [crossCondition]);
    fixture.detectChanges();
  }

  function bandAlerts(): HTMLElement[] {
    return fixture.debugElement.queryAll(By.css('.mc-wizard-notice .alert'))
      .map(element => element.nativeElement as HTMLElement);
  }

  it('sums the two selection counts', () => {
    band({ runs: 3, groups: 4 });

    expect(component.selectedSourceCount).toBe(7);
  });

  it('states the run count in the band label when only runs are selected', () => {
    band({ runs: 3, sources: runSources(3) });

    const label = textOf('.mc-wizard-notice-label');
    expect(label).toContain('3 runs selected');
  });

  it('states the group count in the band label when only groups are selected', () => {
    band({ groups: 2, sources: groupSources(2) });

    const label = textOf('.mc-wizard-notice-label');
    expect(label).toContain('2 groups selected');
  });

  it('states both counts when the selection spans runs and groups', () => {
    band({ runs: 2, groups: 1, sources: [...runSources(2), ...groupSources(1)] });

    const label = textOf('.mc-wizard-notice-label');
    expect(label).toContain('2 runs and 1 group selected');
  });

  it('pluralises the selection summary across all four forms', () => {
    fixture.componentRef.setInput('selectedRunCount', 0);
    fixture.componentRef.setInput('selectedGroupCount', 0);
    expect(component.selectionSummary).toBe('nothing selected yet');

    fixture.componentRef.setInput('selectedRunCount', 1);
    expect(component.selectionSummary).toBe('1 run selected');

    fixture.componentRef.setInput('selectedRunCount', 2);
    expect(component.selectionSummary).toBe('2 runs selected');

    fixture.componentRef.setInput('selectedRunCount', 0);
    fixture.componentRef.setInput('selectedGroupCount', 1);
    expect(component.selectionSummary).toBe('1 group selected');

    fixture.componentRef.setInput('selectedGroupCount', 3);
    expect(component.selectionSummary).toBe('3 groups selected');

    fixture.componentRef.setInput('selectedRunCount', 1);
    expect(component.selectionSummary).toBe('1 run and 3 groups selected');

    fixture.componentRef.setInput('selectedGroupCount', 2);
    expect(component.selectionSummary).toBe('1 run and 2 groups selected');

    fixture.componentRef.setInput('selectedRunCount', 2);
    fixture.componentRef.setInput('selectedGroupCount', 1);
    expect(component.selectionSummary).toBe('2 runs and 1 group selected');
  });

  it('labels the band for assistive technology', () => {
    band({ runs: 2, sources: runSources(2) });

    const strip = fixture.debugElement.query(By.css('.mc-wizard-notice')).nativeElement as HTMLElement;
    expect(strip.getAttribute('role')).toBe('region');
    const labelId = strip.getAttribute('aria-labelledby')!;
    expect((fixture.debugElement.query(By.css('.mc-wizard-notice-label'))
      .nativeElement as HTMLElement).id).toBe(labelId);
  });

  it('renders one chip per selected source, the provider badge on a run chip and not on a group chip', () => {
    const sources: ComparisonSelectedSource[] = [
      { kind: 'run', id: 1, label: 'Gemini 2.5 Flash', provider: 'Google', detail: '#1' },
      { kind: 'group', id: 3, label: 'Nightly regression', provider: null, detail: '4 runs' }
    ];
    band({ runs: 1, groups: 1, sources, notices: [] });

    const chips = fixture.debugElement.queryAll(By.css('.mc-selection-chip'));
    expect(chips.length).toBe(2);
    expect(chips[0].nativeElement.textContent).toContain('#1');
    expect(chips[0].nativeElement.textContent).toContain('Gemini 2.5 Flash');
    expect(chips[0].query(By.css('app-provider-badge'))).toBeTruthy();
    expect(chips[0].nativeElement.classList).not.toContain('mc-selection-chip--group');

    expect(chips[1].nativeElement.textContent).toContain('4 runs');
    expect(chips[1].nativeElement.textContent).toContain('Nightly regression');
    expect(chips[1].query(By.css('app-provider-badge'))).toBeNull();
    expect(chips[1].nativeElement.classList).toContain('mc-selection-chip--group');
  });

  it('emits the chip through removeSource when its remove button is clicked', () => {
    const source: ComparisonSelectedSource =
      { kind: 'run', id: 1, label: 'Gemini 2.5 Flash', provider: 'Google', detail: '#1' };
    band({ runs: 1, sources: [source], notices: [] });

    const removed: ComparisonSelectedSource[] = [];
    component.removeSource.subscribe(s => removed.push(s));

    (fixture.debugElement.query(By.css('.mc-selection-remove')).nativeElement as HTMLButtonElement).click();

    expect(removed).toEqual([source]);
  });

  it('renders on step 1 with nothing selected, its summary visually hidden and no hint', () => {
    band({ notices: [] });

    expect(fixture.debugElement.query(By.css('.mc-wizard-notice'))).toBeTruthy();
    // Still read to a screen reader, but the alert is the one visible message.
    expect(textOf('.mc-wizard-notice-label')).toContain('Your selection — nothing selected yet');
    const summary = fixture.debugElement.query(By.css('#mc-selection-label > span'))
      .nativeElement as HTMLElement;
    expect(summary.classList).toContain('visually-hidden');
    expect(summary.getBoundingClientRect().width).toBeLessThanOrEqual(1);
    expect(fixture.debugElement.query(By.css('.mc-wizard-selection-hint'))).toBeNull();
    expect(textOf('.mc-wizard-selection')).not.toContain('Select runs or groups above');
    expect(fixture.debugElement.query(By.css('.mc-selection-chips'))).toBeNull();
  });

  it('shows the summary text once something is selected', () => {
    band({ runs: 2, sources: runSources(2), notices: [] });

    const summary = fixture.debugElement.query(By.css('#mc-selection-label > span'))
      .nativeElement as HTMLElement;
    expect(summary.classList).not.toContain('visually-hidden');
  });

  it('offers Clear selection only when something is selected, first in the chip row, and emits clearSelection', () => {
    band({ notices: [] });
    expect(fixture.debugElement.query(By.css('.mc-selection-clear'))).toBeNull();

    band({ runs: 2, sources: runSources(2), notices: [] });

    const cleared: void[] = [];
    component.clearSelection.subscribe(() => cleared.push(undefined));

    const button = fixture.debugElement.query(By.css('.mc-selection-clear'))
      .nativeElement as HTMLButtonElement;
    expect(button.textContent?.trim()).toBe('Clear selection (2)');
    expect(button.classList).toContain('btn-gh');
    expect(button.classList).toContain('btn-gh-cancel');
    expect(button.classList).toContain('btn-gh-small');
    expect(button.classList).not.toContain('btn-ghost');
    // Before the chips, so it is reached first by keyboard.
    const chips = fixture.debugElement.query(By.css('.mc-selection-chips')).nativeElement as HTMLElement;
    expect(button.compareDocumentPosition(chips) & Node.DOCUMENT_POSITION_FOLLOWING).not.toBe(0);
    button.click();

    expect(cleared.length).toBe(1);
  });

  it('sizes the chip remove buttons as 32 by 32 action buttons', () => {
    band({ runs: 1, sources: runSources(1), notices: [] });

    const remove = fixture.debugElement.query(By.css('.mc-selection-remove')).nativeElement as HTMLElement;
    expect(remove.classList).toContain('action-btn');
    const style = getComputedStyle(remove);
    expect(style.width).toBe('32px');
    expect(style.height).toBe('32px');
  });

  describe('focus after a removal', () => {
    /** As the host does: drops what was removed and checks the view before the emit returns. */
    function followHost(sources: ComparisonSelectedSource[]): void {
      let current = [...sources];
      const apply = (): void => {
        fixture.componentRef.setInput('selectedRunCount', current.length);
        fixture.componentRef.setInput('selectedSources', current);
        fixture.detectChanges();
      };
      component.removeSource.subscribe(removed => {
        current = current.filter(source => source !== removed);
        apply();
      });
      component.clearSelection.subscribe(() => {
        current = [];
        apply();
      });
    }

    function removeButtons(): HTMLButtonElement[] {
      return fixture.debugElement.queryAll(By.css('.mc-selection-remove'))
        .map(element => element.nativeElement as HTMLButtonElement);
    }

    function label(): HTMLElement {
      return fixture.debugElement.query(By.css('#mc-selection-label')).nativeElement as HTMLElement;
    }

    it('moves focus to the band label after Clear selection, which is a programmatic target only', () => {
      const sources = runSources(2);
      band({ runs: 2, sources, notices: [] });
      followHost(sources);

      const button = fixture.debugElement.query(By.css('.mc-selection-clear'))
        .nativeElement as HTMLButtonElement;
      button.focus();
      button.click();

      expect(fixture.debugElement.query(By.css('.mc-selection-clear'))).toBeNull();
      expect(label().getAttribute('tabindex')).toBe('-1');
      expect(document.activeElement).toBe(label());
    });

    it('moves focus to the next chip, then to Clear selection, then to the label', () => {
      const sources = runSources(3);
      band({ runs: 3, sources, notices: [] });
      followHost(sources);

      // The first chip: the next chip's remove button.
      const [first, second] = removeButtons();
      first.focus();
      first.click();
      expect(removeButtons().length).toBe(2);
      expect(document.activeElement).toBe(second);

      // The last of two: Clear selection, which stays while a chip remains.
      const last = removeButtons()[1];
      last.focus();
      last.click();
      expect(removeButtons().length).toBe(1);
      expect(document.activeElement)
        .toBe(fixture.debugElement.query(By.css('.mc-selection-clear')).nativeElement);

      // The only chip: the label, since Clear selection goes with it.
      const only = removeButtons()[0];
      only.focus();
      only.click();
      expect(removeButtons().length).toBe(0);
      expect(fixture.debugElement.query(By.css('.mc-selection-clear'))).toBeNull();
      expect(document.activeElement).toBe(label());
    });
  });

  it('keeps the notices below the chip row when the selection carries both', () => {
    const source: ComparisonSelectedSource =
      { kind: 'run', id: 1, label: 'Gemini 2.5 Flash', provider: 'Google', detail: '#1' };
    band({ runs: 1, sources: [source], notices: [crossCondition] });

    const head = fixture.debugElement.query(By.css('.mc-selection-head')).nativeElement as HTMLElement;
    const notices = fixture.debugElement.query(By.css('.mc-wizard-notices')).nativeElement as HTMLElement;
    expect(head.nextElementSibling).toBe(notices);
  });

  it('announces through one polite live region around the notices, apart from the summary status', () => {
    band({ runs: 3, notices: [indexFailure, crossCondition, stillComputing] });

    const strip = fixture.debugElement.query(By.css('.mc-wizard-notice')).nativeElement as HTMLElement;
    expect(strip.hasAttribute('aria-live')).toBe(false);

    const list = fixture.debugElement.query(By.css('.mc-wizard-notices')).nativeElement as HTMLElement;
    expect(list.getAttribute('aria-live')).toBe('polite');
    // Only the notice that appeared is announced, rather than the whole list again.
    expect(list.getAttribute('aria-atomic')).toBe('false');

    // The summary line carries the band's one role="status"; a role nested inside the aria-live
    // region above would double-announce in several screen readers, so no notice carries one.
    const statuses = fixture.debugElement.queryAll(By.css('.mc-wizard-notice [role="status"]'));
    expect(statuses.length).toBe(1);
    expect((statuses[0].nativeElement as HTMLElement).id).toBe('mc-selection-label');
    expect(bandAlerts().map(alert => alert.getAttribute('role'))).toEqual([null, null, null]);
  });

  it('renders one alert per notice, each with its own variant, glyph and heading', () => {
    band({ runs: 3, notices: [indexFailure, crossCondition, stillComputing] });

    const alerts = bandAlerts();
    expect(alerts.length).toBe(3);
    expect(alerts[0].classList).toContain('alert-danger');
    expect(alerts[1].classList).toContain('alert-warning');
    expect(alerts[2].classList).toContain('alert-info');

    expect(alerts[0].querySelector('.alert-heading')?.textContent)
      .toContain('Conditions could not be computed');
    expect(alerts[1].querySelector('.alert-body')?.textContent).toContain('fall outside Condition A');

    // Severity is carried by shape as well as by hue: three distinct glyphs, none of them decorative
    // to a screen reader.
    const glyphs = alerts.map(alert => alert.querySelector('svg.alert-icon'));
    expect(glyphs.every(glyph => glyph?.getAttribute('aria-hidden') === 'true')).toBe(true);
    expect(new Set(glyphs.map(glyph => glyph?.innerHTML)).size).toBe(3);
  });

  it('stacks errors above warnings above information, whatever order they arrive in', () => {
    band({ runs: 3, notices: [stillComputing, crossCondition, indexFailure] });

    expect(component.bandNotices.map(notice => notice.id))
      .toEqual(['index-error', 'cross-condition', 'index-loading']);
    expect(bandAlerts().map(alert => alert.className.includes('alert-danger')))
      .toEqual([true, false, false]);
  });

  it('bands the notices above the footer on step 1, under one label', () => {
    band({ runs: 3, notices: [indexFailure, crossCondition] });

    expect(fixture.debugElement.queryAll(By.css('.mc-wizard-notice-label')).length).toBe(1);
    const strip = fixture.debugElement.query(By.css('.mc-wizard-notice')).nativeElement as HTMLElement;
    expect(strip.nextElementSibling?.classList).toContain('mc-wizard-nav');
  });

  it('keeps the notice band and the navigation as separate regions', () => {
    band({ runs: 2 });

    const nav = fixture.debugElement.query(By.css('.mc-wizard-nav')).nativeElement as HTMLElement;
    expect(getComputedStyle(nav).borderTopWidth).not.toBe('0px');
  });

  it('derives the plot-cap notice from its own constant, as information', () => {
    band({ runs: component.maxPlottedEntries + 1, notices: [] });

    expect(component.plotCapNotice?.id).toBe('plot-cap');
    const alerts = bandAlerts();
    expect(alerts.length).toBe(1);
    // Blue, not amber: the plot cap changes how much is drawn, not what the figures mean.
    expect(alerts[0].classList).toContain('alert-info');
    expect(alerts[0].textContent).toContain(`plot at most ${component.maxPlottedEntries}`);
  });

  it('drops the plot-cap notice above the request cap, leaving the band with no notice list', () => {
    band({ runs: component.maxSources + 1, notices: [] });

    expect(component.plotCapNotice).toBeNull();
    expect(fixture.debugElement.query(By.css('.mc-wizard-notice'))).toBeTruthy();
    expect(fixture.debugElement.query(By.css('.mc-wizard-notices'))).toBeNull();
  });

  it('sorts the wizard-derived plot-cap notice in with the host-derived ones', () => {
    band({ runs: component.maxPlottedEntries + 1 });

    expect(component.bandNotices.map(notice => notice.id)).toEqual(['cross-condition', 'plot-cap']);
  });

  it('drops the band from step 2 on', () => {
    band({ runs: component.maxPlottedEntries + 1 });
    render(buildDto(comparableSet(3)), 2);
    fixture.detectChanges();

    expect(fixture.debugElement.query(By.css('.mc-wizard-notice'))).toBeNull();
  });

  it('renders the band with no notice list when the selection is within both caps and has nothing to report', () => {
    band({ runs: 2, sources: runSources(2), notices: [] });

    expect(fixture.debugElement.query(By.css('.mc-wizard-notice'))).toBeTruthy();
    expect(fixture.debugElement.query(By.css('.mc-wizard-notices'))).toBeNull();
  });

  it('says in the band that nothing is selected yet, and stops as soon as something is', () => {
    band({ runs: 0, notices: [] });

    expect(component.bandNotices.map(notice => notice.id)).toEqual(['nothing-selected']);
    const alerts = bandAlerts();
    expect(alerts.length).toBe(1);
    // A warning: Compare is blocked, but nothing is wrong with the view or the index.
    expect(alerts[0].classList).toContain('alert-warning');
    expect(alerts[0].querySelector('.alert-heading')?.textContent).toContain('Nothing is selected yet');
    expect(alerts[0].querySelector('.alert-body')?.textContent).toContain(
      'Select at least one completed run, analysis group or battery result in the tables above. '
      + 'Compare stays unavailable until you do.');
    // The band explains; the footer names the blocked control, and neither repeats the other.
    expect(textOf('.mc-wizard-blocked')).toContain('Select at least one run, analysis group or battery result.');

    fixture.componentRef.setInput('selectedRunCount', 1);
    fixture.detectChanges();

    expect(component.nothingSelectedNotice).toBeNull();
    // The band itself stays on screen; only its notice list, now with nothing to report, is gone.
    expect(fixture.debugElement.query(By.css('.mc-wizard-notice'))).toBeTruthy();
    expect(fixture.debugElement.query(By.css('.mc-wizard-notices'))).toBeNull();
  });

  it('drops the nothing-selected notice while a comparison is being computed', () => {
    band({ runs: 0, notices: [] });
    fixture.componentRef.setInput('loading', true);
    fixture.detectChanges();

    expect(component.nothingSelectedNotice).toBeNull();
    expect(fixture.debugElement.query(By.css('.mc-wizard-notice'))).toBeTruthy();
    expect(fixture.debugElement.query(By.css('.mc-wizard-notices'))).toBeNull();
  });

  it('shows a spinner and Comparing on the footer while step 1 waits for its comparison', () => {
    render(null, 1);
    fixture.componentRef.setInput('selectedRunCount', 2);
    fixture.componentRef.setInput('loading', true);
    fixture.detectChanges();

    expect(component.comparing).toBe(true);
    expect(component.nextLabel).toBe('Comparing…');

    const next = nextButton();
    expect(next.textContent).toContain('Comparing…');
    expect(next.querySelector('.gh-spinner-small')).toBeTruthy();
    expect(next.getAttribute('aria-busy')).toBe('true');
    // aria-disabled, never disabled: the reason has to stay reachable by keyboard.
    expect(next.getAttribute('aria-disabled')).toBe('true');
    expect(next.hasAttribute('disabled')).toBe(false);

    // The busy state is in the footer, where the click was; nothing is inserted above the tabs.
    expect(fixture.debugElement.query(By.css('.mc-wizard-loading'))).toBeNull();
    expect(textOf('#mc-next-blocked')).toContain('pricing every entry server-side');
    const root = fixture.nativeElement as HTMLElement;
    const header = root.querySelector('.mc-wizard-header')!;
    const tabs = root.querySelector('.mc-wizard-steps')!;
    const statusAboveTabs = Array.from(root.querySelectorAll('[role="status"]')).filter(status =>
      (header.compareDocumentPosition(status) & Node.DOCUMENT_POSITION_FOLLOWING) !== 0 &&
      (tabs.compareDocumentPosition(status) & Node.DOCUMENT_POSITION_PRECEDING) !== 0);
    expect(statusAboveTabs).toEqual([]);

    // The step-1 panel says its result is pending; the picker's own controls stay live.
    const panel = fixture.debugElement.query(By.css('#mc-step-panel-1')).nativeElement as HTMLElement;
    expect(panel.getAttribute('aria-busy')).toBe('true');

    fixture.componentRef.setInput('loading', false);
    fixture.detectChanges();
    expect(component.comparing).toBe(false);
    expect(component.nextLabel).toBe('Compare');
    expect(next.hasAttribute('aria-busy')).toBe(false);
  });

  it('labels the footer Next rather than Comparing while step 2 refetches', () => {
    render(buildDto(comparableSet(3)), 2);
    fixture.componentRef.setInput('loading', true);
    fixture.detectChanges();

    // A Prices or Recompute refetch loads too, and Next on step 2 is not blocked by it.
    expect(component.comparing).toBe(false);
    expect(component.nextLabel).toBe('Next');
    expect(component.canGoNext).toBe(true);
    expect(nextButton().querySelector('.gh-spinner-small')).toBeNull();
  });

  describe('while a comparison is loading', () => {
    afterEach(() => {
      vi.useRealTimers();
    });

    /** Step 1 with a valid selection, Compare pressed and the request in flight. */
    function comparingOnStep1(): void {
      render(null, 1);
      fixture.componentRef.setInput('selectedRunCount', 2);
      fixture.componentRef.setInput('loading', true);
      fixture.detectChanges();
    }

    it('offers Cancel Comparison only while step 1 is comparing, and emits cancelCompare once', () => {
      render(null, 1);
      fixture.componentRef.setInput('selectedRunCount', 2);
      fixture.detectChanges();
      expect(cancelCompareButton()).toBeNull();

      fixture.componentRef.setInput('loading', true);
      fixture.detectChanges();
      const cancel = cancelCompareButton();
      expect(cancel).toBeTruthy();
      // A dismissal: the cancel variant, and no icon.
      expect(cancel!.querySelector('svg')).toBeNull();

      const cancelled: number[] = [];
      component.cancelCompare.subscribe(() => cancelled.push(1));
      cancel!.click();
      expect(cancelled.length).toBe(1);
    });

    it('offers no Cancel Comparison on a step-2 refetch', () => {
      render(buildDto(comparableSet(3)), 2);
      fixture.componentRef.setInput('loading', true);
      fixture.detectChanges();

      expect(cancelCompareButton()).toBeNull();
    });

    it('moves focus to Next when Cancel Comparison is pressed and removed', () => {
      comparingOnStep1();
      // As the host does: it drops loading and checks the view before the emit returns.
      component.cancelCompare.subscribe(() => {
        fixture.componentRef.setInput('loading', false);
        fixture.detectChanges();
      });

      const cancel = cancelCompareButton()!;
      cancel.focus();
      cancel.click();
      fixture.detectChanges();

      expect(cancelCompareButton()).toBeNull();
      expect(document.activeElement).toBe(nextButton());
    });

    it('moves focus to Next when the result lands while Cancel Comparison has focus', () => {
      comparingOnStep1();
      cancelCompareButton()!.focus();
      expect(document.activeElement).toBe(cancelCompareButton());

      fixture.componentRef.setInput('loading', false);
      fixture.detectChanges();

      expect(cancelCompareButton()).toBeNull();
      expect(document.activeElement).toBe(nextButton());
    });

    it('says after 15 seconds that the comparison is slow, and how to leave it', () => {
      vi.useFakeTimers();
      comparingOnStep1();

      vi.advanceTimersByTime(14_999);
      fixture.detectChanges();
      expect(component.slowLoading).toBe(false);
      expect(textOf('#mc-next-blocked')).not.toContain('longer than usual');

      vi.advanceTimersByTime(1);
      fixture.detectChanges();
      expect(component.slowLoading).toBe(true);
      expect(textOf('#mc-next-blocked')).toContain('longer than usual');
      expect(textOf('#mc-next-blocked')).toContain('close the wizard');

      fixture.componentRef.setInput('loading', false);
      fixture.detectChanges();
      expect(component.slowLoading).toBe(false);
    });

    it('shows a spinner-bearing refetch line on step 2, where Next carries no spinner', () => {
      render(buildDto(comparableSet(3)), 2);
      fixture.componentRef.setInput('loading', true);
      fixture.detectChanges();

      const busy = fixture.debugElement.query(By.css('.mc-wizard-position .mc-wizard-busy'))
        .nativeElement as HTMLElement;
      expect(busy.querySelector('.gh-spinner-small')).toBeTruthy();
      expect(busy.textContent).toContain('Recomputing the comparison');
      expect(nextButton().querySelector('.gh-spinner-small')).toBeNull();

      fixture.componentRef.setInput('loading', false);
      fixture.detectChanges();
      expect(fixture.debugElement.query(By.css('.mc-wizard-busy'))).toBeNull();
    });

    it('keeps the header close button enabled and emitting while step 1 is comparing', () => {
      comparingOnStep1();

      const close = fixture.debugElement.query(By.css('[aria-label="Close cross-model comparison"]'))
        .nativeElement as HTMLButtonElement;
      expect(close.disabled).toBe(false);

      const closed: number[] = [];
      component.closeRequested.subscribe(() => closed.push(1));
      close.click();
      expect(closed.length).toBe(1);
    });

    it('puts no blocking layer over the wizard, on step 1 or on a step-2 refetch', () => {
      const expectNoBlockingLayer = (where: string): void => {
        const root = fixture.nativeElement as HTMLElement;
        expect(root.querySelectorAll('[inert]').length, `${where}: inert`).toBe(0);
        expect(root.querySelectorAll('[class*="overlay"], [class*="scrim"], [class*="backdrop"]').length, `${where}: overlay`).toBe(0);
      };

      comparingOnStep1();
      expectNoBlockingLayer('step 1');

      render(buildDto(comparableSet(3)), 2);
      fixture.componentRef.setInput('loading', true);
      fixture.detectChanges();
      expectNoBlockingLayer('step 2');
    });
  });

  it('emits compare from the footer rather than advancing, while no comparison exists', () => {
    render(null);
    fixture.componentRef.setInput('selectedRunCount', 2);
    fixture.detectChanges();

    const asked: number[] = [];
    component.compare.subscribe(() => asked.push(1));
    component.nextStep();

    // The step advances when the payload lands, not on the click: advancing now would show an
    // empty step 2 for the length of the round trip.
    expect(asked.length).toBe(1);
    expect(component.step).toBe(1);
  });

  it('advances on the first comparison, stays put on a refetch, and drops back when it is lost', () => {
    fixture.componentRef.setInput('comparison', null);
    fixture.detectChanges();
    expect(component.step).toBe(1);

    fixture.componentRef.setInput('comparison', buildDto(comparableSet(3)));
    fixture.detectChanges();
    expect(component.step).toBe(2);

    // A pricing-basis refetch replaces one payload with another; it must not move the reader.
    fixture.componentRef.setInput('comparison', buildDto(comparableSet(3)));
    fixture.detectChanges();
    expect(component.step).toBe(2);

    fixture.componentRef.setInput('comparison', null);
    fixture.detectChanges();
    expect(component.step).toBe(1);
  });

  it('opens step 2 over a set no chart can draw, with Next unblocked', () => {
    render(buildDto([
      buildExcludedEntry('run:8', ['ScoringMethodVersion']),
      buildExcludedEntry('run:9', ['CandidatePromptOptions'])
    ]), 2);

    // The table is the artefact that says what could not be compared, so its step opens here.
    expect(component.step).toBe(2);
    expect(component.isStepReachable(2)).toBe(true);
    expect(component.canGoNext).toBe(true);
    expect(component.nextBlockedReason).toBe('');
    expect(nextButton().getAttribute('aria-disabled')).toBe('false');

    const chartsTab = fixture.debugElement
      .queryAll(By.css('.mc-wizard-steps .gh-tab'))[1].nativeElement as HTMLElement;
    expect(chartsTab.getAttribute('aria-disabled')).toBe('false');
  });

  it('reaches step 2 whenever a comparison exists, and not without one', () => {
    render(null);

    expect(component.isStepReachable(2)).toBe(false);

    render(buildDto(comparableSet(3)));
    expect(component.isStepReachable(2)).toBe(true);
    // Step 2 gates on nothing but the payload: the table behind it always has rows.
    expect(component.canGoNext).toBe(true);
    expect(component.nextBlockedReason).toBe('');
  });

  it('labels step 4 Next as Close and emits closeRequested from it, and step 2 Next as Next', () => {
    render(buildDto(comparableSet(3)), 2);
    expect(component.step).toBe(2);
    expect(component.nextLabel).toBe('Next');
    expect(textOf('.mc-wizard-position')).toContain('Step 2 of 4 — Charts & table');

    render(buildDto(comparableSet(3)), 4);
    expect(component.step).toBe(4);
    expect(component.nextLabel).toBe('Close');
    expect(textOf('.mc-wizard-position')).toContain('Step 4 of 4 — Documents');

    const closed: number[] = [];
    component.closeRequested.subscribe(() => closed.push(1));
    component.nextStep();

    expect(closed.length).toBe(1);
  });

  it('hides the step 1 panel rather than rendering it beside the open step', () => {
    render(buildDto(comparableSet(4)), 2);

    // `display: flex` on .mc-step is an author declaration and outranks the user-agent [hidden]
    // rule, so the panel needs an explicit [hidden] declaration of its own; without it both
    // panels render side by side in .mc-wizard-body's row.
    const panel = fixture.debugElement.query(By.css('#mc-step-panel-1'))
      .nativeElement as HTMLElement;
    expect(getComputedStyle(panel).display).toBe('none');

    component.goToStep(1);
    fixture.detectChanges();

    expect(getComputedStyle(panel).display).not.toBe('none');
  });

  it('survives being measured at width zero, which is what a closed dialog reports', () => {
    render(buildDto(comparableSet(3)), 2);

    expect(() => component.applyContainerWidth(0)).not.toThrow();
    expect(component.orientation).toBe('vertical');
  });

  it('disables its own close controls while an export is running, and nothing else', () => {
    render(buildDto(comparableSet(3)), 2);
    component.exporting = true;
    refresh();

    const close = fixture.debugElement.query(By.css('.mc-wizard-header .btn-icon-action'))
      .nativeElement as HTMLButtonElement;
    const next = nextButton();
    expect(close.disabled).toBe(true);
    expect(next.disabled).toBe(true);
    // The wizard's two are the only close controls: no preview dialog carries a third. The About
    // dialog's own close stays live, since closing it leaves the export alone.
    const wizardCloses = fixture.debugElement.queryAll(By.css('.btn-icon-action'))
      .filter(button => !(button.nativeElement as HTMLElement).closest('.mc-about-dialog'));
    expect(wizardCloses.length).toBe(1);
    expect(fixture.debugElement.query(By.css('dialog.mc-preview-dialog'))).toBeNull();

    component.exporting = false;
    refresh();
    expect(close.disabled).toBe(false);
    expect(next.disabled).toBe(false);
  });
});

describe('selectionNotices', () => {
  function entry(
    overrides: Partial<BenchmarkComparabilityIndexEntryDto> = {}
  ): BenchmarkComparabilityIndexEntryDto {
    return {
      key: 'run:1',
      sourceKind: 'Run',
      sourceId: 1,
      conditionOrdinal: 1,
      conditionLabel: 'Condition A',
      signature: 'sig-a',
      selfInconsistent: false,
      selfInconsistentKeys: [],
      differencesFromLargest: [],
      questionParallelism: '1',
      speedCalibration: 'speed-a',
      pricingSnapshot: '2026-09-01',
      ...overrides
    };
  }

  function buildIndex(entries: BenchmarkComparabilityIndexEntryDto[]): BenchmarkComparabilityIndexDto {
    return {
      computedAtUtc: '2026-09-05T12:00:00Z',
      entries,
      conditions: [
        {
          ordinal: 1, label: 'Condition A', sourceCount: 2, runCount: 2,
          signature: 'sig-a', newestRunStartedAtUtc: '2026-09-05T10:00:00Z'
        },
        {
          ordinal: 2, label: 'Condition B', sourceCount: 1, runCount: 1,
          signature: 'sig-b', newestRunStartedAtUtc: '2026-09-04T10:00:00Z'
        }
      ],
      largestConditionKeys: [{
        name: 'serviceTier',
        label: 'Candidate service tier',
        description: 'The service tier the candidate ran under.',
        kind: 'Instrument',
        valueKind: 'Text',
        value: 'standard',
        displayValue: null
      }],
      referenceSelectionRule: 'The reference condition is the one with the most sources.',
      mustMatchKeyNames: ['serviceTier'],
      modelAxisKeyNames: ['modelId'],
      degradingKeyNames: ['QuestionParallelism', 'PricingSnapshot']
    };
  }

  /**
   * Two runs in the reference condition, one outside it, and one analysis group whose own members
   * disagree — which is the whole space of placements the index can report.
   */
  function defaultIndex(): BenchmarkComparabilityIndexDto {
    return buildIndex([
      entry({ key: 'run:1', sourceId: 1 }),
      entry({ key: 'run:2', sourceId: 2 }),
      entry({
        key: 'run:3',
        sourceId: 3,
        conditionOrdinal: 2,
        conditionLabel: 'Condition B',
        signature: 'sig-b'
      }),
      entry({
        key: 'group:11',
        sourceKind: 'Group',
        sourceId: 11,
        conditionOrdinal: 0,
        conditionLabel: 'Self-inconsistent',
        selfInconsistent: true,
        selfInconsistentKeys: ['ScoringMethodVersion']
      })
    ]);
  }

  function state(overrides: Partial<ComparisonSelectionState> = {}): ComparisonSelectionState {
    return {
      index: defaultIndex(),
      indexLoading: false,
      indexError: null,
      runIds: [],
      groupIds: [],
      pricingBasis: 'Current',
      ...overrides
    };
  }

  function ids(notices: readonly ComparisonSelectionNotice[]): string[] {
    return notices.map(notice => notice.id);
  }

  it('says nothing about a selection that sits wholly in the reference condition', () => {
    expect(selectionNotices(state({ runIds: [1, 2] }))).toEqual([]);
  });

  it('says nothing while nothing is selected and the index is in hand', () => {
    expect(selectionNotices(state())).toEqual([]);
  });

  it('reports a failed index as an error, carrying the server text', () => {
    const notices = selectionNotices(state({
      index: null,
      indexError: 'The index could not be built.'
    }));

    expect(ids(notices)).toEqual(['index-error']);
    expect(notices[0].severity).toBe('error');
    expect(notices[0].body).toContain('The index could not be built.');
    expect(notices[0].body).toContain('cannot be checked for comparability before Compare');
  });

  it('reports an index still in flight as information', () => {
    const notices = selectionNotices(state({ index: null, indexLoading: true }));

    expect(ids(notices)).toEqual(['index-loading']);
    expect(notices[0].severity).toBe('info');
  });

  it('refuses the whole selection when no condition contains any of it', () => {
    const notices = selectionNotices(state({ groupIds: [11] }));

    expect(ids(notices)).toEqual(['no-condition', 'self-inconsistent']);
    expect(notices[0].severity).toBe('error');
    expect(notices[0].body).toContain('would chart nothing');
  });

  it('stays silent about an empty condition set while the index has not landed', () => {
    // Otherwise every selection would be refused for the length of the round trip.
    expect(ids(selectionNotices(state({ index: null, indexLoading: true, runIds: [1] }))))
      .toEqual(['index-loading']);
  });

  it('names the excluded count and the reference condition once the selection crosses one', () => {
    const notices = selectionNotices(state({ runIds: [1, 3] }));

    expect(ids(notices)).toEqual(['cross-condition', 'single-point']);
    expect(notices[0].body).toBe('1 of 2 selected sources fall outside Condition A and will be '
      + 'excluded from the comparison — only one condition can be charted.');
  });

  it('warns about a self-inconsistent group inside an otherwise single-condition selection', () => {
    // The gap the cross-condition sentence cannot close: an unassigned group folds to no condition,
    // so the selection still spans exactly one and nothing used to be said about the exclusion.
    const notices = selectionNotices(state({ runIds: [1, 2], groupIds: [11] }));

    expect(ids(notices)).toEqual(['self-inconsistent']);
    expect(notices[0].severity).toBe('warning');
    expect(notices[0].body).toContain('Analysis group 11');
    expect(notices[0].body).toContain('ScoringMethodVersion');
  });

  it('warns that one point in the reference condition draws no figure', () => {
    const notices = selectionNotices(state({ runIds: [1] }));

    expect(ids(notices)).toEqual(['single-point']);
    expect(notices[0].body).toContain('A comparison needs two points');
  });

  it('warns when the sources to be charted differ on question parallelism', () => {
    const notices = selectionNotices(state({
      index: buildIndex([
        entry({ key: 'run:1', sourceId: 1, questionParallelism: '1' }),
        entry({ key: 'run:2', sourceId: 2, questionParallelism: '4' })
      ]),
      runIds: [1, 2]
    }));

    expect(ids(notices)).toEqual(['degrading-keys']);
    expect(notices[0].heading).toBe('The speed and cost axes will be flagged');
    expect(notices[0].body).toContain('QuestionParallelism');
    expect(notices[0].body).toContain('speed axis');
    expect(notices[0].body).toContain('cost axis');
  });

  it('warns that a differing speed calibration flags the speed axis alone', () => {
    const index = buildIndex([
      entry({ key: 'run:1', sourceId: 1, speedCalibration: 'speed-old' }),
      entry({ key: 'run:2', sourceId: 2, speedCalibration: 'speed-new' })
    ]);

    const notices = selectionNotices(state({ index, runIds: [1, 2] }));

    expect(ids(notices)).toEqual(['degrading-keys']);
    // The calibration cannot move a quality or a cost number, so the heading claims neither.
    expect(notices[0].heading).toBe('The speed axis will be flagged');
    expect(notices[0].body).toContain('SpeedCalibration');
    expect(notices[0].body).not.toContain('cost axis');
  });

  it('warns about a differing pricing snapshot only on the As-run basis', () => {
    const index = buildIndex([
      entry({ key: 'run:1', sourceId: 1, pricingSnapshot: '2026-08-01' }),
      entry({ key: 'run:2', sourceId: 2, pricingSnapshot: '2026-09-01' })
    ]);

    // Repriced to one basis, the figures are not charting the stored snapshot prices at all, so a
    // snapshot difference no longer describes the cost axis.
    expect(selectionNotices(state({ index, runIds: [1, 2], pricingBasis: 'Current' }))).toEqual([]);

    const notices = selectionNotices(state({ index, runIds: [1, 2], pricingBasis: 'AsRun' }));
    expect(ids(notices)).toEqual(['degrading-keys']);
    // The snapshot degrades cost only, so the heading claims nothing about the speed axis.
    expect(notices[0].heading).toBe('The cost axis will be flagged');
    expect(notices[0].body).toContain('PricingSnapshot');
    expect(notices[0].body).toContain('cost axis');
  });

  it('orders errors above warnings above information', () => {
    const notices = selectionNotices(state({
      indexError: 'The index is stale.',
      indexLoading: true,
      runIds: [1, 3]
    }));

    expect(ids(notices))
      .toEqual(['index-error', 'cross-condition', 'single-point', 'index-loading']);
    expect(notices.map(notice => notice.severity))
      .toEqual(['error', 'warning', 'warning', 'info']);
  });
});

/**
 * The projected step-1 content, which is the source picker in the running application.
 *
 * Its own state — two TableState instances holding a sort column, a page and a set of filters —
 * is exactly what would be lost if step 1 were an @if rather than [hidden], so this asserts the
 * element survives a round trip through another step rather than being re-created.
 */
@Component({
  standalone: true,
  imports: [ModelComparisonComponent],
  template: `
    <app-benchmark-model-comparison [comparison]="comparison" [selectedRunCount]="2">
      <input id="projected-picker-state" type="text">
    </app-benchmark-model-comparison>`
})
class ProjectionHostComponent {
  /** As the real host does: it checks its own view after every mutation it makes. */
  readonly cdr = inject(ChangeDetectorRef);

  comparison: BenchmarkModelComparisonDto | null = null;
}

describe('ModelComparisonComponent projected step 1', () => {
  it('keeps the projected content alive across a step away and back', async () => {
    await TestBed.configureTestingModule({
      imports: [ProjectionHostComponent],
      providers: [provideCharts({ registerables: APP_CHART_REGISTRABLES }), provideHttpClient(), provideHttpClientTesting()]
    }).compileComponents();

    const fixture = TestBed.createComponent(ProjectionHostComponent);
    fixture.detectChanges();

    const wizard = fixture.debugElement
      .query(By.directive(ModelComparisonComponent)).componentInstance as ModelComparisonComponent;
    const before = fixture.debugElement.query(By.css('#projected-picker-state'))
      .nativeElement as HTMLInputElement;
    before.value = 'sorted by condition, page 3';

    fixture.componentInstance.comparison = {
      pricingBasis: 'Current',
      pricingBasisLabel: 'Current catalog',
      computedAtUtc: '2026-09-07T12:00:00Z',
      baselineSuiteId: 5,
      baselineSuiteName: 'Suite',
      baselineEntryKeys: [],
      baselineKeyValues: {},
      baselineSignature: '',
      modelAxisKeys: [],
      entries: [],
      comparableCount: 0,
      excludedCount: 0,
      thinkingLevelsDiffer: false,
      speedAxisCaveat: null,
      explanation: '',
      excludedMeasures: []
    };
    fixture.componentInstance.cdr.detectChanges();
    expect(wizard.step).toBe(2);

    wizard.goToStep(1);
    fixture.componentInstance.cdr.detectChanges();

    const after = fixture.debugElement.query(By.css('#projected-picker-state'))
      .nativeElement as HTMLInputElement;
    expect(after).toBe(before);
    expect(after.value).toBe('sorted by condition, page 3');
  });
});

describe('model-comparison adapter', () => {
  const excluded: BenchmarkModelComparisonEntryDto = {
    key: 'run:9',
    sourceKind: 'Run',
    sourceId: 9,
    sourceName: null,
    runIds: [9],
    runCount: 1,
    suiteId: 5,
    suiteName: 'Suite',
    provider: 'Google',
    modelId: 'gemini-2.5-flash-lite',
    modelDisplayName: 'Gemini 2.5 Flash Lite',
    thinkingLevel: null,
    reasoningMode: null,
    reasoningSummary: null,
    serviceTier: null,
    maxOutputTokens: null,
    parallelExecutionMode: 'Enabled',
    label: 'Gemini 2.5 Flash Lite',
    firstRunStartedAtUtc: '2026-09-01T10:00:00Z',
    lastRunStartedAtUtc: '2026-09-01T10:00:00Z',
    state: 'Excluded',
    comparable: false,
    excluded: true,
    speedDegraded: false,
    costDegraded: false,
    excludingKeys: ['ScoringMethodVersion'],
    speedDegradingKeys: [],
    costDegradingKeys: [],
    differences: [],
    explanation: 'Graded under a different scoring method version.',
    quality: null,
    speed: null,
    cost: null,
    table: null
  };

  it('reports an unmeasured axis as absent rather than as zero', () => {
    const [entry] = toChartEntries({
      pricingBasis: 'Current',
      pricingBasisLabel: 'Current catalog',
      computedAtUtc: '2026-09-07T12:00:00Z',
      baselineSuiteId: 5,
      baselineSuiteName: 'Suite',
      baselineEntryKeys: [],
      baselineKeyValues: {},
      baselineSignature: '',
      modelAxisKeys: [],
      entries: [excluded],
      comparableCount: 0,
      excludedCount: 1,
      thinkingLevelsDiffer: false,
      speedAxisCaveat: null,
      explanation: '',
      excludedMeasures: []
    });

    expect(entry.excluded).toBe(true);
    expect(entry.excludedReasonKeys).toEqual(['ScoringMethodVersion']);
    // NaN, never 0: a zero cost would plot as a bar on the baseline and read as "free".
    expect(Number.isNaN(entry.intelligenceIndex)).toBe(true);
    expect(Number.isNaN(entry.ttftP50Ms)).toBe(true);
    expect(Number.isNaN(entry.modelTimeMeanMs)).toBe(true);
    expect(Number.isNaN(entry.totalModelTimeMs)).toBe(true);
    expect(Number.isNaN(entry.candidateCostPerQuestionUsd)).toBe(true);
    expect(Number.isNaN(entry.candidateCostPerRunUsd)).toBe(true);
    // No cost object, so no run total: NaN, like every other absent measure.
    expect(Number.isNaN(entry.totalRunCostUsd)).toBe(true);
    expect(entry.totalRunCostSdUsd).toBeNull();
    expect(entry.candidateCostPerQuestionSdUsd).toBeNull();
    expect(entry.speedIndexSd).toBeNull();
    expect(entry.totalModelTimeSdMs).toBeNull();
  });

  it('maps the run total including grading roles when the payload carries one, and NaN when it is null', () => {
    const cost: BenchmarkModelComparisonCostDto = {
      candidateCostPerQuestionUsd: 0.0123,
      candidateCostPerRunUsd: 0.2214,
      totalRunCostPerRunUsd: 0.9876,
      totalRunCostSdUsd: 0.0432,
      basis: 'Current',
      pricingResolved: true,
      degraded: false
    };
    const measured = (key: string, overrides: Partial<BenchmarkModelComparisonCostDto>): BenchmarkModelComparisonEntryDto =>
      ({ ...excluded, key, excluded: false, comparable: true, state: 'Comparable', excludingKeys: [], cost: { ...cost, ...overrides } });
    const [withTotal, withoutTotal] = toChartEntries({
      pricingBasis: 'Current',
      pricingBasisLabel: 'Current catalog',
      computedAtUtc: '2026-09-07T12:00:00Z',
      baselineSuiteId: 5,
      baselineSuiteName: 'Suite',
      baselineEntryKeys: [],
      baselineKeyValues: {},
      baselineSignature: '',
      modelAxisKeys: [],
      entries: [
        measured('run:1', {}),
        measured('run:2', {
          totalRunCostPerRunUsd: null,
          totalRunCostSdUsd: null,
          totalRunCostUnavailableReason: 'Run 2 has no resolvable pricing.'
        })
      ],
      comparableCount: 2,
      excludedCount: 0,
      thinkingLevelsDiffer: false,
      speedAxisCaveat: null,
      explanation: '',
      excludedMeasures: []
    });

    expect(withTotal.totalRunCostUsd).toBe(0.9876);
    expect(withTotal.totalRunCostSdUsd).toBe(0.0432);
    expect(Number.isNaN(withoutTotal.totalRunCostUsd)).toBe(true);
    expect(withoutTotal.totalRunCostSdUsd).toBeNull();
  });

  it('reads the question counts and the pricing label off the payload header', () => {
    const context = toChartContext(null);
    expect(context.scoredItemsMin).toBe(0);
    expect(context.scoredItemsMax).toBe(0);
    expect(context.examItemCount).toBe(0);
    expect(context.pricingBasisLabel).toBe('Unknown pricing basis');
    expect(context.pricingBasis).toBe('');
    expect(context.pricedOn).toBe('');
    expect(context.questionsAskedPerRun).toBeNull();
  });
});
