# Sentry Error Reporting for the Website (MobileGnollHackLogger)

The website — the Razor Pages and the game API controllers under `Areas/API/` — reports backend errors to Sentry through `Sentry.AspNetCore`. There is no client-side reporting: no browser SDK, no tunnel endpoint, no source maps.

Overseer has its own, larger setup; see [overseer/sentry-logging-architecture.md](overseer/sentry-logging-architecture.md).

---

## 1. What Is Reported

- **Unhandled exceptions.** `UseExceptionHandler("/Error")` in production and the developer exception page in Development both log the exception at Error, and the SDK reports it.
- **`ILogger` entries at Error level.** Two exist today:
  - `Areas/Identity/Pages/Account/Register.cshtml.cs`: recording the Terms acceptance failed.
  - `Areas/Identity/Pages/Account/Manage/DeletePersonalData.cshtml.cs`: the account deletion review email could not be sent. This email goes to the operator's `AccountHold:ReviewNotificationEmail` address and is the operator's only signal that a deletion is waiting for review, so it is the one email failure that is reported.
- **The game API's caught server failures.** Each catch block that answers 500 for a server-side failure calls `SentrySdk.CaptureException(ex);` before its database request log entry:

  | Controller | Catch blocks captured |
  |------------|-----------------------|
  | `LogController.Post` | user verification; game log insertion; the outermost catch |
  | `BonesController.Post` | user verification; adding a bones entry (`SendBonesFile`); reading the bones file and the bones request branch around it; the confirmation receipt branch that deletes the file (`ConfirmReceipt`); the outermost catch |
  | `ReplayController.Post` | user verification; the outermost catch |
  | `SaveFileTrackingController` | `Create` database insertion; `Use`; `LogInAsync` user verification and its outermost catch |

**One failure, one event.** For a database failure EF Core logs the failed command at Error ("Failed executing DbCommand …", category `Microsoft.EntityFrameworkCore.Database.Command`) before the controller catches the wrapping `DbUpdateException`. The SDK's duplicate detection keeps the first report and drops the controller's capture of the same exception. So for a database failure, look for EF Core's command error event; its message carries the SQL text, with parameter values masked as long as `EnableSensitiveDataLogging` stays off. The controller captures are the only report for everything else: file I/O under the dumplog and bones directories, xlog parsing, Identity lookups.

## 2. What Is Not Reported

- **Browser errors.** There is no client SDK.
- **Catch blocks left out on purpose:**
  - `SaveFileTrackingController.Create`, "Error parsing Sha256 from Base64 string." — client input, 400.
  - `SaveFileTrackingController.Decrypt`, "Decryption failed." — the decrypted value comes from the client, so a garbage or tampered value fails here. It answers 500 but is not a server bug, and reporting it would let any caller fill Sentry.
  - `catch (InvalidOperationException)` → 410 in `LogController.Post` and `BonesController.Post` — the handled "gone" outcome.
  - The bare `catch` around the bone counting query in `BonesController.Post`, which falls back to counts of -1 and lets the upload proceed. A broken query would surface in the next, captured, query.
  - `catch (DbUpdateConcurrencyException)` in `LogController.GetAsyncNew` — expected and ignored.
  - The email-sending catches in `ForgotPassword`, `Register` and `ResendEmailConfirmation`. These are Azure email service failures, an operational upstream class of error, and the exception text can contain the recipient's address.
- **Client protocol errors and aborted requests**, dropped by `WebsiteSentryEventProcessor` (`MobileGnollHackLogger/Monitoring/`):
  1. An exception chain containing `BadHttpRequestException` — Kestrel's malformed or truncated request, typically a mobile client dropping mid-upload. Answered with 400.
  2. When the request was aborted (`HttpContext.RequestAborted` is cancelled) **and** the chain contains an `OperationCanceledException`, an `IOException`, or a `SqlException` whose message contains "Operation cancelled by user". Both conditions are required, so a genuine I/O or SQL failure on a live request is still reported.

  The chain walk follows `InnerException` and every member of an `AggregateException`: `LogController.Post` copies its upload streams with `Task.WaitAll`, so an upload failure arrives wrapped in one.

## 3. What Is Scrubbed and Pinned

`WebsiteSentryEventProcessor` scrubs every event it lets through:

- Request headers `Authorization`, `Cookie`, `Set-Cookie`, `Proxy-Authorization`, `X-XSRF-TOKEN`, `X-Api-Key`, `X-Sentry-Auth`, and the separate `Request.Cookies` property, become `[scrubbed]`.
- Query values named `token`, `key`, `apiKey`, `api_key`, `password`, `code`, `secret`, `access_token`, `email` and `userId` become `[scrubbed]` in `Request.QueryString` and in the query part of `Request.Url`. Names match exactly and case-insensitively, and the query keeps its shape. `email` and `userId` are website-specific: the Identity pages `ConfirmEmail`, `ConfirmEmailChange` and `ResetPassword` carry them in their links.
- `User.Email`, `User.Username` and `User.IpAddress` are cleared, and so is `User.Other`.
- Tags with any of the scrubbed query names are scrubbed.
- Breadcrumbs: the same query values are blanked wherever they appear in a breadcrumb's message or data, such as the URL in ASP.NET Core's "Request starting" log entry. This runs in `ScrubBreadcrumb`, registered with `SetBeforeBreadcrumb` in `Program.cs`, because an event's breadcrumbs cannot be replaced in the event processor. A name counts only after `?` or `&`, so `postcode=` is kept.

`UseSentry` in `Program.cs` pins two options that are already the SDK defaults, so an upgrade cannot change them silently:

- `SendDefaultPii = false` — no IP address, user name or email on any event.
- `MaxRequestBodySize = RequestSize.None` — no request bodies. The game API's form body carries the player's password.

## 4. Configuration

- The website reports to its **own Sentry project, `account`**, separate from Overseer's, so its issues never mix with Overseer's.
- The DSN is read from the `SentryDSN` configuration key:
  - Development: User Secrets. From `MobileGnollHackLogger/`:
    ```bash
    dotnet user-secrets set "SentryDSN" "<dsn>"
    ```
  - Production: the host's configuration.
  - **Never in `appsettings*.json`.**
- An empty or missing `SentryDSN` disables the SDK; the website starts normally.
- The environment comes from the host (`development` / `production`) and the release from the entry assembly (`MobileGnollHackLogger@<version>`).
- The website and Overseer reference the same `Sentry.AspNetCore` version. Upgrade both together, or the shared `GnollHackServer.Data` graph can resolve two `Sentry` core versions.

## 5. Testing

In Development only, `Program.cs` maps a test endpoint:

- `GET /dev/sentry-test` captures a handled exception and answers `Sentry event <id> captured.` An id of 32 zeros means the SDK is disabled, that is, `SentryDSN` was not picked up.
- `GET /dev/sentry-test?throw=true` throws an unhandled exception, shown on the developer exception page and reported to Sentry. Under the Visual Studio debugger the throw first stops as *Exception User-Unhandled*, before any middleware has seen it: press **Continue** (F5) to let the request finish. Stopping the debugger there ends the process before anything is logged, so no event is sent.

Add query values such as `?code=abc123&email=someone%40example.com&page=2` to check the scrubbing: the event in Sentry should show `code=[scrubbed]&email=[scrubbed]&page=2`, in the request and in the "Request starting" breadcrumb alike.

The endpoint does not exist in production.

## 6. How It Differs from Overseer, and Why

- **No unauthenticated-request drop.** Overseer drops every event from an unauthenticated request. On the website the leaderboards and dumplogs are public, and the game client authenticates with credentials in the form body, not with a sign-in cookie, so nearly every request is unauthenticated: that rule would discard almost every event.
- **No AI-provider or tool host suppression, and no confidential-session drop.** The website has neither.
- **No tunnel and no client SDK.** Only the server reports.

The processors share their scrubbing algorithm but not their code: the website cannot reference Overseer.
