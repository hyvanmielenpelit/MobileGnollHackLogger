import { ADMIN_USER_ACTION_KINDS } from '../../../services/admin-users.service';
import { KIND_LABELS, KIND_TONES, adminLogKindLabel, adminLogKindTone } from './admin-log-kinds';

describe('admin log kinds', () => {
  it('gives every kind a label and a tone', () => {
    expect(Object.keys(KIND_LABELS).sort()).toEqual([...ADMIN_USER_ACTION_KINDS].sort());
    expect(Object.keys(KIND_TONES).sort()).toEqual([...ADMIN_USER_ACTION_KINDS].sort());
    for (const kind of ADMIN_USER_ACTION_KINDS) {
      expect(adminLogKindLabel(kind)).toBe(KIND_LABELS[kind]);
      expect(adminLogKindTone(kind)).toBe(KIND_TONES[kind]);
    }
  });

  it('labels every kind in words', () => {
    expect(adminLogKindLabel('AiChatBlocked')).toBe('Blocked from AI chat');
    expect(adminLogKindLabel('HoldPlaced')).toBe('Deletion hold placed');
    expect(adminLogKindLabel('DeletionRequestDeclined')).toBe('Deletion request declined');
    expect(adminLogKindLabel('EmailChanged')).toBe('Email changed');
    expect(adminLogKindLabel('GameSignInBlockCleared')).toBe('Game sign-in block cleared');
  });

  it('reads an unknown kind in words, with the info tone', () => {
    expect(adminLogKindLabel('SomethingNew')).toBe('Something new');
    expect(adminLogKindTone('SomethingNew')).toBe('info');
  });

  it('never reads an inherited property as a label or a tone', () => {
    expect(adminLogKindLabel('constructor')).toBe('Constructor');
    expect(adminLogKindLabel('__proto__')).toBe('Proto');
    expect(adminLogKindLabel('toString')).toBe('To string');
    expect(adminLogKindTone('constructor')).toBe('info');
    expect(adminLogKindTone('__proto__')).toBe('info');
    expect(adminLogKindTone('hasOwnProperty')).toBe('info');
  });
});
