import { of, throwError } from 'rxjs';

import {
  BatteryRunReportHarness,
  HASH,
  analysis,
  batteryPairComparison,
  batteryRun,
  comparison,
  completeResult,
  configureBatteryRunReport,
  tearDownBatteryRunReport
} from './battery-run-report-dialog.testing';

// The analysis and paired-comparison views that moved from the Multi-Suite tab into the Battery Run
// Report, with their bb- classes.
describe('BatteryRunReportDialogComponent analysis', () => {
  let h: BatteryRunReportHarness;

  beforeEach(async () => {
    h = await configureBatteryRunReport();
  });

  afterEach(() => {
    tearDownBatteryRunReport(h);
  });

  function showTab(key: string): void {
    h.click(`#brr-tab-${key}`);
  }

  it('shows a complete analysis with its headline, uncertainty, profile and sensitivity', () => {
    h.open();

    expect(h.service.getBatteryAnalysis).toHaveBeenCalledWith(7);
    showTab('robustness');
    expect(h.text('.bb-headline-value')).toBe('72.4 ± 5.1');
    expect(h.text('.bb-headline-interval')).toContain('[67.3, 77.5]');
    expect(h.text('.bb-uncertainty')).toContain('15.6');
    expect(h.text('.bb-uncertainty')).toContain('2.131');
    expect(h.text('.bb-sensitivity-table')).toContain('Questions only');
    expect(h.text('.bb-sensitivity-table')).toContain('Sensitivity');
    expect(h.text('.bb-loo-table')).toContain('−2.4');

    showTab('suites');
    expect(h.el().querySelectorAll('.bb-profile-table tbody tr').length).toBe(2);
    expect(h.text('.bb-profile-table thead')).toContain('Critical errors');
    expect(h.text('.bb-profile-table tbody tr:first-child')).toContain('$1.50');
    expect(h.el().querySelector('.bb-recompute-callout')).toBeNull();
  });

  it('shows an incomplete analysis without a headline and calls out Recompute', () => {
    const incomplete = completeResult();
    incomplete.complete = false;
    incomplete.completedSuiteCount = 1;
    incomplete.overallIndex = null;
    incomplete.suites = [incomplete.suites[0], { ...incomplete.suites[1], complete: false, index: null }];
    h.service.getBatteryAnalysis.mockReturnValue(of(analysis({
      complete: false,
      result: incomplete,
      excludedMembers: [{ suiteIndex: 1, round: 1, runId: 102, reason: 'index withheld (a question failed at the provider)' }]
    })));
    h.open();

    showTab('robustness');
    expect(h.text('.bb-headline-value')).toBe('Incomplete (1 of 2 suites)');
    expect(h.el().querySelector('.bb-uncertainty')).toBeNull();
    expect(h.text('.bb-excluded')).toContain('Board Reading, round 1, run #102');
    expect(h.text('.bb-excluded')).toContain('index withheld');
    expect(h.text('.bb-recompute-callout')).toContain('Recompute');
    expect(h.text('.brr-integrity-notice')).toContain('Incomplete: 1 of 2 suites have a usable result');

    h.click('.bb-recompute-callout .bb-recompute');
    expect(h.service.analyseBatteryRun).toHaveBeenCalledWith(7);
  });

  it('offers Compute when no analysis exists', () => {
    h.service.getBatteryAnalysis.mockReturnValue(of(null));
    h.open();

    expect(h.text('.bb-no-analysis')).toContain('No analysis has been computed');
    expect(h.text('.bb-no-analysis .bb-recompute')).toBe('Compute');
    expect(h.text('#brr-actions-popover [data-action="recompute"]')).toBe('Compute analysis');

    h.click('.bb-no-analysis .bb-recompute');
    expect(h.service.analyseBatteryRun).toHaveBeenCalledWith(7);
    expect(h.el().querySelector('.bb-no-analysis')).toBeNull();
    expect(h.service.getBatteryRun).toHaveBeenCalledTimes(2);
  });

  it('shows the server message when a recompute fails', () => {
    h.service.analyseBatteryRun.mockReturnValue(throwError(() => ({ status: 500, error: 'The analysis failed.' })));
    h.open();
    h.action('recompute').click();
    h.fixture.detectChanges();
    expect(h.text('.bb-analysis-error')).toBe('The analysis failed.');
  });

  it('lists dimensions, speed, cost and usage on their own tabs', () => {
    const result = completeResult();
    result.usage = {
      totalInputTokens: 1200, totalOutputTokens: 340, totalCacheReadTokens: 50,
      totalAssessmentInputTokens: 900, totalAssessmentOutputTokens: 200,
      totalClaimVerificationInputTokens: 0, totalClaimVerificationOutputTokens: 0,
      totalToolCalls: 12, totalModelCalls: 30, toolCallsByFamily: { wiki: 8, source: 4 },
      claimsSupported: 3, claimsRefuted: 1, claimsIndeterminate: 0, claimsChecked: 4
    };
    h.service.getBatteryAnalysis.mockReturnValue(of(analysis({ result })));
    h.open();

    expect(h.text('#brr-panel-dimensions .bb-dimensions')).toContain('Accuracy');
    expect(h.text('#brr-panel-dimensions .bb-dimensions')).toContain('8.0 %');
    expect(h.text('#brr-panel-speed .bb-speed')).toContain('4.2 s / 9.0 s / 12.0 s');
    expect(h.text('#brr-panel-cost .bb-cost')).toContain('Pass cost: Candidate');
    expect(h.text('#brr-panel-cost .bb-usage')).toContain('Tool calls: wiki');
    expect(h.el().querySelector('#brr-panel-cost')?.classList).toContain('rr-panel-narrow');
    expect(h.el().querySelector('#brr-panel-configuration')?.classList).toContain('rr-panel-medium');
    expect(h.text('#brr-panel-configuration')).toContain(HASH);
    expect(h.text('#brr-panel-configuration .brr-config-suites')).toContain('60.0 %');
  });

  it('lists each suite\'s recorded instrument hashes on the Configuration tab, short and in full', () => {
    const prompt = 'aaaaaaaa' + HASH;
    const guides = 'bbbbbbbb' + HASH;
    const kb = 'cccccccc' + HASH;
    const wiki = 'dddddddd' + HASH;
    const source = 'eeeeeeee' + HASH;
    h.service.getBatteryRun.mockReturnValue(of(batteryRun({
      suites: [
        {
          index: 0, suiteId: 11, suiteName: 'Gameplay Help', customWeight: null,
          candidateSystemPromptSha256: prompt, toolGuidesSha256: guides, knowledgeBaseHeadSha: kb,
          wikiHeadSha: wiki, sourceCodeHeadSha: source
        },
        { index: 1, suiteId: 12, suiteName: 'Board Reading', customWeight: null, wikiHeadSha: null }
      ]
    })));
    h.open();
    showTab('configuration');

    const table = h.el().querySelector('#brr-panel-configuration .brr-config-fingerprints') as HTMLTableElement;
    expect(table).not.toBeNull();
    expect(Array.from(table.querySelectorAll('thead th')).map(th => th.firstChild?.textContent?.trim()))
      .toEqual(['#', 'Suite', 'PROMPT', 'GUIDES', 'KB', 'WIKI', 'SRC']);
    expect(table.querySelector('thead th[data-fingerprint="KB"] .visually-hidden')?.textContent).toBe(' (knowledge base)');

    const rows = table.querySelectorAll('tbody tr');
    expect(rows.length).toBe(2);
    expect(rows[0].querySelector('th[scope="row"]')?.textContent?.trim()).toBe('Gameplay Help');
    const promptCell = rows[0].querySelector('td[data-fingerprint="PROMPT"]') as HTMLElement;
    const short = promptCell.querySelector('code.brr-fp-short') as HTMLElement;
    expect(short.textContent).toBe('aaaaaaaa');
    expect(short.classList).toContain('fp-prompt');
    expect(short.getAttribute('aria-hidden')).toBe('true');
    expect(short.hasAttribute('title')).toBe(false);
    expect(promptCell.querySelector('.visually-hidden.brr-fp-full')?.textContent).toBe(prompt);
    expect(rows[0].querySelector('td[data-fingerprint="SRC"] code.brr-fp-short')?.textContent).toBe('eeeeeeee');
    expect(rows[0].querySelector('td[data-fingerprint="SRC"] code.brr-fp-short')?.classList).toContain('fp-source');

    const unrecorded = Array.from(rows[1].querySelectorAll('td[data-fingerprint]')) as HTMLElement[];
    expect(unrecorded.length).toBe(5);
    for (const cell of unrecorded) {
      expect(cell.querySelector('code')).toBeNull();
      expect(cell.textContent?.trim()).toBe('not recorded');
    }
  });

  describe('Paired Test', () => {
    function baselineOptions(): string[] {
      return Array.from(h.el().querySelectorAll('#bb-compare-baseline option'))
        .map(o => (o as HTMLOptionElement).value)
        .filter(v => v !== '');
    }

    it('loads the definition\'s results when the tab is chosen and offers them as baselines, this run left out', () => {
      h.open();
      expect(h.service.getBatteryLeaderboard).not.toHaveBeenCalled();

      showTab('paired');
      expect(h.service.getBatteryLeaderboard).toHaveBeenCalledWith(HASH);
      expect(baselineOptions()).toEqual(['8', '4', '5']);
      expect(Array.from(h.el().querySelectorAll('#bb-compare-baseline optgroup')).map(g => g.getAttribute('label')))
        .toEqual(['Harness 30', 'Harness 29']);
      expect(h.text('.bb-compare-treatment')).toContain('#7 · Model X');
      expect((h.el().querySelector('.bb-compare-btn') as HTMLButtonElement).disabled).toBe(true);
      expect(h.text('#bb-compare-kind')).toBe('Choose a baseline.');
    });

    it('loads the results on open when Paired Test is the remembered tab', () => {
      localStorage.setItem('overseer.benchmark.batteryRunReport.tab', 'paired');
      h.open();
      expect(h.service.getBatteryLeaderboard).toHaveBeenCalledTimes(1);
      expect(baselineOptions().length).toBe(3);
    });

    it('names the kind of each pairing', () => {
      h.open();
      showTab('paired');
      h.select('#bb-compare-baseline', '8');
      expect(h.text('#bb-compare-kind')).toContain('Model comparison');
      h.select('#bb-compare-baseline', '4');
      expect(h.text('#bb-compare-kind')).toContain('Replicate');
      expect(h.text('#bb-compare-kind')).toContain('cannot be compared');
      expect((h.el().querySelector('.bb-compare-btn') as HTMLButtonElement).disabled).toBe(true);
      h.select('#bb-compare-baseline', '5');
      expect(h.text('#bb-compare-kind')).toContain('probably be refused');
    });

    it('says so when the definition has no other analyzed run', () => {
      const board = { definitionSha256: HASH, batteryId: 3, batteryName: 'Core Battery', incomplete: [], classes: [] };
      h.service.getBatteryLeaderboard.mockReturnValue(of(board));
      h.open();
      showTab('paired');
      expect(h.text('.bb-no-baseline')).toContain('No other analyzed battery run');
      expect(h.el().querySelector('#bb-compare-baseline')).toBeNull();
    });

    it('shows the refusal text when a comparison is refused', () => {
      h.open();
      showTab('paired');
      h.service.analyseBatteryRun.mockReturnValue(throwError(() => ({
        status: 400,
        error: 'Not comparable: HarnessVersion and CandidateSystemPromptSha256 differ.'
      })));

      h.select('#bb-compare-baseline', '5');
      h.click('.bb-compare-btn');

      expect(h.service.analyseBatteryRun).toHaveBeenCalledWith(7, 5);
      expect(h.text('.bb-compare-refusal')).toContain('HarnessVersion and CandidateSystemPromptSha256 differ');
      expect(h.service.getBatteryPairedComparison).not.toHaveBeenCalled();
      expect(h.el().querySelector('.brr-paired-measures')).toBeNull();
    });

    it('shows D, its interval, the randomization p and Holm-adjusted per-suite p, with this run as the treatment', () => {
      h.open();
      showTab('paired');
      h.service.analyseBatteryRun.mockReturnValue(of(analysis({ batteryRunId: 7, comparedWithBatteryRunId: 8, comparison: comparison() })));

      h.select('#bb-compare-baseline', '8');
      h.click('.bb-compare-btn');

      expect(h.service.analyseBatteryRun).toHaveBeenCalledWith(7, 8);
      expect(h.text('.bb-compare-d')).toBe('+4.4');
      expect(h.text('.bb-compare-headline')).toContain('[1.6, 7.2]');
      expect(h.text('.bb-compare-facts')).toContain('0.012');
      expect(h.text('.bb-compare-facts')).toContain('Exact enumeration');
      const rows = h.el().querySelectorAll('.bb-compare-table tbody tr');
      expect(rows.length).toBe(2);
      expect(rows[1].textContent).toContain('0.040');

      h.select('#bb-compare-baseline', '4');
      expect(h.el().querySelector('.bb-compare-d')).toBeNull();
      expect(h.el().querySelector('.brr-paired-measures')).toBeNull();
    });

    it('tests the same pair on the dimensions, speed and cost after M7, which stays the Intelligence test', () => {
      h.open();
      showTab('paired');
      h.service.analyseBatteryRun.mockReturnValue(of(analysis({ batteryRunId: 7, comparedWithBatteryRunId: 8, comparison: comparison() })));

      h.select('#bb-compare-baseline', '8');
      h.click('.bb-compare-btn');

      expect(h.service.getBatteryPairedComparison).toHaveBeenCalledWith(7, 8);
      expect(h.text('.bb-compare-d')).toBe('+4.4');
      expect(h.text('.brr-paired-verdict')).toBe('Higher on the same questions');
      expect(h.el().querySelector('.brr-paired-verdict circle')?.getAttribute('fill')).toBe('currentColor');

      // The result leaves its own Intelligence headline out: M7 above is that test.
      expect(h.el().querySelector('.brr-paired-measures .ptr-headline')).toBeNull();
      expect(h.text('.brr-paired-measures .ptr-kind')).toContain('Model comparison');
      expect(h.text('#brr-paired-result-section-speed')).toContain('0.82× (0.71–0.95)');
      expect(h.text('#brr-paired-result-section-speed')).toContain('Faster on the same questions');
      expect(h.text('#brr-paired-result-section-cost')).toContain('Cheaper on the same questions');
      expect(h.text('#brr-paired-result-section-dimensions')).toContain('Accuracy');
      expect(h.text('#brr-paired-result-section-dimensions')).toContain('No difference established');
      expect(h.text('#brr-paired-result-section-dimensions')).not.toContain('equal');
    });

    it('names the changed instrument key of a verification', () => {
      h.service.getBatteryPairedComparison.mockReturnValue(of(batteryPairComparison({
        kind: 'Verification', kindLabel: 'Verification of a change',
        explanation: 'The same model; one instrument key differs.', changedKeys: ['CandidateSystemPromptSha256']
      })));
      h.open();
      showTab('paired');
      h.service.analyseBatteryRun.mockReturnValue(of(analysis({ batteryRunId: 7, comparedWithBatteryRunId: 5, comparison: comparison() })));

      h.select('#bb-compare-baseline', '5');
      h.click('.bb-compare-btn');

      expect(h.text('.brr-paired-measures .ptr-kind')).toContain('Verification of a change');
      expect(h.text('.brr-paired-measures .ptr-changed')).toContain('CandidateSystemPromptSha256');
    });

    it('keeps the M7 result when the other measures are refused, and says why', () => {
      h.open();
      showTab('paired');
      h.service.analyseBatteryRun.mockReturnValue(of(analysis({ batteryRunId: 7, comparedWithBatteryRunId: 8, comparison: comparison() })));
      h.service.getBatteryPairedComparison.mockReturnValue(throwError(() => ({
        status: 400, error: 'Not comparable: the speed measurement is degraded.'
      })));

      h.select('#bb-compare-baseline', '8');
      h.click('.bb-compare-btn');

      expect(h.text('.bb-compare-d')).toBe('+4.4');
      expect(h.text('.brr-paired-error')).toBe('Not comparable: the speed measurement is degraded.');
      expect(h.el().querySelector('.brr-paired-measures app-paired-test-result')).toBeNull();
      expect(h.el().querySelector('.brr-paired-verdict')).toBeNull();
    });

    it('keeps the baseline choices of the battery run\'s own definition', () => {
      h.service.getBatteryRun.mockReturnValue(of(batteryRun({ definitionSha256: 'other-hash' })));
      h.open();
      showTab('paired');
      expect(h.service.getBatteryLeaderboard).toHaveBeenCalledWith('other-hash');
    });
  });
});
