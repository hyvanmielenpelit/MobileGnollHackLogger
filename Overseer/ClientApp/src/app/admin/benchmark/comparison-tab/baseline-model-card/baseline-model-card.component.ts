import { ChangeDetectionStrategy, ChangeDetectorRef, Component, Input, OnDestroy, OnInit, inject } from '@angular/core';
import { DatePipe } from '@angular/common';
import { Subscription } from 'rxjs';

import { AdminBenchmarkService, BenchmarkBaselineModelDto } from '../../../../services/admin-benchmark.service';
import { ModelPickerComponent, ModelPickerKey, ModelPickerModel, ModelPickerOption } from '../../../../shared/model-picker/model-picker.component';
import { ProviderBadgeComponent } from '../../../../shared/provider-badge/provider-badge.component';
import { parseServerUtcDate } from '../../../../utils/date.util';
import { httpErrorText } from '../../batteries/battery.models';

/** The server's `{ error }` refusal, else the message `httpErrorText` finds, else the fallback. */
function refusalText(err: unknown, fallback: string): string {
  const body = (err as { error?: unknown } | null | undefined)?.error;
  const message = body && typeof body === 'object' ? (body as { error?: unknown }).error : null;
  return typeof message === 'string' && message.trim() !== '' ? message.trim() : httpErrorText(err, fallback);
}

/**
 * The Model Comparison launcher's *Baseline model* card: the GnollBench baseline model, the model the
 * Overseer team currently prefers, which the comparison and its documents judge the other models
 * against. It is stored on the server, so every administrator and browser sees the same one.
 * **Change** opens the shared model picker over the enabled configurations with the Benchmark role;
 * **Clear** removes it.
 */
@Component({
  selector: 'app-baseline-model-card',
  standalone: true,
  imports: [DatePipe, ModelPickerComponent, ProviderBadgeComponent],
  templateUrl: './baseline-model-card.component.html',
  styleUrls: ['./baseline-model-card.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class BaselineModelCardComponent implements OnInit, OnDestroy {
  private readonly benchmarkService = inject(AdminBenchmarkService);
  private readonly cdr = inject(ChangeDetectorRef);

  /** The enabled configurations with the Benchmark role, as the launcher's pickers offer them. */
  @Input() pickerOptions: readonly ModelPickerOption<ModelPickerModel>[] = [];

  /** The picker's text when no configuration is offered. */
  @Input() emptyHint: string | null = null;

  baseline: BenchmarkBaselineModelDto | null = null;
  loading = false;
  loadError: string | null = null;
  /** The picker is open for a new choice. */
  changing = false;
  saving = false;
  saveError: string | null = null;
  /** The outcome of the last change, in the card's live line. */
  status = '';

  private loadSub: Subscription | null = null;
  private saveSub: Subscription | null = null;

  ngOnInit(): void {
    this.load();
  }

  ngOnDestroy(): void {
    this.loadSub?.unsubscribe();
    this.saveSub?.unsubscribe();
  }

  /** Reads the baseline model; a failure leaves the card with its reason and Change still offered. */
  load(): void {
    this.loadSub?.unsubscribe();
    this.loading = true;
    this.loadError = null;
    this.loadSub = this.benchmarkService.getBaselineModel().subscribe({
      next: baseline => {
        this.loading = false;
        this.baseline = baseline;
        this.cdr.markForCheck();
      },
      error: (err: unknown) => {
        this.loading = false;
        this.loadError = refusalText(err, 'The baseline model could not be loaded.');
        this.cdr.markForCheck();
      }
    });
  }

  /** When it was set, as a date the pipe reads in local time. */
  get setAt(): Date | null {
    return this.baseline ? parseServerUtcDate(this.baseline.setAtUtc) : null;
  }

  /** The configuration it was set from, as the picker keys it; null when unknown. */
  get selectedKey(): ModelPickerKey | null {
    return this.baseline?.sourceConfigurationId ?? null;
  }

  /** The settings beyond provider, model id and thinking level: those that are set. */
  get otherSettings(): readonly { readonly term: string; readonly value: string }[] {
    const baseline = this.baseline;
    if (!baseline) {
      return [];
    }
    const settings: { term: string; value: string }[] = [];
    if (baseline.reasoningMode) {
      settings.push({ term: 'Reasoning mode', value: baseline.reasoningMode });
    }
    if (baseline.serviceTier) {
      settings.push({ term: 'Service tier', value: baseline.serviceTier });
    }
    if (baseline.maxOutputTokens != null) {
      settings.push({ term: 'Max output tokens', value: baseline.maxOutputTokens.toLocaleString('en-US') });
    }
    return settings;
  }

  startChange(): void {
    if (this.saving) {
      return;
    }
    this.changing = !this.changing;
    this.saveError = null;
    this.cdr.markForCheck();
  }

  /** The picker's choice: the configuration's current settings become the baseline model. */
  choose(key: ModelPickerKey | null): void {
    if (typeof key !== 'number' || this.saving) {
      return;
    }
    this.save(key);
  }

  clear(): void {
    if (this.saving || !this.baseline) {
      return;
    }
    this.save(null);
  }

  private save(configurationId: number | null): void {
    this.saving = true;
    this.saveError = null;
    this.status = '';
    this.saveSub?.unsubscribe();
    this.saveSub = this.benchmarkService.setBaselineModel(configurationId).subscribe({
      next: baseline => {
        this.saving = false;
        this.baseline = baseline;
        this.changing = false;
        this.status = baseline ? `The baseline model is now ${baseline.displayName}.` : 'The baseline model is cleared.';
        this.cdr.markForCheck();
      },
      error: (err: unknown) => {
        this.saving = false;
        this.saveError = refusalText(err, 'The baseline model could not be saved.');
        this.cdr.markForCheck();
      }
    });
  }
}
