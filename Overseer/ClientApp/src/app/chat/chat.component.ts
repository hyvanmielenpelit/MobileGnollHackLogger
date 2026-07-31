import { Component, OnInit, OnDestroy, inject, ChangeDetectorRef, ViewChild, ElementRef, HostListener, NgZone } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ChatService, ChatSession, ChatMessage } from '../services/chat.service';
import { AuthService } from '../services/auth.service';
import { DebugService } from '../services/debug.service';
import { Router, ActivatedRoute, RouterModule } from '@angular/router';
import { MarkdownPipe } from './markdown.pipe';
import { RelativeTimePipe } from './relative-time.pipe';
import { SettingsService } from '../services/settings.service';
import * as signalR from '@microsoft/signalr';

@Component({
  selector: 'app-chat',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterModule, MarkdownPipe, RelativeTimePipe],
  styleUrl: './chat.component.scss',
  templateUrl: './chat.component.html'
})
export class ChatComponent implements OnInit, OnDestroy {
  chatService = inject(ChatService);
  settingsService = inject(SettingsService);
  
  @ViewChild('messagesContainer') messagesContainer!: ElementRef;
  @ViewChild('deleteConfirmDialog') deleteConfirmDialog!: ElementRef<HTMLDialogElement>;
  @ViewChild('imagePreviewDialog') imagePreviewDialog!: ElementRef<HTMLDialogElement>;
  @ViewChild('errorToast') errorToast!: ElementRef<HTMLElement>;
  autoScrollEnabled = true;

  private hubConnection: signalR.HubConnection | null = null;

  sidebarWidth = 250;
  isResizing = false;
  isSidebarOpen = false;
  private resizeStartX = 0;
  private resizeStartWidth = 0;
  private mouseMoveListener: ((e: MouseEvent | TouchEvent) => void) | null = null;
  private mouseUpListener: (() => void) | null = null;
  private animationFrameId: number | null = null;
  
  @ViewChild('sidebar') sidebarEl!: ElementRef<HTMLElement>;
  ngZone = inject(NgZone);
  authService = inject(AuthService);
  debugService = inject(DebugService);
  router = inject(Router);
  route = inject(ActivatedRoute);
  cdr = inject(ChangeDetectorRef);

  isOffline = !navigator.onLine;
  private onlineHandler = () => { this.isOffline = false; this.cdr.detectChanges(); this.loadSessions(); };
  private offlineHandler = () => { this.isOffline = true; this.cdr.detectChanges(); };

  sessions: ChatSession[] = [];
  loadingSessions = false;
  currentSessionId: number | null = null;
  sessionToDelete: number | null = null;
  messages: ChatMessage[] = [];
  
  copiedMsgIndex: number | null = null;
  copiedStreamMsg = false;
  
  previewAttachment: any = null;
  
  currentInput = '';
  isStreaming = false;
  streamingMessage = '';
  pendingAttachments: { file: File, base64: string, name: string, type: string }[] = [];
  
  maxAttachmentSize = 15728640; // default 15MB
  errorMessage = '';
  hasApiKey = true;
  hasModel = true;

  currentStatusText = '';
  showSpinner = false;
  requestStartTime = 0;
  abortController: AbortController | null = null;

  onUserInteraction() {
    this.autoScrollEnabled = false;
  }

  onScroll() {
    if (!this.messagesContainer) return;
    const container = this.messagesContainer.nativeElement;
    const targetScrollTop = container.scrollHeight - container.clientHeight;
    const streamingEl = container.querySelector('.message-box.assistant:last-child');
    
    let clampedScrollTop = targetScrollTop;
    if (streamingEl && this.isStreaming) {
      clampedScrollTop = Math.min(targetScrollTop, Math.max(0, (streamingEl as HTMLElement).offsetTop - 20));
    }
    
    // Re-engage auto-scroll if user manually scrolls back to the target position
    if (Math.abs(container.scrollTop - clampedScrollTop) < 10) {
      this.autoScrollEnabled = true;
    }
  }

  ngOnDestroy() {
    window.removeEventListener('online', this.onlineHandler);
    window.removeEventListener('offline', this.offlineHandler);
    if (this.hubConnection) {
      this.hubConnection.stop();
    }
  }

  getHubConnection() {
    return this.hubConnection;
  }

  toggleSidebar() {
    this.isSidebarOpen = !this.isSidebarOpen;
  }

  closeSidebar() {
    this.isSidebarOpen = false;
  }

  scrollToBottomClamped(smooth: boolean = false) {
    if (!this.autoScrollEnabled || !this.messagesContainer) return;
    const container = this.messagesContainer.nativeElement;
    const targetScrollTop = container.scrollHeight - container.clientHeight;
    const streamingEl = container.querySelector('.message-box.assistant:last-child');
    
    let finalScrollTop = targetScrollTop;
    if (streamingEl && this.isStreaming) {
      const maxScroll = Math.max(0, (streamingEl as HTMLElement).offsetTop - 20);
      finalScrollTop = Math.min(targetScrollTop, maxScroll);
    }
    
    if (container.scrollTop !== finalScrollTop) {
      container.scrollTo({
        top: finalScrollTop,
        behavior: smooth ? 'smooth' : 'auto'
      });
    }
  }

  ngOnInit() {
    window.addEventListener('online', this.onlineHandler);
    window.addEventListener('offline', this.offlineHandler);

    if (!("popover" in HTMLElement.prototype)) {
      import("@oddbird/popover-polyfill").catch(err => console.warn('Failed to load popover polyfill', err));
    }
    
    this.settingsService.getSettings().subscribe(settings => {
      if (settings) {
        if (settings.maxAttachmentSize) {
          this.maxAttachmentSize = settings.maxAttachmentSize;
        }
        this.hasApiKey = settings.hasApiKey;
        this.hasModel = !!settings.model;
      }
    });

    const savedWidth = localStorage.getItem('overseer_sidebar_width');
    if (savedWidth) {
      const parsed = parseInt(savedWidth, 10);
      if (!isNaN(parsed) && parsed >= 150 && parsed <= 600) {
        this.sidebarWidth = parsed;
      }
    }

    this.loadSessions();
    this.route.queryParams.subscribe(params => {
      const idParam = params['sessionId'];
      if (idParam) {
        const id = Number(idParam);
        if (isNaN(id)) {
          this.navigateToNewSession();
        } else if (this.currentSessionId !== id) {
          if (this.isStreaming) this.stopRequest();
          this.loadSession(id);
        }
      } else {
        if (this.currentSessionId !== null || this.messages.length > 0) {
          if (this.isStreaming) this.stopRequest();
          this.newSession();
        } else {
          this.loadDraft();
        }
      }
    });

    this.setupSignalR();
  }

  setupSignalR() {
    this.hubConnection = new signalR.HubConnectionBuilder()
      .withUrl('/chathub')
      .withAutomaticReconnect()
      .build();

    this.hubConnection.on('ReceiveChatEvent', (evt: any) => {
      this.ngZone.run(() => {
        if (evt.type === 'debug') {
          this.debugService.log(`[Backend] ${evt.data}`);
        } else if (evt.type === 'status') {
          this.currentStatusText = evt.data;
          this.cdr.detectChanges();
        } else if (evt.type === 'chunk') {
          if (!this.isStreaming) {
            this.isStreaming = true;
            this.showSpinner = false;
            this.currentStatusText = 'Receiving data (background task)...';
          }
          this.streamingMessage += evt.data;
          this.cdr.detectChanges();
          this.scrollToBottomClamped(false);
        } else if (evt.type === 'error') {
          this.currentStatusText = `Error: ${evt.data}`;
          this.debugService.log(`[Backend Error] ${evt.data}`);
          this.streamingMessage += `\n\n**Error:** ${evt.data}`;
          this.showSpinner = false;
          this.cdr.detectChanges();
        } else if (evt.type === 'done') {
          if (this.isStreaming) {
            this.messages.push({ role: 'assistant', content: this.streamingMessage, timestampUtc: new Date().toISOString() });
            this.streamingMessage = '';
            this.isStreaming = false;
            this.showSpinner = false;
            this.currentStatusText = 'Generation complete.';
            this.cdr.detectChanges();
            this.loadSessions();
          }
        }
      });
    });

    this.hubConnection.start().then(() => {
      if (this.currentSessionId) {
        this.hubConnection?.invoke("JoinSession", this.currentSessionId).catch(console.error);
      }
    }).catch(err => console.error('SignalR connection error: ', err));
  }

  loadDraft() {
    const key = this.currentSessionId ? `chat_draft_${this.currentSessionId}` : 'chat_draft_new';
    this.currentInput = localStorage.getItem(key) || '';
  }

  saveDraft() {
    const key = this.currentSessionId ? `chat_draft_${this.currentSessionId}` : 'chat_draft_new';
    localStorage.setItem(key, this.currentInput);
  }

  clearDraft() {
    const key = this.currentSessionId ? `chat_draft_${this.currentSessionId}` : 'chat_draft_new';
    localStorage.removeItem(key);
  }

  loadSessions() {
    this.loadingSessions = true;
    this.chatService.getSessions().subscribe({
      next: (s) => {
        this.sessions = s;
        this.loadingSessions = false;
      },
      error: (err) => {
        console.error('Failed to load sessions', err);
        this.loadingSessions = false;
      }
    });
  }

  navigateToNewSession() {
    if (window.innerWidth <= 768) {
      this.closeSidebar();
    }
    this.router.navigate([], {
      relativeTo: this.route,
      queryParams: { sessionId: null },
      queryParamsHandling: 'merge'
    });
  }

  navigateToSession(id: number) {
    if (window.innerWidth <= 768) {
      this.closeSidebar();
    }
    if (this.currentSessionId === id) return;
    this.router.navigate([], {
      relativeTo: this.route,
      queryParams: { sessionId: id },
      queryParamsHandling: 'merge'
    });
  }

  newSession() {
    this.currentSessionId = null;
    this.messages = [];
    this.currentStatusText = '';
    this.loadDraft();
  }

  loadSession(id: number) {
    this.chatService.getSession(id).subscribe({
      next: (s) => {
        if (this.hubConnection?.state === signalR.HubConnectionState.Connected) {
          if (this.currentSessionId && this.currentSessionId !== id) {
            this.hubConnection.invoke("LeaveSession", this.currentSessionId).catch(console.error);
          }
          this.hubConnection.invoke("JoinSession", id).catch(console.error);
        }

        this.currentSessionId = s.id;
        this.messages = s.messages || [];
        this.currentStatusText = '';
        this.loadDraft();
        
        if (!this.sessions.find(x => x.id === id)) {
           this.loadSessions();
        }
      },
      error: (err) => {
        console.warn(`Failed to load session ${id}. Bouncing to new chat.`, err);
        this.navigateToNewSession();
      }
    });
  }

  requestDeleteSession(id: number, event: Event) {
    event.stopPropagation();
    this.sessionToDelete = id;
    if (this.deleteConfirmDialog) {
      this.deleteConfirmDialog.nativeElement.showModal();
    }
  }

  confirmDelete() {
    if (this.sessionToDelete === null) return;
    const id = this.sessionToDelete;
    
    this.chatService.deleteSession(id).subscribe(() => {
      if (this.currentSessionId === id) this.navigateToNewSession();
      this.loadSessions();
      if (this.deleteConfirmDialog) {
        this.deleteConfirmDialog.nativeElement.close();
      }
      this.sessionToDelete = null;
    });
  }

  stopRequest() {
    if (this.abortController) {
      this.abortController.abort();
    }
  }

  async sendMessage() {
    if ((!this.currentInput.trim() && this.pendingAttachments.length === 0) || this.isStreaming) return;
    
    const message = this.currentInput;
    const attachmentsPayload = this.pendingAttachments.map(a => ({
      fileName: a.name,
      contentType: a.type,
      base64Data: a.base64
    }));

    this.messages.push({ 
      role: 'user', 
      content: message, 
      timestampUtc: new Date().toISOString(),
      attachments: attachmentsPayload.map(a => ({ fileName: a.fileName, contentType: a.contentType, base64Data: a.base64Data }))
    });
    
    this.autoScrollEnabled = true;
    
    // Wait for angular to render the new user message, then scroll it into view
    setTimeout(() => {
      if (this.messagesContainer) {
        const container = this.messagesContainer.nativeElement;
        const msgEls = container.querySelectorAll('.message-box.user');
        const lastUserMsg = msgEls[msgEls.length - 1];
        if (lastUserMsg) {
          lastUserMsg.scrollIntoView({ behavior: 'smooth', block: 'start' });
        }
      }
    }, 0);
    
    this.clearDraft();
    this.currentInput = '';
    this.pendingAttachments = [];
    this.isStreaming = true;
    this.streamingMessage = '';

    this.requestStartTime = performance.now();
    this.currentStatusText = 'Connecting...';
    this.showSpinner = true;
    this.abortController = new AbortController();
    this.debugService.log(`Starting UI Request to backend for chat message.`);

    try {
      for await (const evt of this.chatService.streamMessage(this.currentSessionId, message, attachmentsPayload, this.abortController.signal)) {
        if (evt.type === 'sessionId') {
          this.currentSessionId = Number(evt.data);
          const urlTree = this.router.createUrlTree([], {
            relativeTo: this.route,
            queryParams: { sessionId: this.currentSessionId },
            queryParamsHandling: 'merge'
          });
          this.router.navigateByUrl(urlTree, { replaceUrl: true });
        } else if (evt.type === 'debug') {
          this.debugService.log(`[Backend] ${evt.data}`);
        } else if (evt.type === 'status') {
          this.currentStatusText = evt.data;
          this.cdr.detectChanges();
        } else if (evt.type === 'chunk') {
          if (this.showSpinner) {
            this.showSpinner = false;
            const ttfb = performance.now() - this.requestStartTime;
            this.currentStatusText = `Receiving data (${Math.round(ttfb)}ms)...`;
          }
          this.streamingMessage += evt.data;
          this.cdr.detectChanges();
          this.scrollToBottomClamped(false);
        } else if (evt.type === 'error') {
          this.currentStatusText = `Error: ${evt.data}`;
          this.debugService.log(`[Backend Error] ${evt.data}`);
          this.streamingMessage += `\n\n**Error:** ${evt.data}`;
          this.showSpinner = false;
          this.cdr.detectChanges();
        }
      }
      
      const totalTimeMs = performance.now() - this.requestStartTime;
      if (this.currentStatusText && !this.currentStatusText.startsWith('Error')) {
          this.currentStatusText = `Request completed (${Math.round(totalTimeMs)}ms)`;
      }
      this.messages.push({ role: 'assistant', content: this.streamingMessage, timestampUtc: new Date().toISOString() });
    } catch (e: any) {
      if (e.name === 'AbortError') {
        this.currentStatusText = `Cancelled by user.`;
        this.debugService.log(`Request aborted by user.`);
      } else {
        console.error(e);
        this.currentStatusText = `Error: ${e.message}`;
        this.messages.push({ role: 'assistant', content: 'Error: ' + e, timestampUtc: new Date().toISOString() });
        this.debugService.log(`Frontend Error: ${e.message}`);
      }
    } finally {
      this.streamingMessage = '';
      this.isStreaming = false;
      this.showSpinner = false;
      this.abortController = null;
      this.loadSessions(); // Refresh sessions list in case a new one was created
      this.cdr.detectChanges();
    }
  }

  logout(event: Event) {
    event.preventDefault();
    this.authService.logout().subscribe(() => this.router.navigate(['/login']));
  }

  triggerFileInput() {
    const el = document.getElementById('fileInput') as HTMLInputElement;
    if (el) el.click();
  }

  onFileSelected(event: any) {
    const files = event.target.files;
    this.addFiles(files);
    event.target.value = '';
  }

  onPaste(event: ClipboardEvent) {
    if (event.clipboardData && event.clipboardData.files && event.clipboardData.files.length > 0) {
      this.addFiles(event.clipboardData.files);
    }
  }

  addFiles(files: FileList | File[]) {
    for (let i = 0; i < files.length; i++) {
      if (this.pendingAttachments.length >= 5) break;
      const file = files[i];
      const ext = file.name.split('.').pop()?.toLowerCase();
      if (!['html', 'htm', 'txt', 'md', 'png', 'jpg', 'jpeg', 'webp'].includes(ext || '')) continue;
      
      if (file.size > this.maxAttachmentSize) {
        const sizeMb = (this.maxAttachmentSize / 1024 / 1024).toFixed(1);
        this.showErrorToast(`The file "${file.name}" exceeds the maximum allowed size of ${sizeMb} MB.`);
        continue;
      }
      
      const reader = new FileReader();
      reader.onload = (e: any) => {
        this.pendingAttachments.push({
          file: file,
          base64: e.target.result,
          name: file.name,
          type: file.type || 'application/octet-stream' // fallback
        });
        this.cdr.detectChanges();
      };
      reader.readAsDataURL(file);
    }
  }
  
  removeAttachment(index: number) {
    this.pendingAttachments.splice(index, 1);
  }

  showErrorToast(msg: string) {
    this.errorMessage = msg;
    this.cdr.detectChanges();
    const toast = this.errorToast?.nativeElement as any;
    if (toast && ("popover" in HTMLElement.prototype || toast.classList.contains('\\:popover-open') || 'showPopover' in toast)) {
      toast.showPopover();
      setTimeout(() => this.closeErrorToast(), 5000);
    }
  }

  closeErrorToast() {
    const toast = this.errorToast?.nativeElement as any;
    if (toast) {
      try { toast.hidePopover(); } catch(e) {}
    }
  }

  isImage(fileName: string): boolean {
    if (!fileName) return false;
    const ext = fileName.split('.').pop()?.toLowerCase();
    return ['png', 'jpg', 'jpeg', 'webp'].includes(ext || '');
  }

  getFileExtension(fileName: string): string {
    if (!fileName || !fileName.includes('.')) return 'NONE';
    const ext = fileName.split('.').pop()?.toUpperCase() || 'NONE';
    return ext;
  }

  openImagePreview(att: any, event: Event) {
    event.preventDefault();
    this.previewAttachment = att;
    if (this.imagePreviewDialog) {
      this.imagePreviewDialog.nativeElement.showModal();
    }
  }

  closeImagePreview() {
    if (this.imagePreviewDialog) {
      this.imagePreviewDialog.nativeElement.close();
    }
    this.previewAttachment = null;
  }

  onDialogClick(event: MouseEvent) {
    if (this.imagePreviewDialog) {
      const dialog = this.imagePreviewDialog.nativeElement;
      const rect = dialog.getBoundingClientRect();
      const isInDialog = (rect.top <= event.clientY && event.clientY <= rect.top + rect.height
        && rect.left <= event.clientX && event.clientX <= rect.left + rect.width);
      if (!isInDialog) {
        this.closeImagePreview();
      }
    }
  }

  async copyToClipboard(text: string, index: number | null) {
    try {
      await navigator.clipboard.writeText(text);
      if (index !== null) {
        this.copiedMsgIndex = index;
        setTimeout(() => {
          if (this.copiedMsgIndex === index) this.copiedMsgIndex = null;
          this.cdr.detectChanges();
        }, 2000);
      } else {
        this.copiedStreamMsg = true;
        setTimeout(() => {
          this.copiedStreamMsg = false;
          this.cdr.detectChanges();
        }, 2000);
      }
    } catch (err) {
      console.error('Failed to copy text: ', err);
    }
  }

  startResize(event: MouseEvent | TouchEvent) {
    if (window.innerWidth <= 768) return;
    event.preventDefault();
    this.isResizing = true;
    this.resizeStartX = event instanceof MouseEvent ? event.clientX : event.touches[0].clientX;
    this.resizeStartWidth = this.sidebarWidth;
    document.body.style.userSelect = 'none';
    document.body.style.cursor = 'col-resize';

    this.ngZone.runOutsideAngular(() => {
      this.mouseMoveListener = (e: MouseEvent | TouchEvent) => this.onMouseMove(e);
      this.mouseUpListener = () => this.stopResize();
      
      window.addEventListener('mousemove', this.mouseMoveListener);
      window.addEventListener('touchmove', this.mouseMoveListener);
      window.addEventListener('mouseup', this.mouseUpListener);
      window.addEventListener('touchend', this.mouseUpListener);
    });
  }

  onMouseMove(event: MouseEvent | TouchEvent) {
    if (!this.isResizing) return;
    const clientX = event instanceof MouseEvent ? event.clientX : event.touches[0].clientX;
    const delta = clientX - this.resizeStartX;
    let newWidth = this.resizeStartWidth + delta;
    newWidth = Math.max(150, Math.min(newWidth, 600));
    
    if (this.animationFrameId === null) {
      this.animationFrameId = requestAnimationFrame(() => {
        // Update the native element directly for performance to avoid triggering Angular change detection on every pixel move
        if (this.sidebarEl) {
          this.sidebarEl.nativeElement.style.width = `${newWidth}px`;
        }
        this.sidebarWidth = newWidth;
        this.animationFrameId = null;
      });
    }
  }

  stopResize() {
    if (this.isResizing) {
      this.ngZone.run(() => {
        this.isResizing = false;
      });
      
      if (this.animationFrameId !== null) {
        cancelAnimationFrame(this.animationFrameId);
        this.animationFrameId = null;
      }
      
      localStorage.setItem('overseer_sidebar_width', this.sidebarWidth.toString());
      document.body.style.userSelect = '';
      document.body.style.cursor = '';
      
      if (this.mouseMoveListener) {
        window.removeEventListener('mousemove', this.mouseMoveListener);
        window.removeEventListener('touchmove', this.mouseMoveListener);
        this.mouseMoveListener = null;
      }
      if (this.mouseUpListener) {
        window.removeEventListener('mouseup', this.mouseUpListener);
        window.removeEventListener('touchend', this.mouseUpListener);
        this.mouseUpListener = null;
      }
    }
  }
}
