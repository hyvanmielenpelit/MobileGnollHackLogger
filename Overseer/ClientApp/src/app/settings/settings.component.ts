import { Component, OnInit, OnDestroy, inject, ViewChild, ElementRef, ChangeDetectionStrategy, ChangeDetectorRef } from '@angular/core';

import { FormsModule } from '@angular/forms';
import { SettingsService, UserAiSettings, ApiModelDto } from '../services/settings.service';
import { SystemService } from '../services/system.service';
import { ChangelogService } from '../services/changelog.service';
import { RouterModule } from '@angular/router';
import { ChangelogComponent } from '../changelog/changelog.component';

@Component({
    selector: 'app-settings',
    imports: [FormsModule, RouterModule, ChangelogComponent],
    styleUrl: './settings.component.scss',
    changeDetection: ChangeDetectionStrategy.Eager,
    templateUrl: './settings.component.html'
})
export class SettingsComponent implements OnInit, OnDestroy {
  settingsService = inject(SettingsService);
  systemService = inject(SystemService);
  changelogService = inject(ChangelogService);
  cdr = inject(ChangeDetectorRef);
  
  appVersion = '';
  
  @ViewChild('successToast') successToast!: ElementRef<HTMLElement>;
  @ViewChild('confirmDialog') confirmDialog!: ElementRef<HTMLDialogElement>;
  @ViewChild('changelogDialog') changelogDialog!: ElementRef<HTMLDialogElement>;

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
  }

  onSelectChange(field: string, value: any) {
    if (value !== 'custom') {
      const numVal = value === 'null' || value === null ? null : Number(value);
      if (field === 'maxResultLength') this.maxResultLength = numVal;
      if (field === 'maxCallsPerSession') this.maxCallsPerSession = numVal;
      if (field === 'maxToolIterations') this.maxToolIterations = numVal;
    }
  }

  getSelectedOptionDisplay(field: 'maxResultLength' | 'maxCallsPerSession' | 'maxToolIterations'): { label: string, valueText: string } {
    let selectVal, options;
    if (field === 'maxResultLength') { selectVal = this.maxResultLengthSelect; options = this.resultLengthOptions; }
    else if (field === 'maxCallsPerSession') { selectVal = this.maxCallsPerSessionSelect; options = this.callsOptions; }
    else { selectVal = this.maxToolIterationsSelect; options = this.iterationsOptions; }

    const opt = options.find(o => o.value === selectVal);
    if (!opt) return { label: 'Select...', valueText: '' };

    if (opt.value === 'custom') return { label: 'Custom', valueText: '' };
    
    const valStr = `${opt.value ?? this.performanceLimits?.[field]?.defaultValue} ${field === 'maxResultLength' ? 'chars' : ''}`;
    return { label: opt.label, valueText: valStr.trim() };
  }

  selectCustomOption(field: 'maxResultLength' | 'maxCallsPerSession' | 'maxToolIterations', value: any, popoverId: string) {
    if (field === 'maxResultLength') this.maxResultLengthSelect = value;
    else if (field === 'maxCallsPerSession') this.maxCallsPerSessionSelect = value;
    else if (field === 'maxToolIterations') this.maxToolIterationsSelect = value;
    
    this.onSelectChange(field, value);
    
    const popoverElement = document.getElementById(popoverId) as any;
    if (popoverElement && typeof popoverElement.hidePopover === 'function') {
      popoverElement.hidePopover();
    }
  }

  saveSettings() {
    this.loading = true;
    this.saved = false;
    this.settingsService.saveSettings(this.spoilerFreeMode, this.enableWebSearch, this.enableToolUse, this.enableClientTools, this.enableGameActions, this.showSourceCodeReferences, this.maxResultLength, this.maxCallsPerSession, this.maxToolIterations, Number(this.showThoughtsAndTools), this.requestTimeout).subscribe({
      next: () => {
        this.loading = false;
        this.settingsService.showThoughtsAndToolsUpdated.next(Number(this.showThoughtsAndTools));
        
        const toast = this.successToast?.nativeElement as any;
        if (toast && ("popover" in HTMLElement.prototype || toast.classList.contains('\:popover-open') || 'showPopover' in toast)) {
          toast.showPopover();
          setTimeout(() => {
            try { toast.hidePopover(); } catch(e) {}
          }, 3000);
        } else {
          this.saved = true;
          setTimeout(() => this.saved = false, 3000);
        }

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
        this.initRequestTimeout = this.requestTimeout;
      },
      error: () => {
        this.loading = false;
      }
    });
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
