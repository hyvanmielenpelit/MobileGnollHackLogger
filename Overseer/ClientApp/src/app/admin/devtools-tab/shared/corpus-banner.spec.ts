import { buildCorpusIndexInfo, buildCorpusJob, buildCorpusStatus } from '../../admin.component.testing';
import { formatTime } from './devtools-format';
import { CORPUS_STAGES, corpusActionName, corpusBanner, corpusStageName, sortCorpusJobs } from './corpus-banner';

const nf = new Intl.NumberFormat();
const index = buildCorpusIndexInfo();

describe('corpusBanner', () => {
  it('says the corpus is not configured when the key is unset and there is no fallback path', () => {
    const banner = corpusBanner(buildCorpusStatus('gnollhack-wiki', { configured: false, path: null, pathExists: false }), 'GnollHack wiki');
    expect(banner).toEqual({
      status: 'warning', word: 'Not configured',
      sentence: 'WikiPath is not set, so the GnollHack wiki tools have nothing to search.'
    });
  });

  it('speaks of one tool for the knowledge base', () => {
    const banner = corpusBanner(buildCorpusStatus('knowledge-base', { configured: false, path: null, pathExists: false }), 'knowledge base');
    expect(banner.sentence).toBe('KbPath is not set, so the knowledge base tool has nothing to search.');
  });

  it('names the built-in default when the key is unset but a fallback path is read', () => {
    const banner = corpusBanner(buildCorpusStatus('gnollhack-wiki', { configured: false, path: 'c:\\wiki', index }), 'GnollHack wiki');
    expect(banner).toEqual({
      status: 'warning', word: 'Using default path',
      sentence: 'WikiPath is not set; the service reads the built-in default c:\\wiki.'
    });
  });

  it('reports a missing built-in default folder as an error', () => {
    const banner = corpusBanner(buildCorpusStatus('gnollhack-wiki', { configured: false, path: 'c:\\wiki', pathExists: false }), 'GnollHack wiki');
    expect(banner.status).toBe('error');
    expect(banner.word).toBe('Folder missing');
    expect(banner.sentence).toBe('WikiPath is not set, and the built-in default c:\\wiki does not exist.');
  });

  it('reports a missing configured folder', () => {
    const banner = corpusBanner(buildCorpusStatus('nethack-wiki', { pathExists: false }), 'NetHack wiki');
    expect(banner).toEqual({
      status: 'error', word: 'Folder missing',
      sentence: 'The NetHackWikiPath folder does not exist on the server.'
    });
  });

  it('shows the running stage with the file count while files are read', () => {
    const banner = corpusBanner(buildCorpusStatus('gnollhack-wiki', {
      index,
      currentJob: buildCorpusJob({ phase: 'ReadingFiles', filesRead: 1204, filesTotal: 2512, outcome: 'Running', finishedAtUtc: null })
    }), 'GnollHack wiki');
    expect(banner).toEqual({
      status: 'running', word: 'Indexing',
      sentence: `Stage: Reading files (${nf.format(1204)} of ${nf.format(2512)}).`
    });
  });

  it('shows the stage alone when the file total is unknown, or the start before the first stage', () => {
    const reading = buildCorpusStatus('gnollhack-wiki', {
      currentJob: buildCorpusJob({ phase: 'ReadingFiles', filesTotal: null, outcome: 'Running', finishedAtUtc: null })
    });
    expect(corpusBanner(reading, 'GnollHack wiki').sentence).toBe('Stage: Reading files.');

    const building = buildCorpusStatus('gnollhack-wiki', {
      currentJob: buildCorpusJob({ phase: 'BuildingIndex', outcome: 'Running', finishedAtUtc: null })
    });
    expect(corpusBanner(building, 'GnollHack wiki').sentence).toBe('Stage: Building index.');

    const idle = buildCorpusStatus('gnollhack-wiki', {
      currentJob: buildCorpusJob({ phase: 'Idle', outcome: 'Running', finishedAtUtc: null })
    });
    expect(corpusBanner(idle, 'GnollHack wiki').sentence).toBe('The pass is starting.');
  });

  it('puts a running pass before a failed one', () => {
    const banner = corpusBanner(buildCorpusStatus('gnollhack-wiki', {
      currentJob: buildCorpusJob({ id: 'b', outcome: 'Running', finishedAtUtc: null, phase: 'Publishing' }),
      recentJobs: [buildCorpusJob({ id: 'a', outcome: 'Failed', message: 'Boom' })]
    }), 'GnollHack wiki');
    expect(banner.status).toBe('running');
  });

  it('shows the message of the newest finished pass when it failed', () => {
    const banner = corpusBanner(buildCorpusStatus('gnollhack-wiki', {
      index,
      recentJobs: [
        buildCorpusJob({ id: 'old', outcome: 'Succeeded', finishedAtUtc: '2026-10-18T10:00:00Z' }),
        buildCorpusJob({ id: 'new', outcome: 'Failed', message: 'IOException: The disk is full.', finishedAtUtc: '2026-10-18T11:00:00Z' })
      ]
    }), 'GnollHack wiki');
    expect(banner).toEqual({ status: 'error', word: 'Last pass failed', sentence: 'IOException: The disk is full.' });
  });

  it('falls back to a pointer at Recent activity when the failed pass has no message', () => {
    const banner = corpusBanner(buildCorpusStatus('knowledge-base', {
      recentJobs: [buildCorpusJob({ outcome: 'Failed', message: null })]
    }), 'knowledge base');
    expect(banner.sentence).toBe('See Recent activity for the pass.');
  });

  it('ignores an older failure once a later pass succeeded', () => {
    const banner = corpusBanner(buildCorpusStatus('gnollhack-wiki', {
      index,
      recentJobs: [
        buildCorpusJob({ id: 'old', outcome: 'Failed', finishedAtUtc: '2026-10-18T10:00:00Z' }),
        buildCorpusJob({ id: 'new', outcome: 'Succeeded', finishedAtUtc: '2026-10-18T11:00:00Z' })
      ]
    }), 'GnollHack wiki');
    expect(banner.word).toBe('Ready');
  });

  it('says nothing is indexed before the first publish', () => {
    expect(corpusBanner(buildCorpusStatus('gnollhack-wiki'), 'GnollHack wiki')).toEqual({
      status: 'warning', word: 'Not indexed',
      sentence: 'No index has been published; the tools answer that indexing is in progress.'
    });
    expect(corpusBanner(buildCorpusStatus('knowledge-base'), 'knowledge base').sentence)
      .toBe('No index has been published; the tool answers that indexing is in progress.');
  });

  it('warns that a Git-polled folder outside a working tree never refreshes', () => {
    const wiki = corpusBanner(buildCorpusStatus('gnollhack-wiki', { index, isGitRepository: false }), 'GnollHack wiki');
    expect(wiki).toEqual({
      status: 'warning', word: 'Never refreshes',
      sentence: 'The folder is not a Git working tree, so automatic refresh never fires. Use Re-index after a change.'
    });
    const kb = corpusBanner(buildCorpusStatus('knowledge-base', { index, isGitRepository: false }), 'knowledge base');
    expect(kb.sentence).toBe('The folder is not a Git working tree, so automatic refresh never fires. Use Reload articles after a change.');
  });

  it('does not ask a startup-and-manual corpus for a Git working tree', () => {
    const banner = corpusBanner(buildCorpusStatus('nethack-wiki', { index, isGitRepository: false }), 'NetHack wiki');
    expect(banner.word).toBe('Ready');
  });

  it('warns that the index is out of date, with the next automatic check', () => {
    const next = '2026-10-18T14:20:00Z';
    const banner = corpusBanner(buildCorpusStatus('gnollhack-wiki', { index, upToDate: false, nextCheckAtUtc: next }), 'GnollHack wiki');
    expect(banner).toEqual({
      status: 'warning', word: 'Out of date',
      sentence: `HEAD has moved since the index was built; the next automatic check is at ${formatTime(next)}.`
    });
    const noCheck = corpusBanner(buildCorpusStatus('gnollhack-wiki', { index, upToDate: false }), 'GnollHack wiki');
    expect(noCheck.sentence).toBe('HEAD has moved since the index was built.');
  });

  it('warns that the NetHack tree was regenerated after the index was built', () => {
    const banner = corpusBanner(buildCorpusStatus('nethack-wiki', { index, treeChangedSinceIndex: true }), 'NetHack wiki');
    expect(banner).toEqual({
      status: 'warning', word: 'Tree regenerated',
      sentence: 'The wiki was regenerated after this index was built. Re-index to load it.'
    });
  });

  it('counts the unreadable files, in the singular for one', () => {
    const three = corpusBanner(buildCorpusStatus('gnollhack-wiki', {
      index: buildCorpusIndexInfo({ failedFileCount: 3 })
    }), 'GnollHack wiki');
    expect(three).toEqual({
      status: 'warning', word: 'Some files unreadable',
      sentence: '3 files could not be read and are not searchable.'
    });
    const one = corpusBanner(buildCorpusStatus('gnollhack-wiki', {
      index: buildCorpusIndexInfo({ failedFileCount: 1 })
    }), 'GnollHack wiki');
    expect(one.sentence).toBe('1 file could not be read and is not searchable.');
  });

  it('is ready when nothing is wrong, in each corpus\'s own words', () => {
    expect(corpusBanner(buildCorpusStatus('gnollhack-wiki', { index }), 'GnollHack wiki')).toEqual({
      status: 'ok', word: 'Ready', sentence: 'The index matches the current HEAD.'
    });
    expect(corpusBanner(buildCorpusStatus('nethack-wiki', { index }), 'NetHack wiki')).toEqual({
      status: 'ok', word: 'Ready', sentence: 'The index matches the generated tree.'
    });
    const articles = Array.from({ length: 1234 }, (_, i) => ({
      topic: `topic-${i}`, title: null, hasTitle: false, hasSummary: false, bytes: 10, lastWriteUtc: '2026-10-18T11:00:00Z'
    }));
    expect(corpusBanner(buildCorpusStatus('knowledge-base', { index, articles }), 'knowledge base')).toEqual({
      status: 'ok', word: 'Ready', sentence: `All ${nf.format(1234)} articles are loaded.`
    });
  });

  it('says a knowledge base with no article has none, and why', () => {
    const noContent = corpusBanner(buildCorpusStatus('knowledge-base', { index, contentPathExists: false }), 'knowledge base');
    expect(noContent).toEqual({
      status: 'warning', word: 'No articles',
      sentence: 'The KbPath folder has no Content folder, so no article is loaded.'
    });
    const empty = corpusBanner(buildCorpusStatus('knowledge-base', { index }), 'knowledge base');
    expect(empty.sentence).toBe('The Content folder holds no article.');
  });
});

describe('corpus banner helpers', () => {
  it('names the three stages in pass order', () => {
    expect(CORPUS_STAGES.map(s => s.key)).toEqual(['ReadingFiles', 'BuildingIndex', 'Publishing']);
    expect(corpusStageName('Publishing')).toBe('Publishing index');
    expect(corpusStageName('Idle')).toBeNull();
  });

  it('sorts finished passes newest first', () => {
    const sorted = sortCorpusJobs([
      buildCorpusJob({ id: 'a', finishedAtUtc: '2026-10-18T09:00:00Z' }),
      buildCorpusJob({ id: 'b', finishedAtUtc: '2026-10-18T11:00:00Z' }),
      buildCorpusJob({ id: 'c', finishedAtUtc: '2026-10-18T10:00:00Z' })
    ]);
    expect(sorted.map(j => j.id)).toEqual(['b', 'c', 'a']);
  });

  it('names the manual action', () => {
    expect(corpusActionName(buildCorpusStatus('knowledge-base'))).toBe('Reload articles');
    expect(corpusActionName(buildCorpusStatus('nethack-wiki'))).toBe('Re-index');
  });
});
