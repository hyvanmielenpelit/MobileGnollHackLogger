using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using Overseer.Controllers;
using Overseer.Services.Tools;

namespace Overseer.Services.Providers;

public interface IAiProvider
{
    string ProviderName { get; }

    IReadOnlyList<string> SupportedServiceTiers { get; }

    Dictionary<string, object> BuildChatRequestBody(
        string modelId,
        List<object> messageHistory,
        int? maxOutputTokens,
        string? thinkingLevel,
        ToolsForRequest requestTools,
        string? reasoningMode = null,
        string? reasoningSummary = null,
        string? serviceTier = null,
        bool? parallelToolCalls = null,
        SegmentedPrompt? segmentedPrompt = null,
        string? promptCacheKey = null,
        bool cacheConversationTail = true,
        bool disablePromptCache = false);

    /// <summary>
    /// The chat request body carrying the end user's safety identifier, for providers whose API has a field
    /// for one. The default ignores the identifier; a caller passes null for a user's own key or a custom
    /// endpoint.
    /// </summary>
    Dictionary<string, object> BuildChatRequestBody(
        string modelId,
        List<object> messageHistory,
        int? maxOutputTokens,
        string? thinkingLevel,
        ToolsForRequest requestTools,
        string? reasoningMode,
        string? reasoningSummary,
        string? serviceTier,
        bool? parallelToolCalls,
        SegmentedPrompt? segmentedPrompt,
        string? promptCacheKey,
        bool cacheConversationTail,
        bool disablePromptCache,
        string? safetyIdentifier)
        => BuildChatRequestBody(modelId, messageHistory, maxOutputTokens, thinkingLevel, requestTools, reasoningMode,
            reasoningSummary, serviceTier, parallelToolCalls, segmentedPrompt, promptCacheKey, cacheConversationTail,
            disablePromptCache);

    /// <param name="endpoint">
    /// Where the request goes and how it authenticates. Pass
    /// <see cref="AiEndpointDescriptor.Official"/> for the provider's public API.
    /// </param>
    /// <remarks>
    /// The descriptor has no default value on purpose. A caller that forgot to pass one would
    /// silently send a configured deployment's traffic to the public endpoint instead — with a
    /// real credential attached — and nothing would report it.
    /// </remarks>
    string GetChatStreamUrl(string modelId, string apiKey, AiEndpointDescriptor endpoint);

    void ConfigureRequest(HttpRequestMessage request, string apiKey, AiEndpointDescriptor endpoint);

    // Stream parsing
    IAsyncEnumerable<ChatEvent> ParseStreamAsync(
        HttpResponseMessage response,
        bool showDebugLog,
        CancellationToken cancellationToken);

    // Message formatting
    object FormatMessage(string role, string text, List<SendMessageAttachment>? imageAttachments);

    List<object> PrepareMessageHistory(List<object> messages);

    // Tool call history
    void AppendAssistantToolCallsToHistory(
        List<object> messageHistory,
        string iterationText,
        List<JsonElement> toolCalls,
        List<JsonElement>? providerHistoryItems = null);

    void AppendToolResultsToHistory(
        List<object> messageHistory,
        List<ProviderToolResult> results);

    /// <summary>
    /// Appends a user text to the end of the history in this provider's wire shape, merging it
    /// into a trailing user message where the provider requires roles to alternate. The default
    /// adds nothing; every production provider implements it.
    /// </summary>
    void AppendUserTextToHistory(List<object> messageHistory, string text) { }

    bool TryRewriteToolResult(
        List<object> messageHistory,
        string toolCallId,
        string replacementText);

    // Title generation
    Dictionary<string, object> BuildTitleRequestBody(
        string modelId, string systemPrompt, string userMessage, int maxTokens, string? serviceTier = null);

    /// <summary>The title request body carrying the end user's safety identifier; the default ignores it.</summary>
    Dictionary<string, object> BuildTitleRequestBody(
        string modelId, string systemPrompt, string userMessage, int maxTokens, string? serviceTier, string? safetyIdentifier)
        => BuildTitleRequestBody(modelId, systemPrompt, userMessage, maxTokens, serviceTier);

    string GetTitleUrl(string modelId, string apiKey, AiEndpointDescriptor endpoint);

    string? ParseTitleResponse(JsonElement root);

    /// <summary>The usage a non-streaming title response reports; null when it carries no usage object.</summary>
    TokenUsageReport? ParseTitleUsage(JsonElement root) => null;

    // Tool declarations
    object? BuildWebSearchTool();

    object BuildFunctionDeclaration(string name, string description, object parameterSchema);

    object? BuildToolsPayload(List<object> providerTools, List<object> functionDeclarations);

    // Diagnostics & Observability
    // Header-based tier reporting only works on Google's non-streaming :generateContent
    // endpoint — measured absent on :streamGenerateContent on 2026-08-31. Prefer
    // ExtractServiceTierFromBody wherever a parsed response body is available.
    string? ExtractServiceTierFromHeaders(HttpResponseMessage response) => null;

    // Reads the served tier from a parsed response root. The same shape works for a
    // streaming chunk and for a non-streaming response for all supported providers.
    string? ExtractServiceTierFromBody(JsonElement root) => null;
}

