import { Component, OnInit, inject, ChangeDetectorRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ChatService, ChatSession, ChatMessage } from '../services/chat.service';
import { AuthService } from '../services/auth.service';
import { DebugService } from '../services/debug.service';
import { Router, ActivatedRoute, RouterModule } from '@angular/router';
import { MarkdownPipe } from './markdown.pipe';

@Component({
  selector: 'app-chat',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterModule, MarkdownPipe],
  template: `
    <div class="layout">
      <div class="sidebar gh-main-container">
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
            <button (click)="deleteSession(s.id, $event)" title="Delete Session">X</button>
          </li>
        </ul>
        <div class="bottom-links">
          <a routerLink="/debug-log">Debug Log</a>
          <a routerLink="/settings">Settings</a>
          <a href="#" (click)="logout($event)">Logout</a>
        </div>
      </div>
      <div class="chat-area gh-main-container">
        <div class="messages">
          <div *ngFor="let msg of messages" [ngClass]="msg.role">
            <strong>{{ msg.role === 'user' ? 'You' : 'Overseer' }}</strong>
            <div *ngIf="msg.attachments && msg.attachments.length > 0" class="msg-attachments">
              <div *ngFor="let att of msg.attachments" class="msg-att-item">
                <img *ngIf="att.contentType.startsWith('image/')" [src]="'/api/chat/attachments/' + att.id" width="200" />
                <div *ngIf="!att.contentType.startsWith('image/')">📎 {{ att.fileName }}</div>
              </div>
            </div>
            <div [innerHTML]="msg.content | markdown" class="markdown-body"></div>
          </div>
          <div *ngIf="streamingMessage" class="assistant">
            <strong>Overseer</strong>
            <div [innerHTML]="streamingMessage | markdown" class="markdown-body"></div>
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
  `,
  styles: [`
    .layout { display: flex; height: 100vh; padding: 20px; box-sizing: border-box; gap: 20px; }
    .sidebar { width: 250px; padding: 20px; display: flex; flex-direction: column; }
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
    .messages { flex-grow: 1; padding: 20px; overflow-y: auto; }
    .messages div { margin-bottom: 15px; padding: 12px 18px; border-radius: 8px; max-width: 80%; line-height: 1.5; }
    .messages .user { background: rgba(212, 160, 23, 0.15); align-self: flex-end; margin-left: auto; border: 1px solid var(--border-glass); }
    .messages .assistant { background: rgba(255, 255, 255, 0.05); align-self: flex-start; border: 1px solid rgba(255,255,255,0.1); }
    .messages strong { color: var(--title-color); display: block; margin-bottom: 5px; font-family: "Cinzel", serif; }
    ::ng-deep .markdown-body p { margin: 5px 0 10px; white-space: pre-wrap; }
    ::ng-deep .markdown-body ul, ::ng-deep .markdown-body ol { margin: 5px 0 10px; padding-left: 20px; }
    ::ng-deep .markdown-body h1, ::ng-deep .markdown-body h2, ::ng-deep .markdown-body h3 { margin: 10px 0 5px; color: var(--title-color); font-family: "Cinzel", serif; }
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
  `]
})
export class ChatComponent implements OnInit {
  chatService = inject(ChatService);
  authService = inject(AuthService);
  debugService = inject(DebugService);
  router = inject(Router);
  route = inject(ActivatedRoute);
  cdr = inject(ChangeDetectorRef);

  sessions: ChatSession[] = [];
  loadingSessions = false;
  currentSessionId: number | null = null;
  messages: ChatMessage[] = [];
  
  currentInput = '';
  isStreaming = false;
  streamingMessage = '';
  pendingAttachments: { file: File, base64: string, name: string, type: string }[] = [];

  currentStatusText = '';
  showSpinner = false;
  requestStartTime = 0;
  abortController: AbortController | null = null;

  ngOnInit() {
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

  deleteSession(id: number, event: Event) {
    event.stopPropagation();
    this.chatService.deleteSession(id).subscribe(() => {
      if (this.currentSessionId === id) this.newSession();
      this.loadSessions();
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
}
