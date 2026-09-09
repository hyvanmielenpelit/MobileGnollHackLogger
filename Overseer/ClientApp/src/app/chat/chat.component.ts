import { Component, OnInit, OnDestroy, inject, ChangeDetectorRef, ViewChild, ElementRef, HostListener, NgZone, AfterViewInit, ChangeDetectionStrategy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ChatService, ChatSession, ChatMessage, ChatMessageToolCall, ChatContextUsage } from '../services/chat.service';
import { AuthService } from '../services/auth.service';
import { DebugService } from '../services/debug.service';
import { Router, ActivatedRoute, RouterModule, NavigationEnd, NavigationStart } from '@angular/router';
import { MarkdownPipe } from './markdown.pipe';
import { RelativeTimePipe } from './relative-time.pipe';
import { SettingsService } from '../services/settings.service';
import { ChangelogService } from '../services/changelog.service';
import { ClientBridgeService } from '../services/client-bridge.service';
import { setSentryConfidentialSession } from '../utils/sentry-filter.util';
import { AdminAlertsComponent } from './admin-alerts.component';
import { TrashModalComponent } from '../shared/trash-modal/trash-modal.component';
import { AdminBenchmarkService } from '../services/admin-benchmark.service';
import { ensureOverlayPolyfills, refreshAnchorPositioning } from '../utils/polyfills.util';
import * as signalR from '@microsoft/signalr';
import { firstValueFrom, filter, Subscription, Subject, debounceTime, distinctUntilChanged } from 'rxjs';
export interface ToolClientRequest {
    type: string;
    requestId: string;
    toolName: string;
    parameters: any;
}

export interface ToolResponse {
    type: string;
    requestId: string;
    success: boolean;
    content: string;
    errorMessage: string | null;
}

/**
 * What actually reached the model from one attachment that was too large to send whole: the
 * count of retrieved parts, the coverage that implies, how they were selected, and anything
 * the extractor removed from the file.
 */
export interface AttachmentExcerptNotice {
    fileName: string;
    usedChunks: number;
    totalChunks: number;
    coveragePercent: number;
    /** Retrieval method as the server names it: 'embedding', 'bm25' or 'head'. */
    method: string;
    /** Active content stripped during extraction, described in words, e.g. "a VBA macro project". */
    removedActiveContent: string[];
    /** True when text extraction stopped at a size cap rather than at the end of the file. */
    wasTruncated: boolean;
}

@Component({
    selector: 'app-chat',
    imports: [CommonModule, FormsModule, RouterModule, MarkdownPipe, RelativeTimePipe, AdminAlertsComponent, TrashModalComponent],
    styleUrl: './chat.component.scss',
    changeDetection: ChangeDetectionStrategy.Eager,
    templateUrl: './chat.component.html'
})
export class ChatComponent implements OnInit, OnDestroy, AfterViewInit {
  private static readonly TOOL_DISPLAY_NAMES: Record<string, string> = {
    'get_full_message_history': 'Reading message history',
    'get_directory_listing': 'Reading game folder',
    'refresh_snapshot': 'Refreshing game status',
    'get_save_info': 'Reading save game info',
    'get_player_library': 'Reading manuals',
    'get_oracle_consultations': 'Reading consultations',
    'get_player_xlog': 'Reading recent games',
    'get_player_dumplogs': 'Reading player dumplogs',
    'get_app_log': 'Reading application log',
    'get_panic_log': 'Reading panic log',
    'item_lookup': 'Searching Wiki for an item',
    'monster_lookup': 'Searching Wiki for a monster',
    'nethack_wiki_search': 'Searching NetHack Wiki',
    'nethack_wiki_view': 'Viewing NetHack Wiki article',
    'search_server_dumplogs': 'Searching server dumplogs',
    'source_code_search': 'Searching GnollHack source code',
    'source_code_view': 'Viewing GnollHack source code',
    'wiki_search': 'Searching GnollHack Wiki',
    'list_indexed_files': 'Listing GnollHack source files',
    'get_constants': 'Searching GnollHack constants',
    'get_knowledge_article': 'Searching knowledge base',
    'wiki_view': 'Viewing GnollHack Wiki article',
    'search_definitions': 'Searching GnollHack definitions',
    'get_function_definition': 'Reading GnollHack function definition',
    'get_item_stats': 'Reading item stats',
    'get_monster_stats': 'Reading monster stats',
    'get_artifact_stats': 'Reading artifact stats',
    'get_github_repo_info': 'Retrieving GitHub repository information',
    'search_github': 'Searching GitHub',
    'delegate_to_subagent': 'Invoking subagent'
  };

  private static readonly NETHACK_TOOL_DISPLAY_NAMES: Record<string, string> = {
    'list_indexed_files': 'Listing NetHack source files',
    'source_code_search': 'Searching NetHack source code',
    'source_code_view': 'Viewing NetHack source code',
    'get_constants': 'Searching NetHack constants',
    'search_definitions': 'Searching NetHack definitions',
    'get_function_definition': 'Reading NetHack function definition'
  };

  private static titleCaseIdentifier(id: string): string {
    if (!id || typeof id !== 'string') return '';
    return id.trim()
      .split(/[_\-\s]+/)
      .filter(t => t.length > 0)
      .map(t => t.charAt(0).toUpperCase() + t.slice(1).toLowerCase())
      .join(' ');
  }

  private static lowercaseTypePhrase(typeName: string): string {
    if (!typeName || typeof typeName !== 'string') return '';
    return typeName.trim()
      .split(' ')
      .filter(w => w.length > 0)
      .map(w => /^[A-Z][a-z]*$/.test(w) ? w.toLowerCase() : w)
      .join(' ');
  }

  private static normalizeInstanceName(raw: any): string {
    if (!raw || typeof raw !== 'string') return '';
    const collapsed = raw.trim().replace(/\s+/g, ' ');
    return collapsed.length > 80 ? collapsed.substring(0, 80) : collapsed;
  }

  public static getToolDisplayName(name: string, argsOrRepo?: any): string {
    if (name === 'delegate_to_subagent') {
      if (typeof argsOrRepo === 'object' && argsOrRepo !== null) {
        const agentName = typeof argsOrRepo.agent_name === 'string' ? argsOrRepo.agent_name.trim() : '';
        if (agentName) {
          const typeName = ChatComponent.titleCaseIdentifier(agentName);
          const rawInstance = argsOrRepo.subagent_name ?? argsOrRepo.subagentName;
          const instance = ChatComponent.normalizeInstanceName(rawInstance);
          if (instance &&
              instance.toLowerCase() !== typeName.toLowerCase() &&
              instance.toLowerCase() !== agentName.toLowerCase()) {
            return `Invoking ${ChatComponent.lowercaseTypePhrase(typeName)} subagent: ${instance}`;
          }
          return `Invoking subagent: ${typeName}`;
        }
      }
      return ChatComponent.TOOL_DISPLAY_NAMES['delegate_to_subagent'] || 'Invoking subagent';
    }

    const isNetHack = typeof argsOrRepo === 'string'
      ? argsOrRepo.toLowerCase() === 'nethack'
      : (typeof argsOrRepo?.repository === 'string' && argsOrRepo.repository.toLowerCase() === 'nethack');

    if (isNetHack && ChatComponent.NETHACK_TOOL_DISPLAY_NAMES[name]) {
      return ChatComponent.NETHACK_TOOL_DISPLAY_NAMES[name];
    }
    return ChatComponent.TOOL_DISPLAY_NAMES[name] || name;
  }

  private buildToolArgsText(name: string, args: any): string {
    if (!args) return '';
    try {
      if (name === 'delegate_to_subagent') {
        return args.task ? `"${args.task}"` : '';
      }
      if (name === 'source_code_search') {
        if (args.query && args.file_filter) return `"${args.query}" in ${args.file_filter}`;
        if (args.query) return `"${args.query}"`;
      }
      if (name === 'source_code_view') {
        if (args.file && args.start_line) return `${args.file}:L${args.start_line}`;
        if (args.file) return args.file;
      }
      if (name === 'list_indexed_files') {
        return args.path_filter || '';
      }
      if (name === 'get_constants' && args.name) return args.name;
      if (name === 'search_definitions' && args.symbol) return args.symbol;
      if (name === 'get_function_definition' && args.name) return args.name;
      if (name === 'monster_lookup' && args.name) return args.name;
      if (name === 'item_lookup' && args.name) return args.name;
      if (name === 'wiki_search' && args.query) return args.query;
      if (name === 'wiki_view' && args.article) return args.article;
      if (name === 'nethack_wiki_search' && args.query) return args.query;
      if (name === 'nethack_wiki_view' && args.article) return args.article;
      if (name === 'get_knowledge_article') {
        if (args.topic_title) return args.topic_title;
        if (args.topic) return args.topic;
      }
      
      const firstKey = Object.keys(args).find(k => k !== 'repository');
      if (firstKey) return String(args[firstKey]);
    } catch (e) {}
    return '';
  }
  chatService = inject(ChatService);
  settingsService = inject(SettingsService);
  changelogService = inject(ChangelogService);
  
  showChangelogAnimation = false;
  showChatCost = true;
  liveCost: number | null = null;
  liveIsOperatorCost: boolean = false;
  sessionTotalCost: number | null = null;
  
  @ViewChild('messagesContainer') messagesContainer!: ElementRef;
  @ViewChild('deleteConfirmDialog') deleteConfirmDialog!: ElementRef<HTMLDialogElement>;
  @ViewChild('bulkDeleteConfirmDialog') bulkDeleteConfirmDialog!: ElementRef<HTMLDialogElement>;
  @ViewChild('unpinAllConfirmDialog') unpinAllConfirmDialog!: ElementRef<HTMLDialogElement>;
  @ViewChild('reportConfirmDialog') reportConfirmDialog!: ElementRef<HTMLDialogElement>;
  @ViewChild('imagePreviewDialog') imagePreviewDialog!: ElementRef<HTMLDialogElement>;
  @ViewChild('errorToast') errorToast!: ElementRef<HTMLElement>;
  @ViewChild('promptInput') promptInput!: ElementRef<HTMLTextAreaElement>;
  @ViewChild('renameInput') renameInput!: ElementRef<HTMLInputElement>;
  @ViewChild('logoutDialog') logoutDialog!: ElementRef<HTMLDialogElement>;
  @ViewChild('ephemeralCloseDialog') ephemeralCloseDialog!: ElementRef<HTMLDialogElement>;
  @ViewChild('trashModal') trashModal!: TrashModalComponent;
  autoScrollEnabled = true;
  readonly STREAMING_SCROLL_OFFSET = 50;

  activeSessionCount?: number;
  pinnedSessionCount = 0;
  maxSessionQuota = 50;
  maxPinnedQuota = 5;
  cancelingSubAgentId: string | null = null;
  trashCount = 0;

  includePinnedInBulkDelete = false;
  isBulkDeleting = false;
  isUnpinningAll = false;

  sessionSearchQuery = '';
  private sessionSearchSubject = new Subject<string>();
  private sessionSearchSub: Subscription | null = null;

  private hubConnection: signalR.HubConnection | null = null;
  private hubStartPromise: Promise<void> | null = null;

  sidebarWidth = 250;
  isResizing = false;
  isSidebarOpen = false;
  private resizeStartX = 0;
  private resizeStartWidth = 0;
  private mouseMoveListener: ((e: MouseEvent | TouchEvent) => void) | null = null;
  private mouseUpListener: (() => void) | null = null;
  private animationFrameId: number | null = null;
  
  @ViewChild('sidebar') sidebarEl!: ElementRef<HTMLElement>;
  @ViewChild('captureBoardDialog') captureBoardDialog?: ElementRef<HTMLDialogElement>;
  ngZone = inject(NgZone);
  authService = inject(AuthService);
  debugService = inject(DebugService);
  router = inject(Router);
  route = inject(ActivatedRoute);
  cdr = inject(ChangeDetectorRef);
  clientBridge = inject(ClientBridgeService);
  adminBenchmarkService = inject(AdminBenchmarkService);
  
  readonly CLIENT_TOOL_TIMEOUT_MS = 14000;
  pendingRequests = new Map<string, ReturnType<typeof setTimeout>>();

  isOffline = !navigator.onLine;
  private onlineHandler = () => { this.isOffline = false; this.cdr.detectChanges(); this.loadSessions(true); };
  private offlineHandler = () => { this.isOffline = true; this.cdr.detectChanges(); };
  sessions: ChatSession[] = [];
  hasMoreSessions = false;
  loadingMoreSessions = false;
  loadingSessions = true;
  private sessionLoadSub: Subscription | null = null;
  /* A session reference, not a database id: either a decimal id for a persisted chat
     ("1234") or "eph_<guid>" for an ephemeral one, which has no database row at all. */
  currentSessionId: string | null = null;
  sessionToDelete: number | null = null;
  messages: ChatMessage[] = [];
  
  copiedMsgIndex: number | null = null;
  copiedStreamMsg = false;
  copiedToolCallId: string | null = null;
  
  previewAttachment: any = null;
  
  currentInput = '';
  isStreaming = false;
  isThinkingActive = false;
  
  /* Avatar animation state */
  currentAvatarState: 'idle' | 'thinking' | 'talking' | 'toolUse' | 'yawning' = 'idle';
  private pendingAvatarState: string | null = null;
  private avatarSwapTimeout: any = null;
  private animStartTime: number = 0;
  private suppressImmediateSwap = false;
  private waitingForAvatarLoadToTransition = false;

  /* Flags that drive desired avatar state */
  get hasActiveToolCall(): boolean {
    return this.streamingToolCalls.some(tc => tc.status === 'running');
  }
  /* Network activity tracking for avatar state decisions */
  private hasEnteredWorkingPhase = false;
  private thinkingAnimStartTime: number = 0;
  private yawningCheckTimeout: any = null;
  private sessionStateMap = new Map<string, {
    requestStartTime: number;
    thinkingAnimStartTime: number;
    hasEnteredWorkingPhase: boolean;
  }>();

  /**
   * Animations in this set can be interrupted mid-loop for an immediate swap.
   * Animations NOT in this set will always play their current loop to completion
   * before transitioning. Add or remove names to tune responsiveness vs. smoothness.
   * Default: empty (all animations wait for loop end).
   */
  private static readonly INTERRUPTIBLE_ANIMATIONS: ReadonlySet<string> = new Set([
    // Add animation names here to allow mid-loop interrupts, e.g.:
    // 'thinking',
    // 'toolUse',
  ]);

  private static readonly IMMEDIATE_SWAP_THRESHOLD_MS = 250;

  private static readonly AVATAR_LOOP_DURATIONS: Record<string, number> = {
    thinking: 3569,  // 43 frames × 83ms
    talking:  3735,  // 45 frames × 83ms
    toolUse:  3818,  // 46 frames × 83ms
    yawning:  3735,  // 45 frames × 83ms
  };

  private static readonly AVATAR_SRCS: Record<string, string> = {
    thinking: '/img/gnoll-overseer-avatar-128x128-animated-thinking.webp',
    talking:  '/img/GnollOverseerAvatar-128x128-animated.webp',
    toolUse:  '/img/gnoll-overseer-avatar-128x128-animated-tools2.webp',
    yawning:  '/img/gnoll-overseer-avatar-128x128-animated-yawning.webp',
  };

  hasRealContent = false;
  realContentTimeout: any = null;
  pendingChunkBuffer: string = '';
  pendingChunkTimeout: any = null;
  streamingMessage = '';
  isFinishingAnimation = false;
  finishDoneTimeout: any = null;
  streamingToolCalls: ChatMessageToolCall[] = [];
  pendingAttachments: { file: File | null, base64: string, name: string, type: string }[] = [];
  timeToFirstTokenMs: number | null = null;
  totalDurationMs: number | null = null;
  /**
   * Context window occupancy as of the last assistant reply. Session state, not streaming
   * state: it deliberately survives a 'done' event and only resets on a session change.
   */
  contextUsage: ChatContextUsage | null = null;
  
  isGeneratingTitle = false;
  titleStatusText = '';
  lastSeenSeqNo: number = -1;
  private hasOngoingGeneration = false;
  isLoadingSession = false;
  private liveEventBuffer: any[] = [];
  private handoffTimeoutHandle: any = null;

  maxAttachmentSize = 15728640; // default 15MB

  /**
   * The formats the file picker offers, served by /api/settings from the same allowlist the
   * upload endpoint enforces. The default is the set every server build has accepted: a
   * settings request that fails, or a server too old to send the field, then still leaves a
   * usable dialog rather than one that accepts nothing at all. The server's list replaces it
   * wholesale, because the server is the only authority on what an upload survives.
   */
  attachmentAcceptExtensions: string[] =
    ['.html', '.htm', '.txt', '.md', '.png', '.jpg', '.jpeg', '.webp'];

  /** The picker's `accept` attribute value. */
  get attachmentAcceptAttr(): string {
    return this.attachmentAcceptExtensions.join(',');
  }

  /**
   * The accepted formats named for the user, derived from the same list as `accept`. It omits
   * the verb because the control's accessible name already carries it, and interestfor wires
   * this text up as the button's description: repeating "add attachments" would announce it
   * twice.
   */
  get attachmentFormatsHint(): string {
    const names = this.attachmentAcceptExtensions
      .map(e => e.replace(/^\./, '').toUpperCase())
      .filter(n => n.length > 0);
    return names.length > 0
      ? `Accepted formats: ${names.join(', ')}`
      : 'No attachment formats are accepted';
  }

  /** Lower-cased extensions without the leading dot, for checking a chosen or pasted file. */
  private get acceptedExtensions(): Set<string> {
    return new Set(this.attachmentAcceptExtensions
      .map(e => e.replace(/^\./, '').toLowerCase())
      .filter(e => e.length > 0));
  }

  /**
   * One entry per attachment of the current turn that the model saw only excerpts of. Per-turn
   * state, but deliberately not cleared on 'done': the notice has to still be there when the
   * reply turns out to be partial, which is long after a toast would have gone.
   */
  attachmentExcerpts: AttachmentExcerptNotice[] = [];

  /** Wording for `wasTruncated`, which is about the extraction and not about retrieval. */
  readonly excerptTruncationText =
    'Text extraction of this file stopped at a size limit, so even the parts counted above do not reach the end of it.';

  /**
   * What the model read of one attachment. It says "parts", never "summary": nothing was
   * summarised, whole sections simply never arrived.
   */
  excerptSummaryLine(n: AttachmentExcerptNotice): string {
    const name = n.fileName || 'this file';
    const counts = n.totalChunks > 0
      ? `${n.usedChunks} of the ${n.totalChunks} parts it was split into`
      : `${n.usedChunks} parts of it`;
    const coverage = n.coveragePercent >= 1
      ? `about ${Math.round(n.coveragePercent)}% of the document`
      : 'under 1% of the document';
    return `The assistant read parts of "${name}" — ${counts}, ${coverage}. It did not read the rest.`;
  }

  /**
   * Who chose the parts, in the user's terms rather than the retrieval method's own name. The
   * choice was made by a search before the assistant saw any of the file, so the sentence must
   * not read as the assistant having picked what to look at.
   */
  excerptSelectionLine(n: AttachmentExcerptNotice): string {
    const method = (n.method || '').toLowerCase();
    if (method === 'head') {
      return 'The parts were taken from the beginning of the document, before the assistant saw any of it.';
    }
    const search = method === 'embedding'
      ? 'a semantic search'
      : (method === 'bm25' ? 'a keyword search' : 'a search');
    return `The parts were chosen by ${search} over the document against your question, before the assistant saw any of it.`;
  }

  /** What the extractor stripped out of the file. Never omitted: the removal is security-relevant. */
  excerptRemovedLine(n: AttachmentExcerptNotice): string {
    const items = n.removedActiveContent.join(', ');
    return `Removed from the file before it was read: ${items}. None of it reached the assistant.`;
  }

  errorTitle = 'Error';
  errorMessage = '';
  isPinnedQuotaAlerting = false;
  hasApiKey = true;
  hasModel = true;
  isTitleGenerationInProgress = false;
  showThoughtsAndTools = 0;
  spoilerFreeMode = false;
  showParallelBadge = true;
  parallelBadgeEnabled = true;
  showContextWindowUsage = true;

  get shouldRenderParallelBadge(): boolean {
    return this.showParallelBadge && this.parallelBadgeEnabled;
  }

  userModels: import('../services/settings.service').UserAiModel[] = [];
  systemModels: import('../services/settings.service').UserAiModel[] = [];
  selectedModelKey: string | null = null;
  isModelDropdownOpen = false;
  singleModelInfo: any = null;
  
  isRenamingTitle = false;
  renameTitleValue = '';
  renameError: string | null = null;

  get streamingAvatarSrc(): string {
    if (this.currentAvatarState === 'idle') {
      return '/img/gnoll-overseer-avatar-128x128-static.webp';
    }
    return ChatComponent.AVATAR_SRCS[this.currentAvatarState] || ChatComponent.AVATAR_SRCS['thinking'];
  }

  get totalLoadedCost(): number {
    return this.messages.reduce((sum, m) => sum + (m.role === 'assistant' ? (m.estimatedCost || 0) : 0), 0) + (this.liveCost || 0);
  }

  /**
   * Every cost figure in the chat: per reply, live, and the conversation total.
   * Dollars at or above $1, cents below it — a single reply, and often a whole short chat,
   * costs a fraction of a cent, and "$0.00" would read as free. Two decimals of a cent
   * resolve to $0.0001; anything smaller but non-zero prints "<0.01¢" rather than
   * rounding to zero.
   */
  formatCost(cost: number | null | undefined): string {
    if (cost == null) return '';
    if (cost >= 1) return `$${cost.toFixed(2)}`;
    const cents = cost * 100;
    if (cost > 0 && cents < 0.01) return '<0.01¢';
    return `${cents.toFixed(2)}¢`;
  }

  /** True if any loaded assistant turn, or the in-flight turn, carries a resolved price. */
  get hasAnyLoadedCost(): boolean {
    return this.liveCost != null
      || this.messages.some(m => m.role === 'assistant' && m.estimatedCost != null);
  }

  /**
   * True if some assistant turn ran unpriced, so the total is real but incomplete. An operator-funded
   * turn whose price this viewer may not see is not "unpriced" — the operator paid for it and it is
   * correctly outside the user's total, so it must not raise the PARTIAL badge.
   */
  get isChatCostPartial(): boolean {
    return this.messages.some(m =>
      m.role === 'assistant' && m.estimatedCost == null && !m.isOperatorCost);
  }

  /**
   * Authoritative chat cost in USD: the persisted session total plus the in-flight turn.
   * Falls back to summing loaded per-message costs for chats saved before the session total
   * existed. Null means "no cost is known" — the indicator is hidden entirely.
   */
  get totalChatCost(): number | null {
    if (this.sessionTotalCost == null) {
      return this.hasAnyLoadedCost ? this.totalLoadedCost : null;
    }
    return this.sessionTotalCost + (this.liveCost ?? 0);
  }

  /** The chat total as displayed: same dollars-or-cents rule as every per-reply figure. */
  get formattedChatCost(): string {
    return this.formatCost(this.totalChatCost);
  }

  get chatCostTooltip(): string {
    let tip = 'Total estimated cost of this chat.';
    if (this.isChatCostPartial) {
      tip += ' Some responses ran on a model with no configured pricing and are not included.';
    }
    return tip;
  }

  /**
   * Determines the desired avatar state from the current flags
   * and requests a loop-boundary-aware transition if it differs
   * from the current state.
   */
  private updateDesiredAvatarState() {
    let desired: 'idle' | 'thinking' | 'talking' | 'toolUse' | 'yawning' = 'idle';

    if (!this.isStreaming) {
      desired = 'idle';
    } else if (this.hasRealContent) {
      desired = 'talking';
    } else {
      // Pre-response phase: tools, thinking texts, or waiting
      if (this.hasActiveToolCall || this.isThinkingActive || this.pendingChunkBuffer.length > 0) {
        this.hasEnteredWorkingPhase = true;
        if (this.currentSessionId) {
          const state = this.sessionStateMap.get(this.currentSessionId);
          if (state) state.hasEnteredWorkingPhase = true;
        }
      }

      if (this.hasEnteredWorkingPhase) {
        desired = 'toolUse';
      } else {
        // Initial wait phase
        desired = 'thinking';
      }
    }

    // Yawning override: only from Thinking state, after 30s of continuous Thinking
    if (desired === 'thinking') {
      if (this.thinkingAnimStartTime === 0) {
        this.thinkingAnimStartTime = performance.now();
      }
      
      const elapsedThinking = performance.now() - this.thinkingAnimStartTime;
      if (elapsedThinking > 30000) {
        desired = 'yawning';
      } else {
        // We are in thinking, but not yet 30s. Ensure we check exactly when 30s hits.
        if (!this.yawningCheckTimeout) {
          this.yawningCheckTimeout = setTimeout(() => {
            this.yawningCheckTimeout = null;
            if (this.isStreaming) {
              this.updateDesiredAvatarState();
            }
          }, 30000 - elapsedThinking);
        }
      }
    } else {
      this.thinkingAnimStartTime = 0; // reset when leaving thinking
      if (this.yawningCheckTimeout) {
        clearTimeout(this.yawningCheckTimeout);
        this.yawningCheckTimeout = null;
      }
    }

    this.requestAvatarTransition(desired);
  }

  /**
   * Schedules an avatar swap at the next loop boundary of the
   * currently playing animation. If a transition is already
   * pending, it is replaced (the timer still fires at the
   * original boundary, but applies the latest desired state).
   *
   * If the current animation has been playing for less than
   * IMMEDIATE_SWAP_THRESHOLD_MS, it is replaced immediately
   * (the user barely saw it). This prevents rapid state
   * changes from locking in the first animation for a full
   * loop while subsequent changes queue up.
   */
  private requestAvatarTransition(newState: string) {
    if (newState === this.currentAvatarState && !this.pendingAvatarState) {
      this.suppressImmediateSwap = false;
      return;
    }

    // If no animation is playing yet, or current state is idle, switch immediately
    if (this.animStartTime === 0 || this.currentAvatarState === 'idle') {
      this.suppressImmediateSwap = false;
      this.applyAvatarState(newState);
      return;
    }

    const elapsed = performance.now() - this.animStartTime;

    // Check if the current animation can be interrupted mid-loop
    if (ChatComponent.INTERRUPTIBLE_ANIMATIONS.has(this.currentAvatarState)) {
      if (this.avatarSwapTimeout) {
        clearTimeout(this.avatarSwapTimeout);
        this.avatarSwapTimeout = null;
      }
      this.pendingAvatarState = null;
      this.suppressImmediateSwap = false;
      this.applyAvatarState(newState);
      return;
    }

    // Update the pending state (timer will pick up the latest)
    this.pendingAvatarState = newState;

    // Only schedule a new timer if one isn't already running
    if (this.avatarSwapTimeout) return;

    const loopDuration = ChatComponent.AVATAR_LOOP_DURATIONS[this.currentAvatarState] || 1000;
    const timeInLoop = elapsed % loopDuration;
    const timeUntilLoopEnd = loopDuration - timeInLoop;

    this.avatarSwapTimeout = setTimeout(() => {
      this.avatarSwapTimeout = null;
      this.suppressImmediateSwap = false;
      if (this.pendingAvatarState && this.pendingAvatarState !== this.currentAvatarState) {
        this.applyAvatarState(this.pendingAvatarState);
      }
      this.pendingAvatarState = null;
    }, timeUntilLoopEnd);
  }

  private applyAvatarState(state: string) {
    this.currentAvatarState = state as any;
    // animStartTime will be set precisely by the (load) event
    this.animStartTime = performance.now(); // fallback in case load doesn't fire (cached)
    this.cdr.detectChanges();
  }

  /** Called by the <img (load)> event to precisely record when playback starts */
  onAvatarLoaded() {
    this.animStartTime = performance.now();
    if (this.waitingForAvatarLoadToTransition) {
      this.waitingForAvatarLoadToTransition = false;
      if (this.avatarSwapTimeout) {
        clearTimeout(this.avatarSwapTimeout);
        this.avatarSwapTimeout = null;
      }
      this.updateDesiredAvatarState();
    }
  }



  private resetAvatarState() {
    if (this.avatarSwapTimeout) {
      clearTimeout(this.avatarSwapTimeout);
      this.avatarSwapTimeout = null;
    }
    if (this.yawningCheckTimeout) {
      clearTimeout(this.yawningCheckTimeout);
      this.yawningCheckTimeout = null;
    }
    this.currentAvatarState = 'idle';
    this.pendingAvatarState = null;
    this.animStartTime = 0;
    this.suppressImmediateSwap = false;
    this.waitingForAvatarLoadToTransition = false;
    if (this.avatarSwapTimeout) {
      clearTimeout(this.avatarSwapTimeout);
      this.avatarSwapTimeout = null;
    }
  }

  private preloadAvatarImages() {
    Object.values(ChatComponent.AVATAR_SRCS).forEach(src => {
      const img = new Image();
      img.src = src;
    });
  }

  /* currentSessionId is a session *reference* -- a decimal id for a saved chat, `eph_<guid>`
     for an incognito one -- while a sidebar row carries a numeric id. Comparing them directly
     is always false, and silently: the template would simply stop highlighting the open chat. */
  isCurrentSession(id: number | string): boolean {
    return this.currentSessionId !== null && String(id) === this.currentSessionId;
  }

  get currentTitle(): string {
    if (this.isEphemeralSession) return 'Incognito chat';
    const session = this.sessions.find(s => String(s.id) === this.currentSessionId);
    return session ? session.title : 'New Chat';
  }

  get selectedModel() {
    if (!this.selectedModelKey) return undefined;
    if (this.selectedModelKey.startsWith('s_')) {
      const sId = Number(this.selectedModelKey.substring(2));
      return this.systemModels.find(m => m.id === sId);
    } else if (this.selectedModelKey.startsWith('u_')) {
      const uId = Number(this.selectedModelKey.substring(2));
      return this.userModels.find(m => m.id === uId);
    }
    const id = Number(this.selectedModelKey);
    return this.userModels.find(m => m.id === id) || 
           this.systemModels.find(m => m.id === id);
  }

  toggleModelDropdown(event: Event) {
    event.stopPropagation();
    this.isModelDropdownOpen = !this.isModelDropdownOpen;
  }
  
  startRename() {
    this.isRenamingTitle = true;
    this.renameTitleValue = this.currentTitle;
    this.renameError = null;
    setTimeout(() => {
      this.renameInput?.nativeElement?.focus();
    }, 0);
  }
  
  saveRename() {
    if (!this.isRenamingTitle) return;
    
    const newTitle = this.renameTitleValue.trim();
    if (!newTitle) {
      this.renameError = 'Chat title cannot be empty.';
      return;
    }
    
    if (/[<>{}[\]\\\/]/.test(newTitle)) {
      this.renameError = 'Chat title contains illegal characters. Please remove any < > { } [ ] \\ or /';
      return;
    }
    
    this.isRenamingTitle = false;
    this.renameError = null;
    
    const sessionRef = this.currentSessionId;
    if (!sessionRef || this.isEphemeralSession) return;

    const session = this.sessions.find(s => String(s.id) === sessionRef);
    if (session && session.title !== newTitle) {
      session.title = newTitle;
      this.chatService.renameSession(sessionRef, newTitle).subscribe({
        error: (err) => console.error('Failed to rename session', err)
      });
    }
  }
  
  cancelRename() {
    this.isRenamingTitle = false;
    this.renameError = null;
  }

  selectModel(model: import('../services/settings.service').UserAiModel | undefined) {
    if (model && model.id !== undefined) {
      const key = (model.isSystem ? 's_' : 'u_') + model.id;
      this.selectedModelKey = key;
      localStorage.setItem('overseer_chat_model_global', key);
      /* An ephemeral chat leaves no per-session key behind: it would name a chat that no
         longer exists and never be cleaned up. */
      if (this.currentSessionId !== null && !this.isEphemeralSession) {
        localStorage.setItem(`overseer_chat_model_session_${this.currentSessionId}`, key);
      }
    }
    this.isModelDropdownOpen = false;
  }

  private findModelByKey(key: string | null): import('../services/settings.service').UserAiModel | undefined {
    if (!key) return undefined;
    if (key.startsWith('s_')) {
      const sId = Number(key.substring(2));
      return this.systemModels.find(m => m.id === sId);
    }
    if (key.startsWith('u_')) {
      const uId = Number(key.substring(2));
      return this.userModels.find(m => m.id === uId);
    }
    const id = Number(key);
    if (!isNaN(id)) {
      return this.userModels.find(m => m.id === id) || this.systemModels.find(m => m.id === id);
    }
    return undefined;
  }

  applySavedModelPreference() {
    if (this.userModels.length === 0 && this.systemModels.length === 0) return;

    let targetKey: string | null = null;

    if (this.currentSessionId !== null) {
      const sessionPref = localStorage.getItem(`overseer_chat_model_session_${this.currentSessionId}`);
      if (sessionPref) targetKey = sessionPref;
    }

    if (!targetKey || !this.findModelByKey(targetKey)) {
      const globalPref = localStorage.getItem('overseer_chat_model_global');
      if (globalPref) targetKey = globalPref;
    }

    const matchedModel = this.findModelByKey(targetKey);
    if (matchedModel && matchedModel.id !== undefined) {
      this.selectedModelKey = (matchedModel.isSystem ? 's_' : 'u_') + matchedModel.id;
    } else {
      if (this.userModels.length > 0 && this.userModels[0].id !== undefined) {
        this.selectedModelKey = 'u_' + this.userModels[0].id;
      } else if (this.systemModels.length > 0 && this.systemModels[0].id !== undefined) {
        this.selectedModelKey = 's_' + this.systemModels[0].id;
      } else {
        this.selectedModelKey = null;
      }
    }
  }

  formatThinkingLevel(level: string | undefined): string {
    if (!level) return 'Default';
    return level.charAt(0).toUpperCase() + level.slice(1);
  }

  showReasoningBadge(mode: string | null | undefined): boolean {
    if (!mode) return false;
    const lower = mode.toLowerCase();
    return lower !== 'default' && lower !== 'standard';
  }

  formatServiceTier(tier: string | null | undefined): string {
    if (!tier) return '';
    if (tier.toLowerCase() === 'standard_only') return 'Standard Only';
    return tier.charAt(0).toUpperCase() + tier.slice(1);
  }



  @HostListener('document:click', ['$event'])
  onDocumentClick(event: MouseEvent) {
    if (this.isModelDropdownOpen) {
      const target = event.target as HTMLElement;
      if (!target.closest('.custom-model-selector')) {
        this.isModelDropdownOpen = false;
      }
    }
  }

  isReporting = false;
  reportingMessageId: number | null = null;
  reportedMsgIndex: number | null = null;
  reportSuccessIndex: number | null = null;
  reportError = '';
  showDebugLog = localStorage.getItem('showDebugLog') === 'true';

  hasGameSnapshot = false;

  /* The privacy badge for the loaded session, or null when Confidentiality Mode is off — which
     is every session until Stage E of the privacy framework adds the mode. `state` is one of
     green, yellow, orange, red; the tooltip enumerates the active controls and the provider's
     retention posture. */
  privateBadge: { state: string; label: string; tooltip: string } | null = null;

  /** Whether the open chat is in Confidentiality Mode. Drives the upgrade action's availability. */
  isConfidentialSession = false;

  /** Whether the open chat is ephemeral: held in RAM, with no database row and no file on disk. */
  isEphemeralSession = false;

  /* The two privacy choices for the *next* chat. Both are only meaningful while no chat is
     open, because a persisted chat's rows are already written and an ephemeral chat cannot
     be created retroactively. */
  newChatConfidential = false;
  newChatEphemeral = false;

  /** Whether the privacy panel above the composer is expanded. */
  isPrivacyPanelOpen = false;

  /** Whether the "what incognito does and does not do" detail is expanded in the banner. */
  isEphemeralDetailOpen = false;

  isClosingEphemeral = false;
  ephemeralCloseError: string | null = null;

  /** Announced politely when an ephemeral chat is destroyed or is about to be left behind. */
  ephemeralNotice = '';

  /* Where to go once the open ephemeral chat has been closed. Set by the navigation guard so
     the click that triggered the confirmation still lands after the chat is destroyed. */
  private pendingEphemeralNavigation: { kind: 'new' } | { kind: 'session'; id: number } | null = null;
  private ephemeralNoticeTimeout: any = null;
  private navigationStartSub: Subscription | null = null;

  /* The browser's own leave prompt, which is all a tab close allows. Browsers show it only
     after the user has interacted with the page, and its wording is not ours to set. */
  private beforeUnloadHandler = (event: BeforeUnloadEvent) => {
    if (!this.hasEphemeralContent) return;
    event.preventDefault();
    event.returnValue = '';
  };
  captureBoardMode: 'live' | 'attached' = 'live';
  showCaptureBoardModal = false;
  captureBoardName = '';
  captureBoardNotes = '';
  captureBoardVersion = '';
  isCapturingBoard = false;
  captureBoardError: string | null = null;
  captureBoardResult: {
    boardName: string;
    charCount: number;
    shaPrefix: string;
    suiteName: string;
    suiteId: number;
    wasRenamed?: boolean;
    requestedName?: string;
  } | null = null;

  localToolRequests = new Map<string, {
    resolve: (content: string) => void;
    reject: (err: any) => void;
    timer: ReturnType<typeof setTimeout>;
  }>();
  isAttachingSnapshot = false;

  openCaptureBoardModal(event?: Event, mode: 'live' | 'attached' = 'live') {
    if (event) {
      event.preventDefault();
    }
    this.captureBoardMode = mode;
    this.captureBoardError = null;
    this.captureBoardResult = null;
    const now = new Date().toISOString().substring(0, 10);
    this.captureBoardName = `Board ${now}`;
    this.captureBoardNotes = '';
    this.captureBoardVersion = '';
    this.showCaptureBoardModal = true;
    this.cdr.detectChanges();
    if (this.captureBoardDialog?.nativeElement) {
      ensureOverlayPolyfills();
      this.captureBoardDialog.nativeElement.showModal();
    }
  }

  closeCaptureBoardModal() {
    this.captureBoardDialog?.nativeElement?.close();
    this.showCaptureBoardModal = false;
    this.captureBoardError = null;
    this.cdr.detectChanges();
  }

  submitCaptureBoard() {
    /* An ephemeral chat has nothing to capture from: a board snapshot is a stored row, which
       is exactly what this mode does not produce. */
    const sessionRef = this.currentSessionId;
    if (!sessionRef || this.isEphemeralSession || !this.captureBoardName.trim()) {
      return;
    }
    this.isCapturingBoard = true;
    this.captureBoardError = null;
    this.captureBoardResult = null;
    this.cdr.detectChanges();

    const requestedName = this.captureBoardName.trim();
    const req = {
      sessionId: sessionRef,
      name: requestedName,
      notes: this.captureBoardNotes.trim() || undefined,
      sourceGnollHackVersion: this.captureBoardVersion.trim() || undefined
    };

    const call$ = this.captureBoardMode === 'attached'
      ? this.adminBenchmarkService.saveAttachedSnapshot(req)
      : this.adminBenchmarkService.captureSnapshot(req);

    call$.subscribe({
      next: (res) => {
        this.isCapturingBoard = false;
        this.captureBoardResult = {
          boardName: res.board.name,
          charCount: res.board.charCount,
          shaPrefix: res.board.sha256 ? res.board.sha256.substring(0, 8) : '',
          suiteName: res.suite.name,
          suiteId: res.suite.id,
          wasRenamed: res.board.name !== requestedName,
          requestedName
        };
        this.cdr.detectChanges();
      },
      error: (err) => {
        this.isCapturingBoard = false;
        this.captureBoardError = err?.error?.error || err?.error?.message || (typeof err?.error === 'string' ? err.error : null) || 'Failed to capture benchmark board.';
        this.cdr.detectChanges();
      }
    });
  }

  async attachGameSnapshotFromClient() {
    if (!this.clientBridge.isEmbedded() || this.hasGameSnapshot || this.isAttachingSnapshot) {
      return;
    }

    this.isAttachingSnapshot = true;
    this.cdr.detectChanges();

    const requestId = 'local_' + Math.random().toString(36).substring(2, 11) + '_' + Date.now();

    try {
      const snapshotText = await new Promise<string>((resolve, reject) => {
        const timer = setTimeout(() => {
          this.localToolRequests.delete(requestId);
          reject(new Error('Game client timed out responding to snapshot request.'));
        }, 45000);

        this.localToolRequests.set(requestId, { resolve, reject, timer });

        this.clientBridge.postMessage({
          type: 'tool_client_request',
          requestId,
          toolName: 'refresh_snapshot',
          parameters: {}
        });
      });

      this.chatService.attachGameSnapshot(this.currentSessionId, snapshotText).subscribe({
        next: (res) => {
          this.isAttachingSnapshot = false;
          this.hasGameSnapshot = true;
          const newSessionId = String(res.sessionId);
          if (this.currentSessionId !== newSessionId) {
            this.currentSessionId = newSessionId;
            this.isEphemeralSession = ChatComponent.isEphemeralRef(newSessionId);
            this.clientBridge.notifySessionChanged(newSessionId);

            if (this.hubConnection?.state === signalR.HubConnectionState.Connected) {
              this.hubConnection.invoke("JoinSession", this.currentSessionId).catch(console.error);
            }
            this.loadSessions(true);

            if (!this.isEphemeralSession) {
              const urlTree = this.router.createUrlTree([], {
                relativeTo: this.route,
                queryParams: { sessionId: this.currentSessionId },
                queryParamsHandling: 'merge'
              });
              this.router.navigateByUrl(urlTree, { replaceUrl: true });
            }
          }
          this.cdr.detectChanges();
        },
        error: (err) => {
          this.isAttachingSnapshot = false;
          console.error('Failed to attach game snapshot:', err);
          this.showErrorToast(err?.error?.error || 'Failed to attach game snapshot to chat.', 'Snapshot Error');
          this.cdr.detectChanges();
        }
      });
    } catch (err: any) {
      this.isAttachingSnapshot = false;
      console.error('Failed to request snapshot from client bridge:', err);
      this.showErrorToast(err?.message || 'Failed to request snapshot from game client.', 'Bridge Error');
      this.cdr.detectChanges();
    }
  }

  currentStatusText = '';
  showSpinner = false;
  requestStartTime = 0;
  abortController: AbortController | null = null;
  timeUpdateInterval: any;

  get isHandoffWaiting(): boolean {
    if (this.messages.length > 0) return false;
    if (this.streamingMessage) return false;
    if (this.currentStatusText.startsWith('Error')) return false;
    
    if (this.isStreaming || this.showSpinner || this.currentStatusText || this.hasOngoingGeneration) return true;
    
    return false;
  }

  @HostListener('window:focus')
  onWindowFocus() {
    this.focusPromptInput();
  }

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
      clampedScrollTop = Math.min(targetScrollTop, Math.max(0, (streamingEl as HTMLElement).offsetTop - this.STREAMING_SCROLL_OFFSET));
    }
    
    // Re-engage auto-scroll if user manually scrolls back to the target position
    if (Math.abs(container.scrollTop - clampedScrollTop) < 10) {
      this.autoScrollEnabled = true;
    }
  }

  ngOnDestroy() {
    window.removeEventListener('online', this.onlineHandler);
    window.removeEventListener('offline', this.offlineHandler);
    window.removeEventListener('beforeunload', this.beforeUnloadHandler);
    window.removeEventListener('changelog_badge_reset', this.changelogBadgeResetHandler);
    this.navigationStartSub?.unsubscribe();
    if (this.ephemeralNoticeTimeout) {
      clearTimeout(this.ephemeralNoticeTimeout);
      this.ephemeralNoticeTimeout = null;
    }
    if (this.hubConnection) {
      this.hubConnection.stop();
    }
    
    (window as any).onGnollHackToolResponse = undefined;
    (window as any).__gnollhackReceiveFiles = undefined;
    this.pendingRequests.forEach(timer => clearTimeout(timer));
    this.pendingRequests.clear();
    this.localToolRequests.forEach(req => clearTimeout(req.timer));
    this.localToolRequests.clear();
    this.sessionSearchSub?.unsubscribe();
    if (this.timeUpdateInterval) clearInterval(this.timeUpdateInterval);
    if (this.handoffTimeoutHandle) { clearTimeout(this.handoffTimeoutHandle); this.handoffTimeoutHandle = null; }

    // Restore document overflow and clean up viewport listener
    document.documentElement.style.overflow = '';
    if (window.visualViewport) {
      window.visualViewport.removeEventListener('resize', this.onVisualViewportResize);
    }
    document.documentElement.style.removeProperty('--viewport-height');
  }

  private onVisualViewportResize = () => {
    if (window.visualViewport) {
      document.documentElement.style.setProperty(
        '--viewport-height',
        `${window.visualViewport.height}px`
      );
      // Defensive: force document scroll to top in case any browser behavior
      // manages to scroll despite overflow:hidden
      requestAnimationFrame(() => window.scrollTo(0, 0));
    }
  };

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
      const maxScroll = Math.max(0, (streamingEl as HTMLElement).offsetTop - this.STREAMING_SCROLL_OFFSET);
      finalScrollTop = Math.min(targetScrollTop, maxScroll);
    }
    
    if (container.scrollTop !== finalScrollTop) {
      container.scrollTo({
        top: finalScrollTop,
        behavior: smooth ? 'smooth' : 'auto'
      });
    }
  }

  ngAfterViewInit() {
    this.focusPromptInput();
    
    if (this.logoutDialog && this.logoutDialog.nativeElement) {
      if (!('closedBy' in HTMLDialogElement.prototype)) {
        this.logoutDialog.nativeElement.addEventListener('click', (event: MouseEvent) => {
          const dialog = this.logoutDialog.nativeElement;
          if (event.target !== dialog) return;
          const rect = dialog.getBoundingClientRect();
          const isDialogContent = (
            rect.top <= event.clientY &&
            event.clientY <= rect.top + rect.height &&
            rect.left <= event.clientX &&
            event.clientX <= rect.left + rect.width
          );
          if (!isDialogContent) {
            dialog.close();
          }
        });
      }
    }
  }

  private focusPromptInput() {
    setTimeout(() => {
      if (this.promptInput && this.promptInput.nativeElement) {
        this.promptInput.nativeElement.focus();
      }
    }, 100);
    setTimeout(() => {
      if (this.promptInput && this.promptInput.nativeElement) {
        this.promptInput.nativeElement.focus();
      }
    }, 500);
    setTimeout(() => {
      if (this.promptInput && this.promptInput.nativeElement) {
        this.promptInput.nativeElement.focus();
      }
    }, 1000);
  }

  perfLog(tag: string, message: string, startTime?: number) {
    const now = performance.now();
    const duration = startTime !== undefined ? ` (${(now - startTime).toFixed(1)}ms)` : '';
    const logStr = `[Perf][${tag}] ${message}${duration}`;
    this.debugService.log(logStr);
  }

  loadSettings(isInit: boolean = false) {
    const t0 = performance.now();
    this.perfLog('Settings', `loadSettings started (isInit=${isInit})`);
    if (isInit) {
      this.loadSessions();
    }

    this.settingsService.getSettingsResponse().subscribe({
      next: (httpResponse) => {
        const settingsDuration = performance.now() - t0;
        const settings = httpResponse.body;
        const serverTiming = httpResponse.headers.get('Server-Timing');
        const timingLog = serverTiming ? ` | Server-Timing: ${serverTiming}` : '';
        this.perfLog('Settings', `getSettings received in ${settingsDuration.toFixed(1)}ms${timingLog}`);
        setTimeout(() => {
          const entries = performance.getEntriesByType('resource') as PerformanceResourceTiming[];
          const settingsEntry = entries.filter(e => e.name.endsWith('/api/settings')).pop();
          if (settingsEntry) {
            this.perfLog('Settings', `ResourceTiming: startTime=${settingsEntry.startTime.toFixed(0)}, fetchStart=${settingsEntry.fetchStart.toFixed(0)}, requestStart=${settingsEntry.requestStart.toFixed(0)}, responseStart=${settingsEntry.responseStart.toFixed(0)}, responseEnd=${settingsEntry.responseEnd.toFixed(0)}, duration=${settingsEntry.duration.toFixed(0)}, stalled=${(settingsEntry.requestStart - settingsEntry.startTime).toFixed(0)}ms, ttfb=${(settingsEntry.responseStart - settingsEntry.requestStart).toFixed(0)}ms`);
          }
        }, 100);
        if (settings) {
          this.hasApiKey = settings.hasApiKey;
          this.hasModel = settings.hasModel ?? false;
          if (settings.maxAttachmentSize) {
            this.maxAttachmentSize = settings.maxAttachmentSize;
          }
          /* An empty list is treated as no answer and keeps the default: an accept attribute
             offering nothing would leave the user unable to pick any file at all. */
          if (Array.isArray(settings.attachmentAcceptExtensions)) {
            const served = settings.attachmentAcceptExtensions
              .filter(e => typeof e === 'string' && e.trim().length > 0)
              .map(e => e.trim().toLowerCase());
            if (served.length > 0) {
              this.attachmentAcceptExtensions = served;
            }
          }
          this.showDebugLog = settings.showDebugLog ?? false;
          localStorage.setItem('showDebugLog', this.showDebugLog.toString());
          this.debugService.setEnabled(this.showDebugLog);
          
          this.showThoughtsAndTools = Number(settings.showThoughtsAndTools ?? 0);
          this.spoilerFreeMode = settings.spoilerFreeMode === true;
          this.showParallelBadge = settings.showParallelBadge ?? true;
          this.showContextWindowUsage = settings.showContextWindowUsage ?? true;
          this.showChatCost = settings.showChatCost ?? true;
          this.parallelBadgeEnabled = settings.parallelBadgeEnabled ?? true;
          this.debugService.log(`[Overseer] showThoughtsAndTools loaded: ${this.showThoughtsAndTools} (type: ${typeof this.showThoughtsAndTools})`);

          const tModels0 = performance.now();
          this.settingsService.getUserModels().subscribe({
            next: (models) => {
              const modelsDuration = performance.now() - tModels0;
              this.perfLog('Settings', `getUserModels received in ${modelsDuration.toFixed(1)}ms (${models.length} models)`);
              this.userModels = models.filter(m => !m.isSystem && (m.modelRole === undefined || (m.modelRole & 1) === 1));
              this.systemModels = models.filter(m => m.isSystem && (m.modelRole === undefined || (m.modelRole & 1) === 1));
              this.hasModel = this.userModels.length > 0 || this.systemModels.length > 0;
              
              if (this.hasModel) {
                this.applySavedModelPreference();
              } else {
                this.singleModelInfo = null;
                this.selectedModelKey = null;
              }
            },
            error: (err) => {
              const modelsDuration = performance.now() - tModels0;
              this.perfLog('Settings', `getUserModels FAILED in ${modelsDuration.toFixed(1)}ms: ${err.message || err}`);
            }
          });
        }

        if (isInit) {
          // Handle route AFTER settings are loaded to avoid
          // showThoughtsAndTools race condition (defaulting to 0 before settings arrive)
          this.debugService.log(`[Overseer] Settings loaded, now subscribing to route. showThoughtsAndTools=${this.showThoughtsAndTools}`);
          this.route.queryParams.subscribe(params => this.applyRouteSessionParam(params['sessionId'], true));
        }
      },
      error: (err) => {
        const settingsDuration = performance.now() - t0;
        this.perfLog('Settings', `getSettings FAILED in ${settingsDuration.toFixed(1)}ms: ${err.message || err}`);
        if (isInit) {
          this.route.queryParams.subscribe(params => this.applyRouteSessionParam(params['sessionId'], false));
        }
      }
    });
  }

  /**
   * Resolves the `sessionId` query parameter to a loaded chat. An ephemeral reference never
   * reaches the URL, so a route change while an ephemeral chat holds content would discard it
   * unrecoverably — that case asks first and carries the requested destination through the
   * confirmation instead.
   */
  private applyRouteSessionParam(idParam: any, verbose: boolean): void {
    if (idParam) {
      const id = Number(idParam);
      if (isNaN(id)) {
        this.navigateToNewSession();
        return;
      }
      if (this.currentSessionId === String(id)) return;
      if (this.guardEphemeralNavigation({ kind: 'session', id })) return;
      if (verbose && this.isStreaming) {
        this.debugService.log(`[Frontend] Navigating away from session ${this.currentSessionId} while streaming. Generation continues in background.`);
      }
      this.loadSession(id);
      return;
    }

    if (this.currentSessionId !== null || this.messages.length > 0) {
      if (this.guardEphemeralNavigation({ kind: 'new' })) return;
      if (verbose && this.isStreaming) {
        this.debugService.log('[Frontend] Navigating to new session, clearing local streaming state. Generation continues in background.');
      }
      this.newSession();
    } else {
      this.loadDraft();
    }
  }

  ngOnInit() {
    const tInit0 = performance.now();
    this.perfLog('Init', 'ChatComponent ngOnInit starting');
    // Feature-detected and code-split: a browser with native popover, interestfor and
    // anchor positioning downloads nothing. Needed by the context-window tooltip.
    ensureOverlayPolyfills();
    setTimeout(() => this.preloadAvatarImages(), 2500);
    this.settingsService.showThoughtsAndToolsUpdated.subscribe(val => {
      this.showThoughtsAndTools = val;
    });

    this.sessionSearchSub = this.sessionSearchSubject.pipe(
      debounceTime(300),
      distinctUntilChanged()
    ).subscribe(() => {
      this.loadSessions(false);
    });

    let previousUrl = '';
    this.router.events.pipe(
      filter(event => event instanceof NavigationEnd)
    ).subscribe((event: any) => {
      const currentUrl = event.urlAfterRedirects;
      if (currentUrl && currentUrl.startsWith('/chat')) {
        if (previousUrl && !previousUrl.startsWith('/chat')) {
          this.debugService.log(`[Overseer] Re-entered chat window from ${previousUrl}. Refetching settings and models.`);
          this.loadSettings(false);
          this.loadSessions(true);
          this.checkChangelogAnimation();
          
          // Re-join SignalR group in case connection was silently reset during navigation
          if (this.currentSessionId && this.hubConnection?.state === signalR.HubConnectionState.Connected) {
            this.hubConnection.invoke("JoinSession", this.currentSessionId).catch(err => {
              this.debugService.log(`[Frontend] Re-join after route re-entry failed: ${err}`);
            });
          }
        }
      }
      previousUrl = currentUrl || '';
    });

    this.navigationStartSub = this.router.events.pipe(
      filter(event => event instanceof NavigationStart)
    ).subscribe((event: any) => {
      const url: string = event.url || '';
      if (!url.startsWith('/chat')) {
        this.warnEphemeralLeave();
      }
    });

    window.addEventListener('online', this.onlineHandler);
    window.addEventListener('offline', this.offlineHandler);
    window.addEventListener('beforeunload', this.beforeUnloadHandler);

    if (!("popover" in HTMLElement.prototype)) {
      import("@oddbird/popover-polyfill").catch(err => console.warn('Failed to load popover polyfill', err));
    }
    
    this.loadSettings(true);
    this.perfLog('Init', 'ChatComponent ngOnInit initial dispatch finished', tInit0);

    const savedWidth = localStorage.getItem('overseer_sidebar_width');
    if (savedWidth) {
      const parsed = parseInt(savedWidth, 10);
      if (!isNaN(parsed) && parsed >= 150 && parsed <= 600) {
        this.sidebarWidth = parsed;
      }
    }

    (window as any).onGnollHackToolResponse = (jsonString: string) => {
      this.onGnollHackToolResponse(jsonString);
    };

    (window as any).__gnollhackReceiveFiles = (json: string) => {
      this.ngZone.run(() => this.receiveNativeFiles(json));
    };

    this.setupSignalR();

    this.ngZone.runOutsideAngular(() => {
      this.timeUpdateInterval = setInterval(() => {
        this.ngZone.run(() => {
          this.cdr.detectChanges();
        });
      }, 30000);
    });

    // Prevent document-level scrolling (fixes Android WebView keyboard push)
    document.documentElement.style.overflow = 'hidden';
    if (window.visualViewport) {
      window.visualViewport.addEventListener('resize', this.onVisualViewportResize);
      this.onVisualViewportResize(); // set initial value
    }

    // Listen for custom events to reset the changelog badge
    this.changelogBadgeResetHandler = () => this.checkChangelogAnimation();
    window.addEventListener('changelog_badge_reset', this.changelogBadgeResetHandler);
    
    this.checkChangelogAnimation();
  }

  private changelogBadgeResetHandler!: () => void;

  private checkChangelogAnimation() {
    const t0 = performance.now();
    this.changelogService.getReleaseNotes().subscribe({
      next: (response) => {
        const duration = performance.now() - t0;
        this.perfLog('Changelog', `getReleaseNotes received in ${duration.toFixed(1)}ms (${response?.notes?.length ?? 0} notes)`);
        if (response.notes && response.notes.length > 0) {
          const latestVersion = response.notes[0].version;
          this.showChangelogAnimation = this.changelogService.hasNewMajorOrMinorVersion(latestVersion);
          this.cdr.detectChanges();
        }
      },
      error: (err) => {
        const duration = performance.now() - t0;
        this.perfLog('Changelog', `getReleaseNotes failed after ${duration.toFixed(1)}ms: ${err?.message ?? err}`);
        console.error('Failed to check release notes for animation', err);
      }
    });
  }

  private flushPendingChunkBuffer() {
    if (this.pendingChunkTimeout) {
      clearTimeout(this.pendingChunkTimeout);
      this.pendingChunkTimeout = null;
    }
    if (this.pendingChunkBuffer.length > 0) {
      this.streamingMessage += this.pendingChunkBuffer;
      this.pendingChunkBuffer = '';
      
      this.hasRealContent = true;
      if (this.realContentTimeout) {
        clearTimeout(this.realContentTimeout);
        this.realContentTimeout = null;
      }
      
      this.updateHasRealContent();
      this.updateDesiredAvatarState();
      this.cdr.detectChanges();
      this.scrollToBottomClamped(false);
    }
  }

  static stripThoughts(text: string | null | undefined): string {
    if (!text) return '';
    const stripped = text.replace(/<div\s+class=["']ai-thought["']>[\s\S]*?(?:<\/div>|$)/gi, '');
    const parts = stripped.split(/(```[\s\S]*?```)/g);
    for (let i = 0; i < parts.length; i += 2) {
      parts[i] = parts[i].replace(/\r\n/g, '\n').replace(/\n{3,}/g, '\n\n');
    }
    return parts.join('').trim();
  }

  updateHasRealContent() {
    const stripped = ChatComponent.stripThoughts(this.streamingMessage);
    if (stripped.length > 0) {
      if (!this.hasRealContent && !this.realContentTimeout) {
        this.realContentTimeout = setTimeout(() => {
          this.hasRealContent = true;
          this.realContentTimeout = null;
          this.cdr.detectChanges();
          this.scrollToBottomClamped(false);
        }, 300);
      }
    } else {
      if (this.realContentTimeout) {
        clearTimeout(this.realContentTimeout);
        this.realContentTimeout = null;
      }
      this.hasRealContent = false;
      this.updateDesiredAvatarState();
    }
  }

  processChatEvent(evt: any) {
    /* The hub carries a session reference, which is a decimal id for a persisted chat and
       "eph_<guid>" for an ephemeral one. Both arrive as strings once compared. */
    const evtRef = (typeof evt.sessionId === 'number' || typeof evt.sessionId === 'string')
      ? String(evt.sessionId)
      : null;
    if (evtRef !== null && evtRef !== this.currentSessionId) {
      // Accept user_message_created if we are waiting for a new session ID
      if (evt.type === 'user_message_created' && this.currentSessionId == null && this.isStreaming) {
         // Proceed normally
      } else {
         return;
      }
    }

    if (evt.seqNo !== undefined && evt.seqNo !== null) {
      if (evt.seqNo <= this.lastSeenSeqNo) {
        this.debugService.log(`[Frontend] Skipping duplicate event seqNo=${evt.seqNo} (lastSeen=${this.lastSeenSeqNo})`);
        return;
      }
      this.lastSeenSeqNo = evt.seqNo;
    }

    if (evt.type !== 'debug' && evt.type !== 'title_update' && evt.type !== 'title_status' && evt.type !== 'status' && evt.type !== 'done') {
      this.updateDesiredAvatarState();
    }

    if (evt.type === 'debug') {
      this.debugService.log(`[Backend] ${evt.data}`);
    } else if (evt.type === 'user_message_created') {
      try {
        console.log('[Frontend Debug] Received user_message_created event:', evt.data);
        this.debugService.log(`[Frontend] Received user_message_created event: ${evt.data}`);
        
        const data = JSON.parse(evt.data);
        console.log(`[Frontend Debug] Parsed data. messageId: ${data.messageId}, attachments:`, data.attachments);
        this.debugService.log(`[Frontend] Parsed data. messageId: ${data.messageId}, attachments count: ${data.attachments ? data.attachments.length : 0}`);
        
        let messageFound = false;
        for (let i = this.messages.length - 1; i >= 0; i--) {
          if (this.messages[i].role === 'user') {
            messageFound = true;
            this.messages[i].id = data.messageId;
            console.log(`[Frontend Debug] Found latest user message at index ${i}. Updated id to ${data.messageId}`);
            
            if (data.attachments && this.messages[i].attachments) {
              for (const serverAtt of data.attachments) {
                const localAtt = this.messages[i].attachments!.find(
                  a => a.fileName === serverAtt.fileName && !a.id
                );
                if (localAtt) {
                  localAtt.id = serverAtt.id;
                  console.log(`[Frontend Debug] Successfully matched and updated attachment: ${serverAtt.fileName} with id ${serverAtt.id}`);
                  this.debugService.log(`[Frontend] Updated attachment: ${serverAtt.fileName} (id: ${serverAtt.id})`);
                } else {
                  console.warn(`[Frontend Debug] Failed to find local attachment matching fileName: ${serverAtt.fileName} without an id.`);
                  this.debugService.log(`[Frontend] Warning: Failed to find matching local attachment for: ${serverAtt.fileName}`);
                }
              }
            } else {
                console.log(`[Frontend Debug] No attachments to process. Data attachments: ${!!data.attachments}, Local attachments: ${!!this.messages[i].attachments}`);
            }
            break;
          }
        }
        
        if (!messageFound) {
            console.warn(`[Frontend Debug] Could not find any user message in the local array! Array length: ${this.messages.length}`);
            this.debugService.log(`[Frontend] Error: Could not find any user message in the local array.`);
        }

        this.cdr.detectChanges();
      } catch (e) {
        console.error('Failed to process user_message_created event', e);
      }
    } else if (evt.type === 'attachment_error') {
      // One rejected file, not a failed turn: the reply continues without it.
      this.debugService.log(`[Backend] attachment rejected: ${evt.data}`);
      this.showErrorToast(evt.data, 'Attachment not accepted');
    } else if (evt.type === 'attachment_excerpt') {
      /* One attachment per event, arriving before the reply streams. Rendered as a notice on
         the turn rather than a toast, because it qualifies an answer the user reads later. */
      try {
        const d = JSON.parse(evt.data);
        const notice: AttachmentExcerptNotice = {
          fileName: typeof d.fileName === 'string' ? d.fileName : '',
          usedChunks: Number(d.usedChunks) || 0,
          totalChunks: Number(d.totalChunks) || 0,
          coveragePercent: Number(d.coveragePercent) || 0,
          method: typeof d.method === 'string' ? d.method : '',
          removedActiveContent: Array.isArray(d.removedActiveContent)
            ? d.removedActiveContent.filter((x: any) => typeof x === 'string' && x.trim().length > 0)
            : [],
          wasTruncated: d.wasTruncated === true
        };
        this.debugService.log(`[Backend] attachment read as excerpts: ${evt.data}`);
        /* Keyed by file name, so a replayed generation buffer cannot list one file twice. */
        const existing = this.attachmentExcerpts.findIndex(x => x.fileName === notice.fileName);
        if (existing >= 0) {
          this.attachmentExcerpts[existing] = notice;
        } else {
          this.attachmentExcerpts = [...this.attachmentExcerpts, notice];
        }
        this.cdr.detectChanges();
      } catch (e) {
        this.debugService.log('[Frontend] Failed to parse attachment_excerpt event.');
      }
    } else if (evt.type === 'status') {
      this.currentStatusText = evt.data;
      this.debugService.log(`[Frontend] status updated to: ${evt.data}`);
      this.cdr.detectChanges();
    } else if (evt.type === 'thinking_chunk') {
      if (!this.isStreaming) {
        this.resetAvatarState();
        this.isStreaming = true;
        this.showSpinner = false;
      }
      this.updateDesiredAvatarState();
      // Always append thinking text; CSS .hide-thoughts handles visibility
      if (!this.isThinkingActive) {
        this.debugService.log(`[Frontend] thinking_chunk started. Current streamingMessage length: ${this.streamingMessage.length}`);
        this.streamingMessage += '\n\n<div class="ai-thought">\n\n';
      }
      this.streamingMessage += evt.data;
      this.cdr.detectChanges();
      this.scrollToBottomClamped(false);
      this.isThinkingActive = true;
    } else if (evt.type === 'ttft') {
      this.timeToFirstTokenMs = parseInt(evt.data, 10);
      this.cdr.detectChanges();
    } else if (evt.type === 'duration') {
      this.totalDurationMs = parseInt(evt.data, 10);
      this.cdr.detectChanges();
    } else if (evt.type === 'cost') {
      try {
        const d = JSON.parse(evt.data);
        this.liveCost = d.estimatedCost ?? null;
        this.liveIsOperatorCost = d.isOperatorCost;
        this.cdr.detectChanges();
      } catch { }
    } else if (evt.type === 'context') {
      try {
        const d = JSON.parse(evt.data);
        this.contextUsage = {
          promptTokens: d.promptTokens,
          outputTokens: d.outputTokens ?? 0,
          usedTokens: d.promptTokens + (d.outputTokens ?? 0),
          windowTokens: d.windowTokens,
          inputLimitTokens: d.inputLimitTokens ?? undefined,
          modelDisplayName: d.modelDisplayName ?? undefined
        };
        this.cdr.detectChanges();
        refreshAnchorPositioning();
      } catch { }
    } else if (evt.type === 'chunk') {
      this.debugService.log(`[Frontend] chunk received: seqNo=${evt.seqNo} "${evt.data}" streamingMessage.length=${this.streamingMessage.length}`);
      if (this.isThinkingActive) {
          this.debugService.log(`[Frontend] closing ai-thought div before chunk.`);
          this.isThinkingActive = false;
          this.streamingMessage += '\n\n</div>\n\n';
      }
      if (!this.isStreaming) {
        this.resetAvatarState();
        this.isStreaming = true;
        this.showSpinner = false;
        this.currentStatusText = 'Receiving data (background task)...';
      }
      this.hasOngoingGeneration = false;
      
      if ((this.showThoughtsAndTools === 0 || this.showThoughtsAndTools === 1) && !this.hasRealContent) {
        this.pendingChunkBuffer += evt.data;
        if (this.pendingChunkBuffer.length > 150 || this.pendingChunkBuffer.includes('\n')) {
          this.flushPendingChunkBuffer();
        } else if (!this.pendingChunkTimeout) {
          this.pendingChunkTimeout = setTimeout(() => {
            this.flushPendingChunkBuffer();
          }, 2000);
        }
      } else {
        this.streamingMessage += evt.data;
        this.updateHasRealContent();
        this.updateDesiredAvatarState();
        this.cdr.detectChanges();
        this.scrollToBottomClamped(false);
      }
    } else if (evt.type === 'error') {
      this.flushPendingChunkBuffer();
      this.currentStatusText = `Error: ${evt.data}`;
      this.debugService.log(`[Backend Error] ${evt.data}`);
      this.streamingMessage += `\n\n**Error:** ${evt.data}`;
      this.showSpinner = false;
      this.hasRealContent = true;
      if (this.realContentTimeout) {
         clearTimeout(this.realContentTimeout);
         this.realContentTimeout = null;
      }
      this.cdr.detectChanges();
    } else if (evt.type === 'tool_start') {
      try {
        // Close any active thinking div
        if (this.isThinkingActive) {
            this.isThinkingActive = false;
            this.streamingMessage += '\n\n</div>\n\n';
        }

        if (this.pendingChunkBuffer.length > 0) {
          if (this.pendingChunkTimeout) {
            clearTimeout(this.pendingChunkTimeout);
            this.pendingChunkTimeout = null;
          }
          this.streamingMessage += '\n\n<div class="ai-thought">\n\n' 
            + this.pendingChunkBuffer.trim() + '\n\n</div>\n\n';
          this.pendingChunkBuffer = '';
        }

        // Wrap any preceding text (reasoning before tool call) in ai-thought div
        if (this.streamingMessage.length > 0) {
          const lastDivIndex = this.streamingMessage.lastIndexOf('</div>');
          const thoughtStartIndex = lastDivIndex >= 0 ? lastDivIndex + 6 : 0;
          const thoughtText = this.streamingMessage.substring(thoughtStartIndex).trim();
          if (thoughtText.length > 0) {
            this.streamingMessage = this.streamingMessage.substring(0, thoughtStartIndex)
              + '\n\n<div class="ai-thought">\n\n' + thoughtText + '\n\n</div>\n\n';
          }
        }
        this.updateHasRealContent();

        const toolInfo = JSON.parse(evt.data);
        this.debugService.log(`[Frontend] tool_start: ${toolInfo.name}, streamingMessage.length after=${this.streamingMessage.length}`);

        const args = JSON.parse(toolInfo.arguments || '{}');
        const displayName = toolInfo.display_name || ChatComponent.getToolDisplayName(toolInfo.name, args);
        const argsText = this.buildToolArgsText(toolInfo.name, args);
        
        this.streamingToolCalls.push({ 
          id: toolInfo.id, 
          name: toolInfo.name, 
          status: 'running',
          displayName,
          argsText,
          agentName: toolInfo.agent_name || (toolInfo.name === 'delegate_to_subagent' ? args?.agent_name : undefined),
          parentToolCallId: toolInfo.parent_tool_call_id,
          depth: toolInfo.depth || 0
        });
        this.updateDesiredAvatarState();
        this.cdr.detectChanges();
        this.scrollToBottomClamped(false);
      } catch(e) {}
    } else if (evt.type === 'tool_result') {
      try {
        const toolInfo = JSON.parse(evt.data);
        const tc = this.streamingToolCalls.find(t => t.id === toolInfo.id && t.status === 'running');
        if (tc) {
          tc.status = toolInfo.status || 'completed';
          tc.result = toolInfo.result;
        }
        this.updateDesiredAvatarState();
        this.cdr.detectChanges();
        this.scrollToBottomClamped(false);
      } catch(e) {}
    } else if (evt.type === 'tool_error') {
      try {
        const toolInfo = JSON.parse(evt.data);
        const tc = this.streamingToolCalls.find(t => t.id === toolInfo.id && t.status === 'running');
        if (tc) {
          tc.status = toolInfo.status || 'error';
          tc.error = toolInfo.error;
        }
        this.updateDesiredAvatarState();
        this.cdr.detectChanges();
        this.scrollToBottomClamped(false);
      } catch(e) {}
    } else if (evt.type === 'tool_client_request') {
      try {
        const request: ToolClientRequest = JSON.parse(evt.data);
        this.forwardToolRequest(request);
      } catch (e) {
        console.error('Failed to parse tool_client_request:', e);
      }
    } else if (evt.type === 'title_update') {
      try {
        const data = JSON.parse(evt.data);
        const s = this.sessions.find(x => String(x.id) === String(data.sessionId));
        if (s) {
          s.title = data.title;
          this.cdr.detectChanges();
        }
      } catch(e) {}
    } else if (evt.type === 'title_status') {
      try {
        const data = JSON.parse(evt.data);
        if (String(data.sessionId) !== this.currentSessionId) return;
    
        if (data.status === 'canceled' || data.status === '') {
          this.isGeneratingTitle = false;
          this.titleStatusText = data.status === 'canceled' ? 'Title generation canceled.' : 'Title generation complete.';
          setTimeout(() => {
            if (this.titleStatusText === 'Title generation canceled.' || this.titleStatusText === 'Title generation complete.') {
              this.titleStatusText = '';
              this.cdr.detectChanges();
            }
          }, 2000);
        } else {
          this.isGeneratingTitle = true;
          this.titleStatusText = data.status;
        }
        this.cdr.detectChanges();
      } catch(e) {}
    } else if (evt.type === 'done') {
      this.hasOngoingGeneration = false;
      this.debugService.log(`[Frontend] done received. hasRealContent=${this.hasRealContent}, streamingMessage.length=${this.streamingMessage.length}`);
      if (this.realContentTimeout) {
         clearTimeout(this.realContentTimeout);
         this.realContentTimeout = null;
      }
      if (this.isStreaming) {
        this.flushPendingChunkBuffer();
        if (this.isThinkingActive) {
            this.isThinkingActive = false;
            this.streamingMessage += '\n\n</div>\n\n';
        }

        // We explicitly do NOT call updateDesiredAvatarState() here because isStreaming
        // is still true, which would cause the system to schedule a transition back to 'thinking'.
        if (this.avatarSwapTimeout) {
          clearTimeout(this.avatarSwapTimeout);
          this.avatarSwapTimeout = null;
        }
        
        const elapsed = performance.now() - this.animStartTime;
        const loopDuration = ChatComponent.AVATAR_LOOP_DURATIONS[this.currentAvatarState] || 1000;
        const timeInLoop = elapsed % loopDuration;
        const timeUntilLoopEnd = loopDuration - timeInLoop;

        this.isFinishingAnimation = true;

        const executeDone = () => {
          if (!this.isFinishingAnimation) return;
          
          this.isFinishingAnimation = false;
          this.finishDoneTimeout = null;
          
          // Force the state to idle instantly before the DOM swap
          this.applyAvatarState('idle');
          if (this.currentSessionId) {
            this.sessionStateMap.delete(this.currentSessionId);
          }
          
          this.messages.push({ 
            role: 'assistant', 
            content: this.streamingMessage, 
            timestampUtc: new Date().toISOString(),
            toolCalls: [...this.streamingToolCalls], 
            modelDisplayName: this.selectedModel?.displayName || this.selectedModel?.modelId || this.singleModelInfo?.modelId,
            thinkingLevel: this.selectedModel?.thinkingLevel || this.singleModelInfo?.thinkingLevel,
            reasoningMode: this.selectedModel?.reasoningMode || this.singleModelInfo?.reasoningMode,
            serviceTier: this.selectedModel?.serviceTier || this.singleModelInfo?.serviceTier,
            timeToFirstTokenMs: this.timeToFirstTokenMs ?? undefined,
            totalDurationMs: this.totalDurationMs ?? undefined,
            contextPromptTokens: this.contextUsage?.promptTokens,
            contextOutputTokens: this.contextUsage?.outputTokens,
            contextWindowTokens: this.contextUsage?.windowTokens,
            contextInputLimitTokens: this.contextUsage?.inputLimitTokens,
            estimatedCost: this.liveCost,
            isOperatorCost: this.liveIsOperatorCost
          });

          // Fold only here, on normal completion. Cancel and error paths deliberately do not
          // fold: the server's stored total is authoritative on the next load, and a second
          // fold would double-count.
          if (this.liveCost != null) {
            this.sessionTotalCost = (this.sessionTotalCost ?? 0) + this.liveCost;
          }

          this.isStreaming = false;
          this.hasRealContent = false;
          this.streamingMessage = '';
          this.streamingToolCalls = [];
          this.timeToFirstTokenMs = null;
    this.liveCost = null;
    this.liveIsOperatorCost = false;
          this.totalDurationMs = null;
          this.showSpinner = false;
          this.currentStatusText = 'Generation complete.';
          
          // Fallback for missing attachment IDs
          if (this.currentSessionId) {
            const lastUserMsg = [...this.messages].reverse().find(m => m.role === 'user');
            if (lastUserMsg && lastUserMsg.attachments && lastUserMsg.attachments.some(a => !a.id)) {
              console.log('[Frontend Debug] Fallback: Missing attachment IDs detected. Fetching session to patch...');
              this.debugService.log(`[Frontend] Fallback: Missing attachment IDs detected. Fetching session to patch...`);
              this.chatService.getSession(this.currentSessionId).subscribe({
                next: (res) => {
                  const s = res.body;
                  if (!s) return;
                  const serverUserMsg = [...(s.messages || [])].reverse().find(m => m.role === 'user');
                  if (serverUserMsg && serverUserMsg.attachments) {
                    for (const serverAtt of serverUserMsg.attachments) {
                      const localAtt = lastUserMsg.attachments!.find(a => a.fileName === serverAtt.fileName && !a.id);
                      if (localAtt) {
                        localAtt.id = serverAtt.id;
                        console.log(`[Frontend Debug] Fallback patched attachment: ${serverAtt.fileName} (id: ${serverAtt.id})`);
                      }
                    }
                    this.cdr.detectChanges();
                  }
                },
                error: (err) => {
                  this.debugService.log(`[Frontend] Fallback getSession failed: ${err.message || err}`);
                }
              });
            }
          }

          this.cdr.detectChanges();
          this.loadSessions(true);
          this.focusPromptInput();
        };

        this.finishDoneTimeout = setTimeout(executeDone, timeUntilLoopEnd + 20);
      }
    }
  }

  /**
   * Derives {@link contextUsage} from the newest assistant message that actually carries the
   * measurement. Scanning backwards past messages without it means a chat whose most recent
   * reply predates this feature still shows the last measured value rather than nothing.
   */
  private recomputeContextUsage(): void {
    this.contextUsage = null;
    for (let i = this.messages.length - 1; i >= 0; i--) {
      const m = this.messages[i];
      if (m.role !== 'assistant') continue;
      if (m.contextPromptTokens == null || m.contextWindowTokens == null) continue;
      const out = m.contextOutputTokens ?? 0;
      this.contextUsage = {
        promptTokens: m.contextPromptTokens,
        outputTokens: out,
        usedTokens: m.contextPromptTokens + out,
        windowTokens: m.contextWindowTokens,
        inputLimitTokens: m.contextInputLimitTokens ?? undefined,
        modelDisplayName: m.modelDisplayName
      };
      refreshAnchorPositioning();
      return;
    }
  }

  formatTokenCount(n: number | null | undefined): string {
    if (n == null) return '';
    if (n < 1000) return String(n);
    if (n < 1000000) return this.trimZeros(n / 1000, 1) + 'k';
    return this.trimZeros(n / 1000000, 2) + 'M';
  }

  private trimZeros(v: number, digits: number): string {
    return v.toFixed(digits).replace(/\.?0+$/, '');
  }

  get contextUsagePercent(): number {
    if (!this.contextUsage || this.contextUsage.windowTokens <= 0) return 0;
    const pct = (this.contextUsage.usedTokens / this.contextUsage.windowTokens) * 100;
    return Math.min(100, Math.max(0, Math.round(pct)));
  }

  /** Structured tooltip content. One string per rendered line. */
  get contextUsageTooltipLines(): string[] {
    const u = this.contextUsage;
    if (!u) return [];
    const lines = [
      `${u.promptTokens.toLocaleString()} prompt + ${u.outputTokens.toLocaleString()} output tokens`,
      `of a ${u.windowTokens.toLocaleString()}-token context window`
    ];
    if (u.inputLimitTokens) {
      lines.push(`History is truncated above ${u.inputLimitTokens.toLocaleString()} input tokens.`);
    }
    if (u.modelDisplayName) {
      lines.push(`Measured on ${u.modelDisplayName} at the last reply.`);
    }
    return lines;
  }

  /**
   * Short factual value for the progressbar. The tooltip carries the explanation via
   * interestfor's implicit aria-describedby, so repeating it here would announce it twice.
   */
  get contextUsageValueText(): string {
    const u = this.contextUsage;
    if (!u) return '';
    return `${u.usedTokens.toLocaleString()} of ${u.windowTokens.toLocaleString()} tokens, ${this.contextUsagePercent}%`;
  }

  formatTtft(ms: number | null | undefined): string {
    if (ms == null) return '';
    const seconds = ms / 1000;
    return seconds < 1 ? seconds.toFixed(1) + 's' : Math.round(seconds) + 's';
  }

  formatDuration(ttftMs: number | null | undefined, totalMs: number | null | undefined): string {
    const ttftStr = this.formatTtft(ttftMs);
    const totalStr = this.formatTtft(totalMs);
    if (ttftStr && totalStr) {
      return `${ttftStr}→${totalStr}`;
    }
    return ttftStr || totalStr || '';
  }

  getMinimalStatusLabel(): string {
    if (this.streamingToolCalls && this.streamingToolCalls.length > 0) {
      const lastTool = this.streamingToolCalls[this.streamingToolCalls.length - 1];
      return (lastTool.displayName || lastTool.name) + '...';
    }
    return 'Thinking...';
  }

  setupSignalR() {
    this.perfLog('SignalR', 'SignalR connect started (/chathub)');
    const tSignalR0 = performance.now();

    this.hubConnection = new signalR.HubConnectionBuilder()
      .withUrl('/chathub')
      .withAutomaticReconnect()
      .build();

    this.hubConnection.on('ReceiveChatEvent', (evt: any) => {
      this.ngZone.run(() => {
        if (this.isLoadingSession) {
          this.debugService.log(`[Frontend] Buffered live event seqNo=${evt.seqNo} type=${evt.type}`);
          this.liveEventBuffer.push(evt);
        } else {
          this.processChatEvent(evt);
        }
      });
    });

    this.hubConnection.onreconnecting((error) => {
      this.debugService.log(`[Frontend] SignalR reconnecting... Error: ${error?.message ?? 'none'}`);
    });

    this.hubConnection.onreconnected(async (connectionId) => {
      this.debugService.log(`[Frontend] SignalR reconnected with connectionId=${connectionId}. Re-joining session ${this.currentSessionId}.`);
      if (this.currentSessionId) {
        try {
          await this.hubConnection!.invoke("JoinSession", this.currentSessionId);
          this.debugService.log(`[Frontend] Re-joined session ${this.currentSessionId} after reconnect.`);
          if (this.isStreaming) {
            this.debugService.log(`[Frontend] Reconnected while streaming session ${this.currentSessionId}. Silently re-syncing session...`);
            this.syncSessionSilently(this.currentSessionId);
          }
        } catch (err) {
          this.debugService.log(`[Frontend] Failed to re-join session ${this.currentSessionId} after reconnect: ${err}`);
          console.error('JoinSession after reconnect failed:', err);
        }
      }
    });

    this.hubConnection.onclose((error) => {
      this.debugService.log(`[Frontend] SignalR connection closed. Error: ${error?.message ?? 'none'}`);
    });

    this.hubStartPromise = this.hubConnection.start();
    this.hubStartPromise.then(() => {
      const connectDuration = performance.now() - tSignalR0;
      this.perfLog('SignalR', `SignalR connected successfully in ${connectDuration.toFixed(1)}ms`);
      if (this.currentSessionId) {
        this.hubConnection?.invoke("JoinSession", this.currentSessionId).catch(console.error);
      }
    }).catch(err => {
      const connectDuration = performance.now() - tSignalR0;
      this.perfLog('SignalR', `SignalR connection error after ${connectDuration.toFixed(1)}ms: ${err?.message ?? err}`);
      console.error('SignalR connection error: ', err);
    });
  }

  /* An incognito composer keeps its text in memory only, so there is nothing on disk to
     restore and nothing to overwrite the live text with. */
  loadDraft() {
    if (this.suppressDraftPersistence) return;
    const key = this.currentSessionId ? `chat_draft_${this.currentSessionId}` : 'chat_draft_new';
    this.currentInput = localStorage.getItem(key) || '';
  }

  saveDraft() {
    if (this.suppressDraftPersistence) return;
    const key = this.currentSessionId ? `chat_draft_${this.currentSessionId}` : 'chat_draft_new';
    localStorage.setItem(key, this.currentInput);
  }

  clearDraft() {
    if (this.suppressDraftPersistence) return;
    const key = this.currentSessionId ? `chat_draft_${this.currentSessionId}` : 'chat_draft_new';
    localStorage.removeItem(key);
  }
  onSessionSearchInput(e: Event) {
    const value = (e.target as HTMLInputElement).value;
    this.sessionSearchQuery = value;
    this.sessionSearchSubject.next(value);
  }

  clearSessionSearch() {
    if (!this.sessionSearchQuery) return;
    this.sessionSearchQuery = '';
    this.sessionSearchSubject.next('');
  }

  loadSessions(preserveLoaded: boolean = false) {
    const t0 = performance.now();
    this.loadingSessions = true;
    const take = preserveLoaded && this.sessions.length > 0 ? this.sessions.length : undefined;
    this.perfLog('Sessions', `loadSessions started (take=${take ?? 'default'}, preserveLoaded=${preserveLoaded}, search=${this.sessionSearchQuery})`);
    this.chatService.getSessions(0, take, this.sessionSearchQuery).subscribe({
      next: (httpResponse) => {
        const netDuration = performance.now() - t0;
        const response = httpResponse.body;
        const serverTiming = httpResponse.headers.get('Server-Timing');
        const timingLog = serverTiming ? ` | Server-Timing: ${serverTiming}` : '';
        
        const tRender0 = performance.now();
        this.sessions = response?.sessions || [];
        this.hasMoreSessions = response?.hasMore || false;
        this.activeSessionCount = response?.totalCount ?? response?.activeCount ?? this.sessions.length;
        this.pinnedSessionCount = response?.pinnedCount ?? this.sessions.filter(s => s.isPinned).length;
        this.maxSessionQuota = response?.maxQuota || 50;
        this.maxPinnedQuota = response?.maxPinned || 5;
        this.loadingSessions = false;
        this.trashModal?.loadTrash();
        this.cdr.detectChanges();
        const renderDuration = performance.now() - tRender0;

        this.perfLog('Sessions', `loadSessions completed: ${this.sessions.length} sessions (hasMore=${this.hasMoreSessions}) in ${netDuration.toFixed(1)}ms (render: ${renderDuration.toFixed(1)}ms)${timingLog}`);
        setTimeout(() => {
          const entries = performance.getEntriesByType('resource') as PerformanceResourceTiming[];
          const sessionEntry = entries.filter(e => e.name.includes('/api/chat/sessions')).pop();
          if (sessionEntry) {
            this.perfLog('Sessions', `ResourceTiming: startTime=${sessionEntry.startTime.toFixed(0)}, fetchStart=${sessionEntry.fetchStart.toFixed(0)}, requestStart=${sessionEntry.requestStart.toFixed(0)}, responseStart=${sessionEntry.responseStart.toFixed(0)}, responseEnd=${sessionEntry.responseEnd.toFixed(0)}, duration=${sessionEntry.duration.toFixed(0)}, stalled=${(sessionEntry.requestStart - sessionEntry.startTime).toFixed(0)}ms, ttfb=${(sessionEntry.responseStart - sessionEntry.requestStart).toFixed(0)}ms`);
          }
        }, 100);
      },
      error: (err) => {
        const netDuration = performance.now() - t0;
        this.perfLog('Sessions', `loadSessions FAILED in ${netDuration.toFixed(1)}ms: ${err.message || err}`);
        console.error('Failed to load sessions', err);
        this.loadingSessions = false;
        setTimeout(() => {
          const entries = performance.getEntriesByType('resource') as PerformanceResourceTiming[];
          const sessionEntry = entries.filter(e => e.name.includes('/api/chat/sessions')).pop();
          if (sessionEntry) {
            this.perfLog('Sessions', `ResourceTiming (on error): startTime=${sessionEntry.startTime.toFixed(0)}, fetchStart=${sessionEntry.fetchStart.toFixed(0)}, requestStart=${sessionEntry.requestStart.toFixed(0)}, responseStart=${sessionEntry.responseStart.toFixed(0)}, duration=${sessionEntry.duration.toFixed(0)}, stalled=${(sessionEntry.requestStart - sessionEntry.startTime).toFixed(0)}ms, ttfb=${(sessionEntry.responseStart - sessionEntry.requestStart).toFixed(0)}ms`);
          }
        }, 100);
      }
    });
  }

  loadMoreSessions() {
    if (this.loadingMoreSessions || this.loadingSessions || !this.hasMoreSessions) return;
    const t0 = performance.now();
    this.loadingMoreSessions = true;
    const skip = this.sessions.length;
    this.perfLog('Sessions', `loadMoreSessions started (skip=${skip}, search=${this.sessionSearchQuery})`);
    this.chatService.getSessions(skip, undefined, this.sessionSearchQuery).subscribe({
      next: (httpResponse) => {
        const netDuration = performance.now() - t0;
        const response = httpResponse.body;
        const serverTiming = httpResponse.headers.get('Server-Timing');
        const timingLog = serverTiming ? ` | Server-Timing: ${serverTiming}` : '';
        
        const newSessions = response?.sessions || [];
        this.sessions = [...this.sessions, ...newSessions];
        this.hasMoreSessions = response?.hasMore || false;
        this.loadingMoreSessions = false;
        this.cdr.detectChanges();

        this.perfLog('Sessions', `loadMoreSessions completed: +${newSessions.length} sessions (total: ${this.sessions.length}, hasMore=${this.hasMoreSessions}) in ${netDuration.toFixed(1)}ms${timingLog}`);
        setTimeout(() => {
          const entries = performance.getEntriesByType('resource') as PerformanceResourceTiming[];
          const sessionEntry = entries.filter(e => e.name.includes('/api/chat/sessions')).pop();
          if (sessionEntry) {
            this.perfLog('Sessions', `ResourceTiming (loadMore): startTime=${sessionEntry.startTime.toFixed(0)}, fetchStart=${sessionEntry.fetchStart.toFixed(0)}, requestStart=${sessionEntry.requestStart.toFixed(0)}, responseStart=${sessionEntry.responseStart.toFixed(0)}, responseEnd=${sessionEntry.responseEnd.toFixed(0)}, duration=${sessionEntry.duration.toFixed(0)}, stalled=${(sessionEntry.requestStart - sessionEntry.startTime).toFixed(0)}ms, ttfb=${(sessionEntry.responseStart - sessionEntry.requestStart).toFixed(0)}ms`);
          }
        }, 100);
      },
      error: (err) => {
        const netDuration = performance.now() - t0;
        this.perfLog('Sessions', `loadMoreSessions FAILED in ${netDuration.toFixed(1)}ms: ${err.message || err}`);
        console.error('Failed to load more sessions', err);
        this.loadingMoreSessions = false;
      }
    });
  }

  navigateToNewSession() {
    if (window.innerWidth <= 768) {
      this.closeSidebar();
    }
    if (this.guardEphemeralNavigation({ kind: 'new' })) return;
    this.performNavigateToNewSession();
  }

  /* The plain navigation, past the ephemeral confirmation. An ephemeral chat is not in the
     URL, so this is also the only way to leave one once its content is gone. */
  private performNavigateToNewSession() {
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
    if (this.currentSessionId === String(id)) return;
    if (this.guardEphemeralNavigation({ kind: 'session', id })) return;
    this.router.navigate([], {
      relativeTo: this.route,
      queryParams: { sessionId: id },
      queryParamsHandling: 'merge'
    });
  }

  newSession() {
    this.sessionLoadSub?.unsubscribe();
    this.currentSessionId = null;
    this.hasGameSnapshot = false;
    this.privateBadge = null;
    this.isConfidentialSession = false;
    this.isEphemeralSession = false;
    this.isEphemeralDetailOpen = false;
    setSentryConfidentialSession(false);
    this.sessionTotalCost = null;
    this.clientBridge.notifySessionChanged(null);
    this.lastSeenSeqNo = -1;
    this.messages = [];
    this.clearStreamingState();
    this.loadDraft();
    this.applySavedModelPreference();
    this.focusPromptInput();
  }

  private clearStreamingState() {
    this.isStreaming = false;
    this.isFinishingAnimation = false;
    if (this.finishDoneTimeout) {
      clearTimeout(this.finishDoneTimeout);
      this.finishDoneTimeout = null;
    }
    this.pendingChunkBuffer = '';
    if (this.pendingChunkTimeout) {
      clearTimeout(this.pendingChunkTimeout);
      this.pendingChunkTimeout = null;
    }
    this.streamingMessage = '';
    if (this.realContentTimeout) {
      clearTimeout(this.realContentTimeout);
      this.realContentTimeout = null;
    }
    this.hasRealContent = false;
    this.streamingToolCalls = [];
    this.attachmentExcerpts = [];
    this.showSpinner = false;
    this.currentStatusText = '';
    this.isGeneratingTitle = false;
    this.isThinkingActive = false;
    this.titleStatusText = '';
    this.timeToFirstTokenMs = null;
    this.liveCost = null;
    this.liveIsOperatorCost = false;
    this.totalDurationMs = null;
    this.contextUsage = null;
    this.hasOngoingGeneration = false;
    this.isLoadingSession = false;
    this.liveEventBuffer = [];
    if (this.handoffTimeoutHandle) { clearTimeout(this.handoffTimeoutHandle); this.handoffTimeoutHandle = null; }
    this.resetAvatarState();
    this.loadDraft();
    this.applySavedModelPreference();
    this.focusPromptInput();
  }

  private formatMessageToolCalls(messages: ChatMessage[]) {
    messages.forEach(msg => {
      if (msg.toolCalls) {
        msg.toolCalls.forEach(tc => {
          let argsObj: any = null;
          if (tc.argsText) {
            const trimmed = tc.argsText.trim();
            if (trimmed.startsWith('{') || trimmed.startsWith('[')) {
              try {
                argsObj = JSON.parse(trimmed);
                tc.argsText = this.buildToolArgsText(tc.name, argsObj);
              } catch (e) {
                // Ignore JSON parsing errors and leave argsText as is
              }
            }
          }
          if (tc.name) {
            tc.displayName = tc.displayName || ChatComponent.getToolDisplayName(tc.name, argsObj);
          }
        });
      }
    });
  }

  async loadSession(sessionRef: string | number) {
    /* Callers hold either a sidebar row's numeric id or a session reference already in string
       form; everything below compares and transmits the string. */
    const id = String(sessionRef);
    if (this.currentSessionId === id && this.messages.length > 0 && !this.isLoadingSession) {
      this.debugService.log(`[Frontend] loadSession(${id}) skipped because session is already active.`);
      return;
    }

    this.sessionLoadSub?.unsubscribe();

    this.messages = [];
    this.hasGameSnapshot = false;
    this.privateBadge = null;
    this.isConfidentialSession = false;
    this.isEphemeralSession = false;
    this.isEphemeralDetailOpen = false;
    setSentryConfidentialSession(false);
    this.sessionTotalCost = null;
    this.autoScrollEnabled = true;
    this.lastSeenSeqNo = -1;
    this.clearStreamingState();
    this.resetAvatarState();

    if (this.hubConnection?.state === signalR.HubConnectionState.Connected) {
      if (this.currentSessionId && this.currentSessionId !== id) {
        this.hubConnection.invoke("LeaveSession", this.currentSessionId).catch(console.error);
      }
    }
    
    this.currentSessionId = id;
    this.clientBridge.notifySessionChanged(id);
    this.isLoadingSession = true;
    this.liveEventBuffer = [];

    if (this.sessionStateMap.has(id)) {
      const state = this.sessionStateMap.get(id)!;
      this.requestStartTime = state.requestStartTime;
      this.thinkingAnimStartTime = state.thinkingAnimStartTime;
      this.hasEnteredWorkingPhase = state.hasEnteredWorkingPhase;
    }

    const joinSessionAsync = async () => {
      this.debugService.log(`[Frontend] loadSession(${id}): hub state BEFORE await = ${this.hubConnection?.state ?? 'null'}`);

      if (this.hubStartPromise) {
        try {
          await this.hubStartPromise;
        } catch {
          // Connection failed — proceed without SignalR
        }
      }

      this.debugService.log(`[Frontend] loadSession(${id}): hub state AFTER await = ${this.hubConnection?.state ?? 'null'}`);

      if (this.currentSessionId === id && this.hubConnection?.state === signalR.HubConnectionState.Connected) {
        try {
          await this.hubConnection.invoke("JoinSession", id);
          this.debugService.log(`[Frontend] loadSession(${id}): JoinSession succeeded`);
        } catch (err) {
          this.debugService.log(`[Frontend] loadSession(${id}): JoinSession FAILED: ${err}`);
          console.error('JoinSession failed:', err);
        }
      } else if (this.currentSessionId === id) {
        this.debugService.log(`[Frontend] loadSession(${id}): JoinSession SKIPPED — hub not connected`);
      }
    };

    await Promise.race([
      joinSessionAsync(),
      new Promise(r => setTimeout(r, 1500))
    ]);

    if (this.currentSessionId !== id) return;

    const tSession0 = performance.now();
    this.perfLog('SessionDetail', `loadSession(${id}) HTTP request dispatched`);
    this.sessionLoadSub = this.chatService.getSession(id).subscribe({
      next: (httpResponse) => {
        const netDuration = performance.now() - tSession0;
        const s = httpResponse.body;
        const serverTiming = httpResponse.headers.get('Server-Timing');
        const timingLog = serverTiming ? ` | Server-Timing: ${serverTiming}` : '';
        if (!s) return;

        if (s.isGnollHackSession) {
          this.chatService.hasGreeted = true;
        }

        this.messages = s.messages || [];
        this.hasGameSnapshot = !!s.hasGameSnapshot;
        this.privateBadge = s.privateBadge ?? null;
        this.isConfidentialSession = !!s.isConfidential;
        this.isEphemeralSession = s.isEphemeral === true || ChatComponent.isEphemeralRef(id);
        // Suppresses client telemetry while a confidential chat is on screen.
        setSentryConfidentialSession(this.isConfidentialSession);
        this.sessionTotalCost = s.totalEstimatedCost ?? null;
        this.hasOngoingGeneration = s.hasOngoingGeneration === true;
        this.formatMessageToolCalls(this.messages);
        this.recomputeContextUsage();

        // Debug: summarize loaded messages
        const asstMsgs = this.messages.filter(m => m.role === 'assistant');
        const msgsWithThinking = asstMsgs.filter(m => m.content && m.content.includes('ai-thought'));
        const msgsWithTools = asstMsgs.filter(m => m.toolCalls && m.toolCalls.length > 0);
        const totalTools = asstMsgs.reduce((sum, m) => sum + (m.toolCalls?.length || 0), 0);
        this.debugService.log(`[Frontend] Session ${id} loaded: ${this.messages.length} messages, ${asstMsgs.length} assistant, ${msgsWithThinking.length} with thinking text, ${msgsWithTools.length} with tool calls (${totalTools} total tools). showThoughtsAndTools=${this.showThoughtsAndTools}`);
        this.perfLog('SessionDetail', `loadSession(${id}) received ${this.messages.length} msgs in ${netDuration.toFixed(1)}ms${timingLog}`);
        this.currentStatusText = '';
        this.isGeneratingTitle = false;
        this.titleStatusText = '';
        this.loadDraft();

        if (s.ongoingGeneration && s.ongoingGeneration.events) {
          const seqNos = s.ongoingGeneration.events
            .filter((e: any) => e.seqNo != null)
            .map((e: any) => e.seqNo);
          const minSeq = seqNos.length > 0 ? Math.min(...seqNos) : 'none';
          const maxSeq = seqNos.length > 0 ? Math.max(...seqNos) : 'none';
          this.debugService.log(`[Frontend] Session ${id} has ongoing generation with ${s.ongoingGeneration.events.length} buffered events (seqNo range: ${minSeq}–${maxSeq}). Replaying...`);
          this.isStreaming = true;
          for (const evt of s.ongoingGeneration.events) {
            if (evt.seqNo !== undefined && evt.seqNo !== null && evt.seqNo <= this.lastSeenSeqNo) {
              this.debugService.log(`[Frontend] Skipping duplicated replayed event seqNo=${evt.seqNo}`);
              continue;
            }
            this.processChatEvent(evt);
          }
          this.debugService.log(`[Frontend] Replay complete. isStreaming=${this.isStreaming}, streamingMessage length=${this.streamingMessage.length}`);
        }

        if (s.lastEventSeqNo != null && typeof s.lastEventSeqNo === 'number') {
          this.lastSeenSeqNo = Math.max(this.lastSeenSeqNo, s.lastEventSeqNo);
        }
        
        this.isLoadingSession = false;
        if (this.liveEventBuffer.length > 0) {
          const seqNos = this.liveEventBuffer
              .filter((e: any) => e.seqNo != null)
              .map((e: any) => e.seqNo);
          const minSeq = seqNos.length > 0 ? Math.min(...seqNos) : 'none';
          const maxSeq = seqNos.length > 0 ? Math.max(...seqNos) : 'none';
          this.debugService.log(`[Frontend] Flushing ${this.liveEventBuffer.length} buffered live events (seqNo range: ${minSeq}–${maxSeq}, lastSeenSeqNo=${this.lastSeenSeqNo}).`);
          for (const evt of this.liveEventBuffer) {
            if (evt.seqNo !== undefined && evt.seqNo !== null && evt.seqNo <= this.lastSeenSeqNo) {
              continue;
            }
            this.processChatEvent(evt);
          }
          this.liveEventBuffer = [];
        } else {
          this.debugService.log(`[Frontend] No buffered live events to flush. lastSeenSeqNo=${this.lastSeenSeqNo}`);
        }
        this.liveEventBuffer = [];

        // Safety timeout: if the "Consulting" overlay is still showing after 60s with no events, dismiss it
        if (this.handoffTimeoutHandle) { clearTimeout(this.handoffTimeoutHandle); this.handoffTimeoutHandle = null; }
        if (this.isHandoffWaiting) {
          this.handoffTimeoutHandle = setTimeout(() => {
            if (this.isHandoffWaiting) {
              this.debugService.log('[Frontend] Handoff timeout reached (60s). Dismissing consulting overlay.');
              this.hasOngoingGeneration = false;
              this.isStreaming = false;
              this.showSpinner = false;
              this.currentStatusText = '';
              this.cdr.detectChanges();
            }
          }, 60000);
        }

        if (!this.sessions.find(x => String(x.id) === id)) {
           this.loadSessions(true);
        }
        this.applySavedModelPreference();
        this.focusPromptInput();
        this.forceWebViewRepaint();
        this.autoScrollEnabled = true;
        setTimeout(() => this.scrollToBottomClamped(false), 0);
      },
      error: (err) => {
        const netDuration = performance.now() - tSession0;
        this.perfLog('SessionDetail', `loadSession(${id}) FAILED in ${netDuration.toFixed(1)}ms: ${err.message || err}`);
        this.isLoadingSession = false;
        this.liveEventBuffer = [];
        console.warn(`Failed to load session ${id}. Bouncing to new chat.`, err);
        this.navigateToNewSession();
        this.forceWebViewRepaint();
      }
    });
  }

  async syncSessionSilently(sessionRef: string | number) {
    const id = String(sessionRef);
    if (this.currentSessionId !== id) return;
    this.debugService.log(`[Frontend] Silently syncing session ${id}...`);

    this.chatService.getSession(id).subscribe({
      next: (httpResponse) => {
        const s = httpResponse.body;
        if (!s || this.currentSessionId !== id) return;

        if (s.isGnollHackSession) {
          this.chatService.hasGreeted = true;
        }

        if (s.ongoingGeneration && s.ongoingGeneration.events) {
          const missedEvents = s.ongoingGeneration.events.filter(
            (e: any) => e.seqNo != null && e.seqNo > this.lastSeenSeqNo
          );
          this.debugService.log(`[Frontend] Silent sync found ${missedEvents.length} missed events.`);
          for (const evt of missedEvents) {
            this.processChatEvent(evt);
          }
        }

        if (s.lastEventSeqNo != null && typeof s.lastEventSeqNo === 'number') {
          this.lastSeenSeqNo = Math.max(this.lastSeenSeqNo, s.lastEventSeqNo);
        }

        if (!s.hasOngoingGeneration && this.isStreaming) {
          this.debugService.log(`[Frontend] Silent sync detected generation finished/died.`);
          const hasAssistantResponse = s.messages && s.messages.length > 0 && s.messages[s.messages.length - 1].role === 'assistant';

          if (hasAssistantResponse) {
            this.messages = s.messages || [];
            this.formatMessageToolCalls(this.messages);
            this.clearStreamingState();
            this.recomputeContextUsage();
            this.resetAvatarState();
            this.autoScrollEnabled = true;
            this.cdr.detectChanges();
            setTimeout(() => this.scrollToBottomClamped(false), 0);
          } else {
            this.processChatEvent({ type: 'error', data: 'Connection lost and generation failed on server.' });
            this.processChatEvent({ type: 'done' });
          }
        }
      },
      error: (err) => {
        this.debugService.log(`[Frontend] Silent sync failed: ${err.message || err}`);
      }
    });
  }

  private forceWebViewRepaint() {
    requestAnimationFrame(() => {
      this.cdr.detectChanges();
      const el = this.messagesContainer?.nativeElement;
      if (el) {
        el.style.transform = 'translateZ(0)';
        requestAnimationFrame(() => { el.style.transform = ''; });
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
    
    this.chatService.deleteSession(id).subscribe({
      next: () => {
        if (this.currentSessionId === String(id)) this.navigateToNewSession();
        this.loadSessions(true);
        if (this.deleteConfirmDialog) {
          this.deleteConfirmDialog.nativeElement.close();
        }
        this.sessionToDelete = null;
      },
      error: () => {
        if (this.deleteConfirmDialog) {
          this.deleteConfirmDialog.nativeElement.close();
        }
        this.sessionToDelete = null;
      }
    });
  }

  triggerPinnedQuotaAlert() {
    this.isPinnedQuotaAlerting = true;
    this.cdr.detectChanges();
    setTimeout(() => {
      this.isPinnedQuotaAlerting = false;
      this.cdr.detectChanges();
    }, 500);
  }

  togglePinSession(id: number, event: Event) {
    event.stopPropagation();
    (event.currentTarget as HTMLElement)?.blur();
    this.chatService.togglePinSession(id).subscribe({
      next: (res) => {
        const session = this.sessions.find(s => s.id === id);
        if (session) {
          session.isPinned = res.isPinned;
          if (res.isPinned) {
            this.pinnedSessionCount++;
          } else {
            this.pinnedSessionCount = Math.max(0, this.pinnedSessionCount - 1);
          }
          this.sessions.sort((a, b) => {
            if (!!a.isPinned !== !!b.isPinned) return a.isPinned ? -1 : 1;
            return new Date(b.lastMessageUtc).getTime() - new Date(a.lastMessageUtc).getTime();
          });
          this.cdr.detectChanges();
        }
      },
      error: (err) => {
        console.error('Failed to toggle pin', err);
        const msg = err.error?.message || 'You can pin a maximum of 5 sessions. Please unpin an existing session to pin a new one.';
        this.triggerPinnedQuotaAlert();
        this.showErrorToast(msg, 'Pin Limit Reached');
      }
    });
  }

  openTrashDialog() {
    this.trashModal?.open();
  }

  get bulkDeleteTargetCount(): number {
    const total = this.activeSessionCount ?? this.sessions.length;
    if (this.includePinnedInBulkDelete) {
      return total;
    }
    return Math.max(0, total - this.pinnedSessionCount);
  }

  openBulkDeleteDialog() {
    this.includePinnedInBulkDelete = false;
    this.isBulkDeleting = false;
    if (this.bulkDeleteConfirmDialog) {
      this.bulkDeleteConfirmDialog.nativeElement.showModal();
    }
  }

  closeBulkDeleteDialog() {
    if (this.bulkDeleteConfirmDialog) {
      this.bulkDeleteConfirmDialog.nativeElement.close();
    }
    this.isBulkDeleting = false;
  }

  confirmBulkDelete() {
    this.isBulkDeleting = true;
    const includePinned = this.includePinnedInBulkDelete;
    this.chatService.bulkDeleteSessions(includePinned).subscribe({
      next: () => {
        this.isBulkDeleting = false;
        this.closeBulkDeleteDialog();

        if (this.currentSessionId) {
          const currentSession = this.sessions.find(s => String(s.id) === this.currentSessionId);
          if (currentSession && (!currentSession.isPinned || includePinned)) {
            this.navigateToNewSession();
          }
        }
        this.loadSessions(true);
        this.trashModal?.loadTrash();
      },
      error: (err) => {
        this.isBulkDeleting = false;
        this.closeBulkDeleteDialog();
        this.showErrorToast(err.error?.message || 'Failed to move chats to trash', 'Bulk Delete Failed');
      }
    });
  }

  openUnpinAllDialog() {
    this.isUnpinningAll = false;
    if (this.unpinAllConfirmDialog) {
      this.unpinAllConfirmDialog.nativeElement.showModal();
    }
  }

  closeUnpinAllDialog() {
    if (this.unpinAllConfirmDialog) {
      this.unpinAllConfirmDialog.nativeElement.close();
    }
    this.isUnpinningAll = false;
  }

  confirmUnpinAll() {
    this.isUnpinningAll = true;
    this.chatService.unpinAllSessions().subscribe({
      next: () => {
        this.isUnpinningAll = false;
        this.closeUnpinAllDialog();
        this.loadSessions(true);
      },
      error: (err) => {
        this.isUnpinningAll = false;
        this.closeUnpinAllDialog();
        this.showErrorToast(err.error?.message || 'Failed to unpin all chats', 'Unpin Failed');
      }
    });
  }

  requestReportMessage(messageId: number, index: number) {
    this.reportingMessageId = messageId;
    this.reportedMsgIndex = index;
    this.reportError = '';
    this.isReporting = false;
    if (this.reportConfirmDialog) {
      this.reportConfirmDialog.nativeElement.showModal();
    }
  }

  closeReportConfirm() {
    if (this.reportConfirmDialog) {
      this.reportConfirmDialog.nativeElement.close();
    }
    this.reportingMessageId = null;
    this.reportedMsgIndex = null;
    this.reportError = '';
    this.isReporting = false;
  }

  confirmReport() {
    if (this.reportingMessageId === null) return;
    this.isReporting = true;
    this.reportError = '';
    
    this.chatService.reportMessage(this.reportingMessageId).subscribe({
      next: (res: any) => {
        if (res?.debugLogs) {
           res.debugLogs.forEach((l: string) => this.debugService.log(l));
        }
        this.reportSuccessIndex = this.reportedMsgIndex;
        setTimeout(() => {
           this.reportSuccessIndex = null;
           this.cdr.detectChanges();
        }, 3000);
        this.closeReportConfirm();
      },
      error: (err) => {
        this.isReporting = false;
        if (err.error?.debugLogs) {
           err.error.debugLogs.forEach((l: string) => this.debugService.log(l));
        }
        this.debugService.log(`Report error: ${JSON.stringify(err)}`);
        
        if (err.error?.message && typeof err.error.message === 'string') {
          this.reportError = err.error.message;
        } else if (err.error && typeof err.error === 'string') {
          this.reportError = err.error;
        } else {
          this.reportError = 'Failed to report message. Please try again later.';
        }
        this.cdr.detectChanges();
      }
    });
  }

  /** True for a session reference that names an ephemeral, RAM-only chat. */
  static isEphemeralRef(ref: string | null | undefined): boolean {
    return typeof ref === 'string' && ref.startsWith('eph_');
  }

  /** True while no chat is open, which is the only point at which the privacy mode is chosen. */
  get isNewChat(): boolean {
    return this.currentSessionId === null;
  }

  /** True while an ephemeral chat holds something that closing, expiry or a reload would destroy. */
  get hasEphemeralContent(): boolean {
    return this.isEphemeralSession
      && (this.messages.length > 0 || this.isStreaming || this.currentInput.trim().length > 0);
  }

  /** The mode named on the privacy panel's trigger, so the choice is visible while collapsed. */
  get privacyModeLabel(): string {
    if (this.newChatEphemeral) return 'Incognito';
    if (this.newChatConfidential) return 'Confidential';
    return 'Standard';
  }

  /* True while the composer's text must stay out of localStorage: for an open ephemeral chat,
     and for a new chat already marked incognito, whose first message would otherwise be
     written to disk as a draft before it is ever sent. */
  private get suppressDraftPersistence(): boolean {
    return this.isEphemeralSession || (this.isNewChat && this.newChatEphemeral);
  }

  /**
   * The privacy flags for an outgoing turn. A chat that already exists reports what it is —
   * the mode cannot be changed after creation, and sending `isEphemeral` without
   * `isConfidential` is rejected by the server.
   */
  private get outgoingPrivacyFlags(): { isConfidential: boolean; isEphemeral: boolean } {
    if (this.isNewChat) {
      return {
        isConfidential: this.newChatConfidential || this.newChatEphemeral,
        isEphemeral: this.newChatEphemeral
      };
    }
    return {
      isConfidential: this.isConfidentialSession || this.isEphemeralSession,
      isEphemeral: this.isEphemeralSession
    };
  }

  togglePrivacyPanel() {
    this.isPrivacyPanelOpen = !this.isPrivacyPanelOpen;
  }

  toggleEphemeralDetail() {
    this.isEphemeralDetailOpen = !this.isEphemeralDetailOpen;
  }

  onNewChatConfidentialChange(enabled: boolean) {
    this.newChatConfidential = enabled;
    // Incognito is a stricter form of Confidentiality Mode, so it cannot outlive it.
    if (!enabled) this.newChatEphemeral = false;
  }

  onNewChatEphemeralChange(enabled: boolean) {
    this.newChatEphemeral = enabled;
    if (enabled) {
      this.newChatConfidential = true;
      /* Anything already typed has been saved as a draft under the new-chat key. It stays in
         the textarea, but it stops being on disk from here on. */
      try {
        localStorage.removeItem('chat_draft_new');
      } catch { /* storage unavailable — nothing was written either */ }
    }
  }

  /**
   * Asks before a navigation abandons an ephemeral chat. Returns true when the caller must
   * stop: the requested destination is carried by the confirmation and applied once the chat
   * has actually been closed.
   */
  private guardEphemeralNavigation(target: { kind: 'new' } | { kind: 'session'; id: number }): boolean {
    if (!this.hasEphemeralContent) return false;
    this.pendingEphemeralNavigation = target;
    this.requestCloseEphemeralSession();
    return true;
  }

  requestCloseEphemeralSession() {
    if (!this.isEphemeralSession) return;
    this.ephemeralCloseError = null;
    this.isClosingEphemeral = false;
    this.ephemeralCloseDialog?.nativeElement?.showModal();
  }

  /** Dismisses the confirmation, and with it any navigation that raised it. */
  closeEphemeralCloseDialog() {
    this.ephemeralCloseDialog?.nativeElement?.close();
    this.pendingEphemeralNavigation = null;
    this.isClosingEphemeral = false;
    this.ephemeralCloseError = null;
    this.cdr.detectChanges();
  }

  confirmCloseEphemeralSession() {
    const ref = this.currentSessionId;
    if (!ref || !this.isEphemeralSession) {
      this.closeEphemeralCloseDialog();
      return;
    }
    this.isClosingEphemeral = true;
    this.ephemeralCloseError = null;
    this.cdr.detectChanges();

    this.chatService.closeEphemeralSession(ref).subscribe({
      next: () => this.finishEphemeralClose(ref),
      error: (err) => {
        /* 404 means the server no longer holds the chat — it expired or was already closed.
           The content is gone either way, so the client must stop displaying it. */
        if (err?.status === 404) {
          this.finishEphemeralClose(ref);
          return;
        }
        this.isClosingEphemeral = false;
        this.ephemeralCloseError = err?.error?.message
          || 'Could not close the incognito chat. Its content stays in server memory until it expires.';
        this.cdr.detectChanges();
      }
    });
  }

  private finishEphemeralClose(ref: string) {
    this.isClosingEphemeral = false;
    if (this.hubConnection?.state === signalR.HubConnectionState.Connected) {
      this.hubConnection.invoke('LeaveSession', ref).catch(console.error);
    }
    this.sessionStateMap.delete(ref);
    this.ephemeralCloseDialog?.nativeElement?.close();

    const target = this.pendingEphemeralNavigation;
    this.pendingEphemeralNavigation = null;

    // Both toggles return to off: a privacy mode is chosen per chat, never inherited.
    this.newChatEphemeral = false;
    this.newChatConfidential = false;
    this.currentInput = '';
    this.newSession();
    this.setEphemeralNotice('Incognito chat closed. Its content was destroyed and cannot be recovered.');
    this.cdr.detectChanges();

    if (target && target.kind === 'session') {
      this.navigateToSession(target.id);
    } else {
      this.performNavigateToNewSession();
    }
  }

  /**
   * Warns that an open ephemeral chat is being left behind, without stopping the navigation.
   * Wired to Angular's own guard as well, so a route-level `canDeactivate` behaves the same.
   */
  canDeactivate(): boolean {
    this.warnEphemeralLeave();
    return true;
  }

  private warnEphemeralLeave() {
    if (!this.hasEphemeralContent) return;
    this.setEphemeralNotice('The incognito chat stays in memory while you are away. It is destroyed when you close it, when it expires, or when this tab closes.');
  }

  private setEphemeralNotice(text: string) {
    this.ephemeralNotice = text;
    if (this.ephemeralNoticeTimeout) {
      clearTimeout(this.ephemeralNoticeTimeout);
    }
    this.ephemeralNoticeTimeout = setTimeout(() => {
      this.ephemeralNoticeTimeout = null;
      this.ephemeralNotice = '';
      this.cdr.detectChanges();
    }, 12000);
  }

  dismissEphemeralNotice() {
    if (this.ephemeralNoticeTimeout) {
      clearTimeout(this.ephemeralNoticeTimeout);
      this.ephemeralNoticeTimeout = null;
    }
    this.ephemeralNotice = '';
  }

  stopRequest() {
    if (this.isStreaming && this.currentSessionId && this.hubConnection) {
      this.hubConnection.invoke('CancelGeneration', this.currentSessionId).catch(console.error);
    }
  }

  cancelTitleGeneration() {
    if (this.hubConnection && this.currentSessionId) {
      this.hubConnection.invoke('CancelTitleGeneration', this.currentSessionId).catch(console.error);
    }
  }

  onEnter(event: Event) {
    const keyboardEvent = event as KeyboardEvent;
    if (!keyboardEvent.shiftKey) {
      keyboardEvent.preventDefault();
      this.sendMessage();
    }
  }

  private sanitizeInput(text: string): string {
    if (!text) return '';
    return text.replace(/[\x00-\x08\x0B\x0C\x0E-\x1F\x7F]/g, '');
  }

  isLastAssistantMessage(index: number): boolean {
    if (this.isStreaming) return false;
    for (let i = this.messages.length - 1; i >= 0; i--) {
      if (this.messages[i].role === 'assistant') {
        return i === index;
      }
    }
    return false;
  }

  async sendMessage() {
    const message = this.sanitizeInput(this.currentInput).trim();
    if (!message && this.pendingAttachments.length === 0) return;
    
    if (this.isFinishingAnimation) {
      if (this.finishDoneTimeout) {
        clearTimeout(this.finishDoneTimeout);
        this.finishDoneTimeout = null;
      }
      this.isFinishingAnimation = false;
      this.isStreaming = false;
      this.pendingChunkBuffer = '';
      if (this.pendingChunkTimeout) {
        clearTimeout(this.pendingChunkTimeout);
        this.pendingChunkTimeout = null;
      }
      this.streamingMessage = '';
      this.streamingToolCalls = [];
    } else if (this.isStreaming) {
      return;
    }

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
    this.isThinkingActive = false;
    this.pendingChunkBuffer = '';
    if (this.pendingChunkTimeout) {
      clearTimeout(this.pendingChunkTimeout);
      this.pendingChunkTimeout = null;
    }
    this.streamingMessage = '';
    if (this.realContentTimeout) {
      clearTimeout(this.realContentTimeout);
      this.realContentTimeout = null;
    }
    this.hasRealContent = false;
    this.streamingToolCalls = [];
    this.attachmentExcerpts = [];
    this.timeToFirstTokenMs = null;
    this.liveCost = null;
    this.liveIsOperatorCost = false;
    this.totalDurationMs = null;
    this.lastSeenSeqNo = -1; // [NEW] Reset sequence tracker for new generation

    this.focusPromptInput();

    this.requestStartTime = performance.now();
    this.resetAvatarState();
    this.hasEnteredWorkingPhase = false;
    this.thinkingAnimStartTime = performance.now();
    this.updateDesiredAvatarState();
    if (this.currentSessionId) {
      this.sessionStateMap.set(this.currentSessionId, {
        requestStartTime: this.requestStartTime,
        thinkingAnimStartTime: this.thinkingAnimStartTime,
        hasEnteredWorkingPhase: this.hasEnteredWorkingPhase
      });
    }
    this.currentStatusText = 'Connecting...';
    this.showSpinner = true;

    // Refetch showThoughtsAndTools since the chat component is reused across navigations
    try {
      const settings = await firstValueFrom(this.settingsService.getSettings());
      if (settings) {
        this.showThoughtsAndTools = Number(settings.showThoughtsAndTools ?? 0);
        this.spoilerFreeMode = settings.spoilerFreeMode === true;
      }
    } catch { /* use cached value */ }

    this.debugService.log(`[Overseer] Starting UI Request to backend for chat message.`);
    this.debugService.log(`[Overseer] showThoughtsAndTools=${this.showThoughtsAndTools}`);

    try {
      const selectedModelObj = this.selectedModel;
      let uId: number | undefined = undefined;
      let sId: number | undefined = undefined;
      if (selectedModelObj) {
        if (selectedModelObj.isSystem) {
          sId = selectedModelObj.id;
        } else {
          uId = selectedModelObj.id;
        }
      }

      // Ensure SignalR group membership before sending — guards against silent reconnects
      if (this.currentSessionId && this.hubConnection?.state === signalR.HubConnectionState.Connected) {
        try {
          await this.hubConnection.invoke("JoinSession", this.currentSessionId);
          this.debugService.log(`[Frontend] sendMessage: Pre-send JoinSession(${this.currentSessionId}) succeeded.`);
        } catch (err) {
          this.debugService.log(`[Frontend] sendMessage: Pre-send JoinSession(${this.currentSessionId}) failed: ${err}`);
        }
      }

      const currentHasGreeted = this.chatService.hasGreeted;
      const privacy = this.outgoingPrivacyFlags;
      const res = await firstValueFrom(this.chatService.sendMessage(
        this.currentSessionId, message, attachmentsPayload, uId, sId, currentHasGreeted,
        privacy.isConfidential, privacy.isEphemeral));
      this.chatService.hasGreeted = true;
      const newSessionId = String(res.sessionId);

      if (this.currentSessionId !== newSessionId) {
        this.currentSessionId = newSessionId;
        this.isEphemeralSession = ChatComponent.isEphemeralRef(newSessionId);
        if (this.isEphemeralSession) {
          this.isConfidentialSession = true;
          setSentryConfidentialSession(true);
          this.isPrivacyPanelOpen = false;
          this.ephemeralNotice = 'Incognito chat started. Nothing in it is being saved.';
        }
        this.clientBridge.notifySessionChanged(newSessionId);

        if (this.hubConnection?.state === signalR.HubConnectionState.Connected) {
          this.hubConnection.invoke("JoinSession", this.currentSessionId).catch(console.error);
        }
        this.loadSessions(true);

        /* An ephemeral reference is deliberately kept out of the URL: it would outlive the
           chat in browser history and survive as a dead link, and the chat itself cannot be
           reopened from it. */
        if (!this.isEphemeralSession) {
          const urlTree = this.router.createUrlTree([], {
            relativeTo: this.route,
            queryParams: { sessionId: this.currentSessionId },
            queryParamsHandling: 'merge'
          });
          this.router.navigateByUrl(urlTree, { replaceUrl: true });
        }
      }
    } catch (e: any) {
      console.error(e);
      let errorDisplay = e.message || 'Unknown error';
      if (e.name === 'HttpErrorResponse') {
         errorDisplay = `Network error: ${e.message} (Status: ${e.status} ${e.statusText})`;
         if (e.error) {
             try {
                 errorDisplay += ` - Details: ${typeof e.error === 'string' ? e.error : JSON.stringify(e.error)}`;
             } catch (stringifyErr) {
                 errorDisplay += ` - Details: [Unserializable Error Object]`;
             }
         }
      } else if (typeof e === 'string') {
          errorDisplay = e;
      } else if (e && typeof e === 'object' && !e.message) {
          try {
              errorDisplay = JSON.stringify(e);
          } catch(err) {
              errorDisplay = 'Unknown error object';
          }
      }

      this.currentStatusText = `Error: ${e.message || 'Unknown error'}`;
      this.messages.push({ role: 'assistant', content: '**Error:**\n\n```text\n' + errorDisplay + '\n```', timestampUtc: new Date().toISOString() });
      
      try {
          this.debugService.log(`Frontend Error: ${JSON.stringify(e, Object.getOwnPropertyNames(e))}`);
      } catch (err) {
          this.debugService.log(`Frontend Error: ${e.toString()}`);
      }
      
      this.isStreaming = false;
      this.showSpinner = false;
    }
  }

  confirmLogout(event: Event) {
    event.preventDefault();
    if (this.logoutDialog && this.logoutDialog.nativeElement) {
      this.logoutDialog.nativeElement.showModal();
    }
  }

  closeLogoutConfirm() {
    if (this.logoutDialog && this.logoutDialog.nativeElement) {
      this.logoutDialog.nativeElement.close();
    }
  }

  executeLogout() {
    this.closeLogoutConfirm();
    this.authService.logout().subscribe({
      next: () => this.router.navigate(['/login']),
      error: () => this.router.navigate(['/login'])
    });
  }

  triggerFileInput() {
    /* On iOS inside GnollHack, bypass the built-in WKWebView file picker
     * (which shows "Take Photo" and crashes without NSCameraUsageDescription)
     * and use the native bridge to present our own picker instead. */
    if (this.clientBridge.getPlatform() === 'ios') {
        /* Pass the + button's bounding rect so MAUI can anchor the
         * iOS popover arrow to the correct position.
         * getBoundingClientRect() returns CSS pixels relative to the
         * viewport, which map 1:1 to WKWebView points. */
        const btn = document.querySelector('.add-media-icon') as HTMLElement;
        let sourceRect = { x: 0, y: 0, width: 0, height: 0 };
        if (btn) {
            const r = btn.getBoundingClientRect();
            sourceRect = { x: r.left, y: r.top, width: r.width, height: r.height };
        }
        this.clientBridge.postMessage({ type: 'pick_files', sourceRect });
        return;
    }
    const el = document.getElementById('fileInput') as HTMLInputElement;
    if (el) el.click();
  }

  /* Called from native iOS code after the user picks files via PHPicker
   * or UIDocumentPicker. Each entry has { name, type, dataUrl }. */
  receiveNativeFiles(filesJson: string) {
      try {
          const files: Array<{ name: string, type: string, dataUrl: string }> =
              JSON.parse(filesJson);
          for (const f of files) {
              if (this.pendingAttachments.length >= 5) break;
              /* Validate the extension against the server's allowlist, the same one the
                 picker's accept attribute is built from. */
              const ext = f.name.split('.').pop()?.toLowerCase();
              if (!this.acceptedExtensions.has(ext || '')) continue;
              this.pendingAttachments.push({
                  file: null,
                  base64: f.dataUrl,  /* already a data:... URL */
                  name: f.name,
                  type: f.type || 'application/octet-stream'
              });
          }
          this.cdr.detectChanges();
      } catch (e) {
          console.error('receiveNativeFiles error:', e);
      }
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
    /* Read once per call: the picker's accept attribute filters the dialog, and this filters
       everything else that reaches the composer, drag-and-drop and paste included. */
    const accepted = this.acceptedExtensions;
    for (let i = 0; i < files.length; i++) {
      if (this.pendingAttachments.length >= 5) break;
      const file = files[i];
      const ext = file.name.split('.').pop()?.toLowerCase();
      if (!accepted.has(ext || '')) continue;

      if (file.size > this.maxAttachmentSize) {
        const sizeMb = (this.maxAttachmentSize / 1024 / 1024).toFixed(1);
        this.showErrorToast(`The file "${file.name}" exceeds the maximum allowed size of ${sizeMb} MB.`, 'File Too Large');
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

  showErrorToast(msg: string, title: string = 'Error') {
    this.errorTitle = title;
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

  openImagePreview(att: any, event: Event, isPending: boolean = false) {
    event.preventDefault();
    this.previewAttachment = {
      isPending: isPending,
      fileName: isPending ? att.name : att.fileName,
      id: att.id,
      base64: att.base64,
      downloadUrl: isPending ? att.base64 : '/api/chat/attachments/' + att.id
    };
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
      const cleanText = ChatComponent.stripThoughts(text);
      await navigator.clipboard.writeText(cleanText);
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

  async copyToolResult(text: string | undefined | null, tcId: string, event: Event) {
    event.stopPropagation();
    if (!text) return;
    try {
      await navigator.clipboard.writeText(text);
      this.copiedToolCallId = tcId;
      setTimeout(() => {
        if (this.copiedToolCallId === tcId) this.copiedToolCallId = null;
        this.cdr.detectChanges();
      }, 2000);
    } catch (err) {
      console.error('Failed to copy tool result: ', err);
    }
  }

  cancelSubAgent(toolCallId: string | undefined, event?: Event) {
    if (event) event.stopPropagation();
    const sessionRef = this.currentSessionId;
    if (!toolCallId || !sessionRef) return;

    this.cancelingSubAgentId = toolCallId;
    this.chatService.cancelSubAgent(sessionRef, toolCallId).subscribe({
      next: (res) => {
        this.cancelingSubAgentId = null;
        this.debugService.log(`[Frontend] Cancelled subagent ${toolCallId}: ${res.message}`);
        const tc = this.streamingToolCalls.find(t => t.id === toolCallId);
        if (tc && tc.status === 'running') {
          tc.status = 'canceled';
        }
        this.cdr.detectChanges();
      },
      error: (err) => {
        this.cancelingSubAgentId = null;
        this.debugService.log(`[Frontend] Error cancelling subagent ${toolCallId}: ${err?.message || err}`);
      }
    });
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

  forwardToolRequest(request: ToolClientRequest): void {
    if (!this.clientBridge.isEmbedded()) {
        console.error('No client bridge available');
        this.sendToolResult(request.requestId, false, null, 'Client bridge not available');
        return;
    }

    this.clientBridge.postMessage(request);

    const timer = setTimeout(() => {
        this.pendingRequests.delete(request.requestId);
        this.sendToolResult(request.requestId, false, null, 'Client tool timed out');
    }, this.CLIENT_TOOL_TIMEOUT_MS);

    this.pendingRequests.set(request.requestId, timer);
  }

  async sendToolResult(requestId: string, success: boolean, content: string | null, errorMessage: string | null): Promise<void> {
    try {
        if (!this.hubConnection || this.hubConnection.state !== signalR.HubConnectionState.Connected) {
            console.error('SignalR not connected, cannot send tool result');
            return;
        }
        
        await this.hubConnection.invoke('SubmitToolResult',
            requestId,
            this.currentSessionId ?? '',
            success, 
            success ? content : (errorMessage || 'Tool execution failed')
        );
    } catch (e) {
        console.error('Failed to send tool result:', e);
    }
  }

  onGnollHackToolResponse(data: ToolResponse | string): void {
    try {
      const response: ToolResponse = typeof data === 'string' ? JSON.parse(data) : data;

      if (response.type !== 'tool_response') {
        return;
      }

      const localReq = this.localToolRequests.get(response.requestId);
      if (localReq) {
        clearTimeout(localReq.timer);
        this.localToolRequests.delete(response.requestId);
        if (response.success) {
          localReq.resolve(response.content || '');
        } else {
          localReq.reject(new Error(response.errorMessage || response.content || 'Local tool execution failed'));
        }
        return;
      }

      const timer = this.pendingRequests.get(response.requestId);
      if (timer) {
        clearTimeout(timer);
        this.pendingRequests.delete(response.requestId);
      }

      this.sendToolResult(response.requestId, response.success, response.content, response.errorMessage);
    } catch (e) {
      console.error('Failed to parse tool response from GnollHack:', e);
    }
  }

}
