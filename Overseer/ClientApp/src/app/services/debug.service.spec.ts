import { TestBed } from '@angular/core/testing';

import { DebugService } from './debug.service';

/** The buffer size; `DebugService.MAX_LOGS` is private, so the value is repeated here. */
const MAX_LOGS = 2000;

describe('DebugService', () => {
  let service: DebugService;

  beforeEach(() => {
    TestBed.configureTestingModule({});
    service = TestBed.inject(DebugService);
  });

  const messages = () => service.getLogs().map(entry => entry.message);

  it('records each message in order with a timestamp while enabled', () => {
    service.log('first');
    service.log('second');

    expect(messages()).toEqual(['first', 'second']);
    expect(service.getLogs().every(entry => entry.timestamp instanceof Date)).toBe(true);
  });

  it('clears the log when disabled, records nothing while disabled, and records again once enabled', () => {
    service.log('before');

    service.setEnabled(false);
    expect(messages()).toEqual([]);
    service.log('while disabled');
    expect(messages()).toEqual([]);

    service.setEnabled(true);
    expect(messages()).toEqual([]);
    service.log('after');
    expect(messages()).toEqual(['after']);
  });

  it('keeps only the newest 2000 entries', () => {
    for (let i = 0; i <= MAX_LOGS; i++) {
      service.log(`message ${i}`);
    }

    const kept = messages();
    expect(kept.length).toBe(MAX_LOGS);
    expect(kept[0]).toBe('message 1');
    expect(kept[kept.length - 1]).toBe(`message ${MAX_LOGS}`);
  });
});
