import {
  ChangeDetectionStrategy,
  ChangeDetectorRef,
  Component,
  ElementRef,
  EventEmitter,
  Input,
  OnChanges,
  OnDestroy,
  OnInit,
  Output,
  SimpleChanges,
  ViewChild,
  inject
} from '@angular/core';

import {
  AccountDeletionCategory,
  AdminBulkExecuteRequest,
  AdminBulkPreview,
  AdminBulkPreviewItem,
  AdminBulkRefusedGroup,
  bulkConfirmationPhrase
} from '../../../services/admin-users.service';
import { CardListState } from '../../../shared/data-table/card-list-state';
import { TableState, customFilter } from '../../../shared/data-table/table-state';
import { missingText } from '../user-action-dialogs/user-action-dialogs';
import { formatUtcDay } from '../user-status/admin-user-status';
import { parseServerUtcDate } from '../../../utils/date.util';
import { UserStatusBadgesComponent } from '../user-status/user-status-badges.component';
import { bulkActionInfo } from './bulk-action-step.component';

/** The choices the review asks for, before the operation runs. The wizard owns them. */
export interface BulkReviewChoices {
  /** Eligible accounts the admin unchecked, by user id. */
  excluded: readonly string[];
  /** Delete only: also delete the accounts with a confirmed email. */
  includeAdvanced: boolean;
  /** `reviewed`, `irreversible`, `advanced`. */
  acknowledgments: readonly string[];
  /** Delete only: the typed confirmation phrase. */
  phrase: string;
}

export const EMPTY_REVIEW_CHOICES: BulkReviewChoices = { excluded: [], includeAdvanced: false, acknowledgments: [], phrase: '' };

/** Whether the advanced accounts are in: chosen, and there are any. */
function advancedIncluded(preview: AdminBulkPreview, choices: BulkReviewChoices): boolean {
  return choices.includeAdvanced && preview.action === 'Delete' && preview.advanced.length > 0;
}

/** How many accounts the operation would run on: the eligible ones left included, plus the advanced ones when included. */
export function bulkLiveCount(preview: AdminBulkPreview, choices: BulkReviewChoices): number {
  const excluded = new Set(choices.excluded);
  const eligible = preview.eligible.filter(item => !excluded.has(item.userId)).length;
  return eligible + (advancedIncluded(preview, choices) ? preview.advanced.length : 0);
}

/** The acknowledgments the server requires: the danger level's, plus `advanced` with the advanced accounts. */
export function bulkRequiredAcknowledgments(preview: AdminBulkPreview, choices: BulkReviewChoices): string[] {
  const required = [...preview.requiredAcknowledgments];
  if (advancedIncluded(preview, choices) && !required.includes('advanced')) required.push('advanced');
  return required;
}

/** `DELETE 37 ACCOUNTS` for a delete, following the live count; null for every other action. */
export function bulkRequiredPhrase(preview: AdminBulkPreview, choices: BulkReviewChoices): string | null {
  return preview.action === 'Delete' ? bulkConfirmationPhrase(bulkLiveCount(preview, choices)) : null;
}

/** Whether the typed phrase is the one the live count needs; true when none is needed. */
export function bulkPhraseMatches(preview: AdminBulkPreview, choices: BulkReviewChoices): boolean {
  const required = bulkRequiredPhrase(preview, choices);
  return required === null || choices.phrase.trim() === required;
}

/** `Still needed: every acknowledgment and DELETE 37 ACCOUNTS typed exactly.`, or null when the operation can run. */
export function bulkReviewMissing(preview: AdminBulkPreview, choices: BulkReviewChoices): string | null {
  const missing: string[] = [];
  if (bulkLiveCount(preview, choices) === 0) missing.push('at least one account included');
  const checked = new Set(choices.acknowledgments);
  if (bulkRequiredAcknowledgments(preview, choices).some(key => !checked.has(key))) missing.push('every acknowledgment');
  const phrase = bulkRequiredPhrase(preview, choices);
  if (phrase !== null && !bulkPhraseMatches(preview, choices)) missing.push(`${phrase} typed exactly`);
  return missingText(missing);
}

/** The execute request the choices make. */
export function bulkExecuteRequest(preview: AdminBulkPreview, choices: BulkReviewChoices): AdminBulkExecuteRequest {
  const eligible = new Set(preview.eligible.map(item => item.userId));
  const required = new Set(bulkRequiredAcknowledgments(preview, choices));
  return {
    excludedUserIds: choices.excluded.filter(id => eligible.has(id)),
    includeAdvanced: advancedIncluded(preview, choices),
    acknowledgments: choices.acknowledgments.filter(key => required.has(key)),
    confirmationPhrase: preview.action === 'Delete' ? choices.phrase.trim() : ''
  };
}

/** `15:52 UTC`; empty for an unreadable value. */
function utcTime(utc: string): string {
  const date = parseServerUtcDate(utc);
  if (isNaN(date.getTime())) return '';
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${pad(date.getUTCHours())}:${pad(date.getUTCMinutes())} UTC`;
}

/** The accounts list's batch. */
export const BULK_REVIEW_BATCH = 50;

/**
 * Step 3 of the Bulk Actions wizard, in this order: the danger header, the summary with the cap and
 * the expiry, the protected and skipped accounts, the eligible accounts with an include checkbox each,
 * the advanced accounts behind a closed disclosure (delete only), the acknowledgments and the typed
 * phrase. The run button is the wizard's footer. Presentational for the choices: the wizard owns them
 * and feeds back what `choicesChange` emits.
 */
@Component({
  selector: 'app-bulk-review-step',
  standalone: true,
  imports: [UserStatusBadgesComponent],
  templateUrl: './bulk-review-step.component.html',
  styleUrl: './bulk-review-step.component.scss',
  changeDetection: ChangeDetectionStrategy.Eager   // matches every other component here
})
export class BulkReviewStepComponent implements OnInit, OnChanges, OnDestroy {
  private readonly cdr = inject(ChangeDetectorRef);

  @ViewChild('advancedDetails') advancedDetails?: ElementRef<HTMLDetailsElement>;

  @Input({ required: true }) preview!: AdminBulkPreview;
  @Input() choices: BulkReviewChoices = EMPTY_REVIEW_CHOICES;
  /** What the action does to each account. */
  @Input() effectText = '';
  /** After an expired review was refreshed: what changed. */
  @Input() refreshedNote: string | null = null;
  /** Why the operation did not start. */
  @Input() executeError: string | null = null;
  /** While the operation is being started or the review refreshed. */
  @Input() busy = false;

  @Output() readonly choicesChange = new EventEmitter<BulkReviewChoices>();

  /** The eligible accounts' search and batches. */
  readonly table = new TableState<AdminBulkPreviewItem>('userName', 'asc').registerAccessors({}, {
    search: customFilter<AdminBulkPreviewItem>((item, value) => {
      const term = value.toLowerCase();
      return item.userName.toLowerCase().includes(term) || item.email.toLowerCase().includes(term);
    })
  });

  readonly list = new CardListState<AdminBulkPreviewItem>(this.table, {
    idPrefix: 'bw-review',
    batch: BULK_REVIEW_BATCH,
    sorts: [],
    defaultSort: '',
    storageKey: 'overseer.admin.users.bulkReview.view',
    facets: [],
    onChange: () => this.cdr.markForCheck()
  });

  /** The clock the countdown counts from. */
  now: () => number = () => Date.now();

  /** `14:32 left`; visible, never announced. */
  countdown = '';

  private countdownTimer: ReturnType<typeof setInterval> | null = null;

  ngOnInit(): void {
    this.tick();
    this.countdownTimer = setInterval(() => this.tick(), 1000);
  }

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['preview'] && !changes['preview'].firstChange) {
      // A refreshed review starts over: the whole list, and the advanced accounts closed.
      this.list.reset();
      const details = this.advancedDetails?.nativeElement;
      if (details) details.open = false;
      this.tick();
    }
  }

  ngOnDestroy(): void {
    if (this.countdownTimer !== null) clearInterval(this.countdownTimer);
    this.list.dispose();
  }

  // --- Reading ---

  get info() {
    return bulkActionInfo(this.preview.action);
  }

  get isDelete(): boolean {
    return this.preview.action === 'Delete';
  }

  get liveCount(): number {
    return bulkLiveCount(this.preview, this.choices);
  }

  get requiredAcknowledgments(): string[] {
    return bulkRequiredAcknowledgments(this.preview, this.choices);
  }

  get requiredPhrase(): string | null {
    return bulkRequiredPhrase(this.preview, this.choices);
  }

  /** The phrase field's own validity, for `aria-invalid`: wrong once something is typed. */
  get phraseInvalid(): boolean {
    return this.choices.phrase !== '' && !bulkPhraseMatches(this.preview, this.choices);
  }

  /** The `pattern` the phrase field checks with `:user-invalid`; surrounding spaces are allowed, as the server trims. */
  get phrasePattern(): string | null {
    const phrase = this.requiredPhrase;
    return phrase === null ? null : `\\s*${phrase}\\s*`;
  }

  /** `37 accounts will be deleted · 391 protected and skipped · 0 already done`. */
  get summaryText(): string {
    const n = this.liveCount;
    const accounts = `${n.toLocaleString('en-US')} account${n === 1 ? '' : 's'} ${this.info.willBe}`;
    return `${accounts} · ${this.preview.refusedCount.toLocaleString('en-US')} protected and skipped · ${this.preview.noOpCount.toLocaleString('en-US')} already done`;
  }

  get truncationText(): string | null {
    const more = this.preview.truncatedCount;
    if (more <= 0) return null;
    return `Only the ${this.preview.maxUsers.toLocaleString('en-US')} oldest are in this operation; run it again for the rest `
      + `(${more.toLocaleString('en-US')} more match).`;
  }

  get expiryText(): string {
    const time = utcTime(this.preview.expiresUtc);
    return time ? `This review is valid until ${time}.` : 'This review is valid for a short time.';
  }

  get protectedCount(): number {
    return this.preview.refusedCount + this.preview.noOpCount;
  }

  get hasProtected(): boolean {
    return this.preview.refused.length > 0 || this.preview.noOpCount > 0;
  }

  get hasAdvanced(): boolean {
    return this.isDelete && this.preview.advanced.length > 0;
  }

  get advancedCount(): number {
    return this.preview.advanced.length;
  }

  get eligibleView(): AdminBulkPreviewItem[] {
    return this.list.view(this.preview.eligible);
  }

  get remainingCount(): number {
    return this.list.remainingCount(this.preview.eligible);
  }

  get nextBatchCount(): number {
    return this.list.nextBatchCount(this.preview.eligible);
  }

  get listStatus(): string {
    return this.list.statusText(this.preview.eligible, { one: 'account', many: 'accounts' });
  }

  isExcluded(item: AdminBulkPreviewItem): boolean {
    return this.choices.excluded.includes(item.userId);
  }

  isAcknowledged(key: string): boolean {
    return this.choices.acknowledgments.includes(key);
  }

  acknowledgmentText(key: string): string {
    const n = this.liveCount;
    switch (key) {
      case 'reviewed':
        return `I have reviewed the ${n.toLocaleString('en-US')} account${n === 1 ? '' : 's'} listed above.`;
      case 'irreversible':
        return 'I understand deleted accounts and their data cannot be restored.';
      case 'advanced': {
        const a = this.advancedCount;
        return `I understand this includes ${a} account${a === 1 ? '' : 's'} with a confirmed email address.`;
      }
      default:
        return key;
    }
  }

  /** Delete only: the data the accounts hold, zero by construction and shown anyway. */
  get dataTotals(): AccountDeletionCategory[] {
    return this.preview.dataTotals ?? [];
  }

  /** `alice, bob and 38 more`: a refusal's example user names. */
  namesText(group: AdminBulkRefusedGroup): string {
    const names = group.exampleUserNames.join(', ');
    const more = group.count - group.exampleUserNames.length;
    return more > 0 ? `${names} and ${more.toLocaleString('en-US')} more` : names;
  }

  registeredText(item: AdminBulkPreviewItem): string {
    const day = formatUtcDay(item.createdUtc);
    if (!day) return 'Registration date not recorded';
    return item.createdUtcIsEstimate ? `Registered on or before ${day}` : `Registered ${day}`;
  }

  signInText(item: AdminBulkPreviewItem): string {
    const day = formatUtcDay(item.lastSignInUtc);
    return day ? `Last sign-in ${day}` : 'Last sign-in not recorded';
  }

  // --- Events ---

  onSearchInput(event: Event): void {
    this.list.setSearchInput((event.target as HTMLInputElement).value);
  }

  /** Escape with text clears the search; with none it is left to the dialog. */
  onSearchKeydown(event: KeyboardEvent): void {
    this.list.clearSearchOnEscape(event);
  }

  /** Shows the next batch and focuses the first new account's checkbox. */
  showMore(): void {
    const index = this.list.showMore(this.preview.eligible);
    this.cdr.detectChanges();
    const item = this.list.matching(this.preview.eligible)[index];
    if (item) document.getElementById(`bw-include-${item.userId}`)?.focus();
  }

  onInclude(item: AdminBulkPreviewItem, event: Event): void {
    const included = (event.target as HTMLInputElement).checked;
    const excluded = this.choices.excluded.filter(id => id !== item.userId);
    if (!included) excluded.push(item.userId);
    this.emit({ excluded });
  }

  /** Closing the advanced disclosure takes the advanced accounts back out. */
  onAdvancedToggle(event: Event): void {
    const open = (event.target as HTMLDetailsElement).open;
    if (!open && this.choices.includeAdvanced) {
      this.emit({ includeAdvanced: false, acknowledgments: this.choices.acknowledgments.filter(key => key !== 'advanced') });
    }
  }

  onIncludeAdvanced(event: Event): void {
    const checked = (event.target as HTMLInputElement).checked;
    this.emit(checked
      ? { includeAdvanced: true }
      : { includeAdvanced: false, acknowledgments: this.choices.acknowledgments.filter(key => key !== 'advanced') });
  }

  onAcknowledge(key: string, event: Event): void {
    const checked = (event.target as HTMLInputElement).checked;
    const acknowledgments = this.choices.acknowledgments.filter(k => k !== key);
    if (checked) acknowledgments.push(key);
    this.emit({ acknowledgments });
  }

  onPhrase(event: Event): void {
    this.emit({ phrase: (event.target as HTMLInputElement).value });
  }

  private emit(change: Partial<BulkReviewChoices>): void {
    this.choicesChange.emit({ ...this.choices, ...change });
  }

  /** The countdown to the review's expiry, in minutes and seconds. */
  private tick(): void {
    const expires = parseServerUtcDate(this.preview.expiresUtc).getTime();
    if (isNaN(expires)) {
      this.countdown = '';
      return;
    }
    const left = Math.max(0, Math.floor((expires - this.now()) / 1000));
    const text = left === 0
      ? 'expired; running it refreshes the review'
      : `${Math.floor(left / 60)}:${String(left % 60).padStart(2, '0')} left`;
    if (text !== this.countdown) {
      this.countdown = text;
      this.cdr.markForCheck();
    }
  }
}
