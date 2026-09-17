import {
  BUILDER_LABELS,
  INSTRUCTIONS_FILE_NAME,
  PANEL_LABELS,
  WIZARD_LABELS,
  WorkflowDetails,
  buildSuiteWorkflowInstructions
} from './suite-workflow-instructions';

/** Every `**bold**` run of a Markdown text. */
function boldRuns(markdown: string): string[] {
  return Array.from(markdown.matchAll(/\*\*([^*]+)\*\*/g)).map(m => m[1]);
}

function allowedLabels(details: WorkflowDetails): Set<string> {
  const suite = details.suiteName ?? '<suite>';
  const name = details.suiteName ?? '<name>';
  const count = details.questionCount ?? 'N';
  return new Set<string>([
    ...Object.values(WIZARD_LABELS),
    ...Object.values(BUILDER_LABELS),
    PANEL_LABELS.validateAndReview,
    PANEL_LABELS.addQuestions(count, suite),
    PANEL_LABELS.createSuite(name),
    // The chosen suite, named in bold in route A's second step.
    suite
  ]);
}

describe('buildSuiteWorkflowInstructions', () => {
  it('names the download file', () => {
    expect(INSTRUCTIONS_FILE_NAME).toBe('overseer-snapshot-suite-steps.md');
  });

  it('lists both routes as numbered checklists for the help dialog', () => {
    const text = buildSuiteWorkflowInstructions('both');
    expect(text).toContain('## A — The snapshot is attached to a suite in Overseer');
    expect(text).toContain('## B — A snapshot file from GnollHack');
    expect(text).toMatch(/^1\. \[ \] /m);
    expect(text).not.toContain('\r');
  });

  it('bolds nothing but a real control label, generic and concrete', () => {
    const concrete: WorkflowDetails = {
      suiteName: 'Valkyrie at Dlvl 11',
      sourceFileName: 'overseer-suite-export-valkyrie-at-dlvl-11.yaml',
      sourcePath: 'C:\\temp\\overseer-suite-export-valkyrie-at-dlvl-11.yaml',
      outputFileName: 'agent-new-questions-valkyrie-at-dlvl-11.yaml',
      questionCount: 18
    };
    for (const details of [{}, concrete] as WorkflowDetails[]) {
      for (const route of ['add-to-suite', 'create-suite'] as const) {
        const allowed = allowedLabels(details);
        for (const run of boldRuns(buildSuiteWorkflowInstructions(route, details))) {
          expect(allowed.has(run)).withContext(`${route}: **${run}**`).toBeTrue();
        }
      }
    }
  });

  it('writes the concrete names once they are known', () => {
    const text = buildSuiteWorkflowInstructions('add-to-suite', {
      suiteName: 'Core', outputFileName: 'agent-new-questions-core.yaml', questionCount: 1, sourcePath: 'C:\\t\\s.yaml'
    });
    expect(text).toContain('**Add 1 Question to Core**');
    expect(text).toContain('`agent-new-questions-core.yaml`');
    expect(text).toContain('the one starting `agent-new-`, not the `overseer-suite-export-` file you downloaded');
    expect(text).toContain('this is the file the agent reads');
    expect(text).toContain('`C:\\t\\s.yaml`');
    expect(text).not.toContain('## B');
  });

  it('ends route A with assessment, the suggested description and the optional checks', () => {
    expect(WIZARD_LABELS.applyDescription).toBe('Apply Suggested Description');
    const text = buildSuiteWorkflowInstructions('add-to-suite');
    expect(text).toContain('its name and snapshot are not changed; the description changes only if you apply the suggested one.');
    expect(text).toContain('Press **Assess Difficulty**; the suite cannot run until every question is assessed. The step shows a check mark when it is done.');
    expect(text).toContain('Press **Next**, read the description the agent suggested — if the YAML file included none, paste it from the agent\'s handoff — and press **Apply Suggested Description**. You can change it later with **Edit suite**.');
    expect(text).toMatch(/\. \[ \] Optionally run Suite Health \*Snapshot facts\* and the citation check\.\n$/);
  });

  it('names the create button for route B', () => {
    expect(buildSuiteWorkflowInstructions('create-suite', { suiteName: 'New' })).toContain('**Create Suite New**');
  });
});
