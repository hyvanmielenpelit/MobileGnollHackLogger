namespace Overseer.Services.Benchmarking;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using Overseer.Services.Agents;
using Overseer.Services.Providers;
using Overseer.Services.Tools;

/// <summary>
/// Checks that a grading request — assessor, second opinion, evidence-informed re-grade,
/// calibration, trial or claim verifier — carries its instructions, the board when the suite has
/// one, and the question, in that order, by building the request body through the provider that
/// will send it and reading the serialised JSON back.
/// </summary>
/// <remarks>
/// <para>
/// A grading prompt built with the board says nothing about the bytes that reached the provider:
/// a provider that drops a second system message grades without the board, and the per-role board
/// figures, read off the loaded entity, would still report it delivered. This is the check that
/// notices, and a role's board figure is recorded only after it passed.
/// </para>
/// <para>
/// The body is JSON-escaped, so the check looks for needles — the first
/// <see cref="NeedleLength"/> characters of each text, escaped by the same serializer — rather
/// than whole texts. The board's needle is taken from the head of its delimited block and must
/// occur exactly once; the board text itself is not required once, because a rubric's BOARD FACTS
/// quote board lines inside the question by design. Order is read in the order the model reads the
/// request — the system text first, then the conversation — which is not always the body's key
/// order: the OpenAI body serialises <c>input</c> before <c>instructions</c>.
/// </para>
/// <para>
/// No network call is made and no tokens are spent: the body is serialised, inspected and
/// discarded.
/// </para>
/// </remarks>
public static class BenchmarkGradingRequestProbe
{
    /// <summary>
    /// How much of each text has to survive into the body. Long enough that no boilerplate line
    /// matches by accident, short enough to be unaffected by anything a provider appends.
    /// </summary>
    private const int NeedleLength = 120;

    /// <summary>Where the three providers put the system text: Anthropic, Google, OpenAI.</summary>
    private static readonly string[] SystemKeys = { "system", "systemInstruction", "instructions" };

    /// <summary>Where the three providers put the conversation: Anthropic, Google, OpenAI.</summary>
    private static readonly string[] ConversationKeys = { "messages", "contents", "input" };

    /// <summary>
    /// Throws <see cref="InvalidOperationException"/> when the body <paramref name="provider"/>
    /// builds for <paramref name="request"/> lacks <paramref name="systemText"/>, the board block
    /// (when <paramref name="boardBlock"/> is given) exactly once, the board text, or
    /// <paramref name="questionMarker"/>, or carries them in another order. Returns silently when
    /// the request is sound.
    /// </summary>
    /// <param name="role">The grading role, named in the message ("assessor", "claim verifier").</param>
    /// <param name="systemText">The text the request's system instructions open with.</param>
    /// <param name="boardBlock">The delimited board as the request carries it; null for a suite without a board.</param>
    /// <param name="boardText">The board text inside <paramref name="boardBlock"/>; null for a suite without a board.</param>
    /// <param name="questionMarker">The line that opens the question-specific part of the request.</param>
    /// <param name="questionNumber">The question this request grades, named in the message.</param>
    public static void Verify(
        IAiProvider provider,
        AgentRunRequest request,
        string role,
        string systemText,
        string? boardBlock,
        string? boardText,
        string questionMarker,
        int? questionNumber = null)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(request);

        string text = ModelOrderText(SerializeBody(provider, request));
        string scope = questionNumber.HasValue ? $" for question {questionNumber.Value}" : string.Empty;
        string subject = $"the {provider.ProviderName} {role} request{scope}";

        var missing = new List<string>();
        int systemAt = IndexOfNeedle(text, systemText);
        if (systemAt < 0) missing.Add("the instructions");

        bool hasBoard = !string.IsNullOrEmpty(boardBlock);
        int boardAt = -1;
        int boardTextAt = -1;
        if (hasBoard)
        {
            string needle = EncodedNeedle(boardBlock)!;
            int count = OccurrenceCount(text, needle);
            if (count > 1)
            {
                throw new InvalidOperationException(
                    $"Harness delivery check failed: {subject} carried the board {count} times.");
            }

            boardAt = count == 1 ? text.IndexOf(needle, StringComparison.Ordinal) : -1;
            if (boardAt < 0) missing.Add("the board");

            if (!string.IsNullOrEmpty(boardText))
            {
                boardTextAt = IndexOfNeedle(text, boardText);
                if (boardTextAt < 0 && boardAt >= 0) missing.Add("the board text");
            }
        }

        int questionAt = IndexOfNeedle(text, questionMarker);
        if (questionAt < 0) missing.Add("the question");

        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                $"Harness delivery check failed: {subject} did not contain {string.Join(", ", missing)}.");
        }

        bool ordered = hasBoard
            ? systemAt < boardAt && boardAt < questionAt && (boardTextAt < 0 || (boardAt < boardTextAt && boardTextAt < questionAt))
            : systemAt < questionAt;
        if (!ordered)
        {
            string expected = hasBoard ? "the instructions, the board and the question" : "the instructions and the question";
            throw new InvalidOperationException(
                $"Harness delivery check failed: {subject} did not carry {expected} in that order.");
        }
    }

    /// <summary>
    /// The request body exactly as <c>AgentLoopRunner</c> builds it for the first call: the
    /// request's system prompt inserted when no segmented prompt is in play and the history has no
    /// system message, the history prepared by the provider, and the body built with the request's
    /// segments, cache key and conversation-tail setting.
    /// </summary>
    internal static string SerializeBody(IAiProvider provider, AgentRunRequest request)
    {
        var history = new List<object>(request.SeedHistory);
        if (!string.IsNullOrWhiteSpace(request.SystemPrompt) && request.SegmentedPrompt == null &&
            !history.Any(m => string.Equals(
                ProviderHelper.GetProperty(m, "role")?.ToString(), "system", StringComparison.OrdinalIgnoreCase)))
        {
            history.Insert(0, new { role = "system", content = request.SystemPrompt });
        }

        history = provider.PrepareMessageHistory(history);
        var body = provider.BuildChatRequestBody(
            modelId: "probe",
            messageHistory: history,
            maxOutputTokens: null,
            thinkingLevel: null,
            requestTools: new ToolsForRequest(),
            segmentedPrompt: request.SegmentedPrompt,
            promptCacheKey: request.PromptCacheKey,
            cacheConversationTail: request.CacheConversationTail);

        return JsonSerializer.Serialize(body);
    }

    /// <summary>
    /// The serialised system text followed by the serialised conversation, in the order the model
    /// reads them. A body that has neither under a known key is returned whole, so an unknown
    /// provider is still checked for presence.
    /// </summary>
    internal static string ModelOrderText(string serializedBody)
    {
        using var doc = JsonDocument.Parse(serializedBody);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            return serializedBody;
        }

        var sb = new StringBuilder();
        foreach (string key in SystemKeys.Concat(ConversationKeys))
        {
            if (root.TryGetProperty(key, out var element))
            {
                // A raw line break never occurs in the compact serialised JSON, so no needle can
                // span the seam between two parts.
                sb.Append(element.GetRawText()).Append('\n');
            }
        }

        return sb.Length > 0 ? sb.ToString() : serializedBody;
    }

    private static int IndexOfNeedle(string text, string? source)
    {
        string? needle = EncodedNeedle(source);
        return needle == null ? -1 : text.IndexOf(needle, StringComparison.Ordinal);
    }

    /// <summary>
    /// The opening of <paramref name="source"/>, encoded by the same serializer that produced the
    /// body, so the comparison is between two encodings of the same characters.
    /// </summary>
    private static string? EncodedNeedle(string? source)
    {
        if (string.IsNullOrEmpty(source)) return null;

        int length = Math.Min(NeedleLength, source.Length);
        // A cut between the halves of a surrogate pair is not a character, and the serializer
        // would encode the lone half differently from the pair it came from.
        if (length < source.Length && char.IsHighSurrogate(source[length - 1])) length--;
        if (length <= 0) return null;

        string encoded = JsonSerializer.Serialize(source[..length]);
        return encoded[1..^1];
    }

    private static int OccurrenceCount(string haystack, string needle)
    {
        int count = 0;
        for (int i = haystack.IndexOf(needle, StringComparison.Ordinal); i >= 0;
             i = haystack.IndexOf(needle, i + needle.Length, StringComparison.Ordinal))
        {
            count++;
        }
        return count;
    }
}
