import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpResponse } from '@angular/common/http';

export interface ChatSession {
  id: number;
  title: string;
  lastMessageUtc: string;
  isGnollHackSession?: boolean;
}

export interface ChatMessageAttachment {
  id?: number;
  fileName: string;
  contentType: string;
  base64Data?: string;
}

export interface ChatMessageToolCall {
  id?: string;
  name: string;
  displayName?: string;
  argsText?: string;
  parameters?: string;
  result?: string;
  error?: string;
  status: 'running' | 'completed' | 'error';
}

export interface ChatMessage {
  id?: number;
  role: string;
  content: string;
  timestampUtc: string;
  attachments?: ChatMessageAttachment[];
  toolCalls?: ChatMessageToolCall[];
  modelDisplayName?: string;
  thinkingLevel?: string;
  timeToFirstTokenMs?: number;
}

export interface ChatStreamEvent {
  type: 'chunk' | 'status' | 'debug' | 'error' | 'sessionId' | 'tool_start' | 'tool_result' | 'tool_error' | 'title_update' | 'thinking_chunk' | 'ttft' | 'final';
  data: string;
  seqNo?: number;
}
export interface ChatSessionsResponse {
  sessions: ChatSession[];
  hasMore: boolean;
}

export interface ChatSessionDetailResponse {
  id: number;
  title: string;
  isGnollHackSession?: boolean;
  messages: ChatMessage[];
  hasOngoingGeneration?: boolean;
  ongoingGeneration?: { events: ChatStreamEvent[] };
}

@Injectable({
  providedIn: 'root'
})
export class ChatService {
  private http = inject(HttpClient);
  
  public hasGreeted: boolean = false;

  getSessions(skip: number = 0, take?: number) {
    let url = `/api/chat/sessions?skip=${skip}`;
    if (take) {
      url += `&take=${take}`;
    }
    return this.http.get<ChatSessionsResponse>(url, {
      observe: 'response',
      headers: {
        'Cache-Control': 'no-cache',
        'Pragma': 'no-cache',
        'Expires': '0'
      }
    });
  }

  getSession(id: number) {
    return this.http.get<ChatSessionDetailResponse>(`/api/chat/sessions/${id}`, {
      observe: 'response',
      headers: {
        'Cache-Control': 'no-cache',
        'Pragma': 'no-cache',
        'Expires': '0'
      }
    });
  }

  deleteSession(id: number) {
    return this.http.delete(`/api/chat/sessions/${id}`);
  }

  reportMessage(messageId: number) {
    return this.http.post('/api/chat/report', { messageId });
  }

  renameSession(sessionId: number, newTitle: string) {
    return this.http.put(`/api/chat/sessions/${sessionId}/title`, { title: newTitle });
  }

  sendMessage(sessionId: number | null, message: string, attachments?: ChatMessageAttachment[], userModelId?: number, systemModelId?: number, hasGreeted?: boolean) {
    return this.http.post<{sessionId: number}>('/api/chat/send', {
      sessionId,
      message,
      attachments: attachments || [],
      userModelId,
      systemModelId,
      hasGreeted
    });
  }
  
  private getCookie(name: string): string | null {
    const value = `; ${document.cookie}`;
    const parts = value.split(`; ${name}=`);
    if (parts.length === 2) return parts.pop()?.split(';').shift() || null;
    return null;
  }
}
