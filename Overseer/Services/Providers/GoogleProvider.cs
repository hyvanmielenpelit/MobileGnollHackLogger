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
    public static readonly IReadOnlyList<string> ProviderHosts = new[]
    {
        "generativelanguage.googleapis.com"
    };

    private readonly IConfiguration _configuration;

    public GoogleProvider(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public string ProviderName => "Google";

    public IReadOnlyList<string> SupportedServiceTiers => new[] { "priority", "flex", "standard" };

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
        ToolsForRequest requestTools,
        string? reasoningMode = null,
        string? reasoningSummary = null,
        string? serviceTier = null,
        bool? parallelToolCalls = null,
        SegmentedPrompt? segmentedPrompt = null,
        string? promptCacheKey = null)
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
        int? configuredDefault = _configuration?.GetValue<int?>("DefaultMaxOutputTokens:Google");
        int? effectiveMaxTokens = maxOutputTokens ?? configuredDefault;
        if (effectiveMaxTokens.HasValue)
        {
            genConfig["maxOutputTokens"] = effectiveMaxTokens.Value;
        }

        if (!string.IsNullOrEmpty(thinkingLevel))
        {
            genConfig["thinkingConfig"] = new { thinkingLevel = thinkingLevel.ToUpperInvariant() };
        }

        if (genConfig.Count > 0)
        {
            req["generationConfig"] = genConfig;
        }

        if (!string.IsNullOrEmpty(serviceTier) && !string.Equals(serviceTier, "none", StringComparison.OrdinalIgnoreCase))
        {
            req["service_tier"] = serviceTier;
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

        yield return new ChatEvent { Type = "provider_history_reset", Data = "" };

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
                var toolCallEvts = new List<ChatEvent>();
                var debugEvts = new List<ChatEvent>();
                var providerItemEvts = new List<ChatEvent>();
                var usageEvts = new List<ChatEvent>();
                ChatEvent? errorEvt = null;

                try
                {
                    var json = JsonSerializer.Deserialize<JsonElement>(data);
                    if (json.TryGetProperty("candidates", out var candidates) && candidates.GetArrayLength() > 0)
                    {
                        var cand = candidates[0];
                        if (cand.TryGetProperty("finishReason", out var frProp) && frProp.ValueKind == JsonValueKind.String)
                        {
                            var finishReason = frProp.GetString() ?? "";
                            if (finishReason == "MAX_TOKENS")
                            {
                                debugEvts.Add(new ChatEvent { Type = "debug", Data = "[Google] Response incomplete: finishReason=MAX_TOKENS" });
                            }
                            if (showDebugLog && !string.IsNullOrEmpty(finishReason))
                            {
                                debugEvts.Add(new ChatEvent { Type = "debug", Data = $"[Main Chat - Google] candidate finishReason={finishReason}" });
                            }
                        }

                        if (cand.TryGetProperty("content", out var content) && content.TryGetProperty("parts", out var parts))
                        {
                            foreach (var part in parts.EnumerateArray())
                            {
                                if (part.TryGetProperty("thought", out var thoughtProp) && thoughtProp.GetBoolean() == true)
                                {
                                    if (!part.TryGetProperty("thoughtSignature", out var tsProp) || tsProp.ValueKind != JsonValueKind.String || string.IsNullOrEmpty(tsProp.GetString()))
                                    {
                                        replayUnavailable = true;
                                        if (showDebugLog) debugEvts.Add(new ChatEvent { Type = "debug", Data = "[Main Chat - Google] history item: part=thought, thoughtSignature=absent" });
                                    }
                                    else
                                    {
                                        if (!replayUnavailable)
                                        {
                                            providerItemEvts.Add(new ChatEvent { Type = "provider_history_item", Data = part.GetRawText() });
                                        }
                                        if (showDebugLog) debugEvts.Add(new ChatEvent { Type = "debug", Data = "[Main Chat - Google] history item: part=thought, thoughtSignature=present" });
                                    }

                                    if (part.TryGetProperty("text", out var tProp))
                                    {
                                        var text = tProp.GetString() ?? "";
                                        var sanitized = reasoningSanitizer.Push(text);
                                        if (!string.IsNullOrEmpty(sanitized))
                                        {
                                            thinkingChunkStr = (thinkingChunkStr ?? "") + sanitized;
                                        }
                                    }
                                }
                                else if (part.TryGetProperty("text", out var textProp))
                                {
                                    if (!replayUnavailable)
                                    {
                                        providerItemEvts.Add(new ChatEvent { Type = "provider_history_item", Data = part.GetRawText() });
                                    }
                                    var text = textProp.GetString() ?? "";
                                    var sanitized = visibleSanitizer.Push(text);
                                    if (!string.IsNullOrEmpty(sanitized))
                                    {
                                        chunkStr = (chunkStr ?? "") + sanitized;
                                    }
                                    if (showDebugLog) debugEvts.Add(new ChatEvent { Type = "debug", Data = $"[Main Chat - Google] history item: part=text ({text.Length} chars)" });
                                }
                                else if (part.TryGetProperty("functionCall", out var fcProp))
                                {
                                    if (!replayUnavailable)
                                    {
                                        providerItemEvts.Add(new ChatEvent { Type = "provider_history_item", Data = part.GetRawText() });
                                    }
                                    var fname = fcProp.GetProperty("name").GetString();
                                    var fargs = fcProp.GetProperty("args").GetRawText();
                                    string? providerId = fcProp.TryGetProperty("id", out var fid) ? fid.GetString() : null;
                                    var callObj = new { id = Guid.NewGuid().ToString(), name = fname, arguments = fargs, raw_part = part, provider_id = providerId };
                                    toolCallEvts.Add(new ChatEvent { Type = "tool_call_complete", Data = JsonSerializer.Serialize(callObj) });
                                    if (showDebugLog) debugEvts.Add(new ChatEvent { Type = "debug", Data = $"[Main Chat - Google] history item: part=functionCall, name={fname}" });
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

                    if (json.TryGetProperty("usageMetadata", out var usageProp))
                    {
                        int promptTokens = usageProp.TryGetProperty("promptTokenCount", out var pt) ? pt.GetInt32() : 0;
                        int outputTokens = usageProp.TryGetProperty("candidatesTokenCount", out var ct) ? ct.GetInt32() : 0;
                        int cachedTokens = usageProp.TryGetProperty("cachedContentTokenCount", out var cct) ? cct.GetInt32() : 0;
                        int thoughtTokens = usageProp.TryGetProperty("thoughtsTokenCount", out var tht) ? tht.GetInt32() : 0;
                        int uncached = Math.Max(0, promptTokens - cachedTokens);

                        var report = new TokenUsageReport
                        {
                            TotalPromptTokens = promptTokens,
                            CacheReadTokens = cachedTokens,
                            CacheCreationTokens = 0,
                            UncachedInputTokens = uncached,
                            OutputTokens = outputTokens,
                            ReasoningTokens = thoughtTokens
                        };
                        usageEvts.Add(new ChatEvent
                        {
                            Type = "usage",
                            Data = JsonSerializer.Serialize(report),
                            UsageReport = report
                        });

                        if (showDebugLog)
                        {
                            var totalTokens = usageProp.TryGetProperty("totalTokenCount", out var tt) ? tt.ToString() : (promptTokens + outputTokens).ToString();
                            string thoughtTokensStr = thoughtTokens > 0 ? $", thought_tokens={thoughtTokens}" : "";
                            string cachedTokensStr = cachedTokens > 0 ? $", cached_tokens={cachedTokens}" : "";
                            debugEvts.Add(new ChatEvent { Type = "debug", Data = $"[Main Chat - Google] usage: prompt_tokens={promptTokens}, output_tokens={outputTokens}, total_tokens={totalTokens}{thoughtTokensStr}{cachedTokensStr}" });
                        }
                    }
                }
                catch (JsonException) { }

                foreach (var dbg in debugEvts) yield return dbg;
                if (errorEvt != null) yield return errorEvt;
                foreach (var pEvt in providerItemEvts) yield return pEvt;
                foreach (var uEvt in usageEvts) yield return uEvt;
                if (!string.IsNullOrEmpty(thinkingChunkStr)) yield return new ChatEvent { Type = "thinking_chunk", Data = thinkingChunkStr };
                if (!string.IsNullOrEmpty(chunkStr)) yield return new ChatEvent { Type = "chunk", Data = chunkStr };
                foreach (var evt in toolCallEvts) yield return evt;
            }
        }

        var rTail = reasoningSanitizer.Flush();
        if (!string.IsNullOrEmpty(rTail)) yield return new ChatEvent { Type = "thinking_chunk", Data = rTail };

        var vTail = visibleSanitizer.Flush();
        if (!string.IsNullOrEmpty(vTail)) yield return new ChatEvent { Type = "chunk", Data = vTail };

        if (replayUnavailable)
        {
            if (showDebugLog) yield return new ChatEvent { Type = "debug", Data = "[Main Chat - Google] turn not replayable (thought part without thoughtSignature) — using reconstruction" };
            yield return new ChatEvent { Type = "provider_history_discard", Data = "" };
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
        List<JsonElement> toolCalls,
        List<JsonElement>? providerHistoryItems = null)
    {
        if (providerHistoryItems != null && providerHistoryItems.Count > 0)
        {
            messageHistory.Add(new { role = "model", parts = providerHistoryItems });
            return;
        }

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

            if (!string.IsNullOrEmpty(res.ProviderToolCallId))
            {
                userParts.Add(new
                {
                    functionResponse = new
                    {
                        id = res.ProviderToolCallId,
                        name = res.ToolName,
                        response = new
                        {
                            name = res.ToolName,
                            content = parsedResponse
                        }
                    }
                });
            }
            else
            {
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
        }

        messageHistory.Add(new { role = "user", parts = userParts });
    }

    public bool TryRewriteToolResult(List<object> messageHistory, string toolCallId, string replacementText)
    {
        for (int i = 0; i < messageHistory.Count; i++)
        {
            var msgObj = messageHistory[i];
            var parts = ProviderHelper.GetProperty(msgObj, "parts") as IEnumerable<object>;
            if (parts == null && msgObj is JsonElement je && je.ValueKind == JsonValueKind.Object && je.TryGetProperty("parts", out var partsProp) && partsProp.ValueKind == JsonValueKind.Array)
            {
                var newParts = new List<object>();
                bool rewritten = false;
                foreach (var part in partsProp.EnumerateArray())
                {
                    if (part.TryGetProperty("functionResponse", out var frProp))
                    {
                        string? fid = frProp.TryGetProperty("id", out var fidProp) ? fidProp.GetString() : null;
                        string? fname = frProp.TryGetProperty("name", out var fnameProp) ? fnameProp.GetString() : null;
                        if (fid == toolCallId || fname == toolCallId)
                        {
                            rewritten = true;
                            if (!string.IsNullOrEmpty(fid))
                            {
                                newParts.Add(new
                                {
                                    functionResponse = new
                                    {
                                        id = fid,
                                        name = fname,
                                        response = new
                                        {
                                            name = fname,
                                            content = replacementText
                                        }
                                    }
                                });
                            }
                            else
                            {
                                newParts.Add(new
                                {
                                    functionResponse = new
                                    {
                                        name = fname,
                                        response = new
                                        {
                                            name = fname,
                                            content = replacementText
                                        }
                                    }
                                });
                            }
                            continue;
                        }
                    }
                    newParts.Add(part);
                }
                if (rewritten)
                {
                    messageHistory[i] = new { role = "user", parts = newParts };
                    return true;
                }
            }
            else if (parts != null)
            {
                var partsList = parts.ToList();
                bool rewritten = false;
                for (int p = 0; p < partsList.Count; p++)
                {
                    var fr = ProviderHelper.GetProperty(partsList[p], "functionResponse");
                    if (fr != null)
                    {
                        string? fid = ProviderHelper.GetProperty(fr, "id")?.ToString();
                        string? fname = ProviderHelper.GetProperty(fr, "name")?.ToString();
                        if (fid == toolCallId || fname == toolCallId)
                        {
                            rewritten = true;
                            if (!string.IsNullOrEmpty(fid))
                            {
                                partsList[p] = new
                                {
                                    functionResponse = new
                                    {
                                        id = fid,
                                        name = fname,
                                        response = new
                                        {
                                            name = fname,
                                            content = replacementText
                                        }
                                    }
                                };
                            }
                            else
                            {
                                partsList[p] = new
                                {
                                    functionResponse = new
                                    {
                                        name = fname,
                                        response = new
                                        {
                                            name = fname,
                                            content = replacementText
                                        }
                                    }
                                };
                            }
                        }
                    }
                }
                if (rewritten)
                {
                    messageHistory[i] = new { role = "user", parts = partsList };
                    return true;
                }
            }
        }
        return false;
    }

    public Dictionary<string, object> BuildTitleRequestBody(
        string modelId, string systemPrompt, string userMessage, int maxTokens, string? serviceTier = null)
    {
        var req = new Dictionary<string, object>
        {
            ["contents"] = new List<object>
            {
                new { role = "user", parts = new[] { new { text = $"{systemPrompt}\n\nUser Message: {userMessage}" } } }
            },
            ["generationConfig"] = new { maxOutputTokens = maxTokens }
        };

        if (!string.IsNullOrEmpty(serviceTier) && !string.Equals(serviceTier, "none", StringComparison.OrdinalIgnoreCase))
        {
            req["service_tier"] = serviceTier;
        }

        return req;
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
