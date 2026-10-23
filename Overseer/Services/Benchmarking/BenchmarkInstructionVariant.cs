namespace Overseer.Services.Benchmarking;

using System;
using System.Collections.Concurrent;
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

    /// <summary>
    /// Each variant folder's own appendix, parent first. <see cref="Appendix"/> is their texts
    /// joined by a blank line.
    /// </summary>
    public IReadOnlyList<BenchmarkVariantAppendixPart> AppendixParts { get; init; } = Array.Empty<BenchmarkVariantAppendixPart>();

    public IReadOnlyDictionary<string, string> ToolGuides { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<string> ExperimentalTools { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> Experiments { get; init; } = Array.Empty<string>();

    /// <summary>The run stamp: <c>name:hash12</c>.</summary>
    public string Stamp => BenchmarkInstructionVariantCatalog.StampOf(Name, Hash);

    public bool HasExperiment(string experiment)
        => Experiments.Contains(experiment, StringComparer.Ordinal);
}

/// <summary>One variant folder's own appendix text, trimmed.</summary>
public sealed record BenchmarkVariantAppendixPart(string Variant, string Text);

/// <summary>What kind of change a variant feature is.</summary>
public enum BenchmarkVariantFeatureKind
{
    /// <summary>An experimental tool, together with its variant guide.</summary>
    ExperimentalTool,

    /// <summary>A tool-output switch.</summary>
    Experiment,

    /// <summary>A replacement description of a production tool.</summary>
    RewordedGuide,

    /// <summary>One variant folder's appendix to the system prompt.</summary>
    Appendix
}

/// <summary>
/// One feature of a resolved variant, derived from its content: an experimental tool, an experiment,
/// a reworded tool description or one folder's appendix. A single-feature ablation leaves one out or
/// keeps only one. Feature ids contain no <c>:</c>, so a stamp still splits at its first colon.
/// </summary>
public sealed class BenchmarkVariantFeature
{
    public const string ToolPrefix = "tool.";
    public const string ExperimentPrefix = "experiment.";
    public const string GuidePrefix = "guide.";
    public const string AppendixPrefix = "appendix.";

    /// <summary><c>tool.&lt;name&gt;</c>, <c>experiment.&lt;name&gt;</c>, <c>guide.&lt;tool&gt;</c> or <c>appendix.&lt;variant&gt;</c>.</summary>
    public string Id { get; init; } = string.Empty;

    public BenchmarkVariantFeatureKind Kind { get; init; }

    /// <summary>The tool, the experiment, or the variant folder whose appendix it is.</summary>
    public string Target { get; init; } = string.Empty;

    /// <summary>The feature comes from the variant this one extends.</summary>
    public bool FromParent { get; init; }

    /// <summary>The variant guide of a tool or a reworded description, or the appendix text; null for an experiment or a tool without a guide.</summary>
    public string? Text { get; init; }

    /// <summary>The ids of the variant's other features that <see cref="Text"/> names.</summary>
    public IReadOnlyList<string> Mentions { get; init; } = Array.Empty<string>();

    /// <summary>The registered handler of the feature's tool; null for an experiment or an appendix.</summary>
    public IToolHandler? Handler { get; init; }

    /// <summary>
    /// Whether the feature reaches the candidate under <paramref name="condition"/>: Closed book
    /// declares no tools, so only an appendix applies there; Source code only withholds the wiki,
    /// knowledge-base and lookup tools, so a reworded description of one of those does not apply.
    /// </summary>
    public bool AppliesUnder(BenchmarkInformationCondition condition) => Kind switch
    {
        BenchmarkVariantFeatureKind.Appendix => true,
        _ when condition == BenchmarkInformationCondition.ClosedBook => false,
        BenchmarkVariantFeatureKind.RewordedGuide when condition == BenchmarkInformationCondition.SourceOnly
            => !BenchmarkInformationConditions.SourceOnlyExcludedTools.Contains(Target, StringComparer.OrdinalIgnoreCase),
        _ => true
    };

    /// <summary>The wire name of <see cref="Kind"/>.</summary>
    public string KindName => KindNameOf(Kind);

    public static string KindNameOf(BenchmarkVariantFeatureKind kind) => kind switch
    {
        BenchmarkVariantFeatureKind.ExperimentalTool => "experimentalTool",
        BenchmarkVariantFeatureKind.Experiment => "experiment",
        BenchmarkVariantFeatureKind.RewordedGuide => "rewordedGuide",
        _ => "appendix"
    };

    /// <summary>A feature id in plain words, such as <i>the reworded wiki_search description</i>; the id itself when it has no known prefix.</summary>
    public static string Describe(string featureId)
    {
        if (featureId.StartsWith(ToolPrefix, StringComparison.Ordinal))
            return $"the {featureId[ToolPrefix.Length..]} tool";
        if (featureId.StartsWith(ExperimentPrefix, StringComparison.Ordinal))
            return $"the {featureId[ExperimentPrefix.Length..]} tool-output switch";
        if (featureId.StartsWith(GuidePrefix, StringComparison.Ordinal))
            return $"the reworded {featureId[GuidePrefix.Length..]} description";
        if (featureId.StartsWith(AppendixPrefix, StringComparison.Ordinal))
            return $"the {featureId[AppendixPrefix.Length..]} instruction paragraph";
        return featureId;
    }
}

/// <summary>How a synthesized ablation entry was derived from its base variant.</summary>
public sealed class BenchmarkVariantAblation
{
    public string BaseName { get; init; } = string.Empty;

    /// <summary><see cref="BenchmarkInstructionVariantCatalog.MinusMode"/> or <see cref="BenchmarkInstructionVariantCatalog.OnlyMode"/>.</summary>
    public string Mode { get; init; } = string.Empty;

    /// <summary>The base's feature that is left out or kept alone.</summary>
    public BenchmarkVariantFeature Feature { get; init; } = new();

    public bool IsMinus => string.Equals(Mode, BenchmarkInstructionVariantCatalog.MinusMode, StringComparison.Ordinal);
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

    /// <summary>
    /// The features the context carries: experimental tools, experiments, reworded descriptions in
    /// ordinal order, then appendices, parent first. Empty when invalid.
    /// </summary>
    public IReadOnlyList<BenchmarkVariantFeature> Features { get; init; } = Array.Empty<BenchmarkVariantFeature>();

    /// <summary>The base and the feature of an ablation spec; null for a variant folder.</summary>
    public BenchmarkVariantAblation? Ablation { get; init; }
}

/// <summary>
/// The benchmark-only instruction variants under <c>BenchmarkVariants/</c>, loaded once at startup.
/// A variant folder holds a <c>variant.json</c> and the files it names; a variant may extend one
/// other variant that itself extends none. <see cref="Find"/> also resolves a single-feature
/// ablation of a folder variant, <c>&lt;name&gt;/minus/&lt;feature&gt;</c> or
/// <c>&lt;name&gt;/only/&lt;feature&gt;</c>.
/// </summary>
public sealed class BenchmarkInstructionVariantCatalog
{
    /// <summary>Tool-output switches a variant may turn on.</summary>
    public const string StatsReferencedByExperiment = "stats_referenced_by";

    public static readonly IReadOnlyList<string> KnownExperiments = new[] { StatsReferencedByExperiment };

    public const string VariantFileName = "variant.json";

    /// <summary>The ablation mode that leaves one feature out; compared with the whole variant.</summary>
    public const string MinusMode = "minus";

    /// <summary>The ablation mode that keeps one feature alone; compared with no variant.</summary>
    public const string OnlyMode = "only";

    public const char SpecSeparator = '/';

    /// <summary>The length of <c>BenchmarkRun.InstructionVariant</c>, which a stamp must fit.</summary>
    public const int MaxStampLength = 160;

    /// <summary>The word in a text that names an experiment's output.</summary>
    private static readonly IReadOnlyDictionary<string, string> ExperimentMentionWords = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [StatsReferencedByExperiment] = "referenced_by"
    };

    private static readonly Regex NamePattern = new("^[a-z0-9]+(-[a-z0-9]+)*$", RegexOptions.CultureInvariant);

    private readonly Dictionary<string, BenchmarkInstructionVariant> _variants;

    private readonly ConcurrentDictionary<string, BenchmarkInstructionVariant> _ablations = new(StringComparer.Ordinal);

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

    /// <summary>
    /// The folder variant named <paramref name="spec"/>; for a spec containing <c>/</c>, the
    /// synthesized ablation it names, or an invalid entry saying why it is refused. Null for an
    /// unknown name without <c>/</c>.
    /// </summary>
    public BenchmarkInstructionVariant? Find(string? spec)
    {
        if (spec == null) return null;
        if (_variants.TryGetValue(spec, out var variant)) return variant;
        if (spec.IndexOf(SpecSeparator) < 0) return null;
        if (_ablations.TryGetValue(spec, out var cached)) return cached;

        var ablation = ResolveAblation(spec);
        // Only valid specs are cached, so arbitrary refused strings do not accumulate.
        return ablation.IsValid ? _ablations.GetOrAdd(spec, ablation) : ablation;
    }

    /// <summary>
    /// Splits <c>&lt;name&gt;/&lt;mode&gt;/&lt;feature&gt;</c> into its three non-empty parts;
    /// false for any other form. The mode is not checked.
    /// </summary>
    public static bool TryParseSpec(string? spec, out string baseName, out string mode, out string featureId)
    {
        baseName = mode = featureId = string.Empty;
        if (string.IsNullOrEmpty(spec)) return false;
        string[] parts = spec.Split(SpecSeparator, 3);
        if (parts.Length != 3 || parts.Any(p => p.Length == 0)) return false;
        baseName = parts[0];
        mode = parts[1];
        featureId = parts[2];
        return true;
    }

    /// <summary>
    /// An ablation spec in words, such as <i>asker-profile-v1, leaving out the reworded wiki_search
    /// description</i>; null for a plain name or a spec of no known mode.
    /// </summary>
    public static string? DescribeAblation(string spec)
    {
        if (!TryParseSpec(spec, out string baseName, out string mode, out string featureId)) return null;
        string feature = BenchmarkVariantFeature.Describe(featureId);
        return mode switch
        {
            MinusMode => $"{baseName}, leaving out {feature}",
            OnlyMode => $"{baseName}, only {feature}",
            _ => null
        };
    }

    /// <summary>The synthesized entry of an ablation spec, or an invalid entry with the reason it is refused.</summary>
    private BenchmarkInstructionVariant ResolveAblation(string spec)
    {
        BenchmarkInstructionVariant Invalid(string reason, BenchmarkInstructionVariant? source = null) => new()
        {
            Name = spec,
            Description = source?.Description,
            Extends = source?.Extends,
            InvalidReason = reason
        };

        if (!TryParseSpec(spec, out string baseName, out string mode, out string featureId))
            return Invalid($"An ablation is written <variant>/{MinusMode}/<feature> or <variant>/{OnlyMode}/<feature>.");
        if (!_variants.TryGetValue(baseName, out var baseVariant))
            return Invalid($"Its base '{baseName}' is not a variant.");
        if (!baseVariant.IsValid || baseVariant.Context == null)
            return Invalid($"Its base '{baseName}' is invalid: {baseVariant.InvalidReason}", baseVariant);
        if (mode != MinusMode && mode != OnlyMode)
            return Invalid($"'{mode}' is not an ablation mode; use '{MinusMode}' or '{OnlyMode}'.", baseVariant);

        var feature = baseVariant.Features.FirstOrDefault(f => string.Equals(f.Id, featureId, StringComparison.Ordinal));
        if (feature == null)
        {
            string known = baseVariant.Features.Count == 0
                ? "it has none"
                : "its features are " + string.Join(", ", baseVariant.Features.Select(f => f.Id));
            return Invalid($"'{featureId}' is not a feature of '{baseName}'; {known}.", baseVariant);
        }
        if (baseVariant.Features.Count == 1)
        {
            return Invalid(mode == MinusMode
                ? $"'{baseName}' has no other feature, so leaving out '{featureId}' leaves nothing; choose None."
                : $"'{baseName}' has no other feature, so only '{featureId}' is the whole variant; choose it.", baseVariant);
        }

        var baseContext = baseVariant.Context;
        string hash = Sha256Hex($"base:{baseContext.Hash}\n{mode}:{featureId}\n");
        string stamp = StampOf(spec, hash);
        if (stamp.Length > MaxStampLength)
            return Invalid($"Its run stamp would be {stamp.Length} characters, more than the {MaxStampLength} a run records.", baseVariant);

        bool minus = mode == MinusMode;
        var kept = baseVariant.Features.Where(f => ReferenceEquals(f, feature) != minus).ToList();
        HashSet<string> KeptTargets(params BenchmarkVariantFeatureKind[] kinds)
            => new(kept.Where(f => kinds.Contains(f.Kind)).Select(f => f.Target), StringComparer.OrdinalIgnoreCase);

        // A tool's guide goes with the tool; a guide left out falls back to the production description.
        var keptTools = KeptTargets(BenchmarkVariantFeatureKind.ExperimentalTool);
        var keptGuides = KeptTargets(BenchmarkVariantFeatureKind.ExperimentalTool, BenchmarkVariantFeatureKind.RewordedGuide);
        var keptExperiments = KeptTargets(BenchmarkVariantFeatureKind.Experiment);
        var keptAppendices = KeptTargets(BenchmarkVariantFeatureKind.Appendix);

        var guides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (tool, text) in baseContext.ToolGuides)
        {
            if (keptGuides.Contains(tool)) guides[tool] = text;
        }
        var parts = baseContext.AppendixParts.Where(p => keptAppendices.Contains(p.Variant)).ToList();

        return new BenchmarkInstructionVariant
        {
            Name = spec,
            Description = baseVariant.Description,
            Extends = baseVariant.Extends,
            Features = kept,
            Ablation = new BenchmarkVariantAblation { BaseName = baseName, Mode = mode, Feature = feature },
            Context = new BenchmarkVariantContext
            {
                Name = spec,
                Hash = hash,
                Appendix = parts.Count == 0 ? null : string.Join("\n\n", parts.Select(p => p.Text)),
                AppendixParts = parts,
                ToolGuides = guides,
                ExperimentalTools = baseContext.ExperimentalTools.Where(keptTools.Contains).ToList(),
                Experiments = baseContext.Experiments.Where(keptExperiments.Contains).ToList()
            }
        };
    }

    private static string Sha256Hex(string text)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

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

        var appendixParts = new List<BenchmarkVariantAppendixPart>();
        if (parentAppendix != null) appendixParts.Add(new BenchmarkVariantAppendixPart(file.Extends!, parentAppendix));
        if (ownAppendix != null) appendixParts.Add(new BenchmarkVariantAppendixPart(folderName, ownAppendix));

        var context = new BenchmarkVariantContext
        {
            Name = folderName,
            Hash = hash,
            Appendix = appendix,
            AppendixParts = appendixParts,
            ToolGuides = guides,
            ExperimentalTools = tools,
            Experiments = experiments
        };

        return new BenchmarkInstructionVariant
        {
            Name = folderName,
            Description = file.Description,
            Extends = file.Extends,
            Context = context,
            Features = DeriveFeatures(context, file, parent, handlers)
        };
    }

    /// <summary>
    /// The features of a resolved context: each experimental tool with its guide, each experiment,
    /// each guide of a tool that is not experimental (ordinal order), and each appendix part.
    /// </summary>
    private static IReadOnlyList<BenchmarkVariantFeature> DeriveFeatures(
        BenchmarkVariantContext context,
        BenchmarkInstructionVariantFile file,
        BenchmarkInstructionVariantFile? parent,
        List<IToolHandler> handlers)
    {
        IToolHandler? HandlerOf(string tool)
            => handlers.FirstOrDefault(h => string.Equals(h.ToolName, tool, StringComparison.OrdinalIgnoreCase));
        string? GuideOf(string tool) => context.ToolGuides.TryGetValue(tool, out var text) ? text : null;
        bool ParentLists(IEnumerable<string>? list, string value, StringComparer comparer)
            => parent != null && list != null && list.Contains(value, comparer);

        var features = new List<BenchmarkVariantFeature>();
        foreach (string tool in context.ExperimentalTools)
        {
            string id = BenchmarkVariantFeature.ToolPrefix + tool;
            string? text = GuideOf(tool);
            features.Add(new BenchmarkVariantFeature
            {
                Id = id,
                Kind = BenchmarkVariantFeatureKind.ExperimentalTool,
                Target = tool,
                FromParent = ParentLists(parent?.ExperimentalTools, tool, StringComparer.OrdinalIgnoreCase),
                Text = text,
                Mentions = MentionsIn(text, id, context),
                Handler = HandlerOf(tool)
            });
        }

        foreach (string experiment in context.Experiments)
        {
            features.Add(new BenchmarkVariantFeature
            {
                Id = BenchmarkVariantFeature.ExperimentPrefix + experiment,
                Kind = BenchmarkVariantFeatureKind.Experiment,
                Target = experiment,
                FromParent = ParentLists(parent?.Experiments, experiment, StringComparer.Ordinal)
            });
        }

        var reworded = context.ToolGuides.Keys
            .Where(tool => !context.ExperimentalTools.Contains(tool, StringComparer.OrdinalIgnoreCase))
            .OrderBy(tool => tool, StringComparer.Ordinal);
        foreach (string tool in reworded)
        {
            string id = BenchmarkVariantFeature.GuidePrefix + tool;
            string text = context.ToolGuides[tool];
            bool ownGuide = file.ToolGuides?.Keys.Contains(tool, StringComparer.OrdinalIgnoreCase) == true;
            features.Add(new BenchmarkVariantFeature
            {
                Id = id,
                Kind = BenchmarkVariantFeatureKind.RewordedGuide,
                Target = tool,
                FromParent = parent != null && !ownGuide,
                Text = text,
                Mentions = MentionsIn(text, id, context),
                Handler = HandlerOf(tool)
            });
        }

        foreach (var part in context.AppendixParts)
        {
            string id = BenchmarkVariantFeature.AppendixPrefix + part.Variant;
            features.Add(new BenchmarkVariantFeature
            {
                Id = id,
                Kind = BenchmarkVariantFeatureKind.Appendix,
                Target = part.Variant,
                FromParent = !string.Equals(part.Variant, context.Name, StringComparison.Ordinal),
                Text = part.Text,
                Mentions = MentionsIn(part.Text, id, context)
            });
        }

        return features;
    }

    /// <summary>
    /// The ids of the context's features that <paramref name="text"/> names as a whole word: an
    /// experimental tool's name, or the output word of an experiment (<c>referenced_by</c>).
    /// </summary>
    private static IReadOnlyList<string> MentionsIn(string? text, string selfId, BenchmarkVariantContext context)
    {
        if (string.IsNullOrEmpty(text)) return Array.Empty<string>();

        var mentions = new List<string>();
        foreach (string tool in context.ExperimentalTools)
        {
            string id = BenchmarkVariantFeature.ToolPrefix + tool;
            if (id != selfId && ContainsWord(text, tool)) mentions.Add(id);
        }
        foreach (string experiment in context.Experiments)
        {
            string id = BenchmarkVariantFeature.ExperimentPrefix + experiment;
            if (id != selfId && ExperimentMentionWords.TryGetValue(experiment, out string? word) && ContainsWord(text, word))
                mentions.Add(id);
        }
        return mentions;
    }

    private static bool ContainsWord(string text, string word)
        => Regex.IsMatch(text, $"(?<![A-Za-z0-9_]){Regex.Escape(word)}(?![A-Za-z0-9_])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

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
