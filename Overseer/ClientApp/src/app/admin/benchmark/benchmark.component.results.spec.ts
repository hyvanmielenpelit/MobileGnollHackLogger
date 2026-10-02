import { ComponentFixture } from '@angular/core/testing';
import { AdminBenchmarkComponent } from './benchmark.component';
import { clearStoredState, createAdminBenchmarkFixture } from './benchmark.component.testing';

describe('AdminBenchmarkComponent', () => {
  let component: AdminBenchmarkComponent;
  let fixture: ComponentFixture<AdminBenchmarkComponent>;

  beforeEach(clearStoredState);

  afterEach(clearStoredState);

  beforeEach(async () => {
    ({ component, fixture } = await createAdminBenchmarkFixture());
  });

  describe('results screen: agreement, weighting and profile fit', () => {
    function buildFinishedRun(overrides: any = {}): any {
      return {
        id: 77,
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
        status: 'Completed',
        startedAtUtc: '2026-09-03T06:52:00Z',
        completedAtUtc: '2026-09-03T07:28:00Z',
        qualityIndex: 94,
        unweightedQualityIndex: 92,
        speedIndex: 67,
        totalAnswerDurationMs: 900000,
        totalDurationMs: 900000,
        scoringProfileId: 1,
        scoringProfileName: 'Standard Intelligence Index (Default)',
        scoringProfileSpeedTargetMs: 15000,
        scoringMethodVersion: 6,
        harnessVersion: '7',
        transportDefectAnswerCount: 0,
        advisoryFlagAnswerCount: 0,
        scrubbedArtifactAnswerCount: 0,
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
        errorMessage: null,
        answers: [],
        ...overrides
      };
    }

    function scoreCardText(label: string): string {
      const cards: HTMLElement[] = Array.from(fixture.nativeElement.querySelectorAll('.score-card'));
      const card = cards.find(c => (c.querySelector('.score-label')?.textContent || '').trim() === label);
      return (card?.textContent || '').replace(/\s+/g, ' ').trim();
    }

    it('should show the unweighted mean beside the weighted index, with the weighting delta', () => {
      component.selectedRunDetail = buildFinishedRun();
      fixture.detectChanges();

      expect(component.showUnweightedQualityTile).toBe(true);
      expect(component.weightingDeltaLabel).toBe('+2');
      expect(scoreCardText('Unweighted Mean')).toContain('92 / 100');
      expect(scoreCardText('Unweighted Mean')).toContain('equal weights · difficulty weighting moved the index +2');
    });

    it('should omit the unweighted tile when the two aggregations agree', () => {
      component.selectedRunDetail = buildFinishedRun({ qualityIndex: 92, unweightedQualityIndex: 92 });
      fixture.detectChanges();

      expect(component.showUnweightedQualityTile).toBe(false);
      expect(scoreCardText('Unweighted Mean')).toBe('');
    });

    it('should mark the Speed Index advisory for a deliberating candidate on an interactive profile', () => {
      component.selectedRunDetail = buildFinishedRun();
      fixture.detectChanges();

      expect(component.showRunProfileFitAdvisory).toBe(true);
      expect(scoreCardText('Speed Index')).toContain('*');
      expect(component.runProfileFitAdvisoryTitle).toContain('thinking level max');
    });

    it('should not mark it advisory against a profile that is not an interactive one', () => {
      component.selectedRunDetail = buildFinishedRun({ scoringProfileSpeedTargetMs: 30000 });
      fixture.detectChanges();

      expect(component.showRunProfileFitAdvisory).toBe(false);
    });

    it('should show the agreement tile with its coverage fraction beneath the value', () => {
      component.selectedRunDetail = buildFinishedRun({
        secondOpinionModeUsed: 1,
        secondOpinionGradedAnswerCount: 4,
        secondOpinionMeanAbsDelta: 4.25,
        secondOpinionDisagreementCount: 2
      });
      fixture.detectChanges();

      // The coverage never travels separately from the figure: 4 of 18 selected by trigger and
      // 18 of 18 are different measurements, and only the fraction tells them apart.
      expect(component.agreementCoverageLabel).toBe('4 of 18 answers graded twice');
      expect(component.agreementIsSelective).toBe(true);
      const text = scoreCardText('Assessor Agreement');
      expect(text).toContain('mean |Δ| 4.3 pts');
      expect(text).toContain('4 of 18 answers graded twice · Flagged only');
    });

    it('should name the signed mean after the absolute one', () => {
      component.selectedRunDetail = buildFinishedRun({
        secondOpinionModeUsed: 3,
        secondOpinionGradedAnswerCount: 18,
        secondOpinionMeanAbsDelta: 3.1,
        secondOpinionMeanSignedDelta: -0.4
      });
      fixture.detectChanges();

      expect(scoreCardText('Assessor Agreement')).toContain('mean |Δ| 3.1 pts · signed −0.4');
    });

    it('should give the trigger-selected cause alone when the sample is large enough', () => {
      component.selectedRunDetail = buildFinishedRun({
        secondOpinionModeUsed: 1,
        secondOpinionGradedAnswerCount: 6,
        secondOpinionMeanAbsDelta: 2.0
      });
      fixture.detectChanges();

      expect(component.showAgreementAdvisory).toBe(true);
      expect(component.agreementAdvisoryTitle)
        .toBe('Coverage is selected by trigger, so this is conditioned on the first assessor’s own uncertainty, not an unbiased agreement rate. n = 6 of 18.');
    });

    it('should give the small-sample cause alone for a small Every answer sample', () => {
      component.selectedRunDetail = buildFinishedRun({
        secondOpinionModeUsed: 3,
        secondOpinionGradedAnswerCount: 3,
        secondOpinionMeanAbsDelta: 2.0
      });
      fixture.detectChanges();

      expect(component.showAgreementAdvisory).toBe(true);
      expect(component.agreementAdvisoryTitle)
        .toBe('Only n = 3 of 18 answers were graded twice, too few for a mean to be an agreement rate.');
    });

    it('should give both causes when coverage is selective and the sample small', () => {
      component.selectedRunDetail = buildFinishedRun({
        secondOpinionModeUsed: 1,
        secondOpinionGradedAnswerCount: 2,
        secondOpinionMeanAbsDelta: 2.0
      });
      fixture.detectChanges();

      const title = component.agreementAdvisoryTitle;
      expect(title).toContain('Coverage is selected by trigger');
      expect(title).toContain('Only n = 2 of 18 answers were graded twice');
    });

    it('should drop the selective caveat when every answer was graded twice', () => {
      component.selectedRunDetail = buildFinishedRun({
        secondOpinionModeUsed: 3,
        secondOpinionGradedAnswerCount: 18,
        secondOpinionMeanAbsDelta: 3.0
      });
      fixture.detectChanges();

      expect(component.agreementCoverageLabel).toBe('18 of 18 answers graded twice');
      expect(component.agreementIsSelective).toBe(false);
      expect(scoreCardText('Assessor Agreement')).toContain('Every answer');
    });

    it('should label FlaggedPlusSample rather than falling through to Manual only', () => {
      component.selectedRunDetail = buildFinishedRun({
        secondOpinionModeUsed: 4,
        secondOpinionGradedAnswerCount: 6,
        secondOpinionMeanAbsDelta: 2.5
      });
      fixture.detectChanges();

      expect(component.agreementModeLabel).toBe('Flagged plus sample');
      const text = scoreCardText('Assessor Agreement');
      expect(text).toContain('Flagged plus sample');
      expect(text).not.toContain('Manual only');
    });

    it('should hide the agreement tile when nothing was graded twice', () => {
      component.selectedRunDetail = buildFinishedRun({ secondOpinionGradedAnswerCount: 0 });
      fixture.detectChanges();

      expect(component.showAgreementTile).toBe(false);
      expect(scoreCardText('Assessor Agreement')).toBe('');
    });

    describe('key figures: notes, Critical Errors and Answered', () => {
      function answer(orderIndex: number, overrides: any = {}): any {
        return {
          id: orderIndex, orderIndex, questionText: `Q${orderIndex}`, difficulty: 1, answerText: 'a',
          status: 'Ok', assessmentStatus: 'Scored', durationMs: 1000, modelTimeMs: 1000,
          scrubbedArtifactCount: 0, answerFlags: 0, answerFlagNames: [], qualityScore: 80,
          ...overrides
        };
      }

      function figureCard(key: string): HTMLElement | null {
        return fixture.nativeElement.querySelector(`.score-card[data-figure="${key}"]`);
      }

      function figureNotes(key: string): string[] {
        return Array.from(figureCard(key)?.querySelectorAll('.score-note') ?? [])
          .map(n => (n.textContent || '').replace(/\s+/g, ' ').trim());
      }

      function figureValue(key: string): string {
        return (figureCard(key)?.querySelector('.score-subvalue')?.textContent || '').replace(/\s+/g, ' ').trim();
      }

      it('should say the raw index is before the caps', () => {
        component.selectedRunDetail = buildFinishedRun({ rawQualityIndex: 97 });
        fixture.detectChanges();

        expect(figureNotes('raw-quality')).toEqual(['before critical-error caps']);
      });

      it('should say the holistic score is not an index on a single-assessor run', () => {
        component.selectedRunDetail = buildFinishedRun({ finalScore: 90 });
        fixture.detectChanges();

        expect(figureNotes('holistic')).toEqual(['assessor\'s whole-run judgment, not an index']);
      });

      it('should mark the holistic fallback on a run with no per-question index', () => {
        component.selectedRunDetail = buildFinishedRun({ qualityIndex: null, unweightedQualityIndex: null, finalScore: 81 });
        fixture.detectChanges();

        const card = figureCard('intelligence')!;
        expect(card.querySelector('.score-value')?.textContent).toContain('81 / 100');
        expect(figureNotes('intelligence')).toContain('holistic score — this run has no per-question index');
      });

      it('should not mark the fallback when the index exists', () => {
        component.selectedRunDetail = buildFinishedRun({ finalScore: 81 });
        fixture.detectChanges();

        expect(figureNotes('intelligence')).not.toContain('holistic score — this run has no per-question index');
      });

      it('should count critical errors and name their questions', () => {
        component.selectedRunDetail = buildFinishedRun({
          answers: [answer(7, { criticalError: true }), answer(3, { criticalError: true }), answer(5)]
        });
        fixture.detectChanges();

        expect(component.keyFigureCriticalErrorAnswers.map(a => a.orderIndex)).toEqual([3, 7]);
        expect(figureValue('critical-errors')).toBe('2');
        expect(figureNotes('critical-errors')).toEqual(['Q3, Q7']);
      });

      it('should read None with no critical error', () => {
        component.selectedRunDetail = buildFinishedRun({ answers: [answer(1), answer(2)] });
        fixture.detectChanges();

        expect(figureValue('critical-errors')).toBe('None');
        expect(figureNotes('critical-errors')).toEqual([]);
      });

      it('should count member B\'s critical errors on a panel run, as the report does', () => {
        component.selectedRunDetail = buildFinishedRun({
          isPanelRun: true,
          answers: [answer(1, { criticalError: true }), answer(2, { coAssessmentCriticalError: true }), answer(3)]
        });
        fixture.detectChanges();

        expect(component.keyFigureCriticalErrorAnswers.length).toBe(2);
        expect(figureValue('critical-errors')).toBe('2');
        expect(figureNotes('critical-errors')).toEqual(['Q1, Q2']);
      });

      it('should ignore member B\'s flag outside a panel run', () => {
        component.selectedRunDetail = buildFinishedRun({
          answers: [answer(1, { coAssessmentCriticalError: true })]
        });
        fixture.detectChanges();

        expect(figureValue('critical-errors')).toBe('None');
      });

      it('should say how many critical errors the second reader disputed', () => {
        component.selectedRunDetail = buildFinishedRun({
          answers: [
            answer(1, { criticalError: true, secondOpinionCriticalError: false }),
            answer(2, { criticalError: true, secondOpinionCriticalError: true })
          ]
        });
        fixture.detectChanges();

        expect(figureNotes('critical-errors')).toEqual(['Q1, Q2', '1 disputed by the second reader']);
      });

      it('should name the reference reader on a panel run', () => {
        component.selectedRunDetail = buildFinishedRun({
          isPanelRun: true,
          answers: [answer(1, { criticalError: true, secondOpinionCriticalError: false })]
        });
        fixture.detectChanges();

        expect(figureNotes('critical-errors')).toEqual(['Q1', '1 disputed by the reference reader']);
      });

      it('should hide the Critical Errors card on a run with no answer rows', () => {
        component.selectedRunDetail = buildFinishedRun({ answers: [] });
        fixture.detectChanges();

        expect(figureCard('critical-errors')).toBeNull();
        expect(component.shownKeyFigureKeys).not.toContain('critical-errors');
        expect(component.shownKeyFigureKeys).toContain('answered');
      });

      describe('from scoring method 13', () => {
        function outcomeSummary(overrides: any = {}): any {
          return {
            correctCount: 10, partialCount: 4, incorrectCount: 2, notAttemptedCount: 1, noAnswerCount: 1,
            classifiedCount: 17,
            confirmedCriticalErrorCount: 1, unresolvedCriticalErrorCount: 1, overturnedCriticalErrorCount: 1,
            criticalErrorRate: 1 / 17, criticalErrorRateLow: 0.0104, criticalErrorRateHigh: 0.2598,
            correctWhenAttempted: 10 / 16, wrongInsteadOfAbstaining: 2 / 3,
            confirmedCriticalErrorQuestions: [3], unresolvedCriticalErrorQuestions: [5],
            overturnedCriticalErrorQuestions: [9], notAttemptedQuestions: [11],
            ...overrides
          };
        }

        function resolvedAnswers(): any[] {
          return [
            answer(3, { criticalError: true, coAssessmentCriticalError: true, criticalErrorResolution: 'Agreed' }),
            answer(5, { criticalError: true, coAssessmentCriticalError: false, criticalErrorResolution: 'Unresolved' }),
            answer(9, { criticalError: false, coAssessmentCriticalError: true, criticalErrorResolution: 'OverturnedByVerifier' }),
            answer(11, { criticalErrorResolution: 'None', outcomeClass: 'NotAttempted' })
          ];
        }

        it('should count confirmed critical errors, with the rate, unresolved and overturned', () => {
          component.selectedRunDetail = buildFinishedRun({
            scoringMethodVersion: 13,
            isPanelRun: true,
            outcomeSummary: outcomeSummary(),
            answers: resolvedAnswers()
          });
          fixture.detectChanges();

          expect(component.keyFigureCriticalErrorAnswers.map(a => a.orderIndex)).toEqual([3]);
          expect(figureValue('critical-errors')).toBe('1');
          expect(figureNotes('critical-errors')).toEqual([
            'Q3',
            'rate 6 % (95 % CI 1–26 %)',
            '1 unresolved (Q5) · 1 overturned by the verifier (Q9)'
          ]);
        });

        it('should read None with no confirmed critical error and still give the rate', () => {
          component.selectedRunDetail = buildFinishedRun({
            scoringMethodVersion: 13,
            outcomeSummary: outcomeSummary({
              confirmedCriticalErrorCount: 0, unresolvedCriticalErrorCount: 0, overturnedCriticalErrorCount: 0,
              confirmedCriticalErrorQuestions: [], unresolvedCriticalErrorQuestions: [], overturnedCriticalErrorQuestions: [],
              criticalErrorRate: 0, criticalErrorRateLow: 0, criticalErrorRateHigh: 0.184
            }),
            answers: [answer(1, { criticalErrorResolution: 'None' }), answer(2, { criticalErrorResolution: 'None' })]
          });
          fixture.detectChanges();

          expect(figureValue('critical-errors')).toBe('None');
          expect(figureNotes('critical-errors')).toEqual(['rate 0 % (95 % CI 0–18 %)']);
        });

        it('should keep the flag rule and its notes before scoring method 13', () => {
          component.selectedRunDetail = buildFinishedRun({
            scoringMethodVersion: 12,
            isPanelRun: true,
            outcomeSummary: outcomeSummary(),
            answers: resolvedAnswers()
          });
          fixture.detectChanges();

          expect(figureValue('critical-errors')).toBe('3');
          expect(figureNotes('critical-errors')).toEqual(['Q3, Q5, Q9']);
        });

        it('should keep the key figure cards and their order', () => {
          component.selectedRunDetail = buildFinishedRun({ scoringMethodVersion: 12, answers: resolvedAnswers() });
          fixture.detectChanges();
          const before = [...component.shownKeyFigureKeys];

          component.selectedRunDetail = buildFinishedRun({
            scoringMethodVersion: 13, outcomeSummary: outcomeSummary(), answers: resolvedAnswers()
          });
          fixture.detectChanges();

          expect(component.shownKeyFigureKeys).toEqual(before);
        });
      });

      it('should show every question answered', () => {
        component.selectedRunDetail = buildFinishedRun();
        fixture.detectChanges();

        expect(figureValue('answered')).toBe('18 / 18');
        expect(figureNotes('answered')).toEqual(['every question answered']);
      });

      it('should give one clause per cause, adding up to the shortfall', () => {
        const answers = Array.from({ length: 17 }, (_, i) => answer(i + 1));
        component.selectedRunDetail = buildFinishedRun({
          answeredQuestionCount: 12,
          totalQuestionCount: 18,
          unansweredQuestionCount: 2,
          terminalFailureAnswerCount: 1,
          answers
        });
        fixture.detectChanges();

        expect(figureValue('answered')).toBe('12 / 18');
        const note = component.answeredNote;
        expect(note).toBe('2 without text · 1 failed at the provider · 1 never asked · 2 other errors');
        const sum = note.split(' · ').reduce((total, clause) => total + parseInt(clause, 10), 0);
        expect(sum).toBe(18 - 12);
      });

      it('should use the singular for one other error', () => {
        component.selectedRunDetail = buildFinishedRun({
          answeredQuestionCount: 17,
          totalQuestionCount: 18,
          unansweredQuestionCount: 0,
          answers: Array.from({ length: 18 }, (_, i) => answer(i + 1))
        });

        expect(component.answeredNote).toBe('1 other error');
      });

      it('should cap the clauses at the shortfall', () => {
        component.selectedRunDetail = buildFinishedRun({
          answeredQuestionCount: 16,
          totalQuestionCount: 18,
          unansweredQuestionCount: 2,
          terminalFailureAnswerCount: 3,
          answers: Array.from({ length: 18 }, (_, i) => answer(i + 1))
        });

        expect(component.answeredNote).toBe('2 without text');
      });

      it('should not report never-asked questions from an empty answer list', () => {
        component.selectedRunDetail = buildFinishedRun({
          answeredQuestionCount: 16,
          totalQuestionCount: 18,
          unansweredQuestionCount: 0,
          answers: []
        });

        expect(component.answeredNote).toBe('2 other errors');
      });

      it('should place the two new cards after the quality cards', () => {
        component.selectedRunDetail = buildFinishedRun({ answers: [answer(1)] });
        fixture.detectChanges();

        const keys: string[] = Array.from(fixture.nativeElement.querySelectorAll('.rr-figures .score-card'))
          .map(c => (c as HTMLElement).getAttribute('data-figure') ?? '');
        expect(keys.indexOf('critical-errors')).toBe(keys.indexOf('unweighted-mean') + 1);
        expect(keys.indexOf('answered')).toBe(keys.indexOf('critical-errors') + 1);
        expect(keys.indexOf('speed')).toBe(keys.indexOf('answered') + 1);
        expect(component.shownKeyFigureKeys as string[]).toEqual(keys);
      });
    });

    it('should name the questions on the tool-budget line and report contested and re-assessed answers', () => {
      component.selectedRunDetail = buildFinishedRun({
        toolStarvedAnswerCount: 1,
        contestedVerdictAnswerCount: 2,
        reassessedAnswerCount: 1,
        answers: [
          { id: 1, orderIndex: 10, questionText: 'Q10', difficulty: 1, answerText: 'a', status: 'Ok', assessmentStatus: 'Scored', durationMs: 1, modelTimeMs: 1, scrubbedArtifactCount: 0, answerFlags: 32, answerFlagNames: ['ContestedVerdict'], toolBudgetExhausted: true, qualityScore: 60 },
          { id: 2, orderIndex: 11, questionText: 'Q11', difficulty: 1, answerText: 'b', status: 'Ok', assessmentStatus: 'Scored', durationMs: 1, modelTimeMs: 1, scrubbedArtifactCount: 0, answerFlags: 32, answerFlagNames: ['ContestedVerdict'], qualityScore: 84, reassessmentCount: 1, previousQualityScore: 60, reassessedByModelDisplayNameUsed: 'Claude Opus 5' }
        ]
      });
      fixture.detectChanges();

      expect(component.toolBudgetQuestionNumbers).toBe('10');
      expect(component.contestedVerdictQuestionNumbers).toBe('10, 11');
      expect(component.reassessedQuestionNumbers).toBe('11');

      const notices: HTMLElement[] = Array.from(fixture.nativeElement.querySelectorAll('.alert-heading'));
      const heading = notices.find(n => (n.textContent || '').includes('Run Integrity Notice'));
      const body = ((heading?.parentElement?.querySelector('.alert-body') as HTMLElement)?.textContent || '')
        .replace(/\s+/g, ' ').trim();

      expect(body).toContain('harness limit (tool budget) (question(s) 10)');
      expect(body).toContain('2 contested verdict(s)');
      expect(body).toContain('1 answer(s) were re-assessed after the run finished');
      expect(body).toContain('The Speed Index is advisory for this run');
    });

    it('should list the assessor\'s unverified claims on the answer that carried them', () => {
      const answer: any = {
        id: 1, orderIndex: 1, questionText: 'Q1', difficulty: 1, answerText: 'a',
        status: 'Ok', assessmentStatus: 'Scored', durationMs: 1, modelTimeMs: 1,
        scrubbedArtifactCount: 0, answerFlags: 0, answerFlagNames: [], qualityScore: 60,
        unverifiedClaimCount: 2,
        unverifiedClaimsJson: '["gnomes gain infravision","orcs gain poison resistance"]'
      };
      expect(component.unverifiedClaimsOf(answer).length).toBe(2);
      // A malformed blob costs the panel, never the screen.
      expect(component.unverifiedClaimsOf({ ...answer, unverifiedClaimsJson: '{oops' }).length).toBe(0);
      expect(component.unverifiedClaimTotal).toBe(0);

      component.selectedRunDetail = buildFinishedRun({ answers: [answer] });
      expect(component.unverifiedClaimTotal).toBe(2);
    });

    it('should render the zero-coverage clause in the integrity notice when second opinion was selected but unused', () => {
      component.selectedRunDetail = buildFinishedRun({
        secondOpinionAssessorModelConfigurationId: 4,
        secondOpinionAssessorModelDisplayNameUsed: 'Claude Opus 5',
        secondOpinionGradedAnswerCount: 0
      });
      fixture.detectChanges();

      expect(component.secondOpinionSelectedButUnused).toBe(true);
      const notices: HTMLElement[] = Array.from(fixture.nativeElement.querySelectorAll('.alert-heading'));
      const heading = notices.find(n => (n.textContent || '').includes('Run Integrity Notice'));
      const body = ((heading?.parentElement?.querySelector('.alert-body') as HTMLElement)?.textContent || '')
        .replace(/\s+/g, ' ').trim();

      expect(body).toContain('A second reader was selected but no answer met a trigger');
      expect(body).toContain('grader agreement is not measured for this run');
    });

    it('should render claim verification rows and colour only Refuted verdicts', () => {
      const answer: any = {
        id: 1, orderIndex: 1, questionText: 'Q1', difficulty: 1, answerText: 'a',
        status: 'Ok', assessmentStatus: 'Scored', durationMs: 1, modelTimeMs: 1,
        scrubbedArtifactCount: 0, answerFlags: 0, answerFlagNames: [], qualityScore: 80,
        claimVerificationJson: JSON.stringify([
          { claimIndex: 0, claim: 'Claim 1', verdict: 'Supported', citation: 'src/a.c', basis: 'Valid.' },
          { claimIndex: 1, claim: 'Claim 2', verdict: 'Refuted', citation: 'src/b.c', basis: 'Invalid.' },
          { claimIndex: 2, claim: 'Claim 3', verdict: 'Indeterminate', citation: null, basis: 'Unknown.' }
        ])
      };

      component.selectedRunDetail = buildFinishedRun({ answers: [answer] });
      component.expandedQuestions.add(1);
      fixture.detectChanges();

      const verifications = component.claimVerificationsOf(answer);
      expect(verifications.length).toBe(3);

      const box = fixture.nativeElement.querySelector('.claim-verification-box');
      expect(box).toBeTruthy();

      const pills: HTMLElement[] = Array.from(box.querySelectorAll('.verdict-pill'));
      expect(pills.length).toBe(3);
      expect(pills[0].classList.contains('verdict-refuted')).toBe(false);
      expect(pills[1].classList.contains('verdict-refuted')).toBe(true);
      expect(pills[2].classList.contains('verdict-refuted')).toBe(false);
    });

    function renderClaimVerifications(claimVerificationJson: string): HTMLElement {
      const answer: any = {
        id: 1, orderIndex: 1, questionText: 'Q1', difficulty: 1, answerText: 'a',
        status: 'Ok', assessmentStatus: 'Scored', durationMs: 1, modelTimeMs: 1,
        scrubbedArtifactCount: 0, answerFlags: 0, answerFlagNames: [], qualityScore: 80,
        claimVerificationJson
      };
      component.selectedRunDetail = buildFinishedRun({ answers: [answer] });
      fixture.detectChanges();
      (fixture.nativeElement.querySelector('.question-card-header') as HTMLElement).click();
      fixture.detectChanges();
      return fixture.nativeElement.querySelector('.claim-verification-box') as HTMLElement;
    }

    it('should label the entries the harness sent to check the assessor', () => {
      const box = renderClaimVerifications(JSON.stringify([
        { claimIndex: 0, claim: 'Quoted as critical', verdict: 'Supported', citation: 'src/a.c', basis: 'True.', roles: ['criticalErrorQuote'] },
        { claimIndex: 1, claim: 'Charged as false', verdict: 'Supported', citation: 'src/b.c', basis: 'True.', roles: ['accusedQuote'] },
        { claimIndex: 2, claim: 'Unverified and charged', verdict: 'Refuted', citation: 'src/c.c', basis: 'False.', roles: ['unverifiedClaim', 'accusedQuote'] },
        { claimIndex: 3, claim: 'Own claim', verdict: 'Indeterminate', citation: null, basis: 'Unknown.', roles: ['unverifiedClaim'] }
      ]));

      expect(box).toBeTruthy();
      const items: HTMLElement[] = Array.from(box.querySelectorAll('.claim-verification-item'));
      expect(items.length).toBe(4);
      const labelsOf = (item: HTMLElement) =>
        Array.from(item.querySelectorAll('.claim-role-label')).map(l => (l.textContent || '').trim());
      expect(labelsOf(items[0])).toEqual(['(critical-error quote)']);
      expect(labelsOf(items[1])).toEqual(['(sentence the assessor charged as false)']);
      expect(labelsOf(items[2])).toEqual(['(sentence the assessor charged as false)']);
      expect(labelsOf(items[3])).toEqual([]);
      expect(box.querySelector('.claim-roles-note')).toBeTruthy();
    });

    it('should name the panel member who submitted each checked entry', () => {
      const box = renderClaimVerifications(JSON.stringify([
        { claimIndex: 0, claim: 'Quoted as critical', verdict: 'Supported', citation: 'src/a.c', basis: 'True.', roles: ['criticalErrorQuote'], raisedBy: ['A'] },
        { claimIndex: 1, claim: 'Charged as false', verdict: 'Supported', citation: 'src/b.c', basis: 'True.', roles: ['accusedQuote'], raisedBy: ['B'] },
        { claimIndex: 2, claim: 'Basis', verdict: 'Refuted', citation: 'src/c.c', basis: 'False.', roles: ['outOfRubricBasis'], raisedBy: ['A', 'B'] },
        { claimIndex: 3, claim: 'Own claim', verdict: 'Indeterminate', citation: null, basis: 'Unknown.', roles: ['unverifiedClaim'], raisedBy: ['A'] }
      ]));

      const items: HTMLElement[] = Array.from(box.querySelectorAll('.claim-verification-item'));
      const labelsOf = (item: HTMLElement) =>
        Array.from(item.querySelectorAll('.claim-role-label')).map(l => (l.textContent || '').trim());
      expect(labelsOf(items[0])).toEqual(['(critical-error quote from member A)']);
      expect(labelsOf(items[1])).toEqual(['(sentence member B charged as false)']);
      expect(labelsOf(items[2])).toEqual(['(out-of-rubric basis from both members)']);
      expect(labelsOf(items[3])).toEqual([]);

      // Without raisedBy, a single-assessor run's labels are unchanged.
      expect(component.claimRoleLabels(['criticalErrorQuote', 'outOfRubricBasis', 'accusedQuote']))
        .toEqual(['critical-error quote', 'out-of-rubric basis', 'sentence the assessor charged as false']);
    });

    it('should name only the accusing member of a sentence both members raised', () => {
      const box = renderClaimVerifications(JSON.stringify([
        { claimIndex: 0, claim: 'Charged by A', verdict: 'Supported', citation: 'src/a.c', basis: 'True.', roles: ['unverifiedClaim', 'accusedQuote'], raisedBy: ['A', 'B'], accusedBy: ['A'] },
        { claimIndex: 1, claim: 'Legacy record', verdict: 'Supported', citation: 'src/b.c', basis: 'True.', roles: ['accusedQuote'], raisedBy: ['A', 'B'] }
      ]));

      const items: HTMLElement[] = Array.from(box.querySelectorAll('.claim-verification-item'));
      const labelsOf = (item: HTMLElement) =>
        Array.from(item.querySelectorAll('.claim-role-label')).map(l => (l.textContent || '').trim());
      expect(labelsOf(items[0])).toEqual(['(sentence member A charged as false)']);
      // A record stored before harness 44 has no accusedBy and falls back to raisedBy.
      expect(labelsOf(items[1])).toEqual(['(sentence both members charged as false)']);
    });

    it('should render a legacy verification record without roles and without labels', () => {
      const box = renderClaimVerifications(JSON.stringify([
        { claimIndex: 0, claim: 'Claim 1', verdict: 'Supported', citation: 'src/a.c', basis: 'Valid.' }
      ]));

      expect(box).toBeTruthy();
      expect(box.querySelectorAll('.claim-verification-item').length).toBe(1);
      expect(box.querySelectorAll('.claim-role-label').length).toBe(0);
      expect(box.querySelector('.claim-roles-note')).toBeNull();
    });

    describe('Band drift', () => {
      function bandedAnswer(orderIndex: number, difficulty: number, assessedDifficulty: number): any {
        return {
          id: orderIndex, orderIndex, questionText: `Q${orderIndex}`, difficulty, assessedDifficulty,
          answerText: 'a', status: 'Ok', assessmentStatus: 'Scored', durationMs: 1, modelTimeMs: 1,
          scrubbedArtifactCount: 0, answerFlags: 0, answerFlagNames: [], qualityScore: 80
        };
      }

      function bandSectionText(): string {
        const section = fixture.nativeElement.querySelector('.band-agreement-section');
        return ((section as HTMLElement)?.textContent || '').replace(/\s+/g, ' ').trim();
      }

      it('should call out a drift that moved every mismatch the same way', () => {
        // Two authored Simple (midpoint 25) assessed into the Intermediate band, one authored
        // Intermediate (midpoint 55) assessed into Advanced: +30, +40, +30, all upward.
        component.selectedRunDetail = buildFinishedRun({
          answers: [
            bandedAnswer(1, 1, 55), bandedAnswer(2, 1, 65),
            bandedAnswer(3, 2, 85), bandedAnswer(4, 1, 25)
          ]
        });
        fixture.detectChanges();

        expect(component.bandDisagreements().length).toBe(3);
        const drift = component.bandDriftSummary()!;
        expect(drift.up).toBe(3);
        expect(drift.down).toBe(0);
        expect(drift.meanDelta).toBeCloseTo(33.3, 1);
        expect(drift.oneDirection).toBe('up');

        const text = bandSectionText();
        expect(text).toContain('3 assessed harder than authored, 0 easier');
        expect(text).toContain('+33.3');
        expect(text).toContain('Every mismatch moved the same way — upward');
      });

      it('should not claim a direction when the mismatches disagree', () => {
        component.selectedRunDetail = buildFinishedRun({
          answers: [bandedAnswer(1, 1, 55), bandedAnswer(2, 3, 25)]
        });
        fixture.detectChanges();

        const drift = component.bandDriftSummary()!;
        expect(drift.up).toBe(1);
        expect(drift.down).toBe(1);
        expect(drift.oneDirection).toBeNull();
        expect(bandSectionText()).not.toContain('Every mismatch moved the same way');
      });

      it('should render no drift line when every question stayed in its authored band', () => {
        component.selectedRunDetail = buildFinishedRun({
          answers: [bandedAnswer(1, 1, 25), bandedAnswer(2, 2, 55)]
        });
        fixture.detectChanges();

        expect(component.bandDisagreements().length).toBe(0);
        expect(component.bandDriftSummary()).toBeNull();
        expect(fixture.nativeElement.querySelector('.band-agreement-section')).toBeNull();
      });
    });

    describe('Speed Index saturation', () => {
      function scoredAnswer(orderIndex: number, speedScore: number, modelTimeMs = 1): any {
        return {
          id: orderIndex, orderIndex, questionText: `Q${orderIndex}`, difficulty: 1, answerText: 'a',
          status: 'Ok', assessmentStatus: 'Scored', durationMs: modelTimeMs, modelTimeMs,
          scrubbedArtifactCount: 0, answerFlags: 0, answerFlagNames: [], qualityScore: 80, speedScore
        };
      }

      /**
       * The speed card, found by either of its two labels: the card leads with the Speed Index
       * ordinarily and with median model time once the index cannot discriminate.
       */
      function speedCard(): HTMLElement | undefined {
        const cards: HTMLElement[] = Array.from(fixture.nativeElement.querySelectorAll('.score-card'));
        return cards.find(c => {
          const label = (c.querySelector('.score-label')?.textContent || '').trim();
          return label === 'Speed Index' || label === 'Median Model Time';
        });
      }

      function speedCardLabel(): string {
        return (speedCard()?.querySelector('.score-label')?.textContent || '').trim();
      }

      function speedCardValueText(): string {
        return ((speedCard()?.querySelector('.score-subvalue') as HTMLElement)?.textContent || '')
          .replace(/\s+/g, ' ').trim();
      }

      function speedIndexNoteText(): string {
        const notes: HTMLElement[] = Array.from(speedCard()?.querySelectorAll('.score-note') ?? []);
        return notes.map(n => (n.textContent || '').replace(/\s+/g, ' ').trim()).join(' ');
      }

      it('should lead with median model time once exactly half the scored answers sit at the ceiling', () => {
        component.selectedRunDetail = buildFinishedRun({
          scoringProfileSpeedTargetMs: 30000,
          answers: [
            scoredAnswer(1, 100, 1000), scoredAnswer(2, 100, 2000),
            scoredAnswer(3, 50, 3000), scoredAnswer(4, 50, 4000)
          ]
        });
        fixture.detectChanges();

        expect(component.speedIndexScoredAnswerCount).toBe(4);
        expect(component.speedIndexCeilingAnswerCount).toBe(2);
        expect(component.showSpeedIndexSaturationAdvisory).toBe(true);
        expect(component.demoteSpeedIndex).toBe(true);
        expect(component.medianModelTimeMs).toBe(2500);
        expect(speedCardLabel()).toBe('Median Model Time');
        expect(speedCardValueText()).toBe('2.5 s');
        expect(speedIndexNoteText()).toContain('Speed Index');
        expect(speedIndexNoteText()).toContain('saturated: 2 of 4 at the ceiling');
      });

      it('should keep the concurrency marker on the demoted card and on Mean Time', () => {
        component.selectedRunDetail = buildFinishedRun({
          scoringProfileSpeedTargetMs: 30000,
          speedMeasurementDegraded: true,
          answers: [
            scoredAnswer(1, 100, 1000), scoredAnswer(2, 100, 2000),
            scoredAnswer(3, 50, 3000), scoredAnswer(4, 50, 4000)
          ]
        });
        fixture.detectChanges();

        expect(component.demoteSpeedIndex).toBe(true);
        const marker = speedCard()?.querySelector('.score-subvalue .degraded-tag');
        expect(marker?.getAttribute('title')).toBe('Concurrency enabled; speed advisory');
        const cards: HTMLElement[] = Array.from(fixture.nativeElement.querySelectorAll('.score-card'));
        const meanTime = cards.find(c => c.getAttribute('data-figure') === 'mean-time');
        expect(meanTime?.querySelector('.score-subvalue .degraded-tag')?.getAttribute('title'))
          .toBe('Concurrency enabled; speed advisory');
      });

      it('should not flag saturation just below half', () => {
        component.selectedRunDetail = buildFinishedRun({
          scoringProfileSpeedTargetMs: 30000,
          answers: [scoredAnswer(1, 100), scoredAnswer(2, 100), scoredAnswer(3, 50), scoredAnswer(4, 50), scoredAnswer(5, 50)]
        });
        fixture.detectChanges();

        expect(component.speedIndexScoredAnswerCount).toBe(5);
        expect(component.speedIndexCeilingAnswerCount).toBe(2);
        expect(component.showSpeedIndexSaturationAdvisory).toBe(false);
        expect(component.demoteSpeedIndex).toBe(false);
        expect(speedCardLabel()).toBe('Speed Index');
        expect(speedIndexNoteText()).not.toContain('saturated');
      });
    });

    describe('instrument measurements', () => {
      function measurementsText(): string {
        const notes: HTMLElement[] = Array.from(fixture.nativeElement.querySelectorAll('.alert-info'));
        const block = notes.find(n => (n.querySelector('.alert-heading')?.textContent || '')
          .includes('Instrument Measurements'));
        return (block?.textContent || '').replace(/\s+/g, ' ').trim();
      }

      it('should report both counts as measurements, outside the integrity notice', () => {
        component.selectedRunDetail = buildFinishedRun({
          completenessOutOfScopeCount: 2,
          readabilityFormOnlyCount: 3,
          completenessOutOfScopeDeductedCount: 1,
          readabilityFormOnlyDeductedCount: 2
        });
        fixture.detectChanges();

        expect(component.completenessOutOfScopeCount).toBe(2);
        expect(component.readabilityFormOnlyCount).toBe(3);
        expect(component.completenessOutOfScopeDeductedCount).toBe(1);
        expect(component.readabilityFormOnlyDeductedCount).toBe(2);
        expect(component.hasInstrumentMeasurements).toBe(true);
        expect(measurementsText()).toContain('2 out-of-scope completeness deduction(s)');
        expect(measurementsText()).toContain('3 rubric format suggestion(s) not followed');
        expect(measurementsText()).toContain('1 of them sit beside a Completeness level below 6');
        expect(measurementsText()).toContain('2 of them sit beside a Readability level below 6');
      });

      it('should show only the count that was recorded', () => {
        component.selectedRunDetail = buildFinishedRun({
          completenessOutOfScopeCount: 0,
          readabilityFormOnlyCount: 1
        });
        fixture.detectChanges();

        expect(component.hasInstrumentMeasurements).toBe(true);
        expect(measurementsText()).not.toContain('out-of-scope completeness deduction(s)');
        expect(measurementsText()).toContain('1 rubric format suggestion(s) not followed');
      });

      it('should give each panel member its own counts, and name member A on the docked subsets', () => {
        const memberB = (orderIndex: number, flags: any): any => ({
          id: 600 + orderIndex, orderIndex, questionText: `Q${orderIndex}`, difficulty: 1, answerText: 'a',
          status: 'Ok', assessmentStatus: 'Scored', durationMs: 1, modelTimeMs: 1,
          scrubbedArtifactCount: 0, answerFlags: 0, answerFlagNames: [], qualityScore: 80,
          panelQualityScore: 78, coAssessmentStatus: 'Scored', coAssessmentQualityScore: 76,
          coAssessmentCriticalError: false, coAssessmentJson: JSON.stringify({ qualityScore: 76, flags })
        });
        component.selectedRunDetail = buildFinishedRun({
          isPanelRun: true,
          completenessOutOfScopeCount: 0,
          readabilityFormOnlyCount: 8,
          readabilityFormOnlyDeductedCount: 2,
          answers: [
            memberB(1, { readabilityFormOnly: true, completenessOutOfScope: true }),
            memberB(2, { readabilityFormOnly: true }),
            memberB(3, {})
          ]
        });
        fixture.detectChanges();

        expect(component.memberBReadabilityFormOnlyCount).toBe(2);
        expect(component.memberBCompletenessOutOfScopeCount).toBe(1);
        expect(component.hasInstrumentMeasurements).toBe(true);
        const text = measurementsText();
        expect(text).toContain('8 (member A) and 2 (member B) rubric format suggestion(s) not followed');
        expect(text).toContain("2 of member A's sit beside a Readability level below 6");
        // Member A recorded none, member B one: the line shows because either member counted it.
        expect(text).toContain('0 (member A) and 1 (member B) out-of-scope completeness deduction(s)');
        expect(text).toContain('rubric points a panel member itself placed outside what the question asked');
        expect(text).not.toContain('the assessor itself');
      });

      it('should stay hidden when neither was recorded', () => {
        // Zero is not a finding here: a run graded before either marker existed reports zero too.
        component.selectedRunDetail = buildFinishedRun({
          completenessOutOfScopeCount: 0,
          readabilityFormOnlyCount: 0
        });
        fixture.detectChanges();

        expect(component.hasInstrumentMeasurements).toBe(false);
        expect(measurementsText()).toBe('');
      });
    });
  });
});
