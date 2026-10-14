using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MobileGnollHackLogger.Data;
using Overseer.Models;
using Overseer.Services.Security;
using Xunit;

namespace Overseer.Tests.UnitTests;

/// <summary>
/// The Bulk Actions wizard: the preview's narrowing rule, refusals, cap and order; every refusal of execute; the
/// advanced accounts; and the runner, driven synchronously, re-checking each account, honoring a cancel after the
/// current account, stopping after consecutive failures, and marking interrupted runs at startup.
/// </summary>
public sealed class AdminBulkUserOperationTests : IDisposable
{
    private readonly AdminUserTestHost _host = new();

    public void Dispose() => _host.Dispose();

    private static AdminUserCriteria NeverSignedIn => new() { LastSignIn = "never" };

    private Task<AdminResult<AdminBulkPreviewDto>> PreviewAsync(AdminBulkActionKind action, AdminUserCriteria criteria,
        string? reason = "Cleanup", DateTimeOffset? until = null) =>
        _host.Bulk.PreviewAsync(_host.Actor, new AdminBulkPreviewRequest
        {
            Action = action,
            Criteria = criteria,
            Options = new AdminBulkOptionsDto { Reason = reason, Until = until }
        }, TestContext.Current.CancellationToken);

    private Task<ApplicationUser> AddUnconfirmedAsync(string name, int registeredDaysAgo = 200) =>
        _host.AddUserAsync("id-" + name, name, u =>
        {
            u.EmailConfirmed = false;
            u.CreatedUtc = _host.NowUtc.AddDays(-registeredDaysAgo);
        });

    private async Task<AdminBulkOperation> ReadOperationAsync(long id)
    {
        using var scope = _host.Provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.AdminBulkOperations.AsNoTracking().SingleAsync(o => o.Id == id, TestContext.Current.CancellationToken);
    }

    private async Task<List<AdminBulkOperationItem>> ReadItemsAsync(long id)
    {
        using var scope = _host.Provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.AdminBulkOperationItems.AsNoTracking().Where(i => i.OperationId == id).OrderBy(i => i.Id)
            .ToListAsync(TestContext.Current.CancellationToken);
    }

    private static AdminBulkExecuteRequest DeleteRequest(int count, bool includeAdvanced = false, params string[] excluded) => new()
    {
        ExcludedUserIds = excluded.ToList(),
        IncludeAdvanced = includeAdvanced,
        Acknowledgments = includeAdvanced
            ? new List<string> { AdminBulkAcknowledgments.Reviewed, AdminBulkAcknowledgments.Irreversible, AdminBulkAcknowledgments.Advanced }
            : new List<string> { AdminBulkAcknowledgments.Reviewed, AdminBulkAcknowledgments.Irreversible },
        ConfirmationPhrase = AdminBulkUserOperationService.RequiredPhrase(count)
    };

    // --- Preview ---

    [Theory]
    [InlineData(null, null, null)]
    [InlineData(null, 29, null)]
    [InlineData(null, null, "olderThanDays:29")]
    [InlineData(null, null, "withinDays:400")]
    [InlineData("yes", null, null)]
    public async Task PreviewRefusesADeleteWithoutANarrowingCriterion(string? emailConfirmed, int? olderThanDays, string? lastSignIn)
    {
        await _host.SeedAdministratorsAsync();

        var result = await PreviewAsync(AdminBulkActionKind.Delete, new AdminUserCriteria
        {
            EmailConfirmed = emailConfirmed,
            RegisteredOlderThanDays = olderThanDays,
            LastSignIn = lastSignIn
        });

        Assert.Equal(AdminResultStatus.BadRequest, result.Status);
        Assert.Equal("narrowing", result.Code);
        Assert.Equal(AdminBulkUserOperationService.DeleteNarrowingMessage, result.Message);
    }

    [Theory]
    [InlineData("no", null, null)]
    [InlineData(null, 30, null)]
    [InlineData(null, null, "never")]
    [InlineData(null, null, "olderThanDays:30")]
    public void EachNarrowingCriterionAllowsADelete(string? emailConfirmed, int? olderThanDays, string? lastSignIn)
    {
        Assert.True(AdminBulkUserOperationService.NarrowsDeletion(new AdminUserCriteria
        {
            EmailConfirmed = emailConfirmed,
            RegisteredOlderThanDays = olderThanDays,
            LastSignIn = lastSignIn
        }));
    }

    [Fact]
    public async Task PreviewClassifiesEveryRefusalReason()
    {
        await _host.SeedAdministratorsAsync();
        var ct = TestContext.Current.CancellationToken;
        var held = await AddUnconfirmedAsync("held");
        await _host.AddHoldAsync(held.Id);
        var pending = await AddUnconfirmedAsync("pending");
        await _host.AddPendingRequestAsync(pending.Id, "pending");
        var gamer = await AddUnconfirmedAsync("gamer");
        await _host.AddGameLogAsync(gamer.Id);
        var chatter = await AddUnconfirmedAsync("chatter");
        await _host.AddChatAsync(chatter.Id);
        var bencher = await AddUnconfirmedAsync("bencher");
        _host.Db.BenchmarkRunGroups.Add(new BenchmarkRunGroup { Name = "Group", CreatedByUserId = bencher.Id });
        await _host.Db.SaveChangesAsync(ct);
        await AddUnconfirmedAsync("newbie", registeredDaysAgo: 2);
        await _host.AddUserAsync("id-nodate", "nodate", u => { u.EmailConfirmed = false; u.CreatedUtc = null; });
        var plain = await AddUnconfirmedAsync("plain");
        var confirmed = await _host.AddUserAsync("id-confirmed", "confirmed");

        var result = await PreviewAsync(AdminBulkActionKind.Delete, NeverSignedIn);

        Assert.Equal(AdminResultStatus.Ok, result.Status);
        var preview = result.Value!;
        var refused = preview.Refused.ToDictionary(g => g.Code, g => g.ExampleUserNames.OrderBy(n => n).ToArray());
        Assert.Equal(new[] { "boss" }, refused[AdminRefusalCodes.Self]);
        Assert.Equal(new[] { "deputy" }, refused[AdminRefusalCodes.Administrator]);
        Assert.Equal(new[] { "held" }, refused[AdminRefusalCodes.Hold]);
        Assert.Equal(new[] { "pending" }, refused[AdminRefusalCodes.PendingRequest]);
        Assert.Equal(new[] { "gamer" }, refused["game-data"]);
        Assert.Equal(new[] { "bencher", "chatter" }, refused["overseer-data"]);
        Assert.Equal(new[] { "newbie", "nodate" }, refused["too-new"]);
        Assert.Equal(9, preview.RefusedCount);

        Assert.Equal(plain.Id, Assert.Single(preview.Eligible).UserId);
        Assert.Equal(confirmed.Id, Assert.Single(preview.Advanced).UserId);
        Assert.Equal("DELETE 1 ACCOUNT", preview.RequiredPhrase);
        Assert.Equal("danger", preview.DangerLevel);
        Assert.Equal(new[] { AdminBulkAcknowledgments.Reviewed, AdminBulkAcknowledgments.Irreversible }, preview.RequiredAcknowledgments);

        var op = await ReadOperationAsync(preview.OperationId);
        Assert.Equal(AdminBulkOperationStatus.Previewed, op.Status);
        Assert.Equal(_host.NowUtc.AddMinutes(_host.Options.BulkPreviewMinutes), op.ExpiresUtc);
        var items = await ReadItemsAsync(op.Id);
        Assert.Equal(new[] { (plain.Id, false), (confirmed.Id, true) }.OrderBy(t => t.Item1),
            items.Select(i => (i.UserId, i.IsAdvanced)).OrderBy(t => t.Item1));
    }

    [Fact]
    public async Task PreviewRefusesARecentSignIn()
    {
        await _host.SeedAdministratorsAsync();
        await _host.AddUserAsync("id-recent", "recent", u => { u.EmailConfirmed = false; u.LastSignInUtc = _host.NowUtc.AddDays(-45); });
        var stale = await _host.AddUserAsync("id-stale", "stale", u => { u.EmailConfirmed = false; u.LastSignInUtc = _host.NowUtc.AddDays(-120); });

        var result = await PreviewAsync(AdminBulkActionKind.Delete, new AdminUserCriteria { LastSignIn = "olderThanDays:30" });

        var group = Assert.Single(result.Value!.Refused);
        Assert.Equal("recent-sign-in", group.Code);
        Assert.Equal(new[] { "recent" }, group.ExampleUserNames);
        Assert.Equal(stale.Id, Assert.Single(result.Value.Eligible).UserId);
    }

    [Fact]
    public async Task PreviewCapsAtTheMaximumOldestFirstAndStatesTheRest()
    {
        await _host.SeedAdministratorsAsync();
        _host.Options.BulkMaxUsers = 3;
        foreach (int age in new[] { 20, 50, 10, 40, 30 })
        {
            await _host.AddUserAsync("id-cap" + age, "cap" + age, u => u.CreatedUtc = _host.NowUtc.AddDays(-age));
        }

        var result = await PreviewAsync(AdminBulkActionKind.SignOutEverywhere, new AdminUserCriteria { Search = "cap" }, reason: null);

        Assert.Equal(AdminResultStatus.Ok, result.Status);
        Assert.Equal(new[] { "cap50", "cap40", "cap30" }, result.Value!.Eligible.Select(i => i.UserName));
        Assert.Equal(2, result.Value.TruncatedCount);
        Assert.Equal(5, result.Value.MatchedCount);
        Assert.Equal("safe", result.Value.DangerLevel);
        Assert.Empty(result.Value.RequiredAcknowledgments);
    }

    [Fact]
    public async Task PreviewCountsAccountsTheActionWouldNotChangeAsAlreadyDone()
    {
        await _host.SeedAdministratorsAsync();
        await _host.AddUserAsync("id-free", "free");
        var locked = await _host.AddUserAsync("id-locked", "locked", u => u.LockoutEnd = _host.Time.Now.AddDays(1));

        var result = await PreviewAsync(AdminBulkActionKind.Unlock, new AdminUserCriteria { Search = "e" }, reason: null);

        Assert.Equal(1, result.Value!.NoOpCount);
        Assert.Equal(locked.Id, Assert.Single(result.Value.Eligible).UserId);
    }

    [Fact]
    public async Task PreviewOfADeleteIsRefusedWhileTheErasurePathsAreMissing()
    {
        using var host = new AdminUserTestHost(erasurePaths: false);
        await host.SeedAdministratorsAsync();

        var result = await host.Bulk.PreviewAsync(host.Actor, new AdminBulkPreviewRequest
        {
            Action = AdminBulkActionKind.Delete,
            Criteria = NeverSignedIn,
            Options = new AdminBulkOptionsDto { Reason = "Cleanup" }
        }, TestContext.Current.CancellationToken);

        Assert.Equal(AdminResultStatus.Conflict, result.Status);
        Assert.Equal(AdminRefusalCodes.ErasurePaths, result.Code);
    }

    // --- Execute ---

    private async Task<AdminBulkPreviewDto> TwoAccountDeletePreviewAsync()
    {
        await _host.SeedAdministratorsAsync();
        await AddUnconfirmedAsync("first");
        await AddUnconfirmedAsync("second");
        var result = await PreviewAsync(AdminBulkActionKind.Delete, NeverSignedIn);
        Assert.Equal(2, result.Value!.EligibleCount);
        return result.Value;
    }

    [Fact]
    public async Task ExecuteRefusesAnotherAdministrator()
    {
        var preview = await TwoAccountDeletePreviewAsync();

        var result = await _host.Bulk.ExecuteAsync(new AdminActor(AdminUserTestHost.OtherAdminId, AdminUserTestHost.OtherAdminName),
            preview.OperationId, DeleteRequest(2), TestContext.Current.CancellationToken);

        Assert.Equal(AdminResultStatus.Forbidden, result.Status);
        Assert.Empty(_host.Queue.Queued);
    }

    [Fact]
    public async Task ExecuteRefusesAnExpiredPreview()
    {
        var preview = await TwoAccountDeletePreviewAsync();
        _host.Time.Now = _host.Time.Now.AddMinutes(_host.Options.BulkPreviewMinutes + 1);

        var result = await _host.Bulk.ExecuteAsync(_host.Actor, preview.OperationId, DeleteRequest(2), TestContext.Current.CancellationToken);

        Assert.Equal(AdminResultStatus.Conflict, result.Status);
        Assert.Equal("expired", result.Code);
        Assert.Equal(AdminBulkOperationStatus.Expired, (await ReadOperationAsync(preview.OperationId)).Status);
    }

    [Fact]
    public async Task ExecuteRefusesWhileAnotherOperationRuns()
    {
        var preview = await TwoAccountDeletePreviewAsync();
        _host.Db.AdminBulkOperations.Add(new AdminBulkOperation
        {
            ActorUserId = AdminUserTestHost.OtherAdminId,
            ActorUserName = AdminUserTestHost.OtherAdminName,
            Action = AdminBulkActionKind.SignOutEverywhere,
            Status = AdminBulkOperationStatus.Running,
            CreatedUtc = _host.NowUtc,
            ExpiresUtc = _host.NowUtc.AddMinutes(15)
        });
        await _host.Db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await _host.Bulk.ExecuteAsync(_host.Actor, preview.OperationId, DeleteRequest(2), TestContext.Current.CancellationToken);

        Assert.Equal(AdminResultStatus.Conflict, result.Status);
        Assert.Equal("busy", result.Code);
    }

    [Theory]
    [InlineData("DELETE 1 ACCOUNTS")]
    [InlineData("delete 2 accounts")]
    [InlineData("DELETE ACCOUNTS")]
    [InlineData("")]
    public async Task ExecuteRefusesAWrongPhrase(string phrase)
    {
        var preview = await TwoAccountDeletePreviewAsync();
        var request = DeleteRequest(2);
        request.ConfirmationPhrase = phrase;

        var result = await _host.Bulk.ExecuteAsync(_host.Actor, preview.OperationId, request, TestContext.Current.CancellationToken);

        Assert.Equal(AdminResultStatus.BadRequest, result.Status);
        Assert.Equal("phrase", result.Code);
    }

    [Fact]
    public async Task ThePhraseFollowsTheCountAfterExclusions()
    {
        var preview = await TwoAccountDeletePreviewAsync();
        string excluded = preview.Eligible[0].UserId;

        var result = await _host.Bulk.ExecuteAsync(_host.Actor, preview.OperationId, DeleteRequest(1, false, excluded), TestContext.Current.CancellationToken);

        Assert.Equal(AdminResultStatus.Ok, result.Status);
        Assert.Equal(AdminBulkOperationStatus.Queued, result.Value!.Status);
        Assert.Equal(new[] { preview.OperationId }, _host.Queue.Queued);
        Assert.True((await ReadItemsAsync(preview.OperationId)).Single(i => i.UserId == excluded).Excluded);
    }

    [Theory]
    [InlineData(AdminBulkAcknowledgments.Reviewed)]
    [InlineData(AdminBulkAcknowledgments.Irreversible)]
    public async Task ExecuteRefusesAMissingAcknowledgment(string missing)
    {
        var preview = await TwoAccountDeletePreviewAsync();
        var request = DeleteRequest(2);
        request.Acknowledgments!.Remove(missing);

        var result = await _host.Bulk.ExecuteAsync(_host.Actor, preview.OperationId, request, TestContext.Current.CancellationToken);

        Assert.Equal(AdminResultStatus.BadRequest, result.Status);
        Assert.Equal(AdminRefusalCodes.Acknowledgments, result.Code);
    }

    [Fact]
    public async Task ExecuteRefusesAnExcludedIdOutsideTheItems()
    {
        var preview = await TwoAccountDeletePreviewAsync();

        var result = await _host.Bulk.ExecuteAsync(_host.Actor, preview.OperationId, DeleteRequest(2, false, "id-not-in-preview"),
            TestContext.Current.CancellationToken);

        Assert.Equal(AdminResultStatus.BadRequest, result.Status);
        Assert.Equal("excluded", result.Code);
    }

    // --- Advanced accounts ---

    private async Task<(AdminBulkPreviewDto Preview, string PlainId, string ConfirmedId)> AdvancedPreviewAsync()
    {
        await _host.SeedAdministratorsAsync();
        var plain = await AddUnconfirmedAsync("plain");
        var confirmed = await _host.AddUserAsync("id-confirmed", "confirmed");
        var preview = (await PreviewAsync(AdminBulkActionKind.Delete, NeverSignedIn)).Value!;
        return (preview, plain.Id, confirmed.Id);
    }

    [Fact]
    public async Task IncludingTheAdvancedAccountsNeedsItsAcknowledgment()
    {
        var (preview, _, _) = await AdvancedPreviewAsync();
        var request = DeleteRequest(2, includeAdvanced: true);
        request.Acknowledgments!.Remove(AdminBulkAcknowledgments.Advanced);

        var result = await _host.Bulk.ExecuteAsync(_host.Actor, preview.OperationId, request, TestContext.Current.CancellationToken);

        Assert.Equal(AdminRefusalCodes.Acknowledgments, result.Code);
    }

    [Fact]
    public async Task WithoutIncludeAdvancedTheConfirmedAccountIsExcludedAndKept()
    {
        var (preview, plainId, confirmedId) = await AdvancedPreviewAsync();

        var executed = await _host.Bulk.ExecuteAsync(_host.Actor, preview.OperationId, DeleteRequest(1), TestContext.Current.CancellationToken);
        await _host.Runner.ProcessOperationAsync(preview.OperationId, TestContext.Current.CancellationToken);

        Assert.Equal(AdminResultStatus.Ok, executed.Status);
        Assert.Null(await _host.ReadUserAsync(plainId));
        Assert.NotNull(await _host.ReadUserAsync(confirmedId));
        var op = await ReadOperationAsync(preview.OperationId);
        Assert.Equal(AdminBulkOperationStatus.Completed, op.Status);
        Assert.False(op.IncludedAdvanced);
        Assert.Equal(1, op.ChangedCount);
        Assert.True((await ReadItemsAsync(op.Id)).Single(i => i.UserId == confirmedId).Excluded);

        var row = Assert.Single(await _host.AuditRowsAsync(plainId));
        Assert.Equal(AdminUserActionKind.Deleted, row.Kind);
        Assert.Equal(op.Id, row.BulkOperationId);
        Assert.Equal("plain", row.SubjectUserName);
    }

    [Fact]
    public async Task WithIncludeAdvancedAndItsAcknowledgmentBothAreDeleted()
    {
        var (preview, plainId, confirmedId) = await AdvancedPreviewAsync();

        var executed = await _host.Bulk.ExecuteAsync(_host.Actor, preview.OperationId, DeleteRequest(2, includeAdvanced: true),
            TestContext.Current.CancellationToken);
        await _host.Runner.ProcessOperationAsync(preview.OperationId, TestContext.Current.CancellationToken);

        Assert.Equal(AdminResultStatus.Ok, executed.Status);
        Assert.Null(await _host.ReadUserAsync(plainId));
        Assert.Null(await _host.ReadUserAsync(confirmedId));
        Assert.True((await ReadOperationAsync(preview.OperationId)).IncludedAdvanced);
    }

    // --- The runner ---

    [Fact]
    public async Task AUserWhoSignedInAfterThePreviewIsSkippedAsChanged()
    {
        var preview = await TwoAccountDeletePreviewAsync();
        await _host.Bulk.ExecuteAsync(_host.Actor, preview.OperationId, DeleteRequest(2), TestContext.Current.CancellationToken);
        using (var scope = _host.Provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var first = await db.Users.OfType<ApplicationUser>().SingleAsync(u => u.Id == "id-first", TestContext.Current.CancellationToken);
            first.LastSignInUtc = _host.NowUtc;
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await _host.Runner.ProcessOperationAsync(preview.OperationId, TestContext.Current.CancellationToken);

        Assert.NotNull(await _host.ReadUserAsync("id-first"));
        Assert.Null(await _host.ReadUserAsync("id-second"));
        var items = await ReadItemsAsync(preview.OperationId);
        var skipped = items.Single(i => i.UserId == "id-first");
        Assert.Equal(AdminBulkItemOutcome.Skipped, skipped.Outcome);
        Assert.StartsWith(AdminBulkUserOperationService.ChangedSincePreview, skipped.Message);
        Assert.Equal(AdminBulkItemOutcome.Changed, items.Single(i => i.UserId == "id-second").Outcome);
        var op = await ReadOperationAsync(preview.OperationId);
        Assert.Equal((1, 1, 0), (op.ChangedCount, op.SkippedCount, op.FailedCount));
        Assert.Empty(await _host.AuditRowsAsync("id-first"));
    }

    [Fact]
    public async Task ABulkDeletionClosesTheIncognitoSessionsOfEveryDeletedAccountAndNoOtherAccounts()
    {
        var preview = await TwoAccountDeletePreviewAsync();
        var firstA = _host.OpenIncognito("id-first");
        var firstB = _host.OpenIncognito("id-first");
        var second = _host.OpenIncognito("id-second");
        var bystander = _host.OpenIncognito(AdminUserTestHost.OtherAdminId);

        var executed = await _host.Bulk.ExecuteAsync(_host.Actor, preview.OperationId, DeleteRequest(2), TestContext.Current.CancellationToken);
        await _host.Runner.ProcessOperationAsync(preview.OperationId, TestContext.Current.CancellationToken);

        Assert.Equal(AdminResultStatus.Ok, executed.Status);
        Assert.Null(await _host.ReadUserAsync("id-first"));
        Assert.Null(await _host.ReadUserAsync("id-second"));
        Assert.Null(_host.Ephemeral.Get(firstA.Ref, "id-first"));
        Assert.Null(_host.Ephemeral.Get(firstB.Ref, "id-first"));
        Assert.Null(_host.Ephemeral.Get(second.Ref, "id-second"));
        Assert.True(firstA.IsErased);
        Assert.True(firstB.IsErased);
        Assert.True(second.IsErased);
        Assert.NotNull(_host.Ephemeral.Get(bystander.Ref, AdminUserTestHost.OtherAdminId));
        Assert.False(bystander.IsErased);
    }

    private async Task<long> QueuedBanOfThreeAsync()
    {
        await _host.SeedAdministratorsAsync();
        foreach (string name in new[] { "ban1", "ban2", "ban3" })
        {
            await _host.AddUserAsync("id-" + name, name);
        }
        var preview = (await PreviewAsync(AdminBulkActionKind.Ban, new AdminUserCriteria { Search = "ban" }, reason: "Cheating ring")).Value!;
        Assert.Equal(3, preview.EligibleCount);
        Assert.Equal("caution", preview.DangerLevel);
        var executed = await _host.Bulk.ExecuteAsync(_host.Actor, preview.OperationId, new AdminBulkExecuteRequest
        {
            Acknowledgments = new List<string> { AdminBulkAcknowledgments.Reviewed }
        }, TestContext.Current.CancellationToken);
        Assert.Equal(AdminResultStatus.Ok, executed.Status);
        return preview.OperationId;
    }

    [Fact]
    public async Task ACancelStopsTheRunAfterTheCurrentAccount()
    {
        long opId = await QueuedBanOfThreeAsync();
        // The first ban's security event arrives while that account is being processed.
        _host.Events.OnRecord = entry =>
        {
            if (entry.Kind != SecurityEventKind.AdminBanned) return;
            using var scope = _host.Provider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.AdminBulkOperations.Where(o => o.Id == opId)
                .ExecuteUpdate(s => s.SetProperty(o => o.Status, AdminBulkOperationStatus.CancelRequested));
        };

        await _host.Runner.ProcessOperationAsync(opId, TestContext.Current.CancellationToken);

        var op = await ReadOperationAsync(opId);
        Assert.Equal(AdminBulkOperationStatus.Canceled, op.Status);
        Assert.Equal(1, op.ChangedCount);
        var items = await ReadItemsAsync(opId);
        Assert.Equal(1, items.Count(i => i.Outcome == AdminBulkItemOutcome.Changed));
        Assert.Equal(2, items.Count(i => i.Outcome == AdminBulkItemOutcome.Pending));
        Assert.Single(_host.Events.Entries);
    }

    [Fact]
    public async Task ABanRunRecordsTheBeforeAndAfterOfEachAccount()
    {
        long opId = await QueuedBanOfThreeAsync();

        await _host.Runner.ProcessOperationAsync(opId, TestContext.Current.CancellationToken);

        var op = await ReadOperationAsync(opId);
        Assert.Equal(AdminBulkOperationStatus.Completed, op.Status);
        Assert.Equal(3, op.ChangedCount);
        Assert.All(await ReadItemsAsync(opId), i =>
        {
            Assert.Equal(AdminBulkItemOutcome.Changed, i.Outcome);
            Assert.Equal("In good standing", i.BeforeStatus);
            Assert.Equal("Banned", i.AfterStatus);
        });
        Assert.True((await _host.ReadUserAsync("id-ban2"))!.IsBanned);
        Assert.Equal(opId, Assert.Single(await _host.AuditRowsAsync("id-ban2")).BulkOperationId);
    }

    [Fact]
    public async Task FiveConsecutiveFailuresStopTheRun()
    {
        await _host.SeedAdministratorsAsync();
        for (int i = 1; i <= 7; i++)
        {
            await _host.AddUserAsync("id-lock" + i, "lock" + i);
        }
        var preview = (await PreviewAsync(AdminBulkActionKind.LockOut, new AdminUserCriteria { Search = "lock" },
            reason: "Investigation", until: _host.Time.Now.AddMinutes(2))).Value!;
        await _host.Bulk.ExecuteAsync(_host.Actor, preview.OperationId, new AdminBulkExecuteRequest
        {
            Acknowledgments = new List<string> { AdminBulkAcknowledgments.Reviewed }
        }, TestContext.Current.CancellationToken);
        // By the time the run starts, the lockout's end has passed, so every account fails validation.
        _host.Time.Now = _host.Time.Now.AddMinutes(3);

        await _host.Runner.ProcessOperationAsync(preview.OperationId, TestContext.Current.CancellationToken);

        var op = await ReadOperationAsync(preview.OperationId);
        Assert.Equal(AdminBulkOperationStatus.Completed, op.Status);
        Assert.Equal(5, op.FailedCount);
        Assert.Equal(2, (await ReadItemsAsync(op.Id)).Count(i => i.Outcome == AdminBulkItemOutcome.Pending));
        Assert.Equal("Stopped after 5 consecutive failures.", AdminBulkUserOperationService.ReadOptions(op).StopMessage);

        using var scope = _host.Provider.CreateScope();
        var dto = await scope.ServiceProvider.GetRequiredService<AdminBulkUserOperationService>()
            .GetAsync(op.Id, "failed", 0, 50, TestContext.Current.CancellationToken);
        Assert.Equal(5, dto.Value!.ItemsTotalCount);
        Assert.Equal(2, dto.Value.PendingCount);
        Assert.Equal("Stopped after 5 consecutive failures.", dto.Value.StopMessage);
    }

    [Fact]
    public async Task AtStartupARunningOrQueuedOperationBecomesInterrupted()
    {
        await _host.SeedAdministratorsAsync();
        AdminBulkOperation Op(AdminBulkOperationStatus status) => new()
        {
            ActorUserId = AdminUserTestHost.ActorId,
            ActorUserName = AdminUserTestHost.ActorName,
            Action = AdminBulkActionKind.SignOutEverywhere,
            Status = status,
            CreatedUtc = _host.NowUtc,
            ExpiresUtc = _host.NowUtc.AddMinutes(15)
        };
        var running = Op(AdminBulkOperationStatus.Running);
        var queued = Op(AdminBulkOperationStatus.Queued);
        var completed = Op(AdminBulkOperationStatus.Completed);
        _host.Db.AdminBulkOperations.AddRange(running, queued, completed);
        await _host.Db.SaveChangesAsync(TestContext.Current.CancellationToken);

        int marked = await _host.Runner.RecoverInterruptedAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, marked);
        Assert.Equal(AdminBulkOperationStatus.Interrupted, (await ReadOperationAsync(running.Id)).Status);
        Assert.Equal(AdminBulkOperationStatus.Interrupted, (await ReadOperationAsync(queued.Id)).Status);
        Assert.Equal(AdminBulkOperationStatus.Completed, (await ReadOperationAsync(completed.Id)).Status);
    }

    [Fact]
    public async Task CancelingAPreviewDiscardsIt()
    {
        var preview = await TwoAccountDeletePreviewAsync();

        var canceled = await _host.Bulk.CancelAsync(preview.OperationId, TestContext.Current.CancellationToken);
        var executed = await _host.Bulk.ExecuteAsync(_host.Actor, preview.OperationId, DeleteRequest(2), TestContext.Current.CancellationToken);

        Assert.Equal(AdminBulkOperationStatus.Canceled, canceled.Value!.Status);
        Assert.Equal("not-previewed", executed.Code);
    }

    [Fact]
    public async Task TheReportNeutralizesAFormulaInAUserName()
    {
        await _host.SeedAdministratorsAsync();
        await _host.AddUserAsync("id-formula", "=cmd", u => u.CreatedUtc = _host.NowUtc.AddDays(-30));
        var preview = (await PreviewAsync(AdminBulkActionKind.SignOutEverywhere, new AdminUserCriteria { Search = "=cmd" }, reason: null)).Value!;

        var csv = await _host.Bulk.ReportCsvAsync(preview.OperationId, TestContext.Current.CancellationToken);

        Assert.Contains("\"'=cmd\"", csv.Value);
        Assert.DoesNotContain(",\"=cmd\"", csv.Value);
    }
}
