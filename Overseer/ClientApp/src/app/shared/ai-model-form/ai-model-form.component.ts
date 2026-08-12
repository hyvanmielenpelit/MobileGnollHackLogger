import { Component, EventEmitter, Input, OnInit, Output, inject, ChangeDetectionStrategy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { SettingsService, ApiModelDto } from '../../services/settings.service';

export interface AiModelFormResult {
  displayName: string;
  provider: string;
  modelId: string;
  thinkingLevel: string | null;
  reasoningMode: string | null;
  reasoningSummary: string | null;
  maxInputTokens: number | null;
  maxOutputTokens: number | null;
  apiKey?: string;
  isEnabled?: boolean;
  isSystemWide?: boolean;
  modelRole?: number;
  note?: string | null;
}

@Component({
    selector: 'app-ai-model-form',
    imports: [CommonModule, FormsModule],
    templateUrl: './ai-model-form.component.html',
    changeDetection: ChangeDetectionStrategy.Eager,
    styleUrl: './ai-model-form.component.scss'
})
export class AiModelFormComponent implements OnInit {
  private settingsService = inject(SettingsService);

  @Input() mode: 'add' | 'edit' = 'add';
  @Input() isAdmin: boolean = false;
  @Input() providers: string[] = ['OpenAI', 'Anthropic', 'Google'];
  @Input() initialProvider?: string;
  @Input() initialData?: any;
  @Input() saving: boolean = false;

  @Output() save = new EventEmitter<AiModelFormResult>();
  @Output() cancel = new EventEmitter<void>();

  // Form fields
  displayName = '';
  provider = '';
  modelId = '';
  customModelId = '';
  thinkingLevel = '';
  customThinkingLevel = '';
  reasoningMode = '';
  customReasoningMode = '';
  reasoningSummary = '';
  customReasoningSummary = '';
  maxInputTokens: number | null = null;
  maxOutputTokens: number | null = null;

  // Admin fields
  apiKey = '';
  hasApiKey = false;
  isEnabled = true;
  isSystemWide = false;
  modelRole: number = 3;
  note: string | null = null;

  // State
  loadingModels = false;
  modelError = '';
  availableModels: ApiModelDto[] = [];
  selectedModelObj: ApiModelDto | null = null;
  
  // UI Selection State
  pickerModelSelect = '';
  pickerThinkingLevelSelect = '';
  pickerReasoningModeSelect = '';
  pickerReasoningSummarySelect = '';
  sortMode: 'alphabetical' | 'newest' = 'alphabetical';
  lastAutoDisplayName = '';
  showApiKeyInfo = false;
  editingApiKey = false;
  deleteApiKey = false;

  get sortedModels() {
    return [...this.availableModels].sort((a, b) => {
      if (a.isRecommended && !b.isRecommended) return -1;
      if (!a.isRecommended && b.isRecommended) return 1;
      if (a.isRecommended && b.isRecommended) {
        return (a.recommendationRank || 0) - (b.recommendationRank || 0);
      }

      if (this.sortMode === 'newest') {
        if (b.createdAt !== a.createdAt) {
          return b.createdAt - a.createdAt;
        }
        return b.id.localeCompare(a.id, undefined, { numeric: true, sensitivity: 'base' });
      }
      return a.id.localeCompare(b.id, undefined, { numeric: true, sensitivity: 'base' });
    });
  }

  ngOnInit() {
    if (this.initialProvider) {
      this.provider = this.initialProvider;
    } else if (this.providers.length > 0) {
      this.provider = this.providers[0];
    } else {
      this.provider = 'OpenAI';
    }

    if (this.initialData) {
      this.displayName = this.initialData.displayName || '';
      this.modelId = this.initialData.modelId || '';
      this.thinkingLevel = this.initialData.thinkingLevel || '';
      this.reasoningMode = this.initialData.reasoningMode || '';
      this.reasoningSummary = this.initialData.reasoningSummary || '';
      this.maxInputTokens = this.initialData.maxInputTokens || null;
      this.maxOutputTokens = this.initialData.maxOutputTokens || null;
      
      if (this.isAdmin) {
        this.apiKey = ''; // Start blank for edit, user can update it
        this.hasApiKey = this.initialData.hasApiKey || false;
        this.editingApiKey = !this.hasApiKey;
        this.isEnabled = this.initialData.isEnabled ?? true;
        this.isSystemWide = this.initialData.isSystemWide ?? false;
        this.modelRole = this.initialData.modelRole ?? 3;
        this.note = this.initialData.note || null;
      }

      this.pickerModelSelect = this.modelId;
      this.pickerThinkingLevelSelect = this.thinkingLevel;
      this.pickerReasoningModeSelect = this.reasoningMode;
      this.pickerReasoningSummarySelect = this.reasoningSummary;
      
      if (!this.isAdmin || this.hasApiKey) {
        this.fetchModels(true);
      } else {
        if (this.modelId) {
           this.pickerModelSelect = 'custom';
           this.customModelId = this.modelId;
        }
        if (this.thinkingLevel) {
           this.pickerThinkingLevelSelect = 'custom';
           this.customThinkingLevel = this.thinkingLevel;
        }
        if (this.reasoningMode) {
           this.pickerReasoningModeSelect = 'custom';
           this.customReasoningMode = this.reasoningMode;
        }
        if (this.reasoningSummary) {
           this.pickerReasoningSummarySelect = 'custom';
           this.customReasoningSummary = this.reasoningSummary;
        }
      }
    } else {
      // Add mode
      if (!this.isAdmin) {
        this.fetchModels();
      }
    }
  }

  onProviderChange() {
    this.availableModels = [];
    this.selectedModelObj = null;
    this.modelError = '';
    
    if (!this.isAdmin) {
      this.fetchModels();
    } else {
      if (this.apiKey) {
        this.fetchModels();
      }
    }
  }

  onCheckModels() {
    if (this.isAdmin && !this.apiKey && !this.hasApiKey) {
       this.modelError = 'Please enter an API Key first.';
       return;
    }
    this.fetchModels();
  }

  fetchModels(isInitializingEdit = false) {
    if (this.isAdmin && !this.apiKey && !this.hasApiKey && this.mode === 'add') {
       if (!isInitializingEdit) {
           this.modelError = 'Please enter an API Key first.';
       }
       return;
    }

    this.loadingModels = true;
    this.modelError = '';
    
    const keyToSend = this.isAdmin ? this.apiKey : '';
    const systemConfigId = this.isAdmin ? this.initialData?.id : undefined;

    this.settingsService.getAvailableModels(this.provider, keyToSend, systemConfigId).subscribe({
      next: (models) => {
        this.availableModels = models;
        this.loadingModels = false;
        if (this.availableModels.length === 0) {
          this.modelError = 'No models available or API key not configured.';
          return;
        }

        if (isInitializingEdit) {
           this.selectedModelObj = this.availableModels.find(m => m.id === this.modelId) || null;
           if (this.selectedModelObj) {
              this.pickerModelSelect = this.modelId;
           } else {
              this.pickerModelSelect = 'custom';
              this.customModelId = this.modelId;
           }
           
           if (this.thinkingLevel && this.selectedModelObj && !this.selectedModelObj.supportedThinkingLevels.includes(this.thinkingLevel)) {
              this.pickerThinkingLevelSelect = 'custom';
              this.customThinkingLevel = this.thinkingLevel;
           }
           if (this.reasoningMode && this.selectedModelObj && !this.selectedModelObj.supportedReasoningModes?.includes(this.reasoningMode)) {
              this.pickerReasoningModeSelect = 'custom';
              this.customReasoningMode = this.reasoningMode;
           }
           if (this.reasoningSummary && this.selectedModelObj && !this.selectedModelObj.supportedReasoningSummaries?.includes(this.reasoningSummary)) {
              this.pickerReasoningSummarySelect = 'custom';
              this.customReasoningSummary = this.reasoningSummary;
           }
        } else {
           if (this.sortedModels.length > 0) {
             this.pickerModelSelect = this.sortedModels[0].id;
             this.onPickerModelSelect();
           } else {
             this.pickerModelSelect = 'custom';
             this.onPickerModelSelect();
           }
        }
      },
      error: (err) => {
        this.loadingModels = false;
        this.modelError = err.error?.message || err.message || 'Error fetching models.';
        if (isInitializingEdit) {
            if (this.modelId) {
                this.pickerModelSelect = 'custom';
                this.customModelId = this.modelId;
            }
            if (this.thinkingLevel) {
                this.pickerThinkingLevelSelect = 'custom';
                this.customThinkingLevel = this.thinkingLevel;
            }
            if (this.reasoningMode) {
                this.pickerReasoningModeSelect = 'custom';
                this.customReasoningMode = this.reasoningMode;
            }
            if (this.reasoningSummary) {
                this.pickerReasoningSummarySelect = 'custom';
                this.customReasoningSummary = this.reasoningSummary;
            }
        }
      }
    });
  }

  onPickerModelSelect() {
    if (this.pickerModelSelect !== 'custom') {
      this.modelId = this.pickerModelSelect;
    } else {
      this.modelId = '';
    }
    
    this.selectedModelObj = this.availableModels.find(m => m.id === this.modelId) || null;
    if (this.selectedModelObj) {
      if (this.selectedModelObj.supportedThinkingLevels && this.selectedModelObj.supportedThinkingLevels.length > 0) {
        if (this.selectedModelObj.isRecommended && this.selectedModelObj.recommendedThinkingLevel && this.selectedModelObj.supportedThinkingLevels.includes(this.selectedModelObj.recommendedThinkingLevel)) {
          this.thinkingLevel = this.selectedModelObj.recommendedThinkingLevel;
        } else {
          this.thinkingLevel = this.selectedModelObj.supportedThinkingLevels.includes('medium') 
            ? 'medium' 
            : this.selectedModelObj.supportedThinkingLevels[0];
        }
        this.pickerThinkingLevelSelect = this.thinkingLevel;
      } else {
        this.thinkingLevel = '';
        this.pickerThinkingLevelSelect = '';
      }
      
      if (this.selectedModelObj.supportedReasoningModes && this.selectedModelObj.supportedReasoningModes.length > 0) {
        this.reasoningMode = this.selectedModelObj.supportedReasoningModes.includes('medium') 
          ? 'medium' 
          : this.selectedModelObj.supportedReasoningModes[0];
        this.pickerReasoningModeSelect = this.reasoningMode;
      } else {
        this.reasoningMode = '';
        this.pickerReasoningModeSelect = '';
      }

      if (this.selectedModelObj.supportedReasoningSummaries && this.selectedModelObj.supportedReasoningSummaries.length > 0) {
        this.reasoningSummary = this.selectedModelObj.supportedReasoningSummaries.includes('auto') 
          ? 'auto' 
          : this.selectedModelObj.supportedReasoningSummaries[0];
        this.pickerReasoningSummarySelect = this.reasoningSummary;
      } else {
        this.reasoningSummary = '';
        this.pickerReasoningSummarySelect = '';
      }
      
      this.maxInputTokens = this.selectedModelObj.maxInputTokens || null;
      this.maxOutputTokens = this.selectedModelObj.maxOutputTokens || null;
      
      if (this.isAdmin) {
          if (!this.displayName || this.displayName === this.lastAutoDisplayName) {
              this.displayName = this.selectedModelObj.description || this.selectedModelObj.id;
              this.lastAutoDisplayName = this.displayName;
          }
      }
    } else {
      this.thinkingLevel = '';
      this.pickerThinkingLevelSelect = '';
      this.reasoningMode = '';
      this.pickerReasoningModeSelect = '';
      this.reasoningSummary = '';
      this.pickerReasoningSummarySelect = '';
    }
  }

  onPickerThinkingLevelChange() {
    if (this.pickerThinkingLevelSelect !== 'custom') {
      this.thinkingLevel = this.pickerThinkingLevelSelect;
    } else {
      this.thinkingLevel = ''; 
    }
  }

  onPickerReasoningModeChange() {
    if (this.pickerReasoningModeSelect !== 'custom') {
      this.reasoningMode = this.pickerReasoningModeSelect;
    } else {
      this.reasoningMode = ''; 
    }
  }

  onPickerReasoningSummaryChange() {
    if (this.pickerReasoningSummarySelect !== 'custom') {
      this.reasoningSummary = this.pickerReasoningSummarySelect;
    } else {
      this.reasoningSummary = ''; 
    }
  }

  onSortChange() {
    if (this.sortedModels.length > 0) {
      this.pickerModelSelect = this.sortedModels[0].id;
      this.onPickerModelSelect();
    }
  }

  formatThinkingLevel(level: string): string {
    if (!level) return 'Default';
    return level.charAt(0).toUpperCase() + level.slice(1);
  }

  toggleApiKeyInfo() {
    this.showApiKeyInfo = !this.showApiKeyInfo;
  }

  onCancel() {
    this.cancel.emit();
  }

  onSave() {
    this.modelError = '';
    const finalModelId = this.pickerModelSelect === 'custom' ? this.customModelId : this.modelId;
    const finalThinkingLevel = this.pickerThinkingLevelSelect === 'custom' ? this.customThinkingLevel : this.thinkingLevel;
    const finalReasoningMode = this.pickerReasoningModeSelect === 'custom' ? this.customReasoningMode : this.reasoningMode;
    const finalReasoningSummary = this.pickerReasoningSummarySelect === 'custom' ? this.customReasoningSummary : this.reasoningSummary;
    
    if (!finalModelId) {
        this.modelError = 'Model is required.';
        return;
    }

    let finalDisplayName = this.displayName;
    if (!finalDisplayName || finalDisplayName.trim() === '') {
      finalDisplayName = this.selectedModelObj?.description || this.selectedModelObj?.id || finalModelId;
    }

    if (this.isAdmin) {
      if (!/^[a-zA-Z0-9 _\-.]+$/.test(finalDisplayName)) {
        this.modelError = 'Display Name can only contain letters, numbers, spaces, underscores, dashes, and dots.';
        return;
      }
    }

    const result: AiModelFormResult = {
      displayName: finalDisplayName,
      provider: this.provider,
      modelId: finalModelId,
      thinkingLevel: finalThinkingLevel || null,
      reasoningMode: finalReasoningMode || null,
      reasoningSummary: finalReasoningSummary || null,
      maxInputTokens: this.maxInputTokens,
      maxOutputTokens: this.maxOutputTokens
    };

    if (this.isAdmin) {
      if (this.deleteApiKey) {
        result.apiKey = '';
      } else if (this.editingApiKey) {
        result.apiKey = this.apiKey;
      }
      result.isEnabled = this.isEnabled;
      result.isSystemWide = this.isSystemWide;
      result.modelRole = this.modelRole;
      result.note = this.note;
    }

    this.save.emit(result);
  }

  getReasoningSummaryDefaultLabel(): string {
    if (this.provider === 'Anthropic') {
      return 'Default (Full Thinking)';
    }
    return 'None';
  }
}
