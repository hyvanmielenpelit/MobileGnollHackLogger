import {
  groupRunsLabel,
  runGroupAnalysisState,
  runGroupAnalysisStateLabel,
  runGroupTierClass,
  runGroupTierName,
  runGroupTierOrder
} from './run-group-format';

describe('run-group-format', () => {
  it('names, classes and orders the tiers, Tier A highest', () => {
    expect(runGroupTierName('Replicate')).toBe('Tier A — Replicate');
    expect(runGroupTierName('QualityComparable')).toBe('Tier B — Quality-comparable');
    expect(runGroupTierName('CrossCondition')).toBe('Tier C — Cross-condition');
    expect(runGroupTierName(null)).toBe('Not comparable');
    expect(runGroupTierClass('Replicate')).toBe('tier-replicate');
    expect(runGroupTierClass('Unknown')).toBe('tier-none');
    expect(['CrossCondition', 'Replicate', 'NotComparable', 'QualityComparable']
      .sort((a, b) => runGroupTierOrder(b) - runGroupTierOrder(a)))
      .toEqual(['Replicate', 'QualityComparable', 'CrossCondition', 'NotComparable']);
  });

  it('tells a current analysis from a stale one and from none', () => {
    expect(runGroupAnalysisState({ latestAnalysisId: null, analysisStale: false })).toBe('none');
    expect(runGroupAnalysisState({ latestAnalysisId: 3, analysisStale: true })).toBe('stale');
    expect(runGroupAnalysisState({ latestAnalysisId: 3, analysisStale: false })).toBe('current');
    expect(runGroupAnalysisStateLabel('current')).toBe('Current');
    expect(runGroupAnalysisStateLabel('stale')).toBe('Stale');
    expect(runGroupAnalysisStateLabel('none')).toBe('Not analyzed');
  });

  it('labels the runs as #41 #42 #43', () => {
    expect(groupRunsLabel([41, 42, 43])).toBe('#41 #42 #43');
    expect(groupRunsLabel([])).toBe('');
    expect(groupRunsLabel(null)).toBe('');
  });
});
