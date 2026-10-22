namespace Overseer.Services.Feedback;

using System.Globalization;
using Microsoft.Extensions.Configuration;

/// <summary>The <c>Feedback</c> configuration section.</summary>
/// <param name="Enabled">
/// The chat feedback switch's state until an administrator first sets it; once a chat setting row
/// exists it is not consulted again.
/// </param>
/// <param name="MaxReportsPerUserPerDay">Recorded reports per user per UTC day in the production chat.</param>
public sealed record FeedbackSettings(bool Enabled, int MaxReportsPerUserPerDay)
{
    public const string SectionName = "Feedback";
    public const int DefaultMaxReportsPerUserPerDay = 20;
    public const int MinMaxReportsPerUserPerDay = 1;
    public const int MaxMaxReportsPerUserPerDay = 200;

    /// <summary>
    /// Reads the section by hand rather than with <c>GetValue&lt;T&gt;</c>, which throws on an
    /// unconvertible value: an unparseable <c>Enabled</c> is false, and an unparseable or
    /// out-of-range daily cap is <see cref="DefaultMaxReportsPerUserPerDay"/>.
    /// </summary>
    public static FeedbackSettings Read(IConfiguration configuration)
    {
        var section = configuration.GetSection(SectionName);

        bool enabled = bool.TryParse(section["Enabled"]?.Trim(), out bool parsedEnabled) && parsedEnabled;

        int cap = int.TryParse(section["MaxReportsPerUserPerDay"]?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedCap)
            && parsedCap >= MinMaxReportsPerUserPerDay
            && parsedCap <= MaxMaxReportsPerUserPerDay
                ? parsedCap
                : DefaultMaxReportsPerUserPerDay;

        return new FeedbackSettings(enabled, cap);
    }
}
