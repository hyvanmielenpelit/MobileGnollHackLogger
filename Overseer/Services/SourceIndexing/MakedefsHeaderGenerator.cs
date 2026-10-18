using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Overseer.Services.SourceIndexing;

/// <summary>What one makedefs check ended in.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum MakedefsOutcomeKind
{
    /// <summary>The inputs and the outputs match the stamp; no process was started.</summary>
    UpToDate,

    /// <summary><c>MakedefsBranch</c> names another branch than the one checked out.</summary>
    SkippedBranch,

    /// <summary><c>MakedefsBuildCommand</c> failed, so the executable was not run.</summary>
    BuildFailed,

    ExecutableMissing,

    /// <summary>A makedefs run failed or did not write its output; the stamp was left untouched.</summary>
    RunFailed,

    Generated
}

/// <summary>The result of <see cref="MakedefsHeaderGenerator.EnsureHeadersAsync"/>.</summary>
public sealed record MakedefsOutcome
{
    public MakedefsOutcomeKind Kind { get; init; }
    public string? Message { get; init; }
    public IReadOnlyList<string> OutputTail { get; init; } = Array.Empty<string>();

    /// <summary>A generated header's content differs from what it was before the run.</summary>
    public bool HeadersChanged { get; init; }

    /// <summary>The executable was run without a configured build command, so it may predate its sources.</summary>
    public bool UsedUnbuiltExecutable { get; init; }

    /// <summary>The build command was run by this check.</summary>
    public bool BuildRan { get; init; }

    public DateTime FinishedAtUtc { get; init; }
    public TimeSpan Duration { get; init; }

    /// <summary>The check left headers on disk that match makedefs' current inputs.</summary>
    public bool HeadersCurrent => Kind is MakedefsOutcomeKind.UpToDate or MakedefsOutcomeKind.Generated;
}

/// <summary>
/// The record of the last successful generation, kept beside the executable as
/// <c>overseer-makedefs-stamp.json</c>.
/// </summary>
public sealed class MakedefsStamp
{
    [JsonPropertyName("inputsSha256")]
    public string InputsSha256 { get; set; } = string.Empty;

    /// <summary>Generated header name to the SHA-256 of its content.</summary>
    [JsonPropertyName("outputs")]
    public Dictionary<string, string> Outputs { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonPropertyName("generatedAtUtc")]
    public DateTime GeneratedAtUtc { get; set; }

    [JsonPropertyName("headSha")]
    public string? HeadSha { get; set; }
}

/// <summary>The makedefs configuration and files as the dashboard shows them.</summary>
public sealed record MakedefsState
{
    public string BuildCommand { get; init; } = string.Empty;
    public bool BuildCommandToolExists { get; init; }
    public string ExecutablePath { get; init; } = string.Empty;
    public bool ExecutableExists { get; init; }
    public DateTime? ExecutableLastWriteUtc { get; init; }
    public string? BranchRestriction { get; init; }

    /// <summary>A <see cref="MakedefsInputsState"/> value.</summary>
    public string InputsState { get; init; } = MakedefsInputsState.Unknown;

    public DateTime? StampGeneratedAtUtc { get; init; }
    public MakedefsOutcome? LastOutcome { get; init; }
    public DateTime? LastBuildAtUtc { get; init; }
    public TimeSpan? LastBuildDuration { get; init; }
}

/// <summary>
/// Generates <c>pm.h</c>, <c>onames.h</c>, <c>animoff.h</c> and <c>animtotals.h</c> for one GnollHack
/// tree: builds makedefs with <c>MakedefsBuildCommand</c> and runs it only when the
/// <see cref="MakedefsInputSet"/> fingerprint differs from the stamp, or an output is missing or was
/// changed by something else.
/// </summary>
public sealed class MakedefsHeaderGenerator
{
    public const string StampFileName = "overseer-makedefs-stamp.json";
    public const int DefaultBuildTimeoutSeconds = 300;
    public const int DefaultRunTimeoutSeconds = 60;

    private static readonly JsonSerializerOptions StampJsonOptions = new() { WriteIndented = true };

    private readonly string _sourceCodePath;
    private readonly IConfiguration _configuration;
    private readonly IExternalProcessRunner _runner;
    private readonly ILogger _logger;
    private readonly object _stateLock = new();
    private MakedefsOutcome? _lastOutcome;
    private DateTime? _lastBuildAtUtc;
    private TimeSpan? _lastBuildDuration;

    public MakedefsHeaderGenerator(string sourceCodePath, IConfiguration configuration, IExternalProcessRunner runner, ILogger logger)
    {
        _sourceCodePath = sourceCodePath ?? string.Empty;
        _configuration = configuration;
        _runner = runner;
        _logger = logger;
    }

    public string BuildCommand => _configuration["MakedefsBuildCommand"]?.Trim() ?? string.Empty;

    public string? BranchRestriction
    {
        get
        {
            string? branch = _configuration["MakedefsBranch"];
            return string.IsNullOrWhiteSpace(branch) ? null : branch.Trim();
        }
    }

    /// <summary><c>MakedefsExecutablePath</c>, else the <c>ToolsDir</c> output of <c>win/win32/vs/dirs.props</c>.</summary>
    public string ExecutablePath
    {
        get
        {
            string? configured = _configuration["MakedefsExecutablePath"];
            return string.IsNullOrWhiteSpace(configured)
                ? Path.Combine(_sourceCodePath, "tools", "Release", "x64", "makedefs.exe")
                : configured.Trim();
        }
    }

    public string StampPath => Path.Combine(Path.GetDirectoryName(ExecutablePath) ?? string.Empty, StampFileName);

    public TimeSpan BuildTimeout => TimeSpan.FromSeconds(ReadPositiveInt("MakedefsBuildTimeoutSeconds", DefaultBuildTimeoutSeconds));

    public TimeSpan RunTimeout => TimeSpan.FromSeconds(ReadPositiveInt("MakedefsRunTimeoutSeconds", DefaultRunTimeoutSeconds));

    /// <summary>The outcome of the last check; null until one has run.</summary>
    public MakedefsOutcome? LastOutcome
    {
        get { lock (_stateLock) return _lastOutcome; }
    }

    /// <summary>
    /// The program a command line starts: the text inside a leading pair of double quotes, else the
    /// text up to the first whitespace. Null for an empty command.
    /// </summary>
    public static string? CommandTool(string? command)
    {
        string trimmed = command?.Trim() ?? string.Empty;
        if (trimmed.Length == 0) return null;

        if (trimmed[0] == '"')
        {
            int close = trimmed.IndexOf('"', 1);
            return close > 1 ? trimmed.Substring(1, close - 1) : trimmed.Trim('"');
        }

        int space = trimmed.IndexOfAny(new[] { ' ', '\t' });
        return space < 0 ? trimmed : trimmed.Substring(0, space);
    }

    /// <summary>Whether the program the build command starts exists as a file, as given or under the source root.</summary>
    public bool BuildCommandToolExists()
    {
        string? tool = CommandTool(BuildCommand);
        if (string.IsNullOrEmpty(tool)) return false;
        try
        {
            return File.Exists(tool) || (!Path.IsPathRooted(tool) && File.Exists(Path.Combine(_sourceCodePath, tool)));
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <summary>
    /// Brings the generated headers up to date: does nothing when they already are (unless
    /// <paramref name="force"/>), otherwise builds makedefs when a build command is configured and
    /// runs <c>-o</c> and <c>-p</c> in <c>util/</c> and <c>-a</c> in <c>dat/</c>. A failure is
    /// returned as an outcome; only cancellation throws.
    /// </summary>
    public async Task<MakedefsOutcome> EnsureHeadersAsync(bool force, IProgress<SourceIndexProgress>? progress, CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();
        var tail = new List<string>();

        MakedefsOutcome Finish(MakedefsOutcomeKind kind, string message, bool headersChanged = false, bool usedUnbuilt = false, bool buildRan = false)
        {
            var outcome = new MakedefsOutcome
            {
                Kind = kind,
                Message = message,
                OutputTail = tail.Count > ExternalProcessRunner.MaxTailLines
                    ? tail.Skip(tail.Count - ExternalProcessRunner.MaxTailLines).ToArray()
                    : tail.ToArray(),
                HeadersChanged = headersChanged,
                UsedUnbuiltExecutable = usedUnbuilt,
                BuildRan = buildRan,
                FinishedAtUtc = DateTime.UtcNow,
                Duration = stopwatch.Elapsed
            };
            lock (_stateLock) _lastOutcome = outcome;

            if (kind is MakedefsOutcomeKind.BuildFailed or MakedefsOutcomeKind.RunFailed or MakedefsOutcomeKind.ExecutableMissing)
            {
                _logger.LogWarning("makedefs: {Kind}. {Message}", kind, message);
            }
            else
            {
                _logger.LogInformation("makedefs: {Kind}. {Message}", kind, message);
            }
            return outcome;
        }

        string? allowedBranch = BranchRestriction;
        if (allowedBranch != null)
        {
            string currentBranch = GitHelper.GetCurrentBranch(_sourceCodePath);
            if (!string.Equals(currentBranch, allowedBranch, StringComparison.OrdinalIgnoreCase))
            {
                return Finish(MakedefsOutcomeKind.SkippedBranch,
                    $"The checked-out branch '{currentBranch}' is not MakedefsBranch '{allowedBranch}'; the headers on disk are used as they are.");
            }
        }

        string buildCommand = BuildCommand;
        var fingerprint = MakedefsInputSet.Compute(_sourceCodePath, buildCommand);
        var stamp = ReadStamp();
        var outputsBefore = HashOutputs();

        if (!force && IsUpToDate(stamp, fingerprint, outputsBefore))
        {
            return Finish(MakedefsOutcomeKind.UpToDate, "The makedefs inputs and the generated headers match the stamp.");
        }

        ct.ThrowIfCancellationRequested();

        bool buildRan = false;
        if (buildCommand.Length > 0)
        {
            progress?.Report(new SourceIndexProgress(SourceIndexPhase.BuildingMakedefs));
            var buildStarted = DateTime.UtcNow;
            var build = await _runner.RunAsync(buildCommand, _sourceCodePath, BuildTimeout, null, ct).ConfigureAwait(false);
            buildRan = true;
            lock (_stateLock)
            {
                _lastBuildAtUtc = buildStarted;
                _lastBuildDuration = build.Duration;
            }
            tail.AddRange(build.OutputTail);
            if (build.Canceled) ct.ThrowIfCancellationRequested();

            if (!build.Succeeded)
            {
                /* A failed build never runs a possibly stale executable. */
                return Finish(MakedefsOutcomeKind.BuildFailed, "The makedefs build " + Describe(build, BuildTimeout) + ".", buildRan: true);
            }
        }

        string executable = ExecutablePath;
        if (!File.Exists(executable))
        {
            return Finish(MakedefsOutcomeKind.ExecutableMissing, $"The makedefs executable was not found at {executable}.", buildRan: buildRan);
        }

        bool usedUnbuilt = buildCommand.Length == 0;
        progress?.Report(new SourceIndexProgress(SourceIndexPhase.GeneratingHeaders));

        /* Working directories match aftermakedefs.proj: -o/-p from util/, -a from dat/. All use
           INCLUDE_TEMPLATE ("../include/%s"), so the output goes to include/ either way. */
        var runs = new (string Argument, string Directory)[]
        {
            ("-o", Path.Combine(_sourceCodePath, "util")),
            ("-p", Path.Combine(_sourceCodePath, "util")),
            ("-a", Path.Combine(_sourceCodePath, "dat"))
        };

        var failures = new List<string>();
        foreach (var (argument, directory) in runs)
        {
            var run = await _runner.RunAsync($"\"{executable}\" {argument}", directory, RunTimeout, null, ct).ConfigureAwait(false);
            tail.AddRange(run.OutputTail);
            if (run.Canceled) ct.ThrowIfCancellationRequested();
            if (!run.Succeeded) failures.Add($"makedefs {argument} " + Describe(run, RunTimeout));
        }

        if (failures.Count > 0)
        {
            return Finish(MakedefsOutcomeKind.RunFailed, string.Join("; ", failures) + ". The stamp was not updated, so the next pass retries.",
                usedUnbuilt: usedUnbuilt, buildRan: buildRan);
        }

        var outputsAfter = HashOutputs();
        var notWritten = outputsAfter.Where(o => o.Value == null).Select(o => o.Key).ToList();
        if (notWritten.Count > 0)
        {
            return Finish(MakedefsOutcomeKind.RunFailed, $"makedefs exited 0 but did not write {string.Join(", ", notWritten)}.",
                usedUnbuilt: usedUnbuilt, buildRan: buildRan);
        }

        bool headersChanged = MakedefsInputSet.GeneratedHeaders.Any(name =>
            !string.Equals(outputsBefore[name], outputsAfter[name], StringComparison.OrdinalIgnoreCase));

        string stampNote = string.Empty;
        try
        {
            WriteStamp(new MakedefsStamp
            {
                InputsSha256 = fingerprint.Sha256,
                Outputs = outputsAfter.ToDictionary(o => o.Key, o => o.Value!, StringComparer.OrdinalIgnoreCase),
                GeneratedAtUtc = DateTime.UtcNow,
                HeadSha = GitHelper.GetGitHeadSha(_sourceCodePath)
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not write the makedefs stamp at {Path}.", StampPath);
            stampNote = " The stamp could not be written, so the next pass regenerates again.";
        }

        string message = (headersChanged ? "Generated the headers; at least one changed." : "Generated the headers; none changed.")
            + (usedUnbuilt ? " MakedefsBuildCommand is not set, so the executable may be older than its sources." : string.Empty)
            + stampNote;
        return Finish(MakedefsOutcomeKind.Generated, message, headersChanged, usedUnbuilt, buildRan);
    }

    /// <summary>The configuration, the executable, the stamp and the last outcome, for the dashboard.</summary>
    public MakedefsState GetState()
    {
        string executable = ExecutablePath;
        bool executableExists = false;
        DateTime? executableLastWrite = null;
        try
        {
            executableExists = File.Exists(executable);
            if (executableExists) executableLastWrite = File.GetLastWriteTimeUtc(executable);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            executableExists = false;
        }

        var stamp = ReadStamp();
        string inputsState = MakedefsInputsState.Unknown;
        try
        {
            if (!string.IsNullOrWhiteSpace(_sourceCodePath) && Directory.Exists(_sourceCodePath))
            {
                var fingerprint = MakedefsInputSet.Compute(_sourceCodePath, BuildCommand);
                inputsState = IsUpToDate(stamp, fingerprint, HashOutputs()) ? MakedefsInputsState.UpToDate : MakedefsInputsState.Stale;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            inputsState = MakedefsInputsState.Unknown;
        }

        lock (_stateLock)
        {
            return new MakedefsState
            {
                BuildCommand = BuildCommand,
                BuildCommandToolExists = BuildCommandToolExists(),
                ExecutablePath = executable,
                ExecutableExists = executableExists,
                ExecutableLastWriteUtc = executableLastWrite,
                BranchRestriction = BranchRestriction,
                InputsState = inputsState,
                StampGeneratedAtUtc = stamp?.GeneratedAtUtc,
                LastOutcome = _lastOutcome,
                LastBuildAtUtc = _lastBuildAtUtc,
                LastBuildDuration = _lastBuildDuration
            };
        }
    }

    /// <summary>The stamp beside the executable; null when it is missing or unreadable.</summary>
    public MakedefsStamp? ReadStamp()
    {
        try
        {
            string path = StampPath;
            if (!File.Exists(path)) return null;
            var stamp = JsonSerializer.Deserialize<MakedefsStamp>(File.ReadAllText(path), StampJsonOptions);
            if (stamp == null || string.IsNullOrEmpty(stamp.InputsSha256)) return null;
            stamp.Outputs = new Dictionary<string, string>(stamp.Outputs ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase);
            return stamp;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException or NotSupportedException)
        {
            _logger.LogWarning(ex, "The makedefs stamp at {Path} could not be read; treating the headers as stale.", StampPath);
            return null;
        }
    }

    /// <summary>Writes the stamp to a temporary file and moves it over the old one.</summary>
    internal void WriteStamp(MakedefsStamp stamp)
    {
        string path = StampPath;
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        string temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(stamp, StampJsonOptions));
        File.Move(temporary, path, overwrite: true);
    }

    private static bool IsUpToDate(MakedefsStamp? stamp, MakedefsFingerprint fingerprint, IReadOnlyDictionary<string, string?> outputs)
    {
        if (stamp == null) return false;
        if (!string.Equals(stamp.InputsSha256, fingerprint.Sha256, StringComparison.OrdinalIgnoreCase)) return false;

        foreach (string name in MakedefsInputSet.GeneratedHeaders)
        {
            string? current = outputs[name];
            if (current == null) return false;
            if (!stamp.Outputs.TryGetValue(name, out var recorded)) return false;
            if (!string.Equals(recorded, current, StringComparison.OrdinalIgnoreCase)) return false;
        }
        return true;
    }

    /// <summary>Each generated header's SHA-256, or null when it does not exist.</summary>
    private Dictionary<string, string?> HashOutputs()
    {
        var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (string name in MakedefsInputSet.GeneratedHeaders)
        {
            string path = Path.Combine(_sourceCodePath, "include", name);
            try
            {
                result[name] = File.Exists(path)
                    ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant()
                    : null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                result[name] = null;
            }
        }
        return result;
    }

    private static string Describe(ProcessRunResult result, TimeSpan timeout)
    {
        if (result.TimedOut) return $"timed out after {timeout.TotalSeconds:0} s";
        if (result.Canceled) return "was canceled";
        if (result.ExitCode == null)
        {
            return "could not be started" + (result.OutputTail.Count > 0 ? ": " + result.OutputTail[^1] : string.Empty);
        }
        return $"failed with exit code {result.ExitCode}";
    }

    private int ReadPositiveInt(string key, int fallback)
        => int.TryParse(_configuration[key], out int value) && value > 0 ? value : fallback;
}
