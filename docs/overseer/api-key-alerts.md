# API Key Failure Alerts

When a provider tells Overseer that the API key of a **System AI Config** has run out of
credit, or that the key is invalid, expired, revoked or not permitted, Overseer emails the
developers. It sends **at most one email per API key per throttle window** (6 hours by
default), and only for failures a **user** meets.

The code lives in `Overseer/Services/ApiKeyAlerts/`.

## What triggers an alert

An alert needs all three of these:

1. **An operator key.** The run is funded by a System AI Config (`SystemModelId` is set). A
   user's own API key never alerts: it is not the operator's to top up.
2. **A user-facing run.** The run carries an `ApiKeyAlertContext`, which only these set:
   - the main chat (`ChatService.StreamMessageAsync`),
   - its sub-agents (`DelegateToSubAgentTool`, which relabels the context with the agent name),
   - chat-title generation (`ChatService.GenerateTitleAsync`).

   **AI benchmarking never alerts.** No benchmark caller sets the context, and a future
   caller is excluded by default. The admin is usually running the benchmarks and sees the
   errors directly.
3. **A key failure**, as `ApiKeyFailureClassifier` decides from the provider's response.
   Balance rules are checked first, so a 403 that names billing counts as a balance failure.
   Matching is case-insensitive. A custom endpoint (Azure OpenAI, a gateway) uses its
   provider's rules.

| Provider | Out of balance | Rejected key |
|---|---|---|
| Anthropic | status 402; body `billing_error`; status 400 with `credit balance` | status 401; status 403 |
| OpenAI | body `insufficient_quota` (any status, **including 429**); body `billing_hard_limit_reached`; status 402 | status 401; status 403 |
| Google | status 402; status 400/403 with `BILLING_DISABLED` or `billing account` | status 401; status 403; status 400 with `API_KEY_INVALID`, `API key not valid` or `API key expired` |

A provider **stream** error event (no HTTP status) is matched on tokens only: `billing_error`,
`insufficient_quota`, `billing_hard_limit_reached` and `BILLING_DISABLED` mean out of balance;
`authentication_error`, `invalid_api_key` and `API_KEY_INVALID` mean a rejected key.

**Never an alert:** a 429 without a balance token (a rate limit), any 5xx, Anthropic's 529
overload, or a timeout.

A classified failure is not retried, because neither an empty balance nor a rejected key
clears by waiting. OpenAI's `429 insufficient_quota` therefore fails at once instead of
after four rate-limit backoffs. Every classified failure of a System AI Config, benchmark or
not, is also recorded in `SystemAiErrorLogs` as a one-line message; the full redacted
response goes to the email only.

## Known gap: Google

Google answers both per-minute rate limits and depleted quota with `429 RESOURCE_EXHAUSTED`,
and its rate-limit text already says *"check your plan and billing details"*. No token in a
429 body reliably separates the two, and congested Gemini models produce long runs of 429s
on healthy keys (`gemini-service-tier-measurements.md`). So **a Google 429 is never treated
as a balance failure**: a depleted Google balance may surface only as repeated rate-limit
errors in the chat, with no email. A classifier test pins this decision.

To close the gap once a real "credits exhausted" body has been seen:

1. Find the `Google HTTP 429: …` warning that `AgentLoopRunner` logs for every non-success
   provider response, in the server log or in Sentry.
2. Add a token rule for that body to `ApiKeyFailureClassifier`.
3. Replace the pinning case in `ApiKeyFailureClassifierTests` with that body, expecting
   `InsufficientBalance`, and keep a plain rate-limit 429 expecting no match.

## The throttle, and what "per key" means

The throttle is per **key value**, not per configuration. A provider's default key is copied
into every System AI Config that uses it, so one dead default key would otherwise send one
email per configuration.

State is kept in `ApiKeyFailureAlertStates`, one row per key, keyed by `KeyFingerprint`: the
lower-case hex SHA-256 of `overseer-api-key-alert:v1:` plus the key. The table never holds
the key. A replaced key has a new fingerprint and starts a fresh throttle on its own.

`ApiKeyAlertDispatcher`, a background service, handles one report at a time:

1. It counts the occurrence on the key's row.
2. If an email for this key went out within the throttle window of the failure, it stops
   there (logged as *suppressed*).
3. Otherwise it gathers the details and sends the email. On success it stamps
   `LastEmailSentUtc` and resets the counter. On failure it logs an error (Sentry picks it
   up) and does **not** stamp the time, so the next occurrence tries again.

Reporting never blocks the chat: the report goes into a bounded in-memory queue (256) and
the user's error is shown at once. A restart loses at most the queued, unsent reports; the
throttle state itself is in the database, so a restart does not re-send.

## What the email contains

- **Subject:** environment and machine name, provider, *out of balance* or *rejected*, the
  config's display name and id, and whether the key is the provider's **default** key or a
  **custom** key of the config.
- **Summary and what to do:** top up the provider account, or create a new key and save it
  under *Admin → Default API keys* (default key) or in the System AI Config (custom key).
- **Every configuration sharing the key**, found by decrypting each key of the same provider
  only to fingerprint it. A key that fails to decrypt is listed as *key unreadable*.
- **The error:** time of the provider response in UTC and Helsinki time, HTTP status and
  reason, the provider's message, the request target, elapsed time, attempt number, the
  allowlisted response headers (`request-id`, `x-request-id`, `anthropic-organization-id`,
  `openai-organization`, `openai-project`, `retry-after`) and the redacted response body.
- **Occurrences** since the previous email, and the first occurrence of the series.
- **The full non-sensitive System AI Config**, including usage counters and limits; the
  custom endpoint with its query and user info removed; and custom **header names** only.
- **The default key's** hint, update time and verification status, when it applies.
- **Chat context:** session id (or *ephemeral*), the confidential and GnollHack-session
  flags, and the user's id and user name.
- **Diagnostics:** environment, machine, Overseer version, throttle window, generation time.

**Never in the email:** the API key (only its last four characters, as `••••a1B2`), header
values from a configuration, the request URL's query (Google's public endpoint carries the
key there), the user's message, the session title, or any reply text. The key is read only
inside `ApiKeyFailureReport.Create`; the body and URL are redacted with
`ApiKeyValidator.Redact`, which removes every run of 8 or more characters shared with the key.

## Settings

Non-sensitive, in `Overseer/appsettings.json`:

```json
"ApiKeyAlerts": {
  "Enabled": true,
  "RecipientEmail": "gnollhack@hyvanmielenpelit.fi",
  "ThrottleHours": 6,
  "MaxResponseBodyChars": 4000
}
```

- `Enabled`: only an explicit `false` disables alerts; a missing or unreadable value means on.
- `ThrottleHours`: a value of 0 or less, or an unreadable one, means 6.
- `MaxResponseBodyChars`: clamped to 500–20000.

Sending uses the existing Azure Communication Services sender, whose connection string is
`ConnectionStrings:EmailConnection` in User Secrets. While alerts are enabled,
`ConfigHealthService` raises an admin alert if that connection string or the recipient is
missing (`api-key-alert-email-missing`, `api-key-alert-recipient-missing`).

## Testing by hand

`ApiKeyValidator` refuses to save an invalid key, so make a key invalid after saving it:
create a throwaway key at the provider, save it as a **custom** key of a test System AI
Config with the Chat role, revoke it in the provider console, and chat with that model.

To clear the throttle for a repeat test, delete the key's state row:

```sql
DELETE FROM ApiKeyFailureAlertStates WHERE LastSystemAiApiConfigurationId = <config id>;
```

## The removed budget latch

System AI Configs used to carry `IsBudgetExhausted` and `LastBudgetNotificationSentUtc`. The
first budget error latched the flag, after which the config was refused for every user, and
nothing ever cleared it, so a config stayed blocked after a top-up until someone edited the
database. A benchmark that hit a 402 latched it for the chat as well. Both columns were
dropped by the `AddApiKeyFailureAlerts` migration. The provider's own error is now the
authority: after a top-up the config works again at once, and the email throttle keeps the
alert from repeating.
