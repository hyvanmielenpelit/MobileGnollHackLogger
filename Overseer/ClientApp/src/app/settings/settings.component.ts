import { Component, OnInit, inject } from '@angular/core';
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
          <label>API Key (Leave blank to keep existing)</label>
          <div style="display: flex; gap: 10px; align-items: center;">
            <input type="password" [(ngModel)]="apiKey" name="apiKey" class="gh-input" style="flex: 1;" />
            <span *ngIf="apiKey.length > 0" title="API key set but not saved yet." style="color: #ffc107; font-size: 1.5em; flex-shrink: 0; margin-left: 10px;">&#10004;</span>
            <span *ngIf="apiKey.length === 0 && hasApiKey" title="An API key is currently saved." style="color: #28a745; font-size: 1.5em; flex-shrink: 0; margin-left: 10px;">&#10004;</span>
            <span *ngIf="apiKey.length === 0 && !hasApiKey" title="No API key saved." style="color: #dc3545; font-size: 1.5em; flex-shrink: 0; margin-left: 10px;">&#9888;</span>
            <button *ngIf="hasApiKey" type="button" class="btn-gh btn-gh-delete" style="min-width: 100px; min-height: 36px; padding: 5px 10px;" (click)="deleteApiKey()" [disabled]="loading">Delete</button>
          </div>
        </div>
        <div class="model-row">
          <label>Model</label>
          <div style="display: flex; gap: 10px;">
            <input type="text" [(ngModel)]="model" name="model" style="flex: 1;" class="gh-input" />
            <button type="button" class="btn-gh" (click)="checkModels()" [disabled]="loadingModels">
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
        
        <button type="submit" class="btn-gh" [disabled]="loading">Save</button>
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
            <option *ngFor="let m of sortedModels" [value]="m.id">{{ m.id }}</option>
          </select>
          
          <div *ngIf="selectedModelObj" class="model-meta">
            <strong>Description:</strong> {{ selectedModelObj.description }}<br/>
            <div *ngIf="selectedModelObj.supportedThinkingLevels && selectedModelObj.supportedThinkingLevels.length > 0" style="margin-top: 10px;">
              <strong>Supported Thinking:</strong>
              <select [(ngModel)]="modalThinkingLevel" class="gh-input" style="margin-top: 5px;">
                <option *ngFor="let level of selectedModelObj.supportedThinkingLevels" [value]="level">{{ level }}</option>
              </select>
            </div>
            <div *ngIf="!selectedModelObj.supportedThinkingLevels || selectedModelObj.supportedThinkingLevels.length === 0" style="margin-top: 10px;">
              <strong>Supported Thinking:</strong> None
            </div>
          </div>

          <div class="modal-actions">
            <button type="button" class="btn-gh btn-gh-cancel" (click)="closeModal()">Cancel</button>
            <button type="button" class="btn-gh" (click)="applySelectedModel()" [disabled]="!selectedModel">OK</button>
          </div>
        </div>
      </div>
    </div>
  `,
  styles: [`
    .settings-container { max-width: 600px; margin: 50px auto; padding: 30px; }
    h2 { margin-top: 0; }
    form div { margin-bottom: 15px; }
    .model-row { margin-bottom: 15px; }
    label { display: block; margin-bottom: 5px; font-weight: bold; }
    .success { color: #28a745; margin-left: 10px; font-weight: bold; }

    /* Modal Styles */
    .modal-overlay {
      position: fixed; top: 0; left: 0; width: 100%; height: 100%;
      background: rgba(0, 0, 0, 0.5);
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
  `]
})
export class SettingsComponent implements OnInit {
  settingsService = inject(SettingsService);

  provider = 'OpenAI';
  model = 'gpt-4o-mini';
  thinkingLevel = 'high';
  thinkingLevelSelect = 'high';

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

  ngOnInit() {
    this.settingsService.getSettings().subscribe(s => {
      if (s) {
        if (s.provider) this.provider = s.provider;
        if (s.model) this.model = s.model;
        if (s.thinkingLevel !== undefined && s.thinkingLevel !== null) {
          this.thinkingLevel = s.thinkingLevel;
          const standardOptions = ['', 'minimal', 'low', 'medium', 'high', 'xhigh', 'max', 'pro'];
          if (standardOptions.includes(this.thinkingLevel)) {
            this.thinkingLevelSelect = this.thinkingLevel;
          } else {
            this.thinkingLevelSelect = 'custom';
          }
        }
        this.hasApiKey = s.hasApiKey;
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
    this.settingsService.saveSettings(this.provider, this.model, this.apiKey, this.thinkingLevel).subscribe(() => {
      this.loading = false;
      this.saved = true;
      if (this.apiKey) {
        this.hasApiKey = true;
        this.apiKey = '';
      }
      setTimeout(() => this.saved = false, 3000);
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
    }
    this.closeModal();
  }
}
