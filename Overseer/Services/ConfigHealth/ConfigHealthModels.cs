using Overseer.Services.SourceIndexing;

namespace Overseer.Services.ConfigHealth;

/// <summary>The status strings a configuration health check reports.</summary>
public static class ConfigHealthStatus
{
    public const string Ok = "ok";
    public const string Warning = "warning";
    public const string Error = "error";
    public const string Info = "info";
}

/// <summary>The Configuration Health report: every catalog check and the loaded configuration sources.</summary>
public sealed class ConfigHealthReportDto
{
    public DateTime GeneratedAtUtc { get; init; }

    /// <summary>The host environment's name, or null when the service has no host environment.</summary>
    public string? EnvironmentName { get; init; }

    /// <summary>True in Development, where ASP.NET Core loads User Secrets.</summary>
    public bool UserSecretsExpected { get; init; }

    public IReadOnlyList<ConfigSourceDto> Sources { get; init; } = Array.Empty<ConfigSourceDto>();

    public ConfigHealthSummaryDto Summary { get; init; } = new();

    public IReadOnlyList<ConfigHealthCheckDto> Checks { get; init; } = Array.Empty<ConfigHealthCheckDto>();
}

/// <summary>One loaded configuration provider. Only the number of keys it holds is reported, never a value.</summary>
public sealed class ConfigSourceDto
{
    public required string Name { get; init; }

    /// <summary><c>appsettings</c>, <c>userSecrets</c>, <c>jsonFile</c>, <c>environmentVariables</c>, <c>commandLine</c>, <c>memory</c> or <c>other</c>.</summary>
    public required string Kind { get; init; }

    /// <summary>Whether the provider's file exists; null for a provider that reads no file.</summary>
    public bool? FileExists { get; init; }

    public int KeyCount { get; init; }
}

/// <summary>The number of checks with each status.</summary>
public sealed class ConfigHealthSummaryDto
{
    public int Ok { get; init; }
    public int Warning { get; init; }
    public int Error { get; init; }
    public int Info { get; init; }
}

/// <summary>One configuration health check.</summary>
public sealed class ConfigHealthCheckDto
{
    public required string Id { get; init; }
    public required string Category { get; init; }
    public required string Title { get; init; }
    public required IReadOnlyList<string> Keys { get; init; }

    /// <summary>One of the <see cref="ConfigHealthStatus"/> strings.</summary>
    public required string Status { get; init; }

    /// <summary>The provider the keys' values come from, <c>Not set</c>, or null for a check that reads no key.</summary>
    public string? Source { get; init; }

    /// <summary>The configured value of a non-sensitive setting; always null for a sensitive one.</summary>
    public string? Value { get; init; }

    public required string Summary { get; init; }

    /// <summary>What to change; null when the check is OK.</summary>
    public string? Remedy { get; init; }
}

/// <summary>What the GnollHack source service reports about makedefs, for the health report and the system alerts.</summary>
public sealed record MakedefsHealthState(
    MakedefsOutcomeKind? LastKind,
    string? LastMessage,
    DateTime? FinishedAtUtc,
    IReadOnlyList<string> MissingHeaders);

/// <summary>How a catalog check evaluates its first key.</summary>
public enum ConfigCheckKind
{
    Directory,
    File,
    GitRepository,
    Secret,
    Required,
    Command,
    Range,
    Custom
}
