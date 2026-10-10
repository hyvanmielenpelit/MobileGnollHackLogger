import { ComponentFixture, TestBed } from '@angular/core/testing';

import { CcAnalysisResult, CcAnalysisSummary } from '../chat-consistency.models';
import {
  CC_BATTERY_SET_KEY,
  ccAnalysisResult,
  ccAnalysisSummary,
  ccAxis,
  ccUnitView,
  textOf
} from '../chat-consistency-tab.testing';
import { CC_LATEST_NO_DOCUMENTS, CcLatestAnalysisCardComponent } from './latest-analysis-card.component';

describe('CcLatestAnalysisCardComponent', () => {
  let fixture: ComponentFixture<CcLatestAnalysisCardComponent>;
  let card: CcLatestAnalysisCardComponent;
  let el: HTMLElement;

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [CcLatestAnalysisCardComponent] }).compileComponents();
    fixture = TestBed.createComponent(CcLatestAnalysisCardComponent);
    card = fixture.componentInstance;
    el = fixture.nativeElement as HTMLElement;
  });

  afterEach(() => fixture.destroy());

  function render(inputs: Partial<{
    summary: CcAnalysisSummary | null;
    result: CcAnalysisResult | null;
    loading: boolean;
    error: string | null;
    opening: boolean;
    openError: string | null;
    listLoading: boolean;
    listError: string | null;
  }>): void {
    for (const [name, value] of Object.entries(inputs)) fixture.componentRef.setInput(name, value);
    fixture.detectChanges();
  }

  /** The facts as `[term, value]` pairs. */
  function facts(): string[][] {
    return Array.from(el.querySelectorAll('dl.bm-summary-facts > div'))
      .map(row => [textOf(row.querySelector('dt')), textOf(row.querySelector('dd'))]);
  }

  const open = (): HTMLButtonElement => el.querySelector<HTMLButtonElement>('#cc-latest-open')!;
  const documents = (): HTMLButtonElement => el.querySelector<HTMLButtonElement>('#cc-latest-documents')!;

  it('says no analysis is saved yet, with no actions, while the list is empty', () => {
    render({ summary: null });
    expect(textOf(el.querySelector('#cc-latest-eyebrow'))).toBe('Latest analysis');
    expect(textOf(el.querySelector('.cc-latest-empty')))
      .toBe('No analysis is saved yet. Open the wizard, choose a model, and Analyze in step 3.');
    expect(el.querySelector('button')).toBeNull();
    expect(el.querySelector('#cc-latest-title')).toBeNull();
  });

  it('says the saved analyses are loading, or why they could not be read', () => {
    render({ summary: null, listLoading: true });
    expect(textOf(el.querySelector('.cc-latest-empty'))).toBe('Loading the saved analyses…');
    render({ listLoading: false, listError: 'The saved analyses could not be loaded.' });
    expect(textOf(el.querySelector('.cc-latest-error'))).toBe('The saved analyses could not be loaded.');
  });

  it('shows the summary at once and announces the verdicts loading, with a ring', () => {
    render({ summary: ccAnalysisSummary(7), loading: true });

    expect(el.querySelector('section.cc-latest')!.getAttribute('aria-labelledby')).toBe('cc-latest-eyebrow cc-latest-title');
    expect(el.querySelector('h4#cc-latest-title')).not.toBeNull();
    expect(textOf(el.querySelector('#cc-latest-title'))).toBe('#7 · Analysis 7');
    expect(textOf(el.querySelector('.cc-latest-subject .thinking-badge'))).toBe('thinking level High');
    expect(textOf(el.querySelector('.cc-latest-subject .cc-kind-tag'))).toBe('All suites');
    const status = el.querySelector('.cc-latest-status')!;
    expect(status.getAttribute('role')).toBe('status');
    expect(textOf(status)).toBe('Loading the verdicts…');
    expect(status.querySelector('svg.dc-ring')).not.toBeNull();
    expect(el.querySelector('.cc-latest-outcome')).toBeNull();
    expect(el.querySelector('app-cc-endpoint-chips')).toBeNull();
    expect(facts()).toEqual([
      ['Baseline', '2026-09-01 – 2026-09-14'],
      ['Comparison', '2026-09-15 – 2026-10-01'],
      ['Saved', '2026-10-02 09:00 UTC · Protocol V1'],
      ['Reports', 'None yet']
    ]);
    // Each period's color beside its word.
    expect(el.querySelector('[data-fact="baseline"] dt .cc-latest-swatch[data-period="baseline"]')).not.toBeNull();
  });

  it('shows the outcome, the endpoint chips and the unit counts once the result arrives', () => {
    render({ summary: ccAnalysisSummary(7), loading: false, result: ccAnalysisResult() });

    const outcome = el.querySelector('.cc-latest-outcome')!;
    expect(outcome.getAttribute('data-outcome')).toBe('changed');
    expect(textOf(outcome.querySelector('.cc-latest-outcome-title'))).toBe('The chat changed');
    expect(textOf(outcome.querySelector('.cc-latest-outcome-detail'))).toBe('Time to first answer text: degraded (indicated)');
    expect(textOf(el.querySelector('.cc-latest-status'))).toBe('');
    const chips = Array.from(el.querySelectorAll('.cc-ep-chip')).map(chip => chip.getAttribute('data-status'));
    expect(chips).toEqual(['within', 'changed', 'within', 'within', 'within']);
    expect(el.querySelector('ul.cc-ep-chips')!.getAttribute('aria-label')).toBe('Endpoints of analysis #7');
    expect(facts().slice(0, 2)).toEqual([
      ['Baseline', '2026-09-01 – 2026-09-14 · 3 runs'],
      ['Comparison', '2026-09-15 – 2026-10-01 · 3 runs']
    ]);
  });

  it('counts battery runs in a battery analysis, and names a one-day period by its day', () => {
    render({
      summary: ccAnalysisSummary(4, {
        comparisonSetKey: CC_BATTERY_SET_KEY, comparisonSetLabel: 'Two initial suites (revision 1)',
        baselineStartUtc: '2026-10-08T08:00:00Z', baselineEndUtc: '2026-10-08T09:00:00Z',
        comparisonStartUtc: '2026-10-08T12:00:00Z', comparisonEndUtc: '2026-10-08T13:00:00Z'
      }),
      result: ccAnalysisResult({
        analysisId: 4, unitKind: 'batteryRun',
        units: [ccUnitView(11, 'baseline', { kind: 'batteryRun' }), ccUnitView(12, 'comparison', { kind: 'batteryRun' })]
      })
    });
    expect(textOf(el.querySelector('.cc-latest-subject .cc-kind-tag'))).toBe('Battery');
    expect(textOf(el.querySelector('.cc-latest-compared-label'))).toBe('Two initial suites (revision 1)');
    expect(facts().slice(0, 2)).toEqual([
      ['Baseline', '2026-10-08 · 1 battery run'],
      ['Comparison', '2026-10-08 · 1 battery run']
    ]);
  });

  it('ignores a result of another analysis', () => {
    render({ summary: ccAnalysisSummary(8), result: ccAnalysisResult(), loading: true });
    expect(el.querySelector('.cc-latest-outcome')).toBeNull();
    expect(textOf(el.querySelector('.cc-latest-status'))).toBe('Loading the verdicts…');
  });

  it('says the verdicts could not be loaded, and keeps the summary and the actions', () => {
    render({ summary: ccAnalysisSummary(7), error: 'The verdicts of analysis #7 could not be loaded.' });
    expect(textOf(el.querySelector('.cc-latest-error'))).toBe('The verdicts of analysis #7 could not be loaded.');
    expect(textOf(el.querySelector('.cc-latest-status'))).toBe('');
    expect(facts().length).toBe(4);
    expect(open()).not.toBeNull();
  });

  it('opens the analysis with the gold button, and refuses Documents with its reason while none is written', () => {
    const opened: number[] = [];
    const docs: number[] = [];
    card.openAnalysis.subscribe(id => opened.push(id));
    card.openDocuments.subscribe(id => docs.push(id));
    render({ summary: ccAnalysisSummary(7) });

    expect(textOf(open())).toBe('Open Analysis #7');
    expect(open().classList).toContain('btn-gh');
    expect(el.querySelector('.cc-latest-actions')!.getAttribute('role')).toBe('group');
    open().click();
    expect(opened).toEqual([7]);

    expect(textOf(documents())).toBe('Documents (0)');
    expect(documents().classList).toContain('btn-ghost');
    expect(documents().getAttribute('aria-disabled')).toBe('true');
    expect(documents().getAttribute('aria-describedby')).toBe('cc-latest-documents-reason');
    expect(textOf(el.querySelector('#cc-latest-documents-reason'))).toBe(CC_LATEST_NO_DOCUMENTS);
    documents().click();
    expect(docs).toEqual([]);
  });

  it('opens the documents while some are written, and counts them in Reports', () => {
    const docs: number[] = [];
    card.openDocuments.subscribe(id => docs.push(id));
    render({ summary: ccAnalysisSummary(7, { reportDocumentCount: 3 }) });

    expect(textOf(documents())).toBe('Documents (3)');
    expect(documents().hasAttribute('aria-disabled')).toBe(false);
    expect(el.querySelector('#cc-latest-documents-reason')).toBeNull();
    expect(facts()[3]).toEqual(['Reports', '3 report documents']);
    documents().click();
    expect(docs).toEqual([7]);
  });

  it('is busy while opening, refuses a second open and shows an open error', () => {
    const opened: number[] = [];
    card.openAnalysis.subscribe(id => opened.push(id));
    render({ summary: ccAnalysisSummary(7), opening: true });
    expect(open().getAttribute('aria-busy')).toBe('true');
    open().click();
    expect(opened).toEqual([]);

    render({ opening: false, openError: 'The analysis could not be opened.' });
    expect(open().hasAttribute('aria-busy')).toBe(false);
    const error = el.querySelector('.cc-latest-open-error')!;
    expect(error.getAttribute('role')).toBe('alert');
    expect(textOf(error)).toBe('The analysis could not be opened.');
  });

  it('takes the badges from the model axis when the summary does not name its model, and shows none without one', () => {
    fixture.componentRef.setInput('axes', [ccAxis({ key: 'anthropic/claude', provider: 'Anthropic', thinkingLevel: null })]);
    render({ summary: ccAnalysisSummary(5, { subject: null, subjectModelKey: 'anthropic/claude', endpoints: undefined }) });
    expect(textOf(el.querySelector('.cc-latest-subject .provider-badge'))).toBe('Anthropic');

    render({ summary: ccAnalysisSummary(6, { subject: null, subjectModelKey: 'gone/model' }) });
    expect(el.querySelector('.cc-latest-subject .provider-badge')).toBeNull();
    expect(el.querySelector('.cc-latest-subject .cc-kind-tag')).not.toBeNull();
  });
});
