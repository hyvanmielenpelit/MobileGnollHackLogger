import type { SystemConfigBlockerDto } from '../../services/admin.service';

/** How a model row relates to the model catalog, as the server reports it. */
export type ModelAvailabilityStatus = 'available' | 'custom' | 'customEndpoint' | 'retired' | 'notInCatalog';

/** The persisted catalog mode of a user model or system configuration. */
export type ModelCatalogMode = 'catalog' | 'custom';

export interface ModelReplacement {
  modelId: string;
  displayName: string;
}

/** Values "Keep as custom model" is prefilled with. */
export interface SuggestedCustomSettings {
  maxInputTokens?: number | null;
  maxOutputTokens?: number | null;
  inputPricePerMillion?: number | null;
  outputPricePerMillion?: number | null;
  cachedInputPricePerMillion?: number | null;
}

/** Whether a user model or system configuration still names a model the catalog offers. */
export interface ModelAvailability {
  status: ModelAvailabilityStatus;
  /** True for `retired` and `notInCatalog`. */
  needsAttention: boolean;
  /** yyyy-MM-dd, the day the provider withdrew a retired model. */
  retiredOn?: string | null;
  note?: string | null;
  /** The retired catalog entry's display name. */
  catalogDisplayName?: string | null;
  /** The retired entry's replacement, only when the catalog offers it. */
  replacement?: ModelReplacement | null;
  suggestedCustom?: SuggestedCustomSettings | null;
}

export type ModelResolutionAction = 'switch' | 'keepCustom';

export interface ModelResolutionRequest {
  action: ModelResolutionAction;
  /** The catalog model ID to switch to; `switch` only. */
  targetModelId?: string;
  maxInputTokens?: number;
  maxOutputTokens?: number;
  inputPricePerMillion?: number;
  outputPricePerMillion?: number;
  cachedInputPricePerMillion?: number;
  /** True computes the changes without saving them. */
  dryRun: boolean;
}

/** One field a resolution changes, named as the model form labels it. */
export interface ModelResolutionChange {
  field: string;
  from?: string | null;
  to?: string | null;
  note?: string | null;
}

/**
 * What a resolution changed, or would change on a dry run. `model` is the row after the change:
 * a `UserAiModel` from the user endpoint, a `SystemAiConfigDto` from the system endpoint. A user
 * model deleted from `app-model-resolution-dialog` is reported as `deleted: true` with no
 * changes, no blockers and a null `model` (`isResolutionDeletion`); the server never sets
 * `deleted`.
 */
export interface ModelResolutionResult<M = unknown> {
  changes: ModelResolutionChange[];
  /** Work using the system configuration right now, which the change would affect. */
  blockers: SystemConfigBlockerDto[];
  model: M | null;
  deleted?: boolean;
}

/** The body of a refused resolution: 400 `{ message }`, or 409 `{ message, blockers }` for a blocked switch. */
export interface ModelResolutionRefusal {
  message?: string | null;
  blockers?: SystemConfigBlockerDto[] | null;
}

/** A catalog model a row can be switched to. */
export interface CatalogTarget {
  modelId: string;
  displayName: string;
  /** yyyy-MM-dd. */
  releaseDate: string;
  thinkingLevels: string[];
  contextWindowSize: number;
  maxOutputTokens: number;
  inputPerMillion?: number | null;
  outputPerMillion?: number | null;
}

/** The chip a model picker shows beside a model that needs attention. */
export interface AvailabilityChip {
  text: 'Removed' | 'Not in catalog';
  tone: 'warning' | 'info';
}

export function needsAttention(availability: ModelAvailability | null | undefined): boolean {
  return !!availability?.needsAttention;
}

export function availabilityChip(availability: ModelAvailability | null | undefined): AvailabilityChip | null {
  switch (availability?.status) {
    case 'retired':
      return { text: 'Removed', tone: 'warning' };
    case 'notInCatalog':
      return { text: 'Not in catalog', tone: 'info' };
    default:
      return null;
  }
}

const LONG_DATE = new Intl.DateTimeFormat('en-US', { year: 'numeric', month: 'long', day: 'numeric', timeZone: 'UTC' });

/** A yyyy-MM-dd catalog date as a fixed en-US long date ("September 30, 2026"); anything else unchanged. */
export function formatCatalogDate(date: string | null | undefined): string {
  if (!date) return '';
  const match = /^(\d{4})-(\d{2})-(\d{2})$/.exec(date.trim());
  if (!match) return date;
  const value = new Date(Date.UTC(Number(match[1]), Number(match[2]) - 1, Number(match[3])));
  return Number.isNaN(value.getTime()) ? date : LONG_DATE.format(value);
}

/**
 * The one sentence every surface uses for a model that needs attention, or '' when it needs none.
 * Retired: "{modelName} was removed from the model catalog on {date}. {note}". Not in catalog:
 * "{modelId} isn't in Overseer's model catalog, so its limits and price aren't known.", where
 * `modelId` falls back to `modelName` when absent.
 */
export function availabilitySentence(
  availability: ModelAvailability | null | undefined,
  modelName: string,
  modelId?: string | null
): string {
  switch (availability?.status) {
    case 'retired': {
      const date = formatCatalogDate(availability.retiredOn);
      const removed = date
        ? `${modelName} was removed from the model catalog on ${date}.`
        : `${modelName} was removed from the model catalog.`;
      const note = availability.note?.trim();
      return note ? `${removed} ${note}` : removed;
    }
    case 'notInCatalog':
      return `${modelId || modelName} isn't in Overseer's model catalog, so its limits and price aren't known.`;
    default:
      return '';
  }
}

/** True for the result `app-model-resolution-dialog` emits after deleting a user model. */
export function isResolutionDeletion(result: ModelResolutionResult | null | undefined): boolean {
  return !!result && (result.deleted === true || (result.model === null && result.changes.length === 0 && result.blockers.length === 0));
}

/** A UTC timestamp as "yyyy-MM-dd HH:mm UTC"; an offset-less value is read as UTC. */
export function formatUtcMinute(value: string | null | undefined): string {
  if (!value) return '';
  const hasOffset = /(Z|[+-]\d{2}:?\d{2})$/i.test(value);
  const parsed = new Date(hasOffset ? value : `${value}Z`);
  if (Number.isNaN(parsed.getTime())) return value;
  return `${parsed.toISOString().slice(0, 16).replace('T', ' ')} UTC`;
}

/** "Run #12 — as the assessor and the claim verifier, started 2026-10-10 09:40 UTC." */
export function blockerText(blocker: SystemConfigBlockerDto): string {
  const named = (blocker.roles ?? []).map(role => `the ${role}`);
  const roles = named.length === 0
    ? ''
    : named.length === 1
      ? `as ${named[0]}`
      : `as ${named.slice(0, -1).join(', ')} and ${named[named.length - 1]}`;
  const started = formatUtcMinute(blocker.startedAtUtc);
  let text = blocker.label;
  if (roles) text += ` — ${roles}`;
  if (started) text += `, started ${started}`;
  return `${text}.`;
}
