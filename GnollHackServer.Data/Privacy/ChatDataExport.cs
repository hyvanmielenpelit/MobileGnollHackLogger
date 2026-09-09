using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using MobileGnollHackLogger.Data;

namespace GnollHackServer.Data.Privacy;

/// <summary>One exported conversation.</summary>
public sealed class ExportedChatSession
{
    public long Id { get; set; }
    public string? Title { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime LastMessageUtc { get; set; }
    public bool IsConfidential { get; set; }
    public bool IsPinned { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedUtc { get; set; }
    public string? DeletionReason { get; set; }
    public decimal? TotalEstimatedCost { get; set; }
    public List<ExportedChatMessage> Messages { get; set; } = new();
}

/// <summary>One exported message, with the metadata that stays readable either way.</summary>
public sealed class ExportedChatMessage
{
    public long Id { get; set; }
    public string? Role { get; set; }
    public string? Content { get; set; }
    public DateTime TimestampUtc { get; set; }
    public string? ProviderUsed { get; set; }
    public string? ModelUsed { get; set; }
    public int? InputTokens { get; set; }
    public int? OutputTokens { get; set; }
    public decimal? EstimatedCost { get; set; }
    public List<ExportedToolCall> ToolCalls { get; set; } = new();
    public List<ExportedAttachment> Attachments { get; set; } = new();
}

/// <summary>One exported tool call. Payloads are content and are decrypted with the message.</summary>
public sealed class ExportedToolCall
{
    public string? Name { get; set; }
    public string? DisplayName { get; set; }
    public string? Status { get; set; }
    public string? AgentName { get; set; }
    public string? Arguments { get; set; }
    public string? Result { get; set; }
    public string? Error { get; set; }
}

/// <summary>
/// One exported attachment's metadata. The bytes are not in the export.
/// </summary>
/// <remarks>
/// Deliberately metadata only. Inlining a 15 MB image as base64 into a JSON file makes the export
/// unusable in the tools people actually open it with, and an attachment is already downloadable
/// one at a time from the conversation. The row says what was attached so nothing is hidden.
/// </remarks>
public sealed class ExportedAttachment
{
    public long Id { get; set; }
    public string? FileName { get; set; }
    public string? ContentType { get; set; }
}

/// <summary>
/// Builds a user's chat history as portable data.
/// </summary>
/// <remarks>
/// <para>
/// A static helper in the data project for the same reason as <see cref="CryptoShred"/> and
/// <see cref="ChatAccessAudit"/>: both web projects need it and neither references the other.
/// </para>
/// <para>
/// <b>Decryption is the caller's, and that split is forced rather than chosen.</b> The keyring
/// lives in Overseer's User Secrets under its own <c>UserSecretsId</c>; the Razor application has
/// a different one and therefore does not hold the key at all. So the Razor export passes no
/// decryptor and a confidential message exports as an explicit notice naming where to get it,
/// while Overseer's export passes <c>ContentProtectionService.Decrypt</c> and is complete. One
/// implementation, two capabilities, and the difference is stated in the file rather than left
/// for the user to notice.
/// </para>
/// </remarks>
public static class ChatDataExport
{
    /// <summary>
    /// What stands in for content the caller cannot decrypt. Written into the export so a reader
    /// can tell an encrypted message from an empty one.
    /// </summary>
    public const string EncryptedPlaceholder =
        "[This message is in a confidential chat and is stored encrypted. "
        + "Export it from Overseer, which holds the key, to get its text.]";

    /// <summary>
    /// Every conversation belonging to <paramref name="userId"/>, including ones in the trash.
    /// </summary>
    /// <param name="decrypt">
    /// Turns a stored value into its plaintext for a given session, or null when the caller has
    /// no key. A value that is not an envelope must pass through unchanged, because an upgraded
    /// conversation legitimately holds both kinds.
    /// </param>
    /// <remarks>
    /// Soft-deleted conversations are included on purpose: they are still the user's data until
    /// the purge removes them, so omitting them would make the export a smaller answer than the
    /// truth.
    /// </remarks>
    public static async Task<List<ExportedChatSession>> BuildAsync(
        ApplicationDbContext dbContext,
        string userId,
        Func<ChatSession, string?, string?>? decrypt = null,
        CancellationToken cancellationToken = default)
    {
        var sessions = await dbContext.ChatSession
            .AsNoTracking()
            .Where(s => s.AspNetUserId == userId)
            .OrderBy(s => s.CreatedUtc)
            .ToListAsync(cancellationToken);

        if (sessions.Count == 0) return new List<ExportedChatSession>();

        var sessionIds = sessions.Select(s => s.Id).ToList();

        var messages = await dbContext.ChatMessage
            .AsNoTracking()
            .Where(m => sessionIds.Contains(m.ChatSessionId))
            .OrderBy(m => m.TimestampUtc)
            .ToListAsync(cancellationToken);

        var messageIds = messages.Select(m => m.Id).ToList();

        var toolCalls = messageIds.Count == 0
            ? new List<ChatMessageToolCall>()
            : await dbContext.ChatMessageToolCall
                .AsNoTracking()
                .Where(tc => messageIds.Contains(tc.ChatMessageId))
                .OrderBy(tc => tc.SortOrder)
                .ToListAsync(cancellationToken);

        var attachments = messageIds.Count == 0
            ? new List<ChatMessageAttachment>()
            : await dbContext.ChatMessageAttachment
                .AsNoTracking()
                .Where(a => messageIds.Contains(a.ChatMessageId))
                .ToListAsync(cancellationToken);

        var toolCallsByMessage = toolCalls.ToLookup(tc => tc.ChatMessageId);
        var attachmentsByMessage = attachments.ToLookup(a => a.ChatMessageId);
        var messagesBySession = messages.ToLookup(m => m.ChatSessionId);

        var exported = new List<ExportedChatSession>(sessions.Count);

        foreach (var session in sessions)
        {
            /* Read once per session rather than per value: whether this caller can decrypt is a
               property of the caller, and whether it needs to is a property of the session. */
            string? Read(string? stored)
            {
                if (!session.IsConfidential) return stored;
                if (decrypt == null) return string.IsNullOrEmpty(stored) ? stored : EncryptedPlaceholder;
                return decrypt(session, stored);
            }

            var exportedSession = new ExportedChatSession
            {
                Id = session.Id,
                Title = Read(session.Title),
                CreatedUtc = session.CreatedUtc,
                LastMessageUtc = session.LastMessageUtc,
                IsConfidential = session.IsConfidential,
                IsPinned = session.IsPinned,
                IsDeleted = session.IsDeleted,
                DeletedUtc = session.DeletedUtc,
                DeletionReason = session.DeletionReason,
                TotalEstimatedCost = session.TotalEstimatedCost
            };

            foreach (var message in messagesBySession[session.Id])
            {
                /* Hidden system messages are included. They are part of what the model was told
                   on the user's behalf, and a portability export that quietly omitted them would
                   answer a narrower question than the one asked. */
                var exportedMessage = new ExportedChatMessage
                {
                    Id = message.Id,
                    Role = message.Role,
                    Content = Read(message.Content),
                    TimestampUtc = message.TimestampUtc,
                    ProviderUsed = message.ProviderUsed,
                    ModelUsed = message.ModelUsed,
                    InputTokens = message.InputTokens,
                    OutputTokens = message.OutputTokens,
                    EstimatedCost = message.EstimatedCost
                };

                foreach (var call in toolCallsByMessage[message.Id])
                {
                    exportedMessage.ToolCalls.Add(new ExportedToolCall
                    {
                        Name = call.Name,
                        DisplayName = call.DisplayName,
                        Status = call.Status,
                        AgentName = call.AgentName,
                        Arguments = Read(call.ArgsText),
                        Result = Read(call.Result),
                        Error = Read(call.Error)
                    });
                }

                foreach (var attachment in attachmentsByMessage[message.Id])
                {
                    exportedMessage.Attachments.Add(new ExportedAttachment
                    {
                        Id = attachment.Id,
                        FileName = Read(attachment.FileName),
                        ContentType = attachment.ContentType
                    });
                }

                exportedSession.Messages.Add(exportedMessage);
            }

            exported.Add(exportedSession);
        }

        return exported;
    }

    /// <summary>
    /// The note that goes beside the conversations, saying what this export does and does not
    /// contain.
    /// </summary>
    /// <remarks>
    /// Part of the export rather than of the page that produced it, so it travels with the file.
    /// Someone reading the JSON a year from now is the person who needs it.
    /// </remarks>
    public static Dictionary<string, string> DescribeExport(bool canDecrypt) => new()
    {
        ["about"] =
            "Your Overseer conversations, in the order they were created. Conversations in the "
            + "trash are included, because they are still yours until they are purged.",
        ["attachments"] =
            "Attachment names and types are listed; the files themselves are not in this export. "
            + "Download them individually from the conversation.",
        ["incognitoChats"] =
            "Incognito chats are absent, and there is nothing to include: they were never stored.",
        ["confidentialChats"] = canDecrypt
            ? "Confidential conversations are decrypted here."
            : "Confidential conversations are stored encrypted and the key is not available to "
              + "this part of the application, so their text is replaced by a notice. Export from "
              + "Overseer to include it.",
        ["whatIsMissing"] =
            "This export covers conversations. Your account details are in the personal-data "
            + "download beside it, and game logs are separate again."
    };
}
