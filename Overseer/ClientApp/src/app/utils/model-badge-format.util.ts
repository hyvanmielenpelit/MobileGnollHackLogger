/**
 * Formats a model's thinking level (`low` / `medium` / `high`, or unset) for the thinking badge
 * next to a model name. Title-cased; unset reads as the model's own default rather than as absent.
 */
export function formatThinkingLevel(level: string | null | undefined): string {
  if (!level) return 'Default';
  return level.charAt(0).toUpperCase() + level.slice(1);
}

/**
 * Whether a model's reasoning mode is worth its own badge. `default` and `standard` are the
 * providers' own baseline behaviour, so a badge for either would say nothing a reader does not
 * already assume.
 */
export function showReasoningBadge(mode: string | null | undefined): boolean {
  if (!mode) return false;
  const lower = mode.toLowerCase();
  return lower !== 'default' && lower !== 'standard';
}

/**
 * Formats a requested service tier for display. `standard_only` is the one tier whose API name
 * does not title-case into something readable on its own.
 */
export function formatServiceTier(tier: string | null | undefined): string {
  if (!tier) return 'None';
  if (tier.toLowerCase() === 'standard_only') return 'Standard Only';
  return tier.charAt(0).toUpperCase() + tier.slice(1);
}

/**
 * Formats a `BenchmarkDifficulty` value, accepted as either its numeric enum value or its name,
 * into the band label shown throughout the benchmark views.
 */
export function formatDifficulty(diff: string | number): string {
  if (diff === 1 || diff === 'Simple') return 'Simple';
  if (diff === 2 || diff === 'Intermediate') return 'Intermediate';
  if (diff === 3 || diff === 'Advanced') return 'Advanced';
  return String(diff);
}
