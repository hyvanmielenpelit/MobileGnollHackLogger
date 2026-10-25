import type { BenchmarkComparabilityTier, BenchmarkRunGroupDto } from '../../../services/admin-benchmark.service';

/** A tier as the DTOs carry it; an unknown or missing value reads as not comparable. */
type TierValue = BenchmarkComparabilityTier | string | null | undefined;

const ISO_DATE_SEGMENT = /^\d{4}-\d{2}-\d{2}$/;
const RUN_COUNT_SEGMENT = /^R=\d+$/;

/**
 * Whether an analysis group's name only repeats facts its row already shows, so it can be left
 * unrendered: every `' · '`-separated segment is the suite name, the model name, an ISO date or a
 * run count. That covers the server's generator, `BenchmarkRunGroupNames.Default`, which names a
 * group created without a name `Suite · Model · yyyy-MM-dd · R=n`, and the shorter `Suite · R=n`
 * of older groups; a third generator must produce the same segments or its names are shown.
 * Anything a user typed fails the test.
 */
export function isGeneratedGroupName(
  name: string,
  suiteName: string | null | undefined,
  modelName: string | null | undefined
): boolean {
  const trimmed = name.trim();
  if (trimmed === '') {
    return false;
  }
  const suite = suiteName?.trim() ?? '';
  const model = modelName?.trim() ?? '';
  return trimmed.split(' · ').every(part => {
    const segment = part.trim();
    return segment !== ''
      && ((suite !== '' && segment === suite)
        || (model !== '' && segment === model)
        || ISO_DATE_SEGMENT.test(segment)
        || RUN_COUNT_SEGMENT.test(segment));
  });
}

/** `Tier A — Replicate`, `Tier B — Quality-comparable`, `Tier C — Cross-condition` or `Not comparable`. */
export function runGroupTierName(tier: TierValue): string {
  switch (tier) {
    case 'Replicate': return 'Tier A — Replicate';
    case 'QualityComparable': return 'Tier B — Quality-comparable';
    case 'CrossCondition': return 'Tier C — Cross-condition';
    default: return 'Not comparable';
  }
}

/** The tier badge's modifier class. Color reinforces the tier; the word carries it. */
export function runGroupTierClass(tier: TierValue): string {
  switch (tier) {
    case 'Replicate': return 'tier-replicate';
    case 'QualityComparable': return 'tier-quality';
    case 'CrossCondition': return 'tier-cross';
    default: return 'tier-none';
  }
}

/**
 * The comparability enum's own numeric order, mirroring `BenchmarkComparabilityTier` on the
 * server: `NotComparable` 0 through `Replicate` 3, so a descending sort lists Tier A first. A sort
 * on the label would hold only while the letters happen to match the order.
 */
export function runGroupTierOrder(tier: TierValue): number {
  switch (tier) {
    case 'Replicate': return 3;
    case 'QualityComparable': return 2;
    case 'CrossCondition': return 1;
    default: return 0;
  }
}

/** Whether a group's stored analysis describes it: `current`, `stale`, or `none` when it has none. */
export type RunGroupAnalysisState = 'current' | 'stale' | 'none';

/**
 * A group with no analysis is `none`; one whose latest analysis no longer describes its members
 * (the membership changed, or a member was re-run after it) is `stale` rather than `current`.
 */
export function runGroupAnalysisState(
  group: Pick<BenchmarkRunGroupDto, 'latestAnalysisId' | 'analysisStale'>
): RunGroupAnalysisState {
  if (group.latestAnalysisId == null) return 'none';
  return group.analysisStale ? 'stale' : 'current';
}

/** `Current`, `Stale` or `Not analyzed`. */
export function runGroupAnalysisStateLabel(state: RunGroupAnalysisState): string {
  switch (state) {
    case 'current': return 'Current';
    case 'stale': return 'Stale';
    default: return 'Not analyzed';
  }
}

/** `#41 #42 #43`; empty for no runs. */
export function groupRunsLabel(ids: readonly number[] | null | undefined): string {
  return (ids ?? []).map(id => `#${id}`).join(' ');
}
