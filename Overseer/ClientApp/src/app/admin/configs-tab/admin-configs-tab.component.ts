import { Component, OnInit, OnDestroy, AfterViewInit, EventEmitter, Output, inject, ViewChild, ElementRef, ChangeDetectionStrategy } from '@angular/core';
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

/** The Admin page's System Configs tab: the system AI configurations, their filter, order and dialogs. */
@Component({
  selector: 'app-admin-configs-tab',
  imports: [CommonModule, AiModelFormComponent, ConfigAnalyticsComponent, ConfigFilterComponent, ProviderBadgeComponent, AdminRateLimitsDialogComponent],
  templateUrl: './admin-configs-tab.component.html',
  changeDetection: ChangeDetectionStrategy.Eager,   // matches every other component here
  styleUrl: './admin-configs-tab.component.scss'
})
export class AdminConfigsTabComponent implements OnInit, OnDestroy, AfterViewInit {
  private adminService = inject(AdminService);
  protected store = inject(AdminPageStore);

  /** A benchmark run the delete-config dialog asks the Benchmark tab to open. */
  @Output() openBenchmarkRun = new EventEmitter<number>();

  @ViewChild('configDialog') configDialog!: ElementRef<HTMLDialogElement>;
  @ViewChild('analyticsDialog') analyticsDialog!: ElementRef<HTMLDialogElement>;
  @ViewChild('deleteConfigDialog') deleteConfigDialog!: ElementRef<HTMLDialogElement>;

  private configsChangedSub?: Subscription;

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
  }

  ngAfterViewInit(): void {
    setTimeout(() => refreshAnchorPositioning(), 0);
  }

  ngOnDestroy() {
    this.configsChangedSub?.unsubscribe();
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

  formatLevel(level: string | null | undefined): string {
    if (!level) return '';
    return level.charAt(0).toUpperCase() + level.slice(1);
  }

  formatReasoningMode(level: string | null | undefined): string {
    if (!level) return 'Default';
    return level.charAt(0).toUpperCase() + level.slice(1);
  }

  formatReasoningSummary(level: string | null | undefined, provider: string): string {
    if (!level) {
      if (provider === 'Anthropic') {
        return 'Default';
      }
      return 'None';
    }
    return level.charAt(0).toUpperCase() + level.slice(1);
  }

  formatServiceTier(tier: string | null | undefined): string {
    if (!tier) return 'None';
    if (tier.toLowerCase() === 'standard_only') return 'Standard Only';
    return tier.charAt(0).toUpperCase() + tier.slice(1);
  }

  formatPrice(config: SystemAiConfigDto): string {
    const input = config.effectiveInputPricePerMillion ?? (config.pricingMode === 'custom' ? config.inputPricePerMillion : null);
    const output = config.effectiveOutputPricePerMillion ?? (config.pricingMode === 'custom' ? config.outputPricePerMillion : null);
    const cached = config.effectiveCachedInputPricePerMillion ?? (config.pricingMode === 'custom' ? config.cachedInputPricePerMillion : null);

    if (input == null || output == null) {
      return '';
    }

    const fmt = (val: number) => this.formatRate(val);

    let result = `${fmt(input)} in / ${fmt(output)} out`;
    if (cached != null) {
      result += ` / ${fmt(cached)} cached`;
    }
    result += ' per 1M';
    return result;
  }

  private formatRate(val: number): string {
    const formatted = new Intl.NumberFormat('en-US', {
      minimumFractionDigits: 2,
      maximumFractionDigits: 6
    }).format(val);
    return `$${formatted}`;
  }

  /**
   * The model's long-prompt rate card, shown beside the base price. Empty for a flat-rate model — which
   * is every Anthropic model, every Gemini Flash model, and every custom price override.
   */
  formatLongContextPrice(config: SystemAiConfigDto): string {
    const threshold = config.effectiveLongContextThresholdTokens;
    const input = config.effectiveLongContextInputPricePerMillion;
    const output = config.effectiveLongContextOutputPricePerMillion;
    if (threshold == null || input == null || output == null) {
      return '';
    }
    const tokens = new Intl.NumberFormat('en-US').format(threshold);
    return `Prompts over ${tokens} tokens: ${this.formatRate(input)} in / ${this.formatRate(output)} out per 1M`;
  }

  /**
   * A quiet note about an announced future price change, or — once its date has passed — the advisory
   * that the base rates already carry it. The cost is correct either way; the advisory exists so the
   * catalog does not silently turn into a changelog of elapsed schedules.
   */
  formatPricingSchedule(config: SystemAiConfigDto): string {
    if (config.pricingScheduleElapsed) {
      return 'A scheduled price change is in effect — fold it into the base rates and re-verify.';
    }
    if (!config.pricingScheduledChangeFrom) {
      return '';
    }
    const input = config.pricingScheduledChangeInputPricePerMillion;
    const output = config.pricingScheduledChangeOutputPricePerMillion;
    const change = (input != null && output != null)
      ? `Price changes to ${this.formatRate(input)} in / ${this.formatRate(output)} out per 1M on ${config.pricingScheduledChangeFrom}.`
      : `Price changes on ${config.pricingScheduledChangeFrom}.`;
    return config.pricingScheduledChangeNote ? `${change} ${config.pricingScheduledChangeNote}` : change;
  }

  getPricingBadge(config: SystemAiConfigDto): 'Custom' | 'Catalog' {
    return (config.pricingSource === 'custom' || config.pricingMode === 'custom') ? 'Custom' : 'Catalog';
  }

  modelRoleBadges(role: number): Array<{ label: string; cssClass: string }> {
    const badges: Array<{ label: string; cssClass: string }> = [];
    if ((role & 1) === 1) badges.push({ label: 'Chat', cssClass: 'badge-role-chat' });
    if ((role & 2) === 2) badges.push({ label: 'Title', cssClass: 'badge-role-title' });
    if ((role & 4) === 4) badges.push({ label: 'Benchmark', cssClass: 'badge-role-benchmark' });
    return badges;
  }

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
    const items = document.querySelectorAll('.model-item');
    items.forEach(item => item.classList.remove('drag-over-top', 'drag-over-bottom'));
  }

  onConfigDragOver(event: DragEvent) {
    event.preventDefault();
    if (event.dataTransfer) {
      event.dataTransfer.dropEffect = 'move';
    }
    const targetItem = (event.target as HTMLElement).closest('.model-item');
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
    const targetItem = (event.target as HTMLElement).closest('.model-item');
    if (targetItem) {
      targetItem.classList.remove('drag-over-top', 'drag-over-bottom');
    }
  }

  onConfigDrop(event: DragEvent, dropIndex: number): void {
    event.preventDefault();
    const targetItem = (event.target as HTMLElement).closest('.model-item');
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
