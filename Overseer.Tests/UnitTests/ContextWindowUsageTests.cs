using Overseer.Services.Agents;
using Xunit;

namespace Overseer.Tests.UnitTests;

/// <summary>
/// Covers the persistence gate that <c>ChatService</c> applies to the context-window columns
/// through <see cref="AgentRunResult.ReportedContextPromptTokens"/> and
/// <see cref="AgentRunResult.ReportedContextOutputTokens"/>: the token columns are written only
/// when the provider actually reported usage, so a turn with no usage report leaves the indicator
/// absent rather than showing a fabricated estimate.
/// </summary>
public class ContextWindowUsageTests
{
    [Fact]
    public void TurnWithUsage_ReportsThePromptAndOutputTokens()
    {
        var runResult = new AgentRunResult { LastPromptTokens = 204400, LastOutputTokens = 1200 };

        Assert.Equal(204400, runResult.ReportedContextPromptTokens);
        Assert.Equal(1200, runResult.ReportedContextOutputTokens);
    }

    [Fact]
    public void TurnWithoutUsage_LeavesBothTokenColumnsNull()
    {
        // No provider usage report at all: LastPromptTokens stays at its default of zero.
        var runResult = new AgentRunResult();

        Assert.Null(runResult.ReportedContextPromptTokens);
        Assert.Null(runResult.ReportedContextOutputTokens);
    }

    [Fact]
    public void ZeroOutputWithNonZeroPrompt_IsStoredAsZeroNotNull()
    {
        // An empty reply is a real measurement, not a missing one.
        var runResult = new AgentRunResult { LastPromptTokens = 5000, LastOutputTokens = 0 };

        Assert.Equal(5000, runResult.ReportedContextPromptTokens);
        Assert.Equal(0, runResult.ReportedContextOutputTokens);
    }
}
