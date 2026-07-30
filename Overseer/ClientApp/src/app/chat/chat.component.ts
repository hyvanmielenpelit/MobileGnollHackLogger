import { Component, OnInit, inject, ChangeDetectorRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ChatService, ChatSession, ChatMessage } from '../services/chat.service';
import { AuthService } from '../services/auth.service';
import { Router, ActivatedRoute, RouterModule } from '@angular/router';
import { MarkdownPipe } from './markdown.pipe';

@Component({
  selector: 'app-chat',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterModule, MarkdownPipe],
  template: `
    <div class="layout">
      <div class="sidebar">
        <h3>Sessions</h3>
        <button (click)="newSession()">New Chat</button>
        <ul>
          <li *ngFor="let s of sessions" [class.active]="s.id === currentSessionId" (click)="loadSession(s.id)">
            {{ s.title }}
            <button (click)="deleteSession(s.id, $event)">X</button>
          </li>
        </ul>
        <div class="bottom-links">
          <a routerLink="/settings">Settings</a>
          <a href="#" (click)="logout($event)">Logout</a>
        </div>
      </div>
      <div class="chat-area">
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
            <button (click)="sendMessage()" [disabled]="isStreaming || (!currentInput.trim() && pendingAttachments.length === 0)">Send</button>
          </div>
        </div>
      </div>
    </div>
  `,
  styles: [`
    .layout { display: flex; height: 100vh; }
    .sidebar { width: 250px; background: #f4f4f4; padding: 20px; display: flex; flex-direction: column; }
    .sidebar h3 { margin-top: 0; }
    .sidebar ul { list-style: none; padding: 0; flex-grow: 1; overflow-y: auto; }
    .sidebar li { padding: 10px; cursor: pointer; border-bottom: 1px solid #ddd; display: flex; justify-content: space-between; }
    .sidebar li.active { background: #e0e0e0; font-weight: bold; }
    .bottom-links a { display: block; margin-top: 10px; }
    .chat-area { flex-grow: 1; display: flex; flex-direction: column; }
    .messages { flex-grow: 1; padding: 20px; overflow-y: auto; background: #fff; }
    .messages div { margin-bottom: 15px; padding: 10px; border-radius: 8px; max-width: 80%; }
    .messages .user { background: #e3f2fd; align-self: flex-end; margin-left: auto; }
    .messages .assistant { background: #f5f5f5; align-self: flex-start; }
    ::ng-deep .markdown-body p { margin: 5px 0 10px; white-space: pre-wrap; }
    ::ng-deep .markdown-body ul, ::ng-deep .markdown-body ol { margin: 5px 0 10px; padding-left: 20px; }
    ::ng-deep .markdown-body h1, ::ng-deep .markdown-body h2, ::ng-deep .markdown-body h3 { margin: 10px 0 5px; }
    .input-area-container { border-top: 1px solid #ddd; background: #fafafa; }
    .attachments-preview { display: flex; padding: 10px 20px 0; gap: 10px; flex-wrap: wrap; }
    .attachment-chip { background: #e0e0e0; padding: 5px 10px; border-radius: 15px; font-size: 12px; display: flex; align-items: center; }
    .attachment-chip button { margin-left: 5px; border: none; background: transparent; cursor: pointer; font-weight: bold; padding: 0; }
    .add-media-btn { padding: 10px; cursor: pointer; font-size: 20px; font-weight: bold; width: 40px; height: 40px; border-radius: 50%; border: 1px solid #ccc; display: flex; align-items: center; justify-content: center; margin-right: 10px; align-self: center; background: #fff; }
    .add-media-btn:disabled { color: #ccc; cursor: not-allowed; }
    .input-area { padding: 20px; display: flex; align-items: center; }
    textarea { flex-grow: 1; padding: 10px; resize: none; height: 60px; }
    button { padding: 10px 20px; margin-left: 10px; cursor: pointer; }
    .msg-attachments { display: flex; gap: 10px; margin-bottom: 10px; flex-wrap: wrap; }
    .msg-att-item img { max-width: 100%; border-radius: 4px; border: 1px solid #ddd; }
    .msg-att-item div { background: #eee; padding: 5px 10px; border-radius: 4px; font-size: 12px; color: #555; }
  `]
})
export class ChatComponent implements OnInit {
  chatService = inject(ChatService);
  authService = inject(AuthService);
  router = inject(Router);
  route = inject(ActivatedRoute);
  cdr = inject(ChangeDetectorRef);

  sessions: ChatSession[] = [];
  currentSessionId: number | null = null;
  messages: ChatMessage[] = [];
  
  currentInput = '';
  isStreaming = false;
  streamingMessage = '';
  pendingAttachments: { file: File, base64: string, name: string, type: string }[] = [];

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
    this.chatService.getSessions().subscribe(s => this.sessions = s);
  }

  newSession() {
    this.currentSessionId = null;
    this.messages = [];
    this.loadDraft();
  }

  loadSession(id: number) {
    this.chatService.getSession(id).subscribe(s => {
      this.currentSessionId = s.id;
      this.messages = s.messages || [];
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

  async sendMessage() {
    if ((!this.currentInput.trim() && this.pendingAttachments.length === 0) || this.isStreaming) return;
    
    const message = this.currentInput;
    const attachmentsPayload = this.pendingAttachments.map(a => ({
      fileName: a.name,
      contentType: a.type,
      base64Data: a.base64
    }));
    
    const sentAttachments = [...this.pendingAttachments]; // keep for local display if needed, but the server handles saving it

    this.messages.push({ 
      role: 'user', 
      content: message, 
      timestampUtc: new Date().toISOString(),
      attachments: attachmentsPayload.map(a => ({ fileName: a.fileName, contentType: a.contentType, base64Data: a.base64Data })) // temporarily show without id
    });
    
    this.clearDraft();
    this.currentInput = '';
    this.pendingAttachments = [];
    this.isStreaming = true;
    this.streamingMessage = '';

    try {
      for await (const chunk of this.chatService.streamMessage(this.currentSessionId, message, attachmentsPayload)) {
        this.streamingMessage += chunk;
        this.cdr.detectChanges();
      }
      this.messages.push({ role: 'assistant', content: this.streamingMessage, timestampUtc: new Date().toISOString() });
      this.cdr.detectChanges();
    } catch (e) {
      console.error(e);
      this.messages.push({ role: 'assistant', content: 'Error: ' + e, timestampUtc: new Date().toISOString() });
      this.cdr.detectChanges();
    } finally {
      this.streamingMessage = '';
      this.isStreaming = false;
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
