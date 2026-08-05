using System;
using System.Collections.Generic;
using System.IO;
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

public class OpenAiProvider : IAiProvider
{
    public string ProviderName => "OpenAI";

    public void ConfigureRequest(HttpRequestMessage request, string apiKey)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
    }

    public string GetChatStreamUrl(string modelId, string apiKey)
    {
        return "https://api.openai.com/v1/chat/completions";
    }

    public Dictionary<string, object> BuildChatRequestBody(
        string modelId,
        List<object> messageHistory,
        int? maxOutputTokens,
        string? thinkingLevel,
        ToolsForRequest requestTools)
    {
        var req = new Dictionary<string, object>
        {
            ["model"] = modelId,
            ["messages"] = messageHistory,
            ["stream"] = true
        };

        if (maxOutputTokens.HasValue)
        {
            req["max_tokens"] = maxOutputTokens.Value;
        }

        if (!string.IsNullOrEmpty(thinkingLevel))
        {
            req["reasoning_effort"] = thinkingLevel;
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

        var toolCallsInProgress = new Dictionary<int, (string id, string name, StringBuilder args)>();

        while (!cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (line == null) break;

            if (line.StartsWith("data: "))
            {
                var data = line.Substring(6).Trim();
                if (data == "[DONE]") break;

                string? chunkStr = null;
                string? thinkingChunkStr = null;
                var toolCallEvts = new List<ChatEvent>();
                ChatEvent? errorEvt = null;

                try
                {
                    var json = JsonSerializer.Deserialize<JsonElement>(data);

                    if (json.TryGetProperty("error", out var errObj))
                    {
                        var errMessage = errObj.TryGetProperty("message", out var em) ? em.GetString() : "Unknown error";
                        var errType = errObj.TryGetProperty("type", out var et) ? et.GetString() : "unknown";
                        errorEvt = new ChatEvent { Type = "error", Data = $"OpenAI stream error: [{errType}] {errMessage}" };
                    }
                    else if (json.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
                    {
                        var choice = choices[0];
                        if (choice.TryGetProperty("delta", out var delta))
                        {
                            if (delta.TryGetProperty("content", out var contentProp) && contentProp.ValueKind == JsonValueKind.String)
                            {
                                chunkStr = contentProp.GetString();
                            }

                            if (delta.TryGetProperty("thinking", out var thinkingProp) && thinkingProp.ValueKind == JsonValueKind.String)
                            {
                                thinkingChunkStr = thinkingProp.GetString();
                            }
                            else if (delta.TryGetProperty("reasoning_content", out var reasoningProp) && reasoningProp.ValueKind == JsonValueKind.String)
                            {
                                thinkingChunkStr = reasoningProp.GetString();
                            }

                            if (delta.TryGetProperty("tool_calls", out var toolCalls))
                            {
                                foreach (var tc in toolCalls.EnumerateArray())
                                {
                                    int index = tc.GetProperty("index").GetInt32();
                                    if (!toolCallsInProgress.ContainsKey(index))
                                    {
                                        toolCallsInProgress[index] = ("", "", new StringBuilder());
                                    }

                                    var current = toolCallsInProgress[index];
                                    if (tc.TryGetProperty("id", out var idProp))
                                        current.id = idProp.GetString() ?? "";
                                    if (tc.TryGetProperty("function", out var funcProp))
                                    {
                                        if (funcProp.TryGetProperty("name", out var nameProp))
                                            current.name = nameProp.GetString() ?? "";
                                        if (funcProp.TryGetProperty("arguments", out var argsProp))
                                            current.args.Append(argsProp.GetString());
                                    }
                                    toolCallsInProgress[index] = current;
                                }
                            }
                        }

                        if (choice.TryGetProperty("finish_reason", out var finishReasonProp) && finishReasonProp.ValueKind == JsonValueKind.String)
                        {
                            var finishReason = finishReasonProp.GetString();
                            if (finishReason == "tool_calls" || (finishReason == "stop" && toolCallsInProgress.Count > 0))
                            {
                                foreach (var kvp in toolCallsInProgress)
                                {
                                    var callObj = new { id = kvp.Value.id, name = kvp.Value.name, arguments = kvp.Value.args.ToString() };
                                    toolCallEvts.Add(new ChatEvent { Type = "tool_call_complete", Data = JsonSerializer.Serialize(callObj) });
                                }
                                toolCallsInProgress.Clear();
                            }
                        }
                    }
                }
                catch (JsonException) { }

                if (errorEvt != null) yield return errorEvt;
                if (!string.IsNullOrEmpty(thinkingChunkStr)) yield return new ChatEvent { Type = "thinking_chunk", Data = thinkingChunkStr };
                if (!string.IsNullOrEmpty(chunkStr)) yield return new ChatEvent { Type = "chunk", Data = chunkStr };
                foreach (var evt in toolCallEvts) yield return evt;
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
                    type = "image_url",
                    image_url = new { url = $"data:{img.ContentType};base64,{img.Base64Data}" }
                });
            }
            return new { role = role, content = contentList };
        }
        return new { role = role, content = text };
    }

    public List<object> PrepareMessageHistory(List<object> messages)
    {
        return messages;
    }

    public void AppendAssistantToolCallsToHistory(
        List<object> messageHistory,
        string iterationText,
        List<JsonElement> toolCalls)
    {
        var formattedToolCalls = new List<object>();
        foreach (var tc in toolCalls)
        {
            var id = tc.GetProperty("id").GetString();
            var name = tc.GetProperty("name").GetString();
            var args = tc.GetProperty("arguments").GetString();
            formattedToolCalls.Add(new
            {
                id = id,
                type = "function",
                function = new { name = name, arguments = args }
            });
        }
        var msg = new Dictionary<string, object>
        {
            ["role"] = "assistant",
            ["content"] = iterationText,
            ["tool_calls"] = formattedToolCalls
        };
        messageHistory.Add(msg);
    }

    public void AppendToolResultsToHistory(
        List<object> messageHistory,
        List<ProviderToolResult> results)
    {
        foreach (var res in results)
        {
            messageHistory.Add(new
            {
                role = "tool",
                tool_call_id = res.ToolCallId,
                content = res.Content
            });
        }
    }

    public Dictionary<string, object> BuildTitleRequestBody(
        string modelId, string systemPrompt, string userMessage, int maxTokens)
    {
        return new Dictionary<string, object>
        {
            ["model"] = modelId,
            ["messages"] = new List<object>
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userMessage }
            },
            ["max_tokens"] = maxTokens
        };
    }

    public string GetTitleUrl(string modelId, string apiKey)
    {
        return "https://api.openai.com/v1/chat/completions";
    }

    public string? ParseTitleResponse(JsonElement root)
    {
        if (root.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
        {
            var firstChoice = choices[0];
            if (firstChoice.TryGetProperty("message", out var msgObj) && msgObj.TryGetProperty("content", out var contentProp))
            {
                var text = contentProp.GetString() ?? "";
                return text.Trim('"', ' ', '\r', '\n');
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
            function = new
            {
                name = name,
                description = description,
                parameters = parameterSchema
            }
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
