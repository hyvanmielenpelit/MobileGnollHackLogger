using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using MobileGnollHackLogger.Data;
using Overseer.Services.Benchmarking;
using Xunit;

namespace Overseer.Tests.UnitTests;

public class DefaultSuiteCatalogServiceTests : IDisposable
{
    private readonly string _tempDir;

    public DefaultSuiteCatalogServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "DefaultSuiteCatalogServiceTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }
        catch (IOException)
        {
        }
    }

    private IConfiguration CreateConfig(int maxQuestions = 50) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [DefaultSuiteCatalogService.DefaultSuitesPathKey] = _tempDir,
                ["Benchmark:Compliance:MaxQuestionsPerSuite"] = maxQuestions.ToString()
            })
            .Build();

    private static ApplicationDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options);

    /// <summary>Questions cycle Simple, Intermediate, Advanced by position.</summary>
    private void WriteSuiteFile(string fileName, string? key, string name, int version = 1, int questionCount = 3)
    {
        var root = new Dictionary<string, object?>();
        if (key != null)
        {
            root["key"] = key;
        }
        root["version"] = version;
        root["name"] = name;
        root["description"] = $"Description of {name}.";
        root["questions"] = Enumerable.Range(1, questionCount).Select(i => new Dictionary<string, object?>
        {
            ["orderIndex"] = i,
            ["difficulty"] = (i % 3) switch { 1 => "Simple", 2 => "Intermediate", _ => "Advanced" },
            ["questionText"] = $"{name} question {i}?",
            ["expectedPoints"] = $"- point {i}"
        }).ToList();

        File.WriteAllText(Path.Combine(_tempDir, fileName), JsonSerializer.Serialize(root));
    }

    [Fact]
    public void GetCatalog_TwoValidFilesAndOneWithoutKey_ListsAllThree_FlagsOnlyTheKeylessOne()
    {
        WriteSuiteFile("alpha.json", "alpha-suite", "Alpha Suite", questionCount: 3);
        WriteSuiteFile("beta.json", "beta-suite", "Beta Suite", questionCount: 4);
        WriteSuiteFile("nokey.json", null, "No Key Suite");

        var catalog = new DefaultSuiteCatalogService(CreateConfig()).GetCatalog();

        Assert.Equal(3, catalog.Count);

        var invalid = Assert.Single(catalog, e => e.Error != null);
        Assert.Equal("nokey.json", invalid.FileName);
        Assert.Null(invalid.Key);
        Assert.Contains("key", invalid.Error);

        var alpha = Assert.Single(catalog, e => e.Key == "alpha-suite");
        Assert.Null(alpha.Error);
        Assert.Equal("Alpha Suite", alpha.Name);
        Assert.Equal(1, alpha.Version);
        Assert.Equal(3, alpha.QuestionCount);
        Assert.Equal(1, alpha.DifficultyCounts["Simple"]);
        Assert.Equal(1, alpha.DifficultyCounts["Intermediate"]);
        Assert.Equal(1, alpha.DifficultyCounts["Advanced"]);

        var beta = Assert.Single(catalog, e => e.Key == "beta-suite");
        Assert.Null(beta.Error);
        Assert.Equal(4, beta.QuestionCount);
        Assert.Equal(2, beta.DifficultyCounts["Simple"]);
    }

    [Fact]
    public void GetCatalog_DuplicateKeys_FlagsBothFiles()
    {
        WriteSuiteFile("first.json", "shared-key", "First Suite");
        WriteSuiteFile("second.json", "shared-key", "Second Suite");

        var catalog = new DefaultSuiteCatalogService(CreateConfig()).GetCatalog();

        Assert.Equal(2, catalog.Count);
        Assert.All(catalog, e =>
        {
            Assert.NotNull(e.Error);
            Assert.Contains("shared-key", e.Error);
        });
    }

    [Fact]
    public async Task ImportAsync_OneKey_CreatesSuiteWithOrigin_OrderedFromOne_RevisionOne_Unassessed()
    {
        var ct = TestContext.Current.CancellationToken;
        WriteSuiteFile("alpha.json", "alpha-suite", "Alpha Suite", version: 2, questionCount: 5);
        var config = CreateConfig();
        await using var db = CreateDbContext();
        var guard = new BenchmarkComplianceGuard(config, db);
        var service = new DefaultSuiteCatalogService(config);

        var outcome = await service.ImportAsync(new[] { "alpha-suite" }, db, guard, ct);

        Assert.Empty(outcome.Skipped);
        var created = Assert.Single(outcome.Imported);

        var suite = await db.BenchmarkSuites.Include(s => s.Questions).SingleAsync(ct);
        Assert.Equal(created.Id, suite.Id);
        Assert.Equal("Alpha Suite", suite.Name);
        Assert.Equal("alpha-suite", suite.DefaultSuiteKey);
        Assert.Equal(2, suite.DefaultSuiteVersion);

        var questions = suite.Questions.OrderBy(q => q.OrderIndex).ToList();
        Assert.Equal(Enumerable.Range(1, 5), questions.Select(q => q.OrderIndex));
        Assert.All(questions, q =>
        {
            Assert.Equal(1, q.ItemRevision);
            Assert.Null(q.AssessedDifficulty);
            Assert.False(q.IsGenerated);
            Assert.Null(q.ReviewedAtRevision);
        });
        Assert.Equal("Alpha Suite question 1?", questions[0].QuestionText);
        Assert.Equal(BenchmarkDifficulty.Intermediate, questions[1].Difficulty);
        Assert.Equal("- point 3", questions[2].ExpectedPoints);
    }

    [Fact]
    public async Task ImportAsync_SameKeyTwice_SecondCopyIsNumbered()
    {
        var ct = TestContext.Current.CancellationToken;
        WriteSuiteFile("alpha.json", "alpha-suite", "Alpha Suite");
        var config = CreateConfig();
        await using var db = CreateDbContext();
        var guard = new BenchmarkComplianceGuard(config, db);
        var service = new DefaultSuiteCatalogService(config);

        var first = await service.ImportAsync(new[] { "alpha-suite" }, db, guard, ct);
        var second = await service.ImportAsync(new[] { "alpha-suite" }, db, guard, ct);

        Assert.Equal("Alpha Suite", Assert.Single(first.Imported).Name);
        Assert.Equal("Alpha Suite (2)", Assert.Single(second.Imported).Name);
        Assert.Equal(2, await db.BenchmarkSuites.CountAsync(s => s.DefaultSuiteKey == "alpha-suite", ct));
    }

    [Fact]
    public async Task ImportAsync_UnknownKey_IsSkippedWithReason_NoRowsWritten()
    {
        var ct = TestContext.Current.CancellationToken;
        WriteSuiteFile("alpha.json", "alpha-suite", "Alpha Suite");
        var config = CreateConfig();
        await using var db = CreateDbContext();
        var guard = new BenchmarkComplianceGuard(config, db);
        var service = new DefaultSuiteCatalogService(config);

        var outcome = await service.ImportAsync(new[] { "no-such-suite" }, db, guard, ct);

        Assert.Empty(outcome.Imported);
        var skip = Assert.Single(outcome.Skipped);
        Assert.Equal("no-such-suite", skip.Key);
        Assert.Contains("No default suite", skip.Reason);
        Assert.False(await db.BenchmarkSuites.AnyAsync(ct));
    }

    [Fact]
    public async Task ImportAsync_QuotaRefusal_IsSkippedWithQuotaReason_NoRowsWritten()
    {
        var ct = TestContext.Current.CancellationToken;
        WriteSuiteFile("alpha.json", "alpha-suite", "Alpha Suite", questionCount: 3);
        var config = CreateConfig(maxQuestions: 0);
        await using var db = CreateDbContext();
        var guard = new BenchmarkComplianceGuard(config, db);
        var service = new DefaultSuiteCatalogService(config);

        var outcome = await service.ImportAsync(new[] { "alpha-suite" }, db, guard, ct);

        Assert.Empty(outcome.Imported);
        var skip = Assert.Single(outcome.Skipped);
        Assert.Equal("alpha-suite", skip.Key);
        Assert.Contains("question limit", skip.Reason);
        Assert.DoesNotContain("not found", skip.Reason);
        Assert.False(await db.BenchmarkSuites.AnyAsync(ct));
        Assert.False(await db.BenchmarkQuestions.AnyAsync(ct));
    }
}
