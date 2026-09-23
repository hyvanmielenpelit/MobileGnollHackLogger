import { questionsBadge } from './figure-chrome';

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
