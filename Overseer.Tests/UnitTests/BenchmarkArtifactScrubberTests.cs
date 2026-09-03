using MobileGnollHackLogger.Data;
using Overseer.Services.Benchmarking;
using Xunit;

namespace Overseer.Tests.UnitTests;

/// <summary>
/// Every positive fixture here is taken from the 2026-09-03 GPT-5.6 Luna benchmark run, where
/// the model emitted tool invocations and reasoning narration into the text channel and the
/// harness graded them as authored prose.
///
/// The negative fixtures matter just as much: the worst possible failure of this class is
/// silently deleting real answer text, so authored content that merely resembles an artifact is
/// pinned here too.
/// </summary>
public class BenchmarkArtifactScrubberTests
{
    private static readonly BenchmarkArtifactScrubber Scrubber = BenchmarkArtifactScrubber.Default;

    // ---------------------------------------------------------------------------------------
    // Q7 — narration, repeated four times, then a routing marker and two bare payloads.
    // ---------------------------------------------------------------------------------------

    private const string Q7Answer =
        "Assuming your spellcast succeeds, GnollHack’s **Fear** has two defenses:\n\n" +
        "- **Automatic fear resistance:** the monster is unaffected if its species resists fear.\n" +
        "- **Wisdom saving throw:** otherwise it rolls a save using **Wisdom**.";

    private const string Q7Raw =
        "GnollHack’s code shows a two-stage check—fear immunity first, then a Wisdom saving " +
        "throw—so I’m checking the save formula and the spell’s built-in modifier to state " +
        "precisely what affects the roll." +
        "I’m also locating the shared saving-throw routine and the fear spell’s data entry; " +
        "those determine whether “resists” means an immunity or a failed Wisdom save." +
        "I’m locating the shared saving-throw routine and the fear spell’s data entry; those " +
        "determine whether “resists” means an immunity or a failed Wisdom save." +
        "I’m checking the shared saving-throw routine and the spell’s data entry so I can " +
        "distinguish a built-in immunity from a failed Wisdom save.\n\n" +
        "I’m locating the shared saving-throw routine and the fear spell’s data entry; those " +
        "determine whether “resists” means an immunity or a failed Wisdom save. " +
        "to=multi_tool_use.parallel code\n" +
        "{\"tool_uses\":[{\"recipient_name\":\"functions.source_code_search\",\"parameters\":" +
        "{\"query\":\"check_ability_resistance_success\",\"repository\":\"gnollhack\"}}]}\n\n" +
        "  code\n" +
        "{\"query\":\"Fear spell\",\"category\":\"spell\",\"max_results\":10}\n\n" +
        Q7Answer;

    [Fact]
    public void Q7_LeakedPayloadsAndNarration_RemovedLeavingAuthoredAnswer()
    {
        var result = Scrubber.Scrub(Q7Raw);

        Assert.Equal(Q7Answer, result.AnswerText);
        Assert.DoesNotContain("to=", result.AnswerText);
        Assert.DoesNotContain("tool_uses", result.AnswerText);
        Assert.DoesNotContain("I’m checking", result.AnswerText);
        Assert.True(result.Flags.HasFlag(BenchmarkAnswerFlags.HarnessArtifacts));
        Assert.True(result.Flags.HasFlag(BenchmarkAnswerFlags.ReasoningBleed));
    }

    [Fact]
    public void Q7_RemovedTextIsRetainedForAudit()
    {
        var result = Scrubber.Scrub(Q7Raw);

        Assert.NotNull(result.ArtifactText);
        Assert.Contains("tool_uses", result.ArtifactText);
        Assert.Contains("saving-throw routine", result.ArtifactText);
    }

    [Fact]
    public void Q7_FourfoldRepeatedNarration_FlaggedAsRepeatedFragments()
    {
        var result = Scrubber.Scrub(Q7Raw);

        Assert.True(result.Flags.HasFlag(BenchmarkAnswerFlags.RepeatedFragments));
    }

    // ---------------------------------------------------------------------------------------
    // Q13 — channel literals (`code:`, `/json`) and a garbage token between payloads.
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void Q13_ChannelLiteralsAndGarbageTokens_Removed()
    {
        const string answer =
            "## Red dragon — complete combat profile\n\n" +
            "This is the **ordinary red dragon**. Treat it primarily as an **8-square ranged " +
            "fire threat**; closing with it suppresses the breath attack.";

        string raw =
            "The combat code adds two important details: the fire breath has an 8-square " +
            "line-up range. I’m checking the exact recovery timing before summarizing it." +
            "I’m verifying whether “8-square range” means a blocked beam or a broader area " +
            "effect, so the positioning advice is precise.  code:\n" +
            "{\"symbol\":\"lined_up\",\"kind\":\"function\",\"repository\":\"gnollhack\"} unerquicklich  code:\n" +
            "{\"symbol\":\"lined_up\",\"kind\":\"function\",\"repository\":\"gnollhack\"}   /json\n" +
            "{\"name\":\"lined_up\",\"type\":\"function\",\"start_line\":0,\"repository\":\"gnollhack\"}\n\n" +
            "I’m checking one last combat flag—thick hide.  code:\n" +
            "{\"query\":\"thick_skinned\",\"file_filter\":\"src\",\"max_results\":20,\"repository\":\"gnollhack\"}\n\n" +
            answer;

        var result = Scrubber.Scrub(raw);

        Assert.Equal(answer, result.AnswerText);
        Assert.DoesNotContain("unerquicklich", result.AnswerText);
        Assert.DoesNotContain("lined_up", result.AnswerText);
        Assert.DoesNotContain("/json", result.AnswerText);
        Assert.True(result.Flags.HasFlag(BenchmarkAnswerFlags.HarnessArtifacts));
    }

    // ---------------------------------------------------------------------------------------
    // Q16 — bracketed channel literals, non-Latin garbage, and a hallucinated parameter name
    // that no registered tool schema declares.
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void Q16_BracketedLiteralsAndNonLatinGarbage_Removed()
    {
        const string answer =
            "GnollHack does **not** use one formula for both properties. It first chooses a " +
            "monster appropriate to the effective dungeon difficulty, then assigns the spawned " +
            "monster its runtime level.\n\n" +
            "### Monster-selection scaling\n\n" +
            "Let `P = u.ulevel` and `D = level_difficulty()`.";

        string raw =
            "The code has two separate mechanisms: `adj_lev()` sets an individual monster’s " +
            "runtime level. I’m tracing the encounter-selection functions now." +
            "I’m checking the encounter data structures next.   (jsonത്തിനു? no)\n\n" +
            "  rsat\n" +
            "{\"file\":\"include/encounter.h\",\"start_line\":1,\"line_count\":120,\"search_term\":\"\",\"repository\":\"gnollhack\"}   อาคารจีเอ็มเอ็ม\n" +
            "{\"file\":\"include/encounter.h\",\"start_line\":1,\"line_count\":120,\"search_term\":\"\",\"repository\":\"gnollhack\"}   (json)\n\n" +
            "   (json)\n" +
            "{\"function_name\":\"get_encounter_monster_count\",\"repository\":\"gnollhack\"}\n\n" +
            "   (commentary code)\n" +
            "{\"query\":\"create_monster_or_encounter\",\"file_filter\":\"src\",\"max_results\":50,\"repository\":\"gnollhack\"}\n\n" +
            answer;

        var result = Scrubber.Scrub(raw);

        Assert.Equal(answer, result.AnswerText);
        Assert.DoesNotContain("อาคาร", result.AnswerText);
        Assert.DoesNotContain("rsat", result.AnswerText);
        Assert.DoesNotContain("encounter.h", result.AnswerText);
        Assert.DoesNotContain("function_name", result.AnswerText);
    }

    [Fact]
    public void HallucinatedParameterName_StillDetected()
    {
        // `function_name` is declared by no registered tool. A vocabulary derived purely from
        // tool schemas would have let this payload through, which is why the distinctive-key
        // set is pinned and test-guarded rather than reflected at runtime.
        string raw =
            "{\"function_name\":\"get_encounter_monster_count\",\"repository\":\"gnollhack\"}\n\n" +
            "### Answer\n\nThe encounter count is computed from the player level and depth, " +
            "scaled by the effective dungeon difficulty of the current level.";

        var result = Scrubber.Scrub(raw);

        Assert.StartsWith("### Answer", result.AnswerText);
        Assert.True(result.Flags.HasFlag(BenchmarkAnswerFlags.HarnessArtifacts));
    }

    // ---------------------------------------------------------------------------------------
    // Q2 / Q17 — narration with no JSON payload at all. These went completely unflagged before,
    // because the old detector had no rule that did not involve JSON or a `to=` marker.
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void Q2_NarrationPrefixWithNoPayload_MovedOutOfAnswer()
    {
        const string answer =
            "Weapon quality multiplies the weapon’s **base damage**—its normal damage dice and " +
            "base flat bonus—not its enchantment or every other damage bonus.";

        string raw =
            "The code confirms the damage multipliers: Exceptional doubles, Elite triples, and " +
            "all three planar qualities quadruple base damage. I’m checking the exact " +
            "alignment/creature restrictions so I can distinguish “cannot use” from merely " +
            "“less effective.”\n\n" +
            answer;

        var result = Scrubber.Scrub(raw);

        Assert.Equal(answer, result.AnswerText);
        Assert.True(result.Flags.HasFlag(BenchmarkAnswerFlags.ReasoningBleed));
        Assert.False(result.Flags.HasFlag(BenchmarkAnswerFlags.HarnessArtifacts));
        Assert.Equal(0, result.ArtifactBlockCount);
    }

    [Fact]
    public void Q17_RunTogetherNarration_MovedOutOfAnswer()
    {
        string raw =
            "The gift check is in `src/pray.c`: it is not a turn-based percentage. I’m " +
            "verifying what the artifact count includes so I can explain the progression " +
            "accurately.I’m verifying what the game counts as an “existing artifact,” since " +
            "that count is part of the gift denominator.I’m checking the definition of the " +
            "artifact-count helper now.\n\n" +
            "In GnollHack, a sacrifice gift is a chance to receive a divine artifact.";

        var result = Scrubber.Scrub(raw);

        Assert.StartsWith("In GnollHack, a sacrifice gift", result.AnswerText);
        Assert.DoesNotContain("I’m verifying", result.AnswerText);
        Assert.True(result.Flags.HasFlag(BenchmarkAnswerFlags.ReasoningBleed));
    }

    // ---------------------------------------------------------------------------------------
    // Negative fixtures — authored content that must survive untouched.
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void FencedJsonBlock_SurvivesUntouched()
    {
        string raw =
            "You call the tool like this:\n\n" +
            "```json\n" +
            "{\"query\":\"lined_up\",\"repository\":\"gnollhack\",\"max_results\":20}\n" +
            "```\n\n" +
            "The `repository` argument selects the codebase.";

        var result = Scrubber.Scrub(raw);

        Assert.Equal(raw, result.AnswerText);
        Assert.Equal(BenchmarkAnswerFlags.None, result.Flags);
        Assert.Null(result.ArtifactText);
    }

    [Fact]
    public void UnfencedJsonWithNoToolVocabulary_SurvivesUntouched()
    {
        string raw =
            "The saved configuration looks like this:\n\n" +
            "{\"enabled\":true,\"level\":3,\"nickname\":\"Rodney\"}\n\n" +
            "which the game reads at startup.";

        var result = Scrubber.Scrub(raw);

        Assert.Equal(raw, result.AnswerText);
        Assert.Equal(BenchmarkAnswerFlags.None, result.Flags);
    }

    [Fact]
    public void SingleGenericKey_IsNotEnoughToDeleteAnObject()
    {
        // `name` alone is an ordinary English word; deleting on one generic key would make the
        // scrubber unsafe on authored examples.
        string raw =
            "A monster record is keyed by name:\n\n" +
            "{\"name\":\"red dragon\"}\n\n" +
            "and looked up on demand.";

        var result = Scrubber.Scrub(raw);

        Assert.Equal(raw, result.AnswerText);
        Assert.Equal(BenchmarkAnswerFlags.None, result.Flags);
    }

    [Fact]
    public void FirstPersonOpeningThatIsNotInvestigationNarration_SurvivesUntouched()
    {
        string raw =
            "I would not melee a red dragon below level 14 without fire resistance, because " +
            "its breath alone can outpace your healing.\n\n" +
            "Here is the safer approach, step by step.";

        var result = Scrubber.Scrub(raw);

        Assert.Equal(raw, result.AnswerText);
        Assert.False(result.Flags.HasFlag(BenchmarkAnswerFlags.ReasoningBleed));
    }

    [Fact]
    public void LeadingParagraphWithMarkdownStructure_IsNeverTreatedAsNarration()
    {
        string raw =
            "- I’m checking the prayer timeout table below\n" +
            "- and the alignment record\n\n" +
            "Both matter for whether prayer is safe.";

        var result = Scrubber.Scrub(raw);

        Assert.Equal(raw, result.AnswerText);
        Assert.False(result.Flags.HasFlag(BenchmarkAnswerFlags.ReasoningBleed));
    }

    [Fact]
    public void NarrationParagraphWithNothingAfterIt_IsKept()
    {
        // If narration is all there is, removing it would leave an empty answer and destroy the
        // evidence of what the model actually said.
        string raw = "I’m checking the save formula now.";

        var result = Scrubber.Scrub(raw);

        Assert.Equal(raw, result.AnswerText);
        Assert.False(result.Flags.HasFlag(BenchmarkAnswerFlags.ReasoningBleed));
    }

    [Fact]
    public void TrailingPayload_DoesNotTruncateTheAuthoredAnswer()
    {
        // The structural narration rule only fires when substantial text follows the last
        // payload. A payload that trails at the end must remove itself and nothing more.
        string raw =
            "### Silver dragon scale mail\n\n" +
            "Base AC is 1, magic cancellation 4, and it grants both cold resistance and " +
            "reflection. The material is dragon hide, not silver metal, so it does not count " +
            "as silver for silver-damage purposes.\n\n" +
            "{\"query\":\"silver dragon scale mail\",\"repository\":\"gnollhack\"}";

        var result = Scrubber.Scrub(raw);

        Assert.Contains("Base AC is 1", result.AnswerText);
        Assert.Contains("dragon hide", result.AnswerText);
        Assert.DoesNotContain("{\"query\"", result.AnswerText);
        Assert.True(result.Flags.HasFlag(BenchmarkAnswerFlags.HarnessArtifacts));
    }

    [Fact]
    public void CleanAnswer_IsReturnedUnchangedWithNoFlags()
    {
        string raw =
            "Gnolls are a hyena-headed race with **+1 Dexterity** and **+1 Constitution**.\n\n" +
            "**Allowed roles:**\n- Barbarian\n- Caveman\n- Healer\n\n" +
            "They are immune to lycanthropy.";

        var result = Scrubber.Scrub(raw);

        Assert.Equal(raw, result.AnswerText);
        Assert.Equal(BenchmarkAnswerFlags.None, result.Flags);
        Assert.Null(result.ArtifactText);
        Assert.Equal(0, result.ArtifactBlockCount);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void NullOrEmpty_YieldsEmptyResult(string? input)
    {
        var result = Scrubber.Scrub(input);

        Assert.Equal(string.Empty, result.AnswerText);
        Assert.Null(result.ArtifactText);
        Assert.Equal(BenchmarkAnswerFlags.None, result.Flags);
    }

    // ---------------------------------------------------------------------------------------
    // Detection parity — the flags the report and UI key on.
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void HasArtifacts_DetectsWhatTheOldRegexSetMissed()
    {
        Assert.True(Scrubber.HasArtifacts("to=multi_tool_use.parallel {\"tool_uses\":[]}"));
        Assert.True(Scrubber.HasArtifacts("{\"function_name\":\"x\",\"repository\":\"gnollhack\"}"));
        Assert.True(Scrubber.HasArtifacts("text <|constrain|> more"));
        Assert.True(Scrubber.HasArtifacts(
            "{\"file\":\"include/encounter.h\",\n \"start_line\":1,\n \"repository\":\"gnollhack\"}"));

        Assert.False(Scrubber.HasArtifacts("A perfectly ordinary answer about red dragons."));
        Assert.False(Scrubber.HasArtifacts("```json\n{\"repository\":\"gnollhack\"}\n```"));
        Assert.False(Scrubber.HasArtifacts(null));
    }

    [Fact]
    public void MultiLinePayload_IsDetected()
    {
        // The old single-line regex could not see a payload spanning lines.
        string raw =
            "{\n  \"file\": \"src/encounter.c\",\n  \"start_line\": 1160,\n" +
            "  \"line_count\": 500,\n  \"repository\": \"gnollhack\"\n}\n\n" +
            "### Encounter scaling\n\nThe count derives from player level and depth, and is " +
            "clamped by MAX_ENCOUNTER_ATTACKING_MONSTERS.";

        var result = Scrubber.Scrub(raw);

        Assert.StartsWith("### Encounter scaling", result.AnswerText);
        Assert.True(result.Flags.HasFlag(BenchmarkAnswerFlags.HarnessArtifacts));
    }

    // ---------------------------------------------------------------------------------------
    // Adjacent-junk widening must not reach into authored prose.
    //
    // The structural narration rule removes everything before the final payload, so the only
    // case the widening serves is a payload trailing the answer — precisely where over-reach
    // would destroy real content.
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void MidLineJsonInProse_IsAuthoredContentAndSurvives()
    {
        // Position is half the evidence. A leaked payload always begins its own line; a brace
        // sitting inside a sentence is a writer showing an argument, even when the keys are
        // tool-shaped, so it is left alone.
        string raw =
            "### Source lookup\n\n" +
            "The answer depends on which codebase you search. Passing the wrong one silently " +
            "returns NetHack results instead of GnollHack ones, which is easy to miss.\n\n" +
            "The repository setting is {\"repository\":\"gnollhack\"} by default.";

        var result = Scrubber.Scrub(raw);

        Assert.Equal(raw, result.AnswerText);
        Assert.Equal(BenchmarkAnswerFlags.None, result.Flags);
    }

    [Fact]
    public void ProseBeforeATrailingPayload_IsNotConsumedAsJunk()
    {
        string raw =
            "### Source lookup\n\n" +
            "The answer depends on which codebase you search. Passing the wrong one silently " +
            "returns NetHack results instead of GnollHack ones, which is easy to miss.\n\n" +
            "{\"repository\":\"gnollhack\"}";

        var result = Scrubber.Scrub(raw);

        Assert.Contains("### Source lookup", result.AnswerText);
        Assert.Contains("which codebase you search", result.AnswerText);
        Assert.Contains("easy to miss.", result.AnswerText);
        Assert.DoesNotContain("\"gnollhack\"", result.AnswerText);
        Assert.True(result.Flags.HasFlag(BenchmarkAnswerFlags.HarnessArtifacts));
    }

    [Fact]
    public void ProseAfterATrailingPayload_IsNotConsumedAsJunk()
    {
        string raw =
            "### Source lookup\n\n" +
            "The answer depends on which codebase you search, and choosing wrong returns the " +
            "other game's results without any warning at all.\n\n" +
            "{\"repository\":\"gnollhack\"} selects the GnollHack tree.";

        var result = Scrubber.Scrub(raw);

        Assert.Contains("selects the GnollHack tree.", result.AnswerText);
        Assert.DoesNotContain("\"repository\"", result.AnswerText);
    }
}
