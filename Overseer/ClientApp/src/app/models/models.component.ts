import { Component, OnInit, inject, ViewChild, ElementRef, HostListener, ChangeDetectionStrategy } from '@angular/core';

import { FormsModule } from '@angular/forms';
import { RouterModule } from '@angular/router';
import { SettingsService, UserAiModel, ApiModelDto } from '../services/settings.service';
import { AiModelFormComponent, AiModelFormResult } from '../shared/ai-model-form/ai-model-form.component';

@Component({
    selector: 'app-models',
    imports: [FormsModule, RouterModule, AiModelFormComponent],
    templateUrl: './models.component.html',
    changeDetection: ChangeDetectionStrategy.Eager,
    styleUrl: './models.component.scss'
})
export class ModelsComponent implements OnInit {
  settingsService = inject(SettingsService);

  @ViewChild('modelPickerDialog') modelPickerDialog!: ElementRef<HTMLDialogElement>;
  @ViewChild('editModelDialog') editModelDialog!: ElementRef<HTMLDialogElement>;
  @ViewChild('deleteModelConfirmDialog') deleteModelConfirmDialog!: ElementRef<HTMLDialogElement>;

  userModels: UserAiModel[] = [];
  systemModels: UserAiModel[] = [];
  titleUserModels: UserAiModel[] = [];
  titleSystemModels: UserAiModel[] = [];
  loading = false;
  saving = false;
  titleModelSelection: string | null = null;
  savingTitleModel = false;
  savedTitleModelSuccess = false;
  isTitleDropdownOpen = false;
  
  // Model Picker State
  providers = ['OpenAI', 'Anthropic', 'Google'];
  pickerProvider = 'OpenAI';
  isAddingModel = false;
  
  // Edit State
  editingModel: UserAiModel | null = null;
  editFormData: any = null;
  modelToDeleteId: number | undefined = undefined;

  ngOnInit() {
    this.settingsService.getSettings().subscribe({
      next: (settings) => {
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

  loadModels() {
    this.loading = true;
    this.settingsService.getUserModels().subscribe({
      next: (models) => {
        this.userModels = models.filter(m => !m.isSystem && (m.modelRole === undefined || (m.modelRole & 1) === 1));
        this.systemModels = models.filter(m => m.isSystem && (m.modelRole === undefined || (m.modelRole & 1) === 1));
        this.titleUserModels = models.filter(m => !m.isSystem && (m.modelRole === undefined || (m.modelRole & 2) === 2));
        this.titleSystemModels = models.filter(m => m.isSystem && (m.modelRole === undefined || (m.modelRole & 2) === 2));
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
        this.settingsService.getSettings().subscribe({
          next: (settings) => {
            if (settings.titleGenerationModelId) {
              this.titleModelSelection = 'u_' + settings.titleGenerationModelId;
            } else if (settings.titleGenerationSystemModelId) {
              this.titleModelSelection = 's_' + settings.titleGenerationSystemModelId;
            } else {
              this.titleModelSelection = null;
            }
          }
        });
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

  @HostListener('document:click', ['$event'])
  onDocumentClick(event: Event) {
    const target = event.target as HTMLElement;
    if (!target.closest('.title-model-selector-wrapper')) {
      this.isTitleDropdownOpen = false;
    }
  }

  toggleTitleDropdown(event: Event) {
    event.stopPropagation();
    this.isTitleDropdownOpen = !this.isTitleDropdownOpen;
  }

  selectTitleModel(id: number | null, isSystem: boolean) {
    if (id === null) {
      this.titleModelSelection = null;
    } else if (isSystem) {
      this.titleModelSelection = 's_' + id;
    } else {
      this.titleModelSelection = 'u_' + id;
    }
    
    this.isTitleDropdownOpen = false;
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

  get selectedTitleModel(): UserAiModel | null {
    if (!this.titleModelSelection) return null;
    
    if (this.titleModelSelection.startsWith('u_')) {
      const id = parseInt(this.titleModelSelection.substring(2));
      return this.titleUserModels.find(m => m.id === id) || null;
    } else if (this.titleModelSelection.startsWith('s_')) {
      const id = parseInt(this.titleModelSelection.substring(2));
      return this.titleSystemModels.find(m => m.id === id) || null;
    }
    
    return null;
  }

  get selectedTitleModelDisplay(): string {
    const model = this.selectedTitleModel;
    if (!model) {
      return 'Default (First Available Chat Model)';
    }
    return model.displayName || model.modelId;
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
    this.settingsService.addUserModel(
      formData.provider, 
      formData.modelId, 
      formData.displayName, 
      formData.thinkingLevel || undefined, 
      formData.maxInputTokens, 
      formData.maxOutputTokens
    ).subscribe({
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
    this.editFormData = { ...model };
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
      this.settingsService.updateUserModel(
        this.editingModel.id, 
        formData.displayName, 
        formData.thinkingLevel || undefined, 
        formData.maxInputTokens, 
        formData.maxOutputTokens
      ).subscribe({
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

  formatThinkingLevel(level: string | undefined): string {
    if (!level) return 'None';
    return level.charAt(0).toUpperCase() + level.slice(1);
  }
}
