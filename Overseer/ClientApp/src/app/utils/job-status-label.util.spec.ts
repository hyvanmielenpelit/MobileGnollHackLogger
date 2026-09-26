import { jobStatusLabel } from './job-status-label.util';

describe('jobStatusLabel', () => {
  it('should show the Cancelled wire value as Canceled', () => {
    expect(jobStatusLabel('Cancelled')).toBe('Canceled');
  });

  it('should leave any other status unchanged', () => {
    expect(jobStatusLabel('Completed')).toBe('Completed');
  });

  it('should return an empty string for null or undefined', () => {
    expect(jobStatusLabel(null)).toBe('');
    expect(jobStatusLabel(undefined)).toBe('');
  });
});
