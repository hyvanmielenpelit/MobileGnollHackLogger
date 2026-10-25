using System.Collections.Concurrent;
using Microsoft.AspNetCore.Identity.UI.Services;

namespace MobileGnollHackLogger.Tests.Helpers;

/// <summary>An <see cref="IEmailSender"/> that records every message instead of sending it.</summary>
public sealed class RecordingEmailSender : IEmailSender
{
    private readonly ConcurrentQueue<(string Email, string Subject, string HtmlMessage)> _sent = new();

    public IReadOnlyList<(string Email, string Subject, string HtmlMessage)> Sent => _sent.ToArray();

    public Task SendEmailAsync(string email, string subject, string htmlMessage)
    {
        _sent.Enqueue((email, subject, htmlMessage));
        return Task.CompletedTask;
    }
}
