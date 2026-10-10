namespace Overseer.Services.Benchmarking;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Overseer.Models;

/// <summary>
/// The plain-text diagnostics of a model batch, as the progress dialog copies or downloads it: what
/// ran, under which settings and guardrails, in which order, on which instrument, how fast, what
/// failed, and where it stands now. Pure: everything comes from the batch DTO and the labels given.
/// </summary>
public static class BenchmarkModelBatchDiagnostics
{
    public static string BuildText(
        BenchmarkModelBatchRunDto batch,
        IReadOnlyDictionary<long, string>? configurationLabels = null,
        DateTime? nowUtc = null)
    {
        ArgumentNullException.ThrowIfNull(batch);
        var labels = configurationLabels ?? new Dictionary<long, string>();
        var now = nowUtc ?? DateTime.UtcNow;
        var sb = new StringBuilder();

        string Name(long? id) => id is long value
            ? (labels.TryGetValue(value, out var label) ? $"{label} (#{Inv(value)})" : $"#{Inv(value)}")
            : "none";

        sb.AppendLine($"GnollBench model batch #{Inv(batch.Id)} diagnostics");
        sb.AppendLine($"Generated: {Utc(now)}");
        sb.AppendLine();

        Section(sb, "BATCH");
        Line(sb, "Status", batch.Status);
        if (batch.StopReason != null) Line(sb, "Stop reason", $"{batch.StopReason} ({batch.StopReasonText})");
        if (!string.IsNullOrWhiteSpace(batch.StopDetail)) Line(sb, "Stop detail", batch.StopDetail);
        Line(sb, "Target", $"{batch.TargetKind} '{batch.TargetName}' (#{Inv(batch.SuiteId ?? batch.BatteryId ?? 0)})"
                           + (batch.BatteryRevision.HasValue ? $", revision {Inv(batch.BatteryRevision.Value)}" : string.Empty));
        if (batch.BatteryDefinitionSha256 != null) Line(sb, "Battery definition", batch.BatteryDefinitionSha256);
        if (batch.SuiteNames.Count > 0) Line(sb, "Suites", string.Join(", ", batch.SuiteNames));
        Line(sb, "Created", $"{Utc(batch.CreatedAtUtc)} by {batch.CreatedByUserName ?? "unknown"}");
        Line(sb, "Started", Utc(batch.StartedAtUtc));
        Line(sb, "Completed", Utc(batch.CompletedAtUtc));
        Line(sb, "Members", $"{Inv(batch.RequestedMemberCount)} requested, {Inv(batch.CompletedMemberCount)} finished, " +
                            $"{Inv(batch.FailedMemberCount)} failed or stopped, {Inv(batch.SkippedMemberCount)} skipped");
        Line(sb, "Cost so far", $"candidate {Usd(batch.LiveCandidateCostUsd)}, total {Usd(batch.LiveTotalCostUsd)}");
        Line(sb, "Driven by this process", YesNo(batch.IsDriving));
        Line(sb, "Resume options", batch.ResumeOptions.Count == 0 ? "none" : string.Join(", ", batch.ResumeOptions.Select(o => o.Label)));
        sb.AppendLine();

        Section(sb, "SETTINGS");
        Line(sb, "Runs per model", Inv(batch.RunsPerModel) + (batch.TargetKind == "Battery" ? " per suite" : string.Empty));
        Line(sb, "Wait when the run cap blocks", YesNo(batch.AllowCapWait));
        var run = batch.Run;
        if (run != null)
        {
            Line(sb, "Assessor", Name(run.AssessorModelConfigurationId));
            Line(sb, "Co-assessor", Name(run.CoAssessorModelConfigurationId));
            Line(sb, run.CoAssessorModelConfigurationId.HasValue ? "Reference reader" : "Second reader",
                Name(run.SecondOpinionAssessorModelConfigurationId));
            Line(sb, "Second opinion mode", run.SecondOpinionMode?.ToString(CultureInfo.InvariantCulture) ?? "profile default");
            Line(sb, "Claim verifier", Name(run.ClaimVerifierModelConfigurationId));
            Line(sb, "Report writer", Name(run.ReportWriterModelConfigurationId));
            Line(sb, "Scoring profile", run.ScoringProfileId.HasValue ? "#" + Inv(run.ScoringProfileId.Value) : "default");
            Line(sb, "Response style", run.VerboseMode == true ? "Detailed" : "Concise");
            Line(sb, "Source code references", run.AllowSourceCodeReferences == true ? "Allowed" : "Disallowed");
        }
        else
        {
            sb.AppendLine("  The stored run settings could not be read.");
        }
        sb.AppendLine();

        Section(sb, "GUARDRAILS");
        if (batch.AcknowledgedFindings.Count == 0) sb.AppendLine("  Acknowledged at start: none");
        foreach (var finding in batch.AcknowledgedFindings)
        {
            sb.AppendLine($"  Acknowledged {finding.Code} {finding.Title} — {finding.Detail}" + Ids(finding.ModelConfigurationIds));
        }
        if (batch.AdviceAtStart.Count == 0) sb.AppendLine("  Advice at start: none");
        foreach (var finding in batch.AdviceAtStart)
        {
            sb.AppendLine($"  Advice {finding.Code} {finding.Title} — {finding.Detail}" + Ids(finding.ModelConfigurationIds));
        }
        if (batch.InstrumentChangeAcknowledged)
        {
            sb.AppendLine("  An instrument or grader change was accepted: the batch is not comparable across it.");
        }
        sb.AppendLine();

        Section(sb, "ORDER");
        Line(sb, "Mode", batch.Order);
        Line(sb, "Seed", batch.OrderSeed.HasValue ? Inv(batch.OrderSeed.Value) : "none");
        Line(sb, "Run order", string.Join(", ", batch.Members.OrderBy(m => m.OrderIndex).Select(m => m.Model.DisplayName)));
        sb.AppendLine();

        Section(sb, "MEMBERS");
        foreach (var member in batch.Members.OrderBy(m => m.OrderIndex))
        {
            var model = member.Model;
            sb.AppendLine($"  {Inv(member.OrderIndex + 1)}. {model.DisplayName} — {model.Provider} {model.ModelId}" +
                          $", thinking {model.ThinkingLevel ?? "default"}, tier {model.ServiceTier ?? "default"}" +
                          $", parallel {model.ParallelExecutionMode ?? "unknown"}, endpoint {model.Endpoint}" +
                          $", max output {(model.MaxOutputTokens.HasValue ? Inv(model.MaxOutputTokens.Value) : "default")}" +
                          $" (configuration #{Inv(model.ConfigurationId)})");
            sb.AppendLine($"     Status {member.Status}; run {Id(member.RunId)}, series {Id(member.SeriesId)}, battery run {Id(member.BatteryRunId)}; " +
                          $"runs [{string.Join(", ", member.RunIds.Select(Inv))}]");
            sb.AppendLine($"     Started {Utc(member.StartedAtUtc)}{Stratum(member.StartedAtUtc)}, completed {Utc(member.CompletedAtUtc)}, " +
                          $"duration {Duration(member.StartedAtUtc, member.CompletedAtUtc)}");
            if (member.CurrentRunId.HasValue)
            {
                sb.AppendLine($"     In flight: run #{Inv(member.CurrentRunId.Value)}, step {Inv(member.CurrentStepIndex ?? 0)} of {Inv(member.StepCount)}, " +
                              $"stage {member.CurrentStage ?? "unknown"}, {Inv(member.AnsweredQuestionCount)}/{Inv(member.TotalQuestionCount)} answered");
            }
            if (member.Result is { } result)
            {
                string index = result.OverallIndex.HasValue
                    ? $"Overall Index {Number(result.OverallIndex)}"
                    : $"Intelligence Index {Number(result.IntelligenceIndex)}";
                sb.AppendLine($"     Result: {index} ± {Number(result.IndexHalfWidth)} ({result.IndexSource ?? "none"}), " +
                              $"candidate {Usd(result.CandidateCostUsd)} ({Usd(result.CandidateCostPerQuestionUsd)} per question), total {Usd(result.TotalCostUsd)}, " +
                              $"refuted claims {Inv(result.RefutedClaims)}, confirmed critical errors {Inv(result.ConfirmedCriticalErrors)}");
            }
        }
        sb.AppendLine();

        Section(sb, "INSTRUMENT");
        if (batch.FirstMemberInstrument is { } first)
        {
            sb.AppendLine("  First member:");
            Instrument(sb, first, "    ");
        }
        else
        {
            sb.AppendLine("  First member: not recorded yet");
        }
        foreach (var member in batch.Members.OrderBy(m => m.OrderIndex).Where(m => m.Instrument != null))
        {
            sb.AppendLine($"  {Inv(member.OrderIndex + 1)}. {member.Model.DisplayName}" +
                          (member.InstrumentDriftKeys.Count > 0 ? $" — DRIFT: {string.Join(", ", member.InstrumentDriftKeys)}" : " — matches the first member"));
            Instrument(sb, member.Instrument!, "    ");
        }
        sb.AppendLine();

        Section(sb, "TIMING");
        foreach (var member in batch.Members.OrderBy(m => m.OrderIndex))
        {
            var result = member.Result;
            sb.AppendLine($"  {Inv(member.OrderIndex + 1)}. {member.Model.DisplayName}: median model time {Ms(result?.MedianModelTimeMs)}, " +
                          $"TTFT P50 {Ms(result?.TtftP50Ms)}, own-wait share {Percent(result?.OwnWaitShare)}");
        }
        sb.AppendLine();

        Section(sb, "ERRORS");
        foreach (var member in batch.Members.OrderBy(m => m.OrderIndex))
        {
            var result = member.Result;
            sb.AppendLine($"  {Inv(member.OrderIndex + 1)}. {member.Model.DisplayName}: failed answers {Inv(result?.FailedAnswers ?? 0)}, " +
                          $"provider errors {Inv(result?.ProviderErrors ?? 0)}, retries {Inv(result?.Retries ?? 0)}" +
                          (string.IsNullOrWhiteSpace(member.ErrorMessage) ? string.Empty : $"; error: {member.ErrorMessage}"));
        }
        sb.AppendLine();

        Section(sb, "CAPS");
        Line(sb, "Wait when the run cap blocks", YesNo(batch.AllowCapWait));
        Line(sb, "Waiting on the cap now", YesNo(batch.Status == "WaitingForCap"));
        Line(sb, "Stopped on a cap or spend refusal", YesNo(batch.StopReason is "RunCapReached" or "SpendDenied"));
        sb.AppendLine();

        Section(sb, "PROGRESS");
        Line(sb, "Last progress", Utc(batch.LastProgressAtUtc));
        Line(sb, "Since last progress", batch.LastProgressAtUtc.HasValue
            ? FormatDuration(now - batch.LastProgressAtUtc.Value)
            : "unknown");
        Line(sb, "Stall threshold", $"{Inv(batch.StallMinutes)} min");
        Line(sb, "Stalled", YesNo(batch.Stalled));
        Line(sb, "Current member", batch.CurrentMemberIndex.HasValue ? Inv(batch.CurrentMemberIndex.Value + 1) : "none");

        return sb.ToString();
    }

    private static void Instrument(StringBuilder sb, BenchmarkModelBatchInstrumentDto instrument, string indent)
    {
        sb.AppendLine($"{indent}Candidate system prompt {instrument.CandidateSystemPromptSha256 ?? "(not recorded)"}");
        sb.AppendLine($"{indent}Tool guides {instrument.ToolGuidesSha256 ?? "(not recorded)"}");
        sb.AppendLine($"{indent}Knowledge base {instrument.KnowledgeBaseHeadSha ?? "(not recorded)"}");
        sb.AppendLine($"{indent}Wiki {instrument.WikiHeadSha ?? "(not recorded)"}");
        sb.AppendLine($"{indent}Source code {instrument.SourceCodeHeadSha ?? "(not recorded)"}");
        sb.AppendLine($"{indent}Harness {instrument.HarnessVersion ?? "(not recorded)"}, scoring method " +
                      (instrument.ScoringMethodVersion.HasValue ? Inv(instrument.ScoringMethodVersion.Value) : "(not recorded)"));
        if (instrument.CorpusIndexFingerprintsJson != null)
        {
            sb.AppendLine($"{indent}Index fingerprints {instrument.CorpusIndexFingerprintsJson}");
        }
    }

    private static void Section(StringBuilder sb, string name) => sb.AppendLine(name);

    private static void Line(StringBuilder sb, string name, string? value) => sb.AppendLine($"  {name}: {value ?? "none"}");

    private static string Ids(IReadOnlyCollection<long> ids)
        => ids.Count == 0 ? string.Empty : $" [configurations {string.Join(", ", ids.Select(Inv))}]";

    private static string Id(long? id) => id.HasValue ? "#" + Inv(id.Value) : "none";

    private static string Utc(DateTime? value)
        => value.HasValue ? value.Value.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + " UTC" : "not yet";

    private static string Stratum(DateTime? value)
        => value.HasValue ? $" ({BenchmarkModelBatchGuardrails.Stratum(value.Value)})" : string.Empty;

    private static string Duration(DateTime? start, DateTime? end)
        => start.HasValue && end.HasValue ? FormatDuration(end.Value - start.Value) : "n/a";

    private static string FormatDuration(TimeSpan span)
    {
        if (span < TimeSpan.Zero) span = TimeSpan.Zero;
        return span.TotalHours >= 1
            ? $"{Inv((int)span.TotalHours)} h {Inv(span.Minutes)} min"
            : $"{Inv(span.Minutes)} min {Inv(span.Seconds)} s";
    }

    private static string Usd(decimal? value)
        => value.HasValue ? "US$" + value.Value.ToString("0.0000", CultureInfo.InvariantCulture) : "unknown";

    private static string Ms(double? value)
        => value.HasValue ? value.Value.ToString("0", CultureInfo.InvariantCulture) + " ms" : "n/a";

    private static string Percent(double? value)
        => value.HasValue ? (value.Value * 100).ToString("0.0", CultureInfo.InvariantCulture) + " %" : "n/a";

    private static string Number(double? value)
        => value.HasValue ? value.Value.ToString("0.0", CultureInfo.InvariantCulture) : "n/a";

    private static string YesNo(bool value) => value ? "yes" : "no";

    private static string Inv(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Inv(long value) => value.ToString(CultureInfo.InvariantCulture);
}
