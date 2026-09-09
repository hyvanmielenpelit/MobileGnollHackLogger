using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MobileGnollHackLogger.Data;

namespace Overseer.Services.Privacy;

/// <summary>
/// One message of an ephemeral session. Content is held as UTF-8 bytes so the store's own copy
/// can be overwritten on teardown.
/// </summary>
/// <remarks>
/// A <see cref="string"/> would be simpler and is what the rest of the pipeline uses, but the
/// CLR offers no way to overwrite one: it is immutable, it may be interned, and it survives in
/// the heap until a collection that nothing can force. The transient strings a turn creates are
/// unreachable garbage the moment the turn ends; the store's copy is the one that would sit in
/// memory for the whole hour of the sliding timeout, so that is the copy worth being able to
/// erase.
/// </remarks>
public sealed class EphemeralMessage
{
    public long Id { get; init; }
    public string Role { get; init; } = "";
    public bool IsHidden { get; init; }
    public bool IsGameSnapshot { get; init; }
    public bool IsMessageHistory { get; init; }
    public DateTime TimestampUtc { get; init; } = DateTime.UtcNow;

    public string? ProviderUsed { get; init; }
    public string? ModelUsed { get; init; }
    public string? ModelDisplayNameUsed { get; init; }
    public string? ThinkingLevelUsed { get; init; }
    public string? ReasoningModeUsed { get; init; }
    public string? ServiceTierUsed { get; init; }
    public string? ActualServiceTierUsed { get; init; }
    public int? TimeToFirstTokenMs { get; init; }
    public int? TotalDurationMs { get; init; }
    public int? TokensUsed { get; init; }
    public int? ContextPromptTokens { get; init; }
    public int? ContextOutputTokens { get; init; }
    public int? ContextWindowTokens { get; init; }
    public int? ContextInputLimitTokens { get; init; }
    public int? InputTokens { get; init; }
    public int? OutputTokens { get; init; }
    public int? CacheReadTokens { get; init; }
    public int? CacheCreationTokens { get; init; }
    public decimal? EstimatedCost { get; init; }
    public string? PricingSource { get; init; }
    public long? SystemAiConfigurationIdUsed { get; init; }

    private byte[] _content = Array.Empty<byte>();

    public string Content
    {
        get => _content.Length == 0 ? "" : Encoding.UTF8.GetString(_content);
        set => _content = string.IsNullOrEmpty(value) ? Array.Empty<byte>() : Encoding.UTF8.GetBytes(value);
    }

    public List<EphemeralToolCall> ToolCalls { get; } = new();

    /// <summary>
    /// A detached <see cref="ChatMessage"/> carrying this message's values, so the read paths in
    /// <c>ChatService</c> and <c>ChatController</c> work against one shape for both kinds of
    /// session. The instance is never attached to a <c>DbContext</c>.
    /// </summary>
    public ChatMessage ToChatMessage() => new ChatMessage
    {
        Id = Id,
        Role = Role,
        Content = Content,
        IsHidden = IsHidden,
        IsGameSnapshot = IsGameSnapshot,
        IsMessageHistory = IsMessageHistory,
        TimestampUtc = TimestampUtc,
        ProviderUsed = ProviderUsed,
        ModelUsed = ModelUsed,
        ModelDisplayNameUsed = ModelDisplayNameUsed,
        ThinkingLevelUsed = ThinkingLevelUsed,
        ReasoningModeUsed = ReasoningModeUsed,
        ServiceTierUsed = ServiceTierUsed,
        ActualServiceTierUsed = ActualServiceTierUsed,
        TimeToFirstTokenMs = TimeToFirstTokenMs,
        TotalDurationMs = TotalDurationMs,
        TokensUsed = TokensUsed,
        ContextPromptTokens = ContextPromptTokens,
        ContextOutputTokens = ContextOutputTokens,
        ContextWindowTokens = ContextWindowTokens,
        ContextInputLimitTokens = ContextInputLimitTokens,
        InputTokens = InputTokens,
        OutputTokens = OutputTokens,
        CacheReadTokens = CacheReadTokens,
        CacheCreationTokens = CacheCreationTokens,
        EstimatedCost = EstimatedCost,
        PricingSource = PricingSource,
        SystemAiConfigurationIdUsed = SystemAiConfigurationIdUsed
    };

    /// <summary>
    /// A store entry carrying everything a <see cref="ChatMessage"/> the turn built holds,
    /// tool calls included.
    /// </summary>
    /// <remarks>
    /// Copies rather than an overload per subset: <c>ChatMessage</c> has some thirty metadata
    /// columns and a hand-written subset would quietly stop carrying whichever one is added
    /// next, so an ephemeral transcript would lose a timing or a cost that a persisted one
    /// keeps.
    /// </remarks>
    public static EphemeralMessage From(long id, ChatMessage source)
    {
        var message = new EphemeralMessage
        {
            Id = id,
            Role = source.Role ?? "",
            IsHidden = source.IsHidden,
            IsGameSnapshot = source.IsGameSnapshot,
            IsMessageHistory = source.IsMessageHistory,
            TimestampUtc = source.TimestampUtc,
            ProviderUsed = source.ProviderUsed,
            ModelUsed = source.ModelUsed,
            ModelDisplayNameUsed = source.ModelDisplayNameUsed,
            ThinkingLevelUsed = source.ThinkingLevelUsed,
            ReasoningModeUsed = source.ReasoningModeUsed,
            ServiceTierUsed = source.ServiceTierUsed,
            ActualServiceTierUsed = source.ActualServiceTierUsed,
            TimeToFirstTokenMs = source.TimeToFirstTokenMs,
            TotalDurationMs = source.TotalDurationMs,
            TokensUsed = source.TokensUsed,
            ContextPromptTokens = source.ContextPromptTokens,
            ContextOutputTokens = source.ContextOutputTokens,
            ContextWindowTokens = source.ContextWindowTokens,
            ContextInputLimitTokens = source.ContextInputLimitTokens,
            InputTokens = source.InputTokens,
            OutputTokens = source.OutputTokens,
            CacheReadTokens = source.CacheReadTokens,
            CacheCreationTokens = source.CacheCreationTokens,
            EstimatedCost = source.EstimatedCost,
            PricingSource = source.PricingSource,
            SystemAiConfigurationIdUsed = source.SystemAiConfigurationIdUsed,
            Content = source.Content ?? ""
        };

        foreach (var tc in source.ToolCalls)
        {
            message.ToolCalls.Add(EphemeralToolCall.From(tc));
        }

        return message;
    }

    internal void Erase()
    {
        CryptographicOperations.ZeroMemory(_content);
        _content = Array.Empty<byte>();
        foreach (var tc in ToolCalls) tc.Erase();
        ToolCalls.Clear();
    }
}

/// <summary>
/// One tool call of an ephemeral message. Arguments, result and error text are held as UTF-8
/// bytes for the same reason as <see cref="EphemeralMessage.Content"/>; the tool's name, id and
/// status stay strings because they are the tool layer's own identifiers, not the user's
/// content.
/// </summary>
public sealed class EphemeralToolCall
{
    public string? ToolCallId { get; init; }
    public string? Name { get; init; }
    public string? DisplayName { get; init; }
    public string? Status { get; init; }
    public int SortOrder { get; init; }
    public string? AgentName { get; init; }
    public string? ParentToolCallId { get; init; }
    public int Depth { get; init; }
    public int? BatchIndex { get; init; }
    public int? QueueWaitMs { get; init; }
    public int? ExecutionMs { get; init; }

    private byte[] _args = Array.Empty<byte>();
    private byte[] _result = Array.Empty<byte>();
    private byte[] _error = Array.Empty<byte>();

    public string? ArgsText
    {
        get => _args.Length == 0 ? null : Encoding.UTF8.GetString(_args);
        set => _args = string.IsNullOrEmpty(value) ? Array.Empty<byte>() : Encoding.UTF8.GetBytes(value);
    }

    public string? Result
    {
        get => _result.Length == 0 ? null : Encoding.UTF8.GetString(_result);
        set => _result = string.IsNullOrEmpty(value) ? Array.Empty<byte>() : Encoding.UTF8.GetBytes(value);
    }

    public string? Error
    {
        get => _error.Length == 0 ? null : Encoding.UTF8.GetString(_error);
        set => _error = string.IsNullOrEmpty(value) ? Array.Empty<byte>() : Encoding.UTF8.GetBytes(value);
    }

    /// <summary>A detached <see cref="ChatMessageToolCall"/> carrying this call's values.</summary>
    public ChatMessageToolCall ToChatMessageToolCall(long chatMessageId) => new ChatMessageToolCall
    {
        ChatMessageId = chatMessageId,
        ToolCallId = ToolCallId,
        Name = Name,
        DisplayName = DisplayName,
        ArgsText = ArgsText,
        Result = Result,
        Error = Error,
        Status = Status,
        SortOrder = SortOrder,
        AgentName = AgentName,
        ParentToolCallId = ParentToolCallId,
        Depth = Depth,
        BatchIndex = BatchIndex,
        QueueWaitMs = QueueWaitMs,
        ExecutionMs = ExecutionMs
    };

    public static EphemeralToolCall From(ChatMessageToolCall source) => new EphemeralToolCall
    {
        ToolCallId = source.ToolCallId,
        Name = source.Name,
        DisplayName = source.DisplayName,
        Status = source.Status,
        SortOrder = source.SortOrder,
        AgentName = source.AgentName,
        ParentToolCallId = source.ParentToolCallId,
        Depth = source.Depth,
        BatchIndex = source.BatchIndex,
        QueueWaitMs = source.QueueWaitMs,
        ExecutionMs = source.ExecutionMs,
        ArgsText = source.ArgsText,
        Result = source.Result,
        Error = source.Error
    };

    internal void Erase()
    {
        CryptographicOperations.ZeroMemory(_args);
        CryptographicOperations.ZeroMemory(_result);
        CryptographicOperations.ZeroMemory(_error);
        _args = Array.Empty<byte>();
        _result = Array.Empty<byte>();
        _error = Array.Empty<byte>();
    }
}

/// <summary>
/// One attachment of an ephemeral session, held entirely in memory. No file reaches
/// <c>ConversationsDataLocation</c>.
/// </summary>
public sealed class EphemeralAttachment
{
    /// <summary>Per-session, 1-based. Not a database key and never collides with one.</summary>
    public int Id { get; init; }
    public long ChatMessageId { get; init; }
    public string FileName { get; init; } = "";
    public string ContentType { get; init; } = "";

    private byte[] _bytes = Array.Empty<byte>();

    public byte[] Bytes
    {
        get => _bytes;
        init => _bytes = value ?? Array.Empty<byte>();
    }

    internal void Erase()
    {
        CryptographicOperations.ZeroMemory(_bytes);
        _bytes = Array.Empty<byte>();
    }
}

/// <summary>
/// The whole of an ephemeral conversation: its session-shaped settings, its messages and its
/// attachment buffers. Nothing here is ever attached to a <c>DbContext</c> and nothing is
/// written to disk.
/// </summary>
public sealed class EphemeralSession
{
    private readonly object _gate = new();
    private readonly List<EphemeralMessage> _messages = new();
    private readonly List<EphemeralAttachment> _attachments = new();
    private long _nextMessageId;
    private int _nextAttachmentId;

    internal EphemeralSession(Guid token, string userId, ChatSession template)
    {
        Token = token;
        UserId = userId;
        Session = template;
        CreatedUtc = DateTime.UtcNow;
        LastAccessUtc = CreatedUtc;
    }

    public Guid Token { get; }
    public string UserId { get; }
    public SessionRef Ref => SessionRef.Ephemeral(Token);
    public DateTime CreatedUtc { get; }
    public DateTime LastAccessUtc { get; internal set; }
    public bool IsErased { get; private set; }

    /// <summary>
    /// A detached <see cref="ChatSession"/> standing in for the row an ephemeral session does
    /// not have. Its <c>Id</c> stays 0: everything that used to key off a session id now takes a
    /// <see cref="SessionRef"/>, so a zero here can no longer be mistaken for a real key.
    /// </summary>
    public ChatSession Session { get; }

    public IReadOnlyList<EphemeralMessage> Messages
    {
        get { lock (_gate) return _messages.ToList(); }
    }

    public IReadOnlyList<EphemeralAttachment> Attachments
    {
        get { lock (_gate) return _attachments.ToList(); }
    }

    public int MessageCount
    {
        get { lock (_gate) return _messages.Count; }
    }

    /// <summary>
    /// Appends a message. The caller builds it because <see cref="EphemeralMessage"/> carries
    /// the same thirty-odd metadata columns <c>ChatMessage</c> does, and an overload per subset
    /// would drift out of step with it.
    /// </summary>
    public long AddMessage(Func<long, EphemeralMessage> build)
    {
        lock (_gate)
        {
            var id = ++_nextMessageId;
            var message = build(id);
            _messages.Add(message);
            LastAccessUtc = DateTime.UtcNow;
            return id;
        }
    }

    /// <summary>Appends an attachment and assigns it a per-session id.</summary>
    public EphemeralAttachment AddAttachment(long chatMessageId, string fileName, string contentType, byte[] bytes)
    {
        lock (_gate)
        {
            var attachment = new EphemeralAttachment
            {
                Id = ++_nextAttachmentId,
                ChatMessageId = chatMessageId,
                FileName = fileName,
                ContentType = contentType,
                Bytes = bytes
            };
            _attachments.Add(attachment);
            LastAccessUtc = DateTime.UtcNow;
            return attachment;
        }
    }

    public EphemeralAttachment? FindAttachment(int id)
    {
        lock (_gate) return _attachments.FirstOrDefault(a => a.Id == id);
    }

    /// <summary>
    /// Overwrites every buffer this session holds and drops the lists.
    /// </summary>
    /// <remarks>
    /// This is what the mode's promise rests on, and its reach should be stated plainly:
    /// it erases the store's own copies. It does not reach a copy the operating system paged
    /// out, one captured in a process dump, or the transient strings a turn built while talking
    /// to the provider. "Not saved" means "not written to Overseer's database or its file
    /// store" — nothing stronger.
    /// </remarks>
    internal void Erase()
    {
        lock (_gate)
        {
            foreach (var m in _messages) m.Erase();
            foreach (var a in _attachments) a.Erase();
            _messages.Clear();
            _attachments.Clear();
            Session.Title = "";
            Session.ClientSettings = null;
            IsErased = true;
        }
    }
}

/// <summary>
/// Holds ephemeral ("incognito") chat sessions in memory for the duration of a sliding timeout.
/// </summary>
/// <remarks>
/// <para>
/// A singleton, because the lifetime of a conversation outlasts any request scope and an
/// ephemeral session has no row to reload it from. Sessions are keyed by the token inside a
/// <see cref="SessionRef"/> and are owned by exactly one user, recorded when the reference is
/// minted — which is what lets <see cref="IsOwnedBy"/> answer the authorisation question a
/// <c>ChatSession</c> row lookup answers for a persistent session.
/// </para>
/// <para>
/// Eviction is both explicit and timed. The timed half is a sliding window read from
/// <c>PrivacySettings:Ephemeral:TimeoutMinutes</c>, refreshed on every access, so a conversation
/// left open in a background tab goes away on its own; a browser that closes without calling the
/// close endpoint is the normal case, not the exception.
/// </para>
/// </remarks>
public sealed class EphemeralSessionStore : IDisposable
{
    private readonly ConcurrentDictionary<Guid, EphemeralSession> _sessions = new();
    private readonly ILogger<EphemeralSessionStore>? _logger;
    private readonly Timer? _sweeper;
    private bool _disposed;

    /// <summary>Default sliding timeout when configuration names none.</summary>
    public const int DefaultTimeoutMinutes = 60;

    /// <summary>How often the sweeper looks for expired sessions.</summary>
    private static readonly TimeSpan SweepInterval = TimeSpan.FromMinutes(1);

    public EphemeralSessionStore(
        IConfiguration configuration,
        ILogger<EphemeralSessionStore>? logger = null,
        bool startSweeper = true)
    {
        _logger = logger;

        int minutes = configuration?.GetValue<int?>("PrivacySettings:Ephemeral:TimeoutMinutes")
            ?? DefaultTimeoutMinutes;
        if (minutes <= 0) minutes = DefaultTimeoutMinutes;
        Timeout = TimeSpan.FromMinutes(minutes);

        if (startSweeper)
        {
            _sweeper = new Timer(_ => SweepQuietly(), null, SweepInterval, SweepInterval);
        }
    }

    /// <summary>The sliding inactivity window after which a session is evicted and erased.</summary>
    public TimeSpan Timeout { get; }

    public int Count => _sessions.Count;

    /// <summary>
    /// Mints a reference and creates the session behind it. The caller supplies the detached
    /// <c>ChatSession</c> template so the settings snapshot — confidentiality policy included —
    /// is resolved by the same code that resolves it for a persistent session.
    /// </summary>
    public EphemeralSession Create(string userId, ChatSession template)
    {
        if (string.IsNullOrEmpty(userId)) throw new ArgumentException("An owner is required.", nameof(userId));
        ArgumentNullException.ThrowIfNull(template);

        var token = Guid.NewGuid();
        var session = new EphemeralSession(token, userId, template);
        _sessions[token] = session;
        _logger?.LogInformation("Ephemeral session {Token} created; {Count} now held.", token, _sessions.Count);
        return session;
    }

    /// <summary>
    /// The session behind an ephemeral reference, or null if the reference is not ephemeral, the
    /// session is gone, or it belongs to someone else. Refreshes the sliding window on success.
    /// </summary>
    public EphemeralSession? Get(SessionRef sessionRef, string? userId)
    {
        if (!sessionRef.IsEphemeral || string.IsNullOrEmpty(userId)) return null;
        if (!_sessions.TryGetValue(sessionRef.EphemeralToken, out var session)) return null;
        if (!string.Equals(session.UserId, userId, StringComparison.Ordinal)) return null;
        if (IsExpired(session))
        {
            Evict(session.Token, "expired");
            return null;
        }

        session.LastAccessUtc = DateTime.UtcNow;
        return session;
    }

    /// <summary>
    /// Whether this user owns this ephemeral session — the authorisation check that stands in
    /// for the <c>ChatSession</c> row lookup a persistent session gets. The store owns it
    /// because the store is where ownership was recorded.
    /// </summary>
    public bool IsOwnedBy(SessionRef sessionRef, string? userId) => Get(sessionRef, userId) != null;

    /// <summary>
    /// Evicts and erases a session at its owner's request. Returns false if the reference names
    /// nothing this user owns, which is what a repeated close looks like.
    /// </summary>
    public bool Close(SessionRef sessionRef, string? userId)
    {
        if (!sessionRef.IsEphemeral || string.IsNullOrEmpty(userId)) return false;
        if (!_sessions.TryGetValue(sessionRef.EphemeralToken, out var session)) return false;
        if (!string.Equals(session.UserId, userId, StringComparison.Ordinal)) return false;

        return Evict(session.Token, "closed by user");
    }

    /// <summary>Evicts and erases every session belonging to one user.</summary>
    /// <remarks>
    /// Account deletion calls this. A user whose account is being erased should not keep an open
    /// incognito conversation in the server's memory for the rest of its timeout.
    /// </remarks>
    public int CloseAllForUser(string? userId)
    {
        if (string.IsNullOrEmpty(userId)) return 0;

        int closed = 0;
        foreach (var kvp in _sessions)
        {
            if (string.Equals(kvp.Value.UserId, userId, StringComparison.Ordinal)
                && Evict(kvp.Key, "owner deleted"))
            {
                closed++;
            }
        }
        return closed;
    }

    /// <summary>Evicts and erases every session past its sliding window. Returns how many.</summary>
    public int Sweep()
    {
        int evicted = 0;
        foreach (var kvp in _sessions)
        {
            if (IsExpired(kvp.Value) && Evict(kvp.Key, "expired"))
            {
                evicted++;
            }
        }
        return evicted;
    }

    private bool IsExpired(EphemeralSession session) => DateTime.UtcNow - session.LastAccessUtc > Timeout;

    private bool Evict(Guid token, string reason)
    {
        if (!_sessions.TryRemove(token, out var session)) return false;

        /* Erased only after it is unreachable through the dictionary, so no request can be
           reading the buffers this overwrites. */
        session.Erase();
        _logger?.LogInformation(
            "Ephemeral session {Token} evicted ({Reason}); {Count} still held.", token, reason, _sessions.Count);
        return true;
    }

    private void SweepQuietly()
    {
        try
        {
            Sweep();
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Sweeping expired ephemeral sessions failed.");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _sweeper?.Dispose();
        foreach (var token in _sessions.Keys)
        {
            Evict(token, "store disposed");
        }
    }
}
