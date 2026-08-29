namespace Overseer.Services.Providers;

public record SegmentedPrompt(string FrozenPrefix, string SessionPrefix, string VolatileSuffix)
{
    public string FullPrompt => (FrozenPrefix ?? "") + (SessionPrefix ?? "") + (VolatileSuffix ?? "");
}
