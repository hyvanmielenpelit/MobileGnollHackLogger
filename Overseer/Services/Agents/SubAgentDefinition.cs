namespace Overseer.Services.Agents;

public class SubAgentModelPreference
{
    public string Provider { get; set; } = string.Empty;
    public string ModelId { get; set; } = string.Empty;
}

public class SubAgentDefinition
{
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Instructions { get; set; } = string.Empty;
    public List<string> AllowedTools { get; set; } = new();
    public bool EnableWebSearch { get; set; } = false;
    public int? MaxIterations { get; set; }
    public int? MaxOutputTokens { get; set; }
    public string? ThinkingLevel { get; set; }
    public bool IsEnabled { get; set; } = true;
    public SubAgentModelPreference? ModelPreference { get; set; }
}
