using System;
using System.ComponentModel.DataAnnotations;

namespace MobileGnollHackLogger.Data;

public class ChatMessageAttachment
{
    public long Id { get; set; }
    
    public long ChatMessageId { get; set; }
    public ChatMessage? ChatMessage { get; set; }
    
    /// <summary>
    /// The uploader's filename, display-only, and enveloped when the session is confidential.
    /// </summary>
    /// <remarks>
    /// 2048 is headroom for the envelope; the plaintext cap is 256 and is enforced by
    /// <c>AttachmentValidator.SanitizeDisplayFileName</c>. See <c>ChatSession.Title</c> for the
    /// arithmetic and for why the cap belongs in the application rather than in the column.
    /// </remarks>
    [MaxLength(2048)]
    public string? FileName { get; set; }
    
    [MaxLength(128)]
    public string? ContentType { get; set; }
    
    [MaxLength(1024)]
    public string? RelativePath { get; set; }
}
