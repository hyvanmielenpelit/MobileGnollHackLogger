using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using GnollHackServer.Data.Privacy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using MobileGnollHackLogger.Data;
using Overseer.Services.Privacy;
using Xunit;

namespace Overseer.Tests.UnitTests;

/// <summary>
/// The portability export: what a user gets, and what they are told about what they did not get.
/// </summary>
/// <remarks>
/// Erasure has always been complete in this application; portability was not — account details
/// were downloadable and the conversations were not, which made the export answer a narrower
/// question than the one being asked.
/// </remarks>
public class ChatDataExportTests
{
    private const string Owner = "export-user";
    private const string Other = "other-user";

    private static ApplicationDbContext NewDb()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static IConfiguration Config()
        => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            { "PrivacySettings:KeyRing:v1", Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) },
            { "PrivacySettings:ActiveKeyVersion", "v1" }
        }).Build();

    private static ContentProtectionService Protection(IConfiguration config)
        => new(new ConfigurationContentKeyRing(config));

    private static async Task<(long SessionId, long MessageId)> SeedAsync(
        ApplicationDbContext db,
        string userId,
        bool confidential,
        ContentProtectionService? protection = null,
        string title = "A chat",
        string userText = "the question I asked")
    {
        var session = new ChatSession
        {
            AspNetUserId = userId,
            Title = title,
            CreatedUtc = DateTime.UtcNow.AddMinutes(-10),
            LastMessageUtc = DateTime.UtcNow,
            IsConfidential = confidential
        };

        db.ChatSession.Add(session);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        /* The key is minted AFTER the session has an id, and the order is not incidental: the
           DEK's associated data is "chatsession:<id>", so wrapping it while the id is still 0
           produces a key that cannot be unwrapped once the database assigns a real one. That is
           the replay protection working, and production never hits it -- the session is saved
           before a turn runs. */
        if (confidential && protection != null)
        {
            protection.EnsureSessionKey(session);
            session.Title = protection.Encrypt(session, title);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var message = new ChatMessage
        {
            ChatSessionId = session.Id,
            Role = "user",
            Content = confidential && protection != null
                ? protection.Encrypt(session, userText)
                : userText,
            TimestampUtc = DateTime.UtcNow.AddMinutes(-9)
        };
        db.ChatMessage.Add(message);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        db.ChatMessageToolCall.Add(new ChatMessageToolCall
        {
            ChatMessageId = message.Id,
            ToolCallId = "call-1",
            Name = "wiki_search",
            Status = "completed",
            SortOrder = 0,
            ArgsText = confidential && protection != null
                ? protection.Encrypt(session, "{\"q\":\"wand of digging\"}")
                : "{\"q\":\"wand of digging\"}",
            Result = confidential && protection != null
                ? protection.Encrypt(session, "found three articles")
                : "found three articles"
        });

        db.ChatMessageAttachment.Add(new ChatMessageAttachment
        {
            ChatMessageId = message.Id,
            FileName = confidential && protection != null
                ? protection.Encrypt(session, "budget.xlsx")
                : "budget.xlsx",
            ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            RelativePath = $"{session.Id}/abc123.xlsx"
        });

        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return (session.Id, message.Id);
    }

    [Fact]
    public async Task AnOrdinaryConversationExportsWithItsContent()
    {
        using var db = NewDb();
        await SeedAsync(db, Owner, confidential: false);

        var exported = await ChatDataExport.BuildAsync(
            db, Owner, cancellationToken: TestContext.Current.CancellationToken);

        var session = Assert.Single(exported);
        Assert.Equal("A chat", session.Title);
        var message = Assert.Single(session.Messages);
        Assert.Equal("the question I asked", message.Content);

        var call = Assert.Single(message.ToolCalls);
        Assert.Equal("wiki_search", call.Name);
        Assert.Contains("wand of digging", call.Arguments!);
        Assert.Equal("found three articles", call.Result);

        // Attachment metadata, not bytes: inlining a 15 MB image as base64 makes the file
        // unusable in the tools people actually open it with.
        var attachment = Assert.Single(message.Attachments);
        Assert.Equal("budget.xlsx", attachment.FileName);
    }

    [Fact]
    public async Task SomebodyElsesConversationIsNotInYourExport()
    {
        using var db = NewDb();
        await SeedAsync(db, Owner, confidential: false, title: "Mine");
        await SeedAsync(db, Other, confidential: false, title: "Theirs");

        var exported = await ChatDataExport.BuildAsync(
            db, Owner, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("Mine", Assert.Single(exported).Title);
    }

    [Fact]
    public async Task AConfidentialConversationExportsAsANoticeWithoutTheKey()
    {
        /* The split is forced rather than chosen: the keyring lives in Overseer's User Secrets
           under its own UserSecretsId, and the Razor application has a different one, so it does
           not hold the key at all. */
        using var db = NewDb();
        var config = Config();
        var protection = Protection(config);
        await SeedAsync(db, Owner, confidential: true, protection: protection, userText: "a private question");

        var exported = await ChatDataExport.BuildAsync(
            db, Owner, decrypt: null, cancellationToken: TestContext.Current.CancellationToken);

        var message = Assert.Single(Assert.Single(exported).Messages);
        Assert.Equal(ChatDataExport.EncryptedPlaceholder, message.Content);
        // And never the ciphertext: an export full of "enc:v1:..." would be worse than the notice.
        Assert.DoesNotContain(ContentProtectionService.RowPrefix, message.Content!);
        Assert.DoesNotContain("a private question", message.Content!);
    }

    [Fact]
    public async Task AConfidentialConversationExportsCompletelyWithTheKey()
    {
        using var db = NewDb();
        var config = Config();
        var protection = Protection(config);
        await SeedAsync(db, Owner, confidential: true, protection: protection,
            title: "Private chat", userText: "a private question");

        var exported = await ChatDataExport.BuildAsync(
            db, Owner,
            decrypt: (session, stored) => protection.Decrypt(session, stored),
            cancellationToken: TestContext.Current.CancellationToken);

        var session = Assert.Single(exported);
        Assert.Equal("Private chat", session.Title);

        var message = Assert.Single(session.Messages);
        Assert.Equal("a private question", message.Content);

        var call = Assert.Single(message.ToolCalls);
        Assert.Contains("wand of digging", call.Arguments!);
        Assert.Equal("found three articles", call.Result);
        Assert.Equal("budget.xlsx", Assert.Single(message.Attachments).FileName);
    }

    [Fact]
    public async Task AnEmptyValueStaysEmptyRatherThanBecomingTheNotice()
    {
        // A message with no content is not an encrypted message, and saying so would be a lie
        // about a row that has nothing in it.
        using var db = NewDb();
        var protection = Protection(Config());

        var session = new ChatSession
        {
            AspNetUserId = Owner, Title = "T", IsConfidential = true,
            CreatedUtc = DateTime.UtcNow, LastMessageUtc = DateTime.UtcNow
        };
        db.ChatSession.Add(session);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        // After the id exists; see the note in SeedAsync.
        protection.EnsureSessionKey(session);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        db.ChatMessage.Add(new ChatMessage
        {
            ChatSessionId = session.Id, Role = "assistant", Content = null,
            TimestampUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var exported = await ChatDataExport.BuildAsync(
            db, Owner, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Null(Assert.Single(Assert.Single(exported).Messages).Content);
    }

    [Fact]
    public async Task ATrashedConversationIsStillExported()
    {
        /* It is still the user's data until the purge removes it, so omitting it would make the
           export a smaller answer than the truth. */
        using var db = NewDb();
        var (sessionId, _) = await SeedAsync(db, Owner, confidential: false, title: "In the trash");

        var session = await db.ChatSession.FindAsync(
            new object[] { sessionId }, TestContext.Current.CancellationToken);
        session!.IsDeleted = true;
        session.DeletedUtc = DateTime.UtcNow;
        session.DeletionReason = "User";
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var exported = await ChatDataExport.BuildAsync(
            db, Owner, cancellationToken: TestContext.Current.CancellationToken);

        var result = Assert.Single(exported);
        Assert.True(result.IsDeleted);
        Assert.Equal("User", result.DeletionReason);
    }

    [Fact]
    public async Task AUserWithNoConversationsGetsAnEmptyListAndNotAFailure()
    {
        using var db = NewDb();

        var exported = await ChatDataExport.BuildAsync(
            db, Owner, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Empty(exported);
    }

    [Fact]
    public void TheExportNoteSaysWhatIsMissingAndWhy()
    {
        /* The note travels with the file rather than living on the page that produced it, because
           the person who needs it is someone reading the JSON a year from now. */
        var withoutKey = ChatDataExport.DescribeExport(canDecrypt: false);
        var withKey = ChatDataExport.DescribeExport(canDecrypt: true);

        Assert.Contains("Export from Overseer", withoutKey["confidentialChats"]);
        Assert.Contains("decrypted here", withKey["confidentialChats"]);

        // Both must say what an incognito chat's absence means, or it reads as data withheld.
        foreach (var note in new[] { withoutKey, withKey })
        {
            Assert.Contains("never stored", note["incognitoChats"]);
            Assert.Contains("not in this export", note["attachments"]);
            Assert.Contains("trash", note["about"]);
        }
    }

    [Fact]
    public async Task MessagesComeBackInTheOrderTheyWereWritten()
    {
        using var db = NewDb();
        var session = new ChatSession
        {
            AspNetUserId = Owner, Title = "Ordered",
            CreatedUtc = DateTime.UtcNow, LastMessageUtc = DateTime.UtcNow
        };
        db.ChatSession.Add(session);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        for (int i = 0; i < 5; i++)
        {
            db.ChatMessage.Add(new ChatMessage
            {
                ChatSessionId = session.Id,
                Role = i % 2 == 0 ? "user" : "assistant",
                Content = $"message {i}",
                TimestampUtc = DateTime.UtcNow.AddMinutes(i)
            });
        }
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var exported = await ChatDataExport.BuildAsync(
            db, Owner, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(
            Enumerable.Range(0, 5).Select(i => $"message {i}"),
            Assert.Single(exported).Messages.Select(m => m.Content));
    }
}
