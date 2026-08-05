using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Logging;
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
        private readonly string _guidesPath;
        private string _policyText = string.Empty;
        private string _spoilerPolicyText = string.Empty;

        public ToolRegistry(IEnumerable<IToolHandler> handlers, IClientToolBridge clientBridge, ILogger<ToolRegistry> logger)
        {
            _handlers = handlers;
            _clientBridge = clientBridge;
            _logger = logger;
            
            // Assume ToolGuides is at the root of the application
            _guidesPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ToolGuides");
            
            LoadGuides();
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

        public ToolsForRequest BuildToolsForRequest(IAiProvider provider, ToolExecutionContext context, bool enableWebSearch, bool enableToolUse, bool enableClientTools, bool enableGameActions)
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

            foreach (var handler in _handlers)
            {
                // Filter by execution location and settings
                if (handler.ExecutionLocation == ToolExecutionLocation.Server && !enableToolUse) continue;
                
                if (handler.ExecutionLocation == ToolExecutionLocation.Client)
                {
                    if (!enableClientTools || !_clientBridge.IsClientConnected) continue;
                    
                    if (handler.Category == ToolCategory.GameAction && !enableGameActions) continue;
                    
                    if (handler.Category == ToolCategory.ClientActiveSessionQuery && !context.IsGameOn) continue;
                    
                    if (handler.Category == ToolCategory.ClientPersistentDataQuery && !context.IsGnollHackSession) continue;
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
    }
}
