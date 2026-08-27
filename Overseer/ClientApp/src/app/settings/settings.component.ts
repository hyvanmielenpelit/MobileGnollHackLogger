import { Component, OnInit, OnDestroy, inject, ViewChild, ElementRef, ChangeDetectionStrategy, ChangeDetectorRef } from '@angular/core';

import { FormsModule } from '@angular/forms';
import { SettingsService, UserAiSettings, ApiModelDto } from '../services/settings.service';
import { SystemService } from '../services/system.service';
import { ChangelogService } from '../services/changelog.service';
import { ChatService } from '../services/chat.service';
import { RouterModule } from '@angular/router';
import { ChangelogComponent } from '../changelog/changelog.component';
import { TrashModalComponent } from '../shared/trash-modal/trash-modal.component';

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
  @ViewChild('confirmDialog') confirmDialog!: ElementRef<HTMLDialogElement>;
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
  initSpoilerFreeMode = true;
  
  showSourceCodeReferences = false;
  initShowSourceCodeReferences = false;

  showThoughtsAndTools = 0;
  initShowThoughtsAndTools = 0;

  enableWebSearch = true;
  initEnableWebSearch = true;
  enableToolUse = true;
  initEnableToolUse = true;
  enableClientTools = true;
  initEnableClientTools = true;
  enableGameActions = false;
  initEnableGameActions = false;

  maxResultLength: number | null = null;
  initMaxResultLength: number | null = null;
  maxResultLengthSelect: any = null;

  maxCallsPerSession: number | null = null;
  initMaxCallsPerSession: number | null = null;
  maxCallsPerSessionSelect: any = null;

  maxToolIterations: number | null = null;
  initMaxToolIterations: number | null = null;
  maxToolIterationsSelect: any = null;

  maxParallelToolCalls: number | null = null;
  initMaxParallelToolCalls: number | null = null;
  maxParallelToolCallsSelect: any = null;

  requestTimeout: number | null = null;
  initRequestTimeout: number | null = null;

  performanceLimits: any = null;

  loading = false;
  saved = false;

  get isDirty(): boolean {
    return this.spoilerFreeMode !== this.initSpoilerFreeMode ||
           this.showSourceCodeReferences !== this.initShowSourceCodeReferences ||
           this.enableWebSearch !== this.initEnableWebSearch ||
           this.enableToolUse !== this.initEnableToolUse ||
           this.enableClientTools !== this.initEnableClientTools ||
           this.enableGameActions !== this.initEnableGameActions ||
           this.showThoughtsAndTools !== this.initShowThoughtsAndTools ||
           this.maxResultLength !== this.initMaxResultLength ||
           this.maxCallsPerSession !== this.initMaxCallsPerSession ||
           this.maxToolIterations !== this.initMaxToolIterations ||
           this.maxParallelToolCalls !== this.initMaxParallelToolCalls ||
           this.requestTimeout !== this.initRequestTimeout;
  }

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

  canDeactivate(): Promise<boolean> | boolean {
    if (!this.isDirty) return true;
    
    const dialog = this.confirmDialog.nativeElement;
    dialog.showModal();
    
    return new Promise<boolean>((resolve) => {
      const onClose = () => {
        dialog.removeEventListener('close', onClose);
        resolve(dialog.returnValue === 'discard');
      };
      dialog.addEventListener('close', onClose);
    });
  }

  ngOnInit() {
    this.checkChangelogBadge();
    
    this.changelogBadgeResetHandler = () => this.checkChangelogBadge();
    window.addEventListener('changelog_badge_reset', this.changelogBadgeResetHandler);

    if (!("popover" in HTMLElement.prototype)) {
      import("@oddbird/popover-polyfill").catch(err => console.warn('Failed to load popover polyfill', err));
    }
    if (!('interestForElement' in HTMLButtonElement.prototype)) {
      // @ts-ignore
      import("interestfor").catch(err => console.warn('Failed to load interestfor polyfill', err));
    }
    if (!("anchorName" in document.documentElement.style)) {
      // @ts-ignore
      import("@oddbird/css-anchor-positioning").catch(err => console.warn('Failed to load anchor positioning polyfill', err));
    }

    this.systemService.getVersion().subscribe({
      next: (v) => this.appVersion = v,
      error: () => {}
    });

    this.loadChatMetrics();

    this.settingsService.getSettings().subscribe({
      next: (s) => {
        if (s) {
          if (s.spoilerFreeMode !== undefined) {
            this.spoilerFreeMode = s.spoilerFreeMode;
            this.initSpoilerFreeMode = s.spoilerFreeMode;
          }
          if (s.showSourceCodeReferences !== undefined) {
            this.showSourceCodeReferences = s.showSourceCodeReferences;
            this.initShowSourceCodeReferences = s.showSourceCodeReferences;
          }
          if (s.enableWebSearch !== undefined) {
            this.enableWebSearch = s.enableWebSearch;
            this.initEnableWebSearch = s.enableWebSearch;
          }
          if (s.enableToolUse !== undefined) {
            this.enableToolUse = s.enableToolUse;
            this.initEnableToolUse = s.enableToolUse;
          }
          if (s.enableClientTools !== undefined) {
            this.enableClientTools = s.enableClientTools;
            this.initEnableClientTools = s.enableClientTools;
          }
          if (s.enableGameActions !== undefined) {
            this.enableGameActions = s.enableGameActions;
            this.initEnableGameActions = s.enableGameActions;
          }
          if (s.showThoughtsAndTools !== undefined) {
            this.showThoughtsAndTools = Number(s.showThoughtsAndTools ?? 0);
            this.initShowThoughtsAndTools = this.showThoughtsAndTools;
          }
          if (s.maxResultLength !== undefined) {
            this.maxResultLength = s.maxResultLength;
            this.initMaxResultLength = s.maxResultLength;
          }
          if (s.maxCallsPerSession !== undefined) {
            this.maxCallsPerSession = s.maxCallsPerSession;
            this.initMaxCallsPerSession = s.maxCallsPerSession;
          }
          if (s.maxToolIterations !== undefined) {
            this.maxToolIterations = s.maxToolIterations;
            this.initMaxToolIterations = s.maxToolIterations;
          }
          if (s.maxParallelToolCalls !== undefined) {
            this.maxParallelToolCalls = s.maxParallelToolCalls;
            this.initMaxParallelToolCalls = s.maxParallelToolCalls;
          }
          if (s.requestTimeout !== undefined) {
            this.requestTimeout = s.requestTimeout;
            this.initRequestTimeout = s.requestTimeout;
          }
          if (s.performanceLimits) {
            this.performanceLimits = s.performanceLimits;
          }
          this.initializeSelects();
        }
      },
      error: () => {}
    });
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

  saveSettings() {
    this.loading = true;
    this.saved = false;
    this.settingsService.saveSettings(this.spoilerFreeMode, this.enableWebSearch, this.enableToolUse, this.enableClientTools, this.enableGameActions, this.showSourceCodeReferences, this.maxResultLength, this.maxCallsPerSession, this.maxToolIterations, this.maxParallelToolCalls, Number(this.showThoughtsAndTools), this.requestTimeout).subscribe({
      next: () => {
        this.loading = false;
        this.settingsService.showThoughtsAndToolsUpdated.next(Number(this.showThoughtsAndTools));
        
        this.showToast('Settings saved successfully!');

        this.initSpoilerFreeMode = this.spoilerFreeMode;
        this.initShowSourceCodeReferences = this.showSourceCodeReferences;
        this.initEnableWebSearch = this.enableWebSearch;
        this.initEnableToolUse = this.enableToolUse;
        this.initEnableClientTools = this.enableClientTools;
        this.initEnableGameActions = this.enableGameActions;
        this.initShowThoughtsAndTools = this.showThoughtsAndTools;
        this.initMaxResultLength = this.maxResultLength;
        this.initMaxCallsPerSession = this.maxCallsPerSession;
        this.initMaxToolIterations = this.maxToolIterations;
        this.initMaxParallelToolCalls = this.maxParallelToolCalls;
        this.initRequestTimeout = this.requestTimeout;
      },
      error: () => {
        this.loading = false;
      }
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
  }
}
