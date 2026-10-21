namespace Overseer.Services.Benchmarking;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Overseer.Services.Tools;

/// <summary>
/// What a benchmark candidate's requests carry when the run uses an instruction variant: the text
/// appended to the system prompt, per-request tool-guide texts, the experimental tools offered and
/// the tool-output switches. Production never builds one.
/// </summary>
public sealed class BenchmarkVariantContext
{
    public string Name { get; init; } = string.Empty;

    /// <summary>Lower-case hex SHA-256 of the variant's files and its parent's.</summary>
    public string Hash { get; init; } = string.Empty;

    /// <summary>Text appended to the system prompt as a final paragraph; null for none.</summary>
    public string? Appendix { get; init; }

    public IReadOnlyDictionary<string, string> ToolGuides { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<string> ExperimentalTools { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> Experiments { get; init; } = Array.Empty<string>();

    /// <summary>The run stamp: <c>name:hash12</c>.</summary>
    public string Stamp => BenchmarkInstructionVariantCatalog.StampOf(Name, Hash);

    public bool HasExperiment(string experiment)
        => Experiments.Contains(experiment, StringComparer.Ordinal);
}

/// <summary>The <c>variant.json</c> of one variant folder.</summary>
public sealed class BenchmarkInstructionVariantFile
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("extends")]
    public string? Extends { get; set; }

    [JsonPropertyName("promptAppendix")]
    public string? PromptAppendix { get; set; }

    [JsonPropertyName("toolGuides")]
    public Dictionary<string, string>? ToolGuides { get; set; }

    [JsonPropertyName("experimentalTools")]
    public List<string>? ExperimentalTools { get; set; }

    [JsonPropertyName("experiments")]
    public List<string>? Experiments { get; set; }
}

/// <summary>One catalog entry: a resolved variant, or the reason it is not selectable.</summary>
public sealed class BenchmarkInstructionVariant
{
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string? Extends { get; init; }
    public bool IsValid => InvalidReason == null;
    public string? InvalidReason { get; init; }

    /// <summary>The resolved runtime context; null when invalid.</summary>
    public BenchmarkVariantContext? Context { get; init; }
}

/// <summary>
/// The benchmark-only instruction variants under <c>BenchmarkVariants/</c>, loaded once at startup.
/// A variant folder holds a <c>variant.json</c> and the files it names; a variant may extend one
/// other variant that itself extends none.
/// </summary>
public sealed class BenchmarkInstructionVariantCatalog
{
    /// <summary>Tool-output switches a variant may turn on.</summary>
    public const string StatsReferencedByExperiment = "stats_referenced_by";

    public static readonly IReadOnlyList<string> KnownExperiments = new[] { StatsReferencedByExperiment };

    public const string VariantFileName = "variant.json";

    private static readonly Regex NamePattern = new("^[a-z0-9]+(-[a-z0-9]+)*$", RegexOptions.CultureInvariant);

    private readonly Dictionary<string, BenchmarkInstructionVariant> _variants;

    public BenchmarkInstructionVariantCatalog(IEnumerable<IToolHandler> handlers, ILogger<BenchmarkInstructionVariantCatalog> logger)
        : this(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "BenchmarkVariants"), handlers, logger)
    {
    }

    internal BenchmarkInstructionVariantCatalog(string root, IEnumerable<IToolHandler> handlers, ILogger logger)
    {
        Root = root;
        _variants = Load(root, handlers.ToList(), logger);
    }

    public string Root { get; }

    /// <summary>Every variant found, valid or not, ordered by name.</summary>
    public IReadOnlyList<BenchmarkInstructionVariant> All
        => _variants.Values.OrderBy(v => v.Name, StringComparer.Ordinal).ToList();

    public BenchmarkInstructionVariant? Find(string? name)
        => name != null && _variants.TryGetValue(name, out var variant) ? variant : null;

    /// <summary><c>name:hash12</c>, the form a run records.</summary>
    public static string StampOf(string name, string hash)
        => $"{name}:{(hash.Length > 12 ? hash[..12] : hash)}";

    /// <summary>The variant name of a run stamp (<c>name:hash12</c>); the input when it has no colon.</summary>
    public static string NameOfStamp(string stamp)
    {
        int colon = stamp.IndexOf(':');
        return colon < 0 ? stamp : stamp[..colon];
    }

    private static Dictionary<string, BenchmarkInstructionVariant> Load(string root, List<IToolHandler> handlers, ILogger logger)
    {
        var parsed = new Dictionary<string, (BenchmarkInstructionVariantFile? File, string Folder, string? Error)>(StringComparer.Ordinal);
        if (Directory.Exists(root))
        {
            foreach (string folder in Directory.GetDirectories(root).OrderBy(f => f, StringComparer.Ordinal))
            {
                string folderName = Path.GetFileName(folder);
                string variantFile = Path.Combine(folder, VariantFileName);
                if (!File.Exists(variantFile))
                {
                    parsed[folderName] = (null, folder, $"No {VariantFileName} in the folder.");
                    continue;
                }

                try
                {
                    var file = JsonSerializer.Deserialize<BenchmarkInstructionVariantFile>(File.ReadAllText(variantFile));
                    parsed[folderName] = file == null
                        ? (null, folder, $"{VariantFileName} is empty.")
                        : (file, folder, null);
                }
                catch (JsonException ex)
                {
                    parsed[folderName] = (null, folder, $"{VariantFileName} does not parse: {ex.Message}");
                }
            }
        }

        var result = new Dictionary<string, BenchmarkInstructionVariant>(StringComparer.Ordinal);
        foreach (var (folderName, entry) in parsed)
        {
            BenchmarkInstructionVariant variant;
            try
            {
                variant = Resolve(folderName, entry.File, entry.Folder, entry.Error, parsed, handlers);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                variant = new BenchmarkInstructionVariant { Name = folderName, InvalidReason = $"A file could not be read: {ex.Message}" };
            }

            if (!variant.IsValid)
            {
                logger.LogWarning("Instruction variant {Variant} is invalid: {Reason}", folderName, variant.InvalidReason);
            }

            result[folderName] = variant;
        }

        return result;
    }

    private static BenchmarkInstructionVariant Resolve(
        string folderName,
        BenchmarkInstructionVariantFile? file,
        string folder,
        string? parseError,
        IReadOnlyDictionary<string, (BenchmarkInstructionVariantFile? File, string Folder, string? Error)> all,
        List<IToolHandler> handlers)
    {
        BenchmarkInstructionVariant Invalid(string reason) => new()
        {
            Name = folderName,
            Description = file?.Description,
            Extends = file?.Extends,
            InvalidReason = reason
        };

        if (parseError != null || file == null) return Invalid(parseError ?? "Unreadable.");

        string? ownError = ValidateOwn(folderName, file, folder, handlers);
        if (ownError != null) return Invalid(ownError);

        BenchmarkInstructionVariantFile? parent = null;
        string? parentFolder = null;
        if (file.Extends != null)
        {
            if (!all.TryGetValue(file.Extends, out var parentEntry))
                return Invalid($"It extends '{file.Extends}', which is not a variant.");
            if (parentEntry.File == null || parentEntry.Error != null)
                return Invalid($"It extends '{file.Extends}', which is invalid.");
            if (parentEntry.File.Extends != null)
                return Invalid($"It extends '{file.Extends}', which itself extends another variant; one level is allowed.");
            string? parentError = ValidateOwn(file.Extends, parentEntry.File, parentEntry.Folder, handlers);
            if (parentError != null)
                return Invalid($"It extends '{file.Extends}', which is invalid: {parentError}");
            parent = parentEntry.File;
            parentFolder = parentEntry.Folder;
        }

        var guides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (parent?.ToolGuides != null)
        {
            foreach (var (tool, guideFile) in parent.ToolGuides)
                guides[tool] = File.ReadAllText(Path.Combine(parentFolder!, guideFile));
        }
        if (file.ToolGuides != null)
        {
            foreach (var (tool, guideFile) in file.ToolGuides)
                guides[tool] = File.ReadAllText(Path.Combine(folder, guideFile));
        }

        var tools = (parent?.ExperimentalTools ?? new List<string>())
            .Concat(file.ExperimentalTools ?? new List<string>())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var experiments = (parent?.Experiments ?? new List<string>())
            .Concat(file.Experiments ?? new List<string>())
            .Distinct(StringComparer.Ordinal)
            .ToList();

        string? parentAppendix = parent?.PromptAppendix != null
            ? File.ReadAllText(Path.Combine(parentFolder!, parent.PromptAppendix)).Trim()
            : null;
        string? ownAppendix = file.PromptAppendix != null
            ? File.ReadAllText(Path.Combine(folder, file.PromptAppendix)).Trim()
            : null;
        string? appendix = (parentAppendix, ownAppendix) switch
        {
            (null, null) => null,
            (string p, null) => p,
            (null, string o) => o,
            (string p, string o) => p + "\n\n" + o
        };

        string? ownHash = BenchmarkService.ComputeToolGuidesSha256(folder);
        if (ownHash == null) return Invalid("The folder's files could not be hashed.");
        string hash = ownHash;
        if (parentFolder != null)
        {
            string? parentHash = BenchmarkService.ComputeToolGuidesSha256(parentFolder);
            if (parentHash == null) return Invalid("The parent variant's files could not be hashed.");
            hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
                $"parent:{parentHash}\nself:{ownHash}\n"))).ToLowerInvariant();
        }

        return new BenchmarkInstructionVariant
        {
            Name = folderName,
            Description = file.Description,
            Extends = file.Extends,
            Context = new BenchmarkVariantContext
            {
                Name = folderName,
                Hash = hash,
                Appendix = appendix,
                ToolGuides = guides,
                ExperimentalTools = tools,
                Experiments = experiments
            }
        };
    }

    /// <summary>The checks one <c>variant.json</c> must pass on its own; null when it passes.</summary>
    private static string? ValidateOwn(string folderName, BenchmarkInstructionVariantFile file, string folder, List<IToolHandler> handlers)
    {
        if (!string.Equals(file.Name, folderName, StringComparison.Ordinal))
            return $"'name' is '{file.Name}', not the folder name '{folderName}'.";
        if (folderName.Length > 64 || !NamePattern.IsMatch(folderName))
            return "The name must be lower-case kebab-case, at most 64 characters.";
        if (file.Extends != null && string.Equals(file.Extends, folderName, StringComparison.Ordinal))
            return "A variant cannot extend itself.";

        if (file.PromptAppendix != null)
        {
            if (!IsFolderFile(folder, file.PromptAppendix) || !file.PromptAppendix.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
                return $"'promptAppendix' names '{file.PromptAppendix}', which is not a Markdown file of the folder.";
        }

        foreach (var (tool, guideFile) in file.ToolGuides ?? new Dictionary<string, string>())
        {
            if (!handlers.Any(h => string.Equals(h.ToolName, tool, StringComparison.OrdinalIgnoreCase)))
                return $"'toolGuides' names '{tool}', which is not a registered tool.";
            if (!IsFolderFile(folder, guideFile))
                return $"'toolGuides' maps '{tool}' to '{guideFile}', which is not a file of the folder.";
        }

        foreach (string tool in file.ExperimentalTools ?? new List<string>())
        {
            var handler = handlers.FirstOrDefault(h => string.Equals(h.ToolName, tool, StringComparison.OrdinalIgnoreCase));
            if (handler == null)
                return $"'experimentalTools' names '{tool}', which is not a registered tool.";
            if (!handler.IsExperimental)
                return $"'experimentalTools' names '{tool}', which is not an experimental tool.";
        }

        foreach (string experiment in file.Experiments ?? new List<string>())
        {
            if (!KnownExperiments.Contains(experiment, StringComparer.Ordinal))
                return $"'experiments' names '{experiment}', which is not a known experiment ({string.Join(", ", KnownExperiments)}).";
        }

        return null;
    }

    private static bool IsFolderFile(string folder, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative)
            || relative.Contains('/') || relative.Contains('\\') || relative.Contains(".."))
            return false;
        return File.Exists(Path.Combine(folder, relative));
    }
}
