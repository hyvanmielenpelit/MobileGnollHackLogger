namespace Overseer.Services.Agents;

using Microsoft.Extensions.Configuration;
using Overseer.Services.Tools;

public static class SubAgentAvailability
{
    public static bool IsAvailableFor(
        ModelMetadataService metadataService,
        string provider,
        string? modelId,
        SubAgentCatalogService catalogService,
        ToolExecutionContext context,
        bool enableToolUse,
        IConfiguration configuration)
    {
        bool subAgentsEnabled = configuration.GetValue<bool>("SubAgentSettings:Enabled", true);
        if (!subAgentsEnabled)
        {
            return false;
        }

        if (!context.EnableSubAgents)
        {
            return false;
        }

        if (!enableToolUse)
        {
            return false;
        }

        if (context.AgentDepth >= context.MaxAgentDepth)
        {
            return false;
        }

        var enabledAgents = catalogService.GetEnabledSubAgents();
        if (enabledAgents.Count == 0)
        {
            return false;
        }

        if (string.IsNullOrEmpty(modelId))
        {
            return true;
        }

        var meta = metadataService.GetMetadata(provider, modelId);
        return meta.SupportsSubAgentCoordination;
    }
}
