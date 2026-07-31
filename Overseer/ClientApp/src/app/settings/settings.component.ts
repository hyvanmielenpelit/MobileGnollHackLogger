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
  @ViewChild('apiKeyInfoDialog') apiKeyInfoDialog!: ElementRef<HTMLDialogElement>;
  @ViewChild('modelPickerDialog') modelPickerDialog!: ElementRef<HTMLDialogElement>;

  provider = 'OpenAI';
  model = 'gpt-4o-mini';
  thinkingLevel = 'high';
  thinkingLevelSelect = 'high';

  initProvider = 'OpenAI';
  initModel = 'gpt-4o-mini';
  initThinkingLevel = 'high';

  spoilerFreeMode = true;
  initSpoilerFreeMode = true;

  maxInputTokens: number | null = null;
  maxOutputTokens: number | null = null;
  initMaxInputTokens: number | null = null;
  initMaxOutputTokens: number | null = null;

  apiKey = '';
  hasApiKey = false;
  loading = false;
  saved = false;

  // Model Picker State
  loadingModels = false;
  availableModels: ApiModelDto[] = [];
  selectedModel = '';
  selectedModelObj: ApiModelDto | null = null;
  modalThinkingLevel = '';
  modelError = '';
  sortMode: 'alphabetical' | 'newest' = 'alphabetical';

  get sortedModels() {
    if (this.sortMode === 'newest') {
      return [...this.availableModels].sort((a, b) => {
        if (b.createdAt !== a.createdAt) {
          return b.createdAt - a.createdAt;
        }
        // Fallback for models with tied/zero createdAt (like Gemini)
        // Reverse alphabetical with numeric sort will put newer versions first (e.g., 3.5 before 2.5)
        return b.id.localeCompare(a.id, undefined, { numeric: true, sensitivity: 'base' });
      });
    }
    return [...this.availableModels].sort((a, b) => a.id.localeCompare(b.id, undefined, { numeric: true, sensitivity: 'base' }));
  }

  get isDirty(): boolean {
    return this.provider !== this.initProvider ||
           this.model !== this.initModel ||
           this.thinkingLevel !== this.initThinkingLevel ||
           this.spoilerFreeMode !== this.initSpoilerFreeMode ||
           this.maxInputTokens !== this.initMaxInputTokens ||
           this.maxOutputTokens !== this.initMaxOutputTokens ||
           this.apiKey.length > 0;
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
        if (s.provider) { this.provider = s.provider; this.initProvider = s.provider; }
        if (s.model) { this.model = s.model; this.initModel = s.model; }
        if (s.thinkingLevel !== undefined && s.thinkingLevel !== null) {
          this.thinkingLevel = s.thinkingLevel;
          this.initThinkingLevel = s.thinkingLevel;
          const standardOptions = ['', 'minimal', 'low', 'medium', 'high', 'xhigh', 'max', 'pro'];
          if (standardOptions.includes(this.thinkingLevel)) {
            this.thinkingLevelSelect = this.thinkingLevel;
          } else {
            this.thinkingLevelSelect = 'custom';
          }
        }
        this.hasApiKey = s.hasApiKey;
        if (s.spoilerFreeMode !== undefined) {
          this.spoilerFreeMode = s.spoilerFreeMode;
          this.initSpoilerFreeMode = s.spoilerFreeMode;
        }
        if (s.maxInputTokens !== undefined) {
          this.maxInputTokens = s.maxInputTokens;
          this.initMaxInputTokens = s.maxInputTokens;
        }
        if (s.maxOutputTokens !== undefined) {
          this.maxOutputTokens = s.maxOutputTokens;
          this.initMaxOutputTokens = s.maxOutputTokens;
        }
      }
    });
  }

  onSelectChange() {
    if (this.thinkingLevelSelect !== 'custom') {
      this.thinkingLevel = this.thinkingLevelSelect;
    } else {
      this.thinkingLevel = ''; // Clear for user to type
    }
  }

  saveSettings() {
    this.loading = true;
    this.saved = false;
    this.settingsService.saveSettings(this.provider, this.model, this.apiKey, this.thinkingLevel, this.spoilerFreeMode, this.maxInputTokens, this.maxOutputTokens).subscribe(() => {
      this.loading = false;
      
      const toast = this.successToast?.nativeElement as any;
      if (toast && ("popover" in HTMLElement.prototype || toast.classList.contains('\\:popover-open') || 'showPopover' in toast)) {
        toast.showPopover();
        setTimeout(() => {
          try { toast.hidePopover(); } catch(e) {}
        }, 3000);
      } else {
        this.saved = true;
        setTimeout(() => this.saved = false, 3000);
      }

      if (this.apiKey) {
        this.hasApiKey = true;
        this.apiKey = '';
      }
      this.initProvider = this.provider;
      this.initModel = this.model;
      this.initThinkingLevel = this.thinkingLevel;
      this.initSpoilerFreeMode = this.spoilerFreeMode;
      this.initMaxInputTokens = this.maxInputTokens;
      this.initMaxOutputTokens = this.maxOutputTokens;
    });
  }

  deleteApiKey() {
    if (confirm("Are you sure you want to delete your saved API key?")) {
      this.loading = true;
      this.settingsService.deleteApiKey().subscribe({
        next: () => {
          this.loading = false;
          this.hasApiKey = false;
          this.apiKey = '';
        },
        error: (err) => {
          this.loading = false;
          console.error("Failed to delete API key", err);
        }
      });
    }
  }

  checkModels() {
    this.loadingModels = true;
    this.modelError = '';
    this.availableModels = [];
    this.selectedModel = '';
    this.selectedModelObj = null;
    this.modalThinkingLevel = '';
    this.modelPickerDialog?.nativeElement.showModal();

    this.settingsService.getAvailableModels(this.provider, this.apiKey).subscribe({
      next: (models) => {
        this.availableModels = models;
        this.loadingModels = false;
        if (this.sortedModels.length > 0) {
          this.selectedModel = this.sortedModels[0].id;
          this.onModelSelect();
        }
      },
      error: (err) => {
        this.loadingModels = false;
        this.modelError = err.error?.message || err.message || 'An unknown error occurred while fetching models.';
      }
    });
  }

  onModelSelect() {
    this.selectedModelObj = this.availableModels.find(m => m.id === this.selectedModel) || null;
    if (this.selectedModelObj && this.selectedModelObj.supportedThinkingLevels && this.selectedModelObj.supportedThinkingLevels.length > 0) {
      // Pick a default if available, e.g. medium or the first one
      this.modalThinkingLevel = this.selectedModelObj.supportedThinkingLevels.includes('medium') 
        ? 'medium' 
        : this.selectedModelObj.supportedThinkingLevels[0];
    } else {
      this.modalThinkingLevel = '';
    }
  }

  closeModal() {
    this.modelPickerDialog?.nativeElement.close();
    this.modelError = '';
    this.availableModels = [];
    this.selectedModelObj = null;
  }

  onSortChange() {
    if (this.sortedModels.length > 0) {
      this.selectedModel = this.sortedModels[0].id;
      this.onModelSelect();
    }
  }

  applySelectedModel() {
    if (this.selectedModel) {
      this.model = this.selectedModel;
      
      // Map the thinking level back to the main form
      if (this.modalThinkingLevel) {
        this.thinkingLevel = this.modalThinkingLevel;
        const standardOptions = ['', 'minimal', 'low', 'medium', 'high', 'xhigh', 'max', 'pro'];
        if (standardOptions.includes(this.thinkingLevel)) {
          this.thinkingLevelSelect = this.thinkingLevel;
        } else {
          this.thinkingLevelSelect = 'custom';
        }
      } else {
        this.thinkingLevel = '';
        this.thinkingLevelSelect = '';
      }
      
      this.maxInputTokens = this.selectedModelObj?.maxInputTokens ?? null;
      this.maxOutputTokens = this.selectedModelObj?.maxOutputTokens ?? null;
    }
    this.closeModal();
  }

  openApiKeyInfo() {
    this.apiKeyInfoDialog?.nativeElement.showModal();
  }

  closeApiKeyInfo() {
    this.apiKeyInfoDialog?.nativeElement.close();
  }
}
