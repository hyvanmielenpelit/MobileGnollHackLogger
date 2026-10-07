namespace MobileGnollHackLogger.Data;

using System;
using System.ComponentModel.DataAnnotations;

public enum ChatConsistencyAnnotationKind
{
    ModelRelease = 1,
    ProviderStatement = 2,

    /// <summary>The provider confirmed a cause. The only kind a report may cite for a mechanism claim.</summary>
    ProviderConfirmedCause = 3,

    PriceChange = 4,
    OurChange = 5,
    Other = 6,
}

/// <summary>A dated admin note on the chat consistency timeline, such as a model release or a provider statement.</summary>
public class ChatConsistencyAnnotation
{
    public int Id { get; set; }

    public DateTime AtUtc { get; set; }

    /// <summary>Null applies the note to every provider.</summary>
    [MaxLength(64)]
    public string? Provider { get; set; }

    /// <summary>Null applies the note to every model of <see cref="Provider"/>.</summary>
    [MaxLength(128)]
    public string? ModelId { get; set; }

    public ChatConsistencyAnnotationKind Kind { get; set; }

    [MaxLength(1000)]
    public string Text { get; set; } = default!;

    [MaxLength(512)]
    public string? SourceUrl { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
