---
name: configuration_management
description: Guidelines for managing configuration settings between appsettings.json and User Secrets in MobileGnollHackLogger and Overseer projects, including handling missing settings via ConfigHealthService, AdminAlertService, and admin-alerts component.
---

# Configuration Management Rules

In the MobileGnollHackLogger, Overseer, and Overseer.Tests projects, configuration settings are strictly divided between `appsettings.json` and User Secrets, and missing configurations are reported via administrative alerts.

## The Rules
1. **Never write sensitive data to `appsettings.json`**. This includes API keys, database connection strings with credentials, external service tokens, passwords, and any other secrets. This file is committed to source control and must remain safe for public viewing.
2. **Never write non-sensitive data to User Secrets**. User Secrets are only for development environment overrides and sensitive information. General application configuration, default settings, feature toggles, and non-sensitive structural configuration belong in `appsettings.json`.
3. **Never use hard-coded fallback paths or values for unconfigured settings in backend services**. If a configuration setting (such as a database path, data directory, API URL, or token) is not configured, do NOT fall back to arbitrary local drive paths (e.g. `c:\wiki`, `c:\hmp\nethackwiki`, `c:\data`). Instead:
   - Allow the setting to default to `string.Empty` or `null`.
   - Check `string.IsNullOrWhiteSpace(value)`.
   - Log a warning with `_logger.LogWarning(...)` and exit/no-op gracefully without crashing or throwing.
   - Register a missing configuration alert in `ConfigHealthService`.

## Missing Configuration Handling & Admin Alerts

When a required or recommended configuration setting is missing, null, empty, or whitespace, the standard architecture across the Overseer project notifies administrators in the UI via the `ConfigHealthService` → `AdminAlertService` → `AdminAlertsComponent` pipeline.

### 1. Backend Service Implementation Pattern
When reading configuration in backend services:
```csharp
public class MyDataService
{
    private readonly string _dataPath;
    private readonly ILogger<MyDataService>? _logger;

    public MyDataService(IConfiguration configuration, ILogger<MyDataService>? logger = null)
    {
        _logger = logger;
        _dataPath = configuration["MyDataPath"] ?? string.Empty;

        Initialize();
    }

    private void Initialize()
    {
        if (string.IsNullOrWhiteSpace(_dataPath))
        {
            _logger?.LogWarning("MyData directory not configured (MyDataPath is empty).");
            return;
        }

        if (!Directory.Exists(_dataPath))
        {
            _logger?.LogWarning("MyData directory not found: {Path}", _dataPath);
            return;
        }

        // Proceed with loading / indexing...
    }
}
```

### 2. Registering Alerts in `ConfigHealthService`
Located at `Overseer/Services/ConfigHealthService.cs`:
Add a check in `GetSystemAlertsAsync()` (asynchronous: a database read there is awaited with `ToListAsync(ct)`, never `.ToList()` or `.GetAwaiter().GetResult()`):
```csharp
if (string.IsNullOrWhiteSpace(_configuration["MyDataPath"]))
{
    alerts.Add(new SystemAlert
    {
        Id = "my-data-path-missing",
        Type = "warning", // "warning" or "error"
        Message = "MyData path is not configured. Set MyDataPath in configuration settings."
    });
}
```

#### Alerts with a link
`SystemAlert` (`Overseer/Models/SystemAlert.cs`) has two optional properties, `LinkUrl` (an in-app route) and `LinkText`, both null by default. Set them together when the alert has a place to fix it; `app-admin-alerts` renders them as a call-to-action link under the message; following the link dismisses the alert, so the link is for an alert whose place to fix it *is* the review, since the alert is hidden on arrival. An alert without them is unchanged. An Admin link uses the path form (`/admin/configs`, `/admin/users/deletions`, `/admin/services`; the scheme is in `docs/overseer/admin-page-urls.md`), never `/admin?tab=…`.

#### GnollHack source alerts
`source-code-path-missing` (`warning`) is raised while `SourceCodePath` is not set, the counterpart of `nethack-source-code-path-missing`. `gnollhack-makedefs-failed` (`error`) is raised when the last makedefs outcome of `SourceCodeService` is `BuildFailed` or `RunFailed` **and** a generated header (`pm.h`, `onames.h`, `animoff.h`, `animtotals.h`) is missing from `include/`; it links to `/admin/services` with the text *Open GnollHack Source*. A failed build over headers still on disk raises nothing, since the index still holds the constants.

#### Retired-model alerts
`GetSystemAlertsAsync()` also raises one `warning` per **retired model** (an entry in `Overseer/Services/ModelCatalogs/RetiredModels.json`) that at least one **enabled** system configuration still uses, classified by `ModelAvailabilityService.Evaluate`. Its id is `retired-model-{provider}-{firstPrefix}`, its message names the number of configurations, the model and its retirement date, and it links to `/admin/configs` with the text *Review in System Configs*. A configuration in custom mode or on a custom endpoint does not count, and a model that is merely *not in the catalog* raises no alert. The alert clears once every such configuration is switched, kept as a custom model, disabled or deleted. See `docs/overseer/adding-ai-models.md` § *Retiring a Model*.

### 3. API & Frontend Alert Pipeline
- **API Endpoint**: `AdminController.GetSystemAlerts([FromServices] ConfigHealthService configHealthService, CancellationToken ct)` at `/api/admin/system-alerts` returns the active list of `SystemAlert` items.
- **Frontend Service**: `AdminAlertService` (`Overseer/ClientApp/src/app/services/admin-alert.service.ts`) fetches system alerts on each `user$` emission (sign-in, and every confirmed `checkAuth()`), and on `refresh()`, which `AdminAlertsComponent` calls each time the chat view is entered from another route; it publishes them through two streams: `allAlerts$`, every alert the server reports, dismissed ones included, for code that needs to know a condition (Model Comparison reads it for its chart-storage check); and `alerts$`, the alerts the signed-in user has not dismissed, for code that shows them.
- **Dismissals persist per user until the alert's condition clears.** They are kept in `localStorage` under `overseer.adminAlerts.dismissed.<user>` (the lowercased user name, since the client has no user id) as `{ version: 1, ids }`. An id is dropped as soon as the server stops reporting that alert, so a condition that clears and later returns shows its alert again. Following an alert's link dismisses it the same way as its ✕ button. The older per-tab `sessionStorage` key `dismissed_admin_alerts` is merged into the record once and removed. Where storage is blocked or unreadable, a dismissal lasts only as long as the page, and the alerts show again on the next load.
- **UI Component**: `AdminAlertsComponent` (`Overseer/ClientApp/src/app/chat/admin-alerts.component.ts` / `admin-alerts.component.html`) renders alerts as floating popovers in the main chat window for administrators, allowing them to see missing configurations and dismiss warnings. The popover is hidden while a modal dialog is open, since one shown above a modal dialog is inert, and shown again when the dialog closes.

### 4. Configuration Health Report (Admin → Services → Configuration Health)
Alerts name only what needs action now. The full picture is `ConfigHealthService.GetHealthReport()`, served at `GET /api/admin/devtools/config-health` (`AdminDevToolsController`, `AdminOnly`) and shown on the **Configuration Health** sub-tab of the Services tab (`/admin/services/config-health`).
- **Declared checks**: `Overseer/Services/ConfigHealth/ConfigHealthCatalog.cs` holds the table — id, category, title, keys, kind (`Directory`, `File`, `GitRepository`, `Secret`, `Required`, `Command`, `Range`, `Custom`), sensitivity, the severity when missing or absent, and the remedy. Categories: *Tool corpora*, *GnollHack source build*, *Storage paths*, *Database and email*, *Security and privacy*, *Monitoring*, *Secret placement*, *Limits*. The *Secret placement* checks (one per sensitive key) and the *Limits* checks (one per `AiPerformanceSettings` entry with `Min` / `Default` / `Max`, which must parse and satisfy Min ≤ Default ≤ Max) are generated from the configuration's own keys. A new setting that needs a health check gets a row in the catalog.
- **Where a value comes from**: `ConfigurationSourceInspector` walks the configuration providers last to first and names the one that supplies a key — `appsettings.json`, `appsettings.{Environment}.json`, `User Secrets`, `Environment variables`, `Command line` — or *Not set*. The report also lists every loaded source with its key **count**. In Development a missing User Secrets file is a warning.
- **Rule 1 is enforced as an error.** Every sensitive key — `ConnectionStrings:*`, `AesEncryptionKey`, `AI:APIKey` and every `AI:{provider}:APIKey`, `GitHub:PersonalAccessToken`, `PrivacySettings:KeyRing`, `PromptCacheSettings:PromptCacheKeySalt`, `SentryDSN`, `AntiForgeryToken` (`ConfigHealthCatalog.SensitiveKeys` and `IsSensitive`) — is an **error** when any `appsettings*.json` holds a non-empty value for it, even when User Secrets override it: the committed file still carries the secret. The remedy names the `dotnet user-secrets set` command. The one exception is a key's public built-in default (`ConfigHealthCatalog.PublicDefaults`: the shipped `PromptCacheKeySalt`, `ChatService.DefaultPromptCacheKeySalt`), which is reported as **info**, since it hides nothing; any other value in `appsettings*.json` is still an error.
- **Sensitive values never leave the server.** For a sensitive key the report carries only the status, the source and a summary; `value` is always null, and the UI shows *Value hidden*. Non-sensitive values (paths, the makedefs command, limits) are shown. A new sensitive key goes into `SensitiveKeys`, or it would be reported with its value.
- The report is computed per request, with no cache and no database query.

## Best Practices for Reading and Writing Configuration

### appsettings.json
- **Writing**: Add non-sensitive configuration sections and keys directly to `appsettings.json` or environment-specific files like `appsettings.Development.json` (as long as they don't contain secrets).
- **Reading**: Use standard ASP.NET Core `IConfiguration` injection to read values. 

### User Secrets
User Secrets are stored outside the project tree to prevent accidental commits.
- **Writing**: Use the .NET CLI to set user secrets. Make sure you are in the directory of the specific project (e.g., MobileGnollHackLogger, Overseer, or Overseer.Tests) before running these commands.
  - To set a secret: `dotnet user-secrets set "Section:Key" "Value"`
  - To list secrets: `dotnet user-secrets list`
  - To remove a secret: `dotnet user-secrets remove "Section:Key"`
- **Reading**: `IConfiguration` automatically loads User Secrets in the Development environment. Access them using the standard configuration patterns (e.g., `_configuration["Section:Key"]`).

### Test Configuration (`Overseer.Tests`)
For `Overseer.Tests`, all live AI credentials and encryption keys are stored exclusively in User Secrets and **never** in `appsettings.json`. For the authoritative schema, required keys list, setup commands, and troubleshooting guide, see **[`docs/overseer/test-configuration.md`](../../../docs/overseer/test-configuration.md)**.

**Remember**: When setting up a new service or dependency that requires authentication or API keys, always instruct the user or use the `dotnet user-secrets set` command to configure the secret, and only add the empty/non-sensitive structural placeholder to `appsettings.json` if absolutely necessary for binding.
