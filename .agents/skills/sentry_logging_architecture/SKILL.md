---
name: sentry_logging_architecture
description: Documentation of the Sentry crash logging architecture for the Overseer project, including the tunnel endpoint, authentication filtering, AI provider error suppression, and source map exclusion logic.
---

# Sentry Logging Architecture for Overseer

This document explains the architecture implemented for Sentry crash logging in the Overseer ASP.NET Core & Angular application.

---

## 1. Core Architectural Principle: Application Handling vs Sentry Error Logging

A foundational principle of Overseer's logging architecture is the strict separation between **Application Error Handling** and **Sentry Crash Logging**:

- **Application Error Handling**: When operational errors occur (e.g., transient network drops, AI provider overload, invalid user API keys, wrong model names, quota limits), the application **MUST handle them gracefully**:
  - Retry transient HTTP errors using exponential backoff in `ChatService.cs`.
  - Stream clear, structured user-facing messages to the frontend via SignalR (`ChatEvent { Type = "error", Data = "..." }`).
  - Return clean HTTP `BadRequest` responses with descriptive messages in REST controllers (e.g. `SettingsController.cs`).
  - Record usage and provider budget depletion in the database for administrative reporting.
- **Sentry Crash Logging**: Sentry is reserved **exclusively for unexpected bugs, runtime crashes, and unhandled software defects** (such as `NullReferenceException`, unhandled database connection failures, logic bugs, or unhandled Angular frontend crashes).
- **Suppression from Sentry**: Operational failures, upstream third-party AI provider outages (5xx/429), and user misconfigurations are **normal application events**, NOT application bugs. Therefore, they are **dropped and not sent to Sentry**.

---

## 2. Error Filtering & Suppression Matrix

The table below outlines how various errors are handled in the application and why/how they are filtered from Sentry:

| Error Category | Specific Status / Exception | Handled in Application Logic | Sentry Outcome & Mechanism |
|---|---|---|---|
| **AI Provider Overload & Server Outages** | `429`, `500`, `501`, `502`, `503`, `504`, `529`, and all 5xx status codes targeting AI providers | `ChatService.cs` retries transient errors with exponential backoff (1s, 5s, 10s, 20s, 30s, 60s). If max retries are exceeded, streams a friendly `ChatEvent` error message to the client. | **Dropped (Filtered)**: `AuthSentryEventProcessor` intercepts Sentry synthetic HTTP events and `HttpRequestException`s matching `AiProviderHosts` and returns `null`. |
| **Invalid User API Key** | `401 Unauthorized` (OpenAI, Anthropic), `400 Bad Request` / `403 Forbidden` (Google) | `ChatService.cs` yields a `ChatEvent` error; `SettingsController.cs` returns `BadRequest(new { message = ... })`. | **Not Logged**: Sentry `SentryHttpMessageHandler` only captures 5xx by default; no unhandled exception is thrown. |
| **Non-existent / Invalid Model ID** | `404 Not Found` (OpenAI, Anthropic, Google), `400 Bad Request` | `ChatService.cs` streams the provider error body back to the user; `SettingsController.cs` returns `BadRequest`. | **Not Logged**: 4xx responses are ignored by Sentry HTTP handler; application handles inline without throwing. |
| **Model Parameter Mismatch / Context Exceeded** | `400 Bad Request` | `ChatService.cs` streams the error body to the client UI. | **Not Logged**: 4xx ignored by Sentry; application handles gracefully. |
| **Budget / Quota Exhaustion** | `402 Payment Required`, `429 insufficient_quota` | Recorded in database via `SystemAiConfigService.RecordErrorAsync`; informs user via SignalR. | **Not Logged**: Handled inline without throwing unhandled exceptions. |
| **Missing API Key / Unsupported Provider** | Pre-flight validation | `ChatService.cs` pre-validates keys/providers before sending network requests; yields user-facing error. | **Not Logged**: Never executes network call; no exception thrown. |
| **Unauthenticated Bot / Probe** | `401 Unauthorized`, `403 Forbidden` on web routes | ASP.NET Core Identity authentication middleware rejects request. | **Dropped (Filtered)**: `AuthSentryEventProcessor` drops any event where `httpContext.User.Identity.IsAuthenticated != true`. |
| **Frontend HTTP Failure & Network Drops** | Any HTTP 4xx/5xx in Angular client, `TypeError: Failed to fetch`, `AbortError`, `NetworkError` | Angular components display toasts/banners to the user; provide `error:` callbacks on RxJS subscriptions. | **Dropped (Filtered)**: Angular `beforeSend` hook in `main.ts` recursively unwraps Zone.js/RxJS errors and drops all `HttpErrorResponse` and browser-native fetch dropouts to prevent duplicate noise and spurious reports from device sleep/roaming. |
| **Unexpected App Bug / Crash** | `NullReferenceException`, unhandled 500 in controllers, unhandled Angular crash | Global exception handler / Angular error handler. | **Logged to Sentry ✅**: Full stack trace and breadcrumbs captured for developer triage. |

---

## 3. Frontend Error Handling & Sentry Filtering Architecture

### 3.1 Sentry `beforeSend` Network Error Filter (`main.ts`)
When using `@angular/common/http` with `withFetch()`, transient connection drops (e.g. mobile device sleep, network switching) throw native browser errors (`TypeError: Failed to fetch`, WebKit `TypeError: Load failed`, `DOMException: AbortError`). In Angular, Zone.js and RxJS may wrap these errors inside `rejection`, `ngOriginalError`, or `error` properties.

To ensure transient client disconnects never create spurious issues in Sentry:
1. **`isHttpOrNetworkError(err)` Helper**:
   - Inspects direct and nested `HttpErrorResponse` instances (`instanceof HttpErrorResponse` and `err?.name === 'HttpErrorResponse'`).
   - Checks error messages against known native fetch error signatures (`failed to fetch`, `networkerror`, `load failed`, `fetch failed`, `aborterror`, `timeout`).
   - Recursively unwraps Zone.js / Angular wrapper properties (`err.rejection`, `err.ngOriginalError`, `err.originalError`, `err.error`).
2. **`event.exception.values` Inspection**:
   - Inspects normalized exception values in Sentry's `event` payload as a fallback in case raw exception references were lost during transmission.

### 3.2 RxJS Subscription Error Handling Requirement
In Angular/RxJS, any observable subscription (`observable.subscribe(...)`) that does not provide an `error:` callback will cause unhandled errors to bubble straight to Angular's global `ErrorHandler` (`Sentry.createErrorHandler()`).
- **Rule**: All HTTP observable subscriptions in components MUST supply an `error:` callback (e.g. `.subscribe({ next: (res) => { ... }, error: (err) => { ... } })`).
- If an error is non-fatal or handled elsewhere, an explicit empty error callback `error: () => {}` or debug log callback `error: (err) => this.debugService.log(...)` must be provided.

---

## 4. Architecture of `AuthSentryEventProcessor`

`AuthSentryEventProcessor` (`Overseer/Services/AuthSentryEventProcessor.cs`) is registered as an `ISentryEventProcessor` in ASP.NET Core DI:

1. **Authentication Check**:
   - If `HttpContext` exists and the user is not authenticated (`IsAuthenticated != true`), the event is immediately discarded to eliminate external scanner and bot noise.
2. **External Service & Tool Host Registry**:
   - Each supported provider class (`GoogleProvider`, `AnthropicProvider`, `OpenAiResponsesProvider`) declares a public static collection `ProviderHosts`.
   - External lookup tools (`ExternalToolHosts`) declare external hosts used by tools (`GitHubHosts` for `api.github.com` & `github.com`).
   - `AuthSentryEventProcessor` aggregates these into `ExternalServiceHosts`.
3. **External Upstream 5xx, 429, and Transient Network Filtering**:
   - Synthetic HTTP events from Sentry's `SentryHttpFailedRequestHandler` and raw `HttpRequestException` instances targeting external service hosts are inspected.
   - Any status code in the **500–599** range (including 500, 501, 502, 503, 504, 529), status code **429**, and network connection drop exceptions are dropped (`return null`).
4. **Preservation of Internal Errors**:
   - If an internal endpoint or internal service fails with an unexpected exception or 500 error, `AuthSentryEventProcessor` preserves the event so developers are alerted in Sentry.

---

## 5. Zero DSN Exposure & Secure Tunneling

The Sentry DSN is considered a sensitive secret in Overseer to prevent unauthenticated actors from forging crash reports or staging Prompt Injection attacks against the AI triage assistant.

1. **Frontend Configuration**:
   - The Angular frontend initializes Sentry with a placeholder DSN and sets `tunnel: '/api/sentry/log'`.
   - A custom transport using `Sentry.makeFetchTransport` sets `credentials: 'include'` so browser cookies accompany tunnel requests.
2. **Backend Proxy (`SentryTunnelController`)**:
   - Protected by `[Authorize]` and rate-limited (`TunnelRateLimit`).
   - Safely parses the envelope header, rewrites the `dsn` and `public_key` to match server secrets, and forwards the payload to Sentry's ingest servers.
   - Ignores any incoming host in the envelope to completely eliminate Server-Side Request Forgery (SSRF).

---

## 6. Source Maps & MSBuild Exclusion

- `angular.json` sets `sourceMap: { hidden: true }`.
- `sentry-cli` injects Debug IDs and uploads hidden source maps to Sentry during release workflows.
- `Overseer.csproj` MSBuild target excludes `.map` files from public publication:
  ```xml
  <DistFiles Include="wwwroot\**" Exclude="wwwroot\**\*.map" />
  ```
