namespace Overseer.Services.Benchmarking.RootCause;

using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// One term of the Root Cause Analysis glossary. <see cref="Definition"/> is one to three plain sentences and
/// may hold digits, since only code prints it. <see cref="Gloss"/> is at most twelve words with no digit, worded
/// to follow the term in parentheses, with no causal or certainty word, so the report writer may repeat it in any
/// slot without breaking a claim rule.
/// </summary>
public sealed record RootCauseTerm(string Key, string Name, string Definition, string Gloss);

/// <summary>
/// The terms a Root Cause Analysis explains: in its Key terms section, in Appendix H, at their first use in
/// the template text, and to the report writer as the TERMS block of its prompt.
/// </summary>
public static class RootCauseGlossary
{
    public const string IntelligenceIndex = "intelligenceIndex";
    public const string AnswerQuality = "answerQuality";
    public const string Dimension = "dimension";
    public const string ScoringLevels = "scoringLevels";
    public const string GeometricMean = "geometricMean";
    public const string DifficultyWeighted = "difficultyWeighted";
    public const string CriticalError = "criticalError";
    public const string PanelMember = "panelMember";
    public const string ReferenceReader = "referenceReader";
    public const string ClaimVerifier = "claimVerifier";
    public const string PanelDisagreement = "panelDisagreement";
    public const string NoToolAnswers = "noToolAnswers";
    public const string Interval95 = "interval95";
    public const string TiedRank = "tiedRank";
    public const string PairedTest = "pairedTest";
    public const string PValue = "pValue";
    public const string Holm = "holm";
    public const string NullHypothesis = "nullHypothesis";
    public const string GradeWords = "gradeWords";
    public const string Hypothesis = "hypothesis";
    public const string RobustnessCheck = "robustnessCheck";
    public const string ReplicateRun = "replicateRun";
    public const string LeaveOneOut = "leaveOneOut";
    public const string Confound = "confound";
    public const string Locus = "locus";
    public const string PointsAtRisk = "pointsAtRisk";
    public const string IsolatedMiss = "isolatedMiss";
    public const string MedianAndMean = "medianAndMean";
    public const string TimeToFirstToken = "timeToFirstToken";
    public const string OutputGeneration = "outputGeneration";
    public const string ToolRound = "toolRound";
    public const string Tokens = "tokens";
    public const string PricingBasis = "pricingBasis";
    public const string Ratio = "ratio";
    public const string DecompositionShare = "decompositionShare";
    public const string BootstrapInterval = "bootstrapInterval";
    public const string BaselineModel = "baselineModel";
    public const string ReferenceModel = "referenceModel";
    public const string Suite = "suite";
    public const string Battery = "battery";
    public const string RunsPerSuite = "runsPerSuite";
    public const string OverallIndex = "overallIndex";
    public const string ResponseStyle = "responseStyle";
    public const string Control = "control";

    /// <summary>Every term, grouped by subject: scoring, grading, statistics, grades and causes, speed and cost, the design.</summary>
    public static IReadOnlyList<RootCauseTerm> Terms { get; } = new[]
    {
        new RootCauseTerm(IntelligenceIndex, "Intelligence Index",
            "A model's overall score for answer quality, from 0 to 100. It is the mean quality of the model's answers, with harder questions counting for more.",
            "the overall answer-quality score, out of a hundred"),
        new RootCauseTerm(AnswerQuality, "Answer quality",
            "The score of one answer, from 0 to 100. It combines the four dimension scores in a weighted geometric mean and is capped when the answer has a critical error.",
            "the score of one answer, from its four graded dimensions"),
        new RootCauseTerm(Dimension, "Dimension",
            "One of the four qualities each answer is graded on: Accuracy, Completeness, Conciseness and Readability. Each has its own weight in the answer's quality.",
            "one of the four things graded: accuracy, completeness, conciseness, readability"),
        new RootCauseTerm(ScoringLevels, "Scoring levels",
            "Each dimension is graded on a fixed scale of levels, each described by the answer behavior that earns it (behaviorally anchored levels). Each level stands for a fixed score.",
            "fixed grade steps, each described by the answer behavior that earns it"),
        new RootCauseTerm(GeometricMean, "Weighted geometric mean",
            "The way an answer's four dimension scores are combined. Unlike a plain average, it falls sharply when any one dimension is very low, so strong dimensions cannot hide a weak one.",
            "an average that one very low score pulls down sharply"),
        new RootCauseTerm(DifficultyWeighted, "Difficulty-weighted mean",
            "The average of answer quality over the questions, with harder questions counting for more. The Intelligence Index is computed this way.",
            "an average in which harder questions count for more"),
        new RootCauseTerm(CriticalError, "Critical error",
            "A mistake the graders judge serious, such as a wrong game rule stated as fact. It caps the answer's quality, whatever the answer's other scores.",
            "a mistake serious enough to cap the answer's score"),
        new RootCauseTerm(PanelMember, "Panel member",
            "In a panel run, two AI graders, Panel member A and Panel member B, score each answer. The answer's score is the mean of their two scores.",
            "one of the two AI graders whose scores are averaged"),
        new RootCauseTerm(ReferenceReader, "Reference reader",
            "A third AI grader that scores the same answers as a check on the panel. Its scores do not count toward any result.",
            "a third AI grader whose scores serve as a check only"),
        new RootCauseTerm(ClaimVerifier, "Claim verifier",
            "An AI model that checks single statements, in answers or in grader comments, against the game's source code and wiki. Its rulings are advisory and can be wrong.",
            "an AI model that checks single statements against the game's sources"),
        new RootCauseTerm(PanelDisagreement, "Panel disagreement",
            "An answer on which the two panel members' quality scores differ by more than 15 points, or on which only one of them found a critical error. Its rate is a share of the answers both members graded.",
            "the two panel members scoring the same answer far apart"),
        new RootCauseTerm(NoToolAnswers, "Answers without tools",
            "Answers for which the model called no tool, such as the wiki search or the source code search, and answered from its own knowledge alone.",
            "answers the model wrote without looking anything up"),
        new RootCauseTerm(Interval95, "95% interval",
            "A range built so that, if the questions were drawn again and again, 95 of 100 such ranges would contain the true value. When two models' ranges overlap, the ranges alone cannot say which is higher.",
            "the range the true value most likely lies in"),
        new RootCauseTerm(TiedRank, "Tied rank",
            "When a model's 95% interval overlaps a neighbor's, the data cannot put them in order, so they share a rank. The rank then shows the span of places the tied group covers, such as 2–9 (tied).",
            "a shared rank for models the data cannot put in order"),
        new RootCauseTerm(PairedTest, "Paired test",
            "A comparison of two models question by question, on the questions both answered, so that how hard each question is counts the same for both.",
            "a comparison of two models on the same questions"),
        new RootCauseTerm(PValue, "p-value",
            "The chance of a difference at least this large if the two models were really equal. After the Holm adjustment, a p-value below 0.05 counts as a real difference.",
            "how likely a gap this large would be by chance alone"),
        new RootCauseTerm(Holm, "Holm adjustment",
            "When many pairs are compared at once, some differences look real by chance. The Holm adjustment raises the bar for each comparison, so the whole set keeps at most a one-in-twenty chance of any false finding.",
            "a correction for running many comparisons at once"),
        new RootCauseTerm(NullHypothesis, "Null hypothesis",
            "The starting assumption that a difference, between two models or between two groups of questions, is only chance. A paired test, or else a bootstrap interval that excludes zero, can reject it. A null that stands does not prove that the two are equal.",
            "the starting assumption that a difference is only chance"),
        new RootCauseTerm(GradeWords, "Grade words",
            "Established (High) means measured, still clear after the correction for many comparisons, and robust in every check, with no open confound and two or more runs per suite. "
            + "Probable (Moderate) misses exactly one of those conditions. Suggestive (Low) is a lead only. Ruled out (Not supported) means the hypothesis's own test contradicts it.",
            "how sure the document is of a cause, strongest to weakest"),
        new RootCauseTerm(Hypothesis, "Hypothesis",
            "A possible cause of a difference, raised by a rule fixed in advance and tested by code against the data. Each hypothesis states what would refute it and receives a grade.",
            "a possible cause that code tested against the data"),
        new RootCauseTerm(RobustnessCheck, "Robustness check",
            "A test of whether a hypothesis still holds when the data changes a little: one question left out, another suite, a second run, the other panel member, or another setting.",
            "a test of whether a result survives a small change"),
        new RootCauseTerm(ReplicateRun, "Replicate run",
            "Running the same model on the same questions again. With only one run per suite, the document cannot tell whether a result would repeat, which caps every grade at Probable.",
            "a second run of the same model, to see if results repeat"),
        new RootCauseTerm(LeaveOneOut, "Leave-one-out check",
            "A robustness check that repeats a result with each question removed in turn. If the result rests on a single question, the check fails.",
            "rechecking a result with each question left out in turn"),
        new RootCauseTerm(Confound, "Confound",
            "A setting other than the model itself that differs between the two models a hypothesis compares, such as the response style. While it is open, its effect cannot be told apart from the model's.",
            "another difference between two models that could account for a result"),
        new RootCauseTerm(Locus, "Locus",
            "Where a hypothesis places the cause: the model itself; the Overseer chat prompt or its tools (chat); the grading (harness); a question or its rubric (suite); the data a tool reads (corpus); or the serving (provider).",
            "where a cause lies: model, chat, grading, questions, data or provider"),
        new RootCauseTerm(PointsAtRisk, "Points at risk",
            "The index points a model lost on its answers below its own median answer, each answer counted once under its first signal, such as no tool call, a defect or a critical error. "
            + "They are an upper bound on what each signal costs, not an estimate of its effect.",
            "the most index points a failure signal could have cost"),
        new RootCauseTerm(IsolatedMiss, "Isolated miss",
            "A question a model scored 50 or less on while the other models' median score was 75 or more.",
            "a question one model missed while the others did well"),
        new RootCauseTerm(MedianAndMean, "Median and mean",
            "The median is the middle value once all values are sorted, so a few very slow or costly questions barely move it. The mean is the plain average, which they do move.",
            "the middle value, and the plain average"),
        new RootCauseTerm(TimeToFirstToken, "Time to first token",
            "The seconds from sending a request to the first word of the reply. For reasoning models it is mostly the time the model spends thinking before it answers.",
            "the wait before the model starts writing, mostly its thinking"),
        new RootCauseTerm(OutputGeneration, "Output generation and decode rate",
            "Output generation is the time the model spends writing its reply once it has started. The decode rate is how fast it writes, in tokens per second.",
            "the time spent writing the reply, and how fast it is written"),
        new RootCauseTerm(ToolRound, "Tool round",
            "One cycle in which the model calls a tool, such as the wiki search, reads its result and continues. Each round adds a model call, with its own wait for the first token.",
            "one cycle of calling a tool and reading its result"),
        new RootCauseTerm(Tokens, "Tokens",
            "The pieces of text, roughly three quarters of a word each, that a model reads and writes; providers price by them. Uncached input is text read at the full price. "
            + "Cache reads are input stored from an earlier call and charged less; cache writes store it. Visible output is the answer, and reasoning is the model's hidden thinking, billed as output.",
            "the word pieces a model reads and writes, which set its price"),
        new RootCauseTerm(PricingBasis, "Pricing basis",
            "The prices per token used to turn token counts into dollars: today's catalog prices, or the prices stored with each run. Costs on different bases are not comparable.",
            "which prices turned the token counts into dollars"),
        new RootCauseTerm(Ratio, "Time ratio and spend ratio",
            "One model's own time, or spend, per question divided by the other's, on the questions both answered. A ratio of 1.50 means half as much again, and 0.50 means half.",
            "one model's time or cost divided by another's"),
        new RootCauseTerm(DecompositionShare, "Decomposition share",
            "The part of a time or cost difference that one component accounts for, found by a standard method (the logarithmic-mean Divisia index). The parts always add up to the whole difference, "
            + "so one part can be larger than the whole when another pulls the other way: a 2 s gap can be 3 s from one part and −1 s from another.",
            "how much of a difference one part accounts for"),
        new RootCauseTerm(BootstrapInterval, "Bootstrap interval",
            "An interval found by redrawing the questions at random, with repeats, many times and recomputing the figure each time. The middle 95 of every 100 recomputed figures form the interval.",
            "a range found by resampling the questions many times"),
        new RootCauseTerm(BaselineModel, "Baseline model",
            "The model a comparison marks as its standard, so that the others can be read against it. Where it is also the reference model, every other model is paired-tested against it.",
            "the model the comparison marks as the standard to compare with"),
        new RootCauseTerm(ReferenceModel, "Reference model",
            "The model each other model is paired-tested against: the one chosen for the comparison, or else the model with the highest Intelligence Index.",
            "the model every other model is tested against"),
        new RootCauseTerm(Suite, "Suite",
            "A fixed set of benchmark questions, each with its rubric. A run answers every question of one suite once.",
            "a fixed set of benchmark questions with their rubrics"),
        new RootCauseTerm(Battery, "Battery",
            "A set of suites run together. Each suite is scored on its own, and the battery's Overall Index combines them under the battery's weights.",
            "several suites run together and scored as one"),
        new RootCauseTerm(RunsPerSuite, "Runs per suite",
            "How many times a model answered every question of a suite. With one run per suite, the data cannot show whether a result would repeat.",
            "how many times each model answered each suite"),
        new RootCauseTerm(OverallIndex, "Overall Index",
            "A battery's combined score: each suite's Intelligence Index times the suite's weight, summed over the suites. It is not comparable with a single suite's index.",
            "a battery's score: its suites' Intelligence Indexes, weighted and added"),
        new RootCauseTerm(ResponseStyle, "Response style",
            "The production chat asks the model for concise answers; a run can ask for detailed ones instead. Completeness is graded under the style the run used, and only a pair of runs in both styles shows how much the style matters.",
            "whether the chat asks for concise or detailed answers"),
        new RootCauseTerm(Control, "Control",
            "An earlier run of another model that answered the same questions under the same graders and settings. Controls show how hard each question is, which helps tell the model from the instrument.",
            "an earlier run of another model on the same questions")
    };

    private static readonly IReadOnlyDictionary<string, RootCauseTerm> ByKey = Terms.ToDictionary(t => t.Key, StringComparer.Ordinal);

    /// <summary>The term of <paramref name="key"/>; an unknown key throws, so a misspelled key fails every render that uses it.</summary>
    public static RootCauseTerm Get(string key)
        => ByKey.TryGetValue(key, out var term) ? term : throw new ArgumentException($"No glossary term has the key '{key}'.", nameof(key));

    /// <summary>Whether <paramref name="key"/> names a term.</summary>
    public static bool Contains(string key) => key != null && ByKey.ContainsKey(key);

    /// <summary>The terms of <paramref name="keys"/>, alphabetically by name; an unknown key throws.</summary>
    public static IReadOnlyList<RootCauseTerm> Alphabetical(IEnumerable<string> keys)
        => keys
            .Distinct(StringComparer.Ordinal)
            .Select(Get)
            .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(t => t.Name, StringComparer.Ordinal)
            .ToList();
}
