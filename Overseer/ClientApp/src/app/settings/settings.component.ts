import { Component, OnInit, OnDestroy, inject, ViewChild, ElementRef, ChangeDetectionStrategy, ChangeDetectorRef } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { SettingsService, UserAiSettings, ApiModelDto } from '../services/settings.service';
import { SystemService } from '../services/system.service';
import { ChangelogService } from '../services/changelog.service';
import { ChatService } from '../services/chat.service';
import { RouterModule } from '@angular/router';
import { ChangelogComponent } from '../changelog/changelog.component';
import { TrashModalComponent } from '../shared/trash-modal/trash-modal.component';
import { Subject, BehaviorSubject, Subscription, of, timer, firstValueFrom, EMPTY } from 'rxjs';
import { debounce, tap, switchMap, catchError, filter, timeout } from 'rxjs/operators';
import { ensureOverlayPolyfills } from '../utils/polyfills.util';

@Component({
    selector: 'app-settings',
    imports: [FormsModule, RouterModule, ChangelogComponent, TrashModalComponent],
    styleUrl: './settings.component.scss',
    changeDetection: ChangeDetectionStrategy.Eager,
    templateUrl: './settings.component.html'
})
export class SettingsComponent implements OnInit, OnDestroy {
  settingsService = inject(SettingsService);
  systemService = inject(SystemService);
  changelogService = inject(ChangelogService);
  chatService = inject(ChatService);
  cdr = inject(ChangeDetectorRef);
  
  appVersion = '';
  
  @ViewChild('successToast') successToast!: ElementRef<HTMLElement>;
  @ViewChild('changelogDialog') changelogDialog!: ElementRef<HTMLDialogElement>;
  @ViewChild('settingsBulkDeleteDialog') settingsBulkDeleteDialog!: ElementRef<HTMLDialogElement>;
  @ViewChild('settingsUnpinAllDialog') settingsUnpinAllDialog!: ElementRef<HTMLDialogElement>;
  @ViewChild('settingsTrashModal') settingsTrashModal!: TrashModalComponent;

  activeSessionCount: number = 0;
  pinnedSessionCount: number = 0;
  maxSessionQuota: number = 50;
  maxPinnedQuota: number = 5;
  trashCount: number = 0;

  includePinnedInBulkDelete = false;
  isBulkDeleting = false;
  isUnpinningAll = false;
  toastMessage = '';

  showChangelogBadge = false;
  private changelogBadgeResetHandler!: () => void;

  spoilerFreeMode = true;
  showSourceCodeReferences = false;
  showThoughtsAndTools = 0;
  showParallelBadge = true;
  parallelBadgeEnabled = true;
  showContextWindowUsage = true;

  enableWebSearch = true;
  enableToolUse = true;
  enableSubAgents = false;
  enableClientTools = true;
  enableGameActions = false;

  maxResultLength: number | null = null;
  maxResultLengthSelect: any = null;

  maxCallsPerSession: number | null = null;
  maxCallsPerSessionSelect: any = null;

  maxToolIterations: number | null = null;
  maxToolIterationsSelect: any = null;

  maxParallelToolCalls: number | null = null;
  maxParallelToolCallsSelect: any = null;

  requestTimeout: number | null = null;

  performanceLimits: any = null;

  saved = false;

  saveState: 'idle' | 'saving' | 'saved' | 'error' = 'idle';
  private saveStateSubject = new BehaviorSubject<'idle' | 'saving' | 'saved' | 'error'>('idle');
  private saveSubject = new Subject<{ immediate: boolean }>();
  private saveSubscription!: Subscription;
  private hasPendingChanges = false;
  private isInitialized = false;

  validationErrors: { [field: string]: string } = {};

  lastSavedMaxResultLength: number | null = null;
  lastSavedMaxCallsPerSession: number | null = null;
  lastSavedMaxToolIterations: number | null = null;
  lastSavedMaxParallelToolCalls: number | null = null;
  lastSavedRequestTimeout: number | null = null;

  get resultLengthOptions() {
    if (!this.performanceLimits?.maxResultLength) return [];
    const l = this.performanceLimits.maxResultLength;
    return [
      { label: 'Default', value: null, text: `Default \u2013 ${l.defaultValue} chars` },
      { label: 'Minimal', value: Math.max(l.min, 1000), text: `Minimal \u2013 ${Math.max(l.min, 1000)} chars` },
      { label: 'Low', value: Math.max(l.min, 3000), text: `Low \u2013 ${Math.max(l.min, 3000)} chars` },
      { label: 'Medium', value: 8000, text: `Medium \u2013 8000 chars` },
      { label: 'High', value: Math.min(l.max, 20000), text: `High \u2013 ${Math.min(l.max, 20000)} chars` },
      { label: 'Very High', value: Math.min(l.max, 50000), text: `Very High \u2013 ${Math.min(l.max, 50000)} chars` },
      { label: 'Custom', value: 'custom', text: 'Custom' }
    ];
  }

  get callsOptions() {
    if (!this.performanceLimits?.maxCallsPerSession) return [];
    const l = this.performanceLimits.maxCallsPerSession;
    return [
      { label: 'Default', value: null, text: `Default \u2013 ${l.defaultValue}` },
      { label: 'Minimal', value: Math.max(l.min, 5), text: `Minimal \u2013 ${Math.max(l.min, 5)}` },
      { label: 'Low', value: Math.max(l.min, 15), text: `Low \u2013 ${Math.max(l.min, 15)}` },
      { label: 'Medium', value: 50, text: `Medium \u2013 50` },
      { label: 'High', value: Math.min(l.max, 100), text: `High \u2013 ${Math.min(l.max, 100)}` },
      { label: 'Very High', value: Math.min(l.max, 250), text: `Very High \u2013 ${Math.min(l.max, 250)}` },
      { label: 'Custom', value: 'custom', text: 'Custom' }
    ];
  }

  get iterationsOptions() {
    if (!this.performanceLimits?.maxToolIterations) return [];
    const l = this.performanceLimits.maxToolIterations;
    return [
      { label: 'Default', value: null, text: `Default \u2013 ${l.defaultValue}` },
      { label: 'Minimal', value: Math.max(l.min, 3), text: `Minimal \u2013 ${Math.max(l.min, 3)}` },
      { label: 'Low', value: Math.max(l.min, 5), text: `Low \u2013 ${Math.max(l.min, 5)}` },
      { label: 'Medium', value: 10, text: `Medium \u2013 10` },
      { label: 'High', value: Math.min(l.max, 20), text: `High \u2013 ${Math.min(l.max, 20)}` },
      { label: 'Very High', value: Math.min(l.max, 30), text: `Very High \u2013 ${Math.min(l.max, 30)}` },
      { label: 'Custom', value: 'custom', text: 'Custom' }
    ];
  }

  get parallelOptions() {
    if (!this.performanceLimits?.maxParallelToolCalls) return [];
    const l = this.performanceLimits.maxParallelToolCalls;
    return [
      { label: 'Default', value: null, text: `Default \u2013 ${l.defaultValue}` },
      { label: 'Serial', value: 1, text: `Serial \u2013 1` },
      { label: 'Low', value: Math.min(l.max, Math.max(l.min, 2)), text: `Low \u2013 ${Math.min(l.max, Math.max(l.min, 2))}` },
      { label: 'Medium', value: Math.min(l.max, Math.max(l.min, 4)), text: `Medium \u2013 ${Math.min(l.max, Math.max(l.min, 4))}` },
      { label: 'High', value: Math.min(l.max, 6), text: `High \u2013 ${Math.min(l.max, 6)}` },
      { label: 'Maximum', value: l.max, text: `Maximum \u2013 ${l.max}` },
      { label: 'Custom', value: 'custom', text: 'Custom' }
    ];
  }

  async canDeactivate(): Promise<boolean> {
    if (!this.hasPendingChanges) return true;
    this.revertInvalidFieldsToLastSaved();
    this.saveSubject.next({ immediate: true });
    if (this.saveStateSubject.value === 'saving') {
      try {
        await firstValueFrom(
          this.saveStateSubject.pipe(
            filter(s => s !== 'saving'),
            timeout({ each: 5000, with: () => of('error' as const) })
          )
        );
      } catch {
        // Safety timeout: never block navigation
      }
    }
    return true;
  }

  ngOnInit() {
    this.checkChangelogBadge();
    
    this.changelogBadgeResetHandler = () => this.checkChangelogBadge();
    window.addEventListener('changelog_badge_reset', this.changelogBadgeResetHandler);

    ensureOverlayPolyfills();

    this.systemService.getVersion().subscribe({
      next: (v) => this.appVersion = v,
      error: () => {}
    });

    this.loadChatMetrics();

    this.saveSubscription = this.saveSubject.pipe(
      debounce(req => req.immediate ? of(null) : timer(500)),
      tap(() => {
        this.saveState = 'saving';
        this.saveStateSubject.next('saving');
        this.cdr.detectChanges();
      }),
      switchMap(() => {
        return this.settingsService.saveSettings(
          this.spoilerFreeMode,
          this.enableWebSearch,
          this.enableToolUse,
          this.enableSubAgents,
          this.enableClientTools,
          this.enableGameActions,
          this.showSourceCodeReferences,
          this.maxResultLength,
          this.maxCallsPerSession,
          this.maxToolIterations,
          this.maxParallelToolCalls,
          Number(this.showThoughtsAndTools),
          this.requestTimeout,
          this.showParallelBadge,
          this.showContextWindowUsage
        ).pipe(
          tap(() => {
            this.hasPendingChanges = false;
            this.saveState = 'saved';
            this.saveStateSubject.next('saved');
            this.settingsService.showThoughtsAndToolsUpdated.next(Number(this.showThoughtsAndTools));
            this.updateLastSavedFields();
            this.cdr.detectChanges();
          }),
          catchError(() => {
            this.saveState = 'error';
            this.saveStateSubject.next('error');
            this.cdr.detectChanges();
            return EMPTY;
          })
        );
      })
    ).subscribe();

    this.settingsService.getSettings().subscribe({
      next: (s) => {
        if (s) {
          if (s.spoilerFreeMode !== undefined) {
            this.spoilerFreeMode = s.spoilerFreeMode;
          }
          if (s.showSourceCodeReferences !== undefined) {
            this.showSourceCodeReferences = s.showSourceCodeReferences;
          }
          if (s.showParallelBadge !== undefined) {
            this.showParallelBadge = s.showParallelBadge;
          }
          if (s.parallelBadgeEnabled !== undefined) {
            this.parallelBadgeEnabled = s.parallelBadgeEnabled;
          }
          if (s.showContextWindowUsage !== undefined) {
            this.showContextWindowUsage = s.showContextWindowUsage;
          }
          if (s.enableWebSearch !== undefined) {
            this.enableWebSearch = s.enableWebSearch;
          }
          if (s.enableToolUse !== undefined) {
            this.enableToolUse = s.enableToolUse;
          }
          if (s.enableSubAgents !== undefined) {
            this.enableSubAgents = s.enableSubAgents;
          }
          if (s.enableClientTools !== undefined) {
            this.enableClientTools = s.enableClientTools;
          }
          if (s.enableGameActions !== undefined) {
            this.enableGameActions = s.enableGameActions;
          }
          if (s.showThoughtsAndTools !== undefined) {
            this.showThoughtsAndTools = Number(s.showThoughtsAndTools ?? 0);
          }
          if (s.maxResultLength !== undefined) {
            this.maxResultLength = s.maxResultLength;
          }
          if (s.maxCallsPerSession !== undefined) {
            this.maxCallsPerSession = s.maxCallsPerSession;
          }
          if (s.maxToolIterations !== undefined) {
            this.maxToolIterations = s.maxToolIterations;
          }
          if (s.maxParallelToolCalls !== undefined) {
            this.maxParallelToolCalls = s.maxParallelToolCalls;
          }
          if (s.requestTimeout !== undefined) {
            this.requestTimeout = s.requestTimeout;
          }
          if (s.performanceLimits) {
            this.performanceLimits = s.performanceLimits;
          }
          this.updateLastSavedFields();
          this.initializeSelects();
          this.isInitialized = true;
        }
      },
      error: () => {}
    });
  }

  updateLastSavedFields() {
    this.lastSavedMaxResultLength = this.maxResultLength;
    this.lastSavedMaxCallsPerSession = this.maxCallsPerSession;
    this.lastSavedMaxToolIterations = this.maxToolIterations;
    this.lastSavedMaxParallelToolCalls = this.maxParallelToolCalls;
    this.lastSavedRequestTimeout = this.requestTimeout;
  }

  validateField(field: string, value: number | null): boolean {
    if (value === null || value === undefined) {
      delete this.validationErrors[field];
      return true;
    }
    const limits = this.performanceLimits?.[field];
    if (limits) {
      if ((limits.min !== undefined && value < limits.min) || (limits.max !== undefined && value > limits.max)) {
        this.validationErrors[field] = `Allowed range: ${limits.min} \u2013 ${limits.max}`;
        return false;
      }
    }
    delete this.validationErrors[field];
    return true;
  }

  validateSettings(): boolean {
    const v1 = this.validateField('maxResultLength', this.maxResultLength);
    const v2 = this.validateField('maxCallsPerSession', this.maxCallsPerSession);
    const v3 = this.validateField('maxToolIterations', this.maxToolIterations);
    const v4 = this.validateField('maxParallelToolCalls', this.maxParallelToolCalls);
    const v5 = this.validateField('requestTimeout', this.requestTimeout);
    return v1 && v2 && v3 && v4 && v5;
  }

  revertInvalidFieldsToLastSaved() {
    this.validateSettings();
    if (this.validationErrors['maxResultLength']) {
      this.maxResultLength = this.lastSavedMaxResultLength;
      delete this.validationErrors['maxResultLength'];
    }
    if (this.validationErrors['maxCallsPerSession']) {
      this.maxCallsPerSession = this.lastSavedMaxCallsPerSession;
      delete this.validationErrors['maxCallsPerSession'];
    }
    if (this.validationErrors['maxToolIterations']) {
      this.maxToolIterations = this.lastSavedMaxToolIterations;
      delete this.validationErrors['maxToolIterations'];
    }
    if (this.validationErrors['maxParallelToolCalls']) {
      this.maxParallelToolCalls = this.lastSavedMaxParallelToolCalls;
      delete this.validationErrors['maxParallelToolCalls'];
    }
    if (this.validationErrors['requestTimeout']) {
      this.requestTimeout = this.lastSavedRequestTimeout;
      delete this.validationErrors['requestTimeout'];
    }
    this.initializeSelects();
  }

  onSettingChange() {
    if (!this.isInitialized) return;
    this.hasPendingChanges = true;
    this.saveSubject.next({ immediate: true });
  }

  onClientToolsChange() {
    if (!this.isInitialized) return;
    if (!this.enableClientTools) {
      this.enableGameActions = false;
    }
    this.onSettingChange();
  }

  onNumberInputChange() {
    if (!this.isInitialized) return;
    this.hasPendingChanges = true;
    this.saveSubject.next({ immediate: false });
  }

  onNumberInputBlur(field?: string) {
    if (!this.isInitialized) return;
    if (field) {
      const val = (this as any)[field];
      this.validateField(field, val);
    } else {
      this.validateSettings();
    }
    if (this.validateSettings()) {
      this.hasPendingChanges = true;
      this.saveSubject.next({ immediate: true });
    }
    this.cdr.detectChanges();
  }

  retrySave() {
    this.hasPendingChanges = true;
    this.saveSubject.next({ immediate: true });
  }

  // Closes the open popover for an explicitly specified HTMLElement
  closePopover(popoverElement: HTMLElement | null) {
    if (popoverElement && typeof popoverElement.hidePopover === 'function') {
      popoverElement.hidePopover();
    }
  }

  initializeSelects() {
    const isStandardOption = (val: any, options: any[]) => {
      return options.some(o => o.value !== 'custom' && String(o.value) === String(val));
    };

    this.maxResultLengthSelect = isStandardOption(this.maxResultLength, this.resultLengthOptions) ? this.maxResultLength : 'custom';
    this.maxCallsPerSessionSelect = isStandardOption(this.maxCallsPerSession, this.callsOptions) ? this.maxCallsPerSession : 'custom';
    this.maxToolIterationsSelect = isStandardOption(this.maxToolIterations, this.iterationsOptions) ? this.maxToolIterations : 'custom';
    this.maxParallelToolCallsSelect = isStandardOption(this.maxParallelToolCalls, this.parallelOptions) ? this.maxParallelToolCalls : 'custom';
  }

  onSelectChange(field: string, value: any) {
    if (value !== 'custom') {
      const numVal = value === 'null' || value === null ? null : Number(value);
      if (field === 'maxResultLength') this.maxResultLength = numVal;
      if (field === 'maxCallsPerSession') this.maxCallsPerSession = numVal;
      if (field === 'maxToolIterations') this.maxToolIterations = numVal;
      if (field === 'maxParallelToolCalls') this.maxParallelToolCalls = numVal;
    }
  }

  getSelectedOptionDisplay(field: 'maxResultLength' | 'maxCallsPerSession' | 'maxToolIterations' | 'maxParallelToolCalls'): { label: string, valueText: string } {
    let selectVal, options;
    if (field === 'maxResultLength') { selectVal = this.maxResultLengthSelect; options = this.resultLengthOptions; }
    else if (field === 'maxCallsPerSession') { selectVal = this.maxCallsPerSessionSelect; options = this.callsOptions; }
    else if (field === 'maxToolIterations') { selectVal = this.maxToolIterationsSelect; options = this.iterationsOptions; }
    else { selectVal = this.maxParallelToolCallsSelect; options = this.parallelOptions; }

    const opt = options.find(o => o.value === selectVal);
    if (!opt) return { label: 'Select...', valueText: '' };

    if (opt.value === 'custom') return { label: 'Custom', valueText: '' };
    
    const valStr = `${opt.value ?? this.performanceLimits?.[field]?.defaultValue} ${field === 'maxResultLength' ? 'chars' : ''}`;
    return { label: opt.label, valueText: valStr.trim() };
  }

  selectCustomOption(field: 'maxResultLength' | 'maxCallsPerSession' | 'maxToolIterations' | 'maxParallelToolCalls', value: any, popoverId: string) {
    if (field === 'maxResultLength') this.maxResultLengthSelect = value;
    else if (field === 'maxCallsPerSession') this.maxCallsPerSessionSelect = value;
    else if (field === 'maxToolIterations') this.maxToolIterationsSelect = value;
    else if (field === 'maxParallelToolCalls') this.maxParallelToolCallsSelect = value;
    
    this.onSelectChange(field, value);
    
    const popoverElement = document.getElementById(popoverId) as any;
    if (popoverElement && typeof popoverElement.hidePopover === 'function') {
      popoverElement.hidePopover();
    }

    if (value !== 'custom') {
      delete this.validationErrors[field];
      this.onSettingChange();
    } else {
      delete this.validationErrors[field];
    }
  }

  showToast(msg: string) {
    this.toastMessage = msg;
    this.cdr.detectChanges();
    const toast = this.successToast?.nativeElement as any;
    if (toast && ("popover" in HTMLElement.prototype || toast.classList.contains('\:popover-open') || 'showPopover' in toast)) {
      try { toast.showPopover(); } catch(e) {}
      setTimeout(() => {
        try { toast.hidePopover(); } catch(e) {}
      }, 3000);
    } else {
      this.saved = true;
      setTimeout(() => this.saved = false, 3000);
    }
  }

  loadChatMetrics() {
    this.chatService.getSessions(0, 1).subscribe({
      next: (res) => {
        if (res.body) {
          this.activeSessionCount = res.body.activeCount ?? 0;
          this.pinnedSessionCount = res.body.pinnedCount ?? 0;
          this.maxSessionQuota = res.body.maxQuota ?? 50;
          this.maxPinnedQuota = res.body.maxPinned ?? 5;
          this.cdr.detectChanges();
        }
      },
      error: () => {}
    });
    this.chatService.getTrashSessions().subscribe({
      next: (sessions) => {
        this.trashCount = sessions?.length ?? 0;
        this.cdr.detectChanges();
      },
      error: () => {}
    });
  }

  get bulkDeleteTargetCount(): number {
    if (this.includePinnedInBulkDelete) {
      return this.activeSessionCount;
    }
    return Math.max(0, this.activeSessionCount - this.pinnedSessionCount);
  }

  openSettingsBulkDeleteDialog() {
    this.includePinnedInBulkDelete = false;
    this.isBulkDeleting = false;
    this.settingsBulkDeleteDialog?.nativeElement?.showModal();
  }

  closeSettingsBulkDeleteDialog() {
    this.settingsBulkDeleteDialog?.nativeElement?.close();
    this.isBulkDeleting = false;
  }

  confirmSettingsBulkDelete() {
    this.isBulkDeleting = true;
    this.chatService.bulkDeleteSessions(this.includePinnedInBulkDelete).subscribe({
      next: () => {
        this.isBulkDeleting = false;
        this.closeSettingsBulkDeleteDialog();
        this.loadChatMetrics();
        this.showToast('Active chats moved to trash successfully!');
      },
      error: () => {
        this.isBulkDeleting = false;
        this.closeSettingsBulkDeleteDialog();
      }
    });
  }

  openSettingsUnpinAllDialog() {
    this.isUnpinningAll = false;
    this.settingsUnpinAllDialog?.nativeElement?.showModal();
  }

  closeSettingsUnpinAllDialog() {
    this.settingsUnpinAllDialog?.nativeElement?.close();
    this.isUnpinningAll = false;
  }

  confirmSettingsUnpinAll() {
    this.isUnpinningAll = true;
    this.chatService.unpinAllSessions().subscribe({
      next: () => {
        this.isUnpinningAll = false;
        this.closeSettingsUnpinAllDialog();
        this.loadChatMetrics();
        this.showToast('All chats unpinned successfully!');
      },
      error: () => {
        this.isUnpinningAll = false;
        this.closeSettingsUnpinAllDialog();
      }
    });
  }

  openSettingsTrashDialog() {
    this.settingsTrashModal?.open();
  }

  onSettingsSessionRestored(sessionId: number) {
    this.loadChatMetrics();
    this.showToast('Chat restored successfully!');
  }

  onSettingsTrashEmptied() {
    this.loadChatMetrics();
    this.showToast('Trash emptied successfully!');
  }

  onSettingsTrashCountChange(count: number) {
    this.trashCount = count;
    this.cdr.detectChanges();
  }

  checkChangelogBadge() {
    this.changelogService.getReleaseNotes().subscribe({
      next: (response) => {
        if (response.notes && response.notes.length > 0) {
          const latestVersion = response.notes[0].version;
          this.showChangelogBadge = this.changelogService.hasNewMajorOrMinorVersion(latestVersion);
          this.cdr.detectChanges();
        }
      },
      error: (err) => console.error('Failed to check release notes for animation', err)
    });
  }

  openChangelog() {
    if (this.changelogDialog?.nativeElement) {
      this.changelogDialog.nativeElement.showModal();
    }
  }

  closeChangelogDialog(event: Event) {
    event.stopPropagation();
    event.preventDefault();
    if (this.changelogDialog?.nativeElement) {
      this.changelogDialog.nativeElement.close();
    }
  }

  onChangelogDialogClose() {
    this.checkChangelogBadge();
  }

  onChangelogDialogClick(event: MouseEvent) {
    if (!('closedBy' in HTMLDialogElement.prototype)) {
      const dialog = this.changelogDialog.nativeElement;
      if (event.target !== dialog) return;
      const rect = dialog.getBoundingClientRect();
      const isInside = (
        rect.top <= event.clientY &&
        event.clientY <= rect.top + rect.height &&
        rect.left <= event.clientX &&
        event.clientX <= rect.left + rect.width
      );
      if (!isInside) {
        dialog.close();
      }
    }
  }

  ngOnDestroy() {
    window.removeEventListener('changelog_badge_reset', this.changelogBadgeResetHandler);
    if (this.saveSubscription) {
      this.saveSubscription.unsubscribe();
    }
  }
}
