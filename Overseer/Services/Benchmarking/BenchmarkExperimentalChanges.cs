namespace Overseer.Services.Benchmarking;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MobileGnollHackLogger.Data;
using Overseer.Services.Tools;
using StoredChange = MobileGnollHackLogger.Data.BenchmarkExperimentalChange;

/// <summary>
/// What a benchmark candidate's requests carry when the run uses experimental changes: the text
/// appended to the system prompt, per-request tool descriptions, the experimental tools offered and
/// the tool-output switches. Production never builds one.
/// </summary>
public sealed class BenchmarkVariantContext
{
    /// <summary>The canonical spec of the selection: its change ids, sorted, joined by <c>+</c>.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Lower-case hex SHA-256 of the selection.</summary>
    public string Hash { get; init; } = string.Empty;

    /// <summary>Text appended to the system prompt as a final paragraph; null for none.</summary>
    public string? Appendix { get; init; }

    /// <summary>
    /// Each change's own instructions, in spec order. <see cref="Appendix"/> is their texts joined
    /// by a blank line.
    /// </summary>
    public IReadOnlyList<BenchmarkVariantAppendixPart> AppendixParts { get; init; } = Array.Empty<BenchmarkVariantAppendixPart>();

    /// <summary>The description each changed tool is declared with, replacing its live description.</summary>
    public IReadOnlyDictionary<string, string> ToolGuides { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<string> ExperimentalTools { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> Experiments { get; init; } = Array.Empty<string>();

    /// <summary>The run stamp: <c>spec:hash12</c>.</summary>
    public string Stamp => BenchmarkExperimentalChangeCatalog.StampOf(Name, Hash);

    public bool HasExperiment(string experiment)
        => Experiments.Contains(experiment, StringComparer.Ordinal);
}

/// <summary>One change's instructions, trimmed; <see cref="Variant"/> is the change id.</summary>
public sealed record BenchmarkVariantAppendixPart(string Variant, string Text);

/// <summary>Where an experimental change is defined.</summary>
public enum BenchmarkExperimentalChangeSource
{
    /// <summary>A folder under <c>Overseer/BenchmarkChanges/</c>.</summary>
    BuiltIn,

    /// <summary>A row of <c>BenchmarkExperimentalChanges</c>, written in Overseer.</summary>
    Database
}

/// <summary>What an experimental change does, derived from its contents.</summary>
public enum BenchmarkExperimentalChangeKind
{
    NewTool,
    ToolOutput,
    ToolDescription,
    Instructions
}

/// <summary>How much of a change reaches the candidate under one information condition.</summary>
public enum BenchmarkChangeApplicability
{
    Full,
    Partial,
    None
}

/// <summary>Whether a description edit is appended to the live description or replaces it.</summary>
public enum BenchmarkChangeDescriptionEditMode
{
    Add,
    Replace
}

/// <summary>One production tool's description edit; <see cref="Text"/> is normalized.</summary>
public sealed record BenchmarkChangeDescriptionEdit(string Tool, BenchmarkChangeDescriptionEditMode Mode, string Text)
{
    public bool IsReplacement => Mode == BenchmarkChangeDescriptionEditMode.Replace;
}

/// <summary>The wire names of the experimental-change enumerations.</summary>
public static class BenchmarkExperimentalChangeWire
{
    public const string BuiltIn = "builtIn";
    public const string Database = "database";
    public const string Add = "add";
    public const string Replace = "replace";

    public static string SourceName(BenchmarkExperimentalChangeSource source)
        => source == BenchmarkExperimentalChangeSource.Database ? Database : BuiltIn;

    public static string KindName(BenchmarkExperimentalChangeKind kind) => kind switch
    {
        BenchmarkExperimentalChangeKind.NewTool => "newTool",
        BenchmarkExperimentalChangeKind.ToolOutput => "toolOutput",
        BenchmarkExperimentalChangeKind.ToolDescription => "toolDescription",
        _ => "instructions"
    };

    public static string ModeName(BenchmarkChangeDescriptionEditMode mode)
        => mode == BenchmarkChangeDescriptionEditMode.Replace ? Replace : Add;

    public static bool TryParseMode(string? value, out BenchmarkChangeDescriptionEditMode mode)
    {
        switch (value?.Trim())
        {
            case Add:
                mode = BenchmarkChangeDescriptionEditMode.Add;
                return true;
            case Replace:
                mode = BenchmarkChangeDescriptionEditMode.Replace;
                return true;
            default:
                mode = BenchmarkChangeDescriptionEditMode.Add;
                return false;
        }
    }

    public static string ApplicabilityName(BenchmarkChangeApplicability applicability) => applicability switch
    {
        BenchmarkChangeApplicability.Full => "full",
        BenchmarkChangeApplicability.Partial => "partial",
        _ => "none"
    };
}

/// <summary>
/// One experimental change, built in or written in Overseer: a titled, self-contained idea a run can
/// test. Texts are normalized (<see cref="BenchmarkExperimentalChangeHash.Normalize"/>).
/// <see cref="Mentions"/> and <see cref="ConflictsWith"/> are set by the catalog snapshot.
/// </summary>
public sealed record BenchmarkExperimentalChange
{
    public string Id { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
    public string? ExpectedEffect { get; init; }
    public BenchmarkExperimentalChangeSource Source { get; init; }

    /// <summary>A registered experimental tool offered to the candidate; built in only.</summary>
    public string? ExperimentalTool { get; init; }

    /// <summary>The experimental tool's own description; null keeps the tool's declared one.</summary>
    public string? ExperimentalToolDescription { get; init; }

    /// <summary>A tool-output switch; built in only.</summary>
    public string? Experiment { get; init; }

    public IReadOnlyList<BenchmarkChangeDescriptionEdit> DescriptionEdits { get; init; } = Array.Empty<BenchmarkChangeDescriptionEdit>();

    /// <summary>Text appended to the end of the system prompt; null for none.</summary>
    public string? Instructions { get; init; }

    /// <summary>The change hash, 64 lower-case hex characters.</summary>
    public string Hash { get; init; } = string.Empty;

    public string? InvalidReason { get; init; }
    public bool IsValid => InvalidReason == null;
    public bool Retired { get; init; }

    public DateTime? CreatedAtUtc { get; init; }
    public DateTime? UpdatedAtUtc { get; init; }

    /// <summary>When a run was first stamped with the change; database changes only.</summary>
    public DateTime? LockedAtUtc { get; init; }
    public bool Locked => LockedAtUtc != null;

    /// <summary>The change this one was copied from as a new version.</summary>
    public string? CopiedFrom { get; init; }

    /// <summary>The built-in folder; null for a database change.</summary>
    public string? Folder { get; init; }

    /// <summary>The ids of other changes whose experimental tool or switch this change's texts name.</summary>
    public IReadOnlyList<string> Mentions { get; init; } = Array.Empty<string>();

    /// <summary>The ids of other changes that cannot be selected with this one.</summary>
    public IReadOnlyList<string> ConflictsWith { get; init; } = Array.Empty<string>();

    /// <summary>The first 12 hex characters of <see cref="Hash"/>.</summary>
    public string HashPrefix => Hash.Length > 12 ? Hash[..12] : Hash;

    /// <summary>A new tool or a tool-output switch, which only a built-in change can have.</summary>
    public bool HasCodeEffect => ExperimentalTool != null || Experiment != null;

    /// <summary>The number of effects: the tool, the switch, each description edit and the instructions.</summary>
    public int EffectCount
        => (ExperimentalTool != null ? 1 : 0) + (Experiment != null ? 1 : 0) + DescriptionEdits.Count + (Instructions != null ? 1 : 0);

    public IReadOnlyList<BenchmarkExperimentalChangeKind> Kinds
    {
        get
        {
            var kinds = new List<BenchmarkExperimentalChangeKind>();
            if (ExperimentalTool != null) kinds.Add(BenchmarkExperimentalChangeKind.NewTool);
            if (Experiment != null) kinds.Add(BenchmarkExperimentalChangeKind.ToolOutput);
            if (DescriptionEdits.Count > 0) kinds.Add(BenchmarkExperimentalChangeKind.ToolDescription);
            if (Instructions != null) kinds.Add(BenchmarkExperimentalChangeKind.Instructions);
            return kinds;
        }
    }

    /// <summary>Every text of the change: the tool's description, the description edits and the instructions.</summary>
    public IReadOnlyList<string> Texts
    {
        get
        {
            var texts = new List<string>();
            if (ExperimentalToolDescription != null) texts.Add(ExperimentalToolDescription);
            texts.AddRange(DescriptionEdits.Select(e => e.Text));
            if (Instructions != null) texts.Add(Instructions);
            return texts;
        }
    }

    /// <summary>The tools whose description edit Source code only withholds.</summary>
    public IReadOnlyList<string> NotApplyingUnderSourceOnly
        => DescriptionEdits
            .Select(e => e.Tool)
            .Where(BenchmarkExperimentalChangeCatalog.IsWithheldUnderSourceOnly)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>
    /// How much of the change reaches the candidate: under Closed book only instructions do; under
    /// Source code only a description edit of a withheld tool does not.
    /// </summary>
    public BenchmarkChangeApplicability AppliesUnder(BenchmarkInformationCondition condition)
    {
        int total = 0;
        int reaching = 0;
        void Count(bool reaches)
        {
            total++;
            if (reaches) reaching++;
        }

        bool toolsOffered = condition != BenchmarkInformationCondition.ClosedBook;
        if (ExperimentalTool != null) Count(toolsOffered);
        if (Experiment != null) Count(toolsOffered);
        foreach (var edit in DescriptionEdits)
        {
            Count(toolsOffered && !(condition == BenchmarkInformationCondition.SourceOnly
                && BenchmarkExperimentalChangeCatalog.IsWithheldUnderSourceOnly(edit.Tool)));
        }
        if (Instructions != null) Count(true);

        if (reaching == 0) return BenchmarkChangeApplicability.None;
        return reaching == total ? BenchmarkChangeApplicability.Full : BenchmarkChangeApplicability.Partial;
    }
}

/// <summary>Text normalization and the change and selection hashes.</summary>
public static class BenchmarkExperimentalChangeHash
{
    /// <summary>CRLF and CR become LF and the text is trimmed; null stays null.</summary>
    public static string? Normalize(string? text)
        => text?.Replace("\r\n", "\n").Replace('\r', '\n').Trim();

    /// <summary><see cref="Normalize"/>, with an empty result read as none.</summary>
    public static string? NormalizeOrNull(string? text)
        => Normalize(text) is { Length: > 0 } normalized ? normalized : null;

    public static string Sha256Hex(string text)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

    /// <summary>
    /// The SHA-256 of the change's canonical lines, sorted ordinally, each ending <c>\n</c>:
    /// <c>id:</c>, <c>tool:</c>, <c>tooldesc:</c>, <c>experiment:</c>, <c>add:&lt;tool&gt;:</c>,
    /// <c>replace:&lt;tool&gt;:</c> and <c>instructions:</c>, each text by its own SHA-256. The
    /// title, summary, expected effect and source are not in it.
    /// </summary>
    public static string Of(BenchmarkExperimentalChange change)
    {
        var lines = new List<string> { "id:" + change.Id };
        if (change.ExperimentalTool != null) lines.Add("tool:" + change.ExperimentalTool);
        if (change.ExperimentalToolDescription != null) lines.Add("tooldesc:" + TextHash(change.ExperimentalToolDescription));
        if (change.Experiment != null) lines.Add("experiment:" + change.Experiment);
        foreach (var edit in change.DescriptionEdits)
        {
            lines.Add((edit.IsReplacement ? "replace:" : "add:") + edit.Tool + ":" + TextHash(edit.Text));
        }
        if (change.Instructions != null) lines.Add("instructions:" + TextHash(change.Instructions));

        lines.Sort(StringComparer.Ordinal);
        var sb = new StringBuilder();
        foreach (string line in lines) sb.Append(line).Append('\n');
        return Sha256Hex(sb.ToString());
    }

    /// <summary>The SHA-256 of <c>&lt;id&gt;:&lt;change hash&gt;\n</c> per change, in spec order.</summary>
    public static string OfSelection(IEnumerable<BenchmarkExperimentalChange> changes)
    {
        var sb = new StringBuilder();
        foreach (var change in changes) sb.Append(change.Id).Append(':').Append(change.Hash).Append('\n');
        return Sha256Hex(sb.ToString());
    }

    private static string TextHash(string text) => Sha256Hex(Normalize(text) ?? string.Empty);
}

/// <summary>A validation failure of an experimental change, with the editor field it belongs to.</summary>
public sealed record BenchmarkExperimentalChangeProblem(string Field, string Message);

/// <summary>The rules every experimental change must pass, built in or written in Overseer.</summary>
public static class BenchmarkExperimentalChangeValidator
{
    public const int IdMaxLength = 32;
    public const int TitleMaxLength = 60;
    public const int SummaryMaxLength = 220;
    public const int ExpectedEffectMaxLength = 220;
    public const int TextMaxLength = 4000;
    public const int DescriptionEditsMax = 6;

    /// <summary>Kebab-case ending <c>-v&lt;N&gt;</c>.</summary>
    public static readonly Regex IdPattern = new("^[a-z0-9]+(-[a-z0-9]+)*-v[1-9][0-9]*$", RegexOptions.CultureInvariant);

    public static bool IsValidId(string? id)
        => id != null && id.Length <= IdMaxLength && IdPattern.IsMatch(id);

    /// <summary>The first problem of <paramref name="change"/>; null when it is valid.</summary>
    public static string? Validate(
        BenchmarkExperimentalChange change, IReadOnlyList<IToolHandler> handlers, IReadOnlyList<string> allowedTools)
        => Check(change, handlers, allowedTools).FirstOrDefault()?.Message;

    /// <summary>
    /// Every problem of <paramref name="change"/>, in field order. Description-edit fields are
    /// <c>descriptionEdits[&lt;i&gt;].tool</c> and <c>descriptionEdits[&lt;i&gt;].text</c>; a change
    /// without effects is <c>effects</c>. <paramref name="checkId"/> false skips the id rules, for a
    /// draft whose id is not generated yet.
    /// </summary>
    public static IReadOnlyList<BenchmarkExperimentalChangeProblem> Check(
        BenchmarkExperimentalChange change,
        IReadOnlyList<IToolHandler> handlers,
        IReadOnlyList<string> allowedTools,
        bool checkId = true)
    {
        var problems = new List<BenchmarkExperimentalChangeProblem>();
        void Add(string field, string message) => problems.Add(new BenchmarkExperimentalChangeProblem(field, message));
        IToolHandler? HandlerOf(string tool)
            => handlers.FirstOrDefault(h => string.Equals(h.ToolName, tool, StringComparison.OrdinalIgnoreCase));

        if (checkId && !IsValidId(change.Id))
            Add("id", $"The id must be lower-case kebab-case ending -v<N>, at most {IdMaxLength} characters.");

        if (string.IsNullOrWhiteSpace(change.Title)) Add("title", "Give it a title.");
        else if (change.Title.Length > TitleMaxLength) Add("title", $"The title is longer than {TitleMaxLength} characters.");

        if (string.IsNullOrWhiteSpace(change.Summary)) Add("summary", "Give it a one-sentence summary.");
        else if (change.Summary.Length > SummaryMaxLength) Add("summary", $"The summary is longer than {SummaryMaxLength} characters.");

        if (change.ExpectedEffect != null && change.ExpectedEffect.Length > ExpectedEffectMaxLength)
            Add("expectedEffect", $"Should improve is longer than {ExpectedEffectMaxLength} characters.");

        if (change.EffectCount == 0) Add("effects", "It changes nothing: give it at least one effect.");

        if (change.ExperimentalTool != null)
        {
            var handler = HandlerOf(change.ExperimentalTool);
            if (handler == null) Add("experimentalTool", $"'{change.ExperimentalTool}' is not a registered tool.");
            else if (!handler.IsExperimental) Add("experimentalTool", $"'{change.ExperimentalTool}' is not an experimental tool.");
        }
        else if (change.ExperimentalToolDescription != null)
        {
            Add("experimentalTool", "It gives an experimental tool description but no experimental tool.");
        }

        if (change.ExperimentalToolDescription != null)
            CheckText("experimentalToolDescription", "The experimental tool's description is", change.ExperimentalToolDescription);

        if (change.Experiment != null && !BenchmarkExperimentalChangeCatalog.KnownExperiments.Contains(change.Experiment, StringComparer.Ordinal))
        {
            Add("experiment", $"'{change.Experiment}' is not a known experiment ({string.Join(", ", BenchmarkExperimentalChangeCatalog.KnownExperiments)}).");
        }

        if (change.DescriptionEdits.Count > DescriptionEditsMax)
            Add("effects", $"A change has at most {DescriptionEditsMax} description edits.");

        for (int i = 0; i < change.DescriptionEdits.Count; i++)
        {
            var edit = change.DescriptionEdits[i];
            string toolField = $"descriptionEdits[{i}].tool";
            var handler = string.IsNullOrWhiteSpace(edit.Tool) ? null : HandlerOf(edit.Tool);
            if (string.IsNullOrWhiteSpace(edit.Tool)) Add(toolField, "Choose a tool.");
            else if (handler == null) Add(toolField, $"'{edit.Tool}' is not a registered tool.");
            else if (handler.IsExperimental) Add(toolField, $"'{edit.Tool}' is an experimental tool; only a production tool's description can be changed.");
            else if (!allowedTools.Contains(edit.Tool, StringComparer.OrdinalIgnoreCase)) Add(toolField, $"'{edit.Tool}' is not offered to benchmark runs.");
            else
            {
                var others = change.DescriptionEdits.Take(i).Where(e => string.Equals(e.Tool, edit.Tool, StringComparison.OrdinalIgnoreCase)).ToList();
                if (others.Any(e => e.Mode != edit.Mode)) Add(toolField, $"'{edit.Tool}' is both added to and replaced.");
                else if (others.Count > 0) Add(toolField, $"'{edit.Tool}' has more than one description edit.");
            }

            CheckText($"descriptionEdits[{i}].text", "The text is", edit.Text);
        }

        if (change.Instructions != null) CheckText("instructions", "The instructions are", change.Instructions);

        return problems;

        void CheckText(string field, string subject, string text)
        {
            if (string.IsNullOrWhiteSpace(text)) Add(field, $"{subject} empty.");
            else if (text.Length > TextMaxLength) Add(field, $"{subject} longer than {TextMaxLength.ToString("N0", CultureInfo.InvariantCulture)} characters.");
        }
    }
}

/// <summary>The <c>change.json</c> of a built-in change folder, and of a downloaded folder.</summary>
public sealed class BenchmarkExperimentalChangeFile
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("summary")]
    public string? Summary { get; set; }

    [JsonPropertyName("expectedEffect")]
    public string? ExpectedEffect { get; set; }

    [JsonPropertyName("experimentalTool")]
    public string? ExperimentalTool { get; set; }

    [JsonPropertyName("experimentalToolDescription")]
    public string? ExperimentalToolDescription { get; set; }

    [JsonPropertyName("experiment")]
    public string? Experiment { get; set; }

    /// <summary>Production tool → the <c>.md</c> file appended to its live description.</summary>
    [JsonPropertyName("descriptionAdditions")]
    public Dictionary<string, string>? DescriptionAdditions { get; set; }

    /// <summary>Production tool → the <c>.md</c> file that replaces its description.</summary>
    [JsonPropertyName("descriptionReplacements")]
    public Dictionary<string, string>? DescriptionReplacements { get; set; }

    [JsonPropertyName("instructions")]
    public string? Instructions { get; set; }
}

/// <summary>One item of <c>BenchmarkExperimentalChange.DescriptionEditsJson</c>.</summary>
public sealed class BenchmarkStoredDescriptionEdit
{
    [JsonPropertyName("tool")]
    public string? Tool { get; set; }

    [JsonPropertyName("mode")]
    public string? Mode { get; set; }

    [JsonPropertyName("text")]
    public string? Text { get; set; }
}

/// <summary>
/// A resolved selection of experimental changes: its canonical spec, the changes in spec order and
/// the composed context, or the reason it is refused. <see cref="None"/> is no changes.
/// </summary>
public sealed class BenchmarkExperimentalChangeSelection
{
    public static readonly BenchmarkExperimentalChangeSelection None = new();

    /// <summary>The canonical spec; the normalized input when refused; null for no changes.</summary>
    public string? Spec { get; init; }

    public IReadOnlyList<BenchmarkExperimentalChange> Changes { get; init; } = Array.Empty<BenchmarkExperimentalChange>();

    /// <summary>The selection hash; null when refused or empty.</summary>
    public string? Hash { get; init; }

    /// <summary>The composed context; null when refused or empty.</summary>
    public BenchmarkVariantContext? Context { get; init; }

    public string? Error { get; init; }

    public bool IsValid => Error == null;
    public bool IsEmpty => Spec == null && Error == null;
    public string? Stamp => Context?.Stamp;

    /// <summary>
    /// Why the selection cannot run under <paramref name="condition"/>: the first change none of
    /// whose effects reaches the candidate there, since the stamp would claim it. Null when every
    /// change has an effect.
    /// </summary>
    public string? ConditionRefusal(BenchmarkInformationCondition condition)
    {
        foreach (var change in Changes)
        {
            if (change.AppliesUnder(condition) != BenchmarkChangeApplicability.None) continue;

            string reason = condition == BenchmarkInformationCondition.ClosedBook
                ? "no tools are offered there"
                : $"{BenchmarkExperimentalChangeCatalog.JoinWords(change.NotApplyingUnderSourceOnly)} "
                    + (change.NotApplyingUnderSourceOnly.Count == 1 ? "is" : "are") + " not offered there";
            return $"{change.Title} has no effect under {condition.ShortName()}: {reason}. Untick it, or choose another Tool Access.";
        }
        return null;
    }
}

/// <summary>An immutable view of every experimental change: the built-in ones merged with the database's.</summary>
public sealed class BenchmarkExperimentalChangeSnapshot
{
    private readonly IReadOnlyDictionary<string, BenchmarkExperimentalChange> _byId;

    internal BenchmarkExperimentalChangeSnapshot(
        IReadOnlyList<BenchmarkExperimentalChange> changes,
        IReadOnlyDictionary<string, BenchmarkExperimentalChange> byId,
        bool databaseLoaded)
    {
        Changes = changes;
        _byId = byId;
        DatabaseLoaded = databaseLoaded;
    }

    /// <summary>Built-in changes by title, then database changes by title; valid or not, retired or not.</summary>
    public IReadOnlyList<BenchmarkExperimentalChange> Changes { get; }

    /// <summary>The database changes were read; false until the first refresh succeeds.</summary>
    public bool DatabaseLoaded { get; }

    public BenchmarkExperimentalChange? Find(string? id)
        => id != null && _byId.TryGetValue(id, out var change) ? change : null;
}

/// <summary>
/// The benchmark-only experimental changes: the built-in folders under <c>BenchmarkChanges/</c>,
/// loaded once at startup, merged with the changes written in Overseer into an immutable
/// <see cref="Snapshot"/> that <see cref="RefreshAsync"/> rebuilds and swaps. A spec is the selected
/// ids, sorted, joined by <c>+</c>; a run records <c>spec:hash12</c>.
/// </summary>
public sealed class BenchmarkExperimentalChangeCatalog
{
    /// <summary>Tool-output switches a change may turn on.</summary>
    public const string StatsReferencedByExperiment = "stats_referenced_by";

    public static readonly IReadOnlyList<string> KnownExperiments = new[] { StatsReferencedByExperiment };

    public const string FolderName = "BenchmarkChanges";
    public const string ChangeFileName = "change.json";
    public const string RetiredFileName = "retired.json";
    public const char SpecSeparator = '+';

    /// <summary>The length of <c>BenchmarkRun.InstructionVariant</c>, which a stamp must fit.</summary>
    public const int MaxStampLength = 160;

    public const string StampTooLongRefusal =
        "Too many changes for one run: the run record holds 160 characters. Choose fewer.";

    /// <summary>Why a built-in folder whose id a database change with other text holds is invalid.</summary>
    public const string IdTakenReason = "Its id is taken by a change written in Overseer with different text.";

    /// <summary>The word in a text that names an experiment's output.</summary>
    private static readonly IReadOnlyDictionary<string, string> ExperimentMentionWords = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [StatsReferencedByExperiment] = "referenced_by"
    };

    private static readonly Regex SpecIdPattern = new("^[a-z0-9]+(-[a-z0-9]+)*$", RegexOptions.CultureInvariant);

    /// <summary>The catalog the container built, for the reports' title lookups; null in tests.</summary>
    private static volatile BenchmarkExperimentalChangeCatalog? s_current;

    private readonly List<IToolHandler> _handlers;
    private readonly ILogger _logger;
    private readonly Func<CancellationToken, Task<IReadOnlyList<StoredChange>>>? _loadStored;
    private readonly IReadOnlyList<BenchmarkExperimentalChange> _builtIns;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private readonly ConcurrentDictionary<string, BenchmarkVariantContext> _contexts = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, bool> _loggedCollisions = new(StringComparer.Ordinal);
    private BenchmarkExperimentalChangeSnapshot _snapshot;
    private int _loadAttempted;

    /// <param name="toolRegistry">Taken so the live tool descriptions are loaded before any composition reads them.</param>
    public BenchmarkExperimentalChangeCatalog(
        IEnumerable<IToolHandler> handlers,
        ToolRegistry toolRegistry,
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<BenchmarkExperimentalChangeCatalog> logger)
        : this(
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, FolderName),
            handlers,
            logger,
            ct => LoadStoredAsync(scopeFactory, ct),
            BenchmarkService.ConfiguredAllowedTools(configuration))
    {
        ArgumentNullException.ThrowIfNull(toolRegistry);
        s_current = this;
    }

    /// <param name="loadStored">Reads the database changes; null for a catalog of built-in changes only.</param>
    /// <param name="allowedTools">The benchmark allowlist; null is <see cref="BenchmarkService.DefaultAllowedTools"/>.</param>
    internal BenchmarkExperimentalChangeCatalog(
        string root,
        IEnumerable<IToolHandler> handlers,
        ILogger logger,
        Func<CancellationToken, Task<IReadOnlyList<StoredChange>>>? loadStored = null,
        IReadOnlyList<string>? allowedTools = null)
    {
        Root = root;
        _handlers = handlers.ToList();
        _logger = logger;
        _loadStored = loadStored;
        AllowedTools = allowedTools ?? BenchmarkService.DefaultAllowedTools;
        _builtIns = LoadBuiltIns(root);
        _snapshot = BuildSnapshot(Array.Empty<StoredChange>(), databaseLoaded: loadStored == null);
        if (loadStored == null) _loadAttempted = 1;
    }

    public string Root { get; }

    /// <summary>The benchmark tool allowlist a description edit must name a tool of.</summary>
    public IReadOnlyList<string> AllowedTools { get; }

    public IReadOnlyList<IToolHandler> Handlers => _handlers;

    /// <summary>Every built-in folder, valid or not, including one a database change with other text displaces.</summary>
    public IReadOnlyList<BenchmarkExperimentalChange> BuiltIns => _builtIns;

    /// <summary>The current snapshot; the first read loads the database changes.</summary>
    public BenchmarkExperimentalChangeSnapshot Snapshot
    {
        get
        {
            EnsureLoaded();
            return Volatile.Read(ref _snapshot);
        }
    }

    /// <summary>Re-reads the database changes and swaps in a new snapshot; a failed read keeps the old one.</summary>
    public async Task RefreshAsync(CancellationToken ct = default)
    {
        if (_loadStored == null) return;

        await _refreshLock.WaitAsync(ct);
        try
        {
            Interlocked.Exchange(ref _loadAttempted, 1);
            IReadOnlyList<StoredChange> rows;
            try
            {
                rows = await _loadStored(ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "The experimental changes written in Overseer could not be read.");
                return;
            }

            Volatile.Write(ref _snapshot, BuildSnapshot(rows, databaseLoaded: true));
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    internal static async Task<IReadOnlyList<StoredChange>> LoadStoredAsync(ApplicationDbContext db, CancellationToken ct)
        => await db.BenchmarkExperimentalChanges.AsNoTracking().OrderBy(c => c.Id).ToListAsync(ct);

    private static async Task<IReadOnlyList<StoredChange>> LoadStoredAsync(IServiceScopeFactory scopeFactory, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await LoadStoredAsync(db, ct);
    }

    private void EnsureLoaded()
    {
        if (_loadStored == null || Volatile.Read(ref _loadAttempted) != 0) return;
        try
        {
            Task.Run(() => RefreshAsync(CancellationToken.None)).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Interlocked.Exchange(ref _loadAttempted, 1);
            _logger.LogWarning(ex, "The experimental changes written in Overseer could not be read.");
        }
    }

    // --- Specs and stamps ---------------------------------------------------------------------

    /// <summary><c>spec:hash12</c>, the form a run records.</summary>
    public static string StampOf(string spec, string hash)
        => $"{spec}:{(hash.Length > 12 ? hash[..12] : hash)}";

    /// <summary>The spec of a run stamp (<c>spec:hash12</c>); the input when it has no colon.</summary>
    public static string SpecOfStamp(string stamp)
    {
        int colon = stamp.IndexOf(':');
        return colon < 0 ? stamp : stamp[..colon];
    }

    /// <summary>
    /// The spec trimmed, with spaces turned back into <c>+</c> (a query string decodes <c>+</c> to a
    /// space, and ids contain none); null for a blank spec.
    /// </summary>
    public static string? NormalizeSpec(string? spec)
        => string.IsNullOrWhiteSpace(spec) ? null : spec.Trim().Replace(' ', SpecSeparator);

    /// <summary>
    /// <see cref="NormalizeSpec"/>, with the ids sorted ordinally when every part is a well-formed id;
    /// any other value is returned normalized and unsorted.
    /// </summary>
    public static string? CanonicalizeSpec(string? spec)
    {
        string? normalized = NormalizeSpec(spec);
        if (normalized == null) return null;
        string[] ids = normalized.Split(SpecSeparator);
        return ids.All(id => SpecIdPattern.IsMatch(id)) ? CanonicalSpec(ids) : normalized;
    }

    /// <summary>Whether every part of a normalized spec is a well-formed id.</summary>
    public static bool IsWellFormedSpec(string spec)
        => spec.Split(SpecSeparator).All(id => SpecIdPattern.IsMatch(id));

    /// <summary>The ids sorted ordinally and joined by <c>+</c>.</summary>
    public static string CanonicalSpec(IEnumerable<string> ids)
        => string.Join(SpecSeparator, ids.OrderBy(id => id, StringComparer.Ordinal));

    /// <summary>The change ids of a spec or a stamp; empty for none.</summary>
    public static IReadOnlyList<string> IdsOf(string? specOrStamp)
    {
        string? spec = NormalizeSpec(specOrStamp == null ? null : SpecOfStamp(specOrStamp));
        return spec == null
            ? Array.Empty<string>()
            : spec.Split(SpecSeparator, StringSplitOptions.RemoveEmptyEntries);
    }

    internal static bool IsWithheldUnderSourceOnly(string tool)
        => BenchmarkInformationConditions.SourceOnlyExcludedTools.Contains(tool, StringComparer.OrdinalIgnoreCase);

    // --- Titles -------------------------------------------------------------------------------

    /// <summary>The change's title, or the id when the catalog does not know it.</summary>
    public string TitleOf(string id)
        => Snapshot.Find(id)?.Title is { Length: > 0 } title ? title : id;

    /// <summary>The titles of a spec's or a stamp's changes, joined by commas; <c>none</c> for none.</summary>
    public string Describe(string? specOrStamp) => DescribeWith(specOrStamp, TitleOf);

    /// <summary>The title of <paramref name="id"/> in the catalog the container built, else the id.</summary>
    public static string SharedTitleOf(string id)
    {
        var current = s_current;
        if (current == null) return id;
        try
        {
            return current.TitleOf(id);
        }
        catch (Exception)
        {
            return id;
        }
    }

    internal static string DescribeWith(string? specOrStamp, Func<string, string?> titleOf)
    {
        var ids = IdsOf(specOrStamp);
        return ids.Count == 0 ? "none" : string.Join(", ", ids.Select(id => titleOf(id) ?? id));
    }

    /// <summary>Words joined as <i>a</i>, <i>a and b</i>, <i>a, b and c</i>.</summary>
    internal static string JoinWords(IReadOnlyList<string> words) => words.Count switch
    {
        0 => string.Empty,
        1 => words[0],
        _ => string.Join(", ", words.Take(words.Count - 1)) + " and " + words[^1]
    };

    // --- Resolution ---------------------------------------------------------------------------

    /// <summary>
    /// The selection <paramref name="spec"/> names, refused in this order: a malformed spec, an
    /// unknown id, an invalid change, a retired change (unless <paramref name="allowRetired"/>, as for
    /// a re-run), a duplicate, a conflict, a stamp too long for the run record.
    /// </summary>
    public BenchmarkExperimentalChangeSelection Resolve(string? spec, bool allowRetired = false)
    {
        string? normalized = NormalizeSpec(spec);
        if (normalized == null) return BenchmarkExperimentalChangeSelection.None;

        BenchmarkExperimentalChangeSelection Refused(string error) => new() { Spec = normalized, Error = error };

        string[] ids = normalized.Split(SpecSeparator);
        if (ids.Any(id => !SpecIdPattern.IsMatch(id)))
            return Refused($"'{normalized}' is not a list of experimental change ids joined by '+'.");

        var snapshot = Snapshot;
        var changes = new List<BenchmarkExperimentalChange>();
        foreach (string id in ids)
        {
            var change = snapshot.Find(id);
            if (change == null) return Refused($"Unknown experimental change '{id}'.");
            changes.Add(change);
        }

        foreach (var change in changes)
        {
            if (!change.IsValid) return Refused($"{LabelOf(change)} is invalid: {change.InvalidReason}");
        }

        if (!allowRetired)
        {
            foreach (var change in changes)
            {
                if (change.Retired) return Refused($"{change.Title} is retired.");
            }
        }

        var duplicate = changes.GroupBy(c => c.Id, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1);
        if (duplicate != null) return Refused($"{duplicate.First().Title} is selected more than once.");

        var ordered = changes.OrderBy(c => c.Id, StringComparer.Ordinal).ToList();
        for (int i = 0; i < ordered.Count; i++)
        {
            for (int j = i + 1; j < ordered.Count; j++)
            {
                string? conflict = ConflictBetween(ordered[i], ordered[j]);
                if (conflict != null) return Refused($"{ordered[i].Title} and {ordered[j].Title} {conflict}.");
            }
        }

        string canonical = string.Join(SpecSeparator, ordered.Select(c => c.Id));
        string hash = BenchmarkExperimentalChangeHash.OfSelection(ordered);
        if (StampOf(canonical, hash).Length > MaxStampLength) return Refused(StampTooLongRefusal);

        var context = _contexts.GetOrAdd(canonical + ":" + hash, _ => Compose(canonical, hash, ordered));
        return new BenchmarkExperimentalChangeSelection
        {
            Spec = canonical,
            Changes = ordered,
            Hash = hash,
            Context = context
        };
    }

    /// <summary>
    /// Why <paramref name="a"/> and <paramref name="b"/> cannot run together, as the end of a sentence
    /// naming both; null when they compose. Additions to one tool compose.
    /// </summary>
    public static string? ConflictBetween(BenchmarkExperimentalChange a, BenchmarkExperimentalChange b)
    {
        if (a.ExperimentalTool != null && string.Equals(a.ExperimentalTool, b.ExperimentalTool, StringComparison.OrdinalIgnoreCase))
            return $"both offer the {a.ExperimentalTool} tool";
        if (a.Experiment != null && string.Equals(a.Experiment, b.Experiment, StringComparison.Ordinal))
            return $"both turn on the {a.Experiment} tool output";

        foreach (var edit in a.DescriptionEdits)
        {
            var other = b.DescriptionEdits.FirstOrDefault(e => string.Equals(e.Tool, edit.Tool, StringComparison.OrdinalIgnoreCase));
            if (other == null) continue;
            if (edit.IsReplacement && other.IsReplacement) return $"both replace the {edit.Tool} description";
            if (edit.IsReplacement || other.IsReplacement)
                return $"both change the {edit.Tool} description, and one of them replaces it";
        }
        return null;
    }

    /// <summary>The live description of a production tool, normalized; empty when it has none.</summary>
    public string LiveDescriptionOf(string tool)
        => BenchmarkExperimentalChangeHash.Normalize(HandlerOf(tool)?.Description) ?? string.Empty;

    internal IToolHandler? HandlerOf(string tool)
        => _handlers.FirstOrDefault(h => string.Equals(h.ToolName, tool, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The context of a valid selection, in spec order: experimental tools and experiments are the
    /// union; a tool's description is its replacement, else the live description followed by a blank
    /// line and each addition; instructions are joined by a blank line.
    /// </summary>
    private BenchmarkVariantContext Compose(string spec, string hash, IReadOnlyList<BenchmarkExperimentalChange> changes)
    {
        var tools = new List<string>();
        var experiments = new List<string>();
        var guides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var parts = new List<BenchmarkVariantAppendixPart>();

        foreach (var change in changes)
        {
            if (change.ExperimentalTool is string tool)
            {
                if (!tools.Contains(tool, StringComparer.OrdinalIgnoreCase)) tools.Add(tool);
                if (change.ExperimentalToolDescription != null) guides[tool] = change.ExperimentalToolDescription;
            }
            if (change.Experiment is string experiment && !experiments.Contains(experiment, StringComparer.Ordinal))
            {
                experiments.Add(experiment);
            }
            if (change.Instructions != null)
            {
                parts.Add(new BenchmarkVariantAppendixPart(change.Id, change.Instructions));
            }
        }

        var edits = changes.SelectMany(c => c.DescriptionEdits).ToList();
        foreach (string tool in edits.Select(e => e.Tool).Distinct(StringComparer.OrdinalIgnoreCase).ToList())
        {
            var forTool = edits.Where(e => string.Equals(e.Tool, tool, StringComparison.OrdinalIgnoreCase)).ToList();
            var replacement = forTool.FirstOrDefault(e => e.IsReplacement);
            if (replacement != null)
            {
                guides[tool] = replacement.Text;
                continue;
            }

            var sb = new StringBuilder(LiveDescriptionOf(tool));
            foreach (var addition in forTool)
            {
                if (sb.Length > 0) sb.Append("\n\n");
                sb.Append(addition.Text);
            }
            guides[tool] = sb.ToString();
        }

        return new BenchmarkVariantContext
        {
            Name = spec,
            Hash = hash,
            Appendix = parts.Count == 0 ? null : string.Join("\n\n", parts.Select(p => p.Text)),
            AppendixParts = parts,
            ToolGuides = guides,
            ExperimentalTools = tools,
            Experiments = experiments
        };
    }

    private static string LabelOf(BenchmarkExperimentalChange change)
        => string.IsNullOrWhiteSpace(change.Title) ? change.Id : change.Title;

    // --- Snapshots ----------------------------------------------------------------------------

    /// <summary>
    /// Merges the built-in changes with the database rows. A row whose id a valid built-in change
    /// holds with an equal hash is hidden; with other text the row is kept and the built-in folder is
    /// invalid and not listed. Mentions and conflicts are computed over the listed valid changes.
    /// </summary>
    private BenchmarkExperimentalChangeSnapshot BuildSnapshot(IReadOnlyList<StoredChange> rows, bool databaseLoaded)
    {
        var stored = rows.Select(FromStored).ToList();
        var storedIds = new HashSet<string>(stored.Select(c => c.Id), StringComparer.Ordinal);
        var byId = new Dictionary<string, BenchmarkExperimentalChange>(StringComparer.Ordinal);
        var builtIns = new List<BenchmarkExperimentalChange>();
        var hiddenRows = new HashSet<string>(StringComparer.Ordinal);

        foreach (var builtIn in _builtIns)
        {
            if (byId.ContainsKey(builtIn.Id)) continue;
            if (storedIds.Contains(builtIn.Id))
            {
                var row = stored.First(c => c.Id == builtIn.Id);
                if (builtIn.IsValid && string.Equals(builtIn.Hash, row.Hash, StringComparison.Ordinal))
                {
                    hiddenRows.Add(builtIn.Id);
                }
                else
                {
                    if (_loggedCollisions.TryAdd(builtIn.Id, true))
                    {
                        _logger.LogWarning("Experimental change {Change} is invalid: {Reason}", builtIn.Id, IdTakenReason);
                    }
                    continue;
                }
            }
            byId[builtIn.Id] = builtIn;
            builtIns.Add(builtIn);
        }

        var database = new List<BenchmarkExperimentalChange>();
        foreach (var change in stored)
        {
            if (hiddenRows.Contains(change.Id) || byId.ContainsKey(change.Id)) continue;
            byId[change.Id] = change;
            database.Add(change);
        }

        static IEnumerable<BenchmarkExperimentalChange> ByTitle(IEnumerable<BenchmarkExperimentalChange> changes)
            => changes.OrderBy(c => c.Title, StringComparer.OrdinalIgnoreCase).ThenBy(c => c.Id, StringComparer.Ordinal);

        var listed = ByTitle(builtIns).Concat(ByTitle(database)).ToList();
        var valid = listed.Where(c => c.IsValid).ToList();
        var related = listed
            .Select(c => c with
            {
                Mentions = valid.Where(o => o.Id != c.Id && Mentions(c, o)).Select(o => o.Id).ToList(),
                ConflictsWith = valid.Where(o => o.Id != c.Id && ConflictBetween(c, o) != null).Select(o => o.Id).ToList()
            })
            .ToList();

        var relatedById = new Dictionary<string, BenchmarkExperimentalChange>(StringComparer.Ordinal);
        foreach (var change in related) relatedById[change.Id] = change;
        return new BenchmarkExperimentalChangeSnapshot(related, relatedById, databaseLoaded);
    }

    /// <summary>
    /// Whether a text of <paramref name="change"/> names, as a whole word, the experimental tool of
    /// <paramref name="other"/> or the output word of its experiment (<c>referenced_by</c>).
    /// </summary>
    private static bool Mentions(BenchmarkExperimentalChange change, BenchmarkExperimentalChange other)
    {
        var words = new List<string>();
        if (other.ExperimentalTool != null) words.Add(other.ExperimentalTool);
        if (other.Experiment != null && ExperimentMentionWords.TryGetValue(other.Experiment, out string? word)) words.Add(word);
        if (words.Count == 0) return false;
        return change.Texts.Any(text => words.Any(w => ContainsWord(text, w)));
    }

    private static bool ContainsWord(string text, string word)
        => Regex.IsMatch(text, $"(?<![A-Za-z0-9_]){Regex.Escape(word)}(?![A-Za-z0-9_])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>A database row as a change, validated; its hash recomputed from its text.</summary>
    internal BenchmarkExperimentalChange FromStored(StoredChange row)
    {
        var edits = new List<BenchmarkChangeDescriptionEdit>();
        string? editsError = null;
        if (!string.IsNullOrWhiteSpace(row.DescriptionEditsJson))
        {
            try
            {
                var items = JsonSerializer.Deserialize<List<BenchmarkStoredDescriptionEdit>>(row.DescriptionEditsJson)
                    ?? new List<BenchmarkStoredDescriptionEdit>();
                foreach (var item in items)
                {
                    if (!BenchmarkExperimentalChangeWire.TryParseMode(item.Mode, out var mode))
                    {
                        editsError ??= $"A description edit has the mode '{item.Mode}', not 'add' or 'replace'.";
                    }
                    edits.Add(new BenchmarkChangeDescriptionEdit(
                        CanonicalToolName(item.Tool ?? string.Empty), mode, BenchmarkExperimentalChangeHash.Normalize(item.Text) ?? string.Empty));
                }
            }
            catch (JsonException ex)
            {
                editsError = $"Its description edits do not parse: {ex.Message}";
            }
        }

        var change = new BenchmarkExperimentalChange
        {
            Id = row.ChangeId,
            Title = row.Title?.Trim() ?? string.Empty,
            Summary = row.Summary?.Trim() ?? string.Empty,
            ExpectedEffect = string.IsNullOrWhiteSpace(row.ExpectedEffect) ? null : row.ExpectedEffect.Trim(),
            Source = BenchmarkExperimentalChangeSource.Database,
            DescriptionEdits = edits,
            Instructions = BenchmarkExperimentalChangeHash.NormalizeOrNull(row.Instructions),
            Retired = row.RetiredAtUtc != null,
            CreatedAtUtc = AsUtc(row.CreatedAtUtc),
            UpdatedAtUtc = AsUtc(row.UpdatedAtUtc),
            LockedAtUtc = row.LockedAtUtc is DateTime locked ? AsUtc(locked) : null,
            CopiedFrom = row.CopiedFromChangeId
        };
        change = change with { Hash = BenchmarkExperimentalChangeHash.Of(change) };

        string? reason = editsError ?? BenchmarkExperimentalChangeValidator.Validate(change, _handlers, AllowedTools);
        return reason == null ? change : change with { InvalidReason = reason };
    }

    private static DateTime AsUtc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);

    /// <summary>The registered handler's spelling of <paramref name="tool"/>; the input when none is registered.</summary>
    internal string CanonicalToolName(string tool)
        => HandlerOf(tool.Trim())?.ToolName ?? tool.Trim();

    // --- Built-in folders ---------------------------------------------------------------------

    private IReadOnlyList<BenchmarkExperimentalChange> LoadBuiltIns(string root)
    {
        var result = new List<BenchmarkExperimentalChange>();
        if (!Directory.Exists(root)) return result;

        var retired = ReadRetired(root);
        foreach (string folder in Directory.GetDirectories(root).OrderBy(f => f, StringComparer.Ordinal))
        {
            string folderName = Path.GetFileName(folder);
            BenchmarkExperimentalChange change;
            try
            {
                change = LoadBuiltIn(folderName, folder);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                change = new BenchmarkExperimentalChange
                {
                    Id = folderName,
                    Source = BenchmarkExperimentalChangeSource.BuiltIn,
                    Folder = folder,
                    InvalidReason = $"A file could not be read: {ex.Message}"
                };
            }

            change = change with { Retired = retired.Contains(change.Id) };
            if (!change.IsValid)
            {
                _logger.LogWarning("Experimental change {Change} is invalid: {Reason}", folderName, change.InvalidReason);
            }
            result.Add(change);
        }
        return result;
    }

    private HashSet<string> ReadRetired(string root)
    {
        var retired = new HashSet<string>(StringComparer.Ordinal);
        string path = Path.Combine(root, RetiredFileName);
        if (!File.Exists(path)) return retired;
        try
        {
            foreach (string? id in JsonSerializer.Deserialize<List<string?>>(File.ReadAllText(path)) ?? new List<string?>())
            {
                if (!string.IsNullOrWhiteSpace(id)) retired.Add(id.Trim());
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "{File} could not be read; no built-in change is retired.", RetiredFileName);
        }
        return retired;
    }

    private BenchmarkExperimentalChange LoadBuiltIn(string folderName, string folder)
    {
        BenchmarkExperimentalChangeFile? file = null;
        BenchmarkExperimentalChange Invalid(string reason) => new()
        {
            Id = folderName,
            Title = file?.Title?.Trim() ?? string.Empty,
            Summary = file?.Summary?.Trim() ?? string.Empty,
            ExpectedEffect = string.IsNullOrWhiteSpace(file?.ExpectedEffect) ? null : file.ExpectedEffect.Trim(),
            Source = BenchmarkExperimentalChangeSource.BuiltIn,
            Folder = folder,
            InvalidReason = reason
        };

        string path = Path.Combine(folder, ChangeFileName);
        if (!File.Exists(path)) return Invalid($"No {ChangeFileName} in the folder.");
        try
        {
            file = JsonSerializer.Deserialize<BenchmarkExperimentalChangeFile>(File.ReadAllText(path));
        }
        catch (JsonException ex)
        {
            return Invalid($"{ChangeFileName} does not parse: {ex.Message}");
        }
        if (file == null) return Invalid($"{ChangeFileName} is empty.");
        if (!string.Equals(file.Id, folderName, StringComparison.Ordinal))
            return Invalid($"'id' is '{file.Id}', not the folder name '{folderName}'.");

        string? ReadText(string? fileName, string field, ref string? error)
        {
            if (fileName == null || error != null) return null;
            if (!IsFolderMarkdownFile(folder, fileName))
            {
                error = $"'{field}' names '{fileName}', which is not a Markdown file of the folder.";
                return null;
            }
            return BenchmarkExperimentalChangeHash.Normalize(File.ReadAllText(Path.Combine(folder, fileName))) ?? string.Empty;
        }

        string? fileError = null;
        string? toolDescription = ReadText(file.ExperimentalToolDescription, "experimentalToolDescription", ref fileError);
        var edits = new List<BenchmarkChangeDescriptionEdit>();
        foreach (var (tool, fileName) in (file.DescriptionAdditions ?? new()).OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            string? text = ReadText(fileName, "descriptionAdditions", ref fileError);
            edits.Add(new BenchmarkChangeDescriptionEdit(CanonicalToolName(tool), BenchmarkChangeDescriptionEditMode.Add, text ?? string.Empty));
        }
        foreach (var (tool, fileName) in (file.DescriptionReplacements ?? new()).OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            string? text = ReadText(fileName, "descriptionReplacements", ref fileError);
            edits.Add(new BenchmarkChangeDescriptionEdit(CanonicalToolName(tool), BenchmarkChangeDescriptionEditMode.Replace, text ?? string.Empty));
        }
        string? instructions = ReadText(file.Instructions, "instructions", ref fileError);
        if (fileError != null) return Invalid(fileError);

        var change = new BenchmarkExperimentalChange
        {
            Id = folderName,
            Title = file.Title?.Trim() ?? string.Empty,
            Summary = file.Summary?.Trim() ?? string.Empty,
            ExpectedEffect = string.IsNullOrWhiteSpace(file.ExpectedEffect) ? null : file.ExpectedEffect.Trim(),
            Source = BenchmarkExperimentalChangeSource.BuiltIn,
            ExperimentalTool = string.IsNullOrWhiteSpace(file.ExperimentalTool) ? null : CanonicalToolName(file.ExperimentalTool),
            ExperimentalToolDescription = toolDescription,
            Experiment = string.IsNullOrWhiteSpace(file.Experiment) ? null : file.Experiment.Trim(),
            DescriptionEdits = edits,
            Instructions = instructions,
            Folder = folder
        };
        change = change with { Hash = BenchmarkExperimentalChangeHash.Of(change) };

        string? reason = BenchmarkExperimentalChangeValidator.Validate(change, _handlers, AllowedTools);
        return reason == null ? change : change with { InvalidReason = reason };
    }

    /// <summary>A file name without <c>/</c>, <c>\</c> or <c>..</c>, ending <c>.md</c>, that exists in the folder.</summary>
    private static bool IsFolderMarkdownFile(string folder, string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName) || Path.IsPathRooted(fileName)
            || fileName.Contains('/') || fileName.Contains('\\') || fileName.Contains("..")
            || !fileName.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            return false;
        return File.Exists(Path.Combine(folder, fileName));
    }
}
