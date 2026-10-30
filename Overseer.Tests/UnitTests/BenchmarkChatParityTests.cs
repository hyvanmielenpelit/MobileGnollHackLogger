using System.Text.Json;
using Overseer.Services.Benchmarking;
using Xunit;

namespace Overseer.Tests.UnitTests;

/// <summary>
/// GnollBench candidates run under the chat's limits and turn guidance, read from the chat's own keys. The
/// chat's configuration and the code are compared directly, so the two cannot drift apart unnoticed.
/// </summary>
public class BenchmarkChatParityTests
{
    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "MobileGnollHackLogger.slnx")))
        {
            dir = dir.Parent;
        }

        Assert.True(dir != null, "Repository root not found above " + AppContext.BaseDirectory);
        return dir!.FullName;
    }

    private static string Source(params string[] relativePath)
        => File.ReadAllText(Path.Combine(new[] { RepositoryRoot() }.Concat(relativePath).ToArray()));

    private static JsonElement AppSettings()
    {
        using var document = JsonDocument.Parse(
            Source("Overseer", "appsettings.json"),
            new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        return document.RootElement.Clone();
    }

    private static int Occurrences(string text, string term)
    {
        int count = 0;
        for (int at = text.IndexOf(term, StringComparison.Ordinal); at >= 0; at = text.IndexOf(term, at + term.Length, StringComparison.Ordinal))
        {
            count++;
        }
        return count;
    }

    [Fact]
    public void TheCandidatesCaps_AreTheChatsDefaults()
    {
        var settings = AppSettings();
        var performance = settings.GetProperty("AiPerformanceSettings");

        Assert.Equal(performance.GetProperty("MaxCallsPerSession").GetProperty("Default").GetInt32(), BenchmarkService.DefaultToolCallBudget());
        Assert.Equal(performance.GetProperty("MaxToolIterations").GetProperty("Default").GetInt32(), BenchmarkService.DefaultToolIterations());
        Assert.Equal(performance.GetProperty("ChatRequestTimeout").GetProperty("Default").GetInt32(), BenchmarkService.DefaultQuestionTimeoutSeconds());
        Assert.Equal(settings.GetProperty("SubAgentSettings").GetProperty("MaxTotalModelCalls").GetInt32(), BenchmarkService.DefaultTotalModelCalls());
    }

    [Theory]
    [InlineData("ToolCallBudget")]
    [InlineData("ToolIterations")]
    [InlineData("TotalModelCalls")]
    [InlineData("QuestionTimeoutSeconds")]
    public void NoBenchmarkCapKey_IsLeftInConfiguration(string key)
    {
        Assert.False(AppSettings().GetProperty("Benchmark").TryGetProperty(key, out _));
    }

    [Theory]
    [InlineData(BenchmarkInformationCondition.Production, true)]
    [InlineData(BenchmarkInformationCondition.SourceOnly, false)]
    [InlineData(BenchmarkInformationCondition.ClosedBook, false)]
    public void WikiContext_IsOnUnderProductionAlone(BenchmarkInformationCondition condition, bool expected)
    {
        var options = BenchmarkService.CandidatePromptOptionsFor(false, true, false, condition);

        Assert.Equal(expected, options.HasWikiContext);
    }

    /// <summary>
    /// Web search stays off under every condition, a deliberate divergence from the chat's default: a run's
    /// answers must stay reproducible from the local corpora. A later "alignment" must not undo it silently.
    /// </summary>
    [Theory]
    [InlineData(BenchmarkInformationCondition.Production)]
    [InlineData(BenchmarkInformationCondition.SourceOnly)]
    [InlineData(BenchmarkInformationCondition.ClosedBook)]
    public void WebSearch_IsOffUnderEveryCondition(BenchmarkInformationCondition condition)
    {
        Assert.False(BenchmarkService.CandidatePromptOptionsFor(false, true, false, condition).EnableWebSearch);
    }

    [Fact]
    public void WebSearchAndSubAgents_AreOffAtBothCandidateRequests()
    {
        string source = Source("Overseer", "Services", "Benchmarking", "BenchmarkService.cs");

        Assert.True(Occurrences(source, "EnableWebSearch = false") >= 2);
        Assert.True(Occurrences(source, "EnableSubAgents = false") >= 2);
        Assert.DoesNotContain("EnableWebSearch = true", source);
        Assert.False(new MobileGnollHackLogger.Data.UserAiSettings().EnableSubAgents);
    }

    [Fact]
    public void TurnGuidance_IsSetByTheChatAndBothCandidateRequests_AndReportingByTheChatAlone()
    {
        string benchmark = Source("Overseer", "Services", "Benchmarking", "BenchmarkService.cs");
        string chat = Source("Overseer", "Services", "ChatService.cs");
        string subAgent = Source("Overseer", "Services", "Tools", "DelegateToSubAgentTool.cs");

        Assert.Equal(2, Occurrences(benchmark, "ApplyTurnGuidance = true"));
        Assert.Equal(1, Occurrences(chat, "ApplyTurnGuidance = true"));
        Assert.Equal(1, Occurrences(subAgent, "ApplyTurnGuidance = true"));

        Assert.Equal(1, Occurrences(chat, "ReportTurnLimits = true"));
        Assert.Equal(0, Occurrences(benchmark, "ReportTurnLimits"));
        Assert.Equal(0, Occurrences(subAgent, "ReportTurnLimits"));
    }

    [Fact]
    public void WikiContextArticleList_NamesEachArticlesPath()
    {
        var articles = new[] { "--- Monsters/Gnoll.md ---\nA gnoll.", "--- Items/Lamp.md ---\nA lamp." };

        Assert.Equal("Monsters/Gnoll.md;Items/Lamp.md", BenchmarkService.WikiContextArticleList(articles));
        Assert.Null(BenchmarkService.WikiContextArticleList(Array.Empty<string>()));
    }

    [Fact]
    public void WikiContextArticleList_KeepsWholeEntriesWithinTheColumn()
    {
        var articles = Enumerable.Range(0, 100).Select(i => $"--- Dir/{new string('a', 50)}{i}.md ---\nText").ToList();

        string list = BenchmarkService.WikiContextArticleList(articles)!;

        Assert.True(list.Length <= 1000);
        Assert.All(list.Split(';'), entry => Assert.EndsWith(".md", entry));
    }
}
