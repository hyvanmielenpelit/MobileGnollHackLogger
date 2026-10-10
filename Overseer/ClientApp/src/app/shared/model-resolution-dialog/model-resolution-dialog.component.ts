import {
  ChangeDetectionStrategy,
  ChangeDetectorRef,
  Component,
  ElementRef,
  EventEmitter,
  Input,
  OnDestroy,
  Output,
  ViewChild,
  inject
} from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { EMPTY, Observable, Subject, Subscription, catchError, map, of, switchMap } from 'rxjs';

import { AdminService, SystemConfigBlockerDto } from '../../services/admin.service';
import { SettingsService } from '../../services/settings.service';
import {
  CatalogTarget,
  ModelAvailability,
  ModelResolutionRefusal,
  ModelResolutionRequest,
  ModelResolutionResult,
  availabilitySentence,
  blockerText,
  formatCatalogDate
} from '../model-availability/model-availability';

/** The row the dialog resolves. */
export interface ModelResolutionSubject {
  id: number;
  provider: string;
  modelId: string;
  displayName: string;
  availability: ModelAvailability;
}

export type ModelResolutionChoice = 'switch' | 'keepCustom' | 'delete';

type CustomField = 'maxInputTokens' | 'maxOutputTokens' | 'inputPrice' | 'outputPrice' | 'cachedInputPrice';

interface CustomFieldSpec {
  key: CustomField;
  label: string;
  hint: string;
  inputmode: 'numeric' | 'decimal';
}

const CUSTOM_FIELDS: readonly CustomFieldSpec[] = [
  { key: 'maxInputTokens', label: 'Max input tokens', hint: 'A whole number, 1 or more.', inputmode: 'numeric' },
  { key: 'maxOutputTokens', label: 'Max output tokens', hint: 'A whole number, 1 or more.', inputmode: 'numeric' },
  {
    key: 'inputPrice', label: 'Input price per 1M tokens, USD',
    hint: 'Leave the input and output prices empty to keep the current pricing.', inputmode: 'decimal'
  },
  { key: 'outputPrice', label: 'Output price per 1M tokens, USD', hint: '0 or more.', inputmode: 'decimal' },
  { key: 'cachedInputPrice', label: 'Cached input price per 1M tokens, USD', hint: 'Optional, 0 or more.', inputmode: 'decimal' }
];

const INTEGER = /^\d+$/;
const DECIMAL = /^\d+(\.\d+)?$/;

interface PreviewOutcome {
  result: ModelResolutionResult | null;
  error: string | null;
}

let nextUid = 0;

/**
 * *Resolve "{model}"*: what to do with a user model or system configuration that is no longer in
 * the model catalog. Three radios choose the action: switch to a catalog model (with a dry-run
 * preview of the changes and of any work that blocks it), keep it as a custom model with its own
 * limits and prices, or delete it. `open(subject)` resets the dialog. A save emits `resolved` and
 * closes; for the user kind, *Delete model* deletes the row and emits a result with `deleted: true`
 * (`isResolutionDeletion`). For the system kind, *Delete…* emits `deleteRequested` and closes, so
 * the host's own delete flow takes over. `closed` follows every close.
 */
@Component({
  selector: 'app-model-resolution-dialog',
  standalone: true,
  templateUrl: './model-resolution-dialog.component.html',
  styleUrl: './model-resolution-dialog.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class ModelResolutionDialogComponent implements OnDestroy {
  private readonly cdr = inject(ChangeDetectorRef);
  private readonly settings = inject(SettingsService);
  private readonly admin = inject(AdminService);

  @Input() kind: 'user' | 'system' = 'user';
  @Input() allowDelete = true;

  @Output() resolved = new EventEmitter<ModelResolutionResult>();
  @Output() deleteRequested = new EventEmitter<number>();
  @Output() closed = new EventEmitter<void>();

  @ViewChild('mrdDialog') dialog?: ElementRef<HTMLDialogElement>;
  @ViewChild('mrdHeading') heading?: ElementRef<HTMLElement>;

  readonly uid = `mrd-${++nextUid}`;
  readonly customFields = CUSTOM_FIELDS;
  readonly blockerText = blockerText;

  subject: ModelResolutionSubject | null = null;
  choice: ModelResolutionChoice = 'switch';

  targets: CatalogTarget[] = [];
  targetsLoading = false;
  targetsError: string | null = null;
  targetId = '';
  targetError: string | null = null;

  preview: ModelResolutionResult | null = null;
  /** The target the preview on screen belongs to. */
  previewTargetId: string | null = null;
  previewLoading = false;
  previewError: string | null = null;

  values: Record<CustomField, string> = this.emptyValues();
  errors: Partial<Record<CustomField, string>> = {};

  busy = false;
  submitError: string | null = null;
  submitBlockers: SystemConfigBlockerDto[] = [];

  /** Bumped by every `open`, so a response to an earlier opening is ignored. */
  private generation = 0;
  private readonly previewRequests = new Subject<{ subject: ModelResolutionSubject; targetId: string } | null>();
  private readonly subscriptions = new Subscription();

  constructor() {
    this.subscriptions.add(this.previewRequests.pipe(
      switchMap(request => request ? this.fetchPreview(request.subject, request.targetId) : EMPTY)
    ).subscribe(outcome => {
      this.previewLoading = false;
      this.preview = outcome.result;
      this.previewError = outcome.error;
      this.cdr.markForCheck();
    }));
  }

  get summary(): string {
    return this.subject ? availabilitySentence(this.subject.availability, this.subject.displayName, this.subject.modelId) : '';
  }

  get radioName(): string { return `${this.uid}-action`; }

  get keepCustomHint(): string {
    return 'Overseer stops checking it against the catalog and uses the limits and prices below. '
      + `Replies fail if ${this.subject?.provider ?? 'the provider'} no longer serves it.`;
  }

  get deleteHint(): string {
    return this.kind === 'user'
      ? 'The model is removed from your list. Chats keep their history.'
      : "Next you'll see what uses this configuration before anything is deleted.";
  }

  /** Blockers in the preview of the chosen target, which make a switch wait. */
  get switchBlocked(): boolean {
    return this.choice === 'switch' && !!this.preview && this.previewTargetId === this.targetId && this.preview.blockers.length > 0;
  }

  get primaryLabel(): string {
    switch (this.choice) {
      case 'switch': return this.busy ? 'Switching…' : 'Switch model';
      case 'keepCustom': return this.busy ? 'Saving…' : 'Keep as custom model';
      default: return this.kind === 'system' ? 'Delete…' : this.busy ? 'Deleting…' : 'Delete model';
    }
  }

  get primaryDisabled(): boolean { return this.busy || this.switchBlocked; }

  targetLabel(target: CatalogTarget): string {
    const date = formatCatalogDate(target.releaseDate);
    return date ? `${target.displayName} — released ${date}` : target.displayName;
  }

  fieldId(key: CustomField): string { return `${this.uid}-${key}`; }

  fieldDescribedBy(key: CustomField): string {
    return this.errors[key] ? `${this.fieldId(key)}-hint ${this.fieldId(key)}-error` : `${this.fieldId(key)}-hint`;
  }

  /** Resets every choice and value, then opens the dialog modally with focus on its title. */
  open(subject: ModelResolutionSubject): void {
    const generation = ++this.generation;
    this.subject = subject;
    this.choice = subject.availability.replacement ? 'switch' : 'keepCustom';
    this.targets = [];
    this.targetsLoading = true;
    this.targetsError = null;
    this.targetId = '';
    this.targetError = null;
    this.resetPreview();
    this.values = this.prefill(subject.availability);
    this.errors = {};
    this.busy = false;
    this.submitError = null;
    this.submitBlockers = [];
    this.cdr.markForCheck();
    this.cdr.detectChanges();

    const dialog = this.dialog?.nativeElement;
    if (dialog && !dialog.open) {
      dialog.showModal();
    }
    this.heading?.nativeElement.focus();

    this.settings.getCatalogTargets(subject.provider).subscribe({
      next: targets => {
        if (generation !== this.generation) return;
        this.targetsLoading = false;
        this.targets = this.orderTargets(targets ?? [], subject.availability.replacement?.modelId ?? null);
        this.targetId = this.targets[0]?.modelId ?? '';
        this.cdr.markForCheck();
        this.requestPreview();
      },
      error: () => {
        if (generation !== this.generation) return;
        this.targetsLoading = false;
        this.targetsError = 'The catalog models could not be loaded.';
        this.cdr.markForCheck();
      }
    });
  }

  close(): void {
    const dialog = this.dialog?.nativeElement;
    if (dialog?.open) {
      dialog.close();
    }
  }

  onDialogClose(): void {
    this.previewRequests.next(null);
    this.closed.emit();
  }

  chooseAction(choice: ModelResolutionChoice): void {
    if (this.choice === choice) return;
    this.choice = choice;
    this.submitError = null;
    this.submitBlockers = [];
    this.cdr.markForCheck();
    this.requestPreview();
  }

  onTargetChange(event: Event): void {
    this.targetId = (event.target as HTMLSelectElement).value;
    this.targetError = null;
    this.submitError = null;
    this.submitBlockers = [];
    this.cdr.markForCheck();
    this.requestPreview();
  }

  onFieldInput(key: CustomField, event: Event): void {
    this.values = { ...this.values, [key]: (event.target as HTMLInputElement).value };
  }

  onFieldBlur(key: CustomField): void {
    const all = this.validateCustom();
    const next = { ...this.errors };
    const related: CustomField[] = key === 'inputPrice' || key === 'outputPrice' || key === 'cachedInputPrice'
      ? ['inputPrice', 'outputPrice', 'cachedInputPrice']
      : [key];
    for (const field of related) {
      // A blur shows its own field's error; the other price fields only update an error already shown.
      if (field === key || next[field]) {
        if (all[field]) next[field] = all[field]; else delete next[field];
      }
    }
    this.errors = next;
    this.cdr.markForCheck();
  }

  submit(): void {
    const subject = this.subject;
    if (!subject || this.primaryDisabled) return;
    this.submitError = null;
    this.submitBlockers = [];

    switch (this.choice) {
      case 'switch':
        if (!this.targetId) {
          this.targetError = 'Choose a catalog model.';
          this.cdr.markForCheck();
          this.focusLater(`${this.uid}-target`);
          return;
        }
        this.save(subject, { action: 'switch', targetModelId: this.targetId, dryRun: false });
        return;
      case 'keepCustom': {
        this.errors = this.validateCustom();
        const first = CUSTOM_FIELDS.find(field => this.errors[field.key]);
        if (first) {
          this.cdr.markForCheck();
          this.focusLater(this.fieldId(first.key));
          return;
        }
        this.save(subject, this.customRequest());
        return;
      }
      default:
        this.delete(subject);
    }
  }

  ngOnDestroy(): void {
    this.subscriptions.unsubscribe();
  }

  private save(subject: ModelResolutionSubject, request: ModelResolutionRequest): void {
    const generation = this.generation;
    this.busy = true;
    this.cdr.markForCheck();
    this.resolve(subject, request).subscribe({
      next: result => {
        this.resolved.emit(result);
        if (generation !== this.generation) return;
        this.busy = false;
        this.cdr.markForCheck();
        this.close();
      },
      error: (err: unknown) => {
        if (generation !== this.generation) return;
        this.busy = false;
        const refusal = this.readRefusal(err);
        this.submitError = refusal.message;
        this.submitBlockers = refusal.blockers;
        this.cdr.markForCheck();
      }
    });
  }

  private delete(subject: ModelResolutionSubject): void {
    if (this.kind === 'system') {
      this.deleteRequested.emit(subject.id);
      this.close();
      return;
    }
    const generation = this.generation;
    this.busy = true;
    this.cdr.markForCheck();
    this.settings.deleteUserModel(subject.id).subscribe({
      next: () => {
        this.resolved.emit({ changes: [], blockers: [], model: null, deleted: true });
        if (generation !== this.generation) return;
        this.busy = false;
        this.cdr.markForCheck();
        this.close();
      },
      error: (err: unknown) => {
        if (generation !== this.generation) return;
        this.busy = false;
        this.submitError = this.readRefusal(err, 'The model could not be deleted. Try again.').message;
        this.cdr.markForCheck();
      }
    });
  }

  private resolve(subject: ModelResolutionSubject, request: ModelResolutionRequest): Observable<ModelResolutionResult> {
    return this.kind === 'system'
      ? this.admin.resolveSystemConfig(subject.id, request)
      : this.settings.resolveUserModel(subject.id, request);
  }

  private requestPreview(): void {
    if (!this.subject || this.choice !== 'switch' || !this.targetId) {
      return;
    }
    if (this.previewTargetId === this.targetId && (this.preview || this.previewLoading)) {
      return;
    }
    this.previewTargetId = this.targetId;
    this.preview = null;
    this.previewError = null;
    this.previewLoading = true;
    this.cdr.markForCheck();
    this.previewRequests.next({ subject: this.subject, targetId: this.targetId });
  }

  private fetchPreview(subject: ModelResolutionSubject, targetId: string): Observable<PreviewOutcome> {
    return this.resolve(subject, { action: 'switch', targetModelId: targetId, dryRun: true }).pipe(
      map(result => ({ result, error: null })),
      catchError((err: unknown) => of({ result: null, error: this.readRefusal(err, 'The changes could not be checked.').message }))
    );
  }

  private resetPreview(): void {
    this.previewRequests.next(null);
    this.preview = null;
    this.previewTargetId = null;
    this.previewLoading = false;
    this.previewError = null;
  }

  /** The replacement first, then newest first. */
  private orderTargets(targets: CatalogTarget[], replacementId: string | null): CatalogTarget[] {
    const sorted = [...targets].sort((a, b) => (b.releaseDate ?? '').localeCompare(a.releaseDate ?? ''));
    const index = replacementId ? sorted.findIndex(target => target.modelId === replacementId) : -1;
    if (index > 0) {
      sorted.unshift(...sorted.splice(index, 1));
    }
    return sorted;
  }

  private emptyValues(): Record<CustomField, string> {
    return { maxInputTokens: '', maxOutputTokens: '', inputPrice: '', outputPrice: '', cachedInputPrice: '' };
  }

  private prefill(availability: ModelAvailability): Record<CustomField, string> {
    const suggested = availability.suggestedCustom;
    const text = (value: number | null | undefined) => value == null ? '' : String(value);
    return {
      maxInputTokens: text(suggested?.maxInputTokens),
      maxOutputTokens: text(suggested?.maxOutputTokens),
      inputPrice: text(suggested?.inputPricePerMillion),
      outputPrice: text(suggested?.outputPricePerMillion),
      cachedInputPrice: text(suggested?.cachedInputPricePerMillion)
    };
  }

  private validateCustom(): Partial<Record<CustomField, string>> {
    const errors: Partial<Record<CustomField, string>> = {};
    const value = (key: CustomField) => this.values[key].trim();
    for (const key of ['maxInputTokens', 'maxOutputTokens'] as const) {
      const text = value(key);
      if (!INTEGER.test(text) || Number(text) < 1) {
        errors[key] = 'Enter a whole number of 1 or more.';
      }
    }
    for (const key of ['inputPrice', 'outputPrice', 'cachedInputPrice'] as const) {
      const text = value(key);
      if (text && !DECIMAL.test(text)) {
        errors[key] = 'Enter a price of 0 or more, or leave it empty.';
      }
    }
    const input = value('inputPrice');
    const output = value('outputPrice');
    if (input && !output && !errors.outputPrice) {
      errors.outputPrice = 'Enter the output price too, or leave both prices empty to keep the current pricing.';
    }
    if (output && !input && !errors.inputPrice) {
      errors.inputPrice = 'Enter the input price too, or leave both prices empty to keep the current pricing.';
    }
    if (value('cachedInputPrice') && !input && !output && !errors.cachedInputPrice) {
      errors.cachedInputPrice = 'Enter the input and output prices too, or leave the cached price empty.';
    }
    return errors;
  }

  private customRequest(): ModelResolutionRequest {
    const number = (key: CustomField) => {
      const text = this.values[key].trim();
      return text ? Number(text) : undefined;
    };
    const request: ModelResolutionRequest = {
      action: 'keepCustom',
      maxInputTokens: number('maxInputTokens'),
      maxOutputTokens: number('maxOutputTokens'),
      dryRun: false
    };
    const input = number('inputPrice');
    const output = number('outputPrice');
    if (input !== undefined && output !== undefined) {
      request.inputPricePerMillion = input;
      request.outputPricePerMillion = output;
      const cached = number('cachedInputPrice');
      if (cached !== undefined) {
        request.cachedInputPricePerMillion = cached;
      }
    }
    return request;
  }

  private readRefusal(err: unknown, fallback = 'The change could not be saved. Try again.'): { message: string; blockers: SystemConfigBlockerDto[] } {
    if (err instanceof HttpErrorResponse) {
      const body = err.error as ModelResolutionRefusal | string | null;
      const blockers = typeof body === 'object' && body && Array.isArray(body.blockers) ? body.blockers : [];
      if (typeof body === 'string' && body.trim()) {
        return { message: body, blockers };
      }
      if (typeof body === 'object' && body?.message) {
        return { message: body.message, blockers };
      }
      if (err.status === 404) {
        return { message: 'This model no longer exists. Close this dialog and reload the list.', blockers };
      }
      if (err.status === 409) {
        return { message: 'This configuration is in use; try again when the work below finishes.', blockers };
      }
      return { message: fallback, blockers };
    }
    return { message: fallback, blockers: [] };
  }

  private focusLater(id: string): void {
    this.cdr.detectChanges();
    this.dialog?.nativeElement.querySelector<HTMLElement>(`#${id}`)?.focus();
  }
}
