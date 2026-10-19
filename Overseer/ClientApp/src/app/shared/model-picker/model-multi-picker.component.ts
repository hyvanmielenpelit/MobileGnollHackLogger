import {
  ChangeDetectionStrategy,
  ChangeDetectorRef,
  Component,
  EventEmitter,
  Input,
  OnChanges,
  OnDestroy,
  OnInit,
  Output,
  SimpleChanges,
  ViewChild,
  inject
} from '@angular/core';

import { MultiPickerComponent } from '../multi-picker/multi-picker.component';
import { MultiPickerOption, MultiPickerSelection, MultiPickerStatus } from '../multi-picker/multi-picker.models';
import { ModelOptionBadgesComponent } from './model-option-badges.component';
import { ModelPickerKey, ModelPickerModel, ModelPickerNote, ModelPickerOption } from './model-picker.component';
import { ModelPickerSelectShown, ModelPickerToolsComponent } from './model-picker-tools.component';
import {
  DEFAULT_MODEL_PICKER_VIEW,
  ModelPickerMatchPart,
  ModelPickerProviderFacet,
  ModelPickerViewState,
  ModelPickerViewTools,
  ModelPickerViewWriter,
  applyModelPickerView,
  effectiveView,
  hasReleaseDates,
  isReleaseSort,
  matchParts,
  providerFacets,
  readModelPickerView,
  releaseMonthLabel,
  sameModelPickerView,
  searchTerms,
  showsViewTools
} from './model-picker-view';

export interface ModelMultiPickerSelection<M extends ModelPickerModel = ModelPickerModel> {
  /** The chosen keys, in option order. */
  keys: ModelPickerKey[];
  /** The chosen options' models, in the same order. */
  models: M[];
}

let nextUid = 0;

/**
 * A multi-select model picker: `app-multi-picker` with each option drawn as the model's name and
 * the single model picker's badges (`app-model-option-badges`), then a muted detail; each chip
 * carries the same badges under the name, and its `notes` as a list (else the detail as a muted
 * note). Price and
 * parallel-execution badges are off by default, since choosing which models something covers is
 * not choosing what to run. Presentational: the host owns the selection and `status`, passed through.
 * From 6 options up it adds the view toolbar of `app-model-picker` (search, sort, provider filter,
 * *Select all shown*), which narrows the list and never the selection.
 */
@Component({
  selector: 'app-model-multi-picker',
  standalone: true,
  imports: [MultiPickerComponent, ModelOptionBadgesComponent, ModelPickerToolsComponent],
  templateUrl: './model-multi-picker.component.html',
  styleUrl: './model-multi-picker.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class ModelMultiPickerComponent<M extends ModelPickerModel = ModelPickerModel> implements OnInit, OnChanges, OnDestroy {
  private cdr = inject(ChangeDetectorRef);

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
  @Input() status: MultiPickerStatus | null = null;
  /** The search, sort and provider toolbar: `auto` shows it from 6 options up. */
  @Input() viewTools: ModelPickerViewTools = 'auto';
  /** Names the picker's role; its view is stored as `overseer.modelPicker.view.<key>`. Null keeps it in memory only. */
  @Input() viewStateKey: string | null = null;

  /** Once per toggle, All, None, chip removal or *Select all shown*. */
  @Output() selectionChange = new EventEmitter<ModelMultiPickerSelection<M>>();

  @ViewChild(MultiPickerComponent, { static: true }) private picker!: MultiPickerComponent;

  readonly uid = `mmp-${++nextUid}`;

  pickerOptions: MultiPickerOption[] = [];

  toolsShown = false;
  /** The stored view, shown in the toolbar as is. */
  viewState: ModelPickerViewState = DEFAULT_MODEL_PICKER_VIEW;
  facets: ModelPickerProviderFacet[] = [];
  dateSortAvailable = false;
  /** The listbox's keys in display order, or null without the toolbar. */
  visibleKeys: ModelPickerKey[] | null = null;

  private byKey = new Map<ModelPickerKey, ModelPickerOption<M>>();
  private nameParts = new Map<ModelPickerKey, ModelPickerMatchPart[]>();
  private releases = new Map<ModelPickerKey, string>();
  private visibleSet = new Set<ModelPickerKey>();
  private viewWriter = new ModelPickerViewWriter(() => this.viewStateKey);

  /** How many of the selected models the view hides, as a status phrase. */
  get hiddenSelectionText(): string | null {
    if (!this.toolsShown) return null;
    const hidden = this.selectedKeys.filter(key => this.byKey.has(key) && !this.visibleSet.has(key)).length;
    if (hidden === 0) return null;
    return hidden === 1 ? '1 selected model hidden' : `${hidden} selected models hidden`;
  }

  /** The shown models *Select all shown* would add, and why it cannot; one object until either changes. */
  selectShown: ModelPickerSelectShown = { count: 0, reason: null };

  ngOnInit(): void {
    this.viewState = readModelPickerView(this.viewStateKey);
    this.rebuildView();
  }

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['options']) {
      this.byKey = new Map(this.options.map(o => [o.key, o]));
      this.pickerOptions = this.options.map(o => {
        // Notes stand in for the detail, so the remove button is described by them.
        const detail = o.notes?.length ? o.notes.map(n => n.text).join(' · ') : o.detail;
        return {
          key: o.key,
          label: this.modelName(o.model),
          ...(o.tag ? { tag: o.tag } : {}),
          ...(o.group ? { group: o.group } : {}),
          ...(detail ? { detail } : {}),
          ...(o.disabledReason ? { disabledReason: o.disabledReason } : {})
        };
      });
    }
    if (changes['options'] || changes['viewTools']) {
      this.rebuildView();
    } else if (changes['selectedKeys'] || changes['max']) {
      this.refreshSelectShown();
    }
  }

  ngOnDestroy(): void {
    this.viewWriter.dispose();
  }

  modelFor(key: ModelPickerKey): M | null {
    return this.byKey.get(key)?.model ?? null;
  }

  notesFor(key: ModelPickerKey): readonly ModelPickerNote[] {
    return this.byKey.get(key)?.notes ?? [];
  }

  /** The option's name in runs, the ones matching the search marked. */
  namePartsFor(option: MultiPickerOption): ModelPickerMatchPart[] {
    return this.nameParts.get(option.key) ?? [{ text: option.label, match: false }];
  }

  /** The release month while a release-date sort applies, else null. */
  releaseFor(key: ModelPickerKey): string | null {
    return this.releases.get(key) ?? null;
  }

  modelName(model: ModelPickerModel): string { return model.displayName || model.modelId || ''; }

  onSelectionChange(selection: MultiPickerSelection): void {
    const models = selection.keys
      .map(key => this.modelFor(key))
      .filter((model): model is M => model !== null);
    this.selectionChange.emit({ keys: selection.keys, models });
  }

  onViewStateChange(state: ModelPickerViewState): void {
    const previous = this.viewState;
    if (sameModelPickerView(previous, state)) return;
    this.viewState = state;
    const searchOnly = previous.sort === state.sort
      && previous.providers.length === state.providers.length
      && previous.providers.every((p, i) => p === state.providers[i]);
    this.viewWriter.write(state, searchOnly);
    this.rebuildView();
    this.cdr.markForCheck();
  }

  onSelectShown(): void {
    this.picker.addToSelection(this.selectShownKeys());
  }

  /** The shown, enabled models not selected yet. */
  private selectShownKeys(): ModelPickerKey[] {
    const selected = new Set(this.selectedKeys);
    return (this.visibleKeys ?? []).filter(key => !selected.has(key) && !this.byKey.get(key)?.disabledReason);
  }

  private rebuildView(): void {
    this.toolsShown = showsViewTools(this.viewTools, this.options.length);
    this.nameParts = new Map();
    this.releases = new Map();
    if (!this.toolsShown) {
      this.facets = [];
      this.dateSortAvailable = false;
      this.visibleKeys = null;
      this.visibleSet = new Set(this.options.map(o => o.key));
      return;
    }
    const view = effectiveView(this.viewState, this.options);
    const terms = searchTerms(view.search);
    const releaseShown = isReleaseSort(view.sort);
    this.facets = providerFacets(this.options);
    this.dateSortAvailable = hasReleaseDates(this.options);
    this.visibleKeys = applyModelPickerView(this.options, view).map(i => {
      const option = this.options[i];
      if (terms.length > 0) this.nameParts.set(option.key, matchParts(this.modelName(option.model), terms));
      if (releaseShown) this.releases.set(option.key, releaseMonthLabel(option.model.releaseDate) || 'date unknown');
      return option.key;
    });
    this.visibleSet = new Set(this.visibleKeys);
    this.refreshSelectShown();
  }

  private refreshSelectShown(): void {
    const count = this.selectShownKeys().length;
    let reason: string | null = null;
    if (count === 0) {
      reason = 'All shown models are already selected.';
    } else if (this.max !== null && this.selectedKeys.length >= this.max) {
      reason = `At most ${this.max} ${this.max === 1 ? 'model' : 'models'} can be selected.`;
    }
    if (count !== this.selectShown.count || reason !== this.selectShown.reason) {
      this.selectShown = { count, reason };
    }
  }
}
