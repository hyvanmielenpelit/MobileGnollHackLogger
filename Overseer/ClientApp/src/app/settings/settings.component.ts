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
    <div class="settings-container">
      <h2>AI Provider Settings</h2>
      <a routerLink="/chat">Back to Chat</a>
      <hr>
      <form (ngSubmit)="saveSettings()">
        <div>
          <label>Provider</label>
          <select [(ngModel)]="provider" name="provider">
            <option value="OpenAI">OpenAI</option>
            <option value="Anthropic">Anthropic</option>
            <option value="Google">Google</option>
          </select>
        </div>
        <div>
          <label>API Key (Leave blank to keep existing)</label>
          <input type="password" [(ngModel)]="apiKey" name="apiKey" />
          <small *ngIf="hasApiKey">An API key is currently saved.</small>
        </div>
        <div class="model-row">
          <label>Model</label>
          <div style="display: flex; gap: 10px;">
            <input type="text" [(ngModel)]="model" name="model" style="flex: 1;" />
            <button type="button" class="btn-check-models" (click)="checkModels()" [disabled]="loadingModels">
              {{ loadingModels ? 'Checking...' : 'Check Models' }}
            </button>
          </div>
        </div>
        <div>
          <label>Thinking Level</label>
          <div style="display: flex; gap: 10px;">
            <select [(ngModel)]="thinkingLevelSelect" (ngModelChange)="onSelectChange()" name="thinkingLevelSelect" style="flex: 1;">
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
            <input *ngIf="thinkingLevelSelect === 'custom'" type="text" [(ngModel)]="thinkingLevel" name="thinkingLevel" placeholder="Enter custom value..." style="flex: 1;" />
          </div>
        </div>
        
        <button type="submit" [disabled]="loading">Save</button>
        <span *ngIf="saved" class="success">Settings saved successfully!</span>
      </form>
    </div>

    <!-- Modal for picking models -->
    <div class="modal-overlay" *ngIf="showModelModal">
      <div class="modal-content">
        <h3>Available Models</h3>
        
        <div *ngIf="modelError" class="modal-error">
          <p>{{ modelError }}</p>
          <div class="modal-actions">
            <button type="button" class="btn-primary" (click)="closeModal()">OK</button>
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
          <select [(ngModel)]="selectedModel" size="10" class="model-listbox">
            <option *ngFor="let m of sortedModels" [value]="m.id">{{ m.id }}</option>
          </select>
          <div class="modal-actions">
            <button type="button" class="btn-secondary" (click)="closeModal()">Cancel</button>
            <button type="button" class="btn-primary" (click)="applySelectedModel()" [disabled]="!selectedModel">OK</button>
          </div>
        </div>
      </div>
    </div>
  `,
  styles: [`
    .settings-container { max-width: 600px; margin: 50px auto; padding: 20px; border: 1px solid #ccc; border-radius: 8px; }
    form div { margin-bottom: 15px; }
    .model-row { margin-bottom: 15px; }
    label { display: block; margin-bottom: 5px; font-weight: bold; }
    input, select { width: 100%; padding: 8px; box-sizing: border-box; }
    button { padding: 10px 20px; background: #28a745; color: white; border: none; border-radius: 4px; cursor: pointer; }
    .btn-check-models { background: #007bff; white-space: nowrap; }
    .btn-check-models:disabled { background: #6c757d; cursor: not-allowed; }
    .success { color: green; margin-left: 10px; }

    /* Modal Styles */
    .modal-overlay {
      position: fixed; top: 0; left: 0; width: 100%; height: 100%;
      background: rgba(0, 0, 0, 0.5);
      display: flex; justify-content: center; align-items: center;
      z-index: 1000;
    }
    .modal-content {
      background: white; padding: 20px; border-radius: 8px;
      width: 400px; max-width: 90%;
      box-shadow: 0 4px 6px rgba(0,0,0,0.1);
    }
    .modal-content h3 { margin-top: 0; }
    .model-listbox { width: 100%; padding: 5px; margin-bottom: 15px; }
    .modal-actions { display: flex; justify-content: flex-end; gap: 10px; margin-top: 15px; }
    .btn-primary { background: #007bff; }
    .btn-secondary { background: #6c757d; }
    .modal-error { color: #dc3545; }
    
    .segmented-control {
      display: inline-flex;
      background: #f0f0f0;
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
      color: #666;
      transition: all 0.3s ease;
    }
    .segmented-control input[type="radio"] {
      display: none;
    }
    .segmented-control label:has(input:checked) {
      background: #fff;
      color: #333;
      box-shadow: 0 1px 3px rgba(0,0,0,0.1);
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

  checkModels() {
    this.loadingModels = true;
    this.modelError = '';
    this.availableModels = [];
    this.selectedModel = '';
    this.showModelModal = true;

    this.settingsService.getAvailableModels(this.provider, this.apiKey).subscribe({
      next: (models) => {
        this.availableModels = models;
        this.loadingModels = false;
        if (this.sortedModels.length > 0) {
          this.selectedModel = this.sortedModels[0].id;
        }
      },
      error: (err) => {
        this.loadingModels = false;
        this.modelError = err.error?.message || err.message || 'An unknown error occurred while fetching models.';
      }
    });
  }

  closeModal() {
    this.showModelModal = false;
    this.modelError = '';
    this.availableModels = [];
  }

  onSortChange() {
    if (this.sortedModels.length > 0) {
      this.selectedModel = this.sortedModels[0].id;
    }
  }

  applySelectedModel() {
    if (this.selectedModel) {
      this.model = this.selectedModel;
    }
    this.closeModal();
  }
}
