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
Add a check in `GetSystemAlerts()`:
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

### 3. API & Frontend Alert Pipeline
- **API Endpoint**: `AdminController.GetSystemAlerts([FromServices] ConfigHealthService configHealthService)` at `/api/admin/system-alerts` returns the active list of `SystemAlert` items.
- **Frontend Service**: `AdminAlertService` (`Overseer/ClientApp/src/app/services/admin-alert.service.ts`) fetches system alerts periodically and on navigation events, publishing them through `alerts$`.
- **UI Component**: `AdminAlertsComponent` (`Overseer/ClientApp/src/app/chat/admin-alerts.component.ts` / `admin-alerts.component.html`) renders alerts as floating popovers in the main chat window for administrators, allowing them to see missing configurations and dismiss warnings.

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

**Remember**: When setting up a new service or dependency that requires authentication or API keys, always instruct the user or use the `dotnet user-secrets set` command to configure the secret, and only add the empty/non-sensitive structural placeholder to `appsettings.json` if absolutely necessary for binding.
