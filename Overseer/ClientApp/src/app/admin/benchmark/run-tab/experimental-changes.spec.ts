import {
  NO_CHANGES_READOUT,
  applicabilityLine,
  changeErrorText,
  changeKinds,
  changeReadout,
  changeSpec,
  changeSummaryLines,
  changesResetNote,
  conflictBetween,
  conflictReason,
  experimentLine,
  mentionLine,
  missingMentions,
  overlapLine,
  parseChangeSpec,
  resolveChangeSpec,
  selectionLabel,
  stampLength,
  toolDisplayName,
  unofferedIds
} from './experimental-changes';
import {
  askerCharacterProcedure,
  askerSearchWords,
  buildChange,
  builtInChanges,
  citePageFirst,
  raceRoleTool,
  statsReferencedBy
} from './experimental-changes.testing';

describe('experimental changes helpers', () => {
  const all = (): ReturnType<typeof builtInChanges> => [...builtInChanges(), citePageFirst()];

  describe('specs', () => {
    it('should parse a spec into its ids, a space standing for the plus, and none into an empty list', () => {
      expect(parseChangeSpec(null)).toEqual([]);
      expect(parseChangeSpec('  ')).toEqual([]);
      expect(parseChangeSpec('b-v1+a-v1')).toEqual(['b-v1', 'a-v1']);
      expect(parseChangeSpec('a-v1 b-v1')).toEqual(['a-v1', 'b-v1']);
    });

    it('should refuse a malformed spec: an empty id, a duplicate, or a stamp', () => {
      expect(parseChangeSpec('a-v1++b-v1')).toBeNull();
      expect(parseChangeSpec('+a-v1')).toBeNull();
      expect(parseChangeSpec('a-v1+a-v1')).toBeNull();
      expect(parseChangeSpec('a-v1:0123456789ab')).toBeNull();
    });

    it('should make the canonical spec: unique ids sorted ordinally, joined by a plus', () => {
      expect(changeSpec(['stats-referenced-by-v1', 'race-role-tool-v1', 'race-role-tool-v1'])).toBe('race-role-tool-v1+stats-referenced-by-v1');
      expect(changeSpec(['b', 'B', 'a'])).toBe('B+a+b');
      expect(changeSpec([])).toBeNull();
    });

    it('should measure the stamp a spec gets, its hash included', () => {
      expect(stampLength(null)).toBe(0);
      expect(stampLength('race-role-tool-v1')).toBe('race-role-tool-v1'.length + 13);
      // The four built-in ids make a 91-character spec and a 104-character stamp, well inside 160.
      const all4 = changeSpec(builtInChanges().map(c => c.id));
      expect(all4?.length).toBe(91);
      expect(stampLength(all4)).toBe(104);
    });

    it('should resolve a spec only when every change is offered, and name the ones that are not', () => {
      const changes = [raceRoleTool(), statsReferencedBy({ retired: true }), buildChange({ id: 'broken-v1', isValid: false })];
      expect(resolveChangeSpec(null, changes)).toEqual([]);
      expect(resolveChangeSpec('race-role-tool-v1', changes)?.map(c => c.id)).toEqual(['race-role-tool-v1']);
      expect(resolveChangeSpec('race-role-tool-v1+stats-referenced-by-v1', changes)).toBeNull();
      expect(unofferedIds('race-role-tool-v1+stats-referenced-by-v1+broken-v1+gone-v1', changes))
        .toEqual(['stats-referenced-by-v1', 'broken-v1', 'gone-v1']);
      expect(unofferedIds('asker-profile-v1/minus/guide.wiki_search:ab', changes)).toEqual(['asker-profile-v1/minus/guide.wiki_search:ab']);
    });

    it('should word the reset note for one id and for several', () => {
      expect(changesResetNote(['asker-profile-v1'])).toBe('Experimental Changes reset to None: asker-profile-v1 is no longer offered.');
      expect(changesResetNote(['a-v1', 'b-v1', 'c-v1'])).toBe('Experimental Changes reset to None: a-v1, b-v1 and c-v1 are no longer offered.');
    });
  });

  describe('words', () => {
    it('should list a change\'s kinds in tag order, from its kinds and its contents', () => {
      expect(changeKinds(statsReferencedBy())).toEqual(['toolOutput', 'toolDescription']);
      expect(changeKinds(raceRoleTool({ kinds: [] }))).toEqual(['newTool']);
      expect(changeKinds(askerCharacterProcedure({ kinds: [] }))).toEqual(['instructions']);
    });

    it('should read out none, one change by its title, and several with their count', () => {
      expect(changeReadout(null, all())).toBe(NO_CHANGES_READOUT);
      expect(changeReadout('race-role-tool-v1', all())).toBe('Race and role lookup tool');
      expect(changeReadout('stats-referenced-by-v1+race-role-tool-v1', all()))
        .toBe('2 changes: Race and role lookup tool, Where the game code uses it');
      expect(changeReadout('gone-v1', all())).toBe('gone-v1');
    });

    it('should give each ticked change\'s title and summary in spec order', () => {
      expect(changeSummaryLines('stats-referenced-by-v1+race-role-tool-v1', all()).map(l => l.title))
        .toEqual(['Race and role lookup tool', 'Where the game code uses it']);
      expect(changeSummaryLines('race-role-tool-v1', all())[0].summary).toContain('get_character_info');
    });

    it('should say what does not reach the model under Closed book and Source code only, and nothing when all of it does', () => {
      expect(applicabilityLine(raceRoleTool(), 'production')).toBeNull();
      expect(applicabilityLine(raceRoleTool(), 'closedBook')).toBe('Has no effect under Closed book: the AI gets no tools there.');
      expect(applicabilityLine(raceRoleTool({ appliesUnder: { production: 'full', sourceOnly: 'full', closedBook: 'partial' } }), 'closedBook'))
        .toBe('Under Closed book only its instructions reach the AI: the AI gets no tools there.');
      expect(applicabilityLine(askerSearchWords(), 'sourceOnly')).toBe('Has no effect under Source code only: wiki_search is not offered there.');
      expect(applicabilityLine(statsReferencedBy({
        appliesUnder: { production: 'full', sourceOnly: 'partial', closedBook: 'none' }, notApplyingUnderSourceOnly: ['get_item_stats']
      }), 'sourceOnly')).toBe('Under Source code only its edit of get_item_stats does not reach the AI: that tool is not offered there.');
      expect(applicabilityLine(askerCharacterProcedure(), 'closedBook')).toBeNull();
    });

    it('should name a conflict and why, from either side', () => {
      const changes = all();
      const replace = citePageFirst();
      const add = changes.find(c => c.id === 'asker-search-words-v1')!;
      expect(conflictBetween(replace, add)).toBe('one replaces the wiki_search description the other adds to');
      expect(conflictReason(add, ['cite-page-first-v1'], changes))
        .toBe('Cannot run with Cite the page first: one replaces the wiki_search description the other adds to.');
      expect(conflictReason(add, ['race-role-tool-v1'], changes)).toBeNull();
      const twin = raceRoleTool({ id: 'race-role-tool-v2', conflictsWith: ['race-role-tool-v1'] });
      expect(conflictBetween(raceRoleTool(), twin)).toBe('both offer get_character_info');
    });

    it('should warn about a ticked change that mentions one that is not ticked, until it is', () => {
      const changes = all();
      const missing = missingMentions(['asker-character-procedure-v1'], changes);
      expect(missing.map(m => [m.change.id, m.missing.id])).toEqual([['asker-character-procedure-v1', 'race-role-tool-v1']]);
      expect(mentionLine(missing[0]))
        .toBe('Answer from the player\'s own character mentions get_character_info, which this run does not offer.');
      expect(missingMentions(['asker-character-procedure-v1', 'race-role-tool-v1'], changes)).toEqual([]);
      // A mentioned change that is retired is not offered, so nothing is suggested.
      expect(missingMentions(['asker-character-procedure-v1'], [askerCharacterProcedure(), raceRoleTool({ retired: true })])).toEqual([]);
    });

    it('should name an output switch by the word its texts use', () => {
      const mention = { change: askerCharacterProcedure({ mentions: ['stats-referenced-by-v1'] }), missing: statsReferencedBy() };
      expect(mentionLine(mention)).toBe('Answer from the player\'s own character mentions referenced_by, which this run does not offer.');
    });

    it('should label a recent selection All four when it is every built-in change, else by its titles', () => {
      const changes = all();
      expect(selectionLabel(builtInChanges().map(c => c.id), changes)).toBe('All four');
      expect(selectionLabel(['stats-referenced-by-v1', 'race-role-tool-v1'], changes))
        .toBe('Race and role lookup tool + Where the game code uses it');
      expect(selectionLabel(['gone-v1'], changes)).toBe('gone-v1');
    });

    it('should word an output switch, a tool and an overlap', () => {
      expect(experimentLine('stats_referenced_by')).toContain('places in the game\'s code');
      expect(experimentLine('nope')).toBeNull();
      expect(toolDisplayName('get_character_info')).toBe('race and role lookup');
      expect(toolDisplayName('nope')).toBeNull();
      expect(overlapLine({
        text: 'the quest artifact of the healer is the staff', suiteId: 1, suiteName: 'Gnoll Basics', questionId: 9,
        questionNumber: 3, field: 'rubric'
      })).toBe('Shares "the quest artifact of the healer is the staff" with the rubric of question 3 of Gnoll Basics.');
      expect(overlapLine({ text: 'a b c d e f g h', suiteId: 1, suiteName: 'S', questionId: 9, questionNumber: 1, field: 'question' }))
        .toBe('Shares "a b c d e f g h" with question 1 of S.');
    });

    it('should read a refusal from the error body, a string body or a message', () => {
      expect(changeErrorText({ error: { error: 'Its text is locked.' } }, 'x')).toBe('Its text is locked.');
      expect(changeErrorText({ error: 'Plain text.' }, 'x')).toBe('Plain text.');
      expect(changeErrorText({ error: { message: 'A message.' } }, 'x')).toBe('A message.');
      expect(changeErrorText({ status: 500 }, 'Fallback.')).toBe('Fallback.');
    });
  });
});
