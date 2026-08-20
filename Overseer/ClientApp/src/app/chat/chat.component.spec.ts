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
import { provideRouter } from '@angular/router';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { HttpResponse } from '@angular/common/http';
import { of, Subject } from 'rxjs';
import { ChatService, ChatSessionDetailResponse } from '../services/chat.service';

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
});

