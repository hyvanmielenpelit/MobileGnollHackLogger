using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MobileGnollHackLogger.Data;
using Overseer.Services.Agents;
using Overseer.Services.Security;
using Xunit;

namespace Overseer.Tests.UnitTests;

/// <summary>
/// The queued security event recorder: run aggregation, grouping, failure handling and the bounded queue; and
/// <see cref="ProviderRefusalEvents.FromRun"/>, which turns a run's model calls into events.
/// </summary>
public class SecurityEventRecorderTests
{
    private static readonly DateTime T0 = new(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class ThrowingScopeFactory : IServiceScopeFactory
    {
        public IServiceScope CreateScope() => throw new InvalidOperationException("The database is unavailable.");
    }

    private sealed class CollectingRecorder : ISecurityEventRecorder
    {
        public List<SecurityEventEntry> Entries { get; } = new();
        public void Record(SecurityEventEntry entry) => Entries.Add(entry);
    }

    private sealed class Harness : IDisposable
    {
        public required ServiceProvider Provider { get; init; }
        public required SecurityEventRecorder Recorder { get; init; }

        public async Task<List<SecurityEvent>> RowsAsync()
        {
            using var scope = Provider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            return await db.SecurityEvents.AsNoTracking().OrderBy(e => e.Id).ToListAsync(TestContext.Current.CancellationToken);
        }

        public void Dispose()
        {
            Recorder.Dispose();
            Provider.Dispose();
        }
    }

    private static Harness CreateHarness(TimeProvider? time = null)
    {
        string dbName = Guid.NewGuid().ToString();
        var services = new ServiceCollection();
        services.AddDbContext<ApplicationDbContext>(o => o.UseInMemoryDatabase(dbName));
        var provider = services.BuildServiceProvider();
        var recorder = new SecurityEventRecorder(
            provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<SecurityEventRecorder>.Instance, time);
        return new Harness { Provider = provider, Recorder = recorder };
    }

    private static SecurityEventEntry Failed(DateTime at, string? userId = null, string? ip = "203.0.113.7")
        => new(SecurityEventKind.LoginFailed, UserId: userId, IpAddress: ip, OccurredUtc: at);

    [Fact]
    public async Task QueuedEvent_IsWrittenWhenProcessed_WithItsFields()
    {
        var ct = TestContext.Current.CancellationToken;
        using var harness = CreateHarness();

        harness.Recorder.Record(new SecurityEventEntry(
            SecurityEventKind.ProviderRefusal,
            UserId: "user-1",
            IpAddress: "203.0.113.7",
            SystemAiApiConfigurationId: 42,
            Provider: "Anthropic",
            ModelId: "claude-test",
            Code: "refusal",
            Detail: "fixed-detail",
            OccurredUtc: T0));

        Assert.Empty(await harness.RowsAsync());

        await harness.Recorder.ProcessPendingAsync(ct);

        var row = Assert.Single(await harness.RowsAsync());
        Assert.Equal(SecurityEventKind.ProviderRefusal, row.Kind);
        Assert.Equal(1, row.Count);
        Assert.Equal("user-1", row.AspNetUserId);
        Assert.Equal("203.0.113.7", row.IpAddress);
        Assert.Equal(42L, row.SystemAiApiConfigurationId);
        Assert.Equal("Anthropic", row.Provider);
        Assert.Equal("claude-test", row.ModelId);
        Assert.Equal("refusal", row.Code);
        Assert.Equal("fixed-detail", row.Detail);
        Assert.Equal(T0, row.OccurredUtc);
        Assert.Equal(T0, row.LastOccurredUtc);
    }

    [Fact]
    public async Task EventWithoutATime_IsStampedByTheClockWhenRecorded()
    {
        var ct = TestContext.Current.CancellationToken;
        using var harness = CreateHarness(new FixedClock(new DateTimeOffset(T0)));

        harness.Recorder.Record(new SecurityEventEntry(SecurityEventKind.RateLimitRejected, IpAddress: "198.51.100.1"));
        await harness.Recorder.ProcessPendingAsync(ct);

        var row = Assert.Single(await harness.RowsAsync());
        Assert.Equal(T0, row.OccurredUtc);
        Assert.Equal(T0, row.LastOccurredUtc);
    }

    [Fact]
    public async Task TenIdenticalEventsWithinTenMinutes_BecomeOneRowWithCountTen()
    {
        var ct = TestContext.Current.CancellationToken;
        using var harness = CreateHarness();

        for (int i = 0; i < 10; i++)
        {
            harness.Recorder.Record(Failed(T0.AddMinutes(i), userId: "user-1"));
        }
        await harness.Recorder.ProcessPendingAsync(ct);

        var row = Assert.Single(await harness.RowsAsync());
        Assert.Equal(10, row.Count);
        Assert.Equal(T0, row.OccurredUtc);
        Assert.Equal(T0.AddMinutes(9), row.LastOccurredUtc);
    }

    [Fact]
    public async Task IdenticalEventsInALaterBatch_ExtendTheStoredRow()
    {
        var ct = TestContext.Current.CancellationToken;
        using var harness = CreateHarness();

        for (int i = 0; i < 5; i++)
        {
            harness.Recorder.Record(Failed(T0.AddMinutes(i), userId: "user-1"));
        }
        await harness.Recorder.ProcessPendingAsync(ct);

        for (int i = 5; i < 10; i++)
        {
            harness.Recorder.Record(Failed(T0.AddMinutes(i), userId: "user-1"));
        }
        await harness.Recorder.ProcessPendingAsync(ct);

        var row = Assert.Single(await harness.RowsAsync());
        Assert.Equal(10, row.Count);
        Assert.Equal(T0, row.OccurredUtc);
        Assert.Equal(T0.AddMinutes(9), row.LastOccurredUtc);
    }

    [Fact]
    public async Task IdenticalEventPastTheRunWindow_StartsANewRow()
    {
        var ct = TestContext.Current.CancellationToken;
        using var harness = CreateHarness();

        harness.Recorder.Record(Failed(T0, userId: "user-1"));
        harness.Recorder.Record(Failed(T0 + SecurityEventRecorder.RunWindow + TimeSpan.FromMinutes(1), userId: "user-1"));
        await harness.Recorder.ProcessPendingAsync(ct);

        var rows = await harness.RowsAsync();
        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.Equal(1, r.Count));
    }

    [Fact]
    public async Task EventsOfDifferentUsers_AreSeparateRows()
    {
        var ct = TestContext.Current.CancellationToken;
        using var harness = CreateHarness();

        harness.Recorder.Record(Failed(T0, userId: "user-a"));
        harness.Recorder.Record(Failed(T0.AddMinutes(1), userId: "user-b"));
        harness.Recorder.Record(Failed(T0.AddMinutes(2), userId: "user-a"));
        await harness.Recorder.ProcessPendingAsync(ct);

        var rows = await harness.RowsAsync();
        Assert.Equal(2, rows.Count);
        Assert.Equal(2, rows.Single(r => r.AspNetUserId == "user-a").Count);
        Assert.Equal(1, rows.Single(r => r.AspNetUserId == "user-b").Count);
    }

    [Fact]
    public async Task EventsWithoutAUser_GroupByIpAddress()
    {
        var ct = TestContext.Current.CancellationToken;
        using var harness = CreateHarness();

        harness.Recorder.Record(Failed(T0, ip: "203.0.113.7"));
        harness.Recorder.Record(Failed(T0.AddMinutes(1), ip: "198.51.100.1"));
        harness.Recorder.Record(Failed(T0.AddMinutes(2), ip: "203.0.113.7"));
        await harness.Recorder.ProcessPendingAsync(ct);

        var rows = await harness.RowsAsync();
        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.Null(r.AspNetUserId));
        Assert.Equal(2, rows.Single(r => r.IpAddress == "203.0.113.7").Count);
        Assert.Equal(1, rows.Single(r => r.IpAddress == "198.51.100.1").Count);
    }

    [Fact]
    public async Task ProcessPending_WhenTheScopeFactoryThrows_DoesNotThrow()
    {
        var ct = TestContext.Current.CancellationToken;
        using var recorder = new SecurityEventRecorder(new ThrowingScopeFactory(), NullLogger<SecurityEventRecorder>.Instance);

        recorder.Record(Failed(T0, userId: "user-1"));

        await recorder.ProcessPendingAsync(ct);
    }

    [Fact]
    public async Task ProcessPending_WhenTheServiceProviderIsDisposed_DoesNotThrow()
    {
        var ct = TestContext.Current.CancellationToken;
        var services = new ServiceCollection();
        string dbName = Guid.NewGuid().ToString();
        services.AddDbContext<ApplicationDbContext>(o => o.UseInMemoryDatabase(dbName));
        var provider = services.BuildServiceProvider();
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();
        provider.Dispose();
        using var recorder = new SecurityEventRecorder(scopeFactory, NullLogger<SecurityEventRecorder>.Instance);

        recorder.Record(Failed(T0, userId: "user-1"));

        await recorder.ProcessPendingAsync(ct);
    }

    [Fact]
    public async Task QueueOverCapacity_DropsTheOldestEvent()
    {
        var ct = TestContext.Current.CancellationToken;
        using var harness = CreateHarness();

        // One identity, one run: the row's Count and first occurrence show which events survived the queue.
        for (int i = 0; i <= SecurityEventRecorder.QueueCapacity; i++)
        {
            harness.Recorder.Record(Failed(T0.AddMilliseconds(i), userId: "user-1"));
        }

        // A call writes at most 200 events.
        for (int pass = 0; pass < 10; pass++)
        {
            await harness.Recorder.ProcessPendingAsync(ct);
        }

        var row = Assert.Single(await harness.RowsAsync());
        Assert.Equal(SecurityEventRecorder.QueueCapacity, row.Count);
        Assert.Equal(T0.AddMilliseconds(1), row.OccurredUtc);
        Assert.Equal(T0.AddMilliseconds(SecurityEventRecorder.QueueCapacity), row.LastOccurredUtc);
    }

    // --- ProviderRefusalEvents.FromRun --------------------------------------------------------------

    private static ModelCallRecord Call(string provider, bool isRefusal = false, string? finishReason = null,
        ProviderAttemptRecord[]? attempts = null)
        => new()
        {
            Provider = provider,
            RequestedModelId = provider.ToLowerInvariant() + "-model",
            IsRefusal = isRefusal,
            FinishReason = finishReason,
            Attempts = (attempts ?? Array.Empty<ProviderAttemptRecord>()).ToList()
        };

    private static ProviderAttemptRecord Attempt(ProviderAttemptOutcome outcome, int? status, string? code, int index = 0)
        => new() { AttemptIndex = index, Outcome = outcome, HttpStatus = status, ProviderErrorCode = code };

    private static AgentRunResult Run(params ModelCallRecord[] calls) => new() { ModelCalls = calls.ToList() };

    [Fact]
    public void FromRun_AnthropicRefusal_IsAProviderRefusal()
    {
        var events = ProviderRefusalEvents.FromRun(Run(Call("Anthropic", isRefusal: true, finishReason: "refusal")), "user-1", 7);

        var entry = Assert.Single(events);
        Assert.Equal(SecurityEventKind.ProviderRefusal, entry.Kind);
        Assert.Equal("user-1", entry.UserId);
        Assert.Equal(7L, entry.SystemAiApiConfigurationId);
        Assert.Equal("Anthropic", entry.Provider);
        Assert.Equal("anthropic-model", entry.ModelId);
        Assert.Equal("refusal", entry.Code);
    }

    [Fact]
    public void FromRun_GoogleRefusal_IsAProviderSafetyBlock()
    {
        var events = ProviderRefusalEvents.FromRun(Run(Call("Google", isRefusal: true, finishReason: "SAFETY")), "user-1", 7);

        var entry = Assert.Single(events);
        Assert.Equal(SecurityEventKind.ProviderSafetyBlock, entry.Kind);
        Assert.Equal("Google", entry.Provider);
        Assert.Equal("SAFETY", entry.Code);
    }

    [Fact]
    public void FromRun_FinalBadRequestWithAPolicyCode_IsAProviderPolicyRejection()
    {
        var call = Call("OpenAI", attempts: new[] { Attempt(ProviderAttemptOutcome.OurRequest, 400, "invalid_prompt") });

        var events = ProviderRefusalEvents.FromRun(Run(call), "user-1", null);

        var entry = Assert.Single(events);
        Assert.Equal(SecurityEventKind.ProviderPolicyRejection, entry.Kind);
        Assert.Equal("user-1", entry.UserId);
        Assert.Null(entry.SystemAiApiConfigurationId);
        Assert.Equal("OpenAI", entry.Provider);
        Assert.Equal("invalid_prompt", entry.Code);
    }

    [Fact]
    public void FromRun_PolicyCodeOnAnEarlierAttempt_OrANonPolicyCode_RecordsNothing()
    {
        var recovered = Call("OpenAI", attempts: new[]
        {
            Attempt(ProviderAttemptOutcome.OurRequest, 400, "invalid_prompt", index: 0),
            Attempt(ProviderAttemptOutcome.Ok, 200, null, index: 1)
        });
        var plainBadRequest = Call("OpenAI", attempts: new[] { Attempt(ProviderAttemptOutcome.OurRequest, 400, "invalid_request_error") });

        var events = ProviderRefusalEvents.FromRun(Run(recovered, plainBadRequest), "user-1", 7);

        Assert.Empty(events);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void FromRun_WithoutAUser_RecordsNothing(string? userId)
    {
        var run = Run(
            Call("Anthropic", isRefusal: true, finishReason: "refusal"),
            Call("OpenAI", attempts: new[] { Attempt(ProviderAttemptOutcome.OurRequest, 400, "invalid_prompt") }));

        Assert.Empty(ProviderRefusalEvents.FromRun(run, userId, 7));
    }

    [Fact]
    public void Record_PassesEveryEventToTheRecorder_AndToleratesNoRecorder()
    {
        var run = Run(
            Call("Anthropic", isRefusal: true, finishReason: "refusal"),
            Call("OpenAI", attempts: new[] { Attempt(ProviderAttemptOutcome.OurRequest, 400, "invalid_prompt") }));
        var recorder = new CollectingRecorder();

        ProviderRefusalEvents.Record(recorder, run, "user-1", 7);
        ProviderRefusalEvents.Record(null, run, "user-1", 7);

        Assert.Equal(
            new[] { SecurityEventKind.ProviderRefusal, SecurityEventKind.ProviderPolicyRejection },
            recorder.Entries.Select(e => e.Kind));
    }
}
