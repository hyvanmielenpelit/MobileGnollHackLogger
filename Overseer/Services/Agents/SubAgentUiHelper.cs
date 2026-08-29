using System.Text.RegularExpressions;

namespace Overseer.Services.Agents;

public static class SubAgentUiHelper
{
    private static readonly Regex TokenSplitRegex = new(@"[_\-\s]+", RegexOptions.Compiled);
    private static readonly Regex WhitespaceCollapseRegex = new(@"\s+", RegexOptions.Compiled);
    private static readonly Regex PlainCapitalizedWordRegex = new(@"^[A-Z][a-z]*$", RegexOptions.Compiled);

    public static string TitleCaseIdentifier(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return "";

        var tokens = TokenSplitRegex.Split(id.Trim());
        var result = new List<string>();
        foreach (var token in tokens)
        {
            if (string.IsNullOrEmpty(token)) continue;
            if (token.Length == 1)
            {
                result.Add(char.ToUpperInvariant(token[0]).ToString());
            }
            else
            {
                result.Add(char.ToUpperInvariant(token[0]) + token[1..].ToLowerInvariant());
            }
        }
        return string.Join(" ", result);
    }

    public static string LowercaseTypePhrase(string? typeName)
    {
        if (string.IsNullOrWhiteSpace(typeName)) return "";

        var words = typeName.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var result = new List<string>(words.Length);
        foreach (var word in words)
        {
            if (PlainCapitalizedWordRegex.IsMatch(word))
            {
                result.Add(word.ToLowerInvariant());
            }
            else
            {
                result.Add(word);
            }
        }
        return string.Join(" ", result);
    }

    public static string NormalizeInstanceName(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";

        string trimmed = raw.Trim();
        string collapsed = WhitespaceCollapseRegex.Replace(trimmed, " ");
        if (collapsed.Length > 80)
        {
            collapsed = collapsed[..80];
        }
        return collapsed;
    }

    public static string BuildDisplayName(string? agentName, string? subagentName, SubAgentCatalogService? catalog)
    {
        if (string.IsNullOrWhiteSpace(agentName))
        {
            return "Invoking subagent";
        }

        string typeName = "";
        var subAgentDef = catalog?.GetSubAgent(agentName);
        if (subAgentDef != null && !string.IsNullOrWhiteSpace(subAgentDef.DisplayName))
        {
            typeName = subAgentDef.DisplayName.Trim();
        }
        else
        {
            typeName = TitleCaseIdentifier(agentName);
        }

        if (string.IsNullOrWhiteSpace(typeName))
        {
            typeName = "Subagent";
        }

        string instance = NormalizeInstanceName(subagentName);
        if (!string.IsNullOrEmpty(instance))
        {
            if (string.Equals(instance, typeName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(instance, agentName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(instance, TitleCaseIdentifier(agentName), StringComparison.OrdinalIgnoreCase))
            {
                instance = "";
            }
        }

        string label;
        if (!string.IsNullOrEmpty(instance))
        {
            label = $"Invoking {LowercaseTypePhrase(typeName)} subagent: {instance}";
        }
        else
        {
            label = $"Invoking subagent: {typeName}";
        }

        if (label.Length > 256)
        {
            label = label[..256];
        }

        return label;
    }
}
