namespace Overseer.Tests.UnitTests;

using System.Collections.Generic;
using MobileGnollHackLogger.Data;
using Overseer.Services.Benchmarking;
using Xunit;

public class BenchmarkChatTransferTests
{
    [Theory]
    [InlineData("source_code_search", BenchmarkToolFamily.SourceCode)]
    [InlineData("source_code_view", BenchmarkToolFamily.SourceCode)]
    [InlineData("search_definitions", BenchmarkToolFamily.SourceCode)]
    [InlineData("get_function_definition", BenchmarkToolFamily.SourceCode)]
    [InlineData("get_constants", BenchmarkToolFamily.SourceCode)]
    [InlineData("list_indexed_files", BenchmarkToolFamily.SourceCode)]
    [InlineData("wiki_search", BenchmarkToolFamily.Wiki)]
    [InlineData("wiki_view", BenchmarkToolFamily.Wiki)]
    [InlineData("nethack_wiki_search", BenchmarkToolFamily.Wiki)]
    [InlineData("nethack_wiki_view", BenchmarkToolFamily.Wiki)]
    [InlineData("monster_lookup", BenchmarkToolFamily.StructuredLookup)]
    [InlineData("item_lookup", BenchmarkToolFamily.StructuredLookup)]
    [InlineData("get_monster_stats", BenchmarkToolFamily.StructuredLookup)]
    [InlineData("get_item_stats", BenchmarkToolFamily.StructuredLookup)]
    [InlineData("get_knowledge_article", BenchmarkToolFamily.KnowledgeBase)]
    [InlineData("custom_calculator", BenchmarkToolFamily.Other)]
    [InlineData("web_search", BenchmarkToolFamily.Other)]
    public void ClassifyTool_MapsKnownAndUnknownTools(string toolName, BenchmarkToolFamily expectedFamily)
    {
        Assert.Equal(expectedFamily, BenchmarkChatTransfer.ClassifyTool(toolName));
    }

    [Fact]
    public void AggregateToolCounts_CalculatesRun11ToolDistributionAccurately()
    {
        // Simulate a run with 208 source, 77 wiki, 7 lookup, 1 knowledge base = 293 total
        var answers = new List<BenchmarkRunAnswer>
        {
            new BenchmarkRunAnswer
            {
                OrderIndex = 1,
                ToolCallSummary = "source_code_search×150, source_code_view×58, wiki_search×50, wiki_view×27, monster_lookup×5, item_lookup×2, get_knowledge_article×1"
            },
            new BenchmarkRunAnswer
            {
                OrderIndex = 2,
                ToolCallSummary = null // no tool calls
            }
        };

        var profile = BenchmarkChatTransfer.AnalyzeToolRouting(answers);

        Assert.Equal(293, profile.TotalCalls);
        Assert.Equal(208, profile.FamilyCalls[BenchmarkToolFamily.SourceCode]);
        Assert.Equal(77, profile.FamilyCalls[BenchmarkToolFamily.Wiki]);
        Assert.Equal(7, profile.FamilyCalls[BenchmarkToolFamily.StructuredLookup]);
        Assert.Equal(1, profile.FamilyCalls[BenchmarkToolFamily.KnowledgeBase]);
        Assert.Equal(0, profile.FamilyCalls[BenchmarkToolFamily.Other]);

        // One answer made a KB call, one did not -> 1 answer with 0 KB calls
        Assert.Equal(1, profile.ZeroKnowledgeBaseAnswerCount);
    }

    [Fact]
    public void ComputePearsonCorrelation_HandlesPerfectAndDegenerateCases()
    {
        var xPositive = new double[] { 1, 2, 3, 4, 5 };
        var yPositive = new double[] { 2, 4, 6, 8, 10 };
        var rPos = BenchmarkChatTransfer.ComputePearsonCorrelation(xPositive, yPositive);
        Assert.NotNull(rPos);
        Assert.InRange(rPos.Value, 0.999, 1.001);

        var yNegative = new double[] { 10, 8, 6, 4, 2 };
        var rNeg = BenchmarkChatTransfer.ComputePearsonCorrelation(xPositive, yNegative);
        Assert.NotNull(rNeg);
        Assert.InRange(rNeg.Value, -1.001, -0.999);

        // Constant values -> standard deviation is 0 -> null
        var yConstant = new double[] { 5, 5, 5, 5, 5 };
        Assert.Null(BenchmarkChatTransfer.ComputePearsonCorrelation(xPositive, yConstant));

        // Less than 2 pairs -> null
        Assert.Null(BenchmarkChatTransfer.ComputePearsonCorrelation(new double[] { 1 }, new double[] { 2 }));
    }

    [Fact]
    public void HasResponseStyleConflict_TestsCriteriaCorrectly()
    {
        // Run 11 case: verbose=false, Completeness 83.0, Accuracy 97.7, Completeness is lowest dimension, gap = 14.7 >= 13
        Assert.True(BenchmarkChatTransfer.HasResponseStyleConflict(
            verboseMode: false,
            accuracyAverage: 97.7,
            completenessAverage: 83.0,
            concisenessAverage: 95.0,
            readabilityAverage: 95.0));

        // When verbose=true: false
        Assert.False(BenchmarkChatTransfer.HasResponseStyleConflict(
            verboseMode: true,
            accuracyAverage: 97.7,
            completenessAverage: 83.0,
            concisenessAverage: 95.0,
            readabilityAverage: 95.0));

        // When gap is under 13: false (e.g. 97.7 - 85.0 = 12.7 < 13.0)
        Assert.False(BenchmarkChatTransfer.HasResponseStyleConflict(
            verboseMode: false,
            accuracyAverage: 97.7,
            completenessAverage: 85.0,
            concisenessAverage: 95.0,
            readabilityAverage: 95.0));

        // When completeness is not the lowest: false (e.g. conciseness is 80.0)
        Assert.False(BenchmarkChatTransfer.HasResponseStyleConflict(
            verboseMode: false,
            accuracyAverage: 97.7,
            completenessAverage: 83.0,
            concisenessAverage: 80.0,
            readabilityAverage: 95.0));

        // When any average is null: false
        Assert.False(BenchmarkChatTransfer.HasResponseStyleConflict(
            verboseMode: false,
            accuracyAverage: null,
            completenessAverage: 83.0,
            concisenessAverage: 95.0,
            readabilityAverage: 95.0));
    }
}
