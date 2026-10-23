import {
  ChangeDetectionStrategy,
  ChangeDetectorRef,
  Component,
  ElementRef,
  EventEmitter,
  Input,
  OnDestroy,
  OnInit,
  Output,
  ViewChild,
  inject
} from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';
import { Subscription } from 'rxjs';
import {
  AdminBenchmarkService,
  BENCHMARK_INFORMATION_CONDITIONS,
  BenchmarkInformationCondition,
  BenchmarkInstructionVariantDetailsDto,
  BenchmarkInstructionVariantDto,
  BenchmarkInstructionVariantFeatureDetailsDto,
  BenchmarkInstructionVariantFeatureDto,
  BenchmarkInstructionVariantRunDto
} from '../../../../services/admin-benchmark.service';
import { TextDiffComponent } from '../../../../shared/text-diff/text-diff.component';
import { DiffResult, diffLines } from '../../../../utils/text-diff.util';
import { ensureOverlayPolyfills } from '../../../../utils/polyfills.util';
import { formatStatusLabel, refusalText } from '../../benchmark-run-format';
import { formatUtc } from '../../report-pack/report-document-format';
import {
  VariantAblationMode,
  ablationLabel,
  danglingMentionLine,
  danglingMentions,
  experimentLine,
  featureNotApplyingReason,
  keptFeatures,
  parseVariantSpec,
  resolveVariantSpec,
  variantOptionSuffix,
  variantReferenceLine,
  variantSpec
} from '../instruction-variant-summary';

/** How a run uses the checked variant: whole, with one feature left out, or with only one. */
export type InstructionVariantRunMode = 'whole' | VariantAblationMode;

/** The two presentations of a reworded description's change. */
export type InstructionVariantDiffView = 'side' | 'diff';

/** The `localStorage` key of the dialog's remembered view. */
export const INSTRUCTION_VARIANT_DIALOG_VIEW_KEY = 'overseer.benchmark.instructionVariantDialog.view';

interface InstructionVariantDialogView {
  version: 1;
  aboutOpen: boolean;
}

/** One parameter of an experimental tool, from its JSON schema. */
export interface VariantToolParameter {
  name: string;
  type: string;
  required: boolean;
  description: string;
}

/** One feature of the checked variant as the dialog shows it. */
export interface VariantFeatureView {
  feature: BenchmarkInstructionVariantFeatureDto;
  /** The feature id made safe for DOM ids. */
  domId: string;
  title: string;
  /** False while the details have not arrived; the texts are then unknown. */
  textLoaded: boolean;
  text: string | null;
  productionText: string | null;
  /** The live description against the variant's, for a reworded description with both texts. */
  diff: DiffResult | null;
  /** The live description has lines the variant's copy does not contain (see `productionGuideDrifted`). */
  drifted: boolean;
  /** Null when the tool has no schema or the schema does not parse. */
  parameters: VariantToolParameter[] | null;
  parametersUnreadable: boolean;
}

/** The checked variant's details as they load. */
export type DetailsEntry =
  | { status: 'loading' }
  | { status: 'loaded'; data: BenchmarkInstructionVariantDetailsDto }
  | { status: 'error'; message: string };

const FEATURE_TITLE_PREFIXES: Readonly<Record<BenchmarkInstructionVariantFeatureDto['kind'], string>> = {
  experimentalTool: 'Experimental tool',
  experiment: 'Output switch',
  rewordedGuide: 'Reworded description',
  appendix: 'Extra instructions'
};

/**
 * Whether a production tool description has changed since a variant copied it. A variant's guide is
 * the production text it copied plus the lines it adds or rewords, so in the line diff from the live
 * text to the variant's every `added` line is the variant's own and every `removed` line is a line
 * of the live text that the variant's copy lacks. The heuristic counts any non-blank removed line as
 * drift: the live text then has a line the copy never had, or changed one the copy still holds in its
 * old form. A variant that deliberately deletes or rewrites a production line reads as drifted too,
 * which is the cautious side; blank lines and line endings are ignored.
 */
export function productionGuideDrifted(diff: DiffResult): boolean {
  return diff.lines.some(line => line.kind === 'removed' && line.text.trim() !== '');
}

/** An experimental tool's parameters from its JSON schema; null when the schema does not parse. */
export function toolParametersOf(schemaJson: string): VariantToolParameter[] | null {
  let schema: unknown;
  try {
    schema = JSON.parse(schemaJson);
  } catch {
    return null;
  }
  if (!schema || typeof schema !== 'object') return null;
  const record = schema as { properties?: unknown; required?: unknown };
  const properties = record.properties && typeof record.properties === 'object'
    ? record.properties as Record<string, unknown>
    : {};
  const required = Array.isArray(record.required) ? record.required.filter((r): r is string => typeof r === 'string') : [];
  return Object.entries(properties).map(([name, value]) => {
    const property = (value && typeof value === 'object' ? value : {}) as { type?: unknown; enum?: unknown; description?: unknown };
    const types = Array.isArray(property.type) ? property.type.map(String) : property.type != null ? [String(property.type)] : [];
    let type = types.join(' or ') || 'any';
    if (Array.isArray(property.enum) && property.enum.length > 0) {
      type += ` (one of ${property.enum.map(String).join(', ')})`;
    }
    return {
      name,
      type,
      required: required.includes(name),
      description: typeof property.description === 'string' ? property.description : ''
    };
  });
}

function readView(): InstructionVariantDialogView | null {
  try {
    const raw = localStorage.getItem(INSTRUCTION_VARIANT_DIALOG_VIEW_KEY);
    if (!raw) return null;
    const parsed = JSON.parse(raw) as Partial<InstructionVariantDialogView> | null;
    return parsed?.version === 1 && typeof parsed.aboutOpen === 'boolean' ? { version: 1, aboutOpen: parsed.aboutOpen } : null;
  } catch {
    return null;
  }
}

function writeView(view: InstructionVariantDialogView): void {
  try {
    localStorage.setItem(INSTRUCTION_VARIANT_DIALOG_VIEW_KEY, JSON.stringify(view));
  } catch {
    // Storage may be unavailable or full; the dialog then opens with About open next time.
  }
}

/**
 * The Experimental Changes dialog: choose no variant, a whole variant, or a single-feature ablation of
 * one, after reading what each feature changes. The host opens it with the current spec and receives
 * the chosen spec from `chosen`; `closed` fires however it closes, so the host can return focus.
 */
@Component({
  selector: 'app-instruction-variant-dialog',
  standalone: true,
  imports: [NgTemplateOutlet, TextDiffComponent],
  templateUrl: './instruction-variant-dialog.component.html',
  styleUrls: ['./instruction-variant-dialog.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class InstructionVariantDialogComponent implements OnInit, OnDestroy {
  private readonly benchmarkService = inject(AdminBenchmarkService);
  private readonly cdr = inject(ChangeDetectorRef);

  @ViewChild('ivDialog', { static: true }) dialog?: ElementRef<HTMLDialogElement>;

  /** The whole catalog, valid and invalid variants alike. */
  @Input() variants: readonly BenchmarkInstructionVariantDto[] = [];

  /** The launcher's Tool Access choice, which decides which features apply. */
  @Input() informationCondition: BenchmarkInformationCondition = 'production';

  /** The chosen spec on **Use this**: a name, an ablation spec, or null for None. */
  @Output() readonly chosen = new EventEmitter<string | null>();

  /** The dialog closed, by Use this, Cancel, Escape or a run link. */
  @Output() readonly closed = new EventEmitter<void>();

  /** A run of the Runs that used it list whose report should open. */
  @Output() readonly openRunReport = new EventEmitter<number>();

  readonly docPath = 'docs/overseer/ai-benchmark-conditions-and-variants.md § 6';

  /** The checked variant's name; null for None. */
  variantName: string | null = null;
  mode: InstructionVariantRunMode = 'whole';
  featureId: string | null = null;
  danglingAcknowledged = false;
  aboutOpen = true;
  featureViews: VariantFeatureView[] = [];

  private readonly diffViews = new Map<string, InstructionVariantDiffView>();
  private readonly details = new Map<string, DetailsEntry>();
  private readonly subscriptions: Subscription[] = [];

  ngOnInit(): void {
    // The close button carries an interest-triggered tooltip.
    ensureOverlayPolyfills();
  }

  ngOnDestroy(): void {
    this.cancelLoads();
  }

  // --- Opening and closing ---

  /** Shows the dialog with `spec` checked, loads the valid variants' details and focuses the checked variant. */
  open(spec: string | null): void {
    const parsed = parseVariantSpec(spec);
    const base = parsed ? this.validVariants.find(v => v.name === parsed.base) ?? null : null;
    const resolved = resolveVariantSpec(spec, this.variants);
    this.variantName = base?.name ?? null;
    this.mode = resolved?.mode ?? 'whole';
    this.featureId = resolved?.feature ?? null;
    this.danglingAcknowledged = false;
    this.diffViews.clear();
    this.aboutOpen = readView()?.aboutOpen ?? true;

    this.cancelLoads();
    this.details.clear();
    const order = [...this.validVariants].sort((a, b) => (a.name === this.variantName ? -1 : b.name === this.variantName ? 1 : 0));
    for (const variant of order) {
      this.loadDetails(variant.name);
    }
    this.refreshFeatureViews();

    this.cdr.markForCheck();
    this.cdr.detectChanges();
    const dialog = this.dialog?.nativeElement;
    if (dialog && !dialog.open) {
      dialog.showModal();
    }
    dialog?.querySelector<HTMLInputElement>('input[name="ivVariant"]:checked')?.focus();
  }

  /** Cancel, the close button and Escape: closes without a choice. */
  cancel(): void {
    this.dialog?.nativeElement.close();
  }

  /** The dialog's close event, however it closed. */
  onDialogClosed(): void {
    this.cancelLoads();
    this.closed.emit();
  }

  /** Use this: emits the chosen spec and closes; refused while the choice is incomplete. */
  useThis(): void {
    if (this.useRefusal) return;
    this.chosen.emit(this.spec);
    this.dialog?.nativeElement.close();
  }

  /** A run link: closes the dialog and asks the host for that run's report. */
  openRun(run: BenchmarkInstructionVariantRunDto): void {
    this.dialog?.nativeElement.close();
    this.openRunReport.emit(run.id);
  }

  onAboutToggle(event: Event): void {
    this.aboutOpen = (event.target as HTMLDetailsElement).open;
    writeView({ version: 1, aboutOpen: this.aboutOpen });
  }

  // --- The choice ---

  selectVariant(name: string | null): void {
    this.variantName = name;
    this.mode = 'whole';
    this.featureId = null;
    this.danglingAcknowledged = false;
    this.refreshFeatureViews();
  }

  selectMode(mode: InstructionVariantRunMode): void {
    this.mode = mode;
    if (mode === 'whole' || !this.isChoosable(this.featureId)) {
      this.featureId = null;
    }
    this.danglingAcknowledged = false;
  }

  selectFeature(id: string): void {
    if (!this.isChoosable(id)) return;
    this.featureId = id;
    this.danglingAcknowledged = false;
  }

  onDanglingAcknowledged(event: Event): void {
    this.danglingAcknowledged = (event.target as HTMLInputElement).checked;
  }

  diffViewOf(featureId: string): InstructionVariantDiffView {
    return this.diffViews.get(featureId) ?? 'diff';
  }

  setDiffView(featureId: string, view: InstructionVariantDiffView): void {
    this.diffViews.set(featureId, view);
  }

  // --- What the template reads ---

  get validVariants(): BenchmarkInstructionVariantDto[] {
    return this.variants.filter(v => v.isValid);
  }

  get invalidVariants(): BenchmarkInstructionVariantDto[] {
    return this.variants.filter(v => !v.isValid);
  }

  get checkedVariant(): BenchmarkInstructionVariantDto | null {
    return this.variantName == null ? null : this.validVariants.find(v => v.name === this.variantName) ?? null;
  }

  /** The checked variant's features: the details' when they have arrived, else the catalog's. */
  get features(): BenchmarkInstructionVariantFeatureDto[] {
    return this.featureViews.map(v => v.feature);
  }

  get ablating(): boolean {
    return this.mode !== 'whole';
  }

  get conditionName(): string {
    return BENCHMARK_INFORMATION_CONDITIONS.find(c => c.value === this.informationCondition)?.name ?? this.informationCondition;
  }

  /** The spec the current choice makes; null for None, and the bare name until an ablation has its feature. */
  get spec(): string | null {
    const variant = this.checkedVariant;
    if (!variant) return null;
    return this.ablating && this.featureId ? variantSpec(variant.name, this.mode as VariantAblationMode, this.featureId) : variant.name;
  }

  get specLabel(): string {
    const spec = this.spec;
    return spec ? ablationLabel(spec, this.variants) : 'None — same as the live chat';
  }

  get referenceLine(): string | null {
    const spec = this.spec;
    return spec ? variantReferenceLine(spec) : null;
  }

  /** The lines naming a kept text that mentions a feature the run leaves out. */
  get danglingLines(): string[] {
    if (!this.ablating || !this.featureId) return [];
    const features = this.features;
    return danglingMentions(features, this.mode as VariantAblationMode, this.featureId)
      .map(m => danglingMentionLine(m, features));
  }

  /** Why Use this waits; null once the choice is complete and every warning is acknowledged. */
  get useRefusal(): string | null {
    if (!this.checkedVariant || !this.ablating) return null;
    if (!this.featureId) {
      return this.mode === 'minus' ? 'Choose the feature to leave out.' : 'Choose the feature to run alone.';
    }
    if (!this.isChoosable(this.featureId)) {
      return `That feature does not apply under ${this.conditionName}; choose another.`;
    }
    if (this.danglingLines.length > 0 && !this.danglingAcknowledged) {
      return 'Confirm that the model may be told about something this run leaves out.';
    }
    return null;
  }

  /** Each feature the run keeps, with whether it applies under the current Tool Access. */
  get conditionRows(): { title: string; reason: string | null }[] {
    if (!this.checkedVariant) return [];
    const mode = this.ablating ? this.mode as VariantAblationMode : null;
    const keptIds = new Set(keptFeatures(this.features, mode && this.featureId ? mode : null, this.featureId).map(f => f.id));
    return this.featureViews
      .filter(v => keptIds.has(v.feature.id))
      .map(v => ({ title: v.title, reason: featureNotApplyingReason(v.feature, this.informationCondition) }));
  }

  get detailsEntry(): DetailsEntry | null {
    return this.variantName ? this.details.get(this.variantName) ?? null : null;
  }

  get loadedDetails(): BenchmarkInstructionVariantDetailsDto | null {
    const entry = this.detailsEntry;
    return entry?.status === 'loaded' ? entry.data : null;
  }

  get detailsError(): string | null {
    const entry = this.detailsEntry;
    return entry?.status === 'error' ? entry.message : null;
  }

  get detailsLoading(): boolean {
    return this.detailsEntry?.status === 'loading';
  }

  /** The stamp a run of the whole variant gets: from the details, else from the catalog's hash prefix. */
  get stamp(): string | null {
    const variant = this.checkedVariant;
    if (!variant) return null;
    return this.loadedDetails?.stamp ?? (variant.hashPrefix ? `${variant.name}:${variant.hashPrefix}` : null);
  }

  /** *Used by N runs* for a variant card, once its details have arrived. */
  runCountText(name: string): string | null {
    const entry = this.details.get(name);
    if (entry?.status !== 'loaded') return null;
    const count = entry.data.runCount;
    return count === 1 ? 'Used by 1 run' : `Used by ${count} runs`;
  }

  optionSuffix(variant: BenchmarkInstructionVariantDto): string | null {
    return variantOptionSuffix(variant);
  }

  notApplying(view: VariantFeatureView): string | null {
    return featureNotApplyingReason(view.feature, this.informationCondition);
  }

  experimentText(target: string): string {
    return experimentLine(target) ?? `Turns on the output switch ${target}.`;
  }

  specText(spec: string): string {
    return ablationLabel(spec, this.variants);
  }

  conditionText(condition: BenchmarkInformationCondition): string {
    return BENCHMARK_INFORMATION_CONDITIONS.find(c => c.value === condition)?.name ?? String(condition);
  }

  statusText(status: string): string {
    return formatStatusLabel(status);
  }

  dateText(iso: string): string {
    return formatUtc(iso);
  }

  /** *12 runs in all; the newest 10 are listed. 3 of them used an older version of the files.* */
  runsSummary(details: BenchmarkInstructionVariantDetailsDto): string {
    const total = details.runCount === 1 ? '1 run' : `${details.runCount} runs`;
    let text = details.runs.length < details.runCount
      ? `${total} in all; the newest ${details.runs.length} are listed.`
      : `${total} in all.`;
    if (details.olderHashRunCount > 0) {
      text += details.olderHashRunCount === 1
        ? ' 1 of them used an older version of the files.'
        : ` ${details.olderHashRunCount} of them used an older version of the files.`;
    }
    return text;
  }

  // --- Internals ---

  private isChoosable(id: string | null): boolean {
    if (!id) return false;
    const view = this.featureViews.find(v => v.feature.id === id);
    return !!view && featureNotApplyingReason(view.feature, this.informationCondition) === null;
  }

  private loadDetails(name: string): void {
    if (this.details.has(name)) return;
    this.details.set(name, { status: 'loading' });
    this.subscriptions.push(this.benchmarkService.getInstructionVariantDetails(name).subscribe({
      next: data => {
        this.details.set(name, { status: 'loaded', data });
        if (name === this.variantName) this.refreshFeatureViews();
        this.cdr.markForCheck();
      },
      error: err => {
        this.details.set(name, { status: 'error', message: refusalText(err, `What ${name} changes could not be loaded.`) });
        this.cdr.markForCheck();
      }
    }));
  }

  private cancelLoads(): void {
    for (const subscription of this.subscriptions) subscription.unsubscribe();
    this.subscriptions.length = 0;
    // A load cut short is tried again on the next open.
    for (const [name, entry] of this.details) {
      if (entry.status === 'loading') this.details.delete(name);
    }
  }

  private refreshFeatureViews(): void {
    const variant = this.checkedVariant;
    if (!variant) {
      this.featureViews = [];
      return;
    }
    const entry = this.details.get(variant.name);
    const detailed = entry?.status === 'loaded' ? entry.data.features : null;
    const features: (BenchmarkInstructionVariantFeatureDto | BenchmarkInstructionVariantFeatureDetailsDto)[] =
      detailed && detailed.length > 0 ? detailed : variant.features ?? [];
    this.featureViews = features.map(feature => this.featureView(feature, detailed !== null));
  }

  private featureView(
    feature: BenchmarkInstructionVariantFeatureDto | BenchmarkInstructionVariantFeatureDetailsDto, loaded: boolean): VariantFeatureView {
    const details = feature as BenchmarkInstructionVariantFeatureDetailsDto;
    const text = loaded ? details.text ?? null : null;
    const productionText = loaded ? details.productionText ?? null : null;
    const diff = feature.kind === 'rewordedGuide' && text != null && productionText != null ? diffLines(productionText, text) : null;
    const schema = loaded && feature.kind === 'experimentalTool' ? details.parameterSchemaJson ?? null : null;
    const parameters = schema ? toolParametersOf(schema) : null;
    return {
      feature,
      domId: feature.id.replace(/[^A-Za-z0-9_-]/g, '-'),
      title: `${FEATURE_TITLE_PREFIXES[feature.kind] ?? 'Feature'}: ${feature.target}`,
      textLoaded: loaded,
      text,
      productionText,
      diff,
      drifted: diff ? productionGuideDrifted(diff) : false,
      parameters,
      parametersUnreadable: schema != null && parameters === null
    };
  }
}
