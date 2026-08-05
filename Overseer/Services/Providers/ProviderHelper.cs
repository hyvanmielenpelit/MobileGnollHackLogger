namespace Overseer.Services.Providers;

public static class ProviderHelper
{
    public static object? GetProperty(object? obj, string propertyName)
    {
        if (obj == null) return null;
        var prop = obj.GetType().GetProperty(propertyName);
        return prop?.GetValue(obj);
    }

    public static int MapThinkingBudget(string thinkingLevel)
    {
        return thinkingLevel switch
        {
            "low" => 2048,
            "medium" => 8192,
            "high" => 16384,
            _ => -1
        };
    }
}
