using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Overseer.Services;
using Overseer.Services.Agents;
using Overseer.Services.Tools;
using Xunit;

namespace Overseer.Tests.UnitTests;

public class SubAgentCatalogTests
{
    private class DummyToolHandler : IToolHandler
    {
        public string ToolName { get; init; } = "";
        public string Description { get; set; } = "Dummy";
        public ToolExecutionLocation ExecutionLocation { get; init; } = ToolExecutionLocation.Server;
        public ToolCategory Category { get; init; } = ToolCategory.InformationRetrieval;
        public JsonElement ParameterSchema => JsonDocument.Parse("{}").RootElement;
        public int TimeoutSeconds => 10;

        public Task<ToolResult> ExecuteAsync(JsonElement parameters, ToolExecutionContext context, CancellationToken cancellationToken)
        {
            return Task.FromResult(new ToolResult { Success = true });
        }
    }

    [Fact]
    public void CatalogService_LoadsSeedCatalogSuccessfully()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection().Build();
        var catalogService = new SubAgentCatalogService(config, NullLogger<SubAgentCatalogService>.Instance);

        var agents = catalogService.GetSubAgents();
        Assert.NotEmpty(agents);
        Assert.Contains(agents, a => a.Name == "wiki_researcher");
        Assert.Contains(agents, a => a.Name == "source_investigator");
        Assert.Contains(agents, a => a.Name == "game_data_analyst");
    }

    [Fact]
    public void ModelMetadata_FlashLiteAndNano_DoNotSupportCoordination()
    {
        var metadataService = new ModelMetadataService();

        var nanoMeta = metadataService.GetMetadata("OpenAI", "gpt-5.4-nano");
        Assert.False(nanoMeta.SupportsSubAgentCoordination);
        Assert.True(nanoMeta.SupportsSubAgentExecution);

        var flashLiteMeta = metadataService.GetMetadata("Google", "gemini-3.1-flash-lite");
        Assert.False(flashLiteMeta.SupportsSubAgentCoordination);
        Assert.True(flashLiteMeta.SupportsSubAgentExecution);

        var flashMeta = metadataService.GetMetadata("Google", "gemini-3.5-flash");
        Assert.True(flashMeta.SupportsSubAgentCoordination);
        Assert.True(flashMeta.SupportsSubAgentExecution);

        var gptProMeta = metadataService.GetMetadata("OpenAI", "gpt-5.4-pro");
        Assert.True(gptProMeta.SupportsSubAgentCoordination);
        Assert.True(gptProMeta.SupportsSubAgentExecution);
    }

    [Fact]
    public void SubAgentAvailability_ReturnsFalseForNanoOrFlashLite()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection().Build();
        var catalogService = new SubAgentCatalogService(config, NullLogger<SubAgentCatalogService>.Instance);
        var metadataService = new ModelMetadataService();
        var context = new ToolExecutionContext { AgentDepth = 0, MaxAgentDepth = 1 };

        bool availableForNano = SubAgentAvailability.IsAvailableFor(
            metadataService, "OpenAI", "gpt-5.4-nano", catalogService, context, true, config);
        Assert.False(availableForNano);

        bool availableForFlashLite = SubAgentAvailability.IsAvailableFor(
            metadataService, "Google", "gemini-3.1-flash-lite", catalogService, context, true, config);
        Assert.False(availableForFlashLite);

        bool availableForPro = SubAgentAvailability.IsAvailableFor(
            metadataService, "OpenAI", "gpt-5.4-pro", catalogService, context, true, config);
        Assert.True(availableForPro);

        // At max depth, delegation is prohibited
        var deepContext = new ToolExecutionContext { AgentDepth = 1, MaxAgentDepth = 1 };
        bool availableAtMaxDepth = SubAgentAvailability.IsAvailableFor(
            metadataService, "OpenAI", "gpt-5.4-pro", catalogService, deepContext, true, config);
        Assert.False(availableAtMaxDepth);
    }
}
