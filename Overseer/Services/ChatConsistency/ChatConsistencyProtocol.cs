namespace Overseer.Services.ChatConsistency;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;

/// <summary>The scale an endpoint's effect is estimated on.</summary>
public enum ChatConsistencyEffectScale
{
    /// <summary>Comparison minus baseline, in the endpoint's own units.</summary>
    Difference = 0,

    /// <summary>ln(comparison / baseline); reported also as a percentage, 100 · (e^x − 1).</summary>
    LogRatio = 1
}

/// <summary>The ids of the five primary endpoints, in protocol order.</summary>
public static class ChatConsistencyEndpointIds
{
    public const string Quality = "P1";
    public const string TimeToFirstAnswerText = "P2";
    public const string StreamingRate = "P3";
    public const string Work = "P4";
    public const string Cost = "P5";

    public static readonly IReadOnlyList<string> All = new[] { Quality, TimeToFirstAnswerText, StreamingRate, Work, Cost };
}

/// <summary>One pre-declared primary endpoint.</summary>
public sealed record ChatConsistencyEndpointProtocol
{
    public string Id { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    /// <summary>The unit of the effect: "index points", or "log ratio" for a ratio endpoint.</summary>
    public string Unit { get; init; } = string.Empty;

    public ChatConsistencyEffectScale Scale { get; init; }

    /// <summary>The equivalence margin on the effect scale: index points, or ln(1 + fraction).</summary>
    public double Margin { get; init; }

    /// <summary>The verdict mapping's direction. P4 maps "more work" to the lower-is-better side without judging it.</summary>
    public bool HigherIsBetter { get; init; }

    /// <summary>The endpoint reports "more work" / "less work" rather than "degraded" / "improved".</summary>
    public bool WorkDirection { get; init; }

    /// <summary>The measurement axis whose segments the endpoint's runs must share.</summary>
    public ChatConsistencyAxis Axis { get; init; }

    /// <summary>The bootstrap resamples runs only and keeps each drawn run whole.</summary>
    public bool RunClustersOnly { get; init; }

    /// <summary>The effect is the equal-weight mean of within-stratum shifts over common strata.</summary>
    public bool Stratified { get; init; }

    /// <summary>How items are paired and what is resampled.</summary>
    public string Pairing { get; init; } = string.Empty;

    /// <summary>The margin as text, for example "±3 index points" or "±15 %".</summary>
    public string MarginText => Scale == ChatConsistencyEffectScale.LogRatio
        ? "±" + (100.0 * (Math.Exp(Margin) - 1.0)).ToString("0.#", CultureInfo.InvariantCulture) + " %"
        : "±" + Margin.ToString("0.###", CultureInfo.InvariantCulture) + " " + Unit;
}

/// <summary>One recorded deviation from the published protocol.</summary>
public sealed record ChatConsistencyProtocolOverride(string Field, string From, string To);

/// <summary>
/// Overrides a request may apply to the protocol. Every applied override is recorded in
/// <see cref="ChatConsistencyProtocol.Overrides"/> and labels the result.
/// </summary>
public sealed record ChatConsistencyProtocolOverrides
{
    /// <summary>
    /// Margins by endpoint id: index points for a difference endpoint, a fraction (0.15 = ±15 %) for a
    /// log-ratio endpoint.
    /// </summary>
    public IReadOnlyDictionary<string, double>? Margins { get; init; }

    public double? Alpha { get; init; }

    public int? BootstrapReplicates { get; init; }

    public int? BootstrapSeed { get; init; }

    public int? MinimumPairedItems { get; init; }

    public int? MinimumRunsPerPeriod { get; init; }

    public int? MinimumDaysPerPeriod { get; init; }

    public int? MinimumSpeedRunsPerStratum { get; init; }
}

/// <summary>
/// The pre-declared method of a GnollBench chat consistency analysis. Immutable: any change to a
/// default is a new <see cref="ProtocolVersion"/>. A request may override fields; each override is
/// recorded in <see cref="Overrides"/> and shown in <see cref="Label"/>.
///
/// <para>V1: five primary endpoints (P1 quality, P2 time to first answer text, P3 answer streaming
/// rate, P4 work per turn, P5 cost per question) at α = 0.05 with Holm's adjustment across them;
/// Benjamini–Hochberg within each secondary family; Lakens' verdicts against each margin; the
/// minimum samples, evidence grades and robustness checks of the analysis service.</para>
/// </summary>
public sealed record ChatConsistencyProtocol
{
    public const string V1Version = "V1";

    /// <summary>The weekday strata overlapping US business hours, weekdays 14–22 UTC: 12–16, 16–20 and 20–24 UTC.</summary>
    private static readonly IReadOnlyList<int> DefaultUsBusinessHourStrata = new[] { 3, 4, 5 };

    private static readonly IReadOnlyList<ChatConsistencyEndpointProtocol> DefaultEndpoints = new[]
    {
        new ChatConsistencyEndpointProtocol
        {
            Id = ChatConsistencyEndpointIds.Quality,
            Name = "Quality",
            Unit = "index points",
            Scale = ChatConsistencyEffectScale.Difference,
            Margin = 3.0,
            HigherIsBetter = true,
            Axis = ChatConsistencyAxis.Quality,
            Pairing = "Item-paired quality score under a common grader (native grades when none covers every run); "
                + "mean of per-item differences; two-level bootstrap over runs, then items."
        },
        new ChatConsistencyEndpointProtocol
        {
            Id = ChatConsistencyEndpointIds.TimeToFirstAnswerText,
            Name = "Time to first answer text",
            Unit = "log ratio",
            Scale = ChatConsistencyEffectScale.LogRatio,
            Margin = Math.Log(1.15),
            HigherIsBetter = false,
            Axis = ChatConsistencyAxis.SpeedTelemetry,
            RunClustersOnly = true,
            Stratified = true,
            Pairing = "Net of Overseer's own waits; item-paired by centering each answer's log time on its item's mean; "
                + "Hodges–Lehmann shift within each common time stratum, equal-weight mean over strata; bootstrap over runs only."
        },
        new ChatConsistencyEndpointProtocol
        {
            Id = ChatConsistencyEndpointIds.StreamingRate,
            Name = "Answer streaming rate",
            Unit = "log ratio",
            Scale = ChatConsistencyEffectScale.LogRatio,
            Margin = Math.Log(1.10),
            HigherIsBetter = true,
            Axis = ChatConsistencyAxis.SpeedTelemetry,
            RunClustersOnly = true,
            Stratified = true,
            Pairing = "Final candidate call's visible decode rate; item-paired by centering on the item's mean log rate; "
                + "Hodges–Lehmann shift within each common time stratum, equal-weight mean over strata; bootstrap over runs only."
        },
        new ChatConsistencyEndpointProtocol
        {
            Id = ChatConsistencyEndpointIds.Work,
            Name = "Work per turn",
            Unit = "log ratio",
            Scale = ChatConsistencyEffectScale.LogRatio,
            Margin = Math.Log(1.15),
            HigherIsBetter = false,
            WorkDirection = true,
            Axis = ChatConsistencyAxis.Work,
            Pairing = "Total candidate output tokens per item; Hodges–Lehmann of per-item log differences; "
                + "two-level bootstrap over runs, then items. Reported as more or less work, not as better or worse."
        },
        new ChatConsistencyEndpointProtocol
        {
            Id = ChatConsistencyEndpointIds.Cost,
            Name = "Cost per question",
            Unit = "log ratio",
            Scale = ChatConsistencyEffectScale.LogRatio,
            Margin = Math.Log(1.10),
            HigherIsBetter = false,
            Axis = ChatConsistencyAxis.Cost,
            Pairing = "Candidate cost per item at one price card for every compared run; Hodges–Lehmann of per-item "
                + "log differences; two-level bootstrap over runs, then items."
        }
    };

    /// <summary>The published protocol.</summary>
    public static ChatConsistencyProtocol V1 { get; } = new();

    public string ProtocolVersion { get; init; } = V1Version;

    public double Alpha { get; init; } = ChatConsistencyStatistics.DefaultAlpha;

    public double SecondaryFalseDiscoveryRate { get; init; } = 0.05;

    public double Power { get; init; } = ChatConsistencyStatistics.DefaultPower;

    public int BootstrapReplicates { get; init; } = ChatConsistencyStatistics.DefaultBootstrapReplicates;

    public int BootstrapSeed { get; init; } = ChatConsistencyStatistics.DefaultSeed;

    /// <summary>P1, P4, P5: at least this many runs per period ...</summary>
    public int MinimumRunsPerPeriod { get; init; } = 2;

    /// <summary>... on at least this many distinct UTC days per period ...</summary>
    public int MinimumDaysPerPeriod { get; init; } = 2;

    /// <summary>... and at least this many paired items.</summary>
    public int MinimumPairedItems { get; init; } = 20;

    /// <summary>P2, P3: at least this many runs per period in at least one common stratum.</summary>
    public int MinimumSpeedRunsPerStratum { get; init; } = 3;

    /// <summary>A common stratum enters the across-strata sign check with at least this many runs per period.</summary>
    public int MinimumRunsPerStratumForSignCheck { get; init; } = 2;

    /// <summary>An own-wait share moving by at least this much (a fraction of model time) between periods is material.</summary>
    public double OwnWaitShareMaterialChange { get; init; } = 0.05;

    /// <summary>An answer counts as correct for the flip rate at this quality score or above, without a critical error.</summary>
    public int FlipPassThreshold { get; init; } = 50;

    /// <summary>The anchor's grader drift passes the grader-stability check within ± this many index points.</summary>
    public double GraderDriftMargin { get; init; } = 3.0;

    /// <summary>The stratum indexes counted as US business hours; see <see cref="UsBusinessHoursDefinition"/>.</summary>
    public IReadOnlyList<int> UsBusinessHourStrata { get; init; } = DefaultUsBusinessHourStrata;

    public string UsBusinessHoursDefinition { get; init; } =
        "US business hours are weekdays 14–22 UTC. A common stratum counts as inside them when it is a weekday block "
        + "overlapping that window (12–16, 16–20 or 20–24 UTC) and as outside them otherwise (weekday 00–12 UTC and every "
        + "weekend block). A change is called load-independent only when the common strata include at least one block of each kind.";

    public IReadOnlyList<ChatConsistencyEndpointProtocol> Endpoints { get; init; } = DefaultEndpoints;

    /// <summary>The deviations from the published protocol, by field name. Empty for the published protocol.</summary>
    public IReadOnlyList<ChatConsistencyProtocolOverride> Overrides { get; init; } = Array.Empty<ChatConsistencyProtocolOverride>();

    /// <summary>True when any field deviates from the published protocol.</summary>
    public bool IsOverridden => Overrides.Count > 0;

    /// <summary><c>V1</c>, or <c>V1 with overrides: …</c>.</summary>
    public string Label => IsOverridden
        ? ProtocolVersion + " with overrides: " + string.Join("; ", Overrides.Select(o => o.Field + " " + o.From + " → " + o.To))
        : ProtocolVersion;

    /// <summary>The endpoint with <paramref name="id"/>.</summary>
    public ChatConsistencyEndpointProtocol Endpoint(string id)
        => Endpoints.FirstOrDefault(e => string.Equals(e.Id, id, StringComparison.Ordinal))
           ?? throw new ArgumentOutOfRangeException(nameof(id), id, "Unknown endpoint.");

    /// <summary>The protocol as camelCase JSON, the form stored in <c>ChatConsistencyAnalysis.ProtocolJson</c>.</summary>
    public string ToJson() => JsonSerializer.Serialize(this, ChatConsistencyJson.Options);

    /// <summary>Reads a protocol written by <see cref="ToJson"/>.</summary>
    public static ChatConsistencyProtocol FromJson(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        return JsonSerializer.Deserialize<ChatConsistencyProtocol>(json, ChatConsistencyJson.Options)
               ?? throw new JsonException("The protocol JSON is empty.");
    }

    /// <summary>
    /// This protocol with <paramref name="overrides"/> applied. A value equal to the current one is
    /// not an override. Invalid values are refused with <see cref="ChatConsistencyRequestException"/>.
    /// </summary>
    public ChatConsistencyProtocol WithOverrides(ChatConsistencyProtocolOverrides? overrides)
    {
        if (overrides == null) return this;

        var recorded = new List<ChatConsistencyProtocolOverride>(Overrides);
        var protocol = this;

        if (overrides.Margins != null)
        {
            var endpoints = protocol.Endpoints.ToList();
            foreach (var pair in overrides.Margins.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                int index = endpoints.FindIndex(e => string.Equals(e.Id, pair.Key, StringComparison.Ordinal));
                if (index < 0)
                {
                    throw new ChatConsistencyRequestException("Unknown endpoint '" + pair.Key + "' in the margin overrides.");
                }

                if (!(pair.Value > 0.0) || double.IsInfinity(pair.Value))
                {
                    throw new ChatConsistencyRequestException("The margin of " + pair.Key + " must be positive and finite.");
                }

                var endpoint = endpoints[index];
                double margin = endpoint.Scale == ChatConsistencyEffectScale.LogRatio ? Math.Log(1.0 + pair.Value) : pair.Value;
                if (Math.Abs(margin - endpoint.Margin) < 1e-12) continue;

                var changed = endpoint with { Margin = margin };
                recorded.Add(new ChatConsistencyProtocolOverride(endpoint.Id + " margin", endpoint.MarginText, changed.MarginText));
                endpoints[index] = changed;
            }

            protocol = protocol with { Endpoints = endpoints };
        }

        if (overrides.Alpha is double alpha && alpha != protocol.Alpha)
        {
            if (!(alpha > 0.0 && alpha < 0.5)) throw new ChatConsistencyRequestException("α must lie strictly between 0 and 0.5.");
            recorded.Add(new ChatConsistencyProtocolOverride("alpha", Inv(protocol.Alpha), Inv(alpha)));
            protocol = protocol with { Alpha = alpha };
        }

        if (overrides.BootstrapReplicates is int replicates && replicates != protocol.BootstrapReplicates)
        {
            if (replicates < 200 || replicates > 100_000) throw new ChatConsistencyRequestException("Bootstrap replicates must lie between 200 and 100,000.");
            recorded.Add(new ChatConsistencyProtocolOverride("bootstrapReplicates", Inv(protocol.BootstrapReplicates), Inv(replicates)));
            protocol = protocol with { BootstrapReplicates = replicates };
        }

        if (overrides.BootstrapSeed is int seed && seed != protocol.BootstrapSeed)
        {
            recorded.Add(new ChatConsistencyProtocolOverride("bootstrapSeed", Inv(protocol.BootstrapSeed), Inv(seed)));
            protocol = protocol with { BootstrapSeed = seed };
        }

        if (overrides.MinimumPairedItems is int items && items != protocol.MinimumPairedItems)
        {
            if (items < 1) throw new ChatConsistencyRequestException("The minimum paired item count must be at least 1.");
            recorded.Add(new ChatConsistencyProtocolOverride("minimumPairedItems", Inv(protocol.MinimumPairedItems), Inv(items)));
            protocol = protocol with { MinimumPairedItems = items };
        }

        if (overrides.MinimumRunsPerPeriod is int runs && runs != protocol.MinimumRunsPerPeriod)
        {
            if (runs < 1) throw new ChatConsistencyRequestException("The minimum run count per period must be at least 1.");
            recorded.Add(new ChatConsistencyProtocolOverride("minimumRunsPerPeriod", Inv(protocol.MinimumRunsPerPeriod), Inv(runs)));
            protocol = protocol with { MinimumRunsPerPeriod = runs };
        }

        if (overrides.MinimumDaysPerPeriod is int days && days != protocol.MinimumDaysPerPeriod)
        {
            if (days < 1) throw new ChatConsistencyRequestException("The minimum day count per period must be at least 1.");
            recorded.Add(new ChatConsistencyProtocolOverride("minimumDaysPerPeriod", Inv(protocol.MinimumDaysPerPeriod), Inv(days)));
            protocol = protocol with { MinimumDaysPerPeriod = days };
        }

        if (overrides.MinimumSpeedRunsPerStratum is int speedRuns && speedRuns != protocol.MinimumSpeedRunsPerStratum)
        {
            if (speedRuns < 1) throw new ChatConsistencyRequestException("The minimum speed run count per stratum must be at least 1.");
            recorded.Add(new ChatConsistencyProtocolOverride("minimumSpeedRunsPerStratum", Inv(protocol.MinimumSpeedRunsPerStratum), Inv(speedRuns)));
            protocol = protocol with { MinimumSpeedRunsPerStratum = speedRuns };
        }

        return protocol with { Overrides = recorded };
    }

    private static string Inv(double value) => value.ToString("R", CultureInfo.InvariantCulture);

    private static string Inv(int value) => value.ToString(CultureInfo.InvariantCulture);
}
