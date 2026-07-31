import { Component, OnInit, inject, ViewChild, ElementRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { SettingsService, UserAiSettings, ApiModelDto } from '../services/settings.service';
import { RouterModule } from '@angular/router';

@Component({
  selector: 'app-settings',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterModule],
  template: `
    <div class="settings-container gh-main-container">
      <div style="display: flex; justify-content: space-between; align-items: center; margin-bottom: 20px;">
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
          <div style="display: grid; grid-template-columns: 1fr 40px 140px; gap: 10px; align-items: center; margin-top: 5px;">
            <input type="password" id="apiKeyInput" [(ngModel)]="apiKey" name="apiKey" class="gh-input" aria-describedby="apiKeyHint" style="margin: 0;" />
            <div style="display: flex; justify-content: center; align-items: center;">
              <span *ngIf="apiKey.length > 0" title="API key set but not saved yet." style="color: #ffc107; font-size: 1.5em; transform: translateY(5px); display: inline-block;">&#10004;</span>
              <span *ngIf="apiKey.length === 0 && hasApiKey" title="An API key is currently saved." style="color: #28a745; font-size: 1.5em; transform: translateY(5px); display: inline-block;">&#10004;</span>
              <span *ngIf="apiKey.length === 0 && !hasApiKey" title="No API key saved." style="color: #dc3545; font-size: 1.5em; transform: translateY(5px); display: inline-block;">&#9888;</span>
            </div>
            <button *ngIf="hasApiKey" type="button" class="btn-gh btn-gh-delete" style="width: 140px; min-width: 140px; min-height: 36px; padding: 5px 10px; margin: 0;" (click)="deleteApiKey()" [disabled]="loading">Delete</button>
          </div>
        </div>
        <div class="model-row">
          <label>Model</label>
          <div style="display: grid; grid-template-columns: 1fr 40px 140px; gap: 10px; align-items: center;">
            <input type="text" [(ngModel)]="model" name="model" class="gh-input" style="margin: 0;" />
            <button type="button" class="btn-gh" style="grid-column: 2 / span 2; width: 100%; margin: 0;" (click)="checkModels()" [disabled]="loadingModels">
              {{ loadingModels ? 'Checking...' : 'Check Models' }}
            </button>
          </div>
        </div>
        <div>
          <label>Thinking Level</label>
          <div style="display: flex; gap: 10px;">
            <select [(ngModel)]="thinkingLevelSelect" (ngModelChange)="onSelectChange()" name="thinkingLevelSelect" style="flex: 1;" class="gh-input">
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
            <input *ngIf="thinkingLevelSelect === 'custom'" type="text" [(ngModel)]="thinkingLevel" name="thinkingLevel" placeholder="Enter custom value..." style="flex: 1;" class="gh-input" />
          </div>
        </div>
        
        <div style="margin-top: 20px; margin-bottom: 20px;">
          <label style="display: flex; align-items: center; gap: 10px; cursor: pointer;">
            <input type="checkbox" [(ngModel)]="spoilerFreeMode" name="spoilerFreeMode" style="width: auto; margin: 0;" aria-describedby="spoilerFreeHint" />
            <span>Spoiler-Free Mode</span>
          </label>
          <span id="spoilerFreeHint" class="form-hint" style="margin-left: 28px; margin-top: 4px;">Limit hints to avoid spoiling secrets</span>
        </div>

        <div style="display: flex; gap: 30px; margin-bottom: 20px;">
          <div style="flex: 1;">
            <label for="maxInputTokensInput">Max Input Tokens</label>
            <span id="maxInputHint" class="form-hint">Leave blank for no limit</span>
            <input type="number" id="maxInputTokensInput" [(ngModel)]="maxInputTokens" name="maxInputTokens" class="gh-input" style="width: 100%; margin-top: 5px;" aria-describedby="maxInputHint" />
          </div>
          <div style="flex: 1;">
            <label for="maxOutputTokensInput">Max Output Tokens</label>
            <span id="maxOutputHint" class="form-hint">Leave blank for default</span>
            <input type="number" id="maxOutputTokensInput" [(ngModel)]="maxOutputTokens" name="maxOutputTokens" class="gh-input" style="width: 100%; margin-top: 5px;" aria-describedby="maxOutputHint" />
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
        <h3 style="color: var(--title-color);">Available Models</h3>
        <p style="font-size: 0.8em; color: #aaa; margin-top: -10px; margin-bottom: 15px;">Showing models released after 1 Jan, 2026</p>
        
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
            <div style="display: grid; grid-template-columns: max-content 1fr; gap: 10px 15px; align-items: center;">
              <strong>Context Window:</strong> 
              <span>{{ selectedModelObj.contextWindowSize | number }} tokens</span>
              
              <strong>Max Input Tokens:</strong> 
              <span>{{ selectedModelObj.maxInputTokens | number }} tokens</span>
              
              <strong>Max Output Tokens:</strong> 
              <span>{{ selectedModelObj.maxOutputTokens | number }} tokens</span>
              
              <strong>Supported Thinking:</strong>
              <ng-container *ngIf="selectedModelObj.supportedThinkingLevels && selectedModelObj.supportedThinkingLevels.length > 0; else noThinking">
                <select [(ngModel)]="modalThinkingLevel" class="gh-input" style="margin: 0; width: 100%;">
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
        <h3 style="margin-top: 0; color: var(--title-color);">Unsaved Changes</h3>
        <p>You have unsaved changes. Are you sure you want to discard them and leave?</p>
        <div class="modal-actions" style="margin-top: 20px;">
          <button value="cancel" class="btn-gh btn-gh-cancel">Cancel</button>
          <button value="discard" class="btn-gh">Discard</button>
        </div>
      </form>
    </dialog>
  `,
  styles: [`
    .settings-container { max-width: 600px; margin: 50px auto; padding: 30px; }
    h2 { margin-top: 0; }
    form div { margin-bottom: 15px; }
    .model-row { margin-bottom: 15px; }
    label { display: block; margin-bottom: 5px; font-weight: bold; }
    .form-hint { font-size: 0.85em; color: #aaa; font-weight: normal; display: block; }
    .success { color: #28a745; margin-left: 10px; font-weight: bold; }

    /* Modern Toast Notification */
    .save-btn {
      anchor-name: --save-btn;
    }
    
    .toast-success {
      margin: 0;
      border: 1px solid #28a745;
      background-color: rgba(40, 167, 69, 0.1);
      color: #28a745;
      padding: 10px 20px;
      border-radius: 8px;
      font-weight: bold;
      box-shadow: 0 4px 12px rgba(0,0,0,0.5);

      /* Top Layer transitions */
      transition: display 0.3s allow-discrete, opacity 0.3s, transform 0.3s;
      opacity: 0;
      transform: translateY(5px);
      
      /* Anchor position */
      position-anchor: --save-btn;
      left: calc(anchor(right) + 15px);
      top: anchor(center);
      translate: 0 -50%;
    }

    /* Open state */
    .toast-success:is(:popover-open, .\\:popover-open) {
      opacity: 1;
      transform: translateY(0);
      
      @starting-style {
        opacity: 0;
        transform: translateY(5px);
      }
    }

    /* Fallback for lack of anchor positioning */
    @supports not (anchor-name: --save-btn) {
      .toast-success {
        bottom: 30px;
        right: 30px;
        top: auto;
        left: auto;
        translate: 0 0;
      }
    }

    /* Modal Styles */
    .modal-overlay {
      position: fixed; top: 0; left: 0; width: 100%; height: 100%;
      background: rgba(0, 0, 0, 0.35);
      backdrop-filter: blur(2px);
      -webkit-backdrop-filter: blur(2px);
      display: flex; justify-content: center; align-items: center;
      z-index: 1000;
    }
    .modal-content {
      padding: 20px; border-radius: 8px;
      width: 400px; max-width: 90%;
    }
    .modal-content h3 { margin-top: 0; }
    .model-listbox { width: 100%; padding: 5px; margin-bottom: 15px; }
    .modal-actions { display: flex; justify-content: flex-end; gap: 10px; margin-top: 15px; }
    .btn-primary { background: #007bff; }
    .btn-secondary { background: #6c757d; }
    .modal-error { color: #dc3545; }
    
    .segmented-control {
      display: inline-flex;
      background: rgba(0, 0, 0, 0.4);
      border: 1px solid var(--border-glass);
      border-radius: 20px;
      padding: 3px;
      margin-bottom: 15px;
    }
    .segmented-control label {
      padding: 6px 16px;
      cursor: pointer;
      border-radius: 17px;
      margin: 0;
      font-size: 0.9em;
      font-weight: 500;
      color: #aaa;
      transition: all 0.3s ease;
    }
    .segmented-control input[type="radio"] {
      display: none;
    }
    .segmented-control label:has(input:checked) {
      background: var(--primary-color);
      color: black;
      box-shadow: 0 0 5px var(--gold-glow);
    }
    
    .gh-dialog {
      padding: 20px;
      border: 1px solid var(--border-glass, #444);
      border-radius: 8px;
      background: rgba(20, 20, 20, 0.95);
      color: var(--text-color, #fff);
      box-shadow: 0 4px 15px rgba(0, 0, 0, 0.5);
      backdrop-filter: blur(5px);
      -webkit-backdrop-filter: blur(5px);
      max-width: 400px;
    }
    .gh-dialog::backdrop {
      background: rgba(0, 0, 0, 0.6);
      backdrop-filter: blur(2px);
      -webkit-backdrop-filter: blur(2px);
    }
  `]
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
