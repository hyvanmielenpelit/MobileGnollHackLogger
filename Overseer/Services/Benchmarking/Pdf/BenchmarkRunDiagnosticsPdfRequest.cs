namespace Overseer.Services.Benchmarking.Pdf;

using System;
using System.Globalization;

/// <summary>
/// The body of the run-diagnostics PDF request: the diagnostics text the client captured and when it
/// captured it. The text is rendered and returned, never stored or logged.
/// </summary>
public sealed class BenchmarkRunDiagnosticsPdfRequest
{
    public string? Text { get; set; }

    /// <summary>The capture time, ISO 8601; a time without an offset is read as UTC.</summary>
    public string? CapturedAtUtc { get; set; }

    public static bool TryParseCapturedAt(string? value, out DateTime capturedAtUtc)
    {
        capturedAtUtc = default;
        if (string.IsNullOrWhiteSpace(value)) return false;

        if (!DateTimeOffset.TryParse(
                value.Trim(),
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsed))
        {
            return false;
        }

        capturedAtUtc = parsed.UtcDateTime;
        return true;
    }
}
