namespace Overseer.Tests.UnitTests;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Overseer.Services;
using Overseer.Services.Benchmarking;
using Xunit;

/// <summary>
/// The corpus condition marker a scrubbed wiki carries at its root: what it reads as, how it is
/// printed, and that the wiki records it at each index pass without ever indexing it.
/// </summary>
public sealed class BenchmarkCorpusConditionMarkerTests : IDisposable
{
    private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(30);

    private readonly string _tempDir;
    private readonly List<WikiService> _services = new();

    public BenchmarkCorpusConditionMarkerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "BenchmarkCorpusConditionMarkerTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        foreach (var service in _services)
        {
            service.Dispose();
        }

        if (Directory.Exists(_tempDir))
        {
            try
            {
                Directory.Delete(_tempDir, true);
            }
            catch { }
        }
    }

    private void WriteMarker(string content)
        => File.WriteAllText(Path.Combine(_tempDir, BenchmarkCorpusConditionMarker.FileName), content);

    private void WriteArticle(string relativePath, string content)
    {
        string path = Path.Combine(_tempDir, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private async Task<WikiService> IndexedWikiAsync()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { { "WikiPath", _tempDir } })
            .Build();
        var service = new WikiService(config);
        _services.Add(service);
        await service.InitializationTask.WaitAsync(WaitLimit, TestContext.Current.CancellationToken);
        return service;
    }

    // -- Read ------------------------------------------------------------------------------------

    [Fact]
    public void Read_WithoutAMarker_IsNull()
    {
        Assert.Null(BenchmarkCorpusConditionMarker.Read(_tempDir));
        Assert.Null(BenchmarkCorpusConditionMarker.Read(null));
        Assert.Null(BenchmarkCorpusConditionMarker.Read("  "));
        Assert.Null(BenchmarkCorpusConditionMarker.Read(Path.Combine(_tempDir, "missing")));
    }

    [Fact]
    public void Read_AValidMarker_IsConditionColonLabel()
    {
        WriteMarker("{ \"condition\": \"scarce\", \"label\": \"run-130 gold facts\", \"suite\": \"Core\", \"scrubbedPages\": [\"Gnoll.md\"] }");

        Assert.Equal("scarce:run-130 gold facts", BenchmarkCorpusConditionMarker.Read(_tempDir));
    }

    [Fact]
    public void Read_AMarkerWithoutALabel_KeepsTheColon()
    {
        WriteMarker("{ \"condition\": \" scarce \" }");

        Assert.Equal("scarce:", BenchmarkCorpusConditionMarker.Read(_tempDir));
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("{ \"label\": \"no condition\" }")]
    [InlineData("{ \"condition\": \"\" }")]
    [InlineData("{ \"condition\": 3 }")]
    [InlineData("[\"scarce\"]")]
    public void Read_AMarkerThatDoesNotParse_OrLacksACondition_IsInvalid(string content)
    {
        WriteMarker(content);

        Assert.Equal(BenchmarkCorpusConditionMarker.Invalid, BenchmarkCorpusConditionMarker.Read(_tempDir));
    }

    [Fact]
    public void Read_ALongLabel_IsTruncatedTo128Characters()
    {
        WriteMarker("{ \"condition\": \"scarce\", \"label\": \"" + new string('x', 300) + "\" }");

        string? value = BenchmarkCorpusConditionMarker.Read(_tempDir);

        Assert.NotNull(value);
        Assert.Equal(BenchmarkCorpusConditionMarker.MaxLength, value!.Length);
        Assert.Equal(128, value.Length);
        Assert.StartsWith("scarce:xxx", value);
    }

    // -- Describe --------------------------------------------------------------------------------

    [Theory]
    [InlineData(null, "live")]
    [InlineData("", "live")]
    [InlineData("scarce:run-130", "scarce — run-130")]
    [InlineData("scarce:", "scarce")]
    [InlineData("invalid", "invalid")]
    public void Describe_PrintsLive_OrConditionAndLabel(string? value, string expected)
    {
        Assert.Equal(expected, BenchmarkCorpusConditionMarker.Describe(value));
    }

    // -- The wiki index --------------------------------------------------------------------------

    [Fact]
    public async Task AWikiWithoutAMarker_HasNoCorpusCondition()
    {
        WriteArticle("Gnoll.md", "Gnolls are common early monsters.\n");

        var service = await IndexedWikiAsync();

        Assert.Null(service.CorpusCondition);
    }

    [Fact]
    public async Task AWikiWithAMarker_RecordsItsCondition_AndNeverIndexesTheMarker()
    {
        WriteArticle("Gnoll.md", "The quokkaberry grows near the gnoll village.\n");
        WriteMarker("{ \"condition\": \"scarce\", \"label\": \"quuxzorbian scrub\", \"baseWikiSha\": \"abc123\" }");

        var service = await IndexedWikiAsync();

        Assert.Equal("scarce:quuxzorbian scrub", service.CorpusCondition);
        Assert.Equal(1, service.GetStatus().Index!.FileCount);
        Assert.Empty(service.GetRelevantContext("quuxzorbian"));
        Assert.Empty(service.GetRelevantContext("baseWikiSha"));
        Assert.Contains("Gnoll.md", Assert.Single(service.GetRelevantContext("quokkaberry")));
    }

    [Fact]
    public async Task AnInvalidMarker_IsRecordedAsInvalid()
    {
        WriteArticle("Gnoll.md", "Gnolls are common early monsters.\n");
        WriteMarker("{ not valid json");

        var service = await IndexedWikiAsync();

        Assert.Equal(BenchmarkCorpusConditionMarker.Invalid, service.CorpusCondition);
        Assert.Equal(1, service.GetStatus().Index!.FileCount);
    }
}
