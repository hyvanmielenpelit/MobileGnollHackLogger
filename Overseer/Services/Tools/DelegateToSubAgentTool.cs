namespace Overseer.Services.Tools;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MobileGnollHackLogger.Data;
using Overseer.Services.Agents;
using Overseer.Services.Providers;

public class DelegateToSubAgentTool : IToolHandler
{
    private readonly SubAgentCatalogService _catalogService;
    private readonly OngoingChatManager _ongoingChatManager;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly ModelMetadataService _metadataService;
    private readonly ILogger<DelegateToSubAgentTool> _logger;

    public string ToolName => "delegate_to_subagent";
    public string Description { get; set; } = "Delegates a specialized sub-task to an autonomous subagent.";
    public ToolExecutionLocation ExecutionLocation => ToolExecutionLocation.Server;
    public ToolCategory Category => ToolCategory.SubAgent;
    public int TimeoutSeconds { get; set; }

    public JsonElement ParameterSchema
    {
        get
        {
            var agentNames = _catalogService.GetEnabledSubAgents().Select(a => a.Name).ToArray();
            var agentNameNode = new Dictionary<string, object>
            {
                ["type"] = "string",
                ["description"] = "The registered name of the specialized subagent to invoke (e.g., 'wiki_researcher', 'source_investigator', 'game_data_analyst')."
            };
            if (agentNames.Length > 0)
            {
                agentNameNode["enum"] = agentNames;
            }

            var schema = new
            {
                type = "object",
                properties = new Dictionary<string, object>
                {
                    ["agent_name"] = agentNameNode,
                    ["task"] = new
                    {
                        type = "string",
                        description = "The specific task or inquiry for the subagent to execute autonomously."
                    },
                    ["context"] = new
                    {
                        type = "string",
                        description = "Optional background context, prior discoveries, or game state information relevant to the task."
                    }
                },
                required = new[] { "agent_name", "task" }
            };
            return JsonSerializer.SerializeToElement(schema);
        }
    }

    public DelegateToSubAgentTool(
        SubAgentCatalogService catalogService,
        OngoingChatManager ongoingChatManager,
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ModelMetadataService metadataService,
        ILogger<DelegateToSubAgentTool> logger)
    {
        _catalogService = catalogService;
        _ongoingChatManager = ongoingChatManager;
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _metadataService = metadataService;
        _logger = logger;
        TimeoutSeconds = configuration.GetValue<int>("SubAgentSettings:TimeoutSeconds", 600);
    }

    public async Task<ToolResult> ExecuteAsync(
        JsonElement parameters,
        ToolExecutionContext context,
        CancellationToken cancellationToken)
    {
        if (!context.EnableSubAgents)
        {
            return new ToolResult
            {
                Success = false,
                ErrorMessage = "Subagent execution is disabled in user settings."
            };
        }

        string? agentName = null;
        string? taskText = null;
        string? contextText = null;

        if (parameters.ValueKind == JsonValueKind.Object)
        {
            if (parameters.TryGetProperty("agent_name", out var agentProp) && agentProp.ValueKind == JsonValueKind.String)
                agentName = agentProp.GetString();
            if (parameters.TryGetProperty("task", out var taskProp) && taskProp.ValueKind == JsonValueKind.String)
                taskText = taskProp.GetString();
            if (parameters.TryGetProperty("context", out var ctxProp) && ctxProp.ValueKind == JsonValueKind.String)
                contextText = ctxProp.GetString();
        }

        if (string.IsNullOrWhiteSpace(agentName) || string.IsNullOrWhiteSpace(taskText))
        {
            return new ToolResult
            {
                Success = false,
                ErrorMessage = "Missing required parameters 'agent_name' and 'task'."
            };
        }

        if (context.AgentDepth >= context.MaxAgentDepth)
        {
            return new ToolResult
            {
                Success = false,
                ErrorMessage = $"Maximum subagent recursion depth ({context.MaxAgentDepth}) reached."
            };
        }

        var subAgentDef = _catalogService.GetSubAgent(agentName);
        if (subAgentDef == null || !subAgentDef.IsEnabled)
        {
            return new ToolResult
            {
                Success = false,
                ErrorMessage = $"Subagent '{agentName}' is not registered or is disabled."
            };
        }

        // Budget check
        if (context.Budget != null && !context.Budget.TryStartSubAgent(context.ShowDebugLog, out string? budgetError))
        {
            return new ToolResult
            {
                Success = false,
                ErrorMessage = budgetError ?? "Subagent execution limit reached."
            };
        }

        string toolCallId = context.ToolCallId ?? Guid.NewGuid().ToString();
        using var subAgentCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _ongoingChatManager.TryRegisterSubAgent(context.SessionId, toolCallId, subAgentCts);

        var subAgentResult = new AgentRunResult();

        try
        {
            using var scope = _scopeFactory.CreateScope();

            // 1. Resolve Provider, Model, and API Key
            var resolved = await ResolveModelAndCredentialsAsync(subAgentDef, scope.ServiceProvider, context, cancellationToken);

            var providers = scope.ServiceProvider.GetServices<IAiProvider>();
            var aiProvider = providers.FirstOrDefault(p => string.Equals(p.ProviderName, resolved.Provider, StringComparison.OrdinalIgnoreCase));
            if (aiProvider == null)
            {
                return new ToolResult
                {
                    Success = false,
                    ErrorMessage = $"AI provider '{resolved.Provider}' is not available."
                };
            }

            var metadata = _metadataService.GetMetadata(resolved.Provider, resolved.ModelId);
            if (!metadata.SupportsSubAgentExecution)
            {
                return new ToolResult
                {
                    Success = false,
                    ErrorMessage = $"Model '{resolved.ModelId}' is not permitted to execute subagents."
                };
            }

            // 2. Resolve Output Tokens & Reasoning Parameters with precedence & validation
            int? resolvedMaxOutputTokens = subAgentDef.MaxOutputTokens ?? resolved.MaxOutputTokens;
            string? resolvedThinkingLevel = subAgentDef.ThinkingLevel ?? resolved.ThinkingLevel;
            string? resolvedReasoningMode = resolved.ReasoningMode;
            string? resolvedReasoningSummary = resolved.ReasoningSummary;
            string? resolvedServiceTier = resolved.ServiceTier;

            if (!resolvedMaxOutputTokens.HasValue && metadata.MaxOutputTokens > 0)
            {
                resolvedMaxOutputTokens = metadata.MaxOutputTokens;
            }

            if (!string.IsNullOrEmpty(resolvedThinkingLevel) &&
                metadata.SupportedThinkingLevels.Count > 0 &&
                !metadata.SupportedThinkingLevels.Contains(resolvedThinkingLevel, StringComparer.OrdinalIgnoreCase))
            {
                resolvedThinkingLevel = null;
            }

            if (!string.IsNullOrEmpty(resolvedReasoningMode) &&
                metadata.SupportedReasoningModes.Count > 0 &&
                !metadata.SupportedReasoningModes.Contains(resolvedReasoningMode, StringComparer.OrdinalIgnoreCase))
            {
                resolvedReasoningMode = null;
            }

            if (!string.IsNullOrEmpty(resolvedReasoningSummary) &&
                metadata.SupportedReasoningSummaries.Count > 0 &&
                !metadata.SupportedReasoningSummaries.Contains(resolvedReasoningSummary, StringComparer.OrdinalIgnoreCase))
            {
                resolvedReasoningSummary = null;
            }

            if (!string.IsNullOrEmpty(resolvedServiceTier) &&
                aiProvider.SupportedServiceTiers.Count > 0 &&
                !aiProvider.SupportedServiceTiers.Contains(resolvedServiceTier, StringComparer.OrdinalIgnoreCase))
            {
                resolvedServiceTier = null;
            }

            // 3. Build subagent initial seed history & execution context
            var seedHistory = new List<object>();
            string fullUserPrompt = taskText;
            if (!string.IsNullOrWhiteSpace(contextText))
            {
                fullUserPrompt += $"\n\nContext:\n{contextText}";
            }

            seedHistory.Add(aiProvider.FormatMessage("user", fullUserPrompt, null));
            seedHistory.Insert(0, new { role = "system", content = subAgentDef.Instructions });
            seedHistory = aiProvider.PrepareMessageHistory(seedHistory);

            var subAgentExecContext = context.CloneFor(toolCallId);
            subAgentExecContext.AgentName = subAgentDef.Name;
            subAgentExecContext.AgentDepth = context.AgentDepth + 1;
            subAgentExecContext.MaxAgentDepth = context.MaxAgentDepth;
            subAgentExecContext.ActiveSystemModelId = resolved.SystemModelId;
            subAgentExecContext.ActiveUserModelId = resolved.UserModelId;

            string maxOutputTokensSource = subAgentDef.MaxOutputTokens.HasValue ? "SubAgentCatalog"
                : resolved.MaxOutputTokens.HasValue ? "InheritedModelRow"
                : "ModelCatalog";

            if (context.ShowDebugLog && context.EventSink != null)
            {
                await context.EventSink.Invoke(new ChatEvent
                {
                    Type = "debug",
                    Data = $"[SubAgent:{agentName} - Model Configuration]\n" +
                           $"Provider: {resolved.Provider}\n" +
                           $"Model: {resolved.ModelId}\n" +
                           $"Credential Source: {resolved.CredentialSource}\n" +
                           $"Thinking Level: {resolvedThinkingLevel ?? "None"}\n" +
                           $"Reasoning Mode: {resolvedReasoningMode ?? "None"}\n" +
                           $"Service Tier: {resolvedServiceTier ?? "None"}\n" +
                           $"Max Output Tokens: {resolvedMaxOutputTokens?.ToString() ?? "Provider default"}\n" +
                           $"Max Output Tokens Source: {maxOutputTokensSource}\n" +
                           $"Spoiler Free Mode: {subAgentExecContext.SpoilerFreeMode}\n" +
                           $"Session: {subAgentExecContext.SessionId}\n" +
                           $"Allowed Tools: {string.Join(", ", subAgentDef.AllowedTools)}\n" +
                           $"Max Iterations: {subAgentDef.MaxIterations ?? 10}\n" +
                           $"Depth: {context.AgentDepth + 1}/{context.MaxAgentDepth}"
                });
            }

            // 4. Create subagent request
            var subAgentRequest = new AgentRunRequest
            {
                ProviderName = resolved.Provider,
                ModelId = resolved.ModelId,
                ApiKey = resolved.ApiKey,
                ModelDisplayName = subAgentDef.DisplayName,
                SystemPrompt = subAgentDef.Instructions,
                SeedHistory = seedHistory,
                ThinkingLevel = resolvedThinkingLevel,
                ReasoningMode = resolvedReasoningMode,
                ReasoningSummary = resolvedReasoningSummary,
                ServiceTier = resolvedServiceTier,
                MaxOutputTokens = resolvedMaxOutputTokens,
                MaxToolIterations = subAgentDef.MaxIterations ?? 10,
                MaxParallelTools = 1,
                MaxParallelClientTools = 1,
                EnableWebSearch = subAgentDef.EnableWebSearch,
                EnableToolUse = true,
                EnableClientTools = false,
                EnableGameActions = false,
                ToolExecutionContext = subAgentExecContext,
                SystemModelId = resolved.SystemModelId,
                AgentName = subAgentDef.Name,
                AgentDepth = context.AgentDepth + 1,
                MaxAgentDepth = context.MaxAgentDepth,
                ParentToolCallId = toolCallId,
                AllowedToolNames = subAgentDef.AllowedTools,
                ShowDebugLog = context.ShowDebugLog,
                AiProvider = aiProvider,
                Budget = context.Budget
            };

            var runner = scope.ServiceProvider.GetRequiredService<AgentLoopRunner>();

            await foreach (var evt in runner.RunAsync(subAgentRequest, context.Budget, subAgentResult, subAgentCts.Token))
            {
                if (context.EventSink == null)
                {
                    continue;
                }

                // Explicit allow-list. Everything else from the subagent stays isolated
                if ((evt.Type == "debug" && context.ShowDebugLog) ||
                    evt.Type == "tool_start" ||
                    evt.Type == "tool_result" ||
                    evt.Type == "tool_error")
                {
                    await context.EventSink.Invoke(evt);
                }
            }

            string status = subAgentResult.TerminationReason ?? "completed";
            bool completed = status == "completed";
            bool aborted = status == "canceled" || status == "error";
            string content = subAgentResult.FinalText ?? "";

            if (!completed && !string.IsNullOrWhiteSpace(content))
            {
                content = $"[PARTIAL RESULT — subagent '{agentName}' terminated early: {status}. " +
                          $"Findings below are incomplete and must not be presented as a complete answer.]\n\n"
                          + content;
            }

            return new ToolResult
            {
                Success = !aborted && !string.IsNullOrWhiteSpace(content),
                Content = content,
                // Success=false + non-empty Content: ToolBatchRunner prefers ErrorMessage over Content
                // when Success is false, so ErrorMessage must be null for the banner to reach the model.
                ErrorMessage = string.IsNullOrWhiteSpace(content)
                    ? (completed ? null : $"Subagent terminated with status: {status}")
                    : null,
                NestedToolCalls = subAgentResult.ToolCalls,
                TerminationStatus = status
            };
        }
        catch (OperationCanceledException) when (subAgentCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation("Subagent {AgentName} (ToolCallId: {ToolCallId}) was canceled by user request.", agentName, toolCallId);
            string content = subAgentResult.FinalText ?? "";
            if (!string.IsNullOrWhiteSpace(content))
            {
                content = $"[PARTIAL RESULT — subagent '{agentName}' terminated early: canceled. " +
                          $"Findings below are incomplete and must not be presented as a complete answer.]\n\n"
                          + content;
            }
            return new ToolResult
            {
                Success = false,
                ErrorMessage = string.IsNullOrWhiteSpace(content) ? "Subagent execution was canceled by the user." : null,
                Content = content,
                NestedToolCalls = subAgentResult.ToolCalls,
                TerminationStatus = "canceled"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in Subagent {AgentName} execution for Session {SessionId}", agentName, context.SessionId);
            string content = subAgentResult.FinalText ?? "";
            if (!string.IsNullOrWhiteSpace(content))
            {
                content = $"[PARTIAL RESULT — subagent '{agentName}' terminated early: error. " +
                          $"Findings below are incomplete and must not be presented as a complete answer.]\n\n"
                          + content;
            }
            return new ToolResult
            {
                Success = false,
                ErrorMessage = string.IsNullOrWhiteSpace(content) ? $"Subagent execution error: {ex.Message}" : null,
                Content = content,
                NestedToolCalls = subAgentResult.ToolCalls,
                TerminationStatus = "error"
            };
        }
        finally
        {
            _ongoingChatManager.UnregisterSubAgent(context.SessionId, toolCallId);
            if (context.Budget != null)
            {
                context.Budget.EndSubAgent();
            }
        }
    }

    private sealed record ResolvedSubAgentModel(
        string Provider,
        string ModelId,
        string ApiKey,
        string CredentialSource,
        int? MaxOutputTokens,
        string? ThinkingLevel,
        string? ReasoningMode,
        string? ReasoningSummary,
        string? ServiceTier,
        long? SystemModelId,
        long? UserModelId = null);

    private async Task<ResolvedSubAgentModel> ResolveModelAndCredentialsAsync(
        SubAgentDefinition definition,
        IServiceProvider scopedServices,
        ToolExecutionContext context,
        CancellationToken cancellationToken)
    {
        var dbContext = scopedServices.GetRequiredService<ApplicationDbContext>();
        var cryptoService = scopedServices.GetRequiredService<CryptoService>();
        var settingsService = scopedServices.GetRequiredService<SettingsService>();
        var systemAiConfigService = scopedServices.GetRequiredService<SystemAiConfigService>();

        bool IsEligible(string provider, string modelId) =>
            scopedServices.GetServices<IAiProvider>()
                .Any(p => string.Equals(p.ProviderName, provider, StringComparison.OrdinalIgnoreCase))
            && _metadataService.GetMetadata(provider, modelId).SupportsSubAgentExecution;

        var session = await dbContext.ChatSession.FirstOrDefaultAsync(s => s.Id == context.SessionId, cancellationToken);
        string? userId = session?.AspNetUserId;

        // Step 1: Check if definition specifies a preferred model
        if (definition.ModelPreference != null &&
            !string.IsNullOrWhiteSpace(definition.ModelPreference.Provider) &&
            !string.IsNullOrWhiteSpace(definition.ModelPreference.ModelId) &&
            IsEligible(definition.ModelPreference.Provider, definition.ModelPreference.ModelId))
        {
            string prefProvider = definition.ModelPreference.Provider;
            string prefModelId = definition.ModelPreference.ModelId;

            // 1.1 Try Authorized System Configuration in DB
            if (!string.IsNullOrEmpty(userId))
            {
                var sysModels = await settingsService.GetResolvedSystemModelsAsync(userId, roleFilter: 1);
                var matchingSys = sysModels.FirstOrDefault(c =>
                    string.Equals(c.Config.Provider, prefProvider, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(c.Config.ModelId, prefModelId, StringComparison.OrdinalIgnoreCase)).Config;

                if (matchingSys != null && !string.IsNullOrWhiteSpace(matchingSys.EncryptedApiKey) &&
                    !string.IsNullOrWhiteSpace(matchingSys.ApiKeyNonce) && !string.IsNullOrWhiteSpace(matchingSys.ApiKeyTag))
                {
                    try
                    {
                        var decryptedKey = cryptoService.Decrypt(matchingSys.EncryptedApiKey, matchingSys.ApiKeyNonce, matchingSys.ApiKeyTag, "SYSTEM_API_KEY");
                        if (!string.IsNullOrWhiteSpace(decryptedKey))
                        {
                            return new ResolvedSubAgentModel(
                                prefProvider,
                                prefModelId,
                                decryptedKey,
                                "PreferredSystemModel",
                                matchingSys.MaxOutputTokens,
                                matchingSys.ThinkingLevel,
                                matchingSys.ReasoningMode,
                                matchingSys.ReasoningSummary,
                                matchingSys.ServiceTier,
                                matchingSys.Id,
                                null);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to decrypt API key during subagent model resolution (branch 1.1).");
                    }
                }
            }

            // 1.2 Try AppSettings System Config Key
            string? appSettingKey = _configuration[$"AI:{prefProvider}:APIKey"] ??
                                   (string.Equals(_configuration["AI:Provider"], prefProvider, StringComparison.OrdinalIgnoreCase) ? _configuration["AI:APIKey"] : null);

            if (!string.IsNullOrWhiteSpace(appSettingKey))
            {
                return new ResolvedSubAgentModel(
                    prefProvider,
                    prefModelId,
                    appSettingKey,
                    "PreferredAppSettingsKey",
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null);
            }

            // 1.3 Try User Custom Key for this provider
            if (!string.IsNullOrEmpty(userId))
            {
                var userKey = await dbContext.UserAiApiKeys.FirstOrDefaultAsync(
                    k => k.AspNetUserId == userId && k.Provider == prefProvider, cancellationToken);

                if (userKey != null && !string.IsNullOrWhiteSpace(userKey.EncryptedApiKey) &&
                    !string.IsNullOrWhiteSpace(userKey.ApiKeyNonce) && !string.IsNullOrWhiteSpace(userKey.ApiKeyTag))
                {
                    try
                    {
                        var decryptedKey = cryptoService.Decrypt(userKey.EncryptedApiKey, userKey.ApiKeyNonce, userKey.ApiKeyTag, userId);
                        if (!string.IsNullOrWhiteSpace(decryptedKey))
                        {
                            return new ResolvedSubAgentModel(
                                prefProvider,
                                prefModelId,
                                decryptedKey,
                                "PreferredUserKey",
                                null,
                                null,
                                null,
                                null,
                                null,
                                null,
                                null);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to decrypt API key during subagent model resolution (branch 1.3).");
                    }
                }
            }
        }

        // Step 2: Active Turn & Fallback Resolution
        // Branch 2A (Active System Model)
        if (context.ActiveSystemModelId.HasValue && !string.IsNullOrEmpty(userId))
        {
            var (sysConfig, errorMessage) = await systemAiConfigService.GetAndCheckSystemConfigAsync(context.ActiveSystemModelId.Value, userId, requiredRoleFilter: 1);
            if (sysConfig != null && IsEligible(sysConfig.Provider, sysConfig.ModelId) && !string.IsNullOrWhiteSpace(sysConfig.EncryptedApiKey) &&
                !string.IsNullOrWhiteSpace(sysConfig.ApiKeyNonce) && !string.IsNullOrWhiteSpace(sysConfig.ApiKeyTag))
            {
                try
                {
                    var decryptedKey = cryptoService.Decrypt(sysConfig.EncryptedApiKey, sysConfig.ApiKeyNonce, sysConfig.ApiKeyTag, "SYSTEM_API_KEY");
                    if (!string.IsNullOrWhiteSpace(decryptedKey))
                    {
                        return new ResolvedSubAgentModel(
                            sysConfig.Provider,
                            sysConfig.ModelId,
                            decryptedKey,
                            "ActiveSystemModel",
                            sysConfig.MaxOutputTokens,
                            sysConfig.ThinkingLevel,
                            sysConfig.ReasoningMode,
                            sysConfig.ReasoningSummary,
                            sysConfig.ServiceTier,
                            sysConfig.Id,
                            null);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to decrypt API key during subagent model resolution (branch 2A).");
                }
            }
            else if (errorMessage != null)
            {
                _logger.LogDebug("Subagent could not use ActiveSystemModelId {ConfigId}: {ErrorMessage}", context.ActiveSystemModelId.Value, errorMessage);
            }
        }

        // Branch 2B (Active User Model)
        if (context.ActiveUserModelId.HasValue && !string.IsNullOrEmpty(userId))
        {
            var activeUserModel = await dbContext.UserAiModels.FirstOrDefaultAsync(
                m => m.Id == context.ActiveUserModelId.Value && m.AspNetUserId == userId, cancellationToken);
            if (activeUserModel != null && IsEligible(activeUserModel.Provider, activeUserModel.ModelId))
            {
                string? userApiKey = null;
                var userKey = await dbContext.UserAiApiKeys.FirstOrDefaultAsync(
                    k => k.AspNetUserId == userId && k.Provider == activeUserModel.Provider, cancellationToken);
                if (userKey != null && !string.IsNullOrWhiteSpace(userKey.EncryptedApiKey) &&
                    !string.IsNullOrWhiteSpace(userKey.ApiKeyNonce) && !string.IsNullOrWhiteSpace(userKey.ApiKeyTag))
                {
                    try
                    {
                        userApiKey = cryptoService.Decrypt(userKey.EncryptedApiKey, userKey.ApiKeyNonce, userKey.ApiKeyTag, userId);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to decrypt API key during subagent model resolution (branch 2B).");
                    }
                }

                if (string.IsNullOrWhiteSpace(userApiKey))
                {
                    userApiKey = _configuration[$"AI:{activeUserModel.Provider}:APIKey"] ??
                                 (string.Equals(_configuration["AI:Provider"], activeUserModel.Provider, StringComparison.OrdinalIgnoreCase) ? _configuration["AI:APIKey"] : null);
                }

                if (!string.IsNullOrWhiteSpace(userApiKey))
                {
                    return new ResolvedSubAgentModel(
                        activeUserModel.Provider,
                        activeUserModel.ModelId,
                        userApiKey,
                        "ActiveUserModel",
                        activeUserModel.MaxOutputTokens,
                        activeUserModel.ThinkingLevel,
                        activeUserModel.ReasoningMode,
                        activeUserModel.ReasoningSummary,
                        activeUserModel.ServiceTier,
                        null,
                        activeUserModel.Id);
                }
            }
        }

        // Branch 2C (User Default Model)
        if (!string.IsNullOrEmpty(userId))
        {
            var userModels = await dbContext.UserAiModels
                .Where(m => m.AspNetUserId == userId)
                .OrderBy(m => m.OrderIndex)
                .ToListAsync(cancellationToken);
            var defaultUserModel = userModels.FirstOrDefault(m => IsEligible(m.Provider, m.ModelId));
            if (defaultUserModel != null)
            {
                string? userApiKey = null;
                var userKey = await dbContext.UserAiApiKeys.FirstOrDefaultAsync(
                    k => k.AspNetUserId == userId && k.Provider == defaultUserModel.Provider, cancellationToken);
                if (userKey != null && !string.IsNullOrWhiteSpace(userKey.EncryptedApiKey) &&
                    !string.IsNullOrWhiteSpace(userKey.ApiKeyNonce) && !string.IsNullOrWhiteSpace(userKey.ApiKeyTag))
                {
                    try
                    {
                        userApiKey = cryptoService.Decrypt(userKey.EncryptedApiKey, userKey.ApiKeyNonce, userKey.ApiKeyTag, userId);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to decrypt API key during subagent model resolution (branch 2C).");
                    }
                }

                if (string.IsNullOrWhiteSpace(userApiKey))
                {
                    userApiKey = _configuration[$"AI:{defaultUserModel.Provider}:APIKey"] ??
                                 (string.Equals(_configuration["AI:Provider"], defaultUserModel.Provider, StringComparison.OrdinalIgnoreCase) ? _configuration["AI:APIKey"] : null);
                }

                if (!string.IsNullOrWhiteSpace(userApiKey))
                {
                    return new ResolvedSubAgentModel(
                        defaultUserModel.Provider,
                        defaultUserModel.ModelId,
                        userApiKey,
                        "UserDefaultModel",
                        defaultUserModel.MaxOutputTokens,
                        defaultUserModel.ThinkingLevel,
                        defaultUserModel.ReasoningMode,
                        defaultUserModel.ReasoningSummary,
                        defaultUserModel.ServiceTier,
                        null,
                        defaultUserModel.Id);
                }
            }
        }

        // Branch 2D (First Authorized System Model)
        if (!string.IsNullOrEmpty(userId))
        {
            var sysModels = await settingsService.GetResolvedSystemModelsAsync(userId, roleFilter: 1);
            var firstSys = sysModels.FirstOrDefault(c => c.Config != null && IsEligible(c.Config.Provider, c.Config.ModelId)).Config;
            if (firstSys != null && !string.IsNullOrWhiteSpace(firstSys.EncryptedApiKey) &&
                !string.IsNullOrWhiteSpace(firstSys.ApiKeyNonce) && !string.IsNullOrWhiteSpace(firstSys.ApiKeyTag))
            {
                try
                {
                    var decryptedKey = cryptoService.Decrypt(firstSys.EncryptedApiKey, firstSys.ApiKeyNonce, firstSys.ApiKeyTag, "SYSTEM_API_KEY");
                    if (!string.IsNullOrWhiteSpace(decryptedKey))
                    {
                        return new ResolvedSubAgentModel(
                            firstSys.Provider,
                            firstSys.ModelId,
                            decryptedKey,
                            "AssignedSystemModel",
                            firstSys.MaxOutputTokens,
                            firstSys.ThinkingLevel,
                            firstSys.ReasoningMode,
                            firstSys.ReasoningSummary,
                            firstSys.ServiceTier,
                            firstSys.Id,
                            null);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to decrypt API key during subagent model resolution (branch 2D).");
                }
            }
        }

        // Branch 2E (AppSettings Fallback)
        string fallbackProvider = _configuration["AI:Provider"] ?? "OpenAI";
        string fallbackModelId = _configuration["AI:Model"] ?? "gpt-5.4";
        string fallbackApiKey = _configuration[$"AI:{fallbackProvider}:APIKey"] ?? _configuration["AI:APIKey"] ?? "";

        return new ResolvedSubAgentModel(
            fallbackProvider,
            fallbackModelId,
            fallbackApiKey,
            "AppSettings",
            null,
            null,
            null,
            null,
            null,
            null,
            null);
    }
}
