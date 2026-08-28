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
using Overseer.Extensions;
using Overseer.Services.Tools;
using Overseer.Services.Providers;
using Overseer.Services.Agents;

namespace Overseer.Services;

public class ChatService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly WikiService _wikiService;
    private readonly CryptoService _cryptoService;
    private readonly IHttpClientFactory _httpClientFactory;
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<long, System.Threading.CancellationTokenSource> _titleCancellationTokens = new();
    private readonly IConfiguration _configuration;
    private readonly IHubContext<ChatHub> _hubContext;
    private readonly ModelMetadataService _modelMetadataService;
    private readonly ToolRegistry _toolRegistry;
    private readonly ToolExecutor _toolExecutor;
    private readonly KnowledgeBaseService _knowledgeBaseService;
    private readonly OngoingChatManager _ongoingChatManager;
    private readonly AgentLoopRunner _agentLoopRunner;
    private readonly Dictionary<string, IAiProvider> _aiProviders;
    private readonly SubAgentCatalogService? _subAgentCatalogService;
    private bool _showDebugLog = false;

    public ChatService(
        IServiceScopeFactory scopeFactory,
        WikiService wikiService,
        CryptoService cryptoService,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        IHubContext<ChatHub> hubContext,
        ModelMetadataService modelMetadataService,
        ToolRegistry toolRegistry,
        ToolExecutor toolExecutor,
        KnowledgeBaseService knowledgeBaseService,
        OngoingChatManager ongoingChatManager,
        AgentLoopRunner agentLoopRunner,
        IEnumerable<IAiProvider> aiProviders,
        SubAgentCatalogService? subAgentCatalogService = null)
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
        _knowledgeBaseService = knowledgeBaseService;
        _ongoingChatManager = ongoingChatManager;
        _agentLoopRunner = agentLoopRunner;
        _aiProviders = aiProviders.ToDictionary(p => p.ProviderName, p => p, StringComparer.OrdinalIgnoreCase);
        _subAgentCatalogService = subAgentCatalogService;
    }

    /// <summary>
    /// Canonical prefix of the system message that carries the uploaded game state snapshot.
    /// Written by SessionController.CreateSession and matched by IsGameSnapshotMessage.
    /// </summary>
    public const string GameSnapshotPrefix = "Game Context Snapshot:";

    /// <summary>
    /// Canonical prefix of the system message that carries the in-game message history.
    /// Written by SessionController.CreateSession and matched by IsMessageHistoryMessage.
    /// </summary>
    public const string MessageHistoryPrefix = "Full Message History";

    public static string SanitizeSnapshotForLlm(string html)
        => DumpHtmlSanitizer.Sanitize(html);

    public static bool IsGameSnapshotMessage(string? content)
    {
        if (string.IsNullOrEmpty(content))
            return false;

        /* "Game Snapshot" is a legacy prefix from older clients and is kept.
           "Game Context Snapshot" (no colon) subsumes GameSnapshotPrefix, so
           testing the constant separately would be dead code. */
        return content.StartsWith("Game Snapshot", StringComparison.OrdinalIgnoreCase)
            || content.StartsWith("Game Context Snapshot", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsMessageHistoryMessage(string? content)
    {
        if (string.IsNullOrEmpty(content))
            return false;

        return content.StartsWith(MessageHistoryPrefix, StringComparison.OrdinalIgnoreCase);
    }

    public async Task GenerateAndBroadcastMessageAsync(long sessionId, string message, List<SendMessageAttachment>? attachments, string userId, bool isHidden, CancellationToken cancellationToken, long? userModelId = null, long? systemModelId = null, bool hasGreeted = false)
    {
        try
        {
            await foreach (var evt in StreamMessageAsync(sessionId, message, attachments, userId, isHidden, cancellationToken, userModelId, systemModelId, hasGreeted))
            {
                evt.SessionId = sessionId;
                _ongoingChatManager.ProcessEvent(sessionId, evt);
                
                if (evt.Type == "user_message_created")
                {
                    await _hubContext.Clients.User(userId).SendAsync("ReceiveChatEvent", evt, CancellationToken.None);
                }
                else
                {
                    await _hubContext.Clients.Group(sessionId.ToString()).SendAsync("ReceiveChatEvent", evt, CancellationToken.None);
                }
            }
        }
        catch (Exception ex)
        {
            var errEvt = new ChatEvent { Type = "error", Data = ex.Message, SessionId = sessionId };
            _ongoingChatManager.ProcessEvent(sessionId, errEvt);
            await _hubContext.Clients.Group(sessionId.ToString()).SendAsync("ReceiveChatEvent", errEvt, CancellationToken.None);
        }
        finally
        {
            var finalState = _ongoingChatManager.TryGet(sessionId);
            if (finalState != null)
            {
                var totalEvents = finalState.AccumulatedEvents.Count;
                var lastSeq = finalState.EventSequence;
                var statsEvt = new ChatEvent { Type = "debug", Data = $"[Backend] Generation complete. Total events={totalEvents}, lastSeqNo={lastSeq}", SessionId = sessionId };
                _ongoingChatManager.ProcessEvent(sessionId, statsEvt);
                await _hubContext.Clients.Group(sessionId.ToString()).SendAsync("ReceiveChatEvent", statsEvt, CancellationToken.None);
            }

            bool isUserCancel = _ongoingChatManager.TryGet(sessionId) == null;
            if (isUserCancel || cancellationToken.IsCancellationRequested)
            {
                var canceledEvt = new ChatEvent { Type = "title_status", Data = "{\"status\":\"canceled\",\"sessionId\":" + sessionId + "}", SessionId = sessionId };
                _ongoingChatManager.ProcessEvent(sessionId, canceledEvt);
                await _hubContext.Clients.Group(sessionId.ToString()).SendAsync("ReceiveChatEvent", canceledEvt, CancellationToken.None);
            }

            var doneEvt = new ChatEvent { Type = "done", Data = "", SessionId = sessionId };
            _ongoingChatManager.ProcessEvent(sessionId, doneEvt);
            await _hubContext.Clients.Group(sessionId.ToString()).SendAsync("ReceiveChatEvent", doneEvt, CancellationToken.None);
        }
    }

    public async IAsyncEnumerable<ChatEvent> StreamMessageAsync(long sessionId, string message, List<SendMessageAttachment>? attachments, string userId, bool isHidden, [EnumeratorCancellation] CancellationToken cancellationToken, long? userModelId = null, long? systemModelId = null, bool hasGreeted = false)
    {
        if (string.IsNullOrEmpty(userId)) yield break;

        // Sanitize: strip dangerous control characters safely handling nulls, then trim
        message = System.Text.RegularExpressions.Regex.Replace(message ?? "", @"[\x00-\x08\x0B\x0C\x0E-\x1F\x7F]", "").Trim();
        if (string.IsNullOrEmpty(message) && (attachments == null || attachments.Count == 0))
            yield break;

        long currentSessionId = 0;
        string? apiKey = null;
        string? provider = null;
        string? model = null;
        string? modelDisplayName = null;
        string? thinkingLevel = null;
        string? reasoningMode = null;
        string? reasoningSummary = null;
        string? serviceTier = null;
        IAiProvider? aiProvider = null;
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
        bool showSourceCodeReferences = false;
        int? userMaxResultLength = null;
        int? userMaxCallsPerSession = null;
        int? userMaxToolIterations = null;
        int? userMaxParallelToolCalls = null;
        string systemPrompt = "";

        int estimatedInputTokens = 0;

        using (var scope = _scopeFactory.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var userName = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.FirstOrDefaultAsync(System.Linq.Queryable.Select(System.Linq.Queryable.Where(dbContext.Users, u => u.Id == userId), u => u.UserName), cancellationToken);
            _showDebugLog = _configuration.ShouldShowDebugLog(userName);
            var settings = await dbContext.UserAiSettings.FindAsync(userId);
            if (settings != null)
            {
                spoilerFreeMode = settings.SpoilerFreeMode;
                enableWebSearch = settings.EnableWebSearch;
                enableToolUse = settings.EnableToolUse;
                enableClientTools = settings.EnableClientTools;
                enableGameActions = settings.EnableGameActions;
                showSourceCodeReferences = settings.ShowSourceCodeReferences;
                userMaxResultLength = settings.MaxResultLength;
                userMaxCallsPerSession = settings.MaxCallsPerSession;
                userMaxToolIterations = settings.MaxToolIterations;
                userMaxParallelToolCalls = settings.MaxParallelToolCalls;
            }

            var tempSession = await dbContext.ChatSession.FindAsync(sessionId);
            if (tempSession != null && tempSession.IsGnollHackSession)
            {
                userModelId = null; // force first model because GnollHack doesn't support model selection
            }

            if (systemModelId.HasValue)
            {
                var systemAiConfigService = scope.ServiceProvider.GetRequiredService<SystemAiConfigService>();
                var (config, errorMessage) = await systemAiConfigService.GetAndCheckSystemConfigAsync(systemModelId.Value, userId, requiredRoleFilter: 1);
                
                if (config == null)
                {
                    yield return new ChatEvent { Type = "error", Data = $"Error: {errorMessage}" };
                    yield break;
                }

                userModelId = null;
                provider = config.Provider;
                model = config.ModelId;
                modelDisplayName = !string.IsNullOrEmpty(config.DisplayName) ? config.DisplayName : config.ModelId;
                thinkingLevel = config.ThinkingLevel;
                reasoningMode = config.ReasoningMode;
                reasoningSummary = config.ReasoningSummary;
                serviceTier = config.ServiceTier;
                if (config.MaxInputTokens.HasValue) maxInputTokens = config.MaxInputTokens.Value;
                if (config.MaxOutputTokens.HasValue) maxOutputTokens = config.MaxOutputTokens.Value;
                
                if (!string.IsNullOrEmpty(config.EncryptedApiKey) && !string.IsNullOrEmpty(config.ApiKeyNonce) && !string.IsNullOrEmpty(config.ApiKeyTag))
                {
                    apiKey = _cryptoService.Decrypt(config.EncryptedApiKey, config.ApiKeyNonce, config.ApiKeyTag, "SYSTEM_API_KEY");
                }
            }
            else if (userModelId.HasValue)
            {
                var userModel = await dbContext.UserAiModels.FirstOrDefaultAsync(m => m.Id == userModelId.Value && m.AspNetUserId == userId);
                if (userModel != null)
                {
                    provider = userModel.Provider;
                    model = userModel.ModelId;
                    modelDisplayName = !string.IsNullOrEmpty(userModel.DisplayName) ? userModel.DisplayName : userModel.ModelId;
                    thinkingLevel = userModel.ThinkingLevel;
                    reasoningMode = userModel.ReasoningMode;
                    reasoningSummary = userModel.ReasoningSummary;
                    serviceTier = userModel.ServiceTier;
                    if (userModel.MaxInputTokens.HasValue) maxInputTokens = userModel.MaxInputTokens.Value;
                    if (userModel.MaxOutputTokens.HasValue) maxOutputTokens = userModel.MaxOutputTokens.Value;
                }
                else
                {
                    userModelId = null;
                }
            }
            if (string.IsNullOrEmpty(provider) || string.IsNullOrEmpty(model))
            {
                var defaultModel = await dbContext.UserAiModels.OrderBy(m => m.OrderIndex).FirstOrDefaultAsync(m => m.AspNetUserId == userId);
                if (defaultModel != null)
                {
                    userModelId = defaultModel.Id;
                    provider = defaultModel.Provider;
                    model = defaultModel.ModelId;
                    modelDisplayName = !string.IsNullOrEmpty(defaultModel.DisplayName) ? defaultModel.DisplayName : defaultModel.ModelId;
                    thinkingLevel = defaultModel.ThinkingLevel;
                    reasoningMode = defaultModel.ReasoningMode;
                    reasoningSummary = defaultModel.ReasoningSummary;
                    serviceTier = defaultModel.ServiceTier;
                    if (defaultModel.MaxInputTokens.HasValue) maxInputTokens = defaultModel.MaxInputTokens.Value;
                    if (defaultModel.MaxOutputTokens.HasValue) maxOutputTokens = defaultModel.MaxOutputTokens.Value;
                }
                else
                {
                    var settingsService = scope.ServiceProvider.GetRequiredService<SettingsService>();
                    var sysModels = await settingsService.GetResolvedSystemModelsAsync(userId, 1);
                    var firstSys = sysModels.FirstOrDefault();
                    if (firstSys.Config != null)
                    {
                        var config = firstSys.Config;
                        provider = config.Provider;
                        model = config.ModelId;
                        modelDisplayName = !string.IsNullOrEmpty(config.DisplayName) ? config.DisplayName : config.ModelId;
                        thinkingLevel = config.ThinkingLevel;
                        reasoningMode = config.ReasoningMode;
                        reasoningSummary = config.ReasoningSummary;
                        serviceTier = config.ServiceTier;
                        if (config.MaxInputTokens.HasValue) maxInputTokens = config.MaxInputTokens.Value;
                        if (config.MaxOutputTokens.HasValue) maxOutputTokens = config.MaxOutputTokens.Value;
                        if (!string.IsNullOrEmpty(config.EncryptedApiKey) && !string.IsNullOrEmpty(config.ApiKeyNonce) && !string.IsNullOrEmpty(config.ApiKeyTag))
                        {
                            apiKey = _cryptoService.Decrypt(config.EncryptedApiKey, config.ApiKeyNonce, config.ApiKeyTag, "SYSTEM_API_KEY");
                        }
                        systemModelId = config.Id;
                    }
                }
            }

            if (string.IsNullOrEmpty(provider) || string.IsNullOrEmpty(model))
            {
                yield return new ChatEvent { Type = "error", Data = "Error: No AI provider configured. Please select a model." };
                yield break;
            }

            if (!systemModelId.HasValue)
            {
                var providerKey = await dbContext.UserAiApiKeys.FirstOrDefaultAsync(k => k.AspNetUserId == userId && k.Provider == provider);
                if (providerKey != null && !string.IsNullOrEmpty(providerKey.EncryptedApiKey) && !string.IsNullOrEmpty(providerKey.ApiKeyNonce) && !string.IsNullOrEmpty(providerKey.ApiKeyTag))
                {
                    apiKey = _cryptoService.Decrypt(providerKey.EncryptedApiKey, providerKey.ApiKeyNonce, providerKey.ApiKeyTag, userId);
                }
            }

            if (string.IsNullOrEmpty(apiKey))
            {
                yield return new ChatEvent { Type = "error", Data = $"Error: API Key not configured for {provider}. Please configure it in the API Keys page or select a system-provided model." };
                yield break;
            }

            if (!_aiProviders.TryGetValue(provider, out aiProvider) || aiProvider == null)
            {
                yield return new ChatEvent { Type = "error", Data = $"Error: AI Provider '{provider}' not supported." };
                yield break;
            }

            bool hasGameSnapshot = false;
            bool hasMessageHistory = false;

            ChatSession? session = null;
            bool shouldGenerateTitle = false;

            session = await dbContext.ChatSession.FindAsync(sessionId);
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
                        messageHistory.Add(aiProvider.FormatMessage(pm.Role ?? "", pm.Content ?? "", msgImageAttachments));
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
                    if (IsGameSnapshotMessage(pm.Content)) hasGameSnapshot = true;
                    if (IsMessageHistoryMessage(pm.Content)) hasMessageHistory = true;
                }
            }

            if (!isHidden && !pastMessages.Any(m => m.Role == "user" && !m.IsHidden))
            {
                shouldGenerateTitle = true;
            }

            if (shouldGenerateTitle)
            {
                _ = Task.Run(async () =>
                {
                    await GenerateTitleAsync(currentSessionId, message, userId);
                });
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
            
            bool allowSourceCodeReferences = showSourceCodeReferences || developerMode;

            bool enableSubAgents = SubAgentAvailability.IsAvailableFor(
                _modelMetadataService,
                provider ?? "",
                model,
                _subAgentCatalogService ?? new SubAgentCatalogService(_configuration, Microsoft.Extensions.Logging.Abstractions.NullLogger<SubAgentCatalogService>.Instance),
                new ToolExecutionContext { AgentDepth = 0, MaxAgentDepth = 1 },
                enableToolUse,
                _configuration);

            // enableToolUse handled above
            systemPrompt = BuildSystemPrompt(contextDocs, spoilerFreeMode, verboseMode, isGameOn, developerMode, overseerMode, hasGameSnapshot, hasMessageHistory, session?.ClientSettings, enableToolUse, enableWebSearch, allowSourceCodeReferences, enableSubAgents);

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
            await dbContext.SaveChangesAsync(CancellationToken.None);
            
            var textAttachmentsContent = new StringBuilder();
            List<SendMessageAttachment> imageAttachments = new();
            var savedDbAttachments = new List<ChatMessageAttachment>();

            if (attachments != null && attachments.Count > 0)
            {
                baseDir = _configuration["ConversationsDataLocation"];
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
                            savedDbAttachments.Add(dbAtt);

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
                    await dbContext.SaveChangesAsync(CancellationToken.None);
                }
            }

            yield return new ChatEvent
            {
                Type = "debug",
                Data = $"[Backend] Emitting user_message_created for userMsg.Id={userMsg.Id}, attachmentsCount={savedDbAttachments.Count}, fileNames=[{string.Join(", ", savedDbAttachments.Select(a => a.FileName))}]"
            };

            yield return new ChatEvent
            {
                Type = "user_message_created",
                Data = JsonSerializer.Serialize(new
                {
                    messageId = userMsg.Id,
                    attachments = savedDbAttachments.Select(a => new { id = a.Id, fileName = a.FileName }).ToList()
                })
            };

            string finalMessageText = message;
            if (textAttachmentsContent.Length > 0)
            {
                finalMessageText += "\n\n" + textAttachmentsContent.ToString();
            }

            if (!hasGreeted)
            {
                if (!isGnollHackSession)
                {
                    finalMessageText += "\n\n[System instruction: Greet me.]";
                }
            }
            else
            {
                finalMessageText += "\n\n[System instruction: Do not greet me, unless I greet you first.]";
            }

            messageHistory.Add(aiProvider.FormatMessage("user", finalMessageText, imageAttachments));
            
            // Truncation logic
            var meta = _modelMetadataService.GetMetadata(provider ?? "", model);
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
            
            // Prepare history for provider (e.g. Anthropic alternating / Google parts)
            messageHistory = aiProvider.PrepareMessageHistory(messageHistory);
            
            // Note: maxOutputTokens is handled later

            estimatedInputTokens = totalTokens;

            if (_showDebugLog) yield return new ChatEvent 
            { 
                Type = "debug", 
                Data = $"[Model Configuration]\nProvider: {provider}\nModel: {model}\nDisplay Name: {(string.IsNullOrEmpty(modelDisplayName) ? "None" : modelDisplayName)}\nThinking Level: {(string.IsNullOrEmpty(thinkingLevel) ? "None" : thinkingLevel)}\nService Tier: {(string.IsNullOrEmpty(serviceTier) ? "None" : serviceTier)}\nMax Input Tokens (Limit): {effectiveInputLimit}\nMax Output Tokens: {(maxOutputTokens.HasValue ? maxOutputTokens.Value.ToString() : "Default")}\nEstimated Request Input Tokens: ~{totalTokens}" 
            };
        }

        var runBudget = new AgentRunBudget
        {
            MaxTotalModelCalls = _configuration.GetValue<int>("SubAgentSettings:MaxTotalModelCalls", 48),
            MaxSubAgentRuns = _configuration.GetValue<int>("SubAgentSettings:MaxSubAgentRuns", 6),
            MaxParallelSubAgents = _configuration.GetValue<int>("SubAgentSettings:MaxParallelSubAgents", 3)
        };

        var execContext = new ToolExecutionContext { 
            SessionId = currentSessionId, 
            UserId = userId,
            IsGameOn = isGameOn, 
            SpoilerFreeMode = spoilerFreeMode, 
            OverseerMode = overseerMode, 
            IsGnollHackSession = isGnollHackSession,
            MaxResultLength = userMaxResultLength ?? _configuration.GetValue<int>("AiPerformanceSettings:MaxResultLength:Default", 3000),
            MaxCallsPerSession = userMaxCallsPerSession ?? _configuration.GetValue<int>("AiPerformanceSettings:MaxCallsPerSession:Default", 30),
            ShowDebugLog = _showDebugLog,
            Budget = runBudget,
            ActiveUserModelId = userModelId,
            ActiveSystemModelId = systemModelId,
            EventSink = async (evt) => {
                evt.SessionId = currentSessionId;
                _ongoingChatManager.ProcessEvent(currentSessionId, evt);
                await _hubContext.Clients.Group(currentSessionId.ToString()).SendAsync("ReceiveChatEvent", evt, CancellationToken.None);
            }
        };
        int maxToolIterations = userMaxToolIterations ?? _configuration.GetValue<int>("AiPerformanceSettings:MaxToolIterations:Default", 32);

        int cfgParallelMin = _configuration.GetValue<int>("AiPerformanceSettings:MaxParallelToolCalls:Min", 1);
        int cfgParallelMax = _configuration.GetValue<int>("AiPerformanceSettings:MaxParallelToolCalls:Max", 10);
        int cfgParallelDefault = _configuration.GetValue<int>("AiPerformanceSettings:MaxParallelToolCalls:Default", 6);

        int lowerParallel = Math.Max(1, cfgParallelMin);
        int upperParallel = Math.Max(lowerParallel, cfgParallelMax);
        int maxParallelTools = Math.Clamp(userMaxParallelToolCalls ?? cfgParallelDefault, lowerParallel, upperParallel);

        int cfgClientMin = _configuration.GetValue<int>("AiPerformanceSettings:MaxParallelClientToolCalls:Min", 1);
        int cfgClientMax = _configuration.GetValue<int>("AiPerformanceSettings:MaxParallelClientToolCalls:Max", 4);
        int cfgClientDefault = _configuration.GetValue<int>("AiPerformanceSettings:MaxParallelClientToolCalls:Default", 1);
        int maxParallelClientTools = Math.Clamp(
            Math.Clamp(cfgClientDefault, Math.Max(1, cfgClientMin), Math.Max(1, cfgClientMax)),
            1, maxParallelTools);

        var runRequest = new AgentRunRequest
        {
            ProviderName = provider ?? "",
            ModelId = model ?? "",
            ApiKey = apiKey,
            ModelDisplayName = modelDisplayName,
            SystemPrompt = systemPrompt,
            SeedHistory = messageHistory,
            ThinkingLevel = thinkingLevel,
            ReasoningMode = reasoningMode,
            ReasoningSummary = reasoningSummary,
            ServiceTier = serviceTier,
            MaxOutputTokens = maxOutputTokens,
            MaxToolIterations = maxToolIterations,
            MaxParallelTools = maxParallelTools,
            MaxParallelClientTools = maxParallelClientTools,
            EnableWebSearch = enableWebSearch,
            EnableToolUse = enableToolUse,
            EnableClientTools = enableClientTools,
            EnableGameActions = enableGameActions,
            ToolExecutionContext = execContext,
            SystemModelId = systemModelId,
            ShowDebugLog = _showDebugLog,
            AiProvider = aiProvider,
            Budget = runBudget
        };

        var runResult = new AgentRunResult();

        await foreach (var evt in _agentLoopRunner.RunAsync(runRequest, runBudget, runResult, cancellationToken))
        {
            yield return evt;
        }

        string fullResponse = runResult.FinalText ?? "";
        int? timeToFirstTokenMs = runResult.TimeToFirstTokenMs;
        int? totalDurationMs = runResult.TotalDurationMs;
        var streamToolCalls = runResult.ToolCalls;

        if (cancellationToken.IsCancellationRequested)
        {
            bool isUserCancel = _ongoingChatManager.TryGet(currentSessionId) == null;
            if (!isUserCancel)
            {
                string errMsg = "The request timed out. The AI provider may be overloaded. Please try again.";
                fullResponse += $"\n\n**Error:** {errMsg}";
                yield return new ChatEvent { Type = "error", Data = errMsg };
            }
        }

        using (var scope = _scopeFactory.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var session = await dbContext.ChatSession.FindAsync(currentSessionId);
            if (session != null)
            {
                bool hasThinkingDivs = fullResponse.Contains("ai-thought");
                if (_showDebugLog) yield return new ChatEvent { Type = "debug", Data = $"[Main Chat] Saving assistant message: {fullResponse.Length} chars, hasThinkingDivs={hasThinkingDivs}, thinkingDivsCount={runResult.EmittedDivCount}, toolCalls={streamToolCalls.Count}" };

                var asstMsg = new ChatMessage
                {
                    ChatSessionId = currentSessionId,
                    Role = "assistant",
                    Content = fullResponse,
                    TimestampUtc = DateTime.UtcNow,
                    ProviderUsed = provider,
                    ModelUsed = model,
                    ThinkingLevelUsed = thinkingLevel,
                    ReasoningModeUsed = reasoningMode,
                    ServiceTierUsed = serviceTier,
                    ModelDisplayNameUsed = modelDisplayName,
                    ToolCalls = streamToolCalls,
                    TimeToFirstTokenMs = timeToFirstTokenMs,
                    TotalDurationMs = totalDurationMs
                };
                dbContext.ChatMessage.Add(asstMsg);
                session.LastMessageUtc = DateTime.UtcNow;

                if (systemModelId.HasValue)
                {
                    var systemAiConfigService = scope.ServiceProvider.GetRequiredService<SystemAiConfigService>();
                    int outputTokens = fullResponse.Length / 4;
                    await systemAiConfigService.RecordUsageAsync(systemModelId.Value, userId, estimatedInputTokens, outputTokens, roleContext: 1);
                }

                await dbContext.SaveChangesAsync(CancellationToken.None);

                var ongoingState = _ongoingChatManager.TryGet(currentSessionId);
                if (ongoingState != null)
                {
                    ongoingState.SavedMessageId = asstMsg.Id;
                }

                if (_showDebugLog) yield return new ChatEvent { Type = "debug", Data = $"[Main Chat] Assistant message saved to DB (id={asstMsg.Id})" };
            }
        }
    }

    internal string BuildSystemPrompt(
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
        bool enableWebSearch,
        bool allowSourceCodeReferences,
        bool enableSubAgents = false)
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
        // SECTION 5b: Knowledge Base
        // ──────────────────────────────────────────────
        if (enableToolUse)
        {
            var topicList = _knowledgeBaseService.GetTopicList();
            if (!string.IsNullOrEmpty(topicList))
            {
                sb.AppendLine("## Knowledge Base");
                sb.AppendLine("You have curated reference articles available via the get_knowledge_article tool on the following topics:");
                sb.AppendLine(topicList);
                sb.AppendLine();
                sb.AppendLine("### Information Routing");
                sb.AppendLine("- For topics listed above: ALWAYS call get_knowledge_article FIRST. The knowledge base is the authoritative source for these topics. Only use wiki or other tools if the article does not fully answer the question.");
                sb.AppendLine("- For game mechanics, monsters, items, spells, or other topics NOT listed above: skip the knowledge base entirely and go directly to wiki_search, monster_lookup, or item_lookup.");
                sb.AppendLine();
            }
        }

        // ──────────────────────────────────────────────
        // SECTION 6: Available Context
        // ──────────────────────────────────────────────
        sb.AppendLine("## Available Context in This Session");
        if (hasGameSnapshot) sb.AppendLine("- ✅ Game snapshot (current map, stats, inventory, recent messages, spells, skills, attributes, and the player's Discoveries list — which object types they have identified this game)");
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
            sb.AppendLine("You have access to both the GnollHack and NetHack 5.0 C source code via the source_code_search, source_code_view, list_indexed_files, get_constants, search_definitions, and get_function_definition tools. Use the `repository` parameter set to \"nethack\" to search NetHack source code. This is useful when comparing mechanics between the two games.");
            sb.AppendLine("IMPORTANT: Source code searches are expensive — they typically require multiple follow-up calls and produce large outputs. Always try wiki_search, monster_lookup, or item_lookup first. Only use source code tools when:");
            sb.AppendLine("- The wiki/lookup tools do not have the information or the answer is ambiguous");
            sb.AppendLine("- The user asks about exact formulas, probabilities, or undocumented mechanics");
            sb.AppendLine("- You are actively investigating a bug or the user explicitly requests source code verification");
            sb.AppendLine("When the wiki or lookup tools give a clear answer, trust it without code verification.");
            if (allowSourceCodeReferences)
            {
                sb.AppendLine("When citing source code findings, mention the file and line number, and translate the C code into player-friendly language.");
            }
            else
            {
                sb.AppendLine("IMPORTANT: Do NOT include raw source code file names, paths, or line numbers in your responses. Instead, describe the underlying mechanics in plain, player-friendly language without referencing the code itself. Use the source code tools internally to find accurate information, but present only the gameplay-relevant conclusions.");
            }
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

        sb.Append(BuildRandomizedAppearancePolicy());

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
        sb.AppendLine("- The GnollHack source code is the ultimate authority if the wiki and source code disagree on exact formulas or probabilities. However, for general game information (stats, properties, descriptions), the wiki is authoritative and does not require source code verification.");
        sb.AppendLine("- For inherited NetHack mechanics not yet documented on the GnollHack Wiki, the NetHack Wiki tools (nethack_wiki_search, nethack_wiki_view) or the source code tools with `repository: \"nethack\"` can be used to check NetHack's implementation directly. Always caveat that mechanics may differ between GnollHack and NetHack.");

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
        // SECTION 15: Tool Use Policy & Subagents
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

        if (enableSubAgents)
        {
            var catalogService = _subAgentCatalogService ?? new SubAgentCatalogService(_configuration, Microsoft.Extensions.Logging.Abstractions.NullLogger<SubAgentCatalogService>.Instance);
            var enabledAgents = catalogService.GetEnabledSubAgents();
            if (enabledAgents.Count > 0)
            {
                sb.AppendLine("## Subagent Delegation (Multi-Agent Support)");
                sb.AppendLine("You have access to specialized autonomous subagents via the `delegate_to_subagent` tool:");
                foreach (var agent in enabledAgents)
                {
                    sb.AppendLine($"- **{agent.Name}** ({agent.DisplayName}): {agent.Description}");
                }
                sb.AppendLine();
                sb.AppendLine("When a complex inquiry requires multi-turn investigation or specialized deep dive, delegate the sub-task to the appropriate subagent. Subagents execute autonomously and return synthesized findings.");
                sb.AppendLine("Do NOT use delegation for trivial single-turn lookups.");
                sb.AppendLine();
            }
        }

        return sb.ToString();
    }

    public static void CancelTitleGeneration(long sessionId)
    {
        if (_titleCancellationTokens.TryRemove(sessionId, out var cts))
        {
            cts.Cancel();
            cts.Dispose();
        }
    }

    internal async Task GenerateTitleAsync(long sessionId, string userMessage, string userId)
    {
        CancelTitleGeneration(sessionId);
        
        int titleTimeout = _configuration.GetValue<int>("AITitleGenerationTimeout", 120);
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(titleTimeout));
        _titleCancellationTokens[sessionId] = cts;
        var cancellationToken = cts.Token;

        try
        {
            using var startScope = _scopeFactory.CreateScope();
            var startDbContext = startScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var settingsCheck = await startDbContext.UserAiSettings.FindAsync(new object[] { userId }, cancellationToken);
            if (settingsCheck?.TitleGenerationDisabled == true)
            {
                var titleUserNameCheck = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.FirstOrDefaultAsync(System.Linq.Queryable.Select(System.Linq.Queryable.Where(startDbContext.Users, u => u.Id == userId), u => u.UserName), cancellationToken);
                if (_configuration.ShouldShowDebugLog(titleUserNameCheck))
                {
                    await _hubContext.Clients.Group(sessionId.ToString()).SendAsync("ReceiveChatEvent", new ChatEvent { Type = "debug", Data = $"[Title Gen] Skipped: Disabled by user." }, CancellationToken.None);
                }
                return;
            }

            var titleUserName = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.FirstOrDefaultAsync(System.Linq.Queryable.Select(System.Linq.Queryable.Where(startDbContext.Users, u => u.Id == userId), u => u.UserName), cancellationToken);
            _showDebugLog = _configuration.ShouldShowDebugLog(titleUserName);

            await Task.Delay(2500, cancellationToken); // Give the UI time to navigate and join the SignalR session
            
            if (_showDebugLog) await _hubContext.Clients.Group(sessionId.ToString()).SendAsync("ReceiveChatEvent", new ChatEvent { Type = "debug", Data = $"[Title Gen] Task started for session {sessionId}." }, CancellationToken.None);
            
            var initialStatus = new { sessionId = sessionId, status = "Generating AI title..." };
            await _hubContext.Clients.User(userId).SendAsync("ReceiveChatEvent", new ChatEvent { Type = "title_status", Data = System.Text.Json.JsonSerializer.Serialize(initialStatus) }, CancellationToken.None);
            
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var cryptoService = scope.ServiceProvider.GetRequiredService<CryptoService>();
            
            var settings = await dbContext.UserAiSettings.FindAsync(new object[] { userId }, cancellationToken);
            UserAiModel? model = null;
            string provider = "";
            string modelId = "";
            string apiKey = "";
            string? serviceTier = null;

            long? usedSystemModelId = settings?.TitleGenerationSystemModelId;

            if (settings?.TitleGenerationSystemModelId != null)
            {
                var systemAiConfigService = scope.ServiceProvider.GetRequiredService<SystemAiConfigService>();
                var (config, errorMessage) = await systemAiConfigService.GetAndCheckSystemConfigAsync(settings.TitleGenerationSystemModelId.Value, userId, requiredRoleFilter: 2);
                
                if (config != null && !string.IsNullOrEmpty(config.EncryptedApiKey) && !string.IsNullOrEmpty(config.ApiKeyNonce) && !string.IsNullOrEmpty(config.ApiKeyTag))
                {
                    provider = config.Provider;
                    modelId = config.ModelId;
                    serviceTier = config.ServiceTier;
                    apiKey = cryptoService.Decrypt(config.EncryptedApiKey, config.ApiKeyNonce, config.ApiKeyTag, "SYSTEM_API_KEY");
                }
                else if (config == null)
                {
                    if (_showDebugLog) await _hubContext.Clients.Group(sessionId.ToString()).SendAsync("ReceiveChatEvent", new ChatEvent { Type = "debug", Data = $"[Title Gen] Aborted: {errorMessage}" }, CancellationToken.None);
                    return;
                }
            }
            else
            {
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

                if (model != null)
                {
                    provider = model.Provider;
                    modelId = model.ModelId;
                    serviceTier = model.ServiceTier;

                    var apiKeyEntry = await dbContext.UserAiApiKeys.FirstOrDefaultAsync(k => k.AspNetUserId == userId && k.Provider == provider, cancellationToken);
                    if (apiKeyEntry != null && !string.IsNullOrEmpty(apiKeyEntry.EncryptedApiKey) && !string.IsNullOrEmpty(apiKeyEntry.ApiKeyNonce) && !string.IsNullOrEmpty(apiKeyEntry.ApiKeyTag))
                    {
                        apiKey = cryptoService.Decrypt(apiKeyEntry.EncryptedApiKey, apiKeyEntry.ApiKeyNonce, apiKeyEntry.ApiKeyTag, userId);
                    }
                }

                if (string.IsNullOrEmpty(provider) || string.IsNullOrEmpty(apiKey))
                {
                    var settingsService = scope.ServiceProvider.GetRequiredService<SettingsService>();
                    var resolvedSystemModels = await settingsService.GetResolvedSystemModelsAsync(userId, roleFilter: 2);
                    if (!resolvedSystemModels.Any())
                    {
                        resolvedSystemModels = await settingsService.GetResolvedSystemModelsAsync(userId, roleFilter: 1);
                    }
                    var firstSystemModel = resolvedSystemModels.FirstOrDefault();
                    if (firstSystemModel.Config != null && !string.IsNullOrEmpty(firstSystemModel.Config.EncryptedApiKey) && !string.IsNullOrEmpty(firstSystemModel.Config.ApiKeyNonce) && !string.IsNullOrEmpty(firstSystemModel.Config.ApiKeyTag))
                    {
                        provider = firstSystemModel.Config.Provider;
                        modelId = firstSystemModel.Config.ModelId;
                        serviceTier = firstSystemModel.Config.ServiceTier;
                        apiKey = cryptoService.Decrypt(firstSystemModel.Config.EncryptedApiKey, firstSystemModel.Config.ApiKeyNonce, firstSystemModel.Config.ApiKeyTag, "SYSTEM_API_KEY");
                        usedSystemModelId = firstSystemModel.Config.Id;
                    }
                }
            }

            if (string.IsNullOrEmpty(provider) || string.IsNullOrEmpty(apiKey))
            {
                if (_showDebugLog) await _hubContext.Clients.Group(sessionId.ToString()).SendAsync("ReceiveChatEvent", new ChatEvent { Type = "debug", Data = $"[Title Gen] Aborted: No valid model or API key found." }, CancellationToken.None);
                return;
            }

            if (!_aiProviders.TryGetValue(provider, out var aiProvider))
            {
                if (_showDebugLog) await _hubContext.Clients.Group(sessionId.ToString()).SendAsync("ReceiveChatEvent", new ChatEvent { Type = "debug", Data = $"[Title Gen] Aborted: Provider {provider} not supported." }, CancellationToken.None);
                return;
            }
            
            string prompt = "Generate a descriptive 3 to 8 word title for this conversation based on the user's message. Output ONLY the title itself in Title Case, with no quotes, and no extra text. Use correct punctuation (such as commas for lists or hyphens), but do NOT end the title with a period. Do NOT use single-word abbreviations.";
            string generatedTitle = string.Empty;
            int maxTokens = _configuration.GetValue<int>("AITitleGenerationMaxTokens", 2048);
            
            var client = _httpClientFactory.CreateClient();
            
            var titleReqBody = aiProvider.BuildTitleRequestBody(modelId, prompt, userMessage, maxTokens, serviceTier);
            string reqBodyStr = System.Text.Json.JsonSerializer.Serialize(titleReqBody);
            string titleUrl = aiProvider.GetTitleUrl(modelId, apiKey);

            var sw = System.Diagnostics.Stopwatch.StartNew();
            var safeUri = titleUrl;
            if (safeUri.Contains("key=")) { safeUri = System.Text.RegularExpressions.Regex.Replace(safeUri, "key=[^&]+", "key=APIKEY_NOT_DISCLOSED"); }
            if (_showDebugLog) await _hubContext.Clients.Group(sessionId.ToString()).SendAsync("ReceiveChatEvent", new ChatEvent { Type = "debug", Data = $"[Title Gen - {provider}] Starting POST request to {safeUri}..." }, CancellationToken.None);
            
            HttpResponseMessage? response = null;
            int[] retryDelays = { 1000, 3000, 5000, 10000, 15000 };
            
            for (int i = 0; i <= retryDelays.Length; i++)
            {
                try
                {
                    var reqClone = new HttpRequestMessage(HttpMethod.Post, titleUrl)
                    {
                        Content = new StringContent(reqBodyStr, Encoding.UTF8, "application/json")
                    };
                    aiProvider.ConfigureRequest(reqClone, apiKey);
                    
                    cancellationToken.ThrowIfCancellationRequested();
                    
                    if (i > 0)
                    {
                        var retryStatus = new { sessionId = sessionId, status = $"Retrying title generation ({i}/{retryDelays.Length})..." };
                        await _hubContext.Clients.User(userId).SendAsync("ReceiveChatEvent", new ChatEvent { Type = "title_status", Data = System.Text.Json.JsonSerializer.Serialize(retryStatus) }, CancellationToken.None);
                    }
                    
                    response = await client.SendAsync(reqClone, cancellationToken);
                    
                    if (_showDebugLog) await _hubContext.Clients.Group(sessionId.ToString()).SendAsync("ReceiveChatEvent", new ChatEvent { Type = "debug", Data = $"[Title Gen - {provider}] HTTP {(int)response.StatusCode} Received ({sw.ElapsedMilliseconds}ms, Attempt {i + 1})" }, CancellationToken.None);
                    
                    if (response.StatusCode != System.Net.HttpStatusCode.ServiceUnavailable || i == retryDelays.Length)
                    {
                        break;
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    if (_showDebugLog) await _hubContext.Clients.Group(sessionId.ToString()).SendAsync("ReceiveChatEvent", new ChatEvent { Type = "debug", Data = $"[Title Gen - {provider}] Request failed: {ex.Message} (Attempt {i + 1})" }, CancellationToken.None);
                    if (i == retryDelays.Length) throw;
                }
                
                int delayMs = retryDelays[i];
                if (_showDebugLog) await _hubContext.Clients.Group(sessionId.ToString()).SendAsync("ReceiveChatEvent", new ChatEvent { Type = "debug", Data = $"[Title Gen - {provider}] Sleeping for {delayMs / 1000.0}s before retry..." }, CancellationToken.None);
                await Task.Delay(delayMs, cancellationToken);
            }
            
            if (response != null && response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync(cancellationToken);
                var root = System.Text.Json.JsonDocument.Parse(json).RootElement;
                var parsedTitle = aiProvider.ParseTitleResponse(root);
                if (!string.IsNullOrEmpty(parsedTitle))
                {
                    generatedTitle = parsedTitle.Trim('"', '\'', ' ', '.', '\n', '\r', '\t');
                }
                
                if (string.IsNullOrWhiteSpace(generatedTitle))
                {
                    if (_showDebugLog) await _hubContext.Clients.Group(sessionId.ToString()).SendAsync("ReceiveChatEvent", new ChatEvent { Type = "debug", Data = $"[Title Gen - {provider}] Warning: API returned empty or whitespace title. Raw JSON: {json}" }, CancellationToken.None);
                }
                else
                {
                    if (_showDebugLog) await _hubContext.Clients.Group(sessionId.ToString()).SendAsync("ReceiveChatEvent", new ChatEvent { Type = "debug", Data = $"[Title Gen - {provider}] Generated Title: \"{generatedTitle}\"" }, CancellationToken.None);
                    
                    if (usedSystemModelId != null)
                    {
                        var systemAiConfigService = startScope.ServiceProvider.GetRequiredService<SystemAiConfigService>();
                        int estimatedInputTokens = (prompt.Length + userMessage.Length) / 4;
                        int outputTokens = generatedTitle.Length / 4;
                        await systemAiConfigService.RecordUsageAsync(usedSystemModelId.Value, userId, estimatedInputTokens, outputTokens, roleContext: 2);
                    }
                }
            }
            else if (response != null)
            {
                var errorStr = await response.Content.ReadAsStringAsync(CancellationToken.None);
                if (_showDebugLog) await _hubContext.Clients.Group(sessionId.ToString()).SendAsync("ReceiveChatEvent", new ChatEvent { Type = "debug", Data = $"[Title Gen - {provider}] API Error: {errorStr}" }, CancellationToken.None);
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
                    await _hubContext.Clients.User(userId).SendAsync("ReceiveChatEvent", evt, CancellationToken.None);
                }
            }
            else
            {
                // Fallback: use truncated user message so the title doesn't stay as "GnollHack..."
                var fallbackTitle = userMessage.Length > 50 ? userMessage.Substring(0, 47) + "..." : userMessage;
                if (!string.IsNullOrWhiteSpace(fallbackTitle))
                {
                    var session = await dbContext.ChatSession.FindAsync(new object[] { sessionId }, cancellationToken);
                    if (session != null && session.Title?.StartsWith("GnollHack", StringComparison.OrdinalIgnoreCase) == true)
                    {
                        session.Title = fallbackTitle;
                        await dbContext.SaveChangesAsync(cancellationToken);
                        
                        var titleUpdateData = new { sessionId = sessionId, title = fallbackTitle };
                        var evt = new ChatEvent { Type = "title_update", Data = System.Text.Json.JsonSerializer.Serialize(titleUpdateData) };
                        await _hubContext.Clients.User(userId).SendAsync("ReceiveChatEvent", evt, CancellationToken.None);
                    }
                }
            }
            
            var successStatus = new { sessionId = sessionId, status = "" };
            await _hubContext.Clients.User(userId).SendAsync("ReceiveChatEvent", new ChatEvent { Type = "title_status", Data = System.Text.Json.JsonSerializer.Serialize(successStatus) }, CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
            var cancelStatus = new { sessionId = sessionId, status = "canceled" };
            await _hubContext.Clients.User(userId).SendAsync("ReceiveChatEvent", new ChatEvent { Type = "title_status", Data = System.Text.Json.JsonSerializer.Serialize(cancelStatus) }, CancellationToken.None);
        }
        catch (Exception ex)
        {
            if (_showDebugLog) await _hubContext.Clients.Group(sessionId.ToString()).SendAsync("ReceiveChatEvent", new ChatEvent { Type = "debug", Data = $"[Title Gen - Exception] {ex.GetType().Name}: {ex.Message}" }, CancellationToken.None);
            
            // Fallback title on exception
            try
            {
                using var fallbackScope = _scopeFactory.CreateScope();
                var fallbackDb = fallbackScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var session = await fallbackDb.ChatSession.FindAsync(new object[] { sessionId });
                if (session != null && session.Title?.StartsWith("GnollHack", StringComparison.OrdinalIgnoreCase) == true)
                {
                    var fallbackTitle = userMessage.Length > 50 ? userMessage.Substring(0, 47) + "..." : userMessage;
                    if (!string.IsNullOrWhiteSpace(fallbackTitle))
                    {
                        session.Title = fallbackTitle;
                        await fallbackDb.SaveChangesAsync();
                        
                        var titleUpdateData = new { sessionId = sessionId, title = fallbackTitle };
                        await _hubContext.Clients.User(userId).SendAsync("ReceiveChatEvent", new ChatEvent { Type = "title_update", Data = System.Text.Json.JsonSerializer.Serialize(titleUpdateData) }, CancellationToken.None);
                    }
                }
            }
            catch { /* best-effort */ }

            var errorStatus = new { sessionId = sessionId, status = "" };
            await _hubContext.Clients.User(userId).SendAsync("ReceiveChatEvent", new ChatEvent { Type = "title_status", Data = System.Text.Json.JsonSerializer.Serialize(errorStatus) }, CancellationToken.None);
        }
    }

    public static string BuildRandomizedAppearancePolicy()
    {
        var sb = new StringBuilder();
        sb.AppendLine("## Randomized Item Appearances & Anti-Hallucination Policy");
        sb.AppendLine("- Appearances and descriptions of magical items are reshuffled at the start of every game by shuffle_all() in src/o_init.c.");
        sb.AppendLine("- Whole classes reshuffled (appearance AND materials randomized): amulets, potions, scrolls, spellbooks, venoms.");
        sb.AppendLine("- Type ranges reshuffled, appearance only (materials fixed): helmets, gloves, shirts, cloaks, boots, staves, bags, candles, lamps, whistles, flutes, horns, harps, drums, healing-salve jars.");
        sb.AppendLine("- Type ranges reshuffled, materials also randomized: wands, rings, robes, bracers, brooches, nose rings, headbands, ioun stones, lenses, goggles, belts, crowns, cornuthaum-class hats, mushrooms.");
        sb.AppendLine("- NOT reshuffled: magic swords and all other weapons except staves; the potion of water and every potion type after it in the enum; non-magic and unique amulets, scrolls and spellbooks; reagents.");
        sb.AppendLine("- The appearance strings in src/objects.c (and any wiki appearance tables) are compile-time defaults before shuffling. They describe the pool of possible appearances, NEVER the mapping in the player's current game.");
        sb.AppendLine("- HARD PROHIBITION: Never assert or deny an item's identity from a label, color, material, or description found in source code or on the wiki. Do not say \"the remove curse scroll is labelled PRATYAVAYAH\"; say \"one of the scroll labels corresponds to remove curse, but which label is randomized in each game\".");
        sb.AppendLine("- The ONLY authoritative source for the current game's item identities is the `Discoveries` section of the game state snapshot, which lists identified object types as `true name (appearance)`. A `*` marks a type known from the start of the game and `called X` is a nickname the player assigned. If an appearance is not listed there, the item is unidentified — say so rather than guessing.");
        sb.AppendLine("- When the player asks about an unidentified item, recommend in-game identification: reading a scroll of identify, shop price-bracket identification, BUC testing on an altar, engrave-testing wands, and observing effects on use.");
        sb.AppendLine();
        return sb.ToString();
    }
}
