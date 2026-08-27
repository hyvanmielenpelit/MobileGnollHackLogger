using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Overseer.Controllers;
using Overseer.Services.Tools;

namespace Overseer.Services.Providers;

public class AnthropicProvider : IAiProvider
{
    public static readonly IReadOnlyList<string> ProviderHosts = new[]
    {
        "api.anthropic.com"
    };

    private readonly IConfiguration _configuration;

    public AnthropicProvider(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public string ProviderName => "Anthropic";

    public IReadOnlyList<string> SupportedServiceTiers => new[] { "auto", "standard_only" };

    public void ConfigureRequest(HttpRequestMessage request, string apiKey)
    {
        request.Headers.TryAddWithoutValidation("x-api-key", apiKey);
        request.Headers.TryAddWithoutValidation("anthropic-version", "2023-06-01");
    }

    public string GetChatStreamUrl(string modelId, string apiKey)
    {
        return "https://api.anthropic.com/v1/messages";
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
        var (systemContent, nonSystemMessages) = ExtractSystemAndNonSystemMessages(messageHistory);

        int defaultAnthropicTokens = _configuration.GetValue<int?>("DefaultMaxOutputTokens:Anthropic") ?? 8192;
        int effectiveMaxTokens = maxOutputTokens.HasValue ? maxOutputTokens.Value : defaultAnthropicTokens;

        var req = new Dictionary<string, object>
        {
            ["model"] = modelId,
            ["messages"] = nonSystemMessages,
            ["stream"] = true,
            ["max_tokens"] = effectiveMaxTokens
        };

        if (!string.IsNullOrEmpty(systemContent))
        {
            req["system"] = systemContent;
        }

        if (!string.IsNullOrEmpty(thinkingLevel))
        {
            if (!string.IsNullOrEmpty(reasoningSummary) && reasoningSummary != "default")
            {
                req["thinking"] = new { type = "adaptive", display = reasoningSummary };
            }
            else
            {
                req["thinking"] = new { type = "adaptive" };
            }
            req["output_config"] = new { effort = thinkingLevel };
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

    private class BlockInProgress
    {
        public string Type { get; set; } = "";
        public StringBuilder Content { get; set; } = new();
        public StringBuilder Signature { get; set; } = new();
        public string? ToolId { get; set; }
        public string? ToolName { get; set; }
        public StringBuilder ToolArgs { get; set; } = new();
        public JsonElement? RawBlock { get; set; }
    }

    public async IAsyncEnumerable<ChatEvent> ParseStreamAsync(
        HttpResponseMessage response,
        bool showDebugLog,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        yield return new ChatEvent { Type = "provider_history_reset", Data = "" };

        var blocksInProgress = new Dictionary<int, BlockInProgress>();
        var reasoningSanitizer = new ReasoningTextSanitizer();
        var visibleSanitizer = new ReasoningTextSanitizer();
        bool replayUnavailable = false;

        while (!cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (line == null) break;

            if (line.StartsWith("data: "))
            {
                var data = line.Substring(6).Trim();
                string? chunkStr = null;
                string? thinkingChunkStr = null;
                ChatEvent? toolCallEvt = null;
                ChatEvent? errorEvt = null;
                ChatEvent? debugEvt = null;
                var providerItemEvts = new List<ChatEvent>();

                try
                {
                    var json = JsonSerializer.Deserialize<JsonElement>(data);
                    if (json.TryGetProperty("type", out var type))
                    {
                        var t = type.GetString();
                        if (t == "content_block_start")
                        {
                            int idx = json.TryGetProperty("index", out var idxProp) ? idxProp.GetInt32() : 0;
                            var cb = json.GetProperty("content_block");
                            var block = new BlockInProgress();
                            blocksInProgress[idx] = block;

                            if (cb.TryGetProperty("type", out var cbType))
                            {
                                var typeStr = cbType.GetString() ?? "";
                                block.Type = typeStr;
                                if (typeStr == "tool_use")
                                {
                                    block.ToolId = cb.GetProperty("id").GetString();
                                    block.ToolName = cb.GetProperty("name").GetString();
                                }
                                else if (typeStr == "thinking")
                                {
                                    if (cb.TryGetProperty("thinking", out var tProp))
                                    {
                                        var text = tProp.GetString() ?? "";
                                        block.Content.Append(text);
                                        var sanitized = reasoningSanitizer.Push(text);
                                        if (!string.IsNullOrEmpty(sanitized))
                                        {
                                            thinkingChunkStr = sanitized;
                                        }
                                    }
                                    if (cb.TryGetProperty("signature", out var sProp))
                                    {
                                        block.Signature.Append(sProp.GetString() ?? "");
                                    }
                                }
                                else if (typeStr == "redacted_thinking")
                                {
                                    block.RawBlock = cb.Clone();
                                }
                                else if (typeStr == "text")
                                {
                                    if (cb.TryGetProperty("text", out var txProp))
                                    {
                                        var text = txProp.GetString() ?? "";
                                        block.Content.Append(text);
                                        var sanitized = visibleSanitizer.Push(text);
                                        if (!string.IsNullOrEmpty(sanitized))
                                        {
                                            chunkStr = sanitized;
                                        }
                                    }
                                }
                            }
                        }
                        else if (t == "content_block_delta")
                        {
                            int idx = json.TryGetProperty("index", out var idxProp) ? idxProp.GetInt32() : 0;
                            if (blocksInProgress.TryGetValue(idx, out var block))
                            {
                                var delta = json.GetProperty("delta");
                                if (delta.TryGetProperty("type", out var deltaType))
                                {
                                    var dt = deltaType.GetString();
                                    if (dt == "text_delta")
                                    {
                                        var text = delta.GetProperty("text").GetString() ?? "";
                                        block.Content.Append(text);
                                        var sanitized = visibleSanitizer.Push(text);
                                        if (!string.IsNullOrEmpty(sanitized))
                                        {
                                            chunkStr = sanitized;
                                        }
                                    }
                                    else if (dt == "thinking_delta")
                                    {
                                        var text = delta.GetProperty("thinking").GetString() ?? "";
                                        block.Content.Append(text);
                                        var sanitized = reasoningSanitizer.Push(text);
                                        if (!string.IsNullOrEmpty(sanitized))
                                        {
                                            thinkingChunkStr = sanitized;
                                        }
                                    }
                                    else if (dt == "signature_delta")
                                    {
                                        var sig = delta.GetProperty("signature").GetString() ?? "";
                                        block.Signature.Append(sig);
                                    }
                                    else if (dt == "input_json_delta")
                                    {
                                        block.ToolArgs.Append(delta.GetProperty("partial_json").GetString());
                                    }
                                }
                            }
                        }
                        else if (t == "content_block_stop")
                        {
                            int idx = json.TryGetProperty("index", out var idxProp) ? idxProp.GetInt32() : 0;
                            if (blocksInProgress.TryGetValue(idx, out var block))
                            {
                                blocksInProgress.Remove(idx);
                                if (block.Type == "thinking")
                                {
                                    var sig = block.Signature.ToString();
                                    if (string.IsNullOrEmpty(sig))
                                    {
                                        replayUnavailable = true;
                                        if (showDebugLog) debugEvt = new ChatEvent { Type = "debug", Data = $"[Main Chat - Anthropic] history item: type=thinking, signature=absent" };
                                    }
                                    else
                                    {
                                        var thoughtObj = new { type = "thinking", thinking = block.Content.ToString(), signature = sig };
                                        var rawJson = JsonSerializer.Serialize(thoughtObj);
                                        if (!replayUnavailable) providerItemEvts.Add(new ChatEvent { Type = "provider_history_item", Data = rawJson });
                                        if (showDebugLog) debugEvt = new ChatEvent { Type = "debug", Data = $"[Main Chat - Anthropic] history item: type=thinking, signature=present ({sig.Length} chars)" };
                                    }
                                }
                                else if (block.Type == "redacted_thinking")
                                {
                                    var rawJson = block.RawBlock.HasValue ? block.RawBlock.Value.GetRawText() : JsonSerializer.Serialize(new { type = "redacted_thinking", data = "" });
                                    if (!replayUnavailable) providerItemEvts.Add(new ChatEvent { Type = "provider_history_item", Data = rawJson });
                                    if (showDebugLog) debugEvt = new ChatEvent { Type = "debug", Data = $"[Main Chat - Anthropic] history item: type=redacted_thinking" };
                                }
                                else if (block.Type == "text")
                                {
                                    var textObj = new { type = "text", text = block.Content.ToString() };
                                    var rawJson = JsonSerializer.Serialize(textObj);
                                    if (!replayUnavailable) providerItemEvts.Add(new ChatEvent { Type = "provider_history_item", Data = rawJson });
                                    if (showDebugLog) debugEvt = new ChatEvent { Type = "debug", Data = $"[Main Chat - Anthropic] history item: type=text ({block.Content.Length} chars)" };
                                }
                                else if (block.Type == "tool_use")
                                {
                                    object argsObj = new { };
                                    try
                                    {
                                        if (block.ToolArgs.Length > 0)
                                            argsObj = JsonSerializer.Deserialize<object>(block.ToolArgs.ToString()) ?? new { };
                                    }
                                    catch { }

                                    var toolObj = new { type = "tool_use", id = block.ToolId, name = block.ToolName, input = argsObj };
                                    var rawJson = JsonSerializer.Serialize(toolObj);
                                    if (!replayUnavailable) providerItemEvts.Add(new ChatEvent { Type = "provider_history_item", Data = rawJson });

                                    var callObj = new { id = block.ToolId, name = block.ToolName, arguments = block.ToolArgs.ToString() };
                                    toolCallEvt = new ChatEvent { Type = "tool_call_complete", Data = JsonSerializer.Serialize(callObj) };
                                    if (showDebugLog) debugEvt = new ChatEvent { Type = "debug", Data = $"[Main Chat - Anthropic] history item: type=tool_use, id={block.ToolId}" };
                                }
                            }
                        }
                        else if (t == "error")
                        {
                            string errMsg = "Unknown stream error";
                            if (json.TryGetProperty("error", out var errObj))
                            {
                                var errType = errObj.TryGetProperty("type", out var et) ? et.GetString() : "unknown";
                                var errMessage = errObj.TryGetProperty("message", out var em) ? em.GetString() : "Unknown error";
                                errMsg = $"Anthropic stream error: [{errType}] {errMessage}";
                            }
                            errorEvt = new ChatEvent { Type = "error", Data = errMsg };
                        }
                        else if (t == "message_start")
                        {
                            if (showDebugLog && json.TryGetProperty("message", out var msg))
                            {
                                var resolvedModel = msg.TryGetProperty("model", out var mp) ? mp.GetString() : "unknown";
                                string usageInfo = "";
                                if (msg.TryGetProperty("usage", out var usage))
                                {
                                    var inputTokens = usage.TryGetProperty("input_tokens", out var it) ? it.GetInt32().ToString() : "?";
                                    usageInfo = $", input_tokens={inputTokens}";
                                }
                                debugEvt = new ChatEvent { Type = "debug", Data = $"[Main Chat - Anthropic] message_start: model={resolvedModel}{usageInfo}" };
                            }
                        }
                        else if (t == "message_delta")
                        {
                            if (showDebugLog && json.TryGetProperty("delta", out var delta2))
                            {
                                var stopReason = delta2.TryGetProperty("stop_reason", out var sr) ? sr.GetString() : "null";
                                string usageInfo = "";
                                if (json.TryGetProperty("usage", out var usage))
                                {
                                    var outputTokens = usage.TryGetProperty("output_tokens", out var ot) ? ot.GetInt32().ToString() : "?";
                                    usageInfo = $", output_tokens={outputTokens}";
                                }
                                debugEvt = new ChatEvent { Type = "debug", Data = $"[Main Chat - Anthropic] message_delta: stop_reason={stopReason}{usageInfo}" };
                            }
                        }
                    }
                }
                catch (JsonException) { }

                if (debugEvt != null) yield return debugEvt;
                if (errorEvt != null) yield return errorEvt;
                foreach (var pEvt in providerItemEvts) yield return pEvt;
                if (!string.IsNullOrEmpty(thinkingChunkStr)) yield return new ChatEvent { Type = "thinking_chunk", Data = thinkingChunkStr };
                if (!string.IsNullOrEmpty(chunkStr)) yield return new ChatEvent { Type = "chunk", Data = chunkStr };
                if (toolCallEvt != null) yield return toolCallEvt;
            }
        }

        var rTail = reasoningSanitizer.Flush();
        if (!string.IsNullOrEmpty(rTail)) yield return new ChatEvent { Type = "thinking_chunk", Data = rTail };

        var vTail = visibleSanitizer.Flush();
        if (!string.IsNullOrEmpty(vTail)) yield return new ChatEvent { Type = "chunk", Data = vTail };

        if (replayUnavailable)
        {
            if (showDebugLog) yield return new ChatEvent { Type = "debug", Data = "[Main Chat - Anthropic] turn not replayable (thinking block without signature) — using reconstruction" };
            yield return new ChatEvent { Type = "provider_history_discard", Data = "" };
        }
    }

    public object FormatMessage(string role, string text, List<SendMessageAttachment>? imageAttachments)
    {
        if (imageAttachments != null && imageAttachments.Count > 0)
        {
            var contentList = new List<object>
            {
                new { type = "text", text = text }
            };
            foreach (var img in imageAttachments)
            {
                contentList.Add(new
                {
                    type = "image",
                    source = new
                    {
                        type = "base64",
                        media_type = img.ContentType,
                        data = img.Base64Data
                    }
                });
            }
            return new { role = role, content = contentList };
        }
        return new { role = role, content = text };
    }

    public List<object> PrepareMessageHistory(List<object> messages)
    {
        return AlternateAnthropicMessages(messages);
    }

    public void AppendAssistantToolCallsToHistory(
        List<object> messageHistory,
        string iterationText,
        List<JsonElement> toolCalls,
        List<JsonElement>? providerHistoryItems = null)
    {
        if (providerHistoryItems != null && providerHistoryItems.Count > 0)
        {
            messageHistory.Add(new { role = "assistant", content = providerHistoryItems });
            var updatedMsg = AlternateAnthropicMessages(messageHistory);
            messageHistory.Clear();
            messageHistory.AddRange(updatedMsg);
            return;
        }

        var contentBlocks = new List<object>();
        if (!string.IsNullOrEmpty(iterationText))
        {
            contentBlocks.Add(new { type = "text", text = iterationText });
        }
        foreach (var tc in toolCalls)
        {
            var id = tc.GetProperty("id").GetString();
            var name = tc.GetProperty("name").GetString();
            var argsStr = tc.GetProperty("arguments").GetString();
            object argsObj = new { };
            try
            {
                if (!string.IsNullOrEmpty(argsStr))
                    argsObj = JsonSerializer.Deserialize<object>(argsStr) ?? new { };
            }
            catch { }
            contentBlocks.Add(new { type = "tool_use", id = id, name = name, input = argsObj });
        }

        messageHistory.Add(new { role = "assistant", content = contentBlocks });

        var updated = AlternateAnthropicMessages(messageHistory);
        messageHistory.Clear();
        messageHistory.AddRange(updated);
    }

    public void AppendToolResultsToHistory(
        List<object> messageHistory,
        List<ProviderToolResult> results)
    {
        var userContentBlocks = new List<object>();
        foreach (var res in results)
        {
            userContentBlocks.Add(new
            {
                type = "tool_result",
                tool_use_id = res.ToolCallId,
                content = res.Content,
                is_error = !res.Success
            });
        }

        messageHistory.Add(new { role = "user", content = userContentBlocks });

        var updated = AlternateAnthropicMessages(messageHistory);
        messageHistory.Clear();
        messageHistory.AddRange(updated);
    }

    public Dictionary<string, object> BuildTitleRequestBody(
        string modelId, string systemPrompt, string userMessage, int maxTokens, string? serviceTier = null)
    {
        var req = new Dictionary<string, object>
        {
            ["model"] = modelId,
            ["system"] = systemPrompt,
            ["messages"] = new List<object>
            {
                new { role = "user", content = userMessage }
            },
            ["max_tokens"] = maxTokens
        };

        if (!string.IsNullOrEmpty(serviceTier) && !string.Equals(serviceTier, "none", StringComparison.OrdinalIgnoreCase))
        {
            req["service_tier"] = serviceTier;
        }

        return req;
    }

    public string GetTitleUrl(string modelId, string apiKey)
    {
        return "https://api.anthropic.com/v1/messages";
    }

    public string? ParseTitleResponse(JsonElement root)
    {
        if (root.TryGetProperty("content", out var contentArray) && contentArray.GetArrayLength() > 0)
        {
            var firstBlock = contentArray[0];
            if (firstBlock.TryGetProperty("text", out var textProp))
            {
                var text = textProp.GetString() ?? "";
                return text.Trim('"', ' ', '\r', '\n');
            }
        }
        return null;
    }

    public object? BuildWebSearchTool()
    {
        return new { type = "web_search_20260318", name = "web_search" };
    }

    public object BuildFunctionDeclaration(string name, string description, object parameterSchema)
    {
        return new
        {
            name = name,
            description = description,
            input_schema = parameterSchema
        };
    }

    public object? BuildToolsPayload(List<object> providerTools, List<object> functionDeclarations)
    {
        var combined = new List<object>();
        combined.AddRange(providerTools);
        combined.AddRange(functionDeclarations);
        return combined.Count > 0 ? combined : null;
    }

    private (string systemContent, List<object> nonSystemMessages) ExtractSystemAndNonSystemMessages(List<object> messages)
    {
        var systemSb = new StringBuilder();
        var nonSystem = new List<object>();

        foreach (var msg in messages)
        {
            var role = ProviderHelper.GetProperty(msg, "role")?.ToString();
            if (role == "system")
            {
                var content = ProviderHelper.GetProperty(msg, "content")?.ToString();
                if (!string.IsNullOrEmpty(content))
                {
                    if (systemSb.Length > 0) systemSb.AppendLine();
                    systemSb.Append(content);
                }
            }
            else
            {
                nonSystem.Add(msg);
            }
        }

        return (systemSb.ToString(), nonSystem);
    }

    private List<object> AlternateAnthropicMessages(List<object> messages)
    {
        var result = new List<object>();
        string? currentRole = null;

        foreach (var msg in messages)
        {
            var role = ProviderHelper.GetProperty(msg, "role")?.ToString();
            if (role == "system") continue;

            if (currentRole == null)
            {
                result.Add(msg);
                currentRole = role;
            }
            else if (currentRole == role)
            {
                if (role == "assistant")
                {
                    result.Add(new { role = "user", content = "Continue" });
                    result.Add(msg);
                }
                else if (role == "user")
                {
                    result.Add(new { role = "assistant", content = "I understand. Please continue." });
                    result.Add(msg);
                }
            }
            else
            {
                result.Add(msg);
                currentRole = role;
            }
        }

        return result;
    }
}
