import { Component, OnInit, inject, ViewChild, ElementRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { SettingsService, UserAiSettings, ApiModelDto } from '../services/settings.service';
import { RouterModule } from '@angular/router';

@Component({
  selector: 'app-settings',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterModule],
  styleUrl: './settings.component.scss',
  templateUrl: './settings.component.html'
})
export class SettingsComponent implements OnInit {
  settingsService = inject(SettingsService);
  
  @ViewChild('successToast') successToast!: ElementRef<HTMLElement>;
  @ViewChild('confirmDialog') confirmDialog!: ElementRef<HTMLDialogElement>;
  allowMultipleModels = false;
  initAllowMultipleModels = false;

  spoilerFreeMode = true;
  initSpoilerFreeMode = true;
  
  showSourceCodeReferences = false;
  initShowSourceCodeReferences = false;

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

  performanceLimits: any = null;

  loading = false;
  saved = false;

  get isDirty(): boolean {
    return this.allowMultipleModels !== this.initAllowMultipleModels ||
           this.spoilerFreeMode !== this.initSpoilerFreeMode ||
           this.showSourceCodeReferences !== this.initShowSourceCodeReferences ||
           this.enableWebSearch !== this.initEnableWebSearch ||
           this.enableToolUse !== this.initEnableToolUse ||
           this.enableClientTools !== this.initEnableClientTools ||
           this.enableGameActions !== this.initEnableGameActions ||
           this.maxResultLength !== this.initMaxResultLength ||
           this.maxCallsPerSession !== this.initMaxCallsPerSession ||
           this.maxToolIterations !== this.initMaxToolIterations;
  }

  get resultLengthOptions() {
    if (!this.performanceLimits?.maxResultLength) return [];
    const l = this.performanceLimits.maxResultLength;
    return [
      { label: 'Default', value: null, text: `Default \u2013 ${l.defaultValue} chars` },
      { label: 'Minimal', value: l.min, text: `Minimal \u2013 ${l.min} chars` },
      { label: 'Low', value: Math.max(l.min, Math.round(l.defaultValue * 0.5)), text: `Low \u2013 ${Math.max(l.min, Math.round(l.defaultValue * 0.5))} chars` },
      { label: 'Medium', value: l.defaultValue, text: `Medium \u2013 ${l.defaultValue} chars` },
      { label: 'High', value: Math.min(l.max, Math.round(l.defaultValue * 2)), text: `High \u2013 ${Math.min(l.max, Math.round(l.defaultValue * 2))} chars` },
      { label: 'Very High', value: l.max, text: `Very High \u2013 ${l.max} chars` },
      { label: 'Custom', value: 'custom', text: 'Custom' }
    ];
  }

  get callsOptions() {
    if (!this.performanceLimits?.maxCallsPerSession) return [];
    const l = this.performanceLimits.maxCallsPerSession;
    return [
      { label: 'Default', value: null, text: `Default \u2013 ${l.defaultValue}` },
      { label: 'Minimal', value: l.min, text: `Minimal \u2013 ${l.min}` },
      { label: 'Low', value: Math.max(l.min, Math.round(l.defaultValue * 0.5)), text: `Low \u2013 ${Math.max(l.min, Math.round(l.defaultValue * 0.5))}` },
      { label: 'Medium', value: l.defaultValue, text: `Medium \u2013 ${l.defaultValue}` },
      { label: 'High', value: Math.min(l.max, Math.round(l.defaultValue * 2)), text: `High \u2013 ${Math.min(l.max, Math.round(l.defaultValue * 2))}` },
      { label: 'Very High', value: l.max, text: `Very High \u2013 ${l.max}` },
      { label: 'Custom', value: 'custom', text: 'Custom' }
    ];
  }

  get iterationsOptions() {
    if (!this.performanceLimits?.maxToolIterations) return [];
    const l = this.performanceLimits.maxToolIterations;
    return [
      { label: 'Default', value: null, text: `Default \u2013 ${l.defaultValue}` },
      { label: 'Minimal', value: l.min, text: `Minimal \u2013 ${l.min}` },
      { label: 'Low', value: Math.max(l.min, Math.round(l.defaultValue * 0.5)), text: `Low \u2013 ${Math.max(l.min, Math.round(l.defaultValue * 0.5))}` },
      { label: 'Medium', value: l.defaultValue, text: `Medium \u2013 ${l.defaultValue}` },
      { label: 'High', value: Math.min(l.max, Math.round(l.defaultValue * 2)), text: `High \u2013 ${Math.min(l.max, Math.round(l.defaultValue * 2))}` },
      { label: 'Very High', value: l.max, text: `Very High \u2013 ${l.max}` },
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

    this.settingsService.getSettings().subscribe(s => {
      if (s) {
        if (s.allowMultipleModels !== undefined) {
          this.allowMultipleModels = s.allowMultipleModels;
          this.initAllowMultipleModels = s.allowMultipleModels;
        }
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
        if (s.maxResultLength !== undefined) {
          this.maxResultLength = s.maxResultLength ?? null;
          this.initMaxResultLength = this.maxResultLength;
        }
        if (s.maxCallsPerSession !== undefined) {
          this.maxCallsPerSession = s.maxCallsPerSession ?? null;
          this.initMaxCallsPerSession = this.maxCallsPerSession;
        }
        if (s.maxToolIterations !== undefined) {
          this.maxToolIterations = s.maxToolIterations ?? null;
          this.initMaxToolIterations = this.maxToolIterations;
        }
        if (s.performanceLimits) {
          this.performanceLimits = s.performanceLimits;
        }
        this.initializeSelects();
      }
    });
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

  saveSettings() {
    this.loading = true;
    this.saved = false;
    this.settingsService.saveSettings(this.spoilerFreeMode, this.enableWebSearch, this.enableToolUse, this.enableClientTools, this.enableGameActions, this.allowMultipleModels, this.showSourceCodeReferences, this.maxResultLength, this.maxCallsPerSession, this.maxToolIterations).subscribe(() => {
      this.loading = false;
      
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

      this.initAllowMultipleModels = this.allowMultipleModels;
      this.initSpoilerFreeMode = this.spoilerFreeMode;
      this.initShowSourceCodeReferences = this.showSourceCodeReferences;
      this.initEnableWebSearch = this.enableWebSearch;
      this.initEnableToolUse = this.enableToolUse;
      this.initEnableClientTools = this.enableClientTools;
      this.initEnableGameActions = this.enableGameActions;
      this.initMaxResultLength = this.maxResultLength;
      this.initMaxCallsPerSession = this.maxCallsPerSession;
      this.initMaxToolIterations = this.maxToolIterations;
    });
  }
}
