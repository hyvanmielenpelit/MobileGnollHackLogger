import type { Mock } from 'vitest';
import { TestBed, discardPeriodicTasks, fakeAsync, tick } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import {
  AdminBenchmarkService,
  BenchmarkBatteryDto,
  BenchmarkModelBatchFindingDto,
  BenchmarkRunDetailDto,
  BenchmarkScoringProfileDto,
  BenchmarkSecondOpinionMode,
  BenchmarkSuiteDto,
  RecommendedRun
} from '../../../services/admin-benchmark.service';
import { SystemAiConfigDto } from '../../../services/admin.service';
import { BenchmarkLauncherState, MODEL_BATCH_PREFLIGHT_DEBOUNCE_MS } from './benchmark-launcher.state';
import { launcherSetupFromRecommendedRun } from './benchmark-shell-bridge.service';
import type { LauncherSetupPrefill } from './benchmark-shell-bridge.service';
import { BenchmarkViewSync } from './benchmark-view-sync.service';
import { BenchmarkWorkspaceStore } from './benchmark-workspace.store';
import { buildCatalog, buildChange, builtInChanges, citePageFirst } from '../run-tab/experimental-changes.testing';

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

describe('BenchmarkLauncherState: models removed from the catalog', () => {
  let launcher: BenchmarkLauncherState;
  let workspace: BenchmarkWorkspaceStore;

  const retired = { status: 'retired', needsAttention: true, retiredOn: '2026-09-30' } as const;

  const stored = (): any => JSON.parse(localStorage.getItem(RUN_SETTINGS_KEY) ?? 'null');

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
    workspace.setSystemConfigs([
      config(1, 'Model One'),
      config(2, 'Model Two', { provider: 'OpenAI', modelAvailability: retired }),
      config(3, 'Model Three', { provider: 'Google', modelAvailability: retired }),
      config(4, 'Model Four', { provider: 'Google', modelAvailability: { status: 'notInCatalog', needsAttention: true } })
    ]);
    launcher.testedConfigId = 1;
    launcher.assessorConfigId = 1;
  });

  afterEach(clearRunSettings);

  it('is null while no selected role uses a retired model', () => {
    launcher.claimVerifierConfigId = 4;

    expect(launcher.retiredRoleRefusal()).toBeNull();
  });

  it('names the first selected role in field order whose model was removed', () => {
    launcher.reportWriterConfigId = 3;
    launcher.claimVerifierConfigId = 2;

    expect(launcher.retiredRoleRefusal()).toEqual({
      role: 'claimVerifier', label: 'Claim Verifier', config: expect.objectContaining({ id: 2 })
    });

    launcher.testedConfigId = 3;
    expect(launcher.retiredRoleRefusal()?.label).toBe('Model Under Test');
  });

  it('labels the second reader as the reference reader in a panel run', () => {
    launcher.secondOpinionConfigId = 2;
    expect(launcher.retiredRoleRefusal()?.label).toBe('Second Reader');

    launcher.coAssessorConfigId = 1;
    expect(launcher.retiredRoleRefusal()).toEqual(expect.objectContaining({ role: 'secondOpinion', label: 'Reference Reader' }));
  });

  it('leaves the hidden Model Under Test out in a model batch', () => {
    launcher.testedConfigId = 2;
    launcher.runMode = 'batch';

    expect(launcher.retiredRoleRefusal()).toBeNull();
  });

  it('keeps a remembered retired configuration selected, with no restore note', () => {
    localStorage.setItem(RUN_SETTINGS_KEY, JSON.stringify({ testedConfigId: 2, assessorConfigId: 1, claimVerifierConfigId: 3 }));
    launcher.testedConfigId = null;
    launcher.assessorConfigId = null;

    launcher.restoreRunSettings();
    launcher.setDefaultModelSelections();

    expect(launcher.testedConfigId).toBe(2);
    expect(launcher.claimVerifierConfigId).toBe(3);
    expect(launcher.restoreNotes).toEqual([]);
    expect(launcher.retiredRoleRefusal()?.role).toBe('tested');
  });

  it('notes each remembered configuration that no longer qualifies and what was chosen instead', () => {
    localStorage.setItem(RUN_SETTINGS_KEY, JSON.stringify({
      testedConfigId: 9, assessorConfigId: 1, coAssessorConfigId: 8, reportWriterConfigId: 4
    }));
    launcher.testedConfigId = null;
    launcher.assessorConfigId = null;

    launcher.restoreRunSettings();
    launcher.setDefaultModelSelections();

    expect(launcher.testedConfigId).toBe(1);
    expect(launcher.coAssessorConfigId).toBeNull();
    expect(launcher.reportWriterConfigId).toBe(4);
    expect(launcher.restoreNotes).toEqual([
      'Model Under Test: the remembered configuration is no longer available, so Model One was chosen.',
      'Co-Assessor: the remembered configuration is no longer available, so none was chosen.'
    ]);
    expect(stored()).not.toHaveProperty('restoreNotes');

    // A second pass over the same lists does not repeat a note.
    launcher.setDefaultModelSelections();
    expect(launcher.restoreNotes.length).toBe(2);

    launcher.clearRestoreNotes();
    expect(launcher.restoreNotes).toEqual([]);
  });

  it('notes nothing without remembered settings', () => {
    launcher.testedConfigId = null;
    launcher.restoreRunSettings();
    launcher.setDefaultModelSelections();

    expect(launcher.testedConfigId).toBe(1);
    expect(launcher.restoreNotes).toEqual([]);
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

describe('BenchmarkLauncherState: prefillFromSetup', () => {
  let launcher: BenchmarkLauncherState;
  let workspace: BenchmarkWorkspaceStore;
  let service: Record<string, Mock>;

  const battery = (id: number, name: string): BenchmarkBatteryDto => ({
    id, name, isArchived: false, brokenSuiteNames: [], validationErrors: [],
    suites: [
      { index: 0, suiteId: 1, suiteName: 'Default Suite', difficultyFullyAssessed: true },
      { index: 1, suiteId: 2, suiteName: 'Second Suite', difficultyFullyAssessed: true }
    ]
  }) as unknown as BenchmarkBatteryDto;

  /**
   * A control run on suite 2: a batch of models 2 and 3, as listed, graded by a panel of 1 and 3 with 2
   * as the reference reader reading every answer, and 2 verifying claims.
   */
  function setup(overrides: Partial<LauncherSetupPrefill> = {}): LauncherSetupPrefill {
    return {
      sourceLabel: 'Set up from Root Cause Analysis #12, run 1',
      kind: 'control',
      targetKind: 'Suite', suiteId: 2, suiteName: 'Second Suite', batteryId: null, batteryName: null,
      runMode: 'Batch', testedModelConfigurationIds: [2, 3], modelNames: ['Model Two', 'Model Three'], order: 'AsListed',
      runsPerModel: 2, runCount: 1, scoringProfileId: 2, verboseMode: true, allowSourceCodeReferences: true,
      assessorModelConfigurationId: 1, coAssessorModelConfigurationId: 3, secondOpinionAssessorModelConfigurationId: 2,
      secondOpinionMode: BenchmarkSecondOpinionMode.All, claimVerifierModelConfigurationId: 2, notes: [],
      ...overrides
    };
  }

  /** The server's note for graders the analyzed runs did not share. */
  const NOT_SHARED = 'The analyzed runs differ on CoAssessorModelConfigurationId, so it is left for you to choose.';

  function loadPage(): void {
    launcher.restoreRunSettings();
    workspace.suitesLoaded$.next();
    workspace.profilesLoaded$.next();
    launcher.setDefaultModelSelections();
    workspace.batteriesLoaded$.next(true);
  }

  beforeEach(() => {
    clearRunSettings();
    service = {
      getLastAssessor: vi.fn(() => of({})),
      previewBatteryReuse: vi.fn(() => of(null)),
      preflightModelBatch: vi.fn(() => of({ findings: [], projection: null, maxModels: 12 })),
      startRun: vi.fn(),
      startRunSeries: vi.fn(),
      startBatteryRun: vi.fn(),
      startModelBatch: vi.fn()
    };
    TestBed.configureTestingModule({
      providers: [
        BenchmarkLauncherState,
        BenchmarkWorkspaceStore,
        BenchmarkViewSync,
        { provide: AdminBenchmarkService, useValue: service }
      ]
    });
    launcher = TestBed.inject(BenchmarkLauncherState);
    workspace = TestBed.inject(BenchmarkWorkspaceStore);
    workspace.suites = [suite(1, 'Default Suite'), suite(2, 'Second Suite')];
    workspace.scoringProfiles = [profile(1, 'Standard', true), profile(2, 'Strict')];
    workspace.launcherBatteries = [battery(5, 'Core Battery')];
    workspace.setSystemConfigs([
      config(1, 'Model One'), config(2, 'Model Two', { provider: 'OpenAI' }), config(3, 'Model Three', { provider: 'Google' }),
      config(4, 'Model Four', { isEnabled: false })
    ]);
  });

  afterEach(clearRunSettings);

  it('applies every setting, clears the report writer, labels the note and starts nothing', () => {
    loadPage();
    launcher.reportWriterConfigId = 1;
    launcher.reuseEarlierRuns = true;

    launcher.prefillFromSetup(setup());

    expect(launcher.runTargetKind).toBe('suite');
    expect(launcher.selectedSuiteId).toBe(2);
    expect(launcher.selectedScoringProfileId).toBe(2);
    expect(launcher.runMode).toBe('batch');
    expect(launcher.batchModelKeys).toEqual([2, 3]);
    expect(launcher.batchOrder).toBe('asListed');
    expect(launcher.batchOrderKeys).toEqual([2, 3]);
    expect(launcher.batchRunsPerModel).toBe(2);
    expect(launcher.assessorConfigId).toBe(1);
    expect(launcher.coAssessorConfigId).toBe(3);
    expect(launcher.secondOpinionConfigId).toBe(2);
    expect(launcher.claimVerifierConfigId).toBe(2);
    expect(launcher.reportWriterConfigId).toBeNull();
    expect(launcher.reuseEarlierRuns).toBe(false);
    expect(launcher.candidateVerboseMode).toBe(true);
    expect(launcher.candidateAllowSourceCodeReferences).toBe(true);
    expect(launcher.prefillResult).toEqual({
      runId: null, sourceLabel: 'Set up from Root Cause Analysis #12, run 1', notes: []
    });
    for (const start of ['startRun', 'startRunSeries', 'startBatteryRun', 'startModelBatch']) {
      expect(service[start]).not.toHaveBeenCalled();
    }
  });

  it('applies a single-model run with its Number of Runs and second-opinion coverage', () => {
    loadPage();

    launcher.prefillFromSetup(setup({
      runMode: 'Single', testedModelConfigurationIds: [3], modelNames: ['Model Three'], runCount: 2, runsPerModel: 1,
      coAssessorModelConfigurationId: null, secondOpinionMode: BenchmarkSecondOpinionMode.FlaggedAndOutliers
    }));

    expect(launcher.runMode).toBe('single');
    expect(launcher.testedConfigId).toBe(3);
    expect(launcher.runCount).toBe(2);
    expect(launcher.coAssessorConfigId).toBeNull();
    expect(launcher.secondOpinionMode).toBe(BenchmarkSecondOpinionMode.FlaggedAndOutliers);
    expect(launcher.prefillResult!.notes).toEqual([]);
  });

  it('applies the grader set whole: a null grader is none and a null coverage follows the profile', () => {
    loadPage();
    launcher.coAssessorConfigId = 3;
    launcher.secondOpinionConfigId = 2;
    launcher.claimVerifierConfigId = 2;
    launcher.secondOpinionMode = BenchmarkSecondOpinionMode.All;

    launcher.prefillFromSetup(setup({
      coAssessorModelConfigurationId: null, secondOpinionAssessorModelConfigurationId: null,
      secondOpinionMode: null, claimVerifierModelConfigurationId: null
    }));

    expect(launcher.assessorConfigId).toBe(1);
    expect(launcher.coAssessorConfigId).toBeNull();
    expect(launcher.secondOpinionConfigId).toBeNull();
    expect(launcher.claimVerifierConfigId).toBeNull();
    // Profile 2's own coverage.
    expect(launcher.secondOpinionMode).toBe(BenchmarkSecondOpinionMode.Flagged);
    expect(launcher.prefillResult!.notes).toEqual([]);
  });

  it('keeps the current graders when the server notes that the analyzed runs did not share them, and shows that note', () => {
    loadPage();
    launcher.assessorConfigId = 2;
    launcher.coAssessorConfigId = 3;
    launcher.secondOpinionConfigId = 1;
    launcher.claimVerifierConfigId = 1;

    launcher.prefillFromSetup(setup({
      assessorModelConfigurationId: 1, coAssessorModelConfigurationId: null, secondOpinionAssessorModelConfigurationId: null,
      secondOpinionMode: null, claimVerifierModelConfigurationId: null, notes: [NOT_SHARED]
    }));

    expect(launcher.assessorConfigId).toBe(2);
    expect(launcher.coAssessorConfigId).toBe(3);
    expect(launcher.secondOpinionConfigId).toBe(1);
    expect(launcher.claimVerifierConfigId).toBe(1);
    expect(launcher.prefillResult!.notes).toEqual([NOT_SHARED]);
    // The rest of the setup is still applied.
    expect(launcher.selectedSuiteId).toBe(2);
    expect(launcher.batchModelKeys).toEqual([2, 3]);
  });

  it('notes each missing suite, profile, model and grader by its stored name, and keeps those fields', () => {
    loadPage();
    const before = { suite: launcher.selectedSuiteId, profile: launcher.selectedScoringProfileId, claim: launcher.claimVerifierConfigId };

    launcher.prefillFromSetup(setup({
      suiteId: 9, suiteName: 'Deleted Suite', scoringProfileId: 8,
      testedModelConfigurationIds: [2, 4, 77], modelNames: ['Model Two', 'Model Four', 'Retired Model'],
      claimVerifierModelConfigurationId: 4
    }));

    expect(launcher.selectedSuiteId).toBe(before.suite);
    expect(launcher.selectedScoringProfileId).toBe(before.profile);
    expect(launcher.claimVerifierConfigId).toBe(before.claim);
    expect(launcher.batchModelKeys).toEqual([2]);
    const notes = launcher.prefillResult!.notes;
    expect(notes).toEqual([
      expect.stringContaining('Benchmark Suite: Deleted Suite'),
      expect.stringContaining('Scoring Profile: profile #8'),
      expect.stringContaining('Model Under Test: Model Four'),
      expect.stringContaining('Model Under Test: Retired Model'),
      expect.stringContaining('Claim Verifier: Model Four')
    ]);
  });

  it('notes a missing battery by its stored name', () => {
    loadPage();

    launcher.prefillFromSetup(setup({ targetKind: 'Battery', batteryId: 6, batteryName: 'Archived Battery', suiteId: null, suiteName: null }));

    expect(launcher.runTargetKind).toBe('battery');
    expect(launcher.selectedBatteryId).toBe(5);
    expect(launcher.prefillResult!.notes).toEqual([expect.stringContaining('Battery: Archived Battery')]);
  });

  it('sets Runs per Suite to 3 on a battery batch and says so', () => {
    loadPage();

    launcher.prefillFromSetup(setup({
      kind: 'replicate', targetKind: 'Battery', batteryId: 5, batteryName: 'Core Battery', suiteId: null, suiteName: null,
      runsPerModel: 3, runCount: 1
    }));

    expect(launcher.selectedBatteryId).toBe(5);
    expect(launcher.runCount).toBe(3);
    expect(launcher.prefillResult!.notes).toEqual(['Runs per Suite was set to 3 for this recommendation.']);
  });

  it('asks a time-of-day repeat to start at a different time of day once, beside the recommendation\'s own notes', () => {
    loadPage();

    launcher.prefillFromSetup(setup({ kind: 'timeStratum', runsPerModel: 1, notes: ['Run one in the evening.'] }));
    const notes = launcher.prefillResult!.notes;
    expect(notes[0]).toContain('different time of day');
    expect(notes[1]).toBe('Run one in the evening.');

    const serverNote = 'Start each of the 3 runs at a different time of day.';
    launcher.prefillFromSetup(setup({ kind: 'timeStratum', runsPerModel: 1, notes: [serverNote] }));
    expect(launcher.prefillResult!.notes).toEqual([serverNote]);
  });

  it('waits for the lists, then applies', () => {
    launcher.restoreRunSettings();

    launcher.prefillFromSetup(setup());

    expect(launcher.pendingPrefillSetupLabel).toBe('Set up from Root Cause Analysis #12, run 1');
    expect(launcher.prefillResult).toBeNull();

    workspace.suitesLoaded$.next();
    workspace.profilesLoaded$.next();
    launcher.setDefaultModelSelections();

    expect(launcher.pendingPrefillSetupLabel).toBeNull();
    expect(launcher.prefillResult?.sourceLabel).toBe('Set up from Root Cause Analysis #12, run 1');
    expect(launcher.selectedSuiteId).toBe(2);
  });

  it('sets the production condition, no experimental changes and an offered feedback tool, as a recommended run has them', () => {
    loadPage();
    launcher.informationCondition = 'closedBook';
    launcher.instructionVariant = 'race-role-tool-v1';
    launcher.withholdFeedbackTool = true;

    launcher.prefillFromSetup(setup());

    expect(launcher.informationCondition).toBe('production');
    expect(launcher.instructionVariant).toBeNull();
    expect(launcher.withholdFeedbackTool).toBe(false);
    expect(launcher.prefillResult!.notes).toEqual([]);

    const prefill = launcherSetupFromRecommendedRun({ kind: 'control', targetKind: 'Suite', runMode: 'Single' } as RecommendedRun, 'x');
    expect(prefill.informationCondition).toBe('production');
    expect(prefill.instructionVariant).toBeNull();
  });
});

describe('BenchmarkLauncherState: information condition and experimental changes', () => {
  let launcher: BenchmarkLauncherState;
  let workspace: BenchmarkWorkspaceStore;
  let catalogMock: Mock;

  const stored = (): any => JSON.parse(localStorage.getItem(RUN_SETTINGS_KEY) ?? 'null');
  const SPEC = 'race-role-tool-v1+stats-referenced-by-v1';

  function loadPage(): void {
    launcher.restoreRunSettings();
    workspace.suitesLoaded$.next();
    workspace.profilesLoaded$.next();
    launcher.setDefaultModelSelections();
    workspace.batteriesLoaded$.next(true);
  }

  beforeEach(() => {
    clearRunSettings();
    catalogMock = vi.fn(() => of(buildCatalog([
      ...builtInChanges(),
      citePageFirst({ retired: true }),
      buildChange({ id: 'broken-v1', isValid: false, invalidReason: 'Its change.json does not parse.' })
    ])));
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
            getExperimentalChanges: catalogMock
          }
        }
      ]
    });
    launcher = TestBed.inject(BenchmarkLauncherState);
    workspace = TestBed.inject(BenchmarkWorkspaceStore);
    workspace.suites = [suite(1, 'Default Suite'), suite(2, 'Second Suite')];
    workspace.scoringProfiles = [profile(1, 'Standard', true), profile(2, 'Strict')];
    workspace.setSystemConfigs([
      config(1, 'Model One'), config(2, 'Model Two', { provider: 'OpenAI' }), config(3, 'Model Three', { provider: 'Google' })
    ]);
  });

  afterEach(clearRunSettings);

  it('defaults to Production and no changes, and the request carries both', () => {
    expect(launcher.informationCondition).toBe('production');
    expect(launcher.instructionVariant).toBeNull();

    const req = launcher.buildRunRequest(1, 2, 1);
    expect(req.informationCondition).toBe('production');
    expect(req.instructionVariant).toBeNull();

    launcher.informationCondition = 'sourceOnly';
    launcher.instructionVariant = SPEC;
    const changed = launcher.buildRunRequest(1, 2, 1);
    expect(changed.informationCondition).toBe('sourceOnly');
    expect(changed.instructionVariant).toBe(SPEC);
  });

  it('carries both into a model batch template', () => {
    loadPage();
    launcher.runMode = 'batch';
    launcher.setBatchModels([2, 3]);
    launcher.informationCondition = 'closedBook';
    launcher.instructionVariant = 'asker-character-procedure-v1';

    const req = launcher.buildModelBatchRequest()!;

    expect(req.run.informationCondition).toBe('closedBook');
    expect(req.run.instructionVariant).toBe('asker-character-procedure-v1');
  });

  it('round-trips both through the remembered settings', () => {
    loadPage();
    launcher.informationCondition = 'closedBook';
    launcher.instructionVariant = SPEC;
    launcher.persistRunSettings();
    expect(stored()).toEqual(expect.objectContaining({ informationCondition: 'closedBook', instructionVariant: SPEC }));

    launcher.informationCondition = 'production';
    launcher.instructionVariant = null;
    launcher.restoreRunSettings();
    expect(launcher.informationCondition).toBe('closedBook');
    expect(launcher.instructionVariant).toBe(SPEC);
  });

  it('restores Production from a blob that predates the fields or names an unknown condition', () => {
    localStorage.setItem(RUN_SETTINGS_KEY, JSON.stringify({ suiteId: 1, informationCondition: 'openBook' }));

    loadPage();

    expect(launcher.informationCondition).toBe('production');
    expect(launcher.instructionVariant).toBeNull();
  });

  it('resolves the spec against the catalog, in spec order', () => {
    loadPage();
    launcher.loadExperimentalChanges();
    launcher.instructionVariant = SPEC;

    expect(catalogMock).toHaveBeenCalledTimes(1);
    expect(launcher.selectedExperimentalChanges.map(c => c.id)).toEqual(['race-role-tool-v1', 'stats-referenced-by-v1']);
    launcher.instructionVariant = 'race-role-tool-v1+gone-v1';
    expect(launcher.selectedExperimentalChanges).toEqual([]);
  });

  it('keeps a remembered spec of offered changes, in canonical order', () => {
    localStorage.setItem(RUN_SETTINGS_KEY, JSON.stringify({ suiteId: 1, instructionVariant: 'stats-referenced-by-v1+race-role-tool-v1' }));
    loadPage();

    launcher.loadExperimentalChanges();

    expect(launcher.instructionVariant).toBe(SPEC);
    expect(launcher.restoreNotes).toEqual([]);
    expect(stored().instructionVariant).toBe(SPEC);
  });

  it('resets a remembered legacy spec to None, with the note', () => {
    localStorage.setItem(RUN_SETTINGS_KEY, JSON.stringify({ suiteId: 1, instructionVariant: 'asker-profile-v1' }));
    loadPage();
    expect(launcher.instructionVariant).toBe('asker-profile-v1');

    launcher.loadExperimentalChanges();

    expect(launcher.instructionVariant).toBeNull();
    expect(launcher.restoreNotes).toEqual(['Experimental Changes reset to None: asker-profile-v1 is no longer offered.']);
    expect(stored().instructionVariant).toBeNull();
  });

  it('resets a spec with a retired or an invalid change, naming each', () => {
    localStorage.setItem(RUN_SETTINGS_KEY, JSON.stringify({ suiteId: 1, instructionVariant: 'broken-v1+cite-page-first-v1+race-role-tool-v1' }));
    loadPage();

    launcher.loadExperimentalChanges();

    expect(launcher.instructionVariant).toBeNull();
    expect(launcher.restoreNotes).toEqual(['Experimental Changes reset to None: broken-v1 and cite-page-first-v1 are no longer offered.']);
  });

  it('falls back to None, with a note, when the catalog cannot be read', () => {
    catalogMock.mockReturnValue(throwError(() => ({ status: 500 })));
    loadPage();
    launcher.instructionVariant = SPEC;

    launcher.loadExperimentalChanges();

    expect(launcher.experimentalChangesLoaded).toBe(true);
    expect(launcher.experimentalChanges).toBeNull();
    expect(launcher.experimentalChangesError).toBe('the server did not answer.');
    expect(launcher.instructionVariant).toBeNull();
    expect(launcher.restoreNotes).toEqual(['Experimental Changes reset to None: the list of changes could not be loaded.']);
  });

  it('reloads the catalog after an edit, and runs the callback once it has arrived', () => {
    loadPage();
    launcher.loadExperimentalChanges();
    launcher.instructionVariant = SPEC;
    catalogMock.mockReturnValue(of(buildCatalog([...builtInChanges(), citePageFirst()])));

    let done = 0;
    launcher.reloadExperimentalChanges(() => done++);

    expect(catalogMock).toHaveBeenCalledTimes(2);
    expect(done).toBe(1);
    expect(launcher.experimentalChangeList.find(c => c.id === 'cite-page-first-v1')?.retired).toBe(false);
    expect(launcher.instructionVariant).toBe(SPEC);
  });

  it('keeps the catalog and the spec when a reload fails', () => {
    loadPage();
    launcher.loadExperimentalChanges();
    launcher.instructionVariant = SPEC;
    catalogMock.mockReturnValue(throwError(() => ({ status: 500, error: { error: 'Down for a moment.' } })));

    let done = 0;
    launcher.reloadExperimentalChanges(() => done++);

    expect(done).toBe(1);
    expect(launcher.experimentalChangesError).toBe('Down for a moment.');
    expect(launcher.experimentalChangeList.length).toBeGreaterThan(0);
    expect(launcher.instructionVariant).toBe(SPEC);
    expect(launcher.restoreNotes).toEqual([]);
  });

  it('repeats a run\'s condition and changes, the spec without its hash', () => {
    loadPage();
    launcher.loadExperimentalChanges();

    launcher.prefillFromRun(run({
      candidatePromptOptionsJson: '{"verboseMode":true,"enableToolUse":true,"allowSourceCodeReferences":true,"informationCondition":"sourceOnly"}',
      instructionVariant: `${SPEC}:ab12cd34ef56`
    }));

    expect(launcher.informationCondition).toBe('sourceOnly');
    expect(launcher.instructionVariant).toBe(SPEC);
    expect(launcher.prefillResult!.notes).toEqual([]);
    expect(stored()).toEqual(expect.objectContaining({ informationCondition: 'sourceOnly', instructionVariant: SPEC }));
  });

  it('repeats Production for a run whose prompt options name no condition, falling back to the run\'s own field', () => {
    loadPage();
    launcher.informationCondition = 'closedBook';

    launcher.prefillFromRun(run());
    expect(launcher.informationCondition).toBe('production');

    launcher.prefillFromRun(run({ informationCondition: 'closedBook' }));
    expect(launcher.informationCondition).toBe('closedBook');
  });

  it('keeps the current Tool Access, with a note, for a condition the launcher does not offer', () => {
    loadPage();
    launcher.informationCondition = 'sourceOnly';

    launcher.prefillFromRun(run({ informationCondition: 'oracle' }));

    expect(launcher.informationCondition).toBe('sourceOnly');
    expect(launcher.prefillResult!.notes).toEqual([
      'Tool Access: the run used oracle, which this launcher does not offer, so the current choice was kept.'
    ]);
  });

  it('repeats no changes, with a note, when the catalog no longer offers the run\'s', () => {
    loadPage();
    launcher.loadExperimentalChanges();
    launcher.instructionVariant = SPEC;

    launcher.prefillFromRun(run({ instructionVariant: 'cite-page-first-v1:0123456789ab' }));

    expect(launcher.instructionVariant).toBeNull();
    expect(launcher.prefillResult!.notes).toEqual(['Experimental Changes reset to None: cite-page-first-v1 is no longer offered.']);
  });

  it('checks a repeated spec once the catalog arrives, when it arrives later', () => {
    loadPage();

    launcher.prefillFromRun(run({ instructionVariant: 'gone-v1:0123456789ab' }));
    expect(launcher.instructionVariant).toBe('gone-v1');

    launcher.loadExperimentalChanges();

    expect(launcher.instructionVariant).toBeNull();
    expect(launcher.prefillResult!.notes).toEqual(['Experimental Changes reset to None: gone-v1 is no longer offered.']);
  });

  it('strips the hash at the first colon, so a spec keeps its plus signs', () => {
    expect(BenchmarkLauncherState.instructionVariantName(`${SPEC}:ab12cd34ef56`)).toBe(SPEC);
    expect(BenchmarkLauncherState.instructionVariantName('race-role-tool-v1:ab12cd34ef56')).toBe('race-role-tool-v1');
    expect(BenchmarkLauncherState.instructionVariantName('race-role-tool-v1')).toBe('race-role-tool-v1');
    expect(BenchmarkLauncherState.instructionVariantName('  ')).toBeNull();
  });
});

describe('BenchmarkLauncherState: feedback tool', () => {
  let launcher: BenchmarkLauncherState;
  let workspace: BenchmarkWorkspaceStore;

  const stored = (): any => JSON.parse(localStorage.getItem(RUN_SETTINGS_KEY) ?? 'null');

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
          useValue: {
            getLastAssessor: vi.fn(() => of({})),
            previewBatteryReuse: vi.fn(() => of(null)),
            getExperimentalChanges: vi.fn(() => of(buildCatalog([])))
          }
        }
      ]
    });
    launcher = TestBed.inject(BenchmarkLauncherState);
    workspace = TestBed.inject(BenchmarkWorkspaceStore);
    workspace.suites = [suite(1, 'Default Suite'), suite(2, 'Second Suite')];
    workspace.scoringProfiles = [profile(1, 'Standard', true), profile(2, 'Strict')];
    workspace.setSystemConfigs([
      config(1, 'Model One'), config(2, 'Model Two', { provider: 'OpenAI' }), config(3, 'Model Three', { provider: 'Google' })
    ]);
  });

  afterEach(clearRunSettings);

  it('defaults to Offered, and the request carries the choice', () => {
    expect(launcher.withholdFeedbackTool).toBe(false);
    expect(launcher.buildRunRequest(1, 2, 1).withholdFeedbackTool).toBe(false);

    launcher.withholdFeedbackTool = true;
    expect(launcher.buildRunRequest(1, 2, 1).withholdFeedbackTool).toBe(true);
  });

  it('carries the choice into a model batch template', () => {
    loadPage();
    launcher.runMode = 'batch';
    launcher.setBatchModels([2, 3]);
    launcher.withholdFeedbackTool = true;

    expect(launcher.buildModelBatchRequest()!.run.withholdFeedbackTool).toBe(true);
  });

  it('round-trips the choice through the remembered settings', () => {
    loadPage();
    launcher.withholdFeedbackTool = true;
    launcher.persistRunSettings();
    expect(stored()).toEqual(expect.objectContaining({ withholdFeedbackTool: true }));

    launcher.withholdFeedbackTool = false;
    launcher.restoreRunSettings();
    expect(launcher.withholdFeedbackTool).toBe(true);
  });

  it('restores Offered from a blob that predates the field', () => {
    localStorage.setItem(RUN_SETTINGS_KEY, JSON.stringify({ suiteId: 1 }));

    loadPage();

    expect(launcher.withholdFeedbackTool).toBe(false);
  });

  it('restores Offered from a blob whose value is not a boolean', () => {
    localStorage.setItem(RUN_SETTINGS_KEY, JSON.stringify({ suiteId: 1, withholdFeedbackTool: 'yes' }));
    launcher.withholdFeedbackTool = false;

    loadPage();

    expect(launcher.withholdFeedbackTool).toBe(false);
  });

  it('repeats a run\'s recorded choice, and Offered for a run that recorded none', () => {
    loadPage();

    launcher.prefillFromRun(run({
      candidatePromptOptionsJson: '{"verboseMode":false,"enableToolUse":true,"feedbackToolWithheld":true}'
    }));
    expect(launcher.withholdFeedbackTool).toBe(true);
    expect(launcher.prefillResult!.notes).toEqual([]);
    expect(stored()).toEqual(expect.objectContaining({ withholdFeedbackTool: true }));

    launcher.prefillFromRun(run({ candidatePromptOptionsJson: '{"verboseMode":false,"enableToolUse":true}' }));
    expect(launcher.withholdFeedbackTool).toBe(false);
    expect(launcher.prefillResult!.notes).toEqual([]);
  });
});
