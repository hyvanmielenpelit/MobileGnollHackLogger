---
name: sentry_logging_architecture
description: Documentation of the Sentry crash logging architecture for the Overseer project, including the tunnel endpoint, authentication filtering, and source map exclusion logic.
---

# Sentry Logging Architecture for Overseer

This document explains the architecture implemented for Sentry crash logging in the Overseer ASP.NET Core & Angular application.

## 1. Zero DSN Exposure & Tunneling

The Sentry DSN is considered a highly sensitive secret in this project. Exposing it in the Angular frontend would allow unauthenticated actors to forge crash reports containing prompt-injection payloads, which could compromise the AI assistant that reads these logs via the Sentry MCP server.

To mitigate this:
- The Angular frontend initializes Sentry with a placeholder DSN and uses the `tunnel: '/api/sentry/log'` option.
- The `SentryTunnelController` in the ASP.NET Core backend acts as a proxy.
- The backend requires the user to be authenticated (`[Authorize]`).
- The backend reads the envelope, finds the first `\n` byte to separate the header from the binary payload, and **rewrites the placeholder DSN with the real DSN** from the backend's configuration before forwarding the request to Sentry.

## 2. SSRF Prevention

Because the Sentry envelope header specifies the `dsn` to which the envelope belongs, a blind proxy is vulnerable to Server-Side Request Forgery (SSRF) — an attacker could specify a malicious DSN pointing to an internal IP (e.g., `169.254.169.254`).

To prevent this, the `SentryTunnelController` ignores the host provided in the incoming envelope header. It extracts the real host and project ID exclusively from the securely stored server-side `SentryDSN`.

## 3. Error Filtering (Logging Decision Matrix)

We strictly filter out bot noise and expected application errors (like invalid user input or external API overloads).

| Scenario | Handled By | Outcome |
|----------|------------|---------|
| Unauthenticated user | `SentryTunnelController` & `AuthSentryEventProcessor` | Dropped (blocked) |
| Frontend HTTP error (4xx/5xx) | Angular `beforeSend` hook | Dropped (prevents double-logging) |
| Transient AI API error (429, 502, 503) | `AuthSentryEventProcessor` (backend) | Dropped (safety net) |
| Invalid AI API key | `ChatService.cs` / `SettingsController.cs` | Dropped (handled inline as a user error, never throws) |
| Valid, unexpected crash | Both | Logged ✅ |

> **Crucial Pattern:** Do not throw exceptions for expected user misconfigurations. E.g., `ChatService.ExecuteApiWithRetriesAsync` yields a `ChatEvent { Type = "error" }` instead of throwing. This prevents noise from reaching Sentry.

## 4. Source Maps & MSBuild Exclusion

To ensure stack traces are readable, Angular generates source maps. However, `.map` files must not be exposed on the public web server or bloat the FTP payload.

- `angular.json` is configured with `sourceMap: { hidden: true }`.
- `sentry-cli` is used to inject Debug IDs and upload the source maps after `npm run build`.
- The `Overseer.csproj` uses MSBuild to physically exclude `.map` files from the publish output: `<DistFiles Include="wwwroot\**" Exclude="wwwroot\**\*.map" />`.

See `Overseer/SENTRY_SOURCEMAPS_GUIDE.md` for full CI/CD deployment instructions.
