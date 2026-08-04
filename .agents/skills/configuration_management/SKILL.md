---
name: configuration_management
description: Guidelines for managing configuration settings between appsettings.json and User Secrets in MobileGnollHackLogger and Overseer projects.
---

# Configuration Management Rules

In the MobileGnollHackLogger, Overseer, and Overseer.Test projects, configuration settings are strictly divided between `appsettings.json` and User Secrets. 

## The Rules
1. **Never write sensitive data to `appsettings.json`**. This includes API keys, database connection strings with credentials, external service tokens, passwords, and any other secrets. This file is committed to source control and must remain safe for public viewing.
2. **Never write non-sensitive data to User Secrets**. User Secrets are only for development environment overrides and sensitive information. General application configuration, default settings, feature toggles, and non-sensitive structural configuration belong in `appsettings.json`.

## Best Practices for Reading and Writing Configuration

### appsettings.json
- **Writing**: Add non-sensitive configuration sections and keys directly to `appsettings.json` or environment-specific files like `appsettings.Development.json` (as long as they don't contain secrets).
- **Reading**: Use standard ASP.NET Core `IConfiguration` injection to read values. 

### User Secrets
User Secrets are stored outside the project tree to prevent accidental commits.
- **Writing**: Use the .NET CLI to set user secrets. Make sure you are in the directory of the specific project (e.g., MobileGnollHackLogger, Overseer, or Overseer.Test) before running these commands.
  - To set a secret: `dotnet user-secrets set "Section:Key" "Value"`
  - To list secrets: `dotnet user-secrets list`
  - To remove a secret: `dotnet user-secrets remove "Section:Key"`
- **Reading**: `IConfiguration` automatically loads User Secrets in the Development environment. Access them using the standard configuration patterns (e.g., `_configuration["Section:Key"]`).

**Remember**: When setting up a new service or dependency that requires authentication or API keys, always instruct the user or use the `dotnet user-secrets set` command to configure the secret, and only add the empty/non-sensitive structural placeholder to `appsettings.json` if absolutely necessary for binding.
