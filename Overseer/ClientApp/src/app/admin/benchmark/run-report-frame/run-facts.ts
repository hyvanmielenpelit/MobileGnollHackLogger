/**
 * One description of a run's settings: rendered by `app-run-facts` in the run report's header and
 * drawn by `key-figures-image.ts` in the key-figures images, so the two say the same thing.
 *
 * No Angular DI: `formatDate` from `@angular/common` is a plain function.
 */

import { formatDate } from '@angular/common';

import type { BenchmarkRunDetailDto } from '../../../services/admin-benchmark.service';
import { formatServiceTier, formatThinkingLevel, showReasoningBadge } from '../../../utils/model-badge-format.util';

/** One model as a run used it, with the settings its badges show. */
export interface RunFactModel {
  /** `A` / `B` for a panel member; absent otherwise. */
  readonly role?: 'A' | 'B';
  /** Display name, else model id, else `not recorded`. */
  readonly name: string;
  readonly provider: string | null;
  readonly thinkingLevel: string | null;
  readonly reasoningMode: string | null;
  /** The model under test only. */
  readonly serviceTier: string | null;
  /** The model under test only. */
  readonly customEndpoint: boolean;
}

export type RunFactBadgeKind = 'thinking' | 'reasoning' | 'provider' | 'tier' | 'endpoint';

export interface RunFactBadge {
  readonly kind: RunFactBadgeKind;
  /** What is shown: `High`, `OpenAI`, `Flex`, `Custom endpoint`. */
  readonly text: string;
  /** The visually hidden lead-in, as the model pickers read their badges. */
  readonly srPrefix: string;
  /** For kind `provider`. */
  readonly provider?: string;
}

/** One grading role's board delivery: `Assessor 18/18`. */
export interface BoardFigure {
  readonly role: string;
  readonly delivered: number;
  readonly total: number;
}

export type RunFactItem =
  | { readonly kind: 'models'; readonly models: readonly RunFactModel[] }
  | { readonly kind: 'prompt'; readonly name: string; readonly tags: readonly string[]; readonly summary: string }
  | { readonly kind: 'text'; readonly text: string }
  | { readonly kind: 'time'; readonly iso: string; readonly text: string }
  | { readonly kind: 'board'; readonly figures: readonly BoardFigure[]; readonly note: string; readonly gaps: readonly string[] };

/** Stable keys: the image-detail selection is stored by them. */
export const RUN_FACT_KEYS = ['model', 'assessor', 'prompt', 'profile', 'started', 'board'] as const;
export type RunFactKey = typeof RUN_FACT_KEYS[number];

/** The rows the run report header always shows; the rest sit in its Run details disclosure. */
export const RUN_FACT_PRIMARY_KEYS = ['model', 'assessor'] as const;

export interface RunFactRow {
  readonly key: RunFactKey;
  readonly label: string;
  readonly item: RunFactItem;
}

/** The candidate prompt's options as the run recorded them, or the prompt source when they do not parse. */
export type CandidatePromptParts =
  | {
    readonly name: string;
    readonly style: 'concise' | 'detailed';
    readonly tools: 'tools on' | 'tools off';
    readonly snapshot: boolean;
  }
  | { readonly fallback: string };

const BOARD_NOTE = 'Synthesis: yes · Difficulty assessment: digest (no map)';

/**
 * A model's badges, in order: thinking level (only when one is configured), reasoning mode (only
 * when not `default` or `standard`), provider, requested service tier, custom endpoint. The model
 * pickers' rules, so a model reads the same in a picker, the header and the image.
 */
export function runFactBadges(model: RunFactModel): RunFactBadge[] {
  const badges: RunFactBadge[] = [];
  if (model.thinkingLevel) {
    badges.push({ kind: 'thinking', text: formatThinkingLevel(model.thinkingLevel), srPrefix: 'thinking level ' });
  }
  if (showReasoningBadge(model.reasoningMode)) {
    badges.push({ kind: 'reasoning', text: model.reasoningMode!, srPrefix: 'reasoning mode ' });
  }
  if (model.provider) {
    badges.push({ kind: 'provider', text: model.provider, srPrefix: '', provider: model.provider });
  }
  if (model.serviceTier) {
    badges.push({ kind: 'tier', text: formatServiceTier(model.serviceTier), srPrefix: 'service tier ' });
  }
  if (model.customEndpoint) {
    badges.push({ kind: 'endpoint', text: 'Custom endpoint', srPrefix: '' });
  }
  return badges;
}

/** Parses the run's candidate prompt options; null when the run recorded none. */
export function candidatePromptParts(
  run: { candidatePromptOptionsJson?: string | null; candidatePromptSourceUsed?: string | null } | null | undefined
): CandidatePromptParts | null {
  if (!run?.candidatePromptOptionsJson) return null;
  try {
    const opts = JSON.parse(run.candidatePromptOptionsJson);
    const style = opts.verboseMode ? 'detailed' : 'concise';
    const tools = opts.enableToolUse !== false ? 'tools on' : 'tools off';
    return { name: 'Gameplay Help', style, tools, snapshot: !!opts.hasGameSnapshot };
  } catch {
    return { fallback: run.candidatePromptSourceUsed || 'ChatService.BuildSystemPrompt' };
  }
}

/** `Gameplay Help · concise (tools on) · snapshot`, the fallback source, or null. */
export function formatCandidatePrompt(parts: CandidatePromptParts | null): string | null {
  if (!parts) return null;
  if ('fallback' in parts) return parts.fallback;
  return `${parts.name} · ${parts.style} (${parts.tools})${parts.snapshot ? ' · snapshot' : ''}`;
}

/**
 * The board delivery per recorded grading role, in the order assessor, co-assessor (panel only),
 * second or reference reader, claim verifier. A role with no record is left out.
 */
export function boardDeliveryFigures(run: BenchmarkRunDetailDto | null | undefined): BoardFigure[] {
  const figures = run?.boardDelivery ?? [];
  if (figures.length === 0) {
    return [];
  }
  const roles: { label: string; roles: string[] }[] = [{ label: 'Assessor', roles: ['assessor'] }];
  if (run?.isPanelRun) {
    roles.push({ label: 'Co-assessor', roles: ['co-assessor'] });
  }
  roles.push(
    { label: run?.isPanelRun ? 'Reference reader' : 'Second reader', roles: ['second reader', 'reference reader'] },
    { label: 'Claim verifier', roles: ['claim verifier'] }
  );
  const result: BoardFigure[] = [];
  for (const entry of roles) {
    const figure = figures.find(f => entry.roles.includes(f.role));
    if (figure) {
      result.push({ role: entry.label, delivered: figure.delivered, total: figure.total });
    }
  }
  return result;
}

/** A row's value as one line of plain text, for the key-figures chooser. */
export function runFactPlainText(row: RunFactRow): string {
  const item = row.item;
  switch (item.kind) {
    case 'models':
      return item.models.map(model => model.name).join(' + ');
    case 'prompt':
      return item.summary;
    case 'text':
    case 'time':
      return item.text;
    case 'board':
      return item.figures.map(f => `${f.role} ${f.delivered}/${f.total}`).join(' · ');
  }
}

/**
 * The secondary rows as one line, joined by ` · `: the prompt name without its tags, the scoring
 * profile, `Started {text}` and `Board {delivered}/{total}`, or `Board incomplete` while any figure
 * falls short. Complete figures that differ read as the largest, the question count. Rows that are absent,
 * and the model and assessor rows, contribute nothing.
 */
export function runFactsReadout(rows: readonly RunFactRow[]): string {
  const parts: string[] = [];
  for (const row of rows) {
    const item = row.item;
    switch (row.key) {
      case 'prompt':
        if (item.kind === 'prompt') {
          parts.push(item.name);
        }
        break;
      case 'profile':
        if (item.kind === 'text') {
          parts.push(item.text);
        }
        break;
      case 'started':
        if (item.kind === 'text' || item.kind === 'time') {
          parts.push(`Started ${item.text}`);
        }
        break;
      case 'board':
        if (item.kind === 'board' && item.figures.length > 0) {
          const complete = item.figures.every(f => f.delivered === f.total);
          if (complete) {
            const total = Math.max(...item.figures.map(f => f.total));
            parts.push(`Board ${total}/${total}`);
          } else {
            parts.push('Board incomplete');
          }
        }
        break;
    }
  }
  return parts.join(' · ');
}

/** An ISO timestamp with a zone designator; the server stores UTC, so a bare one is UTC. */
function utcIso(value: string): string {
  return /(?:Z|[+-]\d{2}:?\d{2})$/i.test(value) ? value : `${value}Z`;
}

/** The start time as `yyyy-MM-dd HH:mm:ss UTC`; a missing one reads `not recorded`, an unparsable one as given. */
function startedItem(startedAtUtc: string | null | undefined): RunFactItem {
  if (!startedAtUtc) {
    return { kind: 'text', text: 'not recorded' };
  }
  try {
    return { kind: 'time', iso: utcIso(startedAtUtc), text: `${formatDate(startedAtUtc, 'yyyy-MM-dd HH:mm:ss', 'en-US')} UTC` };
  } catch {
    return { kind: 'text', text: startedAtUtc };
  }
}

function modelName(displayName: string | null | undefined, modelId: string | null | undefined): string {
  return displayName || modelId || 'not recorded';
}

/**
 * The run's settings as the header lists them: Model, Assessor(s), Prompt, Scoring profile,
 * Started, Board. Prompt only when the run recorded prompt options; Board only when it recorded
 * board delivery. `gaps` are the roles graded without the board, worded by the caller.
 */
export function buildRunFacts(run: BenchmarkRunDetailDto, options: { gaps: readonly string[] }): RunFactRow[] {
  const rows: RunFactRow[] = [];

  rows.push({
    key: 'model',
    label: 'Model',
    item: {
      kind: 'models',
      models: [{
        name: modelName(run.testedModelDisplayNameUsed, run.testedModelIdUsed),
        provider: run.testedModelProviderUsed || null,
        thinkingLevel: run.testedModelThinkingLevelUsed ?? null,
        reasoningMode: run.testedModelReasoningModeUsed ?? null,
        serviceTier: run.testedModelServiceTierUsed ?? null,
        customEndpoint: !!run.testedModelEndpoint && run.testedModelEndpoint !== 'official'
      }]
    }
  });

  const assessor: RunFactModel = {
    name: modelName(run.assessorModelDisplayNameUsed, run.assessorModelIdUsed),
    provider: run.assessorModelProviderUsed || null,
    thinkingLevel: run.assessorModelThinkingLevelUsed ?? null,
    reasoningMode: run.assessorModelReasoningModeUsed ?? null,
    serviceTier: null,
    customEndpoint: false
  };
  if (run.isPanelRun) {
    rows.push({
      key: 'assessor',
      label: 'Assessors',
      item: {
        kind: 'models',
        models: [
          { ...assessor, role: 'A' },
          {
            role: 'B',
            name: modelName(run.coAssessorModelDisplayNameUsed, run.coAssessorModelIdUsed),
            provider: run.coAssessorModelProviderUsed || null,
            thinkingLevel: run.coAssessorModelThinkingLevelUsed ?? null,
            reasoningMode: run.coAssessorModelReasoningModeUsed ?? null,
            serviceTier: null,
            customEndpoint: false
          }
        ]
      }
    });
  } else {
    rows.push({ key: 'assessor', label: 'Assessor', item: { kind: 'models', models: [assessor] } });
  }

  const prompt = candidatePromptParts(run);
  if (prompt) {
    rows.push({
      key: 'prompt',
      label: 'Prompt',
      item: 'fallback' in prompt
        ? { kind: 'prompt', name: prompt.fallback, tags: [], summary: prompt.fallback }
        : {
          kind: 'prompt',
          name: prompt.name,
          tags: [prompt.style, prompt.tools, ...(prompt.snapshot ? ['snapshot'] : [])],
          summary: formatCandidatePrompt(prompt)!
        }
    });
  }

  rows.push({ key: 'profile', label: 'Scoring profile', item: { kind: 'text', text: run.scoringProfileName ?? 'Default' } });

  rows.push({ key: 'started', label: 'Started', item: startedItem(run.startedAtUtc) });

  const figures = boardDeliveryFigures(run);
  if (figures.length > 0) {
    rows.push({ key: 'board', label: 'Board', item: { kind: 'board', figures, note: BOARD_NOTE, gaps: [...options.gaps] } });
  }

  return rows;
}
