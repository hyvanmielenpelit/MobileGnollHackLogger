namespace Overseer.Services.Benchmarking;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using MobileGnollHackLogger.Data;

public static class BenchmarkReportBuilder
{
    private static string Inv(IFormattable value, string? format = null)
    {
        return value.ToString(format, CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// A per-claim (or other small per-unit) dollar figure, with enough precision to stay
    /// visible: two decimals at or above a cent, three decimals below it, and "&lt;$0.001" rather
    /// than a misleading "$0.000" for a non-zero figure that still rounds to nothing at three
    /// decimals. A genuinely zero figure prints "$0.00".
    /// </summary>
    private static string PerUnitCost(decimal amount)
    {
        if (amount <= 0m || amount >= 0.01m)
        {
            return $"${Inv(amount, "F2")}";
        }

        string threeDecimals = Inv(amount, "F3");
        return threeDecimals == "0.000" ? "<$0.001" : $"${threeDecimals}";
    }

    /// <summary>
    /// A Harness Cost role's token line: the always-present in/out pair, with cache read and cache
    /// creation appended only when non-zero, so a role that carries no cache activity reads exactly
    /// as an in/out-only line.
    /// </summary>
    private static string HarnessCostTokenLine(long input, long output, long cacheRead, long cacheCreation)
    {
        var extras = new List<string>();
        if (cacheRead > 0) extras.Add($"{Inv(cacheRead, "N0")} cache read");
        if (cacheCreation > 0) extras.Add($"{Inv(cacheCreation, "N0")} cache creation");
        string line = $"{Inv(input, "N0")} in / {Inv(output, "N0")} out";
        return extras.Count > 0 ? $"{line} ({string.Join(", ", extras)})" : line;
    }

    /// <summary>
    /// True when a stored <see cref="BenchmarkRunAnswer.ClaimVerificationError"/> is the
    /// deterministic token-budget skip written by
    /// <see cref="BenchmarkService.BenchmarkClaimVerificationNotCheckedReason"/> rather than a real
    /// verifier failure — no call was made for this answer, so it must not be reported as one.
    /// </summary>
    private static bool IsClaimVerificationBudgetNotChecked(string? claimVerificationError) =>
        claimVerificationError != null &&
        claimVerificationError.StartsWith(BenchmarkService.ClaimVerificationNotCheckedPrefix, StringComparison.Ordinal);

    /// <summary>
    /// What <see cref="Overseer.Services.Tools.ToolRegistry.GetParallelOverrideText"/> actually
    /// selects for a given mode, in report prose. <c>Enabled</c> loads no override file; the
    /// batching guidance in Overseer/ToolGuides/_policy.md still applies unchanged.
    /// </summary>
    private static string ParallelPolicyDescription(MobileGnollHackLogger.Data.ParallelExecutionMode mode) => mode switch
    {
        MobileGnollHackLogger.Data.ParallelExecutionMode.Disabled =>
            "selects Overseer/ToolGuides/_policy_parallel_disabled.md, so this is part of the prompt text.",
        MobileGnollHackLogger.Data.ParallelExecutionMode.OnRequest =>
            "selects Overseer/ToolGuides/_policy_parallel_on_request.md, so this is part of the prompt text.",
        _ =>
            "selects no override file; the batching guidance in Overseer/ToolGuides/_policy.md applies unchanged."
    };

    /// <summary>
    /// What is known about whether the candidate's request really carried the prompt and the board,
    /// from the harness version the run was stamped with. Null when there is nothing to state: a
    /// pre-29 run with no snapshot and a candidate whose provider was never affected.
    /// </summary>
    /// <remarks>
    /// This is a fact about the harness, read from the run row, not an interpretation of the run's
    /// answers. Before harness 29 nothing checked the wire, and on a snapshot suite a Google or
    /// Anthropic candidate received no board while an OpenAI candidate received no system prompt.
    /// </remarks>
    private static string? DeliveryStatement(BenchmarkRun run, bool hasGameSnapshot)
    {
        if (int.TryParse(run.HarnessVersion, out int harness) && harness >= 30)
        {
            // From harness 30 the sentence rests on the recorded probe, not on the version stamp.
            DateTime? preRun = run.CandidateDeliveryVerifiedAtUtc;

            // Before harness 33 a re-run overwrote the pre-run stamp; one taken after the re-run
            // began is the re-run's own and says nothing about the first question.
            if (harness < 33 && preRun.HasValue && run.RerunStartedAtUtc.HasValue && preRun.Value >= run.RerunStartedAtUtc.Value)
            {
                return $"prompt and board delivery verified against the provider request body before the re-run ({Stamp(preRun.Value)} UTC); the pre-run probe's stamp was overwritten by the re-run, as on every run before harness 33.";
            }

            string statement = preRun.HasValue
                ? $"prompt and board delivery verified against the provider request body before the first question ({Stamp(preRun.Value)} UTC)"
                : "not recorded — no pre-run delivery probe is on record for this run";
            if (run.RerunCandidateDeliveryVerifiedAtUtc.HasValue)
            {
                statement += $"; re-verified before the re-run ({Stamp(run.RerunCandidateDeliveryVerifiedAtUtc.Value)} UTC)";
            }
            return statement + ".";
        }

        if (harness >= 29)
        {
            return "prompt and board delivery verified against the provider request body before the first question.";
        }

        bool isOpenAi = (run.TestedModelSnapshot.Provider ?? string.Empty)
            .Contains("openai", StringComparison.OrdinalIgnoreCase);

        if (hasGameSnapshot)
        {
            return "not verified — before harness 29 a Google or Anthropic candidate did not receive the board and an OpenAI candidate did not receive this prompt.";
        }

        return isOpenAi
            ? "not verified — before harness 29 an OpenAI candidate did not receive this prompt."
            : null;
    }

    /// <summary>The run was graded against a game board.</summary>
    private static bool RunHasBoard(BenchmarkRun run) => !string.IsNullOrWhiteSpace(run.GameSnapshotSha256Used);

    /// <summary>
    /// One grading role's board delivery: how many of its verdicts on record carried the board
    /// (<c>Delivered</c>), of how many (<c>Total</c>), and which questions went without.
    /// </summary>
    internal sealed record BoardDeliveryFigure(string Role, int Delivered, int Total, IReadOnlyList<int> MissingQuestions);

    /// <summary>
    /// Board delivery per grading role, from the per-answer board-character columns. A role's total
    /// is every answer carrying that role's verdict or a recorded board figure, so a verdict whose
    /// prompt carried no board counts against it. A panel run adds member B as <c>co-assessor</c>.
    /// Empty for a run before harness 30, which recorded none of this.
    /// </summary>
    internal static IReadOnlyList<BoardDeliveryFigure> BoardDeliveryFigures(BenchmarkRun run, IReadOnlyList<BenchmarkRunAnswer> answers)
    {
        if (PredatesHarnessVersion(run, 30) || !int.TryParse(run.HarnessVersion, out _))
        {
            return Array.Empty<BoardDeliveryFigure>();
        }

        BoardDeliveryFigure Figure(string role, Func<BenchmarkRunAnswer, bool> hasVerdict, Func<BenchmarkRunAnswer, int?> chars)
        {
            var population = answers.Where(a => hasVerdict(a) || chars(a).HasValue).OrderBy(a => a.OrderIndex).ToList();
            var missing = population.Where(a => (chars(a) ?? 0) <= 0).Select(a => a.OrderIndex).ToList();
            return new BoardDeliveryFigure(role, population.Count - missing.Count, population.Count, missing);
        }

        bool isPanelRun = BenchmarkRunFinalizer.IsPanelRun(run);
        var figures = new List<BoardDeliveryFigure>
        {
            Figure("assessor",
                a => a.AssessmentStatus == BenchmarkAssessmentStatus.Scored && a.AssessedByModelConfigurationId.HasValue,
                a => a.AssessorBoardChars)
        };
        if (isPanelRun)
        {
            figures.Add(Figure("co-assessor", a => a.CoAssessmentStatus == BenchmarkAssessmentStatus.Scored, a => a.CoAssessorBoardChars));
        }
        figures.Add(Figure(isPanelRun ? "reference reader" : "second reader", a => a.SecondOpinionQualityScore.HasValue, a => a.SecondOpinionBoardChars));
        figures.Add(Figure("claim verifier", a => !string.IsNullOrWhiteSpace(a.ClaimVerificationJson), a => a.VerifierBoardChars));
        return figures;
    }

    /// <summary>
    /// The Delivery manifest's per-role line on a board suite: <c>Board delivered — assessor N of M
    /// graded, …</c>, each role looked up by name. Null when the run has no board or predates harness 30.
    /// </summary>
    internal static string? BoardDeliveryLine(BenchmarkRun run, IReadOnlyList<BenchmarkRunAnswer> answers)
    {
        if (!RunHasBoard(run)) return null;

        var figures = BoardDeliveryFigures(run, answers);
        if (figures.Count == 0) return null;

        BoardDeliveryFigure? Role(string role) => figures.FirstOrDefault(f => f.Role == role);
        var assessor = Role("assessor")!;
        var coAssessor = Role("co-assessor");
        var reader = Role("reference reader") ?? Role("second reader")!;
        var verifier = Role("claim verifier")!;
        string coAssessorPart = coAssessor != null
            ? $"co-assessor {Inv(coAssessor.Delivered)} of {Inv(coAssessor.Total)}, "
            : string.Empty;
        return $"Board delivered — assessor {Inv(assessor.Delivered)} of {Inv(assessor.Total)} graded, "
            + coAssessorPart
            + $"{reader.Role} {Inv(reader.Delivered)} of {Inv(reader.Total)}, "
            + $"claim verifier {Inv(verifier.Delivered)} of {Inv(verifier.Total)}; "
            + "synthesis: yes; difficulty assessment: digest (no map).";
    }

    /// <summary>
    /// A UTC timestamp in the report's own fixed shape. Needed because ":" in a custom format
    /// string is the culture's *time separator* rather than a literal, so an interpolated
    /// "{d:yyyy-MM-dd HH:mm:ss}" renders "08.30.00" under fi-FI — the culture these machines
    /// actually run. A report is an artifact that gets compared across machines; its timestamps
    /// have to look the same on all of them.
    /// </summary>
    private static string Stamp(DateTime value)
    {
        return value.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Whether this run was recorded before the harness version that added a field. False when the run
    /// carries no parseable version, because "unknown" is not evidence of age — and a missing figure on
    /// a current run has a current cause, usually a run that stopped early or a failed stage, which is
    /// what the reader needs told instead.
    /// </summary>
    private static bool PredatesHarnessVersion(BenchmarkRun run, int addedInVersion)
    {
        return int.TryParse(run.HarnessVersion, out int version) && version < addedInVersion;
    }

    /// <summary>
    /// Where the run's suite came from: a default suite (with its key and, when reliably known, its
    /// version), a custom suite, or "not recorded" for a run that predates <see cref="BenchmarkRun.DefaultSuiteKeyUsed"/>.
    ///
    /// <para>Unlike <see cref="PredatesHarnessVersion"/>, an absent or unparseable
    /// <see cref="BenchmarkRun.HarnessVersion"/> counts as "before harness 24" here rather than
    /// "not before" — this run has no <c>DefaultSuiteKeyUsed</c> column to read at all, so its
    /// origin genuinely was never recorded, which is a different fact from a harness-24-or-later
    /// run that recorded a null (a custom suite).</para>
    ///
    /// <para>The version is <see cref="BenchmarkRun.DefaultSuiteVersionUsed"/>, recorded at launch;
    /// a run made before it was recorded prints the key alone rather than guessing a version.</para>
    /// </summary>
    private static string SuiteOriginText(BenchmarkRun run)
    {
        bool harnessRecordsSuiteOrigin = int.TryParse(run.HarnessVersion, out int version) && version >= 24;
        if (!harnessRecordsSuiteOrigin)
        {
            return "not recorded (run before harness 24)";
        }

        if (string.IsNullOrWhiteSpace(run.DefaultSuiteKeyUsed))
        {
            return "custom suite";
        }

        string versionSuffix = run.DefaultSuiteVersionUsed.HasValue
            ? $" (v{run.DefaultSuiteVersionUsed.Value.ToString(CultureInfo.InvariantCulture)})"
            : string.Empty;

        return $"default suite `{run.DefaultSuiteKeyUsed}`{versionSuffix}";
    }

    /// <summary>
    /// The harness version that added <see cref="BenchmarkRunAnswer.UnverifiedClaimCount"/>. A
    /// constant rather than the current version: interpolating the latter made every run claim to
    /// predate the harness it ran under.
    /// </summary>
    private const int UnverifiedClaimsHarnessVersion = 12;

    // The top of the 0-6 assessment level scale, the ceiling an advisory recomputation raises a
    // level to.
    private const int MaxAssessmentLevel = 6;

    private static readonly Regex BlockedCallsRegex =
        new(@"\((\d+)\s+blocked by budget\)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Fraction of a question's tool call budget above which the question is reported as having
    // run under budget pressure, short of actually exhausting it.
    private const double BudgetPressureFraction = 0.90;

    // Below this speed target, a scoring profile is an interactive-latency profile, and a
    // high/max thinking candidate scores against it in a way that says more about the profile
    // than about the model.
    private const int InteractiveSpeedTargetMaxMs = 30000;

    /// <summary>
    /// The speed constants this run was actually scored with, read back from the run's own
    /// profile snapshot so a report always describes the run in front of it rather than
    /// today's default profile. Falls back to the defaults when a run carries no snapshot.
    /// </summary>
    private static BenchmarkScoringConstants ScoringConstantsOf(BenchmarkRun run)
    {
        return BenchmarkScoring.ConstantsFromSnapshot(run.ScoringProfileSnapshotJson);
    }

    // Same fence-splitting pattern as BenchmarkAnswerSanitizer.CodeBlockRegex: a "#" inside a
    // fenced block is a comment in someone's example, not a heading, and must not be rewritten.
    private static readonly Regex CodeFenceRegex = new(@"(```[\s\S]*?```)", RegexOptions.Compiled);

    // An ATX heading line: 1-6 "#" markers followed by whitespace or end of line. Group 1 is the
    // marker run, group 2 is everything after it (including the leading space, if any) so the
    // replacement can rebuild the line with a different marker length and identical content.
    private static readonly Regex AtxHeadingLineRegex =
        new(@"^(#{1,6})(?=\s|$)(.*)$", RegexOptions.Multiline | RegexOptions.Compiled);

    /// <summary>
    /// Demotes every ATX heading in a model's answer so it nests strictly under the report's own
    /// question heading. Model answers routinely emit their own top-level headings — on the
    /// 2026-09-03 run one answer opened with <c>## GnollHack's spell schools</c>, a sibling of
    /// the report's own <c>## 3. Questions and Replies</c>, and another opened with
    /// <c>### Available roles</c>, a sibling of the answer's own <c>### Question N</c> heading —
    /// which corrupts every outline view of the exported Markdown.
    ///
    /// Only fenced code blocks are protected; everything else outside a fence is a candidate.
    /// The shallowest heading level present is found first, and if it is already at or below
    /// <paramref name="minLevel"/> (i.e. deeper or equal), the text is returned byte-identical —
    /// this function only ever pushes headings deeper, never shallower.
    ///
    /// Setext headings (a line of text followed by a line of <c>===</c> or <c>---</c>) are
    /// deliberately out of scope: none appear in the corpus, and telling a setext underline apart
    /// from a Markdown table separator or a horizontal rule is a larger change with more ways to
    /// be wrong than this fix justifies.
    /// </summary>
    internal static string DemoteAnswerHeadings(string answerText, int minLevel)
    {
        if (string.IsNullOrEmpty(answerText))
        {
            return answerText;
        }

        var parts = CodeFenceRegex.Split(answerText);

        int shallowest = int.MaxValue;
        for (int i = 0; i < parts.Length; i += 2) // Even indices are outside fences.
        {
            foreach (Match m in AtxHeadingLineRegex.Matches(parts[i]))
            {
                int level = m.Groups[1].Value.Length;
                if (level < shallowest) shallowest = level;
            }
        }

        if (shallowest == int.MaxValue || shallowest >= minLevel)
        {
            // No heading found, or the shallowest one is already at or beyond minLevel.
            return answerText;
        }

        int shift = minLevel - shallowest;
        var sb = new StringBuilder();
        for (int i = 0; i < parts.Length; i++)
        {
            if (i % 2 == 1) // Inside a fenced code block: never rewritten.
            {
                sb.Append(parts[i]);
                continue;
            }

            sb.Append(AtxHeadingLineRegex.Replace(parts[i], m =>
            {
                int newLevel = Math.Min(6, m.Groups[1].Value.Length + shift);
                return new string('#', newLevel) + m.Groups[2].Value;
            }));
        }

        return sb.ToString();
    }

    /// <summary>
    /// Splits an answer's tool calls into the ones that executed and the ones the budget refused.
    /// <c>ToolCallCount</c> counts attempts, so printing it against the budget produced lines like
    /// "27 of 25 calls used" — a sentence that cannot be true. The blocked figure is recorded in
    /// the tool summary the executor writes.
    /// </summary>
    private static (int Executed, int Blocked) ToolCallSplit(BenchmarkRunAnswer answer)
    {
        int attempted = answer.ToolCallCount ?? 0;
        int blocked = answer.ToolCallsBlocked ?? 0;

        if (blocked == 0 && !string.IsNullOrWhiteSpace(answer.ToolCallSummary))
        {
            var match = BlockedCallsRegex.Match(answer.ToolCallSummary);
            if (match.Success && int.TryParse(match.Groups[1].Value, out int parsed))
            {
                blocked = parsed;
            }
        }

        blocked = Math.Clamp(blocked, 0, attempted);
        return (attempted - blocked, blocked);
    }

    /// <summary>
    /// Reads the assessor's stored evidence. Returns nulls for a run graded before evidence was
    /// collected, which is every run up to harness version 3.
    ///
    /// <c>Readability</c> is absent on a run graded before the stored blob carried a readability
    /// key, so a null there is the ordinary case for those runs rather than a malformed blob, and
    /// the FORM count falls back to the per-answer
    /// <see cref="BenchmarkRunAnswer.ReadabilityFormOnly"/> column for them.
    /// </summary>
    /// <summary>One top-level string property of a stored JSON blob; null when absent or malformed.</summary>
    private static string? ReadJsonString(string? json, string property)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;

        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.ValueKind == JsonValueKind.Object &&
                   doc.RootElement.TryGetProperty(property, out var value) &&
                   value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>The sentences the assessor charged as false that the verifier supported with a citation; empty on a legacy record.</summary>
    private static List<BenchmarkClaimVerification> SupportedAccusationsOf(BenchmarkRunAnswer answer)
        => BenchmarkService.SupportedAccusations(ClaimVerificationsOf(answer));

    /// <summary>Every verification item stored for an answer, with its roles; null when none is stored or the JSON is unreadable.</summary>
    private static List<BenchmarkClaimVerification>? ClaimVerificationsOf(BenchmarkRunAnswer answer)
    {
        if (string.IsNullOrWhiteSpace(answer.ClaimVerificationJson)) return null;
        try
        {
            return JsonSerializer.Deserialize<List<BenchmarkClaimVerification>>(
                answer.ClaimVerificationJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// The sentences of the answer the assessor quoted as false and the harness sent to the claim
    /// verifier, each with its verdict. Read by role, so a record stored without roles has none.
    /// </summary>
    internal static List<BenchmarkClaimVerification> AccusedSentencesOf(BenchmarkRunAnswer answer)
        => (ClaimVerificationsOf(answer) ?? new List<BenchmarkClaimVerification>())
            .Where(v => BenchmarkClaimRoles.HasRole(v, BenchmarkClaimRoles.AccusedQuote))
            .ToList();

    /// <summary>
    /// The statements of the assessor's own evidence the harness sent to the claim verifier, each with
    /// its verdict; a Refuted one means the assessor, not the answer, was wrong. None before harness 33.
    /// </summary>
    private static List<BenchmarkClaimVerification> AssessorStatementsOf(BenchmarkRunAnswer answer)
        => (ClaimVerificationsOf(answer) ?? new List<BenchmarkClaimVerification>())
            .Where(v => BenchmarkClaimRoles.HasRole(v, BenchmarkClaimRoles.AssessorStatement))
            .ToList();

    /// <summary>The answer sentences the assessor reported as <c>Suspected false:</c> (scoring method 12), each with its verdict.</summary>
    private static List<BenchmarkClaimVerification> SuspectedFalseClaimsOf(BenchmarkRunAnswer answer)
        => (ClaimVerificationsOf(answer) ?? new List<BenchmarkClaimVerification>())
            .Where(v => v.SuspectedFalse == true)
            .ToList();

    /// <summary>
    /// The panel member(s) that submitted an item: <c>member A</c>, <c>member B</c> or <c>both
    /// members</c>. Null when <see cref="BenchmarkClaimVerification.RaisedBy"/> is null (a
    /// single-assessor run or a record before harness 40), where the report says "the assessor".
    /// </summary>
    internal static string? MemberPhrase(BenchmarkClaimVerification v) => MemberPhrase(v.RaisedBy);

    /// <summary><see cref="MemberPhrase(BenchmarkClaimVerification)"/> for a set of member labels; null for a null set.</summary>
    internal static string? MemberPhrase(IReadOnlyList<string>? members)
    {
        if (members == null) return null;
        bool a = members.Contains("A");
        bool b = members.Contains("B");
        return a && b ? "both members" : a ? "member A" : b ? "member B" : null;
    }

    /// <summary>Who charged an accused sentence: the phrase of <see cref="BenchmarkClaimVerification.AccusingMembers"/>, else "the assessor".</summary>
    private static string AccusedByText(BenchmarkClaimVerification v) => MemberPhrase(v.AccusingMembers) ?? "the assessor";

    /// <summary>Who recorded a sentence as suspected false: the phrase of <see cref="BenchmarkClaimVerification.SuspectingMembers"/>, else "the assessor".</summary>
    private static string SuspectedByText(BenchmarkClaimVerification v) => MemberPhrase(v.SuspectingMembers) ?? "the assessor";

    /// <summary>The possessive of the member phrase of the members that raised an item: "member A's", "both members'" or "the assessor's".</summary>
    private static string ChargedByPossessive(BenchmarkClaimVerification v) => MemberPhrase(v) switch
    {
        null => "the assessor's",
        "both members" => "both members'",
        string phrase => phrase + "'s"
    };

    /// <summary>
    /// A panel run's split of items by the member(s) that raised them, <c>A n, B m, both k</c>: A and B
    /// count the items that member raised alone, and an item without <c>raisedBy</c> counts as both.
    /// With <paramref name="membersOf"/>, the items are split by that member set instead of
    /// <c>raisedBy</c>, such as <see cref="BenchmarkClaimVerification.AccusingMembers"/>.
    /// </summary>
    internal static string MemberSplitText(
        IEnumerable<BenchmarkClaimVerification> items,
        string labelA = "A",
        string labelB = "B",
        Func<BenchmarkClaimVerification, IReadOnlyList<string>?>? membersOf = null)
    {
        membersOf ??= v => v.RaisedBy;
        int onlyA = 0, onlyB = 0, both = 0;
        foreach (var v in items)
        {
            switch (MemberPhrase(membersOf(v)))
            {
                case "member A": onlyA++; break;
                case "member B": onlyB++; break;
                default: both++; break;
            }
        }
        return $"{labelA} {onlyA}, {labelB} {onlyB}, both {both}";
    }

    /// <summary>
    /// One member's part of the Panel Agreement spread line, e.g.
    /// <c>A 70–95 (SD 5.6), most frequent levels 6/3/6/5 on 8 of 18</c>.
    /// </summary>
    internal static string MemberSpreadText(BenchmarkPanelMemberSpread spread)
    {
        string range = spread.MinQualityScore is int min && spread.MaxQualityScore is int max
            ? $"{Inv(min)}–{Inv(max)}"
            : "no scores";
        string sd = spread.StandardDeviation is double deviation ? $"SD {Inv(deviation, "F1")}" : "SD not computed";
        string levels = spread.ModalLevels is { Count: > 0 } modal
            ? $", most frequent levels {string.Join("/", modal.Select(l => Inv(l)))} on {Inv(spread.ModalLevelsCount)} of {Inv(spread.AnswerCount)}"
            : string.Empty;
        return $"{spread.Member} {range} ({sd}){levels}";
    }

    /// <summary>Verifications by the verdict the harness reads (<see cref="BenchmarkClaimVerification.EffectiveVerdict"/>): supported, refuted, indeterminate.</summary>
    internal static (int Supported, int Refuted, int Indeterminate) VerdictCounts(IEnumerable<BenchmarkClaimVerification> verifications)
    {
        var list = verifications.ToList();
        return (
            list.Count(v => v.EffectiveVerdict == BenchmarkClaimVerdict.Supported),
            list.Count(v => v.EffectiveVerdict == BenchmarkClaimVerdict.Refuted),
            list.Count(v => v.EffectiveVerdict == BenchmarkClaimVerdict.Indeterminate));
    }

    /// <summary>
    /// The verdict word as the harness reads it, with the verifier's own verdict beside it when a
    /// citation note demoted it, e.g. <c>indeterminate (verifier: refuted; cited function priest_talk has no live call site)</c>.
    /// </summary>
    private static string VerdictText(BenchmarkClaimVerification v)
    {
        string effective = v.EffectiveVerdict.ToString().ToLowerInvariant();
        return string.IsNullOrWhiteSpace(v.CitationNote)
            ? effective
            : $"{effective} (verifier: {v.Verdict.ToString().ToLowerInvariant()}; {v.CitationNote})";
    }

    /// <summary>The quoted fragments an accused sentence was widened from, when they differ from it; empty otherwise.</summary>
    private static string QuotedFragmentsText(BenchmarkClaimVerification v)
    {
        var fragments = (v.QuotedFragments ?? Array.Empty<string>())
            .Where(f => !string.IsNullOrWhiteSpace(f) && !string.Equals(f.Trim(), v.Claim?.Trim(), StringComparison.Ordinal))
            .ToList();
        return fragments.Count == 0
            ? string.Empty
            : $" (quoted: {string.Join(", ", fragments.Select(f => $"\"{f}\""))})";
    }

    /// <summary>
    /// The panel verification-cleared Accuracy sensitivity clause, bold label closed by <c>:**</c> as
    /// the other sensitivity clauses are: the recomputed index beside the published one and the
    /// questions lifted for each member, or "not computed" when no answer qualifies.
    /// </summary>
    internal static string PanelSensitivityClause(BenchmarkRun run, PanelSensitivityResult result)
    {
        const string Label = "Panel verification-cleared Accuracy sensitivity:**";
        if (result.Index is not double index)
        {
            return $"{Label} not computed — no answer qualifies.";
        }

        static string Questions(IReadOnlyList<int> orderIndexes) => string.Join(", ", orderIndexes.Select(q => $"Q{q}"));

        var lifted = new List<string>();
        if (result.LiftedA.Count > 0)
        {
            lifted.Add($"member A's Accuracy one level higher on {Questions(result.LiftedA)}");
        }
        if (result.LiftedB.Count > 0)
        {
            lifted.Add(lifted.Count > 0
                ? $"member B's on {Questions(result.LiftedB)}"
                : $"member B's Accuracy one level higher on {Questions(result.LiftedB)}");
        }

        string published = run.QualityIndex.HasValue ? Inv(run.QualityIndex.Value) : "not published";
        string approximate = BenchmarkPanelSensitivity.IsApproximate(run)
            ? $" Approximate: this run predates harness {BenchmarkPanelSensitivity.MemberAttributionHarnessVersion}, which records the member that charged or raised each item, so an item without that record counts for both members."
            : string.Empty;
        return $"{Label} {Inv(index, "F0")} / 100 (published {published}) — {string.Join(", ", lifted)}, where the claim verifier supported every sentence that member charged, or every out-of-rubric claim it raised. Advisory: no score moves. A lower bound: a charge the verifier wrongly refuted is not lifted.{approximate}";
    }

    /// <summary>The display name of a corpus key of <see cref="CorpusIndexFingerprintProvider.CorpusKeys"/>.</summary>
    internal static string CorpusDisplayName(string key) => key switch
    {
        "gnollhackWiki" => "GnollHack wiki",
        "gnollhackSource" => "GnollHack source",
        "knowledgeBase" => "Knowledge base",
        "nethackWiki" => "NetHack wiki",
        "nethackSource" => "NetHack source",
        _ => key
    };

    /// <summary>The first 12 hex characters of a corpus fingerprint.</summary>
    internal static string ShortCorpusFingerprint(CorpusContentFingerprint fingerprint)
        => fingerprint.Sha256.Length > 12 ? fingerprint.Sha256[..12] : fingerprint.Sha256;

    /// <summary>
    /// One line per corpus of a run's <see cref="BenchmarkRun.CorpusIndexFingerprintsJson"/>, in
    /// <see cref="CorpusIndexFingerprintProvider.CorpusKeys"/> order: <c>GnollHack wiki: 1a2b3c4d5e6f (410 files)</c>,
    /// or <c>&lt;name&gt;: not indexed yet</c>. Null when the column is not recorded.
    /// </summary>
    internal static IReadOnlyList<string>? CorpusFingerprintLines(string? corpusIndexFingerprintsJson)
    {
        var fingerprints = CorpusIndexFingerprintProvider.Parse(corpusIndexFingerprintsJson);
        if (fingerprints == null) return null;

        return CorpusIndexFingerprintProvider.CorpusKeys
            .Select(key => fingerprints.TryGetValue(key, out var fingerprint) && fingerprint != null
                ? $"{CorpusDisplayName(key)}: {ShortCorpusFingerprint(fingerprint)} ({Inv(fingerprint.FileCount, "N0")} files)"
                : $"{CorpusDisplayName(key)}: not indexed yet")
            .ToList();
    }

    /// <summary>The harness version that first sent sentences docked against the rubric to the claim verifier.</summary>
    private const int RubricChargedVerificationHarnessVersion = 46;

    private static bool PredatesRubricChargedVerification(BenchmarkRun run)
        => int.TryParse(run.HarnessVersion, out int version) && version < RubricChargedVerificationHarnessVersion;

    /// <summary>
    /// The display label of <see cref="BenchmarkAnswerFlags.RubricContradictedBySource"/>. The flag
    /// says the source supports a sentence a grader docked against the rubric, not which of the two
    /// is wrong.
    /// </summary>
    internal const string RubricContradictedLabel = "Rubric-charged deduction contradicted by source";

    /// <summary>The line the <see cref="RubricContradictedLabel"/> bullet ends with.</summary>
    internal const string RubricRepairLeadLine = "Check both the rubric point and the grader's reading of it against the cited source before the next run.";

    /// <summary>
    /// The <see cref="RubricContradictedLabel"/> bullet: for each answer <see cref="BenchmarkRunFinalizer.IsRubricContradicted"/>
    /// holds for, every accused item docked against the rubric that the claim verifier supported with a
    /// citation (<see cref="BenchmarkService.SupportedRubricContradictions"/>), with its question, the
    /// charged sentence and part, the rubric text the grader quoted (or that it quoted none), and the
    /// verifier's citation, followed by its basis when that says more than the citation; then
    /// <see cref="RubricRepairLeadLine"/>. Nothing when no answer is flagged.
    /// </summary>
    private static void AppendRubricContradictions(StringBuilder sb, IReadOnlyList<BenchmarkRunAnswer> answers, bool isPanelRun)
    {
        var flagged = answers
            .Where(BenchmarkRunFinalizer.IsRubricContradicted)
            .OrderBy(a => a.OrderIndex)
            .ToList();
        if (flagged.Count == 0)
        {
            return;
        }

        sb.AppendLine($"- **{RubricContradictedLabel}:** {Inv(flagged.Count)} (question(s) {string.Join(", ", flagged.Select(a => $"Q{a.OrderIndex}"))}) — a sentence {(isPanelRun ? "a panel member" : "the assessor")} docked because it disagrees with the rubric's text was checked by the claim verifier against the source code/wiki and **supported** with a citation. Either the rubric point is wrong or the grader misread a correct one; the flag does not say which. Advisory: the deduction stands and no index moved.");
        foreach (var answer in flagged)
        {
            foreach (var item in BenchmarkService.SupportedRubricContradictions(ClaimVerificationsOf(answer)))
            {
                string chargedPart = item.QuotedFragments is { Count: > 0 } parts
                    ? $" (charged part: {string.Join(", ", parts.Select(p => $"\"{p}\""))})"
                    : string.Empty;
                string accusers = isPanelRun && item.AccusingMembers is { Count: > 0 } members
                    ? $" — accused by {string.Join(" and ", members)}"
                    : string.Empty;
                string rubric = string.IsNullOrWhiteSpace(item.RubricQuote)
                    ? "rubric text: not quoted by the grader"
                    : $"rubric: \"{item.RubricQuote}\"";
                string basis = item.Basis?.Trim() ?? string.Empty;
                string basisPart = basis.Length > 0 && !string.Equals(basis, item.Citation?.Trim() ?? string.Empty, StringComparison.Ordinal)
                    ? $" — {item.Basis}"
                    : string.Empty;
                sb.AppendLine($"  - Q{answer.OrderIndex}: \"{item.Claim}\"{chargedPart}{accusers} — {rubric} — source: {item.Citation}{basisPart}");
            }
        }
        sb.AppendLine($"  - *{RubricRepairLeadLine}*");
    }

    private const string BasisRefutedCause = "own-knowledge basis refuted";
    private const string AccusationSupportedCause = "a sentence the assessor quoted as false was supported";
    private const string AssessorStatementRefutedCause = "a statement of the assessor's own evidence was refuted";
    private const string DockedSuspicionSupportedCause = "docked suspected-false sentence supported";
    private const string CauseNotRecorded = "cause not recorded";

    /// <summary>
    /// Why an answer carries <see cref="BenchmarkAnswerFlags.ContestedAccuracyDeduction"/>, read from
    /// its verification items by role: the out-of-rubric basis refuted, an accused sentence supported,
    /// a statement of the assessor's own evidence refuted, or a <c>Suspected false:</c> sentence the
    /// Accuracy evidence docks supported, in any combination. A record stored without roles, or one
    /// where none is found, yields <see cref="CauseNotRecorded"/> alone. With a panel
    /// <paramref name="member"/>, only the items that member raised are read, against that member's
    /// own verdict: an accused sentence only when that member accused it, and a suspected-false
    /// sentence only when that member suspected it.
    /// </summary>
    private static List<string> ContestedDeductionCauses(BenchmarkRunAnswer answer, BenchmarkPanelMember? member = null)
    {
        var verifications = ClaimVerificationsOf(answer);
        var causes = new List<string>();
        if (!BenchmarkClaimRoles.HasRoles(verifications))
        {
            causes.Add(CauseNotRecorded);
            return causes;
        }

        var raised = member.HasValue ? BenchmarkService.RaisedByMembers(verifications, member.Value) : verifications!;
        var accused = member.HasValue ? BenchmarkService.AccusedByMembers(verifications, member.Value) : verifications!;
        var dockedSuspicions = member.HasValue
            ? (BenchmarkVerdictView.For(answer, member.Value) is BenchmarkVerdictView view
                ? BenchmarkService.SupportedDockedSuspicions(view, verifications, member.Value)
                : new List<BenchmarkClaimVerification>())
            : BenchmarkService.SupportedDockedSuspicions(answer, verifications);

        if (raised.Any(v => BenchmarkClaimRoles.HasRole(v, BenchmarkClaimRoles.OutOfRubricBasis)
            && v.EffectiveVerdict == BenchmarkClaimVerdict.Refuted))
        {
            causes.Add(BasisRefutedCause);
        }
        if (BenchmarkService.SupportedAccusations(accused).Count > 0)
        {
            causes.Add(AccusationSupportedCause);
        }
        if (BenchmarkService.RefutedAssessorStatements(raised).Count > 0)
        {
            causes.Add(AssessorStatementRefutedCause);
        }
        if (dockedSuspicions.Count > 0)
        {
            causes.Add(DockedSuspicionSupportedCause);
        }
        if (causes.Count == 0)
        {
            causes.Add(CauseNotRecorded);
        }
        return causes;
    }

    /// <summary>
    /// A cause from <see cref="ContestedDeductionCauses"/> as printed for <paramref name="member"/>:
    /// member B's supported accusation names member B as the one that quoted the sentence.
    /// </summary>
    private static string CauseText(string cause, BenchmarkPanelMember? member)
        => member == BenchmarkPanelMember.B && cause == AccusationSupportedCause
            ? "a sentence member B quoted as false was supported"
            : cause;

    /// <summary>
    /// Member B's advisory flags as the <see cref="BenchmarkAnswerFlags"/> bits that are their
    /// counterparts on member A's answer, read from <see cref="BenchmarkRunAnswer.CoAssessmentJson"/>.
    /// <c>None</c> without a record or a flag. The completeness-out-of-scope and readability-form-only
    /// markers have no bit and are not included.
    /// </summary>
    internal static BenchmarkAnswerFlags CoAssessmentAnswerFlags(BenchmarkRunAnswer answer)
        => CoAssessmentAnswerFlags(BenchmarkCoAssessmentRecord.Parse(answer.CoAssessmentJson)?.Flags);

    /// <summary><see cref="CoAssessmentAnswerFlags(BenchmarkRunAnswer)"/> for a parsed flag set.</summary>
    internal static BenchmarkAnswerFlags CoAssessmentAnswerFlags(BenchmarkCoAssessmentFlags? flags)
    {
        if (flags == null) return BenchmarkAnswerFlags.None;

        var result = BenchmarkAnswerFlags.None;
        if (flags.ContestedVerdict) result |= BenchmarkAnswerFlags.ContestedVerdict;
        if (flags.UnevidencedDeduction) result |= BenchmarkAnswerFlags.UnevidencedDeduction;
        if (flags.OmissionAsAccuracy) result |= BenchmarkAnswerFlags.OmissionAsAccuracy;
        if (flags.OutOfRubricAccuracy) result |= BenchmarkAnswerFlags.OutOfRubricAccuracyDeduction;
        if (flags.ContestedCriticalError) result |= BenchmarkAnswerFlags.ContestedCriticalError;
        if (flags.ContestedAccuracyDeduction) result |= BenchmarkAnswerFlags.ContestedAccuracyDeduction;
        if (flags.DimensionOutlier) result |= BenchmarkAnswerFlags.DimensionOutlier;
        if (flags.RubricContradictedBySource) result |= BenchmarkAnswerFlags.RubricContradictedBySource;
        return result;
    }

    /// <summary>The names of the set bits of <paramref name="flags"/>, in enum order; empty for <c>None</c>.</summary>
    internal static List<string> AnswerFlagNamesOf(BenchmarkAnswerFlags flags)
        => flags == BenchmarkAnswerFlags.None
            ? new List<string>()
            : Enum.GetValues<BenchmarkAnswerFlags>()
                .Where(f => f != BenchmarkAnswerFlags.None && flags.HasFlag(f))
                .Select(f => f.ToString())
                .ToList();

    /// <summary>
    /// The manifest line for the BOARD FACTS quote check stamped at launch, or null for a run that
    /// carries none. Questions are named by order index, as everywhere else in the report.
    /// </summary>
    internal static string? BoardFactsManifestLine(Overseer.Models.BoardFactsCheckDto? check)
    {
        if (check == null) return null;

        static string PerQuestion(IEnumerable<Overseer.Models.BoardFactIssueDto> issues)
            => string.Join(", ", issues
                .GroupBy(i => i.OrderIndex)
                .OrderBy(g => g.Key)
                .Select(g => $"Q{g.Key} ×{g.Count()}"));

        var line = new StringBuilder($"- **Rubric board quotes:** {check.CheckedLiteralCount} checked, ");
        line.Append(check.MissingLiterals.Count == 0
            ? "none missing"
            : $"{check.MissingLiterals.Count} missing ({PerQuestion(check.MissingLiterals)}) — these rubrics quote text this board does not contain; grades on them rest on stale facts");
        line.Append('.');

        if (check.UnquotedBulletCount > 0)
        {
            line.Append(check.UnquotedBulletCount == 1
                ? " 1 BOARD FACTS line carries no quoted literal and was not checked"
                : $" {check.UnquotedBulletCount} BOARD FACTS lines carry no quoted literal and were not checked");
            line.Append($" ({PerQuestion(check.UnquotedBullets)}).");
        }

        return line.ToString();
    }

    /// <summary>What a re-executed answer replaced, one line per answer, e.g. <c>Q15 re-executed: was ProviderError — …</c>.</summary>
    internal static IEnumerable<string> ReExecutedAnswerLines(IEnumerable<BenchmarkRunAnswer> answers)
        => answers
            .Where(a => a.RerunAtUtc.HasValue)
            .OrderBy(a => a.OrderIndex)
            .Select(a => $"Q{a.OrderIndex} re-executed: {ReplacedAttemptText(a)}");

    /// <summary><c>was ProviderError — message</c>, or <c>replaced attempt not recorded</c>.</summary>
    private static string ReplacedAttemptText(BenchmarkRunAnswer a)
    {
        if (!a.RerunOfStatus.HasValue)
        {
            return "replaced attempt not recorded";
        }
        string text = $"was {a.RerunOfStatus.Value}";
        if (!string.IsNullOrWhiteSpace(a.RerunOfErrorMessage))
        {
            text += $" — {a.RerunOfErrorMessage.ReplaceLineEndings(" ").Trim()}";
        }
        return text;
    }

    internal const int MissingBoardQuoteListCap = 20;

    /// <summary>
    /// The sub-bullets under <see cref="BoardFactsManifestLine"/> that name each missing literal,
    /// capped at <see cref="MissingBoardQuoteListCap"/>. Empty when nothing is missing.
    /// </summary>
    internal static IReadOnlyList<string> BoardFactsMissingLiteralLines(Overseer.Models.BoardFactsCheckDto? check)
    {
        if (check == null || check.MissingLiterals.Count == 0) return Array.Empty<string>();

        var lines = check.MissingLiterals
            .OrderBy(i => i.OrderIndex)
            .Take(MissingBoardQuoteListCap)
            .Select(i => $"  - Q{i.OrderIndex}: \"{i.Literal}\"")
            .ToList();
        int more = check.MissingLiterals.Count - MissingBoardQuoteListCap;
        if (more > 0)
        {
            lines.Add($"  - and {more} more");
        }
        return lines;
    }

    /// <summary>
    /// The per-answer re-grade line: its score, the four levels it returned, what it withdrew, what
    /// was dropped and why, and whether it was validated, rejected, or is a legacy record.
    /// </summary>
    private static string EvidenceInformedRegradeLine(BenchmarkRun run, BenchmarkRunAnswer a)
    {
        string? json = a.EvidenceInformedJson;
        string regrader = a.EvidenceInformedByModelSnapshot.Label() ?? ReadJsonString(json, "assessor") ?? a.AssessedByModelSnapshot.Label() ?? "assessor";
        var withdrawn = BenchmarkService.ReadEvidenceInformedWithdrawn(json);
        string withdrew = withdrawn.Count > 0 ? string.Join("; ", withdrawn) : "nothing";

        int? accuracy = ReadJsonInt(json, "accuracyLevel");
        int? completeness = ReadJsonInt(json, "completenessLevel");
        int? conciseness = ReadJsonInt(json, "concisenessLevel");
        int? readability = ReadJsonInt(json, "readabilityLevel");
        string levels = accuracy.HasValue && completeness.HasValue && conciseness.HasValue && readability.HasValue
            ? $", levels {accuracy}/{completeness}/{conciseness}/{readability} (Accuracy/Completeness/Conciseness/Readability)"
            : ", levels not recorded";

        string status;
        string rejection = string.Empty;
        if (BenchmarkService.HasEvidenceInformedValidation(json))
        {
            bool eligible = BenchmarkService.IsEligibleEvidenceInformedRegrade(run, a);
            status = eligible ? "validated" : "rejected";
            var dropped = ReadJsonStrings(json, "withdrawnDropped");
            var errors = ReadJsonStrings(json, "validationErrors");
            if (dropped.Count > 0)
            {
                rejection += $"; dropped: {string.Join("; ", dropped)}";
            }
            var verdictErrors = errors.Where(e => !dropped.Contains(e)).ToList();
            if (verdictErrors.Count > 0)
            {
                rejection += $"; rejected because {string.Join("; ", verdictErrors)}";
            }
        }
        else
        {
            status = BenchmarkService.IsLegacyEvidenceInformed(run, a)
                ? "legacy, unvalidated"
                : "no validation provenance, excluded";
        }

        return $"**Evidence-informed re-grade ({regrader}; {status}):** {a.EvidenceInformedQualityScore} / 100, critical error {(a.EvidenceInformedCriticalError == true ? "yes" : "no")}{levels} — withdrew: {withdrew}{rejection}. *Advisory; the first verdict is what scored.*";
    }

    private static int? ReadJsonInt(string? json, string property)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty(property, out var value)
                && value.ValueKind == JsonValueKind.Number
                && value.TryGetInt32(out int number)
                ? number
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static List<string> ReadJsonStrings(string? json, string property)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(json)) return result;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty(property, out var list)
                && list.ValueKind == JsonValueKind.Array)
            {
                result.AddRange(list.EnumerateArray()
                    .Where(x => x.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(x.GetString()))
                    .Select(x => x.GetString()!));
            }
        }
        catch (JsonException)
        {
            // A malformed record reads as empty.
        }
        return result;
    }

    private static (string? Accuracy, string? Completeness, string? Readability, bool CriticalErrorDemoted) ReadEvidence(BenchmarkRunAnswer answer)
    {
        if (string.IsNullOrWhiteSpace(answer.AssessmentEvidenceJson))
        {
            return (null, null, null, false);
        }

        try
        {
            using var doc = JsonDocument.Parse(answer.AssessmentEvidenceJson);
            var root = doc.RootElement;

            string? accuracy = root.TryGetProperty("accuracy", out var acc) && acc.ValueKind == JsonValueKind.String
                ? acc.GetString() : null;
            string? completeness = root.TryGetProperty("completeness", out var comp) && comp.ValueKind == JsonValueKind.String
                ? comp.GetString() : null;
            string? readability = root.TryGetProperty("readability", out var read) && read.ValueKind == JsonValueKind.String
                ? read.GetString() : null;
            bool demoted = root.TryGetProperty("criticalErrorDemoted", out var dem) && dem.ValueKind == JsonValueKind.True;

            return (accuracy, completeness, readability, demoted);
        }
        catch (JsonException)
        {
            // Evidence is commentary, never a score input: a malformed blob costs a line of the
            // report and nothing else.
            return (null, null, null, false);
        }
    }

    /// <summary>
    /// The second reader's free-text <c>comment</c> from a disputed answer's stored
    /// <see cref="BenchmarkRunAnswer.SecondOpinionJson"/> blob, collapsed to one line. Null for a
    /// null, blank or malformed blob, or one with no comment — in the same spirit as
    /// <c>AdminBenchmarkComponent.answerEvidence</c> on the client, this never throws.
    /// </summary>
    private static string? ReadSecondOpinionComment(string? secondOpinionJson)
    {
        if (string.IsNullOrWhiteSpace(secondOpinionJson))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(secondOpinionJson);
            if (doc.RootElement.TryGetProperty("comment", out var comment) && comment.ValueKind == JsonValueKind.String)
            {
                string? text = comment.GetString();
                if (string.IsNullOrWhiteSpace(text))
                {
                    return null;
                }
                string oneLine = text.Replace("\r\n", " ").Replace("\r", " ").Replace("\n", " ");
                return WhitespaceRunRegex.Replace(oneLine, " ").Trim();
            }
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>"25 executed, 2 blocked, budget 25" — three numbers that mean three things.</summary>
    private static string FormatToolBudgetLine(BenchmarkRunAnswer answer)
    {
        var (executed, blocked) = ToolCallSplit(answer);
        string budget = answer.ToolCallBudgetUsed.HasValue
            ? answer.ToolCallBudgetUsed.Value.ToString(CultureInfo.InvariantCulture)
            : "not recorded";

        // H1: the model-call count is what separates "many calls" from "large context" as the cause of a
        // question's input-token growth, and it is the fact the per-question line was missing. Omitted for
        // answers recorded before it was persisted, which is "not recorded", never zero.
        string modelCalls = answer.ModelCallCount.HasValue
            ? $", model calls: {answer.ModelCallCount.Value.ToString(CultureInfo.InvariantCulture)}"
            : string.Empty;

        // How many tool rounds the turn spent, and how many calls it packed into each: a turn that
        // batched its retrieval reads differently from one that took the same calls one round at a
        // time. Derived from the per-call rows, which exist from harness 17 onward only, so an
        // answer without them prints nothing — omission is "not recorded", never zero, exactly as
        // the model-call clause treats its own absence.
        string toolRounds = string.Empty;
        int roundCount = ToolRoundCount(answer);
        if (roundCount > 0)
        {
            double callsPerRound = answer.ToolCalls.Count / (double)roundCount;
            toolRounds = $", tool rounds: {Inv(roundCount)} ({Inv(callsPerRound, "F1")} calls/round)";
        }

        return blocked > 0
            ? $"{executed} executed, {blocked} blocked, budget {budget}{modelCalls}{toolRounds}"
            : $"{executed} executed, budget {budget}{modelCalls}{toolRounds}";
    }

    /// <summary>
    /// Distinct tool rounds in an answer's per-call rows, and 0 when it carries none. A row whose
    /// round was not recorded counts as its own round rather than being dropped, so the figure never
    /// understates how spread out the calls were.
    /// </summary>
    private static int ToolRoundCount(BenchmarkRunAnswer answer)
    {
        if (answer.ToolCalls.Count == 0) return 0;
        return answer.ToolCalls.Select(c => c.IterationIndex).Distinct().Count();
    }

    private static readonly Regex WhitespaceRunRegex = new(@"\s+", RegexOptions.Compiled);

    /// <summary>
    /// A bounded, single-line preview of a tool call's arguments for the per-question table:
    /// line breaks collapsed to spaces, runs of whitespace collapsed to one, every <c>|</c>
    /// escaped so the cell cannot break the table row, then truncated to 80 characters with an
    /// ellipsis appended when it was longer. <c>(pruned)</c> marks a null or blank
    /// <paramref name="argsText"/> beside a non-zero <paramref name="resultLengthChars"/> — the
    /// retention sweep having nulled the payload, not an absent record — and <c>—</c> covers
    /// every other empty case.
    /// </summary>
    private static string FormatArgsPreview(string? argsText, int resultLengthChars)
    {
        if (string.IsNullOrWhiteSpace(argsText))
        {
            return resultLengthChars != 0 ? "(pruned)" : "—";
        }

        string oneLine = argsText.Replace("\r\n", " ").Replace("\r", " ").Replace("\n", " ");
        string collapsed = WhitespaceRunRegex.Replace(oneLine, " ").Trim();
        string escaped = collapsed.Replace("|", "\\|");
        return escaped.Length > 80 ? escaped[..80] + "…" : escaped;
    }

    /// <summary>
    /// One band's figure out of a run's banded cap column — <see cref="BenchmarkRun.ToolIterationCapsJson"/>
    /// and its siblings, which store <c>{"Simple":22,"Intermediate":22,"Advanced":22}</c>. Null for a run
    /// recorded before the column existed, or for a blob that will not parse: the caps are commentary here,
    /// so an unreadable one costs a report line and nothing else.
    /// </summary>
    private static int? BandedCap(string? capsJson, BenchmarkDifficulty band)
    {
        if (string.IsNullOrWhiteSpace(capsJson)) return null;
        try
        {
            using var doc = JsonDocument.Parse(capsJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;
            if (!doc.RootElement.TryGetProperty(band.ToString(), out var value)) return null;
            return value.TryGetInt32(out int cap) ? cap : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Whether an answer finished within one step of a configured ceiling, and which one. A turn that
    /// stopped one round short of its cap produced the answer the model could reach under the cap, not
    /// the answer it would have produced without it, and no other line in the report says so.
    ///
    /// <para>The round count itself is not persisted on an answer, so
    /// <see cref="BenchmarkRunAnswer.ModelCallCount"/> is measured against both caps. It is the right
    /// proxy for the iteration cap because the agent loop makes exactly one model call per round, but
    /// it is a model-call count and is labelled as one — the two figures can differ by the one forced
    /// final call a fired limit produces.</para>
    /// </summary>
    private static string? NearCeilingNote(BenchmarkRun run, BenchmarkRunAnswer answer)
    {
        if (!answer.ModelCallCount.HasValue) return null;
        int calls = answer.ModelCallCount.Value;

        int? modelCallCap = BandedCap(run.TotalModelCallCapsJson, answer.Difficulty);
        int? iterationCap = BandedCap(run.ToolIterationCapsJson, answer.Difficulty);

        var reached = new List<string>(2);
        if (modelCallCap.HasValue && modelCallCap.Value > 1 && calls >= modelCallCap.Value - 1)
        {
            reached.Add($"{calls} model call(s) against a model-call cap of {modelCallCap.Value}");
        }
        if (iterationCap.HasValue && iterationCap.Value > 1 && calls >= iterationCap.Value - 1)
        {
            reached.Add($"{calls} model call(s) against a tool-iteration cap of {iterationCap.Value}");
        }

        return reached.Count > 0 ? string.Join("; ", reached) : null;
    }

    /// <summary>
    /// The second-opinion mode this run was actually graded under, read from the run's own
    /// stamped value. An out-of-range value falls back to <c>Flagged</c> rather than to
    /// <c>Off</c>: <c>Off</c> would claim no second verdict was configured, which is the one
    /// reading a run carrying second verdicts cannot support.
    /// </summary>
    private static BenchmarkSecondOpinionMode ModeOf(BenchmarkRun run)
    {
        return Enum.IsDefined(typeof(BenchmarkSecondOpinionMode), run.SecondOpinionModeUsed)
            ? (BenchmarkSecondOpinionMode)run.SecondOpinionModeUsed
            : BenchmarkSecondOpinionMode.Flagged;
    }

    /// <summary>The clause that turns a mode name into a statement about coverage.</summary>
    private static string ModeGloss(BenchmarkSecondOpinionMode mode) => mode switch
    {
        BenchmarkSecondOpinionMode.All => " — every answer graded twice.",
        BenchmarkSecondOpinionMode.FlaggedAndOutliers => " — flagged answers, plus a post-scoring sweep for outliers.",
        BenchmarkSecondOpinionMode.Flagged => " — flagged answers only.",
        BenchmarkSecondOpinionMode.FlaggedPlusSample => " — flagged answers, topped up to the profile's minimum sample.",
        _ => " — no second verdict was configured; anything recorded came from a manual re-grade."
    };

    /// <summary>Trigger names as stored, in the words the report uses for them.</summary>
    internal static string TriggerLabel(string trigger) => trigger switch
    {
        "CriticalError" => "critical error",
        "RefutedClaim" => "refuted claim",
        "ContestedVerdict" => "contested verdict",
        "OutOfRubricAccuracy" => "out-of-rubric accuracy deduction",
        "UnevidencedDeduction" => "unevidenced deduction",
        "OmissionAsAccuracy" => "omission docked as accuracy",
        "DimensionOutlier" => "dimension outlier",
        "UnverifiedClaims" => "unverifiable claims",
        "BelowThreshold" => "score below the profile threshold",
        "Outlier" => "outlier below the run median",
        "Sample" => "sample top-up",
        "All" => "double grading, every answer",
        "Manual" => "manual re-grade",
        _ => trigger
    };

    /// <summary>
    /// " — N tool call(s), X input tokens, Y s" from the answer's stored verifier columns, naming
    /// only those present; empty when none is.
    /// </summary>
    internal static string ClaimVerificationSpendText(BenchmarkRunAnswer a)
    {
        var parts = new List<string>();
        if (a.ClaimVerificationToolCallCount.HasValue) parts.Add($"{Inv(a.ClaimVerificationToolCallCount.Value, "N0")} tool call(s)");
        if (a.ClaimVerificationInputTokens.HasValue) parts.Add($"{Inv(a.ClaimVerificationInputTokens.Value, "N0")} input tokens");
        if (a.ClaimVerificationDurationMs.HasValue) parts.Add($"{Inv(a.ClaimVerificationDurationMs.Value / 1000.0, "N1")} s");
        return parts.Count == 0 ? string.Empty : " — " + string.Join(", ", parts);
    }

    /// <summary>
    /// The first 600 characters of a failed verification's raw response, on one line and with
    /// backticks replaced so the Markdown around it holds; empty when no raw text was stored.
    /// </summary>
    internal static string ClaimVerificationRawTextHead(BenchmarkRunAnswer a)
    {
        if (string.IsNullOrWhiteSpace(a.ClaimVerificationRawText)) return string.Empty;

        string head = a.ClaimVerificationRawText.Length > 600 ? a.ClaimVerificationRawText.Substring(0, 600) : a.ClaimVerificationRawText;
        head = Regex.Replace(head, @"\s*[\r\n]+\s*", " ").Replace('`', '\'').Trim();
        return $" Raw response, first 600 characters: {head}";
    }

    /// <summary>
    /// "Verifier spend by answer: highest Q.. (… input tokens), Q.., Q..; mean … input tokens and …
    /// tool calls per verified answer." over the answers with a stored verifier input-token count;
    /// null when there is none.
    /// </summary>
    internal static string? VerifierSpendByAnswerLine(IEnumerable<BenchmarkRunAnswer> answers)
    {
        var verified = answers.Where(a => a.ClaimVerificationInputTokens.HasValue && a.ClaimVerificationInputTokens.Value > 0).ToList();
        if (verified.Count == 0) return null;

        var ranked = verified
            .OrderByDescending(a => a.ClaimVerificationInputTokens!.Value)
            .ThenBy(a => a.OrderIndex)
            .Take(3)
            .ToList();
        var named = new List<string> { $"Q{ranked[0].OrderIndex} ({Inv(ranked[0].ClaimVerificationInputTokens!.Value, "N0")} input tokens)" };
        named.AddRange(ranked.Skip(1).Select(a => $"Q{a.OrderIndex}"));

        double meanInput = verified.Average(a => (double)a.ClaimVerificationInputTokens!.Value);
        double meanCalls = verified.Average(a => (double)(a.ClaimVerificationToolCallCount ?? 0));
        return $"- **Verifier spend by answer:** highest {string.Join(", ", named)}; " +
            $"mean {Inv(meanInput, "N0")} input tokens and {Inv(meanCalls, "N1")} tool calls per verified answer.";
    }

    /// <summary>
    /// The assessor's unverifiable claims. Empty for a run graded before the field existed, and
    /// empty rather than throwing on a malformed blob: these are commentary, never a score input.
    /// </summary>
    private static IReadOnlyList<string> ReadUnverifiedClaims(BenchmarkRunAnswer answer)
    {
        if (string.IsNullOrWhiteSpace(answer.UnverifiedClaimsJson))
        {
            return Array.Empty<string>();
        }

        try
        {
            var claims = JsonSerializer.Deserialize<List<string>>(answer.UnverifiedClaimsJson);
            return claims?.Where(c => !string.IsNullOrWhiteSpace(c)).ToList() ?? (IReadOnlyList<string>)Array.Empty<string>();
        }
        catch (JsonException)
        {
            return Array.Empty<string>();
        }
    }

    /// <summary>
    /// The reference reader's Intelligence Index of a panel run, taken over the panel's own item set
    /// with the reader's score where it graded and an unanswered question's 0 where nobody could; null
    /// when it graded none. <paramref name="covered"/> is the items it holds a score for.
    /// </summary>
    internal static int? ReferenceReaderIndex(BenchmarkRun run, IEnumerable<BenchmarkRunAnswer> answers, out int covered)
    {
        bool panel = BenchmarkRunFinalizer.IsPanelRun(run);
        var readerItems = answers
            .Where(a => BenchmarkRunFinalizer.CountsTowardQualityIndex(a) && BenchmarkScoring.IndexQuality(a, panel).HasValue)
            .Select(a => (Score: a.SecondOpinionQualityScore.HasValue
                    ? (double?)a.SecondOpinionQualityScore.Value
                    : (BenchmarkRunFinalizer.IsModelProducedEmptyAnswer(a) ? BenchmarkScoring.IndexQuality(a, panel) : null),
                Difficulty: (int?)(a.AssessedDifficulty ?? BenchmarkRunFinalizer.FallbackDifficulty(a.Difficulty))))
            .ToList();
        covered = readerItems.Count(i => i.Score.HasValue);
        return BenchmarkScoring.QualityIndex(readerItems);
    }

    /// <summary>
    /// The answers a panel run's reference reader graded that count toward the index, a Manual
    /// re-grade excepted, in order-index order.
    /// </summary>
    internal static List<BenchmarkRunAnswer> ReferenceReaderCovered(IEnumerable<BenchmarkRunAnswer> answers)
        => answers
            .Where(a => a.SecondOpinionQualityScore.HasValue
                        && !string.Equals(a.SecondOpinionTrigger, "Manual", StringComparison.Ordinal)
                        && BenchmarkRunFinalizer.CountsTowardQualityIndex(a))
            .OrderBy(a => a.OrderIndex)
            .ToList();

    /// <summary>
    /// The reference reader's mean signed difference from the panel score over the covered answers
    /// that carry one, rounded as every agreement delta is; null when none does.
    /// </summary>
    internal static double? ReferenceReaderSignedOffset(IEnumerable<BenchmarkRunAnswer> answers)
    {
        var graded = ReferenceReaderCovered(answers).Where(a => a.PanelQualityScore.HasValue).ToList();
        return graded.Count > 0
            ? BenchmarkRunFinalizer.RoundAgreementDelta(graded.Average(a => a.SecondOpinionQualityScore!.Value - a.PanelQualityScore!.Value))
            : null;
    }

    /// <summary>
    /// The <paramref name="p"/> quantile by <see cref="BenchmarkGroupStatistics.Percentile"/>'s linear
    /// interpolation, rounded to whole milliseconds, so the run report and every report-pack document
    /// print one P90. Zero over no value.
    /// </summary>
    private static long Percentile(IReadOnlyList<long> sorted, double p)
    {
        if (sorted.Count == 0) return 0;
        double value = BenchmarkGroupStatistics.Percentile(sorted.Select(v => (double)v).ToList(), p * 100.0)!.Value;
        return (long)Math.Round(value, MidpointRounding.AwayFromZero);
    }

    private static double Median(IEnumerable<double> values)
    {
        var sorted = values.OrderBy(v => v).ToList();
        if (sorted.Count == 0) return 0;
        int mid = sorted.Count / 2;
        return (sorted.Count % 2 != 0)
            ? sorted[mid]
            : (sorted[mid - 1] + sorted[mid]) / 2.0;
    }

    /// <summary>
    /// A P50 figure computed as the true statistical median (mean of the two middle values for an
    /// even count), so the report's P50 lines agree with the Angular UI card, which is computed the
    /// same way.
    /// </summary>
    private static long MedianMs(IReadOnlyList<long> sorted)
        => sorted.Count == 0 ? 0 : (long)Math.Round(Median(sorted.Select(v => (double)v)), MidpointRounding.AwayFromZero);

    /// <summary>
    /// The headline text for a per-run index <see cref="BenchmarkRunFinalizer.Apply"/> may have
    /// withheld: the score plus <paramref name="scoredSuffix"/> when present, a pointer to § 5 when
    /// withheld because of a terminal provider failure, otherwise <paramref name="noScoreText"/>. A
    /// run finalised before harness 21 never recorded <see cref="BenchmarkRun.TerminalFailureAnswerCount"/>,
    /// so <paramref name="terminalFailureCount"/> is expected to already carry that fallback.
    /// </summary>
    private static string IndexHeadline(int? value, string scoredSuffix, int terminalFailureCount, int totalQuestionCount, string noScoreText)
    {
        if (value.HasValue)
        {
            return $"{value.Value}{scoredSuffix}";
        }

        return terminalFailureCount > 0
            ? $"Not computed — {terminalFailureCount} of {totalQuestionCount} question(s) failed at the provider; see § 5"
            : noScoreText;
    }

    /// <summary>
    /// Whether two providers are one family, at the granularity of
    /// <see cref="BenchmarkComplianceGuard.IsSameProvider(string, string)"/>: trimmed,
    /// case-insensitive, and false when either is blank.
    /// </summary>
    private static bool IsSameFamily(string? first, string? second)
    {
        if (string.IsNullOrWhiteSpace(first) || string.IsNullOrWhiteSpace(second))
        {
            return false;
        }

        return string.Equals(first.Trim(), second.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The candidate is an Anthropic model, whose usage reports no separate reasoning-token count.</summary>
    private static bool IsAnthropicCandidate(BenchmarkRun run)
        => string.Equals(run.TestedModelSnapshot.Provider?.Trim(), "Anthropic", StringComparison.OrdinalIgnoreCase);

    /// <summary><c>same-family</c> when a grader shares the candidate's provider, otherwise <c>cross-family</c>.</summary>
    private static string FamilyRelation(string? candidateProvider, string? graderProvider)
        => IsSameFamily(candidateProvider, graderProvider) ? "same-family" : "cross-family";

    /// <summary>A score that may be a panel mean, without a trailing ".0" on a whole number.</summary>
    private static string ScoreText(double value) => Inv(Math.Round(value, 1, MidpointRounding.AwayFromZero));

    /// <summary>A signed one-decimal delta, with "+" on a positive one.</summary>
    private static string SignedDelta(double value) => $"{(value > 0 ? "+" : string.Empty)}{Inv(value, "F1")}";

    /// <summary>A fraction as a whole percent without the sign: 0.0556 is "6".</summary>
    private static string PercentNumber(double fraction)
        => Inv(Math.Round(fraction * 100.0, MidpointRounding.AwayFromZero), "F0");

    /// <summary>A fraction as a whole percent: 0.0556 is "6%".</summary>
    private static string PercentText(double fraction) => PercentNumber(fraction) + "%";

    /// <summary>Who confirmed a critical error, as the scoring method 13 Critical Errors line names it.</summary>
    private static string ConfirmedResolutionText(BenchmarkCriticalErrorResolution? resolution) => resolution switch
    {
        BenchmarkCriticalErrorResolution.Agreed => "both panel members",
        BenchmarkCriticalErrorResolution.UpheldByVerifier => "upheld by the claim verifier",
        BenchmarkCriticalErrorResolution.SingleAssessor => "assessor",
        _ => "confirmed"
    };

    /// <summary>
    /// A scoring method 13 panel answer's resolution as its Panel Score line names it, or empty for
    /// none. <paramref name="ceiling"/> is the run's critical-error ceiling.
    /// </summary>
    private static string PanelResolutionNote(BenchmarkRunAnswer a, int ceiling)
    {
        string flagging = a.CriticalError ? "A" : "B";
        string other = a.CriticalError ? "B" : "A";
        return a.CriticalErrorResolution switch
        {
            BenchmarkCriticalErrorResolution.Agreed => " — critical error confirmed by both panel members",
            BenchmarkCriticalErrorResolution.UpheldByVerifier => $" — critical error upheld by the claim verifier: member {other} counted at most {ceiling}",
            BenchmarkCriticalErrorResolution.OverturnedByVerifier => $" — critical error overturned by the claim verifier: member {flagging} counted at its pre-cap score",
            BenchmarkCriticalErrorResolution.Unresolved => " — critical-error split unresolved: both members averaged as graded",
            _ => string.Empty
        };
    }

    /// <summary>
    /// Member A's critical-error label on a question under scoring method 13, naming the resolution.
    /// <paramref name="capLoweredScore"/> is whether the cap lowered member A's own score.
    /// </summary>
    private static string ResolvedCapNote(BenchmarkCriticalErrorResolution? resolution, bool capLoweredScore)
    {
        if (resolution == BenchmarkCriticalErrorResolution.OverturnedByVerifier)
        {
            return " *(CRITICAL ERROR OVERTURNED by the claim verifier — the panel score uses the pre-cap score)*";
        }

        string by = resolution switch
        {
            BenchmarkCriticalErrorResolution.Agreed => "confirmed by both panel members",
            BenchmarkCriticalErrorResolution.UpheldByVerifier => "upheld by the claim verifier",
            BenchmarkCriticalErrorResolution.Unresolved => "split unresolved, averaged",
            BenchmarkCriticalErrorResolution.SingleAssessor => "confirmed by the assessor",
            _ => "not resolved"
        };
        return capLoweredScore
            ? $" *(CRITICAL ERROR CAP APPLIED — {by})*"
            : $" *(CRITICAL ERROR — cap not binding; {by})*";
    }

    /// <summary>
    /// Whether the not-attempted floor raised a member's quality score: the member marked the answer
    /// not attempted and raised no critical error, its stored pre-cap score is the profile's floor,
    /// and its levels re-scored without the floor come out lower than with it.
    /// </summary>
    private static bool NotAttemptedFloorApplied(
        bool notAttempted,
        bool criticalError,
        int accuracy,
        int completeness,
        int conciseness,
        int readability,
        int? storedRawScore,
        BenchmarkScoringConstants constants)
    {
        if (!notAttempted || criticalError || constants.NotAttemptedScore is not int floor || storedRawScore != floor)
        {
            return false;
        }

        return BenchmarkScoring.Quality(accuracy, completeness, conciseness, readability, false, constants, notAttempted: true).Score
            > BenchmarkScoring.Quality(accuracy, completeness, conciseness, readability, false, constants).Score;
    }

    /// <summary>One line of a table cell: line breaks collapsed and <c>|</c> escaped; "—" when empty.</summary>
    private static string TableCell(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "—";
        string oneLine = WhitespaceRunRegex.Replace(text.Replace("\r\n", " ").Replace("\r", " ").Replace("\n", " "), " ").Trim();
        return oneLine.Replace("|", "\\|");
    }

    /// <summary>A finding's category in report prose: <c>critical_error</c> reads "critical error".</summary>
    private static string FindingCategoryText(string? category)
        => string.IsNullOrWhiteSpace(category) ? "other" : category.Trim().Replace('_', ' ');

    /// <summary>
    /// A synthesis's structured findings as <c>**Strengths**</c> and <c>**Weaknesses**</c> bullets,
    /// each with its question numbers and category. Appends nothing when there are none, which is
    /// every synthesis written before harness 40.
    /// </summary>
    private static void AppendSynthesisFindings(StringBuilder sb, IReadOnlyList<BenchmarkSynthesisFinding> findings)
    {
        if (findings.Count == 0) return;

        void Group(string title, string kind)
        {
            var group = findings.Where(f => string.Equals(f.Kind, kind, StringComparison.OrdinalIgnoreCase)).ToList();
            if (group.Count == 0) return;

            sb.AppendLine();
            sb.AppendLine($"**{title}**");
            sb.AppendLine();
            foreach (var f in group)
            {
                string questions = f.Questions.Count > 0
                    ? string.Join(", ", f.Questions.OrderBy(q => q).Select(q => $"Q{q}")) + "; "
                    : string.Empty;
                sb.AppendLine($"- {f.Text} *({questions}{FindingCategoryText(f.Category)})*");
            }
        }

        Group("Strengths", "strength");
        Group("Weaknesses", "weakness");
    }

    /// <summary>
    /// The run's Markdown report. <paramref name="battery"/> is the battery run slot the run fills,
    /// printed in the manifest; null when the run is no battery member.
    /// </summary>
    public static string BuildMarkdownReport(
        BenchmarkRun run,
        string? overseerVersion = null,
        BenchmarkRunPricing? runPricing = null,
        BenchmarkRunBatteryContext? battery = null)
    {
        var sb = new StringBuilder();
        // Read back from the run's own profile snapshot, so the report describes the run in
        // front of it rather than whatever the default profile says today.
        var scoringConstants = ScoringConstantsOf(run);

        // A panel run publishes the mean of two members' verdicts. Every panel-only line below is
        // gated on this, so a single-assessor run renders exactly as it always has.
        bool isPanelRun = BenchmarkRunFinalizer.IsPanelRun(run);

        // The non-scoring reader's name: the reference reader in a panel run, the second reader otherwise.
        string readerName = isPanelRun ? "reference reader" : "second reader";
        string readerTitle = isPanelRun ? "Reference Reader" : "Second Reader";

        // The answer's published score: the panel score in a panel run, member A's otherwise.
        double? IndexQualityOf(BenchmarkRunAnswer a) => BenchmarkScoring.IndexQuality(a, isPanelRun);

        string memberALabel = run.AssessorModelSnapshot.Label() ?? "member A";
        string memberBLabel = run.CoAssessorModelSnapshot.Label() ?? "member B";
        string memberARelation = FamilyRelation(run.TestedModelSnapshot.Provider, run.AssessorModelSnapshot.Provider);
        string memberBRelation = FamilyRelation(run.TestedModelSnapshot.Provider, run.CoAssessorModelSnapshot?.Provider);

        // 1. Introduction
        sb.AppendLine("# GnollHack Overseer AI Intelligence Benchmark Report");
        sb.AppendLine();
        string suiteDisplay = !string.IsNullOrEmpty(run.GameSnapshotNameUsed)
            ? $"suite **{run.SuiteName}** (Snapshot: **{run.GameSnapshotNameUsed}**)"
            : $"suite **{run.SuiteName}**";
        sb.AppendLine($"This report contains the automated domain knowledge, reasoning, and efficiency benchmark results for {suiteDisplay}, evaluated against model **{run.TestedModelSnapshot.Label()}** ({run.TestedModelSnapshot.Provider} / {run.TestedModelSnapshot.ModelId}).");
        sb.AppendLine($"Run conducted on {Stamp(run.StartedAtUtc)} UTC" + (!string.IsNullOrEmpty(run.StartedByUser?.UserName) ? $" by {run.StartedByUser.UserName}." : "."));
        sb.AppendLine();
        sb.AppendLine("> *Note:* This benchmark evaluates domain-specific roguelike intelligence, codebase comprehension, and tool usage within the GnollHack Overseer harness. Scoring uses Behaviorally Anchored Rating Scales (BARS), weighted geometric aggregation, and logarithmic speed decay.");
        sb.AppendLine();

        if (!run.SuiteQuestionsReviewed)
        {
            sb.AppendLine("> ⚠️ **Unreviewed questions**: This run contains AI-generated questions that were not verified before the run. Scores may reflect rubric defects.");
            sb.AppendLine();
        }

        // At a Glance goes here. Its figures are the ones the sections below compute, so it is
        // inserted at this offset once they have all been written.
        int atAGlanceOffset = sb.Length;

        // 2. Run Manifest
        sb.AppendLine("## 1. Run Manifest");
        sb.AppendLine();
        sb.AppendLine($"- **Overseer Version:** {overseerVersion ?? "1.0.0"}");
        sb.AppendLine($"- **Suite Name:** {run.SuiteName}");
        sb.AppendLine($"- **Suite origin:** {SuiteOriginText(run)}");
        if (battery != null)
        {
            sb.AppendLine(battery.ManifestLine());
        }
        if (!string.IsNullOrEmpty(run.GameSnapshotNameUsed))
        {
            string shaPrefix = run.GameSnapshotSha256Used?.Length >= 12
                ? run.GameSnapshotSha256Used[..12]
                : (run.GameSnapshotSha256Used ?? "n/a");
            // The format is recorded from harness 33; an earlier run's null is "not recorded", not
            // "not stated", so nothing is printed for it.
            string formatText = int.TryParse(run.HarnessVersion, out int manifestHarness) && manifestHarness >= 33
                ? (run.GameSnapshotFormatVersionUsed.HasValue
                    ? $", format {run.GameSnapshotFormatVersionUsed.Value}"
                    : ", format not stated")
                : string.Empty;
            sb.AppendLine($"- **Game Snapshot:** {run.GameSnapshotNameUsed} ({run.GameSnapshotCaptureMethodUsed}, {run.GameSnapshotCharCountUsed} chars, SHA-256 {shaPrefix}{formatText})");
        }
        var boardFactsCheck = BenchmarkBoardFactsChecker.Deserialize(run.BoardFactsCheckJson);
        string? boardFactsLine = BoardFactsManifestLine(boardFactsCheck);
        if (boardFactsLine != null)
        {
            sb.AppendLine(boardFactsLine);
            foreach (string missing in BoardFactsMissingLiteralLines(boardFactsCheck))
            {
                sb.AppendLine(missing);
            }
        }
        sb.AppendLine($"- **Total Questions:** {run.TotalQuestionCount}");
        sb.AppendLine($"- **Answered Questions:** {run.AnsweredQuestionCount} of {run.TotalQuestionCount}");
        // Also At a Glance's Answered cell.
        string answerRateText = $"{run.AnsweredQuestionCount} of {run.TotalQuestionCount}"
            + (run.TotalQuestionCount > 0
                ? $" ({Inv(run.AnsweredQuestionCount * 100.0 / run.TotalQuestionCount, "F1")}%)"
                : string.Empty);
        sb.AppendLine($"- **Answer Rate:** {answerRateText}");
        sb.AppendLine($"- **Run Status:** {run.Status}");
        sb.AppendLine($"- **Start Time (UTC):** {Stamp(run.StartedAtUtc)}");
        string endTime = run.CompletedAtUtc.HasValue ? Stamp(run.CompletedAtUtc.Value) : "In Progress / Interrupted";
        if (run.RerunStartedAtUtc.HasValue)
        {
            sb.AppendLine($"- **End Time (UTC, original execution):** {endTime}");
            sb.AppendLine($"- **Re-run span (UTC):** {RerunSpan(run)}");
        }
        else
        {
            sb.AppendLine($"- **End Time (UTC):** {endTime}");
        }
        sb.AppendLine($"- **Total Elapsed Wall Time:** {FormatDuration(run.TotalDurationMs)}");
        sb.AppendLine($"- **Total Candidate Answer Time:** {FormatDuration(run.TotalAnswerDurationMs)}"
            + (run.RerunStartedAtUtc.HasValue ? " *(includes re-executed answers; the wall time above is the original execution's)*" : string.Empty));
        // "Question Parallelism", not "Parallel …": the Model Under Test block reports the
        // provider's parallel *tool calls* under a similar name, and one report using the same
        // word for two mechanisms is how a reader concludes a sequential run ran concurrently.
        sb.AppendLine($"- **Question Parallelism:** {run.MaxParallelQuestionsUsed} question(s) at a time" + (run.SpeedMeasurementDegraded ? " *(Speed metrics advisory due to concurrency)*" : " *(Sequential, strict timing)*"));
        sb.AppendLine();

        // Declared before the comparability block, which reports the per-question
        // tool call budgets that were actually applied.
        var answers = run.Answers.OrderBy(a => a.OrderIndex).ToList();

        // Comparability block
        sb.AppendLine("### Comparability");
        sb.AppendLine($"- **Harness Version:** {run.HarnessVersion ?? "1 (unversioned legacy)"}");
        sb.AppendLine($"- **Scoring Method Version:** {run.ScoringMethodVersion}");
        sb.AppendLine($"- **Scoring Profile:** {run.ScoringProfile?.Name ?? "Default Intelligence Profile"}");
        string itemRevisionsLine = string.Join(", ", answers
            .Select(a => $"Q{a.OrderIndex} r{a.ItemRevisionUsed?.ToString(CultureInfo.InvariantCulture) ?? "?"}"));
        sb.AppendLine($"- **Suite item revisions:** {(string.IsNullOrEmpty(itemRevisionsLine) ? "none" : itemRevisionsLine)}");
        string assessedDifficultiesLine = string.Join(", ", answers
            .Select(a => $"Q{a.OrderIndex} {a.AssessedDifficulty?.ToString(CultureInfo.InvariantCulture) ?? "?"}"));
        sb.AppendLine($"- **Assessed difficulties:** {(string.IsNullOrEmpty(assessedDifficultiesLine) ? "none" : assessedDifficultiesLine)}");
        sb.AppendLine("  - *An item revision moves only when the rubric text is edited; an assessed difficulty can move on its own from Assess Difficulty, with no edit and no revision bump — so two runs can match on every item revision and still have been weighted by two different exams (see `SuiteAssessedDifficulties`).*");

        // A heavy-thinking candidate graded against an interactive-latency profile produces a
        // Speed Index that describes the profile more than the model: the 2026-09-03 run scored
        // a max-thinking model 65 on speed beside 91 on intelligence. Say so where the reader
        // meets the number, rather than leaving it to be inferred from the thinking level.
        string candidateThinking = run.TestedModelSnapshot.ThinkingLevel ?? string.Empty;
        bool deliberatingCandidate =
            candidateThinking.Equals("high", StringComparison.OrdinalIgnoreCase) ||
            candidateThinking.Equals("max", StringComparison.OrdinalIgnoreCase);
        bool profileMisfit = deliberatingCandidate && scoringConstants.SpeedTargetMs < InteractiveSpeedTargetMaxMs;
        if (profileMisfit)
        {
            sb.AppendLine($"- **Profile Fit:** this profile targets interactive latency ({Inv(scoringConstants.SpeedTargetMs, "N0")} ms) while the candidate ran at thinking level **{candidateThinking}**. The Speed Index is advisory for this run; compare it only against runs sharing both the profile and the thinking level.");
        }

        var budgetsUsed = answers
            .Where(a => a.ToolCallBudgetUsed.HasValue)
            .Select(a => a.ToolCallBudgetUsed!.Value)
            .Distinct()
            .OrderBy(v => v)
            .ToList();
        string budgetText = budgetsUsed.Count switch
        {
            0 => run.MaxToolCallsPerQuestionUsed.HasValue
                    ? $"{run.MaxToolCallsPerQuestionUsed.Value} (flat)"
                    : "unlimited (legacy)",
            // The resource caps are flat from harness 13 on, so every answer of a current run carries the
            // same figure and this is the branch that prints.
            1 => budgetsUsed[0].ToString(),
            // Runs 1-13 resolved the budget per difficulty band and their answers carry the old per-band
            // values, so the joined form is kept for them rather than collapsed into something they never
            // ran under.
            _ => string.Join(" / ", budgetsUsed.Select(v => v.ToString())) + " (per difficulty band)"
        };
        sb.AppendLine($"- **Tool Call Budget per Question:** {budgetText}");
        sb.AppendLine($"- **Timing Mode:** {(run.SpeedMeasurementDegraded ? "Concurrent (advisory speed)" : "Sequential (comparable speed)")}");

        // Who graded whom. Three distinct providers is the arrangement with the fewest shared
        // priors; an assessor and a second opinion drawn from one provider is the arrangement in
        // which the "independent reader" is least independent, and a reader of the agreement
        // figures below needs to know which of the two produced them.
        var pairingProviders = new List<string?> { run.TestedModelSnapshot.Provider, run.AssessorModelSnapshot.Provider };
        if (isPanelRun)
        {
            pairingProviders.Add(run.CoAssessorModelSnapshot?.Provider);
        }
        if (run.SecondOpinionAssessorModelConfigurationId.HasValue)
        {
            pairingProviders.Add(run.SecondOpinionAssessorModelSnapshot?.Provider);
        }
        int distinctProviders = pairingProviders
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        if (isPanelRun)
        {
            // Each member's relation to the candidate, and whether the panel is family-balanced: two
            // members from two families leave any candidate at most one same-family grader.
            string readerPart = run.SecondOpinionAssessorModelConfigurationId.HasValue
                ? $", reference reader {run.SecondOpinionAssessorModelSnapshot?.Provider} ({FamilyRelation(run.TestedModelSnapshot.Provider, run.SecondOpinionAssessorModelSnapshot?.Provider)}, advisory)"
                : ", no reference reader";
            string balance = IsSameFamily(run.AssessorModelSnapshot.Provider, run.CoAssessorModelSnapshot?.Provider)
                ? $"Both members come from one family ({run.AssessorModelSnapshot.Provider}), so the panel is not family-balanced: a candidate of that family faces two same-family members."
                : "The members come from two families, so each candidate family has at most one same-family member.";
            sb.AppendLine($"- **Assessor Pairing:** candidate {run.TestedModelSnapshot.Provider}, panel member A {run.AssessorModelSnapshot.Provider} ({memberARelation}), panel member B {run.CoAssessorModelSnapshot?.Provider} ({memberBRelation}){readerPart} — {distinctProviders} distinct provider(s). {balance}");
            if (run.SecondOpinionAssessorModelConfigurationId.HasValue)
            {
                var readerSharesWith = new List<string>();
                if (IsSameFamily(run.AssessorModelSnapshot.Provider, run.SecondOpinionAssessorModelSnapshot?.Provider)) readerSharesWith.Add("member A");
                if (IsSameFamily(run.CoAssessorModelSnapshot?.Provider, run.SecondOpinionAssessorModelSnapshot?.Provider)) readerSharesWith.Add("member B");
                if (readerSharesWith.Count > 0)
                {
                    sb.AppendLine($"  - *The reference reader shares a provider with panel {string.Join(" and ", readerSharesWith)}, so it is not a third family: its reading of the gap between candidate families is not a neutral anchor.*");
                }
            }
        }
        else
        {
            sb.AppendLine(run.SecondOpinionAssessorModelConfigurationId.HasValue
                ? $"- **Assessor Pairing:** candidate {run.TestedModelSnapshot.Provider}, assessor {run.AssessorModelSnapshot.Provider}, second reader {run.SecondOpinionAssessorModelSnapshot?.Provider} — {distinctProviders} distinct provider(s)"
                : $"- **Assessor Pairing:** candidate {run.TestedModelSnapshot.Provider}, assessor {run.AssessorModelSnapshot.Provider} — {distinctProviders} distinct provider(s), no second reader");
            if (run.SecondOpinionAssessorModelConfigurationId.HasValue &&
                string.Equals(run.AssessorModelSnapshot.Provider, run.SecondOpinionAssessorModelSnapshot?.Provider, StringComparison.OrdinalIgnoreCase))
            {
                sb.AppendLine("  - *The assessor and the second reader come from the same provider, so the second verdict is a weaker check than a cross-provider one: two models from one family share training data and failure modes, and can agree for reasons that have nothing to do with the answer.*");
            }
        }
        sb.AppendLine($"- **Candidate System Prompt SHA-256:** {run.CandidateSystemPromptSha256 ?? "not recorded"}");
        sb.AppendLine($"- **Candidate System Prompt Text:** {(string.IsNullOrEmpty(run.CandidateSystemPromptText) ? "not stored" : $"stored ({run.CandidateSystemPromptText.Length:N0} characters)")}");
        sb.AppendLine($"- **ToolGuides SHA-256:** {run.ToolGuidesSha256 ?? "not recorded"}");
        sb.AppendLine($"- **Knowledge Base HEAD SHA:** {run.KnowledgeBaseHeadSha ?? "not recorded"}");
        sb.AppendLine($"- **GnollHack Wiki HEAD SHA:** {run.WikiHeadSha ?? "not recorded"}");
        sb.AppendLine($"- **GnollHack Source HEAD SHA:** {run.SourceCodeHeadSha ?? "not recorded"}");
        sb.AppendLine("  - *Two runs are a reproduction only when `CandidateSystemPromptSha256` matches.*");
        const string CorpusFingerprintsLabel = "- **Corpus Index Fingerprints** (what each index held; provenance, not a comparability key):";
        if (CorpusFingerprintLines(run.CorpusIndexFingerprintsJson) is { } corpusLines)
        {
            sb.AppendLine(CorpusFingerprintsLabel);
            foreach (string line in corpusLines)
            {
                sb.AppendLine($"  - {line}");
            }
        }
        else
        {
            sb.AppendLine($"{CorpusFingerprintsLabel} not recorded");
        }

        // A re-run that repairs answers under a system prompt or ToolGuides build different from
        // the run's own leaves the run graded on two instruments rather than one, and this is the
        // only place that fact survives: per-answer, only which answers the re-run touched is known,
        // not which instrument produced them, so the caution is stated at run level. A re-run under the
        // same prompt instrument is still disclosed: its answers came from the re-run's harness build.
        bool rerunPromptDiffers = !string.IsNullOrWhiteSpace(run.RerunCandidateSystemPromptSha256) &&
            !string.Equals(run.RerunCandidateSystemPromptSha256, run.CandidateSystemPromptSha256, StringComparison.Ordinal);
        bool rerunToolGuidesDiffers = !string.IsNullOrWhiteSpace(run.RerunToolGuidesSha256) &&
            !string.Equals(run.RerunToolGuidesSha256, run.ToolGuidesSha256, StringComparison.Ordinal);
        if (run.RerunStartedAtUtc.HasValue || rerunPromptDiffers || rerunToolGuidesDiffers)
        {
            string rerunSpan = (run.RerunStartedAtUtc.HasValue || run.RerunCompletedAtUtc.HasValue)
                ? RerunSpan(run)
                : "not recorded";
            string rerunHarness = run.RerunHarnessVersion ?? "not recorded (re-run predates harness 22)";
            string runHarness = run.HarnessVersion ?? "1 (unversioned legacy)";
            sb.AppendLine();
            if (rerunPromptDiffers || rerunToolGuidesDiffers)
            {
                sb.AppendLine($"> **Re-run under a different instrument.** This run was repaired by a re-run recorded under Candidate System Prompt SHA-256 `{run.RerunCandidateSystemPromptSha256 ?? run.CandidateSystemPromptSha256 ?? "not recorded"}` and ToolGuides SHA-256 `{run.RerunToolGuidesSha256 ?? run.ToolGuidesSha256 ?? "not recorded"}`, against this run's own `{run.CandidateSystemPromptSha256 ?? "not recorded"}` and `{run.ToolGuidesSha256 ?? "not recorded"}`, under harness {rerunHarness} (this run: {runHarness}). **This is not a clean reproduction half:** the answers the re-run repaired were produced under the re-run instrument, and the rest under the run's own. The re-run's own span was {rerunSpan} — distinct from the run's {FormatDuration(run.TotalDurationMs)} total elapsed wall time above.");
            }
            else
            {
                sb.AppendLine($"> **Repaired by a re-run** from {rerunSpan} under harness {rerunHarness} (this run: {runHarness}). Candidate System Prompt and ToolGuides SHA-256 matched the run's own, so the answers are on one prompt instrument; the harness build differs where the versions differ.");
            }
        }

        // What each re-executed answer replaced; recorded from harness 33.
        var reExecutedAnswers = answers.Where(a => a.RerunAtUtc.HasValue).ToList();
        if (reExecutedAnswers.Count > 0)
        {
            if (!(run.RerunStartedAtUtc.HasValue || rerunPromptDiffers || rerunToolGuidesDiffers))
            {
                sb.AppendLine();
            }
            foreach (string line in ReExecutedAnswerLines(reExecutedAnswers))
            {
                sb.AppendLine($"> - {line}");
            }
            sb.AppendLine("> The replaced attempts' tool-call records are not kept.");
        }
        sb.AppendLine();

        sb.AppendLine("### Model Under Test");
        sb.AppendLine($"- **Display Name:** {run.TestedModelSnapshot.Label()}");
        sb.AppendLine($"- **Provider:** {run.TestedModelSnapshot.Provider}");
        sb.AppendLine($"- **Model ID:** {run.TestedModelSnapshot.ModelId}");
        sb.AppendLine($"- **Endpoint:** {SystemAiConfigurationSnapshotStore.DescribeEndpoint(run.TestedModelSnapshot)}");
        sb.AppendLine($"- **Thinking Level:** {run.TestedModelSnapshot.ThinkingLevel ?? "Default"}");
        sb.AppendLine($"- **Reasoning Mode:** {run.TestedModelSnapshot.ReasoningMode ?? "Default"}");
        sb.AppendLine($"- **Reasoning Summary:** {run.TestedModelSnapshot.ReasoningSummary ?? "Default"}");
        sb.AppendLine($"- **Requested Service Tier:** {run.TestedModelSnapshot.ServiceTier ?? "Default"}");
        sb.AppendLine($"- **Max Output Tokens:** {(run.TestedModelSnapshot.MaxOutputTokens.HasValue ? run.TestedModelSnapshot.MaxOutputTokens.Value.ToString() : "Default")}");
        sb.AppendLine($"- **Parallel Tool Calls:** {run.TestedModelSnapshot.ParallelExecutionMode ?? MobileGnollHackLogger.Data.ParallelExecutionMode.Enabled} *(provider-side tool batching)*");
        sb.AppendLine();

        sb.AppendLine("### Chat Prompt Under Test");

        if (string.IsNullOrWhiteSpace(run.CandidatePromptOptionsJson))
        {
            sb.AppendLine("The candidate is graded under the **production Overseer chat system prompt** (`ChatService.BuildSystemPrompt`), not a benchmark-specific prompt. Every quality verdict below is a verdict on the prompt real users receive.");
            sb.AppendLine();
            sb.AppendLine($"- **Tool batching policy:** {run.TestedModelSnapshot.ParallelExecutionMode ?? MobileGnollHackLogger.Data.ParallelExecutionMode.Enabled} — {ParallelPolicyDescription(run.TestedModelSnapshot.ParallelExecutionMode ?? MobileGnollHackLogger.Data.ParallelExecutionMode.Enabled)}");
            sb.AppendLine("- *Configuration not recorded for this run.*");
        }
        else
        {
            var promptOpts = BenchmarkCandidatePromptOptions.FromJson(run.CandidatePromptOptionsJson);
            string modeStr = promptOpts.OverseerMode == 0 ? "Gameplay Help" : $"Mode {promptOpts.OverseerMode}";
            string styleStr = promptOpts.VerboseMode ? "detailed (`verboseMode: true`)" : "concise (`verboseMode: false`)";
            string toolsStr = promptOpts.EnableToolUse ? "enabled" : "disabled";
            string webStr = promptOpts.EnableWebSearch ? "enabled" : "disabled";
            string subagentsStr = promptOpts.EnableSubAgents ? "enabled" : "disabled";
            string srcStr = promptOpts.AllowSourceCodeReferences ? "allowed" : "disallowed";
            string spoilerStr = promptOpts.SpoilerFreeMode ? "on" : "off";
            string activeGameStr = promptOpts.IsGameOn ? "yes" : "no";
            string historyStr = promptOpts.HasMessageHistory ? "yes" : "no";

            sb.AppendLine("The candidate is graded under the **production Overseer chat system prompt** (`ChatService.BuildSystemPrompt`), not a benchmark-specific prompt. Every quality verdict below is a verdict on the prompt real users receive with these settings.");
            sb.AppendLine();
            sb.AppendLine($"- **Mode:** {modeStr} · **Response style:** {styleStr}");
            sb.AppendLine($"- **Tools:** {toolsStr} · **Web search:** {webStr} · **Subagents:** {subagentsStr} · **Source code references:** {srcStr}");
            if (promptOpts.AllowSourceCodeReferences)
            {
                sb.AppendLine($"  - {SourceCodeReferencesAllowedNote}");
            }
            sb.AppendLine($"- **Spoiler-free mode:** {spoilerStr} · **Active game:** {activeGameStr} · **Message history:** {historyStr} · **Game snapshot:** {(promptOpts.HasGameSnapshot ? "yes" : "no")}");
            sb.AppendLine($"- **Tool batching policy:** {run.TestedModelSnapshot.ParallelExecutionMode ?? MobileGnollHackLogger.Data.ParallelExecutionMode.Enabled} — {ParallelPolicyDescription(run.TestedModelSnapshot.ParallelExecutionMode ?? MobileGnollHackLogger.Data.ParallelExecutionMode.Enabled)}");
            sb.AppendLine("- **Pre-injected wiki context:** none — live chat pre-injects relevant articles, so this run is a strictly harder configuration than production and its tool counts are an upper bound on chat's.");
            string? delivery = DeliveryStatement(run, promptOpts.HasGameSnapshot);
            if (delivery != null)
            {
                sb.AppendLine($"- **Delivery:** {delivery}");
            }
            string? boardDelivery = BoardDeliveryLine(run, answers);
            if (boardDelivery != null)
            {
                sb.AppendLine($"  - {boardDelivery}");
            }
            sb.AppendLine();
            sb.AppendLine("*Configurations differ in what they measure. Two runs are comparable on Completeness, Conciseness and Readability only if this block matches.*");
        }
        sb.AppendLine();

        sb.AppendLine(isPanelRun ? "### Assessor (Panel Member A)" : "### Assessment Model");
        sb.AppendLine($"- **Display Name:** {run.AssessorModelSnapshot.Label()}");
        sb.AppendLine($"- **Provider:** {run.AssessorModelSnapshot.Provider}");
        sb.AppendLine($"- **Model ID:** {run.AssessorModelSnapshot.ModelId}");
        string assessorEndpoint = SystemAiConfigurationSnapshotStore.DescribeEndpoint(run.AssessorModelSnapshot);
        if (assessorEndpoint != "official")
        {
            sb.AppendLine($"- **Endpoint:** {assessorEndpoint}");
        }
        sb.AppendLine($"- **Thinking Level:** {run.AssessorModelSnapshot.ThinkingLevel ?? "Default"}");
        sb.AppendLine($"- **Reasoning Mode:** {run.AssessorModelSnapshot.ReasoningMode ?? "Default"}");
        if (isPanelRun)
        {
            sb.AppendLine($"- **Role:** Panel member A ({memberARelation} to the candidate) — grades every answer blind to member B, with the identical prompt; the published score is the mean of both members' quality scores.");
        }
        sb.AppendLine();

        if (isPanelRun)
        {
            sb.AppendLine("### Co-Assessor (Panel Member B)");
            sb.AppendLine($"- **Display Name:** {run.CoAssessorModelSnapshot.Label()}");
            sb.AppendLine($"- **Provider:** {run.CoAssessorModelSnapshot?.Provider}");
            sb.AppendLine($"- **Model ID:** {run.CoAssessorModelSnapshot?.ModelId}");
            string coAssessorEndpoint = SystemAiConfigurationSnapshotStore.DescribeEndpoint(run.CoAssessorModelSnapshot);
            if (coAssessorEndpoint != "official")
            {
                sb.AppendLine($"- **Endpoint:** {coAssessorEndpoint}");
            }
            sb.AppendLine($"- **Thinking Level:** {run.CoAssessorModelSnapshot?.ThinkingLevel ?? "Default"}");
            sb.AppendLine($"- **Reasoning Mode:** {run.CoAssessorModelSnapshot?.ReasoningMode ?? "Default"}");
            sb.AppendLine($"- **Role:** Panel member B ({memberBRelation} to the candidate) — grades every answer blind to member A, with the identical prompt, and writes its own synthesis from its own verdicts.");
            sb.AppendLine();
        }

        // Named whether or not one was used: "no second opinion" is itself a fact about how the
        // run was graded, and a reader comparing two runs needs to know which had one.
        sb.AppendLine($"### {readerTitle}");
        if (run.SecondOpinionAssessorModelConfigurationId.HasValue)
        {
            sb.AppendLine($"- **Display Name:** {run.SecondOpinionAssessorModelSnapshot.Label()}");
            sb.AppendLine($"- **Provider:** {run.SecondOpinionAssessorModelSnapshot?.Provider}");
            sb.AppendLine($"- **Model ID:** {run.SecondOpinionAssessorModelSnapshot?.ModelId}");
            string secondOpinionEndpoint = SystemAiConfigurationSnapshotStore.DescribeEndpoint(run.SecondOpinionAssessorModelSnapshot);
            if (secondOpinionEndpoint != "official")
            {
                sb.AppendLine($"- **Endpoint:** {secondOpinionEndpoint}");
            }
            sb.AppendLine($"- **Thinking Level:** {run.SecondOpinionAssessorModelSnapshot?.ThinkingLevel ?? "Default"}");
            sb.AppendLine($"- **Reasoning Mode:** {run.SecondOpinionAssessorModelSnapshot?.ReasoningMode ?? "Default"}");
            var configuredMode = ModeOf(run);
            if (isPanelRun)
            {
                // A panel run forces the reader to every answer, blind.
                sb.AppendLine("- **Mode:** All, blind — reference reader: advisory, never scores; compared against the panel score.");
            }
            else
            {
                sb.AppendLine($"- **Mode:** {configuredMode}{ModeGloss(configuredMode)} Advisory throughout: the first verdict is what scored.");
            }
            if (!isPanelRun && configuredMode is BenchmarkSecondOpinionMode.Flagged or BenchmarkSecondOpinionMode.FlaggedAndOutliers)
            {
                sb.AppendLine($"- **Triggers:** a critical error; a refuted claim; a contested verdict; an out-of-rubric Accuracy deduction (an accuracy level of {BenchmarkVerdictConsistency.UnevidencedDeductionMaxLevel} or below whose deduction rests on the assessor's own knowledge rather than the rubric); an unevidenced deduction (a level docked to {BenchmarkVerdictConsistency.UnevidencedDeductionMaxLevel} or below whose stated evidence names no defect, or rests only on unverifiability); an omission docked as an accuracy defect; a dimension outlier (one level at 1 or 0 beside three at {BenchmarkVerdictConsistency.DimensionOutlierCompanionMinLevel} or above, with no defect of that kind named); unverifiable claims alongside an accuracy level of {BenchmarkService.UnverifiedClaimsAccuracyMaxLevel} or below; a quality score below the profile's threshold of {scoringConstants.SecondOpinionQualityThreshold}" +
                    (configuredMode == BenchmarkSecondOpinionMode.FlaggedAndOutliers
                        ? $"; and, after scoring, any answer more than {scoringConstants.SecondOpinionOutlierDeltaPoints} points below the run's median."
                        : "."));
            }
        }
        else if (isPanelRun)
        {
            // Both members grade every answer, so no trigger-selected reading was forgone.
            sb.AppendLine("- **None selected.** No answer in this run was read by a reference reader.");
        }
        else
        {
            sb.AppendLine("- **None selected.** No answer in this run was read by a second reader.");

            // What was forgone, stated in the run's own numbers. The 2026-09-03 run produced
            // two critical errors with no second opinion selected — precisely the trigger the
            // feature exists for — and nothing in the report connected the two facts. It was
            // also silent about the larger number: double grading would have covered every
            // answer, and that is the only setting that measures grader agreement instead of
            // sampling it.
            int threshold = scoringConstants.SecondOpinionQualityThreshold;

            // Mirrors the first-match cascade in BenchmarkService.ResolveSecondOpinionTrigger,
            // so the counts partition the answers rather than double-counting one that satisfies
            // two triggers.
            string? WouldTrigger(BenchmarkRunAnswer a)
            {
                if (a.CriticalError) return "critical error";
                if ((((BenchmarkAnswerFlags)a.AnswerFlags) & BenchmarkAnswerFlags.RefutedClaim) != 0) return "refuted claim";
                if ((((BenchmarkAnswerFlags)a.AnswerFlags) & BenchmarkAnswerFlags.ContestedVerdict) != 0) return "contested verdict";
                if ((((BenchmarkAnswerFlags)a.AnswerFlags) & BenchmarkAnswerFlags.UnevidencedDeduction) != 0) return "unevidenced deduction";
                if ((((BenchmarkAnswerFlags)a.AnswerFlags) & BenchmarkAnswerFlags.OmissionAsAccuracy) != 0) return "omission docked as accuracy";
                if ((a.UnverifiedClaimCount ?? 0) > 0 && (a.AccuracyLevel ?? 6) <= BenchmarkService.UnverifiedClaimsAccuracyMaxLevel
                    && ((a.ClaimsRefutedCount ?? 0) > 0 || (a.ClaimsIndeterminateCount ?? 0) > 0 || (a.ClaimVerificationJson == null && a.ClaimVerificationError == null))) return "unverifiable claims";
                if (threshold > 0 && IndexQualityOf(a) is double published && published < threshold) return $"below the profile's threshold of {threshold}";
                return null;
            }

            var wouldByTrigger = answers
                .Select(WouldTrigger)
                .Where(t => t != null)
                .GroupBy(t => t!, StringComparer.Ordinal)
                .OrderByDescending(g => g.Count())
                .ThenBy(g => g.Key, StringComparer.Ordinal)
                .ToList();
            int wouldTotal = wouldByTrigger.Sum(g => g.Count());
            if (wouldTotal > 0)
            {
                sb.AppendLine($"- **{wouldTotal} answer(s) would have been read by a second reader** under `Flagged`, had one been selected ({string.Join("; ", wouldByTrigger.Select(g => $"{g.Key}: {g.Count()}"))}).");
            }

            int answeredForForgone = answers.Count(a => a.Status == BenchmarkAnswerStatus.Ok);
            if (answeredForForgone > 0)
            {
                sb.AppendLine($"- **{answeredForForgone} answer(s) would have been graded twice** under `All` (double grading) — the only mode that measures grader agreement rather than sampling it. `FlaggedAndOutliers` would have added a post-scoring sweep for answers more than {scoringConstants.SecondOpinionOutlierDeltaPoints} points below the run's median.");
            }
        }
        sb.AppendLine();

        sb.AppendLine("### Claim Verifier");
        if (run.ClaimVerifierModelConfigurationId.HasValue)
        {
            sb.AppendLine($"- **Display Name:** {run.ClaimVerifierModelSnapshot.Label()}");
            sb.AppendLine($"- **Provider:** {run.ClaimVerifierModelSnapshot?.Provider}");
            sb.AppendLine($"- **Model ID:** {run.ClaimVerifierModelSnapshot?.ModelId}");
            string claimVerifierEndpoint = SystemAiConfigurationSnapshotStore.DescribeEndpoint(run.ClaimVerifierModelSnapshot);
            if (claimVerifierEndpoint != "official")
            {
                sb.AppendLine($"- **Endpoint:** {claimVerifierEndpoint}");
            }
            sb.AppendLine($"- **Thinking Level:** {run.ClaimVerifierModelSnapshot?.ThinkingLevel ?? "Default"}");
            sb.AppendLine($"- **Reasoning Mode:** {run.ClaimVerifierModelSnapshot?.ReasoningMode ?? "Default"}");
            sb.AppendLine("- **Role:** Verifies unverified factual claims against source code and wiki using read-only tools. Advisory throughout: nothing here is read by any scoring path.");
            if (!string.IsNullOrWhiteSpace(run.TestedModelSnapshot.Provider) &&
                string.Equals(run.TestedModelSnapshot.Provider, run.ClaimVerifierModelSnapshot?.Provider, StringComparison.OrdinalIgnoreCase))
            {
                sb.AppendLine("  - *The verifier and the candidate come from the same provider: the tools supply the evidence rather than the model's memory, so this is not worthless — but it is the weakest available pairing.*");
            }
            if (run.SecondOpinionAssessorModelConfigurationId.HasValue &&
                (run.SecondOpinionAssessorModelConfigurationId == run.ClaimVerifierModelConfigurationId ||
                 (!string.IsNullOrWhiteSpace(run.SecondOpinionAssessorModelSnapshot?.ModelId) &&
                  string.Equals(run.SecondOpinionAssessorModelSnapshot?.ModelId, run.ClaimVerifierModelSnapshot?.ModelId, StringComparison.OrdinalIgnoreCase))))
            {
                sb.AppendLine($"  - *Same model as the {readerName}. Under blind mode the {readerName} is given the verification findings, so a refuted claim and a harsh {readerName} verdict on the same answer are one finding, not two independent ones.*");
            }
        }
        else
        {
            int unverifiedTotalClaims = answers.Where(a => (a.UnverifiedClaimCount ?? 0) > 0).Sum(a => a.UnverifiedClaimCount!.Value);
            int unverifiedAnswerCount = answers.Count(a => (a.UnverifiedClaimCount ?? 0) > 0);
            if (unverifiedTotalClaims > 0)
            {
                sb.AppendLine($"- **None selected.** No answer had its unverified claims checked against the source or wiki ({unverifiedTotalClaims} claim(s) across {unverifiedAnswerCount} answer(s) went unchecked).");
            }
            else
            {
                sb.AppendLine("- **None selected.** No answer in this run had its unverified claims checked against the source or wiki.");
            }
        }
        sb.AppendLine();

        // 3. Results Summary
        var scoredAnswers = answers.Where(a => a.Status == BenchmarkAnswerStatus.Ok && IndexQualityOf(a).HasValue).ToList();

        // The item set the quality indices are computed over: graded answers, and the questions the
        // model failed to answer, at 0. Wider than scoredAnswers, which stays the set a grader
        // actually read and therefore the set the dimensional averages are taken over. Both are
        // needed: an index over a different item set than the run's stored one would put two
        // disagreeing numbers on one run.
        var indexAnswers = answers
            .Where(a => BenchmarkRunFinalizer.CountsTowardQualityIndex(a) && IndexQualityOf(a).HasValue)
            .ToList();

        var unansweredAnswers = answers
            .Where(BenchmarkRunFinalizer.IsModelProducedEmptyAnswer)
            .OrderBy(a => a.OrderIndex)
            .ToList();

        // BenchmarkRunFinalizer.Apply withholds the Quality and Speed Indices below when this is
        // greater than zero (harness version 21). A run finalised before that version never recorded
        // TerminalFailureAnswerCount, so it is recomputed here rather than trusted as zero.
        int terminalFailureCount = run.TerminalFailureAnswerCount ?? answers.Count(BenchmarkRunFinalizer.HasTerminalFailure);

        var rawScorableItems = indexAnswers
            .Select(a => (BenchmarkScoring.IndexRawQuality(a, isPanelRun), (int?)(a.AssessedDifficulty ?? BenchmarkRunFinalizer.FallbackDifficulty(a.Difficulty))))
            .ToList();
        int? rawQualityIndex = BenchmarkScoring.QualityIndex(rawScorableItems);
        int cappedCount = scoredAnswers.Count(a =>
            BenchmarkScoring.IndexRawQuality(a, isPanelRun) is double raw && IndexQualityOf(a) is double published && raw > published);

        sb.AppendLine("## 2. Results Summary");
        sb.AppendLine();

        double? se = run.QualityIndexStandardError;
        if (!se.HasValue && indexAnswers.Count >= 3)
        {
            se = BenchmarkScoring.QualityIndexStandardError(
                indexAnswers.Select(a => (IndexQualityOf(a), (int?)(a.AssessedDifficulty ?? BenchmarkRunFinalizer.FallbackDifficulty(a.Difficulty)))));
        }

        string seText = string.Empty;
        int ciLower = 0, ciUpper = 100;
        if (se.HasValue && run.QualityIndex.HasValue)
        {
            double halfWidth = 1.96 * se.Value;
            ciLower = (int)Math.Max(0, Math.Round(run.QualityIndex.Value - halfWidth));
            ciUpper = (int)Math.Min(100, Math.Round(run.QualityIndex.Value + halfWidth));
            seText = $" ± {halfWidth:F0} (95% CI over {indexAnswers.Count} items)";
        }

        sb.AppendLine($"### **Intelligence Index: {IndexHeadline(run.QualityIndex, $"{seText} / 100", terminalFailureCount, run.TotalQuestionCount, "Not Scored")}**");
        if (isPanelRun)
        {
            string readerIndexText;
            if (run.SecondOpinionAssessorModelConfigurationId.HasValue)
            {
                int? readerIndex = ReferenceReaderIndex(run, answers, out int readerCovered);
                readerIndexText = readerIndex.HasValue
                    ? $"{readerIndex.Value} / 100" + (readerCovered < indexAnswers.Count ? $" (over {readerCovered} of {indexAnswers.Count} items)" : string.Empty)
                    : "not graded";
            }
            else
            {
                readerIndexText = "none configured";
            }
            string memberAIndexText = run.AssessorOnlyQualityIndex.HasValue ? $"{run.AssessorOnlyQualityIndex.Value} / 100" : "not recorded";
            string memberBIndexText = run.CoAssessorOnlyQualityIndex.HasValue ? $"{run.CoAssessorOnlyQualityIndex.Value} / 100" : "not recorded";
            sb.AppendLine($"- **Panel:** mean of both members' per-answer quality. Member A alone: {memberAIndexText}. Member B alone: {memberBIndexText}. Reference reader (advisory): {readerIndexText}.");
        }
        // A critical error caps Quality at 25 (see BenchmarkScoring), which the Raw/Intelligence
        // Index pair below already shows as a point delta — but that delta is diluted by every
        // *other* answer's difficulty weight, so a single hallucinated answer can move the index
        // by as little as one point (see the "How to read these" note under Final Indices). The
        // Critical Errors line gives the reader the actual count instead of asking them to infer it
        // from a small index shift.
        // "Applied" means the cap actually fired (CriticalError == true), independent of whether
        // anyone disputed it — a run whose every cap is disputed must still say 2, not 0, or the
        // reader cannot tell a capped-and-unchallenged answer from a capped-and-contested one. In a
        // panel run a cap from either member lowers the panel score, so either member's counts.
        // From scoring method 13 it is a confirmed critical error (Agreed, UpheldByVerifier or
        // SingleAssessor); an unresolved or overturned split is named separately.
        bool resolvesCriticalErrors = BenchmarkCriticalErrorResolver.Applies(run);
        bool AnyMemberFlagged(BenchmarkRunAnswer a) => a.CriticalError || (isPanelRun && a.CoAssessmentCriticalError == true);
        bool CapApplied(BenchmarkRunAnswer a) => resolvesCriticalErrors ? BenchmarkCriticalErrorResolver.IsConfirmed(a) : AnyMemberFlagged(a);
        var appliedCriticalAnswers = answers.Where(CapApplied).OrderBy(a => a.OrderIndex).ToList();
        var outcomeSummary = BenchmarkOutcomeSummary.Compute(run, answers);

        // Split direction matters: an applied cap the second reader disagreed with is a different
        // claim from a critical error the second reader raised on its own initiative.
        var disputedBySecondReader = appliedCriticalAnswers
            .Where(a => a.SecondOpinionCriticalError == false)
            .OrderBy(a => a.OrderIndex)
            .ToList();
        var raisedOnlyBySecondReader = answers
            .Where(a => !AnyMemberFlagged(a) && a.SecondOpinionCriticalError == true)
            .OrderBy(a => a.OrderIndex)
            .ToList();
        bool criticalErrorsLinePrints = appliedCriticalAnswers.Count > 0 || disputedBySecondReader.Count > 0 || raisedOnlyBySecondReader.Count > 0
            || (outcomeSummary != null && (outcomeSummary.ClassifiedCount > 0 || outcomeSummary.UnresolvedCriticalErrorCount > 0 || outcomeSummary.OverturnedCriticalErrorCount > 0));

        if (se.HasValue && run.QualityIndex.HasValue)
        {
            sb.AppendLine("*This reflects finite item-sampling uncertainty — how much the index would move under a different draw of questions of the same difficulty profile. Two runs whose intervals overlap are statistically indistinguishable on this suite.*");
            // H12. The interval is a dispersion measure over the same difficulty-weighted scores, so a
            // capped answer enters it as a large squared deviation weighted by assessed difficulty
            // squared. A run with critical errors therefore reports a wide interval for a reason that is
            // not item sampling, and reading the width as sampling noise understates the failure. The
            // formula is unchanged — only what the reader is told about it.
            if (cappedCount > 0)
            {
                sb.AppendLine();
                string criticalErrorsPointer = !isPanelRun || criticalErrorsLinePrints
                    ? "Read the **Critical Errors** count below for that failure mode; do"
                    : "Do";
                sb.AppendLine($"> ⚠️ **The interval is inflated by {cappedCount} capped answer(s).** A critical-error cap replaces a score with 25, and that deviation enters the variance weighted by the item's assessed difficulty *squared*, so most of this width is those answers rather than question sampling. {criticalErrorsPointer} not read the width as noise.");
            }
            sb.AppendLine();
        }
        // Only shown when a critical-error cap actually moved it. Printing the identical number
        // under a second heading told the reader nothing.
        if (rawQualityIndex.HasValue && rawQualityIndex.Value != (run.QualityIndex ?? 0))
        {
            sb.AppendLine($"### **Raw Quality Index: {rawQualityIndex.Value} / 100 ({cappedCount} question(s) whose score the cap lowered)**");
        }

        // The gap between the difficulty-weighted index and the plain mean of the same scores is
        // how much the weighting moved the headline, and it is invisible from either number
        // alone. On the 2026-09-03 run it moved the index *up* by two points, because the
        // model's two weakest answers were also two of its easiest questions. Computed here for
        // runs that predate the stored column, so the line works on the whole archive.
        int? unweightedMean = run.UnweightedQualityIndex
            ?? BenchmarkScoring.UnweightedQualityMean(indexAnswers.Select(IndexQualityOf));
        if (unweightedMean.HasValue && run.QualityIndex.HasValue &&
            Math.Abs(run.QualityIndex.Value - unweightedMean.Value) >= 1)
        {
            int weightingDelta = run.QualityIndex.Value - unweightedMean.Value;
            sb.AppendLine();
            sb.AppendLine($"- **Unweighted Quality Mean:** {unweightedMean.Value} / 100 — difficulty weighting moved the index by **{(weightingDelta > 0 ? "+" : string.Empty)}{Inv(weightingDelta)}** points. The Intelligence Index weights each answer by its assessed difficulty, so a run whose weak answers are its easy ones reads higher than its plain average.");
            sb.AppendLine();
        }

        // ContestedCriticalError is set only for a critical-error adjudication (CriticalError ==
        // true, see BenchmarkService.IsCriticalErrorAdjudication) whose quote the claim verifier
        // checked against the source or wiki and found supported — so it is always a subset of
        // appliedCriticalAnswers, never of raisedOnlyBySecondReader.
        var verifierSupportedQuoteAnswers = appliedCriticalAnswers
            .Where(a => ((BenchmarkAnswerFlags)a.AnswerFlags).HasFlag(BenchmarkAnswerFlags.ContestedCriticalError))
            .OrderBy(a => a.OrderIndex)
            .ToList();

        // § 7 Final Indices prints this same figure; one computation, so the two cannot drift.
        int? sensitivityIndex = null;

        // Answers whose Accuracy was docked out of rubric and whose claims the verifier then
        // supported — none refuted, none left indeterminate. This is the instrument's own share of
        // the Accuracy shortfall, the way OUT-OF-SCOPE is of Completeness: on run 40 seven-plus
        // answers were docked citing only claims the rubric did not cover, and every one the
        // verifier checked came back supported. The Assessor Findings block below names them and
        // the sensitivity index beside the contested one prices them; both are advisory and move
        // no score. § 7 prints the index from this same variable, so the two cannot drift.
        var verificationClearedAnswers = answers
            .Where(BenchmarkService.IsVerificationClearedAccuracyDeduction)
            .OrderBy(a => a.OrderIndex)
            .ToList();
        int? verificationClearedIndex = null;

        // The Readability counterpart of verificationClearedAnswers above: answers whose only
        // Readability basis was a rubric FORM suggestion the answer did not adopt, so the level
        // was docked with no defect named. § 7 Final Indices prints the index from this same
        // variable, so the two cannot drift.
        var formClearedAnswers = scoredAnswers
            .Where(a => a.ReadabilityFormOnly &&
                BenchmarkVerdictConsistency.IsFormOnlyDeduction(a.ReadabilityLevel ?? 0, ReadEvidence(a).Readability, a.ReadabilityFormOnly))
            .OrderBy(a => a.OrderIndex)
            .ToList();
        int? formClearedIndex = null;

        // Every critical-error split, in either direction, is resolved at the second reader's score.
        var splitAnswers = disputedBySecondReader.Concat(raisedOnlyBySecondReader).ToList();

        // The critical-error count and the sensitivity figures sit with the Intelligence Index they
        // qualify, ahead of the Speed Index, in the order § 7 Final Indices prints them.

        if (outcomeSummary != null && criticalErrorsLinePrints)
        {
            // Scoring method 13: confirmed critical errors with their resolution, the rate over the
            // classified answers with its Wilson interval, then the splits that stayed averaged and
            // those the claim verifier overturned.
            var criticalErrorsLine = new StringBuilder($"- **Critical Errors:** {appliedCriticalAnswers.Count} confirmed");
            if (appliedCriticalAnswers.Count > 0)
            {
                criticalErrorsLine.Append($" ({string.Join("; ", appliedCriticalAnswers.Select(a => $"Q{a.OrderIndex}, {ConfirmedResolutionText(a.CriticalErrorResolution)}"))})");
            }
            if (outcomeSummary.CriticalErrorRate is double rate)
            {
                string interval = outcomeSummary.CriticalErrorRateLow is double low && outcomeSummary.CriticalErrorRateHigh is double high
                    ? $"95% CI {PercentNumber(low)}–{PercentText(high)}, "
                    : string.Empty;
                criticalErrorsLine.Append($" · rate {PercentText(rate)} ({interval}{outcomeSummary.ConfirmedCriticalErrorCount} of {outcomeSummary.ClassifiedCount})");
            }
            if (outcomeSummary.UnresolvedCriticalErrorCount > 0)
            {
                criticalErrorsLine.Append($" · {outcomeSummary.UnresolvedCriticalErrorCount} split(s) unresolved, averaged ({string.Join(", ", outcomeSummary.UnresolvedCriticalErrorQuestions.Select(q => $"Q{q}"))})");
            }
            if (outcomeSummary.OverturnedCriticalErrorCount > 0)
            {
                criticalErrorsLine.Append($" · {outcomeSummary.OverturnedCriticalErrorCount} overturned by the claim verifier ({string.Join(", ", outcomeSummary.OverturnedCriticalErrorQuestions.Select(q => $"Q{q}"))})");
            }
            if (disputedBySecondReader.Count > 0)
            {
                criticalErrorsLine.Append($" — {disputedBySecondReader.Count} disputed by the {readerName} (question(s) {string.Join(", ", disputedBySecondReader.Select(a => a.OrderIndex))})");
            }
            if (raisedOnlyBySecondReader.Count > 0)
            {
                criticalErrorsLine.Append($"; {raisedOnlyBySecondReader.Count} raised only by the {readerName} (question(s) {string.Join(", ", raisedOnlyBySecondReader.Select(a => a.OrderIndex))})");
            }
            if (verifierSupportedQuoteAnswers.Count > 0)
            {
                criticalErrorsLine.Append($"; verifier-supported quote(s): {string.Join(", ", verifierSupportedQuoteAnswers.Select(a => $"Q{a.OrderIndex}"))}");
            }
            sb.AppendLine(criticalErrorsLine.ToString());
        }
        else if (outcomeSummary == null && (appliedCriticalAnswers.Count > 0 || splitAnswers.Count > 0))
        {
            var criticalErrorsLine = new StringBuilder($"- **Critical Errors:** {appliedCriticalAnswers.Count} applied");
            if (appliedCriticalAnswers.Count > 0)
            {
                criticalErrorsLine.Append($" (question(s) {string.Join(", ", appliedCriticalAnswers.Select(a => a.OrderIndex))})");
            }
            if (isPanelRun && appliedCriticalAnswers.Count > 0)
            {
                int byA = appliedCriticalAnswers.Count(a => a.CriticalError);
                int byB = appliedCriticalAnswers.Count(a => a.CoAssessmentCriticalError == true);
                criticalErrorsLine.Append($" — flagged by member A on {byA}, member B on {byB}");
            }
            if (disputedBySecondReader.Count > 0)
            {
                criticalErrorsLine.Append($" — {disputedBySecondReader.Count} disputed by the {readerName} (question(s) {string.Join(", ", disputedBySecondReader.Select(a => a.OrderIndex))})");
            }
            if (raisedOnlyBySecondReader.Count > 0)
            {
                criticalErrorsLine.Append($"; {raisedOnlyBySecondReader.Count} raised only by the {readerName} (question(s) {string.Join(", ", raisedOnlyBySecondReader.Select(a => a.OrderIndex))})");
            }
            if (verifierSupportedQuoteAnswers.Count > 0)
            {
                criticalErrorsLine.Append($"; verifier-supported quote(s): {string.Join(", ", verifierSupportedQuoteAnswers.Select(a => $"Q{a.OrderIndex}"))}");
            }
            sb.AppendLine(criticalErrorsLine.ToString());
        }

        // Scoring method 13: every classified answer by outcome class.
        if (outcomeSummary != null)
        {
            int attempted = outcomeSummary.CorrectCount + outcomeSummary.PartialCount + outcomeSummary.IncorrectCount;
            int abstainedOrWrong = outcomeSummary.IncorrectCount + outcomeSummary.NotAttemptedCount;
            string correctWhenAttempted = outcomeSummary.CorrectWhenAttempted is double cwa
                ? $"{PercentText(cwa)} ({outcomeSummary.CorrectCount} of {attempted})"
                : "n/a";
            string wrongInsteadOfAbstaining = outcomeSummary.WrongInsteadOfAbstaining is double wia
                ? $"{PercentText(wia)} ({outcomeSummary.IncorrectCount} of {abstainedOrWrong})"
                : "n/a";
            sb.AppendLine($"- **Outcomes:** {outcomeSummary.CorrectCount} correct, {outcomeSummary.PartialCount} partial, {outcomeSummary.IncorrectCount} incorrect, {outcomeSummary.NotAttemptedCount} not attempted, {outcomeSummary.NoAnswerCount} without an answer · correct when attempted {correctWhenAttempted} · wrong instead of abstaining {wrongInsteadOfAbstaining}");
        }

        // A panel run's verification-cleared Accuracy sensitivity lifts each member on its own
        // evidence (BenchmarkPanelSensitivity). The other three figures each re-score member A's
        // verdict alone, so a panel run states once why it has none and points at the member-alone
        // indices instead. § 7 Final Indices prints the same clauses.
        string? panelSensitivityClause = null;
        if (isPanelRun)
        {
            panelSensitivityClause = PanelSensitivityClause(run, BenchmarkPanelSensitivity.Compute(run, indexAnswers, scoringConstants));
            sb.AppendLine($"- **{panelSensitivityClause}");
            sb.AppendLine("- **Other sensitivity figures:** the contested-verdict, evidence-informed and FORM-cleared sensitivities are not computed for a panel run — each re-scores member A's verdict alone, which would give one family's judge a correction channel the other does not have; the member-alone indices above bound how much the published index depends on either member.");
        }

        // A panel run scored before method 13: the index the method 13 critical-error rule
        // (BenchmarkCriticalErrorResolver.ResolvePanel) would have published, printed only when that
        // rule settles at least one split by the claim verifier. Its panel score replaces the published
        // one on the upheld and overturned answers alone; every other answer keeps its own, which the
        // rule leaves unchanged. Over the Intelligence Index's own items and weights; nothing is
        // written. § 7 Final Indices prints the same clause.
        string? resolutionSensitivityClause = null;
        if (isPanelRun && !resolvesCriticalErrors)
        {
            int ceiling = BenchmarkCriticalErrorResolver.CeilingOf(run);
            var resolvedPanel = indexAnswers
                .Select(a => (Answer: a, Resolved: BenchmarkCriticalErrorResolver.ResolvePanel(a, ceiling)))
                .ToList();
            List<int> QuestionsResolved(BenchmarkCriticalErrorResolution resolution) => resolvedPanel
                .Where(x => x.Resolved?.Resolution == resolution)
                .Select(x => x.Answer.OrderIndex)
                .OrderBy(q => q)
                .ToList();
            var upheld = QuestionsResolved(BenchmarkCriticalErrorResolution.UpheldByVerifier);
            var overturned = QuestionsResolved(BenchmarkCriticalErrorResolution.OverturnedByVerifier);
            if (upheld.Count > 0 || overturned.Count > 0)
            {
                var unresolved = QuestionsResolved(BenchmarkCriticalErrorResolution.Unresolved);
                int? resolvedIndex = BenchmarkScoring.QualityIndex(resolvedPanel
                    .Select(x => ((x.Resolved is { } r && (r.Resolution is BenchmarkCriticalErrorResolution.UpheldByVerifier or BenchmarkCriticalErrorResolution.OverturnedByVerifier))
                                      ? (double?)((r.ScoreA + r.ScoreB) / 2.0)
                                      : IndexQualityOf(x.Answer),
                                  (int?)(x.Answer.AssessedDifficulty ?? BenchmarkRunFinalizer.FallbackDifficulty(x.Answer.Difficulty))))
                    .ToList());
                if (resolvedIndex.HasValue)
                {
                    var settled = new List<string>();
                    if (upheld.Count > 0)
                    {
                        settled.Add($"upheld by the claim verifier on {string.Join(", ", upheld.Select(q => $"Q{q}"))} (the other member counted at most {ceiling})");
                    }
                    if (overturned.Count > 0)
                    {
                        settled.Add($"overturned by the claim verifier on {string.Join(", ", overturned.Select(q => $"Q{q}"))} (the flagging member counted at its pre-cap score)");
                    }
                    string averaged = unresolved.Count > 0
                        ? $"{unresolved.Count} split(s) stay averaged ({string.Join(", ", unresolved.Select(q => $"Q{q}"))})"
                        : "0 split(s) stay averaged";
                    resolutionSensitivityClause = $"Critical-error resolution sensitivity (scoring method 13 rule):** {resolvedIndex.Value} / 100 — Intelligence Index recomputed with each one-member critical-error split settled by the claim verifier's verdict on the flagging member's quote: {string.Join("; ", settled)}; {averaged}; advisory, changes no score.";
                    sb.AppendLine($"- **{resolutionSensitivityClause}");
                }
            }
        }

        if (!isPanelRun && splitAnswers.Count > 0)
        {
            var sensitivityScorableItems = scoredAnswers
                .Select(a =>
                {
                    double? score = IndexQualityOf(a);
                    if (splitAnswers.Any(ca => ca.OrderIndex == a.OrderIndex) && a.SecondOpinionQualityScore.HasValue)
                    {
                        score = a.SecondOpinionQualityScore.Value;
                    }
                    return (score, (int?)(a.AssessedDifficulty ?? BenchmarkRunFinalizer.FallbackDifficulty(a.Difficulty)));
                })
                .ToList();
            sensitivityIndex = BenchmarkScoring.QualityIndex(sensitivityScorableItems);
            if (sensitivityIndex.HasValue)
            {
                sb.AppendLine($"- **Contested-Verdict Sensitivity:** {sensitivityIndex.Value} / 100 — Intelligence Index recomputed with each split resolved at the second reader's score (raises and lowers both).");
            }
        }

        // The same substitution as the contested-verdict figure above, with the primary assessor's
        // evidence-informed re-grade in place of the second reader's score. Omitted at K = 0. One
        // predicate decides both which re-grades are counted and which are substituted. From harness
        // 31 a re-grade counts only when it passed validation, over the Intelligence Index's own item
        // set; an excluded one keeps its primary score in the figure. An earlier run keeps the
        // calculation it always had, over the graded answers, labelled legacy and unvalidated.
        bool validatedRegrades = BenchmarkService.IsValidatedRegradeHarness(run.HarnessVersion);
        var evidenceInformedPopulation = validatedRegrades ? indexAnswers : scoredAnswers;
        var regradedAnswers = evidenceInformedPopulation
            .Where(a => a.EvidenceInformedQualityScore.HasValue)
            .OrderBy(a => a.OrderIndex)
            .ToList();
        var evidenceInformedAnswers = regradedAnswers
            .Where(a => BenchmarkService.IsEligibleEvidenceInformedRegrade(run, a))
            .ToList();
        int excludedRegradeCount = regradedAnswers.Count - evidenceInformedAnswers.Count;
        int? evidenceInformedIndex = null;
        string? evidenceInformedClause = null;
        if (!isPanelRun && regradedAnswers.Count > 0)
        {
            var eligibleIds = evidenceInformedAnswers.Select(a => a.OrderIndex).ToHashSet();
            evidenceInformedIndex = BenchmarkScoring.QualityIndex(evidenceInformedPopulation
                .Select(a => (eligibleIds.Contains(a.OrderIndex) ? (double?)a.EvidenceInformedQualityScore : IndexQualityOf(a),
                              (int?)(a.AssessedDifficulty ?? BenchmarkRunFinalizer.FallbackDifficulty(a.Difficulty))))
                .ToList());
            if (evidenceInformedIndex.HasValue)
            {
                evidenceInformedClause = validatedRegrades
                    ? $"Evidence-informed Sensitivity (validated re-grades only):** {evidenceInformedIndex.Value} / 100 — Intelligence Index recomputed with the evidence-informed score on the {evidenceInformedAnswers.Count} answer(s) whose re-grade passed validation; {excludedRegradeCount} re-grade(s) excluded (rejected, or without validation provenance) keep their primary score; advisory, changes no score."
                    : $"Evidence-informed Sensitivity (legacy, unvalidated):** {evidenceInformedIndex.Value} / 100 — Intelligence Index recomputed with the evidence-informed score on the {evidenceInformedAnswers.Count} answer(s) re-graded with the verifier's findings in hand; advisory, changes no score.";
                sb.AppendLine($"- **{evidenceInformedClause}");
            }
        }

        // The run's own scoring profile snapshot, not today's profile: a sensitivity figure
        // beside a run's index has to be computed under the constants that produced it. The
        // critical-error cap comes along unchanged, because the recomputation runs through the
        // same Quality call that produced the stored score. An answer missing any of the four
        // levels keeps its stored score: there is nothing to raise it from. The item set is
        // indexAnswers — the Intelligence Index's own, unanswered questions at 0 included — so
        // the figure is comparable with the index it sits beside rather than with a narrower
        // set that would read higher for that reason alone. Shared by the Accuracy and
        // Readability sensitivity figures below, which differ only in which levels the caller
        // lifts, so the aggregation exists once.
        int? ClearedSensitivityIndex(HashSet<int> targetOrderIndexes, Func<BenchmarkRunAnswer, (int Accuracy, int Completeness, int Conciseness, int Readability)> liftedLevels)
        {
            var scorableItems = indexAnswers
                .Select(a =>
                {
                    double? score = IndexQualityOf(a);
                    if (targetOrderIndexes.Contains(a.OrderIndex) &&
                        a.AccuracyLevel.HasValue && a.CompletenessLevel.HasValue &&
                        a.ConcisenessLevel.HasValue && a.ReadabilityLevel.HasValue)
                    {
                        var (accuracy, completeness, conciseness, readability) = liftedLevels(a);
                        score = BenchmarkScoring.Quality(
                            accuracy,
                            completeness,
                            conciseness,
                            readability,
                            a.CriticalError,
                            scoringConstants,
                            notAttempted: resolvesCriticalErrors && a.NotAttempted == true).Score;
                    }
                    return (score, (int?)(a.AssessedDifficulty ?? BenchmarkRunFinalizer.FallbackDifficulty(a.Difficulty)));
                })
                .ToList();
            return BenchmarkScoring.QualityIndex(scorableItems);
        }

        if (!isPanelRun && verificationClearedAnswers.Count > 0)
        {
            var clearedOrderIndexes = verificationClearedAnswers.Select(a => a.OrderIndex).ToHashSet();
            verificationClearedIndex = ClearedSensitivityIndex(clearedOrderIndexes, a =>
                (Math.Min(MaxAssessmentLevel, a.AccuracyLevel!.Value + 1), a.CompletenessLevel!.Value, a.ConcisenessLevel!.Value, a.ReadabilityLevel!.Value));
            if (verificationClearedIndex.HasValue)
            {
                sb.AppendLine($"- **Verification-cleared Accuracy Sensitivity:** {verificationClearedIndex.Value} / 100 — Intelligence Index recomputed with Accuracy one level higher on the {verificationClearedAnswers.Count} answer(s) listed under Assessor Findings; advisory, changes no score.");
            }
        }

        if (!isPanelRun && formClearedAnswers.Count > 0)
        {
            // The Readability counterpart of the block above: recomputed with Readability one
            // level higher, on the answers whose only Readability basis was a rubric FORM
            // suggestion the answer did not adopt (formClearedAnswers). Omitted at zero for the
            // same reason as the Assessor Findings rubric-format-suggestions count further down.
            var formClearedOrderIndexes = formClearedAnswers.Select(a => a.OrderIndex).ToHashSet();
            formClearedIndex = ClearedSensitivityIndex(formClearedOrderIndexes, a =>
                (a.AccuracyLevel!.Value, a.CompletenessLevel!.Value, a.ConcisenessLevel!.Value, Math.Min(MaxAssessmentLevel, a.ReadabilityLevel!.Value + 1)));
            if (formClearedIndex.HasValue)
            {
                sb.AppendLine($"- **FORM-cleared Readability Sensitivity:** {formClearedIndex.Value} / 100 — Intelligence Index recomputed with Readability one level higher on the {formClearedAnswers.Count} answer(s) whose only Readability basis was a rubric FORM suggestion; advisory, changes no score.");
            }
        }

        // One blank line before the speed heading whether or not any qualifier printed, so a
        // sensitivity line appearing or not changes no other line of the report.
        string blankLine = Environment.NewLine + Environment.NewLine;
        if (sb.Length < blankLine.Length || sb.ToString(sb.Length - blankLine.Length, blankLine.Length) != blankLine)
        {
            sb.AppendLine();
        }

        // Shared by the saturation notice below and the Model Time Percentiles line further
        // down; both must report the same median. ModelTimeMs falls back to DurationMs when a
        // run predates the ToolTimeMs column, so this is available on the whole archive.
        var okAnswers = answers.Where(a => a.Status == BenchmarkAnswerStatus.Ok).ToList();
        var modelTimesSorted = okAnswers.Select(a => a.ModelTimeMs).OrderBy(d => d).ToList();
        long? medianModelTimeMs = modelTimesSorted.Count > 0 ? MedianMs(modelTimesSorted) : (long?)null;
        // Over the same Ok answers as the median, as Model Comparison's ModelTimeMeanMs is.
        double? meanModelTimeMs = modelTimesSorted.Count > 0 ? modelTimesSorted.Average(t => (double)t) : null;

        // A run whose Speed Index sits at the ceiling on most of its answers carries no
        // discriminating information: every answer at 100 looks identical to the index whether
        // it finished at the target or well inside it. Median model time still separates them.
        int speedScoredCount = scoredAnswers.Count;
        int speedCeilingCount = scoredAnswers.Count(a => a.SpeedScore.HasValue && a.SpeedScore.Value >= 100);
        bool speedSaturated = speedScoredCount > 0 && speedCeilingCount * 2 >= speedScoredCount;

        // H9. Where the index cannot discriminate, the figure that can leads and the index follows as
        // advisory. Presentation only: no score, no scoring profile and no method version moves, because
        // a metric that has run out of resolution is a reporting problem and re-tuning the target would
        // end the comparable series for every quality dimension as well.
        bool speedAdvisory = speedSaturated || profileMisfit;

        // The speed headline and its qualifier, printed here and repeated by At a Glance.
        bool medianLeadsSpeed = speedAdvisory && medianModelTimeMs.HasValue;
        string speedIndexText = IndexHeadline(run.SpeedIndex, " / 100", terminalFailureCount, run.TotalQuestionCount, "Not Scored");
        string speedHeadline = medianLeadsSpeed
            ? $"Median Model Time: {Inv(medianModelTimeMs!.Value, "N0")} ms"
            : $"Speed Index: {speedIndexText}";
        string? speedQualifier = medianLeadsSpeed
            ? $"Speed Index {speedIndexText} — advisory for this run{(run.SpeedMeasurementDegraded ? ", and measured under concurrency" : string.Empty)}"
            : (run.SpeedMeasurementDegraded ? "Advisory — measured under concurrency" : null);
        if (medianLeadsSpeed)
        {
            sb.AppendLine($"### **{speedHeadline}**");
            sb.AppendLine($"*{speedQualifier}.*");
        }
        else
        {
            sb.AppendLine($"### **{speedHeadline}**" + (speedQualifier != null ? $" *({speedQualifier})*" : string.Empty));
        }
        if (speedSaturated)
        {
            string medianClause = medianModelTimeMs.HasValue
                ? $"Compare median model time ({Inv(medianModelTimeMs.Value, "N0")} ms) instead."
                : "Compare median model time instead.";
            sb.AppendLine($"*Saturated — {speedCeilingCount} of {speedScoredCount} answers finished inside their difficulty-scaled target, so this index cannot discriminate at this speed. {medianClause}*");
        }

        if (unansweredAnswers.Count > 0)
        {
            string unansweredNumbers = string.Join(", ", unansweredAnswers.Select(a => a.OrderIndex));
            sb.AppendLine($"- **Unanswered Questions:** {unansweredAnswers.Count} of {run.TotalQuestionCount} (question(s) {unansweredNumbers}) — *the model ended its turn without producing an answer. Each scores 0 under scoring method 10, and the run is reported as CompletedWithErrors.*");
        }
        sb.AppendLine();
        if (isPanelRun)
        {
            sb.AppendLine($"- **Holistic Score, Panel Member A:** {(run.FinalScore.HasValue ? $"{run.FinalScore.Value} / 100" : "N/A")}");
            sb.AppendLine($"- **Holistic Score, Panel Member B:** {(run.CoAssessorFinalScore.HasValue ? $"{run.CoAssessorFinalScore.Value} / 100" : "N/A")}");
        }
        else
        {
            sb.AppendLine($"- **Holistic Assessor Score:** {(run.FinalScore.HasValue ? $"{run.FinalScore.Value} / 100" : "N/A")}");
        }
        sb.AppendLine($"- **Total Model Answer Duration:** {FormatDuration(run.TotalAnswerDurationMs)} ({Inv(run.TotalAnswerDurationMs, "N0")} ms)");

        var durations = okAnswers.Select(a => a.DurationMs).OrderBy(d => d).ToList();
        if (durations.Count > 0)
        {
            long p50 = MedianMs(durations);
            long p90 = Percentile(durations, 0.90);
            long maxD = durations[^1];
            sb.AppendLine($"- **Turn Duration Percentiles:** Median (P50) = {Inv(p50, "N0")} ms, P90 = {Inv(p90, "N0")} ms, Max = {Inv(maxD, "N0")} ms");
        }

        // Split the turn into model time and harness tool I/O, so a low speed score can be
        // attributed to the model rather than to the harness. Speed is scored on model time.
        if (okAnswers.Any(a => a.ToolTimeMs.HasValue))
        {
            long toolOverhead = okAnswers.Sum(a => a.ToolTimeMs ?? 0L);
            long modelTotal = okAnswers.Sum(a => a.ModelTimeMs);
            sb.AppendLine($"- **Total Tool Overhead:** {FormatDuration(toolOverhead)} ({Inv(toolOverhead, "N0")} ms)");
            sb.AppendLine($"- **Total Model-Attributable Time:** {FormatDuration(modelTotal)} ({Inv(modelTotal, "N0")} ms)");

            sb.AppendLine($"- **Model Time Percentiles:** Median (P50) = {Inv(medianModelTimeMs!.Value, "N0")} ms, P90 = {Inv(Percentile(modelTimesSorted, 0.90), "N0")} ms, Max = {Inv(modelTimesSorted[^1], "N0")} ms");
            sb.AppendLine($"- **Model Time Mean:** {Inv(meanModelTimeMs!.Value, "N0")} ms per answered question");
        }
        else
        {
            sb.AppendLine(PredatesHarnessVersion(run, 3)
                ? "- **Tool Overhead:** Not recorded (run predates harness version 3); speed was scored on total turn duration."
                : "- **Tool Overhead:** Not recorded — no answered question carries tool timing.");
        }

        // Time to first token: the only latency figure a thinking=max configuration does not
        // dominate, and therefore the one that describes how the model would feel in the chat.
        var ttfts = okAnswers.Where(a => a.TimeToFirstTokenMs.HasValue)
            .Select(a => a.TimeToFirstTokenMs!.Value)
            .OrderBy(d => d)
            .ToList();
        if (ttfts.Count > 0)
        {
            sb.AppendLine($"- **Time to First Token:** Median (P50) = {Inv(MedianMs(ttfts), "N0")} ms, P90 = {Inv(Percentile(ttfts, 0.90), "N0")} ms, Max = {Inv(ttfts[^1], "N0")} ms");
        }

        sb.AppendLine($"- **Total Input Tokens:** {Inv(run.TotalInputTokens, "N0")}");
        sb.AppendLine($"- **Total Output Tokens:** {Inv(run.TotalOutputTokens, "N0")}");
        // Anthropic reports no separate thinking count: its zero is "not reported", not "no thinking".
        bool reasoningUnreported = IsAnthropicCandidate(run);
        if (reasoningUnreported)
        {
            sb.AppendLine("- **Of Which Reasoning Tokens:** n/a *(not reported separately by this provider; thinking is counted in output tokens)*");
        }
        else if (answers.Any(a => a.ReasoningTokens.HasValue))
        {
            long reasoningTotal = answers.Sum(a => (long)(a.ReasoningTokens ?? 0));
            int unrecorded = answers.Count(a => !a.ReasoningTokens.HasValue);
            string unrecordedNote = unrecorded > 0 ? $" (not recorded on {unrecorded} answer(s))" : string.Empty;
            sb.AppendLine($"- **Of Which Reasoning Tokens:** {Inv(reasoningTotal, "N0")}{unrecordedNote}");
        }
        sb.AppendLine($"- **Total Cache Read Tokens:** {Inv(run.TotalCacheReadTokens, "N0")}");
        // A real zero and "this provider does not report the counter" are different facts, and
        // printing 0 beside four million cache reads reads as a cache that never warmed. OpenAI
        // reports cache reads only; the 2026-09-03 run showed exactly that shape.
        bool cacheCreationUnreported =
            run.TotalCacheCreationTokens == 0 &&
            run.TotalCacheReadTokens > 0 &&
            string.Equals(run.TestedModelSnapshot.Provider, "OpenAI", StringComparison.OrdinalIgnoreCase);
        sb.AppendLine(cacheCreationUnreported
            ? "- **Total Cache Creation Tokens:** n/a *(not reported by this provider)*"
            : $"- **Total Cache Creation Tokens:** {Inv(run.TotalCacheCreationTokens, "N0")}");

        // H1. ModelCallCount has been persisted per answer since harness 11, but nothing an analyst reads
        // carried it, so input-token growth could not be attributed: many calls and large context produce
        // the same total and demand opposite responses. Input tokens per model call is the figure that
        // separates them, and run 13 could not answer it.
        var modelCallCounts = answers
            .Where(a => a.ModelCallCount.HasValue && a.ModelCallCount.Value > 0 && BenchmarkRunFinalizer.CountsTowardQualityIndex(a))
            .ToList();
        if (modelCallCounts.Count > 0)
        {
            int totalModelCalls = modelCallCounts.Sum(a => a.ModelCallCount!.Value);
            double meanModelCalls = (double)totalModelCalls / modelCallCounts.Count;
            var maxAnswer = modelCallCounts.OrderByDescending(a => a.ModelCallCount!.Value).First();
            sb.AppendLine(
                $"- **Model Calls:** {Inv(totalModelCalls, "N0")} across {modelCallCounts.Count} answered question(s) " +
                $"(mean {Inv(meanModelCalls, "F1")}, max {Inv(maxAnswer.ModelCallCount!.Value, "N0")} on Q{maxAnswer.OrderIndex})");

            if (totalModelCalls > 0 && run.TotalInputTokens > 0)
            {
                sb.AppendLine(
                    $"- **Input Tokens per Model Call:** {Inv(run.TotalInputTokens / totalModelCalls, "N0")}");
            }
        }

        // T18. Input-token cost is driven by model-call count (rounds re-send every prior tool
        // result in that answer), not tool-call count, and it concentrates: on run 14 three answers
        // were half the run's input tokens. InputTokenShare is computed here, never stored — see
        // the "Note on InputTokenShare" in the implementation plan.
        var byInputTokens = answers
            .Where(a => a.InputTokens.HasValue && a.InputTokens.Value > 0)
            .OrderByDescending(a => a.InputTokens!.Value)
            .ToList();
        if (byInputTokens.Count > 0 && run.TotalInputTokens > 0)
        {
            var topAnswers = byInputTokens.Take(3).ToList();
            long topSum = topAnswers.Sum(a => (long)a.InputTokens!.Value);
            double topShare = (double)topSum / run.TotalInputTokens * 100.0;
            string topList = string.Join(", ", topAnswers.Select(a =>
                $"Q{a.OrderIndex} ({Inv(a.InputTokens!.Value, "N0")}, {Inv((double)a.InputTokens!.Value / run.TotalInputTokens * 100.0, "F1")}%)"));
            sb.AppendLine(
                $"- **Highest Input-Token Answers:** {topList} — together {Inv(topShare, "F1")}% of the run's input tokens.");
        }
        sb.AppendLine();

        // At a Glance's Cost cell, set from the Estimated Cost figures below.
        string glanceCostText = "*not recorded*";

        // Harness cost. The token totals above are the candidate's alone; grading an 18-question
        // suite question by question is not a rounding error, and until this block existed the
        // run's actual consumption was recorded nowhere.
        // The candidate's own spend is knowable whether or not a grading stage ran, and a run that stopped
        // early often has only the former. Gating the section on the grading roles alone hid the cost of
        // exactly the runs whose cost is least obvious elsewhere.
        if (run.TotalInputTokens > 0 || run.TotalOutputTokens > 0 ||
            run.TotalAssessmentInputTokens > 0 || run.TotalAssessmentOutputTokens > 0 || run.TotalAssessmentDurationMs > 0 ||
            run.TotalSecondOpinionInputTokens > 0 || run.TotalSecondOpinionOutputTokens > 0 || run.TotalSecondOpinionDurationMs > 0 ||
            run.TotalClaimVerificationInputTokens > 0 || run.TotalClaimVerificationOutputTokens > 0 || run.TotalClaimVerificationDurationMs > 0 ||
            run.TotalSynthesisInputTokens > 0 || run.TotalSynthesisOutputTokens > 0 || run.TotalSynthesisDurationMs > 0 ||
            run.TotalCoAssessmentInputTokens > 0 || run.TotalCoAssessmentOutputTokens > 0 || run.TotalCoAssessmentDurationMs > 0 ||
            run.TotalCoSynthesisInputTokens > 0 || run.TotalCoSynthesisOutputTokens > 0 || run.TotalCoSynthesisDurationMs > 0)
        {
            sb.AppendLine("### Harness Cost");

            if (PredatesHarnessVersion(run, 15))
            {
                sb.AppendLine("*Recorded before per-role cost tracking: the second reader's spend is inside the assessor line, and the final synthesis is not counted at all.*");
            }

            sb.AppendLine($"- **Candidate Tokens:** {Inv(run.TotalInputTokens, "N0")} in / {Inv(run.TotalOutputTokens, "N0")} out");
            sb.AppendLine($"- **Assessor Tokens:** {HarnessCostTokenLine(run.TotalAssessmentInputTokens, run.TotalAssessmentOutputTokens, run.TotalAssessmentCacheReadTokens, run.TotalAssessmentCacheCreationTokens)}");
            if (isPanelRun)
            {
                sb.AppendLine($"- **Co-Assessor Tokens:** {HarnessCostTokenLine(run.TotalCoAssessmentInputTokens, run.TotalCoAssessmentOutputTokens, run.TotalCoAssessmentCacheReadTokens, run.TotalCoAssessmentCacheCreationTokens)}");
            }
            if (run.TotalSecondOpinionInputTokens > 0 || run.TotalSecondOpinionOutputTokens > 0 || run.SecondOpinionAssessorModelConfigurationId.HasValue)
            {
                sb.AppendLine($"- **{readerTitle} Tokens:** {HarnessCostTokenLine(run.TotalSecondOpinionInputTokens, run.TotalSecondOpinionOutputTokens, run.TotalSecondOpinionCacheReadTokens, run.TotalSecondOpinionCacheCreationTokens)}");
            }
            if (run.TotalClaimVerificationInputTokens > 0 || run.TotalClaimVerificationOutputTokens > 0 || run.ClaimVerifierModelConfigurationId.HasValue)
            {
                bool allVerificationAttemptsFailed =
                    answers.Any(a => !string.IsNullOrWhiteSpace(a.ClaimVerificationError) && !IsClaimVerificationBudgetNotChecked(a.ClaimVerificationError)) &&
                    !answers.Any(a => string.IsNullOrWhiteSpace(a.ClaimVerificationError) && (a.ClaimsSupportedCount.HasValue || a.ClaimsRefutedCount.HasValue || a.ClaimsIndeterminateCount.HasValue));
                string failedCaveat = allVerificationAttemptsFailed ? " — *every attempt failed; see Issues*" : string.Empty;
                sb.AppendLine($"- **Claim Verifier Tokens:** {HarnessCostTokenLine(run.TotalClaimVerificationInputTokens, run.TotalClaimVerificationOutputTokens, run.TotalClaimVerificationCacheReadTokens, run.TotalClaimVerificationCacheCreationTokens)}{failedCaveat}");
            }
            if (run.TotalSynthesisInputTokens > 0 || run.TotalSynthesisOutputTokens > 0 || run.TotalSynthesisDurationMs > 0)
            {
                sb.AppendLine($"- **Synthesis Tokens:** {HarnessCostTokenLine(run.TotalSynthesisInputTokens, run.TotalSynthesisOutputTokens, run.TotalSynthesisCacheReadTokens, run.TotalSynthesisCacheCreationTokens)}");
            }
            if (isPanelRun)
            {
                sb.AppendLine($"- **Co-Synthesis Tokens:** {HarnessCostTokenLine(run.TotalCoSynthesisInputTokens, run.TotalCoSynthesisOutputTokens, run.TotalCoSynthesisCacheReadTokens, run.TotalCoSynthesisCacheCreationTokens)}");
            }
            long totalInput = run.TotalInputTokens + run.TotalAssessmentInputTokens + run.TotalSecondOpinionInputTokens +
                run.TotalClaimVerificationInputTokens + run.TotalSynthesisInputTokens +
                run.TotalCoAssessmentInputTokens + run.TotalCoSynthesisInputTokens;
            long totalOutput = run.TotalOutputTokens + run.TotalAssessmentOutputTokens + run.TotalSecondOpinionOutputTokens +
                run.TotalClaimVerificationOutputTokens + run.TotalSynthesisOutputTokens +
                run.TotalCoAssessmentOutputTokens + run.TotalCoSynthesisOutputTokens;
            sb.AppendLine($"- **Total Tokens:** {Inv(totalInput, "N0")} in / {Inv(totalOutput, "N0")} out");
            sb.AppendLine($"- **Assessment Time:** {FormatDuration(run.TotalAssessmentDurationMs)} ({Inv(run.TotalAssessmentDurationMs, "N0")} ms)");
            if (run.TotalCoAssessmentDurationMs > 0)
            {
                sb.AppendLine($"- **Co-Assessment Time:** {FormatDuration(run.TotalCoAssessmentDurationMs)} ({Inv(run.TotalCoAssessmentDurationMs, "N0")} ms)");
            }
            if (run.TotalSecondOpinionDurationMs > 0)
            {
                sb.AppendLine($"- **{readerTitle} Time:** {FormatDuration(run.TotalSecondOpinionDurationMs)} ({Inv(run.TotalSecondOpinionDurationMs, "N0")} ms)");
            }
            if (run.TotalClaimVerificationDurationMs > 0)
            {
                sb.AppendLine($"- **Claim Verification Time:** {FormatDuration(run.TotalClaimVerificationDurationMs)} ({Inv(run.TotalClaimVerificationDurationMs, "N0")} ms)");
            }
            if (run.TotalSynthesisDurationMs > 0)
            {
                sb.AppendLine($"- **Synthesis Time:** {FormatDuration(run.TotalSynthesisDurationMs)} ({Inv(run.TotalSynthesisDurationMs, "N0")} ms)");
            }
            if (run.TotalCoSynthesisDurationMs > 0)
            {
                sb.AppendLine($"- **Co-Synthesis Time:** {FormatDuration(run.TotalCoSynthesisDurationMs)} ({Inv(run.TotalCoSynthesisDurationMs, "N0")} ms)");
            }

            // Estimated Cost block (H7). Per-role totals come from ModelPricingService.ComputeRunRoleCosts,
            // the same routine the cost-panel UI calls, so the report and the UI cannot disagree about what
            // the Grading subtotal includes. The itemised buckets come from
            // ModelPricingService.ComputeRunRoleCostBreakdowns, which the totals are read from as well, so
            // the parts printed beside a role total are that total's own parts. No rate card is multiplied
            // by a token count here: doing so would drop the long-context split and the service-tier
            // multiplier, and would misread a stored Total*InputTokens column as an uncached figure when it
            // is a prompt total that already contains the cache reads beside it.
            bool hasAssessor = run.TotalAssessmentInputTokens > 0 || run.TotalAssessmentOutputTokens > 0;
            bool hasSecondOpinion = run.TotalSecondOpinionInputTokens > 0 || run.TotalSecondOpinionOutputTokens > 0;
            bool hasVerifier = run.TotalClaimVerificationInputTokens > 0 || run.TotalClaimVerificationOutputTokens > 0;
            bool hasSynthesis = run.TotalSynthesisInputTokens > 0 || run.TotalSynthesisOutputTokens > 0;
            bool hasCoAssessor = run.TotalCoAssessmentInputTokens > 0 || run.TotalCoAssessmentOutputTokens > 0;
            bool hasCoSynthesis = run.TotalCoSynthesisInputTokens > 0 || run.TotalCoSynthesisOutputTokens > 0;

            // Printed under the Claim Verification Yield line when the verifier is priced, otherwise
            // after the cost block.
            string? verifierSpendLine = VerifierSpendByAnswerLine(answers);

            var candidatePricing = runPricing?.Candidate;
            // Synthesis is priced on the assessor's own card, so its pricing requirement folds into
            // the assessor's rather than needing a card of its own.
            var assessorPricing = (hasAssessor || hasSynthesis) ? runPricing?.Assessor : null;
            var secondOpinionPricing = hasSecondOpinion ? runPricing?.SecondOpinion : null;
            var verifierPricing = hasVerifier ? runPricing?.ClaimVerifier : null;
            // Member B's synthesis is priced on member B's card, as member A's is on the assessor's.
            var coAssessorPricing = (hasCoAssessor || hasCoSynthesis) ? runPricing?.CoAssessor : null;

            bool canEstimateCost = runPricing != null &&
                candidatePricing != null &&
                (!(hasAssessor || hasSynthesis) || assessorPricing != null) &&
                (!hasSecondOpinion || secondOpinionPricing != null) &&
                (!hasVerifier || verifierPricing != null) &&
                (!(hasCoAssessor || hasCoSynthesis) || coAssessorPricing != null);

            if (canEstimateCost)
            {
                // canEstimateCost has already established the candidate's card, which is the one role that
                // must price for anything here to be printable.
                ModelPricing candidateCard = candidatePricing!;

                // The candidate's long-context buckets were partitioned per model call at answer time and
                // persisted, and the served tier is a property of the turn. Both are resolved once and
                // handed to the two costing calls, so the totals and their parts see the same inputs.
                string? servedServiceTier = BenchmarkRunFinalizer.ResolveServedServiceTier(run.Answers);
                var roleCosts = ModelPricingService.ComputeRunRoleCosts(run, runPricing!, servedServiceTier);
                var roleParts = ModelPricingService.ComputeRunRoleCostBreakdowns(run, runPricing!, servedServiceTier);

                decimal candidateTotalCost = roleCosts.Candidate;
                decimal assessorTotalCost = roleCosts.Assessor;
                decimal secondOpinionTotalCost = roleCosts.SecondOpinion;
                decimal verifierTotalCost = roleCosts.ClaimVerifier;
                decimal synthesisTotalCost = roleCosts.Synthesis;
                decimal coAssessorTotalCost = roleCosts.CoAssessor;
                decimal coSynthesisTotalCost = roleCosts.CoSynthesis;

                decimal gradingTotalCost = roleCosts.Grading;
                decimal totalCost = roleCosts.Total;

                // "uncached in" and "cached in" are separated only where the card publishes a distinct
                // cached rate; otherwise cache reads bill at the input rate and naming them separately
                // would imply a saving the run did not get. Either way the printed components are the four
                // buckets of that role's own costing, so they sum to the role total shown beside them.
                static string CostParts(ModelCostBreakdown parts, ModelPricing card)
                {
                    var pieces = new List<string>(4);
                    if (parts.CacheRead > 0m && card.CachedInputPerMillion.HasValue)
                    {
                        pieces.Add($"uncached in: ${Inv(parts.UncachedInput, "F2")}");
                        pieces.Add($"cached in: ${Inv(parts.CacheRead, "F2")}");
                    }
                    else
                    {
                        pieces.Add($"in: ${Inv(parts.UncachedInput + parts.CacheRead, "F2")}");
                    }
                    if (parts.CacheWrite > 0m)
                    {
                        pieces.Add($"cache write: ${Inv(parts.CacheWrite, "F2")}");
                    }
                    pieces.Add($"out: ${Inv(parts.Output, "F2")}");
                    return string.Join(", ", pieces);
                }

                if (!roleCosts.Incomplete)
                {
                    sb.AppendLine($"- **Estimated Cost:** ${Inv(totalCost, "F2")} total");
                }
                else
                {
                    sb.AppendLine("- **Estimated Cost:** not available as a single total — the participating roles do not price in comparable units; see the per-role figures below.");
                }

                // Per question asked: every answer row, whatever its status, as the run's token totals are.
                string candidatePerQuestion = answers.Count > 0
                    ? $"; candidate {PerUnitCost(candidateTotalCost / answers.Count)} per question over {answers.Count} asked"
                    : string.Empty;
                glanceCostText = (!roleCosts.Incomplete
                    ? $"${Inv(totalCost, "F2")} estimated total"
                    : "no single total (the roles do not price in comparable units)") + candidatePerQuestion;

                sb.AppendLine($"  - Candidate ({run.TestedModelSnapshot.ModelId}): ${Inv(candidateTotalCost, "F2")} ({CostParts(roleParts.Candidate, candidateCard)})");

                if (hasAssessor && assessorPricing != null)
                {
                    sb.AppendLine($"  - Assessor ({run.AssessorModelSnapshot.ModelId}): ${Inv(assessorTotalCost, "F2")} ({CostParts(roleParts.Assessor, assessorPricing)})");
                }

                if (hasCoAssessor && coAssessorPricing != null)
                {
                    sb.AppendLine($"  - Co-Assessor ({run.CoAssessorModelSnapshot?.ModelId}): ${Inv(coAssessorTotalCost, "F2")} ({CostParts(roleParts.CoAssessor, coAssessorPricing)})");
                }

                if (hasSecondOpinion && secondOpinionPricing != null)
                {
                    sb.AppendLine($"  - {readerTitle} ({run.SecondOpinionAssessorModelSnapshot?.ModelId}): ${Inv(secondOpinionTotalCost, "F2")} ({CostParts(roleParts.SecondOpinion, secondOpinionPricing)})");
                }

                if (hasVerifier && verifierPricing != null)
                {
                    sb.AppendLine($"  - Claim Verifier ({run.ClaimVerifierModelSnapshot?.ModelId}): ${Inv(verifierTotalCost, "F2")} ({CostParts(roleParts.ClaimVerifier, verifierPricing)})");
                }

                if (hasSynthesis && assessorPricing != null)
                {
                    sb.AppendLine($"  - Synthesis ({run.AssessorModelSnapshot.ModelId}): ${Inv(synthesisTotalCost, "F2")} ({CostParts(roleParts.Synthesis, assessorPricing)})");
                }

                if (hasCoSynthesis && coAssessorPricing != null)
                {
                    sb.AppendLine($"  - Co-Synthesis ({run.CoAssessorModelSnapshot?.ModelId}): ${Inv(coSynthesisTotalCost, "F2")} ({CostParts(roleParts.CoSynthesis, coAssessorPricing)})");
                }

                // Printed after the role lines, so they stay contiguous, rather than between the
                // Claim Verifier and Synthesis lines.
                if (hasVerifier && verifierPricing != null)
                {
                    // H4. The verifier's own yield — what its dollars actually bought — was
                    // previously unreported: run 13 to run 14 alone it grew from 36% to 67% of run
                    // cost with no figure an operator could steer by. "Checked" excludes answers the
                    // token budget stopped before a call was made (BenchmarkClaimVerificationNotCheckedReason),
                    // so this line never counts a claim the verifier never saw.
                    int claimsChecked = run.ClaimsSupportedCount + run.ClaimsRefutedCount + run.ClaimsIndeterminateCount;

                    // The accused sentences the verifier also checked, read by role from each answer's
                    // ClaimVerificationJson: the run's claim columns count the answers' own claims only.
                    var accusedChecked = answers.SelectMany(a => AccusedSentencesOf(a)).ToList();
                    var assessorChecked = answers.SelectMany(a => AssessorStatementsOf(a)).ToList();
                    if (claimsChecked > 0 && accusedChecked.Count == 0 && assessorChecked.Count == 0)
                    {
                        decimal costPerClaim = verifierTotalCost / claimsChecked;
                        decimal verifierCostShare = totalCost > 0 ? verifierTotalCost / totalCost * 100m : 0m;
                        sb.AppendLine(
                            $"- **Claim Verification Yield:** {Inv(claimsChecked, "N0")} claim(s) checked — " +
                            $"{Inv(run.ClaimsSupportedCount, "N0")} supported, {Inv(run.ClaimsRefutedCount, "N0")} refuted, " +
                            $"{Inv(run.ClaimsIndeterminateCount, "N0")} indeterminate. " +
                            $"${Inv(verifierTotalCost, "F2")} ({PerUnitCost(costPerClaim)}/claim), {Inv(verifierCostShare, "F0")}% of run cost.");
                    }
                    else if (accusedChecked.Count > 0 || assessorChecked.Count > 0)
                    {
                        int itemsChecked = claimsChecked + accusedChecked.Count + assessorChecked.Count;
                        decimal costPerItem = verifierTotalCost / itemsChecked;
                        decimal verifierCostShare = totalCost > 0 ? verifierTotalCost / totalCost * 100m : 0m;
                        var heads = new List<string> { $"{Inv(claimsChecked, "N0")} unverified claim(s)" };
                        var parts = new List<string>
                        {
                            $"claims: {Inv(run.ClaimsSupportedCount, "N0")} supported, {Inv(run.ClaimsRefutedCount, "N0")} refuted, {Inv(run.ClaimsIndeterminateCount, "N0")} indeterminate"
                        };
                        void AddPopulation(string head, string label, List<BenchmarkClaimVerification> items)
                        {
                            if (items.Count == 0) return;
                            var counts = VerdictCounts(items);
                            heads.Add($"{Inv(items.Count, "N0")} {head}");
                            parts.Add($"{label}: {Inv(counts.Supported, "N0")} supported, {Inv(counts.Refuted, "N0")} refuted, {Inv(counts.Indeterminate, "N0")} indeterminate");
                        }
                        AddPopulation("accused sentence(s)", "accused sentences", accusedChecked);
                        AddPopulation("assessor statement(s)", "assessor statements", assessorChecked);
                        string over = heads.Count == 2 ? "both" : "all three";
                        sb.AppendLine(
                            $"- **Claim Verification Yield:** {string.Join(" + ", heads)} checked — {string.Join("; ", parts)}. " +
                            $"${Inv(verifierTotalCost, "F2")} ({PerUnitCost(costPerItem)}/item over {over}), {Inv(verifierCostShare, "F0")}% of run cost.");
                    }

                    if (verifierSpendLine != null)
                    {
                        sb.AppendLine(verifierSpendLine);
                        verifierSpendLine = null;
                    }
                }

                if (hasAssessor || hasSecondOpinion || hasVerifier || hasSynthesis || hasCoAssessor || hasCoSynthesis)
                {
                    decimal gradingShare = totalCost > 0 ? gradingTotalCost / totalCost * 100m : 0m;
                    sb.AppendLine($"  - **Grading subtotal:** ${Inv(gradingTotalCost, "F2")} ({Inv(gradingShare, "F0")}% of total)");
                }

                // Both lines are printed only when they apply. An absent tier is omitted entirely rather
                // than shown as 1.0x, and a run with no long-context tokens prints no surcharge line — a
                // reader must not have to tell "no surcharge" from "surcharge of zero".
                if (run.TotalLongContextInputTokens > 0 && candidateCard.LongContext != null)
                {
                    int longContextAnswerCount = run.Answers.Count(a => (a.LongContextInputTokens ?? 0) > 0);
                    sb.AppendLine(
                        $"- **Long-context surcharge:** applied to {Inv(longContextAnswerCount, "N0")} answer(s) — " +
                        $"{Inv(run.TotalLongContextInputTokens, "N0")} input token(s) billed at the " +
                        $">{Inv(candidateCard.LongContext.ThresholdInputTokens, "N0")} rate " +
                        $"(${Inv(candidateCard.LongContext.InputPerMillion, "F2")}/M input vs " +
                        $"${Inv(candidateCard.InputPerMillion, "F2")}/M). " +
                        $"${Inv(roleParts.Candidate.LongContextPortion, "F2")} of the candidate's " +
                        $"${Inv(candidateTotalCost, "F2")} was billed at that card.");
                }

                decimal servedTierMultiplier = ModelPricingService.ResolveServiceTierMultiplier(
                    candidateCard, servedServiceTier, run.TestedModelSnapshot.ServiceTier);
                if (servedTierMultiplier != 1.0m && !string.IsNullOrEmpty(servedServiceTier))
                {
                    sb.AppendLine(
                        $"- **Service tier:** served {servedServiceTier} — prices scaled by {Inv(servedTierMultiplier, "0.##")}x.");
                }

                var provenanceParts = new List<string>();
                string FormatProv(string roleTitle, ModelPricing p)
                {
                    if (p.Source == ModelPricingSource.Custom)
                        return $"{roleTitle} custom";
                    return $"{roleTitle} catalog" + (!string.IsNullOrEmpty(p.AsOf) ? $" (as of {p.AsOf})" : "");
                }
                provenanceParts.Add(FormatProv("candidate", candidateCard));
                if ((hasAssessor || hasSynthesis) && assessorPricing != null)
                {
                    provenanceParts.Add(FormatProv("assessor", assessorPricing));
                }
                if ((hasCoAssessor || hasCoSynthesis) && coAssessorPricing != null)
                {
                    provenanceParts.Add(FormatProv("co-assessor", coAssessorPricing));
                }
                if (hasSecondOpinion && secondOpinionPricing != null)
                {
                    provenanceParts.Add(FormatProv(readerName, secondOpinionPricing));
                }
                if (hasVerifier && verifierPricing != null)
                {
                    provenanceParts.Add(FormatProv("verifier", verifierPricing));
                }

                string provenanceLine = $"- *Prices: {string.Join("; ", provenanceParts)}.*";
                if (runPricing != null && !runPricing.IsSnapshot)
                {
                    provenanceLine += " *(priced at report generation time; this run predates price snapshotting)*";
                }
                sb.AppendLine(provenanceLine);
            }
            else
            {
                var missingRoles = new List<string>();
                if (candidatePricing == null) missingRoles.Add("candidate");
                if ((hasAssessor || hasSynthesis) && assessorPricing == null) missingRoles.Add("assessor");
                if ((hasCoAssessor || hasCoSynthesis) && coAssessorPricing == null) missingRoles.Add("co-assessor");
                if (hasSecondOpinion && secondOpinionPricing == null) missingRoles.Add(readerName);
                if (hasVerifier && verifierPricing == null) missingRoles.Add("claim verifier");
                if (missingRoles.Count == 0) missingRoles.Add("participating models");

                sb.AppendLine($"- **Estimated Cost:** not available — no price is known for {string.Join(", ", missingRoles)}. Set a price in Admin → System AI Configs (Custom), or add `pricing` to the model's catalog entry.");
                glanceCostText = $"*not available* — no price is known for {string.Join(", ", missingRoles)}";
            }

            if (verifierSpendLine != null)
            {
                sb.AppendLine(verifierSpendLine);
            }


            // H6. Only a run with question-level concurrency actually pipelines an answer's grading
            // behind the next answer's candidate call. A sequential run has no such overlap to
            // disclaim, so it gets the measured figure instead: wall clock minus every recorded stage
            // duration, candidate included. A run whose synthesis is available folds it into that sum,
            // since a synthesis call sits on the same critical path as the other grading stages —
            // one measured run's residual dropped to 17 seconds once synthesis was counted with it.
            // A repaired run's stage durations include its re-run, which the preserved wall clock
            // does not, so no residual is meaningful there.
            if (run.RerunStartedAtUtc.HasValue)
            {
                sb.AppendLine($"*Stage durations include the re-run ({RerunSpan(run)}), which lies outside the original wall clock; overlap is not computed for a repaired run.*");
            }
            else if (run.MaxParallelQuestionsUsed > 1)
            {
                sb.AppendLine("*Assessment runs pipelined behind each answer, so assessment time overlaps the candidate's and the two do not sum to the wall time.*");
            }
            else
            {
                // In a panel run the two members grade each answer concurrently, so an answer's
                // assessment stage lasts as long as the slower member; the two syntheses run one
                // after the other, so both count.
                long assessmentStageMs = isPanelRun
                    ? answers.Sum(a => Math.Max(a.AssessmentDurationMs ?? 0L, a.CoAssessmentDurationMs ?? 0L))
                    : run.TotalAssessmentDurationMs;
                long summedStageDurations = run.TotalAnswerDurationMs + assessmentStageMs +
                    run.TotalSecondOpinionDurationMs + run.TotalClaimVerificationDurationMs + run.TotalSynthesisDurationMs +
                    (isPanelRun ? run.TotalCoSynthesisDurationMs : 0L);
                string stageList = isPanelRun
                    ? $"candidate, assessment (the slower member per answer), {readerName}, claim verification, synthesis, co-synthesis"
                    : $"candidate, assessment, {readerName}, claim verification, synthesis";
                long measuredOverlapMs = run.TotalDurationMs - summedStageDurations;
                if (measuredOverlapMs < 0)
                {
                    long excessMs = -measuredOverlapMs;
                    sb.AppendLine(
                        $"*Measured overlap: the summed stage durations ({stageList}) exceed the wall clock by " +
                        $"{FormatDuration(excessMs)} ({Inv(excessMs, "N0")} ms) — grading stages ran concurrently with candidate answering.*");
                }
                else
                {
                    sb.AppendLine(
                        $"*Measured overlap: wall clock minus the summed stage durations ({stageList}) leaves " +
                        $"{FormatDuration(measuredOverlapMs)} ({Inv(measuredOverlapMs, "N0")} ms) unaccounted for by sequential stage time.*");
                }
            }
            sb.AppendLine();
        }
        else
        {
            sb.AppendLine(PredatesHarnessVersion(run, 4)
                ? "*Assessor token and time accounting was not recorded for this run (it predates harness version 4).*"
                : "*Assessor and claim-verifier accounting is zero for this run: no grading stage recorded any usage.*");
            sb.AppendLine();
        }

        // Run Integrity block.
        //
        // Every answer falls into exactly one of Clean / TransportDefect / HarnessLimit, so the
        // three always sum to the question count. The previous block printed a "Degraded" total
        // that included tool-budget exhaustion beside a breakdown that omitted it, so the
        // figures did not add up (7 = "empty: 0, harness artifacts: 4, truncated: 0"), and it
        // conflated a transport defect with a configured cap working as designed.
        int totalQuestions = run.TotalQuestionCount;
        int emptyCount = answers.Count(a => a.Status == BenchmarkAnswerStatus.EmptyAnswer || ((BenchmarkAnswerFlags)a.AnswerFlags).HasFlag(BenchmarkAnswerFlags.Empty));
        int artifactCount = answers.Count(a => ((BenchmarkAnswerFlags)a.AnswerFlags).HasFlag(BenchmarkAnswerFlags.HarnessArtifacts));
        int truncatedCount = answers.Count(a => ((BenchmarkAnswerFlags)a.AnswerFlags).HasFlag(BenchmarkAnswerFlags.Truncated));
        int bleedCount = answers.Count(a => ((BenchmarkAnswerFlags)a.AnswerFlags).HasFlag(BenchmarkAnswerFlags.ReasoningBleed));
        int repeatCount = answers.Count(a => ((BenchmarkAnswerFlags)a.AnswerFlags).HasFlag(BenchmarkAnswerFlags.RepeatedFragments));
        int contestedCount = answers.Count(a => ((BenchmarkAnswerFlags)a.AnswerFlags).HasFlag(BenchmarkAnswerFlags.ContestedVerdict));
        int unevidencedCount = answers.Count(a => ((BenchmarkAnswerFlags)a.AnswerFlags).HasFlag(BenchmarkAnswerFlags.UnevidencedDeduction));
        int omissionCount = answers.Count(a => ((BenchmarkAnswerFlags)a.AnswerFlags).HasFlag(BenchmarkAnswerFlags.OmissionAsAccuracy));
        int refutedCount = answers.Count(a => ((BenchmarkAnswerFlags)a.AnswerFlags).HasFlag(BenchmarkAnswerFlags.RefutedClaim));
        int contestedCriticalErrorCount = answers.Count(a => ((BenchmarkAnswerFlags)a.AnswerFlags).HasFlag(BenchmarkAnswerFlags.ContestedCriticalError));
        int contestedAccuracyDeductionCount = answers.Count(a => ((BenchmarkAnswerFlags)a.AnswerFlags).HasFlag(BenchmarkAnswerFlags.ContestedAccuracyDeduction));
        // Null on a run before harness 20, which never adjudicated an out-of-rubric basis: that is
        // "not recorded", never zero.
        string contestedAccuracyDeductionFigure = run.ContestedAccuracyDeductionAnswerCount.HasValue || contestedAccuracyDeductionCount > 0
            ? Inv(contestedAccuracyDeductionCount)
            : "not recorded";
        int dimensionOutlierCount = answers.Count(a => ((BenchmarkAnswerFlags)a.AnswerFlags).HasFlag(BenchmarkAnswerFlags.DimensionOutlier));
        int rubricContradictedCount = answers.Count(a => ((BenchmarkAnswerFlags)a.AnswerFlags).HasFlag(BenchmarkAnswerFlags.RubricContradictedBySource));
        // "Not recorded" on a run before harness 46, which never sent a rubric-charged sentence to
        // the verifier: that is not a zero.
        string rubricContradictedFigure = rubricContradictedCount > 0 || !PredatesRubricChargedVerification(run)
            ? Inv(rubricContradictedCount)
            : "not recorded";
        int outOfRubricAccuracyCount = answers.Count(a => ((BenchmarkAnswerFlags)a.AnswerFlags).HasFlag(BenchmarkAnswerFlags.OutOfRubricAccuracyDeduction));
        int answerFramingOpenerCount = answers.Count(a => ((BenchmarkAnswerFlags)a.AnswerFlags).HasFlag(BenchmarkAnswerFlags.AnswerFramingOpener));
        int providerErrorCount = answers.Count(BenchmarkRunFinalizer.HasTerminalFailure);

        // Member B's advisory flags, read from its record. The counts above and the run's stored
        // counts are member A's; a panel run prints the two side by side as "A n, B m".
        var memberBFlagsByAnswer = isPanelRun
            ? answers
                .Select(a => (Answer: a, Flags: BenchmarkCoAssessmentRecord.Parse(a.CoAssessmentJson)?.Flags))
                .Where(x => x.Flags != null)
                .Select(x => (x.Answer, Flags: x.Flags!))
                .ToList()
            : new List<(BenchmarkRunAnswer Answer, BenchmarkCoAssessmentFlags Flags)>();
        List<BenchmarkRunAnswer> MemberBFlagged(Func<BenchmarkCoAssessmentFlags, bool> flag)
            => memberBFlagsByAnswer.Where(x => flag(x.Flags)).Select(x => x.Answer).OrderBy(a => a.OrderIndex).ToList();
        string FlagFigure(string memberAFigure, Func<BenchmarkCoAssessmentFlags, bool> flag)
            => isPanelRun ? $"A {memberAFigure}, B {Inv(MemberBFlagged(flag).Count)}" : memberAFigure;
        int memberBContestedCriticalErrorCount = MemberBFlagged(f => f.ContestedCriticalError).Count;
        int memberBContestedAccuracyDeductionCount = MemberBFlagged(f => f.ContestedAccuracyDeduction).Count;

        int transportDefectCount = answers.Count(a => BenchmarkRunFinalizer.Classify(a) == BenchmarkAnswerIntegrity.TransportDefect);
        int recoveredCount = answers.Count(a => BenchmarkRunFinalizer.Classify(a) == BenchmarkAnswerIntegrity.Recovered);
        int harnessLimitCount = answers.Count(a => BenchmarkRunFinalizer.Classify(a) == BenchmarkAnswerIntegrity.HarnessLimit);
        int unansweredCount = answers.Count(a => BenchmarkRunFinalizer.Classify(a) == BenchmarkAnswerIntegrity.Unanswered);
        int advisoryCount = answers.Count(BenchmarkRunFinalizer.HasAdvisoryFlag);
        // The count above is member A's, as the run stores it; a panel run adds the answers either
        // member flagged, computed from the answers.
        string eitherMemberAdvisoryText = isPanelRun
            ? $"; {Inv(answers.Count(a => BenchmarkRunFinalizer.HasAdvisoryFlag(a) || CoAssessmentAnswerFlags(a) != BenchmarkAnswerFlags.None))} answer(s) on either member"
            : string.Empty;
        // NarrationBlockCount is the honest figure: how many narration blocks the scrubber
        // actually removed from this answer. Runs before harness version 6 did not record it,
        // and there null means "not recorded" — never zero. For those the old proxy stands, a
        // non-empty ScrubbedArtifactText, which is weaker because it is also true when only a
        // leaked payload was removed. That weakness is why the 2026-09-03 run's report claimed
        // narration had been removed from two answers that still carried it.
        static bool NarrationRemoved(BenchmarkRunAnswer a) =>
            a.NarrationBlockCount.HasValue
                ? a.NarrationBlockCount.Value > 0
                : !string.IsNullOrWhiteSpace(a.ScrubbedArtifactText);

        static bool BleedFlagged(BenchmarkRunAnswer a) =>
            ((BenchmarkAnswerFlags)a.AnswerFlags).HasFlag(BenchmarkAnswerFlags.ReasoningBleed);

        int bleedRemoved = answers.Count(a => BleedFlagged(a) && NarrationRemoved(a));
        int bleedUnrecorded = answers.Count(a => BleedFlagged(a) && !a.NarrationBlockCount.HasValue);
        int scrubbedTransportCount = answers.Count(a => a.ScrubbedArtifactCount > 0);
        int scrubbedAnyCount = answers.Count(a =>
            a.ScrubbedArtifactCount > 0 || (BleedFlagged(a) && NarrationRemoved(a)));
        int cleanCount = answers.Count(a => BenchmarkRunFinalizer.Classify(a) == BenchmarkAnswerIntegrity.Clean);
        double cleanPct = totalQuestions > 0 ? (cleanCount * 100.0 / totalQuestions) : 0.0;

        sb.AppendLine("### Run Integrity");
        sb.AppendLine($"- **Clean Answers:** {cleanCount} of {totalQuestions} ({Inv(cleanPct, "F1")}%)");
        sb.AppendLine($"- **Transport Defects:** {transportDefectCount} (empty: {emptyCount}, truncated: {truncatedCount}) — *unrecoverable; excluded or invalid*");
        sb.AppendLine($"- **Recovered:** {recoveredCount} (leaked transport artifacts in: {artifactCount}) — *the harness removed the leaked payloads and graded the answer beneath them; a provider-path defect, not a damaged result*");
        sb.AppendLine($"- **Harness Limits:** {harnessLimitCount} (tool budget exhausted: {answers.Count(a => a.ToolBudgetExhausted)})");

        // H6. Beside the Harness Limits line, never inside it: BenchmarkRunFinalizer.HasHarnessLimit is
        // the tool-budget predicate that feeds Classify, the clean count and the run status, and folding a
        // termination reason into it would move all three. A loop that ended on its iteration cap is a
        // reporting fact about the answer, not a reclassification of it.
        var terminated = answers
            .Where(a => !string.IsNullOrWhiteSpace(a.TerminationReason) &&
                        !string.Equals(a.TerminationReason, "completed", StringComparison.OrdinalIgnoreCase))
            .OrderBy(a => a.OrderIndex)
            .ToList();
        if (terminated.Count > 0)
        {
            string reasonBreakdown = string.Join(", ", terminated
                .GroupBy(a => a.TerminationReason!, StringComparer.OrdinalIgnoreCase)
                .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                .Select(g => $"{g.Key}: {g.Count()} (Q{string.Join(", Q", g.OrderBy(a => a.OrderIndex).Select(a => a.OrderIndex))})"));
            sb.AppendLine($"- **Early Terminations:** {terminated.Count} of {totalQuestions} answer(s) did not end on their own — {reasonBreakdown} — *the answer is valid; the loop stopped before the model did, so it is the answer reachable under the cap*");
        }

        var nearCeiling = answers
            .Select(a => (Answer: a, Note: NearCeilingNote(run, a)))
            .Where(x => x.Note != null &&
                        !string.Equals(x.Answer.TerminationReason, "iteration_limit", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(x.Answer.TerminationReason, "budget_exhausted", StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x.Answer.OrderIndex)
            .ToList();
        if (nearCeiling.Count > 0)
        {
            sb.AppendLine($"- **Near a Ceiling:** {string.Join("; ", nearCeiling.Select(x => $"Q{x.Answer.OrderIndex} — {x.Note}"))} — *finished within one step of a configured cap, so the cap may be shaping the answer even though it never fired*");
        }
        sb.AppendLine($"- **Unanswered:** {unansweredCount} — *the model produced no answer; scored 0, not excluded*");
        sb.AppendLine($"- **Provider Errors:** {providerErrorCount}");
        // The finalizer's own persisted count, as of the last Apply — see IndexHeadline above. A
        // mismatch against Provider Errors means the answers moved since the run was last finalized.
        sb.AppendLine($"- **Terminal provider failures:** {terminalFailureCount}");
        if (RunHasBoard(run))
        {
            foreach (var gap in BoardDeliveryFigures(run, answers).Where(f => f.MissingQuestions.Count > 0))
            {
                sb.AppendLine($"- **Board Not Delivered ({gap.Role}):** {gap.MissingQuestions.Count} of {gap.Total} verdict(s) — Q{string.Join(", Q", gap.MissingQuestions)} — *graded without the game board this snapshot suite is about; re-assess them*");
            }
        }
        sb.AppendLine($"*Clean + transport defects + recovered + harness limits + unanswered = {cleanCount + transportDefectCount + recoveredCount + harnessLimitCount + unansweredCount} of {totalQuestions}.*");
        sb.AppendLine();
        // On the 2026-09-03 run the report claimed the removal was unconditional; the streaming
        // writer's bug (fixed alongside this) meant five graded answers still carried their own
        // narration. The sentence now says so when it happens instead of asserting it away.
        //
        // With no reasoning bleed detected at all there is nothing for either removal clause to
        // describe, and both read as a claim about text that never existed.
        string advisoryNote = bleedCount == 0
            ? "— *advisory only; these overlap the categories above and do not affect the run status. An answer may carry more than one, so the breakdown can exceed the count.*"
            : bleedRemoved == bleedCount
            ? "— *advisory only; these overlap the categories above, do not affect the run status, and the text they describe was removed before grading. An answer may carry more than one, so the breakdown can exceed the count.*"
            : $"— *advisory only; these overlap the categories above and do not affect the run status. Removed before grading in {bleedRemoved} of {bleedCount}; in the remainder the text was detected but remained in the graded answer. An answer may carry more than one, so the breakdown can exceed the count.*";
        if (bleedUnrecorded > 0)
        {
            advisoryNote += $" *Removal was not recorded for {bleedUnrecorded} of these — the run predates harness version {BenchmarkAssessmentPrompt.HarnessVersion}, which added the counter; that figure is inferred, not measured.*";
        }
        sb.AppendLine($"- **Advisory Flags:** {advisoryCount}{eitherMemberAdvisoryText} (reasoning bleed: {bleedCount}, repeated fragments: {repeatCount}, contested verdicts: {FlagFigure(Inv(contestedCount), f => f.ContestedVerdict)}, unevidenced deductions: {FlagFigure(Inv(unevidencedCount), f => f.UnevidencedDeduction)}, omissions as accuracy: {FlagFigure(Inv(omissionCount), f => f.OmissionAsAccuracy)}, refuted claims: {refutedCount}, contested critical errors: {FlagFigure(Inv(contestedCriticalErrorCount), f => f.ContestedCriticalError)}, out-of-rubric accuracy deductions: {FlagFigure(Inv(outOfRubricAccuracyCount), f => f.OutOfRubricAccuracy)}, contested accuracy deductions: {FlagFigure(contestedAccuracyDeductionFigure, f => f.ContestedAccuracyDeduction)}, rubric-charged deduction contradicted: {FlagFigure(rubricContradictedFigure, f => f.RubricContradictedBySource)}, dimension outliers: {FlagFigure(Inv(dimensionOutlierCount), f => f.DimensionOutlier)}, answer-framing openers: {answerFramingOpenerCount}) {advisoryNote}");

        // The Accuracy-specific share of the generic unevidenced-deduction flag, which is shared
        // by dimensions. Read from the stored evidence, so a run graded before the rule existed
        // is measured by it too.
        var precisionWithheldAnswers = answers
            .Where(a => a.AccuracyLevel.HasValue
                        && BenchmarkVerdictConsistency.IsPrecisionGroundedAccuracyDeduction(a.AccuracyLevel.Value, ReadEvidence(a).Accuracy))
            .OrderBy(a => a.OrderIndex)
            .ToList();
        if (precisionWithheldAnswers.Count > 0)
        {
            sb.AppendLine($"- **Accuracy Withheld for Precision:** {precisionWithheldAnswers.Count} ({string.Join(", ", precisionWithheldAnswers.Select(a => $"Q{a.OrderIndex}"))}) — *Accuracy below 6 whose evidence withholds the level for missing precision, nuance or depth and names no false statement. Detected on the stored Accuracy evidence; an advisory heuristic that changed no score.*");
        }
        if (answerFramingOpenerCount > 0)
        {
            var answerFramingOpenerAnswers = answers
                .Where(a => ((BenchmarkAnswerFlags)a.AnswerFlags).HasFlag(BenchmarkAnswerFlags.AnswerFramingOpener))
                .OrderBy(a => a.OrderIndex)
                .ToList();
            // Not a benchmark artifact: unlike the mid-answer lookup narration this report treats as
            // prompt-compliant (see the "Reasoning narration is a benchmark-only removal" note below),
            // the opening text here is left in the graded answer unmodified because it is exactly what
            // production chat would have sent — the defect is in the live prompt, not in this harness.
            sb.AppendLine($"- **Answer-Framing Openers:** {run.AnswerFramingOpenerAnswerCount} ({string.Join(", ", answerFramingOpenerAnswers.Select(a => $"Q{a.OrderIndex}"))}) — *the opening text was **not** removed. It violates the answer-opening rule in `Overseer/ToolGuides/_policy.md` and reaches production chat unmodified.*");
        }
        if (contestedCriticalErrorCount > 0 || memberBContestedCriticalErrorCount > 0)
        {
            var contestedCriticalErrorAnswers = answers
                .Where(a => ((BenchmarkAnswerFlags)a.AnswerFlags).HasFlag(BenchmarkAnswerFlags.ContestedCriticalError))
                .Concat(MemberBFlagged(f => f.ContestedCriticalError))
                .DistinctBy(a => a.OrderIndex)
                .OrderBy(a => a.OrderIndex)
                .ToList();
            string secondReaderDisputeNote = disputedBySecondReader.Count > 0
                ? $" — counts quotes the claim verifier supported; {(isPanelRun ? "reference-reader" : "second-reader")} disputes are counted on the Critical Errors line."
                : string.Empty;
            string contestedCriticalErrorFigure = isPanelRun
                ? $"A {contestedCriticalErrorCount}, B {memberBContestedCriticalErrorCount}"
                : Inv(contestedCriticalErrorCount);
            string contestedEffect = resolvesCriticalErrors && isPanelRun
                ? "Under scoring method 13 a supported quote overturns a critical error only one member raised (see the Critical Errors line); one both members raised stands. Re-assess from the run detail."
                : "Advisory: the cap stands and no index moved; re-assess from the run detail.";
            sb.AppendLine($"- **Contested Critical Errors:** {contestedCriticalErrorFigure} (question(s) {string.Join(", ", contestedCriticalErrorAnswers.Select(a => $"Q{a.OrderIndex}"))}) — the critical-error quote was checked against the source code/wiki by the claim verifier and **supported** as a standalone sentence; the error may lie in its context, so read the verdict's basis before treating the critical error as overturned. {contestedEffect}{secondReaderDisputeNote}");
        }
        if (contestedAccuracyDeductionCount > 0 || memberBContestedAccuracyDeductionCount > 0)
        {
            var contestedAccuracyDeductionAnswers = answers
                .Where(a => ((BenchmarkAnswerFlags)a.AnswerFlags).HasFlag(BenchmarkAnswerFlags.ContestedAccuracyDeduction))
                .OrderBy(a => a.OrderIndex)
                .ToList();
            // An answer with both causes is listed under both. In a panel run each member's flag is
            // explained by the items that member raised.
            var byCause = contestedAccuracyDeductionAnswers
                .SelectMany(a => ContestedDeductionCauses(a, isPanelRun ? BenchmarkPanelMember.A : (BenchmarkPanelMember?)null).Select(cause => (Cause: cause, Answer: a)))
                .ToList();
            var byCauseB = MemberBFlagged(f => f.ContestedAccuracyDeduction)
                .SelectMany(a => ContestedDeductionCauses(a, BenchmarkPanelMember.B).Select(cause => (Cause: cause, Answer: a)))
                .ToList();
            IEnumerable<string> CauseParts(List<(string Cause, BenchmarkRunAnswer Answer)> causes, BenchmarkPanelMember? member = null)
                => new[] { BasisRefutedCause, AccusationSupportedCause, AssessorStatementRefutedCause, DockedSuspicionSupportedCause, CauseNotRecorded }
                    .Select(cause => (Cause: cause, Answers: causes.Where(x => x.Cause == cause).Select(x => $"Q{x.Answer.OrderIndex}").ToList()))
                    .Where(p => p.Answers.Count > 0)
                    .Select(p => $"{CauseText(p.Cause, member)}: {string.Join(", ", p.Answers)}");
            var causeParts = CauseParts(byCause);
            var allCauses = byCause.Concat(byCauseB).ToList();
            // The third cause is named only where it occurs, so a report without one reads as before.
            string charger = isPanelRun ? "a panel member" : "the assessor";
            string assessorStatementClause = allCauses.Any(x => x.Cause == AssessorStatementRefutedCause)
                ? (isPanelRun ? ", or a statement of a panel member's own accuracy evidence was **refuted**" : ", or a statement of the assessor's own accuracy evidence was **refuted**")
                : string.Empty;
            string dockedSuspicionClause = allCauses.Any(x => x.Cause == DockedSuspicionSupportedCause)
                ? $", or a sentence {charger} reported as suspected false and docked Accuracy for was **supported**"
                : string.Empty;
            string causesText = isPanelRun
                ? string.Join("; ", new[]
                    {
                        byCause.Count > 0 ? $"member A — {string.Join("; ", causeParts)}" : null,
                        byCauseB.Count > 0 ? $"member B — {string.Join("; ", CauseParts(byCauseB, BenchmarkPanelMember.B))}" : null
                    }.Where(p => p != null))
                : string.Join("; ", causeParts);
            string countText = isPanelRun
                ? $"A {contestedAccuracyDeductionCount}, B {memberBContestedAccuracyDeductionCount}"
                : Inv(contestedAccuracyDeductionCount);
            sb.AppendLine($"- **Contested Accuracy Deductions:** {countText} — {causesText}. The claim verifier checked these against the source code/wiki: either the own-knowledge statement an out-of-rubric Accuracy deduction rests on was **refuted**, or a sentence {charger} quoted as false was **supported**{assessorStatementClause}{dockedSuspicionClause}. Advisory: the deduction stands and no index moved; re-assess from the run detail.");
        }
        AppendRubricContradictions(sb, answers, isPanelRun);
        sb.AppendLine($"- **Answers Scrubbed:** {scrubbedAnyCount} of {totalQuestions} (transport payloads: {scrubbedTransportCount}, reasoning narration: {bleedRemoved})");
        sb.AppendLine();

        // Assessor Findings — advisory signals about the *grading* rather than the answers:
        // what the assessor could not verify, where its own prose contradicted its
        // critical-error flag, and where an omission was docked under accuracy.
        var withClaims = answers
            .Where(a => (a.UnverifiedClaimCount ?? 0) > 0)
            .OrderBy(a => a.OrderIndex)
            .ToList();
        int unverifiedTotal = withClaims.Sum(a => a.UnverifiedClaimCount!.Value);
        bool claimsRecorded = answers.Any(a => a.UnverifiedClaimCount.HasValue);
        var contestedAnswers = answers
            .Where(a => (((BenchmarkAnswerFlags)a.AnswerFlags) & BenchmarkAnswerFlags.ContestedVerdict) != 0)
            .OrderBy(a => a.OrderIndex)
            .ToList();
        var omissionAnswers = answers
            .Where(a => (((BenchmarkAnswerFlags)a.AnswerFlags) & BenchmarkAnswerFlags.OmissionAsAccuracy) != 0)
            .OrderBy(a => a.OrderIndex)
            .ToList();
        var dimensionOutlierAnswers = answers
            .Where(a => (((BenchmarkAnswerFlags)a.AnswerFlags) & BenchmarkAnswerFlags.DimensionOutlier) != 0)
            .OrderBy(a => a.OrderIndex)
            .ToList();
        var refutedAnswers = answers
            .Where(a => (a.ClaimsRefutedCount ?? 0) > 0 && !string.IsNullOrWhiteSpace(a.ClaimVerificationJson))
            .OrderBy(a => a.OrderIndex)
            .ToList();
        // Distinguishes "the verifier ran and returned no verdict" from "the deterministic token
        // budget stopped the run before a call was made for this answer" — the same
        // ClaimVerificationError field carries both (see BenchmarkService.RunClaimVerificationAsync
        // / BenchmarkClaimVerificationNotCheckedReason), and conflating them would report a budget
        // cutoff as a verifier defect.
        var notCheckedAnswers = answers
            .Where(a => IsClaimVerificationBudgetNotChecked(a.ClaimVerificationError))
            .OrderBy(a => a.OrderIndex)
            .ToList();
        var verificationFailedAnswers = answers
            .Where(a => !string.IsNullOrWhiteSpace(a.ClaimVerificationError) && !IsClaimVerificationBudgetNotChecked(a.ClaimVerificationError))
            .OrderBy(a => a.OrderIndex)
            .ToList();
        var supportedAccusations = answers
            .OrderBy(a => a.OrderIndex)
            .SelectMany(a => SupportedAccusationsOf(a).Select(v => (Answer: a, Verification: v)))
            .ToList();
        // Computed from ClaimVerificationJson by role; the run's claim-count columns count the
        // answers' own claims only.
        var accusedByAnswer = answers
            .Select(a => (Answer: a, Accused: AccusedSentencesOf(a)))
            .Where(x => x.Accused.Count > 0)
            .ToList();
        var accusedRunTotals = VerdictCounts(accusedByAnswer.SelectMany(x => x.Accused));
        int accusedRunCount = accusedByAnswer.Sum(x => x.Accused.Count);
        var assessorStatementsByAnswer = answers
            .Select(a => (Answer: a, Statements: AssessorStatementsOf(a)))
            .Where(x => x.Statements.Count > 0)
            .ToList();
        var assessorStatementTotals = VerdictCounts(assessorStatementsByAnswer.SelectMany(x => x.Statements));
        int assessorStatementRunCount = assessorStatementsByAnswer.Sum(x => x.Statements.Count);
        var suspectedFalseByAnswer = answers
            .Select(a => (Answer: a, Claims: SuspectedFalseClaimsOf(a)))
            .Where(x => x.Claims.Count > 0)
            .ToList();
        var suspectedFalseTotals = VerdictCounts(suspectedFalseByAnswer.SelectMany(x => x.Claims));
        int suspectedFalseRunCount = suspectedFalseByAnswer.Sum(x => x.Claims.Count);
        // A panel run counts the answers' own claims from the verifier's record, once each however
        // many members raised them; member A's claim-count column holds member A's alone.
        var panelClaimsByAnswer = isPanelRun
            ? answers
                .Select(a => (Answer: a, Claims: (ClaimVerificationsOf(a) ?? new List<BenchmarkClaimVerification>()).Where(BenchmarkClaimRoles.IsOrdinaryClaim).ToList()))
                .Where(x => x.Claims.Count > 0)
                .ToList()
            : new List<(BenchmarkRunAnswer Answer, List<BenchmarkClaimVerification> Claims)>();
        int panelClaimCount = panelClaimsByAnswer.Sum(x => x.Claims.Count);

        if (!claimsRecorded || unverifiedTotal > 0 || panelClaimCount > 0 || contestedAnswers.Count > 0 || omissionAnswers.Count > 0 || dimensionOutlierAnswers.Count > 0 || refutedAnswers.Count > 0 || verificationFailedAnswers.Count > 0 || notCheckedAnswers.Count > 0 || supportedAccusations.Count > 0 || accusedRunCount > 0 || assessorStatementRunCount > 0)
        {
            sb.AppendLine("### Assessor Findings");
            if (!claimsRecorded)
            {
                sb.AppendLine(PredatesHarnessVersion(run, UnverifiedClaimsHarnessVersion)
                    ? $"- **Unverified Claims:** not recorded — this run predates harness version {UnverifiedClaimsHarnessVersion}, which added the field."
                    : "- **Unverified Claims:** not recorded — no answer carries a claim count, so no assessment reached the stage that records it.");
            }
            else if (panelClaimCount > 0)
            {
                var panelClaimTotals = VerdictCounts(panelClaimsByAnswer.SelectMany(x => x.Claims));
                sb.AppendLine($"- **Unverified Claims:** {panelClaimCount} across {panelClaimsByAnswer.Count} answer(s) ({string.Join(", ", panelClaimsByAnswer.Select(x => $"Q{x.Answer.OrderIndex}"))}) — verified: {panelClaimTotals.Supported} supported, {panelClaimTotals.Refuted} refuted, {panelClaimTotals.Indeterminate} indeterminate ({MemberSplitText(panelClaimsByAnswer.SelectMany(x => x.Claims), "member A", "member B")}) — *claims a panel member could neither confirm nor refute against the rubric, each counted once however many members raised it. Advisory: from harness version 7 these do not reduce Accuracy.*");

                if (verificationClearedAnswers.Count > 0)
                {
                    sb.AppendLine($"- **Verification-cleared Accuracy deductions (member A):** {verificationClearedAnswers.Count} ({string.Join(", ", verificationClearedAnswers.Select(a => $"Q{a.OrderIndex}"))}) — Accuracy was docked citing only claims the rubric did not cover, and every such claim the verifier later checked was supported. Advisory; this is the instrument's share of the Accuracy shortfall, as OUT-OF-SCOPE is of Completeness.");
                }

                if (verificationFailedAnswers.Count > 0)
                {
                    string firstError = BenchmarkAssessmentFailure.Truncate(verificationFailedAnswers[0].ClaimVerificationError, 200) ?? string.Empty;
                    sb.AppendLine($"- **Claim Verification Failed:** {verificationFailedAnswers.Count} answer(s) ({string.Join(", ", verificationFailedAnswers.Select(a => $"Q{a.OrderIndex}"))}) — the verifier was configured but returned no verdict. First error: `{firstError}`.");
                }
            }
            else if (unverifiedTotal > 0)
            {
                int totalSupported = run.ClaimsSupportedCount > 0 ? run.ClaimsSupportedCount : answers.Sum(a => a.ClaimsSupportedCount ?? 0);
                int totalRefuted = run.ClaimsRefutedCount > 0 ? run.ClaimsRefutedCount : answers.Sum(a => a.ClaimsRefutedCount ?? 0);
                int totalIndeterminate = run.ClaimsIndeterminateCount > 0 ? run.ClaimsIndeterminateCount : answers.Sum(a => a.ClaimsIndeterminateCount ?? 0);
                bool hasVerification = (run.ClaimVerifiedAnswerCount > 0 || answers.Any(a => a.ClaimsSupportedCount.HasValue || a.ClaimsRefutedCount.HasValue || a.ClaimsIndeterminateCount.HasValue))
                    && (totalSupported > 0 || totalRefuted > 0 || totalIndeterminate > 0);

                string outcome = hasVerification
                    ? $" — verified: {totalSupported} supported, {totalRefuted} refuted, {totalIndeterminate} indeterminate."
                    : string.Empty;

                sb.AppendLine($"- **Unverified Claims:** {unverifiedTotal} across {withClaims.Count} answer(s) ({string.Join(", ", withClaims.Select(a => $"Q{a.OrderIndex}"))}){outcome} — *claims the assessor could neither confirm nor refute against the rubric. Advisory: from harness version 7 these do not reduce Accuracy.*");

                if (verificationClearedAnswers.Count > 0)
                {
                    sb.AppendLine($"- **Verification-cleared Accuracy deductions:** {verificationClearedAnswers.Count} ({string.Join(", ", verificationClearedAnswers.Select(a => $"Q{a.OrderIndex}"))}) — Accuracy was docked citing only claims the rubric did not cover, and every such claim the verifier later checked was supported. Advisory; this is the instrument's share of the Accuracy shortfall, as OUT-OF-SCOPE is of Completeness.");
                }

                if (verificationFailedAnswers.Count > 0)
                {
                    string firstError = BenchmarkAssessmentFailure.Truncate(verificationFailedAnswers[0].ClaimVerificationError, 200) ?? string.Empty;
                    sb.AppendLine($"- **Claim Verification Failed:** {verificationFailedAnswers.Count} of {withClaims.Count} answer(s) with unverified claims ({string.Join(", ", verificationFailedAnswers.Select(a => $"Q{a.OrderIndex}"))}) — the verifier was configured but returned no verdict, so the unverified claims above were never checked. First error: `{firstError}`.");
                }
            }
            else if (verificationFailedAnswers.Count > 0)
            {
                string firstError = BenchmarkAssessmentFailure.Truncate(verificationFailedAnswers[0].ClaimVerificationError, 200) ?? string.Empty;
                sb.AppendLine($"- **Claim Verification Failed:** {verificationFailedAnswers.Count} answer(s) ({string.Join(", ", verificationFailedAnswers.Select(a => $"Q{a.OrderIndex}"))}) — the verifier was configured but returned no verdict. First error: `{firstError}`.");
            }
            if (notCheckedAnswers.Count > 0)
            {
                sb.AppendLine($"- **Claim Verification Not Checked (budget):** {notCheckedAnswers.Count} answer(s) ({string.Join(", ", notCheckedAnswers.Select(a => $"Q{a.OrderIndex}"))}) — the configured `Benchmark:ClaimVerificationInputTokenBudget` was exhausted before these were checked. Not a verifier failure: no call was made.");
            }
            if (contestedAnswers.Count > 0)
            {
                sb.AppendLine($"- **Contested Verdicts:** {contestedAnswers.Count} ({string.Join(", ", contestedAnswers.Select(a => $"Q{a.OrderIndex}"))}) — *the assessor's own comment describes a fabrication while its critical-error flag is false. Advisory; no scoring effect.*");
            }
            if (omissionAnswers.Count > 0)
            {
                sb.AppendLine($"- **Omission Docked as Accuracy:** {omissionAnswers.Count} ({string.Join(", ", omissionAnswers.Select(a => $"Q{a.OrderIndex}"))}) — *the assessor docked Accuracy citing an omission, which scoring rules reserve for Completeness. An omission is not a defect of truthfulness.*");
            }
            if (dimensionOutlierAnswers.Count > 0)
            {
                sb.AppendLine($"- **Dimension Outliers:** {dimensionOutlierAnswers.Count} ({string.Join(", ", dimensionOutlierAnswers.Select(a => $"Q{a.OrderIndex}"))}) — *one dimension at level ≤ 1 beside three at ≥ 3 with no defect of that kind named. Advisory; routed to {(isPanelRun ? "the reference reader" : "a second reader")}.*");
            }
            if (accusedRunCount > 0)
            {
                sb.AppendLine(isPanelRun
                    ? $"- **Accused Sentences Checked:** {accusedRunCount} ({MemberSplitText(accusedByAnswer.SelectMany(x => x.Accused), membersOf: v => v.AccusingMembers)}) across {accusedByAnswer.Count} answer(s) ({string.Join(", ", accusedByAnswer.Select(x => $"Q{x.Answer.OrderIndex}"))}) — supported {accusedRunTotals.Supported}, refuted {accusedRunTotals.Refuted}, indeterminate {accusedRunTotals.Indeterminate}. *Sentences a panel member quoted as false when it docked Accuracy, sent to the claim verifier; counted apart from the answers' own claims.*"
                    : $"- **Accused Sentences Checked:** {accusedRunCount} across {accusedByAnswer.Count} answer(s) ({string.Join(", ", accusedByAnswer.Select(x => $"Q{x.Answer.OrderIndex}"))}) — supported {accusedRunTotals.Supported}, refuted {accusedRunTotals.Refuted}, indeterminate {accusedRunTotals.Indeterminate}. *Sentences the assessor quoted as false when it docked Accuracy, sent to the claim verifier; counted apart from the answers' own claims.*");
            }
            if (assessorStatementRunCount > 0)
            {
                sb.AppendLine($"- **Assessor Statements Checked:** {assessorStatementRunCount} across {assessorStatementsByAnswer.Count} answer(s) ({string.Join(", ", assessorStatementsByAnswer.Select(x => $"Q{x.Answer.OrderIndex}"))}) — supported {assessorStatementTotals.Supported}, refuted {assessorStatementTotals.Refuted}, indeterminate {assessorStatementTotals.Indeterminate}. *Sentences of the assessor's own accuracy evidence, sent to the claim verifier on a contested answer. A refuted one means the **assessor** was wrong; none is counted as a claim of the answer.*");
            }
            if (suspectedFalseRunCount > 0 && isPanelRun)
            {
                sb.AppendLine($"- **Suspected False by the Panel:** {suspectedFalseRunCount} ({MemberSplitText(suspectedFalseByAnswer.SelectMany(x => x.Claims), membersOf: v => v.SuspectingMembers)}) across {suspectedFalseByAnswer.Count} answer(s) ({string.Join(", ", suspectedFalseByAnswer.Select(x => $"Q{x.Answer.OrderIndex}"))}) — refuted {suspectedFalseTotals.Refuted} (the verifier sided with the member), supported {suspectedFalseTotals.Supported} (the verifier sided with the answer), indeterminate {suspectedFalseTotals.Indeterminate}. *Answer sentences a panel member believed false from its own knowledge, which neither the rubric nor the board settles; under scoring method {Inv(run.ScoringMethodVersion)} they lower no level and are checked by the claim verifier instead. Included in the unverified claims above.*");
            }
            else if (suspectedFalseRunCount > 0)
            {
                sb.AppendLine($"- **Suspected False by the Assessor:** {suspectedFalseRunCount} across {suspectedFalseByAnswer.Count} answer(s) ({string.Join(", ", suspectedFalseByAnswer.Select(x => $"Q{x.Answer.OrderIndex}"))}) — refuted {suspectedFalseTotals.Refuted} (the verifier sided with the assessor), supported {suspectedFalseTotals.Supported} (the verifier sided with the answer), indeterminate {suspectedFalseTotals.Indeterminate}. *Answer sentences the assessor believed false from its own knowledge, which neither the rubric nor the board settles; under scoring method {Inv(run.ScoringMethodVersion)} they lower no level and are checked by the claim verifier instead. Included in the unverified claims above.*");
            }
            if (supportedAccusations.Count > 0)
            {
                sb.AppendLine(isPanelRun
                    ? $"- **Supported Accusations:** {supportedAccusations.Count} ({MemberSplitText(supportedAccusations.Select(s => s.Verification), membersOf: v => v.AccusingMembers)}) ({string.Join(", ", supportedAccusations.Select(s => $"Q{s.Answer.OrderIndex}").Distinct())}) — *a sentence a panel member quoted when it docked Accuracy was checked by the claim verifier and supported. The harness finds only accusations a member quoted, so this is a bounded count. Advisory; the deduction stands and no score moves.*"
                    : $"- **Supported Accusations:** {supportedAccusations.Count} ({string.Join(", ", supportedAccusations.Select(s => $"Q{s.Answer.OrderIndex}").Distinct())}) — *a sentence the assessor quoted when it docked Accuracy was checked by the claim verifier and supported. The harness finds only accusations the assessor quoted, so this is a bounded count. Advisory; the deduction stands and no score moves.*");
                foreach (var (ans, v) in supportedAccusations)
                {
                    sb.AppendLine($"  - **Q{ans.OrderIndex}:** a sentence {AccusedByText(v)} charged as false was checked by the claim verifier and **supported** — \"{v.Claim}\" ({v.Citation}).");
                }
            }
            if (refutedAnswers.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("#### Refuted Claims");
                sb.AppendLine("*(Advisory; recorded after scoring and folded into no index)*");
                sb.AppendLine();
                foreach (var ans in refutedAnswers)
                {
                    try
                    {
                        var vers = JsonSerializer.Deserialize<List<BenchmarkClaimVerification>>(ans.ClaimVerificationJson!, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                        if (vers != null)
                        {
                            // The out-of-rubric basis, the critical-error quote, an accused sentence
                            // and an assessor statement are the assessor's statements or charges, not
                            // claims the answer left unverified; a refutation of any of them is
                            // reported elsewhere.
                            foreach (var v in BenchmarkService.OrdinaryClaimVerifications(vers, ans)
                                         .Where(x => x.EffectiveVerdict == BenchmarkClaimVerdict.Refuted))
                            {
                                string suspectedTag = v.SuspectedFalse == true ? $" *(suspected false by {SuspectedByText(v)})*" : string.Empty;
                                sb.AppendLine($"- **Q{ans.OrderIndex}:** \"{v.Claim}\"{suspectedTag}");
                                if (!string.IsNullOrWhiteSpace(v.Citation))
                                {
                                    sb.AppendLine($"  - **Citation:** {v.Citation}");
                                }
                                // A basis that only repeats the citation adds nothing.
                                if (!string.IsNullOrWhiteSpace(v.Basis)
                                    && !string.Equals(v.Basis.Trim(), v.Citation?.Trim(), StringComparison.Ordinal))
                                {
                                    sb.AppendLine($"  - **Basis:** {v.Basis}");
                                }
                            }
                        }
                    }
                    catch (JsonException)
                    {
                        // Ignore malformed JSON in report
                    }
                }
            }
            sb.AppendLine();
        }

        // The reference reader against the panel: its critical-error flag splits from the panel only
        // when it differs from both members', which requires the members to agree with each other.
        bool ReaderSplitsFromPanel(BenchmarkRunAnswer a) =>
            a.SecondOpinionCriticalError.HasValue && a.CoAssessmentCriticalError.HasValue &&
            a.SecondOpinionCriticalError.Value != a.CriticalError &&
            a.SecondOpinionCriticalError.Value != a.CoAssessmentCriticalError.Value;

        // A gap above the second-opinion threshold from the panel score, or a critical-error split
        // against both members.
        bool ReaderDisagreesWithPanel(BenchmarkRunAnswer a) =>
            a.SecondOpinionQualityScore.HasValue && a.PanelQualityScore.HasValue &&
            (Math.Abs(a.SecondOpinionQualityScore.Value - a.PanelQualityScore.Value) > BenchmarkService.SecondOpinionDisagreementPoints
             || ReaderSplitsFromPanel(a));

        // At a Glance's one-line summary of the Panel Agreement figures; set for a panel run only.
        string? glanceAgreementLine = null;

        // Panel Agreement: member B against member A over the answers both scored. The finalizer's
        // stored statistics are read first; the answers supply each figure a run did not store and
        // every question list.
        if (isPanelRun)
        {
            var panelPairs = answers
                .Where(a => BenchmarkRunFinalizer.CountsTowardQualityIndex(a)
                            && a.AssessmentStatus == BenchmarkAssessmentStatus.Scored && a.QualityScore.HasValue
                            && a.CoAssessmentStatus == BenchmarkAssessmentStatus.Scored && a.CoAssessmentQualityScore.HasValue)
                .OrderBy(a => a.OrderIndex)
                .ToList();
            int answeredForPanel = answers.Count(BenchmarkRunFinalizer.CountsTowardQualityIndex);
            int panelGraded = run.PanelGradedAnswerCount ?? panelPairs.Count;
            double? panelMeanAbs = run.PanelMeanAbsDelta.HasValue
                ? BenchmarkRunFinalizer.RoundAgreementDelta(run.PanelMeanAbsDelta.Value)
                : (panelPairs.Count > 0
                    ? BenchmarkRunFinalizer.RoundAgreementDelta(panelPairs.Average(a => (double)Math.Abs(a.CoAssessmentQualityScore!.Value - a.QualityScore!.Value)))
                    : null);
            double? panelMeanSigned = run.PanelMeanSignedDelta.HasValue
                ? BenchmarkRunFinalizer.RoundAgreementDelta(run.PanelMeanSignedDelta.Value)
                : (panelPairs.Count > 0
                    ? BenchmarkRunFinalizer.RoundAgreementDelta(panelPairs.Average(a => (double)(a.CoAssessmentQualityScore!.Value - a.QualityScore!.Value)))
                    : null);
            double? panelIcc = run.PanelIntraclassCorrelation
                ?? BenchmarkScoring.IntraclassCorrelationAbsolute(panelPairs
                    .Select(a => ((double)a.QualityScore!.Value, (double)a.CoAssessmentQualityScore!.Value))
                    .ToList());
            var panelSplits = panelPairs
                .Where(a => a.CoAssessmentCriticalError.HasValue && a.CoAssessmentCriticalError.Value != a.CriticalError)
                .ToList();
            int panelSplitCount = run.PanelCriticalErrorSplitCount ?? panelSplits.Count;
            var panelDisagreed = panelPairs.Where(a => a.PanelDisagreed == true).ToList();
            int panelDisagreementCount = run.PanelDisagreementCount ?? panelDisagreed.Count;
            var panelUnscored = answers
                .Where(a => BenchmarkRunFinalizer.CountsTowardQualityIndex(a) && !IndexQualityOf(a).HasValue)
                .OrderBy(a => a.OrderIndex)
                .ToList();

            sb.AppendLine("### Panel Agreement");
            sb.AppendLine($"- **Members:** A {memberALabel} ({run.AssessorModelSnapshot.Provider}, {memberARelation}); B {memberBLabel} ({run.CoAssessorModelSnapshot?.Provider}, {memberBRelation}). Both grade every answer blind to each other, with the identical prompt; the published score is their mean.");
            sb.AppendLine($"- **Graded by both:** {panelGraded} of {answeredForPanel} answered questions.");
            sb.AppendLine(panelMeanAbs.HasValue
                ? $"- **Mean absolute difference |B − A|:** {Inv(panelMeanAbs.Value, "F1")} points."
                : "- **Mean absolute difference |B − A|:** not recorded.");
            sb.AppendLine(panelMeanSigned.HasValue
                ? $"- **Mean signed difference B − A:** {SignedDelta(panelMeanSigned.Value)} points — negative means member B graded lower."
                : "- **Mean signed difference B − A:** not recorded.");
            sb.AppendLine(panelIcc.HasValue
                ? $"- **ICC(A,1):** {Inv(panelIcc.Value, "F2")} — two-way random, absolute agreement, single rater."
                : "- **ICC(A,1):** not computed — fewer than 5 answers both members scored, or no variance between their scores.");
            if (panelPairs.Count > 0)
            {
                var spreadA = BenchmarkPanelDiagnostics.MemberSpread(panelPairs, BenchmarkPanelMember.A);
                var spreadB = BenchmarkPanelDiagnostics.MemberSpread(panelPairs, BenchmarkPanelMember.B);
                string narrowNote = BenchmarkPanelDiagnostics.IsNarrowSpread(spreadA, spreadB)
                    ? " *Both members used a narrow range, so ICC measures agreement on a few points' difference and says little; read the mean |B − A| and the disagreements instead.*"
                    : string.Empty;
                sb.AppendLine($"- **Member spread:** {MemberSpreadText(spreadA)}; {MemberSpreadText(spreadB)}.{narrowNote}");
            }
            sb.AppendLine(panelSplitCount > 0
                ? $"- **Critical-error splits:** {panelSplitCount} of {panelGraded}" + (panelSplits.Count > 0 ? " — " + string.Join(", ", panelSplits.Select(a => $"Q{a.OrderIndex}")) : string.Empty) + ". The members disagree on whether the answer contains a fabrication; each member's own cap enters the mean."
                : $"- **Critical-error splits:** none of {panelGraded}.");
            if (panelGraded > 0)
            {
                double panelDisagreementPct = panelDisagreementCount * 100.0 / panelGraded;
                string panelDisagreedNamed = panelDisagreed.Count > 0
                    ? " — " + string.Join(", ", panelDisagreed.Select(a => $"Q{a.OrderIndex}"))
                    : string.Empty;
                sb.AppendLine($"- **Disagreements:** {panelDisagreementCount} of {panelGraded} ({Inv(panelDisagreementPct, "F1")}%){panelDisagreedNamed}. *A disagreement is a gap above {BenchmarkService.SecondOpinionDisagreementPoints} quality points or a split on criticalError.*");
            }
            if (panelUnscored.Count > 0)
            {
                static string MemberStatus(BenchmarkAssessmentStatus? status) => status?.ToString() ?? "not attempted";
                string unscoredNamed = string.Join(", ", panelUnscored.Select(a =>
                {
                    var missing = new List<string>();
                    if (a.AssessmentStatus != BenchmarkAssessmentStatus.Scored) missing.Add($"member A: {MemberStatus(a.AssessmentStatus)}");
                    if (a.CoAssessmentStatus != BenchmarkAssessmentStatus.Scored) missing.Add($"member B: {MemberStatus(a.CoAssessmentStatus)}");
                    return missing.Count > 0 ? $"Q{a.OrderIndex} ({string.Join(", ", missing)})" : $"Q{a.OrderIndex}";
                }));
                sb.AppendLine($"- **Not scored by the panel:** {panelUnscored.Count} — {unscoredNamed}. *An answer needs both members' verdicts for a panel score; these are excluded from the Intelligence Index until retried.*");
            }
            else
            {
                sb.AppendLine("- **Not scored by the panel:** none.");
            }
            sb.AppendLine();

            glanceAgreementLine = panelGraded > 0
                ? $"- **Grader agreement:** members A and B both graded {panelGraded} of {answeredForPanel} answered questions; " +
                  (panelMeanAbs.HasValue ? $"they differ by {Inv(panelMeanAbs.Value, "F1")} points on average" : "their mean difference is not recorded") +
                  (panelIcc.HasValue ? $", ICC(A,1) {Inv(panelIcc.Value, "F2")}" : ", ICC(A,1) not computed") +
                  $", and disagree on {panelDisagreementCount} of {panelGraded}."
                : $"- **Grader agreement:** not measured — members A and B both graded none of {answeredForPanel} answered questions.";
        }

        // Assessor Agreement. The coverage fraction travels with the figure everywhere it is
        // printed, because the two are not separable: a mean delta over trigger-selected answers
        // is conditioned on the first assessor's own uncertainty and says nothing about the
        // instrument, while the same number over every answer is an inter-rater agreement rate.
        // In a panel run the second opinion is the reference reader, read against the panel score.
        if (isPanelRun && answers.Any(a => a.SecondOpinionQualityScore.HasValue))
        {
            int answeredForAgreement = answers.Count(BenchmarkRunFinalizer.CountsTowardQualityIndex);
            var readerCovered = ReferenceReaderCovered(answers);
            var readerGraded = readerCovered.Where(a => a.PanelQualityScore.HasValue).ToList();

            sb.AppendLine("### Reference Reader Agreement");
            sb.AppendLine($"- **Reader:** {run.SecondOpinionAssessorModelSnapshot.Label() ?? "reference reader"} ({run.SecondOpinionAssessorModelSnapshot?.Provider}, {FamilyRelation(run.TestedModelSnapshot.Provider, run.SecondOpinionAssessorModelSnapshot?.Provider)}) — advisory, never scores; compared against the panel score.");
            sb.AppendLine($"- **Coverage:** {readerCovered.Count} of {answeredForAgreement} answered questions" +
                (readerGraded.Count < readerCovered.Count ? $"; {readerGraded.Count} of them carry a panel score to compare against." : "."));
            sb.AppendLine($"- **Prompt Protocol:** {(run.SecondOpinionBlindUsed ? "Blind — the reference reader received the candidate's answer without seeing either member's scores, comments, or critical error flags." : "Anchored — the reference reader saw a member's verdict and comment.")}");
            if (readerGraded.Count > 0)
            {
                double readerAbs = BenchmarkRunFinalizer.RoundAgreementDelta(readerGraded.Average(a => Math.Abs(a.SecondOpinionQualityScore!.Value - a.PanelQualityScore!.Value)));
                double readerSigned = ReferenceReaderSignedOffset(answers)!.Value;
                double readerVsA = BenchmarkRunFinalizer.RoundAgreementDelta(readerGraded.Average(a => (double)(a.SecondOpinionQualityScore!.Value - a.QualityScore!.Value)));
                double readerVsB = BenchmarkRunFinalizer.RoundAgreementDelta(readerGraded.Average(a => (double)(a.SecondOpinionQualityScore!.Value - a.CoAssessmentQualityScore!.Value)));
                sb.AppendLine($"- **Mean absolute difference from the panel:** {Inv(readerAbs, "F1")} points.");
                sb.AppendLine($"- **Mean signed difference from the panel:** {SignedDelta(readerSigned)} points (over {readerGraded.Count} of {answeredForAgreement} answered).");
                sb.AppendLine($"- **Mean signed difference from each member:** member A {SignedDelta(readerVsA)} points, member B {SignedDelta(readerVsB)} points. *A reader of a third family that sits closer to one member is evidence about that member's calibration, not about the candidate; its neutrality between the two families is an assumption, not a measurement.*");

                var readerSplits = readerGraded.Where(ReaderSplitsFromPanel).ToList();
                if (readerSplits.Count > 0)
                {
                    sb.AppendLine($"- **Critical-error splits:** {readerSplits.Count} of {readerGraded.Count} — {string.Join(", ", readerSplits.Select(a => $"Q{a.OrderIndex}"))}. The reader's critical-error flag differs from both members'.");
                }
                var readerDisagreed = readerGraded.Where(ReaderDisagreesWithPanel).ToList();
                double readerDisagreementPct = readerDisagreed.Count * 100.0 / readerGraded.Count;
                string readerDisagreedNamed = readerDisagreed.Count > 0
                    ? " — " + string.Join(", ", readerDisagreed.Select(a => $"Q{a.OrderIndex}"))
                    : string.Empty;
                sb.AppendLine($"- **Disagreements:** {readerDisagreed.Count} of {readerGraded.Count} ({Inv(readerDisagreementPct, "F1")}%){readerDisagreedNamed}. *A disagreement is a gap above {BenchmarkService.SecondOpinionDisagreementPoints} quality points from the panel score, or a critical-error flag that differs from both members'.*");

                // The same definition once the reader's printed mean offset is taken off every gap, so
                // a reader that grades uniformly higher or lower is not counted as disagreeing throughout.
                const int offsetAdjustedMinAnswers = 5;
                if (readerGraded.Count >= offsetAdjustedMinAnswers)
                {
                    var offsetDisagreed = readerGraded
                        .Where(a => Math.Abs(a.SecondOpinionQualityScore!.Value - a.PanelQualityScore!.Value - readerSigned) > BenchmarkService.SecondOpinionDisagreementPoints
                                    || ReaderSplitsFromPanel(a))
                        .ToList();
                    string offsetDisagreedNamed = offsetDisagreed.Count > 0
                        ? " — " + string.Join(", ", offsetDisagreed.Select(a => $"Q{a.OrderIndex}"))
                        : string.Empty;
                    sb.AppendLine($"- **Disagreements after removing the reader's mean offset ({SignedDelta(readerSigned)}):** {offsetDisagreed.Count} of {readerGraded.Count}{offsetDisagreedNamed}. *Advisory; the definition above is unchanged.*");
                }
            }
            else
            {
                sb.AppendLine("- **Mean absolute difference from the panel:** not computed — no reader verdict sits beside a panel score.");
            }
            sb.AppendLine();
        }
        else if (isPanelRun && run.SecondOpinionAssessorModelConfigurationId.HasValue)
        {
            int answeredForAgreement = answers.Count(BenchmarkRunFinalizer.CountsTowardQualityIndex);
            var readerFailedAnswers = answers
                .Where(a => !string.IsNullOrWhiteSpace(a.SecondOpinionError))
                .OrderBy(a => a.OrderIndex)
                .ToList();

            sb.AppendLine("### Reference Reader Agreement");
            sb.AppendLine("- **Mode:** All, blind — reference reader: advisory, never scores; compared against the panel score.");
            if (readerFailedAnswers.Count > 0)
            {
                string firstError = readerFailedAnswers[0].SecondOpinionError!.Trim();
                if (firstError.Length > 200)
                {
                    firstError = firstError.Substring(0, 197) + "...";
                }
                sb.AppendLine($"- **Coverage:** 0 of {answeredForAgreement} answered questions. **{readerFailedAnswers.Count} reference reader call(s) failed, so the reader's agreement with the panel is not measured for this run.** First error: `{firstError}`.");
            }
            else
            {
                sb.AppendLine($"- **Coverage:** 0 of {answeredForAgreement} answered questions. **The reference reader recorded no verdict, so its agreement with the panel is not measured for this run.**");
            }
            sb.AppendLine();
        }
        else if (run.SecondOpinionGradedAnswerCount > 0)
        {
            var agreementMode = ModeOf(run);
            var graded = answers
                .Where(a => a.SecondOpinionQualityScore.HasValue && a.QualityScore.HasValue
                            && !string.Equals(a.SecondOpinionTrigger, "Manual", StringComparison.Ordinal)
                            && BenchmarkRunFinalizer.CountsTowardQualityIndex(a))
                .OrderBy(a => a.OrderIndex)
                .ToList();
            var disagreedAnswers = graded.Where(a => a.SecondOpinionDisagreed).ToList();
            int answeredForAgreement = answers.Count(BenchmarkRunFinalizer.CountsTowardQualityIndex);
            double? meanAbsDelta = run.SecondOpinionMeanAbsDelta.HasValue
                ? BenchmarkRunFinalizer.RoundAgreementDelta(run.SecondOpinionMeanAbsDelta.Value)
                : (graded.Count > 0
                    ? BenchmarkRunFinalizer.RoundAgreementDelta(graded.Average(a => Math.Abs(a.SecondOpinionQualityScore!.Value - a.QualityScore!.Value)))
                    : null);
            double? meanSignedDelta = run.SecondOpinionMeanSignedDelta.HasValue
                ? BenchmarkRunFinalizer.RoundAgreementDelta(run.SecondOpinionMeanSignedDelta.Value)
                : (graded.Count > 0
                    ? BenchmarkRunFinalizer.RoundAgreementDelta(graded.Average(a => (double)(a.SecondOpinionQualityScore!.Value - a.QualityScore!.Value)))
                    : null);
            var criticalErrorSplits = graded
                .Where(a => a.SecondOpinionCriticalError.HasValue && a.SecondOpinionCriticalError.Value != a.CriticalError)
                .ToList();

            sb.AppendLine("### Assessor Agreement");
            sb.AppendLine($"- **Mode:** {agreementMode}{ModeGloss(agreementMode)}");
            sb.AppendLine($"- **Coverage:** {run.SecondOpinionGradedAnswerCount} of {answeredForAgreement} answered questions.");
            sb.AppendLine($"- **Prompt Protocol:** {(run.SecondOpinionBlindUsed ? "Blind — the second reader received the candidate's answer without seeing the assessor's scores, comments, or critical error flag." : "Anchored — the second reader saw the assessor's verdict and comment.")}");
            if (run.SecondOpinionBlindUsed)
            {
                sb.AppendLine("  - *Note: Assessor agreement is reported for a **blind** second reader. Blind and anchored agreement figures are not comparable.*");
            }
            else
            {
                sb.AppendLine("  - *Note: Anchored second readers exhibit anchoring bias toward the first assessor's verdict and cannot be compared directly with blind second readers.*");
            }
            sb.AppendLine(meanAbsDelta.HasValue
                ? $"- **Mean absolute difference:** {Inv(meanAbsDelta.Value, "F1")} points."
                : "- **Mean absolute difference:** not recorded.");
            if (meanSignedDelta.HasValue)
            {
                string directionSuffix = string.Empty;
                var deltas = graded.Select(a => a.SecondOpinionQualityScore!.Value - a.QualityScore!.Value).ToList();
                if (deltas.Count >= 3 && deltas.All(d => d < 0))
                {
                    directionSuffix = " — the second reader graded **lower** on every re-graded answer. A one-directional gap of this size is a statement about the grader whose verdict scores, not about noise between two readers.";
                }
                else if (deltas.Count >= 3 && deltas.All(d => d > 0))
                {
                    directionSuffix = " — the second reader graded **higher** on every re-graded answer. A one-directional gap of this size is a statement about the grader whose verdict scores, not about noise between two readers.";
                }
                sb.AppendLine($"- **Mean signed difference:** {Inv(meanSignedDelta.Value, "F1")} points (over {deltas.Count} of {answeredForAgreement} answered){directionSuffix}");
            }
            else
            {
                sb.AppendLine("- **Mean signed difference:** not recorded.");
            }
            int splitCount = run.SecondOpinionCriticalErrorSplitCount > 0 ? run.SecondOpinionCriticalErrorSplitCount : criticalErrorSplits.Count;
            if (splitCount > 0 && graded.Count > 0)
            {
                string splitNamed = criticalErrorSplits.Count > 0
                    ? " — " + string.Join(", ", criticalErrorSplits.Select(a => $"Q{a.OrderIndex}"))
                    : string.Empty;
                sb.AppendLine($"- **Critical-error splits:** {splitCount} of {graded.Count}{splitNamed}. The two readers disagree on whether the answer contains a fabrication, which is the most severe disagreement this harness records.");
            }
            if (graded.Count > 0)
            {
                double disagreementPct = disagreedAnswers.Count * 100.0 / graded.Count;
                string named = disagreedAnswers.Count > 0
                    ? " — " + string.Join(", ", disagreedAnswers.Select(a => $"Q{a.OrderIndex}"))
                    : string.Empty;
                sb.AppendLine($"- **Disagreements:** {disagreedAnswers.Count} of {graded.Count} ({Inv(disagreementPct, "F1")}%){named}. *A disagreement is a gap above {BenchmarkService.SecondOpinionDisagreementPoints} quality points — roughly one BARS level on the dominant dimension — or a split on criticalError.*");
            }
            // H2. The pooled figures above are kept exactly as they were, so no historical number
            // changes meaning — but they mix two populations. A second opinion on an answer carrying a
            // refuted claim is handed that refutation in its own prompt (BenchmarkAssessmentPrompt's
            // FACT-CHECK VERIFICATION CONTEXT block), so its disagreement is partly the verifier's
            // finding rather than a second reader's independent judgement. The uninformed subset is the
            // only part of the coverage that measures agreement between two readers of the same evidence.
            if (graded.Count > 0)
            {
                string triggerBreakdown = string.Join(", ", graded
                    .GroupBy(a => string.IsNullOrWhiteSpace(a.SecondOpinionTrigger) ? "not recorded" : a.SecondOpinionTrigger!, StringComparer.Ordinal)
                    .OrderByDescending(g => g.Count())
                    .ThenBy(g => g.Key, StringComparer.Ordinal)
                    .Select(g => $"{TriggerLabel(g.Key)}: {g.Count()}"));
                sb.AppendLine($"- **Coverage by trigger:** {triggerBreakdown}.");

                static bool SawRefutation(BenchmarkRunAnswer a) =>
                    string.Equals(a.SecondOpinionTrigger, "RefutedClaim", StringComparison.Ordinal) ||
                    (((BenchmarkAnswerFlags)a.AnswerFlags) & BenchmarkAnswerFlags.RefutedClaim) != 0 ||
                    (a.ClaimsRefutedCount ?? 0) > 0;

                var uninformed = graded.Where(a => !SawRefutation(a)).ToList();
                int informedCount = graded.Count - uninformed.Count;
                if (informedCount > 0)
                {
                    if (uninformed.Count > 0)
                    {
                        double uninformedSigned = uninformed.Average(a => (double)(a.SecondOpinionQualityScore!.Value - a.QualityScore!.Value));
                        double uninformedAbs = uninformed.Average(a => Math.Abs(a.SecondOpinionQualityScore!.Value - a.QualityScore!.Value));
                        int uninformedDisagreements = uninformed.Count(a => a.SecondOpinionDisagreed);
                        sb.AppendLine(
                            $"- **Agreement over the verification-uninformed subset:** mean signed " +
                            $"{(uninformedSigned > 0 ? "+" : string.Empty)}{Inv(uninformedSigned, "F1")} points, " +
                            $"mean absolute {Inv(uninformedAbs, "F1")} points, {uninformedDisagreements} disagreement(s) " +
                            $"over {uninformed.Count} of {graded.Count} re-graded answers " +
                            $"({string.Join(", ", uninformed.Select(a => $"Q{a.OrderIndex}"))}). " +
                            $"The other {informedCount} saw a refuted claim in their own prompt, so their delta is not an independent second reading.");
                    }
                    else
                    {
                        sb.AppendLine($"- **Agreement over the verification-uninformed subset:** none — all {graded.Count} re-graded answers carried a refuted claim, so this run measures no independent second reading at all.");
                    }
                }
            }
            if (agreementMode != BenchmarkSecondOpinionMode.All)
            {
                sb.AppendLine($"- *Coverage: {run.SecondOpinionGradedAnswerCount} of {answeredForAgreement}, selected by trigger. The disagreement rate is conditioned on the first assessor's own uncertainty and is not an unbiased estimate of grader agreement; `SecondOpinionMode = All` measures that.*");
            }
            sb.AppendLine();
        }
        else if (run.SecondOpinionAssessorModelConfigurationId.HasValue)
        {
            var agreementMode = ModeOf(run);
            int answeredForAgreement = answers.Count(BenchmarkRunFinalizer.CountsTowardQualityIndex);
            string assessorName = run.SecondOpinionAssessorModelSnapshot.Label() ?? "configured assessor";

            sb.AppendLine("### Assessor Agreement");
            sb.AppendLine($"- **Mode:** {agreementMode}{ModeGloss(agreementMode)}");

            var secondOpinionFailedAnswers = answers
                .Where(a => !string.IsNullOrWhiteSpace(a.SecondOpinionError))
                .OrderBy(a => a.OrderIndex)
                .ToList();

            if (secondOpinionFailedAnswers.Count > 0)
            {
                string firstError = secondOpinionFailedAnswers[0].SecondOpinionError!.Trim();
                if (firstError.Length > 200)
                {
                    firstError = firstError.Substring(0, 197) + "...";
                }
                sb.AppendLine($"- **Coverage:** 0 of {answeredForAgreement} answered questions. **{secondOpinionFailedAnswers.Count} answer(s) met a trigger but the second-reader call failed, so grader agreement is not measured for this run.** First error: `{firstError}`.");
            }
            else
            {
                sb.AppendLine($"- **Coverage:** 0 of {answeredForAgreement} answered questions. **No answer met a trigger, so no answer was graded twice and grader agreement is not measured for this run.** The second reader ({assessorName}) made no calls and appears in the Harness Cost figures only as zero.");
            }

            var scoredOkAnswers = answers.Where(a => a.Status == BenchmarkAnswerStatus.Ok && IndexQualityOf(a).HasValue).OrderBy(a => IndexQualityOf(a)!.Value).ToList();
            if (scoredOkAnswers.Count > 0)
            {
                var lowest = scoredOkAnswers[0];
                double lowestScore = IndexQualityOf(lowest)!.Value;
                int threshold = scoringConstants.SecondOpinionQualityThreshold;
                if (threshold > 0)
                {
                    double margin = lowestScore - threshold;
                    if (margin >= 0)
                    {
                        sb.AppendLine($"- **Nearest miss:** lowest quality score {ScoreText(lowestScore)} (Q{lowest.OrderIndex}), {ScoreText(margin)} points above the profile's threshold of {threshold}.");
                    }
                    else
                    {
                        sb.AppendLine($"- **Nearest miss:** lowest quality score {ScoreText(lowestScore)} (Q{lowest.OrderIndex}), below threshold {threshold}.");
                    }
                }
                else
                {
                    sb.AppendLine($"- **Nearest miss:** lowest quality score {ScoreText(lowestScore)} (Q{lowest.OrderIndex}); no quality threshold configured.");
                }

                double median = Median(scoredOkAnswers.Select(a => IndexQualityOf(a)!.Value));
                int roundedMedian = (int)Math.Round(median, MidpointRounding.AwayFromZero);
                int delta = scoringConstants.SecondOpinionOutlierDeltaPoints;
                int outlierCandidateCount = scoredOkAnswers.Count(a => median - IndexQualityOf(a)!.Value > delta);

                string outlierReason = outlierCandidateCount == 0
                    ? $"no answer is more than {delta} points below this run's median of {roundedMedian}"
                    : $"{outlierCandidateCount} answer(s) are more than {delta} points below this run's median of {roundedMedian}";
                sb.AppendLine($"- `FlaggedAndOutliers` would have re-graded {outlierCandidateCount} further answer(s) — {outlierReason}. `All` would have graded all {answeredForAgreement} twice; it is the only mode that measures grader agreement rather than sampling it.");
            }
            sb.AppendLine();
        }

        // Member B's verdict record on each graded answer of a panel run; empty otherwise.
        var memberBRecords = isPanelRun
            ? scoredAnswers
                .Where(a => a.CoAssessmentStatus == BenchmarkAssessmentStatus.Scored)
                .Select(a => (Answer: a, Record: BenchmarkCoAssessmentRecord.Parse(a.CoAssessmentJson)))
                .Where(x => x.Record != null)
                .Select(x => (x.Answer, Record: x.Record!))
                .ToList()
            : new List<(BenchmarkRunAnswer Answer, BenchmarkCoAssessmentRecord Record)>();
        static string QuestionList(IEnumerable<BenchmarkRunAnswer> list)
        {
            var named = list.OrderBy(a => a.OrderIndex).Select(a => $"Q{a.OrderIndex}").ToList();
            return named.Count > 0 ? string.Join(", ", named) : "none";
        }

        // Response-style conflict inputs: the four dimension averages the note reads.
        double? styleAccuracy = null, styleCompleteness = null, styleConciseness = null, styleReadability = null;

        if (scoredAnswers.Count > 0)
        {
            sb.AppendLine("### Dimensional Score Averages");
            if (isPanelRun)
            {
                // One row per member and a panel row, the mean of the two members' averages. Member
                // B's points are its levels on this run's level table, as member A's are.
                var averages = BenchmarkPanelDimensions.Averages(scoredAnswers, scoringConstants.LevelScores)!;
                string[] dimensionNames = { "Accuracy (55%)", "Completeness (25%)", "Conciseness (10%)", "Readability (10%)" };
                static string Cell(BenchmarkDimensionAverage v) => $"{Inv(v.Points, "F1")} / 100 (level {Inv(v.Level, "F1")})";

                sb.AppendLine($"| Reader | {string.Join(" | ", dimensionNames)} |");
                sb.AppendLine($"|--------|{string.Concat(dimensionNames.Select(_ => "---|"))}");
                sb.AppendLine($"| Member A | {string.Join(" | ", averages.MemberA.Select(Cell))} |");
                if (averages.MemberB != null && averages.Panel != null)
                {
                    sb.AppendLine($"| Member B | {string.Join(" | ", averages.MemberB.Select(Cell))} |");
                    sb.AppendLine($"| Panel | {string.Join(" | ", averages.Panel.Select(Cell))} |");
                    styleAccuracy = averages.Panel[0].Points;
                    styleCompleteness = averages.Panel[1].Points;
                    styleConciseness = averages.Panel[2].Points;
                    styleReadability = averages.Panel[3].Points;
                }
                else
                {
                    sb.AppendLine($"| Member B | {string.Join(" | ", dimensionNames.Select(_ => "not recorded"))} |");
                }
                sb.AppendLine();
                sb.AppendLine($"*Over the {scoredAnswers.Count} answer(s) carrying a panel score; member B's row over the {averages.MemberBAnswerCount} of them whose record carries all four levels. The panel row is the mean of the two members' rows.*");
            }
            else
            {
                sb.AppendLine($"- **Accuracy (Weight 55%):** {Inv(scoredAnswers.Average(a => a.AccuracyScore ?? 0), "F1")} / 100 (Avg Level: {Inv(scoredAnswers.Average(a => a.AccuracyLevel ?? 0), "F1")} / 6)");
                sb.AppendLine($"- **Completeness (Weight 25%):** {Inv(scoredAnswers.Average(a => a.CompletenessScore ?? 0), "F1")} / 100 (Avg Level: {Inv(scoredAnswers.Average(a => a.CompletenessLevel ?? 0), "F1")} / 6)");
                sb.AppendLine($"- **Conciseness (Weight 10%):** {Inv(scoredAnswers.Average(a => a.ConcisenessScore ?? 0), "F1")} / 100 (Avg Level: {Inv(scoredAnswers.Average(a => a.ConcisenessLevel ?? 0), "F1")} / 6)");
                sb.AppendLine($"- **Readability (Weight 10%):** {Inv(scoredAnswers.Average(a => a.ReadabilityScore ?? 0), "F1")} / 100 (Avg Level: {Inv(scoredAnswers.Average(a => a.ReadabilityLevel ?? 0), "F1")} / 6)");
            }

            // The instrument's own share of the Accuracy→Completeness gap, as a measured figure.
            // Scoring method v8 tells the assessor that the question defines the scope and that a
            // rubric point the question did not ask for must be recorded rather than deducted for;
            // this counts what it recorded. Printed only when there is something to print — a zero
            // on a v8 run and a zero on a run graded before the marker existed look identical, and
            // asserting "none found" for a grader that was never asked would be the same mistake
            // NarrationBlockCount exists to avoid.
            var outOfScopeAnswers = scoredAnswers.Where(a => a.CompletenessOutOfScope)
                .OrderBy(a => a.OrderIndex)
                .ToList();
            // Member B's markers are read from its record and counted on the same terms.
            var outOfScopeB = memberBRecords.Where(x => x.Record.Flags?.CompletenessOutOfScope == true).ToList();
            var formOnlyB = memberBRecords.Where(x => x.Record.Flags?.ReadabilityFormOnly == true).ToList();
            if (isPanelRun && (outOfScopeAnswers.Count > 0 || outOfScopeB.Count > 0))
            {
                sb.AppendLine($"- **Out-of-scope completeness deductions:** A {outOfScopeAnswers.Count} ({QuestionList(outOfScopeAnswers)}), B {outOfScopeB.Count} ({QuestionList(outOfScopeB.Select(x => x.Answer))})");
                sb.AppendLine($"  - These are rubric points a panel member itself placed outside what the question asked, recorded under the `OUT-OF-SCOPE:` marker; the instruction is not to deduct for them. They are the instrument's share of the Accuracy→Completeness gap: the part of that gap the rubric caused rather than the answer.");
                var onlyA = outOfScopeAnswers
                    .Where(a => BenchmarkVerdictConsistency.IsOutOfScopeOnlyDeduction(a.CompletenessLevel ?? 0, ReadEvidence(a).Completeness))
                    .ToList();
                var onlyB = outOfScopeB
                    .Where(x => BenchmarkVerdictConsistency.IsOutOfScopeOnlyDeduction(x.Record.CompletenessLevel ?? 0, x.Record.CompletenessEvidence))
                    .Select(x => x.Answer)
                    .ToList();
                if (onlyA.Count > 0 || onlyB.Count > 0)
                {
                    sb.AppendLine($"  - **A {onlyA.Count}, B {onlyB.Count}** of them sit beside a Completeness level below 6 with no in-scope defect named (A: {QuestionList(onlyA)}; B: {QuestionList(onlyB)}) — the instruction was not followed there; those verdicts carry the `UnevidencedDeduction` flag.");
                }
            }
            else if (outOfScopeAnswers.Count > 0)
            {
                string questionList = string.Join(", ", outOfScopeAnswers.Select(a => $"Q{a.OrderIndex}"));
                sb.AppendLine($"- **Out-of-scope completeness deductions:** {outOfScopeAnswers.Count} ({questionList})");
                sb.AppendLine($"  - These are rubric points the assessor itself placed outside what the question asked, recorded under the `OUT-OF-SCOPE:` marker; the instruction is not to deduct for them. They are the instrument's share of the Accuracy→Completeness gap: the part of that gap the rubric caused rather than the answer.");

                // Whether the instruction was followed is a separate question from whether the
                // marker was written, and the marker count alone answers only the second. These are
                // the verdicts where the assessor recorded the point and docked the level anyway,
                // naming nothing in scope to justify it.
                var outOfScopeOnly = outOfScopeAnswers
                    .Where(a => BenchmarkVerdictConsistency.IsOutOfScopeOnlyDeduction(a.CompletenessLevel ?? 0, ReadEvidence(a).Completeness))
                    .ToList();
                if (outOfScopeOnly.Count > 0)
                {
                    string onlyList = string.Join(", ", outOfScopeOnly.Select(a => $"Q{a.OrderIndex}"));
                    sb.AppendLine($"  - **{outOfScopeOnly.Count}** of them sit beside a Completeness level below 6 with no in-scope defect named ({onlyList}) — the instruction was not followed there; those verdicts carry the `UnevidencedDeduction` flag.");
                }
            }

            // The Readability counterpart, printed on the same terms and suppressed at zero for the
            // same reason: a v9 run where the assessor found nothing to set aside and a run graded
            // before the marker existed are indistinguishable in this count.
            var formOnlyAnswers = scoredAnswers.Where(a => a.ReadabilityFormOnly)
                .OrderBy(a => a.OrderIndex)
                .ToList();
            if (isPanelRun && (formOnlyAnswers.Count > 0 || formOnlyB.Count > 0))
            {
                sb.AppendLine($"- **Rubric format suggestions not followed:** A {formOnlyAnswers.Count} ({QuestionList(formOnlyAnswers)}), B {formOnlyB.Count} ({QuestionList(formOnlyB.Select(x => x.Answer))})");
                sb.AppendLine($"  - These are rubric FORM criteria naming a presentation the answer did not adopt, recorded under the `FORM:` marker; the instruction is not to deduct for them. Readability is graded on its level anchors alone, so this is the rubric's share of the Readability shortfall rather than the answer's.");
                var deductedA = formOnlyAnswers
                    .Where(a => BenchmarkVerdictConsistency.IsFormOnlyDeduction(a.ReadabilityLevel ?? 0, ReadEvidence(a).Readability, a.ReadabilityFormOnly))
                    .ToList();
                var deductedB = formOnlyB
                    .Where(x => BenchmarkVerdictConsistency.IsFormOnlyDeduction(x.Record.ReadabilityLevel ?? 0, x.Record.ReadabilityEvidence, true))
                    .Select(x => x.Answer)
                    .ToList();
                if (deductedA.Count > 0 || deductedB.Count > 0)
                {
                    sb.AppendLine($"  - **A {deductedA.Count}, B {deductedB.Count}** of them sit beside a Readability level below 6 with no defect named (A: {QuestionList(deductedA)}; B: {QuestionList(deductedB)}) — the instruction was not followed there.");
                }
            }
            else if (formOnlyAnswers.Count > 0)
            {
                string questionList = string.Join(", ", formOnlyAnswers.Select(a => $"Q{a.OrderIndex}"));
                sb.AppendLine($"- **Rubric format suggestions not followed:** {formOnlyAnswers.Count} ({questionList})");
                sb.AppendLine($"  - These are rubric FORM criteria naming a presentation the answer did not adopt, recorded under the `FORM:` marker; the instruction is not to deduct for them. Readability is graded on its level anchors alone, so this is the rubric's share of the Readability shortfall rather than the answer's.");

                // The Readability counterpart of the out-of-scope split above, and counted on the
                // same terms — but not flagged: readability evidence is not a deduction basis, so a
                // level docked here is a measurement of the instrument, never a contested verdict.
                // The stored marker column is passed alongside the evidence string because runs
                // graded before the evidence blob carried a readability key have only the column.
                var formOnlyDeductions = formOnlyAnswers
                    .Where(a => BenchmarkVerdictConsistency.IsFormOnlyDeduction(
                        a.ReadabilityLevel ?? 0, ReadEvidence(a).Readability, a.ReadabilityFormOnly))
                    .ToList();
                if (formOnlyDeductions.Count > 0)
                {
                    string deductionList = string.Join(", ", formOnlyDeductions.Select(a => $"Q{a.OrderIndex}"));
                    sb.AppendLine($"  - **{formOnlyDeductions.Count}** of them sit beside a Readability level below 6 with no defect named ({deductionList}) — the instruction was not followed there.");
                }
            }

            // The Accuracy counterpart to both markers above, but deducted rather than set aside:
            // the assessor docked Accuracy from its own general knowledge rather than from the
            // rubric or the supplied corpus. That is exactly the failure mode a second, independent
            // reader exists to catch, so these route to one instead of standing on the first
            // assessor's word alone.
            var outOfRubricAccuracyAnswers = scoredAnswers
                .Where(a => (((BenchmarkAnswerFlags)a.AnswerFlags) & BenchmarkAnswerFlags.OutOfRubricAccuracyDeduction) != 0)
                .OrderBy(a => a.OrderIndex)
                .ToList();
            var outOfRubricAccuracyB = memberBRecords.Where(x => x.Record.Flags?.OutOfRubricAccuracy == true).Select(x => x.Answer).ToList();
            if (isPanelRun && (outOfRubricAccuracyAnswers.Count > 0 || outOfRubricAccuracyB.Count > 0))
            {
                sb.AppendLine($"- **Out-of-rubric Accuracy deductions:** A {run.OutOfRubricAccuracyAnswerCount} ({QuestionList(outOfRubricAccuracyAnswers)}), B {outOfRubricAccuracyB.Count} ({QuestionList(outOfRubricAccuracyB)})");
                sb.AppendLine($"  - These are {BenchmarkVerdictConsistency.OutOfRubricAccuracyDeductionDescription(run.ScoringMethodVersion)}");
            }
            else if (outOfRubricAccuracyAnswers.Count > 0)
            {
                string questionList = string.Join(", ", outOfRubricAccuracyAnswers.Select(a => $"Q{a.OrderIndex}"));
                sb.AppendLine($"- **Out-of-rubric Accuracy deductions:** {run.OutOfRubricAccuracyAnswerCount} ({questionList})");
                sb.AppendLine($"  - These are {BenchmarkVerdictConsistency.OutOfRubricAccuracyDeductionDescription(run.ScoringMethodVersion)}");
            }

            sb.AppendLine();
            // A panel run reads the conflict on the panel's averages, not on member A's alone.
            double gap = 0.0;
            bool styleConflict = isPanelRun
                ? styleAccuracy.HasValue && styleCompleteness.HasValue
                    && BenchmarkChatTransfer.HasResponseStyleConflict(
                        BenchmarkCandidatePromptOptions.FromJson(run.CandidatePromptOptionsJson).VerboseMode,
                        styleAccuracy, styleCompleteness, styleConciseness, styleReadability)
                : BenchmarkChatTransfer.HasResponseStyleConflict(run, scoredAnswers, out gap);
            if (isPanelRun && styleConflict)
            {
                gap = styleAccuracy!.Value - styleCompleteness!.Value;
            }
            if (styleConflict)
            {
                sb.AppendLine($"> **Response-style conflict.** This run graded a candidate instructed to *\"Default to 2–5 sentences per response\"* (`verboseMode: false`) against a Completeness dimension worth 25%, and Completeness is the weakest dimension by {Inv(gap, "F1")} points. Some of that gap may be the prompt rather than the model. Running the same suite at `verboseMode: true` separates the two; that run is not comparable with this one on Completeness, Conciseness or Readability.");
                sb.AppendLine();
            }
        }

        // Difficulty breakdown bucketed by AssessedDifficulty, using the shared band boundaries.
        // These used to be hardcoded here as 33/66 while the difficulty assessor was told
        // 35/70, so a question rated 35 *as Simple* was reported as Intermediate.
        int AssessedOf(BenchmarkRunAnswer a) =>
            a.AssessedDifficulty ?? BenchmarkRunFinalizer.FallbackDifficulty(a.Difficulty);

        var simpleAssessed = scoredAnswers.Where(a => BenchmarkDifficultyBands.IsSimple(AssessedOf(a))).ToList();
        var intermediateAssessed = scoredAnswers.Where(a => BenchmarkDifficultyBands.IsIntermediate(AssessedOf(a))).ToList();
        var advancedAssessed = scoredAnswers.Where(a => BenchmarkDifficultyBands.IsAdvanced(AssessedOf(a))).ToList();

        string BandLine(string name, BenchmarkDifficulty band, List<BenchmarkRunAnswer> bucket)
        {
            string range = BenchmarkDifficultyBands.RangeLabel(band);
            if (bucket.Count == 0) return $"- **{name} ({range}):** None";

            // A band average alone cannot separate "uniformly mediocre" from "one bad answer":
            // the 2026-09-03 run's Simple band averaged 88.2 out of 60, 95, 97, 97 and 92. The
            // spread and the weakest question number say which of the two it was.
            var weakestInBand = bucket.OrderBy(a => IndexQualityOf(a)!.Value).ThenBy(a => a.OrderIndex).First();
            string dispersion = bucket.Count > 1
                ? $", quality range {ScoreText(bucket.Min(a => IndexQualityOf(a)!.Value))}–{ScoreText(bucket.Max(a => IndexQualityOf(a)!.Value))}, lowest Q{weakestInBand.OrderIndex}"
                : string.Empty;
            return $"- **{name} ({range}):** {Inv(bucket.Average(a => IndexQualityOf(a)!.Value), "F1")} / 100 " +
                   $"({bucket.Count} answered, avg diff: {Inv(bucket.Average(a => (double)AssessedOf(a)), "F0")}{dispersion})";
        }

        sb.AppendLine("### Difficulty Breakdown");
        sb.AppendLine("Buckets by **assessed** difficulty; the Authored Band Distribution below buckets the same answers by their **authored** difficulty instead, which is why the two counts can differ.");
        sb.AppendLine(BandLine("Simple", BenchmarkDifficulty.Simple, simpleAssessed));
        sb.AppendLine(BandLine("Intermediate", BenchmarkDifficulty.Intermediate, intermediateAssessed));
        sb.AppendLine(BandLine("Advanced", BenchmarkDifficulty.Advanced, advancedAssessed));
        sb.AppendLine();

        int authoredSimple = answers.Count(a => a.Difficulty == BenchmarkDifficulty.Simple);
        int authoredIntermediate = answers.Count(a => a.Difficulty == BenchmarkDifficulty.Intermediate);
        int authoredAdvanced = answers.Count(a => a.Difficulty == BenchmarkDifficulty.Advanced);
        // The counts are over the answers this run stored, not over the suite as authored: a run that
        // stopped early has fewer of them, and the old label read as the suite's authored mix.
        sb.AppendLine($"- **Authored Band Distribution (of {answers.Count} answers, by authored difficulty):** {authoredSimple} Simple, {authoredIntermediate} Intermediate, {authoredAdvanced} Advanced");
        sb.AppendLine();

        // Band Agreement. Without this, a reader sees an authored distribution of 6/6/6 next to
        // an answered breakdown of 5/7/6 and has no way to tell which question moved or why.
        var bandDisagreements = answers
            .Where(a => a.AssessedDifficulty.HasValue &&
                        BenchmarkDifficultyBands.BandOf(a.AssessedDifficulty.Value) != a.Difficulty)
            .OrderBy(a => a.OrderIndex)
            .ToList();

        sb.AppendLine("### Band Agreement");
        sb.AppendLine("Assessed difficulty is a property of the suite item, not of this run — it is stamped once per question and stays byte-identical across every run of this suite until the item is re-assessed or edited, so this section describes the suite, not this run.");
        sb.AppendLine();
        if (bandDisagreements.Count == 0)
        {
            sb.AppendLine("All questions were assessed within their authored difficulty band.");
        }
        else
        {
            sb.AppendLine($"{bandDisagreements.Count} of {totalQuestions} question(s) were assessed outside their authored band. The Difficulty Breakdown above buckets by **assessed** difficulty, which is why its counts can differ from the authored distribution.");
            sb.AppendLine();

            // H5. Assessed difficulty is the Intelligence Index weight, so a drift that shares a
            // direction across every mismatch is not eighteen independent authoring slips — it moves the
            // headline, and a list of per-question band changes does not show it. Signed against the
            // authored band's own midpoint, the 25/55/85 map in BenchmarkRunFinalizer.FallbackDifficulty.
            int movedUp = bandDisagreements.Count(
                a => a.AssessedDifficulty!.Value > BenchmarkRunFinalizer.FallbackDifficulty(a.Difficulty));
            int movedDown = bandDisagreements.Count(
                a => a.AssessedDifficulty!.Value < BenchmarkRunFinalizer.FallbackDifficulty(a.Difficulty));
            double meanSignedDelta = bandDisagreements.Average(
                a => (double)(a.AssessedDifficulty!.Value - BenchmarkRunFinalizer.FallbackDifficulty(a.Difficulty)));
            string driftDirection = (movedUp > 0 && movedDown == 0) ? " **Every mismatch moved the same way — upward**, so the authored bands under-rate this suite systematically rather than in scattered cases."
                : (movedDown > 0 && movedUp == 0) ? " **Every mismatch moved the same way — downward**, so the authored bands over-rate this suite systematically rather than in scattered cases."
                : string.Empty;
            sb.AppendLine(
                $"- **Band Drift:** {movedUp} assessed harder than authored, {movedDown} easier; mean signed delta " +
                $"**{(meanSignedDelta > 0 ? "+" : string.Empty)}{Inv(meanSignedDelta, "F1")}** points against the authored band's reference difficulty (25 / 55 / 85). " +
                $"Assessed difficulty is the Intelligence Index weight, so this shifts the headline as well as the bucketing.{driftDirection}");
            sb.AppendLine();

            foreach (var a in bandDisagreements)
            {
                sb.AppendLine($"- **Question {a.OrderIndex}:** authored {a.Difficulty} → assessed {BenchmarkDifficultyBands.BandOf(a.AssessedDifficulty!.Value)} ({a.AssessedDifficulty.Value})");
            }
        }
        sb.AppendLine();

        if (simpleAssessed.Count > 0 && intermediateAssessed.Count > 0 && advancedAssessed.Count > 0)
        {
            double sAvg = simpleAssessed.Average(a => IndexQualityOf(a)!.Value);
            double iAvg = intermediateAssessed.Average(a => IndexQualityOf(a)!.Value);
            double aAvg = advancedAssessed.Average(a => IndexQualityOf(a)!.Value);
            if (sAvg < iAvg || iAvg < aAvg)
            {
                // When every critical-error cap landed in one band, that band's average is
                // depressed by the cap rather than by difficulty, and the generic explanation
                // is not the one that applies. On the 2026-09-03 run both caps fell on Simple
                // questions, which alone accounted for the inversion.
                var cappedAnswers = scoredAnswers.Where(a => a.CriticalError).ToList();
                string cappedBand = string.Empty;
                if (cappedAnswers.Count > 0)
                {
                    bool allSimple = cappedAnswers.All(a => BenchmarkDifficultyBands.IsSimple(AssessedOf(a)));
                    bool allIntermediate = cappedAnswers.All(a => BenchmarkDifficultyBands.IsIntermediate(AssessedOf(a)));
                    bool allAdvanced = cappedAnswers.All(a => BenchmarkDifficultyBands.IsAdvanced(AssessedOf(a)));
                    if (allSimple) cappedBand = "Simple";
                    else if (allIntermediate) cappedBand = "Intermediate";
                    else if (allAdvanced) cappedBand = "Advanced";
                }

                // Second branch: no cap explains it, so test whether the inversion rests on a
                // single answer. Removing the depressed band's weakest and re-checking the
                // ordering is the cheapest available discriminator between "this band is hard
                // for the model" and "one answer went wrong". On the 2026-09-03 run dropping Q1
                // lifts Simple from 88.2 to 95.3, above Intermediate's 90.0.
                string? SingleAnswerExplanation()
                {
                    var depressed = new List<(string Name, List<BenchmarkRunAnswer> Bucket)>();
                    if (sAvg < iAvg) depressed.Add(("Simple", simpleAssessed));
                    if (iAvg < aAvg) depressed.Add(("Intermediate", intermediateAssessed));

                    foreach (var (bandName, bucket) in depressed)
                    {
                        if (bucket.Count < 2) continue;

                        var weakest = bucket.OrderBy(x => IndexQualityOf(x)!.Value).ThenBy(x => x.OrderIndex).First();
                        double lifted = bucket.Where(x => !ReferenceEquals(x, weakest)).Average(x => IndexQualityOf(x)!.Value);

                        double s2 = ReferenceEquals(bucket, simpleAssessed) ? lifted : sAvg;
                        double i2 = ReferenceEquals(bucket, intermediateAssessed) ? lifted : iAvg;
                        if (s2 >= i2 && i2 >= aAvg)
                        {
                            return $"Removing the **{bandName}** band's single weakest answer (question {weakest.OrderIndex}, {ScoreText(IndexQualityOf(weakest)!.Value)} / 100) lifts that band to {Inv(lifted, "F1")} and restores the ordering — read the inversion as one outlier, not a difficulty effect.";
                        }
                    }

                    return null;
                }

                string monotonicityCause = cappedBand.Length > 0
                    ? $"All {cappedAnswers.Count} critical-error cap(s) on this run fell in the **{cappedBand}** band (question(s) {string.Join(", ", cappedAnswers.OrderBy(a => a.OrderIndex).Select(a => a.OrderIndex.ToString()))}), which is what depressed that band's average — read the inversion as a critical-error effect, not a difficulty effect."
                    : SingleAnswerExplanation()
                      ?? "This is common on small question sets or when the model has specific domain strengths.";

                sb.AppendLine($"*Note:* Average quality does not decrease monotonically with assessed difficulty on this run (Simple: {Inv(sAvg, "F1")}, Intermediate: {Inv(iAvg, "F1")}, Advanced: {Inv(aAvg, "F1")}). {monotonicityCause}");
                sb.AppendLine();
            }
        }

        // Tool Usage Profile. The old report said a cap had been hit but never which tools
        // consumed the budget, leaving the operator no basis for tuning it: Q11 of the
        // 2026-09-03 run spent all 25 calls on wiki search churn and nothing said so.
        var toolCounts = BenchmarkChatTransfer.AggregateToolCounts(answers);

        // Whether this run carries the per-call tool record at all. Those rows exist only from
        // harness 17 onward and no backfill is possible, so every line derived from them is gated
        // on this: on a legacy run the report prints nothing rather than a zero, because "0 failed"
        // and "failures were never recorded" are opposite claims and a reader cannot tell them
        // apart once the figure is on the page.
        bool hasToolCallRows = answers.Any(a => a.ToolCalls.Count > 0);

        sb.AppendLine("### Tool Usage Profile");
        int totalToolCalls = answers.Sum(a => a.ToolCallCount ?? 0);
        sb.AppendLine($"- **Total Tool Calls:** {totalToolCalls}");
        if (answers.Count > 0)
        {
            sb.AppendLine($"- **Mean Calls per Question:** {Inv(totalToolCalls / (double)answers.Count, "F1")}");
        }

        if (hasToolCallRows)
        {
            var (succeededCalls, failedCalls, refusedCalls) =
                BenchmarkToolCallRecorder.Outcomes(answers.SelectMany(a => a.ToolCalls));
            sb.AppendLine($"- **Tool Call Outcomes:** {succeededCalls} succeeded, {failedCalls} failed, {refusedCalls} refused by budget. " +
                "*A failed call is one that ran and did not complete — most commonly a JSON result over the result cap, which `ToolExecutor` converts into an error telling the model to narrow its query. It appears in no tool-name count in the table below, which is why the outcome split is stated separately from the profile: a model that repeatedly over-fetched leaves the profile looking sparse rather than looking wasteful. A refused call ran no tool code at all — its budget was already spent when it was emitted.*");

            // What the successful calls returned, read from the stored rows by
            // BenchmarkToolResultClassifier. A payload the retention sweep nulled is neither a hit
            // nor a miss, so it is counted apart rather than in either figure.
            var toolResults = BenchmarkToolResultClassifier.Summarize(answers.SelectMany(a => a.ToolCalls));
            static string ToolTally(IReadOnlyList<KeyValuePair<string, int>> byTool) =>
                string.Join(", ", byTool.Select(kv => $"`{kv.Key}` ×{Inv(kv.Value)}"));
            string notFoundTools = toolResults.NotFoundByTool.Count > 0
                ? $" ({ToolTally(toolResults.NotFoundByTool)})"
                : string.Empty;
            string failedNotFound = toolResults.FailedNotFound > 0
                ? $"; plus {toolResults.FailedNotFound} failed call(s) whose error is a not-found ({ToolTally(toolResults.FailedNotFoundByTool)})"
                : string.Empty;
            sb.AppendLine($"- **Not-found results:** {toolResults.NotFound} of {toolResults.InspectableSuccessful} inspectable successful payloads{notFoundTools}{failedNotFound}; {toolResults.Unavailable} payloads unavailable.");
            if (toolResults.SectionMiss > 0)
            {
                string sectionMissTools = toolResults.SectionMissByTool.Count > 0
                    ? $" ({ToolTally(toolResults.SectionMissByTool)})"
                    : string.Empty;
                sb.AppendLine($"- **Section not found:** {toolResults.SectionMiss}{sectionMissTools} — *the article exists but has no section by the requested name; the result lists its headings.*");
            }
            // AgentLoopRunner stores a result before the batch budget and the turn limit cut it, so
            // those two cuts never reach a stored row: they are not recorded, which is not zero.
            sb.AppendLine($"- **Cut before the model saw it:** {toolResults.PerToolCap} by the per-tool cap; batch budget and turn limit not recorded. **Cut in the stored record only:** {toolResults.RecordCut}. " +
                "*A result is stored before the batch budget and the turn limit apply, so a cut by either is invisible in the stored rows.*");

            // Over the answers that actually called tools, not over every question: a question that
            // called none has no rounds, and averaging its absence in would report the run as more
            // batched than it was.
            var withRounds = answers
                .Where(a => ToolRoundCount(a) > 0)
                .OrderByDescending(ToolRoundCount)
                .ThenBy(a => a.OrderIndex)
                .ToList();
            if (withRounds.Count > 0)
            {
                double meanRounds = withRounds.Average(a => (double)ToolRoundCount(a));
                int roundTotal = withRounds.Sum(ToolRoundCount);
                int callTotal = withRounds.Sum(a => a.ToolCalls.Count);
                double meanCallsPerRound = roundTotal > 0 ? callTotal / (double)roundTotal : 0.0;
                var maxByRounds = withRounds[0];
                sb.AppendLine($"- **Tool Rounds:** mean {Inv(meanRounds, "F1")} per answered question with tool calls; " +
                    $"mean calls per round {Inv(meanCallsPerRound, "F1")}; " +
                    $"max {Inv(ToolRoundCount(maxByRounds))} on Q{maxByRounds.OrderIndex}.");
            }
        }

        // Budget pressure. A question that stopped one call short of its budget is not
        // "exhausted" and is not flagged anywhere, yet it may have been cut off mid-
        // investigation — an outcome indistinguishable from a model choosing to stop. On the
        // 2026-09-03 run Q7 spent 34 of 35 and Q2 23 of 25, and nothing said so.
        var pressured = answers
            .Where(a => !a.ToolBudgetExhausted && (a.ToolCallsBlocked ?? 0) == 0
                        && a.ToolCallBudgetUsed.HasValue && a.ToolCallBudgetUsed.Value > 0
                        && a.ToolCallCount.HasValue
                        && a.ToolCallCount.Value >= a.ToolCallBudgetUsed.Value * BudgetPressureFraction
                        && a.ToolCallCount.Value < a.ToolCallBudgetUsed.Value)
            .OrderBy(a => a.OrderIndex)
            .ToList();

        var saturated = answers
            .Where(a => !a.ToolBudgetExhausted && (a.ToolCallsBlocked ?? 0) == 0
                        && a.ToolCallBudgetUsed.HasValue && a.ToolCallBudgetUsed.Value > 0
                        && a.ToolCallCount.HasValue
                        && a.ToolCallCount.Value >= a.ToolCallBudgetUsed.Value)
            .OrderBy(a => a.OrderIndex)
            .ToList();

        // Whether the cap cost anything is a different question from whether it was reached, and
        // the report used to answer only the second. On the 2026-09-03 run Q10 stopped one call
        // short of its budget and scored 60 — the worst answer in its band — and Q11 exhausted
        // its budget and scored 84, both against a run mean of 92. Marking the ones that scored
        // below the run's own mean is what turns "a cap was reached" into a testable hypothesis.
        string BelowMeanMarker(BenchmarkRunAnswer a)
        {
            if (!unweightedMean.HasValue || IndexQualityOf(a) is not double published) return string.Empty;
            return published < unweightedMean.Value
                ? $" **— scored {ScoreText(published)}, below the run mean of {unweightedMean.Value}**"
                : string.Empty;
        }

        if (pressured.Count > 0)
        {
            sb.AppendLine($"- **Budget Pressure:** {pressured.Count} question(s) used at least {Inv(BudgetPressureFraction * 100, "F0")}% of the tool call budget without exhausting it — " +
                string.Join(", ", pressured.Select(a =>
                    $"Q{a.OrderIndex} {a.ToolCallCount!.Value}/{a.ToolCallBudgetUsed!.Value} ({a.ToolCallBudgetUsed.Value - a.ToolCallCount.Value} left){BelowMeanMarker(a)}")) +
                ". *An answer this close to its cap may have stopped investigating because of the cap rather than because it was finished.*");
        }

        if (saturated.Count > 0)
        {
            sb.AppendLine($"- **Budget Saturated:** {saturated.Count} question(s) reached the configured tool call budget without any calls blocked — " +
                string.Join(", ", saturated.Select(a =>
                    $"Q{a.OrderIndex} {a.ToolCallCount!.Value}/{a.ToolCallBudgetUsed!.Value}{BelowMeanMarker(a)}")) +
                ". *These questions used every allocated tool call but were not cut off mid-call; they may benefit from a larger budget.*");
        }

        var exhausted = answers
            .Where(a => a.ToolBudgetExhausted || (a.ToolCallsBlocked ?? 0) > 0)
            .OrderBy(a => a.OrderIndex)
            .ToList();

        if (exhausted.Count > 0)
        {
            sb.AppendLine($"- **Budget Exhausted:** {exhausted.Count} question(s) exhausted their tool call budget — " +
                string.Join(", ", exhausted.Select(a =>
                {
                    var (_, blocked) = ToolCallSplit(a);
                    string blockedClause = blocked > 0
                        ? $" ({blocked} calls refused by budget)"
                        : string.Empty;
                    return $"Q{a.OrderIndex} {a.ToolCallCount?.ToString() ?? "N/A"}/{a.ToolCallBudgetUsed?.ToString() ?? "N/A"}{blockedClause}{BelowMeanMarker(a)}";
                })) +
                ". *Further tool calls were refused; these questions were cut off mid-investigation.*");
        }

        var budgetConstrainedBelowMean = answers
            .Where(a => a.ToolBudgetExhausted || (a.ToolCallsBlocked ?? 0) > 0 || saturated.Contains(a) || pressured.Contains(a))
            .Where(a => unweightedMean.HasValue && IndexQualityOf(a) is double published && published < unweightedMean.Value)
            .OrderBy(a => a.OrderIndex)
            .ToList();
        if (budgetConstrainedBelowMean.Count > 0)
        {
            sb.AppendLine($"- **Budget/Quality Correlation:** {budgetConstrainedBelowMean.Count} budget-constrained question(s) scored below the run's unweighted mean of {unweightedMean!.Value} — " +
                string.Join(", ", budgetConstrainedBelowMean.Select(a =>
                    $"Q{a.OrderIndex} ({ScoreText(IndexQualityOf(a)!.Value)}, {(a.ToolBudgetExhausted || (a.ToolCallsBlocked ?? 0) > 0 ? "budget exhausted" : saturated.Contains(a) ? "budget saturated" : "budget pressured")})")) +
                ". *The cap is a candidate explanation, not a demonstrated one — raise `Benchmark:ToolCallBudget` and re-run to test it.*");
        }

        // Grounding. An Advanced question answered from memory is not necessarily wrong, but it
        // is no longer testing source retrieval, which is what the Advanced band exists for.
        // Q14 and Q17 of the 2026-09-03 run each executed a single tool call at assessed 78 and
        // 79. This is a signal about the suite, not about the model.
        var ungroundedAdvanced = answers
            .Where(a => BenchmarkDifficultyBands.IsAdvanced(AssessedOf(a)) && (a.ToolCallCount ?? 0) <= 1)
            .OrderBy(a => a.OrderIndex)
            .ToList();
        if (ungroundedAdvanced.Count > 0)
        {
            // On a snapshot suite the board often settles the question, so answering from it is
            // the expected behaviour rather than a sign the item stopped testing anything.
            string groundingNote = RunHasBoard(run)
                ? $"- **Grounding:** {ungroundedAdvanced.Count} Advanced-band question(s) answered from the board with one tool call or fewer — " +
                  string.Join(", ", ungroundedAdvanced.Select(a => $"Q{a.OrderIndex} ({a.ToolCallCount ?? 0})")) +
                  ". *Expected on a snapshot suite when the board settles the question.*"
                : $"- **Grounding:** {ungroundedAdvanced.Count} Advanced-band question(s) answered with one tool call or fewer — " +
                  string.Join(", ", ungroundedAdvanced.Select(a => $"Q{a.OrderIndex} ({a.ToolCallCount ?? 0})")) +
                  ". *Worth reviewing as suite maintenance: these may no longer test source retrieval.*";
            sb.AppendLine(groundingNote);
        }

        if (toolCounts.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("| Tool | Successful Calls |");
            sb.AppendLine("|------|-----------------:|");
            foreach (var kv in toolCounts.OrderByDescending(k => k.Value).ThenBy(k => k.Key, StringComparer.Ordinal))
            {
                sb.AppendLine($"| `{kv.Key}` | {kv.Value} |");
            }
        }

        var routing = BenchmarkChatTransfer.AnalyzeToolRouting(answers, isPanelRun);
        if (routing.TotalCalls > 0)
        {
            sb.AppendLine();
            sb.AppendLine("#### Tool Routing");
            sb.AppendLine("Distribution of tool calls across functional tool families:");
            sb.AppendLine();
            sb.AppendLine("| Tool Family | Calls | Share |");
            sb.AppendLine("|-------------|------:|------:|");
            foreach (var fs in routing.RunWideStats.Where(s => s.CallCount > 0))
            {
                string famName = fs.Family switch
                {
                    BenchmarkToolFamily.SourceCode => "Source Code",
                    BenchmarkToolFamily.Wiki => "Wiki",
                    BenchmarkToolFamily.StructuredLookup => "Structured Lookup",
                    BenchmarkToolFamily.KnowledgeBase => "Knowledge Base",
                    _ => "Other"
                };
                sb.AppendLine($"| {famName} | {fs.CallCount} | {Inv(fs.SharePercentage, "F1")}% |");
            }

            // BenchmarkChatTransfer.AnalyzeToolRouting computes these over the same gradeable
            // population every other run-wide figure here uses, so they are read from it rather
            // than recomputed.
            int routingAnsweredCount = routing.AnsweredQuestionCount;
            int routingZeroKbCount = routing.ZeroKnowledgeBaseAnswerCount;
            if (BenchmarkChatTransfer.HasKnowledgeBaseRoutingQuestion(answers))
            {
                sb.AppendLine($"- **Knowledge base under-use:** {routingZeroKbCount} of {routingAnsweredCount} answered question(s) made zero `get_knowledge_article` calls.");
            }
            else
            {
                sb.AppendLine($"- *Prompt observation:* {routingZeroKbCount} of {routingAnsweredCount} answered question(s) made zero `get_knowledge_article` calls. Per `Overseer/Services/ChatService.cs` § \"Information Routing\" and `Overseer/ToolGuides/get_knowledge_article.md`, the knowledge base is scoped to app navigation, settings, troubleshooting and platform documentation; for game mechanics, monsters, items, spells, or other topics not listed there, the prompt instructs the model to skip the knowledge base entirely.");
            }

            if (routing.CorrelationSampleSize >= 2)
            {
                string rTimeStr = routing.SourceShareModelTimeCorrelation.HasValue
                    ? Inv(routing.SourceShareModelTimeCorrelation.Value, "F2") : "N/A";
                string rQualStr = routing.SourceShareQualityScoreCorrelation.HasValue
                    ? Inv(routing.SourceShareQualityScoreCorrelation.Value, "F2") : "N/A";
                sb.AppendLine($"- **Source-family correlations (n = {routing.CorrelationSampleSize}):** r = {rTimeStr} with model time; r = {rQualStr} with quality score.");
            }
            if (routing.SourceFamilySharePercentage > 50.0)
            {
                // Do not quote the tool policy here. This line previously carried a hardcoded
                // paraphrase introduced as something "the production chat system prompt states",
                // and the prompt had never contained it — the policy is conditional, not a blanket
                // wiki-over-source preference. A hardcoded copy of a file that is edited
                // independently drifts silently, and a report that misquotes the prompt produces
                // findings about a rule that does not exist. Point at the policy file instead.
                sb.AppendLine($"- *Prompt observation:* Source code tools accounted for {Inv(routing.SourceFamilySharePercentage, "F1")}% of all tool calls. The tool preference hierarchy the candidate was given is in `Overseer/ToolGuides/_policy.md`: it routes strategy and general \"what is X\" questions to wiki tools first, routes specific mechanics questions (exact AC, damage dice, MR, resistances, speed, material, artifact flags) to the structured stats tools, and places source code tools at rung 4 for questions that require reading game logic. {routing.AdvancedQuestionCount} question(s) were assessed in the Advanced band, where source-level inspection is expected. Read this share against the suite's question mix and against the policy text itself — it is an observation for operator review, not a rule violation.");
            }
            sb.AppendLine();

            // Both branches must survive. The caveat is exactly true of a run without rows, and a
            // run without rows is every run before harness 17 — deleting the sentence would make
            // this report assert of every historical run that its tool ordering can be read, which
            // it cannot, by anyone, ever.
            if (hasToolCallRows)
            {
                sb.AppendLine("*Note on tool ordering:* the family counts above are aggregates, but this run also records each call on its own, so ordering **is** derivable here: the per-question tables in section 3 list every attempted call in emission order with the tool round it belonged to. Whether wiki tools were attempted before source code tools on a given question is read off that table directly, not inferred from these totals.");
            }
            else
            {
                sb.AppendLine("*Note on tool ordering:* `ToolCallSummary` records aggregate call counts, not an ordered execution log (ordering is not recorded). Whether wiki tools were attempted before source code tools on any given question is not derivable from this data.");
            }
        }

        var starved = answers.Where(a => a.ToolBudgetExhausted || (a.ToolCallsBlocked ?? 0) > 0).OrderBy(a => a.OrderIndex).ToList();
        if (starved.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("**Questions that exhausted their tool call budget:**");
            foreach (var a in starved)
            {
                sb.AppendLine($"- **Question {a.OrderIndex}** ({a.Difficulty}, assessed {AssessedOf(a)}): {FormatToolBudgetLine(a)}{BelowMeanMarker(a)}");
            }
        }
        sb.AppendLine();

        // 4. Questions and Replies
        sb.AppendLine("## 3. Questions and Replies");
        sb.AppendLine();

        foreach (var a in answers)
        {
            // Label with the same fallback the band buckets use, so an unassessed question is
            // never labelled 50 while being bucketed somewhere else.
            int shownDifficulty = AssessedOf(a);
            string assessedBandNote = a.AssessedDifficulty.HasValue &&
                                      BenchmarkDifficultyBands.BandOf(a.AssessedDifficulty.Value) != a.Difficulty
                ? $" → {BenchmarkDifficultyBands.BandOf(a.AssessedDifficulty.Value)}"
                : string.Empty;

            string itemRevisionText = a.ItemRevisionUsed?.ToString(CultureInfo.InvariantCulture) ?? "?";
            sb.AppendLine($"### Question {a.OrderIndex} [Authored: {a.Difficulty} | Assessed Diff: {shownDifficulty}{assessedBandNote} | Item rev {itemRevisionText}]");
            sb.AppendLine($"**Question:** {a.QuestionText}");
            sb.AppendLine();
            sb.AppendLine($"- **Status:** {a.Status}" + (a.HttpStatusCode.HasValue ? $" (HTTP {a.HttpStatusCode.Value})" : ""));
            if (a.RerunAtUtc.HasValue)
            {
                sb.AppendLine($"- **Re-executed:** {Stamp(a.RerunAtUtc.Value)} UTC; {ReplacedAttemptText(a)}");
            }
            string timingSuffix = a.ToolTimeMs.HasValue
                ? $", model {a.ModelTimeMs} ms, tools {a.ToolTimeMs.Value} ms"
                : string.Empty;
            sb.AppendLine($"- **Duration:** {a.DurationMs} ms (TTFT: {(a.TimeToFirstTokenMs.HasValue ? $"{a.TimeToFirstTokenMs.Value} ms" : "N/A")}{timingSuffix})");
            string reasoningSuffix = a.ReasoningTokens.HasValue && !IsAnthropicCandidate(run) ? $", Reasoning={a.ReasoningTokens.Value}" : string.Empty;
            sb.AppendLine($"- **Tokens:** In={a.InputTokens ?? 0}, Out={a.OutputTokens ?? 0}, CacheRead={a.CacheReadInputTokens ?? 0}{reasoningSuffix}");
            if (!string.IsNullOrWhiteSpace(a.ActualServiceTierUsed))
            {
                sb.AppendLine($"- **Served Service Tier:** {a.ActualServiceTierUsed}");
            }
            if (!string.IsNullOrWhiteSpace(a.ToolCallSummary))
            {
                sb.AppendLine($"- **Tools Called:** `{a.ToolCallSummary}`");
            }
            // H6. Only when the loop did not end on its own: "completed" is the ordinary case and saying
            // so on every question would bury the four answers where it matters.
            if (!string.IsNullOrWhiteSpace(a.TerminationReason) &&
                !string.Equals(a.TerminationReason, "completed", StringComparison.OrdinalIgnoreCase))
            {
                sb.AppendLine($"- **Termination:** `{a.TerminationReason}` — the loop stopped before the model did");
            }
            else
            {
                string? nearCeilingNote = NearCeilingNote(run, a);
                if (nearCeilingNote != null)
                {
                    sb.AppendLine($"- **Near a Ceiling:** {nearCeilingNote} — finished within one step of a configured cap");
                }
            }
            if (a.ToolBudgetExhausted)
            {
                sb.AppendLine($"- **Tool Budget:** Exhausted — {FormatToolBudgetLine(a)} (configured limit, not an error)");
            }
            else if (a.ToolCallBudgetUsed.HasValue)
            {
                sb.AppendLine($"- **Tool Budget:** {FormatToolBudgetLine(a)}");
            }
            if (a.AnswerFlags != 0)
            {
                var flags = (BenchmarkAnswerFlags)a.AnswerFlags;
                sb.AppendLine($"- **Integrity Flags:** {flags}");
            }
            var memberBFlags = isPanelRun ? CoAssessmentAnswerFlags(a) : BenchmarkAnswerFlags.None;
            if (memberBFlags != BenchmarkAnswerFlags.None)
            {
                sb.AppendLine($"- **Integrity Flags (member B):** {memberBFlags}");
            }
            if (a.ScrubbedArtifactCount > 0)
            {
                sb.AppendLine($"- **Transport Artifacts Removed:** {a.ScrubbedArtifactCount} block(s) removed before grading");
            }

            // The ordered record of this turn: every call the model attempted, successes and
            // failures alike, in emission order rather than in the aggregate shape of
            // `Tools Called` above. An answer with no rows is one from a run before harness 17,
            // where no such record exists — nothing is printed, because a blank table would read
            // as a turn that called no tools.
            //
            // The Args column carries a bounded, single-line, 80-character preview of each call's
            // arguments so a reader can tell one call from another at a glance. Full payloads stay
            // out of this table — a single tool result can be tens of thousands of characters of
            // game source — and live behind the per-answer admin endpoint and the tool-call log
            // export instead. The result column carries ResultLengthChars — the true size the tool
            // produced, before any cap — so an over-large or truncated payload is visible as a
            // number without a character of it being quoted here. The Note column carries
            // BenchmarkToolResultClassifier's fixed vocabulary, which needs no escaping.
            if (a.ToolCalls.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("| Round | Tool | Args | Status | Exec (ms) | Result Size | Note |");
                sb.AppendLine("|------:|------|------|--------|----------:|------------:|------|");
                foreach (var call in a.ToolCalls.OrderBy(c => c.SortOrder))
                {
                    string round = call.IterationIndex.HasValue ? Inv(call.IterationIndex.Value) : "N/A";
                    string toolName = string.IsNullOrWhiteSpace(call.Name) ? "*(unnamed)*" : $"`{call.Name}`";
                    string args = FormatArgsPreview(call.ArgsText, call.ResultLengthChars);
                    string status = string.IsNullOrWhiteSpace(call.Status) ? "*(none recorded)*" : call.Status;
                    string execMs = call.ExecutionMs.HasValue ? Inv(call.ExecutionMs.Value, "N0") : "N/A";
                    string resultSize = call.ResultLengthChars > 0
                        ? $"{Inv(call.ResultLengthChars, "N0")} chars"
                        : "—";
                    string callNote = BenchmarkToolResultClassifier.Note(call);
                    sb.AppendLine($"| {round} | {toolName} | {args} | {status} | {execMs} | {resultSize} | {callNote} |");
                }
            }

            sb.AppendLine();

            if (a.Status == BenchmarkAnswerStatus.ProviderError)
            {
                sb.AppendLine($"**Provider Error:** {a.ErrorMessage}");
            }
            else if (a.Status == BenchmarkAnswerStatus.Canceled)
            {
                sb.AppendLine($"**Canceled:** {a.ErrorMessage}");
            }
            else if (a.Status == BenchmarkAnswerStatus.EmptyAnswer)
            {
                // A finish reason that means a normal stop is the model failing to answer; anything else,
                // a null included, is a transport defect and keeps the wording it always had.
                sb.AppendLine(BenchmarkRunFinalizer.IsModelProducedEmptyAnswer(a)
                    ? $"**Reply:** *(No answer — the model ended its turn without producing text; provider finish reason: `{a.ProviderFinishReason}`)*"
                    : "**Reply:** *(Empty answer produced)*");
            }
            else
            {
                sb.AppendLine("**Reply:**");
                sb.AppendLine();
                // Question headings are "###"; demote anything shallower so a model's own "##"
                // or "###" heading never lands at or above the report's own outline level.
                sb.AppendLine(DemoteAnswerHeadings(a.AnswerText, minLevel: 4));
            }
            sb.AppendLine();

            if (a.QualityScore.HasValue || a.AccuracyLevel.HasValue || !string.IsNullOrWhiteSpace(a.ReviewComment) ||
                (a.AssessedByModelConfigurationId.HasValue && a.AssessedByModelConfigurationId != run.AssessorModelConfigurationId) ||
                (isPanelRun && a.CoAssessmentStatus.HasValue))
            {
                sb.AppendLine("> **Evaluation:**");
                if (isPanelRun)
                {
                    sb.AppendLine($"> - **Panel Member A ({a.AssessedByModelSnapshot.Label() ?? memberALabel}):** the levels, score and evidence below are member A's verdict.");
                }
                if (a.AccuracyLevel.HasValue)
                {
                    sb.AppendLine($"> - **Levels (0–6):** Accuracy={a.AccuracyLevel}/6 ({a.AccuracyScore} pts), Completeness={a.CompletenessLevel}/6 ({a.CompletenessScore} pts), Conciseness={a.ConcisenessLevel}/6 ({a.ConcisenessScore} pts), Readability={a.ReadabilityLevel}/6 ({a.ReadabilityScore} pts)");
                }

                if (a.QualityScore.HasValue)
                {
                    string rawPart = (a.RawQualityScore.HasValue && a.RawQualityScore.Value != a.QualityScore.Value)
                        ? $" (raw: {a.RawQualityScore.Value})"
                        : string.Empty;
                    // Same guard as cappedCount above: the flag alone does not mean the cap
                    // changed anything — an answer whose raw score already sat at or below the
                    // cap is unaffected by it, and the report must not say otherwise.
                    bool capLoweredScore = a.RawQualityScore.HasValue && a.RawQualityScore.Value > a.QualityScore.Value;
                    string capNote = !a.CriticalError
                        ? string.Empty
                        : resolvesCriticalErrors
                        ? ResolvedCapNote(a.CriticalErrorResolution, capLoweredScore)
                        : (capLoweredScore ? " *(CRITICAL ERROR CAP APPLIED)*" : " *(CRITICAL ERROR — cap not binding)*");
                    if (resolvesCriticalErrors
                        && a.AccuracyLevel.HasValue && a.CompletenessLevel.HasValue && a.ConcisenessLevel.HasValue && a.ReadabilityLevel.HasValue
                        && NotAttemptedFloorApplied(a.NotAttempted == true, a.CriticalError,
                            a.AccuracyLevel.Value, a.CompletenessLevel.Value, a.ConcisenessLevel.Value, a.ReadabilityLevel.Value,
                            a.RawQualityScore ?? a.QualityScore, scoringConstants))
                    {
                        capNote += $" *(NOT ATTEMPTED — floor of {scoringConstants.NotAttemptedScore} applied)*";
                    }
                    if (BenchmarkRunFinalizer.IsModelProducedEmptyAnswer(a))
                    {
                        sb.AppendLine($"> - **Quality Score:** {a.QualityScore.Value} / 100 *(NO ANSWER — scored 0 by rule; no grader read this)*");
                    }
                    else
                    {
                        sb.AppendLine($"> - **Quality Score:** {a.QualityScore.Value} / 100{rawPart}{capNote}");
                    }
                }
                else
                {
                    sb.AppendLine("> - **Quality Score:** N/A");
                }

                // Model time and the effective target, not the raw turn duration: those are the
                // two numbers the score is actually computed from, so printing DurationMs here
                // left a reader unable to check the arithmetic — and misleading by exactly the
                // tool time on a tool-heavy question.
                // BenchmarkRunFinalizer.Apply nulls SpeedScore on every answer that fails
                // CountsTowardQualityIndex, so a failed or provider-error answer prints no Speed
                // Score line at all here rather than a misleading N/A beside a terminal failure.
                if (BenchmarkRunFinalizer.CountsTowardQualityIndex(a) && a.SpeedScore.HasValue)
                {
                    double speedTarget = BenchmarkScoring.EffectiveSpeedTargetMs(
                        a.AssessedDifficulty ?? BenchmarkRunFinalizer.FallbackDifficulty(a.Difficulty),
                        scoringConstants);
                    sb.AppendLine($"> - **Speed Score:** {a.SpeedScore.Value} / 100 (model {a.ModelTimeMs} ms vs target {Inv(Math.Round(speedTarget), "N0")} ms)");
                }
                else if (BenchmarkRunFinalizer.CountsTowardQualityIndex(a))
                {
                    sb.AppendLine("> - **Speed Score:** N/A");
                }
                if (!string.IsNullOrWhiteSpace(a.ReviewComment))
                {
                    sb.AppendLine($"> - **Assessor Comment:** {a.ReviewComment}");
                }

                // What the deductions rest on. A score argued from the authored rubric and one
                // argued from the grader's own recall are different claims, and only the record
                // can tell them apart afterwards.
                var (accuracyEvidence, completenessEvidence, _, criticalErrorDemoted) = ReadEvidence(a);
                if (!string.IsNullOrWhiteSpace(accuracyEvidence))
                {
                    sb.AppendLine($"> - **Accuracy Evidence:** {accuracyEvidence}");
                }
                if (!string.IsNullOrWhiteSpace(completenessEvidence))
                {
                    sb.AppendLine($"> - **Completeness Evidence:** {completenessEvidence}");
                }
                if (a.CriticalError && !string.IsNullOrWhiteSpace(a.CriticalErrorQuote))
                {
                    sb.AppendLine($"> - **Critical Error Quote:** \"{a.CriticalErrorQuote}\"");
                }
                // The claims themselves, not just the count: the run-level Assessor Findings
                // block says how many there were, and this is the only place a reader can see
                // what they were and judge whether the rubric should have covered them.
                var unverifiedClaims = ReadUnverifiedClaims(a);
                if (unverifiedClaims.Count > 0)
                {
                    sb.AppendLine($"> - **Unverified Claims ({unverifiedClaims.Count}):** " +
                        string.Join("; ", unverifiedClaims.Select(c => $"\"{c}\"")) +
                        " — *neither confirmed nor refuted against the rubric; not an Accuracy deduction from harness version 7.*");
                }
                if (criticalErrorDemoted)
                {
                    sb.AppendLine("> - **Critical Error:** claimed by the assessor but not applied — the quoted claim could not be found in the graded answer, and an omission is not a critical error.");
                }
                if ((((BenchmarkAnswerFlags)a.AnswerFlags) & BenchmarkAnswerFlags.UnevidencedDeduction) != 0)
                {
                    var flaggedDims = new List<string>();
                    if (a.AccuracyLevel.HasValue && a.AccuracyLevel.Value <= BenchmarkVerdictConsistency.UnevidencedDeductionMaxLevel)
                    {
                        if (BenchmarkVerdictConsistency.IsNoFaultEvidence(accuracyEvidence))
                        {
                            string evDisplay = !string.IsNullOrWhiteSpace(accuracyEvidence) ? $"\"{accuracyEvidence.Trim()}\"" : "(none)";
                            flaggedDims.Add($"Accuracy to {a.AccuracyLevel.Value}/6 while its stated evidence ({evDisplay}) names no defect");
                        }
                        else if (BenchmarkVerdictConsistency.IsUnverifiabilityGroundedDeduction(a.AccuracyLevel.Value, accuracyEvidence, a.UnverifiedClaimCount ?? unverifiedClaims.Count))
                        {
                            flaggedDims.Add($"Accuracy to {a.AccuracyLevel.Value}/6 while its stated evidence rests only on claims it could not verify, which the scoring method does not permit as an accuracy deduction");
                        }
                        else if (BenchmarkVerdictConsistency.IsPrecisionGroundedAccuracyDeduction(a.AccuracyLevel.Value, accuracyEvidence))
                        {
                            flaggedDims.Add($"Accuracy to {a.AccuracyLevel.Value}/6 while its stated evidence withholds the level for missing precision, nuance or depth rather than naming a statement that is wrong or imprecise");
                        }
                    }
                    if (a.CompletenessLevel.HasValue && a.CompletenessLevel.Value <= BenchmarkVerdictConsistency.UnevidencedDeductionMaxLevel && BenchmarkVerdictConsistency.IsNoFaultEvidence(completenessEvidence))
                    {
                        string evDisplay = !string.IsNullOrWhiteSpace(completenessEvidence) ? $"\"{completenessEvidence.Trim()}\"" : "(none)";
                        flaggedDims.Add($"Completeness to {a.CompletenessLevel.Value}/6 while its stated evidence ({evDisplay}) names no defect");
                    }

                    if (flaggedDims.Count > 0)
                    {
                        sb.AppendLine($"> - **Harness note:** the assessor docked {string.Join(" and ", flaggedDims)}. Advisory: the verdict stands and this did not change the score.");
                    }
                    else
                    {
                        sb.AppendLine("> - **Harness note:** the assessor docked a level while its stated evidence names no defect, rests only on unverifiability, or withholds Accuracy for missing precision. Advisory: the verdict stands and this did not change the score.");
                    }
                }
                if (isPanelRun)
                {
                    // Member B's verdict, read from its stored JSON, then the mean both scored.
                    string memberBAnswerLabel = a.CoAssessedByModelSnapshot.Label() ?? memberBLabel;
                    var memberB = BenchmarkVerdictView.FromCoAssessment(a);
                    if (memberB != null)
                    {
                        string memberBScore = memberB.QualityScore.HasValue ? $"{memberB.QualityScore.Value} / 100" : "N/A";
                        string memberBRaw = a.CoAssessmentRawQualityScore.HasValue && memberB.QualityScore.HasValue && a.CoAssessmentRawQualityScore.Value != memberB.QualityScore.Value
                            ? $" (raw: {a.CoAssessmentRawQualityScore.Value})"
                            : string.Empty;
                        string memberBFloor = resolvesCriticalErrors
                            && NotAttemptedFloorApplied(a.CoAssessmentNotAttempted == true, memberB.CriticalError,
                                memberB.AccuracyLevel, memberB.CompletenessLevel, memberB.ConcisenessLevel, memberB.ReadabilityLevel,
                                a.CoAssessmentRawQualityScore ?? memberB.QualityScore, scoringConstants)
                            ? $" *(NOT ATTEMPTED — floor of {scoringConstants.NotAttemptedScore} applied)*"
                            : string.Empty;
                        sb.AppendLine($"> - **Panel Member B ({memberBAnswerLabel}):** Accuracy={memberB.AccuracyLevel}/6, Completeness={memberB.CompletenessLevel}/6, Conciseness={memberB.ConcisenessLevel}/6, Readability={memberB.ReadabilityLevel}/6 — {memberBScore}{memberBRaw}, critical error {(memberB.CriticalError ? "yes" : "no")}{memberBFloor}");
                        if (!string.IsNullOrWhiteSpace(memberB.Comment))
                        {
                            sb.AppendLine($">   - **Comment:** {memberB.Comment}");
                        }
                        if (!string.IsNullOrWhiteSpace(memberB.AccuracyEvidence))
                        {
                            sb.AppendLine($">   - **Accuracy Evidence:** {memberB.AccuracyEvidence}");
                        }
                        if (!string.IsNullOrWhiteSpace(memberB.CompletenessEvidence))
                        {
                            sb.AppendLine($">   - **Completeness Evidence:** {memberB.CompletenessEvidence}");
                        }
                        if (memberB.CriticalError && !string.IsNullOrWhiteSpace(memberB.CriticalErrorQuote))
                        {
                            sb.AppendLine($">   - **Critical Error Quote:** \"{memberB.CriticalErrorQuote}\"");
                        }
                        if (memberB.UnverifiedClaims.Count > 0)
                        {
                            sb.AppendLine($">   - **Unverified Claims ({memberB.UnverifiedClaims.Count}):** " +
                                string.Join("; ", memberB.UnverifiedClaims.Select(c => $"\"{c}\"")));
                        }
                    }
                    else
                    {
                        string memberBError = string.IsNullOrWhiteSpace(a.CoAssessmentError)
                            ? string.Empty
                            : $" — {BenchmarkAssessmentFailure.Truncate(a.CoAssessmentError, 200)}";
                        sb.AppendLine($"> - **Panel Member B ({memberBAnswerLabel}):** not scored ({a.CoAssessmentStatus?.ToString() ?? "not attempted"}){memberBError}");
                    }

                    if (a.PanelQualityScore.HasValue)
                    {
                        string membersDisagree = a.PanelDisagreed == true ? " — members disagree" : string.Empty;
                        string resolutionNote = resolvesCriticalErrors ? PanelResolutionNote(a, scoringConstants.CriticalErrorCeiling) : string.Empty;
                        sb.AppendLine($"> - **Panel Score:** {ScoreText(a.PanelQualityScore.Value)} (mean of A and B){resolutionNote}{membersDisagree}");
                    }
                    else if (!BenchmarkRunFinalizer.IsModelProducedEmptyAnswer(a))
                    {
                        sb.AppendLine("> - **Panel Score:** not computed — both members' verdicts are needed; excluded from the Intelligence Index until retried.");
                    }
                }
                if (isPanelRun && a.SecondOpinionQualityScore.HasValue)
                {
                    string readerCritical = a.SecondOpinionCriticalError == true ? "yes" : "no";
                    string readerAgreement = a.PanelQualityScore.HasValue
                        ? (ReaderDisagreesWithPanel(a) ? "**disagrees** with the panel score" : "agrees with the panel score")
                        : "no panel score to compare against";
                    sb.AppendLine($"> - **Reference Reader ({a.SecondOpinionByModelSnapshot.Label()}):** {a.SecondOpinionQualityScore.Value} / 100, critical error {readerCritical} — {readerAgreement}. Advisory.");
                }
                else if (a.SecondOpinionQualityScore.HasValue)
                {
                    string agreement = a.SecondOpinionDisagreed ? "**disagrees**" : "agrees";
                    string secondCritical = a.SecondOpinionCriticalError == true ? "yes" : "no";
                    // Why this answer was graded twice. "Every answer" and "this one looked
                    // wrong" are different facts about the same second verdict, and only the
                    // trigger separates them.
                    string triggerPart = string.IsNullOrWhiteSpace(a.SecondOpinionTrigger)
                        ? string.Empty
                        : $" (trigger: {TriggerLabel(a.SecondOpinionTrigger)})";
                    sb.AppendLine($"> - **Second Reader ({a.SecondOpinionByModelSnapshot.Label()}):** {a.SecondOpinionQualityScore.Value} / 100, critical error {secondCritical} — {agreement} with the first verdict{triggerPart}. Advisory; the first verdict is what scored.");
                }
                if (!string.IsNullOrWhiteSpace(a.ClaimVerificationError))
                {
                    string verifierName = a.ClaimVerificationByModelSnapshot.Label() ?? run.ClaimVerifierModelSnapshot.Label() ?? "claim verifier";
                    string err = BenchmarkAssessmentFailure.Truncate(a.ClaimVerificationError, 200) ?? a.ClaimVerificationError;
                    sb.AppendLine($"> - **Claim Verification ({verifierName}):** failed — {err}. The unverified claims above were not checked{ClaimVerificationSpendText(a)}.{ClaimVerificationRawTextHead(a)}");
                }
                if (!string.IsNullOrWhiteSpace(a.ClaimVerificationJson) || a.ClaimsSupportedCount.HasValue || a.ClaimsRefutedCount.HasValue || a.ClaimsIndeterminateCount.HasValue)
                {
                    string verifierName = a.ClaimVerificationByModelSnapshot.Label() ?? run.ClaimVerifierModelSnapshot.Label() ?? "claim verifier";
                    int sCount = a.ClaimsSupportedCount ?? 0;
                    int rCount = a.ClaimsRefutedCount ?? 0;
                    int iCount = a.ClaimsIndeterminateCount ?? 0;
                    sb.AppendLine($"> - **Claim Verification ({verifierName}):** {sCount} supported, {rCount} refuted, {iCount} indeterminate — *checked against source/wiki; advisory, not reflected in the score.*{ClaimVerificationSpendText(a)}");
                }
                // Every accused sentence submitted, apart from the answer's own claims counted above.
                var accusedSentences = AccusedSentencesOf(a);
                if (accusedSentences.Count > 0)
                {
                    var (accusedSupported, accusedRefuted, accusedIndeterminate) = VerdictCounts(accusedSentences);
                    string accusedSplit = isPanelRun ? $" ({MemberSplitText(accusedSentences, membersOf: v => v.AccusingMembers)})" : string.Empty;
                    sb.AppendLine($"> - **Accused sentences checked:** {accusedSentences.Count}{accusedSplit} — supported {accusedSupported}, refuted {accusedRefuted}, indeterminate {accusedIndeterminate}");
                }
                var supportedAccusationsOfAnswer = BenchmarkService.SupportedAccusations(accusedSentences);
                foreach (var accusation in supportedAccusationsOfAnswer)
                {
                    sb.AppendLine($"> - **Supported accusation:** a sentence {AccusedByText(accusation)} charged as false was checked by the claim verifier and **supported** — \"{accusation.Claim}\"{QuotedFragmentsText(accusation)} ({accusation.Citation}). *Advisory; the deduction stands.*");
                }
                foreach (var accusation in accusedSentences.Where(v => !supportedAccusationsOfAnswer.Contains(v)))
                {
                    string verdictWord = accusation.EffectiveVerdict.ToString().ToLowerInvariant();
                    string citation = string.IsNullOrWhiteSpace(accusation.Citation) ? "no citation" : accusation.Citation;
                    sb.AppendLine($"> - **Accused sentence, {verdictWord}:** a sentence {AccusedByText(accusation)} charged as false was checked by the claim verifier and returned **{VerdictText(accusation)}** — \"{accusation.Claim}\"{QuotedFragmentsText(accusation)} ({citation}).");
                }
                // The assessor's own evidence sentences: a refutation clears the answer, not convicts it.
                var assessorStatements = AssessorStatementsOf(a);
                if (assessorStatements.Count > 0)
                {
                    var (statementSupported, statementRefuted, statementIndeterminate) = VerdictCounts(assessorStatements);
                    sb.AppendLine($"> - **Assessor statements checked:** {assessorStatements.Count} — supported {statementSupported}, refuted {statementRefuted}, indeterminate {statementIndeterminate}");
                    foreach (var statement in assessorStatements.Where(v => v.EffectiveVerdict == BenchmarkClaimVerdict.Refuted))
                    {
                        string citation = string.IsNullOrWhiteSpace(statement.Citation) ? "no citation" : statement.Citation;
                        string wrongParty = MemberPhrase(statement) is string statementMember ? char.ToUpperInvariant(statementMember[0]) + statementMember[1..] : "The assessor";
                        sb.AppendLine($"> - **Assessor statement refuted:** a statement of {ChargedByPossessive(statement)} own evidence was checked by the claim verifier and **refuted** — \"{statement.Claim}\" ({citation}). *{wrongParty}, not the answer, {(MemberPhrase(statement) == "both members" ? "were" : "was")} wrong on this point; advisory, the deduction stands.*");
                    }
                }
                var suspectedFalse = SuspectedFalseClaimsOf(a);
                if (suspectedFalse.Count > 0)
                {
                    var (suspectedSupported, suspectedRefuted, suspectedIndeterminate) = VerdictCounts(suspectedFalse);
                    sb.AppendLine(isPanelRun
                        ? $"> - **Suspected false by the panel:** {suspectedFalse.Count} ({MemberSplitText(suspectedFalse, membersOf: v => v.SuspectingMembers)}) — refuted {suspectedRefuted} (the verifier sided with the member), supported {suspectedSupported} (the verifier sided with the answer), indeterminate {suspectedIndeterminate}"
                        : $"> - **Suspected false by the assessor:** {suspectedFalse.Count} — refuted {suspectedRefuted} (the verifier sided with the assessor), supported {suspectedSupported} (the verifier sided with the answer), indeterminate {suspectedIndeterminate}");
                }
                // A verdict the harness demoted: the verifier cited a function nothing calls.
                foreach (var noted in (ClaimVerificationsOf(a) ?? new List<BenchmarkClaimVerification>())
                             .Where(v => !string.IsNullOrWhiteSpace(v.CitationNote)))
                {
                    sb.AppendLine($"> - **Citation note:** \"{noted.Claim}\" — the verifier returned {noted.Verdict.ToString().ToLowerInvariant()} citing {noted.Citation}, but {noted.CitationNote}; the harness reads it as indeterminate.");
                }
                if (a.EvidenceInformedQualityScore.HasValue)
                {
                    sb.AppendLine($"> - {EvidenceInformedRegradeLine(run, a)}");
                }
                if (a.AssessedByModelConfigurationId.HasValue &&
                    a.AssessedByModelConfigurationId != run.AssessorModelConfigurationId)
                {
                    sb.AppendLine($"> - **Assessed by:** {a.AssessedByModelSnapshot.Label()} ({a.AssessedByModelSnapshot?.Provider}, {a.AssessedByModelSnapshot?.ModelId}) — differs from this run's assessor");
                }
                // A published index can move after publication. Where it did, the report says so
                // on the answer that moved, with the score it replaced.
                if (a.ReassessmentCount > 0)
                {
                    string previous = a.PreviousQualityScore.HasValue
                        ? $"{a.PreviousQualityScore.Value} / 100"
                        : "not recorded";
                    sb.AppendLine($"> - **Re-assessed:** {a.ReassessmentCount} time(s), most recently {(a.ReassessedAtUtc.HasValue ? Stamp(a.ReassessedAtUtc.Value) + " UTC" : "at an unrecorded time")} by {a.ReassessedByModelSnapshot.Label() ?? "an unrecorded model"} — the first verdict scored {previous} and this one replaced it.");
                }
                sb.AppendLine();
            }
        }

        // 5. Scoring Method & Configuration
        sb.AppendLine("## 4. Scoring Method & Configuration");
        sb.AppendLine();
        sb.AppendLine($"- **Scoring Method Version:** {run.ScoringMethodVersion}");
        sb.AppendLine($"- **Scoring Profile:** {run.ScoringProfile?.Name ?? "Default Intelligence Profile"}");
        // H4. "No (independently assessed)" read as a measurement of this run, and it is not one:
        // BenchmarkRunLauncher refuses to launch a suite carrying any question without an assessed
        // difficulty, so a run that exists cannot have fallen back. The line states the guarantee and
        // names the guard rather than implying a check the report performed.
        sb.AppendLine(run.DifficultyFallbackUsed
            ? "- **Difficulty Fallback Applied:** Yes (authored bands Simple=25, Intermediate=55, Advanced=85)"
            : "- **Difficulty Fallback Applied:** No — every question carried an independently assessed difficulty. Guaranteed rather than measured: `BenchmarkRunLauncher` refuses to launch a suite with any unassessed question.");
        sb.AppendLine();
        sb.AppendLine("### Compliance & Evaluation Terms");
        sb.AppendLine($"- **Purpose Statement:** {run.PurposeStatementUsed ?? "Internal evaluation of candidate AI models for the Overseer assistant within GnollHack. Benchmark outputs are third-party generated content used solely for automated capability evaluation and scoring, and are not used for training, fine-tuning, distilling, or developing competing AI models."}");
        // A panel run names every grading role, so the compliance list covers each model that read the output.
        string evaluatedBy = isPanelRun
            ? $"**{run.AssessorModelSnapshot.Label()}** ({run.AssessorModelSnapshot.Provider}) and **{run.CoAssessorModelSnapshot.Label()}** ({run.CoAssessorModelSnapshot?.Provider})"
                + (run.SecondOpinionAssessorModelConfigurationId.HasValue
                    ? $"; reference reader **{run.SecondOpinionAssessorModelSnapshot.Label()}** ({run.SecondOpinionAssessorModelSnapshot?.Provider})"
                    : string.Empty)
                + (run.ClaimVerifierModelConfigurationId.HasValue
                    ? $"; claim verifier **{run.ClaimVerifierModelSnapshot.Label()}** ({run.ClaimVerifierModelSnapshot?.Provider})"
                    : string.Empty)
            : $"**{run.AssessorModelSnapshot.Label()}** ({run.AssessorModelSnapshot.Provider})";
        sb.AppendLine($"- **Third-Party Model Content:** Outputs generated by **{run.TestedModelSnapshot.Label()}** ({run.TestedModelSnapshot.Provider}) and evaluated by {evaluatedBy} are third-party content evaluated solely for domain-specific benchmark scoring and operational model selection.");
        sb.AppendLine("- **Distillation / Training Prohibition:** No prompt, completion, or evaluation output in this benchmark is used for model training, fine-tuning, distillation, or developing competing AI models.");
        bool isSameProvider = string.Equals(run.TestedModelSnapshot.Provider, run.AssessorModelSnapshot.Provider, StringComparison.OrdinalIgnoreCase);
        if (isPanelRun)
        {
            // Replaces the single-assessor notice: both members' relation to the candidate, and which
            // roles scored.
            sb.AppendLine($"- **Panel Disclosure:** the published score is the mean of panel member A, {memberALabel} ({run.AssessorModelSnapshot.Provider}), which is **{memberARelation}** to the candidate, and panel member B, {memberBLabel} ({run.CoAssessorModelSnapshot?.Provider}), which is **{memberBRelation}** to it ({run.TestedModelSnapshot.Label()}, {run.TestedModelSnapshot.Provider}). Only the two members score; the reference reader and the claim verifier are advisory.");
        }
        else if (isSameProvider)
        {
            sb.AppendLine($"- **Same-Provider Evaluation Notice:** Both candidate model ({run.TestedModelSnapshot.Label()}) and assessor model ({run.AssessorModelSnapshot.Label()}) belong to the same provider ({run.TestedModelSnapshot.Provider}). Same-provider evaluation acknowledged: **{(run.SameProviderAcknowledged ? "Yes" : "No")}**.");
        }
        sb.AppendLine();
        sb.AppendLine("### Aggregation Formulas");
        if (resolvesCriticalErrors)
        {
            string floorRule = scoringConstants.NotAttemptedScore is int notAttemptedFloor
                ? $"raised to at least {notAttemptedFloor} when the grader marks the answer not attempted, raises no critical error and gives Accuracy 5 or above; "
                : "no not-attempted floor in this profile; ";
            string panelRule = isPanelRun
                ? $" In the panel score a critical error both members flag stands; one only one member flags is settled by the claim verifier's verdict on that member's quote: refuted upholds it and the other member counts at most {scoringConstants.CriticalErrorCeiling}, supported overturns it and the flagging member counts at its pre-cap score, and otherwise both members are averaged as graded."
                : string.Empty;
            sb.AppendLine($"- **Quality Score:** $Quality = A^{{0.55}} \\cdot C^{{0.25}} \\cdot Cn^{{0.10}} \\cdot R^{{0.10}}$ ({floorRule}then capped at {scoringConstants.CriticalErrorCeiling} if criticalError is true).{panelRule}");
        }
        else
        {
            sb.AppendLine("- **Quality Score:** $Quality = A^{0.55} \\cdot C^{0.25} \\cdot Cn^{0.10} \\cdot R^{0.10}$ (capped at 25 if criticalError is true)");
        }
        sb.AppendLine("- **Model Time:** $ModelTime = \\max(0, \\text{DurationMs} - \\text{ToolTimeMs})$ — the turn duration with harness tool I/O removed");
        sb.AppendLine("- **Speed Target:** $Target(q) = T \\cdot (1 + s \\cdot \\text{Difficulty}(q) / 100)$, where $T$ is SpeedTargetMs and $s$ is SpeedDifficultyScaling");
        sb.AppendLine("- **Speed Score:** $Speed = \\text{clamp}(100 - k \\cdot \\log_2(\\text{ModelTime} / Target(q)), 1, 100)$, where $k$ is SpeedDecayK");
        sb.AppendLine("- **Intelligence Index:** $\\Sigma(\\text{Difficulty}(q) \\cdot \\text{Quality}(q)) / \\Sigma(\\text{Difficulty}(q))$ over answered questions and unanswered questions alike, the latter at 0 — an unanswered question is a failed question. Quality only: the Speed Index is reported separately by design and is not folded in.");
        sb.AppendLine("- **Speed Index:** equal-weight mean of $Speed(q)$ over answered questions only, since an answer that does not exist has no latency. Difficulty enters through $Target(q)$, not through the weight; weighting here as well would count difficulty twice and pull the index toward the floor.");
        sb.AppendLine();
        sb.AppendLine($"> **Comparing Speed Indices:** thinking level dominates model time, so a Speed Index is comparable between runs at the same thinking level and misleading across levels. This run used thinking level **{run.TestedModelSnapshot.ThinkingLevel ?? "Default"}**.");
        sb.AppendLine();
        if (run.SpeedMeasurementDegraded)
        {
            sb.AppendLine("> ⚠️ **Concurrency Timing Notice:** This run was executed with concurrency (MaxParallelQuestions > 1). Model turn durations include resource queueing against simultaneous requests. Speed Index is advisory.");
            sb.AppendLine();
        }

        // 6. Issues
        sb.AppendLine("## 5. Issues");
        sb.AppendLine();
        var issueAnswers = answers.Where(a =>
            a.Status is BenchmarkAnswerStatus.ProviderError or BenchmarkAnswerStatus.Failed
                or BenchmarkAnswerStatus.Canceled or BenchmarkAnswerStatus.EmptyAnswer
            || a.ToolBudgetExhausted
            || a.AnswerFlags != 0
            || (isPanelRun && CoAssessmentAnswerFlags(a) != BenchmarkAnswerFlags.None)).ToList();

        if (issueAnswers.Count == 0 && string.IsNullOrEmpty(run.ErrorMessage))
        {
            sb.AppendLine("None. All questions completed cleanly without provider outages, degradation, or unexpected failures.");
        }
        else
        {
            if (!string.IsNullOrEmpty(run.ErrorMessage))
            {
                sb.AppendLine($"- **Run Level Error:** {run.ErrorMessage}");
            }
            foreach (var ia in issueAnswers)
            {
                var iaFlags = (BenchmarkAnswerFlags)ia.AnswerFlags;
                bool unanswered = BenchmarkRunFinalizer.IsModelProducedEmptyAnswer(ia);
                var flagDescriptions = new List<string>();
                if (ia.Status == BenchmarkAnswerStatus.EmptyAnswer)
                {
                    flagDescriptions.Add(unanswered
                        ? $"No answer — the model ended its turn without producing text (provider finish reason: {ia.ProviderFinishReason})"
                        : "Empty answer");
                }
                string httpSuffix = ia.HttpStatusCode.HasValue ? $" (HTTP {ia.HttpStatusCode.Value})" : string.Empty;
                if (ia.Status == BenchmarkAnswerStatus.ProviderError) flagDescriptions.Add($"Provider error{httpSuffix}: {ia.ErrorMessage}");
                if (ia.Status == BenchmarkAnswerStatus.Failed) flagDescriptions.Add($"Failed{httpSuffix}: {ia.ErrorMessage}");
                if (ia.Status == BenchmarkAnswerStatus.Canceled) flagDescriptions.Add($"Canceled: {ia.ErrorMessage}");
                if (iaFlags.HasFlag(BenchmarkAnswerFlags.HarnessArtifacts))
                {
                    flagDescriptions.Add($"Transport artifacts removed before grading ({ia.ScrubbedArtifactCount} block(s)) — recovered, and graded normally; a provider-path defect, not a damaged answer");
                }
                if (iaFlags.HasFlag(BenchmarkAnswerFlags.Truncated)) flagDescriptions.Add("Answer truncated (output token limit)");
                if (iaFlags.HasFlag(BenchmarkAnswerFlags.ReasoningBleed))
                {
                    // Same signal as the run-level advisory sentence: NarrationBlockCount where
                    // the run recorded it, and the weaker ScrubbedArtifactText proxy for runs
                    // that predate it. A run before harness version 6 cannot distinguish
                    // "removed" from "detected and left in place", and says so rather than
                    // asserting the flattering reading.
                    if (!ia.NarrationBlockCount.HasValue)
                    {
                        flagDescriptions.Add(!string.IsNullOrWhiteSpace(ia.ScrubbedArtifactText)
                            ? "Reasoning narration detected; removal not recorded for this run (advisory)"
                            : "Reasoning narration present in the graded answer (advisory)");
                    }
                    else
                    {
                        flagDescriptions.Add(ia.NarrationBlockCount.Value > 0
                            ? $"Reasoning narration removed before grading — {ia.NarrationBlockCount.Value} block(s) (advisory)"
                            : "Reasoning narration present in the graded answer (advisory)");
                    }
                }
                if (iaFlags.HasFlag(BenchmarkAnswerFlags.RepeatedFragments)) flagDescriptions.Add("Repeated reasoning fragments in the removed narration (advisory)");
                if (iaFlags.HasFlag(BenchmarkAnswerFlags.ContestedVerdict)) flagDescriptions.Add("Contested verdict (advisory, changed no score)");
                if (iaFlags.HasFlag(BenchmarkAnswerFlags.UnevidencedDeduction)) flagDescriptions.Add("Unevidenced deduction (advisory, changed no score)");
                if (iaFlags.HasFlag(BenchmarkAnswerFlags.RefutedClaim)) flagDescriptions.Add("Refuted claim (advisory, changed no score)");
                if (iaFlags.HasFlag(BenchmarkAnswerFlags.ContestedCriticalError)) flagDescriptions.Add("Contested critical error (advisory, changed no score)");
                if (iaFlags.HasFlag(BenchmarkAnswerFlags.ContestedAccuracyDeduction))
                {
                    flagDescriptions.Add($"Contested accuracy deduction (advisory, changed no score) ({string.Join(" and ", ContestedDeductionCauses(ia, isPanelRun ? BenchmarkPanelMember.A : (BenchmarkPanelMember?)null))})");
                }
                if (iaFlags.HasFlag(BenchmarkAnswerFlags.RubricContradictedBySource)) flagDescriptions.Add($"{RubricContradictedLabel}: a sentence docked against the rubric was supported with a citation (advisory, changed no score)");
                if (iaFlags.HasFlag(BenchmarkAnswerFlags.OmissionAsAccuracy)) flagDescriptions.Add("Omission docked as accuracy (advisory, changed no score)");
                if (iaFlags.HasFlag(BenchmarkAnswerFlags.DimensionOutlier)) flagDescriptions.Add("Dimension outlier: one level ≤ 1 beside three at ≥ 3, no defect of that kind named (advisory, changed no score)");

                // Member B's flags, from its co-assessment record, each named for the member.
                var iaFlagsB = isPanelRun ? CoAssessmentAnswerFlags(ia) : BenchmarkAnswerFlags.None;
                if (iaFlagsB.HasFlag(BenchmarkAnswerFlags.ContestedVerdict)) flagDescriptions.Add("Member B: contested verdict (advisory, changed no score)");
                if (iaFlagsB.HasFlag(BenchmarkAnswerFlags.UnevidencedDeduction)) flagDescriptions.Add("Member B: unevidenced deduction (advisory, changed no score)");
                if (iaFlagsB.HasFlag(BenchmarkAnswerFlags.ContestedCriticalError)) flagDescriptions.Add("Member B: contested critical error (advisory, changed no score)");
                if (iaFlagsB.HasFlag(BenchmarkAnswerFlags.ContestedAccuracyDeduction))
                {
                    flagDescriptions.Add($"Member B: contested accuracy deduction (advisory, changed no score) ({string.Join(" and ", ContestedDeductionCauses(ia, BenchmarkPanelMember.B).Select(c => CauseText(c, BenchmarkPanelMember.B)))})");
                }
                if (iaFlagsB.HasFlag(BenchmarkAnswerFlags.RubricContradictedBySource)) flagDescriptions.Add($"Member B: {RubricContradictedLabel.ToLowerInvariant()}: a sentence docked against the rubric was supported with a citation (advisory, changed no score)");
                if (iaFlagsB.HasFlag(BenchmarkAnswerFlags.OmissionAsAccuracy)) flagDescriptions.Add("Member B: omission docked as accuracy (advisory, changed no score)");
                if (iaFlagsB.HasFlag(BenchmarkAnswerFlags.DimensionOutlier)) flagDescriptions.Add("Member B: dimension outlier: one level ≤ 1 beside three at ≥ 3, no defect of that kind named (advisory, changed no score)");
                if (ia.ToolBudgetExhausted)
                {
                    flagDescriptions.Add($"Tool call budget reached ({FormatToolBudgetLine(ia)}) — configured harness limit, not an error");
                }

                if (flagDescriptions.Count == 0 && ia.Status == BenchmarkAnswerStatus.Ok)
                {
                    continue;
                }

                string desc = string.Join("; ", flagDescriptions);
                string note;
                if (unanswered)
                {
                    note = " *(Scored 0: no answer produced)*";
                }
                else if (ia.Status is BenchmarkAnswerStatus.ProviderError or BenchmarkAnswerStatus.Failed
                    or BenchmarkAnswerStatus.Canceled or BenchmarkAnswerStatus.EmptyAnswer)
                {
                    note = " *(Note: Excluded from scoring)*";
                }
                else
                {
                    note = string.Empty;
                }
                sb.AppendLine($"- **Question {ia.OrderIndex}:** Status {ia.Status} — {desc}.{note}");
            }

            // Why the narration lines above are not a chat defect. The scrubber lives entirely in
            // the benchmark path, and it must stay there: a reader who sees "reasoning narration
            // removed" enough times will eventually propose removing it in production too, which
            // would make the chat agent violate its own prompt. Stated here, beside the finding,
            // because that is where the reader who would draw the wrong conclusion is standing.
            if (issueAnswers.Any(a => ((BenchmarkAnswerFlags)a.AnswerFlags).HasFlag(BenchmarkAnswerFlags.ReasoningBleed)))
            {
                sb.AppendLine();
                sb.AppendLine("> **Reasoning narration is a benchmark-only removal, not a production defect.** Narrating a lookup is prompt-compliant in production chat — `Overseer/ToolGuides/_policy.md` instructs the agent to *\"Briefly tell the player what you're looking up when using a tool\"* — and the harness strips it here only so that the graded text is the answer rather than the commentary around it. `BenchmarkArtifactScrubber` has no equivalent in the chat path by design, and must not acquire one.");
            }
        }
        sb.AppendLine();

        var stageFailedAnswers = answers.Where(a =>
            !string.IsNullOrWhiteSpace(a.AssessmentError)
            || !string.IsNullOrWhiteSpace(a.ClaimVerificationError)
            || !string.IsNullOrWhiteSpace(a.SecondOpinionError)
            || (isPanelRun && !string.IsNullOrWhiteSpace(a.CoAssessmentError)))
            .OrderBy(a => a.OrderIndex)
            .ToList();

        if (stageFailedAnswers.Count > 0)
        {
            sb.AppendLine("### Harness Stage Failures");
            sb.AppendLine("*(Advisory infrastructure failures; candidate output was not damaged)*");
            sb.AppendLine();
            foreach (var sfa in stageFailedAnswers)
            {
                var stageErrors = new List<string>();
                if (!string.IsNullOrWhiteSpace(sfa.AssessmentError))
                {
                    stageErrors.Add($"Assessment failed: {sfa.AssessmentError.Trim()}");
                }
                if (isPanelRun && !string.IsNullOrWhiteSpace(sfa.CoAssessmentError))
                {
                    stageErrors.Add($"Panel member B assessment failed: {sfa.CoAssessmentError.Trim()}");
                }
                if (!string.IsNullOrWhiteSpace(sfa.ClaimVerificationError))
                {
                    stageErrors.Add($"Claim verification failed: {sfa.ClaimVerificationError.Trim()}");
                }
                if (!string.IsNullOrWhiteSpace(sfa.SecondOpinionError))
                {
                    stageErrors.Add($"{(isPanelRun ? "Reference reader" : "Second reader")} failed: {sfa.SecondOpinionError.Trim()}");
                }
                sb.AppendLine($"- **Question {sfa.OrderIndex}:** {string.Join("; ", stageErrors)}");
            }
            sb.AppendLine();
        }

        // Synthesis divergence. The run-level synthesis reads every verdict at once and can name
        // a hallucination the per-question grader declined to flag — which is what happened on
        // the 2026-09-03 run, where the synthesis made a fabrication its headline finding while
        // that question's own verdict returned criticalError: false. The per-question verdict is
        // what scored, so this is advisory, but the contradiction belongs in the record. In a panel
        // run each member's synthesis is read against that member's own verdicts; a verdict the
        // member never returned (a null critical error) is not a divergence.
        void AppendSynthesisDivergence(
            string? synthesisText,
            string? member,
            Func<BenchmarkRunAnswer, bool?> criticalErrorOf,
            Func<BenchmarkRunAnswer, (int? Level, string? Evidence)> accuracyOf)
        {
            string headingSuffix = member == null ? string.Empty : $" (Panel Member {member})";

            var synthesisNamed = BenchmarkVerdictConsistency.QuestionsNamedWithFabrication(
                synthesisText,
                answers.Select(a => a.OrderIndex));
            var synthesisDivergent = synthesisNamed
                .Select(index => answers.FirstOrDefault(a => a.OrderIndex == index))
                .Where(a => a != null && criticalErrorOf(a) == false)
                .Select(a => a!)
                .ToList();
            if (synthesisDivergent.Count > 0)
            {
                sb.AppendLine($"### Synthesis Divergence{headingSuffix}");
                sb.AppendLine();
                foreach (var d in synthesisDivergent)
                {
                    sb.AppendLine(member == null
                        ? $"- **Question {d.OrderIndex}:** the run synthesis reports a hallucination that the per-question verdict did not flag as a critical error. Advisory — the per-question verdict is what scored."
                        : $"- **Question {d.OrderIndex}:** member {member}'s synthesis reports a hallucination that member {member}'s own verdict did not flag as a critical error. Advisory — the panel score is what scored.");
                }
                sb.AppendLine();
            }

            // Synthesis accuracy divergence — the same defect as its neighbour above, from the other
            // direction: there the synthesis said more than the verdicts, here it says less. On the
            // 2026-09-06 run the synthesis reported the run's weaknesses as "confined to secondary
            // omissions rather than factual errors" while Q11's and Q14's own accuracyEvidence each
            // named a concrete false assertion. Neither had a refuted claim or a critical-error split,
            // which is all the scoring method v7 guardrail covered, so nothing said so.
            //
            // Rendered only when both halves hold: the claim was made, and there is evidence against
            // it. Either alone is ordinary — a clean run makes the claim honestly, and a run with
            // accuracy deductions whose synthesis reports them is doing its job. Advisory; changes no
            // score, exactly like its neighbour.
            if (BenchmarkVerdictConsistency.SynthesisClaimsNoFactualErrors(synthesisText))
            {
                // Materialised once: the evidence lives in a JSON blob per answer, and the list below
                // needs both the order index it selects on and the string it prints.
                var accuracyVerdicts = answers
                    .Select(a =>
                    {
                        var accuracy = accuracyOf(a);
                        return (
                            OrderIndex: a.OrderIndex,
                            AccuracyLevel: accuracy.Level,
                            AccuracyEvidence: accuracy.Evidence);
                    })
                    .ToList();

                var namedAccuracyDefects = BenchmarkVerdictConsistency.AnswersWithNamedAccuracyDefects(accuracyVerdicts);

                if (namedAccuracyDefects.Count > 0)
                {
                    sb.AppendLine($"### Synthesis Accuracy Divergence{headingSuffix}");
                    sb.AppendLine();
                    sb.AppendLine(member == null
                        ? $"The run synthesis describes this run as free of factual errors, while {namedAccuracyDefects.Count} answer(s) carry an accuracy deduction whose own evidence names a defect. Advisory — the per-question verdicts below are what scored, and no score changes here."
                        : $"Member {member}'s synthesis describes this run as free of factual errors, while {namedAccuracyDefects.Count} answer(s) carry an accuracy deduction in member {member}'s own verdicts whose evidence names a defect. Advisory — the panel score is what scored, and no score changes here.");
                    sb.AppendLine();
                    foreach (int index in namedAccuracyDefects)
                    {
                        var v = accuracyVerdicts.First(x => x.OrderIndex == index);
                        string level = v.AccuracyLevel?.ToString(CultureInfo.InvariantCulture) ?? "?";
                        string evidence = BenchmarkAssessmentFailure.Truncate(v.AccuracyEvidence, 300) ?? string.Empty;
                        sb.AppendLine($"- **Question {index}:** Accuracy {level} / 6 — {evidence}");
                    }
                    sb.AppendLine();
                }
            }
        }

        if (isPanelRun)
        {
            AppendSynthesisDivergence(run.AssessmentText, "A",
                a => a.CriticalError,
                a => (a.AccuracyLevel, ReadEvidence(a).Accuracy));
            AppendSynthesisDivergence(run.CoAssessorSynthesisText, "B",
                a => BenchmarkVerdictView.FromCoAssessment(a)?.CriticalError,
                a => BenchmarkVerdictView.FromCoAssessment(a) is { } view
                    ? ((int?)view.AccuracyLevel, view.AccuracyEvidence)
                    : ((int?)null, (string?)null));
        }
        else
        {
            AppendSynthesisDivergence(run.AssessmentText, null,
                a => a.CriticalError,
                a => (a.AccuracyLevel, ReadEvidence(a).Accuracy));
        }

        // Disputed assessments. A single grader deciding a low score is the least reproducible
        // part of this benchmark, so where a second one was asked and disagreed, the report says
        // so rather than presenting one verdict as settled fact.
        var disputed = answers.Where(a => a.SecondOpinionDisagreed && a.SecondOpinionQualityScore.HasValue)
            .OrderBy(a => a.OrderIndex)
            .ToList();
        if (disputed.Count > 0)
        {
            sb.AppendLine("### Disputed Assessments");
            sb.AppendLine();
            sb.AppendLine(isPanelRun
                ? $"{disputed.Count} answer(s) carry a reference reader verdict flagged as materially different. The panel score and the reference reader's are shown here, with each member's score beside the panel's; the panel score is what scored, and the reader is advisory."
                : $"{disputed.Count} answer(s) were read by a second reader, which reached a materially different verdict. The assessor's verdict is what scored; these are flagged for a human to settle, and re-assessing from the run detail is the way to do it.");
            sb.AppendLine();
            foreach (var d in disputed)
            {
                // The accused sentences are counted apart from the answer's own claims, from the stored verification.
                var claimNoteParts = new List<string>();
                if (d.ClaimsSupportedCount.HasValue || d.ClaimsRefutedCount.HasValue || d.ClaimsIndeterminateCount.HasValue)
                {
                    claimNoteParts.Add($"Claims: {d.ClaimsSupportedCount ?? 0} supported, {d.ClaimsRefutedCount ?? 0} refuted, {d.ClaimsIndeterminateCount ?? 0} indeterminate");
                }
                var disputedAccused = AccusedSentencesOf(d);
                if (disputedAccused.Count > 0)
                {
                    var (disputedSupported, disputedRefuted, disputedIndeterminate) = VerdictCounts(disputedAccused);
                    string accusedCounts = $"{disputedSupported} supported, {disputedRefuted} refuted, {disputedIndeterminate} indeterminate";
                    claimNoteParts.Add(claimNoteParts.Count == 0 ? $"Accused sentences: {accusedCounts}" : $"accused sentences: {accusedCounts}");
                }
                string claimNote = claimNoteParts.Count == 0 ? string.Empty : $" [{string.Join("; ", claimNoteParts)}]";
                if (isPanelRun)
                {
                    static string YesNo(bool? value) => value == true ? "yes" : "no";
                    string panelScore = d.PanelQualityScore.HasValue ? $"{ScoreText(d.PanelQualityScore.Value)} / 100" : "not computed";
                    string memberBScore = d.CoAssessmentQualityScore.HasValue ? Inv(d.CoAssessmentQualityScore.Value) : "not scored";
                    sb.AppendLine($"- **Question {d.OrderIndex}:** panel {panelScore} (A {d.QualityScore?.ToString(CultureInfo.InvariantCulture) ?? "not scored"}, B {memberBScore}; critical error A {YesNo(d.CriticalError)}, B {YesNo(d.CoAssessmentCriticalError)}) vs reference reader {d.SecondOpinionQualityScore!.Value} / 100 (critical error {YesNo(d.SecondOpinionCriticalError)}, {d.SecondOpinionByModelSnapshot.Label()}){claimNote}");
                }
                else
                {
                    sb.AppendLine($"- **Question {d.OrderIndex}:** first {d.QualityScore ?? 0} / 100 (critical error {(d.CriticalError ? "yes" : "no")}, {d.AssessedByModelSnapshot.Label()}) vs second {d.SecondOpinionQualityScore!.Value} / 100 (critical error {(d.SecondOpinionCriticalError == true ? "yes" : "no")}, {d.SecondOpinionByModelSnapshot.Label()}){claimNote}");
                }
                string? secondReaderComment = ReadSecondOpinionComment(d.SecondOpinionJson);
                if (secondReaderComment != null)
                {
                    sb.AppendLine($"  - {(isPanelRun ? "Reference reader" : "Second reader")}: {secondReaderComment}");
                }
            }
            sb.AppendLine();
        }

        // 7. Assessment
        sb.AppendLine("## 6. Synthesis Assessment");
        sb.AppendLine();

        // One synthesis: its overall comments (or the raw output when it did not parse), then its
        // structured findings, which only a harness-40 synthesis carries.
        void AppendSynthesis(string? text, bool parseFailed, string? json, IReadOnlyList<BenchmarkSynthesisFinding> findings)
        {
            if (!string.IsNullOrWhiteSpace(text))
            {
                sb.AppendLine(text);
            }
            else if (parseFailed)
            {
                sb.AppendLine("*Note: The assessment output could not be parsed into the structured schema. Raw output:*");
                sb.AppendLine();
                sb.AppendLine(json);
            }
            else
            {
                sb.AppendLine("No synthesis assessment generated.");
            }
            AppendSynthesisFindings(sb, findings);
            sb.AppendLine();
        }

        var memberAFindings = BenchmarkAssessmentParser.ParseSynthesisFindings(run.AssessmentJson);
        IReadOnlyList<BenchmarkSynthesisConvergenceRow> convergence = Array.Empty<BenchmarkSynthesisConvergenceRow>();
        if (isPanelRun)
        {
            var memberBFindings = BenchmarkAssessmentParser.ParseSynthesisFindings(run.CoAssessorSynthesisJson);

            sb.AppendLine($"### 6.1 Panel Member A: {memberALabel} ({run.AssessorModelSnapshot.Provider}) — {memberARelation} reading");
            sb.AppendLine();
            AppendSynthesis(run.AssessmentText, run.AssessmentParseFailed, run.AssessmentJson, memberAFindings);

            sb.AppendLine($"### 6.2 Panel Member B: {memberBLabel} ({run.CoAssessorModelSnapshot?.Provider}) — {memberBRelation} reading");
            sb.AppendLine();
            AppendSynthesis(run.CoAssessorSynthesisText, run.CoAssessorSynthesisParseFailed, run.CoAssessorSynthesisJson, memberBFindings);

            // Computed from the two members' structured findings, never written by either model.
            sb.AppendLine("### 6.3 Where the Readers Agree and Disagree (computed)");
            sb.AppendLine();
            convergence = BenchmarkSynthesisConvergence.Compute(memberAFindings, memberBFindings);
            if (convergence.Count > 0)
            {
                sb.AppendLine("| Finding | Questions | A | B | Status |");
                sb.AppendLine("|---------|-----------|---|---|--------|");
                foreach (var row in convergence)
                {
                    string status = row.Status switch
                    {
                        BenchmarkConvergenceStatus.Convergent => "Convergent",
                        BenchmarkConvergenceStatus.MemberAOnly => "Member A only",
                        BenchmarkConvergenceStatus.MemberBOnly => "Member B only",
                        BenchmarkConvergenceStatus.Conflicting => "Conflicting",
                        _ => row.Status.ToString()
                    };
                    string questions = ConvergenceQuestionsText(row);
                    string kind = row.Status == BenchmarkConvergenceStatus.Conflicting
                        ? $"{row.Kind} (A) vs {BenchmarkSynthesisConvergence.OppositeKind(row.Kind)} (B)"
                        : row.Kind;
                    sb.AppendLine($"| {TableCell(kind)} · {TableCell(FindingCategoryText(row.Category))} | {questions} | {TableCell(row.MemberAText)} | {TableCell(row.MemberBText)} | {status} |");
                }
            }
            else
            {
                sb.AppendLine("Neither synthesis recorded structured findings, so agreement between the two readings is not computed.");
            }
            sb.AppendLine();
            sb.AppendLine($"*Reading rule: a convergent finding — named by both members, from {(IsSameFamily(run.AssessorModelSnapshot.Provider, run.CoAssessorModelSnapshot?.Provider) ? "one family" : "two families")}, on the same question — is the strongest evidence this section offers. A finding only one member named is one reader's view, and single-reader praise from the member of the candidate's own family is the weakest evidence of all.*");
            sb.AppendLine();
        }
        else
        {
            AppendSynthesis(run.AssessmentText, run.AssessmentParseFailed, run.AssessmentJson, memberAFindings);
        }

        int totalRefutedClaims = run.ClaimsRefutedCount > 0 ? run.ClaimsRefutedCount : answers.Sum(a => a.ClaimsRefutedCount ?? 0);
        int disputedVerdicts = answers.Count(a => a.SecondOpinionDisagreed && a.SecondOpinionQualityScore.HasValue);
        if (totalRefutedClaims > 0 || disputedVerdicts > 0 || contestedCriticalErrorCount > 0 || contestedAccuracyDeductionCount > 0)
        {
            // The first two figures are always stated, zero or not: the sentence exists to put the
            // record beside the narrative, and "0 refuted claim(s)" is itself the record. The
            // contested figures are stated only when non-zero, because a zero there is
            // indistinguishable from a run whose verifier never checked a critical-error quote or
            // an out-of-rubric basis at all.
            var countParts = new List<string>
            {
                $"{totalRefutedClaims} refuted claim(s)",
                $"{disputedVerdicts} disputed verdict(s)"
            };
            if (contestedCriticalErrorCount > 0) countParts.Add($"{contestedCriticalErrorCount} contested critical error(s)");
            if (contestedAccuracyDeductionCount > 0) countParts.Add($"{contestedAccuracyDeductionCount} contested accuracy deduction(s)");
            string counts = string.Join(", ", countParts.Take(countParts.Count - 1)) + " and " + countParts[^1];
            sb.AppendLine(isPanelRun
                ? $"*The syntheses above are the panel members' own narratives. This run recorded {counts} — see Run Integrity and Disputed Assessments.*"
                : $"*The synthesis above is the primary assessor's own narrative. This run recorded {counts} — see Run Integrity and Disputed Assessments.*");
            sb.AppendLine();
        }

        // 8. Final Score
        sb.AppendLine("## 7. Final Indices");
        sb.AppendLine();
        sb.AppendLine($"# **Intelligence Index: {IndexHeadline(run.QualityIndex, $"{seText} / 100", terminalFailureCount, run.TotalQuestionCount, "N/A")}**");
        // As in the summary, only printed when a critical-error cap actually moved it.
        if (rawQualityIndex.HasValue && rawQualityIndex.Value != (run.QualityIndex ?? 0))
        {
            sb.AppendLine($"### Raw Quality Index: {rawQualityIndex.Value} / 100");
        }
        if (isPanelRun)
        {
            // The member-alone indices take the member-A-only sensitivity figures' place: how far the
            // published index rests on either member.
            sb.AppendLine($"### Panel Member A Alone: {(run.AssessorOnlyQualityIndex.HasValue ? $"{run.AssessorOnlyQualityIndex.Value} / 100" : "not recorded")} — {memberALabel}, {memberARelation}; advisory, the published index is the panel's.");
            sb.AppendLine($"### Panel Member B Alone: {(run.CoAssessorOnlyQualityIndex.HasValue ? $"{run.CoAssessorOnlyQualityIndex.Value} / 100" : "not recorded")} — {memberBLabel}, {memberBRelation}; advisory, the published index is the panel's.");
            // The same clause as § 2, from the same variable.
            if (panelSensitivityClause != null)
            {
                sb.AppendLine($"### {panelSensitivityClause.Replace(":**", ":", StringComparison.Ordinal)}");
            }
            sb.AppendLine("### Other Sensitivity Figures: not computed for a panel run — the contested-verdict, evidence-informed and FORM-cleared sensitivities each re-score member A's verdict alone; see § 2.");
        }
        // The same value and clause as § 2, from the same variable.
        if (resolutionSensitivityClause != null)
        {
            sb.AppendLine($"### {resolutionSensitivityClause.Replace(":**", ":", StringComparison.Ordinal)}");
        }
        // Same value and clause as § 2 — this is where the headline figures live, and it is the
        // one that says how fragile they are.
        if (splitAnswers.Count > 0 && sensitivityIndex.HasValue)
        {
            sb.AppendLine($"### Contested-Verdict Sensitivity: {sensitivityIndex.Value} / 100 — Intelligence Index recomputed with each split resolved at the second reader's score (raises and lowers both).");
        }
        // Likewise the same value as § 2, from the same variable.
        if (verificationClearedIndex.HasValue)
        {
            sb.AppendLine($"### Verification-cleared Accuracy Sensitivity: {verificationClearedIndex.Value} / 100 — Intelligence Index recomputed with Accuracy one level higher on the {verificationClearedAnswers.Count} answer(s) above; advisory, changes no score.");
        }
        // Likewise the Readability counterpart's same value as § 2, from the same variable.
        if (formClearedIndex.HasValue)
        {
            sb.AppendLine($"### FORM-cleared Readability Sensitivity: {formClearedIndex.Value} / 100 — Intelligence Index recomputed with Readability one level higher on the {formClearedAnswers.Count} answer(s) whose only Readability basis was a rubric FORM suggestion; advisory, changes no score.");
        }
        // Likewise the same value and clause as § 2, from the same variables.
        if (evidenceInformedClause != null)
        {
            sb.AppendLine($"### {evidenceInformedClause.Replace(":**", ":", StringComparison.Ordinal)}");
        }
        // H9. The same demotion as § 2, from the same two conditions, so the headline block and the
        // summary cannot present the speed figure differently.
        if (speedAdvisory && medianModelTimeMs.HasValue)
        {
            sb.AppendLine($"### Median Model Time: {Inv(medianModelTimeMs.Value, "N0")} ms");
            sb.AppendLine($"*Speed Index {IndexHeadline(run.SpeedIndex, " / 100", terminalFailureCount, run.TotalQuestionCount, "N/A")} — advisory: {(speedSaturated ? "the index is saturated on this run" : "the profile's latency target does not fit this candidate's thinking level")}.*");
        }
        else
        {
            sb.AppendLine($"### Speed Index: {IndexHeadline(run.SpeedIndex, " / 100", terminalFailureCount, run.TotalQuestionCount, "N/A")}");
        }
        if (isPanelRun)
        {
            sb.AppendLine($"### Holistic Score, Panel Member A: {run.FinalScore?.ToString() ?? "N/A"} / 100");
            sb.AppendLine($"### Holistic Score, Panel Member B: {run.CoAssessorFinalScore?.ToString() ?? "N/A"} / 100");
            sb.AppendLine();
            sb.AppendLine("> **How to read these:** the Intelligence Index is the canonical, reproducible metric and is **quality only** — Speed Index is not folded into it, by design, so a slow model and an inaccurate one are never confused for each other. It is the panel's: the mean of both members' per-answer quality. The two holistic scores are each member's own narrative judgment and are reported for contrast, not used in any aggregate.");
        }
        else
        {
            sb.AppendLine($"### Holistic Assessor Score: {run.FinalScore?.ToString() ?? "N/A"} / 100");
            sb.AppendLine();
            sb.AppendLine("> **How to read these:** the Intelligence Index is the canonical, reproducible metric and is **quality only** — Speed Index is not folded into it, by design, so a slow model and an inaccurate one are never confused for each other. The Holistic Assessor Score is the assessor's own narrative judgment and is reported for contrast, not used in any aggregate.");
        }
        sb.AppendLine();
        // The Intelligence Index weights each question by its assessed difficulty, so a critical
        // error caps that one question's Quality at 25 but moves the overall index least on the
        // easiest questions — on the 2026-09-03 run a fully hallucinated answer at assessed
        // difficulty 25 moved the index by one point. The Critical Errors count under Results
        // Summary above is the figure to read for this failure mode, not the index delta.
        // A panel run names the Critical Errors line only when it printed.
        sb.AppendLine(!isPanelRun || criticalErrorsLinePrints
            ? "> The Intelligence Index weights each question by its assessed difficulty, so a critical error on an easy question moves the index the least of all — a fully hallucinated answer at assessed difficulty 25 can move it by as little as one point. The **Critical Errors** count under Results Summary is the number to read for this failure mode."
            : "> The Intelligence Index weights each question by its assessed difficulty, so a critical error on an easy question moves the index the least of all — a fully hallucinated answer at assessed difficulty 25 can move it by as little as one point. Neither panel member nor the reference reader recorded a critical error on this run.");
        sb.AppendLine();
        if (isPanelRun)
        {
            // One notice per synthesis, each against the published panel index.
            void PanelDivergenceNotice(string member, int? holistic, int? memberAlone)
            {
                if (!holistic.HasValue || !run.QualityIndex.HasValue) return;
                int panelDiff = Math.Abs(holistic.Value - run.QualityIndex.Value);
                if (panelDiff <= 10) return;
                string aloneClause = memberAlone.HasValue ? $"; member {member}'s own verdicts alone index at {memberAlone.Value}" : string.Empty;
                sb.AppendLine($"> ℹ️ **Index Divergence Notice (Panel Member {member}):** member {member}'s holistic synthesis score ({holistic.Value}) differs by {panelDiff} points from the difficulty-weighted Intelligence Index ({run.QualityIndex.Value}){aloneClause}. The Intelligence Index is the reproducible canonical metric.");
                sb.AppendLine();
            }

            PanelDivergenceNotice("A", run.FinalScore, run.AssessorOnlyQualityIndex);
            PanelDivergenceNotice("B", run.CoAssessorFinalScore, run.CoAssessorOnlyQualityIndex);
        }
        else if (run.FinalScore.HasValue && run.QualityIndex.HasValue)
        {
            int diff = Math.Abs(run.FinalScore.Value - run.QualityIndex.Value);
            if (diff > 10)
            {
                sb.AppendLine($"> ℹ️ **Index Divergence Notice:** The assessor's holistic synthesis score ({run.FinalScore.Value}) differs by {diff} points from the difficulty-weighted Intelligence Index ({run.QualityIndex.Value}). The Intelligence Index is the reproducible canonical metric.");
                sb.AppendLine();
            }
        }

        // At a Glance, from the figures the sections above printed.
        string criticalErrorVerb = resolvesCriticalErrors ? "confirmed" : "applied";
        var glanceCriticalErrors = new StringBuilder(appliedCriticalAnswers.Count > 0
            ? $"{appliedCriticalAnswers.Count} {criticalErrorVerb} ({string.Join(", ", appliedCriticalAnswers.Select(a => $"Q{a.OrderIndex}"))})"
            : $"none {criticalErrorVerb}");
        if (outcomeSummary is { UnresolvedCriticalErrorCount: > 0 })
        {
            glanceCriticalErrors.Append($"; {outcomeSummary.UnresolvedCriticalErrorCount} split(s) unresolved");
        }
        if (outcomeSummary is { OverturnedCriticalErrorCount: > 0 })
        {
            glanceCriticalErrors.Append($"; {outcomeSummary.OverturnedCriticalErrorCount} overturned by the claim verifier");
        }
        if (disputedBySecondReader.Count > 0)
        {
            glanceCriticalErrors.Append($"; {disputedBySecondReader.Count} disputed by the {readerName}");
        }
        if (raisedOnlyBySecondReader.Count > 0)
        {
            glanceCriticalErrors.Append($"; {raisedOnlyBySecondReader.Count} raised only by the {readerName}");
        }
        string glanceAnswered = unansweredAnswers.Count > 0
            ? $"{answerRateText}; {unansweredAnswers.Count} unanswered ({string.Join(", ", unansweredAnswers.Select(a => $"Q{a.OrderIndex}"))})"
            : answerRateText;

        var glance = new StringBuilder();
        AppendAtAGlance(
            glance,
            intelligence: IndexHeadline(run.QualityIndex, $"{seText} / 100", terminalFailureCount, run.TotalQuestionCount, "*not computed*"),
            speed: speedHeadline + GlanceModelTimeClause(medianLeadsSpeed, meanModelTimeMs, medianModelTimeMs)
                + (speedQualifier != null ? $" ({speedQualifier})" : string.Empty),
            cost: glanceCostText,
            criticalErrors: glanceCriticalErrors.ToString(),
            answered: glanceAnswered,
            isPanelRun: isPanelRun,
            agreementLine: glanceAgreementLine,
            convergence: convergence,
            findings: memberAFindings);
        sb.Insert(atAGlanceOffset, glance.ToString());

        return sb.ToString();
    }

    /// <summary>The Chat Prompt Under Test note under the Tools line of a run that allowed source code references.</summary>
    internal const string SourceCodeReferencesAllowedNote =
        "*Source code references: allowed — a user's default is disallowed (the Show source code references setting), so these answers may cite files and lines a default user's would not.*";

    /// <summary>
    /// The model-time clause At a Glance's Speed row adds to the speed headline:
    /// <c> · mean 17.2 s, median 14.7 s model time per question</c>, or, when the median already leads
    /// the row, <c> · mean 17.2 s model time per question</c>. Empty without an Ok answer.
    /// </summary>
    internal static string GlanceModelTimeClause(bool medianLeads, double? meanModelTimeMs, long? medianModelTimeMs)
    {
        if (!meanModelTimeMs.HasValue || !medianModelTimeMs.HasValue) return string.Empty;

        static string Seconds(double ms) => $"{Inv(ms / 1000.0, "F1")} s";
        return medianLeads
            ? $" · mean {Seconds(meanModelTimeMs.Value)} model time per question"
            : $" · mean {Seconds(meanModelTimeMs.Value)}, median {Seconds(medianModelTimeMs.Value)} model time per question";
    }

    /// <summary>
    /// The Questions cell of a § 6.3 row. A row both members raised splits its questions into those
    /// both named, those only A named and those only B named, e.g.
    /// <c>both Q13, Q15 · A only Q1, Q3 · B only Q9</c>, leaving out an empty group; a row one member
    /// raised lists its questions; a run-wide row reads <c>—</c>.
    /// </summary>
    internal static string ConvergenceQuestionsText(BenchmarkSynthesisConvergenceRow row)
    {
        if (row.Questions.Count == 0) return "—";

        static string QuestionList(IEnumerable<int> questions) => string.Join(", ", questions.Select(q => $"Q{q}"));
        if (row.Status is BenchmarkConvergenceStatus.MemberAOnly or BenchmarkConvergenceStatus.MemberBOnly)
        {
            return QuestionList(row.Questions);
        }

        var onlyA = row.QuestionsA.Except(row.SharedQuestions).OrderBy(q => q).ToList();
        var onlyB = row.QuestionsB.Except(row.SharedQuestions).OrderBy(q => q).ToList();
        var parts = new List<string>();
        if (row.SharedQuestions.Count > 0) parts.Add($"both {QuestionList(row.SharedQuestions)}");
        if (onlyA.Count > 0) parts.Add($"A only {QuestionList(onlyA)}");
        if (onlyB.Count > 0) parts.Add($"B only {QuestionList(onlyB)}");
        return string.Join(" · ", parts);
    }

    /// <summary>The most convergent findings At a Glance lists for a panel run.</summary>
    internal const int AtAGlanceMaxConvergentFindings = 5;

    /// <summary>The most strengths, and separately weaknesses, At a Glance lists for a single-assessor run.</summary>
    internal const int AtAGlanceMaxFindingsPerKind = 3;

    /// <summary>
    /// The unnumbered At a Glance section between the introduction and § 1: the five headline
    /// figures as a table, then for a panel run the graders' agreement and the findings both members
    /// named, or for a single assessor its synthesis's first strengths and weaknesses, then which
    /// section answers which question. Every figure is passed in as the numbered sections print it;
    /// none is computed here. Carries no <c>###</c> heading and no sensitivity figure, so the first
    /// occurrence of each of those stays in the section that owns it.
    /// </summary>
    private static void AppendAtAGlance(
        StringBuilder sb,
        string intelligence,
        string speed,
        string cost,
        string criticalErrors,
        string answered,
        bool isPanelRun,
        string? agreementLine,
        IReadOnlyList<BenchmarkSynthesisConvergenceRow> convergence,
        IReadOnlyList<BenchmarkSynthesisFinding> findings)
    {
        // A finding as one bullet: kind, category and questions, then its text on one line.
        static string FindingLine(string kind, string? category, IReadOnlyList<int> questions, string text)
        {
            string label = kind.Length > 0 ? char.ToUpperInvariant(kind[0]) + kind[1..].ToLowerInvariant() : "Finding";
            string questionText = questions.Count > 0
                ? " · " + string.Join(", ", questions.OrderBy(q => q).Select(q => $"Q{q}"))
                : string.Empty;
            return $"- {label} · {FindingCategoryText(category)}{questionText} — {TableCell(text)}";
        }

        sb.AppendLine("## At a Glance");
        sb.AppendLine();
        sb.AppendLine("| Figure | Value |");
        sb.AppendLine("|--------|-------|");
        sb.AppendLine($"| Intelligence | {TableCell(intelligence)} |");
        sb.AppendLine($"| Speed | {TableCell(speed)} |");
        sb.AppendLine($"| Cost | {TableCell(cost)} |");
        sb.AppendLine($"| Critical errors | {TableCell(criticalErrors)} |");
        sb.AppendLine($"| Answered | {TableCell(answered)} |");
        sb.AppendLine();

        if (isPanelRun)
        {
            if (agreementLine != null)
            {
                sb.AppendLine(agreementLine);
                sb.AppendLine();
            }

            var convergent = convergence.Where(r => r.Status == BenchmarkConvergenceStatus.Convergent).ToList();
            if (convergent.Count > 0)
            {
                string shown = convergent.Count > AtAGlanceMaxConvergentFindings
                    ? $"the first {AtAGlanceMaxConvergentFindings} of {convergent.Count}"
                    : $"{convergent.Count.ToString(CultureInfo.InvariantCulture)} in all";
                sb.AppendLine($"**Findings both members named** ({shown}; the full comparison is § 6.3):");
                sb.AppendLine();
                foreach (var row in convergent.Take(AtAGlanceMaxConvergentFindings))
                {
                    string rowText = string.Join(" / ", new[] { row.MemberAText, row.MemberBText }.Where(t => !string.IsNullOrWhiteSpace(t)));
                    sb.AppendLine(FindingLine(row.Kind, row.Category, row.SharedQuestions, rowText));
                }
            }
            else
            {
                sb.AppendLine("**Findings both members named:** none; the full comparison is § 6.3.");
            }
            sb.AppendLine();
        }
        else
        {
            var strengths = findings
                .Where(f => string.Equals(f.Kind, "strength", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(f.Text))
                .Take(AtAGlanceMaxFindingsPerKind)
                .ToList();
            var weaknesses = findings
                .Where(f => string.Equals(f.Kind, "weakness", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(f.Text))
                .Take(AtAGlanceMaxFindingsPerKind)
                .ToList();
            if (strengths.Count > 0 || weaknesses.Count > 0)
            {
                sb.AppendLine("**The assessor's leading findings** (the full synthesis is § 6):");
                sb.AppendLine();
                foreach (var f in strengths.Concat(weaknesses))
                {
                    sb.AppendLine(FindingLine(f.Kind.Trim(), f.Category, f.Questions, f.Text));
                }
            }
            else
            {
                sb.AppendLine("**The assessor's leading findings:** none recorded as structured findings; the synthesis is § 6.");
            }
            sb.AppendLine();
        }

        sb.AppendLine("**Where to find what**");
        sb.AppendLine();
        sb.AppendLine("- *What was run, and is it comparable with another run?* § 1 Run Manifest");
        sb.AppendLine("- *How good and how fast, and how certain is that?* § 2 Results Summary; the closing figures are § 7 Final Indices");
        sb.AppendLine("- *What did the run cost?* § 2 Harness Cost");
        sb.AppendLine("- *Did the run itself go wrong anywhere?* § 2 Run Integrity and § 5 Issues");
        if (isPanelRun)
        {
            sb.AppendLine("- *Do the two graders agree?* § 2 Panel Agreement and § 6.3");
        }
        sb.AppendLine("- *Which questions did the model get wrong, and why?* § 3 Questions and Replies");
        sb.AppendLine("- *How is a score computed?* § 4 Scoring Method & Configuration");
        sb.AppendLine("- *What did the graders conclude overall?* § 6 Synthesis Assessment");
        sb.AppendLine();
    }

    /// <summary>
    /// The most recent re-run's (failed-question or single-answer) span, "start to end UTC (duration)". The end reads
    /// "unrecorded end" while the re-run is still running or when a stale stamp predates the start.
    /// </summary>
    private static string RerunSpan(BenchmarkRun run)
    {
        bool hasEnd = run.RerunCompletedAtUtc.HasValue &&
            (!run.RerunStartedAtUtc.HasValue || run.RerunCompletedAtUtc.Value >= run.RerunStartedAtUtc.Value);
        string start = run.RerunStartedAtUtc.HasValue ? Stamp(run.RerunStartedAtUtc.Value) : "unrecorded start";
        string end = hasEnd ? Stamp(run.RerunCompletedAtUtc!.Value) : "unrecorded end";
        string duration = hasEnd && run.RerunStartedAtUtc.HasValue
            ? $" ({FormatDuration((long)(run.RerunCompletedAtUtc!.Value - run.RerunStartedAtUtc.Value).TotalMilliseconds)})"
            : string.Empty;
        return $"{start} to {end} UTC{duration}";
    }

    private static string FormatDuration(long ms)
    {
        if (ms < 0)
        {
            return "-" + FormatDuration(-ms);
        }

        var ts = TimeSpan.FromMilliseconds(ms);
        if (ts.TotalHours >= 1)
        {
            return $"{(int)ts.TotalHours}h {ts.Minutes}m {ts.Seconds}s";
        }
        if (ts.TotalMinutes >= 1)
        {
            return $"{ts.Minutes}m {ts.Seconds}s";
        }
        return $"{ts.Seconds}.{ts.Milliseconds / 100}s";
    }
}

/// <summary>
/// The battery run slot a benchmark run fills, as the run's report manifest names it: the battery
/// run, the battery's name and definition revision, the 1-based suite position of
/// <see cref="SuiteCount"/> and the round of <see cref="RoundCount"/>. <see cref="Revision"/> is
/// null when the battery run's stored definition cannot be read.
/// </summary>
public sealed record BenchmarkRunBatteryContext(
    long BatteryRunId,
    string BatteryName,
    int? Revision,
    int SuiteNumber,
    int SuiteCount,
    int Round,
    int RoundCount)
{
    /// <summary>
    /// The context of <paramref name="member"/> within <paramref name="batteryRun"/>. The revision and
    /// the suite count are read from the battery run's definition snapshot; when it cannot be read the
    /// suite count falls back to the requested member count over the runs per suite.
    /// </summary>
    public static BenchmarkRunBatteryContext From(BenchmarkBatteryRunMember member, BenchmarkBatteryRun batteryRun)
    {
        ArgumentNullException.ThrowIfNull(member);
        ArgumentNullException.ThrowIfNull(batteryRun);

        BenchmarkBatteryDefinition? definition;
        try
        {
            definition = BenchmarkBatteryDefinition.FromJson(batteryRun.DefinitionJson ?? string.Empty);
        }
        catch (JsonException)
        {
            definition = null;
        }

        int suiteCount = definition?.Suites.Count
            ?? (batteryRun.RunsPerSuite > 0 ? batteryRun.RequestedMemberCount / batteryRun.RunsPerSuite : 0);

        return new BenchmarkRunBatteryContext(
            batteryRun.Id,
            batteryRun.BatteryName,
            definition?.Revision,
            member.SuiteIndex + 1,
            suiteCount,
            member.Round,
            batteryRun.RunsPerSuite);
    }

    /// <summary>
    /// The manifest line, e.g. <c>- **Battery:** Battery run #4 (Core, revision 2), suite 1 of 3, round 2 of 3</c>.
    /// </summary>
    public string ManifestLine()
    {
        string revision = Revision.HasValue
            ? $"revision {Revision.Value.ToString(CultureInfo.InvariantCulture)}"
            : "revision not recorded";
        return $"- **Battery:** Battery run #{BatteryRunId.ToString(CultureInfo.InvariantCulture)} ({BatteryName}, {revision}), "
            + $"suite {SuiteNumber.ToString(CultureInfo.InvariantCulture)} of {SuiteCount.ToString(CultureInfo.InvariantCulture)}, "
            + $"round {Round.ToString(CultureInfo.InvariantCulture)} of {RoundCount.ToString(CultureInfo.InvariantCulture)}";
    }
}
