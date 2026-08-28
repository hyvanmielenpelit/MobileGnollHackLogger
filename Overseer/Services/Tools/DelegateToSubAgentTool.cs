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
            var schema = new
            {
                type = "object",
                properties = new
                {
                    agent_name = new
                    {
                        type = "string",
                        description = "The registered name of the specialized subagent to invoke (e.g., 'wiki_researcher', 'source_investigator', 'game_data_analyst')."
                    },
                    task = new
                    {
                        type = "string",
                        description = "The specific task or inquiry for the subagent to execute autonomously."
                    },
                    context = new
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
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var cryptoService = scope.ServiceProvider.GetRequiredService<CryptoService>();

            // 1. Resolve Provider, Model, and API Key
            var resolved = await ResolveModelAndCredentialsAsync(subAgentDef, dbContext, cryptoService, context.SessionId, cancellationToken);

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
                if (evt.Type == "debug" && context.EventSink != null)
                {
                    await context.EventSink.Invoke(evt);
                }
            }

            return new ToolResult
            {
                Success = subAgentResult.TerminationReason == "Completed" || !string.IsNullOrWhiteSpace(subAgentResult.FinalText),
                Content = subAgentResult.FinalText ?? "",
                ErrorMessage = subAgentResult.TerminationReason == "Completed" ? null : $"Subagent terminated with status: {subAgentResult.TerminationReason}",
                NestedToolCalls = subAgentResult.ToolCalls,
                TerminationStatus = subAgentResult.TerminationReason
            };
        }
        catch (OperationCanceledException) when (subAgentCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation("Subagent {AgentName} (ToolCallId: {ToolCallId}) was canceled by user request.", agentName, toolCallId);
            return new ToolResult
            {
                Success = false,
                ErrorMessage = "Subagent execution was canceled by the user.",
                Content = subAgentResult.FinalText ?? "",
                NestedToolCalls = subAgentResult.ToolCalls,
                TerminationStatus = "canceled"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in Subagent {AgentName} execution for Session {SessionId}", agentName, context.SessionId);
            return new ToolResult
            {
                Success = false,
                ErrorMessage = $"Subagent execution error: {ex.Message}",
                Content = subAgentResult.FinalText ?? "",
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
        long? SystemModelId);

    private async Task<ResolvedSubAgentModel> ResolveModelAndCredentialsAsync(
        SubAgentDefinition definition,
        ApplicationDbContext dbContext,
        CryptoService cryptoService,
        long sessionId,
        CancellationToken cancellationToken)
    {
        var session = await dbContext.ChatSession.FirstOrDefaultAsync(s => s.Id == sessionId, cancellationToken);
        string? userId = session?.AspNetUserId;

        // Check if definition specifies a preferred model
        if (definition.ModelPreference != null &&
            !string.IsNullOrWhiteSpace(definition.ModelPreference.Provider) &&
            !string.IsNullOrWhiteSpace(definition.ModelPreference.ModelId))
        {
            string prefProvider = definition.ModelPreference.Provider;
            string prefModelId = definition.ModelPreference.ModelId;

            // 1. Try System Configuration in DB
            var sysConfig = await dbContext.SystemAiApiConfigurations.FirstOrDefaultAsync(
                c => c.Provider == prefProvider && c.ModelId == prefModelId && c.IsEnabled, cancellationToken);

            if (sysConfig != null && !string.IsNullOrWhiteSpace(sysConfig.EncryptedApiKey) &&
                !string.IsNullOrWhiteSpace(sysConfig.ApiKeyNonce) && !string.IsNullOrWhiteSpace(sysConfig.ApiKeyTag))
            {
                try
                {
                    var decryptedKey = cryptoService.Decrypt(sysConfig.EncryptedApiKey, sysConfig.ApiKeyNonce, sysConfig.ApiKeyTag, "SYSTEM_API_KEY");
                    if (!string.IsNullOrWhiteSpace(decryptedKey))
                    {
                        return new ResolvedSubAgentModel(
                            prefProvider,
                            prefModelId,
                            decryptedKey,
                            "SystemConfig",
                            sysConfig.MaxOutputTokens,
                            sysConfig.ThinkingLevel,
                            sysConfig.ReasoningMode,
                            sysConfig.ReasoningSummary,
                            sysConfig.ServiceTier,
                            sysConfig.Id);
                    }
                }
                catch { }
            }

            // 2. Try AppSettings System Config Key
            string? appSettingKey = _configuration[$"AI:{prefProvider}:APIKey"] ??
                                   (string.Equals(_configuration["AI:Provider"], prefProvider, StringComparison.OrdinalIgnoreCase) ? _configuration["AI:APIKey"] : null);

            if (!string.IsNullOrWhiteSpace(appSettingKey))
            {
                return new ResolvedSubAgentModel(
                    prefProvider,
                    prefModelId,
                    appSettingKey,
                    "SystemConfig",
                    null,
                    null,
                    null,
                    null,
                    null,
                    null);
            }

            // 3. Try User Custom Key for this provider
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
                                "UserKey",
                                null,
                                null,
                                null,
                                null,
                                null,
                                null);
                        }
                    }
                    catch { }
                }
            }
        }

        // 4. Fallback: Inherit coordinator model & credentials from user default or system config
        string fallbackProvider = _configuration["AI:Provider"] ?? "OpenAI";
        string fallbackModelId = _configuration["AI:Model"] ?? "gpt-5.4";
        string fallbackApiKey = "";
        int? fallbackMaxOutputTokens = null;
        string? fallbackThinkingLevel = null;
        string? fallbackReasoningMode = null;
        string? fallbackReasoningSummary = null;
        string? fallbackServiceTier = null;
        long? fallbackSystemModelId = null;

        if (!string.IsNullOrEmpty(userId))
        {
            var userModel = await dbContext.UserAiModels.OrderBy(m => m.OrderIndex).FirstOrDefaultAsync(m => m.AspNetUserId == userId, cancellationToken);
            if (userModel != null)
            {
                fallbackProvider = userModel.Provider;
                fallbackModelId = userModel.ModelId;
                fallbackMaxOutputTokens = userModel.MaxOutputTokens;
                fallbackThinkingLevel = userModel.ThinkingLevel;
                fallbackReasoningMode = userModel.ReasoningMode;
                fallbackReasoningSummary = userModel.ReasoningSummary;
                fallbackServiceTier = userModel.ServiceTier;

                var userKey = await dbContext.UserAiApiKeys.FirstOrDefaultAsync(k => k.AspNetUserId == userId && k.Provider == userModel.Provider, cancellationToken);
                if (userKey != null && !string.IsNullOrWhiteSpace(userKey.EncryptedApiKey) &&
                    !string.IsNullOrWhiteSpace(userKey.ApiKeyNonce) && !string.IsNullOrWhiteSpace(userKey.ApiKeyTag))
                {
                    try { fallbackApiKey = cryptoService.Decrypt(userKey.EncryptedApiKey, userKey.ApiKeyNonce, userKey.ApiKeyTag, userId); } catch { }
                }
            }
        }

        if (string.IsNullOrWhiteSpace(fallbackApiKey))
        {
            var firstSys = await dbContext.SystemAiApiConfigurations.FirstOrDefaultAsync(c => c.IsEnabled, cancellationToken);
            if (firstSys != null && !string.IsNullOrWhiteSpace(firstSys.EncryptedApiKey) &&
                !string.IsNullOrWhiteSpace(firstSys.ApiKeyNonce) && !string.IsNullOrWhiteSpace(firstSys.ApiKeyTag))
            {
                try
                {
                    fallbackApiKey = cryptoService.Decrypt(firstSys.EncryptedApiKey, firstSys.ApiKeyNonce, firstSys.ApiKeyTag, "SYSTEM_API_KEY");
                    fallbackProvider = firstSys.Provider;
                    fallbackModelId = firstSys.ModelId;
                    fallbackMaxOutputTokens = firstSys.MaxOutputTokens;
                    fallbackThinkingLevel = firstSys.ThinkingLevel;
                    fallbackReasoningMode = firstSys.ReasoningMode;
                    fallbackReasoningSummary = firstSys.ReasoningSummary;
                    fallbackServiceTier = firstSys.ServiceTier;
                    fallbackSystemModelId = firstSys.Id;
                }
                catch { }
            }
        }

        if (string.IsNullOrWhiteSpace(fallbackApiKey))
        {
            fallbackApiKey = _configuration[$"AI:{fallbackProvider}:APIKey"] ?? _configuration["AI:APIKey"] ?? "";
        }

        return new ResolvedSubAgentModel(
            fallbackProvider,
            fallbackModelId,
            fallbackApiKey,
            "Inherited",
            fallbackMaxOutputTokens,
            fallbackThinkingLevel,
            fallbackReasoningMode,
            fallbackReasoningSummary,
            fallbackServiceTier,
            fallbackSystemModelId);
    }
}
