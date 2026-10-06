using System.Globalization;

namespace Overseer.Services.ApiKeyAlerts;

/// <summary>
/// The <c>ApiKeyAlerts</c> settings. Parsed without throwing: an unreadable value falls back to its
/// default, and for <see cref="Enabled"/> that default is on, so a typo cannot silence the alert.
/// </summary>
public sealed class ApiKeyAlertOptions
{
    public const string SectionName = "ApiKeyAlerts";
    public const double DefaultThrottleHours = 6;
    public const int DefaultMaxResponseBodyChars = 4000;
    public const int MinResponseBodyCharsLimit = 500;

    public bool Enabled { get; init; } = true;
    public string? RecipientEmail { get; init; }
    public double ThrottleHours { get; init; } = DefaultThrottleHours;
    public int MaxResponseBodyChars { get; init; } = DefaultMaxResponseBodyChars;

    public TimeSpan ThrottleWindow => TimeSpan.FromHours(ThrottleHours);

    public ApiKeyAlertOptions()
    {
    }

    public ApiKeyAlertOptions(IConfiguration configuration)
    {
        var section = configuration.GetSection(SectionName);

        Enabled = !bool.TryParse(section["Enabled"]?.Trim(), out bool enabled) || enabled;

        string? recipient = section["RecipientEmail"]?.Trim();
        RecipientEmail = string.IsNullOrEmpty(recipient) ? null : recipient;

        ThrottleHours = double.TryParse(section["ThrottleHours"]?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double hours)
            && hours > 0 && double.IsFinite(hours)
            ? hours
            : DefaultThrottleHours;

        MaxResponseBodyChars = int.TryParse(section["MaxResponseBodyChars"]?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int chars)
            ? Math.Clamp(chars, MinResponseBodyCharsLimit, ApiKeyFailureReport.MaxResponseBodyCharsLimit)
            : DefaultMaxResponseBodyChars;
    }
}
