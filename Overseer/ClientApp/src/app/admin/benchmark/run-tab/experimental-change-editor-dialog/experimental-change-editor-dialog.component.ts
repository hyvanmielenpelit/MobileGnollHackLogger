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
import { Subscription } from 'rxjs';
import {
  AdminBenchmarkService,
  BenchmarkChangeOverlapDto,
  BenchmarkDescriptionEditRequest,
  BenchmarkExperimentalChangeCatalogDto,
  BenchmarkExperimentalChangeConflictReason,
  BenchmarkExperimentalChangeDto,
  BenchmarkExperimentalChangeEditRequest,
  BenchmarkExperimentalChangeLimitsDto,
  BenchmarkExperimentalChangeProductionToolDto
} from '../../../../services/admin-benchmark.service';
import { TextDiffComponent } from '../../../../shared/text-diff/text-diff.component';
import { DiffResult, diffLines } from '../../../../utils/text-diff.util';
import { ensureOverlayPolyfills } from '../../../../utils/polyfills.util';
import {
  ExperimentalChangeEditorMode,
  ExperimentalChangeEditorRequest,
  ExperimentalChangeSaved,
  changeErrorBody,
  changeErrorText,
  overlapLine
} from '../experimental-changes';

/** How long the editor waits after typing stops before it screens the texts. */
export const EXPERIMENTAL_CHANGE_SCREEN_DEBOUNCE_MS = 600;

/** The limits used until the catalog says otherwise. */
const DEFAULT_LIMITS: BenchmarkExperimentalChangeLimitsDto = { titleMax: 60, summaryMax: 220, textMax: 4000, descriptionEditsMax: 6 };

/** What one effect block of the editor is. */
export type EditorBlockKind = 'add' | 'replace' | 'instructions';

/** One effect block: a description addition, a description replacement, or the instructions. */
export interface EditorBlock {
  /** Stable for the block's lifetime; its DOM ids derive from it. */
  key: number;
  kind: EditorBlockKind;
  /** The production tool; empty for instructions or while none is chosen. */
  tool: string;
  text: string;
}

/** Each block kind's legend. */
export const EDITOR_BLOCK_LEGENDS: Readonly<Record<EditorBlockKind, string>> = {
  add: 'Added to the end of a tool\'s description',
  replace: 'Replaces a tool\'s description',
  instructions: 'Added to the end of the AI\'s instructions'
};

const SERVER_EDIT_FIELD = /^descriptionEdits\[(\d+)\]\.(text|tool)$/;

/**
 * The editor of a text-only experimental change: name it, write what it changes, check it against the
 * benchmark questions and save it. It is a sibling dialog of the picker, hosted by the run tab, so its
 * close and cancel events stop here and closing it never closes the picker. `saved` fires before it
 * closes after a save; `closed` fires however it closes, with whether a save closed it.
 */
@Component({
  selector: 'app-experimental-change-editor-dialog',
  standalone: true,
  imports: [TextDiffComponent],
  templateUrl: './experimental-change-editor-dialog.component.html',
  styleUrls: ['./experimental-change-editor-dialog.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class ExperimentalChangeEditorDialogComponent implements OnInit, OnDestroy {
  private readonly benchmarkService = inject(AdminBenchmarkService);
  private readonly cdr = inject(ChangeDetectorRef);

  @ViewChild('xceDialog', { static: true }) dialog?: ElementRef<HTMLDialogElement>;

  /** The catalog: the production tools a change may edit and the limits. */
  @Input() catalog: BenchmarkExperimentalChangeCatalogDto | null = null;

  /** A save landed; the host reloads the catalog. */
  @Output() readonly saved = new EventEmitter<ExperimentalChangeSaved>();

  /** The dialog closed; true when a save closed it. */
  @Output() readonly closed = new EventEmitter<boolean>();

  /** Reload after a 409: the host reloads the catalog and calls `reloadFrom`. */
  @Output() readonly reloadRequested = new EventEmitter<string>();

  readonly legends = EDITOR_BLOCK_LEGENDS;
  readonly separator = '\n\n';

  mode: ExperimentalChangeEditorMode = 'new';
  /** The change edited or copied; null for a new one. */
  source: BenchmarkExperimentalChangeDto | null = null;

  title = '';
  summary = '';
  expectedEffect = '';
  blocks: EditorBlock[] = [];
  confirmed = false;

  /** The fields left after an edit, whose errors then show. */
  readonly touched = new Set<string>();
  /** A save was tried: every field's error shows. */
  submitted = false;
  /** The server's field errors, by the editor's field keys. */
  fieldErrors: Record<string, string> = {};

  overlaps: BenchmarkChangeOverlapDto[] | null = null;
  screenStatus: 'idle' | 'checking' | 'done' | 'error' = 'idle';
  screenError: string | null = null;

  saving = false;
  saveError: string | null = null;
  /** A 409 or 404: the message, and the reason when the server gave one. */
  conflict: { message: string; reason: BenchmarkExperimentalChangeConflictReason | null } | null = null;
  /** Cancel, the close button or Escape with edits made asks first. */
  discardPrompt = false;

  private initialSnapshot = '';
  private initialTexts = '';
  private nextKey = 1;
  private closingAfterSave = false;
  private screenTimer: ReturnType<typeof setTimeout> | null = null;
  private screenSubscription: Subscription | null = null;
  private saveSubscription: Subscription | null = null;
  private readonly diffCache = new Map<number, { tool: string; text: string; live: string; diff: DiffResult }>();

  ngOnInit(): void {
    // The close button carries an interest-triggered tooltip.
    ensureOverlayPolyfills();
  }

  ngOnDestroy(): void {
    this.cancelScreen();
    this.saveSubscription?.unsubscribe();
  }

  // --- Opening and closing ---

  /** Shows the editor for a new change, an edit, or a new version, and focuses Title. */
  open(request: ExperimentalChangeEditorRequest): void {
    this.mode = request.mode;
    this.source = request.change;
    const change = request.change;
    this.title = change?.title ?? '';
    this.summary = change?.summary ?? '';
    this.expectedEffect = change?.expectedEffect ?? '';
    this.blocks = [];
    this.nextKey = 1;
    for (const edit of change?.descriptionEdits ?? []) {
      this.blocks.push({ key: this.nextKey++, kind: edit.mode === 'replace' ? 'replace' : 'add', tool: edit.tool, text: edit.text });
    }
    if (change?.instructions?.trim()) {
      this.blocks.push({ key: this.nextKey++, kind: 'instructions', tool: '', text: change.instructions });
    }
    this.confirmed = false;
    this.touched.clear();
    this.submitted = false;
    this.fieldErrors = {};
    this.saving = false;
    this.saveError = null;
    this.conflict = null;
    this.discardPrompt = false;
    this.closingAfterSave = false;
    this.diffCache.clear();
    this.overlaps = null;
    this.screenStatus = 'idle';
    this.screenError = null;
    this.saveSubscription?.unsubscribe();
    this.initialSnapshot = this.snapshot();
    this.initialTexts = this.textsKey();
    this.scheduleScreen();

    this.cdr.markForCheck();
    this.cdr.detectChanges();
    const dialog = this.dialog?.nativeElement;
    if (dialog && !dialog.open) {
      dialog.showModal();
    }
    dialog?.querySelector<HTMLElement>('#xceTitle')?.focus();
  }

  /** Cancel and the close button: closes, or asks first when there are edits. */
  requestClose(): void {
    if (this.dirty && !this.saving) {
      this.showDiscardPrompt();
      return;
    }
    this.dialog?.nativeElement.close();
  }

  /** Escape: the same as Cancel; the event goes no further than this dialog. */
  onCancel(event: Event): void {
    event.stopPropagation();
    if (this.dirty && !this.saving) {
      event.preventDefault();
      this.showDiscardPrompt();
    }
  }

  /** The dialog's close event, however it closed; it goes no further than this dialog. */
  onClose(event: Event): void {
    event.stopPropagation();
    this.cancelScreen();
    const saved = this.closingAfterSave;
    this.closingAfterSave = false;
    this.discardPrompt = false;
    this.closed.emit(saved);
  }

  keepEditing(): void {
    this.discardPrompt = false;
    this.cdr.markForCheck();
    this.cdr.detectChanges();
    this.dialog?.nativeElement.querySelector<HTMLElement>('#xceCancel')?.focus();
  }

  discard(): void {
    this.discardPrompt = false;
    this.dialog?.nativeElement.close();
  }

  /** The locked banner's Copy as new version: the editor starts again as a new version of the change. */
  copyAsNewVersion(): void {
    if (!this.source) return;
    this.open({ mode: 'copy', change: this.source });
  }

  /** After a 409's Reload: the editor starts again on the change as it now stands. */
  reloadFrom(catalog: BenchmarkExperimentalChangeCatalogDto | null): void {
    const id = this.source?.id;
    const change = id ? catalog?.changes.find(c => c.id === id) ?? null : null;
    if (!change) {
      this.conflict = { message: 'This change no longer exists.', reason: null };
    } else if (change.source === 'builtIn') {
      this.conflict = { message: 'This change is now built in; change it in the repository.', reason: 'builtIn' };
    } else {
      this.open({ mode: 'edit', change });
      return;
    }
    this.cdr.markForCheck();
  }

  reload(): void {
    if (this.source) this.reloadRequested.emit(this.source.id);
  }

  // --- Editing ---

  onTitleInput(event: Event): void {
    this.title = (event.target as HTMLInputElement).value;
    delete this.fieldErrors['title'];
  }

  onSummaryInput(event: Event): void {
    this.summary = (event.target as HTMLTextAreaElement).value;
    delete this.fieldErrors['summary'];
  }

  onExpectedInput(event: Event): void {
    this.expectedEffect = (event.target as HTMLInputElement).value;
    delete this.fieldErrors['expectedEffect'];
  }

  markTouched(field: string): void {
    this.touched.add(field);
  }

  onBlockText(block: EditorBlock, event: Event): void {
    block.text = (event.target as HTMLTextAreaElement).value;
    delete this.fieldErrors[`block-${block.key}-text`];
    this.textsChanged();
  }

  /** A tool chosen; an empty replacement starts as the tool's live description. */
  onBlockTool(block: EditorBlock, event: Event): void {
    block.tool = (event.target as HTMLSelectElement).value;
    delete this.fieldErrors[`block-${block.key}-tool`];
    if (block.kind === 'replace' && block.text.trim() === '') {
      block.text = this.liveDescription(block.tool);
    }
    this.textsChanged();
  }

  addBlock(kind: EditorBlockKind): void {
    if (this.locked) return;
    if (kind === 'instructions' ? !this.canAddInstructions : !this.canAddDescription) return;
    const block: EditorBlock = { key: this.nextKey++, kind, tool: '', text: '' };
    this.blocks = [...this.blocks, block];
    delete this.fieldErrors['effects'];
    this.textsChanged();
    this.cdr.detectChanges();
    const target = kind === 'instructions' ? `xce-block-${block.key}-text` : `xce-block-${block.key}-tool`;
    this.dialog?.nativeElement.querySelector<HTMLElement>(`#${target}`)?.focus();
  }

  removeBlock(block: EditorBlock): void {
    if (this.locked) return;
    this.blocks = this.blocks.filter(b => b !== block);
    this.diffCache.delete(block.key);
    delete this.fieldErrors[`block-${block.key}-tool`];
    delete this.fieldErrors[`block-${block.key}-text`];
    this.textsChanged();
    this.cdr.detectChanges();
    const next = this.dialog?.nativeElement.querySelector<HTMLElement>('.xce-add')
      ?? this.dialog?.nativeElement.querySelector<HTMLElement>('#xceTitle');
    next?.focus();
  }

  onConfirmChange(event: Event): void {
    this.confirmed = (event.target as HTMLInputElement).checked;
    delete this.fieldErrors['genericConfirmed'];
  }

  /**
   * Save: refused with every reason shown and focus on the first invalid field; else sends the change.
   * After a 409 it waits for Reload.
   */
  save(): void {
    if (this.saving || this.conflict) return;
    if (this.saveRefusal) {
      this.submitted = true;
      this.cdr.markForCheck();
      this.cdr.detectChanges();
      this.focusFirstInvalid();
      return;
    }
    const request = this.buildRequest();
    const source = this.source;
    const created = this.mode !== 'edit';
    this.saving = true;
    this.saveError = null;
    this.conflict = null;
    this.fieldErrors = {};
    this.cdr.markForCheck();
    const call = !created && source
      ? this.benchmarkService.updateExperimentalChange(source.id, { ...request, expectedUpdatedAtUtc: source.updatedAtUtc ?? null })
      : this.benchmarkService.createExperimentalChange(request);
    this.saveSubscription?.unsubscribe();
    this.saveSubscription = call.subscribe({
      next: result => {
        this.saving = false;
        this.saved.emit({ change: result.change, created });
        this.closingAfterSave = true;
        this.dialog?.nativeElement.close();
        this.cdr.markForCheck();
      },
      error: err => {
        this.saving = false;
        this.applySaveError(err);
        this.cdr.markForCheck();
        this.cdr.detectChanges();
        if (Object.keys(this.fieldErrors).length > 0) this.focusFirstInvalid();
      }
    });
  }

  // --- What the template reads ---

  get heading(): string {
    if (this.mode === 'edit' && this.source) return `Edit ${this.source.title}`;
    if (this.mode === 'copy' && this.source) return `New version of ${this.source.title}`;
    return 'New experimental change';
  }

  /** Editing a change a run has used: its texts are read-only. */
  get locked(): boolean {
    return this.mode === 'edit' && !!this.source && !this.source.canEditText;
  }

  get limits(): BenchmarkExperimentalChangeLimitsDto {
    return this.catalog?.limits ?? DEFAULT_LIMITS;
  }

  get productionTools(): BenchmarkExperimentalChangeProductionToolDto[] {
    return this.catalog?.productionTools ?? [];
  }

  get descriptionBlocks(): EditorBlock[] {
    return this.blocks.filter(b => b.kind !== 'instructions');
  }

  get canAddDescription(): boolean {
    return !this.locked
      && this.descriptionBlocks.length < this.limits.descriptionEditsMax
      && this.productionTools.some(t => !this.descriptionBlocks.some(b => b.tool === t.name));
  }

  get canAddInstructions(): boolean {
    return !this.locked && !this.blocks.some(b => b.kind === 'instructions');
  }

  /** The production tools a block may choose: those no other block of this change uses. */
  toolOptions(block: EditorBlock): BenchmarkExperimentalChangeProductionToolDto[] {
    const taken = new Set(this.descriptionBlocks.filter(b => b !== block).map(b => b.tool));
    return this.productionTools.filter(t => !taken.has(t.name));
  }

  toolOptionText(tool: BenchmarkExperimentalChangeProductionToolDto): string {
    return tool.withheldUnderSourceOnly ? `${tool.name} (not offered under Source code only)` : tool.name;
  }

  isWithheld(toolName: string): boolean {
    return this.productionTools.find(t => t.name === toolName)?.withheldUnderSourceOnly === true;
  }

  liveDescription(toolName: string): string {
    return this.productionTools.find(t => t.name === toolName)?.description ?? '';
  }

  /** A replacement against the live description, cached while neither changes. */
  diffFor(block: EditorBlock): DiffResult | null {
    if (block.kind !== 'replace' || !block.tool) return null;
    const live = this.liveDescription(block.tool);
    const cached = this.diffCache.get(block.key);
    if (cached && cached.tool === block.tool && cached.text === block.text && cached.live === live) return cached.diff;
    const diff = diffLines(live, block.text);
    this.diffCache.set(block.key, { tool: block.tool, text: block.text, live, diff });
    return diff;
  }

  /** The texts differ from the stored change, for an edit; always true for a new change or version. */
  get textChanged(): boolean {
    return this.mode !== 'edit' || this.textsKey() !== this.initialTexts;
  }

  /** The confirmation is asked on every save that creates or changes text. */
  get confirmationRequired(): boolean {
    return !this.locked && this.textChanged;
  }

  get dirty(): boolean {
    return this.snapshot() !== this.initialSnapshot;
  }

  /** Why Save waits; null when it can send. */
  get saveRefusal(): string | null {
    if (this.saving) return 'Saving…';
    if (this.title.trim() === '') return 'Write a title.';
    if (this.summary.trim() === '') return 'Write a summary.';
    if (this.blocks.length === 0) return 'Add at least one change: an addition to a tool description, a replacement, or instructions.';
    if (this.descriptionBlocks.some(b => b.tool === '')) return 'Choose the tool of each description block.';
    if (this.blocks.some(b => b.text.trim() === '')) return 'Write the text of each block.';
    if (this.confirmationRequired && !this.confirmed) return 'Tick the confirmation that the text states no game fact, answer or hint.';
    return null;
  }

  /** The error shown under a field: the server's, else the editor's once the field was left or a save tried. */
  errorFor(field: string): string | null {
    const server = this.fieldErrors[field];
    if (server) return server;
    if (!this.submitted && !this.touched.has(field)) return null;
    return this.clientError(field);
  }

  /** A field's `aria-describedby`: its count and hint ids, then its error while one shows. */
  describedBy(field: string, ...ids: string[]): string | null {
    const all = ids.filter(id => id !== '');
    if (this.errorFor(field)) all.push(`${this.fieldDomId(field)}-error`);
    return all.length > 0 ? all.join(' ') : null;
  }

  fieldDomId(field: string): string {
    switch (field) {
      case 'title': return 'xceTitle';
      case 'summary': return 'xceSummary';
      case 'expectedEffect': return 'xceExpected';
      case 'effects': return 'xceEffects';
      case 'genericConfirmed': return 'xceConfirm';
      default: return `xce-${field}`;
    }
  }

  overlapText(overlap: BenchmarkChangeOverlapDto): string {
    return overlapLine(overlap);
  }

  hasText(): boolean {
    return this.blocks.some(b => b.text.trim() !== '');
  }

  // --- Internals ---

  private clientError(field: string): string | null {
    switch (field) {
      case 'title': return this.title.trim() === '' ? 'Write a title.' : null;
      case 'summary': return this.summary.trim() === '' ? 'Write a summary.' : null;
      case 'effects': return this.blocks.length === 0 ? 'Add at least one change.' : null;
      case 'genericConfirmed':
        return this.submitted && this.confirmationRequired && !this.confirmed ? 'Tick the confirmation to save.' : null;
    }
    const match = /^block-(\d+)-(tool|text)$/.exec(field);
    if (!match) return null;
    const block = this.blocks.find(b => b.key === Number(match[1]));
    if (!block) return null;
    if (match[2] === 'tool') return block.kind !== 'instructions' && block.tool === '' ? 'Choose a tool.' : null;
    return block.text.trim() === '' ? 'Write the text.' : null;
  }

  /** The fields in the order they appear, for focusing the first invalid one. */
  private fieldOrder(): string[] {
    const fields = ['title', 'summary', 'expectedEffect'];
    for (const block of this.blocks) {
      if (block.kind !== 'instructions') fields.push(`block-${block.key}-tool`);
      fields.push(`block-${block.key}-text`);
    }
    fields.push('effects', 'genericConfirmed');
    return fields;
  }

  private focusFirstInvalid(): void {
    const field = this.fieldOrder().find(f => this.errorFor(f));
    if (!field) return;
    const root = this.dialog?.nativeElement;
    const target = field === 'effects'
      ? root?.querySelector<HTMLElement>('.xce-add')
      : root?.querySelector<HTMLElement>(`#${this.fieldDomId(field)}`);
    target?.focus();
  }

  private showDiscardPrompt(): void {
    this.discardPrompt = true;
    this.cdr.markForCheck();
    this.cdr.detectChanges();
    this.dialog?.nativeElement.querySelector<HTMLElement>('#xceKeepEditing')?.focus();
  }

  private textsChanged(): void {
    this.confirmed = false;
    this.scheduleScreen();
    this.cdr.markForCheck();
  }

  private buildRequest(): BenchmarkExperimentalChangeEditRequest {
    const instructions = this.blocks.find(b => b.kind === 'instructions');
    return {
      title: this.title.trim(),
      summary: this.summary.trim(),
      expectedEffect: this.expectedEffect.trim() || null,
      descriptionEdits: this.descriptionBlocks.map((b): BenchmarkDescriptionEditRequest => ({
        tool: b.tool, mode: b.kind === 'replace' ? 'replace' : 'add', text: b.text
      })),
      instructions: instructions ? instructions.text : null,
      copiedFrom: this.mode === 'copy' ? this.source?.id ?? null : this.mode === 'edit' ? this.source?.copiedFrom ?? null : null,
      genericConfirmed: this.confirmationRequired ? this.confirmed : false
    };
  }

  private applySaveError(err: unknown): void {
    const status = (err as { status?: number } | null)?.status;
    const body = changeErrorBody(err);
    if (status === 409 || status === 404) {
      this.conflict = {
        message: body?.error || (status === 404 ? 'This change no longer exists.' : 'The change was changed elsewhere.'),
        reason: body?.reason ?? null
      };
      return;
    }
    if (status === 400 && body?.fieldErrors) {
      const descriptionBlocks = this.descriptionBlocks;
      const instructions = this.blocks.find(b => b.kind === 'instructions');
      const mapped: Record<string, string> = {};
      for (const [field, message] of Object.entries(body.fieldErrors)) {
        const edit = SERVER_EDIT_FIELD.exec(field);
        if (edit) {
          const block = descriptionBlocks[Number(edit[1])];
          mapped[block ? `block-${block.key}-${edit[2]}` : 'effects'] = message;
        } else if (field === 'instructions') {
          mapped[instructions ? `block-${instructions.key}-text` : 'effects'] = message;
        } else {
          mapped[field] = message;
        }
      }
      this.fieldErrors = mapped;
      this.submitted = true;
      this.saveError = body.error || null;
      return;
    }
    this.saveError = changeErrorText(err, 'The change could not be saved.');
  }

  private snapshot(): string {
    return JSON.stringify([this.title, this.summary, this.expectedEffect, this.blocks.map(b => [b.kind, b.tool, b.text])]);
  }

  private textsKey(): string {
    return JSON.stringify(this.blocks.map(b => [b.kind, b.tool, b.text.replace(/\r\n?/g, '\n').trim()]));
  }

  /** Screens the texts once typing has paused; clears the findings when there is no text. */
  private scheduleScreen(): void {
    if (this.screenTimer != null) {
      clearTimeout(this.screenTimer);
      this.screenTimer = null;
    }
    const texts = this.blocks.map(b => b.text.trim()).filter(t => t !== '');
    if (texts.length === 0) {
      this.screenSubscription?.unsubscribe();
      this.screenSubscription = null;
      this.overlaps = null;
      this.screenStatus = 'idle';
      this.screenError = null;
      return;
    }
    this.screenTimer = setTimeout(() => {
      this.screenTimer = null;
      this.screenSubscription?.unsubscribe();
      this.screenStatus = 'checking';
      this.cdr.markForCheck();
      this.screenSubscription = this.benchmarkService.screenExperimentalChangeTexts(texts).subscribe({
        next: result => {
          this.overlaps = result?.overlaps ?? [];
          this.screenStatus = 'done';
          this.screenError = null;
          this.cdr.markForCheck();
        },
        error: err => {
          this.screenStatus = 'error';
          this.screenError = changeErrorText(err, 'The overlap check could not run.');
          this.cdr.markForCheck();
        }
      });
    }, EXPERIMENTAL_CHANGE_SCREEN_DEBOUNCE_MS);
  }

  private cancelScreen(): void {
    if (this.screenTimer != null) {
      clearTimeout(this.screenTimer);
      this.screenTimer = null;
    }
    this.screenSubscription?.unsubscribe();
    this.screenSubscription = null;
  }
}
