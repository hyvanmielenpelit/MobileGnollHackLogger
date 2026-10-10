import {
  ChangeDetectionStrategy,
  Component,
  EventEmitter,
  Input,
  OnChanges,
  Output,
  SimpleChanges
} from '@angular/core';

import { MultiPickerComponent } from '../multi-picker/multi-picker.component';
import { MultiPickerOption, MultiPickerSelection } from '../multi-picker/multi-picker.models';
import { ModelOptionBadgesComponent } from './model-option-badges.component';
import { ModelPickerKey, ModelPickerModel, ModelPickerOption } from './model-picker.component';

export interface ModelMultiPickerSelection<M extends ModelPickerModel = ModelPickerModel> {
  /** The chosen keys, in option order. */
  keys: ModelPickerKey[];
  /** The chosen options' models, in the same order. */
  models: M[];
}

/**
 * A multi-select model picker: `app-multi-picker` with each option drawn as the model's name and
 * the single model picker's badges (`app-model-option-badges`), then a muted detail; each chip
 * carries the same badges under the name, and the detail as a note. Price and
 * parallel-execution badges are off by default, since choosing which models something covers is
 * not choosing what to run. Presentational: the host owns the selection.
 */
@Component({
  selector: 'app-model-multi-picker',
  standalone: true,
  imports: [MultiPickerComponent, ModelOptionBadgesComponent],
  templateUrl: './model-multi-picker.component.html',
  styleUrl: './model-multi-picker.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class ModelMultiPickerComponent<M extends ModelPickerModel = ModelPickerModel> implements OnChanges {
  @Input() options: readonly ModelPickerOption<M>[] = [];
  /** Compared with `===`. */
  @Input() selectedKeys: readonly ModelPickerKey[] = [];
  @Input() min = 0;
  @Input() max: number | null = null;
  @Input() labelledBy: string | null = null;
  @Input() label: string | null = null;
  @Input() describedBy: string | null = null;
  @Input() placeholder: string | null = null;
  @Input() emptyHint: string | null = null;
  @Input() chips: 'none' | 'selected' = 'selected';
  @Input() maxChips = 6;
  @Input() showAllNone = true;
  @Input() dropsUp = false;
  @Input() showPrice = false;
  @Input() showParallel = false;
  @Input() chipStyle: 'pill' | 'card' = 'pill';
  /**
   * How a chip's `detail` is drawn — muted, or as an amber warning line with the alert-triangle
   * glyph and a hidden "Warning:" prefix.
   */
  @Input() detailTone: 'muted' | 'warning' = 'muted';

  /** Once per toggle, All, None or chip removal. */
  @Output() selectionChange = new EventEmitter<ModelMultiPickerSelection<M>>();

  pickerOptions: MultiPickerOption[] = [];

  private byKey = new Map<ModelPickerKey, ModelPickerOption<M>>();

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['options']) {
      this.byKey = new Map(this.options.map(o => [o.key, o]));
      this.pickerOptions = this.options.map(o => ({
        key: o.key,
        label: this.modelName(o.model),
        ...(o.tag ? { tag: o.tag } : {}),
        ...(o.group ? { group: o.group } : {}),
        ...(o.detail ? { detail: o.detail } : {}),
        ...(o.disabledReason ? { disabledReason: o.disabledReason } : {})
      }));
    }
  }

  modelFor(key: ModelPickerKey): M | null {
    return this.byKey.get(key)?.model ?? null;
  }

  modelName(model: ModelPickerModel): string { return model.displayName || model.modelId || ''; }

  onSelectionChange(selection: MultiPickerSelection): void {
    const models = selection.keys
      .map(key => this.modelFor(key))
      .filter((model): model is M => model !== null);
    this.selectionChange.emit({ keys: selection.keys, models });
  }
}
