namespace Overseer.Services;

/// <summary>The thinking level a recorded call sent; <see cref="Value"/> null when it sent none.</summary>
public readonly record struct UsageThinkingLevel(string? Value);
