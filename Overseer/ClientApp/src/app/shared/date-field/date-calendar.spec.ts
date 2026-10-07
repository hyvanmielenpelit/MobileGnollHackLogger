import {
  addDays,
  addMonths,
  addYears,
  clampDay,
  dateToDay,
  dayInRange,
  dayLabel,
  daysInMonth,
  isDay,
  monthGrid,
  monthTitle,
  moveDay,
  normalizeDayInput,
  parseDay,
  todayUtc,
  weekdayOf
} from './date-calendar';

describe('date-calendar', () => {
  describe('parseDay', () => {
    it('reads real calendar days only', () => {
      expect(parseDay('2026-10-07')).toEqual({ year: 2026, month: 10, date: 7 });
      expect(parseDay('2028-02-29')).toEqual({ year: 2028, month: 2, date: 29 });
      expect(parseDay('2026-02-29')).toBeNull();
      expect(parseDay('2026-13-01')).toBeNull();
      expect(parseDay('2026-10-00')).toBeNull();
      expect(parseDay('2026-9-5')).toBeNull();
      expect(parseDay(' 2026-10-07')).toBeNull();
      expect(parseDay('')).toBeNull();
      expect(parseDay(null)).toBeNull();
      expect(isDay('2026-04-30')).toBe(true);
      expect(isDay('2026-04-31')).toBe(false);
    });
  });

  describe('UTC days', () => {
    it('takes the UTC day of an instant late in the UTC day, whatever the local zone', () => {
      const lateUtc = new Date(Date.UTC(2026, 9, 7, 23, 30));
      expect(dateToDay(lateUtc)).toBe('2026-10-07');
      expect(todayUtc(lateUtc)).toBe('2026-10-07');

      const earlyUtc = new Date(Date.UTC(2026, 9, 7, 0, 30));
      expect(todayUtc(earlyUtc)).toBe('2026-10-07');
    });

    it('steps days across month, year and daylight-saving boundaries without a shift', () => {
      expect(addDays('2026-10-31', 1)).toBe('2026-11-01');
      expect(addDays('2026-01-01', -1)).toBe('2025-12-31');
      expect(addDays('2026-03-28', 2)).toBe('2026-03-30');
      expect(addDays('2026-10-24', 2)).toBe('2026-10-26');
      expect(addDays('2028-02-28', 1)).toBe('2028-02-29');
    });

    it('knows month lengths, leap years included', () => {
      expect(daysInMonth(2026, 2)).toBe(28);
      expect(daysInMonth(2028, 2)).toBe(29);
      expect(daysInMonth(2100, 2)).toBe(28);
      expect(daysInMonth(2000, 2)).toBe(29);
      expect(daysInMonth(2026, 4)).toBe(30);
      expect(daysInMonth(2026, 12)).toBe(31);
    });

    it('clamps a date to the end of a shorter month and normalizes the month', () => {
      expect(clampDay(2026, 2, 31)).toBe('2026-02-28');
      expect(clampDay(2028, 2, 31)).toBe('2028-02-29');
      expect(clampDay(2026, 13, 31)).toBe('2027-01-31');
      expect(clampDay(2026, 0, 15)).toBe('2025-12-15');
    });

    it('moves months and years with the day clamped', () => {
      expect(addMonths('2026-01-31', 1)).toBe('2026-02-28');
      expect(addMonths('2028-01-31', 1)).toBe('2028-02-29');
      expect(addMonths('2026-03-31', -1)).toBe('2026-02-28');
      expect(addMonths('2026-12-15', 1)).toBe('2027-01-15');
      expect(addYears('2028-02-29', 1)).toBe('2029-02-28');
      expect(addYears('2028-02-29', 4)).toBe('2032-02-29');
    });

    it('reads the weekday, Sunday first', () => {
      expect(weekdayOf('2026-10-04')).toBe(0);
      expect(weekdayOf('2026-10-07')).toBe(3);
      expect(weekdayOf('2026-10-10')).toBe(6);
    });
  });

  describe('monthGrid', () => {
    const days = (grid: ReturnType<typeof monthGrid>): string[] => grid.flat().map(cell => cell.day);

    it('is six Sunday-first weeks of seven days', () => {
      const grid = monthGrid(2026, 10);
      expect(grid.length).toBe(6);
      expect(grid.every(week => week.length === 7)).toBe(true);
      expect(grid.every(week => weekdayOf(week[0].day) === 0)).toBe(true);
    });

    it('starts on the 1st when the month begins on a Sunday', () => {
      // November 2026 begins on a Sunday.
      const grid = monthGrid(2026, 11);
      expect(grid[0][0]).toEqual({ day: '2026-11-01', date: 1, inMonth: true });
      expect(grid[4][1]).toEqual({ day: '2026-11-30', date: 30, inMonth: true });
      expect(grid[4][2]).toEqual({ day: '2026-12-01', date: 1, inMonth: false });
      expect(days(grid)[41]).toBe('2026-12-12');
    });

    it('pads a month that begins on a Saturday with six days of the month before', () => {
      // August 2026 begins on a Saturday and needs all six weeks.
      const grid = monthGrid(2026, 8);
      expect(grid[0].slice(0, 6).map(cell => cell.inMonth)).toEqual([false, false, false, false, false, false]);
      expect(grid[0][0].day).toBe('2026-07-26');
      expect(grid[0][6]).toEqual({ day: '2026-08-01', date: 1, inMonth: true });
      expect(grid[5][1]).toEqual({ day: '2026-08-31', date: 31, inMonth: true });
      expect(grid[5][2].inMonth).toBe(false);
    });

    it('holds February 29 in a leap year', () => {
      const inMonth = monthGrid(2028, 2).flat().filter(cell => cell.inMonth);
      expect(inMonth.length).toBe(29);
      expect(inMonth[28].day).toBe('2028-02-29');
      expect(monthGrid(2026, 2).flat().filter(cell => cell.inMonth).length).toBe(28);
    });
  });

  describe('moveDay', () => {
    it('moves by day, week and to the week ends', () => {
      expect(moveDay('2026-10-07', 'ArrowLeft')).toBe('2026-10-06');
      expect(moveDay('2026-10-07', 'ArrowRight')).toBe('2026-10-08');
      expect(moveDay('2026-10-07', 'ArrowUp')).toBe('2026-09-30');
      expect(moveDay('2026-10-07', 'ArrowDown')).toBe('2026-10-14');
      expect(moveDay('2026-10-07', 'Home')).toBe('2026-10-04');
      expect(moveDay('2026-10-07', 'End')).toBe('2026-10-10');
      expect(moveDay('2026-10-04', 'Home')).toBe('2026-10-04');
      expect(moveDay('2026-10-10', 'End')).toBe('2026-10-10');
    });

    it('moves by month and, with Shift, by year, clamping the day', () => {
      expect(moveDay('2026-10-07', 'PageDown')).toBe('2026-11-07');
      expect(moveDay('2026-10-07', 'PageUp')).toBe('2026-09-07');
      expect(moveDay('2026-01-31', 'PageDown')).toBe('2026-02-28');
      expect(moveDay('2028-01-31', 'PageDown')).toBe('2028-02-29');
      expect(moveDay('2026-03-31', 'PageUp')).toBe('2026-02-28');
      expect(moveDay('2026-10-07', 'PageDown', true)).toBe('2027-10-07');
      expect(moveDay('2026-10-07', 'PageUp', true)).toBe('2025-10-07');
      expect(moveDay('2028-02-29', 'PageDown', true)).toBe('2029-02-28');
      expect(moveDay('2028-02-29', 'PageUp', true)).toBe('2027-02-28');
    });

    it('ignores Shift on the arrow keys and returns null for other keys', () => {
      expect(moveDay('2026-10-07', 'ArrowRight', true)).toBe('2026-10-08');
      expect(moveDay('2026-10-07', 'Enter')).toBeNull();
      expect(moveDay('2026-10-07', 'a')).toBeNull();
      expect(moveDay('2026-10-07', 'Tab')).toBeNull();
    });
  });

  describe('labels', () => {
    it('names a day and a month in US English', () => {
      expect(dayLabel('2026-10-07')).toBe('Wednesday, October 7, 2026');
      expect(dayLabel('2028-02-29')).toBe('Tuesday, February 29, 2028');
      expect(monthTitle(2026, 10)).toBe('October 2026');
      expect(monthTitle(2027, 1)).toBe('January 2027');
    });
  });

  describe('dayInRange', () => {
    it('is inclusive and treats an empty or malformed bound as open', () => {
      expect(dayInRange('2026-10-07', '2026-10-07', '2026-10-07')).toBe(true);
      expect(dayInRange('2026-10-06', '2026-10-07', null)).toBe(false);
      expect(dayInRange('2026-10-08', null, '2026-10-07')).toBe(false);
      expect(dayInRange('2026-10-08', '', '')).toBe(true);
      expect(dayInRange('2026-10-08', '2026-9-1', 'later')).toBe(true);
      expect(dayInRange('2026-10-08')).toBe(true);
    });
  });

  describe('normalizeDayInput', () => {
    it('writes slash, dot and short dash dates as YYYY-MM-DD and trims', () => {
      expect(normalizeDayInput('2026/9/5')).toBe('2026-09-05');
      expect(normalizeDayInput('2026.9.5')).toBe('2026-09-05');
      expect(normalizeDayInput('2026-9-5')).toBe('2026-09-05');
      expect(normalizeDayInput('  2026-10-07 ')).toBe('2026-10-07');
      expect(normalizeDayInput('2026/10/07')).toBe('2026-10-07');
    });

    it('returns anything else as typed, trimmed', () => {
      expect(normalizeDayInput('')).toBe('');
      expect(normalizeDayInput('  ')).toBe('');
      expect(normalizeDayInput('yesterday')).toBe('yesterday');
      expect(normalizeDayInput('2026/9-5')).toBe('2026/9-5');
      expect(normalizeDayInput('2026-2-30')).toBe('2026-2-30');
      expect(normalizeDayInput('2026-13-01')).toBe('2026-13-01');
      expect(normalizeDayInput('05.09.2026')).toBe('05.09.2026');
    });
  });
});
