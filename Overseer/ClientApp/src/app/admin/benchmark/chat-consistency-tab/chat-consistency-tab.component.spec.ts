import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpTestingController } from '@angular/common/http/testing';

import { BenchmarkShellBridge } from '../state/benchmark-shell-bridge.service';
import { ChatConsistencyTabComponent } from './chat-consistency-tab.component';
import {
  CC_API,
  ccAnalysisSummary,
  ccAxis,
  ccRunRows,
  ccTimeline,
  chatConsistencyTestProviders,
  textOf
} from './chat-consistency-tab.testing';

describe('ChatConsistencyTabComponent', () => {
  let fixture: ComponentFixture<ChatConsistencyTabComponent>;
  let http: HttpTestingController;
  let bridge: BenchmarkShellBridge;
  let el: HTMLElement;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [ChatConsistencyTabComponent],
      providers: chatConsistencyTestProviders()
    }).compileComponents();
    fixture = TestBed.createComponent(ChatConsistencyTabComponent);
    http = TestBed.inject(HttpTestingController);
    bridge = TestBed.inject(BenchmarkShellBridge);
    el = fixture.nativeElement as HTMLElement;

    fixture.detectChanges();
    http.expectOne(`${CC_API}/models`).flush([ccAxis(), ccAxis({ key: 'empty', displayName: 'No runs', runCount: 0 })]);
    http.expectOne(`${CC_API}/analyses`).flush([ccAnalysisSummary(7)]);
    fixture.detectChanges();
  });

  afterEach(() => {
    el.querySelectorAll('dialog').forEach(dialog => dialog.open && dialog.close());
    http.verify();
    fixture.destroy();
  });

  /** Chooses the model with the Timeline's picker and answers the requests it makes. */
  function chooseModel(): void {
    el.querySelector<HTMLButtonElement>('.cc-subject-model-selector .selector-trigger')!.click();
    fixture.detectChanges();
    const options = Array.from(el.querySelectorAll<HTMLElement>('.cc-subject-model-selector [role="option"]'));
    // The axis without runs is not offered.
    expect(options.map(option => textOf(option))).toEqual([expect.stringContaining('GPT-5 high')]);
    options[0].click();
    fixture.detectChanges();

    const timeline = http.expectOne(r => r.url === `${CC_API}/timeline`);
    expect(timeline.request.method).toBe('GET');
    expect(timeline.request.params.get('modelKey')).toBe('openai/gpt-5|high');
    const runs = http.expectOne(r => r.url === `${CC_API}/runs`);
    expect(runs.request.params.get('modelKey')).toBe('openai/gpt-5|high');
    timeline.flush(ccTimeline());
    runs.flush(ccRunRows());
    const annotations = http.expectOne(r => r.url === `${CC_API}/annotations`);
    expect(annotations.request.params.get('provider')).toBe('OpenAI');
    expect(annotations.request.params.get('modelId')).toBe('gpt-5');
    annotations.flush([]);
    fixture.detectChanges();
  }

  const row = (runId: number): HTMLTableRowElement =>
    el.querySelector<HTMLTableRowElement>(`.cc-run-table tr[data-run-id="${runId}"]`)!;

  it('opens on the Timeline section, with the others closed', () => {
    const sections = Array.from(el.querySelectorAll<HTMLDetailsElement>('details.gh-disclosure--section.cc-section'));
    expect(sections.map(section => section.id)).toEqual(['cc-section-timeline', 'cc-section-analyze', 'cc-section-saved', 'cc-section-annotations']);
    expect(sections.map(section => section.open)).toEqual([true, false, false, false]);
    expect(textOf(sections[0].querySelector('summary .gh-section-title'))).toBe('Timeline');
    expect(textOf(el.querySelector('.cc-tl-status'))).toBe('Choose a model to see its runs over time.');
  });

  it('loads the timeline and the run table when a model is chosen', () => {
    chooseModel();

    expect(textOf(el.querySelector('.cc-tl-status'))).toBe('6 runs of GPT-5 high in this range.');
    expect(el.querySelectorAll('.cc-run-table tbody tr').length).toBe(6);
    // Newest first by default.
    expect(el.querySelector('.cc-run-table tbody tr')!.getAttribute('data-run-id')).toBe('106');
    const figures = Array.from(el.querySelectorAll('app-cc-timeline-panel figure.cc-figure'));
    expect(figures.map(figure => figure.getAttribute('data-figure')))
      .toEqual(['quality', 'ttfat', 'rate', 'work', 'cost', 'reliability', 'timeline']);
    expect(textOf(figures[0].querySelector('figcaption'))).toContain('Quality held between 71 and 74 across 6 runs.');
    expect(figures[0].querySelector('canvas[role="img"]')!.getAttribute('aria-label')).toContain('Quality per run.');
    expect(figures[0].querySelector('details.cc-figure-data table caption')).not.toBeNull();
  });

  it('reads the range again as UTC day bounds', () => {
    chooseModel();
    const from = el.querySelector<HTMLInputElement>('#cc-tl-from')!;
    from.value = '2026-09-10';
    from.dispatchEvent(new Event('change'));
    fixture.detectChanges();

    const timeline = http.expectOne(r => r.url === `${CC_API}/timeline`);
    expect(timeline.request.params.get('from')).toBe('2026-09-10T00:00:00.000Z');
    expect(timeline.request.params.has('to')).toBe(false);
    timeline.flush(ccTimeline());
    http.expectOne(r => r.url === `${CC_API}/runs`).flush(ccRunRows());
  });

  it('renders each run\'s eligibility as badges with text and an icon, and the reason of an exclusion', () => {
    chooseModel();

    const badges = Array.from(row(103).querySelectorAll<HTMLElement>('.cc-elig'));
    expect(badges.map(badge => badge.getAttribute('data-axis'))).toEqual(['quality', 'speedTelemetry', 'speedLegacy', 'work', 'cost']);
    const excluded = row(103).querySelector<HTMLElement>('.cc-elig[data-axis="speedTelemetry"]')!;
    expect(excluded.classList).toContain('is-excluded');
    expect(textOf(excluded)).toBe('Speed (telemetry): not eligible');
    expect(excluded.querySelector('svg')!.getAttribute('aria-hidden')).toBe('true');
    expect(textOf(row(103).querySelector('.cc-elig[data-axis="quality"]'))).toBe('Quality: eligible');
    expect(textOf(row(103).querySelector('.cc-elig-reason'))).toBe('Speed (telemetry): No call telemetry');
    expect(textOf(row(103).querySelector('.cc-legacy-tag'))).toBe('Legacy');
    expect(textOf(row(106))).toContain('#206');
  });

  it('asks the shell bridge to repeat a run\'s setup and to open its run report', () => {
    chooseModel();
    const repeat = vi.spyOn(bridge, 'repeatRunSetup');
    const report = vi.spyOn(bridge, 'viewRunDetail');

    const repeatButton = row(105).querySelector<HTMLButtonElement>('.cc-repeat-btn')!;
    expect(textOf(repeatButton)).toBe('Repeat this run\'s setup');
    expect(repeatButton.getAttribute('aria-label')).toBe('Repeat this run\'s setup: run #105');
    repeatButton.click();
    expect(repeat).toHaveBeenCalledWith(105);

    row(104).querySelector<HTMLButtonElement>('.cc-open-report-btn')!.click();
    expect(report).toHaveBeenCalledWith(104);
  });

  it('marks and unmarks the grader anchor with PUT, updating the row', () => {
    chooseModel();
    const button = () => row(104).querySelector<HTMLButtonElement>('.cc-anchor-btn')!;
    expect(textOf(button())).toBe('Mark as anchor');

    button().click();
    fixture.detectChanges();
    expect(button().getAttribute('aria-disabled')).toBe('true');
    const mark = http.expectOne(`${CC_API}/runs/104/anchor`);
    expect(mark.request.method).toBe('PUT');
    expect(mark.request.body).toEqual({ isAnchor: true });
    mark.flush({ runId: 104, isAnchor: true });
    fixture.detectChanges();

    expect(textOf(row(104).querySelector('.cc-anchor-tag'))).toBe('Anchor');
    expect(textOf(button())).toBe('Unmark anchor');
    expect(textOf(el.querySelector('.cc-tl-announcement'))).toBe('Run #104 is the grader anchor.');

    button().click();
    fixture.detectChanges();
    const unmark = http.expectOne(`${CC_API}/runs/104/anchor`);
    expect(unmark.request.body).toEqual({ isAnchor: false });
    unmark.flush({ runId: 104, isAnchor: false });
    fixture.detectChanges();
    expect(row(104).querySelector('.cc-anchor-tag')).toBeNull();
  });

  it('shows an anchor refusal inline', () => {
    chooseModel();
    row(104).querySelector<HTMLButtonElement>('.cc-anchor-btn')!.click();
    http.expectOne(`${CC_API}/runs/104/anchor`).flush(null, { status: 404, statusText: 'Not Found' });
    fixture.detectChanges();
    expect(textOf(el.querySelector('.cc-tl-anchor-error'))).toBe('The anchor of run #104 could not be saved: It no longer exists.');
  });

  it('shows the 409 refusal in the delete confirmation and keeps the analysis', () => {
    el.querySelector<HTMLDetailsElement>('#cc-section-saved')!.open = true;
    fixture.detectChanges();
    const card = el.querySelector<HTMLElement>('.cc-analysis-card[data-analysis-id="7"]')!;
    expect(textOf(card.querySelector('.cc-analysis-title'))).toBe('Analysis 7');
    expect(textOf(card)).toContain('GPT-5 high');

    card.querySelector<HTMLButtonElement>('.cc-analysis-delete')!.click();
    fixture.detectChanges();
    const dialog = el.querySelector<HTMLDialogElement>('dialog.cc-delete-dialog')!;
    expect(dialog.open).toBe(true);
    expect(textOf(dialog.querySelector('h3'))).toBe('Delete Analysis 7?');

    dialog.querySelector<HTMLButtonElement>('.cc-delete-confirm')!.click();
    const del = http.expectOne(`${CC_API}/analyses/7`);
    expect(del.request.method).toBe('DELETE');
    del.flush({ error: 'Report documents were written from this analysis. Delete them first.' }, { status: 409, statusText: 'Conflict' });
    fixture.detectChanges();

    expect(dialog.open).toBe(true);
    const error = dialog.querySelector('.cc-delete-error')!;
    expect(error.getAttribute('role')).toBe('alert');
    expect(textOf(error)).toBe('Report documents were written from this analysis. Delete them first.');
    expect(el.querySelector('.cc-analysis-card[data-analysis-id="7"]')).not.toBeNull();
  });

  it('removes a deleted analysis from the list', () => {
    el.querySelector<HTMLDetailsElement>('#cc-section-saved')!.open = true;
    fixture.detectChanges();
    el.querySelector<HTMLButtonElement>('.cc-analysis-card[data-analysis-id="7"] .cc-analysis-delete')!.click();
    fixture.detectChanges();
    el.querySelector<HTMLButtonElement>('dialog.cc-delete-dialog .cc-delete-confirm')!.click();
    http.expectOne(`${CC_API}/analyses/7`).flush(null, { status: 204, statusText: 'No Content' });
    fixture.detectChanges();

    expect(el.querySelector<HTMLDialogElement>('dialog.cc-delete-dialog')!.open).toBe(false);
    expect(el.querySelector('.cc-analysis-card')).toBeNull();
    expect(textOf(el.querySelector('.cc-saved-empty'))).toContain('No analysis is saved yet.');
  });
});
