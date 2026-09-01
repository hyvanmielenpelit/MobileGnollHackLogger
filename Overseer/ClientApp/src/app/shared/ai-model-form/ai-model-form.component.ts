import { Component, EventEmitter, Input, OnInit, Output, inject, ChangeDetectionStrategy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { SettingsService, ApiModelDto } from '../../services/settings.service';

export type DisplayNameMode = 'model_name' | 'model_id' | 'custom';

export interface AiModelFormResult {
  displayName: string;
  displayNameMode: DisplayNameMode;
  provider: string;
  modelId: string;
  thinkingLevel: string | null;
  reasoningMode: string | null;
  reasoningSummary: string | null;
  serviceTier: string | null;
  maxInputTokens: number | null;
  maxOutputTokens: number | null;
  apiKey?: string;
  isEnabled?: boolean;
  isSystemWide?: boolean;
  modelRole?: number;
  parallelExecutionMode?: number;
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
  displayNameMode: DisplayNameMode = 'model_name';
  customDisplayName = '';
  private needsDisplayNameModeInference = false;
  provider = '';
  modelId = '';
  customModelId = '';
  thinkingLevel = '';
  customThinkingLevel = '';
  reasoningMode = '';
  customReasoningMode = '';
  reasoningSummary = '';
  customReasoningSummary = '';
  serviceTier = '';
  customServiceTier = '';
  maxInputTokens: number | null = null;
  maxOutputTokens: number | null = null;

  // Admin fields
  apiKey = '';
  hasApiKey = false;
  isEnabled = true;
  isSystemWide = false;
  modelRole: number = 3;
  parallelExecutionMode: number = 2;
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
  pickerServiceTierSelect = '';
  sortMode: 'alphabetical_asc' | 'alphabetical_desc' | 'created_asc' | 'created_desc' = 'created_desc';
  showApiKeyInfo = false;
  editingApiKey = false;
  deleteApiKey = false;

  /** The model id currently in effect, whether picked from the catalog or typed. */
  get currentModelId(): string {
    return this.pickerModelSelect === 'custom' ? this.customModelId : this.modelId;
  }

  /** The catalog-supplied name, or '' when the model is custom / not in the catalog. */
  get catalogDisplayName(): string {
    const m = this.selectedModelObj;
    if (!m) return '';
    return m.displayName || m.description || m.id;
  }

  getPreviewDisplayName(): string {
    switch (this.displayNameMode) {
      case 'model_id':
        return this.currentModelId;
      case 'custom':
        return this.customDisplayName.trim() || this.currentModelId;
      default:
        return this.catalogDisplayName || this.currentModelId;
    }
  }

  /** The value actually saved. Must never be empty: SystemAiApiConfiguration.DisplayName is non-nullable. */
  getEffectiveDisplayName(): string {
    return this.getPreviewDisplayName();
  }

  private isKnownMode(value: any): value is DisplayNameMode {
    return value === 'model_name' || value === 'model_id' || value === 'custom';
  }

  /** Legacy rows only: reconstruct the mode from the stored name. */
  private inferDisplayNameMode() {
    const stored = (this.displayName || '').trim();
    if (!stored) {
      this.displayNameMode = 'model_name';
      this.customDisplayName = '';
    } else if (this.catalogDisplayName && stored === this.catalogDisplayName) {
      this.displayNameMode = 'model_name';
      this.customDisplayName = '';
    } else if (stored === this.currentModelId) {
      this.displayNameMode = 'model_id';
      this.customDisplayName = '';
    } else {
      this.displayNameMode = 'custom';
      this.customDisplayName = stored;
    }
  }

  private resolveDisplayNameModeIfNeeded() {
    if (this.needsDisplayNameModeInference) {
      this.inferDisplayNameMode();
      this.needsDisplayNameModeInference = false;
    }
  }

  get sortedModels() {
    return [...this.availableModels].sort((a, b) => {
      if (a.isRecommended && !b.isRecommended) return -1;
      if (!a.isRecommended && b.isRecommended) return 1;
      if (a.isRecommended && b.isRecommended) {
        return (a.recommendationRank || 0) - (b.recommendationRank || 0);
      }

      if (this.sortMode === 'created_desc') {
        if (b.createdAt !== a.createdAt) {
          return b.createdAt - a.createdAt;
        }
        return b.id.localeCompare(a.id, undefined, { numeric: true, sensitivity: 'base' });
      }
      if (this.sortMode === 'created_asc') {
        if (a.createdAt !== b.createdAt) {
          return a.createdAt - b.createdAt;
        }
        return a.id.localeCompare(b.id, undefined, { numeric: true, sensitivity: 'base' });
      }
      if (this.sortMode === 'alphabetical_desc') {
        return b.id.localeCompare(a.id, undefined, { numeric: true, sensitivity: 'base' });
      }
      return a.id.localeCompare(b.id, undefined, { numeric: true, sensitivity: 'base' });
    });
  }

  private setAllToCustom() {
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
    if (this.serviceTier) {
      this.pickerServiceTierSelect = 'custom';
      this.customServiceTier = this.serviceTier;
    }
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
      if (this.isKnownMode(this.initialData.displayNameMode)) {
        this.displayNameMode = this.initialData.displayNameMode;
        this.customDisplayName = this.displayNameMode === 'custom' ? (this.initialData.displayName || '') : '';
        this.needsDisplayNameModeInference = false;
      } else {
        this.needsDisplayNameModeInference = true;
      }
      this.modelId = this.initialData.modelId || '';
      this.thinkingLevel = this.initialData.thinkingLevel || '';
      this.reasoningMode = this.initialData.reasoningMode || '';
      this.reasoningSummary = this.initialData.reasoningSummary || '';
      this.serviceTier = this.initialData.serviceTier || '';
      this.maxInputTokens = this.initialData.maxInputTokens || null;
      this.maxOutputTokens = this.initialData.maxOutputTokens || null;
      
      if (this.isAdmin) {
        this.apiKey = ''; // Start blank for edit, user can update it
        this.hasApiKey = this.initialData.hasApiKey || false;
        this.editingApiKey = !this.hasApiKey;
        this.isEnabled = this.initialData.isEnabled ?? true;
        this.isSystemWide = this.initialData.isSystemWide ?? false;
        this.modelRole = this.initialData.modelRole ?? 3;
        this.parallelExecutionMode = this.initialData.parallelExecutionMode ?? 2;
        this.note = this.initialData.note || null;
      }

      this.pickerModelSelect = this.modelId;
      this.pickerThinkingLevelSelect = this.thinkingLevel;
      this.pickerReasoningModeSelect = this.reasoningMode;
      this.pickerReasoningSummarySelect = this.reasoningSummary;
      this.pickerServiceTierSelect = this.serviceTier;
      
      if (!this.isAdmin || this.hasApiKey) {
        this.fetchModels(true);
      } else {
        this.setAllToCustom();
        this.resolveDisplayNameModeIfNeeded();
      }
    } else {
      // Add mode
      if (!this.isAdmin) {
        this.fetchModels();
      }
    }
  }

  onProviderChange() {
    if (this.mode === 'edit') {
      return;
    }
    this.availableModels = [];
    this.selectedModelObj = null;
    this.modelError = '';
    
    this.thinkingLevel = '';
    this.customThinkingLevel = '';
    this.pickerThinkingLevelSelect = '';
    this.reasoningMode = '';
    this.customReasoningMode = '';
    this.pickerReasoningModeSelect = '';
    this.reasoningSummary = '';
    this.customReasoningSummary = '';
    this.pickerReasoningSummarySelect = '';
    this.serviceTier = '';
    this.customServiceTier = '';
    this.pickerServiceTierSelect = '';
    this.maxInputTokens = null;
    this.maxOutputTokens = null;
    this.customModelId = '';
    this.modelId = '';
    this.pickerModelSelect = '';
    this.displayNameMode = 'model_name';
    this.customDisplayName = '';

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
          if (isInitializingEdit) {
            this.setAllToCustom();
            this.resolveDisplayNameModeIfNeeded();
          }
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
           
           if (this.thinkingLevel && (!this.selectedModelObj || !this.selectedModelObj.supportedThinkingLevels?.includes(this.thinkingLevel))) {
              this.pickerThinkingLevelSelect = 'custom';
              this.customThinkingLevel = this.thinkingLevel;
           }
           if (this.reasoningMode && (!this.selectedModelObj || !this.selectedModelObj.supportedReasoningModes?.includes(this.reasoningMode))) {
              this.pickerReasoningModeSelect = 'custom';
              this.customReasoningMode = this.reasoningMode;
           }
           if (this.reasoningSummary && (!this.selectedModelObj || !this.selectedModelObj.supportedReasoningSummaries?.includes(this.reasoningSummary))) {
              this.pickerReasoningSummarySelect = 'custom';
              this.customReasoningSummary = this.reasoningSummary;
           }
           if (this.serviceTier) {
              const supported = (this.selectedModelObj?.supportedServiceTiers && this.selectedModelObj.supportedServiceTiers.length > 0)
                ? this.selectedModelObj.supportedServiceTiers
                : this.getProviderServiceTiers();
              if (!supported.includes(this.serviceTier)) {
                this.pickerServiceTierSelect = 'custom';
                this.customServiceTier = this.serviceTier;
              }
           }
           this.resolveDisplayNameModeIfNeeded();
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
          this.setAllToCustom();
          this.resolveDisplayNameModeIfNeeded();
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
    const isEditPreserveMode = this.mode === 'edit';

    if (isEditPreserveMode) {
      const currentThinking = this.pickerThinkingLevelSelect === 'custom' ? this.customThinkingLevel : this.thinkingLevel;
      const currentReasoningMode = this.pickerReasoningModeSelect === 'custom' ? this.customReasoningMode : this.reasoningMode;
      const currentReasoningSummary = this.pickerReasoningSummarySelect === 'custom' ? this.customReasoningSummary : this.reasoningSummary;
      const currentServiceTier = this.pickerServiceTierSelect === 'custom' ? this.customServiceTier : this.serviceTier;

      if (this.selectedModelObj) {
        // Thinking Level
        const supportedThinking = this.selectedModelObj.supportedThinkingLevels || [];
        if (!currentThinking) {
          this.thinkingLevel = '';
          this.pickerThinkingLevelSelect = '';
          this.customThinkingLevel = '';
        } else if (supportedThinking.includes(currentThinking)) {
          this.thinkingLevel = currentThinking;
          this.pickerThinkingLevelSelect = currentThinking;
          this.customThinkingLevel = '';
        } else {
          if (supportedThinking.length > 0) {
            if (this.selectedModelObj.recommendedThinkingLevel && supportedThinking.includes(this.selectedModelObj.recommendedThinkingLevel)) {
              this.thinkingLevel = this.selectedModelObj.recommendedThinkingLevel;
            } else {
              this.thinkingLevel = supportedThinking.includes('medium') ? 'medium' : supportedThinking[0];
            }
            this.pickerThinkingLevelSelect = this.thinkingLevel;
          } else {
            this.thinkingLevel = '';
            this.pickerThinkingLevelSelect = '';
          }
          this.customThinkingLevel = '';
        }

        // Reasoning Mode
        const supportedReasoningModes = this.selectedModelObj.supportedReasoningModes || [];
        if (!currentReasoningMode) {
          this.reasoningMode = '';
          this.pickerReasoningModeSelect = '';
          this.customReasoningMode = '';
        } else if (supportedReasoningModes.includes(currentReasoningMode)) {
          this.reasoningMode = currentReasoningMode;
          this.pickerReasoningModeSelect = currentReasoningMode;
          this.customReasoningMode = '';
        } else {
          if (supportedReasoningModes.length > 0) {
            this.reasoningMode = supportedReasoningModes.includes('medium') ? 'medium' : supportedReasoningModes[0];
            this.pickerReasoningModeSelect = this.reasoningMode;
          } else {
            this.reasoningMode = '';
            this.pickerReasoningModeSelect = '';
          }
          this.customReasoningMode = '';
        }

        // Reasoning Summary
        const supportedReasoningSummaries = this.selectedModelObj.supportedReasoningSummaries || [];
        if (!currentReasoningSummary) {
          this.reasoningSummary = '';
          this.pickerReasoningSummarySelect = '';
          this.customReasoningSummary = '';
        } else if (supportedReasoningSummaries.includes(currentReasoningSummary)) {
          this.reasoningSummary = currentReasoningSummary;
          this.pickerReasoningSummarySelect = currentReasoningSummary;
          this.customReasoningSummary = '';
        } else {
          if (supportedReasoningSummaries.length > 0) {
            this.reasoningSummary = supportedReasoningSummaries.includes('auto') ? 'auto' : supportedReasoningSummaries[0];
            this.pickerReasoningSummarySelect = this.reasoningSummary;
          } else {
            this.reasoningSummary = '';
            this.pickerReasoningSummarySelect = '';
          }
          this.customReasoningSummary = '';
        }

        // Service Tier
        const supportedTiers = this.selectedModelObj.supportedServiceTiers || this.getProviderServiceTiers();
        if (!currentServiceTier || supportedTiers.includes(currentServiceTier)) {
          this.serviceTier = currentServiceTier;
          this.pickerServiceTierSelect = currentServiceTier;
          this.customServiceTier = '';
        } else {
          this.serviceTier = '';
          this.pickerServiceTierSelect = '';
          this.customServiceTier = '';
        }

        // Token Clamping
        if (this.maxInputTokens !== null && typeof this.selectedModelObj.maxInputTokens === 'number') {
          if (this.maxInputTokens > this.selectedModelObj.maxInputTokens) {
            this.maxInputTokens = this.selectedModelObj.maxInputTokens;
          }
        }
        if (this.maxOutputTokens !== null && typeof this.selectedModelObj.maxOutputTokens === 'number') {
          if (this.maxOutputTokens > this.selectedModelObj.maxOutputTokens) {
            this.maxOutputTokens = this.selectedModelObj.maxOutputTokens;
          }
        }
      } else {
        // "Custom..." model selected: preserve current values into custom fields
        this.thinkingLevel = '';
        this.pickerThinkingLevelSelect = currentThinking ? 'custom' : '';
        this.customThinkingLevel = currentThinking;

        this.reasoningMode = '';
        this.pickerReasoningModeSelect = currentReasoningMode ? 'custom' : '';
        this.customReasoningMode = currentReasoningMode;

        this.reasoningSummary = '';
        this.pickerReasoningSummarySelect = currentReasoningSummary ? 'custom' : '';
        this.customReasoningSummary = currentReasoningSummary;

        this.serviceTier = '';
        this.pickerServiceTierSelect = currentServiceTier ? 'custom' : '';
        this.customServiceTier = currentServiceTier;
      }
    } else {
      // Add mode or after provider change (fresh defaults)
      if (this.selectedModelObj) {
        if (this.selectedModelObj.supportedThinkingLevels && this.selectedModelObj.supportedThinkingLevels.length > 0) {
          if (this.selectedModelObj.recommendedThinkingLevel && this.selectedModelObj.supportedThinkingLevels.includes(this.selectedModelObj.recommendedThinkingLevel)) {
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
        
        this.serviceTier = '';
        this.pickerServiceTierSelect = '';
        
        this.maxInputTokens = this.selectedModelObj.maxInputTokens || null;
        this.maxOutputTokens = this.selectedModelObj.maxOutputTokens || null;
      } else {
        this.thinkingLevel = '';
        this.pickerThinkingLevelSelect = '';
        this.reasoningMode = '';
        this.pickerReasoningModeSelect = '';
        this.reasoningSummary = '';
        this.pickerReasoningSummarySelect = '';
        this.serviceTier = '';
        this.pickerServiceTierSelect = '';
      }
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

  onPickerServiceTierChange() {
    if (this.pickerServiceTierSelect !== 'custom') {
      this.serviceTier = this.pickerServiceTierSelect;
    } else {
      this.serviceTier = ''; 
    }
  }

  getProviderServiceTiers(): string[] {
    if (this.provider === 'OpenAI') {
      return ['auto', 'default', 'flex', 'priority', 'fast'];
    } else if (this.provider === 'Anthropic') {
      return ['auto', 'standard_only'];
    } else if (this.provider === 'Google') {
      return ['priority', 'flex', 'standard', 'deferred'];
    }
    return [];
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

  formatServiceTier(tier: string): string {
    if (!tier) return 'None (Default)';
    if (tier.toLowerCase() === 'standard_only') return 'Standard Only';
    return tier.charAt(0).toUpperCase() + tier.slice(1);
  }

  toggleApiKeyInfo() {
    this.showApiKeyInfo = !this.showApiKeyInfo;
  }

  onCancel() {
    this.cancel.emit();
  }

  onSave() {
    this.modelError = '';
    this.resolveDisplayNameModeIfNeeded();
    const finalModelId = this.pickerModelSelect === 'custom' ? this.customModelId : this.modelId;
    const finalThinkingLevel = this.pickerThinkingLevelSelect === 'custom' ? this.customThinkingLevel : this.thinkingLevel;
    const finalReasoningMode = this.pickerReasoningModeSelect === 'custom' ? this.customReasoningMode : this.reasoningMode;
    const finalReasoningSummary = this.pickerReasoningSummarySelect === 'custom' ? this.customReasoningSummary : this.reasoningSummary;
    const finalServiceTier = this.pickerServiceTierSelect === 'custom' ? this.customServiceTier : this.serviceTier;
    
    if (!finalModelId) {
        this.modelError = 'Model is required.';
        return;
    }

    const finalDisplayName = this.getEffectiveDisplayName();

    if (!finalDisplayName) {
      this.modelError = 'Display Name could not be resolved. Choose Custom and enter a name.';
      return;
    }

    if (this.isAdmin) {
      if (!/^[a-zA-Z0-9 _\-.]+$/.test(finalDisplayName)) {
        this.modelError = this.displayNameMode === 'custom'
          ? 'Display Name can only contain letters, numbers, spaces, underscores, dashes, and dots.'
          : `The resolved name "${finalDisplayName}" contains characters that are not allowed. Choose Custom and enter a name.`;
        return;
      }
    }

    const result: AiModelFormResult = {
      displayName: finalDisplayName,
      displayNameMode: this.displayNameMode,
      provider: this.provider,
      modelId: finalModelId,
      thinkingLevel: finalThinkingLevel || null,
      reasoningMode: finalReasoningMode || null,
      reasoningSummary: finalReasoningSummary || null,
      serviceTier: finalServiceTier || null,
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
      result.parallelExecutionMode = this.parallelExecutionMode;
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
