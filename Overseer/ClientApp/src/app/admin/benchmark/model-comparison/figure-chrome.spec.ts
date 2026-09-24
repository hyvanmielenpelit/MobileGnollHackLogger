import { figureDirectionRotation, figureDirectionText, questionsBadge } from './figure-chrome';

describe('figureDirectionRotation and figureDirectionText', () => {
  it('turns the up-right arrow toward each corner of a trade-off plot', () => {
    expect(figureDirectionRotation({ x: 'right', y: 'top', label: 'Better' })).toBe(0);
    expect(figureDirectionRotation({ x: 'right', y: 'bottom', label: 'Better' })).toBe(90);
    expect(figureDirectionRotation({ x: 'left', y: 'bottom', label: 'Better' })).toBe(180);
    expect(figureDirectionRotation({ x: 'left', y: 'top', label: 'Better' })).toBe(270);
  });

  it('turns it straight along one axis for a bar chart', () => {
    expect(figureDirectionRotation({ y: 'top', label: 'Better' })).toBe(315);
    expect(figureDirectionRotation({ x: 'right', label: 'Better' })).toBe(45);
    expect(figureDirectionRotation({ y: 'bottom', label: 'Better' })).toBe(135);
    expect(figureDirectionRotation({ x: 'left', label: 'Better' })).toBe(225);
  });

  it('spells out only the sides present', () => {
    expect(figureDirectionText({ x: 'left', y: 'top', label: 'Better' })).toBe('Better toward the top left');
    expect(figureDirectionText({ y: 'top', label: 'Better' })).toBe('Better toward the top');
    expect(figureDirectionText({ x: 'right', label: 'Better' })).toBe('Better toward the right');
  });
});

describe('questionsBadge', () => {
  it('names the suite size alone when every plotted entry scored every question', () => {
    expect(questionsBadge(18, 18, 18)).toEqual({ text: '18 questions', tone: 'neutral', kind: 'questions' });
    expect(questionsBadge(1, 1, 1).text).toBe('1 question');
  });

  it('states the scored count against the suite when fewer are scored', () => {
    expect(questionsBadge(16, 16, 18)).toEqual({
      text: '16 of 18 questions',
      tone: 'neutral',
      ariaLabel: "16 of the suite's 18 questions scored",
      kind: 'questions',
    });
  });

  it('gives a range when the plotted entries scored different counts', () => {
    const badge = questionsBadge(15, 16, 18);
    expect(badge.text).toBe('15–16 of 18 questions');
    expect(badge.ariaLabel).toBe("15–16 of the suite's 18 questions scored");
  });

  it('falls back to the scored count when the suite size is unknown', () => {
    expect(questionsBadge(16, 16, 0)).toEqual({ text: '16 questions', tone: 'neutral', kind: 'questions' });
    expect(questionsBadge(1, 1, 0).text).toBe('1 question');
    expect(questionsBadge(15, 16, 0).text).toBe('15–16 questions');
    expect(questionsBadge(16, 16, 0).ariaLabel).toBeUndefined();
  });
});
