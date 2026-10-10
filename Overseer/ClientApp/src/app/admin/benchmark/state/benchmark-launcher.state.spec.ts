import type { Mock } from 'vitest';
import { TestBed, discardPeriodicTasks, fakeAsync, tick } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import {
  AdminBenchmarkService,
  BenchmarkBatteryDto,
  BenchmarkModelBatchFindingDto,
  BenchmarkRunDetailDto,
  BenchmarkScoringProfileDto,
  BenchmarkSuiteDto
} from '../../../services/admin-benchmark.service';
import { SystemAiConfigDto } from '../../../services/admin.service';
import { BenchmarkLauncherState, MODEL_BATCH_PREFLIGHT_DEBOUNCE_MS } from './benchmark-launcher.state';
import { BenchmarkViewSync } from './benchmark-view-sync.service';
import { BenchmarkWorkspaceStore } from './benchmark-workspace.store';

const RUN_SETTINGS_KEY = 'overseer_admin_benchmark_run_settings';

function clearRunSettings(): void {
  try {
    localStorage.removeItem(RUN_SETTINGS_KEY);
  } catch { /* private-browsing modes throw */ }
}

const suite = (id: number, name: string): BenchmarkSuiteDto => ({
  id, name, description: 'Test', createdAtUtc: '2026-09-01T00:00:00Z', modifiedAtUtc: null,
  questionCount: 15, assessedQuestionCount: 15, difficultyFullyAssessed: true
}) as unknown as BenchmarkSuiteDto;

const profile = (id: number, name: string, isDefault = false): BenchmarkScoringProfileDto =>
  ({ id, name, isDefault, secondOpinionMode: 1 }) as unknown as BenchmarkScoringProfileDto;

const config = (id: number, displayName: string, overrides: Partial<SystemAiConfigDto> = {}): SystemAiConfigDto => ({
  id, displayName, provider: 'Anthropic', modelId: `model-${id}`, modelRole: 7, hasApiKey: true, isEnabled: true,
  ...overrides
}) as unknown as SystemAiConfigDto;

/**
 * Run 55 of suite 2 under profile 2: candidate 2, assessor 1, second reader 3 reading every answer, claim
 * verifier 2, report writer 1, no co-assessor; detailed style with source code references allowed.
 */
function run(overrides: Partial<BenchmarkRunDetailDto> = {}): BenchmarkRunDetailDto {
  return {
    id: 55, benchmarkSuiteId: 2, suiteName: 'Second Suite',
    scoringProfileId: 2, scoringProfileName: 'Strict',
    testedModelConfigurationId: 2, testedModelDisplayNameUsed: 'Model Two',
    assessorModelConfigurationId: 1, assessorModelDisplayNameUsed: 'Model One',
    coAssessorModelConfigurationId: null, coAssessorModelDisplayNameUsed: null,
    secondOpinionAssessorModelConfigurationId: 3, secondOpinionAssessorModelDisplayNameUsed: 'Model Three',
    secondOpinionModeUsed: 3,
    claimVerifierModelConfigurationId: 2, claimVerifierDisplayNameUsed: 'Model Two',
    reportWriterModelConfigurationId: 1, reportWriterDisplayName: 'Model One',
    candidatePromptOptionsJson: '{"verboseMode":true,"enableToolUse":true,"allowSourceCodeReferences":true}',
    isPanelRun: false,
    answers: [],
    ...overrides
  } as unknown as BenchmarkRunDetailDto;
}

describe('BenchmarkLauncherState: prefillFromRun', () => {
  let launcher: BenchmarkLauncherState;
  let workspace: BenchmarkWorkspaceStore;

  const stored = (): any => JSON.parse(localStorage.getItem(RUN_SETTINGS_KEY) ?? 'null');

  /** The lists a page load brings: suites 1 and 2, profiles 1 and 2, configurations 1 to 3 and a disabled 4. */
  function setLists(): void {
    workspace.suites = [suite(1, 'Default Suite'), suite(2, 'Second Suite')];
    workspace.scoringProfiles = [profile(1, 'Standard', true), profile(2, 'Strict')];
    workspace.setSystemConfigs([
      config(1, 'Model One'), config(2, 'Model Two', { provider: 'OpenAI' }), config(3, 'Model Three', { provider: 'Google' }),
      config(4, 'Model Four', { isEnabled: false })
    ]);
  }

  /** What the shell's ngOnInit and the loaders do: read the stored settings, then each list arrives. */
  function loadPage(): void {
    launcher.restoreRunSettings();
    workspace.suitesLoaded$.next();
    workspace.profilesLoaded$.next();
    launcher.setDefaultModelSelections();
    workspace.batteriesLoaded$.next(true);
  }

  beforeEach(() => {
    clearRunSettings();
    TestBed.configureTestingModule({
      providers: [
        BenchmarkLauncherState,
        BenchmarkWorkspaceStore,
        BenchmarkViewSync,
        {
          provide: AdminBenchmarkService,
          useValue: { getLastAssessor: vi.fn(() => of({})), previewBatteryReuse: vi.fn(() => of(null)) }
        }
      ]
    });
    launcher = TestBed.inject(BenchmarkLauncherState);
    workspace = TestBed.inject(BenchmarkWorkspaceStore);
    setLists();
  });

  afterEach(clearRunSettings);

  it('sets every recorded selection, one run of a single suite, and stores them', () => {
    loadPage();
    launcher.runCount = 4;
    launcher.runTargetKind = 'battery';

    launcher.prefillFromRun(run());

    expect(launcher.selectedSuiteId).toBe(2);
    expect(launcher.selectedScoringProfileId).toBe(2);
    expect(launcher.testedConfigId).toBe(2);
    expect(launcher.assessorConfigId).toBe(1);
    expect(launcher.coAssessorConfigId).toBeNull();
    expect(launcher.secondOpinionConfigId).toBe(3);
    expect(launcher.secondOpinionMode).toBe(3);
    expect(launcher.claimVerifierConfigId).toBe(2);
    expect(launcher.reportWriterConfigId).toBe(1);
    expect(launcher.candidateVerboseMode).toBe(true);
    expect(launcher.candidateAllowSourceCodeReferences).toBe(true);
    expect(launcher.runCount).toBe(1);
    expect(launcher.runTargetKind).toBe('suite');
    expect(launcher.prefillResult).toEqual({ runId: 55, notes: [] });

    expect(stored()).toEqual(expect.objectContaining({
      suiteId: 2, scoringProfileId: 2, testedConfigId: 2, assessorConfigId: 1, coAssessorConfigId: null,
      secondOpinionConfigId: 3, secondOpinionMode: 3, claimVerifierConfigId: 2, reportWriterConfigId: 1,
      verboseMode: true, allowSourceCodeReferences: true, runCount: 1, targetKind: 'suite'
    }));
  });

  it('clears an optional role the run did not use', () => {
    loadPage();
    launcher.coAssessorConfigId = 3;
    launcher.reportWriterConfigId = 3;

    launcher.prefillFromRun(run({ reportWriterModelConfigurationId: null, reportWriterDisplayName: null }));

    expect(launcher.coAssessorConfigId).toBeNull();
    expect(launcher.reportWriterConfigId).toBeNull();
    expect(launcher.prefillResult?.notes).toEqual([]);
  });

  it('follows the profile coverage again for a run without a second reader', () => {
    loadPage();
    launcher.secondOpinionMode = 4;

    launcher.prefillFromRun(run({
      secondOpinionAssessorModelConfigurationId: null, secondOpinionAssessorModelDisplayNameUsed: null, secondOpinionModeUsed: 0
    }));

    expect(launcher.secondOpinionConfigId).toBeNull();
    // Profile 2's own default.
    expect(launcher.secondOpinionMode).toBe(1);
    expect(stored().secondOpinionMode).toBeNull();
  });

  it('keeps each field whose recorded selection is gone or disabled, and names it in a note', () => {
    loadPage();
    const before = {
      suite: launcher.selectedSuiteId, profile: launcher.selectedScoringProfileId,
      tested: launcher.testedConfigId, coAssessor: launcher.coAssessorConfigId
    };

    launcher.prefillFromRun(run({
      benchmarkSuiteId: 9, suiteName: 'Deleted Suite',
      scoringProfileId: 8, scoringProfileName: 'Deleted Profile',
      testedModelConfigurationId: 4, testedModelDisplayNameUsed: 'Model Four',
      coAssessorModelConfigurationId: null, coAssessorModelDisplayNameUsed: 'Deleted Co-Assessor'
    }));

    expect(launcher.selectedSuiteId).toBe(before.suite);
    expect(launcher.selectedScoringProfileId).toBe(before.profile);
    expect(launcher.testedConfigId).toBe(before.tested);
    expect(launcher.coAssessorConfigId).toBe(before.coAssessor);
    // The rest of the setup is still taken over.
    expect(launcher.assessorConfigId).toBe(1);
    expect(launcher.secondOpinionConfigId).toBe(3);

    const notes = launcher.prefillResult!.notes;
    expect(notes.length).toBe(4);
    expect(notes[0]).toContain('Benchmark Suite: Deleted Suite');
    expect(notes[1]).toContain('Scoring Profile: Deleted Profile');
    expect(notes[2]).toContain('Model Under Test: Model Four');
    expect(notes[3]).toContain('Co-Assessor: Deleted Co-Assessor');
  });

  it('keeps the response style when the run recorded no prompt options, and says so', () => {
    loadPage();
    launcher.candidateVerboseMode = false;

    launcher.prefillFromRun(run({ candidatePromptOptionsJson: null }));

    expect(launcher.candidateVerboseMode).toBe(false);
    expect(launcher.prefillResult!.notes).toEqual([expect.stringContaining('Prompt options')]);
  });

  it('holds the prefill until the remembered settings are applied, so they cannot overwrite it', () => {
    localStorage.setItem(RUN_SETTINGS_KEY, JSON.stringify({
      suiteId: 1, scoringProfileId: 1, testedConfigId: 1, assessorConfigId: 2, verboseMode: false, runCount: 3, targetKind: 'suite'
    }));
    launcher.restoreRunSettings();

    launcher.prefillFromRun(run());

    expect(launcher.prefillReady).toBe(false);
    expect(launcher.pendingPrefillRunId).toBe(55);
    expect(launcher.prefillResult).toBeNull();
    expect(launcher.runCount).toBe(3);

    workspace.suitesLoaded$.next();
    workspace.profilesLoaded$.next();
    launcher.setDefaultModelSelections();
    // Three of the four parts applied: the stored blob still waits for the batteries.
    expect(launcher.pendingPrefillRunId).toBe(55);
    expect(launcher.testedConfigId).toBe(1);

    workspace.batteriesLoaded$.next(true);

    expect(launcher.pendingRunSettings).toBeNull();
    expect(launcher.pendingPrefillRunId).toBeNull();
    expect(launcher.selectedSuiteId).toBe(2);
    expect(launcher.testedConfigId).toBe(2);
    expect(launcher.assessorConfigId).toBe(1);
    expect(launcher.candidateVerboseMode).toBe(true);
    expect(launcher.runCount).toBe(1);
    expect(stored()).toEqual(expect.objectContaining({ suiteId: 2, testedConfigId: 2, assessorConfigId: 1, runCount: 1 }));
  });

  it('waits for the lists even when nothing was remembered', () => {
    launcher.restoreRunSettings();

    launcher.prefillFromRun(run());
    expect(launcher.pendingPrefillRunId).toBe(55);

    workspace.suitesLoaded$.next();
    workspace.profilesLoaded$.next();
    expect(launcher.pendingPrefillRunId).toBe(55);

    launcher.setDefaultModelSelections();

    expect(launcher.pendingPrefillRunId).toBeNull();
    expect(launcher.testedConfigId).toBe(2);
  });

  it('does not apply a prefill before the remembered settings were read', () => {
    workspace.suitesLoaded$.next();
    workspace.profilesLoaded$.next();
    launcher.setDefaultModelSelections();

    launcher.prefillFromRun(run());

    expect(launcher.prefillReady).toBe(false);
    expect(launcher.pendingPrefillRunId).toBe(55);
  });

  it('switches back to One model, since a run\'s setup has one model under test', () => {
    loadPage();
    launcher.runMode = 'batch';

    launcher.prefillFromRun(run());

    expect(launcher.runMode).toBe('single');
    expect(stored().runMode).toBe('single');
  });
});

describe('BenchmarkLauncherState: model batches', () => {
  let launcher: BenchmarkLauncherState;
  let workspace: BenchmarkWorkspaceStore;
  let preflight: Mock;

  const stored = (): any => JSON.parse(localStorage.getItem(RUN_SETTINGS_KEY) ?? 'null');

  const warning = (key: string, overrides: Partial<BenchmarkModelBatchFindingDto> = {}): BenchmarkModelBatchFindingDto => ({
    code: 'MB-W01', name: 'MixedFamiliesSingleAssessor', severity: 'Warning', field: 'coAssessor',
    title: 'Use a two-family panel for this batch',
    detail: 'A single assessor favors its own provider\'s models over the others.',
    modelConfigurationIds: [2, 3], acknowledgmentKey: key, ...overrides
  });

  const battery = (): BenchmarkBatteryDto => ({
    id: 5, name: 'Core Battery', isArchived: false, brokenSuiteNames: [], validationErrors: [],
    suites: [
      { index: 0, suiteId: 1, suiteName: 'Default Suite', difficultyFullyAssessed: true },
      { index: 1, suiteId: 2, suiteName: 'Second Suite', difficultyFullyAssessed: true }
    ]
  }) as unknown as BenchmarkBatteryDto;

  function loadPage(): void {
    launcher.restoreRunSettings();
    workspace.suitesLoaded$.next();
    workspace.profilesLoaded$.next();
    launcher.setDefaultModelSelections();
    workspace.batteriesLoaded$.next(true);
  }

  beforeEach(() => {
    clearRunSettings();
    preflight = vi.fn(() => of({ findings: [], projection: { plannedRunCount: 4, members: [] }, maxModels: 12 }));
    TestBed.configureTestingModule({
      providers: [
        BenchmarkLauncherState,
        BenchmarkWorkspaceStore,
        BenchmarkViewSync,
        {
          provide: AdminBenchmarkService,
          useValue: {
            getLastAssessor: vi.fn(() => of({})),
            previewBatteryReuse: vi.fn(() => of(null)),
            preflightModelBatch: preflight
          }
        }
      ]
    });
    launcher = TestBed.inject(BenchmarkLauncherState);
    workspace = TestBed.inject(BenchmarkWorkspaceStore);
    workspace.suites = [suite(1, 'Default Suite'), suite(2, 'Second Suite')];
    workspace.scoringProfiles = [profile(1, 'Standard', true)];
    workspace.setSystemConfigs([
      config(1, 'Model One'), config(2, 'Model Two', { provider: 'OpenAI' }), config(3, 'Model Three', { provider: 'Google' }),
      config(4, 'Model Four', { isEnabled: false })
    ]);
  });

  afterEach(clearRunSettings);

  it('keeps the As listed order of the models that stay chosen and appends new ones', () => {
    launcher.setBatchModels([1, 2, 3]);
    launcher.setBatchOrderKeys([3, 1, 2]);

    launcher.setBatchModels([1, 3]);
    expect(launcher.batchOrderKeys).toEqual([3, 1]);

    launcher.setBatchModels([1, 2, 3]);
    expect(launcher.batchOrderKeys).toEqual([3, 1, 2]);

    launcher.setBatchOrderKeys([2, 9, 2]);
    expect(launcher.batchOrderKeys).toEqual([2, 1, 3]);
  });

  it('builds a suite batch: the As listed order, runs per model, the template and only current acknowledgments', () => {
    loadPage();
    launcher.runMode = 'batch';
    launcher.setBatchModels([2, 3]);
    launcher.batchOrder = 'asListed';
    launcher.setBatchOrderKeys([3, 2]);
    launcher.batchRunsPerModel = 2;
    launcher.reportWriterConfigId = 1;
    launcher.launchAcknowledgments = { assessor: true, reportWriter: true };
    launcher.applyFindings([warning('MB-W01:2,3')]);
    launcher.setFindingAcknowledged('MB-W01:2,3', true);
    launcher.setFindingAcknowledged('MB-W10:9', true);

    const req = launcher.buildModelBatchRequest()!;

    expect(req).toEqual({
      targetKind: 'Suite', suiteId: 1, batteryId: null, testedModelConfigurationIds: [3, 2], runsPerModel: 2,
      order: 'AsListed', allowCapWait: false,
      run: expect.objectContaining({
        suiteId: 1, testedModelConfigurationId: 3, assessorModelConfigurationId: 1, reportWriterModelConfigurationId: 1,
        acknowledgeSameProvider: false
      }),
      acknowledgedFindingKeys: ['MB-W01:2,3']
    });
    expect('acknowledgeSameProviderReportWriter' in req.run).toBe(false);
  });

  it('builds a randomized battery batch with Runs per Suite as R', () => {
    loadPage();
    workspace.launcherBatteries = [battery()];
    launcher.runMode = 'batch';
    launcher.runTargetKind = 'battery';
    launcher.selectedBatteryId = 5;
    launcher.runCount = 3;
    launcher.batchRunsPerModel = 7;
    launcher.allowCapWait = true;
    launcher.setBatchModels([2, 3]);

    const req = launcher.buildModelBatchRequest()!;

    expect(req.targetKind).toBe('Battery');
    expect(req.batteryId).toBe(5);
    expect(req.suiteId).toBeNull();
    expect(req.run.suiteId).toBe(1);
    expect(req.runsPerModel).toBe(3);
    expect(req.order).toBe('Randomized');
    expect(req.testedModelConfigurationIds).toEqual([2, 3]);
    expect(req.allowCapWait).toBe(true);
  });

  it('builds nothing without an assessor', () => {
    loadPage();
    launcher.assessorConfigId = null;
    launcher.setBatchModels([2, 3]);

    expect(launcher.buildModelBatchRequest()).toBeNull();
  });

  it('asks the server once the settings settle, and only in batch mode', fakeAsync(() => {
    loadPage();
    launcher.setBatchModels([2, 3]);

    launcher.requestPreflight();
    tick(MODEL_BATCH_PREFLIGHT_DEBOUNCE_MS);
    expect(preflight).not.toHaveBeenCalled();
    expect(launcher.preflightLoading).toBe(false);

    launcher.runMode = 'batch';
    launcher.requestPreflight();
    tick(100);
    launcher.requestPreflight();
    tick(100);
    launcher.requestPreflight();
    expect(launcher.preflightLoading).toBe(true);
    tick(MODEL_BATCH_PREFLIGHT_DEBOUNCE_MS - 1);
    expect(preflight).not.toHaveBeenCalled();
    tick(1);

    expect(preflight).toHaveBeenCalledTimes(1);
    expect(preflight.mock.calls[0][0]).toEqual(expect.objectContaining({ testedModelConfigurationIds: [2, 3] }));
    expect(launcher.preflightLoading).toBe(false);
    expect(launcher.preflightAnswered).toBe(true);
    expect(launcher.projection?.plannedRunCount).toBe(4);
    expect(launcher.maxModelsPerBatch).toBe(12);
    discardPeriodicTasks();
  }));

  it('takes the findings and drops an acknowledgment whose key is gone', fakeAsync(() => {
    loadPage();
    launcher.runMode = 'batch';
    launcher.setBatchModels([2, 3]);
    launcher.setFindingAcknowledged('MB-W01:2', true);
    launcher.setFindingAcknowledged('MB-W01:2,3', true);
    preflight.mockReturnValue(of({
      findings: [
        {
          code: 'MB-B03', name: 'GraderIsCandidate', severity: 'Blocker', field: 'assessor',
          title: 'Model One cannot grade itself', detail: '.', modelConfigurationIds: [1]
        },
        warning('MB-W01:2,3'),
        warning('MB-W03', { code: 'MB-W03', name: 'NoClaimVerifier', field: 'verifier', title: 'No claim verifier' })
      ],
      projection: null
    }));

    launcher.requestPreflight();
    tick(MODEL_BATCH_PREFLIGHT_DEBOUNCE_MS);

    expect([...launcher.acknowledgedFindingKeys]).toEqual(['MB-W01:2,3']);
    expect(launcher.batchBlockers.map(f => f.code)).toEqual(['MB-B03']);
    expect(launcher.unacknowledgedBatchWarnings.map(f => f.acknowledgmentKey)).toEqual(['MB-W03']);
    expect(launcher.acknowledgedBatchWarnings.map(f => f.acknowledgmentKey)).toEqual(['MB-W01:2,3']);
    discardPeriodicTasks();
  }));

  it('backs off after a failed check and asks again', fakeAsync(() => {
    loadPage();
    launcher.runMode = 'batch';
    launcher.setBatchModels([2, 3]);
    preflight.mockReturnValueOnce(throwError(() => ({ error: 'The guardrails are unavailable.' })));

    launcher.requestPreflight();
    tick(MODEL_BATCH_PREFLIGHT_DEBOUNCE_MS);
    expect(launcher.preflightError).toBe('The guardrails are unavailable.');
    expect(launcher.preflightAnswered).toBe(false);

    tick(2000 + MODEL_BATCH_PREFLIGHT_DEBOUNCE_MS);

    expect(preflight).toHaveBeenCalledTimes(2);
    expect(launcher.preflightError).toBeNull();
    expect(launcher.preflightAnswered).toBe(true);
    discardPeriodicTasks();
  }));

  it('clears the findings on a switch to One model', fakeAsync(() => {
    loadPage();
    launcher.runMode = 'batch';
    launcher.setBatchModels([2, 3]);
    launcher.applyFindings([warning('MB-W01:2,3')]);

    launcher.runMode = 'single';
    launcher.requestPreflight();
    tick(MODEL_BATCH_PREFLIGHT_DEBOUNCE_MS);

    expect(launcher.findings).toEqual([]);
    expect(launcher.projection).toBeNull();
    expect(preflight).not.toHaveBeenCalled();
    discardPeriodicTasks();
  }));

  it('stores the batch settings but never the acknowledgments', () => {
    loadPage();
    launcher.runMode = 'batch';
    launcher.setBatchModels([3, 2]);
    launcher.batchOrder = 'asListed';
    launcher.batchRunsPerModel = 3;
    launcher.setFindingAcknowledged('MB-W01:2,3', true);

    launcher.persistRunSettings();

    expect(stored()).toEqual(expect.objectContaining({
      runMode: 'batch', batchModelIds: [3, 2], batchOrder: 'asListed', batchOrderIds: [3, 2], batchRunsPerModel: 3
    }));
    expect(JSON.stringify(stored())).not.toContain('MB-W01');
  });

  it('restores the batch settings, dropping a model that no longer qualifies', fakeAsync(() => {
    localStorage.setItem(RUN_SETTINGS_KEY, JSON.stringify({
      suiteId: 1, testedConfigId: 1, assessorConfigId: 1, runMode: 'batch',
      batchModelIds: [2, 4, 3, 99], batchOrder: 'asListed', batchOrderIds: [3, 4, 2], batchRunsPerModel: 3
    }));

    loadPage();
    tick(MODEL_BATCH_PREFLIGHT_DEBOUNCE_MS);

    expect(launcher.runMode).toBe('batch');
    expect(launcher.batchModelKeys).toEqual([2, 3]);
    expect(launcher.batchOrderKeys).toEqual([3, 2]);
    expect(launcher.batchOrder).toBe('asListed');
    expect(launcher.batchRunsPerModel).toBe(3);
    expect(launcher.acknowledgedFindingKeys.size).toBe(0);
    expect(preflight).toHaveBeenCalled();
    discardPeriodicTasks();
  }));

  it('restores One model from a blob that predates model batches', () => {
    localStorage.setItem(RUN_SETTINGS_KEY, JSON.stringify({ suiteId: 1, testedConfigId: 1, assessorConfigId: 1 }));

    loadPage();

    expect(launcher.runMode).toBe('single');
    expect(launcher.batchModelKeys).toEqual([]);
    expect(launcher.batchOrder).toBe('randomized');
  });
});
