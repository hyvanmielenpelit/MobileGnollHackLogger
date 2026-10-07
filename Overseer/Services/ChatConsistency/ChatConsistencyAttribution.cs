namespace Overseer.Services.ChatConsistency;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

/// <summary>One primary endpoint as the attribution table reads it.</summary>
public sealed record ChatConsistencyAttributionEndpoint
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public bool Computed { get; init; }
    public ConsistencyVerdict? Verdict { get; init; }
    public string VerdictLabel { get; init; } = string.Empty;
    public ChatConsistencyEvidenceGrade Grade { get; init; } = ChatConsistencyEvidenceGrade.NotEstablished;
    public double? Estimate { get; init; }
}

/// <summary>Everything the attribution decision table reads. Pure data, so every row can be tested alone.</summary>
public sealed record ChatConsistencyAttributionInput
{
    public IReadOnlyList<ChatConsistencyAttributionEndpoint> Endpoints { get; init; } = Array.Empty<ChatConsistencyAttributionEndpoint>();

    /// <summary>Overseer events inside the compared span.</summary>
    public IReadOnlyList<ChatConsistencyEventView> Events { get; init; } = Array.Empty<ChatConsistencyEventView>();
    public IReadOnlyList<ChatConsistencyControlEffect> ControlEffects { get; init; } = Array.Empty<ChatConsistencyControlEffect>();
    public IReadOnlyList<ChatConsistencyMissingControlView> MissingControls { get; init; } = Array.Empty<ChatConsistencyMissingControlView>();
    public ChatConsistencyServedModels ServedModels { get; init; } = new();
    public string SubjectProvider { get; init; } = string.Empty;

    /// <summary>Every decile of the P3 shift function excludes zero on the side of the P3 estimate; null when not computed.</summary>
    public bool? StreamingChangedAtEveryDecile { get; init; }

    /// <summary>Every common stratum with enough runs shows the P2 (P3) shift's sign; null when fewer than two such strata exist.</summary>
    public bool? TimeToFirstAnswerTextStrataSameSign { get; init; }
    public bool? StreamingRateStrataSameSign { get; init; }

    /// <summary>The P2 (P3) effect differs between the sampled strata: signs disagree, or the spread of stratum shifts exceeds the margin.</summary>
    public bool TimeToFirstAnswerTextStrataDiffer { get; init; }
    public bool StreamingRateStrataDiffer { get; init; }

    /// <summary>The run-to-run standard deviation of the P2 (P3) run medians exceeds the margin.</summary>
    public bool TimeToFirstAnswerTextHighRunVariance { get; init; }
    public bool StreamingRateHighRunVariance { get; init; }

    /// <summary>The 429 or 5xx rate of candidate calls rose (rejected within the reliability family).</summary>
    public bool RateLimitOrServerErrorIncrease { get; init; }
    public bool UsBusinessHoursCovered { get; init; }
    public bool OutsideBusinessHoursCovered { get; init; }

    /// <summary>The verdict on P2 measured gross, with Overseer's own waits left in; null when not computed.</summary>
    public ConsistencyVerdict? GrossTimeToFirstAnswerTextVerdict { get; init; }
    public bool OwnWaitShareMovedMaterially { get; init; }

    /// <summary>Restricted to answers without retries, the P2 change no longer holds.</summary>
    public bool RetriesAccountForSpeedChange { get; init; }
    public bool ReasoningTokensChanged { get; init; }
    public bool ModelCallsChanged { get; init; }
    public bool ToolMixChanged { get; init; }

    /// <summary>Annotations of kind ProviderConfirmedCause inside the compared span, the only citable mechanism source.</summary>
    public IReadOnlyList<ChatConsistencyAnnotationView> ProviderConfirmedCauses { get; init; } = Array.Empty<ChatConsistencyAnnotationView>();
}

/// <summary>
/// The pre-declared attribution decision table. It reports every endpoint's total change first, then
/// the rows that fire. It names the side a change fits — ours, the provider's, our infrastructure — and
/// the events and controls behind it; it never names a mechanism or an intent. Only a
/// ProviderConfirmedCause annotation is cited, verbatim, as a provider's own statement.
///
/// <list type="table">
/// <item><term>R1 overseer-change</term><description>An endpoint changed, an Overseer event lies in the span,
/// and a matched control moved the same way with a DiD interval including 0.</description></item>
/// <item><term>R2 not-attributable</term><description>An endpoint changed, an Overseer event lies in the span,
/// and no control qualifies: the candidate causes and the missing-control note are listed.</description></item>
/// <item><term>R3 declared-snapshot-change</term><description>The served model ids differ between the periods.</description></item>
/// <item><term>R4 served-configuration</term><description>Calls were served at another tier than requested, or by a fallback model.</description></item>
/// <item><term>R5 model-behavior</term><description>P4, reasoning tokens, model calls or the tool mix changed, P3 is
/// Equivalent, and no Overseer event lies in the span or a DiD isolates the target.</description></item>
/// <item><term>R6 load-related</term><description>P2 or P3 changed and the effect differs between strata, or
/// same-provider controls moved with it, or the run-to-run variance is high, or 429/5xx rates rose.</description></item>
/// <item><term>R7 persistent-serving</term><description>P3 changed at every decile and in every common stratum with
/// one sign; provider-wide when a same-provider control moved too, else model-specific; load-independent only
/// when the strata cover US business hours and outside them.</description></item>
/// <item><term>R8 infrastructure</term><description>Overseer's own waits or retries account for the speed change.</description></item>
/// <item><term>R9 undeclared-change</term><description>P1 degraded or behavior changed for the target alone (a DiD
/// separates it), the served model id did not change, and neither our infrastructure nor an Overseer change explains it.</description></item>
/// <item><term>R10 improvement</term><description>P1 improved, Established: with a declared snapshot change, or undeclared.</description></item>
/// <item><term>R11 undetermined</term><description>Every endpoint is Inconclusive, a changed endpoint fits no row, or rows
/// of several sides fit one endpoint.</description></item>
/// </list>
///
/// <para>A provider-side row (R5–R7, R9, R10) needs its endpoint isolated: no Overseer event in the span, or a
/// control DiD separating the target.</para>
/// </summary>
public static class ChatConsistencyAttribution
{
    public const string SideOurs = "ours";
    public const string SideProvider = "provider";
    public const string SideInfrastructure = "infrastructure";
    public const string SideUndetermined = "undetermined";

    public const string RuleOverseerChange = "R1 overseer-change";
    public const string RuleNotAttributable = "R2 not-attributable";
    public const string RuleDeclaredSnapshotChange = "R3 declared-snapshot-change";
    public const string RuleServedConfiguration = "R4 served-configuration";
    public const string RuleModelBehavior = "R5 model-behavior";
    public const string RuleLoadRelated = "R6 load-related";
    public const string RulePersistentServing = "R7 persistent-serving";
    public const string RuleInfrastructure = "R8 infrastructure";
    public const string RuleUndeclaredChange = "R9 undeclared-change";
    public const string RuleImprovement = "R10 improvement";
    public const string RuleUndetermined = "R11 undetermined";

    /// <summary>Runs the decision table.</summary>
    public static ChatConsistencyAttributionOutcome Attribute(ChatConsistencyAttributionInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var endpoints = input.Endpoints.ToDictionary(e => e.Id, StringComparer.Ordinal);
        ChatConsistencyAttributionEndpoint? Get(string id) => endpoints.TryGetValue(id, out var e) ? e : null;

        var totals = input.Endpoints
            .Select(e => new ChatConsistencyTotalChange
            {
                EndpointId = e.Id,
                Name = e.Name,
                VerdictLabel = e.Computed ? e.VerdictLabel : "not computable",
                Grade = e.Computed ? e.Grade : ChatConsistencyEvidenceGrade.NotEstablished
            })
            .ToList();

        var rows = new List<ChatConsistencyAttributionResult>();
        bool hasEvents = input.Events.Count > 0;
        var eventRefs = input.Events.Select(e => e.Label).Distinct(StringComparer.Ordinal).ToList();
        string providerCitation = ProviderCitation(input.ProviderConfirmedCauses);

        var changed = input.Endpoints.Where(IsChanged).ToList();
        var p1 = Get(ChatConsistencyEndpointIds.Quality);
        var p2 = Get(ChatConsistencyEndpointIds.TimeToFirstAnswerText);
        var p3 = Get(ChatConsistencyEndpointIds.StreamingRate);
        var p4 = Get(ChatConsistencyEndpointIds.Work);

        IReadOnlyList<ChatConsistencyControlEffect> Effects(string id)
            => input.ControlEffects.Where(c => string.Equals(c.EndpointId, id, StringComparison.Ordinal)).ToList();

        bool OverseerExplained(string id)
            => hasEvents && Effects(id).Any(c => c.DidIncludesZero && c.ControlMovedSameWay);

        bool Isolated(string id) => !hasEvents || Effects(id).Any(c => c.DidSeparatesTarget);

        // --- R1 and R2: changes with an Overseer event in the span ---
        foreach (var e in changed)
        {
            if (!hasEvents) continue;

            var effects = Effects(e.Id);
            var sameWay = effects.Where(c => c.DidIncludesZero && c.ControlMovedSameWay).ToList();
            if (sameWay.Count > 0)
            {
                rows.Add(new ChatConsistencyAttributionResult
                {
                    Label = "Overseer change",
                    Side = SideOurs,
                    Grade = e.Grade,
                    Rule = RuleOverseerChange,
                    Endpoints = new[] { e.Id },
                    EventRefs = eventRefs,
                    Evidence = e.Name + " " + e.VerdictLabel + " with " + JoinEvents(eventRefs) + " in the compared span; control "
                        + string.Join(", ", sameWay.Select(c => c.ControlDisplay).Distinct(StringComparer.Ordinal))
                        + " moved the same way and the difference-in-differences interval includes 0."
                });
            }
            else if (effects.Count == 0)
            {
                string missing = input.MissingControls.Count > 0
                    ? " " + string.Join(" ", input.MissingControls.Select(m => m.SuggestedText))
                    : " No control run of another subject under the same Overseer build was found.";
                rows.Add(new ChatConsistencyAttributionResult
                {
                    Label = "Total change, not attributable",
                    Side = SideUndetermined,
                    Grade = ChatConsistencyEvidenceGrade.NotEstablished,
                    Rule = RuleNotAttributable,
                    Endpoints = new[] { e.Id },
                    EventRefs = eventRefs,
                    Evidence = e.Name + " " + e.VerdictLabel + ". Candidate causes: " + JoinEvents(eventRefs)
                        + "; the provider (" + ProviderName(input.SubjectProvider) + ")." + missing
                });
            }
        }

        // --- R3: declared snapshot change ---
        var served = input.ServedModels;
        if (served.Changed)
        {
            rows.Add(new ChatConsistencyAttributionResult
            {
                Label = "Declared snapshot change",
                Side = SideProvider,
                Grade = ChatConsistencyEvidenceGrade.Established,
                Rule = RuleDeclaredSnapshotChange,
                Endpoints = changed.Select(e => e.Id).ToList(),
                Evidence = "The provider reported serving " + ServedList(served.Baseline) + " in the baseline and "
                    + ServedList(served.Comparison) + " in the comparison." + providerCitation
            });
        }

        // --- R4: served configuration differs from the request ---
        if (served.ServedConfigurationDiffers)
        {
            rows.Add(new ChatConsistencyAttributionResult
            {
                Label = "Served configuration differs from requested",
                Side = SideProvider,
                Grade = ChatConsistencyEvidenceGrade.Established,
                Rule = RuleServedConfiguration,
                Endpoints = changed.Select(e => e.Id).ToList(),
                Evidence = "Calls served at another tier than requested: baseline " + Inv(served.BaselineTierMismatchCalls) + " of "
                    + Inv(served.BaselineCalls) + ", comparison " + Inv(served.ComparisonTierMismatchCalls) + " of " + Inv(served.ComparisonCalls)
                    + ". Calls served by a fallback model: baseline " + Inv(served.BaselineFallbackCalls) + ", comparison "
                    + Inv(served.ComparisonFallbackCalls) + "." + SpeedsText(served)
            });
        }

        // --- R8: our infrastructure ---
        bool p2Changed = p2 != null && IsChanged(p2);
        bool grossChanged = input.GrossTimeToFirstAnswerTextVerdict is ConsistencyVerdict.ChangedDegraded or ConsistencyVerdict.ChangedImproved;
        bool netUnchanged = p2 != null && p2.Computed && p2.Verdict is ConsistencyVerdict.Equivalent or ConsistencyVerdict.Inconclusive;
        var infrastructureReasons = new List<string>();
        if (grossChanged && netUnchanged)
        {
            infrastructureReasons.Add("time to first answer text changed with Overseer's own waits left in, and not net of them");
        }

        if (input.OwnWaitShareMovedMaterially && (p2Changed || grossChanged))
        {
            infrastructureReasons.Add("the share of model time spent in Overseer's own permit and retry waits moved materially");
        }

        if (input.RetriesAccountForSpeedChange && p2Changed)
        {
            infrastructureReasons.Add("restricted to answers without retries, the speed change does not hold");
        }

        bool infrastructure = infrastructureReasons.Count > 0;
        if (infrastructure)
        {
            rows.Add(new ChatConsistencyAttributionResult
            {
                Label = "Our infrastructure",
                Side = SideInfrastructure,
                Grade = ChatConsistencyEvidenceGrade.Indicated,
                Rule = RuleInfrastructure,
                Endpoints = new[] { ChatConsistencyEndpointIds.TimeToFirstAnswerText },
                Evidence = Sentence(infrastructureReasons)
            });
        }

        // --- R5: model behavior change ---
        bool p4Changed = p4 != null && IsChanged(p4);
        var behaviorSignals = new List<string>();
        if (p4Changed) behaviorSignals.Add("work per turn " + p4!.VerdictLabel);
        if (input.ReasoningTokensChanged) behaviorSignals.Add("reasoning tokens changed");
        if (input.ModelCallsChanged) behaviorSignals.Add("model calls per answer changed");
        if (input.ToolMixChanged) behaviorSignals.Add("the tool mix changed");
        bool behaviorChanged = behaviorSignals.Count > 0;
        bool behaviorIsolated = !hasEvents || (p4Changed && Isolated(ChatConsistencyEndpointIds.Work));
        bool p3Equivalent = p3 != null && p3.Computed && p3.Verdict == ConsistencyVerdict.Equivalent;

        if (behaviorChanged && p3Equivalent && behaviorIsolated)
        {
            var grade = p4Changed ? Weaker(p4!.Grade, p3!.Grade) : Weaker(ChatConsistencyEvidenceGrade.Indicated, p3!.Grade);
            var ids = new List<string>();
            if (p4Changed) ids.Add(ChatConsistencyEndpointIds.Work);
            ids.Add(ChatConsistencyEndpointIds.StreamingRate);
            rows.Add(new ChatConsistencyAttributionResult
            {
                Label = "Model behavior change",
                Side = SideProvider,
                Grade = grade,
                Rule = RuleModelBehavior,
                Endpoints = ids,
                Evidence = Sentence(behaviorSignals) + " The answer streaming rate is equivalent"
                    + (hasEvents ? ", and a control difference-in-differences isolates the target." : ", and no Overseer event lies in the compared span.")
                    + providerCitation
            });
        }

        // --- R7: persistent serving change (P3) ---
        bool p3Changed = p3 != null && IsChanged(p3);
        bool persistent = p3Changed
            && input.StreamingChangedAtEveryDecile == true
            && input.StreamingRateStrataSameSign != false
            && Isolated(ChatConsistencyEndpointIds.StreamingRate);
        if (persistent)
        {
            bool providerWide = Effects(ChatConsistencyEndpointIds.StreamingRate).Any(c => c.SameProvider && c.ControlMovedSameWay);
            bool loadIndependent = input.UsBusinessHoursCovered && input.OutsideBusinessHoursCovered;
            rows.Add(new ChatConsistencyAttributionResult
            {
                Label = "Persistent serving change within the sampled hours ("
                    + (providerWide ? "provider-wide" : "model-specific") + "; "
                    + (loadIndependent ? "load-independent" : "time-of-day dependence not assessable") + ")",
                Side = SideProvider,
                Grade = p3!.Grade,
                Rule = RulePersistentServing,
                Endpoints = new[] { ChatConsistencyEndpointIds.StreamingRate },
                Evidence = "The answer streaming rate " + p3!.VerdictLabel + " at every decile"
                    + (input.StreamingRateStrataSameSign == true ? " and in every common time stratum, with one sign." : " in the one common time stratum sampled.")
                    + (providerWide ? " A control of the same provider moved the same way." : " No control of the same provider moved the same way.")
                    + (loadIndependent ? string.Empty : " The sampled strata do not cover both US business hours and hours outside them.")
                    + providerCitation
            });
        }

        // --- R6: load- or capacity-related (P2, and P3 unless R7 holds it) ---
        var loadEndpoints = new List<string>();
        var loadReasons = new List<string>();
        void ConsiderLoad(ChatConsistencyAttributionEndpoint? e, bool strataDiffer, bool highVariance)
        {
            if (e == null || !IsChanged(e) || !Isolated(e.Id)) return;

            var reasons = new List<string>();
            if (strataDiffer) reasons.Add(e.Name + ": the effect differs between the sampled time strata");
            if (!hasEvents && Effects(e.Id).Any(c => c.SameProvider && c.ControlMovedSameWay && c.DidIncludesZero))
            {
                reasons.Add(e.Name + ": a control of the same provider moved with it");
            }

            if (highVariance) reasons.Add(e.Name + ": the run-to-run variance is high");
            if (input.RateLimitOrServerErrorIncrease) reasons.Add("the rate of 429 or 5xx responses rose");
            if (reasons.Count == 0) return;

            loadEndpoints.Add(e.Id);
            loadReasons.AddRange(reasons);
        }

        ConsiderLoad(p2, input.TimeToFirstAnswerTextStrataDiffer, input.TimeToFirstAnswerTextHighRunVariance);
        if (!persistent) ConsiderLoad(p3, input.StreamingRateStrataDiffer, input.StreamingRateHighRunVariance);
        if (loadEndpoints.Count > 0)
        {
            var grade = loadEndpoints
                .Select(id => Get(id)!.Grade)
                .Aggregate(ChatConsistencyEvidenceGrade.Indicated, Weaker);
            rows.Add(new ChatConsistencyAttributionResult
            {
                Label = "Load- or capacity-related (within the sampled hours)",
                Side = SideProvider,
                Grade = grade,
                Rule = RuleLoadRelated,
                Endpoints = loadEndpoints,
                Evidence = Sentence(loadReasons.Distinct(StringComparer.Ordinal)) + providerCitation
            });
        }

        // --- R9: undeclared model change (P1 degraded, or behavior, for the target alone) ---
        bool servedRecordedUnchanged = served.Baseline.Count > 0 && served.Comparison.Count > 0 && !served.Changed;
        var undeclared = new List<string>();
        if (p1 != null && p1.Verdict == ConsistencyVerdict.ChangedDegraded
            && Effects(p1.Id).Any(c => c.DidSeparatesTarget) && !OverseerExplained(p1.Id))
        {
            undeclared.Add(p1.Id);
        }

        if (p4Changed && Effects(p4!.Id).Any(c => c.DidSeparatesTarget) && !OverseerExplained(p4!.Id))
        {
            undeclared.Add(p4!.Id);
        }

        if (undeclared.Count > 0 && servedRecordedUnchanged && !infrastructure)
        {
            var grade = undeclared.Select(id => Get(id)!.Grade).Aggregate(ChatConsistencyEvidenceGrade.Established, Weaker);
            rows.Add(new ChatConsistencyAttributionResult
            {
                Label = "Undeclared model change",
                Side = SideProvider,
                Grade = grade,
                Rule = RuleUndeclaredChange,
                Endpoints = undeclared,
                Evidence = "Flagged for review: " + string.Join(" and ", undeclared.Select(id => Get(id)!.Name.ToLowerInvariant() + " " + Get(id)!.VerdictLabel))
                    + " for this model alone (a control difference-in-differences separates it), while the provider reported serving the same model id ("
                    + ServedList(served.Comparison) + ") and neither Overseer's infrastructure nor an Overseer change accounts for it."
                    + providerCitation
            });
        }

        // --- R10: improvement ---
        if (p1 != null && p1.Verdict == ConsistencyVerdict.ChangedImproved && p1.Grade == ChatConsistencyEvidenceGrade.Established
            && Isolated(p1.Id))
        {
            rows.Add(new ChatConsistencyAttributionResult
            {
                Label = served.Changed ? "Improvement with a new snapshot" : "Undeclared improvement",
                Side = SideProvider,
                Grade = ChatConsistencyEvidenceGrade.Established,
                Rule = RuleImprovement,
                Endpoints = new[] { p1.Id },
                Evidence = "Quality improved"
                    + (served.Changed
                        ? " together with a change of the served model id (" + ServedList(served.Baseline) + " → " + ServedList(served.Comparison) + ")."
                        : servedRecordedUnchanged
                            ? " while the provider reported serving the same model id (" + ServedList(served.Comparison) + ")."
                            : "; no served model id change was recorded.")
                    + providerCitation
            });
        }

        // --- R11: undetermined or multiple causes ---
        var computed = input.Endpoints.Where(e => e.Computed).ToList();
        if (computed.Count > 0 && computed.All(e => e.Verdict == ConsistencyVerdict.Inconclusive))
        {
            rows.Add(new ChatConsistencyAttributionResult
            {
                Label = "Undetermined",
                Side = SideUndetermined,
                Grade = ChatConsistencyEvidenceGrade.NotEstablished,
                Rule = RuleUndetermined,
                Endpoints = computed.Select(e => e.Id).ToList(),
                Evidence = "Every computed endpoint is inconclusive: the data cannot tell a change from no change."
            });
        }
        else
        {
            var causeRules = new HashSet<string>(StringComparer.Ordinal)
            {
                RuleOverseerChange, RuleModelBehavior, RuleLoadRelated, RulePersistentServing,
                RuleInfrastructure, RuleUndeclaredChange, RuleImprovement, RuleDeclaredSnapshotChange
            };

            var unexplained = new List<string>();
            var multiple = new List<string>();
            foreach (var e in changed)
            {
                if (rows.Any(r => r.Rule == RuleNotAttributable && r.Endpoints.Contains(e.Id))) continue;

                var sides = rows
                    .Where(r => causeRules.Contains(r.Rule) && r.Endpoints.Contains(e.Id))
                    .Select(r => r.Side)
                    .Distinct(StringComparer.Ordinal)
                    .ToList();
                if (sides.Count == 0) unexplained.Add(e.Id);
                else if (sides.Count > 1) multiple.Add(e.Id);
            }

            if (unexplained.Count > 0)
            {
                rows.Add(new ChatConsistencyAttributionResult
                {
                    Label = "Undetermined",
                    Side = SideUndetermined,
                    Grade = ChatConsistencyEvidenceGrade.NotEstablished,
                    Rule = RuleUndetermined,
                    Endpoints = unexplained,
                    EventRefs = eventRefs,
                    Evidence = "No row of the decision table fits the change of "
                        + string.Join(", ", unexplained.Select(id => Get(id)!.Name.ToLowerInvariant())) + "."
                });
            }

            if (multiple.Count > 0)
            {
                rows.Add(new ChatConsistencyAttributionResult
                {
                    Label = "Multiple causes",
                    Side = SideUndetermined,
                    Grade = ChatConsistencyEvidenceGrade.NotEstablished,
                    Rule = RuleUndetermined,
                    Endpoints = multiple,
                    EventRefs = eventRefs,
                    Evidence = "Rows of more than one side fit the change of "
                        + string.Join(", ", multiple.Select(id => Get(id)!.Name.ToLowerInvariant())) + "; the data do not single one out."
                });
            }
        }

        return new ChatConsistencyAttributionOutcome { TotalChanges = totals, Attributions = rows };
    }

    /// <summary>A decisive change beyond the margin.</summary>
    public static bool IsChanged(ChatConsistencyAttributionEndpoint endpoint)
        => endpoint.Computed && endpoint.Verdict is ConsistencyVerdict.ChangedDegraded or ConsistencyVerdict.ChangedImproved;

    /// <summary>The weaker of two grades.</summary>
    public static ChatConsistencyEvidenceGrade Weaker(ChatConsistencyEvidenceGrade a, ChatConsistencyEvidenceGrade b)
        => (ChatConsistencyEvidenceGrade)Math.Max((int)a, (int)b);

    private static string ProviderCitation(IReadOnlyList<ChatConsistencyAnnotationView> causes)
    {
        if (causes.Count == 0) return string.Empty;
        return " Provider-confirmed cause annotated: "
            + string.Join("; ", causes.Select(c => c.AtUtc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + " \"" + c.Text + "\""
                + (string.IsNullOrWhiteSpace(c.SourceUrl) ? string.Empty : " (" + c.SourceUrl + ")")))
            + ".";
    }

    private static string JoinEvents(IReadOnlyList<string> refs)
        => refs.Count == 0 ? "no Overseer event" : string.Join(", ", refs);

    private static string ProviderName(string provider)
        => string.IsNullOrWhiteSpace(provider) ? "the model's provider" : provider;

    private static string ServedList(IReadOnlyList<ChatConsistencyServedModelCount> served)
        => served.Count == 0 ? "no recorded id" : string.Join(", ", served.Select(s => s.ModelId));

    private static string SpeedsText(ChatConsistencyServedModels served)
    {
        if (served.BaselineServedSpeeds.Count == 0 && served.ComparisonServedSpeeds.Count == 0) return string.Empty;
        return " Served speeds: baseline " + (served.BaselineServedSpeeds.Count == 0 ? "none recorded" : string.Join(", ", served.BaselineServedSpeeds))
            + ", comparison " + (served.ComparisonServedSpeeds.Count == 0 ? "none recorded" : string.Join(", ", served.ComparisonServedSpeeds)) + ".";
    }

    private static string Sentence(IEnumerable<string> parts)
    {
        string joined = string.Join("; ", parts);
        if (joined.Length == 0) return joined;
        return char.ToUpperInvariant(joined[0]) + joined.Substring(1) + ".";
    }

    private static string Inv(int value) => value.ToString(CultureInfo.InvariantCulture);
}
