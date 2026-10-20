import { isImeComposing } from './ime.util';

describe('isImeComposing', () => {
  it.each([
    { name: 'a plain Enter is not composing', init: { key: 'Enter' }, expected: false },
    { name: 'an Enter during a composition is composing', init: { key: 'Enter', isComposing: true }, expected: true },
    { name: "Safari's keyCode 229 Enter is composing", init: { key: 'Enter', keyCode: 229 }, expected: true },
    { name: 'an Enter with keyCode 13 is not composing', init: { key: 'Enter', keyCode: 13 }, expected: false }
  ])('$name', ({ init, expected }) => {
    expect(isImeComposing(new KeyboardEvent('keydown', init))).toBe(expected);
  });
});
