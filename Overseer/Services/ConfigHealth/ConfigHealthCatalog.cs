using System.Globalization;
using Overseer.Services.ApiKeyAlerts;
using Overseer.Services.Benchmarking;
using Overseer.Services.Privacy;
using Overseer.Services.SourceIndexing;

namespace Overseer.Services.ConfigHealth;

/// <summary>What one catalog check reads and how a missing or absent value is graded.</summary>
internal sealed record ConfigHealthCheckDefinition
{
    public required string Id { get; init; }
    public required string Category { get; init; }
    public required string Title { get; init; }
    public required IReadOnlyList<string> Keys { get; init; }
    public required ConfigCheckKind Kind { get; init; }
    public bool Sensitive { get; init; }

    /// <summary>The status when the first key is not set.</summary>
    public string SeverityWhenMissing { get; init; } = ConfigHealthStatus.Warning;

    /// <summary>The status when a configured path does not exist.</summary>
    public string SeverityWhenAbsent { get; init; } = ConfigHealthStatus.Error;

    public required string Remedy { get; init; }

    /// <summary>The summary when the first key is not set; "Not set." when null.</summary>
    public string? MissingSummary { get; init; }

    /// <summary>Subdirectories a Directory or GitRepository check requires.</summary>
    public IReadOnlyList<string> RequiredSubdirectories { get; init; } = Array.Empty<string>();

    /// <summary>The default a Range check's code uses when the key is not set.</summary>
    public int? DefaultValue { get; init; }

    /// <summary>The evaluation of a Custom check.</summary>
    public Func<ConfigHealthContext, ConfigHealthCheckDefinition, ConfigHealthOutcome>? Evaluate { get; init; }
}

/// <summary>What the checks read: the configuration, its sources, and the state of services they ask about.</summary>
internal sealed class ConfigHealthContext
{
    public required IConfiguration Configuration { get; init; }
    public required ConfigurationSourceInspector Sources { get; init; }

    /// <summary>Null when the GnollHack source service could not be resolved.</summary>
    public MakedefsHealthState? Makedefs { get; init; }

    /// <summary>Null when the keyring could not be evaluated.</summary>
    public ConfigurationContentKeyRing? KeyRing { get; init; }

    /// <summary>Null when no Terms document reader is registered.</summary>
    public GnollHackServer.Data.Legal.LegalDocumentReader? LegalDocuments { get; init; }

    /// <summary>The trimmed value of <paramref name="key"/>, or an empty string.</summary>
    public string Read(string key) => Configuration[key]?.Trim() ?? string.Empty;
}

internal readonly record struct ConfigHealthOutcome(string Status, string Summary, string? Value = null);

/// <summary>
/// The declared Configuration Health checks, grouped by category, and their evaluation. Secret placement
/// and AiPerformanceSettings limit checks are generated from the configuration's own keys.
/// </summary>
internal static class ConfigHealthCatalog
{
    public const string ToolCorpora = "Tool corpora";
    public const string SourceBuild = "GnollHack source build";
    public const string StoragePaths = "Storage paths";
    public const string DatabaseAndEmail = "Database and email";
    public const string SecurityAndPrivacy = "Security and privacy";
    public const string Monitoring = "Monitoring";
    public const string SecretPlacement = "Secret placement";
    public const string Limits = "Limits";

    private const string LimitsSection = "AiPerformanceSettings";

    /// <summary>Keys whose values never leave the server. <c>ConnectionStrings:*</c> and <c>AI:*:APIKey</c> are added from the configuration.</summary>
    public static readonly IReadOnlyList<string> SensitiveKeys = new[]
    {
        "ConnectionStrings:SqlDatabaseConnection",
        "ConnectionStrings:EmailConnection",
        "AesEncryptionKey",
        "AI:APIKey",
        "GitHub:PersonalAccessToken",
        "PrivacySettings:KeyRing",
        "PromptCacheSettings:PromptCacheKeySalt",
        "SentryDSN",
        "AntiForgeryToken"
    };

    /// <summary>Sensitive keys whose built-in default is public; that value in <c>appsettings*.json</c> is not a misplaced secret.</summary>
    public static readonly IReadOnlyDictionary<string, string> PublicDefaults =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["PromptCacheSettings:PromptCacheKeySalt"] = ChatService.DefaultPromptCacheKeySalt
        };

    /// <summary>Whether <paramref name="key"/> is, or lies under, a sensitive key.</summary>
    public static bool IsSensitive(string key)
    {
        if (key.StartsWith("ConnectionStrings:", StringComparison.OrdinalIgnoreCase)
            || key.EndsWith(":APIKey", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return SensitiveKeys.Any(s => string.Equals(key, s, StringComparison.OrdinalIgnoreCase)
            || key.StartsWith(s + ":", StringComparison.OrdinalIgnoreCase));
    }

    public static readonly IReadOnlyList<ConfigHealthCheckDefinition> Definitions = new[]
    {
        // Tool corpora
        new ConfigHealthCheckDefinition
        {
            Id = "source-code-path", Category = ToolCorpora, Title = "GnollHack source code",
            Keys = new[] { "SourceCodePath" }, Kind = ConfigCheckKind.GitRepository,
            RequiredSubdirectories = new[] { "src", "include" },
            MissingSummary = "Not set. The GnollHack source tools have nothing to search.",
            Remedy = "Set SourceCodePath to a clone of the GnollHack repository."
        },
        new ConfigHealthCheckDefinition
        {
            Id = "nethack-source-code-path", Category = ToolCorpora, Title = "NetHack source code",
            Keys = new[] { "NetHackSourceCodePath" }, Kind = ConfigCheckKind.Directory,
            RequiredSubdirectories = new[] { "src", "include" },
            MissingSummary = "Not set. The NetHack source tools have nothing to search.",
            Remedy = "Set NetHackSourceCodePath to a clone of the NetHack repository."
        },
        new ConfigHealthCheckDefinition
        {
            Id = "wiki-path", Category = ToolCorpora, Title = "GnollHack wiki",
            Keys = new[] { "WikiPath" }, Kind = ConfigCheckKind.Directory,
            Remedy = "Set WikiPath to a clone of the GnollHack wiki."
        },
        new ConfigHealthCheckDefinition
        {
            Id = "nethack-wiki-path", Category = ToolCorpora, Title = "NetHack wiki",
            Keys = new[] { "NetHackWikiPath" }, Kind = ConfigCheckKind.Directory,
            Remedy = "Set NetHackWikiPath to the folder of NetHack wiki Markdown files."
        },
        new ConfigHealthCheckDefinition
        {
            Id = "kb-path", Category = ToolCorpora, Title = "Knowledge base",
            Keys = new[] { "KbPath" }, Kind = ConfigCheckKind.Directory,
            RequiredSubdirectories = new[] { "Content" },
            Remedy = "Set KbPath to the knowledge base repository, which holds its articles under Content."
        },
        new ConfigHealthCheckDefinition
        {
            Id = "max-source-file-size", Category = ToolCorpora, Title = "Largest indexed source file (KB)",
            Keys = new[] { "MaxSourceFileSizeKB" }, Kind = ConfigCheckKind.Range, DefaultValue = 800,
            Remedy = "Set MaxSourceFileSizeKB to a positive whole number, or remove it."
        },
        new ConfigHealthCheckDefinition
        {
            Id = "max-wiki-file-size", Category = ToolCorpora, Title = "Largest indexed wiki file (KB)",
            Keys = new[] { "MaxWikiFileSizeKB" }, Kind = ConfigCheckKind.Range, DefaultValue = 100,
            Remedy = "Set MaxWikiFileSizeKB to a positive whole number, or remove it."
        },
        new ConfigHealthCheckDefinition
        {
            Id = "max-nethack-wiki-file-size", Category = ToolCorpora, Title = "Largest indexed NetHack wiki file (KB)",
            Keys = new[] { "MaxNetHackWikiFileSizeKB" }, Kind = ConfigCheckKind.Range, DefaultValue = 500,
            Remedy = "Set MaxNetHackWikiFileSizeKB to a positive whole number, or remove it."
        },

        // GnollHack source build
        new ConfigHealthCheckDefinition
        {
            Id = "makedefs-build-command", Category = SourceBuild, Title = "makedefs build command",
            Keys = new[] { "MakedefsBuildCommand" }, Kind = ConfigCheckKind.Command,
            MissingSummary = "Not set. makedefs is not built; headers are generated only by an executable already on disk.",
            Remedy = "Set MakedefsBuildCommand to the MSBuild command line that builds makedefs.vcxproj, with the full path of MSBuild.exe."
        },
        new ConfigHealthCheckDefinition
        {
            Id = "makedefs-executable-path", Category = SourceBuild, Title = "makedefs executable",
            Keys = new[] { "MakedefsExecutablePath" }, Kind = ConfigCheckKind.Custom,
            SeverityWhenAbsent = ConfigHealthStatus.Warning,
            Remedy = "Leave MakedefsExecutablePath empty to use tools\\Release\\x64\\makedefs.exe under SourceCodePath, or set it to the executable the build command produces.",
            Evaluate = EvaluateMakedefsExecutable
        },
        new ConfigHealthCheckDefinition
        {
            Id = "makedefs-build-timeout", Category = SourceBuild, Title = "makedefs build timeout (seconds)",
            Keys = new[] { "MakedefsBuildTimeoutSeconds" }, Kind = ConfigCheckKind.Range, DefaultValue = 300,
            Remedy = "Set MakedefsBuildTimeoutSeconds to a positive whole number, or remove it."
        },
        new ConfigHealthCheckDefinition
        {
            Id = "makedefs-run-timeout", Category = SourceBuild, Title = "makedefs run timeout (seconds)",
            Keys = new[] { "MakedefsRunTimeoutSeconds" }, Kind = ConfigCheckKind.Range, DefaultValue = 60,
            Remedy = "Set MakedefsRunTimeoutSeconds to a positive whole number, or remove it."
        },
        new ConfigHealthCheckDefinition
        {
            Id = "makedefs-branch", Category = SourceBuild, Title = "makedefs branch restriction",
            Keys = new[] { "MakedefsBranch" }, Kind = ConfigCheckKind.Custom,
            Remedy = "Set MakedefsBranch to restrict makedefs to one branch, or leave it empty.",
            Evaluate = EvaluateMakedefsBranch
        },
        new ConfigHealthCheckDefinition
        {
            Id = "makedefs-last-outcome", Category = SourceBuild, Title = "Last makedefs outcome",
            Keys = Array.Empty<string>(), Kind = ConfigCheckKind.Custom,
            Remedy = "Open GnollHack Source in Developer Tools to see the build output, fix the cause, and build makedefs again.",
            Evaluate = EvaluateMakedefsOutcome
        },

        // Storage paths
        new ConfigHealthCheckDefinition
        {
            Id = "dumplog-path", Category = StoragePaths, Title = "Dumplog store",
            Keys = new[] { "DumpLogPath" }, Kind = ConfigCheckKind.Directory,
            MissingSummary = "Not set. Dumplog search finds nothing, and account deletions held for review cannot be approved.",
            Remedy = "Set DumpLogPath to the same folder the website writes dumplogs to."
        },
        new ConfigHealthCheckDefinition
        {
            Id = "conversations-data-location", Category = StoragePaths, Title = "Conversation files",
            Keys = new[] { "ConversationsDataLocation" }, Kind = ConfigCheckKind.Directory,
            SeverityWhenAbsent = ConfigHealthStatus.Warning,
            MissingSummary = "Not set. Attachments are not stored, and account deletions held for review cannot be approved.",
            Remedy = "Set ConversationsDataLocation to the same folder the website uses."
        },
        new ConfigHealthCheckDefinition
        {
            Id = "data-protection-keys-path", Category = StoragePaths, Title = "Data Protection keys",
            Keys = new[] { "PrivacySettings:DataProtectionKeysPath" }, Kind = ConfigCheckKind.Directory,
            SeverityWhenAbsent = ConfigHealthStatus.Warning,
            MissingSummary = "Not set. The framework default stands, which can sign every user out on a restart.",
            Remedy = "Set PrivacySettings:DataProtectionKeysPath to a folder outside the application directory."
        },
        new ConfigHealthCheckDefinition
        {
            Id = "report-charts-location", Category = StoragePaths, Title = "Report chart storage",
            Keys = new[] { BenchmarkReportChartStore.ConfigurationKey }, Kind = ConfigCheckKind.Custom,
            Remedy = "Set Benchmark:ReportPack:ChartsDataLocation to an absolute folder.",
            Evaluate = EvaluateChartsLocation
        },
        new ConfigHealthCheckDefinition
        {
            Id = "default-suites-path", Category = StoragePaths, Title = "Default GnollBench suites",
            Keys = new[] { DefaultSuiteCatalogService.DefaultSuitesPathKey }, Kind = ConfigCheckKind.Custom,
            Remedy = "Leave Benchmark:DefaultSuitesPath empty to use the built-in suites, or set it to an existing folder.",
            Evaluate = EvaluateDefaultSuitesPath
        },
        new ConfigHealthCheckDefinition
        {
            Id = "embedding-model-path", Category = StoragePaths, Title = "Embedding model",
            Keys = new[] { "RagSettings:EmbeddingModelPath" }, Kind = ConfigCheckKind.File,
            SeverityWhenMissing = ConfigHealthStatus.Info, SeverityWhenAbsent = ConfigHealthStatus.Warning,
            MissingSummary = "Not set. Document retrieval ranks with BM25.",
            Remedy = "Set RagSettings:EmbeddingModelPath and RagSettings:EmbeddingVocabPath to an ONNX model and its vocabulary, or leave both empty."
        },
        new ConfigHealthCheckDefinition
        {
            Id = "embedding-vocab-path", Category = StoragePaths, Title = "Embedding vocabulary",
            Keys = new[] { "RagSettings:EmbeddingVocabPath" }, Kind = ConfigCheckKind.File,
            SeverityWhenMissing = ConfigHealthStatus.Info, SeverityWhenAbsent = ConfigHealthStatus.Warning,
            MissingSummary = "Not set. Document retrieval ranks with BM25.",
            Remedy = "Set RagSettings:EmbeddingModelPath and RagSettings:EmbeddingVocabPath to an ONNX model and its vocabulary, or leave both empty."
        },

        // Database and email
        new ConfigHealthCheckDefinition
        {
            Id = "sql-database-connection", Category = DatabaseAndEmail, Title = "Database connection",
            Keys = new[] { "ConnectionStrings:SqlDatabaseConnection" }, Kind = ConfigCheckKind.Secret, Sensitive = true,
            SeverityWhenMissing = ConfigHealthStatus.Error,
            MissingSummary = "Not set. Overseer cannot reach its database.",
            Remedy = "Set ConnectionStrings:SqlDatabaseConnection in User Secrets."
        },
        new ConfigHealthCheckDefinition
        {
            Id = "email-connection", Category = DatabaseAndEmail, Title = "Email connection",
            Keys = new[] { "ConnectionStrings:EmailConnection" }, Kind = ConfigCheckKind.Custom, Sensitive = true,
            Remedy = "Set ConnectionStrings:EmailConnection in User Secrets.",
            Evaluate = EvaluateEmailConnection
        },
        new ConfigHealthCheckDefinition
        {
            Id = "api-key-alert-recipient", Category = DatabaseAndEmail, Title = "API key alert recipient",
            Keys = new[] { "ApiKeyAlerts:RecipientEmail" }, Kind = ConfigCheckKind.Custom,
            Remedy = "Set ApiKeyAlerts:RecipientEmail, or set ApiKeyAlerts:Enabled to false.",
            Evaluate = EvaluateAlertRecipient
        },

        // Security and privacy
        new ConfigHealthCheckDefinition
        {
            Id = "content-keyring", Category = SecurityAndPrivacy, Title = "Content keyring",
            Keys = new[] { "PrivacySettings:KeyRing", "PrivacySettings:ActiveKeyVersion" }, Kind = ConfigCheckKind.Custom, Sensitive = true,
            Remedy = "Set PrivacySettings:KeyRing:v1 to a base64 32-byte key in User Secrets, and PrivacySettings:ActiveKeyVersion to v1.",
            Evaluate = EvaluateKeyRing
        },
        new ConfigHealthCheckDefinition
        {
            Id = "admins", Category = SecurityAndPrivacy, Title = "Administrators",
            Keys = new[] { "Admins" }, Kind = ConfigCheckKind.Custom,
            Remedy = "Set Admins to a comma-separated list of administrator user names.",
            Evaluate = EvaluateAdmins
        },
        new ConfigHealthCheckDefinition
        {
            Id = "aes-encryption-key", Category = SecurityAndPrivacy, Title = "AES encryption key",
            Keys = new[] { "AesEncryptionKey" }, Kind = ConfigCheckKind.Custom, Sensitive = true,
            Remedy = "Set AesEncryptionKey to a base64 32-byte key in User Secrets.",
            Evaluate = EvaluateAesKey
        },

        // Monitoring
        new ConfigHealthCheckDefinition
        {
            Id = "sentry-dsn", Category = Monitoring, Title = "Sentry DSN",
            Keys = new[] { "SentryDSN" }, Kind = ConfigCheckKind.Secret, Sensitive = true,
            MissingSummary = "Not set. Errors are not reported to Sentry.",
            Remedy = "Set SentryDSN in User Secrets."
        }
    };

    /// <summary>Every check, the declared ones first, then secret placement and limits.</summary>
    public static IReadOnlyList<ConfigHealthCheckDto> Evaluate(ConfigHealthContext context, ILogger? logger)
    {
        var definitions = Definitions
            .Concat(LegalDocumentDefinitions(context))
            .Concat(SecretPlacementDefinitions(context))
            .Concat(LimitDefinitions(context));

        return definitions.Select(d => Run(context, d, logger)).ToList();
    }

    private static ConfigHealthCheckDto Run(ConfigHealthContext context, ConfigHealthCheckDefinition definition, ILogger? logger)
    {
        bool sensitive = definition.Sensitive || definition.Keys.Any(IsSensitive);

        ConfigHealthOutcome outcome;
        try
        {
            outcome = definition.Evaluate != null
                ? definition.Evaluate(context, definition)
                : EvaluateDeclared(context, definition);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Configuration health check {CheckId} failed", definition.Id);
            outcome = new ConfigHealthOutcome(ConfigHealthStatus.Error,
                sensitive ? "The check could not be evaluated." : "The check could not be evaluated: " + ex.Message);
        }

        return new ConfigHealthCheckDto
        {
            Id = definition.Id,
            Category = definition.Category,
            Title = definition.Title,
            Keys = definition.Keys.ToArray(),
            Status = outcome.Status,
            Source = DescribeSource(context, definition.Keys),
            Value = sensitive || string.IsNullOrEmpty(outcome.Value) ? null : outcome.Value,
            Summary = outcome.Summary,
            Remedy = outcome.Status == ConfigHealthStatus.Ok ? null : definition.Remedy
        };
    }

    private static string? DescribeSource(ConfigHealthContext context, IReadOnlyList<string> keys)
    {
        if (keys.Count == 0)
        {
            return null;
        }

        var described = keys
            .Select(context.Sources.Describe)
            .Where(s => s != ConfigurationSourceInspector.NotSet)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        return described.Count == 0 ? ConfigurationSourceInspector.NotSet : string.Join(", ", described);
    }

    private static ConfigHealthOutcome EvaluateDeclared(ConfigHealthContext context, ConfigHealthCheckDefinition definition)
    {
        string key = definition.Keys[0];
        string value = context.Read(key);

        if (value.Length == 0)
        {
            return definition.Kind == ConfigCheckKind.Range
                ? new ConfigHealthOutcome(ConfigHealthStatus.Info, $"Not set. The default of {definition.DefaultValue} is used.")
                : new ConfigHealthOutcome(definition.SeverityWhenMissing, definition.MissingSummary ?? "Not set.");
        }

        return definition.Kind switch
        {
            ConfigCheckKind.Directory or ConfigCheckKind.GitRepository => EvaluateDirectory(definition, value, definition.SeverityWhenAbsent),
            ConfigCheckKind.File => EvaluateFile(value, definition.SeverityWhenAbsent),
            ConfigCheckKind.Range => EvaluatePositiveInteger(definition, value),
            ConfigCheckKind.Command => EvaluateBuildCommand(context, value),
            _ => new ConfigHealthOutcome(ConfigHealthStatus.Ok, "Set.", value)
        };
    }

    private static ConfigHealthOutcome EvaluateDirectory(ConfigHealthCheckDefinition definition, string value, string severityWhenAbsent)
    {
        if (!TryFullPath(value, out string full))
        {
            return new ConfigHealthOutcome(ConfigHealthStatus.Error, "Not a valid path.", value);
        }

        if (!Directory.Exists(full))
        {
            return new ConfigHealthOutcome(severityWhenAbsent, $"The folder {full} does not exist.", value);
        }

        var missing = definition.RequiredSubdirectories
            .Where(sub => !Directory.Exists(Path.Combine(full, sub)))
            .ToList();
        if (missing.Count > 0)
        {
            return new ConfigHealthOutcome(ConfigHealthStatus.Error,
                $"The folder {full} has no {string.Join(" or ", missing)} subfolder.", value);
        }

        // GitHelper reads HEAD from a .git folder; without one a git pull is never noticed.
        if (definition.Kind == ConfigCheckKind.GitRepository && !Directory.Exists(Path.Combine(full, ".git")))
        {
            return new ConfigHealthOutcome(ConfigHealthStatus.Warning,
                $"The folder {full} is not a Git repository, so a git pull is not detected and the index is not refreshed.", value);
        }

        return new ConfigHealthOutcome(ConfigHealthStatus.Ok, $"The folder {full} exists.", value);
    }

    private static ConfigHealthOutcome EvaluateFile(string value, string severityWhenAbsent)
    {
        if (!TryFullPath(value, out string full))
        {
            return new ConfigHealthOutcome(ConfigHealthStatus.Error, "Not a valid path.", value);
        }

        return File.Exists(full)
            ? new ConfigHealthOutcome(ConfigHealthStatus.Ok, $"The file {full} exists.", value)
            : new ConfigHealthOutcome(severityWhenAbsent, $"The file {full} does not exist.", value);
    }

    private static ConfigHealthOutcome EvaluatePositiveInteger(ConfigHealthCheckDefinition definition, string value)
        => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) && parsed > 0
            ? new ConfigHealthOutcome(ConfigHealthStatus.Ok, $"{parsed}.", value)
            : new ConfigHealthOutcome(ConfigHealthStatus.Error, $"'{value}' is not a positive whole number.", value);

    /// <summary>The program a command line starts: its quoted first token, or the text up to the first space.</summary>
    internal static string? BuildToolOf(string command)
    {
        command = command.Trim();
        if (command.Length == 0)
        {
            return null;
        }

        if (command[0] == '"')
        {
            int end = command.IndexOf('"', 1);
            return end < 0 ? command[1..] : command[1..end];
        }

        int space = command.IndexOfAny(new[] { ' ', '\t' });
        return space < 0 ? command : command[..space];
    }

    private static ConfigHealthOutcome EvaluateBuildCommand(ConfigHealthContext context, string command)
    {
        string? tool = BuildToolOf(command);
        if (string.IsNullOrWhiteSpace(tool))
        {
            return new ConfigHealthOutcome(ConfigHealthStatus.Error, "The command names no program.", command);
        }

        // A bare program name is found on PATH by the shell, which this check does not repeat.
        if (!Path.IsPathRooted(tool) && tool.IndexOfAny(new[] { '\\', '/' }) < 0)
        {
            return new ConfigHealthOutcome(ConfigHealthStatus.Info, $"Runs {tool} from PATH; its location is not checked.", command);
        }

        // The command runs in SourceCodePath, so a relative program path is relative to it.
        string sourceCodePath = context.Read("SourceCodePath");
        string candidate = Path.IsPathRooted(tool) || sourceCodePath.Length == 0 ? tool : Path.Combine(sourceCodePath, tool);
        if (!TryFullPath(candidate, out string full))
        {
            return new ConfigHealthOutcome(ConfigHealthStatus.Error, $"The program path {tool} is not valid.", command);
        }

        return File.Exists(full)
            ? new ConfigHealthOutcome(ConfigHealthStatus.Ok, $"The build program {full} exists.", command)
            : new ConfigHealthOutcome(ConfigHealthStatus.Error, $"The build program {full} does not exist.", command);
    }

    private static ConfigHealthOutcome EvaluateMakedefsExecutable(ConfigHealthContext context, ConfigHealthCheckDefinition definition)
    {
        string configured = context.Read("MakedefsExecutablePath");
        if (configured.Length > 0)
        {
            return EvaluateFile(configured, definition.SeverityWhenAbsent);
        }

        string sourceCodePath = context.Read("SourceCodePath");
        if (sourceCodePath.Length == 0)
        {
            return new ConfigHealthOutcome(ConfigHealthStatus.Info, "Not set, and SourceCodePath is not set either.");
        }

        string defaultPath = Path.Combine(sourceCodePath, "tools", "Release", "x64", "makedefs.exe");
        if (!TryFullPath(defaultPath, out string full))
        {
            return new ConfigHealthOutcome(ConfigHealthStatus.Info, "Not set. SourceCodePath is not a valid path.");
        }

        return File.Exists(full)
            ? new ConfigHealthOutcome(ConfigHealthStatus.Info, $"Not set. The default {full} exists.")
            : new ConfigHealthOutcome(ConfigHealthStatus.Info, $"Not set. The default {full} does not exist yet; the build command creates it.");
    }

    private static ConfigHealthOutcome EvaluateMakedefsBranch(ConfigHealthContext context, ConfigHealthCheckDefinition definition)
    {
        string branch = context.Read("MakedefsBranch");
        return branch.Length == 0
            ? new ConfigHealthOutcome(ConfigHealthStatus.Info, "Not set. makedefs runs on any branch.")
            : new ConfigHealthOutcome(ConfigHealthStatus.Info, $"makedefs runs only while {branch} is checked out.", branch);
    }

    private static ConfigHealthOutcome EvaluateMakedefsOutcome(ConfigHealthContext context, ConfigHealthCheckDefinition definition)
    {
        var state = context.Makedefs;
        if (state == null)
        {
            return new ConfigHealthOutcome(ConfigHealthStatus.Info, "The GnollHack source service is not available, so the makedefs outcome is unknown.");
        }

        string missing = state.MissingHeaders.Count > 0
            ? $" Missing generated headers: {string.Join(", ", state.MissingHeaders)}."
            : string.Empty;
        string when = state.FinishedAtUtc is DateTime finished
            ? $" ({finished.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)} UTC)"
            : string.Empty;
        string message = string.IsNullOrWhiteSpace(state.LastMessage)
            ? string.Empty
            : " " + Truncate(state.LastMessage.Trim(), 500);
        string? value = state.LastKind?.ToString();

        string okOrMissing = state.MissingHeaders.Count > 0 ? ConfigHealthStatus.Warning : ConfigHealthStatus.Ok;

        return state.LastKind switch
        {
            null => new ConfigHealthOutcome(ConfigHealthStatus.Info, "makedefs has not run since Overseer started." + missing),
            MakedefsOutcomeKind.BuildFailed => new ConfigHealthOutcome(ConfigHealthStatus.Error, $"The makedefs build failed{when}.{message}{missing}", value),
            MakedefsOutcomeKind.RunFailed => new ConfigHealthOutcome(ConfigHealthStatus.Error, $"makedefs did not generate every header{when}.{message}{missing}", value),
            MakedefsOutcomeKind.ExecutableMissing => new ConfigHealthOutcome(ConfigHealthStatus.Warning, $"The makedefs executable was not found{when}.{message}{missing}", value),
            MakedefsOutcomeKind.SkippedBranch => new ConfigHealthOutcome(ConfigHealthStatus.Info, $"makedefs was skipped because the checked-out branch is not MakedefsBranch{when}.{missing}", value),
            MakedefsOutcomeKind.UpToDate => new ConfigHealthOutcome(okOrMissing, $"The generated headers were up to date{when}.{missing}", value),
            MakedefsOutcomeKind.Generated => new ConfigHealthOutcome(okOrMissing, $"makedefs generated the headers{when}.{missing}", value),
            _ => new ConfigHealthOutcome(ConfigHealthStatus.Info, $"Last outcome: {value}{when}.{missing}", value)
        };
    }

    private static ConfigHealthOutcome EvaluateChartsLocation(ConfigHealthContext context, ConfigHealthCheckDefinition definition)
    {
        string value = context.Read(BenchmarkReportChartStore.ConfigurationKey);
        if (value.Length == 0)
        {
            return new ConfigHealthOutcome(ConfigHealthStatus.Warning, "Not set. Charts cannot be added to report documents.");
        }

        if (!BenchmarkReportChartStore.IsUsableRoot(value))
        {
            return new ConfigHealthOutcome(ConfigHealthStatus.Warning, "Not an absolute path. Charts cannot be added to report documents.", value);
        }

        // The store creates the folder when it writes the first chart.
        var outcome = EvaluateDirectory(definition, value, ConfigHealthStatus.Info);
        return outcome.Status == ConfigHealthStatus.Info
            ? outcome with { Summary = outcome.Summary + " It is created when the first chart is stored." }
            : outcome;
    }

    private static ConfigHealthOutcome EvaluateDefaultSuitesPath(ConfigHealthContext context, ConfigHealthCheckDefinition definition)
    {
        string value = context.Read(DefaultSuiteCatalogService.DefaultSuitesPathKey);
        if (value.Length > 0)
        {
            return EvaluateDirectory(definition, value, ConfigHealthStatus.Error);
        }

        string resolved = new DefaultSuiteCatalogService(context.Configuration).ResolveDirectory();
        return Directory.Exists(resolved)
            ? new ConfigHealthOutcome(ConfigHealthStatus.Ok, $"Not set. The built-in suites in {resolved} are used.")
            : new ConfigHealthOutcome(ConfigHealthStatus.Warning, $"Not set, and the built-in suites folder {resolved} does not exist.");
    }

    private static ConfigHealthOutcome EvaluateEmailConnection(ConfigHealthContext context, ConfigHealthCheckDefinition definition)
    {
        if (context.Read("ConnectionStrings:EmailConnection").Length > 0)
        {
            return new ConfigHealthOutcome(ConfigHealthStatus.Ok, "Set.");
        }

        return new ApiKeyAlertOptions(context.Configuration).Enabled
            ? new ConfigHealthOutcome(ConfigHealthStatus.Warning, "Not set, and API key failure alerts are enabled, so they cannot be emailed.")
            : new ConfigHealthOutcome(ConfigHealthStatus.Info, "Not set. No email is sent.");
    }

    private static ConfigHealthOutcome EvaluateAlertRecipient(ConfigHealthContext context, ConfigHealthCheckDefinition definition)
    {
        var options = new ApiKeyAlertOptions(context.Configuration);
        if (!string.IsNullOrEmpty(options.RecipientEmail))
        {
            return new ConfigHealthOutcome(ConfigHealthStatus.Ok,
                options.Enabled ? "Set." : "Set; API key failure alerts are turned off.", options.RecipientEmail);
        }

        return options.Enabled
            ? new ConfigHealthOutcome(ConfigHealthStatus.Warning, "Not set, and API key failure alerts are enabled.")
            : new ConfigHealthOutcome(ConfigHealthStatus.Info, "Not set; API key failure alerts are turned off.");
    }

    private static ConfigHealthOutcome EvaluateKeyRing(ConfigHealthContext context, ConfigHealthCheckDefinition definition)
    {
        var keyRing = context.KeyRing;
        if (keyRing == null)
        {
            return new ConfigHealthOutcome(ConfigHealthStatus.Error, "The keyring could not be evaluated.");
        }

        return keyRing.IsUsable
            ? new ConfigHealthOutcome(ConfigHealthStatus.Ok, $"Usable; the active version is {keyRing.ActiveVersion} of {keyRing.Versions.Count}.")
            : new ConfigHealthOutcome(ConfigHealthStatus.Error, "Confidential chats cannot store encrypted content: " + keyRing.ValidationError);
    }

    private static ConfigHealthOutcome EvaluateAdmins(ConfigHealthContext context, ConfigHealthCheckDefinition definition)
    {
        string value = context.Read("Admins");
        int count = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length;
        return count == 0
            ? new ConfigHealthOutcome(ConfigHealthStatus.Error, "No administrators are configured.")
            : new ConfigHealthOutcome(ConfigHealthStatus.Ok, count == 1 ? "1 administrator." : $"{count} administrators.", value);
    }

    private static ConfigHealthOutcome EvaluateAesKey(ConfigHealthContext context, ConfigHealthCheckDefinition definition)
    {
        string value = context.Read("AesEncryptionKey");
        if (value.Length == 0)
        {
            return new ConfigHealthOutcome(ConfigHealthStatus.Error, "Not set. Stored API keys cannot be encrypted or read.");
        }

        byte[] key;
        try
        {
            key = Convert.FromBase64String(value);
        }
        catch (FormatException)
        {
            return new ConfigHealthOutcome(ConfigHealthStatus.Error, "Set, but not valid base64.");
        }

        return key.Length == 32
            ? new ConfigHealthOutcome(ConfigHealthStatus.Ok, "Set; a 32-byte key.")
            : new ConfigHealthOutcome(ConfigHealthStatus.Error, $"Set, but {key.Length} bytes long instead of 32.");
    }

    /// <summary>One check per Terms of Service document this host serves.</summary>
    private static IEnumerable<ConfigHealthCheckDefinition> LegalDocumentDefinitions(ConfigHealthContext context)
        => (context.LegalDocuments?.DocumentKeys ?? Array.Empty<string>())
            .OrderBy(k => k, StringComparer.Ordinal)
            .Select(key => new ConfigHealthCheckDefinition
            {
                Id = "legal-document:" + key,
                Category = ToolCorpora,
                Title = (context.LegalDocuments!.GetTitle(key) ?? key) + " (Terms of Service)",
                Keys = new[] { "WikiPath", GnollHackServer.Data.Legal.LegalDocumentOptions.SectionName + ":Documents:" + key + ":WikiPageName" },
                Kind = ConfigCheckKind.Custom,
                Remedy = "Make sure WikiPath is a clone of the GnollHack wiki that holds the page, and that the page starts with a line such as **Last updated:** 18 October 2026.",
                Evaluate = EvaluateLegalDocument
            });

    private static ConfigHealthOutcome EvaluateLegalDocument(ConfigHealthContext context, ConfigHealthCheckDefinition definition)
    {
        string key = definition.Id["legal-document:".Length..];
        var reader = context.LegalDocuments!;
        string fileName = (reader.GetTitle(key) ?? key) + ".md";

        var document = reader.Get(key);
        if (document == null)
        {
            return new ConfigHealthOutcome(ConfigHealthStatus.Warning,
                $"The file {fileName} cannot be read from WikiPath. The Terms viewer offers the wiki link only, and the required version is the newest one any user has accepted.");
        }

        if (document.Version == null)
        {
            return new ConfigHealthOutcome(ConfigHealthStatus.Warning,
                $"{fileName} has no readable 'Last updated' line and Git gives no commit date, so no version can be required.");
        }

        return document.VersionOrigin == "git"
            ? new ConfigHealthOutcome(ConfigHealthStatus.Warning,
                $"{fileName} has no readable 'Last updated' line; version {document.Version} comes from its last Git commit.", document.Version)
            : new ConfigHealthOutcome(ConfigHealthStatus.Ok, $"Version {document.Version} ({document.VersionLabel}).", document.Version);
    }

    /// <summary>One check per sensitive key, the configuration's own connection strings and provider API keys included.</summary>
    private static IEnumerable<ConfigHealthCheckDefinition> SecretPlacementDefinitions(ConfigHealthContext context)
    {
        var keys = new List<string>(SensitiveKeys);
        keys.AddRange(context.Configuration.GetSection("ConnectionStrings").GetChildren().Select(c => c.Path));
        keys.AddRange(context.Configuration.GetSection("AI").GetChildren()
            .Select(c => c.GetSection("APIKey"))
            .Where(s => s.Exists())
            .Select(s => s.Path));

        return keys
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(key => new ConfigHealthCheckDefinition
            {
                Id = "secret-placement:" + key,
                Category = SecretPlacement,
                Title = key,
                Keys = new[] { key },
                Kind = ConfigCheckKind.Custom,
                Sensitive = true,
                Remedy = $"Remove the value of {key} from appsettings and set it with dotnet user-secrets set \"{key}\" in the Overseer project.",
                Evaluate = EvaluateSecretPlacement
            });
    }

    private static ConfigHealthOutcome EvaluateSecretPlacement(ConfigHealthContext context, ConfigHealthCheckDefinition definition)
    {
        string key = definition.Keys[0];
        PublicDefaults.TryGetValue(key, out string? publicDefault);
        var files = context.Sources.AppSettingsFilesWithValue(key, publicDefault);
        if (files.Count > 0)
        {
            return new ConfigHealthOutcome(ConfigHealthStatus.Error,
                $"A value is set in {string.Join(" and ", files)}, which is committed to source control. Move it to User Secrets.");
        }

        var section = context.Configuration.GetSection(key);
        if (publicDefault != null && string.Equals(section.Value, publicDefault, StringComparison.Ordinal))
        {
            return new ConfigHealthOutcome(ConfigHealthStatus.Info,
                "The built-in default is in use. It is public, so it adds no secrecy.");
        }
        return !string.IsNullOrWhiteSpace(section.Value) || section.GetChildren().Any()
            ? new ConfigHealthOutcome(ConfigHealthStatus.Ok, $"Set in {context.Sources.Describe(key)}.")
            : new ConfigHealthOutcome(ConfigHealthStatus.Ok, "Not set.");
    }

    /// <summary>One check per <c>AiPerformanceSettings</c> entry that has a Min, Default or Max.</summary>
    private static IEnumerable<ConfigHealthCheckDefinition> LimitDefinitions(ConfigHealthContext context)
        => context.Configuration.GetSection(LimitsSection).GetChildren()
            .Where(c => c["Min"] != null || c["Default"] != null || c["Max"] != null)
            .Select(c => new ConfigHealthCheckDefinition
            {
                Id = "limits:" + c.Key,
                Category = Limits,
                Title = c.Key,
                Keys = new[] { c.Path + ":Min", c.Path + ":Default", c.Path + ":Max" },
                Kind = ConfigCheckKind.Range,
                Remedy = $"Set {c.Path}:Min, :Default and :Max to whole numbers with Min ≤ Default ≤ Max.",
                Evaluate = EvaluateLimit
            });

    private static ConfigHealthOutcome EvaluateLimit(ConfigHealthContext context, ConfigHealthCheckDefinition definition)
    {
        var names = new[] { "Min", "Default", "Max" };
        var parsed = new int?[3];
        var problems = new List<string>();

        for (int i = 0; i < 3; i++)
        {
            string raw = context.Read(definition.Keys[i]);
            if (raw.Length == 0)
            {
                problems.Add($"{names[i]} is not set");
            }
            else if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int number))
            {
                parsed[i] = number;
            }
            else
            {
                problems.Add($"{names[i]} '{raw}' is not a whole number");
            }
        }

        string value = string.Join(", ", names.Select((n, i) => $"{n} {(parsed[i]?.ToString(CultureInfo.InvariantCulture) ?? "?")}"));

        if (problems.Count > 0)
        {
            bool unparsable = problems.Any(p => p.EndsWith("whole number", StringComparison.Ordinal));
            return new ConfigHealthOutcome(unparsable ? ConfigHealthStatus.Error : ConfigHealthStatus.Warning,
                string.Join("; ", problems) + (unparsable ? "." : "; the code default is used."), value);
        }

        int min = parsed[0]!.Value, def = parsed[1]!.Value, max = parsed[2]!.Value;
        return min <= def && def <= max
            ? new ConfigHealthOutcome(ConfigHealthStatus.Ok, value + ".", value)
            : new ConfigHealthOutcome(ConfigHealthStatus.Error, $"Min ≤ Default ≤ Max does not hold: {value}.", value);
    }

    private static bool TryFullPath(string value, out string full)
    {
        try
        {
            full = Path.GetFullPath(value);
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            full = value;
            return false;
        }
    }

    private static string Truncate(string text, int max) => text.Length <= max ? text : text[..max] + "…";
}
