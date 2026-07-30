using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using MobileGnollHackLogger.Data;

namespace Overseer.Services;

public class ChatService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly WikiService _wikiService;
    private readonly CryptoService _cryptoService;
    private readonly IHttpClientFactory _httpClientFactory;

    public ChatService(IServiceScopeFactory scopeFactory, WikiService wikiService, CryptoService cryptoService, IHttpClientFactory httpClientFactory)
    {
        _scopeFactory = scopeFactory;
        _wikiService = wikiService;
        _cryptoService = cryptoService;
        _httpClientFactory = httpClientFactory;
    }

    public async IAsyncEnumerable<string> StreamMessageAsync(long? sessionId, string message, ClaimsPrincipal user, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId == null) yield break;

        long currentSessionId = 0;
        string? apiKey = null;
        string provider = "OpenAI";
        string model = "gpt-4o-mini";
        List<object> messageHistory = new();

        using (var scope = _scopeFactory.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var settings = await dbContext.UserAiSettings.FindAsync(userId);
            if (settings != null)
            {
                if (!string.IsNullOrEmpty(settings.DefaultProvider)) provider = settings.DefaultProvider;
                if (!string.IsNullOrEmpty(settings.DefaultModel)) model = settings.DefaultModel;

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
            
            messageHistory.Add(new { role = "user", content = message });
        }

        string fullResponse = "";
        
        if (provider.Equals("OpenAI", StringComparison.OrdinalIgnoreCase))
        {
            var httpClient = _httpClientFactory.CreateClient();
            httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");

            var requestBody = new
            {
                model = model,
                messages = messageHistory,
                stream = true
            };

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
                    catch { /* ignore json parse errors for partial chunks */ }
                    
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
