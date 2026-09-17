namespace Overseer.Services.Benchmarking;

using System;
using System.Collections.Generic;
using System.Text.Json;
using Overseer.Services.Providers;
using Overseer.Services.Tools;

/// <summary>
/// Checks that a candidate request really carries the production chat system prompt and, when the
/// suite has one, the game board, by building the request body through the provider that will send
/// it and reading the serialised JSON back.
/// </summary>
/// <remarks>
/// <para>
/// A run's recorded options describe the prompt that was <em>built</em>, not the bytes that reached
/// the provider. Runs 50 and 51 both recorded <c>hasGameSnapshot: true</c> and a candidate prompt
/// hash while the Google candidate received no board and the OpenAI candidate no prompt, and
/// nothing in the harness noticed. This is the check that notices.
/// </para>
/// <para>
/// No network call is made and no tokens are spent: the body is serialised, inspected and
/// discarded.
/// </para>
/// </remarks>
public static class BenchmarkCandidateRequestProbe
{
    /// <summary>
    /// How much of each text has to survive into the body. Long enough that no boilerplate line
    /// matches by accident, short enough to be unaffected by anything a provider appends.
    /// </summary>
    private const int NeedleLength = 120;

    /// <summary>
    /// Throws <see cref="InvalidOperationException"/> when the request body
    /// <paramref name="provider"/> builds from <paramref name="seedHistory"/> does not carry
    /// <paramref name="systemPrompt"/>, or does not carry <paramref name="boardText"/> when there
    /// is one. Returns silently when both are present.
    /// </summary>
    /// <param name="questionNumber">
    /// The question this request belongs to, named in the message. Null for the check that runs
    /// once before a run's first question.
    /// </param>
    public static void Verify(
        IAiProvider provider,
        List<object> seedHistory,
        SegmentedPrompt? segmentedPrompt,
        string systemPrompt,
        string? boardText,
        int? questionNumber = null)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(seedHistory);

        var history = provider.PrepareMessageHistory(new List<object>(seedHistory));
        var body = provider.BuildChatRequestBody(
            modelId: "probe",
            messageHistory: history,
            maxOutputTokens: null,
            thinkingLevel: null,
            requestTools: new ToolsForRequest(),
            segmentedPrompt: segmentedPrompt);

        string serialized = JsonSerializer.Serialize(body);

        var missing = new List<string>();
        if (!ContainsPrompt(serialized, segmentedPrompt, systemPrompt)) missing.Add("system prompt");
        if (!string.IsNullOrEmpty(boardText) && !ContainsNeedle(serialized, boardText)) missing.Add("board");

        if (missing.Count == 0) return;

        string parts = string.Join(" and the ", missing);
        string scope = questionNumber.HasValue ? $" for question {questionNumber.Value}" : string.Empty;
        throw new InvalidOperationException(
            $"Harness delivery check failed: the {provider.ProviderName} request{scope} did not contain the {parts}.");
    }

    /// <summary>
    /// Whether the body carries the system prompt. With a segmented prompt the providers emit the
    /// segments as separate strings — separate Gemini parts, separate Anthropic system blocks — so
    /// each segment is looked for on its own: a needle spanning a segment boundary is nowhere in
    /// the body although every character of the prompt is there.
    /// </summary>
    /// <remarks>
    /// The segments are used only while they still concatenate to <paramref name="systemPrompt"/>,
    /// which is what <c>BuildSegmentedSystemPrompt</c> guarantees and what the run's
    /// <c>CandidateSystemPromptSha256</c> is computed over. Should they ever diverge, the flat
    /// prompt is what the run claims was sent, so that is what is required.
    /// </remarks>
    private static bool ContainsPrompt(string serializedBody, SegmentedPrompt? segmentedPrompt, string systemPrompt)
    {
        if (segmentedPrompt == null ||
            !string.Equals(segmentedPrompt.FullPrompt, systemPrompt, StringComparison.Ordinal))
        {
            return ContainsNeedle(serializedBody, systemPrompt);
        }

        bool anySegment = false;
        foreach (string segment in new[]
                 {
                     segmentedPrompt.FrozenPrefix,
                     segmentedPrompt.SessionPrefix,
                     segmentedPrompt.VolatileSuffix
                 })
        {
            if (string.IsNullOrEmpty(segment)) continue;
            anySegment = true;
            if (!ContainsNeedle(serializedBody, segment)) return false;
        }

        return anySegment;
    }

    /// <summary>
    /// Whether the serialised body contains the opening of <paramref name="text"/>. Both sides are
    /// encoded by the same serializer, so the comparison is between two encodings of the same
    /// characters rather than between raw text and an escaped body.
    /// </summary>
    private static bool ContainsNeedle(string serializedBody, string? text)
    {
        if (string.IsNullOrEmpty(text)) return false;

        int length = Math.Min(NeedleLength, text.Length);
        // A cut between the halves of a surrogate pair is not a character, and the serializer
        // would encode the lone half differently from the pair it came from.
        if (length < text.Length && char.IsHighSurrogate(text[length - 1])) length--;
        if (length <= 0) return false;

        string encoded = JsonSerializer.Serialize(text[..length]);
        return serializedBody.Contains(encoded[1..^1], StringComparison.Ordinal);
    }
}
