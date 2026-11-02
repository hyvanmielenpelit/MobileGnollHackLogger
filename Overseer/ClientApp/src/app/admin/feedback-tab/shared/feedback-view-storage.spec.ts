import {
  FEEDBACK_SELECTION_CAP,
  FeedbackViewRecord,
  defaultFeedbackRange,
  feedbackExportFormat,
  isFeedbackOccurrenceCount,
  forgetFeedbackSelection,
  loadFeedbackView,
  saveFeedbackView
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
      range: { preset: 'forever', start: 'x', end: 'y' }, exportOptions: { occurrencesPerIssue: 0 }
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

  it('keeps any whole occurrence count from 1 to 1,000, or all, and reads anything else as no options', () => {
    for (const occurrencesPerIssue of [1, 5, 50, 100, 137, 1000, 'all'] as const) {
      const stored = record({ exportOptions: { occurrencesPerIssue, includeQuestionText: true, format: 'yaml' } });
      saveFeedbackView('overseer', stored);
      expect(loadFeedbackView('overseer')!.exportOptions).toEqual({ occurrencesPerIssue, includeQuestionText: true, format: 'yaml' });
    }

    for (const occurrencesPerIssue of [0, -5, 1001, 2.5, '10', 'All', null, true]) {
      localStorage.setItem('overseer.admin.feedback.overseer.view', JSON.stringify({
        ...record(), exportOptions: { occurrencesPerIssue, includeQuestionText: true }
      }));
      expect(loadFeedbackView('overseer')!.exportOptions).toBeUndefined();
      expect(loadFeedbackView('overseer')!.sort).toBe('count');
    }

    expect(isFeedbackOccurrenceCount(1)).toBe(true);
    expect(isFeedbackOccurrenceCount(1000)).toBe(true);
    expect(isFeedbackOccurrenceCount(1001)).toBe(false);
    expect(isFeedbackOccurrenceCount(0.5)).toBe(false);
    expect(isFeedbackOccurrenceCount(Number.NaN)).toBe(false);
    expect(isFeedbackOccurrenceCount('5')).toBe(false);
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

  it('caps a selection at 500 issues', () => {
    expect(FEEDBACK_SELECTION_CAP).toBe(500);
  });

  it('forgets only its own sub-tab\'s selection record, and leaves the views', () => {
    const overseer = 'overseer.admin.feedback.overseer.selection';
    const gnollbench = 'overseer.admin.feedback.gnollbench.selection';
    localStorage.setItem(overseer, JSON.stringify({ version: 1, ids: [1] }));
    localStorage.setItem(gnollbench, JSON.stringify({ version: 1, fingerprints: ['a'] }));
    saveFeedbackView('overseer', record());

    forgetFeedbackSelection('overseer');
    expect(localStorage.getItem(overseer)).toBeNull();
    expect(localStorage.getItem(gnollbench)).not.toBeNull();
    expect(loadFeedbackView('overseer')).not.toBeNull();

    forgetFeedbackSelection('gnollbench');
    expect(localStorage.getItem(gnollbench)).toBeNull();
  });

  it('survives storage that throws', () => {
    const get = vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => { throw new Error('denied'); });
    const set = vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => { throw new Error('denied'); });
    const remove = vi.spyOn(Storage.prototype, 'removeItem').mockImplementation(() => { throw new Error('denied'); });

    expect(loadFeedbackView('overseer')).toBeNull();
    expect(() => saveFeedbackView('overseer', record())).not.toThrow();
    expect(() => forgetFeedbackSelection('overseer')).not.toThrow();
    expect(() => forgetFeedbackSelection('gnollbench')).not.toThrow();

    get.mockRestore();
    set.mockRestore();
    remove.mockRestore();
  });
});
