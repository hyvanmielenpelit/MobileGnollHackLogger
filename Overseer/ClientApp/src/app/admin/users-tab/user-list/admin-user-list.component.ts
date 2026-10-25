import {
  ChangeDetectionStrategy,
  ChangeDetectorRef,
  Component,
  ElementRef,
  OnDestroy,
  OnInit,
  ViewChild,
  inject
} from '@angular/core';
import { Observable, Subscription } from 'rxjs';

import { AdminService, GroupDto } from '../../../services/admin.service';
import {
  AdminUser,
  AdminUserActionResult,
  AdminUserCriteria,
  AdminUserFacetCounts,
  AdminUserSort,
  AdminUserStatus,
  AdminUsersService,
  adminApiError
} from '../../../services/admin-users.service';
import { refreshAnchorPositioning, ensureOverlayPolyfills } from '../../../utils/polyfills.util';
import { AdminConfirmDialogComponent } from '../../admin-dialogs/admin-confirm-dialog.component';
import { AdminReasonDialogComponent } from '../../admin-dialogs/admin-reason-dialog.component';
import { ConfigAccessDialogComponent } from '../../admin-dialogs/config-access-dialog/config-access-dialog.component';
import { AdminPageStore } from '../../admin-page.store';
import { AdminUserCardComponent } from '../user-card/admin-user-card.component';
import { AdminUserActionId } from '../user-card/admin-user-card-actions';
import { UserBanDialogComponent } from '../user-action-dialogs/user-ban-dialog.component';
import { UserChangeEmailDialogComponent } from '../user-action-dialogs/user-change-email-dialog.component';
import { UserDeleteDialogComponent } from '../user-action-dialogs/user-delete-dialog.component';
import { UserActionDone, ADMIN_REASON_MAX } from '../user-action-dialogs/user-action-dialogs';
import { UserLockoutDialogComponent } from '../user-action-dialogs/user-lockout-dialog.component';
import { UserRestrictionsDialogComponent } from '../user-action-dialogs/user-restrictions-dialog.component';
import { UserCriteriaBarComponent } from '../user-criteria/user-criteria-bar.component';
import { hasAnyCriteria, hasFilterCriteria } from '../user-criteria/user-criteria';
import { addedStatuses, changeNote } from '../user-status/admin-user-status';

/** The users the list fetches at first, and how many more Show more fetches. */
export const USERS_BATCH = 20;

/** The longest AI-chat block reason the server accepts; it is shown to the user. */
export const AI_CHAT_BLOCK_REASON_MAX = 256;

/** The longest deletion hold reason the server accepts. */
export const ACCOUNT_HOLD_REASON_MAX = 512;

const NO_STATUSES: readonly AdminUserStatus[] = [];

/** The note of the last change on a card, until dismissed or reloaded. */
interface CardChange {
  note: string;
  highlight: AdminUserStatus[];
}

/**
 * The Users sub-tab: a server-paged card list of accounts under the shared criteria bar, each card
 * with its status badges and actions. Every account action goes through one handler, which opens the
 * action's dialog, calls the server, replaces the card's account with the result, shows the before →
 * after note, announces the summary in the page toast and returns focus to the card's Actions. A
 * deleted account stays as a tombstone until dismissed or reloaded. The Manage Groups and AI
 * configuration access dialogs live here.
 */
@Component({
  selector: 'app-admin-user-list',
  standalone: true,
  imports: [
    UserCriteriaBarComponent, AdminUserCardComponent,
    UserLockoutDialogComponent, UserBanDialogComponent, UserRestrictionsDialogComponent, UserDeleteDialogComponent,
    UserChangeEmailDialogComponent, AdminConfirmDialogComponent, AdminReasonDialogComponent, ConfigAccessDialogComponent
  ],
  templateUrl: './admin-user-list.component.html',
  styleUrl: './admin-user-list.component.scss',
  changeDetection: ChangeDetectionStrategy.Eager   // matches every other component here
})
export class AdminUserListComponent implements OnInit, OnDestroy {
  private readonly usersService = inject(AdminUsersService);
  private readonly adminService = inject(AdminService);
  private readonly store = inject(AdminPageStore);
  private readonly cdr = inject(ChangeDetectorRef);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);

  @ViewChild(UserCriteriaBarComponent) criteriaBar?: UserCriteriaBarComponent;
  @ViewChild(UserLockoutDialogComponent, { static: true }) lockoutDialog!: UserLockoutDialogComponent;
  @ViewChild(UserBanDialogComponent, { static: true }) banDialog!: UserBanDialogComponent;
  @ViewChild(UserRestrictionsDialogComponent, { static: true }) restrictionsDialog!: UserRestrictionsDialogComponent;
  @ViewChild(UserDeleteDialogComponent, { static: true }) deleteDialog!: UserDeleteDialogComponent;
  @ViewChild(UserChangeEmailDialogComponent, { static: true }) changeEmailDialog!: UserChangeEmailDialogComponent;
  @ViewChild(AdminConfirmDialogComponent, { static: true }) confirmDialog!: AdminConfirmDialogComponent;
  @ViewChild(AdminReasonDialogComponent, { static: true }) reasonDialog!: AdminReasonDialogComponent;
  @ViewChild(ConfigAccessDialogComponent, { static: true }) configAccess!: ConfigAccessDialogComponent;
  @ViewChild('manageGroupsDialog', { static: true }) manageGroupsDialog!: ElementRef<HTMLDialogElement>;
  @ViewChild('manageGroupsTitle', { static: true }) manageGroupsTitle!: ElementRef<HTMLElement>;

  /** The accounts fetched so far, in the server's order; a deleted one stays until dismissed. */
  rows: AdminUser[] = [];
  totalCount = 0;
  nowUtc: string | null = null;
  loading = false;
  loaded = false;
  error: string | null = null;

  /** The counts per facet option under the current search; null until counted. */
  facetCounts: AdminUserFacetCounts | null = null;

  /** The account whose action is in flight. */
  busyUserId: string | null = null;
  /** The last change per account, by id. */
  changes: Record<string, CardChange> = {};
  /** Deleted accounts still shown: `erased 0 game logs, 2 chats`, by id. */
  tombstones: Record<string, string> = {};

  // --- Manage Groups ---
  selectedUser: AdminUser | null = null;

  private listSub: Subscription | null = null;
  private countsSub: Subscription | null = null;
  private actionSub: Subscription | null = null;
  private manageUserSub: Subscription | null = null;
  /** The search the facet counts were taken for; null forces a recount. */
  private countedSearch: string | null = null;

  get groups(): GroupDto[] {
    return this.store.groups;
  }

  get criteria(): AdminUserCriteria {
    return this.store.usersQuery.criteria;
  }

  get sort(): AdminUserSort {
    return this.store.usersQuery.sort;
  }

  ngOnInit(): void {
    ensureOverlayPolyfills();
    this.reload();
    this.takePendingManageUser();
    this.manageUserSub = this.store.manageUserRequested$.subscribe(() => this.takePendingManageUser());
  }

  ngOnDestroy(): void {
    this.listSub?.unsubscribe();
    this.countsSub?.unsubscribe();
    this.actionSub?.unsubscribe();
    this.manageUserSub?.unsubscribe();
    if (this.manageGroupsDialog?.nativeElement.open) this.manageGroupsDialog.nativeElement.close();
  }

  /**
   * Reads and clears the page store's pending manage request and opens the AI configuration access
   * dialog for that user, which needs only the id and the name.
   */
  private takePendingManageUser(): void {
    const pending = this.store.pendingManageUser;
    if (!pending) {
      return;
    }
    this.store.pendingManageUser = null;
    this.openManageUserConfigs({ id: pending.userId, userName: pending.userName });
  }

  // --- Criteria ---

  onCriteriaChange(criteria: AdminUserCriteria): void {
    this.store.usersQuery.criteria = criteria;
    this.reload();
  }

  onSortChange(sort: AdminUserSort): void {
    this.store.usersQuery.sort = sort;
    this.reload();
  }

  /** The empty state's Clear all filters: every criterion goes, and focus to the search. */
  clearAllFilters(): void {
    this.onCriteriaChange({});
    this.cdr.detectChanges();
    this.criteriaBar?.focusSearch();
  }

  get searchTerm(): string {
    return this.criteria.search?.trim() ?? '';
  }

  get filtered(): boolean {
    return hasAnyCriteria(this.criteria);
  }

  /** The accounts on screen that still exist. */
  get liveCount(): number {
    return this.rows.filter(r => !this.tombstones[r.id]).length;
  }

  /** Accounts not fetched yet; a tombstone counts in both the rows and the total it was fetched with. */
  get remainingCount(): number {
    return Math.max(0, this.totalCount - this.rows.length);
  }

  get nextBatchCount(): number {
    return Math.min(USERS_BATCH, this.remainingCount);
  }

  /** `Showing 20 of 1,284 users · filtered from 9,102`; empty with no users or an error. */
  get listStatus(): string {
    if (this.loading && !this.loaded) return 'Loading users…';
    const shown = this.liveCount;
    const matching = Math.max(0, this.totalCount - (this.rows.length - shown));
    if (this.error || matching === 0) return '';
    const text = matching === 1 && shown === 1
      ? 'One user'
      : `Showing ${shown.toLocaleString('en-US')} of ${matching.toLocaleString('en-US')} users`;
    const total = this.facetCounts?.total;
    return hasFilterCriteria(this.criteria) && total != null && total !== matching
      ? `${text} · filtered from ${total.toLocaleString('en-US')}`
      : text;
  }

  // --- Fetching ---

  /** Fetches the first batch of the current criteria, replacing the list, and recounts when the search changed. */
  reload(): void {
    this.changes = {};
    this.tombstones = {};
    this.fetch(false);
    if (this.countedSearch !== this.searchTerm) {
      this.loadCounts();
    }
  }

  /** Appends the next batch and focuses the title of the first new card. */
  showMore(): void {
    if (this.loading || this.remainingCount === 0) return;
    this.fetch(true);
  }

  private fetch(append: boolean): void {
    if (!append) {
      this.loaded = false;
    }
    this.loading = true;
    this.error = null;
    const skip = append ? this.liveCount : 0;
    this.listSub?.unsubscribe();
    this.listSub = this.usersService.list(this.criteria, this.sort, skip, USERS_BATCH).subscribe({
      next: page => {
        const firstNew = append ? this.rows.length : -1;
        if (append) {
          const known = new Set(this.rows.map(r => r.id));
          this.rows = [...this.rows, ...page.rows.filter(r => !known.has(r.id))];
        } else {
          this.rows = page.rows;
        }
        this.totalCount = page.totalCount;
        this.nowUtc = page.nowUtc;
        this.loading = false;
        this.loaded = true;
        const focusId = firstNew >= 0 ? this.rows[firstNew]?.id ?? null : null;
        if (focusId === null) {
          this.cdr.markForCheck();
          return;
        }
        this.cdr.detectChanges();
        refreshAnchorPositioning();
        this.titleOf(focusId)?.focus();
      },
      error: err => {
        this.loading = false;
        this.loaded = true;
        this.error = 'The users could not be loaded: ' + adminApiError(err).message;
        this.cdr.markForCheck();
      }
    });
  }

  /** Counts each facet option under the current search; a failed count keeps the counts there are. */
  private loadCounts(): void {
    const search = this.searchTerm;
    this.countedSearch = search;
    this.countsSub?.unsubscribe();
    this.countsSub = this.usersService.facets(search || undefined).subscribe({
      next: counts => {
        this.facetCounts = counts;
        this.cdr.markForCheck();
      },
      error: () => {
        this.countedSearch = null;
      }
    });
  }

  // --- A card ---

  changeOf(user: AdminUser): string | null {
    return this.changes[user.id]?.note ?? null;
  }

  highlightOf(user: AdminUser): readonly AdminUserStatus[] {
    return this.changes[user.id]?.highlight ?? NO_STATUSES;
  }

  tombstoneOf(user: AdminUser): string | null {
    return this.tombstones[user.id] ?? null;
  }

  dismissNote(user: AdminUser): void {
    const { [user.id]: _, ...rest } = this.changes;
    this.changes = rest;
    this.cdr.detectChanges();
    this.element(`#au-${user.id}-actions-btn`)?.focus();
  }

  /** Removes a tombstone, then focuses the next card's title, else the previous one, else the heading. */
  dismissTombstone(user: AdminUser): void {
    const index = this.rows.findIndex(r => r.id === user.id);
    this.rows = this.rows.filter(r => r.id !== user.id);
    const { [user.id]: _, ...rest } = this.tombstones;
    this.tombstones = rest;
    this.totalCount = Math.max(0, this.totalCount - 1);
    this.cdr.detectChanges();
    this.focusNear(index);
  }

  showSecurityEvents(user: AdminUser): void {
    this.store.requestSecurityUser(user.id, user.userName);
  }

  // --- Account actions ---

  /** Opens the action's dialog; the dialog's confirmation calls the server through `run`. */
  onAction(user: AdminUser, action: AdminUserActionId): void {
    if (this.busyUserId !== null) return;
    const name = user.userName;
    switch (action) {
      case 'lockout':
        this.lockoutDialog.open(user);
        return;
      case 'ban':
        this.banDialog.open(user);
        return;
      case 'restrictions':
        this.restrictionsDialog.open(user);
        return;
      case 'changeEmail':
        this.changeEmailDialog.open(user);
        return;
      case 'delete':
        this.deleteDialog.open(user);
        return;
      case 'unlock':
        this.confirmDialog.open({
          title: `Unlock ${name}`,
          message: `${name} can sign in again on the website, in Overseer and in GnollHack. Failed sign-ins are reset. A game sign-in block after repeated wrong passwords can last up to 15 more minutes.`,
          buttonText: 'Unlock',
          buttonClass: 'btn-gh'
        }, () => this.run(user, this.usersService.unlock(user.id)));
        return;
      case 'unban':
        this.confirmDialog.open({
          title: `Unban ${name}`,
          message: `${name} can sign in again on the website, in Overseer and in GnollHack.`,
          buttonText: 'Unban',
          buttonClass: 'btn-gh'
        }, () => this.run(user, this.usersService.unban(user.id)));
        return;
      case 'signout':
        this.confirmDialog.open({
          title: `Sign out ${name} everywhere`,
          message: `Every signed-in session of ${name} on the website and in Overseer ends within 5 minutes. ${name} can sign in again.`,
          buttonText: 'Sign Out Everywhere',
          buttonClass: 'btn-gh'
        }, () => this.run(user, this.usersService.signOutEverywhere(user.id)));
        return;
      case 'confirmEmail':
        this.confirmDialog.open({
          title: `Mark the email of ${name} as confirmed`,
          message: `${user.email} is marked as confirmed, as if ${name} had followed the confirmation link.`,
          buttonText: 'Mark Confirmed',
          buttonClass: 'btn-gh'
        }, () => this.run(user, this.usersService.confirmEmail(user.id)));
        return;
      case 'resetTwoFactor':
        this.reasonDialog.open({
          title: 'Reset two-factor authentication',
          message: `Do this only after you have verified the identity of the person asking. ${name} can then sign in with the password alone, in GnollHack too. Signed-in sessions end within 5 minutes.`,
          label: 'Reason',
          maxLength: ADMIN_REASON_MAX,
          confirmText: 'Reset Two-Factor',
          confirmClass: 'btn-gh btn-gh-delete'
        }, reason => this.run(user, this.usersService.resetTwoFactor(user.id, reason)));
        return;
      case 'block':
        this.reasonDialog.open({
          title: 'Block from the Overseer AI chat',
          message: `${name} keeps signing in, reading their chats, exporting their data, deleting their account and changing their settings, but cannot start any AI work in Overseer. The reason is shown to them.`,
          label: 'Reason',
          maxLength: AI_CHAT_BLOCK_REASON_MAX,
          confirmText: 'Block',
          confirmClass: 'btn-gh btn-gh-delete'
        }, reason => this.run(user, this.usersService.blockFromAiChat(user.id, reason)));
        return;
      case 'unblock':
        this.confirmDialog.open({
          title: 'Unblock from the Overseer AI chat',
          message: `${name} can start AI work in Overseer again.`,
          buttonText: 'Unblock',
          buttonClass: 'btn-gh'
        }, () => this.run(user, this.usersService.unblockFromAiChat(user.id)));
        return;
      case 'hold':
        this.reasonDialog.open({
          title: 'Place a deletion hold',
          message: `Deleting ${name}'s account will be held for administrator review. The reason is never shown to the user.`,
          label: 'Reason',
          maxLength: ACCOUNT_HOLD_REASON_MAX,
          confirmText: 'Place Hold',
          confirmClass: 'btn-gh'
        }, reason => this.run(user, this.usersService.placeHold(user.id, reason)));
        return;
      case 'releaseHold':
        this.confirmDialog.open({
          title: 'Release the deletion hold',
          message: `${name}'s account is no longer held by an administrator. Releasing the hold does not approve a pending deletion request; decide it in Deletion requests.`,
          buttonText: 'Release Hold',
          buttonClass: 'btn-gh'
        }, () => this.run(user, this.usersService.releaseHold(user.id)));
        return;
    }
  }

  /** A custom dialog's success: the dialog has closed, the result is applied here. */
  onDialogDone(done: UserActionDone): void {
    this.applyResult(done.user, done.result, done.erasedText);
  }

  /** The delete dialog's *Ban instead*. */
  onBanInstead(user: AdminUser): void {
    this.banDialog.open(user);
  }

  /** Sends one action whose dialog closed on confirm; an error is a toast. */
  private run(user: AdminUser, request: Observable<AdminUserActionResult>): void {
    this.busyUserId = user.id;
    this.actionSub?.unsubscribe();
    this.actionSub = request.subscribe({
      next: result => {
        this.busyUserId = null;
        this.applyResult(user, result);
      },
      error: err => {
        this.busyUserId = null;
        this.store.showToast(adminApiError(err).message, 'error', 'Error');
        this.cdr.detectChanges();
        this.element(`#au-${user.id}-actions-btn`)?.focus();
      }
    });
  }

  /**
   * Replaces the card's account with the result and shows the before → after note, or makes the card a
   * tombstone after a delete; announces the summary and moves focus.
   */
  private applyResult(user: AdminUser, result: AdminUserActionResult, erased?: string): void {
    const index = this.rows.findIndex(r => r.id === user.id);
    if (result.deleted || !result.user) {
      this.tombstones = { ...this.tombstones, [user.id]: erased ?? 'erased' };
      const { [user.id]: _, ...rest } = this.changes;
      this.changes = rest;
      this.store.showToast(result.summary || `${user.userName} was deleted.`, 'success', 'Account Deleted');
      this.cdr.detectChanges();
      this.focusNear(index + 1, index);
      return;
    }
    const updated = result.user;
    if (index >= 0) {
      this.rows = this.rows.map(r => r.id === user.id ? updated : r);
    }
    this.changes = { ...this.changes, [user.id]: { note: changeNote(result), highlight: addedStatuses(result) } };
    this.store.showToast(result.summary || `${user.userName} was updated.`, 'success', 'Account Updated');
    this.cdr.detectChanges();
    this.element(`#au-${user.id}-actions-btn`)?.focus();
  }

  /**
   * Focuses the title of the first card from `index` that is not a tombstone, else the nearest before
   * `before` (default `index`), else the list section.
   */
  private focusNear(index: number, before = index): void {
    const live = (i: number) => this.rows[i] && !this.tombstones[this.rows[i].id] ? this.rows[i] : null;
    let target: AdminUser | null = null;
    for (let i = Math.max(0, index); i < this.rows.length && !target; i++) target = live(i);
    for (let i = Math.min(before, this.rows.length) - 1; i >= 0 && !target; i--) target = live(i);
    (target ? this.titleOf(target.id) : this.element('#au-section'))?.focus();
  }

  private titleOf(id: string): HTMLElement | null {
    return this.element(`[id="au-${id}-title"]`);
  }

  private element(selector: string): HTMLElement | null {
    try {
      return this.host.nativeElement.querySelector<HTMLElement>(selector);
    } catch {
      return null;
    }
  }

  // --- Users & Groups ---
  openManageUserGroups(user: AdminUser) {
    this.selectedUser = user;
    this.manageGroupsDialog.nativeElement.showModal();
    this.manageGroupsTitle.nativeElement.focus();
  }

  closeManageGroups() {
    this.manageGroupsDialog.nativeElement.close();
    this.selectedUser = null;
  }

  isUserInGroup(user: AdminUser, groupId: number): boolean {
    return user.groups?.some(g => g.id === groupId) || false;
  }

  toggleUserGroup(user: AdminUser, group: GroupDto, event: any) {
    const checked = event.target.checked;
    if (checked) {
      this.adminService.addUserToGroup(user.id, group.id).subscribe({
        next: () => {
          this.replaceGroups(user, [...(user.groups ?? []), group]);
        },
        error: () => {
          event.target.checked = false; // revert on error
        }
      });
    } else {
      this.adminService.removeUserFromGroup(user.id, group.id).subscribe({
        next: () => {
          this.replaceGroups(user, (user.groups ?? []).filter(g => g.id !== group.id));
        },
        error: () => {
          event.target.checked = true; // revert on error
        }
      });
    }
  }

  /** A new account object with the groups, so the card redraws; the open dialog follows it. */
  private replaceGroups(user: AdminUser, groups: AdminUser['groups']): void {
    const current = this.rows.find(r => r.id === user.id) ?? user;
    const updated: AdminUser = { ...current, groups };
    this.rows = this.rows.map(r => r.id === user.id ? updated : r);
    if (this.selectedUser?.id === user.id) {
      this.selectedUser = updated;
    }
    this.cdr.markForCheck();
  }

  // --- AI configuration access ---

  openManageUserConfigs(user: { id: string; userName: string }) {
    this.configAccess.open({ kind: 'user', id: user.id, name: user.userName });
  }
}
