using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Overseer.Controllers;
using Overseer.Services.Tools;

namespace Overseer.Services.Providers;

public class OpenAiResponsesProvider : IAiProvider
{
    public static readonly IReadOnlyList<string> ProviderHosts = new[]
    {
        "api.openai.com"
    };

    private readonly IConfiguration _configuration;

    public OpenAiResponsesProvider(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public string ProviderName => "OpenAI";

    public IReadOnlyList<string> SupportedServiceTiers => new[] { "auto", "default", "flex", "priority", "fast" };

    /// <summary>The path the Responses API lives at, on the public endpoint and on a gateway alike.</summary>
    private const string ResponsesPath = "/v1/responses";

    private const string OfficialResponsesUrl = "https://api.openai.com" + ResponsesPath;

    public void ConfigureRequest(HttpRequestMessage request, string apiKey, AiEndpointDescriptor endpoint)
    {
        switch (endpoint.AuthStyle)
        {
            case AiEndpointAuthStyle.AzureApiKey:
                /* Azure OpenAI authenticates with an api-key header, not a bearer token. Sending
                   a bearer token there fails with a 401 that names neither cause. */
                request.Headers.TryAddWithoutValidation("api-key", apiKey);
                break;

            case AiEndpointAuthStyle.None:
                break;

            default:
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
                break;
        }

        ApplyCustomHeaders(request, endpoint);
    }

    public string GetChatStreamUrl(string modelId, string apiKey, AiEndpointDescriptor endpoint)
        => ComposeResponsesUrl(endpoint);

    /* Azure exposes the Responses API under /openai/v1/ and requires api-version as a query
       parameter; an OpenAI-compatible gateway serves the same /v1/responses path the public API
       does. Note the consequence for a local server: Overseer speaks the Responses API, so an
       OpenAI-compatible endpoint that only implements /v1/chat/completions will not work. */
    private static string ComposeResponsesUrl(AiEndpointDescriptor endpoint)
    {
        if (!endpoint.IsCustom)
            return OfficialResponsesUrl;

        string baseUrl = endpoint.BaseUrl!.TrimEnd('/');

        if (endpoint.AuthStyle == AiEndpointAuthStyle.AzureApiKey)
        {
            string version = Uri.EscapeDataString(endpoint.ApiVersion ?? string.Empty);
            return $"{baseUrl}/openai/v1/responses?api-version={version}";
        }

        return baseUrl + ResponsesPath;
    }

    private static void ApplyCustomHeaders(HttpRequestMessage request, AiEndpointDescriptor endpoint)
    {
        if (endpoint.CustomHeaders == null)
            return;

        // Already allowlisted by EndpointPolicy; nothing here can be a credential or Host header.
        foreach (var (name, value) in endpoint.CustomHeaders)
            request.Headers.TryAddWithoutValidation(name, value);
    }

    public Dictionary<string, object> BuildChatRequestBody(
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
        bool cacheConversationTail = true)
    {
        // Extract system message
        string systemContent = "";
        var nonSystemMessages = new List<object>();

        foreach (var msgObj in messageHistory)
        {
            try
            {
                var msgJson = JsonSerializer.Serialize(msgObj);
                using var doc = JsonDocument.Parse(msgJson);
                var root = doc.RootElement;
                if (root.TryGetProperty("role", out var roleProp) && roleProp.GetString() == "system")
                {
                    if (root.TryGetProperty("content", out var contentProp) && contentProp.ValueKind == JsonValueKind.String)
                    {
                        var text = contentProp.GetString();
                        if (!string.IsNullOrEmpty(text))
                        {
                            if (string.IsNullOrEmpty(systemContent)) systemContent = text;
                            else systemContent += "\n\n" + text;
                        }
                    }
                }
                else
                {
                    nonSystemMessages.Add(msgObj);
                }
            }
            catch { }
        }

        var req = new Dictionary<string, object>
        {
            ["model"] = modelId,
            ["input"] = nonSystemMessages,
            ["stream"] = true,
            ["store"] = false // Privacy: Do not store state on OpenAI's servers
        };

        bool enablePromptCacheKey = _configuration?.GetValue<bool>("PromptCacheSettings:EnableOpenAiPromptCacheKey", true) ?? true;
        if (enablePromptCacheKey && !string.IsNullOrEmpty(promptCacheKey))
        {
            // Note: OpenAI prompt caching reduces latency and token cost for cached prefix tokens,
            // but cached input tokens still count against TPM (Tokens Per Minute) rate limits.
            req["prompt_cache_key"] = promptCacheKey;
        }

        if (!string.IsNullOrEmpty(systemContent))
        {
            req["instructions"] = systemContent;
        }

        int? configuredDefault = _configuration?.GetValue<int?>("DefaultMaxOutputTokens:OpenAI");
        int? effectiveMaxTokens = maxOutputTokens ?? configuredDefault;
        if (effectiveMaxTokens.HasValue)
        {
            req["max_output_tokens"] = effectiveMaxTokens.Value;
        }

        var reasoningObj = new Dictionary<string, object>();
        if (!string.IsNullOrEmpty(thinkingLevel) && thinkingLevel != "none")
        {
            reasoningObj["effort"] = thinkingLevel;
        }
        if (!string.IsNullOrEmpty(reasoningMode) && reasoningMode != "none")
        {
            reasoningObj["mode"] = reasoningMode;
        }
        if (!string.IsNullOrEmpty(reasoningSummary) && reasoningSummary != "default")
        {
            reasoningObj["summary"] = reasoningSummary;
        }
        if (reasoningObj.Count > 0)
        {
            req["include"] = new[] { "reasoning.encrypted_content" };
            req["reasoning"] = reasoningObj;
        }

        if (!string.IsNullOrEmpty(serviceTier) && !string.Equals(serviceTier, "none", StringComparison.OrdinalIgnoreCase))
        {
            req["service_tier"] = serviceTier;
        }

        var toolsPayload = BuildToolsPayload(requestTools.ProviderTools, requestTools.FunctionDeclarations);
        if (toolsPayload != null)
        {
            req["tools"] = toolsPayload;
            if (parallelToolCalls.HasValue)
            {
                req["parallel_tool_calls"] = parallelToolCalls.Value;
            }
        }

        return req;
    }

    public async IAsyncEnumerable<ChatEvent> ParseStreamAsync(
        HttpResponseMessage response,
        bool showDebugLog,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        yield return new ChatEvent { Type = "provider_history_reset", Data = "" };

        var toolCallsInProgress = new Dictionary<string, (string name, StringBuilder args)>();
        var reasoningSanitizer = new ReasoningTextSanitizer();
        var visibleSanitizer = new ReasoningTextSanitizer();
        bool replayUnavailable = false;

        while (!cancellationToken.IsCancellationRequested)
        {
            var eventLine = await reader.ReadLineAsync(cancellationToken);
            if (eventLine == null) break;

            if (eventLine.StartsWith("event: "))
            {
                var eventType = eventLine.Substring(7).Trim();
                
                var dataLine = await reader.ReadLineAsync(cancellationToken);
                if (dataLine != null && dataLine.StartsWith("data: "))
                {
                    var dataStr = dataLine.Substring(6).Trim();
                    if (dataStr == "[DONE]") continue;

                    string? chunkStr = null;
                    string? thinkingChunkStr = null;
                    ChatEvent? toolCallEvt = null;
                    ChatEvent? errorEvt = null;
                    ChatEvent? debugEvt = null;
                    ChatEvent? providerItemEvt = null;
                    ChatEvent? tierEvt = null;
                    ChatEvent? usageEvt = null;
                    ChatEvent? finishReasonEvt = null;

                    try
                    {
                        var json = JsonSerializer.Deserialize<JsonElement>(dataStr);

                        if (eventType == "response.output_text.delta")
                        {
                            if (json.TryGetProperty("delta", out var delta) && delta.ValueKind == JsonValueKind.String)
                            {
                                var text = delta.GetString() ?? "";
                                var sanitized = visibleSanitizer.Push(text);
                                if (!string.IsNullOrEmpty(sanitized))
                                {
                                    chunkStr = sanitized;
                                }
                            }
                        }
                        else if (eventType == "response.reasoning_summary_text.delta")
                        {
                            if (json.TryGetProperty("delta", out var delta) && delta.ValueKind == JsonValueKind.String)
                            {
                                var text = delta.GetString() ?? "";
                                var sanitized = reasoningSanitizer.Push(text);
                                if (!string.IsNullOrEmpty(sanitized))
                                {
                                    thinkingChunkStr = sanitized;
                                }
                            }
                        }
                        else if (eventType == "response.output_item.added")
                        {
                            if (json.TryGetProperty("item", out var item))
                            {
                                if (item.TryGetProperty("type", out var typeProp) && typeProp.GetString() == "function_call")
                                {
                                    if (item.TryGetProperty("call_id", out var callIdProp) && item.TryGetProperty("name", out var nameProp))
                                    {
                                        var callId = callIdProp.GetString() ?? "";
                                        var name = nameProp.GetString() ?? "";
                                        toolCallsInProgress[callId] = (name, new StringBuilder());
                                    }
                                }
                            }
                        }
                        else if (eventType == "response.function_call_arguments.delta")
                        {
                            if (json.TryGetProperty("call_id", out var callIdProp) && json.TryGetProperty("delta", out var delta) && delta.ValueKind == JsonValueKind.String)
                            {
                                var callId = callIdProp.GetString() ?? "";
                                if (toolCallsInProgress.ContainsKey(callId))
                                {
                                    toolCallsInProgress[callId].args.Append(delta.GetString());
                                }
                            }
                        }
                        else if (eventType == "response.function_call_arguments.done")
                        {
                            if (json.TryGetProperty("call_id", out var callIdProp) && json.TryGetProperty("arguments", out var argsProp) && argsProp.ValueKind == JsonValueKind.String)
                            {
                                var callId = callIdProp.GetString() ?? "";
                                if (toolCallsInProgress.ContainsKey(callId))
                                {
                                    toolCallsInProgress[callId].args.Clear();
                                    toolCallsInProgress[callId].args.Append(argsProp.GetString());
                                }
                            }
                        }
                        else if (eventType == "response.output_item.done")
                        {
                            if (json.TryGetProperty("item", out var item))
                            {
                                if (item.TryGetProperty("type", out var typeProp))
                                {
                                    var itemType = typeProp.GetString();
                                    if (itemType == "function_call")
                                    {
                                        if (item.TryGetProperty("call_id", out var callIdProp))
                                        {
                                            var callId = callIdProp.GetString() ?? "";
                                            if (toolCallsInProgress.ContainsKey(callId))
                                            {
                                                var callData = toolCallsInProgress[callId];
                                                var argsStr = callData.args.ToString();
                                                if (item.TryGetProperty("arguments", out var finalArgs) && finalArgs.ValueKind == JsonValueKind.String)
                                                {
                                                    argsStr = finalArgs.GetString() ?? argsStr;
                                                }
                                                
                                                if (!replayUnavailable)
                                                {
                                                    providerItemEvt = new ChatEvent { Type = "provider_history_item", Data = item.GetRawText() };
                                                }

                                                var callObj = new { id = callId, name = callData.name, arguments = argsStr };
                                                toolCallEvt = new ChatEvent { Type = "tool_call_complete", Data = JsonSerializer.Serialize(callObj) };
                                                toolCallsInProgress.Remove(callId);
                                            }
                                        }
                                    }
                                    else if (itemType == "reasoning")
                                    {
                                        if (!item.TryGetProperty("encrypted_content", out var ecProp) || ecProp.ValueKind != JsonValueKind.String || string.IsNullOrEmpty(ecProp.GetString()))
                                        {
                                            replayUnavailable = true;
                                        }
                                        else if (!replayUnavailable)
                                        {
                                            providerItemEvt = new ChatEvent { Type = "provider_history_item", Data = item.GetRawText() };
                                        }
                                    }
                                    else if (itemType == "message")
                                    {
                                        if (!replayUnavailable)
                                        {
                                            providerItemEvt = new ChatEvent { Type = "provider_history_item", Data = item.GetRawText() };
                                        }
                                    }
                                }
                            }
                        }
                        else if (eventType == "response.completed" || eventType == "response.done" || eventType == "response.incomplete")
                        {
                            var respObj = json.TryGetProperty("response", out var rProp) ? rProp : json;

                            var tier = ExtractServiceTierFromBody(respObj);
                            if (tier != null)
                            {
                                tierEvt = new ChatEvent { Type = "service_tier", Data = tier };
                            }

                            if (respObj.TryGetProperty("usage", out var usageProp))
                            {
                                int inputTokens = usageProp.TryGetProperty("input_tokens", out var itProp) ? itProp.GetInt32() : 0;
                                int outputTokens = usageProp.TryGetProperty("output_tokens", out var otProp) ? otProp.GetInt32() : 0;
                                int cachedTokens = 0;
                                if (usageProp.TryGetProperty("input_tokens_details", out var itdProp) &&
                                    itdProp.TryGetProperty("cached_tokens", out var ctProp))
                                {
                                    cachedTokens = ctProp.GetInt32();
                                }
                                int reasoningTokens = 0;
                                if (usageProp.TryGetProperty("output_tokens_details", out var otdProp) &&
                                    otdProp.TryGetProperty("reasoning_tokens", out var rtProp))
                                {
                                    reasoningTokens = rtProp.GetInt32();
                                }
                                int uncached = Math.Max(0, inputTokens - cachedTokens);

                                var report = new TokenUsageReport
                                {
                                    TotalPromptTokens = inputTokens,
                                    CacheReadTokens = cachedTokens,
                                    CacheCreationTokens = 0,
                                    UncachedInputTokens = uncached,
                                    OutputTokens = outputTokens,
                                    ReasoningTokens = reasoningTokens
                                };
                                usageEvt = new ChatEvent
                                {
                                    Type = "usage",
                                    Data = JsonSerializer.Serialize(report),
                                    UsageReport = report
                                };

                                if (showDebugLog)
                                {
                                    debugEvt = new ChatEvent
                                    {
                                        Type = "debug",
                                        Data = $"[Main Chat - OpenAI] usage: input_tokens={inputTokens}, output_tokens={outputTokens}, cached_tokens={cachedTokens}, reasoning_tokens={reasoningTokens}"
                                    };
                                }
                            }

                            if (respObj.TryGetProperty("status", out var statusProp) && statusProp.GetString() == "incomplete")
                            {
                                string reason = "unknown";
                                if (respObj.TryGetProperty("incomplete_details", out var incDetails) &&
                                    incDetails.TryGetProperty("reason", out var reasonProp))
                                {
                                    reason = reasonProp.GetString() ?? "unknown";
                                }
                                string tokenUsage = "";
                                if (respObj.TryGetProperty("usage", out var usageProp2))
                                {
                                    if (usageProp2.TryGetProperty("output_tokens", out var otProp2))
                                    {
                                        tokenUsage = $", output_tokens={otProp2.GetInt32()}";
                                    }
                                }
                                debugEvt = new ChatEvent
                                {
                                    Type = "debug",
                                    Data = $"[OpenAI] Response incomplete: reason={reason}{tokenUsage}"
                                };
                            }

                            // The provider's own reason for ending this response, verbatim: the incomplete
                            // reason where there is one, and the response status otherwise. Last write wins:
                            // the loop runner keeps the final call's value, which is the one that describes
                            // the turn the user got.
                            if (respObj.TryGetProperty("status", out var finishStatusProp) &&
                                finishStatusProp.ValueKind == JsonValueKind.String)
                            {
                                string? finishReason = finishStatusProp.GetString();
                                if (finishReason == "incomplete" &&
                                    respObj.TryGetProperty("incomplete_details", out var finishDetails) &&
                                    finishDetails.TryGetProperty("reason", out var finishReasonProp) &&
                                    !string.IsNullOrEmpty(finishReasonProp.GetString()))
                                {
                                    finishReason = finishReasonProp.GetString();
                                }
                                if (!string.IsNullOrEmpty(finishReason))
                                {
                                    finishReasonEvt = new ChatEvent { Type = "finish_reason", Data = finishReason };
                                }
                            }
                        }
                        else if (eventType == "response.failed")
                        {
                            /* The failure carries its code and message inside response.error; the
                               top-level error object is the older shape. The status is the last
                               informative fallback when neither carries a message. */
                            JsonElement errorObj = default;
                            bool hasError = false;
                            if (json.TryGetProperty("response", out var failedResp) &&
                                failedResp.ValueKind == JsonValueKind.Object &&
                                failedResp.TryGetProperty("error", out var nestedError) &&
                                nestedError.ValueKind == JsonValueKind.Object)
                            {
                                errorObj = nestedError;
                                hasError = true;
                            }
                            else if (json.TryGetProperty("error", out var topError) &&
                                     topError.ValueKind == JsonValueKind.Object)
                            {
                                errorObj = topError;
                                hasError = true;
                            }

                            string? errCode = hasError ? ReadStringProperty(errorObj, "code") : null;
                            string? errMessage = hasError ? ReadStringProperty(errorObj, "message") : null;
                            string? failedStatus = failedResp.ValueKind == JsonValueKind.Object
                                ? ReadStringProperty(failedResp, "status")
                                : null;

                            string describedMessage = errMessage
                                ?? (string.IsNullOrEmpty(failedStatus) ? "Unknown error" : $"status={failedStatus}");

                            errorEvt = new ChatEvent
                            {
                                Type = "error",
                                Data = $"OpenAI stream error: [{errCode ?? "response.failed"}] {describedMessage}",
                                Detail = TruncateDetail(dataStr)
                            };
                        }
                        else if (eventType == "error")
                        {
                            // In case they emit an "error" event directly
                            if (json.TryGetProperty("error", out var errorObj) && errorObj.ValueKind == JsonValueKind.Object)
                            {
                                string? errCode = ReadStringProperty(errorObj, "code");
                                string? errMessage = ReadStringProperty(errorObj, "message");
                                errorEvt = new ChatEvent
                                {
                                    Type = "error",
                                    Data = $"OpenAI stream error: [{errCode ?? "error"}] {errMessage ?? "Unknown error"}",
                                    Detail = TruncateDetail(dataStr)
                                };
                            }
                            else
                            {
                                string? errCode = ReadStringProperty(json, "code");
                                string? errMessage = ReadStringProperty(json, "message");
                                errorEvt = new ChatEvent
                                {
                                    Type = "error",
                                    Data = $"OpenAI stream error: [{errCode ?? "error"}] {errMessage ?? "Unknown error"}",
                                    Detail = TruncateDetail(dataStr)
                                };
                            }
                        }
                    }
                    catch (JsonException) { }

                    if (debugEvt != null) yield return debugEvt;
                    if (finishReasonEvt != null) yield return finishReasonEvt;
                    if (errorEvt != null) yield return errorEvt;
                    if (providerItemEvt != null) yield return providerItemEvt;
                    if (tierEvt != null) yield return tierEvt;
                    if (usageEvt != null) yield return usageEvt;
                    if (!string.IsNullOrEmpty(thinkingChunkStr)) yield return new ChatEvent { Type = "thinking_chunk", Data = thinkingChunkStr };
                    if (!string.IsNullOrEmpty(chunkStr)) yield return new ChatEvent { Type = "chunk", Data = chunkStr };
                    if (toolCallEvt != null) yield return toolCallEvt;
                }
            }
        }

        var rTail = reasoningSanitizer.Flush();
        if (!string.IsNullOrEmpty(rTail)) yield return new ChatEvent { Type = "thinking_chunk", Data = rTail };

        var vTail = visibleSanitizer.Flush();
        if (!string.IsNullOrEmpty(vTail)) yield return new ChatEvent { Type = "chunk", Data = vTail };

        if (replayUnavailable)
        {
            yield return new ChatEvent { Type = "provider_history_discard", Data = "" };
        }
    }

    public object FormatMessage(string role, string text, List<SendMessageAttachment>? imageAttachments)
    {
        var contentList = new List<object>();

        if (role == "user")
        {
            contentList.Add(new { type = "input_text", text = text });
            
            if (imageAttachments != null)
            {
                foreach (var img in imageAttachments)
                {
                    contentList.Add(new
                    {
                        type = "input_image",
                        image_url = $"data:{img.ContentType};base64,{img.Base64Data}"
                    });
                }
            }
        }
        else if (role == "assistant")
        {
            contentList.Add(new { type = "output_text", text = text });
        }
        
        return new { role = role, content = contentList };
    }

    public List<object> PrepareMessageHistory(List<object> messages)
    {
        // System message extraction is done in BuildChatRequestBody, 
        // so we just return the messages as-is here.
        return messages;
    }

    public void AppendAssistantToolCallsToHistory(
        List<object> messageHistory,
        string iterationText,
        List<JsonElement> toolCalls,
        List<JsonElement>? providerHistoryItems = null)
    {
        if (providerHistoryItems != null && providerHistoryItems.Count > 0)
        {
            foreach (var item in providerHistoryItems)
            {
                messageHistory.Add(item);
            }
            return;
        }

        if (!string.IsNullOrEmpty(iterationText))
        {
            messageHistory.Add(new { role = "assistant", content = new[] { new { type = "output_text", text = iterationText } } });
        }

        foreach (var tc in toolCalls)
        {
            var id = tc.GetProperty("id").GetString();
            var name = tc.GetProperty("name").GetString();
            var args = tc.GetProperty("arguments").GetString();
            
            messageHistory.Add(new
            {
                type = "function_call",
                call_id = id,
                name = name,
                arguments = args
            });
        }
    }

    public void AppendToolResultsToHistory(
        List<object> messageHistory,
        List<ProviderToolResult> results)
    {
        foreach (var res in results)
        {
            messageHistory.Add(new
            {
                type = "function_call_output",
                call_id = res.ToolCallId,
                output = res.Content
            });
        }
    }

    public bool TryRewriteToolResult(List<object> messageHistory, string toolCallId, string replacementText)
    {
        for (int i = 0; i < messageHistory.Count; i++)
        {
            var item = messageHistory[i];
            string? callId = null;
            if (item is JsonElement je && je.ValueKind == JsonValueKind.Object)
            {
                if (je.TryGetProperty("type", out var tProp) && tProp.GetString() == "function_call_output" &&
                    je.TryGetProperty("call_id", out var cidProp))
                {
                    callId = cidProp.GetString();
                }
            }
            else
            {
                var typeVal = ProviderHelper.GetProperty(item, "type")?.ToString();
                if (typeVal == "function_call_output")
                {
                    callId = ProviderHelper.GetProperty(item, "call_id")?.ToString();
                }
            }

            if (callId == toolCallId)
            {
                messageHistory[i] = new
                {
                    type = "function_call_output",
                    call_id = toolCallId,
                    output = replacementText
                };
                return true;
            }
        }
        return false;
    }

    public Dictionary<string, object> BuildTitleRequestBody(
        string modelId, string systemPrompt, string userMessage, int maxTokens, string? serviceTier = null)
    {
        var req = new Dictionary<string, object>
        {
            ["model"] = modelId,
            ["input"] = new List<object>
            {
                new { role = "user", content = new[] { new { type = "input_text", text = userMessage } } }
            },
            ["instructions"] = systemPrompt,
            ["max_output_tokens"] = maxTokens,
            ["store"] = false
        };

        if (!string.IsNullOrEmpty(serviceTier) && !string.Equals(serviceTier, "none", StringComparison.OrdinalIgnoreCase))
        {
            req["service_tier"] = serviceTier;
        }

        return req;
    }

    public string GetTitleUrl(string modelId, string apiKey, AiEndpointDescriptor endpoint)
        => ComposeResponsesUrl(endpoint);

    public string? ParseTitleResponse(JsonElement root)
    {
        if (root.TryGetProperty("output", out var outputArray))
        {
            foreach (var item in outputArray.EnumerateArray())
            {
                if (item.TryGetProperty("type", out var typeProp) && typeProp.GetString() == "message")
                {
                    if (item.TryGetProperty("content", out var contentArray) && contentArray.GetArrayLength() > 0)
                    {
                        var firstContent = contentArray[0];
                        if (firstContent.TryGetProperty("text", out var textProp))
                        {
                            return textProp.GetString()?.Trim('"', ' ', '\r', '\n');
                        }
                    }
                }
            }
        }
        return null;
    }

    public object? BuildWebSearchTool()
    {
        return new { type = "web_search" };
    }

    public object BuildFunctionDeclaration(string name, string description, object parameterSchema)
    {
        return new
        {
            type = "function",
            name = name,
            description = description,
            parameters = parameterSchema
        };
    }

    public object? BuildToolsPayload(List<object> providerTools, List<object> functionDeclarations)
    {
        var combined = new List<object>();
        combined.AddRange(providerTools);
        combined.AddRange(functionDeclarations);
        return combined.Count > 0 ? combined : null;
    }

    public string? ExtractServiceTierFromBody(JsonElement root)
    {
        if (root.TryGetProperty("service_tier", out var tier) && tier.ValueKind == JsonValueKind.String)
        {
            return ProviderHelper.NormalizeServiceTier(tier.GetString());
        }
        return null;
    }

    /// <summary>The named property as a non-empty string, or null when it is absent, null or another kind.</summary>
    private static string? ReadStringProperty(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object) return null;
        if (!element.TryGetProperty(propertyName, out var prop)) return null;
        if (prop.ValueKind != JsonValueKind.String) return null;
        var value = prop.GetString();
        return string.IsNullOrEmpty(value) ? null : value;
    }

    /// <summary>The raw failure payload kept for diagnostics, cut to a bounded length.</summary>
    private const int MaxErrorDetailLength = 3500;

    private static string? TruncateDetail(string? raw)
    {
        if (string.IsNullOrEmpty(raw)) return raw;
        return raw.Length <= MaxErrorDetailLength ? raw : raw.Substring(0, MaxErrorDetailLength);
    }
}
