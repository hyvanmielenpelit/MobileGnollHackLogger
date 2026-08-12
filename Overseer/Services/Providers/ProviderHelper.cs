namespace Overseer.Services.Providers;

public static class ProviderHelper
{
    public static object? GetProperty(object? obj, string propertyName)
    {
        if (obj == null) return null;
        var prop = obj.GetType().GetProperty(propertyName);
        return prop?.GetValue(obj);
    }
}
