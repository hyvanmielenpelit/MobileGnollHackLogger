import { TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import {
  AdminBenchmarkService,
  BenchmarkRunDetailDto,
  BenchmarkScoringProfileDto,
  BenchmarkSuiteDto
} from '../../../services/admin-benchmark.service';
import { SystemAiConfigDto } from '../../../services/admin.service';
import { BenchmarkLauncherState } from './benchmark-launcher.state';
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
});
