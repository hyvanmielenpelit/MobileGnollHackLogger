namespace Overseer.Services.Benchmarking;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Overseer.Models;

/// <summary>
/// One covered model's figures the notable-finding rules read, each null where the comparison does not
/// carry it: a degraded axis has no time or cost, a model without a failure profile no profile counts.
/// </summary>
public sealed class BenchmarkNotableModel
{
    public string Letter { get; init; } = string.Empty;
    public string Provider { get; init; } = string.Empty;
    public string ModelId { get; init; } = string.Empty;
    public string? ThinkingLevel { get; init; }

    public double? Quality { get; init; }
    public double? QualityLower { get; init; }
    public double? QualityUpper { get; init; }

    /// <summary>Median model time per answer; null when degraded or not recorded.</summary>
    public double? ModelTimeP50Ms { get; init; }

    /// <summary>Cost per question; null when degraded or not priced.</summary>
    public double? CostPerQuestion { get; init; }

    public double? SimpleBandScore { get; init; }
    public int SimpleBandQuestions { get; init; }
    public double? AdvancedBandScore { get; init; }
    public int AdvancedBandQuestions { get; init; }

    /// <summary>Critical errors, and the answers counting toward the index they are counted over.</summary>
    public int? CriticalErrors { get; init; }
    public int? CountingAnswers { get; init; }

    /// <summary>Panel disagreements, and the panel-graded answers they are counted over; null without a panel in every run.</summary>
    public int? PanelDisagreements { get; init; }
    public int? PanelAnswers { get; init; }

    public double? ToolCallsPerQuestion { get; init; }
    public double? MedianAnswerChars { get; init; }

    /// <summary>Scored answers, and of them the truncated ones and the tool-limit hits.</summary>
    public int? Answers { get; init; }
    public int? Truncated { get; init; }
    public int? ToolLimitHits { get; init; }

    /// <summary>Provider errors ÷ valid attempts; null when not recorded.</summary>
    public double? ProviderErrorRate { get; init; }
}

/// <summary>What <see cref="BenchmarkNotableFindings.Detect"/> reads.</summary>
public sealed class BenchmarkNotableFindingsInput
{
    /// <summary>The covered models in letter order: A has the highest Intelligence Index.</summary>
    public IReadOnlyList<BenchmarkNotableModel> Models { get; init; } = Array.Empty<BenchmarkNotableModel>();

    /// <summary>The comparison's question matrix, each question with its per-model cells.</summary>
    public IReadOnlyList<BenchmarkReportQuestion> Questions { get; init; } = Array.Empty<BenchmarkReportQuestion>();

    /// <summary>The sheet's paired-test families, oriented by letter.</summary>
    public IReadOnlyList<BenchmarkReportPairedFamily> Families { get; init; } = Array.Empty<BenchmarkReportPairedFamily>();

    /// <summary>The reference model's letter; the first letter when null.</summary>
    public string? ReferenceLetter { get; init; }

    /// <summary>The reference model is the Overseer team's baseline model.</summary>
    public bool ReferenceIsBaselineModel { get; init; }

    /// <summary>Pairs of letters whose order depends on which panel member grades.</summary>
    public IReadOnlyList<(string First, string Second)> JudgeDependentPairs { get; init; } = Array.Empty<(string, string)>();
}

/// <summary>
/// Detects the notable findings of a comparison-scope sheet from its facts, its question matrix and its
/// models' failure profiles: patterns a reader would want pointed out, each stated by code in one
/// sentence that names models as <c>Model X</c>, so it reads named or anonymized like every fact. In
/// <see cref="Kinds"/> order, at most <see cref="MaxPerKind"/> of a kind and <see cref="MaxFindings"/> in
/// all. A rule whose figures a model lacks skips that model. Pure: no I/O, no clock, no culture.
/// </summary>
public static class BenchmarkNotableFindings
{
    public const string CheaperAndBetter = "cheaperAndBetter";
    public const string FasterAndBetter = "fasterAndBetter";
    public const string ThinkingLevelInversion = "thinkingLevelInversion";
    public const string LeaderMiss = "leaderMiss";
    public const string UnderdogWin = "underdogWin";
    public const string SharedFailure = "sharedFailure";
    public const string DifficultyInversion = "difficultyInversion";
    public const string CriticalErrorConcentration = "criticalErrorConcentration";
    public const string GraderDisagreementConcentration = "graderDisagreementConcentration";
    public const string JudgeDependentOrder = "judgeDependentOrder";
    public const string OutputTrouble = "outputTrouble";
    public const string ToolUseOutlier = "toolUseOutlier";
    public const string AnswerLengthOutlier = "answerLengthOutlier";
    public const string ProviderErrors = "providerErrors";

    /// <summary>The kinds in priority order.</summary>
    public static readonly IReadOnlyList<string> Kinds = new[]
    {
        CheaperAndBetter, FasterAndBetter, ThinkingLevelInversion, LeaderMiss, UnderdogWin, SharedFailure, DifficultyInversion,
        CriticalErrorConcentration, GraderDisagreementConcentration, JudgeDependentOrder, OutputTrouble, ToolUseOutlier,
        AnswerLengthOutlier, ProviderErrors
    };

    public const int MaxPerKind = 2;
    public const int MaxFindings = 10;

    /// <summary>Cheaper or faster and better: the better model's cost or time is at most this share of the other's.</summary>
    public const double AtMostHalf = 0.5;

    /// <summary>A leader's miss: it scored at most this where the other models' median is at least <see cref="LeaderMissOthersMedianAtLeast"/>.</summary>
    public const double LeaderMissAtMost = 50.0;
    public const double LeaderMissOthersMedianAtLeast = 75.0;
    public const int LeaderMissMinOthers = 2;

    /// <summary>An underdog's win: the lowest-Index model scored at least this many points above every other model.</summary>
    public const double UnderdogMarginPoints = 15.0;

    /// <summary>A shared failure: every covered model scored below this.</summary>
    public const double SharedFailureBelow = 50.0;

    /// <summary>The questions one shared-failure finding names at most.</summary>
    public const int SharedFailureQuestions = 3;

    /// <summary>A difficulty inversion: the advanced-band score at least this many points above the simple-band score…</summary>
    public const double DifficultyInversionPoints = 10.0;

    /// <summary>…each band with at least this many questions.</summary>
    public const int DifficultyInversionMinQuestions = 3;

    /// <summary>Critical errors: at least this many, at a rate at least <see cref="ConcentrationFactor"/> times the others' median.</summary>
    public const int CriticalErrorMinCount = 2;

    /// <summary>Panel disagreements: at least this many, at a rate at least <see cref="ConcentrationFactor"/> times the others' median.</summary>
    public const int DisagreementMinCount = 3;

    public const double ConcentrationFactor = 2.0;

    /// <summary>Output trouble: truncated answers plus tool-limit hits at least this share of the model's answers.</summary>
    public const double OutputTroubleShare = 0.10;

    /// <summary>A tool-use outlier: the others' median tool calls per question is above this.</summary>
    public const double ToolUseMinOthersMedian = 0.5;

    /// <summary>A provider error rate at least this…</summary>
    public const double ProviderErrorRateAtLeast = 0.02;

    /// <summary>…and at least this many times the others' median.</summary>
    public const double ProviderErrorFactor = 5.0;

    /// <summary>What the section printing the findings says the rules are.</summary>
    public const string RulesNote = "Detected by code from the figures, in this order, at most two of a kind and ten in all: "
        + "a model established as scoring higher than another (by a paired test or by 95 % intervals that do not overlap) at no more than half its cost per question, or half its median answer time; "
        + "one model at two thinking levels where the lower level's Intelligence Index is at least the higher's; "
        + "the highest-Index or reference model scoring 50 or less where the other models' median is 75 or more; "
        + "the lowest-Index model scoring at least 15 points above every other model on a question; "
        + "every model scoring below 50 on a question; "
        + "an advanced-band score at least 10 points above the simple-band score, each band with at least three questions; "
        + "at least two critical errors, or three panel disagreements, at twice the other models' median rate or more; "
        + "a pair whose order depends on which panel member grades; "
        + "truncated answers and tool-limit hits on at least 10 % of a model's answers; "
        + "tool calls per question or median answer length at most half or at least twice the other models' median; "
        + "and a provider error rate of at least 2 % and five times the other models' median.";

    /// <summary>What a section prints when no rule found anything.</summary>
    public const string NothingDetected = "Nothing unusual was detected by the rules below.";

    /// <summary>The kind in words, as the <c>notable.&lt;n&gt;.kind</c> fact displays it.</summary>
    public static string KindText(string kind) => kind switch
    {
        CheaperAndBetter => "cheaper and better",
        FasterAndBetter => "faster and better",
        ThinkingLevelInversion => "thinking-level inversion",
        LeaderMiss => "a leading model's miss",
        UnderdogWin => "the lowest-ranked model's win",
        SharedFailure => "a failure every model shares",
        DifficultyInversion => "difficulty inversion",
        CriticalErrorConcentration => "concentrated critical errors",
        GraderDisagreementConcentration => "concentrated grader disagreement",
        JudgeDependentOrder => "judge-dependent order",
        OutputTrouble => "output trouble",
        ToolUseOutlier => "tool-use outlier",
        AnswerLengthOutlier => "answer-length outlier",
        ProviderErrors => "provider errors",
        _ => kind
    };

    /// <summary>The number of notable findings on <paramref name="sheet"/>: its <c>notable.count</c> fact, else its findings list; 0 for neither.</summary>
    public static int CountOf(BenchmarkReportFactSheet sheet)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        var fact = sheet.Facts.FirstOrDefault(f => string.Equals(f.Key, "notable.count", StringComparison.Ordinal));
        if (fact is { Available: true, Value: System.Text.Json.Nodes.JsonValue value } && value.TryGetValue(out int count)) return count;
        return sheet.NotableFindings?.Count ?? 0;
    }

    /// <summary>One detected finding before numbering, with the figure that orders it within its kind (larger first).</summary>
    private sealed record Candidate(string Kind, IReadOnlyList<string> Models, IReadOnlyList<int> Questions, IReadOnlyList<string> Facts, string Summary, double Strength);

    /// <summary>The findings in priority order, numbered from 1.</summary>
    public static List<BenchmarkReportNotableFinding> Detect(BenchmarkNotableFindingsInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var models = (input.Models ?? Array.Empty<BenchmarkNotableModel>()).Where(m => m != null).ToList();
        var questions = (input.Questions ?? Array.Empty<BenchmarkReportQuestion>()).Where(q => q != null).OrderBy(q => q.Number).ToList();
        var families = input.Families ?? Array.Empty<BenchmarkReportPairedFamily>();
        string? reference = input.ReferenceLetter ?? models.FirstOrDefault()?.Letter;

        var byKind = new Dictionary<string, List<Candidate>>(StringComparer.Ordinal)
        {
            [CheaperAndBetter] = BetterAt(models, families, m => m.CostPerQuestion, CheaperAndBetter, "its cost per question", "cost.perQuestion"),
            [FasterAndBetter] = BetterAt(models, families, m => m.ModelTimeP50Ms, FasterAndBetter, "its median answer time", "speed.modelTimeP50"),
            [ThinkingLevelInversion] = Inversions(models, families),
            [LeaderMiss] = LeaderMisses(models, questions, reference, input.ReferenceIsBaselineModel),
            [UnderdogWin] = UnderdogWins(models, questions),
            [SharedFailure] = SharedFailures(models, questions),
            [DifficultyInversion] = DifficultyInversions(models),
            [CriticalErrorConcentration] = Concentrations(models, CriticalErrorConcentration),
            [GraderDisagreementConcentration] = Concentrations(models, GraderDisagreementConcentration),
            [JudgeDependentOrder] = JudgeDependent(models, input.JudgeDependentPairs ?? Array.Empty<(string, string)>()),
            [OutputTrouble] = OutputTroubles(models),
            [ToolUseOutlier] = Outliers(models, ToolUseOutlier),
            [AnswerLengthOutlier] = Outliers(models, AnswerLengthOutlier),
            [ProviderErrors] = ProviderErrorRates(models)
        };

        var findings = new List<BenchmarkReportNotableFinding>();
        foreach (string kind in Kinds)
        {
            foreach (var candidate in byKind[kind]
                .OrderByDescending(c => c.Strength)
                .ThenBy(c => string.Join(",", c.Models), StringComparer.Ordinal)
                .ThenBy(c => c.Questions.Count == 0 ? 0 : c.Questions[0])
                .Take(MaxPerKind))
            {
                if (findings.Count >= MaxFindings) return findings;
                findings.Add(new BenchmarkReportNotableFinding
                {
                    Number = findings.Count + 1,
                    Kind = candidate.Kind,
                    Models = candidate.Models.ToList(),
                    Questions = candidate.Questions.ToList(),
                    Facts = candidate.Facts.Distinct(StringComparer.Ordinal).ToList(),
                    Summary = candidate.Summary
                });
            }
        }
        return findings;
    }

    // ---------------------------------------------------------------------------------------------
    // Rules
    // ---------------------------------------------------------------------------------------------

    /// <summary>X is established above Y on quality: by which fact, and <c>established by …</c> in words; null when it is not.</summary>
    private static (string Fact, string How)? EstablishedAbove(
        BenchmarkNotableModel x, BenchmarkNotableModel y, IReadOnlyList<BenchmarkReportPairedFamily> families)
    {
        var (first, second) = Ordered(x.Letter, y.Letter);
        foreach (var family in families)
        {
            var intelligence = family.Measures.FirstOrDefault(m => m.Measure == BenchmarkPairedTests.IntelligenceMeasure);
            var pair = intelligence?.Pairs.FirstOrDefault(p => p.FirstLetter == first && p.SecondLetter == second);
            if (pair == null || !pair.Established || pair.Favors == "None") continue;

            string winner = pair.Favors == "First" ? pair.FirstLetter : pair.SecondLetter;
            if (winner != x.Letter) return null;

            string name = family.Mode == "AllPairs" ? BenchmarkComparisonReportFacts.AllPairsFamilyName : BenchmarkComparisonReportFacts.ReferenceFamilyName;
            return (BenchmarkComparisonReportFacts.PairPrefix(first, second) + "quality." + name, "established by a paired test after its adjustment");
        }

        if (x.QualityLower is double xLower && y.QualityUpper is double yUpper && xLower > yUpper)
        {
            return (BenchmarkComparisonReportFacts.PairPrefix(first, second) + "intervalOverlap", "established by 95 % intervals that do not overlap");
        }
        return null;
    }

    /// <summary>Cheaper and better, or faster and better: X established above Y at no more than half Y's figure.</summary>
    private static List<Candidate> BetterAt(
        IReadOnlyList<BenchmarkNotableModel> models, IReadOnlyList<BenchmarkReportPairedFamily> families,
        Func<BenchmarkNotableModel, double?> figure, string kind, string what, string factKey)
    {
        var found = new List<Candidate>();
        foreach (var x in models)
        {
            foreach (var y in models.Where(m => m.Letter != x.Letter))
            {
                if (figure(x) is not double fx || figure(y) is not double fy || fy <= 0 || fx > fy * AtMostHalf) continue;
                if (EstablishedAbove(x, y, families) is not { } basis) continue;

                double ratio = fx / fy;
                found.Add(new Candidate(kind, Letters(x.Letter, y.Letter), Array.Empty<int>(),
                    new[] { basis.Fact, Model(x, "quality.index"), Model(y, "quality.index"), Model(x, factKey), Model(y, factKey) },
                    $"Model {x.Letter} scored higher than Model {y.Letter} ({basis.How}) at {Ratio(ratio)} times {what}.",
                    1 - ratio));
            }
        }
        return found;
    }

    /// <summary>One model at two thinking levels whose lower level's Intelligence Index is at least the higher's.</summary>
    private static List<Candidate> Inversions(IReadOnlyList<BenchmarkNotableModel> models, IReadOnlyList<BenchmarkReportPairedFamily> families)
    {
        var found = new List<Candidate>();
        foreach (var low in models)
        {
            foreach (var high in models.Where(m => m.Letter != low.Letter))
            {
                if (!SameModel(low, high) || CompareLevels(low.ThinkingLevel, high.ThinkingLevel) is not < 0) continue;
                if (low.Quality is not double lowQuality || high.Quality is not double highQuality || lowQuality < highQuality) continue;

                string order = EstablishedAbove(low, high, families) is { } basis
                    ? "the order is " + basis.How
                    : "the order between them is not established";
                var facts = new List<string> { Model(low, "quality.index"), Model(high, "quality.index"), Model(low, "thinkingLevel"), Model(high, "thinkingLevel") };

                var costs = new List<string>();
                if (low.ModelTimeP50Ms is double lt && high.ModelTimeP50Ms is double ht && ht > 0)
                {
                    costs.Add("took " + Ratio(lt / ht) + " times as long");
                    facts.Add(Model(low, "speed.modelTimeP50"));
                    facts.Add(Model(high, "speed.modelTimeP50"));
                }
                if (low.CostPerQuestion is double lc && high.CostPerQuestion is double hc && hc > 0)
                {
                    costs.Add("cost " + Ratio(lc / hc) + " times as much per question");
                    facts.Add(Model(low, "cost.perQuestion"));
                    facts.Add(Model(high, "cost.perQuestion"));
                }

                string summary = $"Model {low.Letter}, at thinking level {low.ThinkingLevel!.Trim()}, has an Intelligence Index of {BenchmarkReportFormat.Whole(lowQuality)}, "
                    + $"at least that of Model {high.Letter}, the same model at thinking level {high.ThinkingLevel!.Trim()} ({BenchmarkReportFormat.Whole(highQuality)}); {order}."
                    + (costs.Count > 0 ? $" Model {low.Letter} {BenchmarkReportFormat.LetterList(costs)}." : string.Empty);
                found.Add(new Candidate(ThinkingLevelInversion, Letters(low.Letter, high.Letter), Array.Empty<int>(), facts, summary, lowQuality - highQuality));
            }
        }
        return found;
    }

    /// <summary>The same provider and model id, trimmed and ignoring case.</summary>
    private static bool SameModel(BenchmarkNotableModel a, BenchmarkNotableModel b)
        => !string.IsNullOrWhiteSpace(a.ModelId)
           && string.Equals(a.Provider?.Trim(), b.Provider?.Trim(), StringComparison.OrdinalIgnoreCase)
           && string.Equals(a.ModelId.Trim(), b.ModelId?.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>The named thinking levels from the lowest up.</summary>
    private static readonly IReadOnlyDictionary<string, int> LevelRanks = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
    {
        ["none"] = 0,
        ["off"] = 0,
        ["disabled"] = 0,
        ["minimal"] = 1,
        ["low"] = 2,
        ["medium"] = 3,
        ["high"] = 4,
        ["xhigh"] = 5,
        ["max"] = 6
    };

    /// <summary>
    /// Below zero when level <paramref name="a"/> is lower than <paramref name="b"/>, above when higher, zero
    /// when equal; null when either is unset or the two cannot be ordered (a name against a token budget).
    /// </summary>
    public static int? CompareLevels(string? a, string? b)
    {
        string x = a?.Trim() ?? string.Empty;
        string y = b?.Trim() ?? string.Empty;
        if (x.Length == 0 || y.Length == 0) return null;

        if (LevelRanks.TryGetValue(x, out int rx) && LevelRanks.TryGetValue(y, out int ry)) return rx.CompareTo(ry);
        if (long.TryParse(x, NumberStyles.None, CultureInfo.InvariantCulture, out long nx)
            && long.TryParse(y, NumberStyles.None, CultureInfo.InvariantCulture, out long ny))
        {
            return nx.CompareTo(ny);
        }
        return null;
    }

    /// <summary>The highest-Index model, or the reference, scored at most 50 where the other models' median is at least 75.</summary>
    private static List<Candidate> LeaderMisses(
        IReadOnlyList<BenchmarkNotableModel> models, IReadOnlyList<BenchmarkReportQuestion> questions, string? reference, bool referenceIsBaseline)
    {
        var found = new List<Candidate>();
        if (models.Count == 0) return found;

        var leaders = new List<(string Letter, string Role)> { (models[0].Letter, "the highest Intelligence Index") };
        if (reference != null && reference != models[0].Letter)
        {
            leaders.Add((reference, referenceIsBaseline ? "the baseline model" : "the reference model"));
        }

        foreach (var (letter, role) in leaders)
        {
            foreach (var question in questions)
            {
                var cells = Scored(question);
                if (!cells.TryGetValue(letter, out double score) || score > LeaderMissAtMost) continue;

                var others = cells.Where(c => c.Key != letter).Select(c => c.Value).ToList();
                if (others.Count < LeaderMissMinOthers || BenchmarkComparisonModelProfile.Median(others) is not double median || median < LeaderMissOthersMedianAtLeast) continue;

                found.Add(new Candidate(LeaderMiss, new[] { letter }, new[] { question.Number }, new[] { "model." + letter + ".quality.index" },
                    $"Model {letter}, {role}, scored {BenchmarkReportFormat.Whole(score)} on {QuestionLabel(question)}, where the other models' median was {BenchmarkReportFormat.Whole(median)}.",
                    median - score));
            }
        }
        return found;
    }

    /// <summary>The lowest-Index model scored at least 15 points above every other model on a question.</summary>
    private static List<Candidate> UnderdogWins(IReadOnlyList<BenchmarkNotableModel> models, IReadOnlyList<BenchmarkReportQuestion> questions)
    {
        var found = new List<Candidate>();
        var underdog = models.LastOrDefault(m => m.Quality.HasValue);
        if (underdog == null || models.Count(m => m.Quality.HasValue) < 2) return found;

        foreach (var question in questions)
        {
            var cells = Scored(question);
            if (!cells.TryGetValue(underdog.Letter, out double score)) continue;

            var others = cells.Where(c => c.Key != underdog.Letter).Select(c => c.Value).ToList();
            if (others.Count == 0 || score < others.Max() + UnderdogMarginPoints) continue;

            found.Add(new Candidate(UnderdogWin, new[] { underdog.Letter }, new[] { question.Number }, new[] { Model(underdog, "quality.index") },
                $"Model {underdog.Letter}, the lowest Intelligence Index, scored {BenchmarkReportFormat.Whole(score)} on {QuestionLabel(question)}, "
                + $"at least {BenchmarkReportFormat.Whole(UnderdogMarginPoints)} points above every other model (the next best scored {BenchmarkReportFormat.Whole(others.Max())}).",
                score - others.Max()));
        }
        return found;
    }

    /// <summary>Every covered model scored below 50 on a question, up to three questions a finding, the lowest best score first.</summary>
    private static List<Candidate> SharedFailures(IReadOnlyList<BenchmarkNotableModel> models, IReadOnlyList<BenchmarkReportQuestion> questions)
    {
        var found = new List<Candidate>();
        if (models.Count < 2) return found;

        var failed = questions
            .Select(q => (Question: q, Cells: Scored(q)))
            .Where(x => models.All(m => x.Cells.ContainsKey(m.Letter)) && x.Cells.Values.All(s => s < SharedFailureBelow))
            .OrderBy(x => x.Cells.Values.Max())
            .ThenBy(x => x.Question.Number)
            .ToList();

        var letters = models.Select(m => m.Letter).ToList();
        for (int i = 0; i < failed.Count; i += SharedFailureQuestions)
        {
            var group = failed.Skip(i).Take(SharedFailureQuestions).OrderBy(x => x.Question.Number).ToList();
            found.Add(new Candidate(SharedFailure, letters, group.Select(x => x.Question.Number).ToList(), new[] { "questions.sharedLow" },
                $"Every model scored below {BenchmarkReportFormat.Whole(SharedFailureBelow)} on {BenchmarkReportFormat.LetterList(group.Select(x => QuestionLabel(x.Question)).ToList())}: "
                + "a likely chat, corpus or question problem rather than one model's.",
                -i));
        }
        return found;
    }

    /// <summary>A model's advanced-band score at least 10 points above its simple-band score, each band with at least three questions.</summary>
    private static List<Candidate> DifficultyInversions(IReadOnlyList<BenchmarkNotableModel> models)
    {
        var found = new List<Candidate>();
        foreach (var m in models)
        {
            if (m.AdvancedBandScore is not double advanced || m.SimpleBandScore is not double simple) continue;
            if (m.AdvancedBandQuestions < DifficultyInversionMinQuestions || m.SimpleBandQuestions < DifficultyInversionMinQuestions) continue;
            if (advanced - simple < DifficultyInversionPoints) continue;

            found.Add(new Candidate(DifficultyInversion, new[] { m.Letter }, Array.Empty<int>(),
                new[] { Model(m, "band.advanced.score"), Model(m, "band.simple.score") },
                $"Model {m.Letter} scored {BenchmarkReportFormat.Whole(advanced)} on advanced questions and {BenchmarkReportFormat.Whole(simple)} on simple ones, "
                + $"{BenchmarkReportFormat.WholeDifference(advanced, simple).TrimStart('+')} points higher on the harder band.",
                advanced - simple));
        }
        return found;
    }

    /// <summary>Critical errors or panel disagreements: at least the minimum count, at no less than twice the other models' median rate.</summary>
    private static List<Candidate> Concentrations(IReadOnlyList<BenchmarkNotableModel> models, string kind)
    {
        bool critical = kind == CriticalErrorConcentration;
        (int Count, int Of)? CountsOf(BenchmarkNotableModel m)
        {
            int? count = critical ? m.CriticalErrors : m.PanelDisagreements;
            int? of = critical ? m.CountingAnswers : m.PanelAnswers;
            if (count is not int c || of is not int n || n <= 0) return null;
            return (c, n);
        }

        var found = new List<Candidate>();
        foreach (var m in models)
        {
            if (CountsOf(m) is not { } own || own.Count < (critical ? CriticalErrorMinCount : DisagreementMinCount)) continue;

            var others = models
                .Where(o => o.Letter != m.Letter)
                .Select(CountsOf)
                .Where(o => o.HasValue)
                .Select(o => (double)o!.Value.Count / o.Value.Of)
                .ToList();
            if (others.Count == 0 || BenchmarkComparisonModelProfile.Median(others) is not double median) continue;

            double rate = (double)own.Count / own.Of;
            if (rate < median * ConcentrationFactor) continue;

            string against = median > 0
                ? Ratio(rate / median) + " times the other models' median rate"
                : "while the other models' median rate is zero";
            string summary = critical
                ? $"Model {m.Letter} made {Inv(own.Count)} critical errors in {Inv(own.Of)} answers, {against}."
                : $"The panel disagreed on {Inv(own.Count)} of Model {m.Letter}'s {Inv(own.Of)} answers, {against}, so its Intelligence Index is less certain.";
            found.Add(new Candidate(kind, new[] { m.Letter }, Array.Empty<int>(),
                new[] { Model(m, critical ? "errors.critical" : "panel.disagreements") }, summary, rate));
        }
        return found;
    }

    /// <summary>A pair whose order depends on which panel member grades.</summary>
    private static List<Candidate> JudgeDependent(IReadOnlyList<BenchmarkNotableModel> models, IReadOnlyList<(string First, string Second)> pairs)
    {
        var letters = models.Select(m => m.Letter).ToHashSet(StringComparer.Ordinal);
        return pairs
            .Where(p => letters.Contains(p.First) && letters.Contains(p.Second) && p.First != p.Second)
            .Select(p => Ordered(p.First, p.Second))
            .Distinct()
            .Select(p => new Candidate(JudgeDependentOrder, Letters(p.First, p.Second), Array.Empty<int>(),
                new[]
                {
                    "model." + p.First + ".panel.memberAAlone", "model." + p.First + ".panel.memberBAlone",
                    "model." + p.Second + ".panel.memberAAlone", "model." + p.Second + ".panel.memberBAlone",
                    BenchmarkComparisonReportFacts.PairPrefix(p.First, p.Second) + "intervalOverlap"
                },
                $"Which of Model {p.First} and Model {p.Second} scored higher depends on which panel member grades: member A alone and member B alone order them differently.",
                0))
            .ToList();
    }

    /// <summary>Truncated answers plus tool-limit hits on at least 10 % of a model's answers.</summary>
    private static List<Candidate> OutputTroubles(IReadOnlyList<BenchmarkNotableModel> models)
    {
        var found = new List<Candidate>();
        foreach (var m in models)
        {
            if (m.Answers is not int answers || answers <= 0 || m.Truncated is not int truncated || m.ToolLimitHits is not int hits) continue;

            double share = (double)(truncated + hits) / answers;
            if (truncated + hits == 0 || share < OutputTroubleShare) continue;

            found.Add(new Candidate(OutputTrouble, new[] { m.Letter }, Array.Empty<int>(),
                new[] { Model(m, "profile.truncated"), Model(m, "profile.toolLimitHits"), Model(m, "profile.answers") },
                $"Model {m.Letter} had {Inv(truncated)} truncated {(truncated == 1 ? "answer" : "answers")} and {Inv(hits)} tool-limit {(hits == 1 ? "hit" : "hits")} "
                + $"in {Inv(answers)} answers ({BenchmarkReportFormat.Percent(share * 100)}).",
                share));
        }
        return found;
    }

    /// <summary>Tool calls per question or median answer length at most half or at least twice the other models' median.</summary>
    private static List<Candidate> Outliers(IReadOnlyList<BenchmarkNotableModel> models, string kind)
    {
        bool tools = kind == ToolUseOutlier;
        double? Of(BenchmarkNotableModel m) => tools ? m.ToolCallsPerQuestion : m.MedianAnswerChars;

        var found = new List<Candidate>();
        foreach (var m in models)
        {
            if (Of(m) is not double own) continue;

            var others = models.Where(o => o.Letter != m.Letter).Select(Of).Where(v => v.HasValue).Select(v => v!.Value).ToList();
            if (others.Count == 0 || BenchmarkComparisonModelProfile.Median(others) is not double median || median <= 0) continue;
            if (tools && median <= ToolUseMinOthersMedian) continue;

            bool low = own <= median * AtMostHalf;
            bool high = own >= median / AtMostHalf;
            if (!low && !high) continue;

            string side = low ? "at most half" : "at least twice";
            string summary = tools
                ? $"Model {m.Letter} made {BenchmarkReportFormat.OneDecimal(own)} tool calls per question, {side} the other models' median of {BenchmarkReportFormat.OneDecimal(median)}."
                : $"Model {m.Letter}'s median answer is {BenchmarkReportFormat.Count(own)} characters, {side} the other models' median of {BenchmarkReportFormat.Count(median)}.";
            found.Add(new Candidate(kind, new[] { m.Letter }, Array.Empty<int>(),
                new[] { Model(m, tools ? "tools.callsPerQuestion" : "profile.answerChars.median") }, summary,
                Math.Abs(Math.Log(Math.Max(own, 1e-9) / median))));
        }
        return found;
    }

    /// <summary>A provider error rate of at least 2 % and at least five times the other models' median.</summary>
    private static List<Candidate> ProviderErrorRates(IReadOnlyList<BenchmarkNotableModel> models)
    {
        var found = new List<Candidate>();
        foreach (var m in models)
        {
            if (m.ProviderErrorRate is not double rate || rate < ProviderErrorRateAtLeast) continue;

            var others = models.Where(o => o.Letter != m.Letter && o.ProviderErrorRate.HasValue).Select(o => o.ProviderErrorRate!.Value).ToList();
            if (others.Count == 0 || BenchmarkComparisonModelProfile.Median(others) is not double median || rate < median * ProviderErrorFactor) continue;

            found.Add(new Candidate(ProviderErrors, new[] { m.Letter }, Array.Empty<int>(),
                new[] { Model(m, "availability.providerErrorRate"), Model(m, "availability.providerErrors") },
                $"Model {m.Letter} met provider errors on {BenchmarkProviderAvailability.FormatPercent(rate)} of its attempts, at least five times the other models' median of {BenchmarkProviderAvailability.FormatPercent(median)}.",
                rate));
        }
        return found;
    }

    // ---------------------------------------------------------------------------------------------
    // Facts
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// <c>notable.count</c>, and for each finding <c>notable.&lt;n&gt;.kind</c>, <c>.models</c>,
    /// <c>.questions</c> and <c>.summary</c>; <paramref name="questionLabel"/> names a matrix question.
    /// </summary>
    internal static void AddFacts(BenchmarkReportFacts.FactList facts, IReadOnlyList<BenchmarkReportNotableFinding> findings, Func<int, string> questionLabel)
    {
        ArgumentNullException.ThrowIfNull(findings);
        facts.Add("notable.count", findings.Count, findings.Count switch
        {
            0 => "none detected",
            1 => "1 notable finding",
            _ => Inv(findings.Count) + " notable findings"
        });

        foreach (var finding in findings)
        {
            string prefix = "notable." + Inv(finding.Number) + ".";
            facts.Add(prefix + "kind", finding.Kind, KindText(finding.Kind));
            facts.Add(prefix + "models", finding.Models.Count, BenchmarkComparisonReportFacts.ModelsText(finding.Models));
            facts.Add(prefix + "questions", finding.Questions.Count,
                finding.Questions.Count == 0 ? "none" : string.Join(", ", finding.Questions.Select(questionLabel)));
            facts.Add(prefix + "summary", finding.Summary, finding.Summary);
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------------------------

    /// <summary>The scored cells of a question, by letter.</summary>
    private static Dictionary<string, double> Scored(BenchmarkReportQuestion question)
        => (question.Models ?? new List<BenchmarkReportQuestionModelScore>())
            .Where(c => c.Score.HasValue)
            .GroupBy(c => c.Letter, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().Score!.Value, StringComparer.Ordinal);

    /// <summary>A battery question's reference, else <c>Q&lt;n&gt;</c>.</summary>
    public static string QuestionLabel(BenchmarkReportQuestion question)
        => !string.IsNullOrWhiteSpace(question.Reference) ? question.Reference! : "Q" + Inv(question.Number);

    /// <summary>The two letters, the earlier first: shorter letters first, then ordinal.</summary>
    private static (string First, string Second) Ordered(string a, string b)
        => a.Length != b.Length ? (a.Length < b.Length ? (a, b) : (b, a)) : (string.CompareOrdinal(a, b) <= 0 ? (a, b) : (b, a));

    private static IReadOnlyList<string> Letters(string a, string b)
    {
        var (first, second) = Ordered(a, b);
        return new[] { first, second };
    }

    private static string Model(BenchmarkNotableModel model, string key) => BenchmarkComparisonReportFacts.ModelPrefix(model.Letter) + key;

    /// <summary>A ratio with two decimals.</summary>
    private static string Ratio(double value)
        => Math.Round(value, 2, MidpointRounding.AwayFromZero).ToString("0.00", CultureInfo.InvariantCulture);

    private static string Inv(int value) => value.ToString(CultureInfo.InvariantCulture);
}
