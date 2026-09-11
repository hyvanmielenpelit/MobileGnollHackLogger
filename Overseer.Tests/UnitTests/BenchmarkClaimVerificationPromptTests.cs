namespace Overseer.Tests.UnitTests;

using System.Collections.Generic;
using Overseer.Services.Benchmarking;
using Xunit;

public class BenchmarkClaimVerificationPromptTests
{
    private static string BuildPrompt(bool isDisputedVerdict = false)
    {
        return BenchmarkClaimVerificationPrompt.BuildPrompt(
            "GnollHack Suite",
            1,
            "How does the spell compute its damage?",
            "**REQUIRED** - damage formula.",
            new List<string> { "Claim text." },
            new List<string> { "source_code_search" },
            15,
            isDisputedVerdict: isDisputedVerdict);
    }

    [Fact]
    public void BuildPrompt_StatesThatComputationClaimsAreCheckedInTheImplementingCode()
    {
        string prompt = BuildPrompt();

        Assert.Contains(
            "A table or page that omits a term does not refute a claim that names the term",
            prompt);
    }

    [Fact]
    public void BuildPrompt_Instruction3a_FollowsInstruction3WithoutRenumberingLaterInstructions()
    {
        string prompt = BuildPrompt();

        int index3 = prompt.IndexOf("3. Use the available tools to search the GnollHack codebase and wiki", System.StringComparison.Ordinal);
        int index3a = prompt.IndexOf("3a. A claim about how a spell, attack or effect is computed", System.StringComparison.Ordinal);
        int index4 = prompt.IndexOf("4. Possible verdicts for each claim:", System.StringComparison.Ordinal);

        Assert.True(index3 >= 0, "Instruction 3 must still be present.");
        Assert.True(index3a > index3, "Instruction 3a must follow instruction 3.");
        Assert.True(index4 > index3a, "Instruction 4 must follow 3a, unrenumbered.");
    }

    [Fact]
    public void BuildPrompt_DisputedVerdict_StillCarriesInstruction3a()
    {
        string prompt = BuildPrompt(isDisputedVerdict: true);

        Assert.Contains(
            "A table or page that omits a term does not refute a claim that names the term",
            prompt);
    }
}
