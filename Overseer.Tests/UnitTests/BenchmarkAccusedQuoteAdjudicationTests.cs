namespace Overseer.Tests.UnitTests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MobileGnollHackLogger.Data;
using Overseer.Services;
using Overseer.Services.Benchmarking;
using Xunit;

/// <summary>
/// The sentences of the answer the assessor quoted when it docked Accuracy: how they are found, how
/// they join the verifier's submission with harness-owned roles, and how every consumer reads those
/// roles — or, on a legacy record without them, the exact-text rules it was always read by.
/// </summary>
public class BenchmarkAccusedQuoteAdjudicationTests
{
    private const string AnswerText =
        "The Grail of Healing is a spelltool.\n\n"
        + "**Using it:**\n"
        + "- Applying it heals **1000 hit points** and restores 500 mana.\n"
        + "- It has no charges and never runs out.\n\n"
        + "Gnolls regenerate hit points faster at night than other races do.";

    private static string Evidence(params string[] quotes)
        => "Accuracy held at 4. " + string.Join(" ", quotes.Select(q => $"The answer says \"{q}\", which is wrong."));

    // --- Extraction -------------------------------------------------------------------------

    [Fact]
    public void Extract_ReturnsTheAnswersOwnSpan_IgnoringEmphasisAndWhitespace()
    {
        // The assessor dropped the bold markers and re-wrapped the sentence.
        var quotes = BenchmarkService.ExtractAccusedQuotes(
            AnswerText, Evidence("Applying it heals 1000   hit points and restores 500 mana."));

        var quote = Assert.Single(quotes);
        Assert.Equal("Applying it heals **1000 hit points** and restores 500 mana.", quote.Text);
    }

    [Fact]
    public void Extract_DropsAQuotationThatIsNotInTheAnswer()
    {
        // A rubric or board quotation the assessor compared the answer with.
        var quotes = BenchmarkService.ExtractAccusedQuotes(
            AnswerText, "The rubric states \"the grail heals 2000 hit points when invoked\" instead.");

        Assert.Empty(quotes);
    }

    [Fact]
    public void Extract_IgnoresAnUnmatchedQuote()
    {
        var quotes = BenchmarkService.ExtractAccusedQuotes(
            AnswerText, "The answer says \"It has no charges and never runs out. That is wrong.");

        Assert.Empty(quotes);
    }

    [Fact]
    public void Extract_ReadsTypographicQuotes_AndANestedStraightQuote()
    {
        string evidence = "The answer claims “It has no charges and never runs out.” "
            + "and later “the answer's \"Gnolls regenerate hit points faster at night\" line” is invented.";

        var quotes = BenchmarkService.ExtractAccusedQuotes(AnswerText, evidence);

        Assert.Contains(quotes, q => q.Text == "It has no charges and never runs out.");
        // The fragment is submitted as the sentence it came from, and kept beside it.
        var widened = Assert.Single(quotes, q => q.Text == "Gnolls regenerate hit points faster at night than other races do.");
        Assert.Equal(new[] { "Gnolls regenerate hit points faster at night" }, widened.QuotedFragments);
        Assert.Equal(2, quotes.Count);
    }

    [Fact]
    public void Extract_DropsSpansOutsideTheLengthBounds()
    {
        string tooShort = "a spelltool";
        string tooLong = new string('x', BenchmarkService.AccusedQuoteMaxLength + 1);

        Assert.Empty(BenchmarkService.ExtractAccusedQuotes(AnswerText + " " + tooLong, Evidence(tooShort, tooLong)));
    }

    [Fact]
    public void Extract_CapsAtThree_LongestFirst_TiesInEvidenceOrder()
    {
        string answer = "Alpha beta gamma delta one. Alpha beta gamma delta two. Alpha beta gamma delta three. "
            + "A much longer sentence the assessor also quoted here.";

        var quotes = BenchmarkService.ExtractAccusedQuotes(answer, Evidence(
            "Alpha beta gamma delta one.",
            "Alpha beta gamma delta two.",
            "Alpha beta gamma delta three.",
            "A much longer sentence the assessor also quoted here."));

        Assert.Equal(BenchmarkService.MaxAccusedQuotesPerAnswer, quotes.Count);
        Assert.Equal("A much longer sentence the assessor also quoted here.", quotes[0].Text);
        Assert.Equal("Alpha beta gamma delta three.", quotes[1].Text);
        Assert.Equal("Alpha beta gamma delta one.", quotes[2].Text);
    }

    [Fact]
    public void Extract_CarriesTheListHeadingAndThePrecedingSentence()
    {
        var quote = Assert.Single(BenchmarkService.ExtractAccusedQuotes(
            AnswerText, Evidence("It has no charges and never runs out.")));

        Assert.NotNull(quote.Context);
        Assert.Contains("Under \"Using it\"", quote.Context);
        Assert.Contains("Applying it heals", quote.Context);
    }

    // --- Sentence widening and the charge ---------------------------------------------------

    private const string Run55Q17Answer =
        "As a vegan Monk you have to plan your food.\n\n"
        + "**Food:**\n"
        + "- Keep a healthy supply of vegan food such as fortune cookies, and candy bars, since as a vegan Monk you will not eat corpses.\n"
        + "- Pray when Weak.";

    private const string Run55Q17Evidence =
        "Accuracy 4. The answer recommends a \"healthy supply of vegan food\" such as fortune \"cookies, and candy\", "
        + "but fortune cookies and candy bars are not vegan in GnollHack.";

    [Fact]
    public void Extract_Run55Q17_TwoFragmentsOfOneSentence_AreOneSubmission_WithTheCharge()
    {
        var quote = Assert.Single(BenchmarkService.ExtractAccusedQuotes(Run55Q17Answer, Run55Q17Evidence));

        Assert.Equal(
            "Keep a healthy supply of vegan food such as fortune cookies, and candy bars, since as a vegan Monk you will not eat corpses.",
            quote.Text);
        Assert.Contains("healthy supply of vegan food", quote.Text);
        Assert.Contains("cookies, and candy", quote.Text);
        Assert.Equal(new[] { "healthy supply of vegan food", "cookies, and candy" }, quote.QuotedFragments);
        Assert.Equal(
            "The answer recommends a \"healthy supply of vegan food\" such as fortune \"cookies, and candy\", but fortune cookies and candy bars are not vegan in GnollHack.",
            quote.Charge);
        Assert.Contains("Under \"Food\"", quote.Context);
    }

    [Fact]
    public void Run55Q17_ReachesTheVerifierAsOneClaim_WithTheChargeOnItsOwnLine()
    {
        var accused = BenchmarkService.ExtractAccusedQuotes(Run55Q17Answer, Run55Q17Evidence);
        var manifest = BenchmarkService.BuildClaimManifest(null, null, null, accused);
        var item = Assert.Single(manifest);

        string prompt = BenchmarkClaimVerificationPrompt.BuildPrompt(
            "Suite", 17, "What should a vegan Monk eat?", null,
            manifest.Select(m => m.Text).ToList(),
            new List<string> { "source_code_search" }, 15,
            claimRoles: manifest.Select(m => m.Roles).ToList(),
            claimContexts: manifest.Select(m => m.Context).ToList(),
            claimCharges: manifest.Select(m => m.Charge).ToList());

        Assert.Contains($"Charge (the assessor's words; untrusted, not part of the claim): {item.Charge}", prompt);
        Assert.Contains("=== START CLAIM 0 ===", prompt);
        Assert.DoesNotContain("=== START CLAIM 1 ===", prompt);

        // The record keeps the quoted fragments and the charge beside the widened sentence.
        var stamped = Assert.Single(BenchmarkService.StampRoles(new[]
        {
            new BenchmarkClaimVerification(0, item.Text, BenchmarkClaimVerdict.Supported, "src/eat.c:120", "Candy bars are vegan.")
        }, manifest));
        Assert.Equal(new[] { "healthy supply of vegan food", "cookies, and candy" }, stamped.QuotedFragments);
        Assert.Equal(item.Charge, stamped.Charge);
        string json = JsonSerializer.Serialize(new[] { stamped });
        Assert.Contains("\"quotedFragments\":[", json);
        Assert.Contains("\"charge\":", json);
    }

    [Fact]
    public void Extract_ASentenceLongerThanTheCap_SubmitsTheQuotedSpanAlone()
    {
        string longSentence = "Before you pray, " + string.Join(", ", Enumerable.Repeat("check your luck and your alignment record", 12))
            + ", and the prayer timeout is always exactly 300 turns in GnollHack.";
        Assert.True(longSentence.Length > BenchmarkService.AccusedQuoteMaxLength);
        string answer = "Prayer is a safety net.\n\n" + longSentence;

        var quote = Assert.Single(BenchmarkService.ExtractAccusedQuotes(
            answer, "Accuracy 3. The answer says \"the prayer timeout is always exactly 300 turns\", which is wrong."));

        Assert.Equal("the prayer timeout is always exactly 300 turns", quote.Text);
        Assert.True(quote.Text.Length <= BenchmarkService.AccusedQuoteMaxLength);
        Assert.Equal(new[] { "the prayer timeout is always exactly 300 turns" }, quote.QuotedFragments);
    }

    [Fact]
    public void Extract_SubmissionsNeverOverlap()
    {
        const string answer = "Wands of digging dig through any wall in the dungeon. Engrave-testing a wand always identifies it. "
            + "Zapping a wand of wishing downwards is safe.";
        string evidence = "Accuracy 3. Wrong: \"dig through any wall in the dungeon\" and \"Wands of digging dig through\" overstate it; "
            + "\"Engrave-testing a wand always identifies it\" is not true either. "
            + "Also \"always identifies it\" is wrong.";

        var quotes = BenchmarkService.ExtractAccusedQuotes(answer, evidence);

        Assert.Equal(2, quotes.Count);
        var ranges = quotes.Select(q => (Start: answer.IndexOf(q.Text, StringComparison.Ordinal), q.Text.Length)).ToList();
        Assert.All(ranges, r => Assert.True(r.Start >= 0));
        for (int i = 0; i < ranges.Count; i++)
        {
            for (int j = i + 1; j < ranges.Count; j++)
            {
                bool overlap = ranges[i].Start < ranges[j].Start + ranges[j].Length && ranges[j].Start < ranges[i].Start + ranges[i].Length;
                Assert.False(overlap, $"\"{quotes[i].Text}\" overlaps \"{quotes[j].Text}\".");
            }
        }

        var digging = Assert.Single(quotes, q => q.Text == "Wands of digging dig through any wall in the dungeon.");
        Assert.Equal(2, digging.QuotedFragments!.Count);
        var engrave = Assert.Single(quotes, q => q.Text == "Engrave-testing a wand always identifies it.");
        Assert.Equal(2, engrave.QuotedFragments!.Count);
    }

    // --- Single quotes and clause polarity -----------------------------------------------------

    [Fact]
    public void Extract_Run54Q7_SkipsTheSentenceTheEvidenceApproves_AndKeepsTheOneItCharges()
    {
        const string answer =
            "An uncursed scroll of identify reveals 2 items (and a blessed one reveals 3), so read it first.\n\n"
            + "Soft glass scratches/crushes differently than real gems, so rub the gray stones before you sell them.";
        const string evidence =
            "Correct on the core mechanic (\"an uncursed scroll of identify reveals 2 items (and a blessed one reveals 3)\"). "
            + "Imprecision: \"Soft glass scratches/crushes differently than real gems\" implies a hardness test the game does not model.";

        var quote = Assert.Single(BenchmarkService.ExtractAccusedQuotes(answer, evidence));

        Assert.Equal("Soft glass scratches/crushes differently than real gems, so rub the gray stones before you sell them.", quote.Text);
        Assert.Equal(new[] { "Soft glass scratches/crushes differently than real gems" }, quote.QuotedFragments);
    }

    [Fact]
    public void Extract_Run54Q9Shape_ReadsAStraightSingleQuotedGraveMessage()
    {
        const string answer = "The headstone reads Here lies Fred, killed by a jackal. That makes this a bones level.";
        const string evidence = "The answer says the grave reads 'Here lies Fred, killed by a jackal.' which is not what the board shows.";

        var quote = Assert.Single(BenchmarkService.ExtractAccusedQuotes(answer, evidence));

        Assert.Equal("The headstone reads Here lies Fred, killed by a jackal.", quote.Text);
        Assert.Equal(new[] { "Here lies Fred, killed by a jackal." }, quote.QuotedFragments);
    }

    [Fact]
    public void Extract_ReadsATypographicSingleQuotedSpan()
    {
        var quote = Assert.Single(BenchmarkService.ExtractAccusedQuotes(
            AnswerText, "Accuracy held at 4: ‘It has no charges and never runs out.’ is wrong."));

        Assert.Equal("It has no charges and never runs out.", quote.Text);
    }

    [Fact]
    public void Extract_AnApostropheHeavyEvidenceString_YieldsNothingSpurious()
    {
        const string answer = "It's the dwarves' pick-axes, not the gnomes' wands, that dig; rock 'n' roll aside, "
            + "the '90s-era wiki's claim doesn't apply to GnollHack's Sokoban.";
        const string evidence = "It's the dwarves' pick-axes, not the gnomes' wands, that dig; rock 'n' roll aside, "
            + "the '90s-era wiki's claim doesn't apply to GnollHack's Sokoban — that's wrong, and the answer's framing isn't the rubric's.";

        Assert.Empty(BenchmarkService.ExtractAccusedQuotes(answer, evidence));
    }

    [Fact]
    public void Extract_AClauseWithBothMarkerKinds_IsKept()
    {
        const string answer = "Sacrificing a fresh corpse can grant a gift, even a same-race one.";
        const string evidence = "Correct that \"Sacrificing a fresh corpse can grant a gift\", but not for a same-race corpse.";

        var quote = Assert.Single(BenchmarkService.ExtractAccusedQuotes(answer, evidence));

        Assert.Equal("Sacrificing a fresh corpse can grant a gift, even a same-race one.", quote.Text);
    }

    [Theory]
    [InlineData("\"Elbereth scares most monsters away\" matches the rubric.")]
    [InlineData("The answer rightly says \"Elbereth scares most monsters away\"; the rest is thin.")]
    [InlineData("Wrong about prayer (though \"Elbereth scares most monsters away\" is correct).")]
    public void Extract_AClauseThatOnlyApproves_SkipsTheSpan(string evidence)
    {
        const string answer = "Elbereth scares most monsters away. Praying at 1 HP always works.";

        Assert.Empty(BenchmarkService.ExtractAccusedQuotes(answer, evidence));
    }

    [Theory]
    [InlineData("Correct overall (but \"Praying at 1 HP always works\" is wrong).")]
    [InlineData("Accuracy held at 4. The answer says \"Praying at 1 HP always works\".")]
    public void Extract_ACorrectOuterClause_DoesNotApproveAChargedParenthetical_OrAnUnmarkedQuote(string evidence)
    {
        const string answer = "Elbereth scares most monsters away. Praying at 1 HP always works.";

        var quote = Assert.Single(BenchmarkService.ExtractAccusedQuotes(answer, evidence));

        Assert.Equal("Praying at 1 HP always works.", quote.Text);
    }

    [Fact]
    public void Extract_Run56_AnApprovedQuoteInsideAParentheticalListOfClauses_IsSkipped()
    {
        const string answer =
            "Items from the grave lie on the floor, not buried. Digging up a grave as a lawful character costs alignment, "
            + "and you see 'You have violated the sanctity of this grave!' when you do. Bones gear is often cursed, "
            + "so test them with your Holy Grail before wearing anything.";
        const string evidence =
            "Core claims match the rubric (items lie on the floor, not buried; lawful alignment loss and the exact "
            + "'You have violated the sanctity of this grave!' message; bones gear often cursed). The suggestion to "
            + "'test them with your Holy Grail' is an imprecise/unsupported BUC-testing method, keeping this below 6.";

        var quote = Assert.Single(BenchmarkService.ExtractAccusedQuotes(answer, evidence));

        Assert.Equal(new[] { "test them with your Holy Grail" }, quote.QuotedFragments);
    }

    [Fact]
    public void Extract_AChargeInsideTheSpansOwnClauseOfAParentheticalList_StillWins()
    {
        const string answer = "Items lie on the floor. You see 'You have violated the sanctity of this grave!' when digging.";
        const string evidence =
            "Core claims match the rubric (items lie on the floor; the exact 'You have violated the sanctity of this grave!' "
            + "message is wrong; bones gear often cursed).";

        var quote = Assert.Single(BenchmarkService.ExtractAccusedQuotes(answer, evidence));

        Assert.Equal(new[] { "You have violated the sanctity of this grave!" }, quote.QuotedFragments);
    }

    // --- A docked Suspected-false sentence (run 56, Q18) -------------------------------------

    private const string Run56Q18Answer =
        "* **Sulfurous ash:** Used for offensive fire spells (*Fireball*, *Flame Burst*). You don't have fire spells yet, "
        + "but at 0.1 lbs, you can safely tuck it inside your oriental silk sack for later.\n\n"
        + "Ore nuggets are crafting materials used by the **Blacksmith** inside a **Smithy**. "
        + "They can also be sold for gold (copper: 100gp, silver: 150gp, iron: 50gp).";

    private const string Run56Q18Evidence =
        "The answer names \"Flame Burst\" as a sulfurous-ash fire spell; the rubric's fire spells are flame strike and fireball. "
        + "Otherwise the reagent-to-spell mappings it gives (ginseng/minor healing, garlic/protection from lycanthropy, "
        + "sporal powder/create food) match the board.";

    private static readonly string[] Run56Q18UnverifiedClaims =
    {
        "Suspected false: Used for offensive fire spells (*Fireball*, *Flame Burst*). — the fire spell taking sulfurous ash is flame strike; \"Flame Burst\" does not appear to be a GnollHack spell name.",
        "Suspected false: They can also be sold for gold (copper: 100gp, silver: 150gp, iron: 50gp). — specific per-nugget prices not supported by the board or rubric and look invented."
    };

    [Fact]
    public void DocksSuspectedFalse_Run56Q18_AShortQuotedSpellNameInsideASuspectedSentence_IsDetected()
    {
        Assert.True(BenchmarkService.DocksSuspectedFalse(Run56Q18Answer, Run56Q18Evidence, Run56Q18UnverifiedClaims, 4));
    }

    [Fact]
    public void DocksSuspectedFalse_AtAccuracySix_IsFalse()
    {
        Assert.False(BenchmarkService.DocksSuspectedFalse(Run56Q18Answer, Run56Q18Evidence, Run56Q18UnverifiedClaims, 6));
        Assert.False(BenchmarkService.DocksSuspectedFalse(Run56Q18Answer, Run56Q18Evidence, Run56Q18UnverifiedClaims, null));
    }

    [Fact]
    public void DocksSuspectedFalse_AnApprovedQuote_OrNoSuspectedEntry_IsFalse()
    {
        Assert.False(BenchmarkService.DocksSuspectedFalse(Run56Q18Answer, "Correct: \"Flame Burst\" is right.", Run56Q18UnverifiedClaims, 4));
        Assert.False(BenchmarkService.DocksSuspectedFalse(Run56Q18Answer, Run56Q18Evidence, new[] { "Ore nuggets are crafting materials." }, 4));
        Assert.False(BenchmarkService.DocksSuspectedFalse(Run56Q18Answer, "Minor naming slip; ore prices are unsupported.", Run56Q18UnverifiedClaims, 4));
    }

    private static BenchmarkRunAnswer Run56Q18Graded() => new()
    {
        AnswerText = Run56Q18Answer,
        AccuracyLevel = 4,
        AssessmentEvidenceJson = JsonSerializer.Serialize(new { accuracy = Run56Q18Evidence })
    };

    private static BenchmarkClaimVerification FlameBurstVerdict(BenchmarkClaimVerdict verdict) =>
        new(0, "Used for offensive fire spells (*Fireball*, *Flame Burst*).", verdict, "src/spell.c:120", "Flame burst is a spell.")
        {
            Roles = new[] { BenchmarkClaimRoles.UnverifiedClaim },
            SuspectedFalse = true
        };

    [Fact]
    public void ASupportedDockedSuspicion_ContestsTheAccuracyDeduction()
    {
        var answer = Run56Q18Graded();

        BenchmarkService.ApplyClaimVerificationOutcome(answer, new[] { FlameBurstVerdict(BenchmarkClaimVerdict.Supported) }, false, null);

        Assert.NotEqual(0, answer.AnswerFlags & (int)BenchmarkAnswerFlags.ContestedAccuracyDeduction);
    }

    [Fact]
    public void ARefutedDockedSuspicion_ContestsNothing()
    {
        var answer = Run56Q18Graded();

        BenchmarkService.ApplyClaimVerificationOutcome(answer, new[] { FlameBurstVerdict(BenchmarkClaimVerdict.Refuted) }, false, null);

        Assert.Equal(0, answer.AnswerFlags & (int)BenchmarkAnswerFlags.ContestedAccuracyDeduction);
    }

    [Theory]
    [InlineData("the answer is correct here", true)]
    [InlineData("consistent with the source", true)]
    [InlineData("correct but overstated", false)]
    [InlineData("this implies more than it says", false)]
    [InlineData("the answer says", null)]
    [InlineData("a notable claim", null)]
    public void AccusationClausePolarity_ChargeWins_WholeWordsOnly(string clause, bool? expected)
    {
        Assert.Equal(expected, BenchmarkVerdictConsistency.AccusationClauseApproves(clause));
    }

    [Theory]
    [InlineData(4, BenchmarkAnswerFlags.None, true)]
    [InlineData(2, BenchmarkAnswerFlags.None, true)]
    [InlineData(5, BenchmarkAnswerFlags.None, true)]
    [InlineData(6, BenchmarkAnswerFlags.None, false)]
    [InlineData(6, BenchmarkAnswerFlags.ContestedVerdict, true)]
    public void Eligible_AtAccuracyFiveOrBelow_OrOnAContestedVerdict(int accuracy, BenchmarkAnswerFlags flags, bool expected)
    {
        var answer = new BenchmarkRunAnswer { AccuracyLevel = accuracy, AnswerFlags = (int)flags };
        Assert.Equal(expected, BenchmarkService.IsAccusedQuoteEligible(answer));
    }

    [Theory]
    [InlineData(true, 1)]
    [InlineData(false, 0)]
    public void TheToggle_TurnsAccusedQuotesOffEntirely(bool enabled, int expected)
    {
        var service = CreateService(new Dictionary<string, string?>
        {
            ["Benchmark:ClaimVerification:AccusedQuotesEnabled"] = enabled ? "true" : "false"
        });
        var answer = new BenchmarkRunAnswer
        {
            AnswerText = AnswerText,
            AccuracyLevel = 4,
            AssessmentEvidenceJson = JsonSerializer.Serialize(new { accuracy = Evidence("It has no charges and never runs out.") })
        };

        Assert.Equal(expected, service.AccusedQuotesFor(answer).Count);
        Assert.Equal(enabled, service.NeedsClaimVerificationOrAccusation(answer));
    }

    // --- The manifest and its roles ---------------------------------------------------------

    [Fact]
    public void Manifest_AnAccusedSpanEqualToAnUnverifiedClaim_IsOneItemWithTwoRoles_CountedOnceAsOrdinary()
    {
        const string claim = "It has no charges and never runs out.";
        var manifest = BenchmarkService.BuildClaimManifest(
            new[] { claim, "Gnolls regenerate hit points faster at night than other races do." },
            criticalErrorQuote: null,
            outOfRubricBasis: null,
            new[] { new BenchmarkService.AccusedQuote(claim, "Under \"Using it\".") });

        Assert.Equal(2, manifest.Count);
        Assert.Equal(new[] { BenchmarkClaimRoles.UnverifiedClaim, BenchmarkClaimRoles.AccusedQuote }, manifest[0].Roles);
        Assert.Equal("Under \"Using it\".", manifest[0].Context);

        var stamped = BenchmarkService.StampRoles(new[]
        {
            new BenchmarkClaimVerification(0, claim, BenchmarkClaimVerdict.Refuted, "src/objects.c:2889", "It has charges."),
            new BenchmarkClaimVerification(1, manifest[1].Text, BenchmarkClaimVerdict.Supported, "src/attrib.c", "True.")
        }, manifest);

        var ordinary = stamped.Where(BenchmarkClaimRoles.IsOrdinaryClaim).ToList();
        Assert.Equal(2, ordinary.Count);
        Assert.Single(ordinary, v => v.Verdict == BenchmarkClaimVerdict.Refuted);
    }

    [Fact]
    public void Manifest_KeepsTheQuoteAndBasisOrder_AndAppendsAccusedQuotes()
    {
        const string quote = "Applying it heals **1000 hit points** and restores 500 mana.";
        const string basis = "the grail heals 2000 hit points.";
        var manifest = BenchmarkService.BuildClaimManifest(
            new[] { "Gnolls regenerate hit points faster at night than other races do." },
            quote,
            basis,
            new[]
            {
                new BenchmarkService.AccusedQuote(quote, null),
                new BenchmarkService.AccusedQuote("It has no charges and never runs out.", "ctx")
            });

        Assert.Equal(new[] { quote, basis, "Gnolls regenerate hit points faster at night than other races do.", "It has no charges and never runs out." },
            manifest.Select(m => m.Text));
        // The critical-error quote keeps its role and only gains the accused one.
        Assert.Equal(new[] { BenchmarkClaimRoles.CriticalErrorQuote, BenchmarkClaimRoles.AccusedQuote }, manifest[0].Roles);
        Assert.Equal(new[] { BenchmarkClaimRoles.OutOfRubricBasis }, manifest[1].Roles);
        Assert.Equal(new[] { BenchmarkClaimRoles.UnverifiedClaim }, manifest[2].Roles);
        Assert.Equal(new[] { BenchmarkClaimRoles.AccusedQuote }, manifest[3].Roles);
    }

    [Fact]
    public void Manifest_ACriticalErrorQuoteEqualToAnUnverifiedClaim_TakesItsPlace_AsTheTextRuleAlwaysRead()
    {
        const string quote = "It has no charges and never runs out.";
        var manifest = BenchmarkService.BuildClaimManifest(new[] { quote }, quote, null, null);

        var item = Assert.Single(manifest);
        Assert.Equal(new[] { BenchmarkClaimRoles.CriticalErrorQuote }, item.Roles);
    }

    // --- De-duplication of unverified claims ------------------------------------------------

    [Fact]
    public void Manifest_APlainDuplicateOfASuspectedFalseClaim_IsDroppedInFavorOfThePrefixedOne()
    {
        const string sentence = "The Grail heals 500 mana when invoked.";
        var manifest = BenchmarkService.BuildClaimManifest(
            new[] { sentence, $"Suspected false: {sentence} — the rubric gives 250" },
            criticalErrorQuote: null,
            outOfRubricBasis: null,
            accusedQuotes: null,
            answerText: sentence);

        var item = Assert.Single(manifest);
        Assert.Equal(sentence, item.Text);
        Assert.True(item.SuspectedFalse);
        Assert.Equal("the rubric gives 250", item.Suspicion);
    }

    [Fact]
    public void DeduplicateUnverifiedClaims_APlainDuplicateThatComesFirst_IsReplacedByTheSuspectedFalseOne()
    {
        const string entry = "Suspected false: Candy bars restore 100 nutrition each. — the source gives 100 only for a fresh bar.";
        var claims = new[] { "Candy bars   restore 100 nutrition each.", entry };

        var deduplicated = BenchmarkService.DeduplicateUnverifiedClaims(claims, "Candy bars restore 100 nutrition each.");

        Assert.Equal(new[] { entry }, deduplicated);
    }

    [Fact]
    public void DeduplicateUnverifiedClaims_DistinctClaims_AreAllKept()
    {
        var claims = new[] { "Candy bars restore 100 nutrition each.", "Lembas wafers give 800 nutrition." };

        Assert.Equal(claims, BenchmarkService.DeduplicateUnverifiedClaims(claims, null));
    }

    [Fact]
    public void DeduplicateUnverifiedClaims_NullOrEmpty_ReturnsAnEmptyList()
    {
        Assert.Empty(BenchmarkService.DeduplicateUnverifiedClaims(null, null));
        Assert.Empty(BenchmarkService.DeduplicateUnverifiedClaims(Array.Empty<string>(), null));
    }

    [Fact]
    public void ARefutedAccusedOnlyItem_IsNotCounted_AndContestsNothing()
    {
        var verifications = new[]
        {
            new BenchmarkClaimVerification(0, "It has no charges and never runs out.", BenchmarkClaimVerdict.Refuted, "src/objects.c:2889", "It has charges.")
                { Roles = new[] { BenchmarkClaimRoles.AccusedQuote } }
        };

        Assert.DoesNotContain(verifications, BenchmarkClaimRoles.IsOrdinaryClaim);
        Assert.Empty(BenchmarkService.SupportedAccusations(verifications));
    }

    [Fact]
    public void ASupportedAccusedItem_WithACitation_IsASupportedAccusation_AndWithoutOneIsNot()
    {
        var cited = new BenchmarkClaimVerification(0, "It has no charges and never runs out.", BenchmarkClaimVerdict.Supported, "src/objects.c:2889", "True.")
            { Roles = new[] { BenchmarkClaimRoles.AccusedQuote } };
        var uncited = cited with { Citation = "  " };

        var supported = Assert.Single(BenchmarkService.SupportedAccusations(new[] { cited }));
        Assert.Equal("src/objects.c:2889", supported.Citation);
        Assert.Empty(BenchmarkService.SupportedAccusations(new[] { uncited }));
    }

    // --- Stored shape and legacy records ----------------------------------------------------

    [Fact]
    public void Roles_AreSerialisedWhenSet_AndOmittedWhenNull()
    {
        var withRoles = new BenchmarkClaimVerification(0, "c", BenchmarkClaimVerdict.Supported, "x", null)
            { Roles = new[] { BenchmarkClaimRoles.AccusedQuote } };
        var legacy = new BenchmarkClaimVerification(0, "c", BenchmarkClaimVerdict.Supported, "x", null);

        Assert.Contains("\"roles\":[\"accusedQuote\"]", JsonSerializer.Serialize(new[] { withRoles }));
        Assert.DoesNotContain("roles", JsonSerializer.Serialize(new[] { legacy }));

        var roundTrip = JsonSerializer.Deserialize<List<BenchmarkClaimVerification>>(JsonSerializer.Serialize(new[] { withRoles }))!;
        Assert.Equal(new[] { BenchmarkClaimRoles.AccusedQuote }, roundTrip[0].Roles);
    }

    [Fact]
    public void ALegacyRecord_IsReadByTheExactTextRules_AndNeverAcquiresAnAccusedRole()
    {
        const string quote = "Applying it heals 1000 hit points.";
        const string basis = "the grail heals 2000 hit points.";
        const string legacyJson = "[" +
            "{\"claimIndex\":0,\"claim\":\"Applying it heals 1000 hit points.\",\"verdict\":\"Supported\",\"citation\":\"src/a.c\",\"basis\":null}," +
            "{\"claimIndex\":1,\"claim\":\"the grail heals 2000 hit points.\",\"verdict\":\"Refuted\",\"citation\":\"src/b.c\",\"basis\":null}," +
            "{\"claimIndex\":2,\"claim\":\"It has no charges and never runs out.\",\"verdict\":\"Supported\",\"citation\":\"src/c.c\",\"basis\":null}]";
        var verifications = JsonSerializer.Deserialize<List<BenchmarkClaimVerification>>(legacyJson)!;
        Assert.All(verifications, v => Assert.Null(v.Roles));
        Assert.False(BenchmarkClaimRoles.HasRoles(verifications));

        var answer = new BenchmarkRunAnswer
        {
            CriticalError = true,
            CriticalErrorQuote = quote,
            AnswerFlags = (int)BenchmarkAnswerFlags.OutOfRubricAccuracyDeduction,
            AssessmentEvidenceJson = JsonSerializer.Serialize(new { accuracy = $"Not in rubric: {basis}" })
        };

        var ordinary = BenchmarkService.OrdinaryClaimVerifications(verifications, answer);
        Assert.Equal("It has no charges and never runs out.", Assert.Single(ordinary).Claim);
        Assert.Empty(BenchmarkService.SupportedAccusations(verifications));
        Assert.All(verifications, v => Assert.True(BenchmarkClaimRoles.IsOrdinaryClaim(v)));
    }

    [Fact]
    public void ARecordWithRoles_IsFilteredByRole_NotByText()
    {
        var verifications = new[]
        {
            new BenchmarkClaimVerification(0, "Quoted.", BenchmarkClaimVerdict.Supported, "a", null) { Roles = new[] { BenchmarkClaimRoles.CriticalErrorQuote } },
            new BenchmarkClaimVerification(1, "Basis.", BenchmarkClaimVerdict.Refuted, "b", null) { Roles = new[] { BenchmarkClaimRoles.OutOfRubricBasis } },
            new BenchmarkClaimVerification(2, "Charged.", BenchmarkClaimVerdict.Supported, "c", null) { Roles = new[] { BenchmarkClaimRoles.AccusedQuote } },
            new BenchmarkClaimVerification(3, "Own.", BenchmarkClaimVerdict.Supported, "d", null) { Roles = new[] { BenchmarkClaimRoles.UnverifiedClaim } }
        };

        var ordinary = BenchmarkService.OrdinaryClaimVerifications(verifications, new BenchmarkRunAnswer());
        Assert.Equal("Own.", Assert.Single(ordinary).Claim);
    }

    // --- Lifecycle --------------------------------------------------------------------------

    [Fact]
    public void ANewVerdict_ClearsThePreviousVerification_AndItsFlags()
    {
        var answer = new BenchmarkRunAnswer
        {
            ClaimVerificationJson = "[]",
            ClaimVerificationError = "timeout",
            ClaimVerificationRawText = "raw",
            ClaimsSupportedCount = 1,
            ClaimsRefutedCount = 2,
            ClaimsIndeterminateCount = 3,
            ClaimVerificationInputTokens = 900,
            EvidenceInformedQualityScore = 80,
            EvidenceInformedCriticalError = false,
            EvidenceInformedJson = "{}",
            AnswerFlags = (int)(BenchmarkAnswerFlags.RefutedClaim | BenchmarkAnswerFlags.ContestedCriticalError
                | BenchmarkAnswerFlags.ContestedAccuracyDeduction | BenchmarkAnswerFlags.ContestedVerdict)
        };

        BenchmarkService.ClearReplacedVerdictEvidence(answer);

        Assert.Null(answer.ClaimVerificationJson);
        Assert.Null(answer.ClaimVerificationError);
        Assert.Null(answer.ClaimVerificationRawText);
        Assert.Null(answer.ClaimsSupportedCount);
        Assert.Null(answer.ClaimsRefutedCount);
        Assert.Null(answer.ClaimsIndeterminateCount);
        Assert.Null(answer.EvidenceInformedQualityScore);
        Assert.Null(answer.EvidenceInformedCriticalError);
        Assert.Null(answer.EvidenceInformedJson);
        Assert.Equal((int)BenchmarkAnswerFlags.ContestedVerdict, answer.AnswerFlags);

        // What the discarded verification cost is kept.
        Assert.Equal(900, answer.ClaimVerificationInputTokens);
    }

    [Fact]
    public async Task ToolCallLeads_AreLoadedThroughAFreshContext_InCallOrder()
    {
        var ct = TestContext.Current.CancellationToken;
        var dbOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        long answerId;
        await using (var seed = new ApplicationDbContext(dbOptions))
        {
            var answer = new BenchmarkRunAnswer { QuestionText = "Q", AnswerText = "A", OrderIndex = 1 };
            var run = new BenchmarkRun
            {
                SuiteName = "S", TestedModelDisplayNameUsed = "c", TestedModelProviderUsed = "p", TestedModelIdUsed = "m",
                AssessorModelDisplayNameUsed = "a", AssessorModelProviderUsed = "p", AssessorModelIdUsed = "m",
                StartedAtUtc = DateTime.UtcNow
            };
            run.Answers.Add(answer);
            answer.ToolCalls.Add(new BenchmarkRunAnswerToolCall { SortOrder = 2, Name = "wiki_view", ArgsText = "{\"title\":\"Grail\"}" });
            answer.ToolCalls.Add(new BenchmarkRunAnswerToolCall { SortOrder = 1, Name = "wiki_search", ArgsText = "{\"query\":\"grail\nof healing\"}" });
            answer.ToolCalls.Add(new BenchmarkRunAnswerToolCall { SortOrder = 3, Name = "source_code_search", ArgsText = null });
            answer.ToolCalls.Add(new BenchmarkRunAnswerToolCall { SortOrder = 4, Name = "delete_everything", ArgsText = "{}" });
            answer.ToolCalls.Add(new BenchmarkRunAnswerToolCall { SortOrder = 5, Name = "wiki_search", ArgsText = "{\"query\":\"" + new string('q', 300) + "\"}" });
            seed.BenchmarkRuns.Add(run);
            await seed.SaveChangesAsync(ct);
            answerId = answer.Id;
        }

        // A fresh context, as a retry or a re-assessment has: nothing is tracked.
        await using var fresh = new ApplicationDbContext(dbOptions);
        var leads = await BenchmarkService.LoadToolCallLeadsAsync(
            fresh, answerId, new[] { "wiki_search", "wiki_view", "source_code_search" }, ct);

        Assert.Equal(3, leads.Lines.Count);
        Assert.Equal("wiki_search {\"query\":\"grail of healing\"}", leads.Lines[0]);
        Assert.Equal("wiki_view {\"title\":\"Grail\"}", leads.Lines[1]);
        Assert.EndsWith("…", leads.Lines[2]);
        Assert.Equal("wiki_search ".Length + BenchmarkClaimVerificationPrompt.MaxToolCallLeadArgsChars + 1, leads.Lines[2].Length);
        Assert.Equal(1, leads.PrunedCount);
        Assert.Equal(0, leads.NotShownCount);
    }

    [Fact]
    public void ToolCallLeads_StopAtTwelveLines_AndCountTheRest()
    {
        var calls = Enumerable.Range(1, 15).Select(i => ((string?)"wiki_search", (string?)$"{{\"query\":\"q{i}\"}}"));

        var leads = BenchmarkClaimVerificationPrompt.BuildToolCallLeads(calls, new[] { "wiki_search" });

        Assert.Equal(BenchmarkClaimVerificationPrompt.MaxToolCallLeads, leads.Lines.Count);
        Assert.Equal(3, leads.NotShownCount);
    }

    [Fact]
    public async Task ARetryOfAFailedVerification_DropsTheRegradeTheFailedAttemptLeftBehind()
    {
        var ct = TestContext.Current.CancellationToken;
        var dbOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var (service, runManager) = CreateServiceOver(dbOptions);

        long runId;
        await using (var seed = new ApplicationDbContext(dbOptions))
        {
            var run = new BenchmarkRun
            {
                SuiteName = "S", TestedModelDisplayNameUsed = "c", TestedModelProviderUsed = "p", TestedModelIdUsed = "m",
                AssessorModelDisplayNameUsed = "a", AssessorModelProviderUsed = "p", AssessorModelIdUsed = "m",
                StartedAtUtc = DateTime.UtcNow.AddHours(-1),
                CompletedAtUtc = DateTime.UtcNow,
                Status = BenchmarkRunStatus.Running,
                TotalQuestionCount = 1,
                ScoringMethodVersion = BenchmarkAssessmentPrompt.ScoringMethodVersion
            };
            run.Answers.Add(new BenchmarkRunAnswer
            {
                QuestionText = "Q", AnswerText = "A", OrderIndex = 1,
                Status = BenchmarkAnswerStatus.Ok,
                AssessmentStatus = BenchmarkAssessmentStatus.Scored,
                ClaimVerificationError = "timeout",
                EvidenceInformedQualityScore = 88,
                EvidenceInformedCriticalError = false,
                EvidenceInformedJson = "{\"withdrawn\":[\"stale\"]}"
            });
            seed.BenchmarkRuns.Add(run);
            await seed.SaveChangesAsync(ct);
            runId = run.Id;
        }

        Assert.True(runManager.TryStart(runId, new CancellationTokenSource(), out _));
        await service.RetryFailedClaimVerificationAsync(runId, null, CancellationToken.None);

        await using var readback = new ApplicationDbContext(dbOptions);
        var retried = await readback.BenchmarkRunAnswers.SingleAsync(a => a.BenchmarkRunId == runId, ct);
        Assert.Null(retried.ClaimVerificationError);
        Assert.Null(retried.EvidenceInformedQualityScore);
        Assert.Null(retried.EvidenceInformedCriticalError);
        Assert.Null(retried.EvidenceInformedJson);
    }

    private static BenchmarkService CreateService(Dictionary<string, string?> settings)
    {
        var services = new ServiceCollection();
        var scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
        return new BenchmarkService(
            scopeFactory, null!, null!, null!, new BenchmarkRunManager(), new BenchmarkDifficultyJobManager(),
            new BenchmarkScoringProfileService(scopeFactory, NullLogger<BenchmarkScoringProfileService>.Instance),
            new ConfigurationBuilder().AddInMemoryCollection(settings).Build(),
            NullLogger<BenchmarkService>.Instance);
    }

    private static (BenchmarkService Service, BenchmarkRunManager RunManager) CreateServiceOver(DbContextOptions<ApplicationDbContext> dbOptions)
    {
        var runManager = new BenchmarkRunManager();
        var services = new ServiceCollection();
        services.AddScoped(_ => new ApplicationDbContext(dbOptions));
        services.AddScoped<SystemAiConfigService>();
        services.AddSingleton<ILogger<SystemAiConfigService>>(NullLogger<SystemAiConfigService>.Instance);
        var scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
        var service = new BenchmarkService(
            scopeFactory, null!, null!, null!, runManager, new BenchmarkDifficultyJobManager(),
            new BenchmarkScoringProfileService(scopeFactory, NullLogger<BenchmarkScoringProfileService>.Instance),
            new ConfigurationBuilder().Build(),
            NullLogger<BenchmarkService>.Instance);
        return (service, runManager);
    }
}
