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

    [Fact]
    public void Parse_CriticalErrorQuoteAtIndexZero_EchoedVerbatim_CountsAsSupported()
    {
        // The critical-error quote is submitted as claim 0 and is matched back by the same verbatim
        // echo every other claim uses; nothing about it is special to the parser.
        const string quote = "Praying on an unaligned altar at 1 HP is always safe.";
        var claims = new List<string>
        {
            quote,
            "Master Kaen has AC -2"
        };

        string json = @"```json
{
  ""verifications"": [
    {
      ""claimIndex"": 0,
      ""claim"": ""Praying on an unaligned altar at 1 HP is always safe."",
      ""verdict"": ""Supported"",
      ""citation"": ""src/pray.c:812"",
      ""basis"": ""The prayer path treats an unaligned altar as the player's own here.""
    },
    {
      ""claimIndex"": 1,
      ""claim"": ""Master Kaen has AC -2"",
      ""verdict"": ""Indeterminate"",
      ""citation"": null,
      ""basis"": ""Not located within the tool budget.""
    }
  ]
}
```";

        var result = BenchmarkClaimVerificationParser.Parse(json, claims);

        Assert.True(result.Success);
        Assert.Equal(2, result.Verifications.Count);
        Assert.Equal(1, result.ClaimsSupportedCount);
        Assert.Equal(0, result.MismatchesDropped);

        // Index, text and verdict all survive, which is what lets the harness pick this one
        // verification out of the set as the quote's own.
        Assert.Equal(0, result.Verifications[0].ClaimIndex);
        Assert.Equal(quote, result.Verifications[0].Claim);
        Assert.Equal(BenchmarkClaimVerdict.Supported, result.Verifications[0].Verdict);
        Assert.Equal("src/pray.c:812", result.Verifications[0].Citation);
    }

    [Fact]
    public void Parse_IgnoresARolesMemberInModelOutput()
    {
        var claims = new List<string> { "Gnolls gain infravision at level 1" };
        string json = "{\"verifications\":[{\"claimIndex\":0,\"claim\":\"Gnolls gain infravision at level 1\",\"verdict\":\"Supported\",\"citation\":\"src/role.c:45\",\"basis\":\"b\",\"roles\":[\"accusedQuote\"]}]}";

        var result = BenchmarkClaimVerificationParser.Parse(json, claims);

        Assert.True(result.Success);
        Assert.Null(result.Verifications[0].Roles);
    }

    [Fact]
    public void StoredJsonWithoutRoles_StillReads()
    {
        const string stored = "[{\"claimIndex\":0,\"claim\":\"c\",\"verdict\":\"Refuted\",\"citation\":\"src/a.c\",\"basis\":\"b\"}]";

        var verifications = System.Text.Json.JsonSerializer.Deserialize<List<BenchmarkClaimVerification>>(stored)!;

        Assert.Equal(BenchmarkClaimVerdict.Refuted, verifications[0].Verdict);
        Assert.Null(verifications[0].Roles);
        Assert.True(BenchmarkClaimRoles.IsOrdinaryClaim(verifications[0]));
        Assert.Null(verifications[0].ChargedPart);
        Assert.Null(verifications[0].ItemVerdict);
    }

    private const string ChargedSentence = "Studying a spellbook takes 6 turns, and dig is a matter spell.";

    [Fact]
    public void Parse_AChargedItem_IsReadFromItsChargedPartVerdict_AndKeepsTheItemsOwnVerdict()
    {
        var claims = new List<string> { ChargedSentence };
        string json = "{\"verifications\":[{\"claimIndex\":0,\"claim\":\"" + ChargedSentence + "\","
            + "\"verdict\":\"Supported\",\"citation\":\"src/spell.c:640\",\"basis\":\"Dig is a matter spell.\","
            + "\"chargedPartVerdict\":\"Refuted\",\"chargedPartBasis\":\"The delay is set per book in src/objects.c:3560, not 6 for every book.\"}]}";

        var result = BenchmarkClaimVerificationParser.Parse(json, claims, new[] { true });

        Assert.True(result.Success);
        var v = Assert.Single(result.Verifications);
        Assert.Equal(BenchmarkClaimVerdict.Refuted, v.Verdict);
        Assert.Equal(BenchmarkClaimVerdict.Refuted, v.EffectiveVerdict);
        Assert.Equal("The delay is set per book in src/objects.c:3560, not 6 for every book.", v.Basis);
        Assert.Equal("The delay is set per book in src/objects.c:3560, not 6 for every book.", v.Citation);
        Assert.Null(v.CitationNote);
        Assert.True(v.ChargedPart);
        Assert.Equal("Supported", v.ItemVerdict);
        Assert.Equal("src/spell.c:640", v.ItemCitation);
        Assert.Equal("Dig is a matter spell.", v.ItemBasis);
        Assert.Equal(0, result.ClaimsSupportedCount);
        Assert.Equal(1, result.ClaimsRefutedCount);
    }

    [Theory]
    [InlineData("")]
    [InlineData(",\"chargedPartVerdict\":\"Probably false\"")]
    [InlineData(",\"chargedPartVerdict\":null")]
    public void Parse_AChargedItemWithoutAChargedPartVerdict_IsIndeterminateWithTheNote(string chargedPartField)
    {
        var claims = new List<string> { ChargedSentence };
        string json = "{\"verifications\":[{\"claimIndex\":0,\"claim\":\"" + ChargedSentence + "\","
            + "\"verdict\":\"Supported\",\"citation\":\"src/spell.c:640\",\"basis\":\"Dig is a matter spell.\""
            + chargedPartField + "}]}";

        var result = BenchmarkClaimVerificationParser.Parse(json, claims, new[] { true });

        var v = Assert.Single(result.Verifications);
        Assert.Equal(BenchmarkClaimVerificationParser.ChargedPartNotJudgedNote, v.CitationNote);
        Assert.Equal("the charged part was not judged separately", v.CitationNote);
        Assert.Equal(BenchmarkClaimVerdict.Indeterminate, v.EffectiveVerdict);
        Assert.Equal(BenchmarkClaimVerdict.Supported, v.Verdict);
        Assert.Equal("Supported", v.ItemVerdict);
        Assert.True(v.ChargedPart);
        Assert.Equal(0, result.ClaimsSupportedCount);
        Assert.Equal(1, result.ClaimsIndeterminateCount);
    }

    [Fact]
    public void Parse_AChargedPartVerdictWhoseBasisCitesNothing_IsDemotedToIndeterminate()
    {
        var claims = new List<string> { ChargedSentence };
        string json = "{\"verifications\":[{\"claimIndex\":0,\"claim\":\"" + ChargedSentence + "\","
            + "\"verdict\":\"Supported\",\"citation\":\"src/spell.c:640\",\"basis\":\"b\","
            + "\"chargedPartVerdict\":\"Refuted\",\"chargedPartBasis\":\"Books differ in delay.\"}]}";

        var result = BenchmarkClaimVerificationParser.Parse(json, claims, new[] { true });

        var v = Assert.Single(result.Verifications);
        Assert.Equal(BenchmarkClaimVerdict.Indeterminate, v.Verdict);
        Assert.Null(v.Citation);
        Assert.Contains("missing citation", v.Basis);
        Assert.Equal(1, result.CitationsMissingDemoted);
    }

    [Fact]
    public void Parse_AnUnchargedItem_IgnoresAChargedPartVerdict()
    {
        var claims = new List<string> { ChargedSentence };
        string json = "{\"verifications\":[{\"claimIndex\":0,\"claim\":\"" + ChargedSentence + "\","
            + "\"verdict\":\"Supported\",\"citation\":\"src/spell.c:640\",\"basis\":\"b\","
            + "\"chargedPartVerdict\":\"Refuted\",\"chargedPartBasis\":\"src/objects.c:3560\"}]}";

        var withoutFlags = BenchmarkClaimVerificationParser.Parse(json, claims);
        var withFalseFlag = BenchmarkClaimVerificationParser.Parse(json, claims, new[] { false });

        foreach (var result in new[] { withoutFlags, withFalseFlag })
        {
            var v = Assert.Single(result.Verifications);
            Assert.Equal(BenchmarkClaimVerdict.Supported, v.EffectiveVerdict);
            Assert.Equal("src/spell.c:640", v.Citation);
            Assert.Null(v.ChargedPart);
            Assert.Null(v.ItemVerdict);
            Assert.Null(v.CitationNote);
        }
    }

    [Fact]
    public void AChargedItem_RoundTripsThroughStoredJson()
    {
        var claims = new List<string> { ChargedSentence };
        string json = "{\"verifications\":[{\"claimIndex\":0,\"claim\":\"" + ChargedSentence + "\","
            + "\"verdict\":\"Supported\",\"citation\":\"src/spell.c:640\",\"basis\":\"b\","
            + "\"chargedPartVerdict\":\"Refuted\",\"chargedPartBasis\":\"src/objects.c:3560 sets it per book.\"}]}";
        var parsed = Assert.Single(BenchmarkClaimVerificationParser.Parse(json, claims, new[] { true }).Verifications);

        string stored = System.Text.Json.JsonSerializer.Serialize(new[] { parsed });
        var read = Assert.Single(System.Text.Json.JsonSerializer.Deserialize<List<BenchmarkClaimVerification>>(stored)!);

        Assert.Equal(BenchmarkClaimVerdict.Refuted, read.EffectiveVerdict);
        Assert.True(read.ChargedPart);
        Assert.Equal("Supported", read.ItemVerdict);
        Assert.Equal("src/spell.c:640", read.ItemCitation);
        Assert.Contains("\"itemVerdict\":\"Supported\"", stored);
    }
}
