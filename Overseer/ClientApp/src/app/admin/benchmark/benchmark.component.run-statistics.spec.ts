import type { MockedObject } from "vitest";
import { ComponentFixture, fakeAsync, discardPeriodicTasks } from '@angular/core/testing';
import { of } from 'rxjs';
import { AdminBenchmarkComponent } from './benchmark.component';
import { AdminBenchmarkService, BenchmarkRunAnswerDto } from '../../services/admin-benchmark.service';
import { AdminBenchmarkSpecContext, clearStoredState, createAdminBenchmarkFixture } from './benchmark.component.testing';

describe('AdminBenchmarkComponent', () => {
  let ctx: AdminBenchmarkSpecContext;
  let component: AdminBenchmarkComponent;
  let fixture: ComponentFixture<AdminBenchmarkComponent>;
  let benchmarkServiceMock: MockedObject<AdminBenchmarkService>;

  beforeEach(clearStoredState);

  afterEach(clearStoredState);

  beforeEach(async () => {
    ctx = await createAdminBenchmarkFixture();
    ({ component, fixture, benchmarkServiceMock } = ctx);
  });

  describe('run diagnostics capture', () => {
    function buildDiagnosticsRun(overrides: any = {}): any {
      return {
        id: 88,
        benchmarkSuiteId: 1,
        suiteName: 'Default Suite',
        testedModelDisplayNameUsed: 'GPT-5.6 Luna',
        testedModelProviderUsed: 'OpenAI',
        testedModelIdUsed: 'gpt-5.6-luna',
        testedModelThinkingLevelUsed: 'max',
        testedModelParallelExecutionModeUsed: 0,
        assessorModelDisplayNameUsed: 'Gemini 3.7 Flash',
        assessorModelProviderUsed: 'Google',
        assessorModelIdUsed: 'gemini-3.7-flash',
        secondOpinionAssessorModelConfigurationId: 4,
        secondOpinionAssessorModelDisplayNameUsed: 'Claude Opus 5',
        secondOpinionAssessorModelProviderUsed: 'Anthropic',
        secondOpinionAssessorModelIdUsed: 'claude-opus-5',
        status: 'Completed',
        startedAtUtc: '2026-09-03T06:52:00Z',
        completedAtUtc: '2026-09-03T07:28:00Z',
        qualityIndex: 94,
        unweightedQualityIndex: 92,
        rawQualityIndex: 96,
        speedIndex: 67,
        finalScore: 91,
        computedScore: null,
        totalAnswerDurationMs: 900000,
        totalDurationMs: 900000,
        scoringProfileId: 1,
        scoringProfileName: 'Standard Intelligence Index (Default)',
        scoringProfileSpeedTargetMs: 15000,
        scoringProfileSpeedDecayK: 20,
        scoringProfileSecondOpinionQualityThreshold: 50,
        scoringProfileSecondOpinionOutlierDeltaPoints: 25,
        scoringMethodVersion: 6,
        harnessVersion: '7',
        secondOpinionModeUsed: 3,
        secondOpinionGradedAnswerCount: 2,
        secondOpinionMeanAbsDelta: 4.25,
        secondOpinionDisagreementCount: 1,
        contestedVerdictAnswerCount: 1,
        reassessedAnswerCount: 1,
        transportDefectAnswerCount: 0,
        recoveredAnswerCount: 0,
        toolStarvedAnswerCount: 1,
        advisoryFlagAnswerCount: 1,
        scrubbedArtifactAnswerCount: 0,
        toolOverheadMs: 1056,
        difficultyFallbackUsed: false,
        speedMeasurementDegraded: false,
        maxParallelQuestionsUsed: 1,
        answeredQuestionCount: 2,
        totalQuestionCount: 2,
        assessmentParseFailed: false,
        totalInputTokens: 100,
        totalOutputTokens: 200,
        totalCacheReadTokens: 0,
        totalCacheCreationTokens: 0,
        errorMessage: null,
        answers: [
          {
            id: 1, benchmarkRunId: 88, orderIndex: 1, questionText: 'Q1', answerText: 'a',
            difficulty: 1, assessedDifficulty: 25, status: 'Ok', assessmentStatus: 'Scored',
            durationMs: 48800, modelTimeMs: 47744, toolTimeMs: 1056, scrubbedArtifactCount: 0,
            answerFlags: 32, answerFlagNames: ['ContestedVerdict'],
            qualityScore: 60, rawQualityScore: 60, speedScore: 29, criticalError: false,
            accuracyLevel: 3, completenessLevel: 4, concisenessLevel: 5, readabilityLevel: 6,
            toolCallCount: 34, toolCallBudgetUsed: 35, toolCallSummary: 'wiki_search×34',
            narrationBlockCount: 3, unverifiedClaimCount: 2,
            secondOpinionQualityScore: 85, secondOpinionTrigger: 'All', secondOpinionDisagreed: true,
            reassessmentCount: 1, previousQualityScore: 42,
            reassessedByModelDisplayNameUsed: 'Claude Opus 5'
          },
          {
            id: 2, benchmarkRunId: 88, orderIndex: 2, questionText: 'Q2', answerText: 'b',
            difficulty: 3, assessedDifficulty: 85, status: 'Ok', assessmentStatus: 'Scored',
            durationMs: 20000, modelTimeMs: 20000, scrubbedArtifactCount: 0,
            answerFlags: 0, answerFlagNames: [], qualityScore: 99, speedScore: 70,
            criticalError: false, toolCallCount: 25, toolCallBudgetUsed: 25,
            toolCallSummary: 'wiki_search×25 (3 blocked by budget)', toolBudgetExhausted: true,
            secondOpinionQualityScore: 97, secondOpinionTrigger: 'All'
          }
        ],
        ...overrides
      };
    }

    it('should name all three model roles', () => {
      ctx.monitor.activeRunDetail = buildDiagnosticsRun();
      const text = component.runDiagnosticsText;

      expect(text).toContain('Second:   Claude Opus 5 (Anthropic / claude-opus-5)');
    });

    it('should say so when no second opinion assessor was selected', () => {
      ctx.monitor.activeRunDetail = buildDiagnosticsRun({
        secondOpinionAssessorModelConfigurationId: null
      });
      expect(component.runDiagnosticsText).toContain('Second:   none selected');
    });

    it('should include a Verifier line built like the neighbouring model lines', () => {
      ctx.monitor.activeRunDetail = buildDiagnosticsRun({
        claimVerifierModelConfigurationId: 5,
        claimVerifierDisplayNameUsed: 'GnollHack Verifier',
        claimVerifierProviderUsed: 'Anthropic',
        claimVerifierModelIdUsed: 'claude-verifier',
        claimVerifierThinkingLevelUsed: 'high',
        claimVerifierReasoningModeUsed: 'enabled'
      });
      expect(component.runDiagnosticsText)
        .toContain('Verifier: GnollHack Verifier (Anthropic / claude-verifier), thinking: high, reasoning: enabled');
    });

    it('should say so when no claim verifier was selected', () => {
      ctx.monitor.activeRunDetail = buildDiagnosticsRun();
      expect(component.runDiagnosticsText).toContain('Verifier: none selected');
    });

    it('should print an unset service tier as default (none requested) in the MODELS block', () => {
      ctx.monitor.activeRunDetail = buildDiagnosticsRun();
      expect(component.runDiagnosticsText).toContain('service tier: default (none requested)');
    });

    it('should record the scoring constants the run was actually scored with', () => {
      ctx.monitor.activeRunDetail = buildDiagnosticsRun();
      const text = component.runDiagnosticsText;

      expect(text).toContain('harness version: 7');
      expect(text).toContain('scoring method version: 6');
      expect(text).toContain('Speed: target 15000 ms, decay k 20');
      // The outlier delta is read only by the FlaggedAndOutliers trigger, so under All it governed
      // nothing and printing it read as a threshold this run applied.
      expect(text).toContain('Second reader: mode All, threshold 50');
      expect(text).not.toContain('outlier delta');
    });

    it('should print the outlier delta only under the trigger that reads it', () => {
      ctx.monitor.activeRunDetail = buildDiagnosticsRun({ secondOpinionModeUsed: 2 });
      expect(component.runDiagnosticsText)
        .toContain('Second reader: mode FlaggedAndOutliers, threshold 50, outlier delta 25');
    });

    it('should name the mode added after this capture was written', () => {
      ctx.monitor.activeRunDetail = buildDiagnosticsRun({ secondOpinionModeUsed: 4 });
      expect(component.runDiagnosticsText).toContain('Second reader: mode FlaggedPlusSample');
      expect(component.runDiagnosticsText).not.toContain('mode unknown');
    });

    it('should omit the superseded computed score rather than printing "computed: n/a"', () => {
      ctx.monitor.activeRunDetail = buildDiagnosticsRun();
      const text = component.runDiagnosticsText;

      expect(text).not.toContain('computed:');
      expect(text).toContain('unweighted mean: 92');

      ctx.monitor.activeRunDetail = buildDiagnosticsRun({ computedScore: 88 });
      expect(component.runDiagnosticsText).toContain('computed (superseded): 88');
    });

    it('should carry an integrity block with the four-class accounting and the agreement figures', () => {
      ctx.monitor.activeRunDetail = buildDiagnosticsRun();
      const text = component.runDiagnosticsText;

      expect(text).toContain('--- INTEGRITY ---');
      expect(text).toContain('clean: 1, transport defects: 0, recovered: 0, harness limits: 1 (sums to 2)');
      expect(text).toContain('contested verdicts: 1, unevidenced deductions: 0, refuted claims: 0, contested critical errors: 0, contested accuracy deductions: not recorded, rubric contradicted by source: not recorded, dimension outliers: not recorded, re-assessed: 1');
      expect(text).toContain('unverified claims: 2');
      expect(text).toContain('4.3 mean abs delta');
      expect(text).toContain('over 2 of 2 answered, disagreements: 1');
      // Full coverage, so no conditioning caveat.
      expect(text).not.toContain('coverage selected by trigger');
    });

    it('should print the contested accuracy deduction count when recorded, zero included', () => {
      ctx.monitor.activeRunDetail = buildDiagnosticsRun({ contestedAccuracyDeductionAnswerCount: 2 });
      expect(component.runDiagnosticsText).toContain('contested critical errors: 0, contested accuracy deductions: 2, rubric contradicted by source: not recorded, dimension outliers: not recorded, re-assessed: 1');

      // Zero is a measurement on a harness-20 run; only null reads as not recorded.
      ctx.monitor.activeRunDetail = buildDiagnosticsRun({ contestedAccuracyDeductionAnswerCount: 0 });
      expect(component.runDiagnosticsText).toContain('contested accuracy deductions: 0,');

      ctx.monitor.activeRunDetail = buildDiagnosticsRun({ contestedAccuracyDeductionAnswerCount: null });
      expect(component.runDiagnosticsText).toContain('contested accuracy deductions: not recorded');
    });

    it('should print the rubric-contradicted count when recorded', () => {
      ctx.monitor.activeRunDetail = buildDiagnosticsRun({ rubricContradictedAnswerCount: 3 });
      expect(component.runDiagnosticsText).toContain('rubric contradicted by source: 3, dimension outliers:');
    });

    it('should caveat the agreement rate when coverage was selected by trigger', () => {
      ctx.monitor.activeRunDetail = buildDiagnosticsRun({ secondOpinionModeUsed: 1 });
      expect(component.runDiagnosticsText).toContain('coverage selected by trigger');
    });

    it('should extend each question line with the fields that explain its score', () => {
      ctx.monitor.activeRunDetail = buildDiagnosticsRun();
      const text = component.runDiagnosticsText;

      expect(text).toContain('band=Simple');
      expect(text).toContain('assessedDiff=25');
      expect(text).toContain('levels=3/4/5/6');
      expect(text).toContain('critical=false');
      expect(text).toContain('tools=34/35');
      expect(text).toContain('narration=3');
      expect(text).toContain('unverified=2');
      expect(text).toContain('flags=ContestedVerdict');
      expect(text).toContain('secondOpinion=85/All disagreed');
      expect(text).toContain('reassessed=42→60/Claude Opus 5');
      // Blocked calls come from the tool summary, because toolCallCount counts attempts.
      expect(text).toContain('tools=25/25 (3 blocked) exhausted');
    });

    it('should record whether the candidate prompt carried a game snapshot', () => {
      ctx.monitor.activeRunDetail = buildDiagnosticsRun({
        candidatePromptOptionsJson: '{"verboseMode":false,"enableToolUse":true,"hasGameSnapshot":true}'
      });
      expect(component.runDiagnosticsText).toContain('snapshot=true');

      ctx.monitor.activeRunDetail = buildDiagnosticsRun({
        candidatePromptOptionsJson: '{"verboseMode":false,"enableToolUse":true}'
      });
      expect(component.runDiagnosticsText).toContain('snapshot=false');
    });

    it('should record the candidate delivery probe and the board delivery per grading role', () => {
      ctx.monitor.activeRunDetail = buildDiagnosticsRun();
      expect(component.runDiagnosticsText).toContain('Candidate delivery probe: not recorded');
      expect(component.runDiagnosticsText).not.toContain('Board delivered');

      ctx.monitor.activeRunDetail = buildDiagnosticsRun({
        candidateDeliveryVerifiedAtUtc: '2026-09-18T07:11:00Z',
        boardDelivery: [
          { role: 'assessor', delivered: 18, total: 18, missingQuestions: [] },
          { role: 'second reader', delivered: 13, total: 14, missingQuestions: [6] },
          { role: 'claim verifier', delivered: 9, total: 9, missingQuestions: [] }
        ]
      });
      const text = component.runDiagnosticsText;

      expect(text).toContain('Candidate delivery probe: verified at 2026-09-18T07:11:00Z');
      expect(text).toContain('Board delivered — assessor 18 of 18 graded, second reader 13 of 14, claim verifier 9 of 9; synthesis: yes; difficulty assessment: digest (no map).');
      expect(text).toContain('Board not delivered — second reader: Q6');
    });

    it('should name the reference reader throughout the diagnostics of a panel run', () => {
      ctx.monitor.activeRunDetail = buildDiagnosticsRun({
        isPanelRun: true,
        secondOpinionModeUsed: 3,
        boardDelivery: [
          { role: 'assessor', delivered: 18, total: 18, missingQuestions: [] },
          { role: 'co-assessor', delivered: 18, total: 18, missingQuestions: [] },
          { role: 'reference reader', delivered: 18, total: 18, missingQuestions: [] },
          { role: 'claim verifier', delivered: 9, total: 9, missingQuestions: [] }
        ]
      });
      const text = component.runDiagnosticsText;

      expect(text).toContain('Reference reader: Claude Opus 5 (Anthropic / claude-opus-5)');
      expect(text).not.toContain('Second:');
      expect(text).toContain('Reference reader: mode All');
      expect(text).toContain('reference reader now:');
      expect(text).toContain('Answers with verified claims: 0, reference-read 2');
      expect(text).not.toContain('second-graded');
      expect(text).toContain('reference reader vs panel: 4.3 mean abs delta');
      expect(text).not.toMatch(/^agreement:/m);
      expect(text).toContain('sameProviderAcknowledged=n/a (panel run)');
      expect(text).toContain('Board delivered — assessor 18 of 18 graded, co-assessor 18 of 18, reference reader 18 of 18, claim verifier 9 of 9');
      expect(text).not.toMatch(/second opinion/i);
      expect(text).not.toContain('second reader');

      ctx.monitor.activeRunDetail = buildDiagnosticsRun({ secondOpinionModeUsed: 3 });
      const single = component.runDiagnosticsText;
      expect(single).toContain('Second reader: mode All');
      expect(single).toContain('second reader now:');
      expect(single).toContain('Second:   Claude Opus 5');
      expect(single).toContain('Answers with verified claims: 0, second-graded 2');
      expect(single).toContain('agreement: 4.3 mean abs delta');
      expect(single).toContain('sameProviderAcknowledged=false');
    });

    it('should print the panel line and member B\'s per-question figures', () => {
      const run = buildDiagnosticsRun({
        isPanelRun: true,
        coAssessorFinalScore: 88,
        assessorOnlyQualityIndex: 93,
        coAssessorOnlyQualityIndex: 90,
        panelMeanAbsDelta: 6.2,
        panelMeanSignedDelta: -3.5,
        panelIntraclassCorrelation: 0.8123,
        panelDisagreementCount: 1,
        panelCriticalErrorSplitCount: 0
      });
      run.answers[0] = {
        ...run.answers[0],
        panelQualityScore: 72.5,
        coAssessmentQualityScore: 85,
        coAssessmentJson: JSON.stringify({
          accuracyLevel: 5, completenessLevel: 4, concisenessLevel: 6, readabilityLevel: 5,
          unverifiedClaims: ['x'],
          flags: { contestedVerdict: true, readabilityFormOnly: true }
        }),
        assessorBoardChars: 12037,
        coAssessorBoardChars: 12040,
        secondOpinionBoardChars: null,
        verifierBoardChars: 900,
        claimVerificationJson: JSON.stringify([
          { claimIndex: 0, claim: 'c1', verdict: 'Supported', roles: ['unverifiedClaim'], raisedBy: ['A'] },
          { claimIndex: 1, claim: 'c2', verdict: 'Refuted', roles: ['unverifiedClaim'], raisedBy: ['A', 'B'] },
          { claimIndex: 2, claim: 'c3', verdict: 'Indeterminate', roles: ['unverifiedClaim'], raisedBy: ['B'] },
          { claimIndex: 3, claim: 'c4', verdict: 'Supported', roles: ['accusedQuote'], raisedBy: ['B'] }
        ])
      };
      run.answers[1] = {
        ...run.answers[1],
        panelQualityScore: 98,
        coAssessmentQualityScore: 97,
        coAssessmentJson: JSON.stringify({ flags: { contestedVerdict: true } })
      };
      ctx.monitor.activeRunDetail = run;
      const text = component.runDiagnosticsText;
      const lines = text.split('\n');

      expect(text).toContain('holistic: A 91, B 88, quality index: 94');
      expect(text).toContain('panel: A-alone 93, B-alone 90, mean |B−A| 6.2, mean B−A −3.5, ICC 0.81, disagreements 1, critical-error splits 0');
      const advisory = lines.findIndex(l => l.startsWith('advisory flags:'));
      expect(lines[advisory + 1]).toBe('member B flags: contested verdicts: 2, unevidenced deductions: 0, omission as accuracy: 0, '
        + 'out-of-rubric accuracy: 0, dimension outliers: 0, completeness out of scope: 0, readability form only: 1, '
        + 'contested critical errors: 0, contested accuracy deductions: 0, rubric contradicted by source: 0');
      // The union of both members' ordinary claims; the accused sentence is not one.
      expect(text).toContain('unverified claims: 3 (member A 1, member B 1, both 1)');

      const q1 = lines.find(l => l.startsWith('[Q1]'))!;
      expect(q1).toContain('panel=72.5 b=85 bLevels=5/4/6/5 band=');
      expect(q1).toContain('levels=3/4/5/6');
      expect(q1).toContain('boardChars=12037/12040/-/900');
      const q2 = lines.find(l => l.startsWith('[Q2]'))!;
      expect(q2).toContain('panel=98 b=97 band=');
      expect(q2).not.toContain('bLevels=');
      expect(q2).not.toContain('boardChars=');

      // Without a verified answer there is no union: each member's recorded count instead.
      run.answers[0] = { ...run.answers[0], claimVerificationJson: null };
      ctx.monitor.activeRunDetail = { ...run };
      expect(component.runDiagnosticsText).toContain('unverified claims: not verified (member A 2, member B 1 recorded)');

      // A single-assessor capture carries none of it.
      ctx.monitor.activeRunDetail = buildDiagnosticsRun();
      const single = component.runDiagnosticsText;
      expect(single).toContain('holistic: 91, quality index: 94');
      expect(single).not.toContain('panel:');
      expect(single).not.toContain('member B flags');
      expect(single).not.toContain('panel=');
      expect(single).toContain('unverified claims: 2');
    });

    it('should count either member\'s critical error on a panel run, with the member split', () => {
      const run = buildDiagnosticsRun({ isPanelRun: true });
      run.answers[0] = { ...run.answers[0], criticalError: true, coAssessmentCriticalError: true };
      run.answers[1] = { ...run.answers[1], criticalError: false, coAssessmentCriticalError: true };
      ctx.monitor.activeRunDetail = run;

      expect(component.runDiagnosticsText).toContain('critical errors: 2 (Q1, Q2; member A 1, member B 2), unverified claims:');

      // Outside a panel run member B's flag does not count and no split is printed.
      const single = buildDiagnosticsRun();
      single.answers[0] = { ...single.answers[0], criticalError: true };
      single.answers[1] = { ...single.answers[1], coAssessmentCriticalError: true };
      ctx.monitor.activeRunDetail = single;
      expect(component.runDiagnosticsText).toContain('critical errors: 1 (Q1), unverified claims:');
    });

    it('should append the re-verified stamp when the candidate delivery probe was re-checked before the re-run', () => {
      ctx.monitor.activeRunDetail = buildDiagnosticsRun({
        candidateDeliveryVerifiedAtUtc: '2026-09-18T07:11:00Z',
        rerunCandidateDeliveryVerifiedAtUtc: '2026-09-19T09:00:00Z'
      });
      expect(component.runDiagnosticsText).toContain(
        'Candidate delivery probe: verified at 2026-09-18T07:11:00Z; re-verified before the re-run at 2026-09-19T09:00:00Z'
      );
    });

    it('should record the board format when the run had a board', () => {
      const boardFactsCheck = { bulletCount: 1, checkedLiteralCount: 1, unquotedBulletCount: 0, unquotedBullets: [], missingLiterals: [] };

      ctx.monitor.activeRunDetail = buildDiagnosticsRun({ boardFactsCheck, gameSnapshotFormatVersionUsed: 3 });
      expect(component.runDiagnosticsText).toContain('Board format: 3');

      ctx.monitor.activeRunDetail = buildDiagnosticsRun({ boardFactsCheck, harnessVersion: '33', gameSnapshotFormatVersionUsed: null });
      expect(component.runDiagnosticsText).toContain('Board format: not stated');

      ctx.monitor.activeRunDetail = buildDiagnosticsRun({ boardFactsCheck, harnessVersion: '32', gameSnapshotFormatVersionUsed: null });
      expect(component.runDiagnosticsText).toContain('Board format: not recorded (before harness 33)');
    });

    it('should omit the board format line for a board-less run', () => {
      ctx.monitor.activeRunDetail = buildDiagnosticsRun();
      expect(component.runDiagnosticsText).not.toContain('Board format:');
    });

    it('should add a continuation line for a re-executed answer', () => {
      const run = buildDiagnosticsRun();
      run.answers[0] = {
        ...run.answers[0],
        rerunAtUtc: '2026-09-19T10:00:00Z',
        rerunOfStatus: 'ProviderError',
        rerunOfErrorMessage: 'HTTP 529: overloaded'
      };
      ctx.monitor.activeRunDetail = run;
      const text = component.runDiagnosticsText;

      expect(text).toContain('     re-executed at 2026-09-19T10:00:00Z: was ProviderError — HTTP 529: overloaded');
    });

    it('should extend a question line with its board characters and evidence-informed re-grade', () => {
      const run = buildDiagnosticsRun();
      run.answers[0] = {
        ...run.answers[0],
        assessorBoardChars: 12037,
        secondOpinionBoardChars: 12037,
        verifierBoardChars: null,
        evidenceInformedQualityScore: 74,
        evidenceInformedCriticalError: false,
        evidenceInformedJson: '{"withdrawn":["Accuracy deduction: peacefuls are never displaced"]}'
      };
      ctx.monitor.activeRunDetail = run;
      const text = component.runDiagnosticsText;

      expect(text).toContain('boardChars=12037/12037/-');
      expect(text).toContain('evidenceInformed=74 withdrew=1');
      // Neither is printed for an answer that recorded neither.
      expect(text.split('\n').find(l => l.startsWith('[Q2]'))).not.toContain('boardChars=');
      expect(component.evidenceInformedWithdrawn(run.answers[0])).toEqual(['Accuracy deduction: peacefuls are never displaced']);
      expect(component.evidenceInformedWithdrawn({ ...run.answers[0], evidenceInformedJson: 'not json' })).toEqual([]);
    });

    function diagnosticsOutcomeSummary(): any {
      return {
        correctCount: 1, partialCount: 0, incorrectCount: 0, notAttemptedCount: 1, noAnswerCount: 0,
        classifiedCount: 2,
        confirmedCriticalErrorCount: 0, unresolvedCriticalErrorCount: 0, overturnedCriticalErrorCount: 1,
        criticalErrorRate: 0, criticalErrorRateLow: 0, criticalErrorRateHigh: 0.658,
        correctWhenAttempted: 1, wrongInsteadOfAbstaining: 0,
        confirmedCriticalErrorQuestions: [], unresolvedCriticalErrorQuestions: [],
        overturnedCriticalErrorQuestions: [1], notAttemptedQuestions: [2]
      };
    }

    it('should add the critical-error resolution and outcome lines from scoring method 13', () => {
      const run = buildDiagnosticsRun({ scoringMethodVersion: 13, outcomeSummary: diagnosticsOutcomeSummary() });
      run.answers[0] = { ...run.answers[0], coAssessmentCriticalError: true, criticalErrorResolution: 'OverturnedByVerifier' };
      run.answers[1] = { ...run.answers[1], criticalErrorResolution: 'None', notAttempted: true, outcomeClass: 'NotAttempted' };
      ctx.monitor.activeRunDetail = run;
      const lines = component.runDiagnosticsText.split('\n');

      const critical = lines.findIndex(l => l.startsWith('critical errors:'));
      expect(lines[critical]).toContain('critical errors: 0, unverified claims:');
      expect(lines[critical + 1]).toBe('critical error resolution: confirmed 0, unresolved 0, overturned 1 (Q1); rate 0 % (95 % CI 0–66 %) of 2 classified');
      expect(lines[critical + 2]).toBe('answer outcomes: correct 1, partial 0, incorrect 0, not attempted 1 (Q2), no answer 0; '
        + 'correct when attempted 100 %, wrong instead of abstaining 0 %');
    });

    it('should leave the resolution and outcome lines out before scoring method 13', () => {
      ctx.monitor.activeRunDetail = buildDiagnosticsRun({ outcomeSummary: diagnosticsOutcomeSummary() });
      const text = component.runDiagnosticsText;

      expect(text).not.toContain('critical error resolution:');
      expect(text).not.toContain('answer outcomes:');
    });

    it('should name the snapshot in the prompt summary only when the run had one', () => {
      expect(component.candidatePromptSummaryOf({
        candidatePromptOptionsJson: '{"verboseMode":false,"enableToolUse":true,"hasGameSnapshot":true}'
      } as any)).toBe('Gameplay Help · concise (tools on) · snapshot');

      expect(component.candidatePromptSummaryOf({
        candidatePromptOptionsJson: '{"verboseMode":false,"enableToolUse":true}'
      } as any)).toBe('Gameplay Help · concise (tools on)');
    });
  });

  describe('assessor calibration panel', () => {
    it('should load calibrations when a run detail opens and clear them on close', () => {
      benchmarkServiceMock.getRun.mockReturnValue(of({
        id: 99, suiteName: 'Default Suite', status: 'Completed',
        testedModelDisplayNameUsed: 'M', testedModelProviderUsed: 'OpenAI', testedModelIdUsed: 'm',
        testedModelParallelExecutionModeUsed: 0,
        assessorModelDisplayNameUsed: 'A', assessorModelProviderUsed: 'Google', assessorModelIdUsed: 'a',
        startedAtUtc: '2026-09-03T06:52:00Z', totalAnswerDurationMs: 0, totalDurationMs: 0,
        scoringMethodVersion: 6, transportDefectAnswerCount: 0, advisoryFlagAnswerCount: 0,
        scrubbedArtifactAnswerCount: 0, difficultyFallbackUsed: false, speedMeasurementDegraded: false,
        maxParallelQuestionsUsed: 1, answeredQuestionCount: 0, totalQuestionCount: 0,
        assessmentParseFailed: false, totalInputTokens: 0, totalOutputTokens: 0,
        totalCacheReadTokens: 0, totalCacheCreationTokens: 0, answers: []
      } as any));
      benchmarkServiceMock.getCalibrations.mockReturnValue(of([
        {
          id: 1, benchmarkRunId: 99, assessorDisplayNameUsed: 'Claude Opus 5',
          assessorProviderUsed: 'Anthropic', assessorModelIdUsed: 'claude-opus-5',
          createdAtUtc: '2026-09-04T08:00:00Z', answerCount: 18, skippedAnswerCount: 0,
          meanAbsDelta: 5.5, disagreementCount: 2, inputTokens: 1000, outputTokens: 500,
          durationMs: 42000
        }
      ]));
      vi.spyOn(component.runDetailDialog.nativeElement, 'showModal').mockReturnValue(undefined);

      component.viewRunDetail(99);

      expect(benchmarkServiceMock.getCalibrations).toHaveBeenCalledWith(99);
      expect(component.calibrations.length).toBe(1);

      component.closeRunDetail();
      expect(component.calibrations.length).toBe(0);
    });

    it('should refuse to calibrate without an assessor selected', () => {
      component.calibrationAssessorConfigId = null;
      component.runCalibration(99);
      expect(benchmarkServiceMock.calibrateAssessor).not.toHaveBeenCalled();
    });

    it('should reload the list after a calibration completes', () => {
      benchmarkServiceMock.calibrateAssessor.mockReturnValue(of({ id: 2 } as any));
      benchmarkServiceMock.getCalibrations.mockReturnValue(of([]));
      component.calibrationAssessorConfigId = 3;

      component.runCalibration(99);

      expect(benchmarkServiceMock.calibrateAssessor).toHaveBeenCalledWith(99, 3);
      expect(benchmarkServiceMock.getCalibrations).toHaveBeenCalledWith(99);
      expect(component.calibrating).toBe(false);
    });
  });

  describe('trial re-assessment', () => {
    const answer: any = {
      id: 5, benchmarkRunId: 99, orderIndex: 3, questionText: 'Q3', answerText: 'a',
      difficulty: 1, status: 'Ok', assessmentStatus: 'Scored', durationMs: 1, modelTimeMs: 1,
      scrubbedArtifactCount: 0, answerFlags: 0, answerFlagNames: [], qualityScore: 60
    };

    beforeEach(() => {
      vi.spyOn(component.retryDialog.nativeElement, 'showModal').mockReturnValue(undefined);
      vi.spyOn(component.retryDialog.nativeElement, 'close').mockReturnValue(undefined);
      benchmarkServiceMock.trialReassessAnswer.mockReturnValue(of({ runId: 99 }));
    });

    it('should call the trial endpoint and never the one that replaces the verdict', fakeAsync(() => {
      component.openRetryDialog('trial', 99, answer);
      component.retryAssessorConfigId = 4;
      component.confirmRetry();

      expect(benchmarkServiceMock.trialReassessAnswer).toHaveBeenCalledWith(99, 5, 4, false);
      expect(benchmarkServiceMock.reassessAnswer).not.toHaveBeenCalled();

      component.stopDetailPolling();
      discardPeriodicTasks();
    }));

    it('should ask to replace an automatic second opinion, but not a previous trial', fakeAsync(() => {
      component.openRetryDialog('trial', 99, { ...answer, secondOpinionQualityScore: 80, secondOpinionTrigger: 'All' });
      component.retryAssessorConfigId = 4;
      component.confirmRetry();
      expect(vi.mocked(benchmarkServiceMock.trialReassessAnswer).mock.lastCall![3]).toBe(true);

      component.openRetryDialog('trial', 99, { ...answer, secondOpinionQualityScore: 80, secondOpinionTrigger: 'Manual' });
      component.retryAssessorConfigId = 4;
      component.confirmRetry();
      expect(vi.mocked(benchmarkServiceMock.trialReassessAnswer).mock.lastCall![3]).toBe(false);

      component.stopDetailPolling();
      discardPeriodicTasks();
    }));
  });

  describe('live run statistics, token formatting, and integrity notice', () => {
    it('should format token cards in run-stat strip with commas', () => {
      ctx.monitor.activeRunDetail = {
        id: 1,
        suiteName: 'Suite',
        status: 'Running',
        totalInputTokens: 1234567,
        totalOutputTokens: 8910,
        totalCacheReadTokens: 50000,
        totalCacheCreationTokens: 12000,
        answers: []
      } as any;

      ctx.refresh();

      const el: HTMLElement = fixture.nativeElement;
      const text = el.textContent || '';
      expect(text).toContain('1,234,567');
      expect(text).toContain('8,910');
      expect(text).toContain('50,000');
      expect(text).toContain('12,000');
    });

    it("should report Cache Creation as n/a for OpenAI when the provider reports cache reads but no cache creation", () => {
      ctx.monitor.activeRunDetail = {
        id: 1,
        suiteName: 'Suite',
        status: 'Running',
        testedModelProviderUsed: 'OpenAI',
        totalInputTokens: 0,
        totalOutputTokens: 0,
        totalCacheReadTokens: 50000,
        totalCacheCreationTokens: 0,
        answers: []
      } as any;

      expect(component.runCacheCreationUnreported).toBe(true);

      ctx.refresh();
      const el: HTMLElement = fixture.nativeElement;
      expect(el.textContent || '').toContain('n/a');
    });

    it('should report Cache Creation as a number for a provider that does report it', () => {
      ctx.monitor.activeRunDetail = {
        id: 1,
        suiteName: 'Suite',
        status: 'Running',
        testedModelProviderUsed: 'Anthropic',
        totalInputTokens: 0,
        totalOutputTokens: 0,
        totalCacheReadTokens: 50000,
        totalCacheCreationTokens: 12000,
        answers: []
      } as any;

      expect(component.runCacheCreationUnreported).toBe(false);

      ctx.refresh();
      const el: HTMLElement = fixture.nativeElement;
      expect(el.textContent || '').toContain('12,000');
    });

    it('should display candidate totals when run is running', () => {
      ctx.monitor.activeRunDetail = {
        id: 1,
        suiteName: 'Suite',
        status: 'Running',
        totalInputTokens: 15000,
        totalOutputTokens: 3000,
        totalCacheReadTokens: 0,
        totalCacheCreationTokens: 0,
        answers: [
          { orderIndex: 1, inputTokens: 5000, outputTokens: 1000 } as any,
          { orderIndex: 2, inputTokens: 10000, outputTokens: 2000 } as any
        ]
      } as any;

      ctx.refresh();

      const el: HTMLElement = fixture.nativeElement;
      const text = el.textContent || '';
      expect(text).toContain('15,000');
      expect(text).toContain('3,000');
    });

    it('should display claim verification failure clause in Run Integrity Notice when claimVerificationFailedAnswerCount > 0', () => {
      component.selectedRunDetail = {
        id: 1,
        suiteName: 'Suite',
        status: 'Completed',
        answers: [
          { orderIndex: 3, status: 'Ok', claimVerificationError: 'Model timeout after 120s' } as any
        ]
      } as any;

      expect(component.claimVerificationFailedAnswerCount).toBe(1);
      expect(component.claimVerificationFailedQuestionNumbers).toBe('3');

      fixture.detectChanges();

      const el: HTMLElement = fixture.nativeElement;
      const text = el.textContent || '';
      expect(text).toContain('Run Integrity Notice');
      expect(text).toContain('1 answer(s) had claim verification fail');
      expect(text).toContain('(question(s) 3)');
      expect(text).toContain('unverified claims were never checked');
    });

    it('should display contested critical error clause in Run Integrity Notice when the count is above zero', () => {
      component.selectedRunDetail = {
        id: 1,
        suiteName: 'Suite',
        status: 'Completed',
        contestedCriticalErrorAnswerCount: 1,
        answers: [
          { orderIndex: 1, status: 'Ok', criticalError: true, answerFlagNames: ['ContestedCriticalError'] } as any
        ]
      } as any;

      expect(component.contestedCriticalErrorAnswerCount).toBe(1);
      expect(component.contestedCriticalErrorQuestionNumbers).toBe('1');

      fixture.detectChanges();

      const el: HTMLElement = fixture.nativeElement;
      const text = el.textContent || '';
      expect(text).toContain('Run Integrity Notice');
      expect(text).toContain('1 answer(s) carry a contested critical error');
      expect(text).toContain('(question(s) 1)');
      expect(text).toContain('the cap stands and no score changed');
    });

    it('should display contested accuracy deduction clause in Run Integrity Notice when the count is above zero', () => {
      component.selectedRunDetail = {
        id: 1,
        suiteName: 'Suite',
        status: 'Completed',
        contestedAccuracyDeductionAnswerCount: 2,
        answers: [
          { orderIndex: 3, status: 'Ok', answerFlagNames: ['OutOfRubricAccuracyDeduction', 'ContestedAccuracyDeduction'] } as any,
          { orderIndex: 7, status: 'Ok', answerFlagNames: ['OutOfRubricAccuracyDeduction', 'ContestedAccuracyDeduction'] } as any
        ]
      } as any;

      expect(component.contestedAccuracyDeductionAnswerCount).toBe(2);
      expect(component.contestedAccuracyDeductionQuestionNumbers).toBe('3, 7');

      fixture.detectChanges();

      const el: HTMLElement = fixture.nativeElement;
      const text = el.textContent || '';
      expect(text).toContain('Run Integrity Notice');
      expect(text).toContain('2 answer(s) carry a contested accuracy deduction');
      expect(text).toContain('(question(s) 3, 7)');
      expect(text).toContain('the deduction stands and no score changed');
    });

    it('should display dimension outlier clause in Run Integrity Notice when the count is above zero', () => {
      component.selectedRunDetail = {
        id: 1,
        suiteName: 'Suite',
        status: 'Completed',
        dimensionOutlierAnswerCount: 2,
        answers: [
          { orderIndex: 3, status: 'Ok', answerFlagNames: ['DimensionOutlier'] } as any,
          { orderIndex: 7, status: 'Ok', answerFlagNames: ['DimensionOutlier'] } as any
        ]
      } as any;

      expect(component.dimensionOutlierAnswerCount).toBe(2);
      expect(component.dimensionOutlierQuestionNumbers).toBe('3, 7');

      fixture.detectChanges();

      const el: HTMLElement = fixture.nativeElement;
      const text = el.textContent || '';
      expect(text).toContain('Run Integrity Notice');
      expect(text).toContain('2 answer(s) with a dimension outlier');
      expect(text).toContain('(question(s) 3, 7)');
      expect(text).toContain('routed to a second reader and no score changed');
    });

    it('should omit the contested accuracy deduction clause when the count is null or zero', () => {
      for (const count of [null, 0]) {
        component.selectedRunDetail = {
          id: 1,
          suiteName: 'Suite',
          status: 'Completed',
          contestedAccuracyDeductionAnswerCount: count,
          answers: [{ orderIndex: 1, status: 'Ok', answerFlagNames: [] } as any]
        } as any;

        fixture.detectChanges();

        const text = (fixture.nativeElement as HTMLElement).textContent || '';
        expect(text).not.toContain('contested accuracy deduction');
      }
      expect(component.contestedAccuracyDeductionAnswerCount).toBe(0);
    });

    it('should render the tool call outcome split only when every answer with tool calls has recorded outcomes', () => {
      component.selectedRunDetail = {
        id: 1,
        suiteName: 'Suite',
        status: 'Completed',
        answers: [
          { orderIndex: 1, status: 'Ok', toolCallCount: 5, toolCallsSucceeded: 4, toolCallsFailed: 1, toolCallsRefused: 0 } as any,
          { orderIndex: 2, status: 'Ok', toolCallCount: 3, toolCallsSucceeded: 3, toolCallsFailed: 0, toolCallsRefused: 0 } as any
        ]
      } as any;

      expect(component.toolCallOutcomeSummary()).toBe('7 succeeded, 1 failed, 0 refused by budget');

      fixture.detectChanges();
      expect((fixture.nativeElement as HTMLElement).textContent || '')
        .toContain('7 succeeded, 1 failed, 0 refused by budget');

      // One answer whose outcomes predate the per-call record makes the run-level sum a figure
      // that omits it silently, so the line is withheld entirely rather than under-reported.
      component.selectedRunDetail = {
        id: 1,
        suiteName: 'Suite',
        status: 'Completed',
        answers: [
          { orderIndex: 1, status: 'Ok', toolCallCount: 5, toolCallsSucceeded: 4, toolCallsFailed: 1, toolCallsRefused: 0 } as any,
          { orderIndex: 2, status: 'Ok', toolCallCount: 3 } as any
        ]
      } as any;

      expect(component.toolCallOutcomeSummary()).toBeNull();
    });

    it('should render the tool rounds line from the per-call rows the dialog has loaded', () => {
      component.selectedRunDetail = {
        id: 1,
        suiteName: 'Suite',
        status: 'Completed',
        answers: [
          { orderIndex: 1, status: 'Ok', toolCallCount: 4 } as any
        ]
      } as any;

      expect(component.toolRoundsSummary()).toBeNull();

      component.toolCallsByAnswer.set(1, [
        { id: 1, iterationIndex: 0, name: 'wiki_search' } as any,
        { id: 2, iterationIndex: 0, name: 'wiki_view' } as any,
        { id: 3, iterationIndex: 1, name: 'source_code_search' } as any,
        { id: 4, iterationIndex: 1, name: 'source_code_view' } as any
      ]);

      const summary = component.toolRoundsSummary();
      expect(summary).toContain('2.0 tool round(s) per answer on average');
      expect(summary).toContain('2.0 call(s) per round');
      expect(summary).toContain('over 1 answer(s) loaded');

      fixture.detectChanges();
      expect((fixture.nativeElement as HTMLElement).textContent || '')
        .toContain('2.0 tool round(s) per answer on average');
    });

    it('should display second-opinion failure clause and suppress no-trigger clause when secondOpinionFailedAnswerCount > 0', () => {
      component.selectedRunDetail = {
        id: 1,
        suiteName: 'Suite',
        status: 'Completed',
        secondOpinionAssessorModelConfigurationId: 4,
        secondOpinionGradedAnswerCount: 0,
        answers: [
          { orderIndex: 7, status: 'Ok', secondOpinionError: '429 Rate limited' } as any
        ]
      } as any;

      expect(component.secondOpinionFailedAnswerCount).toBe(1);
      expect(component.secondOpinionFailedQuestionNumbers).toBe('7');
      expect(component.secondOpinionSelectedButUnused).toBe(true);

      fixture.detectChanges();

      const el: HTMLElement = fixture.nativeElement;
      const text = el.textContent || '';
      expect(text).toContain('Run Integrity Notice');
      expect(text).toContain('1 answer(s) met a trigger but the second-reader call failed');
      expect(text).toContain('(question(s) 7)');
      expect(text).not.toContain('A second reader was selected but no answer met a trigger');
    });

    it('should display no-trigger clause when secondOpinionSelectedButUnused is true and secondOpinionFailedAnswerCount is 0', () => {
      component.selectedRunDetail = {
        id: 1,
        suiteName: 'Suite',
        status: 'Completed',
        secondOpinionAssessorModelConfigurationId: 4,
        secondOpinionGradedAnswerCount: 0,
        answers: [
          { orderIndex: 1, status: 'Ok' } as any
        ]
      } as any;

      expect(component.secondOpinionFailedAnswerCount).toBe(0);
      expect(component.secondOpinionSelectedButUnused).toBe(true);

      fixture.detectChanges();

      const el: HTMLElement = fixture.nativeElement;
      const text = el.textContent || '';
      expect(text).toContain('Run Integrity Notice');
      expect(text).toContain('A second reader was selected but no answer met a trigger');
      expect(text).not.toContain('second-reader call failed');
    });

    it('should measure agreement over the completed second opinions when some failed but others completed', () => {
      component.selectedRunDetail = {
        id: 1,
        suiteName: 'Suite',
        status: 'Completed',
        secondOpinionAssessorModelConfigurationId: 4,
        secondOpinionGradedAnswerCount: 3,
        answers: [
          { orderIndex: 3, status: 'Ok', secondOpinionError: '429 Rate limited' } as any,
          { orderIndex: 1, status: 'Ok' } as any,
          { orderIndex: 2, status: 'Ok' } as any,
          { orderIndex: 4, status: 'Ok' } as any
        ]
      } as any;

      expect(component.secondOpinionFailedAnswerCount).toBe(1);
      expect(component.secondOpinionCompletedAnswerCount).toBe(3);

      fixture.detectChanges();

      const el: HTMLElement = fixture.nativeElement;
      const text = el.textContent || '';
      expect(text).toContain('1 answer(s) met a trigger but the second-reader call failed');
      expect(text).toContain('(question(s) 3)');
      expect(text).toContain('grader agreement is measured over the 3 answer(s) whose second reading completed');
      expect(text).not.toContain('grader agreement is not measured for this run');
    });
  });

  describe('Harness Version 11 fidelity features', () => {
    it('should compute indexConfidenceLabel correctly from qualityIndexStandardError', () => {
      component.selectedRunDetail = {
        id: 1,
        qualityIndexStandardError: 3.06
      } as any;
      expect(component.indexConfidenceLabel).toBe('± 6');

      component.selectedRunDetail = {
        id: 1,
        qualityIndexStandardError: null
      } as any;
      expect(component.indexConfidenceLabel).toBe('');

      component.selectedRunDetail = {
        id: 1,
        qualityIndexStandardError: 0
      } as any;
      expect(component.indexConfidenceLabel).toBe('');
    });

    it('should compute secondOpinionBlindLabel correctly', () => {
      component.selectedRunDetail = {
        id: 1,
        secondOpinionBlindUsed: true
      } as any;
      expect(component.secondOpinionBlindLabel).toBe('blind');

      component.selectedRunDetail = {
        id: 1,
        secondOpinionBlindUsed: false
      } as any;
      expect(component.secondOpinionBlindLabel).toBe('anchored');
    });

    it('should compute disputeVerificationLabel for single and multiple disputed answers with verification', () => {
      // Single disputed answer with verified claims
      component.selectedRunDetail = {
        id: 1,
        answers: [
          {
            orderIndex: 1,
            secondOpinionDisagreed: true,
            claimsSupportedCount: 3,
            claimsRefutedCount: 0,
            claimsIndeterminateCount: 0
          } as any,
          {
            orderIndex: 2,
            secondOpinionDisagreed: false
          } as any
        ]
      } as any;

      expect(component.disputeVerificationLabel).toBe('Claim verification for Q1: 3 supported, 0 refuted, 0 indeterminate.');

      // Multiple disputed answers with verified claims
      component.selectedRunDetail = {
        id: 1,
        answers: [
          {
            orderIndex: 1,
            secondOpinionDisagreed: true,
            claimsSupportedCount: 2,
            claimsRefutedCount: 1,
            claimsIndeterminateCount: 0
          } as any,
          {
            orderIndex: 3,
            secondOpinionDisagreed: true,
            claimsSupportedCount: 1,
            claimsRefutedCount: 0,
            claimsIndeterminateCount: 1
          } as any
        ]
      } as any;

      expect(component.disputeVerificationLabel).toBe('Claim verification for disputed answer(s): 3 supported, 1 refuted, 1 indeterminate.');

      // Disputed answer with no claim verification
      component.selectedRunDetail = {
        id: 1,
        answers: [
          {
            orderIndex: 1,
            secondOpinionDisagreed: true
          } as any
        ]
      } as any;

      expect(component.disputeVerificationLabel).toBe('');
    });

    it('should append the accused-sentence counts only when they are non-zero', () => {
      // Zero claim counts, but the accused-sentence check found something.
      component.selectedRunDetail = {
        id: 1,
        answers: [
          {
            orderIndex: 3,
            secondOpinionDisagreed: true,
            claimsSupportedCount: 0,
            claimsRefutedCount: 0,
            claimsIndeterminateCount: 0,
            accusedSupportedCount: 1,
            accusedRefutedCount: 1,
            accusedIndeterminateCount: 0
          } as any
        ]
      } as any;

      expect(component.disputeVerificationLabel).toBe(
        'Claim verification for Q3: 0 supported, 0 refuted, 0 indeterminate; accused sentences: 1 supported, 1 refuted, 0 indeterminate.'
      );

      // Accused counts all zero: no accused part, but the answer still counts as verified.
      component.selectedRunDetail = {
        id: 1,
        answers: [
          {
            orderIndex: 3,
            secondOpinionDisagreed: true,
            claimsSupportedCount: 0,
            claimsRefutedCount: 0,
            claimsIndeterminateCount: 0,
            accusedSupportedCount: 0,
            accusedRefutedCount: 0,
            accusedIndeterminateCount: 0
          } as any
        ]
      } as any;

      expect(component.disputeVerificationLabel).toBe('Claim verification for Q3: 0 supported, 0 refuted, 0 indeterminate.');
    });

    it('should compute omissionAsAccuracyAnswerCount and omissionAsAccuracyQuestionNumbers', () => {
      component.selectedRunDetail = {
        id: 1,
        omissionAsAccuracyAnswerCount: 2,
        answers: [
          { orderIndex: 1, answerFlagNames: ['OmissionAsAccuracy'] } as any,
          { orderIndex: 4, answerFlagNames: ['UnevidencedDeduction'] } as any,
          { orderIndex: 10, answerFlagNames: ['OmissionAsAccuracy', 'RefutedClaim'] } as any
        ]
      } as any;

      expect(component.omissionAsAccuracyAnswerCount).toBe(2);
      expect(component.omissionAsAccuracyQuestionNumbers).toBe('1, 10');
    });

    it('should map secondOpinionTriggerLabel for RefutedClaim and OmissionAsAccuracy', () => {
      expect(component.secondOpinionTriggerLabel('RefutedClaim')).toBe('refuted claim');
      expect(component.secondOpinionTriggerLabel('OmissionAsAccuracy')).toBe('omission docked as accuracy');
    });

    it('should map every second-opinion trigger to words, never to its raw name', () => {
      // SecondOpinionTriggers in Overseer/Services/Benchmarking/BenchmarkService.cs.
      const triggers = [
        'CriticalError', 'RefutedClaim', 'ContestedVerdict', 'OutOfRubricAccuracy',
        'UnevidencedDeduction', 'OmissionAsAccuracy', 'DimensionOutlier', 'UnverifiedClaims',
        'BelowThreshold', 'Outlier', 'All', 'Manual', 'Sample'
      ];
      for (const trigger of triggers) {
        const label = component.secondOpinionTriggerLabel(trigger);
        expect(label).not.toBe(trigger);
        expect(label.includes(' ') || /^[a-z]/.test(label), trigger).toBe(true);
      }
    });

    it('should render 95% CI score note under Intelligence Index tile and blind label in Assessor Agreement', () => {
      component.selectedRunDetail = {
        id: 1,
        status: 'Completed',
        suiteName: 'Suite',
        qualityIndex: 91,
        qualityIndexStandardError: 3.06,
        secondOpinionGradedAnswerCount: 2,
        secondOpinionBlindUsed: true,
        answers: []
      } as any;

      fixture.detectChanges();

      const el: HTMLElement = fixture.nativeElement;
      const text = el.textContent || '';
      expect(text).toContain('± 6 (95% CI)');
      expect(text).toContain('blind');
    });

    it('should render omission-as-accuracy clause and dispute claim verification in Run Integrity Notice', () => {
      component.selectedRunDetail = {
        id: 1,
        status: 'Completed',
        suiteName: 'Suite',
        omissionAsAccuracyAnswerCount: 1,
        answers: [
          {
            orderIndex: 1,
            status: 'Ok',
            secondOpinionDisagreed: true,
            claimsSupportedCount: 3,
            claimsRefutedCount: 0,
            claimsIndeterminateCount: 0,
            answerFlagNames: ['OmissionAsAccuracy']
          } as any
        ]
      } as any;

      fixture.detectChanges();

      const el: HTMLElement = fixture.nativeElement;
      const text = el.textContent || '';
      expect(text).toContain('Run Integrity Notice');
      expect(text).toContain('1 answer(s) carry an omission docked as accuracy');
      expect(text).toContain('(question(s) 1)');
      expect(text).toContain('Claim verification for Q1: 3 supported, 0 refuted, 0 indeterminate.');
    });

    it('should default candidateVerboseMode to false and reflect appropriate hint', () => {
      expect(ctx.launcher.candidateVerboseMode).toBe(false);
      expect(ctx.runTab().candidateResponseStyleHint).toContain('production chat');

      ctx.launcher.candidateVerboseMode = true;
      expect(ctx.runTab().candidateResponseStyleHint).toContain('Only Accuracy stays comparable');
    });

    it('should include verboseMode in startRun payload', () => {
      benchmarkServiceMock.startRun.mockReturnValue(of({ runId: 101 } as any));
      ctx.launcher.selectedSuiteId = 1;
      ctx.launcher.testedConfigId = 10;
      ctx.launcher.assessorConfigId = 20;
      ctx.launcher.candidateVerboseMode = true;

      ctx.runTab().startBenchmark();

      expect(benchmarkServiceMock.startRun).toHaveBeenCalledWith(expect.objectContaining({
        verboseMode: true
      }));
    });

    it('should send allowSourceCodeReferences, false by default', () => {
      benchmarkServiceMock.startRun.mockReturnValue(of({ runId: 101 } as any));
      ctx.launcher.selectedSuiteId = 1;
      ctx.launcher.testedConfigId = 10;
      ctx.launcher.assessorConfigId = 20;

      ctx.runTab().startBenchmark();
      expect(vi.mocked(benchmarkServiceMock.startRun).mock.lastCall![0].allowSourceCodeReferences).toBe(false);

      ctx.launcher.candidateAllowSourceCodeReferences = true;
      ctx.runTab().startBenchmark();
      expect(vi.mocked(benchmarkServiceMock.startRun).mock.lastCall![0].allowSourceCodeReferences).toBe(true);
    });

    it('should identify failed claim verifications and trigger retry', () => {
      component.selectedRunDetail = {
        id: 55,
        status: 'Completed',
        answers: [
          { orderIndex: 1, claimVerificationError: null },
          { orderIndex: 2, claimVerificationError: 'Model timeout' }
        ]
      } as any;

      expect(component.claimVerificationFailedAnswerCount).toBe(1);
      expect(component.claimVerificationFailedQuestionNumbers).toBe('2');

      benchmarkServiceMock.retryClaimVerification.mockReturnValue(of({ runId: 55 } as any));
      benchmarkServiceMock.getRun.mockReturnValue(of({ id: 55, answers: [] } as any));

      component.openRetryDialog('claim-verification', 55);
      expect(component.retryScope).toBe('claim-verification');
      expect(component.retryRunId).toBe(55);

      component.confirmRetry();
      expect(benchmarkServiceMock.retryClaimVerification).toHaveBeenCalledWith(55, expect.anything());
      component.stopDetailPolling();
    });
  });

  // ---------------------------------------------------------------------------
  // U1. Tool routing, budget pressure, ungrounded Advanced answers and the
  // source-share correlations, all computed client-side from answer DTOs the run
  // detail dialog already holds. No endpoint backs any of it.
  // ---------------------------------------------------------------------------
  describe('run answer analytics (U1)', () => {
    /** Only the fields the four analytics read; everything else is filler the DTO demands. */
    function answer(overrides: Partial<BenchmarkRunAnswerDto> = {}): BenchmarkRunAnswerDto {
      return {
        id: 1,
        benchmarkRunId: 14,
        orderIndex: 1,
        questionText: 'Q',
        difficulty: 'Intermediate',
        assessedDifficulty: 55,
        answerText: 'A',
        status: 'Ok',
        durationMs: 20000,
        modelTimeMs: 15000,
        toolTimeMs: 5000,
        toolCallCount: 0,
        toolCallBudgetUsed: 45,
        toolBudgetExhausted: false,
        toolCallsBlocked: 0,
        toolCallSummary: null,
        qualityScore: 70,
        ...overrides
      } as BenchmarkRunAnswerDto;
    }

    function withAnswers(answers: BenchmarkRunAnswerDto[]): void {
      component.selectedRunDetail = { id: 14, answers } as any;
    }

    // --- Tool routing families ---

    it('should count tool calls by family and report each family share of the run', () => {
      withAnswers([
        answer({ orderIndex: 1, toolCallSummary: 'source_code_search×6, wiki_search×4' }),
        answer({
          orderIndex: 2,
          toolCallSummary: 'source_code_view×4, wiki_view×2, monster_lookup×3, get_knowledge_article×1'
        })
      ]);

      const rows = component.toolRoutingFamilies();

      expect(component.totalRoutedToolCalls()).toBe(20);
      expect(rows.map(r => r.label)).toEqual(['Source Code', 'Wiki', 'Structured Lookup', 'Knowledge Base']);
      expect(rows.map(r => r.count)).toEqual([10, 6, 3, 1]);
      expect(rows.map(r => r.sharePercentage)).toEqual([50, 30, 15, 5]);
    });

    it('should omit families with no calls, exactly as the report table does', () => {
      withAnswers([answer({ toolCallSummary: 'wiki_search×2' })]);

      const rows = component.toolRoutingFamilies();

      expect(rows.length).toBe(1);
      expect(rows[0].label).toBe('Wiki');
      expect(rows[0].sharePercentage).toBe(100);
    });

    it('should classify an unlisted tool as Other rather than dropping its calls', () => {
      withAnswers([answer({ toolCallSummary: 'wiki_search×3, some_new_tool×1' })]);

      const rows = component.toolRoutingFamilies();

      // Dropping it would make the shares sum to 100 % of a total that is not the run's.
      expect(component.totalRoutedToolCalls()).toBe(4);
      expect(rows.find(r => r.family === 'Other')?.count).toBe(1);
    });

    it('should route only answers the run actually produced', () => {
      withAnswers([
        answer({ orderIndex: 1, toolCallSummary: 'wiki_search×2' }),
        answer({ orderIndex: 2, status: 'Failed', toolCallSummary: 'source_code_search×9' })
      ]);

      expect(component.totalRoutedToolCalls()).toBe(2);
      expect(component.toolRoutingFamilies().map(r => r.family)).toEqual(['Wiki']);
    });

    // --- Budget pressure ---

    it('should select answers at or above 90 % of budget that never reached it', () => {
      withAnswers([
        answer({ orderIndex: 11, toolCallCount: 41, toolCallBudgetUsed: 45 }),
        answer({ orderIndex: 16, toolCallCount: 43, toolCallBudgetUsed: 45 }),
        answer({ orderIndex: 1, toolCallCount: 10, toolCallBudgetUsed: 45 })
      ]);

      expect(component.budgetPressuredAnswers().map(a => a.orderIndex)).toEqual([11, 16]);
    });

    it('should treat the 90 % threshold as inclusive and the budget itself as exclusive', () => {
      withAnswers([
        // 40.5 is the threshold: 40 is below it, 41 is not.
        answer({ orderIndex: 1, toolCallCount: 40, toolCallBudgetUsed: 45 }),
        answer({ orderIndex: 2, toolCallCount: 41, toolCallBudgetUsed: 45 }),
        // Reaching the budget is exhaustion, which the exhausted list already reports.
        answer({ orderIndex: 3, toolCallCount: 45, toolCallBudgetUsed: 45 })
      ]);

      expect(component.budgetPressuredAnswers().map(a => a.orderIndex)).toEqual([2]);
    });

    it('should exclude exhausted answers and answers with blocked calls', () => {
      withAnswers([
        answer({ orderIndex: 1, toolCallCount: 44, toolCallBudgetUsed: 45, toolBudgetExhausted: true }),
        // An answer whose calls were blocked is exhausted, not pressured.
        answer({ orderIndex: 2, toolCallCount: 44, toolCallBudgetUsed: 45, toolCallsBlocked: 2 }),
        answer({ orderIndex: 3, toolCallCount: 44, toolCallBudgetUsed: 45 })
      ]);

      expect(component.budgetPressuredAnswers().map(a => a.orderIndex)).toEqual([3]);
    });

    // --- Ungrounded Advanced answers ---

    it('should select Advanced answers produced with one tool call or fewer', () => {
      withAnswers([
        answer({ orderIndex: 14, assessedDifficulty: 85, toolCallCount: 1 }),
        answer({ orderIndex: 15, assessedDifficulty: 85, toolCallCount: 0 }),
        answer({ orderIndex: 16, assessedDifficulty: 85, toolCallCount: 5 }),
        answer({ orderIndex: 17, assessedDifficulty: 55, toolCallCount: 0 })
      ]);

      expect(component.ungroundedAdvancedAnswers().map(a => a.orderIndex)).toEqual([14, 15]);
    });

    it('should band an unrated answer by its authored band midpoint', () => {
      withAnswers([
        // No assessed difficulty: authored Advanced falls back to 85, which bands Advanced.
        answer({ orderIndex: 1, assessedDifficulty: null, difficulty: 'Advanced', toolCallCount: 1 }),
        answer({ orderIndex: 2, assessedDifficulty: null, difficulty: 'Intermediate', toolCallCount: 1 })
      ]);

      expect(component.ungroundedAdvancedAnswers().map(a => a.orderIndex)).toEqual([1]);
    });

    // --- Source-share correlations ---

    it('should pair the two correlations over one sample of scored answers', () => {
      withAnswers([
        answer({ orderIndex: 1, toolCallSummary: 'wiki_search×4', modelTimeMs: 10000, qualityScore: 50 }),
        answer({ orderIndex: 2, toolCallSummary: 'source_code_search×2, wiki_search×2', modelTimeMs: 20000, qualityScore: 70 }),
        answer({ orderIndex: 3, toolCallSummary: 'source_code_search×4', modelTimeMs: 30000, qualityScore: 50 })
      ]);

      const correlations = component.sourceShareCorrelations();

      // Shares 0, 0.5, 1 against times 10k, 20k, 30k are exactly linear.
      expect(correlations.modelTimeR).toBeCloseTo(1, 10);
      // The same shares against qualities 50, 70, 50 have zero covariance: this is run 14's shape,
      // where source calls bought time and not accuracy.
      expect(correlations.qualityR).toBeCloseTo(0, 10);
      expect(correlations.sampleSize).toBe(3);
    });

    it('should drop an unscored answer from both correlations, not from one', () => {
      withAnswers([
        answer({ orderIndex: 1, toolCallSummary: 'wiki_search×4', modelTimeMs: 10000, qualityScore: 50 }),
        answer({ orderIndex: 2, toolCallSummary: 'source_code_search×2, wiki_search×2', modelTimeMs: 20000, qualityScore: 70 }),
        answer({ orderIndex: 3, toolCallSummary: 'source_code_search×4', modelTimeMs: 30000, qualityScore: 50 }),
        answer({ orderIndex: 4, toolCallSummary: 'source_code_search×4', modelTimeMs: 99000, qualityScore: null })
      ]);

      const correlations = component.sourceShareCorrelations();

      expect(correlations.sampleSize).toBe(3);
      expect(correlations.modelTimeR).toBeCloseTo(1, 10);
    });

    it('should fall back to duration minus tool time when model time was never recorded', () => {
      withAnswers([
        answer({ orderIndex: 1, toolCallSummary: 'wiki_search×4', modelTimeMs: 0, durationMs: 15000, toolTimeMs: 5000, qualityScore: 50 }),
        answer({ orderIndex: 2, toolCallSummary: 'source_code_search×2, wiki_search×2', modelTimeMs: 0, durationMs: 25000, toolTimeMs: 5000, qualityScore: 70 }),
        answer({ orderIndex: 3, toolCallSummary: 'source_code_search×4', modelTimeMs: 0, durationMs: 35000, toolTimeMs: 5000, qualityScore: 50 })
      ]);

      const correlations = component.sourceShareCorrelations();

      // 10k, 20k, 30k again: leaving these out would shrink the sample silently instead.
      expect(correlations.sampleSize).toBe(3);
      expect(correlations.modelTimeR).toBeCloseTo(1, 10);
    });

    it('should report no coefficient where r is undefined rather than calling it zero', () => {
      withAnswers([answer({ toolCallSummary: 'wiki_search×2', qualityScore: 50 })]);

      const correlations = component.sourceShareCorrelations();

      expect(correlations.sampleSize).toBe(1);
      expect(correlations.modelTimeR).toBeNull();
      expect(correlations.qualityR).toBeNull();
    });
  });
  describe('Model Pricing Feature', () => {
    // U3. Four decimals throughout, so a sub-cent run resolves to something other than $0.00 and
    // every cost in the admin benchmark views lines up on the decimal point.
    it('should format every cost with four decimals', () => {
      expect(component.formatCostAmount(2.5312)).toBe('$2.5312');
      expect(component.formatCostAmount(1)).toBe('$1.0000');
      expect(component.formatCostAmount(0.9912)).toBe('$0.9912');
      expect(component.formatCostAmount(0.0004)).toBe('$0.0004');
    });

    it('should render a missing or non-finite cost as a dash rather than $0', () => {
      expect(component.formatCostAmount(null)).toBe('-');
      expect(component.formatCostAmount(undefined)).toBe('-');
      expect(component.formatCostAmount(Number.NaN)).toBe('-');
    });

    it('should render the Total Cost card with the incomplete-pricing marker when pricingIncomplete is true', () => {
      component.activeSubTab = 'run';
      component.selectedRunDetail = {
        id: 1,
        benchmarkSuiteId: 1,
        suiteName: 'Test',
        status: 2,
        estimatedCost: 1.2345,
        pricingSource: 'catalog',
        pricingIncomplete: true,
        answers: []
      } as any;
      fixture.detectChanges();

      const cards = Array.from(fixture.nativeElement.querySelectorAll('.score-card')) as HTMLElement[];
      const card = cards.find(c => c.querySelector('.score-label')?.textContent?.trim() === 'Total Cost');
      expect(card).toBeTruthy();

      const content = card!.textContent?.replace(/\s+/g, ' ').trim() || '';
      expect(content).toContain('$1.2345');
      expect(content).toContain('no single total — a role has no price');

      const marker = card!.querySelector('.degraded-tag');
      expect(marker).toBeTruthy();
      expect(marker?.textContent?.trim()).toBe('*');
    });

    // H5. The summary row carries the same pair the run history Cost cell does: the model under
    // test first, the catalog total beside it.
    it('should render a Candidate Cost card with the candidate figure and its share of the total', () => {
      component.activeSubTab = 'run';
      component.selectedRunDetail = {
        id: 1,
        benchmarkSuiteId: 1,
        suiteName: 'Test',
        status: 2,
        estimatedCost: 3.03,
        estimatedCandidateCost: 2.30,
        estimatedAssessorCost: 0.50,
        estimatedSecondOpinionCost: 0.10,
        estimatedVerifierCost: 0.10,
        estimatedSynthesisCost: 0.03,
        pricingSource: 'catalog',
        answers: []
      } as any;
      fixture.detectChanges();

      const cards = Array.from(fixture.nativeElement.querySelectorAll('.score-card')) as HTMLElement[];
      const card = cards.find(c => c.querySelector('.score-label')?.textContent?.trim() === 'Candidate Cost');
      expect(card).toBeTruthy();

      const content = card!.textContent?.replace(/\s+/g, ' ').trim() || '';
      expect(content).toContain('$2.3000');
      expect(content).toContain('76 % of estimated total');
      // No answer rows, so no per-question figure.
      expect(content).not.toContain('per question');
      expect(card!.querySelector('app-key-figure-card-actions')).toBeTruthy();
    });

    it('should add the candidate cost per question asked, over every answer row', () => {
      component.activeSubTab = 'run';
      component.selectedRunDetail = {
        id: 1,
        benchmarkSuiteId: 1,
        suiteName: 'Test',
        status: 2,
        estimatedCost: 1.0,
        estimatedCandidateCost: 0.3978,
        estimatedAssessorCost: 0.6022,
        pricingSource: 'catalog',
        answers: Array.from({ length: 18 }, (_, i) => ({ id: i + 1, orderIndex: i + 1, status: i === 0 ? 'Error' : 'Ok' }))
      } as any;

      expect(component.candidateCostPerQuestionLabel(component.selectedRunDetail!)).toBe('$0.0221 per question · 18 asked');
    });

    it('should word the Total Cost note by pricing source', () => {
      const run = (overrides: any) => ({ id: 1, answers: [], ...overrides }) as any;
      expect(component.totalCostNote(run({ pricingSource: 'catalog' }))).toBe('estimated · all roles · catalog prices');
      expect(component.totalCostNote(run({ pricingSource: 'custom' }))).toBe('estimated · all roles · custom prices');
      expect(component.totalCostNote(run({ pricingSource: 'mixed' }))).toBe('estimated · all roles · catalog and custom prices');
      expect(component.totalCostNote(run({ pricingSource: null }))).toBe('pricing unknown');
      expect(component.totalCostNote(run({ pricingSource: 'mixed', pricingIncomplete: true }))).toBe('no single total — a role has no price');
    });

    it('should give the share of the priced roles when pricing is incomplete', () => {
      const run = {
        id: 1,
        estimatedCost: null,
        estimatedCandidateCost: 1.0,
        estimatedAssessorCost: 3.0,
        pricingIncomplete: true,
        answers: []
      } as any;
      expect(component.candidateCostShareLabel(run)).toBe('25 % of the priced roles');
    });

    // H4. The card and the cost panel below it apportion the same five role amounts, in the same
    // order, by the same largest-remainder rule, so they can never disagree on the whole percent.
    it('should print the same apportioned percent as the cost panel below it', () => {
      component.activeSubTab = 'run';
      component.selectedRunDetail = {
        id: 1,
        benchmarkSuiteId: 1,
        suiteName: 'Test',
        status: 2,
        estimatedCost: 2.7247,
        estimatedCandidateCost: 0.6926,
        estimatedAssessorCost: 0.80,
        estimatedSecondOpinionCost: 0.10,
        estimatedVerifierCost: 1.10,
        estimatedSynthesisCost: 0.0321,
        estimatedGradingCost: 2.0321,
        pricingSource: 'Anthropic API',
        answers: []
      } as any;
      fixture.detectChanges();

      // Exact shares: candidate 25.42, assessor 29.36, second opinion 3.67, claim verifier 40.37,
      // synthesis 1.18 -- the floors sum to 98, so the two largest remainders (second opinion,
      // then candidate) each get one extra point, giving the candidate 26 %.
      const cards = Array.from(fixture.nativeElement.querySelectorAll('.score-card')) as HTMLElement[];
      const card = cards.find(c => c.querySelector('.score-label')?.textContent?.trim() === 'Candidate Cost');
      expect(card!.textContent).toContain('26 % of estimated total');

      const panelShares = Array.from(
        (fixture.nativeElement as HTMLElement).querySelectorAll<HTMLElement>(
          '.gh-cost-role:not(.gh-cost-role--subtotal) .gh-cost-role__share'
        )
      ).map(el => el.textContent?.trim());
      expect(panelShares[0]).toBe('26%');
    });

    it('should omit the Candidate Cost card when the candidate cost was never recorded', () => {
      component.activeSubTab = 'run';
      component.selectedRunDetail = {
        id: 1,
        benchmarkSuiteId: 1,
        suiteName: 'Test',
        status: 2,
        estimatedCost: 3.03,
        estimatedCandidateCost: null,
        pricingSource: 'Anthropic API',
        answers: []
      } as any;
      fixture.detectChanges();

      const labels = (Array.from(fixture.nativeElement.querySelectorAll('.score-card')) as HTMLElement[])
        .map(c => c.querySelector('.score-label')?.textContent?.trim());
      expect(labels).not.toContain('Candidate Cost');
      expect(labels).toContain('Total Cost');
    });

    it('should say pricing incomplete in words under the run history cost, with no asterisk and no title', () => {
      component.activeSubTab = 'history';
      // The History tab loads the history when it renders, so the run is set after that load.
      fixture.detectChanges();
      ctx.workspace.historyRuns = [
        {
          id: 10,
          suiteName: 'Test Suite',
          status: 2,
          estimatedCost: 0.50,
          pricingIncomplete: true
        } as any
      ];
      ctx.refresh();

      const metric = fixture.nativeElement.querySelector('.rh-card .rh-metric[data-metric="cost"]') as HTMLElement;
      expect(metric).toBeTruthy();
      expect(metric.querySelector('dt')?.textContent?.trim()).toBe('Cost');

      const lines = Array.from(metric.querySelectorAll('dd > span')).map(span => span.textContent?.trim());
      expect(lines).toEqual(['$0.5000', 'catalog $0.5000', 'pricing incomplete']);
      expect(metric.textContent).not.toContain('*');
      expect(metric.querySelector('[title]')).toBeNull();
      expect(metric.querySelector('.degraded-tag')).toBeNull();
    });
  });
});
