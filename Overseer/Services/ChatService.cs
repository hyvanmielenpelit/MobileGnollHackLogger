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

    public async IAsyncEnumerable<string> StreamMessageAsync(long? sessionId, string message, List<SendMessageAttachment>? attachments, ClaimsPrincipal user, [EnumeratorCancellation] CancellationToken cancellationToken)
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
                yield return "Error: API Key not configured. Please configure it in Settings.";
                yield break;
            }

            ChatSession? session = null;
            if (sessionId.HasValue && sessionId.Value > 0)
            {
                session = await dbContext.ChatSession.FindAsync(sessionId.Value);
                if (session == null || session.AspNetUserId != userId)
                {
                    yield return "Error: Session not found.";
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
                        catch (Exception ex)
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
                // Multimodal format for the current message
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
                    messageHistory.Add(new { role = "user", parts = partsList }); // Wait, Gemini requires 'parts' inside 'content' but our history just has 'role' and 'content' right now?
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

            var requestContent = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
            var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions")
            {
                Content = requestContent
            };

            using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync(cancellationToken);
                yield return $"API Error: {response.StatusCode} - {error}";
                yield break;
            }

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
                        fullResponse += chunk;
                        yield return chunk;
                    }
                }
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
                // Note: Anthropic's new thinking format might vary, passing as a top-level property for now
                requestBody["thinking"] = new { type = "enabled", budget_tokens = 1024 }; 
            }

            var requestContent = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
            var request = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages")
            {
                Content = requestContent
            };

            using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync(cancellationToken);
                yield return $"API Error: {response.StatusCode} - {error}";
                yield break;
            }

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
                        fullResponse += chunk;
                        yield return chunk;
                    }
                }
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

            // Gemini does not yet officially support ThinkingLevel in the REST API generationConfig
            // We'll skip sending it to prevent BadRequest errors, or if supported in the future, we'll map it here.

            var requestContent = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
            var request = new HttpRequestMessage(HttpMethod.Post, $"https://generativelanguage.googleapis.com/v1beta/models/{model}:streamGenerateContent?alt=sse&key={apiKey}")
            {
                Content = requestContent
            };

            using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync(cancellationToken);
                yield return $"API Error: {response.StatusCode} - {error}";
                yield break;
            }

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
                        fullResponse += chunk;
                        yield return chunk;
                    }
                }
            }
        }
        else
        {
            yield return $"Provider {provider} is not fully implemented yet in this demo.";
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
}
