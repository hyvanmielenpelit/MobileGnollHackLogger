using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using MobileGnollHackLogger.Data;
using Overseer.Services.Security;
using Xunit;

namespace Overseer.Tests.UnitTests;

/// <summary>
/// The status rules of the admin Users tab: the in-memory evaluator and the SQL criteria agree for every status
/// on a fixture of every combination, "In good standing" is exactly the absence of the eight problems, and an
/// estimated registration date never satisfies an age criterion earlier than the true date would.
/// </summary>
public sealed class AdminUserStatusTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 10, 13, 12, 0, 0, DateTimeKind.Utc);

    private readonly ApplicationDbContext _db = new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .Options);

    public void Dispose() => _db.Dispose();

    private IQueryable<ApplicationUser> Users => _db.Users.OfType<ApplicationUser>();

    [Flags]
    private enum Problem
    {
        None = 0,
        LockedOut = 1 << 0,
        Banned = 1 << 1,
        GameLogBanned = 1 << 2,
        BonesBanned = 1 << 3,
        AiChatBlocked = 1 << 4,
        Hold = 1 << 5,
        Pending = 1 << 6,
        Unconfirmed = 1 << 7,
        Administrator = 1 << 8,
        All = (1 << 9) - 1
    }

    private sealed record Fixture(Dictionary<string, Problem> Problems, List<string> AdminNames);

    /// <summary>One account per combination of the nine raw conditions, 512 in all.</summary>
    private async Task<Fixture> SeedEveryCombinationAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        var problems = new Dictionary<string, Problem>();
        var admins = new List<string>();
        for (int i = 0; i <= (int)Problem.All; i++)
        {
            var p = (Problem)i;
            string id = "combo-" + i;
            string name = "combo" + i;
            problems[id] = p;
            if (p.HasFlag(Problem.Administrator)) admins.Add(name.ToUpperInvariant());

            _db.Users.Add(new ApplicationUser
            {
                Id = id,
                UserName = name,
                NormalizedUserName = name.ToUpperInvariant(),
                Email = name + "@example.com",
                NormalizedEmail = (name + "@example.com").ToUpperInvariant(),
                EmailConfirmed = !p.HasFlag(Problem.Unconfirmed),
                // A past lockout end is not a lockout.
                LockoutEnd = p.HasFlag(Problem.LockedOut) ? new DateTimeOffset(Now.AddDays(3)) : new DateTimeOffset(Now.AddDays(-3)),
                IsBanned = p.HasFlag(Problem.Banned) ? true : (i % 2 == 0 ? false : null),
                IsGameLogBanned = p.HasFlag(Problem.GameLogBanned) ? true : null,
                IsBonesBanned = p.HasFlag(Problem.BonesBanned) ? true : false,
                OverseerBlockedUtc = p.HasFlag(Problem.AiChatBlocked) ? Now.AddDays(-1) : null
            });
            if (p.HasFlag(Problem.Hold))
            {
                _db.AccountHolds.Add(new AccountHold { AspNetUserId = id, Reason = "Review", PlacedByUserId = "x", PlacedUtc = Now });
            }
            else
            {
                // A released hold is not a hold.
                _db.AccountHolds.Add(new AccountHold { AspNetUserId = id, Reason = "Old", PlacedByUserId = "x", PlacedUtc = Now.AddDays(-9), ReleasedUtc = Now.AddDays(-8) });
            }
            _db.AccountDeletionRequests.Add(new AccountDeletionRequest
            {
                AspNetUserId = id,
                UserNameSnapshot = name,
                RequestedUtc = Now.AddDays(-1),
                Status = p.HasFlag(Problem.Pending) ? AccountDeletionRequestStatus.Pending : AccountDeletionRequestStatus.Declined,
                HoldReasons = "Manual hold"
            });
        }
        await _db.SaveChangesAsync(ct);
        return new Fixture(problems, admins);
    }

    private async Task<Dictionary<string, AdminUserStatusSet>> EvaluateAllAsync(Fixture fixture)
    {
        var ct = TestContext.Current.CancellationToken;
        var users = await Users.AsNoTracking().ToListAsync(ct);
        var holds = (await _db.AccountHolds.Where(h => h.ReleasedUtc == null).Select(h => h.AspNetUserId).ToListAsync(ct)).ToHashSet();
        var pending = (await _db.AccountDeletionRequests.Where(r => r.Status == AccountDeletionRequestStatus.Pending)
            .Select(r => r.AspNetUserId!).ToListAsync(ct)).ToHashSet();
        return users.ToDictionary(u => u.Id,
            u => AdminUserStatus.Evaluate(u, new AdminUserFacts(holds.Contains(u.Id), pending.Contains(u.Id)), fixture.AdminNames, Now));
    }

    [Fact]
    public async Task TheEvaluatorAndTheSqlCriteriaAgreeForEveryStatus()
    {
        var ct = TestContext.Current.CancellationToken;
        var fixture = await SeedEveryCombinationAsync();
        var evaluated = await EvaluateAllAsync(fixture);

        foreach (var kind in Enum.GetValues<AdminUserStatusKind>())
        {
            var criteria = new AdminUserCriteria { Statuses = new() { kind } };
            var sql = (await AdminUserStatus.Apply(Users.AsNoTracking(), _db, criteria, fixture.AdminNames, Now)
                .Select(u => u.Id).ToListAsync(ct)).ToHashSet();
            var memory = evaluated.Where(e => e.Value.Has(kind)).Select(e => e.Key).ToHashSet();

            Assert.True(sql.SetEquals(memory), $"{kind}: SQL {sql.Count}, in memory {memory.Count}.");
            Assert.NotEmpty(memory);
        }
    }

    [Fact]
    public async Task SeveralStatusesMatchAnyOfThem()
    {
        var ct = TestContext.Current.CancellationToken;
        var fixture = await SeedEveryCombinationAsync();
        var evaluated = await EvaluateAllAsync(fixture);

        var criteria = new AdminUserCriteria { Statuses = new() { AdminUserStatusKind.Banned, AdminUserStatusKind.DeletionHold } };
        var sql = (await AdminUserStatus.Apply(Users.AsNoTracking(), _db, criteria, fixture.AdminNames, Now)
            .Select(u => u.Id).ToListAsync(ct)).ToHashSet();
        var memory = evaluated.Where(e => e.Value.Has(AdminUserStatusKind.Banned) || e.Value.Has(AdminUserStatusKind.DeletionHold))
            .Select(e => e.Key).ToHashSet();

        Assert.True(sql.SetEquals(memory));
    }

    [Fact]
    public async Task InGoodStandingIsExactlyTheAbsenceOfTheEightProblems()
    {
        var fixture = await SeedEveryCombinationAsync();
        var evaluated = await EvaluateAllAsync(fixture);
        const Problem eight = Problem.All & ~Problem.Administrator;

        foreach (var (id, set) in evaluated)
        {
            bool none = (fixture.Problems[id] & eight) == Problem.None;
            Assert.Equal(none, set.Has(AdminUserStatusKind.GoodStanding));
        }
        // With and without the administrator badge.
        Assert.Equal(2, evaluated.Values.Count(s => s.Has(AdminUserStatusKind.GoodStanding)));
    }

    [Fact]
    public async Task EveryProblemShowsItsOwnBadgeAndTheLockoutsEnd()
    {
        await SeedEveryCombinationAsync();
        var user = await Users.AsNoTracking().SingleAsync(u => u.Id == "combo-1", TestContext.Current.CancellationToken);

        var set = AdminUserStatus.Evaluate(user, new AdminUserFacts(true, false), Array.Empty<string>(), Now);

        Assert.Equal(new[] { AdminUserStatusKind.LockedOut, AdminUserStatusKind.DeletionHold }, set.Statuses);
        Assert.Equal(new DateTimeOffset(Now.AddDays(3)), set.LockedOutUntil);
        Assert.Equal("Locked out until 2026-10-16 12:00 UTC, Deletion hold", set.Text);
    }

    [Fact]
    public async Task AnEstimatedRegistrationDateNeverSatisfiesAnAgeCriterionEarlierThanTheTrueDate()
    {
        var ct = TestContext.Current.CancellationToken;
        var trueAges = new Dictionary<string, int>();
        int n = 0;
        foreach (int trueAge in new[] { 3, 6, 7, 8, 29, 30, 31, 60, 364, 365, 366, 900 })
        {
            foreach (int lag in new[] { 0, 1, 5, 40, 400 })
            {
                // The backfill takes the earliest game log or chat: never before the true registration.
                int estimatedAge = Math.Max(0, trueAge - lag);
                string id = "age-" + n++;
                trueAges[id] = trueAge;
                _db.Users.Add(new ApplicationUser
                {
                    Id = id,
                    UserName = id,
                    NormalizedUserName = id.ToUpperInvariant(),
                    CreatedUtc = Now.AddDays(-estimatedAge),
                    CreatedUtcIsEstimate = true
                });
            }
        }
        _db.Users.Add(new ApplicationUser { Id = "no-date", UserName = "no-date", NormalizedUserName = "NO-DATE", CreatedUtc = null });
        await _db.SaveChangesAsync(ct);

        foreach (int days in new[] { 7, 30, 365 })
        {
            var matched = await AdminUserStatus.Apply(Users.AsNoTracking(), _db, new AdminUserCriteria { RegisteredOlderThanDays = days },
                Array.Empty<string>(), Now).Select(u => u.Id).ToListAsync(ct);

            Assert.DoesNotContain("no-date", matched);
            Assert.All(matched, id => Assert.True(trueAges[id] >= days, $"{id} (true age {trueAges[id]}) matched older than {days} days."));
        }
    }

    [Fact]
    public async Task TheSearchMatchesUserNameOrEmailCaseInsensitively()
    {
        var ct = TestContext.Current.CancellationToken;
        _db.Users.AddRange(
            new ApplicationUser { Id = "a", UserName = "Gnollhacker", NormalizedUserName = "GNOLLHACKER", Email = "a@example.com", NormalizedEmail = "A@EXAMPLE.COM" },
            new ApplicationUser { Id = "b", UserName = "other", NormalizedUserName = "OTHER", Email = "gnoll.fan@example.org", NormalizedEmail = "GNOLL.FAN@EXAMPLE.ORG" },
            new ApplicationUser { Id = "c", UserName = "third", NormalizedUserName = "THIRD", Email = "c@example.com", NormalizedEmail = "C@EXAMPLE.COM" });
        await _db.SaveChangesAsync(ct);

        var matched = await AdminUserStatus.Apply(Users.AsNoTracking(), _db, new AdminUserCriteria { Search = " gNoLL " },
            Array.Empty<string>(), Now).Select(u => u.Id).OrderBy(id => id).ToListAsync(ct);

        Assert.Equal(new[] { "a", "b" }, matched);
    }

    [Fact]
    public async Task LastSignInAndDataCriteria()
    {
        var ct = TestContext.Current.CancellationToken;
        _db.Users.AddRange(
            new ApplicationUser { Id = "never", UserName = "never", LastSignInUtc = null },
            new ApplicationUser { Id = "recent", UserName = "recent", LastSignInUtc = Now.AddDays(-10) },
            new ApplicationUser { Id = "old", UserName = "old", LastSignInUtc = Now.AddDays(-200) });
        _db.GameLog.Add(new GameLog { AspNetUserId = "old", CreatedDate = Now });
        _db.ChatSession.Add(new ChatSession { AspNetUserId = "recent", Title = "t", CreatedUtc = Now, LastMessageUtc = Now });
        await _db.SaveChangesAsync(ct);

        async Task<string[]> Match(AdminUserCriteria c) => (await AdminUserStatus.Apply(Users.AsNoTracking(), _db, c, Array.Empty<string>(), Now)
            .Select(u => u.Id).OrderBy(id => id).ToListAsync(ct)).ToArray();

        Assert.Equal(new[] { "never" }, await Match(new AdminUserCriteria { LastSignIn = "never" }));
        Assert.Equal(new[] { "old" }, await Match(new AdminUserCriteria { LastSignIn = "olderThanDays:90" }));
        Assert.Equal(new[] { "recent" }, await Match(new AdminUserCriteria { LastSignIn = "withinDays:30" }));
        Assert.Equal(new[] { "old" }, await Match(new AdminUserCriteria { HasGameData = "yes" }));
        Assert.Equal(new[] { "never", "recent" }, await Match(new AdminUserCriteria { HasGameData = "no" }));
        Assert.Equal(new[] { "recent" }, await Match(new AdminUserCriteria { HasOverseerData = "yes" }));
    }

    [Theory]
    [InlineData("never", true)]
    [InlineData("olderThanDays:30", true)]
    [InlineData("withinDays:7", true)]
    [InlineData("WithinDays:7", true)]
    [InlineData("olderThanDays:", false)]
    [InlineData("olderThanDays:-3", false)]
    [InlineData("sometime", false)]
    public void TheLastSignInCriterionIsParsedStrictly(string value, bool valid)
    {
        Assert.Equal(valid, AdminUserCriteria.TryParseLastSignIn(value, out _));
        Assert.Equal(valid, new AdminUserCriteria { LastSignIn = value }.Validate() == null);
    }

    [Fact]
    public void ATriStateOtherThanYesOrNoIsRefused()
    {
        Assert.Null(new AdminUserCriteria { EmailConfirmed = "yes", HasGameData = "no", HasOverseerData = "any" }.Validate());
        Assert.NotNull(new AdminUserCriteria { EmailConfirmed = "maybe" }.Validate());
        Assert.NotNull(new AdminUserCriteria { RegisteredWithinDays = -1 }.Validate());
    }
}
