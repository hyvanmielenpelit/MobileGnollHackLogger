using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Overseer.Services.Agents;
using Overseer.Services.Providers;

namespace Overseer.Services.Tools
{
    public class ToolsForRequest
    {
        /// <summary>Provider native tools — separate wire format per provider.</summary>
        public List<object> ProviderTools { get; set; } = new List<object>();

        /// <summary>Custom function declarations (server + client tools).</summary>
        public List<object> FunctionDeclarations { get; set; } = new List<object>();

        public bool HasTools => ProviderTools.Count > 0 || FunctionDeclarations.Count > 0;
    }

    public class ToolRegistry
    {
        private readonly IEnumerable<IToolHandler> _handlers;
        private readonly IClientToolBridge _clientBridge;
        private readonly ILogger<ToolRegistry> _logger;
        private readonly SubAgentCatalogService? _catalogService;
        private readonly ModelMetadataService? _modelMetadataService;
        private readonly IConfiguration? _configuration;
        private readonly string _guidesPath;
        private string _policyText = string.Empty;
        private string _spoilerPolicyText = string.Empty;
        private string _policyParallelDisabledText = string.Empty;
        private string _policyParallelOnRequestText = string.Empty;

        public ToolRegistry(
            IEnumerable<IToolHandler> handlers,
            IClientToolBridge clientBridge,
            ILogger<ToolRegistry> logger,
            SubAgentCatalogService? catalogService = null,
            ModelMetadataService? modelMetadataService = null,
            IConfiguration? configuration = null)
        {
            // Handlers sorted by ToolName (Ordinal) to ensure deterministic tool schema serialization across restarts and deployments.
            _handlers = handlers.OrderBy(h => h.ToolName, StringComparer.Ordinal).ToList();
            _clientBridge = clientBridge;
            _logger = logger;
            _catalogService = catalogService;
            _modelMetadataService = modelMetadataService;
            _configuration = configuration;
            
            // Assume ToolGuides is at the root of the application
            _guidesPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ToolGuides");
            
            LoadGuides();

            if (_catalogService != null && _modelMetadataService != null)
            {
                _catalogService.Validate(_handlers, _modelMetadataService);
            }
        }

        private void LoadGuides()
        {
            if (!Directory.Exists(_guidesPath))
            {
                return;
            }

            var policyFile = Path.Combine(_guidesPath, "_policy.md");
            if (File.Exists(policyFile))
            {
                _policyText = File.ReadAllText(policyFile);
            }

            var spoilerPolicyFile = Path.Combine(_guidesPath, "spoiler_policy.md");
            if (File.Exists(spoilerPolicyFile))
            {
                _spoilerPolicyText = File.ReadAllText(spoilerPolicyFile);
            }

            var parallelDisabledFile = Path.Combine(_guidesPath, "_policy_parallel_disabled.md");
            if (File.Exists(parallelDisabledFile))
            {
                _policyParallelDisabledText = File.ReadAllText(parallelDisabledFile);
            }

            var parallelOnRequestFile = Path.Combine(_guidesPath, "_policy_parallel_on_request.md");
            if (File.Exists(parallelOnRequestFile))
            {
                _policyParallelOnRequestText = File.ReadAllText(parallelOnRequestFile);
            }

            foreach (var handler in _handlers)
            {
                var guideFile = Path.Combine(_guidesPath, $"{handler.ToolName}.md");
                if (File.Exists(guideFile))
                {
                    handler.Description = File.ReadAllText(guideFile);
                }
                else
                {
                    _logger.LogWarning("Missing tool guide for {ToolName}. Using fallback description.", handler.ToolName);
                }
            }
        }

        public string GetPolicyText()
        {
            return _policyText;
        }

        public string GetSpoilerPolicyText()
        {
            return _spoilerPolicyText;
        }

        public string GetParallelOverrideText(MobileGnollHackLogger.Data.ParallelExecutionMode mode)
        {
            return mode switch
            {
                MobileGnollHackLogger.Data.ParallelExecutionMode.Disabled => _policyParallelDisabledText,
                MobileGnollHackLogger.Data.ParallelExecutionMode.OnRequest => _policyParallelOnRequestText,
                _ => string.Empty
            };
        }

        public ToolsForRequest BuildToolsForRequest(
            IAiProvider provider,
            ToolExecutionContext context,
            bool enableWebSearch,
            bool enableToolUse,
            bool enableClientTools,
            bool enableGameActions,
            string? modelId = null,
            IEnumerable<string>? allowedToolNames = null)
        {
            var result = new ToolsForRequest();

            if (enableWebSearch)
            {
                var webSearchTool = provider.BuildWebSearchTool();
                if (webSearchTool != null)
                {
                    result.ProviderTools.Add(webSearchTool);
                }
            }

            if (!enableToolUse && !enableClientTools)
            {
                return result;
            }

            HashSet<string>? allowedSet = null;
            if (allowedToolNames != null)
            {
                allowedSet = new HashSet<string>(allowedToolNames, StringComparer.OrdinalIgnoreCase);
            }

            foreach (var handler in _handlers)
            {
                if (allowedSet != null && !allowedSet.Contains(handler.ToolName))
                {
                    continue;
                }

                // Filter by execution location and settings
                if (handler.ExecutionLocation == ToolExecutionLocation.Server && !enableToolUse) continue;
                
                if (handler.ExecutionLocation == ToolExecutionLocation.Client)
                {
                    if (!enableClientTools || !_clientBridge.IsClientConnected) continue;
                    
                    if (handler.Category == ToolCategory.GameAction && !enableGameActions) continue;
                    
                    if (handler.Category == ToolCategory.ClientActiveSessionQuery && !context.IsGameOn) continue;
                    
                    if (handler.Category == ToolCategory.ClientPersistentDataQuery && !context.IsGnollHackSession) continue;
                }

                if (handler.Category == ToolCategory.SubAgent)
                {
                    if (context.AgentDepth >= context.MaxAgentDepth) continue;

                    if (_catalogService != null && _modelMetadataService != null && _configuration != null)
                    {
                        if (!SubAgentAvailability.IsAvailableFor(_modelMetadataService, provider.ProviderName, modelId, _catalogService, context, enableToolUse, _configuration))
                        {
                            continue;
                        }
                    }
                }

                // Append function declaration
                result.FunctionDeclarations.Add(
                    provider.BuildFunctionDeclaration(handler.ToolName, GetAdjustedDescription(handler, context), handler.ParameterSchema));
            }

            return result;
        }

        private string GetAdjustedDescription(IToolHandler handler, ToolExecutionContext context)
        {
            var desc = handler.Description;
            if (context.SpoilerFreeMode)
            {
                desc += "\n[Spoiler Free Mode Active: Evaluate returned information against the spoiler policy before sharing with the player.]";
            }
            return desc;
        }

        public ToolExecutionLocation? GetExecutionLocation(string toolName)
        {
            var handler = _handlers.FirstOrDefault(h =>
                string.Equals(h.ToolName, toolName, StringComparison.OrdinalIgnoreCase));
            return handler?.ExecutionLocation;
        }
    }
}
