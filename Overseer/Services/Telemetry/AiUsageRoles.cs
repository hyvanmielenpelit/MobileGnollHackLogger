namespace Overseer.Services.Telemetry;

/// <summary>
/// The role groups of usage-log RoleContext values, and the read-side normalization of InputTokens, whose stored
/// meaning differs by role.
/// </summary>
public static class AiUsageRoles
{
    public const string Chat = "chat", TitleGeneration = "title", Benchmark = "benchmark", Other = "other";

    /// <summary>The three named groups, in display order.</summary>
    public static readonly IReadOnlyList<string> NamedGroups = new[] { Chat, TitleGeneration, Benchmark };

    public static string GroupOf(int roleContext) => roleContext switch
    {
        1 or 3 => Chat, 2 => TitleGeneration, >= 4 and <= 8 => Benchmark, _ => Other
    };

    /// <summary>
    /// The whole prompt of a usage row. Chat rows (1, 3) store the prompt minus cache reads; every other
    /// role stores the whole prompt.
    /// </summary>
    public static long PromptTokens(int roleContext, long input, long cacheRead) =>
        roleContext is 1 or 3 ? input + cacheRead : input;

    /// <summary>The prompt tokens neither read from nor written to the cache; never negative.</summary>
    public static long UncachedInputTokens(int roleContext, long input, long cacheRead, long cacheWrite) =>
        Math.Max(0, PromptTokens(roleContext, input, cacheRead) - cacheRead - cacheWrite);
}
