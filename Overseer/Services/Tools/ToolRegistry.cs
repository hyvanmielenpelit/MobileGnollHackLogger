using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Logging;

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

        public ToolsForRequest BuildToolsForRequest(string provider, ToolExecutionContext context, bool enableWebSearch, bool enableToolUse, bool enableClientTools, bool enableGameActions)
        {
            var result = new ToolsForRequest();

            if (enableWebSearch)
            {
                if (provider.Equals("Google", StringComparison.OrdinalIgnoreCase))
                {
                    result.ProviderTools.Add(new { googleSearch = new { } });
                }
                else if (provider.Equals("OpenAI", StringComparison.OrdinalIgnoreCase))
                {
                    result.ProviderTools.Add(new { type = "web_search" });
                }
                else if (provider.Equals("Anthropic", StringComparison.OrdinalIgnoreCase))
                {
                    result.ProviderTools.Add(new { type = "web_search_20260318", name = "web_search" });
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
                    
                    if (handler.Category == ToolCategory.ClientStateQuery && !context.IsGameOn) continue;
                }

                // Append function declaration
                if (provider.Equals("Google", StringComparison.OrdinalIgnoreCase))
                {
                    result.FunctionDeclarations.Add(new
                    {
                        name = handler.ToolName,
                        description = GetAdjustedDescription(handler, context),
                        parameters = handler.ParameterSchema
                    });
                }
                else if (provider.Equals("OpenAI", StringComparison.OrdinalIgnoreCase))
                {
                    result.FunctionDeclarations.Add(new
                    {
                        type = "function",
                        function = new
                        {
                            name = handler.ToolName,
                            description = GetAdjustedDescription(handler, context),
                            parameters = handler.ParameterSchema
                        }
                    });
                }
                else if (provider.Equals("Anthropic", StringComparison.OrdinalIgnoreCase))
                {
                    result.FunctionDeclarations.Add(new
                    {
                        name = handler.ToolName,
                        description = GetAdjustedDescription(handler, context),
                        input_schema = handler.ParameterSchema
                    });
                }
            }

            return result;
        }

        private string GetAdjustedDescription(IToolHandler handler, ToolExecutionContext context)
        {
            var desc = handler.Description;
            if (context.SpoilerFreeMode)
            {
                desc += "\n[Spoiler Free Mode Active: This tool will return limited information.]";
            }
            return desc;
        }
    }
}
