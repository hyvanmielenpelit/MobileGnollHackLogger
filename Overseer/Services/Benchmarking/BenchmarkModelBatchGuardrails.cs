namespace Overseer.Services.Benchmarking;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using MobileGnollHackLogger.Data;
using Overseer.Models;
using Overseer.Services;

/// <summary>The graders of a run or a batch, by configuration id; a role left empty is null.</summary>
public sealed record BenchmarkModelBatchRoster(long? AssessorId, long? CoAssessorId, long? ReaderId, long? VerifierId);

/// <summary>Everything <see cref="BenchmarkModelBatchGuardrails.Evaluate"/> reads. Pure data.</summary>
public sealed record BenchmarkModelBatchGuardContext
{
    public StartBenchmarkModelBatchRequest Request { get; init; } = new();

    /// <summary>The resolved models under test, in request order, each once.</summary>
    public IReadOnlyList<SystemAiApiConfiguration> Candidates { get; init; } = Array.Empty<SystemAiApiConfiguration>();

    /// <summary>Requested configuration ids that do not exist.</summary>
    public IReadOnlyList<long> MissingCandidateIds { get; init; } = Array.Empty<long>();

    public SystemAiApiConfiguration? Assessor { get; init; }
    public SystemAiApiConfiguration? CoAssessor { get; init; }

    /// <summary>The second reader of a single-assessor run, or the reference reader of a panel; null when none reads.</summary>
    public SystemAiApiConfiguration? Reader { get; init; }

    public SystemAiApiConfiguration? Verifier { get; init; }
    public SystemAiApiConfiguration? ReportWriter { get; init; }

    /// <summary>The suite or battery name, for messages.</summary>
    public string TargetName { get; init; } = string.Empty;

    /// <summary>Why the target cannot run, each in the launcher's or the battery start's own words (MB-B07).</summary>
    public IReadOnlyList<string> TargetRefusals { get; init; } = Array.Empty<string>();

    /// <summary>Per candidate, the launcher's refusal of its request (MB-B06).</summary>
    public IReadOnlyDictionary<long, string> CandidateRefusals { get; init; } = new Dictionary<long, string>();

    /// <summary>What is already running, or null when nothing is (MB-B09).</summary>
    public string? ActiveDescription { get; init; }

    public BenchmarkModelBatchProjectionResult Projection { get; init; } = new();

    public BenchmarkModelBatchOptions Options { get; init; } = new();

    /// <summary>The <c>RecommendedModels</c> entries by provider name.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<RecommendedModel>> Recommended { get; init; }
        = new Dictionary<string, IReadOnlyList<RecommendedModel>>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Candidates whose price card does not resolve (MB-A07).</summary>
    public IReadOnlyCollection<long> UnpricedCandidateIds { get; init; } = Array.Empty<long>();

    /// <summary>The scoring profile's speed target (MB-A08).</summary>
    public int? SpeedTargetMs { get; init; }

    /// <summary>The scoring profile blinds the second reader (MB-A12).</summary>
    public bool SecondOpinionBlind { get; init; } = true;

    /// <summary>The graders of the most recent completed run on the target; null when there is none (MB-A10).</summary>
    public BenchmarkModelBatchRoster? RecentRoster { get; init; }

    /// <summary>Per candidate, when its most recent completed run on the target started (MB-T03).</summary>
    public IReadOnlyDictionary<long, DateTime> LastRunStartedAtUtc { get; init; } = new Dictionary<long, DateTime>();

    public DateTime NowUtc { get; init; } = DateTime.UtcNow;

    /// <summary>The provider and model rules; a guard over an empty configuration when null.</summary>
    public BenchmarkComplianceGuard? Guard { get; init; }
}

/// <summary>
/// The model batch guardrails: every blocker, warning and piece of advice of
/// <c>model_batch_guardrails_v3.md</c> §§ 3.1–3.4, evaluated in one place over a
/// <see cref="BenchmarkModelBatchGuardContext"/>. The launcher renders what this returns, and the
/// start endpoint evaluates it again.
/// </summary>
public static class BenchmarkModelBatchGuardrails
{
    public const int TitleMaxLength = 60;
    public const int DetailMaxLength = 140;

    /// <summary>Thinking levels a profile fit check treats as deliberating (MB-A08).</summary>
    private static readonly string[] DeliberatingLevels = { "high", "max" };

    /// <summary>Grader effort levels that slow graders and break their JSON (MB-W04).</summary>
    private static readonly string[] ExcessiveGraderLevels = { "xhigh", "max" };

    /// <summary>Speed targets under this are interactive (MB-A08).</summary>
    public const int InteractiveSpeedTargetMaxMs = 30000;

    // Field names, as the launcher's controls are keyed.
    public const string FieldModels = "models";
    public const string FieldAssessor = "assessor";
    public const string FieldCoAssessor = "coAssessor";
    public const string FieldReader = "reader";
    public const string FieldVerifier = "verifier";
    public const string FieldReportWriter = "reportWriter";
    public const string FieldProfile = "profile";
    public const string FieldResponseStyle = "responseStyle";
    public const string FieldSourceReferences = "sourceReferences";
    public const string FieldRunsPerModel = "runsPerModel";
    public const string FieldOrder = "order";
    public const string FieldCapWait = "capWait";
    public const string FieldTarget = "target";

    private static readonly BenchmarkComplianceGuard DefaultGuard =
        new(new ConfigurationBuilder().Build(), null!);

    public static IReadOnlyList<BenchmarkModelBatchFindingDto> Evaluate(BenchmarkModelBatchGuardContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);

        var guard = ctx.Guard ?? DefaultGuard;
        var findings = new List<BenchmarkModelBatchFindingDto>();
        var request = ctx.Request;
        var run = request.Run ?? new StartBenchmarkRunRequest();
        var candidates = ctx.Candidates;
        var candidateIds = candidates.Select(c => c.Id).ToList();
        bool panel = ctx.CoAssessor != null;
        string readerRole = panel ? "reference reader" : "second reader";
        int requestedCount = request.TestedModelConfigurationIds?.Distinct().Count() ?? 0;
        var projection = ctx.Projection;

        void Add(string code, string name, string severity, string? field, string title, string detail,
                 IEnumerable<long>? ids = null, string? discriminator = null, bool clipDetail = true)
        {
            var sorted = (ids ?? Array.Empty<long>()).Distinct().OrderBy(id => id).ToList();
            findings.Add(new BenchmarkModelBatchFindingDto
            {
                Code = code,
                Name = name,
                Severity = severity,
                Field = field,
                Title = Clip(title, TitleMaxLength),
                Detail = clipDetail ? Clip(detail, DetailMaxLength) : detail,
                ModelConfigurationIds = sorted,
                AcknowledgmentKey = severity == BenchmarkModelBatchSeverity.Warning
                    ? AcknowledgmentKey(code, sorted, discriminator)
                    : null
            });
        }

        const string Blocker = BenchmarkModelBatchSeverity.Blocker;
        const string Warning = BenchmarkModelBatchSeverity.Warning;
        const string Advice = BenchmarkModelBatchSeverity.Advice;

        // --- 3.1 Blockers ----------------------------------------------------------------------

        if (requestedCount < 2)
        {
            Add("MB-B01", "TooFewModels", Blocker, FieldModels,
                "Choose at least two models",
                "A batch compares models; for one model, choose One model.");
        }

        if (requestedCount > ctx.Options.MaxModels)
        {
            Add("MB-B02", "TooManyModels", Blocker, FieldModels,
                $"At most {Inv(ctx.Options.MaxModels)} models in one batch",
                "Split the comparison into two batches under the same graders.");
        }

        var gradesItself = new HashSet<long>();
        foreach (var (role, field, grader) in new[]
                 {
                     ("assessor", FieldAssessor, ctx.Assessor),
                     ("co-assessor", FieldCoAssessor, ctx.CoAssessor)
                 })
        {
            if (grader == null) continue;
            foreach (var candidate in candidates.Where(c => guard.IsSameModel(c, grader)))
            {
                gradesItself.Add(candidate.Id);
                Add("MB-B03", "GraderIsCandidate", Blocker, field,
                    $"{Label(candidate)} cannot grade itself",
                    $"Choose another {role}: a scoring grader must not be a model under test.",
                    new[] { candidate.Id });
            }
        }

        var writesOwnReport = new HashSet<long>();
        if (ctx.ReportWriter != null)
        {
            foreach (var candidate in candidates.Where(c => guard.IsSameModel(c, ctx.ReportWriter)))
            {
                writesOwnReport.Add(candidate.Id);
                Add("MB-B04", "ReportWriterIsCandidate", Blocker, FieldReportWriter,
                    $"{Label(candidate)} cannot write its own report",
                    "Choose another report writer, or None.",
                    new[] { candidate.Id });
            }
        }

        bool panelInvalid = false;
        if (ctx.Assessor != null && ctx.CoAssessor != null)
        {
            if (ctx.CoAssessor.Id == ctx.Assessor.Id)
            {
                panelInvalid = true;
                Add("MB-B05", "PanelInvalid", Blocker, FieldCoAssessor,
                    "The co-assessor is the assessor",
                    "The co-assessor must be a different configuration from the assessor.");
            }
            else if (guard.IsSameProvider(ctx.Assessor, ctx.CoAssessor))
            {
                panelInvalid = true;
                Add("MB-B05", "PanelInvalid", Blocker, FieldCoAssessor,
                    "The panel needs two providers",
                    $"A panel needs members from two providers. The assessor and the co-assessor both belong to {ctx.Assessor.Provider}.",
                    clipDetail: false);
            }
        }

        foreach (long missing in ctx.MissingCandidateIds)
        {
            Add("MB-B06", "MemberRefused", Blocker, FieldModels,
                $"Configuration #{Inv(missing)} is refused",
                $"Configuration #{Inv(missing)}: the model configuration no longer exists.",
                new[] { missing }, clipDetail: false);
        }

        foreach (var candidate in candidates)
        {
            if (!ctx.CandidateRefusals.TryGetValue(candidate.Id, out var refusal)) continue;
            if (panelInvalid || gradesItself.Contains(candidate.Id) || writesOwnReport.Contains(candidate.Id)) continue;

            Add("MB-B06", "MemberRefused", Blocker, FieldModels,
                $"{Label(candidate)} is refused",
                $"{Label(candidate)}: {refusal}",
                new[] { candidate.Id }, clipDetail: false);
        }

        foreach (string refusal in ctx.TargetRefusals)
        {
            Add("MB-B07", "TargetNotReady", Blocker, FieldTarget,
                string.IsNullOrWhiteSpace(ctx.TargetName) ? "The target is not ready" : $"{ctx.TargetName} is not ready",
                refusal, clipDetail: false);
        }

        if (projection.ExceedsDailyCap && !request.AllowCapWait)
        {
            Add("MB-B08", "CapExceeded", Blocker, FieldCapWait,
                $"{Inv(projection.PlannedLaunches)} runs exceed the daily cap of {Inv(projection.Projection.Limits.MaxRunsPerDay)}",
                "Check Wait when the run cap blocks the next run, or choose fewer models.");
        }

        if (projection.MemberPlanExceeds)
        {
            int plan = projection.Projection.Limits.MemberPlanRuns ?? 0;
            if (request.TargetKind == BenchmarkModelBatchTargetKind.Battery)
            {
                Add("MB-B08", "CapExceeded", Blocker, FieldRunsPerModel,
                    $"{Inv(plan)} runs per model exceed the battery limit of {Inv(projection.MemberPlanLimit)}",
                    $"One battery run may plan at most {Inv(projection.MemberPlanLimit)} runs (Benchmark:Battery:MaxMembers); lower Runs per Suite.");
            }
            else
            {
                Add("MB-B08", "CapExceeded", Blocker, FieldRunsPerModel,
                    $"{Inv(plan)} runs per model exceed the daily cap of {Inv(projection.MemberPlanLimit)}",
                    $"One series may plan at most {Inv(projection.MemberPlanLimit)} runs, the daily cap; lower Runs per model.");
            }
        }

        if (projection.SpendRefusesStart)
        {
            var limits = projection.Projection.Limits;
            Add("MB-B08", "CapExceeded", Blocker, limits.SpendDenialIsCap ? FieldCapWait : null,
                limits.SpendDenialIsCap ? "The run cap refuses the first launch" : "The spend guard refuses the first launch",
                limits.SpendDenialReason ?? "The benchmark spend guard refused the first launch.",
                clipDetail: false);
        }

        if (ctx.ActiveDescription != null)
        {
            Add("MB-B09", "RunActive", Blocker, null,
                "A benchmark is already running",
                "Wait for it to finish or cancel it.");
        }

        // --- 3.2 Warnings ----------------------------------------------------------------------

        var providers = candidates
            .Select(c => (c.Provider ?? string.Empty).Trim())
            .Where(p => p.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (providers.Count >= 2 && !panel)
        {
            Add("MB-W01", "MixedFamiliesSingleAssessor", Warning, FieldCoAssessor,
                "Use a two-family panel for this batch",
                "A single assessor favors its own provider's models over the others.",
                candidateIds);
        }

        foreach (var candidate in candidates)
        {
            var roles = new List<(string Role, string Field)>();
            if (ctx.Reader != null && guard.IsSameModel(candidate, ctx.Reader)) roles.Add((readerRole, FieldReader));
            if (ctx.Verifier != null && guard.IsSameModel(candidate, ctx.Verifier)) roles.Add(("claim verifier", FieldVerifier));
            if (roles.Count == 0) continue;

            Add("MB-W02", "AnchorIsCandidate", Warning, roles[0].Field,
                $"{Label(candidate)} checks its own answers",
                $"Its {string.Join(" and ", roles.Select(r => r.Role))} findings on its own run are not independent.",
                new[] { candidate.Id });
        }

        if (ctx.Verifier == null)
        {
            Add("MB-W03", "NoClaimVerifier", Warning, FieldVerifier,
                "No claim verifier",
                "Refuted claims go unchecked, and a panel split on a critical error stays unresolved.");
        }

        var graderRoles = new List<(string Role, string Field, SystemAiApiConfiguration Config)>();
        if (ctx.Assessor != null) graderRoles.Add(("Assessor", FieldAssessor, ctx.Assessor));
        if (ctx.CoAssessor != null) graderRoles.Add(("Co-assessor", FieldCoAssessor, ctx.CoAssessor));
        if (ctx.Reader != null) graderRoles.Add((panel ? "Reference reader" : "Second reader", FieldReader, ctx.Reader));
        if (ctx.Verifier != null) graderRoles.Add(("Claim verifier", FieldVerifier, ctx.Verifier));

        foreach (var group in graderRoles
                     .Where(g => IsOneOf(g.Config.ThinkingLevel, ExcessiveGraderLevels))
                     .GroupBy(g => g.Config.Id))
        {
            var roles = group.ToList();
            string roleText = roles.Count == 1
                ? roles[0].Role
                : roles[0].Role + " and " + string.Join(" and ", roles.Skip(1).Select(r => r.Role.ToLowerInvariant()));
            Add("MB-W04", "GraderEffortTooHigh", Warning, roles[0].Field,
                $"{roleText} at {roles[0].Config.ThinkingLevel!.Trim()} effort",
                "Very high effort slows graders and breaks their JSON; medium is recommended.",
                new[] { group.Key });
        }

        if (candidates.Count >= 2)
        {
            foreach (var (attribute, value) in new (string, Func<SystemAiApiConfiguration, string>)[]
                     {
                         ("Parallel mode", c => c.ParallelExecutionMode.ToString()),
                         ("Service tier", c => string.IsNullOrWhiteSpace(c.ServiceTier) ? "default" : c.ServiceTier.Trim()),
                         ("Endpoint", c => EndpointKind(c)),
                         ("Max output tokens", c => c.MaxOutputTokens?.ToString(CultureInfo.InvariantCulture) ?? "default")
                     })
            {
                var groups = candidates
                    .GroupBy(value, StringComparer.OrdinalIgnoreCase)
                    .OrderByDescending(g => g.Count())
                    .ToList();
                if (groups.Count < 2) continue;

                var odd = groups.Count == candidates.Count ? groups.SelectMany(g => g) : groups.Skip(1).SelectMany(g => g);
                string models = string.Join(", ", odd.Select(c => $"{Label(c)} ({value(c)})"));
                Add("MB-W05", "CandidateSettingsDiffer", Warning, FieldModels,
                    "Models differ in more than the model",
                    $"{attribute} differs: {models}; the comparison mixes it with the model.",
                    candidateIds, discriminator: string.Concat(attribute.Split(' ').Select(word => char.ToUpperInvariant(word[0]) + word[1..])));
            }
        }

        foreach (var duplicates in candidates
                     .GroupBy(c => string.Join("|",
                         Norm(c.Provider), Norm(c.ModelId), Norm(c.ThinkingLevel), Norm(c.ServiceTier),
                         c.ParallelExecutionMode.ToString(), EndpointKind(c)))
                     .Where(g => g.Count() > 1))
        {
            var group = duplicates.ToList();
            Add("MB-W06", "DuplicateConfiguration", Warning, FieldModels,
                $"{Label(group[0])} is selected twice",
                "For repeats, use Runs per model instead.",
                group.Select(c => c.Id));
        }

        if (run.VerboseMode == true)
        {
            Add("MB-W07", "DetailedStyle", Warning, FieldResponseStyle,
                "Detailed style is not the production chat",
                "Results will not compare with Concise runs of these models.");
        }

        if (ctx.ReportWriter != null)
        {
            var sameFamily = candidates
                .Where(c => !writesOwnReport.Contains(c.Id)
                            && BenchmarkRunReportDocumentService.WriterWarning(ctx.ReportWriter, c, guard) != null)
                .ToList();
            if (sameFamily.Count > 0)
            {
                Add("MB-W08", "ReportWriterSharesFamily", Warning, FieldReportWriter,
                    $"{Label(ctx.ReportWriter)} reports on its own provider",
                    $"{JoinLabels(sameFamily)}' documents come from a same-provider writer.",
                    sameFamily.Select(c => c.Id));
            }
        }

        decimal? cost = projection.Projection.ProjectedCostUsd;
        long? wallMs = projection.Projection.ProjectedWallMs;
        double? wallHours = wallMs.HasValue ? wallMs.Value / 3_600_000d : null;
        if ((cost.HasValue && cost.Value > ctx.Options.WarnCostUsd)
            || (wallHours.HasValue && wallHours.Value > ctx.Options.WarnWallHours))
        {
            string costText = cost.HasValue ? "US$" + cost.Value.ToString("0", CultureInfo.InvariantCulture) : "unknown cost";
            string hoursText = wallHours.HasValue ? wallHours.Value.ToString("0.#", CultureInfo.InvariantCulture) : "?";
            Add("MB-W09", "LargeBatch", Warning, null,
                $"Large batch: about {costText}, {hoursText} h",
                "Check the projection before starting.",
                candidateIds);
        }

        if (!panel && ctx.Assessor != null)
        {
            var sameProvider = candidates
                .Where(c => !gradesItself.Contains(c.Id) && guard.IsSameProvider(c, ctx.Assessor))
                .ToList();
            if (sameProvider.Count > 0)
            {
                Add("MB-W10", "SameProviderAssessor", Warning, FieldAssessor,
                    $"{Label(ctx.Assessor)} grades its own provider's models",
                    $"{JoinLabels(sameProvider)} are graded by a same-provider assessor.",
                    sameProvider.Select(c => c.Id));
            }
        }

        if (projection.ExceedsDailyHeadroom)
        {
            Add("MB-W11", "ExceedsDailyHeadroom", Warning, FieldCapWait,
                $"{Inv(projection.PlannedLaunches)} runs, {Inv(projection.Projection.Limits.RemainingDailyHeadroom)} left in the 24-hour window",
                "With Wait checked it pauses at the cap; without, it stops there.",
                candidateIds);
        }

        if (projection.HourlyCapRisk)
        {
            string max = Inv(projection.Projection.Limits.MaxRunsPerHour);
            Add("MB-W12", "HourlyCapRisk", Warning, FieldCapWait,
                $"Runs may outpace the hourly cap of {max}",
                $"Short runs launch faster than {max} per hour allows; the batch will pause.",
                candidateIds);
        }

        // --- 3.3 Advice ------------------------------------------------------------------------

        if (request.RunsPerModel == 1)
        {
            Add("MB-A01", "SingleRunPerModel", Advice, FieldRunsPerModel,
                "One run per model",
                "Differences under about 2 index points are noise; use 2–3 runs to rank.");
        }

        if (!panel && ctx.Assessor != null && candidates.Count > 0
            && candidates.All(c => guard.IsSameProvider(c, ctx.Assessor)))
        {
            Add("MB-A02", "UniformFamilyBias", Advice, FieldAssessor,
                "All models share the assessor's provider",
                "The ranking is fair, but absolute scores may run high.");
        }

        foreach (var (role, field, anchor) in new[]
                 {
                     (panel ? "Reference reader" : "Second reader", FieldReader, ctx.Reader),
                     ("Claim verifier", FieldVerifier, ctx.Verifier)
                 })
        {
            if (anchor == null) continue;

            var sharing = candidates
                .Where(c => guard.IsSameProvider(c, anchor) && !guard.IsSameModel(c, anchor))
                .Select(Label)
                .ToList();
            var sharingIds = candidates
                .Where(c => guard.IsSameProvider(c, anchor) && !guard.IsSameModel(c, anchor))
                .Select(c => c.Id)
                .ToList();
            foreach (var member in new[] { ctx.Assessor, ctx.CoAssessor })
            {
                if (member != null && member.Id != anchor.Id && guard.IsSameProvider(member, anchor))
                {
                    sharing.Add(Label(member));
                }
            }

            if (sharing.Count == 0) continue;
            Add("MB-A03", "AnchorSharesFamily", Advice, field,
                $"{role} shares {anchor.Provider} with {string.Join(", ", sharing.Distinct())}",
                "A third family keeps the reference checks neutral.",
                sharingIds);
        }

        if (ctx.Reader == null)
        {
            Add("MB-A04", "NoReferenceReader", Advice, FieldReader,
                "No reference reader",
                "Model Comparison cannot estimate the panel's family bias without one.");
        }

        if (panel && ctx.Assessor != null
            && !string.Equals(Norm(ctx.Assessor.ThinkingLevel), Norm(ctx.CoAssessor!.ThinkingLevel), StringComparison.Ordinal))
        {
            Add("MB-A05", "CoAssessorNotPeer", Advice, FieldCoAssessor,
                "Panel members at different effort",
                "A panel assumes peers; use the same effort for both.");
        }

        if (run.AllowSourceCodeReferences == true)
        {
            Add("MB-A06", "SourceReferencesAllowed", Advice, FieldSourceReferences,
                "Not the production default",
                "Players get Disallowed; results will differ from what they see.");
        }

        foreach (var candidate in candidates.Where(c => ctx.UnpricedCandidateIds.Contains(c.Id)))
        {
            Add("MB-A07", "PricingIncomplete", Advice, FieldModels,
                $"No price for {Label(candidate)}",
                "Its cost per question will be missing from the comparison.",
                new[] { candidate.Id });
        }

        if (ctx.SpeedTargetMs is int speedTarget && speedTarget < InteractiveSpeedTargetMaxMs)
        {
            foreach (var candidate in candidates.Where(c => IsOneOf(c.ThinkingLevel, DeliberatingLevels)))
            {
                Add("MB-A08", "ProfileFit", Advice, FieldProfile,
                    "This profile targets interactive latency",
                    $"{Label(candidate)} at {candidate.ThinkingLevel!.Trim()} will score low on Speed Index; read it as advisory.",
                    new[] { candidate.Id });
            }
        }

        var recommendedHere = providers
            .SelectMany(p => ctx.Recommended.TryGetValue(p, out var list)
                ? list.Select(r => (Provider: p, Entry: r))
                : Enumerable.Empty<(string Provider, RecommendedModel Entry)>())
            .ToList();
        if (recommendedHere.Count > 0
            && !candidates.Any(c => recommendedHere.Any(r => IsRecommendedEntry(c, r.Provider, r.Entry, guard))))
        {
            string names = string.Join(" or ", recommendedHere.Select(r =>
                string.IsNullOrWhiteSpace(r.Entry.ThinkingLevel) ? r.Entry.Model : $"{r.Entry.Model} ({r.Entry.ThinkingLevel})"));
            Add("MB-A09", "NoBaseline", Advice, FieldModels,
                "No current production model in the batch",
                $"Add {names} as the baseline the others are judged against.");
        }

        if (ctx.RecentRoster != null)
        {
            var roster = new BenchmarkModelBatchRoster(ctx.Assessor?.Id, ctx.CoAssessor?.Id, ctx.Reader?.Id, ctx.Verifier?.Id);
            if (roster != ctx.RecentRoster)
            {
                Add("MB-A10", "RosterDiffersFromRecent", Advice, FieldAssessor,
                    "Graders differ from recent runs",
                    "This batch will not compare with earlier runs of these models.");
            }
        }

        if (ctx.ReportWriter != null)
        {
            int perReport = BenchmarkRunReportDocumentService.Audiences.Count;
            int documents = request.TargetKind == BenchmarkModelBatchTargetKind.Battery
                ? candidates.Count * perReport
                : candidates.Count * Math.Max(1, request.RunsPerModel) * perReport;
            Add("MB-A11", "ReportWriterInBatch", Advice, FieldReportWriter,
                $"{Inv(documents)} AI documents will be written",
                "For one document on the whole batch, use Model Comparison after it.");
        }

        if (!panel && ctx.Reader != null && !ctx.SecondOpinionBlind)
        {
            Add("MB-A12", "AnchoredSecondReader", Advice, FieldReader,
                "Second reader sees the first verdict",
                "A blind reader gives an independent check.");
        }

        // --- 3.4 Time and order ----------------------------------------------------------------

        if (request.Order == BenchmarkModelBatchOrder.AsListed)
        {
            Add("MB-T01", "OrderAsListed", Advice, FieldOrder,
                "Models run in a fixed order",
                "Randomize so no model always gets the same hours.");
        }

        if (wallHours.HasValue && wallHours.Value > ctx.Options.LongWallHours)
        {
            Add("MB-T02", "LongWallTime", Advice, null,
                "Models run hours apart",
                "Their speed figures include time-of-day effects.");
        }

        string nowStratum = Stratum(ctx.NowUtc);
        foreach (var candidate in candidates)
        {
            if (!ctx.LastRunStartedAtUtc.TryGetValue(candidate.Id, out var last)) continue;
            string lastStratum = Stratum(last);
            if (string.Equals(lastStratum, nowStratum, StringComparison.Ordinal)) continue;

            Add("MB-T03", "StratumDiffersFromRecent", Advice, null,
                $"{Label(candidate)} last ran {lastStratum}",
                "Starting now puts it in another time block for Chat Consistency speed.",
                new[] { candidate.Id });
        }

        return findings
            .Select((f, i) => (Finding: f, Index: i))
            .OrderBy(x => SeverityRank(x.Finding.Severity))
            .ThenBy(x => x.Index)
            .Select(x => x.Finding)
            .ToList();
    }

    /// <summary>The findings that block a start.</summary>
    public static IReadOnlyList<BenchmarkModelBatchFindingDto> Blockers(IEnumerable<BenchmarkModelBatchFindingDto> findings)
        => findings.Where(f => f.Severity == BenchmarkModelBatchSeverity.Blocker).ToList();

    /// <summary>The warnings whose acknowledgment key is not among <paramref name="acknowledgedKeys"/>.</summary>
    public static IReadOnlyList<BenchmarkModelBatchFindingDto> UnacknowledgedWarnings(
        IEnumerable<BenchmarkModelBatchFindingDto> findings, IEnumerable<string>? acknowledgedKeys)
    {
        var acknowledged = new HashSet<string>(acknowledgedKeys ?? Array.Empty<string>(), StringComparer.Ordinal);
        return findings
            .Where(f => f.Severity == BenchmarkModelBatchSeverity.Warning
                        && (f.AcknowledgmentKey == null || !acknowledged.Contains(f.AcknowledgmentKey)))
            .ToList();
    }

    /// <summary>The code, an optional discriminator, and the sorted affected ids: <c>MB-W05/ServiceTier:3,7</c>.</summary>
    public static string AcknowledgmentKey(string code, IEnumerable<long> affectedIds, string? discriminator = null)
    {
        var ids = affectedIds.Distinct().OrderBy(id => id).ToList();
        string key = discriminator == null ? code : code + "/" + discriminator;
        return ids.Count == 0 ? key : key + ":" + string.Join(",", ids.Select(Inv));
    }

    /// <summary>The day kind and 4-hour UTC block a start falls in, as the stratum text reads it.</summary>
    public static string Stratum(DateTime utc)
    {
        bool weekend = utc.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
        int block = utc.Hour / 4 * 4;
        return $"on a {(weekend ? "weekend" : "weekday")}, {block:00}–{block + 4:00} UTC";
    }

    /// <summary>The display name of a configuration, else its model id.</summary>
    public static string Label(SystemAiApiConfiguration config)
        => string.IsNullOrWhiteSpace(config.DisplayName) ? config.ModelId : config.DisplayName.Trim();

    /// <summary><c>official</c>, or the custom endpoint's fingerprint.</summary>
    public static string EndpointKind(SystemAiApiConfiguration config)
        => SystemAiConfigurationSnapshotStore.EndpointFingerprint(SystemAiConfigurationSnapshotStore.FromConfiguration(config));

    internal static string Clip(string text, int max)
        => text.Length <= max ? text : text.Substring(0, max - 1).TrimEnd() + "…";

    private static bool IsRecommendedEntry(SystemAiApiConfiguration candidate, string provider, RecommendedModel entry, BenchmarkComplianceGuard guard)
    {
        if (!guard.IsSameProvider(candidate.Provider, provider)) return false;
        if (!string.Equals(Norm(candidate.ModelId), Norm(entry.Model), StringComparison.Ordinal)) return false;
        return string.IsNullOrWhiteSpace(entry.ThinkingLevel)
               || string.Equals(Norm(candidate.ThinkingLevel), Norm(entry.ThinkingLevel), StringComparison.Ordinal);
    }

    private static string JoinLabels(IReadOnlyList<SystemAiApiConfiguration> configs)
        => string.Join(", ", configs.Select(Label));

    private static bool IsOneOf(string? level, IEnumerable<string> levels)
        => !string.IsNullOrWhiteSpace(level) && levels.Contains(level.Trim().ToLowerInvariant());

    private static string Norm(string? value) => (value ?? string.Empty).Trim().ToLowerInvariant();

    private static int SeverityRank(string severity) => severity switch
    {
        BenchmarkModelBatchSeverity.Blocker => 0,
        BenchmarkModelBatchSeverity.Warning => 1,
        _ => 2
    };

    private static string Inv(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Inv(long value) => value.ToString(CultureInfo.InvariantCulture);
}

/// <summary>
/// Builds a <see cref="BenchmarkModelBatchGuardContext"/> from the database and the running
/// services, and evaluates it. Scoped: it holds the request's <see cref="ApplicationDbContext"/>.
/// Side-effect free; the launcher is asked through <see cref="BenchmarkRunLauncher.ValidateRequestAsync"/>.
/// </summary>
public class BenchmarkModelBatchGuardrailService
{
    private static readonly BenchmarkRunStatus[] CompletedStatuses =
    {
        BenchmarkRunStatus.Completed,
        BenchmarkRunStatus.CompletedWithLimits,
        BenchmarkRunStatus.CompletedWithErrors
    };

    private readonly ApplicationDbContext _db;
    private readonly BenchmarkComplianceGuard _guard;
    private readonly BenchmarkRunLauncher _launcher;
    private readonly BenchmarkRunManager _runManager;
    private readonly IConfiguration _configuration;
    private readonly BenchmarkSeriesOrchestrator? _seriesOrchestrator;
    private readonly BenchmarkBatteryOrchestrator? _batteryOrchestrator;
    private readonly ModelPricingService? _pricing;

    public BenchmarkModelBatchGuardrailService(
        ApplicationDbContext db,
        BenchmarkComplianceGuard guard,
        BenchmarkRunLauncher launcher,
        BenchmarkRunManager runManager,
        IConfiguration configuration,
        BenchmarkSeriesOrchestrator? seriesOrchestrator = null,
        BenchmarkBatteryOrchestrator? batteryOrchestrator = null,
        ModelPricingService? pricing = null)
    {
        _db = db;
        _guard = guard;
        _launcher = launcher;
        _runManager = runManager;
        _configuration = configuration;
        _seriesOrchestrator = seriesOrchestrator;
        _batteryOrchestrator = batteryOrchestrator;
        _pricing = pricing;
    }

    /// <summary>The findings and the projection a preflight returns.</summary>
    public async Task<(IReadOnlyList<BenchmarkModelBatchFindingDto> Findings, BenchmarkModelBatchProjectionDto Projection)> EvaluateAsync(
        StartBenchmarkModelBatchRequest request,
        string? activeBatchDescription,
        CancellationToken ct = default)
    {
        var ctx = await BuildContextAsync(request, activeBatchDescription, ct);
        return (BenchmarkModelBatchGuardrails.Evaluate(ctx), ctx.Projection.Projection);
    }

    public async Task<BenchmarkModelBatchGuardContext> BuildContextAsync(
        StartBenchmarkModelBatchRequest request,
        string? activeBatchDescription,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var run = request.Run ?? new StartBenchmarkRunRequest();
        var options = BenchmarkModelBatchOptions.From(_configuration);

        var requestedIds = (request.TestedModelConfigurationIds ?? new List<long>()).Distinct().ToList();
        var configs = await _db.SystemAiApiConfigurations
            .AsNoTracking()
            .Where(c => requestedIds.Contains(c.Id))
            .ToListAsync(ct);
        var byId = configs.ToDictionary(c => c.Id);
        var candidates = requestedIds.Where(byId.ContainsKey).Select(id => byId[id]).ToList();
        var missing = requestedIds.Where(id => !byId.ContainsKey(id)).ToList();

        async Task<SystemAiApiConfiguration?> Find(long? id)
            => id.HasValue
                ? await _db.SystemAiApiConfigurations.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id.Value, ct)
                : null;

        var assessor = await Find(run.AssessorModelConfigurationId);
        var coAssessor = await Find(run.CoAssessorModelConfigurationId);
        var reader = run.SecondOpinionMode == (int)BenchmarkSecondOpinionMode.Off
            ? null
            : await Find(run.SecondOpinionAssessorModelConfigurationId);
        var verifier = await Find(run.ClaimVerifierModelConfigurationId);
        var writer = await Find(run.ReportWriterModelConfigurationId);

        // The target: its suites and whatever keeps it from running.
        var targetRefusals = new List<string>();
        var suiteIds = new List<long>();
        string targetName = string.Empty;
        if (request.TargetKind == BenchmarkModelBatchTargetKind.Battery)
        {
            var battery = request.BatteryId.HasValue
                ? await _db.BenchmarkBatteries.AsNoTracking().Include(b => b.Suites).FirstOrDefaultAsync(b => b.Id == request.BatteryId.Value, ct)
                : null;
            if (battery == null)
            {
                targetRefusals.Add("Battery not found.");
            }
            else
            {
                targetName = battery.Name;
                if (battery.IsArchived) targetRefusals.Add($"Battery '{battery.Name}' is archived.");
                var errors = BenchmarkBatteryDefinition.Validate(battery);
                if (errors.Count > 0)
                {
                    targetRefusals.Add($"Battery '{battery.Name}' cannot be run: {string.Join(" ", errors)}");
                }

                suiteIds = battery.Suites
                    .OrderBy(s => s.OrderIndex)
                    .ThenBy(s => s.Id)
                    .Where(s => s.BenchmarkSuiteId.HasValue)
                    .Select(s => s.BenchmarkSuiteId!.Value)
                    .ToList();
                foreach (long suiteId in suiteIds)
                {
                    string? suiteRefusal = await SuiteRefusalAsync(suiteId, ct);
                    if (suiteRefusal != null) targetRefusals.Add(suiteRefusal);
                }
            }
        }
        else
        {
            if (request.SuiteId is long suiteId)
            {
                suiteIds.Add(suiteId);
                targetName = await _db.BenchmarkSuites.Where(s => s.Id == suiteId).Select(s => s.Name).FirstOrDefaultAsync(ct) ?? string.Empty;
                string? suiteRefusal = await SuiteRefusalAsync(suiteId, ct);
                if (suiteRefusal != null) targetRefusals.Add(suiteRefusal);
            }
            else
            {
                targetRefusals.Add("Choose the suite every model runs.");
            }
        }

        // MB-B06: the launcher's own rules, per candidate, once the target itself can run. The
        // acknowledgeable same-provider gates are MB-W10 and MB-W08, so they are acknowledged here.
        var candidateRefusals = new Dictionary<long, string>();
        if (targetRefusals.Count == 0 && suiteIds.Count > 0)
        {
            foreach (var candidate in candidates)
            {
                var probe = CloneRequest(run);
                probe.SuiteId = suiteIds[0];
                probe.TestedModelConfigurationId = candidate.Id;
                probe.RunCount = 1;
                probe.AcknowledgeSameProvider = true;
                probe.AcknowledgeSameProviderReportWriter = true;

                var refusal = await _launcher.ValidateRequestAsync(probe, ct);
                if (refusal != null && refusal.Outcome != BenchmarkRunLaunchOutcome.SpendDenied && refusal.Error != null)
                {
                    candidateRefusals[candidate.Id] = refusal.Error;
                }
            }
        }

        // The projection and the run limits.
        var limits = await _guard.GetLimitsAsync(_db, ct);
        var spend = await _guard.CheckSpendAsync(_db, ct);
        var (suiteBases, modelBases) = await BenchmarkModelBatchProjection.LoadBasesAsync(
            _db, new BenchmarkRunCostEstimator(_pricing), suiteIds, candidates.Select(c => c.Id).ToList(), ct);
        var projection = BenchmarkModelBatchProjection.Compute(new BenchmarkModelBatchProjectionInput
        {
            CandidateIds = requestedIds,
            SuiteIds = suiteIds,
            IsBattery = request.TargetKind == BenchmarkModelBatchTargetKind.Battery,
            RunsPerModel = request.RunsPerModel,
            AllowCapWait = request.AllowCapWait,
            SuiteBases = suiteBases,
            ModelBases = modelBases,
            Limits = limits,
            MaxBatteryMembers = _guard.MaxBatteryMembers,
            Spend = spend
        });

        // The scoring profile: the chosen one, else the default.
        var profile = run.ScoringProfileId.HasValue
            ? await _db.BenchmarkScoringProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.Id == run.ScoringProfileId.Value, ct)
            : await _db.BenchmarkScoringProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.IsDefault, ct);

        // The most recent completed run on the target, and each candidate's own.
        var recentRuns = suiteIds.Count == 0
            ? new List<RecentRun>()
            : await _db.BenchmarkRuns
                .AsNoTracking()
                .Where(r => r.BenchmarkSuiteId.HasValue && suiteIds.Contains(r.BenchmarkSuiteId.Value) && CompletedStatuses.Contains(r.Status))
                .OrderByDescending(r => r.StartedAtUtc)
                .Select(r => new RecentRun(
                    r.TestedModelConfigurationId,
                    r.StartedAtUtc,
                    r.AssessorModelConfigurationId,
                    r.CoAssessorModelConfigurationId,
                    r.SecondOpinionAssessorModelConfigurationId,
                    r.ClaimVerifierModelConfigurationId))
                .Take(500)
                .ToListAsync(ct);
        var newest = recentRuns.FirstOrDefault();
        var lastByCandidate = new Dictionary<long, DateTime>();
        foreach (var recent in recentRuns)
        {
            if (recent.TestedId is long id && requestedIds.Contains(id) && !lastByCandidate.ContainsKey(id))
            {
                lastByCandidate[id] = recent.StartedAtUtc;
            }
        }

        var unpriced = _pricing == null
            ? new List<long>()
            : candidates.Where(c => _pricing.Resolve(c) == null).Select(c => c.Id).ToList();

        return new BenchmarkModelBatchGuardContext
        {
            Request = request,
            Candidates = candidates,
            MissingCandidateIds = missing,
            Assessor = assessor,
            CoAssessor = coAssessor,
            Reader = reader,
            Verifier = verifier,
            ReportWriter = writer,
            TargetName = targetName,
            TargetRefusals = targetRefusals,
            CandidateRefusals = candidateRefusals,
            ActiveDescription = ActiveDescription(activeBatchDescription),
            Projection = projection,
            Options = options,
            Recommended = LoadRecommended(),
            UnpricedCandidateIds = unpriced,
            SpeedTargetMs = profile?.SpeedTargetMs,
            SecondOpinionBlind = profile?.SecondOpinionBlind ?? true,
            RecentRoster = newest == null
                ? null
                : new BenchmarkModelBatchRoster(newest.AssessorId, newest.CoAssessorId, newest.ReaderId, newest.VerifierId),
            LastRunStartedAtUtc = lastByCandidate,
            NowUtc = DateTime.UtcNow,
            Guard = _guard
        };
    }

    private sealed record RecentRun(
        long? TestedId,
        DateTime StartedAtUtc,
        long? AssessorId,
        long? CoAssessorId,
        long? ReaderId,
        long? VerifierId);

    /// <summary>What is already running, or null: a run, a claim, a series, a battery run or a batch.</summary>
    private string? ActiveDescription(string? activeBatchDescription)
    {
        if (activeBatchDescription != null) return activeBatchDescription;
        if (_runManager.CurrentRunId is long runId) return $"Run #{runId.ToString(CultureInfo.InvariantCulture)} is running.";
        if (_runManager.ClaimHolder is { } owner) return BenchmarkRunManager.ClaimConflictMessage(owner);
        if (_seriesOrchestrator?.ActiveSeriesId is long seriesId) return $"Series #{seriesId.ToString(CultureInfo.InvariantCulture)} is running.";
        if (_batteryOrchestrator?.ActiveBatteryRunId is long batteryRunId) return $"Battery run #{batteryRunId.ToString(CultureInfo.InvariantCulture)} is running.";
        return null;
    }

    /// <summary>The launcher's refusal of a suite that cannot run, in its own words; null when it can.</summary>
    private async Task<string?> SuiteRefusalAsync(long suiteId, CancellationToken ct)
    {
        var suite = await _db.BenchmarkSuites
            .AsNoTracking()
            .Include(s => s.Questions)
            .FirstOrDefaultAsync(s => s.Id == suiteId, ct);
        if (suite == null) return "Benchmark suite not found.";
        if (suite.Questions.Count == 0) return $"Benchmark suite '{suite.Name}' has no questions.";

        int unassessed = suite.Questions.Count(q => q.AssessedDifficulty == null);
        return unassessed > 0
            ? $"Benchmark suite '{suite.Name}' has {unassessed} of {suite.Questions.Count} question(s) without an assessed difficulty. " +
              "Assess question difficulty for the whole suite before running a benchmark."
            : null;
    }

    private IReadOnlyDictionary<string, IReadOnlyList<RecommendedModel>> LoadRecommended()
    {
        var result = new Dictionary<string, IReadOnlyList<RecommendedModel>>(StringComparer.OrdinalIgnoreCase);
        foreach (var provider in _configuration.GetSection("RecommendedModels").GetChildren())
        {
            var models = provider.Get<List<RecommendedModel>>();
            if (models != null && models.Count > 0) result[provider.Key] = models;
        }
        return result;
    }

    internal static StartBenchmarkRunRequest CloneRequest(StartBenchmarkRunRequest request)
        => System.Text.Json.JsonSerializer.Deserialize<StartBenchmarkRunRequest>(System.Text.Json.JsonSerializer.Serialize(request))!;
}
