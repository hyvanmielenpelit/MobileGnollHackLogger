namespace Overseer.Services.Benchmarking;

using System;
using System.Text.RegularExpressions;

public record ProviderErrorClassification(bool IsProviderError, int? HttpStatus, string Message);

public static class BenchmarkProviderErrorClassifier
{
    private static readonly Regex ApiErrorRegex = new(@"API Error:\s*(\d{3})", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static ProviderErrorClassification Classify(string? errorMessage)
    {
        if (string.IsNullOrWhiteSpace(errorMessage))
        {
            return new ProviderErrorClassification(false, null, string.Empty);
        }

        string msg = errorMessage.Trim();

        // 429 Rate limiting
        if (msg.Contains("429") || msg.IndexOf("rate limit", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return new ProviderErrorClassification(true, 429, msg);
        }

        // 529 Overload (Anthropic)
        if (msg.Contains("529") || msg.IndexOf("overloaded_error", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return new ProviderErrorClassification(true, 529, msg);
        }

        // 503 / Overloaded / Service Unavailable / Bad Gateway / 504 Gateway Timeout / 500 Internal Server Error
        if (msg.Contains("503") || msg.IndexOf("overloaded", StringComparison.OrdinalIgnoreCase) >= 0 ||
            msg.IndexOf("service unavailable", StringComparison.OrdinalIgnoreCase) >= 0 ||
            msg.Contains("502") || msg.IndexOf("bad gateway", StringComparison.OrdinalIgnoreCase) >= 0 ||
            msg.Contains("504") || msg.IndexOf("gateway timeout", StringComparison.OrdinalIgnoreCase) >= 0 ||
            msg.Contains("500") || msg.IndexOf("internal server error", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            int code = msg.Contains("502") ? 502 : (msg.Contains("504") ? 504 : (msg.Contains("500") ? 500 : 503));
            return new ProviderErrorClassification(true, code, msg);
        }

        // Timeouts
        if (msg.IndexOf("timed out", StringComparison.OrdinalIgnoreCase) >= 0 ||
            msg.IndexOf("timeout", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return new ProviderErrorClassification(true, 408, msg);
        }

        // API Error: XXX
        var match = ApiErrorRegex.Match(msg);
        if (match.Success && int.TryParse(match.Groups[1].Value, out int statusCode))
        {
            if (statusCode == 429 || statusCode == 500 || statusCode == 502 || statusCode == 503 || statusCode == 504 || statusCode == 529 || statusCode == 408)
            {
                return new ProviderErrorClassification(true, statusCode, msg);
            }
        }

        // Genuine model or application errors (not provider transient failure)
        return new ProviderErrorClassification(false, null, msg);
    }
}
