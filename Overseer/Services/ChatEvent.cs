namespace Overseer.Services;

using Overseer.Services.Providers;

public class ChatEvent
{
    public string Type { get; set; } = "chunk";
    public string Data { get; set; } = "";
    /* The session this event belongs to, in SessionRef wire form: a decimal id for a
       persisted session, "eph_<guid>" for an ephemeral one. A string rather than a long
       because an ephemeral session has no numeric id -- and because the client uses this to
       discard events from a session it has already navigated away from, a comparison that has
       to work for both kinds. */
    public string? SessionId { get; set; }
    public int? SeqNo { get; set; }
    // Bounded raw provider failure payload; set only on "error" events, null otherwise.
    public string? Detail { get; set; }
    public TokenUsageReport? UsageReport { get; set; }
}

