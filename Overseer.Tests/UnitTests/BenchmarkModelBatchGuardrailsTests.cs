namespace Overseer.Tests.UnitTests;

using System;
using System.Collections.Generic;
using System.Linq;
using MobileGnollHackLogger.Data;
using Overseer.Models;
using Overseer.Services;
using Overseer.Services.Benchmarking;
using Overseer.Services.ChatConsistency;
using Xunit;
using ParallelExecutionMode = MobileGnollHackLogger.Data.ParallelExecutionMode;

/// <summary>
/// The model batch guardrails (<c>model_batch_guardrails_v3.md</c> §§ 3.1–3.4): one positive and one
/// negative case per code, the acknowledgment key and its change with the affected set, the text
/// limits, and a clean roster that raises nothing.
/// </summary>
public class BenchmarkModelBatchGuardrailsTests
{
    // --- Fixture ------------------------------------------------------------------------------

    private static SystemAiApiConfiguration Config(
        long id,
        string provider,
        string modelId,
        string? thinking = "high",
        string? displayName = null,
        string? tier = null,
        ParallelExecutionMode parallel = ParallelExecutionMode.Enabled,
        int? maxOutput = null,
        string? baseUrl = null)
        => new()
        {
            Id = id,
            Provider = provider,
            ModelId = modelId,
            DisplayName = displayName ?? modelId,
            ThinkingLevel = thinking,
            ServiceTier = tier,
            ParallelExecutionMode = parallel,
            MaxOutputTokens = maxOutput,
            BaseUrl = baseUrl,
            IsEnabled = true,
            EncryptedApiKey = "encrypted",
            ModelRole = 4
        };

    // Two candidates of two providers, a two-provider panel of two others, a reference reader and a
    // claim verifier of further providers, so the default roster raises nothing.
    private static readonly SystemAiApiConfiguration CandidateA = Config(1, "Anthropic", "claude-opus-5", displayName: "Opus");
    private static readonly SystemAiApiConfiguration CandidateB = Config(2, "OpenAI", "gpt-5", displayName: "GPT");
    private static readonly SystemAiApiConfiguration Assessor = Config(10, "Google", "gemini-pro", "medium", "Gemini Pro");
    private static readonly SystemAiApiConfiguration CoAssessor = Config(11, "Mistral", "mistral-large", "medium", "Mistral Large");
    private static readonly SystemAiApiConfiguration Reader = Config(12, "DeepSeek", "deepseek-r", "medium", "DeepSeek");
    private static readonly SystemAiApiConfiguration Verifier = Config(13, "Cohere", "command", "medium", "Command");

    private static readonly DateTime Now = new(2026, 10, 7, 10, 0, 0, DateTimeKind.Utc); // a Wednesday, 08–12 UTC

    private static BenchmarkModelBatchProjectionResult Projection(
        int models = 2,
        int runsPerModel = 2,
        bool battery = false,
        int suiteCount = 1,
        bool allowCapWait = false,
        int maxRunsPerDay = 120,
        int runsInLast24Hours = 0,
        int maxRunsPerHour = 30,
        int runsInLastHour = 0,
        int maxBatteryMembers = 120,
        double meanWallMs = 600_000,
        decimal meanCost = 2m,
        BenchmarkSpendCheck? spend = null)
    {
        var suiteIds = Enumerable.Range(1, battery ? suiteCount : 1).Select(i => (long)(100 + i)).ToList();
        return BenchmarkModelBatchProjection.Compute(new BenchmarkModelBatchProjectionInput
        {
            CandidateIds = Enumerable.Range(1, models).Select(i => (long)i).ToList(),
            SuiteIds = suiteIds,
            IsBattery = battery,
            RunsPerModel = runsPerModel,
            AllowCapWait = allowCapWait,
            SuiteBases = suiteIds.ToDictionary(id => id, _ => new BenchmarkModelBatchRunBasis(5, meanWallMs, meanCost, meanCost / 2)),
            Limits = new BenchmarkRunLimits(maxRunsPerHour, maxRunsPerDay, runsInLastHour, runsInLast24Hours,
                Math.Max(0, maxRunsPerDay - runsInLast24Hours)),
            MaxBatteryMembers = maxBatteryMembers,
            Spend = spend ?? BenchmarkSpendCheck.Allow
        });
    }

    private static StartBenchmarkModelBatchRequest Request(
        IEnumerable<SystemAiApiConfiguration>? candidates = null,
        int runsPerModel = 2,
        BenchmarkModelBatchOrder order = BenchmarkModelBatchOrder.Randomized,
        bool allowCapWait = false,
        BenchmarkModelBatchTargetKind target = BenchmarkModelBatchTargetKind.Suite,
        Action<StartBenchmarkRunRequest>? run = null)
    {
        var request = new StartBenchmarkModelBatchRequest
        {
            TargetKind = target,
            SuiteId = 101,
            BatteryId = target == BenchmarkModelBatchTargetKind.Battery ? 7 : null,
            TestedModelConfigurationIds = (candidates ?? new[] { CandidateA, CandidateB }).Select(c => c.Id).ToList(),
            RunsPerModel = runsPerModel,
            Order = order,
            AllowCapWait = allowCapWait,
            Run = new StartBenchmarkRunRequest
            {
                AssessorModelConfigurationId = Assessor.Id,
                CoAssessorModelConfigurationId = CoAssessor.Id,
                SecondOpinionAssessorModelConfigurationId = Reader.Id,
                ClaimVerifierModelConfigurationId = Verifier.Id
            }
        };
        run?.Invoke(request.Run);
        return request;
    }

    /// <summary>The clean context, with the changes a test makes.</summary>
    private static BenchmarkModelBatchGuardContext Context(
        IReadOnlyList<SystemAiApiConfiguration>? candidates = null,
        StartBenchmarkModelBatchRequest? request = null,
        Func<BenchmarkModelBatchGuardContext, BenchmarkModelBatchGuardContext>? change = null)
    {
        var chosen = candidates ?? new[] { CandidateA, CandidateB };
        var ctx = new BenchmarkModelBatchGuardContext
        {
            Request = request ?? Request(chosen),
            Candidates = chosen,
            Assessor = Assessor,
            CoAssessor = CoAssessor,
            Reader = Reader,
            Verifier = Verifier,
            TargetName = "Suite A",
            Projection = Projection(models: chosen.Count),
            Options = new BenchmarkModelBatchOptions(),
            NowUtc = Now
        };
        return change == null ? ctx : change(ctx);
    }

    private static IReadOnlyList<BenchmarkModelBatchFindingDto> Evaluate(BenchmarkModelBatchGuardContext ctx)
        => BenchmarkModelBatchGuardrails.Evaluate(ctx);

    private static HashSet<string> Codes(BenchmarkModelBatchGuardContext ctx)
        => Evaluate(ctx).Select(f => f.Code).ToHashSet();

    private static BenchmarkModelBatchFindingDto Single(BenchmarkModelBatchGuardContext ctx, string code)
        => Assert.Single(Evaluate(ctx), f => f.Code == code);

    // --- The clean roster and the general rules -----------------------------------------------

    [Fact]
    public void TheDefaultRoster_RaisesOnlyTheComparisonDocumentsAdvice()
    {
        Assert.Equal(new[] { "MB-A13" }, Evaluate(Context()).Select(f => f.Code).ToArray());
    }

    [Fact]
    public void EveryLine_KeepsTheTextLimits_AndBlockersComeFirst()
    {
        var many = Enumerable.Range(1, 14)
            .Select(i => Config(i, i % 2 == 0 ? "Anthropic" : "OpenAI", $"model-with-a-rather-long-identifier-{i}",
                tier: i % 3 == 0 ? "priority" : null, displayName: $"A Configuration With A Very Long Display Name {i}"))
            .ToList();
        var ctx = Context(many, Request(many, runsPerModel: 1, order: BenchmarkModelBatchOrder.AsListed), c => c with
        {
            CoAssessor = null,
            Verifier = null,
            Assessor = Config(10, "Anthropic", "assessor-model", "max", "Assessor Model")
        });

        var findings = Evaluate(ctx);

        Assert.NotEmpty(findings);
        Assert.All(findings, f => Assert.True(f.Title.Length <= BenchmarkModelBatchGuardrails.TitleMaxLength, f.Title));
        Assert.All(findings.Where(f => f.Code is not ("MB-B05" or "MB-B06" or "MB-B07" or "MB-B08" or "MB-A09" or "MB-A13")),
            f => Assert.True(f.Detail.Length <= BenchmarkModelBatchGuardrails.DetailMaxLength, f.Detail));

        var ranks = findings.Select(f => f.Severity switch { "Blocker" => 0, "Warning" => 1, _ => 2 }).ToList();
        Assert.Equal(ranks.OrderBy(r => r).ToList(), ranks);
    }

    private static readonly Dictionary<string, Func<BenchmarkModelBatchGuardContext>> NoteContexts = new()
    {
        ["many"] = () =>
        {
            var many = Enumerable.Range(1, 14)
                .Select(i => Config(i, i % 2 == 0 ? "Anthropic" : "OpenAI", $"model-with-a-rather-long-identifier-{i % 4}",
                    tier: i % 3 == 0 ? "priority" : null, displayName: $"A Configuration With A Very Long Display Name {i}",
                    maxOutput: i % 5 == 0 ? 123456 : null))
                .ToList();
            return Context(many, Request(many, runsPerModel: 1), c => c with
            {
                CoAssessor = null,
                Assessor = Config(10, "Anthropic", "assessor-model", "medium", "Assessor Model"),
                ReportWriter = Config(20, "OpenAI", "writer-model", displayName: "Writer Model With A Long Name")
            });
        },
        ["graders"] = () => Context(change: c => c with
        {
            Assessor = Config(10, "Anthropic", "claude-opus-5", "medium"),
            ReportWriter = Config(20, "OpenAI", "gpt-5"),
            Reader = Config(12, "Anthropic", "claude-opus-5"),
            Verifier = Config(13, "Anthropic", "claude-opus-5")
        }),
        ["refused"] = () => Context(change: c => c with
        {
            CandidateRefusals = new Dictionary<long, string> { [1] = "no.", [2] = "no." },
            MissingCandidateIds = new long[] { 44 }
        }),
        ["endpoint"] = () => Context(new[]
        {
            CandidateA,
            Config(2, "OpenAI", "gpt-5", displayName: "GPT", baseUrl: "https://example.test/v1"),
            Config(3, "Mistral", "mistral-medium", displayName: "Mistral", baseUrl: "https://example.test/v2")
        })
    };

    [Theory]
    [InlineData("many")]
    [InlineData("graders")]
    [InlineData("refused")]
    [InlineData("endpoint")]
    public void EveryModelNote_KeepsTheLengthLimit_AndIsOnAnAffectedModel(string name)
    {
        var findings = Evaluate(NoteContexts[name]());

        Assert.Contains(findings, f => f.ModelNotes.Count > 0);
        Assert.All(findings, f => Assert.All(f.ModelNotes, n =>
        {
            Assert.True(n.Text.Length <= BenchmarkModelBatchGuardrails.NoteMaxLength, n.Text);
            Assert.Contains(n.ModelConfigurationId, f.ModelConfigurationIds);
        }));
    }

    [Fact]
    public void AcknowledgmentKey_IsTheCodeAndTheSortedAffectedIds_AndChangesWithThem()
    {
        Assert.Equal("MB-W10:2,5", BenchmarkModelBatchGuardrails.AcknowledgmentKey("MB-W10", new long[] { 5, 2, 5 }));
        Assert.Equal("MB-W03", BenchmarkModelBatchGuardrails.AcknowledgmentKey("MB-W03", Array.Empty<long>()));
        Assert.Equal("MB-W05/ServiceTier:1,2", BenchmarkModelBatchGuardrails.AcknowledgmentKey("MB-W05", new long[] { 2, 1 }, "ServiceTier"));

        var assessor = Config(10, "Anthropic", "claude-sonnet", "medium", "Sonnet");
        var third = Config(3, "Anthropic", "claude-haiku", displayName: "Haiku");
        var before = Single(Context(change: c => c with { CoAssessor = null, Assessor = assessor }), "MB-W10");
        var withThird = new[] { CandidateA, CandidateB, third };
        var after = Single(Context(withThird, change: c => c with { CoAssessor = null, Assessor = assessor }), "MB-W10");

        Assert.Equal("MB-W10:1", before.AcknowledgmentKey);
        Assert.Equal("MB-W10:1,3", after.AcknowledgmentKey);
        Assert.NotEqual(before.AcknowledgmentKey, after.AcknowledgmentKey);
    }

    [Fact]
    public void OnlyWarnings_CarryAnAcknowledgmentKey()
    {
        var findings = Evaluate(Context(new[] { CandidateA }, Request(new[] { CandidateA }, runsPerModel: 1),
            c => c with { Verifier = null }));

        Assert.All(findings.Where(f => f.Severity != BenchmarkModelBatchSeverity.Warning), f => Assert.Null(f.AcknowledgmentKey));
        Assert.All(findings.Where(f => f.Severity == BenchmarkModelBatchSeverity.Warning), f => Assert.NotNull(f.AcknowledgmentKey));
    }

    [Fact]
    public void UnacknowledgedWarnings_AreTheWarningsWhoseKeyWasNotSent()
    {
        var findings = Evaluate(Context(change: c => c with { Verifier = null }));
        var w03 = Assert.Single(findings, f => f.Code == "MB-W03");

        Assert.Single(BenchmarkModelBatchGuardrails.UnacknowledgedWarnings(findings, Array.Empty<string>()));
        Assert.Empty(BenchmarkModelBatchGuardrails.UnacknowledgedWarnings(findings, new[] { w03.AcknowledgmentKey! }));
    }

    // --- 3.1 Blockers ----------------------------------------------------------------------------

    [Fact]
    public void B01_TooFewModels()
    {
        var one = Single(Context(new[] { CandidateA }), "MB-B01");
        Assert.Equal(BenchmarkModelBatchSeverity.Blocker, one.Severity);
        Assert.Equal("models", one.Field);
        Assert.Equal("Choose at least two models", one.Title);
        Assert.DoesNotContain("MB-B01", Codes(Context()));
    }

    [Fact]
    public void B02_TooManyModels()
    {
        var thirteen = Enumerable.Range(1, 13).Select(i => Config(i, i % 2 == 0 ? "Anthropic" : "OpenAI", $"m{i}")).ToList();
        var twelve = thirteen.Take(12).ToList();

        var finding = Single(Context(thirteen), "MB-B02");
        Assert.Equal("At most 12 models in one batch", finding.Title);
        Assert.DoesNotContain("MB-B02", Codes(Context(twelve)));
    }

    [Fact]
    public void B03_GraderIsCandidate()
    {
        var finding = Single(Context(change: c => c with { Assessor = Config(10, "Anthropic", "claude-opus-5", "medium") }), "MB-B03");
        Assert.Equal("assessor", finding.Field);
        Assert.Equal("Opus cannot grade itself", finding.Title);
        Assert.Contains("Choose another assessor", finding.Detail);
        Assert.Equal(new long[] { 1 }, finding.ModelConfigurationIds);
        var note = Assert.Single(finding.ModelNotes);
        Assert.Equal(1, note.ModelConfigurationId);
        Assert.Equal("Is the assessor: cannot grade itself", note.Text);

        var coAssessor = Single(Context(change: c => c with { CoAssessor = Config(11, "OpenAI", "gpt-5", "medium") }), "MB-B03");
        Assert.Equal("coAssessor", coAssessor.Field);
        Assert.Equal("Is the co-assessor: cannot grade itself", Assert.Single(coAssessor.ModelNotes).Text);

        Assert.DoesNotContain("MB-B03", Codes(Context()));
    }

    [Fact]
    public void B04_ReportWriterIsCandidate()
    {
        var finding = Single(Context(change: c => c with { ReportWriter = Config(20, "OpenAI", "gpt-5") }), "MB-B04");
        Assert.Equal("reportWriter", finding.Field);
        Assert.Equal("GPT cannot write its own report", finding.Title);
        var note = Assert.Single(finding.ModelNotes);
        Assert.Equal(2, note.ModelConfigurationId);
        Assert.Equal("Is the report writer: cannot report on itself", note.Text);

        Assert.DoesNotContain("MB-B04", Codes(Context(change: c => c with { ReportWriter = Config(20, "xAI", "grok") })));
    }

    [Fact]
    public void B05_PanelInvalid()
    {
        var same = Single(Context(change: c => c with { CoAssessor = Assessor }), "MB-B05");
        Assert.Equal("The co-assessor must be a different configuration from the assessor.", same.Detail);

        var shared = Single(Context(change: c => c with { CoAssessor = Config(11, "Google", "gemini-flash", "medium") }), "MB-B05");
        Assert.Contains("both belong to Google", shared.Detail);

        Assert.DoesNotContain("MB-B05", Codes(Context()));
    }

    [Fact]
    public void B06_MemberRefused()
    {
        var refused = Single(Context(change: c => c with
        {
            CandidateRefusals = new Dictionary<long, string> { [2] = "its custom endpoint is not allowed by the endpoint policy." }
        }), "MB-B06");
        Assert.Equal("GPT: its custom endpoint is not allowed by the endpoint policy.", refused.Detail);
        Assert.Equal(new long[] { 2 }, refused.ModelConfigurationIds);
        var note = Assert.Single(refused.ModelNotes);
        Assert.Equal(2, note.ModelConfigurationId);
        Assert.Equal("Refused: see Batch Readiness", note.Text);

        // A missing configuration has no card to carry a note.
        var missing = Single(Context(change: c => c with { MissingCandidateIds = new long[] { 44 } }), "MB-B06");
        Assert.Contains("#44", missing.Detail);
        Assert.Empty(missing.ModelNotes);

        Assert.DoesNotContain("MB-B06", Codes(Context()));
    }

    [Fact]
    public void B07_TargetNotReady()
    {
        var finding = Single(Context(change: c => c with { TargetRefusals = new[] { "Battery 'Pair' is archived." } }), "MB-B07");
        Assert.Equal("target", finding.Field);
        Assert.Equal("Battery 'Pair' is archived.", finding.Detail);
        Assert.DoesNotContain("MB-B07", Codes(Context()));
    }

    [Fact]
    public void B08_CapExceeded_DailyCapWithoutWait_ButNotWithIt()
    {
        var over = Context(change: c => c with { Projection = Projection(models: 2, runsPerModel: 61, maxRunsPerDay: 120) });
        var finding = Single(over, "MB-B08");
        Assert.Equal("122 runs exceed the daily cap of 120", finding.Title);
        Assert.Equal("capWait", finding.Field);

        var request = Request(runsPerModel: 61, allowCapWait: true);
        var waiting = Context(request: request, change: c => c with
        {
            Projection = Projection(models: 2, runsPerModel: 61, maxRunsPerDay: 120, allowCapWait: true)
        });
        Assert.DoesNotContain("MB-B08", Codes(waiting));
    }

    [Fact]
    public void B08_CapExceeded_MemberPlanAndSpendVariants()
    {
        var battery = Request(target: BenchmarkModelBatchTargetKind.Battery, runsPerModel: 61);
        var plan = Single(Context(request: battery, change: c => c with
        {
            Projection = Projection(models: 2, runsPerModel: 61, battery: true, suiteCount: 2, maxBatteryMembers: 120, maxRunsPerDay: 1000)
        }), "MB-B08");
        Assert.Equal("runsPerModel", plan.Field);
        Assert.Contains("battery limit of 120", plan.Title);

        var spend = Single(Context(change: c => c with
        {
            Projection = Projection(spend: BenchmarkSpendCheck.Deny(BenchmarkSpendDenialKind.Other, "Spending is suspended."))
        }), "MB-B08");
        Assert.Equal("Spending is suspended.", spend.Detail);
    }

    [Fact]
    public void B09_RunActive()
    {
        var finding = Single(Context(change: c => c with { ActiveDescription = "Run #4 is running." }), "MB-B09");
        Assert.Equal("A benchmark is already running", finding.Title);
        Assert.Equal("Wait for it to finish or cancel it.", finding.Detail);
        Assert.DoesNotContain("MB-B09", Codes(Context()));
    }

    // --- 3.2 Warnings ----------------------------------------------------------------------------

    [Fact]
    public void W01_MixedFamiliesSingleAssessor()
    {
        var finding = Single(Context(change: c => c with { CoAssessor = null }), "MB-W01");
        Assert.Equal("Use a two-family panel for this batch", finding.Title);
        Assert.Equal(BenchmarkModelBatchSeverity.Warning, finding.Severity);

        var oneFamily = new[] { CandidateA, Config(3, "Anthropic", "claude-sonnet-5") };
        Assert.DoesNotContain("MB-W01", Codes(Context(oneFamily, change: c => c with { CoAssessor = null })));
    }

    [Fact]
    public void W02_AnchorIsCandidate()
    {
        var finding = Single(Context(change: c => c with { Verifier = Config(13, "Anthropic", "claude-opus-5") }), "MB-W02");
        Assert.Equal("Opus checks its own answers", finding.Title);
        Assert.Contains("claim verifier", finding.Detail);
        var note = Assert.Single(finding.ModelNotes);
        Assert.Equal(1, note.ModelConfigurationId);
        Assert.Equal("Also the claim verifier", note.Text);

        var both = Single(Context(change: c => c with
        {
            Reader = Config(12, "Anthropic", "claude-opus-5"),
            Verifier = Config(13, "Anthropic", "claude-opus-5")
        }), "MB-W02");
        Assert.Equal("Also the reference reader and claim verifier", Assert.Single(both.ModelNotes).Text);

        Assert.DoesNotContain("MB-W02", Codes(Context()));
    }

    [Fact]
    public void W03_NoClaimVerifier()
    {
        Assert.Equal("No claim verifier", Single(Context(change: c => c with { Verifier = null }), "MB-W03").Title);
        Assert.DoesNotContain("MB-W03", Codes(Context()));
    }

    [Fact]
    public void W04_GraderEffortTooHigh()
    {
        var finding = Single(Context(change: c => c with { Assessor = Config(10, "Google", "gemini-pro", "max", "Gemini Pro") }), "MB-W04");
        Assert.Equal("Assessor at max effort", finding.Title);
        Assert.Equal(new long[] { 10 }, finding.ModelConfigurationIds);

        Assert.DoesNotContain("MB-W04", Codes(Context(change: c => c with { Assessor = Config(10, "Google", "gemini-pro", "high") })));
    }

    [Fact]
    public void W05_CandidateSettingsDiffer()
    {
        // Two models, two values: a tie, so neither is the norm and both are named.
        var tiered = new[] { CandidateA, Config(2, "OpenAI", "gpt-5", displayName: "GPT", tier: "priority") };
        var tie = Single(Context(tiered), "MB-W05");
        Assert.Equal("Service tier differs between models", tie.Title);
        Assert.StartsWith("Service tier differs:", tie.Detail);
        Assert.Equal(new long[] { 1, 2 }, tie.ModelConfigurationIds);
        Assert.Equal("MB-W05/ServiceTier:1,2", tie.AcknowledgmentKey);
        Assert.Equal("Service tier priority; no majority", Assert.Single(tie.ModelNotes, n => n.ModelConfigurationId == 2).Text);

        // Two against one: only the deviating model is affected.
        var third = Config(3, "Mistral", "mistral-medium", displayName: "Mistral");
        var finding = Single(Context(new[] { CandidateA, tiered[1], third }), "MB-W05");
        Assert.Equal("Service tier differs between models", finding.Title);
        Assert.Equal(new long[] { 2 }, finding.ModelConfigurationIds);
        Assert.Equal("MB-W05/ServiceTier:2", finding.AcknowledgmentKey);
        Assert.EndsWith("; the other 2 use default.", finding.Detail);
        var note = Assert.Single(finding.ModelNotes);
        Assert.Equal(2, note.ModelConfigurationId);
        Assert.Equal("Service tier priority; the others use default", note.Text);

        Assert.DoesNotContain("MB-W05", Codes(Context()));
    }

    [Fact]
    public void W05_TieNamesEveryModel()
    {
        var models = new[]
        {
            CandidateA,
            CandidateB,
            Config(3, "Mistral", "mistral-medium", displayName: "Mistral", maxOutput: 8192),
            Config(4, "xAI", "grok", displayName: "Grok", maxOutput: 8192)
        };

        var finding = Single(Context(models), "MB-W05");

        Assert.Equal("Max output tokens differs between models", finding.Title);
        Assert.Equal(new long[] { 1, 2, 3, 4 }, finding.ModelConfigurationIds);
        Assert.Equal(new long[] { 1, 2, 3, 4 }, finding.ModelNotes.Select(n => n.ModelConfigurationId));
        Assert.Equal("Max output tokens 8192; no majority", finding.ModelNotes[2].Text);
        Assert.Equal("Max output tokens default; no majority", finding.ModelNotes[0].Text);
    }

    [Fact]
    public void W05_EndpointNote_SaysCustomEndpoint_AndTheDetailKeepsTheFingerprint()
    {
        var custom = Config(3, "Mistral", "mistral-medium", displayName: "Mistral", baseUrl: "https://example.test/v1");
        var finding = Single(Context(new[] { CandidateA, CandidateB, custom }), "MB-W05");

        Assert.Equal("Endpoint differs between models", finding.Title);
        Assert.Equal(new long[] { 3 }, finding.ModelConfigurationIds);
        Assert.Contains("Mistral (custom-", finding.Detail);
        Assert.EndsWith("; the other 2 use official.", finding.Detail);
        Assert.Equal("Endpoint custom endpoint; the others use official", Assert.Single(finding.ModelNotes).Text);
    }

    [Fact]
    public void W06_DuplicateConfiguration()
    {
        var twin = Config(3, "Anthropic", "claude-opus-5", displayName: "Opus copy");
        var finding = Single(Context(new[] { CandidateA, CandidateB, twin }), "MB-W06");
        Assert.Equal("Opus is selected twice", finding.Title);
        Assert.Equal(new long[] { 1, 3 }, finding.ModelConfigurationIds);
        Assert.Equal("Same configuration as Opus copy", Assert.Single(finding.ModelNotes, n => n.ModelConfigurationId == 1).Text);
        Assert.Equal("Same configuration as Opus", Assert.Single(finding.ModelNotes, n => n.ModelConfigurationId == 3).Text);

        var otherLevel = Config(3, "Anthropic", "claude-opus-5", "medium", "Opus medium");
        Assert.DoesNotContain("MB-W06", Codes(Context(new[] { CandidateA, CandidateB, otherLevel })));
    }

    [Fact]
    public void W07_DetailedStyle()
    {
        var detailed = Request(run: r => r.VerboseMode = true);
        Assert.Equal("responseStyle", Single(Context(request: detailed), "MB-W07").Field);
        Assert.DoesNotContain("MB-W07", Codes(Context(request: Request(run: r => r.VerboseMode = false))));
    }

    [Fact]
    public void W08_ReportWriterSharesFamily()
    {
        var finding = Single(Context(change: c => c with { ReportWriter = Config(20, "OpenAI", "gpt-5-mini", displayName: "GPT Mini") }), "MB-W08");
        Assert.Equal("GPT Mini reports on its own provider", finding.Title);
        Assert.Equal(new long[] { 2 }, finding.ModelConfigurationIds);
        var note = Assert.Single(finding.ModelNotes);
        Assert.Equal(2, note.ModelConfigurationId);
        Assert.Equal("Same provider as the report writer", note.Text);

        Assert.DoesNotContain("MB-W08", Codes(Context(change: c => c with { ReportWriter = Config(20, "xAI", "grok") })));
    }

    [Fact]
    public void W09_LargeBatch()
    {
        var expensive = Context(change: c => c with { Projection = Projection(meanCost: 30m) });
        Assert.StartsWith("Large batch: about US$", Single(expensive, "MB-W09").Title);

        var long12h = Context(change: c => c with { Projection = Projection(meanWallMs: 4 * 3_600_000d) });
        Assert.Contains("MB-W09", Codes(long12h));

        Assert.DoesNotContain("MB-W09", Codes(Context()));
    }

    [Fact]
    public void W10_SameProviderAssessor()
    {
        var anthropicAssessor = Config(10, "Anthropic", "claude-sonnet", "medium", "Sonnet");
        var finding = Single(Context(change: c => c with { CoAssessor = null, Assessor = anthropicAssessor }), "MB-W10");
        Assert.Equal("Sonnet grades its own provider's models", finding.Title);
        Assert.Equal("Opus are graded by a same-provider assessor.", finding.Detail);
        var note = Assert.Single(finding.ModelNotes);
        Assert.Equal(1, note.ModelConfigurationId);
        Assert.Equal("Same provider as the assessor", note.Text);

        // A panel is balanced by construction: no same-provider gate.
        Assert.DoesNotContain("MB-W10", Codes(Context(change: c => c with { Assessor = anthropicAssessor })));
    }

    [Fact]
    public void W11_ExceedsDailyHeadroom_BetweenTheHeadroomAndTheCap()
    {
        // L = 4 against 4 left: at the headroom, nothing.
        var atHeadroom = Context(change: c => c with { Projection = Projection(runsInLast24Hours: 116) });
        Assert.DoesNotContain("MB-W11", Codes(atHeadroom));

        // L = 120 = the cap, with 100 left: a warning, not a blocker.
        var atCap = Context(request: Request(runsPerModel: 60), change: c => c with
        {
            Projection = Projection(runsPerModel: 60, runsInLast24Hours: 20)
        });
        var finding = Single(atCap, "MB-W11");
        Assert.Equal("120 runs, 100 left in the 24-hour window", finding.Title);
        Assert.DoesNotContain("MB-B08", Codes(atCap));
    }

    [Fact]
    public void W12_HourlyCapRisk()
    {
        // A one-minute mean run launches 60 per hour against 30 allowed.
        var fast = Context(request: Request(runsPerModel: 20), change: c => c with
        {
            Projection = Projection(runsPerModel: 20, meanWallMs: 60_000)
        });
        Assert.Equal("Runs may outpace the hourly cap of 30", Single(fast, "MB-W12").Title);

        Assert.DoesNotContain("MB-W12", Codes(Context()));
    }

    // --- 3.3 Advice ------------------------------------------------------------------------------

    [Fact]
    public void A01_SingleRunPerModel()
    {
        var finding = Single(Context(request: Request(runsPerModel: 1)), "MB-A01");
        Assert.Equal(BenchmarkModelBatchSeverity.Advice, finding.Severity);
        Assert.Null(finding.AcknowledgmentKey);
        Assert.DoesNotContain("MB-A01", Codes(Context()));
    }

    [Fact]
    public void A02_UniformFamilyBias()
    {
        var anthropic = new[] { CandidateA, Config(3, "Anthropic", "claude-sonnet-5") };
        var assessor = Config(10, "Anthropic", "claude-haiku", "medium");
        Assert.Contains("MB-A02", Codes(Context(anthropic, change: c => c with { CoAssessor = null, Assessor = assessor })));
        Assert.DoesNotContain("MB-A02", Codes(Context(change: c => c with { CoAssessor = null, Assessor = assessor })));
    }

    [Fact]
    public void A03_AnchorSharesFamily()
    {
        var finding = Single(Context(change: c => c with { Verifier = Config(13, "Anthropic", "claude-haiku", "medium") }), "MB-A03");
        Assert.Equal("Claim verifier shares Anthropic with Opus", finding.Title);

        var readerWithPanel = Single(Context(change: c => c with { Reader = Config(12, "Google", "gemini-flash", "medium") }), "MB-A03");
        Assert.Contains("Gemini Pro", readerWithPanel.Title);

        Assert.DoesNotContain("MB-A03", Codes(Context()));
    }

    [Fact]
    public void A04_NoReferenceReader()
    {
        Assert.Contains("MB-A04", Codes(Context(change: c => c with { Reader = null })));
        Assert.DoesNotContain("MB-A04", Codes(Context()));
    }

    [Fact]
    public void A05_CoAssessorNotPeer()
    {
        Assert.Contains("MB-A05", Codes(Context(change: c => c with { CoAssessor = Config(11, "Mistral", "mistral-large", "high") })));
        Assert.DoesNotContain("MB-A05", Codes(Context()));
    }

    [Fact]
    public void A06_SourceReferencesAllowed()
    {
        Assert.Contains("MB-A06", Codes(Context(request: Request(run: r => r.AllowSourceCodeReferences = true))));
        Assert.DoesNotContain("MB-A06", Codes(Context(request: Request(run: r => r.AllowSourceCodeReferences = false))));
    }

    [Fact]
    public void A07_PricingIncomplete()
    {
        var finding = Single(Context(change: c => c with { UnpricedCandidateIds = new long[] { 2 } }), "MB-A07");
        Assert.Equal("No price for GPT", finding.Title);
        Assert.DoesNotContain("MB-A07", Codes(Context()));
    }

    [Fact]
    public void A08_ProfileFit()
    {
        var finding = Single(Context(new[] { CandidateA, Config(2, "OpenAI", "gpt-5", "low", "GPT") },
            change: c => c with { SpeedTargetMs = 15000 }), "MB-A08");
        Assert.Equal(new long[] { 1 }, finding.ModelConfigurationIds);

        Assert.DoesNotContain("MB-A08", Codes(Context(change: c => c with { SpeedTargetMs = 30000 })));
    }

    [Fact]
    public void A09_NoBaseline()
    {
        var recommended = new Dictionary<string, IReadOnlyList<RecommendedModel>>(StringComparer.OrdinalIgnoreCase)
        {
            ["Anthropic"] = new List<RecommendedModel> { new() { Model = "claude-opus-6", ThinkingLevel = "high" } }
        };
        var finding = Single(Context(change: c => c with { Recommended = recommended }), "MB-A09");
        Assert.Contains("claude-opus-6 (high)", finding.Detail);

        var included = new Dictionary<string, IReadOnlyList<RecommendedModel>>(StringComparer.OrdinalIgnoreCase)
        {
            ["Anthropic"] = new List<RecommendedModel> { new() { Model = "claude-opus-5", ThinkingLevel = "high" } }
        };
        Assert.DoesNotContain("MB-A09", Codes(Context(change: c => c with { Recommended = included })));
    }

    private static string AxisKeyOf(SystemAiApiConfiguration configuration)
        => ChatConsistencyComparability.ModelAxisKey(SystemAiConfigurationSnapshotStore.FromConfiguration(configuration));

    private static readonly Dictionary<string, IReadOnlyList<RecommendedModel>> OtherRecommended =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["Anthropic"] = new List<RecommendedModel> { new() { Model = "claude-opus-7", ThinkingLevel = "high" } }
        };

    [Fact]
    public void A09_NoBaseline_NamesTheBaselineModel_WhenOneIsSet()
    {
        var baseline = Config(30, "Anthropic", "claude-opus-6", displayName: "Opus 6");

        var finding = Single(Context(change: c => c with
        {
            BaselineModelAxisKey = AxisKeyOf(baseline),
            BaselineModelName = "Opus 6",
            Recommended = OtherRecommended
        }), "MB-A09");

        Assert.Equal(BenchmarkModelBatchSeverity.Advice, finding.Severity);
        Assert.Equal("models", finding.Field);
        Assert.Equal("No baseline model in the batch", finding.Title);
        Assert.Equal(
            "Add Opus 6 so the others are judged against it under the same graders. " +
            "Otherwise Open in Model Comparison adds its latest comparable result, if there is one.",
            finding.Detail);
    }

    [Fact]
    public void A09_NoBaseline_IsSilent_WhenACandidateIsTheBaselineModel()
    {
        // A rename keeps the model-axis key; the recommended models are not consulted.
        var renamed = Config(30, "Anthropic", "claude-opus-5", displayName: "Opus renamed");

        Assert.DoesNotContain("MB-A09", Codes(Context(change: c => c with
        {
            BaselineModelAxisKey = AxisKeyOf(renamed),
            BaselineModelName = "Opus renamed",
            Recommended = OtherRecommended
        })));
    }

    [Fact]
    public void A09_NoBaseline_KeepsTheRecommendedModelsWording_WithoutABaselineModel()
    {
        var finding = Single(Context(change: c => c with { Recommended = OtherRecommended, BaselineModelAxisKey = null }), "MB-A09");

        Assert.Equal("No current production model in the batch", finding.Title);
        Assert.Equal("Add claude-opus-7 (high) as the baseline the others are judged against.", finding.Detail);
    }

    [Fact]
    public void A10_RosterDiffersFromRecent()
    {
        var same = new BenchmarkModelBatchRoster(Assessor.Id, CoAssessor.Id, Reader.Id, Verifier.Id);
        var other = same with { AssessorId = 99 };
        Assert.Contains("MB-A10", Codes(Context(change: c => c with { RecentRoster = other })));
        Assert.DoesNotContain("MB-A10", Codes(Context(change: c => c with { RecentRoster = same })));
    }

    [Fact]
    public void A11_ReportWriterInBatch()
    {
        var finding = Single(Context(change: c => c with { ReportWriter = Config(20, "xAI", "grok") }), "MB-A11");
        Assert.Equal($"{4 * BenchmarkRunReportDocumentService.Audiences.Count} AI documents will be written", finding.Title);
        Assert.Equal(
            "Per run: an Executive Summary and a Root Cause Analysis. For whole-batch documents, follow the steps the batch dialog shows.",
            finding.Detail);
        Assert.DoesNotContain("MB-A11", Codes(Context()));
    }

    [Fact]
    public void A12_AnchoredSecondReader()
    {
        Assert.Contains("MB-A12", Codes(Context(change: c => c with { CoAssessor = null, SecondOpinionBlind = false })));
        // A panel's reference reader is blinded by the service whatever the profile says.
        Assert.DoesNotContain("MB-A12", Codes(Context(change: c => c with { SecondOpinionBlind = false })));
    }

    [Fact]
    public void A13_ComparisonDocumentsAreManual_ForTwoOrMoreModels()
    {
        var finding = Single(Context(), "MB-A13");
        Assert.Equal("ComparisonDocumentsAreManual", finding.Name);
        Assert.Equal(BenchmarkModelBatchSeverity.Advice, finding.Severity);
        Assert.Null(finding.Field);
        Assert.Null(finding.AcknowledgmentKey);
        Assert.Equal("Comparison documents are written by you, after the batch", finding.Title);
        Assert.Equal(
            "When the batch finishes, its dialog shows the steps: Open in Model Comparison, then step 3, Write, " +
            "with the wizard kept open so the charts are drawn.",
            finding.Detail);

        Assert.DoesNotContain("MB-A13", Codes(Context(new[] { CandidateA })));
    }

    // --- 3.4 Time and order ----------------------------------------------------------------------

    [Fact]
    public void T01_OrderAsListed()
    {
        Assert.Contains("MB-T01", Codes(Context(request: Request(order: BenchmarkModelBatchOrder.AsListed))));
        Assert.DoesNotContain("MB-T01", Codes(Context()));
    }

    [Fact]
    public void T02_LongWallTime()
    {
        // 2 models × 2 runs × 1.5 h = 6 h: above 4 h, below the 12 h of MB-W09.
        var finding = Single(Context(change: c => c with { Projection = Projection(meanWallMs: 1.5 * 3_600_000) }), "MB-T02");
        Assert.Equal(BenchmarkModelBatchSeverity.Advice, finding.Severity);
        Assert.DoesNotContain("MB-T02", Codes(Context()));
    }

    [Fact]
    public void T03_StratumDiffersFromRecent()
    {
        var saturdayEvening = new DateTime(2026, 10, 3, 21, 0, 0, DateTimeKind.Utc);
        var finding = Single(Context(change: c => c with
        {
            LastRunStartedAtUtc = new Dictionary<long, DateTime> { [1] = saturdayEvening }
        }), "MB-T03");
        Assert.Equal("Opus last ran on a weekend, 20–24 UTC", finding.Title);

        var sameBlock = Now.AddDays(-7).AddMinutes(30);
        Assert.DoesNotContain("MB-T03", Codes(Context(change: c => c with
        {
            LastRunStartedAtUtc = new Dictionary<long, DateTime> { [1] = sameBlock }
        })));
    }
}
