import { Component, OnInit, OnDestroy, inject, ChangeDetectorRef, ViewChild, ElementRef, HostListener, NgZone, AfterViewInit, ChangeDetectionStrategy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ChatService, ChatSession, ChatMessage, ChatMessageToolCall } from '../services/chat.service';
import { AuthService } from '../services/auth.service';
import { DebugService } from '../services/debug.service';
import { Router, ActivatedRoute, RouterModule, NavigationEnd } from '@angular/router';
import { MarkdownPipe } from './markdown.pipe';
import { RelativeTimePipe } from './relative-time.pipe';
import { SettingsService } from '../services/settings.service';
import { ChangelogService } from '../services/changelog.service';
import * as signalR from '@microsoft/signalr';
import { firstValueFrom, filter, Subscription } from 'rxjs';
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

@Component({
    selector: 'app-chat',
    imports: [CommonModule, FormsModule, RouterModule, MarkdownPipe, RelativeTimePipe],
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
    'search_server_dumplogs': 'Searching server dumplogs',
    'source_code_search': 'Searching source code',
    'source_code_view': 'Viewing source code',
    'wiki_search': 'Searching GnollHack Wiki',
    'list_indexed_files': 'Listing source files',
    'get_constants': 'Searching constants',
    'get_knowledge_article': 'Searching knowledge base',
    'wiki_view': 'Viewing Wiki page',
    'search_definitions': 'Searching definitions',
    'get_function_definition': 'Reading function definition',
    'get_item_stats': 'Reading item stats',
    'get_monster_stats': 'Reading monster stats',
    'get_artifact_stats': 'Reading artifact stats',
    'get_github_repo_info': 'Retrieving GitHub repository information',
    'search_github': 'Searching GitHub'
  };

  private buildToolArgsText(name: string, args: any): string {
    if (!args) return '';
    try {
      if (name === 'source_code_search') {
        if (args.query && args.file_filter) return `"${args.query}" in ${args.file_filter}`;
        if (args.query) return `"${args.query}"`;
      }
      if (name === 'source_code_view') {
        if (args.file && args.start_line) return `${args.file}:L${args.start_line}`;
        if (args.file) return args.file;
      }
      if (name === 'list_indexed_files' && args.path_filter) {
        return args.path_filter;
      }
      if (name === 'monster_lookup' && args.name) return args.name;
      if (name === 'item_lookup' && args.name) return args.name;
      if (name === 'wiki_search' && args.query) return args.query;
      if (name === 'nethack_wiki_search' && args.query) return args.query;
      if (name === 'get_knowledge_article') {
        if (args.topic_title) return args.topic_title;
        if (args.topic) return args.topic;
      }
      
      const firstKey = Object.keys(args)[0];
      if (firstKey) return String(args[firstKey]);
    } catch (e) {}
    return '';
  }
  chatService = inject(ChatService);
  settingsService = inject(SettingsService);
  changelogService = inject(ChangelogService);
  
  showChangelogAnimation = false;
  
  @ViewChild('messagesContainer') messagesContainer!: ElementRef;
  @ViewChild('deleteConfirmDialog') deleteConfirmDialog!: ElementRef<HTMLDialogElement>;
  @ViewChild('reportConfirmDialog') reportConfirmDialog!: ElementRef<HTMLDialogElement>;
  @ViewChild('imagePreviewDialog') imagePreviewDialog!: ElementRef<HTMLDialogElement>;
  @ViewChild('errorToast') errorToast!: ElementRef<HTMLElement>;
  @ViewChild('promptInput') promptInput!: ElementRef<HTMLTextAreaElement>;
  @ViewChild('renameInput') renameInput!: ElementRef<HTMLInputElement>;
  @ViewChild('logoutDialog') logoutDialog!: ElementRef<HTMLDialogElement>;
  autoScrollEnabled = true;

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
  ngZone = inject(NgZone);
  authService = inject(AuthService);
  debugService = inject(DebugService);
  router = inject(Router);
  route = inject(ActivatedRoute);
  cdr = inject(ChangeDetectorRef);
  
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
  currentSessionId: number | null = null;
  sessionToDelete: number | null = null;
  messages: ChatMessage[] = [];
  
  copiedMsgIndex: number | null = null;
  copiedStreamMsg = false;
  copiedToolCallId: string | null = null;
  
  previewAttachment: any = null;
  
  currentInput = '';
  isStreaming = false;
  isThinkingActive = false;
  hasRealContent = false;
  realContentTimeout: any = null;
  streamingMessage = '';
  streamingToolCalls: ChatMessageToolCall[] = [];
  pendingAttachments: { file: File | null, base64: string, name: string, type: string }[] = [];
  timeToFirstTokenMs: number | null = null;
  
  isGeneratingTitle = false;
  titleStatusText = '';
  lastSeenSeqNo: number = -1;
  private hasOngoingGeneration = false;
  isLoadingSession = false;
  private liveEventBuffer: any[] = [];
  private handoffTimeoutHandle: any = null;

  maxAttachmentSize = 15728640; // default 15MB
  errorMessage = '';
  hasApiKey = true;
  hasModel = true;
  isTitleGenerationInProgress = false;
  showThoughtsAndTools = 0;
  spoilerFreeMode = false;

  userModels: import('../services/settings.service').UserAiModel[] = [];
  systemModels: import('../services/settings.service').UserAiModel[] = [];
  selectedUserModelId: number | null = null;
  isModelDropdownOpen = false;
  singleModelInfo: any = null;
  
  isRenamingTitle = false;
  renameTitleValue = '';
  renameError: string | null = null;

  get currentTitle(): string {
    const session = this.sessions.find(s => s.id === this.currentSessionId);
    return session ? session.title : 'New Chat';
  }

  get selectedModel() {
    return this.userModels.find(m => m.id === this.selectedUserModelId) || 
           this.systemModels.find(m => m.id === this.selectedUserModelId);
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
    
    if (!this.currentSessionId) return;
    
    const session = this.sessions.find(s => s.id === this.currentSessionId);
    if (session && session.title !== newTitle) {
      session.title = newTitle;
      this.chatService.renameSession(this.currentSessionId, newTitle).subscribe({
        error: (err) => console.error('Failed to rename session', err)
      });
    }
  }
  
  cancelRename() {
    this.isRenamingTitle = false;
    this.renameError = null;
  }

  selectModel(modelId: number | undefined) {
    if (modelId !== undefined) {
      this.selectedUserModelId = modelId;
      localStorage.setItem('overseer_chat_model_global', modelId.toString());
      if (this.currentSessionId !== null) {
        localStorage.setItem(`overseer_chat_model_session_${this.currentSessionId}`, modelId.toString());
      }
    }
    this.isModelDropdownOpen = false;
  }

  applySavedModelPreference() {
    if (this.userModels.length === 0 && this.systemModels.length === 0) return;

    let targetId: number | null = null;

    if (this.currentSessionId !== null) {
      const sessionPref = localStorage.getItem(`overseer_chat_model_session_${this.currentSessionId}`);
      if (sessionPref) targetId = Number(sessionPref);
    }

    if (!targetId || (!this.userModels.find(m => m.id === targetId) && !this.systemModels.find(m => m.id === targetId))) {
      const globalPref = localStorage.getItem('overseer_chat_model_global');
      if (globalPref) targetId = Number(globalPref);
    }

    if (targetId && (this.userModels.find(m => m.id === targetId) || this.systemModels.find(m => m.id === targetId))) {
      this.selectedUserModelId = targetId;
    } else {
      if (this.userModels.length > 0) {
        this.selectedUserModelId = this.userModels[0].id ?? null;
      } else if (this.systemModels.length > 0) {
        this.selectedUserModelId = this.systemModels[0].id ?? null;
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
    window.removeEventListener('changelog_badge_reset', this.changelogBadgeResetHandler);
    if (this.hubConnection) {
      this.hubConnection.stop();
    }
    
    (window as any).onGnollHackToolResponse = undefined;
    (window as any).__gnollhackReceiveFiles = undefined;
    this.pendingRequests.forEach(timer => clearTimeout(timer));
    this.pendingRequests.clear();
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

  loadSettings(isInit: boolean = false) {
    this.settingsService.getSettings().subscribe(settings => {
      if (settings) {
        this.hasApiKey = settings.hasApiKey;
        this.hasModel = settings.hasModel ?? false;
        if (settings.maxAttachmentSize) {
          this.maxAttachmentSize = settings.maxAttachmentSize;
        }
        this.showDebugLog = settings.showDebugLog ?? false;
        localStorage.setItem('showDebugLog', this.showDebugLog.toString());
        this.debugService.setEnabled(this.showDebugLog);
        
        this.showThoughtsAndTools = Number(settings.showThoughtsAndTools ?? 0);
        this.spoilerFreeMode = settings.spoilerFreeMode === true;
        this.debugService.log(`[Overseer] showThoughtsAndTools loaded: ${this.showThoughtsAndTools} (type: ${typeof this.showThoughtsAndTools})`);

        this.settingsService.getUserModels().subscribe({ next: (models) => {
        this.userModels = models.filter(m => !m.isSystem && (m.modelRole === undefined || (m.modelRole & 1) === 1));
        this.systemModels = models.filter(m => m.isSystem && (m.modelRole === undefined || (m.modelRole & 1) === 1));
        this.hasModel = this.userModels.length > 0 || this.systemModels.length > 0;
        
        if (this.hasModel) {
          this.applySavedModelPreference();
        } else {
          this.singleModelInfo = null;
          this.selectedUserModelId = null;
        }
      },  });
      }

      if (isInit) {
        // Load sessions and handle route AFTER settings are loaded to avoid
        // showThoughtsAndTools race condition (defaulting to 0 before settings arrive)
        this.debugService.log(`[Overseer] Settings loaded, now loading sessions. showThoughtsAndTools=${this.showThoughtsAndTools}`);
        this.loadSessions();
        this.route.queryParams.subscribe(params => {
          const idParam = params['sessionId'];
          if (idParam) {
            const id = Number(idParam);
            if (isNaN(id)) {
              this.navigateToNewSession();
            } else if (this.currentSessionId !== id) {
              if (this.isStreaming) {
                this.debugService.log(`[Frontend] Navigating away from session ${this.currentSessionId} while streaming. Generation continues in background.`);
              }
              this.loadSession(id);
            }
          } else {
            if (this.currentSessionId !== null || this.messages.length > 0) {
              if (this.isStreaming) {
                this.debugService.log('[Frontend] Navigating to new session, clearing local streaming state. Generation continues in background.');
              }
              this.newSession();
            } else {
              this.loadDraft();
            }
          }
        });
      }
    });
  }

  ngOnInit() {
    this.settingsService.showThoughtsAndToolsUpdated.subscribe(val => {
      this.showThoughtsAndTools = val;
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

    window.addEventListener('online', this.onlineHandler);
    window.addEventListener('offline', this.offlineHandler);

    if (!("popover" in HTMLElement.prototype)) {
      import("@oddbird/popover-polyfill").catch(err => console.warn('Failed to load popover polyfill', err));
    }
    
    this.loadSettings(true);

    const savedWidth = localStorage.getItem('overseer_sidebar_width');
    if (savedWidth) {
      const parsed = parseInt(savedWidth, 10);
      if (!isNaN(parsed) && parsed >= 150 && parsed <= 600) {
        this.sidebarWidth = parsed;
      }
    }

    (window as any).onGnollHackToolResponse = (jsonString: string) => {
      try {
          const response: ToolResponse = JSON.parse(jsonString);

          if (response.type !== 'tool_response') {
              return;
          }

          const timer = this.pendingRequests.get(response.requestId);
          if (timer) {
              clearTimeout(timer);
              this.pendingRequests.delete(response.requestId);
          }

          this.sendToolResult(response.requestId, response.success, response.content, response.errorMessage);
      } catch (e) {
          console.error('Failed to parse tool response:', e);
      }
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
    this.changelogService.getReleaseNotes().subscribe({
      next: (response) => {
        if (response.notes && response.notes.length > 0) {
          const latestVersion = response.notes[0].version;
          this.showChangelogAnimation = this.changelogService.hasNewMajorOrMinorVersion(latestVersion);
          this.cdr.detectChanges();
        }
      },
      error: (err) => console.error('Failed to check release notes for animation', err)
    });
  }

  updateHasRealContent() {
    const stripped = this.streamingMessage.replace(/<div class="ai-thought">[\s\S]*?<\/div>/g, '').trim();
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
    }
  }

  processChatEvent(evt: any) {
    if (typeof evt.sessionId === 'number' && evt.sessionId !== this.currentSessionId) return;

    if (evt.seqNo !== undefined && evt.seqNo !== null) {
      if (evt.seqNo <= this.lastSeenSeqNo) {
        this.debugService.log(`[Frontend] Skipping duplicate event seqNo=${evt.seqNo} (lastSeen=${this.lastSeenSeqNo})`);
        return;
      }
      this.lastSeenSeqNo = evt.seqNo;
    }

    if (evt.type === 'debug') {
      this.debugService.log(`[Backend] ${evt.data}`);
    } else if (evt.type === 'status') {
      this.currentStatusText = evt.data;
      this.debugService.log(`[Frontend] status updated to: ${evt.data}`);
      this.cdr.detectChanges();
    } else if (evt.type === 'thinking_chunk') {
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
    } else if (evt.type === 'chunk') {
      this.debugService.log(`[Frontend] chunk received: seqNo=${evt.seqNo} "${evt.data}" streamingMessage.length=${this.streamingMessage.length}`);
      if (this.isThinkingActive) {
          this.debugService.log(`[Frontend] closing ai-thought div before chunk.`);
          this.isThinkingActive = false;
          this.streamingMessage += '\n\n</div>\n\n';
      }
      if (!this.isStreaming) {
        this.isStreaming = true;
        this.showSpinner = false;
        this.currentStatusText = 'Receiving data (background task)...';
      }
      this.hasOngoingGeneration = false;
      this.streamingMessage += evt.data;
      this.updateHasRealContent();
      this.cdr.detectChanges();
      this.scrollToBottomClamped(false);
    } else if (evt.type === 'error') {
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
        const displayName = ChatComponent.TOOL_DISPLAY_NAMES[toolInfo.name] || toolInfo.name;
        const argsText = this.buildToolArgsText(toolInfo.name, args);
        
        this.streamingToolCalls.push({ 
          id: toolInfo.id, 
          name: toolInfo.name, 
          status: 'running',
          displayName,
          argsText
        });
        this.cdr.detectChanges();
        this.scrollToBottomClamped(false);
      } catch(e) {}
    } else if (evt.type === 'tool_result') {
      try {
        const toolInfo = JSON.parse(evt.data);
        const tc = this.streamingToolCalls.find(t => t.id === toolInfo.id && t.status === 'running');
        if (tc) {
          tc.status = 'completed';
          tc.result = toolInfo.result;
        }
        this.cdr.detectChanges();
        this.scrollToBottomClamped(false);
      } catch(e) {}
    } else if (evt.type === 'tool_error') {
      try {
        const toolInfo = JSON.parse(evt.data);
        const tc = this.streamingToolCalls.find(t => t.id === toolInfo.id && t.status === 'running');
        if (tc) {
          tc.status = 'error';
          tc.error = toolInfo.error;
        }
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
        const s = this.sessions.find(x => x.id === data.sessionId);
        if (s) {
          s.title = data.title;
          this.cdr.detectChanges();
        }
      } catch(e) {}
    } else if (evt.type === 'title_status') {
      try {
        const data = JSON.parse(evt.data);
        if (data.sessionId !== this.currentSessionId) return;
    
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
      const stripped = this.streamingMessage.replace(/<div class="ai-thought">[\s\S]*?<\/div>/g, '').trim();
      this.hasRealContent = stripped.length > 0;
      const hasThinking = this.streamingMessage.includes('ai-thought');
      this.debugService.log(`[Frontend] done: ${this.streamingMessage.length} chars, hasThinkingText=${hasThinking}, toolCalls=${this.streamingToolCalls.length}, mode=${this.showThoughtsAndTools}`);
      if (this.isStreaming) {
        if (this.isThinkingActive) {
            this.isThinkingActive = false;
            this.streamingMessage += '\n\n</div>\n\n';
        }
        this.messages.push({ 
          role: 'assistant', 
          content: this.streamingMessage, 
          timestampUtc: new Date().toISOString(), 
          toolCalls: [...this.streamingToolCalls], 
          timeToFirstTokenMs: this.timeToFirstTokenMs ?? undefined,
          modelDisplayName: this.selectedModel?.displayName || this.selectedModel?.modelId || this.singleModelInfo?.modelId,
          thinkingLevel: this.selectedModel?.thinkingLevel || this.singleModelInfo?.thinkingLevel
        });
        this.streamingMessage = '';
        this.streamingToolCalls = [];
        this.timeToFirstTokenMs = null;
        this.isStreaming = false;
        this.showSpinner = false;
        this.currentStatusText = 'Generation complete.';
        this.cdr.detectChanges();
        this.loadSessions(true);
        this.focusPromptInput();
      }
    }
  }

  formatTtft(ms: number | null | undefined): string {
    if (ms == null) return '';
    const seconds = ms / 1000;
    return seconds < 1 ? seconds.toFixed(1) + 's' : Math.round(seconds) + 's';
  }

  getMinimalStatusLabel(): string {
    if (this.streamingToolCalls && this.streamingToolCalls.length > 0) {
      const lastTool = this.streamingToolCalls[this.streamingToolCalls.length - 1];
      return (lastTool.displayName || lastTool.name) + '...';
    }
    return 'Thinking...';
  }

  setupSignalR() {
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
      if (this.currentSessionId) {
        this.hubConnection?.invoke("JoinSession", this.currentSessionId).catch(console.error);
      }
    }).catch(err => console.error('SignalR connection error: ', err));
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
  loadSessions(preserveLoaded: boolean = false) {
    this.loadingSessions = true;
    const take = preserveLoaded && this.sessions.length > 0 ? this.sessions.length : undefined;
    this.chatService.getSessions(0, take).subscribe({
      next: (response) => {
        this.sessions = response.sessions;
        this.hasMoreSessions = response.hasMore;
        this.loadingSessions = false;
      },
      error: (err) => {
        console.error('Failed to load sessions', err);
        this.loadingSessions = false;
      }
    });
  }

  loadMoreSessions() {
    if (this.loadingMoreSessions || this.loadingSessions || !this.hasMoreSessions) return;
    this.loadingMoreSessions = true;
    this.chatService.getSessions(this.sessions.length).subscribe({
      next: (response) => {
        this.sessions = [...this.sessions, ...response.sessions];
        this.hasMoreSessions = response.hasMore;
        this.loadingMoreSessions = false;
      },
      error: (err) => {
        console.error('Failed to load more sessions', err);
        this.loadingMoreSessions = false;
      }
    });
  }

  navigateToNewSession() {
    if (window.innerWidth <= 768) {
      this.closeSidebar();
    }
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
    if (this.currentSessionId === id) return;
    this.router.navigate([], {
      relativeTo: this.route,
      queryParams: { sessionId: id },
      queryParamsHandling: 'merge'
    });
  }

  newSession() {
    this.sessionLoadSub?.unsubscribe();
    this.currentSessionId = null;
    this.lastSeenSeqNo = -1;
    this.messages = [];
    this.isStreaming = false;
    this.streamingMessage = '';
    if (this.realContentTimeout) {
      clearTimeout(this.realContentTimeout);
      this.realContentTimeout = null;
    }
    this.hasRealContent = false;
    this.streamingToolCalls = [];
    this.showSpinner = false;
    this.currentStatusText = '';
    this.isGeneratingTitle = false;
    this.isThinkingActive = false;
    this.titleStatusText = '';
    this.timeToFirstTokenMs = null;
    this.hasOngoingGeneration = false;
    this.isLoadingSession = false;
    this.liveEventBuffer = [];
    if (this.handoffTimeoutHandle) { clearTimeout(this.handoffTimeoutHandle); this.handoffTimeoutHandle = null; }
    this.loadDraft();
    this.applySavedModelPreference();
    this.focusPromptInput();
  }

  async loadSession(id: number) {
    this.sessionLoadSub?.unsubscribe();

    this.isStreaming = false;
    this.lastSeenSeqNo = -1;
    this.streamingMessage = '';
    if (this.realContentTimeout) {
      clearTimeout(this.realContentTimeout);
      this.realContentTimeout = null;
    }
    this.hasRealContent = false;
    this.streamingToolCalls = [];
    this.showSpinner = false;
    this.isThinkingActive = false;
    this.timeToFirstTokenMs = null;

    if (this.hubConnection?.state === signalR.HubConnectionState.Connected) {
      if (this.currentSessionId && this.currentSessionId !== id) {
        this.hubConnection.invoke("LeaveSession", this.currentSessionId).catch(console.error);
      }
    }
    
    this.currentSessionId = id;
    this.isLoadingSession = true;
    this.liveEventBuffer = [];

    this.debugService.log(`[Frontend] loadSession(${id}): hub state BEFORE await = ${this.hubConnection?.state ?? 'null'}`);

    if (this.hubStartPromise) {
      try {
        await this.hubStartPromise;
      } catch {
        // Connection failed — proceed without SignalR
      }
    }

    this.debugService.log(`[Frontend] loadSession(${id}): hub state AFTER await = ${this.hubConnection?.state ?? 'null'}`);

    if (this.hubConnection?.state === signalR.HubConnectionState.Connected) {
      try {
        await this.hubConnection.invoke("JoinSession", id);
        this.debugService.log(`[Frontend] loadSession(${id}): JoinSession succeeded`);
      } catch (err) {
        this.debugService.log(`[Frontend] loadSession(${id}): JoinSession FAILED: ${err}`);
        console.error('JoinSession failed:', err);
      }
    } else {
      this.debugService.log(`[Frontend] loadSession(${id}): JoinSession SKIPPED — hub not connected`);
    }

    if (this.currentSessionId !== id) {
      return;
    }

    this.sessionLoadSub = this.chatService.getSession(id).subscribe({
      next: (s) => {
        if (s.isGnollHackSession) {
          this.chatService.hasGreeted = true;
        }

        this.messages = s.messages || [];
        this.hasOngoingGeneration = s.hasOngoingGeneration === true;
        this.messages.forEach(msg => {
          if (msg.toolCalls) {
            msg.toolCalls.forEach(tc => {
              if (!tc.displayName && tc.name) {
                tc.displayName = ChatComponent.TOOL_DISPLAY_NAMES[tc.name] || tc.name;
              }
            });
          }
        });

        // Debug: summarize loaded messages
        const asstMsgs = this.messages.filter(m => m.role === 'assistant');
        const msgsWithThinking = asstMsgs.filter(m => m.content && m.content.includes('ai-thought'));
        const msgsWithTools = asstMsgs.filter(m => m.toolCalls && m.toolCalls.length > 0);
        const totalTools = asstMsgs.reduce((sum, m) => sum + (m.toolCalls?.length || 0), 0);
        this.debugService.log(`[Frontend] Session ${id} loaded: ${this.messages.length} messages, ${asstMsgs.length} assistant, ${msgsWithThinking.length} with thinking text, ${msgsWithTools.length} with tool calls (${totalTools} total tools). showThoughtsAndTools=${this.showThoughtsAndTools}`);
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
        
        this.isLoadingSession = false;
        if (this.liveEventBuffer.length > 0) {
          const seqNos = this.liveEventBuffer
              .filter((e: any) => e.seqNo != null)
              .map((e: any) => e.seqNo);
          const minSeq = seqNos.length > 0 ? Math.min(...seqNos) : 'none';
          const maxSeq = seqNos.length > 0 ? Math.max(...seqNos) : 'none';
          this.debugService.log(`[Frontend] Flushing ${this.liveEventBuffer.length} buffered live events (seqNo range: ${minSeq}–${maxSeq}, lastSeenSeqNo=${this.lastSeenSeqNo}).`);
          for (const evt of this.liveEventBuffer) {
            this.processChatEvent(evt);
          }
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

        if (!this.sessions.find(x => x.id === id)) {
           this.loadSessions(true);
        }
        this.applySavedModelPreference();
        this.focusPromptInput();
        this.forceWebViewRepaint();
      },
      error: (err) => {
        this.isLoadingSession = false;
        this.liveEventBuffer = [];
        console.warn(`Failed to load session ${id}. Bouncing to new chat.`, err);
        this.navigateToNewSession();
        this.forceWebViewRepaint();
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
    
    this.chatService.deleteSession(id).subscribe(() => {
      if (this.currentSessionId === id) this.navigateToNewSession();
      this.loadSessions(true);
      if (this.deleteConfirmDialog) {
        this.deleteConfirmDialog.nativeElement.close();
      }
      this.sessionToDelete = null;
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

  async sendMessage() {
    const message = this.sanitizeInput(this.currentInput).trim();
    if ((!message && this.pendingAttachments.length === 0) || this.isStreaming) return;

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
    this.streamingMessage = '';
    if (this.realContentTimeout) {
      clearTimeout(this.realContentTimeout);
      this.realContentTimeout = null;
    }
    this.hasRealContent = false;
    this.streamingToolCalls = [];
    this.timeToFirstTokenMs = null;
    this.lastSeenSeqNo = -1; // [NEW] Reset sequence tracker for new generation

    this.focusPromptInput();

    this.requestStartTime = performance.now();
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
      const res = await firstValueFrom(this.chatService.sendMessage(this.currentSessionId, message, attachmentsPayload, uId, sId, currentHasGreeted));
      this.chatService.hasGreeted = true;
      const newSessionId = res.sessionId;
      
      if (this.currentSessionId !== newSessionId) {
        this.currentSessionId = newSessionId;
        
        if (this.hubConnection?.state === signalR.HubConnectionState.Connected) {
          this.hubConnection.invoke("JoinSession", this.currentSessionId).catch(console.error);
        }
        this.loadSessions(true);
        
        const urlTree = this.router.createUrlTree([], {
          relativeTo: this.route,
          queryParams: { sessionId: this.currentSessionId },
          queryParamsHandling: 'merge'
        });
        this.router.navigateByUrl(urlTree, { replaceUrl: true });
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
    this.authService.logout().subscribe(() => this.router.navigate(['/login']));
  }

  triggerFileInput() {
    /* On iOS inside GnollHack, bypass the built-in WKWebView file picker
     * (which shows "Take Photo" and crashes without NSCameraUsageDescription)
     * and use the native bridge to present our own picker instead. */
    if (this.getClientBridge() === 'ios') {
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
        (window as any).webkit.messageHandlers.gnollhackBridge.postMessage(
            JSON.stringify({ type: 'pick_files', sourceRect }));
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
              /* Validate extension against the allowed list */
              const ext = f.name.split('.').pop()?.toLowerCase();
              if (!['html', 'htm', 'txt', 'md', 'png', 'jpg', 'jpeg', 'webp']
                  .includes(ext || '')) continue;
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

  getClientBridge(): 'webview2' | 'android' | 'ios' | null {
    if ((window as any).chrome?.webview) {
        return 'webview2';
    }
    if ((window as any).GnollHackBridge?.onWebMessage) {
        return 'android';
    }
    if ((window as any).webkit?.messageHandlers?.gnollhackBridge) {
        return 'ios';
    }
    return null;
  }

  forwardToolRequest(request: ToolClientRequest): void {
    const bridge = this.getClientBridge();

    if (!bridge) {
        console.error('No client bridge available');
        this.sendToolResult(request.requestId, false, null, 'Client bridge not available');
        return;
    }

    switch (bridge) {
        case 'webview2':
            (window as any).chrome.webview.postMessage(request);
            break;
        case 'android':
            (window as any).GnollHackBridge.onWebMessage(JSON.stringify(request));
            break;
        case 'ios':
            (window as any).webkit.messageHandlers.gnollhackBridge.postMessage(JSON.stringify(request));
            break;
    }

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
            this.currentSessionId || 0,
            success, 
            success ? content : (errorMessage || 'Tool execution failed')
        );
    } catch (e) {
        console.error('Failed to send tool result:', e);
    }
  }

}
