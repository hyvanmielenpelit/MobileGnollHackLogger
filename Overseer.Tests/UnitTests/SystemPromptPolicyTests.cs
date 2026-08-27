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

    [Fact]
    public void ToolGuides_ContainRandomizedAppearanceWarnings()
    {
        // Guide paths relative to test runtime or repo
        var guideDir = Path.Combine(AppContext.BaseDirectory, "ToolGuides");
        if (!Directory.Exists(guideDir))
        {
            // Fallback for direct source inspection in tests
            guideDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Overseer", "ToolGuides"));
        }

        if (Directory.Exists(guideDir))
        {
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
    }
}
