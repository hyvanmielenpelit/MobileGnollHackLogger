import {
  QUESTION_YAML_FORMAT,
  lintRubric,
  parseQuestionYaml,
  toSuiteSnapshot,
  validateForMode
} from './question-yaml-format';
import {
  SUITE_AI_INGRESS,
  SUITE_AI_PROMPT_FILE_NAME,
  SUITE_EXAMPLES_INGRESS,
  SUITE_EXAMPLES_INTRO_MARKDOWN,
  SUITE_GUIDE_TABS,
  SUITE_YAML_EXAMPLES
} from './suite-yaml-guide';

/** The quoted fragments of every `**BOARD FACTS**` bullet, which must occur in the board verbatim. */
function boardFactQuotes(rubric: string): string[] {
  const afterHeading = rubric.split('**BOARD FACTS**')[1] ?? '';
  const section = afterHeading.split(/^\*\*/m)[0];
  return Array.from(section.matchAll(/"([^"]+)"/g)).map(m => m[1]);
}

/** The body of every fenced ```yaml block of a Markdown document. */
function yamlBlocks(markdown: string): string[] {
  return Array.from(markdown.matchAll(/^```yaml\n([\s\S]*?)^```$/gm)).map(m => m[1]);
}

describe('suite-yaml-guide', () => {
  it('gives every guide tab a label, an ingress and text, and opens on Workflow', () => {
    expect(SUITE_GUIDE_TABS.map(t => t.id)).toEqual(['workflow', 'format', 'snapshot']);
    expect(SUITE_GUIDE_TABS.map(t => t.label)).toEqual(['Workflow', 'Format', 'From a Snapshot']);
    for (const tab of SUITE_GUIDE_TABS) {
      expect(tab.markdown.trim()).withContext(tab.id).not.toBe('');
      expect(tab.ingress.trim()).withContext(tab.id).not.toBe('');
    }
    expect(SUITE_GUIDE_TABS.filter(t => t.action === 'wizard').map(t => t.id)).toEqual(['snapshot']);
    expect(SUITE_EXAMPLES_INTRO_MARKDOWN.trim()).not.toBe('');
    expect(SUITE_EXAMPLES_INGRESS.trim()).not.toBe('');
    expect(SUITE_AI_INGRESS.trim()).not.toBe('');
  });

  it('states the format rules the parser enforces on the Format tab', () => {
    const format = SUITE_GUIDE_TABS.find(t => t.id === 'format')!.markdown;
    expect(format).toContain(QUESTION_YAML_FORMAT);
    for (const key of ['name', 'gnollhack_version', 'captured_at', 'notes', 'sha256', 'text']) {
      expect(format).withContext(`snapshot key ${key}`).toContain(key);
    }
    expect(format).toContain('Simple');
    expect(format).toContain('Intermediate');
    expect(format).toContain('Advanced');
  });

  it('gives every example a unique, slug-shaped id', () => {
    const ids = SUITE_YAML_EXAMPLES.map(e => e.id);
    expect(new Set(ids).size).toBe(ids.length);
    for (const id of ids) {
      expect(id).toMatch(/^[a-z0-9-]+$/);
    }
    expect(SUITE_YAML_EXAMPLES.every(e => e.mode === 'suite')).toBeTrue();
  });

  for (const example of SUITE_YAML_EXAMPLES) {
    it(`"${example.title}" parses, validates as a suite, and lints clean`, async () => {
      const result = await parseQuestionYaml(example.yaml);
      expect(result.errors).toEqual([]);
      expect(validateForMode(result, 'suite', []).errors).toEqual([]);
      expect(result.questions.length).toBeGreaterThan(0);

      const hasSnapshot = !!result.suite?.snapshot;
      for (const q of result.questions) {
        expect(lintRubric(q.rubric ?? '', hasSnapshot)).toEqual([]);
      }
    });
  }

  it('carries a complete, quotable board in the agent-authored example', async () => {
    const example = SUITE_YAML_EXAMPLES.find(e => e.id === 'suite-snapshot')!;
    const result = await parseQuestionYaml(example.yaml);
    expect(result.errors).toEqual([]);

    const snapshot = toSuiteSnapshot(result);
    expect(snapshot).not.toBeNull();
    expect(snapshot!.text).toContain('GnollHack 4.2.0 Build 47');
    expect(snapshot!.sourceGnollHackVersion).toBe('4.2.0 Build 47');

    let quoteCount = 0;
    for (const q of result.questions) {
      expect(q.rubric).toContain('**BOARD FACTS**');
      for (const quote of boardFactQuotes(q.rubric!)) {
        quoteCount++;
        expect(snapshot!.text).withContext(`board fact quote ${JSON.stringify(quote)}`).toContain(quote);
      }
    }
    expect(quoteCount).toBeGreaterThan(3);
  });

  it('parses all six snapshot keys and reports the ignored ids in the exported example', async () => {
    const example = SUITE_YAML_EXAMPLES.find(e => e.id === 'suite-exported')!;
    const result = await parseQuestionYaml(example.yaml);
    expect(result.errors).toEqual([]);

    const snapshot = result.suite!.snapshot!;
    expect(snapshot.name).toBe('Valkyrie dlvl 11');
    expect(snapshot.gnollhackVersion).toBe('4.2.0 Build 47');
    expect(snapshot.capturedAt).toBe('2026-09-16T18:04:11.000Z');
    expect(snapshot.notes).toContain('Export AI Snapshot');
    expect(snapshot.sha256).toMatch(/^[0-9a-f]{64}$/);
    expect(snapshot.text).toContain('Map grid:');

    expect(validateForMode(result, 'suite', []).notices)
      .toContain('Question ids in the file are ignored: a suite import always creates new questions.');
  });

  it('names the file the prompt is downloaded as', () => {
    expect(SUITE_AI_PROMPT_FILE_NAME).toBe('overseer-suite-from-snapshot-prompt.md');
  });

  it('keeps every complete document on the Format tab importable as a suite', async () => {
    const blocks = yamlBlocks(SUITE_GUIDE_TABS.find(t => t.id === 'format')!.markdown);
    expect(blocks.length).toBeGreaterThan(3);

    const documents = blocks.filter(b => b.startsWith('format:'));
    expect(documents.length).toBeGreaterThan(0);
    for (const document of documents) {
      const result = await parseQuestionYaml(document);
      expect(result.errors).withContext(document).toEqual([]);
      expect(validateForMode(result, 'suite', []).errors).withContext(document).toEqual([]);
    }
  });

  it('names neither the skill file nor a slash command anywhere in the guide text', () => {
    const texts = [
      ...SUITE_GUIDE_TABS.map(t => t.markdown),
      ...SUITE_GUIDE_TABS.map(t => t.ingress),
      SUITE_EXAMPLES_INTRO_MARKDOWN,
      SUITE_EXAMPLES_INGRESS,
      SUITE_AI_INGRESS
    ];
    for (const text of texts) {
      expect(text).not.toContain('/server-snapshot-suite-authoring');
      expect(text).not.toContain('.agents/');
    }
  });
});
