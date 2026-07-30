import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';

export interface ChatSession {
  id: number;
  title: string;
  lastMessageUtc: string;
}

export interface ChatMessageAttachment {
  id?: number;
  fileName: string;
  contentType: string;
  base64Data?: string;
}

export interface ChatMessage {
  role: string;
  content: string;
  timestampUtc: string;
  attachments?: ChatMessageAttachment[];
}

export interface ChatStreamEvent {
  type: 'chunk' | 'status' | 'debug' | 'error';
  data: string;
}

@Injectable({
  providedIn: 'root'
})
export class ChatService {
  private http = inject(HttpClient);

  getSessions() {
    return this.http.get<ChatSession[]>('/api/chat/sessions');
  }

  getSession(id: number) {
    return this.http.get<{ id: number, title: string, messages: ChatMessage[] }>(`/api/chat/sessions/${id}`);
  }

  deleteSession(id: number) {
    return this.http.delete(`/api/chat/sessions/${id}`);
  }

  async *streamMessage(sessionId: number | null, message: string, attachments?: ChatMessageAttachment[], abortSignal?: AbortSignal): AsyncGenerator<ChatStreamEvent, void, unknown> {
    const response = await fetch('/api/chat/send', {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        'X-XSRF-TOKEN': this.getCookie('XSRF-TOKEN') || ''
      },
      body: JSON.stringify({ sessionId, message, attachments: attachments || [] }),
      signal: abortSignal
    });

    if (!response.ok) {
      throw new Error('Network response was not ok');
    }

    const reader = response.body?.getReader();
    const decoder = new TextDecoder('utf-8');

    if (!reader) return;

    let buffer = '';
    while (true) {
      const { done, value } = await reader.read();
      if (done) break;

      buffer += decoder.decode(value, { stream: true });
      
      let newlineIndex;
      while ((newlineIndex = buffer.indexOf('\n\n')) >= 0) {
        const eventBlock = buffer.slice(0, newlineIndex);
        buffer = buffer.slice(newlineIndex + 2);
        
        let eventType = 'chunk';
        let eventData = '';

        const lines = eventBlock.split('\n');
        for (const line of lines) {
          if (line.startsWith('event: ')) {
            eventType = line.substring(7);
          } else if (line.startsWith('data: ')) {
            if (eventData.length > 0) eventData += '\n';
            eventData += line.substring(6);
          }
        }

        if (eventData.length > 0) {
          yield { type: eventType as any, data: eventData };
        }
      }
    }
  }
  
  private getCookie(name: string): string | null {
    const value = `; ${document.cookie}`;
    const parts = value.split(`; ${name}=`);
    if (parts.length === 2) return parts.pop()?.split(';').shift() || null;
    return null;
  }
}
