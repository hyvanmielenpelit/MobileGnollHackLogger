using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using GnollHackServer.Data.Security;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.EntityFrameworkCore;
using MobileGnollHackLogger.Data;
using Xunit;

namespace Overseer.Tests.UnitTests;

/// <summary>
/// The email that tells administrators a deletion awaits review: its per-user throttle across all of the
/// user's requests, when it is not sent, and what its body carries and never carries.
/// </summary>
public class AccountDeletionReviewEmailTests
{
    private static readonly DateTime Now = new(2026, 10, 14, 12, 0, 0, DateTimeKind.Utc);

    private sealed class CapturingSender : IEmailSender
    {
        public List<(string To, string Subject, string Body)> Sent { get; } = new();

        public Task SendEmailAsync(string email, string subject, string htmlMessage)
        {
            Sent.Add((email, subject, htmlMessage));
            return Task.CompletedTask;
        }
    }

    private sealed class FailingSender : IEmailSender
    {
        public Task SendEmailAsync(string email, string subject, string htmlMessage) =>
            throw new InvalidOperationException("The mail service is unreachable.");
    }

    private static ApplicationDbContext NewDb()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static AccountDeletionRequest Request(string userId, string userName,
        AccountDeletionRequestStatus status = AccountDeletionRequestStatus.Pending, DateTime? lastEmail = null) => new()
    {
        AspNetUserId = userId,
        UserNameSnapshot = userName,
        EmailSnapshot = userName + "@example.com",
        RequestedUtc = Now.AddMinutes(-5),
        Status = status,
        HoldReasons = "Manual hold placed by admin-name on 2026-10-14: secret investigation details",
        LastReviewEmailUtc = lastEmail
    };

    private static AccountHoldOptions Options(string? address = "review@example.com") => new()
    {
        ReviewNotificationEmail = address,
        ReviewEmailMinIntervalHours = 6,
        OverseerAdminUrl = "https://overseer.example/admin"
    };

    [Fact]
    public void ShouldSendWhenNoEmailWasSent()
    {
        Assert.True(AccountDeletionReviewEmail.ShouldSend(null, Now, TimeSpan.FromHours(6)));
    }

    [Fact]
    public void ShouldNotSendFiveHoursFiftyNineMinutesAfterTheLastEmail()
    {
        Assert.False(AccountDeletionReviewEmail.ShouldSend(Now.AddHours(-6).AddMinutes(1), Now, TimeSpan.FromHours(6)));
    }

    [Fact]
    public void ShouldSendSixHoursAfterTheLastEmail()
    {
        Assert.True(AccountDeletionReviewEmail.ShouldSend(Now.AddHours(-6), Now, TimeSpan.FromHours(6)));
    }

    [Fact]
    public async Task TheLastEmailOfAnOlderRequestOfTheSameUserCounts()
    {
        using var db = NewDb();
        db.AccountDeletionRequests.Add(Request("user-a", "alice", AccountDeletionRequestStatus.Declined, lastEmail: Now.AddHours(-2)));
        var again = Request("user-a", "alice");
        db.AccountDeletionRequests.Add(again);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var sender = new CapturingSender();

        bool sent = await AccountDeletionReviewEmail.SendIfDueAsync(db, again, sender, Options(), manual: true, automatic: false,
            Now, ct: TestContext.Current.CancellationToken);

        Assert.False(sent);
        Assert.Empty(sender.Sent);
        Assert.Null(again.LastReviewEmailUtc);
    }

    [Fact]
    public async Task AnotherUsersEmailNeverCounts()
    {
        using var db = NewDb();
        db.AccountDeletionRequests.Add(Request("user-b", "bob", lastEmail: Now.AddMinutes(-10)));
        var request = Request("user-a", "alice");
        db.AccountDeletionRequests.Add(request);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var sender = new CapturingSender();

        bool sent = await AccountDeletionReviewEmail.SendIfDueAsync(db, request, sender, Options(), manual: false, automatic: true,
            Now, ct: TestContext.Current.CancellationToken);

        Assert.True(sent);
        var email = Assert.Single(sender.Sent);
        Assert.Equal("review@example.com", email.To);
        Assert.Equal(Now, request.LastReviewEmailUtc);

        var stored = await db.AccountDeletionRequests.AsNoTracking()
            .SingleAsync(r => r.Id == request.Id, TestContext.Current.CancellationToken);
        Assert.Equal(Now, stored.LastReviewEmailUtc);
    }

    [Fact]
    public async Task AnOlderEmailOutsideTheIntervalAllowsANewOne()
    {
        using var db = NewDb();
        db.AccountDeletionRequests.Add(Request("user-a", "alice", AccountDeletionRequestStatus.Declined, lastEmail: Now.AddHours(-6)));
        var again = Request("user-a", "alice");
        db.AccountDeletionRequests.Add(again);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var sender = new CapturingSender();

        bool sent = await AccountDeletionReviewEmail.SendIfDueAsync(db, again, sender, Options(), manual: true, automatic: false,
            Now, ct: TestContext.Current.CancellationToken);

        Assert.True(sent);
        Assert.Single(sender.Sent);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task AnEmptyReviewAddressNeverSends(string? address)
    {
        using var db = NewDb();
        var request = Request("user-a", "alice");
        db.AccountDeletionRequests.Add(request);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var sender = new CapturingSender();

        bool sent = await AccountDeletionReviewEmail.SendIfDueAsync(db, request, sender, Options(address), manual: true, automatic: false,
            Now, ct: TestContext.Current.CancellationToken);

        Assert.False(sent);
        Assert.Empty(sender.Sent);
        Assert.Null(request.LastReviewEmailUtc);
    }

    [Fact]
    public async Task NoSenderNeverSends()
    {
        using var db = NewDb();
        var request = Request("user-a", "alice");
        db.AccountDeletionRequests.Add(request);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        bool sent = await AccountDeletionReviewEmail.SendIfDueAsync(db, request, null, Options(), manual: true, automatic: false,
            Now, ct: TestContext.Current.CancellationToken);

        Assert.False(sent);
        Assert.Null(request.LastReviewEmailUtc);
    }

    [Fact]
    public async Task AFailingSenderLeavesTheStampUnchangedAndReportsTheError()
    {
        using var db = NewDb();
        var request = Request("user-a", "alice");
        db.AccountDeletionRequests.Add(request);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var errors = new List<Exception>();

        bool sent = await AccountDeletionReviewEmail.SendIfDueAsync(db, request, new FailingSender(), Options(), manual: true,
            automatic: false, Now, errors.Add, TestContext.Current.CancellationToken);

        Assert.False(sent);
        Assert.Null(request.LastReviewEmailUtc);
        var error = Assert.Single(errors);
        Assert.IsType<InvalidOperationException>(error);

        var stored = await db.AccountDeletionRequests.AsNoTracking()
            .SingleAsync(r => r.Id == request.Id, TestContext.Current.CancellationToken);
        Assert.Null(stored.LastReviewEmailUtc);
    }

    [Fact]
    public async Task TheSentEmailNeverCarriesTheHoldReasons()
    {
        using var db = NewDb();
        var request = Request("user-a", "alice");
        db.AccountDeletionRequests.Add(request);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var sender = new CapturingSender();

        await AccountDeletionReviewEmail.SendIfDueAsync(db, request, sender, Options(), manual: true, automatic: true,
            Now, ct: TestContext.Current.CancellationToken);

        var email = Assert.Single(sender.Sent);
        Assert.DoesNotContain("secret investigation details", email.Body);
        Assert.DoesNotContain("secret investigation details", email.Subject);
        Assert.Contains("alice", email.Body);
        Assert.Contains("manual hold and automatic rule", email.Body);
        Assert.Contains("tab=users&amp;subtab=deletions", email.Body);
    }

    [Fact]
    public void ComposeHtmlEncodesTheUserName()
    {
        var (subject, body) = AccountDeletionReviewEmail.Compose("<b>evil&co</b>", Now, manual: true, automatic: false,
            "https://overseer.example/admin");

        Assert.Contains("&lt;b&gt;evil&amp;co&lt;/b&gt;", body);
        Assert.DoesNotContain("<b>evil", body);
        Assert.Contains("<b>evil&co</b>", subject);
    }

    [Theory]
    [InlineData(true, false, "(manual hold)")]
    [InlineData(false, true, "(automatic rule)")]
    [InlineData(true, true, "(manual hold and automatic rule)")]
    [InlineData(false, false, "(hold)")]
    public void ComposeNamesTheHoldType(bool manual, bool automatic, string expected)
    {
        var (_, body) = AccountDeletionReviewEmail.Compose("alice", Now, manual, automatic, "https://overseer.example/admin");

        Assert.Contains(expected, body);
    }

    [Fact]
    public void ComposeLinksToTheDeletionRequestsSubTab()
    {
        var (_, body) = AccountDeletionReviewEmail.Compose("alice", Now, manual: true, automatic: false, "https://overseer.example/admin");

        Assert.Contains("href=\"https://overseer.example/admin?tab=users&amp;subtab=deletions\"", body);
        Assert.Contains("2026-10-14 12:00", body);
    }

    [Fact]
    public void ComposeWithoutAnAdminUrlHasNoLink()
    {
        var (_, body) = AccountDeletionReviewEmail.Compose("alice", Now, manual: true, automatic: false, "");

        Assert.DoesNotContain("<a ", body);
    }

    [Theory]
    [InlineData("https://overseer.example/admin", "https://overseer.example/admin?tab=users&subtab=deletions")]
    [InlineData(" https://overseer.example/admin ", "https://overseer.example/admin?tab=users&subtab=deletions")]
    [InlineData("https://overseer.example/admin?lang=en", "https://overseer.example/admin?lang=en&tab=users&subtab=deletions")]
    public void ReviewLinkAddsTheDeletionsSubTab(string adminUrl, string expected)
    {
        Assert.Equal(expected, AccountDeletionReviewEmail.ReviewLink(adminUrl));
    }
}
