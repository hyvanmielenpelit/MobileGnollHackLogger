import { Component, OnInit, inject, ViewChild, ElementRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { SettingsService, UserAiModel, ApiModelDto } from '../services/settings.service';

@Component({
  selector: 'app-models',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './models.component.html',
  styleUrl: './models.component.scss'
})
export class ModelsComponent implements OnInit {
  settingsService = inject(SettingsService);

  @ViewChild('modelPickerDialog') modelPickerDialog!: ElementRef<HTMLDialogElement>;
  @ViewChild('editModelDialog') editModelDialog!: ElementRef<HTMLDialogElement>;

  userModels: UserAiModel[] = [];
  loading = false;
  saving = false;
  
  // Model Picker State
  providers = ['OpenAI', 'Anthropic', 'Google'];
  pickerProvider = 'OpenAI';
  loadingModels = false;
  availableModels: ApiModelDto[] = [];
  selectedModelId = '';
  selectedModelObj: ApiModelDto | null = null;
  pickerThinkingLevel = '';
  modelError = '';
  sortMode: 'alphabetical' | 'newest' = 'alphabetical';
  
  // Edit State
  editingModel: UserAiModel | null = null;
  editDisplayName = '';
  editThinkingLevel = '';
  editThinkingLevelSelect = '';

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
    this.loadModels();
  }

  loadModels() {
    this.loading = true;
    this.settingsService.getUserModels().subscribe({
      next: (models) => {
        this.userModels = models;
        this.loading = false;
      },
      error: (err) => {
        console.error("Failed to load models", err);
        this.loading = false;
      }
    });
  }

  deleteModel(id: number | undefined) {
    if (!id) return;
    if (confirm("Are you sure you want to remove this model from your list?")) {
      this.saving = true;
      this.settingsService.deleteUserModel(id).subscribe({
        next: () => {
          this.loadModels();
          this.saving = false;
        },
        error: (err) => {
          console.error("Failed to delete model", err);
          this.saving = false;
        }
      });
    }
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

  onDragStart(event: DragEvent, index: number) {
    if (event.dataTransfer) {
      event.dataTransfer.setData('text/plain', index.toString());
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

  onDrop(event: DragEvent, dropIndex: number) {
    event.preventDefault();
    const targetItem = (event.target as HTMLElement).closest('.model-item');
    if (targetItem) {
      targetItem.classList.remove('drag-over-top', 'drag-over-bottom');
    }
    
    if (event.dataTransfer) {
      const dragIndexStr = event.dataTransfer.getData('text/plain');
      if (dragIndexStr !== undefined && dragIndexStr !== '') {
        const dragIndex = parseInt(dragIndexStr, 10);
        if (dragIndex !== dropIndex) {
          const item = this.userModels[dragIndex];
          this.userModels.splice(dragIndex, 1);
          
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
          
          this.userModels.splice(insertIndex, 0, item);
          this.saveOrder();
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

  openModelPicker() {
    this.pickerProvider = 'OpenAI';
    this.checkModelsForPicker();
    this.modelPickerDialog?.nativeElement.showModal();
  }

  closeModelPicker() {
    this.modelPickerDialog?.nativeElement.close();
  }

  onProviderChange() {
    this.checkModelsForPicker();
  }

  checkModelsForPicker() {
    this.loadingModels = true;
    this.modelError = '';
    this.availableModels = [];
    this.selectedModelId = '';
    this.selectedModelObj = null;
    this.pickerThinkingLevel = '';

    // fetch api keys to see if we have one for this provider. Or we can just send empty API key and let backend use saved.
    this.settingsService.getAvailableModels(this.pickerProvider, '').subscribe({
      next: (models) => {
        this.availableModels = models;
        this.loadingModels = false;
        if (this.availableModels.length === 0) {
          this.modelError = 'No models available or API key not configured.';
          return;
        }

        if (this.sortedModels.length > 0) {
          this.selectedModelId = this.sortedModels[0].id;
          this.onPickerModelSelect();
        }
      },
      error: (err) => {
        this.loadingModels = false;
        this.modelError = err.error?.message || err.message || 'Error fetching models.';
      }
    });
  }

  onPickerModelSelect() {
    this.selectedModelObj = this.availableModels.find(m => m.id === this.selectedModelId) || null;
    if (this.selectedModelObj && this.selectedModelObj.supportedThinkingLevels && this.selectedModelObj.supportedThinkingLevels.length > 0) {
      if (this.selectedModelObj.isRecommended && this.selectedModelObj.recommendedThinkingLevel && this.selectedModelObj.supportedThinkingLevels.includes(this.selectedModelObj.recommendedThinkingLevel)) {
        this.pickerThinkingLevel = this.selectedModelObj.recommendedThinkingLevel;
      } else {
        this.pickerThinkingLevel = this.selectedModelObj.supportedThinkingLevels.includes('medium') 
          ? 'medium' 
          : this.selectedModelObj.supportedThinkingLevels[0];
      }
    } else {
      this.pickerThinkingLevel = '';
    }
  }

  onSortChange() {
    if (this.sortedModels.length > 0) {
      this.selectedModelId = this.sortedModels[0].id;
      this.onPickerModelSelect();
    }
  }

  addSelectedModel() {
    if (this.selectedModelId) {
      this.saving = true;
      let displayName = this.selectedModelObj?.id; // default
      this.settingsService.addUserModel(this.pickerProvider, this.selectedModelId, displayName, this.pickerThinkingLevel || undefined).subscribe({
        next: () => {
          this.loadModels();
          this.closeModelPicker();
          this.saving = false;
        },
        error: (err) => {
          console.error("Failed to add model", err);
          this.saving = false;
        }
      });
    }
  }

  openEdit(model: UserAiModel) {
    this.editingModel = Object.assign({}, model);
    this.editDisplayName = model.displayName || model.modelId;
    this.editThinkingLevel = model.thinkingLevel || '';
    
    const standardOptions = ['', 'minimal', 'low', 'medium', 'high', 'xhigh', 'max', 'pro'];
    if (standardOptions.includes(this.editThinkingLevel)) {
      this.editThinkingLevelSelect = this.editThinkingLevel;
    } else {
      this.editThinkingLevelSelect = 'custom';
    }

    this.editModelDialog?.nativeElement.showModal();
  }

  closeEdit() {
    this.editModelDialog?.nativeElement.close();
    this.editingModel = null;
  }

  onEditThinkingLevelChange() {
    if (this.editThinkingLevelSelect !== 'custom') {
      this.editThinkingLevel = this.editThinkingLevelSelect;
    } else {
      this.editThinkingLevel = ''; 
    }
  }

  saveEdit() {
    if (this.editingModel && this.editingModel.id) {
      this.saving = true;
      this.settingsService.updateUserModel(this.editingModel.id, this.editDisplayName, this.editThinkingLevel || undefined).subscribe({
        next: () => {
          this.loadModels();
          this.closeEdit();
          this.saving = false;
        },
        error: (err) => {
          console.error("Failed to update model", err);
          this.saving = false;
        }
      });
    }
  }
}
