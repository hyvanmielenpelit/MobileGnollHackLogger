using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using MobileGnollHackLogger.Data;
using Overseer.Controllers;

namespace Overseer.Services;

public class ChatService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly WikiService _wikiService;
    private readonly CryptoService _cryptoService;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;

    public ChatService(IServiceScopeFactory scopeFactory, WikiService wikiService, CryptoService cryptoService, IHttpClientFactory httpClientFactory, IConfiguration configuration)
    {
        _scopeFactory = scopeFactory;
        _wikiService = wikiService;
        _cryptoService = cryptoService;
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
    }

    public async IAsyncEnumerable<ChatEvent> StreamMessageAsync(long? sessionId, string message, List<SendMessageAttachment>? attachments, ClaimsPrincipal user, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId == null) yield break;

        long currentSessionId = 0;
        string? apiKey = null;
        string provider = "OpenAI";
        string model = "gpt-4o-mini";
        string? thinkingLevel = null;
        List<object> messageHistory = new();

        using (var scope = _scopeFactory.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var settings = await dbContext.UserAiSettings.FindAsync(userId);
            if (settings != null)
            {
                if (!string.IsNullOrEmpty(settings.DefaultProvider)) provider = settings.DefaultProvider;
                if (!string.IsNullOrEmpty(settings.DefaultModel)) model = settings.DefaultModel;
                if (!string.IsNullOrEmpty(settings.ThinkingLevel)) thinkingLevel = settings.ThinkingLevel;

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
                foreach (var pm in pastMessages)
                {
                    messageHistory.Add(new { role = pm.Role, content = pm.Content });
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
            }

            var contextDocs = _wikiService.GetRelevantContext(message);
            string systemPrompt = "You are the Gnoll Overseer, a helpful AI assistant for the roguelike game GnollHack. " +
                                  "Answer questions based on the provided context if available.\n\nContext:\n" + string.Join("\n", contextDocs);

            if (!messageHistory.Any(m => ((dynamic)m).role == "system"))
            {
                messageHistory.Insert(0, new { role = "system", content = systemPrompt });
            }

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

            if (imageAttachments.Count > 0)
            {
                if (provider.Equals("OpenAI", StringComparison.OrdinalIgnoreCase))
                {
                    var contentList = new List<object>();
                    contentList.Add(new { type = "text", text = finalMessageText });
                    foreach(var img in imageAttachments)
                    {
                        contentList.Add(new { type = "image_url", image_url = new { url = $"data:{img.ContentType};base64,{img.Base64Data}" } });
                    }
                    messageHistory.Add(new { role = "user", content = contentList });
                }
                else if (provider.Equals("Anthropic", StringComparison.OrdinalIgnoreCase))
                {
                    var contentList = new List<object>();
                    foreach(var img in imageAttachments)
                    {
                        contentList.Add(new { type = "image", source = new { type = "base64", media_type = img.ContentType, data = img.Base64Data } });
                    }
                    contentList.Add(new { type = "text", text = finalMessageText });
                    messageHistory.Add(new { role = "user", content = contentList });
                }
                else // Google
                {
                    var partsList = new List<object>();
                    partsList.Add(new { text = finalMessageText });
                    foreach(var img in imageAttachments)
                    {
                        partsList.Add(new { inlineData = new { mimeType = img.ContentType, data = img.Base64Data } });
                    }
                    messageHistory.Add(new { role = "user", parts = partsList });
                }
            }
            else
            {
                messageHistory.Add(new { role = "user", content = finalMessageText });
            }
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

            var requestBody = new Dictionary<string, object>
            {
                { "model", model },
                { "system", systemPrompt },
                { "messages", userAsstMessages },
                { "stream", true },
                { "max_tokens", 4096 }
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
}
