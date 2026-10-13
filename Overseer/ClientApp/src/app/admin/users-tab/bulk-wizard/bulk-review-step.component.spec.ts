import { ComponentFixture, TestBed } from '@angular/core/testing';

import { AdminBulkPreview, AdminBulkPreviewItem } from '../../../services/admin-users.service';
import {
  BulkReviewChoices,
  BulkReviewStepComponent,
  EMPTY_REVIEW_CHOICES,
  bulkExecuteRequest,
  bulkLiveCount,
  bulkRequiredAcknowledgments,
  bulkRequiredPhrase,
  bulkReviewMissing
} from './bulk-review-step.component';

const textOf = (el: Element | null | undefined): string => (el?.textContent ?? '').replace(/\s+/g, ' ').trim();

function item(id: string, name: string, overrides: Partial<AdminBulkPreviewItem> = {}): AdminBulkPreviewItem {
  return {
    userId: id,
    userName: name,
    email: `${name}@example.com`,
    statuses: ['EmailUnconfirmed'],
    statusText: 'Email not confirmed',
    createdUtc: '2026-01-02T00:00:00Z',
    createdUtcIsEstimate: false,
    lastSignInUtc: null,
    isAdvanced: false,
    ...overrides
  };
}

/** A delete of three eligible accounts, two refused for game data. */
function buildPreview(overrides: Partial<AdminBulkPreview> = {}): AdminBulkPreview {
  return {
    operationId: 7,
    action: 'Delete',
    expiresUtc: '2026-10-13T15:52:00Z',
    dangerLevel: 'danger',
    requiredAcknowledgments: ['reviewed', 'irreversible'],
    matchedCount: 5,
    eligibleCount: 3,
    advancedCount: 0,
    refusedCount: 2,
    noOpCount: 0,
    truncatedCount: 0,
    maxUsers: 500,
    eligible: [item('u1', 'ann'), item('u2', 'bob'), item('u3', 'cid')],
    advanced: [],
    refused: [{ code: 'game-data', message: 'Game data (game logs, bones or save files)', count: 2, exampleUserNames: ['dan', 'eve'] }],
    dataTotals: [{ key: 'game-logs', label: 'Game logs', count: 0, important: true }],
    requiredPhrase: 'DELETE 3 ACCOUNTS',
    ...overrides
  };
}

const withAdvanced = (): AdminBulkPreview => buildPreview({
  advanced: [item('a1', 'amy', { isAdvanced: true, statuses: ['GoodStanding'] })],
  advancedCount: 1
});

describe('bulk review rules', () => {
  const choices = (overrides: Partial<BulkReviewChoices> = {}): BulkReviewChoices => ({ ...EMPTY_REVIEW_CHOICES, ...overrides });

  it('counts the eligible accounts left included, plus the advanced ones when included', () => {
    expect(bulkLiveCount(buildPreview(), choices())).toBe(3);
    expect(bulkLiveCount(buildPreview(), choices({ excluded: ['u2'] }))).toBe(2);
    expect(bulkLiveCount(withAdvanced(), choices({ excluded: ['u2'], includeAdvanced: true }))).toBe(3);
    expect(bulkLiveCount(withAdvanced(), choices({ includeAdvanced: false }))).toBe(3);
  });

  it('makes the phrase follow the live count', () => {
    expect(bulkRequiredPhrase(buildPreview(), choices())).toBe('DELETE 3 ACCOUNTS');
    expect(bulkRequiredPhrase(buildPreview(), choices({ excluded: ['u2'] }))).toBe('DELETE 2 ACCOUNTS');
    expect(bulkRequiredPhrase(buildPreview(), choices({ excluded: ['u2', 'u3'] }))).toBe('DELETE 1 ACCOUNT');
    expect(bulkRequiredPhrase(buildPreview({ action: 'Ban', dangerLevel: 'caution' }), choices())).toBeNull();
  });

  it('requires advanced only with the advanced accounts included', () => {
    expect(bulkRequiredAcknowledgments(withAdvanced(), choices())).toEqual(['reviewed', 'irreversible']);
    expect(bulkRequiredAcknowledgments(withAdvanced(), choices({ includeAdvanced: true }))).toEqual(['reviewed', 'irreversible', 'advanced']);
    expect(bulkRequiredAcknowledgments(buildPreview({ action: 'LockOut', dangerLevel: 'caution', requiredAcknowledgments: ['reviewed'] }), choices()))
      .toEqual(['reviewed']);
    expect(bulkRequiredAcknowledgments(buildPreview({ action: 'Unlock', dangerLevel: 'safe', requiredAcknowledgments: [] }), choices()))
      .toEqual([]);
  });

  it('names what is missing, and an earlier phrase stops matching when the count changes', () => {
    expect(bulkReviewMissing(buildPreview(), choices()))
      .toBe('Still needed: every acknowledgment and DELETE 3 ACCOUNTS typed exactly.');
    const typed = choices({ acknowledgments: ['reviewed', 'irreversible'], phrase: 'DELETE 3 ACCOUNTS' });
    expect(bulkReviewMissing(buildPreview(), typed)).toBeNull();
    expect(bulkReviewMissing(buildPreview(), { ...typed, excluded: ['u2'] }))
      .toBe('Still needed: DELETE 2 ACCOUNTS typed exactly.');
    expect(bulkReviewMissing(buildPreview(), { ...typed, excluded: ['u1', 'u2', 'u3'], phrase: 'DELETE 0 ACCOUNTS' }))
      .toBe('Still needed: at least one account included.');
    expect(bulkReviewMissing(buildPreview({ action: 'Unlock', dangerLevel: 'safe', requiredAcknowledgments: [] }), choices())).toBeNull();
  });

  it('builds the execute request the server checks', () => {
    expect(bulkExecuteRequest(buildPreview(), choices({
      excluded: ['u2', 'not-in-preview'], acknowledgments: ['irreversible', 'reviewed', 'advanced'], phrase: ' DELETE 2 ACCOUNTS '
    }))).toEqual({
      excludedUserIds: ['u2'],
      includeAdvanced: false,
      acknowledgments: ['irreversible', 'reviewed'],
      confirmationPhrase: 'DELETE 2 ACCOUNTS'
    });
    expect(bulkExecuteRequest(withAdvanced(), choices({ includeAdvanced: true, acknowledgments: ['reviewed', 'irreversible', 'advanced'] })))
      .toEqual(expect.objectContaining({ includeAdvanced: true, acknowledgments: ['reviewed', 'irreversible', 'advanced'] }));
    expect(bulkExecuteRequest(buildPreview({ action: 'Ban', dangerLevel: 'caution', requiredAcknowledgments: ['reviewed'] }),
      choices({ acknowledgments: ['reviewed'], phrase: 'stray' }))).toEqual(expect.objectContaining({ confirmationPhrase: '' }));
  });
});

describe('BulkReviewStepComponent', () => {
  let fixture: ComponentFixture<BulkReviewStepComponent>;
  let component: BulkReviewStepComponent;
  let el: HTMLElement;
  let emitted: BulkReviewChoices[];

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [BulkReviewStepComponent] }).compileComponents();
    fixture = TestBed.createComponent(BulkReviewStepComponent);
    component = fixture.componentInstance;
    el = fixture.nativeElement as HTMLElement;
    emitted = [];
    component.choicesChange.subscribe(c => emitted.push(c));
    component.now = () => Date.parse('2026-10-13T15:37:28Z');
  });

  afterEach(() => fixture.destroy());

  function render(preview: AdminBulkPreview, choices: BulkReviewChoices = EMPTY_REVIEW_CHOICES): void {
    fixture.componentRef.setInput('preview', preview);
    fixture.componentRef.setInput('choices', choices);
    fixture.detectChanges();
  }

  const last = (): BulkReviewChoices => emitted[emitted.length - 1];

  it('leads a delete with the danger zone, the summary and the expiry with an unannounced countdown', () => {
    render(buildPreview({ truncatedCount: 120 }));

    expect(textOf(el.querySelector('.gh-danger-zone #bw-danger-title'))).toBe('Permanent deletion');
    expect(textOf(el.querySelector('.bw-data-total[data-key="game-logs"] dt'))).toBe('Game logs');
    expect(textOf(el.querySelector('.bw-data-total[data-key="game-logs"] dd'))).toBe('0');
    expect(textOf(el.querySelector('#bw-review-summary'))).toBe('3 accounts will be deleted · 2 protected and skipped · 0 already done');
    expect(el.querySelector('#bw-review-summary')?.getAttribute('role')).toBe('status');
    expect(textOf(el.querySelector('.bw-truncation'))).toBe('Only the 500 oldest are in this operation; run it again for the rest (120 more match).');
    expect(textOf(el.querySelector('.bw-expiry'))).toBe('This review is valid until 15:52 UTC. (14:32 left)');
    expect(el.querySelector('.bw-countdown')?.getAttribute('aria-hidden')).toBe('true');
  });

  it('opens Protected and skipped while it lists anything', () => {
    render(buildPreview());

    const details = el.querySelector<HTMLDetailsElement>('details.bw-protected')!;
    expect(details.open).toBe(true);
    expect(textOf(details.querySelector('summary'))).toBe('Protected and skipped (2)');
    expect(textOf(details.querySelector('[data-code="game-data"] .bw-refused-title'))).toBe('Game data (game logs, bones or save files) — 2');
    expect(textOf(details.querySelector('[data-code="game-data"] .bw-refused-names'))).toBe('dan, eve');

    render(buildPreview({ refused: [], refusedCount: 0 }));
    expect(el.querySelector<HTMLDetailsElement>('details.bw-protected')!.open).toBe(false);
  });

  it('asks for the danger acknowledgments and the phrase on a delete', () => {
    render(buildPreview());

    expect(Array.from(el.querySelectorAll('.bw-acks .checkbox-label')).map(l => textOf(l))).toEqual([
      'I have reviewed the 3 accounts listed above.',
      'I understand deleted accounts and their data cannot be restored.'
    ]);
    expect(textOf(el.querySelector('label[for="bw-phrase"]'))).toBe('Type DELETE 3 ACCOUNTS to confirm');
    expect(el.querySelector('#bw-phrase')?.getAttribute('pattern')).toBe('\\s*DELETE 3 ACCOUNTS\\s*');
  });

  it('asks a caution action for the review acknowledgment only, and a safe one for none', () => {
    render(buildPreview({ action: 'LockOut', dangerLevel: 'caution', requiredAcknowledgments: ['reviewed'], dataTotals: null, requiredPhrase: null }));
    expect(Array.from(el.querySelectorAll('.bw-acks input')).map(i => i.id)).toEqual(['bw-ack-reviewed']);
    expect(el.querySelector('#bw-phrase')).toBeNull();
    expect(el.querySelector('.gh-danger-zone')).toBeNull();
    expect(el.querySelector('.bw-effect.alert-warning')).toBeTruthy();

    render(buildPreview({ action: 'Unlock', dangerLevel: 'safe', requiredAcknowledgments: [], dataTotals: null, requiredPhrase: null }));
    expect(el.querySelector('.bw-acks')).toBeNull();
    expect(el.querySelector('.bw-effect.alert-info')).toBeTruthy();
  });

  it('excludes an account its checkbox unchecks, and the labels follow the live count', () => {
    const preview = buildPreview();
    render(preview);

    const bob = el.querySelector<HTMLInputElement>('#bw-include-u2')!;
    expect(bob.checked).toBe(true);
    expect(bob.getAttribute('aria-label')).toBe('Include bob');
    bob.click();
    expect(last()).toEqual({ ...EMPTY_REVIEW_CHOICES, excluded: ['u2'] });

    render(preview, last());
    expect(textOf(el.querySelector('#bw-review-summary'))).toBe('2 accounts will be deleted · 2 protected and skipped · 0 already done');
    expect(textOf(el.querySelector('.bw-acks .checkbox-label'))).toBe('I have reviewed the 2 accounts listed above.');
    expect(textOf(el.querySelector('label[for="bw-phrase"]'))).toBe('Type DELETE 2 ACCOUNTS to confirm');
    expect(el.querySelector('[data-user-id="u2"]')?.classList.contains('is-excluded')).toBe(true);
  });

  it('marks a phrase typed for an earlier count invalid', () => {
    const preview = buildPreview();
    render(preview, { ...EMPTY_REVIEW_CHOICES, phrase: 'DELETE 3 ACCOUNTS' });
    expect(el.querySelector('#bw-phrase')?.getAttribute('aria-invalid')).toBeNull();

    render(preview, { ...EMPTY_REVIEW_CHOICES, phrase: 'DELETE 3 ACCOUNTS', excluded: ['u1'] });
    expect(el.querySelector('#bw-phrase')?.getAttribute('aria-invalid')).toBe('true');
  });

  it('keeps the advanced accounts behind a closed disclosure, and closing it unchecks its box', () => {
    const preview = withAdvanced();
    render(preview, { ...EMPTY_REVIEW_CHOICES, acknowledgments: ['reviewed'] });

    const details = el.querySelector<HTMLDetailsElement>('details.gh-advanced-disclosure')!;
    expect(details.open).toBe(false);
    expect(textOf(details.querySelector('summary'))).toBe('Advanced: accounts with a confirmed email (1)');
    expect(el.querySelector('#bw-ack-advanced')).toBeNull();

    details.open = true;
    el.querySelector<HTMLInputElement>('#bw-include-advanced')!.click();
    expect(last()).toEqual({ ...EMPTY_REVIEW_CHOICES, acknowledgments: ['reviewed'], includeAdvanced: true });

    render(preview, { ...EMPTY_REVIEW_CHOICES, includeAdvanced: true, acknowledgments: ['reviewed', 'advanced'] });
    expect(textOf(el.querySelector('#bw-ack-advanced')?.closest('label'))).toBe('I understand this includes 1 account with a confirmed email address.');
    expect(textOf(el.querySelector('label[for="bw-phrase"]'))).toBe('Type DELETE 4 ACCOUNTS to confirm');

    details.open = false;
    details.dispatchEvent(new Event('toggle'));
    expect(last()).toEqual({ ...EMPTY_REVIEW_CHOICES, includeAdvanced: false, acknowledgments: ['reviewed'] });
  });

  it('shows no advanced disclosure for any other action', () => {
    render(buildPreview({
      action: 'Ban', dangerLevel: 'caution', requiredAcknowledgments: ['reviewed'], requiredPhrase: null,
      advanced: [item('a1', 'amy', { isAdvanced: true })], advancedCount: 1
    }));
    expect(el.querySelector('details.gh-advanced-disclosure')).toBeNull();
  });

  it('shows the refreshed note and the execute error', () => {
    fixture.componentRef.setInput('refreshedNote', 'The review expired and was refreshed. 2 accounts changed; check the list again.');
    fixture.componentRef.setInput('executeError', 'Another bulk operation is running.');
    render(buildPreview());

    expect(textOf(el.querySelector('.bw-refreshed[role="alert"]'))).toBe('The review expired and was refreshed. 2 accounts changed; check the list again.');
    expect(textOf(el.querySelector('.bw-execute-error[role="alert"]'))).toBe('Another bulk operation is running.');
  });
});
