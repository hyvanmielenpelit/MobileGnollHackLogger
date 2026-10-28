import {
  ChangeDetectionStrategy,
  ChangeDetectorRef,
  Component,
  Input,
  OnDestroy,
  inject
} from '@angular/core';
import { Subscription } from 'rxjs';
import {
  FeedbackFormatExample,
  FeedbackFormatExamples,
  FeedbackTransferService
} from '../../../services/feedback-transfer.service';
import { adminApiError } from '../../../services/admin-users.service';
import { InfoTipComponent } from '../../../shared/info-tip/info-tip.component';
import { downloadTextFile } from '../../../utils/download.util';
import { RESOLVED_VIA_OPTIONS } from './feedback-labels';
import type { FeedbackSource } from './feedback-view-storage';

/** The resolution message of one issue, as the `server-feedback-resolution` skill writes it; an invented example. */
function resolutionMessageExample(source: FeedbackSource): string {
  const subject = source === 'gnollbench' ? 'GnollBench feedback 3f2a9c1b4d5e' : 'Feedback #42';
  return [
    `${subject}: Add the dilithium crystal's base price to the GnollHack wiki`,
    '',
    'Resolved via: GnollHack wiki',
    'Changed: Items/Dilithium crystal.md, the base price, validated against src/objects.c.',
    'Verified: the wiki validation session passed; wiki_search finds the price.',
    'Live: about ten minutes after the wiki clone on the server is pulled.'
  ].join('\n');
}

/**
 * The (i) at the end of the selection bar's actions: a dialog on the feedback file formats, the issues file an
 * export writes, the resolution file an import reads, and the resolution message that closes one issue. The two
 * example files come from the server, built by the real builders, the first time the dialog opens; each downloads
 * with its own media type.
 */
@Component({
  selector: 'app-feedback-formats-tip',
  standalone: true,
  imports: [InfoTipComponent],
  templateUrl: './feedback-formats-tip.component.html',
  styleUrls: ['./feedback-formats-tip.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class FeedbackFormatsTipComponent implements OnDestroy {
  private transfer = inject(FeedbackTransferService);
  private cdr = inject(ChangeDetectorRef);

  @Input({ required: true }) source!: FeedbackSource;

  readonly resolvedViaOptions = RESOLVED_VIA_OPTIONS;

  examples: FeedbackFormatExamples | null = null;
  loading = false;
  error: string | null = null;

  private loadedSource: FeedbackSource | null = null;
  private loadSub: Subscription | undefined;

  get tipId(): string {
    return `${this.source}-feedback-formats`;
  }

  get messageExample(): string {
    return resolutionMessageExample(this.source);
  }

  /** The status line's text while the examples load. */
  get statusText(): string {
    return this.loading ? 'Loading examples…' : '';
  }

  /** The dialog opened: the examples load the first time, and again only after a failure. */
  onOpened(): void {
    if (this.loading || (this.examples && this.loadedSource === this.source)) return;
    this.load();
  }

  retry(): void {
    if (this.loading) return;
    this.load();
  }

  download(example: FeedbackFormatExample): void {
    downloadTextFile(example.fileName, example.content, example.mediaType);
  }

  private load(): void {
    const source = this.source;
    this.loadSub?.unsubscribe();
    this.loading = true;
    this.error = null;
    this.examples = null;
    this.cdr.markForCheck();
    this.loadSub = this.transfer.formatExamples(source).subscribe({
      next: examples => {
        this.loading = false;
        this.examples = examples;
        this.loadedSource = source;
        this.cdr.markForCheck();
      },
      error: err => {
        this.loading = false;
        this.error = 'The examples could not be loaded: ' + adminApiError(err).message;
        this.cdr.markForCheck();
      }
    });
  }

  ngOnDestroy(): void {
    this.loadSub?.unsubscribe();
  }
}
