namespace Overseer.Services.Agents;

using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Overseer.Services.Tools;

public class SubAgentCatalogService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<SubAgentCatalogService> _logger;
    private readonly List<SubAgentDefinition> _agents = new();
    private bool _isLoaded;

    public SubAgentCatalogService(
        IConfiguration configuration,
        ILogger<SubAgentCatalogService> logger)
    {
        _configuration = configuration;
        _logger = logger;
        LoadCatalog();
    }

    public virtual IReadOnlyList<SubAgentDefinition> GetSubAgents() => _agents.AsReadOnly();

    public virtual IReadOnlyList<SubAgentDefinition> GetEnabledSubAgents() =>
        _agents.Where(a => a.IsEnabled).ToList().AsReadOnly();

    public virtual SubAgentDefinition? GetSubAgent(string name) =>
        _agents.FirstOrDefault(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase));

    private void LoadCatalog()
    {
        if (_isLoaded) return;
        _isLoaded = true;

        var catalogPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "SubAgentCatalog.json");
        if (!File.Exists(catalogPath))
        {
            _logger.LogWarning("[SubAgentCatalog] Warning: Catalog file absent at {Path}. Running in single-agent mode.", catalogPath);
            return;
        }

        try
        {
            var json = File.ReadAllText(catalogPath);
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };
            var items = JsonSerializer.Deserialize<List<SubAgentDefinition>>(json, options);
            if (items != null)
            {
                _agents.AddRange(items);
                var names = string.Join(", ", _agents.Select(a => a.Name));
                _logger.LogInformation("[SubAgentCatalog] Loading catalog from {Path}. Found {Count} agent definitions: [{Names}]", catalogPath, _agents.Count, names);
            }
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "[SubAgentCatalog] Malformed JSON in catalog file at {Path}", catalogPath);
            throw new InvalidOperationException($"Malformed SubAgentCatalog.json at {catalogPath}: {ex.Message}", ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[SubAgentCatalog] Error reading catalog file at {Path}", catalogPath);
            throw;
        }
    }

    public void Validate(IEnumerable<IToolHandler> handlers, ModelMetadataService modelMetadata)
    {
        if (_agents.Count == 0)
        {
            return;
        }

        var handlerDict = handlers.ToDictionary(h => h.ToolName, h => h, StringComparer.OrdinalIgnoreCase);
        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var agent in _agents)
        {
            if (string.IsNullOrWhiteSpace(agent.Name))
            {
                throw new InvalidOperationException("Subagent definition is missing a required 'name'.");
            }

            if (!seenNames.Add(agent.Name))
            {
                throw new InvalidOperationException($"Duplicate subagent name '{agent.Name}' in catalog.");
            }

            if (string.IsNullOrWhiteSpace(agent.Description))
            {
                throw new InvalidOperationException($"Subagent '{agent.Name}' has an empty description.");
            }

            if (string.IsNullOrWhiteSpace(agent.Instructions))
            {
                throw new InvalidOperationException($"Subagent '{agent.Name}' has empty instructions.");
            }

            foreach (var toolName in agent.AllowedTools)
            {
                if (string.Equals(toolName, "delegate_to_subagent", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException($"Subagent '{agent.Name}' cannot have 'delegate_to_subagent' in AllowedTools (depth recursion prohibited).");
                }

                if (!handlerDict.TryGetValue(toolName, out var handler))
                {
                    throw new InvalidOperationException($"Subagent '{agent.Name}' references unregistered tool '{toolName}'.");
                }

                if (handler.ExecutionLocation == ToolExecutionLocation.Client)
                {
                    throw new InvalidOperationException($"Subagent '{agent.Name}' cannot have client-side tool '{toolName}' in AllowedTools.");
                }

                if (handler.Category == ToolCategory.GameAction ||
                    handler.Category == ToolCategory.ClientActiveSessionQuery ||
                    handler.Category == ToolCategory.ClientPersistentDataQuery)
                {
                    throw new InvalidOperationException($"Subagent '{agent.Name}' cannot have tool '{toolName}' with category {handler.Category}.");
                }
            }

            if (agent.ModelPreference != null)
            {
                if (string.IsNullOrWhiteSpace(agent.ModelPreference.Provider) || string.IsNullOrWhiteSpace(agent.ModelPreference.ModelId))
                {
                    throw new InvalidOperationException($"Subagent '{agent.Name}' has an invalid ModelPreference (must specify both provider and modelId).");
                }

                var meta = modelMetadata.GetMetadata(agent.ModelPreference.Provider, agent.ModelPreference.ModelId);
                if (!meta.SupportsSubAgentExecution)
                {
                    throw new InvalidOperationException($"Subagent '{agent.Name}' requests model '{agent.ModelPreference.ModelId}' on provider '{agent.ModelPreference.Provider}' which does not support subagent execution.");
                }
            }
        }

        int enabledCount = _agents.Count(a => a.IsEnabled);
        int disabledCount = _agents.Count(a => !a.IsEnabled);
        _logger.LogInformation("[SubAgentCatalog] Validation successful. Enabled agents: {EnabledCount}, disabled agents: {DisabledCount}. All tools and model capabilities verified.", enabledCount, disabledCount);
    }
}
