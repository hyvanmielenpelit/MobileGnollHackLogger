namespace Overseer.Services.Feedback;

using System;
using System.Security.Cryptography;
using System.Text;

/// <summary>
/// The grouping key of a feedback report: the SHA-256 of its category and normalized subject, and for
/// a <c>tool_problem</c> also its first related tool. The summary is not part of it.
/// </summary>
public static class FeedbackFingerprint
{
    /// <summary>64 lowercase hex characters.</summary>
    public static string Compute(ReportFeedbackArguments args)
    {
        ArgumentNullException.ThrowIfNull(args);

        var key = new StringBuilder();
        key.Append(args.Category).Append('\n').Append(NormalizeSubject(args.Subject));
        if (args.Category == FeedbackCategories.ToolProblem)
        {
            key.Append('\n').Append(args.RelatedTools.Count > 0 ? args.RelatedTools[0].ToLowerInvariant() : "");
        }

        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(key.ToString()));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// Unicode NFKC, lowercase invariant, every character that is not a letter, a digit or a space
    /// turned into a space, whitespace collapsed to one space, trimmed.
    /// </summary>
    public static string NormalizeSubject(string subject)
    {
        if (string.IsNullOrEmpty(subject)) return "";

        string folded = subject.Normalize(NormalizationForm.FormKC).ToLowerInvariant();
        var result = new StringBuilder(folded.Length);
        bool pendingSpace = false;
        foreach (Rune rune in folded.EnumerateRunes())
        {
            if (Rune.IsLetterOrDigit(rune))
            {
                if (pendingSpace && result.Length > 0) result.Append(' ');
                pendingSpace = false;
                result.Append(rune.ToString());
            }
            else
            {
                pendingSpace = true;
            }
        }
        return result.ToString();
    }
}
