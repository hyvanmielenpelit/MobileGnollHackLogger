namespace Overseer.Services.Benchmarking;

using System;

public record BenchmarkAssessmentFailureInfo(bool IsProviderError, int? HttpStatus, string Message);

public static class BenchmarkAssessmentFailure
{
    public const int MaxErrorLength = 2048;

    /// <summary>
    /// Matches the [MaxLength(1024)] constraint on BenchmarkRunAnswer.ClaimVerificationError.
    /// </summary>
    public const int MaxClaimVerificationErrorLength = 1024;

    // Truncate any text destined for a [MaxLength(2048)] column (or specified maxLength).
    public static string? Truncate(string? text, int maxLength = MaxErrorLength)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        string trimmed = text.Trim();
        if (maxLength <= 0)
        {
            return string.Empty;
        }

        if (trimmed.Length <= maxLength)
        {
            return trimmed;
        }

        return trimmed[..(maxLength - 1)] + "…";
    }

    // terminalError: the agent loop's "error" event payload or a caught exception message.
    // parseErrorMessage: BenchmarkAssessmentParser's message when the JSON could not be read.
    public static BenchmarkAssessmentFailureInfo Describe(string? terminalError, string? parseErrorMessage)
    {
        if (!string.IsNullOrWhiteSpace(terminalError))
        {
            var classification = BenchmarkProviderErrorClassifier.Classify(terminalError);
            if (classification.IsProviderError)
            {
                string rawMsg = $"Assessor provider error (HTTP {classification.HttpStatus}): {terminalError.Trim()}";
                return new BenchmarkAssessmentFailureInfo(true, classification.HttpStatus, Truncate(rawMsg)!);
            }

            string rawFailedMsg = $"Assessor call failed: {terminalError.Trim()}";
            return new BenchmarkAssessmentFailureInfo(false, null, Truncate(rawFailedMsg)!);
        }

        string rawParseMsg = $"Assessor response could not be parsed: {parseErrorMessage?.Trim()}";
        return new BenchmarkAssessmentFailureInfo(false, null, Truncate(rawParseMsg)!);
    }
}
