using System.Globalization;

namespace Overseer.Models;

/// <summary>
/// Raw identity of the SQL Server instance, as reported by SERVERPROPERTY.
/// Every member is nullable: SERVERPROPERTY returns NULL for properties a given
/// version does not implement, and detection may fail entirely.
/// </summary>
public record SqlServerEditionInfo(
    string? Edition,
    int? EngineEdition,
    int? ProductMajorVersion,
    string? ProductVersion,
    string? ProductLevel);

/// <summary>
/// The storage capacity that applies to the current database, resolved from the
/// instance edition and any configured override.
/// </summary>
/// <param name="MaxLimitMb">
/// The ceiling the capacity meter is drawn against, in MB. Zero means there is no
/// ceiling at all; consumers must check <paramref name="HasEngineSizeLimit"/> or test
/// for zero before dividing by this.
/// </param>
/// <param name="HasEngineSizeLimit">
/// True when the database engine itself enforces a per-database size cap. False for
/// Standard, Enterprise, Developer, and the Azure families, where any ceiling shown is
/// an operator-chosen budget rather than a hard limit.
/// </param>
/// <param name="LimitSource">"Detected", "Configured", or "Fallback".</param>
/// <param name="ProductLabel">Display name, e.g. "SQL Server 2025 Express".</param>
public record DatabaseCapacityInfo(
    double MaxLimitMb,
    bool HasEngineSizeLimit,
    string LimitSource,
    string ProductLabel,
    string? EditionName,
    string? ProductVersion);

/// <summary>
/// Maps a SQL Server instance's edition and version onto the per-database data-file
/// size limit it enforces, and classifies an allocation against that limit.
///
/// This type is deliberately pure and dependency-free so that every capacity and
/// threshold decision can be unit-tested without a live database connection.
/// </summary>
public static class SqlServerCapacity
{
    /// <summary>EngineEdition value shared by all Express variants, including LocalDB.</summary>
    public const int ExpressEngineEdition = 4;

    /// <summary>SQL Server 2025 Express (major version 17) and later: 50 GB.</summary>
    public const double ModernExpressLimitMb = 51200;

    /// <summary>SQL Server 2008 R2 through 2022 Express: 10 GB.</summary>
    public const double ClassicExpressLimitMb = 10240;

    /// <summary>SQL Server 2005 and 2008 Express: 4 GB.</summary>
    public const double LegacyExpressLimitMb = 4096;

    /// <summary>
    /// Assumed limit when the instance could not be identified. Deliberately the smaller,
    /// more conservative Express figure so that alerting fails closed rather than silent.
    /// </summary>
    public const double FallbackLimitMb = ClassicExpressLimitMb;

    /// <summary>First major version to carry the raised Express limit.</summary>
    public const int FirstModernExpressMajorVersion = 17;

    public const double DefaultWarningThresholdPercent = 75;
    public const double DefaultCriticalThresholdPercent = 85;

    private const string SourceDetected = "Detected";
    private const string SourceConfigured = "Configured";
    private const string SourceFallback = "Fallback";

    /// <summary>Major version to marketing year, for the display label only.</summary>
    private static readonly Dictionary<int, string> ProductYears = new()
    {
        [9] = "2005",
        [10] = "2008",
        [11] = "2012",
        [12] = "2014",
        [13] = "2016",
        [14] = "2017",
        [15] = "2019",
        [16] = "2022",
        [17] = "2025",
    };

    /// <summary>EngineEdition to edition family, used when the Edition string is missing.</summary>
    private static readonly Dictionary<int, string> EngineEditionNames = new()
    {
        [1] = "Personal",
        [2] = "Standard",
        [3] = "Enterprise",
        [4] = "Express",
        [5] = "Azure SQL Database",
        [6] = "Azure Synapse Analytics",
        [8] = "Azure SQL Managed Instance",
        [9] = "Azure Synapse serverless SQL pool",
        [11] = "Azure SQL Edge",
        [12] = "Azure Synapse serverless SQL pool",
    };

    /// <summary>EngineEdition values that are an Azure service rather than a boxed product.</summary>
    private static readonly HashSet<int> AzureEngineEditions = new() { 5, 6, 8, 9, 11, 12 };

    /// <summary>
    /// Resolves the capacity ceiling for the current database.
    /// </summary>
    /// <param name="info">
    /// Detected instance identity, or null when detection failed.
    /// </param>
    /// <param name="configuredOverrideMb">
    /// Operator override in MB; zero or negative means auto-detect. A positive value always
    /// wins, but detection still fills the label and version fields so that the panel can
    /// show the configured budget alongside the real instance identity.
    /// </param>
    public static DatabaseCapacityInfo Resolve(SqlServerEditionInfo? info, double configuredOverrideMb)
    {
        var (major, minor) = ResolveVersion(info);
        string label = BuildProductLabel(info, major);

        // 1. An explicit override always wins, whatever the instance turns out to be.
        if (configuredOverrideMb > 0)
        {
            return new DatabaseCapacityInfo(
                configuredOverrideMb,
                HasEngineSizeLimit: info?.EngineEdition == ExpressEngineEdition,
                SourceConfigured,
                label,
                info?.Edition,
                info?.ProductVersion);
        }

        // 2. Detection failed outright. Assume the smaller Express limit and say so.
        if (info == null || info.EngineEdition == null)
        {
            return new DatabaseCapacityInfo(
                FallbackLimitMb,
                HasEngineSizeLimit: true,
                SourceFallback,
                label,
                info?.Edition,
                info?.ProductVersion);
        }

        // 3. Express: the engine enforces a per-database data-file cap.
        if (info.EngineEdition == ExpressEngineEdition)
        {
            if (major == null)
            {
                // ProductMajorVersion is absent before SQL Server 2014 SP2 and ProductVersion
                // was unparseable. Fall back to the figure this panel has always assumed.
                return new DatabaseCapacityInfo(
                    FallbackLimitMb,
                    HasEngineSizeLimit: true,
                    SourceFallback,
                    label,
                    info.Edition,
                    info.ProductVersion);
            }

            return new DatabaseCapacityInfo(
                ResolveExpressLimitMb(major.Value, minor),
                HasEngineSizeLimit: true,
                SourceDetected,
                label,
                info.Edition,
                info.ProductVersion);
        }

        // 4. Every other edition: no engine-imposed per-database size cap.
        return new DatabaseCapacityInfo(
            MaxLimitMb: 0,
            HasEngineSizeLimit: false,
            SourceDetected,
            label,
            info.Edition,
            info.ProductVersion);
    }

    /// <summary>
    /// The Express data-file limit for a given product version.
    /// </summary>
    /// <remarks>
    /// The newest band is "greater than or equal to" rather than an exact match on purpose:
    /// an unrecognised newer Express is far more likely to keep or raise the current cap
    /// than to reduce it, so it inherits the most recent known limit instead of silently
    /// falling back to the oldest one.
    /// </remarks>
    public static double ResolveExpressLimitMb(int majorVersion, int? minorVersion)
    {
        if (majorVersion >= FirstModernExpressMajorVersion)
        {
            return ModernExpressLimitMb;
        }

        if (majorVersion >= 11)
        {
            // SQL Server 2012 through 2022.
            return ClassicExpressLimitMb;
        }

        if (majorVersion == 10)
        {
            // 10.50 is 2008 R2, which raised the cap from 4 GB to 10 GB; 10.00 is 2008.
            return minorVersion >= 50 ? ClassicExpressLimitMb : LegacyExpressLimitMb;
        }

        // SQL Server 2005 and anything older.
        return LegacyExpressLimitMb;
    }

    /// <summary>
    /// Classifies an allocation as "Normal", "Warning", or "Critical".
    ///
    /// The percentage thresholds are the primary mechanism and scale with whatever limit
    /// was resolved. The legacy absolute MB thresholds are evaluated in addition whenever
    /// they are set, and the more severe of the two classifications wins, so an operator
    /// can still impose a hard ceiling on an instance shared with other workloads.
    /// </summary>
    public static string ResolveStatusLevel(
        double allocatedMb,
        DatabaseCapacityInfo capacity,
        ChatRetentionSettings settings)
    {
        var byPercent = ClassifyByPercent(allocatedMb, capacity.MaxLimitMb, settings);
        var byAbsolute = ClassifyByAbsolute(allocatedMb, settings);

        return Severity(byPercent) >= Severity(byAbsolute) ? byPercent : byAbsolute;
    }

    private static string ClassifyByPercent(double allocatedMb, double maxLimitMb, ChatRetentionSettings settings)
    {
        if (maxLimitMb <= 0)
        {
            // No ceiling to measure against; only the absolute thresholds can fire.
            return "Normal";
        }

        var warning = NormalizePercent(settings.DatabaseWarningThresholdPercent, DefaultWarningThresholdPercent);
        var critical = NormalizePercent(settings.DatabaseCriticalThresholdPercent, DefaultCriticalThresholdPercent);

        // An inverted pair is a configuration typo. Keep the critical band armed at the
        // higher of the two rather than letting the typo disable it silently.
        critical = Math.Max(warning, critical);

        var usedPercent = (allocatedMb / maxLimitMb) * 100.0;

        if (usedPercent >= critical)
        {
            return "Critical";
        }

        return usedPercent >= warning ? "Warning" : "Normal";
    }

    private static string ClassifyByAbsolute(double allocatedMb, ChatRetentionSettings settings)
    {
        if (settings.DatabaseCriticalThresholdMb > 0 && allocatedMb >= settings.DatabaseCriticalThresholdMb)
        {
            return "Critical";
        }

        if (settings.DatabaseWarningThresholdMb > 0 && allocatedMb >= settings.DatabaseWarningThresholdMb)
        {
            return "Warning";
        }

        return "Normal";
    }

    /// <summary>True when the configured percentage thresholds are inverted or out of range.</summary>
    public static bool HasInvalidThresholds(ChatRetentionSettings settings)
    {
        return !IsUsablePercent(settings.DatabaseWarningThresholdPercent)
            || !IsUsablePercent(settings.DatabaseCriticalThresholdPercent)
            || settings.DatabaseCriticalThresholdPercent <= settings.DatabaseWarningThresholdPercent;
    }

    private static bool IsUsablePercent(double value) => value > 0 && value <= 100;

    private static double NormalizePercent(double value, double fallback)
        => IsUsablePercent(value) ? value : fallback;

    private static int Severity(string statusLevel) => statusLevel switch
    {
        "Critical" => 2,
        "Warning" => 1,
        _ => 0,
    };

    /// <summary>
    /// Determines the major and minor product version, preferring the ProductMajorVersion
    /// property and falling back to parsing ProductVersion, which every version reports.
    /// </summary>
    private static (int? Major, int? Minor) ResolveVersion(SqlServerEditionInfo? info)
    {
        if (info == null)
        {
            return (null, null);
        }

        var (parsedMajor, parsedMinor) = ParseProductVersion(info.ProductVersion);
        return (info.ProductMajorVersion ?? parsedMajor, parsedMinor);
    }

    /// <summary>Parses the leading "major.minor" out of a version string such as "16.0.4200.1".</summary>
    private static (int? Major, int? Minor) ParseProductVersion(string? productVersion)
    {
        if (string.IsNullOrWhiteSpace(productVersion))
        {
            return (null, null);
        }

        var parts = productVersion.Split('.');
        if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var major))
        {
            return (null, null);
        }

        int? minor = null;
        if (parts.Length > 1 && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedMinor))
        {
            minor = parsedMinor;
        }

        return (major, minor);
    }

    /// <summary>
    /// Builds the display label, degrading gracefully as each piece of information goes
    /// missing: "SQL Server 2025 Express", "SQL Server (major version 18) Express",
    /// "Azure SQL Database", "SQL Server 2022", "SQL Server (edition undetected)".
    /// </summary>
    private static string BuildProductLabel(SqlServerEditionInfo? info, int? majorVersion)
    {
        if (info == null || (info.EngineEdition == null && string.IsNullOrWhiteSpace(info.Edition) && majorVersion == null))
        {
            return "SQL Server (edition undetected)";
        }

        var editionWord = ExtractEditionWord(info);

        // The Azure families are services, not a boxed product with a marketing year.
        if (info.EngineEdition != null && AzureEngineEditions.Contains(info.EngineEdition.Value))
        {
            return editionWord ?? "Azure SQL";
        }

        string versionWord;
        if (majorVersion == null)
        {
            versionWord = "SQL Server";
        }
        else if (ProductYears.TryGetValue(majorVersion.Value, out var year))
        {
            versionWord = $"SQL Server {year}";
        }
        else
        {
            versionWord = $"SQL Server (major version {majorVersion.Value})";
        }

        return editionWord == null ? versionWord : $"{versionWord} {editionWord}";
    }

    /// <summary>
    /// Extracts the edition family from the Edition string ("Express Edition (64-bit)" to
    /// "Express"), falling back to the EngineEdition mapping.
    /// </summary>
    private static string? ExtractEditionWord(SqlServerEditionInfo info)
    {
        var edition = info.Edition?.Trim();
        if (!string.IsNullOrEmpty(edition))
        {
            var editionIndex = edition.IndexOf(" Edition", StringComparison.OrdinalIgnoreCase);
            if (editionIndex > 0)
            {
                return edition.Substring(0, editionIndex).Trim();
            }
        }

        if (info.EngineEdition != null && EngineEditionNames.TryGetValue(info.EngineEdition.Value, out var name))
        {
            return name;
        }

        return string.IsNullOrEmpty(edition) ? null : edition;
    }
}
