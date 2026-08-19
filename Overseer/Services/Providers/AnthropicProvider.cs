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
        string? reasoningSummary = null)
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

        string? currentToolId = null;
        string? currentToolName = null;
        var currentToolArgs = new StringBuilder();

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

                try
                {
                    var json = JsonSerializer.Deserialize<JsonElement>(data);
                    if (json.TryGetProperty("type", out var type))
                    {
                        var t = type.GetString();
                        if (t == "content_block_start")
                        {
                            var cb = json.GetProperty("content_block");
                            if (cb.TryGetProperty("type", out var cbType))
                            {
                                if (cbType.GetString() == "tool_use")
                                {
                                    currentToolId = cb.GetProperty("id").GetString();
                                    currentToolName = cb.GetProperty("name").GetString();
                                    currentToolArgs.Clear();
                                }
                                else if (cbType.GetString() == "thinking")
                                {
                                    if (cb.TryGetProperty("thinking", out var tProp))
                                    {
                                        thinkingChunkStr = tProp.GetString() ?? "";
                                    }
                                }
                            }
                        }
                        else if (t == "content_block_delta")
                        {
                            var delta = json.GetProperty("delta");
                            if (delta.TryGetProperty("type", out var deltaType))
                            {
                                if (deltaType.GetString() == "text_delta")
                                {
                                    chunkStr = delta.GetProperty("text").GetString();
                                }
                                else if (deltaType.GetString() == "thinking_delta")
                                {
                                    thinkingChunkStr = delta.GetProperty("thinking").GetString() ?? "";
                                }
                                else if (deltaType.GetString() == "input_json_delta")
                                {
                                    currentToolArgs.Append(delta.GetProperty("partial_json").GetString());
                                }
                            }
                        }
                        else if (t == "content_block_stop")
                        {
                            if (currentToolId != null && currentToolName != null)
                            {
                                var callObj = new { id = currentToolId, name = currentToolName, arguments = currentToolArgs.ToString() };
                                toolCallEvt = new ChatEvent { Type = "tool_call_complete", Data = JsonSerializer.Serialize(callObj) };
                                currentToolId = null;
                                currentToolName = null;
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
                if (!string.IsNullOrEmpty(thinkingChunkStr)) yield return new ChatEvent { Type = "thinking_chunk", Data = thinkingChunkStr };
                if (!string.IsNullOrEmpty(chunkStr)) yield return new ChatEvent { Type = "chunk", Data = chunkStr };
                if (toolCallEvt != null) yield return toolCallEvt;
            }
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
        List<JsonElement> toolCalls)
    {
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
                content = res.Content
            });
        }

        messageHistory.Add(new { role = "user", content = userContentBlocks });

        var updated = AlternateAnthropicMessages(messageHistory);
        messageHistory.Clear();
        messageHistory.AddRange(updated);
    }

    public Dictionary<string, object> BuildTitleRequestBody(
        string modelId, string systemPrompt, string userMessage, int maxTokens)
    {
        return new Dictionary<string, object>
        {
            ["model"] = modelId,
            ["system"] = systemPrompt,
            ["messages"] = new List<object>
            {
                new { role = "user", content = userMessage }
            },
            ["max_tokens"] = maxTokens
        };
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
