using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using MobileGnollHackLogger.Data;
using Overseer.Controllers;
using Microsoft.AspNetCore.SignalR;
using Overseer.Hubs;

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

    public ChatService(IServiceScopeFactory scopeFactory, WikiService wikiService, CryptoService cryptoService, IHttpClientFactory httpClientFactory, IConfiguration configuration, IHubContext<ChatHub> hubContext, ModelMetadataService modelMetadataService)
    {
        _scopeFactory = scopeFactory;
        _wikiService = wikiService;
        _cryptoService = cryptoService;
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _hubContext = hubContext;
        _modelMetadataService = modelMetadataService;
    }

    public async Task GenerateAndBroadcastMessageAsync(long sessionId, string message, List<SendMessageAttachment>? attachments, string userId, CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var evt in StreamMessageAsync(sessionId, message, attachments, userId, cancellationToken))
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

    public async IAsyncEnumerable<ChatEvent> StreamMessageAsync(long? sessionId, string message, List<SendMessageAttachment>? attachments, string userId, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(userId)) yield break;

        long currentSessionId = 0;
        string? apiKey = null;
        string provider = "OpenAI";
        string model = "gpt-4o-mini";
        string? thinkingLevel = null;
        List<object> messageHistory = new();
        bool spoilerFreeMode = false;
        int? maxInputTokens = null;
        int? maxOutputTokens = null;

        using (var scope = _scopeFactory.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var settings = await dbContext.UserAiSettings.FindAsync(userId);
            if (settings != null)
            {
                if (!string.IsNullOrEmpty(settings.DefaultProvider)) provider = settings.DefaultProvider;
                if (!string.IsNullOrEmpty(settings.DefaultModel)) model = settings.DefaultModel;
                if (!string.IsNullOrEmpty(settings.ThinkingLevel)) thinkingLevel = settings.ThinkingLevel;
                spoilerFreeMode = settings.SpoilerFreeMode;
                maxInputTokens = settings.MaxInputTokens;
                maxOutputTokens = settings.MaxOutputTokens;

                if (!string.IsNullOrEmpty(settings.EncryptedApiKey) && !string.IsNullOrEmpty(settings.ApiKeyNonce) && !string.IsNullOrEmpty(settings.ApiKeyTag))
                {
                    apiKey = _cryptoService.Decrypt(settings.EncryptedApiKey, settings.ApiKeyNonce, settings.ApiKeyTag, userId);
                }
            }

            if (string.IsNullOrEmpty(apiKey))
            {
                yield return new ChatEvent { Type = "error", Data = "Error: API Key not configured. Please configure it in Settings." };
                yield break;
            }

            bool hasGameSnapshot = false;
            bool hasDebugData = false;
            bool hasDirectoryManifest = false;
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
                    var msgAtts = pastAttachments.Where(a => a.ChatMessageId == pm.Id && a.ContentType.StartsWith("image/")).ToList();
                    if (msgAtts.Count > 0 && recentMessageIds.Contains(pm.Id) && !string.IsNullOrEmpty(baseDir))
                    {
                        var msgImageAttachments = new List<SendMessageAttachment>();
                        foreach (var att in msgAtts)
                        {
                            var fullPath = Path.Combine(baseDir, att.RelativePath);
                            if (System.IO.File.Exists(fullPath))
                            {
                                var bytes = System.IO.File.ReadAllBytes(fullPath);
                                msgImageAttachments.Add(new SendMessageAttachment { ContentType = att.ContentType, Base64Data = Convert.ToBase64String(bytes) });
                            }
                        }
                        
                        if (msgImageAttachments.Count > 0)
                        {
                            messageHistory.Add(FormatMessage(provider, pm.Role, pm.Content ?? "", msgImageAttachments));
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
                        if (pm.Content.StartsWith("Game Context Snapshot:")) hasGameSnapshot = true;
                        if (pm.Content.StartsWith("Developer Debug Data:")) hasDebugData = true;
                        if (pm.Content.StartsWith("Game Directory Manifest:")) hasDirectoryManifest = true;
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

            bool verboseMode = false;
            bool isGameOn = false;
            bool developerMode = false;
            int overseerMode = 0; // 0=gameplay, 1=technical, 2=developer

            if (!string.IsNullOrEmpty(session?.ClientSettings))
            {
                try
                {
                    var clientSettings = JsonDocument.Parse(session.ClientSettings);
                    var root = clientSettings.RootElement;

                    if (root.TryGetProperty("boolSettings", out var boolSettings))
                    {
                        if (boolSettings.TryGetProperty("allowSpoilers", out var spoilerVal))
                            spoilerFreeMode = !spoilerVal.GetBoolean();

                        if (boolSettings.TryGetProperty("verboseResponses", out var verboseVal))
                            verboseMode = verboseVal.GetBoolean();

                        if (boolSettings.TryGetProperty("isGameOn", out var gameOnVal))
                            isGameOn = gameOnVal.GetBoolean();

                        if (boolSettings.TryGetProperty("developerMode", out var devVal))
                            developerMode = devVal.GetBoolean();
                    }

                    if (root.TryGetProperty("intSettings", out var intSettings))
                    {
                        if (intSettings.TryGetProperty("overseerMode", out var modeVal))
                            overseerMode = modeVal.GetInt32();
                    }
                }
                catch (JsonException) { }
            }

            string systemPrompt = BuildSystemPrompt(contextDocs, spoilerFreeMode, verboseMode, isGameOn, developerMode, overseerMode, hasGameSnapshot, hasDebugData, hasDirectoryManifest, hasMessageHistory);

            var userMsg = new ChatMessage
            {
                ChatSessionId = currentSessionId,
                Role = "user",
                Content = message,
                TimestampUtc = DateTime.UtcNow
            };
            dbContext.ChatMessage.Add(userMsg);
            
            session.LastMessageUtc = DateTime.UtcNow;
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

        }

        string fullResponse = "";
        
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
                if (evt.Type == "chunk") fullResponse += evt.Data;
                yield return evt;
            }
        }
        else if (provider.Equals("Anthropic", StringComparison.OrdinalIgnoreCase))
        {
            var httpClient = _httpClientFactory.CreateClient();
            httpClient.DefaultRequestHeaders.Add("x-api-key", apiKey);
            httpClient.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");

            var systemMessages = messageHistory.Where(m => ((dynamic)m).role == "system").Select(m => ((dynamic)m).content).Cast<string>().ToList();
            string systemPrompt = string.Join("\n\n", systemMessages);
            
            var userAsstMessages = messageHistory.Where(m => ((dynamic)m).role != "system").ToList();

            int defaultAnthropicTokens = _configuration.GetValue<int?>("DefaultMaxOutputTokens:Anthropic") ?? 8192;

            var requestBody = new Dictionary<string, object>
            {
                { "model", model },
                { "system", systemPrompt },
                { "messages", userAsstMessages },
                { "stream", true },
                { "max_tokens", maxOutputTokens ?? defaultAnthropicTokens }
            };

            if (!string.IsNullOrEmpty(thinkingLevel))
            {
                requestBody["thinking"] = new { type = "enabled", budget_tokens = 1024 }; 
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
                if (evt.Type == "chunk") fullResponse += evt.Data;
                yield return evt;
            }
        }
        else if (provider.Equals("Google", StringComparison.OrdinalIgnoreCase))
        {
            var httpClient = _httpClientFactory.CreateClient();
            var contents = new List<object>();
            string systemInstruction = "";

            foreach (var m in messageHistory)
            {
                var role = ((dynamic)m).role;
                var dict = m as IDictionary<string, object>;
                var hasParts = m.GetType().GetProperty("parts") != null;
                
                if (role == "system")
                {
                    var content = ((dynamic)m).content;
                    systemInstruction += content + "\n";
                }
                else
                {
                    if (hasParts)
                    {
                        contents.Add(new
                        {
                            role = role == "user" ? "user" : "model",
                            parts = ((dynamic)m).parts
                        });
                    }
                    else
                    {
                        var content = ((dynamic)m).content;
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
                if (evt.Type == "chunk") fullResponse += evt.Data;
                yield return evt;
            }
        }
        else
        {
            yield return new ChatEvent { Type = "error", Data = $"Provider {provider} is not fully implemented yet in this demo." };
            fullResponse = $"Provider {provider} is not fully implemented yet in this demo.";
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
                    TimestampUtc = DateTime.UtcNow
                };
                dbContext.ChatMessage.Add(asstMsg);
                session.LastMessageUtc = DateTime.UtcNow;
                await dbContext.SaveChangesAsync(CancellationToken.None);
            }
        }
    }

    private async IAsyncEnumerable<ChatEvent> ExecuteApiWithRetriesAsync(
        Func<CancellationToken, Task<HttpResponseMessage>> requestFactory,
        Func<HttpResponseMessage, CancellationToken, IAsyncEnumerable<string>> streamParser,
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
            
            if (response.IsSuccessStatusCode)
            {
                yield return new ChatEvent { Type = "debug", Data = $"[{providerName}] HTTP {(int)response.StatusCode} Received ({sw.ElapsedMilliseconds}ms)" };
                yield return new ChatEvent { Type = "status", Data = $"Streaming response..." };
                
                await foreach (var chunk in streamParser(response, cancellationToken))
                {
                    yield return new ChatEvent { Type = "chunk", Data = chunk };
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

    private async IAsyncEnumerable<string> ParseOpenAIStream(HttpResponseMessage response, [EnumeratorCancellation] CancellationToken cancellationToken)
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
                if (data == "[DONE]") break;

                string? chunk = null;
                try
                {
                    var json = JsonSerializer.Deserialize<JsonElement>(data);
                    if (json.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
                    {
                        var delta = choices[0].GetProperty("delta");
                        if (delta.TryGetProperty("content", out var content))
                        {
                            chunk = content.GetString();
                        }
                    }
                }
                catch { /* ignore */ }
                
                if (!string.IsNullOrEmpty(chunk))
                {
                    yield return chunk;
                }
            }
        }
    }

    private async IAsyncEnumerable<string> ParseAnthropicStream(HttpResponseMessage response, [EnumeratorCancellation] CancellationToken cancellationToken)
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
                string? chunk = null;
                try
                {
                    var json = JsonSerializer.Deserialize<JsonElement>(data);
                    if (json.TryGetProperty("type", out var type) && type.GetString() == "content_block_delta")
                    {
                        var delta = json.GetProperty("delta");
                        if (delta.TryGetProperty("text", out var text))
                        {
                            chunk = text.GetString();
                        }
                    }
                }
                catch { /* ignore */ }
                
                if (!string.IsNullOrEmpty(chunk))
                {
                    yield return chunk;
                }
            }
        }
    }

    private async IAsyncEnumerable<string> ParseGeminiStream(HttpResponseMessage response, [EnumeratorCancellation] CancellationToken cancellationToken)
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
                string? chunk = null;
                try
                {
                    var json = JsonSerializer.Deserialize<JsonElement>(data);
                    if (json.TryGetProperty("candidates", out var candidates) && candidates.GetArrayLength() > 0)
                    {
                        var parts = candidates[0].GetProperty("content").GetProperty("parts");
                        if (parts.GetArrayLength() > 0 && parts[0].TryGetProperty("text", out var text))
                        {
                            chunk = text.GetString();
                        }
                    }
                }
                catch { /* ignore */ }
                
                if (!string.IsNullOrEmpty(chunk))
                {
                    yield return chunk;
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
    
    private List<object> AlternateAnthropicMessages(List<object> messages)
    {
        var combined = new List<object>();
        object? lastMsg = null;
        foreach (var msg in messages)
        {
            if (lastMsg != null && ((dynamic)lastMsg).role == ((dynamic)msg).role && ((dynamic)msg).role != "system")
            {
                var role = ((dynamic)msg).role;
                var c1 = ((dynamic)lastMsg).content;
                var c2 = ((dynamic)msg).content;
                
                var list = new List<object>();
                if (c1 is string s1) list.Add(new { type = "text", text = s1 });
                else if (c1 is IEnumerable<object> e1) list.AddRange(e1);
                
                if (c2 is string s2) list.Add(new { type = "text", text = "\n\n" + s2 });
                else if (c2 is IEnumerable<object> e2) list.AddRange(e2);
                
                lastMsg = new { role = role, content = list };
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

    private static string BuildSystemPrompt(
        IEnumerable<string> wikiContext,
        bool spoilerFreeMode,
        bool verboseMode,
        bool isGameOn,
        bool developerMode,
        int overseerMode,
        bool hasGameSnapshot,
        bool hasDebugData,
        bool hasDirectoryManifest,
        bool hasMessageHistory)
    {
        var sb = new StringBuilder();

        sb.AppendLine("You are the Gnoll Overseer, an expert AI assistant for GnollHack — a modern roguelike game derived from NetHack 3.6.2 with extensive new features including tile graphics, sound, music, voiceovers, and a .NET MAUI mobile/desktop frontend.");
        sb.AppendLine();

        // --- Overseer Mode ---
        switch (overseerMode)
        {
            case 1: // Technical Help
                sb.AppendLine();
                sb.AppendLine("## Mode: Technical Support");
                sb.AppendLine("The user needs help with app issues, save files, configuration, or installation.");
                sb.AppendLine("Be precise and diagnostic. Ask clarifying questions about the user's platform, app version, and error messages.");
                sb.AppendLine("Focus on actionable troubleshooting steps rather than gameplay advice.");
                break;
            case 2: // Developer / Debugging
                sb.AppendLine();
                sb.AppendLine("## Mode: Developer / Debugging");
                sb.AppendLine("The user is a GnollHack developer or power user debugging the game.");
                sb.AppendLine("Be technical and terse. Reference source code files, function names, and data structures.");
                sb.AppendLine("The GnollHack C core is in src/ and include/, the .NET MAUI frontend in win/win32/xpl/.");
                sb.AppendLine("Analyze debug data, crash logs, and runtime state when available.");
                if (developerMode)
                    sb.AppendLine("Developer Mode is ON — the user has access to Wizard Mode (#wizmode) for testing.");
                break;
            default: // 0 = Gameplay Help
                sb.AppendLine();
                sb.AppendLine("## Mode: Gameplay Help");
                sb.AppendLine("The user is playing GnollHack and needs gameplay assistance.");
                sb.AppendLine("Be friendly and encouraging. Provide tactical advice, item identification help, strategy tips, and dungeon navigation guidance.");
                break;
        }

        // --- Capabilities ---
        sb.AppendLine("## Your Capabilities");
        sb.AppendLine("- **Player Assistance**: Tactical advice, item identification, strategy tips, monster info, spell recommendations, dungeon navigation");
        sb.AppendLine("- **Technical Support**: Save file troubleshooting, app issues, file recovery guidance, configuration help");
        if (hasDebugData)
        {
            sb.AppendLine("- **Developer Assistance**: This session includes runtime debug data. You can help diagnose crashes, runtime errors, save corruption, pending task issues, and provide code-level guidance for the GnollHack C core and .NET MAUI frontend.");
        }
        sb.AppendLine();

        // --- Available Context ---
        sb.AppendLine("## Available Context in This Session");
        if (hasGameSnapshot) sb.AppendLine("- ✅ Game snapshot (current map, stats, inventory, recent messages, spells, skills, attributes)");
        if (hasMessageHistory) sb.AppendLine("- ✅ Full message history (up to 16384 in-game messages) — reference when the player asks about earlier events");
        if (hasDirectoryManifest) sb.AppendLine("- ✅ Game directory file listing — use for diagnosing file-related issues (corrupt saves, orphaned files, missing data)");
        if (hasDebugData) sb.AppendLine("- ✅ Runtime debug data (memory usage, thread state, pending tasks, developer settings)");
        if (!hasGameSnapshot && !hasMessageHistory && !hasDirectoryManifest && !hasDebugData)
        {
            sb.AppendLine("- No game context was provided for this session. Answer based on general GnollHack knowledge and wiki content.");
        }
        sb.AppendLine();

        // --- Session Context ---
        if (!isGameOn && !hasGameSnapshot)
        {
            sb.AppendLine("## Session Context");
            sb.AppendLine("This session was started from the main menu (no active game). The player may be asking general questions about GnollHack, seeking help with the app, or browsing dumplogs from previous games.");
        }

        // --- Important Rules ---
        sb.AppendLine("## Important Rules");
        sb.AppendLine("- GnollHack inherits many mechanics from NetHack 3.6.2 but has significant differences (new monsters, items, spells, UI, multi-layered tile rendering, FMOD audio, etc.). Always note when you are referencing NetHack mechanics that may differ in GnollHack.");
        sb.AppendLine("- The GnollHack Wiki at wiki.gnollhack.com is the authoritative source for GnollHack-specific information.");
        sb.AppendLine("- For inherited NetHack mechanics not yet documented on the GnollHack Wiki, the NetHack Wiki (nethackwiki.com) can be referenced as a secondary source, but always caveat that mechanics may differ.");

        // --- Spoiler Control ---
        if (spoilerFreeMode)
        {
            sb.AppendLine();
            sb.AppendLine("## ⚠️ SPOILER-FREE MODE IS ACTIVE");
            sb.AppendLine("The player has enabled spoiler-free mode. You MUST follow these rules:");
            sb.AppendLine("- Give hints and guidance WITHOUT revealing specific solutions, item identities, or optimal strategies directly.");
            sb.AppendLine("- Help the player discover things on their own. Use phrases like \"you might want to try...\" or \"there's something interesting about that item...\" instead of direct answers.");
            sb.AppendLine("- Do NOT reveal: specific artifact names/powers, optimal ascension kit items, fountain/altar interaction outcomes, or puzzle solutions.");
            sb.AppendLine("- You CAN: explain general mechanics, warn about immediate dangers visible in the snapshot, clarify UI elements, and help with technical issues (these are not spoilers).");
        }
        sb.AppendLine();

        // --- Verbose Mode ---
        if (verboseMode)
        {
            sb.AppendLine("## Verbose Mode");
            sb.AppendLine("The player has enabled verbose responses. Provide detailed explanations, include relevant background information, and elaborate on game mechanics when applicable.");
        }
        else
        {
            sb.AppendLine("## Response Style");
            sb.AppendLine("Keep responses concise and action-oriented. Provide direct answers without excessive elaboration.");
        }

        // --- Wiki Context ---
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

        return sb.ToString();
    }
}
