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
        string? promptCacheKey = null);

    string GetChatStreamUrl(string modelId, string apiKey);

    void ConfigureRequest(HttpRequestMessage request, string apiKey);

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

    bool TryRewriteToolResult(
        List<object> messageHistory,
        string toolCallId,
        string replacementText);

    // Title generation
    Dictionary<string, object> BuildTitleRequestBody(
        string modelId, string systemPrompt, string userMessage, int maxTokens, string? serviceTier = null);

    string GetTitleUrl(string modelId, string apiKey);

    string? ParseTitleResponse(JsonElement root);

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

