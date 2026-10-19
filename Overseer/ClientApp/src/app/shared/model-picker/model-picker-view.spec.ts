import {
  DEFAULT_MODEL_PICKER_VIEW,
  MODEL_PICKER_TOOLS_MIN_OPTIONS,
  MODEL_PICKER_VIEW_STORAGE_PREFIX,
  ModelPickerViewOption,
  ModelPickerViewState,
  ModelPickerViewWriter,
  applyModelPickerView,
  effectiveView,
  hasReleaseDates,
  isViewActive,
  matchParts,
  providerFacets,
  readModelPickerView,
  releaseMonthLabel,
  searchTerms,
  showsViewTools,
  toolsToggleLabel,
  viewNarrows,
  withoutNarrowing,
  writeModelPickerView
} from './model-picker-view';

function opt(displayName: string, extra: Partial<ModelPickerViewOption['model']> = {},
             more: Partial<Omit<ModelPickerViewOption, 'model'>> = {}): ModelPickerViewOption {
  return { model: { displayName, ...extra }, ...more };
}

function view(changes: Partial<ModelPickerViewState> = {}): ModelPickerViewState {
  return { sort: 'default', providers: [], search: '', ...changes };
}

function names(options: readonly ModelPickerViewOption[], order: number[]): string[] {
  return order.map(i => options[i].model.displayName ?? options[i].model.modelId ?? '');
}

describe('model-picker-view', () => {
  describe('showsViewTools', () => {
    it('shows the toolbar from the threshold up in auto mode, and always or never on request', () => {
      expect(MODEL_PICKER_TOOLS_MIN_OPTIONS).toBe(2);
      expect(showsViewTools('auto', 0)).toBe(false);
      expect(showsViewTools('auto', 1)).toBe(false);
      expect(showsViewTools('auto', 2)).toBe(true);
      expect(showsViewTools('always', 1)).toBe(true);
      expect(showsViewTools('never', 50)).toBe(false);
    });
  });

  describe('search', () => {
    const options = [
      opt('Gemini Luna 2', { modelId: 'gemini-luna-2', provider: 'Google' }),
      opt('Claude Opus 5.5', { modelId: 'claude-opus-5-5', provider: 'Anthropic' }),
      opt('GPT-6', { modelId: 'gpt-6', provider: 'OpenAI' }, { tag: 'Reader' })
    ];

    it('splits trimmed, lower-cased terms on whitespace', () => {
      expect(searchTerms('  Opus   5 ')).toEqual(['opus', '5']);
      expect(searchTerms('   ')).toEqual([]);
    });

    it('matches every term, case-insensitively, against the name, the model id and the tag', () => {
      expect(names(options, applyModelPickerView(options, view({ search: 'LUNA' })))).toEqual(['Gemini Luna 2']);
      expect(names(options, applyModelPickerView(options, view({ search: 'opus 5' })))).toEqual(['Claude Opus 5.5']);
      expect(names(options, applyModelPickerView(options, view({ search: 'opus luna' })))).toEqual([]);
      expect(names(options, applyModelPickerView(options, view({ search: 'opus-5-5' })))).toEqual(['Claude Opus 5.5']);
      expect(names(options, applyModelPickerView(options, view({ search: 'reader' })))).toEqual(['GPT-6']);
    });
  });

  describe('sorting', () => {
    it('sorts names with numeric collation, either way', () => {
      const options = [opt('GPT-6.10'), opt('gpt-6.2'), opt('Alpha')];
      expect(names(options, applyModelPickerView(options, view({ sort: 'name-asc' })))).toEqual(['Alpha', 'gpt-6.2', 'GPT-6.10']);
      expect(names(options, applyModelPickerView(options, view({ sort: 'name-desc' })))).toEqual(['GPT-6.10', 'gpt-6.2', 'Alpha']);
    });

    it('keeps the original order for equal names and for the default order', () => {
      const options = [opt('Same', { modelId: 'b' }), opt('Same', { modelId: 'a' }), opt('Same', { modelId: 'a' })];
      expect(applyModelPickerView(options, view({ sort: 'name-asc' }))).toEqual([1, 2, 0]);
      expect(applyModelPickerView(options, view({ sort: 'name-desc' }))).toEqual([0, 1, 2]);
      expect(applyModelPickerView(options, view())).toEqual([0, 1, 2]);
    });

    it('sorts release dates with unknown dates last in both directions, ties by name', () => {
      const options = [
        opt('Undated'),
        opt('Middle', { releaseDate: '2026-03-01' }),
        opt('Newest', { releaseDate: '2026-09-01' }),
        opt('Oldest B', { releaseDate: '2025-01-01' }),
        opt('Oldest A', { releaseDate: '2025-01-01' }),
        opt('Bad date', { releaseDate: '2026' })
      ];
      expect(names(options, applyModelPickerView(options, view({ sort: 'release-desc' }))))
        .toEqual(['Newest', 'Middle', 'Oldest A', 'Oldest B', 'Bad date', 'Undated']);
      expect(names(options, applyModelPickerView(options, view({ sort: 'release-asc' }))))
        .toEqual(['Oldest A', 'Oldest B', 'Middle', 'Newest', 'Bad date', 'Undated']);
    });

    it('sorts each run of a group on its own and keeps the runs in order', () => {
      const options = [
        opt('Zed', {}, { group: 'Your Models' }),
        opt('Amber', {}, { group: 'Your Models' }),
        opt('Yew', {}, { group: 'System Models' }),
        opt('Birch', {}, { group: 'System Models' })
      ];
      expect(names(options, applyModelPickerView(options, view({ sort: 'name-asc' }))))
        .toEqual(['Amber', 'Zed', 'Birch', 'Yew']);
    });
  });

  describe('provider filter', () => {
    const options = [
      opt('A', { provider: 'OpenAI' }),
      opt('B', { provider: 'Anthropic' }),
      opt('C', { provider: 'google' }),
      opt('D', { provider: 'Google' })
    ];

    it('lists one facet per provider, case-insensitively, by name', () => {
      expect(providerFacets(options)).toEqual([
        { provider: 'Anthropic', key: 'anthropic', count: 1 },
        { provider: 'google', key: 'google', count: 2 },
        { provider: 'OpenAI', key: 'openai', count: 1 }
      ]);
    });

    it('shows the options of any chosen provider', () => {
      expect(names(options, applyModelPickerView(options, view({ providers: ['openai', 'google'] }))))
        .toEqual(['A', 'C', 'D']);
    });

    it('narrows the stored providers to those present, without rewriting the stored view', () => {
      const stored = view({ providers: ['xai'] });
      const effective = effectiveView(stored, options);
      expect(effective.providers).toEqual([]);
      expect(applyModelPickerView(options, effective)).toEqual([0, 1, 2, 3]);
      expect(stored.providers).toEqual(['xai']);
    });

    it('falls back from a release-date sort to the default order when no option has a date', () => {
      expect(hasReleaseDates(options)).toBe(false);
      expect(effectiveView(view({ sort: 'release-desc' }), options).sort).toBe('default');
      const dated = [...options, opt('E', { releaseDate: '2026-01-01' })];
      expect(effectiveView(view({ sort: 'release-desc' }), dated).sort).toBe('release-desc');
    });
  });

  describe('storage', () => {
    const KEY = 'spec.picker';
    const storageKey = MODEL_PICKER_VIEW_STORAGE_PREFIX + KEY;

    afterEach(() => {
      localStorage.removeItem(storageKey);
      vi.restoreAllMocks();
    });

    it('round-trips a view', () => {
      const state = view({ sort: 'name-desc', providers: ['openai'], search: 'luna' });
      writeModelPickerView(KEY, state);
      expect(JSON.parse(localStorage.getItem(storageKey)!)).toEqual({ version: 1, sort: 'name-desc', providers: ['openai'], search: 'luna' });
      expect(readModelPickerView(KEY)).toEqual(state);
    });

    it('reads the default from corrupt JSON, a wrong version or an unknown sort', () => {
      localStorage.setItem(storageKey, '{not json');
      expect(readModelPickerView(KEY)).toEqual(DEFAULT_MODEL_PICKER_VIEW);
      localStorage.setItem(storageKey, JSON.stringify({ version: 2, sort: 'name-asc', providers: [], search: '' }));
      expect(readModelPickerView(KEY)).toEqual(DEFAULT_MODEL_PICKER_VIEW);
      localStorage.setItem(storageKey, JSON.stringify({ version: 1, sort: 'by-mood', providers: ['OpenAI', 4], search: 7 }));
      expect(readModelPickerView(KEY)).toEqual(view({ providers: ['openai'] }));
    });

    it('reads the default and ignores the write when storage throws', () => {
      vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => { throw new Error('blocked'); });
      vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => { throw new Error('blocked'); });
      expect(readModelPickerView(KEY)).toEqual(DEFAULT_MODEL_PICKER_VIEW);
      expect(() => writeModelPickerView(KEY, view({ search: 'x' }))).not.toThrow();
    });

    it('removes the entry when the default view is written', () => {
      writeModelPickerView(KEY, view({ search: 'x' }));
      writeModelPickerView(KEY, view());
      expect(localStorage.getItem(storageKey)).toBeNull();
    });

    it('reads the default and writes nothing for a null key', () => {
      const setItem = vi.spyOn(Storage.prototype, 'setItem');
      writeModelPickerView(null, view({ search: 'x' }));
      expect(setItem).not.toHaveBeenCalled();
      expect(readModelPickerView(null)).toEqual(DEFAULT_MODEL_PICKER_VIEW);
    });

    it('writes a search after the quiet period, and other changes at once', () => {
      vi.useFakeTimers();
      try {
        const writer = new ModelPickerViewWriter(() => KEY);
        writer.write(view({ search: 'lu' }), true);
        expect(localStorage.getItem(storageKey)).toBeNull();
        vi.advanceTimersByTime(300);
        expect(readModelPickerView(KEY).search).toBe('lu');
        writer.write(view({ search: 'lu', sort: 'name-asc' }), false);
        expect(readModelPickerView(KEY).sort).toBe('name-asc');
        writer.write(view({ search: 'luna', sort: 'name-asc' }), true);
        writer.flush();
        expect(readModelPickerView(KEY).search).toBe('luna');
      } finally {
        vi.useRealTimers();
      }
    });
  });

  describe('isViewActive', () => {
    it('is false for the default view and a blank search only', () => {
      expect(isViewActive(view())).toBe(false);
      expect(isViewActive(view({ search: '  ' }))).toBe(false);
      expect(isViewActive(view({ sort: 'name-asc' }))).toBe(true);
      expect(isViewActive(view({ providers: ['openai'] }))).toBe(true);
    });
  });

  describe('viewNarrows', () => {
    it('is true for a search or a provider filter, and not for a sort or a blank search', () => {
      expect(viewNarrows(view())).toBe(false);
      expect(viewNarrows(view({ search: '  ' }))).toBe(false);
      expect(viewNarrows(view({ sort: 'name-asc' }))).toBe(false);
      expect(viewNarrows(view({ search: 'luna' }))).toBe(true);
      expect(viewNarrows(view({ providers: ['openai'] }))).toBe(true);
    });

    it('ignores a stored provider no option has, through the effective view', () => {
      const options = [opt('A', { provider: 'OpenAI' }), opt('B', { provider: 'Anthropic' })];
      expect(viewNarrows(effectiveView(view({ providers: ['xai'] }), options))).toBe(false);
    });
  });

  describe('withoutNarrowing', () => {
    it('clears the search and the providers and keeps the sort', () => {
      expect(withoutNarrowing(view({ sort: 'release-desc', providers: ['openai'], search: 'luna' })))
        .toEqual(view({ sort: 'release-desc' }));
    });
  });

  describe('toolsToggleLabel', () => {
    it('says whether the view is filtered, sorted, or both', () => {
      expect(toolsToggleLabel(view())).toBe('Filter and sort models');
      expect(toolsToggleLabel(view({ search: 'luna' }))).toBe('Filter and sort models (filtered)');
      expect(toolsToggleLabel(view({ sort: 'name-asc' }))).toBe('Filter and sort models (sorted)');
      expect(toolsToggleLabel(view({ sort: 'name-asc', providers: ['openai'] })))
        .toBe('Filter and sort models (filtered and sorted)');
    });
  });

  describe('matchParts', () => {
    it('marks every matching run, merging overlapping terms', () => {
      expect(matchParts('Claude Opus 5.5', ['opu', 'pus', '5'])).toEqual([
        { text: 'Claude ', match: false },
        { text: 'Opus', match: true },
        { text: ' ', match: false },
        { text: '5', match: true },
        { text: '.', match: false },
        { text: '5', match: true }
      ]);
    });

    it('returns the whole text unmarked without terms', () => {
      expect(matchParts('Alpha', [])).toEqual([{ text: 'Alpha', match: false }]);
    });
  });

  describe('releaseMonthLabel', () => {
    it('reads the month in UTC, and nothing for a missing or malformed date', () => {
      expect(releaseMonthLabel('2026-03-01')).toBe('Mar 2026');
      expect(releaseMonthLabel(null)).toBe('');
      expect(releaseMonthLabel('2026-13-01')).toBe('');
      expect(releaseMonthLabel('March')).toBe('');
    });
  });
});
