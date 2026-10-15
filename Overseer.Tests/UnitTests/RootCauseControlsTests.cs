namespace Overseer.Tests.UnitTests;

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MobileGnollHackLogger.Data;
using Overseer.Services.Benchmarking.RootCause;
using Overseer.Tests.Helpers;
using Xunit;

/// <summary>
/// Control selection for a run-completion Root Cause Analysis (§ A9): a run of another model on the same
/// exam under the same instrument qualifies; another item revision, another assessor, another scoring
/// method, the same model or a withheld index does not; at most three are kept, one per other provider first.
/// </summary>
public class RootCauseControlsTests
{
    private const long Suite = 71;
    private static readonly int[] Difficulties = { 50, 50, 50, 50, 50, 50 };
    private static readonly int[] Subject = { 60, 70, 40, 65, 75, 35 };
    private static readonly int[] Strong = { 85, 90, 45, 88, 92, 40 };

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly ApplicationDbContext _db = BenchmarkBatteryTestData.NewDb();

    private static BenchmarkRun Run(long id, int[] scores, string modelId = "gpt-5.6-luna", string provider = "OpenAI",
        Action<BenchmarkRun>? adjust = null, int? qualityIndex = 70, int? terminalFailures = null)
    {
        var run = BenchmarkBatteryTestData.Run(id, Suite, scores, Difficulties, modelId, qualityIndex: qualityIndex, terminalFailures: terminalFailures);
        if (provider != "OpenAI")
        {
            run.TestedModelSnapshot = BenchmarkModelSnapshots.Model(provider: provider, modelId: modelId, displayName: modelId, thinkingLevel: "high", maxOutputTokens: 32000);
        }
        adjust?.Invoke(run);
        return run;
    }

    private async Task SeedAsync(params BenchmarkRun[] runs)
    {
        _db.BenchmarkRuns.AddRange(runs);
        await _db.SaveChangesAsync(Ct);
    }

    [Fact]
    public async Task ARunOfAnotherModel_OnTheSameExamAndGraders_Qualifies()
    {
        await SeedAsync(Run(1, Subject), Run(2, Strong, "claude-opus-5", "Anthropic"));

        var set = await RootCauseControls.FindForRunAsync(_db, 1, Ct);

        var control = Assert.Single(set.Controls);
        Assert.Equal(2, control.RunId);
        Assert.Equal("Anthropic", control.Provider);
        Assert.Equal(6, control.SharedQuestions);
        Assert.Null(set.NoneReason);
        Assert.Equal("OpenAI", set.SubjectProvider);
        Assert.Equal(6, Assert.Single(set.Scores).ScoresByItem.Count);
    }

    [Fact]
    public async Task NearMisses_AreRejected()
    {
        await SeedAsync(
            Run(1, Subject),
            Run(3, Strong),                                                                                    // the same model
            Run(4, Strong, "claude-opus-5", "Anthropic", r => { foreach (var a in r.Answers) a.ItemRevisionUsed = 2; }),
            Run(5, Strong, "gemini-4-argon", "Google", r => r.AssessorModelSnapshot = BenchmarkModelSnapshots.Model(
                provider: "Anthropic", modelId: "claude-judge", displayName: "Claude Judge")),
            Run(6, Strong, "nova-flash", "Northwind", r => r.ScoringMethodVersion = 8),
            Run(7, Strong, "vega-pro", "Southstar", qualityIndex: null, terminalFailures: 1));

        var set = await RootCauseControls.FindForRunAsync(_db, 1, Ct);

        Assert.Empty(set.Controls);
        Assert.Equal(RootCauseControls.NoRunControlReason, set.NoneReason);
    }

    [Fact]
    public async Task AtMostThreeAreKept_OnePerOtherProviderFirst()
    {
        await SeedAsync(
            Run(1, Subject),
            Run(10, Strong, "claude-opus-5", "Anthropic"),
            Run(11, Strong, "claude-sonnet-5", "Anthropic"),
            Run(12, Strong, "claude-haiku-5", "Anthropic"),
            Run(13, Strong, "gpt-6-astra"),
            Run(14, Strong, "gemini-4-argon", "Google"));

        var set = await RootCauseControls.FindForRunAsync(_db, 1, Ct);

        Assert.Equal(RootCauseControls.MaxControls, set.Controls.Count);
        var providers = set.Controls.Select(c => c.Provider).ToList();
        Assert.Contains("Anthropic", providers);
        Assert.Contains("Google", providers);
        Assert.Equal(set.Controls.Count, set.Controls.Select(c => c.RunId).Distinct().Count());
        Assert.Equal(Enumerable.Range(1, set.Controls.Count), set.Controls.Select(c => c.Number));
    }

    [Fact]
    public async Task FindAsync_ResolvesARunSubjectKey_AndRefusesOtherKeys()
    {
        await SeedAsync(Run(1, Subject), Run(2, Strong, "claude-opus-5", "Anthropic"));

        var bySubject = await RootCauseControls.FindAsync(_db, "run:1", Ct);
        Assert.Equal(2, Assert.Single(bySubject.Controls).RunId);

        var group = await RootCauseControls.FindAsync(_db, "group:4", Ct);
        Assert.Empty(group.Controls);
        Assert.NotNull(group.NoneReason);
    }

    [Fact]
    public async Task AMissingRun_HasNoControls_WithAReason()
    {
        var set = await RootCauseControls.FindForRunAsync(_db, 404, Ct);

        Assert.Empty(set.Controls);
        Assert.NotNull(set.NoneReason);
    }
}
