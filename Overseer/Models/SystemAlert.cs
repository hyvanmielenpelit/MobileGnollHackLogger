namespace Overseer.Models;

public class SystemAlert
{
    public required string Id { get; set; }
    public required string Type { get; set; } // "warning" or "error"
    public required string Message { get; set; }
}
