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
using Overseer.Services.Privacy;
using Overseer.Tests.Helpers;
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

        // A span under 15 characters is dropped unless its sentence charges against the rubric.
        Assert.Empty(BenchmarkService.ExtractAccusedQuotes(AnswerText + " " + tooLong, Evidence(tooShort, tooLong)));

        // Charged against the rubric, a span of 4 to 14 characters that occurs once is kept; one of
        // 3 characters, and one over the maximum, are still dropped.
        var kept = Assert.Single(BenchmarkService.ExtractAccusedQuotes(
            AnswerText + " " + tooLong,
            $"The rubric disagrees with \"{tooShort}\", \"Gra\" and \"{tooLong}\"."));
        Assert.Equal(Spelltool, kept.Text);
        Assert.Equal(new[] { tooShort }, kept.QuotedFragments);
        Assert.True(kept.RubricCited);
    }

    [Fact]
    public void Extract_CapsAtFive_RubricChargedFirst_ThenLongestFirst_TiesInEvidenceOrder()
    {
        string answer = "Alpha beta gamma delta one. Alpha beta gamma delta two. Alpha beta gamma delta three. "
            + "A much longer sentence the assessor also quoted here. Alpha beta gamma delta four. "
            + "Omega rubric item.";

        var quotes = BenchmarkService.ExtractAccusedQuotes(answer, Evidence(
            "Alpha beta gamma delta one.",
            "Alpha beta gamma delta two.",
            "Alpha beta gamma delta three.",
            "A much longer sentence the assessor also quoted here.",
            "Alpha beta gamma delta four.")
            + " The rubric contradicts \"Omega rubric item.\"");

        Assert.Equal(5, BenchmarkService.MaxAccusedQuotesPerAnswer);
        Assert.Equal(BenchmarkService.MaxAccusedQuotesPerAnswer, quotes.Count);
        // The rubric-charged item leads although it is the shortest; the rest are longest first.
        Assert.Equal("Omega rubric item.", quotes[0].Text);
        Assert.True(quotes[0].RubricCited);
        Assert.Equal("A much longer sentence the assessor also quoted here.", quotes[1].Text);
        Assert.Equal("Alpha beta gamma delta three.", quotes[2].Text);
        Assert.Equal("Alpha beta gamma delta four.", quotes[3].Text);
        Assert.Equal("Alpha beta gamma delta one.", quotes[4].Text);
        Assert.All(quotes.Skip(1), q => Assert.False(q.RubricCited));
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

    // --- Charges against the rubric (run 76, suite 6, Q9 and Q16) -----------------------------
    //
    // The answers' sections and both panel members' Accuracy evidence are copied verbatim from run
    // 76's report; each rubric is the excerpt of the suite's rubric at the item revision run 76 used.

    private const string Run76Q9Answer =
        "#### Spell schools\n\n"
        + "**GnollHack has 12 spell schools, and your casting attribute depends on the school—not simply your character’s role.** When a school lists multiple attributes, it uses your **highest** one.\n\n"
        + "| School | Casting attribute | Normally somatic? |\n"
        + "|---|---|---|\n"
        + "| Abjuration | Intelligence or Wisdom | No |\n"
        + "| Arcane | Intelligence | Yes |\n"
        + "| Celestial | Wisdom or Charisma | No |\n"
        + "| Clerical | Wisdom | No |\n"
        + "| Conjuration | Intelligence or Charisma | Yes |\n"
        + "| Divination | Intelligence, Wisdom, or Charisma | No |\n"
        + "| Enchantment | Intelligence or Charisma | No |\n"
        + "| Healing | Wisdom | No |\n"
        + "| Movement | Intelligence or Wisdom | No |\n"
        + "| Nature | Wisdom or Charisma | No |\n"
        + "| Necromancy | Wisdom or Charisma | No |\n"
        + "| Transmutation | Intelligence or Wisdom | Yes |\n\n"
        + "**Somatic spells require gestures and suffer armor casting penalties.** The individual spell’s components matter; the table describes the schools’ usual behavior.\n\n"
        + "#### How this differs from NetHack\n\n"
        + "Compared with traditional NetHack spellcasting:\n\n"
        + "| Aspect | GnollHack | NetHack |\n"
        + "|---|---|---|\n"
        + "| Schools | The 12 schools above | Seven: attack, healing, divination, enchantment, clerical, escape, and matter |\n"
        + "| Casting attribute | Determined by school; may use the highest of several attributes | Intelligence or Wisdom, determined by role |\n"
        + "| Success calculation | Mostly additive modifiers shown above | A nonlinear calculation involving spell difficulty, skill, level, role-specific penalties, and equipment |\n"
        + "| Armor | Casting penalty applies to somatic spells | Metallic armor and shields generally interfere, with role-specific exceptions and robe adjustments |\n"
        + "| Preparation | Most spells require reagents mixed into a supply of castings | No comparable reagent-mixing system |\n"
        + "| Spell memory | Learned spells do not expire over time | Spell knowledge expires unless refreshed from spellbooks |\n"
        + "| Timing | Spells can have different casting times and cooldowns | No comparable general cooldown system |";

    private const string Run76Q9EvidenceA =
        "Three school entries contradict the rubric: Clerical is listed as 'Wisdom' only (rubric: Wis/Cha); "
        + "Movement is marked non-somatic (rubric: Movement is somatic); "
        + "Transmutation's attribute is given as 'Intelligence or Wisdom' (rubric: any attribute).";

    private const string Run76Q9EvidenceB =
        "The school table contradicts three required bindings: Clerical must use Wisdom/Charisma, not Wisdom alone; "
        + "Movement is somatic, not non-somatic; and Transmutation uses any attribute, not only Intelligence/Wisdom. "
        + "The rubric also explicitly marks listing NetHack's seven categories as a critical error.";

    private const string Run76Q9Rubric =
        "- TWELVE spell schools with attribute bindings and somatic flags:\n"
        + "  - Abjuration (Int/Wis, non-somatic)\n"
        + "  - Arcane (Int, somatic)\n"
        + "  - Celestial (Wis/Cha, non-somatic)\n"
        + "  - Clerical (Wis/Cha, non-somatic)\n"
        + "  - Conjuration (Int/Cha, somatic)\n"
        + "  - Divination (any, non-somatic)\n"
        + "  - Enchantment (Int/Cha, non-somatic)\n"
        + "  - Healing (Wis, non-somatic)\n"
        + "  - Movement (Int/Wis, somatic)\n"
        + "  - Nature (Wis/Cha, non-somatic)\n"
        + "  - Necromancy (Wis/Cha, non-somatic)\n"
        + "  - Transmutation (any, somatic)";

    private const string AbjurationRow = "| Abjuration | Intelligence or Wisdom | No |";
    private const string ClericalRow = "| Clerical | Wisdom | No |";
    private const string MovementRow = "| Movement | Intelligence or Wisdom | No |";
    private const string TransmutationRow = "| Transmutation | Intelligence or Wisdom | Yes |";

    private const string Run76Q16Answer =
        "#### Individual monster level: `adj_lev()`\n\n"
        + "Let **B** be the species’ base level, **A** a caller-supplied level adjustment, **P** the player’s experience level, and **D** the effective dungeon difficulty.\n\n"
        + "The ordinary calculation is:\n\n"
        + "1. Start with `L = B + A`.\n"
        + "2. Adjust for dungeon difficulty:\n"
        + "   - If `D < B + A`, subtract **1**.\n"
        + "   - Otherwise, add `floor((D − (B + A)) / 5)`.\n"
        + "3. If `P > B`, add `floor((P − B) / 4)`.\n"
        + "4. Clamp the result between **0** and `clamp(floor(3B / 2) + A, 0, 127)`.\n\n"
        + "Thus, deeper levels add roughly one monster level per five levels of difficulty above the starting level; a stronger player adds one per four experience levels above the species’ base level. A weaker player does **not** directly lower it.\n\n"
        + "There are exceptions: the Wizard of Yendor uses base level plus his recorded death count, capped at 127; `B + A > 127` returns 127 immediately; and `MM_NORMAL_HIT_DICE` bypasses adjustment and uses the species’ base level.  \n"
        + "*References: `src/makemon.c`, approximately lines 2573 and 3976–4011.*\n\n"
        + "#### Selecting monsters and encounters\n\n"
        + "Selection uses a **difficulty-rating range**, which is distinct from the individual monster’s level above. For ordinary generation, let `S = min(D, 50) + P` and `T = D + P`:\n\n"
        + "| Selection attempt | Minimum difficulty | Maximum difficulty |\n"
        + "|---|---:|---:|\n"
        + "| First, tight range | `floor(min(25, max(0, S × 0.5 / 1.66 − 1)))` | `floor(max(1, T × 0.5 + 0.5))` |\n"
        + "| Second, wider range | `floor(min(15, max(0, S × 0.5 / 2.5 − 1)))` | `floor(max(1, T × 0.5 × 1.189 + 0.5))` |\n"
        + "| Further fallback | `0` | `floor(max(1, T × 0.5 × 1.414 + 0.5))` |\n\n"
        + "Caller-supplied difficulty adjustments can alter those multipliers; **the game-difficulty setting’s contribution is explicitly disabled in this helper**. Random encounters use the first range to filter encounter difficulty ratings, then select among eligible definitions by their weights.  \n"
        + "*References: `src/makemon.c`, approximately lines 3649–3738; `src/encounter.c`, approximately lines 1242–1328.*";

    private const string Run76Q16EvidenceA =
        "The answer defines `S = min(D, 50) + P` for the minimum-level term; the rubric states the code uses `min(30, depth) + u.ulevel` "
        + "(minmlev = min(25, max(0, (min(30, depth) + u.ulevel) × 0.5 / 1.66 − 1))). The depth clamp of 50 is incorrect. "
        + "All other formulas (3.32-equivalent divisor, max = half the sum plus 0.5, 1.189/1.414 fallbacks, adj_lev integer rules and 127 cap, disabled m_initgrp) match.";

    private const string Run76Q16EvidenceB =
        "The `adj_lev()` description incorrectly compares dungeon difficulty with `B + A`; the rubric says the depth comparison "
        + "and one-per-five calculation use the monster's base level, while `manual_adj` affects the starting value and cap. "
        + "The selection formulas also use `min(D, 50)` instead of the rubric's `min(30, depth)`, and present the level band as "
        + "floored integers even though the rubric explicitly says it is floating-point.";

    private const string Run76Q16Rubric =
        "Attempt 1: `minmlev = min(25, max(0, (min(30, depth) + u.ulevel) × 0.5 / 1.66 − 1))` — i.e. the sum divided by 3.32, "
        + "minus 1 — and `maxmlev = max(1, (depth + u.ulevel) × 0.5 + 0.5)`.";

    private const string Run76Q16SelectionSentence = "For ordinary generation, let `S = min(D, 50) + P` and `T = D + P`:";

    [Fact]
    public void Extract_Run76Q9_MemberA_AccusesTheClericalMovementAndTransmutationRows_NeverTheAbjurationRow()
    {
        var quotes = BenchmarkService.ExtractAccusedQuotes(Run76Q9Answer, Run76Q9EvidenceA, Run76Q9Rubric);

        Assert.Equal(3, quotes.Count);
        Assert.All(quotes, q => Assert.True(q.RubricCited));
        Assert.DoesNotContain(quotes, q => q.Text == AbjurationRow);

        // 'Wisdom' is 6 characters on ten lines; the clause names the Clerical row.
        var clerical = Assert.Single(quotes, q => q.Text == ClericalRow);
        Assert.Equal(new[] { "Wisdom" }, clerical.QuotedFragments);
        Assert.Equal("Wis/Cha", clerical.RubricQuote);
        Assert.Equal(Run76Q9EvidenceA, clerical.Charge);

        // No quoted span: the clause names the row by its first cell, and is the charge.
        var movement = Assert.Single(quotes, q => q.Text == MovementRow);
        Assert.Null(movement.QuotedFragments);
        Assert.Equal("Movement is marked non-somatic (rubric: Movement is somatic)", movement.Charge);
        Assert.Equal("Movement is somatic", movement.RubricQuote);

        // 'Intelligence or Wisdom' is on four lines; the row the clause names wins over the
        // Casting attribute row, which shares only the word "attribute" with it.
        var transmutation = Assert.Single(quotes, q => q.Text == TransmutationRow);
        Assert.Equal(new[] { "Intelligence or Wisdom" }, transmutation.QuotedFragments);
        Assert.Equal("any attribute", transmutation.RubricQuote);
    }

    [Fact]
    public void Extract_Run76Q9_MemberB_AccusesTheSameThreeRowsByLabel()
    {
        var quotes = BenchmarkService.ExtractAccusedQuotes(Run76Q9Answer, Run76Q9EvidenceB, Run76Q9Rubric);

        Assert.Equal(
            new[] { ClericalRow, MovementRow, TransmutationRow }.OrderBy(t => t, StringComparer.Ordinal),
            quotes.Select(q => q.Text).OrderBy(t => t, StringComparer.Ordinal));
        Assert.All(quotes, q =>
        {
            Assert.True(q.RubricCited);
            Assert.Null(q.QuotedFragments);
            Assert.Null(q.RubricQuote);
        });
        Assert.Equal("Clerical must use Wisdom/Charisma, not Wisdom alone", Assert.Single(quotes, q => q.Text == ClericalRow).Charge);
        Assert.Equal("and Transmutation uses any attribute, not only Intelligence/Wisdom.", Assert.Single(quotes, q => q.Text == TransmutationRow).Charge);
    }

    [Fact]
    public void UnionManifest_Run76Q9_MergesBothMembersCopies_IntoThreeRowsAccusedByBoth()
    {
        var manifest = BenchmarkService.BuildUnionClaimManifest(new[]
        {
            Contribution(BenchmarkPanelMember.A, accused: BenchmarkService.ExtractAccusedQuotes(Run76Q9Answer, Run76Q9EvidenceA, Run76Q9Rubric).ToArray()),
            Contribution(BenchmarkPanelMember.B, accused: BenchmarkService.ExtractAccusedQuotes(Run76Q9Answer, Run76Q9EvidenceB, Run76Q9Rubric).ToArray())
        }, Run76Q9Answer);

        Assert.Equal(3, manifest.Count);
        Assert.All(manifest, item =>
        {
            Assert.Equal(new[] { BenchmarkClaimRoles.AccusedQuote }, item.Roles);
            Assert.Equal(new[] { "A", "B" }, item.AccusedBy);
            Assert.True(item.RubricCited);
        });
        var clerical = Assert.Single(manifest, m => m.Text == ClericalRow);
        Assert.Equal(new[] { "Wisdom" }, clerical.QuotedFragments);
        Assert.Equal("Wis/Cha", clerical.RubricQuote);
        Assert.Contains("Clerical is listed as 'Wisdom' only", clerical.Charge);
        Assert.Contains("Clerical must use Wisdom/Charisma, not Wisdom alone", clerical.Charge);
    }

    [Fact]
    public void Extract_Run76Q16_MemberA_ReadsABacktickQuote_AndTakesTheRubricQuoteFromItsSentence()
    {
        var quote = Assert.Single(BenchmarkService.ExtractAccusedQuotes(Run76Q16Answer, Run76Q16EvidenceA, Run76Q16Rubric));

        Assert.Equal(Run76Q16SelectionSentence, quote.Text);
        Assert.Equal(new[] { "S = min(D, 50) + P" }, quote.QuotedFragments);
        Assert.True(quote.RubricCited);
        Assert.Equal("min(30, depth) + u.ulevel", quote.RubricQuote);
    }

    [Fact]
    public void Extract_Run76Q16_MemberB_KeepsTheShortSpanThatOccursOnce_AndDropsTheUnanchoredAndHeadingSpans()
    {
        var quote = Assert.Single(BenchmarkService.ExtractAccusedQuotes(Run76Q16Answer, Run76Q16EvidenceB, Run76Q16Rubric));

        // `B + A` is on four lines and no anchor word of its clause is on any; `adj_lev()` is only
        // in a heading. `min(D, 50)`, 10 characters, occurs once.
        Assert.Equal(Run76Q16SelectionSentence, quote.Text);
        Assert.Equal(new[] { "min(D, 50)" }, quote.QuotedFragments);
        Assert.True(quote.RubricCited);
        Assert.Equal("min(30, depth)", quote.RubricQuote);
    }

    [Fact]
    public void UnionManifest_Run76Q16_IsOneSentenceAccusedByBoth_WithBothFragments()
    {
        var manifest = BenchmarkService.BuildUnionClaimManifest(new[]
        {
            Contribution(BenchmarkPanelMember.A, accused: BenchmarkService.ExtractAccusedQuotes(Run76Q16Answer, Run76Q16EvidenceA, Run76Q16Rubric).ToArray()),
            Contribution(BenchmarkPanelMember.B, accused: BenchmarkService.ExtractAccusedQuotes(Run76Q16Answer, Run76Q16EvidenceB, Run76Q16Rubric).ToArray())
        }, Run76Q16Answer);

        var item = Assert.Single(manifest);
        Assert.Equal(Run76Q16SelectionSentence, item.Text);
        Assert.Equal(new[] { "S = min(D, 50) + P", "min(D, 50)" }, item.QuotedFragments);
        Assert.Equal(new[] { "A", "B" }, item.AccusedBy);
        Assert.True(item.RubricCited);
        Assert.Equal("min(30, depth) + u.ulevel", item.RubricQuote);
    }

    [Fact]
    public void Extract_WithoutTheRubricText_StillAccusesTheRows_AndQuotesOnlyTheRubricParenthetical()
    {
        var q9 = BenchmarkService.ExtractAccusedQuotes(Run76Q9Answer, Run76Q9EvidenceA);
        Assert.Equal("Wis/Cha", Assert.Single(q9, q => q.Text == ClericalRow).RubricQuote);

        var q16 = Assert.Single(BenchmarkService.ExtractAccusedQuotes(Run76Q16Answer, Run76Q16EvidenceA));
        Assert.True(q16.RubricCited);
        Assert.Null(q16.RubricQuote);
    }

    // Two lines carry the same quoted span; the charge clause decides which one it means.
    private const string AntsAnswer = "Soldier ants deal 2d4 damage to gnomes.\nSoldier ants deal 2d4 damage to dwarves.";

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Extract_ARepeatedSpan_GoesToTheLineItsClauseNames_AndIsDroppedOnATieOrWithoutAnAnchor(bool rubricChargedQuotes)
    {
        var anchored = Assert.Single(BenchmarkService.ExtractAccusedQuotes(
            AntsAnswer, "The answer overstates it for gnomes, \"Soldier ants deal 2d4 damage\" being wrong.", null, rubricChargedQuotes));
        Assert.Equal("Soldier ants deal 2d4 damage to gnomes.", anchored.Text);

        var anchoredSecond = Assert.Single(BenchmarkService.ExtractAccusedQuotes(
            AntsAnswer, "The answer overstates it for dwarves, \"Soldier ants deal 2d4 damage\" being wrong.", null, rubricChargedQuotes));
        Assert.Equal("Soldier ants deal 2d4 damage to dwarves.", anchoredSecond.Text);

        // Both lines hold one anchor word each.
        Assert.Empty(BenchmarkService.ExtractAccusedQuotes(
            AntsAnswer, "The answer overstates it for gnomes and dwarves alike, \"Soldier ants deal 2d4 damage\" being wrong.", null, rubricChargedQuotes));

        // No word of the clause is on either line.
        Assert.Empty(BenchmarkService.ExtractAccusedQuotes(AntsAnswer, Evidence("Soldier ants deal 2d4 damage"), null, rubricChargedQuotes));
    }

    // --- A table header row (run 78, Q2) ------------------------------------------------------
    //
    // The answer and member A's Accuracy evidence are copied verbatim from run 78's report; the
    // rubric is the excerpt of the suite's rubric run 78 used.

    private const string Run78Q2Answer =
        "These quality modifiers multiply a weapon’s **base damage**:\n\n"
        + "| Quality | Base damage | Restriction |\n"
        + "|---|---:|---|\n"
        + "| **Exceptional** | ×2 | No alignment restriction |\n"
        + "| **Elite** | ×3 | No alignment restriction |\n"
        + "| **Celestial** | ×4 | Lawful users only |\n"
        + "| **Primordial** | ×4 | Neutral users only |\n"
        + "| **Infernal** | ×4 | Chaotic users only |\n\n"
        + "The multiplier applies to **base weapon damage**, not simply to the final damage total including every bonus. "
        + "These qualities can occur on magical weapons too, as well as ranged weapons and ammunition.";

    private const string Run78Q2EvidenceA =
        "Multipliers (2x/3x/4x) and the alignment restrictions are all correct; the single imprecision is phrasing the effect "
        + "as multiplying 'base damage' rather than explicitly multiplying the damage dice (rubric: 'The multiplier applies to the dice').";

    private const string Run78Q2Rubric =
        "- Quality modifiers MULTIPLY the weapon's base damage dice.\n"
        + "- The multiplier applies to the dice (e.g., a long sword dealing 1d8 / 1d12 deals 2d8 / 2d12 when exceptional).";

    [Fact]
    public void Extract_Run78Q2_ASpanAlsoInATableHeaderRow_IsPlacedOnItsOneSentence()
    {
        var quote = Assert.Single(BenchmarkService.ExtractAccusedQuotes(Run78Q2Answer, Run78Q2EvidenceA, Run78Q2Rubric));

        // 'base damage' is also in the header row, which names a column; no anchor word of the
        // clause is on either line, so only the sentence left once the header is set aside places it.
        Assert.Equal("These quality modifiers multiply a weapon’s **base damage**:", quote.Text);
        Assert.Equal(new[] { "base damage" }, quote.QuotedFragments);
        Assert.True(quote.RubricCited);
        Assert.Equal("The multiplier applies to the dice", quote.RubricQuote);
        Assert.Equal(Run78Q2EvidenceA, quote.Charge);
    }

    [Fact]
    public void Extract_ASpanOnlyInATableHeaderRow_IsDropped()
    {
        const string answer =
            "Quality changes the damage dice.\n\n"
            + "| Quality | Effect on base damage |\n"
            + "|---|---|\n"
            + "| Exceptional | ×2 |\n"
            + "| Elite | ×3 |";

        Assert.Empty(BenchmarkService.ExtractAccusedQuotes(
            answer, "The rubric says the dice are multiplied, not the 'Effect on base damage' the answer describes.", Run78Q2Rubric));
    }

    [Fact]
    public void Extract_TheSwitchOff_StillAnchorsTheRun76Q9RepeatedSpan_ButReadsNoRubricCharge()
    {
        var quote = Assert.Single(BenchmarkService.ExtractAccusedQuotes(Run76Q9Answer, Run76Q9EvidenceA, Run76Q9Rubric, rubricChargedQuotes: false));

        // The 6-character 'Wisdom' and the unquoted Movement charge are not read; the repeated
        // 'Intelligence or Wisdom' is still placed on the row its clause names.
        Assert.Equal(TransmutationRow, quote.Text);
        Assert.False(quote.RubricCited);
        Assert.Null(quote.RubricQuote);

        // Backticks are no quotation marks with the switch off.
        Assert.Empty(BenchmarkService.ExtractAccusedQuotes(Run76Q16Answer, Run76Q16EvidenceA, Run76Q16Rubric, rubricChargedQuotes: false));
        Assert.Empty(BenchmarkService.ExtractAccusedQuotes(Run76Q9Answer, Run76Q9EvidenceB, Run76Q9Rubric, rubricChargedQuotes: false));
    }

    [Theory]
    // Not charged against the rubric: a short span is dropped.
    [InlineData("Clerical is listed as 'Wisdom' only.", null)]
    // Charged against the rubric: the same short span is kept on the row its clause names.
    [InlineData("Clerical is listed as 'Wisdom' only (rubric: Wis/Cha).", ClericalRow)]
    // A clause naming two rows accuses neither, and one naming a single row accuses it.
    [InlineData("The rubric binds Clerical and Healing to Wisdom/Charisma, not Wisdom alone.", null)]
    [InlineData("The rubric binds Clerical to Wisdom/Charisma, not Wisdom alone.", ClericalRow)]
    // A clause that approves of the row it names accuses nothing.
    [InlineData("Clerical matches the rubric.", null)]
    public void Extract_ShortSpansAndNamedRows_AreReadOnlyUnderARubricCharge(string evidence, string? expectedRow)
    {
        var quotes = BenchmarkService.ExtractAccusedQuotes(Run76Q9Answer, evidence, Run76Q9Rubric);

        if (expectedRow == null)
        {
            Assert.Empty(quotes);
        }
        else
        {
            var quote = Assert.Single(quotes);
            Assert.Equal(expectedRow, quote.Text);
            Assert.True(quote.RubricCited);
        }
    }

    public static TheoryData<string, string, string[]> Harness45Fixtures => new()
    {
        { AnswerText, Evidence("Applying it heals 1000   hit points and restores 500 mana."), new[] { "Applying it heals **1000 hit points** and restores 500 mana." } },
        { AnswerText, "The rubric states \"the grail heals 2000 hit points when invoked\" instead.", Array.Empty<string>() },
        {
            AnswerText,
            "The answer claims “It has no charges and never runs out.” and later “the answer's \"Gnolls regenerate hit points faster at night\" line” is invented.",
            new[] { "Gnolls regenerate hit points faster at night", "It has no charges and never runs out." }
        },
        {
            "Alpha beta gamma delta one. Alpha beta gamma delta two. Alpha beta gamma delta three. A much longer sentence the assessor also quoted here.",
            Evidence("Alpha beta gamma delta one.", "Alpha beta gamma delta two.", "Alpha beta gamma delta three.", "A much longer sentence the assessor also quoted here."),
            new[] { "A much longer sentence the assessor also quoted here.", "Alpha beta gamma delta three.", "Alpha beta gamma delta one." }
        },
        { Run55Q17Answer, Run55Q17Evidence, new[] { "healthy supply of vegan food" } },
        {
            "An uncursed scroll of identify reveals 2 items (and a blessed one reveals 3), so read it first.\n\n"
                + "Soft glass scratches/crushes differently than real gems, so rub the gray stones before you sell them.",
            "Correct on the core mechanic (\"an uncursed scroll of identify reveals 2 items (and a blessed one reveals 3)\"). "
                + "Imprecision: \"Soft glass scratches/crushes differently than real gems\" implies a hardness test the game does not model.",
            new[] { "Soft glass scratches/crushes differently than real gems" }
        },
        {
            "The headstone reads Here lies Fred, killed by a jackal. That makes this a bones level.",
            "The answer says the grave reads 'Here lies Fred, killed by a jackal.' which is not what the board shows.",
            new[] { "Here lies Fred, killed by a jackal." }
        },
        {
            "Items from the grave lie on the floor, not buried. Digging up a grave as a lawful character costs alignment, "
                + "and you see 'You have violated the sanctity of this grave!' when you do. Bones gear is often cursed, "
                + "so test them with your Holy Grail before wearing anything.",
            "Core claims match the rubric (items lie on the floor, not buried; lawful alignment loss and the exact "
                + "'You have violated the sanctity of this grave!' message; bones gear often cursed). The suggestion to "
                + "'test them with your Holy Grail' is an imprecise/unsupported BUC-testing method, keeping this below 6.",
            new[] { "test them with your Holy Grail" }
        },
        {
            "Items lie on the floor. You see 'You have violated the sanctity of this grave!' when digging.",
            "Core claims match the rubric (items lie on the floor; the exact 'You have violated the sanctity of this grave!' "
                + "message is wrong; bones gear often cursed).",
            new[] { "You have violated the sanctity of this grave!" }
        },
        { Run56Q18Answer, Run56Q18Evidence, Array.Empty<string>() },
        {
            "Wands of digging dig through any wall in the dungeon. Engrave-testing a wand always identifies it. Zapping a wand of wishing downwards is safe.",
            "Accuracy 3. Wrong: \"dig through any wall in the dungeon\" and \"Wands of digging dig through\" overstate it; "
                + "\"Engrave-testing a wand always identifies it\" is not true either. Also \"always identifies it\" is wrong.",
            new[] { "dig through any wall in the dungeon", "Engrave-testing a wand always identifies it" }
        }
    };

    [Theory]
    [MemberData(nameof(Harness45Fixtures))]
    public void Extract_TheSwitchOff_ReproducesHarness45_OnFixturesWhoseSpansOccurOnce(string answer, string evidence, string[] firstFragments)
    {
        var quotes = BenchmarkService.ExtractAccusedQuotes(answer, evidence, rubricText: null, rubricChargedQuotes: false);

        // Each submission's first quoted fragment, in submission order: longest sentence first, at most three.
        Assert.Equal(firstFragments, quotes.Select(q => q.QuotedFragments![0]).ToArray());
        Assert.True(quotes.Count <= BenchmarkService.MaxAccusedQuotesPerAnswerRubricChargesOff);
        Assert.All(quotes, q =>
        {
            Assert.False(q.RubricCited);
            Assert.Null(q.RubricQuote);
        });
    }

    [Theory]
    [InlineData(true, 1)]
    [InlineData(false, 0)]
    public void TheRubricChargedSwitch_DecidesWhetherARubricChargedBacktickQuoteIsSubmitted(bool enabled, int expected)
    {
        var service = CreateService(new Dictionary<string, string?>
        {
            ["Benchmark:ClaimVerification:RubricChargedQuotesEnabled"] = enabled ? "true" : "false"
        });
        var answer = new BenchmarkRunAnswer
        {
            AnswerText = Run76Q16Answer,
            AccuracyLevel = 4,
            AssessmentEvidenceJson = JsonSerializer.Serialize(new { accuracy = Run76Q16EvidenceA })
        };

        var quotes = service.AccusedQuotesFor(answer, Run76Q16Rubric);

        Assert.Equal(expected, quotes.Count);
        Assert.Equal(enabled, service.NeedsClaimVerificationOrAccusation(answer));
    }

    [Fact]
    public void StampRoles_CarriesTheRubricCitationFromTheManifest_AndOmitsItWhenAbsent()
    {
        var accused = BenchmarkService.ExtractAccusedQuotes(Run76Q9Answer, Run76Q9EvidenceA, Run76Q9Rubric);
        var manifest = BenchmarkService.BuildClaimManifest(new[] { "Arcane spells are somatic." }, null, null, accused);
        var stamped = BenchmarkService.StampRoles(manifest
            .Select((m, i) => new BenchmarkClaimVerification(i, m.Text, BenchmarkClaimVerdict.Supported, "src/spell.c:1210", "True."))
            .ToList(), manifest);

        var clerical = Assert.Single(stamped, v => v.Claim == ClericalRow);
        Assert.True(clerical.RubricCited);
        Assert.Equal("Wis/Cha", clerical.RubricQuote);
        var own = Assert.Single(stamped, v => v.Claim == "Arcane spells are somatic.");
        Assert.Null(own.RubricCited);
        Assert.Null(own.RubricQuote);

        string json = JsonSerializer.Serialize(new[] { clerical });
        Assert.Contains("\"rubricCited\":true", json);
        Assert.Contains("\"rubricQuote\":\"Wis/Cha\"", json);
        string ownJson = JsonSerializer.Serialize(new[] { own });
        Assert.DoesNotContain("rubricCited", ownJson);
        Assert.DoesNotContain("rubricQuote", ownJson);

        var read = JsonSerializer.Deserialize<List<BenchmarkClaimVerification>>(json)!;
        Assert.True(read[0].RubricCited);
        Assert.Equal("Wis/Cha", read[0].RubricQuote);
    }

    private static BenchmarkClaimVerification ClericalVerdict(
        BenchmarkClaimVerdict verdict,
        string? citation,
        bool rubricCited,
        string? citationNote = null,
        IReadOnlyList<string>? accusedBy = null)
        => new(0, ClericalRow, verdict, citation, "Clerical spells use Wisdom alone.")
        {
            Roles = new[] { BenchmarkClaimRoles.AccusedQuote },
            QuotedFragments = new[] { "Wisdom" },
            Charge = Run76Q9EvidenceA,
            RubricCited = rubricCited ? true : null,
            RubricQuote = rubricCited ? "Wis/Cha" : null,
            ChargedPart = true,
            CitationNote = citationNote,
            RaisedBy = accusedBy,
            AccusedBy = accusedBy
        };

    [Theory]
    [InlineData(BenchmarkClaimVerdict.Supported, "src/spell.c:1210", true, null, true, true)]
    [InlineData(BenchmarkClaimVerdict.Refuted, "src/spell.c:1210", true, null, false, false)]
    [InlineData(BenchmarkClaimVerdict.Indeterminate, null, true, null, false, false)]
    [InlineData(BenchmarkClaimVerdict.Supported, null, true, null, false, false)]
    [InlineData(BenchmarkClaimVerdict.Supported, "src/spell.c:1210", true, BenchmarkClaimVerificationParser.ChargedPartNotJudgedNote, false, false)]
    [InlineData(BenchmarkClaimVerdict.Supported, "src/spell.c:1210", false, null, false, true)]
    public void Outcome_RaisesRubricContradictedBySource_OnlyForASupportedCitedAccusationChargedAgainstTheRubric(
        BenchmarkClaimVerdict verdict, string? citation, bool rubricCited, string? citationNote, bool expectedRubricFlag, bool expectedContested)
    {
        var answer = new BenchmarkRunAnswer
        {
            AnswerText = Run76Q9Answer,
            AccuracyLevel = 3,
            // A stale flag from an earlier verification is cleared when this one does not raise it.
            AnswerFlags = (int)BenchmarkAnswerFlags.RubricContradictedBySource
        };

        BenchmarkService.ApplyClaimVerificationOutcome(answer, new[] { ClericalVerdict(verdict, citation, rubricCited, citationNote) }, false, null);

        var flags = (BenchmarkAnswerFlags)answer.AnswerFlags;
        Assert.Equal(expectedRubricFlag, flags.HasFlag(BenchmarkAnswerFlags.RubricContradictedBySource));
        Assert.Equal(expectedContested, flags.HasFlag(BenchmarkAnswerFlags.ContestedAccuracyDeduction));
        Assert.Equal(expectedRubricFlag, BenchmarkRunFinalizer.IsRubricContradicted(answer));
        // Both flags are advisory.
        Assert.Equal(expectedRubricFlag || expectedContested, BenchmarkRunFinalizer.HasAdvisoryFlag(answer));
    }

    [Theory]
    [InlineData(new[] { "B" }, false, true)]
    [InlineData(new[] { "A" }, true, false)]
    [InlineData(new[] { "A", "B" }, true, true)]
    public void PanelOutcome_RaisesRubricContradictedBySource_OnTheAccusingMembersOnly(string[] accusedBy, bool expectedA, bool expectedB)
    {
        var answer = PanelAnswer(3, Run76Q9EvidenceA, 3, Run76Q9EvidenceB);
        var verifications = new[] { ClericalVerdict(BenchmarkClaimVerdict.Supported, "src/spell.c:1210", true, accusedBy: accusedBy) };

        BenchmarkService.ApplyPanelClaimVerificationOutcome(
            answer, verifications, BenchmarkVerdictView.FromPrimary(answer), BenchmarkVerdictView.FromCoAssessment(answer));

        var record = BenchmarkCoAssessmentRecord.Parse(answer.CoAssessmentJson)!;
        Assert.Equal(expectedA, ((BenchmarkAnswerFlags)answer.AnswerFlags).HasFlag(BenchmarkAnswerFlags.RubricContradictedBySource));
        Assert.Equal(expectedB, record.Flags!.RubricContradictedBySource);
        // The contested deduction follows the same members.
        Assert.Equal(expectedA, ((BenchmarkAnswerFlags)answer.AnswerFlags).HasFlag(BenchmarkAnswerFlags.ContestedAccuracyDeduction));
        Assert.Equal(expectedB, record.Flags.ContestedAccuracyDeduction);
        Assert.True(BenchmarkRunFinalizer.IsRubricContradicted(answer));
    }

    [Fact]
    public void PanelOutcome_ANonRubricAccusation_RaisesNoRubricContradiction()
    {
        var answer = PanelAnswer(3, Run76Q9EvidenceA, 3, Run76Q9EvidenceB);
        var verifications = new[] { ClericalVerdict(BenchmarkClaimVerdict.Supported, "src/spell.c:1210", false, accusedBy: new[] { "A", "B" }) };

        BenchmarkService.ApplyPanelClaimVerificationOutcome(
            answer, verifications, BenchmarkVerdictView.FromPrimary(answer), BenchmarkVerdictView.FromCoAssessment(answer));

        Assert.False(((BenchmarkAnswerFlags)answer.AnswerFlags).HasFlag(BenchmarkAnswerFlags.RubricContradictedBySource));
        Assert.False(BenchmarkCoAssessmentRecord.Parse(answer.CoAssessmentJson)!.Flags!.RubricContradictedBySource);
        Assert.False(BenchmarkRunFinalizer.IsRubricContradicted(answer));
    }

    [Fact]
    public void ANewVerdict_ClearsRubricContradictedBySource_ForBothMembers()
    {
        var answer = PanelAnswer(3, Run76Q9EvidenceA, 3, Run76Q9EvidenceB);
        BenchmarkService.ApplyPanelClaimVerificationOutcome(
            answer,
            new[] { ClericalVerdict(BenchmarkClaimVerdict.Supported, "src/spell.c:1210", true, accusedBy: new[] { "A", "B" }) },
            BenchmarkVerdictView.FromPrimary(answer),
            BenchmarkVerdictView.FromCoAssessment(answer));
        Assert.True(BenchmarkRunFinalizer.IsRubricContradicted(answer));

        BenchmarkService.ClearReplacedVerdictEvidence(answer);

        Assert.False(((BenchmarkAnswerFlags)answer.AnswerFlags).HasFlag(BenchmarkAnswerFlags.RubricContradictedBySource));
        Assert.False(BenchmarkCoAssessmentRecord.Parse(answer.CoAssessmentJson)!.Flags!.RubricContradictedBySource);
        Assert.False(BenchmarkRunFinalizer.IsRubricContradicted(answer));
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
                SuiteName = "S",
                TestedModelSnapshot = BenchmarkModelSnapshots.Model(provider: "p", modelId: "m", displayName: "c"),
                AssessorModelSnapshot = BenchmarkModelSnapshots.Model(provider: "p", modelId: "m", displayName: "a"),
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
                SuiteName = "S",
                TestedModelSnapshot = BenchmarkModelSnapshots.Model(provider: "p", modelId: "m", displayName: "c"),
                AssessorModelSnapshot = BenchmarkModelSnapshots.Model(provider: "p", modelId: "m", displayName: "a"),
                StartedAtUtc = DateTime.UtcNow.AddHours(-1),
                CompletedAtUtc = DateTime.UtcNow,
                Status = BenchmarkRunStatus.Running,
                TotalQuestionCount = 1,
                ScoringMethodVersion = BenchmarkAssessmentPrompt.ScoringMethodVersion
            };
            run.Answers.Add(new BenchmarkRunAnswer
            {
                QuestionText = "Q", AnswerText = "A", OrderIndex = 1,
                ExpectedPointsRecorded = true,
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

    // --- The panel's union manifest and its provenance -------------------------------------

    private const string Spelltool = "The Grail of Healing is a spelltool.";
    private const string Heals = "Applying it heals **1000 hit points** and restores 500 mana.";
    private const string NoCharges = "It has no charges and never runs out.";
    private const string Regenerate = "Gnolls regenerate hit points faster at night than other races do.";
    private const string BasisA = "the grail heals 2000 hit points.";
    private const string BasisB = "grails hold five charges.";

    private static BenchmarkService.ClaimContribution Contribution(
        BenchmarkPanelMember member,
        string[]? claims = null,
        string? quote = null,
        string? basis = null,
        BenchmarkService.AccusedQuote[]? accused = null,
        string[]? statements = null)
        => new(member, claims, quote, basis, accused, statements);

    [Fact]
    public void UnionManifest_BothMembersQuotes_AKeepsTheFirstPlace_BFollows_AndTheBasesFollowTheQuotes()
    {
        var manifest = BenchmarkService.BuildUnionClaimManifest(new[]
        {
            // Listed out of member order on purpose: the manifest orders by member.
            Contribution(BenchmarkPanelMember.B, new[] { "the grail of healing   is a spelltool." }, NoCharges, BasisB),
            Contribution(BenchmarkPanelMember.A, new[] { Spelltool }, Heals, BasisA)
        }, AnswerText);

        Assert.Equal(new[] { Heals, NoCharges, BasisA, BasisB, Spelltool }, manifest.Select(m => m.Text));
        Assert.Equal(new[] { BenchmarkClaimRoles.CriticalErrorQuote }, manifest[0].Roles);
        Assert.Equal(new[] { BenchmarkClaimRoles.CriticalErrorQuote }, manifest[1].Roles);
        Assert.Equal(new[] { BenchmarkClaimRoles.OutOfRubricBasis }, manifest[2].Roles);
        Assert.Equal(new[] { BenchmarkClaimRoles.OutOfRubricBasis }, manifest[3].Roles);
        Assert.Equal(new[] { BenchmarkClaimRoles.UnverifiedClaim }, manifest[4].Roles);

        Assert.Equal(new[] { "A" }, manifest[0].RaisedBy);
        Assert.Equal(new[] { "B" }, manifest[1].RaisedBy);
        Assert.Equal(new[] { "A" }, manifest[2].RaisedBy);
        Assert.Equal(new[] { "B" }, manifest[3].RaisedBy);
        // A claim both members raised, differing only in case and spacing, is listed once for both.
        Assert.Equal(new[] { "A", "B" }, manifest[4].RaisedBy);
    }

    [Fact]
    public void UnionManifest_AWithoutAQuote_BsQuoteTakesTheFirstPlace()
    {
        var manifest = BenchmarkService.BuildUnionClaimManifest(new[]
        {
            Contribution(BenchmarkPanelMember.A, new[] { Spelltool }),
            Contribution(BenchmarkPanelMember.B, quote: NoCharges)
        }, AnswerText);

        Assert.Equal(new[] { NoCharges, Spelltool }, manifest.Select(m => m.Text));
        Assert.Equal(new[] { BenchmarkClaimRoles.CriticalErrorQuote }, manifest[0].Roles);
        Assert.Equal(new[] { "B" }, manifest[0].RaisedBy);
        Assert.Equal(new[] { "A" }, manifest[1].RaisedBy);

        // The placement a single-assessor manifest gives the quote.
        var single = BenchmarkService.BuildClaimManifest(new[] { Spelltool }, NoCharges, null, null);
        Assert.Equal(single.Select(m => m.Text), manifest.Select(m => m.Text));
    }

    [Fact]
    public void UnionManifest_AQuoteBothMembersRaised_IsOneItemRaisedByBoth()
    {
        var manifest = BenchmarkService.BuildUnionClaimManifest(new[]
        {
            Contribution(BenchmarkPanelMember.A, quote: Heals, basis: BasisA),
            Contribution(BenchmarkPanelMember.B, quote: Heals, basis: BasisA)
        }, AnswerText);

        Assert.Equal(new[] { Heals, BasisA }, manifest.Select(m => m.Text));
        Assert.Equal(new[] { "A", "B" }, manifest[0].RaisedBy);
        Assert.Equal(new[] { "A", "B" }, manifest[1].RaisedBy);
    }

    [Fact]
    public void UnionManifest_BsQuoteEqualToAsUnverifiedClaim_TakesItsPlace_RaisedByBoth()
    {
        var manifest = BenchmarkService.BuildUnionClaimManifest(new[]
        {
            Contribution(BenchmarkPanelMember.A, new[] { Spelltool, NoCharges }),
            Contribution(BenchmarkPanelMember.B, quote: NoCharges)
        }, AnswerText);

        Assert.Equal(new[] { Spelltool, NoCharges }, manifest.Select(m => m.Text));
        Assert.Equal(new[] { BenchmarkClaimRoles.CriticalErrorQuote }, manifest[1].Roles);
        Assert.Equal(new[] { "A", "B" }, manifest[1].RaisedBy);
    }

    [Fact]
    public void UnionManifest_ALabeledAndAnUnlabeledCopy_AreOneItemRaisedByBoth()
    {
        const string labeled = "- **Healing:** Applying it heals 1000 hit points and restores 500 mana.";
        var manifest = BenchmarkService.BuildUnionClaimManifest(new[]
        {
            Contribution(BenchmarkPanelMember.A, new[] { labeled, Spelltool }),
            Contribution(BenchmarkPanelMember.B, new[] { Heals, "`the grail of healing` is a *spelltool*." })
        }, AnswerText);

        // The list marker, the bold label, emphasis, code spans and case do not make a second item.
        Assert.Equal(new[] { labeled, Spelltool }, manifest.Select(m => m.Text));
        Assert.All(manifest, m => Assert.Equal(new[] { BenchmarkClaimRoles.UnverifiedClaim }, m.Roles));
        Assert.All(manifest, m => Assert.Equal(new[] { "A", "B" }, m.RaisedBy));
    }

    [Fact]
    public void PanelOutcome_BsLabeledQuoteMergedIntoAsClaim_KeepsBsContestedFlag()
    {
        const string labeledQuote = "**Charges:** It has no charges and never runs out.";
        var manifest = BenchmarkService.BuildUnionClaimManifest(new[]
        {
            Contribution(BenchmarkPanelMember.A, new[] { Spelltool, NoCharges }),
            Contribution(BenchmarkPanelMember.B, quote: labeledQuote)
        }, AnswerText);

        Assert.Equal(new[] { Spelltool, NoCharges }, manifest.Select(m => m.Text));
        Assert.Equal(new[] { BenchmarkClaimRoles.CriticalErrorQuote }, manifest[1].Roles);
        Assert.Equal(new[] { "A", "B" }, manifest[1].RaisedBy);

        var verifications = BenchmarkService.StampRoles(new[]
        {
            new BenchmarkClaimVerification(0, Spelltool, BenchmarkClaimVerdict.Supported, "src/objects.c:10", "True."),
            new BenchmarkClaimVerification(1, NoCharges, BenchmarkClaimVerdict.Supported, "src/objects.c:2889", "It has no charges.")
        }, manifest);

        var answer = new BenchmarkRunAnswer
        {
            AnswerText = AnswerText,
            Status = BenchmarkAnswerStatus.Ok,
            AssessmentStatus = BenchmarkAssessmentStatus.Scored,
            AccuracyLevel = 5,
            CompletenessLevel = 5,
            ConcisenessLevel = 5,
            ReadabilityLevel = 5,
            QualityScore = 80,
            CriticalError = false,
            CoAssessmentStatus = BenchmarkAssessmentStatus.Scored,
            CoAssessmentQualityScore = 25,
            CoAssessmentCriticalError = true,
            CoAssessmentJson = new BenchmarkCoAssessmentRecord
            {
                AccuracyLevel = 2,
                CompletenessLevel = 4,
                ConcisenessLevel = 5,
                ReadabilityLevel = 5,
                CriticalError = true,
                CriticalErrorQuote = labeledQuote,
                QualityScore = 25,
                Flags = new BenchmarkCoAssessmentFlags()
            }.Serialize()
        };

        BenchmarkService.ApplyPanelClaimVerificationOutcome(
            answer, verifications, BenchmarkVerdictView.FromPrimary(answer), BenchmarkVerdictView.FromCoAssessment(answer));

        // Member B's quote is the item member A listed without the label; the verifier supported it.
        var record = BenchmarkCoAssessmentRecord.Parse(answer.CoAssessmentJson)!;
        Assert.True(record.Flags!.ContestedCriticalError);
        Assert.False(((BenchmarkAnswerFlags)answer.AnswerFlags).HasFlag(BenchmarkAnswerFlags.ContestedCriticalError));
        Assert.True(BenchmarkService.CriticalErrorQuoteWasSupported(verifications, labeledQuote));
    }

    [Fact]
    public void UnionManifest_ASentenceBothMembersCharged_UnionsTheFragmentsAndTheCharges()
    {
        var manifest = BenchmarkService.BuildUnionClaimManifest(new[]
        {
            Contribution(BenchmarkPanelMember.A, accused: new[]
            {
                new BenchmarkService.AccusedQuote(NoCharges, "Under \"Using it\".", new[] { "no charges" }, "A: it has charges.")
            }),
            Contribution(BenchmarkPanelMember.B, accused: new[]
            {
                new BenchmarkService.AccusedQuote(NoCharges, "Under \"Using it\".", new[] { "never runs out", "No charges" }, "B: it runs out.")
            })
        }, AnswerText);

        var item = Assert.Single(manifest);
        Assert.Equal(new[] { BenchmarkClaimRoles.AccusedQuote }, item.Roles);
        Assert.Equal(new[] { "no charges", "never runs out" }, item.QuotedFragments);
        Assert.Equal("A: it has charges. B: it runs out.", item.Charge);
        Assert.Equal(new[] { "A", "B" }, item.RaisedBy);
    }

    [Fact]
    public void UnionManifest_OneMember_ListsWhatTheSingleAssessorManifestLists()
    {
        var accused = new[] { new BenchmarkService.AccusedQuote(NoCharges, "ctx") };
        var single = BenchmarkService.BuildClaimManifest(new[] { Spelltool, Regenerate }, Heals, BasisA, accused, answerText: AnswerText);
        var union = BenchmarkService.BuildUnionClaimManifest(new[]
        {
            Contribution(BenchmarkPanelMember.A, new[] { Spelltool, Regenerate }, Heals, BasisA, accused)
        }, AnswerText);

        Assert.Equal(single.Select(m => m.Text), union.Select(m => m.Text));
        Assert.Equal(single.Select(m => string.Join(",", m.Roles)), union.Select(m => string.Join(",", m.Roles)));
        Assert.All(single, m => Assert.Null(m.RaisedBy));
        Assert.All(union, m => Assert.Equal(new[] { "A" }, m.RaisedBy));
    }

    // Run 74, Q12: one member raised a sentence, the other a two-sentence item containing it.
    private const string Cheapest = "It's cheapest before you level up again.";
    private const string DonateAndCheapest = "Donate for protection now. It's cheapest before you level up again.";
    private const string DonationAnswerText = "Buy the divination first.\n\n" + DonateAndCheapest;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnionManifest_AClaimContainedInTheOtherMembersLongerClaim_IsOneItemRaisedByBoth(bool shortFromA)
    {
        var manifest = BenchmarkService.BuildUnionClaimManifest(new[]
        {
            Contribution(BenchmarkPanelMember.A, new[] { shortFromA ? Cheapest : DonateAndCheapest }),
            Contribution(BenchmarkPanelMember.B, new[] { shortFromA ? DonateAndCheapest : Cheapest })
        }, DonationAnswerText);

        var item = Assert.Single(manifest);
        Assert.Equal(DonateAndCheapest, item.Text);
        Assert.Equal(new[] { BenchmarkClaimRoles.UnverifiedClaim }, item.Roles);
        Assert.Equal(new[] { "A", "B" }, item.RaisedBy);
    }

    [Fact]
    public void UnionManifest_AContainedClaim_MergesWhateverItsMarkupAndCase_AndOtherItemsKeepTheirOrder()
    {
        var manifest = BenchmarkService.BuildUnionClaimManifest(new[]
        {
            Contribution(BenchmarkPanelMember.A, new[] { Spelltool, "- **it's CHEAPEST** before you level up again." }),
            Contribution(BenchmarkPanelMember.B, new[] { DonateAndCheapest, Regenerate })
        }, DonationAnswerText);

        Assert.Equal(new[] { Spelltool, DonateAndCheapest, Regenerate }, manifest.Select(m => m.Text));
        Assert.Equal(new[] { "A" }, manifest[0].RaisedBy);
        Assert.Equal(new[] { "A", "B" }, manifest[1].RaisedBy);
        Assert.Equal(new[] { "B" }, manifest[2].RaisedBy);
    }

    [Fact]
    public void UnionManifest_AnAccusedSentenceContainedInAClaim_IsNotMerged()
    {
        var manifest = BenchmarkService.BuildUnionClaimManifest(new[]
        {
            Contribution(BenchmarkPanelMember.A, new[] { DonateAndCheapest }),
            Contribution(BenchmarkPanelMember.B, accused: new[]
            {
                new BenchmarkService.AccusedQuote(Cheapest, null, new[] { "cheapest" }, "B: the price does not change.")
            })
        }, DonationAnswerText);

        Assert.Equal(new[] { DonateAndCheapest, Cheapest }, manifest.Select(m => m.Text));
        Assert.Equal(new[] { BenchmarkClaimRoles.UnverifiedClaim }, manifest[0].Roles);
        Assert.Equal(new[] { BenchmarkClaimRoles.AccusedQuote }, manifest[1].Roles);
        Assert.Equal(new[] { "A" }, manifest[0].RaisedBy);
        Assert.Equal(new[] { "B" }, manifest[1].RaisedBy);
    }

    [Fact]
    public void UnionManifest_ACriticalErrorQuoteContainedInAClaim_IsNotMerged()
    {
        var manifest = BenchmarkService.BuildUnionClaimManifest(new[]
        {
            Contribution(BenchmarkPanelMember.A, new[] { DonateAndCheapest }),
            Contribution(BenchmarkPanelMember.B, quote: Cheapest)
        }, DonationAnswerText);

        Assert.Equal(2, manifest.Count);
        var quote = Assert.Single(manifest, m => m.Roles.Contains(BenchmarkClaimRoles.CriticalErrorQuote));
        Assert.Equal(Cheapest, quote.Text);
        Assert.Equal(new[] { "B" }, quote.RaisedBy);
        var claim = Assert.Single(manifest, m => m.Roles.Contains(BenchmarkClaimRoles.UnverifiedClaim));
        Assert.Equal(DonateAndCheapest, claim.Text);
        Assert.Equal(new[] { "A" }, claim.RaisedBy);
    }

    [Fact]
    public void UnionManifest_AnAccusedSentenceContainedInTheOtherMembersLongerOne_IsOneItemWithBothChargesAndFragments()
    {
        var manifest = BenchmarkService.BuildUnionClaimManifest(new[]
        {
            Contribution(BenchmarkPanelMember.A, accused: new[]
            {
                new BenchmarkService.AccusedQuote(Cheapest, null, new[] { "cheapest" }, "A: the price does not change.")
            }),
            Contribution(BenchmarkPanelMember.B, accused: new[]
            {
                new BenchmarkService.AccusedQuote(DonateAndCheapest, "Preceded by: \"Buy the divination first.\"", new[] { "Donate for protection" }, "B: donate later.")
            })
        }, DonationAnswerText);

        var item = Assert.Single(manifest);
        Assert.Equal(DonateAndCheapest, item.Text);
        Assert.Equal(new[] { BenchmarkClaimRoles.AccusedQuote }, item.Roles);
        Assert.Equal("Preceded by: \"Buy the divination first.\"", item.Context);
        Assert.Equal(new[] { "Donate for protection", "cheapest" }, item.QuotedFragments);
        Assert.Equal("B: donate later. A: the price does not change.", item.Charge);
        Assert.Equal(new[] { "A", "B" }, item.RaisedBy);
    }

    [Fact]
    public void UnionManifest_ClaimsOfDifferentAnswers_AreNotMerged()
    {
        const string otherAnswerText = "Pray first. " + Cheapest;
        var first = BenchmarkService.BuildUnionClaimManifest(new[]
        {
            Contribution(BenchmarkPanelMember.A, new[] { Cheapest })
        }, otherAnswerText);
        var second = BenchmarkService.BuildUnionClaimManifest(new[]
        {
            Contribution(BenchmarkPanelMember.B, new[] { DonateAndCheapest })
        }, DonationAnswerText);

        Assert.Equal(Cheapest, Assert.Single(first).Text);
        Assert.Equal(new[] { "A" }, first[0].RaisedBy);
        Assert.Equal(DonateAndCheapest, Assert.Single(second).Text);
        Assert.Equal(new[] { "B" }, second[0].RaisedBy);
    }

    [Fact]
    public void StampRoles_CarriesRaisedByFromAUnionManifest_AndNoneFromASingleAssessorManifest()
    {
        var union = BenchmarkService.BuildUnionClaimManifest(new[]
        {
            Contribution(BenchmarkPanelMember.A, new[] { Spelltool }),
            Contribution(BenchmarkPanelMember.B, new[] { Spelltool, Regenerate })
        }, AnswerText);
        var stamped = BenchmarkService.StampRoles(new[]
        {
            new BenchmarkClaimVerification(0, Spelltool, BenchmarkClaimVerdict.Supported, "src/objects.c:10", "True."),
            new BenchmarkClaimVerification(1, Regenerate, BenchmarkClaimVerdict.Refuted, "src/attrib.c:20", "False.")
        }, union);

        Assert.Equal(new[] { "A", "B" }, stamped[0].RaisedBy);
        Assert.Equal(new[] { "B" }, stamped[1].RaisedBy);
        Assert.Contains("\"raisedBy\":[\"A\",\"B\"]", JsonSerializer.Serialize(stamped));

        var single = BenchmarkService.BuildClaimManifest(new[] { Spelltool }, null, null, null);
        var singleStamped = Assert.Single(BenchmarkService.StampRoles(new[]
        {
            new BenchmarkClaimVerification(0, Spelltool, BenchmarkClaimVerdict.Supported, "src/objects.c:10", "True.")
        }, single));
        Assert.Null(singleStamped.RaisedBy);
        Assert.DoesNotContain("raisedBy", JsonSerializer.Serialize(new[] { singleStamped }));
    }

    [Fact]
    public void RaisedByMembers_KeepsEachMembersItems_AndAnItemWithoutProvenanceForBoth()
    {
        var verifications = new[]
        {
            new BenchmarkClaimVerification(0, "a", BenchmarkClaimVerdict.Supported, "x", null) { RaisedBy = new[] { "A" } },
            new BenchmarkClaimVerification(1, "b", BenchmarkClaimVerdict.Supported, "x", null) { RaisedBy = new[] { "B" } },
            new BenchmarkClaimVerification(2, "ab", BenchmarkClaimVerdict.Supported, "x", null) { RaisedBy = new[] { "A", "B" } },
            new BenchmarkClaimVerification(3, "legacy", BenchmarkClaimVerdict.Supported, "x", null)
        };

        Assert.Equal(new[] { "a", "ab", "legacy" }, BenchmarkService.RaisedByMembers(verifications, BenchmarkPanelMember.A).Select(v => v.Claim));
        Assert.Equal(new[] { "b", "ab", "legacy" }, BenchmarkService.RaisedByMembers(verifications, BenchmarkPanelMember.B).Select(v => v.Claim));
        Assert.Equal(4, BenchmarkService.RaisedByMembers(verifications, BenchmarkPanelMember.Both).Count);
    }

    // One member accuses a sentence the other only lists as an unverified claim.
    [Fact]
    public void UnionManifest_AnAccusationIsTheAccusersAlone_WhenTheOtherMemberListedTheSentenceAsAPlainClaim()
    {
        var manifest = BenchmarkService.BuildUnionClaimManifest(new[]
        {
            Contribution(BenchmarkPanelMember.A, accused: new[]
            {
                new BenchmarkService.AccusedQuote(Spelltool, null, new[] { "spelltool" }, "A: it is a tool.")
            }),
            Contribution(BenchmarkPanelMember.B, new[] { Spelltool })
        }, AnswerText);

        var item = Assert.Single(manifest);
        Assert.Equal(new[] { BenchmarkClaimRoles.UnverifiedClaim, BenchmarkClaimRoles.AccusedQuote }, item.Roles);
        Assert.Equal(new[] { "A", "B" }, item.RaisedBy);
        Assert.Equal(new[] { "A" }, item.AccusedBy);
        Assert.Null(item.SuspectedBy);
    }

    [Fact]
    public void UnionManifest_ASuspectedFalseEntry_RecordsOnlyItsMemberInSuspectedBy()
    {
        var manifest = BenchmarkService.BuildUnionClaimManifest(new[]
        {
            Contribution(BenchmarkPanelMember.A, new[] { Spelltool }),
            Contribution(BenchmarkPanelMember.B, new[] { "Suspected false: " + Spelltool + " — it is a relic." })
        }, AnswerText);

        var item = Assert.Single(manifest);
        Assert.Equal(Spelltool, item.Text);
        Assert.True(item.SuspectedFalse);
        Assert.Equal(new[] { "A", "B" }, item.RaisedBy);
        Assert.Equal(new[] { "B" }, item.SuspectedBy);
        Assert.Null(item.AccusedBy);

        // A plain unverified claim raises the item and nothing more.
        var plain = Assert.Single(BenchmarkService.BuildUnionClaimManifest(new[]
        {
            Contribution(BenchmarkPanelMember.A, new[] { Spelltool })
        }, AnswerText));
        Assert.Equal(new[] { "A" }, plain.RaisedBy);
        Assert.Null(plain.AccusedBy);
        Assert.Null(plain.SuspectedBy);
    }

    [Fact]
    public void MergeContainedItems_UnitesTheRaisedAccusedAndSuspectedSetsSeparately()
    {
        var merged = BenchmarkService.MergeContainedItems(new List<BenchmarkService.ClaimSubmission>
        {
            new(DonateAndCheapest, new[] { BenchmarkClaimRoles.AccusedQuote }, null) { RaisedBy = new[] { "B" }, AccusedBy = new[] { "B" } },
            new(Cheapest, new[] { BenchmarkClaimRoles.AccusedQuote }, null) { RaisedBy = new[] { "A" }, AccusedBy = new[] { "A" } },
            new(NoCharges, new[] { BenchmarkClaimRoles.UnverifiedClaim }, null) { RaisedBy = new[] { "A" } },
            new("It has no charges", new[] { BenchmarkClaimRoles.UnverifiedClaim }, null)
            {
                RaisedBy = new[] { "B" },
                SuspectedFalse = true,
                SuspectedBy = new[] { "B" }
            }
        });

        Assert.Equal(new[] { DonateAndCheapest, NoCharges }, merged.Select(m => m.Text));
        Assert.Equal(new[] { "A", "B" }, merged[0].RaisedBy);
        Assert.Equal(new[] { "A", "B" }, merged[0].AccusedBy);
        Assert.Null(merged[0].SuspectedBy);
        Assert.Equal(new[] { "A", "B" }, merged[1].RaisedBy);
        Assert.Null(merged[1].AccusedBy);
        Assert.Equal(new[] { "B" }, merged[1].SuspectedBy);
        Assert.True(merged[1].SuspectedFalse);
    }

    [Fact]
    public void StampRoles_CarriesAccusedByAndSuspectedBy_WhichRoundTripAndAreOmittedWhenNull()
    {
        var manifest = BenchmarkService.BuildUnionClaimManifest(new[]
        {
            Contribution(BenchmarkPanelMember.A, new[] { "Suspected false: " + Regenerate + " — racial regeneration does not vary." },
                accused: new[] { new BenchmarkService.AccusedQuote(Spelltool, null, new[] { "spelltool" }, "A: it is a tool.") }),
            Contribution(BenchmarkPanelMember.B, new[] { Spelltool, Regenerate })
        }, AnswerText);
        var stamped = BenchmarkService.StampRoles(manifest
            .Select((m, i) => new BenchmarkClaimVerification(i, m.Text, BenchmarkClaimVerdict.Supported, "src/objects.c:10", "True."))
            .ToList(), manifest);

        var accused = Assert.Single(stamped, v => v.Claim == Spelltool);
        var suspected = Assert.Single(stamped, v => v.Claim == Regenerate);
        Assert.Equal(new[] { "A" }, accused.AccusedBy);
        Assert.Null(accused.SuspectedBy);
        Assert.Equal(new[] { "A" }, suspected.SuspectedBy);
        Assert.Null(suspected.AccusedBy);

        string json = JsonSerializer.Serialize(stamped);
        Assert.Contains("\"accusedBy\":[\"A\"]", json);
        Assert.Contains("\"suspectedBy\":[\"A\"]", json);
        Assert.DoesNotContain("AccusingMembers", json);
        Assert.DoesNotContain("SuspectingMembers", json);

        var read = JsonSerializer.Deserialize<List<BenchmarkClaimVerification>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        Assert.Equal(new[] { "A" }, read.Single(v => v.Claim == Spelltool).AccusedBy);
        Assert.Equal(new[] { "A" }, read.Single(v => v.Claim == Regenerate).SuspectedBy);

        string plainJson = JsonSerializer.Serialize(new[] { new BenchmarkClaimVerification(0, Spelltool, BenchmarkClaimVerdict.Supported, "x", null) { RaisedBy = new[] { "A" } } });
        Assert.DoesNotContain("accusedBy", plainJson);
        Assert.DoesNotContain("suspectedBy", plainJson);
    }

    [Fact]
    public void AccusedAndSuspectedByMembers_ReadTheRolesOwnSet_AndFallBackToRaisedByOnALegacyRecord()
    {
        var verifications = new[]
        {
            new BenchmarkClaimVerification(0, "accusedByA", BenchmarkClaimVerdict.Supported, "x", null)
                { RaisedBy = new[] { "A", "B" }, AccusedBy = new[] { "A" } },
            new BenchmarkClaimVerification(1, "suspectedByB", BenchmarkClaimVerdict.Supported, "x", null)
                { RaisedBy = new[] { "A", "B" }, SuspectedBy = new[] { "B" } },
            new BenchmarkClaimVerification(2, "legacyPanel", BenchmarkClaimVerdict.Supported, "x", null)
                { RaisedBy = new[] { "B" } },
            new BenchmarkClaimVerification(3, "singleAssessor", BenchmarkClaimVerdict.Supported, "x", null)
        };

        Assert.Equal(new[] { "accusedByA", "suspectedByB", "singleAssessor" },
            BenchmarkService.AccusedByMembers(verifications, BenchmarkPanelMember.A).Select(v => v.Claim));
        Assert.Equal(new[] { "suspectedByB", "legacyPanel", "singleAssessor" },
            BenchmarkService.AccusedByMembers(verifications, BenchmarkPanelMember.B).Select(v => v.Claim));
        Assert.Equal(new[] { "accusedByA", "singleAssessor" },
            BenchmarkService.SuspectedByMembers(verifications, BenchmarkPanelMember.A).Select(v => v.Claim));
        Assert.Equal(new[] { "accusedByA", "suspectedByB", "legacyPanel", "singleAssessor" },
            BenchmarkService.SuspectedByMembers(verifications, BenchmarkPanelMember.B).Select(v => v.Claim));
    }

    /// <summary>A panel answer both members scored at the given Accuracy levels, with the given accuracy evidence.</summary>
    private static BenchmarkRunAnswer PanelAnswer(int accuracyA, string? evidenceA, int accuracyB, string? evidenceB)
        => new()
        {
            AnswerText = AnswerText,
            Status = BenchmarkAnswerStatus.Ok,
            AssessmentStatus = BenchmarkAssessmentStatus.Scored,
            AccuracyLevel = accuracyA,
            CompletenessLevel = 5,
            ConcisenessLevel = 5,
            ReadabilityLevel = 5,
            QualityScore = 70,
            AssessmentEvidenceJson = evidenceA == null ? null : JsonSerializer.Serialize(new { accuracy = evidenceA }),
            CoAssessmentStatus = BenchmarkAssessmentStatus.Scored,
            CoAssessmentQualityScore = 70,
            CoAssessmentCriticalError = false,
            CoAssessmentJson = new BenchmarkCoAssessmentRecord
            {
                AccuracyLevel = accuracyB,
                CompletenessLevel = 5,
                ConcisenessLevel = 5,
                ReadabilityLevel = 5,
                QualityScore = 70,
                AccuracyEvidence = evidenceB,
                Flags = new BenchmarkCoAssessmentFlags()
            }.Serialize()
        };

    [Fact]
    public void PanelOutcome_ASupportedAccusationContestsOnlyTheAccuser_NotTheMemberThatListedTheSentence()
    {
        var manifest = BenchmarkService.BuildUnionClaimManifest(new[]
        {
            Contribution(BenchmarkPanelMember.A, accused: new[]
            {
                new BenchmarkService.AccusedQuote(Spelltool, null, new[] { "spelltool" }, "A: it is a tool.")
            }),
            Contribution(BenchmarkPanelMember.B, new[] { Spelltool })
        }, AnswerText);
        var verifications = BenchmarkService.StampRoles(new[]
        {
            new BenchmarkClaimVerification(0, Spelltool, BenchmarkClaimVerdict.Supported, "src/objects.c:10", "It is a spelltool.")
        }, manifest);
        var answer = PanelAnswer(4, Evidence("spelltool"), 5, null);

        BenchmarkService.ApplyPanelClaimVerificationOutcome(
            answer, verifications, BenchmarkVerdictView.FromPrimary(answer), BenchmarkVerdictView.FromCoAssessment(answer));

        Assert.True(((BenchmarkAnswerFlags)answer.AnswerFlags).HasFlag(BenchmarkAnswerFlags.ContestedAccuracyDeduction));
        Assert.False(BenchmarkCoAssessmentRecord.Parse(answer.CoAssessmentJson)!.Flags!.ContestedAccuracyDeduction);
        Assert.Contains("\"accusedBy\":[\"A\"]", answer.ClaimVerificationJson);
    }

    [Fact]
    public void PanelOutcome_ASupportedSuspicionContestsOnlyTheSuspectingMember()
    {
        var manifest = BenchmarkService.BuildUnionClaimManifest(new[]
        {
            Contribution(BenchmarkPanelMember.A, new[] { Regenerate }),
            Contribution(BenchmarkPanelMember.B, new[] { "Suspected false: " + Regenerate + " — racial regeneration does not vary." })
        }, AnswerText);
        var verifications = BenchmarkService.StampRoles(new[]
        {
            new BenchmarkClaimVerification(0, Regenerate, BenchmarkClaimVerdict.Supported, "src/attrib.c:20", "It does.")
        }, manifest);
        // Both members dock Accuracy quoting the sentence; only member B recorded it as suspected false.
        string evidence = Evidence("regenerate hit points faster at night");
        var answer = PanelAnswer(4, evidence, 4, evidence);

        BenchmarkService.ApplyPanelClaimVerificationOutcome(
            answer, verifications, BenchmarkVerdictView.FromPrimary(answer), BenchmarkVerdictView.FromCoAssessment(answer));

        Assert.False(((BenchmarkAnswerFlags)answer.AnswerFlags).HasFlag(BenchmarkAnswerFlags.ContestedAccuracyDeduction));
        Assert.True(BenchmarkCoAssessmentRecord.Parse(answer.CoAssessmentJson)!.Flags!.ContestedAccuracyDeduction);
    }

    [Fact]
    public void PanelOutcome_ALegacyRecordWithoutAccusedBy_AttributesTheAccusationToEveryRaiser()
    {
        var verifications = new[]
        {
            new BenchmarkClaimVerification(0, Spelltool, BenchmarkClaimVerdict.Supported, "src/objects.c:10", "It is a spelltool.")
            {
                Roles = new[] { BenchmarkClaimRoles.UnverifiedClaim, BenchmarkClaimRoles.AccusedQuote },
                RaisedBy = new[] { "A", "B" }
            }
        };
        var answer = PanelAnswer(4, null, 5, null);

        BenchmarkService.ApplyPanelClaimVerificationOutcome(
            answer, verifications, BenchmarkVerdictView.FromPrimary(answer), BenchmarkVerdictView.FromCoAssessment(answer));

        Assert.True(((BenchmarkAnswerFlags)answer.AnswerFlags).HasFlag(BenchmarkAnswerFlags.ContestedAccuracyDeduction));
        Assert.True(BenchmarkCoAssessmentRecord.Parse(answer.CoAssessmentJson)!.Flags!.ContestedAccuracyDeduction);
    }

    [Fact]
    public void PanelOutcome_ASupportedQuoteContestsOnlyTheMemberThatRaisedIt()
    {
        var answer = new BenchmarkRunAnswer
        {
            AnswerText = AnswerText,
            Status = BenchmarkAnswerStatus.Ok,
            AssessmentStatus = BenchmarkAssessmentStatus.Scored,
            AccuracyLevel = 2,
            CompletenessLevel = 4,
            ConcisenessLevel = 5,
            ReadabilityLevel = 5,
            QualityScore = 25,
            CriticalError = true,
            CriticalErrorQuote = Heals,
            CoAssessmentStatus = BenchmarkAssessmentStatus.Scored,
            CoAssessmentQualityScore = 25,
            CoAssessmentCriticalError = true,
            CoAssessmentJson = new BenchmarkCoAssessmentRecord
            {
                AccuracyLevel = 2,
                CompletenessLevel = 4,
                ConcisenessLevel = 5,
                ReadabilityLevel = 5,
                CriticalError = true,
                CriticalErrorQuote = NoCharges,
                QualityScore = 25,
                Flags = new BenchmarkCoAssessmentFlags()
            }.Serialize()
        };
        var verifications = new[]
        {
            new BenchmarkClaimVerification(0, Heals, BenchmarkClaimVerdict.Refuted, "src/potion.c:40", "It heals less.")
                { Roles = new[] { BenchmarkClaimRoles.CriticalErrorQuote }, RaisedBy = new[] { "A" } },
            new BenchmarkClaimVerification(1, NoCharges, BenchmarkClaimVerdict.Supported, "src/objects.c:2889", "It has no charges.")
                { Roles = new[] { BenchmarkClaimRoles.CriticalErrorQuote }, RaisedBy = new[] { "B" } }
        };

        BenchmarkService.ApplyPanelClaimVerificationOutcome(
            answer, verifications, BenchmarkVerdictView.FromPrimary(answer), BenchmarkVerdictView.FromCoAssessment(answer));

        // Member A's charge was upheld; member B's quote is true, so B's critical error is contested.
        var flags = (BenchmarkAnswerFlags)answer.AnswerFlags;
        Assert.False(flags.HasFlag(BenchmarkAnswerFlags.ContestedCriticalError));
        var record = BenchmarkCoAssessmentRecord.Parse(answer.CoAssessmentJson)!;
        Assert.True(record.Flags!.ContestedCriticalError);
        Assert.False(record.Flags.ContestedAccuracyDeduction);

        // Neither quote is an ordinary claim, so neither is counted.
        Assert.Equal(0, answer.ClaimsSupportedCount);
        Assert.Equal(0, answer.ClaimsRefutedCount);
        Assert.Contains("\"raisedBy\":[\"B\"]", answer.ClaimVerificationJson);
    }

    private static BenchmarkService CreateService(Dictionary<string, string?> settings)
    {
        var services = new ServiceCollection();
        var scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        return new BenchmarkService(
            scopeFactory, null!, null!, null!, new BenchmarkRunManager(), new BenchmarkDifficultyJobManager(),
            new BenchmarkScoringProfileService(scopeFactory, NullLogger<BenchmarkScoringProfileService>.Instance),
            new EndpointPolicy(configuration),
            configuration,
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
        var configuration = new ConfigurationBuilder().Build();
        var service = new BenchmarkService(
            scopeFactory, null!, null!, null!, runManager, new BenchmarkDifficultyJobManager(),
            new BenchmarkScoringProfileService(scopeFactory, NullLogger<BenchmarkScoringProfileService>.Instance),
            new EndpointPolicy(configuration),
            configuration,
            NullLogger<BenchmarkService>.Instance);
        return (service, runManager);
    }
}
