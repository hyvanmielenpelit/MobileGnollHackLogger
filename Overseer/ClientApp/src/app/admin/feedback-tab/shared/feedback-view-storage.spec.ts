import {
  FEEDBACK_SELECTION_CAP,
  FeedbackViewRecord,
  defaultFeedbackRange,
  feedbackExportFormat,
  loadFeedbackView,
  loadGnollBenchSelection,
  loadOverseerSelection,
  saveFeedbackView,
  saveGnollBenchSelection,
  saveOverseerSelection
} from './feedback-view-storage';

const KEYS = [
  'overseer.admin.feedback.overseer.view',
  'overseer.admin.feedback.gnollbench.view',
  'overseer.admin.feedback.overseer.selection',
  'overseer.admin.feedback.gnollbench.selection',
  'overseer.admin.telemetry.feedback.view'
];

function clear(): void {
  for (const key of KEYS) {
    try { localStorage.removeItem(key); } catch { /* nothing stored */ }
  }
}

const record = (overrides: Partial<FeedbackViewRecord> = {}): FeedbackViewRecord => ({
  version: 1,
  sort: 'count',
  filters: { status: ['resolved'], category: ['tool_problem', 'own_mistake'] },
  search: 'crystal',
  range: { preset: 'custom', start: '2026-09-21', end: '2026-10-21' },
  ...overrides
});

describe('feedback-view-storage', () => {
  beforeEach(clear);
  afterEach(clear);

  it('starts on All time', () => {
    expect(defaultFeedbackRange()).toEqual({ preset: 'all', start: '', end: '' });
    expect(defaultFeedbackRange()).not.toBe(defaultFeedbackRange());
  });

  it('keeps each sub-tab\'s view under its own key and reads it back', () => {
    const overseer = record({ exportOptions: { occurrencesPerIssue: 25, includeQuestionText: true } });
    const gnollbench = record({ sort: 'runs', filters: { variant: ['none'] }, search: '' });
    saveFeedbackView('overseer', overseer);
    saveFeedbackView('gnollbench', gnollbench);

    expect(JSON.parse(localStorage.getItem('overseer.admin.feedback.overseer.view')!)).toEqual(overseer);
    expect(JSON.parse(localStorage.getItem('overseer.admin.feedback.gnollbench.view')!)).toEqual(gnollbench);
    expect(loadFeedbackView('overseer')).toEqual(overseer);
    expect(loadFeedbackView('gnollbench')).toEqual(gnollbench);
  });

  it('reads nothing stored, an unreadable record and a wrong version as none', () => {
    expect(loadFeedbackView('overseer')).toBeNull();
    expect(loadFeedbackView('gnollbench')).toBeNull();

    localStorage.setItem('overseer.admin.feedback.gnollbench.view', '{not json');
    expect(loadFeedbackView('gnollbench')).toBeNull();

    localStorage.setItem('overseer.admin.feedback.gnollbench.view', JSON.stringify({ ...record(), version: 2 }));
    expect(loadFeedbackView('gnollbench')).toBeNull();
  });

  it('drops malformed parts of a record and keeps the rest', () => {
    localStorage.setItem('overseer.admin.feedback.overseer.view', JSON.stringify({
      version: 1, sort: 7, filters: { status: ['new', 3], model: 'gpt-x', tool: ['wiki_search'] }, search: null,
      range: { preset: 'forever', start: 'x', end: 'y' }, exportOptions: { occurrencesPerIssue: 7 }
    }));

    expect(loadFeedbackView('overseer')).toEqual({
      version: 1, sort: '', filters: { status: ['new'], tool: ['wiki_search'] }, search: '', range: defaultFeedbackRange()
    });
  });

  it('keeps the export format, and reads any other stored format as YAML', () => {
    const markdown = record({ exportOptions: { occurrencesPerIssue: 10, includeQuestionText: false, format: 'markdown' } });
    saveFeedbackView('gnollbench', markdown);
    expect(loadFeedbackView('gnollbench')).toEqual(markdown);
    expect(loadFeedbackView('gnollbench')!.version).toBe(1);
    expect(feedbackExportFormat(loadFeedbackView('gnollbench')!.exportOptions)).toBe('markdown');

    for (const stored of ['pdf', 'Markdown', 3, null]) {
      localStorage.setItem('overseer.admin.feedback.overseer.view', JSON.stringify({
        ...record(), exportOptions: { occurrencesPerIssue: 5, includeQuestionText: true, format: stored }
      }));
      const options = loadFeedbackView('overseer')!.exportOptions;
      expect(options).toEqual({ occurrencesPerIssue: 5, includeQuestionText: true });
      expect(feedbackExportFormat(options)).toBe('yaml');
    }

    localStorage.setItem('overseer.admin.feedback.overseer.view', JSON.stringify({
      ...record(), exportOptions: { occurrencesPerIssue: 25, includeQuestionText: true }
    }));
    expect(feedbackExportFormat(loadFeedbackView('overseer')!.exportOptions)).toBe('yaml');
    expect(feedbackExportFormat(undefined)).toBe('yaml');
    expect(feedbackExportFormat({ format: 'yaml' })).toBe('yaml');
  });

  it('reads the Overseer sort from the key it had before the sub-tabs, once, and leaves that key in place', () => {
    localStorage.setItem('overseer.admin.telemetry.feedback.view', JSON.stringify({ version: 1, sort: 'firstSeen' }));

    expect(loadFeedbackView('overseer')).toEqual({ version: 1, sort: 'firstSeen', filters: {}, search: '', range: defaultFeedbackRange() });
    expect(loadFeedbackView('gnollbench')).toBeNull();
    expect(localStorage.getItem('overseer.admin.telemetry.feedback.view')).not.toBeNull();

    saveFeedbackView('overseer', record({ sort: 'users' }));
    expect(loadFeedbackView('overseer')!.sort).toBe('users');
    expect(localStorage.getItem('overseer.admin.telemetry.feedback.view')).not.toBeNull();
  });

  it('keeps the Overseer selection as issue ids, at most 500, without duplicates', () => {
    expect(loadOverseerSelection()).toEqual([]);
    saveOverseerSelection([3, 1, 3]);
    expect(JSON.parse(localStorage.getItem('overseer.admin.feedback.overseer.selection')!)).toEqual({ version: 1, ids: [3, 1] });
    expect(loadOverseerSelection()).toEqual([3, 1]);

    saveOverseerSelection(Array.from({ length: 600 }, (_, i) => i + 1));
    expect(loadOverseerSelection().length).toBe(FEEDBACK_SELECTION_CAP);
    expect(FEEDBACK_SELECTION_CAP).toBe(500);
  });

  it('keeps the GnollBench selection as fingerprints, at most 500', () => {
    expect(loadGnollBenchSelection()).toEqual([]);
    saveGnollBenchSelection(['a'.repeat(64), 'b'.repeat(64)]);
    expect(JSON.parse(localStorage.getItem('overseer.admin.feedback.gnollbench.selection')!)).toEqual(
      { version: 1, fingerprints: ['a'.repeat(64), 'b'.repeat(64)] });
    expect(loadGnollBenchSelection()).toEqual(['a'.repeat(64), 'b'.repeat(64)]);

    saveGnollBenchSelection(Array.from({ length: 501 }, (_, i) => `f${i}`));
    expect(loadGnollBenchSelection().length).toBe(500);
  });

  it('reads a malformed or wrong-version selection as empty', () => {
    localStorage.setItem('overseer.admin.feedback.overseer.selection', JSON.stringify({ version: 2, ids: [1] }));
    expect(loadOverseerSelection()).toEqual([]);
    localStorage.setItem('overseer.admin.feedback.overseer.selection', JSON.stringify({ version: 1, ids: [1, 'x', -2, 1.5] }));
    expect(loadOverseerSelection()).toEqual([1]);
    localStorage.setItem('overseer.admin.feedback.gnollbench.selection', 'nope');
    expect(loadGnollBenchSelection()).toEqual([]);
  });

  it('survives storage that throws', () => {
    const get = vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => { throw new Error('denied'); });
    const set = vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => { throw new Error('denied'); });

    expect(loadFeedbackView('overseer')).toBeNull();
    expect(loadOverseerSelection()).toEqual([]);
    expect(loadGnollBenchSelection()).toEqual([]);
    expect(() => saveFeedbackView('overseer', record())).not.toThrow();
    expect(() => saveOverseerSelection([1])).not.toThrow();
    expect(() => saveGnollBenchSelection(['a'])).not.toThrow();

    get.mockRestore();
    set.mockRestore();
  });
});
