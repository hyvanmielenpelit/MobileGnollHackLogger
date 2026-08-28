using System.IO;
using Overseer.Services;
using Xunit;

namespace Overseer.Tests.UnitTests;

public class SystemPromptPolicyTests
{
    [Fact]
    public void BuildRandomizedAppearancePolicy_ContainsRequiredDirectives()
    {
        string policy = ChatService.BuildRandomizedAppearancePolicy();

        Assert.Contains("Randomized Item Appearances & Anti-Hallucination Policy", policy);
        Assert.Contains("shuffle_all() in src/o_init.c", policy);
        Assert.Contains("HARD PROHIBITION", policy);
        Assert.Contains("Whole classes reshuffled", policy);
        Assert.Contains("amulets, potions, scrolls, spellbooks, venoms", policy);
        Assert.Contains("helmets, gloves, shirts, cloaks, boots, staves", policy);
        Assert.Contains("wands, rings, robes", policy);
        Assert.Contains("NOT reshuffled: magic swords", policy);
        Assert.Contains("potion of water and every potion type after it", policy);
        Assert.Contains("src/objects.c", policy);
    }

    private static string GetGuideDirectory()
    {
        var guideDir = Path.Combine(AppContext.BaseDirectory, "ToolGuides");
        if (!Directory.Exists(guideDir))
        {
            guideDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Overseer", "ToolGuides"));
        }

        Assert.True(Directory.Exists(guideDir), $"ToolGuides directory not found at {guideDir}");
        return guideDir;
    }

    [Fact]
    public void ToolGuides_ContainRandomizedAppearanceWarnings()
    {
        var guideDir = GetGuideDirectory();

        string policyText = File.ReadAllText(Path.Combine(guideDir, "_policy.md"));
        Assert.Contains("shuffle_all", policyText);

        string getItemStatsText = File.ReadAllText(Path.Combine(guideDir, "get_item_stats.md"));
        Assert.Contains("randomized per game", getItemStatsText);

        string itemLookupText = File.ReadAllText(Path.Combine(guideDir, "item_lookup.md"));
        Assert.Contains("pre-shuffle", itemLookupText);

        string sourceViewText = File.ReadAllText(Path.Combine(guideDir, "source_code_view.md"));
        Assert.Contains("shuffle_all", sourceViewText);

        string sourceSearchText = File.ReadAllText(Path.Combine(guideDir, "source_code_search.md"));
        Assert.Contains("shuffle_all", sourceSearchText);
    }

    [Fact]
    public void ToolGuides_ContainToolBatchingAndAccuracyPolicy()
    {
        var guideDir = GetGuideDirectory();
        string policyText = File.ReadAllText(Path.Combine(guideDir, "_policy.md"));

        // Section headings at ## level
        Assert.Contains("## Tool Batching and Parallel Execution", policyText);
        Assert.Contains("## Accuracy About Tool Use", policyText);

        // Core rules
        Assert.Contains("**Independent**", policyText);
        Assert.Contains("**Dependent**", policyText);
        Assert.Contains("Do not batch speculative calls", policyText);
        Assert.Contains("Dependent lookups stay sequential", policyText);
        Assert.Contains("Do calculations after the data arrives", policyText);
        Assert.Contains("Web search is not part of a batch", policyText);

        // Truncation markers
        Assert.Contains("(truncated: batch output budget reached)", policyText);
        Assert.Contains("(skipped: batch output budget reached)", policyText);
        Assert.Contains("[Result truncated for length]", policyText);

        // Example anchors
        Assert.Contains("do_eat()", policyText);
        Assert.Contains("three unrelated monsters", policyText);
        Assert.Contains("two different knowledge base articles", policyText);
        Assert.Contains("wiki_view", policyText);
        Assert.Contains("get_function_definition", policyText);

        // Accuracy & negative assertion against vendor-specific wrapper
        Assert.Contains("accuracy, not verbosity", policyText);
        Assert.DoesNotContain("multi_tool_use", policyText);
    }

    [Fact]
    public void ChatService_DoesNotHardcodeToolConcurrencySection()
    {
        var chatServicePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Overseer", "Services", "ChatService.cs"));
        Assert.True(File.Exists(chatServicePath), $"ChatService.cs not found at {chatServicePath}");

        string source = File.ReadAllText(chatServicePath);
        Assert.DoesNotContain("Tool Concurrency & Batching", source);
    }
}
