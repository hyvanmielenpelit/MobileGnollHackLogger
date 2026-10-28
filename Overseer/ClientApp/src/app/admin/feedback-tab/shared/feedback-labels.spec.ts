import {
  FEEDBACK_CATEGORY_LABELS,
  FEEDBACK_NOTICED_VIA_LABELS,
  FEEDBACK_STATUS_LABELS,
  FEEDBACK_SUSPECTED_CAUSE_LABELS,
  RESOLVED_VIA_OPTIONS,
  feedbackCategoryLabel,
  feedbackCauseLabel,
  feedbackNoticedViaLabel,
  feedbackStatusLabel,
  formatResolvedVia,
  labelFrom,
  resolvedViaLabel
} from './feedback-labels';
import { FEEDBACK_RESOLVED_VIA_IDS } from '../../../services/feedback-transfer.service';

describe('feedback labels', () => {
  describe('labelFrom', () => {
    const map = { a: 'Alpha' };

    it('reads a value of the map, the value itself otherwise, and nothing for no value', () => {
      expect(labelFrom(map, 'a')).toBe('Alpha');
      expect(labelFrom(map, 'b')).toBe('b');
      expect(labelFrom(map, 'b', v => v.toUpperCase())).toBe('B');
      expect(labelFrom(map, null)).toBe('');
      expect(labelFrom(map, '')).toBe('');
    });

    it('never reads an inherited property as a label', () => {
      expect(labelFrom(map, 'constructor')).toBe('constructor');
      expect(labelFrom(map, '__proto__')).toBe('__proto__');
      expect(labelFrom(map, 'toString')).toBe('toString');
    });
  });

  describe('status', () => {
    it('labels every status', () => {
      expect(FEEDBACK_STATUS_LABELS).toEqual({
        new: 'New', acknowledged: 'Acknowledged', resolved: 'Resolved', regressed: 'Regressed', dismissed: 'Dismissed'
      });
      expect(feedbackStatusLabel('resolved')).toBe('Resolved');
      expect(feedbackStatusLabel('regressed')).toBe('Regressed');
    });

    it('accepts any case, shows an unknown value as it came and no status as (none)', () => {
      expect(feedbackStatusLabel('Resolved')).toBe('Resolved');
      expect(feedbackStatusLabel('DISMISSED')).toBe('Dismissed');
      expect(feedbackStatusLabel('Pending')).toBe('Pending');
      expect(feedbackStatusLabel(null)).toBe('(none)');
      expect(feedbackStatusLabel(undefined)).toBe('(none)');
      expect(feedbackStatusLabel('')).toBe('(none)');
      expect(feedbackStatusLabel('constructor')).toBe('constructor');
    });
  });

  describe('category, suspected cause and noticed via', () => {
    it('labels every category', () => {
      expect(FEEDBACK_CATEGORY_LABELS).toEqual({
        missing_information: 'Missing information', tool_problem: 'Tool problem', own_mistake: 'Own mistake'
      });
      expect(feedbackCategoryLabel('tool_problem')).toBe('Tool problem');
      expect(feedbackCategoryLabel('new_kind')).toBe('new_kind');
      expect(feedbackCategoryLabel(null)).toBe('');
    });

    it('labels every suspected cause', () => {
      expect(Object.keys(FEEDBACK_SUSPECTED_CAUSE_LABELS)).toEqual([
        'did_not_verify', 'misread_tool_result', 'training_knowledge_conflict', 'search_strategy',
        'corpus_or_tool_limit', 'ambiguous_question', 'answered_too_quickly', 'other'
      ]);
      expect(feedbackCauseLabel('misread_tool_result')).toBe('Misread a tool result');
      expect(feedbackCauseLabel('training_knowledge_conflict')).toBe('Conflict with training knowledge');
      expect(feedbackCauseLabel('other')).toBe('Other');
      expect(feedbackCauseLabel(undefined)).toBe('');
      expect(feedbackCauseLabel('__proto__')).toBe('__proto__');
    });

    it('labels every noticed-via value', () => {
      expect(FEEDBACK_NOTICED_VIA_LABELS).toEqual({
        user_correction: 'The user corrected it', tool_result: 'A tool result contradicted it', self_review: 'Its own review'
      });
      expect(feedbackNoticedViaLabel('tool_result')).toBe('A tool result contradicted it');
      expect(feedbackNoticedViaLabel('hunch')).toBe('hunch');
      expect(feedbackNoticedViaLabel(null)).toBe('');
    });
  });

  describe('resolved via', () => {
    it('offers every id in the canonical order, each with its label', () => {
      expect(RESOLVED_VIA_OPTIONS.map(o => o.id)).toEqual([...FEEDBACK_RESOLVED_VIA_IDS]);
      expect(RESOLVED_VIA_OPTIONS.map(o => o.label)).toEqual([
        'GnollHack wiki', 'Knowledge base', 'Overseer release', 'GnollHack release', 'Server configuration', 'Other'
      ]);
    });

    it('labels an id, an unknown id in words, and no id as nothing', () => {
      expect(resolvedViaLabel('wiki')).toBe('GnollHack wiki');
      expect(resolvedViaLabel('server_configuration')).toBe('Server configuration');
      expect(resolvedViaLabel('mobile_release')).toBe('Mobile release');
      expect(resolvedViaLabel(null)).toBe('');
      expect(resolvedViaLabel('constructor')).toBe('Constructor');
      expect(resolvedViaLabel('__proto__')).toBe('Proto');
    });

    it('formats a list in labels, in the canonical order, without duplicates', () => {
      expect(formatResolvedVia(['wiki', 'knowledge_base'])).toBe('GnollHack wiki, Knowledge base');
      expect(formatResolvedVia(['other', 'knowledge_base', 'wiki', 'wiki'])).toBe('GnollHack wiki, Knowledge base, Other');
      expect(formatResolvedVia(['mobile_release', 'overseer_release'])).toBe('Overseer release, Mobile release');
    });

    it('formats an empty or missing list as None', () => {
      expect(formatResolvedVia([])).toBe('None');
      expect(formatResolvedVia(null)).toBe('None');
      expect(formatResolvedVia(undefined)).toBe('None');
      expect(formatResolvedVia([' '])).toBe('None');
    });
  });
});
