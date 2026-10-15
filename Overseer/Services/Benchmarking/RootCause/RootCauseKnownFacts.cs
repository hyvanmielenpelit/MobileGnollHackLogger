namespace Overseer.Services.Benchmarking.RootCause;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

/// <summary>
/// The known facts of § A3: numbered sentences, each produced by a rule from the computed figures and
/// each stating only what a test, a count, a decomposition or the configuration proves. None is a
/// cross-model correlation, and none says why. Pure and deterministic.
/// </summary>
public static class RootCauseKnownFacts
{
    /// <summary>The most known facts a result carries.</summary>
    public const int MaxFacts = 24;

    public const string KindTest = "test";
    public const string KindCount = "count";
    public const string KindDecomposition = "decomposition";
    public const string KindConfiguration = "configuration";

    internal static List<RootCauseKnownFact> Build(RootCauseContext ctx, RootCauseResult result)
    {
        var facts = new List<RootCauseKnownFact>();
        void Add(string kind, string text, params string[] keys)
        {
            if (facts.Count >= MaxFacts || facts.Any(f => f.Text == text)) return;
            facts.Add(new RootCauseKnownFact
            {
                Number = facts.Count + 1,
                Kind = kind,
                Text = text,
                FactKeys = keys.Where(k => !string.IsNullOrEmpty(k)).Distinct(StringComparer.Ordinal).ToList()
            });
        }

        // Established paired tests, one per pair and measure, the reference family first.
        foreach (var test in ctx.Input.PairTests
                     .Where(t => t.Established && t.Favors != "None" && t.Effect.HasValue
                                 && t.Measure is BenchmarkPairedTests.IntelligenceMeasure or BenchmarkPairedTests.SpeedMeasure or BenchmarkPairedTests.CostMeasure)
                     .OrderBy(t => t.Family == BenchmarkComparisonReportFacts.ReferenceFamilyName ? 0 : 1)
                     .GroupBy(t => t.First + "|" + t.Second + "|" + t.Measure, StringComparer.Ordinal)
                     .Select(g => g.First()))
        {
            string first = ctx.Name(test.First), second = ctx.Name(test.Second);
            string adjustment = test.FamilySize > 1 ? "established after the Holm adjustment across " + Inv(test.FamilySize) + " tests" : "established as a single test";
            string text = test.Measure switch
            {
                BenchmarkPairedTests.SpeedMeasure => first + "'s model time per question is " + Ratio(test.Effect!.Value) + " times " + second + "'s on the same questions, " + adjustment + ".",
                BenchmarkPairedTests.CostMeasure => first + "'s spend per question is " + Ratio(test.Effect!.Value) + " times " + second + "'s on the same questions, " + adjustment + ".",
                _ => first + " scored " + BenchmarkReportFormat.SignedOneDecimal(test.Effect!.Value) + " points against " + second + " on the same questions, " + adjustment + "."
            };
            Add(KindTest, text, test.FactKey ?? string.Empty);
        }

        // Decompositions whose difference is established by its own interval.
        foreach (var pair in result.SpeedPairs.Where(p => p.UnavailableReason == null && p.DifferenceLower is double lo && p.DifferenceUpper is double hi && (lo > 0 || hi < 0)))
        {
            var lead = pair.Components.Where(c => c.Share.HasValue).OrderByDescending(c => c.Share!.Value).FirstOrDefault();
            if (lead == null) continue;
            Add(KindDecomposition,
                RootCauseHypotheses.Capitalize(lead.Label) + " accounts for " + RootCauseHypotheses.SharePct(lead) + " of the per-question time difference between "
                + ctx.Name(pair.First) + " and " + ctx.Name(pair.Second) + ".",
                "rca.speed." + pair.First + "." + pair.Second + "." + lead.Key + ".share");
        }
        foreach (var pair in result.CostPairs.Where(p => p.UnavailableReason == null && p.DifferenceLower is double lo && p.DifferenceUpper is double hi && (lo > 0 || hi < 0)))
        {
            var lead = pair.Components.Where(c => c.Key != "all" && c.Share.HasValue).OrderByDescending(c => c.Share!.Value).FirstOrDefault();
            if (lead == null) continue;
            Add(KindDecomposition,
                RootCauseHypotheses.Capitalize(lead.Label) + " accounts for " + RootCauseHypotheses.SharePct(lead) + " of the per-question cost difference between "
                + ctx.Name(pair.First) + " and " + ctx.Name(pair.Second) + ".",
                "rca.cost." + pair.First + "." + pair.Second + "." + lead.Key + "." + lead.Effect + ".share");
        }

        // Configuration differences of each analyzed pair.
        foreach (var pair in ctx.Pairs)
        {
            var differences = RootCauseContext.ConfigDifferences(ctx.Model(pair.First), ctx.Model(pair.Second))
                .Where(d => d != "provider" && d != "model")
                .ToList();
            if (differences.Count == 0) continue;
            Add(KindConfiguration, ctx.Name(pair.First) + " and " + ctx.Name(pair.Second) + " differ on " + BenchmarkReportFormat.LetterList(differences) + ".",
                ctx.Comparison ? BenchmarkComparisonReportFacts.PairPrefix(pair.First, pair.Second) + "configDifferences" : string.Empty);
        }

        // The controls.
        if (ctx.Input.Controls != null)
        {
            if (result.Controls.Count == 0)
            {
                Add(KindCount, "No earlier run of another model answered these questions under the same instrument.", "rca.controls.count");
            }
            else if (result.ControlDeficitMean is double mean)
            {
                string interval = result.ControlDeficitLower is double lo && result.ControlDeficitUpper is double hi
                    ? " (95 % interval " + BenchmarkReportFormat.SignedOneDecimal(lo) + " to " + BenchmarkReportFormat.SignedOneDecimal(hi) + ")"
                    : string.Empty;
                Add(KindTest, ctx.Name(ctx.Models[0].Token) + "'s mean score differs by " + BenchmarkReportFormat.SignedOneDecimal(mean) + " points" + interval
                    + " from the mean of " + Inv(result.Controls.Count) + (result.Controls.Count == 1 ? " control" : " controls") + " on "
                    + Inv(result.ControlItems.Count) + " shared questions.",
                    "rca.control.deficit.mean", "rca.control.deficit.interval");
            }
            int suspect = result.ControlItems.Count(i => i.InstrumentSuspect);
            if (suspect > 0)
            {
                Add(KindCount, Inv(suspect) + (suspect == 1 ? " question" : " questions") + " " + ctx.Name(ctx.Models[0].Token)
                    + " scored below 50 were scored below 50 by every control too.", "rca.control.instrumentSuspect");
            }
        }

        // Counts of each model's failure signals.
        foreach (var summary in result.Evidence)
        {
            string name = ctx.Name(summary.Token);
            if (summary.ScoredAnswers == 0) continue;
            if (summary.NoToolAnswers > 0)
            {
                Add(KindCount, name + " answered " + Inv(summary.NoToolAnswers) + " of " + Inv(summary.ScoredAnswers) + " scored questions without a tool call.",
                    ctx.Key(summary.Token, "profile.noToolAnswers"));
            }
            if (summary.CriticalAnswers > 0)
            {
                Add(KindCount, name + " has " + Inv(summary.CriticalAnswers) + (summary.CriticalAnswers == 1 ? " critical error" : " critical errors")
                    + " in " + Inv(summary.ScoredAnswers) + " scored answers.", ctx.Key(summary.Token, "errors.critical"));
            }
            if (summary.BudgetOrDefectAnswers > 0)
            {
                Add(KindCount, name + " has " + Inv(summary.BudgetOrDefectAnswers) + " scored " + (summary.BudgetOrDefectAnswers == 1 ? "answer" : "answers")
                    + " with a budget, truncation or output defect.", ctx.Key(summary.Token, "profile.outputDefects"));
            }
        }

        // One model's own time and cost, for a stand-alone subject.
        if (ctx.Models.Count == 1)
        {
            var summary = result.Evidence[0];
            string name = ctx.Name(summary.Token);
            double timeTotal = summary.Time.Sum(s => s.Value);
            var timeLead = summary.Time.OrderByDescending(s => s.Value).FirstOrDefault();
            if (timeLead != null && timeTotal > 0)
            {
                Add(KindDecomposition, RootCauseHypotheses.Capitalize(timeLead.Label) + " is " + RootCauseHypotheses.Pct(timeLead.Value / timeTotal)
                    + " of " + name + "'s time per question.", "rca.within." + summary.Token + ".time." + timeLead.Key + ".share");
            }
            double costTotal = summary.Cost.Sum(s => s.Value);
            var costLead = summary.Cost.OrderByDescending(s => s.Value).FirstOrDefault();
            if (costLead != null && costTotal > 0)
            {
                Add(KindDecomposition, RootCauseHypotheses.Capitalize(costLead.Label) + " is " + RootCauseHypotheses.Pct(costLead.Value / costTotal)
                    + " of " + name + "'s cost per question.", "rca.within." + summary.Token + ".cost." + costLead.Key + ".share");
            }
        }

        return facts;
    }

    private static string Ratio(double value) => Math.Round(value, 2, MidpointRounding.AwayFromZero).ToString("0.00", CultureInfo.InvariantCulture);

    private static string Inv(int value) => value.ToString(CultureInfo.InvariantCulture);
}
