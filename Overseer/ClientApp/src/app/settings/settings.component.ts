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

  enableWebSearch = true;
  initEnableWebSearch = true;
  enableToolUse = true;
  initEnableToolUse = true;
  enableClientTools = true;
  initEnableClientTools = true;
  enableGameActions = false;
  initEnableGameActions = false;

  loading = false;
  saved = false;

  get isDirty(): boolean {
    return this.allowMultipleModels !== this.initAllowMultipleModels ||
           this.spoilerFreeMode !== this.initSpoilerFreeMode ||
           this.enableWebSearch !== this.initEnableWebSearch ||
           this.enableToolUse !== this.initEnableToolUse ||
           this.enableClientTools !== this.initEnableClientTools ||
           this.enableGameActions !== this.initEnableGameActions;
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
      }
    });
  }

  saveSettings() {
    this.loading = true;
    this.saved = false;
    // We send empty strings for the legacy provider/model/apiKey fields 
    // since the API might still require them in the body, but they are now managed in other tabs.
    this.settingsService.saveSettings('', '', '', undefined, this.spoilerFreeMode, null, null, this.enableWebSearch, this.enableToolUse, this.enableClientTools, this.enableGameActions, this.allowMultipleModels).subscribe(() => {
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
      this.initEnableWebSearch = this.enableWebSearch;
      this.initEnableToolUse = this.enableToolUse;
      this.initEnableClientTools = this.enableClientTools;
      this.initEnableGameActions = this.enableGameActions;
    });
  }
}
