using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using MobileGnollHackLogger.Data;
using Overseer.Services.Benchmarking;
using Overseer.Tests.Helpers;
using Xunit;

namespace Overseer.Tests.UnitTests;

/// <summary>
/// The content-addressed store behind benchmark history. The hash must be stable and must agree
/// with the migration's HASHBYTES backfill, and a bound configuration must take its settings from
/// the recorded snapshot and only its key from the live row.
/// </summary>
public class SystemAiConfigurationSnapshotStoreTests
{
    private static ApplicationDbContext CreateDb()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static SystemAiApiConfiguration Live(
        string provider = "OpenAI", string modelId = "gpt-x", string? baseUrl = null, string? thinkingLevel = "high")
        => new()
        {
            Id = 11,
            DisplayName = "GPT X",
            Provider = provider,
            ModelId = modelId,
            ThinkingLevel = thinkingLevel,
            MaxOutputTokens = 4096,
            ParallelExecutionMode = MobileGnollHackLogger.Data.ParallelExecutionMode.Enabled,
            BaseUrl = baseUrl,
            EncryptedApiKey = "live-cipher",
            ApiKeyNonce = "live-nonce",
            ApiKeyTag = "live-tag",
            IsEnabled = true,
            ModelRole = 4
        };

    // -- Canonical form and hash -------------------------------------------------------------

    [Fact]
    public void Hash_MatchesTheGoldenVector()
    {
        /* Pins the canonical form and its encoding. The migration's SQL backfill hashes the same text
           as UTF-16LE with HASHBYTES; a change here that it does not share would split identical
           settings into two rows. */
        var snapshot = SystemAiConfigurationSnapshotStore.FromConfiguration(Live());

        Assert.Equal(
            "SystemAiConfigurationSnapshot/1\nDisplayName=GPT X\nIsComplete=1\nMaxOutputTokens=4096\nModelId=gpt-x\n"
            + "ParallelExecutionMode=2\nProvider=OpenAI\nThinkingLevel=high\n",
            SystemAiConfigurationSnapshotStore.Canonicalize(snapshot));
        Assert.Equal("d5531b84903601defd20651f61bafcab805c1dcf2cbc9d327559f685138c6f16", snapshot.Sha256);
    }

    [Fact]
    public void Hash_ChangesWithAnySingleField_AndOmitsNullFields()
    {
        var baseline = BenchmarkModelSnapshots.Model(thinkingLevel: "high");
        var moved = BenchmarkModelSnapshots.Model(thinkingLevel: "low");
        var unset = BenchmarkModelSnapshots.Model(thinkingLevel: null);

        Assert.NotEqual(baseline.Sha256, moved.Sha256);
        Assert.NotEqual(baseline.Sha256, unset.Sha256);
        Assert.DoesNotContain("ThinkingLevel", SystemAiConfigurationSnapshotStore.Canonicalize(unset));
    }

    [Fact]
    public void IsComplete_SeparatesOtherwiseEqualRows()
    {
        var complete = BenchmarkModelSnapshots.Model(isComplete: true);
        var partial = BenchmarkModelSnapshots.Model(isComplete: false);

        Assert.NotEqual(complete.Sha256, partial.Sha256);
    }

    // -- Deduplication -----------------------------------------------------------------------

    [Fact]
    public async Task GetOrCreate_DeduplicatesWithinOneUnitOfWork_AcrossSaves_AndAcrossRoles()
    {
        using var db = CreateDb();

        // Two roles captured before any save share one instance.
        db.BenchmarkSuites.Add(new BenchmarkSuite { Name = "pending change" });
        var first = await SystemAiConfigurationSnapshotStore.CaptureAsync(db, Live(), CancellationToken.None);
        var second = await SystemAiConfigurationSnapshotStore.CaptureAsync(db, Live(), CancellationToken.None);
        Assert.Same(first, second);
        await db.SaveChangesAsync();

        // After the save, a later capture of the same settings finds the stored row.
        var third = await SystemAiConfigurationSnapshotStore.CaptureAsync(db, Live(), CancellationToken.None);
        Assert.Equal(first.Id, third.Id);

        // Different settings get a row of their own.
        var other = await SystemAiConfigurationSnapshotStore.CaptureAsync(db, Live(thinkingLevel: "low"), CancellationToken.None);
        await db.SaveChangesAsync();
        Assert.NotEqual(first.Id, other.Id);
        Assert.Equal(2, await db.SystemAiConfigurationSnapshots.CountAsync());
    }

    [Fact]
    public async Task GetOrCreate_OnACleanContext_SavesAtOnce()
    {
        using var db = CreateDb();

        var snapshot = await SystemAiConfigurationSnapshotStore.CaptureAsync(db, Live(), CancellationToken.None);

        Assert.Equal(EntityState.Unchanged, db.Entry(snapshot).State);
        Assert.True(snapshot.Id > 0);
    }

    // -- Bind --------------------------------------------------------------------------------

    [Fact]
    public void Bind_TakesSettingsFromTheSnapshot_AndTheKeyFromTheLiveRow()
    {
        var recorded = SystemAiConfigurationSnapshotStore.FromConfiguration(Live(thinkingLevel: "high"));
        var edited = Live(thinkingLevel: "low");
        edited.ModelId = "gpt-x";
        edited.MaxOutputTokens = 99;
        edited.EncryptedApiKey = "rotated-cipher";

        var bound = SystemAiConfigurationSnapshotStore.Bind(edited, recorded);

        Assert.Equal("high", bound.ThinkingLevel);
        Assert.Equal(4096, bound.MaxOutputTokens);
        Assert.Equal("rotated-cipher", bound.EncryptedApiKey);
        Assert.Equal(edited.Id, bound.Id);
        Assert.Same(recorded, SystemAiConfigurationSnapshotStore.SnapshotOf(bound));
        Assert.Null(SystemAiConfigurationSnapshotStore.SnapshotOf(edited));
    }

    [Fact]
    public void Bind_TakesTheEndpointFromTheSnapshot()
    {
        var recorded = SystemAiConfigurationSnapshotStore.FromConfiguration(Live(baseUrl: "https://gw.example.com"));

        var bound = SystemAiConfigurationSnapshotStore.Bind(Live(baseUrl: "https://gw.example.com"), recorded);

        Assert.Equal("https://gw.example.com", bound.BaseUrl);
    }

    [Theory]
    [InlineData("Anthropic", null)]
    [InlineData("OpenAI", "https://gw.example.com")]
    public void Bind_RefusesAProviderOrEndpointMismatch(string liveProvider, string? liveBaseUrl)
    {
        var recorded = SystemAiConfigurationSnapshotStore.FromConfiguration(Live());

        var ex = Assert.Throws<InvalidOperationException>(
            () => SystemAiConfigurationSnapshotStore.Bind(Live(provider: liveProvider, baseUrl: liveBaseUrl), recorded));

        Assert.Equal(SystemAiConfigurationSnapshotStore.MismatchMessage, ex.Message);
    }

    [Fact]
    public void Bind_FillsFieldsAnIncompleteSnapshotNeverRecorded_FromTheLiveRow()
    {
        var legacy = BenchmarkModelSnapshots.Model(provider: "OpenAI", modelId: "gpt-x", thinkingLevel: "high",
            maxOutputTokens: null, parallelExecutionMode: null, isComplete: false);

        var bound = SystemAiConfigurationSnapshotStore.Bind(Live(thinkingLevel: "low"), legacy);

        Assert.Equal("high", bound.ThinkingLevel);
        Assert.Equal(4096, bound.MaxOutputTokens);
    }

    // -- Endpoint fingerprint ----------------------------------------------------------------

    [Fact]
    public void EndpointFingerprint_IsOfficialOrAHostlessCustomFingerprint()
    {
        var official = BenchmarkModelSnapshots.Model();
        var custom = BenchmarkModelSnapshots.Model(baseUrl: "https://gw.example.com", apiVersion: "2024-10-21");

        Assert.Equal("official", SystemAiConfigurationSnapshotStore.EndpointFingerprint(official));

        string fingerprint = SystemAiConfigurationSnapshotStore.EndpointFingerprint(custom);
        Assert.StartsWith("custom-", fingerprint);
        Assert.Equal("custom-".Length + 12, fingerprint.Length);
        Assert.DoesNotContain(";", fingerprint);
        Assert.DoesNotContain("=", fingerprint);

        string described = SystemAiConfigurationSnapshotStore.DescribeEndpoint(custom);
        Assert.Contains("api-version 2024-10-21", described);
        Assert.DoesNotContain("example.com", described);
    }
}
