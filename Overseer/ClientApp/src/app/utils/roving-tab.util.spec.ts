import { rovingTabIndex, rovingTabTarget } from './roving-tab.util';

describe('rovingTabTarget', () => {
  it.each([
    { name: 'ArrowRight from the first tab', key: 'ArrowRight', index: 0, count: 4, expected: 1 },
    { name: 'ArrowRight from the last tab wraps to the first', key: 'ArrowRight', index: 3, count: 4, expected: 0 },
    { name: 'ArrowLeft from the first tab wraps to the last', key: 'ArrowLeft', index: 0, count: 4, expected: 3 },
    { name: 'ArrowLeft from the middle', key: 'ArrowLeft', index: 2, count: 4, expected: 1 },
    { name: 'Home jumps to the first tab', key: 'Home', index: 2, count: 4, expected: 0 },
    { name: 'End jumps to the last tab', key: 'End', index: 1, count: 4, expected: 3 },
    { name: 'ArrowUp is not a tab-row key', key: 'ArrowUp', index: 1, count: 4, expected: null },
    { name: 'ArrowDown is not a tab-row key', key: 'ArrowDown', index: 1, count: 4, expected: null },
    { name: 'Enter is left to the button', key: 'Enter', index: 1, count: 4, expected: null },
    { name: 'Tab is left to the browser', key: 'Tab', index: 1, count: 4, expected: null },
    { name: 'a letter is ignored', key: 'a', index: 1, count: 4, expected: null },
    // An object-prototype property name must not read as a target.
    { name: 'constructor is ignored', key: 'constructor', index: 1, count: 4, expected: null },
    { name: 'ArrowRight with one tab stays on it', key: 'ArrowRight', index: 0, count: 1, expected: 0 },
    { name: 'ArrowLeft with one tab stays on it', key: 'ArrowLeft', index: 0, count: 1, expected: 0 },
    { name: 'End with no tabs', key: 'End', index: 0, count: 0, expected: null },
    { name: 'ArrowRight with no tabs', key: 'ArrowRight', index: 0, count: 0, expected: null }
  ])('$name', ({ key, index, count, expected }) => {
    expect(rovingTabTarget(key, index, count)).toBe(expected);
  });
});

describe('rovingTabIndex', () => {
  it.each([
    { key: 'ArrowRight', expected: 2, prevented: true },
    { key: 'ArrowLeft', expected: 0, prevented: true },
    { key: 'Home', expected: 0, prevented: true },
    { key: 'End', expected: 2, prevented: true },
    { key: 'ArrowDown', expected: null, prevented: false },
    { key: 'Enter', expected: null, prevented: false },
    { key: 'Tab', expected: null, prevented: false }
  ])('returns $expected for $key and prevents the default only for a handled key', ({ key, expected, prevented }) => {
    const event = new KeyboardEvent('keydown', { key, cancelable: true });
    expect(rovingTabIndex(event, 1, 3)).toBe(expected);
    expect(event.defaultPrevented).toBe(prevented);
  });

  it('does not prevent the default when there are no tabs', () => {
    const event = new KeyboardEvent('keydown', { key: 'ArrowRight', cancelable: true });
    expect(rovingTabIndex(event, 0, 0)).toBeNull();
    expect(event.defaultPrevented).toBe(false);
  });
});
