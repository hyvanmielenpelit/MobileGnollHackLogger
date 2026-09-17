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
 * `suite.snapshot` mapping, authoring a suite from an exported AI snapshot, ready-to-edit examples,
 * and the prompt that invokes the offline authoring skill.
 *
 * The questions help (`question-yaml-format.ts`) owns the per-question rules; this file states only
 * what is specific to a whole suite. Everything here is static: the suite variant of the help
 * dialog makes no server call.
 */

const WORKFLOW_MARKDOWN = `## Download, edit, import

1. **Download** a suite with the two icon buttons on its card: **Download Suite as YAML** and **Copy Suite as YAML to Clipboard**. Both are inert while the suite has no questions.
2. The file holds the suite's name and description, the \`snapshot\` mapping for a snapshot suite, and every question with its \`id\`, \`difficulty\`, \`question\` and \`rubric\`. It is named \`benchmark-suite-<slug>.yaml\`.
3. **Import Suite from YAML** on this toolbar takes it back: **Validate** checks the whole document, **Review changes** shows the new suite, what happens to the game snapshot and every question, and **Create suite** writes all of it at once.

## What a suite import does

| | |
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

const FORMAT_MARKDOWN = `## Header

The document starts with \`format: ${QUESTION_YAML_FORMAT}\` and \`version: ${QUESTION_YAML_VERSION}\`. Both are required.

## The \`suite\` block

- \`name\` — required for a suite import, 1–${MAX_SUITE_NAME_LENGTH} characters.
- \`description\` — optional Markdown, best as a \`|\` block scalar.
- \`snapshot\` — optional; a **mapping**, not a name. A document without it imports a suite with no board.

## The \`snapshot\` mapping

| Key | Required | Notes |
|---|---|---|
| \`name\` | no | 1–${MAX_SNAPSHOT_NAME_LENGTH} characters. Left out, the snapshot is named after the suite. |
| \`gnollhack_version\` | no | At most ${MAX_GNOLLHACK_VERSION_LENGTH} characters: the version identifier from the banner line, not the whole banner. |
| \`captured_at\` | no | A date, for example \`"2026-09-16T18:04:11Z"\`. |
| \`notes\` | no | One or two lines about where the board came from. |
| \`sha256\` | no | 64 hexadecimal characters: the hash of the board this file was exported from. Every export writes it. A mismatch is only a **warning** on the review step — editing the board in the file is legitimate — and an agent-authored file simply leaves it out. |
| \`text\` | **yes** | The board itself: flattened snapshot text, or a raw HTML dump, which the server flattens. It is cut at 60,000 characters, and the review step says so when it is. |

## Questions

Question keys are \`id\`, \`difficulty\`, \`question\` and \`rubric\`; any other key is an error. A suite import ignores \`id\`.

\`difficulty\` is **Simple**, **Intermediate** or **Advanced**. Two things to know about it:

- A question with **no** \`difficulty\` key becomes **Simple**, silently.
- This is the **authored** band only. The **AI-assessed** difficulty — 1–100, with Simple 1–35, Intermediate 36–70 and Advanced 71–100 — is what weights the Intelligence Index, the assessor is never shown the authored band, and the launcher refuses to run the suite until **Assess Difficulty** has rated every question.

## Block scalars and indentation

- Write \`description\`, \`notes\`, \`question\`, \`rubric\` and \`text\` as \`|\` block scalars.
- **Indent every line of a block by the same number of spaces.** A map row's own leading spaces come *after* that indent and are kept, which is what keeps the column ruler aligned.
- Indent with spaces, never tabs. Inside a block anything goes: Markdown headings, code fences, \`---\` lines, and a \`#\` line, which is text there and not a comment.
- An uploaded file may be at most 2 MB.

## Reading a validation message

A syntax error is reported as *Line N, column M: reason*. A schema error names its place, as in *\`suite.snapshot.text\` is required.* or *questions[3] (id 42): unknown key \`tier\`*. Fix every message: the import runs only when the whole document is valid.
`;

const SNAPSHOT_MARKDOWN = `## From a game snapshot to a suite, in four steps

1. **In GnollHack**: game menu → **Developer** → **Export AI Snapshot**, and save the \`.ai.html\` file somewhere outside any repository. A \`.snapshot.txt\` downloaded from the Snapshot Viewer works just as well.
2. **In an agent session** that can read the repositories on disk — Claude Code, Antigravity or similar — paste the prompt below with the snapshot's path filled in. Overseer does not need to be running.
3. The agent **shows its count table**, then writes **one file**, \`benchmark-suite-<slug>.yaml\`, beside the snapshot.
4. **Back here**: **Import Suite from YAML** → review → **Create suite** → **Assess Difficulty** on the new card. Optionally run Suite Health *Snapshot facts* and the citation check.

Two ways to invoke the skill:

- **Claude Code**: paste the prompt, or type \`/server-snapshot-suite-authoring <path to the snapshot>\`.
- **Antigravity and others**: paste the prompt; it names the skill file to read.

## How many questions, and how many per band

1. **Survey first.** List the board's distinct *decisions*: an immediate threat, an HP / hunger / status problem, an escape or healing item, a pet, a spell or skill choice, an unidentified item risk, a route or branch choice, a mechanic the board makes relevant. One candidate question per decision.
2. **Classify** each candidate into a band:

| Band | What a question in it tests |
|---|---|
| **Simple** | Immediate tactical survival, direct threats, obvious escape items, standard inventory assessment — all directly visible on the board. |
| **Intermediate** | Multi-turn planning, risk and reward, resource combinations, companion handling, prayer safety, route and branch choices, identification risk. |
| **Advanced** | Obscure engine interactions, damage or survival calculations, GnollHack versus NetHack divergences, deep inventory and spell synergy, edge-case escapes — each needing a mechanics point that can be cited to a source file. |

3. **Total** = the candidates that survive, capped at **50**. The default target is **18**, and **12–24** is the sensible range. **Never pad**: every question costs a candidate call plus one or two assessor calls on *every* run.
4. **Split** toward equal thirds — **6 / 6 / 6** — by trimming the largest band, never by promoting a question into a band it does not belong in. Keep at least **4** per band where the board supports it; otherwise keep what is real and say so.
5. The agent **reports the table before writing the YAML**. Counts you supply win.

The prompt to paste is below and on the *For an AI* tab.
`;

/** The suite help's guide tabs; the Examples and For an AI tabs follow them in the dialog. */
export const SUITE_GUIDE_TABS: ReadonlyArray<GuideTab> = [
  { id: 'workflow', label: 'Workflow', markdown: WORKFLOW_MARKDOWN },
  { id: 'format', label: 'Format', markdown: FORMAT_MARKDOWN },
  { id: 'snapshot', label: 'From a Snapshot', markdown: SNAPSHOT_MARKDOWN }
];

/** Shown above the example accordion on the suite help's Examples tab. */
export const SUITE_EXAMPLES_INTRO_MARKDOWN = `Copy or download an example, put your own text in it, and import it with **Import Suite from YAML** on this toolbar. Every one of these creates a *new* suite, so none of them can overwrite anything. Everything in these files is what the import expects, so edit the text and keep the shape.`;

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

/**
 * The prompt an admin copies into an agent session. It names the skill by its invocable name and
 * by its path, so a harness that indexes skill descriptions and one that does not both reach it;
 * renaming either must change this text and `.agents/skills/server_snapshot_suite_authoring/`.
 */
export const SUITE_AI_PROMPT = `Use the \`server-snapshot-suite-authoring\` skill of the MobileGnollHackLogger repository
(\`.agents/skills/server_snapshot_suite_authoring/SKILL.md\` — read it in full if your harness has
not loaded it) to create an Overseer benchmark suite YAML from this GnollHack AI snapshot.

Snapshot file: <absolute path to the .ai.html or .snapshot.txt>
Suite name: <optional — leave blank and propose one>
Question counts: <optional — leave blank and propose them, e.g. 6 Simple / 6 Intermediate / 6 Advanced>

Overseer is not running and you do not need it. Show me the count table before you write the
questions. Write one file, \`benchmark-suite-<slug>.yaml\`, beside the snapshot and never inside a
repository. If you cannot find the skill, stop and tell me; do not improvise the format.
`;
