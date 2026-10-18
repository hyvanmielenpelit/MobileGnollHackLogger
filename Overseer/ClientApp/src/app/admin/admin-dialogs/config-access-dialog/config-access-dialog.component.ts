import {
  ChangeDetectionStrategy,
  ChangeDetectorRef,
  Component,
  ElementRef,
  Injector,
  OnDestroy,
  OnInit,
  ViewChild,
  afterNextRender,
  inject
} from '@angular/core';
import { NgClass } from '@angular/common';
import { Observable, Subscription } from 'rxjs';

import {
  AdminService,
  GroupSystemAiConfigDto,
  SystemAiConfigDto,
  UserSystemAiConfigDto
} from '../../../services/admin.service';
import { CardListState } from '../../../shared/data-table/card-list-state';
import { TableState, customFilter } from '../../../shared/data-table/table-state';
import { ModelOptionBadgesComponent } from '../../../shared/model-picker/model-option-badges.component';
import { formatServiceTier, modelRoleBadges } from '../../../utils/model-badge-format.util';
import { ensureOverlayPolyfills, refreshAnchorPositioning } from '../../../utils/polyfills.util';
import { AdminPageStore } from '../../admin-page.store';
import { AdminConfigOverrideDialogComponent } from '../admin-config-override-dialog.component';
import { AdminRateLimitsDialogComponent } from '../admin-rate-limits-dialog.component';

/** The user or group whose access the dialog edits. */
export interface ConfigAccessSubject {
  kind: 'user' | 'group';
  id: string | number;
  name: string;
}

/** A user's or a group's assignment of one system configuration. */
export type ConfigAccessAssignment = UserSystemAiConfigDto | GroupSystemAiConfigDto;

/** Above this many configurations the dialog offers a search field. */
export const CONFIG_ACCESS_SEARCH_THRESHOLD = 8;

/** The assignment limits, in the order the Limited tooltip lists them. */
const LIMIT_LABELS: ReadonlyArray<readonly [field: string, label: string]> = [
  ['maxDailyChatRequests', 'Daily chat requests'],
  ['maxMonthlyChatRequests', 'Monthly chat requests'],
  ['maxTotalChatRequests', 'Total chat requests'],
  ['maxDailyChatTokens', 'Daily chat tokens'],
  ['maxMonthlyChatTokens', 'Monthly chat tokens'],
  ['maxTotalChatTokens', 'Total chat tokens'],
  ['maxDailyTitleRequests', 'Daily title requests'],
  ['maxMonthlyTitleRequests', 'Monthly title requests'],
  ['maxTotalTitleRequests', 'Total title requests'],
  ['maxDailyTitleTokens', 'Daily title tokens'],
  ['maxMonthlyTitleTokens', 'Monthly title tokens'],
  ['maxTotalTitleTokens', 'Total title tokens'],
  ['maxResultLength', 'Result length'],
  ['maxCallsPerSession', 'Calls per session'],
  ['maxToolIterations', 'Tool iterations']
];

/** A new assignment: enabled, the configuration's own roles, no limits. */
function newAssignmentPayload(config: SystemAiConfigDto): Record<string, unknown> {
  const payload: Record<string, unknown> = {
    systemAiApiConfigurationId: config.id,
    isEnabled: true,
    modelRole: config.modelRole
  };
  for (const [field] of LIMIT_LABELS) {
    payload[field] = null;
  }
  return payload;
}

/**
 * *AI configuration access for {user}* (or *for group {group}*): every system AI configuration as a
 * card with a switch that assigns it to the user or group at once, its badges, and for an assignment
 * its state (*Off for this user*, *Limited*) and the Rate limits and Overrides dialogs. A search field
 * appears above `CONFIG_ACCESS_SEARCH_THRESHOLD` configurations; every card is shown.
 */
@Component({
  selector: 'app-config-access-dialog',
  standalone: true,
  imports: [NgClass, ModelOptionBadgesComponent, AdminRateLimitsDialogComponent, AdminConfigOverrideDialogComponent],
  templateUrl: './config-access-dialog.component.html',
  styleUrl: './config-access-dialog.component.scss',
  changeDetection: ChangeDetectionStrategy.Eager   // matches every other component here
})
export class ConfigAccessDialogComponent implements OnInit, OnDestroy {
  private readonly adminService = inject(AdminService);
  private readonly store = inject(AdminPageStore);
  private readonly cdr = inject(ChangeDetectorRef);
  private readonly injector = inject(Injector);

  @ViewChild('dialog', { static: true }) dialog!: ElementRef<HTMLDialogElement>;
  @ViewChild('title', { static: true }) title!: ElementRef<HTMLElement>;
  @ViewChild(AdminRateLimitsDialogComponent, { static: true }) rateLimitsDialog!: AdminRateLimitsDialogComponent;
  @ViewChild(AdminConfigOverrideDialogComponent, { static: true }) overrideDialog!: AdminConfigOverrideDialogComponent;

  readonly searchThreshold = CONFIG_ACCESS_SEARCH_THRESHOLD;
  readonly formatServiceTier = formatServiceTier;
  readonly modelRoleBadges = modelRoleBadges;

  subject: ConfigAccessSubject | null = null;
  assignments: ConfigAccessAssignment[] = [];
  loading = false;
  error: string | null = null;
  /** The configurations whose toggle is in flight. */
  processing: number[] = [];

  private readonly table = new TableState<SystemAiConfigDto>('order', 'asc').registerAccessors({}, {
    search: customFilter((config, value) => {
      const needle = value.toLowerCase();
      return [config.displayName, config.modelId, config.provider]
        .some(text => (text ?? '').toLowerCase().includes(needle));
    })
  });

  /** The search; no sort, facets or batches, so every matching card is shown in the store's order. */
  readonly list = new CardListState<SystemAiConfigDto>(this.table, {
    idPrefix: 'cad',
    batch: Number.MAX_SAFE_INTEGER,
    sorts: [],
    defaultSort: 'order',
    storageKey: 'overseer.admin.configAccess.view',
    facets: [],
    onChange: () => this.cdr.markForCheck()
  });

  private loadSub: Subscription | null = null;
  /** Bumped on every open, so a response for an earlier subject is ignored. */
  private generation = 0;

  get configs(): SystemAiConfigDto[] {
    return this.store.configs;
  }

  get shownConfigs(): SystemAiConfigDto[] {
    return this.list.matching(this.configs);
  }

  get heading(): string {
    if (!this.subject) return 'AI configuration access';
    return this.subject.kind === 'group'
      ? `AI configuration access for group ${this.subject.name}`
      : `AI configuration access for ${this.subject.name}`;
  }

  get subtitle(): string {
    const who = !this.subject ? 'this account'
      : this.subject.kind === 'group' ? `members of group ${this.subject.name}` : this.subject.name;
    return `Choose which system AI configurations ${who} can use, and set limits and overrides per configuration. System-wide configurations are available to everyone.`;
  }

  /** `3 of 5 configurations assigned`, with `· 2 shown` while the search hides some. */
  get status(): string {
    if (this.loading) return 'Loading the assignments…';
    const total = this.configs.length;
    if (this.error || total === 0) return '';
    const assigned = this.configs.filter(c => this.isAssigned(c.id)).length;
    const text = `${assigned} of ${total} ${total === 1 ? 'configuration' : 'configurations'} assigned`;
    const shown = this.shownConfigs.length;
    return shown < total ? `${text} · ${shown} shown` : text;
  }

  /** `Off for this user` / `Off for this group`. */
  get offLabel(): string {
    return this.subject?.kind === 'group' ? 'Off for this group' : 'Off for this user';
  }

  ngOnInit(): void {
    ensureOverlayPolyfills();
  }

  ngOnDestroy(): void {
    this.loadSub?.unsubscribe();
    this.list.dispose();
    if (this.dialog?.nativeElement.open) this.dialog.nativeElement.close();
  }

  /** Opens the dialog for the subject, focuses its heading and loads the subject's assignments. */
  open(subject: ConfigAccessSubject): void {
    const generation = ++this.generation;
    this.subject = subject;
    this.assignments = [];
    this.processing = [];
    this.error = null;
    this.loading = true;
    this.list.reset();

    const dialog = this.dialog.nativeElement;
    if (!dialog.open) dialog.showModal();
    this.title.nativeElement.focus();

    this.loadSub?.unsubscribe();
    const request: Observable<ConfigAccessAssignment[]> = subject.kind === 'group'
      ? this.adminService.getGroupSystemConfigs(Number(subject.id))
      : this.adminService.getUserSystemConfigs(String(subject.id));
    this.loadSub = request.subscribe({
      next: assignments => {
        if (generation !== this.generation) return;
        this.assignments = assignments;
        this.loading = false;
        this.cdr.markForCheck();
        afterNextRender(() => refreshAnchorPositioning(), { injector: this.injector });
      },
      error: () => {
        if (generation !== this.generation) return;
        this.loading = false;
        this.error = 'The assignments could not be loaded. Close the dialog and try again.';
        this.cdr.markForCheck();
      }
    });
  }

  close(): void {
    this.dialog.nativeElement.close();
  }

  /** The dialog's own close (Done, Escape); a nested dialog's close never reaches here. */
  onClosed(event: Event): void {
    if (event.target !== this.dialog.nativeElement) return;
    this.generation++;
    this.loadSub?.unsubscribe();
    this.subject = null;
    this.assignments = [];
    this.processing = [];
    this.loading = false;
    this.error = null;
    this.list.reset();
  }

  // --- Search ---

  onSearchInput(event: Event): void {
    this.list.setSearchInput((event.target as HTMLInputElement).value);
  }

  /** Escape with text clears the search at once and keeps the dialog open; in an empty field it closes the dialog. */
  onSearchKeydown(event: KeyboardEvent): void {
    if (this.list.clearSearchOnEscape(event)) this.cdr.markForCheck();
  }

  // --- A card ---

  assignmentOf(configId: number): ConfigAccessAssignment | undefined {
    return this.assignments.find(a => a.systemAiApiConfigurationId === configId);
  }

  isAssigned(configId: number): boolean {
    return this.assignments.some(a => a.systemAiApiConfigurationId === configId);
  }

  isProcessing(configId: number): boolean {
    return this.processing.includes(configId);
  }

  /** `Daily chat requests: 100, Total title tokens: 5,000`; empty when no limit is set. */
  limitsText(assignment: ConfigAccessAssignment): string {
    const values = assignment as unknown as Record<string, unknown>;
    return LIMIT_LABELS
      .filter(([field]) => values[field] != null && values[field] !== '')
      .map(([field, label]) => `${label}: ${Number(values[field]).toLocaleString('en-US')}`)
      .join(', ');
  }

  /** Assigns or unassigns the configuration at once; the switch reverts when the server refuses. */
  toggle(config: SystemAiConfigDto, event: Event): void {
    const input = event.target as HTMLInputElement;
    const checked = input.checked;
    const subject = this.subject;
    if (!subject || this.isProcessing(config.id)) {
      input.checked = !checked;
      return;
    }
    const assignment = this.assignmentOf(config.id);
    const generation = this.generation;
    const done = () => {
      this.processing = this.processing.filter(id => id !== config.id);
      this.cdr.markForCheck();
    };

    if (checked && !assignment) {
      this.processing = [...this.processing, config.id];
      const payload = newAssignmentPayload(config);
      const request: Observable<ConfigAccessAssignment> = subject.kind === 'group'
        ? this.adminService.createGroupSystemConfig(Number(subject.id), payload)
        : this.adminService.createUserSystemConfig(String(subject.id), payload);
      request.subscribe({
        next: created => {
          if (generation !== this.generation) return;
          this.assignments = [...this.assignments, created];
          done();
        },
        error: () => {
          if (generation !== this.generation) return;
          input.checked = false;
          done();
        }
      });
    } else if (!checked && assignment) {
      this.processing = [...this.processing, config.id];
      const request = subject.kind === 'group'
        ? this.adminService.deleteGroupSystemConfig(assignment.id)
        : this.adminService.deleteUserSystemConfig(assignment.id);
      request.subscribe({
        next: () => {
          if (generation !== this.generation) return;
          this.assignments = this.assignments.filter(a => a.id !== assignment.id);
          done();
        },
        error: () => {
          if (generation !== this.generation) return;
          input.checked = true;
          done();
        }
      });
    }
  }

  openRateLimits(assignment: ConfigAccessAssignment): void {
    if (this.subject) this.rateLimitsDialog.open(this.subject.kind, assignment);
  }

  openOverrides(assignment: ConfigAccessAssignment): void {
    if (this.subject) this.overrideDialog.open(this.subject.kind, assignment);
  }

  /** Replaces the saved assignment in the list. */
  onOverrideSaved(saved: ConfigAccessAssignment): void {
    this.assignments = this.assignments.map(a => a.id === saved.id ? saved : a);
  }
}
