# Fix "Show Thinking and Tool Use" Settings in Overseer

## Problem Summary

The "Show Thinking and Tool Use" dropdown in Overseer settings does not work properly. Testing has been done with **Gemini 3.6 Flash**, and the bugs primarily revolve around thinking text being mixed into the main response with no way to hide it, and tool blocks not respecting the setting.

## Root Cause Analysis

### The Setting Values

The dropdown (from [settings.component.html L42-47](file:///c:/hmp/MobileGnollHackLogger/Overseer/ClientApp/src/app/settings/settings.component.html#L42-L47)):
| Value | Label | Intended Behavior |
|-------|-------|-------------------|
| `0` | Nothing | Hide thinking text, hide tool blocks, show "Thinking..." placeholder |
| `1` | Tool blocks | Hide thinking text, show tool blocks |
| `2` | Text | Show thinking text, hide tool blocks |
| `3` | Tool blocks and text | Show both |

The **hiding mechanism** works as follows:
- **Thinking text**: The `.messages` container gets class `hide-thoughts` when `showThoughtsAndTools === 0 || showThoughtsAndTools === 1`. CSS rule `.messages.hide-thoughts ::ng-deep .ai-thought { display: none !important; }` hides any `<div class="ai-thought">` inside the rendered markdown.
- **Tool blocks**: The `*ngIf` on the tool-calls containers checks `showThoughtsAndTools === 1 || showThoughtsAndTools === 3`.

For this system to work, the thinking text **must** be wrapped in `<div class="ai-thought">` so the CSS can target it. **This is where everything breaks.**

---

### Bug 1 (PRIMARY — Gemini): Thinking Parts Treated as Normal Text

**Location**: [ChatService.cs L1214-1262](file:///c:/hmp/MobileGnollHackLogger/Overseer/Services/ChatService.cs#L1214-L1262) (`ParseGeminiStream`)

Gemini 3.x thinking models return parts with a `thought: true` property on thinking text parts. Example response:

```json
{
  "candidates": [{
    "content": {
      "parts": [
        { "text": "Let me think about this...", "thought": true },
        { "text": "Here's my answer." }
      ]
    }
  }]
}
```

The current parser at line 1235 only checks `part.TryGetProperty("text", out var text)` and emits **all** text parts as `chunk` events, regardless of whether `thought: true` is present. This means:

- Thinking text is mixed directly into the visible response as regular text
- It gets concatenated into `fullResponse` and saved to the database as regular content
- There is **no `<div class="ai-thought">`** wrapping, so the CSS hiding rule has nothing to target
- The setting dropdown is completely ineffective for Gemini thinking output

**This is the bug you're seeing with Gemini 3.6 Flash.**

### Bug 2 (Gemini): `thinkingConfig` Never Sent in Request

**Location**: [ChatService.cs L791-794](file:///c:/hmp/MobileGnollHackLogger/Overseer/Services/ChatService.cs#L791-L794) (Gemini request building)

For OpenAI, the code sends `reasoning_effort` (L485-488). For Anthropic, it sends `"thinking": { type = "adaptive" }` and `"output_config": { effort = thinkingLevel }` (L618-622). For Google, **nothing is sent** to configure thinking. The `thinkingLevel` from the user's model configuration is completely ignored for Google.

The Gemini API expects:
```json
{
  "generationConfig": {
    "thinkingConfig": {
      "thinkingBudget": 8192
    }
  }
}
```

Where the budget maps from thinking levels like: `minimal`→512, `low`→2048, `medium`→8192, `high`→32768.

Without this, Gemini models use their **default thinking behavior** (which for Gemini 3.6 Flash means thinking is on by default), and the budget is uncontrolled.

### Bug 3 (Frontend): SSE Path Missing Thought-Wrapping

**Location**: [chat.component.ts L888-903](file:///c:/hmp/MobileGnollHackLogger/Overseer/ClientApp/src/app/chat/chat.component.ts#L888-L903) (SSE `sendMessage` path)

The SSE path handles `tool_start` events **without wrapping preceding text** in `<div class="ai-thought">` divs:

- **SignalR path** (lines 503-512): ✅ Wraps preceding text in `<div class="ai-thought">` before pushing tool call
- **SSE path** (lines 888-903): ❌ Simply pushes the tool call, never wraps preceding text

This means intermediate text output by the AI before a tool call is never wrapped on the primary web path, and options 0/1 (which should hide thinking text) cannot work.

> [!NOTE]
> **Backend wrapping already exists**: Both the Gemini (L868-873) and Anthropic (L681-686) streaming loops already wrap `iterationText` in `<div class="ai-thought">` within `fullResponse` when tool calls occur. This means the **saved database content** already gets properly wrapped for text-before-tool-calls. The SSE bug only affects the **live streaming display** — on session reload, the saved content shows correctly.

### Bug 4 (Anthropic): `thinking` Blocks Completely Ignored

**Location**: [ChatService.cs L1139-1212](file:///c:/hmp/MobileGnollHackLogger/Overseer/Services/ChatService.cs#L1139-L1212) (`ParseAnthropicStream`)

When Anthropic models use thinking (enabled via `"thinking": { "type": "adaptive" }` with `"output_config": { "effort": "<level>" }`), Claude sends `thinking` content blocks with `thinking_delta` events. The current parser:
- Handles `text_delta` → yields `chunk` ✅
- Handles `input_json_delta` → accumulates tool args ✅
- **Does NOT handle `thinking_delta`** ❌ — thinking output is silently dropped

### Bug 5 (Architecture): Tool Calls Not Persisted on Session Reload

When a session is loaded from the database ([ChatController.cs GetSession](file:///c:/hmp/MobileGnollHackLogger/Overseer/Controllers/ChatController.cs#L81-L145)), the API returns `Content`, `Role`, `TimestampUtc`, `Attachments`, `ModelDisplayName`, and `ThinkingLevel` — but **no `toolCalls`**. The `ChatMessage` model ([ChatMessage.cs](file:///c:/hmp/MobileGnollHackLogger/GnollHackServer.Data/ChatMessage.cs)) has no `ToolCalls` property. Tool call data only exists in-memory during streaming. On reload, option 1 ("Tool blocks") shows nothing because `msg.toolCalls` is empty.

---

## Proposed Changes

### Component 1: Backend — Gemini Thinking Support (Bug 1 + Bug 2)

#### [MODIFY] [ChatService.cs](file:///c:/hmp/MobileGnollHackLogger/Overseer/Services/ChatService.cs)

**1a. `ParseGeminiStream` — Distinguish thinking parts from text parts (L1235-1238):**

```diff
 foreach (var part in parts.EnumerateArray())
 {
     if (part.TryGetProperty("text", out var text))
     {
-        eventsToYield.Add(new ChatEvent { Type = "chunk", Data = text.GetString() ?? "" });
+        bool isThought = part.TryGetProperty("thought", out var thoughtProp)
+            && thoughtProp.ValueKind == JsonValueKind.True;
+        eventsToYield.Add(new ChatEvent
+        {
+            Type = isThought ? "thinking_chunk" : "chunk",
+            Data = text.GetString() ?? ""
+        });
     }
```

**1b. Gemini main streaming loop (L834-866) — Handle `thinking_chunk` events:**

The existing Gemini event loop uses `if` for `chunk`, then `if/else` for `tool_call_complete`. Add `thinking_chunk` handling. Track thinking boundaries for Option A wrapping (see 1d):

```diff
+               // Add at loop-level scope (before the await foreach):
+               bool hasThinkingContent = false;
+               int thinkingStartIndex = -1;  // Position in fullResponse where current thinking section starts

                {
                    if (evt.Type == "chunk")
                    {
+                       // If we were in a thinking section and now get regular text, record the boundary
+                       if (hasThinkingContent && thinkingStartIndex >= 0)
+                       {
+                           thinkingBoundaries.Add((thinkingStartIndex, fullResponse.Length));
+                           thinkingStartIndex = -1;
+                       }
                        // existing chunk handling (L835-853)...
                    }
                    
                    if (evt.Type == "tool_call_complete")
                    {
                        hasToolsToRun = true;
                        lastEventWasToolCall = true;
                        currentIterationToolCalls.Add(JsonSerializer.Deserialize<JsonElement>(evt.Data));
                        yield return new ChatEvent { Type = "tool_start", Data = evt.Data };
                    }
+                   else if (evt.Type == "thinking_chunk")
+                   {
+                       if (thinkingStartIndex < 0)
+                       {
+                           thinkingStartIndex = fullResponse.Length;
+                       }
+                       hasThinkingContent = true;
+                       fullResponse += evt.Data;
+                       iterationText += evt.Data;
+                       yield return evt;
+                   }
                    else
                    {
                        yield return evt;
                    }
                }
+               // After loop: close any unclosed thinking boundary
+               if (thinkingStartIndex >= 0 && thinkingStartIndex < fullResponse.Length)
+               {
+                   thinkingBoundaries.Add((thinkingStartIndex, fullResponse.Length));
+               }
```

**1c. Gemini request body (L791-794) — Send `thinkingConfig`:**

After the `maxOutputTokens` / `generationConfig` block, add thinking configuration. Note the existing code sets `generationConfig` only if `maxOutputTokens.HasValue`:

```diff
                if (maxOutputTokens.HasValue)
                {
-                   requestBody["generationConfig"] = new { maxOutputTokens = maxOutputTokens.Value };
+                   var genConfig = new Dictionary<string, object>
+                   {
+                       ["maxOutputTokens"] = maxOutputTokens.Value
+                   };
+                   if (!string.IsNullOrEmpty(thinkingLevel))
+                   {
+                       genConfig["thinkingConfig"] = new { thinkingBudget = MapThinkingBudget(thinkingLevel) };
+                   }
+                   requestBody["generationConfig"] = genConfig;
+               }
+               else if (!string.IsNullOrEmpty(thinkingLevel))
+               {
+                   requestBody["generationConfig"] = new
+                   {
+                       thinkingConfig = new { thinkingBudget = MapThinkingBudget(thinkingLevel) }
+                   };
                }
```

Add helper method to `ChatService`:
```csharp
private static int MapThinkingBudget(string thinkingLevel)
{
    return thinkingLevel.ToLower() switch
    {
        "minimal" => 512,
        "low" => 2048,
        "medium" => 8192,
        "high" => 32768,
        _ => 8192
    };
}
```

**1d. Thinking-only response wrapping (Option A) — Wrap thinking text in `fullResponse` using tracked boundaries:**

Add a `thinkingBoundaries` list at the method scope level (alongside `fullResponse`, `iterationText`, etc.):

```csharp
var thinkingBoundaries = new List<(int start, int end)>();
```

After all streaming is complete and before saving to the database (before L953), retroactively wrap thinking sections:

```csharp
// Wrap thinking boundaries in ai-thought divs (for thinking-only responses)
// When tool calls are present, the existing iterationText wrapping (L868-873 / L681-686) 
// already handles it, so only apply this to final fullResponse if there are remaining
// unwrapped thinking sections.
if (thinkingBoundaries.Count > 0 && !string.IsNullOrWhiteSpace(fullResponse))
{
    // Build fullResponse with ai-thought divs inserted at tracked boundaries
    // Process in reverse order to preserve indices
    var sb = new StringBuilder(fullResponse);
    for (int i = thinkingBoundaries.Count - 1; i >= 0; i--)
    {
        var (start, end) = thinkingBoundaries[i];
        if (start >= 0 && end <= sb.Length && start < end)
        {
            sb.Insert(end, "\n\n</div>\n\n");
            sb.Insert(start, "\n\n<div class=\"ai-thought\">\n\n");
        }
    }
    fullResponse = sb.ToString();
}
```

> [!IMPORTANT]
> **Interaction with existing post-loop wrapping**: After the streaming loop (L868-873 Gemini, L681-686 Anthropic), the existing code wraps the entire `iterationText` in `<div class="ai-thought">` when tool calls occurred. The `thinkingBoundaries` tracking from 1b records fine-grained thinking sections. There is a potential conflict: the post-loop tool-call wrapping rewrites `fullResponse` by removing `iterationText` and re-adding it wrapped. After that, the boundary indices stored in `thinkingBoundaries` would be stale. **Solution**: Clear `thinkingBoundaries` when the existing post-loop wrapping fires (inside the `if (hasToolsToRun && currentIterationToolCalls.Count > 0)` block), since that wrapping already handles the text correctly. The Option A boundary-based wrapping should only apply to text that wasn't already wrapped by the tool-call path.

```csharp
// Inside the existing tool-call wrapping block (L868-873 / L681-686), add:
if (hasToolsToRun && currentIterationToolCalls.Count > 0)
{
    // existing wrapping code...
    thinkingBoundaries.Clear(); // Boundaries are stale after rewrite
    // Reset tracking for next iteration
    thinkingStartIndex = -1;
    hasThinkingContent = false;
}
```

---

### Component 2: Backend — Anthropic Thinking Support (Bug 4)

#### [MODIFY] [ChatService.cs](file:///c:/hmp/MobileGnollHackLogger/Overseer/Services/ChatService.cs)

**2a. `ParseAnthropicStream` (L1139-1212) — Handle thinking blocks:**

In `content_block_start` (L1163-1171), refactor the type check to support multiple block types:

```diff
                        if (t == "content_block_start")
                        {
                            var cb = json.GetProperty("content_block");
-                           if (cb.TryGetProperty("type", out var cbType) && cbType.GetString() == "tool_use")
+                           var cbTypeStr = cb.TryGetProperty("type", out var cbType) ? cbType.GetString() : null;
+                           if (cbTypeStr == "tool_use")
                            {
                                currentToolId = cb.GetProperty("id").GetString();
                                currentToolName = cb.GetProperty("name").GetString();
                                currentToolArgs.Clear();
                            }
+                           else if (cbTypeStr == "thinking")
+                           {
+                               isThinkingBlock = true;
+                           }
                        }
```

In `content_block_delta` (L1173-1186), add `thinking_delta` handling:

```diff
                                if (deltaType.GetString() == "text_delta")
                                {
                                    chunkStr = delta.GetProperty("text").GetString();
                                }
                                else if (deltaType.GetString() == "input_json_delta")
                                {
                                    currentToolArgs.Append(delta.GetProperty("partial_json").GetString());
                                }
+                               else if (deltaType.GetString() == "thinking_delta")
+                               {
+                                   var thinkingText = delta.GetProperty("thinking").GetString();
+                                   if (thinkingText != null)
+                                   {
+                                       thinkingChunkStr = thinkingText;
+                                   }
+                               }
```

In `content_block_stop` (L1188-1197), reset the thinking flag:

```diff
                        else if (t == "content_block_stop")
                        {
+                           if (isThinkingBlock)
+                           {
+                               isThinkingBlock = false;
+                           }
                            if (currentToolId != null && currentToolName != null)
                            {
```

Add the required variables at the top of the method (after L1147):

```diff
         string? currentToolId = null;
         string? currentToolName = null;
         StringBuilder currentToolArgs = new StringBuilder();
+        bool isThinkingBlock = false;
```

Add `thinkingChunkStr` variable and yield (alongside `chunkStr` at L1155, and after yield at L1202-1205):

```diff
                string? chunkStr = null;
+               string? thinkingChunkStr = null;
                ChatEvent? toolCallEvt = null;
```

```diff
                if (chunkStr != null)
                {
                    yield return new ChatEvent { Type = "chunk", Data = chunkStr };
                }
+               if (thinkingChunkStr != null)
+               {
+                   yield return new ChatEvent { Type = "thinking_chunk", Data = thinkingChunkStr };
+               }
                if (toolCallEvt != null)
                {
                    yield return toolCallEvt;
                }
```

**2b. Anthropic main streaming loop (L648-679) — Forward `thinking_chunk`:**

Same pattern as Gemini 1b — add `thinking_chunk` handling and boundary tracking:

```diff
                    if (evt.Type == "tool_call_complete")
                    {
                        hasToolsToRun = true;
                        lastEventWasToolCall = true;
                        currentIterationToolCalls.Add(JsonSerializer.Deserialize<JsonElement>(evt.Data));
                        yield return new ChatEvent { Type = "tool_start", Data = evt.Data };
                    }
+                   else if (evt.Type == "thinking_chunk")
+                   {
+                       if (thinkingStartIndex < 0)
+                       {
+                           thinkingStartIndex = fullResponse.Length;
+                       }
+                       hasThinkingContent = true;
+                       fullResponse += evt.Data;
+                       iterationText += evt.Data;
+                       yield return evt;
+                   }
                    else
                    {
                        yield return evt;
                    }
```

Also add boundary tracking variables (`hasThinkingContent`, `thinkingStartIndex`, `thinkingBoundaries`) and the same post-loop wrapping as Component 1d. And add `thinkingBoundaries.Clear()` inside the existing Anthropic tool-call wrapping block (L681-686).

---

### Component 3: Frontend — Handle `thinking_chunk` Events + SSE Thought-Wrapping (Bug 1 + Bug 3)

#### [MODIFY] [chat.service.ts](file:///c:/hmp/MobileGnollHackLogger/Overseer/ClientApp/src/app/services/chat.service.ts)

**3a. Add `thinking_chunk` to the `ChatStreamEvent` type (L39-42):**

```diff
 export interface ChatStreamEvent {
-  type: 'chunk' | 'status' | 'debug' | 'error' | 'sessionId' | 'tool_start' | 'tool_result' | 'tool_error' | 'title_update';
+  type: 'chunk' | 'status' | 'debug' | 'error' | 'sessionId' | 'tool_start' | 'tool_result' | 'tool_error' | 'title_update' | 'thinking_chunk' | 'final';
   data: string;
 }
```

#### [MODIFY] [chat.component.ts](file:///c:/hmp/MobileGnollHackLogger/Overseer/ClientApp/src/app/chat/chat.component.ts)

**3b. Add state tracking property:**

```typescript
private isThinkingActive = false;
```

**3c. In BOTH the SSE path (`sendMessage`, L850-936) and SignalR path (`setupSignalR`, L474-523), handle `thinking_chunk`:**

Add a new `else if` branch before the existing `tool_start` handler:

```typescript
} else if (evt.type === 'thinking_chunk') {
    if (!this.isThinkingActive) {
        this.isThinkingActive = true;
        this.streamingMessage += '\n\n<div class="ai-thought">\n\n';
    }
    this.streamingMessage += evt.data;
    this.cdr.detectChanges();
    this.scrollToBottomClamped(false);
}
```

**3d. Close the thinking div when a regular `chunk` arrives (thinking ended, normal text started):**

In the existing `chunk` handler, add thinking-close logic at the top:

```diff
 } else if (evt.type === 'chunk') {
+    if (this.isThinkingActive) {
+        this.isThinkingActive = false;
+        this.streamingMessage += '\n\n</div>\n\n';
+    }
     // existing chunk handling...
 }
```

> [!NOTE]
> The SSE chunk handler (L873-881) has additional logic for `showSpinner` and TTFB tracking. The thinking-close should be inserted **before** the spinner check.

**3e. SSE path — Add thought-wrapping before tool calls (matching SignalR path):**

The SignalR path (L503-512) already wraps preceding text in `<div class="ai-thought">` before tool calls. Add the same logic to the SSE `tool_start` handler (L888-903). Also close any active thinking div:

```diff
         } else if (evt.type === 'tool_start') {
           try {
             const toolInfo = JSON.parse(evt.data);
             const args = JSON.parse(toolInfo.arguments || '{}');
             const displayName = ChatComponent.TOOL_DISPLAY_NAMES[toolInfo.name] || toolInfo.name;
             const argsText = this.buildToolArgsText(toolInfo.name, args);
+
+            // Close any active thinking div
+            if (this.isThinkingActive) {
+                this.isThinkingActive = false;
+                this.streamingMessage += '\n\n</div>\n\n';
+            }
+
+            // Wrap preceding text in ai-thought div (same logic as SignalR path)
+            if (this.streamingMessage.length > 0) {
+              const lastDivIndex = this.streamingMessage.lastIndexOf('</div>');
+              const thoughtStartIndex = lastDivIndex >= 0 ? lastDivIndex + 6 : 0;
+              const thoughtText = this.streamingMessage.substring(thoughtStartIndex).trim();
+              if (thoughtText.length > 0) {
+                this.streamingMessage = this.streamingMessage.substring(0, thoughtStartIndex)
+                  + '\n\n<div class="ai-thought">\n\n' + thoughtText + '\n\n</div>\n\n';
+              }
+            }
+
             this.streamingToolCalls.push({
               id: toolInfo.id,
               name: toolInfo.name,
               status: 'running',
               displayName,
               argsText
             });
```

> [!NOTE]
> **Interaction between `thinking_chunk` wrapping and `tool_start` wrapping**: If the AI sends `thinking_chunk` events followed by a `tool_start`, the thinking text is already inside a `<div class="ai-thought">` from step 3c, and then the `tool_start` handler closes it (3e). The subsequent "wrap preceding text" logic then finds the text is already wrapped (inside a `</div>` boundary) so `thoughtText` would be empty and no double-wrapping occurs. However, if there is **regular `chunk` text** between the end of thinking and the `tool_start` (e.g., the model outputs some text after thinking, before calling a tool), that text will be wrapped by the `tool_start` handler — which is the correct behavior.

**3f. Reset `isThinkingActive` in cleanup:**

Reset the flag when starting a new message (alongside the existing `streamingMessage = ''` and `streamingToolCalls = []` resets around L837-838):

```typescript
this.isThinkingActive = false;
```

Also close any open thinking div before finalizing the message (in the `finally` block or message completion):

```typescript
if (this.isThinkingActive) {
    this.isThinkingActive = false;
    this.streamingMessage += '\n\n</div>\n\n';
}
```

---

### Component 4: Tool Call Persistence (Bug 5)

#### [NEW] [ChatMessageToolCall.cs](file:///c:/hmp/MobileGnollHackLogger/GnollHackServer.Data/ChatMessageToolCall.cs)

Create a new entity to persist tool call data:

```csharp
namespace MobileGnollHackLogger.Data;

using System.ComponentModel.DataAnnotations;

public class ChatMessageToolCall
{
    public long Id { get; set; }
    
    public long ChatMessageId { get; set; }
    public ChatMessage? ChatMessage { get; set; }
    
    [MaxLength(128)]
    public string? ToolCallId { get; set; }
    
    [MaxLength(256)]
    public string? Name { get; set; }
    
    [MaxLength(256)]
    public string? DisplayName { get; set; }
    
    public string? ArgsText { get; set; }
    
    [MaxLength(32)]
    public string? Status { get; set; }
    
    public string? Result { get; set; }
    
    public string? Error { get; set; }
    
    public int SortOrder { get; set; }
}
```

#### [MODIFY] [ChatMessage.cs](file:///c:/hmp/MobileGnollHackLogger/GnollHackServer.Data/ChatMessage.cs)

Add the navigation property:

```diff
+    public ICollection<ChatMessageToolCall> ToolCalls { get; set; } = new List<ChatMessageToolCall>();
 }
```

#### [MODIFY] [ApplicationDbContext.cs](file:///c:/hmp/MobileGnollHackLogger/GnollHackServer.Data/ApplicationDbContext.cs)

Add the DbSet:

```diff
         public DbSet<UserAiModel> UserAiModels { get; set; } = null!;
+        public DbSet<ChatMessageToolCall> ChatMessageToolCall { get; set; } = null!;
```

#### EF Core Migration

Run after the model changes:

```bash
dotnet ef migrations add AddChatMessageToolCalls -p MobileGnollHackLogger -s MobileGnollHackLogger -o Data/Migrations
dotnet ef database update -p MobileGnollHackLogger -s MobileGnollHackLogger
```

#### [MODIFY] [ChatService.cs](file:///c:/hmp/MobileGnollHackLogger/Overseer/Services/ChatService.cs)

**4a. Collect tool calls during streaming:**

Add a list at the method scope to collect completed tool calls:

```csharp
var completedToolCalls = new List<(string? id, string? name, string? displayName, string? argsText, string? status, string? result, string? error)>();
```

In both the Gemini and Anthropic streaming loops, when a `tool_start` event is emitted (where `tool_call_complete` is received and re-emitted as `tool_start`), record the tool call:

```csharp
// After: yield return new ChatEvent { Type = "tool_start", Data = evt.Data };
// Parse the tool call data to extract fields
var tcData = JsonSerializer.Deserialize<JsonElement>(evt.Data);
var tcId = tcData.TryGetProperty("id", out var idProp) ? idProp.GetString() : null;
var tcName = tcData.TryGetProperty("name", out var nameProp) ? nameProp.GetString() : null;
completedToolCalls.Add((tcId, tcName, null, null, "running", null, null));
```

When tool results/errors come back, update the corresponding entry. After tool execution (where `tool_result` or `tool_error` events are yielded), update the tool call:

```csharp
// After yielding tool_result:
var existingTc = completedToolCalls.FindIndex(tc => tc.id == tId);
if (existingTc >= 0)
{
    var tc = completedToolCalls[existingTc];
    completedToolCalls[existingTc] = (tc.id, tc.name, null, tArgsStr, "completed", resContent, null);
}

// After yielding tool_error:
var existingTcErr = completedToolCalls.FindIndex(tc => tc.id == tId);
if (existingTcErr >= 0)
{
    var tc = completedToolCalls[existingTcErr];
    completedToolCalls[existingTcErr] = (tc.id, tc.name, null, tArgsStr, "error", null, resContent);
}
```

**4b. Save tool calls alongside the assistant message (L953-973):**

After creating `asstMsg` and saving, also save tool calls:

```diff
                 var asstMsg = new ChatMessage
                 {
                     ChatSessionId = currentSessionId,
                     Role = "assistant",
                     Content = fullResponse,
                     TimestampUtc = DateTime.UtcNow,
                     ProviderUsed = provider,
                     ModelUsed = model,
                     ThinkingLevelUsed = thinkingLevel
                 };
                 dbContext.ChatMessage.Add(asstMsg);
+                await dbContext.SaveChangesAsync(CancellationToken.None); // Save first to get asstMsg.Id
+
+                // Save tool calls
+                int sortOrder = 0;
+                foreach (var tc in completedToolCalls)
+                {
+                    dbContext.ChatMessageToolCall.Add(new ChatMessageToolCall
+                    {
+                        ChatMessageId = asstMsg.Id,
+                        ToolCallId = tc.id,
+                        Name = tc.name,
+                        DisplayName = tc.displayName,
+                        ArgsText = tc.argsText,
+                        Status = tc.status,
+                        Result = tc.result,
+                        Error = tc.error,
+                        SortOrder = sortOrder++
+                    });
+                }
+
                 session.LastMessageUtc = DateTime.UtcNow;
-                await dbContext.SaveChangesAsync(CancellationToken.None);
+                if (completedToolCalls.Count > 0)
+                {
+                    await dbContext.SaveChangesAsync(CancellationToken.None);
+                }
```

#### [MODIFY] [ChatController.cs](file:///c:/hmp/MobileGnollHackLogger/Overseer/Controllers/ChatController.cs)

**4c. Include tool calls in session load (L95-110):**

```diff
             var messages = await _dbContext.ChatMessage
                 .Where(m => m.ChatSessionId == id && m.Role != "system" && !m.IsHidden)
                 .OrderBy(m => m.TimestampUtc)
                 .Select(m => new { 
                     m.Id, 
                     m.Role, 
                     m.Content, 
                     m.TimestampUtc,
                     m.ProviderUsed,
                     m.ModelUsed,
                     Attachments = _dbContext.ChatMessageAttachment
                         .Where(a => a.ChatMessageId == m.Id)
                         .Select(a => new { a.Id, a.FileName, a.ContentType })
-                        .ToList()
+                        .ToList(),
+                    ToolCalls = _dbContext.ChatMessageToolCall
+                        .Where(tc => tc.ChatMessageId == m.Id)
+                        .OrderBy(tc => tc.SortOrder)
+                        .Select(tc => new { 
+                            id = tc.ToolCallId, 
+                            tc.Name, 
+                            tc.DisplayName, 
+                            tc.ArgsText, 
+                            tc.Status, 
+                            tc.Result, 
+                            tc.Error 
+                        })
+                        .ToList()
                 })
                 .ToListAsync();
```

And include `ToolCalls` in the `formattedMessages` projection (L128-136):

```diff
             return new {
                 m.Id,
                 m.Role,
                 m.Content,
                 m.TimestampUtc,
                 m.Attachments,
+                m.ToolCalls,
                 ModelDisplayName = modelDisplayName,
                 ThinkingLevel = thinkingLevel
             };
```

#### [MODIFY] [chat.component.ts](file:///c:/hmp/MobileGnollHackLogger/Overseer/ClientApp/src/app/chat/chat.component.ts)

**4d. Map loaded tool calls from the API response:**

Where messages are loaded from the API (in `loadSession` or wherever `GetSession` response is processed), map the `toolCalls` field. The API now returns `toolCalls` with camelCase properties that match the `ChatMessageToolCall` interface. The frontend already has `toolCalls?: ChatMessageToolCall[]` on `ChatMessage`, so this should work automatically if the response shape matches. Verify the field name casing (`argsText`, `displayName`, `status`, `result`, `error`) matches between the API response and the TypeScript interface.

> [!NOTE]
> The `displayName` field needs to be populated. Currently the backend doesn't have the `TOOL_DISPLAY_NAMES` mapping. Two options:
> - **Option A**: Save `displayName` on the backend by looking up the tool name in a server-side display name mapping. This requires adding the mapping (currently only on the frontend at `ChatComponent.TOOL_DISPLAY_NAMES`).
> - **Option B**: Leave `displayName` null from the backend and let the frontend map it on load using the existing `TOOL_DISPLAY_NAMES` map. The template already handles this: `{{ tc.displayName || tc.name }}`.
> 
> **Recommendation**: Use Option B (frontend mapping on load) since the display name mapping already exists there. Add a mapping step when loading session messages.

---

### ~~Component 5: DOMPurify Safeguard~~ (NOT NEEDED)

The `class` attribute is **already** included in the DOMPurify `ADD_ATTR` configuration in [markdown.pipe.ts L34](file:///c:/hmp/MobileGnollHackLogger/Overseer/ClientApp/src/app/chat/markdown.pipe.ts#L34):

```typescript
ADD_ATTR: ['encoding', 'class']
```

No change needed.

---

## Debug Logging Additions

The Overseer already has a `debug` event pipeline: backend emits `ChatEvent { Type = "debug", Data = ... }` → frontend `DebugService` stores entries → viewable at `/debug-log` (non-production). We'll add targeted logging at every stage of the thinking pipeline to narrow down failures.

### Backend Debug Logging (ChatService.cs)

#### 1. Gemini Raw SSE Data Log

Add in `ParseGeminiStream`, inside the `foreach (var part in parts.EnumerateArray())` loop, **before** the if/else chain:

```csharp
// Log each part's type and whether it has a thought property
var partRaw = part.GetRawText();
if (partRaw.Length > 200) partRaw = partRaw.Substring(0, 200) + "...";
eventsToYield.Add(new ChatEvent { Type = "debug", Data = $"[Gemini Part] {partRaw}" });
```

**What this tells you**: Whether Gemini is actually returning `"thought": true` on parts. If the log shows `{"text": "...", "thought": true}`, the parser is receiving thinking parts. If it only shows `{"text": "..."}` with no `thought` property, Gemini isn't returning thinking content (maybe `thinkingConfig` isn't working or the model doesn't support it).

#### 2. Gemini Thinking Event Log

After the new `thinking_chunk` logic in `ParseGeminiStream`:

```csharp
if (isThought)
{
    eventsToYield.Add(new ChatEvent { Type = "debug", Data = $"[Gemini Thinking] Emitting thinking_chunk ({text.GetString()?.Length ?? 0} chars)" });
}
```

**What this tells you**: Whether the parser correctly identifies thinking parts and emits them as `thinking_chunk` events.

#### 3. Gemini Request Body Thinking Config

Already logged at line 820: `$"[Main Chat - Google] Request Body: {jsonRequest}"`. After the fix, check the Debug Log for the request body and verify it contains `"thinkingConfig": {"thinkingBudget": ...}` inside `"generationConfig"`.

**What this tells you**: Whether the thinking budget is being sent to the API.

#### 4. Gemini Event Loop — Thinking Event Forwarding

In the Gemini main streaming loop (around line 834), when handling `thinking_chunk`:

```csharp
else if (evt.Type == "thinking_chunk")
{
    yield return new ChatEvent { Type = "debug", Data = $"[Google Thinking] Forwarding thinking_chunk to frontend ({evt.Data?.Length ?? 0} chars)" };
    // ... existing handling
}
```

**What this tells you**: Whether thinking events survive from the parser to the main streaming loop and are forwarded to the frontend.

#### 5. Anthropic Thinking Block Log

In `ParseAnthropicStream`, when a thinking block starts:

```csharp
else if (cbTypeStr == "thinking")
{
    isThinkingBlock = true;
    yield return new ChatEvent { Type = "debug", Data = "[Anthropic] Thinking block started" };
}
```

And when thinking deltas arrive:

```csharp
else if (deltaType.GetString() == "thinking_delta")
{
    yield return new ChatEvent { Type = "debug", Data = $"[Anthropic Thinking] Received thinking_delta ({thinkingText?.Length ?? 0} chars)" };
}
```

#### 6. Tool Call Persistence Log

After saving tool calls to the database:

```csharp
yield return new ChatEvent { Type = "debug", Data = $"[Persistence] Saved {completedToolCalls.Count} tool calls for message {asstMsg.Id}" };
```

### Frontend Debug Logging (chat.component.ts)

#### 7. Settings Load Confirmation

In `ngOnInit()` after loading settings (line 396):

```typescript
this.showThoughtsAndTools = settings.showThoughtsAndTools ?? 1;
this.debugService.log(`[Settings] showThoughtsAndTools = ${this.showThoughtsAndTools}`);
```

**What this tells you**: Whether the setting is correctly loaded from the API. If this shows `1` but you set it to `0`, there's a save/load issue.

#### 8. Thinking Chunk Handling

In both SSE and SignalR handlers, when `thinking_chunk` is received:

```typescript
} else if (evt.type === 'thinking_chunk') {
    this.debugService.log(`[Thinking] Received thinking_chunk (${evt.data?.length ?? 0} chars), isThinkingActive=${this.isThinkingActive}`);
    // ... rest of handler
}
```

**What this tells you**: Whether thinking events are reaching the frontend at all, and whether the state tracking is correct.

#### 9. Thought Div Wrapping

When wrapping text in `<div class="ai-thought">` (both SSE and SignalR `tool_start` handlers):

```typescript
this.debugService.log(`[Thought Wrap] Wrapped ${thoughtText.length} chars in ai-thought div`);
```

**What this tells you**: Whether intermediate text before tool calls is being wrapped.

#### 10. CSS Class State

Add a one-time log when `hide-thoughts` class would apply:

```typescript
this.debugService.log(`[CSS State] hide-thoughts active = ${this.showThoughtsAndTools === 0 || this.showThoughtsAndTools === 1}`);
```

#### 11. Tool Calls Loaded on Session Reload

When messages are loaded from the API:

```typescript
this.debugService.log(`[Session Load] Loaded ${messages.length} messages, tool calls: ${messages.filter(m => m.toolCalls?.length).length} messages have tool calls`);
```

---

## Test Scenarios

### Test 1: Gemini Thinking Text Visibility

**Purpose**: Verify Gemini thinking text is correctly separated from response text.

**Steps**:
1. Set "Show Thinking and Tool Use" to **"Tool blocks and text" (3)** (show everything)
2. Send a complex question to Gemini 3.6 Flash, e.g.: *"What are the strategic implications of castling in chess? Give me a thorough analysis."*
3. Open `/debug-log` and look for `[Gemini Part]` entries

**Expected outcome**: You should see `[Gemini Part] {"text": "...", "thought": true}` entries for thinking parts, and `[Gemini Part] {"text": "..."}` without `thought` for regular text. The chat should show thinking text in a visually distinct `ai-thought` div.

**If thinking parts don't show `thought: true`**: The model may not be returning thinking content. Check the request body in the debug log for `thinkingConfig`. If it's missing, the request builder fix didn't apply. If it's present but the response has no `thought` parts, the model might not support thinking at the configured budget.

### Test 2: Gemini Thinking CSS Hiding

**Purpose**: Verify the hide-thoughts CSS rule works with Gemini thinking text.

**Steps**:
1. Run Test 1 first to confirm thinking text arrives
2. Change setting to **"Nothing" (0)**
3. Send the same question
4. While streaming, verify: "Thinking..." placeholder shown, no thinking text visible
5. After completion, verify: thinking text hidden in saved message too

**If thinking text is still visible with setting 0**: Open browser DevTools → Elements panel. Find the `.messages` div and check:
- Does it have class `hide-thoughts`? If not, the `[class.hide-thoughts]` binding is wrong
- Find a `<div class="ai-thought">` inside the markdown-body. If none exist, the wrapping isn't working
- If `hide-thoughts` is present AND `ai-thought` divs exist, check the CSS rule `.messages.hide-thoughts ::ng-deep .ai-thought` in the Styles panel — it should show `display: none !important`

### Test 3: Tool Block Visibility

**Purpose**: Verify tool call blocks respect the setting.

**Steps**:
1. Set to **"Text" (2)** (should hide tool blocks)
2. Ask a question that triggers tool use, e.g.: *"Search the GnollHack wiki for information about dragons"*
3. During streaming, tool blocks (🛠️) should NOT appear
4. Change to **"Tool blocks" (1)** and send another tool-triggering question
5. Tool blocks should appear, but thinking text should be hidden

**If tool blocks appear when they shouldn't**: Check `streamingToolCalls` array in the component. The `*ngIf` condition `showThoughtsAndTools === 1 || showThoughtsAndTools === 3` controls this. If the setting value is wrong, check the debug log for `[Settings] showThoughtsAndTools`.

### Test 4: SSE vs SignalR Path

**Purpose**: Verify both code paths handle thinking correctly.

**Steps**:
1. Send a message normally (SSE path) — check thinking wrapping works
2. Open a second browser tab with the same session. In the first tab, send a message — the second tab receives events via SignalR. Check if thinking wrapping also works in the second tab.

**If SignalR path fails but SSE works (or vice versa)**: The handler code was only added to one path. Check both the `sendMessage()` for-await loop and the `setupSignalR()` `ReceiveChatEvent` handler.

### Test 5: DOMPurify Stripping

**Purpose**: Verify `<div class="ai-thought">` survives markdown sanitization.

**Steps**:
1. Open browser DevTools console
2. Run: `document.querySelectorAll('.ai-thought').length` — should return > 0 if thinking text was in the response
3. If 0, check the raw `msg.content` in the messages array by inspecting the Angular component state

**If `ai-thought` divs are being stripped**: Verify the DOMPurify config in [markdown.pipe.ts L31-35](file:///c:/hmp/MobileGnollHackLogger/Overseer/ClientApp/src/app/chat/markdown.pipe.ts#L31-L35) still has `ADD_ATTR: ['encoding', 'class']`. You can test in the console:
```javascript
DOMPurify.sanitize('<div class="ai-thought">test</div>', {USE_PROFILES: {html: true}, ADD_ATTR: ['class']})
```
This should return the div intact.

### Test 6: Session Reload Persistence (Thinking Text)

**Purpose**: Verify thinking text survives session reload.

**Steps**:
1. Send a message with thinking content (setting = 3 to see everything)
2. Note the `<div class="ai-thought">` blocks in the response
3. Navigate away and back to the session
4. Check if `ai-thought` divs are still in the rendered HTML

**If thinking text disappears on reload**: The backend's `fullResponse` didn't include the `ai-thought` wrappers. Check the database content for the message — if it has raw thinking text without `<div class="ai-thought">` wrapping, the Option A boundary-based wrapping (Component 1d) isn't working. Check the debug log for the thinking boundary tracking.

### Test 7: Session Reload Persistence (Tool Calls)

**Purpose**: Verify tool calls survive session reload.

**Steps**:
1. Send a message that triggers tool use (setting = 3 to see everything)
2. Note the tool call blocks (🛠️) displayed during and after streaming
3. Navigate away and back to the session
4. Check if tool call blocks are still visible with status, results, and errors intact

**If tool calls disappear on reload**: Check:
- Database: Query `ChatMessageToolCall` table for the message ID — are rows present?
- API response: Check the network tab for the `GetSession` response — does it include `toolCalls` on the message?
- Frontend: Check the debug log for `[Session Load]` entries

---

## Troubleshooting Flowchart

If the fix doesn't work, follow this debug sequence using the `/debug-log` page:

```
1. Is thinkingConfig in the request body?
   └─ NO → Fix: Check Gemini request builder (Component 1c)
   └─ YES ↓

2. Do [Gemini Part] logs show "thought": true?
   └─ NO → The API isn't returning thinking parts. 
           Check if the model actually supports thinking.
           Try thinkingBudget = -1 (let model decide).
   └─ YES ↓

3. Do [Gemini Thinking] logs show thinking_chunk events emitted?
   └─ NO → Fix: ParseGeminiStream isn't detecting thought property
   └─ YES ↓

4. Do [Google Thinking] logs show forwarding to frontend?
   └─ NO → Fix: Main streaming loop isn't handling thinking_chunk
   └─ YES ↓

5. Does frontend [Thinking] log show chunks received?
   └─ NO → Event type mismatch between backend and frontend
   └─ YES ↓

6. Does the DOM contain <div class="ai-thought">?
   └─ NO → DOMPurify is stripping it (check markdown.pipe.ts)
   └─ YES ↓

7. Does .messages have class hide-thoughts?
   └─ Check showThoughtsAndTools value matches the setting.
   └─ If value is correct but class is missing, check the
      [class.hide-thoughts] binding in the template.
```

---

## Verification Plan

### Manual Verification
1. **Gemini 3.6 Flash with thinking**: Run Tests 1-2 above
2. **Gemini with tool use**: Run Test 3
3. **Both code paths**: Run Test 4
4. **DOMPurify**: Run Test 5
5. **Session reload (thinking)**: Run Test 6
6. **Session reload (tool calls)**: Run Test 7
7. **Anthropic (Claude) with thinking**: Same tests 1-2 if Anthropic API key is available
8. **Setting changes mid-session**: Change the setting while a chat session is open, then send a new message — verify the new setting applies immediately

> [!NOTE]
> **Note on OpenAI**: OpenAI reasoning models (o1, o3, etc.) do **not** expose thinking text via the streaming API — they use internal reasoning tokens counted in usage stats but the text isn't streamed. No changes needed on the OpenAI path.
