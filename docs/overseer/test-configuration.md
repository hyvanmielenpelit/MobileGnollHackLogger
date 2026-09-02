# Test Configuration & Secrets Guide

This document describes the configuration requirements, User Secrets schema, setup instructions, and troubleshooting procedures for running live API integration tests in `Overseer.Tests`.

---

## 1. Why User Secrets

Live integration tests in `Overseer.Tests` (tagged with `[Trait("Category", "UsesExternalApi")]`) connect to third-party AI providers (Google AI Studio, OpenAI, Anthropic) to verify API contracts, streaming responses, and service-tier metadata.

These tests require valid API keys and encryption secrets. In accordance with the repository's [`configuration_management`](../../.agents/skills/configuration_management/SKILL.md) rules:
- **Never commit sensitive credentials to source control.**
- **Never store API keys or passwords in `appsettings.json`.**
- Sensitive test credentials must be stored exclusively in the local .NET **User Secrets** store for `Overseer.Tests`, which is located outside the repository on the developer's machine.

> [!CAUTION]
> **Never commit real secret values.** This document contains placeholders only. Do not replace these placeholders with real API keys in any file tracked by Git.

---

## 2. Required Secrets Overview

The following secrets are used by the test suite:

| Key | Consumer | Required? | Purpose |
|---|---|---|---|
| `AI:Provider` | `ChatServiceTests` | **Required** | AI provider under test (`Google`, `OpenAI`, or `Anthropic`). |
| `AI:APIKey` | `ChatServiceTests` | **Required** | API key for the provider specified in `AI:Provider`. |
| `AI:Model` | `ChatServiceTests` | **Required** | Model identifier to test, e.g. `gemini-3.5-flash-lite`. |
| `AI:ThinkingLevel` | `ChatServiceTests` | Optional | Thinking budget/level (e.g. `minimal`, `low`, `medium`, `high`). |
| `AesEncryptionKey` | `ChatServiceTests` | **Required** | 32-byte Base64-encoded AES key used by `CryptoService` to encrypt/decrypt stored test credentials. |
| `AI:ServiceTier:APIKey` | `ServiceTierLiveApiTests` | **Required** | Google AI Studio API key used by live Gemini service-tier contract tests. |
| `AI:ServiceTier:Model` | `ServiceTierLiveApiTests` | Optional | Gemini model identifier. Defaults to `gemini-3.5-flash-lite` if omitted. |
| `AI:LiveTests:AllowedModels` | `LiveApiModelPolicy` | Optional | Comma-separated allow-list of Gemini models permitted in live tests. Defaults to `gemini-3.5-flash-lite`. |
| `AI:AnthropicLatency:APIKey` | Latency measurement harness | Optional | Anthropic API key used to reproduce [`anthropic-model-latency-measurements.md`](anthropic-model-latency-measurements.md). Not read by any test in the suite. |
| `AI:ServiceTier:Provider` | *(None)* | **Unused** | Unused by test code (`ServiceTierLiveApiTests` constructs `GoogleProvider` directly). Do not set. |

---

## 3. User Secrets Schema

The complete `secrets.json` schema with placeholders:

```json
{
  "AI:Provider": "Google",
  "AI:APIKey": "<api-key-for-AI:Provider>",
  "AI:Model": "gemini-3.5-flash-lite",
  "AI:ThinkingLevel": "minimal",
  "AesEncryptionKey": "<32-byte-base64-aes-key>",
  "AI:ServiceTier:APIKey": "<google-ai-studio-api-key>",
  "AI:ServiceTier:Model": "gemini-3.5-flash-lite",
  "AI:LiveTests:AllowedModels": "gemini-3.5-flash-lite",
  "AI:AnthropicLatency:APIKey": "<anthropic-api-key>"
}
```

### Important Details
1. **File Location**: On Windows, the file is located at `%APPDATA%\Microsoft\UserSecrets\1ebd8f56-ca58-4fae-813c-a88c0ee98cd8\secrets.json` (derived from the `UserSecretsId` in `Overseer.Tests.csproj`).
2. **Key Format**: Flat colon-delimited keys (e.g. `"AI:APIKey"`) and nested JSON objects (`{ "AI": { "APIKey": "..." } }`) are parsed identically by .NET `IConfiguration`. Running `dotnet user-secrets set` flattens the file structure automatically.
3. **Defaults**: `AI:ThinkingLevel` and `AI:ServiceTier:Model` are optional. If `AI:ServiceTier:Model` is omitted, tests automatically default to `gemini-3.5-flash-lite`.

---

## 4. Setting Up Secrets

You can configure User Secrets via the .NET CLI from the solution root:

```powershell
# Required for ChatServiceTests
dotnet user-secrets set "AI:Provider" "Google" --project Overseer.Tests
dotnet user-secrets set "AI:APIKey" "<your-google-or-provider-api-key>" --project Overseer.Tests
dotnet user-secrets set "AI:Model" "gemini-3.5-flash-lite" --project Overseer.Tests
dotnet user-secrets set "AesEncryptionKey" "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=" --project Overseer.Tests

# Required for ServiceTierLiveApiTests
dotnet user-secrets set "AI:ServiceTier:APIKey" "<your-google-ai-studio-api-key>" --project Overseer.Tests
dotnet user-secrets set "AI:ServiceTier:Model" "gemini-3.5-flash-lite" --project Overseer.Tests

# Optional: reproducing the Anthropic latency measurements (not used by any test)
dotnet user-secrets set "AI:AnthropicLatency:APIKey" "<your-anthropic-api-key>" --project Overseer.Tests
```

Alternatively, in Visual Studio:
1. Right-click the **`Overseer.Tests`** project in Solution Explorer.
2. Select **Manage User Secrets**.
3. Paste the schema from Section 3 above with your credentials.

---

## 5. Finding or Replacing a Model

### Google Gemini

If a configured Gemini model is retired by Google or returns `404 NOT_FOUND`, query Google's Model Service using your configured API key to find available models:

```powershell
# Extract API key safely (splitting on first '=' only)
$k = ((dotnet user-secrets list --project Overseer.Tests | Select-String "AI:ServiceTier:APIKey") -split '=', 2)[1].Trim()

# Query available models and filter on generateContent
((curl.exe -sS "https://generativelanguage.googleapis.com/v1beta/models" -H "x-goog-api-key: $k") | ConvertFrom-Json).models |
    Where-Object { $_.supportedGenerationMethods -contains "generateContent" } |
    Select-Object -ExpandProperty name
```

> [!NOTE]
> **Always filter on `generateContent`, never on `streamGenerateContent`.** Google's API metadata does not report `streamGenerateContent` in `supportedGenerationMethods` even for models that support streaming. Filtering on `streamGenerateContent` will return an empty list.

After choosing a replacement model ID (e.g. `gemini-3.5-flash-lite`), strip any `models/` prefix and update your secrets:

```powershell
dotnet user-secrets set "AI:ServiceTier:Model" "<model-id>" --project Overseer.Tests
```

If the replacement model is not in the default allow-list, add it to `AI:LiveTests:AllowedModels`:

```powershell
dotnet user-secrets set "AI:LiveTests:AllowedModels" "gemini-3.5-flash-lite,<model-id>" --project Overseer.Tests
```

### Anthropic Claude

Anthropic model ids are **exact strings with no date suffix** -- `claude-sonnet-5`, not
`claude-sonnet-5-20260630`. A date-suffixed id returns `404 not_found_error`. Anthropic also
authenticates with the `x-api-key` **header**, not a `?key=` query string as Google does:

```powershell
$k = ((dotnet user-secrets list --project Overseer.Tests | Select-String "AI:AnthropicLatency:APIKey") -split '=', 2)[1].Trim()
((curl.exe -sS "https://api.anthropic.com/v1/models?limit=100" -H "x-api-key: $k" -H "anthropic-version: 2023-06-01") | ConvertFrom-Json).data | Select-Object id, display_name, created_at
```

For measured latency and availability per Claude model, and which one to prefer in live
tests, see [`anthropic-model-latency-measurements.md`](anthropic-model-latency-measurements.md).

---

## 6. Troubleshooting

| Symptom / Error Message | Cause | Resolution |
|---|---|---|
| `... is not configured. These User Secrets are required but missing or empty (N):` | Required User Secrets have not been populated in the `Overseer.Tests` secrets store. | Run the `dotnet user-secrets set` commands listed in the error report to supply the missing keys. |
| `The Gemini model '<model>' (from ...) is not available. Google returned HTTP 404 NOT_FOUND:` | The configured model ID is nonexistent or has been retired by Google. | Follow [Finding a Replacement Model](#5-finding-a-replacement-model) to locate an active model and update `AI:ServiceTier:Model` or `AI:Model`. |
| `The Google API key in ... was rejected. Google returned HTTP 400:` | The provided API key is invalid, revoked, or mistyped. | Generate a valid API key at [Google AI Studio](https://aistudio.google.com/apikey) and update the secret via `dotnet user-secrets set`. |
| `Permission denied for Google API key in ... Google returned HTTP 403 PERMISSION_DENIED:` | The API key is valid but lacks permissions for the requested model or project. | Verify project permissions in Google Cloud Console or AI Studio, or create a new key. |
| `Model '<model>' is not in the live-test allow-list [...]` (Test Skipped) | The configured model is too slow for normal test execution (`gemini-3.6-flash`, `gemini-3.7-flash`). | Use `gemini-3.5-flash-lite` for tests, or override the allow-list via `AI:LiveTests:AllowedModels`. |
| `SKIPPED ASSERTIONS: Google returned 429 / 503` (Test Skipped) | Google Gemini API capacity congestion or rate limiting. | This is an external provider capacity condition, not a software bug. Tests automatically tolerate this by skipping assertions. |
| Anthropic `401 authentication_error` | The Anthropic API key is invalid, revoked, or mistyped. | Issue a new key in the Anthropic Console and update `AI:AnthropicLatency:APIKey`. |
| Anthropic `404 not_found_error` | The Claude model id does not exist -- most often a date suffix appended to a current id (`claude-sonnet-5-20260630`). | Use the exact id with no date suffix. List available ids with the command in [section 5](#anthropic-claude). |
| Anthropic `529 overloaded_error` | Anthropic capacity congestion. | An external provider condition, not a bug. Treat it exactly like Gemini 429/503: log a warning and pass. |
| Anthropic `400 invalid_request_error` naming data retention, on the `claude-fable-*` models only | Claude Fable 5 is unavailable to organisations configured for zero data retention. `claude-fable-5-1` shares the Fable deployment and is *expected* to inherit the restriction -- this has not been verified against a ZDR-configured organisation. | An account configuration fact, not a code defect. Use another model, or change the org retention setting. |

---

## 7. Running the Tests

### Default Test Run (Offline / No Quota)
By default, all live external API tests are excluded to ensure fast and hermetic test runs:

```powershell
dotnet test MobileGnollHackLogger.slnx --filter "Category!=UsesExternalApi"
```

### Live API Test Run (Requires Secrets & Permission)
Per [`testing_guidelines`](../../.agents/skills/testing_guidelines/SKILL.md) §1, AI agents must **always request explicit user permission** before executing tests that connect to external AI APIs:

```powershell
dotnet test Overseer.Tests\Overseer.Tests.csproj --filter "Category=UsesExternalApi"
```
