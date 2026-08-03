import re
import sys

with open(r'Overseer\Services\ChatService.cs', 'r', encoding='utf-8') as f:
    content = f.read()

# 1. Update Main Chat Debug Logs
content = content.replace('[{providerName}]', '[Main Chat - {providerName}]')

# 2. Update GenerateTitleAsync implementation
new_generate = r"""    internal async Task GenerateTitleAsync(long sessionId, string userMessage, string userId)
    {
        CancelTitleGeneration(sessionId);
        
        var cts = new CancellationTokenSource();
        _titleCancellationTokens[sessionId] = cts;
        var cancellationToken = cts.Token;

        try
        {
            await Task.Delay(2500, cancellationToken); // Give the UI time to navigate and join the SignalR session
            
            await _hubContext.Clients.Group(sessionId.ToString()).SendAsync("ReceiveChatEvent", new ChatEvent { Type = "debug", Data = $"[Title Gen] Task started for session {sessionId}." }, CancellationToken.None);
            
            var initialStatus = new { sessionId = sessionId, status = "Generating AI title..." };
            await _hubContext.Clients.Group(sessionId.ToString()).SendAsync("ReceiveChatEvent", new ChatEvent { Type = "title_status", Data = System.Text.Json.JsonSerializer.Serialize(initialStatus) }, CancellationToken.None);
            
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var cryptoService = scope.ServiceProvider.GetRequiredService<CryptoService>();
            
            var settings = await dbContext.UserAiSettings.FindAsync(new object[] { userId }, cancellationToken);
            UserAiModel? model = null;
            
            if (settings?.TitleGenerationModelId != null)
            {
                model = await dbContext.UserAiModels.FirstOrDefaultAsync(m => m.Id == settings.TitleGenerationModelId && m.AspNetUserId == userId, cancellationToken);
            }
            
            if (model == null)
            {
                model = await dbContext.UserAiModels
                    .Where(m => m.AspNetUserId == userId)
                    .OrderBy(m => m.OrderIndex)
                    .FirstOrDefaultAsync(cancellationToken);
            }

            if (model == null)
            {
                await _hubContext.Clients.Group(sessionId.ToString()).SendAsync("ReceiveChatEvent", new ChatEvent { Type = "debug", Data = $"[Title Gen] Aborted: No AI model configured." }, CancellationToken.None);
                return;
            }

            var apiKeyEntry = await dbContext.UserAiApiKeys.FirstOrDefaultAsync(k => k.AspNetUserId == userId && k.Provider == model.Provider, cancellationToken);
            if (apiKeyEntry == null || string.IsNullOrEmpty(apiKeyEntry.EncryptedApiKey) || string.IsNullOrEmpty(apiKeyEntry.ApiKeyNonce) || string.IsNullOrEmpty(apiKeyEntry.ApiKeyTag))
            {
                await _hubContext.Clients.Group(sessionId.ToString()).SendAsync("ReceiveChatEvent", new ChatEvent { Type = "debug", Data = $"[Title Gen] Aborted: No API key found for provider {model.Provider}." }, CancellationToken.None);
                return;
            }
            
            string apiKey = cryptoService.Decrypt(apiKeyEntry.EncryptedApiKey, apiKeyEntry.ApiKeyNonce, apiKeyEntry.ApiKeyTag, userId);
            
            string prompt = "Generate a descriptive 3 to 8 word title for this conversation based on the user's message. Output ONLY the title itself, with no quotes, no punctuation, and no extra text. Do NOT use single-word abbreviations.";
            string generatedTitle = string.Empty;
            
            var client = _httpClientFactory.CreateClient();
            
            HttpRequestMessage? requestMessage = null;
            string reqBodyStr = "";
            
            if (model.Provider == "OpenAI")
            {
                var reqBody = new {
                    model = model.ModelId,
                    messages = new[] {
                        new { role = "system", content = prompt },
                        new { role = "user", content = userMessage }
                    },
                    max_tokens = 50,
                    temperature = 0.5
                };
                reqBodyStr = System.Text.Json.JsonSerializer.Serialize(reqBody);
                requestMessage = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions");
                requestMessage.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
            }
            else if (model.Provider == "Anthropic")
            {
                client.DefaultRequestHeaders.Add("x-api-key", apiKey);
                client.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
                var reqBody = new {
                    model = model.ModelId,
                    system = prompt,
                    messages = new[] {
                        new { role = "user", content = userMessage }
                    },
                    max_tokens = 1024,
                    temperature = 0.5
                };
                reqBodyStr = System.Text.Json.JsonSerializer.Serialize(reqBody);
                requestMessage = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages");
            }
            else if (model.Provider == "Google")
            {
                var reqBody = new Dictionary<string, object>
                {
                    { "system_instruction", new { parts = new[] { new { text = prompt } } } },
                    { "contents", new[] { new { role = "user", parts = new[] { new { text = userMessage } } } } },
                    { "generationConfig", new { maxOutputTokens = 50, temperature = 0.5 } }
                };
                reqBodyStr = System.Text.Json.JsonSerializer.Serialize(reqBody);
                requestMessage = new HttpRequestMessage(HttpMethod.Post, $"https://generativelanguage.googleapis.com/v1beta/models/{model.ModelId}:generateContent?key={apiKey}");
            }

            if (requestMessage != null)
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                await _hubContext.Clients.Group(sessionId.ToString()).SendAsync("ReceiveChatEvent", new ChatEvent { Type = "debug", Data = $"[Title Gen - {model.Provider}] Starting POST request to {requestMessage.RequestUri}..." }, CancellationToken.None);
                
                HttpResponseMessage? response = null;
                int[] retryDelays = { 1000, 3000, 5000, 10000, 15000 };
                
                for (int i = 0; i <= retryDelays.Length; i++)
                {
                    try
                    {
                        var reqClone = new HttpRequestMessage(requestMessage.Method, requestMessage.RequestUri)
                        {
                            Content = new StringContent(reqBodyStr, Encoding.UTF8, "application/json")
                        };
                        foreach (var hdr in requestMessage.Headers) reqClone.Headers.TryAddWithoutValidation(hdr.Key, hdr.Value);
                        
                        cancellationToken.ThrowIfCancellationRequested();
                        
                        if (i > 0)
                        {
                            var retryStatus = new { sessionId = sessionId, status = $"Retrying title generation ({i}/{retryDelays.Length})..." };
                            await _hubContext.Clients.Group(sessionId.ToString()).SendAsync("ReceiveChatEvent", new ChatEvent { Type = "title_status", Data = System.Text.Json.JsonSerializer.Serialize(retryStatus) }, CancellationToken.None);
                        }
                        
                        response = await client.SendAsync(reqClone, cancellationToken);
                        
                        await _hubContext.Clients.Group(sessionId.ToString()).SendAsync("ReceiveChatEvent", new ChatEvent { Type = "debug", Data = $"[Title Gen - {model.Provider}] HTTP {(int)response.StatusCode} Received ({sw.ElapsedMilliseconds}ms, Attempt {i + 1})" }, CancellationToken.None);
                        
                        if (response.StatusCode != System.Net.HttpStatusCode.ServiceUnavailable || i == retryDelays.Length)
                        {
                            break;
                        }
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex)
                    {
                        await _hubContext.Clients.Group(sessionId.ToString()).SendAsync("ReceiveChatEvent", new ChatEvent { Type = "debug", Data = $"[Title Gen - {model.Provider}] Request failed: {ex.Message} (Attempt {i + 1})" }, CancellationToken.None);
                        if (i == retryDelays.Length) throw;
                    }
                    
                    int delayMs = retryDelays[i];
                    await _hubContext.Clients.Group(sessionId.ToString()).SendAsync("ReceiveChatEvent", new ChatEvent { Type = "debug", Data = $"[Title Gen - {model.Provider}] Sleeping for {delayMs / 1000.0}s before retry..." }, CancellationToken.None);
                    await Task.Delay(delayMs, cancellationToken);
                }
                
                if (response != null && response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync(cancellationToken);
                    var root = System.Text.Json.JsonDocument.Parse(json).RootElement;
                    
                    if (model.Provider == "OpenAI")
                    {
                        if (root.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
                        {
                            var content = choices[0].GetProperty("message").GetProperty("content").GetString();
                            if (!string.IsNullOrEmpty(content)) generatedTitle = content.Trim('"', '\'', ' ', '.', '\n', '\r', '\t');
                        }
                    }
                    else if (model.Provider == "Anthropic")
                    {
                        if (root.TryGetProperty("content", out var contentArray) && contentArray.GetArrayLength() > 0)
                        {
                            var content = contentArray[0].GetProperty("text").GetString();
                            if (!string.IsNullOrEmpty(content)) generatedTitle = content.Trim('"', '\'', ' ', '.', '\n', '\r', '\t');
                        }
                    }
                    else if (model.Provider == "Google")
                    {
                        if (root.TryGetProperty("candidates", out var candidates) && candidates.GetArrayLength() > 0)
                        {
                            try 
                            {
                                if (candidates[0].TryGetProperty("content", out var contentProp) && contentProp.TryGetProperty("parts", out var partsProp) && partsProp.GetArrayLength() > 0)
                                {
                                    var content = partsProp[0].GetProperty("text").GetString();
                                    if (!string.IsNullOrEmpty(content)) generatedTitle = content.Trim('"', '\'', ' ', '.', '\n', '\r', '\t');
                                }
                            } catch { }
                        }
                    }
                    
                    if (string.IsNullOrWhiteSpace(generatedTitle))
                    {
                        await _hubContext.Clients.Group(sessionId.ToString()).SendAsync("ReceiveChatEvent", new ChatEvent { Type = "debug", Data = $"[Title Gen - {model.Provider}] Warning: API returned empty or whitespace title." }, CancellationToken.None);
                    }
                    else
                    {
                        await _hubContext.Clients.Group(sessionId.ToString()).SendAsync("ReceiveChatEvent", new ChatEvent { Type = "debug", Data = $"[Title Gen - {model.Provider}] Generated Title: \"{generatedTitle}\"" }, CancellationToken.None);
                    }
                }
                else if (response != null)
                {
                    var errorStr = await response.Content.ReadAsStringAsync(CancellationToken.None);
                    await _hubContext.Clients.Group(sessionId.ToString()).SendAsync("ReceiveChatEvent", new ChatEvent { Type = "debug", Data = $"[Title Gen - {model.Provider}] API Error: {errorStr}" }, CancellationToken.None);
                    try
                    {
                        System.IO.File.WriteAllText($@"C:\Users\TommiGustafsson\.gemini\antigravity\brain\527f1941-808c-47af-84c6-4f4161647aea\scratch\api_error_{Guid.NewGuid()}.txt", errorStr);
                    }
                    catch { }
                }
            }

            if (!string.IsNullOrWhiteSpace(generatedTitle))
            {
                var session = await dbContext.ChatSession.FindAsync(new object[] { sessionId }, cancellationToken);
                if (session != null)
                {
                    session.Title = generatedTitle;
                    await dbContext.SaveChangesAsync(cancellationToken);
                    
                    var titleUpdateData = new { sessionId = sessionId, title = generatedTitle };
                    var evt = new ChatEvent { Type = "title_update", Data = System.Text.Json.JsonSerializer.Serialize(titleUpdateData) };
                    await _hubContext.Clients.Group(sessionId.ToString()).SendAsync("ReceiveChatEvent", evt, CancellationToken.None);
                }
            }
            
            var successStatus = new { sessionId = sessionId, status = "" };
            await _hubContext.Clients.Group(sessionId.ToString()).SendAsync("ReceiveChatEvent", new ChatEvent { Type = "title_status", Data = System.Text.Json.JsonSerializer.Serialize(successStatus) }, CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
            var cancelStatus = new { sessionId = sessionId, status = "canceled" };
            await _hubContext.Clients.Group(sessionId.ToString()).SendAsync("ReceiveChatEvent", new ChatEvent { Type = "title_status", Data = System.Text.Json.JsonSerializer.Serialize(cancelStatus) }, CancellationToken.None);
        }
        catch (Exception ex)
        {
            await _hubContext.Clients.Group(sessionId.ToString()).SendAsync("ReceiveChatEvent", new ChatEvent { Type = "debug", Data = $"[Title Gen - Exception] {ex.GetType().Name}: {ex.Message}" }, CancellationToken.None);
            
            var errorStatus = new { sessionId = sessionId, status = "" };
            await _hubContext.Clients.Group(sessionId.ToString()).SendAsync("ReceiveChatEvent", new ChatEvent { Type = "title_status", Data = System.Text.Json.JsonSerializer.Serialize(errorStatus) }, CancellationToken.None);
        }
    }"""

start_idx = content.find("    internal async Task GenerateTitleAsync")
end_idx = content.rfind("    }") # End of the class ChatService

if start_idx != -1 and end_idx != -1:
    class_end = content.rfind("}", 0, end_idx) # End of the last method before ChatService closes
    
    content = content[:start_idx] + new_generate + "\n" + content[end_idx:]
    with open(r'Overseer\Services\ChatService.cs', 'w', encoding='utf-8') as f:
        f.write(content)
else:
    print("Could not find start or end index.")
    sys.exit(1)
