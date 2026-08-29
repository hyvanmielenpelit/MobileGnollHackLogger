import { ChatComponent } from './chat.component';

describe('ChatComponent.stripThoughts', () => {
  it('should return empty string for null, undefined, or empty input', () => {
    expect(ChatComponent.stripThoughts(null)).toBe('');
    expect(ChatComponent.stripThoughts(undefined)).toBe('');
    expect(ChatComponent.stripThoughts('')).toBe('');
    expect(ChatComponent.stripThoughts('   ')).toBe('');
  });

  it('should return plain text untouched when there are no thinking tags', () => {
    const input = 'This is a normal response without any thinking blocks.';
    expect(ChatComponent.stripThoughts(input)).toBe(input);
  });

  it('should remove a single thinking block', () => {
    const input = '<div class="ai-thought">\n\nThinking about the problem...\n\n</div>\n\nHere is the actual answer.';
    expect(ChatComponent.stripThoughts(input)).toBe('Here is the actual answer.');
  });

  it('should remove multiple consecutive thinking blocks and collapse gaps', () => {
    const input = `
<div class="ai-thought">

I’m checking the GnollHack weapon listings for the exact count of two-handed bludgeoning weapons.

</div>

<div class="ai-thought">

The wiki confirms the category, but not the full item count, so I’m checking the weapon definitions directly to avoid missing special or artifact-base weapons.

</div>

<div class="ai-thought">

I found the two-handed weapon entries; I’m narrowing them to the bludgeoning skill rather than counting all two-handed weapons.

</div>

There is **1** two-handed bludgeoning weapon in GnollHack: the **two-handed club**. It is classified as both \`ENCHTYPE_TWO_HANDED_MELEE_WEAPON\` and \`P_BLUDGEONING_WEAPON\` in \`src/objects.c\` around lines 746–750.
`;
    const expected = 'There is **1** two-handed bludgeoning weapon in GnollHack: the **two-handed club**. It is classified as both `ENCHTYPE_TWO_HANDED_MELEE_WEAPON` and `P_BLUDGEONING_WEAPON` in `src/objects.c` around lines 746–750.';
    expect(ChatComponent.stripThoughts(input)).toBe(expected);
  });

  it('should remove interleaved thinking blocks between response paragraphs', () => {
    const input = 'First paragraph.\n\n<div class="ai-thought">\nThinking...\n</div>\n\nSecond paragraph.';
    expect(ChatComponent.stripThoughts(input)).toBe('First paragraph.\n\nSecond paragraph.');
  });

  it('should remove unclosed thinking block during active streaming', () => {
    const input = 'Intro paragraph.\n\n<div class="ai-thought">\nActively thinking and not closed yet...';
    expect(ChatComponent.stripThoughts(input)).toBe('Intro paragraph.');
  });

  it('should return empty string when message contains only thinking blocks', () => {
    const inputClosed = '<div class="ai-thought">\nJust thinking...\n</div>';
    expect(ChatComponent.stripThoughts(inputClosed)).toBe('');

    const inputUnclosed = '<div class="ai-thought">\nActively thinking streaming start...';
    expect(ChatComponent.stripThoughts(inputUnclosed)).toBe('');
  });

  it('should preserve multi-line formatting and blank lines inside code blocks', () => {
    const input = `<div class="ai-thought">
Thinking about code...
</div>

Here is the code:

\`\`\`python
def calculate(a, b):


    # Notice the multiple blank lines above
    return a + b
\`\`\`

All done!`;

    const expected = `Here is the code:

\`\`\`python
def calculate(a, b):


    # Notice the multiple blank lines above
    return a + b
\`\`\`

All done!`;

    expect(ChatComponent.stripThoughts(input)).toBe(expected);
  });

  it('should handle case insensitivity and tag variations', () => {
    const input = '<DIV CLASS="ai-thought">\nThinking in uppercase tag...\n</DIV>\n\nResult text.';
    expect(ChatComponent.stripThoughts(input)).toBe('Result text.');

    const inputQuotes = "<div class='ai-thought'>\nThinking with single quotes...\n</div>\n\nResult text.";
    expect(ChatComponent.stripThoughts(inputQuotes)).toBe('Result text.');
  });

  it('should handle Windows CRLF line endings properly', () => {
    const input = '<div class="ai-thought">\r\nThinking...\r\n</div>\r\n\r\n\r\n\r\nResult text with CRLF.';
    expect(ChatComponent.stripThoughts(input)).toBe('Result text with CRLF.');
  });
});

import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { HttpResponse } from '@angular/common/http';
import { of, Subject, throwError } from 'rxjs';
import { ChatService, ChatSessionDetailResponse } from '../services/chat.service';
import { SettingsService, UserAiSettings, UserAiModel } from '../services/settings.service';
import { AuthService } from '../services/auth.service';
import { ClientBridgeService } from '../services/client-bridge.service';

describe('ChatComponent session loading and exclusivity', () => {
  let component: ChatComponent;
  let fixture: ComponentFixture<ChatComponent>;
  let chatService: ChatService;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [ChatComponent],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting()
      ]
    }).compileComponents();

    fixture = TestBed.createComponent(ChatComponent);
    component = fixture.componentInstance;
    chatService = TestBed.inject(ChatService);
  });

  it('should immediately clear messages, enable autoScroll, and set isLoadingSession to true on loadSession', async () => {
    // Populate with existing messages
    component.messages = [
      { id: 1, role: 'user', content: 'Previous user message', timestampUtc: '2026-08-20T20:00:00Z' },
      { id: 2, role: 'assistant', content: 'Previous assistant message', timestampUtc: '2026-08-20T20:00:01Z' }
    ];
    component.autoScrollEnabled = false;
    (component as any).hubStartPromise = null;
    (component as any).hubConnection = null;

    const sessionSubject = new Subject<HttpResponse<ChatSessionDetailResponse>>();
    spyOn(chatService, 'getSession').and.returnValue(sessionSubject.asObservable());

    const loadPromise = component.loadSession(42);

    // Verify immediate state change BEFORE response arrives
    expect(component.messages.length).toBe(0);
    expect(component.autoScrollEnabled).toBeTrue();
    expect(component.isLoadingSession).toBeTrue();

    // Yield macro task to let joinSessionAsync resolve and chatService.getSession subscribe
    await new Promise(r => setTimeout(r, 0));

    // Now emit the loaded session
    const mockDetail: ChatSessionDetailResponse = {
      id: 42,
      title: 'New Session',
      messages: [
        { id: 10, role: 'assistant', content: 'Hello in new session', timestampUtc: '2026-08-20T20:01:00Z' }
      ]
    };
    sessionSubject.next(new HttpResponse<ChatSessionDetailResponse>({ body: mockDetail }));
    sessionSubject.complete();

    await loadPromise;

    expect(component.isLoadingSession).toBeFalse();
    expect(component.messages.length).toBe(1);
    expect(component.messages[0].content).toBe('Hello in new session');
  });

  it('should render .conversation-loader and not render .message-box when isLoadingSession is true', () => {
    component.isLoadingSession = true;
    component.messages = [
      { id: 1, role: 'user', content: 'Stale message', timestampUtc: '2026-08-20T20:00:00Z' }
    ];
    fixture.detectChanges();

    const compiled = fixture.nativeElement as HTMLElement;
    const loader = compiled.querySelector('.conversation-loader');
    const messageBoxes = compiled.querySelectorAll('.message-box');

    expect(loader).toBeTruthy();
    expect(loader?.textContent).toContain('Loading conversation...');
    expect(messageBoxes.length).toBe(0);
  });

  it('should render .message-box and not render .conversation-loader when isLoadingSession is false', () => {
    component.isLoadingSession = false;
    component.messages = [
      { id: 1, role: 'user', content: 'Visible message', timestampUtc: '2026-08-20T20:00:00Z' }
    ];
    fixture.detectChanges();

    const compiled = fixture.nativeElement as HTMLElement;
    const loader = compiled.querySelector('.conversation-loader');
    const messageBoxes = compiled.querySelectorAll('.message-box');

    expect(loader).toBeFalsy();
    expect(messageBoxes.length).toBe(1);
    expect(messageBoxes[0].textContent).toContain('Visible message');
  });

  it('should not set isLoadingSession to true or wipe existing messages during syncSessionSilently', async () => {
    component.currentSessionId = 42;
    component.isStreaming = true;
    component.streamingMessage = 'Partial response...';
    component.messages = [
      { id: 1, role: 'user', content: 'User question', timestampUtc: '2026-08-20T20:00:00Z' }
    ];
    component.isLoadingSession = false;

    const mockDetail: ChatSessionDetailResponse = {
      id: 42,
      title: 'Active Session',
      messages: [
        { id: 1, role: 'user', content: 'User question', timestampUtc: '2026-08-20T20:00:00Z' }
      ],
      hasOngoingGeneration: true,
      ongoingGeneration: {
        events: []
      }
    };
    spyOn(chatService, 'getSession').and.returnValue(of(new HttpResponse({ body: mockDetail })));

    await component.syncSessionSilently(42);

    expect(component.isLoadingSession).toBeFalse();
    expect(component.messages.length).toBe(1);
    expect(component.isStreaming).toBeTrue();
    expect(component.streamingMessage).toBe('Partial response...');
  });

  it('should replay missed events with seqNo > lastSeenSeqNo in syncSessionSilently', async () => {
    component.currentSessionId = 42;
    component.isStreaming = true;
    component.lastSeenSeqNo = 2;
    component.messages = [
      { id: 1, role: 'user', content: 'User question', timestampUtc: '2026-08-20T20:00:00Z' }
    ];

    const processSpy = spyOn(component, 'processChatEvent').and.callThrough();

    const mockDetail: ChatSessionDetailResponse = {
      id: 42,
      title: 'Active Session',
      messages: [
        { id: 1, role: 'user', content: 'User question', timestampUtc: '2026-08-20T20:00:00Z' }
      ],
      hasOngoingGeneration: true,
      ongoingGeneration: {
        events: [
          { seqNo: 2, type: 'chunk', data: 'old ' },
          { seqNo: 3, type: 'chunk', data: 'new ' },
          { seqNo: 4, type: 'chunk', data: 'content' }
        ]
      }
    };
    spyOn(chatService, 'getSession').and.returnValue(of(new HttpResponse({ body: mockDetail })));

    await component.syncSessionSilently(42);

    expect(processSpy).toHaveBeenCalledTimes(2);
    expect(component.lastSeenSeqNo).toBe(4);
  });

  it('should adopt messages and clear streaming when hasOngoingGeneration is false with assistant message', async () => {
    component.currentSessionId = 42;
    component.isStreaming = true;
    component.streamingMessage = 'Incomplete';
    component.messages = [
      { id: 1, role: 'user', content: 'User question', timestampUtc: '2026-08-20T20:00:00Z' }
    ];

    const mockDetail: ChatSessionDetailResponse = {
      id: 42,
      title: 'Active Session',
      messages: [
        { id: 1, role: 'user', content: 'User question', timestampUtc: '2026-08-20T20:00:00Z' },
        { id: 2, role: 'assistant', content: 'Complete server response', timestampUtc: '2026-08-20T20:00:05Z' }
      ],
      hasOngoingGeneration: false
    };
    spyOn(chatService, 'getSession').and.returnValue(of(new HttpResponse({ body: mockDetail })));

    await component.syncSessionSilently(42);

    expect(component.isStreaming).toBeFalse();
    expect(component.messages.length).toBe(2);
    expect(component.messages[1].content).toBe('Complete server response');
  });

  it('should no-op when loadSession is called on already active session with messages', async () => {
    component.currentSessionId = 42;
    component.isLoadingSession = false;
    component.messages = [
      { id: 1, role: 'user', content: 'Existing msg', timestampUtc: '2026-08-20T20:00:00Z' }
    ];

    const getSessionSpy = spyOn(chatService, 'getSession');

    await component.loadSession(42);

    expect(getSessionSpy).not.toHaveBeenCalled();
    expect(component.messages.length).toBe(1);
    expect(component.isLoadingSession).toBeFalse();
  });

  it('should trigger scrollToBottomClamped after session loads', async () => {
    (component as any).hubStartPromise = null;
    (component as any).hubConnection = null;

    const scrollSpy = spyOn(component, 'scrollToBottomClamped');

    const mockDetail: ChatSessionDetailResponse = {
      id: 99,
      title: 'Session 99',
      messages: [
        { id: 1, role: 'assistant', content: 'Hello', timestampUtc: '2026-08-20T20:00:00Z' }
      ]
    };
    spyOn(chatService, 'getSession').and.returnValue(of(new HttpResponse({ body: mockDetail })));

    await component.loadSession(99);

    // Yield macro task for setTimeout
    await new Promise(r => setTimeout(r, 0));

    expect(scrollSpy).toHaveBeenCalledWith(false);
  });

  describe('formatDuration and timing metrics', () => {
    it('should format single TTFT values correctly', () => {
      expect(component.formatTtft(null)).toBe('');
      expect(component.formatTtft(undefined)).toBe('');
      expect(component.formatTtft(500)).toBe('0.5s');
      expect(component.formatTtft(9120)).toBe('9s');
      expect(component.formatTtft(30450)).toBe('30s');
    });

    it('should format TTFT and Total Duration pairs correctly with an arrow', () => {
      expect(component.formatDuration(null, null)).toBe('');
      expect(component.formatDuration(undefined, undefined)).toBe('');
      expect(component.formatDuration(9120, null)).toBe('9s');
      expect(component.formatDuration(null, 30450)).toBe('30s');
      expect(component.formatDuration(9120, 30450)).toBe('9s→30s');
      expect(component.formatDuration(500, 1200)).toBe('0.5s→1s');
    });

    it('should determine whether to show reasoning badge correctly', () => {
      expect(component.showReasoningBadge(null)).toBeFalse();
      expect(component.showReasoningBadge(undefined)).toBeFalse();
      expect(component.showReasoningBadge('')).toBeFalse();
      expect(component.showReasoningBadge('default')).toBeFalse();
      expect(component.showReasoningBadge('standard')).toBeFalse();
      expect(component.showReasoningBadge('pro')).toBeTrue();
      expect(component.showReasoningBadge('PRO')).toBeTrue();
    });

    it('should update totalDurationMs when duration event is received', () => {
      expect(component.totalDurationMs).toBeNull();

      component.processChatEvent({ type: 'ttft', data: '9120' });
      expect(component.timeToFirstTokenMs).toBe(9120);
      expect(component.totalDurationMs).toBeNull();

      component.processChatEvent({ type: 'duration', data: '30450' });
      expect(component.totalDurationMs).toBe(30450);
    });

    it('should attach totalDurationMs to assistant message on done event and reset timing state', () => {
      jasmine.clock().install();
      try {
        component.isStreaming = true;
        component.streamingMessage = 'Hello from Overseer';
        component.timeToFirstTokenMs = 9120;
        component.totalDurationMs = 30450;

        component.processChatEvent({ type: 'done', data: '' });

        jasmine.clock().tick(2000);

        expect(component.messages.length).toBe(1);
        expect(component.messages[0].timeToFirstTokenMs).toBe(9120);
        expect(component.messages[0].totalDurationMs).toBe(30450);
        expect(component.timeToFirstTokenMs).toBeNull();
        expect(component.totalDurationMs).toBeNull();
      } finally {
        jasmine.clock().uninstall();
      }
    });
  });

  describe('ChatComponent network error resilience vs normal handling', () => {
    let settingsService: SettingsService;
    let authService: AuthService;
    let router: Router;

    beforeEach(() => {
      settingsService = TestBed.inject(SettingsService);
      authService = TestBed.inject(AuthService);
      router = TestBed.inject(Router);
    });

    describe('loadSettings', () => {
      it('should populate settings and models when API calls succeed normally', () => {
        const mockSettings: UserAiSettings = {
          hasApiKey: true,
          hasModel: true,
          spoilerFreeMode: false,
          showThoughtsAndTools: 1,
          maxAttachmentSize: 10485760
        };
        const mockModels: UserAiModel[] = [
          { id: 1, provider: 'google', modelId: 'gemini-3.7-flash', displayName: 'Gemini 3.7 Flash', isSystem: false, modelRole: 1 }
        ];

        spyOn(settingsService, 'getSettingsResponse').and.returnValue(of(new HttpResponse({ body: mockSettings })));
        spyOn(settingsService, 'getUserModels').and.returnValue(of(mockModels));

        component.loadSettings(false);

        expect(component.hasApiKey).toBeTrue();
        expect(component.hasModel).toBeTrue();
        expect(component.showThoughtsAndTools).toBe(1);
        expect(component.userModels.length).toBe(1);
        expect(component.userModels[0].displayName).toBe('Gemini 3.7 Flash');
      });

      it('should handle TypeError: Failed to fetch on getSettingsResponse without unhandled error', () => {
        spyOn(settingsService, 'getSettingsResponse').and.returnValue(
          throwError(() => new TypeError('Failed to fetch'))
        );

        expect(() => {
          component.loadSettings(true);
        }).not.toThrow();
      });

      it('should handle inner getUserModels failure gracefully when getSettings succeeds', () => {
        const mockSettings: UserAiSettings = {
          hasApiKey: true,
          hasModel: false,
          spoilerFreeMode: false
        };
        spyOn(settingsService, 'getSettingsResponse').and.returnValue(of(new HttpResponse({ body: mockSettings })));
        spyOn(settingsService, 'getUserModels').and.returnValue(
          throwError(() => new TypeError('Failed to fetch'))
        );

        expect(() => {
          component.loadSettings(false);
        }).not.toThrow();
        expect(component.hasApiKey).toBeTrue();
      });
    });

    describe('confirmDelete', () => {
      it('should delete session and clear sessionToDelete on normal success', () => {
        component.sessionToDelete = 123;
        spyOn(chatService, 'deleteSession').and.returnValue(of({} as any));
        spyOn(component, 'loadSessions');

        component.confirmDelete();

        expect(chatService.deleteSession).toHaveBeenCalledWith(123);
        expect(component.sessionToDelete).toBeNull();
      });

      it('should catch TypeError: Failed to fetch on deleteSession and clean up dialog state', () => {
        component.sessionToDelete = 123;
        spyOn(chatService, 'deleteSession').and.returnValue(
          throwError(() => new TypeError('Failed to fetch'))
        );

        expect(() => {
          component.confirmDelete();
        }).not.toThrow();
        expect(component.sessionToDelete).toBeNull();
      });
    });

    describe('executeLogout', () => {
      it('should navigate to /login when logout succeeds normally', () => {
        spyOn(authService, 'logout').and.returnValue(of({}));
        const navigateSpy = spyOn(router, 'navigate');

        component.executeLogout();

        expect(navigateSpy).toHaveBeenCalledWith(['/login']);
      });

      it('should navigate to /login even when logout throws TypeError: Failed to fetch', () => {
        spyOn(authService, 'logout').and.returnValue(
          throwError(() => new TypeError('Failed to fetch'))
        );
        const navigateSpy = spyOn(router, 'navigate');

        expect(() => {
          component.executeLogout();
        }).not.toThrow();
        expect(navigateSpy).toHaveBeenCalledWith(['/login']);
      });
    });
  });

  describe('ChatComponent.getToolDisplayName', () => {
    it('should return symmetrical GnollHack display names for source code tools by default', () => {
      expect(ChatComponent.getToolDisplayName('list_indexed_files')).toBe('Listing GnollHack source files');
      expect(ChatComponent.getToolDisplayName('source_code_search')).toBe('Searching GnollHack source code');
      expect(ChatComponent.getToolDisplayName('source_code_view')).toBe('Viewing GnollHack source code');
      expect(ChatComponent.getToolDisplayName('get_constants')).toBe('Searching GnollHack constants');
      expect(ChatComponent.getToolDisplayName('search_definitions')).toBe('Searching GnollHack definitions');
      expect(ChatComponent.getToolDisplayName('get_function_definition')).toBe('Reading GnollHack function definition');
    });

    it('should return symmetrical NetHack display names when repository is nethack (object or string)', () => {
      expect(ChatComponent.getToolDisplayName('list_indexed_files', { repository: 'nethack' })).toBe('Listing NetHack source files');
      expect(ChatComponent.getToolDisplayName('source_code_search', { repository: 'nethack', query: 'teleport' })).toBe('Searching NetHack source code');
      expect(ChatComponent.getToolDisplayName('source_code_view', { repository: 'NetHack' })).toBe('Viewing NetHack source code');
      expect(ChatComponent.getToolDisplayName('get_constants', 'nethack')).toBe('Searching NetHack constants');
      expect(ChatComponent.getToolDisplayName('search_definitions', { repository: 'NETHACK' })).toBe('Searching NetHack definitions');
      expect(ChatComponent.getToolDisplayName('get_function_definition', { repository: 'nethack' })).toBe('Reading NetHack function definition');
    });

    it('should return correct display names for dedicated single-repository wiki tools', () => {
      expect(ChatComponent.getToolDisplayName('wiki_search')).toBe('Searching GnollHack Wiki');
      expect(ChatComponent.getToolDisplayName('wiki_view')).toBe('Viewing GnollHack Wiki article');
      expect(ChatComponent.getToolDisplayName('nethack_wiki_search')).toBe('Searching NetHack Wiki');
      expect(ChatComponent.getToolDisplayName('nethack_wiki_view')).toBe('Viewing NetHack Wiki article');
    });

    it('should return correct display names for knowledge base, stats, and client tools', () => {
      expect(ChatComponent.getToolDisplayName('get_knowledge_article')).toBe('Searching knowledge base');
      expect(ChatComponent.getToolDisplayName('get_item_stats')).toBe('Reading item stats');
      expect(ChatComponent.getToolDisplayName('get_monster_stats')).toBe('Reading monster stats');
      expect(ChatComponent.getToolDisplayName('get_artifact_stats')).toBe('Reading artifact stats');
      expect(ChatComponent.getToolDisplayName('get_app_log')).toBe('Reading application log');
      expect(ChatComponent.getToolDisplayName('get_full_message_history')).toBe('Reading message history');
    });

    it('should fallback to tool name for unknown tools', () => {
      expect(ChatComponent.getToolDisplayName('custom_unknown_tool')).toBe('custom_unknown_tool');
    });

    it('should format subagent display names correctly', () => {
      expect(ChatComponent.getToolDisplayName('delegate_to_subagent')).toBe('Invoking subagent');
      expect(ChatComponent.getToolDisplayName('delegate_to_subagent', {})).toBe('Invoking subagent');
      expect(ChatComponent.getToolDisplayName('delegate_to_subagent', { agent_name: 'wiki_researcher' })).toBe('Invoking subagent: Wiki Researcher');
      expect(ChatComponent.getToolDisplayName('delegate_to_subagent', { agent_name: 'wiki_researcher', subagent_name: 'Rakshasa stats researcher' })).toBe('Invoking wiki researcher subagent: Rakshasa stats researcher');
      expect(ChatComponent.getToolDisplayName('delegate_to_subagent', { agent_name: 'wiki_researcher', subagentName: 'Rakshasa stats researcher' })).toBe('Invoking wiki researcher subagent: Rakshasa stats researcher');
      expect(ChatComponent.getToolDisplayName('delegate_to_subagent', { agent_name: 'wiki_researcher', subagent_name: '   ' })).toBe('Invoking subagent: Wiki Researcher');
      expect(ChatComponent.getToolDisplayName('delegate_to_subagent', { agent_name: 'wiki_researcher', subagent_name: 'Wiki Researcher' })).toBe('Invoking subagent: Wiki Researcher');
      expect(ChatComponent.getToolDisplayName('delegate_to_subagent', { agent_name: 'wiki_researcher', subagent_name: 'wiki_researcher' })).toBe('Invoking subagent: Wiki Researcher');
      expect(ChatComponent.getToolDisplayName('delegate_to_subagent', { agent_name: 'wiki_researcher', subagent_name: 'a'.repeat(200) })).toBe('Invoking wiki researcher subagent: ' + 'a'.repeat(80));
      expect(ChatComponent.getToolDisplayName('delegate_to_subagent', { agent_name: 'unknown_expert' })).toBe('Invoking subagent: Unknown Expert');
      expect(ChatComponent.getToolDisplayName('delegate_to_subagent', { agent_name: 'custom-agent' })).toBe('Invoking subagent: Custom Agent');
      expect(ChatComponent.getToolDisplayName('delegate_to_subagent', { agent_name: 42 as any })).toBe('Invoking subagent');
      expect(ChatComponent.getToolDisplayName('delegate_to_subagent', 'nethack')).toBe('Invoking subagent');
    });

    it('should format minimal status label with natural tool names when streaming', () => {
      component.streamingToolCalls = [{
        id: '1',
        name: 'list_indexed_files',
        displayName: ChatComponent.getToolDisplayName('list_indexed_files', { repository: 'nethack' }),
        status: 'running'
      }];
      expect(component.getMinimalStatusLabel()).toBe('Listing NetHack source files...');

      component.streamingToolCalls = [{
        id: '2',
        name: 'source_code_search',
        displayName: ChatComponent.getToolDisplayName('source_code_search'),
        status: 'running'
      }];
      expect(component.getMinimalStatusLabel()).toBe('Searching GnollHack source code...');

      component.streamingToolCalls = [{
        id: '3',
        name: 'delegate_to_subagent',
        displayName: 'Invoking wiki researcher subagent: Rakshasa stats researcher',
        status: 'running'
      }];
      expect(component.getMinimalStatusLabel()).toBe('Invoking wiki researcher subagent: Rakshasa stats researcher...');
    });

    it('should format tool args and preserve server-provided displayName in formatMessageToolCalls', () => {
      const messages: any[] = [{
        id: 1,
        role: 'assistant',
        content: 'Done',
        toolCalls: [
          {
            id: 'tc1',
            name: 'delegate_to_subagent',
            displayName: 'Invoking wiki researcher subagent: Persisted Title',
            argsText: '{"agent_name":"wiki_researcher","task":"Compare prayer timeouts","subagent_name":"Persisted Title"}'
          },
          {
            id: 'tc2',
            name: 'delegate_to_subagent',
            displayName: null,
            argsText: '{"agent_name":"source_investigator","task":"Investigate AC calculation"}'
          },
          {
            id: 'tc3',
            name: 'delegate_to_subagent',
            displayName: null,
            argsText: '{"agent_name":"wiki_researcher"}'
          }
        ]
      }];

      (component as any).formatMessageToolCalls(messages);

      expect(messages[0].toolCalls[0].displayName).toBe('Invoking wiki researcher subagent: Persisted Title');
      expect(messages[0].toolCalls[0].argsText).toBe('"Compare prayer timeouts"');

      expect(messages[0].toolCalls[1].displayName).toBe('Invoking subagent: Source Investigator');
      expect(messages[0].toolCalls[1].argsText).toBe('"Investigate AC calculation"');

      expect(messages[0].toolCalls[2].displayName).toBe('Invoking subagent: Wiki Researcher');
      expect(messages[0].toolCalls[2].argsText).toBe('');
    });
  });

  describe('native bridge integration', () => {
    it('should notify ClientBridgeService on loadSession', async () => {
      const bridge = TestBed.inject(ClientBridgeService);
      const sessionSpy = spyOn(bridge, 'notifySessionChanged');

      const mockDetail: ChatSessionDetailResponse = {
        id: 77,
        title: 'Session 77',
        messages: []
      };
      spyOn(chatService, 'getSession').and.returnValue(of(new HttpResponse({ body: mockDetail })));

      (component as any).hubStartPromise = null;
      (component as any).hubConnection = null;

      await component.loadSession(77);

      expect(sessionSpy).toHaveBeenCalledWith(77);
    });

    it('should notify ClientBridgeService on newSession', () => {
      const bridge = TestBed.inject(ClientBridgeService);
      const sessionSpy = spyOn(bridge, 'notifySessionChanged');

      component.newSession();

      expect(sessionSpy).toHaveBeenCalledWith(null);
    });

    it('should forward tool request via ClientBridgeService when embedded', () => {
      const bridge = TestBed.inject(ClientBridgeService);
      spyOn(bridge, 'isEmbedded').and.returnValue(true);
      const postSpy = spyOn(bridge, 'postMessage');

      const request = {
        type: 'client_tool_call',
        requestId: 'req-1',
        toolName: 'get_app_log',
        parameters: {}
      };

      component.forwardToolRequest(request);

      expect(postSpy).toHaveBeenCalledWith(request);
      expect(component.pendingRequests.has('req-1')).toBeTrue();
    });

    it('should fail tool request immediately when not embedded', () => {
      const bridge = TestBed.inject(ClientBridgeService);
      spyOn(bridge, 'isEmbedded').and.returnValue(false);
      const sendResultSpy = spyOn(component, 'sendToolResult');

      const request = {
        type: 'client_tool_call',
        requestId: 'req-2',
        toolName: 'get_app_log',
        parameters: {}
      };

      component.forwardToolRequest(request);

      expect(sendResultSpy).toHaveBeenCalledWith('req-2', false, null, 'Client bridge not available');
    });
  });

  describe('bulk chat actions', () => {
    it('should open and close bulk delete dialog', () => {
      const showModalSpy = jasmine.createSpy('showModal');
      const closeSpy = jasmine.createSpy('close');
      component.bulkDeleteConfirmDialog = {
        nativeElement: { showModal: showModalSpy, close: closeSpy }
      } as any;

      component.openBulkDeleteDialog();
      expect(showModalSpy).toHaveBeenCalled();
      expect(component.includePinnedInBulkDelete).toBeFalse();
      expect(component.isBulkDeleting).toBeFalse();

      component.closeBulkDeleteDialog();
      expect(closeSpy).toHaveBeenCalled();
      expect(component.isBulkDeleting).toBeFalse();
    });

    it('should compute bulkDeleteTargetCount based on includePinnedInBulkDelete', () => {
      component.activeSessionCount = 50;
      component.pinnedSessionCount = 5;
      component.includePinnedInBulkDelete = false;

      expect(component.bulkDeleteTargetCount).toBe(45);

      component.includePinnedInBulkDelete = true;
      expect(component.bulkDeleteTargetCount).toBe(50);
    });

    it('should call bulkDeleteSessions and reload on confirmBulkDelete', () => {
      const closeSpy = jasmine.createSpy('close');
      component.bulkDeleteConfirmDialog = {
        nativeElement: { close: closeSpy }
      } as any;

      spyOn(chatService, 'bulkDeleteSessions').and.returnValue(of({ count: 5 }));
      const loadSessionsSpy = spyOn(component, 'loadSessions');
      component.includePinnedInBulkDelete = true;
      component.currentSessionId = 10;
      component.sessions = [{ id: 10, title: 'Chat 10', isPinned: true, lastMessageUtc: new Date().toISOString() }];
      const navSpy = spyOn(component, 'navigateToNewSession');

      component.confirmBulkDelete();

      expect(chatService.bulkDeleteSessions).toHaveBeenCalledWith(true);
      expect(closeSpy).toHaveBeenCalled();
      expect(navSpy).toHaveBeenCalled();
      expect(loadSessionsSpy).toHaveBeenCalledWith(true);
    });

    it('should open and close unpin all dialog', () => {
      const showModalSpy = jasmine.createSpy('showModal');
      const closeSpy = jasmine.createSpy('close');
      component.unpinAllConfirmDialog = {
        nativeElement: { showModal: showModalSpy, close: closeSpy }
      } as any;

      component.openUnpinAllDialog();
      expect(showModalSpy).toHaveBeenCalled();

      component.closeUnpinAllDialog();
      expect(closeSpy).toHaveBeenCalled();
    });

    it('should call unpinAllSessions and reload on confirmUnpinAll', () => {
      const closeSpy = jasmine.createSpy('close');
      component.unpinAllConfirmDialog = {
        nativeElement: { close: closeSpy }
      } as any;

      spyOn(chatService, 'unpinAllSessions').and.returnValue(of({ count: 3 }));
      const loadSessionsSpy = spyOn(component, 'loadSessions');

      component.confirmUnpinAll();

      expect(chatService.unpinAllSessions).toHaveBeenCalled();
      expect(closeSpy).toHaveBeenCalled();
      expect(loadSessionsSpy).toHaveBeenCalledWith(true);
    });
  });
});


