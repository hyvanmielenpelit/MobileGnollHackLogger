import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpResponse } from '@angular/common/http';

export interface ChatSession {
  id: number;
  title: string;
  lastMessageUtc: string;
  isGnollHackSession?: boolean;
  isPinned?: boolean;
}

export interface TrashSession {
  id: number;
  title: string;
  createdUtc: string;
  lastMessageUtc: string;
  deletedUtc: string;
  deletionReason?: string;
  daysRemaining: number;
  isPinned: boolean;
  isGnollHackSession?: boolean;
  messageCount: number;
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
  status: 'running' | 'completed' | 'error' | 'canceled' | 'iteration_limit' | 'budget_exhausted';
  agentName?: string;
  parentToolCallId?: string;
  depth?: number;
}

export interface SubAgentInfo {
  name: string;
  displayName: string;
  description: string;
  allowedTools: string[];
  maxIterations: number;
  isEnabled: boolean;
}

export interface ModelPricingDto {
  inputPerMillion: number;
  outputPerMillion: number;
  cachedInputPerMillion?: number | null;
  cacheWritePerMillion?: number | null;
  currency?: string | null;
  asOf?: string | null;
}

export interface ChatCostEventData {
  estimatedCost?: number | null;
  currency?: string | null;
  source?: string | null;
  inputTokens?: number;
  outputTokens?: number;
  cacheReadTokens?: number;
  cacheCreationTokens?: number;
  isOperatorCost?: boolean;
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
  reasoningMode?: string;
  serviceTier?: string;
  timeToFirstTokenMs?: number;
  totalDurationMs?: number;
  contextPromptTokens?: number;
  contextOutputTokens?: number;
  contextWindowTokens?: number;
  contextInputLimitTokens?: number;
  inputTokens?: number;
  outputTokens?: number;
  cacheReadTokens?: number;
  cacheCreationTokens?: number;
  estimatedCost?: number | null;
  pricingSource?: string | null;
  costCurrency?: string | null;
  isOperatorCost?: boolean;
}

/**
 * Context window occupancy as of the last assistant reply in a session. Sourced from the
 * provider's own token accounting for the final model call of that turn.
 */
export interface ChatContextUsage {
  usedTokens: number;
  windowTokens: number;
  inputLimitTokens?: number;
  promptTokens: number;
  outputTokens: number;
  modelDisplayName?: string;
}

export interface ChatStreamEvent {
  type: 'chunk' | 'status' | 'debug' | 'error' | 'sessionId' | 'tool_start' | 'tool_result' | 'tool_error' | 'title_update' | 'thinking_chunk' | 'ttft' | 'duration' | 'context' | 'cost' | 'final';
  data: string;
  seqNo?: number;
}

export interface ChatSessionsResponse {
  sessions: ChatSession[];
  hasMore: boolean;
  activeCount?: number;
  pinnedCount?: number;
  totalCount?: number;
  maxQuota?: number;
  maxPinned?: number;
}

export interface ChatSessionDetailResponse {
  id: number;
  title: string;
  isGnollHackSession?: boolean;
  hasGameSnapshot?: boolean;
  totalEstimatedCost?: number | null;
  messages: ChatMessage[];
  hasOngoingGeneration?: boolean;
  ongoingGeneration?: { events: ChatStreamEvent[] };
  lastEventSeqNo?: number;
}

@Injectable({
  providedIn: 'root'
})
export class ChatService {
  private http = inject(HttpClient);
  
  public hasGreeted: boolean = false;

  getSessions(skip: number = 0, take?: number, search?: string) {
    let url = `/api/chat/sessions?skip=${skip}`;
    if (take) {
      url += `&take=${take}`;
    }
    if (search && search.trim()) {
      url += `&search=${encodeURIComponent(search.trim())}`;
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

  togglePinSession(id: number) {
    return this.http.put<{isPinned: boolean}>(`/api/chat/sessions/${id}/pin`, {});
  }

  getTrashSessions(search?: string) {
    let url = '/api/chat/sessions/trash';
    if (search && search.trim()) {
      url += `?search=${encodeURIComponent(search.trim())}`;
    }
    return this.http.get<TrashSession[]>(url);
  }

  restoreSession(id: number) {
    return this.http.post(`/api/chat/sessions/${id}/restore`, {});
  }

  permanentDeleteSession(id: number) {
    return this.http.delete(`/api/chat/sessions/${id}/permanent`);
  }

  bulkDeleteSessions(includePinned: boolean = false) {
    return this.http.post<{count: number}>('/api/chat/sessions/bulk-delete', { includePinned });
  }

  unpinAllSessions() {
    return this.http.post<{count: number}>('/api/chat/sessions/unpin-all', {});
  }

  emptyTrash() {
    return this.http.post<{count: number}>('/api/chat/sessions/trash/empty', {});
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

  attachGameSnapshot(sessionId: number | null, snapshotText: string, sourceGnollHackVersion?: string | null) {
    return this.http.post<{sessionId: number, hasGameSnapshot: boolean}>('/api/chat/sessions/attach-snapshot', {
      sessionId,
      snapshotText,
      sourceGnollHackVersion
    });
  }

  getSubAgents() {
    return this.http.get<SubAgentInfo[]>('/api/chat/subagents');
  }

  cancelSubAgent(sessionId: number, toolCallId: string) {
    return this.http.post<{success: boolean, message: string}>(`/api/chat/sessions/${sessionId}/subagents/${toolCallId}/cancel`, {});
  }

  cancelGeneration(sessionId: number) {
    return this.http.post<{success: boolean}>(`/api/chat/sessions/${sessionId}/cancel`, {});
  }
  
  private getCookie(name: string): string | null {
    const value = `; ${document.cookie}`;
    const parts = value.split(`; ${name}=`);
    if (parts.length === 2) return parts.pop()?.split(';').shift() || null;
    return null;
  }
}
