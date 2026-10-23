namespace Overseer.Tests.UnitTests;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MobileGnollHackLogger.Data;
using Overseer.Controllers;
using Overseer.Models;
using Overseer.Services;
using Overseer.Services.Benchmarking;
using Overseer.Services.Tools;
using Overseer.Tests.Helpers;
using Xunit;

/// <summary>
/// GET instruction-variants/{name}/details: a variant folder's features with their texts, the
/// production description of each reworded tool, an experimental tool's parameter schema, and the
/// runs that used the variant or one of its ablations.
/// </summary>
public sealed class BenchmarkInstructionVariantDetailsTests : IDisposable
{
    private const string WikiProductionDescription = "Production wiki_search description.";
    private const string CharacterSchema = "{\"type\":\"object\",\"properties\":{\"race\":{\"type\":\"string\"}}}";

    private readonly string _root;

    public BenchmarkInstructionVariantDetailsTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "BenchmarkInstructionVariantDetailsTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            try
            {
                Directory.Delete(_root, true);
            }
            catch { }
        }
    }

    private sealed class FakeToolHandler : IToolHandler
    {
        private readonly string _schema;

        public FakeToolHandler(string toolName, string description, bool isExperimental = false, string schema = "{}")
        {
            ToolName = toolName;
            Description = description;
            IsExperimental = isExperimental;
            _schema = schema;
        }

        public string ToolName { get; }
        public string Description { get; set; }
        public ToolExecutionLocation ExecutionLocation => ToolExecutionLocation.Server;
        public ToolCategory Category => ToolCategory.InformationRetrieval;
        public JsonElement ParameterSchema => JsonDocument.Parse(_schema).RootElement;
        public bool IsExperimental { get; }

        public Task<ToolResult> ExecuteAsync(JsonElement parameters, ToolExecutionContext context, CancellationToken cancellationToken)
            => Task.FromResult(new ToolResult { Success = true, Content = "ok" });
    }

    private static readonly IToolHandler[] Handlers =
    {
        new FakeToolHandler("wiki_search", WikiProductionDescription),
        new FakeToolHandler("get_item_stats", "Production get_item_stats description."),
        new FakeToolHandler("get_character_info", "Unused fallback.", isExperimental: true, schema: CharacterSchema)
    };

    private void WriteVariant(string folder, string? variantJson, params (string Name, string Content)[] files)
    {
        string path = Path.Combine(_root, folder);
        Directory.CreateDirectory(path);
        if (variantJson != null)
        {
            File.WriteAllText(Path.Combine(path, BenchmarkInstructionVariantCatalog.VariantFileName), variantJson);
        }
        foreach (var (name, content) in files)
        {
            File.WriteAllText(Path.Combine(path, name), content);
        }
    }

    /// <summary>A parent <c>probe-base</c> and a child <c>probe</c>, an unrelated <c>probe-extra</c>, and an invalid <c>broken</c>.</summary>
    private BenchmarkInstructionVariantCatalog SeedCatalog()
    {
        WriteVariant("probe-base", """
            {
              "name": "probe-base",
              "description": "The parent.",
              "toolGuides": { "get_character_info": "character.md", "get_item_stats": "items.md" },
              "experimentalTools": ["get_character_info"],
              "experiments": ["stats_referenced_by"]
            }
            """,
            ("character.md", "Character guide."),
            ("items.md", "Item guide; see referenced_by."));
        WriteVariant("probe", """
            {
              "name": "probe",
              "description": "The child.",
              "extends": "probe-base",
              "promptAppendix": "appendix.md",
              "toolGuides": { "wiki_search": "wiki.md" }
            }
            """,
            ("appendix.md", "Use get_character_info first."),
            ("wiki.md", "Reworded wiki guide."));
        WriteVariant("probe-extra", "{ \"name\": \"probe-extra\", \"promptAppendix\": \"appendix.md\" }",
            ("appendix.md", "Another paragraph."));
        WriteVariant("broken", null);
        return new BenchmarkInstructionVariantCatalog(_root, Handlers, NullLogger.Instance);
    }

    private static IConfiguration Config()
        => new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "AesEncryptionKey", Convert.ToBase64String(new byte[32]) }
            })
            .Build();

    /// <summary>A benchmark service over <paramref name="catalog"/> that never executes a candidate.</summary>
    private static BenchmarkService CreateService(BenchmarkInstructionVariantCatalog catalog)
    {
        var config = Config();
        var services = new ServiceCollection();
        services.AddSingleton(config);
        var scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

        return new BenchmarkService(
            scopeFactory,
            null!,
            null!,
            new CryptoService(config),
            new BenchmarkRunManager(),
            new BenchmarkDifficultyJobManager(),
            new BenchmarkScoringProfileService(scopeFactory, NullLogger<BenchmarkScoringProfileService>.Instance),
            new Overseer.Services.Privacy.EndpointPolicy(config),
            config,
            NullLogger<BenchmarkService>.Instance)
        {
            VariantCatalog = catalog
        };
    }

    private static (AdminBenchmarkController Controller, ApplicationDbContext Db) CreateController(BenchmarkInstructionVariantCatalog catalog)
    {
        var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options);

        // The action touches only the DbContext and the benchmark service's variant catalog.
        var controller = new AdminBenchmarkController(
            db, CreateService(catalog), null!, null!, null!, null!, null!, null!, null!,
            null!, null!, null!, null!, null!, null!, null!, null!, null!, null!);
        return (controller, db);
    }

    private static BenchmarkRun RunWith(string? stamp, DateTime startedAtUtc, string? condition = null) => new()
    {
        SuiteName = "Variant Suite",
        TestedModelSnapshot = BenchmarkModelSnapshots.Model(provider: "OpenAI", modelId: "gpt-test", displayName: "Candidate"),
        AssessorModelSnapshot = BenchmarkModelSnapshots.Model(provider: "Anthropic", modelId: "claude-test", displayName: "Assessor"),
        StartedAtUtc = startedAtUtc,
        Status = BenchmarkRunStatus.Completed,
        InstructionVariant = stamp,
        CandidatePromptOptionsJson = BenchmarkService.CandidatePromptOptionsFor(
            false, true, false, BenchmarkInformationConditions.FromWire(condition)).ToCanonicalJson()
    };

    private static BenchmarkInstructionVariantDetailsDto ReadDetails(IActionResult result)
        => Assert.IsType<BenchmarkInstructionVariantDetailsDto>(Assert.IsType<OkObjectResult>(result).Value);

    [Fact]
    public async Task AnUnknownName_Is404()
    {
        var ct = TestContext.Current.CancellationToken;
        var (controller, _) = CreateController(SeedCatalog());

        Assert.IsType<NotFoundObjectResult>(await controller.GetInstructionVariantDetails("no-such-variant", ct));
        // An ablation spec is not a variant folder.
        Assert.IsType<NotFoundObjectResult>(await controller.GetInstructionVariantDetails("probe/minus/guide.wiki_search", ct));
    }

    [Fact]
    public async Task AnInvalidVariant_ReturnsItsReason_AndNoFeatures()
    {
        var ct = TestContext.Current.CancellationToken;
        var (controller, _) = CreateController(SeedCatalog());

        var details = ReadDetails(await controller.GetInstructionVariantDetails("broken", ct));

        Assert.Equal("broken", details.Name);
        Assert.False(details.IsValid);
        Assert.Contains(BenchmarkInstructionVariantCatalog.VariantFileName, details.InvalidReason);
        Assert.Null(details.Stamp);
        Assert.Empty(details.Features);
        Assert.Empty(details.Runs);
        Assert.Equal(0, details.RunCount);
    }

    [Fact]
    public async Task AValidVariant_ReturnsItsFeaturesWithTheirTexts()
    {
        var ct = TestContext.Current.CancellationToken;
        var catalog = SeedCatalog();
        var (controller, _) = CreateController(catalog);

        var details = ReadDetails(await controller.GetInstructionVariantDetails("probe", ct));

        Assert.True(details.IsValid);
        Assert.Equal("probe", details.Name);
        Assert.Equal("The child.", details.Description);
        Assert.Equal("probe-base", details.Parent);
        Assert.Equal(catalog.Find("probe")!.Context!.Stamp, details.Stamp);
        Assert.Equal(
            new[] { "tool.get_character_info", "experiment.stats_referenced_by", "guide.get_item_stats", "guide.wiki_search", "appendix.probe" },
            details.Features.Select(f => f.Id));

        var tool = details.Features[0];
        Assert.Equal("experimentalTool", tool.Kind);
        Assert.True(tool.FromParent);
        Assert.Equal("Character guide.", tool.Text);
        Assert.Null(tool.ProductionText);
        Assert.Equal(CharacterSchema, tool.ParameterSchemaJson);
        Assert.False(tool.AppliesUnder.ClosedBook);

        var experiment = details.Features[1];
        Assert.Equal("experiment", experiment.Kind);
        Assert.Null(experiment.Text);
        Assert.Null(experiment.ParameterSchemaJson);

        var itemGuide = details.Features[2];
        Assert.Equal("Item guide; see referenced_by.", itemGuide.Text);
        Assert.Equal("Production get_item_stats description.", itemGuide.ProductionText);
        Assert.Equal(new[] { "experiment.stats_referenced_by" }, itemGuide.Mentions);
        Assert.Null(itemGuide.ParameterSchemaJson);

        var wikiGuide = details.Features[3];
        Assert.Equal("rewordedGuide", wikiGuide.Kind);
        Assert.False(wikiGuide.FromParent);
        Assert.Equal("Reworded wiki guide.", wikiGuide.Text);
        Assert.Equal(WikiProductionDescription, wikiGuide.ProductionText);
        Assert.False(wikiGuide.AppliesUnder.SourceOnly);
        Assert.True(wikiGuide.AppliesUnder.Production);

        var appendix = details.Features[4];
        Assert.Equal("appendix", appendix.Kind);
        Assert.Equal("Use get_character_info first.", appendix.Text);
        Assert.Equal(new[] { "tool.get_character_info" }, appendix.Mentions);
        Assert.True(appendix.AppliesUnder.ClosedBook);
    }

    [Fact]
    public async Task TheRuns_AreTheVariantsAndItsAblations_NewestFirst_WithTheirHashState()
    {
        var ct = TestContext.Current.CancellationToken;
        var catalog = SeedCatalog();
        var (controller, db) = CreateController(catalog);
        string whole = catalog.Find("probe")!.Context!.Stamp;
        string ablation = catalog.Find("probe/minus/guide.wiki_search")!.Context!.Stamp;
        var start = new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);

        for (int i = 0; i < 9; i++)
        {
            db.BenchmarkRuns.Add(RunWith(whole, start.AddHours(i)));
        }
        db.BenchmarkRuns.Add(RunWith("probe:000000000000", start.AddHours(-10)));
        db.BenchmarkRuns.Add(RunWith(ablation, start.AddHours(20), BenchmarkInformationConditions.SourceOnlyWire));
        db.BenchmarkRuns.Add(RunWith("probe/only/appendix.probe:000000000000", start.AddHours(-20)));
        db.BenchmarkRuns.Add(RunWith("probe-extra:" + new string('e', 12), start.AddHours(30)));
        db.BenchmarkRuns.Add(RunWith(catalog.Find("probe-base")!.Context!.Stamp, start.AddHours(31)));
        db.BenchmarkRuns.Add(RunWith(null, start.AddHours(32)));
        await db.SaveChangesAsync(ct);

        var details = ReadDetails(await controller.GetInstructionVariantDetails("probe", ct));

        Assert.Equal(12, details.RunCount);
        Assert.Equal(2, details.OlderHashRunCount);
        Assert.Equal(BenchmarkInstructionVariantDetailsDto.RunListSize, details.Runs.Count);
        Assert.Equal(details.Runs.Select(r => r.StartedAtUtc).OrderByDescending(t => t), details.Runs.Select(r => r.StartedAtUtc));

        var newest = details.Runs[0];
        Assert.Equal("probe/minus/guide.wiki_search", newest.Spec);
        Assert.True(newest.HashMatchesCurrent);
        Assert.Equal(BenchmarkInformationConditions.SourceOnlyWire, newest.InformationCondition);
        Assert.Equal("Variant Suite", newest.SuiteName);
        Assert.Equal("Candidate", newest.ModelDisplayName);
        Assert.Equal(nameof(BenchmarkRunStatus.Completed), newest.Status);

        Assert.All(details.Runs.Skip(1), r =>
        {
            Assert.Equal("probe", r.Spec);
            Assert.True(r.HashMatchesCurrent);
            Assert.Equal(BenchmarkInformationConditions.ProductionWire, r.InformationCondition);
        });
        Assert.DoesNotContain(details.Runs, r => r.Spec.StartsWith("probe-", StringComparison.Ordinal));
    }
}
