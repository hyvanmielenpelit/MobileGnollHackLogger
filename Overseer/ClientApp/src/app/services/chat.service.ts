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
  asOf?: string | null;
}

export interface ChatCostEventData {
  /**
   * Null when no price is known for the model, and **also** null when the viewer is a regular user and
   * the turn was funded by a system AI configuration — in that case `isOperatorCost` is true. The pair
   * distinguishes "unpriced" from "withheld"; never render a withheld price as 0.
   */
  estimatedCost?: number | null;
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
  /**
   * Null when no price is known for the model, and **also** null when the viewer is a regular user and
   * the turn was funded by a system AI configuration — in that case `isOperatorCost` is true. The pair
   * distinguishes "unpriced" from "withheld"; never render a withheld price as 0.
   *
   * Replies saved before cost attribution existed carry no attribution and are read as user-funded, so
   * such a reply still shows operator cost to a regular user. Known and accepted (plan decision D1-A).
   */
  estimatedCost?: number | null;
  pricingSource?: string | null;
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
  /* The session reference in wire form: a decimal id for a saved chat, `eph_<guid>` for an
     incognito one. `id` is 0 for an incognito chat, which has no row and therefore no key. */
  sessionRef?: string;
  title: string;
  isGnollHackSession?: boolean;
  hasGameSnapshot?: boolean;
  /* The privacy badge, or null/absent when Confidentiality Mode is off — which is every
     session until Tier 2 lands. `state` is green, yellow, orange or red. */
  privateBadge?: { state: string; label: string; tooltip: string } | null;
  /** Whether the session is in Confidentiality Mode. One-way: it can be set, never cleared. */
  isConfidential?: boolean;
  /* Whether the session lives only in the server's memory: no session, message, tool-call or
     attachment row, and no file. Implies `isConfidential`, and is decided when the chat is
     created -- there is no upgrade to it, because an existing chat's rows are already
     written. */
  isEphemeral?: boolean;
  /* When an incognito chat expires if nothing touches it. Slides forward on every access, so
     it is the current deadline rather than a fixed one. Absent for a saved chat. */
  ephemeralExpiresUtc?: string;
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

  /* Every session-scoped call below takes `number | string`: a saved chat's reference is
     still its decimal id, so a number is passed through unchanged, and an incognito chat's is
     `eph_<guid>`. Widened rather than overloaded so a caller holding a reference of unknown
     kind -- which the chat component now does -- needs no branch. */
  getSession(id: number | string) {
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

  renameSession(sessionId: number | string, newTitle: string) {
    return this.http.put(`/api/chat/sessions/${sessionId}/title`, { title: newTitle });
  }

  /* isConfidential and isEphemeral are read by the server only when sessionId is null: both
     are properties of a chat's creation. isEphemeral without isConfidential is a 400, because
     incognito is a stricter form of Confidentiality Mode rather than an alternative to it. */
  sendMessage(sessionId: number | string | null, message: string, attachments?: ChatMessageAttachment[], userModelId?: number, systemModelId?: number, hasGreeted?: boolean, isConfidential?: boolean, isEphemeral?: boolean) {
    return this.http.post<{sessionId: string}>('/api/chat/send', {
      sessionId: sessionId === null ? null : String(sessionId),
      message,
      attachments: attachments || [],
      userModelId,
      systemModelId,
      hasGreeted,
      isConfidential: isConfidential ?? false,
      isEphemeral: isEphemeral ?? false
    });
  }

  /* Discards an incognito chat and overwrites what the server held. There is no trash and no
     recovery: a 404 means it was already gone, which a caller should treat as success. */
  closeEphemeralSession(sessionRef: string) {
    return this.http.post(`/api/chat/sessions/${sessionRef}/ephemeral/close`, {});
  }

  /* An incognito attachment is served from memory under its session's reference, because its
     id is an index within that session rather than a ChatMessageAttachment key. */
  ephemeralAttachmentUrl(sessionRef: string, attachmentId: number, inline: boolean = false) {
    return `/api/chat/sessions/${sessionRef}/attachments/${attachmentId}${inline ? '?inline=true' : ''}`;
  }

  attachGameSnapshot(sessionId: number | string | null, snapshotText: string, sourceGnollHackVersion?: string | null) {
    return this.http.post<{sessionId: number, hasGameSnapshot: boolean}>('/api/chat/sessions/attach-snapshot', {
      sessionId,
      snapshotText,
      sourceGnollHackVersion
    });
  }

  getSubAgents() {
    return this.http.get<SubAgentInfo[]>('/api/chat/subagents');
  }

  cancelSubAgent(sessionId: number | string, toolCallId: string) {
    return this.http.post<{success: boolean, message: string}>(`/api/chat/sessions/${sessionId}/subagents/${toolCallId}/cancel`, {});
  }

  cancelGeneration(sessionId: number | string) {
    return this.http.post<{success: boolean}>(`/api/chat/sessions/${sessionId}/cancel`, {});
  }
  
  private getCookie(name: string): string | null {
    const value = `; ${document.cookie}`;
    const parts = value.split(`; ${name}=`);
    if (parts.length === 2) return parts.pop()?.split(';').shift() || null;
    return null;
  }
}
