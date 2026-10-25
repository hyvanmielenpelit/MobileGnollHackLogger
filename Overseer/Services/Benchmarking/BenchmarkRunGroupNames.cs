namespace Overseer.Services.Benchmarking;

using System;
using System.Globalization;

/// <summary>
/// The name a run group gets when nobody names it: <c>Suite · Model · yyyy-MM-dd · R=n</c>. The
/// series, the battery and the manual create all name a group through <see cref="Default"/>, so
/// the three cannot drift apart.
/// </summary>
public static class BenchmarkRunGroupNames
{
    /// <summary>The length of <c>BenchmarkRunGroup.Name</c>, <c>nvarchar(256)</c>.</summary>
    public const int MaxLength = 256;

    private const string Separator = " · ";
    private const string DateFormat = "yyyy-MM-dd";
    private const int DateLength = 10;

    /// <summary>The generated name, cut to <see cref="MaxLength"/>. The date is in UTC and the invariant culture.</summary>
    public static string Default(string suiteName, string modelName, DateTime firstStartedAtUtc, int runCount)
        => Compose(
            suiteName,
            modelName,
            firstStartedAtUtc.ToString(DateFormat, CultureInfo.InvariantCulture),
            runCount);

    /// <summary>
    /// True when <paramref name="name"/> has the shape <see cref="Default"/> gives this suite, model
    /// and run count. The date part is any valid <c>yyyy-MM-dd</c>, because an auto-created group is
    /// dated by its series or battery start rather than by its earliest member.
    /// </summary>
    public static bool IsDefault(string? name, string? suiteName, string? modelName, int runCount)
    {
        if (string.IsNullOrEmpty(name) || suiteName == null || modelName == null) return false;

        string prefix = suiteName + Separator + modelName + Separator;

        // A name cut inside the prefix or the date carries no whole date to read.
        if (prefix.Length + DateLength > MaxLength)
        {
            return name.Length == MaxLength
                && (prefix.Length >= MaxLength
                    ? string.Equals(name, prefix[..MaxLength], StringComparison.Ordinal)
                    : name.StartsWith(prefix, StringComparison.Ordinal));
        }

        if (name.Length < prefix.Length + DateLength) return false;

        string datePart = name.Substring(prefix.Length, DateLength);
        if (!DateTime.TryParseExact(datePart, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
        {
            return false;
        }

        return string.Equals(name, Compose(suiteName, modelName, datePart, runCount), StringComparison.Ordinal);
    }

    private static string Compose(string suiteName, string modelName, string date, int runCount)
    {
        string name = suiteName + Separator + modelName + Separator + date + Separator
            + "R=" + runCount.ToString(CultureInfo.InvariantCulture);
        return name.Length > MaxLength ? name[..MaxLength] : name;
    }
}
