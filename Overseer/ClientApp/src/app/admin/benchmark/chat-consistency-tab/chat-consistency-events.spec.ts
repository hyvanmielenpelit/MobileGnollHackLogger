import {
  CC_EVENT_KIND_LABELS,
  CC_HARNESS_EVENT_KIND,
  CcEventDay,
  CcEventListItem,
  CcMarkerFilter,
  CcMarkerKind,
  buildEventDays,
  compareEventKinds,
  eventGroupLabel,
  eventGroupShown,
  eventKindLabel,
  eventKindSummary,
  groupOverseerEvents,
  mainHarnessVersion,
  servedChangeLabel,
  servedModelChanges,
  taggedAnnotations,
  utcWeekday
} from './chat-consistency-events';
import {
  ccAnnotation,
  ccEvent,
  ccEventAnnotations,
  ccEventPoints,
  ccOverseerEvents,
  ccPoint
} from './chat-consistency-tab.testing';

function filterOf(kinds: CcMarkerKind[], hiddenEventKinds: string[] = []): CcMarkerFilter {
  return { kinds: new Set(kinds), hiddenEventKinds: new Set(hiddenEventKinds) };
}

function itemTag(item: CcEventListItem): string {
  switch (item.kind) {
    case 'event': return item.group.tag;
    case 'annotation': return item.tag;
    default: return item.change.tag;
  }
}

/** `yyyy-MM-dd: E1 A2 S1`, one line per day. */
function dayLines(days: readonly CcEventDay[]): string[] {
  return days.map(day => `${day.day}: ${day.items.map(itemTag).join(' ')}`);
}

describe('chat-consistency-events', () => {
  describe('event kinds', () => {
    it('labels the server kinds and falls back to the kind itself', () => {
      expect(eventKindLabel('CandidateSystemPromptSha256')).toBe('System prompt');
      expect(eventKindLabel('KnowledgeBaseHeadSha')).toBe('Knowledge base');
      expect(eventKindLabel(CC_HARNESS_EVENT_KIND)).toBe('Harness');
      expect(eventKindLabel('SomethingNew')).toBe('SomethingNew');
      // Inherited object members are not labels.
      expect(eventKindLabel('toString')).toBe('toString');
    });

    it('orders known kinds as the server lists them, then unknown kinds by name', () => {
      const kinds = ['Zeta', CC_HARNESS_EVENT_KIND, 'Alpha', 'WikiHeadSha', 'CandidateSystemPromptSha256', 'ToolGuidesSha256'];
      expect(kinds.slice().sort(compareEventKinds))
        .toEqual(['CandidateSystemPromptSha256', 'ToolGuidesSha256', 'WikiHeadSha', CC_HARNESS_EVENT_KIND, 'Alpha', 'Zeta']);
      expect(Object.keys(CC_EVENT_KIND_LABELS)[0]).toBe('CandidateSystemPromptSha256');
      expect(Object.keys(CC_EVENT_KIND_LABELS).at(-1)).toBe(CC_HARNESS_EVENT_KIND);
    });

    it('reads the main version of a harness identity', () => {
      expect(mainHarnessVersion('27')).toBe('27');
      expect(mainHarnessVersion('29 (re-run 30)')).toBe('29');
      expect(mainHarnessVersion(' 28 ')).toBe('28');
      expect(mainHarnessVersion('')).toBeNull();
      expect(mainHarnessVersion(null)).toBeNull();
      expect(mainHarnessVersion(undefined)).toBeNull();
      expect(mainHarnessVersion(' (re-run 3)')).toBeNull();
    });
  });

  describe('groupOverseerEvents', () => {
    const groups = groupOverseerEvents(ccOverseerEvents(), ccEventPoints());

    it('groups by UTC day and harness version, tagged in time order', () => {
      expect(groups.map(g => `${g.tag} ${g.key}`)).toEqual([
        'E1 2026-09-03|27',
        'E2 2026-09-05|28',
        'E3 2026-09-09|29',
        'E4 2026-09-10|29'
      ]);
      expect(groups.map(g => g.day)).toEqual(['2026-09-03', '2026-09-05', '2026-09-09', '2026-09-10']);
      expect(groups.map(g => g.harnessVersion)).toEqual(['27', '28', '29', '29']);
    });

    it('spans a group from its earliest to its latest event and lists its runs once, ascending', () => {
      const [e1, e2, e3, e4] = groups;
      expect([e1.atUtc, e1.lastAtUtc]).toEqual(['2026-09-03T08:00:00Z', '2026-09-03T14:00:00Z']);
      expect([e2.atUtc, e2.lastAtUtc]).toEqual(['2026-09-05T08:00:00Z', '2026-09-05T08:00:00Z']);
      expect([e3.atUtc, e3.lastAtUtc]).toEqual(['2026-09-09T09:00:00Z', '2026-09-09T10:00:00Z']);
      expect(e1.runIds).toEqual([202, 203]);
      expect(e2.runIds).toEqual([204]);
      expect(e3.runIds).toEqual([298, 299]);
      expect(e4.runIds).toEqual([206, 297]);
    });

    it('orders the events of a group by time, then run, then kind order', () => {
      expect(groups[0].events.map(e => `${e.runId} ${e.kind}`)).toEqual([
        '202 CandidateSystemPromptSha256',
        '202 KnowledgeBaseHeadSha',
        '203 KnowledgeBaseHeadSha'
      ]);
      expect(groups[1].events.map(e => e.kind)).toEqual(['ToolGuidesSha256', CC_HARNESS_EVENT_KIND]);

      const sameInstant = groupOverseerEvents([
        ccEvent({ atUtc: '2026-09-15T00:00:00Z', runId: 105, kind: 'WikiHeadSha' }),
        ccEvent({ atUtc: '2026-09-15T00:00:00Z', runId: 103, kind: 'WikiHeadSha' })
      ], [ccPoint(103, '2026-09-12T08:00:00Z'), ccPoint(105, '2026-09-15T00:00:00Z')]);
      expect(sameInstant[0].events.map(e => e.runId)).toEqual([103, 105]);
    });

    it('lists one change per kind in kind order, counting the runs that detected it', () => {
      expect(groups[0].changes).toEqual([
        { kind: 'CandidateSystemPromptSha256', label: 'System prompt', count: 1 },
        { kind: 'KnowledgeBaseHeadSha', label: 'Knowledge base', count: 2 }
      ]);
      expect(groups[1].changes).toEqual([
        { kind: 'ToolGuidesSha256', label: 'Tool guides', count: 1 },
        { kind: CC_HARNESS_EVENT_KIND, label: 'Harness', count: 1 }
      ]);
      expect(groups[3].changes.map(c => c.kind)).toEqual(['SourceCodeHeadSha', 'CorpusIndexFingerprintsJson']);
    });

    it('counts a kind once per run, however often that run reports it', () => {
      const [group] = groupOverseerEvents([
        ccEvent({ atUtc: '2026-09-15T01:00:00Z', runId: 103, kind: 'WikiHeadSha' }),
        ccEvent({ atUtc: '2026-09-15T02:00:00Z', runId: 103, kind: 'WikiHeadSha' })
      ], [ccPoint(103, '2026-09-12T08:00:00Z')]);
      expect(group.changes).toEqual([{ kind: 'WikiHeadSha', label: 'Wiki', count: 1 }]);
      expect(group.events.length).toBe(2);
    });

    it('titles a harness change, a known harness and an unknown one', () => {
      expect(groups.map(g => g.title)).toEqual([
        'Changes under harness 27',
        'Harness 27 → 28',
        'Harness 28 → 29 (re-run 30)',
        'Changes under harness 29'
      ]);
      expect(groups[0].harnessChange).toBeNull();
      expect(groups[1].harnessChange).toEqual({ from: '27', to: '28' });
      expect(groups[2].harnessChange).toEqual({ from: '28', to: '29 (re-run 30)' });

      const unknown = groupOverseerEvents([ccEvent({ atUtc: '2026-09-15T00:00:00Z', runId: 999, kind: 'WikiHeadSha' })], []);
      expect(unknown.map(g => [g.key, g.harnessVersion, g.title])).toEqual([['2026-09-15|', null, 'Overseer changes']]);

      const noFrom = groupOverseerEvents(
        [ccEvent({ atUtc: '2026-09-15T00:00:00Z', runId: 999, kind: CC_HARNESS_EVENT_KIND, from: null, to: '31' })], []);
      expect(noFrom[0].title).toBe('Harness unknown → 31');
      expect(noFrom[0].harnessVersion).toBe('31');
    });

    it('takes a run harness from its timeline point, and a harness change without one from the main version it changed to', () => {
      // Run 299 has no point: its HarnessVersion event's `to` is the identity `29 (re-run 30)`.
      expect(groups[2].harnessVersion).toBe('29');
      // A point with no harness falls back the same way.
      const [group] = groupOverseerEvents(
        [ccEvent({ atUtc: '2026-09-15T00:00:00Z', runId: 103, kind: CC_HARNESS_EVENT_KIND, from: '30', to: '31 (re-run 32)' })],
        [ccPoint(103, '2026-09-15T00:00:00Z', { harnessVersion: '  ' })]);
      expect(group.key).toBe('2026-09-15|31');
      // The point wins over the event's own `to`.
      const [fromPoint] = groupOverseerEvents(
        [ccEvent({ atUtc: '2026-09-15T00:00:00Z', runId: 103, kind: CC_HARNESS_EVENT_KIND, from: '30', to: '31' })],
        [ccPoint(103, '2026-09-15T00:00:00Z', { harnessVersion: '30' })]);
      expect(fromPoint.harnessVersion).toBe('30');
    });

    it('lets an unknown-harness event start its day, and a later known harness adopt that group', () => {
      // E3: the wiki change on run 298 has no harness; the harness change on run 299 gives it one.
      expect(groups[2].events.map(e => `${e.runId} ${e.kind}`)).toEqual(['298 WikiHeadSha', `299 ${CC_HARNESS_EVENT_KIND}`]);
      expect(groups[2].key).toBe('2026-09-09|29');
    });

    it('puts an unknown-harness event into the earliest group of its day', () => {
      // E4: the corpus index change on run 297 joins run 206's group.
      expect(groups[3].events.map(e => e.runId)).toEqual([206, 297]);

      const twoHarnesses = groupOverseerEvents([
        ccEvent({ atUtc: '2026-09-15T08:00:00Z', runId: 1, kind: 'WikiHeadSha' }),
        ccEvent({ atUtc: '2026-09-15T10:00:00Z', runId: 2, kind: 'ToolGuidesSha256' }),
        ccEvent({ atUtc: '2026-09-15T12:00:00Z', runId: 99, kind: 'SourceCodeHeadSha' })
      ], [ccPoint(1, '2026-09-15T08:00:00Z', { harnessVersion: '30' }), ccPoint(2, '2026-09-15T10:00:00Z', { harnessVersion: '31' })]);
      expect(twoHarnesses.map(g => `${g.tag} ${g.key} ${g.runIds.join(',')}`))
        .toEqual(['E1 2026-09-15|30 1,99', 'E2 2026-09-15|31 2']);
    });

    it('starts a new group for a harness bump on the same day', () => {
      const bumped = groupOverseerEvents([
        ccEvent({ atUtc: '2026-09-15T08:00:00Z', runId: 1, kind: 'KnowledgeBaseHeadSha' }),
        ccEvent({ atUtc: '2026-09-15T12:00:00Z', runId: 2, kind: CC_HARNESS_EVENT_KIND, from: '30', to: '31' })
      ], [ccPoint(1, '2026-09-15T08:00:00Z', { harnessVersion: '30' }), ccPoint(2, '2026-09-15T12:00:00Z', { harnessVersion: '31' })]);
      expect(bumped.map(g => g.title)).toEqual(['Changes under harness 30', 'Harness 30 → 31']);
    });

    it('leaves out events without a parsable time and returns nothing for no events', () => {
      const [group] = groupOverseerEvents([
        ccEvent({ atUtc: 'garbage', runId: 103 }),
        ccEvent({ atUtc: '2026-09-15T00:00:00Z', runId: 103 })
      ], [ccPoint(103, '2026-09-12T08:00:00Z')]);
      expect(group.events.length).toBe(1);
      expect(groupOverseerEvents([], ccEventPoints())).toEqual([]);
    });

    describe('with a numbering reference', () => {
      const tagged = (list: readonly { tag: string; key: string }[]) => list.map(g => `${g.tag} ${g.key}`);
      const onDays = (...days: string[]) => ccOverseerEvents().filter(e => days.some(day => e.atUtc.startsWith(day)));

      it('takes the tag of the reference group with the same key', () => {
        const span = groupOverseerEvents(onDays('2026-09-05', '2026-09-10'), ccEventPoints(), groups);
        expect(tagged(span)).toEqual(['E2 2026-09-05|28', 'E4 2026-09-10|29']);
        // Without the reference the span restarts at E1.
        expect(tagged(groupOverseerEvents(onDays('2026-09-05', '2026-09-10'), ccEventPoints())))
          .toEqual(['E1 2026-09-05|28', 'E2 2026-09-10|29']);
      });

      it('gives a group of unknown harness the tag of its day\'s earliest reference group', () => {
        // Run 297 has no point, so its corpus index change has no harness of its own.
        const [corpus] = groupOverseerEvents(ccOverseerEvents().filter(e => e.runId === 297), ccEventPoints(), groups);
        expect(`${corpus.tag} ${corpus.key}`).toBe('E4 2026-09-10|');

        const points = [ccPoint(1, '2026-09-15T08:00:00Z', { harnessVersion: '30' }), ccPoint(2, '2026-09-15T10:00:00Z', { harnessVersion: '31' })];
        const reference = groupOverseerEvents([
          ccEvent({ atUtc: '2026-09-15T10:00:00Z', runId: 2, kind: 'ToolGuidesSha256' }),
          ccEvent({ atUtc: '2026-09-15T08:00:00Z', runId: 1, kind: 'WikiHeadSha' })
        ], points);
        expect(tagged(reference)).toEqual(['E1 2026-09-15|30', 'E2 2026-09-15|31']);
        const [unknown] = groupOverseerEvents([ccEvent({ atUtc: '2026-09-15T12:00:00Z', runId: 99, kind: 'SourceCodeHeadSha' })], points, reference);
        expect(unknown.tag).toBe('E1');
      });

      it('numbers a group the reference lacks after the reference\'s last number, in time order', () => {
        const events = [
          ...onDays('2026-09-05'),
          // A control-series change on a day the reference has no composite on.
          ccEvent({ atUtc: '2026-09-07T09:00:00Z', runId: 501, kind: 'WikiHeadSha', inTargetSeries: false }),
          // A harness the reference never saw on 2026-09-03.
          ccEvent({ atUtc: '2026-09-03T06:00:00Z', runId: 502, kind: 'ToolGuidesSha256' })
        ];
        const points = [...ccEventPoints(), ccPoint(502, '2026-09-03T06:00:00Z', { harnessVersion: '26' })];
        const span = groupOverseerEvents(events, points, groups);
        expect(tagged(span)).toEqual(['E5 2026-09-03|26', 'E2 2026-09-05|28', 'E6 2026-09-07|']);
      });

      it('never gives one tag to two composites', () => {
        // A reference that repeats a tag: the second group to claim it is numbered after the reference.
        const reference = [groups[0], { ...groups[1], tag: 'E1' }];
        const span = groupOverseerEvents(onDays('2026-09-03', '2026-09-05'), ccEventPoints(), reference);
        expect(tagged(span)).toEqual(['E1 2026-09-03|27', 'E2 2026-09-05|28']);

        const mixed = groupOverseerEvents([
          ...ccOverseerEvents(),
          ccEvent({ atUtc: '2026-09-07T09:00:00Z', runId: 501, kind: 'WikiHeadSha' })
        ], ccEventPoints(), groups);
        const tags = mixed.map(g => g.tag);
        expect(new Set(tags).size).toBe(tags.length);
        expect(tags).toEqual(['E1', 'E2', 'E5', 'E3', 'E4']);
      });

      it('numbers as without a reference when the reference is empty', () => {
        const events = onDays('2026-09-05', '2026-09-10');
        expect(groupOverseerEvents(events, ccEventPoints(), [])).toEqual(groupOverseerEvents(events, ccEventPoints()));
      });
    });
  });

  describe('labels and summaries', () => {
    const groups = groupOverseerEvents(ccOverseerEvents(), ccEventPoints());

    it('labels a composite with its title, day, kinds and run count, the harness left out where the title names it', () => {
      expect(groups.map(eventGroupLabel)).toEqual([
        'Changes under harness 27 on 2026-09-03: System prompt, Knowledge base (2 runs)',
        'Harness 27 → 28 on 2026-09-05: Tool guides',
        'Harness 28 → 29 (re-run 30) on 2026-09-09: Wiki (2 runs)',
        'Changes under harness 29 on 2026-09-10: Source code, Corpus index (2 runs)'
      ]);
      const harnessOnly = groupOverseerEvents(
        [ccEvent({ atUtc: '2026-09-15T00:00:00Z', runId: 103, kind: CC_HARNESS_EVENT_KIND, from: '30', to: '31' })],
        [ccPoint(103, '2026-09-15T00:00:00Z', { harnessVersion: '31' })]);
      expect(eventGroupLabel(harnessOnly[0])).toBe('Harness 30 → 31 on 2026-09-15');
    });

    it('counts the composites that contain each kind, in kind order', () => {
      expect(eventKindSummary(groups)).toEqual([
        { kind: 'CandidateSystemPromptSha256', label: 'System prompt', count: 1 },
        { kind: 'ToolGuidesSha256', label: 'Tool guides', count: 1 },
        { kind: 'KnowledgeBaseHeadSha', label: 'Knowledge base', count: 1 },
        { kind: 'WikiHeadSha', label: 'Wiki', count: 1 },
        { kind: 'SourceCodeHeadSha', label: 'Source code', count: 1 },
        { kind: 'CorpusIndexFingerprintsJson', label: 'Corpus index', count: 1 },
        { kind: CC_HARNESS_EVENT_KIND, label: 'Harness', count: 2 }
      ]);
      expect(eventKindSummary([])).toEqual([]);
    });

    it('shows a composite while events show and any of its kinds is not hidden', () => {
      const e2 = groups[1];
      expect(eventGroupShown(e2)).toBe(true);
      expect(eventGroupShown(e2, filterOf(['event']))).toBe(true);
      expect(eventGroupShown(e2, filterOf(['annotation', 'served']))).toBe(false);
      expect(eventGroupShown(e2, filterOf(['event'], ['ToolGuidesSha256']))).toBe(true);
      expect(eventGroupShown(e2, filterOf(['event'], ['ToolGuidesSha256', CC_HARNESS_EVENT_KIND]))).toBe(false);
    });
  });

  describe('served-model changes', () => {
    it('tags the runs whose dominant served model differs from the previous one, S1 first', () => {
      expect(servedModelChanges(ccEventPoints())).toEqual([
        { tag: 'S1', atUtc: '2026-09-05T08:00:00Z', runId: 204, from: 'gpt-5-2026-08', to: 'gpt-5-2026-09' },
        { tag: 'S2', atUtc: '2026-09-08T08:00:00Z', runId: 205, from: 'gpt-5-2026-09', to: 'gpt-5-2026-08' }
      ]);
    });

    it('orders by start, skips runs without a served model or a parsable start, and compares with the latest run that had one', () => {
      const served = (modelId: string) => [{ modelId, callCount: 10 }];
      const changes = servedModelChanges([
        ccPoint(4, '2026-09-04T00:00:00Z', { servedModelIds: served('b') }),
        ccPoint(1, '2026-09-01T00:00:00Z', { servedModelIds: served('a') }),
        ccPoint(2, '2026-09-02T00:00:00Z', { servedModelIds: [] }),
        ccPoint(3, 'garbage', { servedModelIds: served('c') }),
        ccPoint(5, '2026-09-05T00:00:00Z', { servedModelIds: served('b') })
      ]);
      expect(changes).toEqual([{ tag: 'S1', atUtc: '2026-09-04T00:00:00Z', runId: 4, from: 'a', to: 'b' }]);
      expect(servedChangeLabel(changes[0])).toBe('Served model changed from a to b (run #4)');
    });
  });

  describe('annotations', () => {
    it('tags annotations A1… in time order, leaving out those without a parsable time', () => {
      const tagged = taggedAnnotations([...ccEventAnnotations(), ccAnnotation(13, { atUtc: 'garbage' })]);
      expect(tagged.map(t => `${t.tag} ${t.annotation.id}`)).toEqual(['A1 11', 'A2 12']);
    });

    it('keeps the input order for annotations at the same time', () => {
      const tagged = taggedAnnotations([ccAnnotation(2), ccAnnotation(1)]);
      expect(tagged.map(t => t.annotation.id)).toEqual([2, 1]);
    });
  });

  describe('buildEventDays', () => {
    const groups = groupOverseerEvents(ccOverseerEvents(), ccEventPoints());
    const annotations = ccEventAnnotations();
    const served = servedModelChanges(ccEventPoints());

    it('lists days and items oldest first, events before annotations before served changes at one time', () => {
      expect(dayLines(buildEventDays(groups, annotations, served))).toEqual([
        '2026-09-02: A1',
        '2026-09-03: E1',
        '2026-09-05: E2 A2 S1',
        '2026-09-08: S2',
        '2026-09-09: E3',
        '2026-09-10: E4'
      ]);
    });

    it('carries the group, the annotation and the served change on their items', () => {
      const days = buildEventDays(groups, annotations, served);
      const items = days.find(day => day.day === '2026-09-05')!.items;
      expect(items[0]).toEqual({ kind: 'event', group: groups[1] });
      expect(items[1]).toEqual({ kind: 'annotation', tag: 'A2', annotation: annotations[0] });
      expect(items[2]).toEqual({ kind: 'served', change: served[0] });
    });

    it('drops filtered marker kinds and never renumbers the rest', () => {
      expect(dayLines(buildEventDays(groups, annotations, served, filterOf(['annotation', 'served'])))).toEqual([
        '2026-09-02: A1',
        '2026-09-05: A2 S1',
        '2026-09-08: S2'
      ]);
      expect(dayLines(buildEventDays(groups, annotations, served, filterOf(['event', 'served'])))).toEqual([
        '2026-09-03: E1',
        '2026-09-05: E2 S1',
        '2026-09-08: S2',
        '2026-09-09: E3',
        '2026-09-10: E4'
      ]);
      expect(buildEventDays(groups, annotations, served, filterOf([]))).toEqual([]);
    });

    it('keeps a composite while any of its kinds is not hidden', () => {
      // E2 holds only tool guides and the harness; E3 also holds the wiki.
      const hidden = filterOf(['event', 'annotation', 'served'], ['ToolGuidesSha256', CC_HARNESS_EVENT_KIND]);
      expect(dayLines(buildEventDays(groups, annotations, served, hidden))).toEqual([
        '2026-09-02: A1',
        '2026-09-03: E1',
        '2026-09-05: A2 S1',
        '2026-09-08: S2',
        '2026-09-09: E3',
        '2026-09-10: E4'
      ]);
      const wikiHidden = filterOf(['event'], ['WikiHeadSha']);
      expect(dayLines(buildEventDays(groups, annotations, served, wikiHidden))).toEqual([
        '2026-09-03: E1',
        '2026-09-05: E2',
        '2026-09-09: E3',
        '2026-09-10: E4'
      ]);
    });

    it('is empty with nothing to list', () => {
      expect(buildEventDays([], [], [])).toEqual([]);
    });
  });

  it('names the weekday of a UTC day', () => {
    expect(utcWeekday('2026-10-07')).toBe('Wednesday');
    expect(utcWeekday('2026-09-05')).toBe('Saturday');
    expect(utcWeekday('garbage')).toBe('');
  });
});
