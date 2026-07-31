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
  template: `
    <div class="settings-container gh-main-container">
      <div class="header-row">
        <h2>AI Provider Settings</h2>
        <a routerLink="/chat" class="nav-back-link">&larr; Back to Chat</a>
      </div>
      <form (ngSubmit)="saveSettings()">
        <div>
          <label>Provider</label>
          <select [(ngModel)]="provider" name="provider" class="gh-input">
            <option value="OpenAI">OpenAI</option>
            <option value="Anthropic">Anthropic</option>
            <option value="Google">Google</option>
          </select>
        </div>
        <div>
          <label for="apiKeyInput">API Key</label>
          <span id="apiKeyHint" class="form-hint">Leave blank to keep existing</span>
          <div class="api-key-grid">
            <input type="password" id="apiKeyInput" [(ngModel)]="apiKey" name="apiKey" class="gh-input m-0" aria-describedby="apiKeyHint" />
            <div class="flex-center">
              <span *ngIf="apiKey.length > 0" title="API key set but not saved yet." class="status-icon status-warning">&#10004;</span>
              <span *ngIf="apiKey.length === 0 && hasApiKey" title="An API key is currently saved." class="status-icon status-success">&#10004;</span>
              <span *ngIf="apiKey.length === 0 && !hasApiKey" title="No API key saved." class="status-icon status-error">&#9888;</span>
            </div>
            <button *ngIf="hasApiKey" type="button" class="btn-gh btn-gh-delete btn-delete-api" (click)="deleteApiKey()" [disabled]="loading">Delete</button>
          </div>
        </div>
        <div class="model-row">
          <label>Model</label>
          <div class="model-grid">
            <input type="text" [(ngModel)]="model" name="model" class="gh-input m-0" />
            <button type="button" class="btn-gh btn-check-models" (click)="checkModels()" [disabled]="loadingModels">
              {{ loadingModels ? 'Checking...' : 'Check Models' }}
            </button>
          </div>
        </div>
        <div>
          <label>Thinking Level</label>
          <div class="flex-gap-10">
            <select [(ngModel)]="thinkingLevelSelect" (ngModelChange)="onSelectChange()" name="thinkingLevelSelect" class="gh-input flex-1">
              <option value="">None</option>
              <option value="minimal">Minimal</option>
              <option value="low">Low</option>
              <option value="medium">Medium</option>
              <option value="high">High</option>
              <option value="xhigh">Xhigh</option>
              <option value="max">Max</option>
              <option value="pro">Pro</option>
              <option value="custom">Custom...</option>
            </select>
            <input *ngIf="thinkingLevelSelect === 'custom'" type="text" [(ngModel)]="thinkingLevel" name="thinkingLevel" placeholder="Enter custom value..." class="gh-input flex-1" />
          </div>
        </div>
        
        <div class="margin-y-20">
          <label class="checkbox-label">
            <input type="checkbox" [(ngModel)]="spoilerFreeMode" name="spoilerFreeMode" class="w-auto m-0" aria-describedby="spoilerFreeHint" />
            <span>Spoiler-Free Mode</span>
          </label>
          <span id="spoilerFreeHint" class="form-hint hint-indent">Limit hints to avoid spoiling secrets</span>
        </div>

        <div class="token-row">
          <div class="flex-1">
            <label for="maxInputTokensInput">Max Input Tokens</label>
            <span id="maxInputHint" class="form-hint">Leave blank for no limit</span>
            <input type="number" id="maxInputTokensInput" [(ngModel)]="maxInputTokens" name="maxInputTokens" class="gh-input w-100 mt-5" aria-describedby="maxInputHint" />
          </div>
          <div class="flex-1">
            <label for="maxOutputTokensInput">Max Output Tokens</label>
            <span id="maxOutputHint" class="form-hint">Leave blank for default</span>
            <input type="number" id="maxOutputTokensInput" [(ngModel)]="maxOutputTokens" name="maxOutputTokens" class="gh-input w-100 mt-5" aria-describedby="maxOutputHint" />
          </div>
        </div>

        <button type="submit" class="btn-gh save-btn" [disabled]="loading">Save</button>
        <div #successToast popover="manual" class="toast-success">
          &#10004; Settings saved successfully!
        </div>
        <!-- Fallback if polyfill completely fails -->
        <span *ngIf="saved" class="success">Settings saved successfully!</span>
      </form>
    </div>

    <!-- Modal for picking models -->
    <div class="modal-overlay" *ngIf="showModelModal">
      <div class="modal-content gh-main-container">
        <h3 class="modal-title">Available Models</h3>
        <p class="modal-subtitle">Showing models released after 1 Jan, 2026</p>
        
        <div *ngIf="modelError" class="modal-error">
          <p>{{ modelError }}</p>
          <div class="modal-actions">
            <button type="button" class="btn-gh" (click)="closeModal()">OK</button>
          </div>
        </div>

        <div *ngIf="!modelError">
          <div class="segmented-control">
            <label>
              <input type="radio" name="sortMode" value="alphabetical" [(ngModel)]="sortMode" (change)="onSortChange()">
              <span>Alphabetical</span>
            </label>
            <label>
              <input type="radio" name="sortMode" value="newest" [(ngModel)]="sortMode" (change)="onSortChange()">
              <span>Newest</span>
            </label>
          </div>
          <select [(ngModel)]="selectedModel" (ngModelChange)="onModelSelect()" size="10" class="model-listbox gh-input">
            <option *ngFor="let m of sortedModels" [value]="m.id">{{ m.description }}</option>
          </select>
          
          <div *ngIf="selectedModelObj" class="model-meta">
            <div class="meta-grid">
              <strong>Context Window:</strong> 
              <span>{{ selectedModelObj.contextWindowSize | number }} tokens</span>
              
              <strong>Max Input Tokens:</strong> 
              <span>{{ selectedModelObj.maxInputTokens | number }} tokens</span>
              
              <strong>Max Output Tokens:</strong> 
              <span>{{ selectedModelObj.maxOutputTokens | number }} tokens</span>
              
              <strong>Supported Thinking:</strong>
              <ng-container *ngIf="selectedModelObj.supportedThinkingLevels && selectedModelObj.supportedThinkingLevels.length > 0; else noThinking">
                <select [(ngModel)]="modalThinkingLevel" class="gh-input m-0 w-100">
                  <option *ngFor="let level of selectedModelObj.supportedThinkingLevels" [value]="level">{{ level }}</option>
                </select>
              </ng-container>
              <ng-template #noThinking>
                <span>None</span>
              </ng-template>
            </div>
          </div>

          <div class="modal-actions">
            <button type="button" class="btn-gh btn-gh-cancel" (click)="closeModal()">Cancel</button>
            <button type="button" class="btn-gh" (click)="applySelectedModel()" [disabled]="!selectedModel">OK</button>
          </div>
        </div>
      </div>
    </div>
    <!-- Unsaved Changes Dialog -->
    <dialog #confirmDialog class="gh-dialog">
      <form method="dialog">
        <h3 class="modal-title">Unsaved Changes</h3>
        <p>You have unsaved changes. Are you sure you want to discard them and leave?</p>
        <div class="modal-actions mt-20">
          <button value="cancel" class="btn-gh btn-gh-cancel">Cancel</button>
          <button value="discard" class="btn-gh">Discard</button>
        </div>
      </form>
    </dialog>
  `
})
export class SettingsComponent implements OnInit {
  settingsService = inject(SettingsService);
  
  @ViewChild('successToast') successToast!: ElementRef<HTMLElement>;
  @ViewChild('confirmDialog') confirmDialog!: ElementRef<HTMLDialogElement>;

  provider = 'OpenAI';
  model = 'gpt-4o-mini';
  thinkingLevel = 'high';
  thinkingLevelSelect = 'high';

  initProvider = 'OpenAI';
  initModel = 'gpt-4o-mini';
  initThinkingLevel = 'high';

  spoilerFreeMode = false;
  initSpoilerFreeMode = false;

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
  showModelModal = false;
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
    this.showModelModal = true;

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
    this.showModelModal = false;
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
}
