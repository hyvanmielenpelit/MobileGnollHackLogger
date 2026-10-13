import {
  ChangeDetectionStrategy,
  ChangeDetectorRef,
  Component,
  ElementRef,
  EventEmitter,
  Input,
  OnInit,
  Output,
  inject
} from '@angular/core';

import { AdminUser, AdminUserStatus } from '../../../services/admin-users.service';
import { ensureOverlayPolyfills, refreshAnchorPositioning } from '../../../utils/polyfills.util';
import { formatUtcDay, relativeTime } from '../user-status/admin-user-status';
import { UserStatusBadgesComponent } from '../user-status/user-status-badges.component';
import { AdminUserActionGroup, AdminUserActionId, AdminUserActionItem, accountActionGroups } from './admin-user-card-actions';

/**
 * One account of the Users list: the name, email, registration and last sign-in, the status badges,
 * the facts, the note of the last change, and the actions — Manage groups, Manage AI configurations,
 * Security events, and an Actions popover of the account actions in three headed groups. It emits what
 * the user chose and changes nothing itself.
 *
 * Ids: the title `au-{id}-title` (focused after Show more and after a delete), the Actions trigger
 * `au-{id}-actions-btn` (focused after an action), the popover `au-{id}-actions`.
 */
@Component({
  selector: 'app-admin-user-card',
  standalone: true,
  imports: [UserStatusBadgesComponent],
  templateUrl: './admin-user-card.component.html',
  styleUrl: './admin-user-card.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class AdminUserCardComponent implements OnInit {
  private readonly cdr = inject(ChangeDetectorRef);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);

  @Input({ required: true }) user!: AdminUser;
  /** The server's clock for "3 days ago"; the browser's when null. */
  @Input() nowUtc: string | null = null;
  /** `In good standing → Locked out until …`, while the last change is shown. */
  @Input() changeNote: string | null = null;
  /** Badges an action just added. */
  @Input() highlight: readonly AdminUserStatus[] = [];
  /** An action on this account is in flight. */
  @Input() busy = false;

  @Output() readonly action = new EventEmitter<AdminUserActionId>();
  @Output() readonly manageGroups = new EventEmitter<void>();
  @Output() readonly manageConfigs = new EventEmitter<void>();
  @Output() readonly securityEvents = new EventEmitter<void>();
  @Output() readonly dismissNote = new EventEmitter<void>();

  /** Whether the Actions popover is open; the popover polyfill does not set `aria-expanded`. */
  actionsOpen = false;

  private groupsMemo: { user: AdminUser; groups: AdminUserActionGroup[] } | null = null;

  ngOnInit(): void {
    ensureOverlayPolyfills();
  }

  get idBase(): string {
    return `au-${this.user.id}`;
  }

  get actionGroups(): AdminUserActionGroup[] {
    if (this.groupsMemo?.user !== this.user) {
      this.groupsMemo = { user: this.user, groups: accountActionGroups(this.user) };
    }
    return this.groupsMemo.groups;
  }

  /** `Registered 2026-03-02`, `Registered on or before 2026-10-14`, or that it is not recorded. */
  get registeredText(): string {
    const day = formatUtcDay(this.user.createdUtc);
    if (!day) return 'Registration date not recorded';
    return this.user.createdUtcIsEstimate ? `Registered on or before ${day}` : `Registered ${day}`;
  }

  /** `Last sign-in 3 days ago`, or `Last sign-in not recorded`. */
  get lastSignInText(): string {
    const when = relativeTime(this.user.lastSignInUtc, this.nowUtc);
    return when ? `Last sign-in ${when}` : 'Last sign-in not recorded';
  }

  /** `12 game logs · 3 bones`, or `None`. */
  get gameDataText(): string {
    const parts: string[] = [];
    const logs = this.user.gameLogCount ?? 0;
    const bones = this.user.bonesCount ?? 0;
    if (logs > 0) parts.push(`${logs} game log${logs === 1 ? '' : 's'}`);
    if (bones > 0) parts.push(`${bones} bones`);
    return parts.length > 0 ? parts.join(' · ') : 'None';
  }

  get twoFactorText(): string {
    return this.user.twoFactorEnabled ? 'On' : 'Off';
  }

  itemId(item: AdminUserActionItem): string {
    return `${this.idBase}-action-${item.id}`;
  }

  /** An unavailable item explains itself and does nothing. */
  choose(item: AdminUserActionItem): void {
    if (item.disabledReason) return;
    this.hideActions();
    this.trigger()?.focus();
    this.action.emit(item.id);
  }

  onActionsToggle(event: Event): void {
    const open = (event as ToggleEvent).newState === 'open';
    this.actionsOpen = open;
    this.cdr.markForCheck();
    const popover = this.popover();
    if (open) {
      refreshAnchorPositioning();
      popover?.querySelector<HTMLElement>('.gh-action-popover-item:not([aria-disabled="true"])')?.focus();
      return;
    }
    const active = document.activeElement;
    if (!active || active === document.body || !!popover?.contains(active)) {
      this.trigger()?.focus();
    }
  }

  /** Escape closes the popover only. */
  onActionsKeydown(event: KeyboardEvent): void {
    if (event.key !== 'Escape') return;
    event.preventDefault();
    event.stopPropagation();
    this.hideActions();
    this.trigger()?.focus();
  }

  private hideActions(): void {
    try {
      this.popover()?.hidePopover();
    } catch {
      // Already hidden.
    }
    this.actionsOpen = false;
    this.cdr.markForCheck();
  }

  private popover(): HTMLElement | null {
    return this.host.nativeElement.querySelector<HTMLElement>(`[id="${this.idBase}-actions"]`);
  }

  private trigger(): HTMLElement | null {
    return this.host.nativeElement.querySelector<HTMLElement>(`[id="${this.idBase}-actions-btn"]`);
  }
}
