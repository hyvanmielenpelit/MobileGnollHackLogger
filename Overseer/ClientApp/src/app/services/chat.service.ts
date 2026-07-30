import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';

export interface ChatSession {
  id: number;
  title: string;
  lastMessageUtc: string;
}

export interface ChatMessage {
  role: string;
  content: string;
  timestampUtc: string;
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

  // Note: For SSE streaming, we typically use fetch or EventSource directly 
  // since HttpClient doesn't natively support text/event-stream well yet without custom parsing.
  async *streamMessage(sessionId: number | null, message: string) {
    const response = await fetch('/api/chat/send', {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        // Get CSRF token from cookie if possible, but our HttpClientXsrfModule handles regular requests.
        // For fetch, we need to manually read the cookie.
        'X-XSRF-TOKEN': this.getCookie('XSRF-TOKEN') || ''
      },
      body: JSON.stringify({ sessionId, message })
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
        const event = buffer.slice(0, newlineIndex);
        buffer = buffer.slice(newlineIndex + 2);
        
        if (event.startsWith('event: error')) {
            const dataLine = event.split('\n').find(l => l.startsWith('data: '));
            if (dataLine) {
                const errData = JSON.parse(dataLine.substring(6));
                throw new Error(errData.message);
            }
        } else if (event.startsWith('data: ')) {
            const lines = event.split('\n');
            const dataContent = [];
            for (const line of lines) {
                if (line.startsWith('data: ')) {
                    dataContent.push(line.substring(6));
                }
            }
            if (dataContent.length > 0) {
                yield dataContent.join('\n');
            }
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
