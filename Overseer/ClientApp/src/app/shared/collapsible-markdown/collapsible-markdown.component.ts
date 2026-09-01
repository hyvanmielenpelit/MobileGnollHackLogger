import {
  Component,
  Input,
  ElementRef,
  ViewChild,
  AfterViewInit,
  OnChanges,
  OnDestroy,
  SimpleChanges,
  ChangeDetectorRef,
  inject
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { MarkdownPipe } from '../../chat/markdown.pipe';

@Component({
  selector: 'app-collapsible-markdown',
  standalone: true,
  imports: [CommonModule, MarkdownPipe],
  templateUrl: './collapsible-markdown.component.html',
  styleUrls: ['./collapsible-markdown.component.scss']
})
export class CollapsibleMarkdownComponent implements AfterViewInit, OnChanges, OnDestroy {
  @Input() text: string | null | undefined = '';
  @Input() label = '';
  @Input() collapsedMaxHeight = 180;

  @ViewChild('contentEl') contentEl?: ElementRef<HTMLDivElement>;

  isExpanded = false;
  isOverflowing = false;

  private resizeObserver: ResizeObserver | null = null;
  private cdr = inject(ChangeDetectorRef);

  ngAfterViewInit(): void {
    if (typeof ResizeObserver !== 'undefined' && this.contentEl?.nativeElement) {
      this.resizeObserver = new ResizeObserver(() => {
        this.checkOverflow();
      });
      this.resizeObserver.observe(this.contentEl.nativeElement);
    }
    this.checkOverflow();
  }

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['text'] || changes['collapsedMaxHeight']) {
      this.checkOverflow();
    }
  }

  ngOnDestroy(): void {
    if (this.resizeObserver) {
      this.resizeObserver.disconnect();
      this.resizeObserver = null;
    }
  }

  checkOverflow(): void {
    if (!this.text) {
      this.isOverflowing = false;
      this.cdr.markForCheck();
      return;
    }

    const el = this.contentEl?.nativeElement;
    if (el) {
      if (el.scrollHeight > this.collapsedMaxHeight + 10) {
        this.isOverflowing = true;
      } else if (el.scrollHeight > 0) {
        this.isOverflowing = false;
      } else {
        // Fallback for hidden / unmeasured elements (e.g. inside a closed <dialog>)
        this.isOverflowing = this.text.length > 200 || this.text.includes('\n');
      }
    } else {
      this.isOverflowing = this.text.length > 200 || this.text.includes('\n');
    }
    this.cdr.markForCheck();
  }

  toggleExpand(): void {
    this.isExpanded = !this.isExpanded;
    this.cdr.markForCheck();
  }
}
