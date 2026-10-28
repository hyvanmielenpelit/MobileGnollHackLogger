import { FEEDBACK_RESOLVED_VIA_IDS, FeedbackResolvedVia } from '../../../services/feedback-transfer.service';
import { wordsLabel } from '../../../utils/display-labels.util';

/**
 * The Feedback tab's own label maps, shared by both sub-tabs, the triage dialog and the import dialog. The wire
 * values stay the ids; only the UI shows these labels.
 */

export const FEEDBACK_STATUS_LABELS: Readonly<Record<string, string>> = {
  new: 'New',
  acknowledged: 'Acknowledged',
  resolved: 'Resolved',
  regressed: 'Regressed',
  dismissed: 'Dismissed'
};

export const FEEDBACK_CATEGORY_LABELS: Readonly<Record<string, string>> = {
  missing_information: 'Missing information',
  tool_problem: 'Tool problem',
  own_mistake: 'Own mistake'
};

export const FEEDBACK_SUSPECTED_CAUSE_LABELS: Readonly<Record<string, string>> = {
  did_not_verify: 'Did not verify',
  misread_tool_result: 'Misread a tool result',
  training_knowledge_conflict: 'Conflict with training knowledge',
  search_strategy: 'Search strategy',
  corpus_or_tool_limit: 'Corpus or tool limit',
  ambiguous_question: 'Ambiguous question',
  answered_too_quickly: 'Answered too quickly',
  other: 'Other'
};

export const FEEDBACK_NOTICED_VIA_LABELS: Readonly<Record<string, string>> = {
  user_correction: 'The user corrected it',
  tool_result: 'A tool result contradicted it',
  self_review: 'Its own review'
};

/** The *Resolved via* choices in the canonical order, each with its label. */
export const RESOLVED_VIA_OPTIONS: readonly { readonly id: FeedbackResolvedVia; readonly label: string }[] = [
  { id: 'wiki', label: 'GnollHack wiki' },
  { id: 'knowledge_base', label: 'Knowledge base' },
  { id: 'overseer_release', label: 'Overseer release' },
  { id: 'gnollhack_release', label: 'GnollHack release' },
  { id: 'server_configuration', label: 'Server configuration' },
  { id: 'other', label: 'Other' }
];

const RESOLVED_VIA_LABELS: Readonly<Record<string, string>> =
  Object.fromEntries(RESOLVED_VIA_OPTIONS.map(option => [option.id, option.label]));

/**
 * A value of a fixed label map, or `fallback(value)` (by default the value itself); never an inherited property.
 * Empty for an empty value.
 */
export function labelFrom(
  map: Readonly<Record<string, string>>,
  value: string | null | undefined,
  fallback: (value: string) => string = v => v
): string {
  if (!value) return '';
  return Object.hasOwn(map, value) ? map[value] : fallback(value);
}

/** An issue status in words, in any case; `(none)` for no status, and an unknown wire value as it came. */
export function feedbackStatusLabel(status: string | null | undefined): string {
  if (!status) return '(none)';
  return labelFrom(FEEDBACK_STATUS_LABELS, status.toLowerCase(), () => status);
}

/** A report category in words; an unknown value as it came, empty for none. */
export function feedbackCategoryLabel(category: string | null | undefined): string {
  return labelFrom(FEEDBACK_CATEGORY_LABELS, category);
}

/** A suspected cause in words; an unknown value as it came, empty for none. */
export function feedbackCauseLabel(cause: string | null | undefined): string {
  return labelFrom(FEEDBACK_SUSPECTED_CAUSE_LABELS, cause);
}

/** How the model noticed its mistake, in words; an unknown value as it came, empty for none. */
export function feedbackNoticedViaLabel(noticedVia: string | null | undefined): string {
  return labelFrom(FEEDBACK_NOTICED_VIA_LABELS, noticedVia);
}

/** A *Resolved via* id's label (`wiki` reads *GnollHack wiki*); an unknown id reads in words, empty for none. */
export function resolvedViaLabel(id: string | null | undefined): string {
  return labelFrom(RESOLVED_VIA_LABELS, id, wordsLabel);
}

/**
 * A *Resolved via* list in labels, in the canonical order with unknown ids last and duplicates dropped:
 * *GnollHack wiki, Knowledge base*. `None` for an empty or missing list.
 */
export function formatResolvedVia(ids: readonly string[] | null | undefined): string {
  const order = (id: string): number => {
    const index = (FEEDBACK_RESOLVED_VIA_IDS as readonly string[]).indexOf(id);
    return index < 0 ? FEEDBACK_RESOLVED_VIA_IDS.length : index;
  };
  const unique = [...new Set((ids ?? []).map(id => id.trim()).filter(id => id !== ''))];
  if (unique.length === 0) return 'None';
  return unique
    .map((id, position) => ({ id, position }))
    .sort((a, b) => order(a.id) - order(b.id) || a.position - b.position)
    .map(entry => resolvedViaLabel(entry.id))
    .join(', ');
}
