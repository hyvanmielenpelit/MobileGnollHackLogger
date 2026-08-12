using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

public class GoogleProvider : IAiProvider
{
    private readonly IConfiguration _configuration;

    public GoogleProvider(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public string ProviderName => "Google";

    public void ConfigureRequest(HttpRequestMessage request, string apiKey)
    {
        // Google uses API key in URL query parameter, no header configuration needed
    }

    public string GetChatStreamUrl(string modelId, string apiKey)
    {
        return $"https://generativelanguage.googleapis.com/v1beta/models/{modelId}:streamGenerateContent?alt=sse&key={apiKey}";
    }

    public Dictionary<string, object> BuildChatRequestBody(
        string modelId,
        List<object> messageHistory,
        int? maxOutputTokens,
        string? thinkingLevel,
        ToolsForRequest requestTools)
    {
        var (systemParts, contents) = ExtractSystemAndContents(messageHistory);

        var req = new Dictionary<string, object>
        {
            ["contents"] = contents
        };

        if (systemParts.Count > 0)
        {
            req["systemInstruction"] = new { parts = systemParts };
        }

        var genConfig = new Dictionary<string, object>();
        if (maxOutputTokens.HasValue)
        {
            genConfig["maxOutputTokens"] = maxOutputTokens.Value;
        }

        if (!string.IsNullOrEmpty(thinkingLevel))
        {
            genConfig["thinkingConfig"] = new { thinkingLevel = thinkingLevel.ToUpperInvariant() };
        }

        if (genConfig.Count > 0)
        {
            req["generationConfig"] = genConfig;
        }

        var geminiSafetySettings = _configuration.GetSection("SafetySettings:Gemini").GetChildren().Select(c => new
        {
            category = c.Key,
            threshold = c.Value
        }).ToList();

        if (geminiSafetySettings.Any())
        {
            req["safetySettings"] = geminiSafetySettings;
        }

        var toolsPayload = BuildToolsPayload(requestTools.ProviderTools, requestTools.FunctionDeclarations);
        if (toolsPayload != null)
        {
            req["tools"] = toolsPayload;
            req["toolConfig"] = new Dictionary<string, object>
            {
                ["functionCallingConfig"] = new { mode = "AUTO" },
                ["include_server_side_tool_invocations"] = true
            };
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

        while (!cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (line == null) break;

            if (line.StartsWith("data: "))
            {
                var data = line.Substring(6).Trim();
                string? chunkStr = null;
                string? thinkingChunkStr = null;
                var toolCallEvts = new List<ChatEvent>();
                ChatEvent? errorEvt = null;

                try
                {
                    var json = JsonSerializer.Deserialize<JsonElement>(data);
                    if (json.TryGetProperty("candidates", out var candidates) && candidates.GetArrayLength() > 0)
                    {
                        var cand = candidates[0];
                        if (cand.TryGetProperty("content", out var content) && content.TryGetProperty("parts", out var parts))
                        {
                            foreach (var part in parts.EnumerateArray())
                            {
                                if (part.TryGetProperty("thought", out var thoughtProp) && thoughtProp.GetBoolean() == true)
                                {
                                    if (part.TryGetProperty("text", out var tProp))
                                        thinkingChunkStr = (thinkingChunkStr ?? "") + tProp.GetString();
                                }
                                else if (part.TryGetProperty("text", out var textProp))
                                {
                                    chunkStr = (chunkStr ?? "") + textProp.GetString();
                                }
                                else if (part.TryGetProperty("functionCall", out var fcProp))
                                {
                                    var fname = fcProp.GetProperty("name").GetString();
                                    var fargs = fcProp.GetProperty("args").GetRawText();
                                    var callObj = new { id = Guid.NewGuid().ToString(), name = fname, arguments = fargs, raw_part = part };
                                    toolCallEvts.Add(new ChatEvent { Type = "tool_call_complete", Data = JsonSerializer.Serialize(callObj) });
                                }
                            }
                        }
                    }
                    else if (json.TryGetProperty("error", out var errObj))
                    {
                        var errMessage = errObj.TryGetProperty("message", out var em) ? em.GetString() : "Unknown error";
                        var errCode = errObj.TryGetProperty("code", out var ec) ? ec.GetInt32().ToString() : "unknown";
                        errorEvt = new ChatEvent { Type = "error", Data = $"Google stream error: [{errCode}] {errMessage}" };
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
        string mappedRole = role.Equals("assistant", StringComparison.OrdinalIgnoreCase) || role.Equals("model", StringComparison.OrdinalIgnoreCase)
            ? "model"
            : "user";

        if (imageAttachments != null && imageAttachments.Count > 0)
        {
            var partsList = new List<object>
            {
                new { text = text }
            };
            foreach (var img in imageAttachments)
            {
                partsList.Add(new
                {
                    inlineData = new
                    {
                        mimeType = img.ContentType,
                        data = img.Base64Data
                    }
                });
            }
            return new { role = mappedRole, parts = partsList };
        }
        return new { role = mappedRole, parts = new[] { new { text = text } } };
    }

    public List<object> PrepareMessageHistory(List<object> messages)
    {
        var formatted = new List<object>();

        foreach (var msg in messages)
        {
            var role = ProviderHelper.GetProperty(msg, "role")?.ToString() ?? "user";
            var partsProp = ProviderHelper.GetProperty(msg, "parts");

            if (partsProp != null)
            {
                formatted.Add(msg);
            }
            else
            {
                var content = ProviderHelper.GetProperty(msg, "content")?.ToString() ?? "";
                var mappedRole = (role == "assistant" || role == "model") ? "model" : role;
                formatted.Add(new { role = mappedRole, parts = new[] { new { text = content } } });
            }
        }

        return formatted;
    }

    public void AppendAssistantToolCallsToHistory(
        List<object> messageHistory,
        string iterationText,
        List<JsonElement> toolCalls)
    {
        var modelParts = new List<object>();
        if (!string.IsNullOrEmpty(iterationText))
        {
            modelParts.Add(new { text = iterationText });
        }

        foreach (var tc in toolCalls)
        {
            if (tc.TryGetProperty("raw_part", out var rawPart))
            {
                try
                {
                    var rawObj = JsonSerializer.Deserialize<object>(rawPart.GetRawText());
                    if (rawObj != null)
                    {
                        modelParts.Add(rawObj);
                        continue;
                    }
                }
                catch { }
            }

            var name = tc.GetProperty("name").GetString();
            var argsStr = tc.GetProperty("arguments").GetString();
            object argsObj = new { };
            try
            {
                if (!string.IsNullOrEmpty(argsStr))
                    argsObj = JsonSerializer.Deserialize<object>(argsStr) ?? new { };
            }
            catch { }

            modelParts.Add(new
            {
                functionCall = new
                {
                    name = name,
                    args = argsObj
                }
            });
        }

        messageHistory.Add(new { role = "model", parts = modelParts });
    }

    public void AppendToolResultsToHistory(
        List<object> messageHistory,
        List<ProviderToolResult> results)
    {
        var userParts = new List<object>();
        foreach (var res in results)
        {
            object parsedResponse = res.Content;
            try
            {
                parsedResponse = JsonSerializer.Deserialize<object>(res.Content) ?? res.Content;
            }
            catch { }

            userParts.Add(new
            {
                functionResponse = new
                {
                    name = res.ToolName,
                    response = new
                    {
                        name = res.ToolName,
                        content = parsedResponse
                    }
                }
            });
        }

        messageHistory.Add(new { role = "user", parts = userParts });
    }

    public Dictionary<string, object> BuildTitleRequestBody(
        string modelId, string systemPrompt, string userMessage, int maxTokens)
    {
        return new Dictionary<string, object>
        {
            ["contents"] = new List<object>
            {
                new { role = "user", parts = new[] { new { text = $"{systemPrompt}\n\nUser Message: {userMessage}" } } }
            },
            ["generationConfig"] = new { maxOutputTokens = maxTokens }
        };
    }

    public string GetTitleUrl(string modelId, string apiKey)
    {
        return $"https://generativelanguage.googleapis.com/v1beta/models/{modelId}:generateContent?key={apiKey}";
    }

    public string? ParseTitleResponse(JsonElement root)
    {
        if (root.TryGetProperty("candidates", out var candidatesArray) && candidatesArray.GetArrayLength() > 0)
        {
            var firstCandidate = candidatesArray[0];
            if (firstCandidate.TryGetProperty("content", out var cObj) &&
                cObj.TryGetProperty("parts", out var partsArr) && partsArr.GetArrayLength() > 0)
            {
                var firstPart = partsArr[0];
                if (firstPart.TryGetProperty("text", out var textProp))
                {
                    var text = textProp.GetString() ?? "";
                    return text.Trim('"', ' ', '\r', '\n');
                }
            }
        }
        return null;
    }

    public object? BuildWebSearchTool()
    {
        return new { googleSearch = new { } };
    }

    public object BuildFunctionDeclaration(string name, string description, object parameterSchema)
    {
        return new
        {
            name = name,
            description = description,
            parameters = parameterSchema
        };
    }

    public object? BuildToolsPayload(List<object> providerTools, List<object> functionDeclarations)
    {
        var toolsList = new List<object>();
        toolsList.AddRange(providerTools);
        if (functionDeclarations.Count > 0)
        {
            toolsList.Add(new { functionDeclarations = functionDeclarations });
        }
        return toolsList.Count > 0 ? toolsList : null;
    }

    private (List<object> systemParts, List<object> contents) ExtractSystemAndContents(List<object> messages)
    {
        var systemParts = new List<object>();
        var contents = new List<object>();

        foreach (var msg in messages)
        {
            var role = ProviderHelper.GetProperty(msg, "role")?.ToString() ?? "user";
            if (role == "system")
            {
                var parts = ProviderHelper.GetProperty(msg, "parts");
                if (parts != null)
                {
                    if (parts is System.Collections.IEnumerable enumParts)
                    {
                        foreach (var p in enumParts) systemParts.Add(p);
                    }
                }
                else
                {
                    var content = ProviderHelper.GetProperty(msg, "content")?.ToString() ?? "";
                    systemParts.Add(new { text = content });
                }
            }
            else
            {
                contents.Add(msg);
            }
        }

        return (systemParts, contents);
    }
}
