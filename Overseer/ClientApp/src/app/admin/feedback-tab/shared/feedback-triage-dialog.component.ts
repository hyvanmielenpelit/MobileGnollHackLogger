import {
  ChangeDetectionStrategy,
  ChangeDetectorRef,
  Component,
  ElementRef,
  OnDestroy,
  ViewChild,
  inject
} from '@angular/core';
import { Observable, Subscription } from 'rxjs';
import { InfoTipComponent } from '../../../shared/info-tip/info-tip.component';
import { formatInteger } from '../../benchmark/shared/provider-availability/provider-availability';
import { RESOLVED_VIA_OPTIONS } from './feedback-labels';

/** An issue's triage as the form holds it; `status` is empty while a regressed issue has no new status chosen. */
export interface FeedbackTriageValues {
  status: string;
  note: string;
  resolution: string;
  fixedInVersion: string;
  /** *Resolved via* ids, in the canonical order. */
  resolvedVia: readonly string[];
}

/** What Save hands the host: the trimmed values, with `resolvedVia` only when it differs from the stored list. */
export interface FeedbackTriageSubmission extends Omit<FeedbackTriageValues, 'resolvedVia'> {
  /** The chosen ids in the canonical order; `[]` clears the stored list. Absent when unchanged. */
  resolvedVia?: readonly string[];
}

export interface FeedbackTriageDialogOptions {
  /** `tl-fb-42-triage` or `tl-gbfb-<id>-triage`; every element id in the dialog starts with it. */
  idPrefix: string;
  /** The issue's subject, shown under the heading. */
  subject: string;
  /** Short facts shown under the subject, such as the category and the current status. */
  details?: readonly string[];
  /** The stored triage; `status` is empty when the stored status is Regressed. */
  values: FeedbackTriageValues;
  statuses: readonly { value: string; label: string }[];
  /** The (i) text beside Status. */
  statusHelp: string;
  limits: { note: number; resolution: number; fixedInVersion: number };
  fixedInVersionPattern: RegExp;
  /** Shown under Fixed in version when it fails the pattern. */
  fixedInVersionError: string;
  /** Sends the change; the dialog closes when it completes and shows the error's message when it fails. */
  submit: (values: FeedbackTriageSubmission) => Observable<unknown>;
}

type TextField = 'note' | 'resolution' | 'fixedInVersion';

/** `requestClose()` runs the cancel handler first; not every browser has it yet. */
type ClosableDialog = HTMLDialogElement & { requestClose?: () => void };

const EMPTY_VALUES: FeedbackTriageValues = { status: '', note: '', resolution: '', fixedInVersion: '', resolvedVia: [] };

/** Ids in the canonical order, unknown ones last in their own order, without blanks or duplicates. */
function orderedResolvedVia(ids: readonly string[]): string[] {
  const unique = [...new Set(ids.map(id => id.trim()).filter(id => id !== ''))];
  const known = RESOLVED_VIA_OPTIONS.map(option => option.id as string).filter(id => unique.includes(id));
  return [...known, ...unique.filter(id => !known.includes(id))];
}

/**
 * Triage one feedback issue: the issue in a panel under the title, Status and Fixed in version side by
 * side where they fit, then Resolved via, Note and Resolution. Save checks the status and the version at their fields, then hands the trimmed values to
 * the host's `submit`; the dialog closes when it succeeds and keeps the values and the server's message
 * when it fails. Escape or Cancel on a changed form first asks to discard the changes. Used by both
 * Feedback sub-tabs.
 */
@Component({
  selector: 'app-feedback-triage-dialog',
  standalone: true,
  imports: [InfoTipComponent],
  templateUrl: './feedback-triage-dialog.component.html',
  styleUrl: './feedback-triage-dialog.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class FeedbackTriageDialogComponent implements OnDestroy {
  private readonly cdr = inject(ChangeDetectorRef);

  @ViewChild('dialog', { static: true }) dialog!: ElementRef<HTMLDialogElement>;

  readonly formatInteger = formatInteger;
  readonly resolvedViaOptions = RESOLVED_VIA_OPTIONS;

  options: FeedbackTriageDialogOptions | null = null;
  values: FeedbackTriageValues = { ...EMPTY_VALUES };
  statusError: string | null = null;
  versionError: string | null = null;
  /** The server's refusal of the last Save. */
  error: string | null = null;
  busy = false;
  /** Whether *You have unsaved changes.* and Discard changes are shown. */
  confirmingDiscard = false;

  private sub: Subscription | null = null;

  ngOnDestroy(): void {
    this.sub?.unsubscribe();
    if (this.dialog.nativeElement.open) this.dialog.nativeElement.close();
  }

  get id(): string {
    return this.options?.idPrefix ?? 'ftd';
  }

  /** Whether the form differs from the stored triage. */
  get dirty(): boolean {
    const stored = this.options?.values;
    if (!stored) return false;
    return this.values.status !== stored.status
      || this.values.note.trim() !== stored.note.trim()
      || this.values.resolution.trim() !== stored.resolution.trim()
      || this.values.fixedInVersion.trim() !== stored.fixedInVersion.trim()
      || this.resolvedViaChanged;
  }

  /** Whether the checked *Resolved via* ids differ from the stored ones, as sets. */
  get resolvedViaChanged(): boolean {
    const stored = orderedResolvedVia(this.options?.values.resolvedVia ?? []);
    const current = this.values.resolvedVia;
    return stored.length !== current.length || stored.some(id => !current.includes(id));
  }

  isResolvedVia(id: string): boolean {
    return this.values.resolvedVia.includes(id);
  }

  open(options: FeedbackTriageDialogOptions): void {
    this.sub?.unsubscribe();
    // Clear the form first, so its fields are created again with the new values.
    this.options = null;
    this.cdr.detectChanges();
    this.options = options;
    this.values = { ...options.values, resolvedVia: orderedResolvedVia(options.values.resolvedVia ?? []) };
    this.statusError = null;
    this.versionError = null;
    this.error = null;
    this.busy = false;
    this.confirmingDiscard = false;
    this.cdr.detectChanges();
    const dialog = this.dialog.nativeElement;
    if (!dialog.open) dialog.showModal();
    this.field('status')?.focus();
  }

  onStatus(event: Event): void {
    const value = (event.target as HTMLSelectElement).value;
    this.values.status = this.options?.statuses.some(s => s.value === value) ? value : '';
    if (this.values.status) this.statusError = null;
    this.confirmingDiscard = false;
  }

  onText(field: TextField, event: Event): void {
    this.values[field] = (event.target as HTMLInputElement | HTMLTextAreaElement).value;
    if (field === 'fixedInVersion') this.versionError = null;
    this.confirmingDiscard = false;
  }

  /** A *Resolved via* checkbox; the list stays in the canonical order. */
  onResolvedVia(id: string, event: Event): void {
    const checked = (event.target as HTMLInputElement).checked;
    const others = this.values.resolvedVia.filter(value => value !== id);
    this.values.resolvedVia = orderedResolvedVia(checked ? [...others, id] : others);
    this.confirmingDiscard = false;
  }

  /** Escape, or Cancel through `requestClose()`: a save in flight is not abandoned, and changes are not lost unasked. */
  onCancel(event: Event): void {
    if (this.busy || this.holdForDiscard()) event.preventDefault();
  }

  /** Cancel: the same guard as Escape. */
  cancel(): void {
    if (this.busy) return;
    const dialog = this.dialog.nativeElement as ClosableDialog;
    if (typeof dialog.requestClose === 'function') {
      dialog.requestClose();
    } else if (!this.holdForDiscard()) {
      dialog.close();
    }
  }

  discard(): void {
    if (this.busy) return;
    this.dialog.nativeElement.close();
  }

  onClose(): void {
    this.sub?.unsubscribe();
    this.sub = null;
    this.busy = false;
    this.confirmingDiscard = false;
    this.cdr.markForCheck();
  }

  /** Save, or Enter in Fixed in version. Nothing is sent while a field is invalid or a save runs. */
  submit(event?: Event): void {
    event?.preventDefault();
    const options = this.options;
    if (!options || this.busy) return;
    const status = this.values.status;
    const fixedInVersion = this.values.fixedInVersion.trim();
    this.statusError = status ? null : 'Choose a status.';
    this.versionError = fixedInVersion !== '' && !options.fixedInVersionPattern.test(fixedInVersion)
      ? options.fixedInVersionError
      : null;
    this.error = null;
    this.confirmingDiscard = false;
    if (this.statusError || this.versionError) {
      this.cdr.detectChanges();
      this.field(this.statusError ? 'status' : 'fixed')?.focus();
      return;
    }

    const values: FeedbackTriageSubmission = {
      status,
      note: this.values.note.trim().slice(0, options.limits.note),
      resolution: this.values.resolution.trim().slice(0, options.limits.resolution),
      fixedInVersion
    };
    if (this.resolvedViaChanged) values.resolvedVia = [...this.values.resolvedVia];
    this.busy = true;
    this.cdr.markForCheck();
    this.sub?.unsubscribe();
    this.sub = options.submit(values).subscribe({
      complete: () => {
        this.busy = false;
        this.dialog.nativeElement.close();
        this.cdr.markForCheck();
      },
      error: (err: unknown) => {
        this.busy = false;
        this.error = err instanceof Error ? err.message : String(err);
        this.cdr.markForCheck();
      }
    });
  }

  /** Shows the unsaved-changes prompt the first time a changed form would close; true while it holds the dialog open. */
  private holdForDiscard(): boolean {
    if (!this.dirty || this.confirmingDiscard) return false;
    this.confirmingDiscard = true;
    this.cdr.detectChanges();
    this.dialog.nativeElement.querySelector<HTMLElement>('.ftd-discard')?.focus();
    return true;
  }

  private field(name: 'status' | 'fixed'): HTMLElement | null {
    return this.dialog.nativeElement.querySelector<HTMLElement>(`#${this.id}-${name}`);
  }
}
