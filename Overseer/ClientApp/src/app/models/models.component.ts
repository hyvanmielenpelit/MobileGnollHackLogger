import { Component, OnInit, inject, ViewChild, ElementRef, ChangeDetectionStrategy, Injector, afterNextRender } from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterModule } from '@angular/router';
import {
  SettingsService,
  UserAiModel,
  ApiModelDto,
  PosturePrivacyLevel,
  confidentialityPostureLabel,
  confidentialityPostureRank,
  posturePrivacyLevel
} from '../services/settings.service';
import { AiModelFormComponent, AiModelFormResult } from '../shared/ai-model-form/ai-model-form.component';
import { ProviderBadgeComponent } from '../shared/provider-badge/provider-badge.component';
import { ModelPickerComponent, ModelPickerKey, ModelPickerOption, toModelPickerOptions } from '../shared/model-picker/model-picker.component';
import { ensureOverlayPolyfills, refreshAnchorPositioning } from '../utils/polyfills.util';
import {
  AvailabilityChip,
  ModelResolutionResult,
  availabilityChip,
  availabilitySentence,
  isResolutionDeletion,
  needsAttention
} from '../shared/model-availability/model-availability';
import { ModelAvailabilityNoticeComponent } from '../shared/model-availability/model-availability-notice.component';
import { ModelResolutionDialogComponent } from '../shared/model-resolution-dialog/model-resolution-dialog.component';

/** The extra sentence under a system-provided model that needs attention. */
export const SYSTEM_MODEL_ATTENTION_TEXT = 'An administrator needs to update this model. You can choose another model in chat.';

@Component({
    selector: 'app-models',
    imports: [
      NgTemplateOutlet, FormsModule, RouterModule, AiModelFormComponent, ProviderBadgeComponent, ModelPickerComponent,
      ModelAvailabilityNoticeComponent, ModelResolutionDialogComponent
    ],
    templateUrl: './models.component.html',
    changeDetection: ChangeDetectionStrategy.Eager,
    styleUrl: './models.component.scss'
})
export class ModelsComponent implements OnInit {
  settingsService = inject(SettingsService);
  private readonly injector = inject(Injector);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);

  @ViewChild('modelPickerDialog') modelPickerDialog!: ElementRef<HTMLDialogElement>;
  @ViewChild('editModelDialog') editModelDialog!: ElementRef<HTMLDialogElement>;
  @ViewChild('deleteModelConfirmDialog') deleteModelConfirmDialog!: ElementRef<HTMLDialogElement>;
  @ViewChild(ModelResolutionDialogComponent) resolutionDialog?: ModelResolutionDialogComponent;

  readonly systemModelAttentionText = SYSTEM_MODEL_ATTENTION_TEXT;

  /** The user model the resolution dialog was opened for. */
  resolvingModel: UserAiModel | null = null;
  /** The page status line: what the last resolution did. */
  resolutionStatus = '';

  userModels: UserAiModel[] = [];
  systemModels: UserAiModel[] = [];
  titleUserModels: UserAiModel[] = [];
  titleSystemModels: UserAiModel[] = [];
  loading = false;
  saving = false;
  titleModelSelection: string | null = null;
  savingTitleModel = false;
  savedTitleModelSuccess = false;
  titleGenerationEnabled = true;
  
  // Model Picker State
  providers = ['OpenAI', 'Anthropic', 'Google'];
  pickerProvider = 'OpenAI';
  isAddingModel = false;
  
  // Edit State
  editingModel: UserAiModel | null = null;
  editFormData: any = null;
  modelToDeleteId: number | undefined = undefined;

  ngOnInit() {
    ensureOverlayPolyfills();
    this.settingsService.getSettings().subscribe({
      next: (settings) => {
        this.titleGenerationEnabled = !settings.titleGenerationDisabled;
        if (settings.titleGenerationModelId) {
          this.titleModelSelection = 'u_' + settings.titleGenerationModelId;
        } else if (settings.titleGenerationSystemModelId) {
          this.titleModelSelection = 's_' + settings.titleGenerationSystemModelId;
        } else {
          this.titleModelSelection = null;
        }
        if (settings.configuredProviders && settings.configuredProviders.length > 0) {
          this.providers = settings.configuredProviders;
          this.pickerProvider = this.providers[0];
        } else {
          this.providers = [];
        }
        this.loadModels();
      },
      error: () => {
        this.loadModels();
      }
    });
  }

  /** `afterLoad` runs once the lists are replaced, before they render. */
  loadModels(afterLoad?: () => void) {
    this.loading = true;
    this.settingsService.getUserModels().subscribe({
      next: (models) => {
        this.userModels = models.filter(m => !m.isSystem && (m.modelRole === undefined || (m.modelRole & 1) === 1));
        this.systemModels = models.filter(m => m.isSystem && (m.modelRole === undefined || (m.modelRole & 1) === 1));
        this.titleUserModels = models.filter(m => !m.isSystem && (m.modelRole === undefined || (m.modelRole & 2) === 2));
        this.titleSystemModels = models.filter(m => m.isSystem && (m.modelRole === undefined || (m.modelRole & 2) === 2));
        this.loading = false;
        // The anchor-positioning polyfill does not observe DOM mutations, and the trust
        // indicators' tooltips were behind the loading @if until this render.
        setTimeout(() => refreshAnchorPositioning(), 0);
        afterLoad?.();
      },
      error: (err) => {
        console.error("Failed to load models", err);
        this.loading = false;
      }
    });
  }

  deleteModel(id: number | undefined) {
    if (!id) return;
    this.modelToDeleteId = id;
    this.deleteModelConfirmDialog.nativeElement.showModal();
  }

  confirmDelete() {
    const id = this.modelToDeleteId;
    if (!id) return;
    this.saving = true;
    this.settingsService.deleteUserModel(id).subscribe({
      next: () => {
        if (this.titleModelSelection === 'u_' + id) {
          this.titleModelSelection = null;
        }
        this.loadModels();
        this.reloadTitleSelection();
        this.saving = false;
        this.deleteModelConfirmDialog.nativeElement.close();
      },
      error: (err) => {
        console.error("Failed to delete model", err);
        this.saving = false;
        this.deleteModelConfirmDialog.nativeElement.close();
      }
    });
  }

  /** Re-reads the title-generation model, which the server moves off a deleted model. */
  private reloadTitleSelection(): void {
    this.settingsService.getSettings().subscribe({
      next: (settings) => {
        if (settings.titleGenerationModelId) {
          this.titleModelSelection = 'u_' + settings.titleGenerationModelId;
        } else if (settings.titleGenerationSystemModelId) {
          this.titleModelSelection = 's_' + settings.titleGenerationSystemModelId;
        } else {
          this.titleModelSelection = null;
        }
      },
      error: () => {}
    });
  }

  // --- Catalog availability ---

  modelName(model: UserAiModel): string {
    return model.displayName || model.modelId;
  }

  needsAttention(model: UserAiModel): boolean {
    return needsAttention(model.modelAvailability);
  }

  /** A model saved in custom mode: Overseer no longer checks it against the catalog. */
  isCustomMode(model: UserAiModel): boolean {
    return model.modelAvailability?.status === 'custom';
  }

  availabilityChip(model: UserAiModel): AvailabilityChip | null {
    return availabilityChip(model.modelAvailability);
  }

  availabilitySentence(model: UserAiModel): string {
    return availabilitySentence(model.modelAvailability, this.modelName(model), model.modelId);
  }

  /** Unique per row across both lists, like `postureTipId`. */
  availabilityTipId(model: UserAiModel): string {
    return `tip-availability-${model.isSystem ? 's' : 'u'}-${model.id}`;
  }

  resolveLabel(model: UserAiModel): string {
    return `Resolve "${this.modelName(model)}"`;
  }

  openResolve(model: UserAiModel): void {
    if (model.id == null || !model.modelAvailability) return;
    this.resolvingModel = model;
    this.resolutionDialog?.open({
      id: model.id,
      provider: model.provider,
      modelId: model.modelId,
      displayName: this.modelName(model),
      availability: model.modelAvailability
    });
  }

  /**
   * Reloads the list, says what changed in the status line, and moves focus to the next flagged
   * row's Resolve…, else the resolved row's name, else the page heading.
   */
  onResolved(result: ModelResolutionResult): void {
    const subject = this.resolvingModel;
    this.resolvingModel = null;
    if (!subject) {
      this.loadModels();
      return;
    }
    const name = this.modelName(subject);
    const index = this.userModels.findIndex(m => m.id === subject.id);
    const deleted = isResolutionDeletion(result);
    const resolved = result.model as UserAiModel | null;

    if (deleted) {
      this.resolutionStatus = `'${name}' was deleted.`;
    } else if (resolved && (resolved.modelCatalogMode === 'custom' || resolved.modelAvailability?.status === 'custom')) {
      this.resolutionStatus = `'${name}' is now a custom model.`;
    } else {
      const target = resolved
        ? (resolved.displayNameMode === 'custom' ? resolved.modelId : (resolved.displayName || resolved.modelId))
        : '';
      this.resolutionStatus = target ? `'${name}' now uses ${target}.` : `'${name}' was updated.`;
    }

    this.loadModels(() => this.focusAfterResolution(subject.id!, index < 0 ? 0 : index));
    if (deleted && this.titleModelSelection === 'u_' + subject.id) {
      this.titleModelSelection = null;
      this.reloadTitleSelection();
    }
  }

  private focusAfterResolution(resolvedId: number, formerIndex: number): void {
    const flagged = (m: UserAiModel) => m.id !== resolvedId && this.needsAttention(m);
    const next = this.userModels.slice(formerIndex).find(flagged) ?? this.userModels.slice(0, formerIndex).find(flagged);
    const selector = next
      ? `#resolve-model-${next.id}`
      : this.userModels.some(m => m.id === resolvedId) ? `#user-model-name-${resolvedId}` : '#models-heading';
    afterNextRender(() => {
      this.host.nativeElement.querySelector<HTMLElement>(selector)?.focus();
    }, { injector: this.injector });
  }

  moveUp(index: number) {
    if (index > 0) {
      const temp = this.userModels[index];
      this.userModels[index] = this.userModels[index - 1];
      this.userModels[index - 1] = temp;
      this.saveOrder();
    }
  }

  moveDown(index: number) {
    if (index < this.userModels.length - 1) {
      const temp = this.userModels[index];
      this.userModels[index] = this.userModels[index + 1];
      this.userModels[index + 1] = temp;
      this.saveOrder();
    }
  }

  onDragStart(event: DragEvent, index: number, type: 'user' | 'system' = 'user') {
    if (event.dataTransfer) {
      event.dataTransfer.setData('text/plain', JSON.stringify({ index, type }));
      event.dataTransfer.effectAllowed = 'move';
      const target = event.target as HTMLElement;
      setTimeout(() => target.classList.add('dragging'), 0);
    }
  }

  onDragEnd(event: DragEvent) {
    const target = event.target as HTMLElement;
    target.classList.remove('dragging');
    const items = document.querySelectorAll('.model-item');
    items.forEach(item => item.classList.remove('drag-over', 'drag-over-top', 'drag-over-bottom'));
  }

  onDragOver(event: DragEvent) {
    event.preventDefault();
    if (event.dataTransfer) {
      event.dataTransfer.dropEffect = 'move';
    }
    const targetItem = (event.target as HTMLElement).closest('.model-item');
    if (targetItem) {
      const rect = targetItem.getBoundingClientRect();
      const midY = rect.top + rect.height / 2;
      targetItem.classList.remove('drag-over-top', 'drag-over-bottom');
      if (event.clientY < midY) {
        targetItem.classList.add('drag-over-top');
      } else {
        targetItem.classList.add('drag-over-bottom');
      }
    }
  }

  onDragLeave(event: DragEvent) {
    const targetItem = (event.target as HTMLElement).closest('.model-item');
    if (targetItem) {
      targetItem.classList.remove('drag-over-top', 'drag-over-bottom');
    }
  }

  onDrop(event: DragEvent, dropIndex: number, type: 'user' | 'system' = 'user') {
    event.preventDefault();
    const targetItem = (event.target as HTMLElement).closest('.model-item, .system-model-item');
    if (targetItem) {
      targetItem.classList.remove('drag-over-top', 'drag-over-bottom');
    }
    
    if (event.dataTransfer) {
      const dataStr = event.dataTransfer.getData('text/plain');
      if (dataStr) {
        try {
          const data = JSON.parse(dataStr);
          if (data.type !== type) return; // Prevent cross-list dragging
          
          const dragIndex = data.index;
          if (dragIndex !== dropIndex) {
          const isUser = type === 'user';
          const list = isUser ? this.userModels : this.systemModels;
          const item = list[dragIndex];
          list.splice(dragIndex, 1);
          
          // Determine if we drop before or after based on the mouse position relative to the element
          let insertIndex = dropIndex;
          if (targetItem) {
             const rect = targetItem.getBoundingClientRect();
             const midY = rect.top + rect.height / 2;
             if (event.clientY >= midY) {
               insertIndex++; // Insert after if dropped on the bottom half
             }
             if (dragIndex < dropIndex && event.clientY < midY) {
                // Adjustment if dragging downwards but dropping on top half
             } else if (dragIndex < dropIndex) {
               insertIndex--; // Adjust because we removed an item before it
             }
          }
          
          list.splice(insertIndex, 0, item as any);
          if (isUser) {
            this.saveOrder();
          } else {
            this.saveSystemOrder();
          }
        }
        } catch (e) {
          console.error("Invalid drag data", e);
        }
      }
    }
  }

  saveOrder() {
    this.saving = true;
    const orderedIds = this.userModels.map(m => m.id!);
    this.settingsService.reorderUserModels(orderedIds).subscribe({
      next: () => {
        this.saving = false;
      },
      error: (err) => {
        console.error("Failed to save order", err);
        this.saving = false;
      }
    });
  }

  saveSystemOrder() {
    this.saving = true;
    const orderedIds = this.systemModels.map(m => m.id!);
    this.settingsService.reorderSystemModels(orderedIds).subscribe({
      next: () => {
        this.saving = false;
      },
      error: (err) => {
        console.error("Failed to save system order", err);
        this.saving = false;
      }
    });
  }

  resetSystemOrder() {
    this.saving = true;
    this.settingsService.resetSystemModelsOrder().subscribe({
      next: () => {
        this.loadModels();
        this.saving = false;
      },
      error: (err) => {
        console.error("Failed to reset system order", err);
        this.saving = false;
      }
    });
  }

  toggleTitleGeneration() {
    this.savingTitleModel = true;
    this.savedTitleModelSuccess = false;
    const disabled = !this.titleGenerationEnabled;
    
    this.settingsService.saveTitleGenerationModel(null, false, disabled).subscribe({
      next: () => {
        this.savingTitleModel = false;
        this.savedTitleModelSuccess = true;
        setTimeout(() => this.savedTitleModelSuccess = false, 3000);
      },
      error: (err) => {
        this.titleGenerationEnabled = !this.titleGenerationEnabled; // revert on error
        this.savingTitleModel = false;
        console.error("Failed to save title generation toggle", err);
      }
    });
  }

  selectTitleModel(id: number | null, isSystem: boolean) {
    if (id === null) {
      this.titleModelSelection = null;
    } else if (isSystem) {
      this.titleModelSelection = 's_' + id;
    } else {
      this.titleModelSelection = 'u_' + id;
    }
    
    this.savingTitleModel = true;
    this.savedTitleModelSuccess = false;
    
    this.settingsService.saveTitleGenerationModel(id, isSystem).subscribe({
      next: () => {
        this.savingTitleModel = false;
        this.savedTitleModelSuccess = true;
        setTimeout(() => {
          this.savedTitleModelSuccess = false;
        }, 3000);
      },
      error: (err) => {
        this.savingTitleModel = false;
        console.error("Failed to save title generation model", err);
      }
    });
  }

  /** `'u_<id>'` or `'s_<id>'`; `null` is the Default (First Available) option. */
  selectTitleModelByKey(key: ModelPickerKey | null): void {
    if (key === null) {
      this.selectTitleModel(null, false);
      return;
    }
    const text = String(key);
    const id = Number(text.substring(2));
    if (Number.isNaN(id)) return;
    if (text.startsWith('s_')) {
      this.selectTitleModel(id, true);
    } else if (text.startsWith('u_')) {
      this.selectTitleModel(id, false);
    }
  }

  private titleOptionsUserSource: UserAiModel[] | null = null;
  private titleOptionsSystemSource: UserAiModel[] | null = null;
  private titleOptionsCache: ModelPickerOption<UserAiModel>[] = [];

  /** Memoized on the two source arrays, which are only ever reassigned, never mutated. */
  get titleModelOptions(): ModelPickerOption<UserAiModel>[] {
    if (this.titleOptionsUserSource !== this.titleUserModels || this.titleOptionsSystemSource !== this.titleSystemModels) {
      this.titleOptionsUserSource = this.titleUserModels;
      this.titleOptionsSystemSource = this.titleSystemModels;
      this.titleOptionsCache = [
        ...toModelPickerOptions(this.titleUserModels, 'Your Models', 'u_'),
        ...toModelPickerOptions(this.titleSystemModels, 'System Models', 's_')
      ];
    }
    return this.titleOptionsCache;
  }

  openModelPicker() {
    this.pickerProvider = this.providers.length > 0 ? this.providers[0] : 'OpenAI';
    this.isAddingModel = true;
    this.modelPickerDialog?.nativeElement.showModal();
  }

  closeModelPicker() {
    this.modelPickerDialog?.nativeElement.close();
    this.isAddingModel = false;
  }

  onAddModel(formData: AiModelFormResult) {
    this.saving = true;
    const hasPricing = formData.pricingMode !== undefined ||
      formData.inputPricePerMillion !== undefined ||
      formData.outputPricePerMillion !== undefined ||
      formData.cachedInputPricePerMillion !== undefined;

    const addCall$ = hasPricing
      ? this.settingsService.addUserModel(
          formData.provider, 
          formData.modelId, 
          formData.displayName, 
          formData.displayNameMode,
          formData.thinkingLevel || undefined,
          formData.reasoningMode || undefined,
          formData.reasoningSummary || undefined,
          formData.serviceTier || undefined,
          formData.maxInputTokens, 
          formData.maxOutputTokens,
          formData.pricingMode,
          formData.inputPricePerMillion,
          formData.outputPricePerMillion,
          formData.cachedInputPricePerMillion
        )
      : this.settingsService.addUserModel(
          formData.provider, 
          formData.modelId, 
          formData.displayName, 
          formData.displayNameMode,
          formData.thinkingLevel || undefined,
          formData.reasoningMode || undefined,
          formData.reasoningSummary || undefined,
          formData.serviceTier || undefined,
          formData.maxInputTokens, 
          formData.maxOutputTokens
        );

    addCall$.subscribe({
      next: () => {
        this.loadModels();
        this.closeModelPicker();
        this.saving = false;
      },
      error: (err) => {
        console.error("Failed to add model", err);
        this.saving = false;
        alert(err.error?.message || 'Error adding model');
      }
    });
  }

  openEdit(model: UserAiModel) {
    this.editingModel = Object.assign({}, model);
    this.editFormData = {
      ...model,
      pricingMode: model.pricingMode,
      inputPricePerMillion: model.inputPricePerMillion,
      outputPricePerMillion: model.outputPricePerMillion,
      cachedInputPricePerMillion: model.cachedInputPricePerMillion
    };
    this.editModelDialog?.nativeElement.showModal();
  }

  closeEdit() {
    this.editModelDialog?.nativeElement.close();
    this.editingModel = null;
    this.editFormData = null;
  }

  onEditSave(formData: AiModelFormResult) {
    if (this.editingModel && this.editingModel.id) {
      this.saving = true;
      const hasPricing = formData.pricingMode !== undefined ||
        formData.inputPricePerMillion !== undefined ||
        formData.outputPricePerMillion !== undefined ||
        formData.cachedInputPricePerMillion !== undefined;

      const updateCall$ = hasPricing
        ? this.settingsService.updateUserModel(
            this.editingModel.id, 
            formData.displayName, 
            formData.displayNameMode,
            formData.thinkingLevel || undefined, 
            formData.reasoningMode || undefined,
            formData.reasoningSummary || undefined,
            formData.serviceTier || undefined,
            formData.maxInputTokens, 
            formData.maxOutputTokens,
            formData.modelId,
            formData.provider,
            formData.pricingMode,
            formData.inputPricePerMillion,
            formData.outputPricePerMillion,
            formData.cachedInputPricePerMillion
          )
        : this.settingsService.updateUserModel(
            this.editingModel.id, 
            formData.displayName, 
            formData.displayNameMode,
            formData.thinkingLevel || undefined, 
            formData.reasoningMode || undefined,
            formData.reasoningSummary || undefined,
            formData.serviceTier || undefined,
            formData.maxInputTokens, 
            formData.maxOutputTokens,
            formData.modelId,
            formData.provider
          );

      updateCall$.subscribe({
        next: () => {
          this.loadModels();
          this.closeEdit();
          this.saving = false;
        },
        error: (err) => {
          console.error("Failed to update model", err);
          this.saving = false;
          alert(err.error?.message || 'Error updating model');
        }
      });
    }
  }

  onSaveModel(formData: AiModelFormResult) {
    if (this.editingModel?.id) {
      this.onEditSave(formData);
    } else {
      this.onAddModel(formData);
    }
  }

  formatThinkingLevel(level: string | undefined): string {
    if (!level) return 'Default';
    return level.charAt(0).toUpperCase() + level.slice(1);
  }

  formatServiceTier(tier: string | null | undefined): string {
    if (!tier) return 'None';
    if (tier.toLowerCase() === 'standard_only') return 'Standard Only';
    return tier.charAt(0).toUpperCase() + tier.slice(1);
  }

  formatReasoningSummary(level: string | null | undefined, provider?: string): string {
    if (!level) {
      if (provider === 'Anthropic') {
        return 'Default';
      }
      return 'None';
    }
    return level.charAt(0).toUpperCase() + level.slice(1);
  }

  private formatRate(val: number): string {
    const formatted = new Intl.NumberFormat('en-US', {
      minimumFractionDigits: 2,
      maximumFractionDigits: 6
    }).format(val);
    return `$${formatted}`;
  }

  formatPrice(model: UserAiModel): string {
    const input = model.effectiveInputPricePerMillion ?? (model.pricingMode === 'custom' ? model.inputPricePerMillion : null);
    const output = model.effectiveOutputPricePerMillion ?? (model.pricingMode === 'custom' ? model.outputPricePerMillion : null);
    const cached = model.effectiveCachedInputPricePerMillion ?? (model.pricingMode === 'custom' ? model.cachedInputPricePerMillion : null);

    if (input == null || output == null) {
      return '';
    }

    const fmt = (val: number) => this.formatRate(val);

    let result = `${fmt(input)} in / ${fmt(output)} out`;
    if (cached != null) {
      result += ` / ${fmt(cached)} cached`;
    }
    result += ' per 1M';
    return result;
  }

  /**
   * The model's long-prompt rate card, shown beside the base price. Empty for a flat-rate model — which
   * is every Anthropic model, every Gemini Flash model, and every custom price override.
   */
  formatLongContextPrice(model: UserAiModel): string {
    const threshold = model.effectiveLongContextThresholdTokens;
    const input = model.effectiveLongContextInputPricePerMillion;
    const output = model.effectiveLongContextOutputPricePerMillion;
    if (threshold == null || input == null || output == null) {
      return '';
    }
    const tokens = new Intl.NumberFormat('en-US').format(threshold);
    return `Prompts over ${tokens} tokens: ${this.formatRate(input)} in / ${this.formatRate(output)} out per 1M`;
  }

  /**
   * A quiet note about an announced future price change, or — once its date has passed — the advisory
   * that the base rates already carry it. The cost is correct either way; the advisory exists so the
   * catalog does not silently turn into a changelog of elapsed schedules.
   */
  formatPricingSchedule(model: UserAiModel): string {
    if (model.pricingScheduleElapsed) {
      return 'A scheduled price change is in effect — fold it into the base rates and re-verify.';
    }
    if (!model.pricingScheduledChangeFrom) {
      return '';
    }
    const input = model.pricingScheduledChangeInputPricePerMillion;
    const output = model.pricingScheduledChangeOutputPricePerMillion;
    const change = (input != null && output != null)
      ? `Price changes to ${this.formatRate(input)} in / ${this.formatRate(output)} out per 1M on ${model.pricingScheduledChangeFrom}.`
      : `Price changes on ${model.pricingScheduledChangeFrom}.`;
    return model.pricingScheduledChangeNote ? `${change} ${model.pricingScheduledChangeNote}` : change;
  }

  getPricingBadge(model: UserAiModel): 'Custom' | 'Catalog' {
    return (model.pricingSource === 'custom' || model.pricingMode === 'custom') ? 'Custom' : 'Catalog';
  }

  /** False for a legacy row and for anything still on the Unknown rung. */
  hasEstablishedPosture(model: UserAiModel): boolean {
    return confidentialityPostureRank(model.confidentialityPosture) > 0;
  }

  postureLevel(model: UserAiModel): PosturePrivacyLevel {
    return posturePrivacyLevel(model.confidentialityPosture, model.postureVerifiedUtc);
  }

  /**
   * The indicator's own text. It carries the verification status in words as well as in colour,
   * and a posture nobody dated never reads as verified — a user's own key is self-declared, an
   * undated system configuration is merely unverified.
   */
  postureBadgeText(model: UserAiModel): string {
    if (!this.hasEstablishedPosture(model)) {
      return 'Trust: not established';
    }
    const label = confidentialityPostureLabel(model.confidentialityPosture);
    if (model.postureVerifiedUtc) {
      return `${label} · Verified`;
    }
    return `${label} · ${model.isSystem ? 'Unverified' : 'Self-declared'}`;
  }

  postureTooltipLines(model: UserAiModel): string[] {
    const lines = [confidentialityPostureLabel(model.confidentialityPosture)];
    if (model.postureVerifiedUtc) {
      const date = this.formatPostureDate(model.postureVerifiedUtc);
      lines.push(date ? `Operator-verified on ${date}.` : 'Operator-verified.');
    } else if (model.isSystem) {
      lines.push('No operator has verified this against the provider agreement.');
    } else {
      lines.push('Declared by you for your own API key. Overseer cannot verify it.');
    }
    if (model.dataRegion) {
      lines.push(`Inference runs in ${model.dataRegion}.`);
    }
    return lines;
  }

  /** Unique per row across both lists: user model ids and system config ids can collide. */
  postureTipId(model: UserAiModel): string {
    return `tip-posture-${model.isSystem ? 's' : 'u'}-${model.id}`;
  }

  private formatPostureDate(iso: string | null | undefined): string {
    if (!iso) return '';
    const parsed = new Date(iso);
    return isNaN(parsed.getTime()) ? '' : parsed.toLocaleDateString();
  }
}
