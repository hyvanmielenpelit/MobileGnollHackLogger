import type { MockedObject } from "vitest";
import { ComponentFixture, TestBed, fakeAsync, tick, discardPeriodicTasks } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { ModelPickerComponent } from '../../shared/model-picker/model-picker.component';
import { of, throwError } from 'rxjs';
import { AdminBenchmarkComponent } from './benchmark.component';
import { AdminBenchmarkService } from '../../services/admin-benchmark.service';
import { clearStoredState, createAdminBenchmarkFixture, RUN_SETTINGS_KEY } from './benchmark.component.testing';

describe('AdminBenchmarkComponent', () => {
  let component: AdminBenchmarkComponent;
  let fixture: ComponentFixture<AdminBenchmarkComponent>;
  let benchmarkServiceMock: MockedObject<AdminBenchmarkService>;

  beforeEach(clearStoredState);

  afterEach(clearStoredState);

  beforeEach(async () => {
    ({ component, fixture, benchmarkServiceMock } = await createAdminBenchmarkFixture());
  });

  // ---------------------------------------------------------------------------
  // Two-family assessor panel: the launcher's co-assessor, and the run detail's
  // panel rendering, member re-assessment and calibration target.
  // ---------------------------------------------------------------------------
  describe('assessor panel launcher', () => {
    /**
     * Id 1 is the fixture's Anthropic claude-3-5-sonnet. The rest are the other roles a panel run
     * needs: two OpenAI models, an older Anthropic model, a Google model, and a second
     * configuration of the candidate's own model.
     */
    function usePanelConfigs(): void {
      const base = component.systemConfigs[0];
      component.systemConfigs = [
        base,
        { ...base, id: 2, displayName: 'GPT-5 Mini', provider: 'OpenAI', modelId: 'gpt-5-mini' },
        { ...base, id: 3, displayName: 'Gemini 3.7 Pro', provider: 'Google', modelId: 'gemini-3.7-pro' },
        { ...base, id: 4, displayName: 'Claude Opus 4', provider: 'Anthropic', modelId: 'claude-opus-4' },
        { ...base, id: 5, displayName: 'GPT-4.1', provider: 'OpenAI', modelId: 'gpt-4.1' },
        { ...base, id: 6, displayName: 'Sonnet (second key)', provider: 'Anthropic', modelId: 'claude-3-5-sonnet' }
      ];
    }

    /** A valid panel: Anthropic candidate, OpenAI member A, an older Anthropic member B. */
    function selectValidPanel(): void {
      usePanelConfigs();
      component.selectedSuiteId = 1;
      component.testedConfigId = 1;
      component.assessorConfigId = 2;
      component.coAssessorConfigId = 4;
    }

    function startButton(): HTMLButtonElement {
      return fixture.nativeElement.querySelector('.form-actions .btn-gh') as HTMLButtonElement;
    }

    it('should offer a co-assessor selector that defaults to none', () => {
      component.activeSubTab = 'run';
      fixture.detectChanges();

      expect(component.coAssessorConfigId).toBeNull();
      expect(component.isPanelLaunch).toBe(false);
      const trigger = fixture.nativeElement.querySelector('.co-assessor-model-selector .selector-trigger') as HTMLButtonElement;
      expect(trigger).toBeTruthy();
      expect(trigger.textContent).toContain('None — single assessor');
    });

    it('should select a co-assessor through its dropdown, and clear it with the None option', () => {
      usePanelConfigs();
      component.activeSubTab = 'run';
      fixture.detectChanges();

      const trigger = fixture.nativeElement.querySelector('.co-assessor-model-selector .selector-trigger') as HTMLButtonElement;
      trigger.click();
      fixture.detectChanges();

      const options = Array.from(fixture.nativeElement.querySelectorAll('.co-assessor-model-selector .model-option')) as HTMLElement[];
      expect(options[0].textContent).toContain('None — single assessor');
      options.find(o => o.textContent?.includes('Claude Opus 4'))!.click();
      fixture.detectChanges();
      expect(component.coAssessorConfigId).toBe(4);
      expect(trigger.textContent).toContain('Claude Opus 4');

      trigger.click();
      fixture.detectChanges();
      (fixture.nativeElement.querySelector('.co-assessor-model-selector .model-option') as HTMLElement).click();
      fixture.detectChanges();
      expect(component.coAssessorConfigId).toBeNull();
    });

    it('should send the co-assessor only when one is selected', () => {
      benchmarkServiceMock.startRun.mockReturnValue(of({ runId: 99 }));
      vi.spyOn(component.runProgressDialog.nativeElement, 'showModal').mockReturnValue(undefined);
      usePanelConfigs();
      component.selectedSuiteId = 1;
      component.testedConfigId = 1;
      component.assessorConfigId = 2;

      component.startBenchmark();
      const single = vi.mocked(benchmarkServiceMock.startRun).mock.lastCall![0];
      expect('coAssessorModelConfigurationId' in single).toBe(false);

      component.coAssessorConfigId = 4;
      component.startBenchmark();
      expect(vi.mocked(benchmarkServiceMock.startRun).mock.lastCall![0].coAssessorModelConfigurationId).toBe(4);
      component.ngOnDestroy();
    });

    it('should persist the co-assessor when a run is started', () => {
      benchmarkServiceMock.startRun.mockReturnValue(of({ runId: 99 }));
      selectValidPanel();

      component.startBenchmark();

      const stored = JSON.parse(localStorage.getItem(RUN_SETTINGS_KEY)!);
      expect(stored.coAssessorConfigId).toBe(4);
      component.ngOnDestroy();
    });

    it('should restore a remembered co-assessor, and drop one that no longer qualifies', () => {
      usePanelConfigs();
      const configs = component.systemConfigs;

      localStorage.setItem(RUN_SETTINGS_KEY, JSON.stringify({
        suiteId: 1, testedConfigId: 1, assessorConfigId: 2, coAssessorConfigId: 4
      }));
      const restored = TestBed.createComponent(AdminBenchmarkComponent);
      restored.componentInstance.systemConfigs = configs;
      restored.detectChanges();
      expect(restored.componentInstance.coAssessorConfigId).toBe(4);
      restored.componentInstance.ngOnDestroy();

      localStorage.setItem(RUN_SETTINGS_KEY, JSON.stringify({
        suiteId: 1, testedConfigId: 1, assessorConfigId: 2, coAssessorConfigId: 77
      }));
      const dropped = TestBed.createComponent(AdminBenchmarkComponent);
      dropped.componentInstance.systemConfigs = configs;
      dropped.detectChanges();
      expect(dropped.componentInstance.coAssessorConfigId).toBeNull();
      dropped.componentInstance.ngOnDestroy();
    });

    it('should fix the reference reader coverage at every answer and name the reference reader', fakeAsync(() => {
      usePanelConfigs();
      component.secondOpinionConfigId = 3;
      component.secondOpinionMode = 1;
      component.coAssessorConfigId = 4;
      component.activeSubTab = 'run';
      fixture.detectChanges();
      tick();
      fixture.detectChanges();

      // A panel run's coverage is not a choice: a read-only line replaces the select.
      expect(fixture.nativeElement.querySelector('#secondOpinionModeSelect')).toBeNull();
      const fixed = fixture.nativeElement.querySelector('.second-opinion-mode-fixed') as HTMLElement;
      expect(fixed.querySelector('#secondOpinionModeFixedLabel')?.textContent?.trim()).toBe('Coverage');
      expect(fixed.textContent).toContain('Every answer, blind');
      expect(component.secondOpinionMode).toBe(3);

      const label = fixture.nativeElement.querySelector('#bmSecondOpinionModelLabel') as HTMLElement;
      expect(label.textContent?.replace(/\s+/g, ' ').trim()).toBe('Reference Reader Optional');
      const tip = fixture.nativeElement.querySelector('#bmSecondOpinionModelHint') as HTMLElement;
      expect(tip.textContent).toContain('A third model grades every answer blind');
      expect(Array.from(tip.querySelectorAll('p > strong')).map(s => s.textContent)).toContain('Recommended:');

      const picker = fixture.debugElement.query(By.css('.second-opinion-model-selector')).componentInstance as ModelPickerComponent;
      expect(picker.noneLabel).toBe('None — no reference reader');

      // The operator's own override is kept, and returns with a single-assessor run.
      component.coAssessorConfigId = null;
      expect(component.secondOpinionMode).toBe(1);
      discardPeriodicTasks();
    }));

    it('should warn and disable Start with a reason when the panel members share a provider', () => {
      selectValidPanel();
      component.coAssessorConfigId = 5;
      component.activeSubTab = 'run';
      fixture.detectChanges();

      expect(component.showCoAssessorSameProviderAdvisory).toBe(true);
      const advisory = fixture.nativeElement.querySelector('.setup-group-grading .co-assessor-advisory') as HTMLElement;
      expect(advisory).toBeTruthy();
      expect(advisory.classList).toContain('alert-warning');
      expect(advisory.getAttribute('role')).toBe('note');
      expect(advisory.textContent).toContain('Panel members share a provider');

      expect(component.canStartRun).toBe(false);
      expect(startButton().getAttribute('aria-disabled')).toBe('true');
      expect((fixture.nativeElement.querySelector('#startBenchmarkHint') as HTMLElement).textContent)
        .toContain('must come from different providers');

      component.startBenchmark();
      expect(benchmarkServiceMock.startRun).not.toHaveBeenCalled();
    });

    it('should warn and disable Start when either panel member is the model under test', () => {
      selectValidPanel();
      // Another configuration of the candidate's own provider and model id.
      component.coAssessorConfigId = 6;
      component.activeSubTab = 'run';
      fixture.detectChanges();

      expect(component.showCoAssessorCandidateAdvisory).toBe(true);
      expect(fixture.nativeElement.querySelector('.setup-group-grading .co-assessor-advisory')?.textContent)
        .toContain('A panel member is the model under test');
      expect(component.startBenchmarkHint).toContain('Neither panel member may be the model under test');
      expect(component.canStartRun).toBe(false);

      // Member A as the candidate is refused the same way.
      component.coAssessorConfigId = 2;
      component.assessorConfigId = 6;
      expect(component.showCoAssessorCandidateAdvisory).toBe(true);
    });

    it('should accept a member from the candidate\'s own provider when the model differs', () => {
      selectValidPanel();
      component.activeSubTab = 'run';
      fixture.detectChanges();

      expect(component.panelLaunchRefusal).toBe('');
      expect(component.canStartRun).toBe(true);
      expect(fixture.nativeElement.querySelector('.co-assessor-advisory')).toBeNull();
    });

    it('should name the roles a reference reader or claim verifier shares a provider with', () => {
      selectValidPanel();
      component.secondOpinionConfigId = 3;
      component.claimVerifierConfigId = 3;
      expect(component.referenceReaderSharedFamilyRoles).toEqual([]);
      expect(component.claimVerifierSharedFamilyRoles).toEqual([]);

      component.secondOpinionConfigId = 5;
      component.claimVerifierConfigId = 1;
      expect(component.referenceReaderSharedFamilyRoles).toEqual(['panel member A']);
      expect(component.claimVerifierSharedFamilyRoles).toEqual(['the model under test', 'panel member B']);
      // The single-run pairing advisory gives way to the panel's fuller one.
      expect(component.showAssessorPairingAdvisory).toBe(false);

      component.activeSubTab = 'run';
      fixture.detectChanges();
      expect(fixture.nativeElement.querySelector('.reference-reader-family-advisory')).toBeTruthy();
      expect(fixture.nativeElement.querySelector('.claim-verifier-family-advisory')).toBeTruthy();

      // A single-assessor run keeps the silence of before.
      component.coAssessorConfigId = null;
      expect(component.referenceReaderSharedFamilyRoles).toEqual([]);
      expect(component.claimVerifierSharedFamilyRoles).toEqual([]);
    });

    it('should never open the same-provider dialog for a panel run', () => {
      benchmarkServiceMock.startRun.mockReturnValue(throwError(() => ({
        status: 409, error: { sameProvider: true, provider: 'Anthropic' }
      })));
      const showModal = vi.spyOn(component.sameProviderDialog.nativeElement, 'showModal').mockReturnValue(undefined);
      selectValidPanel();

      component.startBenchmark();

      expect(showModal).not.toHaveBeenCalled();
      expect(component.sameProviderWarning).toBeNull();
    });
  });

  describe('assessor panel run detail', () => {
    function panelAnswer(overrides: any = {}): any {
      return {
        id: 501, orderIndex: 1, questionText: 'Q1', difficulty: 1, answerText: 'a',
        status: 'Ok', assessmentStatus: 'Scored', durationMs: 1, modelTimeMs: 1,
        scrubbedArtifactCount: 0, answerFlags: 0, answerFlagNames: [],
        qualityScore: 82, panelQualityScore: 78, panelDisagreed: true,
        coAssessmentStatus: 'Scored', coAssessmentQualityScore: 74, coAssessmentCriticalError: false,
        coAssessedByModelDisplayNameUsed: 'Claude Opus 4',
        coAssessmentJson: JSON.stringify({
          accuracyLevel: 5, completenessLevel: 4, concisenessLevel: 6, readabilityLevel: 5,
          criticalError: false, qualityScore: 74,
          comment: 'Misses <b>one</b> step.',
          accuracyEvidence: 'Matches the rubric.',
          completenessEvidence: 'Omits the prayer timeout.',
          unverifiedClaims: ['gnomes gain infravision']
        }),
        secondOpinionQualityScore: 80, secondOpinionCriticalError: false,
        secondOpinionByModelDisplayNameUsed: 'Gemini 3.7 Pro', secondOpinionTrigger: 'All',
        ...overrides
      };
    }

    function buildPanelRun(overrides: any = {}): any {
      return {
        id: 77, benchmarkSuiteId: 1, suiteName: 'Default Suite',
        testedModelDisplayNameUsed: 'Claude Sonnet 5', testedModelProviderUsed: 'Anthropic',
        testedModelIdUsed: 'claude-sonnet-5', testedModelParallelExecutionModeUsed: 0,
        assessorModelConfigurationId: 2,
        assessorModelDisplayNameUsed: 'GPT-5 Mini', assessorModelProviderUsed: 'OpenAI', assessorModelIdUsed: 'gpt-5-mini',
        assessorAvailable: true,
        isPanelRun: true, coAssessorModelConfigurationId: 4,
        coAssessorModelDisplayNameUsed: 'Claude Opus 4', coAssessorModelProviderUsed: 'Anthropic', coAssessorModelIdUsed: 'claude-opus-4',
        secondOpinionAssessorModelConfigurationId: 3,
        secondOpinionAssessorModelDisplayNameUsed: 'Gemini 3.7 Pro', secondOpinionAssessorModelProviderUsed: 'Google',
        secondOpinionModeUsed: 3, secondOpinionBlindUsed: true,
        secondOpinionGradedAnswerCount: 18, secondOpinionMeanAbsDelta: 3.0,
        panelGradedAnswerCount: 18, panelMeanAbsDelta: 6.2, panelMeanSignedDelta: -4, panelIntraclassCorrelation: 0.712,
        panelDisagreementCount: 2, panelCriticalErrorSplitCount: 1,
        assessorOnlyQualityIndex: 82, coAssessorOnlyQualityIndex: 78,
        status: 'Completed', startedAtUtc: '2026-09-26T06:52:00Z', completedAtUtc: '2026-09-26T07:28:00Z',
        qualityIndex: 80, speedIndex: 67, finalScore: 81, coAssessorFinalScore: 77,
        totalAnswerDurationMs: 900000, totalDurationMs: 900000,
        scoringMethodVersion: 12, harnessVersion: '40',
        transportDefectAnswerCount: 0, advisoryFlagAnswerCount: 0, scrubbedArtifactAnswerCount: 0,
        difficultyFallbackUsed: false, speedMeasurementDegraded: false, maxParallelQuestionsUsed: 1,
        answeredQuestionCount: 18, unansweredQuestionCount: 0, totalQuestionCount: 18,
        assessmentText: 'Member A synthesis.', assessmentParseFailed: false,
        coAssessorSynthesisText: 'Member B synthesis.', coAssessorSynthesisParseFailed: false,
        synthesisFindings: [], coAssessorSynthesisFindings: [], synthesisConvergence: [],
        estimatedCost: 4, estimatedCandidateCost: 1, estimatedAssessorCost: 1, estimatedCoAssessorCost: 1,
        estimatedSynthesisCost: 1, estimatedCoSynthesisCost: 0,
        totalInputTokens: 0, totalOutputTokens: 0, totalCacheReadTokens: 0, totalCacheCreationTokens: 0,
        errorMessage: null,
        answers: [panelAnswer()],
        ...overrides
      };
    }

    function scoreCardText(label: string): string {
      const cards: HTMLElement[] = Array.from(fixture.nativeElement.querySelectorAll('.score-card'));
      const card = cards.find(c => (c.querySelector('.score-label')?.textContent || '').trim() === label);
      return (card?.textContent || '').replace(/\s+/g, ' ').trim();
    }

    /** Opens the first answer card the way the operator does, by its header. */
    function expandFirstAnswer(): HTMLElement {
      (fixture.nativeElement.querySelector('.question-card-header') as HTMLElement).click();
      fixture.detectChanges();
      return fixture.nativeElement.querySelector('.question-detail-card') as HTMLElement;
    }

    it('should list both panel assessors with their badges in the header', () => {
      component.selectedRunDetail = buildPanelRun();
      fixture.detectChanges();

      const assessors = fixture.nativeElement.querySelector('.benchmark-run-detail-dialog app-run-facts [data-fact="assessor"]') as HTMLElement;
      expect(assessors.querySelector('dt')!.textContent!.trim()).toBe('Assessors');
      const models = Array.from(assessors.querySelectorAll('.rr-fact-model'));
      const text = (element: Element | null) => (element?.textContent ?? '').replace(/\s+/g, ' ').trim();
      expect(models.map(model => text(model.querySelector('.rr-fact-model-name')))).toEqual(['GPT-5 Mini', 'Claude Opus 4']);
      expect(models.map(model => text(model.querySelector('.model-option-tag')))).toEqual(['Member A', 'Member B']);
      expect(models.map(model => text(model.querySelector('app-provider-badge')))).toEqual(['OpenAI', 'Anthropic']);
    });

    it('should show the Panel tile and relabel the agreement tile for the reference reader', () => {
      component.selectedRunDetail = buildPanelRun();
      fixture.detectChanges();

      const panel = scoreCardText('Panel');
      expect(panel).toContain('A 82 · B 78');
      expect(panel).toContain('ICC 0.71');
      expect(panel).toContain('mean B − A −4.0');
      expect(panel).toContain('2 disagreement(s) over 18 answers both scored');

      expect(scoreCardText('Reference Reader Agreement')).toContain('3.0 pts');
      expect(scoreCardText('Assessor Agreement')).toBe('');
    });

    it('should render the co-assessment box, the panel chip and the reference reader label on an answer', () => {
      component.selectedRunDetail = buildPanelRun();
      fixture.detectChanges();
      const card = expandFirstAnswer();

      expect(card.querySelector('.panel-score-chip')?.textContent?.replace(/\s+/g, ' ').trim()).toBe('Panel: 78');
      expect(card.querySelector('.question-card-header .panel-disagree-badge')?.textContent?.trim()).toBe('MEMBERS DISAGREE');

      const box = card.querySelector('.co-assessment-box') as HTMLElement;
      expect(box).toBeTruthy();
      const text = box.textContent!.replace(/\s+/g, ' ');
      expect(text).toContain('Co-assessor (Claude Opus 4): 74 / 100, critical error no');
      expect(text).toContain('Panel score: 78 / 100');
      expect(text).toContain('members disagree');
      expect(text).toContain('Level 5/6');
      expect(text).toContain('Omits the prayer timeout.');
      expect(text).toContain('gnomes gain infravision');
      // Model output is text, never markup.
      expect(box.querySelector('b')).toBeNull();
      expect(text).toContain('Misses <b>one</b> step.');

      const reference = card.querySelector('.second-opinion-box strong') as HTMLElement;
      expect(reference.textContent).toContain('Reference reader (Gemini 3.7 Pro)');
    });

    it('should keep the single-assessor labels on a single-assessor run', () => {
      component.selectedRunDetail = buildPanelRun({
        isPanelRun: false, coAssessorModelConfigurationId: null, coAssessorModelDisplayNameUsed: null,
        coAssessorSynthesisText: null,
        answers: [panelAnswer({ panelQualityScore: null, panelDisagreed: null, coAssessmentStatus: null, coAssessmentJson: null, coAssessmentQualityScore: null })]
      });
      fixture.detectChanges();
      const card = expandFirstAnswer();

      expect(scoreCardText('Panel')).toBe('');
      expect(scoreCardText('Assessor Agreement')).toContain('3.0 pts');
      expect(card.querySelector('.co-assessment-box')).toBeNull();
      expect(card.querySelector('.panel-score-chip')).toBeNull();
      expect((card.querySelector('.second-opinion-box strong') as HTMLElement).textContent).toContain('Second reader (');
      expect(fixture.nativeElement.querySelectorAll('app-benchmark-synthesis-panel [role="tab"]').length).toBe(0);
    });

    it('should omit the All trigger on a reference reading', () => {
      component.selectedRunDetail = buildPanelRun({ answers: [panelAnswer({ secondOpinionTrigger: 'All' })] });
      fixture.detectChanges();
      const card = expandFirstAnswer();
      expect(card.querySelector('.second-opinion-box')).toBeTruthy();
      expect(card.querySelector('.second-opinion-trigger')).toBeNull();
    });

    it('should show a manual trial trigger on a panel run', () => {
      component.selectedRunDetail = buildPanelRun({ answers: [panelAnswer({ secondOpinionTrigger: 'Manual' })] });
      fixture.detectChanges();
      expect(expandFirstAnswer().querySelector('.second-opinion-trigger')?.textContent).toContain('manual trial');
    });

    it('should label the coverage badge by run type', () => {
      const panel = buildPanelRun();
      expect(component.runSecondOpinionModeLabel(panel)).toBe('Every answer, blind (reference reading)');
      expect(component.runSecondOpinionModeHint(panel)).toContain('never scores');

      const single = buildPanelRun({ isPanelRun: false, secondOpinionModeUsed: 1 });
      expect(component.runSecondOpinionModeLabel(single)).toBe('Only flagged answers');
      expect(component.runSecondOpinionModeHint(single)).toContain('raised a flag');
    });

    it('should show both syntheses as tabs, from one cached list per run object', () => {
      const run = buildPanelRun();
      component.selectedRunDetail = run;
      fixture.detectChanges();

      const tabs = fixture.nativeElement.querySelectorAll('app-benchmark-synthesis-panel [role="tab"]');
      expect(tabs.length).toBe(3);
      expect(component.synthesisViewsOf(run)).toBe(component.synthesisViewsOf(run));
      expect(component.synthesisViewsOf(run).map(s => s.familyRelation)).toEqual(['cross-family', 'same-family']);
    });

    it('should list the co-assessor cost lines and apportion the candidate share across them', () => {
      const run = buildPanelRun();
      component.selectedRunDetail = run;
      fixture.detectChanges();

      const names = Array.from(fixture.nativeElement.querySelectorAll('.benchmark-run-detail-dialog .gh-cost-role:not(.gh-cost-role--subtotal) .gh-cost-role__name'))
        .map((el: any) => el.textContent.trim());
      expect(names).toContain('Co-assessor');
      expect(names).toContain('Co-assessor synthesis');
      // Candidate, assessor, co-assessor and synthesis at 1 each and co-synthesis at 0: a quarter.
      expect(component.candidateCostShareLabel(run)).toBe('25 % of estimated total');
    });

    describe('re-assessment', () => {
      beforeEach(() => {
        component.selectedRunDetail = buildPanelRun();
        fixture.detectChanges();
      });

      function retryDialog(): HTMLElement {
        return fixture.nativeElement.querySelector('.retry-dialog') as HTMLElement;
      }

      it('should offer the member choice without an assessor picker, and send the chosen member', fakeAsync(() => {
        benchmarkServiceMock.reassessPanelAnswer.mockReturnValue(of({ runId: 77 }));
        const answer = component.selectedRunDetail!.answers[0];

        component.openRetryDialog('assessment', 77, answer);
        fixture.detectChanges();

        expect(retryDialog().querySelector('.retry-assessor-model-selector')).toBeNull();
        const radios = Array.from(retryDialog().querySelectorAll('.retry-panel-member input[type="radio"]')) as HTMLInputElement[];
        expect(radios.length).toBe(3);
        expect(radios[0].checked).toBe(true);

        radios[2].click();
        fixture.detectChanges();
        expect(component.retryPanelMember).toBe('B');

        const confirm = retryDialog().querySelector('.dialog-footer .btn-gh:not(.btn-gh-cancel)') as HTMLButtonElement;
        expect(confirm.disabled).toBe(false);
        confirm.click();

        expect(benchmarkServiceMock.reassessPanelAnswer).toHaveBeenCalledWith(77, 501, 'B');
        expect(benchmarkServiceMock.reassessAnswer).not.toHaveBeenCalled();

        component.stopDetailPolling();
        discardPeriodicTasks();
      }));

      it('should send no assessor override for a panel run\'s question, synthesis and failed-assessment re-runs', fakeAsync(() => {
        benchmarkServiceMock.rerunAnswer.mockReturnValue(of({ runId: 77 }));
        benchmarkServiceMock.rerunFinalSynthesis.mockReturnValue(of({ runId: 77 }));
        benchmarkServiceMock.retryFailedAssessments.mockReturnValue(of({ runId: 77 }));
        const answer = component.selectedRunDetail!.answers[0];

        component.openRetryDialog('question', 77, answer);
        fixture.detectChanges();
        expect(retryDialog().querySelector('.retry-assessor-model-selector')).toBeNull();
        expect(retryDialog().querySelector('.retry-panel-member')).toBeNull();
        component.confirmRetry();
        expect(benchmarkServiceMock.rerunAnswer).toHaveBeenCalledWith(77, 501, null);

        component.openRetryDialog('synthesis', 77);
        fixture.detectChanges();
        expect(retryDialog().querySelector('.retry-assessor-model-selector')).toBeNull();
        component.confirmRetry();
        expect(benchmarkServiceMock.rerunFinalSynthesis).toHaveBeenCalledWith(77, null);

        component.openRetryDialog('assessments', 77);
        component.confirmRetry();
        expect(benchmarkServiceMock.retryFailedAssessments).toHaveBeenCalledWith(77, null);

        component.stopDetailPolling();
        discardPeriodicTasks();
      }));

      it('should offer the trial only where the reference reader has not graded, and never ask to replace', fakeAsync(() => {
        benchmarkServiceMock.trialReassessAnswer.mockReturnValue(of({ runId: 77 }));
        const graded = component.selectedRunDetail!.answers[0];
        const empty = { ...graded, secondOpinionQualityScore: null, secondOpinionTrigger: null };
        expect(component.canTrialReassess(graded)).toBe(false);
        expect(component.canTrialReassess(empty)).toBe(true);

        const card = expandFirstAnswer();
        expect(card.querySelector('.btn-gh-trial')).toBeNull();

        component.openRetryDialog('trial', 77, graded);
        component.retryAssessorConfigId = 1;
        component.confirmRetry();
        expect(vi.mocked(benchmarkServiceMock.trialReassessAnswer).mock.lastCall![3]).toBe(false);

        component.stopDetailPolling();
        discardPeriodicTasks();
      }));
    });

    describe('calibration target', () => {
      it('should offer Compare against on a panel run and send the chosen target', fakeAsync(() => {
        benchmarkServiceMock.calibrateAssessor.mockReturnValue(of({ id: 2 } as any));
        component.selectedRunDetail = buildPanelRun();
        component.calibrationAssessorConfigId = 1;
        fixture.detectChanges();
        tick();
        fixture.detectChanges();

        const trigger = fixture.nativeElement.querySelector('.calibration-target-selector .selector-trigger') as HTMLButtonElement;
        expect(trigger).toBeTruthy();
        trigger.click();
        fixture.detectChanges();

        const options = Array.from(fixture.nativeElement.querySelectorAll('.calibration-target-selector [role="option"]')) as HTMLElement[];
        expect(options.map(o => o.querySelector('.model-option-tag')?.textContent?.trim()))
          .toEqual(['Assessor A', 'Co-assessor B', 'Panel']);
        expect(options.map(o => o.querySelector('.model-name')?.textContent?.trim()))
          .toEqual(['GPT-5 Mini', 'Claude Opus 4', 'Mean of GPT-5 Mini and Claude Opus 4']);
        expect(options[0].querySelector('app-provider-badge')?.textContent).toContain('OpenAI');
        expect(options[1].querySelector('app-provider-badge')?.textContent).toContain('Anthropic');
        expect(options[2].querySelector('app-provider-badge')).toBeNull();

        options[2].click();
        fixture.detectChanges();
        expect(component.calibrationTarget).toBe('Panel');
        expect(trigger.querySelector('.model-option-tag')?.textContent?.trim()).toBe('Panel');

        component.runCalibration(77);
        expect(benchmarkServiceMock.calibrateAssessor).toHaveBeenCalledWith(77, 1, 'Panel');
        discardPeriodicTasks();
      }));

      it('should label Compare against like the calibration assessor', () => {
        component.selectedRunDetail = buildPanelRun();
        fixture.detectChanges();

        const trigger = fixture.nativeElement.querySelector('.calibration-target-selector .selector-trigger') as HTMLButtonElement;
        const labelId = trigger.getAttribute('aria-labelledby')!.split(' ')[0];
        expect(labelId).toBe('bmCalibrationTargetLabel');
        expect(fixture.nativeElement.querySelector(`#${labelId}`)?.textContent?.trim()).toBe('Compare against');
      });

      it('should rebuild the Compare against options only when the run changes', () => {
        component.selectedRunDetail = buildPanelRun();
        const first = component.calibrationTargetPickerOptions;
        expect(component.calibrationTargetPickerOptions).toBe(first);

        component.selectedRunDetail = buildPanelRun();
        expect(component.calibrationTargetPickerOptions).not.toBe(first);
      });

      it('should show the Compared against column on a panel run', () => {
        const rows = [
          { id: 1, benchmarkRunId: 77, assessorDisplayNameUsed: 'Claude Opus 5', assessorProviderUsed: 'Anthropic',
            createdAtUtc: '2026-09-27T08:00:00Z', answerCount: 18, skippedAnswerCount: 0, meanAbsDelta: 5.5,
            disagreementCount: 2, inputTokens: 1, outputTokens: 1, durationMs: 1, comparedAgainst: 'CoAssessor' },
          { id: 2, benchmarkRunId: 77, assessorDisplayNameUsed: 'Claude Opus 5', assessorProviderUsed: 'Anthropic',
            createdAtUtc: '2026-09-27T09:00:00Z', answerCount: 18, skippedAnswerCount: 0, meanAbsDelta: 4.5,
            disagreementCount: 1, inputTokens: 1, outputTokens: 1, durationMs: 1, comparedAgainst: null }
        ] as any[];

        component.selectedRunDetail = buildPanelRun();
        component.calibrations = rows;
        fixture.detectChanges();

        expect(fixture.nativeElement.querySelectorAll('.calibration-list > li.calibration-item').length).toBe(2);
        expect(statValues('Compared against')).toEqual(['Co-assessor B', 'Assessor A']);
      });

      it('should show neither the Compared against column nor the target select on a single-assessor run', () => {
        const rows = [
          { id: 2, benchmarkRunId: 77, assessorDisplayNameUsed: 'Claude Opus 5', assessorProviderUsed: 'Anthropic',
            createdAtUtc: '2026-09-27T09:00:00Z', answerCount: 18, skippedAnswerCount: 0, meanAbsDelta: 4.5,
            disagreementCount: 1, inputTokens: 1, outputTokens: 1, durationMs: 1, comparedAgainst: null }
        ] as any[];

        component.selectedRunDetail = buildPanelRun({ isPanelRun: false });
        component.calibrations = rows;
        fixture.detectChanges();
        expect(fixture.nativeElement.querySelectorAll('.calibration-item').length).toBe(1);
        expect(statTerms()).not.toContain('Compared against');
        expect(fixture.nativeElement.querySelector('.calibration-target-selector')).toBeNull();
      });

      it('should wrap the calibration controls and list the calibrations without a table', () => {
        component.selectedRunDetail = buildPanelRun();
        component.calibrations = [
          { id: 2, benchmarkRunId: 77, assessorDisplayNameUsed: 'Claude Opus 5', assessorProviderUsed: 'Anthropic',
            createdAtUtc: '2026-09-27T09:00:00Z', answerCount: 18, skippedAnswerCount: 3, meanAbsDelta: 4.5,
            disagreementCount: 1, inputTokens: 1, outputTokens: 1, durationMs: 1, comparedAgainst: null,
            errorMessage: 'Three answers could not be graded.' }
        ] as any[];
        fixture.detectChanges();

        const controls = fixture.nativeElement.querySelector('.calibration-panel .calibration-controls') as HTMLElement;
        expect(controls).toBeTruthy();
        expect(controls.querySelector('.calibration-model-field app-model-picker')).toBeTruthy();
        expect(controls.querySelector('.calibration-target-field app-model-picker.calibration-target-selector')).toBeTruthy();
        expect(controls.querySelector('button.btn-gh')?.textContent?.trim()).toBe('Calibrate assessor');
        expect(fixture.nativeElement.querySelector('.calibration-table')).toBeNull();
        expect(fixture.nativeElement.querySelector('.calibration-panel table')).toBeNull();

        expect(statTerms()).toEqual(['Compared against', 'Mean abs. delta', 'Disagreements', 'Answers', 'Tokens', 'Duration']);
        expect(statValues('Answers')).toEqual(['18 (3 skipped)']);
        const item = fixture.nativeElement.querySelector('.calibration-item') as HTMLElement;
        expect(item.querySelector('.calibration-item-head')?.textContent).toContain('Claude Opus 5');
        expect(item.querySelector('p.calibration-error')?.textContent?.trim()).toBe('Three answers could not be graded.');
      });

      it('should order the calibration controls model, target, button on a panel run', () => {
        component.selectedRunDetail = buildPanelRun();
        fixture.detectChanges();
        expect(calibrationControlOrder()).toEqual(['model', 'target', 'button:Calibrate assessor']);
      });

      it('should order the calibration controls model, button on a single-assessor run', () => {
        component.selectedRunDetail = buildPanelRun({ isPanelRun: false });
        fixture.detectChanges();
        expect(calibrationControlOrder()).toEqual(['model', 'button:Calibrate assessor']);
      });

      function calibrationControlOrder(): string[] {
        return Array.from((fixture.nativeElement.querySelector('.calibration-panel .calibration-controls') as HTMLElement).children)
          .map(child => child.matches('.calibration-model-field') ? 'model'
            : child.matches('.calibration-target-field') ? 'target'
            : child.matches('button.btn-gh') ? `button:${child.textContent?.trim()}` : child.tagName);
      }

      function statTerms(): string[] {
        return Array.from(fixture.nativeElement.querySelectorAll('.calibration-stats dt'))
          .map((dt: any) => dt.textContent.trim());
      }

      function statValues(term: string): string[] {
        return Array.from(fixture.nativeElement.querySelectorAll('.calibration-stats dt'))
          .filter((dt: any) => dt.textContent.trim() === term)
          .map((dt: any) => dt.nextElementSibling.textContent.replace(/\s+/g, ' ').trim());
      }
    });
  });
});
