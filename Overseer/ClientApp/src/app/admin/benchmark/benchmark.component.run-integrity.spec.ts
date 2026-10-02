import type { Mock, MockedObject } from "vitest";
import { ComponentFixture, TestBed, fakeAsync, tick, discardPeriodicTasks } from '@angular/core/testing';
import { of } from 'rxjs';
import { AdminBenchmarkComponent } from './benchmark.component';
import { AdminBenchmarkService, BenchmarkRunReportDocumentsStatus } from '../../services/admin-benchmark.service';
import { BenchmarkCompletionSoundService } from '../../services/benchmark-completion-sound.service';
import { BenchmarkBackgroundActivityService } from '../../services/benchmark-background-activity.service';
import { clearStoredState, createAdminBenchmarkFixture } from './benchmark.component.testing';

describe('AdminBenchmarkComponent', () => {
  let component: AdminBenchmarkComponent;
  let fixture: ComponentFixture<AdminBenchmarkComponent>;
  let benchmarkServiceMock: MockedObject<AdminBenchmarkService>;

  beforeEach(clearStoredState);

  afterEach(clearStoredState);

  beforeEach(async () => {
    ({ component, fixture, benchmarkServiceMock } = await createAdminBenchmarkFixture());
  });

  describe('run integrity accounting', () => {
    /**
     * A finished run detail with no integrity problems. Each test raises exactly the counters
     * it is about, so a clause appearing in the banner can only have come from that counter.
     */
    function buildCompletedRun(overrides: any = {}): any {
      return {
        id: 55,
        benchmarkSuiteId: 1,
        suiteName: 'Default Suite',
        testedModelDisplayNameUsed: 'Test Model',
        testedModelProviderUsed: 'OpenAI',
        testedModelIdUsed: 'gpt-5.6-luna',
        testedModelParallelExecutionModeUsed: 0,
        assessorModelDisplayNameUsed: 'Test Assessor',
        assessorModelProviderUsed: 'Google',
        assessorModelIdUsed: 'gemini-3.7-flash',
        startedByUserName: 'admin',
        status: 'Completed',
        startedAtUtc: '2026-09-03T06:52:00Z',
        completedAtUtc: '2026-09-03T07:10:00Z',
        totalAnswerDurationMs: 900000,
        scoringProfileName: 'Standard Intelligence Index (Default)',
        scoringProfileId: 1,
        scoringMethodVersion: 4,
        harnessVersion: '3',
        degradedAnswerCount: 0,
        toolStarvedAnswerCount: 0,
        transportDefectAnswerCount: 0,
        advisoryFlagAnswerCount: 0,
        scrubbedArtifactAnswerCount: 0,
        toolOverheadMs: 0,
        difficultyFallbackUsed: false,
        speedMeasurementDegraded: false,
        maxParallelQuestionsUsed: 1,
        answeredQuestionCount: 18,
        totalQuestionCount: 18,
        assessmentParseFailed: false,
        totalInputTokens: 0,
        totalOutputTokens: 0,
        totalCacheReadTokens: 0,
        totalCacheCreationTokens: 0,
        totalDurationMs: 900000,
        errorMessage: null,
        answers: [],
        ...overrides
      };
    }

    function buildScoredAnswer(orderIndex: number, overrides: any = {}): any {
      return {
        id: 200 + orderIndex,
        benchmarkRunId: 55,
        orderIndex,
        questionText: `Question ${orderIndex}`,
        difficulty: 2,
        assessedDifficulty: 50,
        answerText: `Answer ${orderIndex}`,
        status: 'Ok',
        assessmentStatus: 'Scored',
        durationMs: 48800,
        modelTimeMs: 48800,
        toolCallCount: 4,
        scrubbedArtifactCount: 0,
        answerFlags: 0,
        answerFlagNames: [],
        ...overrides
      };
    }

    function integrityNoticeText(): string {
      const notices: HTMLElement[] = Array.from(fixture.nativeElement.querySelectorAll('.alert-heading'));
      const heading = notices.find(n => (n.textContent || '').includes('Run Integrity Notice'));
      if (!heading) return '';
      return (heading.parentElement?.querySelector('.alert-body') as HTMLElement)?.textContent ?? '';
    }

    it('should describe transport defects, recoveries, harness limits, and advisory flags as separate causes', () => {
      component.selectedRunDetail = buildCompletedRun({
        transportDefectAnswerCount: 4,
        recoveredAnswerCount: 5,
        toolStarvedAnswerCount: 3,
        advisoryFlagAnswerCount: 6
      });
      fixture.detectChanges();

      const text = integrityNoticeText().replace(/\s+/g, ' ').trim();
      expect(text).toContain('4 answer(s) were corrupted beyond recovery (empty or truncated).');
      expect(text).toContain('5 answer(s) were repaired by the harness before grading');
      expect(text).toContain('3 answer(s) hit a configured harness limit (tool budget).');
      expect(text).toContain('6 answer(s) carry advisory flags.');
      // The wording this replaced described tool-starved answers as one of "empty, harness
      // artifacts, or truncated", which they are not — and counted a repaired answer as a
      // defect, which is what made a healthy run read as errored.
      expect(text).not.toContain('degraded answer(s)');
      expect(text).not.toContain('tool-starved');
      expect(text).not.toContain('empty, harness artifacts, or truncated');
    });

    it('should report a disputed assessment in the integrity notice and badge the answer', () => {
      component.selectedRunDetail = buildCompletedRun({
        answers: [
          buildScoredAnswer(1, {
            qualityScore: 25,
            criticalError: true,
            secondOpinionQualityScore: 72,
            secondOpinionCriticalError: false,
            secondOpinionByModelDisplayNameUsed: 'Second Assessor',
            secondOpinionDisagreed: true
          })
        ]
      });
      fixture.detectChanges();

      const text = integrityNoticeText().replace(/\s+/g, ' ').trim();
      expect(text).toContain('1 answer(s) were read by a second reader that reached a materially different verdict. The assessor\'s verdict is what scored.');
      expect(fixture.nativeElement.querySelector('.disputed-badge')).toBeTruthy();
    });

    function questionCard(orderIndex: number): HTMLElement | undefined {
      return (Array.from(fixture.nativeElement.querySelectorAll('.question-detail-card')) as HTMLElement[])
        .find(card => card.querySelector('.q-number')?.textContent?.trim() === `Q${orderIndex}`);
    }

    function criticalBadgeTitle(orderIndex: number): string | null {
      return questionCard(orderIndex)?.querySelector('.critical-badge')?.getAttribute('title') ?? null;
    }

    it('should state the critical-error resolution in the badge title from scoring method 13', () => {
      component.selectedRunDetail = buildCompletedRun({
        scoringMethodVersion: 13,
        isPanelRun: true,
        answers: [
          buildScoredAnswer(1, { criticalError: true, coAssessmentCriticalError: true, criticalErrorResolution: 'Agreed' }),
          buildScoredAnswer(2, { criticalError: true, coAssessmentCriticalError: false, criticalErrorResolution: 'UpheldByVerifier' }),
          buildScoredAnswer(3, { criticalError: true, coAssessmentCriticalError: false, criticalErrorResolution: 'Unresolved' }),
          buildScoredAnswer(4, { criticalError: false, coAssessmentCriticalError: true, criticalErrorResolution: 'OverturnedByVerifier' }),
          buildScoredAnswer(5, { criticalError: false, coAssessmentCriticalError: false, criticalErrorResolution: 'None' })
        ]
      });
      fixture.detectChanges();

      expect(criticalBadgeTitle(1)).toBe('Critical error confirmed by both panel members — Quality capped at 25');
      expect(criticalBadgeTitle(2)).toBe('Critical error flagged by one panel member and upheld by the claim verifier — both members capped at 25');
      expect(criticalBadgeTitle(3)).toBe('Critical error flagged by one panel member only, with no verifier ruling — split unresolved, the two members averaged');
      expect(criticalBadgeTitle(4)).toBe('Critical error flagged by one panel member and overturned by the claim verifier — not applied');
      expect(questionCard(5)).toBeTruthy();
      expect(criticalBadgeTitle(5)).toBeNull();
    });

    it('should name a single assessor\'s critical error from scoring method 13', () => {
      component.selectedRunDetail = buildCompletedRun({
        scoringMethodVersion: 13,
        answers: [buildScoredAnswer(1, { criticalError: true, criticalErrorResolution: 'SingleAssessor' })]
      });
      fixture.detectChanges();

      expect(criticalBadgeTitle(1)).toBe('Critical error flagged by the assessor — Quality capped at 25');
    });

    it('should keep the critical-error badge title and its flag rule before scoring method 13', () => {
      component.selectedRunDetail = buildCompletedRun({
        scoringMethodVersion: 12,
        answers: [
          buildScoredAnswer(1, { criticalError: true, criticalErrorResolution: 'OverturnedByVerifier' }),
          buildScoredAnswer(2, { criticalError: false, criticalErrorResolution: 'Agreed' })
        ]
      });
      fixture.detectChanges();

      expect(criticalBadgeTitle(1)).toBe('Critical error cap applied (Quality capped at 25)');
      expect(criticalBadgeTitle(2)).toBeNull();
    });

    it('should badge a not-attempted answer', () => {
      component.selectedRunDetail = buildCompletedRun({
        scoringMethodVersion: 13,
        answers: [
          buildScoredAnswer(1, { notAttempted: true, outcomeClass: 'NotAttempted', criticalErrorResolution: 'None' }),
          buildScoredAnswer(2, { notAttempted: false, outcomeClass: 'Correct', criticalErrorResolution: 'None' })
        ]
      });
      fixture.detectChanges();

      const badge = questionCard(1)?.querySelector('.badge-flag-notattempted') as HTMLElement;
      expect(badge).toBeTruthy();
      expect(badge.textContent!.trim()).toBe('NOT ATTEMPTED');
      expect(badge.getAttribute('title'))
        .toBe('The answer says it could not find or verify the answer; scored at least the profile\'s not-attempted score');
      expect(questionCard(2)?.querySelector('.badge-flag-notattempted')).toBeNull();
    });

    function missingQuote(orderIndex: number, literal: string): any {
      return { questionId: 100 + orderIndex, orderIndex, literal, lineExcerpt: `- "${literal}"` };
    }

    it('should list each missing board quote by its 1-based question number, even with no other integrity cause', () => {
      component.selectedRunDetail = buildCompletedRun({
        boardFactsCheck: {
          bulletCount: 40, checkedLiteralCount: 38, unquotedBulletCount: 0, unquotedBullets: [],
          missingLiterals: [missingQuote(1, 'a +2 elven mithril-coat'), missingQuote(15, 'Dlvl:12')]
        }
      });
      fixture.detectChanges();

      const notice = fixture.nativeElement.querySelector('.board-facts-notice') as HTMLElement;
      expect(notice).toBeTruthy();
      const items = Array.from(notice.querySelectorAll('li')).map(li => (li.textContent || '').trim());
      expect(items).toEqual(['Q1: "a +2 elven mithril-coat"', 'Q15: "Dlvl:12"']);
    });

    it('should cap the missing board quote list at 20 and say how many more there are', () => {
      component.selectedRunDetail = buildCompletedRun({
        boardFactsCheck: {
          bulletCount: 40, checkedLiteralCount: 40, unquotedBulletCount: 0, unquotedBullets: [],
          missingLiterals: Array.from({ length: 23 }, (_, i) => missingQuote(i + 1, `quote ${i + 1}`))
        }
      });
      fixture.detectChanges();

      const items = Array.from(fixture.nativeElement.querySelectorAll('.board-facts-notice li'))
        .map(li => ((li as HTMLElement).textContent || '').trim());
      expect(items.length).toBe(21);
      expect(items[19]).toBe('Q20: "quote 20"');
      expect(items[20]).toBe('and 3 more');
    });

    it('should show no board quote notice when the check found every quote', () => {
      component.selectedRunDetail = buildCompletedRun({
        boardFactsCheck: { bulletCount: 4, checkedLiteralCount: 4, unquotedBulletCount: 0, unquotedBullets: [], missingLiterals: [] }
      });
      fixture.detectChanges();

      expect(fixture.nativeElement.querySelector('.board-facts-notice')).toBeNull();
    });

    it('should show the assessor evidence and the second opinion as plain text when expanded', () => {
      component.selectedRunDetail = buildCompletedRun({
        answers: [
          buildScoredAnswer(1, {
            accuracyLevel: 2,
            completenessLevel: 2,
            concisenessLevel: 4,
            readabilityLevel: 5,
            qualityScore: 42,
            assessmentEvidenceJson: JSON.stringify({
              accuracy: 'Rubric point 3: Exceptional/Elite give -4/-8 AC.',
              completeness: 'Not in rubric: from my own knowledge of the source.',
              criticalErrorDemoted: false
            }),
            secondOpinionQualityScore: 44,
            secondOpinionCriticalError: false,
            secondOpinionByModelDisplayNameUsed: 'Second Assessor',
            secondOpinionDisagreed: false
          })
        ]
      });
      component.expandedQuestions.add(1);
      fixture.detectChanges();

      const evidence = fixture.nativeElement.querySelector('.assessment-evidence') as HTMLElement;
      expect(evidence).toBeTruthy();
      expect(evidence.textContent).toContain('Rubric point 3');
      expect(evidence.textContent).toContain('Not in rubric');

      const secondOpinion = fixture.nativeElement.querySelector('.second-opinion-box') as HTMLElement;
      expect(secondOpinion).toBeTruthy();
      expect(secondOpinion.textContent).toContain('Second Assessor');
      expect(secondOpinion.textContent).toContain('agrees with');
      expect(secondOpinion.classList).not.toContain('is-disputed');
    });

    it('should render only the harness limit clause when a configured cap was the only cause', () => {
      component.selectedRunDetail = buildCompletedRun({ toolStarvedAnswerCount: 3 });
      fixture.detectChanges();

      const text = integrityNoticeText().replace(/\s+/g, ' ').trim();
      expect(text).toContain('3 answer(s) hit a configured harness limit (tool budget).');
      expect(text).not.toContain('transport defects');
      expect(text).not.toContain('advisory flags');
    });

    it('should render no integrity notice when every cause is zero', () => {
      component.selectedRunDetail = buildCompletedRun();
      fixture.detectChanges();

      expect(integrityNoticeText()).toBe('');
    });

    it('should treat empty answers and the Empty, HarnessArtifacts and Truncated bits as transport defects', () => {
      expect(component.hasTransportDefect(buildScoredAnswer(1, { status: 'EmptyAnswer' }))).toBe(true);
      expect(component.hasTransportDefect(buildScoredAnswer(1, { status: 5 }))).toBe(true);
      expect(component.hasTransportDefect(buildScoredAnswer(1, { answerFlags: 1 }))).toBe(true);
      expect(component.hasTransportDefect(buildScoredAnswer(1, { answerFlags: 2 }))).toBe(true);
      expect(component.hasTransportDefect(buildScoredAnswer(1, { answerFlags: 4 }))).toBe(true);
      expect(component.hasTransportDefect(buildScoredAnswer(1, { answerFlags: 6 }))).toBe(true);
    });

    it('should treat a tool budget cap as a harness limit and never as a transport defect', () => {
      const capped = buildScoredAnswer(1, { toolBudgetExhausted: true, toolCallCount: 25, toolCallBudgetUsed: 25 });

      expect(component.hasHarnessLimit(capped)).toBe(true);
      expect(component.hasTransportDefect(capped)).toBe(false);
      expect(component.hasAdvisoryFlag(capped)).toBe(false);
      expect(component.hasHarnessLimit(buildScoredAnswer(1))).toBe(false);
    });

    it('should count an answer carrying only advisory flags as clean', () => {
      const bleed = buildScoredAnswer(1, { answerFlags: 8, answerFlagNames: ['ReasoningBleed'] });
      const repeated = buildScoredAnswer(2, { answerFlags: 16, answerFlagNames: ['RepeatedFragments'] });
      const both = buildScoredAnswer(3, { answerFlags: 24, answerFlagNames: ['ReasoningBleed', 'RepeatedFragments'] });

      for (const answer of [bleed, repeated, both]) {
        expect(component.hasAdvisoryFlag(answer)).toBe(true);
        expect(component.hasTransportDefect(answer)).toBe(false);
        expect(component.hasHarnessLimit(answer)).toBe(false);
      }

      // Advisory flags may overlap a defect without masking it.
      const overlapping = buildScoredAnswer(4, { answerFlags: 2 | 8 });
      expect(component.hasTransportDefect(overlapping)).toBe(true);
      expect(component.hasAdvisoryFlag(overlapping)).toBe(true);

      const clean = buildScoredAnswer(5);
      expect(component.hasAdvisoryFlag(clean)).toBe(false);
      expect(component.hasTransportDefect(clean)).toBe(false);
    });

    it('should flag an AnswerFramingOpener-only answer as advisory and list its question number', () => {
      const framed = buildScoredAnswer(7, { answerFlags: 1024, answerFlagNames: ['AnswerFramingOpener'] });

      expect(component.hasAdvisoryFlag(framed)).toBe(true);
      expect(component.hasTransportDefect(framed)).toBe(false);
      expect(component.hasHarnessLimit(framed)).toBe(false);

      component.selectedRunDetail = buildCompletedRun({ answers: [framed] });
      fixture.detectChanges();

      expect(component.advisoryFlagQuestionNumbers).toBe('7');
    });

    it('should name only the advisory flags as advisory', () => {
      expect(component.isAdvisoryFlagName('ReasoningBleed')).toBe(true);
      expect(component.isAdvisoryFlagName('RepeatedFragments')).toBe(true);
      expect(component.isAdvisoryFlagName('ContestedVerdict')).toBe(true);
      expect(component.isAdvisoryFlagName('UnevidencedDeduction')).toBe(true);
      expect(component.isAdvisoryFlagName('RefutedClaim')).toBe(true);
      expect(component.isAdvisoryFlagName('OutOfRubricAccuracyDeduction')).toBe(true);
      expect(component.isAdvisoryFlagName('AnswerFramingOpener')).toBe(true);
      expect(component.isAdvisoryFlagName('ContestedCriticalError')).toBe(true);
      expect(component.isAdvisoryFlagName('ContestedAccuracyDeduction')).toBe(true);
      expect(component.isAdvisoryFlagName('DimensionOutlier')).toBe(true);
      expect(component.isAdvisoryFlagName('HarnessArtifacts')).toBe(false);
      expect(component.isAdvisoryFlagName('Truncated')).toBe(false);
      expect(component.isAdvisoryFlagName('Empty')).toBe(false);
    });

    it('should count and name the critical error answers alongside the advisory ones', () => {
      component.selectedRunDetail = buildCompletedRun({
        advisoryFlagAnswerCount: 2,
        answers: [
          buildScoredAnswer(1, { qualityScore: 25, criticalError: true, rawQualityScore: 60 }),
          buildScoredAnswer(2),
          buildScoredAnswer(3, { qualityScore: 25, criticalError: true, rawQualityScore: 70, answerFlags: 8, answerFlagNames: ['ReasoningBleed'] }),
          buildScoredAnswer(4),
          buildScoredAnswer(5, { answerFlags: 16, answerFlagNames: ['RepeatedFragments'] })
        ]
      });
      fixture.detectChanges();

      expect(component.criticalErrorAnswerCount).toBe(2);
      expect(component.criticalErrorQuestionNumbers).toBe('1, 3');
      expect(component.advisoryFlagQuestionNumbers).toBe('3, 5');

      const text = integrityNoticeText().replace(/\s+/g, ' ').trim();
      expect(text).toContain('2 answer(s) flagged with a critical error (question(s) 1, 3).');
      expect(text).toContain('2 answer(s) carry advisory flags (question(s) 3, 5).');
    });

    it('should raise the integrity notice for a critical error that is the only cause', () => {
      component.selectedRunDetail = buildCompletedRun({
        answers: [buildScoredAnswer(1, { qualityScore: 25, criticalError: true, rawQualityScore: 60 })]
      });
      fixture.detectChanges();

      const text = integrityNoticeText().replace(/\s+/g, ' ').trim();
      expect(text).toContain('1 answer(s) flagged with a critical error (question(s) 1).');
      expect(text).toContain('read this count, not the index, for this failure mode.');
      expect(text).not.toContain('advisory flags');
    });

    it('should add the cap-binding clause only when it differs from the flagged count', () => {
      component.selectedRunDetail = buildCompletedRun({
        answers: [
          // Flagged and genuinely capped: the raw score was above the ceiling the ceiling pulled it down to.
          buildScoredAnswer(1, { qualityScore: 25, rawQualityScore: 60, criticalError: true }),
          // Flagged but not capped in effect: the raw score already sat at or below the ceiling.
          buildScoredAnswer(2, { qualityScore: 25, rawQualityScore: 25, criticalError: true })
        ]
      });
      fixture.detectChanges();

      expect(component.criticalErrorAnswerCount).toBe(2);
      expect(component.criticalErrorCapBindingCount).toBe(1);

      const text = integrityNoticeText().replace(/\s+/g, ' ').trim();
      expect(text).toContain('2 answer(s) flagged with a critical error (question(s) 1, 2), of which 1 had their score lowered by the cap.');
    });

    it('should omit the cap-binding clause when every flagged answer was actually capped', () => {
      component.selectedRunDetail = buildCompletedRun({
        answers: [
          buildScoredAnswer(1, { qualityScore: 25, rawQualityScore: 60, criticalError: true }),
          buildScoredAnswer(2, { qualityScore: 25, rawQualityScore: 70, criticalError: true })
        ]
      });
      fixture.detectChanges();

      expect(component.criticalErrorAnswerCount).toBe(2);
      expect(component.criticalErrorCapBindingCount).toBe(2);

      const text = integrityNoticeText().replace(/\s+/g, ' ').trim();
      expect(text).toContain('2 answer(s) flagged with a critical error (question(s) 1, 2).');
      expect(text).not.toContain('had their score lowered by the cap');
    });

    it('should name no questions when there is no run detail or no flagged answer', () => {
      component.selectedRunDetail = null;
      expect(component.criticalErrorAnswerCount).toBe(0);
      expect(component.criticalErrorQuestionNumbers).toBe('');
      expect(component.advisoryFlagQuestionNumbers).toBe('');

      component.selectedRunDetail = buildCompletedRun({ answers: [buildScoredAnswer(1), buildScoredAnswer(2)] });
      fixture.detectChanges();

      expect(component.criticalErrorAnswerCount).toBe(0);
      expect(component.criticalErrorQuestionNumbers).toBe('');
      expect(component.advisoryFlagQuestionNumbers).toBe('');
      expect(integrityNoticeText()).toBe('');
    });

    it('should count member B\'s critical errors on a panel run, as the report and the key figure do', () => {
      component.selectedRunDetail = buildCompletedRun({
        isPanelRun: true,
        answers: [
          buildScoredAnswer(1, { qualityScore: 25, rawQualityScore: 60, criticalError: true, coAssessmentQualityScore: 80, panelQualityScore: 52.5 }),
          buildScoredAnswer(2, { qualityScore: 80, coAssessmentQualityScore: 25, coAssessmentRawQualityScore: 70, coAssessmentCriticalError: true, panelQualityScore: 52.5 }),
          buildScoredAnswer(3, { qualityScore: 80, coAssessmentQualityScore: 82, panelQualityScore: 81 })
        ]
      });
      fixture.detectChanges();

      expect(component.criticalErrorAnswerCount).toBe(2);
      expect(component.criticalErrorAnswerCount).toBe(component.keyFigureCriticalErrorAnswers.length);
      expect(component.criticalErrorQuestionNumbers).toBe('1, 2');
      expect(component.criticalErrorQuestionIndexes).toEqual([1, 2]);
      expect(component.criticalErrorMemberACount).toBe(1);
      expect(component.criticalErrorMemberBCount).toBe(1);
      expect(component.criticalErrorCapBindingCount).toBe(2);

      const text = integrityNoticeText().replace(/\s+/g, ' ').trim();
      expect(text).toContain('2 answer(s) flagged with a critical error (question(s) 1, 2; member A flagged 1, member B 1).');
    });

    it('should raise the integrity notice on a panel run whose only critical error is member B\'s', () => {
      component.selectedRunDetail = buildCompletedRun({
        isPanelRun: true,
        answers: [
          buildScoredAnswer(1, { qualityScore: 80, coAssessmentQualityScore: 25, coAssessmentRawQualityScore: 60, coAssessmentCriticalError: true, panelQualityScore: 52.5 }),
          buildScoredAnswer(2, { qualityScore: 80, coAssessmentQualityScore: 82, panelQualityScore: 81 })
        ]
      });
      fixture.detectChanges();

      expect(component.hasRunIntegrityNotice).toBe(true);
      const text = integrityNoticeText().replace(/\s+/g, ' ').trim();
      expect(text).toContain('1 answer(s) flagged with a critical error (question(s) 1; member A flagged 0, member B 1).');
    });

    it('should ignore member B\'s critical error outside a panel run', () => {
      component.selectedRunDetail = buildCompletedRun({
        answers: [buildScoredAnswer(1, { qualityScore: 80, coAssessmentCriticalError: true })]
      });
      fixture.detectChanges();

      expect(component.criticalErrorAnswerCount).toBe(0);
      expect(component.hasRunIntegrityNotice).toBe(false);
      expect(integrityNoticeText()).toBe('');
    });

    it('should count the cap as binding on a panel run by the panel scores', () => {
      component.selectedRunDetail = buildCompletedRun({
        isPanelRun: true,
        answers: [
          // Member B alone capped a 60 to 25: the panel raw score 70 exceeds the panel score 52.5.
          buildScoredAnswer(1, { qualityScore: 80, coAssessmentQualityScore: 25, coAssessmentRawQualityScore: 60, coAssessmentCriticalError: true, panelQualityScore: 52.5 }),
          // Member A flagged an answer already at the ceiling: raw and published panel scores agree.
          buildScoredAnswer(2, { qualityScore: 25, rawQualityScore: 25, criticalError: true, coAssessmentQualityScore: 30, panelQualityScore: 27.5 })
        ]
      });
      fixture.detectChanges();

      expect(component.criticalErrorAnswerCount).toBe(2);
      expect(component.criticalErrorCapBindingCount).toBe(1);

      const text = integrityNoticeText().replace(/\s+/g, ' ').trim();
      expect(text).toContain('2 answer(s) flagged with a critical error (question(s) 1, 2; member A flagged 1, member B 1), of which 1 had their panel score lowered by the cap.');
    });

    it('should count an answer both panel members flagged once, and in both member counts', () => {
      component.selectedRunDetail = buildCompletedRun({
        isPanelRun: true,
        answers: [
          buildScoredAnswer(1, { qualityScore: 25, rawQualityScore: 60, criticalError: true, coAssessmentQualityScore: 25, coAssessmentRawQualityScore: 70, coAssessmentCriticalError: true, panelQualityScore: 25 })
        ]
      });
      fixture.detectChanges();

      expect(component.criticalErrorAnswerCount).toBe(1);
      expect(component.criticalErrorCapBindingCount).toBe(1);
      const text = integrityNoticeText().replace(/\s+/g, ' ').trim();
      expect(text).toContain('1 answer(s) flagged with a critical error (question(s) 1; member A flagged 1, member B 1).');
    });

    it('should format CompletedWithLimits from both the numeric and the string status', () => {
      expect(component.formatStatus(6)).toBe('CompletedWithLimits');
      expect(component.formatStatus('CompletedWithLimits')).toBe('CompletedWithLimits');
    });

    it('should mute advisory flag badges and show the effective tool budget and scrubbed count', () => {
      component.selectedRunDetail = buildCompletedRun({
        answers: [
          buildScoredAnswer(1, {
            toolBudgetExhausted: true,
            toolCallCount: 25,
            toolCallBudgetUsed: 25,
            scrubbedArtifactCount: 2,
            answerFlags: 2 | 8,
            answerFlagNames: ['HarnessArtifacts', 'ReasoningBleed']
          })
        ]
      });
      fixture.detectChanges();

      const starved = fixture.nativeElement.querySelector('.badge-starved') as HTMLElement;
      expect(starved).toBeTruthy();
      expect(starved.textContent!.replace(/\s+/g, ' ').trim()).toBe('Budget Hit (25/25)');

      const scrubbed = fixture.nativeElement.querySelector('.badge-scrubbed') as HTMLElement;
      expect(scrubbed).toBeTruthy();
      expect(scrubbed.getAttribute('title')).toContain('2 transport artifact block(s)');

      const defectBadge = fixture.nativeElement.querySelector('.badge-flag-harnessartifacts') as HTMLElement;
      expect(defectBadge).toBeTruthy();
      expect(defectBadge.classList).not.toContain('badge-flag-advisory');

      const advisoryBadge = fixture.nativeElement.querySelector('.badge-flag-reasoningbleed') as HTMLElement;
      expect(advisoryBadge).toBeTruthy();
      expect(advisoryBadge.classList).toContain('badge-flag-advisory');
    });

    it('should label a contested accuracy deduction badge "contested deduction" and mute it as advisory', () => {
      component.selectedRunDetail = buildCompletedRun({
        answers: [
          buildScoredAnswer(1, {
            answerFlags: 512 | 4096,
            answerFlagNames: ['OutOfRubricAccuracyDeduction', 'ContestedAccuracyDeduction']
          })
        ]
      });
      fixture.detectChanges();

      const badge = fixture.nativeElement.querySelector('.badge-flag-contestedaccuracydeduction') as HTMLElement;
      expect(badge).toBeTruthy();
      expect(badge.textContent!.trim()).toBe('contested deduction');
      expect(badge.classList).toContain('badge-flag-advisory');
      expect(badge.getAttribute('title')).toContain('refuted');
      expect(badge.getAttribute('title')).toContain('the deduction stands');
    });

    it('should label a dimension outlier badge "dimension outlier" and mute it as advisory', () => {
      component.selectedRunDetail = buildCompletedRun({
        answers: [
          buildScoredAnswer(1, {
            answerFlags: 8192,
            answerFlagNames: ['DimensionOutlier']
          })
        ]
      });
      fixture.detectChanges();

      const badge = fixture.nativeElement.querySelector('.badge-flag-dimensionoutlier') as HTMLElement;
      expect(badge).toBeTruthy();
      expect(badge.textContent!.trim()).toBe('dimension outlier');
      expect(badge.classList).toContain('badge-flag-advisory');
      expect(badge.getAttribute('title')).toContain('Routed to a second reader');
    });

    it('should toggle the removed transport artifacts block per answer', () => {
      expect(component.expandedArtifacts.has(1)).toBe(false);

      component.toggleArtifact(1);
      expect(component.expandedArtifacts.has(1)).toBe(true);
      expect(component.expandedArtifacts.has(2)).toBe(false);

      component.toggleArtifact(1);
      expect(component.expandedArtifacts.has(1)).toBe(false);
    });

    it('should reveal the scrubbed artifact text as plain text only once expanded', () => {
      component.selectedRunDetail = buildCompletedRun({
        answers: [
          buildScoredAnswer(1, {
            scrubbedArtifactCount: 1,
            scrubbedArtifactText: 'to=multi_tool_use.parallel <em>{"tool_uses":[]}</em>'
          })
        ]
      });
      component.expandedQuestions.add(1);
      fixture.detectChanges();

      expect(fixture.nativeElement.querySelector('.artifact-box')).toBeNull();
      const toggle = fixture.nativeElement.querySelector('.question-card-body .btn-link') as HTMLButtonElement;
      expect(toggle).toBeTruthy();
      expect(toggle.getAttribute('aria-expanded')).toBe('false');
      expect(toggle.getAttribute('aria-controls')).toBe('bm-artifact-1');

      toggle.click();
      fixture.detectChanges();

      const box = fixture.nativeElement.querySelector('.artifact-box') as HTMLElement;
      expect(box).toBeTruthy();
      expect(box.id).toBe('bm-artifact-1');
      expect(box.querySelector('em')).toBeNull();
      expect(box.textContent).toContain('to=multi_tool_use.parallel');
      expect(box.textContent).toContain('<em>{"tool_uses":[]}</em>');
      expect((fixture.nativeElement.querySelector('.question-card-body .btn-link') as HTMLButtonElement)
        .getAttribute('aria-expanded')).toBe('true');
    });

    it('should offer the second opinion assessor as an optional selector, defaulting to none', () => {
      fixture.detectChanges();

      const selector = fixture.nativeElement.querySelector('.second-opinion-model-selector') as HTMLElement;
      expect(selector).toBeTruthy();
      // A System AI Config is a database row chosen here, never a value in appsettings.json.
      expect(selector.textContent).toContain('None — no second reader');
      expect(component.secondOpinionConfigId).toBeNull();
    });

    it('should send the second opinion assessor only when one is selected', () => {
      benchmarkServiceMock.startRun.mockReturnValue(of({ runId: 9 }));
      // Suppress the success path's side effects: polling would leave a live interval behind
      // and the dialog would need a real <dialog> to open.
      vi.spyOn(component as any, 'startPolling').mockReturnValue(undefined);
      vi.spyOn(component as any, 'openRunProgressDialog').mockReturnValue(undefined);
      component.selectedSuiteId = 1;
      component.testedConfigId = 10;
      component.assessorConfigId = 11;

      component.startBenchmark();
      expect(vi.mocked(benchmarkServiceMock.startRun).mock.lastCall![0].secondOpinionAssessorModelConfigurationId)
        .toBeNull();

      component.secondOpinionConfigId = 12;
      component.startBenchmark();
      expect(vi.mocked(benchmarkServiceMock.startRun).mock.lastCall![0].secondOpinionAssessorModelConfigurationId)
        .toBe(12);
    });

    it('should reject a second opinion threshold outside 0 to 100 before calling the server', () => {
      component.editingProfileId = null;
      component.profileForm = { ...component.profileForm, name: 'Threshold Profile', secondOpinionQualityThreshold: 140 };

      component.saveProfile();

      expect(component.profileValidationErrors)
        .toContain('Second reader threshold must be between 0 and 100.');
      expect(benchmarkServiceMock.createScoringProfile).not.toHaveBeenCalled();
    });

    it('should default a new profile to a not-attempted score of 50 and send it', () => {
      component.openCreateProfile();
      component.scoringProfileFormDialog?.nativeElement.close();
      expect(component.profileForm.notAttemptedScore).toBe(50);

      benchmarkServiceMock.createScoringProfile.mockReturnValue(of({ id: 9 } as any));
      component.profileForm.name = 'Abstention Profile';
      component.saveProfile();

      expect(component.profileValidationErrors).toEqual([]);
      expect(vi.mocked(benchmarkServiceMock.createScoringProfile).mock.lastCall![0].notAttemptedScore).toBe(50);
    });

    it('should load a profile\'s not-attempted score for editing and send a blank one as null', () => {
      const profile: any = {
        id: 4, name: 'Abstention Profile', isDefault: false,
        weightAccuracy: 0.55, weightCompleteness: 0.25, weightConciseness: 0.10, weightReadability: 0.10,
        levelScoresJson: '[1, 15, 35, 55, 72, 87, 100]', criticalErrorCeiling: 25, notAttemptedScore: 40,
        secondOpinionQualityThreshold: 50, secondOpinionMode: 1, secondOpinionOutlierDeltaPoints: 25,
        speedTargetMs: 15000, speedDecayK: 20, speedDifficultyScaling: 1, maxParallelQuestions: 1,
        createdAtUtc: '2026-10-01T00:00:00Z', modifiedAtUtc: '2026-10-01T00:00:00Z'
      };
      component.openEditProfile(profile);
      component.scoringProfileFormDialog?.nativeElement.close();
      expect(component.profileForm.notAttemptedScore).toBe(40);

      // An update that omits the field clears it on the server, so a blank field is sent as null.
      benchmarkServiceMock.updateScoringProfile.mockReturnValue(of(profile));
      component.profileForm.notAttemptedScore = undefined as any;
      component.saveProfile();

      const sent = vi.mocked(benchmarkServiceMock.updateScoringProfile).mock.lastCall![1];
      expect(Object.prototype.hasOwnProperty.call(sent, 'notAttemptedScore')).toBe(true);
      expect(sent.notAttemptedScore).toBeNull();

      // A profile served without the field edits as blank.
      const legacy: any = { ...profile };
      delete legacy.notAttemptedScore;
      component.openEditProfile(legacy);
      component.scoringProfileFormDialog?.nativeElement.close();
      expect(component.profileForm.notAttemptedScore).toBeNull();
    });

    it('should reject a not-attempted score outside 0 to 100 before calling the server', () => {
      component.editingProfileId = null;
      component.profileForm = { ...component.profileForm, name: 'Abstention Profile', notAttemptedScore: 140 };

      component.saveProfile();

      expect(component.profileValidationErrors)
        .toContain('Not-attempted score must be blank or a whole number between 0 and 100.');
      expect(benchmarkServiceMock.createScoringProfile).not.toHaveBeenCalled();
    });

    it('should label the not-attempted score field and explain it', () => {
      fixture.detectChanges();
      const label = fixture.nativeElement.querySelector('label[for="notAttemptedScore"]') as HTMLElement;
      expect(label.textContent?.trim()).toBe('Not-Attempted Score');
      const input = fixture.nativeElement.querySelector('#notAttemptedScore') as HTMLInputElement;
      expect(input.type).toBe('number');
      expect(input.getAttribute('aria-describedby')).toBe('notAttemptedScoreHint');
      const hint = (fixture.nativeElement.querySelector('#notAttemptedScoreHint') as HTMLElement).textContent!.replace(/\s+/g, ' ');
      expect(hint).toContain('Lowest score for an answer that says it could not find or verify the answer and makes no false claim (scoring method 13 on).');
      expect(hint).toContain('At 50, giving an answer pays off when the model is at least about as likely to be right as wrong.');
    });

    it('should label the profile form second-reader fields and open the guide at coverage from it', () => {
      fixture.detectChanges();
      const label = (forId: string) =>
        (fixture.nativeElement.querySelector(`label[for="${forId}"]`) as HTMLElement | null)?.textContent?.trim();
      expect(label('secondOpinionThreshold')).toBe('Second Reader Threshold');
      expect(label('secondOpinionModeProfile')).toBe('Second Reader Coverage');
      const blind = (fixture.nativeElement.querySelector('#profileSecondOpinionBlind') as HTMLElement).closest('label') as HTMLElement;
      expect(blind.textContent?.trim()).toBe('Blind Second Reader');

      const open = vi.spyOn(component.graderGuide!, 'open').mockReturnValue(undefined);
      const guideButton = (fixture.nativeElement.querySelector('#secondOpinionModeProfile') as HTMLElement)
        .closest('.form-group')!.querySelector('.grader-guide-btn') as HTMLButtonElement;
      expect(guideButton.textContent?.trim()).toBe('How the graders work');
      guideButton.click();
      expect(open).toHaveBeenCalledWith('coverage');
    });

    it('should badge both models below the header rather than in it', () => {
      component.activeRunDetail = buildCompletedRun({
        status: 'Running',
        testedModelThinkingLevelUsed: 'max',
        testedModelServiceTierUsed: 'flex',
        assessorModelThinkingLevelUsed: 'high'
      });
      fixture.detectChanges();

      const strip = fixture.nativeElement.querySelector('.run-model-strip') as HTMLElement;
      expect(strip).toBeTruthy();
      expect(strip.textContent).toContain('Test Model');
      expect(strip.textContent).toContain('Test Assessor');
      expect(strip.querySelector('.thinking-badge')?.textContent?.trim()).toBe('thinking level Max');
      expect(strip.querySelector('.provider-badge')).toBeTruthy();
      expect(strip.querySelector('.config-badge')?.textContent?.trim()).toBe('requested service tier Flex');

      const subtitle = fixture.nativeElement
        .querySelector('.benchmark-run-progress-dialog .dialog-subtitle') as HTMLElement;
      expect(subtitle.textContent).toContain('Default Suite');
      expect(subtitle.textContent).not.toContain('Model:');
      expect(subtitle.textContent).not.toContain('Evaluator:');
      expect(subtitle.textContent).not.toContain('Assessor:');
    });

    it('should render Cancel Run as a text-only button', () => {
      component.activeRunDetail = buildCompletedRun({ status: 'Running' });
      fixture.detectChanges();

      const buttons: HTMLButtonElement[] = Array.from(
        fixture.nativeElement.querySelectorAll('.benchmark-run-progress-dialog .dialog-footer button'));
      const cancel = buttons.find(b => (b.textContent || '').includes('Cancel Run'));

      expect(cancel).toBeTruthy();
      expect(cancel!.querySelector('svg')).toBeNull();
    });

    it('should present the run as three stages', () => {
      // BenchmarkService assesses, verifies and second-guesses each answer immediately after
      // producing it, inside the same loop, so stage 1 is all of that, not "collecting".
      component.activeRunDetail = buildCompletedRun({
        status: 'Running',
        totalQuestionCount: 2,
        answers: [buildScoredAnswer(1), buildScoredAnswer(2)]
      });

      expect(component.runStage).toBe('finalizing');
      expect(component.runStageLabel).toContain('Stage 3 of 3 — Synthesis and scoring');

      component.activeRunDetail = buildCompletedRun({
        status: 'Running',
        totalQuestionCount: 2,
        answers: [buildScoredAnswer(1, { assessmentStatus: 'Pending' }), buildScoredAnswer(2)]
      });

      // An answer still awaiting assessment keeps the run in stage 1: the stage covers both.
      expect(component.runStage).toBe('answering');
      expect(component.runStageLabel).toContain('Stage 1 of 3 — Answering and grading');
    });

    it('should mark a dispatched question Answering and an undispatched one Pending', () => {
      component.activeRunDetail = buildCompletedRun({
        status: 'Running',
        totalQuestionCount: 3,
        inFlightOrderIndexes: [2],
        answers: [buildScoredAnswer(1)]
      });
      component.runProgressQuestions = [
        { orderIndex: 1, questionText: 'Question 1' },
        { orderIndex: 2, questionText: 'Question 2' },
        { orderIndex: 3, questionText: 'Question 3' }
      ] as any;

      const rows = component.runProgressRows;

      expect(rows.length).toBe(3);
      expect(component.runRowChipLabel(rows[0])).toBe('Scored');
      // In flight: the request has reached the provider but no answer row exists yet.
      expect(rows[1].status).toBe('Answering');
      expect(component.runRowChipLabel(rows[1])).toBe('Answering');
      expect(component.runRowChipClass(rows[1])).toBe('status-answering');
      // Not dispatched: Pending keeps its original, narrower meaning.
      expect(rows[2].status).toBe('Pending');
      expect(component.runRowChipLabel(rows[2])).toBe('Pending');
    });

    it('should record in-flight questions and the stage number in the diagnostics text', () => {
      component.activeRunDetail = buildCompletedRun({
        status: 'Running',
        totalQuestionCount: 2,
        inFlightOrderIndexes: [2],
        answers: [buildScoredAnswer(1)]
      });
      component.runProgressQuestions = [
        { orderIndex: 1, questionText: 'Question 1' },
        { orderIndex: 2, questionText: 'Question 2' }
      ] as any;

      const diagnostics = component.runDiagnosticsText;

      expect(diagnostics).toContain('Stage: 1 of 3 (derived)');
      expect(diagnostics).toContain('In flight: Q2');
      expect(diagnostics).toContain('[Q2] status=Answering');
    });

    it('should prefer the server stage over the derivation, and fall back when the server reports none', () => {
      // Nothing in the answer rows moves during verification or the second-opinion passes, so
      // the derivation cannot see either stage. Only the server can.
      const answers = [buildScoredAnswer(1), buildScoredAnswer(2)];

      component.activeRunDetail = buildCompletedRun({
        status: 'Running', totalQuestionCount: 2, stage: 'Verifying', answers
      });
      expect(component.runStage).toBe('verifying');
      expect(component.runStageLabel)
        .toContain('Stage 2 of 3 — Follow-up grading passes: verifying remaining claims');

      component.activeRunDetail = buildCompletedRun({
        status: 'Running', totalQuestionCount: 2, stage: 'SecondOpinion', answers
      });
      expect(component.runStage).toBe('secondopinion');
      expect(component.runStageLabel)
        .toContain('Stage 2 of 3 — Follow-up grading passes: second-reader sweep');

      component.activeRunDetail = buildCompletedRun({
        status: 'Running', totalQuestionCount: 2, stage: 'Answering', answers
      });
      expect(component.runStage).toBe('answering');

      // A run detail from a server predating the field: the derivation still renders a stage.
      component.activeRunDetail = buildCompletedRun({
        status: 'Running', totalQuestionCount: 2, answers
      });
      expect(component.runStage).toBe('finalizing');
      expect(component.runDiagnosticsText).toContain('Stage: 3 of 3 (derived)');

      // A terminal run is terminal regardless of a stale stage.
      component.activeRunDetail = buildCompletedRun({
        status: 'Completed', totalQuestionCount: 2, stage: 'Verifying', answers
      });
      expect(component.runStage).toBe('terminal');
    });

    /**
     * The rail, rendered once per state. Each case builds its own fixture render because this
     * fixture only reflects a state change on its first detectChanges — the existing dialog tests
     * rely on the component refreshing its own view for the same reason.
     */
    function railItems(stage: string): HTMLElement[] {
      component.activeRunDetail = buildCompletedRun({
        status: 'Running',
        totalQuestionCount: 2,
        stage,
        answers: [buildScoredAnswer(1), buildScoredAnswer(2)]
      });
      fixture.detectChanges();
      return Array.from(
        fixture.nativeElement.querySelectorAll('.benchmark-run-progress-dialog .run-stage-rail .run-stage'));
    }

    it('should render three rail items and make stage 2 current while claims are verified', () => {
      const items = railItems('Verifying');

      expect(items.length).toBe(3);
      expect(items[0].classList).toContain('is-done');
      expect(items[1].classList).toContain('is-current');
      expect(items[1].getAttribute('aria-current')).toBe('step');
      expect(items[2].classList).not.toContain('is-current');
      expect(items[2].getAttribute('aria-current')).toBeNull();
    });

    it('should keep the same rail item current during the second-opinion pass', () => {
      // The server marks Verifying again after this pass, so the two sharing one item is what
      // stops the rail stepping backwards late in a run.
      const items = railItems('SecondOpinion');

      expect(items.length).toBe(3);
      expect(items[0].classList).toContain('is-done');
      expect(items[1].classList).toContain('is-current');
      expect(items[1].getAttribute('aria-current')).toBe('step');
      expect(items[2].classList).not.toContain('is-current');
    });

    it('should mark both earlier rail items done during synthesis', () => {
      const items = railItems('Synthesizing');

      expect(items[0].classList).toContain('is-done');
      expect(items[1].classList).toContain('is-done');
      expect(items[1].classList).not.toContain('is-current');
      expect(items[2].classList).toContain('is-current');
      expect(items[2].getAttribute('aria-current')).toBe('step');
    });

    /** The text of the stat-strip cell with this term, or null when the run does not show one. */
    function runStatText(label: string): string | null {
      const cells: HTMLElement[] = Array.from(
        fixture.nativeElement.querySelectorAll('.benchmark-run-progress-dialog .run-stat-strip .run-stat'));
      const cell = cells.find(c => (c.querySelector('dt')?.textContent || '').trim() === label);
      return cell ? (cell.querySelector('dd')?.textContent || '').trim() : null;
    }

    it('should show plain counters instead of bars for claims verified and second opinions', () => {
      component.activeRunDetail = buildCompletedRun({
        status: 'Running',
        totalQuestionCount: 2,
        stage: 'Answering',
        claimVerifierDisplayNameUsed: 'Test Verifier',
        secondOpinionAssessorModelDisplayNameUsed: 'Test Second Opinion',
        secondOpinionModeUsed: 1,
        inFlightVerificationOrderIndexes: [2],
        answers: [
          buildScoredAnswer(1, { claimVerificationJson: '{}' }),
          buildScoredAnswer(2, { secondOpinionQualityScore: 70 })
        ]
      });
      fixture.detectChanges();

      // Only answers with unverified claims or a fired trigger are candidates, so a bar against
      // the answered count has no honest maximum. The counts replace both bars outright.
      const dialog = fixture.nativeElement.querySelector('.benchmark-run-progress-dialog') as HTMLElement;
      expect(dialog.querySelector('#runVerifiedProgressBar')).toBeNull();
      expect(dialog.querySelector('#runSecondOpinionProgressBar')).toBeNull();
      expect(dialog.querySelector('#runAnswersProgressBar')).toBeTruthy();
      expect(dialog.querySelector('#runAssessmentsProgressBar')).toBeTruthy();

      expect(runStatText('Claims verified')).toBe('1 · 1 in progress');
      expect(runStatText('Second readings')).toBe('1');
    });

    it('should show neither counter for a run graded by neither role', () => {
      // Not two zeroes: a run that configured no verifier did not fail to verify anything.
      component.activeRunDetail = buildCompletedRun({
        status: 'Running',
        totalQuestionCount: 2,
        stage: 'Answering',
        answers: [buildScoredAnswer(1), buildScoredAnswer(2)]
      });
      fixture.detectChanges();

      expect(runStatText('Claims verified')).toBeNull();
      expect(runStatText('Second readings')).toBeNull();
    });

    describe('report writing stage', () => {
      const WRITER = {
        reportWriterModelConfigurationId: 9,
        reportWriterDisplayName: 'Report Writer Model',
        reportWriterProvider: 'Anthropic',
        reportWriterModelId: 'claude-writer',
        reportWriterThinkingLevel: 'high'
      };

      function writerRun(overrides: any = {}): any {
        return buildCompletedRun({
          ...WRITER,
          totalQuestionCount: 2,
          answeredQuestionCount: 2,
          answers: [buildScoredAnswer(1), buildScoredAnswer(2)],
          ...overrides
        });
      }

      /** A job view writing the first of two documents; the writer took the slot 72 s before the server's clock. */
      function reportJob(overrides: any = {}): any {
        return {
          runId: 55,
          status: BenchmarkRunReportDocumentsStatus.Writing,
          message: null,
          phase: 'Writing',
          queuedAtUtc: '2026-09-03T07:10:01Z',
          slotAcquiredAtUtc: '2026-09-03T07:10:05Z',
          finishedAtUtc: null,
          cancelRequestedAtUtc: null,
          jobsAhead: null,
          blockingJobLabel: null,
          audiences: [1, 2],
          writerConfigId: 9,
          writerDisplayName: 'Report Writer Model',
          writerProvider: 'Anthropic',
          writerModelId: 'claude-writer',
          writerThinkingLevel: 'high',
          job: {
            id: 'job-1', packId: 'pack-1', subjectKey: 'run:55', subjectLabel: 'Test Model', suiteId: 1,
            suiteName: 'Default Suite', writerConfigId: 9, writerDisplayName: 'Report Writer Model',
            startedByUserId: null, startedAtUtc: '2026-09-03T07:10:05Z', completedAtUtc: null, status: 'Running',
            totalModelCalls: 1, inputTokens: 1000, outputTokens: 200, costUsd: 0.05,
            documents: [
              { audience: 1, status: 'Writing', documentId: null, errorMessage: null, modelCalls: 1 },
              { audience: 2, status: 'Pending', documentId: null, errorMessage: null, modelCalls: 0 }
            ],
            log: []
          },
          serverTimeUtc: '2026-09-03T07:11:17Z',
          ...overrides
        };
      }

      /** Starts the real poll on a run seen Running, with the lock and the document's visibility held still. */
      function startWatching(): Mock {
        const lockService = TestBed.inject(BenchmarkBackgroundActivityService);
        vi.spyOn(lockService, 'acquireForRun').mockReturnValue(undefined);
        vi.spyOn(lockService, 'release').mockReturnValue(undefined);
        vi.spyOn(document, 'hidden', 'get').mockReturnValue(false);
        const playSpy = vi.spyOn(TestBed.inject(BenchmarkCompletionSoundService), 'play').mockResolvedValue('played');
        component.completionSound = true;
        benchmarkServiceMock.getRun.mockReturnValue(of(writerRun({ status: 'Running', stage: 'Synthesizing', completedAtUtc: null })));
        (component as any).startPolling(55);
        return playSpy;
      }

      function pollTicker(): unknown {
        return (component as any).pollTickerHandle;
      }

      it('should show the report writer in the model strip after the claim verifier', () => {
        component.activeRunDetail = writerRun({
          status: 'Running', stage: 'Answering', claimVerifierDisplayNameUsed: 'Test Verifier'
        });
        fixture.detectChanges();

        const rows: HTMLElement[] = Array.from(
          fixture.nativeElement.querySelectorAll('.benchmark-run-progress-dialog .run-model-strip .run-model-row'));
        const terms = rows.map(row => (row.querySelector('dt')?.textContent || '').trim());
        expect(terms.indexOf('Report writer')).toBe(terms.indexOf('Claim verifier') + 1);

        const writerRow = rows[terms.indexOf('Report writer')];
        expect(writerRow.querySelector('.model-name')?.textContent?.trim()).toBe('Report Writer Model');
        expect(writerRow.querySelector('.thinking-badge')).toBeTruthy();
        expect(writerRow.querySelector('app-provider-badge')).toBeTruthy();
      });

      it('should show no report writer row, no fourth rail item and no Reports cell for a run without a writer', () => {
        component.activeRunDetail = buildCompletedRun({
          status: 'Running', stage: 'Verifying', totalQuestionCount: 2,
          answers: [buildScoredAnswer(1), buildScoredAnswer(2)]
        });
        fixture.detectChanges();

        const dialog = fixture.nativeElement.querySelector('.benchmark-run-progress-dialog') as HTMLElement;
        expect(dialog.textContent).not.toContain('Report writer');
        expect(dialog.querySelectorAll('.run-stage-rail .run-stage').length).toBe(3);
        expect(runStatText('Reports')).toBeNull();
        expect(component.runStageLabel).toContain('Stage 2 of 3 — Follow-up grading passes');
      });

      it('should render a fourth rail item and count the stages of 4 when the run names a writer', () => {
        component.activeRunDetail = writerRun({ status: 'Running', stage: 'Verifying' });
        fixture.detectChanges();

        const items: HTMLElement[] = Array.from(
          fixture.nativeElement.querySelectorAll('.benchmark-run-progress-dialog .run-stage-rail .run-stage'));
        expect(items.length).toBe(4);
        expect(items[3].querySelector('.run-stage-name')?.textContent?.trim()).toBe('Writing reports');
        expect(items[3].classList).not.toContain('is-current');
        expect(items[3].classList).not.toContain('is-done');
        expect(items[1].classList).toContain('is-current');
        expect(component.runStageLabel).toContain('Stage 2 of 4 — Follow-up grading passes');
        expect(component.runDiagnosticsText).toContain('Stage: 2 of 4 (verifying) (server)');
      });

      it('should make stage 4 current while the reports are written, with the job in the status line, stat strip and cost panel', () => {
        benchmarkServiceMock.getRun.mockReturnValue(of(writerRun({
          status: 'Completed', reportDocumentsStatus: BenchmarkRunReportDocumentsStatus.Writing
        })));
        benchmarkServiceMock.getRunReportJob.mockReturnValue(of(reportJob()));

        (component as any).pollRunDetail(55);
        fixture.detectChanges();

        expect(benchmarkServiceMock.getRunReportJob).toHaveBeenCalledWith(55);
        expect(component.runRailStage).toBe(4);
        expect(component.runStageLabel).toBe('Stage 4 of 4 — Writing reports: writing the Executive Summary (1 of 2)');

        const items: HTMLElement[] = Array.from(
          fixture.nativeElement.querySelectorAll('.benchmark-run-progress-dialog .run-stage-rail .run-stage'));
        expect(items.length).toBe(4);
        expect(items[0].classList).toContain('is-done');
        expect(items[1].classList).toContain('is-done');
        expect(items[2].classList).toContain('is-done');
        expect(items[3].classList).toContain('is-current');
        expect(items[3].getAttribute('aria-current')).toBe('step');

        expect(runStatText('Reports')).toBe('1m 12s · writing');
        const writerCost = fixture.nativeElement.querySelector(
          '.benchmark-run-progress-dialog .gh-cost-role--report-writer .gh-cost-role__amount') as HTMLElement;
        expect(writerCost.textContent?.trim()).toBe('$0.0500');
        (component as any).stopPolling();
      });

      it('should name the queue position while the job waits for the report writer', () => {
        benchmarkServiceMock.getRun.mockReturnValue(of(writerRun({
          status: 'Completed', reportDocumentsStatus: BenchmarkRunReportDocumentsStatus.Pending
        })));
        benchmarkServiceMock.getRunReportJob.mockReturnValue(of(reportJob({
          phase: 'Queued', status: BenchmarkRunReportDocumentsStatus.Pending, slotAcquiredAtUtc: null, jobsAhead: 1
        })));

        (component as any).pollRunDetail(55);

        expect(component.runStageLabel).toBe('Stage 4 of 4 — Writing reports: waiting for the report writer (1 job ahead)');
        expect(component.runReportsStatLabel).toBe('Waiting');
        (component as any).stopPolling();
      });

      it('should keep polling through Pending and Writing, then stop and chime once the reports are written', fakeAsync(() => {
        const playSpy = startWatching();
        expect(pollTicker()).not.toBeNull();

        benchmarkServiceMock.getRun.mockReturnValue(of(writerRun({
          status: 'Completed', reportDocumentsStatus: BenchmarkRunReportDocumentsStatus.Pending
        })));
        benchmarkServiceMock.getRunReportJob.mockReturnValue(of(reportJob({
          phase: 'Queued', status: BenchmarkRunReportDocumentsStatus.Pending, slotAcquiredAtUtc: null, jobsAhead: 0
        })));
        tick(2000);
        expect(component.runReportStage).toBe('current');
        expect(component.runStageLabel).toBe('Stage 4 of 4 — Writing reports: waiting for the report writer');
        expect(pollTicker()).not.toBeNull();
        expect(playSpy).not.toHaveBeenCalled();

        benchmarkServiceMock.getRun.mockReturnValue(of(writerRun({
          status: 'Completed', reportDocumentsStatus: BenchmarkRunReportDocumentsStatus.Writing
        })));
        benchmarkServiceMock.getRunReportJob.mockReturnValue(of(reportJob()));
        tick(2000);
        expect(component.runStageLabel).toBe('Stage 4 of 4 — Writing reports: writing the Executive Summary (1 of 2)');
        expect(pollTicker()).not.toBeNull();
        expect(playSpy).not.toHaveBeenCalled();

        benchmarkServiceMock.getRun.mockReturnValue(of(writerRun({
          status: 'Completed',
          reportDocumentsStatus: BenchmarkRunReportDocumentsStatus.Completed,
          reportDocumentsWrittenCount: 2,
          reportDocumentsDurationMs: 72000,
          reportDocumentsCostUsd: 0.12
        })));
        tick(2000);
        expect(component.runReportStage).toBe('done');
        expect(component.runStageLabel).toBe('Completed. Answered 2 of 2. Reports written: 2 documents, 1m 12s.');
        expect(component.runReportsStatLabel).toBe('2 documents, 1m 12s');
        expect(component.runReportWriterCost).toBe(0.12);
        expect(pollTicker()).toBeNull();
        expect(playSpy).toHaveBeenCalledTimes(1);
        expect(playSpy).toHaveBeenCalledWith('run:55');

        const polls = vi.mocked(benchmarkServiceMock.getRun).mock.calls.length;
        tick(10000);
        expect(vi.mocked(benchmarkServiceMock.getRun).mock.calls.length).toBe(polls);
        discardPeriodicTasks();
      }));

      it('should stop polling a run whose writer never starts once the 30-second grace has passed, and chime then', fakeAsync(() => {
        const playSpy = startWatching();

        benchmarkServiceMock.getRun.mockReturnValue(of(writerRun({
          status: 'Completed', reportDocumentsStatus: BenchmarkRunReportDocumentsStatus.NotRequested
        })));
        tick(2000);
        // First seen terminal at 2 s: the grace runs to 32 s.
        expect(component.runReportStage).toBe('current');
        expect(component.runStageLabel).toBe('Stage 4 of 4 — Writing reports: waiting for the report writer');
        expect(playSpy).not.toHaveBeenCalled();

        tick(28000);
        expect(pollTicker()).not.toBeNull();
        expect(playSpy).not.toHaveBeenCalled();

        tick(2000);
        expect(pollTicker()).toBeNull();
        expect(component.runReportStage).toBe('notWritten');
        expect(component.runStageLabel).toBe('Completed. Answered 2 of 2.');
        expect(playSpy).toHaveBeenCalledTimes(1);
        expect(playSpy).toHaveBeenCalledWith('run:55');

        const polls = vi.mocked(benchmarkServiceMock.getRun).mock.calls.length;
        tick(10000);
        expect(vi.mocked(benchmarkServiceMock.getRun).mock.calls.length).toBe(polls);
        discardPeriodicTasks();
      }));

      it('should stop at once and chime for a writer run that ends with another terminal status', fakeAsync(() => {
        const playSpy = startWatching();
        benchmarkServiceMock.getRunReportJob.mockClear();

        benchmarkServiceMock.getRun.mockReturnValue(of(writerRun({
          status: 'CompletedWithErrors', reportDocumentsStatus: BenchmarkRunReportDocumentsStatus.Pending
        })));
        tick(2000);

        expect(pollTicker()).toBeNull();
        expect(component.runReportStage).toBe('notWritten');
        expect(benchmarkServiceMock.getRunReportJob).not.toHaveBeenCalled();
        expect(playSpy).toHaveBeenCalledTimes(1);
        expect(playSpy).toHaveBeenCalledWith('run:55');
        discardPeriodicTasks();
      }));

      it('should put a failed report stage in the status line with its message', () => {
        component.activeRunDetail = writerRun({
          status: 'Completed',
          reportDocumentsStatus: BenchmarkRunReportDocumentsStatus.Failed,
          reportDocumentsMessage: 'The writer refused.',
          reportDocumentsWrittenCount: 1,
          reportDocumentsDurationMs: 30000,
          reportDocumentsCostUsd: 0.04
        });

        expect(component.runReportStage).toBe('ended');
        expect(component.runRailStage).toBe(0);
        expect(component.runStageLabel).toBe('Completed. Answered 2 of 2. Report writing failed: The writer refused.');
        expect(component.runReportWriterCost).toBe(0.04);
      });

      it('should add a REPORTS block to the diagnostics of a run that names a writer', () => {
        component.activeRunDetail = writerRun({
          status: 'Completed',
          reportDocumentsStatus: BenchmarkRunReportDocumentsStatus.Completed,
          reportDocumentsMessage: null,
          reportDocumentsWrittenCount: 2,
          reportDocumentsDurationMs: 72000,
          reportDocumentsInputTokens: 1000,
          reportDocumentsOutputTokens: 500,
          reportDocumentsCostUsd: 0.12
        });

        const text = component.runDiagnosticsText;
        expect(text).toContain('--- REPORTS ---');
        expect(text).toContain('Writer: Report Writer Model (Anthropic / claude-writer), thinking: high');
        expect(text).toContain('Status: Completed');
        expect(text).toContain('Message: none');
        expect(text).toContain('Documents written: 2');
        expect(text).toContain('Duration: 1m 12s');
        expect(text).toContain('Tokens: input 1000, output 500');
        expect(text).toContain("Cost: $0.1200 (outside the run's own cost)");
        expect(text.indexOf('--- REPORTS ---')).toBeLessThan(text.indexOf('--- FLAGS ---'));

        component.activeRunDetail = buildCompletedRun();
        expect(component.runDiagnosticsText).not.toContain('--- REPORTS ---');
      });
    });

    it('should chip a re-graded row as Verifying or Second opinion rather than Scored', () => {
      component.activeRunDetail = buildCompletedRun({
        status: 'Running',
        totalQuestionCount: 3,
        stage: 'Verifying',
        inFlightVerificationOrderIndexes: [1],
        inFlightSecondOpinionOrderIndexes: [2],
        answers: [buildScoredAnswer(1), buildScoredAnswer(2), buildScoredAnswer(3)]
      });

      const rows = component.runProgressRows;

      // Both rows already carry a score; without the in-flight sets they read as finished for
      // the whole pass.
      expect(rows[0].status).toBe('Verifying');
      expect(component.runRowChipLabel(rows[0])).toBe('Verifying');
      expect(component.runRowChipClass(rows[0])).toBe('status-verifying');

      expect(rows[1].status).toBe('SecondOpinion');
      expect(component.runRowChipLabel(rows[1])).toBe('Second reader');
      expect(component.runRowChipClass(rows[1])).toBe('status-secondopinion');

      expect(component.runRowChipLabel(rows[2])).toBe('Scored');
    });

    it('should list every question during a re-run and mark only the re-run set', () => {
      component.activeRunDetail = buildCompletedRun({
        status: 'Running',
        totalQuestionCount: 3,
        answers: [
          buildScoredAnswer(1),
          buildScoredAnswer(2, { status: 'ProviderError', assessmentStatus: 'Pending' }),
          buildScoredAnswer(3)
        ]
      });
      component.rerunScopeOrderIndexes = [2];

      const rows = component.runProgressRows;

      // The row list is unchanged: the whole suite stays listed and the rows outside the
      // re-run keep the status they already have.
      expect(rows.length).toBe(3);
      expect(component.runHasRerunScope).toBe(true);
      expect(component.isRerunScope(rows[0])).toBe(false);
      expect(component.isRerunScope(rows[1])).toBe(true);
      expect(component.isRerunScope(rows[2])).toBe(false);
      expect(component.runRowChipLabel(rows[0])).toBe('Scored');
      expect(component.runRowChipLabel(rows[2])).toBe('Scored');
    });

    function buildRerunRun(overrides: any = {}, answer2: any = {}): any {
      return buildCompletedRun({
        status: 'Running',
        stage: 'Answering',
        totalQuestionCount: 3,
        rerunScopeOrderIndexes: [2],
        inFlightOrderIndexes: [],
        rerunAnsweredOrderIndexes: [],
        answers: [
          buildScoredAnswer(1),
          buildScoredAnswer(2, { status: 'ProviderError', assessmentStatus: 'Failed', ...answer2 }),
          buildScoredAnswer(3)
        ],
        ...overrides
      });
    }

    it('should chip a re-run question Answering while its request is in flight even though it already has an answer row', () => {
      component.activeRunDetail = buildRerunRun({ inFlightOrderIndexes: [2] });

      const rows = component.runProgressRows;

      expect(rows[1].status).toBe('Answering');
      expect(component.runRowChipLabel(rows[1])).toBe('Answering');
      expect(component.runRowChipClass(rows[1])).toBe('status-answering');
      expect(component.runRowChipLabel(rows[0])).toBe('Scored');
      expect(component.runRowChipLabel(rows[2])).toBe('Scored');
    });

    it('should chip a queued re-run question Pending rather than its previous failure while the re-run is running', () => {
      component.activeRunDetail = buildRerunRun();

      let rows = component.runProgressRows;

      expect(rows[1].status).toBe('Pending');
      expect(component.runRowChipLabel(rows[1])).toBe('Pending');

      // Bounded to a running re-run: a terminal run shows the row's real status.
      component.activeRunDetail = buildRerunRun({ status: 'Completed' });
      rows = component.runProgressRows;

      expect(component.runRowChipLabel(rows[1])).toBe('Provider Error');
    });

    it('should follow a re-answered re-run question through Answered, Assessing and Scored', () => {
      component.activeRunDetail = buildRerunRun({ rerunAnsweredOrderIndexes: [2] }, { status: 'Ok', assessmentStatus: 'Pending' });
      expect(component.runRowChipLabel(component.runProgressRows[1])).toBe('Answered');

      component.activeRunDetail = buildRerunRun({ rerunAnsweredOrderIndexes: [2] }, { status: 'Ok', assessmentStatus: 'Assessing' });
      let row = component.runProgressRows[1];
      expect(component.runRowChipLabel(row)).toBe('Assessing');
      expect(component.runRowChipClass(row)).toBe('status-assessing');

      component.activeRunDetail = buildRerunRun({ rerunAnsweredOrderIndexes: [2] }, { status: 'Ok', assessmentStatus: 'Scored' });
      row = component.runProgressRows[1];
      expect(component.runRowChipLabel(row)).toBe('Scored');
    });

    it("should list a finished run's own answers only, whatever its suite lost or gained since", () => {
      // The suite lost question 12 (answered as Q2) and gained question 40, which now sits at Q2.
      component.activeRunDetail = buildCompletedRun({
        status: 'Completed',
        totalQuestionCount: 3,
        answers: [
          buildScoredAnswer(1, { benchmarkQuestionId: 11, questionText: 'Stored question 1' }),
          buildScoredAnswer(2, { benchmarkQuestionId: null, questionText: 'Deleted question, as asked' }),
          buildScoredAnswer(3, { benchmarkQuestionId: 13, questionText: 'Stored question 3' })
        ]
      });
      component.runProgressQuestions = [
        { id: 11, orderIndex: 1, questionText: 'Question 1 as the suite words it now' },
        { id: 40, orderIndex: 2, questionText: 'A question added after the run' },
        { id: 13, orderIndex: 3, questionText: 'Question 3 as the suite words it now' }
      ] as any;

      const rows = component.runProgressRows;

      expect(rows.map(r => r.orderIndex)).toEqual([1, 2, 3]);
      expect(rows.map(r => r.questionText))
        .toEqual(['Stored question 1', 'Deleted question, as asked', 'Stored question 3']);
      expect(rows.map(r => component.runRowChipLabel(r))).toEqual(['Scored', 'Scored', 'Scored']);
      expect(rows.every(r => r.answer != null)).toBe(true);
    });

    it('should add a question the first pass has not answered yet, matched by question id rather than order index', () => {
      component.activeRunDetail = buildCompletedRun({
        status: 'Running',
        totalQuestionCount: 3,
        inFlightOrderIndexes: [3],
        answers: [buildScoredAnswer(1, { benchmarkQuestionId: 11 })]
      });
      component.runProgressQuestions = [
        { id: 11, orderIndex: 1, questionText: 'Question 1' },
        { id: 12, orderIndex: 2, questionText: 'Question 2' },
        { id: 13, orderIndex: 3, questionText: 'Question 3' }
      ] as any;

      const rows = component.runProgressRows;

      expect(rows.map(r => r.orderIndex)).toEqual([1, 2, 3]);
      expect(rows[0].questionText).toBe('Question 1');
      expect(rows[0].answer?.benchmarkQuestionId).toBe(11);
      expect(rows[1].status).toBe('Pending');
      expect(rows[1].answer).toBeNull();
      expect(rows[2].status).toBe('Answering');
    });

    it('should add no live question while a re-run is running, even one the suite gained since', () => {
      component.activeRunDetail = buildCompletedRun({
        status: 'Running',
        totalQuestionCount: 2,
        rerunScopeOrderIndexes: [2],
        rerunAnsweredOrderIndexes: [],
        answers: [
          buildScoredAnswer(1, { benchmarkQuestionId: 11 }),
          buildScoredAnswer(2, { benchmarkQuestionId: 12, status: 'ProviderError', assessmentStatus: 'Failed' })
        ]
      });
      component.runProgressQuestions = [
        { id: 11, orderIndex: 1, questionText: 'Question 1' },
        { id: 12, orderIndex: 2, questionText: 'Question 2' },
        { id: 40, orderIndex: 3, questionText: 'A question added after the run' }
      ] as any;

      expect(component.runIsFirstPass).toBe(false);
      expect(component.runProgressRows.map(r => r.orderIndex)).toEqual([1, 2]);

      // A single-answer re-run whose scope the server has not reported yet is still not a first pass.
      component.activeRunDetail = buildCompletedRun({
        status: 'Running',
        totalQuestionCount: 2,
        rerunStartedAtUtc: '2026-09-24T10:00:00Z',
        answers: [buildScoredAnswer(1, { benchmarkQuestionId: 11 }), buildScoredAnswer(2, { benchmarkQuestionId: 12 })]
      });
      expect(component.runIsFirstPass).toBe(false);
      expect(component.runProgressRows.map(r => r.orderIndex)).toEqual([1, 2]);
    });

    it("should write each question's diagnostics line from its own answer", () => {
      component.activeRunDetail = buildCompletedRun({
        status: 'Completed',
        totalQuestionCount: 2,
        answers: [
          buildScoredAnswer(1, { benchmarkQuestionId: 11, durationMs: 1111 }),
          buildScoredAnswer(2, { benchmarkQuestionId: null, durationMs: 2222 })
        ]
      });
      component.runProgressQuestions = [
        { id: 40, orderIndex: 2, questionText: 'A question added after the run' }
      ] as any;

      const diagnostics = component.runDiagnosticsText;

      expect(diagnostics).toMatch(/\[Q1\][^\n]*duration=1111ms/);
      expect(diagnostics).toMatch(/\[Q2\][^\n]*duration=2222ms/);
      expect(diagnostics).not.toContain('[Q2] status=Pending');
    });

    /** The icon-only View game snapshot button of the run report's header actions. */
    function viewGameSnapshotButton(): HTMLButtonElement | undefined {
      const buttons: HTMLButtonElement[] = Array.from(fixture.nativeElement.querySelectorAll(
        '.benchmark-run-detail-dialog [role="group"][aria-label="Run actions"] button'));
      return buttons.find(b => (b.getAttribute('aria-label') || '').startsWith('View game snapshot'));
    }

    it("should open the run's own board read-only from View Game Snapshot", () => {
      const board = { name: 'Run board', sanitizedText: 'Line 1\nLine 2', digestText: null, charCount: 13, sha256: 'feedbeef' };
      benchmarkServiceMock.getRunBoard.mockReturnValue(of(board));
      component.selectedRunDetail = buildCompletedRun({ hasBoardRecord: true, gameSnapshotSha256Used: 'feedbeef' });
      fixture.detectChanges();

      const button = viewGameSnapshotButton();
      expect(button).toBeTruthy();
      button!.click();

      expect(benchmarkServiceMock.getRunBoard).toHaveBeenCalledWith(55);
      const viewer = component.snapshotViewer!;
      expect(viewer.readOnly).toBe(true);
      expect(viewer.readOnlyBoard).toEqual(board);

      const dialog = viewer.viewerDialog.nativeElement;
      expect(dialog.open).toBe(true);
      expect(dialog.textContent).toContain('The board this run was made with.');
      expect(dialog.querySelector('.readonly-board-text')?.textContent).toBe('Line 1\nLine 2');
      expect(Array.from(dialog.querySelectorAll('[role="tab"]')).map(t => (t.textContent || '').trim()))
        .toEqual(['Game Snapshot']);
      expect(dialog.querySelector('.delete-snapshot-btn')).toBeNull();
      expect(dialog.querySelector('.save-all-btn')).toBeNull();
      expect(dialog.querySelector('app-snapshot-text-editor')).toBeNull();

      viewer.close();
    });

    it('should offer View Game Snapshot for a run that recorded only its board hash', () => {
      component.selectedRunDetail = buildCompletedRun({ gameSnapshotSha256Used: 'feedbeef' });
      expect(component.selectedRunHasBoard).toBe(true);
    });

    it('should hide View Game Snapshot for a run made without a board', () => {
      component.selectedRunDetail = buildCompletedRun();
      fixture.detectChanges();

      expect(component.selectedRunHasBoard).toBe(false);
      expect(viewGameSnapshotButton()).toBeUndefined();
    });

    it("should measure Elapsed from the re-run's own start while a re-run scope is active", () => {
      const ninetySecondsAgo = new Date(Date.now() - 90000).toISOString();
      component.activeRunDetail = buildCompletedRun({
        status: 'Running',
        startedAtUtc: '2026-09-11T00:00:00Z',
        completedAtUtc: '2026-09-11T01:00:00Z',
        rerunStartedAtUtc: ninetySecondsAgo,
        rerunCompletedAtUtc: null,
        rerunScopeOrderIndexes: [4],
        rerunAnsweredOrderIndexes: [4],
        rerunScoredOrderIndexes: []
      });

      expect(component.runElapsedIsRerun).toBe(true);
      expect(component.runElapsedLabel).toMatch(/^1m 3\ds$/);

      const diagnostics = component.runDiagnosticsText;
      expect(diagnostics).toContain('Re-run answered: Q4 (1 of 1); re-run scored: none (0 of 1)');
      expect(diagnostics).toContain(`Re-run started:   ${ninetySecondsAgo}`);
      expect(diagnostics).toContain('Re-run completed: n/a');
      expect(diagnostics).toContain('Elapsed (run):    1h 00m 00s');
      expect(diagnostics).toMatch(/Re-run elapsed: {3}1m 3\ds/);
      expect(diagnostics).toContain('Failed-question re-run in progress over: Q4');
      expect(diagnostics).not.toContain('re-run covered');
    });

    it('should print the run span, the re-run span, the covered scope and the run-wide counts for a terminal re-run', () => {
      component.activeRunDetail = buildCompletedRun({
        status: 'Completed',
        totalQuestionCount: 3,
        startedAtUtc: '2026-09-18T21:01:24Z',
        completedAtUtc: '2026-09-18T21:19:44Z',
        rerunStartedAtUtc: '2026-09-18T21:30:00Z',
        rerunCompletedAtUtc: '2026-09-18T21:32:12Z',
        rerunScopeOrderIndexes: [2],
        rerunAnsweredOrderIndexes: [2],
        rerunScoredOrderIndexes: [2],
        // The finalizer's run-wide figures, which the scoped meters must not replace.
        claimVerifiedAnswerCount: 3,
        secondOpinionGradedAnswerCount: 2,
        answers: [
          buildScoredAnswer(1, { claimVerificationJson: '[]', secondOpinionQualityScore: 70 }),
          buildScoredAnswer(2, { claimVerificationJson: '[]' }),
          buildScoredAnswer(3, { claimVerificationJson: '[]', secondOpinionQualityScore: 60 })
        ]
      });
      component.rerunScopeOrderIndexes = [];

      const diagnostics = component.runDiagnosticsText;
      expect(diagnostics).toContain('Elapsed (run):    18m 20s');
      expect(diagnostics).toContain('Re-run elapsed:   2m 12s');
      expect(diagnostics).toContain('Failed-question re-run covered: Q2');
      expect(diagnostics).not.toContain('in progress over');
      expect(diagnostics).toContain('Answers with verified claims: 3, second-graded 2 (re-run scope: 1, 0)');
    });

    it('should print the re-run span from its stamps once the process no longer reports a scope', () => {
      component.activeRunDetail = buildCompletedRun({
        status: 'Completed',
        startedAtUtc: '2026-09-18T21:01:24Z',
        completedAtUtc: '2026-09-18T21:19:44Z',
        rerunStartedAtUtc: '2026-09-18T21:30:00Z',
        rerunCompletedAtUtc: '2026-09-18T21:32:12Z',
        rerunScopeOrderIndexes: [],
        claimVerifiedAnswerCount: 18,
        secondOpinionGradedAnswerCount: 13
      });
      component.rerunScopeOrderIndexes = [];

      const diagnostics = component.runDiagnosticsText;
      expect(diagnostics).toContain('Elapsed (run):    18m 20s');
      expect(diagnostics).toContain('Re-run elapsed:   2m 12s');
      expect(diagnostics).toContain('Answers with verified claims: 18, second-graded 13');
      expect(diagnostics).not.toContain('re-run scope:');
    });

    it("should keep counting Re-run elapsed when a previous re-run's completion stamp is still on the row", () => {
      const ninetySecondsAgo = new Date(Date.now() - 90000).toISOString();
      const anHourBeforeThatStart = new Date(Date.now() - 90000 - 3600000).toISOString();
      component.activeRunDetail = buildCompletedRun({
        status: 'Running',
        startedAtUtc: '2026-09-11T00:00:00Z',
        completedAtUtc: '2026-09-11T01:00:00Z',
        rerunStartedAtUtc: ninetySecondsAgo,
        // Left over from an earlier re-run of the same run; a legacy row predating the server-side
        // clear-on-start fix. Earlier than rerunStartedAtUtc, so elapsedMsBetween would clamp to 0
        // if it were passed as the end while the re-run is still running.
        rerunCompletedAtUtc: anHourBeforeThatStart,
        rerunScopeOrderIndexes: [4],
        rerunAnsweredOrderIndexes: [],
        rerunScoredOrderIndexes: []
      });

      expect(component.runElapsedIsRerun).toBe(true);
      expect(component.runElapsedLabel).toMatch(/^1m 3\ds$/);
    });

    it('should count only gradeable answers as the index population', () => {
      component.activeRunDetail = buildCompletedRun({
        status: 'Completed',
        totalQuestionCount: 4,
        answers: [
          buildScoredAnswer(1),
          buildScoredAnswer(2, { status: 'ProviderError' }),
          // Finished normally and produced nothing: scored 0 rather than excused, so it counts.
          buildScoredAnswer(3, { status: 'EmptyAnswer', providerFinishReason: 'end_turn' }),
          // No recorded finish reason is not evidence of a normal stop.
          buildScoredAnswer(4, { status: 'EmptyAnswer', providerFinishReason: null })
        ]
      });

      expect(component.runGradeableAnswerCount).toBe(2);
      expect(component.runDiagnosticsText).toContain('Gradeable answers (index population): 2 of 4');
    });

    it('should call zero knowledge-base calls prompt-compliant only when the suite has no knowledge-base topic', () => {
      const run = (hasKnowledgeBaseRoutingQuestion: boolean) => buildCompletedRun({
        status: 'Completed',
        toolFamilyCounts: { source: 2, wiki: 1 },
        zeroKnowledgeBaseAnswerCount: 2,
        hasKnowledgeBaseRoutingQuestion,
        answers: [buildScoredAnswer(1), buildScoredAnswer(2)]
      });

      component.activeRunDetail = run(false);
      let text = component.runDiagnosticsText;
      expect(text).toContain('answers with 0 knowledge base calls: 2 of 2 gradeable');
      expect(text).toContain('  (prompt-compliant on game-mechanics topics');
      expect(text).not.toContain('the suite has knowledge-base topics');

      component.activeRunDetail = run(true);
      text = component.runDiagnosticsText;
      expect(text).toContain('answers with 0 knowledge base calls: 2 of 2 gradeable');
      expect(text).toContain("  (the suite has knowledge-base topics; see the report's Tool Routing section)");
      expect(text).not.toContain('prompt-compliant');
    });

    it('should read the rerun meters against the client-captured scope before any re-run answer lands', () => {
      component.activeRunDetail = buildCompletedRun({
        status: 'Running',
        stage: 'Answering',
        totalQuestionCount: 18,
        answers: Array.from({ length: 18 }, (_, i) => buildScoredAnswer(i + 1)),
        rerunAnsweredOrderIndexes: [],
        rerunScoredOrderIndexes: []
      });
      component.rerunScopeOrderIndexes = [4, 9];

      expect(component.effectiveRerunScope).toEqual([4, 9]);
      expect(component.runMeterTotal).toBe(2);
      expect(component.runMeterAnswered).toBe(0);
      expect(component.runMeterScored).toBe(0);
      expect(component.runStageLabel).toContain('Answered 0 of 2 re-run questions');
    });

    it('should read the rerun meters as re-run answers land inside the scope', () => {
      component.activeRunDetail = buildCompletedRun({
        status: 'Running',
        stage: 'Answering',
        totalQuestionCount: 18,
        answers: Array.from({ length: 18 }, (_, i) => buildScoredAnswer(i + 1)),
        rerunAnsweredOrderIndexes: [4],
        rerunScoredOrderIndexes: [4]
      });
      component.rerunScopeOrderIndexes = [4, 9];

      expect(component.runMeterTotal).toBe(2);
      expect(component.runMeterAnswered).toBe(1);
      expect(component.runMeterScored).toBe(1);
      expect(component.runStageLabel).toContain('Answered 1 of 2 re-run questions');
    });

    it('should prefer the server-reported rerun scope over an empty client-captured one', () => {
      component.activeRunDetail = buildCompletedRun({
        rerunScopeOrderIndexes: [3, 7, 11]
      });
      component.rerunScopeOrderIndexes = [];

      expect(component.effectiveRerunScope).toEqual([3, 7, 11]);
    });

    it('should chip a Canceled row with the Canceled label and class', () => {
      const row = { orderIndex: 5, questionText: 'Q5', status: 'Canceled', assessmentStatus: '', errorMessage: null };

      expect(component.runRowChipLabel(row)).toBe('Canceled');
      expect(component.runRowChipClass(row)).toBe('status-canceled');
    });

    it('should format answer status 6 as Canceled', () => {
      expect(component.formatAnswerStatus(6)).toBe('Canceled');
    });

    it('should reject a speed difficulty scaling outside 0.0 to 5.0 before calling the server', () => {
      component.editingProfileId = null;
      component.profileForm = { ...component.profileForm, name: 'Scaled Profile', speedDifficultyScaling: 5.5 };

      component.saveProfile();

      expect(component.profileValidationErrors)
        .toContain('Speed difficulty scaling must be between 0.0 and 5.0.');
      expect(benchmarkServiceMock.createScoringProfile).not.toHaveBeenCalled();
    });
  });
});
