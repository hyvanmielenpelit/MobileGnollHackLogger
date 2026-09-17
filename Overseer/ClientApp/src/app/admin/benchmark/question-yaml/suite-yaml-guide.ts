import {
  EXAMPLE_HEADER,
  GuideTab,
  MAX_GNOLLHACK_VERSION_LENGTH,
  MAX_SNAPSHOT_NAME_LENGTH,
  MAX_SUITE_NAME_LENGTH,
  QUESTION_YAML_FORMAT,
  QUESTION_YAML_VERSION,
  YamlExample
} from './question-yaml-format';

/**
 * The content of the Manage Suites help dialog: the whole-suite YAML workflow, the format with its
 * `suite.snapshot` mapping, authoring a suite from an exported AI snapshot, and ready-to-edit
 * examples. The agent prompt is assembled in `suite-agent-prompt.ts` and built in the Snapshot Suite
 * Wizard; the *AI Prompt* tab points there.
 *
 * The questions help (`question-yaml-format.ts`) owns the per-question rules; this file states only
 * what is specific to a whole suite. Everything here is static: the suite variant of the help
 * dialog makes no server call.
 */

const WORKFLOW_MARKDOWN = `## Download, edit, import

1. **Download** a suite with the two icon buttons on its card: **Download Suite as YAML** and **Copy Suite as YAML to Clipboard**. Both are inert only while the suite has neither questions nor a game snapshot. A snapshot suite with no questions yet downloads with \`questions: []\`: that file is for an agent to write questions against, and does not import as it is.
2. The file holds the suite's name and description, the \`snapshot\` mapping for a snapshot suite, and every question with its \`id\`, \`difficulty\`, \`question\` and \`rubric\`. It is named \`benchmark-suite-<slug>.yaml\`.
3. **Import Suite from YAML** on this toolbar takes it back: **Validate** checks the whole document, **Review changes** shows the new suite, what happens to the game snapshot and every question, and **Create suite** writes all of it at once.

## What a suite import does

| Rule | What it means |
|---|---|
| **Always creates a new suite** | Even when a suite of that name exists; the new one is then named *Name (Imported)*, then *Name (Imported 2)* and so on. |
| **Ignores question ids** | Every question in the file is created as a new question of the new suite. |
| **Attaches the game snapshot in the file** | An identical snapshot that is already stored and belongs to **no** suite is reused, with its own name and notes. Otherwise a snapshot is created — a **copy**, when the identical one belongs to another suite, because a snapshot belongs to one suite. A taken snapshot name gets a *(2)* suffix. The review step says which of these will happen before anything is written, and unticking the box imports the suite without a snapshot. |
| **Creates hand-written questions** | Imported questions are not generated, so they carry no *Needs review* badge and *Verify All* does not apply to them. |
| **Respects the question cap** | At most 50 questions per suite by default. |

## Before the suite can run

Run **Assess Difficulty** on the new suite's card. The launcher refuses a suite until every question has an AI-assessed difficulty, and an import never carries one over.

## Typical uses

- Move a suite **and its board** between servers as one file.
- Keep a suite in version control, and review its changes as a diff.
- Author a whole suite with an AI — see the *From a Snapshot* tab.
`;

const FORMAT_MARKDOWN = `## The file at a glance

One complete document: the header, a \`suite\` block carrying its board, and one question. Copy it into **Import Suite from YAML** and it validates.

\`\`\`yaml
format: ${QUESTION_YAML_FORMAT}
version: ${QUESTION_YAML_VERSION}

suite:
  name: "Valkyrie at Dlvl 11"
  description: |
    One decision on one board.
  snapshot:
    name: "Valkyrie dlvl 11"
    text: |
      GnollHack 4.2.0 Build 47
      Dlvl:11 $:842 HP:14(58) Pw:22(22) AC:2 Xp:9/4210 T:3120 Hungry
      f - 2 uncursed potions of extra healing

questions:
  - difficulty: Simple
    question: |
      A jackal is next to me and I am low on health. What should I do this turn?
    rubric: |
      **BOARD FACTS**
      - The status line reads "HP:14(58)".

      **REQUIRED**
      - Recommends drinking a potion of extra healing, or retreating, rather than trading blows.

      **SOURCE** — board; C source: src/potion.c (extra healing)
\`\`\`

| Top-level key | Required | Holds |
|---|---|---|
| \`format\` | **yes** | Always \`${QUESTION_YAML_FORMAT}\`. |
| \`version\` | **yes** | Always \`${QUESTION_YAML_VERSION}\`. |
| \`suite\` | **yes** for a suite import | The suite's name, description and game snapshot. |
| \`questions\` | **yes** | A non-empty list of questions. |

Any key that is not listed on this tab is an error.

## The header

\`\`\`text
format: ${QUESTION_YAML_FORMAT}
version: ${QUESTION_YAML_VERSION}
\`\`\`

Both lines are required, exactly as written. A different \`format\` or a different \`version\` is refused before the rest of the document is read.

## The \`suite\` block

| Key | Required | Notes |
|---|---|---|
| \`name\` | **yes** | 1–${MAX_SUITE_NAME_LENGTH} characters. A suite import always creates a new suite, so a taken name gets an *(Imported)* suffix rather than an error. |
| \`description\` | no | Markdown. A block scalar keeps its line breaks. |
| \`snapshot\` | no | A **mapping**, not a name. A document without it imports a suite with no board. |

\`\`\`yaml
suite:
  name: "Hunger and Food"
  description: |
    Questions on hunger states and food safety. No board is needed
    to answer them, so this suite carries no snapshot.
\`\`\`

## The \`snapshot\` mapping

| Key | Required | Notes |
|---|---|---|
| \`name\` | no | 1–${MAX_SNAPSHOT_NAME_LENGTH} characters. Left out, the snapshot is named after the suite. |
| \`gnollhack_version\` | no | At most ${MAX_GNOLLHACK_VERSION_LENGTH} characters: the version identifier from the banner line, not the whole banner. |
| \`captured_at\` | no | A date, for example \`"2026-09-16T18:04:11Z"\`. |
| \`notes\` | no | One or two lines about where the board came from. |
| \`sha256\` | no | 64 hexadecimal characters: the hash of the board this file was exported from. Every export writes it. A mismatch is only a **warning** on the review step — editing the board in the file is legitimate — and an agent-authored file simply leaves it out. |
| \`text\` | **yes** | The board itself: flattened snapshot text, or a raw HTML dump, which the server flattens. It is cut at 60,000 characters, and the review step says so when it is. |

These are the four keys an agent writes:

\`\`\`yaml
  snapshot:
    name: "Valkyrie dlvl 11"
    gnollhack_version: "4.2.0 Build 47"
    notes: |
      Exported with Export AI Snapshot; suite authored by an agent.
    text: |
      GnollHack 4.2.0 Build 47
      Dlvl:11 $:842 HP:14(58) Pw:22(22) AC:2 Xp:9/4210 T:3120 Hungry
\`\`\`

It leaves out the two an export alone can know: \`sha256\`, the hash of the board the file came from, and \`captured_at\`, the moment the game wrote it. Both are optional, and a hash that no longer matches is a warning, never a refusal.

## Questions

| Key | Required | Notes |
|---|---|---|
| \`id\` | no | Ignored by a suite import: every question in the file is created as new. |
| \`difficulty\` | no | **Simple**, **Intermediate** or **Advanced**. Absent, the question becomes Simple, silently. |
| \`question\` | **yes** | The question text, phrased as a player would ask it. |
| \`rubric\` | no | Allowed to be absent, but the assessor has nothing to charge without one. |

\`\`\`yaml
questions:
  - difficulty: Intermediate
    question: |
      I am Fainting and my prayer timeout is unknown. Is praying worth the risk?
    rubric: |
      **REQUIRED**
      - Fainting is a major trouble, so a successful prayer fixes it.

      **CRITICAL ERROR**
      - Claims prayer is always safe regardless of timeout.

      **SOURCE** — C source: src/pray.c (prayer troubles and timeout)
\`\`\`

Any other question key is an error.

### Difficulty: authored band and AI-assessed score

| Band (\`difficulty\`) | AI-assessed range |
|---|---|
| Simple | 1–35 |
| Intermediate | 36–70 |
| Advanced | 71–100 |

- The key in the file is the **authored** band only.
- The **AI-assessed** score is what weights the Intelligence Index, and the assessor is never shown the authored band.
- The launcher refuses the suite until **Assess Difficulty** has rated every question.

## Block scalars and indentation

- Write \`description\`, \`notes\`, \`question\`, \`rubric\` and \`text\` as block scalars — the \`key:\` line followed by a bar.
- **Indent every line of a block by the same number of spaces.** A map row's own leading spaces come *after* that indent and are kept, which is what keeps the column ruler aligned.
- Indent with spaces, never tabs.
- Inside a block anything goes: Markdown headings, code fences, \`---\` lines, and a \`#\` line, which is text there and not a comment.

**Right** — every line of the block sits at the same six spaces, and the map's own spaces survive on top of them:

\`\`\`yaml
    text: |
      GnollHack 4.2.0 Build 47

      Map grid:
           0         1
           0123456789012345
        8  |..........@...|
\`\`\`

**Wrong** — the third line is indented less than the first, which ends the block; everything after it is read as YAML and fails:

\`\`\`yaml
    text: |
      GnollHack 4.2.0 Build 47
    Map grid:
\`\`\`

## Limits

| Limit | Value |
|---|---|
| Uploaded file | 2 MB |
| Questions per suite | 50 by default |
| \`suite.name\` | ${MAX_SUITE_NAME_LENGTH} characters |
| \`snapshot.name\` | ${MAX_SNAPSHOT_NAME_LENGTH} characters |
| \`gnollhack_version\` | ${MAX_GNOLLHACK_VERSION_LENGTH} characters |
| Board text | cut at 60,000 characters, reported on the review step |

## Reading a validation message

| Kind | Looks like | What to do |
|---|---|---|
| Syntax | *Line 14, column 7: bad indentation of a mapping entry* | Fix the YAML at that position; it is almost always indentation. |
| Schema, document level | *\`suite.snapshot.text\` is required.* | Add or correct the named key. |
| Schema, one question | *questions[3] (id 42): unknown key \`tier\`* | The index counts from 0; remove or rename the key. |

Fix every message: the import runs only when the whole document is valid.
`;

const SNAPSHOT_MARKDOWN = `## Use the Snapshot Suite Wizard

The **Snapshot Suite Wizard** on this toolbar walks through every step below, generates the agent prompt, and imports the agent's file itself — checking that it does what the route is for before anything is written. It remembers where you are, so you can close it while the agent works.

## Two routes, by where the board is

| The game snapshot is… | The agent… | The wizard's import… |
|---|---|---|
| **Attached to a suite in Overseer** | reads the suite YAML the wizard downloads, and writes \`benchmark-questions-<slug>.yaml\` | **adds** the new questions to that suite; its name, description, snapshot and existing questions stay as they are |
| **A snapshot file from GnollHack** | reads the \`.ai.html\` or \`.snapshot.txt\`, and writes \`benchmark-suite-<slug>.yaml\` | **creates** a new suite with the snapshot attached |

In both routes:

1. **Get the file** the agent reads — Download Suite YAML in the wizard, or *Developer → Export AI Snapshot* in GnollHack — and keep it outside every repository.
2. **Paste its full path** into the wizard. A browser cannot see where a file is on disk.
3. **In an agent session** opened on the MobileGnollHackLogger repository — Claude Code, Antigravity or similar — paste the prompt the wizard generates. Overseer does not need to be running.
4. The agent **shows its count table**, then writes **one file** beside yours.
5. **Back in the wizard**: upload that file, read the checks, confirm, and import. Then **Assess Difficulty**. Optionally run Suite Health *Snapshot facts* and the citation check.

The wizard refuses a file that would replace an existing question, and a new suite without a board. Both remain possible outside it: **Import Questions from YAML** in Manage Questions, and **Import Suite from YAML** on this toolbar.

## How many questions, and how many per band

1. **Survey first.** List the board's distinct *decisions*: an immediate threat, an HP / hunger / status problem, an escape or healing item, a pet, a spell or skill choice, an unidentified item risk, a route or branch choice, a mechanic the board makes relevant. One candidate question per decision.
2. **Classify** each candidate into a band:

| Band | What a question in it tests |
|---|---|
| **Simple** | Immediate tactical survival, direct threats, obvious escape items, standard inventory assessment — all directly visible on the board. |
| **Intermediate** | Multi-turn planning, risk and reward, resource combinations, companion handling, prayer safety, route and branch choices, identification risk. |
| **Advanced** | Obscure engine interactions, damage or survival calculations, GnollHack versus NetHack divergences, deep inventory and spell synergy, edge-case escapes — each needing a mechanics point that can be cited to a source file. |

3. **Total** = the candidates that survive. **Never pad**: every question costs a candidate call plus one or two assessor calls on *every* run.
4. **Split** toward equal thirds by trimming the largest band, never by promoting a question into a band it does not belong in. Where the board cannot fill a band, keep what is real and say so.
5. The agent **reports the table before writing the YAML**. Counts you supply win.

| Setting | Value |
|---|---|
| Default target | 18 |
| Sensible range | 12–24 |
| Cap | 50 |
| Preferred split | 6 / 6 / 6 |
| Minimum per band | 4, where the board supports it |
`;

/** The suite help's guide tabs; the Examples and AI Prompt tabs follow them in the dialog. */
export const SUITE_GUIDE_TABS: ReadonlyArray<GuideTab> = [
  {
    id: 'workflow',
    label: 'Workflow',
    ingress: 'How a whole suite — its questions and its game board — leaves Overseer as one YAML file and comes back as a new suite.',
    markdown: WORKFLOW_MARKDOWN
  },
  {
    id: 'format',
    label: 'Format',
    ingress: 'Every key a suite file may contain, with a snippet for each part, the limits the importer enforces, and how to read its messages.',
    markdown: FORMAT_MARKDOWN
  },
  {
    id: 'snapshot',
    label: 'From a Snapshot',
    ingress: 'Turning a game snapshot into finished questions, with an AI agent doing the writing and the Snapshot Suite Wizard doing the rest.',
    markdown: SNAPSHOT_MARKDOWN,
    action: 'wizard'
  }
];

/** Shown above the example accordion on the suite help's Examples tab. */
export const SUITE_EXAMPLES_INTRO_MARKDOWN = `Copy or download an example, put your own text in it, and import it with **Import Suite from YAML** on this toolbar. Every one of these creates a *new* suite, so none of them can overwrite anything. Everything in these files is what the import expects, so edit the text and keep the shape.`;

/** Shown above the example accordion on the suite help's Examples tab. Plain text. */
export const SUITE_EXAMPLES_INGRESS = 'Four complete suite files to copy, edit and import.';

/** Shown above the wizard button and the checklist on the suite help's AI Prompt tab. Plain text. */
export const SUITE_AI_INGRESS = 'The Snapshot Suite Wizard builds the prompt for Claude Code or Antigravity and imports what the agent writes. The same steps are below as a checklist to keep beside you.';

const EXAMPLE_BOARD = `      GnollHack 4.2.0 Build 47
      Game began 2026-09-16 09:12, snapshot at turn 3120.
      Valkyrie the Fighter, human female neutral

      Map grid:
           0         1
           0123456789012345
        8  |..........@...|
        9  |....d.........|
       10  |......<.......|
       11  |--------------|

      Status:
      Dlvl:11 $:842 HP:14(58) Pw:22(22) AC:2 Xp:9/4210 T:3120 Hungry

      Inventory:
      a - an uncursed +1 long sword (weapon in hand)
      f - 2 uncursed potions of extra healing`;

/** Ready-to-edit suite documents, one per situation. Each one must parse and validate in suite mode. */
export const SUITE_YAML_EXAMPLES: ReadonlyArray<YamlExample> = [
  {
    id: 'suite-minimal',
    title: 'The smallest suite that imports',
    mode: 'suite',
    intro: 'A name and one question, nothing else. The question names no `difficulty`, so it becomes **Simple**; it has no rubric, so the assessor has nothing to charge until you add one.',
    yaml: EXAMPLE_HEADER + `
suite:
  name: "Hunger and Food"

questions:
  - question: |
      What does the Weak hunger state do to a character?
`
  },
  {
    id: 'suite-three-bands',
    title: 'A suite with one question per band',
    mode: 'suite',
    intro: 'A description and three questions, one in each band, with rubrics in the house format: **REQUIRED**, **CRITICAL ERROR**, **SCOPE**, the FORM label written exactly as shown, and **SOURCE** with correctly shaped citations.',
    yaml: EXAMPLE_HEADER + `
suite:
  name: "Hunger, Food and Prayer"
  description: |
    Three questions on hunger states, food safety and prayer timing.

questions:
  - difficulty: Simple
    question: |
      My character is Weak from hunger. What should I eat first, and what should I avoid?
    rubric: |
      **REQUIRED**
      - Eat a safe, filling food item from the inventory first.
      - Avoid cursed or rotten food while Weak.

      **CRITICAL ERROR**
      - Claims that eating a cockatrice corpse is safe.

      **SCOPE**
      - The next few turns; long-term food planning is not required.

      **FORM** (not graded — presentation note only)
      - The food to eat first, then what to avoid.

      **SOURCE** — C source: src/eat.c (hunger states)

  - difficulty: Intermediate
    question: |
      I am Fainting and my prayer timeout is unknown. Is praying worth the risk, or should I keep looking for food?
    rubric: |
      **REQUIRED**
      - Fainting is a major trouble, so a successful prayer fixes it.
      - Weighs the unknown timeout against the immediate risk of fainting in combat.

      **CRITICAL ERROR**
      - Claims prayer is always safe regardless of timeout.

      **SCOPE**
      - This decision only; general prayer strategy is not required.

      **FORM** (not graded — presentation note only)
      - A recommendation first, then the reasoning.

      **SOURCE** — C source: src/pray.c (prayer troubles and timeout)

  - difficulty: Advanced
    question: |
      Which source file implements the hunger state transitions, and what triggers the move from Weak to Fainting?
    rubric: |
      **REQUIRED**
      - Names the correct source file.
      - Describes the nutrition threshold that triggers the transition.

      **SCOPE**
      - The engine mechanic; play advice is not required.

      **SOURCE** — C source: src/eat.c
`
  },
  {
    id: 'suite-snapshot',
    title: 'A snapshot suite, as an agent writes it',
    mode: 'suite',
    intro: 'One file that carries the whole suite **and its board**: the `snapshot` mapping holds the flattened game snapshot, and each rubric\'s **BOARD FACTS** quote that text word for word. This is the shape the *From a Snapshot* workflow produces. Importing it creates the suite and attaches the snapshot.',
    yaml: EXAMPLE_HEADER + `
suite:
  name: "Valkyrie at Dlvl 11"
  description: |
    Two decisions on one board: an immediate threat while Hungry, and the
    resources available to answer it.
  snapshot:
    name: "Valkyrie dlvl 11"
    gnollhack_version: "4.2.0 Build 47"
    notes: |
      Exported with Export AI Snapshot; suite authored by an agent.
    text: |
${EXAMPLE_BOARD}

questions:
  - difficulty: Simple
    question: |
      A jackal is one square below and to the left of me and I am low on health. What should I do this turn?
    rubric: |
      **BOARD FACTS**
      - The status line reads "HP:14(58)", so under a quarter of the character's health is left.
      - The status line ends with "Hungry".
      - The inventory holds "f - 2 uncursed potions of extra healing".

      **REQUIRED**
      - Recognises that 14 of 58 hit points leaves no room for a bad exchange.
      - Recommends drinking a potion of extra healing, or retreating, rather than trading blows.

      **CRITICAL ERROR**
      - Claims the character is in no danger at this health.

      **SCOPE**
      - This turn only.

      **FORM** (not graded — presentation note only)
      - The recommended action first, then why.

      **SOURCE** — board; C source: src/potion.c (extra healing)

  - difficulty: Intermediate
    question: |
      I want to leave this level rather than fight. What does the board tell me about my options, and what should I carry out first?
    rubric: |
      **BOARD FACTS**
      - The map grid shows an up staircase "<" two rows below the character.
      - The character wields "a - an uncursed +1 long sword (weapon in hand)".
      - Power stands at "Pw:22(22)".

      **REQUIRED**
      - Identifies the up staircase on the map as the escape route.
      - Notes that reaching it means moving past the jackal, or dealing with it first.

      **CRITICAL ERROR**
      - Claims there is no way off the level.

      **SCOPE**
      - Leaving this level; the route beyond it is not required.

      **FORM** (not graded — presentation note only)
      - The route first, then the risk on the way.

      **SOURCE** — board; C source: src/do.c (staircase travel)
`
  },
  {
    id: 'suite-exported',
    title: 'A downloaded suite, exactly as it comes',
    mode: 'suite',
    intro: 'What **Download Suite as YAML** writes for a snapshot suite: the leading comments, the question ids, and all six snapshot keys including `sha256`. The ids are ignored on a suite import, and the review step reports whether this snapshot is created, or an identical stored one is reused.',
    yaml: `# Overseer benchmark questions. Edit freely; keep every \`id\` you were given.
# A question without \`id\` is created as new. Omit \`rubric\` to keep the current rubric.
# suite.snapshot is the board the questions are written against. A suite import attaches it; a questions import ignores it.
format: ${QUESTION_YAML_FORMAT}
version: ${QUESTION_YAML_VERSION}

suite:
  name: "Valkyrie at Dlvl 11"
  description: |
    Two decisions on one board.
  snapshot:
    name: "Valkyrie dlvl 11"
    gnollhack_version: "4.2.0 Build 47"
    captured_at: "2026-09-16T18:04:11Z"
    notes: |
      Exported with Export AI Snapshot.
    sha256: "9f2ca4d1b8e73c05a6f419d2be80c37514a9d6e2f0b3c81746d9a2e5c0f83b71"
    text: |
${EXAMPLE_BOARD}

questions:
  # Question 1
  - id: 101
    difficulty: Simple
    question: |
      A jackal is one square below and to the left of me and I am low on health. What should I do this turn?
    rubric: |
      **BOARD FACTS**
      - The status line reads "HP:14(58)".
      - The status line ends with "Hungry".

      **REQUIRED**
      - Recommends healing or retreating rather than trading blows.

      **SOURCE** — board; C source: src/potion.c (extra healing)

  # Question 2
  - id: 102
    difficulty: Intermediate
    question: |
      I want to leave this level rather than fight. What does the board tell me about my options?
    rubric: |
      **BOARD FACTS**
      - The map grid shows an up staircase "<" two rows below the character.

      **REQUIRED**
      - Identifies the up staircase on the map as the escape route.

      **SOURCE** — board; C source: src/do.c (staircase travel)
`
  }
];

export const SUITE_AI_PROMPT_FILE_NAME = 'overseer-suite-from-snapshot-prompt.md';
