import {
  ChangeDetectionStrategy,
  ChangeDetectorRef,
  Component,
  EventEmitter,
  Input,
  OnChanges,
  OnDestroy,
  Output,
  SimpleChanges,
  inject
} from '@angular/core';
import { Subscription } from 'rxjs';

import { AdminChatConsistencyService, ccErrorText } from '../../../../services/admin-chat-consistency.service';
import { annotationKindText, formatUtcDateTime, utcDateTimeInputToIso } from '../chat-consistency-format';
import { CC_ANNOTATION_KINDS, CcAnnotation, CcAnnotationKind, CcModelAxis } from '../chat-consistency.models';

/** The server's field limits (`ChatConsistencyAnnotationRequest`). */
export const CC_ANNOTATION_LIMITS = Object.freeze({ text: 1000, provider: 64, modelId: 128, sourceUrl: 512 });

/**
 * The annotations that apply to the selected model, oldest first, with Delete, and the form that
 * adds one: when (UTC), provider, model id, kind, text and source.
 */
@Component({
  selector: 'app-cc-annotations-panel',
  standalone: true,
  templateUrl: './annotations-panel.component.html',
  styleUrls: ['./annotations-panel.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class CcAnnotationsPanelComponent implements OnChanges, OnDestroy {
  private readonly service = inject(AdminChatConsistencyService);
  private readonly cdr = inject(ChangeDetectorRef);

  @Input() axis: CcModelAxis | null = null;

  /** An annotation was added or deleted; the timeline is read again. */
  @Output() readonly changed = new EventEmitter<void>();

  readonly kinds = CC_ANNOTATION_KINDS;
  readonly limits = CC_ANNOTATION_LIMITS;

  annotations: CcAnnotation[] = [];
  loading = false;
  listError: string | null = null;

  atLocal = '';
  provider = '';
  modelId = '';
  kind: CcAnnotationKind = 'modelRelease';
  text = '';
  sourceUrl = '';
  adding = false;
  addError: string | null = null;
  deleteError: string | null = null;
  announcement = '';

  private listSub: Subscription | null = null;
  private addSub: Subscription | null = null;
  private deleteSub: Subscription | null = null;

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['axis'] && changes['axis'].previousValue?.key !== this.axis?.key) {
      this.provider = this.axis?.provider ?? '';
      this.modelId = this.axis?.modelId ?? '';
      this.addError = null;
      this.deleteError = null;
      this.announcement = '';
      this.load();
    }
  }

  ngOnDestroy(): void {
    for (const sub of [this.listSub, this.addSub, this.deleteSub]) sub?.unsubscribe();
  }

  load(): void {
    const axis = this.axis;
    this.listSub?.unsubscribe();
    if (!axis) {
      this.annotations = [];
      this.loading = false;
      return;
    }
    this.loading = true;
    this.listError = null;
    this.listSub = this.service.listAnnotations(axis.provider, axis.modelId).subscribe({
      next: annotations => {
        this.loading = false;
        this.annotations = annotations;
        this.cdr.markForCheck();
      },
      error: err => {
        this.loading = false;
        this.listError = ccErrorText(err, 'The annotations could not be loaded.');
        this.cdr.markForCheck();
      }
    });
  }

  kindLabel(kind: CcAnnotationKind): string {
    return annotationKindText(kind);
  }

  when(annotation: CcAnnotation): string {
    return formatUtcDateTime(annotation.atUtc);
  }

  appliesTo(annotation: CcAnnotation): string {
    if (!annotation.provider) return 'Every provider';
    return annotation.modelId ? `${annotation.provider} · ${annotation.modelId}` : `${annotation.provider} · every model`;
  }

  /** A link only for an http or https URL; anything else is shown as text. */
  isLink(url: string | null): boolean {
    return !!url && /^https?:\/\//i.test(url);
  }

  onField(field: 'atLocal' | 'provider' | 'modelId' | 'text' | 'sourceUrl', event: Event): void {
    this[field] = (event.target as HTMLInputElement | HTMLTextAreaElement).value;
    this.addError = null;
  }

  onKind(event: Event): void {
    this.kind = (event.target as HTMLSelectElement).value as CcAnnotationKind;
  }

  /** Why Add is refused, checked on the client before the server checks again. */
  get addRefusal(): string {
    if (!utcDateTimeInputToIso(this.atLocal)) return 'Enter the date and time (UTC).';
    if (!this.text.trim()) return 'Enter the annotation text.';
    if (this.text.length > CC_ANNOTATION_LIMITS.text) return `The text is longer than ${CC_ANNOTATION_LIMITS.text} characters.`;
    const url = this.sourceUrl.trim();
    if (url && !/^https?:\/\/\S+$/i.test(url)) return 'The source must be an http or https URL.';
    return '';
  }

  add(): void {
    if (this.adding) return;
    const refusal = this.addRefusal;
    if (refusal) {
      this.addError = refusal;
      this.cdr.markForCheck();
      return;
    }
    this.adding = true;
    this.addError = null;
    this.cdr.markForCheck();
    this.addSub?.unsubscribe();
    this.addSub = this.service.addAnnotation({
      atUtc: utcDateTimeInputToIso(this.atLocal)!,
      provider: this.provider.trim() || null,
      modelId: this.modelId.trim() || null,
      kind: this.kind,
      text: this.text.trim(),
      sourceUrl: this.sourceUrl.trim() || null
    }).subscribe({
      next: () => {
        this.adding = false;
        this.text = '';
        this.sourceUrl = '';
        this.announcement = 'The annotation was added.';
        this.load();
        this.changed.emit();
        this.cdr.markForCheck();
      },
      error: err => {
        this.adding = false;
        this.addError = ccErrorText(err, 'The annotation could not be added.');
        this.cdr.markForCheck();
      }
    });
  }

  remove(annotation: CcAnnotation): void {
    this.deleteError = null;
    this.deleteSub?.unsubscribe();
    this.deleteSub = this.service.deleteAnnotation(annotation.id).subscribe({
      next: () => {
        this.annotations = this.annotations.filter(a => a.id !== annotation.id);
        this.announcement = `The annotation of ${this.when(annotation)} was deleted.`;
        this.changed.emit();
        this.cdr.markForCheck();
      },
      error: err => {
        this.deleteError = ccErrorText(err, 'The annotation could not be deleted.');
        this.cdr.markForCheck();
      }
    });
  }
}
