import { Component, OnInit, OnDestroy, inject, ChangeDetectorRef, ViewChild, ElementRef, HostListener, NgZone } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ChatService, ChatSession, ChatMessage } from '../services/chat.service';
import { AuthService } from '../services/auth.service';
import { DebugService } from '../services/debug.service';
import { Router, ActivatedRoute, RouterModule } from '@angular/router';
import { MarkdownPipe } from './markdown.pipe';
import { SettingsService } from '../services/settings.service';

@Component({
  selector: 'app-chat',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterModule, MarkdownPipe],
  template: `
    <div *ngIf="!isOffline" class="layout" [class.is-resizing]="isResizing">
      <div class="sidebar gh-main-container" #sidebar [style.width.px]="sidebarWidth">
        <h3>Sessions</h3>
        <button class="btn-gh btn-new-chat" (click)="newSession()">New Chat</button>
        <ul>
          <div *ngIf="loadingSessions" class="sessions-loader">
            <div class="sidebar-spinner"></div>
            <span>Loading Conversations...</span>
          </div>
          <li *ngIf="!loadingSessions && sessions.length === 0" style="color: #666; text-align: center; margin-top: 10px;">
            No conversations yet.
          </li>
          <li *ngFor="let s of sessions" [class.active]="s.id === currentSessionId" (click)="loadSession(s.id)">
            {{ s.title }}
            <button (click)="requestDeleteSession(s.id, $event)" title="Delete Session">X</button>
          </li>
        </ul>
        <div class="bottom-links">
          <a routerLink="/debug-log">Debug Log</a>
          <a routerLink="/settings">Settings</a>
          <a href="#" (click)="logout($event)">Logout</a>
        </div>
      </div>
      <div class="resizer" (mousedown)="startResize($event)" (touchstart)="startResize($event)"></div>
      <div class="chat-area gh-main-container">
        <div class="messages" #messagesContainer (wheel)="onUserInteraction()" (touchstart)="onUserInteraction()" (scroll)="onScroll()">
          <div *ngFor="let msg of messages; let i = index" class="message-box" [ngClass]="msg.role">
            <div class="message-header">
              <img *ngIf="msg.role === 'assistant'" src="/img/gnoll-overseer-avatar-128x128-static.webp" class="overseer-avatar" alt="Overseer" width="64" height="64" />
              <strong>{{ msg.role === 'user' ? 'You' : 'Overseer' }}</strong>
            </div>
            <div *ngIf="msg.attachments && msg.attachments.length > 0" class="msg-attachments">
              <div *ngFor="let att of msg.attachments" class="msg-att-item">
                <img *ngIf="att.contentType.startsWith('image/')" [src]="'/api/chat/attachments/' + att.id" width="200" />
                <div *ngIf="!att.contentType.startsWith('image/')">📎 {{ att.fileName }}</div>
              </div>
            </div>
            <div [innerHTML]="msg.content | markdown" class="markdown-body"></div>
            <button class="copy-btn" (click)="copyToClipboard(msg.content, i)" title="Copy">
              <span *ngIf="copiedMsgIndex === i">✅</span>
              <svg *ngIf="copiedMsgIndex !== i" xmlns="http://www.w3.org/2000/svg" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><rect x="9" y="9" width="13" height="13" rx="2" ry="2"></rect><path d="M5 15H4a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h9a2 2 0 0 1 2 2v1"></path></svg>
            </button>
          </div>
          <div *ngIf="streamingMessage" class="message-box assistant">
            <div class="message-header">
              <img src="/img/GnollOverseerAvatar-128x128-animated.webp" class="overseer-avatar" alt="Overseer" width="64" height="64" />
              <strong>Overseer</strong>
            </div>
            <div [innerHTML]="streamingMessage | markdown" class="markdown-body"></div>
            <button class="copy-btn" (click)="copyToClipboard(streamingMessage, null)" title="Copy">
              <span *ngIf="copiedStreamMsg">✅</span>
              <svg *ngIf="!copiedStreamMsg" xmlns="http://www.w3.org/2000/svg" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><rect x="9" y="9" width="13" height="13" rx="2" ry="2"></rect><path d="M5 15H4a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h9a2 2 0 0 1 2 2v1"></path></svg>
            </button>
          </div>
        </div>
        <div class="input-area-container">
          <!-- Progress Indicator at the top of the prompt box -->
          <div class="progress-bar" *ngIf="isStreaming || currentStatusText">
            <span class="status-icon spin" *ngIf="showSpinner">↻</span>
            <span class="status-icon" *ngIf="!showSpinner && !isStreaming">✅</span>
            <span class="status-text">{{ currentStatusText }}</span>
            <button *ngIf="isStreaming" class="stop-btn" (click)="stopRequest()" title="Stop Request">■</button>
          </div>

          <div class="attachments-preview" *ngIf="pendingAttachments.length > 0">
            <div class="attachment-chip" *ngFor="let att of pendingAttachments; let i = index">
              <span class="att-name">{{ att.name }}</span>
              <button (click)="removeAttachment(i)">x</button>
            </div>
          </div>
          <div class="input-area">
            <button class="add-media-btn" (click)="triggerFileInput()" [disabled]="pendingAttachments.length >= 5">+</button>
            <input type="file" id="fileInput" hidden multiple accept=".html,.htm,.txt,.md,.png,.jpg,.jpeg,.webp" (change)="onFileSelected($event)">
            <textarea [(ngModel)]="currentInput" (ngModelChange)="saveDraft()" (keyup.enter)="sendMessage()" (paste)="onPaste($event)" [disabled]="isStreaming"></textarea>
            <button class="btn-gh" style="margin-left: 10px;" (click)="sendMessage()" [disabled]="isStreaming || (!currentInput.trim() && pendingAttachments.length === 0)">Send</button>
          </div>
        </div>
      </div>
    </div>
    
    <div *ngIf="isOffline" class="offline-notice">
      <h2>Overseer requires an internet connection</h2>
      <p>Please check your connection and try again.</p>
    </div>

    <dialog #deleteConfirmDialog class="gh-dialog">
      <h3>Delete Conversation</h3>
      <p>Are you sure you want to delete this conversation? This action cannot be undone.</p>
      <div class="dialog-actions">
        <button class="btn-gh btn-gh-cancel" (click)="deleteConfirmDialog.close()">Cancel</button>
        <button class="btn-gh btn-gh-delete" (click)="confirmDelete()">Delete</button>
      </div>
    </dialog>

    <div #errorToast popover="manual" class="toast-error">
      <div class="toast-content">
        <span class="toast-icon">⚠️</span>
        <div class="toast-body">
          <strong>File Too Large</strong>
          <p>{{ errorMessage }}</p>
        </div>
        <button class="toast-close" (click)="closeErrorToast()">×</button>
      </div>
    </div>
  `,
  styles: [`
    .layout { display: flex; height: 100vh; padding: 20px; box-sizing: border-box; gap: 20px; }
    .layout.is-resizing .chat-area, .layout.is-resizing .sidebar { pointer-events: none; }
    .sidebar { padding: 20px; display: flex; flex-direction: column; flex-shrink: 0; }
    .resizer {
      width: 10px;
      flex-shrink: 0;
      cursor: col-resize;
      margin: 0 -15px;
      z-index: 10;
      display: flex;
      justify-content: center;
      align-items: center;
    }
    .resizer::after {
      content: '';
      width: 4px;
      height: 40px;
      background: rgba(255,255,255,0.2);
      border-radius: 2px;
      transition: background 0.2s;
    }
    .resizer:hover::after, .resizer:active::after {
      background: var(--primary-color, #d4a847);
    }
    .sidebar h3 { margin-top: 0; color: var(--title-color); border-bottom: 1px solid var(--border-glass); padding-bottom: 10px; }
    .sidebar ul { list-style: none; padding: 0; flex-grow: 1; overflow-y: auto; }
    .sidebar li { padding: 10px; cursor: pointer; border-bottom: 1px solid rgba(255,255,255,0.1); display: flex; justify-content: space-between; color: #ccc; transition: background 0.2s; }
    .sidebar li:hover { background: rgba(255,255,255,0.05); }
    .sidebar li.active { background: rgba(212, 160, 23, 0.15); font-weight: bold; color: var(--title-color); border-left: 3px solid var(--primary-color); }
    .sidebar li button { background: transparent; color: #ccc; border: none; cursor: pointer; font-weight: bold; }
    .sidebar li button:hover { color: white; }
    .btn-new-chat { width: 100%; margin-bottom: 15px; }
    .sessions-loader { padding: 30px 10px; color: #aaa; display: flex; flex-direction: column; align-items: center; justify-content: center; font-size: 0.9em; gap: 15px; }
    .sidebar-spinner { width: 36px; height: 36px; border: 4px solid rgba(212, 175, 55, 0.2); border-top-color: #d4af37; border-radius: 50%; animation: spin 1s linear infinite; }
    .bottom-links a { display: block; margin-top: 10px; text-decoration: none; color: var(--link-color); padding: 5px; }
    .bottom-links a:hover { background: rgba(255,255,255,0.05); border-radius: 4px; }
    .chat-area { flex-grow: 1; display: flex; flex-direction: column; overflow: hidden; }
    .messages { flex-grow: 1; padding: 20px; overflow-y: auto; display: flex; flex-direction: column; }
    .message-box { margin-bottom: 15px; padding: 12px 18px 25px 18px; border-radius: 8px; max-width: 80%; line-height: 1.5; position: relative; }
    .message-box.user { background: rgba(212, 160, 23, 0.15); align-self: flex-end; border: 1px solid var(--border-glass); }
    .message-box.assistant { background: rgba(255, 255, 255, 0.05); align-self: flex-start; border: 1px solid rgba(255,255,255,0.1); }
    .copy-btn { position: absolute; bottom: 5px; right: 5px; background: transparent; border: none; color: #888; cursor: pointer; padding: 4px; border-radius: 4px; display: flex; align-items: center; justify-content: center; transition: background 0.2s, color 0.2s; }
    .copy-btn:hover { background: rgba(255,255,255,0.1); color: #fff; }
    .messages strong { color: var(--title-color); display: block; margin-bottom: 5px; font-family: "Cinzel", serif; }
    .message-header { display: flex; align-items: center; gap: 15px; margin-bottom: 10px; }
    .message-header strong { margin-bottom: 0 !important; }
    .overseer-avatar { width: 64px; height: 64px; border-radius: 50%; border: 2px solid var(--primary-color); object-fit: cover; box-shadow: 0 0 10px rgba(212, 175, 55, 0.3); }
    ::ng-deep .markdown-body p { margin: 5px 0 10px; white-space: pre-wrap; }
    ::ng-deep .markdown-body ul, ::ng-deep .markdown-body ol { margin: 5px 0 10px; padding-left: 20px; }
    ::ng-deep .markdown-body h1, ::ng-deep .markdown-body h2, ::ng-deep .markdown-body h3, ::ng-deep .markdown-body h4, ::ng-deep .markdown-body h5, ::ng-deep .markdown-body h6 { margin: 10px 0 5px; color: var(--title-color); font-family: "Cinzel", serif; }
    .input-area-container { border-top: 1px solid var(--border-glass); background: rgba(0,0,0,0.3); display: flex; flex-direction: column; }
    
    .progress-bar {
      display: flex;
      align-items: center;
      padding: 6px 20px;
      background: rgba(212, 160, 23, 0.1);
      border-bottom: 1px solid var(--border-glass);
      font-size: 12px;
      color: #e0ba6d;
    }
    .status-icon { margin-right: 8px; font-size: 14px; }
    .status-icon.spin { animation: spin 1.5s linear infinite; display: inline-block; }
    @keyframes spin { 100% { transform: rotate(360deg); } }
    .status-text { flex-grow: 1; font-family: monospace; }
    .stop-btn {
      background: #dc3545;
      color: white;
      border: none;
      width: 24px;
      height: 24px;
      border-radius: 4px;
      font-size: 12px;
      line-height: 12px;
      cursor: pointer;
      display: flex;
      align-items: center;
      justify-content: center;
      padding: 0;
      margin: 0;
    }
    .stop-btn:hover { background: #c82333; }

    .attachments-preview { display: flex; padding: 10px 20px 0; gap: 10px; flex-wrap: wrap; }
    .attachment-chip { background: rgba(255,255,255,0.1); padding: 5px 10px; border-radius: 15px; font-size: 12px; display: flex; align-items: center; border: 1px solid rgba(255,255,255,0.2); }
    .attachment-chip button { margin-left: 5px; border: none; background: transparent; cursor: pointer; font-weight: bold; padding: 0; color: #ff6b6b; }
    .add-media-btn { padding: 10px; cursor: pointer; font-size: 20px; font-weight: bold; width: 40px; height: 40px; border-radius: 50%; border: 1px solid var(--border-glass); display: flex; align-items: center; justify-content: center; margin-right: 10px; align-self: center; background: rgba(255,255,255,0.05); color: var(--primary-color); transition: background 0.2s; }
    .add-media-btn:hover { background: rgba(255,255,255,0.1); }
    .add-media-btn:disabled { color: #555; border-color: #555; cursor: not-allowed; }
    .input-area { padding: 20px; display: flex; align-items: center; }
    textarea { flex-grow: 1; padding: 10px; resize: none; height: 60px; background: var(--bg-input); border: 1px solid var(--border-glass); color: white; border-radius: 4px; font-family: "Lato", sans-serif; }
    textarea:focus { outline: none; border-color: var(--primary-color); box-shadow: 0 0 5px var(--gold-glow); }
    .msg-attachments { display: flex; gap: 10px; margin-bottom: 10px; flex-wrap: wrap; }
    .msg-att-item img { max-width: 100%; border-radius: 4px; border: 1px solid var(--border-glass); }
    .msg-att-item div { background: rgba(255,255,255,0.1); padding: 5px 10px; border-radius: 4px; font-size: 12px; color: #ccc; }
    
    .gh-dialog {
      background: rgba(20, 20, 20, 0.95);
      border: 2px solid var(--primary-color);
      border-radius: 8px;
      color: white;
      padding: 20px 30px;
      box-shadow: 0 0 20px rgba(212, 175, 55, 0.2);
      font-family: "Lato", sans-serif;
    }
    .offline-notice {
      display: flex;
      flex-direction: column;
      align-items: center;
      justify-content: center;
      height: 60vh;
      text-align: center;
      color: #aaa;
    }
    .offline-notice h2 {
      color: var(--primary-color, #d4a847);
      margin-bottom: 10px;
    }
    .gh-dialog::backdrop {
      background: rgba(0, 0, 0, 0.7);
      backdrop-filter: blur(2px);
    }
    .gh-dialog h3 {
      font-family: "Cinzel", serif;
      color: var(--title-color);
      margin-top: 0;
    }
    .dialog-actions {
      display: flex;
      justify-content: flex-end;
      gap: 10px;
      margin-top: 20px;
    }
    
    .toast-error {
      inset: 20px 20px auto auto;
      margin: 0;
      background: rgba(220, 53, 69, 0.95);
      border: 1px solid #ff6b6b;
      border-radius: 8px;
      color: white;
      padding: 15px;
      box-shadow: 0 4px 12px rgba(0,0,0,0.5);
      font-family: "Lato", sans-serif;
      transition: display 0.3s allow-discrete, opacity 0.3s, transform 0.3s;
      opacity: 0;
      transform: translateY(-20px);
      z-index: 9999;
    }
    .toast-error:is(:popover-open, .\\:popover-open) {
      opacity: 1;
      transform: translateY(0);
    }
    @starting-style {
      .toast-error:is(:popover-open, .\\:popover-open) {
        opacity: 0;
        transform: translateY(-20px);
      }
    }
    .toast-content { display: flex; align-items: flex-start; gap: 12px; }
    .toast-icon { font-size: 24px; }
    .toast-body { flex-grow: 1; }
    .toast-body strong { display: block; margin-bottom: 4px; font-family: "Cinzel", serif; }
    .toast-body p { margin: 0; font-size: 14px; }
    .toast-close { background: transparent; border: none; color: white; font-size: 20px; cursor: pointer; padding: 0; line-height: 1; margin-top: -4px; }
  `]
})
export class ChatComponent implements OnInit, OnDestroy {
  chatService = inject(ChatService);
  settingsService = inject(SettingsService);
  
  @ViewChild('messagesContainer') messagesContainer!: ElementRef;
  @ViewChild('deleteConfirmDialog') deleteConfirmDialog!: ElementRef<HTMLDialogElement>;
  @ViewChild('errorToast') errorToast!: ElementRef<HTMLElement>;
  autoScrollEnabled = true;

  sidebarWidth = 250;
  isResizing = false;
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
  
  currentInput = '';
  isStreaming = false;
  streamingMessage = '';
  pendingAttachments: { file: File, base64: string, name: string, type: string }[] = [];
  
  maxAttachmentSize = 15728640; // default 15MB
  errorMessage = '';

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
      if (settings.maxAttachmentSize) {
        this.maxAttachmentSize = settings.maxAttachmentSize;
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
      if (params['sessionId']) {
        this.loadSession(Number(params['sessionId']));
      } else {
        this.loadDraft();
      }
    });
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

  newSession() {
    this.currentSessionId = null;
    this.messages = [];
    this.currentStatusText = '';
    this.loadDraft();
  }

  loadSession(id: number) {
    this.chatService.getSession(id).subscribe(s => {
      this.currentSessionId = s.id;
      this.messages = s.messages || [];
      this.currentStatusText = '';
      this.loadDraft();
      
      if (!this.sessions.find(x => x.id === id)) {
         this.loadSessions();
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
      if (this.currentSessionId === id) this.newSession();
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
        if (evt.type === 'debug') {
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
