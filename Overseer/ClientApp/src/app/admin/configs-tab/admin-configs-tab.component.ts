import { Component, OnInit, OnDestroy, AfterViewInit, EventEmitter, Output, inject, ViewChild, ElementRef, ChangeDetectionStrategy, ChangeDetectorRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Subscription } from 'rxjs';
import { AdminService, SystemAiConfigDto, SystemConfigDeletionCheckDto, SystemConfigBlockerDto } from '../../services/admin.service';
import { AiModelFormComponent, AiModelFormResult } from '../../shared/ai-model-form/ai-model-form.component';
import { ProviderBadgeComponent } from '../../shared/provider-badge/provider-badge.component';
import { ConfigAnalyticsComponent } from '../config-analytics/config-analytics.component';
import { ConfigFilterComponent } from '../config-filter/config-filter.component';
import {
  ConfigFilter, createEmptyFilter, isFilterActive, matchesFilter, ROLE_OPTIONS
} from '../config-filter/config-filter.model';
import { AdminPageStore } from '../admin-page.store';
import { AdminRateLimitsDialogComponent } from '../admin-dialogs/admin-rate-limits-dialog.component';
import { refreshAnchorPositioning } from '../../utils/polyfills.util';
import { ModelAvailabilityNoticeComponent } from '../../shared/model-availability/model-availability-notice.component';
import { ModelResolutionDialogComponent } from '../../shared/model-resolution-dialog/model-resolution-dialog.component';
import {
  ModelResolutionResult, availabilityChip, availabilitySentence, needsAttention
} from '../../shared/model-availability/model-availability';
import { modelRoleBadges } from '../../utils/model-badge-format.util';
import { AdminApiKeysComponent, DEFAULT_KEY_PROVIDERS } from '../admin-api-keys/admin-api-keys.component';
import { ModelCardSettingsComponent } from '../../shared/model-card/model-card-settings.component';
import { ModelCardFactsComponent } from '../../shared/model-card/model-card-facts.component';
import { DragAutoscrollDirective } from '../../shared/drag-autoscroll/drag-autoscroll.directive';

/** The badge beside a configuration's provider badge that says how it relates to the model catalog. */
export interface ConfigAvailabilityBadge {
  text: 'Removed' | 'Not in catalog' | 'Custom model';
  cssClass: 'badge-warning' | 'badge-info' | 'badge-neutral';
  icon: 'warning' | 'info' | 'custom';
  tip: string;
}

/** The Admin page's System Configs tab: the system AI configurations, their filter, order and dialogs. */
@Component({
  selector: 'app-admin-configs-tab',
  imports: [
    CommonModule, AiModelFormComponent, ConfigAnalyticsComponent, ConfigFilterComponent, ProviderBadgeComponent, AdminRateLimitsDialogComponent,
    ModelAvailabilityNoticeComponent, ModelResolutionDialogComponent, AdminApiKeysComponent,
    ModelCardSettingsComponent, ModelCardFactsComponent, DragAutoscrollDirective
  ],
  templateUrl: './admin-configs-tab.component.html',
  changeDetection: ChangeDetectionStrategy.Eager,   // matches every other component here
  styleUrl: './admin-configs-tab.component.scss'
})
export class AdminConfigsTabComponent implements OnInit, OnDestroy, AfterViewInit {
  private adminService = inject(AdminService);
  protected store = inject(AdminPageStore);
  private cdr = inject(ChangeDetectorRef);
  private host = inject<ElementRef<HTMLElement>>(ElementRef);

  /** A benchmark run the delete-config dialog asks the Benchmark tab to open. */
  @Output() openBenchmarkRun = new EventEmitter<number>();

  @ViewChild('configDialog') configDialog!: ElementRef<HTMLDialogElement>;
  @ViewChild('analyticsDialog') analyticsDialog!: ElementRef<HTMLDialogElement>;
  @ViewChild('deleteConfigDialog') deleteConfigDialog!: ElementRef<HTMLDialogElement>;
  @ViewChild(ModelResolutionDialogComponent) resolutionDialog?: ModelResolutionDialogComponent;
  @ViewChild('configsHeading') configsHeading?: ElementRef<HTMLElement>;
  @ViewChild('apiKeysDialog', { static: true }) apiKeysDialog?: ElementRef<HTMLDialogElement>;
  @ViewChild('apiKeysHeading', { static: true }) apiKeysHeading?: ElementRef<HTMLElement>;
  @ViewChild('apiKeysButton', { static: true }) apiKeysButton?: ElementRef<HTMLButtonElement>;

  private configsChangedSub?: Subscription;
  private resolveRequestSub?: Subscription;
  private apiKeysDialogSub?: Subscription;

  /** Set once the view exists, so the API Keys dialog can be opened. */
  private viewReady = false;

  /** The configuration a resolution saved; focus moves on from its row once the dialog has closed. */
  private focusAfterResolutionId: number | null = null;

  get configs(): SystemAiConfigDto[] {
    return this.store.configs;
  }

  set configs(v: SystemAiConfigDto[]) {
    this.store.setConfigs(v);
  }

  configFilter: ConfigFilter = createEmptyFilter();
  visibleConfigs: SystemAiConfigDto[] = [];

  get isConfigFilterActive(): boolean {
    return isFilterActive(this.configFilter);
  }

  applyConfigFilters(): void {
    this.visibleConfigs = this.configs.filter(c => matchesFilter(c, this.configFilter));
  }

  onConfigFilterChange(next: ConfigFilter): void {
    this.configFilter = next;
    this.applyConfigFilters();
    this.persistConfigFilter();
  }

  clearConfigFilters(): void {
    this.onConfigFilterChange(createEmptyFilter());
  }

  private static readonly FILTER_STORAGE_KEY = 'overseer_admin_config_filters';

  persistConfigFilter(): void {
    try {
      localStorage.setItem(
        AdminConfigsTabComponent.FILTER_STORAGE_KEY, JSON.stringify(this.configFilter));
    } catch {
      // Storage can throw in private-browsing modes. A filter that fails to persist
      // is not worth surfacing to the user.
    }
  }

  restoreConfigFilter(): void {
    let parsed: unknown;
    try {
      const stored = localStorage.getItem(AdminConfigsTabComponent.FILTER_STORAGE_KEY);
      if (!stored) { return; }
      parsed = JSON.parse(stored);
    } catch {
      return;                                   // leaves the field's createEmptyFilter()
    }

    // Whitelist every field. Anything unrecognised is dropped rather than trusted:
    // an unknown role bit or provider produces a chip with no label and a list the
    // user cannot get back from.
    const raw = parsed as Partial<ConfigFilter> | null;
    if (!raw || typeof raw !== 'object') { return; }

    const knownRoles = ROLE_OPTIONS.map(o => o.bit);
    const next = createEmptyFilter();

    if (Array.isArray(raw.roles)) {
      next.roles = raw.roles.filter(
        (r): r is number => typeof r === 'number' && knownRoles.includes(r));
    }
    if (Array.isArray(raw.providers)) {
      next.providers = raw.providers.filter(
        (p): p is string => typeof p === 'string' && this.adminProviders.includes(p));
    }
    next.roleMatchMode = raw.roleMatchMode === 'all' ? 'all' : 'any';

    this.configFilter = next;
  }

  analyticsConfigId: number = 0;
  analyticsConfigName: string = '';

  editingConfig: Partial<SystemAiConfigDto> | null = null;
  isNewConfig = false;
  adminProviders = ['Anthropic', 'Google', 'OpenAI'];
  savingConfig = false;

  ngOnInit() {
    this.restoreConfigFilter();
    this.store.loadDefaultApiKeys();
    this.applyConfigFilters();
    this.configsChangedSub = this.store.configsChanged$.subscribe(() => this.applyConfigFilters());
    this.apiKeysDialogSub = this.store.apiKeysDialogChanged$.subscribe(() => this.syncApiKeysDialog(true));
  }

  ngAfterViewInit(): void {
    setTimeout(() => refreshAnchorPositioning(), 0);
    this.openPendingResolve();
    this.resolveRequestSub = this.store.resolveConfigRequested$.subscribe(() => this.openPendingResolve());
    this.viewReady = true;
    // A direct load of /admin/configs/api-keys asked for the dialog before this view existed; the
    // keys are already loading from ngOnInit.
    this.syncApiKeysDialog(false);
  }

  ngOnDestroy() {
    this.configsChangedSub?.unsubscribe();
    this.resolveRequestSub?.unsubscribe();
    this.apiKeysDialogSub?.unsubscribe();
  }

  // --- API Keys dialog ---

  /** True while the address is System Configs → API Keys. */
  get apiKeysDialogOpen(): boolean {
    return this.store.apiKeysDialogOpen;
  }

  /** Providers whose default key is missing or not verified; 0 until the keys have loaded. */
  get apiKeysAttentionCount(): number {
    const keys = this.store.defaultApiKeys;
    if (keys.length === 0) {
      return 0;
    }
    return DEFAULT_KEY_PROVIDERS.filter(provider => {
      const status = keys.find(k => k.provider.toLowerCase() === provider.toLowerCase());
      return !status?.hasKey || status.verification?.status === 'NotVerified';
    }).length;
  }

  /** API Keys: the dialog's address, as a new history entry. */
  openApiKeysDialog(): void {
    this.store.goTo('configs', 'api-keys', 'push');
  }

  /** Done and the close button: back to System Configs, replacing the dialog's history entry. */
  closeApiKeysDialog(): void {
    this.store.goTo('configs', null, 'replace');
  }

  /**
   * Every close, Escape and an address without `/api-keys` included: the address follows, and focus
   * returns to API Keys unless another tab is being selected.
   */
  onApiKeysDialogClose(): void {
    if (this.store.apiKeysDialogOpen) {
      this.closeApiKeysDialog();
    }
    if (this.store.activeTab === 'configs') {
      this.apiKeysButton?.nativeElement.focus();
    }
  }

  /** Opens or closes the dialog as the page store says; an opening reloads the keys when `load` is set. */
  private syncApiKeysDialog(load: boolean): void {
    const dialog = this.apiKeysDialog?.nativeElement;
    if (!dialog || !this.viewReady) {
      return;
    }
    if (this.store.apiKeysDialogOpen && !dialog.open) {
      if (load && !this.store.defaultApiKeysLoading) {
        this.store.loadDefaultApiKeys();
      }
      dialog.showModal();
      this.apiKeysHeading?.nativeElement.focus();
      setTimeout(() => refreshAnchorPositioning(), 0);
    } else if (!this.store.apiKeysDialogOpen && dialog.open) {
      dialog.close();
    }
  }

  configName(config: SystemAiConfigDto): string {
    return config.displayName || config.modelId;
  }

  /** The model the availability sentence names: the retired catalog entry's name, else the model ID. */
  availabilityModelName(config: SystemAiConfigDto): string {
    return config.modelAvailability?.catalogDisplayName || config.modelId;
  }

  isFlagged(config: SystemAiConfigDto): boolean {
    return needsAttention(config.modelAvailability);
  }

  resolveLabel(config: SystemAiConfigDto): string {
    return `Resolve "${this.configName(config)}"`;
  }

  /** The configurations whose model needs attention, disabled ones included, retired ones first. */
  get attentionConfigs(): SystemAiConfigDto[] {
    const flagged = this.configs.filter(c => this.isFlagged(c));
    return [
      ...flagged.filter(c => c.modelAvailability?.status === 'retired'),
      ...flagged.filter(c => c.modelAvailability?.status !== 'retired')
    ];
  }

  /** Removed or Not in catalog for a model that needs attention, Custom model for a custom-mode row, else null. */
  availabilityBadge(config: SystemAiConfigDto): ConfigAvailabilityBadge | null {
    const chip = availabilityChip(config.modelAvailability);
    if (chip) {
      return {
        text: chip.text,
        cssClass: chip.tone === 'warning' ? 'badge-warning' : 'badge-info',
        icon: chip.tone,
        tip: availabilitySentence(config.modelAvailability, this.availabilityModelName(config), config.modelId)
      };
    }
    if (config.modelCatalogMode === 'custom' || config.modelAvailability?.status === 'custom') {
      return {
        text: 'Custom model',
        cssClass: 'badge-neutral',
        icon: 'custom',
        tip: 'Not checked against the model catalog; it uses its own limits and prices.'
      };
    }
    return null;
  }

  // --- Model resolution ---
  openResolve(config: SystemAiConfigDto): void {
    const availability = config.modelAvailability;
    if (!availability || !needsAttention(availability)) {
      return;
    }
    this.focusAfterResolutionId = null;
    this.resolutionDialog?.open({
      id: config.id,
      provider: config.provider,
      modelId: config.modelId,
      displayName: this.configName(config),
      availability
    });
  }

  /** Opens the dialog for the store's pending configuration and clears it; an unknown or settled one is dropped. */
  private openPendingResolve(): void {
    const id = this.store.pendingResolveConfigId;
    if (id == null) {
      return;
    }
    this.store.pendingResolveConfigId = null;
    const config = this.configs.find(c => c.id === id);
    if (config) {
      this.openResolve(config);
    }
  }

  onConfigResolved(result: ModelResolutionResult): void {
    const updated = result.model as SystemAiConfigDto | null;
    if (!updated) {
      return;
    }
    const previous = this.configs.find(c => c.id === updated.id);
    const name = this.configName(previous ?? updated);
    const custom = updated.modelCatalogMode === 'custom' || updated.modelAvailability?.status === 'custom';
    this.store.replaceConfig(updated);
    this.store.showToast(custom ? `'${name}' is now a custom model.` : `'${name}' now uses ${updated.modelId}.`, 'success');
    this.focusAfterResolutionId = updated.id;
  }

  /** Delete… in the resolution dialog hands over to the row's own delete flow. */
  onResolutionDeleteRequested(id: number): void {
    const config = this.configs.find(c => c.id === id);
    if (config) {
      this.requestDeleteConfig(config);
    }
  }

  /**
   * After a resolution, focus goes to the next flagged row's Resolve…, else to the resolved row's
   * title, else to the section heading.
   */
  onResolutionClosed(): void {
    const id = this.focusAfterResolutionId;
    if (id == null) {
      return;
    }
    this.focusAfterResolutionId = null;
    this.cdr.detectChanges();

    const root = this.host.nativeElement;
    const flagged = this.visibleConfigs.filter(c => this.isFlagged(c));
    const position = this.visibleConfigs.findIndex(c => c.id === id);
    const next = flagged.find(c => this.visibleConfigs.indexOf(c) > position) ?? flagged[0];
    const target = (next ? root.querySelector<HTMLElement>(`#config-resolve-${next.id}`) : null)
      ?? root.querySelector<HTMLElement>(`#config-title-${id}`)
      ?? this.configsHeading?.nativeElement;
    target?.focus();
  }

  openAnalytics(config: SystemAiConfigDto) {
    this.analyticsConfigId = config.id;
    this.analyticsConfigName = config.displayName || config.modelId;
    this.analyticsDialog.nativeElement.showModal();
  }

  closeAnalytics() {
    this.analyticsDialog.nativeElement.close();
    this.analyticsConfigId = 0;
  }

  /** The configuration list's key badge. */
  configKeyBadge(config: SystemAiConfigDto): { label: string; cssClass: string } {
    if (config.useDefaultApiKey) {
      return config.hasApiKey
        ? { label: 'Default Key', cssClass: 'badge-success' }
        : { label: 'Default Key Missing', cssClass: 'badge-warning' };
    }
    return config.hasApiKey
      ? { label: 'Key Saved', cssClass: 'badge-success' }
      : { label: 'No Key', cssClass: 'badge-warning' };
  }

  // --- Configs ---
  openCreateConfig() {
    this.isNewConfig = true;
    this.editingConfig = {
      provider: this.adminProviders[0],
      isEnabled: true,
      isSystemWide: false,
      orderIndex: 0,
      modelRole: 3,
      pricingMode: 'default',
      inputPricePerMillion: null,
      outputPricePerMillion: null,
      cachedInputPricePerMillion: null
    };
    this.configDialog.nativeElement.showModal();
  }

  openEditConfig(config: SystemAiConfigDto) {
    this.isNewConfig = false;
    this.editingConfig = {
      ...config,
      pricingMode: config.pricingMode,
      inputPricePerMillion: config.inputPricePerMillion,
      outputPricePerMillion: config.outputPricePerMillion,
      cachedInputPricePerMillion: config.cachedInputPricePerMillion
    };
    this.configDialog.nativeElement.showModal();
  }

  closeConfig() {
    this.configDialog.nativeElement.close();
    this.editingConfig = null;
    this.configSaveError = null;
  }

  /**
   * The server's own refusal text from the last save. The endpoint and posture rules name the
   * setting or the field they refused, so it is shown in the dialog beside the fields it is
   * about rather than lost in a browser alert.
   */
  configSaveError: string | null = null;

  onConfigSave(formData: AiModelFormResult) {
    this.savingConfig = true;
    this.configSaveError = null;

    // Merge form data with existing config (for things like orderIndex)
    const payload = {
      ...(this.editingConfig || {}),
      ...formData,
      useDefaultApiKey: formData.useDefaultApiKey ?? false,
      pricingMode: formData.pricingMode,
      inputPricePerMillion: formData.inputPricePerMillion,
      outputPricePerMillion: formData.outputPricePerMillion,
      cachedInputPricePerMillion: formData.cachedInputPricePerMillion
    };

    if (this.isNewConfig) {
      this.adminService.createSystemConfig(payload).subscribe({
        next: (c) => {
          this.store.loadConfigs();
          this.savingConfig = false;
          this.closeConfig();
        },
        error: (err) => {
          this.savingConfig = false;
          this.configSaveError = this.describeConfigSaveError(err);
        }
      });
    } else {
      this.adminService.updateSystemConfig(payload.id!, payload).subscribe({
        next: (c) => {
          this.store.loadConfigs();
          this.savingConfig = false;
          this.closeConfig();
        },
        error: (err) => {
          this.savingConfig = false;
          this.configSaveError = this.describeConfigSaveError(err);
        }
      });
    }
  }

  /** `BadRequest(string)` arrives as a plain string body, so `err.error` is the message itself. */
  private describeConfigSaveError(err: any): string {
    if (typeof err?.error === 'string' && err.error.trim()) {
      return err.error;
    }
    return err?.error?.message || 'The configuration could not be saved.';
  }

  // --- Delete System Config Dialog ---
  // A dedicated dialog, not the generic confirm: a delete can be blocked by an active
  // benchmark, and the generic confirm dialog closes before the action completes, so it
  // cannot show a blocked state or switch state in place on a race.
  deleteConfigTarget: SystemAiConfigDto | null = null;
  deleteConfigCheck: SystemConfigDeletionCheckDto | null = null;
  deleteConfigState: 'blocked' | 'deletable' | 'checkFailed' = 'deletable';
  deleteConfigCheckError: string | null = null;
  deleteConfigActionError: string | null = null;
  /** True only while the blocked reason changed without a focus move (a 409 race on confirm). */
  deleteConfigReasonIsLive = false;
  /** The config id currently being (re)checked, so only that row's button shows busy. */
  deleteConfigCheckingId: number | null = null;
  deleteConfigDeleting = false;

  /** The name shown in the dialog's heading and messages. */
  get deleteConfigName(): string {
    return this.deleteConfigCheck?.displayName
      || this.deleteConfigTarget?.displayName
      || this.deleteConfigTarget?.modelId
      || '';
  }

  /** "as the assessor and the claim verifier" — the roles a blocker plays, joined in words. */
  formatBlockerRoles(roles: string[]): string {
    const named = roles.map(r => 'the ' + r);
    if (named.length === 0) return '';
    if (named.length === 1) return 'as ' + named[0];
    return 'as ' + named.slice(0, -1).join(', ') + ' and ' + named[named.length - 1];
  }

  /** `BadRequest`/`Conflict` bodies arrive as either a plain string or `{ error: string }`. */
  private describeDeleteConfigError(err: any, fallback: string): string {
    const raw = err?.error?.error ?? err?.error;
    return typeof raw === 'string' && raw.trim() ? raw : fallback;
  }

  private applyDeleteConfigCheck(check: SystemConfigDeletionCheckDto): void {
    this.deleteConfigCheck = check;
    this.deleteConfigState = check.canDelete ? 'deletable' : 'blocked';
    this.deleteConfigCheckError = null;
    this.deleteConfigActionError = null;
  }

  /** Runs the deletion check, then opens the dialog with the final content already in place. */
  requestDeleteConfig(config: SystemAiConfigDto): void {
    if (this.deleteConfigCheckingId != null) {
      return;
    }
    this.deleteConfigTarget = config;
    this.deleteConfigCheckingId = config.id;
    this.deleteConfigReasonIsLive = false;
    this.adminService.getSystemConfigDeletionCheck(config.id).subscribe({
      next: (check) => {
        this.deleteConfigCheckingId = null;
        this.applyDeleteConfigCheck(check);
        this.deleteConfigDialog.nativeElement.showModal();
      },
      error: (err) => {
        this.deleteConfigCheckingId = null;
        this.deleteConfigCheck = null;
        this.deleteConfigState = 'checkFailed';
        this.deleteConfigCheckError = this.describeDeleteConfigError(err, "Could not check whether this configuration can be deleted.");
        this.deleteConfigDialog.nativeElement.showModal();
      }
    });
  }

  /** Re-runs the deletion check and switches the open dialog's state in place. */
  recheckDeleteConfig(): void {
    const target = this.deleteConfigTarget;
    if (!target || this.deleteConfigCheckingId != null) {
      return;
    }
    this.deleteConfigCheckingId = target.id;
    this.deleteConfigReasonIsLive = false;
    this.adminService.getSystemConfigDeletionCheck(target.id).subscribe({
      next: (check) => {
        this.deleteConfigCheckingId = null;
        this.applyDeleteConfigCheck(check);
      },
      error: (err) => {
        this.deleteConfigCheckingId = null;
        this.deleteConfigCheck = null;
        this.deleteConfigState = 'checkFailed';
        this.deleteConfigCheckError = this.describeDeleteConfigError(err, "Could not check whether this configuration can be deleted.");
      }
    });
  }

  /** The blocked-state Delete button is aria-disabled; the check above already explains why. */
  refuseBlockedDelete(): void {
    // Intentionally inert: the reason is the blocked-state message, not a new one here.
  }

  closeDeleteConfigDialog(): void {
    this.deleteConfigDialog.nativeElement.close();
    this.deleteConfigTarget = null;
    this.deleteConfigCheck = null;
    this.deleteConfigCheckError = null;
    this.deleteConfigActionError = null;
    this.deleteConfigReasonIsLive = false;
    this.deleteConfigDeleting = false;
  }

  confirmDeleteConfig(): void {
    const target = this.deleteConfigTarget;
    if (!target || this.deleteConfigState !== 'deletable' || this.deleteConfigDeleting) {
      return;
    }
    this.deleteConfigDeleting = true;
    this.deleteConfigActionError = null;
    this.adminService.deleteSystemConfig(target.id).subscribe({
      next: () => {
        this.store.setConfigs(this.store.configs.filter(c => c.id !== target.id));
        const name = target.displayName || target.modelId;
        this.closeDeleteConfigDialog();
        this.store.showToast(`Deleted '${name}'. Benchmark history and usage logs are kept.`, 'success');
      },
      error: (err) => {
        this.deleteConfigDeleting = false;
        if (err?.status === 409 && Array.isArray(err?.error?.blockers)) {
          if (this.deleteConfigCheck) {
            this.deleteConfigCheck = { ...this.deleteConfigCheck, canDelete: false, blockers: err.error.blockers as SystemConfigBlockerDto[] };
          }
          this.deleteConfigState = 'blocked';
          this.deleteConfigReasonIsLive = true;
        } else {
          this.deleteConfigActionError = this.describeDeleteConfigError(err, 'The configuration could not be deleted.');
        }
      }
    });
  }

  /** Closes the dialog and emits the run's id through `openBenchmarkRun`, for the Benchmark tab's run detail dialog. */
  openBlockerRun(blocker: SystemConfigBlockerDto): void {
    if (blocker.runId == null) {
      return;
    }
    const runId = blocker.runId;
    this.closeDeleteConfigDialog();
    this.openBenchmarkRun.emit(runId);
  }

  readonly modelRoleBadges = modelRoleBadges;

  // --- Drag and Drop for System Configs ---
  onConfigDragStart(event: DragEvent, index: number) {
    if (event.dataTransfer) {
      event.dataTransfer.setData('text/plain', index.toString());
      event.dataTransfer.effectAllowed = 'move';
      const target = event.target as HTMLElement;
      setTimeout(() => target.classList.add('dragging'), 0);
    }
  }

  onConfigDragEnd(event: DragEvent) {
    const target = event.target as HTMLElement;
    target.classList.remove('dragging');
    const items = this.host.nativeElement.querySelectorAll('.gh-model-card-item');
    items.forEach(item => item.classList.remove('drag-over-top', 'drag-over-bottom'));
  }

  onConfigDragOver(event: DragEvent) {
    event.preventDefault();
    if (event.dataTransfer) {
      event.dataTransfer.dropEffect = 'move';
    }
    const targetItem = (event.target as HTMLElement).closest('.gh-model-card-item');
    if (targetItem) {
      const rect = targetItem.getBoundingClientRect();
      const midY = rect.top + rect.height / 2;
      targetItem.classList.remove('drag-over-top', 'drag-over-bottom');
      if (event.clientY < midY) {
        targetItem.classList.add('drag-over-top');
      } else {
        targetItem.classList.add('drag-over-bottom');
      }
    }
  }

  onConfigDragLeave(event: DragEvent) {
    const targetItem = (event.target as HTMLElement).closest('.gh-model-card-item');
    if (targetItem) {
      targetItem.classList.remove('drag-over-top', 'drag-over-bottom');
    }
  }

  onConfigDrop(event: DragEvent, dropIndex: number): void {
    event.preventDefault();
    const targetItem = (event.target as HTMLElement).closest('.gh-model-card-item');
    targetItem?.classList.remove('drag-over-top', 'drag-over-bottom');
    if (!event.dataTransfer) { return; }

    const raw = event.dataTransfer.getData('text/plain');
    if (raw === '') { return; }
    const dragIndex = parseInt(raw, 10);
    if (Number.isNaN(dragIndex) || dragIndex === dropIndex) { return; }

    const dragged = this.visibleConfigs[dragIndex];
    const target  = this.visibleConfigs[dropIndex];
    if (!dragged || !target) { return; }

    // Drop below the target's midpoint means "after"; above means "before".
    let after = false;
    if (targetItem) {
      const rect = targetItem.getBoundingClientRect();
      after = event.clientY >= rect.top + rect.height / 2;
    }

    const from = this.configs.indexOf(dragged);
    if (from === -1) { return; }
    this.configs.splice(from, 1);

    // Recompute the target's position AFTER the removal, or the index is off by one
    // whenever the dragged config sat before the target.
    const targetPos = this.configs.indexOf(target);
    if (targetPos === -1) { this.configs.splice(from, 0, dragged); return; }
    this.configs.splice(after ? targetPos + 1 : targetPos, 0, dragged);

    this.applyConfigFilters();
    this.saveConfigOrder();
  }

  saveConfigOrder() {
    this.savingConfig = true;
    const orderedIds = this.configs.map(c => c.id);
    this.adminService.reorderSystemConfigs(orderedIds).subscribe({
      next: () => {
        this.savingConfig = false;
      },
      error: (err) => {
        console.error("Failed to save config order", err);
        this.savingConfig = false;
      }
    });
  }
}
