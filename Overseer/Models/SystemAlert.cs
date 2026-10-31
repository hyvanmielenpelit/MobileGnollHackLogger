namespace Overseer.Models;

public class SystemAlert
{
    public required string Id { get; set; }
    public required string Type { get; set; } // "warning" or "error"
    public required string Message { get; set; }

    /// <summary>An in-app route the alert links to, or null for an alert without a link.</summary>
    public string? LinkUrl { get; set; }

    /// <summary>The link's text; set together with <see cref="LinkUrl"/>.</summary>
    public string? LinkText { get; set; }

    /// <summary>Any administrator can mark this alert handled, which clears it for all of them.</summary>
    public bool IsSharedTask { get; set; }
}
