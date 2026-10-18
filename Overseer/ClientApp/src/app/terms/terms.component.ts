import { ChangeDetectionStrategy, ChangeDetectorRef, Component, DestroyRef, OnInit, inject } from '@angular/core';
import { RouterModule } from '@angular/router';
import { Subscription } from 'rxjs';
import { MarkdownPipe } from '../chat/markdown.pipe';
import { LoadingIndicatorComponent } from '../shared/loading-indicator/loading-indicator.component';
import { OVERSEER_TERMS_KEY, TermsDocument, TermsService, formatTermsVersion, withoutLastUpdatedLine } from '../services/terms.service';

/** The title shown until the document arrives. */
const DEFAULT_TITLE = 'Gnoll Overseer Terms of Service';

/** The Gnoll Overseer Terms of Service as a public page, readable without signing in. */
@Component({
  selector: 'app-terms',
  imports: [RouterModule, MarkdownPipe, LoadingIndicatorComponent],
  templateUrl: './terms.component.html',
  styleUrl: './terms.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class TermsComponent implements OnInit {
  private readonly cdr = inject(ChangeDetectorRef);
  private readonly destroyRef = inject(DestroyRef);
  private readonly terms = inject(TermsService);
  private subscription?: Subscription;

  doc: TermsDocument | null = null;
  loading = false;
  loadError = false;

  get title(): string {
    return this.doc?.title || DEFAULT_TITLE;
  }

  /** The text below the Last updated line, which is shown separately. */
  get body(): string {
    return withoutLastUpdatedLine(this.doc?.markdown);
  }

  get lastUpdated(): string | null {
    return this.doc?.versionLabel || formatTermsVersion(this.doc?.version);
  }

  ngOnInit(): void {
    this.destroyRef.onDestroy(() => this.subscription?.unsubscribe());
    this.load(false);
  }

  retryLoad(): void {
    this.load(true);
  }

  private load(reload: boolean): void {
    this.subscription?.unsubscribe();
    this.loading = true;
    this.loadError = false;
    this.cdr.markForCheck();
    this.subscription = this.terms.getTerms(OVERSEER_TERMS_KEY, reload).subscribe({
      next: doc => {
        this.doc = doc;
        this.loading = false;
        this.cdr.markForCheck();
      },
      error: () => {
        this.loading = false;
        this.loadError = true;
        this.cdr.markForCheck();
      }
    });
  }
}
