import { Component, ElementRef, EventEmitter, OnInit, Output, ViewChild, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ChatService, TrashSession } from '../../services/chat.service';

@Component({
  selector: 'app-trash-modal',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './trash-modal.component.html',
  styleUrls: ['./trash-modal.component.scss']
})
export class TrashModalComponent implements OnInit {
  private chatService = inject(ChatService);

  @ViewChild('trashDialog') trashDialog!: ElementRef<HTMLDialogElement>;
  @ViewChild('permanentDeleteConfirmDialog') permanentDeleteConfirmDialog!: ElementRef<HTMLDialogElement>;
  @ViewChild('emptyTrashConfirmDialog') emptyTrashConfirmDialog!: ElementRef<HTMLDialogElement>;

  @Output() sessionRestored = new EventEmitter<number>();
  @Output() trashEmptied = new EventEmitter<void>();
  @Output() trashCountChange = new EventEmitter<number>();

  trashSessions: TrashSession[] = [];
  loadingTrash = false;
  trashSessionToDeletePermanently: number | null = null;

  ngOnInit() {
    this.loadTrash();
  }

  open() {
    this.trashDialog?.nativeElement?.showModal();
    this.loadTrash();
  }

  close() {
    this.trashDialog?.nativeElement?.close();
  }

  loadTrash() {
    this.loadingTrash = true;
    this.chatService.getTrashSessions().subscribe({
      next: (sessions) => {
        this.trashSessions = sessions || [];
        this.loadingTrash = false;
        this.trashCountChange.emit(this.trashSessions.length);
      },
      error: () => {
        this.loadingTrash = false;
      }
    });
  }

  restoreTrashSession(id: number) {
    this.chatService.restoreSession(id).subscribe({
      next: () => {
        this.trashSessions = this.trashSessions.filter(s => s.id !== id);
        this.trashCountChange.emit(this.trashSessions.length);
        this.sessionRestored.emit(id);
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
