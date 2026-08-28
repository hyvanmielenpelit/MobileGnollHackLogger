using System.Collections.Generic;

namespace Overseer.Services;

public class ModelCatalogEntry
{
    public List<string> Prefixes { get; set; } = new();
    public string DisplayName { get; set; } = string.Empty;
    public string ReleaseDate { get; set; } = string.Empty;
    public List<string> ThinkingLevels { get; set; } = new();
    public List<string> ReasoningModes { get; set; } = new();
    public List<string> ReasoningSummaries { get; set; } = new();
    public int ContextWindowSize { get; set; }
    public int MaxOutputTokens { get; set; }
    public bool SupportsSubAgentCoordination { get; set; } = true;
    public bool SupportsSubAgentExecution { get; set; } = true;
}
