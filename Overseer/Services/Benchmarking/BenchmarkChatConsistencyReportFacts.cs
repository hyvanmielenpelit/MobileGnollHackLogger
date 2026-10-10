namespace Overseer.Services.Benchmarking;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using MobileGnollHackLogger.Data;
using Overseer.Models;
using Overseer.Services.ChatConsistency;

// The fact sheet of a chat consistency document: one saved chat consistency analysis, its model the
// subject and its control models the peers, lettered A, B, … by control subject key (ordinal). Every
// free text the analysis wrote is lettered as the comparison scope letters it, so a control's name
// never reaches the writer. Indexed keys count from 1 in the analysis's own order. Numbers print a
// minus as U+2212 and keep their unit on the same line with a no-break space (U+00A0): "−7.6 %".
//
// Fact keys (sorted by key, ordinal, on the sheet):
//
//   analysis.id, .name, .inputSha256, .codeVersion, .savedAt, .relaxedPooling, .compared (code version 4 on, with a comparison set)
//     analysis.inputSha256 stays on the sheet but never reaches the writer or a rendered block (WriterHidden)
//   analysis.writtenOutOfDate   only when the writer started on an out-of-date analysis after the operator confirmed it
//   subject.label, .provider, .modelId, .thinkingLevel, .serviceTier
//   verdict.overall, .short, .quality, .headline, .reliabilityIncreases
//   scope.hours, .excludedShare, .oneTimeStratum
//   coverage.strata.count, coverage.strata.<n>, coverage.usBusinessHours, .outsideBusinessHours
//   period.<baseline|comparison>.start, .end, .window, .runs, .units, .unitNoun, .memberRuns, .days, .items, .answers, .legacyRuns, .suites, .hours
//   level.<baseline|comparison>.answers, .quality, .overallIndex, .timeToFirstAnswerText, .streamingRate, .outputTokens,
//     .costPerQuestion, .failedAnswers   descriptive, unavailable before analysis code version 6
//     level.<period>.quality is kept from the writer in a battery comparison with both Overall Indexes (WriterHiddenKeys)
//   sample.minimumUnits, .minimumDays, .minimumPairedItems, .met, .shortfall (only when not met)
//   protocol.version, .label, .alpha, .overridden, protocol.margin.<P1..P5>
//   n.targetRuns, n.controlRuns, n.answers
//   endpoint.<P1..P5>.name, .verdict, .grade, .estimate, .percent (log scale), .ci95, .ci95Low, .ci95High,
//     .ci90, .p, .adjustedP, .mde, .mdeNote, .runsForMargin, .runs, .items, .legacyProxy, .commonGrader, .minimumSampleMet,
//     .notComputedKind (not computed, analysis code version 6 on)
//   quality.dimensions.<d>.*, quality.criticalErrors.*, flip.*   secondary results of the quality detail family
//   quality.commonGrader
//   grader.drift.count, grader.drift.<n>.anchorRun, .grader, .earliest, .latest, .drift, .items, .withinMargin
//   reliability.<id>.name, .baseline, .comparison, .p, .adjustedP, .increased, .establishedIncrease
//   tools.<id>.*                                   secondary results of the tool-use family
//   secondary.<family>.<id>.*, secondary.<family>.note   every other secondary family
//   events.count, events.<n>.at, .kind, .label, .change, .run, .previousRun, .series
//     a run of a battery comparison's unit reads as its battery run: "battery run #12 (run #98)"
//     (sheets written before .change also stored the raw .from and .to; they never reach the writer)
//   eventGroups.count, eventGroups.<n>.tag, .at, .lastAt, .run, .changes, .series   the events of one UTC day, series,
//     harness version and side of the period split (EventGroupKey), tagged E1, E2, … in time order
//   controls.count, controls.<n>.model, .sameProvider, .runs, .periods   n is the control's letter, A = 1
//   controls.missing.count, controls.missing.<n>.period, .suite, .suggestion, .targetRun, .batteryRun, .buildReplaced
//   did.count, did.<n>.endpoint, .model, .controlChange, .controlChangeCi95, .estimate, .ci95, .p,
//     .includesZero, .separatesTarget, .movedSameWay, .items
//   robustness.count, robustness.failed, robustness.<n>.endpoint, .name, .status, .detail,
//     robustness.<check>.summary   one sentence per check, endpoints sharing a status and detail named together
//   identity.<period>.servedModels, .calls, identity.changed
//   serving.<period>.tierMismatchCalls, .fallbackCalls, .speeds, serving.configurationDiffers,
//     serving.timeOfDayAssessable
//   ownWaits.<period>.share, .permitWait, .backoffWait, .retries, .answersWithTelemetry
//   identity.servedModels, serving.speeds, ownWaits.share, ownWaits.retries   one value for both periods, only when
//     the periods' values are equal; the pair it stands for is then kept from the writer (CombinedPairs)
//   pricing.source, .asOf, .card
//   annotation.count, annotation.providerConfirmed, annotation.<n>.at, .kind, .text, .provider, .model, .source
//   attribution.count, attribution.<n>.label, .side, .grade, .rule, .endpoints, .evidence
//   limitation.count, limitation.<n>, limitation.dataQuality.count, limitation.dataQuality.<n>
//   nextRuns.count, nextRuns.<n>.kind, .period, .endpoint, .reason, .suggestion, .repeatRun
//   requestIds.sample.count, requestIds.sample.<n>   Provider Issue Report only, at most ten
//
// A secondary result (prefix P) states P.name, P.baseline, P.comparison, P.estimate, P.ci95, P.p,
// P.adjustedP, P.rejected, P.items and P.note where the analysis recorded them; P.estimate is always
// present, unavailable with the analysis's note when it was not computed.

/// <summary>What <see cref="BenchmarkChatConsistencyReportFacts.Build(BenchmarkChatConsistencyReportFactsInput)"/> reads.</summary>
public sealed class BenchmarkChatConsistencyReportFactsInput
{
    /// <summary>The saved analysis, with its stored id where it has one.</summary>
    public ChatConsistencyAnalysisResult Result { get; init; } = default!;

    /// <summary>The document the sheet is for; only a Provider Issue Report states sample request ids.</summary>
    public BenchmarkReportAudience Audience { get; init; } = BenchmarkReportAudience.ExecutiveSummary;

    /// <summary>
    /// Provider request ids from the subject's call telemetry in the comparison period, in the order the
    /// caller chose; blanks and repeats are dropped and the first <see cref="BenchmarkChatConsistencyReportFacts.MaxSampleRequestIds"/>
    /// kept. Read for the Provider Issue Report only.
    /// </summary>
    public IReadOnlyList<string>? SampleRequestIds { get; init; }

    /// <summary>
    /// Why the analysis was out of date when the writer started, as the job recorded it after the operator's
    /// acknowledgment ("earlier analysis code (version 4)", "changed inputs" or both); null when it was current.
    /// </summary>
    public string? WrittenOutOfDate { get; init; }
}

/// <summary>
/// Builds the fact sheet of a chat consistency document from a saved analysis. Pure: no I/O, no
/// clock, no culture.
/// </summary>
public static class BenchmarkChatConsistencyReportFacts
{
    /// <summary>The sheet's <see cref="BenchmarkReportFactSheet.SubjectKind"/>.</summary>
    public const string SubjectKind = "ChatConsistency";

    /// <summary>The subject key prefix of a chat consistency document: <c>chat-consistency:12</c>.</summary>
    public const string SubjectKeyPrefix = "chat-consistency:";

    /// <summary>The most sample request ids a Provider Issue Report states.</summary>
    public const int MaxSampleRequestIds = 10;

    public const string TitlePrefix = "Overseer Chat Consistency Report: ";

    public const string ProviderIssueReportUnavailableReason = "No provider-side finding graded Established or Indicated in this analysis.";

    /// <summary>The minus sign of every number display: U+2212, never a hyphen.</summary>
    public const char Minus = '\u2212';

    /// <summary>The space between a number and its unit: U+00A0, so the unit never wraps onto a line of its own.</summary>
    public const char UnitSpace = '\u00A0';

    /// <summary>The key of the analysis's input hash, kept on the sheet for the record only.</summary>
    public const string InputSha256Key = "analysis.inputSha256";

    private const string Baseline = "baseline";
    private const string Comparison = "comparison";

    private static readonly Regex InstrumentNote = new(@"\s*\(instrument [0-9a-fA-F]{12,}\)", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex RawEventValueKey = new(@"^events\.\d+\.(?:from|to)$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Whether a fact stays off the writer's fact list and out of every rendered block:
    /// <see cref="InputSha256Key"/>, and the raw <c>events.&lt;n&gt;.from</c> and <c>.to</c> a sheet written
    /// before <c>events.&lt;n&gt;.change</c> carries, whose values are hashes and JSON.
    /// </summary>
    public static bool WriterHidden(string? key)
        => key != null && (string.Equals(key, InputSha256Key, StringComparison.Ordinal) || RawEventValueKey.IsMatch(key));

    /// <summary>
    /// Whether a fact of <paramref name="sheet"/> stays off the writer's fact list: <see cref="WriterHidden(string)"/>,
    /// or one of <see cref="WriterHiddenKeys"/>. Those stay on the sheet for the rendered blocks.
    /// </summary>
    public static bool WriterHidden(BenchmarkReportFactSheet sheet, string? key)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        return WriterHidden(key) || (key != null && WriterHiddenKeys(sheet.Facts).Contains(key));
    }

    /// <summary>
    /// The facts a sheet keeps from the writer beyond <see cref="WriterHidden(string)"/>: each period's pair a
    /// combined fact states once (<see cref="CombinedPairs"/>), and in a battery comparison where both periods
    /// have a battery Overall Index, each period's <c>level.&lt;period&gt;.quality</c>, the mean answer score,
    /// so that quality has one figure.
    /// </summary>
    public static IReadOnlySet<string> WriterHiddenKeys(IEnumerable<BenchmarkReportFact> facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        var byKey = new Dictionary<string, BenchmarkReportFact>(StringComparer.Ordinal);
        foreach (var fact in facts) byKey.TryAdd(fact.Key, fact);

        var hidden = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (combined, baseline, comparison) in CombinedPairs)
        {
            if (!byKey.ContainsKey(combined)) continue;
            hidden.Add(baseline);
            hidden.Add(comparison);
        }

        bool battery = byKey.TryGetValue("period.baseline.unitNoun", out var noun) && noun.Available && noun.Display == "battery run";
        bool indexes = byKey.TryGetValue("level.baseline.overallIndex", out var b) && b.Available
                       && byKey.TryGetValue("level.comparison.overallIndex", out var c) && c.Available;
        if (battery && indexes)
        {
            hidden.Add("level.baseline.quality");
            hidden.Add("level.comparison.quality");
        }
        return hidden;
    }

    /// <summary>
    /// The facts stated once for both periods when the periods' values are equal, each with the pair it
    /// stands for: retry attempts, own-wait shares, served model IDs and served speeds.
    /// </summary>
    public static readonly IReadOnlyList<(string Combined, string Baseline, string Comparison)> CombinedPairs = new[]
    {
        ("ownWaits.retries", "ownWaits.baseline.retries", "ownWaits.comparison.retries"),
        ("ownWaits.share", "ownWaits.baseline.share", "ownWaits.comparison.share"),
        ("identity.servedModels", "identity.baseline.servedModels", "identity.comparison.servedModels"),
        ("serving.speeds", "serving.baseline.speeds", "serving.comparison.speeds"),
    };

    /// <summary>A control suggestion without its <c>(instrument &lt;hex&gt;)</c> note.</summary>
    public static string WithoutInstrument(string? text)
        => InstrumentNote.Replace(text ?? string.Empty, string.Empty);

    /// <summary>
    /// The analysis's outcome in a few words, as the Results tab titles it: <c>The chat changed</c> when an
    /// endpoint's verdict decides a change, <c>Nothing could be computed</c>, <c>No meaningful change</c> or
    /// <c>No change on the computed endpoints</c> when every computed endpoint stayed within its margin,
    /// else <c>Not enough evidence yet</c>.
    /// </summary>
    public static string OutcomeTitle(IReadOnlyList<ChatConsistencyEndpointResult> endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        var computed = endpoints.Where(e => e.Computed).ToList();
        if (computed.Any(e => e.VerdictLabel is "degraded" or "improved" or "more work" or "less work")) return "The chat changed";
        if (computed.Count == 0) return "Nothing could be computed";
        if (computed.All(e => e.VerdictLabel is "equivalent" or "changed, negligible"))
        {
            return computed.Count == endpoints.Count ? "No meaningful change" : "No change on the computed endpoints";
        }
        return "Not enough evidence yet";
    }

    /// <summary><c>Overseer Chat Consistency Report: &lt;model&gt;</c>.</summary>
    public static string Title(ChatConsistencyAnalysisResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return TitlePrefix + SubjectLabelOf(result);
    }

    /// <summary>The subject's display name, else its model id, else its axis key.</summary>
    public static string SubjectLabelOf(ChatConsistencyAnalysisResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var subject = result.Subject ?? new ChatConsistencySubject();
        return !string.IsNullOrWhiteSpace(subject.DisplayName) ? subject.DisplayName
            : !string.IsNullOrWhiteSpace(subject.ModelId) ? subject.ModelId
            : subject.Key;
    }

    /// <summary><c>chat-consistency:12</c>; <c>chat-consistency</c> for an analysis not yet saved.</summary>
    public static string SubjectKeyOf(int? analysisId)
        => analysisId is int id ? SubjectKeyPrefix + Inv(id) : "chat-consistency";

    /// <summary>
    /// Whether the Provider Issue Report can be written: at least one provider-side attribution graded
    /// Established or Indicated. The reason is <see cref="ProviderIssueReportUnavailableReason"/> when not.
    /// </summary>
    public static (bool Available, string? Reason) ProviderIssueReportAvailability(ChatConsistencyAnalysisResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        bool available = (result.Attribution?.Attributions ?? Array.Empty<ChatConsistencyAttributionResult>())
            .Any(a => string.Equals(a.Side, ChatConsistencyAttribution.SideProvider, StringComparison.Ordinal)
                      && a.Grade is ChatConsistencyEvidenceGrade.Established or ChatConsistencyEvidenceGrade.Indicated);
        return available ? (true, (string?)null) : (false, ProviderIssueReportUnavailableReason);
    }

    /// <summary>The subject's baseline and comparison runs, ascending.</summary>
    public static IReadOnlyList<long> TargetRunIds(ChatConsistencyAnalysisResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return (result.Baseline?.RunIds ?? Array.Empty<long>())
            .Concat(result.Comparison?.RunIds ?? Array.Empty<long>())
            .Distinct()
            .OrderBy(id => id)
            .ToList();
    }

    /// <summary>Every control run the analysis read (candidates, matches and effects), ascending, the subject's runs left out.</summary>
    public static IReadOnlyList<long> ControlRunIds(ChatConsistencyAnalysisResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var targets = TargetRunIds(result).ToHashSet();
        var controls = result.Controls ?? new ChatConsistencyControls();
        return controls.ControlRunIds
            .Concat(controls.Matches.Select(m => m.ControlRunId))
            .Concat(controls.Effects.SelectMany(e => e.ControlBaselineRunIds.Concat(e.ControlComparisonRunIds)))
            .Where(id => !targets.Contains(id))
            .Distinct()
            .OrderBy(id => id)
            .ToList();
    }

    /// <summary>
    /// The runs a chat consistency document fingerprints: the subject's runs, then the control runs
    /// flagged as peers, each ascending.
    /// </summary>
    public static IReadOnlyList<(long RunId, bool IsPeer)> DocumentRuns(ChatConsistencyAnalysisResult result)
        => TargetRunIds(result).Select(id => (id, false))
            .Concat(ControlRunIds(result).Select(id => (id, true)))
            .ToList();

    public static BenchmarkReportFactSheet Build(
        ChatConsistencyAnalysisResult result, BenchmarkReportAudience audience, IReadOnlyList<string>? sampleRequestIds = null,
        string? writtenOutOfDate = null)
        => Build(new BenchmarkChatConsistencyReportFactsInput
        {
            Result = result, Audience = audience, SampleRequestIds = sampleRequestIds, WrittenOutOfDate = writtenOutOfDate
        });

    public static BenchmarkReportFactSheet Build(BenchmarkChatConsistencyReportFactsInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var result = input.Result ?? throw new ArgumentException("An analysis result is required.", nameof(input));
        var subject = result.Subject ?? new ChatConsistencySubject();

        var controls = ControlModels(result);
        var letterOf = controls.ToDictionary(c => c.Key, c => c.Letter, StringComparer.Ordinal);
        var names = BenchmarkComparisonReportFacts.NamesOf(controls
            .Select(c => new BenchmarkReportPeer { Letter = c.Letter, Label = c.Display, DisplayName = c.Display })
            .ToList())
            .Where(n => !SameName(n.Name, subject.DisplayName) && !SameName(n.Name, subject.ModelId))
            .ToList();
        string Lettered(string? text) => BenchmarkComparisonReportFacts.Lettered(text, names) ?? string.Empty;

        var ctx = new Context(result, letterOf, Lettered);
        var facts = new BenchmarkReportFacts.FactList();

        AddAnalysis(facts, result);
        if (!string.IsNullOrWhiteSpace(input.WrittenOutOfDate)) facts.Add("analysis.writtenOutOfDate", input.WrittenOutOfDate.Trim(), input.WrittenOutOfDate.Trim());
        AddSubject(facts, subject);
        AddVerdict(facts, result);
        AddScope(facts, result.Scope ?? new ChatConsistencyScope());
        AddCompared(facts, result);
        AddPeriod(facts, Baseline, result.Baseline ?? new ChatConsistencyPeriodSummary(), result);
        AddPeriod(facts, Comparison, result.Comparison ?? new ChatConsistencyPeriodSummary(), result);
        AddLevels(facts, result);
        AddSample(facts, result);
        AddProtocol(facts, result);
        AddCounts(facts, result);
        foreach (var endpoint in result.Endpoints) AddEndpoint(facts, endpoint);
        AddSecondaryFamilies(facts, ctx);
        AddCommonGrader(facts, result.CommonGrader);
        AddGraderDrift(facts, result.GraderDrift);
        AddReliability(facts, result.Reliability);
        AddEvents(facts, ctx);
        AddEventGroups(facts, ctx);
        AddControls(facts, ctx, controls);
        AddRobustness(facts, ctx);
        AddServing(facts, result);
        AddOwnWaits(facts, result.OwnWaits);
        AddPricing(facts, result.PriceCard ?? new ChatConsistencyPriceCard());
        AddAnnotations(facts, result.Annotations);
        AddAttributions(facts, ctx);
        AddLimitations(facts, ctx);
        AddNextRuns(facts, ctx);
        if (input.Audience == BenchmarkReportAudience.ProviderIssueReport)
        {
            AddRequestIds(facts, input.SampleRequestIds);
        }

        var suites = (result.Baseline?.SuiteNames ?? Array.Empty<string>())
            .Concat(result.Comparison?.SuiteNames ?? Array.Empty<string>())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        return new BenchmarkReportFactSheet
        {
            Scope = BenchmarkReportFactSheet.ChatConsistencyScopeValue,
            SubjectKey = SubjectKeyOf(result.AnalysisId),
            SubjectKind = SubjectKind,
            SubjectLabel = SubjectLabelOf(result),
            SubjectDisplayName = subject.DisplayName,
            SubjectProvider = subject.Provider,
            SubjectModelId = subject.ModelId,
            SubjectThinkingLevel = subject.ThinkingLevel,
            SubjectRunIds = TargetRunIds(result).ToList(),
            SubjectState = "Comparable",
            SubjectExplanation = string.Empty,
            SuiteName = string.Join(", ", suites),
            Peers = controls.Select(c => new BenchmarkReportPeer
            {
                Letter = c.Letter,
                EntryKey = c.Key,
                Label = c.Display,
                DisplayName = c.Display,
                Provider = c.Provider,
                RunIds = c.RunIds.ToList(),
                State = "Comparable"
            }).ToList(),
            Facts = facts.Sorted(),
            KnownNames = KnownNames(result, controls, suites),
            ChatConsistency = new BenchmarkReportChatConsistencySubject
            {
                AnalysisId = result.AnalysisId,
                Name = result.Name,
                Headline = Lettered(result.Headline),
                ProtocolLabel = ProtocolLabelOf(result),
                InputSha256 = result.InputSha256,
                AnalysisCodeVersion = result.AnalysisCodeVersion,
                BaselineRunIds = (result.Baseline?.RunIds ?? Array.Empty<long>()).Distinct().OrderBy(id => id).ToList(),
                ComparisonRunIds = (result.Comparison?.RunIds ?? Array.Empty<long>()).Distinct().OrderBy(id => id).ToList(),
                ControlRunIds = ControlRunIds(result).ToList(),
                ProviderIssueReportAvailable = ProviderIssueReportAvailability(result).Available
            }
        };
    }

    // ---------------------------------------------------------------------------------------------
    // Controls and names
    // ---------------------------------------------------------------------------------------------

    /// <summary>One control model with its letter.</summary>
    private sealed record ControlModel(string Letter, string Key, string Display, string Provider, bool? SameProvider, IReadOnlyList<long> RunIds, IReadOnlyList<string> Periods);

    private sealed record Context(
        ChatConsistencyAnalysisResult Result,
        IReadOnlyDictionary<string, string> LetterOf,
        Func<string?, string> Lettered);

    /// <summary>Every control subject of the matches and effects, lettered by subject key (ordinal).</summary>
    private static List<ControlModel> ControlModels(ChatConsistencyAnalysisResult result)
    {
        var controls = result.Controls ?? new ChatConsistencyControls();
        var keys = controls.Matches.Select(m => m.ControlSubjectKey)
            .Concat(controls.Effects.Select(e => e.ControlSubjectKey))
            .Where(k => !string.IsNullOrWhiteSpace(k))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToList();

        return keys.Select((key, i) =>
        {
            var effects = controls.Effects.Where(e => e.ControlSubjectKey == key).ToList();
            var matches = controls.Matches.Where(m => m.ControlSubjectKey == key).ToList();
            var effect = effects.FirstOrDefault(e => !string.IsNullOrWhiteSpace(e.ControlDisplay));
            var runs = matches.Select(m => m.ControlRunId)
                .Concat(effects.SelectMany(e => e.ControlBaselineRunIds.Concat(e.ControlComparisonRunIds)))
                .Distinct()
                .OrderBy(id => id)
                .ToList();
            var periods = matches.Select(m => m.Period)
                .Concat(effects.Any(e => e.ControlBaselineRunIds.Count > 0) ? new[] { Baseline } : Array.Empty<string>())
                .Concat(effects.Any(e => e.ControlComparisonRunIds.Count > 0) ? new[] { Comparison } : Array.Empty<string>())
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(p => p == Baseline ? 0 : p == Comparison ? 1 : 2)
                .ThenBy(p => p, StringComparer.Ordinal)
                .ToList();
            return new ControlModel(
                BenchmarkReportFacts.LetterFor(i),
                key,
                effect?.ControlDisplay ?? key,
                effects.FirstOrDefault(e => !string.IsNullOrWhiteSpace(e.ControlProvider))?.ControlProvider ?? string.Empty,
                effects.Count > 0 ? (bool?)effects[0].SameProvider : null,
                runs,
                periods);
        }).ToList();
    }

    private static bool SameName(string name, string? other)
        => !string.IsNullOrWhiteSpace(other) && string.Equals(name.Trim(), other.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary><c>Model A</c> for a lettered control; <c>a control model</c> otherwise.</summary>
    private static string ModelOf(Context ctx, string? controlKey)
        => controlKey != null && ctx.LetterOf.TryGetValue(controlKey, out var letter) ? "Model " + letter : "a control model";

    /// <summary>The names the digit rule masks: the subject's, the controls', the providers', the graders', the served model ids, the suites and the compared set's label.</summary>
    private static List<string> KnownNames(ChatConsistencyAnalysisResult result, IReadOnlyList<ControlModel> controls, IReadOnlyList<string> suites)
    {
        var subject = result.Subject ?? new ChatConsistencySubject();
        var served = result.ServedModels ?? new ChatConsistencyServedModels();
        return new string?[] { subject.DisplayName, subject.ModelId, subject.Provider }
            .Concat(controls.SelectMany(c => new string?[] { c.Display, c.Provider }))
            .Concat(suites)
            .Concat(new[] { ComparedSetOf(result)?.Label })
            .Concat(new[] { result.CommonGrader?.Display })
            .Concat(result.GraderDrift.Select(d => (string?)d.Display))
            .Concat(served.Baseline.Concat(served.Comparison).Select(s => (string?)s.ModelId))
            .Concat(result.Annotations.SelectMany(a => new[] { a.Provider, a.ModelId }))
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n!.Trim())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();
    }

    // ---------------------------------------------------------------------------------------------
    // Facts
    // ---------------------------------------------------------------------------------------------

    private static void AddAnalysis(BenchmarkReportFacts.FactList facts, ChatConsistencyAnalysisResult result)
    {
        if (result.AnalysisId is int id) facts.Add("analysis.id", id, "#" + Inv(id));
        if (!string.IsNullOrWhiteSpace(result.Name)) facts.Add("analysis.name", result.Name, result.Name);
        if (!string.IsNullOrWhiteSpace(result.InputSha256))
        {
            facts.Add(InputSha256Key, result.InputSha256, result.InputSha256[..Math.Min(16, result.InputSha256.Length)]);
        }
        facts.Add("analysis.codeVersion", result.AnalysisCodeVersion, Inv(result.AnalysisCodeVersion));
        if (result.CreatedAtUtc is DateTime saved) facts.Add("analysis.savedAt", Iso(saved), When(saved));

        bool relaxed = result.Endpoints.Any(e => e.RelaxedPooling);
        facts.Add("analysis.relaxedPooling", relaxed, relaxed
            ? "runs were pooled across a measurement change, which caps the affected grades at Indicated"
            : "no pooling across a measurement change");
    }

    private static void AddSubject(BenchmarkReportFacts.FactList facts, ChatConsistencySubject subject)
    {
        string label = !string.IsNullOrWhiteSpace(subject.DisplayName) ? subject.DisplayName : subject.ModelId;
        facts.Add("subject.label", label, label);
        facts.Add("subject.provider", subject.Provider, subject.Provider);
        facts.Add("subject.modelId", subject.ModelId, subject.ModelId);

        if (string.IsNullOrWhiteSpace(subject.ThinkingLevel)) facts.Unavailable("subject.thinkingLevel", "The configuration sets no thinking level.");
        else facts.Add("subject.thinkingLevel", subject.ThinkingLevel, subject.ThinkingLevel);

        if (string.IsNullOrWhiteSpace(subject.ServiceTier)) facts.Unavailable("subject.serviceTier", "The configuration requests no service tier.");
        else facts.Add("subject.serviceTier", subject.ServiceTier, subject.ServiceTier);
    }

    /// <summary>The overall verdict's value: the worst decisive change of any computed primary endpoint.</summary>
    public static string OverallVerdict(IReadOnlyList<ChatConsistencyEndpointResult> endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        var computed = endpoints.Where(e => e.Computed).ToList();
        if (computed.Count == 0) return "not computable";
        if (computed.Any(e => e.VerdictLabel == "degraded")) return "degraded";
        if (computed.Any(e => e.VerdictLabel == "improved")) return "improved";
        if (computed.Any(e => e.VerdictLabel is "more work" or "less work")) return "changed";
        if (computed.All(e => e.VerdictLabel is "equivalent" or "changed, negligible")) return "consistent";
        return "inconclusive";
    }

    private static void AddVerdict(BenchmarkReportFacts.FactList facts, ChatConsistencyAnalysisResult result)
    {
        string overall = OverallVerdict(result.Endpoints);
        var parts = result.Endpoints.Select(e => LowerFirst(e.Name) + " "
            + (e.Computed ? e.VerdictLabel + " (" + GradeText(e.Grade).ToLowerInvariant() + ")" : "not computable"));
        facts.Add("verdict.overall", overall, UpperFirst(overall) + (result.Endpoints.Count > 0 ? ": " + string.Join("; ", parts) : string.Empty));
        string outcome = OutcomeTitle(result.Endpoints);
        facts.Add("verdict.short", outcome, outcome);

        var quality = result.Endpoints.FirstOrDefault(e => e.Id == ChatConsistencyEndpointIds.Quality);
        if (quality == null) facts.Unavailable("verdict.quality", "The analysis has no quality endpoint.");
        else if (!quality.Computed) facts.Unavailable("verdict.quality", quality.NotComputedReason ?? "Quality was not computable.");
        else facts.Add("verdict.quality", quality.VerdictLabel, quality.VerdictLabel + " (" + GradeText(quality.Grade) + ")");

        if (!string.IsNullOrWhiteSpace(result.Headline)) facts.Text("verdict.headline", result.Headline);

        var increases = result.HeadlineReliabilityIncreases.Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
        facts.Add("verdict.reliabilityIncreases", increases.Count,
            increases.Count == 0 ? "none established" : string.Join("; ", increases));
    }

    private static void AddScope(BenchmarkReportFacts.FactList facts, ChatConsistencyScope scope)
    {
        bool shared = scope.StrataIndexes.Count > 0 && !string.IsNullOrWhiteSpace(scope.Text);
        if (!shared) facts.Unavailable("scope.hours", NoSharedHoursReason);
        else facts.Add("scope.hours", scope.Text, scope.Text);

        AddNumber(facts, "scope.excludedShare", scope.ExcludedShare, Share);
        facts.Add("scope.oneTimeStratum", scope.OneTimeStratum, scope.OneTimeStratum
            ? "the periods share a single time stratum"
            : "the periods share more than one time stratum");

        facts.Add("coverage.strata.count", scope.StrataUsed.Count, Inv(scope.StrataUsed.Count) + (scope.StrataUsed.Count == 1 ? " stratum" : " strata"));
        for (int i = 0; i < scope.StrataUsed.Count; i++)
        {
            facts.Add("coverage.strata." + Inv(i + 1), scope.StrataUsed[i], scope.StrataUsed[i]);
        }
        facts.Add("coverage.usBusinessHours", scope.UsBusinessHoursCovered, scope.UsBusinessHoursCovered
            ? "the common hours include US business hours"
            : "the common hours leave out US business hours");
        facts.Add("coverage.outsideBusinessHours", scope.OutsideBusinessHoursCovered, scope.OutsideBusinessHoursCovered
            ? "the common hours include hours outside US business hours"
            : "the common hours lie within US business hours only");

        facts.Add("serving.timeOfDayAssessable", scope.TimeOfDayAssessable, scope.TimeOfDayAssessable
            ? "assessable: the common hours include US business hours and other hours"
            : !shared
                ? "not assessable: the periods share no hours"
                : "not assessable: the common hours do not include both US business hours and other hours");
    }

    /// <summary>Why <c>scope.hours</c> is unavailable: the periods share no time-of-week stratum.</summary>
    public const string NoSharedHoursReason = "The periods ran at different hours: they share no time-of-week stratum.";

    /// <summary>The battery or suite compared within, as the analysis recorded it; null before analysis code version 4 and for a run-by-run analysis.</summary>
    public static ChatConsistencyComparedSet? ComparedSetOf(ChatConsistencyAnalysisResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return result.AnalysisCodeVersion > 3 && result.ComparisonSet is { } set && !string.IsNullOrWhiteSpace(set.Key) ? set : null;
    }

    /// <summary>
    /// <c>analysis.compared</c>: "Battery &lt;label&gt;, 4 battery runs" or "Suite &lt;name&gt;, 6 runs", counting the
    /// analyzed units of both periods, or their runs when the result lists no unit.
    /// </summary>
    private static void AddCompared(BenchmarkReportFacts.FactList facts, ChatConsistencyAnalysisResult result)
    {
        if (ComparedSetOf(result) is not { } set) return;

        bool battery = set.Kind == ChatConsistencyComparisonSetKinds.Battery;
        string label = string.IsNullOrWhiteSpace(set.Label) ? set.Key : set.Label.Trim();
        var units = result.Units ?? Array.Empty<ChatConsistencyUnitView>();
        string count = units.Count > 0
            ? Inv(units.Count) + (battery
                ? (units.Count == 1 ? " battery run" : " battery runs")
                : (units.Count == 1 ? " run" : " runs"))
            : Runs((result.Baseline?.RunCount ?? 0) + (result.Comparison?.RunCount ?? 0));
        facts.Add("analysis.compared", set.Key, (battery ? "Battery " : "Suite ") + label + ", " + count);
    }

    /// <summary><c>battery run</c> when the analysis compares battery runs, else <c>run</c>.</summary>
    public static string UnitNounOf(ChatConsistencyAnalysisResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return result.UnitKind == ChatConsistencyComparisonSetKinds.BatteryRunUnit ? "battery run" : "run";
    }

    /// <summary>The analyzed units of a period, ordered by start, then id; the period's runs as run units when the result lists none.</summary>
    private static List<ChatConsistencyUnitView> UnitsOf(ChatConsistencyAnalysisResult result, string name, ChatConsistencyPeriodSummary period)
    {
        var units = (result.Units ?? Array.Empty<ChatConsistencyUnitView>())
            .Where(u => string.Equals(u.Period, name, StringComparison.Ordinal))
            .OrderBy(u => u.StartedAtUtc)
            .ThenBy(u => u.UnitId)
            .ToList();
        if (units.Count > 0) return units;

        return (period.RunIds ?? Array.Empty<long>()).Distinct().OrderBy(id => id)
            .Select(id => new ChatConsistencyUnitView { UnitId = id, Kind = ChatConsistencyComparisonSetKinds.RunUnit, Period = name, MemberRunIds = new[] { id } })
            .ToList();
    }

    /// <summary>The count of a period's analyzed units; its run count when the result lists neither units nor run ids.</summary>
    private static int UnitCountOf(ChatConsistencyAnalysisResult result, string name, ChatConsistencyPeriodSummary period)
    {
        int units = UnitsOf(result, name, period).Count;
        return units > 0 ? units : period.RunCount;
    }

    private static void AddPeriod(BenchmarkReportFacts.FactList facts, string name, ChatConsistencyPeriodSummary period, ChatConsistencyAnalysisResult result)
    {
        string p = "period." + name + ".";
        facts.Add(p + "start", Iso(period.StartUtc), When(period.StartUtc));
        facts.Add(p + "end", Iso(period.EndUtc), When(period.EndUtc));
        facts.Text(p + "window", WindowText(name, period.StartUtc, period.EndUtc, result.Comparison?.StartUtc));
        facts.Add(p + "runs", period.RunCount, Runs(period.RunCount));

        string noun = UnitNounOf(result);
        bool battery = noun == "battery run";
        var units = UnitsOf(result, name, period);
        int unitCount = UnitCountOf(result, name, period);
        facts.Add(p + "units", unitCount, Plural(unitCount, noun));
        facts.Add(p + "unitNoun", noun, noun);

        var members = units.SelectMany(u => u.MemberRunIds).Distinct().ToList();
        string memberText = units.Count == 0
            ? "no run"
            : battery
                ? string.Join("; ", units.Select(u => "battery run #" + Inv(u.UnitId) + " (" + RunList(u.MemberRunIds) + ")"))
                : RunList(members);
        facts.Add(p + "memberRuns", members.Count, memberText);
        facts.Add(p + "days", period.Days.Count, period.Days.Count == 0
            ? "no run day"
            : Inv(period.Days.Count) + (period.Days.Count == 1 ? " day: " : " days: ") + string.Join(", ", period.Days));
        facts.Add(p + "items", period.ItemCount, Items(period.ItemCount));
        facts.Add(p + "answers", period.AnswerCount, Inv(period.AnswerCount) + (period.AnswerCount == 1 ? " answer" : " answers"));
        facts.Add(p + "legacyRuns", period.LegacyRunCount, Inv(period.LegacyRunCount) + " of " + Runs(period.RunCount) + " without call telemetry");
        facts.Add(p + "suites", period.SuiteNames.Count, period.SuiteNames.Count == 0 ? "no suite" : string.Join(", ", period.SuiteNames));

        var hours = result.PeriodHours?.FirstOrDefault(h => string.Equals(h.Period, name, StringComparison.Ordinal));
        if (hours == null) facts.Unavailable(p + "hours", BeforeLevelsReason);
        else facts.Add(p + "hours", hours.Strata.Count, hours.Text);
    }

    /// <summary>
    /// A period's window in words. The baseline reads <c>2026-10-08 00:00 UTC until 14:49 UTC</c> when it ends
    /// within a minute before the comparison starts, its end then the comparison's start (the date left out on
    /// the start's day), and <c>… to …</c> otherwise; the comparison reads <c>from 2026-10-08 14:49 UTC to
    /// 2026-10-08 23:59 UTC</c>.
    /// </summary>
    public static string WindowText(string period, DateTime startUtc, DateTime endUtc, DateTime? comparisonStartUtc)
    {
        if (string.Equals(period, Comparison, StringComparison.Ordinal)) return "from " + When(startUtc) + " to " + When(endUtc);

        if (comparisonStartUtc is DateTime split && split > endUtc && split - endUtc <= TimeSpan.FromMinutes(1))
        {
            bool sameDay = split.Date == startUtc.Date;
            return When(startUtc) + " until " + (sameDay ? split.ToString("HH:mm", CultureInfo.InvariantCulture) + " UTC" : When(split));
        }
        return When(startUtc) + " to " + When(endUtc);
    }

    /// <summary>
    /// <see cref="WindowText(string, DateTime, DateTime, DateTime?)"/> of a period from a sheet's <c>period.&lt;period&gt;.start</c> and <c>.end</c> values,
    /// the baseline's split read from <c>period.comparison.start</c>, so a sheet written before <c>.window</c> reads
    /// the same; null when a value is missing or unreadable.
    /// </summary>
    public static string? WindowText(IReadOnlyList<BenchmarkReportFact> facts, string period)
    {
        ArgumentNullException.ThrowIfNull(facts);
        DateTime? Instant(string key)
            => facts.FirstOrDefault(f => string.Equals(f.Key, key, StringComparison.Ordinal)) is { Available: true, Value: JsonValue value }
               && value.TryGetValue(out string? text)
               && DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed)
                ? parsed
                : null;

        string p = "period." + period + ".";
        return Instant(p + "start") is DateTime start && Instant(p + "end") is DateTime end
            ? WindowText(period, start, end, Instant("period.comparison.start"))
            : null;
    }

    /// <summary>Why a level or a period's hours are unavailable on an analysis saved before they were recorded.</summary>
    public const string BeforeLevelsReason = "This analysis predates analysis code version 6, which records each period's hours and levels.";

    /// <summary>
    /// <c>level.&lt;period&gt;.&lt;measure&gt;</c>: each period's descriptive level of <c>answers</c>, <c>quality</c> (the
    /// native mean), <c>overallIndex</c> (with its 95 % interval when recorded), <c>timeToFirstAnswerText</c>,
    /// <c>streamingRate</c>, <c>outputTokens</c>, <c>costPerQuestion</c> and <c>failedAnswers</c>; unavailable with a
    /// reason where the analysis has no value.
    /// </summary>
    private static void AddLevels(BenchmarkReportFacts.FactList facts, ChatConsistencyAnalysisResult result)
    {
        foreach (string period in new[] { Baseline, Comparison })
        {
            string p = "level." + period + ".";
            var levels = result.PeriodLevels?.FirstOrDefault(l => string.Equals(l.Period, period, StringComparison.Ordinal));
            if (levels == null)
            {
                foreach (string measure in LevelMeasures) facts.Unavailable(p + measure, BeforeLevelsReason);
                continue;
            }

            facts.Add(p + "answers", levels.AnswerCount, Inv(levels.AnswerCount) + (levels.AnswerCount == 1 ? " answer" : " answers"));
            Level(facts, p + "quality", levels.NativeMeanQuality, v => "mean score " + WithUnit(BenchmarkReportFormat.OneDecimal(v), "points"),"No answer of the " + period + " period was graded.");
            if (levels.OverallIndex is double index && double.IsFinite(index))
            {
                string text = BenchmarkReportFormat.OneDecimal(index);
                if (levels.OverallIndexHalfWidth is double half && double.IsFinite(half))
                {
                    text += " (95 % interval " + BenchmarkReportFormat.OneDecimal(index - half) + "–" + BenchmarkReportFormat.OneDecimal(index + half)
                        + (string.IsNullOrWhiteSpace(levels.OverallIndexIntervalNote) ? string.Empty : ", " + levels.OverallIndexIntervalNote.Trim()) + ")";
                }
                facts.Add(p + "overallIndex", index, text);
            }
            else
            {
                facts.Unavailable(p + "overallIndex", result.UnitKind == ChatConsistencyComparisonSetKinds.BatteryRunUnit
                    ? "Not every battery run of the " + period + " period has a current battery analysis with an Overall Index."
                    : "The analysis compares runs, not battery runs, so it has no battery Overall Index.");
            }
            Level(facts, p + "timeToFirstAnswerText", levels.MedianTimeToFirstAnswerTextMs, v => "median " + Seconds(v), "No answer of the " + period + " period recorded call telemetry.");
            Level(facts, p + "streamingRate", levels.MedianStreamingRate, v => "median " + WithUnit(BenchmarkReportFormat.OneDecimal(v), "tokens/s"), "No answer of the " + period + " period has a streaming rate.");
            Level(facts, p + "outputTokens", levels.MeanOutputTokensPerAnswer, v => "mean " + WithUnit(BenchmarkReportFormat.Count(v), "output tokens per answer"), "No answer of the " + period + " period recorded output tokens.");
            Level(facts, p + "costPerQuestion", levels.MeanCostPerQuestionUsd, v => "mean " + UsdPerQuestion(v), "No price card was available, so cost was not computed.");
            facts.Add(p + "failedAnswers", levels.FailedAnswerCount, Inv(levels.FailedAnswerCount) + " of " + Inv(levels.AnswerCount)
                + (levels.AnswerCount == 1 ? " answer" : " answers"));
        }
    }

    private static readonly string[] LevelMeasures =
    {
        "answers", "quality", "overallIndex", "timeToFirstAnswerText", "streamingRate", "outputTokens", "costPerQuestion", "failedAnswers"
    };

    private static void Level(BenchmarkReportFacts.FactList facts, string key, double? value, Func<double, string> format, string reason)
    {
        if (value is double v && double.IsFinite(v)) facts.Add(key, v, format(v));
        else facts.Unavailable(key, reason);
    }

    /// <summary>"$0.0071 per question": four decimals under a cent, else two.</summary>
    private static string UsdPerQuestion(double usd)
        => "$" + (Math.Abs(usd) < 0.01 ? usd.ToString("0.0000", CultureInfo.InvariantCulture) : usd.ToString("0.00", CultureInfo.InvariantCulture)) + " per question";

    /// <summary>
    /// The protocol's minimum sample for quality, work and cost (P1, P4, P5) against the analyzed units:
    /// <c>sample.minimumUnits</c>, <c>.minimumDays</c>, <c>.minimumPairedItems</c>, <c>.met</c>, and while it
    /// is not met <c>sample.shortfall</c>, each part stated as met or missed
    /// (<see cref="ChatConsistencyAnalysisService.MinimumSampleText"/>): <c>1 battery run per period on 1 day,
    /// below the minimum of 2 battery runs on 2 days per period; paired items 32, above the minimum of 20</c>.
    /// The paired items count only where one of those endpoints was computed.
    /// </summary>
    private static void AddSample(BenchmarkReportFacts.FactList facts, ChatConsistencyAnalysisResult result)
    {
        var protocol = result.Protocol ?? ChatConsistencyProtocol.V1;
        string noun = UnitNounOf(result);
        var baseline = result.Baseline ?? new ChatConsistencyPeriodSummary();
        var comparison = result.Comparison ?? new ChatConsistencyPeriodSummary();
        int unitsB = UnitCountOf(result, Baseline, baseline);
        int unitsC = UnitCountOf(result, Comparison, comparison);
        int daysB = baseline.Days.Count;
        int daysC = comparison.Days.Count;
        int? paired = result.Endpoints
            .Where(e => e.Computed && e.Id is ChatConsistencyEndpointIds.Quality or ChatConsistencyEndpointIds.Work or ChatConsistencyEndpointIds.Cost)
            .Select(e => (int?)e.ItemCount)
            .Max();

        facts.Add("sample.minimumUnits", protocol.MinimumRunsPerPeriod, Plural(protocol.MinimumRunsPerPeriod, noun) + " per period");
        facts.Add("sample.minimumDays", protocol.MinimumDaysPerPeriod, Plural(protocol.MinimumDaysPerPeriod, "day") + " per period");
        facts.Add("sample.minimumPairedItems", protocol.MinimumPairedItems, Plural(protocol.MinimumPairedItems, "paired item"));

        bool met = unitsB >= protocol.MinimumRunsPerPeriod && unitsC >= protocol.MinimumRunsPerPeriod
                   && daysB >= protocol.MinimumDaysPerPeriod && daysC >= protocol.MinimumDaysPerPeriod
                   && (paired is not int items || items >= protocol.MinimumPairedItems);
        string minimum = Plural(protocol.MinimumRunsPerPeriod, noun) + " on " + Plural(protocol.MinimumDaysPerPeriod, "day")
            + " per period and " + Plural(protocol.MinimumPairedItems, "paired item");
        facts.Add("sample.met", met, met ? "met: at least " + minimum : "not met: the minimum is " + minimum);

        if (met)
        {
            facts.Unavailable("sample.shortfall", "The runs meet the minimum sample of " + minimum + ".");
            return;
        }

        facts.Text("sample.shortfall", ChatConsistencyAnalysisService.MinimumSampleText(noun, unitsB, daysB, unitsC, daysC, paired, protocol));
    }

    private static string ProtocolLabelOf(ChatConsistencyAnalysisResult result)
        => !string.IsNullOrWhiteSpace(result.ProtocolLabel) ? result.ProtocolLabel : (result.Protocol ?? ChatConsistencyProtocol.V1).Label;

    private static void AddProtocol(BenchmarkReportFacts.FactList facts, ChatConsistencyAnalysisResult result)
    {
        var protocol = result.Protocol ?? ChatConsistencyProtocol.V1;
        string label = ProtocolLabelOf(result);
        facts.Add("protocol.version", protocol.ProtocolVersion, protocol.ProtocolVersion);
        facts.Add("protocol.label", label, label);
        facts.Add("protocol.alpha", protocol.Alpha, protocol.Alpha.ToString("0.###", CultureInfo.InvariantCulture));
        facts.Add("protocol.overridden", protocol.IsOverridden, protocol.IsOverridden
            ? "the published protocol with recorded overrides"
            : "the published protocol, without overrides");

        foreach (var endpoint in protocol.Endpoints)
        {
            // "±3 index points": the unit kept on the line of its number.
            string margin = endpoint.MarginText;
            int space = margin.IndexOf(' ', StringComparison.Ordinal);
            facts.Add("protocol.margin." + endpoint.Id, endpoint.Margin, space > 0 ? margin[..space] + UnitSpace + margin[(space + 1)..] : margin);
        }
    }

    private static void AddCounts(BenchmarkReportFacts.FactList facts, ChatConsistencyAnalysisResult result)
    {
        int targets = TargetRunIds(result).Count;
        int controls = ControlRunIds(result).Count;
        int answers = (result.Baseline?.AnswerCount ?? 0) + (result.Comparison?.AnswerCount ?? 0);
        facts.Add("n.targetRuns", targets, Runs(targets));
        facts.Add("n.controlRuns", controls, Runs(controls));
        facts.Add("n.answers", answers, Inv(answers) + (answers == 1 ? " answer" : " answers"));
    }

    // --- Endpoints -------------------------------------------------------------------------------

    private static void AddEndpoint(BenchmarkReportFacts.FactList facts, ChatConsistencyEndpointResult e)
    {
        if (string.IsNullOrWhiteSpace(e.Id)) return;
        string p = "endpoint." + e.Id + ".";
        bool log = e.Scale == ChatConsistencyEffectScale.LogRatio;

        facts.Add(p + "name", e.Name, e.Name);
        facts.Add(p + "verdict", e.Computed ? e.VerdictLabel : "not computable", e.Computed ? e.VerdictLabel : "not computable");
        string grade = GradeText(e.Grade);
        facts.Add(p + "grade", grade, e.GradeReasons.Count > 0 ? grade + ": " + string.Join("; ", e.GradeReasons) : grade);
        facts.Add(p + "runs", e.BaselineRunCount + e.ComparisonRunCount,
            Runs(e.BaselineRunCount) + " in the baseline and " + Runs(e.ComparisonRunCount) + " in the comparison");
        facts.Add(p + "items", e.ItemCount, Items(e.ItemCount));
        facts.Add(p + "legacyProxy", e.LegacyProxy, e.LegacyProxy
            ? "measured by the legacy proxy, model time per item"
            : "measured directly");
        facts.Add(p + "commonGrader", e.CommonGrader, e.CommonGrader ? "under one common grader" : "without a common grader");
        facts.Add(p + "minimumSampleMet", e.MinimumSampleMet, (e.MinimumSampleMet ? "met" : "not met")
            + (string.IsNullOrWhiteSpace(e.MinimumSampleDetail) ? string.Empty : ": " + e.MinimumSampleDetail.Trim().TrimEnd('.')));

        string[] figures = log
            ? new[] { "estimate", "percent", "ci95", "ci95Low", "ci95High", "ci90", "p", "adjustedP", "mde", "runsForMargin" }
            : new[] { "estimate", "ci95", "ci95Low", "ci95High", "ci90", "p", "adjustedP", "mde", "runsForMargin" };
        if (!e.Computed && !string.IsNullOrWhiteSpace(e.NotComputedKind)) facts.Add(p + "notComputedKind", e.NotComputedKind, e.NotComputedKind);
        if (!e.Computed || e.Estimate is not double estimate || !double.IsFinite(estimate))
        {
            string reason = !e.Computed ? e.NotComputedReason ?? "The endpoint was not computable." : "No estimate was recorded.";
            foreach (string figure in figures) facts.Unavailable(p + figure, reason);
            return;
        }

        // A change in the endpoint's unit, or in percent for a ratio; an interval names its unit once.
        string unit = log ? "%" : e.Unit;
        Func<double, string> number = log ? v => SignedOne(PercentOf(v)) : v => SignedOne(v);
        Func<double, string> change = v => WithUnit(number(v), unit);
        Func<ChatConsistencyInterval, string> interval = ci => WithUnit(number(ci.Lower) + " to " + number(ci.Upper), unit);
        facts.Add(p + "estimate", estimate, log ? SignedPercent(e.EstimatePercent ?? PercentOf(estimate)) : change(estimate));
        if (log) facts.Add(p + "percent", e.EstimatePercent ?? PercentOf(estimate), SignedPercent(e.EstimatePercent ?? PercentOf(estimate)));

        if (e.Ci95 is { } ci95 && double.IsFinite(ci95.Lower) && double.IsFinite(ci95.Upper))
        {
            facts.Text(p + "ci95", interval(ci95));
            facts.Add(p + "ci95Low", ci95.Lower, change(ci95.Lower));
            facts.Add(p + "ci95High", ci95.Upper, change(ci95.Upper));
        }
        else
        {
            foreach (string figure in new[] { "ci95", "ci95Low", "ci95High" }) facts.Unavailable(p + figure, "No 95 % interval was computed.");
        }

        if (e.Ci90 is { } ci90 && double.IsFinite(ci90.Lower) && double.IsFinite(ci90.Upper)) facts.Text(p + "ci90", interval(ci90));
        else facts.Unavailable(p + "ci90", "No 90 % interval was computed.");

        AddP(facts, p + "p", e.PValue, "No p-value was computed.");
        AddP(facts, p + "adjustedP", e.AdjustedPValue, "No adjusted p-value was computed.");

        double? mde = log
            ? e.MinimumDetectableEffectPercent ?? (e.MinimumDetectableEffect is double m ? (double?)PercentOf(m) : null)
            : e.MinimumDetectableEffect;
        if (mde is double value && double.IsFinite(value))
        {
            facts.Add(p + "mde", value, WithUnit("±" + BenchmarkReportFormat.OneDecimal(Math.Abs(value)), unit));
            if (!string.IsNullOrWhiteSpace(e.MinimumDetectableEffectNote)) facts.Text(p + "mdeNote", e.MinimumDetectableEffectNote.Trim());
        }
        else
        {
            facts.Unavailable(p + "mde", e.MinimumDetectableEffectNote ?? "The minimum detectable effect could not be estimated.");
        }

        if (e.RunsPerPeriodForMargin is int runs) facts.Add(p + "runsForMargin", runs, Runs(runs) + " per period");
        else facts.Unavailable(p + "runsForMargin", "Not estimable from the run-to-run spread.");
    }

    // --- Secondary families ----------------------------------------------------------------------

    private static void AddSecondaryFamilies(BenchmarkReportFacts.FactList facts, Context ctx)
    {
        foreach (var family in ctx.Result.SecondaryFamilies)
        {
            switch (family.Id)
            {
                case "reliability":
                case "differenceInDifferences":
                    // Stated as reliability.* and did.* from the rate and control records.
                    continue;

                case "qualityDetail":
                    var used = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var r in family.Results)
                    {
                        string? dimension = r.Id.EndsWith("Level", StringComparison.Ordinal) ? r.Id[..^"Level".Length] : null;
                        string prefix = dimension != null && BenchmarkReportFactLabels.Dimensions.Any(d => d.Key == dimension) ? "quality.dimensions." + dimension + "."
                            : r.Id == "criticalErrors" ? "quality.criticalErrors."
                            : r.Id == "flipRate" ? "flip."
                            : "secondary.qualityDetail." + Unique(KeyPart(r.Id), used) + ".";
                        AddSecondary(facts, prefix, r, ctx);
                    }
                    break;

                case "toolUse":
                    var tools = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var r in family.Results) AddSecondary(facts, "tools." + Unique(KeyPart(r.Id), tools) + ".", r, ctx);
                    break;

                default:
                    string head = "secondary." + KeyPart(family.Id) + ".";
                    var ids = new HashSet<string>(StringComparer.Ordinal) { "note" };
                    foreach (var r in family.Results) AddSecondary(facts, head + Unique(KeyPart(r.Id), ids) + ".", r, ctx);
                    if (!string.IsNullOrWhiteSpace(family.Note)) facts.Text(head + "note", ctx.Lettered(family.Note));
                    break;
            }
        }
    }

    /// <summary>A secondary result under <paramref name="prefix"/>, formatted by its unit.</summary>
    private static void AddSecondary(BenchmarkReportFacts.FactList facts, string prefix, ChatConsistencySecondaryResult r, Context ctx)
    {
        var (value, change) = Formats(r.Unit);
        facts.Add(prefix + "name", ctx.Lettered(r.Name), ctx.Lettered(r.Name));
        if (r.BaselineValue is double b && double.IsFinite(b)) facts.Add(prefix + "baseline", b, value(b));
        if (r.ComparisonValue is double c && double.IsFinite(c)) facts.Add(prefix + "comparison", c, value(c));

        if (r.Estimate is double e && double.IsFinite(e))
        {
            facts.Add(prefix + "estimate", e, change(e));
            if (!string.IsNullOrWhiteSpace(r.Note)) facts.Text(prefix + "note", ctx.Lettered(r.Note));
        }
        else
        {
            facts.Unavailable(prefix + "estimate", string.IsNullOrWhiteSpace(r.Note) ? "Not computable." : ctx.Lettered(r.Note));
        }

        if (r.Ci95 is { } ci && double.IsFinite(ci.Lower) && double.IsFinite(ci.Upper)) facts.Text(prefix + "ci95", change(ci.Lower) + " to " + change(ci.Upper));
        if (r.PValue is double p && double.IsFinite(p)) facts.Add(prefix + "p", p, BenchmarkComparisonReportFacts.PValue(p));
        if (r.AdjustedPValue is double ap && double.IsFinite(ap))
        {
            facts.Add(prefix + "adjustedP", ap, BenchmarkComparisonReportFacts.PValue(ap));
            facts.Add(prefix + "rejected", r.Rejected, r.Rejected
                ? "rejected at the family's false discovery rate"
                : "not rejected at the family's false discovery rate");
        }
        if (r.ItemCount is int items) facts.Add(prefix + "items", items, Items(items));
    }

    /// <summary>How a secondary result's values and changes print: a share in percent, a log ratio as a percent change, else in its unit.</summary>
    private static (Func<double, string> Value, Func<double, string> Change) Formats(string? unit)
    {
        unit = (unit ?? string.Empty).Trim();
        if (unit.StartsWith("share of", StringComparison.Ordinal))
        {
            return (Share, v => WithUnit(SignedOne(v * 100.0), "percentage points"));
        }
        if (unit == "log ratio")
        {
            return (Number, v => SignedPercent(PercentOf(v)));
        }
        return (v => WithUnit(Number(v), unit), v => WithUnit(SignedTwo(v), unit));
    }

    private static void AddCommonGrader(BenchmarkReportFacts.FactList facts, ChatConsistencyCommonGrader? grader)
    {
        if (grader == null)
        {
            facts.Unavailable("quality.commonGrader", "No common grader covered every compared run, so quality compares each run's own grades.");
            return;
        }

        string display = grader.Display + " (" + Runs(grader.CoveredRunIds.Count) + " re-graded"
            + (grader.UncoveredControlRunIds.Count > 0
                ? "; " + Inv(grader.UncoveredControlRunIds.Count) + " control " + (grader.UncoveredControlRunIds.Count == 1 ? "run" : "runs") + " not covered and left out of the quality difference in differences"
                : string.Empty)
            + ")";
        facts.Add("quality.commonGrader", grader.Display, display);
    }

    private static void AddGraderDrift(BenchmarkReportFacts.FactList facts, IReadOnlyList<ChatConsistencyGraderDrift> drift)
    {
        facts.Add("grader.drift.count", drift.Count, Inv(drift.Count) + (drift.Count == 1 ? " anchor run" : " anchor runs"));
        for (int i = 0; i < drift.Count; i++)
        {
            var d = drift[i];
            string p = "grader.drift." + Inv(i + 1) + ".";
            facts.Add(p + "anchorRun", d.AnchorRunId, RunText(d.AnchorRunId));
            facts.Add(p + "grader", d.Display, d.Display);
            facts.Add(p + "earliest", Iso(d.EarliestAtUtc), When(d.EarliestAtUtc));
            facts.Add(p + "latest", Iso(d.LatestAtUtc), When(d.LatestAtUtc));
            AddNumber(facts, p + "drift", d.Drift, v => WithUnit(SignedOne(v), "index points"));
            facts.Add(p + "items", d.ItemCount, Items(d.ItemCount));
            facts.Add(p + "withinMargin", d.WithinMargin, d.WithinMargin ? "within the grader-drift margin" : "outside the grader-drift margin");
        }
    }

    private static void AddReliability(BenchmarkReportFacts.FactList facts, IReadOnlyList<ChatConsistencyRateResult> rates)
    {
        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (var r in rates)
        {
            string p = "reliability." + Unique(KeyPart(r.Id), used) + ".";
            facts.Add(p + "name", r.Name, r.Name);
            AddRate(facts, p + "baseline", r.BaselineCount, r.BaselineTotal, r.BaselineRate, r.Denominator, "baseline");
            AddRate(facts, p + "comparison", r.ComparisonCount, r.ComparisonTotal, r.ComparisonRate, r.Denominator, "comparison");
            AddP(facts, p + "p", r.PValue, "No test was possible.");
            AddP(facts, p + "adjustedP", r.AdjustedPValue, "No test was possible.");
            facts.Add(p + "increased", r.Increased, r.Increased ? "higher in the comparison period" : "not higher in the comparison period");
            facts.Add(p + "establishedIncrease", r.EstablishedIncrease, r.EstablishedIncrease
                ? "an established increase"
                : "not an established increase");
        }
    }

    private static void AddRate(BenchmarkReportFacts.FactList facts, string key, int count, int total, double? rate, string denominator, string period)
    {
        string noun = string.IsNullOrWhiteSpace(denominator) ? "answers" : denominator;
        if (rate is not double value || !double.IsFinite(value))
        {
            facts.Unavailable(key, "The " + period + " period has no " + noun + " to count.");
            return;
        }
        facts.Add(key, value, Inv(count) + " of " + Inv(total) + " " + noun + " (" + Share(value) + ")");
    }

    // --- Events, controls, robustness ------------------------------------------------------------

    private static void AddEvents(BenchmarkReportFacts.FactList facts, Context ctx)
    {
        var events = ctx.Result.Events;
        facts.Add("events.count", events.Count, events.Count == 1 ? "1 Overseer event" : Inv(events.Count) + " Overseer events");
        for (int i = 0; i < events.Count; i++)
        {
            var e = events[i];
            string p = "events." + Inv(i + 1) + ".";
            facts.Add(p + "at", Iso(e.AtUtc), When(e.AtUtc));
            facts.Add(p + "kind", e.Kind, EventKindText(e.Kind));
            facts.Add(p + "label", ctx.Lettered(e.Label), ctx.Lettered(e.Label));
            facts.Text(p + "change", ChatConsistencyEventText.Describe(e.Kind, e.From, e.To));
            facts.Add(p + "run", e.RunId, UnitRunList(ctx.Result, new[] { e.RunId }));
            facts.Add(p + "previousRun", e.PreviousRunId, UnitRunList(ctx.Result, new[] { e.PreviousRunId }));
            string series = e.InTargetSeries ? "the model under test" : ModelOf(ctx, e.SubjectKey);
            facts.Add(p + "series", e.InTargetSeries ? "target" : "control", series);
        }
    }

    /// <summary>
    /// The events grouped into Overseer updates (<see cref="EventGroups"/>), tagged E1, E2, … in time order:
    /// each group's earliest and latest time, its runs those the events were first seen at (by their battery
    /// run in a battery comparison, <see cref="UnitRunList"/>), and its changes the events'
    /// <see cref="ChatConsistencyEventText.Describe"/>, joined with <c>; </c>.
    /// </summary>
    private static void AddEventGroups(BenchmarkReportFacts.FactList facts, Context ctx)
    {
        var groups = EventGroups(ctx.Result.Events, ctx.Result.Baseline?.EndUtc, ctx.Result.Comparison?.StartUtc);

        facts.Add("eventGroups.count", groups.Count, groups.Count == 1 ? "1 Overseer update" : Inv(groups.Count) + " Overseer updates");
        for (int i = 0; i < groups.Count; i++)
        {
            var group = groups[i].Events;
            var first = group[0];
            var last = group[^1];
            string p = "eventGroups." + Inv(i + 1) + ".";
            var runs = group.Select(e => e.RunId).Distinct().OrderBy(id => id).ToList();
            var changes = group.Select(e => ChatConsistencyEventText.Describe(e.Kind, e.From, e.To)).Distinct(StringComparer.Ordinal);

            facts.Add(p + "tag", groups[i].Tag, groups[i].Tag);
            facts.Add(p + "at", Iso(first.AtUtc), When(first.AtUtc));
            facts.Add(p + "lastAt", Iso(last.AtUtc), When(last.AtUtc));
            facts.Add(p + "run", runs[0], UnitRunList(ctx.Result, runs));
            facts.Text(p + "changes", string.Join("; ", changes));
            facts.Add(p + "series", first.InTargetSeries ? "target" : "control", first.InTargetSeries ? "the model under test" : ModelOf(ctx, first.SubjectKey));
        }
    }

    /// <summary>
    /// An event kind as the event table words it, never as a field name: <c>corpus re-index</c>, <c>harness
    /// version</c>, <c>source code revision</c>, <c>wiki revision</c>.
    /// </summary>
    public static string EventKindText(string? kind)
        => kind == OverseerEventKinds.CorpusIndex ? "corpus re-index" : LowerFirst(ChatConsistencyEventText.Label(kind));

    /// <summary>
    /// Runs as the analysis's units name them. In a battery comparison each run that is a member of an analyzed
    /// battery run reads under it, <c>battery run #12 (run #98)</c>, the members of one battery run together, and
    /// any other run (a control's) as <see cref="RunList"/> does; in any other analysis <see cref="RunList"/>.
    /// </summary>
    private static string UnitRunList(ChatConsistencyAnalysisResult result, IReadOnlyCollection<long> runIds)
    {
        if (result.UnitKind != ChatConsistencyComparisonSetKinds.BatteryRunUnit) return RunList(runIds);

        var unitOf = new Dictionary<long, long>();
        foreach (var unit in result.Units ?? Array.Empty<ChatConsistencyUnitView>())
        {
            foreach (long member in unit.MemberRunIds) unitOf.TryAdd(member, unit.UnitId);
        }

        var parts = runIds.Distinct()
            .OrderBy(id => id)
            .GroupBy(id => unitOf.TryGetValue(id, out long unit) ? (long?)unit : null)
            .OrderBy(g => g.Min())
            .Select(g => g.Key is long unit ? "battery run #" + Inv(unit) + " (" + RunList(g.ToList()) + ")" : RunList(g.ToList()))
            .ToList();
        return parts.Count == 0 ? "no run" : BenchmarkReportFormat.LetterList(parts);
    }

    /// <summary>One Overseer update: its tag (<c>E1</c>, …) and its events, in time, then run order.</summary>
    public sealed record EventGroup(string Tag, IReadOnlyList<ChatConsistencyEventView> Events);

    /// <summary>
    /// The events grouped into Overseer updates by <see cref="EventGroupKey"/>, ordered by their earliest event
    /// (time, then run, the target series first) and tagged <c>E1</c>, <c>E2</c>, … in that order. The client's
    /// report charts tag events the same way; a shared JSON fixture pins both.
    /// </summary>
    public static IReadOnlyList<EventGroup> EventGroups(
        IReadOnlyList<ChatConsistencyEventView> events, DateTime? baselineEndUtc, DateTime? comparisonStartUtc)
    {
        ArgumentNullException.ThrowIfNull(events);
        return events
            .GroupBy(e => EventGroupKey(e, HarnessOf(e, events), baselineEndUtc, comparisonStartUtc), StringComparer.Ordinal)
            .Select(g => g.OrderBy(e => e.AtUtc).ThenBy(e => e.RunId).ToList())
            .OrderBy(g => g[0].AtUtc)
            .ThenBy(g => g[0].RunId)
            .ThenBy(g => g[0].InTargetSeries ? 0 : 1)
            .ThenBy(g => g[0].SubjectKey ?? string.Empty, StringComparer.Ordinal)
            .Select((g, i) => new EventGroup("E" + Inv(i + 1), g))
            .ToList();
    }

    /// <summary>
    /// The key one Overseer update's events share: the UTC day, the series (empty for the model under test,
    /// else the control's subject key), the harness version of the event's run (<see cref="HarnessOf"/>) and the
    /// side of the period split (<c>baseline</c> up to the baseline's end, <c>comparison</c> from the
    /// comparison's start, else <c>between</c>), joined with <c>|</c>.
    /// </summary>
    public static string EventGroupKey(ChatConsistencyEventView e, string harness, DateTime? baselineEndUtc, DateTime? comparisonStartUtc)
    {
        ArgumentNullException.ThrowIfNull(e);
        string side = comparisonStartUtc is DateTime cs && e.AtUtc >= cs ? Comparison
            : baselineEndUtc is DateTime be && e.AtUtc <= be ? Baseline
            : "between";
        string series = e.InTargetSeries ? string.Empty : e.SubjectKey ?? string.Empty;
        return e.AtUtc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "|" + series + "|" + harness + "|" + side;
    }

    /// <summary>
    /// The harness version of an event's run, from the harness events of its series: the <c>To</c> of the latest
    /// one at or before it (by time, then run), else the <c>From</c> of the earliest one after it, else empty.
    /// </summary>
    public static string HarnessOf(ChatConsistencyEventView e, IReadOnlyList<ChatConsistencyEventView> events)
    {
        ArgumentNullException.ThrowIfNull(e);
        ArgumentNullException.ThrowIfNull(events);
        var harness = events
            .Where(x => x.Kind == OverseerEventKinds.HarnessVersion && x.InTargetSeries == e.InTargetSeries
                        && (e.InTargetSeries || string.Equals(x.SubjectKey, e.SubjectKey, StringComparison.Ordinal)))
            .OrderBy(x => x.AtUtc)
            .ThenBy(x => x.RunId)
            .ToList();
        var atOrBefore = harness.LastOrDefault(x => x.AtUtc < e.AtUtc || (x.AtUtc == e.AtUtc && x.RunId <= e.RunId));
        if (atOrBefore != null) return atOrBefore.To ?? string.Empty;
        return harness.FirstOrDefault()?.From ?? string.Empty;
    }

    private static void AddControls(BenchmarkReportFacts.FactList facts, Context ctx, IReadOnlyList<ControlModel> controls)
    {
        facts.Add("controls.count", controls.Count, Inv(controls.Count) + (controls.Count == 1 ? " control model" : " control models"));
        for (int i = 0; i < controls.Count; i++)
        {
            var c = controls[i];
            string p = "controls." + Inv(i + 1) + ".";
            facts.Add(p + "model", "Model " + c.Letter, "Model " + c.Letter);
            if (c.SameProvider is bool same)
            {
                facts.Add(p + "sameProvider", same, same ? "the same provider as the model under test" : "another provider than the model under test");
            }
            else
            {
                facts.Unavailable(p + "sameProvider", "No effect was estimated against this control.");
            }
            facts.Add(p + "runs", c.RunIds.Count, Runs(c.RunIds.Count));
            facts.Add(p + "periods", c.Periods.Count, c.Periods.Count == 0 ? "no period" : string.Join(" and ", c.Periods));
        }

        var missing = (ctx.Result.Controls ?? new ChatConsistencyControls()).MissingControls;
        facts.Add("controls.missing.count", missing.Count, Plural(missing.Count, "missing-control note"));
        for (int i = 0; i < missing.Count; i++)
        {
            var m = missing[i];
            string p = "controls.missing." + Inv(i + 1) + ".";
            string suggestion = ctx.Lettered(WithoutInstrument(m.SuggestedText));
            facts.Add(p + "period", m.Period, m.Period);
            facts.Add(p + "suite", m.SuiteName, m.SuiteName);
            facts.Add(p + "suggestion", suggestion, suggestion);
            facts.Add(p + "targetRun", m.TargetRunId, RunText(m.TargetRunId));
            if (m.BatteryRunId is long battery) facts.Add(p + "batteryRun", battery, "battery run #" + Inv(battery));
            if (m.BuildReplaced) facts.Add(p + "buildReplaced", true, "its Overseer build has been replaced, so no control run can be made under it");
        }

        var effects = (ctx.Result.Controls ?? new ChatConsistencyControls()).Effects;
        facts.Add("did.count", effects.Count, Inv(effects.Count) + (effects.Count == 1 ? " estimate" : " estimates"));
        var protocol = ctx.Result.Protocol ?? ChatConsistencyProtocol.V1;
        for (int i = 0; i < effects.Count; i++)
        {
            var e = effects[i];
            string p = "did." + Inv(i + 1) + ".";
            var endpoint = protocol.Endpoints.FirstOrDefault(x => x.Id == e.EndpointId);
            bool log = endpoint?.Scale == ChatConsistencyEffectScale.LogRatio;
            string unit = log ? "%" : endpoint?.Unit ?? string.Empty;
            Func<double, string> number = log ? v => SignedOne(PercentOf(v)) : v => SignedOne(v);
            Func<double, string> change = v => WithUnit(number(v), unit);
            Func<ChatConsistencyInterval, string> interval = ci => WithUnit(number(ci.Lower) + " to " + number(ci.Upper), unit);

            facts.Add(p + "endpoint", e.EndpointId, endpoint == null ? e.EndpointId : endpoint.Name + " (" + e.EndpointId + ")");
            facts.Add(p + "model", ModelOf(ctx, e.ControlSubjectKey), ModelOf(ctx, e.ControlSubjectKey));
            facts.Add(p + "items", e.ItemCount, Items(e.ItemCount));

            if (e.ControlChange is double cc && double.IsFinite(cc)) facts.Add(p + "controlChange", cc, change(cc));
            else facts.Unavailable(p + "controlChange", "The control's own change could not be estimated.");
            if (e.ControlChangeCi95 is { } cci && double.IsFinite(cci.Lower) && double.IsFinite(cci.Upper)) facts.Text(p + "controlChangeCi95", interval(cci));
            else facts.Unavailable(p + "controlChangeCi95", "No interval was computed for the control's own change.");

            if (e.DidEstimate is double d && double.IsFinite(d)) facts.Add(p + "estimate", d, change(d));
            else facts.Unavailable(p + "estimate", "The difference in differences could not be estimated.");
            if (e.DidCi95 is { } dci && double.IsFinite(dci.Lower) && double.IsFinite(dci.Upper)) facts.Text(p + "ci95", interval(dci));
            else facts.Unavailable(p + "ci95", "No interval was computed for the difference in differences.");
            AddP(facts, p + "p", e.DidPValue, "No p-value was computed.");

            facts.Add(p + "includesZero", e.DidIncludesZero, e.DidIncludesZero ? "the interval includes zero" : "the interval excludes zero");
            facts.Add(p + "separatesTarget", e.DidSeparatesTarget, e.DidSeparatesTarget
                ? "separates the model under test from the control"
                : "does not separate the model under test from the control");
            facts.Add(p + "movedSameWay", e.ControlMovedSameWay, e.ControlMovedSameWay
                ? "the control moved the same way"
                : "the control did not move the same way");
        }
    }

    private static void AddRobustness(BenchmarkReportFacts.FactList facts, Context ctx)
    {
        var checks = ctx.Result.RobustnessChecks.Count > 0
            ? ctx.Result.RobustnessChecks
            : ctx.Result.Endpoints.SelectMany(e => e.RobustnessChecks).ToList();
        int failed = checks.Count(c => c.Status == ChatConsistencyCheckStatus.Failed);
        facts.Add("robustness.count", checks.Count, Inv(checks.Count) + (checks.Count == 1 ? " check" : " checks"));
        facts.Add("robustness.failed", failed, Inv(failed) + " of " + Inv(checks.Count) + (checks.Count == 1 ? " check" : " checks"));
        for (int i = 0; i < checks.Count; i++)
        {
            var c = checks[i];
            string p = "robustness." + Inv(i + 1) + ".";
            facts.Add(p + "endpoint", c.EndpointId, c.EndpointId);
            facts.Add(p + "name", c.Name, c.Name);
            string status = c.Status switch
            {
                ChatConsistencyCheckStatus.Passed => "passed",
                ChatConsistencyCheckStatus.Failed => "failed",
                _ => "not assessable"
            };
            facts.Add(p + "status", status, status);
            if (!string.IsNullOrWhiteSpace(c.Detail)) facts.Text(p + "detail", ctx.Lettered(c.Detail));
        }

        // robustness.<check>.summary: one clause per status and detail, naming every endpoint that shares them.
        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (var byName in checks.Where(c => !string.IsNullOrWhiteSpace(c.Name)).GroupBy(c => c.Name, StringComparer.Ordinal))
        {
            var clauses = byName
                .GroupBy(c => (c.Status, Detail: (c.Detail ?? string.Empty).Trim()))
                .Select(g =>
                {
                    var endpoints = g.Select(c => c.EndpointId).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal).ToList();
                    string status = g.Key.Status switch
                    {
                        ChatConsistencyCheckStatus.Passed => "passed",
                        ChatConsistencyCheckStatus.Failed => "failed",
                        _ => "not assessable"
                    };
                    string detail = g.Key.Detail.TrimEnd('.');
                    return status + (endpoints.Count > 0 ? " for " + BenchmarkReportFormat.LetterList(endpoints) : string.Empty)
                        + (detail.Length > 0 ? ": " + LowerFirst(ctx.Lettered(detail)) : string.Empty);
                })
                .ToList();
            facts.Text("robustness." + Unique(KeyPart(LowerFirst(byName.Key)), used) + ".summary", UpperFirst(string.Join("; ", clauses)) + ".");
        }
    }

    // --- Serving, waits, pricing -----------------------------------------------------------------

    private static void AddServing(BenchmarkReportFacts.FactList facts, ChatConsistencyAnalysisResult result)
    {
        var served = result.ServedModels ?? new ChatConsistencyServedModels();
        foreach (var (period, models, calls, mismatch, fallback, speeds) in new[]
        {
            (Baseline, served.Baseline, served.BaselineCalls, served.BaselineTierMismatchCalls, served.BaselineFallbackCalls, served.BaselineServedSpeeds),
            (Comparison, served.Comparison, served.ComparisonCalls, served.ComparisonTierMismatchCalls, served.ComparisonFallbackCalls, served.ComparisonServedSpeeds)
        })
        {
            if (models.Count == 0) facts.Unavailable("identity." + period + ".servedModels", "No served model ID was recorded in the " + period + " period.");
            else facts.Add("identity." + period + ".servedModels", models.Count,
                string.Join(", ", models.Select(m => m.ModelId + " (" + Inv(m.CallCount) + (m.CallCount == 1 ? " call)" : " calls)"))));
            facts.Add("identity." + period + ".calls", calls, Inv(calls) + (calls == 1 ? " candidate call" : " candidate calls"));

            facts.Add("serving." + period + ".tierMismatchCalls", mismatch, Inv(mismatch) + " of " + Inv(calls) + (calls == 1 ? " call" : " calls"));
            facts.Add("serving." + period + ".fallbackCalls", fallback, Inv(fallback) + " of " + Inv(calls) + (calls == 1 ? " call" : " calls"));
            if (speeds.Count == 0) facts.Unavailable("serving." + period + ".speeds", "No served speed was recorded in the " + period + " period.");
            else facts.Add("serving." + period + ".speeds", speeds.Count, string.Join(", ", speeds));
        }

        // One fact for both periods where they were served alike, so a sentence never repeats one value.
        if (served.Baseline.Count > 0 && served.Baseline.Select(m => m.ModelId).SequenceEqual(served.Comparison.Select(m => m.ModelId), StringComparer.Ordinal))
        {
            int baselineCalls = served.Baseline.Sum(m => m.CallCount);
            int comparisonCalls = served.Comparison.Sum(m => m.CallCount);
            facts.Add("identity.servedModels", served.Baseline.Count, string.Join(", ", served.Baseline.Select(m => m.ModelId))
                + " in both periods (" + Inv(baselineCalls) + " and " + Inv(comparisonCalls) + (comparisonCalls == 1 ? " call)" : " calls)"));
        }
        if (served.BaselineServedSpeeds.Count > 0 && served.BaselineServedSpeeds.SequenceEqual(served.ComparisonServedSpeeds, StringComparer.Ordinal))
        {
            facts.Add("serving.speeds", served.BaselineServedSpeeds.Count, string.Join(", ", served.BaselineServedSpeeds) + " in both periods");
        }

        bool recorded = served.Baseline.Count > 0 && served.Comparison.Count > 0;
        facts.Add("identity.changed", served.Changed, served.Changed
            ? "the served model IDs differ between the periods"
            : recorded ? "the served model IDs are the same in both periods" : "not comparable: a period recorded no served model ID");
        facts.Add("serving.configurationDiffers", served.ServedConfigurationDiffers, served.ServedConfigurationDiffers
            ? "some calls were served at another tier than requested or by a fallback model"
            : "every call was served as requested");
    }

    private static void AddOwnWaits(BenchmarkReportFacts.FactList facts, IReadOnlyList<ChatConsistencyOwnWaits> waits)
    {
        var periods = waits.Where(w => w.Period is Baseline or Comparison).GroupBy(w => w.Period).Select(g => g.First()).ToList();
        foreach (var w in periods)
        {
            string p = "ownWaits." + w.Period + ".";
            if (w.OwnWaitShare is double share && double.IsFinite(share)) facts.Add(p + "share", share, Share(share));
            else facts.Unavailable(p + "share", "No answer of the " + w.Period + " period has call telemetry.");
            facts.Add(p + "permitWait", w.PermitWaitMs, Seconds(w.PermitWaitMs));
            facts.Add(p + "backoffWait", w.BackoffWaitMs, Seconds(w.BackoffWaitMs));
            facts.Add(p + "retries", w.RetryAttemptCount, RetryAttempts(w.RetryAttemptCount));
            facts.Add(p + "answersWithTelemetry", w.AnswersWithTelemetry, Inv(w.AnswersWithTelemetry) + (w.AnswersWithTelemetry == 1 ? " answer" : " answers"));
        }

        // One fact for both periods where their values print alike, so a sentence never repeats one value.
        var baseline = periods.FirstOrDefault(w => w.Period == Baseline);
        var comparison = periods.FirstOrDefault(w => w.Period == Comparison);
        if (baseline == null || comparison == null) return;
        if (baseline.RetryAttemptCount == comparison.RetryAttemptCount)
        {
            facts.Add("ownWaits.retries", baseline.RetryAttemptCount, RetryAttempts(baseline.RetryAttemptCount) + " in both periods");
        }
        if (baseline.OwnWaitShare is double b && double.IsFinite(b) && comparison.OwnWaitShare is double c && double.IsFinite(c) && Share(b) == Share(c))
        {
            facts.Add("ownWaits.share", b, Share(b) + " in both periods");
        }
    }

    private static string RetryAttempts(int count) => Inv(count) + (count == 1 ? " retry attempt" : " retry attempts");

    private static void AddPricing(BenchmarkReportFacts.FactList facts, ChatConsistencyPriceCard card)
    {
        if (string.IsNullOrWhiteSpace(card.Source)) facts.Unavailable("pricing.source", "No price card source was recorded.");
        else facts.Add("pricing.source", card.Source, card.Source);

        if (string.IsNullOrWhiteSpace(card.AsOf)) facts.Unavailable("pricing.asOf", "The price card records no date.");
        else facts.Add("pricing.asOf", card.AsOf, card.AsOf);

        if (!card.Available || card.InputPerMillion is not decimal input || card.OutputPerMillion is not decimal output)
        {
            facts.Unavailable("pricing.card", "No price card was available, so cost was not compared.");
            return;
        }

        string text = Usd(input) + " input and " + Usd(output) + " output per million tokens";
        if (card.CachedInputPerMillion is decimal cached) text += ", " + Usd(cached) + " cached input";
        if (card.CacheWritePerMillion is decimal write) text += ", " + Usd(write) + " cache write";
        facts.Text("pricing.card", text);
    }

    // --- Annotations, attributions, limitations, next runs, request ids --------------------------

    private static void AddAnnotations(BenchmarkReportFacts.FactList facts, IReadOnlyList<ChatConsistencyAnnotationView> annotations)
    {
        int confirmed = annotations.Count(a => a.Kind == ChatConsistencyAnnotationKind.ProviderConfirmedCause);
        facts.Add("annotation.count", annotations.Count, Inv(annotations.Count) + (annotations.Count == 1 ? " annotation" : " annotations"));
        facts.Add("annotation.providerConfirmed", confirmed, Inv(confirmed) + (confirmed == 1 ? " provider-confirmed cause" : " provider-confirmed causes"));
        for (int i = 0; i < annotations.Count; i++)
        {
            var a = annotations[i];
            string p = "annotation." + Inv(i + 1) + ".";
            facts.Add(p + "at", Iso(a.AtUtc), When(a.AtUtc));
            facts.Add(p + "kind", a.Kind.ToString(), AnnotationKindText(a.Kind));
            facts.Add(p + "text", a.Text, a.Text);
            facts.Add(p + "provider", a.Provider, string.IsNullOrWhiteSpace(a.Provider) ? "every provider" : a.Provider);
            facts.Add(p + "model", a.ModelId, string.IsNullOrWhiteSpace(a.ModelId) ? "every model" : a.ModelId);
            if (!string.IsNullOrWhiteSpace(a.SourceUrl)) facts.Add(p + "source", a.SourceUrl, a.SourceUrl);
        }
    }

    private static string AnnotationKindText(ChatConsistencyAnnotationKind kind) => kind switch
    {
        ChatConsistencyAnnotationKind.ModelRelease => "model release",
        ChatConsistencyAnnotationKind.ProviderStatement => "provider statement",
        ChatConsistencyAnnotationKind.ProviderConfirmedCause => "provider-confirmed cause",
        ChatConsistencyAnnotationKind.PriceChange => "price change",
        ChatConsistencyAnnotationKind.OurChange => "our change",
        _ => "other"
    };

    private static void AddAttributions(BenchmarkReportFacts.FactList facts, Context ctx)
    {
        var attributions = (ctx.Result.Attribution ?? new ChatConsistencyAttributionOutcome()).Attributions;
        facts.Add("attribution.count", attributions.Count, Inv(attributions.Count) + (attributions.Count == 1 ? " attribution" : " attributions"));
        for (int i = 0; i < attributions.Count; i++)
        {
            var a = attributions[i];
            string p = "attribution." + Inv(i + 1) + ".";
            facts.Add(p + "label", ctx.Lettered(a.Label), ctx.Lettered(a.Label));
            facts.Add(p + "side", a.Side, a.Side switch
            {
                ChatConsistencyAttribution.SideOurs => "our change",
                ChatConsistencyAttribution.SideProvider => "the provider's side",
                ChatConsistencyAttribution.SideInfrastructure => "our infrastructure",
                _ => "undetermined"
            });
            string grade = GradeText(a.Grade);
            facts.Add(p + "grade", grade, grade);
            facts.Add(p + "rule", a.Rule, a.Rule);
            facts.Add(p + "endpoints", a.Endpoints.Count, a.Endpoints.Count == 0 ? "no endpoint" : string.Join(", ", a.Endpoints));
            if (!string.IsNullOrWhiteSpace(a.Evidence)) facts.Text(p + "evidence", ctx.Lettered(a.Evidence));
        }
    }

    private static void AddLimitations(BenchmarkReportFacts.FactList facts, Context ctx)
    {
        var limitations = ctx.Result.Limitations.Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
        facts.Add("limitation.count", limitations.Count, Inv(limitations.Count) + (limitations.Count == 1 ? " limitation" : " limitations"));
        for (int i = 0; i < limitations.Count; i++) facts.Text("limitation." + Inv(i + 1), ctx.Lettered(limitations[i]));

        var notes = ctx.Result.DataQuality.Where(n => !string.IsNullOrWhiteSpace(n.Text)).ToList();
        facts.Add("limitation.dataQuality.count", notes.Count, Inv(notes.Count) + (notes.Count == 1 ? " data-quality note" : " data-quality notes"));
        for (int i = 0; i < notes.Count; i++) facts.Text("limitation.dataQuality." + Inv(i + 1), ctx.Lettered(notes[i].Text));
    }

    private static void AddNextRuns(BenchmarkReportFacts.FactList facts, Context ctx)
    {
        var runs = ctx.Result.NextRuns;
        facts.Add("nextRuns.count", runs.Count, Inv(runs.Count) + (runs.Count == 1 ? " suggested run" : " suggested runs"));
        for (int i = 0; i < runs.Count; i++)
        {
            var r = runs[i];
            string p = "nextRuns." + Inv(i + 1) + ".";
            facts.Add(p + "kind", r.Kind, r.Kind);
            if (!string.IsNullOrWhiteSpace(r.Period)) facts.Add(p + "period", r.Period, r.Period);
            if (!string.IsNullOrWhiteSpace(r.EndpointId)) facts.Add(p + "endpoint", r.EndpointId, r.EndpointId);
            if (!string.IsNullOrWhiteSpace(r.Reason)) facts.Text(p + "reason", ctx.Lettered(WithoutInstrument(r.Reason)));
            if (!string.IsNullOrWhiteSpace(r.Suggestion)) facts.Text(p + "suggestion", ctx.Lettered(WithoutInstrument(r.Suggestion)));
            if (r.RepeatRunId is long repeat) facts.Add(p + "repeatRun", repeat, RunText(repeat));
        }
    }

    /// <summary>The sample request ids a Provider Issue Report states: blanks and repeats dropped, the first ten kept in order.</summary>
    public static IReadOnlyList<string> SampleOf(IReadOnlyList<string>? requestIds)
        => (requestIds ?? Array.Empty<string>())
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .Distinct(StringComparer.Ordinal)
            .Take(MaxSampleRequestIds)
            .ToList();

    private static void AddRequestIds(BenchmarkReportFacts.FactList facts, IReadOnlyList<string>? requestIds)
    {
        var sample = SampleOf(requestIds);
        facts.Add("requestIds.sample.count", sample.Count, sample.Count == 0
            ? "none recorded in the comparison period"
            : Inv(sample.Count) + (sample.Count == 1 ? " request ID" : " request IDs"));
        for (int i = 0; i < sample.Count; i++) facts.Add("requestIds.sample." + Inv(i + 1), sample[i], sample[i]);
    }

    // ---------------------------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------------------------

    private static void AddNumber(BenchmarkReportFacts.FactList facts, string key, double value, Func<double, string> format)
    {
        if (double.IsFinite(value)) facts.Add(key, value, format(value));
        else facts.Unavailable(key, "Not computable.");
    }

    private static void AddP(BenchmarkReportFacts.FactList facts, string key, double? p, string reason)
    {
        if (p is double value && double.IsFinite(value)) facts.Add(key, value, BenchmarkComparisonReportFacts.PValue(value));
        else facts.Unavailable(key, reason);
    }

    /// <summary>
    /// An analysis id as a key part: its letters and digits, each removed character capitalizing the
    /// next (<c>toolMix.wiki_search</c> becomes <c>toolMixWikiSearch</c>); <c>item</c> when nothing is left.
    /// </summary>
    public static string KeyPart(string? id)
    {
        var sb = new StringBuilder();
        bool capitalize = false;
        foreach (char c in id ?? string.Empty)
        {
            if (!char.IsAsciiLetterOrDigit(c))
            {
                capitalize = sb.Length > 0;
                continue;
            }
            sb.Append(capitalize ? char.ToUpperInvariant(c) : c);
            capitalize = false;
        }
        return sb.Length > 0 ? sb.ToString() : "item";
    }

    /// <summary><paramref name="part"/>, or with the lowest number from 2 that makes it unused.</summary>
    private static string Unique(string part, HashSet<string> used)
    {
        string candidate = part;
        for (int n = 2; !used.Add(candidate); n++) candidate = part + Inv(n);
        return candidate;
    }

    private static string GradeText(ChatConsistencyEvidenceGrade grade) => grade switch
    {
        ChatConsistencyEvidenceGrade.Established => "Established",
        ChatConsistencyEvidenceGrade.Indicated => "Indicated",
        _ => "Not established"
    };

    /// <summary>100 · (e^x − 1).</summary>
    private static double PercentOf(double logRatio) => 100.0 * (Math.Exp(logRatio) - 1.0);

    private static string SignedPercent(double percent) => WithUnit(SignedOne(percent), "%");

    private static string Share(double share) => WithUnit(BenchmarkReportFormat.OneDecimal(share * 100.0), "%");

    /// <summary><see cref="BenchmarkReportFormat.SignedOneDecimal"/> with its minus as U+2212: "+16.8", "−2.0", "0.0".</summary>
    private static string SignedOne(double value) => WithMinus(BenchmarkReportFormat.SignedOneDecimal(value));

    private static string SignedTwo(double value) => Signed(value, 2, "0.00");

    private static string Signed(double value, int decimals, string format)
    {
        double rounded = Math.Round(value, decimals, MidpointRounding.AwayFromZero);
        if (rounded == 0) rounded = 0;
        string text = WithMinus(rounded.ToString(format, CultureInfo.InvariantCulture));
        return rounded > 0 ? "+" + text : text;
    }

    /// <summary>A plain figure: a count with separators from 100, one decimal from 1, three below.</summary>
    private static string Number(double value)
    {
        double abs = Math.Abs(value);
        if (abs >= 100) return WithMinus(BenchmarkReportFormat.Count(value));
        if (abs >= 1) return WithMinus(BenchmarkReportFormat.OneDecimal(value));
        double rounded = Math.Round(value, 3, MidpointRounding.AwayFromZero);
        if (rounded == 0) rounded = 0;
        return WithMinus(rounded.ToString("0.000", CultureInfo.InvariantCulture));
    }

    /// <summary>A formatted number with a leading hyphen-minus printed as U+2212.</summary>
    private static string WithMinus(string number)
        => number.StartsWith('-') ? Minus + number[1..] : number;

    /// <summary>The number and its unit joined by a no-break space; the number alone without a unit.</summary>
    private static string WithUnit(string number, string? unit)
        => string.IsNullOrWhiteSpace(unit) ? number : number + UnitSpace + unit.Trim();

    private static string Seconds(double milliseconds) => WithUnit(BenchmarkReportFormat.OneDecimal(milliseconds / 1000.0), "s");

    private static string Usd(decimal value)
        => "$" + Math.Round(value, 4, MidpointRounding.AwayFromZero).ToString("0.00##", CultureInfo.InvariantCulture);

    /// <summary><c>1 battery run</c>, <c>2 battery runs</c>.</summary>
    private static string Plural(int count, string noun) => Inv(count) + " " + (count == 1 ? noun : noun + "s");

    /// <summary><c>run #10</c>, <c>runs #10 and #11</c>, <c>runs #10, #11 and #12</c>; <c>no run</c> for none.</summary>
    private static string RunList(IReadOnlyCollection<long> runIds)
    {
        var ids = runIds.Distinct().OrderBy(id => id).Select(id => "#" + id.ToString(CultureInfo.InvariantCulture)).ToList();
        return ids.Count switch
        {
            0 => "no run",
            1 => "run " + ids[0],
            _ => "runs " + BenchmarkReportFormat.LetterList(ids)
        };
    }

    private static string Runs(int count) => Inv(count) + (count == 1 ? " run" : " runs");

    private static string Items(int count) => Inv(count) + (count == 1 ? " item" : " items");

    private static string RunText(long runId) => "run #" + runId.ToString(CultureInfo.InvariantCulture);

    private static string When(DateTime utc) => utc.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + " UTC";

    private static string Iso(DateTime utc) => utc.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private static string LowerFirst(string text)
        => text.Length > 1 && char.IsUpper(text[0]) && !char.IsUpper(text[1]) ? char.ToLowerInvariant(text[0]) + text[1..] : text;

    private static string UpperFirst(string text)
        => text.Length > 0 ? char.ToUpperInvariant(text[0]) + text[1..] : text;

    private static string Inv(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Inv(long value) => value.ToString(CultureInfo.InvariantCulture);
}
