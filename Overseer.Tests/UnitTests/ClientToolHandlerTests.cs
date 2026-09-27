using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Overseer.Services.Providers;
using Overseer.Services.Tools;
using Xunit;

namespace Overseer.Tests.UnitTests;

public class ClientToolHandlerTests
{
    private const string PerformanceReportsFallbackDescription = "Retrieve in-game performance test reports from the client device.";

    [Fact]
    public void GetPerformanceReportsTool_HasExpectedShape()
    {
        var tool = new GetPerformanceReportsTool();

        Assert.Equal("get_performance_reports", tool.ToolName);
        Assert.Equal(ToolExecutionLocation.Client, tool.ExecutionLocation);
        Assert.Equal(ToolCategory.ClientPersistentDataQuery, tool.Category);
        Assert.Equal(15, tool.TimeoutSeconds);
        Assert.Equal(16000, tool.MaxResultLengthOverride);

        var properties = tool.ParameterSchema.GetProperty("properties");
        Assert.Equal("string", properties.GetProperty("filename").GetProperty("type").GetString());
        Assert.Equal("integer", properties.GetProperty("max_length").GetProperty("type").GetString());
        Assert.False(tool.ParameterSchema.TryGetProperty("required", out _));
    }

    [Fact]
    public void GetPerformanceReportsTool_CapLeavesRoomForTheClientTruncationMarker()
    {
        // The client cuts a report to max_length and then APPENDS this marker, so a
        // truncated read arrives longer than max_length. If the server's cap does not
        // cover both, ToolExecutor replaces the client's marker with its own.
        const int documentedMax = 15000;
        const string marker = "\n...[truncated: 15000 of 1000000 characters; call again with a larger max_length]";

        var tool = new GetPerformanceReportsTool();

        Assert.NotNull(tool.MaxResultLengthOverride);
        Assert.True(tool.MaxResultLengthOverride >= documentedMax + marker.Length,
            $"get_performance_reports cap {tool.MaxResultLengthOverride} must cover the documented "
            + $"{documentedMax}-char max_length plus the client's {marker.Length}-char truncation marker.");

        string? maxLengthDescription = tool.ParameterSchema.GetProperty("properties")
            .GetProperty("max_length").GetProperty("description").GetString();
        Assert.Contains("15000", maxLengthDescription);
    }

    [Fact]
    public void GetPerformanceReportsTool_OfferedInGnollHackSessionsWithClientTools()
    {
        var tool = new GetPerformanceReportsTool();
        var registry = new ToolRegistry(
            new List<IToolHandler> { tool },
            new DummyClientToolBridge(),
            NullLogger<ToolRegistry>.Instance);
        var provider = new OpenAiResponsesProvider(new ConfigurationBuilder().Build());

        List<string?> DeclaredNames(ToolExecutionContext context, bool enableClientTools) =>
            registry.BuildToolsForRequest(provider, context, false, false, enableClientTools, false)
                .FunctionDeclarations
                .Select(decl => ProviderHelper.GetProperty(decl, "name")?.ToString())
                .ToList();

        // IsGameOn false: offered from the main screen, which proves the persistent category
        var gnollHackSession = new ToolExecutionContext { IsGnollHackSession = true, IsGameOn = false };
        Assert.Contains("get_performance_reports", DeclaredNames(gnollHackSession, true));

        var otherSession = new ToolExecutionContext { IsGnollHackSession = false, IsGameOn = false };
        Assert.Empty(DeclaredNames(otherSession, true));

        Assert.Empty(DeclaredNames(gnollHackSession, false));

        // The registry replaced the fallback with ToolGuides/get_performance_reports.md,
        // so the guide reaches the output directory under the tool's own name.
        Assert.NotEqual(PerformanceReportsFallbackDescription, tool.Description);
    }
}
