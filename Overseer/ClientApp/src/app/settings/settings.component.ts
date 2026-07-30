import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { SettingsService, UserAiSettings } from '../services/settings.service';
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
          <label>Model</label>
          <input type="text" [(ngModel)]="model" name="model" />
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
        <div>
          <label>API Key (Leave blank to keep existing)</label>
          <input type="password" [(ngModel)]="apiKey" name="apiKey" />
          <small *ngIf="hasApiKey">An API key is currently saved.</small>
        </div>
        <button type="submit" [disabled]="loading">Save</button>
        <span *ngIf="saved" class="success">Settings saved successfully!</span>
      </form>
    </div>
  `,
  styles: [`
    .settings-container { max-width: 600px; margin: 50px auto; padding: 20px; border: 1px solid #ccc; border-radius: 8px; }
    form div { margin-bottom: 15px; }
    label { display: block; margin-bottom: 5px; font-weight: bold; }
    input, select { width: 100%; padding: 8px; box-sizing: border-box; }
    button { padding: 10px 20px; background: #28a745; color: white; border: none; border-radius: 4px; cursor: pointer; }
    .success { color: green; margin-left: 10px; }
  `]
})
export class SettingsComponent implements OnInit {
  settingsService = inject(SettingsService);

  provider = 'OpenAI';
  model = 'gpt-4o-mini';
  thinkingLevel = 'high';
  thinkingLevelSelect = 'high';

  onSelectChange() {
    if (this.thinkingLevelSelect !== 'custom') {
      this.thinkingLevel = this.thinkingLevelSelect;
    } else {
      this.thinkingLevel = ''; // Clear for user to type
    }
  }
  apiKey = '';
  hasApiKey = false;
  loading = false;
  saved = false;

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
}
