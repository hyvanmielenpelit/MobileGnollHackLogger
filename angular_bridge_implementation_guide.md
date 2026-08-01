# v2 Client Tool Bridge — Angular SPA Implementation Guide

The MAUI client-side bridge is now fully implemented (in [OverseerPage.xaml.cs](file:///c:/hmp/GnollHack/win/win32/xpl/GnollHackX/GnollHackX/Pages/Game/OverseerPage.xaml.cs)). This document describes what the **Angular SPA** (running inside the MAUI WebView) needs to implement to complete the v2 client tool bridge.

> [!IMPORTANT]
> The backend does **not** yet have a `/api/chat/tool-result` endpoint or a real `IClientToolBridge` implementation. Currently only `NullClientToolBridge` exists (always returns `IsClientConnected = false`). Before the Angular SPA changes below are useful, the backend needs:
> 1. A real `IClientToolBridge` implementation that sends tool requests via SignalR and awaits responses (e.g. using `TaskCompletionSource`)
> 2. Either a new `POST /api/chat/tool-result` endpoint on `ChatController`, **or** a new SignalR hub method (e.g. `SubmitToolResult`) on `ChatHub` that completes the pending `TaskCompletionSource`
>
> The approach using a SignalR hub method is recommended since it avoids the need for a separate HTTP POST and the response can be routed directly to the waiting `TaskCompletionSource`.

---

## Architecture Recap

```
Overseer Backend
    ↓ SignalR hub: sends "tool_client_request" event via ReceiveChatEvent
Angular SPA (this document)
    ↓ Detects platform bridge, forwards request via JS call
MAUI Client (already implemented)
    ↓ Executes native game API, sends result back via JS callback
Angular SPA (this document)
    ↑ Receives result via window.onGnollHackToolResponse callback
    ↓ Routes result back to backend (via SignalR hub method or HTTP POST)
Overseer Backend
    ↑ Receives result, completes pending TaskCompletionSource in IClientToolBridge
```

---

## Step 1: Detect Platform Bridge

The Angular SPA runs inside a MAUI WebView. Depending on the platform, a different JS bridge is available. Detection logic:

```typescript
function getClientBridge(): 'webview2' | 'android' | 'ios' | null {
    // Windows — WebView2
    if ((window as any).chrome?.webview) {
        return 'webview2';
    }
    // Android — JavascriptInterface (registered as "GnollHackBridge" in OverseerJsBridge.cs)
    if ((window as any).GnollHackBridge?.onToolRequest) {
        return 'android';
    }
    // iOS — WKWebView script message handler (registered as "gnollhackBridge" in OverseerScriptMessageHandler)
    if ((window as any).webkit?.messageHandlers?.gnollhackBridge) {
        return 'ios';
    }
    return null; // Not running inside MAUI WebView (or bridge not enabled)
}
```

> **Note:** The bridge is only available when the user has enabled "Client Data Access" in their Overseer settings (`enableClientTools = true`) and the MAUI side has `GHApp.OverseerEnableClientTools` set. If `getClientBridge()` returns `null`, the Angular SPA should inform the backend that client tools are unavailable for this session.

---

## Step 2: Forward SignalR Tool Requests to MAUI

When the backend sends a `tool_client_request` message via SignalR, the Angular SPA must forward it to the native MAUI client via the appropriate bridge.

### SignalR message format (from backend)

The backend sends this via the existing `ReceiveChatEvent` SignalR event. All `ReceiveChatEvent` messages use the `ChatEvent` structure: `{ type: string, data: string }`. For a client tool request, the Angular SPA receives:

```json
{
    "type": "tool_client_request",
    "data": "{\"requestId\":\"uuid-123\",\"toolName\":\"get_full_message_history\",\"parameters\":{\"last_n\":200}}"
}
```

The `data` field is a JSON-serialized string containing the actual request payload, consistent with how existing event types (`tool_start`, `tool_result`, `tool_error`) are handled in `chat.component.ts`.

> **Note:** This event type does **not** exist in the backend yet — it needs to be added to the real `IClientToolBridge` implementation. The MAUI client's `HandleToolRequest` method expects the parsed request (see `ClientToolRequest.cs`).

### Handling in Angular

Add a new `else if` branch in the existing `ReceiveChatEvent` handler in `chat.component.ts`:

```typescript
} else if (evt.type === 'tool_client_request') {
    try {
        const request: ToolClientRequest = JSON.parse(evt.data);
        forwardToolRequest(request);
    } catch (e) {
        console.error('Failed to parse tool_client_request:', e);
    }
}
```

### Forwarding to MAUI

First, define the request interface:

```typescript
export interface ToolClientRequest {
    type: string;
    requestId: string;
    toolName: string;
    parameters: any;
}
```

```typescript
function forwardToolRequest(request: ToolClientRequest): void {
    const bridge = getClientBridge();

    if (!bridge) {
        console.error('No client bridge available');
        // Send an error result back to the backend
        sendToolResult(request.requestId, false, null, 'Client bridge not available');
        return;
    }

    switch (bridge) {
        case 'webview2':
            // Windows: WebView2 WebMessageAsJson expects the raw object (not stringified)
            (window as any).chrome.webview.postMessage(request);
            break;

        case 'android':
            // Android: JavascriptInterface method call requires a string argument
            (window as any).GnollHackBridge.onToolRequest(JSON.stringify(request));
            break;

        case 'ios':
            // iOS: WKWebView script message handler expects a string for message.Body.ToString()
            (window as any).webkit.messageHandlers.gnollhackBridge.postMessage(JSON.stringify(request));
            break;
    }
}
```

---

## Step 3: Register Response Callback

The MAUI client sends results back by calling `window.onGnollHackToolResponse(jsonString)` via `EvaluateJavaScriptAsync` (see `SendToolResponse` in `OverseerPage.xaml.cs`). The Angular SPA must register this global callback.

### Response format (JSON string — must be parsed)

```json
{
    "type": "tool_response",
    "requestId": "uuid-123",
    "success": true,
    "content": "...tool result text...",
    "errorMessage": null
}
```

> **Important:** The MAUI client sends the response as a **JSON-encoded string** (double-serialized via `JsonConvert.SerializeObject` for safe JS injection). The callback receives a `string` argument that must be `JSON.parse()`'d.

### Registration

```typescript
interface ToolResponse {
    type: string;
    requestId: string;
    success: boolean;
    content: string;
    errorMessage: string | null;
}

// Register globally — call this once during Angular app initialization
(window as any).onGnollHackToolResponse = (jsonString: string) => {
    try {
        const response: ToolResponse = JSON.parse(jsonString);

        if (response.type !== 'tool_response') {
            return;
        }

        // Send result back to backend
        sendToolResult(
            response.requestId,
            response.success,
            response.content,
            response.errorMessage
        );
    } catch (e) {
        console.error('Failed to parse tool response:', e);
    }
};
```

---

## Step 4: Send Result to Backend

After receiving the response from MAUI, the Angular SPA sends it back to the backend. There are two possible approaches — choose whichever matches the backend implementation:

### Option A: SignalR Hub Method (Recommended)

Add a new hub method `SubmitToolResult` to `ChatHub`. The Angular SPA calls it via the existing SignalR connection:

```typescript
async function sendToolResult(
    requestId: string,
    success: boolean,
    content: string | null,
    errorMessage: string | null
): Promise<void> {
    try {
        // hubConnection is the existing SignalR connection from chat.component.ts
        await hubConnection.invoke('SubmitToolResult', {
            requestId: requestId,
            sessionId: currentSessionId,
            success: success,
            content: success ? content : (errorMessage || 'Tool execution failed')
        });
    } catch (e) {
        console.error('Failed to send tool result:', e);
    }
}
```

### Option B: HTTP POST

Add a new endpoint `POST /api/chat/tool-result` to `ChatController`:

```typescript
async function sendToolResult(
    requestId: string,
    success: boolean,
    content: string | null,
    errorMessage: string | null
): Promise<void> {
    try {
        await fetch('/api/chat/tool-result', {
            method: 'POST',
            headers: { 
                'Content-Type': 'application/json',
                'X-XSRF-TOKEN': getCookie('XSRF-TOKEN') || ''
            },
            body: JSON.stringify({
                requestId: requestId,
                sessionId: currentSessionId,  // from session state
                success: success,
                content: success ? content : (errorMessage || 'Tool execution failed')
            })
        });
    } catch (e) {
        console.error('Failed to send tool result:', e);
    }
}

function getCookie(name: string): string | null {
    const value = `; ${document.cookie}`;
    const parts = value.split(`; ${name}=`);
    if (parts.length === 2) return parts.pop()?.split(';').shift() || null;
    return null;
}
```

> **Note:** If using `fetch`, you **must** include the `X-XSRF-TOKEN` header, otherwise the backend will return 400. The existing `chat.service.ts` already extracts this cookie for `streamMessage()`. Alternatively, you can inject Angular's `HttpClient` which handles this automatically.

---

## Step 5: Timeout Handling

The server uses a **15-second timeout** for client tools (default `TimeoutSeconds` on `IToolHandler`). The Angular SPA should also implement a client-side timeout as a safety net:

```typescript
const CLIENT_TOOL_TIMEOUT_MS = 14000; // slightly less than server's 15s

const pendingRequests = new Map<string, ReturnType<typeof setTimeout>>();

function forwardToolRequest(request: ToolClientRequest): void {
    // ... forward to bridge (Step 2) ...

    // Set timeout
    const timer = setTimeout(() => {
        pendingRequests.delete(request.requestId);
        sendToolResult(request.requestId, false, null, 'Client tool timed out');
    }, CLIENT_TOOL_TIMEOUT_MS);

    pendingRequests.set(request.requestId, timer);
}

// In the response callback (Step 3), clear the timeout:
(window as any).onGnollHackToolResponse = (jsonString: string) => {
    try {
        const response: ToolResponse = JSON.parse(jsonString);

        if (response.type !== 'tool_response') {
            return;
        }

        // Clear timeout
        const timer = pendingRequests.get(response.requestId);
        if (timer) {
            clearTimeout(timer);
            pendingRequests.delete(response.requestId);
        }

        sendToolResult(response.requestId, response.success, response.content, response.errorMessage);
    } catch (e) {
        console.error('Failed to parse tool response:', e);
    }
};
```

---

## Available Client Tools

These are the 4 tools the MAUI client supports (see `AllowedClientTools` in `OverseerPage.xaml.cs` and tool handler classes in `ClientToolHandlers.cs`):

| Tool Name | Description | Parameters (MAUI-side behavior) |
|-----------|-------------|----------------------------------|
| `get_full_message_history` | Returns the game's full message log | `last_n` (int, optional, default 250), `search_term` (string, optional) |
| `get_directory_listing` | Returns a manifest of all files in the game directory | _(none)_ |
| `refresh_snapshot` | Generates a fresh AI snapshot of current game state (HTML) | _(none)_ |
| `get_save_info` | Returns character/location/mode info for a save file | `filename` (string, required — full path) |

> [!WARNING]
> **Server-side schema mismatch:** The `GetSaveInfoTool` class does not override `ParameterSchema`, so it inherits the base class default: `{ "type": "object", "properties": {} }` — meaning the LLM sees no declared parameters for this tool. The MAUI-side implementation (`OverseerPage.xaml.cs` line 1072) **does** expect a `filename` parameter and throws `ArgumentException` if it's missing. The server-side `GetSaveInfoTool` should be updated to override `ParameterSchema` with a `filename` required property to match the MAUI implementation.

---

## Summary Checklist

- [ ] Implement `getClientBridge()` platform detection
- [ ] Handle `tool_client_request` SignalR messages → forward to bridge
- [ ] Register `window.onGnollHackToolResponse` global callback
- [ ] Parse response (JSON.parse the string) and send back to backend
- [ ] Add 14-second client-side timeout for pending requests
- [ ] Report bridge availability to backend during session init (optional)

### Backend Prerequisites (not yet implemented)
- [ ] Implement real `IClientToolBridge` (replace `NullClientToolBridge`) using SignalR + `TaskCompletionSource`
- [ ] Add `SubmitToolResult` hub method to `ChatHub` (or `POST /api/chat/tool-result` endpoint to `ChatController`)
- [ ] Emit `tool_client_request` events via `ReceiveChatEvent` SignalR from the new bridge implementation
- [ ] Fix `GetSaveInfoTool.ParameterSchema` to include required `filename` property
