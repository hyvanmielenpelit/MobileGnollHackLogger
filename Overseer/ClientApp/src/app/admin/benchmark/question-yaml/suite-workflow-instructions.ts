/**
 * The step-by-step checklist for turning a game snapshot into a suite, as Markdown the admin can
 * copy or download. Every control is named in bold exactly as it is labelled; the labels are the
 * constants below, which the wizard, the prompt builder and the import panel bind in their
 * templates, so a renamed button changes the instructions with it.
 */

export const INSTRUCTIONS_FILE_NAME = 'overseer-snapshot-suite-steps.md';

export const WIZARD_LABELS = {
  title: 'Snapshot Suite Wizard',
  routeSuite: 'Attached to a suite in Overseer',
  routeFile: 'A snapshot file from GnollHack',
  next: 'Next',
  downloadSuite: 'Download Suite YAML',
  pathSuite: 'Where did you save it?',
  pathFile: 'Where is the snapshot file?',
  checkFile: 'Check a snapshot file',
  assess: 'Assess Difficulty',
  editSuite: 'Edit suite',
  done: 'Done'
} as const;

export const BUILDER_LABELS = {
  generate: 'Generate Prompt'
} as const;

export const PANEL_LABELS = {
  validateAndReview: 'Validate and Review',
  addQuestions: (count: number | string, suiteName: string): string =>
    `Add ${count} ${count === 1 ? 'Question' : 'Questions'} to ${suiteName}`,
  createSuite: (suiteName: string): string => `Create Suite ${suiteName}`
} as const;

export type WorkflowRoute = 'add-to-suite' | 'create-suite';

export interface WorkflowDetails {
  /** Route A: the chosen suite. Route B: the suite name asked for, when there is one. */
  suiteName?: string | null;
  /** The suite YAML the wizard downloads, or the snapshot file the admin picked. */
  sourceFileName?: string | null;
  sourcePath?: string | null;
  /** The file the agent writes. */
  outputFileName?: string | null;
  questionCount?: number | null;
}

const code = (value: string) => '`' + value + '`';
const bold = (value: string) => `**${value}**`;

function numbered(lines: string[]): string {
  return lines.map((l, i) => `${i + 1}. [ ] ${l}`).join('\n');
}

function routeSteps(route: WorkflowRoute, d: WorkflowDetails): string[] {
  const suite = d.suiteName?.trim() || (route === 'add-to-suite' ? '<suite>' : '<name>');
  const count: number | string = d.questionCount ?? 'N';
  const agentFile = code(d.outputFileName || (route === 'add-to-suite' ? 'agent-new-questions-<slug>.yaml' : 'agent-new-suite-<slug>.yaml'));
  const path = d.sourcePath?.trim() ? ` (${code(d.sourcePath.trim())})` : '';
  const agent = `Paste the prompt into an agent session opened on the MobileGnollHackLogger repository, and approve the count table it shows.`;

  if (route === 'add-to-suite') {
    const downloaded = code(d.sourceFileName || 'overseer-suite-export-<slug>.yaml');
    return [
      `Open the ${bold(WIZARD_LABELS.title)} on the Manage Suites toolbar.`,
      `Choose ${bold(WIZARD_LABELS.routeSuite)}, pick the suite ${bold(suite)}, and press ${bold(WIZARD_LABELS.next)}.`,
      `Press ${bold(WIZARD_LABELS.downloadSuite)} and note where the browser saved ${downloaded} — this is the file the agent reads.`,
      `Paste its full path into ${bold(WIZARD_LABELS.pathSuite)}${path} and press ${bold(WIZARD_LABELS.next)}.`,
      `Press ${bold(BUILDER_LABELS.generate)} and copy the prompt.`,
      agent,
      `Wait for the agent to write ${agentFile} beside the suite file.`,
      `Back in the wizard, upload ${agentFile} — the one starting ${code('agent-new-')}, not the ${code('overseer-suite-export-')} file you downloaded — and press ${bold(PANEL_LABELS.validateAndReview)}.`,
      'Read the checks, look through the questions, and tick the confirmation.',
      `Press ${bold(PANEL_LABELS.addQuestions(count, suite))}.`,
      `Press ${bold(WIZARD_LABELS.assess)}; the suite cannot run until every question is assessed.`,
      `Optionally paste the description the agent suggested with ${bold(WIZARD_LABELS.editSuite)}, and run Suite Health *Snapshot facts*.`
    ];
  }

  const snapshot = d.sourceFileName ? code(d.sourceFileName) : 'the `.ai.html` or `.snapshot.txt` file';
  return [
    'In GnollHack, use *Developer → Export AI Snapshot* and save the file outside every repository.',
    `Open the ${bold(WIZARD_LABELS.title)} on the Manage Suites toolbar.`,
    `Choose ${bold(WIZARD_LABELS.routeFile)} and press ${bold(WIZARD_LABELS.next)}.`,
    `Optionally ${bold(WIZARD_LABELS.checkFile)} to confirm ${snapshot} is a snapshot and fits the size cap.`,
    `Paste the snapshot's full path into ${bold(WIZARD_LABELS.pathFile)}${path} and press ${bold(WIZARD_LABELS.next)}.`,
    `Press ${bold(BUILDER_LABELS.generate)} and copy the prompt.`,
    agent,
    `Wait for the agent to write ${agentFile} beside the snapshot.`,
    `Back in the wizard, upload ${agentFile} and press ${bold(PANEL_LABELS.validateAndReview)}.`,
    'Read the checks, look through the questions, and tick the confirmation.',
    `Press ${bold(PANEL_LABELS.createSuite(suite))}.`,
    `Press ${bold(WIZARD_LABELS.assess)} for the new suite; it cannot run until every question is assessed.`,
    'Optionally run Suite Health *Snapshot facts* and the citation check.'
  ];
}

/** The checklist as Markdown with LF line endings; `both` lists the two routes one after the other. */
export function buildSuiteWorkflowInstructions(route: WorkflowRoute | 'both', details: WorkflowDetails = {}): string {
  const header = '# From a game snapshot to a benchmark suite\n\n'
    + 'A browser cannot see where a file is on disk, so wherever a path is asked for, paste the full path — File Explorer\'s *Copy as path* works, with or without the quotes.';
  const sectionA = '## A — The snapshot is attached to a suite in Overseer\n\n'
    + 'The agent adds questions to that suite; its name, description and snapshot are not changed.\n\n'
    + numbered(routeSteps('add-to-suite', details));
  const sectionB = '## B — A snapshot file from GnollHack\n\n'
    + 'The agent writes a new suite, and the import creates it with the snapshot attached.\n\n'
    + numbered(routeSteps('create-suite', details));

  const sections = route === 'both' ? [sectionA, sectionB] : route === 'add-to-suite' ? [sectionA] : [sectionB];
  return [header, ...sections].join('\n\n') + '\n';
}
