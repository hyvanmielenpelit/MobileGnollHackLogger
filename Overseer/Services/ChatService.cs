using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using MobileGnollHackLogger.Data;
using Microsoft.EntityFrameworkCore;
using Overseer.Controllers;
using Microsoft.AspNetCore.SignalR;
using Overseer.Hubs;
using Overseer.Services.Tools;

namespace Overseer.Services;

public class ChatService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly WikiService _wikiService;
    private readonly CryptoService _cryptoService;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly IHubContext<ChatHub> _hubContext;
    private readonly ModelMetadataService _modelMetadataService;
    private readonly ToolRegistry _toolRegistry;
    private readonly ToolExecutor _toolExecutor;

    public ChatService(IServiceScopeFactory scopeFactory, WikiService wikiService, CryptoService cryptoService, IHttpClientFactory httpClientFactory, IConfiguration configuration, IHubContext<ChatHub> hubContext, ModelMetadataService modelMetadataService, ToolRegistry toolRegistry, ToolExecutor toolExecutor)
    {
        _scopeFactory = scopeFactory;
        _wikiService = wikiService;
        _cryptoService = cryptoService;
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _hubContext = hubContext;
        _modelMetadataService = modelMetadataService;
        _toolRegistry = toolRegistry;
        _toolExecutor = toolExecutor;
    }

    public static string SanitizeSnapshotForLlm(string html)
    {
        if (string.IsNullOrWhiteSpace(html)) return string.Empty;
        var text = System.Text.RegularExpressions.Regex.Replace(html, "<[^>]*>", " ");
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ");
        text = System.Net.WebUtility.HtmlDecode(text);
        return text.Trim();
    }

    public async Task GenerateAndBroadcastMessageAsync(long sessionId, string message, List<SendMessageAttachment>? attachments, string userId, bool isHidden, CancellationToken cancellationToken, long? userModelId = null)
    {
        try
        {
            await foreach (var evt in StreamMessageAsync(sessionId, message, attachments, userId, isHidden, cancellationToken, userModelId))
            {
                await _hubContext.Clients.Group(sessionId.ToString()).SendAsync("ReceiveChatEvent", evt, cancellationToken);
            }
            await _hubContext.Clients.Group(sessionId.ToString()).SendAsync("ReceiveChatEvent", new ChatEvent { Type = "done", Data = "" }, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await _hubContext.Clients.Group(sessionId.ToString()).SendAsync("ReceiveChatEvent", new ChatEvent { Type = "error", Data = ex.Message }, cancellationToken);
        }
    }

    public async IAsyncEnumerable<ChatEvent> StreamMessageAsync(long? sessionId, string message, List<SendMessageAttachment>? attachments, string userId, bool isHidden, [EnumeratorCancellation] CancellationToken cancellationToken, long? userModelId = null)
    {
        if (string.IsNullOrEmpty(userId)) yield break;

        long currentSessionId = 0;
        string? apiKey = null;
        string provider = "OpenAI";
        string model = "gpt-4o-mini";
        string? thinkingLevel = null;
        List<object> messageHistory = new();
        bool spoilerFreeMode = false;
        bool isGameOn = false;
        bool enableWebSearch = true;
        bool enableToolUse = true;
        bool enableClientTools = true;
        bool enableGameActions = false;
        int? maxInputTokens = null;
        int? maxOutputTokens = null;
        int overseerMode = 0;
        bool isGnollHackSession = false;

        using (var scope = _scopeFactory.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var settings = await dbContext.UserAiSettings.FindAsync(userId);
            bool allowMultipleModels = false;
            
            if (settings != null)
            {
                if (!string.IsNullOrEmpty(settings.DefaultProvider)) provider = settings.DefaultProvider;
                if (!string.IsNullOrEmpty(settings.DefaultModel)) model = settings.DefaultModel;
                if (!string.IsNullOrEmpty(settings.ThinkingLevel)) thinkingLevel = settings.ThinkingLevel;
                spoilerFreeMode = settings.SpoilerFreeMode;
                maxInputTokens = settings.MaxInputTokens;
                maxOutputTokens = settings.MaxOutputTokens;
                enableWebSearch = settings.EnableWebSearch;
                enableToolUse = settings.EnableToolUse;
                enableClientTools = settings.EnableClientTools;
                enableGameActions = settings.EnableGameActions;
                allowMultipleModels = settings.AllowMultipleModels;
            }

            if (sessionId.HasValue)
            {
                var tempSession = await dbContext.ChatSession.FindAsync(sessionId.Value);
                if (tempSession != null && tempSession.IsGnollHackSession)
                {
                    userModelId = null; // force first model because GnollHack doesn't support model selection
                }
            }

            if (userModelId.HasValue)
            {
                var userModel = await dbContext.UserAiModels.FirstOrDefaultAsync(m => m.Id == userModelId.Value && m.AspNetUserId == userId);
                if (userModel != null)
                {
                    provider = userModel.Provider;
                    model = userModel.ModelId;
                    thinkingLevel = userModel.ThinkingLevel;
                    if (userModel.MaxInputTokens.HasValue) maxInputTokens = userModel.MaxInputTokens.Value;
                    if (userModel.MaxOutputTokens.HasValue) maxOutputTokens = userModel.MaxOutputTokens.Value;
                }
            }
            else if (!userModelId.HasValue || !allowMultipleModels)
            {
                var firstModel = await dbContext.UserAiModels
                    .Where(m => m.AspNetUserId == userId)
                    .OrderBy(m => m.OrderIndex)
                    .FirstOrDefaultAsync();
                
                if (firstModel != null)
                {
                    provider = firstModel.Provider;
                    model = firstModel.ModelId;
                    thinkingLevel = firstModel.ThinkingLevel;
                }
            }

            var providerKey = await dbContext.UserAiApiKeys.FirstOrDefaultAsync(k => k.AspNetUserId == userId && k.Provider == provider);
            if (providerKey != null && !string.IsNullOrEmpty(providerKey.EncryptedApiKey) && !string.IsNullOrEmpty(providerKey.ApiKeyNonce) && !string.IsNullOrEmpty(providerKey.ApiKeyTag))
            {
                apiKey = _cryptoService.Decrypt(providerKey.EncryptedApiKey, providerKey.ApiKeyNonce, providerKey.ApiKeyTag, userId);
            }
            else if (settings != null && settings.DefaultProvider == provider && !string.IsNullOrEmpty(settings.EncryptedApiKey) && !string.IsNullOrEmpty(settings.ApiKeyNonce) && !string.IsNullOrEmpty(settings.ApiKeyTag))
            {
                apiKey = _cryptoService.Decrypt(settings.EncryptedApiKey, settings.ApiKeyNonce, settings.ApiKeyTag, userId);
            }

            if (string.IsNullOrEmpty(apiKey))
            {
                yield return new ChatEvent { Type = "error", Data = $"Error: API Key not configured for {provider}. Please configure it in the API Keys page." };
                yield break;
            }

            bool hasGameSnapshot = false;
            bool hasMessageHistory = false;

            ChatSession? session = null;
            if (sessionId.HasValue && sessionId.Value > 0)
            {
                session = await dbContext.ChatSession.FindAsync(sessionId.Value);
                if (session == null || session.AspNetUserId != userId)
                {
                    yield return new ChatEvent { Type = "error", Data = "Error: Session not found." };
                    yield break;
                }
                currentSessionId = session.Id;
                
                var pastMessages = dbContext.ChatMessage.Where(m => m.ChatSessionId == currentSessionId).OrderBy(m => m.TimestampUtc).ToList();
                var recentMessageIds = pastMessages.OrderByDescending(m => m.TimestampUtc).Take(5).Select(m => m.Id).ToHashSet();
                
                var pastAttachments = dbContext.ChatMessageAttachment.Where(a => pastMessages.Select(m => m.Id).Contains(a.ChatMessageId)).ToList();
                var baseDir = _configuration["ConversationsDataLocation"];
                
                foreach (var pm in pastMessages)
                {
                    var msgAtts = pastAttachments.Where(a => a.ChatMessageId == pm.Id && a.ContentType != null && a.ContentType.StartsWith("image/")).ToList();
                    if (msgAtts.Count > 0 && recentMessageIds.Contains(pm.Id) && !string.IsNullOrEmpty(baseDir))
                    {
                        var msgImageAttachments = new List<SendMessageAttachment>();
                        foreach (var att in msgAtts)
                        {
                            var fullPath = Path.Combine(baseDir, att.RelativePath ?? "");
                            if (System.IO.File.Exists(fullPath))
                            {
                                var bytes = System.IO.File.ReadAllBytes(fullPath);
                                msgImageAttachments.Add(new SendMessageAttachment { ContentType = att.ContentType ?? "", Base64Data = Convert.ToBase64String(bytes) });
                            }
                        }
                        
                        if (msgImageAttachments.Count > 0)
                        {
                            messageHistory.Add(FormatMessage(provider, pm.Role ?? "", pm.Content ?? "", msgImageAttachments));
                            continue;
                        }
                    }
                    
                    messageHistory.Add(new { role = pm.Role, content = pm.Content });
                }

                // Detect context types from system messages
                foreach (var pm in pastMessages)
                {
                    if (pm.Role == "system" && pm.Content != null)
                    {
                        if (pm.Content.StartsWith("Game Snapshot:")) hasGameSnapshot = true;
                        if (pm.Content.StartsWith("Full Message History")) hasMessageHistory = true;
                    }
                }
            }
            else
            {
                session = new ChatSession
                {
                    AspNetUserId = userId,
                    Title = message.Length > 50 ? message.Substring(0, 47) + "..." : message,
                    CreatedUtc = DateTime.UtcNow,
                    LastMessageUtc = DateTime.UtcNow
                };
                dbContext.ChatSession.Add(session);
                await dbContext.SaveChangesAsync(cancellationToken);
                currentSessionId = session.Id;
                
                yield return new ChatEvent { Type = "sessionId", Data = currentSessionId.ToString() };
            }

            var contextDocs = _wikiService.GetRelevantContext(message);
            isGnollHackSession = session?.IsGnollHackSession ?? false;

            bool verboseMode = false;
            // isGameOn declared above
            bool developerMode = false;
            bool debugLogMessages = false;
            overseerMode = 0; // 0=gameplay, 1=technical, 2=developer

            if (!string.IsNullOrEmpty(session?.ClientSettings))
            {
                try
                {
                    var clientSettings = JsonDocument.Parse(session.ClientSettings);
                    var root = clientSettings.RootElement;

                    if (root.TryGetProperty("BoolData", out var boolData))
                    {
                        if (boolData.TryGetProperty("allowSpoilers", out var spoilerVal))
                            spoilerFreeMode = !spoilerVal.GetBoolean();

                        if (boolData.TryGetProperty("verboseResponses", out var verboseVal))
                            verboseMode = verboseVal.GetBoolean();

                        if (boolData.TryGetProperty("isGameOn", out var gameOnVal))
                            isGameOn = gameOnVal.GetBoolean();

                        if (boolData.TryGetProperty("DeveloperMode", out var devVal))
                            developerMode = devVal.GetBoolean();

                        if (boolData.TryGetProperty("DebugLogMessages", out var debugLogVal))
                            debugLogMessages = debugLogVal.GetBoolean();
                            
                        if (boolData.TryGetProperty("enableClientTools", out var clientToolsVal))
                            enableClientTools = enableClientTools && clientToolsVal.GetBoolean();
                            
                        if (boolData.TryGetProperty("enableGameActions", out var gameActionsVal))
                            enableGameActions = enableGameActions && gameActionsVal.GetBoolean();
                    }

                    if (root.TryGetProperty("IntData", out var intData))
                    {
                        if (intData.TryGetProperty("overseerMode", out var modeVal))
                            overseerMode = modeVal.GetInt32();
                    }
                }
                catch (JsonException) { }
            }

            // Server-side gating: ensure mode 2 is only allowed if both flags are true
            if (overseerMode == 2 && (!developerMode || !debugLogMessages))
            {
                overseerMode = isGameOn ? 0 : 1;
            }
            
            // enableToolUse handled above
            string systemPrompt = BuildSystemPrompt(contextDocs, spoilerFreeMode, verboseMode, isGameOn, developerMode, overseerMode, hasGameSnapshot, hasMessageHistory, session?.ClientSettings, enableToolUse, enableWebSearch);

            var userMsg = new ChatMessage
            {
                ChatSessionId = currentSessionId,
                Role = "user",
                IsHidden = isHidden,
                Content = message,
                TimestampUtc = DateTime.UtcNow
            };
            dbContext.ChatMessage.Add(userMsg);
            
            session!.LastMessageUtc = DateTime.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);
            
            var textAttachmentsContent = new StringBuilder();
            List<SendMessageAttachment> imageAttachments = new();

            if (attachments != null && attachments.Count > 0)
            {
                var baseDir = _configuration["ConversationsDataLocation"];
                if (!string.IsNullOrEmpty(baseDir))
                {
                    var sessionDir = Path.Combine(baseDir, currentSessionId.ToString());
                    if (!Directory.Exists(sessionDir)) Directory.CreateDirectory(sessionDir);
                    
                    foreach (var att in attachments)
                    {
                        try 
                        {
                            var ext = Path.GetExtension(att.FileName);
                            var newFileName = $"{Path.GetFileNameWithoutExtension(att.FileName)}_{Guid.NewGuid()}{ext}";
                            var relPath = Path.Combine(currentSessionId.ToString(), newFileName);
                            var fullPath = Path.Combine(baseDir, relPath);
                            
                            var base64Data = att.Base64Data.Contains(',') ? att.Base64Data.Split(',')[1] : att.Base64Data;
                            var bytes = Convert.FromBase64String(base64Data);
                            await System.IO.File.WriteAllBytesAsync(fullPath, bytes, cancellationToken);
                            
                            var dbAtt = new ChatMessageAttachment
                            {
                                ChatMessageId = userMsg.Id,
                                FileName = att.FileName,
                                ContentType = att.ContentType,
                                RelativePath = relPath
                            };
                            dbContext.ChatMessageAttachment.Add(dbAtt);

                            if (att.ContentType.StartsWith("image/"))
                            {
                                imageAttachments.Add(new SendMessageAttachment { 
                                    FileName = att.FileName, 
                                    ContentType = att.ContentType, 
                                    Base64Data = base64Data 
                                });
                            }
                            else
                            {
                                var textContent = Encoding.UTF8.GetString(bytes);
                                textAttachmentsContent.AppendLine($"\n--- File: {att.FileName} ---");
                                textAttachmentsContent.AppendLine(textContent);
                                textAttachmentsContent.AppendLine($"--- End File ---");
                            }
                        }
                        catch (Exception)
                        {
                            // Ignore specific attachment failure
                        }
                    }
                    await dbContext.SaveChangesAsync(cancellationToken);
                }
            }

            string finalMessageText = message;
            if (textAttachmentsContent.Length > 0)
            {
                finalMessageText += "\n\n" + textAttachmentsContent.ToString();
            }

            messageHistory.Add(FormatMessage(provider, "user", finalMessageText, imageAttachments));
            
            // Truncation logic
            var meta = _modelMetadataService.GetMetadata(model);
            int effectiveInputLimit = maxInputTokens ?? meta.MaxInputTokens;
            if (effectiveInputLimit <= 0) effectiveInputLimit = 100000;
            
            int EstimateTokens(object msg)
            {
                return JsonSerializer.Serialize(msg).Length / 4;
            }
            
            int totalTokens = EstimateTokens(systemPrompt);
            var truncatedHistory = new List<object>();
            
            for (int i = messageHistory.Count - 1; i >= 0; i--)
            {
                int msgTokens = EstimateTokens(messageHistory[i]);
                if (totalTokens + msgTokens > effectiveInputLimit && truncatedHistory.Count > 0)
                {
                    // Always keep the current user message (last one added) and system prompt, truncate others
                    break;
                }
                totalTokens += msgTokens;
                truncatedHistory.Insert(0, messageHistory[i]);
            }
            messageHistory = truncatedHistory;
            
            // Always Insert System Prompt
            messageHistory.Insert(0, new { role = "system", content = systemPrompt });
            
            // Format Anthropic alternating
            if (provider.Equals("Anthropic", StringComparison.OrdinalIgnoreCase))
            {
                messageHistory = AlternateAnthropicMessages(messageHistory);
            }
            
            // Note: maxOutputTokens is handled later

            yield return new ChatEvent 
            { 
                Type = "debug", 
                Data = $"[Model Configuration]\nProvider: {provider}\nModel: {model}\nThinking Level: {(string.IsNullOrEmpty(thinkingLevel) ? "None" : thinkingLevel)}\nMax Input Tokens (Limit): {effectiveInputLimit}\nMax Output Tokens: {(maxOutputTokens.HasValue ? maxOutputTokens.Value.ToString() : "Default")}\nEstimated Request Input Tokens: ~{totalTokens}" 
            };
        }

        string fullResponse = "";
        
        int toolIterations = 0;
        bool hasToolsToRun = true;
        
        var execContext = new ToolExecutionContext { SessionId = currentSessionId, IsGameOn = isGameOn, SpoilerFreeMode = spoilerFreeMode, OverseerMode = overseerMode, IsGnollHackSession = isGnollHackSession };
        
        while (toolIterations <= 5 && hasToolsToRun && !cancellationToken.IsCancellationRequested)
        {
            if (toolIterations == 5 && hasToolsToRun)
            {
                yield return new ChatEvent { Type = "tool_error", Data = "Tool call limit reached. Forcing final response." };
                enableToolUse = false;
                enableWebSearch = false;
                enableClientTools = false;
                enableGameActions = false;
            }
            
            hasToolsToRun = false;
            string iterationText = "";
            var requestTools = _toolRegistry.BuildToolsForRequest(provider, execContext, enableWebSearch, enableToolUse, enableClientTools, enableGameActions);
            var currentIterationToolCalls = new List<JsonElement>();
            
            if (provider.Equals("OpenAI", StringComparison.OrdinalIgnoreCase))
            {
                var httpClient = _httpClientFactory.CreateClient();
                httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");

                var requestBody = new Dictionary<string, object>
                {
                    { "model", model },
                    { "messages", messageHistory },
                    { "stream", true }
                };
                if (maxOutputTokens.HasValue) requestBody["max_tokens"] = maxOutputTokens.Value;

                if (!string.IsNullOrEmpty(thinkingLevel))
                {
                    requestBody["reasoning_effort"] = thinkingLevel;
                }
                
                if (requestTools.HasTools)
                {
                    var allTools = new List<object>();
                    allTools.AddRange(requestTools.ProviderTools);
                    allTools.AddRange(requestTools.FunctionDeclarations);
                    requestBody["tools"] = allTools;
                }

                await foreach (var evt in ExecuteApiWithRetriesAsync(
                    async ct =>
                    {
                        var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions")
                        {
                            Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json")
                        };
                        return await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
                    },
                    ParseOpenAIStream,
                    "OpenAI",
                    cancellationToken))
                {
                    if (evt.Type == "chunk")
                    {
                        fullResponse += evt.Data;
                        iterationText += evt.Data;
                    }
                    
                    if (evt.Type == "tool_call_complete")
                    {
                        hasToolsToRun = true;
                        currentIterationToolCalls.Add(JsonSerializer.Deserialize<JsonElement>(evt.Data));
                        yield return new ChatEvent { Type = "tool_start", Data = evt.Data };
                    }
                    else
                    {
                        yield return evt;
                    }
                }
                
                if (hasToolsToRun && currentIterationToolCalls.Count > 0)
                {
                    // Add assistant tool calls message
                    var toolCallsForHistory = new List<object>();
                    foreach (var tc in currentIterationToolCalls)
                    {
                        toolCallsForHistory.Add(new { 
                            id = tc.GetProperty("id").GetString(),
                            type = "function",
                            function = new {
                                name = tc.GetProperty("name").GetString(),
                                arguments = tc.GetProperty("arguments").GetString()
                            }
                        });
                    }
                    messageHistory.Add(new { role = "assistant", content = iterationText, tool_calls = toolCallsForHistory });
                    
                    // Execute tools
                    foreach (var tc in currentIterationToolCalls)
                    {
                        var tId = tc.GetProperty("id").GetString();
                        var tName = tc.GetProperty("name").GetString() ?? "";
                        var tArgsStr = tc.GetProperty("arguments").GetString() ?? "{}";
                        JsonElement tArgs = JsonDocument.Parse("{}").RootElement;
                        try { tArgs = JsonSerializer.Deserialize<JsonElement>(tArgsStr); } catch { }
                        
                        var res = await _toolExecutor.ExecuteAsync(tName, tArgs, execContext);
                        var resContent = res.Content ?? (res.Success ? "Success" : res.ErrorMessage);
                        
                        messageHistory.Add(new { role = "tool", tool_call_id = tId, content = resContent });
                        
                        if (res.Success)
                        {
                            var resObj = new { id = tId, name = tName, result = resContent };
                            yield return new ChatEvent { Type = "tool_result", Data = JsonSerializer.Serialize(resObj) };
                        }
                        else
                        {
                            var errObj = new { id = tId, name = tName, error = resContent };
                            yield return new ChatEvent { Type = "tool_error", Data = JsonSerializer.Serialize(errObj) };
                        }
                    }
                }
            }
            else if (provider.Equals("Anthropic", StringComparison.OrdinalIgnoreCase))
            {
                var httpClient = _httpClientFactory.CreateClient();
                httpClient.DefaultRequestHeaders.Add("x-api-key", apiKey);
                httpClient.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");

                var systemMessages = messageHistory.Where(m => (GetProperty(m, "role") as string) == "system").Select(m => GetProperty(m, "content") as string ?? "").ToList();
                string systemPromptText = string.Join("\n\n", systemMessages);
                
                var userAsstMessages = messageHistory.Where(m => (GetProperty(m, "role") as string) != "system").ToList();

                int defaultAnthropicTokens = _configuration.GetValue<int?>("DefaultMaxOutputTokens:Anthropic") ?? 8192;

                var requestBody = new Dictionary<string, object>
                {
                    { "model", model },
                    { "system", systemPromptText },
                    { "messages", userAsstMessages },
                    { "stream", true },
                    { "max_tokens", maxOutputTokens ?? defaultAnthropicTokens }
                };

                if (!string.IsNullOrEmpty(thinkingLevel))
                {
                    requestBody["thinking"] = new { type = "adaptive" }; 
                    requestBody["output_config"] = new { effort = thinkingLevel.ToLower() };
                }
                
                if (requestTools.HasTools)
                {
                    var allTools = new List<object>();
                    allTools.AddRange(requestTools.ProviderTools);
                    allTools.AddRange(requestTools.FunctionDeclarations);
                    requestBody["tools"] = allTools;
                }

                await foreach (var evt in ExecuteApiWithRetriesAsync(
                    async ct =>
                    {
                        var request = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages")
                        {
                            Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json")
                        };
                        return await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
                    },
                    ParseAnthropicStream,
                    "Anthropic",
                    cancellationToken))
                {
                    if (evt.Type == "chunk")
                    {
                        fullResponse += evt.Data;
                        iterationText += evt.Data;
                    }
                    
                    if (evt.Type == "tool_call_complete")
                    {
                        hasToolsToRun = true;
                        currentIterationToolCalls.Add(JsonSerializer.Deserialize<JsonElement>(evt.Data));
                        yield return new ChatEvent { Type = "tool_start", Data = evt.Data };
                    }
                    else
                    {
                        yield return evt;
                    }
                }
                
                if (hasToolsToRun && currentIterationToolCalls.Count > 0)
                {
                    var asstContent = new List<object>();
                    foreach (var tc in currentIterationToolCalls)
                    {
                        asstContent.Add(new {
                            type = "tool_use",
                            id = tc.GetProperty("id").GetString(),
                            name = tc.GetProperty("name").GetString(),
                            input = JsonSerializer.Deserialize<JsonElement>(tc.GetProperty("arguments").GetString() ?? "{}")
                        });
                    }
                    
                    if (!string.IsNullOrEmpty(iterationText))
                    {
                        asstContent.Insert(0, new { type = "text", text = iterationText });
                    }
                    
                    messageHistory.Add(new { role = "assistant", content = asstContent });
                    messageHistory = AlternateAnthropicMessages(messageHistory);
                    
                    var userToolResults = new List<object>();
                    foreach (var tc in currentIterationToolCalls)
                    {
                        var tId = tc.GetProperty("id").GetString();
                        var tName = tc.GetProperty("name").GetString() ?? "";
                        var tArgsStr = tc.GetProperty("arguments").GetString() ?? "{}";
                        JsonElement tArgs = JsonDocument.Parse("{}").RootElement;
                        try { tArgs = JsonSerializer.Deserialize<JsonElement>(tArgsStr); } catch { }
                        
                        var res = await _toolExecutor.ExecuteAsync(tName, tArgs, execContext);
                        var resContent = res.Content ?? (res.Success ? "Success" : res.ErrorMessage);
                        
                        userToolResults.Add(new {
                            type = "tool_result",
                            tool_use_id = tId,
                            content = resContent
                        });
                        
                        if (res.Success)
                        {
                            var resObj = new { id = tId, name = tName, result = resContent };
                            yield return new ChatEvent { Type = "tool_result", Data = JsonSerializer.Serialize(resObj) };
                        }
                        else
                        {
                            var errObj = new { id = tId, name = tName, error = resContent };
                            yield return new ChatEvent { Type = "tool_error", Data = JsonSerializer.Serialize(errObj) };
                        }
                    }
                    messageHistory.Add(new { role = "user", content = userToolResults });
                    messageHistory = AlternateAnthropicMessages(messageHistory);
                }
            }
            else if (provider.Equals("Google", StringComparison.OrdinalIgnoreCase))
            {
                var httpClient = _httpClientFactory.CreateClient();
                var contents = new List<object>();
                string systemInstruction = "";

                foreach (var m in messageHistory)
                {
                    var role = GetProperty(m, "role") as string;
                    var hasParts = m.GetType().GetProperty("parts") != null;
                    
                    if (role == "system")
                    {
                        var content = GetProperty(m, "content") as string;
                        systemInstruction += content + "\n";
                    }
                    else
                    {
                        if (hasParts)
                        {
                            contents.Add(new
                            {
                                role = role == "user" ? "user" : "model",
                                parts = GetProperty(m, "parts")
                            });
                        }
                        else
                        {
                            var content = GetProperty(m, "content");
                            contents.Add(new
                            {
                                role = role == "user" ? "user" : "model",
                                parts = new[] { new { text = content } }
                            });
                        }
                    }
                }

                var requestBody = new Dictionary<string, object>
                {
                    { "contents", contents }
                };

                if (!string.IsNullOrEmpty(systemInstruction))
                {
                    requestBody["systemInstruction"] = new
                    {
                        parts = new[] { new { text = systemInstruction } }
                    };
                }
                
                if (maxOutputTokens.HasValue)
                {
                    requestBody["generationConfig"] = new { maxOutputTokens = maxOutputTokens.Value };
                }
                
                var geminiSafetySettings = _configuration.GetSection("SafetySettings:Gemini").GetChildren().Select(c => new
                {
                    category = c.Key,
                    threshold = c.Value
                }).ToList();

                if (geminiSafetySettings.Any())
                {
                    requestBody["safetySettings"] = geminiSafetySettings;
                }
                
                if (requestTools.HasTools)
                {
                    var allTools = new List<object>();
                    allTools.AddRange(requestTools.ProviderTools);
                    if (requestTools.FunctionDeclarations.Any())
                    {
                        allTools.Add(new { functionDeclarations = requestTools.FunctionDeclarations });
                    }
                    requestBody["tools"] = allTools;
                    requestBody["toolConfig"] = new { includeServerSideToolInvocations = true };
                }

                await foreach (var evt in ExecuteApiWithRetriesAsync(
                    async ct =>
                    {
                        var request = new HttpRequestMessage(HttpMethod.Post, $"https://generativelanguage.googleapis.com/v1beta/models/{model}:streamGenerateContent?alt=sse&key={apiKey}")
                        {
                            Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json")
                        };
                        return await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
                    },
                    ParseGeminiStream,
                    "Google",
                    cancellationToken))
                {
                    if (evt.Type == "chunk")
                    {
                        fullResponse += evt.Data;
                        iterationText += evt.Data;
                    }
                    
                    if (evt.Type == "tool_call_complete")
                    {
                        hasToolsToRun = true;
                        currentIterationToolCalls.Add(JsonSerializer.Deserialize<JsonElement>(evt.Data));
                        yield return new ChatEvent { Type = "tool_start", Data = evt.Data };
                    }
                    else
                    {
                        yield return evt;
                    }
                }
                
                if (hasToolsToRun && currentIterationToolCalls.Count > 0)
                {
                    var modelParts = new List<object>();
                    foreach (var tc in currentIterationToolCalls)
                    {
                        var argsStr = tc.GetProperty("arguments").GetString() ?? "{}";
                        JsonElement argJson = JsonDocument.Parse("{}").RootElement;
                        try { argJson = JsonSerializer.Deserialize<JsonElement>(argsStr); } catch { }
                        
                        modelParts.Add(new {
                            functionCall = new {
                                name = tc.GetProperty("name").GetString(),
                                args = argJson
                            }
                        });
                    }
                    
                    if (!string.IsNullOrEmpty(iterationText))
                    {
                        modelParts.Insert(0, new { text = iterationText });
                    }
                    
                    messageHistory.Add(new { role = "model", parts = modelParts });
                    
                    var userParts = new List<object>();
                    foreach (var tc in currentIterationToolCalls)
                    {
                        var tId = tc.GetProperty("id").GetString();
                        var tName = tc.GetProperty("name").GetString() ?? "";
                        var tArgsStr = tc.GetProperty("arguments").GetString() ?? "{}";
                        JsonElement tArgs = JsonDocument.Parse("{}").RootElement;
                        try { tArgs = JsonSerializer.Deserialize<JsonElement>(tArgsStr); } catch { }
                        
                        var res = await _toolExecutor.ExecuteAsync(tName, tArgs, execContext);
                        var resContent = res.Content ?? (res.Success ? "Success" : res.ErrorMessage);
                        
                        userParts.Add(new {
                            functionResponse = new {
                                name = tName,
                                response = new {
                                    name = tName,
                                    content = resContent
                                }
                            }
                        });
                        
                        if (res.Success)
                        {
                            var resObj = new { id = tId, name = tName, result = resContent };
                            yield return new ChatEvent { Type = "tool_result", Data = JsonSerializer.Serialize(resObj) };
                        }
                        else
                        {
                            var errObj = new { id = tId, name = tName, error = resContent };
                            yield return new ChatEvent { Type = "tool_error", Data = JsonSerializer.Serialize(errObj) };
                        }
                    }
                    messageHistory.Add(new { role = "user", parts = userParts });
                }
            }
            else
            {
                yield return new ChatEvent { Type = "error", Data = $"Provider {provider} is not fully implemented yet in this demo." };
                fullResponse = $"Provider {provider} is not fully implemented yet in this demo.";
                hasToolsToRun = false;
            }
            
            toolIterations++;
        }

        using (var scope = _scopeFactory.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var session = await dbContext.ChatSession.FindAsync(currentSessionId);
            if (session != null)
            {
                var asstMsg = new ChatMessage
                {
                    ChatSessionId = currentSessionId,
                    Role = "assistant",
                    Content = fullResponse,
                    TimestampUtc = DateTime.UtcNow,
                    ProviderUsed = provider,
                    ModelUsed = model,
                    ThinkingLevelUsed = thinkingLevel
                };
                dbContext.ChatMessage.Add(asstMsg);
                session.LastMessageUtc = DateTime.UtcNow;
                await dbContext.SaveChangesAsync(CancellationToken.None);
            }
        }
    }

    private async IAsyncEnumerable<ChatEvent> ExecuteApiWithRetriesAsync(
        Func<CancellationToken, Task<HttpResponseMessage>> requestFactory,
        Func<HttpResponseMessage, CancellationToken, IAsyncEnumerable<ChatEvent>> streamParser,
        string providerName,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        int[] retryDelays = { 1, 5, 10, 20, 30, 60 };
        int attempt = 0;
        bool success = false;
        
        while (!success && !cancellationToken.IsCancellationRequested)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            yield return new ChatEvent { Type = "debug", Data = $"[{providerName}] Starting POST request (Attempt {attempt + 1})..." };
            yield return new ChatEvent { Type = "status", Data = $"Waiting for {providerName}..." };

            HttpResponseMessage? response = null;
            Exception? requestException = null;
            try
            {
                response = await requestFactory(cancellationToken);
            }
            catch (Exception ex)
            {
                requestException = ex;
            }

            if (requestException != null)
            {
                sw.Stop();
                if (cancellationToken.IsCancellationRequested)
                {
                    yield return new ChatEvent { Type = "debug", Data = $"[{providerName}] Request canceled by user in {sw.ElapsedMilliseconds}ms" };
                    yield return new ChatEvent { Type = "error", Data = "Request canceled." };
                }
                else
                {
                    yield return new ChatEvent { Type = "debug", Data = $"[{providerName}] Request failed in {sw.ElapsedMilliseconds}ms: {requestException.Message}" };
                    yield return new ChatEvent { Type = "error", Data = $"Request failed: {requestException.Message}" };
                }
                yield break;
            }

            sw.Stop();
            
            if (response!.IsSuccessStatusCode)
            {
                yield return new ChatEvent { Type = "debug", Data = $"[{providerName}] HTTP {(int)response.StatusCode} Received ({sw.ElapsedMilliseconds}ms)" };
                yield return new ChatEvent { Type = "status", Data = $"Streaming response..." };
                
                await foreach (var evt in streamParser(response, cancellationToken))
                {
                    yield return evt;
                }
                
                yield return new ChatEvent { Type = "debug", Data = $"[{providerName}] Stream completed." };
                success = true;
            }
            else
            {
                var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                yield return new ChatEvent { Type = "debug", Data = $"[{providerName}] HTTP {(int)response.StatusCode} Received ({sw.ElapsedMilliseconds}ms)\nBody: {errorBody}" };

                if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests) // 429
                {
                    yield return new ChatEvent { Type = "error", Data = "429 Rate Limited. Please try again later." };
                    yield break;
                }
                else if (response.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable || response.StatusCode == System.Net.HttpStatusCode.BadGateway || response.StatusCode == System.Net.HttpStatusCode.InternalServerError) // 503, 502, 500
                {
                    if (attempt < retryDelays.Length)
                    {
                        int delaySeconds = retryDelays[attempt];
                        yield return new ChatEvent { Type = "status", Data = $"503 Unavailable. Retrying in {delaySeconds}s..." };
                        yield return new ChatEvent { Type = "debug", Data = $"[{providerName}] Sleeping for {delaySeconds}s before retry..." };
                        
                        try {
                            await Task.Delay(delaySeconds * 1000, cancellationToken);
                        } catch(TaskCanceledException) { yield break; }
                        
                        attempt++;
                    }
                    else
                    {
                        yield return new ChatEvent { Type = "error", Data = "503 Unavailable. Max retries exceeded." };
                        yield break;
                    }
                }
                else
                {
                    yield return new ChatEvent { Type = "error", Data = $"API Error: {(int)response.StatusCode} - {errorBody}" };
                    yield break;
                }
            }
        }
    }

    private async IAsyncEnumerable<ChatEvent> ParseOpenAIStream(HttpResponseMessage response, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);
        string? line = null;
        
        var toolCalls = new Dictionary<int, (string Id, string Name, StringBuilder Arguments)>();

        while (!cancellationToken.IsCancellationRequested && (line = await reader.ReadLineAsync()) != null)
        {
            if (string.IsNullOrEmpty(line)) continue;
            if (line.StartsWith("data: "))
            {
                var data = line.Substring(6);
                if (data == "[DONE]") break;

                string? chunkStr = null;
                try
                {
                    var json = JsonSerializer.Deserialize<JsonElement>(data);
                    if (json.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
                    {
                        var delta = choices[0].GetProperty("delta");
                        if (delta.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String)
                        {
                            chunkStr = content.GetString();
                        }
                        
                        if (delta.TryGetProperty("tool_calls", out var tcArray))
                        {
                            foreach (var tc in tcArray.EnumerateArray())
                            {
                                int idx = tc.GetProperty("index").GetInt32();
                                if (!toolCalls.ContainsKey(idx))
                                {
                                    toolCalls[idx] = (
                                        tc.TryGetProperty("id", out var idProp) ? (idProp.GetString() ?? "") : "",
                                        tc.TryGetProperty("function", out var fProp) && fProp.TryGetProperty("name", out var nProp) ? (nProp.GetString() ?? "") : "",
                                        new StringBuilder()
                                    );
                                }
                                
                                if (tc.TryGetProperty("function", out var fProp2) && fProp2.TryGetProperty("arguments", out var argProp))
                                {
                                    toolCalls[idx].Arguments.Append(argProp.GetString());
                                }
                            }
                        }
                    }
                }
                catch { /* ignore */ }
                
                if (chunkStr != null)
                {
                    yield return new ChatEvent { Type = "chunk", Data = chunkStr };
                }
            }
        }
        
        foreach (var tc in toolCalls.Values)
        {
            var callObj = new { id = tc.Id, name = tc.Name, arguments = tc.Arguments.ToString() };
            yield return new ChatEvent { Type = "tool_call_complete", Data = JsonSerializer.Serialize(callObj) };
        }
    }

    private async IAsyncEnumerable<ChatEvent> ParseAnthropicStream(HttpResponseMessage response, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);
        string? line = null;
        
        string? currentToolId = null;
        string? currentToolName = null;
        StringBuilder currentToolArgs = new StringBuilder();

        while (!cancellationToken.IsCancellationRequested && (line = await reader.ReadLineAsync()) != null)
        {
            if (string.IsNullOrEmpty(line)) continue;
            if (line.StartsWith("data: "))
            {
                var data = line.Substring(6);
                string? chunkStr = null;
                ChatEvent? toolCallEvt = null;
                try
                {
                    var json = JsonSerializer.Deserialize<JsonElement>(data);
                    if (json.TryGetProperty("type", out var type))
                    {
                        var t = type.GetString();
                        if (t == "content_block_start")
                        {
                            var cb = json.GetProperty("content_block");
                            if (cb.TryGetProperty("type", out var cbType) && cbType.GetString() == "tool_use")
                            {
                                currentToolId = cb.GetProperty("id").GetString();
                                currentToolName = cb.GetProperty("name").GetString();
                                currentToolArgs.Clear();
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
                    }
                }
                catch { /* ignore */ }
                
                if (chunkStr != null)
                {
                    yield return new ChatEvent { Type = "chunk", Data = chunkStr };
                }
                if (toolCallEvt != null)
                {
                    yield return toolCallEvt;
                }
            }
        }
    }

    private async IAsyncEnumerable<ChatEvent> ParseGeminiStream(HttpResponseMessage response, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);
        string? line = null;
        
        while (!cancellationToken.IsCancellationRequested && (line = await reader.ReadLineAsync()) != null)
        {
            if (string.IsNullOrEmpty(line)) continue;
            if (line.StartsWith("data: "))
            {
                var data = line.Substring(6);
                var eventsToYield = new List<ChatEvent>();
                try
                {
                    var json = JsonSerializer.Deserialize<JsonElement>(data);
                    if (json.TryGetProperty("candidates", out var candidates) && candidates.GetArrayLength() > 0)
                    {
                        var parts = candidates[0].GetProperty("content").GetProperty("parts");
                        foreach (var part in parts.EnumerateArray())
                        {
                            if (part.TryGetProperty("text", out var text))
                            {
                                eventsToYield.Add(new ChatEvent { Type = "chunk", Data = text.GetString() ?? "" });
                            }
                            else if (part.TryGetProperty("functionCall", out var fc))
                            {
                                var name = fc.GetProperty("name").GetString() ?? "";
                                var args = fc.TryGetProperty("args", out var a) ? a.GetRawText() : "{}";
                                var callObj = new { id = Guid.NewGuid().ToString(), name = name, arguments = args };
                                eventsToYield.Add(new ChatEvent { Type = "tool_call_complete", Data = JsonSerializer.Serialize(callObj) });
                            }
                        }
                    }
                }
                catch { /* ignore */ }
                
                foreach(var evt in eventsToYield)
                {
                    yield return evt;
                }
            }
        }
    }

    
    private object FormatMessage(string provider, string role, string text, List<SendMessageAttachment>? imageAttachments)
    {
        if (imageAttachments == null || imageAttachments.Count == 0) return new { role = role, content = text };
        
        if (provider.Equals("OpenAI", StringComparison.OrdinalIgnoreCase))
        {
            var contentList = new List<object>();
            contentList.Add(new { type = "text", text = text });
            foreach(var img in imageAttachments)
            {
                contentList.Add(new { type = "image_url", image_url = new { url = $"data:{img.ContentType};base64,{img.Base64Data}" } });
            }
            return new { role = role, content = contentList };
        }
        else if (provider.Equals("Anthropic", StringComparison.OrdinalIgnoreCase))
        {
            var contentList = new List<object>();
            foreach(var img in imageAttachments)
            {
                contentList.Add(new { type = "image", source = new { type = "base64", media_type = img.ContentType, data = img.Base64Data } });
            }
            contentList.Add(new { type = "text", text = text });
            return new { role = role, content = contentList };
        }
        else // Google
        {
            var partsList = new List<object>();
            partsList.Add(new { text = text });
            foreach(var img in imageAttachments)
            {
                partsList.Add(new { inlineData = new { mimeType = img.ContentType, data = img.Base64Data } });
            }
            return new { role = role, parts = partsList };
        }
    }
    private static object? GetProperty(object? obj, string propertyName)
    {
        if (obj == null) return null;
        return obj.GetType().GetProperty(propertyName)?.GetValue(obj);
    }

    private List<object> AlternateAnthropicMessages(List<object> messages)
    {
        var combined = new List<object>();
        object? lastMsg = null;
        foreach (var msg in messages)
        {
            var lastRole = GetProperty(lastMsg, "role") as string;
            var currentRole = GetProperty(msg, "role") as string;

            if (lastMsg != null && lastRole != null && lastRole == currentRole && currentRole != "system")
            {
                var c1 = GetProperty(lastMsg, "content");
                var c2 = GetProperty(msg, "content");

                var list = new List<object>();
                if (c1 is string s1) list.Add(new { type = "text", text = s1 });
                else if (c1 is IEnumerable<object> e1) list.AddRange(e1);

                if (c2 is string s2) list.Add(new { type = "text", text = "\n\n" + s2 });
                else if (c2 is IEnumerable<object> e2) list.AddRange(e2);

                lastMsg = new { role = currentRole, content = (object)list };
                combined[combined.Count - 1] = lastMsg;
            }
            else
            {
                combined.Add(msg);
                lastMsg = msg;
            }
        }
        return combined;
    }

    private string BuildSystemPrompt(
        IEnumerable<string> wikiContext,
        bool spoilerFreeMode,
        bool verboseMode,
        bool isGameOn,
        bool developerMode,
        int overseerMode,
        bool hasGameSnapshot,
        bool hasMessageHistory,
        string? clientSettings,
        bool enableToolUse,
        bool enableWebSearch)
    {
        var sb = new StringBuilder();

        // ──────────────────────────────────────────────
        // SECTION 1: Identity & Objective
        // ──────────────────────────────────────────────
        sb.AppendLine("You are the Gnoll Overseer, an expert AI assistant for GnollHack — a modern roguelike game derived from NetHack 3.6.2 with extensive new features including tile graphics, sound, music, voiceovers, and a .NET MAUI mobile/desktop frontend.");
        sb.AppendLine();

        // NEW: Restrict to GnollHack topics
        sb.AppendLine("CRITICAL INSTRUCTION: You must strictly limit your assistance and conversation to topics related to GnollHack, NetHack, and their direct technical, development, or gameplay aspects.");
        sb.AppendLine("If the user asks about unrelated topics (e.g., general programming unrelated to the project, other games, cooking, politics, general knowledge), you must politely decline and remind them that you are an assistant dedicated exclusively to GnollHack.");
        sb.AppendLine();

        // NEW: Clear objective that varies by game state
        if (isGameOn || hasGameSnapshot)
        {
            sb.AppendLine("Your objective is to maximize the player's chance of winning while helping them understand important decisions. Every recommendation should be evaluated against one question: Does this help the player survive and improve?");
        }
        else
        {
            sb.AppendLine("Your objective is to help the player understand GnollHack's mechanics, review past games via dumplogs, suggest character builds and strategies for future runs, and provide technical support when needed.");
        }
        sb.AppendLine();

        // ──────────────────────────────────────────────
        // SECTION 2: Mode
        // ──────────────────────────────────────────────
        switch (overseerMode)
        {
            case 1: // Technical Help
                sb.AppendLine("## Mode: Technical Support");
                sb.AppendLine("The user needs help with app issues, save files, configuration, or installation.");
                sb.AppendLine("Be precise and diagnostic. Ask clarifying questions about the user's platform, app version, and error messages.");
                sb.AppendLine("Focus on game mechanics, strategy, and game-related troubleshooting in addition to app problems. Feel empowered to explain complex NetHack/GnollHack mechanics (e.g., armor class, spellcasting penalties, weapon skills) when asked about how the game works.");
                sb.AppendLine("When greeting the player, briefly introduce yourself and mention that you can help with technical issues such as save files, installation, app configuration, and troubleshooting.");
                break;
            case 2: // Developer / Debugging — only when DeveloperMode AND DebugLogMessages are both ON
                sb.AppendLine("## Mode: Developer / Debugging");
                sb.AppendLine("The user is a GnollHack developer or power user debugging the game.");
                sb.AppendLine("Be technical and terse. Reference source code files, function names, and data structures.");
                sb.AppendLine("The GnollHack C core is in src/ and include/, the .NET MAUI frontend in win/win32/xpl/.");
                sb.AppendLine("Analyze debug data, crash logs, and runtime state when available.");
                sb.AppendLine("Developer Mode and Debug Logging are both ON. The user has access to Wizard Mode (#wizmode) for testing.");
                if (developerMode)
                    sb.AppendLine("Debug data has been collected and is available in the session context.");
                sb.AppendLine("When greeting the player, briefly introduce yourself as running in debug analysis mode and mention that you can help analyze crash logs, runtime state, debug data, and provide code-level guidance.");
                break;
            default: // 0 = Gameplay Help
                sb.AppendLine("## Mode: Gameplay Help");
                if (isGameOn || hasGameSnapshot)
                {
                    sb.AppendLine("The user is playing GnollHack and needs gameplay assistance.");
                    sb.AppendLine("Be friendly and encouraging. Provide tactical advice, item identification help, strategy tips, and dungeon navigation guidance.");
                    sb.AppendLine("When greeting the player, briefly introduce yourself and give a short observation about their current situation based on the game snapshot (e.g., their HP, dungeon level, or any visible threats). Keep it to 1–2 sentences.");
                }
                else
                {
                    sb.AppendLine("The user is not currently in an active game. They may be asking general questions about GnollHack, reviewing dumplogs from past games, seeking character build advice, or exploring game mechanics.");
                    sb.AppendLine("Be friendly and knowledgeable. Help analyze past runs, suggest strategies for future characters, and explain game mechanics clearly.");
                    sb.AppendLine("When greeting the player, briefly introduce yourself and mention that you can help with game mechanics questions, reviewing past games, and planning future characters.");
                }
                if (developerMode)
                    sb.AppendLine("Developer Mode is ON — the user has access to Wizard Mode (#wizmode) for testing.");
                break;
        }
        sb.AppendLine();

        // ──────────────────────────────────────────────
        // SECTION 3: Priority Hierarchy (NEW)
        // ──────────────────────────────────────────────
        if (overseerMode == 0) // Gameplay mode only
        {
            sb.AppendLine("## Decision Priorities");
            if (isGameOn || hasGameSnapshot)
            {
                sb.AppendLine("When advising the player, follow this priority order:");
                sb.AppendLine("1. Prevent immediate death");
                sb.AppendLine("2. Avoid irreversible mistakes (e.g., destroying quest items, angering the quest leader)");
                sb.AppendLine("3. Preserve rare or unique resources");
                sb.AppendLine("4. Improve positioning and tactical advantage");
                sb.AppendLine("5. Increase long-term power and progression");
                sb.AppendLine("6. Explain mechanics when helpful");
            }
            else
            {
                sb.AppendLine("When advising the player about past games or future strategies:");
                sb.AppendLine("1. Help the player learn from past mistakes");
                sb.AppendLine("2. Suggest viable character builds and strategies");
                sb.AppendLine("3. Explain relevant game mechanics");
            }
            sb.AppendLine();
        }

        // ──────────────────────────────────────────────
        // SECTION 4: Structured Reasoning (NEW — game-on only)
        // ──────────────────────────────────────────────
        if ((isGameOn || hasGameSnapshot) && overseerMode == 0)
        {
            sb.AppendLine("## Tactical Reasoning");
            sb.AppendLine("Before recommending an action:");
            sb.AppendLine("1. Identify immediate threats (low HP, adjacent enemies, dangerous terrain, status effects)");
            sb.AppendLine("2. Identify available resources (potions, scrolls, escape items, spells)");
            sb.AppendLine("3. Consider the next 1–3 turns");
            sb.AppendLine("4. Recommend the single best action");
            sb.AppendLine("5. Explain the reasoning briefly");
            sb.AppendLine();
        }

        // ──────────────────────────────────────────────
        // SECTION 5: Capabilities
        // ──────────────────────────────────────────────
        sb.AppendLine("## Your Capabilities");
        sb.AppendLine("- **Player Assistance**: Tactical advice, item identification, strategy tips, monster info, spell recommendations, dungeon navigation");
        sb.AppendLine("- **Technical Support**: Save file troubleshooting, app issues, file recovery guidance, configuration help");
        if (developerMode)
        {
            sb.AppendLine("- **Developer Assistance**: This session includes runtime debug data. You can help diagnose crashes, runtime errors, save corruption, pending task issues, and provide code-level guidance for the GnollHack C core and .NET MAUI frontend.");
        }
        sb.AppendLine();

        // ──────────────────────────────────────────────
        // SECTION 6: Available Context
        // ──────────────────────────────────────────────
        sb.AppendLine("## Available Context in This Session");
        if (hasGameSnapshot) sb.AppendLine("- ✅ Game snapshot (current map, stats, inventory, recent messages, spells, skills, attributes)");
        if (hasMessageHistory) sb.AppendLine("- ✅ Full message history (up to 16384 in-game messages) — reference when the player asks about earlier events");
        if (developerMode) sb.AppendLine("- ✅ Runtime debug data (memory usage, thread state, pending tasks, developer settings) via Client Environment Settings");
        if (!hasGameSnapshot && !hasMessageHistory && !developerMode)
        {
            sb.AppendLine("- No game context was provided for this session. Answer based on general GnollHack knowledge and wiki content.");
        }
        sb.AppendLine();

        // ──────────────────────────────────────────────
        // SECTION 6b: Source Code Access
        // ──────────────────────────────────────────────
        if (enableToolUse)
        {
            sb.AppendLine("## Source Code Access");
            sb.AppendLine("You have access to the GnollHack C source code via the source_code_search and source_code_view tools.");
            sb.AppendLine("Use these tools to verify undocumented mechanics, check exact formulas or probabilities, and investigate potential bugs.");
            sb.AppendLine("When a player asks about a specific mechanic that is not covered in the wiki, search the source code to find the authoritative answer.");
            sb.AppendLine("When citing source code findings, mention the file and line number, and translate the C code into player-friendly language.");
            if (overseerMode == 2)
                sb.AppendLine("In Debug Mode, you also have access to the .NET MAUI frontend code (C#/XAML) under win/win32/xpl/.");
            sb.AppendLine();
        }

        // ──────────────────────────────────────────────
        // SECTION 7: Session Context
        // ──────────────────────────────────────────────
        if (!isGameOn && !hasGameSnapshot)
        {
            sb.AppendLine("## Session Context");
            sb.AppendLine("This session was started from the main menu (no active game). The player may be asking general questions about GnollHack, seeking help with the app, or browsing dumplogs from previous games.");
        }

        // ──────────────────────────────────────────────
        // SECTION 8: Information Authority & Anti-Fabrication (NEW)
        // ──────────────────────────────────────────────
        sb.AppendLine("## Information Authority");
        if (isGameOn || hasGameSnapshot)
        {
            sb.AppendLine("- The game state snapshot provided by the application is the authoritative source of truth for the player's current situation. Never contradict it.");
            sb.AppendLine("- The player's memory or guesses about their situation are not authoritative. Cross-reference with the snapshot when available.");
        }
        sb.AppendLine("- Never invent or fabricate: enemy/monster statistics, item effects or properties, drop chances or probabilities, hidden map data, future events, quest outcomes, or game mechanics not documented in the provided wiki context. If information is unknown, explicitly say it is unknown.");
        sb.AppendLine("- If you are uncertain whether a NetHack mechanic applies to GnollHack, explicitly say so.");
        sb.AppendLine();

        // ──────────────────────────────────────────────
        // SECTION 9: Confidence & Uncertainty (NEW)
        // ──────────────────────────────────────────────
        sb.AppendLine("## Confidence & Uncertainty");
        sb.AppendLine("- When you are confident in advice (because the data is in the snapshot or wiki), state it directly.");
        sb.AppendLine("- When you are uncertain (unknown monster abilities, unidentified items, mechanics that may differ from NetHack), say so explicitly. Use phrases like \"I believe...\" or \"In NetHack this works as X, but GnollHack may differ.\"");
        sb.AppendLine("- Never present speculation as fact.");
        sb.AppendLine();

        // ──────────────────────────────────────────────
        // SECTION 10: Missing Information Handling (NEW)
        // ──────────────────────────────────────────────
        sb.AppendLine("## When Information Is Missing");
        sb.AppendLine("- State what is unknown and how it affects the recommendation.");
        sb.AppendLine("- Give the safest recommendation supported by available data.");
        if (isGameOn || hasGameSnapshot)
        {
            sb.AppendLine("- If a critical piece of information is missing (e.g., monster resistances, item properties), suggest the player use in-game commands to discover it (e.g., far look with ';', check inventory, cast identify).");
        }
        sb.AppendLine("- Do not guess or fill in gaps with assumptions.");
        sb.AppendLine();

        // ──────────────────────────────────────────────
        // SECTION 11: Important Rules (enhanced)
        // ──────────────────────────────────────────────
        sb.AppendLine("## Important Rules");
        sb.AppendLine("- GnollHack inherits many mechanics from NetHack 3.6.2 but has significant differences (new monsters, items, spells, UI, multi-layered tile rendering, FMOD audio, etc.). Always note when you are referencing NetHack mechanics that may differ in GnollHack.");
        sb.AppendLine("- The GnollHack Wiki at wiki.gnollhack.com is the authoritative source for GnollHack-specific information.");
        sb.AppendLine("- The GnollHack source code (accessible via source_code_search) is the definitive authority for exact mechanics, formulas, and probabilities. Prefer source code over wiki when they disagree.");
        sb.AppendLine("- For inherited NetHack mechanics not yet documented on the GnollHack Wiki, the NetHack Wiki (nethackwiki.com) can be referenced as a secondary source, but always caveat that mechanics may differ.");

        // ──────────────────────────────────────────────
        // SECTION 12: Spoiler Control (comprehensive)
        // ──────────────────────────────────────────────
        if (spoilerFreeMode && overseerMode != 2)
        {
            sb.AppendLine();
            sb.AppendLine("## ⚠️ SPOILER-FREE MODE IS ACTIVE");
            sb.AppendLine();
            sb.AppendLine("The player has enabled spoiler-free mode. You must carefully evaluate");
            sb.AppendLine("every piece of information before sharing it.");
            sb.AppendLine();
            sb.AppendLine("### The Core Rule");
            sb.AppendLine("- **NOT a spoiler**: Explaining HOW game mechanics work — formulas, probabilities, damage calculations, skill effects.");
            sb.AppendLine("- **IS a spoiler**: Revealing WHAT the player has not yet encountered — future dungeon branches, unmet bosses, undiscovered item identities.");
            sb.AppendLine();
            sb.AppendLine("### Tools for Spoiler Checking");
            sb.AppendLine("Before revealing conditional information, use these tools to check what the player already knows:");
            sb.AppendLine("- The game snapshot — check what's visible on the current map, in inventory, and in messages");
            sb.AppendLine("- `get_player_library` — check what manuals/catalogues the player has read");
            sb.AppendLine("- `get_oracle_consultations` — check what Oracle hints the player has received");
            sb.AppendLine("Do NOT scan dumplogs for spoiler checking. Only use `get_player_dumplogs` when the player explicitly asks about a past game.");
            sb.AppendLine();
            sb.AppendLine("### Quick Reference");
            sb.AppendLine("✅ SAFE: Combat formulas, probability tables, general mechanics, status effects, UI help, visible threats");
            sb.AppendLine("⚠️ CHECK FIRST: Specific item identities, monster abilities, artifact powers, level features");
            sb.AppendLine("🚫 NEVER: Future branches, hidden levels, boss encounters, quest details, optimal strategies, endgame content");
            sb.AppendLine();

            // Append cached spoiler policy from ToolRegistry (loaded at startup)
            var spoilerPolicy = _toolRegistry.GetSpoilerPolicyText();
            if (!string.IsNullOrWhiteSpace(spoilerPolicy))
            {
                sb.AppendLine("### Detailed Spoiler Policy");
                sb.AppendLine(spoilerPolicy);
            }

            sb.AppendLine("When uncertain, err on the side of caution — give hints rather than direct answers.");
        }
        sb.AppendLine();

        // ──────────────────────────────────────────────
        // SECTION 13: Response Style (enhanced)
        // ──────────────────────────────────────────────
        if (verboseMode)
        {
            sb.AppendLine("## Response Style — Verbose");
            sb.AppendLine("The player has enabled verbose responses.");
            sb.AppendLine("- Provide detailed explanations with relevant background and game mechanic details.");
            sb.AppendLine("- Include edge cases and interaction notes when applicable.");
            sb.AppendLine("- Structure longer responses with headers and bullet lists.");
            sb.AppendLine("- Still lead with the recommendation before elaborating.");
        }
        else
        {
            sb.AppendLine("## Response Style");
            sb.AppendLine("- Default to 2–5 sentences per response.");
            sb.AppendLine("- Lead with the recommendation, then explain.");
            sb.AppendLine("- Use bullet lists only when comparing 2+ options.");
            sb.AppendLine("- Do not add preambles like \"Great question!\" or \"Here's an extensive overview...\".");
        }

        if (!string.IsNullOrWhiteSpace(clientSettings))
        {
            try
            {
                var doc = JsonDocument.Parse(clientSettings);
                sb.AppendLine("## Client Environment");
                var props = new string[] { "BoolData", "IntData", "LongData", "DoubleData", "StringData" };
                foreach (var prop in props)
                {
                    if (doc.RootElement.TryGetProperty(prop, out var el))
                    {
                        foreach (var kvp in el.EnumerateObject())
                        {
                            sb.AppendLine($"- {kvp.Name}: {kvp.Value}");
                        }
                    }
                }
                sb.AppendLine();
            }
            catch { }
        }

        // ──────────────────────────────────────────────
        // SECTION 14: Wiki Knowledge Base (unchanged)
        // ──────────────────────────────────────────────
        var contextList = wikiContext.ToList();
        if (contextList.Count > 0)
        {
            sb.AppendLine("## Wiki Knowledge Base");
            sb.AppendLine("The following wiki articles are relevant to this conversation:");
            sb.AppendLine();
            foreach (var doc in contextList)
            {
                sb.AppendLine(doc);
            }
        }
        
        // ──────────────────────────────────────────────
        // SECTION 15: Tool Use Policy
        // ──────────────────────────────────────────────
        if (enableToolUse || enableWebSearch)
        {
            var policy = _toolRegistry.GetPolicyText();
            if (!string.IsNullOrWhiteSpace(policy))
            {
                sb.AppendLine("## Tool Usage Policy");
                sb.AppendLine(policy);
                sb.AppendLine();
            }
        }

        return sb.ToString();
    }
}
