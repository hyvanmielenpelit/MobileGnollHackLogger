namespace Overseer.Tests.UnitTests;

using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Overseer.Services.Providers;
using Overseer.Services.Tools;
using Xunit;

/// <summary>
/// A <see cref="ToolCategory.Feedback"/> handler is declared only when the request enables the
/// feedback tool and tool use; the allow-list neither offers nor withholds it.
/// </summary>
public class ToolRegistryFeedbackGateTests
{
    // Names with no file in ToolGuides, so the registry keeps the descriptions given here.
    private const string FeedbackTool = "feedback_probe_tool";
    private const string ProductionTool = "production_probe_tool";

    private sealed class ProbeToolHandler : IToolHandler
    {
        public ProbeToolHandler(string toolName, ToolCategory category)
        {
            ToolName = toolName;
            Category = category;
        }

        public string ToolName { get; }
        public string Description { get; set; } = "Probe description.";
        public ToolExecutionLocation ExecutionLocation => ToolExecutionLocation.Server;
        public ToolCategory Category { get; }
        public JsonElement ParameterSchema => JsonDocument.Parse("{}").RootElement;

        public Task<ToolResult> ExecuteAsync(JsonElement parameters, ToolExecutionContext context, CancellationToken cancellationToken)
            => Task.FromResult(new ToolResult { Success = true, Content = "probe result" });
    }

    private static readonly IAiProvider Provider = new OpenAiResponsesProvider(new ConfigurationBuilder().Build());

    private static ToolRegistry CreateRegistry() => new(
        new List<IToolHandler>
        {
            new ProbeToolHandler(FeedbackTool, ToolCategory.Feedback),
            new ProbeToolHandler(ProductionTool, ToolCategory.InformationRetrieval)
        },
        new DummyClientToolBridge(),
        NullLogger<ToolRegistry>.Instance);

    private static List<string> Declared(
        bool enableFeedbackTool, bool enableToolUse = true, bool enableClientTools = false, IEnumerable<string>? allowed = null)
        => CreateRegistry()
            .BuildToolsForRequest(Provider, new ToolExecutionContext(), false, enableToolUse, enableClientTools, false,
                allowedToolNames: allowed, enableFeedbackTool: enableFeedbackTool)
            .FunctionDeclarations
            .Select(d => ProviderHelper.GetProperty(d, "name")!.ToString()!)
            .ToList();

    [Fact]
    public void TheFeedbackTool_IsDeclared_OnlyWithTheFlagAndToolUse()
    {
        Assert.Contains(FeedbackTool, Declared(enableFeedbackTool: true));
        Assert.DoesNotContain(FeedbackTool, Declared(enableFeedbackTool: false));
        Assert.Contains(ProductionTool, Declared(enableFeedbackTool: false));
    }

    [Fact]
    public void AnAllowlistThatDoesNotNameIt_StillDeclaresIt_WithTheFlag()
    {
        var declared = Declared(enableFeedbackTool: true, allowed: new[] { ProductionTool });

        Assert.Equal(new[] { FeedbackTool, ProductionTool }, declared.OrderBy(n => n, System.StringComparer.Ordinal));
    }

    [Fact]
    public void AnAllowlistThatNamesIt_DoesNotDeclareIt_WithoutTheFlag()
    {
        var declared = Declared(enableFeedbackTool: false, allowed: new[] { FeedbackTool, ProductionTool });

        Assert.Equal(ProductionTool, Assert.Single(declared));
    }

    [Fact]
    public void TheFlag_DoesNotDeclareIt_WhenToolUseIsOff()
    {
        Assert.Empty(Declared(enableFeedbackTool: true, enableToolUse: false));

        // With client tools on, the loop runs, and the feedback gate still withholds it.
        Assert.DoesNotContain(FeedbackTool, Declared(enableFeedbackTool: true, enableToolUse: false, enableClientTools: true));
    }
}
