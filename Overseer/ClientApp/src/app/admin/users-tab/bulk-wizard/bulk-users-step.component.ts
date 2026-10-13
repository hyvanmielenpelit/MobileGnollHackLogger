import {
  ChangeDetectionStrategy,
  Component,
  EventEmitter,
  Input,
  Output
} from '@angular/core';

import { AdminUser, AdminUserCriteria, AdminUserFacetCounts } from '../../../services/admin-users.service';
import { UserCriteriaBarComponent } from '../user-criteria/user-criteria-bar.component';
import { cloneCriteria, criteriaKey } from '../user-criteria/user-criteria';
import { UserStatusBadgesComponent } from '../user-status/user-status-badges.component';

/** A starting point of step 1: a preset's criteria, or `custom` for the criteria as they stand. */
export type BulkUserPreset = 'unconfirmed-old' | 'locked-out' | 'banned' | 'custom';

export interface BulkUserPresetInfo {
  preset: BulkUserPreset;
  label: string;
  /** The preset's criteria; null for `custom`. */
  criteria: AdminUserCriteria | null;
}

/** The presets, in the order step 1 shows them. */
export const BULK_USER_PRESETS: readonly BulkUserPresetInfo[] = [
  {
    preset: 'unconfirmed-old',
    label: 'Unconfirmed accounts older than 30 days',
    criteria: { emailConfirmed: 'no', registeredOlderThanDays: 30, hasGameData: 'no', hasOverseerData: 'no' }
  },
  { preset: 'locked-out', label: 'Locked out', criteria: { statuses: ['LockedOut'] } },
  { preset: 'banned', label: 'Banned', criteria: { statuses: ['Banned'] } },
  { preset: 'custom', label: 'Custom', criteria: null }
];

/** A copy of a preset's criteria; empty criteria for `custom`. */
export function bulkPresetCriteria(preset: BulkUserPreset): AdminUserCriteria {
  return cloneCriteria(BULK_USER_PRESETS.find(p => p.preset === preset)?.criteria ?? {});
}

/** The preset whose criteria equal these, else `custom`. */
export function bulkPresetOf(criteria: AdminUserCriteria): BulkUserPreset {
  const key = criteriaKey(criteria);
  return BULK_USER_PRESETS.find(p => p.criteria !== null && criteriaKey(p.criteria) === key)?.preset ?? 'custom';
}

/** How many matching accounts step 1 shows as a sample. */
export const BULK_SAMPLE_SIZE = 10;

/**
 * Step 1 of the Bulk Actions wizard: the presets, the shared criteria bar (without Sort by), a live
 * count of the matching accounts and the first of them as compact rows. Presentational: the wizard
 * owns the criteria, fetches the matches and the facet counts, and passes them back in.
 */
@Component({
  selector: 'app-bulk-users-step',
  standalone: true,
  imports: [UserCriteriaBarComponent, UserStatusBadgesComponent],
  templateUrl: './bulk-users-step.component.html',
  styleUrl: './bulk-users-step.component.scss',
  changeDetection: ChangeDetectionStrategy.Eager   // matches every other component here
})
export class BulkUsersStepComponent {
  @Input() criteria: AdminUserCriteria = {};
  @Input() preset: BulkUserPreset = 'custom';
  @Input() facetCounts: AdminUserFacetCounts | null = null;
  /** The first matching accounts, oldest first. */
  @Input() matches: readonly AdminUser[] = [];
  /** How many accounts match; null while not counted. */
  @Input() matchCount: number | null = null;
  @Input() loading = false;
  @Input() error: string | null = null;

  @Output() readonly criteriaChange = new EventEmitter<AdminUserCriteria>();
  @Output() readonly presetChange = new EventEmitter<BulkUserPreset>();

  readonly presets = BULK_USER_PRESETS;

  /** `428 users match`, `1 user matches`; the step's one polite live region. */
  get statusText(): string {
    if (this.error) return '';
    if (this.matchCount == null) return this.loading ? 'Counting the matching accounts…' : '';
    const n = this.matchCount;
    if (n === 0) return 'No users match these criteria.';
    return n === 1 ? '1 user matches' : `${n.toLocaleString('en-US')} users match`;
  }

  get sampleNote(): string {
    const n = this.matchCount ?? 0;
    const shown = this.matches.length;
    const first = n > shown ? `The first ${shown}, oldest registration first. ` : '';
    return `${first}The exact list is fixed in step 3.`;
  }

  choosePreset(preset: BulkUserPreset): void {
    this.presetChange.emit(preset);
  }

  onCriteriaChange(criteria: AdminUserCriteria): void {
    this.criteriaChange.emit(criteria);
  }
}
