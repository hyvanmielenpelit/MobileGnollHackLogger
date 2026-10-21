import { ChangeDetectionStrategy, Component, computed, input, signal } from '@angular/core';
import type { AiTelemetryKeySource } from '../../../../services/admin.service';
import { LoadingIndicatorComponent } from '../../../../shared/loading-indicator/loading-indicator.component';
import { ProviderBadgeComponent } from '../../../../shared/provider-badge/provider-badge.component';
import { ModelPickerMatchPart, ModelPickerProviderFacet, matchParts } from '../../../../shared/model-picker/model-picker-view';
import { TelemetrySortPopoverComponent } from '../../sort-popover/telemetry-sort-popover.component';
import {
  CHART_KEY_SOURCE_LABELS,
  CHART_MODEL_NOUN,
  CHART_MODEL_SORT_FIELDS,
  ChartKeySource,
  ChartKeySourceFacet,
  ChartModelRow,
  ChartModelSortField,
  ChartRowFilter,
  DEFAULT_CHART_MODEL_SORT,
  absentModelRow,
  chartKeySourceFacets,
  chartProviderFacets,
  effectiveChartProviders,
  readChartModelsView,
  usageParts,
  writeChartModelsView
} from './telemetry-chart-filters';
import { ChartSelectionConfig, TelemetryChartSelectionBase } from './telemetry-chart-selection.base';

const KEY_SOURCE_BADGES: Readonly<Record<ChartKeySource, string>> = {
  system: 'System',
  user: 'Own key'
};

/**
 * The charts dialog's Models chooser: the models of the scope with their usage, a search by name,
 * model id or provider, provider and key-source toggles, the sort, and the choice of the models to
 * chart. The sort and the pressed providers are remembered per browser.
 */
@Component({
  selector: 'app-telemetry-chart-models',
  imports: [TelemetrySortPopoverComponent, LoadingIndicatorComponent, ProviderBadgeComponent],
  templateUrl: './telemetry-chart-models.component.html',
  styleUrl: './telemetry-chart-models.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class TelemetryChartModelsComponent extends TelemetryChartSelectionBase<ChartModelRow, ChartModelSortField> {
  /** The dialog's Keys filter; the key-source toggles show under *All keys* only. */
  readonly keys = input<AiTelemetryKeySource>('system');

  /** The pressed providers, lower-cased; none means every provider. Remembered per browser. */
  readonly providers = signal<string[]>(readChartModelsView().providers);
  /** The pressed key sources; none means both. Never stored. */
  readonly keySources = signal<ChartKeySource[]>([]);

  /** One toggle per provider of the rows. */
  readonly providerFacets = computed(() => chartProviderFacets(this.rows()));

  /** *System* and *Own keys*, under *All keys* only. */
  readonly keySourceFacets = computed<ChartKeySourceFacet[]>(() => this.keys() === 'both' ? chartKeySourceFacets(this.rows()) : []);

  readonly effectiveProviders = computed(() =>
    this.providerFacets().length >= 2 ? effectiveChartProviders(this.providers(), this.providerFacets()) : []);

  readonly effectiveKeySources = computed(() => {
    const present = new Set(this.keySourceFacets().map(f => f.key));
    return present.size >= 2 ? this.keySources().filter(source => present.has(source)) : [];
  });

  protected override readonly config: ChartSelectionConfig<ChartModelRow, ChartModelSortField> = {
    idPrefix: 'tl-sel-models',
    noun: CHART_MODEL_NOUN,
    sortFields: CHART_MODEL_SORT_FIELDS,
    defaultSort: DEFAULT_CHART_MODEL_SORT,
    readSort: () => readChartModelsView().sort,
    writeSort: () => undefined,
    absentRow: absentModelRow
  };

  protected override extraFilter(): Partial<ChartRowFilter> {
    return { providers: this.effectiveProviders(), keySources: this.effectiveKeySources() };
  }

  protected override extraNarrowing(): boolean {
    return this.effectiveProviders().length > 0 || this.effectiveKeySources().length > 0;
  }

  protected override clearExtraFilters(): void {
    this.providers.set([]);
    this.keySources.set([]);
    this.saveView();
  }

  /** Stores the sort and the pressed providers. */
  protected override saveView(): void {
    writeChartModelsView({ sort: this.sort(), providers: this.providers() });
  }

  isProviderPressed(facet: ModelPickerProviderFacet): boolean {
    return this.effectiveProviders().includes(facet.key);
  }

  toggleProvider(facet: ModelPickerProviderFacet): void {
    const pressed = this.effectiveProviders();
    this.providers.set(pressed.includes(facet.key) ? pressed.filter(p => p !== facet.key) : [...pressed, facet.key]);
    this.resetBatch();
    this.saveView();
  }

  isKeySourcePressed(facet: ChartKeySourceFacet): boolean {
    return this.effectiveKeySources().includes(facet.key);
  }

  toggleKeySource(facet: ChartKeySourceFacet): void {
    const pressed = this.effectiveKeySources();
    this.keySources.set(pressed.includes(facet.key) ? pressed.filter(k => k !== facet.key) : [...pressed, facet.key]);
    this.resetBatch();
  }

  keySourceToggleLabel(facet: ChartKeySourceFacet): string {
    return CHART_KEY_SOURCE_LABELS[facet.key];
  }

  keySourceBadge(source: ChartKeySource): string {
    return KEY_SOURCE_BADGES[source] ?? source;
  }

  /** The model id under a catalog name, with the search matches marked. */
  modelIdParts(row: ChartModelRow): ModelPickerMatchPart[] {
    return matchParts(row.modelId, this.searchTerms());
  }

  /** *12 requests · est. $0.25 · last used 2026-10-13*. */
  metaParts(row: ChartModelRow): string[] {
    return usageParts(row, 'last used');
  }
}
