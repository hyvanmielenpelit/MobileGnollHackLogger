import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpTestingController } from '@angular/common/http/testing';

import { CC_CURRENT_ANALYSIS_CODE_VERSION, CcAnalysisSummary } from '../chat-consistency.models';
import {
  CC_API,
  CC_BATTERY_SET_KEY,
  ccAnalysisSummary,
  ccAxis,
  ccEndpointBrief,
  chatConsistencyTestProviders,
  textOf
} from '../chat-consistency-tab.testing';
import { CC_HISTORY_VIEW_STORAGE_KEY, CcAnalysisHistoryDialogComponent } from './analysis-history-dialog.component';

function clearStorage(): void {
  try {
    localStorage.removeItem(CC_HISTORY_VIEW_STORAGE_KEY);
  } catch {
    // Nothing stored.
  }
}

const settle = (ms = 0): Promise<void> => new Promise<void>(resolve => setTimeout(resolve, ms));

/** Three analyses, newest first: #9 of GPT-5 in a suite, #8 of Claude in a battery with reports, #7 of GPT-5 over all suites. */
function threeAnalyses(): CcAnalysisSummary[] {
  return [
    ccAnalysisSummary(9, {
      name: 'Suite check', createdAtUtc: '2026-10-09T10:00:00Z', analysisCodeVersion: CC_CURRENT_ANALYSIS_CODE_VERSION,
      comparisonSetKey: 'suite:id:5', comparisonSetLabel: 'Board Suite'
    }),
    ccAnalysisSummary(8, {
      name: 'Chat consistency: Claude 5.5 Haiku (xhigh)', createdAtUtc: '2026-10-08T10:00:00Z', analysisCodeVersion: 4,
      subjectModelKey: 'anthropic/claude-haiku|xhigh',
      subject: { displayName: 'Claude 5.5 Haiku (xhigh)', provider: 'Anthropic', modelId: 'claude-haiku-5-5', thinkingLevel: 'xhigh', serviceTier: null },
      comparisonSetKey: CC_BATTERY_SET_KEY, comparisonSetLabel: 'Two initial suites (revision 1)',
      reportDocumentCount: 3, relaxedPooling: true,
      headline: 'Overseer chat with Claude 5.5 Haiku: nothing decided yet',
      endpoints: [
        ccEndpointBrief('P1', { computed: false, verdictLabel: 'not computable', grade: 'notEstablished' }),
        ccEndpointBrief('P4', { verdictLabel: 'inconclusive', grade: 'notEstablished' })
      ]
    }),
    ccAnalysisSummary(7, { createdAtUtc: '2026-10-02T09:00:00Z', analysisCodeVersion: CC_CURRENT_ANALYSIS_CODE_VERSION })
  ];
}

describe('CcAnalysisHistoryDialogComponent', () => {
  let fixture: ComponentFixture<CcAnalysisHistoryDialogComponent>;
  let history: CcAnalysisHistoryDialogComponent;
  let el: HTMLElement;
  let http: HttpTestingController;

  beforeEach(async () => {
    clearStorage();
    await TestBed.configureTestingModule({
      imports: [CcAnalysisHistoryDialogComponent],
      providers: chatConsistencyTestProviders()
    }).compileComponents();
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    if (fixture) {
      el.querySelectorAll('dialog').forEach(dialog => {
        if (dialog.open) dialog.close();
      });
      fixture.destroy();
    }
    http.verify();
    clearStorage();
  });

  /** Creates the dialog over `analyses`, with the host's part played by the test: a delete removes the row. */
  function create(analyses: CcAnalysisSummary[] = threeAnalyses()): void {
    fixture = TestBed.createComponent(CcAnalysisHistoryDialogComponent);
    history = fixture.componentInstance;
    el = fixture.nativeElement as HTMLElement;
    fixture.componentRef.setInput('analyses', analyses);
    fixture.componentRef.setInput('axes', [ccAxis()]);
    history.deleted.subscribe(id => {
      fixture.componentRef.setInput('analyses', history.analyses.filter(analysis => analysis.id !== id));
    });
    fixture.detectChanges();
  }

  function show(): void {
    history.show();
    fixture.detectChanges();
  }

  const dialog = (): HTMLDialogElement => el.querySelector<HTMLDialogElement>('dialog.cc-history-dialog')!;
  const confirm = (): HTMLDialogElement => el.querySelector<HTMLDialogElement>('dialog.cc-delete-dialog')!;
  const card = (id: number): HTMLElement => el.querySelector<HTMLElement>(`.cc-hist-card[data-analysis-id="${id}"]`)!;
  const cardIds = (): string[] => Array.from(el.querySelectorAll('.cc-hist-card')).map(c => c.getAttribute('data-analysis-id')!);

  it('opens modally with its title focused, the emblem, a subtitle and light dismiss', () => {
    create();
    expect(dialog().open).toBe(false);
    show();

    expect(dialog().open).toBe(true);
    expect(dialog().matches(':modal')).toBe(true);
    expect(dialog().getAttribute('closedby')).toBe('any');
    expect(dialog().getAttribute('aria-labelledby')).toBe('cc-history-title');
    expect(textOf(el.querySelector('h3#cc-history-title'))).toBe('Chat Consistency Analysis History');
    expect(document.activeElement?.id).toBe('cc-history-title');
    const emblem = el.querySelector<HTMLImageElement>('.cc-history-title-row img.gnollbench-emblem')!;
    expect(emblem.getAttribute('alt')).toBe('');
    expect(textOf(el.querySelector('.cc-history-subtitle'))).toBe('3 saved analyses · newest first');
    expect(textOf(el.querySelector('#cc-hist-status'))).toBe('Showing 3 of 3 analyses');
    expect(el.querySelector('#cc-hist-status')!.getAttribute('role')).toBe('status');

    const close = el.querySelector<HTMLButtonElement>('.cc-history-close')!;
    expect(close.getAttribute('aria-label')).toBe('Close analysis history');
    expect(close.getAttribute('interestfor')).toBe('cc-history-close-tip');
    expect(textOf(el.querySelector('#cc-history-close-tip'))).toBe('Close');
    const done = el.querySelector<HTMLButtonElement>('.dialog-footer .cc-history-done')!;
    expect(textOf(done)).toBe('Done');
    expect(done.classList).toContain('btn-gh-cancel');
    done.click();
    expect(dialog().open).toBe(false);
  });

  it('shows each analysis as a card: kicker, title, model, periods, headline, chips and actions', () => {
    create();
    show();
    expect(cardIds()).toEqual(['9', '8', '7']);
    expect(el.querySelector('ul.cc-hist-cards')!.getAttribute('role')).toBe('list');

    const claude = card(8);
    expect(claude.getAttribute('aria-labelledby')).toBe('cc-hist-8-title');
    const kicker = textOf(claude.querySelector('.cc-hist-kicker'));
    expect(kicker).toContain('#8');
    expect(kicker).toContain('saved 2026-10-08 10:00 UTC');
    expect(kicker).toContain('Protocol V1');
    expect(textOf(claude.querySelector('.cc-hist-tag-reports'))).toBe('3 reports');
    expect(textOf(claude.querySelector('.cc-hist-tag-relaxed'))).toBe('Relaxed pooling');
    expect(textOf(claude.querySelector('.cc-hist-tag-earlier'))).toBe('Earlier analysis code');
    expect(textOf(claude.querySelector('#cc-hist-8-code-tip')))
      .toBe(`Saved under analysis code version 4; Overseer now analyzes under version ${CC_CURRENT_ANALYSIS_CODE_VERSION}. `
        + 'Open it and press Analyze again for a current analysis; this one stays as a record.');
    const title = claude.querySelector<HTMLElement>('h4.cc-hist-title#cc-hist-8-title')!;
    expect(textOf(title)).toBe('Chat consistency: Claude 5.5 Haiku (xhigh)');
    expect(title.tabIndex).toBe(-1);
    expect(textOf(claude.querySelector('.cc-hist-model'))).toBe('Claude 5.5 Haiku (xhigh)');
    expect(textOf(claude.querySelector('.cc-hist-subject .provider-badge'))).toBe('Anthropic');
    expect(textOf(claude.querySelector('.cc-hist-subject .cc-kind-tag'))).toBe('Battery');
    expect(textOf(claude.querySelector('.cc-hist-compared-label'))).toBe('Two initial suites (revision 1)');
    expect(textOf(claude.querySelector('[data-period="baseline"] dt'))).toBe('Baseline');
    expect(textOf(claude.querySelector('[data-period="baseline"] dd'))).toBe('2026-09-01 – 2026-09-14');
    expect(textOf(claude.querySelector('.cc-hist-headline'))).toBe('Overseer chat with Claude 5.5 Haiku: nothing decided yet');
    expect(Array.from(claude.querySelectorAll('.cc-ep-chip')).map(chip => chip.getAttribute('data-status')))
      .toEqual(['notComputable', 'inconclusive']);

    // Current code, no reports and no relaxed pooling: none of those tags.
    const current = card(9);
    expect(current.querySelector('.cc-hist-tag-earlier, .cc-hist-tag-reports, .cc-hist-tag-relaxed')).toBeNull();
    expect(textOf(current.querySelector('.cc-kind-tag'))).toBe('Suite');
    expect(textOf(card(7).querySelector('.cc-kind-tag'))).toBe('All suites');

    const actions = claude.querySelector('.cc-hist-actions')!;
    expect(actions.getAttribute('role')).toBe('group');
    expect(actions.getAttribute('aria-label')).toBe('Actions for analysis #8');
    expect(claude.querySelector('.cc-hist-open')!.getAttribute('aria-label')).toBe('Open analysis #8 in the wizard');
  });

  it('names a summary without its model by its axis, else by its key', () => {
    create([
      ccAnalysisSummary(2, { subject: null }),
      ccAnalysisSummary(1, { subject: null, subjectModelKey: 'gone/model', endpoints: undefined })
    ]);
    show();
    expect(textOf(card(2).querySelector('.cc-hist-model'))).toBe('GPT-5 high');
    expect(textOf(card(2).querySelector('.provider-badge'))).toBe('OpenAI');
    expect(textOf(card(1).querySelector('.cc-hist-model'))).toBe('gone/model');
    expect(card(1).querySelector('.provider-badge')).toBeNull();
    expect(card(1).querySelector('.cc-ep-chips')).toBeNull();
  });

  it('emits Open, busy while that analysis opens, and shows an open error', () => {
    create();
    show();
    const opened: number[] = [];
    history.open.subscribe(id => opened.push(id));
    card(8).querySelector<HTMLButtonElement>('.cc-hist-open')!.click();
    expect(opened).toEqual([8]);

    fixture.componentRef.setInput('openingId', 8);
    fixture.detectChanges();
    expect(card(8).querySelector('.cc-hist-open')!.getAttribute('aria-busy')).toBe('true');
    card(7).querySelector<HTMLButtonElement>('.cc-hist-open')!.click();
    expect(opened).toEqual([8]);

    fixture.componentRef.setInput('openingId', null);
    fixture.componentRef.setInput('openError', 'The analysis could not be opened.');
    fixture.detectChanges();
    const error = el.querySelector('.cc-hist-open-error')!;
    expect(error.getAttribute('role')).toBe('alert');
    expect(textOf(error)).toBe('The analysis could not be opened.');
  });

  it('restores the dialog with the title focused, or the Open of the analysis last opened from it', () => {
    create();
    history.restore();
    fixture.detectChanges();
    expect(dialog().open).toBe(true);
    expect(document.activeElement?.id).toBe('cc-history-title');

    card(8).querySelector<HTMLButtonElement>('.cc-hist-open')!.click();
    history.close();
    expect(dialog().open).toBe(false);
    history.restore();
    fixture.detectChanges();
    expect(dialog().open).toBe(true);
    expect(document.activeElement).toBe(card(8).querySelector('.cc-hist-open'));

    // The return target is used once.
    history.close();
    history.restore();
    expect(document.activeElement?.id).toBe('cc-history-title');
  });

  it('searches the name, the headline, the model and #id', async () => {
    create();
    show();
    const search = el.querySelector<HTMLInputElement>('#cc-hist-search')!;
    expect(textOf(el.querySelector('label[for="cc-hist-search"]'))).toBe('Search saved analyses');

    search.value = 'claude';
    search.dispatchEvent(new Event('input'));
    await settle(250);
    fixture.detectChanges();
    expect(cardIds()).toEqual(['8']);
    expect(textOf(el.querySelector('#cc-hist-status'))).toBe('One analysis · filtered from 3');

    search.value = '#7';
    search.dispatchEvent(new Event('input'));
    await settle(250);
    fixture.detectChanges();
    expect(cardIds()).toEqual(['7']);

    // Escape with text clears at once and keeps the dialog open.
    const escape = new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true });
    search.dispatchEvent(escape);
    fixture.detectChanges();
    expect(escape.defaultPrevented).toBe(true);
    expect(cardIds()).toEqual(['9', '8', '7']);
    expect(dialog().open).toBe(true);
  });

  it('sorts newest first, oldest first or by model, and remembers the order', () => {
    create();
    show();
    const sort = el.querySelector<HTMLSelectElement>('#cc-hist-sort')!;
    expect(Array.from(sort.options).map(option => option.text)).toEqual(['Newest first', 'Oldest first', 'Model (A–Z)']);

    sort.value = 'oldest';
    sort.dispatchEvent(new Event('change'));
    fixture.detectChanges();
    expect(cardIds()).toEqual(['7', '8', '9']);
    expect(JSON.parse(localStorage.getItem(CC_HISTORY_VIEW_STORAGE_KEY)!)).toEqual({ version: 1, sort: 'oldest' });
    expect(textOf(el.querySelector('.cc-history-subtitle'))).toBe('3 saved analyses · oldest first');

    sort.value = 'model';
    sort.dispatchEvent(new Event('change'));
    fixture.detectChanges();
    expect(cardIds()[0]).toBe('8');
    expect(textOf(el.querySelector('.cc-history-subtitle'))).toBe('3 saved analyses · by model');

    // A new dialog reads the stored order.
    fixture.destroy();
    create();
    show();
    expect(el.querySelector<HTMLSelectElement>('#cc-hist-sort')!.value).toBe('model');
  });

  it('filters by the Model, Provider, Compared and Reports facets, with removable chips', () => {
    create();
    show();
    const facetIds = Array.from(el.querySelectorAll('app-filter-facet .gh-facet-btn')).map(button => button.id);
    expect(facetIds).toEqual([
      'cc-hist-facet-model-trigger', 'cc-hist-facet-provider-trigger', 'cc-hist-facet-compared-trigger', 'cc-hist-facet-reports-trigger'
    ]);
    const compared = history.facets.find(facet => facet.column === 'compared')!;
    expect(compared.options.map(option => [option.label, option.count])).toEqual([['Battery', 1], ['Suite', 1], ['All suites', 1]]);
    const reports = history.facets.find(facet => facet.column === 'reports')!;
    expect(reports.options.map(option => [option.label, option.count])).toEqual([['Has reports', 1], ['No reports', 2]]);

    history.onFacetChange('provider', ['OpenAI']);
    fixture.detectChanges();
    expect(cardIds()).toEqual(['9', '7']);
    history.onFacetChange('reports', ['No reports']);
    fixture.detectChanges();
    expect(cardIds()).toEqual(['9', '7']);

    const chips = Array.from(el.querySelectorAll<HTMLButtonElement>('.cc-hist-chips .gh-filter-chip'));
    expect(chips.map(chip => chip.getAttribute('aria-label')))
      .toEqual(['Remove filter Provider: OpenAI', 'Remove filter Reports: No reports']);
    chips[0].click();
    fixture.detectChanges();
    expect(cardIds()).toEqual(['9', '7']);
    // Focus moves to the chip now in the removed one's place.
    expect(textOf(document.activeElement)).toContain('No reports');

    el.querySelector<HTMLButtonElement>('.cc-hist-clear-filters')!.click();
    fixture.detectChanges();
    expect(cardIds()).toEqual(['9', '8', '7']);
    expect(document.activeElement?.id).toBe('cc-hist-search');
  });

  it('shows ten cards, then Show N more, focusing the first new card', () => {
    const many = Array.from({ length: 12 }, (_, i) =>
      ccAnalysisSummary(100 - i, { createdAtUtc: `2026-09-${String(28 - i).padStart(2, '0')}T09:00:00Z`, analysisCodeVersion: 5 }));
    create(many);
    show();
    expect(cardIds().length).toBe(10);
    const more = el.querySelector<HTMLButtonElement>('.cc-hist-show-more')!;
    expect(textOf(more)).toBe('Show 2 more');
    expect(el.querySelector('.cc-hist-show-all')).toBeNull();
    more.click();
    fixture.detectChanges();
    expect(cardIds().length).toBe(12);
    expect(document.activeElement?.id).toBe('cc-hist-90-title');
  });

  it('refuses Delete while report documents exist, and says to delete them in step 6 first', () => {
    create();
    show();
    const remove = card(8).querySelector<HTMLButtonElement>('.cc-hist-delete')!;
    expect(remove.classList).toContain('btn-ghost-danger');
    expect(remove.getAttribute('aria-label')).toBe('Delete analysis #8');
    expect(remove.getAttribute('aria-disabled')).toBe('true');
    expect(remove.getAttribute('aria-describedby')).toBe('cc-hist-8-delete-reason');
    expect(textOf(el.querySelector('#cc-hist-8-delete-reason'))).toBe('Delete its 3 report documents in step 6 first.');

    remove.click();
    fixture.detectChanges();
    expect(confirm().open).toBe(false);
    expect(card(9).querySelector('.cc-hist-delete')!.hasAttribute('aria-disabled')).toBe(false);
  });

  it('lays out the confirmation\'s Keep It and Delete as one right-aligned row, the same height, 12px apart', () => {
    create();
    show();
    card(9).querySelector<HTMLButtonElement>('.cc-hist-delete')!.click();
    fixture.detectChanges();
    expect(confirm().open).toBe(true);

    const footer = confirm().querySelector<HTMLElement>('.dialog-footer')!;
    const style = getComputedStyle(footer);
    expect(style.display).toBe('flex');
    expect(style.columnGap).toBe('12px');
    const keep = footer.querySelector<HTMLElement>('.cc-delete-keep')!.getBoundingClientRect();
    const remove = footer.querySelector<HTMLElement>('.cc-delete-confirm')!.getBoundingClientRect();
    expect(Math.abs(keep.top - remove.top)).toBeLessThanOrEqual(0.5);
    expect(Math.abs(keep.bottom - remove.bottom)).toBeLessThanOrEqual(0.5);
    expect(remove.left).toBeGreaterThanOrEqual(keep.right + 11.5);
  });

  it('shows the server\'s 409 refusal in the confirmation and keeps the analysis', () => {
    create();
    show();
    card(9).querySelector<HTMLButtonElement>('.cc-hist-delete')!.click();
    fixture.detectChanges();
    expect(confirm().open).toBe(true);
    expect(textOf(confirm().querySelector('h3'))).toBe('Delete Suite check?');

    confirm().querySelector<HTMLButtonElement>('.cc-delete-confirm')!.click();
    const del = http.expectOne(`${CC_API}/analyses/9`);
    expect(del.request.method).toBe('DELETE');
    del.flush({ error: 'Report documents were written from this analysis. Delete them first.' }, { status: 409, statusText: 'Conflict' });
    fixture.detectChanges();

    expect(confirm().open).toBe(true);
    const error = confirm().querySelector('.cc-delete-error')!;
    expect(error.getAttribute('role')).toBe('alert');
    expect(textOf(error)).toBe('Report documents were written from this analysis. Delete them first.');
    expect(card(9)).not.toBeNull();
    expect(dialog().open).toBe(true);
  });

  it('deletes after the confirmation, says so and focuses the next card\'s title', () => {
    create();
    show();
    const deleted: number[] = [];
    history.deleted.subscribe(id => deleted.push(id));
    card(9).querySelector<HTMLButtonElement>('.cc-hist-delete')!.click();
    fixture.detectChanges();
    confirm().querySelector<HTMLButtonElement>('.cc-delete-confirm')!.click();
    http.expectOne(`${CC_API}/analyses/9`).flush(null, { status: 204, statusText: 'No Content' });
    fixture.detectChanges();

    expect(deleted).toEqual([9]);
    expect(confirm().open).toBe(false);
    expect(dialog().open).toBe(true);
    expect(cardIds()).toEqual(['8', '7']);
    expect(document.activeElement?.id).toBe('cc-hist-8-title');
    expect(textOf(el.querySelector('#cc-hist-status'))).toBe('Showing 2 of 2 analyses · Suite check was deleted.');
  });

  it('stops the confirmation\'s close, cancel and click short of the history dialog', () => {
    create();
    show();
    let reached = 0;
    el.addEventListener('click', () => reached++);
    el.addEventListener('cancel', () => reached++);
    el.addEventListener('close', () => reached++);
    for (const type of ['click', 'cancel', 'close']) {
      confirm().dispatchEvent(new Event(type, { bubbles: true }));
    }
    expect(reached).toBe(0);
  });

  it('says there is none when the list is empty, and shows a load error', () => {
    create([]);
    show();
    expect(textOf(el.querySelector('.cc-history-subtitle'))).toBe('0 saved analyses');
    expect(textOf(el.querySelector('.cc-hist-empty'))).toContain('No analysis is saved yet.');
    expect(el.querySelector('.cc-hist-filter-bar')).toBeNull();

    fixture.componentRef.setInput('error', 'The saved analyses could not be loaded.');
    fixture.detectChanges();
    expect(textOf(el.querySelector('.cc-hist-error'))).toBe('The saved analyses could not be loaded.');
  });
});
