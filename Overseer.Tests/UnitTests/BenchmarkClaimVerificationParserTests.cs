namespace Overseer.Tests.UnitTests;

using System.Collections.Generic;
using Overseer.Services.Benchmarking;
using Xunit;

public class BenchmarkClaimVerificationParserTests
{
    [Fact]
    public void Parse_HappyPath_ThreeClaims_CountsOneEach()
    {
        var claims = new List<string>
        {
            "Gnolls gain infravision at level 1",
            "Master Kaen has AC -5",
            "Amulet of Yendor weighs 20"
        };

        string json = @"```json
[
  {
    ""claimIndex"": 0,
    ""claim"": ""Gnolls gain infravision at level 1"",
    ""verdict"": ""Supported"",
    ""citation"": ""src/role.c:45"",
    ""basis"": ""Confirmed from role definitions.""
  },
  {
    ""claimIndex"": 1,
    ""claim"": ""Master Kaen has AC -5"",
    ""verdict"": ""Refuted"",
    ""citation"": ""src/monst.c:120"",
    ""basis"": ""Master Kaen has AC -2 in GnollHack.""
  },
  {
    ""claimIndex"": 2,
    ""claim"": ""Amulet of Yendor weighs 20"",
    ""verdict"": ""Indeterminate"",
    ""citation"": null,
    ""basis"": ""Could not verify weight in source files.""
  }
]
```";

        var result = BenchmarkClaimVerificationParser.Parse(json, claims);

        Assert.True(result.Success);
        Assert.Equal(3, result.Verifications.Count);
        Assert.Equal(1, result.ClaimsSupportedCount);
        Assert.Equal(1, result.ClaimsRefutedCount);
        Assert.Equal(1, result.ClaimsIndeterminateCount);
        Assert.Equal(0, result.CitationsMissingDemoted);
        Assert.Equal(0, result.MismatchesDropped);

        Assert.Equal(BenchmarkClaimVerdict.Supported, result.Verifications[0].Verdict);
        Assert.Equal("src/role.c:45", result.Verifications[0].Citation);

        Assert.Equal(BenchmarkClaimVerdict.Refuted, result.Verifications[1].Verdict);
        Assert.Equal("src/monst.c:120", result.Verifications[1].Citation);

        Assert.Equal(BenchmarkClaimVerdict.Indeterminate, result.Verifications[2].Verdict);
    }

    [Fact]
    public void Parse_CitationDemotion_SupportedWithBlankCitation_DemotedToIndeterminate()
    {
        var claims = new List<string> { "Gnolls can eat bones" };

        string json = @"[
  {
    ""claimIndex"": 0,
    ""claim"": ""Gnolls can eat bones"",
    ""verdict"": ""Supported"",
    ""citation"": ""   "",
    ""basis"": ""Gnolls possess bone eating trait.""
  }
]";

        var result = BenchmarkClaimVerificationParser.Parse(json, claims);

        Assert.True(result.Success);
        Assert.Single(result.Verifications);
        Assert.Equal(0, result.ClaimsSupportedCount);
        Assert.Equal(1, result.ClaimsIndeterminateCount);
        Assert.Equal(1, result.CitationsMissingDemoted);
        Assert.Equal(BenchmarkClaimVerdict.Indeterminate, result.Verifications[0].Verdict);
        Assert.Contains("demoted to Indeterminate", result.Verifications[0].Basis);
    }

    [Fact]
    public void Parse_EchoMismatch_ParaphrasedEcho_DroppedAndFallsBackToIndeterminate()
    {
        var claims = new List<string> { "Gnolls gain infravision at experience level 1" };

        string json = @"```json
[
  {
    ""claimIndex"": 0,
    ""claim"": ""Infravision is gained by gnolls at level one."",
    ""verdict"": ""Supported"",
    ""citation"": ""src/role.c:50"",
    ""basis"": ""Confirmed.""
  }
]
```";

        var result = BenchmarkClaimVerificationParser.Parse(json, claims);

        Assert.True(result.Success);
        Assert.Single(result.Verifications);
        Assert.Equal(1, result.MismatchesDropped);
        Assert.Equal(0, result.ClaimsSupportedCount);
        Assert.Equal(1, result.ClaimsIndeterminateCount);
        Assert.Equal(BenchmarkClaimVerdict.Indeterminate, result.Verifications[0].Verdict);
        Assert.Contains("absent from verifier response", result.Verifications[0].Basis);
    }

    [Fact]
    public void Parse_CountersSum_OmittedClaimsDefaultToIndeterminate_SumEqualsClaimCount()
    {
        var claims = new List<string>
        {
            "Claim one",
            "Claim two",
            "Claim three",
            "Claim four"
        };

        string json = @"```json
[
  {
    ""claimIndex"": 0,
    ""claim"": ""Claim one"",
    ""verdict"": ""Supported"",
    ""citation"": ""src/role.c:10"",
    ""basis"": ""Found in source.""
  }
]
```";

        var result = BenchmarkClaimVerificationParser.Parse(json, claims);

        Assert.True(result.Success);
        Assert.Equal(4, result.Verifications.Count);
        Assert.Equal(1, result.ClaimsSupportedCount);
        Assert.Equal(0, result.ClaimsRefutedCount);
        Assert.Equal(3, result.ClaimsIndeterminateCount);

        Assert.Equal(claims.Count,
            result.ClaimsSupportedCount + result.ClaimsRefutedCount + result.ClaimsIndeterminateCount);

        for (int i = 1; i <= 3; i++)
        {
            Assert.Equal(BenchmarkClaimVerdict.Indeterminate, result.Verifications[i].Verdict);
            Assert.Equal(claims[i], result.Verifications[i].Claim);
            Assert.Contains("absent from verifier response", result.Verifications[i].Basis);
        }
    }
}
