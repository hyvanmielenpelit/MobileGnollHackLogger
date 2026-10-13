import { ChangeDetectionStrategy, Component, Input, OnInit } from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';

import { AdminUserStatus } from '../../../services/admin-users.service';
import { ensureOverlayPolyfills } from '../../../utils/polyfills.util';
import { AdminUserBadge, AdminUserBadgeSource, badgesFor } from './admin-user-status';

/**
 * One account's status badges: a list of `.gh-account-badge` words, each with its 12 px glyph. A badge
 * with details is a focusable button with a hint tooltip; one an action just added is highlighted.
 */
@Component({
  selector: 'app-user-status-badges',
  standalone: true,
  imports: [NgTemplateOutlet],
  templateUrl: './user-status-badges.component.html',
  styleUrl: './user-status-badges.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class UserStatusBadgesComponent implements OnInit {
  /** The account: an `AdminUser`, or anything with its statuses (a bulk preview item). */
  @Input({ required: true }) user!: AdminUserBadgeSource & { userName?: string };
  /** Unique in the document; the tooltips' ids and anchor names derive from it. */
  @Input({ required: true }) idPrefix!: string;
  /** Statuses to highlight once, as just added. */
  @Input() highlight: readonly AdminUserStatus[] = [];

  private memo: { user: AdminUserBadgeSource; badges: AdminUserBadge[] } | null = null;

  ngOnInit(): void {
    ensureOverlayPolyfills();
  }

  /** The badges, the same array until `user` changes. */
  get badges(): AdminUserBadge[] {
    if (this.memo?.user !== this.user) {
      this.memo = { user: this.user, badges: badgesFor(this.user) };
    }
    return this.memo.badges;
  }

  get listLabel(): string {
    return this.user.userName ? `Status of ${this.user.userName}` : 'Status';
  }

  tipId(badge: AdminUserBadge): string {
    return `${this.idPrefix}-badge-${badge.status}-tip`;
  }

  isNew(badge: AdminUserBadge): boolean {
    return this.highlight.includes(badge.status);
  }
}
