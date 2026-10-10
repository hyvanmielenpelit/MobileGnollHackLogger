import { ccNextRunSections } from '../analysis-wizard/next-runs/next-runs.component';
import { buildEventDays, groupOverseerEvents } from '../chat-consistency-events';
import { ccResultKeyFigures } from '../chat-consistency-results';
import {
  ccAnalysisResult,
  ccAttribution,
  ccEndpoint,
  ccEvent,
  ccRunRows,
  ccRunSelectionView,
  ccTimeline
} from '../chat-consistency-tab.testing';
import { CcAnalysisResult } from '../chat-consistency.models';
import {
  CcResultsImageBlock,
  CcResultsImageContext,
  ccAttributionView,
  ccComputedEndpoints,
  ccImageEndpointCard,
  ccImageText,
  ccResultsImageAnalysisLine,
  ccResultsImageBlocks,
  ccResultsImageHasContent,
  ccResultsImageItems,
  ccResultsImageTitle
} from './results-image-blocks';
import { CC_RESULTS_IMAGE_SECTIONS } from './results-image-settings';

/** Every hexadecimal run of 12 or more characters with a letter: what an image must never carry. */
const HASH = /\b(?=[0-9a-f]*[a-f])[0-9a-f]{12,}\b/i;

function context(result: CcAnalysisResult = ccAnalysisResult()): CcResultsImageContext {
  return {
    rows: ccRunRows(),
    batteryRows: [],
    eventDays: buildEventDays(groupOverseerEvents(result.events, ccTimeline().points), result.annotations, [])
  };
}

/** Every string a block carries, flattened, for text checks. */
function blockTexts(blocks: readonly CcResultsImageBlock[]): string[] {
  const texts: string[] = [];
  const visit = (value: unknown): void => {
    if (typeof value === 'string') texts.push(value);
    else if (Array.isArray(value)) value.forEach(visit);
    else if (value && typeof value === 'object') Object.values(value).forEach(visit);
  };
  visit(blocks);
  return texts;
}

/** A result whose server text carries an instrument fingerprint, prompt hashes and a Git SHA. */
function hashedResult(): CcAnalysisResult {
  const base = ccAnalysisResult();
  return ccAnalysisResult({
    name: `Check ${'a'.repeat(64)}`,
    limitations: ['Prompt hash changed from 3f2a9c1b0d4e5f60718293a4b5c6d7e8 to 9e8d7c6b5a4f3e2d1c0b9a8f7e6d5c4b.'],
    dataQuality: [{ kind: 'note', text: 'Corpus fingerprint 0123456789abcdef0123 differs.' }],
    events: [ccEvent({ label: 'harness changed', from: '27+39113903b9b2', to: '28+0123456789ab' })],
    attribution: {
      ...base.attribution,
      attributions: [ccAttribution('ours', 'Prompt change', { evidence: 'Commit 0d1e2f3a4b5c6d7e8f9a0b1c2d3e4f5a6b7c8d9e moved the prompt.' })]
    },
    nextRuns: [{
      kind: 'control', period: 'comparison', endpointId: null, reason: 'No control run.',
      suggestion: 'Make a run of another provider on Board Suite under the same build as run #205 (instrument 39113903b9b2).',
      repeatRunId: 205
    }]
  });
}

describe('results image blocks', () => {
  describe('items', () => {
    it('lists the Summary items, the reliability notes only when there are some, and a key figure each', () => {
      const result = ccAnalysisResult();
      const keys = ccResultsImageItems('summary', result, context()).map(item => item.key);
      expect(keys).toEqual([
        'verdict', 'model', 'chips', 'scope',
        ...ccResultKeyFigures(result).map(figure => `figure-${figure.key}`)
      ]);
      const reliable = ccResultsImageItems('summary', ccAnalysisResult({ headlineReliabilityIncreases: ['One run per period.'] }), context());
      expect(reliable.map(item => item.key)).toContain('reliability');
      const decided = ccResultsImageItems('summary', result, context()).find(item => item.key === 'figure-decided')!;
      expect(decided.label).toBe('Endpoints decided');
      expect(decided.value).toBe(ccResultKeyFigures(result)[0].value);
    });

    it('lists a Verdicts item per computed endpoint, the not-computable card, then the per-card parts', () => {
      const result = ccAnalysisResult({
        endpoints: [ccEndpoint('P1'), ccEndpoint('P2', { computed: false, notComputedReason: 'No common time stratum.', verdict: null, verdictLabel: '' })]
      });
      const items = ccResultsImageItems('verdicts', result, context(result));
      expect(items.map(item => item.key)).toEqual(['endpoint-P1', 'uncomputed', 'meaning', 'mde', 'compared', 'notes', 'more']);
      expect(items[0]).toEqual({ key: 'endpoint-P1', label: 'P1 Quality', value: 'Within margin' });
    });

    it('lists the Periods, Attribution, Next runs and Details items', () => {
      const result = ccAnalysisResult();
      expect(ccResultsImageItems('periods', result, context()).map(item => item.key)).toEqual(['baseline', 'comparison', 'units']);
      expect(ccResultsImageItems('attribution', result, context()).map(item => item.key)).toEqual(['changes', 'groups', 'unattributed']);
      const groups = ccNextRunSections(result, ccRunRows()).flatMap(section => section.cards.map(card => `group-${card.key}`));
      expect(ccResultsImageItems('nextRuns', result, context()).map(item => item.key)).toEqual([...groups, 'suggestions', 'reasons']);
      expect(ccResultsImageItems('details', result, context()).map(item => item.key))
        .toEqual(['limitations', 'dataQuality', 'events', 'identity']);
      const selected = ccAnalysisResult({ runSelection: ccRunSelectionView() });
      expect(ccResultsImageItems('details', selected, context(selected)).map(item => item.key))
        .toEqual(['limitations', 'dataQuality', 'selection', 'events', 'identity']);
    });
  });

  describe('blocks', () => {
    it('build the Summary as the tab shows it: the outcome, the model, the chips, scope and protocol, the figures', () => {
      const blocks = ccResultsImageBlocks('summary', ccAnalysisResult(), context());
      expect(blocks.map(block => block.kind)).toEqual(['outcome', 'badgeRows', 'chips', 'facts', 'figures']);
      const outcome = blocks[0] as Extract<CcResultsImageBlock, { kind: 'outcome' }>;
      expect(outcome.title).toBe('The chat changed');
      expect(outcome.icon).toBe('alert');
      const chips = blocks[2] as Extract<CcResultsImageBlock, { kind: 'chips' }>;
      expect(chips.chips.map(chip => chip.text)[1]).toBe('P2 Time to first answer text: Changed');
      expect(chips.chips[1].icon).toBe('alert');
      const facts = blocks[3] as Extract<CcResultsImageBlock, { kind: 'facts' }>;
      expect(facts.facts).toEqual([{ term: 'Scope', value: 'weekdays 08–12 UTC' }, { term: 'Protocol', value: 'V1' }]);
      const model = blocks[1] as Extract<CcResultsImageBlock, { kind: 'badgeRows' }>;
      expect(model.rows.map(row => row.label)).toEqual(['Model']);
      expect(model.rows[0].runs.map(run => run.text)).toEqual(['GPT-5 high', 'High', 'OpenAI', 'gpt-5']);

      const battery = ccResultsImageBlocks('summary', ccAnalysisResult({
        comparisonSet: { kind: 'battery', key: 'battery:x', label: 'Two initial suites (rev 1)' }
      }), context(), key => key === 'model');
      const rows = (battery[0] as Extract<CcResultsImageBlock, { kind: 'badgeRows' }>).rows;
      expect(rows[1].label).toBe('Compared');
      expect(rows[1].runs.map(run => run.text)).toEqual(['Battery', 'Two initial suites (rev 1)']);
    });

    it('leave out every item the exclusions name', () => {
      const result = ccAnalysisResult();
      const excluded = new Set(['verdict', 'chips', 'figure-baseline', 'figure-comparison']);
      const blocks = ccResultsImageBlocks('summary', result, context(), key => !excluded.has(key));
      expect(blocks.map(block => block.kind)).toEqual(['badgeRows', 'facts', 'figures']);
      const figures = blocks[2] as Extract<CcResultsImageBlock, { kind: 'figures' }>;
      expect(figures.figures.map(figure => figure.key)).not.toContain('baseline');
      expect(figures.figures.map(figure => figure.key)).toContain('decided');

      const none = ccResultsImageBlocks('summary', result, context(), () => false);
      expect(none).toEqual([]);
      expect(ccResultsImageHasContent(none)).toBe(false);
      expect(ccResultsImageHasContent(ccResultsImageBlocks('verdicts', result, context(), () => false))).toBe(false);
    });

    it('order the endpoint cards as the tab does, and drop the card parts the exclusions name', () => {
      const result = ccAnalysisResult();
      const blocks = ccResultsImageBlocks('verdicts', result, context(), key => key !== 'compared' && key !== 'endpoint-P3');
      const cards = blocks.filter((block): block is Extract<CcResultsImageBlock, { kind: 'endpoint' }> => block.kind === 'endpoint');
      expect(cards.map(card => card.card.id)).toEqual(ccComputedEndpoints(result).map(endpoint => endpoint.id).filter(id => id !== 'P3'));
      expect(cards.every(card => card.card.facts.every(fact => fact.term !== 'Compared'))).toBe(true);
      expect(cards[0].card.geometry).not.toBeNull();
      expect(cards[0].card.statusText).toBe('Changed');
    });

    it('put the notes past the first two and the robustness checks under More about, only while included', () => {
      const endpoint = ccEndpoint('P1', {
        verdict: 'inconclusive', verdictLabel: 'inconclusive', grade: 'notEstablished',
        minimumSampleMet: false, minimumSampleDetail: '1 run on 1 day per period', relaxedPooling: true, commonGrader: true,
        robustnessChecks: [{ endpointId: 'P1', name: 'Leave one out', status: 'passed', detail: 'All signs agree.' }]
      });
      const card = ccImageEndpointCard(endpoint, false);
      expect(card.notes[0]).toEqual({ text: 'Below the minimum sample: 1 run on 1 day per period', shortfall: true });
      expect(card.notes.length).toBe(2);
      expect(card.more).toContain('Leave one out: Passed — All signs agree.');
      expect(card.grade).toBeNull();
      expect(card.facts.map(fact => fact.term)).toContain('Smallest change this sample can detect');
      expect(ccImageEndpointCard(endpoint, false, key => key !== 'more').more).toEqual([]);
      expect(ccImageEndpointCard(endpoint, false, key => key !== 'notes').notes).toEqual([]);
    });

    it('build the period cards and the units, the units only while included', () => {
      const result = ccAnalysisResult();
      const blocks = ccResultsImageBlocks('periods', result, context(), () => true);
      const periods = blocks.filter((block): block is Extract<CcResultsImageBlock, { kind: 'period' }> => block.kind === 'period');
      expect(periods.map(block => block.card.title)).toEqual(['Baseline', 'Comparison']);
      expect(periods[0].card.dates).toBe('2026-09-01 to 2026-09-14');
      expect(periods[0].card.range).toBe('First: run #101 · Last: run #103');
      expect(periods[0].card.facts.map(fact => fact.term)).toEqual(['Runs', 'Days', 'Answers', 'Items', 'Suites', 'Runs without telemetry']);
      expect(blocks.some(block => block.kind === 'heading' && block.text === 'Runs in the periods')).toBe(true);

      const defaults = ccResultsImageBlocks('periods', result, context(), key => key !== 'units');
      expect(defaults.some(block => block.kind === 'heading')).toBe(false);
    });

    it('build the attribution as the tab reads it', () => {
      const result = ccAnalysisResult();
      const view = ccAttributionView(result);
      const blocks = ccResultsImageBlocks('attribution', result, context());
      expect(blocks.map(block => block.kind)).toEqual(['chips', 'attribution', 'attribution', 'attribution', 'paragraph']);
      expect((blocks[0] as Extract<CcResultsImageBlock, { kind: 'chips' }>).chips[0].text).toBe('Time to first answer text Degraded');
      expect(blocks.filter(block => block.kind === 'attribution').map(block => (block as Extract<CcResultsImageBlock, { kind: 'attribution' }>).group.title))
        .toEqual(view.groups.map(group => group.title));
      expect((blocks[4] as Extract<CcResultsImageBlock, { kind: 'paragraph' }>).text).toBe('Nothing is attributed to infrastructure.');
    });

    it('build a card per next-run group, with the reasons only while included', () => {
      const result = ccAnalysisResult();
      const blocks = ccResultsImageBlocks('nextRuns', result, context(), key => key !== 'reasons');
      const cards = blocks.filter((block): block is Extract<CcResultsImageBlock, { kind: 'runGroup' }> => block.kind === 'runGroup');
      expect(cards.map(card => card.card.kind)).toEqual(['control', 'regrade']);
      expect(cards[0].card.facts.map(fact => fact.term)).toEqual(['Suite', 'Same build as', 'Provider']);
      expect(cards[0].card.reasons).toEqual([]);
      expect(cards[0].card.suggestions).toEqual(['Run Claude Opus on Board Suite.']);
      expect(cards[1].card.hint).toBe('Re-grade from step 3: Controls → Re-grade.');
      expect((blocks[0] as Extract<CcResultsImageBlock, { kind: 'paragraph' }>).text).toBe('2 runs would resolve the open questions.');

      const none = ccResultsImageBlocks('nextRuns', ccAnalysisResult({ nextRuns: [] }), context());
      expect(none).toEqual([{ kind: 'paragraph', tone: 'high', text: 'No run is needed: no verdict is waiting on more data.' }]);
    });

    it('build the Details with the analysis identity, which names no hash', () => {
      const result = ccAnalysisResult();
      const blocks = ccResultsImageBlocks('details', result, context());
      const identity = blocks[blocks.length - 1] as Extract<CcResultsImageBlock, { kind: 'facts' }>;
      expect(identity.facts).toEqual([
        { term: 'Analysis', value: '#7 · September check' },
        { term: 'Saved', value: '2026-10-02 09:00 UTC' },
        { term: 'Protocol', value: 'V1' },
        { term: 'Analysis code', value: 'version 1' }
      ]);
      expect(blockTexts(blocks).join('\n')).not.toContain(result.inputSha256);
      expect(blocks.some(block => block.kind === 'heading' && block.text === 'Events in the analyzed span (1)')).toBe(true);
    });

    it('never carry a hash, in any section, with every item included', () => {
      const result = hashedResult();
      for (const section of CC_RESULTS_IMAGE_SECTIONS) {
        const texts = blockTexts(ccResultsImageBlocks(section, result, context(result), () => true));
        for (const text of texts) {
          expect(text, `${section}: ${text}`).not.toMatch(HASH);
          expect(text).not.toMatch(/instrument/i);
        }
      }
    });
  });

  describe('text', () => {
    it('strips instrument fingerprints and hashes, keeping the sentence', () => {
      expect(ccImageText('Run a control under the same build as run #205 (instrument 39113903b9b2).'))
        .toBe('Run a control under the same build as run #205.');
      expect(ccImageText('Hash 3f2a9c1b0d4e5f60718293a4b5c6d7e8 changed.')).toBe('Hash changed.');
      expect(ccImageText('Corpus (0123456789abcdef) moved')).toBe('Corpus moved');
      expect(ccImageText('1,234,567,890,123 answers in 2026-10-01')).toBe('1,234,567,890,123 answers in 2026-10-01');
      expect(ccImageText('Counted 123456789012 tokens')).toBe('Counted 123456789012 tokens');
      expect(ccImageText(null)).toBe('');
    });

    it('titles the image by its section and names the analysis without a hash', () => {
      expect(ccResultsImageTitle('nextRuns')).toBe('Chat Consistency · Next runs');
      expect(ccResultsImageAnalysisLine(ccAnalysisResult())).toBe('Analysis #7 · saved 2026-10-02 09:00 UTC · Protocol V1');
      expect(ccResultsImageAnalysisLine(ccAnalysisResult({ analysisId: null, createdAtUtc: null })))
        .toBe('Unsaved analysis · Protocol V1');
    });
  });
});
