namespace Overseer.Tests.UnitTests;

using System.Text.Json;
using MobileGnollHackLogger.Data;
using Overseer.Models;
using Overseer.Services.Benchmarking;
using Xunit;

/// <summary>
/// The candidate prompt switches a launch request carries, as the run records them in
/// <see cref="BenchmarkCandidatePromptOptions"/>: the response style and whether source code
/// references are allowed, which a user's default disallows.
/// </summary>
public class BenchmarkRunLauncherTests
{
    private static BenchmarkCandidatePromptOptions RecordedOptions(StartBenchmarkRunRequest request, bool hasGameSnapshot = false)
    {
        var (verboseMode, allowSourceCodeReferences) = BenchmarkRunLauncher.ResolvePromptSwitches(request);
        return BenchmarkService.CandidatePromptOptionsFor(verboseMode, allowSourceCodeReferences, hasGameSnapshot);
    }

    [Fact]
    public void ANullAllowSourceCodeReferences_IsRecordedAsDisallowed()
    {
        var request = new StartBenchmarkRunRequest { SuiteId = 1, TestedModelConfigurationId = 1, AssessorModelConfigurationId = 2 };

        Assert.Null(request.AllowSourceCodeReferences);
        var options = RecordedOptions(request);

        Assert.False(options.AllowSourceCodeReferences);
        Assert.False(options.VerboseMode);
        Assert.Contains("\"allowSourceCodeReferences\":false", options.ToCanonicalJson());
        Assert.False(BenchmarkCandidatePromptOptions.FromJson(options.ToCanonicalJson()).AllowSourceCodeReferences);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void AnExplicitAllowSourceCodeReferences_IsRecordedAsGiven(bool requested, bool expected)
    {
        var request = new StartBenchmarkRunRequest { AllowSourceCodeReferences = requested, VerboseMode = true };

        var options = RecordedOptions(request, hasGameSnapshot: true);

        Assert.Equal(expected, options.AllowSourceCodeReferences);
        Assert.True(options.VerboseMode);
        Assert.True(options.HasGameSnapshot);
        Assert.Equal(expected, BenchmarkCandidatePromptOptions.FromJson(options.ToCanonicalJson()).AllowSourceCodeReferences);
    }

    [Fact]
    public void RecordedOptionsWithoutTheMember_StillReadAsAllowed_AsTheyWereGraded()
    {
        const string legacy = "{\"verboseMode\":false,\"spoilerFreeMode\":false,\"overseerMode\":0,\"enableToolUse\":true,\"enableWebSearch\":false}";

        Assert.True(BenchmarkCandidatePromptOptions.FromJson(legacy).AllowSourceCodeReferences);
    }

    [Fact]
    public void ASeriesMember_IsLaunchedWithTheSeriesStoredSetting()
    {
        var request = new StartBenchmarkRunRequest
        {
            SuiteId = 1,
            TestedModelConfigurationId = 1,
            AssessorModelConfigurationId = 2,
            RunCount = 3,
            AllowSourceCodeReferences = true
        };
        var series = new BenchmarkRunSeries { StartRequestJson = JsonSerializer.Serialize(request) };

        var stored = BenchmarkSeriesOrchestrator.DeserializeRequest(series);

        Assert.NotNull(stored);
        Assert.True(stored!.AllowSourceCodeReferences);
        Assert.True(RecordedOptions(stored).AllowSourceCodeReferences);

        // A series stored before the field existed ran with references allowed, and its remaining
        // members keep that, so a resume matches member 1's CandidateSystemPromptSha256.
        var legacySeries = new BenchmarkRunSeries { StartRequestJson = "{\"SuiteId\":1,\"TestedModelConfigurationId\":1,\"AssessorModelConfigurationId\":2,\"RunCount\":3}" };
        var legacy = BenchmarkSeriesOrchestrator.DeserializeRequest(legacySeries);
        Assert.NotNull(legacy);
        Assert.True(legacy!.AllowSourceCodeReferences);
        Assert.True(RecordedOptions(legacy).AllowSourceCodeReferences);
    }

    [Fact]
    public void ASeriesStartedWithoutTheSetting_IsStoredDisallowed()
    {
        var request = new StartBenchmarkRunRequest { SuiteId = 1, TestedModelConfigurationId = 1, AssessorModelConfigurationId = 2, RunCount = 3 };

        request.AllowSourceCodeReferences ??= false;
        var series = new BenchmarkRunSeries { StartRequestJson = JsonSerializer.Serialize(request) };

        var stored = BenchmarkSeriesOrchestrator.DeserializeRequest(series);
        Assert.NotNull(stored);
        Assert.False(stored!.AllowSourceCodeReferences);
        Assert.False(RecordedOptions(stored).AllowSourceCodeReferences);
    }
}
