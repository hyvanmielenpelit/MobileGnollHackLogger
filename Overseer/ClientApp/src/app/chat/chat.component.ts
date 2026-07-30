import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ChatService, ChatSession, ChatMessage } from '../services/chat.service';
import { AuthService } from '../services/auth.service';
import { Router, ActivatedRoute, RouterModule } from '@angular/router';

@Component({
  selector: 'app-chat',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterModule],
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
            <p>{{ msg.content }}</p>
          </div>
          <div *ngIf="streamingMessage" class="assistant">
            <strong>Overseer</strong>
            <p>{{ streamingMessage }}</p>
          </div>
        </div>
        <div class="input-area">
          <textarea [(ngModel)]="currentInput" (keyup.enter)="sendMessage()" [disabled]="isStreaming"></textarea>
          <button (click)="sendMessage()" [disabled]="isStreaming || !currentInput.trim()">Send</button>
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
    .messages p { margin: 5px 0 0; white-space: pre-wrap; }
    .input-area { padding: 20px; border-top: 1px solid #ddd; display: flex; background: #fafafa; }
    textarea { flex-grow: 1; padding: 10px; resize: none; height: 60px; }
    button { padding: 10px 20px; margin-left: 10px; cursor: pointer; }
  `]
})
export class ChatComponent implements OnInit {
  chatService = inject(ChatService);
  authService = inject(AuthService);
  router = inject(Router);
  route = inject(ActivatedRoute);

  sessions: ChatSession[] = [];
  currentSessionId: number | null = null;
  messages: ChatMessage[] = [];
  
  currentInput = '';
  isStreaming = false;
  streamingMessage = '';

  ngOnInit() {
    this.loadSessions();
    this.route.queryParams.subscribe(params => {
      if (params['sessionId']) {
        this.loadSession(Number(params['sessionId']));
      }
    });
  }

  loadSessions() {
    this.chatService.getSessions().subscribe(s => this.sessions = s);
  }

  newSession() {
    this.currentSessionId = null;
    this.messages = [];
  }

  loadSession(id: number) {
    this.chatService.getSession(id).subscribe(s => {
      this.currentSessionId = s.id;
      this.messages = s.messages || [];
      
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
    if (!this.currentInput.trim() || this.isStreaming) return;
    
    const message = this.currentInput;
    this.messages.push({ role: 'user', content: message, timestampUtc: new Date().toISOString() });
    this.currentInput = '';
    this.isStreaming = true;
    this.streamingMessage = '';

    try {
      for await (const chunk of this.chatService.streamMessage(this.currentSessionId, message)) {
        this.streamingMessage += chunk;
      }
      this.messages.push({ role: 'assistant', content: this.streamingMessage, timestampUtc: new Date().toISOString() });
    } catch (e) {
      console.error(e);
      this.messages.push({ role: 'assistant', content: 'Error: ' + e, timestampUtc: new Date().toISOString() });
    } finally {
      this.streamingMessage = '';
      this.isStreaming = false;
      this.loadSessions(); // Refresh sessions list in case a new one was created
    }
  }

  logout(event: Event) {
    event.preventDefault();
    this.authService.logout().subscribe(() => this.router.navigate(['/login']));
  }
}
