namespace Overseer.Services.Providers;

public class ProviderToolResult
{
    public string ToolCallId { get; set; } = "";
    public string ToolName { get; set; } = "";
    public string Content { get; set; } = "";
    public bool Success { get; set; }
}
