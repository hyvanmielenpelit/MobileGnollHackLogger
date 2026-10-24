namespace Overseer.Services.Security;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MobileGnollHackLogger.Data;
using Overseer.Models;
using Overseer.Services.Privacy;
using Overseer.Services.Tools;

/// <summary>Which game text a scanned input is; it decides the channel name and the texts the player and the model get.</summary>
public enum SnapshotGuardSubject
{
    /// <summary>The snapshot uploaded with a session or attached from the chat.</summary>
    Snapshot,

    /// <summary>A snapshot <c>refresh_snapshot</c> fetched.</summary>
    Refresh,

    /// <summary>The in-game message history.</summary>
    History,

    /// <summary>The client's directory manifest.</summary>
    Manifest,

    /// <summary>Another client tool's result.</summary>
    Client,
}

/// <summary>One text to scan, the channel its finding is recorded under, and what it is.</summary>
public sealed record SnapshotGuardInput(string Channel, SnapshotGuardSubject Subject, SnapshotGuardTextKind Kind, string? Text);

/// <summary>The scan of one input. <see cref="Withheld"/> is true when the caller must not store or deliver it.</summary>
public sealed record SnapshotGuardItem(SnapshotGuardInput Input, SnapshotGuardVerdict Verdict, bool Withheld);

/// <summary>
/// What a caller does with the texts it handed in. <see cref="Refuse"/> is true when any is withheld; the code, the
/// player's text and the model's notice are those of the strongest withheld item.
/// </summary>
public sealed record SnapshotGuardDecision(
    bool Refuse,
    string? Code,
    SnapshotGuardSubject? Subject,
    string? UserMessage,
    string? ModelNotice,
    IReadOnlyList<SnapshotGuardItem> Items)
{
    public static readonly SnapshotGuardDecision Allow = new(false, null, null, null, null, Array.Empty<SnapshotGuardItem>());

    /// <summary>Whether the input of <paramref name="channel"/> must not be stored or delivered.</summary>
    public bool IsWithheld(string channel) => Items.Any(i => i.Withheld && i.Input.Channel == channel);

    /// <summary>The value of <see cref="ChatSession.SnapshotRefusalCode"/>: the code, and the subject when it is not the snapshot.</summary>
    public string? SessionCode => Code == null ? null : SnapshotGuardService.SessionCodeOf(Code, Subject ?? SnapshotGuardSubject.Snapshot);
}

/// <summary>
/// The snapshot guard: scans the game texts the client supplies before they are stored or reach the model, records
/// one content-free <see cref="SecurityEventKind.SnapshotRejected"/> event per finding request, refuses in
/// <see cref="SnapshotGuardMode.Enforce"/>, and suspends a player whose own text addressed the AI on
/// <see cref="SnapshotGuardOptions.AutoBlockDistinctDays"/> separate days. The mode is read from
/// <see cref="SnapshotGuardModeService"/> on every call. Singleton.
/// </summary>
/// <remarks>
/// Detection is heuristic (<see cref="SnapshotGuardScanner"/>): the guard bounds injected game text, it does not
/// prevent it. Only the codes of a finding are stored, never the text.
/// </remarks>
public sealed class SnapshotGuardService
{
    /// <summary>The code a refused client tool result and the attach endpoint report.</summary>
    public const string RefusedErrorCode = "snapshot_refused";

    /// <summary>The chat event that tells the player a client result was withheld; its data is <c>{ code, message }</c>.</summary>
    public const string RefusedChatEventType = "snapshot_refused";

    public const string InjectionOwn = "injection-own";
    public const string InjectionForeign = "injection-foreign";
    public const string SuspiciousOwn = "suspicious-own";
    public const string SuspiciousForeign = "suspicious-foreign";

    /// <summary>Prefixes an event's detail in <see cref="SnapshotGuardMode.Observe"/>.</summary>
    public const string ObservedPrefix = "observed;";

    /// <summary>
    /// The first line of every notice the model gets in place of a withheld text. Not "Game snapshot …", which
    /// <c>ChatService.IsGameSnapshotMessage</c> would read as a board.
    /// </summary>
    public const string NoticePrefix = "Snapshot guard notice:";

    /// <summary>The notice row stored for a withheld session snapshot.</summary>
    public const string ModelNotice =
        NoticePrefix + "\nOverseer withheld the game snapshot uploaded for this session because it contained text addressed to an AI assistant. "
        + "No board is available unless a later snapshot is accepted. If the player asks about their current situation, say that the "
        + "snapshot was not loaded and that the chat shows why. Do not guess at the board's contents.";

    public const string AutoBlockReason =
        "Suspended automatically: game snapshots containing instructions addressed to the AI assistant were uploaded on 3 separate days. "
        + "An administrator has been notified and can lift the suspension.";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly SnapshotGuardModeService _mode;
    private readonly OverseerAccessService _access;
    private readonly IConfiguration _configuration;
    private readonly ISecurityEventRecorder? _securityEvents;
    private readonly ILogger<SnapshotGuardService> _logger;
    private readonly IServiceProvider _services;
    private readonly TimeProvider _time;
    private IReadOnlyCollection<string>? _toolNames;

    public SnapshotGuardService(IServiceScopeFactory scopeFactory, SnapshotGuardModeService mode, OverseerAccessService access,
        IConfiguration configuration, ILogger<SnapshotGuardService> logger, IServiceProvider services, ISecurityEventRecorder? securityEvents = null)
        : this(scopeFactory, mode, access, configuration, logger, services, securityEvents, TimeProvider.System)
    {
    }

    internal SnapshotGuardService(IServiceScopeFactory scopeFactory, SnapshotGuardModeService mode, OverseerAccessService access,
        IConfiguration configuration, ILogger<SnapshotGuardService> logger, IServiceProvider services, ISecurityEventRecorder? securityEvents,
        TimeProvider time)
    {
        _scopeFactory = scopeFactory;
        _mode = mode;
        _access = access;
        _configuration = configuration;
        _logger = logger;
        _services = services;
        _securityEvents = securityEvents;
        _time = time;
    }

    /// <summary>The configured options, read on each call.</summary>
    public SnapshotGuardOptions Options => SnapshotGuardOptions.Read(_configuration);

    /// <summary>The mode in force now.</summary>
    public SnapshotGuardMode Mode => _mode.Mode();

    /// <summary>
    /// Scans <paramref name="inputs"/> for <paramref name="userId"/>, records the finding, and in
    /// <see cref="SnapshotGuardMode.Enforce"/> withholds every input with one and runs the automatic block.
    /// <see cref="SnapshotGuardDecision.Allow"/> in <see cref="SnapshotGuardMode.Off"/> and for clean texts.
    /// </summary>
    public async Task<SnapshotGuardDecision> EvaluateAsync(string? userId, IReadOnlyList<SnapshotGuardInput> inputs, CancellationToken ct = default)
    {
        var mode = _mode.Mode();
        if (mode == SnapshotGuardMode.Off) return SnapshotGuardDecision.Allow;

        var toolNames = ToolNames();
        var found = new List<(SnapshotGuardInput Input, SnapshotGuardVerdict Verdict)>();
        foreach (var input in inputs)
        {
            if (string.IsNullOrWhiteSpace(input.Text)) continue;
            var verdict = SnapshotGuardScanner.Scan(input.Text, input.Kind, toolNames);
            if (verdict.Level != SnapshotGuardLevel.Clean) found.Add((input, verdict));
        }
        if (found.Count == 0) return SnapshotGuardDecision.Allow;

        bool enforce = mode == SnapshotGuardMode.Enforce;
        var strongest = found.OrderByDescending(f => Rank(f.Verdict)).First();
        string code = CodeOf(strongest.Verdict);
        string detail = (enforce ? string.Empty : ObservedPrefix)
            + string.Join(";", found.Select(f => f.Input.Channel + ":" + string.Join(",", f.Verdict.Codes)));
        if (detail.Length > 256) detail = detail[..256];

        _securityEvents?.Record(new SecurityEventEntry(SecurityEventKind.SnapshotRejected, UserId: userId, Code: code, Detail: detail));

        var items = found.Select(f => new SnapshotGuardItem(f.Input, f.Verdict, Withheld: enforce)).ToList();
        if (!enforce)
        {
            return new SnapshotGuardDecision(false, code, strongest.Input.Subject, null, null, items);
        }

        if (code == InjectionOwn && !string.IsNullOrEmpty(userId)) await TryBlockAsync(userId, ct);

        return new SnapshotGuardDecision(true, code, strongest.Input.Subject,
            UserMessageFor(code, strongest.Input.Subject, Options.AutoBlockEnabled), ModelNoticeFor(strongest.Input.Subject), items);
    }

    /// <summary>Sets the session's refusal columns; persistent through a scope, ephemeral on the store's template.</summary>
    public async Task SetRefusalAsync(SessionRef session, string? userId, string sessionCode, CancellationToken ct = default)
        => await WriteRefusalAsync(session, userId, _time.GetUtcNow().UtcDateTime, sessionCode, ct);

    /// <summary>Clears the session's refusal columns, once a later snapshot is accepted.</summary>
    public async Task ClearRefusalAsync(SessionRef session, string? userId, CancellationToken ct = default)
        => await WriteRefusalAsync(session, userId, null, null, ct);

    private async Task WriteRefusalAsync(SessionRef session, string? userId, DateTime? at, string? code, CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            if (session.IsEphemeral)
            {
                var store = scope.ServiceProvider.GetService<EphemeralSessionStore>();
                var ephemeral = store?.Get(session, userId);
                if (ephemeral != null)
                {
                    ephemeral.Session.SnapshotRefusedUtc = at;
                    ephemeral.Session.SnapshotRefusalCode = code;
                }
                return;
            }
            if (!session.IsPersistent) return;

            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            long id = session.PersistentId;
            var row = await db.ChatSession.FirstOrDefaultAsync(s => s.Id == id, ct);
            if (row == null || (at == null && row.SnapshotRefusedUtc == null)) return;
            row.SnapshotRefusedUtc = at;
            row.SnapshotRefusalCode = code;
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (!ex.IsCanceledBy(ct))
        {
            _logger.LogWarning(ex, "Could not record the snapshot guard's refusal state on a session.");
        }
    }

    // --- The automatic block ---

    private async Task TryBlockAsync(string userId, CancellationToken ct)
    {
        var options = Options;
        if (!options.AutoBlockEnabled) return;
        try
        {
            DateTime today = _time.GetUtcNow().UtcDateTime.Date;
            DateTime windowStart = today.AddDays(-(options.AutoBlockWindowDays - 1));
            List<DateTime> priorDays;
            using (var scope = _scopeFactory.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var times = await db.SecurityEvents.AsNoTracking()
                    .Where(e => e.AspNetUserId == userId && e.Kind == SecurityEventKind.SnapshotRejected && e.Code == InjectionOwn
                        && e.OccurredUtc >= windowStart && (e.Detail == null || !e.Detail.StartsWith(ObservedPrefix)))
                    .Select(e => e.OccurredUtc)
                    .ToListAsync(ct);
                priorDays = times.Select(t => t.Date).ToList();
            }

            // The recorder is asynchronous, so the refusal being decided is added here rather than read back.
            int days = priorDays.Append(today).Distinct().Count();
            if (days < options.AutoBlockDistinctDays) return;

            string reason = options.AutoBlockDistinctDays == 3
                ? AutoBlockReason
                : AutoBlockReason.Replace("on 3 separate days", $"on {options.AutoBlockDistinctDays} separate days", StringComparison.Ordinal);
            if (await _access.BlockAutomaticallyAsync(userId, reason, ct))
            {
                _logger.LogWarning("The snapshot guard suspended a user from the AI chat after injection-level refusals on {Days} days.", days);
                await NotifyAsync(userId, options, ct);
            }
        }
        catch (Exception ex) when (!ex.IsCanceledBy(ct))
        {
            _logger.LogWarning(ex, "The snapshot guard could not evaluate the automatic block.");
        }
    }

    private async Task NotifyAsync(string userId, SnapshotGuardOptions options, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(options.NotificationEmail)) return;

        string? userName;
        using (var scope = _scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            userName = await db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => u.UserName).FirstOrDefaultAsync(ct);
        }

        IEmailSender? sender;
        try
        {
            sender = _services.GetService<IEmailSender>();
        }
        catch (Exception)
        {
            sender = null;
        }
        if (sender == null)
        {
            _logger.LogWarning("No e-mail sender is configured; the automatic suspension is reported by the admin alert only.");
            return;
        }

        var (subject, body) = ComposeBlockEmail(userName ?? userId, _time.GetUtcNow().UtcDateTime);
        try
        {
            await sender.SendEmailAsync(options.NotificationEmail, subject, body);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "The automatic suspension e-mail could not be sent.");
        }
    }

    /// <summary>The operator e-mail of an automatic block: the user name, the time and where to review it; never the text or the codes.</summary>
    internal static (string Subject, string HtmlBody) ComposeBlockEmail(string userName, DateTime occurredUtc)
    {
        string name = WebUtility.HtmlEncode(userName);
        string at = occurredUtc.ToString("yyyy-MM-dd HH:mm 'UTC'", System.Globalization.CultureInfo.InvariantCulture);
        return ("Overseer: a user was suspended from the AI chat automatically",
            $"<p>The Overseer user <strong>{name}</strong> was blocked automatically from the AI chat at {at} after repeated game snapshots addressed to the AI.</p>"
            + "<p>Review the events and lift the suspension if it is a mistake in Overseer, Admin &gt; Security &gt; Events (/admin/security/events).</p>");
    }

    // --- Codes and texts ---

    private static int Rank(SnapshotGuardVerdict verdict) => (verdict.Level, verdict.PlayerAuthored) switch
    {
        (SnapshotGuardLevel.Injection, true) => 4,
        (SnapshotGuardLevel.Injection, false) => 3,
        (SnapshotGuardLevel.Suspicious, true) => 2,
        _ => 1
    };

    public static string CodeOf(SnapshotGuardVerdict verdict) => (verdict.Level, verdict.PlayerAuthored) switch
    {
        (SnapshotGuardLevel.Injection, true) => InjectionOwn,
        (SnapshotGuardLevel.Injection, false) => InjectionForeign,
        (SnapshotGuardLevel.Suspicious, true) => SuspiciousOwn,
        _ => SuspiciousForeign
    };

    public static string SessionCodeOf(string code, SnapshotGuardSubject subject)
        => subject == SnapshotGuardSubject.Snapshot ? code : code + ":" + subject.ToString().ToLowerInvariant();

    /// <summary>Splits a <see cref="ChatSession.SnapshotRefusalCode"/> into its code and subject.</summary>
    public static (string Code, SnapshotGuardSubject Subject) ParseSessionCode(string sessionCode)
    {
        int colon = sessionCode.IndexOf(':');
        if (colon < 0) return (sessionCode, SnapshotGuardSubject.Snapshot);
        string subject = sessionCode[(colon + 1)..];
        var parsed = Enum.GetValues<SnapshotGuardSubject>().FirstOrDefault(s => string.Equals(s.ToString(), subject, StringComparison.OrdinalIgnoreCase));
        return (sessionCode[..colon], parsed);
    }

    /// <summary>The player's text for a stored session code, as the chat's banner shows it.</summary>
    public string UserMessageForSession(string sessionCode)
    {
        var (code, subject) = ParseSessionCode(sessionCode);
        return UserMessageFor(code, subject, Options.AutoBlockEnabled);
    }

    /// <summary>The player's text for a refusal: what was not loaded, where the text is, and what to do.</summary>
    public static string UserMessageFor(string code, SnapshotGuardSubject subject, bool autoBlockEnabled)
    {
        string what = subject switch
        {
            SnapshotGuardSubject.Refresh => "The updated game snapshot",
            SnapshotGuardSubject.History => "The message history",
            SnapshotGuardSubject.Manifest => "The game's file list",
            SnapshotGuardSubject.Client => "The game data",
            _ => "The game snapshot"
        };
        string without = subject is SnapshotGuardSubject.Snapshot or SnapshotGuardSubject.Refresh ? "the snapshot" : "it";
        bool board = subject is SnapshotGuardSubject.Snapshot or SnapshotGuardSubject.Refresh;
        string own = board ? "in a part of the board you write yourself (a name, a label or a note)" : "in data your game sent";
        string foreign = subject == SnapshotGuardSubject.History
            ? "in the game's messages, which another player's bones level can leave behind (an engraving or a named monster)"
            : "in a part of the board another player's bones level can leave behind (a named monster or a message)";

        return code switch
        {
            InjectionOwn => $"{what} was not loaded. It contains text written as instructions to an AI assistant, {own}. Rename or remove it and reconnect. "
                + $"The chat works without {without}. An administrator has been notified"
                + (autoBlockEnabled ? "; uploading such text on separate days suspends AI chat access automatically." : "."),
            InjectionForeign => $"{what} was not loaded. It contains text written as instructions to an AI assistant, {foreign}. It may not be yours; "
                + $"leaving the level removes it. The chat works without {without}. An administrator has been notified.",
            SuspiciousOwn => $"{what} was not loaded. It may contain text written as instructions to an AI assistant, {own}. If it is a name or note you wrote, "
                + $"rename it and reconnect. The chat works without {without}. An administrator has been notified.",
            _ => $"{what} was not loaded. It may contain text written as instructions to an AI assistant, {foreign}. If it is a name or note you wrote, "
                + $"rename it and reconnect. The chat works without {without}. An administrator has been notified."
        };
    }

    /// <summary>What the model is told in place of a withheld text.</summary>
    public static string ModelNoticeFor(SnapshotGuardSubject subject) => subject switch
    {
        SnapshotGuardSubject.Refresh => NoticePrefix + "\nOverseer withheld the updated game snapshot because it contained text addressed to an AI assistant. "
            + "Use the board you already have, if any, say that it may be out of date, and do not guess at what changed. The chat shows the player why.",
        SnapshotGuardSubject.History => NoticePrefix + "\nOverseer withheld the game's message history because it contained text addressed to an AI assistant. "
            + "If the player asks about earlier messages, say that the history was not loaded and that the chat shows why.",
        SnapshotGuardSubject.Manifest => NoticePrefix + "\nOverseer withheld the game's directory listing because it contained text addressed to an AI assistant. "
            + "Do not guess at the files it lists.",
        SnapshotGuardSubject.Client => NoticePrefix + "\nOverseer withheld this result from the game because it contained text addressed to an AI assistant. "
            + "Do not guess at its contents; the chat shows the player why.",
        _ => ModelNotice
    };

    /// <summary>The scan inputs of a client tool's result, by tool name.</summary>
    public static SnapshotGuardInput ClientToolInput(string toolName, string? content) => toolName switch
    {
        "refresh_snapshot" => new SnapshotGuardInput("tool-" + toolName, SnapshotGuardSubject.Refresh, SnapshotGuardTextKind.Snapshot, content),
        "get_full_message_history" => new SnapshotGuardInput("tool-" + toolName, SnapshotGuardSubject.History, SnapshotGuardTextKind.MessageHistory, content),
        "get_directory_listing" => new SnapshotGuardInput("tool-" + toolName, SnapshotGuardSubject.Manifest, SnapshotGuardTextKind.Manifest, content),
        _ => new SnapshotGuardInput("tool-" + toolName, SnapshotGuardSubject.Client, SnapshotGuardTextKind.ClientData, content)
    };

    private IReadOnlyCollection<string> ToolNames()
    {
        if (_toolNames != null) return _toolNames;
        try
        {
            var names = _services.GetServices<IToolHandler>().Select(h => h.ToolName).Where(n => !string.IsNullOrWhiteSpace(n)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            _toolNames = names;
            return names;
        }
        catch (Exception)
        {
            return Array.Empty<string>();
        }
    }
}
