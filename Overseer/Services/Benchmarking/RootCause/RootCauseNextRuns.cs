namespace Overseer.Services.Benchmarking.RootCause;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using MobileGnollHackLogger.Data;
using Overseer.Services;

/// <summary>
/// The next runs and checks of § A5. Code proposes a run only for a hypothesis below High, and not ruled out,
/// that a run can settle, and a replicate for every null that stands; a hypothesis with a suite or corpus locus,
/// a shared failure and a price effect become checks. An unexpected result across two thinking levels joins a
/// thinking-level pair of the model expected ahead at the other's level, or a configuration check without one, and
/// every other unexpected result joins the replicate. Every run
/// carries its launcher settings, copied from the analyzed runs unless its kind changes them, and is
/// ordered C, then B, then M. <see cref="LoadCatalogAsync"/> is the settings derivation's only database
/// access; <see cref="Build"/> is pure.
/// </summary>
public static class RootCauseNextRuns
{
    /// <summary>The <c>ModelRole</c> bit of a Benchmark-role configuration.</summary>
    public const int BenchmarkRoleBit = 4;

    /// <summary>Runs per model of a replicate, and the launches of a time-of-day run.</summary>
    public const int ReplicateRuns = 3;

    private static readonly string[] KindOrder =
    {
        RootCauseRunKinds.Replicate, RootCauseRunKinds.ThinkingPair, RootCauseRunKinds.StylePair,
        RootCauseRunKinds.PanelSwap, RootCauseRunKinds.TimeStratum, RootCauseRunKinds.Control
    };

    /// <summary>
    /// The configured, enabled Benchmark-role configurations and the <c>RecommendedModels</c> entries of
    /// <paramref name="configuration"/> (none when it is null), as the model batch guardrails read them.
    /// </summary>
    public static async Task<RootCauseCatalog> LoadCatalogAsync(ApplicationDbContext db, IConfiguration? configuration, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        var configs = await db.SystemAiApiConfigurations
            .AsNoTracking()
            .Where(c => c.IsEnabled && (c.ModelRole & BenchmarkRoleBit) != 0)
            .OrderBy(c => c.OrderIndex)
            .ThenBy(c => c.Id)
            .Select(c => new { c.Id, c.Provider, c.ModelId, c.ThinkingLevel, c.DisplayName })
            .ToListAsync(ct);

        var recommended = new List<RootCauseRecommendedModel>();
        if (configuration != null)
        {
            foreach (var provider in configuration.GetSection("RecommendedModels").GetChildren().OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                foreach (var entry in provider.Get<List<RecommendedModel>>() ?? new List<RecommendedModel>())
                {
                    if (string.IsNullOrWhiteSpace(entry.Model)) continue;
                    recommended.Add(new RootCauseRecommendedModel
                    {
                        Provider = provider.Key,
                        Model = entry.Model.Trim(),
                        ThinkingLevel = string.IsNullOrWhiteSpace(entry.ThinkingLevel) ? null : entry.ThinkingLevel.Trim()
                    });
                }
            }
        }

        return new RootCauseCatalog
        {
            Configurations = configs.Select(c => new RootCauseConfigOption
            {
                Id = c.Id,
                Provider = c.Provider,
                ModelId = c.ModelId,
                ThinkingLevel = c.ThinkingLevel,
                Label = string.IsNullOrWhiteSpace(c.DisplayName) ? c.ModelId : c.DisplayName.Trim()
            }).ToList(),
            Recommended = recommended
        };
    }

    /// <summary>
    /// One run in a short phrase, the display of its <c>rca.run.&lt;n&gt;.summary</c> fact: <c>Run 1, a replicate
    /// of all nine models (about $260 and 8 h 50 min)</c>. The run's card in the document holds its details.
    /// Each model name is written through <paramref name="modelName"/> when it is given, so a comparison-scope
    /// fact names no model; a run of every one of <paramref name="analyzedModels"/> models (three or more)
    /// says so instead of listing them.
    /// </summary>
    public static string Summary(RootCauseRecommendedRun run, Func<string, string>? modelName = null, int? analyzedModels = null)
    {
        ArgumentNullException.ThrowIfNull(run);
        var names = run.ModelNames.Select(n => modelName == null ? n : modelName(n)).Distinct(StringComparer.Ordinal).ToList();
        string models = names.Count == 0 ? "no model resolved"
            : analyzedModels is int all && all >= 3 && names.Count == all ? "all " + CountWord(all) + " models"
            : BenchmarkReportFormat.LetterList(names);
        string estimate = EstimateText(run);
        return "Run " + Inv(run.Number) + ", " + Article(KindPhrase(run.Kind)) + " of " + models + (estimate.Length > 0 ? " (" + estimate + ")" : string.Empty);
    }

    /// <summary>
    /// One check in a short phrase, the display of its <c>rca.check.&lt;n&gt;.summary</c> fact: <c>Check 1, a comparison of
    /// the two models' price cards</c>; <c>Check 1</c> alone for a check stored without its summary.
    /// </summary>
    public static string Summary(RootCauseCheck check)
    {
        ArgumentNullException.ThrowIfNull(check);
        return string.IsNullOrWhiteSpace(check.Summary) ? "Check " + Inv(check.Number) : check.Summary;
    }

    /// <summary><c>Check 2, a review of the rubric of Q3</c>: the check's number and its noun phrase.</summary>
    private static string CheckSummary(int number, string phrase) => "Check " + Inv(number) + ", " + phrase;

    /// <summary>
    /// <c>about $7.50 and 14 minutes</c>, <c>about $240 and 7 h 50 min</c>: each figure to two significant figures,
    /// whole dollars from $10 and cents below, and from 90 minutes up in hours and minutes. A run of several launches
    /// states the estimate of one launch times <see cref="RootCauseRecommendedRun.Launches"/>, <c>about $23 and 42 minutes
    /// over 3 launches</c>. Empty when the run is not estimated.
    /// </summary>
    public static string EstimateText(RootCauseRecommendedRun run)
    {
        ArgumentNullException.ThrowIfNull(run);
        if (run.EstimatedCostUsd is not double cost) return string.Empty;
        int launches = Math.Max(1, run.Launches);
        return "about " + Dollars(cost * launches) + (run.EstimatedMinutes is double minutes ? " and " + Duration(minutes * launches) : string.Empty)
            + (launches > 1 ? " over " + Inv(launches) + " launches" : string.Empty);
    }

    /// <summary>The <see cref="RootCauseRecommendedRun.When"/> of a time-of-day run.</summary>
    public static readonly string TimeOfDayWhen = "Launch it " + Inv(ReplicateRuns) + " times, each at a different time of day; Number of Runs "
        + Inv(ReplicateRuns) + ", Runs per model " + Inv(ReplicateRuns) + " or a model batch would run them back to back, which this run must not do";

    /// <summary><c>$7.50</c>, <c>$0.046</c>, <c>$240</c>, <c>$1,200</c>: two significant figures, whole dollars from $10.</summary>
    public static string Dollars(double usd)
    {
        double rounded = TwoSignificant(Math.Max(usd, 0));
        if (rounded >= 10) return "$" + rounded.ToString("#,0", CultureInfo.InvariantCulture);
        return "$" + rounded.ToString(rounded >= 0.1 || rounded == 0 ? "0.00" : "0.00##", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// <c>14 minutes</c>, <c>7 h 50 min</c>, <c>20 h</c>: two significant figures, in hours and minutes from 90
    /// minutes up; <c>less than a minute</c> below one.
    /// </summary>
    public static string Duration(double minutes)
    {
        double rounded = TwoSignificant(Math.Max(minutes, 0));
        if (rounded < 1) return "less than a minute";
        int whole = (int)Math.Round(rounded, MidpointRounding.AwayFromZero);
        if (whole < 90) return Inv(whole) + (whole == 1 ? " minute" : " minutes");
        int hours = whole / 60, rest = whole % 60;
        return Inv(hours) + " h" + (rest > 0 ? " " + Inv(rest) + " min" : string.Empty);
    }

    /// <summary><paramref name="value"/> rounded to two significant figures, half away from zero; zero stays zero.</summary>
    private static double TwoSignificant(double value)
    {
        if (value <= 0) return 0;
        int exponent = 1 - (int)Math.Floor(Math.Log10(value));
        if (exponent >= 0)
        {
            double scale = Math.Pow(10, exponent);
            return Math.Round(value * scale, MidpointRounding.AwayFromZero) / scale;
        }
        double divisor = Math.Pow(10, -exponent);
        return Math.Round(value / divisor, MidpointRounding.AwayFromZero) * divisor;
    }

    /// <summary>The kind as a phrase inside a sentence: <c>replicate</c>, <c>thinking-level pair</c>, ….</summary>
    public static string KindPhrase(string kind) => kind switch
    {
        RootCauseRunKinds.Replicate => "replicate",
        RootCauseRunKinds.ThinkingPair => "thinking-level pair",
        RootCauseRunKinds.StylePair => "response-style pair",
        RootCauseRunKinds.PanelSwap => "panel swap",
        RootCauseRunKinds.TimeStratum => "time-of-day repeat",
        RootCauseRunKinds.Control => "control run",
        _ => kind
    };

    private static string Article(string phrase) => (phrase.Length > 0 && "aeiou".Contains(phrase[0]) ? "an " : "a ") + phrase;

    /// <summary>Two to twelve in words, digits above.</summary>
    private static string CountWord(int count) => count switch
    {
        2 => "two",
        3 => "three",
        4 => "four",
        5 => "five",
        6 => "six",
        7 => "seven",
        8 => "eight",
        9 => "nine",
        10 => "ten",
        11 => "eleven",
        12 => "twelve",
        _ => Inv(count)
    };

    /// <summary>The kind in words.</summary>
    public static string KindName(string kind) => kind switch
    {
        RootCauseRunKinds.Replicate => "Replicate",
        RootCauseRunKinds.ThinkingPair => "Thinking-level pair",
        RootCauseRunKinds.StylePair => "Response-style pair",
        RootCauseRunKinds.PanelSwap => "Panel swap",
        RootCauseRunKinds.TimeStratum => "Time-of-day repeat",
        RootCauseRunKinds.Control => "Control run",
        _ => kind
    };

    // ---------------------------------------------------------------------------------------------
    // Building
    // ---------------------------------------------------------------------------------------------

    /// <summary>A model of a run; <paramref name="Analyzed"/> when it is one of the analyzed models, not a configuration picked from the catalog.</summary>
    private sealed record RunModel(long? ConfigurationId, string Name, double? CostPerRun, double? MinutesPerRun, bool Analyzed = false);

    private sealed class Plan
    {
        public string Key { get; init; } = string.Empty;
        public string Kind { get; init; } = string.Empty;
        public string Purpose { get; set; } = RootCausePurposes.ModelSelection;
        public List<RunModel> Models { get; } = new();
        public List<(RootCauseHypothesis Hypothesis, RootCauseDraft Draft)> Hypotheses { get; } = new();
        public int RunsPerModel { get; set; } = 1;
        public int RunCount { get; set; } = 1;
        public bool Batch { get; set; }
        public bool? VerboseModeOverride { get; set; }
        public long? AssessorOverride { get; set; }
        public long? CoAssessorOverride { get; set; }
        public string? AssessorOverrideName { get; set; }
        public string? CoAssessorOverrideName { get; set; }
        public List<string> Notes { get; } = new();
        public string? Decides { get; set; }
        public int Insertion { get; init; }

        /// <summary>The unexpected results the plan settles, each with the kind of run it was planned for.</summary>
        public List<(RootCauseAnomaly Anomaly, string Kind)> Anomalies { get; } = new();

        /// <summary>
        /// The plan is a time-of-day repeat, or one was folded into it: one pass per model, launched
        /// <see cref="ReplicateRuns"/> times, each at a different time of day.
        /// </summary>
        public bool TimeOfDay { get; set; }
    }

    internal static (List<RootCauseRecommendedRun> Runs, List<RootCauseCheck> Checks) Build(
        RootCauseContext ctx, IReadOnlyList<(RootCauseHypothesis Hypothesis, RootCauseDraft Draft)> kept, bool systemFirst)
    {
        var plans = new Dictionary<string, Plan>(StringComparer.Ordinal);
        var checks = new List<RootCauseCheck>();
        var anchor = ctx.Comparison ? ctx.Model(ctx.Input.ReferenceToken ?? ctx.Models[0].Token) : ctx.Models[0];
        var bestKeys = kept.Where(k => k.Hypothesis.Kind != "null")
            .GroupBy(k => k.Hypothesis.Axis, StringComparer.Ordinal)
            .Select(g => g.First().Hypothesis.Key)
            .ToHashSet(StringComparer.Ordinal);

        Plan GetOrAdd(string key, string kind, Action<Plan> init)
        {
            if (!plans.TryGetValue(key, out var plan))
            {
                plan = new Plan { Key = key, Kind = kind, Insertion = plans.Count };
                init(plan);
                plans[key] = plan;
            }
            return plan;
        }

        void Check(string kind, string text, string phrase, RootCauseHypothesis hypothesis)
        {
            var existing = checks.FirstOrDefault(c => c.Text == text);
            if (existing == null)
            {
                int number = checks.Count + 1;
                existing = new RootCauseCheck { Number = number, Kind = kind, Text = text, Summary = CheckSummary(number, phrase) };
                checks.Add(existing);
            }
            if (!existing.HypothesisIds.Contains(hypothesis.Key)) existing.HypothesisIds.Add(hypothesis.Key);
            hypothesis.CheckNumber ??= existing.Number;
        }

        foreach (var (h, d) in kept)
        {
            // A rejected null is settled, and so is a hypothesis its own test rules out. A null that stands joins the
            // replicate: more runs per model are what can show a difference it missed.
            if (h.Grade == RootCauseGrades.NotSupported || (h.Kind != "null" && h.Grade == RootCauseGrades.High)) continue;
            if (h.Kind == "null")
            {
                var pairModels = h.Models.Where(t => ctx.Models.Any(m => m.Token == t)).Select(ctx.Model).ToList();
                var standing = GetOrAdd("replicate", RootCauseRunKinds.Replicate, p =>
                {
                    p.Batch = true;
                    p.RunsPerModel = ReplicateRuns;
                });
                foreach (var m in pairModels.Append(anchor)) AddModel(standing, m);
                standing.Hypotheses.Add((h, d));
                continue;
            }

            if (d.CheckText != null)
            {
                Check(d.CheckKind, d.CheckText, d.CheckSummary ?? "a review of the " + d.CheckKind + " behind " + h.Key, h);
                continue;
            }
            if (d.Loci.Count > 0 && d.Loci.All(l => l is RootCauseLoci.Suite or RootCauseLoci.Corpus))
            {
                Check("rubric", "Review the questions behind " + h.Key + " against the source and the tools' corpus.",
                    "a review of the questions behind " + h.Key, h);
                continue;
            }

            string? kind = d.NextRunKind;
            bool replicateSettles = d.PairNullStands == true
                || h.Robustness.Any(r => r.Name == RootCauseHypotheses.Replicates && r.Status == RootCauseCheckStatuses.Failed);
            var models = h.Models.Where(t => ctx.Models.Any(m => m.Token == t)).Select(ctx.Model).ToList();

            // A thinking-level pair of one model cannot settle a claim that compares two different models.
            if (kind == RootCauseRunKinds.ThinkingPair && models.Count >= 2 && RootCauseHypotheses.DifferentModels(models[0], models[1]))
            {
                kind = RootCauseRunKinds.Replicate;
            }
            if (kind == RootCauseRunKinds.Replicate && !replicateSettles) kind = null;
            if (kind == RootCauseRunKinds.Control && !ctx.NeedsControlRun) kind = replicateSettles ? RootCauseRunKinds.Replicate : null;
            if (kind == null) continue;

            var worse = models.FirstOrDefault() ?? anchor;
            Plan? plan = null;

            switch (kind)
            {
                case RootCauseRunKinds.Replicate:
                    plan = GetOrAdd("replicate", kind, p =>
                    {
                        p.Batch = true;
                        p.RunsPerModel = ReplicateRuns;
                    });
                    foreach (var m in models.Append(anchor)) AddModel(plan, m);
                    break;

                case RootCauseRunKinds.ThinkingPair:
                    plan = ThinkingPair(ctx, worse, models, h, GetOrAdd);
                    if (plan == null)
                    {
                        Check("configuration", "No configured, enabled Benchmark-role configuration of " + ctx.Name(worse.Token)
                            + " exists at another thinking level; add one to test " + h.Key + ".",
                            "a new configuration of " + ctx.Name(worse.Token) + " at another thinking level", h);
                    }
                    break;

                case RootCauseRunKinds.StylePair:
                    // One run of the reference model in the other style settles every style hypothesis of the document.
                    plan = GetOrAdd("style:" + anchor.Token, kind, p =>
                    {
                        p.Purpose = RootCausePurposes.Chat;
                        p.RunCount = 1;
                        bool concise = anchor.Evidence.Settings.VerboseMode != true;
                        p.VerboseModeOverride = concise;
                        p.Notes.Add("Response Style " + (concise ? "Detailed" : "Concise") + "; the analyzed runs used "
                            + (concise ? "Concise" : "Detailed") + " and are the other half of the pair. Everything else is unchanged.");
                    });
                    AddModel(plan, anchor);
                    break;

                case RootCauseRunKinds.PanelSwap:
                    plan = PanelSwap(ctx, models.Count > 0 ? models : new List<RootCauseModelInput> { anchor }, GetOrAdd);
                    if (plan == null)
                    {
                        Check("graders", "No two configured, enabled Benchmark-role graders of other providers exist for a panel swap; add them to test the grader-dependent hypotheses.",
                            "a panel swap with two graders of other providers", h);
                    }
                    break;

                case RootCauseRunKinds.TimeStratum:
                    plan = GetOrAdd("time:" + worse.Token, kind, p =>
                    {
                        p.Purpose = RootCausePurposes.Chat;
                        p.TimeOfDay = true;
                    });
                    AddModel(plan, worse);
                    break;

                case RootCauseRunKinds.Control:
                    plan = Control(ctx, anchor, GetOrAdd, checks);
                    break;
            }

            if (plan == null) continue;
            plan.Hypotheses.Add((h, d));
        }

        // An unexpected result across two thinking levels is settled by the model expected ahead at the other's level;
        // any other by the replicate.
        var anomalyChecks = new List<(RootCauseAnomaly Anomaly, RootCauseCheck Check)>();
        foreach (var anomaly in ctx.Anomalies)
        {
            var expected = ctx.Model(anomaly.Expected);
            var other = ctx.Model(anomaly.Other);
            if (!RootCauseContext.SameSetting(expected.ThinkingLevel, other.ThinkingLevel))
            {
                var pair = ThinkingPair(ctx, expected, new[] { expected }, null, GetOrAdd, other.ThinkingLevel);
                if (pair != null)
                {
                    pair.Anomalies.Add((anomaly, RootCauseRunKinds.ThinkingPair));
                    continue;
                }

                string level = RootCauseContext.SettingText(other.ThinkingLevel);
                string text = "No configured, enabled Benchmark-role configuration of " + ctx.Name(expected.Token) + " exists at " + level
                              + " thinking; add one to test " + anomaly.Ref + ".";
                var check = checks.FirstOrDefault(c => c.Text == text);
                if (check == null)
                {
                    int number = checks.Count + 1;
                    check = new RootCauseCheck
                    {
                        Number = number,
                        Kind = "configuration",
                        Text = text,
                        Summary = CheckSummary(number, "a new configuration of " + ctx.Name(expected.Token) + " at " + level + " thinking")
                    };
                    checks.Add(check);
                }
                anomalyChecks.Add((anomaly, check));
                continue;
            }

            var replicateRun = GetOrAdd("replicate", RootCauseRunKinds.Replicate, p =>
            {
                p.Batch = true;
                p.RunsPerModel = ReplicateRuns;
            });
            foreach (var m in new[] { expected, other, anchor }) AddModel(replicateRun, m);
            replicateRun.Anomalies.Add((anomaly, RootCauseRunKinds.Replicate));
        }

        if (ctx.NeedsControlRun) Control(ctx, anchor, GetOrAdd, checks);

        // Purposes of replicates: B when every hypothesis it settles points to the harness.
        if (plans.TryGetValue("replicate", out var replicate) && replicate.Hypotheses.Count > 0
            && replicate.Hypotheses.All(x => x.Draft.Loci.Count > 0 && x.Draft.Loci[0] == RootCauseLoci.Harness))
        {
            replicate.Purpose = RootCausePurposes.Benchmark;
        }

        Fold(plans);

        var ordered = plans.Values
            .Where(p => p.Models.Count > 0)
            .OrderBy(p => RootCausePurposes.Order(p.Purpose))
            .ThenBy(p => Array.IndexOf(KindOrder, p.Kind))
            .ThenBy(p => p.Insertion)
            .ToList();

        var runs = new List<RootCauseRecommendedRun>();
        foreach (var plan in ordered)
        {
            var run = ToRun(ctx, anchor, plan, runs.Count + 1, bestKeys, systemFirst);
            runs.Add(run);
            foreach (var (h, _) in plan.Hypotheses) h.NextRunNumber ??= run.Number;
            foreach (var (anomaly, _) in plan.Anomalies) anomaly.SettledByRun ??= run.Number;
        }
        foreach (var (anomaly, check) in anomalyChecks)
        {
            if (anomaly.SettledByRun == null) anomaly.SettledByCheck ??= check.Number;
        }

        return (runs, checks);
    }

    /// <summary>
    /// Folds every time-of-day repeat and thinking-level pair whose analyzed models all belong to a replicate of
    /// <see cref="ReplicateRuns"/> or more runs per suite into that replicate: it settles their hypotheses and unexpected results too,
    /// runs the plan's models from the catalog as well, is launched once per time of day when a
    /// time-of-day repeat was folded, and takes the most urgent purpose among them (the chat, then the benchmark,
    /// then model selection).
    /// </summary>
    private static void Fold(Dictionary<string, Plan> plans)
    {
        if (!plans.TryGetValue("replicate", out var replicate) || !replicate.Batch || replicate.RunsPerModel < ReplicateRuns) return;

        bool Covers(RunModel model)
            => replicate.Models.Any(m => (model.ConfigurationId.HasValue && m.ConfigurationId == model.ConfigurationId) || m.Name == model.Name);

        var folded = plans.Values
            .Where(p => p.Kind is RootCauseRunKinds.TimeStratum or RootCauseRunKinds.ThinkingPair
                        && p.Models.Any(m => m.Analyzed) && p.Models.Where(m => m.Analyzed).All(Covers))
            .OrderBy(p => p.Insertion)
            .ToList();
        foreach (var plan in folded)
        {
            foreach (var model in plan.Models.Where(m => !m.Analyzed && !Covers(m)).ToList()) replicate.Models.Add(model);
            foreach (var hypothesis in plan.Hypotheses)
            {
                if (!replicate.Hypotheses.Any(x => x.Hypothesis.Key == hypothesis.Hypothesis.Key)) replicate.Hypotheses.Add(hypothesis);
            }
            foreach (var item in plan.Anomalies)
            {
                if (!replicate.Anomalies.Any(x => ReferenceEquals(x.Anomaly, item.Anomaly))) replicate.Anomalies.Add(item);
            }
            if (plan.Kind == RootCauseRunKinds.TimeStratum) replicate.TimeOfDay = true;
            if (RootCausePurposes.Order(plan.Purpose) < RootCausePurposes.Order(replicate.Purpose)) replicate.Purpose = plan.Purpose;
            plans.Remove(plan.Key);
        }
    }

    private static void AddModel(Plan plan, RootCauseModelInput model)
    {
        long? id = model.Evidence.Settings.TestedModelConfigurationId;
        if (plan.Models.Any(m => (id.HasValue && m.ConfigurationId == id) || m.Name == model.Label)) return;
        plan.Models.Add(new RunModel(id, model.Label, model.CostPerRunUsd, model.Evidence.MeanMinutesPerRun, Analyzed: true));
    }

    private static void AddOption(Plan plan, RootCauseConfigOption option)
    {
        if (plan.Models.Any(m => m.ConfigurationId == option.Id)) return;
        plan.Models.Add(new RunModel(option.Id, option.Label, null, null));
    }

    /// <summary>
    /// The model at two thinking levels: the pair already analyzed when the hypothesis names one model at two
    /// levels; else the model and an analyzed model of the same id at another level, the nearest level first;
    /// else its configuration and another configured one of the same model id at another level. With
    /// <paramref name="targetLevel"/>, the other level must be that one.
    /// </summary>
    private static Plan? ThinkingPair(RootCauseContext ctx, RootCauseModelInput worse, IReadOnlyList<RootCauseModelInput> models,
        RootCauseHypothesis? hypothesis, Func<string, string, Action<Plan>, Plan> getOrAdd, string? targetLevel = null)
    {
        bool AtTarget(string? level) => targetLevel == null || RootCauseContext.SameSetting(level, targetLevel);

        var sameModel = models.Where(m => string.Equals(m.ModelId?.Trim(), worse.ModelId?.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
        if (sameModel.Count >= 2)
        {
            var plan = getOrAdd("thinking:" + worse.ModelId.Trim().ToLowerInvariant(), RootCauseRunKinds.ThinkingPair, p => Init(p, worse));
            foreach (var m in sameModel) AddModel(plan, m);
            return plan;
        }

        var sibling = ctx.Models
            .Where(m => m.Token != worse.Token
                        && string.Equals(m.ModelId?.Trim(), worse.ModelId?.Trim(), StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(m.ThinkingLevel?.Trim(), worse.ThinkingLevel?.Trim(), StringComparison.OrdinalIgnoreCase)
                        && AtTarget(m.ThinkingLevel))
            .OrderBy(m => Math.Abs(RootCauseHypotheses.ThinkingRank(m.ThinkingLevel) - RootCauseHypotheses.ThinkingRank(worse.ThinkingLevel)))
            .ThenBy(m => m.Token, StringComparer.Ordinal)
            .FirstOrDefault();
        if (sibling != null)
        {
            var plan = getOrAdd("thinking:" + worse.ModelId.Trim().ToLowerInvariant(), RootCauseRunKinds.ThinkingPair, p => Init(p, worse));
            AddModel(plan, worse);
            AddModel(plan, sibling);
            return plan;
        }

        long? own = worse.Evidence.Settings.TestedModelConfigurationId;
        var other = ctx.Input.Catalog.Configurations
            .Where(c => c.Id != own
                        && string.Equals(c.ModelId?.Trim(), worse.ModelId?.Trim(), StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(c.ThinkingLevel?.Trim(), worse.ThinkingLevel?.Trim(), StringComparison.OrdinalIgnoreCase)
                        && AtTarget(c.ThinkingLevel))
            .OrderBy(c => Math.Abs(RootCauseHypotheses.ThinkingRank(c.ThinkingLevel) - RootCauseHypotheses.ThinkingRank(worse.ThinkingLevel)))
            .ThenBy(c => c.Id)
            .FirstOrDefault();
        if (other == null) return null;

        var result = getOrAdd("thinking:" + worse.ModelId.Trim().ToLowerInvariant(), RootCauseRunKinds.ThinkingPair, p => Init(p, worse));
        AddModel(result, worse);
        AddOption(result, other);
        return result;

        static void Init(Plan p, RootCauseModelInput model)
        {
            p.Batch = true;
            p.RunsPerModel = Math.Max(1, model.Evidence.RunsPerSuite);
        }
    }

    /// <summary>The same models graded by a panel of two other providers, from the configured graders.</summary>
    private static Plan? PanelSwap(RootCauseContext ctx, IReadOnlyList<RootCauseModelInput> models, Func<string, string, Action<Plan>, Plan> getOrAdd)
    {
        var current = ctx.Graders
            .Where(g => g.Role is BenchmarkReportFacts.PanelMemberARole or BenchmarkReportFacts.PanelMemberBRole or BenchmarkReportFacts.AssessorRole)
            .Select(g => (g.Provider ?? string.Empty).Trim().ToLowerInvariant())
            .ToHashSet(StringComparer.Ordinal);
        var candidateProviders = models.Select(m => (m.Provider ?? string.Empty).Trim().ToLowerInvariant()).ToHashSet(StringComparer.Ordinal);

        var picks = ctx.Input.Catalog.Configurations
            .Where(c => !current.Contains((c.Provider ?? string.Empty).Trim().ToLowerInvariant()))
            .OrderBy(c => candidateProviders.Contains((c.Provider ?? string.Empty).Trim().ToLowerInvariant()) ? 1 : 0)
            .ThenBy(c => c.Id)
            .GroupBy(c => (c.Provider ?? string.Empty).Trim().ToLowerInvariant(), StringComparer.Ordinal)
            .Select(g => g.First())
            .Take(2)
            .ToList();
        if (picks.Count < 2) return null;

        var plan = getOrAdd("panel", RootCauseRunKinds.PanelSwap, p =>
        {
            p.Purpose = RootCausePurposes.Benchmark;
            p.Batch = true;
            p.RunsPerModel = Math.Max(1, models.Min(m => m.Evidence.RunsPerSuite));
            p.AssessorOverride = picks[0].Id;
            p.CoAssessorOverride = picks[1].Id;
            p.AssessorOverrideName = picks[0].Label;
            p.CoAssessorOverrideName = picks[1].Label;
            p.Notes.Add("Graded by a panel of " + picks[0].Label + " and " + picks[1].Label + ", from two providers other than the analyzed graders'.");
        });
        foreach (var m in models) AddModel(plan, m);
        return plan;
    }

    /// <summary>
    /// The control run: the same target and graders without the subject, with the recommended model of each
    /// other provider that has a configured, enabled Benchmark-role configuration, up to the controls needed.
    /// </summary>
    private static Plan? Control(RootCauseContext ctx, RootCauseModelInput subject, Func<string, string, Action<Plan>, Plan> getOrAdd, List<RootCauseCheck> checks)
    {
        var existing = ctx.Input.Controls?.Controls.Select(c => (c.Provider, c.ModelId)).ToList()
                       ?? ctx.Models.Where(m => m.Token != subject.Token).Select(m => (m.Provider, m.ModelId)).ToList();
        int otherProviders = existing.Count(e => !RootCauseHypotheses.SameProvider(e.Provider, subject.Provider));
        int needed = Math.Max(existing.Count >= 2 && otherProviders == 0 ? 1 : 2 - existing.Count, 1);

        var picks = new List<RootCauseConfigOption>();
        foreach (var provider in ctx.Input.Catalog.Recommended
                     .Select(r => r.Provider)
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .Where(p => !RootCauseHypotheses.SameProvider(p, subject.Provider))
                     .OrderBy(p => p, StringComparer.Ordinal))
        {
            if (picks.Count >= needed) break;
            var option = ctx.Input.Catalog.Recommended
                .Where(r => RootCauseHypotheses.SameProvider(r.Provider, provider))
                .SelectMany(r => ctx.Input.Catalog.Configurations.Where(c =>
                    RootCauseHypotheses.SameProvider(c.Provider, provider)
                    && string.Equals(c.ModelId?.Trim(), r.Model, StringComparison.OrdinalIgnoreCase)
                    && (r.ThinkingLevel == null || string.Equals(c.ThinkingLevel?.Trim(), r.ThinkingLevel, StringComparison.OrdinalIgnoreCase))))
                .Where(c => !string.Equals(c.ModelId?.Trim(), subject.ModelId?.Trim(), StringComparison.OrdinalIgnoreCase)
                            && !existing.Any(e => string.Equals(e.ModelId?.Trim(), c.ModelId?.Trim(), StringComparison.OrdinalIgnoreCase)))
                .OrderBy(c => c.Id)
                .FirstOrDefault();
            if (option != null) picks.Add(option);
        }

        if (picks.Count == 0)
        {
            const string text = "No configured, enabled Benchmark-role configuration of a recommended model of another provider exists; add one to run a control.";
            if (!checks.Any(c => c.Text == text))
            {
                int number = checks.Count + 1;
                checks.Add(new RootCauseCheck
                {
                    Number = number,
                    Kind = "configuration",
                    Text = text,
                    Summary = CheckSummary(number, "a new configuration of a recommended model of another provider, for a control run")
                });
            }
            return null;
        }

        return getOrAdd("control", RootCauseRunKinds.Control, p =>
        {
            p.Purpose = RootCausePurposes.Benchmark;
            p.Batch = true;
            p.RunsPerModel = Math.Max(1, subject.Evidence.RunsPerSuite);
            p.Decides = "Separates the model from the instrument: once two controls, one from another provider, have run on the same questions "
                        + "under the same graders, the intelligence hypotheses are graded without the control cap.";
            p.Notes.Add("Without the subject: its run already exists and becomes the other side of the comparison.");
            foreach (var pick in picks) AddOption(p, pick);
        });
    }

    private static RootCauseRecommendedRun ToRun(
        RootCauseContext ctx, RootCauseModelInput anchor, Plan plan, int number, IReadOnlySet<string> bestKeys, bool systemFirst)
    {
        var settings = anchor.Evidence.Settings;
        bool battery = settings.BatteryId.HasValue || ctx.Battery;
        // A time-of-day run is one pass per model in each launch; a batch only when it holds several models.
        bool batch = plan.TimeOfDay ? plan.Models.Count > 1 : plan.Batch || plan.Models.Count > 1;
        var run = new RootCauseRecommendedRun
        {
            Number = number,
            Kind = plan.Kind,
            Purpose = plan.Purpose,
            // A response-style pair states a condition, not a cause, so it is never more than optional.
            Mark = plan.Kind != RootCauseRunKinds.StylePair
                   && (plan.Kind == RootCauseRunKinds.Control || plan.Hypotheses.Any(x => bestKeys.Contains(x.Hypothesis.Key)))
                ? RootCauseMarks.Recommended
                : RootCauseMarks.Optional,
            HypothesisIds = plan.Hypotheses.Select(x => x.Hypothesis.Key).Distinct(StringComparer.Ordinal).ToList(),
            TargetKind = battery ? "Battery" : "Suite",
            SuiteId = battery ? null : settings.SuiteId,
            SuiteName = battery ? null : settings.SuiteName,
            BatteryId = battery ? settings.BatteryId : null,
            BatteryName = battery ? settings.BatteryName : null,
            RunMode = batch ? "Batch" : "Single",
            Order = "Randomized",
            RunsPerModel = batch && !plan.TimeOfDay ? plan.RunsPerModel : 1,
            RunCount = batch || plan.TimeOfDay ? 1 : plan.RunCount,
            Launches = plan.TimeOfDay ? ReplicateRuns : 1,
            ScoringProfileId = settings.ScoringProfileId,
            VerboseMode = plan.VerboseModeOverride ?? settings.VerboseMode,
            AllowSourceCodeReferences = settings.AllowSourceCodeReferences,
            AssessorModelConfigurationId = plan.AssessorOverride ?? settings.AssessorModelConfigurationId,
            CoAssessorModelConfigurationId = plan.CoAssessorOverride ?? settings.CoAssessorModelConfigurationId,
            SecondOpinionAssessorModelConfigurationId = settings.SecondOpinionAssessorModelConfigurationId,
            SecondOpinionMode = settings.SecondOpinionMode,
            ClaimVerifierModelConfigurationId = settings.ClaimVerifierModelConfigurationId,
            ScoringProfileName = settings.ScoringProfileName,
            AssessorName = plan.AssessorOverride.HasValue ? plan.AssessorOverrideName : settings.AssessorName,
            CoAssessorName = plan.CoAssessorOverride.HasValue ? plan.CoAssessorOverrideName : settings.CoAssessorName,
            ReferenceReaderName = settings.ReferenceReaderName,
            ClaimVerifierName = settings.ClaimVerifierName,
            ReportWriterModelConfigurationId = null,
            AfterSystemHypotheses = systemFirst && plan.Purpose == RootCausePurposes.ModelSelection,
            When = plan.TimeOfDay ? TimeOfDayWhen : null
        };

        foreach (var model in plan.Models)
        {
            if (model.ConfigurationId is long id)
            {
                run.TestedModelConfigurationIds.Add(id);
                run.ModelNames.Add(model.Name);
            }
            else
            {
                run.Notes.Add("No model configuration was recorded for " + model.Name + "; choose it in the launcher.");
            }
        }

        foreach (string field in settings.Differing)
        {
            run.Notes.Add("The analyzed runs differ on " + field + ", so it is left for you to choose.");
        }
        if (!battery && settings.SuiteId == null) run.Notes.Add("The analyzed runs' suite could not be resolved; choose it in the launcher.");
        if (battery && settings.BatteryId == null) run.Notes.Add("The battery could not be resolved; choose it in the launcher.");
        run.Notes.AddRange(plan.Notes);
        if (plan.TimeOfDay) run.Notes.Add("Launch this setup " + Inv(ReplicateRuns) + " times, each at a different time of day.");
        if (run.AfterSystemHypotheses) run.Notes.Add("Make this run after the system hypotheses are settled.");

        run.PredictedTier = plan.Kind switch
        {
            RootCauseRunKinds.Replicate => batch ? "Model comparison with the analyzed runs; a replicate (Tier A) of each model's own runs" : "Replicate (Tier A) of the analyzed runs",
            RootCauseRunKinds.ThinkingPair => "Model comparison: only the thinking level differs",
            RootCauseRunKinds.StylePair => "Not comparable on Completeness, Conciseness or Readability: the prompt options differ",
            RootCauseRunKinds.PanelSwap => "Verification of a change: the graders differ",
            RootCauseRunKinds.TimeStratum => "Replicate (Tier A); time of day differs, which the speed comparison discloses",
            RootCauseRunKinds.Control => "Model comparison: every must-match key agrees and only the model differs",
            _ => "Not predicted"
        };
        if (plan.TimeOfDay && plan.Kind == RootCauseRunKinds.Replicate) run.PredictedTier += "; time of day differs, which the speed comparison discloses";

        var decides = plan.Decides != null ? new List<string> { plan.Decides } : Decides(plan.Hypotheses);
        decides.AddRange(AnomalyDecides(plan.Anomalies));
        run.DecidesItems = decides;
        run.Decides = string.Join(" ", decides);

        var (cost, minutes, estimated) = Estimate(ctx, plan, batch);
        run.EstimatedCostUsd = cost;
        run.EstimatedMinutes = minutes;
        if (estimated) run.Notes.Add("The estimate for a model without analyzed runs is the analyzed models' mean.");
        return run;
    }

    /// <summary>
    /// What a run decides: one sentence per group of its hypotheses that share a confirm-if and a refuted-if
    /// statement, in the order of each group's first hypothesis: <c>Confirms HQ2.1 and HQ2.2 if …; refutes
    /// each if ….</c>, or <c>… refutes it if ….</c> for a group of one.
    /// </summary>
    private static List<string> Decides(IReadOnlyList<(RootCauseHypothesis Hypothesis, RootCauseDraft Draft)> hypotheses)
    {
        var groups = hypotheses
            .Select(x => (x.Hypothesis.Key,
                Confirm: string.IsNullOrWhiteSpace(x.Draft.ConfirmIf) ? "the result repeats" : x.Draft.ConfirmIf,
                Refute: LowerFirst(x.Draft.RefutedIf.TrimEnd('.'))))
            .GroupBy(x => (x.Confirm, x.Refute))
            .Select(g => (Keys: g.Select(x => x.Key).Distinct(StringComparer.Ordinal).ToList(), g.Key.Confirm, g.Key.Refute));

        return groups.Select(g =>
            "Confirms " + BenchmarkReportFormat.LetterList(g.Keys) + " if " + g.Confirm
            + "; refutes " + (g.Keys.Count == 1 ? "it" : "each") + " if " + g.Refute + ".").ToList();
    }

    /// <summary>
    /// What a run shows about its unexpected results: one sentence per kind of run they were planned for, in their order:
    /// <c>Shows whether U1 holds with both models at the same thinking level.</c>, <c>Shows whether U2 and U3 repeat over three
    /// runs per model.</c>
    /// </summary>
    private static IEnumerable<string> AnomalyDecides(IReadOnlyList<(RootCauseAnomaly Anomaly, string Kind)> anomalies)
    {
        foreach (var group in anomalies.GroupBy(a => a.Kind, StringComparer.Ordinal))
        {
            var refs = group.Select(a => a.Anomaly.Ref).Distinct(StringComparer.Ordinal).ToList();
            bool one = refs.Count == 1;
            string what = BenchmarkReportFormat.LetterList(refs);
            yield return group.Key == RootCauseRunKinds.ThinkingPair
                ? "Shows whether " + what + (one ? " holds" : " hold") + " with both models at the same thinking level."
                : "Shows whether " + what + (one ? " repeats" : " repeat") + " over " + CountWord(ReplicateRuns) + " runs per model.";
        }
    }

    /// <summary>
    /// The cost and minutes of one launch of the run from the analyzed runs' own: each model's cost and duration per
    /// run times its runs (one on a time-of-day run), a model without analyzed runs at the analyzed models' mean; null
    /// when nothing is known.
    /// </summary>
    private static (double? Cost, double? Minutes, bool Estimated) Estimate(RootCauseContext ctx, Plan plan, bool batch)
    {
        var knownCost = ctx.Models.Select(m => m.CostPerRunUsd).Where(c => c.HasValue).Select(c => c!.Value).ToList();
        var knownMinutes = ctx.Models.Select(m => m.Evidence.MeanMinutesPerRun).Where(c => c.HasValue).Select(c => c!.Value).ToList();
        double? meanCost = knownCost.Count > 0 ? knownCost.Average() : null;
        double? meanMinutes = knownMinutes.Count > 0 ? knownMinutes.Average() : null;
        int runs = plan.TimeOfDay ? 1 : batch ? plan.RunsPerModel : plan.RunCount;

        double cost = 0, minutes = 0;
        bool costKnown = true, minutesKnown = true, estimated = false;
        foreach (var model in plan.Models)
        {
            double? c = model.CostPerRun ?? meanCost;
            double? m = model.MinutesPerRun ?? meanMinutes;
            if (model.CostPerRun == null || model.MinutesPerRun == null) estimated = true;
            if (c is double cv) cost += cv * runs; else costKnown = false;
            if (m is double mv) minutes += mv * runs; else minutesKnown = false;
        }
        double? totalCost = costKnown && plan.Models.Count > 0 ? cost : null;
        double? totalMinutes = minutesKnown && plan.Models.Count > 0 ? minutes : null;
        return (totalCost, totalMinutes, estimated);
    }

    private static string LowerFirst(string text) => RootCauseHypotheses.LowerFirst(text);

    private static string Inv(int value) => value.ToString(CultureInfo.InvariantCulture);
}
