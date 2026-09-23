import { chooseScaleType, formatTick, linearDomain, logDomain, niceStep, timeUnitFor } from './axis-domain';

describe('axis-domain', () => {
  describe('niceStep', () => {
    const table: [number, number][] = [
      [1, 1],
      [1.5, 2],
      [2, 2],
      [2.1, 2.5],
      [2.5, 2.5],
      [3, 5],
      [6, 10],
      [0.3, 0.5],
      [0.0021, 0.0025],
      [0.0004, 0.0005],
      [12, 20],
      [1000, 1000],
      [1001, 2000],
      [2231, 2500],
    ];

    table.forEach(([raw, expected]) => {
      it(`rounds ${raw} up to ${expected}`, () => {
        expect(niceStep(raw)).toBeCloseTo(expected, 12);
      });
    });

    it('falls back to 1 for a zero, negative or non-finite input', () => {
      expect(niceStep(0)).toBe(1);
      expect(niceStep(-3)).toBe(1);
      expect(niceStep(Number.NaN)).toBe(1);
    });
  });

  describe('chooseScaleType', () => {
    it('turns logarithmic at a ratio of exactly ten, and stays linear just below it', () => {
      expect(chooseScaleType([1, 10])).toBe('logarithmic');
      expect(chooseScaleType([1, 9.99])).toBe('linear');
      expect(chooseScaleType([7000, 200000])).toBe('logarithmic');
      expect(chooseScaleType([21250, 29990])).toBe('linear');
    });

    it('stays linear for an empty set, a zero or a negative value, and ignores NaN', () => {
      expect(chooseScaleType([])).toBe('linear');
      expect(chooseScaleType([0, 100])).toBe('linear');
      expect(chooseScaleType([-1, 50])).toBe('linear');
      expect(chooseScaleType([5, Number.NaN, 60])).toBe('logarithmic');
    });
  });

  describe('linearDomain', () => {
    const INDEX = { bounds: { min: 0, max: 100 }, minSpan: 20 };

    it('fits the screenshot Intelligence intervals to 60-90 on a step of 5', () => {
      const domain = linearDomain({ lows: [66.8, 66.6], highs: [82.8, 82.6], ...INDEX });
      expect(domain.min).toBe(60);
      expect(domain.max).toBe(90);
      expect(domain.step).toBe(5);
      expect(domain.ticks).toEqual([60, 65, 70, 75, 80, 85, 90]);
    });

    it('widens a zero span to the minimum span', () => {
      const domain = linearDomain({ lows: [50], highs: [50], ...INDEX });
      expect(domain.min).toBeLessThanOrEqual(40);
      expect(domain.max).toBeGreaterThanOrEqual(60);
      expect(domain.ticks.length).toBeGreaterThanOrEqual(3);
      expect(domain.ticks.length).toBeLessThanOrEqual(7);
    });

    it('clamps at 0 and at 100 without losing the minimum span', () => {
      const low = linearDomain({ lows: [1], highs: [4], ...INDEX });
      expect(low.min).toBe(0);
      expect(low.ticks[0]).toBe(0);
      expect(low.max - low.min).toBeGreaterThanOrEqual(20);

      const high = linearDomain({ lows: [96], highs: [99.5], ...INDEX });
      expect(high.max).toBe(100);
      expect(high.ticks[high.ticks.length - 1]).toBe(100);
      expect(high.max - high.min).toBeGreaterThanOrEqual(20);
    });

    it('generates ticks without floating-point drift', () => {
      const domain = linearDomain({ lows: [0.0024], highs: [0.0042], bounds: { min: 0 }, minSpan: 0.3 * 0.0042 });
      expect(domain.ticks).toEqual([0.002, 0.0025, 0.003, 0.0035, 0.004, 0.0045]);
    });

    it('keeps every tick within the domain and at most seven of them', () => {
      const cases = [
        { lows: [21250, 29990], highs: [21250, 29990], minSpan: 0.3 * 29990, bounds: { min: 0 } },
        { lows: [0.13], highs: [0.97], minSpan: 0.3 * 0.97, bounds: { min: 0 } },
        { lows: [3], highs: [96], minSpan: 20, bounds: { min: 0, max: 100 } },
        { lows: [], highs: [], minSpan: 20, bounds: { min: 0, max: 100 } },
      ];
      for (const input of cases) {
        const domain = linearDomain(input);
        expect(domain.ticks.length).withContext(JSON.stringify(input)).toBeLessThanOrEqual(7);
        expect(domain.ticks.length).withContext(JSON.stringify(input)).toBeGreaterThanOrEqual(2);
        for (const tick of domain.ticks) {
          expect(tick).toBeGreaterThanOrEqual(domain.min);
          expect(tick).toBeLessThanOrEqual(domain.max);
        }
      }
    });
  });

  describe('logDomain', () => {
    it('carries three to seven ticks across 1.2 to 5 decades', () => {
      for (const base of [1, 2.3, 7, 0.0013]) {
        for (const decades of [1.2, 1.5, 2, 2.3, 2.65, 3, 3.5, 4, 4.5, 5]) {
          const domain = logDomain({ lows: [base], highs: [base * 10 ** decades] });
          const context = `${base} over ${decades} decades`;
          expect(domain.ticks.length).withContext(context).toBeGreaterThanOrEqual(3);
          expect(domain.ticks.length).withContext(context).toBeLessThanOrEqual(7);
          for (const tick of domain.ticks) {
            expect(tick).withContext(context).toBeGreaterThanOrEqual(domain.min);
            expect(tick).withContext(context).toBeLessThanOrEqual(domain.max);
          }
        }
      }
    });

    it('pads each side in log space so no value sits on an edge', () => {
      const domain = logDomain({ lows: [7000], highs: [200000] });
      expect(domain.min).toBeLessThan(7000);
      expect(domain.max).toBeGreaterThan(200000);
    });
  });

  describe('timeUnitFor', () => {
    it('switches to seconds at 1000 ms', () => {
      expect(timeUnitFor(999)).toBe('ms');
      expect(timeUnitFor(1000)).toBe('s');
    });
  });

  describe('formatTick', () => {
    it('writes time, cost and index ticks with the decimals their step needs', () => {
      expect(formatTick(22000, { kind: 'time', unit: 's', step: 1000 })).toBe('22 s');
      expect(formatTick(22500, { kind: 'time', unit: 's', step: 2500 })).toBe('22.5 s');
      expect(formatTick(20000, { kind: 'time', unit: 's', step: 2500 })).toBe('20 s');
      expect(formatTick(850, { kind: 'time', unit: 'ms', step: 50 })).toBe('850 ms');
      expect(formatTick(0.0025, { kind: 'usd', step: 0.0005 })).toBe('$0.0025');
      expect(formatTick(75, { kind: 'index', step: 5 })).toBe('75');
    });

    it('writes a zero tick in the axis unit', () => {
      expect(formatTick(0, { kind: 'time', unit: 's', step: 5000 })).toBe('0 s');
      expect(formatTick(0, { kind: 'usd', step: 0.001 })).toBe('$0');
    });

    it('takes a log tick\'s decimals from its own magnitude and never leaves trailing zeros', () => {
      expect(formatTick(0.0015, { kind: 'usd' })).toBe('$0.0015');
      expect(formatTick(1500, { kind: 'time', unit: 's' })).toBe('1.5 s');
      expect(formatTick(200000, { kind: 'time', unit: 's' })).toBe('200 s');
      expect(formatTick(200, { kind: 'time', unit: 'ms' })).toBe('200 ms');
    });
  });
});
