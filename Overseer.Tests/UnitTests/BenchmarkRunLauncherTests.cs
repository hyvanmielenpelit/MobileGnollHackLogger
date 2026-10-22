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
    public void TheConditionAndVariant_AreResolvedAsGiven_AndTheVariantNameTrimmed()
    {
        var request = new StartBenchmarkRunRequest
        {
            InformationCondition = BenchmarkInformationConditions.ClosedBookWire,
            InstructionVariant = "  source-tools-v1 "
        };

        var switches = BenchmarkRunLauncher.ResolvePromptSwitches(request);

        Assert.Equal(BenchmarkInformationCondition.ClosedBook, switches.InformationCondition);
        Assert.Equal("source-tools-v1", switches.InstructionVariant);

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
            InstructionVariant = "source-tools-v1"
        };
        var series = new BenchmarkRunSeries { StartRequestJson = JsonSerializer.Serialize(request) };

        var stored = BenchmarkSeriesOrchestrator.DeserializeRequest(series);

        Assert.NotNull(stored);
        var switches = BenchmarkRunLauncher.ResolvePromptSwitches(stored!);
        Assert.Equal(BenchmarkInformationCondition.SourceOnly, switches.InformationCondition);
        Assert.Equal("source-tools-v1", switches.InstructionVariant);
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

    [Fact]
    public void ARerun_RefusesAVariantWhoseHashNoLongerMatchesTheRunsStamp()
    {
        string root = Path.Combine(Path.GetTempPath(), "variant-rerun-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "probe-variant"));
        try
        {
            File.WriteAllText(Path.Combine(root, "probe-variant", BenchmarkInstructionVariantCatalog.VariantFileName),
                "{ \"name\": \"probe-variant\", \"description\": \"Probe.\" }");
            var catalog = new BenchmarkInstructionVariantCatalog(
                root, Array.Empty<Overseer.Services.Tools.IToolHandler>(), NullLogger.Instance);
            var service = CreateService(catalog);
            string stamp = catalog.Find("probe-variant")!.Context!.Stamp;

            var (variant, error) = service.ResolveRunVariant(new BenchmarkRun { InstructionVariant = stamp });
            Assert.Null(error);
            Assert.Equal("probe-variant", variant!.Name);

            var (_, changed) = service.ResolveRunVariant(new BenchmarkRun { InstructionVariant = "probe-variant:000000000000" });
            Assert.Equal("The instruction variant 'probe-variant' has changed since this run; re-run is not possible.", changed);

            var (none, noError) = service.ResolveRunVariant(new BenchmarkRun());
            Assert.Null(none);
            Assert.Null(noError);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
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
        BenchmarkInstructionVariantCatalog? variantCatalog = null)
    {
        var benchmarkService = CreateService(variantCatalog, runManager);
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
        BenchmarkInstructionVariantCatalog? variantCatalog = null, BenchmarkRunManager? runManager = null)
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
        if (variantCatalog != null)
        {
            benchmarkService.VariantCatalog = variantCatalog;
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

    // --- Information conditions and instruction variants ------------------------------------------

    /// <summary>A catalog over a fresh temporary root holding the given variant folders.</summary>
    private static (BenchmarkInstructionVariantCatalog Catalog, string Root) VariantCatalogOver(
        params (string Folder, string? VariantJson)[] folders)
    {
        string root = Path.Combine(Path.GetTempPath(), "launcher-variants-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        foreach (var (folder, variantJson) in folders)
        {
            string path = Path.Combine(root, folder);
            Directory.CreateDirectory(path);
            if (variantJson != null)
            {
                File.WriteAllText(Path.Combine(path, BenchmarkInstructionVariantCatalog.VariantFileName), variantJson);
            }
        }

        return (new BenchmarkInstructionVariantCatalog(
            root, Array.Empty<Overseer.Services.Tools.IToolHandler>(), NullLogger.Instance), root);
    }

    private static void DeleteRoot(string root)
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }

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

    [Fact]
    public async Task AnUnknownInstructionVariant_IsRefused_AndWritesNothing()
    {
        var ct = TestContext.Current.CancellationToken;
        var (catalog, root) = VariantCatalogOver();
        try
        {
            using var db = CreateDb(Guid.NewGuid().ToString());
            var (suiteId, _) = await SeedAsync(db);
            var launcher = CreateLauncher(db, new BenchmarkRunManager(), variantCatalog: catalog);

            var request = LaunchRequest(suiteId);
            request.InstructionVariant = " no-such-variant ";

            var refusal = await launcher.ValidateRequestAsync(request, ct);

            Assert.NotNull(refusal);
            Assert.Equal(BenchmarkRunLaunchOutcome.Invalid, refusal!.Outcome);
            Assert.Equal("Unknown instruction variant 'no-such-variant'.", refusal.Error);

            var launch = await launcher.CreateAndLaunchRunAsync(request, "user", ct: ct);
            Assert.Equal(BenchmarkRunLaunchOutcome.Invalid, launch.Outcome);
            Assert.Empty(await db.BenchmarkRuns.ToListAsync(ct));
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public async Task AnInvalidInstructionVariant_IsRefused_WithItsReason()
    {
        var ct = TestContext.Current.CancellationToken;
        var (catalog, root) = VariantCatalogOver(("broken-variant", null));
        try
        {
            using var db = CreateDb(Guid.NewGuid().ToString());
            var (suiteId, _) = await SeedAsync(db);

            var request = LaunchRequest(suiteId);
            request.InstructionVariant = "broken-variant";

            var refusal = await CreateLauncher(db, new BenchmarkRunManager(), variantCatalog: catalog)
                .ValidateRequestAsync(request, ct);

            Assert.NotNull(refusal);
            Assert.Equal(BenchmarkRunLaunchOutcome.Invalid, refusal!.Outcome);
            Assert.StartsWith("The instruction variant 'broken-variant' is invalid: ", refusal.Error);
            Assert.Contains(BenchmarkInstructionVariantCatalog.VariantFileName, refusal.Error);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public async Task AValidInstructionVariant_PassesValidation()
    {
        var ct = TestContext.Current.CancellationToken;
        var (catalog, root) = VariantCatalogOver(("plain-variant", "{\"name\":\"plain-variant\"}"));
        try
        {
            Assert.True(catalog.Find("plain-variant")!.IsValid);

            using var db = CreateDb(Guid.NewGuid().ToString());
            var (suiteId, _) = await SeedAsync(db);

            var request = LaunchRequest(suiteId);
            request.InstructionVariant = "plain-variant";

            Assert.Null(await CreateLauncher(db, new BenchmarkRunManager(), variantCatalog: catalog)
                .ValidateRequestAsync(request, ct));
        }
        finally
        {
            DeleteRoot(root);
        }
    }
}
