import re

with open(r"c:\hmp\MobileGnollHackLogger\Overseer\Services\ChatService.cs", "r", encoding="utf-8") as f:
    content = f.read()

# 1. Inject ModelMetadataService
content = content.replace("private readonly IHubContext<ChatHub> _hubContext;", "private readonly IHubContext<ChatHub> _hubContext;\n    private readonly ModelMetadataService _modelMetadataService;")
content = content.replace("public ChatService(IServiceScopeFactory scopeFactory, WikiService wikiService, CryptoService cryptoService, IHttpClientFactory httpClientFactory, IConfiguration configuration, IHubContext<ChatHub> hubContext)", "public ChatService(IServiceScopeFactory scopeFactory, WikiService wikiService, CryptoService cryptoService, IHttpClientFactory httpClientFactory, IConfiguration configuration, IHubContext<ChatHub> hubContext, ModelMetadataService modelMetadataService)")
content = content.replace("_hubContext = hubContext;", "_hubContext = hubContext;\n        _modelMetadataService = modelMetadataService;")

# 2. Add tokens
content = content.replace(
"""        long currentSessionId = 0;
        string? apiKey = null;
        string provider = "OpenAI";
        string model = "gpt-4o-mini";
        string? thinkingLevel = null;
        List<object> messageHistory = new();
        bool spoilerFreeMode = false;""",
"""        long currentSessionId = 0;
        string? apiKey = null;
        string provider = "OpenAI";
        string model = "gpt-4o-mini";
        string? thinkingLevel = null;
        List<object> messageHistory = new();
        bool spoilerFreeMode = false;
        int? maxInputTokens = null;
        int? maxOutputTokens = null;"""
)

content = content.replace(
"""                if (!string.IsNullOrEmpty(settings.ThinkingLevel)) thinkingLevel = settings.ThinkingLevel;
                spoilerFreeMode = settings.SpoilerFreeMode;""",
"""                if (!string.IsNullOrEmpty(settings.ThinkingLevel)) thinkingLevel = settings.ThinkingLevel;
                spoilerFreeMode = settings.SpoilerFreeMode;
                maxInputTokens = settings.MaxInputTokens;
                maxOutputTokens = settings.MaxOutputTokens;"""
)

# 3. Add FormatMessage helper and Anthropic combiner helper. Let's put them at the end.
helpers = """
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
"""

content = content.replace("private static string BuildSystemPrompt(", helpers + "\n    private static string BuildSystemPrompt(")

# 4. Message history formatting
orig_history = """                var pastMessages = dbContext.ChatMessage.Where(m => m.ChatSessionId == currentSessionId).OrderBy(m => m.TimestampUtc).ToList();
                foreach (var pm in pastMessages)
                {
                    messageHistory.Add(new { role = pm.Role, content = pm.Content });
                }"""

new_history = """                var pastMessages = dbContext.ChatMessage.Where(m => m.ChatSessionId == currentSessionId).OrderBy(m => m.TimestampUtc).ToList();
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
                }"""
content = content.replace(orig_history, new_history)

# 5. Insert system prompt bug fix and truncation and limits logic
orig_insert = """            string systemPrompt = BuildSystemPrompt(contextDocs, spoilerFreeMode, hasGameSnapshot, hasDebugData, hasDirectoryManifest, hasMessageHistory);

            if (!messageHistory.Any(m => ((dynamic)m).role == "system"))
            {
                messageHistory.Insert(0, new { role = "system", content = systemPrompt });
            }"""
            
new_insert = """            string systemPrompt = BuildSystemPrompt(contextDocs, spoilerFreeMode, hasGameSnapshot, hasDebugData, hasDirectoryManifest, hasMessageHistory);"""
content = content.replace(orig_insert, new_insert)

# 6. Current message attachment handling to use FormatMessage and then Truncation
orig_current_msg = """            if (imageAttachments.Count > 0)
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
            }"""
            
new_current_msg = """            messageHistory.Add(FormatMessage(provider, "user", finalMessageText, imageAttachments));
            
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
"""
content = content.replace(orig_current_msg, new_current_msg)

# 7. Add MaxOutputTokens to payload
orig_openai_req = """            var requestBody = new Dictionary<string, object>
            {
                { "model", model },
                { "messages", messageHistory },
                { "stream", true }
            };"""
new_openai_req = """            var requestBody = new Dictionary<string, object>
            {
                { "model", model },
                { "messages", messageHistory },
                { "stream", true }
            };
            if (maxOutputTokens.HasValue) requestBody["max_tokens"] = maxOutputTokens.Value;
"""
content = content.replace(orig_openai_req, new_openai_req)

orig_anthropic_req = """            var requestBody = new Dictionary<string, object>
            {
                { "model", model },
                { "system", systemPrompt },
                { "messages", userAsstMessages },
                { "stream", true },
                { "max_tokens", 4096 }
            };"""
new_anthropic_req = """            var requestBody = new Dictionary<string, object>
            {
                { "model", model },
                { "system", systemPrompt },
                { "messages", userAsstMessages },
                { "stream", true },
                { "max_tokens", maxOutputTokens ?? 4096 }
            };"""
content = content.replace(orig_anthropic_req, new_anthropic_req)

# For Google, MaxOutputTokens config
orig_google_req = """            var requestBody = new Dictionary<string, object>
            {
                { "contents", contents }
            };

            if (!string.IsNullOrEmpty(systemInstruction))
            {
                requestBody["systemInstruction"] = new
                {
                    parts = new[] { new { text = systemInstruction } }
                };
            }"""
new_google_req = """            var requestBody = new Dictionary<string, object>
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
            }"""
content = content.replace(orig_google_req, new_google_req)


with open(r"c:\hmp\MobileGnollHackLogger\Overseer\Services\ChatService.cs", "w", encoding="utf-8") as f:
    f.write(content)
