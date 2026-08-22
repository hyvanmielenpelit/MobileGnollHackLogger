import { Component, ElementRef, EventEmitter, Input, OnDestroy, OnInit, Output, ViewChild, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ChatService, TrashSession } from '../../services/chat.service';
import { Subject, Subscription, debounceTime, distinctUntilChanged } from 'rxjs';

@Component({
  selector: 'app-trash-modal',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './trash-modal.component.html',
  styleUrls: ['./trash-modal.component.scss']
})
export class TrashModalComponent implements OnInit, OnDestroy {
  private chatService = inject(ChatService);

  @ViewChild('trashDialog') trashDialog!: ElementRef<HTMLDialogElement>;
  @ViewChild('permanentDeleteConfirmDialog') permanentDeleteConfirmDialog!: ElementRef<HTMLDialogElement>;
  @ViewChild('emptyTrashConfirmDialog') emptyTrashConfirmDialog!: ElementRef<HTMLDialogElement>;

  @Input() activeSessionCount: number = 0;
  @Input() maxQuota: number = 50;

  @Output() sessionRestored = new EventEmitter<number>();
  @Output() trashEmptied = new EventEmitter<void>();
  @Output() trashCountChange = new EventEmitter<number>();
  @Output() restoreError = new EventEmitter<string>();

  trashSessions: TrashSession[] = [];
  loadingTrash = false;
  trashSessionToDeletePermanently: number | null = null;

  trashSearchQuery = '';
  private trashSearchSubject = new Subject<string>();
  private trashSearchSub: Subscription | null = null;

  get isAtMaxQuota(): boolean {
    return this.activeSessionCount >= this.maxQuota;
  }

  ngOnInit() {
    this.trashSearchSub = this.trashSearchSubject.pipe(
      debounceTime(300),
      distinctUntilChanged()
    ).subscribe(() => {
      this.loadTrash();
    });
    this.loadTrash();
  }

  ngOnDestroy() {
    this.trashSearchSub?.unsubscribe();
  }

  open() {
    this.trashDialog?.nativeElement?.showModal();
    this.loadTrash();
  }

  close() {
    this.trashDialog?.nativeElement?.close();
  }

  onTrashSearchInput(e: Event) {
    const value = (e.target as HTMLInputElement).value;
    this.trashSearchQuery = value;
    this.trashSearchSubject.next(value);
  }

  clearTrashSearch() {
    if (!this.trashSearchQuery) return;
    this.trashSearchQuery = '';
    this.trashSearchSubject.next('');
  }

  loadTrash() {
    this.loadingTrash = true;
    this.chatService.getTrashSessions(this.trashSearchQuery).subscribe({
      next: (sessions) => {
        this.trashSessions = sessions || [];
        this.loadingTrash = false;
        if (!this.trashSearchQuery) {
          this.trashCountChange.emit(this.trashSessions.length);
        }
      },
      error: () => {
        this.loadingTrash = false;
      }
    });
  }

  restoreTrashSession(id: number) {
    if (this.isAtMaxQuota) return;
    this.chatService.restoreSession(id).subscribe({
      next: () => {
        this.trashSessions = this.trashSessions.filter(s => s.id !== id);
        this.trashCountChange.emit(this.trashSessions.length);
        this.sessionRestored.emit(id);
      },
      error: (err) => {
        const msg = err.error?.message || 'Cannot restore chat because active chat quota is full.';
        this.restoreError.emit(msg);
      }
    });
  }

  requestPermanentDelete(id: number) {
    this.trashSessionToDeletePermanently = id;
    this.permanentDeleteConfirmDialog?.nativeElement?.showModal();
  }

  confirmPermanentDelete() {
    if (this.trashSessionToDeletePermanently === null) return;
    const id = this.trashSessionToDeletePermanently;
    this.chatService.permanentDeleteSession(id).subscribe({
      next: () => {
        this.trashSessions = this.trashSessions.filter(s => s.id !== id);
        this.trashSessionToDeletePermanently = null;
        this.trashCountChange.emit(this.trashSessions.length);
        this.permanentDeleteConfirmDialog?.nativeElement?.close();
      },
      error: () => {
        this.permanentDeleteConfirmDialog?.nativeElement?.close();
      }
    });
  }

  requestEmptyTrash() {
    if (this.trashSessions.length === 0) return;
    this.emptyTrashConfirmDialog?.nativeElement?.showModal();
  }

  confirmEmptyTrash() {
    this.chatService.emptyTrash().subscribe({
      next: () => {
        this.trashSessions = [];
        this.trashCountChange.emit(0);
        this.emptyTrashConfirmDialog?.nativeElement?.close();
        this.trashEmptied.emit();
      },
      error: () => {
        this.emptyTrashConfirmDialog?.nativeElement?.close();
      }
    });
  }
}
