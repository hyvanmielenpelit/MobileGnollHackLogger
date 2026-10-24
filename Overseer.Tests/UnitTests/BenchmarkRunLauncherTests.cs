namespace Overseer.Tests.UnitTests;

using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MobileGnollHackLogger.Data;
using Overseer.Models;
using Overseer.Services;
using Overseer.Services.Benchmarking;
using Overseer.Tests.Helpers;
using Xunit;

/// <summary>
/// The candidate prompt switches a launch request carries, as the run records them in
/// <see cref="BenchmarkCandidatePromptOptions"/>: the response style and whether source code
/// references are allowed, which a user's default disallows.
/// </summary>
public class BenchmarkRunLauncherTests
{
    private static BenchmarkCandidatePromptOptions RecordedOptions(StartBenchmarkRunRequest request, bool hasGameSnapshot = false)
    {
        var switches = BenchmarkRunLauncher.ResolvePromptSwitches(request);
        return BenchmarkService.CandidatePromptOptionsFor(
            switches.VerboseMode, switches.AllowSourceCodeReferences, hasGameSnapshot, switches.InformationCondition);
    }

    [Fact]
    public void ANullConditionAndVariant_ResolveToProduction()
    {
        var switches = BenchmarkRunLauncher.ResolvePromptSwitches(new StartBenchmarkRunRequest());

        Assert.Equal(BenchmarkInformationCondition.Production, switches.InformationCondition);
        Assert.Null(switches.InstructionVariant);
        Assert.DoesNotContain("informationCondition", RecordedOptions(new StartBenchmarkRunRequest()).ToCanonicalJson());
    }

    [Fact]
    public void TheConditionAndChanges_AreResolvedAsGiven_AndTheSpecNormalized()
    {
        var request = new StartBenchmarkRunRequest
        {
            InformationCondition = BenchmarkInformationConditions.ClosedBookWire,
            InstructionVariant = "  race-role-tool-v1 stats-referenced-by-v1 "
        };

        var switches = BenchmarkRunLauncher.ResolvePromptSwitches(request);

        Assert.Equal(BenchmarkInformationCondition.ClosedBook, switches.InformationCondition);
        Assert.Equal("race-role-tool-v1+stats-referenced-by-v1", switches.InstructionVariant);

        var options = RecordedOptions(request);
        Assert.False(options.EnableToolUse);
        Assert.Equal(BenchmarkInformationConditions.ClosedBookWire, options.InformationCondition);

        Assert.Null(BenchmarkRunLauncher.ResolvePromptSwitches(new StartBenchmarkRunRequest { InstructionVariant = " " }).InstructionVariant);
    }

    [Fact]
    public void ANullAllowSourceCodeReferences_IsRecordedAsDisallowed()
    {
        var request = new StartBenchmarkRunRequest { SuiteId = 1, TestedModelConfigurationId = 1, AssessorModelConfigurationId = 2 };

        Assert.Null(request.AllowSourceCodeReferences);
        var options = RecordedOptions(request);

        Assert.False(options.AllowSourceCodeReferences);
        Assert.False(options.VerboseMode);
        Assert.Contains("\"allowSourceCodeReferences\":false", options.ToCanonicalJson());
        Assert.False(BenchmarkCandidatePromptOptions.FromJson(options.ToCanonicalJson()).AllowSourceCodeReferences);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void AnExplicitAllowSourceCodeReferences_IsRecordedAsGiven(bool requested, bool expected)
    {
        var request = new StartBenchmarkRunRequest { AllowSourceCodeReferences = requested, VerboseMode = true };

        var options = RecordedOptions(request, hasGameSnapshot: true);

        Assert.Equal(expected, options.AllowSourceCodeReferences);
        Assert.True(options.VerboseMode);
        Assert.True(options.HasGameSnapshot);
        Assert.Equal(expected, BenchmarkCandidatePromptOptions.FromJson(options.ToCanonicalJson()).AllowSourceCodeReferences);
    }

    [Fact]
    public void RecordedOptionsWithoutTheMember_StillReadAsAllowed_AsTheyWereGraded()
    {
        const string legacy = "{\"verboseMode\":false,\"spoilerFreeMode\":false,\"overseerMode\":0,\"enableToolUse\":true,\"enableWebSearch\":false}";

        Assert.True(BenchmarkCandidatePromptOptions.FromJson(legacy).AllowSourceCodeReferences);
    }

    [Fact]
    public void ASeriesMember_IsLaunchedWithTheSeriesStoredSetting()
    {
        var request = new StartBenchmarkRunRequest
        {
            SuiteId = 1,
            TestedModelConfigurationId = 1,
            AssessorModelConfigurationId = 2,
            RunCount = 3,
            AllowSourceCodeReferences = true
        };
        var series = new BenchmarkRunSeries { StartRequestJson = JsonSerializer.Serialize(request) };

        var stored = BenchmarkSeriesOrchestrator.DeserializeRequest(series);

        Assert.NotNull(stored);
        Assert.True(stored!.AllowSourceCodeReferences);
        Assert.True(RecordedOptions(stored).AllowSourceCodeReferences);

        // A series stored before the field existed ran with references allowed, and its remaining
        // members keep that, so a resume matches member 1's CandidateSystemPromptSha256.
        var legacySeries = new BenchmarkRunSeries { StartRequestJson = "{\"SuiteId\":1,\"TestedModelConfigurationId\":1,\"AssessorModelConfigurationId\":2,\"RunCount\":3}" };
        var legacy = BenchmarkSeriesOrchestrator.DeserializeRequest(legacySeries);
        Assert.NotNull(legacy);
        Assert.True(legacy!.AllowSourceCodeReferences);
        Assert.True(RecordedOptions(legacy).AllowSourceCodeReferences);
    }

    [Fact]
    public void ASeriesStartedWithoutTheSetting_IsStoredDisallowed()
    {
        var request = new StartBenchmarkRunRequest { SuiteId = 1, TestedModelConfigurationId = 1, AssessorModelConfigurationId = 2, RunCount = 3 };

        request.AllowSourceCodeReferences ??= false;
        var series = new BenchmarkRunSeries { StartRequestJson = JsonSerializer.Serialize(request) };

        var stored = BenchmarkSeriesOrchestrator.DeserializeRequest(series);
        Assert.NotNull(stored);
        Assert.False(stored!.AllowSourceCodeReferences);
        Assert.False(RecordedOptions(stored).AllowSourceCodeReferences);
    }

    [Fact]
    public void AStoredStartRequest_KeepsItsInformationConditionAndVariant()
    {
        // Series, model batches and batteries store the start request as JSON and launch from it.
        var request = new StartBenchmarkRunRequest
        {
            SuiteId = 1,
            TestedModelConfigurationId = 1,
            AssessorModelConfigurationId = 2,
            RunCount = 3,
            InformationCondition = BenchmarkInformationConditions.SourceOnlyWire,
            InstructionVariant = "race-role-tool-v1"
        };
        var series = new BenchmarkRunSeries { StartRequestJson = JsonSerializer.Serialize(request) };

        var stored = BenchmarkSeriesOrchestrator.DeserializeRequest(series);

        Assert.NotNull(stored);
        var switches = BenchmarkRunLauncher.ResolvePromptSwitches(stored!);
        Assert.Equal(BenchmarkInformationCondition.SourceOnly, switches.InformationCondition);
        Assert.Equal("race-role-tool-v1", switches.InstructionVariant);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(null, false)]
    public void TheFeedbackToolSetting_ResolvesNullAsOffered(bool? requested, bool expectedWithheld)
    {
        var request = new StartBenchmarkRunRequest
        {
            SuiteId = 1,
            TestedModelConfigurationId = 1,
            AssessorModelConfigurationId = 2,
            WithholdFeedbackTool = requested
        };

        Assert.Equal(expectedWithheld, BenchmarkRunLauncher.ResolvePromptSwitches(request).WithholdFeedbackTool);
    }

    [Fact]
    public void AStoredStartRequest_KeepsItsFeedbackToolSetting()
    {
        var request = new StartBenchmarkRunRequest
        {
            SuiteId = 1,
            TestedModelConfigurationId = 1,
            AssessorModelConfigurationId = 2,
            RunCount = 3,
            WithholdFeedbackTool = true
        };
        var series = new BenchmarkRunSeries { StartRequestJson = JsonSerializer.Serialize(request) };

        var stored = BenchmarkSeriesOrchestrator.DeserializeRequest(series);

        Assert.NotNull(stored);
        Assert.True(BenchmarkRunLauncher.ResolvePromptSwitches(stored!).WithholdFeedbackTool);

        // A request stored before the setting existed offers the tool.
        var legacySeries = new BenchmarkRunSeries { StartRequestJson = "{\"SuiteId\":1,\"TestedModelConfigurationId\":1,\"AssessorModelConfigurationId\":2,\"RunCount\":3}" };
        var legacy = BenchmarkSeriesOrchestrator.DeserializeRequest(legacySeries);
        Assert.NotNull(legacy);
        Assert.Null(legacy!.WithholdFeedbackTool);
        Assert.False(BenchmarkRunLauncher.ResolvePromptSwitches(legacy).WithholdFeedbackTool);
    }

    // --- The orchestrator claim and battery members ------------------------------------------

    /// <summary>Runs an action once, while the first save that inserts a run is being written.</summary>
    private sealed class RaceOnRunInsert : SaveChangesInterceptor
    {
        private readonly Action _race;
        private bool _fired;

        public RaceOnRunInsert(Action race) => _race = race;

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (!_fired && eventData.Context!.ChangeTracker.Entries<BenchmarkRun>().Any(e => e.State == EntityState.Added))
            {
                _fired = true;
                _race();
            }

            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    private static ApplicationDbContext CreateDb(string name, params IInterceptor[] interceptors)
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(name)
            .AddInterceptors(interceptors)
            .Options);

    /// <summary>
    /// A launcher over <paramref name="db"/>. The candidate is never executed by these tests, so the
    /// chat service and agent loop are null.
    /// </summary>
    private static BenchmarkRunLauncher CreateLauncher(
        ApplicationDbContext db,
        BenchmarkRunManager runManager,
        ModelAvailabilityService? modelAvailability = null,
        BenchmarkExperimentalChangeCatalog? changeCatalog = null)
    {
        var benchmarkService = CreateService(changeCatalog, runManager);
        var config = LauncherConfig();
        var endpointPolicy = new Overseer.Services.Privacy.EndpointPolicy(config);

        return new BenchmarkRunLauncher(
            db, benchmarkService, runManager, new BenchmarkComplianceGuard(config, db), endpointPolicy,
            modelAvailabilityService: modelAvailability);
    }

    private static IConfiguration LauncherConfig()
        => new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "Benchmark:Compliance:MaxRunsPerDay", "20" },
                { "Benchmark:Compliance:MaxRunsPerHour", "5" },
                { "AesEncryptionKey", Convert.ToBase64String(new byte[32]) }
            })
            .Build();

    /// <summary>A benchmark service that never executes a candidate: the chat service and agent loop are null.</summary>
    private static BenchmarkService CreateService(
        BenchmarkExperimentalChangeCatalog? changeCatalog = null, BenchmarkRunManager? runManager = null)
    {
        var config = LauncherConfig();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(config);
        var scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
        var endpointPolicy = new Overseer.Services.Privacy.EndpointPolicy(config);

        var benchmarkService = new BenchmarkService(
            scopeFactory,
            null!,
            null!,
            new CryptoService(config),
            runManager ?? new BenchmarkRunManager(),
            new BenchmarkDifficultyJobManager(),
            new BenchmarkScoringProfileService(scopeFactory, NullLogger<BenchmarkScoringProfileService>.Instance),
            endpointPolicy,
            config,
            NullLogger<BenchmarkService>.Instance);
        if (changeCatalog != null)
        {
            benchmarkService.ExperimentalChangeCatalog = changeCatalog;
        }

        return benchmarkService;
    }

    /// <summary>A suite the launcher admits, two admissible configurations of two providers, and a battery run.</summary>
    private static async Task<(long SuiteId, long BatteryRunId)> SeedAsync(ApplicationDbContext db)
    {
        var suite = new BenchmarkSuite { Name = "Launcher Suite", Description = "Desc" };
        suite.Questions.Add(new BenchmarkQuestion
        {
            QuestionText = "Q1",
            OrderIndex = 1,
            Difficulty = BenchmarkDifficulty.Simple,
            AssessedDifficulty = 25
        });
        db.BenchmarkSuites.Add(suite);

        db.SystemAiApiConfigurations.Add(new SystemAiApiConfiguration
        {
            Id = 1, Provider = "Anthropic", ModelId = "claude-opus-5", DisplayName = "Tested Model",
            IsEnabled = true, EncryptedApiKey = "encrypted-tested-key", ModelRole = 4
        });
        db.SystemAiApiConfigurations.Add(new SystemAiApiConfiguration
        {
            Id = 2, Provider = "OpenAI", ModelId = "gpt-5", DisplayName = "Assessor Model",
            IsEnabled = true, EncryptedApiKey = "encrypted-assessor-key", ModelRole = 4
        });

        var batteryRun = new BenchmarkBatteryRun
        {
            BatteryName = "Battery",
            DefinitionJson = "{}",
            DefinitionSha256 = new string('0', 64),
            RunsPerSuite = 1,
            RequestedMemberCount = 2,
            Status = BenchmarkRunSeriesStatus.Running,
            StartRequestJson = "{}"
        };
        db.BenchmarkBatteryRuns.Add(batteryRun);

        await db.SaveChangesAsync();
        return (suite.Id, batteryRun.Id);
    }

    private static StartBenchmarkRunRequest LaunchRequest(long suiteId) => new()
    {
        SuiteId = suiteId,
        TestedModelConfigurationId = 1,
        AssessorModelConfigurationId = 2
    };

    [Theory]
    [InlineData("battery:5", "A battery is running; wait for it or cancel it.")]
    [InlineData("series:5", "A benchmark series is running; wait for it or cancel it.")]
    public async Task ASingleRun_IsRefusedUnderAClaim_AndWritesNothing(string owner, string message)
    {
        var ct = TestContext.Current.CancellationToken;
        string name = Guid.NewGuid().ToString();
        using var db = CreateDb(name);
        var (suiteId, _) = await SeedAsync(db);

        var runManager = new BenchmarkRunManager();
        Assert.True(runManager.TryClaimOrchestrator(owner));

        var result = await CreateLauncher(db, runManager).CreateAndLaunchRunAsync(LaunchRequest(suiteId), "user", ct: ct);

        Assert.Equal(BenchmarkRunLaunchOutcome.Conflict, result.Outcome);
        Assert.Equal(message, result.Error);
        Assert.Empty(await db.BenchmarkRuns.ToListAsync(ct));
    }

    [Fact]
    public async Task ABatteryMember_IsRefusedUnderAnotherBatterysClaim()
    {
        var ct = TestContext.Current.CancellationToken;
        using var db = CreateDb(Guid.NewGuid().ToString());
        var (suiteId, batteryRunId) = await SeedAsync(db);

        var runManager = new BenchmarkRunManager();
        Assert.True(runManager.TryClaimOrchestrator(BenchmarkRunManager.BatteryOwner(batteryRunId + 100)));

        var member = new BenchmarkBatteryRunMember { BenchmarkBatteryRunId = batteryRunId, SuiteIndex = 0, Round = 1 };
        var result = await CreateLauncher(db, runManager).CreateAndLaunchRunAsync(
            LaunchRequest(suiteId), "user", null, null, ct, member);

        Assert.Equal(BenchmarkRunLaunchOutcome.Conflict, result.Outcome);
        Assert.Empty(await db.BenchmarkBatteryRunMembers.ToListAsync(ct));
    }

    [Fact]
    public async Task ABatteryMember_ForAnOccupiedSlot_IsRefusedBeforeAnyRowIsWritten()
    {
        var ct = TestContext.Current.CancellationToken;
        using var db = CreateDb(Guid.NewGuid().ToString());
        var (suiteId, batteryRunId) = await SeedAsync(db);

        db.BenchmarkBatteryRunMembers.Add(new BenchmarkBatteryRunMember
        {
            BenchmarkBatteryRunId = batteryRunId, BenchmarkRunId = 4242, SuiteIndex = 0, Round = 1
        });
        await db.SaveChangesAsync(ct);

        var member = new BenchmarkBatteryRunMember { BenchmarkBatteryRunId = batteryRunId, SuiteIndex = 0, Round = 1 };
        var result = await CreateLauncher(db, new BenchmarkRunManager()).CreateAndLaunchRunAsync(
            LaunchRequest(suiteId), "user", null, null, ct, member);

        Assert.Equal(BenchmarkRunLaunchOutcome.Conflict, result.Outcome);
        Assert.Contains("already has a member", result.Error);
        Assert.Empty(await db.BenchmarkRuns.ToListAsync(ct));
    }

    /// <summary>
    /// A launch that loses the run gate after its rows were written marks the run Failed and frees
    /// its slot, so the orphan never occupies the battery's slot.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ALostRace_FailsTheRun_AndSupersedesTheMemberRow(bool raceIsAClaim)
    {
        var ct = TestContext.Current.CancellationToken;
        string name = Guid.NewGuid().ToString();
        var runManager = new BenchmarkRunManager();
        var race = new RaceOnRunInsert(() =>
        {
            if (raceIsAClaim)
            {
                Assert.True(runManager.TryClaimOrchestrator(BenchmarkRunManager.SeriesOwner(77)));
            }
            else
            {
                Assert.True(runManager.TryStart(999, new CancellationTokenSource(), out _));
            }
        });

        using var db = CreateDb(name, race);
        var (suiteId, batteryRunId) = await SeedAsync(db);

        var member = new BenchmarkBatteryRunMember
        {
            BenchmarkBatteryRunId = batteryRunId,
            SuiteIndex = 1,
            Round = 1,
            Origin = BenchmarkBatteryMemberOrigin.Launched
        };

        var result = await CreateLauncher(db, runManager).CreateAndLaunchRunAsync(
            LaunchRequest(suiteId), "user", null, null, ct, member);

        Assert.Equal(BenchmarkRunLaunchOutcome.Conflict, result.Outcome);
        if (raceIsAClaim)
        {
            Assert.Equal("A benchmark series is running; wait for it or cancel it.", result.Error);
        }

        using var readback = CreateDb(name);
        var run = await readback.BenchmarkRuns.SingleAsync(ct);
        var row = await readback.BenchmarkBatteryRunMembers.SingleAsync(ct);

        Assert.Equal(BenchmarkRunStatus.Failed, run.Status);
        Assert.Equal(run.Id, row.BenchmarkRunId);
        Assert.Equal(batteryRunId, row.BenchmarkBatteryRunId);
        Assert.True(row.Superseded);
    }

    [Fact]
    public void TheOwnerToken_IsDerivedFromTheSeriesOrTheBatteryMember()
    {
        var member = new BenchmarkBatteryRunMember { BenchmarkBatteryRunId = 12 };

        Assert.Equal("series:4", BenchmarkRunLauncher.OrchestratorOwnerFor(4, null));
        Assert.Equal("battery:12", BenchmarkRunLauncher.OrchestratorOwnerFor(null, member));
        Assert.Null(BenchmarkRunLauncher.OrchestratorOwnerFor(null, null));
    }

    // --- Retired catalog models --------------------------------------------------------------

    private const long RoleConfigId = 3;
    private const long PanelCoAssessorId = 4;

    /// <summary>
    /// Adds the configuration under test as id 3 (Google, the given model ID and catalog mode) and a
    /// catalogued Google co-assessor as id 4 for the reference-reader panel.
    /// </summary>
    private static async Task SeedRoleConfigsAsync(ApplicationDbContext db, string modelId, string? catalogMode, CancellationToken ct)
    {
        db.SystemAiApiConfigurations.Add(new SystemAiApiConfiguration
        {
            Id = RoleConfigId, Provider = "Google", ModelId = modelId, ModelCatalogMode = catalogMode,
            DisplayName = "Role Model", IsEnabled = true, EncryptedApiKey = "encrypted-role-key", ModelRole = 4
        });
        db.SystemAiApiConfigurations.Add(new SystemAiApiConfiguration
        {
            Id = PanelCoAssessorId, Provider = "Google", ModelId = "gemini-3.8-flash",
            DisplayName = "Panel Co-assessor", IsEnabled = true, EncryptedApiKey = "encrypted-panel-key", ModelRole = 4
        });
        await db.SaveChangesAsync(ct);
    }

    /// <summary>A request that puts configuration 3 in the role the label names.</summary>
    private static StartBenchmarkRunRequest RoleRequest(long suiteId, string role)
    {
        var request = LaunchRequest(suiteId);
        switch (role)
        {
            case "Tested model": request.TestedModelConfigurationId = RoleConfigId; break;
            case "Assessor": request.AssessorModelConfigurationId = RoleConfigId; break;
            case "Co-assessor": request.CoAssessorModelConfigurationId = RoleConfigId; break;
            case "Second reader": request.SecondOpinionAssessorModelConfigurationId = RoleConfigId; break;
            case "Reference reader":
                request.CoAssessorModelConfigurationId = PanelCoAssessorId;
                request.SecondOpinionAssessorModelConfigurationId = RoleConfigId;
                break;
            case "Claim verifier": request.ClaimVerifierModelConfigurationId = RoleConfigId; break;
            case "Report writer": request.ReportWriterModelConfigurationId = RoleConfigId; break;
            default: throw new ArgumentOutOfRangeException(nameof(role), role, null);
        }
        return request;
    }

    public static TheoryData<string> Roles => new()
    {
        "Tested model", "Assessor", "Co-assessor", "Second reader", "Reference reader", "Claim verifier", "Report writer"
    };

    [Theory]
    [MemberData(nameof(Roles))]
    public async Task ARetiredModel_IsRefusedInEveryRole(string role)
    {
        var ct = TestContext.Current.CancellationToken;
        using var db = CreateDb(Guid.NewGuid().ToString());
        var (suiteId, _) = await SeedAsync(db);
        await SeedRoleConfigsAsync(db, "gemini-3.7-flash", null, ct);
        var launcher = CreateLauncher(db, new BenchmarkRunManager(), new ModelAvailabilityService(new ModelMetadataService()));

        var refusal = await launcher.ValidateRequestAsync(RoleRequest(suiteId, role), ct);

        Assert.NotNull(refusal);
        Assert.Equal(BenchmarkRunLaunchOutcome.Invalid, refusal!.Outcome);
        Assert.Equal(
            $"{role}: 'Role Model' uses gemini-3.7-flash, which was removed from the model catalog on 2026-10-10. "
            + "Switch it to another model or keep it as a custom model in System Configs, then start again.",
            refusal.Error);

        var launch = await launcher.CreateAndLaunchRunAsync(RoleRequest(suiteId, role), "user", ct: ct);
        Assert.Equal(BenchmarkRunLaunchOutcome.Invalid, launch.Outcome);
        Assert.Equal(refusal.Error, launch.Error);
        Assert.Empty(await db.BenchmarkRuns.ToListAsync(ct));
    }

    [Theory]
    [MemberData(nameof(Roles))]
    public async Task ARetiredModelKeptAsCustom_OrAModelNotInTheCatalog_IsAccepted(string role)
    {
        var ct = TestContext.Current.CancellationToken;
        var availability = new ModelAvailabilityService(new ModelMetadataService());

        using (var db = CreateDb(Guid.NewGuid().ToString()))
        {
            var (suiteId, _) = await SeedAsync(db);
            await SeedRoleConfigsAsync(db, "gemini-3.7-flash", "custom", ct);

            Assert.Null(await CreateLauncher(db, new BenchmarkRunManager(), availability)
                .ValidateRequestAsync(RoleRequest(suiteId, role), ct));
        }

        using (var db = CreateDb(Guid.NewGuid().ToString()))
        {
            var (suiteId, _) = await SeedAsync(db);
            await SeedRoleConfigsAsync(db, "gemini-9.9-unknown", null, ct);

            Assert.Null(await CreateLauncher(db, new BenchmarkRunManager(), availability)
                .ValidateRequestAsync(RoleRequest(suiteId, role), ct));
        }
    }

    // --- Information conditions and experimental changes -----------------------------------------

    [Fact]
    public async Task AnUnknownInformationCondition_IsRefused_AndWritesNothing()
    {
        var ct = TestContext.Current.CancellationToken;
        using var db = CreateDb(Guid.NewGuid().ToString());
        var (suiteId, _) = await SeedAsync(db);
        var launcher = CreateLauncher(db, new BenchmarkRunManager());

        var request = LaunchRequest(suiteId);
        request.InformationCondition = "openBook";

        var refusal = await launcher.ValidateRequestAsync(request, ct);

        Assert.NotNull(refusal);
        Assert.Equal(BenchmarkRunLaunchOutcome.Invalid, refusal!.Outcome);
        Assert.Equal("Unknown information condition 'openBook'.", refusal.Error);

        var launch = await launcher.CreateAndLaunchRunAsync(request, "user", ct: ct);
        Assert.Equal(BenchmarkRunLaunchOutcome.Invalid, launch.Outcome);
        Assert.Empty(await db.BenchmarkRuns.ToListAsync(ct));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("production")]
    [InlineData("sourceOnly")]
    [InlineData("closedBook")]
    public async Task AKnownInformationCondition_PassesValidation(string? condition)
    {
        var ct = TestContext.Current.CancellationToken;
        using var db = CreateDb(Guid.NewGuid().ToString());
        var (suiteId, _) = await SeedAsync(db);

        var request = LaunchRequest(suiteId);
        request.InformationCondition = condition;

        Assert.Null(await CreateLauncher(db, new BenchmarkRunManager()).ValidateRequestAsync(request, ct));
    }

    /// <summary>
    /// <c>probe-wiki-v1</c> adds to wiki_search, <c>probe-items-v1</c> adds to get_item_stats,
    /// <c>probe-steps-v1</c> adds instructions, <c>probe-wiki-replace-v1</c> replaces wiki_search,
    /// <c>probe-old-v1</c> is retired and <c>probe-broken-v1</c> has no change.json.
    /// </summary>
    private static void WriteProbeChanges(ExperimentalChangeRoot root, string steps = "Probe steps.")
    {
        root.Write("probe-wiki-v1", ExperimentalChangeFixtures.ChangeJson("probe-wiki-v1", title: "Probe wiki",
            additions: new() { ["wiki_search"] = "w.md" }), ("w.md", "Probe wiki addition."));
        root.Write("probe-items-v1", ExperimentalChangeFixtures.ChangeJson("probe-items-v1", title: "Probe items",
            additions: new() { ["get_item_stats"] = "w.md" }), ("w.md", "Probe item addition."));
        root.Write("probe-steps-v1", ExperimentalChangeFixtures.ChangeJson("probe-steps-v1", title: "Probe steps",
            instructions: "i.md"), ("i.md", steps));
        root.Write("probe-wiki-replace-v1", ExperimentalChangeFixtures.ChangeJson("probe-wiki-replace-v1", title: "Probe wiki replacement",
            replacements: new() { ["wiki_search"] = "w.md" }), ("w.md", "Probe wiki replacement."));
        root.Write("probe-old-v1", ExperimentalChangeFixtures.ChangeJson("probe-old-v1", title: "Probe old",
            instructions: "i.md"), ("i.md", "Old steps."));
        root.Write("probe-broken-v1", null);
        root.WriteRetired("probe-old-v1");
    }

    private static BenchmarkExperimentalChangeCatalog ProbeCatalog(ExperimentalChangeRoot root)
        => ExperimentalChangeFixtures.Catalog(root.Root);

    public static TheoryData<string?, string, string> LaunchRefusals => new()
    {
        { null, " no-such-v1 ", "Unknown experimental change 'no-such-v1'." },
        { null, "probe-broken-v1", "probe-broken-v1 is invalid: No change.json in the folder." },
        { null, "probe-old-v1", "Probe old is retired." },
        { null, "asker-profile-v1/minus/guide.wiki_search", "'asker-profile-v1/minus/guide.wiki_search' is not a list of experimental change ids joined by '+'." },
        { null, "probe-steps-v1+probe-steps-v1", "Probe steps is selected more than once." },
        { null, "probe-wiki-v1+probe-wiki-replace-v1", "Probe wiki replacement and Probe wiki both change the wiki_search description, and one of them replaces it." },
        { "closedBook", "probe-wiki-v1+probe-steps-v1", "Probe wiki has no effect under Closed book: no tools are offered there. Uncheck it, or choose another Tool Access." },
        { "sourceOnly", "probe-wiki-v1", "Probe wiki has no effect under Source code only: wiki_search is not offered there. Uncheck it, or choose another Tool Access." }
    };

    [Theory]
    [MemberData(nameof(LaunchRefusals))]
    public async Task ASelectionTheCatalogRefuses_IsRefused_AndWritesNothing(string? condition, string spec, string error)
    {
        var ct = TestContext.Current.CancellationToken;
        using var root = new ExperimentalChangeRoot();
        WriteProbeChanges(root);
        using var db = CreateDb(Guid.NewGuid().ToString());
        var (suiteId, _) = await SeedAsync(db);
        var launcher = CreateLauncher(db, new BenchmarkRunManager(), changeCatalog: ProbeCatalog(root));

        var request = LaunchRequest(suiteId);
        request.InformationCondition = condition;
        request.InstructionVariant = spec;

        var refusal = await launcher.ValidateRequestAsync(request, ct);

        Assert.NotNull(refusal);
        Assert.Equal(BenchmarkRunLaunchOutcome.Invalid, refusal!.Outcome);
        Assert.Equal(error, refusal.Error);

        var launch = await launcher.CreateAndLaunchRunAsync(request, "user", ct: ct);
        Assert.Equal(BenchmarkRunLaunchOutcome.Invalid, launch.Outcome);
        Assert.Equal(error, launch.Error);
        Assert.Empty(await db.BenchmarkRuns.ToListAsync(ct));
    }

    [Theory]
    [InlineData(null, "probe-wiki-v1")]
    [InlineData("production", "probe-wiki-v1+probe-items-v1+probe-steps-v1")]
    [InlineData("sourceOnly", "probe-items-v1")]
    [InlineData("sourceOnly", "probe-steps-v1 probe-items-v1")]
    [InlineData("closedBook", "probe-steps-v1")]
    public async Task ASelectionWithAnEffectUnderTheCondition_PassesValidation(string? condition, string spec)
    {
        var ct = TestContext.Current.CancellationToken;
        using var root = new ExperimentalChangeRoot();
        WriteProbeChanges(root);
        using var db = CreateDb(Guid.NewGuid().ToString());
        var (suiteId, _) = await SeedAsync(db);

        var request = LaunchRequest(suiteId);
        request.InformationCondition = condition;
        request.InstructionVariant = spec;

        Assert.Null(await CreateLauncher(db, new BenchmarkRunManager(), changeCatalog: ProbeCatalog(root))
            .ValidateRequestAsync(request, ct));
    }

    [Fact]
    public void ARerun_RefusesASelectionWhoseHashNoLongerMatchesTheRunsStamp_AndAcceptsARetiredOne()
    {
        using var root = new ExperimentalChangeRoot();
        WriteProbeChanges(root);
        var before = CreateService(ProbeCatalog(root));
        string stamp = before.ResolveExperimentalChanges("probe-steps-v1+probe-items-v1").Stamp!;
        Assert.StartsWith("probe-items-v1+probe-steps-v1:", stamp);

        var (variant, error) = before.ResolveRunVariant(new BenchmarkRun { InstructionVariant = stamp });
        Assert.Null(error);
        Assert.Equal("probe-items-v1+probe-steps-v1", variant!.Name);
        Assert.Equal("Probe steps.", variant.Appendix);

        string retiredStamp = before.ResolveExperimentalChanges("probe-old-v1", allowRetired: true).Stamp!;
        Assert.Null(before.ResolveRunVariant(new BenchmarkRun { InstructionVariant = retiredStamp }).Error);

        WriteProbeChanges(root, "Probe steps, edited.");
        var after = CreateService(ProbeCatalog(root));

        var (none, changed) = after.ResolveRunVariant(new BenchmarkRun { InstructionVariant = stamp });
        Assert.Null(none);
        Assert.Equal("The experimental changes 'probe-items-v1+probe-steps-v1' have changed since this run; re-run is not possible.", changed);

        var (noVariant, noError) = after.ResolveRunVariant(new BenchmarkRun());
        Assert.Null(noVariant);
        Assert.Null(noError);
    }

    /// <summary>
    /// A series stores the spec in its start request; the resume guard and each member's launch
    /// resolve it again, so an edit of a change's text reaches them under the same spec.
    /// </summary>
    [Fact]
    public async Task ASeries_ReadsItsChangesCurrentContent_OnResume()
    {
        var ct = TestContext.Current.CancellationToken;
        using var root = new ExperimentalChangeRoot();
        WriteProbeChanges(root, "Probe steps.");
        var request = new StartBenchmarkRunRequest
        {
            SuiteId = 1, TestedModelConfigurationId = 1, AssessorModelConfigurationId = 2, RunCount = 3,
            InstructionVariant = "probe-steps-v1 probe-items-v1"
        };
        var series = new BenchmarkRunSeries { StartRequestJson = JsonSerializer.Serialize(request) };
        var switches = BenchmarkRunLauncher.ResolvePromptSwitches(BenchmarkSeriesOrchestrator.DeserializeRequest(series)!);
        Assert.Equal("probe-steps-v1+probe-items-v1", switches.InstructionVariant);

        var before = CreateService(ProbeCatalog(root)).ResolveExperimentalChanges(switches.InstructionVariant).Context!;
        Assert.Equal("Probe steps.", before.Appendix);

        WriteProbeChanges(root, "Probe steps, edited.");
        var after = CreateService(ProbeCatalog(root)).ResolveExperimentalChanges(switches.InstructionVariant).Context!;

        Assert.Equal("Probe steps, edited.", after.Appendix);
        Assert.NotEqual(before.Stamp, after.Stamp);
        Assert.Equal(BenchmarkExperimentalChangeCatalog.SpecOfStamp(before.Stamp), BenchmarkExperimentalChangeCatalog.SpecOfStamp(after.Stamp));

        using var db = CreateDb(Guid.NewGuid().ToString());
        var (suiteId, _) = await SeedAsync(db);
        var member = BenchmarkSeriesOrchestrator.DeserializeRequest(series)!;
        member.SuiteId = suiteId;
        Assert.Null(await CreateLauncher(db, new BenchmarkRunManager(), changeCatalog: ProbeCatalog(root))
            .ValidateRequestAsync(member, ct));
    }

    // --- Locking on launch ----------------------------------------------------------------------

    /// <summary>
    /// A launch over SQLite whose run gate is taken while the run row is saved, so the row is written
    /// with its stamp and the run never executes.
    /// </summary>
    private static async Task<BenchmarkRun> LaunchWithoutRunningAsync(
        Microsoft.Data.Sqlite.SqliteConnection connection, BenchmarkExperimentalChangeCatalog catalog, string spec, CancellationToken ct)
    {
        var runManager = new BenchmarkRunManager();
        var race = new RaceOnRunInsert(() => Assert.True(runManager.TryStart(999, new CancellationTokenSource(), out _)));
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).AddInterceptors(race).Options;
        await using var db = new ApplicationDbContext(options);
        var (suiteId, _) = await SeedAsync(db);
        db.Users.Add(new ApplicationUser { Id = "user", UserName = "user" });
        await db.SaveChangesAsync(ct);

        var request = LaunchRequest(suiteId);
        request.InstructionVariant = spec;
        var result = await CreateLauncher(db, runManager, changeCatalog: catalog).CreateAndLaunchRunAsync(request, "user", ct: ct);

        Assert.Equal(BenchmarkRunLaunchOutcome.Conflict, result.Outcome);
        return await db.BenchmarkRuns.AsNoTracking().SingleAsync(ct);
    }

    [Fact]
    public async Task ALaunch_LocksItsDatabaseChanges_StampsTheEditedText_AndALaterTextEditIsRefused()
    {
        var ct = TestContext.Current.CancellationToken;
        using var root = new ExperimentalChangeRoot();
        WriteProbeChanges(root);
        await using var connection = Overseer.Tests.Helpers.SqliteSchemaTemplate.OpenCopy();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
        var catalog = ExperimentalChangeFixtures.Catalog(root.Root, options);
        await using var storeDb = new ApplicationDbContext(options);
        var store = new BenchmarkExperimentalChangeStore(storeDb, catalog, new BenchmarkChangeTextScreen(storeDb));

        var created = (await store.CreateAsync(new BenchmarkExperimentalChangeEditRequest
        {
            Title = "Stored steps",
            Summary = "Adds stored steps.",
            Instructions = "Stored steps.",
            GenericConfirmed = true
        }, ct)).Change!;
        var edited = (await store.UpdateAsync(created.Id, new BenchmarkExperimentalChangeEditRequest
        {
            Title = created.Title,
            Summary = created.Summary,
            Instructions = "Stored steps, edited before the launch.",
            GenericConfirmed = true,
            ExpectedUpdatedAtUtc = created.UpdatedAtUtc
        }, ct)).Change!;
        string spec = "probe-items-v1+" + created.Id;

        var run = await LaunchWithoutRunningAsync(connection, catalog, spec, ct);

        var stored = await storeDb.BenchmarkExperimentalChanges.AsNoTracking().SingleAsync(c => c.ChangeId == created.Id, ct);
        Assert.NotNull(stored.LockedAtUtc);
        await catalog.RefreshAsync(ct);
        var selection = catalog.Resolve(spec);
        Assert.Equal(selection.Stamp, run.InstructionVariant);
        Assert.Contains("Stored steps, edited before the launch.", selection.Context!.Appendix);
        Assert.True(catalog.Snapshot.Find(created.Id)!.Locked);

        var refused = await store.UpdateAsync(created.Id, new BenchmarkExperimentalChangeEditRequest
        {
            Title = edited.Title,
            Summary = edited.Summary,
            Instructions = "Stored steps, edited after the launch.",
            GenericConfirmed = true,
            ExpectedUpdatedAtUtc = edited.UpdatedAtUtc
        }, ct);
        Assert.Equal(BenchmarkChangeConflictReasons.Locked, refused.ConflictReason);

        Assert.Equal(1, await storeDb.BenchmarkExperimentalChanges.CountAsync(c => c.LockedAtUtc != null, ct));
    }

    [Fact]
    public async Task ALaunchOfBuiltInChangesOnly_StampsTheRun_AndLocksNothing()
    {
        var ct = TestContext.Current.CancellationToken;
        using var root = new ExperimentalChangeRoot();
        WriteProbeChanges(root);
        await using var connection = Overseer.Tests.Helpers.SqliteSchemaTemplate.OpenCopy();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
        var catalog = ExperimentalChangeFixtures.Catalog(root.Root, options);

        var run = await LaunchWithoutRunningAsync(connection, catalog, "probe-steps-v1 probe-items-v1", ct);

        Assert.Equal(catalog.Resolve("probe-items-v1+probe-steps-v1").Stamp, run.InstructionVariant);
        Assert.StartsWith("probe-items-v1+probe-steps-v1:", run.InstructionVariant);
    }
}
