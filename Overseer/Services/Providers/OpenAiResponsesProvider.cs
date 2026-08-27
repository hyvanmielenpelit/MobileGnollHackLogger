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

    public string ProviderName => "OpenAI";

    public IReadOnlyList<string> SupportedServiceTiers => new[] { "auto", "default", "flex", "priority", "fast" };

    public void ConfigureRequest(HttpRequestMessage request, string apiKey)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
    }

    public string GetChatStreamUrl(string modelId, string apiKey)
    {
        return "https://api.openai.com/v1/responses";
    }

    public Dictionary<string, object> BuildChatRequestBody(
        string modelId,
        List<object> messageHistory,
        int? maxOutputTokens,
        string? thinkingLevel,
        ToolsForRequest requestTools,
        string? reasoningMode = null,
        string? reasoningSummary = null,
        string? serviceTier = null)
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

        if (!string.IsNullOrEmpty(systemContent))
        {
            req["instructions"] = systemContent;
        }

        if (maxOutputTokens.HasValue)
        {
            req["max_output_tokens"] = maxOutputTokens.Value;
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
                    ChatEvent? providerItemEvt = null;

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
                        else if (eventType == "response.failed")
                        {
                            if (json.TryGetProperty("error", out var errorObj))
                            {
                                var errMessage = errorObj.TryGetProperty("message", out var em) ? em.GetString() : "Unknown error";
                                errorEvt = new ChatEvent { Type = "error", Data = $"OpenAI stream error: {errMessage}" };
                            }
                            else
                            {
                                errorEvt = new ChatEvent { Type = "error", Data = "OpenAI stream error: response.failed" };
                            }
                        }
                        else if (eventType == "error")
                        {
                            // In case they emit an "error" event directly
                            if (json.TryGetProperty("error", out var errorObj))
                            {
                                var errMessage = errorObj.TryGetProperty("message", out var em) ? em.GetString() : "Unknown error";
                                errorEvt = new ChatEvent { Type = "error", Data = $"OpenAI stream error: {errMessage}" };
                            }
                        }
                    }
                    catch (JsonException) { }

                    if (errorEvt != null) yield return errorEvt;
                    if (providerItemEvt != null) yield return providerItemEvt;
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

    public string GetTitleUrl(string modelId, string apiKey)
    {
        return "https://api.openai.com/v1/responses";
    }

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
}
